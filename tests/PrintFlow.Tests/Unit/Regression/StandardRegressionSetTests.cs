using System.Collections.Immutable;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Verification;
using PrintFlow.Tests.Regression;

namespace PrintFlow.Tests.Unit.Regression;

/// <summary>
/// The standard local regression set's own contract (SCRUM-11065).
/// </summary>
/// <remarks>
/// These are about the set machinery, not about the workstation: they build sets in a temporary
/// folder and never touch <c>D:\PrintFlowStudio\TestData\v1</c>, never open an application and
/// never need the accepted preset. The fixed workstation's own run is
/// <c>StandardRegressionSetWorkstationSmoke</c> and is opt-in.
/// <para>
/// What is worth testing here is the machinery that decides whether a run may be believed:
/// whether an incomplete set can report itself complete, whether a replaced input can pass as
/// the original, and whether a skipped case can end up inside a green result.
/// </para>
/// </remarks>
public sealed class StandardRegressionSetTests
{
    // ------------------------------------------------------------------------------------------
    // The vocabulary the gate depends on
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The seven categories are spelled identically in the runner and in the revalidation script.
    /// </summary>
    /// <remarks>
    /// Two independent lists decide whether Production reopens: this one, and the
    /// <c>$RequiredCategories</c> array in <c>Set-PrintFlowProductionRevalidation.ps1</c>. A set
    /// satisfying one spelling and not the other would run green and be recorded as incomplete.
    /// The script is read as text on purpose — it is PowerShell and cannot be referenced.
    /// </remarks>
    [Fact]
    public void The_required_categories_match_the_revalidation_script_exactly()
    {
        string script = ReadRepositoryFile(@"tools\installer\Set-PrintFlowProductionRevalidation.ps1");

        foreach (string category in StandardRegressionCategories.Required)
        {
            script.Contains($"'{category}'", StringComparison.Ordinal)
                .ShouldBeTrue($"Set-PrintFlowProductionRevalidation.ps1 must require {category}.");
        }

        StandardRegressionCategories.Required.Length.ShouldBe(7);
        StandardRegressionCategories.Required.Distinct(StringComparer.Ordinal).Count().ShouldBe(7);
    }

    // ------------------------------------------------------------------------------------------
    // Loading and validating a set
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void A_complete_set_validates()
    {
        using TemporarySet set = TemporarySet.Complete();

        StandardRegressionSet loaded = StandardRegressionSet.Load(set.Root);

        loaded.Assets.Length.ShouldBe(7);
        loaded.SetId.ShouldBe("printflow-regression-v1");
        loaded.Validate().ShouldBeEmpty();
    }

    [Fact]
    public void Every_manifest_records_an_expected_processing_path_and_a_comparison_mode()
    {
        using TemporarySet set = TemporarySet.Complete();

        foreach (RegressionAssetManifest asset in StandardRegressionSet.Load(set.Root).Assets)
        {
            asset.ExpectedProcessingPath.ShouldNotBeEmpty();
            asset.ExpectedWorkflow.ShouldNotBe("(unstated)");
            asset.ComparisonMode.ShouldNotBe("(unstated)");
        }
    }

    /// <summary>A set missing one of the seven is not a set, however good the other six are.</summary>
    [Theory]
    [InlineData("NORMAL_JPG_PORTRAIT")]
    [InlineData("COMPLEX_BACKGROUND_FINE_HAIR")]
    [InlineData("TRANSPARENT_PNG")]
    [InlineData("COMPLETE_CUSTOMER_DESIGN")]
    [InlineData("PSD_WITH_COMPOSITE_PREVIEW")]
    [InlineData("SINGLE_PAGE_PDF")]
    [InlineData("REFERENCE_PRODUCTION_TIFF")]
    public void A_missing_category_invalidates_the_set(string missing)
    {
        using TemporarySet set = TemporarySet.Complete();
        set.Remove(missing);

        ImmutableArray<RegressionSetProblem> problems = StandardRegressionSet.Load(set.Root).Validate();

        problems.ShouldContain(p => p.Detail.Contains($"Required category {missing} is absent"));
    }

    /// <summary>
    /// Seven categories cannot be satisfied by labelling one file twice.
    /// </summary>
    /// <remarks>
    /// The set's whole value is that each category exercises something different. Two manifests
    /// claiming the same category means one of the seven is covered twice and, in practice, that
    /// another was filled by a file chosen to make a checker happy.
    /// </remarks>
    [Fact]
    public void Two_assets_cannot_claim_the_same_category()
    {
        using TemporarySet set = TemporarySet.Complete();
        set.Remove("SINGLE_PAGE_PDF");
        set.Add("FIX-DUPLICATE-001", "TRANSPARENT_PNG");

        ImmutableArray<RegressionSetProblem> problems = StandardRegressionSet.Load(set.Root).Validate();

        problems.ShouldContain(p => p.Detail.Contains("claimed by 2 assets"));
        problems.ShouldContain(p => p.Detail.Contains("Required category SINGLE_PAGE_PDF is absent"));
    }

    /// <summary>
    /// The hash is recomputed from the bytes, so a replaced input cannot pass as the original.
    /// </summary>
    /// <remarks>
    /// This is the property that makes the set usable for upgrade regression at all. A validator
    /// that matched on file name would accept a quietly re-exported JPEG as the accepted one, and
    /// every "the set still passes" claim after that would be about a different set.
    /// </remarks>
    [Fact]
    public void A_replaced_input_is_refused_even_though_its_name_is_unchanged()
    {
        using TemporarySet set = TemporarySet.Complete();
        set.OverwriteBytes("FIX-PORTRAIT-001", [.. "different bytes, same file name"u8]);

        ImmutableArray<RegressionSetProblem> problems = StandardRegressionSet.Load(set.Root).Validate();

        problems.ShouldContain(p => p.AssetId == "FIX-PORTRAIT-001" && p.Detail.Contains("SHA-256 drift"));
    }

    [Fact]
    public void A_missing_input_file_is_refused()
    {
        using TemporarySet set = TemporarySet.Complete();
        set.DeleteInput("FIX-PDF-001");

        StandardRegressionSet.Load(set.Root).Validate()
            .ShouldContain(p => p.AssetId == "FIX-PDF-001" && p.Detail.Contains("does not exist"));
    }

    /// <summary>An accepted set states outcomes; PENDING is the absence of one.</summary>
    [Fact]
    public void A_manifest_still_recording_PENDING_is_refused()
    {
        using TemporarySet set = TemporarySet.Complete();
        set.MakePending("FIX-CUSTOMER-DESIGN-001");

        StandardRegressionSet.Load(set.Root).Validate()
            .ShouldContain(p => p.Detail.Contains("PENDING"));
    }

    [Fact]
    public void An_unknown_comparison_mode_is_refused()
    {
        using TemporarySet set = TemporarySet.Complete();
        set.SetComparisonMode("FIX-PORTRAIT-001", "LooksAboutRight");

        StandardRegressionSet.Load(set.Root).Validate()
            .ShouldContain(p => p.Detail.Contains("Unknown comparison mode"));
    }

    // ------------------------------------------------------------------------------------------
    // Aggregating a run
    // ------------------------------------------------------------------------------------------

    [Fact]
    public void A_run_in_which_every_case_passed_is_Passed()
    {
        StandardRegressionSetRunResult run = Run(
            [.. StandardRegressionCategories.Required.Select(c => PassingCase(c))]);

        run.Status.ShouldBe("Passed");
        run.MissingCategories.ShouldBeEmpty();
        run.Cases.Length.ShouldBe(7);
    }

    /// <summary>
    /// A category that was never run cannot be absent from a green result.
    /// </summary>
    /// <remarks>
    /// The failure mode this exists to prevent is the tempting one: run the six easy cases,
    /// observe six passes, and report a pass. The status is derived from the required list rather
    /// than from the cases that happen to be present, so six of seven is never Passed.
    /// </remarks>
    [Fact]
    public void A_skipped_category_cannot_produce_a_passing_run()
    {
        StandardRegressionSetRunResult run = Run(
            [.. StandardRegressionCategories.Required
                .Where(c => c != "COMPLEX_BACKGROUND_FINE_HAIR")
                .Select(c => PassingCase(c))]);

        run.Status.ShouldNotBe("Passed");
        run.MissingCategories.ShouldBe(["COMPLEX_BACKGROUND_FINE_HAIR"]);
        run.Verdict.ShouldContain("Not run at all: COMPLEX_BACKGROUND_FINE_HAIR");
    }

    [Fact]
    public void A_failed_structural_assertion_fails_its_case_and_the_run()
    {
        RegressionCaseResult broken = PassingCase("TRANSPARENT_PNG") with
        {
            Assertions = [new RegressionAssertion("trimBoundsExact", false, "Got 2724x3684, expected 2724x3685.")],
        };

        StandardRegressionSetRunResult run = Run(
            [.. StandardRegressionCategories.Required
                .Where(c => c != "TRANSPARENT_PNG").Select(c => PassingCase(c)), broken]);

        run.Status.ShouldBe("Failed");
        run.Cases.Single(c => c.Category == "TRANSPARENT_PNG").Outcome.ShouldBe(RegressionOutcome.Failed);
    }

    /// <summary>
    /// A qualitative check nobody decided leaves its case unconcluded, and the run with it.
    /// </summary>
    /// <remarks>
    /// The set records visual checks for the portrait and fine-hair cases because those questions
    /// cannot be answered by an assertion honestly. This is what stops the runner from answering
    /// them anyway: a decision of <see cref="RegressionOutcome.Pending"/> is the absence of a
    /// decision, and no arrangement of pending decisions aggregates to Passed.
    /// </remarks>
    [Fact]
    public void An_undecided_manual_check_cannot_be_marked_passed_automatically()
    {
        RegressionCaseResult undecided = PassingCase("COMPLEX_BACKGROUND_FINE_HAIR") with
        {
            ManualDecisions =
            [
                new RegressionManualDecision("FINE-HAIR-VISUAL-001", "Are strands retained?",
                    RegressionOutcome.Pending, null, null, null, null),
            ],
        };

        undecided.Conclude().Outcome.ShouldBe(RegressionOutcome.Pending);

        StandardRegressionSetRunResult run = Run(
            [.. StandardRegressionCategories.Required
                .Where(c => c != "COMPLEX_BACKGROUND_FINE_HAIR").Select(c => PassingCase(c)), undecided]);

        run.Status.ShouldNotBe("Passed");
    }

    [Fact]
    public void A_decided_manual_check_records_who_decided_it()
    {
        RegressionCaseResult decided = PassingCase("COMPLEX_BACKGROUND_FINE_HAIR") with
        {
            ManualDecisions =
            [
                new RegressionManualDecision("FINE-HAIR-VISUAL-001", "Are strands retained?",
                    RegressionOutcome.Passed, "DESKTOP-0BG8884\\admin", "2026-09-09T18:00:00+12:00",
                    @"D:\evidence\cutout.png", "Strands retained; no halo."),
            ],
        };

        decided.Conclude().Outcome.ShouldBe(RegressionOutcome.Passed);
        decided.ManualDecisions.Single().DecidedBy.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void A_blocked_environment_is_not_a_pass()
    {
        StandardRegressionSetRunResult run = Run(
            [.. StandardRegressionCategories.Required.Select(c => new RegressionCaseResult(
                "(environment)", c, RegressionOutcome.Blocked, null, [], [], [], [], [], [], null,
                "The workstation did not verify."))]);

        run.Status.ShouldBe("Blocked");
        run.Status.ShouldNotBe("Passed");
    }

    /// <summary>
    /// The result the runner writes is the shape the revalidation script already reads.
    /// </summary>
    /// <remarks>
    /// <c>Set-PrintFlowProductionRevalidation.ps1</c> reads four properties by name from this
    /// file. Adding per-case evidence to the result must not move them, so the property names and
    /// casing are asserted against the serialised JSON rather than against the record's fields.
    /// </remarks>
    [Fact]
    public void The_run_result_keeps_the_contract_the_revalidation_script_reads()
    {
        StandardRegressionSetRunResult run = Run(
            [.. StandardRegressionCategories.Required.Select(c => PassingCase(c))]);

        using JsonDocument document = JsonDocument.Parse(run.ToJson());
        JsonElement root = document.RootElement;

        root.GetProperty("SetId").GetString().ShouldBe("printflow-regression-v1");
        root.GetProperty("Status").GetString().ShouldBe("Passed");
        root.GetProperty("CompletedAtLocal").GetString().ShouldNotBeNullOrWhiteSpace();
        root.GetProperty("EvidencePath").GetString().ShouldNotBeNullOrWhiteSpace();

        // The script's own reader is case-insensitive (ConvertFrom-Json onto PowerShell property
        // access), which is why these names may be Pascal-cased here; what must not change is
        // that all four exist and that Status is exactly "Passed" when it passed.
        string script = ReadRepositoryFile(@"tools\installer\Set-PrintFlowProductionRevalidation.ps1");
        script.ShouldContain("$result.setId");
        script.ShouldContain("$result.status");
        script.ShouldContain("$result.completedAtLocal");
        script.ShouldContain("$result.evidencePath");
        script.ShouldContain("-eq 'Passed'");
    }

    // ------------------------------------------------------------------------------------------
    // The revalidation bootstrap
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The bootstrap is consulted when the revalidation record is the only thing missing.
    /// </summary>
    [Fact]
    public async Task The_bootstrap_verifies_a_workstation_whose_only_failure_is_the_missing_revalidation()
    {
        StubVerifier real = new(Result(
            Passed(WorkstationVerificationCheck.PresetIntegrity),
            Passed(WorkstationVerificationCheck.OperatingSystem),
            FailedCheck(WorkstationVerificationCheck.ProductionRevalidation)));

        // What ForStandardRegressionRun produces: the same checks with that one absent, and the
        // live phase able to run because the automatic half now verifies.
        StubVerifier omitted = new(Result(
            Passed(WorkstationVerificationCheck.PresetIntegrity),
            Passed(WorkstationVerificationCheck.OperatingSystem),
            Passed(WorkstationVerificationCheck.PhotoshopSafeStartingState)));

        RegressionBootstrapWorkstationVerifier bootstrap = new(real, omitted);
        WorkstationVerificationResult result = bootstrap.Verify();

        result.Verified.ShouldBeTrue();
        result.Checks.ShouldNotContain(c => c.Check == WorkstationVerificationCheck.ProductionRevalidation);
        bootstrap.BootstrapWasUsed.ShouldBeTrue();

        // And the live phase is the bootstrapped one, which is the entire reason the omission
        // happens inside the verifier rather than around it: the real composition refuses to
        // launch applications while its automatic half is failing, so a bootstrap applied outside
        // would leave every live check Blocked and the run would drive Meitu and Photoshop
        // without their launchability, safe states, colour settings or automation lock verified.
        (await bootstrap.RunLiveChecksAsync(CancellationToken.None)).Verified.ShouldBeTrue();
    }

    /// <summary>
    /// Any other failure still closes the run, and the real verifier's own answer is what comes back.
    /// </summary>
    /// <remarks>
    /// The safety property of the whole bootstrap. A workstation whose display topology changed,
    /// or whose Photoshop was upgraded, is failing something the regression run has no business
    /// waving through — so the bootstrapped verifier is never consulted, the real answer is
    /// returned untouched, and
    /// <see cref="RegressionBootstrapWorkstationVerifier.BootstrapWasUsed"/> stays false.
    /// </remarks>
    [Theory]
    [InlineData(WorkstationVerificationCheck.MeituExecutable)]
    [InlineData(WorkstationVerificationCheck.PhotoshopExecutable)]
    [InlineData(WorkstationVerificationCheck.DisplayConfiguration)]
    [InlineData(WorkstationVerificationCheck.PhotoshopActionArtifact)]
    public async Task The_bootstrap_refuses_to_help_when_anything_else_is_wrong(WorkstationVerificationCheck also)
    {
        StubVerifier real = new(Result(
            Passed(WorkstationVerificationCheck.PresetIntegrity),
            FailedCheck(also),
            FailedCheck(WorkstationVerificationCheck.ProductionRevalidation)));

        // Deliberately a verifier that would say yes. It must never be asked.
        StubVerifier omitted = new(Result(Passed(WorkstationVerificationCheck.PresetIntegrity)));

        RegressionBootstrapWorkstationVerifier bootstrap = new(real, omitted);
        WorkstationVerificationResult result = bootstrap.Verify();

        result.Verified.ShouldBeFalse();
        result.Checks.ShouldContain(c => c.Check == WorkstationVerificationCheck.ProductionRevalidation);
        bootstrap.BootstrapWasUsed.ShouldBeFalse();

        (await bootstrap.RunLiveChecksAsync(CancellationToken.None)).Verified.ShouldBeFalse();
    }

    /// <summary>
    /// A workstation that already has a valid record is not touched by the bootstrap at all.
    /// </summary>
    [Fact]
    public void The_bootstrap_stands_aside_when_a_valid_revalidation_record_exists()
    {
        StubVerifier real = new(Result(
            Passed(WorkstationVerificationCheck.PresetIntegrity),
            Passed(WorkstationVerificationCheck.ProductionRevalidation)));
        StubVerifier omitted = new(Result(Passed(WorkstationVerificationCheck.PresetIntegrity)));

        RegressionBootstrapWorkstationVerifier bootstrap = new(real, omitted);

        bootstrap.Verify().Checks
            .ShouldContain(c => c.Check == WorkstationVerificationCheck.ProductionRevalidation);
        bootstrap.BootstrapWasUsed.ShouldBeFalse();
    }

    /// <summary>
    /// Omitting the check cannot manufacture a verified result out of a broken root of trust.
    /// </summary>
    /// <remarks>
    /// <c>WorkstationVerificationResult.From</c> still derives Verified from the checks that
    /// remain and still requires a passing <c>PresetIntegrity</c>, so a workstation whose preset
    /// does not hash to what configuration demands stays unverified with or without the
    /// revalidation check.
    /// </remarks>
    [Fact]
    public void Omitting_the_revalidation_check_cannot_verify_a_workstation_without_preset_integrity()
    {
        WorkstationVerificationResult omitted = WorkstationVerificationResult.From(
            preset: null,
            [FailedCheck(WorkstationVerificationCheck.PresetIntegrity)],
            DateTimeOffset.Now);

        omitted.Verified.ShouldBeFalse();

        RegressionBootstrapWorkstationVerifier bootstrap = new(
            new StubVerifier(Result(
                Passed(WorkstationVerificationCheck.PresetIntegrity),
                FailedCheck(WorkstationVerificationCheck.ProductionRevalidation))),
            new StubVerifier(omitted));

        bootstrap.Verify().Verified.ShouldBeFalse();
    }

    /// <summary>
    /// The omission seam is internal to Infrastructure, which grants internals to tests alone.
    /// </summary>
    /// <remarks>
    /// This is what makes "the shipped product cannot skip its own revalidation check" a
    /// structural fact. PrintFlow.App has no <c>InternalsVisibleTo</c> grant from
    /// PrintFlow.Infrastructure, so no composition the application performs can name this method
    /// — and the public factory it does call has no parameter that could reach it.
    /// </remarks>
    [Fact]
    public void The_omission_factory_is_not_reachable_from_the_application()
    {
        MethodInfo? omitting = typeof(ProductionWorkstationVerifier).GetMethod(
            "ForStandardRegressionRun", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        omitting.ShouldNotBeNull("The regression bootstrap factory must exist.");
        omitting!.IsPublic.ShouldBeFalse("It must not be part of the Infrastructure public surface.");
        omitting.IsAssembly.ShouldBeTrue("It must be internal, so only PrintFlow.Tests can call it.");

        // The public factory the application actually calls exposes no way to omit a check.
        foreach (MethodInfo publicFactory in typeof(ProductionWorkstationVerifier)
                     .GetMethods(BindingFlags.Static | BindingFlags.Public)
                     .Where(m => m.Name == "ForWorkstation"))
        {
            publicFactory.GetParameters()
                .ShouldNotContain(p => p.ParameterType == typeof(bool),
                    "No public factory may take a flag that turns a check off.");
            publicFactory.GetParameters()
                .ShouldNotContain(p => p.ParameterType == typeof(IProductionRevalidationReader),
                    "No public factory may let a caller supply the revalidation record.");
        }

        typeof(ProductionWorkstationVerifier).Assembly
            .GetCustomAttributes<System.Runtime.CompilerServices.InternalsVisibleToAttribute>()
            .Select(a => a.AssemblyName)
            .ShouldBe(["PrintFlow.Tests"],
                "Infrastructure must grant its internals to the test assembly and nothing else.");
    }

    /// <summary>
    /// The bootstrap is unreachable from the application's own composition.
    /// </summary>
    /// <remarks>
    /// Structural rather than conventional: PrintFlow.App, PrintFlow.Infrastructure,
    /// PrintFlow.Workflow and PrintFlow.Domain do not reference PrintFlow.Tests, so no graph the
    /// application builds can resolve this type. Asserted here so that a later refactor which
    /// moved the seam into Infrastructure "for reuse" fails a test instead of quietly widening
    /// what can authorise Production.
    /// </remarks>
    [Fact]
    public void No_product_assembly_can_reach_the_revalidation_bootstrap()
    {
        Assembly tests = typeof(RegressionBootstrapWorkstationVerifier).Assembly;
        tests.GetName().Name.ShouldBe("PrintFlow.Tests");

        foreach (Assembly product in new[]
                 {
                     typeof(PrintFlow.Domain.Results.FailureCode).Assembly,
                     typeof(PrintFlow.Workflow.Ports.IEnvironmentGate).Assembly,
                     typeof(ProductionRevalidationRecord).Assembly,
                     typeof(PrintFlow.App.Composition.ServiceRegistration).Assembly,
                 })
        {
            product.GetReferencedAssemblies()
                .ShouldNotContain(reference => reference.Name == "PrintFlow.Tests",
                    $"{product.GetName().Name} must not reference the test assembly.");

            product.GetTypes()
                .ShouldNotContain(type => type.Name.Contains("Bootstrap", StringComparison.Ordinal) &&
                                          typeof(IProductionWorkstationVerifier).IsAssignableFrom(type),
                    $"{product.GetName().Name} must not define a workstation verifier that bypasses a check.");
        }
    }

    /// <summary>
    /// PrintFlow still cannot write its own revalidation record.
    /// </summary>
    /// <remarks>
    /// Restated here because this slice is the one that had a reason to want a writer. It did not
    /// add one, and the run records a bootstrap in its evidence instead of forging the record the
    /// bootstrap stands in for.
    /// </remarks>
    [Fact]
    public void Nothing_in_the_solution_writes_a_production_revalidation_record()
    {
        foreach (Assembly assembly in new[]
                 {
                     typeof(ProductionRevalidationRecord).Assembly,
                     typeof(PrintFlow.App.Composition.ServiceRegistration).Assembly,
                     typeof(RegressionBootstrapWorkstationVerifier).Assembly,
                 })
        {
            assembly.GetTypes()
                .Where(t => t.GetInterfaces().Any(i => i.Name.Contains("RevalidationWriter", StringComparison.Ordinal)))
                .ShouldBeEmpty($"{assembly.GetName().Name} must not contain a revalidation writer.");
        }

        typeof(IProductionRevalidationReader).GetMethods()
            .ShouldAllBe(m => m.Name == "Read");
    }

    // ------------------------------------------------------------------------------------------
    // The reference TIFF is not an input
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Adding a reference production TIFF to the test data does not make TIFF an input format.
    /// </summary>
    /// <remarks>
    /// The set now contains a TIFF, which is the one change most likely to invite a later "well,
    /// we accept TIFFs now" — so the rule gets a regression case of its own rather than only the
    /// format list's own unit test.
    /// </remarks>
    [Fact]
    public void The_reference_production_TIFF_category_is_not_a_supported_input()
    {
        SupportedInputFormats.IsSupported(ImageFormat.Tiff).ShouldBeFalse();
        SupportedInputFormats.All.ShouldNotContain(ImageFormat.Tiff);
        StandardRegressionCategories.Required.ShouldContain("REFERENCE_PRODUCTION_TIFF");
    }

    // ------------------------------------------------------------------------------------------
    // TIFF structure
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// The independent TIFF reader agrees with the Product's own output about its structure.
    /// </summary>
    [Fact]
    public void The_independent_tiff_reader_reads_a_production_tiff()
    {
        string path = Path.Combine(Path.GetTempPath(), $"printflow-regression-tiff-{Guid.NewGuid():N}.tif");
        try
        {
            Fixtures.ProductionTiffFixture.WriteAt(path,
                new Fixtures.ProductionTiffFixtureOptions(PixelWidth: 8, PixelHeight: 4));

            TiffStructure structure = TiffStructure.Read(path);

            structure.LittleEndian.ShouldBeTrue();
            structure.Width.ShouldBe(8);
            structure.Height.ShouldBe(4);
            structure.PlanarConfiguration.ShouldBe(1);
            structure.SamplesPerPixel.ShouldBe(5);
            structure.PhotometricInterpretation.ShouldBe(5);
            structure.Compression.ShouldBe(1);
            structure.XResolution.ShouldBe(300);
            structure.YResolution.ShouldBe(300);
        }
        finally { File.Delete(path); }
    }

    // ==========================================================================================
    // Helpers
    // ==========================================================================================

    private static StandardRegressionSetRunResult Run(IEnumerable<RegressionCaseResult> cases) =>
        StandardRegressionSetRunResult.From(
            "printflow-regression-v1", "20260909-180000", "2026-09-09T18:00:00+12:00",
            "2026-09-09T18:40:00+12:00", @"D:\PrintFlowStudio\TestData\v1\runs\20260909-180000",
            "DESKTOP-0BG8884", "0.1.0", "printflow-workstation-v1", "1.16.0", "Production", cases);

    private static RegressionCaseResult PassingCase(string category) =>
        new($"FIX-{category}", category, RegressionOutcome.Pending, "PrepareAsset",
            ["Import", "OriginalConfirmation"], [], ["adapter"], [],
            [new RegressionAssertion("ran", true, "ok")], [], "evidence", "ok");

    private static WorkstationVerificationResult Result(params WorkstationCheckResult[] checks) =>
        WorkstationVerificationResult.From(
            new ProductionPresetRef("printflow-workstation-v1", "1.16.0",
                Sha256.FromBytes(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("preset")))),
            checks, DateTimeOffset.Now);

    private static WorkstationCheckResult Passed(WorkstationVerificationCheck check) =>
        WorkstationCheckResult.Passed(check, WorkstationCheckKind.Immutable, "observed", "fine");

    private static WorkstationCheckResult FailedCheck(WorkstationVerificationCheck check) =>
        WorkstationCheckResult.Failed(check, WorkstationCheckKind.Dynamic,
            FailureCode.EnvironmentNotVerified, "expected", "observed", "not fine");

    private sealed class StubVerifier(WorkstationVerificationResult result) : IProductionWorkstationVerifier
    {
        public WorkstationVerificationResult Verify() => result;

        public Task<WorkstationVerificationResult> RunLiveChecksAsync(CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PrintFlowStudio.sln")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("The repository root must be findable from the test output folder.");
        return File.ReadAllText(Path.Combine(directory!.FullName, relativePath));
    }

    /// <summary>A whole regression set in a temporary folder, built to be broken in one way.</summary>
    private sealed class TemporarySet : IDisposable
    {
        private TemporarySet(string root) => Root = root;

        public string Root { get; }

        /// <summary>The real set's asset ids, so a test can name a case the way an operator would.</summary>
        private static readonly Dictionary<string, string> IdsByCategory = new(StringComparer.Ordinal)
        {
            ["NORMAL_JPG_PORTRAIT"] = "FIX-PORTRAIT-001",
            ["COMPLEX_BACKGROUND_FINE_HAIR"] = "FIX-FINE-HAIR-001",
            ["TRANSPARENT_PNG"] = "FIX-TRANSPARENT-001",
            ["COMPLETE_CUSTOMER_DESIGN"] = "FIX-CUSTOMER-DESIGN-001",
            ["PSD_WITH_COMPOSITE_PREVIEW"] = "FIX-PSD-001",
            ["SINGLE_PAGE_PDF"] = "FIX-PDF-001",
            ["REFERENCE_PRODUCTION_TIFF"] = "FIX-REFERENCE-TIFF-001",
        };

        public static TemporarySet Complete()
        {
            string root = Path.Combine(Path.GetTempPath(), $"printflow-regression-set-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(root, "inputs"));
            Directory.CreateDirectory(Path.Combine(root, "manifests"));

            TemporarySet set = new(root);
            foreach (string category in StandardRegressionCategories.Required)
            {
                set.Add(IdsByCategory[category], category);
            }

            return set;
        }

        public void Add(string id, string category)
        {
            string input = Path.Combine(Root, "inputs", id + ".bin");
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes($"synthetic content for {id}");
            File.WriteAllBytes(input, bytes);

            string manifest = $$"""
            {
              "schemaVersion": 2,
              "setId": "printflow-regression-v1",
              "fixtureId": "{{id}}",
              "category": "{{category}}",
              "file": {
                "path": {{JsonSerializer.Serialize(input)}},
                "length": {{bytes.Length}},
                "sha256": "{{Convert.ToHexString(SHA256.HashData(bytes))}}",
                "format": "BIN"
              },
              "expectedWorkflow": "PrepareAsset",
              "expectedProcessingPath": ["Import", "OriginalConfirmation"],
              "expectedExternalApplications": [],
              "expectedProperties": { "importAccepted": true },
              "comparisonPolicy": { "mode": "Structural", "reason": "test fixture" },
              "manualChecks": [],
              "notes": "test fixture"
            }
            """;
            File.WriteAllText(Path.Combine(Root, "manifests", id + ".json"), manifest);
        }

        public void Remove(string category) =>
            File.Delete(Path.Combine(Root, "manifests", IdsByCategory[category] + ".json"));

        public void DeleteInput(string id) => File.Delete(InputOf(id));

        public void OverwriteBytes(string id, byte[] bytes) => File.WriteAllBytes(InputOf(id), bytes);

        public void MakePending(string id) => Rewrite(id, json =>
            json.Replace("\"importAccepted\": true", "\"finalTiff\": \"PENDING\"", StringComparison.Ordinal));

        public void SetComparisonMode(string id, string mode) => Rewrite(id, json =>
            json.Replace("\"mode\": \"Structural\"", $"\"mode\": \"{mode}\"", StringComparison.Ordinal));

        private void Rewrite(string id, Func<string, string> change)
        {
            string path = ManifestOf(id);
            File.WriteAllText(path, change(File.ReadAllText(path)));
        }

        private string ManifestOf(string id) =>
            Directory.EnumerateFiles(Path.Combine(Root, "manifests"), "*.json")
                .Single(f => File.ReadAllText(f).Contains($"\"fixtureId\": \"{id}", StringComparison.Ordinal) ||
                             Path.GetFileNameWithoutExtension(f).Contains(id, StringComparison.Ordinal));

        private string InputOf(string id)
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(ManifestOf(id)));
            return document.RootElement.GetProperty("file").GetProperty("path").GetString()!;
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch (IOException) { }
        }
    }
}
