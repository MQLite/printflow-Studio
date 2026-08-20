using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Navigation;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels;

/// <summary>One step of the open session, flattened for display.</summary>
public sealed class SessionStepRow
{
    internal SessionStepRow(SessionStep step, bool isCurrent)
    {
        ArgumentNullException.ThrowIfNull(step);

        Ordinal = step.Ordinal + 1;
        Name = DisplayNames.Step(step.Step);
        State = DisplayNames.StepState(step.State);
        IsCurrent = isCurrent;
    }

    public int Ordinal { get; }

    public string Name { get; }

    public string State { get; }

    /// <summary>Whether this is the step the operator is expected to act on.</summary>
    public bool IsCurrent { get; }
}

/// <summary>
/// One quick rejection reason, offered in the review panel (MVP design §7.3).
/// </summary>
/// <remarks>
/// <see cref="Reason"/> is the stable enum value that is persisted; <see cref="Label"/> is the
/// only part an operator reads.
/// </remarks>
public sealed class RejectionReasonChoice
{
    internal RejectionReasonChoice(RejectionReason reason)
    {
        Reason = reason;
        Label = DisplayNames.RejectionReason(reason);
    }

    /// <summary>The persisted value. Never displayed.</summary>
    public RejectionReason Reason { get; }

    /// <summary>The localised operator label.</summary>
    public string Label { get; }
}

/// <summary>
/// One white-underbase branch offered to the operator (Epic 11100 Part 3C3B §6, §7).
/// </summary>
/// <remarks>
/// There are exactly three, they are presented in enum order, and none of them is marked,
/// sorted or styled as preferable. <see cref="Label"/> carries the classification guidance so
/// the operator has something to classify against; the choice itself stays theirs
/// (MVP design §12).
/// </remarks>
public sealed class WhiteUnderbaseChoice
{
    internal WhiteUnderbaseChoice(WhiteUnderbaseBranch branch)
    {
        Branch = branch;
        Label = DisplayNames.WhiteUnderbaseBranch(branch);
    }

    /// <summary>The persisted value. Never displayed.</summary>
    public WhiteUnderbaseBranch Branch { get; }

    /// <summary>The localised label, including the operator guidance for this branch.</summary>
    public string Label { get; }
}

/// <summary>
/// One size shortcut, offered beside the millimetre boxes (Epic 11100 Part 3C3B §5).
/// </summary>
/// <remarks>
/// A shortcut and nothing more: pressing it types the preset's nominal millimetres into the
/// boxes, which the operator can still change before confirming. It confirms nothing, resizes
/// nothing, and is not a size editor.
/// </remarks>
public sealed class SizePresetChoice
{
    internal SizePresetChoice(SizePreset preset, double widthMm, double heightMm)
    {
        Preset = preset;
        WidthMm = widthMm;
        HeightMm = heightMm;
        Label = DisplayNames.SizePreset(preset);
    }

    public SizePreset Preset { get; }

    public double WidthMm { get; }

    public double HeightMm { get; }

    public string Label { get; }
}

/// <summary>
/// One production output the session already holds, flattened for the list
/// (Epic 11100 Part 3C3B §15).
/// </summary>
/// <remarks>
/// Exists so the operator can see Output A is still there while Output B is being made. Every
/// value is a label built from the <see cref="PrintOutputView"/> the service returned; nothing
/// here reads a file, and no path is shown.
/// </remarks>
public sealed class PrintOutputRow
{
    internal PrintOutputRow(PrintOutputView output)
    {
        ArgumentNullException.ThrowIfNull(output);

        FileName = output.FileName;
        Size = string.Format(
            CultureInfo.CurrentCulture,
            Strings.Session_DimensionsSummary,
            output.Dimensions.WidthMm,
            output.Dimensions.HeightMm,
            output.Dimensions.PixelWidth,
            output.Dimensions.PixelHeight,
            output.Dimensions.Dpi);
        Branch = DisplayNames.WhiteUnderbaseBranch(output.Branch);
        Review = DisplayNames.ReviewState(output.ReviewState);
        IsValid = output.IsValid;
        Validity = output.IsValid ? Strings.Session_OutputValid : Strings.Session_OutputInvalid;
    }

    /// <summary>The workspace file name. Never a path.</summary>
    public string FileName { get; }

    public string Size { get; }

    public string Branch { get; }

    public string Review { get; }

    public string Validity { get; }

    /// <summary>Drives the emphasis on an invalidated row; the text says so as well.</summary>
    public bool IsValid { get; }
}

/// <summary>
/// The session processing screen: what the session is, what file it is holding, and the
/// actions the workflow currently permits (Epic 11100 Part 3C3A §3–§16, Part 3C3B §3–§15).
/// </summary>
/// <remarks>
/// Every action goes through <see cref="ISessionService.ExecuteAsync"/> and nothing else. This
/// file performs no file-system access, issues no SQL, references no adapter, and assigns no
/// step state — the closest it comes to a workflow rule is reading
/// <see cref="SessionView.AvailableCommands"/>, which is the engine's own answer rather than a
/// second copy of it (MVP design invariant 12, Part 3C3A §4, §18).
/// <para>
/// After every command the screen shows the <see cref="SessionView"/> the service returned,
/// which is reconstructed from what was actually persisted. There is no local "what I think
/// happened" state to drift out of step with the database.
/// </para>
/// <para>
/// The production decisions this screen carries — the print size and the W1 branch — are
/// operator input, and both are held here only as unconfirmed text or an unconfirmed selection
/// until a command persists them. Nothing derives a size from the image, nothing infers a
/// branch, and nothing pre-selects one. The pixel figures shown come from
/// <see cref="PrintDimensions"/> itself rather than from arithmetic repeated here
/// (Part 3C3B §4, §7).
/// </para>
/// </remarks>
public sealed partial class SessionViewModel : ObservableObject
{
    /// <summary>The smallest and largest zoom the review surface offers (Part C1 §12).</summary>
    /// <remarks>
    /// 10% makes a production-sized design fit at a glance; 800% is well past the point where a
    /// deterministic trim's edge can be judged. Neither bound alters a Revision — zoom is a
    /// property of looking, not of the file.
    /// </remarks>
    public const double MinimumZoom = 0.10;

    /// <inheritdoc cref="MinimumZoom" />
    public const double MaximumZoom = 8.00;

    /// <summary>What one press of Zoom In or Zoom Out multiplies or divides the scale by.</summary>
    private const double ZoomStep = 1.25;

    private readonly ISessionService _sessions;
    private readonly IArtefactPreviewService _previews;
    private readonly INavigationService _navigation;

    /// <summary>
    /// The reason recorded when the operator hands a session over from this screen.
    /// </summary>
    /// <remarks>
    /// Stable English, not a resource, for the same reason
    /// <see cref="WorkflowCommand.Skip.DefaultReason"/> is: it is persisted as audit history,
    /// and a record whose text changes with the workstation's language would be a poor audit
    /// trail (MVP design §13.4).
    /// </remarks>
    private const string HandedOffFromSessionReason =
        "Handed off to the operator from the session screen.";

    [ObservableProperty]
    private string? _notice;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>The quick reason sent with a rejection. Never null once the list is built.</summary>
    [ObservableProperty]
    private RejectionReasonChoice _selectedRejectionReason;

    [ObservableProperty]
    private string? _rejectionNotes;

    /// <summary>Unconfirmed operator input. Means nothing until a command accepts it.</summary>
    [ObservableProperty]
    private string? _widthMmText;

    /// <summary>Unconfirmed operator input. Means nothing until a command accepts it.</summary>
    [ObservableProperty]
    private string? _heightMmText;

    /// <summary>
    /// The branch the operator has picked but not yet confirmed.
    /// </summary>
    /// <remarks>
    /// Starts null and is never assigned a starting value anywhere in this file. That null is
    /// the point: a pre-selected branch would be a default by another name, and the design
    /// forbids one (MVP design §12, Part 3C3B §6).
    /// </remarks>
    [ObservableProperty]
    private WhiteUnderbaseChoice? _selectedWhiteUnderbaseChoice;

    /// <summary>
    /// Which preset, if any, the pending millimetres came from.
    /// </summary>
    /// <remarks>
    /// Reverts to <see cref="SizePreset.Custom"/> the moment either box is edited, so a size
    /// the operator typed is never recorded as having come from a preset.
    /// </remarks>
    private SizePreset _pendingPreset = SizePreset.Custom;

    /// <summary>
    /// Whether the whole image is fitted to its viewport (Part C1 §15).
    /// </summary>
    /// <remarks>
    /// Starts true and returns to true on reset, because the first thing a reviewer needs is
    /// the complete result — opening at pixel-level zoom would show a corner of a design and
    /// call it a review.
    /// </remarks>
    [ObservableProperty]
    private bool _isFitToViewport = true;

    /// <summary>
    /// The zoom multiplier applied when not fitted; 1.0 is one image pixel per screen pixel.
    /// </summary>
    /// <remarks>
    /// Shared by both halves of a comparison on purpose (§14): a before and an after examined
    /// at different magnifications are not a comparison. Scroll position is deliberately
    /// <i>not</i> shared — each pane keeps its own, which is what lets an operator look at the
    /// top-left of one and the bottom-right of the other.
    /// </remarks>
    [ObservableProperty]
    private double _zoomScale = 1.0;

    private SessionView? _session;

    public SessionViewModel(
        ISessionService sessions, IArtefactPreviewService previews, INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(previews);
        ArgumentNullException.ThrowIfNull(navigation);

        _sessions = sessions;
        _previews = previews;
        _navigation = navigation;

        RejectionReasons = new ReadOnlyCollection<RejectionReasonChoice>(
            Enum.GetValues<RejectionReason>().Select(reason => new RejectionReasonChoice(reason)).ToList());
        _selectedRejectionReason = RejectionReasons[0];

        WhiteUnderbaseChoices = new ReadOnlyCollection<WhiteUnderbaseChoice>(
            Enum.GetValues<WhiteUnderbaseBranch>().Select(branch => new WhiteUnderbaseChoice(branch)).ToList());

        // Only the presets that have a nominal size; Custom is what typing produces.
        SizePresets = new ReadOnlyCollection<SizePresetChoice>(
        [
            .. Enum.GetValues<SizePreset>()
                .Select(preset => (Preset: preset, Nominal: PrintDimensions.NominalMillimetres(preset)))
                .Where(candidate => candidate.Nominal is not null)
                .Select(candidate => new SizePresetChoice(
                    candidate.Preset, candidate.Nominal!.Value.WidthMm, candidate.Nominal.Value.HeightMm)),
        ]);
    }

    /// <summary>The open session's steps, in workflow order.</summary>
    public ObservableCollection<SessionStepRow> Steps { get; } = [];

    /// <summary>The production outputs this session already holds, oldest first (§15).</summary>
    public ObservableCollection<PrintOutputRow> Outputs { get; } = [];

    /// <summary>
    /// What the operator is looking at: nothing, one image, or Before then After
    /// (Epic 11200 Part C1 §7, §11).
    /// </summary>
    /// <remarks>
    /// Order is the label's partner, not a substitute for it: the upstream pane is always first
    /// and always headed "Before", so a side-by-side layout reads left-to-right the way the
    /// work happened. A step with no result of its own contributes one pane, and a
    /// <c>ManualCropRequired</c> outcome contributes exactly the upstream one — there is no
    /// path here that manufactures an "after" for a result that was never produced (§17).
    /// </remarks>
    public ObservableCollection<ArtefactPreviewPane> PreviewPanes { get; } = [];

    /// <summary>Every quick rejection reason, in enum order.</summary>
    public IReadOnlyList<RejectionReasonChoice> RejectionReasons { get; }

    /// <summary>Every white-underbase branch, in enum order and with none preferred (§6).</summary>
    public IReadOnlyList<WhiteUnderbaseChoice> WhiteUnderbaseChoices { get; }

    /// <summary>The named size shortcuts (§5).</summary>
    public IReadOnlyList<SizePresetChoice> SizePresets { get; }

    // --- Labels --------------------------------------------------------------------------

    public string BackLabel => Strings.Nav_BackToHome;

    public string StepsHeading => Strings.Session_StepsHeading;

    public string PlaceholderNotice => Strings.Session_PlaceholderNotice;

    public string ConfirmOriginalLabel => Strings.Session_ConfirmOriginal;

    public string RunStepLabel => Strings.Session_RunStep;

    public string ApproveLabel => Strings.Session_Approve;

    public string RejectLabel => Strings.Session_Reject;

    public string RetryLabel => Strings.Session_Retry;

    public string SkipLabel => Strings.Session_Skip;

    public string HandOffLabel => Strings.Session_HandOff;

    public string ReviewHeading => Strings.Session_ReviewHeading;

    public string RejectReasonLabel => Strings.Session_RejectReasonLabel;

    public string RejectNotesLabel => Strings.Session_RejectNotesLabel;

    public string ArtefactHeading => Strings.Session_ArtefactHeading;

    public string ArtefactNoneText => Strings.Session_ArtefactNone;

    public string ArtefactIsInputText => Strings.Session_ArtefactIsInput;

    public string FileNameLabel => Strings.Session_LabelFileName;

    public string FormatLabel => Strings.Session_LabelFormat;

    public string PixelsLabel => Strings.Session_LabelPixels;

    public string DpiLabel => Strings.Session_LabelDpi;

    public string HashLabel => Strings.Session_LabelHash;

    public string RevisionLabel => Strings.Session_LabelRevision;

    public string DimensionsHeading => Strings.Session_DimensionsHeading;

    public string DimensionsHint => Strings.Session_DimensionsHint;

    public string WidthMmLabel => Strings.Session_LabelWidthMm;

    public string HeightMmLabel => Strings.Session_LabelHeightMm;

    public string ConfirmDimensionsLabel => Strings.Session_DimensionsConfirm;

    public string PresetsLabel => Strings.Session_PresetsLabel;

    public string PresetHint => Strings.Session_PresetHint;

    public string WhiteUnderbaseHeading => Strings.Session_W1Heading;

    /// <summary>Classification guidance. Advice to the operator, never a decision (§7).</summary>
    public string WhiteUnderbaseHint => Strings.Session_W1Hint;

    public string ConfirmWhiteUnderbaseLabel => Strings.Session_W1Confirm;

    public string CompleteLabel => Strings.Session_Complete;

    public string AddAnotherSizeLabel => Strings.Session_AddAnotherSize;

    public string OutputsHeading => Strings.Session_OutputsHeading;

    public string BranchLabel => Strings.Session_LabelBranch;

    public string ReviewStateLabel => Strings.Session_LabelReview;

    // --- Image preview (Epic 11200 Part C1) ----------------------------------------------

    public string PreviewHeading => Strings.Session_PreviewHeading;

    /// <summary>The heading given to the upstream half of a comparison (§11).</summary>
    public string BeforeLabel => Strings.Session_PreviewBefore;

    /// <summary>The heading given to the step-result half of a comparison (§11).</summary>
    public string AfterLabel => Strings.Session_PreviewAfter;

    /// <summary>The heading given to a lone preview, where there is nothing to compare.</summary>
    public string SinglePreviewLabel => Strings.Session_PreviewCurrent;

    /// <summary>The zoom read-out while the whole image is fitted (§15).</summary>
    public string FitLabel => Strings.Session_ZoomFit;

    public string ZoomInLabel => Strings.Session_ZoomIn;

    public string ZoomOutLabel => Strings.Session_ZoomOut;

    public string ResetZoomLabel => Strings.Session_ZoomReset;

    /// <summary>Whether there is anything at all to show in the preview area.</summary>
    public bool HasPreview => PreviewPanes.Count > 0;

    /// <summary>The current magnification, or the word "Fit" while the whole image is shown.</summary>
    public string ZoomLabel => IsFitToViewport
        ? FitLabel
        : string.Format(CultureInfo.CurrentCulture, Strings.Session_ZoomPercent, Math.Round(ZoomScale * 100));

    /// <summary>True while a further step in is within <see cref="MaximumZoom"/>.</summary>
    public bool CanZoomIn => EffectiveZoom * ZoomStep <= MaximumZoom + ZoomTolerance;

    /// <summary>True while a further step out is within <see cref="MinimumZoom"/>.</summary>
    public bool CanZoomOut => EffectiveZoom / ZoomStep >= MinimumZoom - ZoomTolerance;

    /// <summary>
    /// The stable-English sentence saying a trim needs a human (Part C1 §17).
    /// </summary>
    /// <remarks>
    /// Shown beside the ordinary failure notice rather than instead of it. The failure line
    /// carries the code a support call quotes; this one says what the operator does next, and
    /// says plainly that the tool is not here yet rather than implying a button they cannot
    /// find.
    /// </remarks>
    public string ManualCropNotice => Strings.Session_ManualCropRequiredNotice;

    /// <summary>
    /// Whether the current step ended in <see cref="FailureCode.ManualCropRequired"/>.
    /// </summary>
    /// <remarks>
    /// Read from <see cref="SessionView.CurrentStepFailure"/>, which is derived from the
    /// persisted attempt row, so the notice survives navigating away and resuming — a state
    /// this screen remembered in a field would not (§17, §19).
    /// </remarks>
    public bool IsManualCropRequired => _session?.CurrentStepFailure == FailureCode.ManualCropRequired;

    /// <summary>
    /// The unmissable warning that this installation produces synthetic results
    /// (Part 3C3A §8).
    /// </summary>
    /// <remarks>
    /// Whether to show it is read from <see cref="SessionView.ProcessingMode"/>, which the
    /// service derives from the adapters actually wired up. A view model that guessed from
    /// configuration could disagree with what really ran.
    /// </remarks>
    public string FakeModeNotice => Strings.Session_FakeModeNotice;

    public bool IsFakeProcessing => _session?.IsFakeProcessing == true;

    /// <summary>
    /// The stronger warning shown when a synthetic <b>production TIFF</b> is involved
    /// (Part 3C3B §10).
    /// </summary>
    /// <remarks>
    /// A generated PNG that is not really enhanced is obviously not finished work. A file
    /// called <c>..._CMYK_W.tif</c> looks exactly like something that could be sent to the
    /// printer, so it gets its own sentence saying what it is not: no CMYK conversion, no W1
    /// spot channel, no Photoshop Action, nothing prepared for Maintop. The warning claims
    /// none of those were done — it never claims any of them were.
    /// </remarks>
    public string FakeTiffNotice => Strings.Session_FakeTiffNotice;

    /// <summary>
    /// Whether the synthetic-TIFF warning applies.
    /// </summary>
    /// <remarks>
    /// <see cref="SessionView.ProducesPrintOutput"/> is the workflow layer's answer to "does
    /// this workflow end in a TIFF", so the screen does not compare step kinds to work it out.
    /// It stays visible after completion, when the finished — still synthetic — output is what
    /// the operator is looking at.
    /// </remarks>
    public bool IsFakeTiffOutput => IsFakeProcessing && _session?.ProducesPrintOutput == true;

    // --- Session identity ----------------------------------------------------------------

    /// <summary>The output name of the open session.</summary>
    public string SessionName => _session?.OutputName.Value ?? string.Empty;

    /// <summary>The localised workflow of the open session.</summary>
    public string Workflow => _session is null ? string.Empty : DisplayNames.Workflow(_session.WorkflowType);

    /// <summary>The localised session state of the open session.</summary>
    public string State => _session is null ? string.Empty : DisplayNames.SessionState(_session.State);

    /// <summary>The localised step the session is waiting on, or a "finished" line.</summary>
    public string CurrentStep => _session?.CurrentStep is { } step
        ? DisplayNames.Step(step.Step)
        : Strings.Session_AllStepsFinished;

    /// <summary>
    /// True for a completed, handed-off or abandoned session, which is a record to read rather
    /// than work to continue (Part 3C2 §11).
    /// </summary>
    public bool IsReadOnly => _session?.CanContinueProcessing != true;

    /// <summary>
    /// True once automation has ended for this session (Part 3C3A §14).
    /// </summary>
    /// <remarks>
    /// It drives a sentence, not a workflow: nothing here resumes automation, watches a folder
    /// or launches an application. The operator continues in whatever tool they choose.
    /// </remarks>
    public bool IsHandedOff => _session?.State == SessionState.HandedOff;

    public string HandedOffNotice => Strings.Session_HandedOffNotice;

    // --- Current artefact ----------------------------------------------------------------

    public bool HasArtefact => _session?.CurrentArtefact is not null;

    /// <summary>True when the file shown is the step's input rather than its result.</summary>
    public bool ArtefactIsInput => _session?.CurrentArtefact is { IsCurrentStepResult: false };

    public string ArtefactFileName => _session?.CurrentArtefact?.FileName ?? string.Empty;

    public string ArtefactFormat => _session?.CurrentArtefact is { } artefact
        ? DisplayNames.ImageFormat(artefact.Facts.Format)
        : string.Empty;

    /// <summary>Pixel dimensions, or a plain "not determined" for a PSD/PDF import.</summary>
    public string ArtefactPixels => _session?.CurrentArtefact?.Facts is { HasPixelDimensions: true } facts
        ? string.Create(CultureInfo.CurrentCulture, $"{facts.PixelWidth} x {facts.PixelHeight}")
        : Strings.Session_ValueUnknown;

    public string ArtefactDpi => _session?.CurrentArtefact?.Facts is { DpiX: > 0, DpiY: > 0 } facts
        ? string.Create(CultureInfo.CurrentCulture, $"{facts.DpiX:0.##} x {facts.DpiY:0.##}")
        : Strings.Session_ValueUnknown;

    /// <summary>The first 12 hex characters of the hash. Never used for comparison.</summary>
    public string ArtefactHash => _session?.CurrentArtefact?.Sha256.ShortForm ?? string.Empty;

    /// <summary>A short revision identifier, enough to tell two results apart on screen.</summary>
    public string ArtefactRevision => _session?.CurrentArtefact is { } artefact
        ? artefact.RevisionId.Value.ToString("N", CultureInfo.InvariantCulture)[..8]
        : string.Empty;

    // --- Command availability ------------------------------------------------------------
    //
    // Every one of these is a reading of the engine's own AvailableCommands. There is no
    // "if the step is ReviewRequired then Approve" anywhere in this file: that rule lives in
    // WorkflowEngine, and restating it here would create a second copy that could disagree
    // with the one that actually accepts the click (Part 3C3A §4).

    public bool CanConfirmOriginal => Allows(CommandKind.ConfirmOriginal);

    public bool CanRunStep => Allows(CommandKind.StartStep);

    public bool CanApprove => Allows(CommandKind.Approve);

    public bool CanReject => Allows(CommandKind.Reject);

    public bool CanRetry => Allows(CommandKind.Retry);

    public bool CanSkip => Allows(CommandKind.Skip);

    public bool CanHandOff => Allows(CommandKind.HandOff);

    /// <summary>
    /// Whether the dimensions panel is shown (§3).
    /// </summary>
    /// <remarks>
    /// "Is the session on the PrintDimensions step" is not restated here: the engine reports
    /// <c>SetPrintDimensions</c> as available exactly when it would accept one, which is the
    /// same question and one fewer place to get it wrong. It is what reopens the panel after
    /// AddAnotherSize as well, with no second rule about reopening.
    /// </remarks>
    public bool CanSetDimensions => Allows(CommandKind.SetPrintDimensions);

    /// <summary>
    /// Whether the W1 selector is shown (§6).
    /// </summary>
    /// <remarks>
    /// Available for the whole active life of a TIFF-producing session, because that is when
    /// the engine will accept the decision — including before the operator has reached the
    /// Photoshop step, and again for each new size. Photoshop output still refuses to start
    /// until a branch has actually been recorded; showing the selector is not the same as
    /// having chosen.
    /// </remarks>
    public bool CanSelectWhiteUnderbase => Allows(CommandKind.SelectWhiteUnderbaseBranch);

    public bool CanComplete => Allows(CommandKind.Complete);

    public bool CanAddAnotherSize => Allows(CommandKind.AddAnotherSize);

    /// <summary>Whether the confirm button under the W1 selector does anything yet.</summary>
    /// <remarks>
    /// Only about this screen's own input being complete — whether a branch has been picked at
    /// all. Legality remains <see cref="CanSelectWhiteUnderbase"/>'s answer.
    /// </remarks>
    public bool CanConfirmWhiteUnderbase => CanSelectWhiteUnderbase && SelectedWhiteUnderbaseChoice is not null;

    /// <summary>The confirmed print size, or a plain "not set".</summary>
    public string ConfirmedDimensions =>
        _session?.Dimensions is { } dimensions ? Describe(dimensions) : Strings.Session_DimensionsNotSet;

    /// <summary>The confirmed W1 branch, or a plain "not chosen".</summary>
    public string ConfirmedWhiteUnderbase => _session?.WhiteUnderbaseBranch is { } branch
        ? DisplayNames.WhiteUnderbaseBranch(branch)
        : Strings.Session_W1NotChosen;

    /// <summary>
    /// What the typed millimetres would become, or empty while they are not a usable size.
    /// </summary>
    /// <remarks>
    /// The pixels come from <see cref="PrintDimensions"/>, which derives them at the fixed
    /// production DPI. This screen does not divide by 25.4 anywhere — the preview and the
    /// value that gets persisted are computed by the same code, so they cannot disagree
    /// (§4).
    /// </remarks>
    public string PendingDimensions => TryReadTypedDimensions(out PrintDimensions typed)
        ? Describe(typed)
        : string.Empty;

    public bool HasOutputs => Outputs.Count > 0;

    /// <summary>
    /// Whether the review panel is shown.
    /// </summary>
    /// <remarks>
    /// Derived from the review commands being legal rather than from the step state, for the
    /// same reason as above: the panel exists to carry Approve and Reject, so "is either of
    /// them offered" is the honest condition, and it cannot drift from the buttons inside it.
    /// </remarks>
    public bool IsReviewRequired => CanApprove || CanReject;

    /// <summary>Shows <paramref name="session"/> exactly as the service returned it.</summary>
    /// <remarks>
    /// Synchronous, because navigation is. The images the screen shows are not: loading them
    /// starts here and completes on <see cref="PreviewsLoaded"/>, so a caller that needs the
    /// pictures to be there — a test, or a later screen — can await that rather than guess.
    /// </remarks>
    public void Open(SessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);

        Show(session);
        Notice = null;
    }

    /// <summary>
    /// The in-flight preview load, or a completed task when there is nothing to load.
    /// </summary>
    /// <remarks>
    /// It never faults: <see cref="LoadPreviewsAsync"/> turns every preview failure into a pane
    /// that says so, because a picture that would not decode is not a reason for anything else
    /// on this screen to stop working (§21).
    /// </remarks>
    public Task PreviewsLoaded { get; private set; } = Task.CompletedTask;

    // --- Commands ------------------------------------------------------------------------

    /// <summary>
    /// Confirms the imported original through the ordinary command path (Part 3C3A §6).
    /// </summary>
    /// <remarks>
    /// Note what this does <i>not</i> do: it does not set the step to Approved. Whether
    /// confirmation is a bare acknowledgement or a hash-bound design-readiness review depends
    /// on the workflow definition, and only the engine knows which.
    /// </remarks>
    [RelayCommand]
    private Task ConfirmOriginalAsync(CancellationToken cancellationToken) =>
        RunAsync(_ => new WorkflowCommand.ConfirmOriginal(), cancellationToken);

    /// <summary>
    /// Starts an attempt for the current step (Part 3C3A §7).
    /// </summary>
    /// <remarks>
    /// The environment gate, the automation lock, the adapter call, output validation, hashing
    /// and the two metadata transactions all happen behind
    /// <see cref="ISessionService.ExecuteAsync"/>. This screen supplies the step and receives
    /// the refreshed session.
    /// </remarks>
    [RelayCommand]
    private Task RunStepAsync(CancellationToken cancellationToken) =>
        RunAsync(step => new WorkflowCommand.StartStep(step), cancellationToken);

    /// <summary>
    /// Approves the result currently on screen, bound to its exact hash (Part 3C3A §10).
    /// </summary>
    /// <remarks>
    /// The hash comes from the artefact this screen displayed, not from a value cached when the
    /// session was first opened, and only when that artefact is the step's own result. If the
    /// file changed after it was shown, the service's integrity re-check refuses the command
    /// with <c>RevisionIntegrityMismatch</c> and the session does not advance — there is no
    /// automatic re-approval anywhere in this path.
    /// </remarks>
    [RelayCommand]
    private Task ApproveAsync(CancellationToken cancellationToken) =>
        RunAsync(
            step => ReviewedHash is Sha256 hash ? new WorkflowCommand.Approve(step, hash) : null,
            cancellationToken);

    /// <summary>Rejects the result currently on screen with a quick reason and optional notes.</summary>
    [RelayCommand]
    private Task RejectAsync(CancellationToken cancellationToken) =>
        RunAsync(
            step => ReviewedHash is Sha256 hash
                ? new WorkflowCommand.Reject(step, hash, SelectedRejectionReason.Reason, Trimmed(RejectionNotes))
                : null,
            cancellationToken);

    /// <summary>
    /// Returns a rejected, failed or interrupted step to a state where a new attempt is legal.
    /// </summary>
    /// <remarks>
    /// Deliberately not combined with Run Step. Retry moves the step to Waiting and stops
    /// there, so the operator sees the state progression rather than a single button that
    /// silently does two things (Part 3C3A §12).
    /// </remarks>
    [RelayCommand]
    private Task RetryAsync(CancellationToken cancellationToken) =>
        RunAsync(step => new WorkflowCommand.Retry(step), cancellationToken);

    /// <summary>
    /// Skips the current step, recording the stable default reason.
    /// </summary>
    /// <remarks>
    /// Which steps may be skipped is the workflow definition's answer, not this screen's: the
    /// button is offered only when <c>AvailableCommands</c> contains Skip, and the engine still
    /// refuses a non-skippable step if it is asked anyway.
    /// </remarks>
    [RelayCommand]
    private Task SkipAsync(CancellationToken cancellationToken) =>
        RunAsync(step => new WorkflowCommand.Skip(step), cancellationToken);

    /// <summary>Ends automated processing and transfers the work to the operator.</summary>
    [RelayCommand]
    private Task HandOffAsync(CancellationToken cancellationToken) =>
        RunAsync(step => new WorkflowCommand.HandOff(step, HandedOffFromSessionReason), cancellationToken);

    /// <summary>
    /// Types a preset's nominal size into the boxes (Part 3C3B §5).
    /// </summary>
    /// <remarks>
    /// Confirms nothing. It is a shortcut past typing four digits, after which the operator
    /// still reads the millimetres, still may change them, and still presses Confirm — which
    /// is what "the operator must still be able to enter the resulting physical dimensions
    /// explicitly" asks for. It never resizes an image and never enlarges anything.
    /// </remarks>
    [RelayCommand]
    private void ApplyPreset(SizePresetChoice? preset)
    {
        if (preset is null)
        {
            return;
        }

        // Assigning the text marks the pending size as Custom through the change handlers
        // below, so the preset is recorded afterwards rather than before.
        WidthMmText = preset.WidthMm.ToString(CultureInfo.CurrentCulture);
        HeightMmText = preset.HeightMm.ToString(CultureInfo.CurrentCulture);
        _pendingPreset = preset.Preset;
    }

    /// <summary>
    /// Confirms the typed print dimensions through the ordinary command path (§3).
    /// </summary>
    /// <remarks>
    /// The only thing this does with the text is turn it into a number. Whether that number is
    /// a usable size is <see cref="PrintDimensions.TryFromMillimetres"/>'s answer, and whether
    /// the session may accept it now is the engine's — neither rule is restated here, and no
    /// size is silently adjusted to make it acceptable (§4).
    /// </remarks>
    [RelayCommand]
    private Task SetDimensionsAsync(CancellationToken cancellationToken)
    {
        if (!TryReadTypedDimensions(out PrintDimensions dimensions))
        {
            Notice = Strings.Session_DimensionsInvalid;
            return Task.CompletedTask;
        }

        return RunAsync(new WorkflowCommand.SetPrintDimensions(dimensions), cancellationToken);
    }

    /// <summary>
    /// Records the operator's explicit white-underbase decision (§6).
    /// </summary>
    /// <remarks>
    /// Does nothing at all until a branch has been picked. There is no fallback to a branch
    /// when none is selected, because a fallback is a default (MVP design §12).
    /// </remarks>
    [RelayCommand]
    private Task SelectWhiteUnderbaseAsync(CancellationToken cancellationToken) =>
        SelectedWhiteUnderbaseChoice is { } choice
            ? RunAsync(
                new WorkflowCommand.SelectWhiteUnderbaseBranch(choice.Branch, JustificationFor(choice.Branch)),
                cancellationToken)
            : Task.CompletedTask;

    /// <summary>
    /// Finishes the session (§12).
    /// </summary>
    /// <remarks>
    /// Offered only while <c>AvailableCommands</c> contains Complete, which the engine reports
    /// when every step is finished and the terminal artefact is Approved. There is no path here
    /// that marks a step done, skips a required one, or completes around one.
    /// </remarks>
    [RelayCommand]
    private Task CompleteAsync(CancellationToken cancellationToken) =>
        RunAsync(new WorkflowCommand.Complete(), cancellationToken);

    /// <summary>
    /// Reopens a completed production session at PrintDimensions to make another size (§14).
    /// </summary>
    /// <remarks>
    /// The outputs already produced are left exactly as they are — the new size is a sibling
    /// derived from the same approved Revision, which is the engine's rule and not something
    /// this screen arranges. The pending size and branch are cleared so the next output's two
    /// decisions are made afresh rather than inherited from the last one.
    /// </remarks>
    [RelayCommand]
    private Task AddAnotherSizeAsync(CancellationToken cancellationToken)
    {
        ClearPendingDecisions();
        return RunAsync(new WorkflowCommand.AddAnotherSize(), cancellationToken);
    }

    /// <summary>Returns to Home. Changes nothing about the session (Part 3C3A §16).</summary>
    /// <remarks>
    /// The panes are dropped on the way out (Part C1 §19). Navigation already discards this
    /// transient view model, so this is belt and braces rather than the mechanism — but it is
    /// the difference between "the images are collectable once the screen is collected" and
    /// "the images are collectable now", and the images are the only large objects here.
    /// </remarks>
    [RelayCommand]
    private async Task BackToHomeAsync(CancellationToken cancellationToken)
    {
        ClearPreviews();
        await _navigation.GoHomeAsync(cancellationToken).ConfigureAwait(true);
    }

    // --- Zoom (Part C1 §12, §13, §15) ----------------------------------------------------
    //
    // State only. Nothing here reads a file, resamples an image or writes anything: the
    // magnification is applied by the view's own transform, and the Revision is untouched
    // whatever the operator does with these three buttons.

    /// <summary>Magnifies one step, leaving fit-to-viewport if that is where it started.</summary>
    [RelayCommand]
    private void ZoomIn() => ApplyZoom(EffectiveZoom * ZoomStep);

    /// <summary>Reduces one step, leaving fit-to-viewport if that is where it started.</summary>
    [RelayCommand]
    private void ZoomOut() => ApplyZoom(EffectiveZoom / ZoomStep);

    /// <summary>Returns to the opening state: the whole image fitted to its viewport (§15).</summary>
    [RelayCommand]
    private void ResetZoom()
    {
        IsFitToViewport = true;
        ZoomScale = 1.0;
    }

    // --- Plumbing ------------------------------------------------------------------------

    /// <summary>
    /// The hash a review decision must be bound to: the hash of the artefact actually
    /// displayed, and only when that artefact is the current step's own result.
    /// </summary>
    private Sha256? ReviewedHash =>
        _session?.CurrentArtefact is { IsCurrentStepResult: true } artefact ? artefact.Sha256 : null;

    private bool Allows(CommandKind kind) => _session?.AvailableCommands.Contains(kind) == true;

    /// <summary>Floating-point slack, so eight steps of ×1.25 still count as reaching 800%.</summary>
    private const double ZoomTolerance = 1e-9;

    /// <summary>
    /// The magnification a zoom step starts from.
    /// </summary>
    /// <remarks>
    /// Fit is treated as 100% for this purpose rather than as the viewport's actual scale,
    /// which this layer does not know and should not: the first press of Zoom In must land on
    /// a stated, reproducible number, not on "whatever 1.25× of however the window happened to
    /// be sized comes to".
    /// </remarks>
    private double EffectiveZoom => IsFitToViewport ? 1.0 : ZoomScale;

    /// <summary>Applies a requested magnification, clamped to the stated bounds (§12).</summary>
    private void ApplyZoom(double requested)
    {
        double clamped = Math.Clamp(requested, MinimumZoom, MaximumZoom);
        IsFitToViewport = false;
        ZoomScale = clamped;
    }

    partial void OnIsFitToViewportChanged(bool value) => NotifyZoomChanged();

    partial void OnZoomScaleChanged(double value) => NotifyZoomChanged();

    private void NotifyZoomChanged()
    {
        OnPropertyChanged(nameof(ZoomLabel));
        OnPropertyChanged(nameof(CanZoomIn));
        OnPropertyChanged(nameof(CanZoomOut));
    }

    /// <summary>
    /// Fills <see cref="PreviewPanes"/> from the preview seam (§7, §9, §22).
    /// </summary>
    /// <remarks>
    /// Two identities in, at most two panes out. The screen never decides <i>which</i> Revision
    /// is upstream — <see cref="SessionView.UpstreamArtefact"/> is resolved from the real
    /// derivation edge in the workflow layer, and this method only asks for it by id (§9).
    /// <para>
    /// A failure from either request produces a pane that says the preview is unavailable and
    /// changes nothing else: no <see cref="Notice"/>, no reload, no command. That separation is
    /// the point of §21 — a decoder that cannot draw a container has said nothing about whether
    /// the bytes on disk are the ones the operator is about to approve.
    /// </para>
    /// </remarks>
    private async Task LoadPreviewsAsync(SessionView session, int generation, CancellationToken cancellationToken)
    {
        if (session.CurrentArtefact is not { } current)
        {
            return;
        }

        List<ArtefactPreviewPane> loaded = [];

        if (session.UpstreamArtefact is { } upstream)
        {
            loaded.Add(await BuildPaneAsync(
                session.Id, BeforeLabel, upstream, cancellationToken).ConfigureAwait(true));
        }

        loaded.Add(await BuildPaneAsync(
            session.Id,
            session.HasBeforeAfterComparison ? AfterLabel : SinglePreviewLabel,
            current,
            cancellationToken).ConfigureAwait(true));

        // Decoding is slow enough that a second command can land while the first load is still
        // in flight. Publishing only for the generation that is still current is what stops the
        // older load's images from reappearing beside the newer state's metadata — the exact
        // staleness a review surface must never show.
        if (generation != _previewGeneration)
        {
            return;
        }

        foreach (ArtefactPreviewPane pane in loaded)
        {
            PreviewPanes.Add(pane);
        }

        OnPropertyChanged(nameof(HasPreview));
    }

    private async Task<ArtefactPreviewPane> BuildPaneAsync(
        SessionId sessionId, string heading, ArtefactView artefact, CancellationToken cancellationToken)
    {
        OperationResult<ImagePreview> preview = await _previews
            .GetPreviewAsync(sessionId, artefact.RevisionId, cancellationToken)
            .ConfigureAwait(true);

        return preview.IsSuccess
            ? ArtefactPreviewPane.From(heading, artefact.FileName, preview.Value)
            : ArtefactPreviewPane.Unreadable(heading, artefact.FileName, preview.Failure);
    }

    /// <summary>
    /// Which preview load is the current one.
    /// </summary>
    /// <remarks>
    /// Incremented by every <see cref="ClearPreviews"/>, which is every state change. A load
    /// that started before the last one publishes nothing.
    /// </remarks>
    private int _previewGeneration;

    /// <summary>Drops every displayed image, so the bytes become collectable at once (§19).</summary>
    private void ClearPreviews()
    {
        _previewGeneration++;

        if (PreviewPanes.Count == 0)
        {
            return;
        }

        PreviewPanes.Clear();
        OnPropertyChanged(nameof(HasPreview));
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// The stable English justification recorded with a W1 decision.
    /// </summary>
    /// <remarks>
    /// Not a resource, for the same reason <see cref="WorkflowCommand.Skip.DefaultReason"/> is
    /// not: it is persisted as audit history, and a record whose wording changed with the
    /// workstation's language would be a poor audit trail (MVP design §13.4). It records the
    /// classification the operator claimed, which is exactly what makes the decision reviewable
    /// later.
    /// </remarks>
    private static string JustificationFor(WhiteUnderbaseBranch branch) => branch switch
    {
        WhiteUnderbaseBranch.W1_0px =>
            "Operator classified the finished design as fine detail and selected 0 px contraction.",
        WhiteUnderbaseBranch.W1_1px =>
            "Operator classified the finished design as ordinary artwork and selected 1 px contraction.",
        WhiteUnderbaseBranch.W1_2px =>
            "Operator classified the finished design as solid or full rectangular artwork and selected 2 px contraction.",
        _ => "Operator selected the white-underbase branch explicitly on the session screen.",
    };

    /// <summary>
    /// Turns the typed millimetres into a <see cref="PrintDimensions"/>, if they are usable.
    /// </summary>
    /// <remarks>
    /// Two steps, and only the first belongs to this screen: parsing text into a number is a
    /// presentation concern, and whether that number is an acceptable size is the domain's
    /// answer through <see cref="PrintDimensions.TryFromMillimetres"/>. Nothing is rounded up,
    /// clamped or substituted on the way through.
    /// </remarks>
    private bool TryReadTypedDimensions(out PrintDimensions dimensions)
    {
        dimensions = default;

        return TryReadMillimetres(WidthMmText, out double widthMm)
            && TryReadMillimetres(HeightMmText, out double heightMm)
            && PrintDimensions.TryFromMillimetres(widthMm, heightMm, _pendingPreset, out dimensions);
    }

    private static bool TryReadMillimetres(string? text, out double millimetres) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out millimetres);

    private static string Describe(PrintDimensions dimensions) => string.Format(
        CultureInfo.CurrentCulture,
        Strings.Session_DimensionsSummary,
        dimensions.WidthMm,
        dimensions.HeightMm,
        dimensions.PixelWidth,
        dimensions.PixelHeight,
        dimensions.Dpi);

    /// <summary>Forgets the unconfirmed size and branch, so the next output decides both afresh.</summary>
    private void ClearPendingDecisions()
    {
        WidthMmText = null;
        HeightMmText = null;
        _pendingPreset = SizePreset.Custom;
        SelectedWhiteUnderbaseChoice = null;
    }

    /// <summary>Editing either box means the size is the operator's, not a preset's.</summary>
    partial void OnWidthMmTextChanged(string? value)
    {
        _pendingPreset = SizePreset.Custom;
        OnPropertyChanged(nameof(PendingDimensions));
    }

    /// <inheritdoc cref="OnWidthMmTextChanged" />
    partial void OnHeightMmTextChanged(string? value)
    {
        _pendingPreset = SizePreset.Custom;
        OnPropertyChanged(nameof(PendingDimensions));
    }

    partial void OnSelectedWhiteUnderbaseChoiceChanged(WhiteUnderbaseChoice? value) =>
        OnPropertyChanged(nameof(CanConfirmWhiteUnderbase));

    /// <summary>
    /// Builds the command for the current step, executes it, and shows whatever came back.
    /// </summary>
    /// <remarks>
    /// For the step-scoped actions only. Session-scoped ones — Complete and AddAnotherSize —
    /// are legal precisely when there is no current step left, so they go straight to the
    /// overload below rather than through a step that would be null.
    /// </remarks>
    private Task RunAsync(Func<StepKind, WorkflowCommand?> build, CancellationToken cancellationToken) =>
        _session?.CurrentStep is { } step
            ? RunAsync(build(step.Step), cancellationToken)
            : Task.CompletedTask;

    /// <summary>
    /// Executes one command and shows whatever came back.
    /// </summary>
    /// <remarks>
    /// One path for every button, so no action can quietly skip the refresh: the screen is
    /// always rebuilt from the <see cref="SessionView"/> the service returned, and a failure is
    /// reported rather than swallowed or worked around.
    /// </remarks>
    private async Task RunAsync(WorkflowCommand? command, CancellationToken cancellationToken)
    {
        if (_session is null || IsBusy || command is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            Notice = null;
            OperationResult<SessionView> result = await _sessions
                .ExecuteAsync(_session.Id, command, Environment.UserName, cancellationToken)
                .ConfigureAwait(true);

            if (result.IsFailure)
            {
                Notice = Describe(result.Failure);

                // The command did not apply, but the session may still have moved — an
                // integrity mismatch invalidates the Revision it was about, and a failed
                // attempt is persisted before the failure returns. Re-reading is what keeps the
                // screen showing the database rather than the last thing that worked.
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
                await PreviewsLoaded.ConfigureAwait(true);
                return;
            }

            Show(result.Value);
            await PreviewsLoaded.ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (_session is null)
        {
            return;
        }

        OperationResult<SessionView> reloaded =
            await _sessions.LoadAsync(_session.Id, cancellationToken).ConfigureAwait(true);

        if (reloaded.IsSuccess)
        {
            Show(reloaded.Value);
        }
    }

    /// <summary>Rebuilds every displayed value from <paramref name="session"/>.</summary>
    /// <remarks>
    /// The previews are rebuilt wholesale like everything else, and for the same reason: an
    /// image left over from the previous state would be the one thing on the screen still
    /// describing a Revision that has been superseded — which, on a review surface, is the
    /// worst possible thing to leave stale.
    /// </remarks>
    private void Show(SessionView session)
    {
        _session = session;

        // Zoom belongs to the artefact being looked at, so a new one opens fitted (§15).
        ResetZoom();
        ClearPreviews();
        PreviewsLoaded = LoadPreviewsAsync(session, _previewGeneration, CancellationToken.None);

        Steps.Clear();
        foreach (SessionStep step in session.Steps)
        {
            Steps.Add(new SessionStepRow(step, isCurrent: step == session.CurrentStep));
        }

        // Rebuilt wholesale from what the service returned, like everything else here: an
        // output whose review state or validity changed must not survive as the row this
        // screen happened to build earlier (§15).
        Outputs.Clear();
        foreach (PrintOutputView output in session.Outputs)
        {
            Outputs.Add(new PrintOutputRow(output));
        }

        OnPropertyChanged(nameof(SessionName));
        OnPropertyChanged(nameof(Workflow));
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(IsReadOnly));
        OnPropertyChanged(nameof(IsHandedOff));
        OnPropertyChanged(nameof(IsFakeProcessing));
        OnPropertyChanged(nameof(IsFakeTiffOutput));

        OnPropertyChanged(nameof(ConfirmedDimensions));
        OnPropertyChanged(nameof(ConfirmedWhiteUnderbase));
        OnPropertyChanged(nameof(HasOutputs));

        OnPropertyChanged(nameof(HasArtefact));
        OnPropertyChanged(nameof(HasPreview));
        OnPropertyChanged(nameof(IsManualCropRequired));
        OnPropertyChanged(nameof(ArtefactIsInput));
        OnPropertyChanged(nameof(ArtefactFileName));
        OnPropertyChanged(nameof(ArtefactFormat));
        OnPropertyChanged(nameof(ArtefactPixels));
        OnPropertyChanged(nameof(ArtefactDpi));
        OnPropertyChanged(nameof(ArtefactHash));
        OnPropertyChanged(nameof(ArtefactRevision));

        OnPropertyChanged(nameof(CanConfirmOriginal));
        OnPropertyChanged(nameof(CanRunStep));
        OnPropertyChanged(nameof(CanApprove));
        OnPropertyChanged(nameof(CanReject));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanSkip));
        OnPropertyChanged(nameof(CanHandOff));
        OnPropertyChanged(nameof(CanSetDimensions));
        OnPropertyChanged(nameof(CanSelectWhiteUnderbase));
        OnPropertyChanged(nameof(CanConfirmWhiteUnderbase));
        OnPropertyChanged(nameof(CanComplete));
        OnPropertyChanged(nameof(CanAddAnotherSize));
        OnPropertyChanged(nameof(IsReviewRequired));
    }

    /// <summary>
    /// A localised sentence plus the stable failure code.
    /// </summary>
    /// <remarks>
    /// <see cref="OperationFailure.TechnicalDetail"/> is never shown: it is English log text
    /// that can name a path. The code is a stable identifier a support call can quote, and no
    /// stack trace reaches this screen (Part 3C3A §15).
    /// </remarks>
    private static string Describe(OperationFailure failure) => string.Format(
        CultureInfo.CurrentCulture,
        Strings.Session_ActionFailed,
        DisplayNames.Failure(failure.Code),
        failure.Code);
}
