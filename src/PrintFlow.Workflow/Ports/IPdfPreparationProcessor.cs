using PrintFlow.Domain.Results;
using PrintFlow.Domain.Files;

namespace PrintFlow.Workflow.Ports;

/// <summary>Inspects the immutable PDF copy before selecting any page, then prepares one full-page raster.</summary>
public interface IPdfPreparationProcessor
{
    string AdapterId { get; }
    AdapterExecutionMode Mode { get; }
    Task<OperationResult<AdapterOutput>> PrepareAsync(PdfPreparationRequest request, CancellationToken cancellationToken);
}

public sealed record PdfPreparationRequest(WorkspaceFileRef Input, WorkspaceFileRef ExpectedOutput)
{
    public IAutomationStopSignal Stop { get; init; } = InertAutomationStopSignal.Instance;
}

/// <summary>Old composition roots fail at the preparation boundary instead of accepting opaque PDF.</summary>
internal sealed class UnavailablePdfPreparationProcessor : IPdfPreparationProcessor
{
    public string AdapterId => "pdf-preparation-unavailable";
    public AdapterExecutionMode Mode => AdapterExecutionMode.Production;
    public Task<OperationResult<AdapterOutput>> PrepareAsync(PdfPreparationRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult.Fail<AdapterOutput>(FailureCode.PdfPreparationFailed,
            "PDF preparation is unavailable in this installation."));
}
