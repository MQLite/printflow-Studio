using System.Buffers.Binary;
using ImageMagick;
using PrintFlow.Domain.AiDesign;

namespace PrintFlow.Design;

public sealed class DesignReviewPipeline(DesignResponses responses)
{
    public async Task<CloudDesignJob> RunAsync(CloudDesignInput input, CancellationToken ct,
        Action<CloudDesignStage>? report = null, CancellationToken? callerCancellation = null)
    {
        List<CloudDesignAttempt> attempts = [];
        string? reason = null;
        string? previousResponseId = null;
        var portrait = input.References.SingleOrDefault(reference => reference.PreserveFace);
        // A service deadline bounds upstream work. After generation, keep locally available artwork
        // for manual review on timeout; an explicit cancellation still stops the entire operation.
        var localCancellation = portrait is null ? ct : callerCancellation ?? ct;

        for (int i = 0; i < 2; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (png, generation) = await responses.GenerateAsync(input, previousResponseId, reason, ct).ConfigureAwait(false);
            ValidatePngHeader(png);
            report?.Invoke(CloudDesignStage.Reviewing);
            var pair = portrait is null || ct.IsCancellationRequested ? null : PortraitPair.TryCreate(portrait, png, localCancellation);
            var review = await Review(png, pair, false).ConfigureAwait(false);
            if (review is null)
            {
                attempts.Add(new(generation, null, ReviewUnavailable, "uncertain"));
                return NeedsReview(png, ReviewUnavailable);
            }
            attempts.Add(new(generation, review.Receipt, review.Approved ? null : review.Reason, review.FaceStatus));
            localCancellation.ThrowIfCancellationRequested();
            if (review.Approved)
                return new(CloudDesignStage.Approved, attempts.ToArray(), png, CreatePdf(png));
            if (review.OtherDesignApproved)
            {
                if (review.FaceStatus != "changed" || pair is null) return NeedsReview(png, review.Reason);

                if (ct.IsCancellationRequested) return NeedsReview(png, ReviewUnavailable);
                var restored = pair.Compose(localCancellation);
                if (restored is null)
                    return NeedsReview(png, "人脸无法可靠对齐 / Unable to align the original face reliably.");
                if (ct.IsCancellationRequested) return NeedsReview(restored, ReviewUnavailable);
                var restoredPair = PortraitPair.TryCreate(portrait!, restored, localCancellation);
                if (restoredPair is null)
                    return NeedsReview(png, "合成后无法可靠定位人脸 / Unable to verify the restored face.");

                var second = await Review(restored, restoredPair, true).ConfigureAwait(false);
                if (second is null) return NeedsReview(restored, ReviewUnavailable);
                attempts[^1] = attempts[^1] with { CompositeReview = second.Receipt };
                localCancellation.ThrowIfCancellationRequested();
                if (!second.Approved)
                    return NeedsReview(restored, second.Reason);
                return new(CloudDesignStage.Approved, attempts.ToArray(), restored, CreatePdf(restored));
            }

            reason = review.Reason;
            previousResponseId = generation.ResponseId;
            if (i == 0) report?.Invoke(CloudDesignStage.Redoing);
        }

        return new(CloudDesignStage.Rejected, attempts.ToArray(), Reason: reason);

        async Task<DesignResponses.ReviewResult?> Review(byte[] candidate, PortraitPair? pair, bool isComposite)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                return await responses.ReviewAsync(input, candidate, pair, isComposite, ct).ConfigureAwait(false);
            }
            catch (Exception error) when (portrait is not null && !localCancellation.IsCancellationRequested &&
                error is HttpRequestException or IOException or InvalidDataException or System.Text.Json.JsonException or OperationCanceledException)
            {
                // Keep the last submitted raster for manual confirmation when the upstream review cannot finish.
                return null;
            }
        }

        CloudDesignJob NeedsReview(byte[] candidate, string why)
        {
            localCancellation.ThrowIfCancellationRequested();
            return new(CloudDesignStage.NeedsReview, attempts.ToArray(), candidate, Reason: why);
        }
    }

    private const string ReviewUnavailable = "API 人脸审稿未完成，请人工确认 / The portrait review could not complete. Check the candidate manually.";

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
