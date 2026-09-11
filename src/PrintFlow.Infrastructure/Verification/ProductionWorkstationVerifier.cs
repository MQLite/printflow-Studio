using System.Collections.Immutable;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Verification;

/// <summary>
/// Establishes, factually, whether this machine is the accepted production workstation
/// (Epic 11500 Part A §20).
/// </summary>
/// <remarks>
/// <b>This interface authorises nothing.</b> It answers a question; it does not open a gate.
/// Epic 11500 Part B wired the answer to <c>VerifiedEnvironmentGate</c>, which is the only thing
/// in the process that consults it and the only thing that turns a result into permission. No
/// adapter can reach this type to vouch for itself, and no caller can hold a result and treat it
/// as authorisation later: the gate re-asks on every Production request.
/// </remarks>
public interface IProductionWorkstationVerifier
{
    /// <summary>
    /// Re-evaluates the workstation and returns one complete, structured, timestamped result.
    /// </summary>
    /// <remarks>
    /// Safe and meaningful to call repeatedly: the dynamic half is observed afresh every time
    /// (§16). It launches nothing (§17), writes nothing, and reads no customer content (§11).
    /// </remarks>
    WorkstationVerificationResult Verify();

    WorkstationVerificationResult Verify(IWorkstationAutomationLease? ownLease) => Verify();

    /// <summary>
    /// Runs the explicit, bounded external-application verification phase after static trust passes.
    /// </summary>
    Task<WorkstationVerificationResult> RunLiveChecksAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Narrow verifier route for production work that does not control an external application.
/// </summary>
/// <remarks>
/// Internal to the gate/verifier collaboration so a workflow caller cannot turn factual
/// verification into its own authorization surface.
/// </remarks>
internal interface IInternalProductionWorkstationVerifier
{
    WorkstationVerificationResult VerifyForInternalWork();
}

/// <summary>
/// The factual workstation verifier Epic 11500's environment gate will later consult.
/// </summary>
/// <remarks>
/// Every accepted value it compares against comes from the hash-verified preset manifest; every
/// observed value comes through <see cref="IWorkstationFactReader"/> or
/// <see cref="IWorkstationArtifactReader"/>, which between them expose exactly the machine facts
/// the preset names and no general-purpose shell, script or registry surface (§18).
/// <para>
/// <b>Caching.</b> The signed baseline — the manifest, its evidence chain, the two binaries and
/// the Action file — is read once and remembered, because those bytes cannot change under a
/// running process without also invalidating the hash that was checked. Session, display,
/// culture and workspace availability are re-read on every call and never remembered, because
/// the operator can lock the screen, attach a monitor or start a remote session between two
/// production steps (§16).
/// </para>
/// </remarks>
public sealed class ProductionWorkstationVerifier :
    IProductionWorkstationVerifier,
    IInternalProductionWorkstationVerifier
{
    private readonly string _manifestAbsolutePath;
    private readonly string _presetId;
    private readonly string _presetVersion;
    private readonly Sha256 _expectedManifestSha256;
    private readonly string _configuredWorkspaceRoot;
    private readonly IWorkstationFactReader _facts;
    private readonly IWorkstationArtifactReader _artifacts;
    private readonly TimeProvider _clock;
    private readonly IProductionLiveWorkstationVerifier? _live;
    private readonly IProductionRevalidationReader _revalidation;

    /// <summary>
    /// Set only by <see cref="ForStandardRegressionRun"/>. False for every verifier the
    /// application composes, and there is no public way to make it true.
    /// </summary>
    private readonly bool _omitProductionRevalidation;

    private readonly object _liveEvidenceSync = new();
    private WorkstationLiveEvidence? _liveEvidence;
    private long _liveEvidenceRevision;
    private WorkstationLiveEvidence? _lastSuccessfulLiveEvidence;
    private DateTimeOffset? _lastSuccessfulLiveAt;
    private DateTimeOffset? _latestAttemptAt;
    private ReadinessProbeDiagnostics? _latestProbe;

    private readonly Lazy<RootOfTrust> _rootOfTrust;
    private ImmutableArray<WorkstationCheckResult>? _baselineChecks;

    /// <param name="manifestAbsolutePath">The configured preset manifest.</param>
    /// <param name="presetId">The configured preset id.</param>
    /// <param name="presetVersion">The configured preset version.</param>
    /// <param name="expectedManifestSha256">The digest configuration says it must hash to.</param>
    /// <param name="configuredWorkspaceRoot">The workspace root this installation is configured with.</param>
    /// <param name="facts">The narrow machine-fact reader.</param>
    /// <param name="artifacts">The narrow on-disk artefact reader.</param>
    /// <param name="clock">Stamps the observation time of the dynamic half.</param>
    public ProductionWorkstationVerifier(
        string manifestAbsolutePath,
        string presetId,
        string presetVersion,
        Sha256 expectedManifestSha256,
        string configuredWorkspaceRoot,
        IWorkstationFactReader facts,
        IWorkstationArtifactReader artifacts,
        TimeProvider clock)
        : this(manifestAbsolutePath, presetId, presetVersion, expectedManifestSha256,
            configuredWorkspaceRoot, facts, artifacts, clock, live: null, revalidation: null)
    {
    }

    /// <param name="revalidation">
    /// Reads the production revalidation record. Null means the real file under the configured
    /// workspace root, which is what the application always uses; the parameter exists so the
    /// fail-closed matrix in <c>ProductionRevalidationTests</c> can present a record without
    /// owning a workspace.
    /// </param>
    internal ProductionWorkstationVerifier(
        string manifestAbsolutePath,
        string presetId,
        string presetVersion,
        Sha256 expectedManifestSha256,
        string configuredWorkspaceRoot,
        IWorkstationFactReader facts,
        IWorkstationArtifactReader artifacts,
        TimeProvider clock,
        IProductionLiveWorkstationVerifier? live,
        IProductionRevalidationReader? revalidation,
        bool omitProductionRevalidation = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestAbsolutePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(presetId);
        ArgumentException.ThrowIfNullOrWhiteSpace(presetVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredWorkspaceRoot);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(clock);

        _manifestAbsolutePath = manifestAbsolutePath;
        _presetId = presetId;
        _presetVersion = presetVersion;
        _expectedManifestSha256 = expectedManifestSha256;
        _configuredWorkspaceRoot = configuredWorkspaceRoot;
        _facts = facts;
        _artifacts = artifacts;
        _clock = clock;
        _live = live;
        _revalidation = revalidation ?? new FileProductionRevalidationReader(configuredWorkspaceRoot);
        _omitProductionRevalidation = omitProductionRevalidation;
        _rootOfTrust = new Lazy<RootOfTrust>(EstablishRootOfTrust);
    }

    /// <summary>Builds a verifier that reads this machine through the real Win32 readers.</summary>
    public static ProductionWorkstationVerifier ForWorkstation(
        string manifestAbsolutePath,
        string presetId,
        string presetVersion,
        Sha256 expectedManifestSha256,
        string configuredWorkspaceRoot,
        TimeProvider clock) =>
        new(manifestAbsolutePath,
            presetId,
            presetVersion,
            expectedManifestSha256,
            configuredWorkspaceRoot,
            new Win32WorkstationFactReader(),
            new FileSystemArtifactReader(),
            clock);

    /// <summary>Builds the full automatic-plus-live verifier used by the application.</summary>
    public static ProductionWorkstationVerifier ForWorkstation(
        string manifestAbsolutePath,
        string presetId,
        string presetVersion,
        Sha256 expectedManifestSha256,
        string configuredWorkspaceRoot,
        IWorkspace workspace,
        IWorkstationAutomationLeaseManager automationLeases,
        string evidenceDirectory,
        TimeProvider clock) =>
        Compose(manifestAbsolutePath, presetId, presetVersion, expectedManifestSha256,
            configuredWorkspaceRoot, workspace, automationLeases, evidenceDirectory, clock,
            omitProductionRevalidation: false);

    /// <summary>
    /// The same verifier with the production-revalidation check omitted, for the standard
    /// regression run whose own success is what creates the record that check reads
    /// (SCRUM-11065; SCRUM-11123 Part H).
    /// </summary>
    /// <remarks>
    /// <b>The circle this exists to break.</b> Production is closed until a revalidation record
    /// says the standard regression set passed. Recording that requires the set to have been run.
    /// Running it drives the real Production adapters, which are closed. Every link in that loop
    /// is a rule worth keeping, but the first run after any upgrade has to be able to happen.
    /// <para>
    /// <b>Omitted, not answered.</b> Nothing here supplies a revalidation record, and there is no
    /// value this class will accept as a passing one that it did not read from the workspace
    /// itself — so this cannot forge an approval, only decline to ask the one question whose
    /// answer depends on the run being composed. Everything else is evaluated exactly as the
    /// application evaluates it: preset integrity and the evidence chain it vouches for, the OS
    /// build, both accepted binaries, the Action artefact, the workspace root, the interactive
    /// session, the display topology, the UI culture, and the entire live application phase —
    /// launchability, safe starting states, Photoshop's colour settings, the test-image round
    /// trip and the shared workstation automation lease. A workstation failing any of those still fails here.
    /// </para>
    /// <para>
    /// <b>Why the application cannot use it.</b> <c>internal</c>, and this assembly grants its
    /// internals to <c>PrintFlow.Tests</c> alone. PrintFlow.App is a separate assembly with no
    /// such grant, so no composition the shipped product performs can reach this method — which
    /// is what makes "PrintFlow cannot skip its own revalidation check" a structural fact rather
    /// than a convention. <c>StandardRegressionSetTests</c> asserts it.
    /// </para>
    /// </remarks>
    internal static ProductionWorkstationVerifier ForStandardRegressionRun(
        string manifestAbsolutePath,
        string presetId,
        string presetVersion,
        Sha256 expectedManifestSha256,
        string configuredWorkspaceRoot,
        IWorkspace workspace,
        IWorkstationAutomationLeaseManager automationLeases,
        string evidenceDirectory,
        TimeProvider clock) =>
        Compose(manifestAbsolutePath, presetId, presetVersion, expectedManifestSha256,
            configuredWorkspaceRoot, workspace, automationLeases, evidenceDirectory, clock,
            omitProductionRevalidation: true);

    /// <summary>
    /// The live composition, in one place.
    /// </summary>
    /// <remarks>
    /// Shared by both factories on purpose. A regression run composed from a second copy of this
    /// would keep working while the application's own composition changed underneath it — and a
    /// regression set that silently exercises a stale composition is worse than none.
    /// </remarks>
    private static ProductionWorkstationVerifier Compose(
        string manifestAbsolutePath,
        string presetId,
        string presetVersion,
        Sha256 expectedManifestSha256,
        string configuredWorkspaceRoot,
        IWorkspace workspace,
        IWorkstationAutomationLeaseManager automationLeases,
        string evidenceDirectory,
        TimeProvider clock,
        bool omitProductionRevalidation)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(automationLeases);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceDirectory);

        IMeituAutomationFoundation meitu = MeituAutomationComposition.CreateFoundation(
            manifestAbsolutePath, expectedManifestSha256, workspace, evidenceDirectory, clock);
        IPhotoshopAutomationFoundation photoshop = PhotoshopAutomationComposition.CreateFoundation(
            manifestAbsolutePath, expectedManifestSha256, workspace, evidenceDirectory, clock);
        IProductionLiveWorkstationVerifier live = new ProductionLiveWorkstationVerifier(
            meitu,
            photoshop,
            new RotPhotoshopRuntimeFactReader(),
            automationLeases,
            workspace,
            clock);

        return new ProductionWorkstationVerifier(
            manifestAbsolutePath,
            presetId,
            presetVersion,
            expectedManifestSha256,
            configuredWorkspaceRoot,
            new Win32WorkstationFactReader(),
            new FileSystemArtifactReader(),
            clock,
            live,
            revalidation: null,
            omitProductionRevalidation);
    }

    /// <inheritdoc />
    public WorkstationVerificationResult Verify() =>
        VerifyCore(ownLease: null, requireAutomationAvailability: true);

    public WorkstationVerificationResult Verify(IWorkstationAutomationLease? ownLease) =>
        VerifyCore(ownLease, requireAutomationAvailability: true);

    WorkstationVerificationResult IInternalProductionWorkstationVerifier.VerifyForInternalWork() =>
        VerifyCore(ownLease: null, requireAutomationAvailability: false);

    private WorkstationVerificationResult VerifyCore(
        IWorkstationAutomationLease? ownLease,
        bool requireAutomationAvailability)
    {
        WorkstationVerificationResult automatic = VerifyAutomatic();
        if (_live is null || automatic.Preset is null || !automatic.Verified)
        {
            return automatic with { Lifecycle = Lifecycle(observationDeferred: false) };
        }

        WorkstationLiveEvidence? evidence;
        long evidenceRevision;
        lock (_liveEvidenceSync)
        {
            evidence = _liveEvidence;
            evidenceRevision = _liveEvidenceRevision;
        }
        WorkstationLiveVerification live = requireAutomationAvailability
            ? _live.Reobserve(_rootOfTrust.Value.Requirements!, evidence, ownLease)
            : _live.ReobserveForInternalWork(_rootOfTrust.Value.Requirements!, evidence);
        ReadinessEvidenceLifecycle lifecycle;
        lock (_liveEvidenceSync)
        {
            if (_liveEvidenceRevision != evidenceRevision)
            {
                // An old observation neither clears a newer run nor grants admission using
                // evidence a concurrent attempt invalidated. Keep its concrete failures, but
                // withdraw any passing round-trip claim until a new passive observation.
                if (live.Evidence is not null)
                {
                    live = live with
                    {
                        Checks = [.. live.Checks.Where(check =>
                                check.Check != WorkstationVerificationCheck.PhotoshopTestImageRoundTrip),
                            WorkstationCheckResult.Blocked(
                                WorkstationVerificationCheck.PhotoshopTestImageRoundTrip,
                                WorkstationCheckKind.Smoke, "Current live evidence",
                                "Live evidence changed during this observation. A fresh observation is required before admission.")],
                    };
                }
            }
            else if (live.Evidence is null && _liveEvidence is not null)
            {
                _liveEvidence = null;
                _liveEvidenceRevision++;
            }
            lifecycle = Lifecycle(live.ObservationDeferred);
        }

        return WorkstationVerificationResult.From(
            automatic.Preset, [.. automatic.Checks, .. live.Checks], automatic.ObservedAt) with
        {
            Lifecycle = lifecycle,
        };
    }

    /// <inheritdoc />
    public async Task<WorkstationVerificationResult> RunLiveChecksAsync(
        CancellationToken cancellationToken)
    {
        DateTimeOffset attemptedAt = _clock.GetUtcNow();
        WorkstationVerificationResult automatic = VerifyAutomatic();
        if (_live is null)
        {
            return automatic;
        }

        WorkstationLiveVerification live;
        if (automatic.Preset is null || !automatic.Verified)
        {
            live = new WorkstationLiveVerification(
                ProductionLiveWorkstationVerifier.BlockedChecks(
                    "Automatic workstation checks must pass before applications are launched."), null);
        }
        else
        {
            live = await _live.RunAsync(_rootOfTrust.Value.Requirements!, cancellationToken)
                .ConfigureAwait(false);
        }

        ReadinessEvidenceLifecycle lifecycle;
        lock (_liveEvidenceSync)
        {
            _liveEvidence = live.Evidence;
            _liveEvidenceRevision++;
            _latestAttemptAt = attemptedAt;
            _latestProbe = live.Probe;
            if (live.Evidence is not null)
            {
                _lastSuccessfulLiveEvidence = live.Evidence;
                _lastSuccessfulLiveAt = _clock.GetUtcNow();
            }
            lifecycle = Lifecycle(observationDeferred: false);
        }
        return WorkstationVerificationResult.From(
            automatic.Preset,
            [.. automatic.Checks, .. live.Checks],
            _clock.GetUtcNow()) with
        {
            Lifecycle = lifecycle,
        };
    }

    private ReadinessEvidenceLifecycle Lifecycle(bool observationDeferred)
    {
        lock (_liveEvidenceSync)
        {
            var meitu = _lastSuccessfulLiveEvidence?.Meitu.Target.Process;
            var photoshop = _lastSuccessfulLiveEvidence?.Photoshop.Target.Process;
            return new ReadinessEvidenceLifecycle(
                _lastSuccessfulLiveAt,
                meitu is not null ? new(meitu.ProcessId, meitu.ExecutablePath, meitu.StartedUtc) : null,
                photoshop is not null ? new(photoshop.ProcessId, photoshop.ExecutablePath, photoshop.StartedUtc) : null,
                _liveEvidence is not null, observationDeferred, _latestAttemptAt, _latestProbe);
        }
    }

    private WorkstationVerificationResult VerifyAutomatic()
    {
        RootOfTrust root = _rootOfTrust.Value;
        DateTimeOffset observedAt = _clock.GetUtcNow();

        // Nothing past the root of trust is interpreted until the manifest and every evidence
        // entry it vouches for have verified. A workstation requirement read out of a document
        // whose integrity is in question is not a requirement (§4).
        if (root.Requirements is not { } requirements)
        {
            return WorkstationVerificationResult.From(null, root.Checks, observedAt);
        }

        ImmutableArray<WorkstationCheckResult> baseline =
            _baselineChecks ??= EvaluateSignedBaseline(requirements);

        List<WorkstationCheckResult> checks = [.. root.Checks, .. baseline, .. EvaluateCurrentState(requirements)];

        return WorkstationVerificationResult.From(requirements.Preset, checks, observedAt);
    }

    private readonly record struct RootOfTrust(
        WorkstationRequirements? Requirements, ImmutableArray<WorkstationCheckResult> Checks);

    /// <summary>Steps 1-4 of §4: resolve, hash, load, then verify every evidence entry.</summary>
    private RootOfTrust EstablishRootOfTrust()
    {
        OperationResult<WorkstationRequirements> read = PresetWorkstationRequirements.Read(
            _manifestAbsolutePath, _presetId, _presetVersion, _expectedManifestSha256);

        if (read.IsFailure)
        {
            return new RootOfTrust(null,
            [
                WorkstationCheckResult.Failed(
                    WorkstationVerificationCheck.PresetIntegrity,
                    WorkstationCheckKind.Immutable,
                    read.Failure.Code,
                    _expectedManifestSha256.ToString(),
                    null,
                    read.Failure.TechnicalDetail),
            ]);
        }

        WorkstationRequirements requirements = read.Value;

        WorkstationCheckResult integrity = WorkstationCheckResult.Passed(
            WorkstationVerificationCheck.PresetIntegrity,
            WorkstationCheckKind.Immutable,
            requirements.Preset.ToString(),
            $"The configured preset manifest hashes to the configured {_expectedManifestSha256.ShortForm}.");

        (WorkstationCheckResult evidence, WorkstationCheckResult advisory) = VerifyEvidence(requirements);

        // Evidence that does not verify closes the chain here. The requirements were parsed to
        // reach the integrity list at all, but they are not handed on: an evidence file the
        // preset claims and cannot produce is tampering or loss, not a missing capability.
        return evidence.Outcome == WorkstationCheckOutcome.Failed
            ? new RootOfTrust(null, [integrity, evidence])
            : new RootOfTrust(requirements, [integrity, evidence, advisory]);
    }

    /// <summary>
    /// Hashes every <c>sourceManifestIntegrity</c> entry, and separately reports the read-only
    /// attribute as an advisory (§4, §15).
    /// </summary>
    private (WorkstationCheckResult Integrity, WorkstationCheckResult ReadOnlyAdvisory) VerifyEvidence(
        WorkstationRequirements requirements)
    {
        List<string> missing = [];
        List<string> mismatched = [];
        List<string> writable = [];

        foreach (AcceptedEvidence entry in requirements.Evidence)
        {
            FileIdentityFacts file = _artifacts.ReadFile(entry.Path);
            if (!file.Exists || file.Sha256 is not { } digest)
            {
                missing.Add(entry.Path);
                continue;
            }

            if (!digest.Equals(entry.Sha256))
            {
                mismatched.Add($"{entry.Path} (expected {entry.Sha256.ShortForm}, computed {digest.ShortForm})");
                continue;
            }

            if (!file.IsReadOnly)
            {
                writable.Add(entry.Path);
            }
        }

        int total = requirements.Evidence.Length;
        string observed = $"{total - missing.Count - mismatched.Count}/{total} entries verified";

        WorkstationCheckResult integrity = missing.Count == 0 && mismatched.Count == 0
            ? WorkstationCheckResult.Passed(
                WorkstationVerificationCheck.EvidenceIntegrity,
                WorkstationCheckKind.Immutable,
                observed,
                $"All {total} evidence files the preset vouches for are present and hash exactly.")
            : WorkstationCheckResult.Failed(
                WorkstationVerificationCheck.EvidenceIntegrity,
                WorkstationCheckKind.Immutable,
                missing.Count > 0 ? FailureCode.EnvironmentNotVerified : FailureCode.PresetHashMismatch,
                $"{total} entries present and matching",
                observed,
                Describe(missing, mismatched));

        // §15: the accepted protocol names SHA-256 as the authority and the filesystem attribute
        // as a convenience applied after hashing. An absent attribute on a file whose bytes hash
        // exactly is a policy observation, not a verification failure — and this slice reports it
        // without changing a single attribute.
        WorkstationCheckResult advisory = WorkstationCheckResult.Advisory(
            WorkstationVerificationCheck.FilesystemReadOnlyPolicyAdvisory,
            WorkstationCheckKind.Immutable,
            $"{writable.Count}/{total} integrity-referenced files are not marked read-only",
            writable.Count == 0
                ? "Every integrity-referenced evidence file also carries the read-only attribute."
                : $"{writable.Count} of {total} integrity-referenced evidence files no longer carry the " +
                  "read-only attribute. SHA-256 remains the accepted authority and every digest matched, " +
                  "so this does not close verification.");

        return (integrity, advisory);
    }

    private static string Describe(List<string> missing, List<string> mismatched)
    {
        if (missing.Count > 0 && mismatched.Count > 0)
        {
            return $"{missing.Count} evidence files the preset vouches for are missing and " +
                   $"{mismatched.Count} no longer hash to their accepted digest. First mismatch: {mismatched[0]}.";
        }

        return missing.Count > 0
            ? $"{missing.Count} evidence files the preset vouches for are missing or unreadable. " +
              $"First: {missing[0]}."
            : $"{mismatched.Count} evidence files no longer hash to their accepted digest. " +
              $"First: {mismatched[0]}.";
    }

    /// <summary>The immutable half: OS identity and the three signed artefacts (§16).</summary>
    private ImmutableArray<WorkstationCheckResult> EvaluateSignedBaseline(WorkstationRequirements requirements) =>
    [
        VerifyOperatingSystem(requirements.OperatingSystem),
        VerifyExecutable(
            WorkstationVerificationCheck.MeituExecutable,
            requirements.Meitu,
            FailureCode.MeituNotInstalled,
            "Meitu"),
        VerifyExecutable(
            WorkstationVerificationCheck.PhotoshopExecutable,
            requirements.Photoshop,
            FailureCode.PhotoshopNotInstalled,
            "Photoshop"),
        VerifyActionArtifact(requirements.PhotoshopAction),
    ];

    /// <summary>The dynamic half: re-observed on every call and never cached (§16).</summary>
    private ImmutableArray<WorkstationCheckResult> EvaluateCurrentState(WorkstationRequirements requirements) =>
    [
        VerifyWorkspaceRoot(requirements.Workspace),
        VerifyInteractiveSession(requirements.Session),
        VerifyDisplay(requirements.Display),
        VerifyUiCulture(requirements.OperatingSystem, requirements.Meitu),
        DescribeExternalApplicationUiLanguage(requirements.Meitu, requirements.Photoshop),

        // Dynamic, and last, because it is the only check that asks about the application rather
        // than the machine: has this PrintFlow build, on this preset, on this Windows build,
        // against these accepted binaries, actually been revalidated? The record is re-read on
        // every call for the same reason the display is — an operator who records a revalidation
        // while PrintFlow is running returns to Production without restarting it, and one whose
        // record is removed is refused on the next request (SCRUM-11123 Part H).
        //
        // .. omitted entirely, and only for the standard regression run — see
        // ForStandardRegressionRun. Omitted rather than answered: there is no value this class
        // will accept as a passing record that it did not read from the workspace itself.
        .. _omitProductionRevalidation
            ? ImmutableArray<WorkstationCheckResult>.Empty
            : [ProductionRevalidationEvaluator.Evaluate(
                _revalidation.Read(),
                requirements,
                _expectedManifestSha256,
                _facts.ReadOperatingSystem().Build,
                ProductionRevalidationEvaluator.RunningProductVersion,
                // Read here rather than inside the evaluator, which stays free of file access.
                // Re-read on every call like the record itself: an operator who reinstalls
                // PrintFlow under a running application must not keep the approval either.
                ProductBuildIdentity.Running())],
    ];

    /// <summary>
    /// Verifies only the OS facts the accepted preset names (§7).
    /// </summary>
    /// <remarks>
    /// Edition, version, build and architecture, and nothing else. Patch level, installed
    /// updates, hostname, hardware and installed software are all unread: they are not part of
    /// the accepted baseline, and failing on one would be this code deciding what the shop
    /// accepted. Drift in what <i>is</i> named fails closed and states both facts — an OS
    /// upgrade is never silently absorbed.
    /// </remarks>
    private WorkstationCheckResult VerifyOperatingSystem(AcceptedOperatingSystem accepted)
    {
        OperatingSystemFacts observed = _facts.ReadOperatingSystem();

        string expectedText = $"{accepted.Edition} {accepted.Version} (build {accepted.Build}, {accepted.Architecture})";
        string observedText = $"{observed.Edition} {observed.Version} (build {observed.Build}, {observed.Architecture})";

        bool matches =
            string.Equals(observed.Edition, accepted.Edition, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(observed.Version, accepted.Version, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(observed.Build, accepted.Build, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(observed.Architecture, accepted.Architecture, StringComparison.OrdinalIgnoreCase);

        return matches
            ? WorkstationCheckResult.Passed(
                WorkstationVerificationCheck.OperatingSystem,
                WorkstationCheckKind.Immutable,
                observedText,
                "The operating system matches the accepted workstation baseline.")
            : WorkstationCheckResult.Failed(
                WorkstationVerificationCheck.OperatingSystem,
                WorkstationCheckKind.Immutable,
                FailureCode.EnvironmentNotVerified,
                expectedText,
                observedText,
                "The operating system differs from the accepted workstation baseline. An upgrade " +
                "requires preset revalidation before production automation resumes.");
    }

    /// <summary>
    /// Verifies an accepted external-application binary from its bytes alone (§12, §13, §17).
    /// </summary>
    /// <remarks>
    /// The application is never started. Hashing a file does not require the program that owns
    /// it to be running, and launching Meitu or Photoshop to establish a static fact would put
    /// two applications on the operator's screen every time the environment was checked.
    /// </remarks>
    private WorkstationCheckResult VerifyExecutable(
        WorkstationVerificationCheck check,
        AcceptedExecutable accepted,
        FailureCode notInstalled,
        string application)
    {
        FileIdentityFacts observed = _artifacts.ReadFile(accepted.Path);

        if (!observed.Exists)
        {
            return WorkstationCheckResult.Failed(
                check, WorkstationCheckKind.Immutable, notInstalled, accepted.Path, "(absent)",
                $"The accepted {application} executable is not present at its accepted path.");
        }

        if (observed.Sha256 is not { } digest)
        {
            return WorkstationCheckResult.Failed(
                check, WorkstationCheckKind.Immutable, notInstalled, accepted.Sha256.ToString(), "(unreadable)",
                $"The {application} executable at its accepted path could not be read, so its " +
                "identity cannot be established.");
        }

        // Versions before the digest, deliberately: an operator reading "reports 21.0, accepted
        // 20.0" learns that Photoshop was upgraded, where a bare hash difference only says the
        // bytes changed. Each version fact is checked only when the preset records it.
        if (accepted.ProductVersion is { } expectedProduct &&
            !string.Equals(observed.ProductVersion, expectedProduct, StringComparison.Ordinal))
        {
            return WorkstationCheckResult.Failed(
                check, WorkstationCheckKind.Immutable, notInstalled,
                expectedProduct, observed.ProductVersion ?? "(unreadable)",
                $"The {application} executable reports a product version other than the accepted one. " +
                "An upgrade requires preset revalidation before production automation resumes.");
        }

        if (accepted.FileVersion is { } expectedFile &&
            !string.Equals(observed.FileVersion, expectedFile, StringComparison.Ordinal))
        {
            return WorkstationCheckResult.Failed(
                check, WorkstationCheckKind.Immutable, notInstalled,
                expectedFile, observed.FileVersion ?? "(unreadable)",
                $"The {application} executable reports a file version other than the accepted one. " +
                "An upgrade requires preset revalidation before production automation resumes.");
        }

        return digest.Equals(accepted.Sha256)
            ? WorkstationCheckResult.Passed(
                check, WorkstationCheckKind.Immutable,
                $"{accepted.Path} ({digest.ShortForm})",
                $"The {application} executable matches the accepted workstation baseline exactly.")
            : WorkstationCheckResult.Failed(
                check, WorkstationCheckKind.Immutable, notInstalled,
                accepted.Sha256.ToString(), digest.ToString(),
                $"The {application} executable does not match the accepted workstation baseline. " +
                "A changed binary requires preset revalidation; it is never absorbed silently.");
    }

    /// <summary>
    /// Verifies the canonical Action file on disk, and only on disk (§14).
    /// </summary>
    /// <remarks>
    /// The set is not loaded, reloaded or opened in Photoshop. Whether the palette Photoshop has
    /// in memory still matches remains the Epic 11400 operation-time guard, checked immediately
    /// before an Action runs; this check answers the different question of whether the artefact
    /// the shop signed is still the artefact on the disk.
    /// </remarks>
    private WorkstationCheckResult VerifyActionArtifact(AcceptedActionArtifact accepted)
    {
        const WorkstationVerificationCheck check = WorkstationVerificationCheck.PhotoshopActionArtifact;
        FileIdentityFacts observed = _artifacts.ReadFile(accepted.Path);

        if (!observed.Exists || observed.Sha256 is not { } digest)
        {
            return WorkstationCheckResult.Failed(
                check, WorkstationCheckKind.Immutable, FailureCode.EnvironmentNotVerified,
                accepted.Path, observed.Exists ? "(unreadable)" : "(absent)",
                $"The canonical '{accepted.SetName}' Action file is not present and readable at its " +
                "accepted path.");
        }

        if (observed.Bytes != accepted.Bytes)
        {
            return WorkstationCheckResult.Failed(
                check, WorkstationCheckKind.Immutable, FailureCode.EnvironmentNotVerified,
                $"{accepted.Bytes} bytes", $"{observed.Bytes} bytes",
                $"The canonical '{accepted.SetName}' Action file is not the accepted size.");
        }

        return digest.Equals(accepted.Sha256)
            ? WorkstationCheckResult.Passed(
                check, WorkstationCheckKind.Immutable,
                $"{accepted.Path} ({digest.ShortForm})",
                $"The canonical '{accepted.SetName}' Action file matches its accepted SHA-256.")
            : WorkstationCheckResult.Failed(
                check, WorkstationCheckKind.Immutable, FailureCode.PresetHashMismatch,
                accepted.Sha256.ToString(), digest.ToString(),
                $"The canonical '{accepted.SetName}' Action file no longer matches its accepted " +
                "SHA-256. Action-driven automation stays blocked until the preset is revalidated.");
    }

    /// <summary>
    /// Verifies the accepted output root is the configured one and is addressable (§11).
    /// </summary>
    /// <remarks>
    /// Nothing inside is enumerated, read, moved or deleted: this establishes that the contract's
    /// root exists and is what it claims to be, not what a customer keeps in it. No cleanup runs
    /// and <c>CleanupWorking</c> is not reachable from here.
    /// </remarks>
    private WorkstationCheckResult VerifyWorkspaceRoot(AcceptedWorkspace accepted)
    {
        const WorkstationVerificationCheck check = WorkstationVerificationCheck.WorkspaceRoot;

        // The configured root and the accepted root are two separate claims and are compared as
        // such. An installation pointed somewhere else has not verified this workstation, however
        // healthy the directory it was pointed at happens to be.
        if (!PathsAgree(_configuredWorkspaceRoot, accepted.Root))
        {
            return WorkstationCheckResult.Failed(
                check, WorkstationCheckKind.Dynamic, FailureCode.EnvironmentNotVerified,
                accepted.Root, _configuredWorkspaceRoot,
                "The configured workspace root is not the root the accepted preset requires.");
        }

        DirectoryFacts observed = _artifacts.ReadDirectory(accepted.Root);

        if (observed.ExistsAsFile)
        {
            return WorkstationCheckResult.Failed(
                check, WorkstationCheckKind.Dynamic, FailureCode.WorkspaceError,
                accepted.Root, "(a file)",
                "The accepted workspace root is a file, not a directory.");
        }

        if (!observed.ExistsAsDirectory)
        {
            return WorkstationCheckResult.Failed(
                check, WorkstationCheckKind.Dynamic, FailureCode.WorkspaceError,
                accepted.Root, "(absent)",
                "The accepted workspace root does not exist.");
        }

        if (observed.IsReparsePoint)
        {
            return WorkstationCheckResult.Failed(
                check, WorkstationCheckKind.Dynamic, FailureCode.WorkspaceError,
                accepted.Root, "(a junction, symlink or substituted path)",
                "The accepted workspace root is redirected elsewhere, so the accepted path and the " +
                "storage it reaches are no longer the same claim.");
        }

        if (accepted.FileSystem is { } expectedFileSystem && observed.FileSystem is { } actualFileSystem &&
            !string.Equals(expectedFileSystem, actualFileSystem, StringComparison.OrdinalIgnoreCase))
        {
            return WorkstationCheckResult.Failed(
                check, WorkstationCheckKind.Dynamic, FailureCode.WorkspaceError,
                expectedFileSystem, actualFileSystem,
                "The workspace root is on a file system other than the accepted one.");
        }

        return WorkstationCheckResult.Passed(
            check, WorkstationCheckKind.Dynamic,
            observed.ResolvedFullPath ?? accepted.Root,
            "The accepted workspace root is present, is a real directory, and is addressable.");
    }

    /// <summary>
    /// Verifies a real interactive local-console desktop is available (§8).
    /// </summary>
    /// <remarks>
    /// <b>Readiness, not ownership.</b> This asks whether a person is signed in at a usable
    /// desktop — never which application currently holds the foreground. Foreground ownership
    /// stays an operation-time guard: Epic 11400 established that another application owning the
    /// foreground at a guarded input yields <see cref="FailureCode.PhotoshopTargetLost"/> with no
    /// input sent, and nothing here weakens that or fights for focus on its own.
    /// <para>
    /// The secure desktop is called out separately because it is the case that most looks like
    /// readiness and is not: a locked workstation or an open UAC prompt has a session, a
    /// signed-in user and a display, and can receive nothing.
    /// </para>
    /// </remarks>
    private WorkstationCheckResult VerifyInteractiveSession(AcceptedSession accepted)
    {
        const WorkstationVerificationCheck check = WorkstationVerificationCheck.InteractiveSession;
        InteractiveSessionFacts observed = _facts.ReadInteractiveSession();

        string observedText =
            $"session {observed.SessionId} '{observed.SessionName ?? "(unnamed)"}', " +
            $"desktop '{observed.InputDesktopName ?? "(unavailable)"}'" +
            (observed.IsRemoteSession ? ", remote" : string.Empty);

        if (!observed.UserInteractive || observed.SessionId == 0)
        {
            return WorkstationCheckResult.Failed(
                check, WorkstationCheckKind.Dynamic, FailureCode.EnvironmentNotVerified,
                accepted.AllowedSession, observedText,
                "No supported interactive desktop session is available; this process is running in a " +
                "service or otherwise non-interactive session.");
        }

        if (observed.IsRemoteSession && !accepted.RemoteAutomationAllowed)
        {
            return WorkstationCheckResult.Failed(
                check, WorkstationCheckKind.Dynamic, FailureCode.EnvironmentNotVerified,
                accepted.AllowedSession, observedText,
                "This is a remote session, and the accepted workstation contract allows production " +
                "automation only from the local console.");
        }

        if (observed.InputDesktopName is null)
        {
            return WorkstationCheckResult.Failed(
                check, WorkstationCheckKind.Dynamic, FailureCode.EnvironmentNotVerified,
                DefaultDesktop, "(unavailable)",
                "The user desktop is not available to this process, so no supported interactive " +
                "desktop session exists.");
        }

        if (!string.Equals(observed.InputDesktopName, DefaultDesktop, StringComparison.OrdinalIgnoreCase))
        {
            return WorkstationCheckResult.Failed(
                check, WorkstationCheckKind.Dynamic, FailureCode.EnvironmentNotVerified,
                DefaultDesktop, observed.InputDesktopName,
                "A secure desktop — a lock screen or an elevation prompt — currently owns input. " +
                "That is not ordinary desktop readiness and is never treated as such.");
        }

        return WorkstationCheckResult.Passed(
            check, WorkstationCheckKind.Dynamic, observedText,
            "A supported interactive local-console desktop session is available.");
    }

    /// <summary>
    /// Verifies the current display topology against the accepted contract (§9).
    /// </summary>
    /// <remarks>
    /// The reason this check exists is that the accepted workstation baseline is signed at one
    /// specific geometry and scaling, and the external-application evidence was captured there.
    /// It is emphatically <b>not</b> a step towards coordinate automation: no coordinate is
    /// computed, stored or sent anywhere in this slice, and the input vocabulary Epic 11400
    /// established — named keystrokes and messages to identified control handles — remains the
    /// only one there is.
    /// <para>
    /// The accepted contract permits a virtual display adapter to stay installed while requiring
    /// the single-active-display topology, so the adapter inventory is not read and the active
    /// count is.
    /// </para>
    /// </remarks>
    private WorkstationCheckResult VerifyDisplay(AcceptedDisplay accepted)
    {
        const WorkstationVerificationCheck check = WorkstationVerificationCheck.DisplayConfiguration;
        DisplayFacts observed = _facts.ReadDisplay();

        string expectedText =
            $"{accepted.ActiveDisplayCount} display, {accepted.MonitorBounds}, work area " +
            $"{accepted.WorkArea}, {accepted.SystemDpi} DPI ({accepted.ScalePercent}%)";
        string observedText =
            $"{observed.ActiveDisplayCount} display(s), {DescribeRectangle(observed.MonitorBounds)}, work area " +
            $"{DescribeRectangle(observed.WorkArea)}, {observed.SystemDpi} DPI ({observed.ScalePercent}%)";

        if (observed.ActiveDisplayCount != accepted.ActiveDisplayCount)
        {
            return WorkstationCheckResult.Failed(
                check, WorkstationCheckKind.Dynamic, FailureCode.EnvironmentNotVerified,
                expectedText, observedText,
                "The display topology differs from the verified workstation configuration.");
        }

        if (observed.SystemDpi != accepted.SystemDpi)
        {
            return WorkstationCheckResult.Failed(
                check, WorkstationCheckKind.Dynamic, FailureCode.EnvironmentNotVerified,
                $"{accepted.SystemDpi} DPI ({accepted.ScalePercent}%)",
                $"{observed.SystemDpi} DPI ({observed.ScalePercent}%)",
                "Display scaling differs from the verified workstation configuration.");
        }

        if (observed.MonitorBounds != accepted.MonitorBounds || observed.WorkArea != accepted.WorkArea)
        {
            return WorkstationCheckResult.Failed(
                check, WorkstationCheckKind.Dynamic, FailureCode.EnvironmentNotVerified,
                expectedText, observedText,
                "The display geometry differs from the verified workstation configuration.");
        }

        // The preset names the primary display in its short form; Win32 reports the device path.
        // Compared on the trailing name so the two spellings of the same device agree, rather
        // than by rewriting either side's vocabulary.
        if (!DeviceNamesAgree(observed.PrimaryDeviceName, accepted.PrimaryDisplay))
        {
            return WorkstationCheckResult.Failed(
                check, WorkstationCheckKind.Dynamic, FailureCode.EnvironmentNotVerified,
                accepted.PrimaryDisplay, observed.PrimaryDeviceName ?? "(unreadable)",
                "The primary display is not the one the verified workstation configuration names.");
        }

        return WorkstationCheckResult.Passed(
            check, WorkstationCheckKind.Dynamic, observedText,
            "The display matches the verified workstation configuration.");
    }

    /// <summary>
    /// Verifies the Windows user UI language the accepted external-app evidence was captured
    /// under (§10).
    /// </summary>
    /// <remarks>
    /// Three different languages get confused easily, so this reads exactly one of them. The
    /// <i>user</i> UI language is what Meitu's window titles and feature labels follow, and the
    /// accepted Meitu evidence records those surfaces in Simplified Chinese. The <i>system</i>
    /// UI language — the language Windows was installed in — differs from it on this workstation
    /// and is not part of the accepted baseline, so it is reported and never failed on. And
    /// PrintFlow's own UI locale is a third thing entirely: passing en-US and zh-CN visual QA
    /// says what PrintFlow renders, not what the applications it drives render, and the withdrawn
    /// Epic 11400 en-US limitation is not reintroduced here in either direction.
    /// </remarks>
    private WorkstationCheckResult VerifyUiCulture(AcceptedOperatingSystem accepted, AcceptedExecutable meitu)
    {
        const WorkstationVerificationCheck check = WorkstationVerificationCheck.UiCulture;
        UiCultureFacts observed = _facts.ReadUiCulture();

        string observedText =
            $"user UI language {observed.UserUiCulture ?? "(unreadable)"} " +
            $"(system UI language {observed.SystemUiCulture ?? "(unreadable)"})";

        if (observed.UserUiCulture is not { } user)
        {
            return WorkstationCheckResult.Failed(
                check, WorkstationCheckKind.Dynamic, FailureCode.EnvironmentNotVerified,
                accepted.UiCulture, "(unreadable)",
                "The Windows user UI language could not be read, so the localised surfaces the " +
                "accepted external-application evidence relies on cannot be confirmed.");
        }

        if (!string.Equals(user, accepted.UiCulture, StringComparison.OrdinalIgnoreCase))
        {
            return WorkstationCheckResult.Failed(
                check, WorkstationCheckKind.Dynamic, FailureCode.EnvironmentNotVerified,
                accepted.UiCulture, user,
                "The Windows user UI language differs from the accepted workstation culture, so the " +
                "external applications would not present the localised surfaces the accepted evidence " +
                "was captured from.");
        }

        // Meitu follows the Windows user UI language, so the preset's own meituContract.uiLanguage
        // has to agree with the workstation culture or the accepted contract contradicts itself.
        if (meitu.UiLanguage is { } meituLanguage &&
            !string.Equals(meituLanguage, accepted.UiCulture, StringComparison.OrdinalIgnoreCase))
        {
            return WorkstationCheckResult.Failed(
                check, WorkstationCheckKind.Dynamic, FailureCode.EnvironmentNotVerified,
                $"{accepted.UiCulture} (workstation) = {meituLanguage} (Meitu)",
                user,
                "The accepted preset records a Meitu interface language that differs from the accepted " +
                "workstation culture, so which localised surfaces apply cannot be established.");
        }

        return WorkstationCheckResult.Passed(
            check, WorkstationCheckKind.Dynamic, observedText,
            "The Windows user UI language matches the accepted workstation culture.");
    }

    /// <summary>
    /// Reports what the preset records about each external application's own interface language
    /// (§10).
    /// </summary>
    /// <remarks>
    /// An advisory rather than a check, because Photoshop's interface language is a setting
    /// inside Photoshop, and the only way to read it is to start Photoshop — which §17 rules out
    /// for a static fact. Recording it as an unverifiable advisory is the honest position, and
    /// it is not a contract gap: the accepted <c>photoshopContract.uiContract</c> recognises
    /// Photoshop by window <i>class</i> precisely so recognition does not move when the UI
    /// language does, and the operation-time Photoshop guard confirms the surfaces it actually
    /// uses at the moment it uses them.
    /// </remarks>
    private static WorkstationCheckResult DescribeExternalApplicationUiLanguage(
        AcceptedExecutable meitu, AcceptedExecutable photoshop) =>
        WorkstationCheckResult.Advisory(
            WorkstationVerificationCheck.ExternalApplicationUiLanguage,
            WorkstationCheckKind.Dynamic,
            $"Meitu {meitu.UiLanguage ?? "(unrecorded)"}, Photoshop {photoshop.UiLanguage ?? "(unrecorded)"}",
            "The accepted external-application interface languages are recorded by the preset. " +
            "Photoshop's own interface language is confirmed by the operation-time guard rather than " +
            "statically, because reading it would require starting Photoshop.");

    private const string DefaultDesktop = "Default";

    private static string DescribeRectangle(DisplayRectangle? rectangle) =>
        rectangle?.ToString() ?? "(unreadable)";

    /// <summary>
    /// Matches <c>DISPLAY1</c> against <c>\\.\DISPLAY1</c>: the same device, written the two
    /// ways the preset and Win32 each write it.
    /// </summary>
    private static bool DeviceNamesAgree(string? observed, string accepted)
    {
        if (observed is null)
        {
            return false;
        }

        int lastSeparator = observed.LastIndexOf('\\');
        string trailing = lastSeparator < 0 ? observed : observed[(lastSeparator + 1)..];

        return string.Equals(trailing, accepted, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Compares two configured paths as paths — case-insensitively and ignoring a trailing
    /// separator — without touching the file system.
    /// </summary>
    private static bool PathsAgree(string left, string right) =>
        string.Equals(
            left.TrimEnd('\\', '/'),
            right.TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);
}
