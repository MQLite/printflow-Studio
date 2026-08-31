using System.IO;
using Microsoft.Data.Sqlite;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Infrastructure.Preset;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Smoke;

/// <summary>
/// The central C2A live proof: one real Photoshop run reaching ReviewRequired through the real
/// workflow (Epic 11400 Part C2A §26, §36).
/// </summary>
/// <remarks>
/// Opt-in, like every workstation smoke, because it drives a live Photoshop on the operator's
/// desktop. What makes it different from the C1 smoke is what is composed around the adapter:
/// a real <c>SessionService</c>, a real migrated SQLite database and a real
/// <c>FileWorkspace</c>, so the run ends in an Attempt row, a Revision and a step state rather
/// than in an Infrastructure result nobody records.
/// <para>
/// The gate is bypassed <b>here and only here</b>, by handing this one
/// <c>SessionService</c> a <see cref="ControlledSeamEnvironmentGate"/>. That bypass is a
/// parameter at this call site rather than a change to <c>FoundationEnvironmentGate</c> or to
/// adapter registration: <c>Adapters.Mode</c> stays <c>Fake</c>, the application composition is
/// untouched, and every other caller of the gate still gets the refusal (§27). Weakening the
/// real gate to let a smoke run would trade the one control standing between a half-built
/// adapter and a live session for a convenience.
/// </para>
/// <para>
/// Nothing here approves, rejects, promotes or completes. C2A ends at ReviewRequired (§28), and
/// the TIFF and the session workspace are deliberately retained so the report can quote their
/// hashes (§37).
/// </para>
/// </remarks>
public sealed class PhotoshopWorkflowOutputWorkstationSmoke
{
    private const string EnableVariable = "PRINTFLOW_PHOTOSHOP_WORKFLOW_SMOKE";

    [Fact]
    public async Task One_real_Photoshop_run_reaches_ReviewRequired_with_exactly_one_TIFF_Revision()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1") return;

        PrintFlowConfiguration configuration = PrintFlowConfiguration.LoadFromFile(
            RepositoryFile("appsettings.json"));

        // Stated rather than assumed: the whole point of the controlled seam is that it proves
        // the production path without the installation being switched to it (§27).
        configuration.Adapters.Mode.ShouldBe("Fake");

        string token = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss") + "-" +
                       Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        string root = Path.Combine(configuration.Workspace.Root, "QA", "Epic11400C2A", token);
        string evidence = Path.Combine(root, "Evidence");
        Directory.CreateDirectory(root);

        FileWorkspace workspace = new(root);
        string manifest = Path.Combine(configuration.Workspace.Root, configuration.Preset.Path);
        Sha256 presetSha = Sha256.Parse(configuration.Preset.ExpectedSha256);

        string databasePath = Path.Combine(root, "printflow-c2a.db");
        SqliteConnectionFactory factory = new(databasePath);
        using (SqliteConnection connection = factory.Open())
        {
            MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();
        }

        // The real production adapter, reached through the same interface SessionService uses.
        IPhotoshopOutputProcessor photoshop = PhotoshopAutomationComposition.CreateProductionProcessor(
            manifest, presetSha, workspace, evidence, TimeProvider.System);
        photoshop.Mode.ShouldBe(AdapterExecutionMode.Production);

        ISessionService service = new SessionService(
            WorkflowEngine.Instance,
            new SqliteSessionRepository(factory),
            workspace,
            new WicFileInspector(),
            new FakeMeituProcessor(workspace),
            photoshop,
            new DeterministicAlphaTrimProcessor(workspace),
            new WicManualCropProcessor(workspace),
            new WorkstationPresetProvider(
                manifest, configuration.Preset.Id, configuration.Preset.Version, presetSha),
            new ControlledSeamEnvironmentGate(),
            SystemIdGenerator.Instance,
            TimeProvider.System);

        // A fresh synthetic source, never customer artwork. The compressible field is the same
        // one C1 established: CC 2019 may store a high-frequency layer channel raw even under
        // LayerCompression.RLE, and the accepted contract is about the resulting payload.
        string sourcePath = Path.Combine(root, $"PF_C2A_{token}.png");
        File.WriteAllBytes(sourcePath, SyntheticImages.PngWithAlpha(
            1200, 800, static (_, _) => byte.MaxValue, dpi: 240));

        Console.WriteLine($"controlled workspace : {root}");
        Console.WriteLine($"database             : {databasePath}");
        Console.WriteLine($"preset               : v{configuration.Preset.Version} / {presetSha}");
        Console.WriteLine($"global Adapters.Mode : {configuration.Adapters.Mode} (unchanged)");
        Console.WriteLine($"adapter              : {photoshop.AdapterId} / {photoshop.Mode}");
        Console.WriteLine($"synthetic source     : {sourcePath}");

        SessionId id = Accept(await service.ImportAsync(
            WorkflowType.GeneratePrintTiff, sourcePath, $"PF_C2A_{token}", "qa",
            CancellationToken.None)).Id;

        Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal("synthetic finished design"), "qa",
            CancellationToken.None));

        PrintDimensions dimensions = PrintDimensions.FromMillimetres(50.8, 100, SizePreset.Custom);
        Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.SetPrintDimensions(dimensions), "qa", CancellationToken.None));
        Accept(await service.ExecuteAsync(
            id,
            new WorkflowCommand.SelectWhiteUnderbaseBranch(WhiteUnderbaseBranch.W1_1px, "ordinary design"),
            "qa",
            CancellationToken.None));

        Console.WriteLine($"size decision        : max {dimensions.MaxWidthMm}x{dimensions.MaxHeightMm} mm @ 300 ppi");
        Console.WriteLine($"W1 branch            : {WhiteUnderbaseBranch.W1_1px}");
        Console.WriteLine("starting real Photoshop run ...");

        OperationResult<SessionView> produced = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "qa", CancellationToken.None);

        SessionAggregate aggregate = (await new SqliteSessionRepository(factory)
            .LoadAsync(id, CancellationToken.None)).Value!;

        if (produced.IsFailure)
        {
            Console.WriteLine($"RESULT               : REFUSED — {produced.Failure.Code}");
            Console.WriteLine($"detail               : {produced.Failure.TechnicalDetail}");
            foreach (KeyValuePair<string, string> entry in produced.Failure.Context)
            {
                Console.WriteLine($"  {entry.Key,-24}: {entry.Value}");
            }

            // The refusal has to be an honest one: no Revision, no PrintOutput, no ReviewRequired.
            aggregate.Revisions.ShouldNotContain(r => r.Operation == OperationKind.PhotoshopOutput);
            aggregate.Outputs.ShouldBeEmpty();
            aggregate.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State
                .ShouldNotBe(StepState.ReviewRequired);
            Console.WriteLine("no Revision was created and the step did not become reviewable.");
            return;
        }

        Revision tiff = aggregate.Revisions.Single(r => r.Operation == OperationKind.PhotoshopOutput);
        ProcessingAttempt attempt = aggregate.Attempts.Single(a => a.Step == StepKind.PhotoshopOutput);
        SessionStep step = aggregate.Steps.Single(s => s.Step == StepKind.PhotoshopOutput);
        string tiffPath = workspace.ResolveAbsolute(tiff.File);

        Console.WriteLine("RESULT               : SUCCESS");
        Console.WriteLine($"attempt              : {attempt.Id} / {attempt.Status}");
        Console.WriteLine($"produced TIFF        : {tiff.File.RelativePath}");
        Console.WriteLine($"on disk              : {tiffPath}");
        Console.WriteLine($"TIFF SHA-256         : {tiff.Facts.Sha256}");
        Console.WriteLine($"TIFF bytes           : {tiff.Facts.ByteLength}");
        Console.WriteLine($"TIFF pixels          : {tiff.Facts.PixelWidth}x{tiff.Facts.PixelHeight}");
        Console.WriteLine($"revision             : {tiff.Id}");
        Console.WriteLine($"source revision      : {tiff.SourceRevisionId}");
        Console.WriteLine($"output revision id   : {attempt.OutputRevisionId}");
        Console.WriteLine($"step state           : {step.State}");
        Console.WriteLine($"adapter notes        : {attempt.AdapterNotes}");
        Console.WriteLine("cleanup policy       : TIFF, database and workspace retained; " +
            "operator closes the synthetic document manually with Don't Save");

        // §36's required end-to-end facts.
        attempt.Status.ShouldBe(AttemptStatus.Succeeded);
        attempt.OutputRevisionId.ShouldBe(tiff.Id);
        attempt.AdapterNotes.ShouldNotBeNullOrWhiteSpace();
        step.State.ShouldBe(StepState.ReviewRequired);
        step.CurrentRevisionId.ShouldBe(tiff.Id);

        aggregate.Revisions.Count(r => r.Operation == OperationKind.PhotoshopOutput).ShouldBe(1);
        aggregate.Outputs.Count.ShouldBe(1);
        aggregate.Outputs.Single().ReviewState.ShouldBe(ReviewState.NotReviewed);

        tiff.File.Area.ShouldBe(WorkspaceArea.Working);
        tiff.File.FileName.ShouldEndWith("_CMYK_W.tif");
        File.Exists(tiffPath).ShouldBeTrue();

        // The recorded hash is the hash of the bytes actually on disk, read again here.
        FileFacts onDisk = Accept(await new WicFileInspector()
            .InspectAsync(tiffPath, CancellationToken.None));
        onDisk.Sha256.ShouldBe(tiff.Facts.Sha256);
        onDisk.ByteLength.ShouldBe(tiff.Facts.ByteLength);

        // The upstream source is unchanged, and only one TIFF was produced.
        Revision source = aggregate.Revisions.Single(r => r.Id == tiff.SourceRevisionId!.Value);
        source.InvalidatedAtUtc.ShouldBeNull();
        File.Exists(workspace.ResolveAbsolute(source.File)).ShouldBeTrue();
        Directory.GetFiles(root, "*.tif", SearchOption.AllDirectories).Length.ShouldBe(1);

        // Nothing was approved and nothing was promoted: C2A ends here (§28).
        aggregate.Session.State.ShouldNotBe(SessionState.Completed);
        Directory.GetFiles(root, "*.tif", SearchOption.AllDirectories)
            .ShouldNotContain(path => path.Contains(
                $"{Path.DirectorySeparatorChar}Approved{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal));

        Console.WriteLine("ReviewRequired reached. No approval, rejection or promotion was performed.");
    }

    /// <summary>
    /// The controlled seam's gate, used by this smoke and nothing else
    /// (Epic 11400 Part C2A §26).
    /// </summary>
    /// <remarks>
    /// It exists in the test project on purpose. A permissive gate that shipped in
    /// Infrastructure would be one registration away from being the real one, and Epic 11500 —
    /// not C2A — owns actually verifying a workstation. Here it can only ever be reached by a
    /// caller that constructed it explicitly.
    /// </remarks>
    private sealed class ControlledSeamEnvironmentGate : IEnvironmentGate
    {
        public OperationResult<PrintFlow.Domain.Results.Unit> Verify(AdapterExecutionMode mode) =>
            OperationResult.Ok();
    }

    private static T Accept<T>(OperationResult<T> result)
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

        current.ShouldNotBeNull();
        return Path.Combine(current.FullName, relativePath);
    }
}
