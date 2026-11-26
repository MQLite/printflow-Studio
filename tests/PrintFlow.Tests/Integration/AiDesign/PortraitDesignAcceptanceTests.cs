using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using ImageMagick;
using PrintFlow.Design;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Infrastructure.Imaging;

namespace PrintFlow.Tests.Integration.AiDesign;

public sealed class PortraitDesignAcceptanceTests
{
    private const int Width = 540;
    private const int Height = 400;
    private const string Brief = "制作横向档案肖像卡，深青背景，左侧放参考照片，右侧排出 Portrait study 和 ARCHIVE SERIES，" +
        "上下各有一条细线。严格保留参考人物的五官、脸型和露齿笑容，不美化或重绘脸部。";

    [Theory]
    [InlineData("preserved")]
    [InlineData("changed")] // A visual false positive cannot override the identical source face pixels.
    public async Task Preserved_smile_delivers_the_first_candidate_byte_for_byte(string visualFaceStatus)
    {
        var candidate = Fixture("preserved");
        using var upstream = new PortraitResponses(candidate, visualFaceStatus);
        var job = await RunAsync(upstream);

        job.Stage.ShouldBe(CloudDesignStage.Approved, job.Reason);
        upstream.GenerationCount.ShouldBe(1);
        upstream.SubmittedCandidates.Count.ShouldBe(1);
        upstream.SubmittedCandidates[0].SequenceEqual(candidate).ShouldBeTrue();
        job.Png.ShouldNotBeNull().SequenceEqual(candidate).ShouldBeTrue();
        await CheckPdfAsync("preserved", job);
    }

    [Fact]
    public async Task Changed_smile_is_restored_locally_and_only_the_reviewed_pixels_are_exported()
    {
        var candidate = Fixture("changed");
        using var upstream = new PortraitResponses(candidate, "changed", "preserved");
        var job = await RunAsync(upstream);

        job.Stage.ShouldBe(CloudDesignStage.Approved, job.Reason);
        upstream.GenerationCount.ShouldBe(1);
        upstream.SubmittedCandidates.Count.ShouldBe(2);
        upstream.SubmittedCandidates[0].SequenceEqual(candidate).ShouldBeTrue();
        upstream.CandidateFaces[0].SequenceEqual(upstream.CandidateFaces[1]).ShouldBeFalse();
        var result = job.Png.ShouldNotBeNull();
        upstream.SubmittedCandidates[1].SequenceEqual(result).ShouldBeTrue();
        await CheckPdfAsync("restored", job);

        var before = Rgb(candidate);
        var after = Rgb(result);
        var expected = Rgb(Fixture("preserved"));
        var originalError = MeanDifference(before, expected, 148, 250, 220, 280);
        originalError.ShouldBeGreaterThan(8);
        MeanDifference(after, expected, 148, 250, 220, 280).ShouldBeLessThan(originalError * 0.7);
        for (int y = 0; y < Height; y++)
        {
            // Compare the complete design, including type, rules and clothing, outside the portrait face.
            if (y is < 80 or >= 348)
                SameRowSegment(before, after, y, 0, Width);
            else
            {
                SameRowSegment(before, after, y, 0, 72);
                SameRowSegment(before, after, y, 308, Width);
            }
        }
    }

    [Theory]
    [InlineData("changed", "changed", true)]
    [InlineData("changed", "uncertain", true)]
    [InlineData("changed", "preserved", false)]
    [InlineData("uncertain", null, true)]
    public async Task Unresolved_review_keeps_the_actual_candidate_for_manual_inspection(
        string first, string? afterRepair, bool finalDesignApproved)
    {
        var judgments = afterRepair is null ? new[] { first } : new[] { first, afterRepair };
        using var upstream = new PortraitResponses(Fixture("changed"), judgments) { FinalDesignApproved = finalDesignApproved };
        var job = await RunAsync(upstream);
        job.Stage.ShouldBe(CloudDesignStage.NeedsReview);
        var retained = job.Png.ShouldNotBeNull();
        retained.SequenceEqual(upstream.SubmittedCandidates[^1]).ShouldBeTrue();
        job.Reason.ShouldNotBeNullOrWhiteSpace();
        job.Pdf.ShouldBeNull();
        upstream.GenerationCount.ShouldBe(1);
        upstream.SubmittedCandidates.Count.ShouldBe(judgments.Length);
    }

    [Theory]
    [InlineData(1, "http")]
    [InlineData(2, "http")]
    [InlineData(1, "invalid-json")]
    [InlineData(2, "invalid-json")]
    [InlineData(1, "missing-verdict")]
    [InlineData(2, "missing-verdict")]
    public async Task Failed_portrait_review_retains_the_image_submitted_to_that_review(int failedReview, string failure)
    {
        var candidate = Fixture("changed");
        using var upstream = new PortraitResponses(candidate, "changed", "preserved")
        {
            InterceptReview = number => number != failedReview ? null : failure switch
            {
                "http" => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
                "invalid-json" => ReviewReply("{"),
                "missing-verdict" => ReviewReply("""{"otherDesignApproved":true,"reason":""}"""),
                _ => throw new ArgumentOutOfRangeException(nameof(failure))
            }
        };

        var job = await RunAsync(upstream);

        upstream.GenerationCount.ShouldBe(1);
        upstream.SubmittedCandidates.Count.ShouldBe(failedReview);
        job.Stage.ShouldBe(CloudDesignStage.NeedsReview);
        job.Pdf.ShouldBeNull();
        job.Reason.ShouldNotBeNullOrWhiteSpace();
        var retained = job.Png.ShouldNotBeNull();
        retained.SequenceEqual(upstream.SubmittedCandidates[^1]).ShouldBeTrue();
        retained.SequenceEqual(candidate).ShouldBe(failedReview == 1);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Caller_cancellation_during_review_propagates_without_returning_a_candidate(int cancelledReview)
    {
        using var cancellation = new CancellationTokenSource();
        using var upstream = new PortraitResponses(Fixture("changed"), "changed", "preserved")
        {
            InterceptReview = number =>
            {
                if (number == cancelledReview) cancellation.Cancel();
                return null;
            }
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(upstream, cancellation.Token));
        upstream.GenerationCount.ShouldBe(1);
        upstream.SubmittedCandidates.Count.ShouldBe(cancelledReview);
    }

    private static HttpResponseMessage ReviewReply(string verdict) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(new { id = "resp_incompleteverdict", status = "completed",
            output = new[] { new { type = "message", role = "assistant", content = new[] {
                new { type = "output_text", text = verdict } } } } })
    };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Review_deadline_preserves_last_candidate_for_confirmation(int overdueReview)
    {
        using var deadline = new CancellationTokenSource();
        using var upstream = new PortraitResponses(Fixture("changed"), "changed", "preserved")
        {
            InterceptReview = number =>
            {
                if (number == overdueReview) deadline.Cancel();
                return null;
            }
        };
        var job = await RunAsync(upstream, deadline.Token, CancellationToken.None, stage =>
        {
            if (stage == CloudDesignStage.Reviewing && overdueReview == 0) deadline.Cancel();
        });
        job.Stage.ShouldBe(CloudDesignStage.NeedsReview);
        upstream.SubmittedCandidates.Count.ShouldBe(overdueReview);
        job.Png.ShouldBe(overdueReview == 0 ? Fixture("changed") : upstream.SubmittedCandidates[^1]);
        job.Pdf.ShouldBeNull();
    }

    private static async Task<CloudDesignJob> RunAsync(PortraitResponses upstream, CancellationToken ct = default,
        CancellationToken? callerCancellation = null, Action<CloudDesignStage>? report = null)
    {
        // Exercise the same serialized reference flag that crosses the workstation/cloud boundary.
        var request = JsonSerializer.SerializeToElement(new
        {
            Prompt = Brief,
            References = new[] { new { Name = "portrait.png", MediaType = "image/png",
                Data = Fixture("reference"), PreserveFace = true } }
        }).Deserialize<CloudDesignInput>()!;
        using var http = new HttpClient(upstream, disposeHandler: false);
        return await new DesignReviewPipeline(new DesignResponses(http, "test-project-key", "gpt-image-test"))
            .RunAsync(request, ct, report, callerCancellation);
    }

    private static byte[] Fixture(string name)
    {
        using var stream = typeof(PortraitDesignAcceptanceTests).Assembly
            .GetManifestResourceStream($"PrintFlow.Tests.Fixtures.Portrait.{name}.png").ShouldNotBeNull();
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] Rgb(byte[] bytes)
    {
        using var image = new MagickImage(bytes);
        image.Width.ShouldBe((uint)Width);
        image.Height.ShouldBe((uint)Height);
        using var pixels = image.GetPixels();
        return pixels.ToByteArray(PixelMapping.RGB)!;
    }

    private static double MeanDifference(byte[] first, byte[] second, int left, int top, int right, int bottom)
    {
        long sum = 0;
        for (int y = top; y < bottom; y++)
            for (int i = (y * Width + left) * 3; i < (y * Width + right) * 3; i++)
                sum += Math.Abs(first[i] - second[i]);
        return (double)sum / ((right - left) * (bottom - top) * 3);
    }

    private static void SameRowSegment(byte[] first, byte[] second, int y, int left, int right)
    {
        int offset = (y * Width + left) * 3;
        int length = (right - left) * 3;
        first.AsSpan(offset, length).SequenceEqual(second.AsSpan(offset, length)).ShouldBeTrue($"row {y}, x={left}..{right}");
    }

    private static async Task CheckPdfAsync(string name, CloudDesignJob job)
    {
        var pdf = job.Pdf.ShouldNotBeNull();
        using var input = new MemoryStream(pdf);
        using var document = await new WindowsPdfDocumentAuthority().OpenAsync(input, CancellationToken.None);
        document.PageCount.ShouldBe(1);
        document.InspectSinglePage();
        var preview = await document.RenderThumbnailAsync(Width, CancellationToken.None);
        var png = job.Png.ShouldNotBeNull();
        var expected = Rgb(png);
        var rendered = Rgb(preview.Payload.ToArray());
        rendered.AsSpan().SequenceEqual(expected).ShouldBeTrue("the full-size PDF page must reproduce every approved RGB pixel");
        if (Environment.GetEnvironmentVariable("PRINTFLOW_PORTRAIT_ARTIFACTS") is { Length: > 0 } folder)
        {
            Directory.CreateDirectory(folder);
            await File.WriteAllBytesAsync(Path.Combine(folder, name + ".pdf"), pdf);
            await File.WriteAllBytesAsync(Path.Combine(folder, name + ".png"), png);
        }
    }

    // Only the external image generator and vision verdict are scripted. Production builds every request and artifact.
    private sealed class PortraitResponses(byte[] candidate, params string[] verdicts) : HttpMessageHandler
    {
        public int GenerationCount { get; private set; }
        public bool FinalDesignApproved { get; init; } = true;
        public Func<int, HttpResponseMessage?>? InterceptReview { get; init; }
        public List<byte[]> SubmittedCandidates { get; } = [];
        public List<byte[]> CandidateFaces { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var root = body.RootElement;
            object[] output;
            if (root.TryGetProperty("tools", out _))
            {
                GenerationCount++;
                output = [new { type = "image_generation_call", id = "ig_candidate", status = "completed",
                    result = Convert.ToBase64String(candidate) }];
            }
            else
            {
                var images = root.GetProperty("input")[0].GetProperty("content").EnumerateArray()
                    .Where(part => part.GetProperty("type").GetString() == "input_image")
                    .Select(part => part.GetProperty("image_url").GetString()!).ToArray();
                var schema = root.GetProperty("text").GetProperty("format").GetProperty("schema");
                schema.GetProperty("properties").TryGetProperty("faceStatus", out _).ShouldBeTrue();
                // Full views provide design context, while two additional views isolate each face for comparison.
                images.Length.ShouldBe(4);
                CandidateFaces.Add(DecodeImage(images[^2]));
                SubmittedCandidates.Add(DecodeImage(images[^1]));
                if (InterceptReview?.Invoke(SubmittedCandidates.Count) is { } response) return response;
                ct.ThrowIfCancellationRequested();
                string faceStatus = verdicts[Math.Min(SubmittedCandidates.Count, verdicts.Length) - 1];
                bool otherDesignApproved = SubmittedCandidates.Count == 1 || FinalDesignApproved;
                var verdict = new { otherDesignApproved, faceStatus,
                    reason = !otherDesignApproved ? "合成后边缘有接缝。" :
                        faceStatus == "preserved" ? "" : "参考人像中的露齿笑容尚未确认保留。" };
                output = [new { type = "message", role = "assistant", content = new[] {
                    new { type = "output_text", text = JsonSerializer.Serialize(verdict) } } }];
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { id = $"resp_sample{GenerationCount}_{SubmittedCandidates.Count}",
                    status = "completed", output })
            };
        }

        private static byte[] DecodeImage(string url) => Convert.FromBase64String(url[(url.IndexOf(',') + 1)..]);
    }
}
