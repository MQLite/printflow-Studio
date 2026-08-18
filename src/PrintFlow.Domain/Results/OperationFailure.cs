namespace PrintFlow.Domain.Results;

/// <summary>
/// An expected production event: an adapter timed out, an output was unreadable, a hash
/// did not match. Distinct from <c>CommandRejection</c>, which signals a defect in the
/// caller rather than a condition of the production environment (Epic 11100 plan §9.3).
/// </summary>
/// <param name="Code">Stable English code; persisted and used for recovery routing.</param>
/// <param name="MessageKey">Resource key resolved to localised operator text at display time.</param>
/// <param name="TechnicalDetail">English detail for the local log. Never shown raw to the operator.</param>
/// <param name="Context">Additional structured detail, for example expected and actual hashes.</param>
/// <param name="IsRetryable">Whether retrying the same operation is a sensible recovery action.</param>
public sealed record OperationFailure(
    FailureCode Code,
    string MessageKey,
    string TechnicalDetail,
    IReadOnlyDictionary<string, string> Context,
    bool IsRetryable)
{
    private static readonly IReadOnlyDictionary<string, string> NoContext =
        new Dictionary<string, string>(0);

    public static OperationFailure Create(
        FailureCode code,
        string technicalDetail,
        bool isRetryable = false,
        IReadOnlyDictionary<string, string>? context = null,
        string? messageKey = null) =>
        new(code,
            messageKey ?? $"Failure_{code}",
            technicalDetail,
            context ?? NoContext,
            isRetryable);

    public override string ToString() => $"{Code}: {TechnicalDetail}";
}
