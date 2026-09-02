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
/// The C2B live proof: one real Photoshop TIFF, reviewed and approved, ending in an approved
/// deliverable and a completed session (Epic 11400 Part C2B §37).
/// </summary>
/// <remarks>
/// Opt-in, like every workstation smoke, because it drives a live Photoshop on the operator's
/// desktop. It takes a <b>fresh</b> synthetic job rather than the retained C2A QA session: an
/// approval run over a session another slice left behind would be approving a file whose history
/// this slice did not produce, and the point is the whole path (§37).
/// <para>
/// The gate is bypassed here and only here, by handing this one <c>SessionService</c> a
/// <see cref="ControlledSeamEnvironmentGate"/>. <c>Adapters.Mode</c> stays <c>Fake</c> and the
/// application's own composition is untouched (§39).
/// </para>
/// <para>
/// The rejection path is deliberately <i>not</i> run against live Photoshop. It is covered
/// deterministically end to end by <c>PhotoshopTiffFinalReviewTests</c>, including the exact
/// Recycle Bin call, the failure and the crash points — none of which a second live run could
/// establish better, and all of which it would cost another real Photoshop job to watch (§37).
/// </para>
/// </remarks>
public sealed class PhotoshopFinalReviewWorkstationSmoke
{
    private const string EnableVariable = "PRINTFLOW_PHOTOSHOP_FINAL_REVIEW_SMOKE";

    [Fact]
    public async Task One_real_Photoshop_TIFF_is_approved_into_Approved_and_completes_the_session()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1") return;

        PrintFlowConfiguration configuration = PrintFlowConfiguration.LoadFromFile(
            RepositoryFile("appsettings.json"));

        // Reported rather than asserted since Epic 11500 Part D. This smoke composes its own
        // adapter and its own gate, so what the installation ships is irrelevant to it — and that
        // independence is the property worth keeping. It read `ShouldBe("Fake")` while Production
        // composition was closed and the seam was the only way to reach the production path.
        Console.WriteLine($"committed Adapters.Mode: {configuration.Adapters.Mode} (this seam composes its own)");

        string token = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss") + "-" +
                       Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        string root = Path.Combine(configuration.Workspace.Root, "QA", "Epic11400C2B", token);
        string evidence = Path.Combine(root, "Evidence");
        Directory.CreateDirectory(root);

        FileWorkspace workspace = new(root);
        string manifest = Path.Combine(configuration.Workspace.Root, configuration.Preset.Path);
        Sha256 presetSha = Sha256.Parse(configuration.Preset.ExpectedSha256);

        string databasePath = Path.Combine(root, "printflow-c2b.db");
        SqliteConnectionFactory factory = new(databasePath);
        using (SqliteConnection connection = factory.Open())
        {
            MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();
        }

        IPhotoshopOutputProcessor photoshop = PhotoshopAutomationComposition.CreateProductionProcessor(
            manifest, presetSha, workspace, evidence, TimeProvider.System);
        photoshop.Mode.ShouldBe(AdapterExecutionMode.Production);

        // The real Recycle Bin, not a double. Nothing in the approval path calls it, and that is
        // worth proving with the real one wired in: an approval that disposed of anything would
        // show up here rather than in a fake's call log.
        RecycleBin recycleBin = new();

        ISessionService service = new SessionService(
            WorkflowEngine.Instance,
            new SqliteSessionRepository(factory),
            workspace,
            recycleBin,
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

        // A fresh synthetic source, never customer artwork.
        string sourcePath = Path.Combine(root, $"PF_C2B_{token}.png");
        File.WriteAllBytes(sourcePath, SyntheticImages.PngWithAlpha(
            1200, 800, static (_, _) => byte.MaxValue, dpi: 240));

        Console.WriteLine($"controlled workspace : {root}");
        Console.WriteLine($"database             : {databasePath}");
        Console.WriteLine($"preset               : v{configuration.Preset.Version} / {presetSha}");
        Console.WriteLine($"global Adapters.Mode : {configuration.Adapters.Mode} (unchanged)");
        Console.WriteLine($"adapter              : {photoshop.AdapterId} / {photoshop.Mode}");
        Console.WriteLine($"synthetic source     : {sourcePath}");

        SessionId id = Accept(await service.ImportAsync(
            WorkflowType.GeneratePrintTiff, sourcePath, $"PF_C2B_{token}", "qa",
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

        SessionAggregate reviewable = (await new SqliteSessionRepository(factory)
            .LoadAsync(id, CancellationToken.None)).Value!;

        if (produced.IsFailure)
        {
            Console.WriteLine($"RESULT               : REFUSED — {produced.Failure.Code}");
            Console.WriteLine($"detail               : {produced.Failure.TechnicalDetail}");
            foreach (KeyValuePair<string, string> entry in produced.Failure.Context)
            {
                Console.WriteLine($"  {entry.Key,-24}: {entry.Value}");
            }

            reviewable.Revisions.ShouldNotContain(r => r.Operation == OperationKind.PhotoshopOutput);
            reviewable.Outputs.ShouldBeEmpty();
            Console.WriteLine("no Revision was created; there is nothing to review or approve.");
            return;
        }

        Revision tiff = reviewable.Revisions.Single(r => r.Operation == OperationKind.PhotoshopOutput);
        string workingPath = workspace.ResolveAbsolute(tiff.File);

        reviewable.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State
            .ShouldBe(StepState.ReviewRequired);
        tiff.File.Area.ShouldBe(WorkspaceArea.Working);

        Console.WriteLine("RESULT               : ReviewRequired");
        Console.WriteLine($"produced TIFF        : {tiff.File.RelativePath}");
        Console.WriteLine($"on disk              : {workingPath}");
        Console.WriteLine($"TIFF SHA-256         : {tiff.Facts.Sha256}");
        Console.WriteLine($"TIFF bytes           : {tiff.Facts.ByteLength}");
        Console.WriteLine($"TIFF pixels          : {tiff.Facts.PixelWidth}x{tiff.Facts.PixelHeight}");
        Console.WriteLine($"adapter notes        : {reviewable.Attempts.Last(a => a.Step == StepKind.PhotoshopOutput).AdapterNotes}");
        Console.WriteLine("^ open this file and look at it before reading the approval below (§37).");

        byte[] reviewedBytes = File.ReadAllBytes(workingPath);

        // The decision, taken over exactly the bytes the review named.
        Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.Approve(StepKind.PhotoshopOutput, tiff.Facts.Sha256), "qa",
            CancellationToken.None));
        Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.Complete(), "qa", CancellationToken.None));

        SessionAggregate approved = (await new SqliteSessionRepository(factory)
            .LoadAsync(id, CancellationToken.None)).Value!;

        PrintOutput output = approved.Outputs.Single();
        string approvedPath = workspace.ResolveAbsolute(output.File);
        ProcessingAttempt attempt = approved.Attempts.Single(a => a.Step == StepKind.PhotoshopOutput);

        Console.WriteLine("RESULT               : APPROVED");
        Console.WriteLine($"approved TIFF        : {output.File.RelativePath}");
        Console.WriteLine($"on disk              : {approvedPath}");
        Console.WriteLine($"approved SHA-256     : {output.Sha256}");
        Console.WriteLine($"review state         : {output.ReviewState}");
        Console.WriteLine($"session state        : {approved.Session.State}");
        Console.WriteLine($"attempt              : {attempt.Id} / {attempt.Status}");
        Console.WriteLine($"revisions (TIFF)     : {approved.Revisions.Count(r => r.Operation == OperationKind.PhotoshopOutput)}");
        Console.WriteLine("cleanup policy       : TIFF, database and workspace retained; " +
            "operator closes the synthetic document manually with Don't Save");

        // §37's required facts, and §8's.
        output.File.Area.ShouldBe(WorkspaceArea.Approved);
        output.ReviewState.ShouldBe(ReviewState.Approved);
        output.PromotionReservation.ShouldBeNull();
        output.Sha256.ShouldBe(tiff.Facts.Sha256);
        File.ReadAllBytes(approvedPath).ShouldBe(reviewedBytes);

        FileFacts promoted = Accept(await new WicFileInspector()
            .InspectAsync(approvedPath, CancellationToken.None));
        promoted.Sha256.ShouldBe(tiff.Facts.Sha256);
        promoted.ByteLength.ShouldBe(tiff.Facts.ByteLength);

        approved.Session.State.ShouldBe(SessionState.Completed);
        approved.Reviews.Count(r => r.Step == StepKind.PhotoshopOutput).ShouldBe(1);
        approved.Revisions.Count(r => r.Operation == OperationKind.PhotoshopOutput).ShouldBe(1);

        // Exactly one file under Approved\, and the producing artefact still where it was made.
        string approvedDirectory = Path.Combine(
            workspace.ResolveAbsoluteDirectory(approved.Session.Workspace), "Approved");
        Directory.GetFiles(approvedDirectory, "*", SearchOption.AllDirectories).Length.ShouldBe(1);
        File.Exists(workingPath).ShouldBeTrue();

        Console.WriteLine("approved TIFF is byte-identical to the reviewed TIFF; session Completed.");
    }

    /// <summary>The controlled seam's gate, used by this smoke and nothing else (§39).</summary>
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
