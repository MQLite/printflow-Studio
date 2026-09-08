using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Workflow.Services;

/// <summary>
/// One earlier step the operator may legally return to (Epic 11200 Part C3 §4).
/// </summary>
/// <remarks>
/// Carries the stable <see cref="StepKind"/> and its position, and no display text. The label
/// an operator reads is built in the shell by <c>DisplayNames.Step</c>, exactly as every other
/// step name on the screen is: enum values are stable English and are what gets persisted,
/// while what is shown is translated (MVP design §13.4). A localised string reaching down into
/// the workflow layer would be the one thing that could not be translated for a zh-CN
/// workstation.
/// <para>
/// The legality is not here either. Every instance of this type comes from
/// <see cref="IWorkflowEngine.AvailableReturnTargets"/>, which produced it by applying the real
/// <c>ReturnToStep</c> command — so the existence of a row <i>is</i> the legality, and there is
/// nothing for a view model to re-derive (§4, §8).
/// </para>
/// </remarks>
/// <param name="Step">The step to return to. Never displayed raw.</param>
/// <param name="Ordinal">Its position in the workflow, so the list reads in workflow order.</param>
public sealed record ReturnTargetView(StepKind Step, int Ordinal);

/// <summary>
/// The file the session screen is currently about, flattened for display
/// (Epic 11100 Part 3C3A §3).
/// </summary>
/// <remarks>
/// Carries the workspace file <i>name</i> and never a path. The operator's original file lives
/// outside the workspace and its location is not operator information; the workspace layout is
/// not either. A name, the structural facts and the hash are what identify a result during a
/// review (MVP design §13.2).
/// <para>
/// <see cref="IsCurrentStepResult"/> is what makes an approval bindable: it is true only when
/// this artefact is the current step's <b>own</b> result rather than the upstream file the step
/// is about to consume. The review UI approves the hash of the artefact it displayed, so the
/// screen needs to know which of the two it is showing (§10).
/// </para>
/// </remarks>
/// <param name="RevisionId">Identity of the Revision shown. Displayed in short form only.</param>
/// <param name="FileName">The workspace file name, including extension.</param>
/// <param name="Facts">Format, pixel dimensions, DPI and hash, frozen at validation time.</param>
/// <param name="IsCurrentStepResult">
/// Whether this is the current step's own result (true) or the upstream input it will work
/// from (false).
/// </param>
/// <param name="SourceRevisionId">
/// The Revision this one was derived from, straight off <see cref="Revision.SourceRevisionId"/>
/// (Epic 11200 Part C1 §9).
/// </param>
/// <remarks>
/// <see cref="SourceRevisionId"/> is the real derivation edge and not an inference from step
/// order: it is what <see cref="SessionView.UpstreamArtefact"/> is resolved through, so a
/// before/after comparison is a fact about the lineage rather than a guess about which step
/// probably ran first.
/// </remarks>
public sealed record ArtefactView(
    RevisionId RevisionId,
    string FileName,
    FileFacts Facts,
    bool IsCurrentStepResult,
    RevisionId? SourceRevisionId)
{
    /// <summary>The hash an approval or rejection of this artefact must be bound to.</summary>
    public Sha256 Sha256 => Facts.Sha256;

    public bool IsManualProcessingResult { get; init; }

    internal static ArtefactView From(Revision revision, bool isCurrentStepResult) => new(
        revision.Id, revision.File.FileName, revision.Facts, isCurrentStepResult, revision.SourceRevisionId)
        { IsManualProcessingResult = revision.Operation == OperationKind.ManualResultImport };
}

/// <summary>
/// One production output this session has already produced, flattened for display
/// (Epic 11100 Part 3C3B §15).
/// </summary>
/// <remarks>
/// Deliberately the smallest thing that lets an operator see Output A still exists while
/// Output B is being made: what size it is, which W1 branch it was made with, whether it was
/// reviewed, whether it is still valid, and what the file is called. No path, no hash, no
/// preset signature, no attempt history — this is a "what have I got" line, not a history
/// browser.
/// <para>
/// <see cref="ReviewState"/> is the cached projection rather than the authority (the
/// <c>ReviewDecision</c> row is), which is exactly what a list wants: it is a label, and no
/// decision is ever taken from it.
/// </para>
/// </remarks>
/// <param name="IsValid">
/// False once an upstream change invalidated this output. Shown rather than hidden: an
/// operator needs to know a size they produced no longer reflects the design.
/// </param>
/// <param name="Area">
/// Which workspace area this output's file currently lives in: <c>Working</c> while it is still
/// the candidate an operator is reviewing, <c>Approved</c> once approval has promoted it
/// (Epic 11400 Part C2B §24). The area, never a path — the screen says where the deliverable is,
/// and the workspace stays the only thing that knows what that means on disk.
/// </param>
/// <param name="IsRecycled">
/// True once a rejected output's file has been sent to the Windows Recycle Bin. The row itself
/// stays — the decision, the hash, the size and the branch remain auditable — but the bytes are
/// no longer available, and the screen must not offer them as though they were (§16, §25).
/// </param>
public sealed record PrintOutputView(
    PrintOutputId Id,
    PrintDimensions Dimensions,
    WhiteUnderbaseBranch Branch,
    ReviewState ReviewState,
    bool IsValid,
    string FileName,
    WorkspaceArea Area,
    bool IsRecycled)
{
    internal static PrintOutputView From(PrintOutput output) => new(
        output.Id,
        output.Dimensions,
        output.Branch,
        output.ReviewState,
        output.IsValid,
        output.File.FileName,
        output.File.Area,
        output.RecycledAtUtc is not null);
}

/// <summary>
/// The immutable preparation plan the attempt that produced the artefact on screen actually ran
/// under (Epic 11400 Part B1A.2B §19, §20).
/// </summary>
/// <remarks>
/// The third member of the family <c>CurrentTrimParameters</c> and
/// <see cref="SessionView.BackgroundRemovalAttemptDecision"/> already belong to, and it exists for
/// the same reason they do: the session's <i>pending</i> plan answers "what would the next run be
/// allowed to do", while this answers "what did the result the operator is reviewing actually run
/// under". A review panel that showed the pending plan would relabel history every time the
/// session's next-run plan moved — after a reject, a retry against different limits, or an
/// Add Another Size.
/// <para>
/// A flattened projection of <c>ProcessingAttempt.PrintPreparationPlan</c> rather than the record
/// itself, so the shell never receives <c>SourceRevisionId</c> or <c>SourceSha256</c> — the raw
/// materials of a second staleness rule. What is here is exactly what an audit line says out loud.
/// </para>
/// <para>
/// Every field is a <b>projection</b>. Nothing in this slice reads geometry back from Photoshop,
/// so there is deliberately no <c>Actual</c> anything: <see cref="ProjectedPixelWidth"/> and
/// <see cref="ProjectedPixelHeight"/> are the planning evidence the plan was validated with, and
/// the real Photoshop-returned geometry is B1A.3's to record (§19).
/// </para>
/// </remarks>
/// <param name="Semantics">
/// The reading the preparation was written under — <c>MaxBoundsV1</c> or <c>TargetEdgeV1</c>. Never
/// <c>LegacyExactPair</c>, because a legacy pair never produced a plan for an attempt to snapshot.
/// Stated rather than assumed, so an audit line says which contract it is quoting.
/// </param>
/// <param name="MaxWidthMm">
/// The fit box that attempt ran under, or null for a target-edge run. Not a width target.
/// </param>
/// <param name="MaxHeightMm">The other half of that box. Not a height target.</param>
/// <param name="Mode">
/// Whether a maximum-bound run resampled at all, or null for a target-edge run — whose equivalent
/// is <paramref name="ResizeDirection"/>, which has three answers rather than two.
/// </param>
/// <param name="LimitingEdge">
/// The single edge the run was allowed to give Photoshop, or <c>None</c>. Selected by the Domain
/// calculation when the plan was made, and never an operator choice.
/// </param>
/// <param name="LimitingValueMm">The millimetres written to that edge, or null when none was.</param>
/// <param name="RequestedTargetEdge">
/// The edge the operator actually asked for on a target-edge run — <c>LongEdge</c> included, so an
/// audit line can say what was requested as well as what it resolved to. Null for a preset fit.
/// </param>
/// <param name="RequestedMillimetres">The operator's exact request, or null for a preset fit.</param>
/// <param name="PresetOverride">
/// Whether that run was an explicit override of a named preset's recommendation. Emphatically not
/// the same fact as an authorised enlargement (§11).
/// </param>
/// <param name="ResizeDirection">
/// Whether that run set resolution only, shrank, or enlarged. Null for a maximum-bound run, which
/// could never enlarge.
/// </param>
/// <param name="ResizePolicy">
/// The neutral resampling policy the run carried. Never a Photoshop DOM value (§26).
/// </param>
/// <param name="WasAuthorisedEnlargement">
/// Whether that run added pixels under an explicit operator authority. False for everything else,
/// including a run that merely exceeded a preset recommendation.
/// </param>
/// <param name="ProjectedPixelWidth">Planning evidence only. Never a Photoshop result.</param>
/// <param name="ProjectedPixelHeight">The other half of that evidence.</param>
/// <param name="ProductionDpi">The fixed production resolution. Never operator-selected.</param>
/// <param name="IsFakeProjection">
/// Whether the adapters wired into this installation are deterministic doubles, so a screen can
/// say plainly that Photoshop was not run. True is the state this slice ships in; it is reported
/// rather than inferred by a view model reading configuration (§21).
/// </param>
public sealed record PrintPreparationAttemptView(
    PrintDimensionSemantics Semantics,
    SizePreset? Preset,
    PresetRecommendationKind? RecommendationKind,
    decimal? RecommendationMaxWidthMm,
    decimal? RecommendationMaxHeightMm,
    double? MaxWidthMm,
    double? MaxHeightMm,
    PrintPreparationMode? Mode,
    LimitingEdge LimitingEdge,
    double? LimitingValueMm,
    TargetEdge? RequestedTargetEdge,
    decimal? RequestedMillimetres,
    bool PresetOverride,
    ResizeDirection? ResizeDirection,
    PhotoshopResizeMode ResizePolicy,
    bool WasAuthorisedEnlargement,
    int ProjectedPixelWidth,
    int ProjectedPixelHeight,
    int ProductionDpi,
    bool IsFakeProjection)
{
    /// <summary>Whether that run would have resampled pixels at all.</summary>
    public bool RequiresShrink => Mode == PrintPreparationMode.ProportionalShrink;

    internal static PrintPreparationAttemptView From(
        PhotoshopPreparation preparation, AdapterExecutionMode processingMode)
    {
        FitWithinBoundsPreparation? bounds = preparation as FitWithinBoundsPreparation;
        TargetEdgePreparation? target = preparation as TargetEdgePreparation;

        // The recommendation the run actually recorded, from whichever form it was made in. An
        // attempt written before the post-final A5 correction recorded none for an ordinary preset
        // fit, and falls through to the historical reading below (§19).
        PresetPrintRecommendation? recommendation =
            target?.Plan.Selection.Recommendation ?? bounds?.Selection?.Recommendation;
        SizePreset? preset = bounds?.Plan.LimitKind is { } limit and not SizePreset.Custom
            ? limit
            : recommendation?.Preset;

        return new PrintPreparationAttemptView(
            preparation.Semantics,
            preset,

            // The historical reading, and only for a run that stored no recommendation of its own.
            // Before v1.15.0 the two configured forms were a box and a long edge, and a long edge
            // was written as the square box of that side — so equal bounds meant a long edge, and
            // for those rows it still does. That is what a v1.14 A5 attempt recorded and what it
            // must keep saying: "recommended long edge 135 mm", never relabelled as a short edge
            // by a later preset change (§12, §19).
            recommendation?.Kind ?? (bounds is null
                ? null
                : bounds.Plan.MaxWidthMm == bounds.Plan.MaxHeightMm
                    ? PresetRecommendationKind.MaximumLongEdge
                    : PresetRecommendationKind.MaximumBox),
            recommendation?.MaxWidthMm ?? (preset is null ? null : (decimal)bounds!.Plan.MaxWidthMm),
            recommendation?.MaxHeightMm ?? (preset is null ? null : (decimal)bounds!.Plan.MaxHeightMm),
            bounds?.Plan.MaxWidthMm,
            bounds?.Plan.MaxHeightMm,
            bounds?.Plan.Mode,
            preparation.PhotoshopEdge,
            preparation.PhotoshopEdgeValueMm,
            target?.Plan.Projection.SelectedTargetEdge,
            target?.Plan.Projection.RequestedMillimetres,
            target?.Plan.Selection.PresetOverridden ?? false,
            target?.Plan.Projection.Direction,
            preparation.ResizePolicy,
            target?.IsAuthorisedEnlargement ?? false,
            preparation.ProjectedPixelWidth,
            preparation.ProjectedPixelHeight,
            preparation.ProductionDpi,
            processingMode == AdapterExecutionMode.Fake);
    }
}

/// <summary>
/// Everything the next Photoshop run's size decision is, and everything the operator may still
/// decide about it (Epic 11400 Part B1A.2D §28).
/// </summary>
/// <remarks>
/// The seam the flexible-size operator UI will bind to, and the whole of what it is allowed to
/// know. Every value is reported by the workflow layer: which mode was chosen, which
/// recommendation it was chosen against, what the request projects to, and whether a run could
/// start. A screen calculates none of it — no fit, no scale, no limiting edge, no comparison of a
/// Revision or a hash — because a screen that recalculated any of them could offer a control the
/// engine would refuse (§28).
/// <para>
/// What is deliberately <b>not</b> here: the source SHA-256, the Revision binding, any Photoshop
/// DOM identifier, and the rational arithmetic behind the projection. Those are the raw materials
/// of a second staleness rule and a second sizing implementation, and the read model exists so
/// neither can be built in the shell.
/// </para>
/// <para>
/// Every value reflects the <b>usable</b> decision, never a raw stored one. A session can hold a
/// plan calculated against content that has since been replaced, and reporting its projection as
/// the current one would present a stale record as readiness.
/// </para>
/// </remarks>
/// <param name="PresetRecommendations">
/// The named sizes this installation's verified preset configures, with their executable limits.
/// Empty when no preset is verified — never a fallback to nominal paper sizes (§3, §4).
/// </param>
/// <param name="SizingMode">
/// Which of the two accepted ways the current size was chosen, or null for a typed custom fit box
/// and for a session that has not chosen yet.
/// </param>
/// <param name="Preset">The named preset behind the decision, or null when there is none.</param>
/// <param name="RecommendationKind">Whether that preset configures a box or a long edge.</param>
/// <param name="RecommendationMaxWidthMm">The configured limit the operator was shown.</param>
/// <param name="RecommendationMaxHeightMm">The other half of it; equal for a long edge.</param>
/// <param name="PresetOverride">
/// Whether the operator explicitly replaced that recommendation with their own edge. Separate from
/// every enlargement fact below, and deliberately so (§11).
/// </param>
/// <param name="RequestedTargetEdge">The edge the operator asked for, or null for a preset fit.</param>
/// <param name="RequestedMillimetres">Their exact request, or null for a preset fit.</param>
/// <param name="ResolvedLimitingEdge">
/// The single edge Photoshop would be given. Resolved by the Domain from the source's own pixels —
/// a <c>LongEdge</c> request becomes a concrete Width or Height — and never an operator choice.
/// </param>
/// <param name="ProjectedPixelWidth">Planning evidence only. Never a Photoshop result.</param>
/// <param name="ProjectedPixelHeight">The other half of that evidence.</param>
/// <param name="ResizeDirection">
/// Whether the next run would set resolution only, shrink, or enlarge. Null when no target-edge
/// plan is usable; a maximum-bound plan reports <c>PreparationMode</c> instead, which has no
/// enlarging answer to give.
/// </param>
/// <param name="ProjectedScalePercent">
/// The exact projected scale as a percentage, for display. The authoritative form is the reduced
/// integer ratio the Domain keeps; this is that ratio rendered, never the thing compared (§7).
/// </param>
/// <param name="PresetLimitExceeded">
/// Whether the request goes past the recommendation it overrode. Says nothing about pixels.
/// </param>
/// <param name="SourceCapacityExceeded">
/// Whether the request needs more pixels than the source holds at 300 ppi. Says nothing about
/// presets. The two are reported separately because they are separate facts, and a screen that
/// merged them would warn about the wrong thing (§11).
/// </param>
/// <param name="NeedsEnlargementAuthority">
/// Whether a current, correctly sized decision is waiting only on an explicit confirmation.
/// </param>
/// <param name="HasUsableEnlargementAuthority">
/// Whether a granted confirmation actually covers the plan on offer. A retry over unchanged
/// content keeps this true without anything re-granting it (§30).
/// </param>
/// <param name="CanAuthoriseEnlargement">
/// Whether the confirmation may be given right now — answered by the engine's own probe, so an
/// offered control and an accepted command cannot disagree.
/// </param>
/// <param name="CanSetPresetFitSize">Whether a named preset may be recorded right now.</param>
/// <param name="CanSetCustomTargetEdgeSize">Whether a custom edge may be recorded right now.</param>
public sealed record FlexibleSizeView(
    IReadOnlyList<PresetPrintRecommendation> PresetRecommendations,
    OperatorSizingMode? SizingMode,
    SizePreset? Preset,
    PresetRecommendationKind? RecommendationKind,
    decimal? RecommendationMaxWidthMm,
    decimal? RecommendationMaxHeightMm,
    bool PresetOverride,
    TargetEdge? RequestedTargetEdge,
    decimal? RequestedMillimetres,
    LimitingEdge? ResolvedLimitingEdge,
    int? ProjectedPixelWidth,
    int? ProjectedPixelHeight,
    ResizeDirection? ResizeDirection,
    decimal? ProjectedScalePercent,
    bool PresetLimitExceeded,
    bool SourceCapacityExceeded,
    bool NeedsEnlargementAuthority,
    bool HasUsableEnlargementAuthority,
    bool CanAuthoriseEnlargement,
    Guid? EnlargementOfferId,
    bool CanSetPresetFitSize,
    bool CanSetCustomTargetEdgeSize)
{
    internal static FlexibleSizeView From(
        WorkflowSnapshot snapshot,
        IReadOnlyList<CommandKind> availableCommands,
        IReadOnlyList<PresetPrintRecommendation> presetRecommendations,
        Guid? enlargementOfferId)
    {
        // The selection is reported only when the decision behind it is still usable. A session
        // holding a selection whose source has been replaced is a session with no current size,
        // and saying otherwise would put a stale record on screen as though it were readiness.
        PhotoshopPreparation? usable = snapshot.UsablePhotoshopPreparation;
        TargetEdgePrintPreparationPlan? targetEdge = snapshot.UsableTargetEdgePlan;
        FlexibleSizeSelection? selection =
            usable is not null || targetEdge is not null ? snapshot.SizeSelection : null;

        return new FlexibleSizeView(
            presetRecommendations,
            selection?.Mode,
            selection?.Recommendation?.Preset,
            selection?.Recommendation?.Kind,
            selection?.Recommendation?.MaxWidthMm,
            selection?.Recommendation?.MaxHeightMm,
            selection?.PresetOverridden ?? false,
            targetEdge?.Projection.SelectedTargetEdge,
            targetEdge?.Projection.RequestedMillimetres,
            targetEdge?.Projection.PhotoshopTargetEdge ?? usable?.PhotoshopEdge,
            targetEdge?.Projection.ProjectedPixelWidth ?? usable?.ProjectedPixelWidth,
            targetEdge?.Projection.ProjectedPixelHeight ?? usable?.ProjectedPixelHeight,
            targetEdge?.Projection.Direction,
            targetEdge?.Projection.ProjectedScale.Percentage,
            targetEdge?.PresetLimitExceeded ?? false,
            targetEdge?.SourceCapacityExceeded ?? false,
            snapshot.NeedsEnlargementAuthority,
            snapshot.HasUsableEnlargementAuthority,

            // The engine's own probe, exactly as CanRunPhotoshopOutput is. Asking whether the
            // command is offered is asking whether it would be accepted — plan, source binding
            // and exact target included (§28).
            availableCommands.Contains(CommandKind.AuthoriseEnlargement),
            enlargementOfferId,
            availableCommands.Contains(CommandKind.SetPresetFitSize),
            availableCommands.Contains(CommandKind.SetCustomTargetEdgeSize));
    }
}

/// <summary>
/// A flattened, UI-safe read model for one session (Epic 11100 plan §9.2).
/// </summary>
/// <remarks>
/// The UI binds to this and to nothing else: no <see cref="WorkflowSnapshot"/>, no
/// <see cref="Domain.Sessions.SessionStep"/> setter, no adapter reference. Available commands
/// come from the engine's own <c>AvailableCommands</c>, so a button is enabled by the same rule
/// that will accept the click — one source of truth for legality (MVP design invariant 12).
/// </remarks>
/// <param name="CurrentStep">
/// The step the operator is expected to act on, copied from
/// <see cref="WorkflowSnapshot.CurrentStep"/> so the UI never restates the "first step that is
/// neither Approved nor Skipped" rule. Null once every step is finished.
/// </param>
/// <param name="CurrentArtefact">
/// The file the current step is about: its own validated result if it has one, otherwise the
/// upstream Revision it would consume. Null before anything has been validated.
/// </param>
/// <param name="ProcessingMode">
/// Whether the adapters wired into this installation are deterministic doubles or real
/// automation. Reported here rather than read from the container by a view model, so the
/// screen can warn that output is synthetic without ever referencing an adapter
/// (Part 3C3A §8).
/// </param>
/// <param name="Outputs">
/// Every production output this session holds, oldest first, so an operator making Output B
/// can still see Output A (Part 3C3B §15). Empty for a workflow that produces no TIFF.
/// </param>
/// <param name="ProducesPrintOutput">
/// Whether this session's workflow ends in a production TIFF. Answered here, where the
/// workflow definition lives, rather than by a view model comparing step kinds — a screen that
/// worked that out for itself would be a second copy of the catalogue (Part 3C3B §10).
/// </param>
/// <param name="UpstreamArtefact">
/// The Revision <see cref="CurrentArtefact"/> was derived from, when it is a step result with a
/// source that still exists (Epic 11200 Part C1 §9).
/// </param>
/// <param name="CurrentStepFailure">
/// The failure code of the current step's most recent attempt, when that step is
/// <see cref="StepState.Failed"/> (Part C1 §17).
/// </param>
/// <param name="CanManualCrop">
/// Whether an operator-selected crop is a legal next action, answered by
/// <see cref="ManualCropEligibility"/> (Epic 11200 Part C2 §3).
/// </param>
/// <param name="ReturnTargets">
/// The earlier steps <c>ReturnToStep</c> would accept right now, in workflow order
/// (Epic 11200 Part C3 §4, §8). Empty when returning is not legal.
/// </param>
/// <param name="TrimMargin">
/// The margin the next deterministic Trim attempt will run with — the session's current
/// decision, which starts at <see cref="Domain.Trimming.TrimMargin.Tight"/> (Part C3 §10).
/// </param>
/// <param name="CanSetTrimParameters">
/// Whether the margin controls should be offered: the automatic trim is about to run, and this
/// file is not one the automatic trim has already refused (Part C3 §9, §17).
/// </param>
/// <param name="CurrentTrimParameters">
/// The margin the attempt that produced <see cref="CurrentArtefact"/> actually ran with, or
/// null when that artefact was not produced by a deterministic trim (Part C3 §18).
/// </param>
/// <param name="ArtefactTrimGeometry">
/// The rectangles the trim that produced <see cref="CurrentArtefact"/> established: what the
/// alpha scan detected, and what was actually cropped out (SCRUM-11081 §12, §15). Null when that
/// artefact was not produced by a deterministic trim — including a manual crop, whose rectangle
/// a human drew — and null for every trim attempt written before migration 0009, whose geometry
/// was never recorded.
/// <para>
/// Deliberately scoped to <b>the artefact</b> rather than to the step's own result, which is
/// where it differs from <see cref="CurrentTrimParameters"/>. Two different questions are being
/// asked of the same fact. During the Trim review it is "how was this file made", and
/// <see cref="CurrentTrimGeometry"/> narrows to exactly that. One step later it is "where did
/// the artwork sit in the original", which is what SCRUM-11094 needs on screen while a print
/// size is being decided — and there the trimmed file is the step's <i>input</i>, so a
/// result-only reading would report nothing. The detected extent of the file being sized is a
/// legitimate thing to state at that point; the crop parameters of an upstream step are not,
/// which is why only this one widens.
/// </para>
/// <para>
/// The domain <see cref="TrimGeometry"/> itself rather than a flattened projection, because
/// unlike a preparation plan it holds nothing a screen must not see: two rectangles in source
/// pixels, no Revision id and no hash. One shared shape also means the Trim review panel and any
/// later Print Dimensions display read the same detected extent rather than each deriving one
/// (§15).
/// </para>
/// </param>
/// <param name="BackgroundRemovalDecision">
/// The reviewed-content decision that <b>currently</b> authorises a Background Removal run, or
/// <see cref="Domain.Sessions.BackgroundRemovalDecision.Unspecified"/> when nothing does
/// (Epic 11300 Part C2B1 §22).
/// </param>
/// <param name="BackgroundRemovalDecisionRevisionId">
/// The Revision that decision was granted over, or null when there is no usable decision.
/// </param>
/// <param name="CanSetBackgroundRemovalDecision">
/// Whether the reviewed-content authorisation may be recorded right now (§22).
/// </param>
/// <param name="CanRunBackgroundRemoval">
/// Whether Background Removal would actually start if asked — decision included (§23).
/// </param>
/// <param name="BackgroundRemovalAttemptDecision">
/// The decision the attempt that produced <see cref="CurrentArtefact"/> actually ran under, or
/// <see cref="Domain.Sessions.BackgroundRemovalDecision.Unspecified"/> when that artefact was
/// not produced by an authorised Background Removal (Epic 11300 Part C2B2 §15, §16).
/// </param>
/// <param name="BackgroundRemovalAttemptReviewedRevisionId">
/// The Revision that attempt's authority was granted over, or null when it had none.
/// </param>
/// <param name="DimensionSemantics">
/// What this session's recorded millimetres mean — a legacy exact pair, or maximum bounds under
/// the accepted contract. Null when no dimensions are recorded (Epic 11400 Part B1A.2A §19).
/// </param>
/// <param name="MaxWidthMm">
/// The maximum width of the <b>currently usable</b> plan's fit box, or null when nothing is
/// usable. A legacy pair reports null here while still appearing in <see cref="SessionView.Dimensions"/>.
/// </param>
/// <param name="MaxHeightMm">The maximum height of the currently usable plan's fit box.</param>
/// <param name="PreparationMode">
/// Whether the next Photoshop run would set resolution only or shrink proportionally. Null when
/// no plan is usable.
/// </param>
/// <param name="LimitingEdge">
/// The single edge the plan would give Photoshop, selected automatically by
/// <c>FitWithinBounds</c>. Never an operator choice, and null when no plan is usable.
/// </param>
/// <param name="ProjectedPixelWidth">
/// Planning evidence that the selected edge fits the other bound. Never a Photoshop target and
/// never an actual result (§20).
/// </param>
/// <param name="ProjectedPixelHeight">The other half of that planning evidence.</param>
/// <param name="NeedsDimensionReview">
/// Whether recorded dimensions exist that cannot be executed — a legacy exact pair, or a plan
/// whose source has since changed — so the operator must reconfirm the limits (§10).
/// </param>
/// <param name="CanSetMaximumBounds">Whether maximum bounds may be recorded right now.</param>
/// <param name="CanRunPhotoshopOutput">
/// Whether Photoshop output would actually start if asked — plan included (§15).
/// </param>
/// <param name="AttemptPreparation">
/// The immutable plan the attempt that produced <see cref="CurrentArtefact"/> ran under, or null
/// when that artefact was not produced by a planned Photoshop output
/// (Epic 11400 Part B1A.2B §19).
/// </param>
/// <remarks>
/// <see cref="CanManualCrop"/> is reported rather than left to the screen because it depends on
/// attempt history the UI does not have and must not reconstruct. It is the same predicate
/// <see cref="SessionService"/> enforces, so an offered control and an accepted command cannot
/// disagree.
/// <para>
/// <see cref="CanSetTrimParameters"/> and <see cref="CurrentTrimParameters"/> are here for the
/// same reason. The first needs attempt history to know the file is not on the manual path; the
/// second needs the attempt row that produced the result on screen. Neither is derivable from
/// anything the shell can see, and a screen that guessed would be guessing about what an
/// operator is being asked to approve.
/// </para>
/// <para>
/// <see cref="BackgroundRemovalAttemptDecision"/> is the third of the same family, and the
/// distinction it draws is the point of it: <see cref="BackgroundRemovalDecision"/> answers
/// "what would the <i>next</i> run be allowed to do", while this answers "what did the result
/// on screen actually run under". A review that showed the first in place of the second would
/// relabel history every time the session's pending authority changed (Part C2B2 §15).
/// </para>
/// </remarks>
public sealed record SessionView(
    SessionId Id,
    WorkflowType WorkflowType,
    OutputName OutputName,
    SessionState State,
    IReadOnlyList<SessionStep> Steps,
    SessionStep? CurrentStep,
    PrintDimensions? Dimensions,
    WhiteUnderbaseBranch? WhiteUnderbaseBranch,
    IReadOnlyList<CommandKind> AvailableCommands,
    ArtefactView? CurrentArtefact,
    AdapterExecutionMode ProcessingMode,
    IReadOnlyList<PrintOutputView> Outputs,
    bool ProducesPrintOutput,
    ArtefactView? UpstreamArtefact,
    FailureCode? CurrentStepFailure,
    bool CanManualCrop,
    IReadOnlyList<ReturnTargetView> ReturnTargets,
    TrimMargin TrimMargin,
    bool CanSetTrimParameters,
    TrimMargin? CurrentTrimParameters,
    TrimGeometry? ArtefactTrimGeometry,
    BackgroundRemovalDecision BackgroundRemovalDecision,
    RevisionId? BackgroundRemovalDecisionRevisionId,
    bool CanSetBackgroundRemovalDecision,
    bool CanRunBackgroundRemoval,
    BackgroundRemovalDecision BackgroundRemovalAttemptDecision,
    RevisionId? BackgroundRemovalAttemptReviewedRevisionId,
    AutomationStopAudit? LastAutomationStop,
    PrintDimensionSemantics? DimensionSemantics,
    double? MaxWidthMm,
    double? MaxHeightMm,
    PrintPreparationMode? PreparationMode,
    LimitingEdge? LimitingEdge,
    int? ProjectedPixelWidth,
    int? ProjectedPixelHeight,
    bool NeedsDimensionReview,
    bool CanSetMaximumBounds,
    bool CanRunPhotoshopOutput,
    PrintPreparationAttemptView? AttemptPreparation,
    FlexibleSizeView Sizing)
{
    /// <summary>Post-completion maintenance outcome; failure leaves State = Completed.</summary>
    public SessionCleanupResult? CompletionCleanup { get; init; }

    public ManualCropGeometry? ArtefactManualCropGeometry { get; init; }
    public ManualCropGeometry? CurrentManualCropGeometry =>
        CurrentArtefact is { IsCurrentStepResult: true } ? ArtefactManualCropGeometry : null;
    public bool HasManualCropGeometry => CurrentManualCropGeometry is not null;

    public bool CanSubmitManualResult => AvailableCommands.Contains(CommandKind.SubmitManualResult);

    public bool CanKeepOriginalExtent => AvailableCommands.Contains(CommandKind.KeepOriginalExtent);

    /// <summary>Explicit persisted Trim outcome, never inferred from the absence of a file.</summary>
    public bool OriginalExtentRetained => Steps.Any(step => step.Step == StepKind.Trim && step.State == StepState.Skipped);

    public ImageFormat? OriginalSourceFormat { get; init; }
    public PdfInspection? PdfInspection { get; init; }

    /// <summary>
    /// The name of the file the operator imported, exactly as the root Revision holds it
    /// (SCRUM-11078).
    /// </summary>
    /// <remarks>
    /// Reported so a screen can show the operator <i>which file</i> it is about to process
    /// beside the output name it will produce. The two are deliberately separate: the output
    /// name is editable and never renames the source (MVP design invariant 1), so a screen that
    /// identified the source by the output name would show the operator their own edit back as
    /// if it were the file on disk.
    /// <para>
    /// This is the workspace copy's file name — the same name the operator chose, since
    /// <c>IWorkspace.ImportSourceAsync</c> copies under it — and never a path. Null only for a
    /// session whose import has not produced a root Revision.
    /// </para>
    /// </remarks>
    public string? SourceFileName { get; init; }

    /// <summary>
    /// The root Revision — the imported source — so a screen can preview it before any step has
    /// run (SCRUM-11078).
    /// </summary>
    /// <remarks>
    /// The identifier only. Reaching the pixels still goes through
    /// <see cref="IArtefactPreviewService"/>, which is the single read-only image seam and is
    /// what keeps "the operator looked at the file" from being an action on the session
    /// (Epic 11200 Part C1 §3, §29).
    /// </remarks>
    public RevisionId? RootRevisionId { get; init; }

    /// <summary>Whether the operator has any legal earlier step to return to (§4).</summary>
    public bool CanReturnToStep => ReturnTargets.Count > 0;

    /// <summary>
    /// Whether the operator should be warned about what the external application may still be
    /// holding or doing (Epic 11300 Part D2A §21, §37).
    /// </summary>
    /// <remarks>
    /// False after a stop whose signed cancel positively took effect, and true after one that
    /// could not resolve a cancel — the honest split. PrintFlow cannot establish that the
    /// external application is finished or safe once it has stopped looking at it, so there is
    /// no state here that says so.
    /// </remarks>
    public bool HasRetainedExternalState =>
        LastAutomationStop?.OperatorActionMayBeRequired == true;

    /// <summary>
    /// Whether the operator must explicitly re-enter automation before this session can be
    /// driven again (Epic 11300 Part D2A §22, §30).
    /// </summary>
    public bool RequiresAutomationReentry =>
        State == SessionState.HandedOff && AvailableCommands.Contains(CommandKind.ReenterAutomation);

    /// <summary>
    /// Whether the artefact on screen carries deterministic trim parameters worth stating
    /// (§18).
    /// </summary>
    public bool HasTrimParameters => CurrentTrimParameters is not null;

    /// <summary>
    /// The crop rectangles for the result the operator is being asked to approve
    /// (SCRUM-11081 §12, §13).
    /// </summary>
    /// <remarks>
    /// <see cref="ArtefactTrimGeometry"/> narrowed to the step's own result, which is the
    /// scoping every other attempt-audit member on this record uses. While the screen shows the
    /// file a step is about to <i>consume</i>, a line beside the review claiming "this is how it
    /// was cropped" would be describing work an earlier step did.
    /// </remarks>
    public TrimGeometry? CurrentTrimGeometry =>
        CurrentArtefact is { IsCurrentStepResult: true } ? ArtefactTrimGeometry : null;

    /// <summary>
    /// Whether the result on screen carries the crop rectangles the trim that produced it
    /// established (SCRUM-11081 §12, §13).
    /// </summary>
    /// <remarks>
    /// False, and therefore no bounds display, for a manual crop, for an artefact that is not a
    /// trim result at all, and for a trim attempt recorded before the geometry was persisted.
    /// A screen must not fill that gap from the output's pixel dimensions: those give the applied
    /// rectangle's size but never its origin, and never the detected extent.
    /// </remarks>
    public bool HasTrimGeometry => CurrentTrimGeometry is not null;

    /// <summary>
    /// Whether the file on screen has a recorded detected graphic extent, whether this step
    /// produced it or is about to consume it (SCRUM-11081 §15).
    /// </summary>
    /// <remarks>
    /// The predicate a Print Dimensions display would read. It stays true one step past the Trim
    /// review, where the trimmed file becomes the input being sized and
    /// <see cref="HasTrimGeometry"/> correctly goes false.
    /// </remarks>
    public bool HasDetectedGraphicBounds => ArtefactTrimGeometry is not null;

    /// <summary>
    /// Whether the artefact on screen was produced under a recorded reviewed-content authority
    /// (Part C2B2 §14).
    /// </summary>
    /// <remarks>
    /// Answered from <see cref="BackgroundRemovalAttemptDecision"/> alone, so it is false for
    /// every artefact that is not an authorised background-removal result — a trim, a manual
    /// crop, a promotion — rather than falling back to whatever the session currently holds.
    /// </remarks>
    public bool HasBackgroundRemovalAttemptAuthority =>
        BackgroundRemovalAttemptDecision != Domain.Sessions.BackgroundRemovalDecision.Unspecified;

    /// <summary>
    /// Whether the artefact on screen was produced under a recorded preparation plan
    /// (Epic 11400 Part B1A.2B §19).
    /// </summary>
    public bool HasAttemptPreparation => AttemptPreparation is not null;

    /// <summary>Whether this session can still be driven forward (Part 3C2 §11).</summary>
    public bool CanContinueProcessing => SessionStateRules.AllowsProgress(State);

    /// <summary>True when the results this session produces are synthetic (Part 3C3A §8).</summary>
    public bool IsFakeProcessing => ProcessingMode == AdapterExecutionMode.Fake;

    /// <summary>
    /// Whether the screen has a genuine before/after pair to show (Part C1 §9, §11).
    /// </summary>
    /// <remarks>
    /// True only when the artefact on screen is this step's own result <b>and</b> the Revision
    /// it was derived from is still resolvable. A step that produced nothing — a failed Trim,
    /// most importantly — has no "after", so there is nothing here to fabricate one from
    /// (§17).
    /// </remarks>
    public bool HasBeforeAfterComparison =>
        CurrentArtefact is { IsCurrentStepResult: true } && UpstreamArtefact is not null &&
        UpstreamArtefact.Facts.Format is not (ImageFormat.Psd or ImageFormat.Pdf);

    public static SessionView From(
        WorkflowSnapshot snapshot,
        IReadOnlyList<CommandKind> availableCommands,
        IReadOnlyList<Revision> revisions,
        IReadOnlyList<PrintOutput> outputs,
        IReadOnlyList<ProcessingAttempt> attempts,
        AdapterExecutionMode processingMode,
        IReadOnlyList<StepKind> returnTargets,
        IReadOnlyList<PresetPrintRecommendation> presetRecommendations,
        Guid? enlargementOfferId)
    {
        ArgumentNullException.ThrowIfNull(presetRecommendations);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(availableCommands);
        ArgumentNullException.ThrowIfNull(revisions);
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentNullException.ThrowIfNull(attempts);
        ArgumentNullException.ThrowIfNull(returnTargets);

        ArtefactView? current = ResolveArtefact(snapshot, revisions);

        // The two halves of "may an operator crop by hand", computed once and read twice: the
        // manual-crop offer needs it, and the margin controls need its negation — a file the
        // automatic trim has refused is on the manual path, where adding margin to a crop that
        // was never decided would be a control that cannot do what it appears to (§17).
        bool canManualCrop = ManualCropEligibility.IsEligible(snapshot, attempts)
            && availableCommands.Contains(CommandKind.SubmitManualCrop);

        BackgroundRemovalAuthority? usable = snapshot.UsableBackgroundRemovalAuthority;

        // The plan that would actually run, asked once here and read several times below, so the
        // read model cannot report a limiting edge from one plan and readiness from another
        // (Epic 11400 Part B1A.2A §19).
        PrintPreparationPlan? usablePlan = snapshot.UsablePrintPreparationPlan;

        // Resolved before the projection so the two authorities sit side by side here, where
        // the difference between them is visible: one is what the next run may do, the other is
        // what the result on screen already did.
        BackgroundRemovalAuthority? producingAuthority = ResolveAttemptAuthority(current, attempts);

        return new SessionView(
            snapshot.SessionId,
            snapshot.WorkflowType,
            snapshot.OutputName,
            snapshot.SessionState,
            snapshot.Steps,
            snapshot.CurrentStep,
            snapshot.Dimensions,
            snapshot.WhiteUnderbaseBranch,
            availableCommands,
            current,
            processingMode,
            [.. outputs.OrderBy(o => o.CreatedAtUtc).Select(PrintOutputView.From)],
            snapshot.Definition.Contains(StepKind.PhotoshopOutput),
            ResolveUpstream(current, revisions),
            ManualCropEligibility.CurrentStepFailure(snapshot, attempts),
            canManualCrop,
            [.. returnTargets.Select(step => new ReturnTargetView(
                step, snapshot.Definition.IndexOf(step)))],
            snapshot.TrimMargin,
            availableCommands.Contains(CommandKind.SetTrimParameters) && !canManualCrop,
            ResolveTrimParameters(current, attempts),
            ResolveTrimGeometry(current, attempts),

            // The *usable* authority, never the raw one. A session can hold an authority granted
            // over content that has since been replaced, and reporting that as the current
            // decision would present a stale record as readiness, which is the one thing §23
            // forbids. Unspecified and null are what "nothing authorises a run right now" looks
            // like, and there is no third state meaning "probably fine".
            usable?.Decision ?? BackgroundRemovalDecision.Unspecified,
            usable?.ReviewedRevisionId,

            // Both answered by the engine's own probe rather than by re-deriving the rules here.
            // AvailableCommands probes StartStep with the *current* step, so asking whether it is
            // offered while BackgroundRemoval is current is exactly asking whether
            // StartStep(BackgroundRemoval) would be accepted, decision and step state included.
            // An offered control and an accepted command cannot disagree because only one of them
            // is deciding (§23).
            availableCommands.Contains(CommandKind.SetBackgroundRemovalDecision),
            snapshot.CurrentStep is { Step: StepKind.BackgroundRemoval }
                && availableCommands.Contains(CommandKind.StartStep),

            // The producing attempt's own authority, resolved exactly as the trim parameters
            // beside it are and never from `usable` above: the two answer different questions,
            // and a review that borrowed the pending one would rewrite what the operator is
            // being told about a result every time the session's next-run authority moved
            // (§15).
            producingAuthority?.Decision ?? BackgroundRemovalDecision.Unspecified,
            producingAuthority?.ReviewedRevisionId,

            // What the last stop on the current step left behind, so the screen can warn about
            // an external application that may still be running or still be holding a processed
            // result. Read from the closed attempt row, which is the only durable record of it
            // and survives a restart exactly as the rest of the history does (Part D2A §29).
            ResolveLastStop(snapshot, attempts),

            // What the recorded millimetres mean. Reported even for a legacy pair, because
            // "these are two exact dimensions from before the contract" is precisely what the
            // operator has to be told before they can reconfirm them (Epic 11400 Part B1A.2A §19).
            snapshot.DimensionSemantics,

            // Every field below comes from the *usable* plan, never the raw one. A session can
            // hold a plan calculated against content that has since been replaced, and reporting
            // its limiting edge as the current one would present a stale record as readiness —
            // the one thing §19 forbids. Null is what "nothing is planned right now" looks like,
            // and there is no third state meaning "probably still fine".
            usablePlan?.MaxWidthMm,
            usablePlan?.MaxHeightMm,
            usablePlan?.Mode,
            usablePlan?.LimitingEdge,
            usablePlan?.ProjectedPixelWidth,
            usablePlan?.ProjectedPixelHeight,

            // The snapshot's own predicate rather than a comparison rebuilt here, so an offered
            // "reconfirm the size" control and the engine's refusal to start Photoshop cannot
            // disagree about whether a plan still applies (§10, §19).
            snapshot.NeedsDimensionReview,

            // Both answered by the engine's own probe rather than by re-deriving the rules here.
            // AvailableCommands probes StartStep with the *current* step, so asking whether it is
            // offered while PhotoshopOutput is current is exactly asking whether
            // StartStep(PhotoshopOutput) would be accepted — plan, W1 branch and step state
            // included (§15).
            availableCommands.Contains(CommandKind.SetPrintDimensions),
            snapshot.CurrentStep is { Step: StepKind.PhotoshopOutput }
                && availableCommands.Contains(CommandKind.StartStep),

            // The producing attempt's own plan, resolved exactly as the trim parameters and the
            // background-removal authority beside it are, and never from `usablePlan` above. The
            // two answer different questions, and a review that borrowed the pending one would
            // rewrite what the operator is told about a result every time the session's next-run
            // plan moved (Part B1A.2B §19).
            ResolveAttemptPreparation(current, attempts, processingMode),

            // The flexible-size seam, assembled from the same snapshot predicates everything else
            // reads. Nothing here is a second opinion: the UI is told what was decided and what
            // may be decided next, and calculates none of it (Part B1A.2D §28).
            FlexibleSizeView.From(
                snapshot, availableCommands, presetRecommendations, enlargementOfferId))
        {
            ArtefactManualCropGeometry = current is null ? null : attempts.FirstOrDefault(a => a.OutputRevisionId == current.RevisionId)?.ManualCropGeometry,
            OriginalSourceFormat = revisions.FirstOrDefault(r => r.IsRoot)?.Facts.Format,

            // The same root Revision the format is read from, so the file name, the format and
            // the previewable identity can never describe three different files.
            SourceFileName = revisions.FirstOrDefault(r => r.IsRoot)?.File.FileName,
            RootRevisionId = revisions.FirstOrDefault(r => r.IsRoot)?.Id,
            PdfInspection = attempts.LastOrDefault(a => a.PdfInspection is not null)?.PdfInspection,
        };
    }

    /// <summary>
    /// The preparation plan the attempt that produced <paramref name="current"/> actually ran
    /// under (Part B1A.2B §19).
    /// </summary>
    /// <remarks>
    /// Found through <see cref="ProcessingAttempt.OutputRevisionId"/> — the attempt that says it
    /// produced this exact Revision — for the same reason
    /// <see cref="ResolveTrimParameters"/> is: after a reject-and-re-run the history holds two
    /// Photoshop attempts, and "the plan whatever ran last used" would label the output on screen
    /// with limits that produced a different file.
    /// <para>
    /// Only for the step's own result. While the screen shows the file a step is about to
    /// <i>consume</i>, there is no produced result for an audit line to describe, and the session's
    /// pending plan is emphatically not a substitute for one.
    /// </para>
    /// </remarks>
    private static PrintPreparationAttemptView? ResolveAttemptPreparation(
        ArtefactView? current,
        IReadOnlyList<ProcessingAttempt> attempts,
        AdapterExecutionMode processingMode)
    {
        if (current is not { IsCurrentStepResult: true } result)
        {
            return null;
        }

        foreach (ProcessingAttempt attempt in attempts)
        {
            if (attempt.OutputRevisionId == result.RevisionId &&
                attempt.Preparation is { } preparation)
            {
                return PrintPreparationAttemptView.From(preparation, processingMode);
            }
        }

        return null;
    }

    /// <summary>
    /// The stop that ended the current step's most recent attempt, when one did
    /// (Epic 11300 Part D2A §29).
    /// </summary>
    /// <remarks>
    /// Deliberately scoped to the current step's <b>most recent</b> attempt rather than to any
    /// stopped attempt in the history. The value drives a warning about what the external
    /// application may still be holding <i>now</i>, and a stop from three retries ago says
    /// nothing about that — a warning sourced from it would still be on screen after a
    /// subsequent run had succeeded.
    /// <para>
    /// Returns null the moment the step moves on, because the newest attempt is then the one
    /// that succeeded or failed rather than the one that was stopped.
    /// </para>
    /// </remarks>
    private static AutomationStopAudit? ResolveLastStop(
        WorkflowSnapshot snapshot, IReadOnlyList<ProcessingAttempt> attempts)
    {
        if (snapshot.CurrentStep is not { } current)
        {
            return null;
        }

        ProcessingAttempt? newest = null;
        foreach (ProcessingAttempt attempt in attempts)
        {
            if (attempt.Step != current.Step)
            {
                continue;
            }

            if (newest is null ||
                attempt.RetrySequence > newest.RetrySequence ||
                (attempt.RetrySequence == newest.RetrySequence &&
                 attempt.StartedAtUtc >= newest.StartedAtUtc))
            {
                newest = attempt;
            }
        }

        return newest is { Status: AttemptStatus.Cancelled }
            ? AutomationStopAudit.Read(newest.Failure)
            : null;
    }

    /// <summary>
    /// The margin the attempt that produced <paramref name="current"/> actually ran with (§18).
    /// </summary>
    /// <remarks>
    /// Found through <see cref="ProcessingAttempt.OutputRevisionId"/> — the attempt that says it
    /// produced this exact Revision — rather than by taking the newest Trim attempt. After a
    /// reject-and-re-run the history holds two, and "the parameters of whatever ran last" would
    /// label the result on screen with settings that produced a different file (§21).
    /// <para>
    /// Only for the step's own result. While the screen is showing the file a step is about to
    /// <i>consume</i>, that file's own trim parameters — if it even had any — describe how it
    /// was made further upstream, which is not what the line beside a review is claiming.
    /// </para>
    /// </remarks>
    private static TrimMargin? ResolveTrimParameters(
        ArtefactView? current, IReadOnlyList<ProcessingAttempt> attempts)
    {
        if (current is not { IsCurrentStepResult: true } result)
        {
            return null;
        }

        foreach (ProcessingAttempt attempt in attempts)
        {
            if (attempt.OutputRevisionId == result.RevisionId)
            {
                return attempt.TrimParameters;
            }
        }

        return null;
    }

    /// <summary>
    /// The crop rectangles the attempt that produced <paramref name="current"/> established
    /// (SCRUM-11081 §12, §15).
    /// </summary>
    /// <remarks>
    /// Found through <see cref="ProcessingAttempt.OutputRevisionId"/>, exactly as
    /// <see cref="ResolveTrimParameters"/> is and for the same reason: after a reject-and-re-run
    /// at a different margin the history holds two trim attempts with the same detected content
    /// and different applied rectangles, and "the geometry of whatever ran last" would label the
    /// file on screen with a crop that produced a different file. Returning upstream and running
    /// again moves this to the new attempt's rectangles while leaving the superseded attempt's
    /// own row untouched (§19).
    /// <para>
    /// Unlike <see cref="ResolveTrimParameters"/> this is <b>not</b> narrowed to the step's own
    /// result. It answers "where did the artwork sit in the original", which stays a true and
    /// useful statement about the file after Trim hands it downstream — and is what a Print
    /// Dimensions display needs without recomputing anything (§15). The narrower
    /// review-line reading is <see cref="CurrentTrimGeometry"/>, one line of filtering away.
    /// </para>
    /// </remarks>
    private static TrimGeometry? ResolveTrimGeometry(
        ArtefactView? current, IReadOnlyList<ProcessingAttempt> attempts)
    {
        if (current is not { } artefact)
        {
            return null;
        }

        foreach (ProcessingAttempt attempt in attempts)
        {
            if (attempt.OutputRevisionId == artefact.RevisionId)
            {
                return attempt.TrimGeometry;
            }
        }

        return null;
    }

    /// <summary>
    /// The reviewed-content authority the attempt that produced <paramref name="current"/> ran
    /// under (Part C2B2 §15).
    /// </summary>
    /// <remarks>
    /// Found through <see cref="ProcessingAttempt.OutputRevisionId"/> — the attempt that says it
    /// produced this exact Revision — for the same reason
    /// <see cref="ResolveTrimParameters"/> is: after a reject-and-re-run the history holds two
    /// background-removal attempts, and "the authority of whatever ran last" would label the
    /// cutout on screen with a decision that produced a different file.
    /// <para>
    /// Only for the step's own result. While the screen shows the file a step is about to
    /// <i>consume</i>, there is no produced result for an audit line to describe, and the
    /// session's pending authority is emphatically not a substitute for one.
    /// </para>
    /// </remarks>
    private static BackgroundRemovalAuthority? ResolveAttemptAuthority(
        ArtefactView? current, IReadOnlyList<ProcessingAttempt> attempts)
    {
        if (current is not { IsCurrentStepResult: true } result)
        {
            return null;
        }

        foreach (ProcessingAttempt attempt in attempts)
        {
            if (attempt.OutputRevisionId == result.RevisionId)
            {
                return attempt.BackgroundRemovalAuthority;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the "before" half of a comparison from the derivation edge itself.
    /// </summary>
    /// <remarks>
    /// Only for an artefact that is the current step's own result: when the screen is already
    /// showing the step's <i>input</i>, that input is the only thing there is to look at, and
    /// pairing it with its own grandparent would answer a question nobody asked.
    /// </remarks>
    private static ArtefactView? ResolveUpstream(ArtefactView? current, IReadOnlyList<Revision> revisions) =>
        current is { IsCurrentStepResult: true, SourceRevisionId: RevisionId sourceId } &&
        Find(revisions, sourceId) is Revision source
            ? ArtefactView.From(source, isCurrentStepResult: false)
            : null;

    /// <summary>
    /// Picks the Revision the screen should describe.
    /// </summary>
    /// <remarks>
    /// The step's own result takes precedence, because that is what a review is about. When it
    /// has none — Waiting, RetryRequired, Failed — the honest thing to show is the file the
    /// step will actually work from, which <see cref="WorkflowSnapshot.UpstreamRevisionOf"/>
    /// already defines (including the fall-through past skipped steps). Neither case restates
    /// a workflow rule here.
    /// </remarks>
    private static ArtefactView? ResolveArtefact(WorkflowSnapshot snapshot, IReadOnlyList<Revision> revisions)
    {
        if (revisions.Count == 0)
        {
            return null;
        }

        SessionStep? current = snapshot.CurrentStep;

        // Every step finished: the terminal step's result is the session's outcome.
        StepKind subject = current?.Step ?? snapshot.Definition.Terminal.Kind;
        RevisionId? own = current is null
            ? snapshot.Step(subject)?.CurrentRevisionId
            : current.CurrentRevisionId;

        if (own is RevisionId ownId && Find(revisions, ownId) is Revision ownRevision)
        {
            return ArtefactView.From(ownRevision, isCurrentStepResult: true);
        }

        return snapshot.UpstreamRevisionOf(subject) is RevisionId upstreamId &&
               Find(revisions, upstreamId) is Revision upstream
            ? ArtefactView.From(upstream, isCurrentStepResult: false)
            : null;
    }

    private static Revision? Find(IReadOnlyList<Revision> revisions, RevisionId id)
    {
        foreach (Revision revision in revisions)
        {
            if (revision.Id == id)
            {
                return revision;
            }
        }

        return null;
    }
}
