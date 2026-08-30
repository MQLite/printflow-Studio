using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Outputs;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// Reads one variant out of an attempt's <see cref="PhotoshopPreparation"/> snapshot
/// (Epic 11400 Part B1A.2D §24).
/// </summary>
/// <remarks>
/// A test convenience only, and deliberately a narrowing one: it answers "what did this attempt
/// run under, <i>if</i> it was a maximum-bound run", and returns null rather than adapting a
/// target-edge run into a shape it never had. An assertion that wants the other contract asks
/// <see cref="TargetEdgePreparationOf"/> and gets the same treatment.
/// <para>
/// It exists so the maximum-bound tests written before the flexible-size contract keep asserting
/// exactly what they asserted: that a plan is snapshotted onto the attempt and never rewritten.
/// Nothing here is a production accessor — product code switches over the union.
/// </para>
/// </remarks>
internal static class AttemptPreparationExtensions
{
    /// <summary>The maximum-bound plan this attempt ran under, or null when it ran under neither.</summary>
    public static PrintPreparationPlan? BoundsPlan(this ProcessingAttempt attempt) =>
        (attempt?.Preparation as FitWithinBoundsPreparation)?.Plan;

    /// <summary>The target-edge preparation this attempt ran under, authority included.</summary>
    public static TargetEdgePreparation? TargetEdgePreparationOf(this ProcessingAttempt attempt) =>
        attempt?.Preparation as TargetEdgePreparation;
}
