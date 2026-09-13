using System.Reflection;
using System.Security.Cryptography;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Verification;
using PrintFlow.Tests.Diagnostics;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Unit.Diagnostics;

/// <summary>
/// The admission boundary of the PF-AUDIT-R4 readiness diagnosis.
/// </summary>
/// <remarks>
/// Synthetic throughout: every verifier here is a stub, so nothing in this file composes a real
/// workstation verifier, opens the default automation lease store, or reaches an application.
/// What is under test is the boundary itself — when the diagnosis may run, what it may omit, what
/// it must keep, and what it must never be mistaken for.
/// </remarks>
public sealed class ReadinessDiagnosticTests
{
    // ----------------------------------------------------------------------------------------
    // The opt-in
    // ----------------------------------------------------------------------------------------

    /// <summary>
    /// Without the opt-in nothing is composed, so nothing can be acquired or launched.
    /// </summary>
    /// <remarks>
    /// The guard has to sit in front of composition rather than inside the run, because the
    /// composition is what names the real lease store and the accepted binaries. A recording
    /// delegate is the direct way to state that: if it was never invoked, no store was opened and
    /// no external call was possible, whatever the rest of the method would have done.
    /// </remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("true")]
    [InlineData("2")]
    [InlineData(" 1")]
    public async Task Without_the_opt_in_nothing_is_composed(string? optIn)
    {
        int composed = 0;

        ReadinessDiagnosticEnvelope? envelope = await ReadinessDiagnostic.RunAsync(
            optIn,
            () => { composed++; return Verifiers(Blocking(), Blocking()); },
            TimeProvider.System,
            CancellationToken.None);

        envelope.ShouldBeNull();
        composed.ShouldBe(0, "the opt-in guard must precede every operational step");
    }

    /// <summary>An opted-in run composes exactly once: one lease authority, one foundation pair.</summary>
    [Fact]
    public async Task An_opted_in_run_composes_exactly_once()
    {
        int composed = 0;
        StubVerifier real = new(Result(Passed(WorkstationVerificationCheck.PresetIntegrity)));

        (await ReadinessDiagnostic.RunAsync(
            "1",
            () => { composed++; return Verifiers(real, real); },
            TimeProvider.System,
            CancellationToken.None)).ShouldNotBeNull();

        composed.ShouldBe(1);
    }

    // ----------------------------------------------------------------------------------------
    // What may be omitted, and when
    // ----------------------------------------------------------------------------------------

    /// <summary>
    /// A sole revalidation blocker lets the diagnostic procedure run, with every other check kept.
    /// </summary>
    [Fact]
    public async Task A_sole_revalidation_blocker_allows_the_diagnostic_procedure()
    {
        StubVerifier real = new(Result(
            Passed(WorkstationVerificationCheck.PresetIntegrity),
            Passed(WorkstationVerificationCheck.PhotoshopExecutable),
            FailedCheck(WorkstationVerificationCheck.ProductionRevalidation)));

        // The same composition with that one question absent: the live phase can now run, and
        // every live check is still present and still had to pass.
        StubVerifier omitted = new(Result(
            Passed(WorkstationVerificationCheck.PresetIntegrity),
            Passed(WorkstationVerificationCheck.PhotoshopExecutable),
            Passed(WorkstationVerificationCheck.MeituLaunchability),
            Passed(WorkstationVerificationCheck.MeituSafeStartingState),
            Passed(WorkstationVerificationCheck.PhotoshopSafeStartingState),
            Passed(WorkstationVerificationCheck.PhotoshopColourSettings),
            Passed(WorkstationVerificationCheck.PhotoshopTestImageRoundTrip)));

        ReadinessDiagnosticEnvelope envelope = await Run(real, omitted);

        envelope.RevalidationCheckOmitted.ShouldBeTrue();
        envelope.DiagnosticReport.Verified.ShouldBeTrue();

        // Meitu is not filtered out to obtain a Photoshop-only pass.
        envelope.DiagnosticReport.Checks
            .ShouldContain(c => c.CheckKey == nameof(WorkstationVerificationCheck.MeituLaunchability));
        envelope.DiagnosticReport.Checks
            .ShouldContain(c => c.CheckKey == nameof(WorkstationVerificationCheck.MeituSafeStartingState));

        // And the real answer is retained beside it, unaltered and still refusing.
        envelope.NormalReport.Verified.ShouldBeFalse();
        envelope.NormalReport.Checks.ShouldContain(c =>
            c.CheckKey == nameof(WorkstationVerificationCheck.ProductionRevalidation) &&
            c.Status == EnvironmentCheckStatus.Failed);
        envelope.ProductionAuthorised.ShouldBeFalse();
    }

    /// <summary>
    /// Any second failure stays blocking, and the real verifier's own answer is what comes back.
    /// </summary>
    /// <remarks>
    /// The safety property. A workstation whose preset no longer hashes, whose Photoshop was
    /// upgraded or whose display changed is failing something no diagnosis may wave through — so
    /// the omitting verifier is never consulted even though it would say yes.
    /// </remarks>
    [Theory]
    [InlineData(WorkstationVerificationCheck.PresetIntegrity)]
    [InlineData(WorkstationVerificationCheck.EvidenceIntegrity)]
    [InlineData(WorkstationVerificationCheck.PhotoshopExecutable)]
    [InlineData(WorkstationVerificationCheck.DisplayConfiguration)]
    [InlineData(WorkstationVerificationCheck.PhotoshopSafeStartingState)]
    public async Task Another_failed_prerequisite_stays_blocking(WorkstationVerificationCheck also)
    {
        StubVerifier real = new(Result(
            Passed(WorkstationVerificationCheck.OperatingSystem),
            FailedCheck(also),
            FailedCheck(WorkstationVerificationCheck.ProductionRevalidation)));

        // Deliberately a verifier that would say yes — it verifies on its own, so a wrongly
        // consulted omission would turn the diagnostic report green. It must never be asked.
        StubVerifier omitted = new(Result(
            Passed(WorkstationVerificationCheck.PresetIntegrity),
            Passed(WorkstationVerificationCheck.OperatingSystem)));
        (await omitted.RunLiveChecksAsync(CancellationToken.None)).Verified.ShouldBeTrue();

        ReadinessDiagnosticEnvelope envelope = await Run(real, omitted);

        envelope.RevalidationCheckOmitted.ShouldBeFalse();
        envelope.DiagnosticReport.Verified.ShouldBeFalse();
        envelope.DiagnosticReport.Checks.ShouldContain(c =>
            c.CheckKey == also.ToString() && c.Status == EnvironmentCheckStatus.Failed);

        // The revalidation row is still there: the diagnosis did not relabel a second failure as
        // a revalidation-only block.
        envelope.DiagnosticReport.Checks.ShouldContain(c =>
            c.CheckKey == nameof(WorkstationVerificationCheck.ProductionRevalidation));
    }

    /// <summary>
    /// A workstation that already has a valid record is diagnosed without any omission at all.
    /// </summary>
    [Fact]
    public async Task A_verified_workstation_is_diagnosed_without_omitting_anything()
    {
        StubVerifier real = new(Result(
            Passed(WorkstationVerificationCheck.PresetIntegrity),
            Passed(WorkstationVerificationCheck.ProductionRevalidation)));

        ReadinessDiagnosticEnvelope envelope = await Run(real, new StubVerifier(Result(
            Passed(WorkstationVerificationCheck.PresetIntegrity))));

        envelope.RevalidationCheckOmitted.ShouldBeFalse();
        envelope.DiagnosticReport.Checks.ShouldContain(c =>
            c.CheckKey == nameof(WorkstationVerificationCheck.ProductionRevalidation));
        envelope.ProductionAuthorised.ShouldBeTrue();
    }

    // ----------------------------------------------------------------------------------------
    // What the envelope must preserve
    // ----------------------------------------------------------------------------------------

    /// <summary>
    /// A failed diagnosis keeps the real R3 stages, cleanup outcome and both failure ranks.
    /// </summary>
    /// <remarks>
    /// The whole purpose of the slice. A diagnosis that summarised the probe into a verdict would
    /// destroy exactly the evidence it was run to obtain — the earliest stage that did not
    /// confirm is what localises the fault.
    /// </remarks>
    [Fact]
    public async Task A_failed_diagnosis_preserves_the_probe_stages_and_failures()
    {
        ReadinessProbeDiagnostics probe = new(
            "probe-7f1c",
            @"C:\Fake\Evidence\probe-7f1c.png",
            new ReadinessProcessIdentity(4242, @"C:\Fake\Photoshop.exe", null),
            [ReadinessProbeStage.ProbeCreated, ReadinessProbeStage.OpenGuard, ReadinessProbeStage.OpenRequested],
            ReadinessProbeStage.OpenConfirmed,
            ReadinessProbeStage.OpenRequested,
            ProbeCleanupOutcome.Failed,
            "The probe file changed on disk, so it was retained rather than deleted.",
            new ReadinessProbeFailure("Open", FailureCode.AdapterUnavailable,
                "The expected document title was never reached.", "Ctrl+O", "Enter"),
            [new ReadinessProbeFailure("Cleanup", FailureCode.AdapterUnavailable,
                "DeleteProbe returned a failure.", null, null)]);

        ReadinessEvidenceLifecycle lifecycle = new(
            null, null, null, false, false, DateTimeOffset.UtcNow, probe);

        StubVerifier real = new(Result(
            Passed(WorkstationVerificationCheck.PresetIntegrity),
            FailedCheck(WorkstationVerificationCheck.ProductionRevalidation)));

        StubVerifier omitted = new(Result(
            Passed(WorkstationVerificationCheck.PresetIntegrity),
            FailedCheck(WorkstationVerificationCheck.PhotoshopTestImageRoundTrip)) with
        {
            Lifecycle = lifecycle,
        });

        ReadinessDiagnosticEnvelope envelope = await Run(real, omitted);

        envelope.RevalidationCheckOmitted.ShouldBeTrue();
        envelope.DiagnosticReport.Verified.ShouldBeFalse(
            "a failed probe is not rescued by having been reached through the diagnosis");

        ReadinessProbeDiagnostics returned = envelope.DiagnosticReport.Lifecycle.ShouldNotBeNull()
            .LatestProbe.ShouldNotBeNull();

        returned.OperationId.ShouldBe("probe-7f1c");
        returned.Stages.ShouldBe([
            ReadinessProbeStage.ProbeCreated,
            ReadinessProbeStage.OpenGuard,
            ReadinessProbeStage.OpenRequested,
        ]);
        returned.LastAttemptedStage.ShouldBe(ReadinessProbeStage.OpenConfirmed);
        returned.LastConfirmedStage.ShouldBe(ReadinessProbeStage.OpenRequested);
        returned.CleanupOutcome.ShouldBe(ProbeCleanupOutcome.Failed);
        returned.PrimaryFailure.ShouldNotBeNull().Phase.ShouldBe("Open");
        returned.SecondaryFailures.Count.ShouldBe(1);
        returned.SecondaryFailures[0].Phase.ShouldBe("Cleanup");

        // A successful diagnosis grants nothing either: the normal answer is still the refusal.
        envelope.ProductionAuthorised.ShouldBeFalse();
    }

    /// <summary>Every invocation gets its own identity; none is borrowed or reused.</summary>
    [Fact]
    public async Task Each_invocation_mints_a_fresh_diagnostic_identity()
    {
        StubVerifier real = new(Result(Passed(WorkstationVerificationCheck.PresetIntegrity)));

        ReadinessDiagnosticEnvelope first = await Run(real, real);
        ReadinessDiagnosticEnvelope second = await Run(real, real);

        first.DiagnosticId.ShouldNotBeNullOrWhiteSpace();
        second.DiagnosticId.ShouldNotBe(first.DiagnosticId);
    }

    // ----------------------------------------------------------------------------------------
    // What it must never be mistaken for
    // ----------------------------------------------------------------------------------------

    /// <summary>
    /// The envelope carries no standard-set result, set identity or operator attestation.
    /// </summary>
    [Fact]
    public void The_envelope_is_not_a_standard_regression_set_result()
    {
        IReadOnlyList<string> members = [.. typeof(ReadinessDiagnosticEnvelope)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)];

        members.ShouldNotContain("SetId");
        members.ShouldNotContain("SetContentDigest");
        members.ShouldNotContain("RunId");
        members.ShouldNotContain("InvocationId");
        members.ShouldNotContain("EvidenceBindingVersion");

        typeof(ReadinessDiagnosticEnvelope)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ShouldNotContain(p => p.PropertyType == typeof(StandardRegressionSetOutcome),
                "a diagnosis may not carry the shape a revalidation record quotes");
        typeof(ReadinessDiagnosticEnvelope)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ShouldNotContain(p => p.PropertyType == typeof(PrintFlow.Tests.Regression.StandardRegressionSetRunResult),
                "a diagnosis may not carry a standard-set run result");
    }

    /// <summary>
    /// A revalidation record built from a diagnosis is refused at the existing boundary.
    /// </summary>
    /// <remarks>
    /// Proved against a synthetic target, and without touching the writer: the evaluator already
    /// refuses an outcome that names no run and invocation, and a diagnosis has neither to give.
    /// That is what makes "this cannot be published" a property of the existing rule rather than
    /// a promise made by this slice.
    /// </remarks>
    [Fact]
    public void A_revalidation_record_quoting_a_diagnosis_is_refused()
    {
        // A synthetic workstation, never the real one: this writes a revalidation record, which
        // is exactly what must not happen to D:\PrintFlowStudio.
        using WorkstationVerificationFixture fixture = new();

        ProductionRevalidationRecord matching = fixture.MatchingRevalidationRecord();

        // The most favourable record a diagnosis could support: everything the fixture's real
        // passing record has, except the one thing a diagnosis cannot mint — the identity of the
        // standard-set execution the outcome came from.
        fixture.WriteRevalidationRecord(matching with
        {
            StandardRegressionSet = matching.StandardRegressionSet! with
            {
                EvidencePath = "(PF-AUDIT-R4 readiness diagnosis)",
                RunId = null,
                InvocationId = null,
                EvidenceBindingVersion = 0,
                SetContentDigest = null,
            },
        });

        WorkstationCheckResult evaluated = fixture.CreateVerifier().Verify().Checks
            .Single(c => c.Check == WorkstationVerificationCheck.ProductionRevalidation);

        evaluated.Outcome.ShouldBe(WorkstationCheckOutcome.Failed);
        evaluated.Observed.ShouldNotBeNull().ShouldContain("unbound");

        // And the writer was not changed to make room for it: the same fixture's own record,
        // which does name a run, still passes.
        fixture.WriteRevalidationRecord(matching);
        fixture.CreateVerifier().Verify().Checks
            .Single(c => c.Check == WorkstationVerificationCheck.ProductionRevalidation)
            .Outcome.ShouldBe(WorkstationCheckOutcome.Passed);
    }

    // ----------------------------------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------------------------------

    private static async Task<ReadinessDiagnosticEnvelope> Run(
        IProductionWorkstationVerifier real,
        IProductionWorkstationVerifier omitted)
    {
        ReadinessDiagnosticEnvelope? envelope = await ReadinessDiagnostic.RunAsync(
            "1", () => Verifiers(real, omitted), TimeProvider.System, CancellationToken.None);

        return envelope.ShouldNotBeNull();
    }

    private static ReadinessDiagnosticVerifiers Verifiers(
        IProductionWorkstationVerifier real,
        IProductionWorkstationVerifier omitted) => new(real, omitted);

    private static StubVerifier Blocking() => new(Result(
        FailedCheck(WorkstationVerificationCheck.PresetIntegrity)));

    private static WorkstationVerificationResult Result(params WorkstationCheckResult[] checks) =>
        WorkstationVerificationResult.From(
            new ProductionPresetRef("printflow-workstation-v1", "1.17.0",
                Sha256.FromBytes(SHA256.HashData("preset"u8.ToArray()))),
            checks, DateTimeOffset.Now);

    private static WorkstationCheckResult Passed(WorkstationVerificationCheck check) =>
        WorkstationCheckResult.Passed(check, WorkstationCheckKind.Immutable, "observed", "fine");

    private static WorkstationCheckResult FailedCheck(WorkstationVerificationCheck check) =>
        WorkstationCheckResult.Failed(check, WorkstationCheckKind.Dynamic,
            FailureCode.EnvironmentNotVerified, "expected", "observed", "not fine");

    private sealed class StubVerifier(WorkstationVerificationResult result)
        : IProductionWorkstationVerifier
    {
        public WorkstationVerificationResult Verify() => result;

        public Task<WorkstationVerificationResult> RunLiveChecksAsync(
            CancellationToken cancellationToken) => Task.FromResult(result);
    }
}
