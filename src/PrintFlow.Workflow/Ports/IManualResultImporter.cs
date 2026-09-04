using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;

namespace PrintFlow.Workflow.Ports;

/// <summary>SCRUM-11092 / SCRUM-11112: copies and validates operator-produced evidence.</summary>
public interface IManualResultImporter
{
    Task<OperationResult<ManualResult>> ImportAsync(
        WorkspaceDirRef session, AttemptId attempt, StepKind step, FileFacts upstream, string selectedPath,
        CancellationToken cancellationToken);
}

public sealed record ManualResult(WorkspaceFileRef File, FileFacts Facts);
