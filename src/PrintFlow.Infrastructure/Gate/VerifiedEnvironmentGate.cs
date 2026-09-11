using System.Collections.Immutable;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Verification;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Gate;

/// <summary>
/// The production-authorisation gate, decided by the accepted workstation verifier
/// (Epic 11500 Part B §4).
/// </summary>
/// <remarks>
/// Supersedes Epic 11100's <c>FoundationEnvironmentGate</c>, which refused every
/// <see cref="AdapterExecutionMode.Production"/> adapter unconditionally because nothing had yet
/// established what this workstation is. Part A built that establishment; this gate is the only
/// thing that consumes it. The historical fail-closed intent is not weakened by the change — an
/// unverified workstation is refused exactly as before — it is now refused on evidence rather
/// than on the absence of any.
/// <para>
/// <b>The division of labour.</b> The verifier establishes facts; this gate decides. It asks one
/// question of the whole workstation, not a different question per adapter: the accepted preset
/// describes one production workstation, so Production means that contract passes as a whole and
/// there is no arrangement in which Photoshop is authorised while Meitu is not (§13).
/// </para>
/// <para>
/// <b>Nothing is cached here.</b> Every Production request calls
/// <see cref="IProductionWorkstationVerifier.Verify"/> afresh, so an operator who locked the
/// screen, attached a monitor or switched to a remote session between two steps is refused on
/// the second even though the first passed — and one who put the workstation back is allowed
/// again without restarting PrintFlow (§6, §7). The verifier's own immutable half stays cached
/// inside the verifier, where the bytes it read cannot change without invalidating the hash that
/// approved them.
/// </para>
/// <para>
/// <b>Authorisation launches nothing.</b> <see cref="Verify"/> and the diagnostic
/// <see cref="Read"/> only re-observe the current certified processes; neither puts Photoshop,
/// Meitu or Maintop on the operator's screen. The separately named
/// <see cref="RunLiveChecksAsync"/> is the sole explicit readiness operation allowed to launch or
/// drive them. Per-attempt automation guards still re-check the live state immediately before an
/// operation (§16), including foreground ownership through <c>PhotoshopTargetLost</c> (§17).
/// </para>
/// </remarks>
public sealed class VerifiedEnvironmentGate :
    IWorkstationScopedEnvironmentGate,
    IInternalProductionEnvironmentGate,
    IEnvironmentDiagnostics
{
    /// <summary>The resource key shown when nothing more specific can be named.</summary>
    internal const string GeneralMessageKey = "Failure_EnvironmentNotVerified";

    /// <summary>The prefix every per-check operator message key shares.</summary>
    internal const string CheckMessageKeyPrefix = "EnvironmentCheck_";

    /// <summary>
    /// How much of one check's explanation reaches the persisted failure context.
    /// </summary>
    /// <remarks>
    /// A cap rather than a trust in the verifier's brevity: §9 forbids the failure from carrying
    /// manifest JSON, registry state or a machine inventory, and the way to keep that true is for
    /// the structure to be incapable of holding one.
    /// </remarks>
    private const int MaxExplanationLength = 200;

    /// <summary>How many failed checks are itemised before the context stops enumerating.</summary>
    private const int MaxItemisedFailures = 8;

    private readonly IProductionWorkstationVerifier _verifier;

    /// <param name="verifier">
    /// The Part A workstation verifier. Required, with no permissive fallback: a gate that could
    /// be constructed without one would be a gate that could authorise Production without
    /// evidence (§14).
    /// </param>
    public VerifiedEnvironmentGate(IProductionWorkstationVerifier verifier)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        _verifier = verifier;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="AdapterExecutionMode.Fake"/> is allowed whatever the workstation looks like, and
    /// the verifier is not even consulted for it (§11). A wrong Photoshop digest or a second
    /// monitor must not stop development, QA, or an operator troubleshooting the very workstation
    /// that is failing verification.
    /// </remarks>
    public OperationResult<Unit> Verify(AdapterExecutionMode mode) => mode switch
    {
        AdapterExecutionMode.Fake => OperationResult.Ok(),
        AdapterExecutionMode.Production => AuthoriseProduction(),

        // An execution mode this gate does not recognise is refused, not waved through: a new
        // member of the enum arriving without a decision here is exactly the case where failing
        // open would be silent.
        _ => OperationResult.Fail<Unit>(
            FailureCode.EnvironmentNotVerified, $"Unknown adapter execution mode '{mode}'."),
    };

    /// <inheritdoc />
    public OperationResult<Unit> Verify(
        AdapterExecutionMode mode,
        IWorkstationAutomationLease workstationLease)
    {
        ArgumentNullException.ThrowIfNull(workstationLease);
        return mode switch
        {
            AdapterExecutionMode.Fake => OperationResult.Ok(),
            AdapterExecutionMode.Production => AuthoriseProduction(workstationLease),
            _ => OperationResult.Fail<Unit>(
                FailureCode.EnvironmentNotVerified, $"Unknown adapter execution mode '{mode}'."),
        };
    }

    /// <inheritdoc />
    public OperationResult<Unit> VerifyForInternalWork(AdapterExecutionMode mode) => mode switch
    {
        AdapterExecutionMode.Fake => OperationResult.Ok(),
        AdapterExecutionMode.Production => AuthoriseInternalProduction(),
        _ => OperationResult.Fail<Unit>(
            FailureCode.EnvironmentNotVerified, $"Unknown adapter execution mode '{mode}'."),
    };

    /// <inheritdoc />
    public EnvironmentReadinessReport Read()
    {
        WorkstationVerificationResult result = _verifier.Verify();

        return ToReadinessReport(result);
    }

    /// <inheritdoc />
    public async Task<EnvironmentReadinessReport> RunLiveChecksAsync(CancellationToken cancellationToken)
    {
        WorkstationVerificationResult result = await _verifier
            .RunLiveChecksAsync(cancellationToken)
            .ConfigureAwait(false);

        return ToReadinessReport(result);
    }

    /// <summary>The stable per-check resource key, for the resx and for tests to enumerate.</summary>
    internal static string MessageKeyFor(WorkstationVerificationCheck check) =>
        CheckMessageKeyPrefix + check;

    /// <summary>
    /// Projects one verification result onto the operator-facing report, naming the individual
    /// facts a screen may need by themselves (SCRUM-11118).
    /// </summary>
    /// <remarks>
    /// The two named facts are lifted here rather than picked out of <c>Checks</c> by a caller,
    /// because the verification vocabulary belongs to this layer: a shell that had to know which
    /// check states the accepted output root would be a shell that knows the check list, which is
    /// exactly the coupling Part B §11.10 forbids. Both are null when the check did not run — a
    /// passive reading states nothing about the live colour setup, and an unverified manifest
    /// states nothing about the accepted root.
    /// </remarks>
    private static EnvironmentReadinessReport ToReadinessReport(WorkstationVerificationResult result)
    {
        WorkstationCheckResult? workspaceRoot = FirstOrNull(
            result, WorkstationVerificationCheck.WorkspaceRoot);
        WorkstationCheckResult? colourSetup = FirstOrNull(
            result, WorkstationVerificationCheck.PhotoshopColourSettings);

        return new EnvironmentReadinessReport(
            result.Verified,
            result.Preset?.ToString(),
            result.ObservedAt,
            [.. result.Checks.Select(ToReport)])
        {
            // The accepted value, never the observed one: this is the root the signed preset
            // requires, which is what "the output location" means to an operator.
            AcceptedOutputRoot = workspaceRoot is null
                ? null
                : workspaceRoot.Expected ?? workspaceRoot.Observed,
            PhotoshopColourSetup = colourSetup is null ? null : ToStatus(colourSetup.Outcome),
            Lifecycle = result.Lifecycle,
        };
    }

    private static WorkstationCheckResult? FirstOrNull(
        WorkstationVerificationResult result, WorkstationVerificationCheck check) =>
        result.Checks.FirstOrDefault(candidate => candidate.Check == check);

    private OperationResult<Unit> AuthoriseProduction(
        IWorkstationAutomationLease? workstationLease = null)
    {
        WorkstationVerificationResult result = _verifier.Verify(workstationLease);

        // Authorisation reads Verified, which the result derives from its own checks and which
        // no advisory can influence. An advisory is carried into diagnostics and into the log,
        // and it changes nothing about permission (§8).
        return Authorise(result);
    }

    private OperationResult<Unit> AuthoriseInternalProduction()
    {
        WorkstationVerificationResult result = _verifier is IInternalProductionWorkstationVerifier internalVerifier
            ? internalVerifier.VerifyForInternalWork()
            : _verifier.Verify();

        return Authorise(result);
    }

    private static OperationResult<Unit> Authorise(WorkstationVerificationResult result) =>
        result.Verified
            ? OperationResult.Ok()
            : OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.EnvironmentNotVerified,
                Truncate(result.Describe(), MaxExplanationLength * MaxItemisedFailures),
                isRetryable: false,
                context: ContextFor(result),
                messageKey: PrimaryMessageKeyFor(result)));

    /// <summary>
    /// The most specific message key the failure can honestly claim (§22).
    /// </summary>
    /// <remarks>
    /// The first blocking failure in evaluation order, which is also the earliest thing wrong:
    /// a manifest that did not verify is reported as that rather than as the display mismatch it
    /// would have gone on to find had it kept reading. The general key is the fallback for a
    /// result with no itemised failure at all — a manifest so unreadable that verification could
    /// not even name a check.
    /// </remarks>
    private static string PrimaryMessageKeyFor(WorkstationVerificationResult result) =>
        result.BlockingChecks.Select(f => MessageKeyFor(f.Check)).FirstOrDefault() ?? GeneralMessageKey;

    /// <summary>
    /// A bounded structured account of the refusal (§9).
    /// </summary>
    /// <remarks>
    /// Failed check names, their failure codes, one concise capped sentence each, the observation
    /// time and the preset identity — and nothing else. Explicitly absent: the manifest, the
    /// evidence chain, registry state, a machine inventory, and any expected/observed pair, which
    /// on this record can hold an accepted path or digest and has no business being persisted on
    /// a workflow attempt. An operator who needs those reads
    /// <see cref="IEnvironmentDiagnostics"/>.
    /// </remarks>
    private static IReadOnlyDictionary<string, string> ContextFor(WorkstationVerificationResult result)
    {
        ImmutableArray<WorkstationCheckResult> failures = [.. result.BlockingChecks.Take(MaxItemisedFailures)];

        Dictionary<string, string> context = new(StringComparer.Ordinal)
        {
            ["verification.result"] = "NotVerified",
            ["verification.observedAt"] = result.ObservedAt.ToString("u"),
            ["verification.preset"] = result.Preset?.ToString() ?? "(unverified)",
            ["verification.failedChecks"] = failures.Length == 0
                ? "(none itemised)"
                : string.Join(", ", failures.Select(f => f.Check.ToString())),
        };

        foreach (WorkstationCheckResult failure in failures)
        {
            context[$"verification.failure.{failure.Check}"] =
                $"{failure.FailureCode?.ToString() ?? nameof(FailureCode.EnvironmentNotVerified)}: " +
                Truncate(failure.Explanation, MaxExplanationLength);
        }

        // Advisories are preserved so support can see them, and are labelled as advisories so
        // nobody later reads one as the reason Production is closed (§8).
        string[] advisories = [.. result.Advisories.Select(a => a.Check.ToString())];
        if (advisories.Length > 0)
        {
            context["verification.advisories"] = string.Join(", ", advisories);
        }

        return context;
    }

    private static EnvironmentCheckStatus ToStatus(WorkstationCheckOutcome outcome) => outcome switch
    {
        WorkstationCheckOutcome.Passed => EnvironmentCheckStatus.Passed,
        WorkstationCheckOutcome.Failed => EnvironmentCheckStatus.Failed,
        WorkstationCheckOutcome.Blocked => EnvironmentCheckStatus.Blocked,
        _ => EnvironmentCheckStatus.Advisory,
    };

    private static EnvironmentCheckReport ToReport(WorkstationCheckResult check) =>
        new(check.Check.ToString(),
            ToStatus(check.Outcome),
            check.Outcome != WorkstationCheckOutcome.Advisory,
            MessageKeyFor(check.Check),
            Truncate(check.Explanation, MaxExplanationLength),
            check.Expected,
            check.Observed,
            check.Kind is WorkstationCheckKind.Live or WorkstationCheckKind.Smoke
                ? EnvironmentCheckPhase.LiveApplication
                : EnvironmentCheckPhase.Automatic);

    private static string Truncate(string text, int limit) =>
        text.Length <= limit ? text : string.Concat(text.AsSpan(0, limit - 1), "…");
}
