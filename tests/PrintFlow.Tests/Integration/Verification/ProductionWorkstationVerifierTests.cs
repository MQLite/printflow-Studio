using System.IO;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Gate;
using PrintFlow.Infrastructure.Verification;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Verification;

/// <summary>
/// The Epic 11500 Part A verification matrix (§22), run against a synthetic workstation whose
/// every accepted value is invented.
/// </summary>
/// <remarks>
/// Each pair of tests is the same check twice: once with the workstation the manifest accepts,
/// once with one it does not. A verifier that passed only the first half could be returning
/// "verified" unconditionally; a verifier that passed only the second could be refusing
/// everything. Both halves together are what makes the answer a fact.
/// </remarks>
public sealed class ProductionWorkstationVerifierTests
{
    // -- root of trust -------------------------------------------------------------------

    [Fact]
    public void The_exact_configured_manifest_verifies_and_carries_its_identity()
    {
        using WorkstationVerificationFixture fixture = new();

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeTrue(result.Describe());
        result.Preset.ShouldNotBeNull();
        result.Preset!.PresetId.ShouldBe(WorkstationVerificationFixture.PresetId);
        result.Preset.PresetVersion.ShouldBe(WorkstationVerificationFixture.PresetVersion);
        result.Preset.ManifestSha256.ShouldBe(fixture.ManifestSha256);
        Outcome(result, WorkstationVerificationCheck.PresetIntegrity).ShouldBe(WorkstationCheckOutcome.Passed);
    }

    /// <summary>
    /// A manifest that does not hash to the configured digest is not evidence, so nothing in it
    /// is interpreted (§4).
    /// </summary>
    [Fact]
    public void A_manifest_hash_mismatch_fails_before_any_requirement_is_trusted()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.ExpectWrongManifestHash();

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeFalse();
        result.Preset.ShouldBeNull();
        result.Checks.ShouldHaveSingleItem();
        result.Checks[0].Check.ShouldBe(WorkstationVerificationCheck.PresetIntegrity);
        result.Checks[0].FailureCode.ShouldBe(FailureCode.PresetHashMismatch);

        // The proof that nothing downstream was trusted: no OS, display, session, culture,
        // executable or Action check exists in the result at all.
        result.Checks.ShouldNotContain(c => c.Check == WorkstationVerificationCheck.PhotoshopExecutable);
    }

    [Fact]
    public void An_evidence_entry_that_no_longer_hashes_exactly_fails_closed()
    {
        using WorkstationVerificationFixture fixture = new();
        WorkstationVerificationFixture.Corrupt(fixture.EvidencePaths[1]);

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeFalse();
        WorkstationCheckResult evidence = Check(result, WorkstationVerificationCheck.EvidenceIntegrity);
        evidence.Outcome.ShouldBe(WorkstationCheckOutcome.Failed);
        evidence.FailureCode.ShouldBe(FailureCode.PresetHashMismatch);
        result.Checks.ShouldNotContain(c => c.Check == WorkstationVerificationCheck.MeituExecutable);
    }

    [Fact]
    public void A_missing_evidence_file_fails_closed()
    {
        using WorkstationVerificationFixture fixture = new();
        WorkstationVerificationFixture.Remove(fixture.EvidencePaths[0]);

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeFalse();
        WorkstationCheckResult evidence = Check(result, WorkstationVerificationCheck.EvidenceIntegrity);
        evidence.Outcome.ShouldBe(WorkstationCheckOutcome.Failed);
        evidence.FailureCode.ShouldBe(FailureCode.EnvironmentNotVerified);
    }

    // -- workspace root ------------------------------------------------------------------

    [Fact]
    public void The_expected_workspace_root_verifies()
    {
        using WorkstationVerificationFixture fixture = new();

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        Outcome(result, WorkstationVerificationCheck.WorkspaceRoot).ShouldBe(WorkstationCheckOutcome.Passed);
    }

    [Fact]
    public void A_workspace_root_other_than_the_accepted_one_fails()
    {
        using WorkstationVerificationFixture fixture = new();
        string elsewhere = Path.Combine(Path.GetTempPath(), "printflow-not-the-accepted-root");
        Directory.CreateDirectory(elsewhere);

        try
        {
            WorkstationVerificationResult result = fixture.CreateVerifier(elsewhere).Verify();

            result.Verified.ShouldBeFalse();
            WorkstationCheckResult root = Check(result, WorkstationVerificationCheck.WorkspaceRoot);
            root.Outcome.ShouldBe(WorkstationCheckOutcome.Failed);
            root.Expected.ShouldBe(fixture.WorkspaceRoot);
        }
        finally
        {
            Directory.Delete(elsewhere);
        }
    }

    [Fact]
    public void A_workspace_root_that_is_a_file_fails_as_its_own_condition()
    {
        using WorkstationVerificationFixture fixture = new();
        Directory.Delete(fixture.WorkspaceRoot);
        File.WriteAllText(fixture.WorkspaceRoot, "not a directory");

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        WorkstationCheckResult root = Check(result, WorkstationVerificationCheck.WorkspaceRoot);
        root.Outcome.ShouldBe(WorkstationCheckOutcome.Failed);
        root.FailureCode.ShouldBe(FailureCode.WorkspaceError);
        root.Observed.ShouldBe("(a file)");
    }

    // -- operating system ----------------------------------------------------------------

    [Fact]
    public void The_accepted_operating_system_facts_verify()
    {
        using WorkstationVerificationFixture fixture = new();

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        Outcome(result, WorkstationVerificationCheck.OperatingSystem).ShouldBe(WorkstationCheckOutcome.Passed);
    }

    [Fact]
    public void An_operating_system_build_outside_the_accepted_baseline_fails_and_states_both_facts()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.Facts.OperatingSystem = fixture.Facts.OperatingSystem with
        {
            Version = "99.0.9999",
            Build = "9999",
        };

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeFalse();
        WorkstationCheckResult os = Check(result, WorkstationVerificationCheck.OperatingSystem);
        os.Outcome.ShouldBe(WorkstationCheckOutcome.Failed);
        os.Expected!.ShouldContain(WorkstationVerificationFixture.OsBuild);
        os.Observed!.ShouldContain("9999");
    }

    /// <summary>
    /// A machine property the accepted baseline says nothing about is not a reason to fail (§7).
    /// </summary>
    [Fact]
    public void A_system_ui_language_the_baseline_does_not_name_does_not_fail_verification()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.Facts.Culture = fixture.Facts.Culture with { SystemUiCulture = "ja-JP" };

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeTrue(result.Describe());
    }

    // -- interactive session -------------------------------------------------------------

    [Fact]
    public void An_interactive_local_console_session_verifies()
    {
        using WorkstationVerificationFixture fixture = new();

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        Outcome(result, WorkstationVerificationCheck.InteractiveSession).ShouldBe(WorkstationCheckOutcome.Passed);
    }

    [Theory]
    [InlineData(false, 1, false, "Default", "non-interactive station")]
    [InlineData(true, 0, false, "Default", "services session")]
    [InlineData(true, 2, true, "Default", "remote session")]
    [InlineData(true, 1, false, "Winlogon", "secure desktop")]
    [InlineData(true, 1, false, null, "no reachable desktop")]
    public void An_unsupported_session_or_desktop_fails(
        bool interactive, int sessionId, bool remote, string? desktop, string because)
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.Facts.Session = new InteractiveSessionFacts(interactive, sessionId, "Console", remote, desktop);

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeFalse(because);
        Outcome(result, WorkstationVerificationCheck.InteractiveSession)
            .ShouldBe(WorkstationCheckOutcome.Failed, because);
    }

    /// <summary>
    /// Session readiness never asks which application owns the foreground (§8).
    /// </summary>
    /// <remarks>
    /// Structural rather than behavioural: <see cref="IWorkstationFactReader"/> has no member
    /// that could report a foreground window, so the verifier cannot consult one, cannot require
    /// Photoshop or Meitu to hold focus, and cannot take focus itself. Foreground ownership stays
    /// where Epic 11400 put it — re-verified immediately before each guarded input, yielding
    /// <see cref="FailureCode.PhotoshopTargetLost"/> with nothing sent when it has moved.
    /// </remarks>
    [Fact]
    public void The_fact_reader_exposes_no_foreground_or_input_surface()
    {
        string[] members = [.. typeof(IWorkstationFactReader).GetMethods().Select(m => m.Name)];

        members.ShouldNotContain(m => m.Contains("Foreground", StringComparison.OrdinalIgnoreCase));
        members.ShouldNotContain(m => m.Contains("Activate", StringComparison.OrdinalIgnoreCase));
        members.ShouldNotContain(m => m.Contains("Focus", StringComparison.OrdinalIgnoreCase));
        members.ShouldNotContain(m => m.Contains("Window", StringComparison.OrdinalIgnoreCase));
    }

    // -- display -------------------------------------------------------------------------

    [Fact]
    public void The_accepted_display_configuration_verifies()
    {
        using WorkstationVerificationFixture fixture = new();

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        Outcome(result, WorkstationVerificationCheck.DisplayConfiguration).ShouldBe(WorkstationCheckOutcome.Passed);
    }

    [Fact]
    public void Display_scaling_that_differs_from_the_verified_configuration_fails()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.Facts.Display = fixture.Facts.Display with { SystemDpi = 96 };

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeFalse();
        WorkstationCheckResult display = Check(result, WorkstationVerificationCheck.DisplayConfiguration);
        display.Outcome.ShouldBe(WorkstationCheckOutcome.Failed);
        display.Explanation.ShouldContain("scaling");
        display.Expected!.ShouldContain("125%");
        display.Observed!.ShouldContain("100%");
    }

    [Fact]
    public void A_second_active_display_fails_the_accepted_topology()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.Facts.Display = fixture.Facts.Display with { ActiveDisplayCount = 2 };

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeFalse();
        Outcome(result, WorkstationVerificationCheck.DisplayConfiguration).ShouldBe(WorkstationCheckOutcome.Failed);
    }

    [Fact]
    public void A_different_work_area_fails_even_at_the_accepted_resolution()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.Facts.Display = fixture.Facts.Display with
        {
            WorkArea = new DisplayRectangle(0, 0, WorkstationVerificationFixture.DisplayWidth, 700),
        };

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeFalse();
        Outcome(result, WorkstationVerificationCheck.DisplayConfiguration).ShouldBe(WorkstationCheckOutcome.Failed);
    }

    // -- culture -------------------------------------------------------------------------

    [Fact]
    public void The_accepted_user_ui_language_verifies()
    {
        using WorkstationVerificationFixture fixture = new();

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        Outcome(result, WorkstationVerificationCheck.UiCulture).ShouldBe(WorkstationCheckOutcome.Passed);
    }

    [Fact]
    public void A_user_ui_language_other_than_the_accepted_one_fails()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.Facts.Culture = fixture.Facts.Culture with { UserUiCulture = "en-US" };

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeFalse();
        WorkstationCheckResult culture = Check(result, WorkstationVerificationCheck.UiCulture);
        culture.Outcome.ShouldBe(WorkstationCheckOutcome.Failed);
        culture.Expected.ShouldBe(WorkstationVerificationFixture.UiCulture);
        culture.Observed.ShouldBe("en-US");
    }

    /// <summary>
    /// The external applications' own interface languages are reported, never statically
    /// demanded (§10, §17).
    /// </summary>
    [Fact]
    public void The_external_application_ui_language_is_an_advisory_and_never_blocks()
    {
        using WorkstationVerificationFixture fixture = new();

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        WorkstationCheckResult advisory =
            Check(result, WorkstationVerificationCheck.ExternalApplicationUiLanguage);
        advisory.Outcome.ShouldBe(WorkstationCheckOutcome.Advisory);
        advisory.Observed!.ShouldContain(WorkstationVerificationFixture.PhotoshopUiLanguage);
        result.Verified.ShouldBeTrue(result.Describe());
    }

    // -- Meitu ---------------------------------------------------------------------------

    [Fact]
    public void The_accepted_Meitu_executable_verifies_exactly()
    {
        using WorkstationVerificationFixture fixture = new();

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        Outcome(result, WorkstationVerificationCheck.MeituExecutable).ShouldBe(WorkstationCheckOutcome.Passed);
    }

    [Fact]
    public void A_changed_Meitu_binary_fails_verification()
    {
        using WorkstationVerificationFixture fixture = new();
        WorkstationVerificationFixture.Corrupt(fixture.MeituPath);

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeFalse();
        WorkstationCheckResult meitu = Check(result, WorkstationVerificationCheck.MeituExecutable);
        meitu.Outcome.ShouldBe(WorkstationCheckOutcome.Failed);
        meitu.FailureCode.ShouldBe(FailureCode.MeituNotInstalled);
    }

    [Fact]
    public void An_absent_Meitu_executable_fails_verification()
    {
        using WorkstationVerificationFixture fixture = new();
        WorkstationVerificationFixture.Remove(fixture.MeituPath);

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        Check(result, WorkstationVerificationCheck.MeituExecutable).Observed.ShouldBe("(absent)");
    }

    // -- Photoshop -----------------------------------------------------------------------

    [Fact]
    public void The_accepted_Photoshop_executable_verifies_exactly()
    {
        using WorkstationVerificationFixture fixture = new();

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        Outcome(result, WorkstationVerificationCheck.PhotoshopExecutable).ShouldBe(WorkstationCheckOutcome.Passed);
    }

    [Fact]
    public void A_changed_Photoshop_binary_fails_verification()
    {
        using WorkstationVerificationFixture fixture = new();
        WorkstationVerificationFixture.Corrupt(fixture.PhotoshopPath);

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeFalse();
        WorkstationCheckResult photoshop = Check(result, WorkstationVerificationCheck.PhotoshopExecutable);
        photoshop.Outcome.ShouldBe(WorkstationCheckOutcome.Failed);
        photoshop.FailureCode.ShouldBe(FailureCode.PhotoshopNotInstalled);
    }

    /// <summary>
    /// A version fact is demanded only when the accepted preset records one (§13).
    /// </summary>
    /// <remarks>
    /// The fixture's <c>photoshopContract</c> records no <c>productVersion</c>, and verification
    /// passes without one. That is the same rule that keeps the historical "20.0.10" shorthand
    /// out of the verifier: it demands the version facts the accepted authority actually carries,
    /// never a version remembered from prose.
    /// </remarks>
    [Fact]
    public void A_version_fact_the_preset_does_not_record_is_not_demanded()
    {
        using WorkstationVerificationFixture fixture = new();

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeTrue(result.Describe());
    }

    // -- canonical Action artefact -------------------------------------------------------

    [Fact]
    public void The_canonical_action_artefact_verifies_exactly()
    {
        using WorkstationVerificationFixture fixture = new();

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        WorkstationCheckResult action = Check(result, WorkstationVerificationCheck.PhotoshopActionArtifact);
        action.Outcome.ShouldBe(WorkstationCheckOutcome.Passed);
        action.Explanation.ShouldContain(WorkstationVerificationFixture.ActionSetName);
    }

    [Fact]
    public void An_action_artefact_that_no_longer_hashes_exactly_fails()
    {
        using WorkstationVerificationFixture fixture = new();
        // Byte-for-byte the same length as the accepted artefact, so the digest is what refuses
        // it rather than the size — an edited Action set is the same size far more often than not.
        File.WriteAllText(fixture.ActionPath, "SYNTHETIC ACTION BYTES");

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeFalse();
        WorkstationCheckResult action = Check(result, WorkstationVerificationCheck.PhotoshopActionArtifact);
        action.Outcome.ShouldBe(WorkstationCheckOutcome.Failed);
        action.FailureCode.ShouldBe(FailureCode.PresetHashMismatch);
    }

    // -- read-only attribute advisory ----------------------------------------------------

    /// <summary>
    /// §15: SHA-256 is the authority; the filesystem attribute is reported, not required.
    /// </summary>
    [Fact]
    public void A_missing_read_only_attribute_is_advisory_and_does_not_close_verification()
    {
        using WorkstationVerificationFixture fixture = new();

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeTrue(result.Describe());

        WorkstationCheckResult advisory =
            Check(result, WorkstationVerificationCheck.FilesystemReadOnlyPolicyAdvisory);
        advisory.Outcome.ShouldBe(WorkstationCheckOutcome.Advisory);
        advisory.FailureCode.ShouldBeNull();
        advisory.Observed!.ShouldContain("3");
        result.Failures.ShouldBeEmpty();
    }

    [Fact]
    public void Read_only_evidence_reports_no_advisory_drift_and_verification_changes_no_attribute()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.MarkEvidenceReadOnly();

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeTrue(result.Describe());
        Check(result, WorkstationVerificationCheck.FilesystemReadOnlyPolicyAdvisory)
            .Observed.ShouldBe("0/3 integrity-referenced files are not marked read-only");

        // The slice reports the policy; it never applies or clears it.
        fixture.EvidencePaths.ShouldAllBe(p => new FileInfo(p).IsReadOnly);
    }

    // -- launches nothing ----------------------------------------------------------------

    /// <summary>
    /// Static verification reads bytes; it starts no application (§17).
    /// </summary>
    /// <remarks>
    /// The fixture's stand-in Meitu and Photoshop "executables" are text files that could not be
    /// launched at all, and verification of them passes — which it could only do by reading them.
    /// The recording reader then shows that every path the verifier touched was touched by a read.
    /// </remarks>
    [Fact]
    public void Static_verification_only_ever_reads_and_starts_no_external_application()
    {
        using WorkstationVerificationFixture fixture = new();
        RecordingArtifactReader recorder = new(fixture.Artifacts);
        fixture.Artifacts = recorder;

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Verified.ShouldBeTrue(result.Describe());
        recorder.FilesRead.ShouldContain(fixture.MeituPath);
        recorder.FilesRead.ShouldContain(fixture.PhotoshopPath);
        recorder.FilesRead.ShouldContain(fixture.ActionPath);
        recorder.DirectoriesRead.ShouldContain(fixture.WorkspaceRoot);

        // The seam itself is the guarantee: there is no member that could start anything.
        typeof(IWorkstationArtifactReader).GetMethods()
            .Select(m => m.Name)
            .ShouldBe(["ReadFile", "ReadDirectory"], ignoreOrder: true);
    }

    // -- dynamic versus immutable --------------------------------------------------------

    /// <summary>
    /// A dynamic pass is never remembered (§16).
    /// </summary>
    [Fact]
    public void A_dynamic_check_that_passed_once_is_re_evaluated_on_the_next_call()
    {
        using WorkstationVerificationFixture fixture = new();
        ProductionWorkstationVerifier verifier = fixture.CreateVerifier();

        verifier.Verify().Verified.ShouldBeTrue();

        // The operator plugs in a second monitor between two production steps.
        fixture.Facts.Display = fixture.Facts.Display with { ActiveDisplayCount = 2 };

        WorkstationVerificationResult second = verifier.Verify();

        second.Verified.ShouldBeFalse("a cached dynamic pass would still say verified");
        Outcome(second, WorkstationVerificationCheck.DisplayConfiguration).ShouldBe(WorkstationCheckOutcome.Failed);
        fixture.Facts.DynamicReadCount.ShouldBe(2);
    }

    [Fact]
    public void Every_check_declares_whether_it_is_immutable_or_dynamic()
    {
        using WorkstationVerificationFixture fixture = new();

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.ImmutableChecks.Select(c => c.Check).ShouldBe(
            [
                WorkstationVerificationCheck.PresetIntegrity,
                WorkstationVerificationCheck.EvidenceIntegrity,
                WorkstationVerificationCheck.FilesystemReadOnlyPolicyAdvisory,
                WorkstationVerificationCheck.OperatingSystem,
                WorkstationVerificationCheck.MeituExecutable,
                WorkstationVerificationCheck.PhotoshopExecutable,
                WorkstationVerificationCheck.PhotoshopActionArtifact,
            ],
            ignoreOrder: true);

        result.DynamicChecks.Select(c => c.Check).ShouldBe(
            [
                WorkstationVerificationCheck.WorkspaceRoot,
                WorkstationVerificationCheck.InteractiveSession,
                WorkstationVerificationCheck.DisplayConfiguration,
                WorkstationVerificationCheck.UiCulture,
                WorkstationVerificationCheck.ExternalApplicationUiLanguage,
            ],
            ignoreOrder: true);
    }

    /// <summary>
    /// The result is a set of typed, explained checks — never a bare boolean (§5, §19).
    /// </summary>
    [Fact]
    public void Every_failure_reports_a_typed_check_a_code_and_an_operator_facing_explanation()
    {
        using WorkstationVerificationFixture fixture = new();
        WorkstationVerificationFixture.Corrupt(fixture.PhotoshopPath);
        fixture.Facts.Display = fixture.Facts.Display with { SystemDpi = 96 };

        WorkstationVerificationResult result = fixture.CreateVerifier().Verify();

        result.Failures.Count().ShouldBe(2);
        foreach (WorkstationCheckResult failure in result.Failures)
        {
            failure.FailureCode.ShouldNotBeNull();
            failure.Explanation.ShouldNotBeNullOrWhiteSpace();
            failure.Expected.ShouldNotBeNullOrWhiteSpace();
            failure.Observed.ShouldNotBeNullOrWhiteSpace();

            // Concise operator text, not an evidence dump.
            failure.Explanation.Length.ShouldBeLessThan(400);
        }

        result.Describe().ShouldContain("NOT verified");
    }

    [Fact]
    public void The_result_stamps_when_the_dynamic_half_was_observed()
    {
        using WorkstationVerificationFixture fixture = new();
        ProductionWorkstationVerifier verifier = fixture.CreateVerifier();

        DateTimeOffset first = verifier.Verify().ObservedAt;
        fixture.Clock.Advance(TimeSpan.FromMinutes(5));

        verifier.Verify().ObservedAt.ShouldBe(first.AddMinutes(5));
    }

    // -- the gate is untouched -----------------------------------------------------------

    /// <summary>
    /// Part A delivers a verifier, not an authorisation (§20).
    /// </summary>
    [Fact]
    public void The_environment_gate_still_refuses_production_however_the_verifier_answers()
    {
        using WorkstationVerificationFixture fixture = new();
        fixture.CreateVerifier().Verify().Verified.ShouldBeTrue();

        IEnvironmentGate gate = new UnverifiedEnvironmentGate();

        gate.Verify(AdapterExecutionMode.Fake).IsSuccess.ShouldBeTrue();

        OperationResult<PrintFlow.Domain.Results.Unit> production = gate.Verify(AdapterExecutionMode.Production);
        production.IsFailure.ShouldBeTrue();
        production.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
    }

    /// <summary>
    /// The configured adapter mode is Production, against the preset Part A accepted
    /// (Epic 11500 Part D §13).
    /// </summary>
    /// <remarks>
    /// This read <c>Fake</c> from Part A until Part D activated Production. The half that has
    /// B1 intentionally moves the accepted identity to 1.16.0 because the signed Photoshop
    /// prompt evidence changes the immutable UI contract; Production mode itself remains fixed.
    /// </remarks>
    [Fact]
    public void The_configured_adapter_mode_is_production_against_the_accepted_preset()
    {
        PrintFlowConfiguration configuration =
            PrintFlowConfiguration.LoadFromFile(RepositoryFile("appsettings.json"));

        configuration.Adapters.Mode.ShouldBe("Production");
        configuration.Preset.Version.ShouldBe("1.16.0");
    }

    private static WorkstationCheckResult Check(
        WorkstationVerificationResult result, WorkstationVerificationCheck check) =>
        result.Checks.SingleOrDefault(c => c.Check == check)
            ?? throw new InvalidOperationException(
                $"No {check} check was reported. Result was: {result.Describe()}");

    private static WorkstationCheckOutcome Outcome(
        WorkstationVerificationResult result, WorkstationVerificationCheck check) =>
        Check(result, check).Outcome;

    internal static string RepositoryFile(string relativePath)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }

        return current is null
            ? throw new InvalidOperationException("Could not locate the repository root.")
            : Path.Combine(current.FullName, relativePath);
    }
}
