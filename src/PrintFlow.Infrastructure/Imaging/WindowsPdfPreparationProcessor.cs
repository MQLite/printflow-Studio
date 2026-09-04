using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Imaging;

/// <summary>Windows is the sole PDF authority; WIC independently validates only its PNG output.</summary>
public sealed class WindowsPdfPreparationProcessor(IWorkspace workspace, IFileInspector inspector) : IPdfPreparationProcessor
{
    internal IPdfDocumentAuthority Authority { get; init; } = new WindowsPdfDocumentAuthority();
    public string AdapterId => "windows-pdf-preparation-v1";
    public AdapterExecutionMode Mode => AdapterExecutionMode.Production;

    public async Task<OperationResult<AdapterOutput>> PrepareAsync(PdfPreparationRequest request, CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        PdfInspection inspection = new(false, null, null, null, null, null, null, null, null,
            PdfInspection.ProductionDpi, null, null, null, Authority.Provider);
        bool opened = false;
        OperationResult<AdapterOutput> Refuse(FailureCode code, string detail, bool retryable = false) =>
            OperationResult.Fail<AdapterOutput>(OperationFailure.Create(code, detail, retryable) with { PdfInspection = inspection });
        void CheckStop()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Stop.RequestedMode is not null) throw new OperationCanceledException("Operator stopped PDF preparation.");
        }

        try
        {
            CheckStop();
            if (request.Input.Area != WorkspaceArea.Working || request.ExpectedOutput.Area != WorkspaceArea.Working)
                return Refuse(FailureCode.PdfPreparationFailed, "PDF preparation requires attempt-owned Working files.");
            string input = workspace.ResolveAbsolute(request.Input);
            string output = workspace.ResolveAbsolute(request.ExpectedOutput);
            if (!string.Equals(Path.GetDirectoryName(input), Path.GetDirectoryName(output), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(input, output, StringComparison.OrdinalIgnoreCase) || File.Exists(output) ||
                !string.Equals(Path.GetExtension(output), ".png", StringComparison.OrdinalIgnoreCase))
                return Refuse(FailureCode.PdfPreparationFailed, "Output must be a new PNG sibling in this attempt.");

            // Denies writers/deletion throughout inspection and rendering. Windows consumes the
            // same stream, so replacing a pathname cannot change page identity after counting.
            using var source = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read);
            byte[] before = SHA256.HashData(source); source.Position = 0;
            using var document = await Authority.OpenAsync(source, cancellationToken);
            opened = true;
            inspection = inspection with { IsReadable = true, IsEncrypted = document.IsEncrypted, PageCount = document.PageCount };
            if (inspection.IsEncrypted == true)
                return Refuse(FailureCode.PdfEncrypted, "Encrypted PDFs are unsupported; no page was selected.");
            if (inspection.PageCount > 1)
                return Refuse(FailureCode.PdfMultiplePages, "This PDF contains multiple pages; only a single-page PDF is accepted.");
            if (inspection.PageCount != 1)
                return Refuse(FailureCode.PdfUnreadable, "The PDF contains no usable page.");
            CheckStop();
            inspection = document.InspectSinglePage();
            if (inspection.PageWidth is not > 0 || inspection.PageHeight is not > 0 ||
                inspection.PixelWidth is not (> 0 and <= 32768) || inspection.PixelHeight is not (> 0 and <= 32768) ||
                (long)inspection.PixelWidth * inspection.PixelHeight > 80_000_000 ||
                inspection.RotationDegrees is not (0 or 90 or 180 or 270))
                return Refuse(FailureCode.PdfPreparationFailed, "Page geometry exceeds the supported 300-PPI raster bounds.");
            CheckStop();
            request.Stop.ReportPhase(ExternalOperationPhase.OperationRequested);
            bool transparent = await document.RenderAsync(output, cancellationToken);
            inspection = inspection with { PreparedPageNumber = 1, HasTransparency = transparent };
            CheckStop();
            using (var verify = File.OpenRead(input))
                if (!before.SequenceEqual(SHA256.HashData(verify)))
                    return Refuse(FailureCode.PdfPreparationFailed, "The input hash changed during preparation.");

            var first = await inspector.InspectAsync(output, cancellationToken);
            if (first.IsFailure) return Refuse(FailureCode.PdfPreparationFailed, first.Failure.TechnicalDetail, true);
            await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken);
            CheckStop();
            var second = await inspector.InspectAsync(output, cancellationToken);
            if (second.IsFailure || first.Value != second.Value)
                return Refuse(FailureCode.PdfPreparationFailed, "PDF raster did not settle between independent observations.", true);
            if (!ValidateRaster(output, second.Value, inspection))
                return Refuse(FailureCode.PdfPreparationFailed, "PNG pixels, format, transparency or dimensions do not match the PDF inspection.", true);
            CheckStop();
            return OperationResult.Ok(new AdapterOutput(request.ExpectedOutput, DateTimeOffset.UtcNow - started,
                "Single-page full visible-page PNG at 300 PPI; transparent background; no trim or production sizing.")
                { PdfInspection = inspection, ValidatedPdfRaster = second.Value });
        }
        catch (OperationCanceledException)
        {
            return Refuse(FailureCode.Cancelled, "PDF preparation cancelled; no Revision may be adopted.", true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            if (ex.HResult == unchecked((int)0x8007052B))
            {
                inspection = inspection with { IsEncrypted = true };
                return Refuse(FailureCode.PdfEncrypted, "PDF requires a password; no password was supplied.");
            }
            return Refuse(opened ? FailureCode.PdfPreparationFailed : FailureCode.PdfUnreadable,
                $"PDF preparation stopped ({ex.HResult:X8}): {ex.Message[..Math.Min(ex.Message.Length, 600)]}", opened);
        }
    }

    internal static bool ValidateRaster(string path, FileFacts facts, PdfInspection inspection)
    {
        if (!inspection.IsPreparedSinglePage || facts.Format != ImageFormat.Png || facts.ColourMode != ColourMode.Rgb ||
            facts.PixelWidth != inspection.PixelWidth || facts.PixelHeight != inspection.PixelHeight ||
            facts.DpiX is not { } dx || facts.DpiY is not { } dy || Math.Abs(dx - 300) > .02 || Math.Abs(dy - 300) > .02)
            return false;
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[26]; stream.ReadExactly(header);
        if (header[24] != 8 || header[25] is not (2 or 6)) return false;
        stream.Position = 0;
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count != 1) return false;
        var pixels = new FormatConvertedBitmap(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
        byte[] row = new byte[checked(pixels.PixelWidth * 4)];
        bool transparent = false;
        // Decode every row to detect partial/unreadable output, even after finding transparency.
        for (int y = 0; y < pixels.PixelHeight; y++)
        {
            pixels.CopyPixels(new System.Windows.Int32Rect(0, y, pixels.PixelWidth, 1), row, row.Length, 0);
            for (int x = 3; x < row.Length; x += 4) if (row[x] != 255) transparent = true;
        }
        return transparent == inspection.HasTransparency;
    }
}
