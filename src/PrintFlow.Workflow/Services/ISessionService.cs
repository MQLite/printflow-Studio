using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Workflow.Services;

/// <summary>
/// The single entry point the UI (or a test) uses to drive a session
/// (Epic 11100 plan §9.2, §35).
/// </summary>
/// <remarks>
/// <c>ViewModel → ISessionService.ExecuteAsync(...) → OperationResult&lt;SessionView&gt;</c> is
/// the entire contract: the UI never constructs a <c>WorkflowSnapshot</c>, never writes SQL,
/// never touches a file path, and never references an adapter.
/// </remarks>
public interface ISessionService
{
    /// <summary>Unresolved interruptions, without Recent Processing's age or count limits.</summary>
    Task<OperationResult<IReadOnlyList<RecoveryItem>>> ListRecoveryAsync(CancellationToken cancellationToken);

    /// <summary>Rechecks current recovery authority and uses the ordinary session command path.</summary>
    Task<OperationResult<SessionView>> ResolveRecoveryAsync(SessionId id, RecoveryAction action,
        string? selectedPath, string? operatorName, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a session, imports <paramref name="sourceAbsolutePath"/> as its root Revision,
    /// and returns the resulting view.
    /// </summary>
    /// <remarks>
    /// There is no <c>ImportInput</c> command (Part 1 deviation, carried forward): import is
    /// driven by the ordinary <c>StartStep(Import)</c> → <c>AttemptSucceeded</c> pair, so there
    /// is exactly one code path that produces a root Revision.
    /// </remarks>
    Task<OperationResult<SessionView>> ImportAsync(
        WorkflowType workflowType,
        string sourceAbsolutePath,
        string? outputName,
        string? operatorName,
        CancellationToken cancellationToken);

    /// <summary>Applies one command to an existing session.</summary>
    Task<OperationResult<SessionView>> ExecuteAsync(
        SessionId id, WorkflowCommand command, string? operatorName, CancellationToken cancellationToken);

    /// <summary>Loads a session's current view without changing anything.</summary>
    Task<OperationResult<SessionView>> LoadAsync(SessionId id, CancellationToken cancellationToken);

    /// <summary>Loads one exact terminal attempt as operator-facing Error Details.</summary>
    Task<OperationResult<ErrorDetailsView>> LoadErrorDetailsAsync(
        SessionId sessionId, AttemptId attemptId, CancellationToken cancellationToken);

    /// <summary>Rechecks the opened attempt and routes an authorised action through the engine.</summary>
    Task<OperationResult<SessionView>> ResolveErrorRecoveryAsync(
        SessionId sessionId,
        AttemptId attemptId,
        ErrorRecoveryAction action,
        string? operatorName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Explicitly authorises the enlargement currently offered for this session.
    /// </summary>
    /// <remarks>
    /// This is the App-safe confirmation seam: the caller supplies no Revision, hash, projected
    /// pixels or scale. The service resolves those hidden binding facts from the current plan and
    /// the ordinary command path re-verifies them before accepting the authority.
    /// </remarks>
    Task<OperationResult<SessionView>> AuthoriseCurrentEnlargementAsync(
        SessionId id,
        Guid enlargementOfferId,
        string? operatorName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Lists the sessions the Home screen offers as entry points, newest first.
    /// </summary>
    /// <remarks>
    /// The window and the cap are the service's policy, not the caller's: a view model that
    /// chose them could quietly disagree with another one, and "how much recent work Home
    /// shows" is a product rule rather than a presentation detail (Epic 11100 Part 3C2 §8).
    /// </remarks>
    Task<OperationResult<IReadOnlyList<SessionListItem>>> ListRecentAsync(CancellationToken cancellationToken);

    /// <summary>
    /// What this session's automation is doing right now (Epic 11300 Part D2A §28).
    /// </summary>
    /// <remarks>
    /// Synchronous and non-mutating, because the question it answers has to be answerable
    /// <i>while</i> <see cref="ExecuteAsync"/> has not returned. That is the whole point: a
    /// Stop control is needed exactly when a command is in flight, which is exactly when no
    /// newer <see cref="SessionView"/> exists.
    /// </remarks>
    AutomationRuntimeView GetAutomationRuntime(SessionId id);

    /// <summary>
    /// Raised whenever <see cref="GetAutomationRuntime"/> would return something different, so
    /// a screen can re-read it rather than poll.
    /// </summary>
    event EventHandler<AutomationRuntimeView>? AutomationRuntimeChanged;

    /// <summary>
    /// Asks the run in flight to stop, in the mode the operator chose
    /// (Epic 11300 Part D2A §3, §17, §27).
    /// </summary>
    /// <remarks>
    /// Records the request and returns immediately; it does not wait for the run to unwind.
    /// The run is inside <see cref="ExecuteAsync"/> on another thread, and the call that
    /// started it is the one that reports the outcome — so a Stop that blocked until the run
    /// finished would deadlock the screen against the very operation it is stopping.
    /// <para>
    /// Nothing here reaches an external application. What it does is set a flag the running
    /// adapter reads at its next safe point; what the adapter is then permitted to do is
    /// decided by <see cref="Ports.AutomationStopPolicy"/> from the phase it has actually
    /// reached. In particular <see cref="Ports.AutomationStopMode.TakeOver"/> permits no input
    /// at all, from any phase (§19, §20).
    /// </para>
    /// <para>
    /// Refuses when nothing is running for this session, and refuses a second request against a
    /// run that is already stopping — §9 permits exactly one cancel invocation, and pressing
    /// Stop twice is how a second one would happen.
    /// </para>
    /// </remarks>
    OperationResult<Unit> RequestStop(SessionId id, AutomationStopMode mode);
}
