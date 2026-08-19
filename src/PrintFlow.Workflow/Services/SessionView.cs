using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
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
public sealed record ArtefactView(
    RevisionId RevisionId,
    string FileName,
    FileFacts Facts,
    bool IsCurrentStepResult)
{
    /// <summary>The hash an approval or rejection of this artefact must be bound to.</summary>
    public Sha256 Sha256 => Facts.Sha256;

    internal static ArtefactView From(Revision revision, bool isCurrentStepResult) => new(
        revision.Id, revision.File.FileName, revision.Facts, isCurrentStepResult);
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
    AdapterExecutionMode ProcessingMode)
{
    /// <summary>Whether this session can still be driven forward (Part 3C2 §11).</summary>
    public bool CanContinueProcessing => SessionStateRules.AllowsProgress(State);

    /// <summary>True when the results this session produces are synthetic (Part 3C3A §8).</summary>
    public bool IsFakeProcessing => ProcessingMode == AdapterExecutionMode.Fake;

    public static SessionView From(
        WorkflowSnapshot snapshot,
        IReadOnlyList<CommandKind> availableCommands,
        IReadOnlyList<Revision> revisions,
        AdapterExecutionMode processingMode)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(availableCommands);
        ArgumentNullException.ThrowIfNull(revisions);

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
            ResolveArtefact(snapshot, revisions),
            processingMode);
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
