using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Workflow.Services;

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
    FailureCode? CurrentStepFailure)
{
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
        AdapterExecutionMode processingMode)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(availableCommands);
        ArgumentNullException.ThrowIfNull(revisions);
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentNullException.ThrowIfNull(attempts);

        ArtefactView? current = ResolveArtefact(snapshot, revisions);

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
            ResolveCurrentStepFailure(snapshot, attempts));
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
    /// The code the current step's newest ended attempt failed with, while the step is Failed.
    /// </summary>
    /// <remarks>
    /// Guarded on <see cref="StepState.Failed"/> rather than reported for any failed attempt in
    /// the history: a step that failed once and then succeeded is not a failed step, and a
    /// screen showing a stale code beside a good result would be worse than showing none. The
    /// one consumer today is the manual-crop notice, which must survive a reload and therefore
    /// cannot live in view-model memory (§17).
    /// </remarks>
    private static FailureCode? ResolveCurrentStepFailure(
        WorkflowSnapshot snapshot, IReadOnlyList<ProcessingAttempt> attempts)
    {
        if (snapshot.CurrentStep is not { State: StepState.Failed } step)
        {
            return null;
        }

        ProcessingAttempt? newest = null;
        foreach (ProcessingAttempt attempt in attempts)
        {
            if (attempt.Step != step.Step || attempt.EndedAtUtc is null)
            {
                continue;
            }

            if (newest is null || attempt.EndedAtUtc > newest.EndedAtUtc)
            {
                newest = attempt;
            }
        }

        return newest?.Failure?.Code;
    }

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
