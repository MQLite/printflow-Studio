using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Infrastructure.Verification;
using PrintFlow.Tests.Regression;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;
using Xunit.Abstractions;

namespace PrintFlow.Tests.Smoke;

/// <summary>
/// Layer 2 of the standard regression procedure: the fixed workstation actually running the set
/// (SCRUM-11065).
/// </summary>
/// <remarks>
/// Opt-in and inert by default, like every workstation smoke — the signed preset and the
/// accepted binaries exist on exactly one machine. <c>PRINTFLOW_STANDARD_REGRESSION_SET=1</c>
/// runs it; <c>tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1</c> is the entry point
/// an operator uses and this is what it invokes.
/// <para>
/// <b>What separates this from Layer 1.</b> Layer 1 proves the set is a set: seven categories,
/// readable manifests, files present, hashes matching. It never opens an application, and it is
/// deliberately not a regression run — a set that exists is not a set that passed. This drives
/// the real <c>ISessionService</c> against the real Production adapters, so the categories whose
/// recorded path names Meitu or Photoshop are answered by Meitu and Photoshop.
/// </para>
/// <para>
/// <b>Fake adapters are not evidence here.</b> Nothing in this file substitutes an adapter. The
/// configured mode is read and asserted to be Production, and a run on a Fake-configured
/// installation reports itself Blocked rather than producing a green result that means nothing.
/// </para>
/// <para>
/// <b>Why it is in the environment-variable collection.</b> Everything about an invocation of this
/// run arrives through process environment variables, and those are process-global. The protocol
/// tests in <c>RegressionEvidenceIntegrityTests</c> set the same variables, so without a shared
/// collection xUnit could start this run against another test's values — which on this workstation
/// would mean driving Meitu and Photoshop because a unit test happened to be running. The claim
/// staked at the top of the run refuses that case before anything is composed; the collection makes
/// sure it never arises.
/// </para>
/// <para>
/// <b>The bootstrap.</b> The one thing this run does not require of the workstation is the
/// revalidation record whose existence depends on this run — see
/// <see cref="RegressionBootstrapWorkstationVerifier"/>, which suppresses that single check and
/// only when it is the sole thing blocking. Whether it was needed is recorded in the run result.
/// </para>
/// </remarks>
[Collection(PrintFlow.Tests.Fixtures.EnvironmentVariableCollection.Name)]
public sealed class StandardRegressionSetWorkstationSmoke(ITestOutputHelper output)
{
    private const string EnableVariable = "PRINTFLOW_STANDARD_REGRESSION_SET";
    private const string SetRootVariable = "PRINTFLOW_REGRESSION_SET_ROOT";
    private const string CategoryFilterVariable = "PRINTFLOW_REGRESSION_CATEGORIES";
    private const string RunIdVariable = "PRINTFLOW_REGRESSION_RUN_ID";
    private const string ReaggregateVariable = "PRINTFLOW_REGRESSION_REAGGREGATE";
    private const string DecisionsVariable = "PRINTFLOW_REGRESSION_VISUAL_DECISIONS";
    private const string InvocationIdVariable = "PRINTFLOW_REGRESSION_INVOCATION_ID";
    private const string CandidateVariable = "PRINTFLOW_REGRESSION_CANDIDATE_INSTALL_FOLDER";

    /// <summary>Where the installation this run attests is expected to be.</summary>
    private static readonly string DefaultCandidateInstallFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PrintFlow Studio");

    private const string DefaultSetRoot = @"D:\PrintFlowStudio\TestData\v1";

    /// <summary>The production size every produced output is prepared at.</summary>
    /// <remarks>
    /// One box for the whole set, and a small one. Every asset in the set is larger than this at
    /// 300 PPI, so every case shrinks and none of them trips the enlargement authority — which is
    /// a separate product rule with its own tests and has no business deciding whether the
    /// regression set passes.
    /// </remarks>
    private static readonly PrintDimensions ProductionBox =
        PrintDimensions.FromMillimetres(50.8, 100, SizePreset.Custom);

    /// <summary>
    /// Re-derives a completed run's verdict after the qualitative checks have been decided.
    /// </summary>
    /// <remarks>
    /// The visual questions the portrait and fine-hair cases record cannot be answered before the
    /// artefacts exist, and the artefacts cost a Meitu operation each. Re-running the set to
    /// carry a decision into it would mean the reviewed pictures were not the ones the verdict
    /// describes, so this reads the per-case evidence the run already wrote, applies the
    /// decisions, and re-derives the top-level status through the same
    /// <see cref="StandardRegressionSetRunResult.From"/> every other path uses. No case is
    /// re-run and no assertion is re-evaluated: a decision can only ever turn a Pending case into
    /// a Passed or Failed one.
    /// <para>
    /// Set <c>PRINTFLOW_REGRESSION_REAGGREGATE</c> to the run folder and
    /// <c>PRINTFLOW_REGRESSION_VISUAL_DECISIONS</c> to the decisions file.
    /// </para>
    /// </remarks>
    [Fact]
    public void Re_derive_a_completed_run_from_recorded_visual_decisions()
    {
        if (Environment.GetEnvironmentVariable(ReaggregateVariable) is not { } runFolder ||
            string.IsNullOrWhiteSpace(runFolder))
        {
            return;
        }

        string decisionsPath = Environment.GetEnvironmentVariable(DecisionsVariable)
            ?? throw new InvalidOperationException(
                $"{ReaggregateVariable} was set without {DecisionsVariable}. Re-deriving a verdict " +
                "requires the decisions that justify it.");

        StandardRegressionSetRunResult previous = Read<StandardRegressionSetRunResult>(
            Path.Combine(runFolder, "result.json"));
        RecordedVisualReview review = Read<RecordedVisualReview>(decisionsPath);

        review.DecidedBy.ShouldNotBeNullOrWhiteSpace(
            "A visual acceptance with no named decider is not an acceptance.");

        string reviewedAt = review.DecidedAtLocal ?? DateTimeOffset.Now.ToString("o");
        Dictionary<string, RecordedVisualDecision> byId = review.Decisions
            .ToDictionary(d => d.Id, StringComparer.OrdinalIgnoreCase);

        // A decision may only conclude a check that is genuinely outstanding. Re-deciding one that
        // already carries an outcome would let a second pass over the same run replace a recorded
        // Failed with a Passed, which is not a review — it is an edit.
        HashSet<string> outstanding = new(
            previous.Cases
                .SelectMany(c => c.ManualDecisions)
                .Where(d => d.Outcome == RegressionOutcome.Pending)
                .Select(d => d.Id),
            StringComparer.OrdinalIgnoreCase);

        ImmutableArray<string> notOutstanding =
            [.. byId.Keys.Where(id => !outstanding.Contains(id)).Order(StringComparer.OrdinalIgnoreCase)];

        if (!notOutstanding.IsEmpty)
        {
            throw new InvalidOperationException(
                "These decisions answer nothing this run left open: " +
                string.Join(", ", notOutstanding) +
                ". A review concludes outstanding qualitative checks; it does not revise decided " +
                "ones, and it cannot turn a Blocked, Failed or unexecuted case into a success.");
        }

        List<RegressionReviewDecision> recorded = [];
        List<RegressionCaseResult> updated = [];
        foreach (RegressionCaseResult existing in previous.Cases)
        {
            RegressionCaseResult replaced = existing with
            {
                ManualDecisions =
                [
                    .. existing.ManualDecisions.Select(pending =>
                    {
                        if (!byId.TryGetValue(pending.Id, out RecordedVisualDecision? decision))
                        {
                            return pending;
                        }

                        // The decision is about a picture, so it is bound to that picture as this
                        // run hashed it. An artefact that no longer hashes to what the run recorded
                        // is not the thing the reviewer looked at, and a decision about it cannot
                        // be carried into the run's verdict.
                        RegressionArtefact? artefact = ArtefactFor(existing, pending);
                        VerifyArtefactUnchanged(existing, pending, artefact);

                        RegressionOutcome outcome =
                            Enum.Parse<RegressionOutcome>(decision.Outcome, ignoreCase: true);

                        recorded.Add(new RegressionReviewDecision(
                            pending.Id, outcome, artefact?.Path, artefact?.Sha256, decision.Notes));

                        return pending with
                        {
                            Outcome = outcome,
                            DecidedBy = review.DecidedBy,
                            DecidedAtLocal = reviewedAt,
                            EvidencePath = artefact?.Path ?? pending.EvidencePath,
                            Notes = decision.Notes,
                        };
                    }),
                ],
            };

            updated.Add(replaced);
            foreach (RegressionManualDecision decision in replaced.ManualDecisions)
            {
                output.WriteLine(
                    $"{replaced.Category} / {decision.Id}: {decision.Outcome} " +
                    $"by {decision.DecidedBy ?? "(nobody)"} — {decision.Notes ?? "(no note)"}");
            }
        }

        // The execution's own facts are carried across untouched: its id, when it started, when it
        // *completed*, the workstation, the candidate and preset it ran against, and every
        // assertion and artefact digest its cases recorded. Only the outstanding decisions change.
        // Re-stamping completedAtLocal to now, which this path used to do, made an old run look as
        // though it had just executed — the reviewer's time belongs to the review, and it is
        // recorded there (PF-AUDIT-R1).
        StandardRegressionSetRunResult rederived = StandardRegressionSetRunResult.From(
            previous.SetId, previous.RunId, previous.StartedAtLocal,
            previous.CompletedAtLocal, previous.EvidencePath, previous.Workstation,
            previous.ProductVersion, previous.PresetId, previous.PresetVersion, previous.AdapterMode,
            updated,
            previous.Binding,
            [
                // Appended, never replaced: a run that has been looked at twice has two reviews,
                // and the earlier one is part of what happened to it.
                .. previous.Reviews.IsDefault ? [] : previous.Reviews,
                new RegressionReviewRecord(
                    Guid.NewGuid().ToString(),
                    review.DecidedBy,
                    reviewedAt,
                    review.Synthetic,
                    [.. recorded]),
            ]);

        WriteResult(runFolder, rederived);
        output.WriteLine(string.Empty);
        output.WriteLine(rederived.Verdict);
    }

    /// <summary>The artefact a manual check was recorded against, if the run named one.</summary>
    private static RegressionArtefact? ArtefactFor(
        RegressionCaseResult existing, RegressionManualDecision pending) =>
        pending.EvidencePath is { } named && !string.IsNullOrWhiteSpace(named)
            ? existing.ProducedArtefacts.FirstOrDefault(a =>
                string.Equals(a.Path, named, StringComparison.OrdinalIgnoreCase))
            : null;

    /// <summary>Refuses a decision whose artefact is not the one the run produced.</summary>
    private static void VerifyArtefactUnchanged(
        RegressionCaseResult existing, RegressionManualDecision pending, RegressionArtefact? artefact)
    {
        if (artefact is null)
        {
            throw new InvalidOperationException(
                $"{existing.Category} / {pending.Id}: the run recorded no artefact for this check, " +
                "so there is nothing a decision about it could be bound to.");
        }

        if (string.IsNullOrWhiteSpace(artefact.Sha256))
        {
            throw new InvalidOperationException(
                $"{existing.Category} / {pending.Id}: '{artefact.Path}' was recorded without a " +
                "digest, so a decision about it cannot be bound to what was actually produced.");
        }

        if (!File.Exists(artefact.Path))
        {
            throw new InvalidOperationException(
                $"{existing.Category} / {pending.Id}: '{artefact.Path}' is gone. A decision is " +
                "about an artefact; this one can no longer be shown.");
        }

        string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(artefact.Path)));
        if (!string.Equals(actual, artefact.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"{existing.Category} / {pending.Id}: '{artefact.Path}' now hashes to {actual}; the " +
                $"run recorded {artefact.Sha256}. This is not the artefact the run produced, so a " +
                "decision about it is not a decision about this run.");
        }
    }

    [Fact]
    public async Task Run_the_standard_local_regression_set_on_the_fixed_workstation()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            return;
        }

        string setRoot = Environment.GetEnvironmentVariable(SetRootVariable) ?? DefaultSetRoot;
        string runId = Environment.GetEnvironmentVariable(RunIdVariable)
            ?? DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string runFolder = Path.Combine(setRoot, "runs", runId);

        // The claim is the first thing this run does — before configuration is read, before the
        // graph is composed, before anything looks at an external application. A run identity that
        // is already claimed, or a destination that already holds a completed run, stops here at no
        // cost and destroys nothing (PF-AUDIT-R1, finding F4). The wrapper mints the invocation id;
        // a direct invocation of this test mints its own, so the seam cannot be used to inherit
        // somebody else's identity.
        string invocationId = Environment.GetEnvironmentVariable(InvocationIdVariable) is { } supplied &&
            !string.IsNullOrWhiteSpace(supplied)
                ? supplied
                : Guid.NewGuid().ToString();

        RegressionExecutionClaim claim = RegressionExecutionClaim.Stake(runFolder, runId, invocationId);

        // The permanent claim stays first. Origin validation is read-only and precedes even
        // configuration/composition; an explicitly publishable request must fail before real work.
        string? receiptPath = Environment.GetEnvironmentVariable("PRINTFLOW_REGRESSION_BUILD_PAIR_RECEIPT");
        RegressionBuildOrigin? origin = null;
        if (!string.IsNullOrWhiteSpace(receiptPath))
        {
            origin = RegressionBuildOrigin.Capture(receiptPath,
                Environment.GetEnvironmentVariable(CandidateVariable) ?? DefaultCandidateInstallFolder);
        }
        else if (Environment.GetEnvironmentVariable("PRINTFLOW_REGRESSION_DIAGNOSTIC_UNBOUND") != "1")
        {
            throw new InvalidDataException("Build origin: a completed build-pair receipt is required before operational setup.");
        }

        DateTimeOffset startedAt = DateTimeOffset.Now;
        output.WriteLine($"Set root : {setRoot}");
        output.WriteLine($"Run      : {runFolder}");
        output.WriteLine($"Claim    : invocation {claim.InvocationId} by {claim.ClaimedBy}");

        // ------------------------------------------------------------------------------
        // Layer 1, re-asserted here
        // ------------------------------------------------------------------------------
        // The PowerShell entry point already ran this. Running it again is not redundancy for
        // its own sake: this process is the one about to drive Photoshop, and "the set was valid
        // a moment ago in another process" is not the same claim as "the set is valid now".
        StandardRegressionSet set = StandardRegressionSet.Load(setRoot);
        ImmutableArray<RegressionSetProblem> problems = set.Validate(requireExecutableExpectations: true);
        foreach (RegressionSetProblem problem in problems)
        {
            output.WriteLine($"PREFLIGHT {problem.AssetId}: {problem.Detail}");
        }

        PrintFlowConfiguration configuration = LoadConfiguration();
        string? meituExecutable = Environment.GetEnvironmentVariable(RegressionMeituOverride.EnvironmentVariable);
        bool usesMeituOverride = !string.IsNullOrWhiteSpace(meituExecutable);
        if (usesMeituOverride)
        {
            configuration = RegressionMeituOverride.Apply(configuration, meituExecutable!, runFolder);
            output.WriteLine(RegressionMeituOverride.PublicationProblem);
        }
        string workstation = Environment.MachineName;

        // Everything this run is about to test, captured now, by the thing testing it. Built before
        // the first result can be written so that every exit from here — preflight refusal, wrong
        // adapter mode, blocked environment, or a completed run — carries the same bound facts and
        // no path can produce a result a reader has to guess about (PF-AUDIT-R1, finding F3).
        RegressionEvidenceBinding binding = Bind(set, configuration, invocationId, origin, usesMeituOverride);
        output.WriteLine(
            $"Candidate: {binding.CandidateInstallFolder ?? "(none named)"} " +
            $"[{ProductBuildIdentity.Fingerprint(binding.CandidateProductAssemblies)[..12]}…]");
        foreach (string problem in binding.CandidateProblems)
        {
            output.WriteLine("CANDIDATE " + problem);
        }

        if (!problems.IsEmpty)
        {
            WriteResult(runFolder, StandardRegressionSetRunResult.From(
                set.SetId ?? "(unknown)", runId, Iso(startedAt), Iso(DateTimeOffset.Now), runFolder,
                workstation, ProductVersion(), configuration.Preset.Id, configuration.Preset.Version,
                configuration.Adapters.Mode,
                [.. StandardRegressionCategories.Required.Select(category => Blocked(
                    "(preflight)", category,
                    "The set did not pass static validation, so no case was started."))],
                binding));
            Assert.Fail("The regression set failed preflight validation; no case was run. See result.json.");
        }

        if (!string.Equals(configuration.Adapters.Mode, "Production", StringComparison.OrdinalIgnoreCase))
        {
            WriteResult(runFolder, StandardRegressionSetRunResult.From(
                set.SetId!, runId, Iso(startedAt), Iso(DateTimeOffset.Now), runFolder,
                workstation, ProductVersion(), configuration.Preset.Id, configuration.Preset.Version,
                configuration.Adapters.Mode,
                [.. StandardRegressionCategories.Required.Select(category => Blocked(
                    "(configuration)", category,
                    $"Adapters:Mode is '{configuration.Adapters.Mode}'. Fake adapters cannot stand as " +
                    "fixed-workstation regression evidence, so nothing was run."))],
                binding));
            Assert.Fail($"Adapters:Mode is '{configuration.Adapters.Mode}', not Production. Nothing was run.");
        }

        // ------------------------------------------------------------------------------
        // Compose the real graph, with the one bootstrap seam
        // ------------------------------------------------------------------------------
        string workspaceRoot = Path.GetFullPath(configuration.Workspace.Root);
        SqliteConnectionFactory connections = new(Path.Combine(runFolder, "regression-run.db"));
        using (SqliteConnection connection = connections.Open())
        {
            MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();
        }

        RegressionBootstrapWorkstationVerifier? bootstrap = null;
        using ServiceProvider services = ServiceRegistration.BuildServiceProvider(
            configuration, workspaceRoot, connections, registrations =>
            {
                // Decorated, never replaced. The descriptor the application registered is taken
                // out, its own factory still builds the verifier that reads this machine, and the
                // wrapper is put in front of it holding a second verifier composed the same way
                // with only the self-referential check omitted. Which of the two answers is used
                // is decided per call by the real one — see RegressionBootstrapWorkstationVerifier.
                ServiceDescriptor existing = registrations.Single(
                    d => d.ServiceType == typeof(IProductionWorkstationVerifier));
                registrations.Remove(existing);
                registrations.AddSingleton<IProductionWorkstationVerifier>(provider =>
                    bootstrap = new RegressionBootstrapWorkstationVerifier(
                        (IProductionWorkstationVerifier) existing.ImplementationFactory!(provider),
                        ProductionWorkstationVerifier.ForStandardRegressionRun(
                            Path.Combine(workspaceRoot, configuration.Preset.Path),
                            configuration.Preset.Id,
                            configuration.Preset.Version,
                            Sha256.Parse(configuration.Preset.ExpectedSha256),
                            workspaceRoot,
                            provider.GetRequiredService<IWorkspace>(),
                            provider.GetRequiredService<IWorkstationAutomationLeaseManager>(),
                            Path.Combine(workspaceRoot, "Evidence"),
                            provider.GetRequiredService<TimeProvider>())));
            });

        IEnvironmentDiagnostics diagnostics = services.GetRequiredService<IEnvironmentDiagnostics>();
        EnvironmentReadinessReport readiness = await diagnostics.RunLiveChecksAsync(CancellationToken.None);
        File.WriteAllText(Path.Combine(runFolder, "readiness.json"),
            System.Text.Json.JsonSerializer.Serialize(readiness, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            }));

        foreach (EnvironmentCheckReport check in readiness.Checks)
        {
            output.WriteLine($"[{check.Status,-8}] {check.CheckKey}: {check.Detail}");
        }

        // The live phase is a positive requirement, not a formality. Meitu and Photoshop
        // launchability, their recognised safe starting states, Photoshop's colour settings, the
        // test-image round trip and the global automation lock must all have actually passed
        // before this run drives an external application. A blocked live check is not a passing
        // one, and a run that started anyway would be exercising an environment nobody verified.
        ImmutableArray<EnvironmentCheckReport> unsatisfied =
        [
            .. readiness.Checks.Where(c =>
                c.IsBlocking && c.Status is not EnvironmentCheckStatus.Passed),
        ];

        if (!unsatisfied.IsEmpty)
        {
            string why = string.Join("; ", unsatisfied.Select(c => $"{c.CheckKey}: {c.Status}"));
            output.WriteLine("Live environment checks did not all pass: " + why);
            WriteResult(runFolder, StandardRegressionSetRunResult.From(
                set.SetId!, runId, Iso(startedAt), Iso(DateTimeOffset.Now), runFolder,
                workstation, ProductVersion(), configuration.Preset.Id, configuration.Preset.Version,
                configuration.Adapters.Mode,
                [.. StandardRegressionCategories.Required.Select(category => Blocked(
                    "(environment)", category,
                    "The required live environment checks did not all pass, so no external " +
                    "application was driven: " + Truncate(why, 400)))],
                binding));
            Assert.Fail("Live environment checks did not all pass. Nothing was run. See readiness.json.");
        }

        OperationResult<PrintFlow.Domain.Results.Unit> gate = services.GetRequiredService<IEnvironmentGate>()
            .Verify(AdapterExecutionMode.Production);

        if (gate.IsFailure)
        {
            string why = gate.Failure.ToString();
            output.WriteLine("Production gate REFUSED: " + why);
            WriteResult(runFolder, StandardRegressionSetRunResult.From(
                set.SetId!, runId, Iso(startedAt), Iso(DateTimeOffset.Now), runFolder,
                workstation, ProductVersion(), configuration.Preset.Id, configuration.Preset.Version,
                configuration.Adapters.Mode,
                [.. StandardRegressionCategories.Required.Select(category => Blocked(
                    "(environment)", category,
                    "The workstation did not verify, so no case was started: " + Truncate(why, 400)))],
                binding));
            Assert.Fail("The workstation did not verify. Nothing was run. See result.json and readiness.json.");
        }

        output.WriteLine("Production gate ALLOWED" +
            (bootstrap?.BootstrapWasUsed == true
                ? " (revalidation bootstrap in use: every other check passed)"
                : " (no bootstrap needed: a valid revalidation record already covers this environment)"));

        // ------------------------------------------------------------------------------
        // Run the cases
        // ------------------------------------------------------------------------------
        HashSet<string>? only = Environment.GetEnvironmentVariable(CategoryFilterVariable) is { } filter &&
            !string.IsNullOrWhiteSpace(filter)
                ? new HashSet<string>(
                    filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    StringComparer.OrdinalIgnoreCase)
                : null;

        List<RegressionCaseResult> results = [];
        foreach (RegressionAssetManifest asset in set.Assets.OrderBy(a => Order(a.Category)))
        {
            if (only is not null && !only.Contains(asset.Category))
            {
                output.WriteLine($"--- {asset.Category}: not selected by {CategoryFilterVariable}");
                continue;
            }

            output.WriteLine(string.Empty);
            output.WriteLine($"=== {asset.Category} / {asset.FixtureId}");
            Stopwatch clock = Stopwatch.StartNew();
            RegressionCaseResult result;
            try
            {
                result = await RunCaseAsync(services, asset, runFolder, workspaceRoot);
            }
            catch (Exception ex)
            {
                // An exception is a failure of this case, never of the run's ability to report.
                // A harness that threw here would leave six good cases unrecorded.
                result = new RegressionCaseResult(
                    asset.FixtureId, asset.Category, RegressionOutcome.Failed, asset.ExpectedWorkflow,
                    [], [], [], [], [new RegressionAssertion("caseCompleted", false, ex.Message)], [],
                    runFolder, "The case threw: " + Truncate(ex.Message, 300));
            }

            clock.Stop();
            result = result.Conclude();
            results.Add(result);
            output.WriteLine($"--- {asset.Category}: {result.Outcome} in {clock.Elapsed:mm\\:ss} — {result.Detail}");

            File.WriteAllText(Path.Combine(runFolder, $"case-{asset.FixtureId}.json"),
                System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
                }));
        }

        StandardRegressionSetRunResult run = StandardRegressionSetRunResult.From(
            set.SetId!, runId, Iso(startedAt), Iso(DateTimeOffset.Now), runFolder,
            workstation, ProductVersion(), configuration.Preset.Id, configuration.Preset.Version,
            configuration.Adapters.Mode, results, binding);

        WriteResult(runFolder, run);
        output.WriteLine(string.Empty);
        output.WriteLine(run.Verdict);
        output.WriteLine("Result: " + Path.Combine(runFolder, "result.json"));
    }

    // ==================================================================================
    // The cases
    // ==================================================================================
    private async Task<RegressionCaseResult> RunCaseAsync(
        ServiceProvider services, RegressionAssetManifest asset, string runFolder, string workspaceRoot) =>
        asset.Category switch
        {
            "NORMAL_JPG_PORTRAIT" => await RunEnhancementCaseAsync(services, asset, runFolder, workspaceRoot),
            "COMPLEX_BACKGROUND_FINE_HAIR" => await RunBackgroundRemovalCaseAsync(services, asset, runFolder, workspaceRoot),
            "TRANSPARENT_PNG" => await RunTransparentTrimCaseAsync(services, asset, runFolder, workspaceRoot),
            "COMPLETE_CUSTOMER_DESIGN" => await RunProductionTiffCaseAsync(
                services, asset, runFolder, workspaceRoot, WorkflowType.PrepareCustomerDesign, WhiteUnderbaseBranch.W1_1px),
            "PSD_WITH_COMPOSITE_PREVIEW" => await RunPreparedInputCaseAsync(
                services, asset, runFolder, workspaceRoot, OperationKind.PreparePsd, WhiteUnderbaseBranch.W1_2px),
            "SINGLE_PAGE_PDF" => await RunPreparedInputCaseAsync(
                services, asset, runFolder, workspaceRoot, OperationKind.PreparePdf, WhiteUnderbaseBranch.W1_2px),
            "REFERENCE_PRODUCTION_TIFF" => await RunReferenceTiffCaseAsync(services, asset, runFolder),
            _ => Blocked(asset.FixtureId, asset.Category,
                $"No runner is defined for category '{asset.Category}'."),
        };

    /// <summary>NORMAL_JPG_PORTRAIT — a real Meitu enhancement, then promotion.</summary>
    private async Task<RegressionCaseResult> RunEnhancementCaseAsync(
        ServiceProvider services, RegressionAssetManifest asset, string runFolder, string workspaceRoot)
    {
        ISessionService service = services.GetRequiredService<ISessionService>();
        ISessionRepository repository = services.GetRequiredService<ISessionRepository>();
        List<string> steps = [];
        List<RegressionAssertion> assertions = [];
        List<RegressionArtefact> artefacts = [];

        byte[] sourceBefore = File.ReadAllBytes(asset.SourcePath);

        SessionId id = Accept(await service.ImportAsync(
            WorkflowType.PrepareAsset, asset.SourcePath, asset.FixtureId, "regression", CancellationToken.None)).Id;
        steps.Add("Import");
        Accept(await service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(
            "Standard regression set: ordinary portrait."), "regression", CancellationToken.None));
        steps.Add("OriginalConfirmation");

        // This is the exact managed Revision offered to Enhancement. Import copied the source into
        // the attempt-owned workspace and inspected those bytes; resolving through the workflow
        // snapshot avoids substituting the fixture path, a literal size, or the later output.
        SessionAggregate beforeEnhancement = (await repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision? enhancementInput = beforeEnhancement.ToSnapshot().UpstreamRevisionOf(StepKind.Enhancement)
            is RevisionId inputId
                ? beforeEnhancement.Revisions.SingleOrDefault(r => r.Id == inputId)
                : null;

        SessionView enhanced = Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "regression", CancellationToken.None));
        steps.Add("Enhancement (Meitu)");

        assertions.Add(new RegressionAssertion("enhancementReachedReview",
            enhanced.CurrentStep?.State == StepState.ReviewRequired,
            $"Enhancement left the step at {enhanced.CurrentStep?.State}."));

        ArtefactView produced = enhanced.CurrentArtefact!;
        SessionAggregate afterEnhancement = (await repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision enhancementRevision = afterEnhancement.Revisions.Last(
            r => r.Operation == OperationKind.Enhance);
        assertions.Add(new RegressionAssertion("enhancedOutputIsPng",
            enhancementRevision.Facts.Format == ImageFormat.Png,
            $"Enhanced export is {enhancementRevision.Facts.Format}."));
        assertions.Add(EnhancedOutputSizeAssertion(
            asset,
            enhancementInput?.Facts.PixelWidth,
            enhancementInput?.Facts.PixelHeight,
            enhancementRevision.Facts.PixelWidth,
            enhancementRevision.Facts.PixelHeight));

        string enhancedPath = services.GetRequiredService<IWorkspace>().ResolveAbsolute(
            enhancementRevision.File);
        string copied = Copy(enhancedPath, runFolder, asset.FixtureId + "-enhanced.png");
        artefacts.Add(Artefact("enhancedExport", copied));

        Accept(await service.ExecuteAsync(id, new WorkflowCommand.Approve(
            StepKind.Enhancement, produced.Sha256, "Standard regression set baseline."),
            "regression", CancellationToken.None));
        steps.Add("Enhancement approved");

        Accept(await service.ExecuteAsync(id, new WorkflowCommand.Skip(
            StepKind.BackgroundRemoval, "Ordinary portrait on a plain backdrop; no cutout in this category."),
            "regression", CancellationToken.None));
        steps.Add("BackgroundRemoval skipped");

        // The enhanced export carries no real transparency, so there is nothing for the alpha
        // trim to measure and the recorded operator choice is to keep the original extent. This
        // is the manifest's stated path, not a workaround for a refusal.
        Accept(await service.ExecuteAsync(id, new WorkflowCommand.KeepOriginalExtent(),
            "regression", CancellationToken.None));
        steps.Add("Trim: KeepOriginalExtent");

        Accept(await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.ApprovedPngExport),
            "regression", CancellationToken.None));
        steps.Add("ApprovedPngExport");

        SessionAggregate final = (await repository.LoadAsync(id, CancellationToken.None)).Value!;
        assertions.Add(new RegressionAssertion("promotedRevisionExists",
            final.Revisions.Any(r => r.Operation == OperationKind.PromoteApproved),
            "A PromoteApproved Revision was recorded."));

        AddCommonAssertions(assertions, repository, asset, sourceBefore, out string lockDetail);
        assertions.Add(new RegressionAssertion("automationLockFree", lockDetail == "free", lockDetail));

        return new RegressionCaseResult(
            asset.FixtureId, asset.Category, RegressionOutcome.Pending, "PrepareAsset",
            [.. steps], ["Meitu XiuXiu"],
            [services.GetRequiredService<IMeituProcessor>().AdapterId],
            [.. artefacts], [.. assertions],
            [.. asset.ManualChecks.Select(c => new RegressionManualDecision(
                c.Id, c.Question, RegressionOutcome.Pending, null, null, copied, null))],
            runFolder,
            $"Meitu enhanced the portrait to {enhancementRevision.Facts.PixelWidth}x" +
            $"{enhancementRevision.Facts.PixelHeight} " +
            "and the approved bytes were promoted unchanged.");
    }

    /// <summary>COMPLEX_BACKGROUND_FINE_HAIR — a real Meitu background removal, then an alpha trim.</summary>
    private async Task<RegressionCaseResult> RunBackgroundRemovalCaseAsync(
        ServiceProvider services, RegressionAssetManifest asset, string runFolder, string workspaceRoot)
    {
        ISessionService service = services.GetRequiredService<ISessionService>();
        ISessionRepository repository = services.GetRequiredService<ISessionRepository>();
        IWorkspace workspace = services.GetRequiredService<IWorkspace>();
        List<string> steps = [];
        List<RegressionAssertion> assertions = [];
        List<RegressionArtefact> artefacts = [];

        byte[] sourceBefore = File.ReadAllBytes(asset.SourcePath);

        SessionId id = Accept(await service.ImportAsync(
            WorkflowType.PrepareAsset, asset.SourcePath, asset.FixtureId, "regression", CancellationToken.None)).Id;
        steps.Add("Import");
        Accept(await service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(
            "Standard regression set: fine hair against a complex background."),
            "regression", CancellationToken.None));
        steps.Add("OriginalConfirmation");

        Accept(await service.ExecuteAsync(id, new WorkflowCommand.Skip(
            StepKind.Enhancement, "Enhancing first would change what the cutout is judged on."),
            "regression", CancellationToken.None));
        steps.Add("Enhancement skipped");

        // Background removal runs only over content a decision was recorded against, bound to the
        // exact revision and hash on offer.
        (RevisionId Id, Sha256 Sha256) upstream =
            (await repository.LoadAsync(id, CancellationToken.None)).Value!
                .ToSnapshot().UpstreamResultOf(StepKind.BackgroundRemoval)!.Value;
        Accept(await service.ExecuteAsync(id, new WorkflowCommand.SetBackgroundRemovalDecision(
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent, upstream.Id, upstream.Sha256),
            "regression", CancellationToken.None));
        steps.Add("BackgroundRemoval authorised");

        SessionView cutout = Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.BackgroundRemoval), "regression", CancellationToken.None));
        steps.Add("BackgroundRemoval (Meitu)");

        assertions.Add(new RegressionAssertion("backgroundRemovalReachedReview",
            cutout.CurrentStep?.State == StepState.ReviewRequired,
            $"Background removal left the step at {cutout.CurrentStep?.State}."));

        ArtefactView produced = cutout.CurrentArtefact!;
        assertions.Add(new RegressionAssertion("cutoutIsPng",
            produced.Facts.Format == ImageFormat.Png, $"Cutout is {produced.Facts.Format}."));
        assertions.Add(new RegressionAssertion("cutoutHasRealTransparency",
            produced.Facts.HasAlpha == true,
            $"Cutout HasAlpha = {produced.Facts.HasAlpha?.ToString() ?? "(unstated)"}."));

        string cutoutPath = workspace.ResolveAbsolute(
            (await repository.LoadAsync(id, CancellationToken.None)).Value!
                .Revisions.Last(r => r.Operation == OperationKind.RemoveBackground).File);
        string copied = Copy(cutoutPath, runFolder, asset.FixtureId + "-cutout.png");
        artefacts.Add(Artefact("cutoutExport", copied));

        Accept(await service.ExecuteAsync(id, new WorkflowCommand.Approve(
            StepKind.BackgroundRemoval, produced.Sha256, "Standard regression set baseline."),
            "regression", CancellationToken.None));
        steps.Add("BackgroundRemoval approved");

        SessionView trimmed = Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Trim), "regression", CancellationToken.None));
        steps.Add("Trim (internal alpha bounds)");

        ArtefactView trimmedArtefact = trimmed.CurrentArtefact!;
        SessionAggregate afterTrim = (await repository.LoadAsync(id, CancellationToken.None)).Value!;
        assertions.Add(FineHairTrimAssertion(
            asset, produced.Facts, trimmedArtefact.Facts,
            afterTrim.Attempts, afterTrim.Revisions, workspace.ResolveAbsolute));
        assertions.Add(new RegressionAssertion("trimmedOutputRetainsAlpha",
            trimmedArtefact.Facts.HasAlpha == true,
            $"Trimmed output HasAlpha = {trimmedArtefact.Facts.HasAlpha?.ToString() ?? "(unstated)"}."));

        artefacts.Add(Artefact("trimmedExport", Copy(
            workspace.ResolveAbsolute((await repository.LoadAsync(id, CancellationToken.None)).Value!
                .Revisions.Last(r => r.Operation == OperationKind.Trim).File),
            runFolder, asset.FixtureId + "-trimmed.png")));

        Accept(await service.ExecuteAsync(id, new WorkflowCommand.Approve(
            StepKind.Trim, trimmedArtefact.Sha256, "Standard regression set baseline."),
            "regression", CancellationToken.None));
        Accept(await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.ApprovedPngExport),
            "regression", CancellationToken.None));
        steps.Add("ApprovedPngExport");

        AddCommonAssertions(assertions, repository, asset, sourceBefore, out string lockDetail);
        assertions.Add(new RegressionAssertion("automationLockFree", lockDetail == "free", lockDetail));

        return new RegressionCaseResult(
            asset.FixtureId, asset.Category, RegressionOutcome.Pending, "PrepareAsset",
            [.. steps], ["Meitu XiuXiu"],
            [services.GetRequiredService<IMeituProcessor>().AdapterId],
            [.. artefacts], [.. assertions],
            [.. asset.ManualChecks.Select(c => new RegressionManualDecision(
                c.Id, c.Question, RegressionOutcome.Pending, null, null, copied, null))],
            runFolder,
            $"Meitu removed the background and the alpha trim cropped to " +
            $"{trimmedArtefact.Facts.PixelWidth}x{trimmedArtefact.Facts.PixelHeight}.");
    }

    /// <summary>TRANSPARENT_PNG — no external application; the deterministic half of the set.</summary>
    private async Task<RegressionCaseResult> RunTransparentTrimCaseAsync(
        ServiceProvider services, RegressionAssetManifest asset, string runFolder, string workspaceRoot)
    {
        ISessionService service = services.GetRequiredService<ISessionService>();
        ISessionRepository repository = services.GetRequiredService<ISessionRepository>();
        IWorkspace workspace = services.GetRequiredService<IWorkspace>();
        List<string> steps = [];
        List<RegressionAssertion> assertions = [];

        byte[] sourceBefore = File.ReadAllBytes(asset.SourcePath);

        SessionId id = Accept(await service.ImportAsync(
            WorkflowType.PrepareAsset, asset.SourcePath, asset.FixtureId, "regression", CancellationToken.None)).Id;
        steps.Add("Import");
        Accept(await service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(
            "Standard regression set: already-approved transparent artwork."),
            "regression", CancellationToken.None));
        steps.Add("OriginalConfirmation");

        Accept(await service.ExecuteAsync(id, new WorkflowCommand.Skip(
            StepKind.Enhancement, "Already an approved export."), "regression", CancellationToken.None));
        Accept(await service.ExecuteAsync(id, new WorkflowCommand.Skip(
            StepKind.BackgroundRemoval, "The background is already removed."),
            "regression", CancellationToken.None));
        steps.Add("Enhancement and BackgroundRemoval skipped");

        SessionView trimmed = Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Trim), "regression", CancellationToken.None));
        steps.Add("Trim (internal alpha bounds)");

        ArtefactView produced = trimmed.CurrentArtefact!;

        // The exact expectation the manifest states. These bounds were recorded against these
        // exact bytes when the cutout was accepted in August, so they are arithmetic rather than
        // a judgement, and this is the one case in the set that says so.
        assertions.Add(new RegressionAssertion("trimBoundsExact",
            produced.Facts.PixelWidth == 2724 && produced.Facts.PixelHeight == 3685,
            $"Trim produced {produced.Facts.PixelWidth}x{produced.Facts.PixelHeight}; " +
            "the manifest expects 2724x3685 from the recorded non-empty alpha bounds."));
        assertions.Add(new RegressionAssertion("trimmedOutputRetainsAlpha",
            produced.Facts.HasAlpha == true,
            $"Trimmed output HasAlpha = {produced.Facts.HasAlpha?.ToString() ?? "(unstated)"}."));

        string copied = Copy(workspace.ResolveAbsolute(
            (await repository.LoadAsync(id, CancellationToken.None)).Value!
                .Revisions.Last(r => r.Operation == OperationKind.Trim).File),
            runFolder, asset.FixtureId + "-trimmed.png");

        Accept(await service.ExecuteAsync(id, new WorkflowCommand.Approve(
            StepKind.Trim, produced.Sha256, "Standard regression set baseline."),
            "regression", CancellationToken.None));
        Accept(await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.ApprovedPngExport),
            "regression", CancellationToken.None));
        steps.Add("ApprovedPngExport");

        AddCommonAssertions(assertions, repository, asset, sourceBefore, out string lockDetail);
        assertions.Add(new RegressionAssertion("automationLockFree", lockDetail == "free", lockDetail));

        return new RegressionCaseResult(
            asset.FixtureId, asset.Category, RegressionOutcome.Pending, "PrepareAsset",
            [.. steps], [], ["internal-trim"],
            [Artefact("trimmedExport", copied)], [.. assertions], [],
            runFolder,
            $"Deterministic alpha trim produced the expected {produced.Facts.PixelWidth}x{produced.Facts.PixelHeight}.");
    }

    /// <summary>COMPLETE_CUSTOMER_DESIGN — a finished design through to a real production TIFF.</summary>
    private async Task<RegressionCaseResult> RunProductionTiffCaseAsync(
        ServiceProvider services,
        RegressionAssetManifest asset,
        string runFolder,
        string workspaceRoot,
        WorkflowType workflow,
        WhiteUnderbaseBranch branch)
    {
        ISessionService service = services.GetRequiredService<ISessionService>();
        ISessionRepository repository = services.GetRequiredService<ISessionRepository>();
        List<string> steps = [];
        List<RegressionAssertion> assertions = [];

        byte[] sourceBefore = File.ReadAllBytes(asset.SourcePath);

        SessionId id = Accept(await service.ImportAsync(
            workflow, asset.SourcePath, asset.FixtureId, "regression", CancellationToken.None)).Id;
        steps.Add("Import");
        Accept(await service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(
            "Standard regression set: complete customer design."), "regression", CancellationToken.None));
        steps.Add("OriginalConfirmation");

        Accept(await service.ExecuteAsync(id, new WorkflowCommand.Skip(
            StepKind.Enhancement, "The design is finished; the accepted enhanced reference already exists."),
            "regression", CancellationToken.None));
        Accept(await service.ExecuteAsync(id, new WorkflowCommand.Skip(
            StepKind.BackgroundRemoval, "A finished design may intentionally keep its background."),
            "regression", CancellationToken.None));
        steps.Add("Enhancement and BackgroundRemoval skipped");

        Accept(await service.ExecuteAsync(id, new WorkflowCommand.KeepOriginalExtent(),
            "regression", CancellationToken.None));
        steps.Add("Trim: KeepOriginalExtent (opaque source, no alpha to measure)");

        return await ProducePrintTiffAsync(
            services, service, repository, asset, id, branch, steps, assertions, sourceBefore,
            runFolder, workflow.ToString());
    }

    /// <summary>PSD_WITH_COMPOSITE_PREVIEW and SINGLE_PAGE_PDF — prepared input, then a real TIFF.</summary>
    private async Task<RegressionCaseResult> RunPreparedInputCaseAsync(
        ServiceProvider services,
        RegressionAssetManifest asset,
        string runFolder,
        string workspaceRoot,
        OperationKind preparation,
        WhiteUnderbaseBranch branch)
    {
        ISessionService service = services.GetRequiredService<ISessionService>();
        ISessionRepository repository = services.GetRequiredService<ISessionRepository>();
        List<string> steps = [];
        List<RegressionAssertion> assertions = [];

        byte[] sourceBefore = File.ReadAllBytes(asset.SourcePath);

        SessionId id = Accept(await service.ImportAsync(
            WorkflowType.GeneratePrintTiff, asset.SourcePath, asset.FixtureId, "regression", CancellationToken.None)).Id;
        steps.Add("Import");

        SessionView prepared = Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.OriginalConfirmation), "regression", CancellationToken.None));
        steps.Add(preparation == OperationKind.PreparePsd
            ? "OriginalConfirmation → PreparePsd (Photoshop)"
            : "OriginalConfirmation → PreparePdf (Windows.Data.Pdf)");

        SessionAggregate afterPreparation = (await repository.LoadAsync(id, CancellationToken.None)).Value!;
        ProcessingAttempt attempt = afterPreparation.Attempts.Last(a => a.Operation == preparation);
        Revision raster = afterPreparation.Revisions.Last(r => r.Operation == preparation);

        assertions.Add(new RegressionAssertion("preparationReachedReview",
            prepared.CurrentStep?.State == StepState.ReviewRequired,
            $"Preparation left the step at {prepared.CurrentStep?.State}."));
        assertions.Add(new RegressionAssertion("preparedRasterIsPng",
            raster.Facts.Format == ImageFormat.Png, $"Managed raster is {raster.Facts.Format}."));

        if (preparation == OperationKind.PreparePsd)
        {
            assertions.Add(new RegressionAssertion("psdCompositeAccepted",
                attempt.PsdInspection is not null && attempt.PsdInspection.HasRealMergedData,
                $"PsdInspection.HasRealMergedData = " +
                $"{attempt.PsdInspection?.HasRealMergedData.ToString() ?? "(no inspection)"}."));
            assertions.Add(new RegressionAssertion("preparedRasterIsRgb",
                raster.Facts.ColourMode == ColourMode.Rgb, $"Managed raster is {raster.Facts.ColourMode}."));
        }
        else
        {
            assertions.Add(new RegressionAssertion("pdfIsSinglePage",
                attempt.PdfInspection?.PageCount == 1,
                $"PdfInspection.PageCount = {attempt.PdfInspection?.PageCount?.ToString() ?? "(no inspection)"}."));
            assertions.Add(new RegressionAssertion("pdfPreparedPageOne",
                attempt.PdfInspection?.IsPreparedSinglePage == true,
                "PdfInspection.IsPreparedSinglePage."));
            assertions.Add(new RegressionAssertion("pdfRasterMatchesPageGeometry",
                raster.Facts.PixelWidth == 1500 && raster.Facts.PixelHeight == 2000,
                $"Raster is {raster.Facts.PixelWidth}x{raster.Facts.PixelHeight}; the 480x640 DIP page " +
                "at 300 PPI is 1500x2000."));
        }

        Accept(await service.ExecuteAsync(id, new WorkflowCommand.Approve(
            StepKind.OriginalConfirmation, prepared.CurrentArtefact!.Sha256,
            "Standard regression set baseline."), "regression", CancellationToken.None));
        steps.Add("Prepared raster approved");

        return await ProducePrintTiffAsync(
            services, service, repository, asset, id, branch, steps, assertions, sourceBefore,
            runFolder, "GeneratePrintTiff");
    }

    /// <summary>The shared tail: size, underbase branch, and the real Photoshop TIFF.</summary>
    private async Task<RegressionCaseResult> ProducePrintTiffAsync(
        ServiceProvider services,
        ISessionService service,
        ISessionRepository repository,
        RegressionAssetManifest asset,
        SessionId id,
        WhiteUnderbaseBranch branch,
        List<string> steps,
        List<RegressionAssertion> assertions,
        byte[] sourceBefore,
        string runFolder,
        string workflow)
    {
        Accept(await service.ExecuteAsync(id, new WorkflowCommand.SetPrintDimensions(ProductionBox),
            "regression", CancellationToken.None));
        steps.Add($"PrintDimensions {ProductionBox}");

        Accept(await service.ExecuteAsync(id, new WorkflowCommand.SelectWhiteUnderbaseBranch(
            branch, "Standard regression set: classification recorded by the set, not inferred."),
            "regression", CancellationToken.None));
        steps.Add($"WhiteUnderbaseBranch {branch}");

        SessionView produced = Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "regression", CancellationToken.None));
        steps.Add("PhotoshopOutput (Photoshop, signed Action, CMYK + W1, TIFF validation)");

        assertions.Add(new RegressionAssertion("photoshopOutputReachedReview",
            produced.CurrentStep?.State == StepState.ReviewRequired,
            $"PhotoshopOutput left the step at {produced.CurrentStep?.State}."));

        SessionAggregate final = (await repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision tiff = final.Revisions.Last(r => r.Operation == OperationKind.PhotoshopOutput);
        string tiffPath = services.GetRequiredService<IWorkspace>().ResolveAbsolute(tiff.File);

        assertions.Add(new RegressionAssertion("productionTiffExists",
            File.Exists(tiffPath), $"Validated output at '{tiffPath}'."));
        assertions.Add(new RegressionAssertion("productionTiffIsTiff",
            tiff.Facts.Format == ImageFormat.Tiff, $"Produced revision is {tiff.Facts.Format}."));

        TiffStructure structure = TiffStructure.Read(tiffPath);
        assertions.Add(new RegressionAssertion("tiffSamplesPerPixel",
            structure.SamplesPerPixel == 5, $"SamplesPerPixel = {structure.SamplesPerPixel}; expected 5 (CMYK + W1)."));
        assertions.Add(new RegressionAssertion("tiffPhotometricSeparated",
            structure.PhotometricInterpretation == 5,
            $"PhotometricInterpretation = {structure.PhotometricInterpretation}; expected 5 (separated)."));
        assertions.Add(new RegressionAssertion("tiffUncompressed",
            structure.Compression == 1, $"Compression = {structure.Compression}; expected 1 (none)."));
        assertions.Add(new RegressionAssertion("tiffResolution300",
            Math.Abs(structure.XResolution - 300) < 0.01 && Math.Abs(structure.YResolution - 300) < 0.01,
            $"Resolution = {structure.XResolution}x{structure.YResolution} dpi; expected 300x300."));

        AddCommonAssertions(assertions, repository, asset, sourceBefore, out string lockDetail);
        assertions.Add(new RegressionAssertion("automationLockFree", lockDetail == "free", lockDetail));

        // The TIFF itself is not copied into the run folder: these run to tens or hundreds of
        // megabytes each and the evidence needs to be reproducible, not duplicated. Its workspace
        // path and hash are recorded, which is what an auditor opens.
        RegressionArtefact artefact = Artefact("productionTiff", tiffPath);

        return new RegressionCaseResult(
            asset.FixtureId, asset.Category, RegressionOutcome.Pending, workflow,
            [.. steps], ["Adobe Photoshop CC 2019 20.0.10"],
            [services.GetRequiredService<IPhotoshopOutputProcessor>().AdapterId],
            [artefact], [.. assertions],
            [.. asset.ManualChecks.Select(c => new RegressionManualDecision(
                c.Id, c.Question, RegressionOutcome.Pending, null, null, tiffPath, null))],
            runFolder,
            $"Produced a validated {structure.Width}x{structure.Height} separated TIFF with " +
            $"{structure.SamplesPerPixel} samples at {structure.XResolution} dpi.");
    }

    /// <summary>REFERENCE_PRODUCTION_TIFF — structure verified, and the refusal asserted.</summary>
    private async Task<RegressionCaseResult> RunReferenceTiffCaseAsync(
        ServiceProvider services, RegressionAssetManifest asset, string runFolder)
    {
        ISessionService service = services.GetRequiredService<ISessionService>();
        ISessionRepository repository = services.GetRequiredService<ISessionRepository>();
        List<RegressionAssertion> assertions = [];

        TiffStructure structure = TiffStructure.Read(asset.SourcePath);
        assertions.Add(new RegressionAssertion("referenceTiffDimensions",
            structure.Width == 3307 && structure.Height == 4474,
            $"{structure.Width}x{structure.Height}; the preset's tiffContract records 3307x4474."));
        assertions.Add(new RegressionAssertion("referenceTiffSeparatedCmykPlusW1",
            structure.SamplesPerPixel == 5 && structure.PhotometricInterpretation == 5,
            $"SamplesPerPixel = {structure.SamplesPerPixel}, Photometric = {structure.PhotometricInterpretation}."));
        assertions.Add(new RegressionAssertion("referenceTiffUncompressed",
            structure.Compression == 1, $"Compression = {structure.Compression}."));
        assertions.Add(new RegressionAssertion("referenceTiffResolution300",
            Math.Abs(structure.XResolution - 300) < 0.01 && Math.Abs(structure.YResolution - 300) < 0.01,
            $"{structure.XResolution}x{structure.YResolution} dpi."));

        // The refusal is the regression, not a formality. TIFF is an output of this product and
        // SupportedInputFormats deliberately excludes it; a build that started accepting one
        // would create sessions that could only fail several steps later.
        OperationResult<SessionView> imported = await service.ImportAsync(
            WorkflowType.GeneratePrintTiff, asset.SourcePath, asset.FixtureId, "regression", CancellationToken.None);

        assertions.Add(new RegressionAssertion("referenceTiffRefusedAsHomeInput",
            imported.IsFailure && imported.Failure.Code == FailureCode.SourceFormatUnsupported,
            imported.IsFailure
                ? $"Import refused with {imported.Failure.Code}."
                : "Import SUCCEEDED. TIFF must not be an accepted PrintFlow input."));

        assertions.Add(new RegressionAssertion("referenceTiffCreatedNoSession",
            (await repository.ListRecentAsync(50, DateTimeOffset.Now, CancellationToken.None)).Value
                .All(s => !string.Equals(s.OutputName.ToString(), asset.FixtureId, StringComparison.OrdinalIgnoreCase)),
            "No session was created for the refused TIFF."));

        return new RegressionCaseResult(
            asset.FixtureId, asset.Category, RegressionOutcome.Pending,
            "None — reference artefact only",
            ["TIFF IFD read directly", "Home import attempted and refused"],
            [], [],
            [new RegressionArtefact("referenceTiff", asset.SourcePath, asset.Sha256, asset.Length)],
            [.. assertions], [], runFolder,
            $"The Maintop-proven reference is structurally intact ({structure.Width}x{structure.Height}, " +
            $"{structure.SamplesPerPixel} samples) and is still refused as a Home input.");
    }

    // ==================================================================================
    // Shared
    // ==================================================================================
    /// <summary>The v2 portrait size expectation consumed by the actual workstation caller.</summary>
    internal static RegressionAssertion EnhancedOutputSizeAssertion(
        RegressionAssetManifest asset,
        int? inputWidth,
        int? inputHeight,
        int? outputWidth,
        int? outputHeight)
    {
        const string name = "enhancedOutputIsNotSmallerThanSource";

        if (asset.EnhancedOutputIsNotSmallerThanSource.Value is not true ||
            asset.EnhancedOutputIsLargerThanSource.Present)
        {
            return new RegressionAssertion(name, false,
                "The loaded portrait manifest does not carry the unambiguous v2 " +
                "enhancedOutputIsNotSmallerThanSource=true expectation.");
        }

        if (inputWidth is not > 0 || inputHeight is not > 0 ||
            outputWidth is not > 0 || outputHeight is not > 0)
        {
            return new RegressionAssertion(name, false,
                $"Decoded dimensions are required: input {inputWidth?.ToString() ?? "(missing)"}x" +
                $"{inputHeight?.ToString() ?? "(missing)"}; output " +
                $"{outputWidth?.ToString() ?? "(missing)"}x{outputHeight?.ToString() ?? "(missing)"}.");
        }

        bool held = outputWidth.Value >= inputWidth.Value && outputHeight.Value >= inputHeight.Value;
        return new RegressionAssertion(name, held,
            $"Managed pre-Enhancement input is {inputWidth}x{inputHeight}; decoded Enhancement " +
            $"Revision is {outputWidth}x{outputHeight}. Required: output width >= input width AND " +
            "output height >= input height.");
    }

    /// <summary>The fine-hair structural trim expectation consumed by the actual workstation caller.</summary>
    /// <remarks>
    /// Chosen by the set the manifest belongs to, never inferred. v3 verifies the exact
    /// alpha-bounds/margin/crop contract against the session's own attempt and files; every other
    /// set keeps its frozen strict-shrink assertion byte for byte, so a v1/v2 run is never judged by
    /// v3's changed expectation.
    /// </remarks>
    internal static RegressionAssertion FineHairTrimAssertion(
        RegressionAssetManifest asset,
        FileFacts cutout,
        FileFacts trimmed,
        IReadOnlyList<ProcessingAttempt> attempts,
        IReadOnlyList<Revision> revisions,
        Func<WorkspaceFileRef, string> resolve) =>
        string.Equals(asset.SetId, RegressionTrimGeometry.SetId, StringComparison.Ordinal)
            ? RegressionTrimGeometry.Verify(asset, cutout.Sha256, trimmed.Sha256, attempts, revisions, resolve)
            : new RegressionAssertion("trimBoundsInsideCanvas",
                trimmed.PixelWidth < cutout.PixelWidth ||
                trimmed.PixelHeight < cutout.PixelHeight,
                $"Trim produced {trimmed.PixelWidth}x{trimmed.PixelHeight} " +
                $"from {cutout.PixelWidth}x{cutout.PixelHeight}.");

    private static void AddCommonAssertions(
        List<RegressionAssertion> assertions,
        ISessionRepository repository,
        RegressionAssetManifest asset,
        byte[] sourceBefore,
        out string lockDetail)
    {
        byte[] after = File.ReadAllBytes(asset.SourcePath);
        assertions.Add(new RegressionAssertion("sourceBytesUnchanged",
            after.AsSpan().SequenceEqual(sourceBefore),
            $"'{Path.GetFileName(asset.SourcePath)}' is byte-identical after the run."));

        bool held = repository.GetAutomationLockAsync(CancellationToken.None).GetAwaiter().GetResult().Value.IsHeld;
        lockDetail = held ? "The global automation lock is still held after the case finished." : "free";
    }

    private static RegressionCaseResult Blocked(string assetId, string category, string detail) =>
        new(assetId, category, RegressionOutcome.Blocked, null, [], [], [], [], [], [], null, detail);

    private static SessionView Accept(OperationResult<SessionView> result) =>
        result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException(result.Failure.ToString());

    private static RegressionArtefact Artefact(string role, string path)
    {
        FileInfo file = new(path);
        return new RegressionArtefact(role, path,
            file.Exists ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : null,
            file.Exists ? file.Length : 0);
    }

    private static string Copy(string from, string runFolder, string name)
    {
        string to = Path.Combine(runFolder, name);
        File.Copy(from, to, overwrite: true);
        return to;
    }

    private static int Order(string category) =>
        StandardRegressionCategories.Required.IndexOf(category) is var index && index >= 0 ? index : int.MaxValue;

    private static string Iso(DateTimeOffset moment) => moment.ToString("o");

    private static string Truncate(string text, int limit) =>
        text.Length <= limit ? text : string.Concat(text.AsSpan(0, limit - 1), "…");

    private static string ProductVersion()
    {
        Version? version = typeof(ServiceRegistration).Assembly.GetName().Version;
        return version is null ? "(unreadable)" : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    /// <summary>
    /// Captures what this run is testing, at the time it tests it (PF-AUDIT-R1, finding F3).
    /// </summary>
    /// <remarks>
    /// <b>Read here and nowhere later.</b> The defect this closes was a writer that read the
    /// machine at publication time and paired those facts with a status it had copied from a run.
    /// Every fact below is read by the process that is about to drive the set, so a record built
    /// from it describes an environment something actually ran in. A fact that cannot be read is
    /// left null and reported; nothing is filled in from the machine afterwards.
    /// <para>
    /// <b>Harness and candidate.</b> The harness is the PrintFlow code this test host loaded — the
    /// bytes actually exercised. The candidate is the installation the resulting record will speak
    /// for. They are not the same bytes even for one commit, because an installation carries a
    /// RID-specific self-contained publish, so the controlled receipt checks each against its own
    /// recorded bytes before this method. Labels remain diagnostic only. A candidate that cannot
    /// be read is recorded as a problem rather than
    /// quietly omitted: a run whose candidate is unbound can still produce evidence of what
    /// happened, but no publication may follow from it.
    /// </para>
    /// </remarks>
    private static RegressionEvidenceBinding Bind(
        StandardRegressionSet set,
        PrintFlowConfiguration configuration,
        string invocationId,
        RegressionBuildOrigin? origin,
        bool usesMeituOverride = false)
    {
        ImmutableArray<ProductAssemblyIdentity> harness = ProductBuildIdentity.Running();

        string candidateFolder = Environment.GetEnvironmentVariable(CandidateVariable) is { } named &&
            !string.IsNullOrWhiteSpace(named)
                ? named
                : DefaultCandidateInstallFolder;

        List<string> candidateProblems = [];
        if (usesMeituOverride)
        {
            candidateProblems.Add(RegressionMeituOverride.PublicationProblem);
        }
        if (origin is null)
        {
            candidateProblems.Add("Diagnostic unbound run: no controlled build-pair association; publication is forbidden.");
        }
        ImmutableArray<ProductAssemblyIdentity> candidate = Directory.Exists(candidateFolder)
            ? ProductBuildIdentity.FromFolder(candidateFolder)
            : [];

        if (!Directory.Exists(candidateFolder))
        {
            candidateProblems.Add(
                $"No installation at '{candidateFolder}', so this run attests no installed " +
                "candidate. Its evidence stands, but no revalidation can be published from it.");
        }
        else
        {
            candidateProblems.AddRange(
                ProductBuildIdentity.CompareBuildIdentity(candidate, harness)
                    .Select(difference =>
                        "The installed candidate was not built from the source this run exercised. " +
                        difference));
        }

        string workspaceRoot = Path.GetFullPath(configuration.Workspace.Root);
        string presetPath = Path.Combine(workspaceRoot, configuration.Preset.Path);
        string? presetSha = File.Exists(presetPath)
            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(presetPath)))
            : null;

        // The accepted external-binary digests come from the preset through the same reader the
        // application uses, so the run binds the identities the gate enforces rather than a second
        // reading of the same JSON.
        string? meitu = null;
        string? photoshop = null;
        if (presetSha is null)
        {
            candidateProblems.Add(
                $"The configured preset manifest '{presetPath}' could not be read, so this run " +
                "established neither its digest nor the accepted external-binary identities.");
        }
        else
        {
            OperationResult<WorkstationRequirements> requirements = PresetWorkstationRequirements.Read(
                presetPath, configuration.Preset.Id, configuration.Preset.Version,
                Sha256.Parse(configuration.Preset.ExpectedSha256));

            if (requirements.IsSuccess)
            {
                meitu = requirements.Value.Meitu.Sha256.ToString();
                photoshop = requirements.Value.Photoshop.Sha256.ToString();
            }
            else
            {
                // Recorded as a problem, not only narrated. A fact this run failed to establish must
                // reach the writer as a refusal; leaving it null and mentioning it in the log is how
                // the publication step comes to fill it in from the machine instead.
                candidateProblems.Add(
                    "The accepted preset could not be read, so this run established no accepted " +
                    $"Meitu or Photoshop identity: {requirements.Failure}");
            }
        }

        ImmutableArray<RegressionSetManifestIdentity> manifests =
        [
            .. set.Assets
                .Where(a => a.ManifestSha256 is not null)
                .OrderBy(a => a.FixtureId, StringComparer.OrdinalIgnoreCase)
                .Select(a => new RegressionSetManifestIdentity(
                    a.FixtureId, a.Category, a.ManifestSha256!, a.Sha256)),
        ];

        return new RegressionEvidenceBinding(
            RegressionEvidenceBinding.CurrentVersion,
            invocationId,
            harness,
            typeof(StandardRegressionSetWorkstationSmoke).Assembly.GetName().Name,
            candidateFolder,
            candidate,
            [.. candidateProblems],
            presetSha,
            Environment.OSVersion.Version.Build.ToString(System.Globalization.CultureInfo.InvariantCulture),
            meitu,
            photoshop,
            RegressionEvidenceBinding.DigestOfSet(manifests),
            manifests, origin);
    }

    private static void WriteResult(string runFolder, StandardRegressionSetRunResult run) =>
        File.WriteAllText(Path.Combine(runFolder, "result.json"), run.ToJson());

    /// <summary>One operator's answers to the qualitative checks a run left open.</summary>
    /// <param name="DecidedBy">Who looked. Required, and written into every decision it carries.</param>
    /// <param name="DecidedAtLocal">When, if the file states it; otherwise the moment of merging.</param>
    /// <param name="Decisions">One entry per manual-check id.</param>
    /// <param name="Synthetic">
    /// Set by a test of this protocol, and by nothing else. A synthetic decision is recorded as
    /// synthetic in the run's review history: this path does not invent a human reviewer, and a
    /// decision made by a test must never read later as one made by a person.
    /// </param>
    private sealed record RecordedVisualReview(
        string DecidedBy,
        string? DecidedAtLocal,
        ImmutableArray<RecordedVisualDecision> Decisions,
        bool Synthetic = false);

    private sealed record RecordedVisualDecision(string Id, string Outcome, string? Notes);

    private static T Read<T>(string path)
    {
        string json = File.ReadAllText(path);
        return System.Text.Json.JsonSerializer.Deserialize<T>(json, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        }) ?? throw new InvalidOperationException($"'{path}' did not deserialise to {typeof(T).Name}.");
    }

    private static PrintFlowConfiguration LoadConfiguration()
    {
        DirectoryInfo? repository = new(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "PrintFlowStudio.sln")))
        {
            repository = repository.Parent;
        }

        return PrintFlowConfiguration.LoadFromFile(
            Path.Combine(repository!.FullName, "appsettings.json"));
    }
}
