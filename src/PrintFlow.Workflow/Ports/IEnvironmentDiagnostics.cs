namespace PrintFlow.Workflow.Ports;

/// <summary>What one workstation readiness check concluded, in App-safe vocabulary.</summary>
/// <remarks>
/// The three values mirror the Infrastructure verifier's own outcomes, restated here so nothing
/// above Infrastructure has to name a verification type to render a support screen
/// (Epic 11500 Part B §21).
/// </remarks>
public enum EnvironmentCheckStatus
{
    /// <summary>The workstation matched the accepted production baseline for this fact.</summary>
    Passed,

    /// <summary>It did not, and Production is therefore closed.</summary>
    Failed,

    /// <summary>An observation worth showing that never closes Production (§8).</summary>
    Advisory,

    /// <summary>A prerequisite prevented the check from running; Production remains closed.</summary>
    Blocked,
}

/// <summary>Whether a row is passive/automatic or part of explicit live application verification.</summary>
public enum EnvironmentCheckPhase
{
    Automatic,
    LiveApplication,
}

/// <summary>
/// One line of the operator-facing readiness report (Epic 11500 Part B §21).
/// </summary>
/// <param name="CheckKey">
/// The stable English name of the fact that was checked — <c>DisplayConfiguration</c>,
/// <c>PhotoshopExecutable</c>. Stable English like a <c>FailureCode</c>, never shown raw.
/// </param>
/// <param name="Status">Passed, Failed, or a non-blocking Advisory.</param>
/// <param name="IsBlocking">
/// Whether this check can close Production. False for every advisory, so a support screen can
/// separate the two without re-deriving the rule (§8).
/// </param>
/// <param name="MessageKey">
/// The resource key for the concise localised sentence, resolved at display time exactly the way
/// an <c>OperationFailure.MessageKey</c> is. The raw <paramref name="CheckKey"/> is deliberately
/// not the operator's primary message (§22).
/// </param>
/// <param name="Detail">One concise English sentence for the local log and for support.</param>
public sealed record EnvironmentCheckReport(
    string CheckKey,
    EnvironmentCheckStatus Status,
    bool IsBlocking,
    string MessageKey,
    string Detail,
    string? Expected = null,
    string? Current = null,
    EnvironmentCheckPhase Phase = EnvironmentCheckPhase.Automatic);

/// <summary>
/// A bounded account of whether this workstation may run Production right now
/// (Epic 11500 Part B §21).
/// </summary>
/// <param name="Verified">True only when every blocking check passed.</param>
/// <param name="PresetIdentity">
/// The accepted preset's id, version and short digest, or <c>null</c> when the preset itself did
/// not verify and therefore states nothing.
/// </param>
/// <param name="ObservedAt">When the dynamic half of this report was observed.</param>
/// <param name="Checks">Every check that ran, in evaluation order.</param>
/// <remarks>
/// The report itself is immutable and never grants permission. There is no "enable", "ignore",
/// "override" or "continue anyway": it exists so a person can see why Production is closed and
/// fix the workstation, not so they can talk the gate out of its answer (§24).
/// <para>
/// It is also a snapshot, never a permission. <see cref="ObservedAt"/> is audit information; the
/// gate re-asks the verifier on every Production request rather than consulting anything stored
/// here (§7).
/// </para>
/// </remarks>
public sealed record EnvironmentReadinessReport(
    bool Verified,
    string? PresetIdentity,
    DateTimeOffset ObservedAt,
    IReadOnlyList<EnvironmentCheckReport> Checks)
{
    /// <summary>Absent in historical reports; absence never means a probe completed.</summary>
    public ReadinessEvidenceLifecycle? Lifecycle { get; init; }

    /// <summary>
    /// The output location this workstation was verified against, or null when the accepted
    /// preset states none (SCRUM-11118).
    /// </summary>
    /// <remarks>
    /// A named fact beside <see cref="PresetIdentity"/> and for the same reason: a screen that
    /// needs one particular verified value must not have to pick a check out of
    /// <see cref="Checks"/> by name. Which check states it is the verification authority's
    /// business, and it is the only thing that fills this in.
    /// </remarks>
    public string? AcceptedOutputRoot { get; init; }

    /// <summary>
    /// What the Photoshop colour-setup check concluded, or null when it did not run
    /// (SCRUM-11110, displayed by SCRUM-11118).
    /// </summary>
    /// <remarks>
    /// Null is the honest answer for a passive reading: the colour setup is verified by the
    /// explicit live application phase, so a report that has not run one states nothing about
    /// it. A display must say so rather than infer a confirmation from silence.
    /// </remarks>
    public EnvironmentCheckStatus? PhotoshopColourSetup { get; init; }

    /// <summary>The blocking checks that closed Production.</summary>
    public IEnumerable<EnvironmentCheckReport> BlockingFailures =>
        Checks.Where(c => c.IsBlocking &&
                          c.Status is EnvironmentCheckStatus.Failed or EnvironmentCheckStatus.Blocked);

    /// <summary>Observations that are reported and never block (§8).</summary>
    public IEnumerable<EnvironmentCheckReport> Advisories =>
        Checks.Where(c => c.Status == EnvironmentCheckStatus.Advisory);
}

/// <summary>
/// The support seam onto workstation readiness (Epic 11500 Part B §21).
/// </summary>
/// <remarks>
/// Separate from <see cref="IEnvironmentGate"/> in intent but implemented by the same object, so
/// there is exactly one thing in the process that consults the workstation verifier and no
/// second path by which a caller could reach it. <see cref="Read"/> is passive; the explicitly
/// named <see cref="RunLiveChecksAsync"/> may launch or attach to the accepted applications and
/// drive one contained synthetic probe. Neither operation authorises Production, and authorising
/// never consults a stored reading.
/// </remarks>
public interface IEnvironmentDiagnostics
{
    /// <summary>Re-observes the workstation and reports what it found. Changes nothing.</summary>
    EnvironmentReadinessReport Read();

    /// <summary>Explicitly runs bounded launch, state, colour, and synthetic-image checks.</summary>
    Task<EnvironmentReadinessReport> RunLiveChecksAsync(CancellationToken cancellationToken);
}
