using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.Ports;

/// <summary>
/// Decodes one persisted local diagnostic image for display without changing it.
/// </summary>
/// <remarks>
/// This is deliberately separate from <see cref="IImagePreviewDecoder"/>. A failure capture is
/// not a Revision and must not be forced into one, while an artefact preview must remain unable
/// to read arbitrary paths. The Workflow service supplies only a path read from the exact
/// attempt's persisted evidence; the App never calls this port directly.
/// </remarks>
public interface IDiagnosticImagePreviewDecoder
{
    Task<OperationResult<DecodedPreview>> DecodeDiagnosticAsync(
        string persistedAbsolutePath, CancellationToken cancellationToken);
}
