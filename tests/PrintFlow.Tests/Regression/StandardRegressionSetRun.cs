using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

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

/// <summary>
/// The whole run, in the shape the existing revalidation tooling already reads.
/// </summary>
/// <remarks>
/// <b>The four fields at the top are a contract.</b> <c>setId</c>, <c>status</c>,
/// <c>completedAtLocal</c> and <c>evidencePath</c> are what
/// <c>Set-PrintFlowProductionRevalidation.ps1</c> reads, and they are spelled and cased exactly
/// as it reads them. Everything else on this record is additional and that script ignores it,
/// which is the point: the per-case evidence SCRUM-11065 asks for is added without changing a
/// contract that already works.
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
    string Verdict)
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
        IEnumerable<RegressionCaseResult> cases)
    {
        ImmutableArray<RegressionCaseResult> concluded = [.. cases.Select(c => c.Conclude())];

        ImmutableArray<string> missing =
        [
            .. StandardRegressionCategories.Required.Where(required =>
                !concluded.Any(c => string.Equals(c.Category, required, StringComparison.OrdinalIgnoreCase))),
        ];

        bool everyCasePassed = concluded.Length > 0 && concluded.All(c => c.Outcome == RegressionOutcome.Passed);
        bool complete = missing.IsEmpty;

        RegressionOutcome overall =
            complete && everyCasePassed ? RegressionOutcome.Passed
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
            Describe(overall, concluded, missing));
    }

    /// <summary>Renders the result as the run writes it.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    private static string Describe(
        RegressionOutcome overall,
        ImmutableArray<RegressionCaseResult> cases,
        ImmutableArray<string> missing)
    {
        int passed = cases.Count(c => c.Outcome == RegressionOutcome.Passed);
        string summary = $"{passed}/{StandardRegressionCategories.Required.Length} required categories passed.";

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
