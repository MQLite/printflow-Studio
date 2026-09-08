using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PrintFlow.App.ViewModels;
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
/// The SCRUM-11104 live proof: one <b>real</b> Photoshop-produced production TIFF taken through
/// the real product flow to final review, inspected in all three modes, independently verified,
/// approved, and reopened after a restart (§50–§54, §60).
/// </summary>
/// <remarks>
/// <b>What is real here, and what is not.</b> The file under review is
/// <c>FIX-CUSTOMER-DESIGN-001_W1-1PX.tif</c> — 3307 × 4474, separated CMYK with a Photoshop W1
/// spot channel, produced by the validated production Action on the accepted workstation and
/// held as the frozen Epic 11000 baseline. Every layer above it is the product's own: the real
/// <c>SessionService</c>, the real SQLite repository and migrations, the real
/// <c>FileWorkspace</c>, the real signed preset provider, the real
/// <c>ProductionTiffInspector</c>, the real review decoder and the real
/// <c>SessionViewModel</c>.
/// <para>
/// What does not happen is a fresh Photoshop run. §60 permits reusing an already generated
/// validated TIFF where practical when only review, decoder and UI changed — which is this slice
/// exactly — and the reuse is not a convenience here: Photoshop is not installed on the machine
/// this was executed on, so the honest choice was between real production bytes with no new
/// Photoshop run and synthetic bytes with one. The bytes matter more (§51).
/// </para>
/// <para>
/// The source is a synthetic design at the baseline TIFF's own geometry, so the preparation the
/// session records and the file the review decodes describe the same output rather than two
/// unrelated sizes. Nothing customer-owned is touched.
/// </para>
/// <para>
/// Opt-in like every workstation smoke. It writes its evidence under the real workspace root's
/// <c>QA\</c> area and prints it, so a reviewer can check every number by hand.
/// </para>
/// </remarks>
public sealed class TiffFinalReviewWorkstationSmoke
{
    private const string EnableVariable = "PRINTFLOW_TIFF_FINAL_REVIEW_SMOKE";

    private const string BaselineTiff =
        @"D:\PrintFlowStudio\Baseline\workstation-v1\actions\authoring\FIX-CUSTOMER-DESIGN-001_W1-1PX.tif";

    /// <summary>The baseline TIFF's own geometry, which the synthetic source is cut to match.</summary>
    private const int BaselineWidth = 3307;

    /// <inheritdoc cref="BaselineWidth" />
    private const int BaselineHeight = 4474;

    private const double MillimetresPerInch = 25.4;

    [Fact]
    public async Task A_real_production_TIFF_is_reviewed_in_three_modes_verified_and_approved()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            return;
        }

        File.Exists(BaselineTiff).ShouldBeTrue(
            "the accepted Epic 11000 baseline TIFF is the real production artefact this proves against.");

        PrintFlowConfiguration configuration = PrintFlowConfiguration.LoadFromFile(
            RepositoryFile("appsettings.json"));

        string token = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        string root = Path.Combine(configuration.Workspace.Root, "QA", "SCRUM-11104", token);
        Directory.CreateDirectory(root);

        FileWorkspace workspace = new(root);
        string manifest = Path.Combine(configuration.Workspace.Root, configuration.Preset.Path);
        Sha256 presetSha = Sha256.Parse(configuration.Preset.ExpectedSha256);

        string databasePath = Path.Combine(root, "printflow-11104.db");
        SqliteConnectionFactory factory = new(databasePath);
        using (SqliteConnection connection = factory.Open())
        {
            MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();
        }

        BaselineProductionTiffProcessor photoshop = new(workspace, BaselineTiff);

        ISessionService service = Compose(factory, workspace, manifest, configuration, presetSha, photoshop);

        // A synthetic production design at the baseline TIFF's own geometry. Never customer
        // artwork, and never the baseline PNG — which the Action cropped, and whose pixels would
        // therefore describe a different output from the TIFF beside it.
        string sourcePath = Path.Combine(root, $"PF_11104_{token}.png");
        File.WriteAllBytes(sourcePath, SyntheticImages.PngWithAlpha(
            BaselineWidth,
            BaselineHeight,
            static (x, y) => (x / 64 % 2) == (y / 64 % 2) ? byte.MaxValue : (byte)0,
            dpi: 300));

        Console.WriteLine($"controlled workspace : {root}");
        Console.WriteLine($"database             : {databasePath}");
        Console.WriteLine($"preset               : v{configuration.Preset.Version} / {presetSha}");
        Console.WriteLine($"production TIFF      : {BaselineTiff} (real Photoshop output, Epic 11000 baseline)");
        Console.WriteLine($"synthetic source     : {sourcePath} ({BaselineWidth}x{BaselineHeight} px @ 300 ppi)");

        // ---- the real product flow, to ReviewRequired -----------------------------------

        SessionId id = Accept(await service.ImportAsync(
            WorkflowType.GeneratePrintTiff, sourcePath, $"PF_11104_{token}", "qa",
            CancellationToken.None)).Id;

        Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal("synthetic finished design"), "qa",
            CancellationToken.None));

        // Bounds the source already fits, so the plan is ResolutionOnly and the projected pixels
        // are the source's own — which are the TIFF's. Nothing is resampled and nothing enlarged.
        double widthMm = BaselineWidth * MillimetresPerInch / PrintDimensions.ProductionDpi;
        double heightMm = BaselineHeight * MillimetresPerInch / PrintDimensions.ProductionDpi;
        PrintDimensions dimensions = PrintDimensions.FromMillimetres(
            Math.Ceiling(widthMm), Math.Ceiling(heightMm), SizePreset.Custom);

        Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.SetPrintDimensions(dimensions), "qa", CancellationToken.None));
        Accept(await service.ExecuteAsync(
            id,
            new WorkflowCommand.SelectWhiteUnderbaseBranch(WhiteUnderbaseBranch.W1_1px, "synthetic design"),
            "qa",
            CancellationToken.None));

        Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "qa", CancellationToken.None));

        SqliteSessionRepository repository = new(factory);
        SessionAggregate reviewable = (await repository.LoadAsync(id, CancellationToken.None)).Value!;

        reviewable.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State
            .ShouldBe(StepState.ReviewRequired);

        Revision tiff = reviewable.Revisions.Single(r => r.Operation == OperationKind.PhotoshopOutput);
        PrintOutput output = reviewable.Outputs.Single();
        PhotoshopPreparation preparation = reviewable.Attempts
            .Single(a => a.OutputRevisionId == tiff.Id).Preparation!;
        string outputPath = workspace.ResolveAbsolute(output.File);

        // ---- the screen an operator actually uses ---------------------------------------

        IProductionTiffReviewService reviews = new ProductionTiffReviewService(
            repository, new ProductionTiffReviewDecoder(workspace), workspace);

        SessionViewModel screen = new(
            service, new ArtefactPreviewService(repository, new WicImagePreviewDecoder(workspace)),
            reviews, new SmokeNavigation());

        screen.Open(Accept(await service.LoadAsync(id, CancellationToken.None)));
        await screen.PreviewsLoaded;

        screen.IsProductionTiffReview.ShouldBeTrue();
        screen.HasTiffReview.ShouldBeTrue(
            "the specialist surface must come up for a real production TIFF.");

        // Colour -> White ink -> Overlay, exactly as an operator moves through them (§50).
        Dictionary<string, int> payloadBytes = [];
        foreach (TiffReviewMode mode in new[]
                 { TiffReviewMode.Colour, TiffReviewMode.WhiteInk, TiffReviewMode.Overlay })
        {
            screen.TiffReviewMode = mode;
            payloadBytes[mode.ToString()] = mode switch
            {
                TiffReviewMode.WhiteInk => screen.TiffWhiteInkPayload.Length,
                TiffReviewMode.Overlay => screen.TiffOverlayPayload.Length,
                _ => screen.TiffColourPayload.Length,
            };
        }

        // Zoom and pan, then confirm the viewport survived a mode switch (§33, §34).
        screen.IsFitToViewport = false;
        screen.ZoomScale = 2.0;
        screen.ReviewViewport.HorizontalPosition = 0.75;
        screen.ReviewViewport.VerticalPosition = 0.25;
        screen.TiffReviewMode = TiffReviewMode.WhiteInk;

        screen.ZoomScale.ShouldBe(2.0);
        screen.ReviewViewport.HorizontalPosition.ShouldBe(0.75);
        screen.ReviewViewport.VerticalPosition.ShouldBe(0.25);

        // ---- independent verification, not the view model agreeing with itself -----------

        // §51: the W1 preview and the inspector read the same fifth sample.
        ProductionTiffFacts facts = Accept(new ProductionTiffInspector().Inspect(outputPath));
        string whiteInk = Value(screen, "WhiteInk");
        whiteInk.ShouldContain(facts.W1NonWhiteSampleCount.ToString("N0", CultureInfo.CurrentCulture));

        // §52: effective source resolution, recomputed here from the persisted preparation.
        double physicalWidthMm =
            preparation.ProjectedPixelWidth * MillimetresPerInch / preparation.ProductionDpi;
        double physicalHeightMm =
            preparation.ProjectedPixelHeight * MillimetresPerInch / preparation.ProductionDpi;
        double effectiveX = preparation.SourcePixelWidth / (physicalWidthMm / MillimetresPerInch);
        double effectiveY = preparation.SourcePixelHeight / (physicalHeightMm / MillimetresPerInch);

        string effective = Value(screen, "EffectiveDpi");
        effective.ShouldContain(Math.Round(effectiveX).ToString("0", CultureInfo.CurrentCulture));
        effective.ShouldContain(Math.Round(effectiveY).ToString("0", CultureInfo.CurrentCulture));

        // §53: the path on screen is the managed output, and its bytes are the reviewed bytes.
        screen.TiffOutputPath.ShouldBe(outputPath);
        Value(screen, "OutputPath").ShouldBe(outputPath);
        HashOf(outputPath).ShouldBe(output.Sha256.Value);
        HashOf(BaselineTiff).ShouldBe(output.Sha256.Value);

        Console.WriteLine($"reviewed PrintOutput : {output.Id.Value}");
        Console.WriteLine($"reviewed SHA-256     : {output.Sha256}");
        Console.WriteLine($"output path          : {outputPath}");
        Console.WriteLine($"TIFF pixels          : {facts.PixelWidth}x{facts.PixelHeight} @ " +
            $"{facts.XResolutionDpi}x{facts.YResolutionDpi} ppi");
        Console.WriteLine($"W1 ink samples       : {facts.W1NonWhiteSampleCount}");
        Console.WriteLine($"physical size        : {physicalWidthMm:0.0} x {physicalHeightMm:0.0} mm");
        Console.WriteLine($"effective source dpi : {effectiveX:0.0} x {effectiveY:0.0} PPI");
        foreach (TiffMetadataRow row in screen.TiffMetadata)
        {
            Console.WriteLine($"  {row.Key,-14}: {row.Value}");
        }

        // ---- approve, still bound to the exact hash -------------------------------------

        await screen.ApproveCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();
        await screen.CompleteCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        SessionAggregate approved = (await repository.LoadAsync(id, CancellationToken.None)).Value!;
        PrintOutput promoted = approved.Outputs.Single();

        promoted.ReviewState.ShouldBe(ReviewState.Approved);
        promoted.Sha256.ShouldBe(output.Sha256);
        promoted.File.Area.ShouldBe(WorkspaceArea.Approved);
        approved.Session.State.ShouldBe(SessionState.Completed);
        HashOf(workspace.ResolveAbsolute(promoted.File)).ShouldBe(output.Sha256.Value);

        // ---- restart: same output, same hash, same metadata, no regeneration (§54) -------

        int runsBeforeRestart = photoshop.GenerateCount;

        ISessionService restartedService = Compose(
            factory, workspace, manifest, configuration, presetSha, photoshop);
        SqliteSessionRepository restartedRepository = new(factory);

        SessionViewModel restarted = new(
            restartedService,
            new ArtefactPreviewService(restartedRepository, new WicImagePreviewDecoder(workspace)),
            new ProductionTiffReviewService(
                restartedRepository, new ProductionTiffReviewDecoder(workspace), workspace),
            new SmokeNavigation());

        OperationResult<TiffReviewPayload> reconstructed = await new ProductionTiffReviewService(
                restartedRepository, new ProductionTiffReviewDecoder(workspace), workspace)
            .GetReviewAsync(id, tiff.Id, CancellationToken.None);

        reconstructed.IsSuccess.ShouldBeTrue(
            reconstructed.IsFailure ? reconstructed.Failure.ToString() : string.Empty);
        reconstructed.Value.PrintOutputId.ShouldBe(output.Id);
        reconstructed.Value.Sha256.ShouldBe(output.Sha256);
        reconstructed.Value.PixelWidth.ShouldBe(BaselineWidth);
        reconstructed.Value.PixelHeight.ShouldBe(BaselineHeight);
        reconstructed.Value.WhiteInkSampleCount.ShouldBe(facts.W1NonWhiteSampleCount);

        photoshop.GenerateCount.ShouldBe(runsBeforeRestart,
            "reopening a reviewed output must never rerun the Photoshop step.");

        // ---- evidence ------------------------------------------------------------------

        string evidencePath = Path.Combine(root, "scrum-11104-final-review-evidence.json");
        await File.WriteAllTextAsync(evidencePath, JsonSerializer.Serialize(new
        {
            token,
            sessionId = id.Value,
            printOutputId = output.Id.Value,
            sha256 = output.Sha256.Value,
            outputPath,
            baselineTiff = BaselineTiff,
            pixels = new { facts.PixelWidth, facts.PixelHeight },
            tiffResolution = new { facts.XResolutionDpi, facts.YResolutionDpi },
            physicalMillimetres = new { width = physicalWidthMm, height = physicalHeightMm },
            sourcePixels = new { preparation.SourcePixelWidth, preparation.SourcePixelHeight },
            effectiveSourceDpi = new { x = effectiveX, y = effectiveY },
            w1InkSamples = facts.W1NonWhiteSampleCount,
            reviewPayloadBytes = payloadBytes,
            metadata = screen.TiffMetadata.ToDictionary(row => row.Key, row => row.Value),
            approvedArea = promoted.File.Area.ToString(),
            sessionState = approved.Session.State.ToString(),
            photoshopRuns = photoshop.GenerateCount,
        }, new JsonSerializerOptions { WriteIndented = true }));

        Console.WriteLine($"evidence             : {evidencePath}");
        Console.WriteLine("RESULT               : final review verified, approved, and reconstructed after restart.");

        restarted.ShouldNotBeNull();
    }

    private static ISessionService Compose(
        SqliteConnectionFactory factory,
        FileWorkspace workspace,
        string manifest,
        PrintFlowConfiguration configuration,
        Sha256 presetSha,
        IPhotoshopOutputProcessor photoshop) => new SessionService(
        WorkflowEngine.Instance,
        new SqliteSessionRepository(factory),
        workspace,
        new RecycleBin(),
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

    /// <summary>
    /// Installs the real accepted baseline TIFF at the reserved output (§60).
    /// </summary>
    /// <remarks>
    /// Not a claim that Photoshop ran here — it did not, and the note says so. What it is, is the
    /// only way to put genuinely Photoshop-produced separated-CMYK + W1 bytes in front of the
    /// review path on a machine where Photoshop is not installed. Everything downstream of the
    /// copy is the product's own: the file is validated, hashed, recorded and reviewed exactly as
    /// a fresh production run's would be.
    /// </remarks>
    private sealed class BaselineProductionTiffProcessor : IPhotoshopOutputProcessor
    {
        private readonly IWorkspace _workspace;
        private readonly string _baseline;

        public BaselineProductionTiffProcessor(IWorkspace workspace, string baseline)
        {
            _workspace = workspace;
            _baseline = baseline;
        }

        public int GenerateCount { get; private set; }

        public string AdapterId => "baseline-production-tiff-v1";

        public AdapterExecutionMode Mode => AdapterExecutionMode.Fake;

        public Task<OperationResult<AdapterOutput>> GenerateAsync(
            PhotoshopRequest request, CancellationToken cancellationToken)
        {
            GenerateCount++;
            File.Copy(_baseline, _workspace.ResolveAbsolute(request.ExpectedOutput), overwrite: true);

            return Task.FromResult(OperationResult.Ok(new AdapterOutput(
                request.ExpectedOutput,
                TimeSpan.Zero,
                $"accepted Epic 11000 baseline production TIFF installed from '{_baseline}'; " +
                "no Photoshop ran in this session.")));
        }
    }

    /// <summary>The controlled seam's gate, used by this smoke and nothing else.</summary>
    private sealed class ControlledSeamEnvironmentGate : IEnvironmentGate
    {
        public OperationResult<PrintFlow.Domain.Results.Unit> Verify(AdapterExecutionMode mode) =>
            OperationResult.Ok();
    }

    /// <summary>Navigation the smoke never uses; the screen simply requires one.</summary>
    private sealed class SmokeNavigation : PrintFlow.App.Navigation.INavigationService
    {
        public object? Current => null;

        public event EventHandler? CurrentChanged
        {
            add { }
            remove { }
        }

        public Task GoHomeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task GoToEnvironmentReadinessAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task GoToSettingsAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task GoToErrorDetailsAsync(SessionId sessionId, AttemptId attemptId,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public void GoToWorkflowSelection(SessionView session)
        {
        }

        public void GoToSession(SessionView session)
        {
        }
    }

    private static string Value(SessionViewModel screen, string key) =>
        screen.TiffMetadata.Single(row => row.Key == key).Value;

    private static string HashOf(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Sha256.FromBytes(SHA256.HashData(stream)).Value;
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
