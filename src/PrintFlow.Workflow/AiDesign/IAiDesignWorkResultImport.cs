using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Workflow.AiDesign;

public interface IAiDesignWorkResultImport
{
    Task ResumePendingAsync(CancellationToken cancellationToken);
    Task<OperationResult<SessionView>> ResumeAsync(string configurationPath, CancellationToken cancellationToken);
}
