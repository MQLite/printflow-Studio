using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Commands;

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
}
