using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Tests.Fixtures;
using Shouldly;

namespace PrintFlow.Tests.Integration.Automation;

public sealed class PhotoshopTiffSaveTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "PrintFlow-PhotoshopTiffSave-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(WhiteUnderbaseBranch.W1_0px)]
    [InlineData(WhiteUnderbaseBranch.W1_1px)]
    [InlineData(WhiteUnderbaseBranch.W1_2px)]
    public async Task Every_factual_B1B_branch_uses_the_same_one_save_contract(
        WhiteUnderbaseBranch branch)
    {
        Harness h = CreateHarness(branch);

        OperationResult<PhotoshopValidatedTiffCandidate> result = await h.Saver
            .SaveProductionTiffAsync(h.Opened, h.Prepared, h.Output, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);
        h.Native.SaveCount.ShouldBe(1);
        result.Value.Tiff.ShouldBe(h.Output);
        result.Value.Facts.PixelWidth.ShouldBe(2);
        result.Value.Facts.PixelHeight.ShouldBe(2);
        result.Value.Facts.ExtraChannelNames.ShouldBe(["W1"]);
        result.Value.Facts.W1IsPhotoshopSpotChannel.ShouldBeTrue();
        result.Value.Facts.W1NonWhiteSampleCount.ShouldBe(1);
        result.Value.SaveSettings.ShouldBe(PhotoshopProductionTiffSaveSettings.Accepted);
        result.Value.BackingWorkingSha256.ShouldBe(h.Prepared.BackingWorkingSha256);
        result.Value.DocumentFullPathAfter.ShouldBe(h.DocumentPath);
        result.Value.SettlingObservations.Length.ShouldBeGreaterThanOrEqualTo(3);
        Directory.EnumerateFiles(h.Directory).Order().ShouldBe(
            new[] { h.DocumentPath, h.OutputPath }.Order());
    }

    [Fact]
    public async Task Existing_destination_refuses_before_native_save_and_is_not_overwritten()
    {
        Harness h = CreateHarness();
        File.WriteAllBytes(h.OutputPath, [9, 8, 7]);
        byte[] before = File.ReadAllBytes(h.OutputPath);

        OperationResult<PhotoshopValidatedTiffCandidate> result = await h.Saver
            .SaveProductionTiffAsync(h.Opened, h.Prepared, h.Output, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        h.Native.SaveCount.ShouldBe(0);
        File.ReadAllBytes(h.OutputPath).ShouldBe(before);
    }

    [Theory]
    [InlineData(WorkspaceArea.Source)]
    [InlineData(WorkspaceArea.Approved)]
    [InlineData(WorkspaceArea.Rejected)]
    [InlineData(WorkspaceArea.Logs)]
    public async Task Only_the_exact_Working_reference_may_be_a_destination(WorkspaceArea area)
    {
        Harness h = CreateHarness();
        WorkspaceFileRef output = WorkspaceFileRef.Create(
            $"Sessions/S1/{area}/PRINT_2mm_CMYK_W.tif", area);

        OperationResult<PhotoshopValidatedTiffCandidate> result = await h.Saver
            .SaveProductionTiffAsync(h.Opened, h.Prepared, output, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        h.Native.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task Wrong_document_identity_refuses_before_save()
    {
        Harness h = CreateHarness();
        PhotoshopOpenedDocument wrong = h.Opened with
        {
            Identity = h.Opened.Identity with { ObservedFullPath = Path.Combine(h.Directory, "other.png") },
        };

        OperationResult<PhotoshopValidatedTiffCandidate> result = await h.Saver
            .SaveProductionTiffAsync(wrong, h.Prepared, h.Output, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        h.Native.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task Wrong_CMYK_or_W1_state_refuses_before_save()
    {
        Harness h = CreateHarness();
        PhotoshopW1PreparedDocument wrong = h.Prepared with
        {
            W1 = h.Prepared.W1 with { IsNonEmpty = false, NonWhitePixelCount = 0 },
        };

        OperationResult<PhotoshopValidatedTiffCandidate> result = await h.Saver
            .SaveProductionTiffAsync(h.Opened, wrong, h.Output, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        h.Native.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task Cancellation_before_save_produces_zero_native_input()
    {
        Harness h = CreateHarness();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        OperationResult<PhotoshopValidatedTiffCandidate> result = await h.Saver
            .SaveProductionTiffAsync(h.Opened, h.Prepared, h.Output, cancellation.Token);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.Cancelled);
        h.Native.SaveCount.ShouldBe(0);
        File.Exists(h.OutputPath).ShouldBeFalse();
    }

    [Fact]
    public async Task A_partial_or_ambiguous_native_save_never_becomes_a_candidate()
    {
        Harness h = CreateHarness();
        h.Native.ResultFactory = command =>
        {
            File.WriteAllBytes(command.ExpectedOutputFullPath, [0x49, 0x49, 42, 0]);
            return OperationResult.Fail<PhotoshopNativeTiffOutcome>(OperationFailure.Create(
                FailureCode.PhotoshopUnknownState, "scripted ambiguous save"));
        };

        OperationResult<PhotoshopValidatedTiffCandidate> result = await h.Saver
            .SaveProductionTiffAsync(h.Opened, h.Prepared, h.Output, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        h.Native.SaveCount.ShouldBe(1);
        File.Exists(h.OutputPath).ShouldBeTrue("partial Working bytes are retained, not deleted");
        result.Failure.Context["validatedCandidateCreated"].ShouldBe("false");
    }

    [Fact]
    public async Task Structurally_wrong_TIFF_is_rejected_after_the_one_save()
    {
        Harness h = CreateHarness();
        h.Native.FixtureOptions = new ProductionTiffFixtureOptions(Compression: 5);

        OperationResult<PhotoshopValidatedTiffCandidate> result = await h.Saver
            .SaveProductionTiffAsync(h.Opened, h.Prepared, h.Output, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.OutputValidationFailed);
        h.Native.SaveCount.ShouldBe(1);
        File.Exists(h.OutputPath).ShouldBeTrue();
    }

    [Fact]
    public async Task Saving_may_not_mutate_the_backing_Working_bytes()
    {
        Harness h = CreateHarness();
        h.Native.AfterWrite = _ => File.AppendAllBytes(h.DocumentPath, [99]);

        OperationResult<PhotoshopValidatedTiffCandidate> result = await h.Saver
            .SaveProductionTiffAsync(h.Opened, h.Prepared, h.Output, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.TechnicalDetail.ShouldContain("backing bytes", Case.Insensitive);
        h.Native.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task Exactly_one_expected_TIFF_may_appear_in_the_attempt_directory()
    {
        Harness h = CreateHarness();
        h.Native.AfterWrite = _ => File.WriteAllBytes(Path.Combine(h.Directory, "sidecar.psd"), [1]);

        OperationResult<PhotoshopValidatedTiffCandidate> result = await h.Saver
            .SaveProductionTiffAsync(h.Opened, h.Prepared, h.Output, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.TechnicalDetail.ShouldContain("file set", Case.Insensitive);
        h.Native.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task Save_as_copy_must_leave_the_original_document_identity_active()
    {
        Harness h = CreateHarness();
        h.Native.AfterPath = h.OutputPath;

        OperationResult<PhotoshopValidatedTiffCandidate> result = await h.Saver
            .SaveProductionTiffAsync(h.Opened, h.Prepared, h.Output, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.TechnicalDetail.ShouldContain("identity", Case.Insensitive);
        h.Native.SaveCount.ShouldBe(1);
    }

    [Fact]
    public void TIFF_settle_requires_three_equal_nonzero_completely_readable_observations()
    {
        PhotoshopTiffSettleRule.IsSettled(
            [Seen(100), Seen(100), Seen(100)]).ShouldBeTrue();
        PhotoshopTiffSettleRule.IsSettled(
            [Seen(100), Seen(101), Seen(101)]).ShouldBeFalse();
        PhotoshopTiffSettleRule.IsSettled(
            [Seen(100), Seen(100), Seen(100, readable: false)]).ShouldBeFalse();
        PhotoshopTiffSettleRule.IsSettled(
            [Seen(0), Seen(0), Seen(0)]).ShouldBeFalse();
    }

    [Fact]
    public void Native_program_exposes_no_caller_selected_TIFF_options()
    {
        PhotoshopNativeTiffCommand command = new(@"C:\Working\source.png", @"C:\Working\out.tif", 2, 2, 300);
        string script = PhotoshopProductionTiffProgram.Create(command);

        script.ShouldContain("options.imageCompression = TIFFEncoding.NONE;");
        script.ShouldContain("options.interleaveChannels = true;");
        script.ShouldContain("options.byteOrder = ByteOrder.IBM;");
        script.ShouldContain("options.layerCompression = LayerCompression.RLE;");
        script.ShouldContain("options.layers = true;");
        script.ShouldContain("options.saveImagePyramid = false;");
        script.ShouldContain("options.transparency = false;");
        script.ShouldContain("options.alphaChannels = false;");
        script.ShouldContain("options.spotColors = true;");
        script.ShouldContain("doc.saveAs(output, options, true, Extension.LOWERCASE);");
    }

    private Harness CreateHarness(WhiteUnderbaseBranch branch = WhiteUnderbaseBranch.W1_1px)
    {
        string executable = Path.Combine(_root, Guid.NewGuid().ToString("N"), "Photoshop.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllBytes(executable, [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00]);
        FileVersionInfo version = FileVersionInfo.GetVersionInfo(executable);

        string directory = Path.Combine(_root, Guid.NewGuid().ToString("N"), "Working", "A_1");
        Directory.CreateDirectory(directory);
        string documentPath = Path.Combine(directory, "PF_C1_WORKING.png");
        File.WriteAllBytes(documentPath, [1, 2, 3, 4, 5, 6, 7, 8]);
        Sha256 backing = Hash(documentPath);

        PhotoshopBaseline baseline = PhotoshopFakes.Baseline() with
        {
            ExecutablePath = executable,
            ExecutableSha256 = Hash(executable),
            AcceptedProductVersion = version.ProductVersion ?? "(unreadable)",
            AcceptedFileVersion = version.FileVersion ?? "(unreadable)",
        };
        StubPhotoshopBaselineProvider baselines = new(baseline);
        ExternalProcessRef process = new(7777, executable,
            new DateTimeOffset(2026, 8, 31, 9, 0, 0, TimeSpan.Zero));
        ExternalWindowRef window = PhotoshopFakes.Window(
            title: PhotoshopFakes.TitleFor(Path.GetFileName(documentPath)));
        PhotoshopTarget target = new(process, window);
        FakeWindowLocator locator = new();
        locator.Register(process, window);
        StubTiffDriver driver = new(documentPath, window.Title);
        GuardedPhotoshopDocumentPreparer targetGuard = new(
            baselines, locator, driver, new UnusedPreparationBridge());
        RecordingTiffBridge native = new(documentPath);
        StubPhotoshopWorkspace workspace = new(directory);
        PhotoshopAutomationOptions options = new()
        {
            PollInterval = TimeSpan.Zero,
            TiffSettleTimeout = TimeSpan.FromSeconds(5),
        };
        GuardedPhotoshopTiffSaver saver = new(
            baselines, targetGuard, native, new ProductionTiffInspector(),
            new FileSystemPhotoshopTiffFileProbe(), workspace, options, TimeProvider.System);

        PhotoshopDocumentIdentity identity = new(
            Path.GetFileName(documentPath), directory, documentPath, window.Title);
        PhotoshopOpenedDocument opened = new(
            target,
            new PhotoshopStateSnapshot(
                PhotoshopStartingState.KnownEditorWithExpectedDocument,
                [],
                new PhotoshopObservation(window.Title, [], [], true,
                    Path.GetFileName(documentPath), documentPath)),
            identity,
            OtherDocumentsMayBeOpen: true);
        PhotoshopW1PreparedDocument prepared = new(
            documentPath,
            PixelWidth: 2,
            PixelHeight: 2,
            ResolutionPpi: 300,
            PhysicalWidthMm: 2 / 300d * 25.4,
            PhysicalHeightMm: 2 / 300d * 25.4,
            ColourMode: "DocumentMode.CMYK",
            BitDepth: "BitsPerChannelType.EIGHT",
            ProcessChannels:
            [
                new("Cyan", "ChannelType.COMPONENT"),
                new("Magenta", "ChannelType.COMPONENT"),
                new("Yellow", "ChannelType.COMPONENT"),
                new("Black", "ChannelType.COMPONENT"),
            ],
            W1: new("W1", "ChannelType.SPOTCOLOR", true, 1, 100, [255, 0, 0]),
            Branch: branch,
            ActionSetName: "PrintFlow DTF",
            ActionName: branch.ToString(),
            OtherDocumentsMayBeOpen: true,
            BackingWorkingSha256: backing,
            ActionInvocationOccurredExactlyOnce: true);
        WorkspaceFileRef output = WorkspaceFileRef.Create(
            "Sessions/S1/Working/A_1/PRINT_2mm_CMYK_W.tif", WorkspaceArea.Working);
        return new Harness(saver, native, opened, prepared, output, documentPath,
            Path.Combine(directory, output.FileName), directory);
    }

    private static PhotoshopTiffSettleObservation Seen(long length, bool readable = true) =>
        new(TimeSpan.Zero, true, length, readable ? length : 0, readable);

    private static Sha256 Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Sha256.FromBytes(SHA256.HashData(stream));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private sealed record Harness(
        GuardedPhotoshopTiffSaver Saver,
        RecordingTiffBridge Native,
        PhotoshopOpenedDocument Opened,
        PhotoshopW1PreparedDocument Prepared,
        WorkspaceFileRef Output,
        string DocumentPath,
        string OutputPath,
        string Directory);

    private sealed class RecordingTiffBridge(string documentPath) : IPhotoshopTiffNativeBridge
    {
        public int SaveCount { get; private set; }
        public ProductionTiffFixtureOptions FixtureOptions { get; set; } = new();
        public Action<PhotoshopNativeTiffCommand>? AfterWrite { get; set; }
        public string? AfterPath { get; set; }
        public Func<PhotoshopNativeTiffCommand, OperationResult<PhotoshopNativeTiffOutcome>>? ResultFactory
        { get; set; }

        public OperationResult<PhotoshopNativeTiffOutcome> SaveOnce(
            PhotoshopNativeTiffCommand command, PhotoshopBaseline baseline)
        {
            _ = baseline;
            SaveCount++;
            if (ResultFactory is not null) return ResultFactory(command);
            ProductionTiffFixture.WriteAt(command.ExpectedOutputFullPath, FixtureOptions);
            AfterWrite?.Invoke(command);
            PhotoshopDocumentFacts before = Cmyk(documentPath);
            PhotoshopDocumentFacts after = Cmyk(AfterPath ?? documentPath);
            return OperationResult.Ok(new PhotoshopNativeTiffOutcome(
                true, 1, null, before, after,
                new PhotoshopW1ChannelFacts(
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
            DocumentCount: 3);
    }

    private sealed class UnusedPreparationBridge : IPhotoshopPreparationNativeBridge
    {
        public OperationResult<PhotoshopNativePreparationOutcome> ApplyOnce(
            PhotoshopNativePreparationCommand command, PhotoshopBaseline baseline) =>
            throw new NotSupportedException();
    }

    private sealed class StubTiffDriver(string path, string title) : IPhotoshopUiDriver
    {
        public Task<OperationResult<PhotoshopStateSnapshot>> InspectStateAsync(
            PhotoshopTarget target, string? expectedDocumentFileName, CancellationToken cancellationToken)
        {
            _ = target;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(OperationResult.Ok(new PhotoshopStateSnapshot(
                PhotoshopStartingState.KnownEditorWithOtherDocument,
                [],
                new PhotoshopObservation(title, [], [], true, expectedDocumentFileName, path))));
        }

        public Task<OperationResult<PhotoshopTarget>> ActivateAsync(
            PhotoshopTarget target, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Ok(target));

        public Task<OperationResult<PhotoshopTarget>> OpenManagedDocumentAsync(
            PhotoshopTarget target, string managedAbsolutePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationResult<PhotoshopDocumentIdentity>> ProbeDocumentIdentityAsync(
            PhotoshopTarget target, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationResult<PhotoshopTarget>> CloseExactDocumentAsync(
            PhotoshopTarget target, string expectedAbsolutePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public OperationResult<EvidenceRef> CaptureEvidence(PhotoshopTarget target, string reason) =>
            OperationResult.Fail<EvidenceRef>(FailureCode.WorkspaceError, "not used");
    }
}
