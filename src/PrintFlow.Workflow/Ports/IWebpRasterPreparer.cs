using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.Ports;

public interface IWebpRasterPreparer
{
    Task<OperationResult<FileFacts>> PrepareAsync(
        WorkspaceFileRef source, Sha256 sourceHash, WorkspaceFileRef destination, CancellationToken cancellationToken);
}
