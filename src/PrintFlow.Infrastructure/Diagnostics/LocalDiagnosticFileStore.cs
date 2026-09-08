using System.IO;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Infrastructure.Diagnostics;

/// <summary>Deletes one positively owned, expired capture without traversing a directory.</summary>
public sealed class LocalDiagnosticFileStore : IDiagnosticFileStore
{
    private readonly string _evidenceRoot;

    public LocalDiagnosticFileStore(string evidenceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceRoot);
        _evidenceRoot = Path.GetFullPath(evidenceRoot);
    }

    public Task<OperationResult<DiagnosticFileDeletion>> DeleteExpiredAsync(
        string absolutePath,
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!DiagnosticRetentionPlan.IsOwnedCapture(_evidenceRoot, absolutePath))
                return Task.FromResult(OperationResult.Ok(DiagnosticFileDeletion.Preserved));

            string full = Path.GetFullPath(absolutePath);
            ReparsePointGuard.RefuseAncestry(full);
            if (!File.Exists(full))
                return Task.FromResult(OperationResult.Ok(DiagnosticFileDeletion.Missing));

            FileAttributes attributes = File.GetAttributes(full);
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.ReadOnly)) != 0)
                return Task.FromResult(OperationResult.Ok(DiagnosticFileDeletion.Preserved));
            if (new DateTimeOffset(File.GetLastWriteTimeUtc(full), TimeSpan.Zero) >= cutoffUtc)
                return Task.FromResult(OperationResult.Ok(DiagnosticFileDeletion.Preserved));

            File.Delete(full);
            return Task.FromResult(OperationResult.Ok(DiagnosticFileDeletion.Deleted));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                       ArgumentException or NotSupportedException)
        {
            return Task.FromResult(OperationResult.Fail<DiagnosticFileDeletion>(
                FailureCode.WorkspaceError,
                $"An expired diagnostic capture was preserved because safe deletion failed: {ex.Message}"));
        }
    }
}
