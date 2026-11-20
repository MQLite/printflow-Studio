using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.AiDesign;

public interface ICloudDesignPdfExport
{
    Task<OperationResult<Unit>> SaveAsync(Guid requestId, string destination, CancellationToken ct);
}
