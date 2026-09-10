using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PrintFlow.Infrastructure.Verification;

namespace PrintFlow.Tests.Regression;

/// <summary>
/// What a case, a check or a whole run concluded.
/// </summary>
/// <remarks>
/// Four words, chosen so that nothing that did not happen can be spelled as something that did.
/// <see cref="Blocked"/> exists because "the workstation would not let this start" is neither a
/// pass nor a failure of the Product, and collapsing it into either is how a green run comes to
/// mean nothing.
/// </remarks>
public enum RegressionOutcome
{
    /// <summary>No verdict has been recorded. Never aggregates to a pass.</summary>
    Pending = 0,

    /// <summary>Ran, and every expectation held.</summary>
    Passed,

    /// <summary>Ran, and something did not hold.</summary>
    Failed,

    /// <summary>Could not start or could not finish for an environmental reason.</summary>
    Blocked,

    /// <summary>Stopped deliberately before a verdict existed.</summary>
    Cancelled,
}

/// <summary>One qualitative check and the decision actually recorded against it.</summary>
/// <param name="Id">The manifest's stable check id.</param>
/// <param name="Question">What was asked.</param>
/// <param name="Outcome">The decision. <see cref="RegressionOutcome.Pending"/> until someone decides.</param>
/// <param name="DecidedBy">
/// Who decided, spelled out. Never a role and never blank: a visual acceptance whose author is
/// unknown is not an acceptance, and the difference between an operator's sign-off and an
/// automated session's judgement is exactly the difference a reader of this file needs.
/// </param>
/// <param name="DecidedAtLocal">When.</param>
/// <param name="EvidencePath">The artefact that was looked at.</param>
/// <param name="Notes">What the decider saw.</param>
public sealed record RegressionManualDecision(
    string Id,
    string Question,
    RegressionOutcome Outcome,
    string? DecidedBy,
    string? DecidedAtLocal,
    string? EvidencePath,
    string? Notes);

/// <summary>One structural expectation and whether it held.</summary>
public sealed record RegressionAssertion(string Name, bool Held, string Detail);

/// <summary>Everything one asset's run produced.</summary>
/// <param name="AssetId">The manifest's fixture id.</param>
/// <param name="Category">Which of the seven this case covers.</param>
/// <param name="Outcome">The case verdict, derived rather than asserted — see <see cref="Conclude"/>.</param>
/// <param name="Workflow">Which fixed workflow ran.</param>
/// <param name="StepsExecuted">The path actually taken, step by step.</param>
/// <param name="ExternalApplications">Which real applications were driven.</param>
/// <param name="AdapterIds">The adapters that answered, as they name themselves.</param>
/// <param name="ProducedArtefacts">What ended up on disk, with hashes.</param>
/// <param name="Assertions">Every structural expectation that was checked.</param>
/// <param name="ManualDecisions">Every qualitative check the manifest demanded.</param>
/// <param name="EvidencePath">Where this case's evidence was written.</param>
/// <param name="Detail">One sentence for a reader who will not open the rest.</param>
public sealed record RegressionCaseResult(
    string AssetId,
    string Category,
    RegressionOutcome Outcome,
    string? Workflow,
    ImmutableArray<string> StepsExecuted,
    ImmutableArray<string> ExternalApplications,
    ImmutableArray<string> AdapterIds,
    ImmutableArray<RegressionArtefact> ProducedArtefacts,
    ImmutableArray<RegressionAssertion> Assertions,
    ImmutableArray<RegressionManualDecision> ManualDecisions,
    string? EvidencePath,
    string Detail)
{
    /// <summary>
    /// Derives the case verdict from what was actually recorded.
    /// </summary>
    /// <remarks>
    /// A case passes only when it ran, every structural assertion held, and every manual check
    /// the manifest demanded carries a real decision. An undecided visual check leaves the case
    /// <see cref="RegressionOutcome.Pending"/>, which never aggregates to a pass — that is the
    /// mechanism by which "do not mark a subjective check passed without a decision" is
    /// structural rather than a rule somebody remembers.
    /// </remarks>
    public RegressionCaseResult Conclude()
    {
        if (Outcome is RegressionOutcome.Blocked or RegressionOutcome.Cancelled or RegressionOutcome.Failed)
        {
            return this;
        }

        if (Assertions.Any(a => !a.Held))
        {
            return this with { Outcome = RegressionOutcome.Failed };
        }

        if (ManualDecisions.Any(d => d.Outcome == RegressionOutcome.Failed))
        {
            return this with { Outcome = RegressionOutcome.Failed };
        }

        if (ManualDecisions.Any(d => d.Outcome != RegressionOutcome.Passed))
        {
            return this with { Outcome = RegressionOutcome.Pending };
        }

        return this with { Outcome = RegressionOutcome.Passed };
    }
}

/// <summary>A file the run produced, named so an auditor can open it.</summary>
public sealed record RegressionArtefact(string Role, string Path, string? Sha256, long Length);

/// <summary>One manifest of the set as this run actually read it.</summary>
/// <remarks>
/// Per-file digests rather than one composite, because the revalidation writer has to check them
/// from PowerShell and <c>Get-FileHash</c> is the same function as <c>SHA256.HashData</c>. A
/// composite would need a second implementation in a second language, and two implementations of
/// one identity is how the two sides come to disagree about whether the set changed.
/// </remarks>
/// <param name="FixtureId">The manifest's fixture id.</param>
/// <param name="Category">Which of the seven it claims.</param>
/// <param name="ManifestSha256">The manifest file's own digest.</param>
/// <param name="InputSha256">The digest the manifest records for its input file.</param>
public sealed record RegressionSetManifestIdentity(
    string FixtureId,
    string Category,
    string ManifestSha256,
    string InputSha256);

/// <summary>
/// The facts a run tested, captured by the run that tested them (PF-AUDIT-R1, findings F3 and F4).
/// </summary>
/// <remarks>
/// <b>Why the run captures these and not the writer.</b> The revalidation writer used to read the
/// current installation, the current preset and the current Windows build, and then take a
/// <c>Passed</c> label from a run result — so a genuine pass from one environment became a record
/// about another. The facts a record binds have to come from the run, at the time of the run.
/// Nothing here is filled in later from the machine: a missing fact stays missing, and a writer
/// which cannot bind refuses rather than helping.
/// <para>
/// <b>Harness and candidate are different facts.</b> <see cref="HarnessProductAssemblies"/> is the
/// PrintFlow code this test host actually loaded and drove — the provenance of the evidence.
/// <see cref="CandidateProductAssemblies"/> is the installed payload the run is attesting, read
/// from <see cref="CandidateInstallFolder"/>. They are not the same bytes even for one commit,
/// because an installation carries a RID-specific self-contained publish and a test host does not,
/// so they are bound to each other by build identity and each pinned by its own digests. See
/// <see cref="ProductBuildIdentity"/>.
/// </para>
/// </remarks>
/// <param name="Version">The contract version. A reader that does not know it must refuse it.</param>
/// <param name="InvocationId">The invocation that produced this result, from the claim it staked.</param>
/// <param name="HarnessProductAssemblies">The Product assemblies this run loaded and exercised.</param>
/// <param name="HarnessAssembly">The test assembly that drove the run, as provenance only.</param>
/// <param name="CandidateInstallFolder">The installation this run attests, or null when none was named.</param>
/// <param name="CandidateProductAssemblies">That installation's Product assemblies as the run read them.</param>
/// <param name="CandidateProblems">
/// Why the candidate could not be bound, when it could not. Non-empty means no publication may
/// follow from this run, and the run says so in its own evidence rather than leaving a reader to
/// notice an absence.
/// </param>
/// <param name="PresetSha256">The preset manifest's digest as this run verified it.</param>
/// <param name="OperatingSystemBuild">The Windows build this run observed.</param>
/// <param name="MeituSha256">The Meitu digest the accepted preset required during this run.</param>
/// <param name="PhotoshopSha256">The Photoshop digest the accepted preset required during this run.</param>
/// <param name="SetContentDigest">A digest over <paramref name="SetManifests"/>, for a diagnostic line.</param>
/// <param name="SetManifests">Every manifest of the set, with its digests.</param>
public sealed record RegressionEvidenceBinding(
    int Version,
    string InvocationId,
    ImmutableArray<ProductAssemblyIdentity> HarnessProductAssemblies,
    string? HarnessAssembly,
    string? CandidateInstallFolder,
    ImmutableArray<ProductAssemblyIdentity> CandidateProductAssemblies,
    ImmutableArray<string> CandidateProblems,
    string? PresetSha256,
    string? OperatingSystemBuild,
    string? MeituSha256,
    string? PhotoshopSha256,
    string? SetContentDigest,
    ImmutableArray<RegressionSetManifestIdentity> SetManifests)
{
    /// <summary>The contract version this build writes and reads.</summary>
    public const int CurrentVersion = 1;

    /// <summary>A digest over the set's manifests and inputs, in a fixed order.</summary>
    /// <remarks>
    /// Derived and never authoritative, exactly like
    /// <see cref="ProductBuildIdentity.Fingerprint"/>: the per-manifest digests underneath it are
    /// what any checker compares, and this exists so "the same set content" is one short string in
    /// a record and a report.
    /// </remarks>
    public static string DigestOfSet(IEnumerable<RegressionSetManifestIdentity> manifests)
    {
        ArgumentNullException.ThrowIfNull(manifests);

        StringBuilder canonical = new();
        foreach (RegressionSetManifestIdentity manifest in manifests
            .OrderBy(m => m.FixtureId, StringComparer.OrdinalIgnoreCase))
        {
            canonical
                .Append(manifest.FixtureId).Append(':')
                .Append(manifest.Category.ToUpperInvariant()).Append(':')
                .Append(manifest.ManifestSha256.ToUpperInvariant()).Append(':')
                .Append(manifest.InputSha256.ToUpperInvariant()).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }
}

/// <summary>One decision a reviewer recorded against one qualitative check.</summary>
/// <param name="Id">The manual check's stable id.</param>
/// <param name="Outcome">What was decided. Only Passed or Failed conclude anything.</param>
/// <param name="EvidencePath">The artefact that was looked at.</param>
/// <param name="EvidenceSha256">
/// That artefact's digest as the run recorded it. A decision is about a picture, and a decision
/// that does not say which picture is not reviewable evidence.
/// </param>
/// <param name="Notes">What the reviewer saw.</param>
public sealed record RegressionReviewDecision(
    string Id,
    RegressionOutcome Outcome,
    string? EvidencePath,
    string? EvidenceSha256,
    string? Notes);

/// <summary>
/// One review of an existing run, appended to the run's own evidence.
/// </summary>
/// <remarks>
/// <b>Why a history and not a field.</b> A review is an event that happened to a run at a time,
/// separate from the run's execution. Overwriting the previous review would delete the record that
/// somebody had already looked and decided — and re-stamping the run's completion time to the
/// moment of review, which the re-derivation used to do, makes an old run look as though it had
/// just executed. An appended list is the smallest mechanism the existing result shape can carry
/// that keeps both facts.
/// </remarks>
/// <param name="ReviewId">Identity of this review, so two reviews are two entries.</param>
/// <param name="DecidedBy">Who looked. Never blank and never a role.</param>
/// <param name="DecidedAtLocal">When they looked — the review's time, not the run's.</param>
/// <param name="Synthetic">
/// True when the decisions came from a test of this protocol rather than from a person. A synthetic
/// decision stays labelled synthetic: nothing here invents a human reviewer.
/// </param>
/// <param name="Decisions">What was decided.</param>
public sealed record RegressionReviewRecord(
    string ReviewId,
    string DecidedBy,
    string DecidedAtLocal,
    bool Synthetic,
    ImmutableArray<RegressionReviewDecision> Decisions);

/// <summary>
/// The whole run, in the shape the existing revalidation tooling already reads.
/// </summary>
/// <remarks>
/// <b>The four fields at the top are a contract.</b> <c>setId</c>, <c>status</c>,
/// <c>completedAtLocal</c> and <c>evidencePath</c> are what
/// <c>Set-PrintFlowProductionRevalidation.ps1</c> reads, and they are spelled and cased exactly
/// as it reads them. Everything else on this record is additional, which is the point: the
/// per-case evidence SCRUM-11065 asks for is added without changing a contract that already works.
/// <para>
/// <b>Those four are no longer sufficient, and that is deliberate</b> (PF-AUDIT-R1). The writer
/// used to read only them, so a status could be lifted out of one run and recorded against a
/// different installation. It now reads <see cref="Binding"/> as well and refuses to publish a pass
/// it cannot bind. A result written before that contract existed has no binding, is still readable
/// as the history of what ran, and cannot produce a current approval.
/// </para>
/// </remarks>
public sealed record StandardRegressionSetRunResult(
    string SetId,
    string Status,
    string CompletedAtLocal,
    string EvidencePath,
    string RunId,
    string StartedAtLocal,
    string Workstation,
    string ProductVersion,
    string PresetId,
    string PresetVersion,
    string AdapterMode,
    ImmutableArray<string> RequiredCategories,
    ImmutableArray<string> MissingCategories,
    ImmutableArray<RegressionCaseResult> Cases,
    string Verdict,
    RegressionEvidenceBinding? Binding = null,
    ImmutableArray<RegressionReviewRecord> Reviews = default)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>
    /// Builds the run result, deriving the top-level status from the cases.
    /// </summary>
    /// <remarks>
    /// Derived, exactly like <c>WorkstationVerificationResult.From</c>, and for the same reason:
    /// no caller can construct a result that claims Passed while carrying a case that did not.
    /// The required-category list is checked here too, so a run that quietly covered six of seven
    /// cannot report a pass on the six it ran — a skipped category is a missing category.
    /// </remarks>
    public static StandardRegressionSetRunResult From(
        string setId,
        string runId,
        string startedAtLocal,
        string completedAtLocal,
        string evidencePath,
        string workstation,
        string productVersion,
        string presetId,
        string presetVersion,
        string adapterMode,
        IEnumerable<RegressionCaseResult> cases,
        RegressionEvidenceBinding? binding = null,
        ImmutableArray<RegressionReviewRecord> reviews = default)
    {
        ImmutableArray<RegressionCaseResult> concluded = [.. cases.Select(c => c.Conclude())];

        ImmutableArray<string> missing =
        [
            .. StandardRegressionCategories.Required.Where(required =>
                !concluded.Any(c => string.Equals(c.Category, required, StringComparison.OrdinalIgnoreCase))),
        ];

        // A category claimed twice is a contradiction, not extra coverage: two cases can disagree,
        // and "six of seven plus one of them twice" would otherwise satisfy a count of seven. The
        // revalidation writer checks this independently from the cases it reads, so neither side
        // has to trust the other's arithmetic.
        ImmutableArray<string> duplicates =
        [
            .. concluded
                .GroupBy(c => c.Category, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .Order(StringComparer.OrdinalIgnoreCase),
        ];

        bool everyCasePassed = concluded.Length > 0 && concluded.All(c => c.Outcome == RegressionOutcome.Passed);
        bool complete = missing.IsEmpty && duplicates.IsEmpty;

        RegressionOutcome overall =
            complete && everyCasePassed ? RegressionOutcome.Passed
            : !duplicates.IsEmpty ? RegressionOutcome.Failed
            : concluded.Any(c => c.Outcome == RegressionOutcome.Failed) ? RegressionOutcome.Failed
            : concluded.Any(c => c.Outcome == RegressionOutcome.Cancelled) ? RegressionOutcome.Cancelled
            : concluded.Any(c => c.Outcome == RegressionOutcome.Blocked) ? RegressionOutcome.Blocked
            : RegressionOutcome.Pending;

        return new StandardRegressionSetRunResult(
            setId,
            overall.ToString(),
            completedAtLocal,
            evidencePath,
            runId,
            startedAtLocal,
            workstation,
            productVersion,
            presetId,
            presetVersion,
            adapterMode,
            StandardRegressionCategories.Required,
            missing,
            concluded,
            Describe(overall, concluded, missing, duplicates),
            binding,
            reviews.IsDefault ? [] : reviews);
    }

    /// <summary>Renders the result as the run writes it.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    private static string Describe(
        RegressionOutcome overall,
        ImmutableArray<RegressionCaseResult> cases,
        ImmutableArray<string> missing,
        ImmutableArray<string> duplicates)
    {
        int passed = cases.Count(c => c.Outcome == RegressionOutcome.Passed);
        string summary = $"{passed}/{StandardRegressionCategories.Required.Length} required categories passed.";

        if (!duplicates.IsEmpty)
        {
            return $"{overall}. {summary} Claimed more than once, which cannot be resolved into one " +
                   $"verdict: {string.Join(", ", duplicates)}.";
        }

        if (!missing.IsEmpty)
        {
            return $"{overall}. {summary} Not run at all: {string.Join(", ", missing)}.";
        }

        ImmutableArray<RegressionCaseResult> notPassed =
            [.. cases.Where(c => c.Outcome != RegressionOutcome.Passed)];

        return notPassed.IsEmpty
            ? $"{overall}. {summary}"
            : $"{overall}. {summary} " + string.Join(" ",
                notPassed.Select(c => $"{c.Category}: {c.Outcome} — {c.Detail}"));
    }
}
