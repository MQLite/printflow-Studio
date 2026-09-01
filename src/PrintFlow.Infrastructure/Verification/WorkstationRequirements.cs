using System.Collections.Immutable;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;

namespace PrintFlow.Infrastructure.Verification;

/// <summary>One accepted external-application binary (§12, §13).</summary>
/// <param name="ProductVersion">Checked only when the preset records one.</param>
/// <param name="FileVersion">Checked only when the preset records one.</param>
/// <param name="UiLanguage">
/// What the preset says that application's own interface is in. Reported, not machine-verified:
/// see <see cref="WorkstationVerificationCheck.ExternalApplicationUiLanguage"/>.
/// </param>
public sealed record AcceptedExecutable(
    string Path,
    Sha256 Sha256,
    string? ProductVersion,
    string? FileVersion,
    string? UiLanguage);

/// <summary>The canonical Photoshop Action file on disk (§14).</summary>
public sealed record AcceptedActionArtifact(string SetName, string Path, long Bytes, Sha256 Sha256);

/// <summary>The operating-system facts the preset makes a condition (§7).</summary>
public sealed record AcceptedOperatingSystem(
    string Edition, string Version, string Build, string Architecture, string UiCulture);

/// <summary>The session policy the preset makes a condition (§8).</summary>
public sealed record AcceptedSession(
    string AllowedSession, bool RemoteAutomationAllowed, bool BlockOnActiveRemoteSession);

/// <summary>The display topology the preset makes a condition (§9).</summary>
public sealed record AcceptedDisplay(
    int ActiveDisplayCount,
    string PrimaryDisplay,
    DisplayRectangle MonitorBounds,
    DisplayRectangle WorkArea,
    int SystemDpi,
    int ScalePercent);

/// <summary>The accepted output root (§11).</summary>
public sealed record AcceptedWorkspace(string Root, string? Volume, string? FileSystem);

/// <summary>One <c>sourceManifestIntegrity</c> row.</summary>
public sealed record AcceptedEvidence(string Path, Sha256 Sha256);

/// <summary>
/// Everything the verified v1.15.0 manifest requires of this workstation (Part A §3).
/// </summary>
/// <remarks>
/// <b>Read from the manifest, never written down here.</b> There is no second workstation
/// catalogue in App or Infrastructure: no path, digest, resolution, DPI, build number or culture
/// tag appears as a literal in any verification source file. The one exception is the JSON
/// property names, which are the manifest's published spelling and are reconciled in exactly one
/// place — <see cref="PresetWorkstationRequirements"/>.
/// <para>
/// A consequence worth stating: a future preset that changes an accepted value changes what the
/// verifier demands, with no code change and no report to consult. Values quoted in an Epic
/// 11400 report are history; this record is authority (§3).
/// </para>
/// </remarks>
public sealed record WorkstationRequirements(
    ProductionPresetRef Preset,
    AcceptedOperatingSystem OperatingSystem,
    AcceptedSession Session,
    AcceptedDisplay Display,
    AcceptedWorkspace Workspace,
    AcceptedExecutable Meitu,
    AcceptedExecutable Photoshop,
    AcceptedActionArtifact PhotoshopAction,
    ImmutableArray<AcceptedEvidence> Evidence);
