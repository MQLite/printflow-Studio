namespace PrintFlow.Infrastructure.Verification;

/// <summary>
/// The closed set of workstation facts Epic 11500 verifies (Part A §6).
/// </summary>
/// <remarks>
/// An enum rather than free strings, and emphatically not a
/// <c>Dictionary&lt;string, object&gt;</c>: the later EnvironmentGate has to explain to an
/// operator exactly why Production is closed, and a caller that has to string-match a reason
/// cannot be tested for having handled every one of them. Adding a member is a deliberate
/// product decision, the same rule <see cref="Domain.Results.FailureCode"/> follows.
/// <para>
/// Each member names a fact the <b>accepted preset</b> states. Nothing here inspects a machine
/// property the signed baseline does not talk about (§7).
/// </para>
/// </remarks>
public enum WorkstationVerificationCheck
{
    /// <summary>The configured manifest hashes to the configured SHA-256 (§4).</summary>
    PresetIntegrity,

    /// <summary>Every <c>sourceManifestIntegrity</c> entry is present and hashes exactly (§4).</summary>
    EvidenceIntegrity,

    /// <summary>The configured workspace root is the accepted root, and is addressable (§11).</summary>
    WorkspaceRoot,

    /// <summary>
    /// This exact installation — this PrintFlow version, on this preset, on this Windows build,
    /// against these accepted Meitu and Photoshop binaries — has been through Environment
    /// Readiness and the standard regression set, and both passed (SCRUM-11123 Part H).
    /// </summary>
    /// <remarks>
    /// The one check that is not about the machine. Every other member here asks whether this is
    /// the accepted workstation; this one asks whether the accepted workstation and the installed
    /// application have actually been tested together since the last thing changed. Without it, a
    /// newly installed PrintFlow binary inherits the previous build's Production approval the
    /// moment it starts, which is what SCRUM-11123 exists to prevent.
    /// <para>
    /// Dynamic rather than immutable, because an operator who records a revalidation while
    /// PrintFlow is running must be able to return to Production without restarting it, and
    /// because a record that is deleted mid-session must close Production on the next request.
    /// </para>
    /// </remarks>
    ProductionRevalidation,

    /// <summary>Edition, version, build and architecture match the accepted baseline (§7).</summary>
    OperatingSystem,

    /// <summary>A supported interactive local-console desktop session exists (§8).</summary>
    InteractiveSession,

    /// <summary>Display count, geometry, work area and scaling match the accepted contract (§9).</summary>
    DisplayConfiguration,

    /// <summary>The Windows user UI language matches the accepted workstation culture (§10).</summary>
    UiCulture,

    /// <summary>What the preset records about Meitu's and Photoshop's own UI language (§10).</summary>
    ExternalApplicationUiLanguage,

    /// <summary>The accepted Meitu binary is present at its path with its exact digest (§12).</summary>
    MeituExecutable,

    /// <summary>The accepted Photoshop binary is present with its exact digest and versions (§13).</summary>
    PhotoshopExecutable,

    /// <summary>The canonical <c>PrintFlow-DTF-v1.atn</c> on disk hashes exactly (§14).</summary>
    PhotoshopActionArtifact,

    /// <summary>
    /// Whether integrity-referenced evidence still carries the read-only filesystem attribute
    /// (§15).
    /// </summary>
    /// <remarks>
    /// Never blocking under the current protocol. Epic 11400 Final QA found that some
    /// integrity-referenced files have lost the attribute, and v1.15.0's own
    /// <c>immutability.filesystemPolicy</c> settles the question in the same breath as raising
    /// it: "SHA-256 remains authoritative". This check exists so the drift is visible, not so it
    /// can close Production.
    /// </remarks>
    FilesystemReadOnlyPolicyAdvisory,

    /// <summary>The shared external-application automation lock was acquired for the live run.</summary>
    ExternalApplicationAutomationLock,

    /// <summary>The accepted Meitu process can start or attach and reach a recognised state.</summary>
    MeituLaunchability,

    /// <summary>Meitu is currently in a positively recognised safe starting state.</summary>
    MeituSafeStartingState,

    /// <summary>The accepted Photoshop process can start or attach and reach a recognised state.</summary>
    PhotoshopLaunchability,

    /// <summary>Photoshop can currently be driven without an unknown dialog or unsaved document.</summary>
    PhotoshopSafeStartingState,

    /// <summary>Photoshop's active colour settings match the accepted preset.</summary>
    PhotoshopColourSettings,

    /// <summary>A PrintFlow-owned synthetic image opened, was identified, and closed safely.</summary>
    PhotoshopTestImageRoundTrip,
}

/// <summary>
/// Whether a check reads a signed, fixed fact or the workstation's current state (Part A §16).
/// </summary>
/// <remarks>
/// The distinction is what stops the later gate caching a stale yes. An immutable check answers
/// "is this the accepted installation?" — a binary's hash does not change while the process
/// runs, so it may be evaluated once. A dynamic check answers "is this workstation ready right
/// now?" — the operator can lock the screen, plug in a second monitor or start a remote session
/// between one production step and the next, so it must be re-evaluated every time.
/// </remarks>
public enum WorkstationCheckKind
{
    /// <summary>A signed baseline fact: preset and evidence digests, binaries, the Action file.</summary>
    Immutable,

    /// <summary>Current machine state: session, display, culture, workspace availability.</summary>
    Dynamic,

    /// <summary>Current external-application state, observed without changing settings or documents.</summary>
    Live,

    /// <summary>An explicit, reversible operation over PrintFlow-owned synthetic data.</summary>
    Smoke,
}

/// <summary>What one check concluded.</summary>
public enum WorkstationCheckOutcome
{
    /// <summary>The observed fact matched the accepted fact.</summary>
    Passed,

    /// <summary>The observed fact did not match. Verification is closed.</summary>
    Failed,

    /// <summary>
    /// An observation worth reporting that the accepted contract does not make a condition of
    /// verification. Never closes Production on its own.
    /// </summary>
    Advisory,

    /// <summary>The check deliberately did not run because its prerequisite was not satisfied.</summary>
    Blocked,
}
