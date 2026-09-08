using PrintFlow.Domain.Automation;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;

namespace PrintFlow.Workflow.Ports;

/// <summary>The stable position after one bounded page of diagnostic rows.</summary>
public sealed record DiagnosticRetentionCursor(DateTimeOffset AtUtc, AutomationLogId Id);

/// <summary>One durable reference to a local diagnostic file.</summary>
public sealed record DiagnosticFileReference(
    string Path,
    DateTimeOffset AtUtc,
    SessionId? SessionId,
    AttemptId? AttemptId,
    StepKind? Step);

/// <summary>One page of log rows old enough to be considered for retention.</summary>
public sealed record DiagnosticRetentionBatch(
    IReadOnlyList<AutomationLogEntry> Entries,
    DiagnosticRetentionCursor? Next);

/// <summary>
/// The retention-specific view of durable diagnostics. It is separate from session mutation:
/// expiring diagnostics must never acquire permission to rewrite workflow history.
/// </summary>
public interface IDiagnosticRetentionRepository
{
    Task<OperationResult<DiagnosticRetentionBatch>> ReadExpiredBatchAsync(
        DateTimeOffset cutoffUtc,
        DiagnosticRetentionCursor? after,
        int maximumCount,
        CancellationToken cancellationToken);

    /// <summary>Reads every log/attempt reference to the supplied candidate paths.</summary>
    Task<OperationResult<IReadOnlyList<DiagnosticFileReference>>> ReadFileReferencesAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns candidate paths that are also a source, Revision, output, reservation or former
    /// authoritative working path. Such files can never be diagnostic-retention deletions.
    /// </summary>
    Task<OperationResult<IReadOnlyList<string>>> FindAuthoritativePathsAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken);

    /// <summary>Expires the exact old rows as one bounded transaction.</summary>
    Task<OperationResult<Unit>> ExpireAsync(
        IReadOnlyList<AutomationLogId> ids,
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken);
}

public enum DiagnosticFileDeletion
{
    Deleted,
    Missing,
    Preserved,
}

/// <summary>Guarded deletion of one positively classified local diagnostic capture.</summary>
public interface IDiagnosticFileStore
{
    Task<OperationResult<DiagnosticFileDeletion>> DeleteExpiredAsync(
        string absolutePath,
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken);
}
