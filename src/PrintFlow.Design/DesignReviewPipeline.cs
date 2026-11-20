using System.Buffers.Binary;
using ImageMagick;
using PrintFlow.Domain.AiDesign;

namespace PrintFlow.Design;

public sealed class DesignReviewPipeline(DesignResponses responses)
{
    public async Task<CloudDesignJob> RunAsync(CloudDesignInput input, CancellationToken ct,
        Action<CloudDesignStage>? report = null)
    {
        List<CloudDesignAttempt> attempts = [];
        string? reason = null;
        string? previousResponseId = null;

        for (int i = 0; i < 2; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (png, generation) = await responses.GenerateAsync(input, previousResponseId, reason, ct).ConfigureAwait(false);
            ValidatePngHeader(png);
            report?.Invoke(CloudDesignStage.Reviewing);
            var (approved, finding, review) = await responses.ReviewAsync(input, png, ct).ConfigureAwait(false);
            attempts.Add(new(generation, review, approved ? null : finding));
            ct.ThrowIfCancellationRequested();
            if (approved)
                return new(CloudDesignStage.Approved, attempts.ToArray(), png, CreatePdf(png));

            reason = finding;
            previousResponseId = generation.ResponseId;
            if (i == 0) report?.Invoke(CloudDesignStage.Redoing);
        }

        return new(CloudDesignStage.Rejected, attempts.ToArray(), Reason: reason);
    }

    private static void ValidatePngHeader(byte[] bytes)
    {
        ReadOnlySpan<byte> png = bytes;
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (png.Length < 33 || !png[..8].SequenceEqual(signature) || !png[12..16].SequenceEqual("IHDR"u8))
            throw new InvalidDataException("Generated file is not a PNG.");
        uint width = BinaryPrimitives.ReadUInt32BigEndian(png[16..20]);
        uint height = BinaryPrimitives.ReadUInt32BigEndian(png[20..24]);
        if (width == 0 || height == 0 || (ulong)width * height > CloudDesignLimits.MaxArtifactPixels)
            throw new InvalidDataException("Generated PNG exceeds the pixel limit.");
    }

    private static byte[] CreatePdf(byte[] png)
    {
        using var image = new MagickImage(png, new MagickReadSettings { Format = MagickFormat.Png });
        var density = image.Density.ChangeUnits(DensityUnit.PixelsPerInch);
        if (density.X <= 0 || density.Y <= 0 || density.Units == DensityUnit.Undefined)
            image.Density = new Density(96, 96, DensityUnit.PixelsPerInch);

        byte[] pdf = image.ToByteArray(MagickFormat.Pdf);
        if (pdf.Length is 0 or > CloudDesignLimits.MaxArtifactBytes || !pdf.AsSpan().StartsWith("%PDF-"u8))
            throw new InvalidDataException("Cloud PDF output is invalid.");
        return pdf;
    }
}
