using PrintFlow.Domain.Ids;

namespace PrintFlow.Domain.Results;

/// <summary>
/// The closed, persisted context carried by a failed or cancelled processing attempt.
/// </summary>
/// <remarks>
/// <see cref="OperationFailure.Context"/> remains the on-disk representation so existing
/// databases need no UI-only migration. These keys are nevertheless owned here: callers do
/// not invent spellings, and readers do not treat arbitrary adapter diagnostics as workflow
/// relationships. The attempt identity is what permits an <c>AutomationLogEntry</c> to enrich
/// one exact failure rather than whichever log row happens to be newest.
/// </remarks>
public static class FailureEvidence
{
    public const string AttemptIdKey = "attemptId";
    public const string ExpectedOutputPathKey = "expectedOutputPath";
    public const string ExpectedOutputEstablishedKey = "expectedOutputEstablished";

    /// <summary>Attaches the exact attempt and records that no output destination existed yet.</summary>
    public static OperationFailure ForAttempt(OperationFailure failure, AttemptId attemptId)
    {
        ArgumentNullException.ThrowIfNull(failure);

        Dictionary<string, string> context = Copy(failure.Context);
        context[AttemptIdKey] = attemptId.ToString();
        context.TryAdd(ExpectedOutputEstablishedKey, "false");
        return failure with { Context = context };
    }

    /// <summary>Records the exact output destination after the workflow has established it.</summary>
    public static OperationFailure WithExpectedOutputPath(OperationFailure failure, string absolutePath)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        Dictionary<string, string> context = Copy(failure.Context);
        context[ExpectedOutputPathKey] = absolutePath;
        context[ExpectedOutputEstablishedKey] = "true";
        return failure with { Context = context };
    }

    public static AttemptId? AttemptIdOf(OperationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return failure.Context.TryGetValue(AttemptIdKey, out string? value) &&
               Guid.TryParse(value, out Guid parsed)
            ? AttemptId.From(parsed)
            : null;
    }

    private static Dictionary<string, string> Copy(IReadOnlyDictionary<string, string> source) =>
        new(source, StringComparer.Ordinal);
}
