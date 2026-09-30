using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Diagnostics;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.WorkstationEntry;

public sealed class ContainedDiagnosticEvidence(OwnedPaths paths) : IDiagnosticPackageEvidenceInspector
{
    private readonly LocalDiagnosticPackageEvidence inner = new(paths.At("evidence"));
    public DiagnosticPackageEvidenceInspection InspectFailureScreenshot(string? absolutePath)
    {
        if (absolutePath is null) return new(DiagnosticPackageEvidenceStatus.Unavailable, null);
        try { using NativePathLease held = paths.Read(absolutePath, "evidence"); return inner.InspectFailureScreenshot(absolutePath); }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        { return new(DiagnosticPackageEvidenceStatus.ExcludedByPolicy, null); }
    }
}

public sealed class ContainedDiagnosticWriter(OwnedPaths paths) : IDiagnosticPackageWriter
{
    // The real writer retains its own catch/finally cleanup behavior. The host owns both directories
    // exclusively and keeps every ancestor/source pinned until that finally has completed.
    private readonly DiagnosticPackageArchiveWriter inner = new(new LocalDiagnosticPackageEvidence(paths.At("evidence")), paths.At("diagnostics", "staging"));
    public async Task<OperationResult<DiagnosticPackageExportResult>> WriteAsync(DiagnosticPackagePlan plan, string requestedDestination, CancellationToken cancellationToken)
    {
        await paths.DiagnosticMutation.WaitAsync(cancellationToken);
        List<NativePathLease> sources = [];
        try
        {
            paths.Require(requestedDestination, "export");
            if (File.Exists(requestedDestination)) sources.Add(NativePathLease.ReadFile(requestedDestination));
            paths.Require(paths.At("diagnostics", "staging"), "staging");
            foreach (DiagnosticPackageItem item in plan.Items.Where(item => item.File is not null))
                sources.Add(paths.Read(item.File!.CanonicalPath, "evidence"));
            return await inner.WriteAsync(plan, requestedDestination, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        { return OperationResult.Fail<DiagnosticPackageExportResult>(FailureCode.WorkspaceError, "Diagnostic export refused: " + ex.Message); }
        finally { foreach (NativePathLease source in sources) source.Dispose(); paths.DiagnosticMutation.Release(); }
    }
}

public sealed class ContainedDiagnosticStore(OwnedPaths paths) : IDiagnosticFileStore
{
    private readonly LocalDiagnosticFileStore inner = new(paths.At("evidence"));
    public async Task<OperationResult<DiagnosticFileDeletion>> DeleteExpiredAsync(string absolutePath, DateTimeOffset cutoffUtc, CancellationToken cancellationToken)
    {
        await paths.DiagnosticMutation.WaitAsync(cancellationToken);
        try
        {
            paths.Require(absolutePath, "evidence");
            if (File.Exists(absolutePath)) { using NativePathLease identity = paths.Read(absolutePath, "evidence"); }
            // Admitted fixture evidence is kept read-locked for the run; the real store reports
            // preserved/unavailable rather than deleting evidence still used by this host.
            return await inner.DeleteExpiredAsync(absolutePath, cutoffUtc, cancellationToken);
        }
        catch (IOException ex) { return OperationResult.Fail<DiagnosticFileDeletion>(FailureCode.WorkspaceError, ex.Message); }
        finally { paths.DiagnosticMutation.Release(); }
    }
}
