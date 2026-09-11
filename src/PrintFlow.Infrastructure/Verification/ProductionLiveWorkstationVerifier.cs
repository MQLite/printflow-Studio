using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Verification;

internal sealed record WorkstationLiveEvidence(
    MeituReadiness Meitu,
    PhotoshopReadiness Photoshop,
    PhotoshopRuntimeFacts PhotoshopFacts);

internal sealed record WorkstationLiveVerification(
    ImmutableArray<WorkstationCheckResult> Checks,
    WorkstationLiveEvidence? Evidence)
{
    public ReadinessProbeDiagnostics? Probe { get; init; }
    public bool ObservationDeferred { get; init; }
}

internal interface IProductionLiveWorkstationVerifier
{
    Task<WorkstationLiveVerification> RunAsync(
        WorkstationRequirements requirements, CancellationToken cancellationToken);

    WorkstationLiveVerification Reobserve(
        WorkstationRequirements requirements, WorkstationLiveEvidence? evidence);

    WorkstationLiveVerification Reobserve(
        WorkstationRequirements requirements,
        WorkstationLiveEvidence? evidence,
        IWorkstationAutomationLease? ownLease) => Reobserve(requirements, evidence);

    WorkstationLiveVerification ReobserveForInternalWork(
        WorkstationRequirements requirements,
        WorkstationLiveEvidence? evidence) => Reobserve(requirements, evidence);
}

/// <summary>
/// The explicit live phase behind the workstation verifier. It reuses the accepted application
/// foundations and exposes no generic process, input, or scripting surface.
/// </summary>
internal sealed class ProductionLiveWorkstationVerifier : IProductionLiveWorkstationVerifier
{
    private static readonly ImmutableArray<WorkstationVerificationCheck> OrderedLiveChecks =
    [
        WorkstationVerificationCheck.ExternalApplicationAutomationLock,
        WorkstationVerificationCheck.MeituLaunchability,
        WorkstationVerificationCheck.MeituSafeStartingState,
        WorkstationVerificationCheck.PhotoshopLaunchability,
        WorkstationVerificationCheck.PhotoshopSafeStartingState,
        WorkstationVerificationCheck.PhotoshopColourSettings,
        WorkstationVerificationCheck.PhotoshopTestImageRoundTrip,
    ];

    private static readonly byte[] ProbePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    private static readonly byte[] ProbeSha256 = SHA256.HashData(ProbePng);

    private readonly IMeituAutomationFoundation _meitu;
    private readonly IPhotoshopAutomationFoundation _photoshop;
    private readonly IPhotoshopRuntimeFactReader _photoshopFacts;
    private readonly IWorkstationAutomationLeaseManager _automationLeases;
    private readonly IWorkspace _workspace;
    private readonly TimeProvider _clock;

    internal ProductionLiveWorkstationVerifier(
        IMeituAutomationFoundation meitu,
        IPhotoshopAutomationFoundation photoshop,
        IPhotoshopRuntimeFactReader photoshopFacts,
        IWorkstationAutomationLeaseManager automationLeases,
        IWorkspace workspace,
        TimeProvider clock)
    {
        _meitu = meitu ?? throw new ArgumentNullException(nameof(meitu));
        _photoshop = photoshop ?? throw new ArgumentNullException(nameof(photoshop));
        _photoshopFacts = photoshopFacts ?? throw new ArgumentNullException(nameof(photoshopFacts));
        _automationLeases = automationLeases ?? throw new ArgumentNullException(nameof(automationLeases));
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<WorkstationLiveVerification> RunAsync(
        WorkstationRequirements requirements, CancellationToken cancellationToken)
    {
        List<WorkstationCheckResult> checks = [];
        IWorkstationAutomationLease? lease = null;
        WorkstationLiveEvidence? evidence = null;
        OperationResult<Unit>? release = null;
        ReadinessProbeDiagnostics? probe = null;

        try
        {
            OperationResult<IWorkstationAutomationLease> acquired = await _automationLeases
                .TryAcquireAsync(enclosingLease: null, cancellationToken)
                .ConfigureAwait(false);
            if (acquired.IsFailure)
            {
                checks.Add(Failed(WorkstationVerificationCheck.ExternalApplicationAutomationLock,
                    WorkstationCheckKind.Live, "Available", "Held", acquired.Failure));
                AddRemainingBlocked(checks, "The shared automation lock was not acquired.");
                return new WorkstationLiveVerification([.. checks], null);
            }

            lease = acquired.Value;
            checks.Add(WorkstationCheckResult.Passed(
                WorkstationVerificationCheck.ExternalApplicationAutomationLock,
                WorkstationCheckKind.Live,
                "Acquired for this bounded live verification",
                "The same global lock used by production automation was acquired."));

            OperationResult<MeituReadiness> meitu = await _meitu
                .EnsureReadyAsync(cancellationToken)
                .ConfigureAwait(false);
            if (meitu.IsFailure)
            {
                checks.Add(Failed(WorkstationVerificationCheck.MeituLaunchability,
                    WorkstationCheckKind.Live, "Accepted process reaches a recognised state", null, meitu.Failure));
                checks.Add(Blocked(WorkstationVerificationCheck.MeituSafeStartingState,
                    WorkstationCheckKind.Live, "Meitu launchability did not pass."));
                AddMissingBlocked(checks, "Meitu did not reach a safe state, so no later application check ran.");
            }
            else
            {
                checks.Add(WorkstationCheckResult.Passed(
                    WorkstationVerificationCheck.MeituLaunchability,
                    WorkstationCheckKind.Live,
                    $"{(meitu.Value.WasLaunched ? "Launched" : "Attached")}; process {meitu.Value.Target.Process.ProcessId}",
                    "The accepted Meitu process presented one positively recognised window."));
                checks.Add(WorkstationCheckResult.Passed(
                    WorkstationVerificationCheck.MeituSafeStartingState,
                    WorkstationCheckKind.Live,
                    meitu.Value.State.State.ToString(),
                    "Meitu is in a positively recognised safe starting state."));

                OperationResult<PhotoshopReadiness> photoshop = await _photoshop
                    .EnsureReadyAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (photoshop.IsFailure)
                {
                    checks.Add(Failed(WorkstationVerificationCheck.PhotoshopLaunchability,
                        WorkstationCheckKind.Live, "Accepted process reaches a recognised state", null,
                        photoshop.Failure));
                    AddPhotoshopBlocked(checks, "Photoshop launchability did not pass.");
                }
                else
                {
                    checks.Add(WorkstationCheckResult.Passed(
                        WorkstationVerificationCheck.PhotoshopLaunchability,
                        WorkstationCheckKind.Live,
                        $"{(photoshop.Value.WasLaunched ? "Launched" : "Attached")}; process {photoshop.Value.Target.Process.ProcessId}",
                        "The accepted Photoshop process presented one positively recognised window."));

                    OperationResult<PhotoshopRuntimeFacts> facts =
                        _photoshopFacts.Read(requirements.Photoshop.Path);
                    if (facts.IsFailure)
                    {
                        checks.Add(Failed(WorkstationVerificationCheck.PhotoshopSafeStartingState,
                            WorkstationCheckKind.Live, "Recognised state with no unsaved document", null,
                            facts.Failure));
                        AddAfterPhotoshopStateBlocked(checks, "Photoshop's current document state could not be read.");
                    }
                    else if (facts.Value.UnsavedDocumentCount > 0)
                    {
                        checks.Add(WorkstationCheckResult.Failed(
                            WorkstationVerificationCheck.PhotoshopSafeStartingState,
                            WorkstationCheckKind.Live,
                            FailureCode.PhotoshopUnknownState,
                            "Recognised state with no unsaved document",
                            facts.Value.DocumentStateDescription,
                            "Photoshop has unsaved operator work. PrintFlow did not save, close, or alter it."));
                        AddAfterPhotoshopStateBlocked(checks, "An unsaved Photoshop document prevents safe live automation.");
                    }
                    else
                    {
                        checks.Add(WorkstationCheckResult.Passed(
                            WorkstationVerificationCheck.PhotoshopSafeStartingState,
                            WorkstationCheckKind.Live,
                            $"{photoshop.Value.State.State}; {facts.Value.DocumentStateDescription}",
                            "Photoshop has a recognised state and no unsaved document or unknown dialog."));

                        PhotoshopColourSettingsContract expected = Expected(requirements.PhotoshopColourSettings);
                        if (!PhotoshopColourSettingsRule.Matches(expected, facts.Value.ColourSettings))
                        {
                            checks.Add(WorkstationCheckResult.Failed(
                                WorkstationVerificationCheck.PhotoshopColourSettings,
                                WorkstationCheckKind.Live,
                                FailureCode.EnvironmentNotVerified,
                                expected.ToString(),
                                facts.Value.ColourSettings.ToString(),
                                "Photoshop's active working spaces differ from the accepted preset. No setting was changed."));
                            checks.Add(Blocked(WorkstationVerificationCheck.PhotoshopTestImageRoundTrip,
                                WorkstationCheckKind.Smoke, "Photoshop colour settings did not pass."));
                        }
                        else
                        {
                            checks.Add(WorkstationCheckResult.Passed(
                                WorkstationVerificationCheck.PhotoshopColourSettings,
                                WorkstationCheckKind.Live,
                                facts.Value.ColourSettings.ToString(),
                                "Photoshop's four active working spaces match the accepted preset."));

                            ProbeRunResult roundTrip = await RunProbeAsync(
                                photoshop.Value, facts.Value, requirements.Workspace.Root, cancellationToken)
                                .ConfigureAwait(false);
                            probe = roundTrip.Diagnostics;
                            if (roundTrip.Failure is { } probeFailure)
                            {
                                checks.Add(Failed(WorkstationVerificationCheck.PhotoshopTestImageRoundTrip,
                                    WorkstationCheckKind.Smoke,
                                    "Open, identify, close without saving, and restore the prior state",
                                    null,
                                    probeFailure));
                            }
                            else
                            {
                                checks.Add(WorkstationCheckResult.Passed(
                                    WorkstationVerificationCheck.PhotoshopTestImageRoundTrip,
                                    WorkstationCheckKind.Smoke,
                                    "Synthetic image opened, identified, closed, and cleaned",
                                    "The exact PrintFlow-owned probe completed and Photoshop returned to its prior safe state."));
                                evidence = new WorkstationLiveEvidence(meitu.Value, photoshop.Value, facts.Value);
                            }
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AddCancellation(checks);
            evidence = null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AddUnexpectedFailure(checks, ex.Message);
            evidence = null;
        }
        finally
        {
            if (lease is not null)
            {
                try
                {
                    release = await lease.ReleaseAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    release = OperationResult.Fail<Unit>(FailureCode.AdapterUnavailable,
                        $"The workstation lease release did not complete: {ex.Message}");
                }
            }
        }

        if (release is { IsFailure: true })
        {
            if (probe is not null)
            {
                ReadinessProbeFailure releaseFailure = ReadinessProbeProgress.ProjectFailure(
                    "LeaseRelease", release.Value.Failure);
                probe = probe.PrimaryFailure is null
                    ? probe with { PrimaryFailure = releaseFailure }
                    : probe with { SecondaryFailures = [.. probe.SecondaryFailures, releaseFailure] };
            }
            int index = checks.FindIndex(check =>
                check.Check == WorkstationVerificationCheck.ExternalApplicationAutomationLock);
            WorkstationCheckResult failedRelease = Failed(
                WorkstationVerificationCheck.ExternalApplicationAutomationLock,
                WorkstationCheckKind.Live,
                "Released on every terminal path",
                "Release failed",
                release.Value.Failure);
            if (index >= 0) checks[index] = failedRelease;
            else checks.Insert(0, failedRelease);
            evidence = null;
        }

        AddMissingBlocked(checks, "The preceding live verification step did not complete.");
        return new WorkstationLiveVerification([.. checks], evidence) { Probe = probe };
    }

    public WorkstationLiveVerification Reobserve(
        WorkstationRequirements requirements, WorkstationLiveEvidence? evidence) =>
        Reobserve(requirements, evidence, ownLease: null, requireAutomationAvailability: true);

    public WorkstationLiveVerification Reobserve(
        WorkstationRequirements requirements,
        WorkstationLiveEvidence? evidence,
        IWorkstationAutomationLease? ownLease) =>
        Reobserve(requirements, evidence, ownLease, requireAutomationAvailability: true);

    public WorkstationLiveVerification ReobserveForInternalWork(
        WorkstationRequirements requirements,
        WorkstationLiveEvidence? evidence) =>
        Reobserve(requirements, evidence, ownLease: null, requireAutomationAvailability: false);

    private WorkstationLiveVerification Reobserve(
        WorkstationRequirements requirements,
        WorkstationLiveEvidence? evidence,
        IWorkstationAutomationLease? ownLease,
        bool requireAutomationAvailability)
    {
        if (evidence is null)
        {
            return new WorkstationLiveVerification(
                BlockedChecks("Live application verification has not been run in this PrintFlow process."), null);
        }

        List<WorkstationCheckResult> checks = [];
        try
        {
            if (requireAutomationAvailability)
            {
                WorkstationAutomationLeaseObservation lockState = _automationLeases
                    .ObserveAsync(ownLease, CancellationToken.None).GetAwaiter().GetResult();
                if (lockState.Status is WorkstationAutomationLeaseStatus.Busy or WorkstationAutomationLeaseStatus.Unknown)
                {
                    checks.Add(WorkstationCheckResult.Failed(
                        WorkstationVerificationCheck.ExternalApplicationAutomationLock,
                        WorkstationCheckKind.Live,
                        FailureCode.AdapterUnavailable,
                        "Available or owned by this operation",
                        lockState.Status.ToString(),
                        lockState.Description));
                    AddRemainingBlocked(checks, lockState.Status == WorkstationAutomationLeaseStatus.Busy
                        ? "Current runtime observations are deferred while automation is busy; prior live evidence was not renewed."
                        : "The shared automation lock is currently unavailable.");
                    // Known contention refuses this external operation but does not invalidate the
                    // already-certified application identities. Internal work can immediately
                    // re-use that evidence and re-inspect every applicable runtime fact. An
                    // unreadable authority is still Unknown and still invalidates the evidence.
                    return new WorkstationLiveVerification(
                        [.. checks],
                        lockState.Status == WorkstationAutomationLeaseStatus.Busy ? evidence : null)
                    {
                        ObservationDeferred = lockState.Status == WorkstationAutomationLeaseStatus.Busy,
                    };
                }

                checks.Add(WorkstationCheckResult.Passed(
                    WorkstationVerificationCheck.ExternalApplicationAutomationLock,
                    WorkstationCheckKind.Live,
                    lockState.Status == WorkstationAutomationLeaseStatus.Owned
                        ? "Owned by this operation"
                        : "Available",
                    lockState.Description));
            }
            else
            {
                checks.Add(WorkstationCheckResult.Advisory(
                    WorkstationVerificationCheck.ExternalApplicationAutomationLock,
                    WorkstationCheckKind.Live,
                    "Not required",
                    "This in-process operation does not control Meitu or Photoshop, so physical " +
                    "automation availability is outside its admission decision."));
            }

            OperationResult<MeituReadiness> meitu = _meitu
                .ReinspectAsync(evidence.Meitu, CancellationToken.None).GetAwaiter().GetResult();
            AddReobservedApplication(checks, meitu, isPhotoshop: false);

            OperationResult<PhotoshopReadiness> photoshop = _photoshop
                .ReinspectAsync(evidence.Photoshop, CancellationToken.None).GetAwaiter().GetResult();
            if (photoshop.IsFailure)
            {
                checks.Add(Failed(WorkstationVerificationCheck.PhotoshopLaunchability,
                    WorkstationCheckKind.Live, "Certified process remains recognised", null, photoshop.Failure));
                AddPhotoshopBlocked(checks, "The certified Photoshop process is no longer ready.");
                return new WorkstationLiveVerification([.. checks], null);
            }

            checks.Add(WorkstationCheckResult.Passed(
                WorkstationVerificationCheck.PhotoshopLaunchability,
                WorkstationCheckKind.Live,
                $"Certified process {photoshop.Value.Target.Process.ProcessId} is still recognised",
                "The same Photoshop process certified by the live run remains available."));

            OperationResult<PhotoshopRuntimeFacts> facts = _photoshopFacts.Read(requirements.Photoshop.Path);
            if (facts.IsFailure || facts.Value.UnsavedDocumentCount > 0)
            {
                checks.Add(facts.IsFailure
                    ? Failed(WorkstationVerificationCheck.PhotoshopSafeStartingState,
                        WorkstationCheckKind.Live, "Recognised state with no unsaved document", null,
                        facts.Failure)
                    : WorkstationCheckResult.Failed(
                        WorkstationVerificationCheck.PhotoshopSafeStartingState,
                        WorkstationCheckKind.Live,
                        FailureCode.PhotoshopUnknownState,
                        "Recognised state with no unsaved document",
                        facts.Value.DocumentStateDescription,
                        "Photoshop now has unsaved work. PrintFlow did not alter it."));
                AddAfterPhotoshopStateBlocked(checks, "Photoshop's current state is not safe for automation.");
                return new WorkstationLiveVerification([.. checks], null);
            }

            checks.Add(WorkstationCheckResult.Passed(
                WorkstationVerificationCheck.PhotoshopSafeStartingState,
                WorkstationCheckKind.Live,
                $"{photoshop.Value.State.State}; {facts.Value.DocumentStateDescription}",
                "Photoshop still has a recognised state with no unsaved document or unknown dialog."));

            PhotoshopColourSettingsContract expected = Expected(requirements.PhotoshopColourSettings);
            if (!PhotoshopColourSettingsRule.Matches(expected, facts.Value.ColourSettings))
            {
                checks.Add(WorkstationCheckResult.Failed(
                    WorkstationVerificationCheck.PhotoshopColourSettings,
                    WorkstationCheckKind.Live,
                    FailureCode.EnvironmentNotVerified,
                    expected.ToString(),
                    facts.Value.ColourSettings.ToString(),
                    "Photoshop's active working spaces changed after certification. No setting was changed."));
                checks.Add(Blocked(WorkstationVerificationCheck.PhotoshopTestImageRoundTrip,
                    WorkstationCheckKind.Smoke, "The current colour settings no longer match."));
                return new WorkstationLiveVerification([.. checks], null);
            }

            checks.Add(WorkstationCheckResult.Passed(
                WorkstationVerificationCheck.PhotoshopColourSettings,
                WorkstationCheckKind.Live,
                facts.Value.ColourSettings.ToString(),
                "Photoshop's active working spaces still match the accepted preset."));
            checks.Add(WorkstationCheckResult.Passed(
                WorkstationVerificationCheck.PhotoshopTestImageRoundTrip,
                WorkstationCheckKind.Smoke,
                $"Certified for Photoshop process {photoshop.Value.Target.Process.ProcessId}",
                "The explicit synthetic-image round trip remains tied to this running Photoshop process."));

            return meitu.IsSuccess
                ? new WorkstationLiveVerification([.. checks],
                    new WorkstationLiveEvidence(meitu.Value, photoshop.Value, facts.Value))
                : new WorkstationLiveVerification([.. checks], null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            AddUnexpectedFailure(checks, ex.Message);
            AddMissingBlocked(checks, "Current live state could not be re-observed.");
            return new WorkstationLiveVerification([.. checks], null);
        }
    }

    internal static ImmutableArray<WorkstationCheckResult> BlockedChecks(string reason) =>
        [.. OrderedLiveChecks.Select(check => Blocked(
            check,
            check == WorkstationVerificationCheck.PhotoshopTestImageRoundTrip
                ? WorkstationCheckKind.Smoke
                : WorkstationCheckKind.Live,
            reason))];

    private sealed record ProbeRunResult(OperationFailure? Failure, ReadinessProbeDiagnostics Diagnostics);

    private async Task<ProbeRunResult> RunProbeAsync(
        PhotoshopReadiness readiness,
        PhotoshopRuntimeFacts before,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        string token = Guid.NewGuid().ToString("N");
        WorkspaceFileRef probe = WorkspaceFileRef.Create(
            $"EnvironmentVerification/{token}/Working/PF_ENV_PROBE_{token}.png",
            WorkspaceArea.Working);
        ReadinessProbeProgress progress = new(token, readiness.Target.Process);
        string? absolute = null;
        PhotoshopOpenedDocument? opened = null;
        bool closed = false;
        bool openAttempted = false;
        string phase = "Create";

        try
        {
            OperationResult<Unit> result = await ExecuteAsync().ConfigureAwait(false);
            if (result.IsFailure) progress.Fail(phase, result.Failure);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            progress.Fail(phase, OperationFailure.Create(FailureCode.Cancelled,
                "Live application verification was cancelled; bounded exact-probe unwind follows."));
            if (opened is not null && !closed)
            {
                using CancellationTokenSource cleanup = new(TimeSpan.FromSeconds(15));
                try
                {
                    progress.Observe(ReadinessProbeStage.CloseGuard);
                    OperationResult<PhotoshopTarget> close = await _photoshop
                        .CloseExactDocumentAsync(opened, probe, progress.Observe, cleanup.Token)
                        .ConfigureAwait(false);
                    closed = close.IsSuccess;
                    if (closed) progress.Observe(ReadinessProbeStage.CloseConfirmed);
                    else progress.Fail("CancellationClose", close.Failure);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    progress.Fail("CancellationClose", OperationFailure.Create(
                        ex is OperationCanceledException ? FailureCode.Cancelled : FailureCode.EnvironmentNotVerified,
                        $"The bounded exact-probe close did not complete: {ex.Message}"));
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            progress.Fail(phase, OperationFailure.Create(FailureCode.EnvironmentNotVerified,
                $"Live verification stopped safely: {ex.Message}"));
        }
        finally
        {
            if (absolute is not null && (closed || !openAttempted))
            {
                progress.CleanupReason = closed
                    ? "The exact probe close was confirmed."
                    : "The open helper was never entered.";
                progress.Observe(ReadinessProbeStage.CleanupAttempted);
                try
                {
                    OperationResult<Unit> cleanup = DeleteProbe(absolute, workspaceRoot);
                    progress.CleanupOutcome = cleanup.IsSuccess ? ProbeCleanupOutcome.Succeeded : ProbeCleanupOutcome.Failed;
                    if (cleanup.IsSuccess) progress.Observe(ReadinessProbeStage.CleanupCompleted);
                    else progress.Fail("Cleanup", cleanup.Failure);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    progress.CleanupOutcome = ProbeCleanupOutcome.Unknown;
                    progress.Fail("Cleanup", OperationFailure.Create(FailureCode.WorkspaceError,
                        $"Probe cleanup did not return an outcome: {ex.Message}"));
                }
            }
            else
            {
                progress.CleanupReason = absolute is null
                    ? "No contained managed probe path was established."
                    : "Open was entered and close is unconfirmed; existing ownership rules retain the backing file.";
            }
        }

        return new ProbeRunResult(progress.Failure, progress.Snapshot());

        async Task<OperationResult<Unit>> ExecuteAsync()
        {
            progress.Observe(ReadinessProbeStage.ProbeCreation);
            absolute = RequireContainedProbePath(_workspace.ResolveAbsolute(probe), workspaceRoot);
            progress.ManagedPath = absolute;
            OperationResult<Unit> created = await CreateProbeAsync(absolute, workspaceRoot, cancellationToken)
                .ConfigureAwait(false);
            if (created.IsFailure) return created;
            progress.Observe(ReadinessProbeStage.ProbeCreated);

            phase = "Open";
            openAttempted = true;
            progress.Observe(ReadinessProbeStage.OpenGuard);
            OperationResult<PhotoshopOpenedDocument> open = await _photoshop
                .OpenManagedWorkingFileAsync(probe, progress.Observe, cancellationToken)
                .ConfigureAwait(false);
            if (open.IsFailure) return OperationResult.Fail<Unit>(open.Failure);
            opened = open.Value;
            // A successful foundation result already contracts both confirmations, even for a
            // legacy implementation without request instrumentation. Requests remain unrecorded.
            progress.Observe(ReadinessProbeStage.OpenConfirmed);
            progress.Observe(ReadinessProbeStage.IdentityConfirmed);

            phase = "Close";
            progress.Observe(ReadinessProbeStage.CloseGuard);
            OperationResult<PhotoshopTarget> close = await _photoshop
                .CloseExactDocumentAsync(opened, probe, progress.Observe, cancellationToken)
                .ConfigureAwait(false);
            if (close.IsFailure) return OperationResult.Fail<Unit>(close.Failure);
            closed = true;
            progress.Observe(ReadinessProbeStage.CloseConfirmed);

            phase = "PriorState";
            progress.Observe(ReadinessProbeStage.PriorStateCheck);
            OperationResult<PhotoshopReadiness> restored = await _photoshop
                .ReinspectAsync(readiness with { Target = close.Value }, cancellationToken)
                .ConfigureAwait(false);
            if (restored.IsFailure) return OperationResult.Fail<Unit>(restored.Failure);
            OperationResult<PhotoshopRuntimeFacts> after = _photoshopFacts.Read(readiness.Target.Process.ExecutablePath);
            if (after.IsFailure) return OperationResult.Fail<Unit>(after.Failure);
            if (!DocumentsRestored(before.Documents, after.Value.Documents))
                return OperationResult.Fail<Unit>(FailureCode.PhotoshopUnknownState,
                    "The synthetic document closed, but Photoshop did not return to the same prior document state.");
            progress.Observe(ReadinessProbeStage.PriorStateRestored);
            return OperationResult.Ok();
        }
    }

    private static async Task<OperationResult<Unit>> CreateProbeAsync(
        string absolute, string workspaceRoot, CancellationToken cancellationToken)
    {
        try
        {
            string full = RequireContainedProbePath(absolute, workspaceRoot);
            string directory = Path.GetDirectoryName(full)!;
            Directory.CreateDirectory(directory);
            RefuseReparseAncestry(directory, Path.GetFullPath(workspaceRoot));
            await File.WriteAllBytesAsync(full, ProbePng, cancellationToken).ConfigureAwait(false);
            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.WorkspaceError,
                $"The PrintFlow-owned synthetic verification image could not be created: {ex.Message}");
        }
    }

    private static OperationResult<Unit> DeleteProbe(string absolute, string workspaceRoot)
    {
        try
        {
            string full = RequireContainedProbePath(absolute, workspaceRoot);
            RefuseReparseAncestry(Path.GetDirectoryName(full)!, Path.GetFullPath(workspaceRoot));
            if (File.Exists(full))
            {
                byte[] observed;
                using (FileStream stream = new(full, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    observed = SHA256.HashData(stream);
                }

                if (!observed.AsSpan().SequenceEqual(ProbeSha256))
                {
                    return OperationResult.Fail<Unit>(
                        FailureCode.WorkspaceError,
                        "The synthetic probe file changed unexpectedly, so it was retained rather than deleted.");
                }

                File.Delete(full);
            }

            string? working = Path.GetDirectoryName(full);
            string? run = working is null ? null : Path.GetDirectoryName(working);
            if (working is not null && Directory.Exists(working) && !Directory.EnumerateFileSystemEntries(working).Any())
                Directory.Delete(working, recursive: false);
            if (run is not null && Directory.Exists(run) && !Directory.EnumerateFileSystemEntries(run).Any())
                Directory.Delete(run, recursive: false);
            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.WorkspaceError,
                $"The confirmed-closed synthetic probe could not be cleaned up safely: {ex.Message}");
        }
    }

    private static string RequireContainedProbePath(string absolute, string workspaceRoot)
    {
        string root = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(absolute);
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            !full.Contains($"{Path.DirectorySeparatorChar}EnvironmentVerification{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(full).StartsWith("PF_ENV_PROBE_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The probe path is outside the verification-owned directory.");
        }

        return full;
    }

    private static void RefuseReparseAncestry(string path, string root)
    {
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Verification refuses reparse traversal at '{current}'.");
            if (string.Equals(current, root, StringComparison.OrdinalIgnoreCase)) return;
        }

        throw new InvalidOperationException("The verification directory is not beneath the configured workspace.");
    }

    private static bool DocumentsRestored(
        ImmutableArray<PhotoshopRuntimeDocument> before,
        ImmutableArray<PhotoshopRuntimeDocument> after) =>
        before.Length == after.Length && before.Zip(after).All(pair =>
            string.Equals(pair.First.Name, pair.Second.Name, StringComparison.Ordinal) &&
            string.Equals(pair.First.FullPath, pair.Second.FullPath, StringComparison.OrdinalIgnoreCase) &&
            pair.First.IsSaved == pair.Second.IsSaved &&
            pair.First.IsActive == pair.Second.IsActive);

    private static PhotoshopColourSettingsContract Expected(AcceptedPhotoshopColourSettings expected) =>
        new(expected.RgbWorkingSpace, expected.CmykWorkingSpace,
            expected.GrayWorkingSpace, expected.SpotWorkingSpace);

    private static WorkstationCheckResult Failed(
        WorkstationVerificationCheck check,
        WorkstationCheckKind kind,
        string? expected,
        string? observed,
        OperationFailure failure) =>
        WorkstationCheckResult.Failed(check, kind, failure.Code, expected, observed, failure.TechnicalDetail);

    private static WorkstationCheckResult Blocked(
        WorkstationVerificationCheck check, WorkstationCheckKind kind, string reason) =>
        WorkstationCheckResult.Blocked(check, kind, ExpectedFor(check), reason);

    private static string ExpectedFor(WorkstationVerificationCheck check) => check switch
    {
        WorkstationVerificationCheck.ExternalApplicationAutomationLock => "Available",
        WorkstationVerificationCheck.MeituLaunchability => "Accepted process reaches a recognised state",
        WorkstationVerificationCheck.MeituSafeStartingState => "Recognised safe state",
        WorkstationVerificationCheck.PhotoshopLaunchability => "Accepted process reaches a recognised state",
        WorkstationVerificationCheck.PhotoshopSafeStartingState => "Recognised state with no unsaved document",
        WorkstationVerificationCheck.PhotoshopColourSettings => "Accepted preset working spaces",
        _ => "Open, identify, close without saving, and restore the prior state",
    };

    private static void AddRemainingBlocked(List<WorkstationCheckResult> checks, string reason) =>
        AddMissingBlocked(checks, reason);

    private static void AddPhotoshopBlocked(List<WorkstationCheckResult> checks, string reason)
    {
        foreach (WorkstationVerificationCheck check in OrderedLiveChecks.SkipWhile(value =>
                     value != WorkstationVerificationCheck.PhotoshopSafeStartingState))
        {
            if (checks.All(existing => existing.Check != check))
                checks.Add(Blocked(check, check == WorkstationVerificationCheck.PhotoshopTestImageRoundTrip
                    ? WorkstationCheckKind.Smoke : WorkstationCheckKind.Live, reason));
        }
    }

    private static void AddAfterPhotoshopStateBlocked(List<WorkstationCheckResult> checks, string reason)
    {
        foreach (WorkstationVerificationCheck check in new[]
                 {
                     WorkstationVerificationCheck.PhotoshopColourSettings,
                     WorkstationVerificationCheck.PhotoshopTestImageRoundTrip,
                 })
        {
            if (checks.All(existing => existing.Check != check))
                checks.Add(Blocked(check, check == WorkstationVerificationCheck.PhotoshopTestImageRoundTrip
                    ? WorkstationCheckKind.Smoke : WorkstationCheckKind.Live, reason));
        }
    }

    private static void AddMissingBlocked(List<WorkstationCheckResult> checks, string reason)
    {
        foreach (WorkstationVerificationCheck check in OrderedLiveChecks)
        {
            if (checks.All(existing => existing.Check != check))
                checks.Add(Blocked(check, check == WorkstationVerificationCheck.PhotoshopTestImageRoundTrip
                    ? WorkstationCheckKind.Smoke : WorkstationCheckKind.Live, reason));
        }
    }

    private static void AddCancellation(List<WorkstationCheckResult> checks)
    {
        WorkstationVerificationCheck active = OrderedLiveChecks.FirstOrDefault(check =>
            checks.All(existing => existing.Check != check));
        if (!Enum.IsDefined(active)) active = WorkstationVerificationCheck.PhotoshopTestImageRoundTrip;
        checks.Add(WorkstationCheckResult.Failed(
            active,
            active == WorkstationVerificationCheck.PhotoshopTestImageRoundTrip
                ? WorkstationCheckKind.Smoke
                : WorkstationCheckKind.Live,
            FailureCode.Cancelled,
            ExpectedFor(active),
            "Cancelled",
            "Live application verification was cancelled; no operator process was terminated."));
    }

    private static void AddUnexpectedFailure(List<WorkstationCheckResult> checks, string detail)
    {
        WorkstationVerificationCheck active = OrderedLiveChecks.FirstOrDefault(check =>
            checks.All(existing => existing.Check != check));
        if (!Enum.IsDefined(active)) active = WorkstationVerificationCheck.PhotoshopTestImageRoundTrip;
        checks.Add(WorkstationCheckResult.Failed(
            active,
            active == WorkstationVerificationCheck.PhotoshopTestImageRoundTrip
                ? WorkstationCheckKind.Smoke
                : WorkstationCheckKind.Live,
            FailureCode.EnvironmentNotVerified,
            ExpectedFor(active),
            "Unexpected error",
            $"Live verification stopped safely: {detail}"));
    }

    private static void AddReobservedApplication(
        List<WorkstationCheckResult> checks,
        OperationResult<MeituReadiness> readiness,
        bool isPhotoshop)
    {
        _ = isPhotoshop;
        if (readiness.IsFailure)
        {
            checks.Add(Failed(WorkstationVerificationCheck.MeituLaunchability,
                WorkstationCheckKind.Live, "Certified process remains recognised", null, readiness.Failure));
            checks.Add(Blocked(WorkstationVerificationCheck.MeituSafeStartingState,
                WorkstationCheckKind.Live, "The certified Meitu process is no longer ready."));
            return;
        }

        checks.Add(WorkstationCheckResult.Passed(
            WorkstationVerificationCheck.MeituLaunchability,
            WorkstationCheckKind.Live,
            $"Certified process {readiness.Value.Target.Process.ProcessId} is still recognised",
            "The same Meitu process certified by the live run remains available."));
        checks.Add(WorkstationCheckResult.Passed(
            WorkstationVerificationCheck.MeituSafeStartingState,
            WorkstationCheckKind.Live,
            readiness.Value.State.State.ToString(),
            "Meitu remains in a positively recognised safe starting state."));
    }
}
