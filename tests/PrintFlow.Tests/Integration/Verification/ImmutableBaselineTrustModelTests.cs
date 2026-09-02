using System.Globalization;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Gate;
using PrintFlow.Infrastructure.Verification;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Verification;

/// <summary>
/// The chosen immutable-baseline trust model, made unmistakable (Epic 11500 Part C §7, §12).
/// </summary>
/// <remarks>
/// <b>Model 1 — restart-bound immutable baseline — is retained, deliberately.</b> The signed
/// baseline is read once per process, so replacing an accepted file under a running PrintFlow
/// does not close the <i>gate</i> until PrintFlow restarts. These tests exist so that changing
/// that is a decision somebody takes rather than something that happens.
/// <para>
/// The model is safe for production because the gate is not the last thing standing between a
/// replaced file and an operation that uses it. The artefacts whose bytes can actually affect a
/// production run — the two executables and the canonical Action file — are re-read and re-hashed
/// by the adapters at operation time, on every run, independently of anything the gate cached.
/// Those proofs live beside the adapters that own them
/// (<c>PhotoshopFoundationTests</c>, <c>ProductionMeituProcessorTests</c>,
/// <c>PhotoshopW1ExecutionTests</c>); what is proved here is the gate half and the wording the
/// operator is given.
/// </para>
/// </remarks>
public sealed class ImmutableBaselineTrustModelTests
{
    // ---------------------------------------------------------------- §7, §12

    /// <summary>
    /// Every cached baseline artefact behaves the same way: the running process keeps its
    /// answer, a new process refuses (§7, §12).
    /// </summary>
    /// <remarks>
    /// Part B proved this for the Photoshop binary alone. Widened here to the whole cached set,
    /// because "which artefacts are restart-bound" is exactly the question an operator has to be
    /// told the answer to, and a single-artefact proof leaves it open whether the others were
    /// simply never considered.
    /// </remarks>
    [Theory]
    [InlineData(BaselineArtefact.Manifest)]
    [InlineData(BaselineArtefact.Evidence)]
    [InlineData(BaselineArtefact.MeituExecutable)]
    [InlineData(BaselineArtefact.PhotoshopExecutable)]
    [InlineData(BaselineArtefact.ActionArtifact)]
    public void A_replaced_baseline_artefact_is_restart_bound_at_the_gate(BaselineArtefact artefact)
    {
        using WorkstationVerificationFixture fixture = new();
        VerifiedEnvironmentGate running = new(fixture.CreateVerifier());

        running.Verify(AdapterExecutionMode.Production).IsSuccess.ShouldBeTrue(
            "the fixture workstation is the accepted one before anything is replaced.");

        Replace(artefact, fixture);

        running.Verify(AdapterExecutionMode.Production).IsSuccess.ShouldBeTrue(
            $"{artefact} is part of the signed baseline, which this process read once. " +
            "This is the documented restart semantic, not an oversight.");

        // A restarted PrintFlow — a newly constructed verification graph — reads the changed
        // bytes and closes Production.
        VerifiedEnvironmentGate restarted = new(fixture.CreateVerifier());
        restarted.Verify(AdapterExecutionMode.Production).IsFailure.ShouldBeTrue(
            $"a restart is what makes replaced {artefact} bytes visible to the gate.");
    }

    /// <summary>
    /// The two halves are genuinely different: dynamic facts move under a running process,
    /// cached ones do not (§7, §8).
    /// </summary>
    /// <remarks>
    /// The single most important sentence on the operator screen is that Refresh re-reads one of
    /// these and not the other. This asserts both halves in one process so the distinction
    /// cannot quietly collapse in either direction — a gate that cached the display would be
    /// wrong, and a gate that re-hashed two application binaries on every production step would
    /// be a different design with a different cost.
    /// </remarks>
    [Fact]
    public void Dynamic_facts_move_under_a_running_process_and_cached_artefacts_do_not()
    {
        using WorkstationVerificationFixture fixture = new();
        VerifiedEnvironmentGate gate = new(fixture.CreateVerifier());

        gate.Verify(AdapterExecutionMode.Production).IsSuccess.ShouldBeTrue();

        // Dynamic: closes immediately, reopens immediately.
        DisplayFacts accepted = fixture.Facts.Display;
        fixture.Facts.Display = accepted with { ActiveDisplayCount = 2 };
        gate.Verify(AdapterExecutionMode.Production).IsFailure.ShouldBeTrue();
        fixture.Facts.Display = accepted;
        gate.Verify(AdapterExecutionMode.Production).IsSuccess.ShouldBeTrue();

        // Immutable: does not close at all, in this process.
        WorkstationVerificationFixture.Corrupt(fixture.PhotoshopPath);
        gate.Verify(AdapterExecutionMode.Production).IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// The diagnostics screen reports the cached conclusion and never claims to have re-read the
    /// file (§7, §8).
    /// </summary>
    /// <remarks>
    /// The wording risk the retained model creates, pinned. After a replaced binary the screen
    /// still shows that check as Passed, because that is what the process knows — and the screen
    /// says, in the same view, that a replaced installation needs a restart. What it must never
    /// do is imply the file was just checked: the row's own text comes from the reading that
    /// actually happened, and the restart sentence is what stops "Passed" being read as
    /// "verified a moment ago".
    /// </remarks>
    [Fact]
    public async Task Diagnostics_show_the_cached_conclusion_and_state_the_restart_requirement()
    {
        CultureInfo previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            // This assertion checks the English wording. Pin its resource culture so the test is
            // deterministic on the accepted zh-CN workstation as well as on developer machines.
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");

            using WorkstationVerificationFixture fixture = new();
            VerifiedEnvironmentGate gate = new(fixture.CreateVerifier());
            EnvironmentReadinessViewModel screen = new(gate, new RecordingNavigation());

            await screen.OpenAsync(CancellationToken.None);
            screen.IsReady.ShouldBeTrue();

            WorkstationVerificationFixture.Corrupt(fixture.PhotoshopPath);
            await screen.RefreshCommand.ExecuteAsync(null);

            EnvironmentCheckRow photoshop = screen.Checks.First(
                row => row.SupportKey == nameof(WorkstationVerificationCheck.PhotoshopExecutable));

            photoshop.IsFailure.ShouldBeFalse(
                "the process read that file once; refreshing did not read it again.");

            // And the operator is told what to do about exactly this situation, on the same screen.
            screen.RestartRequirement.ShouldNotBeNullOrWhiteSpace();
            screen.RestartRequirement.ShouldContain("restart", Case.Insensitive);
            screen.RefreshScope.ShouldNotBeNullOrWhiteSpace();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    /// <summary>
    /// Refreshing re-observes the workstation without re-reading the cached files (§4, §8).
    /// </summary>
    /// <remarks>
    /// Counted rather than described. The dynamic reader is consulted once per refresh; the
    /// tampered binary stays passing throughout, which is only possible if the immutable half
    /// really was not re-read. Together these are the factual basis for the two sentences the
    /// screen shows about what Refresh does.
    /// </remarks>
    [Fact]
    public async Task A_refresh_re_observes_the_workstation_without_re_reading_the_cached_files()
    {
        using WorkstationVerificationFixture fixture = new();
        VerifiedEnvironmentGate gate = new(fixture.CreateVerifier());
        EnvironmentReadinessViewModel screen = new(gate, new RecordingNavigation());

        await screen.OpenAsync(CancellationToken.None);
        int perRead = fixture.Facts.DynamicReadCount;

        WorkstationVerificationFixture.Corrupt(fixture.MeituPath);
        await screen.RefreshCommand.ExecuteAsync(null);

        fixture.Facts.DynamicReadCount.ShouldBe(perRead * 2, "the workstation was looked at again.");
        screen.IsReady.ShouldBeTrue("the replaced installation is not visible until a restart.");
    }

    /// <summary>
    /// A restarted PrintFlow reports the drift to the operator, not only to the gate (§7, §12).
    /// </summary>
    /// <remarks>
    /// The other end of the restart instruction: an operator who follows it must actually see the
    /// problem afterwards. A model that refused Production on restart while the screen still said
    /// Ready would leave them with a refusal and no explanation.
    /// </remarks>
    [Fact]
    public async Task After_a_restart_the_screen_names_the_replaced_artefact()
    {
        using WorkstationVerificationFixture fixture = new();
        VerifiedEnvironmentGate before = new(fixture.CreateVerifier());
        before.Verify(AdapterExecutionMode.Production).IsSuccess.ShouldBeTrue();

        WorkstationVerificationFixture.Corrupt(fixture.ActionPath);

        // The restart: a newly constructed graph, exactly as composition would build it.
        VerifiedEnvironmentGate restarted = new(fixture.CreateVerifier());
        EnvironmentReadinessViewModel screen = new(restarted, new RecordingNavigation());
        await screen.OpenAsync(CancellationToken.None);

        screen.IsReady.ShouldBeFalse();
        screen.BlockingFailures.Select(row => row.SupportKey)
            .ShouldContain(nameof(WorkstationVerificationCheck.PhotoshopActionArtifact));
        restarted.Verify(AdapterExecutionMode.Production).IsFailure.ShouldBeTrue();
    }

    /// <summary>
    /// A replaced artefact never widens what is trusted, only narrows it (§7).
    /// </summary>
    /// <remarks>
    /// The direction of the caching matters as much as its lifetime. The cached values are the
    /// ones that were hash-verified against the configured digest; a manifest rewritten under a
    /// running process cannot inject a <i>new</i> accepted path, digest or display geometry into
    /// this run, because nothing re-reads it. The stale answer is the previously proved answer,
    /// which is why staleness here is a restart requirement rather than a vulnerability.
    /// </remarks>
    [Fact]
    public void A_manifest_rewritten_under_a_running_process_cannot_widen_what_is_accepted()
    {
        using WorkstationVerificationFixture fixture = new();
        VerifiedEnvironmentGate running = new(fixture.CreateVerifier());
        running.Verify(AdapterExecutionMode.Production).IsSuccess.ShouldBeTrue();

        // A manifest that would accept anything, if anything re-read it.
        WorkstationVerificationFixture.Corrupt(fixture.ManifestPath);
        fixture.Facts.Display = fixture.Facts.Display with { ActiveDisplayCount = 4 };

        // The rewritten document changed nothing about what this process accepts: the display it
        // is still comparing against is the accepted one, so the wrong display still refuses.
        running.Verify(AdapterExecutionMode.Production).IsFailure.ShouldBeTrue(
            "the accepted values are the ones that were verified, not the ones now on disk.");
    }

    /// <summary>Which cached artefact a case replaces.</summary>
    public enum BaselineArtefact
    {
        /// <summary>The workstation manifest itself.</summary>
        Manifest,

        /// <summary>One evidence file the manifest vouches for.</summary>
        Evidence,

        /// <summary>The accepted Meitu binary.</summary>
        MeituExecutable,

        /// <summary>The accepted Photoshop binary.</summary>
        PhotoshopExecutable,

        /// <summary>The canonical Photoshop Action file.</summary>
        ActionArtifact,
    }

    private static void Replace(BaselineArtefact artefact, WorkstationVerificationFixture fixture) =>
        WorkstationVerificationFixture.Corrupt(artefact switch
        {
            BaselineArtefact.Manifest => fixture.ManifestPath,
            BaselineArtefact.Evidence => fixture.EvidencePaths[0],
            BaselineArtefact.MeituExecutable => fixture.MeituPath,
            BaselineArtefact.PhotoshopExecutable => fixture.PhotoshopPath,
            BaselineArtefact.ActionArtifact => fixture.ActionPath,
            _ => throw new ArgumentOutOfRangeException(nameof(artefact), artefact, "Not a cached artefact."),
        });
}
