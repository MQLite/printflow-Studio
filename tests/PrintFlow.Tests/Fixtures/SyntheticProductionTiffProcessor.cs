using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// A Photoshop output double that writes a <b>real accepted production TIFF</b>
/// (SCRUM-11104 §42, §60).
/// </summary>
/// <remarks>
/// <see cref="Infrastructure.Adapters.Fake.FakePhotoshopOutputProcessor"/> copies the approved
/// input to the reserved output path, which is exactly right for the workflow mechanics it was
/// built for — a PNG named <c>.tif</c> proves that a Revision, a <c>PrintOutput</c> and a review
/// step are created — and exactly wrong for a final-review test, because the file it leaves
/// behind is not separated CMYK, has no W1 spot channel, and could never be the thing an operator
/// inspects.
/// <para>
/// So this writes one instead: five 8-bit interleaved samples, uncompressed, PhotometricInterpretation
/// 5, ExtraSamples 0, 300 PPI, a Photoshop W1 spot resource and an RLE <c>Layr</c> block — the
/// same bytes <see cref="ProductionTiffFixture"/> produces for the inspector's own tests, at the
/// projected geometry the preparation actually asked for. It passes
/// <c>ProductionTiffInspector</c> for real, so the <c>PrintOutput</c>, the hash it binds to and
/// the review payload decoded from it are all genuine (§45).
/// </para>
/// <para>
/// It is emphatically not a claim that Photoshop ran. Nothing here resamples, converts colour or
/// generates an underbase; the W1 pattern is whatever <see cref="Bands"/> says, and its point is
/// that it is <i>known</i>, so a preview can be checked against it.
/// </para>
/// </remarks>
internal sealed class SyntheticProductionTiffProcessor : IPhotoshopOutputProcessor
{
    private readonly IWorkspace _workspace;

    public SyntheticProductionTiffProcessor(IWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _workspace = workspace;
    }

    /// <summary>
    /// The stored W1 values written as equal vertical bands: none, half, full.
    /// </summary>
    /// <remarks>
    /// 255 is the accepted "no white ink" value and 0 is 100% coverage, so a preview of this file
    /// must run black, mid grey, white from left to right. Settable so a test can make two
    /// outputs in one session visibly different from each other (§38).
    /// </remarks>
    public byte[] Bands { get; set; } = [255, 128, 0];

    /// <summary>The CMYK ink written under the whole canvas.</summary>
    public byte[] Cmyk { get; set; } = [0x10, 0x40, 0x80, 0x20];

    /// <summary>How many times a TIFF was actually produced, so a restart test can prove none was.</summary>
    public int GenerateCount { get; private set; }

    public string AdapterId => "synthetic-production-tiff-v1";

    public AdapterExecutionMode Mode => AdapterExecutionMode.Fake;

    public Task<OperationResult<AdapterOutput>> GenerateAsync(
        PhotoshopRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        GenerateCount++;

        PhotoshopPreparation preparation = request.Preparation;
        ProductionTiffFixture.WriteAt(
            _workspace.ResolveAbsolute(request.ExpectedOutput),
            new ProductionTiffFixtureOptions(
                PixelWidth: preparation.ProjectedPixelWidth,
                PixelHeight: preparation.ProjectedPixelHeight,
                CmykSamples: Cmyk,
                W1VerticalBands: Bands));

        return Task.FromResult(OperationResult.Ok(new AdapterOutput(
            request.ExpectedOutput,
            TimeSpan.Zero,
            $"synthetic production TIFF; {preparation.Semantics}; " +
            $"{preparation.ProjectedPixelWidth}x{preparation.ProjectedPixelHeight} px @ " +
            $"{preparation.ProductionDpi} ppi; no Photoshop ran.")));
    }
}
