using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.AiDesign.Memorial;

public interface IAiAuthoringAssetFork
{
    Task<OperationResult<IReadOnlyList<AiDesignAttachment>>> CopyAsync(Guid sourceOwnerId, Guid targetOwnerId,
        IReadOnlyList<AiDesignAttachment> assets, CancellationToken ct);

    /// <summary>Copies in input order and retains rollback authority until the child publication succeeds.</summary>
    Task<OperationResult<T>> CopyAndPublishAsync<T>(Guid sourceOwnerId, Guid targetOwnerId,
        IReadOnlyList<AiDesignAttachment> assets, Func<IReadOnlyList<AiDesignAttachment>, Task<OperationResult<T>>> publish,
        CancellationToken ct);
}
