using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.Startup;

/// <summary>Where in the fixed startup sequence something went wrong (Epic 11100 Part 3C1 §4).</summary>
public enum StartupStage
{
    /// <summary>Claiming the single-instance guard.</summary>
    SingleInstanceGuard,

    /// <summary>Reading <c>appsettings.json</c>.</summary>
    Configuration,

    /// <summary>Resolving and creating the application-owned root directories.</summary>
    WorkspaceRoot,

    /// <summary>Opening SQLite and applying migrations.</summary>
    Database,

    /// <summary>Building the service graph, including the fail-closed adapter choice.</summary>
    Composition,

    /// <summary>Running <see cref="IStartupRecoveryService.RecoverAsync"/>.</summary>
    Recovery,
}

/// <summary>Why startup stopped. English technical detail; never shown raw to the operator.</summary>
public sealed record StartupFailure(StartupStage Stage, FailureCode? Code, string TechnicalDetail)
{
    public override string ToString() =>
        Code is null ? $"{Stage}: {TechnicalDetail}" : $"{Stage} ({Code}): {TechnicalDetail}";
}

/// <summary>
/// What startup did, in the smallest shape the shell and the later Home UI actually need
/// (Epic 11100 Part 3C1 §13).
/// </summary>
/// <remarks>
/// Deliberately not a diagnostics framework: it is a value, produced once, that answers
/// "may this process proceed?" and "what did recovery change?". The full
/// <see cref="StartupRecoveryReport"/> is retained rather than flattened, so nothing recovery
/// recorded is silently discarded before a real diagnostics surface exists (Part 3C1 §6, §14).
/// </remarks>
public sealed record StartupStatus
{
    private StartupStatus()
    {
    }

    /// <summary>True only for the process that owns the single-instance guard.</summary>
    public bool IsPrimaryInstance { get; private init; }

    /// <summary>Whether the signed workstation preset verified against its expected SHA-256.</summary>
    public bool PresetVerified { get; private init; }

    /// <summary>True once one recovery pass has completed. Never true for a second instance.</summary>
    public bool RecoveryExecuted { get; private init; }

    /// <summary>Everything that pass did, or null when it did not run.</summary>
    public StartupRecoveryReport? RecoveryReport { get; private init; }

    /// <summary>Set when startup stopped; null when the shell may be shown.</summary>
    public StartupFailure? Failure { get; private init; }

    /// <summary>The one question the caller asks: may this process open the shell?</summary>
    public bool CanShowShell => IsPrimaryInstance && Failure is null;

    /// <summary>Attempts moved from <c>Running</c> to <c>Interrupted</c> by this startup.</summary>
    public int RecoveredAttemptCount => RecoveryReport?.InterruptedAttemptCount ?? 0;

    /// <summary>Confirmed-stale automation locks released by this startup.</summary>
    public int ReleasedStaleLockCount => Count(StartupRecoveryAction.AutomationLockReleased);

    /// <summary>Orphaned working files moved to <c>Quarantine\</c> by this startup.</summary>
    public int QuarantinedFileCount => RecoveryReport?.QuarantinedFileCount ?? 0;

    /// <summary>
    /// Units of recovery work that could not complete. A non-zero count is <i>not</i> a startup
    /// failure — the pass itself ran and forced nothing — but it is the difference between
    /// "recovery completed with entries" and "recovery completed cleanly" (Part 3C1 §7).
    /// </summary>
    public int RecoveryFailureCount => Count(StartupRecoveryAction.RecoveryFailed);

    /// <summary>A second instance: refused before configuration, before persistence, before recovery.</summary>
    public static StartupStatus SecondInstance() => new() { IsPrimaryInstance = false };

    /// <summary>
    /// Startup was refused at <paramref name="stage"/>. <paramref name="isPrimaryInstance"/> is
    /// false only when the guard itself was the thing that failed.
    /// </summary>
    public static StartupStatus Refused(
        StartupStage stage, FailureCode? code, string technicalDetail, bool isPrimaryInstance = true) =>
        new()
        {
            IsPrimaryInstance = isPrimaryInstance,
            Failure = new StartupFailure(stage, code, technicalDetail),
        };

    /// <summary>Startup completed: this process is primary and recovery has run exactly once.</summary>
    public static StartupStatus Started(bool presetVerified, StartupRecoveryReport recoveryReport)
    {
        ArgumentNullException.ThrowIfNull(recoveryReport);

        return new StartupStatus
        {
            IsPrimaryInstance = true,
            PresetVerified = presetVerified,
            RecoveryExecuted = true,
            RecoveryReport = recoveryReport,
        };
    }

    private int Count(StartupRecoveryAction action) =>
        RecoveryReport is null ? 0 : RecoveryReport.Entries.Count(entry => entry.Action == action);
}
