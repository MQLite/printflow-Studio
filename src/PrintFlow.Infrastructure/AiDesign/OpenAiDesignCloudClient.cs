using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.AiDesign;

internal static class OpenAiDesignCloudClient
{
    public const string TokenVariable = "PRINTFLOW_DESIGN_CLOUD_TOKEN";

    public static Uri? ParseEndpoint(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
            uri.AbsolutePath == "/" && uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.UserInfo.Length == 0
            ? uri : null;

    public static async Task<OperationResult<CloudDesignJob>> RunAsync(HttpClient http, Uri endpoint,
        CloudDesignInput input, string token, CancellationToken ct)
    {
        if (!input.IsValid())
            return Fail("AiPrompt_CloudReferenceLimit", FailureCode.PreconditionNotMet);

        Uri jobUri = new(endpoint, $"{CloudDesignLimits.Route}/{Guid.NewGuid():D}");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(CloudDesignLimits.CloudTimeoutMinutes + 1));
        bool accepted = false;
        try
        {
            using var submit = Request(HttpMethod.Post, jobUri, token);
            submit.Content = JsonContent.Create(input);
            using var started = await http.SendAsync(submit, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
                .ConfigureAwait(false);
            if (started.StatusCode != HttpStatusCode.Accepted)
                return Fail(started.StatusCode switch
                {
                    HttpStatusCode.TooManyRequests => "AiPrompt_CloudBusy",
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "AiPrompt_CloudAccessDenied",
                    HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.BadRequest => "AiPrompt_CloudReferenceLimit",
                    _ => "AiPrompt_CloudUnavailable"
                });
            accepted = true;

            while (true)
            {
                using var poll = Request(HttpMethod.Get, jobUri, token);
                using var status = await http.SendAsync(poll, deadline.Token).ConfigureAwait(false);
                if (!status.IsSuccessStatusCode) return Fail("AiPrompt_CloudUnavailable");

                var job = await status.Content.ReadFromJsonAsync<CloudDesignJob>(deadline.Token).ConfigureAwait(false);
                if (job?.Stage is CloudDesignStage.Generating or CloudDesignStage.Reviewing or CloudDesignStage.Redoing)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), deadline.Token).ConfigureAwait(false);
                    continue;
                }
                return job?.Stage is CloudDesignStage.Approved or CloudDesignStage.Rejected or CloudDesignStage.NeedsReview
                    ? OperationResult.Ok(job) : Fail("AiPrompt_CloudUnavailable");
            }
        }
        finally
        {
            if (accepted || deadline.IsCancellationRequested)
            {
                // Cancel a running job, or release the retained artifacts after a terminal response.
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try
                {
                    using var release = Request(HttpMethod.Delete, jobUri, token);
                    using var stopped = await http.SendAsync(release, HttpCompletionOption.ResponseHeadersRead, cleanup.Token)
                        .ConfigureAwait(false);
                }
                catch (Exception error) when (error is HttpRequestException or OperationCanceledException or ObjectDisposedException)
                {
                    // The service also expires jobs and artifacts independently.
                }
            }
        }
    }

    private static HttpRequestMessage Request(HttpMethod method, Uri uri, string token)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static OperationResult<CloudDesignJob> Fail(string messageKey, FailureCode code = FailureCode.AdapterUnavailable) =>
        OperationResult.Fail<CloudDesignJob>(OperationFailure.Create(code, messageKey, messageKey: messageKey));
}
