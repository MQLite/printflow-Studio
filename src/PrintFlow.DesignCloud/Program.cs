using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using PrintFlow.Design;
using PrintFlow.Domain.AiDesign;

namespace PrintFlow.DesignCloud;

internal static class CloudDesignProgram
{
    private static Task Main(string[] args) => BuildApp(args,
        Environment.GetEnvironmentVariable("OPENAI_API_KEY")?.Trim() ?? "",
        Environment.GetEnvironmentVariable("PRINTFLOW_DESIGN_CLOUD_TOKEN") ?? "").RunAsync();

    internal static WebApplication BuildApp(string[] args, string apiKey, string accessToken, HttpMessageHandler? upstream = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || !CloudDesignLimits.IsValidAccessToken(accessToken))
            throw new InvalidOperationException("Set OPENAI_API_KEY and PRINTFLOW_DESIGN_CLOUD_TOKEN before starting the design service.");

        var builder = WebApplication.CreateBuilder(args);
        byte[] accessHash = SHA256.HashData(Encoding.ASCII.GetBytes(accessToken));
        string imageModel = builder.Configuration["Design:ImageModel"] ?? "gpt-image-2.6-sunburst";
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = CloudDesignLimits.MaxUploadBytes);
        builder.Services.AddSingleton(_ => new HttpClient(upstream ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            ConnectTimeout = TimeSpan.FromSeconds(15)
        })
        {
            Timeout = TimeSpan.FromMinutes(5),
            MaxResponseContentBufferSize = 56 * 1024 * 1024
        });
        builder.Services.AddSingleton(p => new DesignResponses(p.GetRequiredService<HttpClient>(), apiKey, imageModel));
        builder.Services.AddSingleton<DesignReviewPipeline>();
        builder.Services.AddSingleton<DesignJobs>();

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments(CloudDesignLimits.Route))
            {
                context.Response.Headers.CacheControl = "no-store";
                if (!AuthenticationHeaderValue.TryParse(context.Request.Headers.Authorization, out var header) ||
                    !header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) ||
                    !CloudDesignLimits.IsValidAccessToken(header.Parameter) ||
                    !CryptographicOperations.FixedTimeEquals(accessHash, SHA256.HashData(Encoding.ASCII.GetBytes(header.Parameter!))))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
            }
            await next(context);
        });

        app.MapGet("/healthz", () => Results.Ok());
        var designs = app.MapGroup(CloudDesignLimits.Route);
        designs.MapPost("/{id:guid}", (Guid id, CloudDesignInput input, DesignJobs jobs) =>
        {
            if (id == Guid.Empty || !input.IsValid()) return Results.BadRequest();
            return jobs.Start(id, input) ? Results.Accepted() : Results.StatusCode(StatusCodes.Status429TooManyRequests);
        });
        designs.MapGet("/{id:guid}", (Guid id, DesignJobs jobs) =>
            jobs.Find(id) is { } job ? Results.Ok(job) : Results.NotFound());
        designs.MapDelete("/{id:guid}", (Guid id, DesignJobs jobs) =>
        {
            jobs.CancelOrForget(id);
            return Results.NoContent();
        });
        return app;
    }
}
