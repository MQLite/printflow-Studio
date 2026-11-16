using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.AiDesign.Memorial;

public interface IMemorialRenderStore
{
    Task<OperationResult<MemorialRenderResult>> ReadAsync(MemorialRenderRef file, CancellationToken ct);
    Task<OperationResult<IReadOnlyList<MemorialRenderResult>>> ListAsync(Guid draftId, CancellationToken ct);

    /// <summary>Opens the recorded result on a verified, read-only stream. The caller disposes the held stream.</summary>
    Task<OperationResult<Stream>> OpenVerifiedAsync(MemorialRenderResult expected, CancellationToken ct);
}
