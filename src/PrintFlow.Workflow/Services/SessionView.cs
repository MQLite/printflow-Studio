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

    internal static ArtefactView From(Revision revision, bool isCurrentStepResult) => new(
        revision.Id, revision.File.FileName, revision.Facts, isCurrentStepResult, revision.SourceRevisionId);
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
public sealed record PrintOutputView(
    PrintOutputId Id,
    PrintDimensions Dimensions,
    WhiteUnderbaseBranch Branch,
    ReviewState ReviewState,
    bool IsValid,
    string FileName)
{
    internal static PrintOutputView From(PrintOutput output) => new(
        output.Id,
        output.Dimensions,
        output.Branch,
        output.ReviewState,
        output.IsValid,
        output.File.FileName);
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
    BackgroundRemovalDecision BackgroundRemovalDecision,
    RevisionId? BackgroundRemovalDecisionRevisionId,
    bool CanSetBackgroundRemovalDecision,
    bool CanRunBackgroundRemoval,
    BackgroundRemovalDecision BackgroundRemovalAttemptDecision,
    RevisionId? BackgroundRemovalAttemptReviewedRevisionId)
{
    /// <summary>Whether the operator has any legal earlier step to return to (§4).</summary>
    public bool CanReturnToStep => ReturnTargets.Count > 0;

    /// <summary>
    /// Whether the artefact on screen carries deterministic trim parameters worth stating
    /// (§18).
    /// </summary>
    public bool HasTrimParameters => CurrentTrimParameters is not null;

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
        CurrentArtefact is { IsCurrentStepResult: true } && UpstreamArtefact is not null;

    public static SessionView From(
        WorkflowSnapshot snapshot,
        IReadOnlyList<CommandKind> availableCommands,
        IReadOnlyList<Revision> revisions,
        IReadOnlyList<PrintOutput> outputs,
        IReadOnlyList<ProcessingAttempt> attempts,
        AdapterExecutionMode processingMode,
        IReadOnlyList<StepKind> returnTargets)
    {
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
            producingAuthority?.ReviewedRevisionId);
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
