using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Gate;
using PrintFlow.Infrastructure.Verification;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Verification;

/// <summary>
/// The verified environment gate: what it authorises, what it refuses, and how often it asks
/// (Epic 11500 Part B §28).
/// </summary>
/// <remarks>
/// Every case runs against <see cref="WorkstationVerificationFixture"/>'s synthetic workstation —
/// a machine that exists nowhere, running "Windows Test Edition 99.0.1234" on a 1280x800 display
/// in <c>fr-FR</c>. That is deliberate: a gate that passes here cannot be carrying a hidden copy
/// of the real workstation's paths, digests, resolution or culture, because none of them would
/// match. The one place the real machine is touched is the opt-in live gate smoke.
/// <para>
/// The failing halves of the matrix are produced by presenting different facts through
/// <see cref="FakeWorkstationFactReader"/> and by tampering with the fixture's own temporary
/// files. The live production workstation is never intentionally broken to prove a refusal (§26).
/// </para>
/// </remarks>
public sealed class VerifiedEnvironmentGateTests
{
    // ---------------------------------------------------------------- §28.1, §11, §28.22

    /// <summary>
    /// Fake is authorised without the verifier being consulted at all (§11).
    /// </summary>
    /// <remarks>
    /// Not merely "Fake passes on a good workstation": the verifier here is one that throws if it
    /// is called. Fake work has to keep running on a workstation whose verification is broken —
    /// that is precisely when a developer, QA, or the person fixing the workstation needs the
    /// application — and a gate that consulted the verifier first would make the broken machine
    /// unusable for diagnosing itself.
    /// </remarks>
    [Fact]
    public void Fake_is_allowed_without_consulting_the_verifier()
    {
        VerifiedEnvironmentGate gate = new(new ThrowingVerifier());

        gate.Verify(AdapterExecutionMode.Fake).IsSuccess.ShouldBeTrue();
    }

    /// <summary>Fake stays allowed on a workstation that fails every blocking check (§11).</summary>
    [Fact]
    public void Fake_is_allowed_on_a_workstation_that_fails_verification()
    {
        using WorkstationVerificationFixture fixture = new();
        WorkstationVerificationFixture.Corrupt(fixture.PhotoshopPath);
        fixture.Facts.Session = new InteractiveSessionFacts(false, 0, "Services", false, null);
        fixture.Facts.Display = new DisplayFacts(2, @"\\.\DISPLAY1", null, null, 96);

        VerifiedEnvironmentGate gate = new(fixture.CreateVerifier());

        gate.Verify(AdapterExecutionMode.Production).IsFailure.ShouldBeTrue();
        gate.Verify(AdapterExecutionMode.Fake).IsSuccess.ShouldBeTrue(
            "a broken workstation must still be usable for development, QA and troubleshooting.");
    }

    // ---------------------------------------------------------------- §28.2, §12

    /// <summary>A workstation that passes every blocking check authorises Production (§12).</summary>
    [Fact]
    public void Production_is_allowed_on_a_verified_workstation()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.MarkEvidenceReadOnly();
        VerifiedEnvironmentGate gate = new(fixture.CreateVerifier());

        gate.Verify(AdapterExecutionMode.Production).IsSuccess.ShouldBeTrue();
    }

    // ---------------------------------------------------------------- §28.3–§28.12

    /// <summary>
    /// Every blocking check, one at a time, closes Production (§12, §28.3–§28.12).
    /// </summary>
    /// <remarks>
    /// A theory rather than eleven near-identical facts, and each case breaks exactly one thing
    /// on an otherwise accepted workstation. What it establishes is that no blocking check is
    /// decorative: there is no fact the preset makes a condition which the gate would go on to
    /// authorise anyway.
    /// </remarks>
    [Theory]
    [InlineData(WorkstationVerificationCheck.PresetIntegrity)]
    [InlineData(WorkstationVerificationCheck.EvidenceIntegrity)]
    [InlineData(WorkstationVerificationCheck.WorkspaceRoot)]
    [InlineData(WorkstationVerificationCheck.OperatingSystem)]
    [InlineData(WorkstationVerificationCheck.InteractiveSession)]
    [InlineData(WorkstationVerificationCheck.DisplayConfiguration)]
    [InlineData(WorkstationVerificationCheck.UiCulture)]
    [InlineData(WorkstationVerificationCheck.MeituExecutable)]
    [InlineData(WorkstationVerificationCheck.PhotoshopExecutable)]
    [InlineData(WorkstationVerificationCheck.PhotoshopActionArtifact)]
    public void Any_single_blocking_failure_refuses_production(WorkstationVerificationCheck broken)
    {
        using WorkstationVerificationFixture fixture = new();
        VerifiedEnvironmentGate gate = new(fixture.CreateVerifier(WorkspaceRootFor(broken, fixture)));

        Break(broken, fixture);

        OperationResult<PrintFlow.Domain.Results.Unit> result = gate.Verify(AdapterExecutionMode.Production);

        result.IsFailure.ShouldBeTrue($"{broken} is a blocking check.");
        result.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
        result.Failure.Context["verification.failedChecks"].ShouldContain(broken.ToString());
    }

    /// <summary>The secure desktop is refused as its own case (§18, §28.7).</summary>
    /// <remarks>
    /// Separated from the session theory above because it is the one an operator meets in
    /// practice: the workstation is the accepted workstation, the session is interactive and
    /// local, and the screen is locked. Input is going to Winlogon, not to Photoshop, and a
    /// production step started now would send keystrokes into a lock screen.
    /// </remarks>
    [Fact]
    public void The_secure_desktop_refuses_production()
    {
        using WorkstationVerificationFixture fixture = new();
        VerifiedEnvironmentGate gate = new(fixture.CreateVerifier());

        fixture.Facts.Session = fixture.Facts.Session with { InputDesktopName = "Winlogon" };

        gate.Verify(AdapterExecutionMode.Production).IsFailure.ShouldBeTrue();
    }

    /// <summary>An unsupported remote session refuses Production (§18).</summary>
    [Fact]
    public void A_remote_session_refuses_production()
    {
        using WorkstationVerificationFixture fixture = new();
        VerifiedEnvironmentGate gate = new(fixture.CreateVerifier());

        fixture.Facts.Session = fixture.Facts.Session with { IsRemoteSession = true, SessionName = "RDP-Tcp#1" };

        gate.Verify(AdapterExecutionMode.Production).IsFailure.ShouldBeTrue();
    }

    // ---------------------------------------------------------------- §28.13, §8

    /// <summary>
    /// A result carrying only advisories authorises Production (§8, §28.13).
    /// </summary>
    /// <remarks>
    /// The fixture's evidence files are left without the read-only attribute, which is exactly
    /// the drift Epic 11400 Final QA found on the real workstation and which v1.15.0 settles by
    /// saying SHA-256 remains authoritative. The advisory has to be visible and has to not close
    /// Production; silently promoting it to a failure would shut the shop floor over a file
    /// attribute the accepted contract does not make a condition.
    /// </remarks>
    [Fact]
    public void An_advisory_only_result_still_authorises_production()
    {
        using WorkstationVerificationFixture fixture = new();
        foreach (string evidence in fixture.EvidencePaths)
        {
            WorkstationVerificationFixture.ClearReadOnlyAttribute(evidence);
        }

        VerifiedEnvironmentGate gate = new(fixture.CreateVerifier());

        gate.Verify(AdapterExecutionMode.Production).IsSuccess.ShouldBeTrue();

        // ...and the advisory is still reported rather than swallowed.
        EnvironmentReadinessReport report = gate.Read();
        report.Verified.ShouldBeTrue();
        report.Advisories.ShouldContain(a =>
            a.CheckKey == nameof(WorkstationVerificationCheck.FilesystemReadOnlyPolicyAdvisory));
        report.Advisories.ShouldAllBe(a => !a.IsBlocking);
        report.BlockingFailures.ShouldBeEmpty();
    }

    // ---------------------------------------------------------------- §28.14, §6, §7

    /// <summary>
    /// The gate re-observes the workstation on every Production request (§6, §7, §28.14).
    /// </summary>
    /// <remarks>
    /// The whole reason §7 forbids a second freshness cache. A gate that remembered a yes would
    /// authorise a step on a workstation whose screen has since locked, and no TTL short enough
    /// to be safe would be long enough to be worth having. Counted through the fact reader, so
    /// what is asserted is that the machine was actually looked at again — not merely that a
    /// method returned.
    /// </remarks>
    [Fact]
    public void Every_production_request_re_observes_the_dynamic_facts()
    {
        using WorkstationVerificationFixture fixture = new();
        VerifiedEnvironmentGate gate = new(fixture.CreateVerifier());

        gate.Verify(AdapterExecutionMode.Production).IsSuccess.ShouldBeTrue();
        int afterFirst = fixture.Facts.DynamicReadCount;
        afterFirst.ShouldBeGreaterThan(0);

        gate.Verify(AdapterExecutionMode.Production).IsSuccess.ShouldBeTrue();
        gate.Verify(AdapterExecutionMode.Production).IsSuccess.ShouldBeTrue();

        fixture.Facts.DynamicReadCount.ShouldBe(afterFirst * 3, "no gate request reuses a stored answer.");
    }

    /// <summary>Fake requests never touch the machine (§11).</summary>
    [Fact]
    public void A_fake_request_observes_nothing()
    {
        using WorkstationVerificationFixture fixture = new();
        VerifiedEnvironmentGate gate = new(fixture.CreateVerifier());

        gate.Verify(AdapterExecutionMode.Fake).IsSuccess.ShouldBeTrue();

        fixture.Facts.DynamicReadCount.ShouldBe(0);
    }

    // ---------------------------------------------------------------- §28.15, §19

    /// <summary>
    /// A display change closes Production, and restoring the display reopens it — in one process
    /// (§19, §28.15).
    /// </summary>
    /// <remarks>
    /// The proof that dynamic re-evaluation is real rather than described. If the gate cached, the
    /// third call would still be refusing on a workstation that is once again correct, and the
    /// only cure would be restarting PrintFlow — which is exactly the behaviour §6 rules out.
    /// </remarks>
    [Fact]
    public void A_display_change_closes_production_and_restoring_it_reopens_without_a_restart()
    {
        using WorkstationVerificationFixture fixture = new();
        VerifiedEnvironmentGate gate = new(fixture.CreateVerifier());
        DisplayFacts accepted = fixture.Facts.Display;

        gate.Verify(AdapterExecutionMode.Production).IsSuccess.ShouldBeTrue();

        // A second active display, then the accepted geometry at the wrong scaling.
        fixture.Facts.Display = accepted with { ActiveDisplayCount = 2 };
        gate.Verify(AdapterExecutionMode.Production).IsFailure.ShouldBeTrue();

        fixture.Facts.Display = accepted with { SystemDpi = 96 };
        gate.Verify(AdapterExecutionMode.Production).IsFailure.ShouldBeTrue();

        fixture.Facts.Display = accepted;
        gate.Verify(AdapterExecutionMode.Production).IsSuccess.ShouldBeTrue(
            "the same gate instance must pass again once the display is restored.");
    }

    // ---------------------------------------------------------------- §28.16, §18

    /// <summary>
    /// A locked screen closes Production, and unlocking reopens it — in one process (§18, §28.16).
    /// </summary>
    /// <remarks>
    /// The operator case §18 asks for explicitly: the person walked away, the workstation locked,
    /// they came back. Requiring a restart of PrintFlow after every lunch break would make the
    /// gate something operators route around rather than something they trust.
    /// </remarks>
    [Fact]
    public void A_locked_screen_closes_production_and_unlocking_reopens_without_a_restart()
    {
        using WorkstationVerificationFixture fixture = new();
        VerifiedEnvironmentGate gate = new(fixture.CreateVerifier());
        InteractiveSessionFacts accepted = fixture.Facts.Session;

        gate.Verify(AdapterExecutionMode.Production).IsSuccess.ShouldBeTrue();

        fixture.Facts.Session = accepted with { InputDesktopName = "Winlogon" };
        gate.Verify(AdapterExecutionMode.Production).IsFailure.ShouldBeTrue();

        fixture.Facts.Session = accepted with { UserInteractive = false };
        gate.Verify(AdapterExecutionMode.Production).IsFailure.ShouldBeTrue();

        fixture.Facts.Session = accepted;
        gate.Verify(AdapterExecutionMode.Production).IsSuccess.ShouldBeTrue(
            "returning to a valid local Default desktop must pass without restarting PrintFlow.");
    }

    // ---------------------------------------------------------------- §28.17, §9, §10

    /// <summary>
    /// The refusal keeps one stable workflow code and puts the detail in bounded context
    /// (§9, §10, §28.17).
    /// </summary>
    /// <remarks>
    /// Two rules at once. The persisted vocabulary stays
    /// <see cref="FailureCode.EnvironmentNotVerified"/> — minting a workflow failure code per
    /// workstation sub-check would make display scaling a permanent part of the product's state
    /// language. And the context that carries the detail is capped and itemised rather than
    /// free-form, so a refusal cannot grow into a machine snapshot on a workflow attempt.
    /// </remarks>
    [Fact]
    public void A_refusal_is_one_stable_code_with_bounded_structured_context()
    {
        using WorkstationVerificationFixture fixture = new();
        VerifiedEnvironmentGate gate = new(fixture.CreateVerifier());
        fixture.Facts.Display = fixture.Facts.Display with { SystemDpi = 96 };

        OperationFailure failure = gate.Verify(AdapterExecutionMode.Production).Failure;

        failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
        failure.IsRetryable.ShouldBeFalse();

        failure.Context["verification.result"].ShouldBe("NotVerified");
        failure.Context["verification.failedChecks"]
            .ShouldContain(nameof(WorkstationVerificationCheck.DisplayConfiguration));
        failure.Context["verification.preset"].ShouldContain(WorkstationVerificationFixture.PresetId);
        failure.Context.ShouldContainKey(
            "verification.failure." + nameof(WorkstationVerificationCheck.DisplayConfiguration));
        failure.Context.ShouldNotBeEmpty();

        // Bounded: nothing in the context is manifest-sized, and no entry carries JSON.
        foreach ((string key, string value) in failure.Context)
        {
            value.Length.ShouldBeLessThanOrEqualTo(260, key);
            value.ShouldNotContain("\"sourceManifestIntegrity\"");
            value.ShouldNotContain("{\"schema\"");
        }

        failure.TechnicalDetail.Length.ShouldBeLessThanOrEqualTo(1600);
    }

    /// <summary>
    /// The operator sees a specific localised sentence, not an enum name (§22).
    /// </summary>
    /// <remarks>
    /// The failure code stays stable and English; <c>MessageKey</c> is the seam that lets one code
    /// keep a useful runtime distinction, exactly as Meitu's closed-versus-lost cases already do.
    /// "Display configuration does not match the verified workstation" is actionable;
    /// <c>DisplayConfiguration</c> is not.
    /// </remarks>
    [Fact]
    public void The_refusal_names_the_specific_check_through_a_resource_key()
    {
        using WorkstationVerificationFixture fixture = new();
        VerifiedEnvironmentGate gate = new(fixture.CreateVerifier());
        fixture.Facts.Culture = new UiCultureFacts("de-DE", WorkstationVerificationFixture.SystemUiCulture);

        OperationFailure failure = gate.Verify(AdapterExecutionMode.Production).Failure;

        failure.MessageKey.ShouldBe(
            VerifiedEnvironmentGate.MessageKeyFor(WorkstationVerificationCheck.UiCulture));
        failure.MessageKey.ShouldNotBe(nameof(WorkstationVerificationCheck.UiCulture));
    }

    /// <summary>
    /// A manifest that does not verify falls back to the general message key (§22).
    /// </summary>
    [Fact]
    public void An_unverifiable_preset_reports_the_preset_check_rather_than_nothing()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.ExpectWrongManifestHash();
        VerifiedEnvironmentGate gate = new(fixture.CreateVerifier());

        OperationFailure failure = gate.Verify(AdapterExecutionMode.Production).Failure;

        failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
        failure.MessageKey.ShouldBe(
            VerifiedEnvironmentGate.MessageKeyFor(WorkstationVerificationCheck.PresetIntegrity));
        failure.Context["verification.preset"].ShouldBe("(unverified)");
    }

    // ---------------------------------------------------------------- §21

    /// <summary>
    /// The diagnostics report says what is wrong and offers no way to proceed anyway (§21, §24).
    /// </summary>
    /// <remarks>
    /// The read model exists so a support person can see the blocking failures and the advisories
    /// separately and go and fix the workstation. What it must never grow is a method that changes
    /// the answer — asserted structurally here, because the pressure to add one arrives on the day
    /// the workstation fails and a job is due.
    /// </remarks>
    [Fact]
    public void The_diagnostics_report_separates_blocking_failures_from_advisories_and_changes_nothing()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.Facts.Display = fixture.Facts.Display with { ActiveDisplayCount = 2 };
        VerifiedEnvironmentGate gate = new(fixture.CreateVerifier());

        EnvironmentReadinessReport report = gate.Read();

        report.Verified.ShouldBeFalse();
        report.PresetIdentity!.ShouldContain(WorkstationVerificationFixture.PresetVersion);
        report.ObservedAt.ShouldBe(fixture.Clock.GetUtcNow());
        report.BlockingFailures.ShouldContain(c =>
            c.CheckKey == nameof(WorkstationVerificationCheck.DisplayConfiguration));
        report.Checks.ShouldAllBe(c => c.MessageKey.StartsWith("EnvironmentCheck_", StringComparison.Ordinal));

        // Reading the report is not permission, and there is nothing on it that could become one.
        typeof(IEnvironmentDiagnostics).GetMethods().ShouldHaveSingleItem()
            .Name.ShouldBe(nameof(IEnvironmentDiagnostics.Read));
        typeof(EnvironmentReadinessReport).GetMethods()
            .Where(m => m.DeclaringType == typeof(EnvironmentReadinessReport))
            .ShouldNotContain(m => m.Name.Contains("Enable", StringComparison.OrdinalIgnoreCase) ||
                                   m.Name.Contains("Override", StringComparison.OrdinalIgnoreCase) ||
                                   m.Name.Contains("Ignore", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Diagnostics re-observe too, so a support screen is never stale (§6).</summary>
    [Fact]
    public void The_diagnostics_report_is_re_observed_on_every_read()
    {
        using WorkstationVerificationFixture fixture = new();
        VerifiedEnvironmentGate gate = new(fixture.CreateVerifier());

        gate.Read().Verified.ShouldBeTrue();
        fixture.Facts.Session = fixture.Facts.Session with { InputDesktopName = "Winlogon" };
        gate.Read().Verified.ShouldBeFalse();
        fixture.Facts.Session = fixture.Facts.Session with { InputDesktopName = "Default" };
        gate.Read().Verified.ShouldBeTrue();
    }

    // ---------------------------------------------------------------- §13, §16

    /// <summary>
    /// Production is one answer about the whole workstation, not one per adapter (§13).
    /// </summary>
    /// <remarks>
    /// The accepted preset describes one production workstation, so "Photoshop verified but Meitu
    /// failed, therefore Photoshop may run" is not a decision this gate is entitled to make. The
    /// interface itself is the guarantee: <see cref="IEnvironmentGate.Verify"/> takes an execution
    /// mode and nothing that could name an adapter, so a per-adapter rule has nowhere to live.
    /// </remarks>
    [Fact]
    public void A_meitu_failure_closes_production_for_photoshop_too()
    {
        using WorkstationVerificationFixture fixture = new();
        WorkstationVerificationFixture.Corrupt(fixture.MeituPath);
        VerifiedEnvironmentGate gate = new(fixture.CreateVerifier());

        gate.Verify(AdapterExecutionMode.Production).IsFailure.ShouldBeTrue();

        typeof(IEnvironmentGate).GetMethod(nameof(IEnvironmentGate.Verify))!
            .GetParameters().ShouldHaveSingleItem()
            .ParameterType.ShouldBe(typeof(AdapterExecutionMode));
    }

    /// <summary>
    /// Authorising Production starts no application (§16).
    /// </summary>
    /// <remarks>
    /// Deciding readiness must not put Photoshop or Meitu on the operator's screen. Asserted by
    /// recording every path the artefact reader was asked about: the accepted binaries are read as
    /// files and hashed, and nothing about them is executed. The live application is verified by
    /// the operation-time automation guards, when an operation actually runs.
    /// </remarks>
    [Fact]
    public void Authorising_production_reads_the_accepted_binaries_and_launches_neither()
    {
        using WorkstationVerificationFixture fixture = new();
        RecordingArtifactReader recording = new(fixture.Artifacts);
        fixture.Artifacts = recording;
        VerifiedEnvironmentGate gate = new(fixture.CreateVerifier());

        gate.Verify(AdapterExecutionMode.Production).IsSuccess.ShouldBeTrue();

        recording.FilesRead.ShouldContain(fixture.PhotoshopPath);
        recording.FilesRead.ShouldContain(fixture.MeituPath);

        // The gate itself owns no process-starting surface: its only dependency is the verifier.
        typeof(VerifiedEnvironmentGate).GetConstructors().ShouldHaveSingleItem()
            .GetParameters().ShouldHaveSingleItem()
            .ParameterType.ShouldBe(typeof(IProductionWorkstationVerifier));
    }

    // ---------------------------------------------------------------- §20

    /// <summary>
    /// Baseline drift under a running process is not detected, by design — so it is asserted
    /// rather than assumed (§20).
    /// </summary>
    /// <remarks>
    /// Part A caches the signed baseline because those bytes cannot change without also
    /// invalidating the hash that approved them, and re-hashing two application binaries on every
    /// production step would be a real cost for no security gain against an attacker who can
    /// already write to them. The consequence has to be written down rather than discovered:
    /// <b>changing an accepted artefact while PrintFlow is running does not close Production until
    /// PrintFlow is restarted.</b> This test pins that behaviour so a future change to it is a
    /// deliberate decision and not a silent one. No file watcher is introduced here (§20).
    /// </remarks>
    [Fact]
    public void Immutable_baseline_drift_during_a_run_is_process_cached_until_restart()
    {
        using WorkstationVerificationFixture fixture = new();
        VerifiedEnvironmentGate running = new(fixture.CreateVerifier());

        running.Verify(AdapterExecutionMode.Production).IsSuccess.ShouldBeTrue();

        WorkstationVerificationFixture.Corrupt(fixture.PhotoshopPath);

        running.Verify(AdapterExecutionMode.Production).IsSuccess.ShouldBeTrue(
            "the signed baseline is read once per process; this is the documented restart semantic.");

        // A new process — a restarted PrintFlow — reads the changed bytes and refuses.
        VerifiedEnvironmentGate restarted = new(fixture.CreateVerifier());
        restarted.Verify(AdapterExecutionMode.Production).IsFailure.ShouldBeTrue();
    }

    /// <summary>An unrecognised execution mode is refused rather than waved through.</summary>
    [Fact]
    public void An_unknown_execution_mode_is_refused()
    {
        using WorkstationVerificationFixture fixture = new();
        VerifiedEnvironmentGate gate = new(fixture.CreateVerifier());

        OperationResult<PrintFlow.Domain.Results.Unit> result = gate.Verify((AdapterExecutionMode)42);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
    }

    /// <summary>The gate cannot be built without a verifier (§14).</summary>
    [Fact]
    public void A_gate_cannot_be_constructed_without_a_verifier() =>
        Should.Throw<ArgumentNullException>(() => new VerifiedEnvironmentGate(null!));

    private static string? WorkspaceRootFor(
        WorkstationVerificationCheck broken, WorkstationVerificationFixture fixture) =>
        broken == WorkstationVerificationCheck.WorkspaceRoot
            ? System.IO.Path.Combine(fixture.WorkspaceRoot, "not-the-accepted-root")
            : null;

    /// <summary>Breaks exactly one accepted fact on an otherwise verified workstation.</summary>
    private static void Break(WorkstationVerificationCheck check, WorkstationVerificationFixture fixture)
    {
        switch (check)
        {
            case WorkstationVerificationCheck.PresetIntegrity:
                WorkstationVerificationFixture.Corrupt(fixture.ManifestPath);
                break;

            case WorkstationVerificationCheck.EvidenceIntegrity:
                WorkstationVerificationFixture.Corrupt(fixture.EvidencePaths[0]);
                break;

            case WorkstationVerificationCheck.WorkspaceRoot:
                // Already broken by the configured root the verifier was built with.
                break;

            case WorkstationVerificationCheck.OperatingSystem:
                fixture.Facts.OperatingSystem = fixture.Facts.OperatingSystem with { Build = "9999" };
                break;

            case WorkstationVerificationCheck.InteractiveSession:
                fixture.Facts.Session = new InteractiveSessionFacts(false, 0, "Services", false, null);
                break;

            case WorkstationVerificationCheck.DisplayConfiguration:
                fixture.Facts.Display = fixture.Facts.Display with { ActiveDisplayCount = 2 };
                break;

            case WorkstationVerificationCheck.UiCulture:
                fixture.Facts.Culture = new UiCultureFacts("de-DE", WorkstationVerificationFixture.SystemUiCulture);
                break;

            case WorkstationVerificationCheck.MeituExecutable:
                WorkstationVerificationFixture.Corrupt(fixture.MeituPath);
                break;

            case WorkstationVerificationCheck.PhotoshopExecutable:
                WorkstationVerificationFixture.Corrupt(fixture.PhotoshopPath);
                break;

            case WorkstationVerificationCheck.PhotoshopActionArtifact:
                WorkstationVerificationFixture.Corrupt(fixture.ActionPath);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(check), check, "Not a blocking check.");
        }
    }

    /// <summary>
    /// A verifier that fails the test if it is consulted at all.
    /// </summary>
    /// <remarks>
    /// The only honest way to assert "Fake does not consult the verifier": a verifier that
    /// returned a passing result would leave the assertion true for the wrong reason.
    /// </remarks>
    private sealed class ThrowingVerifier : IProductionWorkstationVerifier
    {
        public WorkstationVerificationResult Verify() =>
            throw new InvalidOperationException("Fake execution must not consult the workstation verifier.");
    }
}
