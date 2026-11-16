using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.AiDesign.Memorial;

/// <summary>Copies an accepted current design-material PNG to the exact destination selected by the operator.</summary>
public interface IMemorialMaterialExporter
{
    Task<OperationResult<string>> ExportAsync(MemorialRenderResult expected, string selectedAbsolutePath, CancellationToken ct);
}
