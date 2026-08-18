using System.IO;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Adapters.Fake;

/// <summary>
/// A deterministic local stand-in for Photoshop TIFF production (Epic 11100 §41; plan §15.2).
/// </summary>
/// <remarks>
/// Writes a real file at the reserved output path so the validation pipeline downstream is
/// genuinely exercised. It makes no claim about CMYK conversion, the white-underbase channel,
/// or any other production-specific TIFF fact — those checks, and the real Photoshop
/// automation that would produce a file worth checking them on, are Epic 11400. Nothing here
/// launches, focuses, reads, or scripts Photoshop.
/// </remarks>
public sealed class FakePhotoshopOutputProcessor : IPhotoshopOutputProcessor
{
    private readonly IWorkspace _workspace;

    public FakePhotoshopOutputProcessor(IWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _workspace = workspace;
    }

    /// <inheritdoc />
    public string AdapterId => "fake-photoshop-v1";

    /// <inheritdoc />
    public Task<OperationResult<AdapterOutput>> GenerateAsync(PhotoshopRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string inputAbsolute = _workspace.ResolveAbsolute(request.ApprovedInput);
        string outputAbsolute = _workspace.ResolveAbsolute(request.ExpectedOutput);

        try
        {
            File.Copy(inputAbsolute, outputAbsolute, overwrite: true);
        }
        catch (IOException ex)
        {
            return Task.FromResult(OperationResult.Fail<AdapterOutput>(
                FailureCode.OutputMissing, $"Fake Photoshop adapter could not write '{outputAbsolute}': {ex.Message}"));
        }

        return Task.FromResult(OperationResult.Ok(new AdapterOutput(request.ExpectedOutput, TimeSpan.Zero, "fake")));
    }
}
