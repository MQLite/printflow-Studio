using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Trimming;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Imaging;

/// <summary>
/// The real deterministic trim: decode with WIC, find every pixel whose alpha is greater than
/// zero, apply the margin, crop the canvas to it, write a PNG (Epic 11200 Part B §13).
/// </summary>
/// <remarks>
/// <b>What it will not do.</b> If the source carries no alpha channel, or its container cannot
/// be decoded at all, this returns <see cref="TrimOutcome.ManualCropRequired"/>. It does not
/// treat white or black as background, does not segment by colour, does not consult a model,
/// and does not call Meitu or Photoshop. A wrong automatic crop destroys the operator's
/// artwork silently; refusing is recoverable.
///
/// <b>Colour is never altered.</b> The crop runs on the decoded frame in its own pixel format
/// via <see cref="CroppedBitmap"/>, so a 16-bit-per-channel source stays 16-bit and the DPI
/// travels with it. The BGRA32 conversion exists only to <i>measure</i> alpha and never
/// reaches the encoder. Trim changes the canvas extent and nothing else.
///
/// <b>Premultiplied alpha is a non-issue</b> precisely because the scan reads the alpha byte
/// itself. Premultiplication changes how the colour channels are interpreted, not whether a
/// pixel has alpha, so <c>Pbgra32</c> and <c>Bgra32</c> give identical bounds.
///
/// The work runs on a thread-pool thread: decoding and scanning a production-sized image is
/// real CPU time, and doing it inline would freeze the operator's window. Every bitmap is
/// frozen, so none acquires an affinity to the thread it happened to be built on.
/// </remarks>
public sealed class DeterministicAlphaTrimProcessor : ITrimProcessor
{
    private const int BytesPerBgra32Pixel = 4;

    private readonly IWorkspace _workspace;

    public DeterministicAlphaTrimProcessor(IWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _workspace = workspace;
    }

    /// <inheritdoc />
    public string ProcessorId => "internal-alpha-trim-v1";

    /// <inheritdoc />
    public async Task<OperationResult<TrimResult>> TrimAsync(
        TrimRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string inputPath = _workspace.ResolveAbsolute(request.Input);
        string outputPath = _workspace.ResolveAbsolute(request.ExpectedOutput);

        try
        {
            return await Task.Run(
                () => Trim(request, inputPath, outputPath, cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Fail<TrimResult>(
                FailureCode.Cancelled, "Trimming was cancelled before it produced a result.");
        }
    }

    private static OperationResult<TrimResult> Trim(
        TrimRequest request, string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(inputPath))
        {
            return OperationResult.Fail<TrimResult>(
                FailureCode.OutputMissing, $"Trim input not found: '{inputPath}'.");
        }

        OperationResult<BitmapFrame?> decoded = TryDecode(inputPath);
        if (decoded.IsFailure)
        {
            return OperationResult.Fail<TrimResult>(decoded.Failure);
        }

        if (decoded.Value is not { } frame)
        {
            return OperationResult.Ok(TrimResult.ManualCropRequired(
                $"WIC could not decode '{Path.GetFileName(inputPath)}', so its alpha is unknown."));
        }

        int width = frame.PixelWidth;
        int height = frame.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            return OperationResult.Ok(TrimResult.ManualCropRequired(
                "The decoded frame reports no pixels, so there is nothing to measure."));
        }

        // The safety-critical branch: a format with no alpha channel is never guessed at. A
        // null answer means the format alone cannot say (an indexed palette may or may not
        // carry transparency), so the decode path is attempted rather than assumed opaque.
        if (WicPixelFormats.HasAlpha(frame.Format) == false)
        {
            return OperationResult.Ok(TrimResult.ManualCropRequired(
                $"'{frame.Format}' carries no alpha channel; PrintFlow does not infer a background from colour.",
                width, height));
        }

        OperationResult<byte[]?> plane = TryReadAlphaPlane(frame, width, height, cancellationToken);
        if (plane.IsFailure)
        {
            return OperationResult.Fail<TrimResult>(plane.Failure);
        }

        if (plane.Value is not { } alpha)
        {
            return OperationResult.Ok(TrimResult.ManualCropRequired(
                $"'{frame.Format}' could not be converted to a readable alpha plane.", width, height));
        }

        if (AlphaBounds.Compute(width, height, alpha) is not { } content)
        {
            return OperationResult.Ok(TrimResult.ManualCropRequired(
                "Every pixel is fully transparent, so there is no content to crop to.", width, height));
        }

        TrimBounds applied = content.Expand(request.Margin, width, height);

        OperationResult<Unit> written = WriteCroppedPng(frame, applied, outputPath, cancellationToken);
        return written.IsFailure
            ? OperationResult.Fail<TrimResult>(written.Failure)
            : OperationResult.Ok(TrimResult.Produced(content, applied, width, height, request.ExpectedOutput));
    }

    /// <summary>
    /// Decodes the first frame fully into memory, or reports a null frame when WIC has no
    /// codec for the container.
    /// </summary>
    /// <remarks>
    /// <see cref="BitmapCacheOption.OnLoad"/> so the file handle is released before anything is
    /// written — the trim output is a sibling in the same attempt directory, and a decoder
    /// still holding the input open would be a lock waiting to happen.
    /// <see cref="BitmapCreateOptions.PreservePixelFormat"/> matters just as much: without it
    /// WIC may hand back a converted format, and the no-alpha check above would then be
    /// testing the conversion rather than the operator's file.
    /// </remarks>
    private static OperationResult<BitmapFrame?> TryDecode(string inputPath)
    {
        try
        {
            using FileStream stream = new(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            BitmapDecoder decoder = BitmapDecoder.Create(
                stream,
                BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count == 0)
            {
                return OperationResult.Ok<BitmapFrame?>(null);
            }

            BitmapFrame frame = decoder.Frames[0];
            Freeze(frame);
            return OperationResult.Ok<BitmapFrame?>(frame);
        }
        catch (NotSupportedException)
        {
            return OperationResult.Ok<BitmapFrame?>(null);
        }
        catch (FileFormatException)
        {
            return OperationResult.Ok<BitmapFrame?>(null);
        }
        catch (IOException ex)
        {
            return OperationResult.Fail<BitmapFrame?>(
                FailureCode.OutputUnreadable, $"Trim input could not be read: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult.Fail<BitmapFrame?>(
                FailureCode.OutputUnreadable, $"Trim input could not be read: {ex.Message}");
        }
    }

    /// <summary>
    /// Copies one alpha byte per pixel out of the frame, row by row.
    /// </summary>
    /// <remarks>
    /// Row-wise so the whole BGRA surface is never materialised: the peak cost is one alpha
    /// plane (width × height bytes) plus a single row, rather than the four-bytes-per-pixel
    /// image a whole-image <c>CopyPixels</c> would need. Returns null when the source cannot be
    /// presented as BGRA32 at all, which is an honest "alpha unknown", not an opaque assumption.
    /// </remarks>
    private static OperationResult<byte[]?> TryReadAlphaPlane(
        BitmapSource frame, int width, int height, CancellationToken cancellationToken)
    {
        BitmapSource bgra;
        try
        {
            if (frame.Format == PixelFormats.Bgra32)
            {
                bgra = frame;
            }
            else
            {
                FormatConvertedBitmap converted = new(frame, PixelFormats.Bgra32, null, 0);
                Freeze(converted);
                bgra = converted;
            }
        }
        catch (NotSupportedException)
        {
            return OperationResult.Ok<byte[]?>(null);
        }
        catch (InvalidOperationException)
        {
            return OperationResult.Ok<byte[]?>(null);
        }

        byte[] plane = new byte[width * height];
        byte[] row = new byte[width * BytesPerBgra32Pixel];

        try
        {
            for (int y = 0; y < height; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                bgra.CopyPixels(new Int32Rect(0, y, width, 1), row, row.Length, 0);

                int planeRow = y * width;
                for (int x = 0; x < width; x++)
                {
                    plane[planeRow + x] = row[(x * BytesPerBgra32Pixel) + 3];
                }
            }
        }
        catch (NotSupportedException)
        {
            return OperationResult.Ok<byte[]?>(null);
        }
        catch (FileFormatException ex)
        {
            return OperationResult.Fail<byte[]?>(
                FailureCode.OutputUnreadable, $"Trim input decoded to an unusable surface: {ex.Message}");
        }

        return OperationResult.Ok<byte[]?>(plane);
    }

    /// <summary>Crops <paramref name="frame"/> to <paramref name="bounds"/> and writes it as a PNG.</summary>
    /// <remarks>
    /// PNG because the whole point of the step is a transparent cut-out, and JPEG has no alpha
    /// to keep it in (Epic 11200 Part B §14). A <see cref="TrimOutcome.NoChangeRequired"/>
    /// result is written the same way rather than skipped, so the step always leaves a real,
    /// hashable artefact for its Revision and the review chain stays unbroken.
    /// </remarks>
    private static OperationResult<Unit> WriteCroppedPng(
        BitmapFrame frame, TrimBounds bounds, string outputPath, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            CroppedBitmap cropped = new(
                frame, new Int32Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height));
            Freeze(cropped);

            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(cropped));

            using FileStream output = new(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            encoder.Save(output);
            return OperationResult.Ok();
        }
        catch (IOException ex)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.WorkspaceError, $"Trimmed output could not be written: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.WorkspaceError, $"Trimmed output could not be written: {ex.Message}");
        }
        catch (NotSupportedException ex)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.OutputValidationFailed, $"Trimmed output could not be encoded as PNG: {ex.Message}");
        }
    }

    private static void Freeze(Freezable freezable)
    {
        if (freezable.CanFreeze && !freezable.IsFrozen)
        {
            freezable.Freeze();
        }
    }
}
