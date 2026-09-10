using System.Collections.Immutable;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using PrintFlow.Domain.Files;

namespace PrintFlow.Infrastructure.Verification;

/// <summary>
/// What the standard regression set concluded on the environment the record describes
/// (SCRUM-11123 Part N, SCRUM-11065).
/// </summary>
/// <remarks>
/// Deliberately more than a boolean. SCRUM-11123 requires the standard test set to be rerun
/// before production use after any PrintFlow, Windows, Meitu or Photoshop upgrade, and the
/// honest answers to "did it pass" include "the set does not exist yet". That case is
/// <see cref="NotAvailable"/> and it blocks Production exactly as a failure does — a test set
/// that was never built cannot have passed, and the gate must not be able to read its absence
/// as anything else.
/// </remarks>
public enum StandardRegressionSetStatus
{
    /// <summary>
    /// The record does not state a status, or states one this build does not recognise.
    /// Blocking: an unreadable answer is not a pass.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The standard local regression set required by SCRUM-11065 has not been built, so it
    /// cannot have been run. Blocking.
    /// </summary>
    NotAvailable,

    /// <summary>The set exists and was run against this environment, and something failed. Blocking.</summary>
    Failed,

    /// <summary>The set exists and every case passed against this exact environment.</summary>
    Passed,
}

/// <summary>
/// The standard-regression-set half of a revalidation record.
/// </summary>
/// <remarks>
/// <b>The identity fields are not decoration.</b> Before PF-AUDIT-R1 this carried a status and
/// three descriptive strings, and a status with nothing behind it is what let a genuine pass from
/// one environment be recorded against another. <see cref="RunId"/> and
/// <see cref="InvocationId"/> name the one execution this outcome came from, and
/// <see cref="EvidenceBindingVersion"/> states that a writer which checks the binding produced it.
/// A <see cref="StandardRegressionSetStatus.Passed"/> outcome without them is refused by
/// <see cref="ProductionRevalidationEvaluator"/> rather than trusted.
/// </remarks>
/// <param name="SetId">Which regression set was run, as the set itself names it.</param>
/// <param name="Status">The verdict. Anything but <see cref="StandardRegressionSetStatus.Passed"/> blocks.</param>
/// <param name="CompletedAtLocal">When the run finished, in the operator's local time.</param>
/// <param name="EvidencePath">Where the run's evidence was written, for an auditor to open.</param>
/// <param name="RunId">The run folder's identity, as the run named itself.</param>
/// <param name="InvocationId">
/// The single invocation that produced the result this outcome was derived from. Minted per
/// invocation, so a result left behind by an earlier run cannot speak for a later one.
/// </param>
/// <param name="EvidenceBindingVersion">
/// The version of the evidence contract the run wrote and the writer validated. Zero means an
/// outcome recorded before that contract existed, which cannot be promoted to a current approval.
/// </param>
/// <param name="SetContentDigest">
/// A digest over the set's manifests and inputs, so "the same set id" and "the same set content"
/// are not the same claim. Derived by the run; the writer validates the per-manifest digests it
/// summarises.
/// </param>
public sealed record StandardRegressionSetOutcome(
    string? SetId,
    StandardRegressionSetStatus Status,
    string? CompletedAtLocal,
    string? EvidencePath,
    string? RunId = null,
    string? InvocationId = null,
    int EvidenceBindingVersion = 0,
    string? SetContentDigest = null);

/// <summary>
/// The operator's record that this exact environment was revalidated and may run Production
/// (SCRUM-11123 Part H).
/// </summary>
/// <remarks>
/// <b>Why this exists.</b> Every other workstation check answers "is this the accepted
/// machine?". None of them answers "has this combination of application build and accepted
/// machine actually been through the environment readiness run and the standard test set since
/// the last thing changed?" — and without that question, installing a new PrintFlow binary over
/// a verified workstation inherits the previous build's Production approval silently, which is
/// the outcome SCRUM-11123 exists to prevent.
/// <para>
/// <b>What it binds.</b> The product version, the preset identity and its digest, the operating
/// system build, and the accepted Meitu and Photoshop digests. Changing any one of them makes
/// the record describe an environment that is no longer the one in front of the operator, and
/// the check fails closed until a new record is written. That is the whole of §19: PrintFlow,
/// Windows, Meitu or Photoshop upgraded means revalidation, with no path where one of them is
/// silently absorbed.
/// <para>
/// Two of those are also verified independently — the preset digest by
/// <see cref="WorkstationVerificationCheck.PresetIntegrity"/>, the binaries by their own
/// executable checks. Binding them here as well is not duplication: those checks compare the
/// machine against the <i>current</i> preset, while this one compares the current preset against
/// the environment that was actually <i>tested</i>. A new preset that accepts a newer Photoshop
/// passes both executable checks on the day it is installed and is precisely the silent
/// environment change §19 forbids.
/// </para>
/// </para>
/// <para>
/// <b>What it is not.</b> Not a licence, not a signature, and not something the application
/// writes for itself. Nothing in PrintFlow creates or updates this record: it is written by the
/// operator's revalidation procedure (tools\installer\Set-PrintFlowProductionRevalidation.ps1)
/// after Environment Readiness and the standard regression set have both been run and their
/// evidence recorded. An application that could write its own approval would be an application
/// that approves itself.
/// </para>
/// </remarks>
/// <param name="SchemaVersion">Bumped only by a breaking change; an unknown version blocks.</param>
/// <param name="ProductVersion">The PrintFlow Studio version that was revalidated.</param>
/// <param name="ProductAssemblies">
/// Which PrintFlow bytes were revalidated, not merely which version string they claim
/// (PF-AUDIT-R1). Empty in a record written before this contract existed, which blocks: see
/// <see cref="ProductBuildIdentity"/> for why a three-part version cannot answer the question.
/// </param>
/// <param name="PresetId">The workstation preset the revalidated environment ran against.</param>
/// <param name="PresetVersion">That preset's version.</param>
/// <param name="PresetSha256">That preset manifest's digest.</param>
/// <param name="OperatingSystemBuild">The Windows build the revalidation ran on.</param>
/// <param name="MeituSha256">The accepted Meitu binary digest at revalidation time.</param>
/// <param name="PhotoshopSha256">The accepted Photoshop binary digest at revalidation time.</param>
/// <param name="EnvironmentReadinessPassed">Whether the readiness run passed.</param>
/// <param name="StandardRegressionSet">What the standard test set concluded.</param>
/// <param name="AttestedBy">The Windows account that recorded the revalidation.</param>
/// <param name="AttestedAtLocal">When it was recorded, in local time.</param>
public sealed record ProductionRevalidationRecord(
    int SchemaVersion,
    string? ProductVersion,
    ImmutableArray<ProductAssemblyIdentity> ProductAssemblies,
    string? PresetId,
    string? PresetVersion,
    string? PresetSha256,
    string? OperatingSystemBuild,
    string? MeituSha256,
    string? PhotoshopSha256,
    bool EnvironmentReadinessPassed,
    StandardRegressionSetOutcome? StandardRegressionSet,
    string? AttestedBy,
    string? AttestedAtLocal)
{
    /// <summary>The only schema this build accepts.</summary>
    /// <remarks>
    /// Raised to 2 by PF-AUDIT-R1, which added the candidate and run binding without which a
    /// recorded pass cannot be shown to be about this installation. A schema-1 record is still
    /// parsed and still readable — it is a historical fact about what an operator did — but it
    /// blocks with <see cref="HistoricalSchemaVersion"/>'s own diagnostic and is never enriched
    /// into a passing schema-2 record.
    /// </remarks>
    public const int CurrentSchemaVersion = 2;

    /// <summary>The pre-binding schema, readable and blocking (PF-AUDIT-R1).</summary>
    public const int HistoricalSchemaVersion = 1;

    /// <summary>
    /// Where the record lives, relative to the workspace root.
    /// </summary>
    /// <remarks>
    /// Under the production workspace and not beside the executable, for two reasons that both
    /// matter. It is production state rather than installed content, so it must survive an
    /// upgrade and an uninstall like every other thing in the workspace (Part K §25). And a
    /// record that lived in the installation directory could be replaced by the very installer
    /// whose upgrade it is supposed to invalidate.
    /// </remarks>
    public const string RelativePath = @"Revalidation\production-revalidation.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = true,
    };

    /// <summary>The absolute path of the record for a given workspace root.</summary>
    public static string PathFor(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        return Path.Combine(workspaceRoot, RelativePath);
    }

    /// <summary>Parses a record, returning null for anything that is not one.</summary>
    /// <remarks>
    /// Null rather than an exception, and null rather than a default-constructed record: every
    /// way of failing to read this file — absent, unreadable, malformed, empty — has to reach
    /// the check as "there is no record", which blocks. A parse failure that produced an empty
    /// record would produce one whose product version matches nothing, which happens to block
    /// too; relying on that coincidence rather than stating the rule is how a fail-open appears
    /// later.
    /// </remarks>
    public static ProductionRevalidationRecord? TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ProductionRevalidationRecord>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Renders the record as the operator tooling writes it.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);
}

/// <summary>
/// Reads the revalidation record. One method, one file, no writing.
/// </summary>
/// <remarks>
/// A port rather than a static file read so the fail-closed matrix can be tested without a
/// temporary workspace per case, and — more importantly — so that the whole of PrintFlow's
/// ability to talk to this file is a read. There is no writer interface anywhere in the
/// solution, which is what makes "the application cannot approve itself" a structural fact
/// rather than a convention. An architecture test asserts it.
/// </remarks>
public interface IProductionRevalidationReader
{
    /// <summary>The record for this workspace, or null when there is not one to read.</summary>
    ProductionRevalidationRecord? Read();
}

/// <summary>Reads the record from the production workspace.</summary>
public sealed class FileProductionRevalidationReader : IProductionRevalidationReader
{
    private readonly string _path;

    /// <param name="workspaceRoot">The configured workspace root.</param>
    public FileProductionRevalidationReader(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _path = ProductionRevalidationRecord.PathFor(workspaceRoot);
    }

    /// <inheritdoc />
    public ProductionRevalidationRecord? Read()
    {
        try
        {
            return File.Exists(_path)
                ? ProductionRevalidationRecord.TryParse(File.ReadAllText(_path))
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A record that cannot be read is not a record. The check that consumes this reports
            // "no revalidation record" and closes Production, which is the same answer an absent
            // file gets and the only safe one.
            return null;
        }
    }
}

/// <summary>
/// Decides whether the recorded revalidation still describes the environment in front of the
/// operator (SCRUM-11123 Part H §18, §19).
/// </summary>
/// <remarks>
/// Pure and static: it takes the record, the running product version and the accepted
/// requirements, and returns one check result. No file access, no clock, no environment — so
/// every branch of the matrix is directly testable and none of them can be reached by accident
/// at run time.
/// </remarks>
internal static class ProductionRevalidationEvaluator
{
    /// <summary>The version this build reports as its own.</summary>
    /// <remarks>
    /// Read from this assembly rather than from a constant, so it is the version
    /// <c>Version.props</c> stamped at build time and there is no second number to keep in step
    /// (Part D §11). Three components, because that is what the product version is and what the
    /// MSI records; the assembly's fourth revision component is always zero here and would only
    /// invite a mismatch between two spellings of the same version.
    /// </remarks>
    internal static string RunningProductVersion
    {
        get
        {
            Version? version = typeof(ProductionRevalidationEvaluator).Assembly.GetName().Version;
            return version is null
                ? "(unreadable)"
                : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    /// <summary>Evaluates the record against the current environment.</summary>
    /// <param name="record">The record as read, or null when there is none.</param>
    /// <param name="requirements">The accepted preset's requirements.</param>
    /// <param name="presetSha256">The digest this installation requires the manifest to hash to.</param>
    /// <param name="observedOsBuild">The Windows build as observed right now.</param>
    /// <param name="runningProductVersion">The running PrintFlow version.</param>
    /// <param name="observedProductAssemblies">
    /// The identity of the PrintFlow assemblies this process actually loaded, read by the caller so
    /// this method stays free of file access. <see cref="ProductBuildIdentity.Running"/> is what
    /// the application passes.
    /// </param>
    internal static WorkstationCheckResult Evaluate(
        ProductionRevalidationRecord? record,
        WorkstationRequirements requirements,
        Sha256 presetSha256,
        string observedOsBuild,
        string runningProductVersion,
        ImmutableArray<ProductAssemblyIdentity> observedProductAssemblies)
    {
        string expected = Describe(
            runningProductVersion,
            requirements.Preset.PresetId,
            requirements.Preset.PresetVersion,
            observedOsBuild);

        if (record is null)
        {
            return Fail(expected, "(no record)",
                "This workstation has no production revalidation record. Production stays closed " +
                "until Environment Readiness and the standard regression set have both been run " +
                "against this installation and the result recorded.");
        }

        // Schema first, and a historical record gets its own sentence rather than the generic
        // unknown-version one. A schema-1 record states honestly what an operator did before the
        // evidence binding existed; what it cannot state is that the run behind it tested these
        // bytes, so it is readable, blocking, and never topped up into a schema-2 pass.
        if (record.SchemaVersion == ProductionRevalidationRecord.HistoricalSchemaVersion)
        {
            return Fail(expected, "schema 1 (pre-binding)",
                "The production revalidation record predates the regression evidence binding: it " +
                "records a status without the run and candidate identity that show the run tested " +
                "this installation. It stays readable as history, and Production stays closed " +
                "until Environment Readiness and the standard regression set have been run again " +
                "and recorded with the current tooling.");
        }

        if (record.SchemaVersion != ProductionRevalidationRecord.CurrentSchemaVersion)
        {
            return Fail(expected, $"schema {record.SchemaVersion}",
                $"The production revalidation record is schema {record.SchemaVersion}; this build " +
                $"reads schema {ProductionRevalidationRecord.CurrentSchemaVersion}. Record the " +
                "revalidation again with the current tooling.");
        }

        if (!Matches(record.ProductVersion, runningProductVersion))
        {
            return Fail(expected, Describe(record.ProductVersion, record.PresetId, record.PresetVersion, record.OperatingSystemBuild),
                $"PrintFlow Studio {runningProductVersion} is installed, but the recorded " +
                $"revalidation covers {Or(record.ProductVersion)}. A PrintFlow upgrade does not " +
                "inherit the previous build's production approval: rerun Environment Readiness " +
                "and the standard regression set before returning to Production.");
        }

        // The bytes as well as the version string. A version is a name two different builds can
        // share, and replacing the installed payload with a different build of "0.1.0" used to
        // inherit this record silently — the audit's F3. The record names the assemblies that were
        // revalidated; these are the assemblies this process loaded.
        ImmutableArray<string> candidateDifferences =
            ProductBuildIdentity.CompareBytes(record.ProductAssemblies, observedProductAssemblies);

        if (!candidateDifferences.IsEmpty)
        {
            return Fail(expected,
                $"candidate {ProductBuildIdentity.Fingerprint(observedProductAssemblies)[..12]}…",
                "The PrintFlow assemblies running here are not the ones the recorded revalidation " +
                "was run against, although the version string matches. A rebuilt or replaced " +
                "payload does not inherit the previous one's production approval: rerun " +
                "Environment Readiness and the standard regression set. " +
                string.Join(" ", candidateDifferences));
        }

        if (!Matches(record.PresetId, requirements.Preset.PresetId) ||
            !Matches(record.PresetVersion, requirements.Preset.PresetVersion))
        {
            return Fail(expected, Describe(record.ProductVersion, record.PresetId, record.PresetVersion, record.OperatingSystemBuild),
                $"The accepted workstation preset is {requirements.Preset.PresetId} " +
                $"{requirements.Preset.PresetVersion}, but the recorded revalidation covers " +
                $"{Or(record.PresetId)} {Or(record.PresetVersion)}. Revalidate against the preset " +
                "this installation is configured with.");
        }

        // The digest as well as the name. PresetIntegrity already proves the manifest on disk
        // hashes to what configuration demands; this proves that what configuration demands is
        // what was actually tested. A preset re-issued under the same id and version — or an
        // appsettings.local.json pointed at a different manifest — passes the first and is
        // exactly the silent change of supported environment §19 forbids.
        if (!MatchesDigest(record.PresetSha256, presetSha256))
        {
            return Fail(expected, $"preset {Short(record.PresetSha256)}",
                "The workstation preset this installation requires is not the one the recorded " +
                "revalidation was run against. Rerun Environment Readiness and the standard " +
                "regression set against the current preset.");
        }

        if (!Matches(record.OperatingSystemBuild, observedOsBuild))
        {
            return Fail(expected, $"Windows build {Or(record.OperatingSystemBuild)}",
                $"Windows is now build {observedOsBuild}; the recorded revalidation covers build " +
                $"{Or(record.OperatingSystemBuild)}. A Windows upgrade changes the supported " +
                "environment: rerun Environment Readiness and the standard regression set.");
        }

        if (!MatchesDigest(record.MeituSha256, requirements.Meitu.Sha256))
        {
            return Fail(expected, $"Meitu {Short(record.MeituSha256)}",
                "The accepted Meitu binary is not the one the recorded revalidation was run " +
                "against. A Meitu upgrade changes the supported environment: rerun Environment " +
                "Readiness and the standard regression set before returning to Production.");
        }

        if (!MatchesDigest(record.PhotoshopSha256, requirements.Photoshop.Sha256))
        {
            return Fail(expected, $"Photoshop {Short(record.PhotoshopSha256)}",
                "The accepted Photoshop binary is not the one the recorded revalidation was run " +
                "against. A Photoshop upgrade changes the supported environment: rerun " +
                "Environment Readiness and the standard regression set before returning to " +
                "Production.");
        }

        if (!record.EnvironmentReadinessPassed)
        {
            return Fail(expected, "readiness not passed",
                "The recorded revalidation does not state a passing Environment Readiness run for " +
                "this installation.");
        }

        StandardRegressionSetStatus regression =
            record.StandardRegressionSet?.Status ?? StandardRegressionSetStatus.Unknown;

        if (regression != StandardRegressionSetStatus.Passed)
        {
            // The honest failure for this workstation today. The standard local regression set
            // SCRUM-11065 requires — a JPG portrait, a fine-hair background, a transparent PNG, a
            // complete customer design, a PSD with a composite preview, a single-page PDF and a
            // reference production TIFF — has not been built, so no revalidation can record it as
            // passed and Production stays closed after any upgrade until it exists and passes.
            return Fail(expected, $"standard regression set: {regression}",
                regression == StandardRegressionSetStatus.NotAvailable
                    ? "The standard local regression set required before production use does not " +
                      "exist on this workstation, so it cannot have passed. Production stays " +
                      "closed until the set is built and run."
                    : $"The standard regression set is recorded as {regression}, not Passed. " +
                      "Production stays closed until it passes against this installation.");
        }

        // A pass has to have an execution behind it. The status alone is what F3 exploited: it was
        // copied from a run result without anything tying it to a run, so the record could not be
        // asked which execution it meant. It can now, and a record that cannot answer is refused.
        StandardRegressionSetOutcome outcome = record.StandardRegressionSet!;

        if (string.IsNullOrWhiteSpace(outcome.RunId) ||
            string.IsNullOrWhiteSpace(outcome.InvocationId) ||
            outcome.EvidenceBindingVersion <= 0)
        {
            return Fail(expected, "standard regression set: Passed, unbound",
                "The record states that the standard regression set passed but names no execution " +
                "it passed in. A status with no run and invocation identity behind it cannot be " +
                "shown to be about this installation, so Production stays closed until the set is " +
                "run and recorded with the current tooling.");
        }

        return WorkstationCheckResult.Passed(
            WorkstationVerificationCheck.ProductionRevalidation,
            WorkstationCheckKind.Dynamic,
            expected,
            $"PrintFlow Studio {runningProductVersion} (candidate " +
            $"{ProductBuildIdentity.Fingerprint(record.ProductAssemblies)[..12]}…) on this preset " +
            $"and Windows build was revalidated by {Or(record.AttestedBy)} at " +
            $"{Or(record.AttestedAtLocal)}, with Environment Readiness passing and the standard " +
            $"regression set passing in run {Or(outcome.RunId)}.");
    }

    private static WorkstationCheckResult Fail(string expected, string observed, string explanation) =>
        WorkstationCheckResult.Failed(
            WorkstationVerificationCheck.ProductionRevalidation,
            WorkstationCheckKind.Dynamic,
            Domain.Results.FailureCode.EnvironmentNotVerified,
            expected,
            observed,
            explanation);

    private static string Describe(string? product, string? presetId, string? presetVersion, string? osBuild) =>
        $"PrintFlow {Or(product)}, preset {Or(presetId)} {Or(presetVersion)}, Windows build {Or(osBuild)}";

    private static bool Matches(string? recorded, string actual) =>
        !string.IsNullOrWhiteSpace(recorded) &&
        string.Equals(recorded.Trim(), actual, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesDigest(string? recorded, Sha256 accepted) =>
        !string.IsNullOrWhiteSpace(recorded) &&
        string.Equals(recorded.Trim(), accepted.ToString(), StringComparison.OrdinalIgnoreCase);

    private static string Or(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "(not recorded)" : value;

    private static string Short(string? digest) =>
        string.IsNullOrWhiteSpace(digest) || digest.Length < 12 ? "(not recorded)" : digest[..12] + "…";
}
