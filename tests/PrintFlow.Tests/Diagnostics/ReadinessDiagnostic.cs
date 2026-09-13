using PrintFlow.Infrastructure.Gate;
using PrintFlow.Infrastructure.Verification;
using PrintFlow.Tests.Regression;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Diagnostics;

/// <summary>
/// The two verifiers a readiness diagnosis composes: the application's own, and the same
/// composition with the self-referential revalidation question omitted.
/// </summary>
/// <param name="Real">The verifier exactly as the application composes it.</param>
/// <param name="WithoutRevalidationCheck">
/// <see cref="ProductionWorkstationVerifier.ForStandardRegressionRun"/> over the same inputs.
/// Consulted only when the real verifier's sole complaint is the revalidation record.
/// </param>
internal sealed record ReadinessDiagnosticVerifiers(
    IProductionWorkstationVerifier Real,
    IProductionWorkstationVerifier WithoutRevalidationCheck);

/// <summary>
/// What one readiness diagnosis observed. Explicitly not an authorisation (PF-AUDIT-R4).
/// </summary>
/// <remarks>
/// <b>Two reports, both kept.</b> <see cref="NormalReport"/> is what the application's own gate
/// says about this workstation right now, unaltered — including the revalidation failure that
/// closes Production. <see cref="DiagnosticReport"/> is what the bounded live procedure found.
/// Keeping the first is what stops the second from reading as permission: a diagnosis that
/// discarded the real answer would be indistinguishable from a bypass.
/// <para>
/// <b>What a <c>Verified</c> of true in <see cref="DiagnosticReport"/> means.</b> That this
/// diagnostic's checks passed, and nothing else. It is not normal App permission, it is not a
/// standard-regression result, and it is not an input any revalidation record may quote —
/// <c>ProductionRevalidationEvaluator</c> refuses an outcome that names no run and invocation,
/// and nothing here supplies one.
/// </para>
/// </remarks>
/// <param name="Kind">A constant discriminator, so a stray file cannot be mistaken for readiness.json.</param>
/// <param name="DiagnosticId">Fresh per invocation; never reused and never a lease token.</param>
/// <param name="StartedAtUtc">When this invocation began, distinct from either report's observation time.</param>
/// <param name="RevalidationCheckOmitted">
/// Whether the omission actually stood in. False when the workstation already had a valid record,
/// and false when something other than the record was also blocking — in which case
/// <see cref="DiagnosticReport"/> is the real verifier's own refusal.
/// </param>
internal sealed record ReadinessDiagnosticEnvelope(
    string Kind,
    string DiagnosticId,
    DateTimeOffset StartedAtUtc,
    bool RevalidationCheckOmitted,
    EnvironmentReadinessReport NormalReport,
    EnvironmentReadinessReport DiagnosticReport)
{
    /// <summary>Stated in the artefact itself, not only in the documentation around it.</summary>
    public string Authority { get; } =
        "Diagnostic only. A Verified result here means this diagnosis' checks passed on this " +
        "workstation at this moment. It grants no Production authorisation, records no standard " +
        "regression set result, and writes no production revalidation record.";

    /// <summary>Production authorisation as the application decides it: the unaltered normal answer.</summary>
    public bool ProductionAuthorised => NormalReport.Verified;
}

/// <summary>
/// A bounded, opt-in readiness diagnosis for PF-AUDIT-R4.
/// </summary>
/// <remarks>
/// <b>Why this exists.</b> The existing live entry —
/// <c>WorkstationVerificationSmoke.Run_the_explicit_live_application_verification</c> — requires
/// every automatic check to pass before its live phase runs. On this workstation the only one
/// failing is <c>ProductionRevalidation</c>, whose record is written after a standard regression
/// run that itself needs the live phase to work. The probe therefore cannot be reached at all,
/// and the readiness fault R4 exists to localise cannot be observed.
/// <para>
/// <b>What it reuses rather than rebuilds.</b> The omission is
/// <see cref="ProductionWorkstationVerifier.ForStandardRegressionRun"/>, which is
/// <c>internal</c> to Infrastructure and therefore unreachable from PrintFlow.App; the decision
/// of whether it may be used is <see cref="RegressionBootstrapWorkstationVerifier"/>, which asks
/// the real verifier first and stands aside unless the revalidation record is the single thing
/// blocking. Neither is copied or widened here — this adds a second internal caller of a seam
/// that already existed, and no new capability. A workstation whose display changed, whose
/// Photoshop was upgraded or whose preset fails to hash still gets the real verifier's refusal.
/// </para>
/// <para>
/// <b>What it will not do.</b> It supplies no revalidation record, real or invented, and there is
/// no value the verifier accepts as a passing one that it did not read from the workspace itself.
/// It composes a verifier and a gate and nothing else: no <c>SessionService</c>, no adapters, no
/// workflow, no registration in the ordinary application graph, and no persisted live cache. It
/// filters no live check — Meitu launchability and safe starting state are required exactly as
/// Photoshop's are, so this cannot become a Photoshop-only pass.
/// </para>
/// </remarks>
internal static class ReadinessDiagnostic
{
    /// <summary>The opt-in. Distinct from the two existing smoke variables on purpose.</summary>
    internal const string OptInVariable = "PRINTFLOW_READINESS_DIAGNOSTIC";

    /// <summary>Marks the envelope as what it is, in the artefact.</summary>
    internal const string EnvelopeKind = "printflow.readiness-diagnostic.v1";

    /// <summary>Nothing but the exact opt-in value runs this.</summary>
    internal static bool IsOptedIn(string? optInValue) =>
        string.Equals(optInValue, "1", StringComparison.Ordinal);

    /// <summary>
    /// Runs one diagnosis, or none at all.
    /// </summary>
    /// <param name="optInValue">The opt-in variable's current value.</param>
    /// <param name="compose">
    /// Builds the verifiers. Invoked <b>only</b> after the opt-in is satisfied, so that a default
    /// test run performs no composition, opens no store and reaches no external application —
    /// the guard precedes operational setup rather than sitting inside it.
    /// </param>
    /// <param name="clock">Stamps this invocation.</param>
    /// <returns><c>null</c> when not opted in; otherwise the diagnostic-only envelope.</returns>
    internal static async Task<ReadinessDiagnosticEnvelope?> RunAsync(
        string? optInValue,
        Func<ReadinessDiagnosticVerifiers> compose,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(compose);
        ArgumentNullException.ThrowIfNull(clock);

        if (!IsOptedIn(optInValue))
        {
            return null;
        }

        ReadinessDiagnosticVerifiers verifiers = compose();
        string diagnosticId = Guid.NewGuid().ToString("N");
        DateTimeOffset startedAt = clock.GetUtcNow();

        // The real verifier's own answer, asked first and kept whatever happens next. Read()
        // launches nothing; it is the passive question the application asks on every request.
        EnvironmentReadinessReport normal =
            new VerifiedEnvironmentGate(verifiers.Real).Read();

        // The admission decision belongs to the existing bootstrap, which re-asks the real
        // verifier itself and delegates to the omitting composition only when the revalidation
        // record is the sole blocker. When it is not, this returns the real refusal and the live
        // phase stays Blocked — no application is launched and no lease is acquired.
        RegressionBootstrapWorkstationVerifier admission =
            new(verifiers.Real, verifiers.WithoutRevalidationCheck);

        EnvironmentReadinessReport diagnostic = await new VerifiedEnvironmentGate(admission)
            .RunLiveChecksAsync(cancellationToken)
            .ConfigureAwait(false);

        // The diagnostic report is carried exactly as it came back: its Verified value, its
        // checks and its Lifecycle/LatestProbe stages are the evidence, and rewriting any of
        // them to look like a normal authorisation is the one thing this must never do.
        return new ReadinessDiagnosticEnvelope(
            EnvelopeKind,
            diagnosticId,
            startedAt,
            admission.BootstrapWasUsed,
            normal,
            diagnostic);
    }
}
