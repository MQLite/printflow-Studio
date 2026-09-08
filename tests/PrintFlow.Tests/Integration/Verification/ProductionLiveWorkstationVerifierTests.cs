using System.Collections.Immutable;
using System.IO;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Verification;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Infrastructure.Sqlite;
using Microsoft.Data.Sqlite;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Verification;

public sealed class ProductionLiveWorkstationVerifierTests
{
    [Fact]
    public async Task Three_successful_runs_restore_state_clean_the_probe_and_release_the_lock()
    {
        using WorkstationVerificationFixture fixture = new();
        FileWorkspace workspace = new(fixture.WorkspaceRoot);
        ScriptedMeitu meitu = new(fixture.MeituPath);
        ScriptedPhotoshop photoshop = new(fixture.PhotoshopPath, workspace);
        ScriptedRuntimeFacts facts = new(MatchingSettings(), []);
        RecordingLock automationLock = new();
        ProductionLiveWorkstationVerifier verifier = new(
            meitu, photoshop, facts, automationLock, workspace, fixture.Clock);

        for (int index = 0; index < 3; index++)
        {
            WorkstationLiveVerification result = await verifier.RunAsync(
                Requirements(fixture), CancellationToken.None);
            result.Checks.ShouldAllBe(check => check.Outcome == WorkstationCheckOutcome.Passed);
            result.Evidence.ShouldNotBeNull();
        }

        meitu.EnsureCalls.ShouldBe(3);
        photoshop.EnsureCalls.ShouldBe(3);
        photoshop.OpenCalls.ShouldBe(3);
        photoshop.CloseCalls.ShouldBe(3);
        automationLock.AcquireCalls.ShouldBe(3);
        automationLock.ReleaseCalls.ShouldBe(3);
        automationLock.IsHeld.ShouldBeFalse();
        Directory.EnumerateFiles(fixture.WorkspaceRoot, "PF_ENV_PROBE_*", SearchOption.AllDirectories)
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task Unsaved_operator_work_blocks_the_probe_without_closing_any_document()
    {
        using WorkstationVerificationFixture fixture = new();
        FileWorkspace workspace = new(fixture.WorkspaceRoot);
        ScriptedPhotoshop photoshop = new(fixture.PhotoshopPath, workspace);
        ImmutableArray<PhotoshopRuntimeDocument> documents =
            [new("customer.psd", @"D:\Customer\customer.psd", IsSaved: false, IsActive: true)];
        RecordingLock automationLock = new();
        ProductionLiveWorkstationVerifier verifier = new(
            new ScriptedMeitu(fixture.MeituPath), photoshop,
            new ScriptedRuntimeFacts(MatchingSettings(), documents), automationLock,
            workspace, fixture.Clock);

        WorkstationLiveVerification result = await verifier.RunAsync(
            Requirements(fixture), CancellationToken.None);

        result.Checks.Single(check => check.Check == WorkstationVerificationCheck.PhotoshopSafeStartingState)
            .Outcome.ShouldBe(WorkstationCheckOutcome.Failed);
        result.Checks.Single(check => check.Check == WorkstationVerificationCheck.PhotoshopTestImageRoundTrip)
            .Outcome.ShouldBe(WorkstationCheckOutcome.Blocked);
        photoshop.OpenCalls.ShouldBe(0);
        photoshop.CloseCalls.ShouldBe(0);
        automationLock.ReleaseCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Colour_mismatch_is_reported_with_expected_and_current_and_never_repaired()
    {
        using WorkstationVerificationFixture fixture = new();
        FileWorkspace workspace = new(fixture.WorkspaceRoot);
        ScriptedPhotoshop photoshop = new(fixture.PhotoshopPath, workspace);
        PhotoshopColourSettingsContract wrong = MatchingSettings() with { CmykWorkingSpace = "Wrong CMYK" };
        ScriptedRuntimeFacts facts = new(wrong, []);
        RecordingLock automationLock = new();
        ProductionLiveWorkstationVerifier verifier = new(
            new ScriptedMeitu(fixture.MeituPath), photoshop, facts, automationLock,
            workspace, fixture.Clock);

        WorkstationLiveVerification result = await verifier.RunAsync(
            Requirements(fixture), CancellationToken.None);

        WorkstationCheckResult mismatch = result.Checks.Single(
            check => check.Check == WorkstationVerificationCheck.PhotoshopColourSettings);
        mismatch.Outcome.ShouldBe(WorkstationCheckOutcome.Failed);
        mismatch.Expected!.ShouldContain("Synthetic CMYK");
        mismatch.Observed!.ShouldContain("Wrong CMYK");
        facts.Writes.ShouldBe(0);
        photoshop.OpenCalls.ShouldBe(0);
        automationLock.ReleaseCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Close_identity_failure_fails_closed_retains_the_still_open_probe_and_releases_the_lock()
    {
        using WorkstationVerificationFixture fixture = new();
        FileWorkspace workspace = new(fixture.WorkspaceRoot);
        ScriptedPhotoshop photoshop = new(fixture.PhotoshopPath, workspace) { FailClose = true };
        RecordingLock automationLock = new();
        ProductionLiveWorkstationVerifier verifier = new(
            new ScriptedMeitu(fixture.MeituPath), photoshop,
            new ScriptedRuntimeFacts(MatchingSettings(), []), automationLock,
            workspace, fixture.Clock);

        WorkstationLiveVerification result = await verifier.RunAsync(
            Requirements(fixture), CancellationToken.None);

        result.Checks.Single(check => check.Check == WorkstationVerificationCheck.PhotoshopTestImageRoundTrip)
            .Outcome.ShouldBe(WorkstationCheckOutcome.Failed);
        Directory.EnumerateFiles(fixture.WorkspaceRoot, "PF_ENV_PROBE_*", SearchOption.AllDirectories)
            .ShouldHaveSingleItem("an unconfirmed-open document's backing file must not be deleted");
        automationLock.ReleaseCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Lock_contention_runs_no_application_step_and_does_not_release_another_owner()
    {
        using WorkstationVerificationFixture fixture = new();
        FileWorkspace workspace = new(fixture.WorkspaceRoot);
        ScriptedMeitu meitu = new(fixture.MeituPath);
        ScriptedPhotoshop photoshop = new(fixture.PhotoshopPath, workspace);
        RecordingLock automationLock = new() { Contended = true };
        ProductionLiveWorkstationVerifier verifier = new(
            meitu, photoshop, new ScriptedRuntimeFacts(MatchingSettings(), []), automationLock,
            workspace, fixture.Clock);

        WorkstationLiveVerification result = await verifier.RunAsync(
            Requirements(fixture), CancellationToken.None);

        result.Checks[0].Check.ShouldBe(WorkstationVerificationCheck.ExternalApplicationAutomationLock);
        result.Checks[0].Outcome.ShouldBe(WorkstationCheckOutcome.Failed);
        result.Checks.Skip(1).ShouldAllBe(check => check.Outcome == WorkstationCheckOutcome.Blocked);
        meitu.EnsureCalls.ShouldBe(0);
        photoshop.EnsureCalls.ShouldBe(0);
        automationLock.ReleaseCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Meitu_launch_failure_blocks_every_later_step_and_releases_the_lock()
    {
        using WorkstationVerificationFixture fixture = new();
        FileWorkspace workspace = new(fixture.WorkspaceRoot);
        ScriptedMeitu meitu = new(fixture.MeituPath)
        {
            Failure = OperationFailure.Create(FailureCode.MeituLaunchFailed, "timed out", true),
        };
        ScriptedPhotoshop photoshop = new(fixture.PhotoshopPath, workspace);
        RecordingLock automationLock = new();
        ProductionLiveWorkstationVerifier verifier = new(
            meitu, photoshop, new ScriptedRuntimeFacts(MatchingSettings(), []), automationLock,
            workspace, fixture.Clock);

        WorkstationLiveVerification result = await verifier.RunAsync(
            Requirements(fixture), CancellationToken.None);

        result.Checks.Single(check => check.Check == WorkstationVerificationCheck.MeituLaunchability)
            .FailureCode.ShouldBe(FailureCode.MeituLaunchFailed);
        result.Checks.Skip(2).ShouldAllBe(check => check.Outcome == WorkstationCheckOutcome.Blocked);
        photoshop.EnsureCalls.ShouldBe(0);
        automationLock.ReleaseCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Photoshop_launch_failure_and_probe_open_failure_are_distinct_terminal_results()
    {
        using WorkstationVerificationFixture fixture = new();
        FileWorkspace workspace = new(fixture.WorkspaceRoot);
        RecordingLock firstLock = new();
        ScriptedPhotoshop unavailable = new(fixture.PhotoshopPath, workspace)
        {
            EnsureFailure = OperationFailure.Create(FailureCode.PhotoshopLaunchFailed, "timed out", true),
        };
        ProductionLiveWorkstationVerifier first = new(
            new ScriptedMeitu(fixture.MeituPath), unavailable,
            new ScriptedRuntimeFacts(MatchingSettings(), []), firstLock, workspace, fixture.Clock);

        WorkstationLiveVerification launch = await first.RunAsync(
            Requirements(fixture), CancellationToken.None);
        launch.Checks.Single(check => check.Check == WorkstationVerificationCheck.PhotoshopLaunchability)
            .FailureCode.ShouldBe(FailureCode.PhotoshopLaunchFailed);
        launch.Checks.Single(check => check.Check == WorkstationVerificationCheck.PhotoshopColourSettings)
            .Outcome.ShouldBe(WorkstationCheckOutcome.Blocked);
        firstLock.ReleaseCalls.ShouldBe(1);

        RecordingLock secondLock = new();
        ScriptedPhotoshop cannotOpen = new(fixture.PhotoshopPath, workspace)
        {
            OpenFailure = OperationFailure.Create(FailureCode.PhotoshopOpenInputFailed, "open failed", true),
        };
        ProductionLiveWorkstationVerifier second = new(
            new ScriptedMeitu(fixture.MeituPath), cannotOpen,
            new ScriptedRuntimeFacts(MatchingSettings(), []), secondLock, workspace, fixture.Clock);
        WorkstationLiveVerification open = await second.RunAsync(
            Requirements(fixture), CancellationToken.None);
        open.Checks.Single(check => check.Check == WorkstationVerificationCheck.PhotoshopTestImageRoundTrip)
            .FailureCode.ShouldBe(FailureCode.PhotoshopOpenInputFailed);
        cannotOpen.CloseCalls.ShouldBe(0);
        secondLock.ReleaseCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Cancellation_during_close_retries_only_the_exact_probe_then_cleans_and_unlocks()
    {
        using WorkstationVerificationFixture fixture = new();
        FileWorkspace workspace = new(fixture.WorkspaceRoot);
        using CancellationTokenSource cancellation = new();
        ScriptedPhotoshop photoshop = new(fixture.PhotoshopPath, workspace)
        {
            CancelFirstClose = cancellation,
        };
        RecordingLock automationLock = new();
        ProductionLiveWorkstationVerifier verifier = new(
            new ScriptedMeitu(fixture.MeituPath), photoshop,
            new ScriptedRuntimeFacts(MatchingSettings(), []), automationLock,
            workspace, fixture.Clock);

        WorkstationLiveVerification result = await verifier.RunAsync(
            Requirements(fixture), cancellation.Token);

        result.Checks.ShouldContain(check => check.FailureCode == FailureCode.Cancelled);
        photoshop.CloseCalls.ShouldBe(2);
        Directory.EnumerateFiles(fixture.WorkspaceRoot, "PF_ENV_PROBE_*", SearchOption.AllDirectories)
            .ShouldBeEmpty();
        automationLock.ReleaseCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Static_identity_failure_marks_live_steps_blocked_and_never_calls_live_automation()
    {
        using WorkstationVerificationFixture fixture = new();
        WorkstationVerificationFixture.Remove(fixture.PhotoshopPath);
        RecordingLiveVerifier live = new();
        ProductionWorkstationVerifier verifier = new(
            fixture.ManifestPath, WorkstationVerificationFixture.PresetId,
            WorkstationVerificationFixture.PresetVersion, fixture.ManifestSha256,
            fixture.WorkspaceRoot, fixture.Facts, fixture.Artifacts, fixture.Clock, live);

        WorkstationVerificationResult result = await verifier.RunLiveChecksAsync(CancellationToken.None);

        result.Checks.Single(check => check.Check == WorkstationVerificationCheck.PhotoshopExecutable)
            .Outcome.ShouldBe(WorkstationCheckOutcome.Failed);
        result.Checks.Where(check => check.Kind is WorkstationCheckKind.Live or WorkstationCheckKind.Smoke)
            .ShouldAllBe(check => check.Outcome == WorkstationCheckOutcome.Blocked);
        live.RunCalls.ShouldBe(0);
    }

    [Fact]
    public async Task Sqlite_live_lock_uses_owned_compare_and_swap_and_releases_only_its_token()
    {
        string directory = Path.Combine(Path.GetTempPath(), "printflow-env-lock-" + Guid.NewGuid().ToString("N"));
        string database = Path.Combine(directory, "lock.db");
        Directory.CreateDirectory(directory);
        try
        {
            SqliteConnectionFactory connections = new(database);
            using (SqliteConnection connection = connections.Open())
                MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();
            SqliteEnvironmentAutomationLock first = new(connections, 101, "TEST");
            SqliteEnvironmentAutomationLock second = new(connections, 202, "TEST");

            OperationResult<EnvironmentAutomationLease> acquired = await first.TryAcquireAsync(
                DateTimeOffset.UnixEpoch, CancellationToken.None);
            acquired.IsSuccess.ShouldBeTrue();
            (await second.TryAcquireAsync(DateTimeOffset.UnixEpoch, CancellationToken.None))
                .IsFailure.ShouldBeTrue();
            (await second.ReleaseAsync(new EnvironmentAutomationLease("not-the-owner"), CancellationToken.None))
                .IsFailure.ShouldBeTrue();
            (await first.ReadAsync(CancellationToken.None)).Value.Purpose
                .ShouldBe(AutomationLockPurpose.EnvironmentVerification);

            (await first.ReleaseAsync(acquired.Value, CancellationToken.None)).IsSuccess.ShouldBeTrue();
            (await first.ReadAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static WorkstationRequirements Requirements(WorkstationVerificationFixture fixture) =>
        PresetWorkstationRequirements.Read(
            fixture.ManifestPath, WorkstationVerificationFixture.PresetId,
            WorkstationVerificationFixture.PresetVersion, fixture.ManifestSha256).Value;

    private static PhotoshopColourSettingsContract MatchingSettings() =>
        new("Synthetic RGB", "Synthetic CMYK", "Synthetic Gray", "Synthetic Spot");

    private sealed class RecordingLiveVerifier : IProductionLiveWorkstationVerifier
    {
        public int RunCalls { get; private set; }

        public Task<WorkstationLiveVerification> RunAsync(
            WorkstationRequirements requirements, CancellationToken cancellationToken)
        {
            RunCalls++;
            throw new InvalidOperationException("Static failure must prevent live automation.");
        }

        public WorkstationLiveVerification Reobserve(
            WorkstationRequirements requirements, WorkstationLiveEvidence? evidence) =>
            new(ProductionLiveWorkstationVerifier.BlockedChecks("not run"), null);
    }

    private sealed class RecordingLock : IEnvironmentAutomationLock
    {
        public bool Contended { get; init; }
        public bool IsHeld { get; private set; }
        public int AcquireCalls { get; private set; }
        public int ReleaseCalls { get; private set; }

        public Task<OperationResult<EnvironmentAutomationLease>> TryAcquireAsync(
            DateTimeOffset atUtc, CancellationToken cancellationToken)
        {
            AcquireCalls++;
            if (Contended)
                return Task.FromResult(OperationResult.Fail<EnvironmentAutomationLease>(
                    FailureCode.AdapterUnavailable, "held"));
            IsHeld = true;
            return Task.FromResult(OperationResult.Ok(new EnvironmentAutomationLease("owned")));
        }

        public Task<OperationResult<PrintFlow.Domain.Results.Unit>> ReleaseAsync(
            EnvironmentAutomationLease lease, CancellationToken cancellationToken)
        {
            ReleaseCalls++;
            IsHeld = false;
            return Task.FromResult(OperationResult.Ok());
        }

        public Task<OperationResult<AutomationLockState>> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Ok(new AutomationLockState(
                null, null, null, null,
                IsHeld ? AutomationLockPurpose.EnvironmentVerification : null,
                IsHeld ? "owned" : null)));
    }

    private sealed class ScriptedRuntimeFacts(
        PhotoshopColourSettingsContract settings,
        ImmutableArray<PhotoshopRuntimeDocument> documents) : IPhotoshopRuntimeFactReader
    {
        public int Writes => 0;

        public OperationResult<PhotoshopRuntimeFacts> Read(string acceptedExecutablePath) =>
            OperationResult.Ok(new PhotoshopRuntimeFacts(settings, documents));
    }

    private sealed class ScriptedPhotoshop(string executable, IWorkspace workspace)
        : IPhotoshopAutomationFoundation
    {
        private readonly PhotoshopTarget _target = new(
            new ExternalProcessRef(707, executable, DateTimeOffset.UnixEpoch),
            PhotoshopFakes.Window(owningProcessId: 707));

        public bool FailClose { get; init; }
        public OperationFailure? EnsureFailure { get; init; }
        public OperationFailure? OpenFailure { get; init; }
        public CancellationTokenSource? CancelFirstClose { get; init; }
        public int EnsureCalls { get; private set; }
        public int OpenCalls { get; private set; }
        public int CloseCalls { get; private set; }

        public Task<OperationResult<PhotoshopReadiness>> EnsureReadyAsync(CancellationToken cancellationToken)
        {
            EnsureCalls++;
            return Task.FromResult(EnsureFailure is { } failure
                ? OperationResult.Fail<PhotoshopReadiness>(failure)
                : OperationResult.Ok(Readiness()));
        }

        public Task<OperationResult<PhotoshopReadiness>> ReinspectAsync(
            PhotoshopReadiness previous, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Ok(Readiness()));

        public Task<OperationResult<PhotoshopOpenedDocument>> OpenManagedWorkingFileAsync(
            WorkspaceFileRef workingFile, CancellationToken cancellationToken)
        {
            OpenCalls++;
            if (OpenFailure is { } failure)
                return Task.FromResult(OperationResult.Fail<PhotoshopOpenedDocument>(failure));
            string path = workspace.ResolveAbsolute(workingFile);
            PhotoshopDocumentIdentity identity = new(
                Path.GetFileName(path), Path.GetDirectoryName(path)!, path, Path.GetFileName(path));
            return Task.FromResult(OperationResult.Ok(new PhotoshopOpenedDocument(
                _target, PhotoshopState(PhotoshopStartingState.KnownEditorWithExpectedDocument),
                identity, OtherDocumentsMayBeOpen: false)));
        }

        public Task<OperationResult<PhotoshopTarget>> CloseExactDocumentAsync(
            PhotoshopOpenedDocument opened, WorkspaceFileRef workingFile, CancellationToken cancellationToken)
        {
            CloseCalls++;
            if (CancelFirstClose is { } cancellation && CloseCalls == 1)
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }
            return Task.FromResult(FailClose
                ? OperationResult.Fail<PhotoshopTarget>(
                    FailureCode.PhotoshopDocumentIdentityUnconfirmed, "identity moved")
                : OperationResult.Ok(_target));
        }

        private PhotoshopReadiness Readiness() =>
            new(_target, PhotoshopState(PhotoshopStartingState.KnownEditorNoDocument), WasLaunched: false);

        private static PhotoshopStateSnapshot PhotoshopState(PhotoshopStartingState state) =>
            new(state, [], new PhotoshopObservation(
                "Adobe Photoshop", [], [], true, null));
    }

    private sealed class ScriptedMeitu(string executable) : IMeituAutomationFoundation
    {
        private readonly MeituReadiness _readiness = new(
            new MeituTarget(
                new ExternalProcessRef(606, executable, DateTimeOffset.UnixEpoch),
                MeituFakes.Window(owningProcessId: 606)),
            new MeituStateSnapshot(MeituStartingState.KnownWelcome, [],
                new MeituObservation("Meitu", [], [], true, null)),
            WasLaunched: false);

        public int EnsureCalls { get; private set; }
        public OperationFailure? Failure { get; init; }

        public Task<OperationResult<MeituReadiness>> EnsureReadyAsync(CancellationToken cancellationToken)
        {
            EnsureCalls++;
            return Task.FromResult(Failure is { } failure
                ? OperationResult.Fail<MeituReadiness>(failure)
                : OperationResult.Ok(_readiness));
        }

        public Task<OperationResult<MeituReadiness>> ReinspectAsync(
            MeituReadiness previous, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Ok(_readiness));

        public Task<OperationResult<MeituOpenedWorkingCopy>> OpenWorkingCopyAsync(
            WorkspaceFileRef workingCopy, IAutomationStopSignal stop, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<OperationResult<MeituEnhancementOutcome>> EnhanceAsync(
            MeituOpenedWorkingCopy opened, WorkspaceFileRef workingCopy, IAutomationStopSignal stop,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OperationResult<MeituBackgroundRemovalOutcome>> RemoveBackgroundAsync(
            MeituOpenedWorkingCopy opened, WorkspaceFileRef workingCopy, BackgroundRemovalDecision modeDecision,
            IAutomationStopSignal stop, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OperationResult<MeituExportedOutput>> ExportEnhancedResultAsync(
            MeituEnhancementOutcome enhancement, WorkspaceFileRef workingCopy, FileFacts workingCopyFactsBefore,
            WorkspaceFileRef output, IAutomationStopSignal stop, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<OperationResult<MeituExportedOutput>> ExportBackgroundRemovalResultAsync(
            MeituBackgroundRemovalOutcome backgroundRemoval, WorkspaceFileRef workingCopy,
            FileFacts workingCopyFactsBefore, WorkspaceFileRef output, IAutomationStopSignal stop,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OperationResult<FileFacts>> InspectManagedFileAsync(
            WorkspaceFileRef file, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OperationResult<bool>> DismissExportResultSurfaceAsync(
            MeituTarget target, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<OperationResult<MeituTarget>> CloseDocumentAsync(
            MeituTarget target, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
