using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Workflow.Ports;

public enum DiagnosticPackageEvidenceStatus
{
    Available,
    Unavailable,
    ExcludedByPolicy,
}

/// <summary>A bounded answer about one candidate failure screenshot; it grants no directory access.</summary>
public sealed record DiagnosticPackageEvidenceInspection(
    DiagnosticPackageEvidenceStatus Status,
    DiagnosticPackageFileSnapshot? File);

public interface IDiagnosticPackageEvidenceInspector
{
    DiagnosticPackageEvidenceInspection InspectFailureScreenshot(string? absolutePath);
}

/// <summary>Writes exactly one already-computed package plan to an operator-selected file path.</summary>
public interface IDiagnosticPackageWriter
{
    Task<OperationResult<DiagnosticPackageExportResult>> WriteAsync(
        DiagnosticPackagePlan plan,
        string requestedDestination,
        CancellationToken cancellationToken);
}
