using System.Globalization;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

/// <summary>
/// The bounded settle policy the prepared PSD raster is observed under (SCRUM-11099; Epic 11400
/// Part A §6).
/// </summary>
/// <param name="Timeout">How long a still-changing output is waited on.</param>
/// <param name="Interval">The gap between two observations.</param>
/// <param name="RequiredEqualObservations">
/// How many consecutive equal observations count as settled.
/// </param>
/// <remarks>
/// Two numbers that were previously read straight off <see cref="PhotoshopAutomationOptions"/>
/// and used in the same loop, now named together with the third number that was only ever
/// implicit — the count the stability rule needs. Keeping them apart is what allowed a
/// combination that cannot be satisfied to look like a valid configuration.
///
/// <para>
/// The invariant is <see cref="MinimumObservationWindow"/>: establishing stability needs
/// <see cref="RequiredEqualObservations"/> observations, which are separated by
/// <see cref="RequiredEqualObservations"/> − 1 intervals, so a budget shorter than that cannot
/// be satisfied by any file, however well behaved. Before this type existed, such a policy did
/// not fail — it produced <c>OutputUnreadable</c> whenever the machine was slow enough, which
/// reads as "the output was bad" when it means "the budget was impossible".
/// </para>
/// <para>
/// The invariant is necessary but not sufficient on its own, and deliberately so: it bounds the
/// configuration, while <see cref="ProductionPhotoshopOutputProcessor.PreparePsdAsync"/> bounds
/// the algorithm by never letting the deadline end the poll before those observations have
/// actually been taken. A budget can always be exhausted by one slow read; the two rules
/// together are what make the outcome depend on the file rather than on the machine.
/// </para>
/// </remarks>
internal sealed record PsdOutputSettlePolicy(
    TimeSpan Timeout, TimeSpan Interval, int RequiredEqualObservations)
{
    /// <summary>
    /// The accepted contract: two equal independent reads (SCRUM-11099).
    /// </summary>
    /// <remarks>
    /// Two, not the Meitu adapter's three. The two rules are measuring different things: Meitu
    /// compares byte lengths from a probe that costs nothing, where a writer flushing in chunks
    /// routinely produces one interval of quiet, so a third observation buys real margin. This
    /// rule compares SHA-256 over the whole file, which a half-written raster cannot match by
    /// accident, and it is the accepted PSD contract. Raising it here would be a silent policy
    /// change, not a determinism fix.
    /// </remarks>
    internal const int AcceptedEqualObservations = 2;

    /// <summary>
    /// The shortest elapsed window in which the stability rule can possibly be satisfied.
    /// </summary>
    internal TimeSpan MinimumObservationWindow =>
        Interval * (RequiredEqualObservations - 1);

    /// <summary>
    /// Builds the policy, refusing combinations no output could ever satisfy.
    /// </summary>
    /// <remarks>
    /// A failure rather than a throw because this runs on a workflow seam: the caller returns it
    /// before Photoshop is touched, so an impossible policy costs nothing and names itself,
    /// instead of surviving to the end of the poll disguised as an unreadable output.
    /// </remarks>
    internal static OperationResult<PsdOutputSettlePolicy> Create(
        TimeSpan timeout, TimeSpan interval, int requiredEqualObservations = AcceptedEqualObservations)
    {
        if (requiredEqualObservations < 2)
        {
            return Reject(
                $"a settle rule needs at least two observations to compare, not " +
                $"{requiredEqualObservations.ToString(CultureInfo.InvariantCulture)}");
        }

        if (interval < TimeSpan.Zero)
        {
            return Reject($"the poll interval may not be negative, but is {Describe(interval)}");
        }

        PsdOutputSettlePolicy policy = new(timeout, interval, requiredEqualObservations);
        return timeout < policy.MinimumObservationWindow
            ? Reject(
                $"a settle timeout of {Describe(timeout)} cannot hold the " +
                $"{requiredEqualObservations.ToString(CultureInfo.InvariantCulture)} observations " +
                $"the stability rule requires, which need at least " +
                $"{Describe(policy.MinimumObservationWindow)} at a {Describe(interval)} poll interval")
            : OperationResult.Ok(policy);
    }

    private static OperationResult<PsdOutputSettlePolicy> Reject(string detail) =>
        OperationResult.Fail<PsdOutputSettlePolicy>(
            FailureCode.PsdPreparationFailed,
            "The PSD output settle policy is impossible to satisfy: " + detail +
            ". No PSD preparation was attempted.");

    private static string Describe(TimeSpan value) =>
        value.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture) + " ms";
}
