using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.Ports;

public interface IOpenAiImageCredentials
{
    OperationResult<string?> Read();
    OperationResult<Unit> Save(string key);
    OperationResult<Unit> Delete();
}
