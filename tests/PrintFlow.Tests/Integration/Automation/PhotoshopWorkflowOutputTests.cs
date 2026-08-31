using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;
using Shouldly;

namespace PrintFlow.Tests.Integration.Automation;

/// <summary>
/// The C2A workflow seam: what <c>GenerateAsync</c> composes, in what order, and what it refuses
/// to turn into workflow output (Epic 11400 Part C2A §4, §5, §8, §9, §17).
/// </summary>
/// <remarks>
/// Every stage below is driven through a scripted native bridge rather than a mock of PrintFlow's
/// own guards: the point of these tests is the <i>composition</i>, so the accepted preparation,
/// W1 and TIFF operations run their real code against scripted Photoshop answers. A test that
/// doubled the guards would prove only that the stub was called in order.
/// <para>
/// The ordering tests all assert the same shape and it is worth saying why once. Each records
/// what the later stages were asked to do and expects zero: "resize failed, so no Action ran and
/// no TIFF exists" is the property, and counting invocations is the only way to state it that a
/// future short-circuit cannot quietly satisfy.
/// </para>
/// </remarks>
public sealed class PhotoshopWorkflowOutputTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "PrintFlow-PhotoshopWorkflowOutput-" + Guid.NewGuid().ToString("N"));

    // -----------------------------------------------------------------------------------
    // §5 — the composed operation, in order
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// One call runs identity, preparation, W1 and the validated TIFF save exactly once each,
    /// and returns workflow output naming the reserved destination (§4, §5).
    /// </summary>
    [Fact]
    public async Task One_run_composes_identity_preparation_W1_and_validated_TIFF_exactly_once()
    {
        Harness h = CreateHarness();

        OperationResult<AdapterOutput> result = await h.Generate();

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);
        h.Preparation.ApplyCount.ShouldBe(1);
        h.W1.ExecuteCount.ShouldBe(1);
        h.Tiff.SaveCount.ShouldBe(1);
        h.Driver.OpenCount.ShouldBe(1);

        result.Value.ProducedFile.ShouldBe(h.Output);
        result.Value.ProducedFile.Area.ShouldBe(WorkspaceArea.Working);
        File.Exists(h.OutputPath).ShouldBeTrue();
    }

    /// <summary>
    /// The stages ran in the accepted order, not merely all of them (§5).
    /// </summary>
    /// <remarks>
    /// Asserted from a single recorded sequence rather than from three separate counters,
    /// because "each ran once" is also true of an order that saved the TIFF before running W1 —
    /// and that order would produce a file with no white underbase in it.
    /// </remarks>
    [Fact]
    public async Task The_accepted_stage_order_is_identity_then_size_then_W1_then_TIFF()
    {
        Harness h = CreateHarness();

        await h.Generate();

        h.Sequence.ShouldBe(["open", "prepare", "w1", "tiff"]);
    }

    // -----------------------------------------------------------------------------------
    // §5 — no later stage runs after an earlier one fails
    // -----------------------------------------------------------------------------------

    /// <summary>A resize that did not happen cannot be followed by an Action or a save (§5).</summary>
    [Fact]
    public async Task Resize_failure_prevents_W1_and_TIFF()
    {
        Harness h = CreateHarness();
        h.Preparation.Fail = "Photoshop reported no resize.";

        OperationResult<AdapterOutput> result = await h.Generate();

        result.IsFailure.ShouldBeTrue();
        result.Failure.Context["adapterOutputConstructed"].ShouldBe("false");
        result.Failure.Context["revisionCreated"].ShouldBe("false");
        h.Preparation.ApplyCount.ShouldBe(1);
        h.W1.ExecuteCount.ShouldBe(0);
        h.Tiff.SaveCount.ShouldBe(0);
        File.Exists(h.OutputPath).ShouldBeFalse();
    }

    /// <summary>An Action that did not complete cannot be followed by a save (§5).</summary>
    [Fact]
    public async Task W1_failure_prevents_TIFF()
    {
        Harness h = CreateHarness();
        h.W1.Fail = "The synchronous Action threw after input began.";

        OperationResult<AdapterOutput> result = await h.Generate();

        result.IsFailure.ShouldBeTrue();
        result.Failure.Context["revisionCreated"].ShouldBe("false");
        h.W1.ExecuteCount.ShouldBe(1);
        h.Tiff.SaveCount.ShouldBe(0);
        File.Exists(h.OutputPath).ShouldBeFalse();
    }

    /// <summary>A save that failed produces no workflow output (§17).</summary>
    [Fact]
    public async Task Save_failure_produces_no_adapter_output()
    {
        Harness h = CreateHarness();
        h.Tiff.Fail = "Photoshop did not complete the Save As Copy.";

        OperationResult<AdapterOutput> result = await h.Generate();

        result.IsFailure.ShouldBeTrue();
        result.Failure.Context["adapterOutputConstructed"].ShouldBe("false");
        h.Tiff.SaveCount.ShouldBe(1);
    }

    /// <summary>
    /// A TIFF that was written but does not independently validate produces no workflow output,
    /// and is left on disk rather than deleted (§18).
    /// </summary>
    /// <remarks>
    /// The non-RLE case C1 explored, run through the whole workflow seam. The file exists and
    /// Photoshop reported success, which is exactly the situation in which "the save returned, so
    /// it worked" would be a plausible-looking shortcut and a wrong answer.
    /// </remarks>
    [Fact]
    public async Task A_written_but_invalid_TIFF_produces_no_adapter_output_and_is_retained()
    {
        Harness h = CreateHarness();
        h.Tiff.FixtureOptions = new ProductionTiffFixtureOptions(LayerCompression: 0);

        OperationResult<AdapterOutput> result = await h.Generate();

        result.IsFailure.ShouldBeTrue();
        result.Failure.Context["adapterOutputConstructed"].ShouldBe("false");
        result.Failure.Context["revisionCreated"].ShouldBe("false");

        // Retained for recovery and audit. No automatic retry, and no hard deletion.
        File.Exists(h.OutputPath).ShouldBeTrue();
        h.Tiff.SaveCount.ShouldBe(1);
    }

    /// <summary>
    /// Cancellation requested before the save crossed the success boundary returns Cancelled and
    /// no workflow output, even though bytes exist on disk (§19).
    /// </summary>
    [Fact]
    public async Task Late_cancellation_during_the_save_returns_no_adapter_output()
    {
        Harness h = CreateHarness();
        using CancellationTokenSource cancellation = new();
        h.Tiff.BeforeReturn = () => cancellation.Cancel();

        OperationResult<AdapterOutput> result = await h.Generate(cancellation.Token);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.Cancelled);
        result.Failure.Context["revisionCreated"].ShouldBe("false");
        File.Exists(h.OutputPath).ShouldBeTrue();
    }

    // -----------------------------------------------------------------------------------
    // §9 — the output reference and the bytes must be the same file by every measure
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// A candidate naming a different file than the reserved destination is refused rather than
    /// silently accepted (§9).
    /// </summary>
    /// <remarks>
    /// Exercised at the construction seam directly, because the whole point is the case where the
    /// two disagree — which the composed run, by construction, cannot produce. The hazard this
    /// guards is a future edit that passes Photoshop's own idea of what it saved.
    /// </remarks>
    [Fact]
    public void A_candidate_naming_a_different_output_is_refused()
    {
        Harness h = CreateHarness();
        PhotoshopValidatedTiffCandidate candidate = h.CandidateFor(
            WorkspaceFileRef.Create("Sessions/S1/Working/A_1/OTHER_2mm_CMYK_W.tif", WorkspaceArea.Working));

        OperationResult<AdapterOutput> result = PhotoshopAdapterOutputFactory.Create(
            h.Request(), candidate, h.Workspace, TimeSpan.FromSeconds(1));

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.OutputValidationFailed);
        result.Failure.Context["adapterOutputConstructed"].ShouldBe("false");
    }

    /// <summary>
    /// A candidate whose recorded hash is not the hash of the bytes now on disk is refused (§9).
    /// </summary>
    [Fact]
    public async Task A_candidate_whose_hash_no_longer_matches_the_file_is_refused()
    {
        Harness h = CreateHarness();
        await h.Generate();

        PhotoshopValidatedTiffCandidate candidate = h.CandidateFor(h.Output) with
        {
            Sha256 = Sha256.Parse(new string('b', 64)),
        };

        OperationResult<AdapterOutput> result = PhotoshopAdapterOutputFactory.Create(
            h.Request(), candidate, h.Workspace, TimeSpan.FromSeconds(1));

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.OutputValidationFailed);
        result.Failure.Context["validatedSha256"].ShouldBe(new string('B', 64));
        result.Failure.Context["actualSha256"].ShouldNotBe(new string('B', 64));
    }

    /// <summary>A candidate whose file is gone is refused rather than reported as produced (§9).</summary>
    [Fact]
    public void A_candidate_whose_file_is_absent_is_refused()
    {
        Harness h = CreateHarness();

        OperationResult<AdapterOutput> result = PhotoshopAdapterOutputFactory.Create(
            h.Request(), h.CandidateFor(h.Output), h.Workspace, TimeSpan.FromSeconds(1));

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.OutputValidationFailed);
    }

    // -----------------------------------------------------------------------------------
    // §16 — the audit note
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The note keeps the bounded facts a later review turns on, and stays a summary (§16).
    /// </summary>
    /// <remarks>
    /// The upper bound is asserted as well as the content. The inspector reads layer records and
    /// per-sample data, and a note that grew to carry them would be a support burden in a column
    /// nobody can read — so the note is a fixed-shape sentence whose length does not scale with
    /// the size of the TIFF.
    /// </remarks>
    [Fact]
    public async Task The_adapter_note_carries_bounded_TIFF_facts_the_Revision_cannot_express()
    {
        Harness h = CreateHarness();

        OperationResult<AdapterOutput> result = await h.Generate();

        string notes = result.Value.AdapterNotes.ShouldNotBeNull();
        notes.ShouldContain("W1_1px");
        notes.ShouldContain("300x300 dpi");
        notes.ShouldContain("compression none");
        notes.ShouldContain("5x8-bit interleaved separated");
        notes.ShouldContain("little-endian");
        notes.ShouldContain("spot W1");
        notes.ShouldContain("all-RLE True");
        notes.ShouldContain("alpha False");
        notes.ShouldContain("pyramid False");
        notes.ShouldContain(PhotoshopAdapterOutputFactory.ValidationVersion);
        notes.Length.ShouldBeLessThan(1024);
    }

    // -----------------------------------------------------------------------------------
    // §13 — the Revision's facts come from the existing inspector, not a second parser
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The workflow's own file inspector can read a production-shaped TIFF, so a validated
    /// candidate can actually become a Revision (§13).
    /// </summary>
    /// <remarks>
    /// Worth its own test because it is the one link in the C2A chain that nothing else exercises
    /// against real production bytes: every workflow test drives the Fake adapter, which produces
    /// a PNG under a <c>.tif</c> name. A five-sample CMYK file with a spot channel is not
    /// something WIC has an obligation to decode, and if the inspector <i>failed</i> on one
    /// rather than reporting honest unknowns, the entire success path would break on the
    /// workstation and nowhere else.
    /// <para>
    /// So what is asserted is the contract the inspector actually promises: the facts a Revision
    /// is built from — format, length and hash — are always present, while pixel metadata is
    /// allowed to be null because WIC may have no codec for this variant. The TIFF-specific facts
    /// live on the attempt note instead, which is exactly why §13 says not to deform FileFacts to
    /// carry them.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_workflow_file_inspector_reads_a_production_shaped_TIFF_without_failing()
    {
        string directory = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        string path = ProductionTiffFixture.Write(directory);

        OperationResult<FileFacts> inspected =
            await new WicFileInspector().InspectAsync(path, CancellationToken.None);

        inspected.IsSuccess.ShouldBeTrue(
            inspected.IsFailure ? inspected.Failure.ToString() : string.Empty);
        inspected.Value.Format.ShouldBe(ImageFormat.Tiff);
        inspected.Value.ByteLength.ShouldBe(new FileInfo(path).Length);
        inspected.Value.Sha256.ShouldBe(Hash(path));
    }

    // -----------------------------------------------------------------------------------
    // Harness
    // -----------------------------------------------------------------------------------

    private Harness CreateHarness()
    {
        string executable = Path.Combine(_root, Guid.NewGuid().ToString("N"), "Photoshop.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllBytes(executable, [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00]);
        FileVersionInfo version = FileVersionInfo.GetVersionInfo(executable);

        string actionPath = Path.Combine(_root, Guid.NewGuid().ToString("N"), "PrintFlow-DTF-v1.atn");
        Directory.CreateDirectory(Path.GetDirectoryName(actionPath)!);
        File.WriteAllBytes(actionPath, [0x41, 0x54, 0x4E, 0x01]);

        string directory = Path.Combine(_root, Guid.NewGuid().ToString("N"), "Working", "A_1");
        Directory.CreateDirectory(directory);
        string documentPath = Path.Combine(directory, "PF_C2A_WORKING.png");
        File.WriteAllBytes(documentPath, [1, 2, 3, 4, 5, 6, 7, 8]);

        PhotoshopBaseline baseline = PhotoshopFakes.Baseline() with
        {
            ExecutablePath = executable,
            ExecutableSha256 = Hash(executable),
            AcceptedProductVersion = version.ProductVersion ?? "(unreadable)",
            AcceptedFileVersion = version.FileVersion ?? "(unreadable)",
            W1Action = new PhotoshopW1ActionContract(
                actionPath,
                Hash(actionPath),
                "PrintFlow DTF",
                [
                    new(WhiteUnderbaseBranch.W1_0px, "W1_0px", ["转换模式", "设置 选区", "建立"]),
                    new(WhiteUnderbaseBranch.W1_1px, "W1_1px", ["转换模式", "设置 选区", "收缩", "建立"]),
                    new(WhiteUnderbaseBranch.W1_2px, "W1_2px", ["转换模式", "设置 选区", "收缩", "建立"]),
                ]),
        };

        ExternalProcessRef process = new(
            7777, executable, new DateTimeOffset(2026, 8, 31, 9, 0, 0, TimeSpan.Zero));
        ExternalWindowRef window = PhotoshopFakes.Window(
            title: PhotoshopFakes.TitleFor(Path.GetFileName(documentPath)));
        FakeWindowLocator locator = new();
        locator.Register(process, window);
        locator.PutInForeground(window);

        List<string> sequence = [];
        StubComposedDriver driver = new(documentPath, window.Title, sequence);
        ScriptedPreparationBridge preparation = new(documentPath, sequence);
        ScriptedW1Bridge w1 = new(sequence);
        ScriptedTiffBridge tiff = new(documentPath, sequence);
        StubPhotoshopWorkspace workspace = new(directory);

        ProductionPhotoshopOutputProcessor adapter = new(
            new StubPhotoshopBaselineProvider(baseline),
            locator,
            driver,
            workspace,
            new PhotoshopAutomationOptions
            {
                PollInterval = TimeSpan.Zero,
                TiffSettleTimeout = TimeSpan.FromSeconds(5),
            },
            TimeProvider.System,
            preparation,
            w1,
            tiff,
            new ProductionTiffInspector(),
            new FileSystemPhotoshopTiffFileProbe());

        WorkspaceFileRef input = WorkspaceFileRef.Create(
            $"Sessions/S1/Working/A_1/{Path.GetFileName(documentPath)}", WorkspaceArea.Working);
        WorkspaceFileRef output = WorkspaceFileRef.Create(
            "Sessions/S1/Working/A_1/PRINT_2mm_CMYK_W.tif", WorkspaceArea.Working);

        return new Harness(
            adapter, driver, preparation, w1, tiff, workspace, sequence,
            input, output, documentPath, Path.Combine(directory, output.FileName),
            Hash(documentPath));
    }

    private static Sha256 Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Sha256.FromBytes(SHA256.HashData(stream));
    }

    private sealed record Harness(
        ProductionPhotoshopOutputProcessor Adapter,
        StubComposedDriver Driver,
        ScriptedPreparationBridge Preparation,
        ScriptedW1Bridge W1,
        ScriptedTiffBridge Tiff,
        IWorkspace Workspace,
        List<string> Sequence,
        WorkspaceFileRef Input,
        WorkspaceFileRef Output,
        string DocumentPath,
        string OutputPath,
        Sha256 SourceSha256)
    {
        /// <summary>
        /// A fully-formed request built the way <c>SessionService</c> builds one: a real plan
        /// from the one domain authority, bound to the exact bytes on disk, and the exact
        /// reserved Working destination.
        /// </summary>
        /// <remarks>
        /// Bound to the backing file's own hash rather than a placeholder, because the accepted
        /// preparation refuses a plan whose source hash is not the file it is about to change —
        /// and a composition test that could not get past that guard would be testing the guard
        /// rather than the composition. The source is already within the bounds, so the plan is
        /// a resolution-only one and the projected pixels are the source's own.
        /// </remarks>
        internal PhotoshopRequest Request() => new(
            Input,
            PrintDimensions.FromMillimetres(200, 100, SizePreset.Custom),
            new FitWithinBoundsPreparation(PrintPreparationPlan.For(
                RevisionId.From(Guid.Parse("11111111-1111-1111-1111-111111111111")),
                SourceSha256,
                sourcePixelWidth: 2,
                sourcePixelHeight: 2,
                PrintDimensions.FromMillimetres(200, 100, SizePreset.Custom))),
            new ProductionPresetRef("printflow-workstation-v1", "1.14.0", Sha256.Parse(new string('0', 64))),
            WhiteUnderbaseBranch.W1_1px,
            Output.FileName,
            WorkspaceDirRef.Create("Sessions/S1/Working/A_1"),
            Output);

        internal Task<OperationResult<AdapterOutput>> Generate(CancellationToken cancellationToken = default) =>
            Adapter.GenerateAsync(Request(), cancellationToken);

        /// <summary>
        /// A structurally complete candidate naming <paramref name="tiff"/>, whose byte length is
        /// the file's real one when the file exists.
        /// </summary>
        /// <remarks>
        /// The length is taken from disk so that a test about the <i>hash</i> check actually
        /// reaches the hash check: a placeholder length would be refused one step earlier, and
        /// the test would pass while proving nothing about hashes.
        /// </remarks>
        internal PhotoshopValidatedTiffCandidate CandidateFor(WorkspaceFileRef tiff) => new(
            tiff,
            Sha256.Parse(new string('c', 64)),
            ByteLength: File.Exists(OutputPath) ? new FileInfo(OutputPath).Length : 4096,
            new ProductionTiffFacts(
                "little-endian", 2, 2, 300, 300, 2, [8, 8, 8, 8, 8], 5, 5, 1, 1, [0], ["W1"],
                W1IsPhotoshopSpotChannel: true, W1NonWhiteSampleCount: 1,
                HasAlphaOrTransparencySample: false, HasImagePyramid: false,
                HasPhotoshopImageSourceData: true, PhotoshopLayerCount: 1,
                AllPhotoshopLayerChannelsUseRle: true, ImageFileDirectoryCount: 1,
                ValidationLimitations: []),
            PhotoshopProductionTiffSaveSettings.Accepted,
            Sha256.Parse(new string('d', 64)),
            DocumentPath,
            DocumentPath,
            TimeSpan.FromSeconds(2),
            [],
            []);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    // -----------------------------------------------------------------------------------
    // Scripted Photoshop
    // -----------------------------------------------------------------------------------

    private sealed class ScriptedPreparationBridge(string documentPath, List<string> sequence)
        : IPhotoshopPreparationNativeBridge
    {
        public int ApplyCount { get; private set; }

        public string? Fail { get; set; }

        public OperationResult<PhotoshopNativePreparationOutcome> ApplyOnce(
            PhotoshopNativePreparationCommand command, PhotoshopBaseline baseline)
        {
            _ = baseline;
            _ = command;
            ApplyCount++;
            sequence.Add("prepare");
            if (Fail is not null)
            {
                return OperationResult.Ok(new PhotoshopNativePreparationOutcome(
                    Succeeded: false, MutationInvoked: true, FailureDetail: Fail,
                    Before: Rgb(documentPath, 2, 2, 72), After: null));
            }

            // A resolution-only preparation: the source is already inside the bounds, so the
            // pixels are unchanged and only the stated resolution moves to the production 300.
            return OperationResult.Ok(new PhotoshopNativePreparationOutcome(
                Succeeded: true,
                MutationInvoked: true,
                FailureDetail: null,
                Before: Rgb(documentPath, 2, 2, 72),
                After: Rgb(documentPath, 2, 2, 300)));
        }

        private static PhotoshopDocumentFacts Rgb(string path, int width, int height, double dpi) => new(
            path, width, height, dpi, width / dpi * 25.4, height / dpi * 25.4,
            "DocumentMode.RGB", "BitsPerChannelType.EIGHT",
            [
                new("Red", "ChannelType.COMPONENT"),
                new("Green", "ChannelType.COMPONENT"),
                new("Blue", "ChannelType.COMPONENT"),
            ],
            W1Exists: false,
            DocumentCount: 1);
    }

    private sealed class ScriptedW1Bridge(List<string> sequence) : IPhotoshopW1NativeBridge
    {
        public int ExecuteCount { get; private set; }

        public string? Fail { get; set; }

        public OperationResult<PhotoshopNativeW1Outcome> ExecuteOnce(
            PhotoshopNativeW1Command command, PhotoshopBaseline baseline)
        {
            _ = baseline;
            ExecuteCount++;
            sequence.Add("w1");

            PhotoshopDocumentFacts before = Facts(command.ExpectedDocumentFullPath, cmyk: false);
            if (Fail is not null)
            {
                return OperationResult.Ok(new PhotoshopNativeW1Outcome(
                    Succeeded: false, ActionInvocationCount: 1, FailureDetail: Fail,
                    Before: before, After: null, W1: null));
            }

            return OperationResult.Ok(new PhotoshopNativeW1Outcome(
                Succeeded: true,
                ActionInvocationCount: 1,
                FailureDetail: null,
                Before: before,
                After: Facts(command.ExpectedDocumentFullPath, cmyk: true),
                W1: new PhotoshopW1ChannelFacts(
                    "W1", "ChannelType.SPOTCOLOR", true, 1, 100, [255, 0, 0])));
        }

        private static PhotoshopDocumentFacts Facts(string path, bool cmyk) => new(
            path, 2, 2, 300, 2 / 300d * 25.4, 2 / 300d * 25.4,
            cmyk ? "DocumentMode.CMYK" : "DocumentMode.RGB",
            "BitsPerChannelType.EIGHT",
            cmyk
                ?
                [
                    new("Cyan", "ChannelType.COMPONENT"),
                    new("Magenta", "ChannelType.COMPONENT"),
                    new("Yellow", "ChannelType.COMPONENT"),
                    new("Black", "ChannelType.COMPONENT"),
                    new("W1", "ChannelType.SPOTCOLOR"),
                ]
                :
                [
                    new("Red", "ChannelType.COMPONENT"),
                    new("Green", "ChannelType.COMPONENT"),
                    new("Blue", "ChannelType.COMPONENT"),
                ],
            W1Exists: cmyk,
            DocumentCount: 1);
    }

    private sealed class ScriptedTiffBridge(string documentPath, List<string> sequence)
        : IPhotoshopTiffNativeBridge
    {
        public int SaveCount { get; private set; }

        public string? Fail { get; set; }

        public ProductionTiffFixtureOptions FixtureOptions { get; set; } = new();

        public Action? BeforeReturn { get; set; }

        public OperationResult<PhotoshopNativeTiffOutcome> SaveOnce(
            PhotoshopNativeTiffCommand command, PhotoshopBaseline baseline)
        {
            _ = baseline;
            SaveCount++;
            sequence.Add("tiff");

            if (Fail is not null)
            {
                return OperationResult.Ok(new PhotoshopNativeTiffOutcome(
                    Succeeded: false, SaveInvocationCount: 1, FailureDetail: Fail,
                    Before: null, After: null, W1Before: null,
                    OutputExists: false, AcceptedSettingsObserved: false));
            }

            ProductionTiffFixture.WriteAt(command.ExpectedOutputFullPath, FixtureOptions);
            BeforeReturn?.Invoke();

            return OperationResult.Ok(new PhotoshopNativeTiffOutcome(
                Succeeded: true,
                SaveInvocationCount: 1,
                FailureDetail: null,
                Before: Cmyk(documentPath),
                After: Cmyk(documentPath),
                W1Before: new PhotoshopW1ChannelFacts(
                    "W1", "ChannelType.SPOTCOLOR", true, 1, 100, [255, 0, 0]),
                OutputExists: true,
                AcceptedSettingsObserved: true));
        }

        private static PhotoshopDocumentFacts Cmyk(string path) => new(
            path, 2, 2, 300, 2 / 300d * 25.4, 2 / 300d * 25.4,
            "DocumentMode.CMYK", "BitsPerChannelType.EIGHT",
            [
                new("Cyan", "ChannelType.COMPONENT"),
                new("Magenta", "ChannelType.COMPONENT"),
                new("Yellow", "ChannelType.COMPONENT"),
                new("Black", "ChannelType.COMPONENT"),
                new("W1", "ChannelType.SPOTCOLOR"),
            ],
            W1Exists: true,
            DocumentCount: 1);
    }

    /// <summary>A Photoshop that is ready, opens the file it is given, and admits which one.</summary>
    private sealed class StubComposedDriver(string path, string title, List<string> sequence)
        : IPhotoshopUiDriver
    {
        public int OpenCount { get; private set; }

        public Task<OperationResult<PhotoshopStateSnapshot>> InspectStateAsync(
            PhotoshopTarget target, string? expectedDocumentFileName, CancellationToken cancellationToken)
        {
            _ = target;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(OperationResult.Ok(new PhotoshopStateSnapshot(
                PhotoshopStartingState.KnownEditorWithOtherDocument,
                [],
                new PhotoshopObservation(
                    title, [], [], true, expectedDocumentFileName, path))));
        }

        public Task<OperationResult<PhotoshopTarget>> ActivateAsync(
            PhotoshopTarget target, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Ok(target));

        public Task<OperationResult<PhotoshopTarget>> OpenManagedDocumentAsync(
            PhotoshopTarget target, string managedAbsolutePath, CancellationToken cancellationToken)
        {
            _ = managedAbsolutePath;
            cancellationToken.ThrowIfCancellationRequested();
            OpenCount++;
            sequence.Add("open");
            return Task.FromResult(OperationResult.Ok(target));
        }

        public Task<OperationResult<PhotoshopDocumentIdentity>> ProbeDocumentIdentityAsync(
            PhotoshopTarget target, CancellationToken cancellationToken)
        {
            _ = target;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(OperationResult.Ok(new PhotoshopDocumentIdentity(
                Path.GetFileName(path), Path.GetDirectoryName(path)!, path, title)));
        }

        public Task<OperationResult<PhotoshopTarget>> CloseExactDocumentAsync(
            PhotoshopTarget target, string expectedAbsolutePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The composed run closes nothing.");

        public OperationResult<EvidenceRef> CaptureEvidence(PhotoshopTarget target, string reason) =>
            OperationResult.Fail<EvidenceRef>(FailureCode.WorkspaceError, "not used");
    }
}
