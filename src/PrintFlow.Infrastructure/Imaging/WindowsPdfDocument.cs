using System.IO;
using PrintFlow.Domain.Files;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace PrintFlow.Infrastructure.Imaging;

// The document remains open on the same read-only stream from count through render. No page
// object is obtained until the processor has accepted the count. No second PDF parser exists.
internal interface IPdfDocument : IDisposable
{
    int PageCount { get; }
    bool IsEncrypted { get; }
    PdfInspection InspectSinglePage();
    Task<bool> RenderAsync(string output, CancellationToken cancellationToken);
}

internal interface IPdfDocumentAuthority
{
    string Provider { get; }
    Task<IPdfDocument> OpenAsync(Stream input, CancellationToken cancellationToken);
}

internal sealed class WindowsPdfDocumentAuthority : IPdfDocumentAuthority
{
    private int _pageSelections;
    private int _renderCalls;
    internal int PageSelections => Volatile.Read(ref _pageSelections);
    internal int RenderCalls => Volatile.Read(ref _renderCalls);
    public string Provider => "Windows.Data.Pdf/" + System.Diagnostics.FileVersionInfo.GetVersionInfo(
        Path.Combine(Environment.SystemDirectory, "Windows.Data.Pdf.dll")).FileVersion;

    public async Task<IPdfDocument> OpenAsync(Stream input, CancellationToken cancellationToken)
    {
        var stream = input.AsRandomAccessStream();
        try
        {
            var document = await PdfDocument.LoadFromStreamAsync(stream).AsTask(cancellationToken);
            return new WindowsPdfDocument(document, stream, Provider, this);
        }
        catch { stream.Dispose(); throw; }
    }

    private sealed class WindowsPdfDocument(PdfDocument document, IRandomAccessStream stream, string provider,
        WindowsPdfDocumentAuthority owner) : IPdfDocument
    {
        private PdfPage? _page;
        private PdfInspection? _inspection;
        public int PageCount => checked((int)document.PageCount);
        public bool IsEncrypted => document.IsPasswordProtected;

        public PdfInspection InspectSinglePage()
        {
            if (PageCount != 1 || IsEncrypted || _page is not null)
                throw new InvalidOperationException("A single readable unencrypted page is required before selection.");
            Interlocked.Increment(ref owner._pageSelections);
            _page = document.GetPage(0); // Only here, after exact count. Public provenance is one-based.
            var size = _page.Size;
            return _inspection = new PdfInspection(true, false, 1, null,
                Box(_page.Dimensions.MediaBox), Box(_page.Dimensions.CropBox), (int)_page.Rotation * 90,
                size.Width, size.Height, PdfInspection.ProductionDpi,
                PdfInspection.RasterPixels(size.Width), PdfInspection.RasterPixels(size.Height), null, provider);
        }

        public async Task<bool> RenderAsync(string output, CancellationToken cancellationToken)
        {
            var inspection = _inspection ?? throw new InvalidOperationException("Inspect before render.");
            using var rendered = new InMemoryRandomAccessStream();
            Interlocked.Increment(ref owner._renderCalls);
            await _page!.RenderToStreamAsync(rendered, new PdfPageRenderOptions
            {
                DestinationWidth = checked((uint)inspection.PixelWidth!.Value),
                DestinationHeight = checked((uint)inspection.PixelHeight!.Value),
                BackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0),
                BitmapEncoderId = BitmapEncoder.PngEncoderId,
                IsIgnoringHighContrast = true,
                // The entire default visible page; never artwork bounds or a second crop rule.
            }).AsTask(cancellationToken);
            rendered.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(rendered).AsTask(cancellationToken);
            if (decoder.PixelWidth != inspection.PixelWidth || decoder.PixelHeight != inspection.PixelHeight)
                throw new InvalidDataException("The renderer did not honour the requested full-page dimensions.");
            var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
                new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage).AsTask(cancellationToken);
            byte[] pixels = data.DetachPixelData();
            bool transparency = false;
            for (int i = 3; i < pixels.Length; i += 4)
                if (pixels[i] != 255) { transparency = true; break; }
            cancellationToken.ThrowIfCancellationRequested();
            // Encode the observed pixels with explicit physical-resolution metadata. This does
            // not render PDF again, trim, resample, composite a background or colour-convert.
            using var file = new FileStream(output, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            using var destination = file.AsRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, destination).AsTask(cancellationToken);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, decoder.PixelWidth,
                decoder.PixelHeight, PdfInspection.ProductionDpi, PdfInspection.ProductionDpi, pixels);
            await encoder.FlushAsync().AsTask(cancellationToken);
            return transparency;
        }

        private static PdfPageBox Box(Windows.Foundation.Rect box) => new(box.X, box.Y, box.Width, box.Height);
        public void Dispose() { _page?.Dispose(); stream.Dispose(); }
    }
}
