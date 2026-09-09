using System.IO;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Infrastructure.Diagnostics;

/// <summary>Classifies and opens only a positively owned direct failure capture.</summary>
public sealed class LocalDiagnosticPackageEvidence : IDiagnosticPackageEvidenceInspector
{
    private readonly string _evidenceRoot;

    public LocalDiagnosticPackageEvidence(string evidenceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceRoot);
        _evidenceRoot = Path.GetFullPath(evidenceRoot);
    }

    public DiagnosticPackageEvidenceInspection InspectFailureScreenshot(string? absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
            return new(DiagnosticPackageEvidenceStatus.Unavailable, null);

        try
        {
            if (!DiagnosticRetentionPlan.IsOwnedCapture(_evidenceRoot, absolutePath))
                return new(DiagnosticPackageEvidenceStatus.ExcludedByPolicy, null);

            string full = Path.GetFullPath(absolutePath);
            try
            {
                ReparsePointGuard.RefuseAncestry(full);
            }
            catch (IOException)
            {
                return new(DiagnosticPackageEvidenceStatus.ExcludedByPolicy, null);
            }
            if (!File.Exists(full))
                return new(DiagnosticPackageEvidenceStatus.Unavailable, null);

            FileAttributes attributes = File.GetAttributes(full);
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                return new(DiagnosticPackageEvidenceStatus.ExcludedByPolicy, null);

            FileInfo info = new(full);
            if (info.Length <= 0)
                return new(DiagnosticPackageEvidenceStatus.Unavailable, null);

            return new(
                DiagnosticPackageEvidenceStatus.Available,
                new DiagnosticPackageFileSnapshot(
                    full,
                    info.Length,
                    new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new(DiagnosticPackageEvidenceStatus.ExcludedByPolicy, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(DiagnosticPackageEvidenceStatus.Unavailable, null);
        }
    }

    /// <summary>
    /// Opens the same file the preview planned and holds a handle that does not share deletion or
    /// writing. Retention may legitimately win before this call; it cannot replace/delete the file
    /// after the returned handle has been verified.
    /// </summary>
    internal OperationResult<FileStream> OpenVerifiedFailureScreenshot(
        DiagnosticPackageFileSnapshot expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        DiagnosticPackageEvidenceInspection current = InspectFailureScreenshot(expected.CanonicalPath);
        if (current is not { Status: DiagnosticPackageEvidenceStatus.Available, File: { } actual } ||
            !string.Equals(actual.CanonicalPath, expected.CanonicalPath, StringComparison.OrdinalIgnoreCase) ||
            actual.Length != expected.Length || actual.LastWriteTimeUtc != expected.LastWriteTimeUtc)
        {
            return OperationResult.Fail<FileStream>(
                FailureCode.WorkspaceError,
                "The planned failure screenshot changed or became unavailable. Review the package contents again.");
        }

        FileStream? stream = null;
        try
        {
            stream = new FileStream(
                actual.CanonicalPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                useAsync: true);

            FileInfo held = new(actual.CanonicalPath);
            DateTimeOffset heldWrite = new(held.LastWriteTimeUtc, TimeSpan.Zero);
            if (stream.Length != expected.Length || heldWrite != expected.LastWriteTimeUtc)
            {
                stream.Dispose();
                return OperationResult.Fail<FileStream>(
                    FailureCode.WorkspaceError,
                    "The planned failure screenshot changed before it could be read. Review the package contents again.");
            }

            return OperationResult.Ok(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                       ArgumentException or NotSupportedException)
        {
            stream?.Dispose();
            return OperationResult.Fail<FileStream>(
                FailureCode.WorkspaceError,
                $"The planned failure screenshot could not be read safely: {ex.Message}");
        }
    }
}
