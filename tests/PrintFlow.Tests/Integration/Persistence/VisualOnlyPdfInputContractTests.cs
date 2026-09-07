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
/// The visual-only input contract that superseded SCRUM-11101, on the PDF side: a customer PDF is
/// page artwork, so a <c>Separation</c> or <c>DeviceN</c> colourant in the source is rasterised as
/// part of the visible page and its ink identity is intentionally not carried forward as
/// production authority.
/// </summary>
/// <remarks>
/// SCRUM-11101-A established that the accepted PDF authority, <c>Windows.Data.Pdf</c>, cannot
/// report a colourant at all: <c>RenderToStreamAsync</c> is the only route to page content and it
/// returns composited BGRA8 pixels. That was recorded as a blocker for the original acceptance
/// criterion, which required the operator to be asked about an existing white-ink separation, and
/// the implementation slice would have had to add a structural PDF parser or a third-party
/// dependency to close it.
/// <para>
/// The business clarification cancels that work rather than scheduling it. PDF is a visual design
/// input; source spot semantics are outside the supported production contract, and PrintFlow
/// generates production white ink when it creates the final TIFF. So the behaviour these tests
/// pin — a spot-carrying PDF prepares as an ordinary visual job — is no longer an unsafe gap to
/// be closed. It is the intended contract, and these tests exist to keep it true.
/// </para>
/// <para>
/// Two assertions from the feasibility gate are deliberately gone. One pinned the exact public
/// surface of <c>Windows.Data.Pdf</c> so that a future SDK exposing colourant metadata would fail
/// the build; that was the signal that the gap had closed, and with the requirement cancelled it
/// would only obstruct a harmless Windows API expansion. The other treated silent rasterisation of
/// a spot PDF as a finding. Neither is Product architecture. The historical evidence is preserved
/// in <c>docs/printflow/scrum-11101-existing-white-ink-decision.md</c>.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class VisualOnlyPdfInputContractTests
{
    /// <summary>
    /// A PDF whose visible artwork is painted through a named <c>W1</c> spot colourant prepares as
    /// an ordinary visual job, and the ink identity reaches nothing downstream.
    /// </summary>
    [Theory]
    [InlineData("separation")]
    [InlineData("devicen")]
    public async Task Existing_W1_spot_colourant_pdf_prepares_as_ordinary_visual_artwork(string mechanism)
    {
        byte[] bytes = mechanism == "separation"
            ? SpotColourantPdfFixtures.SeparationW1()
            : SpotColourantPdfFixtures.DeviceNW1();

        (PdfInspection inspection, long painted) = await PrepareAsync(bytes, mechanism + "-w1.pdf");

        // Prepared, not refused — the same answer the PSD path now gives to the same situation,
        // for the same reason: the source is design artwork and its ink identity is not
        // production authority.
        inspection.IsPreparedSinglePage.ShouldBeTrue();

        // Every field the Product records about a PDF. None of them is about colourants, and the
        // exhaustive list is written out so that a spot-colour field cannot be added without this
        // test being reconsidered — the structural inspector SCRUM-11101 would have needed is
        // cancelled by the visual-only contract, not merely deferred.
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
    /// Not "the spot is hard to see" but "there is no observable difference": the inspection
    /// persisted for a customer file carrying an existing white-ink separation is record-equal to
    /// the one persisted for a file that carries none. Under the visual-only contract that is the
    /// correct outcome rather than a gap — a source separation is a property of the design, and no
    /// downstream rule, guard or operator prompt may be built on it.
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
