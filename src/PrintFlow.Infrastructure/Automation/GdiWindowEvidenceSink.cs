using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Automation;

/// <summary>
/// Writes a PNG of one identified window into a local evidence directory.
/// </summary>
/// <remarks>
/// <c>PrintWindow</c> is used rather than a screen-rectangle grab: it asks the window to render
/// itself, so what lands in the file is that window and nothing that happens to be sitting on
/// top of it. Whatever is behind it — including anything of the customer's — is never in frame
/// (Epic 11300 Part A §20).
///
/// Encoding goes through WIC (<see cref="PngBitmapEncoder"/>), already this project's imaging
/// stack for file inspection and previews, so no new dependency is introduced to take a
/// screenshot.
/// </remarks>
public sealed class GdiWindowEvidenceSink : IAutomationEvidenceSink
{
    /// <summary>A capture wider or taller than this is refused rather than allocated.</summary>
    private const int MaxDimension = 16384;

    private readonly string _evidenceDirectory;
    private readonly TimeProvider _clock;

    /// <param name="evidenceDirectory">
    /// A local directory outside the session workspace. Evidence is diagnostic material for
    /// this workstation, not a workspace artefact: keeping it out of <c>Sessions\</c> is what
    /// stops it being promoted, hashed into a Revision, or cleaned up with a session.
    /// </param>
    /// <param name="clock">Supplies capture timestamps.</param>
    public GdiWindowEvidenceSink(string evidenceDirectory, TimeProvider clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceDirectory);
        ArgumentNullException.ThrowIfNull(clock);

        _evidenceDirectory = evidenceDirectory;
        _clock = clock;
    }

    /// <inheritdoc />
    public OperationResult<EvidenceRef> CaptureWindow(ExternalWindowRef window, string reason)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        int width = window.Bounds.Width;
        int height = window.Bounds.Height;
        if (width <= 0 || height <= 0 || width > MaxDimension || height > MaxDimension)
        {
            return OperationResult.Fail<EvidenceRef>(
                FailureCode.WorkspaceError,
                $"Window {window.Handle} has unusable bounds {window.Bounds} for capture.");
        }

        OperationResult<byte[]> pixels = CapturePixels(window.Handle.Value, width, height);
        if (pixels.IsFailure)
        {
            return OperationResult.Fail<EvidenceRef>(pixels.Failure);
        }

        DateTimeOffset now = _clock.GetUtcNow();
        string fileName = string.Create(
            CultureInfo.InvariantCulture,
            $"{now:yyyyMMdd'T'HHmmss'Z'}_{Sanitise(reason)}_{window.Handle.Value:X}.png");

        try
        {
            Directory.CreateDirectory(_evidenceDirectory);

            BitmapSource source = BitmapSource.Create(
                width, height, 96, 96, PixelFormats.Bgra32, palette: null, pixels.Value, width * 4);
            source.Freeze();

            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(source));

            string absolutePath = Path.Combine(_evidenceDirectory, fileName);
            using (FileStream stream = new(absolutePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                encoder.Save(stream);
            }

            return OperationResult.Ok(new EvidenceRef(absolutePath, reason, now, window.Title));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return OperationResult.Fail<EvidenceRef>(
                FailureCode.WorkspaceError, $"Evidence capture could not be written: {ex.Message}");
        }
    }

    private static OperationResult<byte[]> CapturePixels(nint handle, int width, int height)
    {
        nint windowDc = NativeMethods.GetWindowDC(handle);
        if (windowDc == 0)
        {
            return OperationResult.Fail<byte[]>(
                FailureCode.MeituTargetLost, "The window released its device context before it could be captured.");
        }

        nint memoryDc = 0;
        nint bitmap = 0;
        nint previous = 0;
        try
        {
            memoryDc = NativeMethods.CreateCompatibleDC(windowDc);
            bitmap = NativeMethods.CreateCompatibleBitmap(windowDc, width, height);
            if (memoryDc == 0 || bitmap == 0)
            {
                return OperationResult.Fail<byte[]>(
                    FailureCode.WorkspaceError, "A capture surface could not be allocated.");
            }

            previous = NativeMethods.SelectObject(memoryDc, bitmap);

            // PrintWindow first; BitBlt is the fallback for windows that decline to render
            // themselves. Both read only this window's device context.
            if (!NativeMethods.PrintWindow(handle, memoryDc, NativeMethods.PW_RENDERFULLCONTENT) &&
                !NativeMethods.BitBlt(memoryDc, 0, 0, width, height, windowDc, 0, 0, NativeMethods.SRCCOPY))
            {
                return OperationResult.Fail<byte[]>(
                    FailureCode.WorkspaceError, "The window could not be rendered into the capture surface.");
            }

            // Negative height requests a top-down DIB, matching the row order BitmapSource wants.
            NativeMethods.BITMAPINFOHEADER info = new()
            {
                biSize = 40,
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = NativeMethods.BI_RGB,
            };

            byte[] buffer = new byte[checked(width * height * 4)];
            int copied = NativeMethods.GetDIBits(
                memoryDc, bitmap, 0, (uint)height, buffer, ref info, NativeMethods.DIB_RGB_COLORS);

            return copied == 0
                ? OperationResult.Fail<byte[]>(
                    FailureCode.WorkspaceError, "No scan lines could be read from the capture surface.")
                : OperationResult.Ok(buffer);
        }
        finally
        {
            if (previous != 0)
            {
                NativeMethods.SelectObject(memoryDc, previous);
            }

            if (bitmap != 0)
            {
                NativeMethods.DeleteObject(bitmap);
            }

            if (memoryDc != 0)
            {
                NativeMethods.DeleteDC(memoryDc);
            }

            NativeMethods.ReleaseDC(handle, windowDc);
        }
    }

    private static string Sanitise(string reason)
    {
        Span<char> buffer = stackalloc char[Math.Min(reason.Length, 48)];
        int written = 0;
        foreach (char c in reason)
        {
            if (written == buffer.Length)
            {
                break;
            }

            buffer[written++] = char.IsAsciiLetterOrDigit(c) ? c : '-';
        }

        return written == 0 ? "evidence" : new string(buffer[..written]);
    }
}
