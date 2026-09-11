using System.IO;
using Microsoft.Data.Sqlite;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Automation;
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
/// The Epic 11400 final gate's controlled Production workflow matrix: both workflows that depend
/// on <c>PhotoshopOutput</c>, driven end to end against a live Photoshop
/// (Epic 11400 Final Gate §10, §11, §12, §14).
/// </summary>
/// <remarks>
/// Opt-in, like every workstation smoke. It exists because the slice smokes each proved one path:
/// C2A reached <c>ReviewRequired</c> through GENERATE_PRINT_TIFF, C2B approved one TIFF through the
/// same workflow. Neither ran PREPARE_CUSTOMER_DESIGN, whose Photoshop step consumes a trimmed
/// Revision rather than the imported source, and neither exercised a preset-fit size or a rejection
/// against real Photoshop bytes.
/// <para>
/// Two jobs, chosen so that together they cover what §10 asks for without repeating the B1A.3,
/// B1B or C1 matrices those slices already accepted:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Job A</b> — PREPARE_CUSTOMER_DESIGN, a named-preset fit that shrinks, branch
///     <c>W1_2px</c>, approved through to a Completed session (§11).
///   </item>
///   <item>
///     <b>Job B</b> — GENERATE_PRINT_TIFF, a custom target edge overriding a named preset, branch
///     <c>W1_1px</c>, rejected with a typed reason and retried into a fresh attempt (§12).
///   </item>
/// </list>
/// <para>
/// The gate is bypassed here and only here, through a <see cref="ControlledSeamEnvironmentGate"/>
/// passed to these two <c>SessionService</c> instances. <c>Adapters.Mode</c> stays <c>Fake</c> and
/// the application's own composition is untouched (§27).
/// </para>
/// </remarks>
public sealed class PhotoshopFinalGateWorkstationSmoke
{
    private const string EnableVariable = "PRINTFLOW_PHOTOSHOP_FINAL_GATE_SMOKE";

    /// <summary>
    /// Job A: a customer design, fitted to a named preset, approved into <c>Approved\</c>
    /// (Final Gate §10 A, §11, §14).
    /// </summary>
    [Fact]
    public async Task PREPARE_CUSTOMER_DESIGN_fits_a_named_preset_and_approves_a_real_TIFF()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1") return;

        using Controlled controlled = Controlled.Create("A-customer-design");
        ISessionService service = controlled.Service;

        // A design with a transparent border, so the deterministic Trim has real work to do and the
        // Photoshop step consumes a trimmed Revision rather than the imported source.
        string sourcePath = controlled.WriteSource(2400, 1600, static (x, y) =>
            x is >= 200 and <= 2199 && y is >= 150 and <= 1449 ? byte.MaxValue : (byte)0);

        SessionId id = Accept(await service.ImportAsync(
            WorkflowType.PrepareCustomerDesign, sourcePath, controlled.OutputName, "qa",
            CancellationToken.None)).Id;

        Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal("synthetic customer design"), "qa", CancellationToken.None));

        // Meitu is the deterministic fake in this composition, so both of its steps are skipped
        // rather than pretended: this smoke is about the Photoshop path.
        Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.Skip(StepKind.Enhancement, "not part of the Photoshop gate"),
            "qa", CancellationToken.None));
        Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.Skip(StepKind.BackgroundRemoval, "already transparent"),
            "qa", CancellationToken.None));

        // PrintFlow's own pixel work, never a double.
        Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Trim), "qa", CancellationToken.None));

        SessionAggregate trimmed = await controlled.LoadAsync(id);
        Revision trim = trimmed.Revisions.Last(r => r.Operation == OperationKind.Trim);
        Console.WriteLine($"trimmed revision     : {trim.File.FileName} " +
                          $"{trim.Facts.PixelWidth}x{trim.Facts.PixelHeight}");

        Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.Approve(StepKind.Trim, trim.Sha256), "qa", CancellationToken.None));

        // §10's preset-fit case: A5's configured long-edge recommendation, which shrinks these
        // pixels. The millimetres are not supplied — the command carries the preset and the
        // workflow resolves the recommendation from the verified manifest.
        Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.SetPresetFitSize(SizePreset.A5), "qa", CancellationToken.None));
        Accept(await service.ExecuteAsync(
            id,
            new WorkflowCommand.SelectWhiteUnderbaseBranch(
                WhiteUnderbaseBranch.W1_2px, "solid rectangular design"),
            "qa",
            CancellationToken.None));

        SessionAggregate sized = await controlled.LoadAsync(id);
        Console.WriteLine($"size decision        : preset {SizePreset.A5} → " +
                          $"{sized.Session.Dimensions?.WidthMm}x{sized.Session.Dimensions?.HeightMm} mm");
        Console.WriteLine($"W1 branch            : {WhiteUnderbaseBranch.W1_2px}");
        Console.WriteLine("starting real Photoshop run (job A) ...");

        OperationResult<SessionView> produced = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "qa", CancellationToken.None);
        if (controlled.ReportRefusalIfAny(produced, await controlled.LoadAsync(id))) return;

        SessionAggregate reviewable = await controlled.LoadAsync(id);
        Revision tiff = reviewable.Revisions.Single(r => r.Operation == OperationKind.PhotoshopOutput);
        string workingPath = controlled.Workspace.ResolveAbsolute(tiff.File);
        byte[] reviewedBytes = File.ReadAllBytes(workingPath);

        controlled.ReportTiff("job A TIFF", reviewable, tiff, workingPath);
        reviewable.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State
            .ShouldBe(StepState.ReviewRequired);

        Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.Approve(StepKind.PhotoshopOutput, tiff.Sha256), "qa",
            CancellationToken.None));
        Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.Complete(), "qa", CancellationToken.None));

        SessionAggregate approved = await controlled.LoadAsync(id);
        PrintOutput output = approved.Outputs.Single();
        string approvedPath = controlled.Workspace.ResolveAbsolute(output.File);

        Console.WriteLine($"approved TIFF        : {output.File.RelativePath}");
        Console.WriteLine($"approved SHA-256     : {output.Sha256}");
        Console.WriteLine($"session state        : {approved.Session.State}");

        // §11's required end state.
        approved.Revisions.Count(r => r.Operation == OperationKind.PhotoshopOutput).ShouldBe(1);
        approved.Outputs.Count.ShouldBe(1);
        approved.Reviews.Count(r => r.Step == StepKind.PhotoshopOutput).ShouldBe(1);
        output.File.Area.ShouldBe(WorkspaceArea.Approved);
        output.ReviewState.ShouldBe(ReviewState.Approved);
        output.Sha256.ShouldBe(tiff.Sha256);
        output.PromotionReservation.ShouldBeNull();
        File.ReadAllBytes(approvedPath).ShouldBe(reviewedBytes);
        approved.Session.State.ShouldBe(SessionState.Completed);

        Directory.GetFiles(
            Path.Combine(controlled.Workspace.ResolveAbsoluteDirectory(approved.Session.Workspace), "Approved"),
            "*", SearchOption.AllDirectories).Length.ShouldBe(1);

        // The producing Revision still names readable bytes, so its integrity guard still answers.
        File.Exists(workingPath).ShouldBeTrue();
        FileFacts stillThere = Accept(await new WicFileInspector()
            .InspectAsync(workingPath, CancellationToken.None));
        stillThere.Sha256.ShouldBe(tiff.Sha256);

        Console.WriteLine("job A: approved, byte-identical, session Completed.");
    }

    /// <summary>
    /// Job B: a print-TIFF job at a custom target edge overriding a named preset, rejected and
    /// retried into a fresh attempt (Final Gate §10 B, §12, §14).
    /// </summary>
    /// <remarks>
    /// It stops once the fresh retry attempt and its new output path are established, which is what
    /// §12 permits: producing a second real TIFF would re-prove C1's save and validation contract
    /// and add nothing about the rejection lifecycle.
    /// </remarks>
    [Fact]
    public async Task GENERATE_PRINT_TIFF_at_a_custom_target_edge_rejects_and_retries()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1") return;

        using Controlled controlled = Controlled.Create("B-print-tiff");
        ISessionService service = controlled.Service;

        string sourcePath = controlled.WriteSource(1200, 800, static (_, _) => byte.MaxValue, dpi: 240);

        SessionId id = Accept(await service.ImportAsync(
            WorkflowType.GeneratePrintTiff, sourcePath, controlled.OutputName, "qa",
            CancellationToken.None)).Id;

        // GENERATE_PRINT_TIFF assumes a finished design, so this step is an active confirmation
        // rather than an acknowledgement — but it is still reached from Waiting, which is what
        // ConfirmOriginal is legal from.
        Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal("synthetic finished design"), "qa",
            CancellationToken.None));

        // §10's custom/override case: one exact operator-chosen edge, recorded as an explicit
        // override of A4's recommendation. 50.8 mm at 300 ppi is exactly 600 px, so this shrinks.
        Accept(await service.ExecuteAsync(
            id,
            new WorkflowCommand.SetCustomTargetEdgeSize(TargetEdge.Width, 50.8m, SizePreset.A4),
            "qa",
            CancellationToken.None));
        Accept(await service.ExecuteAsync(
            id,
            new WorkflowCommand.SelectWhiteUnderbaseBranch(WhiteUnderbaseBranch.W1_1px, "ordinary design"),
            "qa",
            CancellationToken.None));

        SessionAggregate sized = await controlled.LoadAsync(id);
        Console.WriteLine($"size decision        : custom WIDTH 50.8 mm overriding {SizePreset.A4} → " +
                          $"{sized.Session.Dimensions?.WidthMm}x{sized.Session.Dimensions?.HeightMm} mm");
        Console.WriteLine($"W1 branch            : {WhiteUnderbaseBranch.W1_1px}");
        Console.WriteLine("starting real Photoshop run (job B) ...");

        OperationResult<SessionView> produced = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "qa", CancellationToken.None);
        if (controlled.ReportRefusalIfAny(produced, await controlled.LoadAsync(id))) return;

        SessionAggregate reviewable = await controlled.LoadAsync(id);
        Revision tiff = reviewable.Revisions.Single(r => r.Operation == OperationKind.PhotoshopOutput);
        string tiffPath = controlled.Workspace.ResolveAbsolute(tiff.File);
        AttemptId firstAttempt = reviewable.Attempts.Last(a => a.Step == StepKind.PhotoshopOutput).Id;

        controlled.ReportTiff("job B TIFF", reviewable, tiff, tiffPath);
        reviewable.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State
            .ShouldBe(StepState.ReviewRequired);

        // §12: a normal typed reason, over the exact reviewed bytes.
        Accept(await service.ExecuteAsync(
            id,
            new WorkflowCommand.Reject(
                StepKind.PhotoshopOutput, tiff.Sha256, RejectionReason.WhiteInkIssue,
                "final-gate rejection rehearsal"),
            "qa",
            CancellationToken.None));

        SessionAggregate rejected = await controlled.LoadAsync(id);
        PrintOutput output = rejected.Outputs.Single();

        Console.WriteLine($"rejected TIFF        : {tiff.File.RelativePath}");
        Console.WriteLine($"recycled at          : {output.RecycledAtUtc:O}");
        Console.WriteLine($"step state           : {rejected.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State}");

        // The exact reviewed TIFF went to the Windows Recycle Bin — the real one, not a double.
        File.Exists(tiffPath).ShouldBeFalse();
        output.ReviewState.ShouldBe(ReviewState.Rejected);
        output.RecycledAtUtc.ShouldNotBeNull();
        rejected.Reviews.Single(r => r.Step == StepKind.PhotoshopOutput).QuickReason
            .ShouldBe(RejectionReason.WhiteInkIssue);
        rejected.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State
            .ShouldBe(StepState.RetryRequired);
        rejected.Session.State.ShouldBe(SessionState.Active);

        string approvedDirectory = Path.Combine(
            controlled.Workspace.ResolveAbsoluteDirectory(rejected.Session.Workspace), "Approved");
        (Directory.Exists(approvedDirectory)
            ? Directory.GetFiles(approvedDirectory, "*", SearchOption.AllDirectories)
            : []).ShouldBeEmpty();

        // §12: Retry establishes a fresh attempt and a fresh output destination. The size decision
        // and the W1 branch are deliberately not reconfirmed.
        Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.Retry(StepKind.PhotoshopOutput), "qa", CancellationToken.None));

        SessionAggregate retried = await controlled.LoadAsync(id);
        retried.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State.ShouldBe(StepState.Waiting);
        retried.Session.Dimensions.ShouldNotBeNull();
        retried.Session.WhiteUnderbaseBranch.ShouldBe(WhiteUnderbaseBranch.W1_1px);
        retried.ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.PhotoshopOutput);

        Console.WriteLine($"retry state          : {StepState.Waiting}, first attempt {firstAttempt}");
        Console.WriteLine("job B: rejected, recycled, retryable with the size and branch retained.");
        Console.WriteLine("stopped before a second real Photoshop TIFF: §12 permits it, and the " +
                          "deterministic C2B tests already cover the rest of the lifecycle.");
    }

    /// <summary>
    /// Re-validates one named TIFF with the accepted C1 parser, after it has been promoted
    /// (Final Gate §14).
    /// </summary>
    /// <remarks>
    /// Points at a file rather than producing one, and that is deliberate: the question is whether
    /// the file an operator will hand to Maintop — the copy in <c>Approved\</c>, after promotion —
    /// still satisfies the accepted production structure, and the only way to ask that is to read
    /// those bytes. Byte-identity with the validated Working TIFF already implies it, but "implies"
    /// is an argument and this is a measurement (§14: do not rely only on the earlier C1 evidence).
    /// <para>
    /// The path arrives in <c>PRINTFLOW_FINAL_GATE_TIFF_PATH</c> rather than being searched for.
    /// A test that went looking through the workspace for something TIFF-shaped would be deciding
    /// for itself what was accepted.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_promoted_Approved_TIFF_still_passes_the_C1_structural_contract()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1") return;

        string? path = Environment.GetEnvironmentVariable("PRINTFLOW_FINAL_GATE_TIFF_PATH");
        if (string.IsNullOrWhiteSpace(path)) return;

        File.Exists(path).ShouldBeTrue($"No TIFF at '{path}'.");

        ProductionTiffFacts facts = Accept(new ProductionTiffInspector().Inspect(path));
        long byteLength = new FileInfo(path).Length;

        Console.WriteLine($"re-validated TIFF    : {path}");
        Console.WriteLine($"byte order           : {facts.ByteOrder}");
        Console.WriteLine($"pixels               : {facts.PixelWidth}x{facts.PixelHeight}");
        Console.WriteLine($"resolution           : {facts.XResolutionDpi}x{facts.YResolutionDpi} " +
                          $"(unit {facts.ResolutionUnit})");
        Console.WriteLine($"samples              : {facts.SamplesPerPixel} x " +
                          string.Join("/", facts.BitsPerSample) + " bit");
        Console.WriteLine($"photometric          : {facts.PhotometricInterpretation} " +
                          $"compression {facts.Compression} planar {facts.PlanarConfiguration}");
        Console.WriteLine($"extra channels       : {string.Join(", ", facts.ExtraChannelNames)}");
        Console.WriteLine($"W1 spot / non-white  : {facts.W1IsPhotoshopSpotChannel} / {facts.W1NonWhiteSampleCount}");
        Console.WriteLine($"alpha / pyramid      : {facts.HasAlphaOrTransparencySample} / {facts.HasImagePyramid}");
        Console.WriteLine($"layers / all RLE     : {facts.PhotoshopLayerCount} / {facts.AllPhotoshopLayerChannelsUseRle}");
        Console.WriteLine($"bytes                : {byteLength}");

        // Every fact §14 names, read from the promoted bytes.
        facts.ByteOrder.ShouldBe("IBM PC / little-endian");
        facts.XResolutionDpi.ShouldBe(300);
        facts.YResolutionDpi.ShouldBe(300);
        facts.ResolutionUnit.ShouldBe((ushort)2);                        // inches
        facts.Compression.ShouldBe((ushort)1);                           // image compression None
        facts.PlanarConfiguration.ShouldBe((ushort)1);                   // interleaved
        facts.SamplesPerPixel.ShouldBe((ushort)5);
        facts.BitsPerSample.ShouldBe([(ushort)8, (ushort)8, (ushort)8, (ushort)8, (ushort)8]);
        facts.PhotometricInterpretation.ShouldBe((ushort)5);             // separated CMYK
        facts.ExtraChannelNames.ShouldBe(["W1"]);
        facts.W1IsPhotoshopSpotChannel.ShouldBeTrue();
        facts.W1NonWhiteSampleCount.ShouldBeGreaterThan(0);
        facts.HasAlphaOrTransparencySample.ShouldBeFalse();
        facts.HasImagePyramid.ShouldBeFalse();
        facts.ImageFileDirectoryCount.ShouldBe(1);
        facts.PhotoshopLayerCount.ShouldBe(1);
        facts.AllPhotoshopLayerChannelsUseRle.ShouldBeTrue();

        Console.WriteLine("the promoted Approved TIFF satisfies the accepted C1 production structure.");
    }

    // -----------------------------------------------------------------------------------
    // The controlled seam
    // -----------------------------------------------------------------------------------

    /// <summary>One controlled Production composition over its own throwaway QA workspace.</summary>
    private sealed class Controlled : IDisposable
    {
        private Controlled(
            string root, FileWorkspace workspace, SqliteConnectionFactory factory,
            ISessionService service, string outputName)
        {
            Root = root;
            Workspace = workspace;
            _factory = factory;
            Service = service;
            OutputName = outputName;
        }

        private readonly SqliteConnectionFactory _factory;

        public string Root { get; }

        public FileWorkspace Workspace { get; }

        public ISessionService Service { get; }

        public string OutputName { get; }

        public static Controlled Create(string jobLabel)
        {
            PrintFlowConfiguration configuration = PrintFlowConfiguration.LoadFromFile(
                RepositoryFile("appsettings.json"));

            // Reported rather than asserted since Epic 11500 Part D. This smoke composes its own
            // adapter and its own gate, so what the installation ships is irrelevant to it — and
            // that independence is the property worth keeping. It read `ShouldBe("Fake")` while
            // Production composition was closed and the seam was the only way to reach the
            // production path; the installation has since been activated, and this run is
            // unaffected either way.
            Console.WriteLine($"committed Adapters.Mode: {configuration.Adapters.Mode} (this seam composes its own)");

            string token = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss") + "-" +
                           Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
            string root = Path.Combine(
                configuration.Workspace.Root, "QA", "Epic11400FinalGate", $"{jobLabel}-{token}");
            Directory.CreateDirectory(root);

            FileWorkspace workspace = new(root);
            string manifest = Path.Combine(configuration.Workspace.Root, configuration.Preset.Path);
            Sha256 presetSha = Sha256.Parse(configuration.Preset.ExpectedSha256);

            SqliteConnectionFactory factory = new(Path.Combine(root, "printflow-final-gate.db"));
            using (SqliteConnection connection = factory.Open())
            {
                MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();
            }

            IPhotoshopOutputProcessor photoshop = PhotoshopAutomationComposition.CreateProductionProcessor(
                manifest, presetSha, workspace, Path.Combine(root, "Evidence"), TimeProvider.System);
            photoshop.Mode.ShouldBe(AdapterExecutionMode.Production);

            ISessionService service = new SessionService(
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
                TimeProvider.System,
                automationLeases: new SqliteWorkstationAutomationLeaseManager());

            Console.WriteLine($"controlled workspace : {root}");
            Console.WriteLine($"preset               : v{configuration.Preset.Version} / {presetSha}");
            Console.WriteLine($"global Adapters.Mode : {configuration.Adapters.Mode} (unchanged)");
            Console.WriteLine($"adapter              : {photoshop.AdapterId} / {photoshop.Mode}");

            return new Controlled(root, workspace, factory, service, $"PF_FG_{token}");
        }

        /// <summary>Writes a fresh synthetic source. Never customer artwork.</summary>
        public string WriteSource(int width, int height, Func<int, int, byte> alpha, int dpi = 300)
        {
            string path = Path.Combine(Root, $"{OutputName}.png");
            File.WriteAllBytes(path, SyntheticImages.PngWithAlpha(width, height, alpha, dpi: dpi));
            Console.WriteLine($"synthetic source     : {path} ({width}x{height} @ {dpi} ppi)");
            return path;
        }

        public async Task<SessionAggregate> LoadAsync(SessionId id) =>
            (await new SqliteSessionRepository(_factory).LoadAsync(id, CancellationToken.None)).Value!;

        /// <summary>Prints and asserts an honest refusal, returning true when the run was refused.</summary>
        public bool ReportRefusalIfAny(OperationResult<SessionView> produced, SessionAggregate aggregate)
        {
            if (produced.IsSuccess) return false;

            Console.WriteLine($"RESULT               : REFUSED — {produced.Failure.Code}");
            Console.WriteLine($"detail               : {produced.Failure.TechnicalDetail}");
            foreach (KeyValuePair<string, string> entry in produced.Failure.Context)
            {
                Console.WriteLine($"  {entry.Key,-24}: {entry.Value}");
            }

            aggregate.Revisions.ShouldNotContain(r => r.Operation == OperationKind.PhotoshopOutput);
            aggregate.Outputs.ShouldBeEmpty();
            Console.WriteLine("no Revision was created; nothing is reviewable.");
            return true;
        }

        /// <summary>Prints the produced TIFF's facts and its full C1 validation note (§14).</summary>
        public void ReportTiff(string label, SessionAggregate aggregate, Revision tiff, string path)
        {
            Console.WriteLine($"{label,-20} : {tiff.File.RelativePath}");
            Console.WriteLine($"on disk              : {path}");
            Console.WriteLine($"SHA-256              : {tiff.Facts.Sha256}");
            Console.WriteLine($"bytes                : {tiff.Facts.ByteLength}");
            Console.WriteLine($"pixels               : {tiff.Facts.PixelWidth}x{tiff.Facts.PixelHeight}");
            Console.WriteLine($"C1 validation note   : " +
                              aggregate.Attempts.Last(a => a.Step == StepKind.PhotoshopOutput).AdapterNotes);
        }

        public void Dispose()
        {
            // The QA workspace, database and generated TIFFs are deliberately retained so the
            // final report can quote their hashes and an operator can open them in Maintop (§15).
            SqliteConnection.ClearAllPools();
        }
    }

    /// <summary>The controlled seam's gate, used by this smoke and nothing else (§27).</summary>
    private sealed class ControlledSeamEnvironmentGate : IWorkstationScopedEnvironmentGate
    {
        public OperationResult<PrintFlow.Domain.Results.Unit> Verify(AdapterExecutionMode mode) =>
            OperationResult.Ok();

        public OperationResult<PrintFlow.Domain.Results.Unit> Verify(
            AdapterExecutionMode mode,
            IWorkstationAutomationLease workstationLease) =>
            workstationLease.IsActive
                ? OperationResult.Ok()
                : OperationResult.Fail<PrintFlow.Domain.Results.Unit>(
                    FailureCode.AdapterUnavailable, "The controlled live seam has no active workstation lease.");
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
