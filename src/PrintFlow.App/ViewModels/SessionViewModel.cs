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
using PrintFlow.Workflow.Ports;
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
/// One maximum-bounds shortcut, offered beside the millimetre boxes (Epic 11100 Part 3C3B §5;
/// Epic 11400 Part B1A.2B §6).
/// </summary>
/// <remarks>
/// A shortcut and nothing more: pressing it types the preset's millimetres into the boxes, which
/// the operator can still change before confirming. It confirms nothing, resizes nothing, and is
/// not a size editor.
/// <para>
/// Under the accepted maximum-bound contract the two millimetres are <b>limits</b>, so
/// <see cref="BoundsLabel"/> says so out loud rather than presenting the pair as an exact
/// width × height output. Which of the two actually governs is decided by <c>FitWithinBounds</c>
/// from the source pixels, and is not on offer here (§6).
/// </para>
/// <para>
/// Every millimetre comes from the <see cref="PresetPrintRecommendation"/> the workflow layer
/// resolved from the verified workstation preset — the one authority for what a named preset means
/// as a print size. Emphatically <b>not</b> <c>PrintDimensions.NominalMillimetres</c>, which is the
/// ISO paper size the preset is named after and is a different number: A4 is 210 × 297 mm of paper
/// and a 280 mm maximum long edge of print. Nothing in this file or in XAML states a size
/// (§6; Epic 11400 Part B1A.2D §3, §4).
/// </para>
/// </remarks>
public sealed class SizePresetChoice
{
    internal SizePresetChoice(PresetPrintRecommendation recommendation)
    {
        ArgumentNullException.ThrowIfNull(recommendation);

        Recommendation = recommendation;
        Preset = recommendation.Preset;
        WidthMm = (double)recommendation.MaxWidthMm;
        HeightMm = (double)recommendation.MaxHeightMm;
        Label = DisplayNames.SizePreset(Preset);
        BoundsLabel = SizeText.MaximumBounds(recommendation);
        RecommendationLabel = SizeText.Recommendation(recommendation);
    }

    /// <summary>The configured recommendation this row shows. Never derived from a paper size.</summary>
    public PresetPrintRecommendation Recommendation { get; }

    public SizePreset Preset { get; }

    /// <summary>The preset's maximum width. A limit, never an exact output width.</summary>
    public double WidthMm { get; }

    /// <inheritdoc cref="WidthMm" />
    public double HeightMm { get; }

    public string Label { get; }

    /// <summary>
    /// The preset's limits in operator wording — a maximum box, or a maximum long edge when the
    /// two limits are the same (§6).
    /// </summary>
    public string BoundsLabel { get; }

    /// <summary>The configured recommendation in the wording its configured shape requires.</summary>
    public string RecommendationLabel { get; }
}

/// <summary>One explicit custom target-edge choice; the label is localised, the enum is persisted.</summary>
public sealed class TargetEdgeChoice
{
    internal TargetEdgeChoice(TargetEdge edge)
    {
        Edge = edge;
        Label = DisplayNames.TargetEdge(edge);
    }

    public TargetEdge Edge { get; }

    public string Label { get; }
}

/// <summary>
/// Turns maximum bounds into operator wording, in one place (Epic 11400 Part B1A.2B §5, §6).
/// </summary>
/// <remarks>
/// Formatting only. Nothing here fits an image, selects an edge, converts millimetres to pixels
/// or decides whether a plan still applies — every one of those is answered by the Domain or the
/// workflow layer and merely read here (§3).
/// <para>
/// The long-edge form is the honest reading of a <b>square</b> fit box: fitting proportionally
/// inside one constrains whichever source edge is longer to that single limit and lets Photoshop
/// derive the other, which is exactly what "maximum long edge" means. Presenting a non-square box
/// that way would hide the second limit, so the box form is what a non-square preset gets (§6).
/// </para>
/// </remarks>
internal static class SizeText
{
    /// <summary>Millimetres as an operator reads them: no trailing zeros, no false precision.</summary>
    private const string Millimetres = "0.##";

    public static string MaximumBounds(double maxWidthMm, double maxHeightMm) =>
        IsLongEdgeOnly(maxWidthMm, maxHeightMm)
            ? string.Format(
                CultureInfo.CurrentCulture,
                Strings.Session_MaxLongEdgeSummary,
                maxWidthMm.ToString(Millimetres, CultureInfo.CurrentCulture))
            : string.Format(
                CultureInfo.CurrentCulture,
                Strings.Session_MaxBoundsSummary,
                maxWidthMm.ToString(Millimetres, CultureInfo.CurrentCulture),
                maxHeightMm.ToString(Millimetres, CultureInfo.CurrentCulture));

    public static string Recommendation(PresetPrintRecommendation recommendation) =>
        Recommendation(
            recommendation.Kind, recommendation.MaxWidthMm, recommendation.MaxHeightMm);

    /// <summary>
    /// The configured recommendation in the wording its configured shape requires
    /// (post-final A5 correction §20, §21).
    /// </summary>
    /// <remarks>
    /// The kind is reported by the workflow layer and merely rendered here. The screen holds no
    /// opinion about which presets are which and states no millimetre: A5 says "short edge" only
    /// because the verified preset configures a maximum short edge, and would say "long edge"
    /// again the moment it did not. The technical enum name never reaches an operator.
    /// </remarks>
    public static string Recommendation(
        PresetRecommendationKind? kind, decimal? maxWidthMm, decimal? maxHeightMm)
    {
        if (kind is null || maxWidthMm is null || maxHeightMm is null)
        {
            return string.Empty;
        }

        return kind switch
        {
            PresetRecommendationKind.MaximumLongEdge => string.Format(
                CultureInfo.CurrentCulture,
                Strings.Session_RecommendedLongEdge,
                maxWidthMm.Value.ToString(Millimetres, CultureInfo.CurrentCulture)),
            PresetRecommendationKind.MaximumShortEdge => string.Format(
                CultureInfo.CurrentCulture,
                Strings.Session_RecommendedShortEdge,
                maxWidthMm.Value.ToString(Millimetres, CultureInfo.CurrentCulture)),
            _ => string.Format(
                CultureInfo.CurrentCulture,
                Strings.Session_RecommendedMaximum,
                maxWidthMm.Value.ToString(Millimetres, CultureInfo.CurrentCulture),
                maxHeightMm.Value.ToString(Millimetres, CultureInfo.CurrentCulture)),
        };
    }

    /// <summary>
    /// One configured recommendation's own limits in operator wording, in the form it was
    /// configured in (post-final A5 correction §20).
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="MaximumBounds(double, double)"/>, which describes a fit box and
    /// can only guess at the form from whether the two numbers happen to match. That guess was
    /// right while a single-edge limit was always a square box; it is wrong for a maximum short
    /// edge, whose box depends on the source. A recommendation knows its own kind, so it is asked.
    /// </remarks>
    public static string MaximumBounds(PresetPrintRecommendation recommendation) =>
        recommendation.Kind switch
        {
            PresetRecommendationKind.MaximumLongEdge => string.Format(
                CultureInfo.CurrentCulture,
                Strings.Session_MaxLongEdgeSummary,
                recommendation.MaxWidthMm.ToString(Millimetres, CultureInfo.CurrentCulture)),
            PresetRecommendationKind.MaximumShortEdge => string.Format(
                CultureInfo.CurrentCulture,
                Strings.Session_MaxShortEdgeSummary,
                recommendation.MaxWidthMm.ToString(Millimetres, CultureInfo.CurrentCulture)),
            _ => string.Format(
                CultureInfo.CurrentCulture,
                Strings.Session_MaxBoundsSummary,
                recommendation.MaxWidthMm.ToString(Millimetres, CultureInfo.CurrentCulture),
                recommendation.MaxHeightMm.ToString(Millimetres, CultureInfo.CurrentCulture)),
        };

    public static string MillimetresValue(decimal value) =>
        value.ToString(Millimetres, CultureInfo.CurrentCulture);

    /// <summary>
    /// Whether the box states one limit rather than two.
    /// </summary>
    /// <remarks>
    /// A square box and a long-edge limit are the same constraint, so this is a statement about
    /// the numbers rather than a rule about which presets are which — the shell has no business
    /// holding a second opinion about that.
    /// </remarks>
    private static bool IsLongEdgeOnly(double maxWidthMm, double maxHeightMm) =>
        Math.Abs(maxWidthMm - maxHeightMm) < 0.005;
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
        Location = DisplayNames.OutputLocation(output.Area, output.IsRecycled);
        IsAvailable = !output.IsRecycled;
        IsValid = output.IsValid;
        Validity = output.IsValid ? Strings.Session_OutputValid : Strings.Session_OutputInvalid;
    }

    /// <summary>The workspace file name. Never a path.</summary>
    public string FileName { get; }

    public string Size { get; }

    public string Branch { get; }

    public string Review { get; }

    /// <summary>
    /// Where the file behind this row currently is, in words rather than as a path
    /// (Epic 11400 Part C2B §24, §25).
    /// </summary>
    /// <remarks>
    /// A production TIFF starts in the attempt's Working directory awaiting review and moves into
    /// Approved only when it is approved. Showing the Working location as though it were the
    /// approved deliverable — or continuing to offer a rejected TIFF's bytes after they have gone
    /// to the Recycle Bin — is exactly what this line exists to prevent.
    /// </remarks>
    public string Location { get; }

    /// <summary>False once the file has been recycled; the record survives, the bytes do not.</summary>
    public bool IsAvailable { get; }

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
    private readonly IFilePicker? _filePicker;
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

    /// <summary>
    /// What the running automation is doing, as the workflow layer reports it
    /// (Epic 11300 Part D2A §24, §25, §28).
    /// </summary>
    /// <remarks>
    /// The authority for whether Stop and Take Over are offered, and the reason neither is a
    /// XAML rule. It is refreshed from <see cref="ISessionService.AutomationRuntimeChanged"/>
    /// rather than derived from <see cref="IsBusy"/>: <c>IsBusy</c> is true for every command
    /// this screen issues, including an approval, and offering "Stop the operation" beside a
    /// review decision would be offering to stop something that is not running.
    /// </remarks>
    private AutomationRuntimeView _runtime = AutomationRuntimeView.Idle;

    [ObservableProperty]
    private string? _notice;

    /// <summary>
    /// Whether the Take Over confirmation is standing (Epic 11300 Part D2A §26).
    /// </summary>
    /// <remarks>
    /// A confirmation rather than a straight button, because a takeover is not undoable in the
    /// way a mis-click usually is: it ends automation for the attempt, and returning needs an
    /// explicit re-entry. It changes nothing while it stands — opening it issues no command.
    /// </remarks>
    [ObservableProperty]
    private bool _isConfirmingTakeOver;

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

    /// <summary>The custom edge the operator has explicitly selected but not yet confirmed.</summary>
    [ObservableProperty]
    private TargetEdgeChoice? _selectedTargetEdgeChoice;

    /// <summary>Exact decimal text for the single custom target edge.</summary>
    [ObservableProperty]
    private string? _customMillimetresText;

    /// <summary>Whether the one-edge custom form is open. Opening it changes no workflow state.</summary>
    [ObservableProperty]
    private bool _isChoosingCustomSize;

    /// <summary>The named recommendation a custom target is explicitly adjusting, when any.</summary>
    private SizePresetChoice? _customPresetContext;

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
        ISessionService sessions, IArtefactPreviewService previews, INavigationService navigation, IFilePicker? filePicker = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(previews);
        ArgumentNullException.ThrowIfNull(navigation);

        _sessions = sessions;
        _filePicker = filePicker;
        _previews = previews;
        _navigation = navigation;

        // The Stop and Take Over controls have to appear and disappear *while* a command is in
        // flight, which is exactly when no new SessionView exists to rebuild the screen from.
        // Subscribing is what makes them live; polling would be the alternative and would put a
        // timer in a view model (Part D2A §28).
        _sessions.AutomationRuntimeChanged += OnAutomationRuntimeChanged;

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

        TargetEdgeChoices = new ReadOnlyCollection<TargetEdgeChoice>(
            Enum.GetValues<TargetEdge>().Select(edge => new TargetEdgeChoice(edge)).ToList());

    }

    /// <summary>The open session's steps, in workflow order.</summary>
    public ObservableCollection<SessionStepRow> Steps { get; } = [];

    /// <summary>The open session identity, exposed for coordination and tests; never shown as sizing data.</summary>
    public SessionId Id => _session?.Id
        ?? throw new InvalidOperationException("No session is open.");

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

    public ReviewViewportState ReviewViewport { get; } = new();

    /// <summary>Every quick rejection reason, in enum order.</summary>
    public IReadOnlyList<RejectionReasonChoice> RejectionReasons { get; }

    /// <summary>Every white-underbase branch, in enum order and with none preferred (§6).</summary>
    public IReadOnlyList<WhiteUnderbaseChoice> WhiteUnderbaseChoices { get; }

    /// <summary>The three trim modes, in enum order (Part C3 §9).</summary>
    public IReadOnlyList<TrimModeChoice> TrimModes { get; }

    /// <summary>Exactly Width, Height and Long edge, in domain order and with no inferred choice.</summary>
    public IReadOnlyList<TargetEdgeChoice> TargetEdgeChoices { get; }

    /// <summary>
    /// The earlier steps the operator may return to, in workflow order (Part C3 §3, §4).
    /// </summary>
    /// <remarks>
    /// Rebuilt wholesale from <see cref="SessionView.ReturnTargets"/> on every state change,
    /// because which steps are behind you changes as the session moves. Nothing here filters,
    /// adds to, or reorders what the workflow layer offered.
    /// </remarks>
    public ObservableCollection<ReturnTargetRow> ReturnTargets { get; } = [];

    /// <summary>
    /// The named sizes this installation's verified preset configures (§5;
    /// Epic 11400 Part B1A.2D §3, §4).
    /// </summary>
    /// <remarks>
    /// Rebuilt from <see cref="FlexibleSizeView.PresetRecommendations"/> on every state change,
    /// exactly as <see cref="ReturnTargets"/> is, and for a stronger reason than "it might vary":
    /// what A4 means as a print size is the configured workstation preset's answer, not a paper
    /// standard's. This list was previously built once from
    /// <c>PrintDimensions.NominalMillimetres</c>, which is the ISO size the preset is
    /// <i>named after</i> — 210 × 297 mm for A4, where v1.11.0 configures a 280 mm maximum long
    /// edge. Nothing here states a millimetre, and an unverified installation gets an empty list
    /// rather than a fallback.
    /// </remarks>
    public ObservableCollection<SizePresetChoice> SizePresets { get; } = [];

    // --- Labels --------------------------------------------------------------------------

    public string BackLabel => Strings.Nav_BackToHome;

    public string StepsHeading => Strings.Session_StepsHeading;

    public string PlaceholderNotice => Strings.Session_PlaceholderNotice;

    public string ConfirmOriginalLabel => Strings.Session_ConfirmOriginal;

    public string RunStepLabel => _session is { OriginalSourceFormat: ImageFormat.Psd, CurrentStep.Step: StepKind.OriginalConfirmation }
        ? Strings.Session_PreparePsd
        : _session is { OriginalSourceFormat: ImageFormat.Pdf, CurrentStep.Step: StepKind.OriginalConfirmation }
            ? Strings.Session_PreparePdf : Strings.Session_RunStep;

    public bool HasPdfSource => _session?.OriginalSourceFormat == ImageFormat.Pdf;
    public string PdfSourceNotice => !HasPdfSource || _session?.CurrentStep?.Step != StepKind.OriginalConfirmation ? string.Empty
        : _session is { CurrentArtefact.Facts.Format: ImageFormat.Png, PdfInspection: { IsPreparedSinglePage: true } pdf }
            ? string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Session_PdfPrepared,
                pdf.PageWidthMillimetres, pdf.PageHeightMillimetres, pdf.PixelWidth, pdf.PixelHeight)
            : Strings.Session_PdfPending;

    public bool HasPsdSource => _session?.OriginalSourceFormat == ImageFormat.Psd;
    public string PsdSourceNotice => HasPsdSource
        ? (_session?.CurrentArtefact?.Facts.Format == ImageFormat.Psd ? Strings.Session_PsdPending : Strings.Session_PsdPrepared)
        : string.Empty;

    /// <summary>
    /// Whether the artefact currently under review is the production TIFF
    /// (Epic 11400 Part C2B §22, §23).
    /// </summary>
    /// <remarks>
    /// The only thing that varies for a TIFF is the wording. There is no second review screen and
    /// no Photoshop-specific command: the generic Approve and Reject controls act on the current
    /// step's Revision exactly as they do for an enhanced PNG or a trim, and a TIFF is not a
    /// reason to build a parallel one (§22). What generic wording <i>would</i> get wrong is what
    /// the operator is being asked about — "Approve" beside a production TIFF that is one click
    /// from being the deliverable reads as smaller than it is.
    /// </remarks>
    public bool IsProductionTiffReview =>
        IsReviewRequired && _session?.CurrentStep is { Step: StepKind.PhotoshopOutput };

    public string ApproveLabel =>
        IsProductionTiffReview ? Strings.Session_ApproveTiff : Strings.Session_Approve;

    public string RejectLabel =>
        IsProductionTiffReview ? Strings.Session_RejectTiff : Strings.Session_Reject;

    /// <summary>
    /// What the system established about this TIFF, in one line (§22, §23).
    /// </summary>
    /// <remarks>
    /// Deliberately short, and deliberately only what is already recorded: the validated colour
    /// and white-underbase shape, and which W1 branch was chosen. The parser's own vocabulary —
    /// sample layout, byte order, layer compression, spot identity — stays out of the operator's
    /// way; it lives in the producing attempt's audit note where an investigation can find it.
    /// </remarks>
    public string TiffReviewSummary => string.Format(
        CultureInfo.CurrentCulture,
        Strings.Session_TiffReviewSummary,
        _session?.WhiteUnderbaseBranch is { } branch
            ? DisplayNames.WhiteUnderbaseBranch(branch)
            : string.Empty);

    /// <summary>
    /// The limit of that claim, said out loud (§23).
    /// </summary>
    /// <remarks>
    /// PrintFlow validated a file's structure. It did not look at the artwork, and an operator
    /// who read "validated" as "this will print well" would be trusting a judgement nothing here
    /// made.
    /// </remarks>
    public string TiffReviewCaveat => Strings.Session_TiffReviewCaveat;

    public string RetryLabel => Strings.Session_Retry;

    public string SkipLabel => Strings.Session_Skip;

    public string HandOffLabel => Strings.Session_HandOff;

    public string ReviewHeading =>
        IsProductionTiffReview ? Strings.Session_TiffReviewHeading : Strings.Session_ReviewHeading;

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

    public string MaximumBoundsHeading => Strings.Session_MaxBoundsHeading;

    /// <summary>
    /// What the two boxes mean, in one sentence (Epic 11400 Part B1A.2B §4, §5).
    /// </summary>
    /// <remarks>
    /// It says limits, proportional fitting, no enlargement and the fixed 300 PPI. It does not
    /// name an authoritative axis or a resampling method, because neither is an operator decision
    /// under the accepted contract (§4, §9).
    /// </remarks>
    public string MaximumBoundsHint => Strings.Session_MaxBoundsHint;

    public string MaxWidthMmLabel => Strings.Session_LabelMaxWidthMm;

    public string MaxHeightMmLabel => Strings.Session_LabelMaxHeightMm;

    public string ConfirmMaximumBoundsLabel => Strings.Session_MaxBoundsConfirm;

    public string PresetsLabel => Strings.Session_PresetsLabel;

    public string PresetHint => Strings.Session_PresetHint;

    public string SizeHeading => Strings.Session_SizeHeading;
    public string UsePresetLabel => Strings.Session_UsePreset;
    public string CustomSizeLabel => Strings.Session_CustomSize;
    public string AdjustSizeLabel => Strings.Session_AdjustSize;
    public string TargetEdgeLabel => Strings.Session_TargetEdge;
    public string TargetSizeMmLabel => Strings.Session_TargetSizeMm;
    public string ConfirmCustomSizeLabel => Strings.Session_ConfirmCustomSize;
    public string ChangeSizeLabel => Strings.Session_ChangeSize;
    public string ContinueWithSizeLabel => Strings.Session_ContinueWithSize;

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

    // --- Stop and Take Over (Epic 11300 Part D2A §24–§28) ---------------------------------

    /// <summary>
    /// Whether a Stop control should be shown (§24).
    /// </summary>
    /// <remarks>
    /// Read straight off <see cref="AutomationRuntimeView.CanStopAutomation"/>, which is the
    /// workflow layer's own answer. Nothing here restates "an attempt is running": a screen
    /// that worked that out for itself would be a second copy of the rule, and the first thing
    /// such a copy does is offer Stop on an idle review screen where it means nothing.
    /// </remarks>
    public bool CanStopAutomation => _runtime.CanStopAutomation;

    /// <summary>
    /// Whether a Take Over control should be shown (§25).
    /// </summary>
    /// <remarks>
    /// Narrower than <see cref="CanStopAutomation"/> by one condition the workflow layer
    /// applies: the run must actually drive an external application. A deterministic trim has
    /// no Meitu to hand over, and offering the control there would be the generic always-on
    /// session button §25 rules out.
    /// </remarks>
    public bool CanTakeOverAutomation => _runtime.CanTakeOverAutomation;

    /// <summary>Whether the operator has already asked this run to stop or be taken over.</summary>
    public bool IsStopping =>
        _runtime.State is AutomationRuntimeState.StopRequested or AutomationRuntimeState.TakeOverRequested;

    /// <summary>What the screen says while a stop is unwinding, or null when none is (§37).</summary>
    /// <remarks>
    /// Two messages rather than one, because the two requests promise different things. Stop
    /// says PrintFlow is trying to cancel the external work; Take Over says PrintFlow has let go
    /// and is not touching anything. Showing the first while the second is happening would tell
    /// the operator PrintFlow is doing something to Meitu that it is deliberately not doing.
    /// </remarks>
    public string? StoppingNotice => _runtime.State switch
    {
        AutomationRuntimeState.StopRequested => Strings.Session_StoppingNotice,
        AutomationRuntimeState.TakeOverRequested => Strings.Session_TakingOverNotice,
        _ => null,
    };

    /// <summary>
    /// Whether the operator needs to be told what the external application may still be holding
    /// (§21, §37).
    /// </summary>
    /// <remarks>
    /// From the closed attempt row rather than from the live runtime, so it survives the run
    /// ending and a restart. False after a stop whose signed cancel positively took effect —
    /// there is nothing left to warn about — and true after one that could not resolve a cancel.
    /// </remarks>
    public bool HasRetainedExternalState => _session?.HasRetainedExternalState == true;

    /// <summary>
    /// The warning about what Meitu may still be doing, or null when there is nothing to say.
    /// </summary>
    /// <remarks>
    /// Deliberately never claims the external application is safe, finished or idle. PrintFlow
    /// stopped looking at it, and the honest statements are "it may still be running" and "it
    /// may still be holding a result" (§21).
    /// </remarks>
    public string? RetainedExternalStateNotice => _session?.LastAutomationStop switch
    {
        null => null,
        { Retained: RetainedExternalState.OperationMayStillBeRunning } =>
            Strings.Session_RetainedOperationRunning,
        { Retained: RetainedExternalState.ProcessedResultRetained } =>
            Strings.Session_RetainedProcessedResult,
        { Retained: RetainedExternalState.Unknown } => Strings.Session_RetainedUnknown,
        _ => null,
    };

    /// <summary>
    /// Whether the operator must explicitly return this session to automation (§22, §30).
    /// </summary>
    public bool CanReenterAutomation => _session?.RequiresAutomationReentry == true;

    /// <summary>The confirmation text shown before a takeover is carried out (§26).</summary>
    public string TakeOverConfirmQuestion => Strings.Session_TakeOverConfirmQuestion;

    public string StopLabel => Strings.Session_Stop;

    /// <summary>
    /// The one-line explanation shown beside the two controls (§27).
    /// </summary>
    /// <remarks>
    /// Both hints together rather than one each, so the operator reads the <i>distinction</i>
    /// at the moment of choosing. Told separately, "stops this operation safely" and "leaves
    /// Meitu as it is" are each easy to read as the other.
    /// </remarks>
    public string StopHint => $"{Strings.Session_StopHint} {Strings.Session_TakeOverHint}";

    public string TakeOverLabel => Strings.Session_TakeOver;

    public string TakeOverConfirmLabel => Strings.Session_TakeOverConfirm;

    public string TakeOverCancelLabel => Strings.Session_TakeOverCancel;

    public string ReenterAutomationLabel => Strings.Session_ReenterAutomation;

    public string ReenterAutomationHint => Strings.Session_ReenterAutomationHint;

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

    // --- Detected and applied trim bounds (SCRUM-11081 §13) -------------------------------
    //
    // The crop geometry of the result on screen, from the attempt that produced this exact
    // Revision. Nothing here computes a rectangle, reads a file or scales anything: every
    // number is the one the processor measured and the closing transaction stored, formatted.
    //
    // Empty strings rather than placeholders when there is no geometry — a manual crop, an
    // artefact that is not a trim result, or a trim recorded before the geometry was persisted.
    // The block is collapsed in that case, because "Left 0 px" would be a measurement nobody
    // took.

    /// <summary>Whether the detected/applied bounds block has anything truthful to show.</summary>
    public bool HasTrimBounds => _session?.HasTrimGeometry == true;

    public string TrimBoundsDetectedHeading => Strings.Session_TrimBoundsDetectedHeading;

    public string TrimBoundsAppliedHeading => Strings.Session_TrimBoundsAppliedHeading;

    /// <summary>What the alpha scan found, before this attempt's margin.</summary>
    public string TrimContentBoundsOrigin => _session?.CurrentTrimGeometry is { } g
        ? DisplayNames.TrimBoundsOrigin(g.ContentBounds)
        : string.Empty;

    /// <inheritdoc cref="TrimContentBoundsOrigin" />
    public string TrimContentBoundsExtent => _session?.CurrentTrimGeometry is { } g
        ? DisplayNames.TrimBoundsExtent(g.ContentBounds)
        : string.Empty;

    /// <inheritdoc cref="TrimContentBoundsOrigin" />
    public string TrimContentBoundsSize => _session?.CurrentTrimGeometry is { } g
        ? DisplayNames.TrimBoundsSize(g.ContentBounds)
        : string.Empty;

    /// <summary>What was actually cropped out: the content rectangle after margin and clamp.</summary>
    public string TrimAppliedBoundsOrigin => _session?.CurrentTrimGeometry is { } g
        ? DisplayNames.TrimBoundsOrigin(g.AppliedBounds)
        : string.Empty;

    /// <inheritdoc cref="TrimAppliedBoundsOrigin" />
    public string TrimAppliedBoundsExtent => _session?.CurrentTrimGeometry is { } g
        ? DisplayNames.TrimBoundsExtent(g.AppliedBounds)
        : string.Empty;

    /// <inheritdoc cref="TrimAppliedBoundsOrigin" />
    public string TrimAppliedBoundsSize => _session?.CurrentTrimGeometry is { } g
        ? DisplayNames.TrimBoundsSize(g.AppliedBounds)
        : string.Empty;

    /// <summary>The one-line statement of what the four edge numbers mean.</summary>
    public string TrimBoundsCaption => Strings.Session_TrimBoundsCaption;

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
                CultureInfo.CurrentCulture,
                _session.BackgroundRemovalAttemptDecision == BackgroundRemovalDecision.ManualResultForReviewedContent
                    ? Strings.Session_ManualBackgroundRemovalAudit : Strings.Session_BackgroundRemovalAttemptAudit,
                ShortRevision(reviewed))
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

    public bool CanRetry => Allows(CommandKind.Retry) && _session?.CurrentStepFailure is not
        (FailureCode.PdfMultiplePages or FailureCode.PdfUnreadable or FailureCode.PdfEncrypted);

    public bool CanSkip => Allows(CommandKind.Skip);

    public bool CanSubmitManualResult => _session?.CanSubmitManualResult == true;
    public string SubmitManualResultLabel => Strings.Session_SubmitManualResult;
    public bool IsManualProcessingResult => _session?.CurrentArtefact?.IsManualProcessingResult == true;
    public string ManualProcessingResultLabel => Strings.Session_ManualProcessingResult;

    public bool CanHandOff => Allows(CommandKind.HandOff);

    /// <summary>
    /// Whether the maximum-bounds panel is shown (§3; Epic 11400 Part B1A.2B §5).
    /// </summary>
    /// <remarks>
    /// "Is the session on the PrintDimensions step" is not restated here: the workflow layer
    /// reports <see cref="SessionView.CanSetMaximumBounds"/> from the engine's own answer about
    /// whether <c>SetPrintDimensions</c> would be accepted, which is the same question and one
    /// fewer place to get it wrong. It is what reopens the panel after AddAnotherSize as well,
    /// with no second rule about reopening (§18).
    /// </remarks>
    public bool CanSetMaximumBounds => _session?.CanSetMaximumBounds == true;

    /// <summary>Whether the workflow currently accepts either flexible-size decision.</summary>
    public bool CanChooseFlexibleSize =>
        _session?.Sizing is { CanSetPresetFitSize: true } or { CanSetCustomTargetEdgeSize: true };

    public bool HasPresetSizeSelection =>
        _session?.Sizing is { SizingMode: OperatorSizingMode.PresetFit, Preset: not null };

    public bool HasCustomSizeSelection =>
        _session?.Sizing.SizingMode == OperatorSizingMode.CustomTargetEdge;

    public bool CanAdjustSelectedPreset =>
        HasPresetSizeSelection && ReviewBoundsTarget is not null && !IsBusy;

    public bool HasCustomPresetContext => _customPresetContext is not null;

    public string CustomPresetContext => _customPresetContext is { } preset
        ? string.Format(CultureInfo.CurrentCulture, Strings.Session_BasedOnPreset, preset.Label)
        : string.Empty;

    public string CustomPresetRecommendation =>
        _customPresetContext?.RecommendationLabel ?? string.Empty;

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

    /// <summary>
    /// The maximum bounds that are currently in force, or a plain "not set"
    /// (Epic 11400 Part B1A.2B §8, §15, §16).
    /// </summary>
    /// <remarks>
    /// Sourced from <see cref="SessionView.MaxWidthMm"/> and <see cref="SessionView.MaxHeightMm"/>,
    /// which the read model populates from the <b>usable</b> plan and from nothing else. A legacy
    /// exact pair and a plan bound to content that has since changed both report null there, so
    /// neither can appear here as an active limit — they are shown as history instead, by
    /// <see cref="HistoricalBounds"/> (§15, §16).
    /// </remarks>
    public string ConfirmedMaximumBounds =>
        _session is { MaxWidthMm: { } maxWidth, MaxHeightMm: { } maxHeight }
            ? SizeText.MaximumBounds(maxWidth, maxHeight)
            : Strings.Session_DimensionsNotSet;

    /// <summary>The confirmed W1 branch, or a plain "not chosen".</summary>
    public string ConfirmedWhiteUnderbase => _session?.WhiteUnderbaseBranch is { } branch
        ? DisplayNames.WhiteUnderbaseBranch(branch)
        : Strings.Session_W1NotChosen;

    /// <summary>
    /// The limits the typed millimetres would set, or empty while they are not usable limits.
    /// </summary>
    /// <remarks>
    /// Millimetres only. It deliberately shows no pixel figure: what the image would <i>become</i>
    /// depends on the source's own pixels and is <c>FitWithinBounds</c>'s answer, calculated when
    /// the bounds are recorded and reported back through <see cref="PreparationProjectedSize"/>.
    /// A pixel pair derived here from the two millimetre values would be the independent
    /// conversion the accepted contract stopped treating as an output size (§7).
    /// </remarks>
    public string PendingMaximumBounds => TryReadTypedDimensions(out PrintDimensions typed)
        ? SizeText.MaximumBounds(typed.MaxWidthMm, typed.MaxHeightMm)
        : string.Empty;

    // --- The projected preparation plan (Epic 11400 Part B1A.2B §8, §10) ------------------
    //
    // Every value below is read from SessionView, which reports only the *usable* plan. Nothing
    // here fits an image, chooses an edge, converts millimetres to pixels, or compares a Revision
    // or a hash: a screen that worked any of that out for itself would be a second answer, free
    // to disagree with the one the engine enforces (§3).

    public string PreparationHeading => Strings.Session_PreparationHeading;

    /// <summary>Whether a plan is currently in force and therefore worth summarising (§8).</summary>
    public bool HasPreparationPlan =>
        _session?.PreparationMode is not null || _session?.Sizing.ResizeDirection is not null;

    /// <summary>The active preset or custom edge, formatted only from the authoritative read model.</summary>
    public string CurrentSizeSummary
    {
        get
        {
            if (_session?.Sizing is not { } sizing)
            {
                return string.Empty;
            }

            if (sizing.SizingMode == OperatorSizingMode.PresetFit && sizing.Preset is { } preset)
            {
                return string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.Session_CurrentPreset,
                    DisplayNames.SizePreset(preset));
            }

            return sizing is
            {
                SizingMode: OperatorSizingMode.CustomTargetEdge,
                RequestedTargetEdge: { } edge,
                RequestedMillimetres: { } millimetres,
            }
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.Session_CurrentCustomTarget,
                    DisplayNames.TargetEdge(edge).ToLower(CultureInfo.CurrentCulture),
                    SizeText.MillimetresValue(millimetres))
                : string.Empty;
        }
    }

    public string CurrentRecommendation => _session?.Sizing is { } sizing
        ? SizeText.Recommendation(
            sizing.RecommendationKind,
            sizing.RecommendationMaxWidthMm,
            sizing.RecommendationMaxHeightMm)
        : string.Empty;

    public bool HasCurrentRecommendation => CurrentRecommendation.Length > 0;

    public string CurrentPresetContext =>
        _session?.Sizing is
        {
            SizingMode: OperatorSizingMode.CustomTargetEdge,
            Preset: { } preset,
        }
            ? string.Format(
                CultureInfo.CurrentCulture,
                Strings.Session_BasedOnPreset,
                DisplayNames.SizePreset(preset))
            : string.Empty;

    public bool HasCurrentPresetContext => CurrentPresetContext.Length > 0;

    /// <summary>
    /// What the planned run would do, in a sentence (§8).
    /// </summary>
    /// <remarks>
    /// Describes behaviour rather than the internal resampling policy: pixels unchanged, or a
    /// proportional reduction. "Bicubic Sharper" is auditable Domain state and never appears as an
    /// operator setting (§9).
    /// </remarks>
    public string PreparationModeText => _session?.PreparationMode switch
    {
        PrintPreparationMode.ResolutionOnly => Strings.Session_PreparationResolutionOnly,
        PrintPreparationMode.ProportionalShrink => Strings.Session_PreparationProportionalShrink,
        _ => _session?.Sizing.ResizeDirection switch
        {
            ResizeDirection.ResolutionOnly => Strings.Session_CustomResolutionOnly,
            ResizeDirection.Shrink => Strings.Session_CustomShrink,
            _ => string.Empty,
        },
    };

    public bool HasPreparationModeText => PreparationModeText.Length > 0;

    public bool HasPresetLimitNotice => _session?.Sizing.PresetLimitExceeded == true;

    public string PresetLimitNotice =>
        _session?.Sizing is { PresetLimitExceeded: true, Preset: { } preset }
            ? string.Format(
                CultureInfo.CurrentCulture,
                Strings.Session_PresetExceeded,
                DisplayNames.SizePreset(preset))
            : string.Empty;

    public bool NeedsEnlargementAuthority =>
        _session?.Sizing.NeedsEnlargementAuthority == true;

    public bool CanAuthoriseEnlargement =>
        _session?.Sizing.CanAuthoriseEnlargement == true && !IsBusy;

    public string EnlargementWarning =>
        _session?.Sizing is
        {
            NeedsEnlargementAuthority: true,
            ProjectedScalePercent: { } scale,
        }
            ? string.Format(
                CultureInfo.CurrentCulture,
                Strings.Session_EnlargementWarning,
                scale.ToString("0.#", CultureInfo.CurrentCulture))
            : string.Empty;

    public bool HasUsableEnlargementAuthority =>
        _session?.Sizing.HasUsableEnlargementAuthority == true;

    public string EnlargementConfirmedNotice => Strings.Session_EnlargementConfirmed;

    /// <summary>
    /// Which edge the plan selected, shown only when one was (§8).
    /// </summary>
    /// <remarks>
    /// A read-out, not a control. The edge is chosen by <c>FitWithinBounds</c> from the source
    /// pixels, and there is nothing anywhere on this screen that lets an operator override it
    /// (§23).
    /// </remarks>
    public string PreparationLimitingEdge => _session?.LimitingEdge is { } edge and not Domain.Outputs.LimitingEdge.None
        ? string.Format(
            CultureInfo.CurrentCulture,
            Strings.Session_PreparationLimitingEdge,
            DisplayNames.LimitingEdge(edge))
        : string.Empty;

    /// <inheritdoc cref="PreparationLimitingEdge" />
    public bool HasPreparationLimitingEdge => PreparationLimitingEdge.Length > 0;

    /// <summary>
    /// The pixels the plan projects, as information (§8).
    /// </summary>
    /// <remarks>
    /// Labelled "projected" in every language, because that is what it is: planning evidence that
    /// the selected edge fits the other bound. It is not a Photoshop target and not a result —
    /// reading real geometry back from Photoshop is B1A.3's.
    /// </remarks>
    public string PreparationProjectedSize =>
        _session is { ProjectedPixelWidth: { } width, ProjectedPixelHeight: { } height }
            ? string.Format(
                CultureInfo.CurrentCulture, Strings.Session_PreparationProjectedSize, width, height)
            : string.Empty;

    /// <summary>The fixed production resolution, stated rather than offered (§8).</summary>
    public string PreparationResolution => string.Format(
        CultureInfo.CurrentCulture, Strings.Session_PreparationResolution, PrintDimensions.ProductionDpi);

    // --- Run readiness and the review a stale or legacy size needs (§10–§13) --------------

    /// <summary>
    /// Whether Photoshop output would actually start if asked (§10).
    /// </summary>
    /// <remarks>
    /// <see cref="SessionView.CanRunPhotoshopOutput"/> and nothing else. Neither "dimensions are
    /// not null" nor "the semantics say MaxBoundsV1" is that question: a session can hold a raw
    /// plan calculated against content that has since been replaced, and both of those would call
    /// it ready (§10, §16).
    /// </remarks>
    public bool CanRunPhotoshopOutput => _session?.CanRunPhotoshopOutput == true;

    /// <summary>What the readiness state means, for an operator looking for the Run button.</summary>
    /// <remarks>
    /// Only while the Photoshop step is the one being worked on — the sentence is about that
    /// step's readiness, and on any other step it would be answering a question nobody asked.
    /// </remarks>
    public string RunReadinessNotice => CanRunPhotoshopOutput
        ? Strings.Session_RunReady
        : Strings.Session_RunNotReady;

    /// <inheritdoc cref="RunReadinessNotice" />
    public bool HasRunReadinessNotice =>
        _session?.CurrentStep is { Step: StepKind.PhotoshopOutput };

    /// <summary>
    /// Whether recorded millimetres exist that cannot be executed (§11).
    /// </summary>
    /// <remarks>
    /// <see cref="SessionView.NeedsDimensionReview"/> is the workflow layer's own predicate,
    /// covering both a legacy exact pair and a plan whose source has since changed. This screen
    /// does not tell the two apart, and should not: the operator action is the same, and the
    /// distinction is not something a shell can establish without comparing Revisions itself
    /// (§11, §16).
    /// </remarks>
    public bool NeedsDimensionReview => _session?.NeedsDimensionReview == true;

    /// <summary>
    /// The warning shown over dimensions that need reconfirming (§11).
    /// </summary>
    /// <remarks>
    /// It is a size decision to redo, not a Photoshop failure: no attempt was made, nothing was
    /// processed, and nothing was lost. The wording says exactly that (§11).
    /// </remarks>
    public string DimensionReviewWarning => Strings.Session_DimensionReviewRequired;

    public string HistoricalBoundsLabel => Strings.Session_HistoricalBoundsLabel;

    /// <summary>
    /// The millimetres the session still holds, shown as history while they need review (§15).
    /// </summary>
    /// <remarks>
    /// Read from <see cref="SessionView.Dimensions"/> — the retained pair — rather than from the
    /// usable plan's bounds, which is precisely why it is labelled unconfirmed. Nothing here
    /// converts it, adopts it, or marks it as reviewed because it happens to suit the current
    /// source ratio; that judgement is the operator's, and it is made by confirming bounds again
    /// (§15).
    /// </remarks>
    public string HistoricalBounds => _session?.Dimensions is { } historical
        ? SizeText.MaximumBounds(historical.MaxWidthMm, historical.MaxHeightMm)
        : string.Empty;

    /// <inheritdoc cref="HistoricalBounds" />
    public bool HasHistoricalBounds => NeedsDimensionReview && _session?.Dimensions is not null;

    public string ReviewMaximumBoundsLabel => Strings.Session_ReviewMaximumBounds;

    /// <summary>
    /// Whether the "review the maximum bounds" action can be offered (§12).
    /// </summary>
    /// <remarks>
    /// Two conditions, and the second is the important one: the workflow layer must currently be
    /// offering <c>PrintDimensions</c> as a legal return target. That the size step is <i>usually</i>
    /// behind the Photoshop step is not a licence to assume it always is — the offer comes from
    /// <see cref="SessionView.ReturnTargets"/>, which the engine produced by applying the real
    /// <c>ReturnToStep</c> command (§12).
    /// </remarks>
    public bool CanReviewMaximumBounds =>
        NeedsDimensionReview && ReviewBoundsTarget is not null && !IsBusy;

    /// <summary>The real return target this action would use, or null when there is none.</summary>
    private ReturnTargetRow? ReviewBoundsTarget =>
        ReturnTargets.FirstOrDefault(target => target.Step == StepKind.PrintDimensions);

    // --- The producing attempt's own plan (§19) -------------------------------------------

    public string PreparationAttemptHeading => Strings.Session_PreparationAttemptHeading;

    /// <summary>
    /// Whether the result on screen was produced under a recorded plan (§19).
    /// </summary>
    /// <remarks>
    /// <see cref="SessionView.AttemptPreparation"/> is resolved from the attempt that produced
    /// this exact Revision, so it is false for every artefact that is not a planned Photoshop
    /// output — and, crucially, it never falls back to the session's pending plan when a result
    /// has none (§19).
    /// </remarks>
    public bool HasPreparationAttemptAudit => _session?.AttemptPreparation is not null;

    /// <summary>
    /// The bounds that run actually used, when it was a maximum-bound run (§19).
    /// </summary>
    /// <remarks>
    /// Empty for a target-edge run, which had no fit box: a run that named one exact edge did not
    /// have two limits, and printing "0 × 0 mm" or the requested edge twice would be an audit line
    /// stating something that never happened. The target-edge wording arrives with the
    /// flexible-size screen (Part B1A.2D §29).
    /// </remarks>
    public string PreparationAttemptBounds =>
        _session?.AttemptPreparation is { MaxWidthMm: { } maxWidth, MaxHeightMm: { } maxHeight }
            ? string.Format(
                CultureInfo.CurrentCulture,
                Strings.Session_PreparationAttemptBounds,
                maxWidth.ToString("0.##", CultureInfo.CurrentCulture),
                maxHeight.ToString("0.##", CultureInfo.CurrentCulture))
            : string.Empty;

    public string PreparationAttemptSelection
    {
        get
        {
            if (_session?.AttemptPreparation is not { } attempt)
            {
                return string.Empty;
            }

            if (attempt.Semantics == PrintDimensionSemantics.MaxBoundsV1 && attempt.Preset is { } preset)
            {
                return string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.Session_CurrentPreset,
                    DisplayNames.SizePreset(preset));
            }

            return attempt is { RequestedTargetEdge: { } edge, RequestedMillimetres: { } millimetres }
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.Session_CurrentCustomTarget,
                    DisplayNames.TargetEdge(edge).ToLower(CultureInfo.CurrentCulture),
                    SizeText.MillimetresValue(millimetres))
                : string.Empty;
        }
    }

    public string PreparationAttemptRecommendation =>
        _session?.AttemptPreparation is { } attempt
            ? SizeText.Recommendation(
                attempt.RecommendationKind,
                attempt.RecommendationMaxWidthMm,
                attempt.RecommendationMaxHeightMm)
            : string.Empty;

    public string PreparationAttemptPresetContext =>
        _session?.AttemptPreparation is
        {
            Semantics: PrintDimensionSemantics.TargetEdgeV1,
            Preset: { } preset,
        }
            ? string.Format(
                CultureInfo.CurrentCulture,
                Strings.Session_BasedOnPreset,
                DisplayNames.SizePreset(preset))
            : string.Empty;

    public string PreparationAttemptPresetOverride =>
        _session?.AttemptPreparation?.PresetOverride == true
            ? Strings.Session_PresetOverrideYes
            : string.Empty;

    public string PreparationAttemptResize =>
        _session?.AttemptPreparation is { ResizeDirection: { } direction }
            ? string.Format(
                CultureInfo.CurrentCulture,
                Strings.Session_ProjectedResize,
                DisplayNames.ResizeDirection(direction))
            : _session?.AttemptPreparation?.Semantics == PrintDimensionSemantics.MaxBoundsV1
                ? Strings.Session_ProportionalFit
                : string.Empty;

    public string PreparationAttemptEnlargementConfirmation =>
        _session?.AttemptPreparation?.WasAuthorisedEnlargement == true
            ? Strings.Session_EnlargementExplicitlyConfirmed
            : string.Empty;

    /// <summary>What that run was planned to do, in the same behavioural wording (§19).</summary>
    public string PreparationAttemptMode => _session?.AttemptPreparation is { Mode: { } mode }
        ? DisplayNames.PreparationMode(mode)
        : string.Empty;

    /// <summary>The edge that run selected, including "None" (§19).</summary>
    /// <remarks>
    /// Unlike the pending summary, the audit line states the edge even when it is None: an audit
    /// that silently omitted it would leave a reader unable to tell "no edge was written" from
    /// "this line was not recorded".
    /// </remarks>
    public string PreparationAttemptLimitingEdge => _session?.AttemptPreparation is { } attempt
        ? string.Format(
            CultureInfo.CurrentCulture,
            Strings.Session_PreparationLimitingEdge,
            DisplayNames.LimitingEdge(attempt.LimitingEdge))
        : string.Empty;

    /// <summary>The pixels that run projected, at the resolution it fixed (§19).</summary>
    public string PreparationAttemptProjected => _session?.AttemptPreparation is { } attempt
        ? string.Format(
            CultureInfo.CurrentCulture,
            Strings.Session_PreparationAttemptProjected,
            attempt.ProjectedPixelWidth,
            attempt.ProjectedPixelHeight,
            attempt.ProductionDpi)
        : string.Empty;

    /// <summary>
    /// The qualification that these figures are a plan and not a Photoshop read-back (§19, §21).
    /// </summary>
    /// <remarks>
    /// Shown from <see cref="PrintPreparationAttemptView.IsFakeProjection"/>, which the workflow
    /// layer sets from the adapters actually wired up. A screen that guessed from configuration
    /// could disagree with what really ran — and this is the one line that must not.
    /// </remarks>
    public string PreparationAttemptProjectionNotice =>
        Strings.Session_PreparationAttemptProjectionNotice;

    /// <inheritdoc cref="PreparationAttemptProjectionNotice" />
    public bool HasPreparationAttemptProjectionNotice =>
        _session?.AttemptPreparation?.IsFakeProjection == true;

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

    // --- Stop and Take Over commands (Epic 11300 Part D2A §24–§27) -------------------------

    /// <summary>
    /// Asks the running operation to stop safely (§9, §27).
    /// </summary>
    /// <remarks>
    /// Deliberately <b>not</b> routed through <see cref="RunAsync"/>, and the reason is
    /// structural rather than stylistic. <c>RunAsync</c> returns immediately while
    /// <see cref="IsBusy"/> is true, and <c>IsBusy</c> is true for the whole of the run this is
    /// trying to stop — so a Stop that went through it would silently do nothing exactly when
    /// it is needed. It is also not a <c>WorkflowCommand</c>: it changes no session state and
    /// creates no attempt. It sets a flag the running adapter reads, and the call that started
    /// the run reports the outcome.
    /// <para>
    /// What Stop is permitted to do about Meitu is decided in the workflow layer from the phase
    /// the adapter reports, not here. This screen cannot express "click cancel", and it does not
    /// know whether one will be clicked (§38).
    /// </para>
    /// </remarks>
    [RelayCommand]
    private void StopAutomation()
    {
        if (_session is null || !CanStopAutomation)
        {
            return;
        }

        Notice = null;
        OperationResult<Unit> requested = _sessions.RequestStop(
            _session.Id, AutomationStopMode.StopOperation);

        if (requested.IsFailure)
        {
            // Reported rather than swallowed. "Stop did nothing" is precisely the outcome an
            // operator must not be left guessing about.
            Notice = Describe(requested.Failure);
        }
    }

    /// <summary>
    /// Opens the Take Over confirmation. Changes nothing (§26).
    /// </summary>
    /// <remarks>
    /// Cannot leave a trace, because it has nothing to leave one with: it issues no command and
    /// reaches no service. The same property <see cref="CancelManualCrop"/> has, and for the
    /// same reason — an operator who opens a confirmation and thinks better of it has not
    /// changed anything.
    /// </remarks>
    [RelayCommand]
    private void BeginTakeOver()
    {
        if (!CanTakeOverAutomation)
        {
            return;
        }

        IsConfirmingTakeOver = true;
    }

    /// <summary>Closes the Take Over confirmation without taking over (§26).</summary>
    [RelayCommand]
    private void CancelTakeOver() => IsConfirmingTakeOver = false;

    /// <summary>
    /// Stops PrintFlow automation and leaves Meitu for the operator (§17, §19, §20).
    /// </summary>
    /// <remarks>
    /// The same one-line mechanism as <see cref="StopAutomation"/> with a different mode, and
    /// the difference is entirely in what the workflow layer then permits: a takeover resolves
    /// to "produce no input at all" from every phase, including Busy and including a blocking
    /// modal. This screen has no way to make it mean anything else.
    /// </remarks>
    [RelayCommand]
    private void ConfirmTakeOver()
    {
        IsConfirmingTakeOver = false;

        if (_session is null)
        {
            return;
        }

        Notice = null;
        OperationResult<Unit> requested = _sessions.RequestStop(
            _session.Id, AutomationStopMode.TakeOver);

        if (requested.IsFailure)
        {
            Notice = Describe(requested.Failure);
        }
    }

    /// <summary>
    /// Returns a handed-off session to automation, explicitly (§22).
    /// </summary>
    /// <remarks>
    /// An ordinary command through the ordinary path, unlike the two above: it changes session
    /// state, so it goes through the engine like everything else. It starts nothing — the
    /// operator still presses Run Step afterwards, which is what produces the new attempt
    /// against a fresh working copy.
    /// </remarks>
    [RelayCommand]
    private async Task SubmitManualResultAsync(CancellationToken cancellationToken)
    {
        if (!CanSubmitManualResult || IsBusy || _session?.CurrentStep is not { } step)
            return;
        string filter = step.Step == StepKind.BackgroundRemoval
            ? Strings.Session_ManualCutoutFilter : Strings.Session_ManualEnhancementFilter;
        string? selected = _filePicker?.PickSingleFile(Strings.Session_SubmitManualResult, filter);
        if (string.IsNullOrWhiteSpace(selected))
            return;
        await RunAsync(new WorkflowCommand.SubmitManualResult(step.Step, selected), cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    private Task ReenterAutomationAsync(CancellationToken cancellationToken) =>
        RunAsync(new WorkflowCommand.ReenterAutomation(), cancellationToken);

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
    /// Types a preset's maximum bounds into the boxes (Part 3C3B §5; Part B1A.2B §6).
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

    /// <summary>Uses one configured named recommendation without asking for millimetres.</summary>
    [RelayCommand]
    private async Task UsePresetAsync(
        SizePresetChoice? preset, CancellationToken cancellationToken)
    {
        if (preset is null || _session?.Sizing.CanSetPresetFitSize != true)
        {
            return;
        }

        IsChoosingCustomSize = false;
        _customPresetContext = null;
        NotifyCustomSizeStateChanged();
        await RunAsync(
            new WorkflowCommand.SetPresetFitSize(preset.Preset), cancellationToken)
            .ConfigureAwait(true);
    }

    /// <summary>Opens the one-edge form with no preset context. This records nothing.</summary>
    [RelayCommand]
    private void ChooseCustomSize()
    {
        if (_session?.Sizing.CanSetCustomTargetEdgeSize != true || IsBusy)
        {
            return;
        }

        _customPresetContext = null;
        SelectedTargetEdgeChoice = null;
        CustomMillimetresText = null;
        IsChoosingCustomSize = true;
        NotifyCustomSizeStateChanged();
    }

    /// <summary>Returns to the size step and opens custom sizing in the active preset's context.</summary>
    [RelayCommand]
    private async Task AdjustSizeAsync(CancellationToken cancellationToken)
    {
        SizePreset? preset = _session?.Sizing.Preset;
        if (preset is null || ReviewBoundsTarget is null || IsBusy)
        {
            return;
        }

        await ReturnToSizeAndEditAsync(
            preset, edge: null, millimetres: null, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Returns from an enlargement offer to editable controls without authorising it.</summary>
    [RelayCommand]
    private async Task ChangeSizeAsync(CancellationToken cancellationToken)
    {
        if (_session?.Sizing is not { } sizing || ReviewBoundsTarget is null || IsBusy)
        {
            return;
        }

        await ReturnToSizeAndEditAsync(
            sizing.Preset,
            sizing.RequestedTargetEdge,
            sizing.RequestedMillimetres,
            cancellationToken).ConfigureAwait(true);
    }

    private async Task ReturnToSizeAndEditAsync(
        SizePreset? preset,
        TargetEdge? edge,
        decimal? millimetres,
        CancellationToken cancellationToken)
    {
        if (ReviewBoundsTarget is not { } target)
        {
            return;
        }

        await RunAsync(
            new WorkflowCommand.ReturnToStep(target.Step), cancellationToken).ConfigureAwait(true);

        if (_session?.Sizing.CanSetCustomTargetEdgeSize != true)
        {
            return;
        }

        _customPresetContext = preset is { } named
            ? SizePresets.FirstOrDefault(choice => choice.Preset == named)
            : null;
        SelectedTargetEdgeChoice = edge is { } selected
            ? TargetEdgeChoices.Single(choice => choice.Edge == selected)
            : null;
        CustomMillimetresText = millimetres is { } value
            ? SizeText.MillimetresValue(value)
            : null;
        IsChoosingCustomSize = true;
        NotifyCustomSizeStateChanged();
    }

    /// <summary>Records exactly one operator-selected decimal target through the service path.</summary>
    [RelayCommand]
    private async Task ConfirmCustomSizeAsync(CancellationToken cancellationToken)
    {
        if (_session?.Sizing.CanSetCustomTargetEdgeSize != true ||
            SelectedTargetEdgeChoice is not { } edge ||
            !TryReadCustomMillimetres(out decimal millimetres))
        {
            Notice = Strings.Session_TargetSizeInvalid;
            return;
        }

        await RunAsync(
            new WorkflowCommand.SetCustomTargetEdgeSize(
                edge.Edge, millimetres, _customPresetContext?.Preset),
            cancellationToken).ConfigureAwait(true);

        if (_session?.Sizing.SizingMode == OperatorSizingMode.CustomTargetEdge)
        {
            IsChoosingCustomSize = false;
        }
    }

    /// <summary>Confirms only the exact enlargement the service currently offers.</summary>
    [RelayCommand]
    private async Task ContinueWithSizeAsync(CancellationToken cancellationToken)
    {
        if (_session is null || IsBusy || !CanAuthoriseEnlargement)
        {
            return;
        }

        IsBusy = true;
        try
        {
            Notice = null;
            OperationResult<SessionView> result = await _sessions
                .AuthoriseCurrentEnlargementAsync(
                    _session.Id,
                    _session.Sizing.EnlargementOfferId ?? Guid.Empty,
                    Environment.UserName,
                    cancellationToken)
                .ConfigureAwait(true);

            if (result.IsFailure)
            {
                Notice = Describe(result.Failure);
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

    /// <summary>
    /// Confirms the typed maximum bounds through the ordinary command path
    /// (§3; Epic 11400 Part B1A.2B §7).
    /// </summary>
    /// <remarks>
    /// The only thing this does with the text is turn it into a number. Whether those numbers are
    /// usable limits is <see cref="PrintDimensions.TryFromMillimetres"/>'s answer, and whether the
    /// session may accept them now is the engine's — neither rule is restated here, and nothing is
    /// silently adjusted to make it acceptable (§4, §7).
    /// <para>
    /// The plan itself is not built here and could not be: calculating one needs the upstream
    /// Revision's own pixels and its re-verified bytes, which is why <c>SessionService</c> owns
    /// it. This screen sends millimetres and reads back what the workflow layer decided (§3).
    /// </para>
    /// </remarks>
    [RelayCommand]
    private Task SetMaximumBoundsAsync(CancellationToken cancellationToken)
    {
        // A named preset still standing means the operator took the configured recommendation and
        // did not edit it, so the decision is recorded as the preset it is — and the millimetres
        // come from the verified preset rather than from this screen's text boxes. Editing either
        // box clears the pending preset to Custom through the change handlers below, and a custom
        // box goes the custom route (Epic 11400 Part B1A.2D §3, §19).
        if (_pendingPreset != SizePreset.Custom)
        {
            return RunAsync(new WorkflowCommand.SetPresetFitSize(_pendingPreset), cancellationToken);
        }

        if (!TryReadTypedDimensions(out PrintDimensions bounds))
        {
            Notice = Strings.Session_MaxBoundsInvalid;
            return Task.CompletedTask;
        }

        return RunAsync(new WorkflowCommand.SetPrintDimensions(bounds), cancellationToken);
    }

    /// <summary>
    /// Opens the ordinary return confirmation, aimed at the size step (§12, §13).
    /// </summary>
    /// <remarks>
    /// Deliberately not a second <c>SetPrintDimensions</c> route. Dimensions that need review are
    /// reconfirmed by going back to the step that owns them, which is what clears the pair and
    /// everything derived from it — and that is <c>ReturnToStep</c>, with its existing warning and
    /// its existing Cancel (§12, §13, §14).
    /// <para>
    /// The destination comes from <see cref="ReviewBoundsTarget"/>, which is a row the workflow
    /// layer offered. Pressing this when no such row exists does nothing rather than sending a
    /// command the engine would refuse.
    /// </para>
    /// <para>
    /// Selecting the target before opening the confirmation matters: assigning
    /// <see cref="SelectedReturnTarget"/> closes any confirmation that was open, so the order is
    /// what makes the panel appear at all.
    /// </para>
    /// </remarks>
    [RelayCommand]
    private void ReviewMaximumBounds()
    {
        if (ReviewBoundsTarget is not { } target || IsBusy)
        {
            return;
        }

        SelectedReturnTarget = target;
        IsConfirmingReturn = true;
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
        ReviewViewport.Reset();
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

        if (current.Facts.Format == ImageFormat.Psd) return;

        List<ArtefactPreviewPane> loaded = [];

        if (session.UpstreamArtefact is { } upstream && upstream.Facts.Format != ImageFormat.Psd)
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
        OnPropertyChanged(nameof(CanReviewMaximumBounds));
        OnPropertyChanged(nameof(CanAdjustSelectedPreset));
        OnPropertyChanged(nameof(CanAuthoriseEnlargement));
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

        // Always Custom. A typed pair is a custom fit box by definition, and a named preset never
        // reaches this method — SetMaximumBoundsAsync routes it to the command that resolves the
        // configured recommendation instead (Epic 11400 Part B1A.2D §3).
        return TryReadMillimetres(WidthMmText, out double widthMm)
            && TryReadMillimetres(HeightMmText, out double heightMm)
            && PrintDimensions.TryFromMillimetres(widthMm, heightMm, SizePreset.Custom, out dimensions);
    }

    private static bool TryReadMillimetres(string? text, out double millimetres) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out millimetres);

    private bool TryReadCustomMillimetres(out decimal millimetres) =>
        decimal.TryParse(
            CustomMillimetresText,
            NumberStyles.Number,
            CultureInfo.CurrentCulture,
            out millimetres) && millimetres > 0m;

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

    /// <summary>Forgets the unconfirmed bounds and branch, so the next output decides both afresh.</summary>
    private void ClearPendingDecisions()
    {
        WidthMmText = null;
        HeightMmText = null;
        _pendingPreset = SizePreset.Custom;
        SelectedTargetEdgeChoice = null;
        CustomMillimetresText = null;
        IsChoosingCustomSize = false;
        _customPresetContext = null;
        SelectedWhiteUnderbaseChoice = null;
        NotifyCustomSizeStateChanged();
    }

    private void NotifyCustomSizeStateChanged()
    {
        OnPropertyChanged(nameof(HasCustomPresetContext));
        OnPropertyChanged(nameof(CustomPresetContext));
        OnPropertyChanged(nameof(CustomPresetRecommendation));
    }

    /// <summary>Editing either box means the limits are the operator's, not a preset's.</summary>
    partial void OnWidthMmTextChanged(string? value)
    {
        _pendingPreset = SizePreset.Custom;
        OnPropertyChanged(nameof(PendingMaximumBounds));
    }

    /// <inheritdoc cref="OnWidthMmTextChanged" />
    partial void OnHeightMmTextChanged(string? value)
    {
        _pendingPreset = SizePreset.Custom;
        OnPropertyChanged(nameof(PendingMaximumBounds));
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

    /// <summary>
    /// Adopts the workflow layer's live automation state (Epic 11300 Part D2A §28).
    /// </summary>
    /// <remarks>
    /// Filtered to this session on purpose. The registry reports one run at a time for the whole
    /// process, and a screen showing session A must not offer to stop session B's run just
    /// because something somewhere is running.
    /// <para>
    /// Raised from whichever thread the run is on, so the property notifications go through
    /// <see cref="System.Windows.Threading.Dispatcher"/> when one is available. A test host has
    /// none, and there the direct call is correct.
    /// </para>
    /// </remarks>
    private void OnAutomationRuntimeChanged(object? sender, AutomationRuntimeView runtime)
    {
        AutomationRuntimeView adopted = _session is not null && runtime.SessionId == _session.Id
            ? runtime
            : AutomationRuntimeView.Idle;

        void Apply()
        {
            _runtime = adopted;

            // A takeover confirmation left standing after the run has ended would offer to take
            // over something that is no longer running, so it closes with the run (§26).
            if (!adopted.CanTakeOverAutomation)
            {
                IsConfirmingTakeOver = false;
            }

            NotifyAutomationRuntimeChanged();
        }

        System.Windows.Threading.Dispatcher? dispatcher =
            System.Windows.Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            dispatcher.Invoke(Apply);
        }
    }

    private void NotifyAutomationRuntimeChanged()
    {
        OnPropertyChanged(nameof(CanStopAutomation));
        OnPropertyChanged(nameof(CanTakeOverAutomation));
        OnPropertyChanged(nameof(IsStopping));
        OnPropertyChanged(nameof(StoppingNotice));
        StopAutomationCommand.NotifyCanExecuteChanged();
        BeginTakeOverCommand.NotifyCanExecuteChanged();
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

        // The run this screen was showing has finished by the time a new view arrives, so the
        // takeover confirmation goes with it — offering to take over a run that has ended would
        // be a button that cannot do what it says (Part D2A §26).
        IsConfirmingTakeOver = false;

        // Re-read rather than left as it was: the retained-external-state warning and the
        // re-entry offer both come from what was just persisted, and the run's own live state
        // is idle again by now.
        _runtime = _sessions.GetAutomationRuntime(session.Id);
        NotifyAutomationRuntimeChanged();
        OnPropertyChanged(nameof(HasRetainedExternalState));
        OnPropertyChanged(nameof(RetainedExternalStateNotice));
        OnPropertyChanged(nameof(CanReenterAutomation));

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

        // The named sizes the verified preset configures, rebuilt wholesale from what the
        // workflow layer offered. An installation whose preset cannot be verified offers none,
        // and the shortcut list is empty rather than falling back to paper sizes
        // (Epic 11400 Part B1A.2D §3, §4).
        SizePresets.Clear();
        foreach (PresetPrintRecommendation recommendation in session.Sizing.PresetRecommendations)
        {
            SizePresets.Add(new SizePresetChoice(recommendation));
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

        OnPropertyChanged(nameof(ConfirmedMaximumBounds));
        OnPropertyChanged(nameof(ConfirmedWhiteUnderbase));
        OnPropertyChanged(nameof(HasOutputs));

        OnPropertyChanged(nameof(HasArtefact));
        OnPropertyChanged(nameof(HasPsdSource));
        OnPropertyChanged(nameof(HasPdfSource));
        OnPropertyChanged(nameof(PdfSourceNotice));
        OnPropertyChanged(nameof(PsdSourceNotice));
        OnPropertyChanged(nameof(RunStepLabel));
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

        // The review wording follows the artefact, so it is refreshed with the review commands
        // that decide whether the panel is there at all (Epic 11400 Part C2B §23).
        OnPropertyChanged(nameof(IsProductionTiffReview));
        OnPropertyChanged(nameof(ReviewHeading));
        OnPropertyChanged(nameof(ApproveLabel));
        OnPropertyChanged(nameof(RejectLabel));
        OnPropertyChanged(nameof(TiffReviewSummary));

        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanSkip));
        OnPropertyChanged(nameof(CanHandOff));
        OnPropertyChanged(nameof(CanSubmitManualResult));
        OnPropertyChanged(nameof(IsManualProcessingResult));
        OnPropertyChanged(nameof(CanSetMaximumBounds));
        OnPropertyChanged(nameof(CanChooseFlexibleSize));
        OnPropertyChanged(nameof(HasPresetSizeSelection));
        OnPropertyChanged(nameof(HasCustomSizeSelection));
        OnPropertyChanged(nameof(CanAdjustSelectedPreset));
        OnPropertyChanged(nameof(CurrentSizeSummary));
        OnPropertyChanged(nameof(CurrentRecommendation));
        OnPropertyChanged(nameof(HasCurrentRecommendation));
        OnPropertyChanged(nameof(CurrentPresetContext));
        OnPropertyChanged(nameof(HasCurrentPresetContext));
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

        OnPropertyChanged(nameof(HasTrimBounds));
        OnPropertyChanged(nameof(TrimContentBoundsOrigin));
        OnPropertyChanged(nameof(TrimContentBoundsExtent));
        OnPropertyChanged(nameof(TrimContentBoundsSize));
        OnPropertyChanged(nameof(TrimAppliedBoundsOrigin));
        OnPropertyChanged(nameof(TrimAppliedBoundsExtent));
        OnPropertyChanged(nameof(TrimAppliedBoundsSize));

        OnPropertyChanged(nameof(CanAuthoriseAutomaticSelection));
        OnPropertyChanged(nameof(CanBeginAutomaticSelection));
        OnPropertyChanged(nameof(IsAutomaticSelectionAuthorised));
        OnPropertyChanged(nameof(IsAutomaticSelectionPending));
        OnPropertyChanged(nameof(AutomaticSelectionAuthorisedNotice));
        OnPropertyChanged(nameof(CanRunBackgroundRemoval));
        OnPropertyChanged(nameof(BackgroundRemovalAttemptAudit));
        OnPropertyChanged(nameof(HasBackgroundRemovalAttemptAudit));

        // The maximum-bound decision and everything the workflow layer derived from it
        // (Epic 11400 Part B1A.2B §8, §10, §11, §19). Every one of these reads the SessionView
        // that has just replaced the previous one, which is what makes a stale plan stop being
        // shown as active the moment the upstream changes.
        OnPropertyChanged(nameof(PendingMaximumBounds));
        OnPropertyChanged(nameof(HasPreparationPlan));
        OnPropertyChanged(nameof(PreparationModeText));
        OnPropertyChanged(nameof(HasPreparationModeText));
        OnPropertyChanged(nameof(HasPresetLimitNotice));
        OnPropertyChanged(nameof(PresetLimitNotice));
        OnPropertyChanged(nameof(NeedsEnlargementAuthority));
        OnPropertyChanged(nameof(CanAuthoriseEnlargement));
        OnPropertyChanged(nameof(EnlargementWarning));
        OnPropertyChanged(nameof(HasUsableEnlargementAuthority));
        OnPropertyChanged(nameof(PreparationLimitingEdge));
        OnPropertyChanged(nameof(HasPreparationLimitingEdge));
        OnPropertyChanged(nameof(PreparationProjectedSize));
        OnPropertyChanged(nameof(CanRunPhotoshopOutput));
        OnPropertyChanged(nameof(RunReadinessNotice));
        OnPropertyChanged(nameof(HasRunReadinessNotice));
        OnPropertyChanged(nameof(NeedsDimensionReview));
        OnPropertyChanged(nameof(HistoricalBounds));
        OnPropertyChanged(nameof(HasHistoricalBounds));
        OnPropertyChanged(nameof(CanReviewMaximumBounds));
        OnPropertyChanged(nameof(HasPreparationAttemptAudit));
        OnPropertyChanged(nameof(PreparationAttemptBounds));
        OnPropertyChanged(nameof(PreparationAttemptSelection));
        OnPropertyChanged(nameof(PreparationAttemptRecommendation));
        OnPropertyChanged(nameof(PreparationAttemptPresetContext));
        OnPropertyChanged(nameof(PreparationAttemptPresetOverride));
        OnPropertyChanged(nameof(PreparationAttemptResize));
        OnPropertyChanged(nameof(PreparationAttemptEnlargementConfirmation));
        OnPropertyChanged(nameof(PreparationAttemptMode));
        OnPropertyChanged(nameof(PreparationAttemptLimitingEdge));
        OnPropertyChanged(nameof(PreparationAttemptProjected));
        OnPropertyChanged(nameof(HasPreparationAttemptProjectionNotice));
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
        DisplayNames.Failure(failure),
        failure.Code);
}
