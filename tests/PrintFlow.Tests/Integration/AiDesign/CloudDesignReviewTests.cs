using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ImageMagick;
using PrintFlow.DesignCloud;
using PrintFlow.Domain.AiDesign;

namespace PrintFlow.Tests.Integration.AiDesign;

public sealed class CloudDesignReviewTests
{
    [Theory]
    [InlineData(true, true, 1, true)]
    [InlineData(false, true, 2, true)]
    [InlineData(false, false, 2, false)]
    public async Task Only_visually_approved_candidates_are_delivered(bool firstPassed, bool secondPassed,
        int attempts, bool delivered)
    {
        using var red = new MagickImage(MagickColors.Red, 32, 32);
        using var yellow = new MagickImage(MagickColors.Yellow, 32, 32);
        byte[] firstImage = red.ToByteArray(MagickFormat.Png);
        byte[] correctedImage = yellow.ToByteArray(MagickFormat.Png);
        using var transport = new ResponsesStub(firstImage, correctedImage, firstPassed, secondPassed);
        using var http = new HttpClient(transport);
        var pipeline = new DesignReviewPipeline(new DesignResponses(http, "test-project-key", "gpt-image-test"));

        var job = await pipeline.RunAsync(new CloudDesignInput("保留布局，把红色改为黄色。",
            [new("layout.png", "image/png", firstImage)]), CancellationToken.None);

        job.Attempts.Length.ShouldBe(attempts);
        transport.Requests.Count.ShouldBe(attempts * 2);
        transport.Requests.ShouldAllBe(request => request.GetProperty("store").GetBoolean());
        job.Stage.ShouldBe(delivered ? CloudDesignStage.Approved : CloudDesignStage.Rejected);
        if (delivered)
        {
            job.Png.ShouldBe(firstPassed ? firstImage : correctedImage);
            Encoding.ASCII.GetString(job.Pdf!, 0, 5).ShouldBe("%PDF-");
        }
        else
        {
            job.Png.ShouldBeNull();
            job.Pdf.ShouldBeNull();
            job.Reason.ShouldBe("图形仍为红色");
        }

        var review = transport.Requests.ElementAt(1);
        review.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean().ShouldBeTrue();
        var reviewContent = review.GetProperty("input")[0].GetProperty("content");
        reviewContent[reviewContent.GetArrayLength() - 1].GetProperty("image_url").GetString()
            .ShouldBe($"data:image/png;base64,{Convert.ToBase64String(firstImage)}");
        if (attempts == 2)
        {
            var correction = transport.Requests.ElementAt(2);
            correction.GetProperty("previous_response_id").GetString().ShouldBe("resp_generation1");
            correction.GetProperty("tools")[0].GetProperty("action").GetString().ShouldBe("edit");
            correction.GetProperty("input")[0].GetProperty("content")[0].GetProperty("text").GetString()!
                .ShouldContain("图形仍为红色");
        }
    }

    private sealed class ResponsesStub(byte[] firstImage, byte[] correctedImage, bool firstPassed, bool secondPassed)
        : HttpMessageHandler
    {
        public ConcurrentQueue<JsonElement> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            request.RequestUri!.AbsolutePath.ShouldBe("/v1/responses");
            using var parsed = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var body = parsed.RootElement.Clone();
            Requests.Enqueue(body);
            int attempt = (Requests.Count + 1) / 2;
            bool generation = body.TryGetProperty("tools", out _);
            object[] output = generation
                ? [new { type = "image_generation_call", status = "completed", id = $"ig_test{attempt}",
                    result = Convert.ToBase64String(attempt == 1 ? firstImage : correctedImage) }]
                : [new { type = "message", role = "assistant", content = new[]
                    { new { type = "output_text", text = JsonSerializer.Serialize(new
                        { approved = attempt == 1 ? firstPassed : secondPassed, reason = "图形仍为红色" }) } } }];
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new
                {
                    id = generation ? $"resp_generation{attempt}" : $"resp_review{attempt}",
                    status = "completed",
                    output,
                    usage = new { input_tokens = 30, output_tokens = 30, total_tokens = 60 }
                })
            };
        }
    }

    [Fact]
    public async Task Cloud_endpoint_authenticates_and_only_returns_terminal_approved_artifacts()
    {
        using var image = new MagickImage(MagickColors.Yellow, 32, 32);
        byte[] candidate = image.ToByteArray(MagickFormat.Png);
        var upstream = new ResponsesStub(candidate, candidate, firstPassed: true, secondPassed: true);
        const string token = "test_cloud_token_for_authorized_clients";
        await using var server = CloudDesignProgram.BuildApp(
            ["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default", "Warning"], "test-project-key", token, upstream);
        await server.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(server.Urls.Single()) };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        string path = $"{CloudDesignLimits.Route}/{Guid.NewGuid():D}";
        var input = new CloudDesignInput("Make a yellow square.", []);

        (await client.PostAsJsonAsync(path, input, timeout.Token)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        upstream.Requests.ShouldBeEmpty();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await client.PostAsJsonAsync(path, input, timeout.Token)).StatusCode.ShouldBe(HttpStatusCode.Accepted);

        CloudDesignJob job;
        do
        {
            job = (await client.GetFromJsonAsync<CloudDesignJob>(path, timeout.Token)).ShouldNotBeNull();
            if (job.Stage is not (CloudDesignStage.Generating or CloudDesignStage.Reviewing)) break;
            job.Png.ShouldBeNull();
            job.Pdf.ShouldBeNull();
            await Task.Delay(20, timeout.Token);
        } while (true);

        job.Stage.ShouldBe(CloudDesignStage.Approved);
        job.Png.ShouldBe(candidate);
        job.Pdf.ShouldNotBeNull();
        upstream.Requests.Count.ShouldBe(2);
        (await client.DeleteAsync(path, timeout.Token)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await client.GetAsync(path, timeout.Token)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Cancelling_a_cloud_task_cancels_its_active_response_and_forgets_the_job()
    {
        var upstream = new PendingResponse();
        const string token = "test_cloud_token_for_authorized_clients";
        await using var server = CloudDesignProgram.BuildApp(
            ["--urls", "http://127.0.0.1:0", "--Logging:LogLevel:Default", "Warning"], "test-project-key", token, upstream);
        await server.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(server.Urls.Single()) };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        string path = $"{CloudDesignLimits.Route}/{Guid.NewGuid():D}";
        using var submitted = await client.PostAsJsonAsync(path, new CloudDesignInput("Make a yellow square.", []), timeout.Token);
        submitted.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await upstream.Started.Task.WaitAsync(timeout.Token);

        using var stopped = await client.DeleteAsync(path, timeout.Token);
        stopped.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await upstream.Cancelled.Task.WaitAsync(timeout.Token);
        using var status = await client.GetAsync(path, timeout.Token);
        status.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private sealed class PendingResponse : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Started.TrySetResult();
            using var registration = ct.Register(() => Cancelled.TrySetResult());
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("This request is expected to be cancelled.");
        }
    }
}
