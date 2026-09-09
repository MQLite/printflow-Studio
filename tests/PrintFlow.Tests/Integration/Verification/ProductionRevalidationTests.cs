using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Gate;
using PrintFlow.Infrastructure.Verification;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Verification;

/// <summary>
/// Production is closed until this exact installation has been revalidated, and every way of
/// changing the environment reopens the question (SCRUM-11123 Part H §18, §19).
/// </summary>
/// <remarks>
/// The matrix these tests cover is the whole of the original requirement's revalidation clause:
/// a PrintFlow, Windows, Meitu or Photoshop upgrade must require rerunning the standard test set
/// before production use rather than silently changing the supported environment. Each of those
/// four is one test here, and each asserts the same thing — the gate refuses — because the
/// requirement makes no distinction between them.
/// <para>
/// The fixture starts revalidated, so every test in this file changes exactly one fact and
/// watches Production close. A test that passed because two things were wrong would not be
/// evidence about either.
/// </para>
/// </remarks>
public sealed class ProductionRevalidationTests
{
    [Fact]
    public void A_fully_revalidated_installation_is_verified()
    {
        using WorkstationVerificationFixture fixture = new();

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeTrue(result.Describe());
        Revalidation(result).Outcome.ShouldBe(WorkstationCheckOutcome.Passed);
    }

    /// <summary>
    /// The state a freshly installed or freshly upgraded workstation is in: binaries in place,
    /// machine unchanged, and nothing that says this build was ever tested here.
    /// </summary>
    [Fact]
    public void No_revalidation_record_closes_production()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.RemoveRevalidationRecord();

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeFalse();
        WorkstationCheckResult check = Revalidation(result);
        check.Outcome.ShouldBe(WorkstationCheckOutcome.Failed);
        check.FailureCode.ShouldBe(FailureCode.EnvironmentNotVerified);
        check.Observed.ShouldBe("(no record)");
    }

    [Fact]
    public void A_malformed_revalidation_record_closes_production()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.WriteRevalidationRecordRaw("{ this is not json");

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeFalse();
        Revalidation(result).Observed.ShouldBe("(no record)");
    }

    [Fact]
    public void A_record_written_against_a_future_schema_closes_production()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.WriteRevalidationRecord(fixture.MatchingRevalidationRecord() with { SchemaVersion = 99 });

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeFalse();
        Revalidation(result).Observed.ShouldBe("schema 99");
    }

    // ------------------------------------------------------------------------------------------
    // The four upgrades the original requirement names
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// "Any PrintFlow ... upgrade must require rerunning the standard test set before production
    /// use." A record covering a different product version is exactly a PrintFlow upgrade.
    /// </summary>
    [Fact]
    public void A_printflow_upgrade_closes_production()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.WriteRevalidationRecord(
            fixture.MatchingRevalidationRecord() with { ProductVersion = "0.0.1-previous" });

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeFalse();
        Revalidation(result).Explanation.ShouldContain("0.0.1-previous");
    }

    [Fact]
    public void A_windows_upgrade_closes_production()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.WriteRevalidationRecord(
            fixture.MatchingRevalidationRecord() with { OperatingSystemBuild = "1233" });

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeFalse();
        Revalidation(result).Explanation.ShouldContain("Windows");
    }

    /// <summary>
    /// A Meitu upgrade the preset has already accepted. The Meitu executable check passes — the
    /// binary on disk is the one the current preset names — and Production still closes, because
    /// the environment that was tested is not the environment in front of the operator.
    /// </summary>
    [Fact]
    public void A_meitu_upgrade_the_preset_already_accepts_still_closes_production()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.WriteRevalidationRecord(
            fixture.MatchingRevalidationRecord() with
            {
                MeituSha256 = "0000000000000000000000000000000000000000000000000000000000000000",
            });

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeFalse();
        result.Checks.Single(c => c.Check == WorkstationVerificationCheck.MeituExecutable)
            .Outcome.ShouldBe(WorkstationCheckOutcome.Passed);
        Revalidation(result).Explanation.ShouldContain("Meitu");
    }

    [Fact]
    public void A_photoshop_upgrade_the_preset_already_accepts_still_closes_production()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.WriteRevalidationRecord(
            fixture.MatchingRevalidationRecord() with
            {
                PhotoshopSha256 = "0000000000000000000000000000000000000000000000000000000000000000",
            });

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeFalse();
        result.Checks.Single(c => c.Check == WorkstationVerificationCheck.PhotoshopExecutable)
            .Outcome.ShouldBe(WorkstationCheckOutcome.Passed);
        Revalidation(result).Explanation.ShouldContain("Photoshop");
    }

    /// <summary>
    /// A preset re-issued under the same id and version. Every other check compares the machine
    /// against the current preset and passes; only the revalidation record notices that the
    /// preset that was tested is not this one.
    /// </summary>
    [Fact]
    public void A_different_preset_digest_under_the_same_identity_closes_production()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.WriteRevalidationRecord(
            fixture.MatchingRevalidationRecord() with
            {
                PresetSha256 = "0000000000000000000000000000000000000000000000000000000000000000",
            });

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeFalse();
        result.Checks.Single(c => c.Check == WorkstationVerificationCheck.PresetIntegrity)
            .Outcome.ShouldBe(WorkstationCheckOutcome.Passed);
        Revalidation(result).Explanation.ShouldContain("preset");
    }

    // ------------------------------------------------------------------------------------------
    // The standard test set (SCRUM-11123 Part N, SCRUM-11065)
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The state of this workstation today. The standard local regression set SCRUM-11065
    /// requires has not been built, so no honest record can say it passed, and Production stays
    /// closed after any upgrade until it exists and passes.
    /// </summary>
    [Fact]
    public void A_standard_regression_set_that_does_not_exist_closes_production()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.WriteRevalidationRecord(
            fixture.MatchingRevalidationRecord() with
            {
                StandardRegressionSet = new StandardRegressionSetOutcome(
                    null, StandardRegressionSetStatus.NotAvailable, null, null),
            });

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeFalse();
        Revalidation(result).Explanation.ShouldContain("does not exist");
    }

    [Fact]
    public void A_failed_standard_regression_set_closes_production()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.WriteRevalidationRecord(
            fixture.MatchingRevalidationRecord() with
            {
                StandardRegressionSet = new StandardRegressionSetOutcome(
                    "set-v1", StandardRegressionSetStatus.Failed, "2026-09-01T09:00:00+12:00", null),
            });

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeFalse();
        Revalidation(result).Observed.ShouldBe("standard regression set: Failed");
    }

    /// <summary>
    /// A record with no regression-set section at all reads as Unknown, and Unknown blocks. An
    /// omitted answer must never be the same as a passing one.
    /// </summary>
    [Fact]
    public void A_record_that_omits_the_standard_regression_set_closes_production()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.WriteRevalidationRecord(
            fixture.MatchingRevalidationRecord() with { StandardRegressionSet = null });

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeFalse();
        Revalidation(result).Observed.ShouldBe("standard regression set: Unknown");
    }

    [Fact]
    public void A_record_without_a_passing_environment_readiness_run_closes_production()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.WriteRevalidationRecord(
            fixture.MatchingRevalidationRecord() with { EnvironmentReadinessPassed = false });

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeFalse();
        Revalidation(result).Observed.ShouldBe("readiness not passed");
    }

    // ------------------------------------------------------------------------------------------
    // The gate, not just the verifier
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The check has to close the gate, not merely appear in a report. Fake stays open, because
    /// an operator troubleshooting a workstation that fails revalidation must still be able to
    /// run the application.
    /// </summary>
    [Fact]
    public void The_gate_refuses_production_and_still_allows_fake_when_revalidation_is_missing()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.RemoveRevalidationRecord();
        VerifiedEnvironmentGate gate = new(fixture.CreateVerifier());

        OperationResult<Domain.Results.Unit> production = gate.Verify(AdapterExecutionMode.Production);
        production.IsFailure.ShouldBeTrue();
        production.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
        production.Failure.MessageKey.ShouldBe(
            "EnvironmentCheck_" + nameof(WorkstationVerificationCheck.ProductionRevalidation));

        gate.Verify(AdapterExecutionMode.Fake).IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// Recorded while PrintFlow is running, and Production opens without a restart; removed, and
    /// it closes again on the next request. This is why the check is dynamic rather than part of
    /// the cached signed baseline.
    /// </summary>
    [Fact]
    public void Recording_and_removing_a_revalidation_takes_effect_without_a_restart()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.RemoveRevalidationRecord();
        VerifiedEnvironmentGate gate = new(fixture.CreateVerifier());

        gate.Verify(AdapterExecutionMode.Production).IsFailure.ShouldBeTrue();

        fixture.WriteRevalidationRecord(fixture.MatchingRevalidationRecord());
        gate.Verify(AdapterExecutionMode.Production).IsSuccess.ShouldBeTrue();

        fixture.RemoveRevalidationRecord();
        gate.Verify(AdapterExecutionMode.Production).IsFailure.ShouldBeTrue();
    }

    /// <summary>
    /// The record lives in the production workspace, not beside the executable — so an upgrade
    /// cannot replace the very record whose invalidation it is supposed to cause, and an
    /// uninstall does not remove it (Part F, Part K).
    /// </summary>
    [Fact]
    public void The_record_lives_under_the_production_workspace()
    {
        ProductionRevalidationRecord.RelativePath.ShouldBe(@"Revalidation\production-revalidation.json");
        ProductionRevalidationRecord.PathFor(@"D:\PrintFlowStudio")
            .ShouldBe(@"D:\PrintFlowStudio\Revalidation\production-revalidation.json");
    }

    /// <summary>
    /// The version the check compares against is the one the build stamped, not a constant a
    /// developer could forget to raise (Part D §11).
    /// </summary>
    [Fact]
    public void The_running_product_version_comes_from_the_built_assembly()
    {
        Version assembly = typeof(ProductionRevalidationRecord).Assembly.GetName().Version!;

        ProductionRevalidationEvaluator.RunningProductVersion
            .ShouldBe($"{assembly.Major}.{assembly.Minor}.{assembly.Build}");
    }

    private static WorkstationCheckResult Revalidation(WorkstationVerificationResult result) =>
        result.Checks.Single(c => c.Check == WorkstationVerificationCheck.ProductionRevalidation);
}
