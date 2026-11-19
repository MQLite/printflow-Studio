using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Ids;

namespace PrintFlow.Workflow.AiDesign;

public enum WorkResultStage { Submitting, AwaitingReturn, AwaitingVerification, Validating, Imported, NeedsAttention }
public enum WorkResultProblem { TimedOut, ReceiverUnavailable, ValidationFailed, RecoveryDeferred }

public sealed record WorkResultObservation(Guid RequestId, Guid AttemptId, long StateVersion,
    WorkResultStage Stage, SessionId? ImportedSessionId = null, WorkResultProblem? Problem = null)
{
    public static WorkResultObservation From(AiDesignRequest request, WorkResultStage stage, WorkResultProblem? problem = null) =>
        new(request.RequestId, request.AttemptId, request.StateVersion, stage,
            stage == WorkResultStage.Imported ? request.ImportedSessionId : null, problem);
}

public interface IWorkResultProgressReader
{
    WorkResultObservation? Find(Guid requestId);
}

public interface IWorkResultProgressObserver
{
    void Report(WorkResultObservation observation);
    void RequestChanged(Guid requestId) { }
}
