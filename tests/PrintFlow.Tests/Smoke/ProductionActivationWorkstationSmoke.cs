using System.Diagnostics;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;
using Xunit.Abstractions;

namespace PrintFlow.Tests.Smoke;

/// <summary>
/// The controlled Production activation smoke: the real composition, the real gate and the real
/// adapters, running one synthetic job on the accepted workstation
/// (Epic 11500 Part D §10, §11).
/// </summary>
/// <remarks>
/// Opt-in and inert by default, like every workstation smoke: the signed preset and the accepted
/// binaries exist on exactly one machine. Set <c>PRINTFLOW_PRODUCTION_ACTIVATION_SMOKE=1</c> to
/// run it.
/// <para>
/// <b>What makes this different from every earlier Production smoke.</b> Epics 11300 and 11400
/// proved their adapters through a controlled seam: a hand-composed object graph and a gate that
/// said yes, because global Production composition was closed and weakening the real gate to open
/// it would have been the wrong trade. Nothing is bypassed here. The graph is built by
/// <see cref="ServiceRegistration"/> from the committed <c>appsettings.json</c>; the gate is
/// <c>VerifiedEnvironmentGate</c> resolved out of that graph; the adapters are whatever
/// <c>Adapters.Mode</c> composed. If the gate refuses, the job refuses, and this smoke reports
/// that rather than routing around it.
/// </para>
/// <para>
/// <b>What is isolated.</b> The database, redirected to a throwaway QA file so no row is written
/// to the operator's installation, and the input, which is a synthetic PNG generated at run time.
/// The workspace root is <i>not</i> redirected and cannot be: the accepted preset names the root
/// this workstation is verified for, and pointing the installation elsewhere would be a
/// workstation that has not verified (Part A §7). The session therefore lives under the real
/// <c>Sessions\</c>, in its own directory, created fresh and touching nothing that was there.
/// </para>
/// <para>
/// <b>What it will not do.</b> It changes no display setting, replaces no binary, edits no
/// accepted file, opens no customer document, and answers no dialog. If Photoshop is holding an
/// unsaved document when the smoke starts, the smoke reports itself blocked and stops — a
/// discard prompt is exactly the dialog §10 forbids automating, and the adapter's own guards
/// refuse to answer one either.
/// </para>
/// </remarks>
public sealed class ProductionActivationWorkstationSmoke(ITestOutputHelper output)
{
    private const string EnableVariable = "PRINTFLOW_PRODUCTION_ACTIVATION_SMOKE";

    /// <summary>The window-title marker Photoshop appends to a document with unsaved changes.</summary>
    private const string UnsavedDocumentMarker = "*";

    /// <summary>The name prefix every artefact this smoke generates carries.</summary>
    private const string SmokeArtefactPrefix = "PF_11500D_";

    [Fact]
    public async Task The_real_composition_runs_one_synthetic_production_job_on_this_workstation()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            // Inert by design; see the class remarks.
            return;
        }

        PrintFlowConfiguration committed =
            PrintFlowConfiguration.LoadFromFile(RepositoryFile("appsettings.json"));

        // Stated, never assumed. Before the activation decision this smoke composes Production
        // through an explicit override and says so; after it, the committed file already says
        // Production and no override is applied. Either way the line below is the evidence of
        // which configuration was actually tested (§4, §13).
        bool overrideApplied = committed.Adapters.Mode != "Production";
        PrintFlowConfiguration configuration = overrideApplied
            ? committed with { Adapters = new AdaptersConfiguration("Production") }
            : committed;

        string workspaceRoot = Path.GetFullPath(configuration.Workspace.Root);
        string token = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss") + "-" +
                       Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        string qaRoot = Path.Combine(workspaceRoot, "QA", "Epic11500D", token);
        Directory.CreateDirectory(qaRoot);

        output.WriteLine("=== configuration ===");
        output.WriteLine($"committed Adapters.Mode : {committed.Adapters.Mode}");
        output.WriteLine($"composed  Adapters.Mode : {configuration.Adapters.Mode}");
        output.WriteLine($"override applied        : {(overrideApplied ? "yes (pre-activation proof)" : "no (committed configuration)")}");
        output.WriteLine($"configured preset       : {configuration.Preset.Id} {configuration.Preset.Version}");
        output.WriteLine($"workspace root          : {workspaceRoot}");
        output.WriteLine($"QA directory            : {qaRoot}");
        output.WriteLine(string.Empty);

        // ---- preflight ------------------------------------------------------------------

        if (UnsavedPhotoshopDocument() is { } unsaved)
        {
            output.WriteLine("=== SMOKE BLOCKED ===");
            output.WriteLine($"Photoshop is holding a document with unsaved changes: {unsaved}");
            output.WriteLine("Nothing was run. PrintFlow does not answer a prompt it did not raise, and this");
            output.WriteLine("smoke will not risk one. Save or close the document and run the smoke again.");
            return;
        }

        WorkspaceCensus before = WorkspaceCensus.Take(workspaceRoot);
        int externalBefore = ExternalApplicationProbe.RunningCount();
        output.WriteLine($"external apps running   : {externalBefore} (before)");

        // ---- the real composed graph ------------------------------------------------------

        string databasePath = Path.Combine(qaRoot, "printflow-activation.db");
        SqliteConnectionFactory factory = new(databasePath);
        using (SqliteConnection connection = factory.Open())
        {
            MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();
        }

        using ServiceProvider services =
            ServiceRegistration.BuildServiceProvider(configuration, workspaceRoot, factory);

        IEnvironmentGate gate = services.GetRequiredService<IEnvironmentGate>();
        IEnvironmentDiagnostics diagnostics = services.GetRequiredService<IEnvironmentDiagnostics>();
        IMeituProcessor meitu = services.GetRequiredService<IMeituProcessor>();
        IPhotoshopOutputProcessor photoshop = services.GetRequiredService<IPhotoshopOutputProcessor>();
        ISessionService service = services.GetRequiredService<ISessionService>();
        ISessionRepository repository = services.GetRequiredService<ISessionRepository>();

        output.WriteLine("=== composition ===");
        output.WriteLine($"gate                    : {gate.GetType().Name}");
        output.WriteLine($"Meitu adapter           : {meitu.GetType().Name} / {meitu.AdapterId} / {meitu.Mode}");
        output.WriteLine($"Photoshop adapter       : {photoshop.GetType().Name} / {photoshop.AdapterId} / {photoshop.Mode}");
        output.WriteLine(string.Empty);

        meitu.ShouldBeOfType<ProductionMeituProcessor>();
        photoshop.ShouldBeOfType<ProductionPhotoshopOutputProcessor>();
        meitu.Mode.ShouldBe(AdapterExecutionMode.Production);
        photoshop.Mode.ShouldBe(AdapterExecutionMode.Production);

        // ---- the gate --------------------------------------------------------------------

        EnvironmentReadinessReport readiness = diagnostics.Read();
        OperationResult<PrintFlow.Domain.Results.Unit> production = gate.Verify(AdapterExecutionMode.Production);

        output.WriteLine("=== readiness ===");
        output.WriteLine($"preset identity         : {readiness.PresetIdentity ?? "(unverified)"}");
        output.WriteLine($"observed at             : {readiness.ObservedAt:u}");
        foreach (EnvironmentCheckReport check in readiness.Checks)
        {
            output.WriteLine($"[{check.Status,-8}] {(check.IsBlocking ? "blocking" : "advisory")} {check.CheckKey}");
        }

        output.WriteLine($"verified                : {readiness.Verified}");
        output.WriteLine($"blocking failures       : {readiness.BlockingFailures.Count()}");
        output.WriteLine($"advisories              : {readiness.Advisories.Count()}");
        output.WriteLine($"gate(Production)        : {(production.IsSuccess ? "ALLOWED" : "REFUSED")}");
        output.WriteLine(string.Empty);

        if (production.IsFailure)
        {
            output.WriteLine("=== SMOKE BLOCKED ===");
            output.WriteLine($"code                    : {production.Failure.Code}");
            output.WriteLine($"detail                  : {production.Failure.TechnicalDetail}");
            output.WriteLine("The gate refused Production on this workstation. Nothing external was run,");
            output.WriteLine("which is the correct outcome of a refusal — repair the workstation, do not");
            output.WriteLine("bypass the gate.");
            return;
        }

        // ---- job A: the Photoshop half ------------------------------------------------------

        string sourcePath = Path.Combine(qaRoot, $"{SmokeArtefactPrefix}{token}.png");
        File.WriteAllBytes(sourcePath, SyntheticImages.PngWithAlpha(
            1200, 800, static (_, _) => byte.MaxValue, dpi: 240));

        output.WriteLine("=== job: GENERATE_PRINT_TIFF ===");
        output.WriteLine($"synthetic source        : {sourcePath} (1200x800 @ 240 ppi, generated now)");

        SessionId id = Accept(await service.ImportAsync(
            WorkflowType.GeneratePrintTiff, sourcePath, $"{SmokeArtefactPrefix}{token}", "qa",
            CancellationToken.None)).Id;

        Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal("synthetic activation fixture"), "qa",
            CancellationToken.None));

        PrintDimensions dimensions = PrintDimensions.FromMillimetres(50.8, 100, SizePreset.Custom);
        Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.SetPrintDimensions(dimensions), "qa", CancellationToken.None));
        Accept(await service.ExecuteAsync(
            id,
            new WorkflowCommand.SelectWhiteUnderbaseBranch(WhiteUnderbaseBranch.W1_1px, "synthetic solid design"),
            "qa",
            CancellationToken.None));

        output.WriteLine($"size decision           : max {dimensions.MaxWidthMm}x{dimensions.MaxHeightMm} mm @ 300 ppi");
        output.WriteLine($"W1 branch               : {WhiteUnderbaseBranch.W1_1px}");
        output.WriteLine("running the real Photoshop operation through the real gate ...");

        OperationResult<SessionView> produced = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "qa", CancellationToken.None);

        SessionAggregate aggregate = (await repository.LoadAsync(id, CancellationToken.None)).Value!;

        output.WriteLine(string.Empty);
        output.WriteLine("=== job A outcome ===");
        output.WriteLine($"result                  : {(produced.IsSuccess ? "SUCCEEDED" : "REFUSED")}");

        if (produced.IsFailure)
        {
            output.WriteLine($"code                    : {produced.Failure.Code}");
            output.WriteLine($"detail                  : {produced.Failure.TechnicalDetail}");
            foreach (KeyValuePair<string, string> entry in produced.Failure.Context)
            {
                output.WriteLine($"  {entry.Key,-22}: {entry.Value}");
            }

            produced.Failure.Code.ShouldNotBe(FailureCode.EnvironmentNotVerified,
                "the gate had already allowed Production; a refusal here came from the operation.");

            aggregate.Revisions.ShouldNotContain(r => r.Operation == OperationKind.PhotoshopOutput);
            aggregate.Outputs.ShouldBeEmpty();
            output.WriteLine("no Revision was created; nothing is reviewable.");
        }
        else
        {
            Revision tiff = aggregate.Revisions
                .Single(r => r.Operation == OperationKind.PhotoshopOutput);
            string tiffPath = Path.Combine(workspaceRoot, tiff.File.RelativePath.Replace('/', '\\'));

            aggregate.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State
                .ShouldBe(StepState.ReviewRequired);
            File.Exists(tiffPath).ShouldBeTrue();

            output.WriteLine($"produced revision       : {tiff.File.RelativePath}");
            output.WriteLine($"on disk                 : {tiffPath}");
            output.WriteLine($"SHA-256                 : {tiff.Facts.Sha256}");
            output.WriteLine($"bytes                   : {tiff.Facts.ByteLength}");
            output.WriteLine($"pixels                  : {tiff.Facts.PixelWidth}x{tiff.Facts.PixelHeight}");
            output.WriteLine($"step state              : {StepState.ReviewRequired}");
            output.WriteLine($"validation note         : " +
                             aggregate.Attempts.Last(a => a.Step == StepKind.PhotoshopOutput).AdapterNotes);
        }

        // ---- job B: the Meitu half ----------------------------------------------------------
        //
        // Activation opens Production for both adapters, so proving one of them live would leave
        // the other enabled on the strength of an earlier epic's controlled seam. This is the
        // same claim as job A, made about the other application: real composition, real gate,
        // real adapter, synthetic input.

        string enhanceSourcePath = Path.Combine(qaRoot, $"{SmokeArtefactPrefix}{token}_ENHANCE.png");
        File.WriteAllBytes(enhanceSourcePath, SyntheticImages.Png(320, 240, dpi: 300, alpha: true));

        output.WriteLine(string.Empty);
        output.WriteLine("=== job B: PREPARE_ASSET / Enhancement ===");
        output.WriteLine($"synthetic source        : {enhanceSourcePath} (320x240 @ 300 ppi, generated now)");

        SessionId enhanceId = Accept(await service.ImportAsync(
            WorkflowType.PrepareAsset, enhanceSourcePath, $"{SmokeArtefactPrefix}{token}_ENH", "qa",
            CancellationToken.None)).Id;

        Accept(await service.ExecuteAsync(
            enhanceId, new WorkflowCommand.ConfirmOriginal("synthetic activation fixture"), "qa",
            CancellationToken.None));

        output.WriteLine("running the real Meitu operation through the real gate ...");

        OperationResult<SessionView> enhanced = await service.ExecuteAsync(
            enhanceId, new WorkflowCommand.StartStep(StepKind.Enhancement), "qa", CancellationToken.None);

        SessionAggregate enhanceAggregate =
            (await repository.LoadAsync(enhanceId, CancellationToken.None)).Value!;
        int externalAfter = ExternalApplicationProbe.RunningCount();

        output.WriteLine(string.Empty);
        output.WriteLine("=== job B outcome ===");
        output.WriteLine($"result                  : {(enhanced.IsSuccess ? "SUCCEEDED" : "REFUSED")}");

        if (enhanced.IsFailure)
        {
            output.WriteLine($"code                    : {enhanced.Failure.Code}");
            output.WriteLine($"detail                  : {enhanced.Failure.TechnicalDetail}");
            foreach (KeyValuePair<string, string> entry in enhanced.Failure.Context)
            {
                output.WriteLine($"  {entry.Key,-22}: {entry.Value}");
            }

            enhanced.Failure.Code.ShouldNotBe(FailureCode.EnvironmentNotVerified,
                "the gate had already allowed Production; a refusal here came from the operation.");

            enhanceAggregate.Revisions.ShouldNotContain(r => r.Operation == OperationKind.Enhance);
            output.WriteLine("no Revision was created; nothing is reviewable.");
        }
        else
        {
            Revision enhancedFile = enhanceAggregate.Revisions
                .Single(r => r.Operation == OperationKind.Enhance);
            string enhancedPath =
                Path.Combine(workspaceRoot, enhancedFile.File.RelativePath.Replace('/', '\\'));

            enhanceAggregate.Steps.Single(s => s.Step == StepKind.Enhancement).State
                .ShouldBe(StepState.ReviewRequired);
            File.Exists(enhancedPath).ShouldBeTrue();

            output.WriteLine($"produced revision       : {enhancedFile.File.RelativePath}");
            output.WriteLine($"on disk                 : {enhancedPath}");
            output.WriteLine($"SHA-256                 : {enhancedFile.Facts.Sha256}");
            output.WriteLine($"bytes                   : {enhancedFile.Facts.ByteLength}");
            output.WriteLine($"pixels                  : {enhancedFile.Facts.PixelWidth}x{enhancedFile.Facts.PixelHeight}");
            output.WriteLine($"step state              : {StepState.ReviewRequired}");
            output.WriteLine($"adapter notes           : " +
                             enhanceAggregate.Attempts.Last(a => a.Step == StepKind.Enhancement).AdapterNotes);
        }

        // ---- what the workstation looks like afterwards -------------------------------------

        string evidenceDirectory = Path.Combine(workspaceRoot, "Evidence");
        int captures = Directory.Exists(evidenceDirectory)
            ? Directory.EnumerateFiles(evidenceDirectory).Count()
            : 0;

        output.WriteLine(string.Empty);
        output.WriteLine("=== workstation after ===");
        output.WriteLine($"external apps running   : {externalAfter} (was {externalBefore})");
        output.WriteLine($"external app launched   : {(externalAfter > externalBefore ? "yes" : "no")}");
        output.WriteLine($"evidence captures       : {captures}");
        before.ReportAndAssert(
            workspaceRoot,
            [
                aggregate.Session.Workspace.RelativePath.Split('/')[^1],
                enhanceAggregate.Session.Workspace.RelativePath.Split('/')[^1],
            ],
            output);

        produced.IsSuccess.ShouldBeTrue(
            produced.IsFailure ? produced.Failure.ToString() : string.Empty);
        enhanced.IsSuccess.ShouldBeTrue(
            enhanced.IsFailure ? enhanced.Failure.ToString() : string.Empty);
    }

    /// <summary>
    /// The rollback, performed against the real installation (Epic 11500 Part D §14).
    /// </summary>
    /// <remarks>
    /// The same committed configuration with the one value an operator would edit, composed by
    /// the real <see cref="ServiceRegistration"/> against the real accepted preset and the real
    /// workspace root. What it establishes is what the runbook promises: both adapters come back
    /// as the deterministic doubles, no external application is touched merely by composing, and
    /// nothing in the managed workspace moves.
    /// <para>
    /// The committed file itself is not edited here. Rollback is a configuration change and a
    /// restart, and a test that rewrote the shipped file to prove that would be doing something
    /// considerably more dangerous than the thing it was checking.
    /// </para>
    /// </remarks>
    [Fact]
    public void Rolling_the_committed_mode_back_to_fake_restores_the_fake_adapters()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            return;
        }

        PrintFlowConfiguration committed =
            PrintFlowConfiguration.LoadFromFile(RepositoryFile("appsettings.json"));
        PrintFlowConfiguration rolledBack =
            committed with { Adapters = new AdaptersConfiguration("Fake") };

        string workspaceRoot = Path.GetFullPath(committed.Workspace.Root);
        string qaRoot = Path.Combine(
            workspaceRoot, "QA", "Epic11500D",
            "rollback-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(qaRoot);

        WorkspaceCensus before = WorkspaceCensus.Take(workspaceRoot);
        int externalBefore = ExternalApplicationProbe.RunningCount();

        SqliteConnectionFactory factory = new(Path.Combine(qaRoot, "printflow-rollback.db"));
        using (SqliteConnection connection = factory.Open())
        {
            MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();
        }

        using ServiceProvider services =
            ServiceRegistration.BuildServiceProvider(rolledBack, workspaceRoot, factory);

        IMeituProcessor meitu = services.GetRequiredService<IMeituProcessor>();
        IPhotoshopOutputProcessor photoshop = services.GetRequiredService<IPhotoshopOutputProcessor>();

        output.WriteLine("=== rollback ===");
        output.WriteLine($"committed Adapters.Mode : {committed.Adapters.Mode}");
        output.WriteLine($"composed  Adapters.Mode : {rolledBack.Adapters.Mode}");
        output.WriteLine($"Meitu adapter           : {meitu.GetType().Name} / {meitu.AdapterId} / {meitu.Mode}");
        output.WriteLine($"Photoshop adapter       : {photoshop.GetType().Name} / {photoshop.AdapterId} / {photoshop.Mode}");
        output.WriteLine($"external apps running   : {ExternalApplicationProbe.RunningCount()} (was {externalBefore})");

        meitu.ShouldBeOfType<FakeMeituProcessor>();
        photoshop.ShouldBeOfType<FakePhotoshopOutputProcessor>();
        meitu.Mode.ShouldBe(AdapterExecutionMode.Fake);
        photoshop.Mode.ShouldBe(AdapterExecutionMode.Fake);

        ExternalApplicationProbe.RunningCount().ShouldBe(externalBefore,
            "rolling back composes a graph; it interacts with no external application.");

        // No session was created, so persisted workflow data is untouched in both directions.
        before.ReportAndAssert(workspaceRoot, [], output);
    }

    // -----------------------------------------------------------------------------------
    // Preflight and census
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The title of an <i>unrecognised</i> Photoshop document with unsaved changes, or
    /// <c>null</c> if there is none.
    /// </summary>
    /// <remarks>
    /// A heuristic on the window title, and honestly a heuristic: Photoshop appends an asterisk
    /// to the title of a modified document. It is used to <i>stop</i>, never to proceed — a false
    /// positive costs a re-run, and the alternative is starting a run beside somebody's unsaved
    /// work.
    /// <para>
    /// A document this smoke itself produced is not what §10 is about. Its name carries
    /// <see cref="SmokeArtefactPrefix"/>, it is a synthetic file generated by an earlier run, and
    /// Photoshop is left holding it because the accepted adapter deliberately closes nothing.
    /// Blocking on it would make the smoke unrunnable twice in a row for the sake of a rule aimed
    /// at customer work.
    /// </para>
    /// </remarks>
    private static string? UnsavedPhotoshopDocument()
    {
        foreach (Process process in Process.GetProcessesByName("Photoshop"))
        {
            using (process)
            {
                string title = process.MainWindowTitle;
                if (title.Contains(UnsavedDocumentMarker, StringComparison.Ordinal) &&
                    !title.Contains(SmokeArtefactPrefix, StringComparison.Ordinal))
                {
                    return title;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// What the managed workspace held before the job, so afterwards can be compared with it.
    /// </summary>
    /// <remarks>
    /// Counts and directory names, never contents: §11 rules out persisting a machine inventory,
    /// and what has to be established is only that nothing outside this run's own session
    /// appeared, vanished or moved.
    /// </remarks>
    private sealed record WorkspaceCensus(
        string[] SessionDirectories, int ComparisonFiles, int QuarantineFiles)
    {
        public static WorkspaceCensus Take(string workspaceRoot) => new(
            Names(Path.Combine(workspaceRoot, "Sessions")),
            FileCount(Path.Combine(workspaceRoot, "Comparison")),
            FileCount(Path.Combine(workspaceRoot, "Quarantine")));

        /// <param name="expectedSessionDirectories">
        /// The directories this run's own sessions reported, read off the persisted sessions
        /// rather than reconstructed from their ids — the workspace derives the folder name, and
        /// a test that re-derived it would be asserting its own arithmetic.
        /// </param>
        public void ReportAndAssert(
            string workspaceRoot, string[] expectedSessionDirectories, ITestOutputHelper output)
        {
            WorkspaceCensus after = Take(workspaceRoot);

            string[] added = [.. after.SessionDirectories.Except(SessionDirectories, StringComparer.Ordinal)];
            string[] removed = [.. SessionDirectories.Except(after.SessionDirectories, StringComparer.Ordinal)];

            output.WriteLine($"sessions before/after   : {SessionDirectories.Length} / {after.SessionDirectories.Length}");
            output.WriteLine($"session directories new : {added.Length} ({string.Join(", ", added)})");
            output.WriteLine($"session directories lost: {removed.Length}");
            output.WriteLine($"Comparison files        : {after.ComparisonFiles} (was {ComparisonFiles})");
            output.WriteLine($"Quarantine files        : {after.QuarantineFiles} (was {QuarantineFiles})");

            removed.ShouldBeEmpty("no existing session may be removed.");
            after.ComparisonFiles.ShouldBe(ComparisonFiles, "no comparison artefact may be touched.");
            after.QuarantineFiles.ShouldBe(QuarantineFiles, "nothing may be quarantined.");

            added.ShouldBe(expectedSessionDirectories, ignoreOrder: true,
                "the only new session directories may be this run's own.");
        }

        private static string[] Names(string directory) =>
            Directory.Exists(directory)
                ? [.. Directory.EnumerateDirectories(directory).Select(Path.GetFileName).OfType<string>()]
                : [];

        private static int FileCount(string directory) =>
            Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Count()
                : 0;
    }

    // -----------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------

    private static SessionView Accept(OperationResult<SessionView> result)
    {
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);
        return result.Value;
    }

    private static string RepositoryFile(string relativePath)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }

        current.ShouldNotBeNull("the repository root must be locatable from the test output.");
        return Path.Combine(current.FullName, relativePath);
    }
}
