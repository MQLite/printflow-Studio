using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Trimming;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Imaging;

/// <summary>
/// The real manual crop: decode with WIC, cut out the operator's rectangle, write a PNG
/// (Epic 11200 Part C2 §10, §11).
/// </summary>
/// <remarks>
/// <b>What it does not do.</b> It does not read the alpha channel, does not decide anything
/// about the content, does not resize, enhance, flatten or colour-correct, and does not call
/// Meitu, Photoshop or a segmentation model. The rectangle came from a human; this type's whole
/// job is to keep exactly those pixels and no others.
///
/// <b>No alpha requirement.</b> Unlike <see cref="DeterministicAlphaTrimProcessor"/>, a source
/// with no alpha channel is cropped normally. That case is the reason manual crop exists: the
/// automatic path refused precisely because it could not measure alpha, and refusing again here
/// would leave the operator with no route forward at all (Part C2 §27).
///
/// <b>Colour is never altered.</b> The cut runs on the decoded frame in its own pixel format via
/// <see cref="CroppedBitmap"/>, so a 16-bit-per-channel source stays 16-bit, an opaque RGB
/// source stays opaque RGB, alpha survives where it exists, and the DPI travels with the frame.
/// Nothing is converted on the way to the encoder.
///
/// The work runs on a thread-pool thread and every bitmap is frozen, for the same reasons as
/// the alpha trim: decoding a production-sized image is real CPU time, and a frozen bitmap
/// acquires no affinity to the thread that built it.
/// </remarks>
public sealed class WicManualCropProcessor : IManualCropProcessor
{
    private readonly IWorkspace _workspace;

    public WicManualCropProcessor(IWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _workspace = workspace;
    }

    /// <inheritdoc />
    public string ProcessorId => "internal-manual-crop-v1";

    /// <inheritdoc />
    public async Task<OperationResult<ManualCropResult>> CropAsync(
        ManualCropRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        string inputPath = _workspace.ResolveAbsolute(request.Input);
        string outputPath = _workspace.ResolveAbsolute(request.ExpectedOutput);

        try
        {
            return await Task.Run(
                () => Crop(request, inputPath, outputPath, cancellationToken), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Fail<ManualCropResult>(
                FailureCode.Cancelled, "The manual crop was cancelled before it produced a result.");
        }
    }

    private static OperationResult<ManualCropResult> Crop(
        ManualCropRequest request, string inputPath, string outputPath, CancellationToken cancellationToken)
    {
        if (request.Crop.IsEmpty)
        {
            return OperationResult.Fail<ManualCropResult>(
                FailureCode.PreconditionNotMet, "A manual crop rectangle contains at least one pixel.");
        }

        if (!File.Exists(inputPath))
        {
            return OperationResult.Fail<ManualCropResult>(
                FailureCode.OutputMissing, $"Manual crop input not found: '{inputPath}'.");
        }

        OperationResult<BitmapFrame?> decoded = TryDecode(inputPath);
        if (decoded.IsFailure)
        {
            return OperationResult.Fail<ManualCropResult>(decoded.Failure);
        }

        if (decoded.Value is not { } frame)
        {
            return OperationResult.Fail<ManualCropResult>(
                FailureCode.OutputUnreadable,
                $"WIC has no codec for '{Path.GetFileName(inputPath)}', so it cannot be cropped.");
        }

        int width = frame.PixelWidth;
        int height = frame.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            return OperationResult.Fail<ManualCropResult>(
                FailureCode.OutputUnreadable, "The decoded frame reports no pixels, so there is nothing to crop.");
        }

        // Refused, never clamped. The caller mapped the operator's drag onto this canvas and
        // already clamped it there; a rectangle that still does not fit means the two disagree
        // about the image, and quietly shrinking it would hide that rather than report it.
        if (!request.Crop.FitsWithin(width, height))
        {
            return OperationResult.Fail<ManualCropResult>(
                FailureCode.PreconditionNotMet,
                $"The crop {request.Crop} does not fit inside the {width}x{height} source canvas.");
        }

        OperationResult<Unit> written = WriteCroppedPng(frame, request.Crop, outputPath, cancellationToken);
        return written.IsFailure
            ? OperationResult.Fail<ManualCropResult>(written.Failure)
            : OperationResult.Ok(new ManualCropResult(request.ExpectedOutput, request.Crop, width, height));
    }

    /// <summary>
    /// Decodes the first frame fully into memory, or reports a null frame when WIC has no codec.
    /// </summary>
    /// <remarks>
    /// <see cref="BitmapCacheOption.OnLoad"/> so the input handle is closed before the output is
    /// written beside it, and <see cref="BitmapCreateOptions.PreservePixelFormat"/> so the frame
    /// handed to <see cref="CroppedBitmap"/> is the operator's own format rather than whatever
    /// WIC would have converted it to.
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
                FailureCode.OutputUnreadable, $"Manual crop input could not be read: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult.Fail<BitmapFrame?>(
                FailureCode.OutputUnreadable, $"Manual crop input could not be read: {ex.Message}");
        }
    }

    /// <summary>Cuts <paramref name="bounds"/> out of <paramref name="frame"/> and writes it as a PNG.</summary>
    /// <remarks>
    /// PNG because the step this feeds produces a transparent-capable cut-out, and because a
    /// JPEG source cropped back to JPEG would be re-compressed — a second generation of loss the
    /// operator did not ask for. A source that already carries alpha keeps it; one that does not
    /// stays opaque, because nothing here invents an alpha channel.
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
                FailureCode.WorkspaceError, $"The cropped output could not be written: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.WorkspaceError, $"The cropped output could not be written: {ex.Message}");
        }
        catch (NotSupportedException ex)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.OutputValidationFailed, $"The cropped output could not be encoded as PNG: {ex.Message}");
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
