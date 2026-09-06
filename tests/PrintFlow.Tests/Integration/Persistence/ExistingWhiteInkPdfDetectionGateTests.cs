using System.IO;
using System.Reflection;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// SCRUM-11101-A Gate A. The original Jira row applies the existing-white-ink decision to "PSD or
/// PDF preparation". PSD preparation refuses a document carrying an existing W1 spot channel.
/// This fixes what PDF preparation does with the same situation, and what it is capable of doing.
/// </summary>
/// <remarks>
/// These tests assert the <b>current</b> behaviour, including the part of it that is unsafe. They
/// are written this way on purpose: the gate's job was to find out whether the accepted PDF
/// authority can see a spot colourant at all, and the answer determines whether SCRUM-11101 can
/// be implemented for PDF from the existing authority or needs a new one. Pinning the present
/// behaviour is what makes the eventual fix visible as a change rather than as a claim.
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class ExistingWhiteInkPdfDetectionGateTests
{
    /// <summary>
    /// The whole public surface of the accepted PDF authority, so that "it cannot report a
    /// colourant" is a checked fact rather than a recollection of the documentation.
    /// </summary>
    /// <remarks>
    /// If a future Windows SDK projection grows a colourant, separation, colour-space or page
    /// resource member, this test fails and the architectural conclusion recorded in
    /// <c>docs/printflow/scrum-11101-existing-white-ink-decision.md</c> must be revisited. That is
    /// the intended failure: it is the only automatic signal that the gap has closed.
    /// </remarks>
    [Fact]
    public void Windows_pdf_authority_exposes_no_colourant_or_colour_space_member_at_all()
    {
        Type[] surface = [.. typeof(Windows.Data.Pdf.PdfDocument).Assembly
            .GetTypes()
            .Where(type => type.Namespace == "Windows.Data.Pdf" && type.IsPublic)];

        // Five types, and no more. A sixth would be a new capability worth reading.
        surface.Select(type => type.Name).Order(StringComparer.Ordinal).ShouldBe(
            ["PdfDocument", "PdfPage", "PdfPageDimensions", "PdfPageRenderOptions", "PdfPageRotation"]);

        string[] members = [.. surface
            .SelectMany(type => type.GetMembers(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Select(member => member.Name)];

        foreach (string forbidden in
            new[] { "Colourant", "Colorant", "Separation", "DeviceN", "ColorSpace", "ColourSpace",
                    "Resources", "Ink", "Spot", "Channel", "Plate" })
        {
            members.ShouldNotContain(
                name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"Windows.Data.Pdf now exposes a '{forbidden}' member. Gate A must be re-decided.");
        }
    }

    /// <summary>
    /// A PDF whose visible artwork is painted through a named <c>W1</c> spot colourant is prepared
    /// successfully today, and nothing about the spot reaches the persisted attempt.
    /// </summary>
    [Theory]
    [InlineData("separation")]
    [InlineData("devicen")]
    public async Task Existing_W1_spot_colourant_pdf_is_prepared_with_no_observation_of_the_spot(string mechanism)
    {
        byte[] bytes = mechanism == "separation"
            ? SpotColourantPdfFixtures.SeparationW1()
            : SpotColourantPdfFixtures.DeviceNW1();

        (PdfInspection inspection, long painted) = await PrepareAsync(bytes, mechanism + "-w1.pdf");

        // Prepared, not refused. The PSD path stops on exactly this situation; the PDF path does
        // not, and cannot, because no fact it collects mentions ink at all.
        inspection.IsPreparedSinglePage.ShouldBeTrue();

        // Every field the Product records about a PDF. None of them is about colourants, and the
        // exhaustive list is written out so that a new field cannot be added without this test
        // being reconsidered.
        inspection.IsReadable.ShouldBeTrue();
        inspection.IsEncrypted.ShouldBe(false);
        inspection.PageCount.ShouldBe(1);
        inspection.PreparedPageNumber.ShouldBe(1);
        inspection.RotationDegrees.ShouldBe(0);
        inspection.HasTransparency.ShouldBe(true);
        inspection.Provider.ShouldStartWith("Windows.Data.Pdf/");

        // The spot really was painted: the rasteriser resolved the tint transform and produced
        // the rectangle. A blank page would have made the whole fixture meaningless.
        painted.ShouldBeGreaterThan(0,
            "the spot-painted rectangle did not reach the raster, so the fixture proves nothing");
    }

    /// <summary>
    /// The spot-carrying PDF and a spot-free PDF of identical geometry are, to PrintFlow,
    /// the same PDF.
    /// </summary>
    /// <remarks>
    /// This is Gate A's actual finding. Not "the spot is hard to see" but "there is no observable
    /// difference": the inspection persisted for a customer file carrying an existing white-ink
    /// separation is byte-for-byte the inspection persisted for one that carries none. No
    /// downstream rule, guard or operator prompt can be built on a distinction the Product never
    /// makes.
    /// </remarks>
    [Fact]
    public async Task Spot_and_spot_free_pdfs_produce_an_indistinguishable_inspection()
    {
        (PdfInspection separation, _) = await PrepareAsync(SpotColourantPdfFixtures.SeparationW1(), "sep.pdf");
        (PdfInspection deviceN, _) = await PrepareAsync(SpotColourantPdfFixtures.DeviceNW1(), "dvn.pdf");
        (PdfInspection control, _) = await PrepareAsync(SpotColourantPdfFixtures.DeviceRgbControl(), "rgb.pdf");

        // PdfInspection is a record: this compares every observed fact, not a chosen subset.
        separation.ShouldBe(control);
        deviceN.ShouldBe(control);
    }

    private static async Task<(PdfInspection Inspection, long PaintedPixels)> PrepareAsync(
        byte[] bytes, string fileName)
    {
        using SessionServiceHarness h = new();
        ISessionService service = PdfPreparationWorkflowTests.Service(
            h, new WindowsPdfPreparationProcessor(h.FileWorkspace, h.FileInspector));
        string source = h.Workspace.CreateSourceFile(fileName, bytes);
        var imported = await service.ImportAsync(WorkflowType.GeneratePrintTiff, source, null, "qa", default);
        imported.IsSuccess.ShouldBeTrue();
        var prepared = await service.ExecuteAsync(
            imported.Value.Id, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation), "qa", default);
        prepared.IsSuccess.ShouldBeTrue(prepared.IsFailure ? prepared.Failure.ToString() : "");

        var stored = (await h.Repository.LoadAsync(imported.Value.Id, default)).Value!;
        ProcessingAttempt attempt = stored.Attempts.Single(a => a.Operation == OperationKind.PreparePdf);
        attempt.Status.ShouldBe(AttemptStatus.Succeeded);
        var raster = stored.Revisions.Single(r => r.Operation == OperationKind.PreparePdf);

        // The customer source is untouched on this path, as everywhere else.
        File.ReadAllBytes(source).ShouldBe(bytes);
        return (attempt.PdfInspection!, CountNonBackgroundPixels(h.FileWorkspace.ResolveAbsolute(raster.File)));
    }

    /// <summary>Counts pixels that are neither fully transparent nor pure white.</summary>
    private static long CountNonBackgroundPixels(string png)
    {
        using FileStream stream = File.OpenRead(png);
        BitmapDecoder decoder = BitmapDecoder.Create(
            stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        FormatConvertedBitmap pixels = new(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        byte[] row = new byte[checked(pixels.PixelWidth * 4)];
        long painted = 0;
        for (int y = 0; y < pixels.PixelHeight; y++)
        {
            pixels.CopyPixels(new System.Windows.Int32Rect(0, y, pixels.PixelWidth, 1), row, row.Length, 0);
            for (int x = 0; x < row.Length; x += 4)
            {
                if (row[x + 3] != 0 && (row[x] != 255 || row[x + 1] != 255 || row[x + 2] != 255)) painted++;
            }
        }
        return painted;
    }
}
