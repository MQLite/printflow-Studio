using PrintFlow.Domain.Files;

namespace PrintFlow.Domain.AiDesign;

public static class CloudDesignLimits
{
    public const string Route = "/v1/designs";
    public const int MaxPromptLength = 32_000;
    public const int MaxReferences = 8;
    public const int MaxReferenceBytes = 20 * 1024 * 1024;
    public const int MaxUploadBytes = 48 * 1024 * 1024;
    public const int MaxCombinedReferenceBytes = 35 * 1024 * 1024;
    public const int MaxArtifactBytes = 40 * 1024 * 1024;
    public const int MaxArtifactPixels = 16_000_000;
    public const int MaxReasonLength = 500;
    public const int CloudTimeoutMinutes = 12;
    public const string ReviewReasonKey = "cloudReviewReason";

    public static bool IsValidAccessToken(string? token) =>
        token is { Length: >= 32 and <= 256 } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
}

public sealed record CloudDesignReference(string Name, string MediaType, byte[] Data);

public sealed record CloudDesignInput(string Prompt, CloudDesignReference[] References)
{
    public bool IsValid(long maxCombinedReferenceBytes = CloudDesignLimits.MaxCombinedReferenceBytes) =>
        !string.IsNullOrWhiteSpace(Prompt) && Prompt.Length <= CloudDesignLimits.MaxPromptLength &&
        References is { Length: <= CloudDesignLimits.MaxReferences } &&
        References.All(r => r is { Name: { Length: > 0 and <= 255 }, Data.Length: > 0 and <= CloudDesignLimits.MaxReferenceBytes }
            && r.MediaType is "image/png" or "image/jpeg" or "image/webp") &&
        References.Sum(r => (long)r.Data.Length) <= maxCombinedReferenceBytes;
}

public enum CloudDesignStage { Generating, Reviewing, Redoing, Approved, Rejected, Unavailable, Cancelled }

public sealed record CloudDesignAttempt(OpenAiDesignReceipt Generation, OpenAiDesignReceipt Review, string? Finding = null);

public sealed record CloudDesignReceipt(CloudDesignAttempt[] Attempts, Sha256 PdfSha256);

public sealed record CloudDesignJob(CloudDesignStage Stage, CloudDesignAttempt[] Attempts,
    byte[]? Png = null, byte[]? Pdf = null, string? Reason = null);
