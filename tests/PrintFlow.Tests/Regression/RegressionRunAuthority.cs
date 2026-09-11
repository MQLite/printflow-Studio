using System.Collections.Immutable;
using PrintFlow.Infrastructure.Verification;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Regression;

/// <summary>
/// Lets the standard regression run start on a workstation whose only unmet condition is that
/// the standard regression run has never happened (SCRUM-11065; SCRUM-11123 Part H).
/// </summary>
/// <remarks>
/// <b>The circle.</b> SCRUM-11123 closes Production until a revalidation record says the
/// standard regression set passed. Recording that requires a successful run of the set. The set
/// runs real Production adapters. Production adapters are closed. Nothing in that loop is wrong
/// — each link is a rule worth having — but somewhere the first run has to be able to happen.
/// <para>
/// <b>What this does about it.</b> It asks the real verifier first, unmodified. If that answer
/// is verified, this changes nothing at all and the run proceeds on the record the workstation
/// already has. If the answer is not verified, this delegates to
/// <see cref="ProductionWorkstationVerifier.ForStandardRegressionRun"/> — the same composition
/// with the self-referential check omitted — but <b>only</b> when
/// <see cref="WorkstationVerificationCheck.ProductionRevalidation"/> is the sole thing blocking.
/// A workstation with a second problem gets the real verifier's own answer back, unaltered, and
/// the run does not start.
/// </para>
/// <para>
/// <b>What it will not do.</b> It supplies no revalidation record, real or invented. There is no
/// value <see cref="ProductionWorkstationVerifier"/> accepts as a passing record that it did not
/// read from the workspace itself, so nothing here can forge the approval the run exists to earn
/// — the question is declined, not answered. Writing a temporary passing record to disk and
/// removing <c>ProductionRevalidation</c> from <c>VerifiedEnvironmentGate</c> were the other two
/// ways out, and both were refused: the first forges the evidence, the second reopens Production
/// for the application permanently to solve a bootstrap that happens once per upgrade.
/// </para>
/// <para>
/// <b>Everything else is still required.</b> Because the bootstrapped verifier is the same
/// composition, dropping that one check lets the live phase run rather than skipping it —
/// which is the point. The preset digest and its evidence chain, the Windows build, both
/// accepted binaries, the Action artefact, the display topology, the local console session, the
/// UI culture, the workspace root, Meitu and Photoshop launchability and safe starting states,
/// Photoshop's colour settings, the test-image round trip and the global automation lock all
/// still have to pass before a single external application is driven.
/// </para>
/// </remarks>
internal sealed class RegressionBootstrapWorkstationVerifier : IProductionWorkstationVerifier
{
    private readonly IProductionWorkstationVerifier _real;
    private readonly IProductionWorkstationVerifier _withoutRevalidationCheck;

    /// <param name="real">The verifier exactly as the application composes it.</param>
    /// <param name="withoutRevalidationCheck">
    /// The same composition with the revalidation check omitted. Consulted only when the real
    /// verifier's sole complaint is that check.
    /// </param>
    internal RegressionBootstrapWorkstationVerifier(
        IProductionWorkstationVerifier real,
        IProductionWorkstationVerifier withoutRevalidationCheck)
    {
        ArgumentNullException.ThrowIfNull(real);
        ArgumentNullException.ThrowIfNull(withoutRevalidationCheck);
        _real = real;
        _withoutRevalidationCheck = withoutRevalidationCheck;
    }

    /// <summary>
    /// Whether the bootstrap has actually stood in for a missing record.
    /// </summary>
    /// <remarks>
    /// Recorded so the run result can say, in writing, that it was used. A bootstrap invisible in
    /// the evidence is a bypass.
    /// </remarks>
    internal bool BootstrapWasUsed { get; private set; }

    /// <inheritdoc />
    public WorkstationVerificationResult Verify() => Verify(ownLease: null);

    /// <inheritdoc />
    public WorkstationVerificationResult Verify(IWorkstationAutomationLease? ownLease)
    {
        WorkstationVerificationResult real = _real.Verify(ownLease);
        return OnlyRevalidationBlocks(real)
            ? Bootstrapped(_withoutRevalidationCheck.Verify(ownLease))
            : real;
    }

    /// <inheritdoc />
    public async Task<WorkstationVerificationResult> RunLiveChecksAsync(CancellationToken cancellationToken)
    {
        // The real verifier's automatic half is asked first, and cheaply: Verify() launches
        // nothing. Only once it is established that the revalidation record is the single thing
        // missing does the bootstrapped composition run the live phase, which does launch
        // applications.
        WorkstationVerificationResult real = _real.Verify();
        if (!OnlyRevalidationBlocks(real))
        {
            return await _real.RunLiveChecksAsync(cancellationToken).ConfigureAwait(false);
        }

        return Bootstrapped(
            await _withoutRevalidationCheck.RunLiveChecksAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// True when the revalidation check is the only thing standing in the way.
    /// </summary>
    /// <remarks>
    /// The safety property of the whole bootstrap, and the reason it is a question about the
    /// real verifier's answer rather than a switch. A workstation whose display topology changed,
    /// or whose Photoshop was upgraded, blocks on that as well — and then this returns false, the
    /// real answer is what the caller sees, and no application is launched.
    /// <para>
    /// A result that is already verified also returns false: there is a valid record, and the
    /// bootstrap has nothing to stand in for.
    /// </para>
    /// </remarks>
    private static bool OnlyRevalidationBlocks(WorkstationVerificationResult result)
    {
        if (result.Verified)
        {
            return false;
        }

        ImmutableArray<WorkstationCheckResult> blocking = [.. result.BlockingChecks];
        return blocking.Length == 1 &&
               blocking[0].Check == WorkstationVerificationCheck.ProductionRevalidation;
    }

    private WorkstationVerificationResult Bootstrapped(WorkstationVerificationResult result)
    {
        BootstrapWasUsed = true;
        return result;
    }
}
