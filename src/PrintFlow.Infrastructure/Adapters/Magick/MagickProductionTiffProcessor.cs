using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using ImageMagick;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Infrastructure.Preset;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Adapters.Magick;

/// <summary>Produces a reviewable CMYK and W1 TIFF from the workflow's exact raster and geometry.</summary>
public sealed class MagickProductionTiffProcessor(IWorkspace workspace, string presetManifestPath, Sha256 expectedPresetHash)
    : ILocalProductionTiffProcessor
{
    private const int MaximumEdge = 8192;
    // 570 × 360 mm at 360 DPI, rounded to the nearest pixel on each axis.
    private const long MaximumPixels = 8_079L * 5_102;
    private const long MaximumInputBytes = 128L * 1024 * 1024;
    private const string WorkingRgb = "sRGB IEC61966-2.1";
    private const string WorkingCmyk = "Coated FOGRA39 (ISO 12647-2:2004)";
    private readonly Lazy<bool> _hasProductionProfile = new(() => SupportsProductionProfile(presetManifestPath, expectedPresetHash));

    public string AdapterId => "local-magick-cmyk-w1-v1";

    public OperationFailure? GetAvailability(FileFacts input, PhotoshopPreparation? preparation)
    {
        if (input.Format is not (ImageFormat.Png or ImageFormat.Jpeg) ||
            input.ByteLength is <= 0 or > MaximumInputBytes || !input.HasPixelDimensions ||
            !LocalImageFiles.Fits((uint)input.PixelWidth!.Value, (uint)input.PixelHeight!.Value, MaximumEdge, MaximumPixels))
            return UnsupportedRaster();

        if (preparation is not null && !FitsOutput(preparation))
            return OutputDimensionsLimit();

        return GetProductionProfileFailure();
    }

    public Task<OperationResult<AdapterOutput>> GenerateAsync(PhotoshopRequest request, CancellationToken cancellationToken) =>
        Task.Run(() => Generate(request, cancellationToken), cancellationToken);

    private OperationResult<AdapterOutput> Generate(PhotoshopRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (GetProductionProfileFailure() is { } unavailable)
            return OperationResult.Fail<AdapterOutput>(unavailable);
        if (request.Preset.ManifestSha256 != expectedPresetHash)
            return OperationResult.Fail<AdapterOutput>(PresetMismatch());

        var preparation = request.Preparation;
        int width = preparation.ProjectedPixelWidth;
        int height = preparation.ProjectedPixelHeight;
        if (!FitsOutput(preparation))
            return OperationResult.Fail<AdapterOutput>(OutputDimensionsLimit());

        var stopwatch = Stopwatch.StartNew();
        try
        {
            string directory = workspace.ResolveAbsoluteDirectory(request.WorkingDirectory);
            string input = workspace.ResolveAbsolute(request.ApprovedInput);
            string output = workspace.ResolveAbsolute(request.ExpectedOutput);
            if (!IsReservedDestination(request, directory, input, output))
                return OperationResult.Fail<AdapterOutput>(FailureCode.PreconditionNotMet,
                    "Local TIFF generation requires the workflow's distinct reserved working files.");

            ReparsePointGuard.RefuseAncestry(input);
            ReparsePointGuard.RefuseAncestry(directory);
            using var stream = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is <= 0 or > MaximumInputBytes)
                return OperationResult.Fail<AdapterOutput>(UnsupportedRaster());

            if (Sha256.FromBytes(SHA256.HashData(stream)) != preparation.SourceSha256)
                return OperationResult.Fail<AdapterOutput>(FailureCode.RevisionIntegrityMismatch,
                    "The TIFF working raster differs from the reviewed raster used for sizing.");

            stream.Position = 0;
            MagickFormat format = ReadSupportedFormat(stream);
            if (format == MagickFormat.Unknown)
                return OperationResult.Fail<AdapterOutput>(UnsupportedRaster());

            var readSettings = new MagickReadSettings { Format = format };
            stream.Position = 0;
            var info = new MagickImageInfo(stream, readSettings);
            if (!LocalImageFiles.Fits(info.Width, info.Height, MaximumEdge, MaximumPixels))
                return OperationResult.Fail<AdapterOutput>(UnsupportedRaster());

            stream.Position = 0;
            using var image = new MagickImage(stream, readSettings);
            image.Progress += (_, progress) => progress.Cancel = token.IsCancellationRequested;
            // Geometry and review use the stored pixel grid, like the WIC reader used upstream.
            if (image.Width != preparation.SourcePixelWidth || image.Height != preparation.SourcePixelHeight ||
                image.ColorSpace is not (ColorSpace.sRGB or ColorSpace.RGB or ColorSpace.Gray or ColorSpace.CMYK))
                return OperationResult.Fail<AdapterOutput>(UnsupportedRaster());

            if (preparation.Resamples)
            {
                image.FilterType = preparation.ResizePolicy == PhotoshopResizeMode.BicubicSharper
                    ? FilterType.Catrom : FilterType.Mitchell;
                image.Resize(new MagickGeometry((uint)width, (uint)height) { IgnoreAspectRatio = true });
            }

            if (image.Width != width || image.Height != height)
                return OperationResult.Fail<AdapterOutput>(OutputFailure("The raster does not match the approved TIFF dimensions."));

            token.ThrowIfCancellationRequested();
            var targetProfile = ColorProfiles.CoatedFOGRA39;
            if (!ConvertToProductionCmyk(image, targetProfile))
                return OperationResult.Fail<AdapterOutput>(OutputFailure("The raster could not be converted to the production CMYK profile."));

            using var pixels = image.GetPixels();
            byte[]? cmyk = pixels.ToByteArray(PixelMapping.CMYK);
            byte[]? alpha = image.HasAlpha ? pixels.ToByteArray("A") : OpaqueAlpha(width, height);
            if (cmyk is null || alpha is null || cmyk.Length != width * height * 4 || alpha.Length != width * height)
                return OperationResult.Fail<AdapterOutput>(OutputFailure("The converted CMYK raster or alpha plane could not be read."));

            byte[] composite = MakeComposite(cmyk, alpha, width, height, (int)request.Branch, token);
            FileWorkspace.WriteNewWorkingCandidate(output,
                file => ProductionTiffWriter.Write(file, width, height, composite, cmyk, alpha, targetProfile.ToByteArray(), token), token);

            using var finished = new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.Read);
            var inspected = new ProductionTiffInspector().Inspect(output);
            if (inspected.IsFailure)
                return OperationResult.Fail<AdapterOutput>(inspected.Failure);
            if (ProductionTiffPreparationMatch.Check(inspected.Value, preparation) is { } mismatch)
                return OperationResult.Fail<AdapterOutput>(mismatch);

            token.ThrowIfCancellationRequested();
            string notes = string.Create(CultureInfo.InvariantCulture,
                $"Magick.NET; Coated FOGRA39; CMYK + {request.Branch}; {width}x{height} px at 300 PPI; independently validated TIFF structure.");
            return OperationResult.Ok(new AdapterOutput(request.ExpectedOutput, stopwatch.Elapsed, notes)
            {
                ValidatedImageSha256 = Sha256.FromBytes(SHA256.HashData(finished)),
            });
        }
        catch (Exception error) when (error is MagickException or InvalidDataException or OverflowException)
        {
            token.ThrowIfCancellationRequested();
            return OperationResult.Fail<AdapterOutput>(OutputFailure("The local TIFF encoder could not process the selected raster."));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            token.ThrowIfCancellationRequested();
            return OperationResult.Fail<AdapterOutput>(FailureCode.WorkspaceError,
                "The local TIFF encoder could not read or write the assigned working files.");
        }
    }

    private OperationFailure? GetProductionProfileFailure()
    {
        if (LocalImageFiles.MagickRuntimeFailure is { } runtimeFailure)
            return runtimeFailure;

        return _hasProductionProfile.Value ? null : PresetMismatch();
    }

    private static bool SupportsProductionProfile(string manifestPath, Sha256 expectedHash)
    {
        var verified = VerifiedJsonFile.Read(manifestPath, expectedHash, "Workstation preset manifest");
        if (verified.IsFailure)
            return false;

        using var manifest = verified.Value;
        if (manifest.RootElement.ValueKind != JsonValueKind.Object ||
            !manifest.RootElement.TryGetProperty("photoshopContract", out var contract) ||
            contract.ValueKind != JsonValueKind.Object)
            return false;

        var colour = PresetPhotoshopBaselineProvider.ReadColourSettings(contract);
        return colour.IsSuccess && colour.Value is { RgbWorkingSpace: WorkingRgb, CmykWorkingSpace: WorkingCmyk };
    }

    private static bool FitsOutput(PhotoshopPreparation preparation) => LocalImageFiles.Fits(
        (uint)preparation.ProjectedPixelWidth, (uint)preparation.ProjectedPixelHeight, MaximumEdge, MaximumPixels);

    private static bool IsReservedDestination(PhotoshopRequest request, string directory, string input, string output) =>
        request.ApprovedInput.Area == WorkspaceArea.Working && request.ExpectedOutput.Area == WorkspaceArea.Working &&
        request.ApprovedInput.RelativePath.StartsWith(request.WorkingDirectory.RelativePath + '/', StringComparison.OrdinalIgnoreCase) &&
        string.Equals(directory, Path.GetDirectoryName(input), StringComparison.OrdinalIgnoreCase) &&
        string.Equals(directory, Path.GetDirectoryName(output), StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(input, output, StringComparison.OrdinalIgnoreCase) &&
        request.ExpectedOutput.FileName == request.OutputFileName && Path.GetExtension(output) == ".tif" &&
        Enum.IsDefined(request.Branch);

    private static MagickFormat ReadSupportedFormat(Stream stream)
    {
        Span<byte> header = stackalloc byte[FormatSniffer.RequiredHeaderLength];
        if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) != header.Length)
            return MagickFormat.Unknown;

        return FormatSniffer.Detect(header) switch
        {
            ImageFormat.Png => MagickFormat.Png,
            ImageFormat.Jpeg => MagickFormat.Jpeg,
            _ => MagickFormat.Unknown,
        };
    }

    private static bool ConvertToProductionCmyk(MagickImage image, IColorProfile target)
    {
        var input = image.GetColorProfile();
        if (image.ColorSpace == ColorSpace.CMYK &&
            (input is null || input.ToByteArray().AsSpan().SequenceEqual(target.ToByteArray())))
            return true;

        image.RenderingIntent = RenderingIntent.Perceptual;
        image.BlackPointCompensation = true;
        if (input is null)
        {
            image.ColorSpace = ColorSpace.sRGB;
            input = ColorProfiles.SRGB;
        }
        return image.TransformColorSpace(input, target) && image.ColorSpace == ColorSpace.CMYK;
    }

    private static byte[] OpaqueAlpha(int width, int height)
    {
        var alpha = new byte[checked(width * height)];
        Array.Fill(alpha, byte.MaxValue);
        return alpha;
    }

    private static byte[] MakeComposite(byte[] cmyk, byte[] alpha, int width, int height, int contraction, CancellationToken token)
    {
        byte[] underbase = contraction == 0 ? alpha : ContractAlpha(alpha, width, height, contraction, token);

        var composite = new byte[checked(alpha.Length * 5)];
        for (int y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (int i = y * width; i < (y + 1) * width; i++)
            {
                for (int channel = 0; channel < 4; channel++)
                    composite[i * 5 + channel] = (byte)((cmyk[i * 4 + channel] * alpha[i] + 127) / 255);
                composite[i * 5 + 4] = (byte)(255 - underbase[i]);
            }
        }
        return composite;
    }

    private static byte[] ContractAlpha(byte[] alpha, int width, int height, int radius, CancellationToken token)
    {
        // The Photoshop references use a 3x3 cross for 1 px and a 5x5 window without corners
        // for 2 px. Clip at the canvas edge and retain the minimum fractional coverage.
        var contracted = new byte[alpha.Length];
        for (int y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                byte minimum = alpha[row + x];
                for (int dy = -radius; dy <= radius && minimum != 0; dy++)
                {
                    int neighbourRow = Math.Clamp(y + dy, 0, height - 1) * width;
                    int horizontal = Math.Abs(dy) == radius ? radius - 1 : radius;
                    int right = Math.Min(x + horizontal, width - 1);
                    for (int nx = Math.Max(x - horizontal, 0); nx <= right; nx++)
                        minimum = Math.Min(minimum, alpha[neighbourRow + nx]);
                }
                contracted[row + x] = minimum;
            }
        }
        return contracted;
    }

    private static OperationFailure UnsupportedRaster() => OperationFailure.Create(FailureCode.SourceFormatUnsupported,
        $"Local TIFF requires a supported PNG or JPEG up to {MaximumEdge} pixels per edge, {MaximumPixels} pixels total and 128 MiB.",
        messageKey: "ProductionTiff_MagickUnsupportedInput");

    private static OperationFailure OutputDimensionsLimit() => OperationFailure.Create(FailureCode.PreconditionNotMet,
        $"The projected TIFF exceeds {MaximumEdge} pixels per edge or {MaximumPixels} pixels total. Reduce its size or choose Photoshop.",
        messageKey: "ProductionTiff_MagickDimensionsLimit");

    private static OperationFailure PresetMismatch() => OperationFailure.Create(FailureCode.EnvironmentNotVerified,
        "The verified preset must specify sRGB IEC61966-2.1 and Coated FOGRA39 for the local TIFF encoder.",
        messageKey: "ProductionTiff_MagickPresetMismatch");

    private static OperationFailure OutputFailure(string detail) => OperationFailure.Create(FailureCode.OutputValidationFailed,
        detail, isRetryable: true, messageKey: "ProductionTiff_MagickOutputInvalid");
}
