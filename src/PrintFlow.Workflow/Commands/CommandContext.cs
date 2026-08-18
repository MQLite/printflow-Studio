using PrintFlow.Domain.Ids;

namespace PrintFlow.Workflow.Commands;

/// <summary>
/// Everything ambient the reducer needs, supplied as input rather than read from the world.
/// </summary>
/// <remarks>
/// The engine never reads a clock and never generates an identifier. Both arrive here, so a
/// test can replay any transition at a fixed instant with fixed identifiers and get exactly
/// the same result (Epic 11100 plan §8.1).
/// </remarks>
/// <param name="NowUtc">The instant to stamp on state changes. Always UTC.</param>
/// <param name="Operator">Windows username, or <c>Operator</c> when unavailable (MVP design §4.1).</param>
/// <param name="NewReviewId">A pre-allocated identifier for a review this command may record.</param>
/// <param name="NewAttemptId">A pre-allocated identifier for an attempt this command may start.</param>
public sealed record CommandContext(
    DateTimeOffset NowUtc,
    string Operator,
    ReviewId NewReviewId,
    AttemptId NewAttemptId)
{
    /// <summary>The fallback operator name when the Windows username cannot be read.</summary>
    public const string UnknownOperator = "Operator";

    /// <summary>Builds a context from a <see cref="TimeProvider"/> and an id source.</summary>
    public static CommandContext Create(TimeProvider timeProvider, IIdGenerator ids, string? @operator)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(ids);

        return new CommandContext(
            timeProvider.GetUtcNow(),
            string.IsNullOrWhiteSpace(@operator) ? UnknownOperator : @operator!.Trim(),
            ReviewId.From(ids.NewId()),
            AttemptId.From(ids.NewId()));
    }
}
