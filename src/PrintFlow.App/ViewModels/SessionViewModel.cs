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
using PrintFlow.Domain.Trimming;
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
/// One earlier step the operator may return to, flattened for the selector
/// (Epic 11200 Part C3 §3, §4).
/// </summary>
/// <remarks>
/// A label over a <see cref="ReturnTargetView"/> the workflow layer produced, and nothing more.
/// The list this belongs to contains exactly the steps <c>ReturnToStep</c> would accept, so
/// there is no "is this legal" question left for the screen to answer — and deliberately no
/// way for it to construct one of these for a step the engine did not offer (§4).
/// </remarks>
public sealed class ReturnTargetRow
{
    internal ReturnTargetRow(ReturnTargetView target)
    {
        ArgumentNullException.ThrowIfNull(target);

        Step = target.Step;
        DisplayName = DisplayNames.Step(target.Step);
        Ordinal = target.Ordinal + 1;
    }

    /// <summary>The persisted step this row returns to. Never displayed raw.</summary>
    public StepKind Step { get; }

    /// <summary>The localised step name — the only part an operator reads.</summary>
    public string DisplayName { get; }

    /// <summary>Its one-based position, so the list reads like the step list above it.</summary>
    public int Ordinal { get; }
}

/// <summary>
/// One trim mode offered beside the margin boxes (Epic 11200 Part C3 §9).
/// </summary>
/// <remarks>
/// Exactly three, in enum order. Unlike <see cref="WhiteUnderbaseChoice"/>, one of them
/// <i>is</i> pre-selected — Tight — and that difference is the point of §10: a trim margin is
/// an operational parameter of a deterministic algorithm whose zero is meaningful, whereas a W1
/// branch is a classification of the artwork that only a human can make. Neither reading is
/// transferable to the other.
/// </remarks>
public sealed class TrimModeChoice
{
    internal TrimModeChoice(TrimMode mode)
    {
        Mode = mode;
        Label = DisplayNames.TrimMode(mode);
    }

    /// <summary>The persisted value. Never displayed.</summary>
    public TrimMode Mode { get; }

    /// <summary>The localised label, carrying what the mode does.</summary>
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

    // --- Manual crop (Epic 11200 Part C2) -------------------------------------------------
    //
    // Three pieces of state and nothing more. Entering crop mode changes nothing about the
    // session, drawing a rectangle changes nothing about the session, and only Apply issues a
    // command — which is what makes Cancel structurally incapable of leaving a trace (§22).

    /// <summary>
    /// Whether the operator is drawing a crop rectangle rather than reviewing (§5).
    /// </summary>
    /// <remarks>
    /// Screen state, deliberately not persisted: nothing about having opened the crop tool is a
    /// fact about the session, and a mode that survived a reload would be a mode the database
    /// had an opinion about. Eligibility to enter it <i>is</i> persisted, and comes from
    /// <see cref="CanManualCrop"/>.
    /// </remarks>
    [ObservableProperty]
    private bool _isCropping;

    /// <summary>
    /// The rectangle the operator has drawn, in <b>source image pixels</b> (§6).
    /// </summary>
    /// <remarks>
    /// Source pixels rather than viewport ones, so the selection means the same thing after the
    /// operator zooms, scrolls or resizes the window — and so the value handed to the command is
    /// the value that was validated. The conversion happens once, in
    /// <see cref="TrySetCropSelection"/>, through the pure <see cref="CropSurfaceLayout"/>.
    /// </remarks>
    [ObservableProperty]
    private TrimBounds? _cropSelection;

    /// <summary>Set when a drag produced nothing usable, cleared by the next usable one (§23).</summary>
    [ObservableProperty]
    private bool _isCropSelectionInvalid;

    // --- Return to an earlier step (Epic 11200 Part C3 §3, §5) ---------------------------
    //
    // Two pieces of state. Picking a destination changes nothing, and opening the confirmation
    // changes nothing — only Confirm issues a command, which is what makes Cancel structurally
    // incapable of leaving a trace, exactly as it is for the crop surface.

    /// <summary>The destination the operator has picked but not yet confirmed.</summary>
    /// <remarks>
    /// Starts null, and every state change clears it. A destination carried over from the
    /// previous state would be a step that may no longer be a legal target at all.
    /// </remarks>
    [ObservableProperty]
    private ReturnTargetRow? _selectedReturnTarget;

    /// <summary>
    /// Whether the confirmation is on screen, waiting to be confirmed or cancelled (§5).
    /// </summary>
    /// <remarks>
    /// Screen state, never persisted. Having looked at a warning is not a fact about the
    /// session.
    /// </remarks>
    [ObservableProperty]
    private bool _isConfirmingReturn;

    // --- Trim margin (Epic 11200 Part C3 §9–§12) ------------------------------------------
    //
    // Unconfirmed operator input, exactly like the millimetre boxes: these mean nothing until
    // SetTrimParameters accepts them, and nothing here computes a rectangle or touches a file.

    /// <summary>The mode the operator has picked. Starts at Tight, which is a default on purpose (§10).</summary>
    [ObservableProperty]
    private TrimModeChoice _selectedTrimMode;

    /// <summary>Unconfirmed operator input for <see cref="TrimMode.UniformMargin"/>.</summary>
    [ObservableProperty]
    private string? _uniformMarginText;

    /// <summary>Unconfirmed operator input for <see cref="TrimMode.EdgeSpecificMargin"/>.</summary>
    [ObservableProperty]
    private string? _topMarginText;

    /// <inheritdoc cref="_topMarginText" />
    [ObservableProperty]
    private string? _rightMarginText;

    /// <inheritdoc cref="_topMarginText" />
    [ObservableProperty]
    private string? _bottomMarginText;

    /// <inheritdoc cref="_topMarginText" />
    [ObservableProperty]
    private string? _leftMarginText;

    // --- Background removal authority (Epic 11300 Part C2B2 §4, §10) ----------------------
    //
    // One piece of state, and it is the confirmation panel being open. Opening it changes
    // nothing about the session, and only Confirm issues a command — the same shape the return
    // confirmation and the crop surface have, and what makes §10's "opening the screen must not
    // authorise" structural rather than promised.

    /// <summary>
    /// Whether the authorisation confirmation is on screen, waiting to be confirmed or
    /// cancelled (§4, §11).
    /// </summary>
    /// <remarks>
    /// Screen state, never persisted. Having looked at what automatic selection will do is not
    /// a fact about the session, and this deliberately is not a checkbox whose ticked-ness
    /// outlives the artefact it was ticked for (§4).
    /// </remarks>
    [ObservableProperty]
    private bool _isConfirmingAutomaticSelection;


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

        // Tight first, and pre-selected. See TrimModeChoice for why this is a default where the
        // W1 selector must not have one (§10).
        TrimModes = new ReadOnlyCollection<TrimModeChoice>(
            Enum.GetValues<TrimMode>().Select(mode => new TrimModeChoice(mode)).ToList());
        _selectedTrimMode = TrimModes[0];

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

    /// <summary>The three trim modes, in enum order (Part C3 §9).</summary>
    public IReadOnlyList<TrimModeChoice> TrimModes { get; }

    /// <summary>
    /// The earlier steps the operator may return to, in workflow order (Part C3 §3, §4).
    /// </summary>
    /// <remarks>
    /// Rebuilt wholesale from <see cref="SessionView.ReturnTargets"/> on every state change,
    /// because which steps are behind you changes as the session moves. Nothing here filters,
    /// adds to, or reorders what the workflow layer offered.
    /// </remarks>
    public ObservableCollection<ReturnTargetRow> ReturnTargets { get; } = [];

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

    // --- Manual crop surface (Epic 11200 Part C2 §5, §22, §23, §34) -----------------------

    public string ManualCropHeading => Strings.Session_ManualCropHeading;

    /// <summary>What the operator does on the image, shown while crop mode is open.</summary>
    public string ManualCropInstructions => Strings.Session_ManualCropInstructions;

    public string BeginManualCropLabel => Strings.Session_ManualCropBegin;

    public string ApplyManualCropLabel => Strings.Session_ManualCropApply;

    public string CancelManualCropLabel => Strings.Session_ManualCropCancel;

    /// <summary>Shown when a drag selected nothing that overlaps the artwork (§23).</summary>
    public string ManualCropInvalidNotice => Strings.Session_ManualCropInvalid;

    /// <summary>
    /// Whether an operator-selected crop is a legal next action (§3).
    /// </summary>
    /// <remarks>
    /// Read straight off <see cref="SessionView.CanManualCrop"/>, which the workflow layer
    /// derives from the session state, the step state and the attempt history through
    /// <c>ManualCropEligibility</c> — the same predicate the service enforces when the command
    /// arrives. This screen restates none of that: an eligibility rule that lived in two places
    /// would eventually give two answers, and the one that decides whether a button appears is
    /// the one that would be wrong (§3, §13).
    /// </remarks>
    public bool CanManualCrop => _session?.CanManualCrop == true;

    /// <summary>True while a drawn rectangle is ready to be submitted (§23).</summary>
    public bool CanApplyManualCrop => IsCropping && CropSelection is not null && !IsBusy;

    /// <summary>
    /// The image the crop rectangle is drawn on, or null when crop mode is closed (§24).
    /// </summary>
    /// <remarks>
    /// The last pane, which is always the one describing <c>SessionView.CurrentArtefact</c>. In
    /// every state a crop is legal in, the Trim step holds no result of its own, so that
    /// artefact is the upstream Revision the step would consume — the very Revision
    /// <c>SubmitManualCrop</c> resolves through <c>UpstreamRevisionOf(Trim)</c>. The operator
    /// therefore draws on the file that is actually going to be cropped, without this screen
    /// choosing a Revision or asking for a second decode: it reuses the pane the C1 preview
    /// seam already produced (§24).
    /// </remarks>
    public ArtefactPreviewPane? CropPane =>
        IsCropping && PreviewPanes.Count > 0 && PreviewPanes[^1] is { HasImage: true } pane ? pane : null;

    /// <summary>The selection in source pixels, or a line saying nothing is selected yet.</summary>
    /// <remarks>
    /// Stated in the artefact's own pixels, never in screen units: it is the number the crop is
    /// actually recorded in, so showing anything else would describe a different rectangle from
    /// the one about to be cropped.
    /// </remarks>
    public string CropSelectionSummary => CropSelection is { } crop
        ? string.Format(
            CultureInfo.CurrentCulture,
            Strings.Session_ManualCropSelection,
            crop.Left, crop.Top, crop.Width, crop.Height)
        : Strings.Session_ManualCropNoSelection;

    // --- Return to an earlier step (Epic 11200 Part C3 §3, §5) ---------------------------

    public string ReturnHeading => Strings.Session_ReturnHeading;

    public string ReturnHint => Strings.Session_ReturnHint;

    public string ReturnTargetLabel => Strings.Session_ReturnTargetLabel;

    public string BeginReturnLabel => Strings.Session_ReturnBegin;

    /// <summary>
    /// What the operator confirms before anything is invalidated (§5).
    /// </summary>
    /// <remarks>
    /// It says later results become invalid and that audit history is kept, and says nothing
    /// about files — because <c>ReturnToStep</c> deletes none. A warning about deletion would
    /// warn about something that does not happen (§5, §6).
    /// </remarks>
    public string ReturnConfirmQuestion => Strings.Session_ReturnConfirmQuestion;

    public string ConfirmReturnLabel => Strings.Session_ReturnConfirm;

    public string CancelReturnLabel => Strings.Session_ReturnCancel;

    /// <summary>
    /// Whether the return control is shown at all (§3, §24).
    /// </summary>
    /// <remarks>
    /// True exactly when the workflow layer offered at least one destination. There is no
    /// condition of this screen's own: <see cref="SessionView.ReturnTargets"/> already contains
    /// only steps the real <c>ReturnToStep</c> accepts, so an offered control and an accepted
    /// command cannot disagree (§4, §8).
    /// </remarks>
    public bool CanReturnToStep => _session?.CanReturnToStep == true;

    /// <summary>True once a destination has been picked, so the confirmation can be opened.</summary>
    public bool CanBeginReturn => CanReturnToStep && SelectedReturnTarget is not null && !IsBusy;

    // --- Trim margin (Epic 11200 Part C3 §9–§12, §18) ------------------------------------

    public string TrimHeading => Strings.Session_TrimHeading;

    public string TrimHint => Strings.Session_TrimHint;

    public string TrimMarginLabel => Strings.Session_TrimMarginLabel;

    public string TrimTopLabel => Strings.Session_TrimTopLabel;

    public string TrimRightLabel => Strings.Session_TrimRightLabel;

    public string TrimBottomLabel => Strings.Session_TrimBottomLabel;

    public string TrimLeftLabel => Strings.Session_TrimLeftLabel;

    public string ApplyTrimMarginLabel => Strings.Session_TrimApply;

    public string TrimCurrentLabel => Strings.Session_TrimCurrentLabel;

    /// <summary>
    /// Whether the margin controls are shown (§9, §17).
    /// </summary>
    /// <remarks>
    /// Read straight off <see cref="SessionView.CanSetTrimParameters"/>, which combines the
    /// engine's answer — Trim is current and between attempts — with the attempt history that
    /// says whether this file is on the manual-crop path. That second half is why the screen
    /// cannot work this out: a file the automatic trim has already refused gets no margin
    /// controls, because adding margin to a crop that was never decided is not a thing the
    /// control could do (§17).
    /// </remarks>
    public bool CanSetTrimParameters => _session?.CanSetTrimParameters == true;

    /// <summary>Whether the single uniform box is the relevant input (§11).</summary>
    public bool IsUniformMargin => SelectedTrimMode.Mode == TrimMode.UniformMargin;

    /// <summary>Whether the four per-edge boxes are the relevant input (§12).</summary>
    public bool IsEdgeSpecificMargin => SelectedTrimMode.Mode == TrimMode.EdgeSpecificMargin;

    /// <summary>
    /// The margin the next Trim run will use, as one line (§18).
    /// </summary>
    /// <remarks>
    /// Read from <see cref="SessionView.TrimMargin"/> — the persisted decision — rather than
    /// from the boxes above it, so it says what would actually happen rather than what has been
    /// typed but not applied.
    /// </remarks>
    public string PendingTrimSummary =>
        _session is { } session ? DisplayNames.TrimMargin(session.TrimMargin) : string.Empty;

    /// <summary>
    /// How the deterministic trim on screen was parameterised (§18).
    /// </summary>
    /// <remarks>
    /// From the attempt that produced this exact Revision, resolved in the workflow layer. It
    /// is the answer to "how was this Trim Revision produced?" shown where the operator is
    /// being asked to approve it — and it is empty for anything that is not a deterministic
    /// trim, rather than falling back to the session's current setting, which would label a
    /// manual crop with a margin nothing applied.
    /// </remarks>
    public string TrimParametersSummary => _session?.CurrentTrimParameters is { } margin
        ? DisplayNames.TrimMargin(margin)
        : string.Empty;

    /// <inheritdoc cref="TrimParametersSummary" />
    public bool HasTrimParameters => _session?.HasTrimParameters == true;

    // --- Background removal authority (Epic 11300 Part C2B2 §3, §7–§9, §14) --------------

    public string BackgroundRemovalHeading => Strings.Session_BackgroundRemovalHeading;

    /// <summary>What automatic selection is, in one line. Advice, never a decision (§3).</summary>
    public string BackgroundRemovalHint => Strings.Session_BackgroundRemovalHint;

    /// <summary>
    /// The operator action, worded as a decision about this image (§3).
    /// </summary>
    /// <remarks>
    /// "Use Automatic Selection for this image", not "enable automatic background removal".
    /// The authority the command records is bound to one Revision and one hash, so a label
    /// that read like a session setting would be describing something the system cannot do.
    /// </remarks>
    public string AuthoriseAutomaticSelectionLabel => Strings.Session_BackgroundRemovalAuthorise;

    /// <summary>
    /// What the operator confirms before the authority is recorded (§11).
    /// </summary>
    /// <remarks>
    /// It says Meitu decides the subject on its own and that the result still needs checking.
    /// It promises nothing about cutout quality and claims nothing about later versions of the
    /// image, because the authority covers neither.
    /// </remarks>
    public string AutomaticSelectionConfirmQuestion => Strings.Session_BackgroundRemovalConfirmQuestion;

    public string ConfirmAutomaticSelectionLabel => Strings.Session_BackgroundRemovalConfirm;

    public string CancelAutomaticSelectionLabel => Strings.Session_BackgroundRemovalCancel;

    /// <summary>What is shown while nothing authorises a run (§8, §9).</summary>
    public string AutomaticSelectionNotAuthorisedNotice => Strings.Session_BackgroundRemovalNotAuthorised;

    /// <summary>What is shown once the step would really start (§8).</summary>
    public string BackgroundRemovalRunnableNotice => Strings.Session_BackgroundRemovalRunnable;

    /// <summary>
    /// Whether the authorisation control is offered at all (§7).
    /// </summary>
    /// <remarks>
    /// Read straight off <see cref="SessionView.CanSetBackgroundRemovalDecision"/>, which is the
    /// engine's own answer to "would <c>SetBackgroundRemovalDecision</c> be accepted right now".
    /// This screen restates none of it: not the step, not the step state, not the session state.
    /// </remarks>
    public bool CanAuthoriseAutomaticSelection => _session?.CanSetBackgroundRemovalDecision == true;

    /// <summary>True once the confirmation may be opened: the offer is real and nothing is in flight.</summary>
    public bool CanBeginAutomaticSelection =>
        CanAuthoriseAutomaticSelection && _session?.CurrentArtefact is not null && !IsBusy;

    /// <summary>
    /// Whether an authority covering the artefact on screen is in force (§9).
    /// </summary>
    /// <remarks>
    /// <see cref="SessionView.BackgroundRemovalDecision"/> is already the <i>usable</i>
    /// authority — the read model reports Unspecified for one granted over content that has
    /// since been replaced — so this is a reading of that single answer and not a second
    /// staleness rule. There is deliberately no comparison of Revisions or hashes anywhere in
    /// this file: a screen with its own opinion about staleness would eventually disagree with
    /// the engine that decides whether the run starts (§2, §6).
    /// </remarks>
    public bool IsAutomaticSelectionAuthorised =>
        _session?.BackgroundRemovalDecision == BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent;

    /// <summary>
    /// Whether the screen is in the unauthorised state (§9).
    /// </summary>
    /// <remarks>
    /// The plain negation of <see cref="IsAutomaticSelectionAuthorised"/>, which exists so the
    /// view can collapse one half and expand the other without an inverting converter. It adds
    /// no condition of its own — in particular, an authority that has gone stale returns the
    /// screen here on its own, because the read model has already stopped reporting it (§9).
    /// </remarks>
    public bool IsAutomaticSelectionPending => !IsAutomaticSelectionAuthorised;

    /// <summary>The authorised state as one compact line, naming the Revision it covers (§9).</summary>
    public string AutomaticSelectionAuthorisedNotice =>
        _session?.BackgroundRemovalDecisionRevisionId is { } revision
            ? string.Format(
                CultureInfo.CurrentCulture, Strings.Session_BackgroundRemovalAuthorised, ShortRevision(revision))
            : string.Empty;

    /// <summary>
    /// Whether Background Removal would really start if asked (§8).
    /// </summary>
    /// <remarks>
    /// <see cref="SessionView.CanRunBackgroundRemoval"/>, and never
    /// "<see cref="IsAutomaticSelectionAuthorised"/> is true": readiness is the engine probing
    /// the real <c>StartStep</c>, which weighs the step state and the automation lock as well as
    /// the decision. Inferring it from the decision alone would offer a run in states where the
    /// command would be refused (§8).
    /// </remarks>
    public bool CanRunBackgroundRemoval => _session?.CanRunBackgroundRemoval == true;

    /// <summary>
    /// The authority the cutout under review was produced under (§14, §15).
    /// </summary>
    /// <remarks>
    /// From <see cref="SessionView.BackgroundRemovalAttemptDecision"/> — the immutable record on
    /// the attempt that produced this exact Revision — and never from the session's current
    /// decision. The two differ the moment an operator returns upstream and authorises different
    /// content, and it is the historical one that describes what is on screen (§15).
    /// <para>
    /// It names the reviewed Revision in short form and says nothing else: no attempt id, no
    /// timestamps, no adapter detail (§14).
    /// </para>
    /// </remarks>
    public string BackgroundRemovalAttemptAudit =>
        _session is { HasBackgroundRemovalAttemptAuthority: true, BackgroundRemovalAttemptReviewedRevisionId: { } reviewed }
            ? string.Format(
                CultureInfo.CurrentCulture, Strings.Session_BackgroundRemovalAttemptAudit, ShortRevision(reviewed))
            : string.Empty;

    /// <inheritdoc cref="BackgroundRemovalAttemptAudit" />
    public bool HasBackgroundRemovalAttemptAudit => _session?.HasBackgroundRemovalAttemptAuthority == true;


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
        ? ShortRevision(artefact.RevisionId)
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

    // --- Manual crop commands (Part C2 §5, §12, §22) --------------------------------------

    /// <summary>
    /// Opens the crop surface. Changes nothing about the session (§22).
    /// </summary>
    /// <remarks>
    /// No command, no attempt, no file: entering crop mode is the operator picking up a tool,
    /// not starting work. The offer itself still comes from the workflow layer — pressing this
    /// when <see cref="CanManualCrop"/> is false does nothing, and the service would refuse the
    /// resulting command anyway (§13).
    /// </remarks>
    [RelayCommand]
    private void BeginManualCrop()
    {
        if (!CanManualCrop || IsBusy)
        {
            return;
        }

        ClearCropState();
        IsCropping = true;
    }

    /// <summary>
    /// Closes the crop surface, discarding the rectangle (§22).
    /// </summary>
    /// <remarks>
    /// The whole of §22 made structural rather than promised: this method cannot leave a trace
    /// because it has nothing to leave one with. It touches no file, issues no command and
    /// reaches no service, so "no file, no Attempt, no Revision, no workflow mutation" is a
    /// property of what the code can do, not of what it happens to do today.
    /// </remarks>
    [RelayCommand]
    private void CancelManualCrop() => ClearCropState();

    /// <summary>
    /// Submits the drawn rectangle through the ordinary command path (§12).
    /// </summary>
    /// <remarks>
    /// This screen calls <see cref="ISessionService.ExecuteAsync"/> and never
    /// <c>IManualCropProcessor</c>. The working copy, the attempt row, the pixel work, the
    /// <c>FileInspector</c> pass, the SHA-256 and the two metadata transactions all happen
    /// behind that call, exactly as they do for Run Step — which is what makes a manual crop
    /// as auditable as an automatic one rather than a side door around the machinery (§9, §15).
    /// </remarks>
    [RelayCommand]
    private async Task ApplyManualCropAsync(CancellationToken cancellationToken)
    {
        if (CropSelection is not { } crop)
        {
            IsCropSelectionInvalid = true;
            return;
        }

        await RunAsync(
            step => new WorkflowCommand.SubmitManualCrop(step, crop), cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Records a drag as a source-pixel rectangle, or refuses it (§7, §23).
    /// </summary>
    /// <remarks>
    /// The view supplies the geometry it can measure and the two points the mouse reported; the
    /// mapping and every validity question belong to <see cref="CropSurfaceLayout"/>, which is
    /// pure and tested on its own. The refusal is the same one the domain would give — a
    /// rectangle with no pixel in it — so an invalid selection is stopped here <i>and</i> would
    /// be stopped again by the engine and the processor if it somehow got through (§23).
    /// </remarks>
    /// <returns>True when the drag produced a usable rectangle.</returns>
    public bool TrySetCropSelection(CropSurfaceLayout layout, double x1, double y1, double x2, double y2)
    {
        if (!IsCropping)
        {
            return false;
        }

        if (!layout.TryToSourceBounds(x1, y1, x2, y2, out TrimBounds bounds))
        {
            CropSelection = null;
            IsCropSelectionInvalid = true;
            return false;
        }

        CropSelection = bounds;
        IsCropSelectionInvalid = false;
        return true;
    }

    // --- Return to an earlier step (Part C3 §3, §5, §6) -----------------------------------

    /// <summary>
    /// Opens the confirmation. Changes nothing about the session (§5).
    /// </summary>
    /// <remarks>
    /// No command, no attempt, no invalidation: this is the operator being told what returning
    /// will do, before anything does it. The offer itself still comes from the workflow layer —
    /// pressing this with nothing selected, or with no legal target, does nothing.
    /// </remarks>
    [RelayCommand]
    private void BeginReturn()
    {
        if (!CanBeginReturn)
        {
            return;
        }

        IsConfirmingReturn = true;
    }

    /// <summary>
    /// Closes the confirmation, discarding it (§24).
    /// </summary>
    /// <remarks>
    /// The same structural guarantee <see cref="CancelManualCrop"/> has: this method cannot
    /// leave a trace because it has nothing to leave one with. It touches no file, issues no
    /// command and reaches no service.
    /// </remarks>
    [RelayCommand]
    private void CancelReturn() => IsConfirmingReturn = false;

    /// <summary>
    /// Returns to the chosen step through the ordinary command path (§3, §6).
    /// </summary>
    /// <remarks>
    /// This screen calls <see cref="ISessionService.ExecuteAsync"/> and nothing else. The
    /// descendant Revision walk, the dependent PrintOutput invalidation, the step resets and the
    /// retention of every review decision and every file all happen behind that call, under the
    /// rules Part 3A already established — none of which is restated, reimplemented or adjusted
    /// here (§6, §7).
    /// </remarks>
    [RelayCommand]
    private async Task ConfirmReturnAsync(CancellationToken cancellationToken)
    {
        if (SelectedReturnTarget is not { } target)
        {
            return;
        }

        await RunAsync(
            new WorkflowCommand.ReturnToStep(target.Step), cancellationToken).ConfigureAwait(true);
    }

    // --- Background removal authority (Part C2B2 §5, §6, §10, §11) ------------------------

    /// <summary>
    /// Opens the confirmation. Changes nothing about the session (§10).
    /// </summary>
    /// <remarks>
    /// No command, no attempt, no authority: this is the operator being told what automatic
    /// selection will do, before anything records that they accepted it. The offer itself still
    /// comes from the workflow layer — pressing this when
    /// <see cref="CanAuthoriseAutomaticSelection"/> is false does nothing.
    /// </remarks>
    [RelayCommand]
    private void BeginAutomaticSelection()
    {
        if (!CanBeginAutomaticSelection)
        {
            return;
        }

        IsConfirmingAutomaticSelection = true;
    }

    /// <summary>
    /// Closes the confirmation, discarding it (§10).
    /// </summary>
    /// <remarks>
    /// The same structural guarantee <see cref="CancelReturn"/> has: this method cannot leave a
    /// trace because it has nothing to leave one with. It touches no file, issues no command and
    /// reaches no service.
    /// </remarks>
    [RelayCommand]
    private void CancelAutomaticSelection() => IsConfirmingAutomaticSelection = false;

    /// <summary>
    /// Authorises automatic selection for the artefact this screen displayed (§5, §6).
    /// </summary>
    /// <remarks>
    /// The Revision and the hash come from <see cref="SessionView.CurrentArtefact"/> — the
    /// artefact whose metadata is on the screen the operator is looking at — and from nowhere
    /// else: not from a filename, not from a field cached when the session was opened, not from
    /// a previous selection, and not from the adapter. Nothing here opens a file or computes a
    /// hash (§5, §17).
    /// <para>
    /// That is also the whole of the stale-screen answer (§6). A screen still showing Revision A
    /// sends A's identity, so if the session has moved to B the engine refuses the command
    /// outright rather than transferring A's authority to B — the same exact-hash rule
    /// <see cref="ApproveAsync"/> relies on. There is deliberately no retry against B: the
    /// operator has not seen B.
    /// </para>
    /// </remarks>
    [RelayCommand]
    private async Task ConfirmAutomaticSelectionAsync(CancellationToken cancellationToken)
    {
        if (_session?.CurrentArtefact is not { } displayed)
        {
            return;
        }

        await RunAsync(
            new WorkflowCommand.SetBackgroundRemovalDecision(
                BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
                displayed.RevisionId,
                displayed.Sha256),
            cancellationToken).ConfigureAwait(true);
    }

    // --- Trim margin (Part C3 §9–§13) -----------------------------------------------------


    /// <summary>
    /// Records the chosen trim margin through the ordinary command path (§13).
    /// </summary>
    /// <remarks>
    /// The only thing this does with the text is turn it into a number. Whether that number is
    /// a usable margin is <see cref="TrimMargin"/>'s answer through its own factories, which
    /// refuse a negative value outright — nothing here clamps, rounds or reinterprets one, and
    /// a negative margin is emphatically not read as "crop further in" (§11).
    /// <para>
    /// The margin then reaches <c>ITrimProcessor</c> only by being persisted and read back when
    /// Run Step starts the attempt. This view model has no reference to the processor and no way
    /// to acquire one (§13).
    /// </para>
    /// </remarks>
    [RelayCommand]
    private Task ApplyTrimMarginAsync(CancellationToken cancellationToken)
    {
        if (!TryReadTypedMargin(out TrimMargin margin))
        {
            Notice = Strings.Session_TrimMarginInvalid;
            return Task.CompletedTask;
        }

        return RunAsync(new WorkflowCommand.SetTrimParameters(margin), cancellationToken);
    }

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

    /// <summary>
    /// Eight hex characters of a Revision id — enough to tell two apart on screen.
    /// </summary>
    /// <remarks>
    /// The <b>last</b> eight, not the first. Revision ids are UUIDv7, whose leading digits are a
    /// millisecond timestamp: two Revisions produced within about a minute of each other — which
    /// is exactly the pair an operator is asked to distinguish after a re-run — share their
    /// leading eight characters entirely. The trailing digits are the random part, so a short
    /// form taken from the end actually differs when the Revisions do
    /// (Epic 11300 Part C2B2 §9, §14).
    /// <para>
    /// Display only, exactly like <see cref="ArtefactHash"/>. Nothing on this screen ever
    /// compares Revisions, and the identity a command carries is always the full value taken
    /// from the read model.
    /// </para>
    /// </remarks>
    private static string ShortRevision(RevisionId revision) =>
        revision.Value.ToString("N", CultureInfo.InvariantCulture)[^8..];


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

    /// <summary>Leaves crop mode with nothing selected. Touches no file and issues no command.</summary>
    private void ClearCropState()
    {
        IsCropping = false;
        CropSelection = null;
        IsCropSelectionInvalid = false;
    }

    partial void OnIsCroppingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanApplyManualCrop));
        OnPropertyChanged(nameof(CropPane));
    }

    partial void OnCropSelectionChanged(TrimBounds? value)
    {
        OnPropertyChanged(nameof(CanApplyManualCrop));
        OnPropertyChanged(nameof(CropSelectionSummary));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanApplyManualCrop));
        OnPropertyChanged(nameof(CanBeginReturn));
        OnPropertyChanged(nameof(CanBeginAutomaticSelection));
    }

    /// <summary>Leaves the return confirmation closed with nothing chosen. Issues no command.</summary>
    private void ClearReturnState()
    {
        IsConfirmingReturn = false;
        SelectedReturnTarget = null;
    }

    partial void OnSelectedReturnTargetChanged(ReturnTargetRow? value)
    {
        // Changing the destination puts the confirmation away: what was confirmed a moment ago
        // was a warning about a different step.
        IsConfirmingReturn = false;
        OnPropertyChanged(nameof(CanBeginReturn));
    }

    partial void OnSelectedTrimModeChanged(TrimModeChoice value)
    {
        OnPropertyChanged(nameof(IsUniformMargin));
        OnPropertyChanged(nameof(IsEdgeSpecificMargin));
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

    /// <summary>
    /// Turns the chosen mode and its boxes into a <see cref="TrimMargin"/>, if they are usable.
    /// </summary>
    /// <remarks>
    /// Two steps, and only the first belongs to this screen: parsing text into whole numbers is
    /// a presentation concern, and whether those numbers are an acceptable margin is the
    /// domain's answer through <see cref="TrimMargin.Uniform"/> and
    /// <see cref="TrimMargin.PerEdge"/>. Nothing is clamped or substituted on the way through —
    /// a blank box, a decimal, a minus sign or a number too large for an <c>int</c> all fail
    /// here and produce the invalid-margin notice rather than a quietly corrected value (§11).
    /// <para>
    /// Tight is the one mode with no input: it means zero on all four edges by construction, so
    /// there is nothing to type and nothing to get wrong.
    /// </para>
    /// </remarks>
    private bool TryReadTypedMargin(out TrimMargin margin)
    {
        margin = TrimMargin.Tight;

        switch (SelectedTrimMode.Mode)
        {
            case TrimMode.TightCrop:
                return true;

            case TrimMode.UniformMargin:
                if (!TryReadPixels(UniformMarginText, out int uniform))
                {
                    return false;
                }

                margin = TrimMargin.Uniform(uniform);
                return true;

            default:
                if (!TryReadPixels(TopMarginText, out int top) ||
                    !TryReadPixels(RightMarginText, out int right) ||
                    !TryReadPixels(BottomMarginText, out int bottom) ||
                    !TryReadPixels(LeftMarginText, out int left))
                {
                    return false;
                }

                margin = TrimMargin.PerEdge(top, right, bottom, left);
                return true;
        }
    }

    /// <summary>
    /// Parses a whole non-negative pixel count.
    /// </summary>
    /// <remarks>
    /// <see cref="NumberStyles.None"/> rather than <c>Integer</c>: it accepts digits and nothing
    /// else, so a leading minus sign is refused by the parse instead of reaching the domain
    /// factory as an exception. The factory would refuse it too — the point is that both do.
    /// </remarks>
    private static bool TryReadPixels(string? text, out int pixels) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.CurrentCulture, out pixels) && pixels >= 0;

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

        // The crop surface belongs to the state that needed one. A rectangle drawn against the
        // file the operator was looking at a moment ago must not survive into a state showing a
        // different one — that is the same staleness the preview generation token exists to
        // prevent, applied to the selection (Part C2 §22, §25).
        ClearCropState();

        // Same reasoning as the crop rectangle: a destination chosen against the previous state
        // may not be a legal target in this one, and a confirmation left standing would be a
        // warning about a step the operator is no longer looking at (§24).
        ClearReturnState();

        // And the same again for the authorisation confirmation. It was opened about one
        // specific artefact; leaving it standing across a state change would put a Confirm
        // button in front of an operator for an image that is no longer the one on screen
        // (Part C2B2 §6, §12).
        IsConfirmingAutomaticSelection = false;

        ClearPreviews();
        PreviewsLoaded = LoadPreviewsAsync(session, _previewGeneration, CancellationToken.None);

        // The margin boxes are re-seeded from the persisted decision rather than left holding
        // what was typed, so what the operator sees is what the next run would actually use.
        // Applying a margin and then looking at the boxes must not show a different number from
        // the summary beside them (§13, §18).
        ShowTrimMargin(session.TrimMargin);

        ReturnTargets.Clear();
        foreach (ReturnTargetView target in session.ReturnTargets)
        {
            ReturnTargets.Add(new ReturnTargetRow(target));
        }

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
        OnPropertyChanged(nameof(CanManualCrop));
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

        OnPropertyChanged(nameof(CanReturnToStep));
        OnPropertyChanged(nameof(CanBeginReturn));
        OnPropertyChanged(nameof(CanSetTrimParameters));
        OnPropertyChanged(nameof(PendingTrimSummary));
        OnPropertyChanged(nameof(TrimParametersSummary));
        OnPropertyChanged(nameof(HasTrimParameters));

        OnPropertyChanged(nameof(CanAuthoriseAutomaticSelection));
        OnPropertyChanged(nameof(CanBeginAutomaticSelection));
        OnPropertyChanged(nameof(IsAutomaticSelectionAuthorised));
        OnPropertyChanged(nameof(IsAutomaticSelectionPending));
        OnPropertyChanged(nameof(AutomaticSelectionAuthorisedNotice));
        OnPropertyChanged(nameof(CanRunBackgroundRemoval));
        OnPropertyChanged(nameof(BackgroundRemovalAttemptAudit));
        OnPropertyChanged(nameof(HasBackgroundRemovalAttemptAudit));
    }

    /// <summary>Re-seeds the mode selector and the margin boxes from a persisted margin.</summary>
    /// <remarks>
    /// Every box gets a value, whichever mode is showing, so switching mode never reveals a
    /// stale number left over from an earlier setting. Tight leaves the boxes at zero, which is
    /// what Tight is.
    /// </remarks>
    private void ShowTrimMargin(TrimMargin margin)
    {
        SelectedTrimMode = TrimModes.First(choice => choice.Mode == margin.Mode);

        UniformMarginText = margin.Top.ToString(CultureInfo.CurrentCulture);
        TopMarginText = margin.Top.ToString(CultureInfo.CurrentCulture);
        RightMarginText = margin.Right.ToString(CultureInfo.CurrentCulture);
        BottomMarginText = margin.Bottom.ToString(CultureInfo.CurrentCulture);
        LeftMarginText = margin.Left.ToString(CultureInfo.CurrentCulture);
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
