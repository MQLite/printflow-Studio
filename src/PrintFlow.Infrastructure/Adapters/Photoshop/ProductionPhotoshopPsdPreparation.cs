using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

internal sealed record PhotoshopValidatedPsdCandidate(string AbsolutePath, FileFacts Facts, PsdInspection Inspection);

public sealed partial class ProductionPhotoshopOutputProcessor
{
    internal IPhotoshopPsdNativeBridge PsdNative { get; init; } = new RotPhotoshopPsdNativeBridge();

    public async Task<OperationResult<AdapterOutput>> PreparePsdAsync(
        PsdPreparationRequest request, CancellationToken cancellationToken)
    {
        DateTimeOffset started = _clock.GetUtcNow();
        PsdInspection? inspection = null;
        OperationResult<AdapterOutput> Fail(OperationFailure failure) =>
            OperationResult.Fail<AdapterOutput>(failure with { PsdInspection = inspection });
        void CheckStop()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.Stop.RequestedMode is not null) throw new OperationCanceledException("Operator stopped PSD preparation.");
        }
        try
        {
            CheckStop();
            if (request.Input.Area != WorkspaceArea.Working || request.ExpectedOutput.Area != WorkspaceArea.Working)
                return Fail(OperationFailure.Create(FailureCode.PsdPreparationFailed, "PSD preparation requires managed Working paths."));
            string input = _workspace.ResolveAbsolute(request.Input);
            string output = _workspace.ResolveAbsolute(request.ExpectedOutput);
            if (!string.Equals(Path.GetDirectoryName(input), Path.GetDirectoryName(output), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(input, output, StringComparison.OrdinalIgnoreCase) || File.Exists(output))
                return Fail(OperationFailure.Create(FailureCode.PsdPreparationFailed, "PSD output must be a new sibling in this attempt."));
            OperationResult<bool> composite = PsdCompositeProbe.Inspect(input);
            if (composite.IsFailure) return Fail(composite.Failure);
            byte[] sourceHash = HashPsdFile(input);
            var ready = await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            if (ready.IsFailure) return Fail(ready.Failure);
            var opened = await OpenManagedWorkingFileAsync(request.Input, cancellationToken).ConfigureAwait(false);
            if (opened.IsFailure) return Fail(opened.Failure);
            var baseline = _baselines.GetVerifiedBaseline();
            if (baseline.IsFailure) return Fail(baseline.Failure);
            var guard = await _preparer.VerifyExactMutationTargetAsync(opened.Value, baseline.Value, cancellationToken).ConfigureAwait(false);
            if (guard.IsFailure) return Fail(guard.Failure);
            CheckStop();
            request.Stop.ReportPhase(ExternalOperationPhase.OperationRequested);
            var exported = PsdNative.ExportOnce(new PsdNativeCommand(input, output), baseline.Value);
            if (!sourceHash.SequenceEqual(HashPsdFile(input)))
                return Fail(OperationFailure.Create(FailureCode.RevisionIntegrityMismatch, "PSD working input changed during preparation."));
            if (exported.IsFailure) return Fail(exported.Failure);
            inspection = exported.Value.Inspection;
            // A synchronous Photoshop call cannot be recalled. Once it returns, a Stop or
            // Take Over prevents all further external input, including document cleanup.
            CheckStop();
            OperationFailure? unsupported = !exported.Value.Succeeded && inspection is not null &&
                (inspection.HasSpots || inspection.HasW1 || inspection.OriginalMode != "RGB" || inspection.BitDepth != 8)
                ? OperationFailure.Create(FailureCode.PsdUnsupported, "PSD was not prepared: " + exported.Value.Detail)
                : null;
            var after = await _preparer.VerifyExactMutationTargetAsync(opened.Value, baseline.Value,
                CancellationToken.None).ConfigureAwait(false);
            if (after.IsFailure) return Fail(WithCleanupFailure(unsupported, after.Failure));
            // Cleanup re-proves ownership and handles only the accepted closed prompt contract.
            var closed = await CloseExactDocumentAsync(opened.Value, request.Input, CancellationToken.None).ConfigureAwait(false);
            if (closed.IsFailure) return Fail(WithCleanupFailure(unsupported, closed.Failure));
            CheckStop();
            if (!exported.Value.Succeeded || inspection is null)
                return Fail(OperationFailure.Create(
                    inspection is { HasSpots: true } or { HasW1: true } ||
                    inspection is not null && (inspection.OriginalMode != "RGB" || inspection.BitDepth != 8)
                        ? FailureCode.PsdUnsupported : FailureCode.PsdPreparationFailed,
                    "PSD was not prepared: " + exported.Value.Detail));

            // Two equal independent reads separated by a bounded settle poll. A script return
            // alone never establishes a file, its format, its alpha, or its hash.
            WicFileInspector reader = new();
            FileFacts? previous = null;
            DateTimeOffset deadline = _clock.GetUtcNow() + _options.TiffSettleTimeout;
            do
            {
                CheckStop();
                var observed = await reader.InspectAsync(output, cancellationToken).ConfigureAwait(false);
                if (observed.IsSuccess && previous is { } first && observed.Value.Sha256 == first.Sha256)
                {
                    FileFacts facts = observed.Value;
                    var validated = ValidatePsdRaster(output, facts, inspection);
                    if (validated.IsFailure) return Fail(validated.Failure);
                    var result = PhotoshopAdapterOutputFactory.CreatePsd(request, validated.Value, _workspace, _clock.GetUtcNow() - started,
                        $"PSD RGB/8 prepared by Photoshop {inspection.PhotoshopVersion}; composite verified; full canvas retained; " +
                        "source unchanged; exact document closed; no automatic retry.");
                    return result.IsFailure ? Fail(result.Failure) : result;
                }
                previous = observed.IsSuccess ? observed.Value : null;
                await Task.Delay(_options.PollInterval, _clock, cancellationToken).ConfigureAwait(false);
            } while (_clock.GetUtcNow() < deadline);
            return Fail(OperationFailure.Create(File.Exists(output) ? FailureCode.OutputUnreadable : FailureCode.OutputMissing,
                "PSD preparation output did not settle into a readable PNG."));
        }
        catch (OperationCanceledException)
        {
            return Fail(OperationFailure.Create(FailureCode.Cancelled, "PSD preparation was cancelled; no raster Revision was created."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return Fail(OperationFailure.Create(FailureCode.PsdPreparationFailed, "PSD preparation failed: " + ex.Message));
        }
    }

    internal static OperationResult<PhotoshopValidatedPsdCandidate> ValidatePsdRaster(string path, FileFacts facts, PsdInspection inspection)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[26]; stream.ReadExactly(header);
            if (facts.Format != ImageFormat.Png || header[24] != 8 || header[25] is not (2 or 6) ||
                facts.ColourMode != ColourMode.Rgb || facts.PixelWidth != inspection.PixelWidth ||
                facts.PixelHeight != inspection.PixelHeight || inspection.OriginalMode != "RGB" ||
                inspection.BitDepth != 8 || inspection.HasSpots || inspection.HasW1 || !inspection.HasRealMergedData)
                return Invalid("Prepared PNG does not match RGB/8, channels, or full PSD canvas.");
            stream.Position = 0;
            BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            FormatConvertedBitmap pixels = new(decoder.Frames[0], PixelFormats.Bgra32, null, 0);
            byte[] row = new byte[checked(pixels.PixelWidth * 4)];
            bool transparency = false;
            for (int y = 0; y < pixels.PixelHeight && !transparency; y++)
            {
                pixels.CopyPixels(new System.Windows.Int32Rect(0, y, pixels.PixelWidth, 1), row, row.Length, 0);
                for (int x = 3; x < row.Length; x += 4) if (row[x] != 255) { transparency = true; break; }
            }
            return inspection.HasTransparency == transparency
                ? OperationResult.Ok(new PhotoshopValidatedPsdCandidate(Path.GetFullPath(path), facts, inspection))
                : Invalid("PNG transparency differs from Photoshop's composite observation.");
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or OverflowException)
        {
            return Invalid("Prepared PNG could not be independently decoded: " + ex.Message);
        }
    }

    private static OperationResult<PhotoshopValidatedPsdCandidate> Invalid(string detail) =>
        OperationResult.Fail<PhotoshopValidatedPsdCandidate>(FailureCode.PsdPreparationFailed, detail);

    private static byte[] HashPsdFile(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return SHA256.HashData(stream);
    }

    private static OperationFailure WithCleanupFailure(OperationFailure? primary, OperationFailure cleanup) =>
        primary is null ? cleanup : primary with
        {
            Context = new Dictionary<string, string>(primary.Context)
            {
                ["cleanupFailureCode"] = cleanup.Code.ToString(),
                ["cleanupFailureDetail"] = cleanup.TechnicalDetail,
                ["retainedExternalState"] = "unknown",
            },
        };
}
