using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// Answers chosen calls with a scripted failure without reaching the real service, and counts
/// every call that could change a job (SCRUM-11151). Everything else goes to the real service.
/// </summary>
/// <remarks>
/// A scripted failure never reaches the database, so a test can prove that showing, re-wording or
/// opening details for it changed nothing: the persisted job is compared before and after.
/// </remarks>
internal sealed class ScriptedFailureSessionService(ISessionService inner) : ISessionService
{
    public OperationFailure? NextExecuteFailure { get; set; }
    public OperationFailure? NextRepairFailure { get; set; }
    public OperationFailure? NextRecoveryFailure { get; set; }
    public OperationFailure? NextErrorRecoveryFailure { get; set; }
    public OperationFailure? NextStopFailure { get; set; }
    public OperationFailure? NextPrepareFailure { get; set; }
    public OperationFailure? NextCorrectionImportFailure { get; set; }
    public OperationFailure? NextEnlargementFailure { get; set; }
    public AutomationRuntimeView? Runtime { get; set; }

    /// <summary>While set, every load fails with this, as a torn-down database would.</summary>
    public OperationFailure? LoadFailure { get; set; }
    public OperationFailure? DetailsLoadFailure { get; set; }

    /// <summary>When set, loads fail only after the next scripted failure has been answered.</summary>
    public bool FailLoadsAfterScriptedFailure { get; set; }

    public int ExecuteCalls { get; private set; }
    public int RepairCalls { get; private set; }
    public int RecoveryCalls { get; private set; }
    public int ErrorRecoveryCalls { get; private set; }
    public int StopCalls { get; private set; }
    public int ImportCalls { get; private set; }
    public int LoadCalls { get; private set; }

    /// <summary>Every call that could have changed a job or started work.</summary>
    public int MutatingCalls => ExecuteCalls + RepairCalls + RecoveryCalls + ErrorRecoveryCalls + StopCalls + ImportCalls;

    private OperationResult<SessionView>? Scripted(ref OperationFailure? next)
    {
        if (next is not { } failure) return null;
        next = null;
        if (FailLoadsAfterScriptedFailure)
            LoadFailure = OperationFailure.Create(FailureCode.PersistenceError, "Scripted: the reload could not be read.");
        return OperationResult.Fail<SessionView>(failure);
    }

    public Task<OperationResult<SessionView>> ExecuteAsync(SessionId id, WorkflowCommand command, string? op, CancellationToken token)
    {
        ExecuteCalls++;
        OperationFailure? next = NextExecuteFailure;
        OperationResult<SessionView>? scripted = Scripted(ref next);
        NextExecuteFailure = next;
        return scripted is { } result ? Task.FromResult(result) : inner.ExecuteAsync(id, command, op, token);
    }

    public Task<OperationResult<SessionView>> RepairCorrectionFilesAsync(SessionId id, Guid request, CorrectionFileNaming naming, CancellationToken token)
    {
        RepairCalls++;
        OperationFailure? next = NextRepairFailure;
        OperationResult<SessionView>? scripted = Scripted(ref next);
        NextRepairFailure = next;
        return scripted is { } result ? Task.FromResult(result) : inner.RepairCorrectionFilesAsync(id, request, naming, token);
    }

    public Task<OperationResult<SessionView>> ResolveRecoveryAsync(SessionId id, RecoveryAction action, string? path, string? op, CancellationToken token)
    {
        RecoveryCalls++;
        OperationFailure? next = NextRecoveryFailure;
        OperationResult<SessionView>? scripted = Scripted(ref next);
        NextRecoveryFailure = next;
        return scripted is { } result ? Task.FromResult(result) : inner.ResolveRecoveryAsync(id, action, path, op, token);
    }

    public Task<OperationResult<SessionView>> ResolveErrorRecoveryAsync(SessionId id, AttemptId attempt, ErrorRecoveryAction action, string? op, CancellationToken token)
    {
        ErrorRecoveryCalls++;
        OperationFailure? next = NextErrorRecoveryFailure;
        OperationResult<SessionView>? scripted = Scripted(ref next);
        NextErrorRecoveryFailure = next;
        return scripted is { } result ? Task.FromResult(result) : inner.ResolveErrorRecoveryAsync(id, attempt, action, op, token);
    }

    public OperationResult<PrintFlow.Domain.Results.Unit> RequestStop(SessionId id, AutomationStopMode mode)
    {
        StopCalls++;
        if (NextStopFailure is not { } failure) return inner.RequestStop(id, mode);
        NextStopFailure = null;
        return OperationResult.Fail<PrintFlow.Domain.Results.Unit>(failure);
    }

    public Task<OperationResult<SessionView>> LoadAsync(SessionId id, CancellationToken token)
    {
        LoadCalls++;
        return LoadFailure is { } failure
            ? Task.FromResult(OperationResult.Fail<SessionView>(failure))
            : inner.LoadAsync(id, token);
    }

    public Task<OperationResult<SessionView>> ImportAsync(WorkflowType type, string path, string? name, string? op, CancellationToken token)
    {
        ImportCalls++;
        return inner.ImportAsync(type, path, name, op, token);
    }

    public event EventHandler<AutomationRuntimeView>? AutomationRuntimeChanged
    {
        add => inner.AutomationRuntimeChanged += value;
        remove => inner.AutomationRuntimeChanged -= value;
    }

    public Task<OperationResult<SessionView>> ApproveExactReviewAsync(SessionId id, StepKind step, RevisionId revision,
        Sha256 hash, string? op, CancellationToken token)
    {
        ExecuteCalls++;
        return inner.ApproveExactReviewAsync(id, step, revision, hash, op, token);
    }

    public Task<OperationResult<SessionView>> RejectExactReviewAsync(SessionId id, StepKind step, RevisionId revision,
        Sha256 hash, PrintFlow.Domain.Reviews.RejectionReason reason, string? notes, string? op, CancellationToken token)
    {
        ExecuteCalls++;
        return inner.RejectExactReviewAsync(id, step, revision, hash, reason, notes, op, token);
    }

    public Task<OperationResult<SessionView>> RequestColleagueCorrectionAsync(SessionId id, Guid request, RevisionId revision,
        Sha256 hash, string? note, CorrectionFileNaming naming, string? op, CancellationToken token)
    {
        ExecuteCalls++;
        OperationFailure? next = NextPrepareFailure;
        OperationResult<SessionView>? scripted = Scripted(ref next);
        NextPrepareFailure = next;
        if (scripted is { } result) return Task.FromResult(result);
        return inner.RequestColleagueCorrectionAsync(id, request, revision, hash, note, naming, op, token);
    }

    public Task<OperationResult<SessionView>> ImportCorrectedImageAsync(SessionId id, Guid request, RevisionId? revision,
        Sha256? hash, string path, string? op, CancellationToken token)
    {
        ImportCalls++;
        OperationFailure? next = NextCorrectionImportFailure;
        OperationResult<SessionView>? scripted = Scripted(ref next);
        NextCorrectionImportFailure = next;
        if (scripted is { } result) return Task.FromResult(result);
        return inner.ImportCorrectedImageAsync(id, request, revision, hash, path, op, token);
    }

    public Task<OperationResult<SessionView>> PromoteReviewedPngAsync(SessionId id, RevisionId revision, Sha256 hash,
        string? op, CancellationToken token)
    {
        ExecuteCalls++;
        return inner.PromoteReviewedPngAsync(id, revision, hash, op, token);
    }

    public Task<OperationResult<PrintDimensionsPreflight>> PreviewPrintDimensionsAsync(SessionId id, WorkflowCommand command,
        CancellationToken token) => inner.PreviewPrintDimensionsAsync(id, command, token);

    public AutomationRuntimeView GetAutomationRuntime(SessionId id) => Runtime ?? inner.GetAutomationRuntime(id);
    public Task<OperationResult<SessionView>> AuthoriseCurrentEnlargementAsync(SessionId id, Guid offer, string? op, CancellationToken token)
    {
        ExecuteCalls++;
        OperationFailure? next = NextEnlargementFailure;
        OperationResult<SessionView>? scripted = Scripted(ref next);
        NextEnlargementFailure = next;
        return scripted is { } result ? Task.FromResult(result) : inner.AuthoriseCurrentEnlargementAsync(id, offer, op, token);
    }
    public Task<OperationResult<IReadOnlyList<RecoveryItem>>> ListRecoveryAsync(CancellationToken token) => inner.ListRecoveryAsync(token);
    public Task<OperationResult<PrintFlow.Workflow.Services.ErrorDetailsView>> LoadErrorDetailsAsync(SessionId id, AttemptId attempt, CancellationToken token) =>
        DetailsLoadFailure is { } failure
            ? Task.FromResult(OperationResult.Fail<PrintFlow.Workflow.Services.ErrorDetailsView>(failure))
            : inner.LoadErrorDetailsAsync(id, attempt, token);
    public Task<OperationResult<IReadOnlyList<SessionListItem>>> ListRecentAsync(CancellationToken token) => inner.ListRecentAsync(token);
    public Task<OperationResult<PrintFlow.Domain.Results.Unit>> RemoveFromRecentAsync(SessionId id, CancellationToken token) => inner.RemoveFromRecentAsync(id, token);
}
