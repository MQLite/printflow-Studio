using System.Globalization;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

/// <summary>
/// Whether a TIFF that is already an accepted production file is the file <i>this</i> preparation
/// asked for (SCRUM-11097, SCRUM-11129).
/// </summary>
/// <remarks>
/// <see cref="ProductionTiffInspector"/> answers an absolute question — is this an accepted
/// separated-CMYK + W1 production TIFF at exactly 300 PPI — and it deliberately knows nothing
/// about any particular job. This answers the relative one the inspector cannot: does the pixel
/// grid on disk match the immutable geometry the workflow validated and the attempt row already
/// recorded. The two together are the whole geometry contract, and keeping them apart is what
/// stops the inspector from needing a preparation before it can read a file.
/// <para>
/// <b>Its only caller today is <see cref="Fake.FakePhotoshopOutputProcessor"/>, and that is not an
/// oversight.</b> The production adapter reaches the same conclusion transitively and does not
/// call this: <c>GuardedPhotoshopDocumentPreparer</c> refuses a document whose pixels are not the
/// projected pixels, and <c>GuardedPhotoshopTiffSaver</c> then refuses a saved TIFF whose pixels
/// are not that document's, so by the time bytes exist they have already been compared to the
/// preparation through the document in between. The Fake adapter has no Photoshop document in the
/// middle, so for it the comparison has to be direct. Routing the guarded save path through here
/// as well would mean handing it the preparation, which its accepted B1B surface deliberately
/// does not accept — an architecture test asserts that surface takes only factual prepared-document
/// state and one managed reference.
/// </para>
/// <para>
/// The consequence is worth stating plainly rather than leaving to be discovered: the
/// <c>expectedPixels</c> / <c>actualPixels</c> / <c>expectedMillimetres</c> / <c>actualMillimetres</c>
/// evidence below appears on Fake-mode refusals only. A production run that produced a
/// wrong-sized TIFF fails earlier, in the preparer or the saver, with those adapters' own
/// diagnostics.
/// </para>
/// <para>
/// <b>Physical dimensions.</b> There is deliberately no third check for physical size, and no
/// resolution check here either — resolution is an absolute contract that
/// <see cref="ProductionTiffInspector"/> owns and has already enforced by the time this runs. At
/// the fixed production resolution the pixel grid and the resolution together define the canvas —
/// <see cref="PrintDimensions.MillimetresFromPixels"/> is the one conversion — so a file whose
/// pixels match the preparation and whose resolution the inspector proved to be exactly 300 PPI
/// cannot resolve to a wrong physical canvas, and a file failing either check already resolves to
/// one. The refusal below therefore states the millimetres the file resolves to beside the
/// millimetres that were expected, so a wrong physical canvas is visible in the evidence without a
/// second validation engine being invented to manufacture it.
/// </para>
/// </remarks>
internal static class ProductionTiffPreparationMatch
{
    /// <summary>
    /// Returns the refusal this file earns against <paramref name="preparation"/>, or null when
    /// the saved geometry is exactly the geometry that was asked for.
    /// </summary>
    /// <remarks>
    /// A failure rather than a boolean, because the caller has nothing useful to add: the
    /// interesting content of "these do not match" is which axis, by how much, and what canvas
    /// that resolves to, and all three are known only here.
    /// </remarks>
    internal static OperationFailure? Check(ProductionTiffFacts facts, PhotoshopPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(preparation);

        return facts.PixelWidth == preparation.ProjectedPixelWidth &&
               facts.PixelHeight == preparation.ProjectedPixelHeight
            ? null
            : Refuse(
                "The saved TIFF pixel dimensions are not the immutable projected dimensions this " +
                "preparation asked for.",
                facts,
                preparation);
    }

    private static OperationFailure Refuse(
        string detail, ProductionTiffFacts facts, PhotoshopPreparation preparation) =>
        OperationFailure.Create(
            FailureCode.OutputValidationFailed,
            detail,
            isRetryable: true,
            context: new Dictionary<string, string>
            {
                ["adapterOutputConstructed"] = "false",
                ["revisionCreated"] = "false",
                ["expectedPixels"] = Pixels(
                    preparation.ProjectedPixelWidth, preparation.ProjectedPixelHeight),
                ["actualPixels"] = Pixels(facts.PixelWidth, facts.PixelHeight),
                ["expectedDpi"] = Number(preparation.ProductionDpi),
                ["actualDpi"] = $"{Number(facts.XResolutionDpi)}x{Number(facts.YResolutionDpi)}",
                ["expectedMillimetres"] = Millimetres(
                    preparation.ProjectedPixelWidth, preparation.ProjectedPixelHeight),
                ["actualMillimetres"] = Millimetres(facts.PixelWidth, facts.PixelHeight),
            });

    private static string Pixels(int width, int height) =>
        $"{width.ToString(CultureInfo.InvariantCulture)}x{height.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// The physical canvas the given pixel grid resolves to at the fixed production resolution.
    /// </summary>
    /// <remarks>
    /// Reported at the file's own stored resolution would be a different and misleading number
    /// for a wrong-DPI file, so both sides are stated at the production resolution: the question
    /// this line answers is "what canvas was this grid meant to be", not "what would a viewer
    /// that trusted the file's metadata show".
    /// </remarks>
    private static string Millimetres(int width, int height) =>
        $"{Number(PrintDimensions.MillimetresFromPixels(width))}x" +
        $"{Number(PrintDimensions.MillimetresFromPixels(height))} mm";

    private static string Number(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
