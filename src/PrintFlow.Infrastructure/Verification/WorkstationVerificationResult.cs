using System.Collections.Immutable;
using System.Text;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Verification;

/// <summary>
/// One verification check and what it concluded (Part A §5, §19).
/// </summary>
/// <param name="Check">The typed fact that was checked.</param>
/// <param name="Kind">Whether the fact is a signed baseline fact or current machine state.</param>
/// <param name="Outcome">Passed, Failed, or a non-blocking Advisory.</param>
/// <param name="FailureCode">Set only when <paramref name="Outcome"/> is Failed.</param>
/// <param name="Expected">The accepted fact, where it is safe to state.</param>
/// <param name="Observed">The fact as found, where it is safe to state.</param>
/// <param name="Explanation">One concise operator-facing sentence.</param>
/// <remarks>
/// <b>Expected</b> and <b>Observed</b> are short, human-comparable strings — a path, a digest, a
/// version, "1920x1080 @ 96 DPI". They are deliberately not raw evidence JSON: an operator
/// reading "Display scaling differs from the verified workstation configuration" needs the two
/// numbers, not the manifest (§19). Nothing on this record carries a secret, a customer file
/// name, or arbitrary machine inventory (§5).
/// </remarks>
public sealed record WorkstationCheckResult(
    WorkstationVerificationCheck Check,
    WorkstationCheckKind Kind,
    WorkstationCheckOutcome Outcome,
    FailureCode? FailureCode,
    string? Expected,
    string? Observed,
    string Explanation)
{
    /// <summary>The observed fact matched the accepted fact.</summary>
    public static WorkstationCheckResult Passed(
        WorkstationVerificationCheck check, WorkstationCheckKind kind, string observed, string explanation) =>
        new(check, kind, WorkstationCheckOutcome.Passed, null, observed, observed, explanation);

    /// <summary>The observed fact differed. Verification is closed and says exactly how.</summary>
    public static WorkstationCheckResult Failed(
        WorkstationVerificationCheck check,
        WorkstationCheckKind kind,
        FailureCode code,
        string? expected,
        string? observed,
        string explanation) =>
        new(check, kind, WorkstationCheckOutcome.Failed, code, expected, observed, explanation);

    /// <summary>An observation the accepted contract does not make a condition of verification.</summary>
    public static WorkstationCheckResult Advisory(
        WorkstationVerificationCheck check, WorkstationCheckKind kind, string? observed, string explanation) =>
        new(check, kind, WorkstationCheckOutcome.Advisory, null, null, observed, explanation);

    /// <summary>A prerequisite prevented this check from running.</summary>
    public static WorkstationCheckResult Blocked(
        WorkstationVerificationCheck check,
        WorkstationCheckKind kind,
        string? expected,
        string explanation) =>
        new(check, kind, WorkstationCheckOutcome.Blocked, null, expected, "(not run)", explanation);
}

/// <summary>
/// The complete factual answer to "is this the accepted production workstation, right now?"
/// (Part A §5).
/// </summary>
/// <param name="Verified">
/// True only when every non-advisory check passed. Part A left this a candidate answer that
/// nothing consumed; Part B made <c>VerifiedEnvironmentGate</c> its one consumer, so Production
/// authorisation is now exactly this flag, re-derived on every request.
/// </param>
/// <param name="Preset">The preset identity that was verified, or <c>null</c> if it was not.</param>
/// <param name="Checks">Every check that ran, in evaluation order.</param>
/// <param name="ObservedAt">When the dynamic half of this result was observed.</param>
/// <remarks>
/// Returned instead of a <c>bool</c> because the question the later EnvironmentGate has to
/// answer is not "may Production run" but "why not" (§5, §20). A boolean can be logged; it
/// cannot tell an operator that the display is at 125% scaling rather than that Photoshop was
/// upgraded.
/// <para>
/// <see cref="ObservedAt"/> exists so a caller can never mistake a stored result for a current
/// one. The dynamic checks in <see cref="Checks"/> were true at that instant and at no other
/// (§16).
/// </para>
/// </remarks>
public sealed record WorkstationVerificationResult(
    bool Verified,
    ProductionPresetRef? Preset,
    ImmutableArray<WorkstationCheckResult> Checks,
    DateTimeOffset ObservedAt)
{
    /// <summary>Optional observation history; never consulted to authorize a request.</summary>
    public ReadinessEvidenceLifecycle? Lifecycle { get; init; }

    /// <summary>The checks that closed verification.</summary>
    public IEnumerable<WorkstationCheckResult> Failures =>
        Checks.Where(c => c.Outcome == WorkstationCheckOutcome.Failed);

    /// <summary>Observations that are reported but never block.</summary>
    public IEnumerable<WorkstationCheckResult> Advisories =>
        Checks.Where(c => c.Outcome == WorkstationCheckOutcome.Advisory);

    /// <summary>Failed observations and deliberately unrun prerequisites; either closes Production.</summary>
    public IEnumerable<WorkstationCheckResult> BlockingChecks =>
        Checks.Where(c => c.Outcome is WorkstationCheckOutcome.Failed or WorkstationCheckOutcome.Blocked);

    /// <summary>The dynamic checks, which a caller must re-evaluate rather than remember (§16).</summary>
    public IEnumerable<WorkstationCheckResult> DynamicChecks =>
        Checks.Where(c => c.Kind == WorkstationCheckKind.Dynamic);

    /// <summary>The signed baseline checks.</summary>
    public IEnumerable<WorkstationCheckResult> ImmutableChecks =>
        Checks.Where(c => c.Kind == WorkstationCheckKind.Immutable);

    /// <summary>Builds a result and derives <see cref="Verified"/> from the checks themselves.</summary>
    /// <remarks>
    /// Derived rather than passed in, so no caller can construct a result that claims to be
    /// verified while carrying a failed check.
    /// </remarks>
    public static WorkstationVerificationResult From(
        ProductionPresetRef? preset,
        IEnumerable<WorkstationCheckResult> checks,
        DateTimeOffset observedAt)
    {
        ImmutableArray<WorkstationCheckResult> all = [.. checks];
        bool verified = preset is not null &&
            all.All(c => c.Outcome is not (WorkstationCheckOutcome.Failed or WorkstationCheckOutcome.Blocked)) &&
            all.Any(c => c.Check == WorkstationVerificationCheck.PresetIntegrity &&
                         c.Outcome == WorkstationCheckOutcome.Passed);

        return new WorkstationVerificationResult(verified, preset, all, observedAt);
    }

    /// <summary>
    /// A compact operator-facing summary: the verdict, then one line per failure and advisory.
    /// </summary>
    /// <remarks>
    /// Deliberately short and free of evidence dumps (§19). Passing checks are counted rather
    /// than listed, because an operator reads this to find out what is wrong.
    /// </remarks>
    public string Describe()
    {
        StringBuilder text = new();
        text.Append(Verified ? "Workstation verified" : "Workstation NOT verified");
        text.Append(" — preset ");
        text.Append(Preset is null ? "(unverified)" : $"{Preset.PresetId} {Preset.PresetVersion}");
        text.Append(", ");
        text.Append(Checks.Count(c => c.Outcome == WorkstationCheckOutcome.Passed));
        text.Append('/');
        text.Append(Checks.Count(c => c.Outcome != WorkstationCheckOutcome.Advisory));
        text.Append(" checks passed, observed ");
        text.Append(ObservedAt.ToString("u"));
        text.Append('.');

        foreach (WorkstationCheckResult failure in Failures)
        {
            text.AppendLine();
            text.Append("  FAIL ").Append(failure.Check).Append(": ").Append(failure.Explanation);
        }

        foreach (WorkstationCheckResult blocked in Checks.Where(c => c.Outcome == WorkstationCheckOutcome.Blocked))
        {
            text.AppendLine();
            text.Append("  BLOCKED ").Append(blocked.Check).Append(": ").Append(blocked.Explanation);
        }

        foreach (WorkstationCheckResult advisory in Advisories)
        {
            text.AppendLine();
            text.Append("  note ").Append(advisory.Check).Append(": ").Append(advisory.Explanation);
        }

        return text.ToString();
    }
}
