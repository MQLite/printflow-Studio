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

    /// <summary>
    /// The same command, stamped at a later instant, for the second transition of an operation
    /// that spans two transactions (SCRUM-11137 prerequisite §3).
    /// </summary>
    /// <remarks>
    /// A producing step is not one transition but two: an opening transaction that records the
    /// attempt as <c>Running</c>, then — after work that can take minutes — a closing one that
    /// records what it became. One context served both, so the closing transaction stamped the
    /// instant the operation <i>started</i>, and every attempt in the database came back with
    /// <c>EndedAtUtc == StartedAtUtc</c>. That is what this exists to prevent.
    /// <para>
    /// The identifiers are deliberately carried across unchanged. <see cref="NewAttemptId"/> is
    /// how the closing command names the attempt the opening one started, so a context that
    /// re-allocated it would close an attempt that never existed. Only the clock reading moves —
    /// which is the one thing that legitimately differs between the two transitions.
    /// </para>
    /// <para>
    /// A derivation rather than a second <see cref="Create"/> call, so the closing instant is
    /// observed once and then used for the whole closing transaction: the attempt's terminal
    /// timestamp, the step it moves to, and the session's <c>UpdatedAtUtc</c> all agree, instead
    /// of being three separate readings of a clock that moves between them.
    /// </para>
    /// </remarks>
    public CommandContext At(DateTimeOffset nowUtc) => this with { NowUtc = nowUtc };

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
