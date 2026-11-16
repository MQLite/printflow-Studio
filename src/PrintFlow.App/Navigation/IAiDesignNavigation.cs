using PrintFlow.Domain.Results;

namespace PrintFlow.App.Navigation;

public interface IAiDesignNavigation
{
    Task GoToPromptWorkbenchAsync(Guid? draftId, CancellationToken ct);
    Task GoToAiWorkTasksAsync(Guid? returnDraftId, CancellationToken ct);
    Task<OperationResult<Unit>> OpenWorkResultAsync(Guid requestId, CancellationToken ct);
}
