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

namespace PrintFlow.Tests.Integration.Automation;

/// <summary>The focused B1B guarded CMYK + W1 operation against faked native/OS seams.</summary>
public sealed class PhotoshopW1ExecutionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "PrintFlowPhotoshopW1Tests", Guid.NewGuid().ToString("N"));

    public PhotoshopW1ExecutionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Theory]
    [InlineData(WhiteUnderbaseBranch.W1_0px, "W1_0px")]
    [InlineData(WhiteUnderbaseBranch.W1_1px, "W1_1px")]
    [InlineData(WhiteUnderbaseBranch.W1_2px, "W1_2px")]
    public async Task Typed_branch_maps_to_one_exact_action_and_invokes_it_once(
        WhiteUnderbaseBranch branch, string expectedAction)
    {
        Harness h = CreateHarness();
        h.Native.OutcomeFactory = command => Success(command, h.Prepared.Actual, h.DocumentCount);

        OperationResult<PhotoshopW1PreparedDocument> result = await h.Adapter.ExecuteW1Async(
            h.Opened, h.Prepared, branch, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);
        result.Value.Branch.ShouldBe(branch);
        result.Value.ActionName.ShouldBe(expectedAction);
        result.Value.ActionSetName.ShouldBe("PrintFlow DTF");
        result.Value.ActionInvocationOccurredExactlyOnce.ShouldBeTrue();
        h.Native.ExecuteCount.ShouldBe(1);
        h.Native.Commands.Single().Branch.ShouldBe(branch);
    }

    [Fact]
    public void Fixed_program_promotes_a_Photoshop_Background_layer_immediately_before_the_Action()
    {
        Harness h = CreateHarness();
        PhotoshopW1ActionContract contract = Contract(h.ArtifactPath, Hash(h.ArtifactPath));
        PhotoshopNativeW1Command command = new(
            h.DocumentPath,
            h.Prepared.Actual.PixelWidth,
            h.Prepared.Actual.PixelHeight,
            h.Prepared.Actual.ResolutionPpi,
            WhiteUnderbaseBranch.W1_1px,
            contract);

        string program = PhotoshopW1Program.Create(command);

        int runtimeGuard = program.IndexOf("var runtimeFailure = verifyRuntime();", StringComparison.Ordinal);
        int promotion = program.IndexOf("promoteBackgroundLayer(doc);", StringComparison.Ordinal);
        int action = program.IndexOf("app.doAction", StringComparison.Ordinal);

        runtimeGuard.ShouldBeGreaterThanOrEqualTo(0);
        promotion.ShouldBeGreaterThan(runtimeGuard,
            "the accepted Action and its transcript must be verified before any layer mutation");
        promotion.ShouldBeLessThan(action,
            "a flattened PNG Background must be editable before the Action's selection commands run");
        (program.Split("background.isBackgroundLayer = false;", StringSplitOptions.None).Length - 1)
            .ShouldBe(1);
        (program.Split("app.doAction", StringSplitOptions.None).Length - 1).ShouldBe(1);
    }

    [Fact]
    public async Task Missing_canonical_atn_refuses_before_native_input()
    {
        Harness h = CreateHarness();
        File.Delete(h.ArtifactPath);

        OperationResult<PhotoshopW1PreparedDocument> result = await Execute(h);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
        result.Failure.Context["actionInvocationCount"].ShouldBe("0");
        h.Native.ExecuteCount.ShouldBe(0);
    }

    /// <summary>
    /// An Action file replaced <i>after</i> a successful run is refused on the next one, in the
    /// same process (Epic 11500 Part C §7).
    /// </summary>
    /// <remarks>
    /// The third operation-time proof behind Part C's decision to keep the restart-bound
    /// environment gate. The canonical artefact is hashed immediately before every invocation, so
    /// a replaced <c>.atn</c> stops the run that would have used it — the gate's process-lifetime
    /// cache is not what is standing between a changed Action and a production output.
    /// </remarks>
    [Fact]
    public async Task An_atn_replaced_after_a_successful_run_refuses_the_next_run()
    {
        Harness h = CreateHarness();
        byte[] acceptedBytes = File.ReadAllBytes(h.ArtifactPath);
        h.Native.OutcomeFactory = command => Success(command, h.Prepared.Actual, h.DocumentCount);

        (await Execute(h)).IsSuccess.ShouldBeTrue();
        h.Native.ExecuteCount.ShouldBe(1);

        File.WriteAllBytes(h.ArtifactPath, [.. File.ReadAllBytes(h.ArtifactPath), 0xFF]);

        OperationResult<PhotoshopW1PreparedDocument> after = await Execute(h);

        after.IsFailure.ShouldBeTrue();
        after.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
        after.Failure.Context["actionInvocationCount"].ShouldBe("0");
        h.Native.ExecuteCount.ShouldBe(1, "the replaced Action was never invoked.");

        File.WriteAllBytes(h.ArtifactPath, acceptedBytes);
        OperationResult<PhotoshopW1PreparedDocument> recovered = await Execute(h);

        recovered.IsSuccess.ShouldBeTrue(recovered.IsFailure ? recovered.Failure.ToString() : string.Empty);
        h.Native.ExecuteCount.ShouldBe(2, "the accepted Action runs once after its bytes are restored.");
    }

    [Fact]
    public async Task Wrong_canonical_atn_hash_refuses_without_repair_or_reload()
    {
        Harness h = CreateHarness();
        File.WriteAllBytes(h.ArtifactPath, [.. File.ReadAllBytes(h.ArtifactPath), 0xFF]);

        OperationResult<PhotoshopW1PreparedDocument> result = await Execute(h);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
        result.Failure.Context["actualSha256"].ShouldNotBe(result.Failure.Context["expectedSha256"]);
        h.Native.ExecuteCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("The exact accepted Action set is missing or duplicated.")]
    [InlineData("An exact accepted Action is missing or duplicated.")]
    [InlineData("An accepted Action command transcript changed.")]
    public async Task Runtime_set_action_or_transcript_disagreement_refuses_with_zero_actions(string detail)
    {
        Harness h = CreateHarness();
        h.Native.OutcomeFactory = _ => new PhotoshopNativeW1Outcome(
            false, 0, detail, h.Prepared.Actual, null, null);

        OperationResult<PhotoshopW1PreparedDocument> result = await Execute(h);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        result.Failure.Context["actionInvocationCount"].ShouldBe("0");
        h.Native.ExecuteCount.ShouldBe(1);
    }

    [Fact]
    public async Task Switched_active_document_refuses_before_native_execution()
    {
        Harness h = CreateHarness();
        h.Driver.WindowTitle = "OPERATOR.png @ 100% (RGB/8)";

        OperationResult<PhotoshopW1PreparedDocument> result = await Execute(h);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PhotoshopUnknownState);
        h.Native.ExecuteCount.ShouldBe(0);
    }

    [Fact]
    public async Task Same_basename_different_path_refuses_before_native_execution()
    {
        Harness h = CreateHarness();
        h.Driver.Identity = h.Driver.Identity with
        {
            ObservedDirectory = Path.Combine(_root, "operator"),
            ObservedFullPath = Path.Combine(_root, "operator", Path.GetFileName(h.DocumentPath)),
        };

        OperationResult<PhotoshopW1PreparedDocument> result = await Execute(h);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PhotoshopDocumentIdentityUnconfirmed);
        h.Native.ExecuteCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("geometry")]
    [InlineData("resolution")]
    [InlineData("mode")]
    [InlineData("bits")]
    [InlineData("w1")]
    [InlineData("alpha")]
    public async Task Invalid_prepared_state_refuses_before_Action(string variant)
    {
        Harness h = CreateHarness();
        PhotoshopDocumentFacts facts = variant switch
        {
            "geometry" => h.Prepared.Actual with { PixelWidth = 0 },
            "resolution" => h.Prepared.Actual with { ResolutionPpi = 299 },
            "mode" => h.Prepared.Actual with { ColourMode = "DocumentMode.CMYK" },
            "bits" => h.Prepared.Actual with { BitDepth = "BitsPerChannelType.SIXTEEN" },
            "w1" => h.Prepared.Actual with { W1Exists = true },
            "alpha" => h.Prepared.Actual with
            {
                Channels = [.. h.Prepared.Actual.Channels, new("Alpha 1", "ChannelType.MASKEDAREA")],
            },
            _ => throw new InvalidOperationException(),
        };
        PhotoshopPreparedDocument invalid = h.Prepared with { Actual = facts };

        OperationResult<PhotoshopW1PreparedDocument> result = await h.Adapter.ExecuteW1Async(
            h.Opened, invalid, WhiteUnderbaseBranch.W1_1px, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        h.Native.ExecuteCount.ShouldBe(0);
    }

    [Fact]
    public async Task Backing_file_drift_refuses_before_Action()
    {
        Harness h = CreateHarness();
        File.WriteAllBytes(h.DocumentPath, [.. File.ReadAllBytes(h.DocumentPath), 0x44]);

        OperationResult<PhotoshopW1PreparedDocument> result = await Execute(h);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.RevisionIntegrityMismatch);
        h.Native.ExecuteCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("rgb")]
    [InlineData("sixteen")]
    [InlineData("pixels")]
    [InlineData("resolution")]
    [InlineData("physical")]
    [InlineData("three-components")]
    [InlineData("ordinary-alpha")]
    [InlineData("duplicate-w1")]
    [InlineData("ordinary-w1")]
    [InlineData("empty-w1")]
    [InlineData("document-count")]
    public async Task Invalid_post_Action_facts_are_not_accepted_or_corrected(string variant)
    {
        Harness h = CreateHarness();
        h.Native.OutcomeFactory = command =>
        {
            PhotoshopNativeW1Outcome value = Success(command, h.Prepared.Actual, h.DocumentCount);
            PhotoshopDocumentFacts after = value.After!;
            PhotoshopW1ChannelFacts w1 = value.W1!;
            return variant switch
            {
                "rgb" => value with { After = after with { ColourMode = "DocumentMode.RGB" } },
                "sixteen" => value with { After = after with { BitDepth = "BitsPerChannelType.SIXTEEN" } },
                "pixels" => value with { After = after with { PixelWidth = after.PixelWidth + 1 } },
                "resolution" => value with { After = after with { ResolutionPpi = 299 } },
                "physical" => value with { After = after with { PhysicalWidthMm = after.PhysicalWidthMm + 1 } },
                "three-components" => value with { After = after with { Channels = [.. after.Channels.Skip(1)] } },
                "ordinary-alpha" => value with { After = after with
                    { Channels = [.. after.Channels, new("Alpha 1", "ChannelType.MASKEDAREA")] } },
                "duplicate-w1" => value with { After = after with
                    { Channels = [.. after.Channels, new("W1", "ChannelType.SPOTCOLOR")] } },
                "ordinary-w1" => value with
                {
                    After = after with
                    {
                        Channels = [.. after.Channels.Select(c => c.Name == "W1"
                            ? c with { Type = "ChannelType.MASKEDAREA" }
                            : c)],
                    },
                    W1 = w1 with { Type = "ChannelType.MASKEDAREA" },
                },
                "empty-w1" => value with { W1 = w1 with { IsNonEmpty = false, NonWhitePixelCount = 0 } },
                "document-count" => value with { After = after with { DocumentCount = h.DocumentCount + 1 } },
                _ => throw new InvalidOperationException(),
            };
        };

        OperationResult<PhotoshopW1PreparedDocument> result = await Execute(h);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.OutputValidationFailed);
        result.Failure.Context["automaticRetry"].ShouldBe("false");
        h.Native.ExecuteCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_second_reported_Action_invocation_is_never_accepted()
    {
        Harness h = CreateHarness();
        h.Native.OutcomeFactory = command => Success(command, h.Prepared.Actual, h.DocumentCount) with
        {
            ActionInvocationCount = 2,
        };

        OperationResult<PhotoshopW1PreparedDocument> result = await Execute(h);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PhotoshopUnknownState);
        result.Failure.Context["automaticRetry"].ShouldBe("false");
        h.Native.ExecuteCount.ShouldBe(1);
    }

    [Fact]
    public async Task Throwing_Action_is_one_ambiguous_invocation_with_retained_state_not_a_retryable_precondition()
    {
        Harness h = CreateHarness();
        h.Native.OutcomeFactory = _ => new PhotoshopNativeW1Outcome(
            Succeeded: false,
            ActionInvocationCount: 1,
            FailureDetail: "The synchronous Action threw after input began.",
            Before: h.Prepared.Actual,
            After: null,
            W1: null);

        OperationResult<PhotoshopW1PreparedDocument> result = await Execute(h);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PhotoshopUnknownState);
        result.Failure.Context["actionInvocationCount"].ShouldBe("1");
        result.Failure.Context["inMemoryCmykW1MayBeRetained"].ShouldBe("true");
        result.Failure.Context["automaticRetry"].ShouldBe("false");
        h.Native.ExecuteCount.ShouldBe(1);
    }

    [Fact]
    public async Task Cancellation_before_Action_returns_structured_zero_invocation()
    {
        Harness h = CreateHarness();
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        OperationResult<PhotoshopW1PreparedDocument> result = await h.Adapter.ExecuteW1Async(
            h.Opened, h.Prepared, WhiteUnderbaseBranch.W1_1px, cancellation.Token);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.Cancelled);
        result.Failure.Context["actionInvocationCount"].ShouldBe("0");
        h.Native.ExecuteCount.ShouldBe(0);
        Hash(h.DocumentPath).ShouldBe(h.Prepared.BackingWorkingSha256);
    }

    [Fact]
    public async Task Cancellation_after_Action_records_retained_state_without_retry_or_save()
    {
        Harness h = CreateHarness();
        using CancellationTokenSource cancellation = new();
        h.Native.OutcomeFactory = command =>
        {
            cancellation.Cancel();
            return Success(command, h.Prepared.Actual, h.DocumentCount);
        };

        OperationResult<PhotoshopW1PreparedDocument> result = await h.Adapter.ExecuteW1Async(
            h.Opened, h.Prepared, WhiteUnderbaseBranch.W1_1px, cancellation.Token);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.Cancelled);
        result.Failure.Context["actionInvocationCount"].ShouldBe("1");
        result.Failure.Context["saved"].ShouldBe("false");
        h.Native.ExecuteCount.ShouldBe(1);
    }

    [Fact]
    public async Task Target_loss_after_Action_is_unknown_retained_state_not_success()
    {
        Harness h = CreateHarness();
        h.Native.OutcomeFactory = command =>
        {
            h.Locator.DeadProcessIds.Add(h.Opened.Target.Process.ProcessId);
            return Success(command, h.Prepared.Actual, h.DocumentCount);
        };

        OperationResult<PhotoshopW1PreparedDocument> result = await Execute(h);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PhotoshopTargetLost);
        result.Failure.Context["inMemoryCmykW1MayBeRetained"].ShouldBe("true");
        h.Native.ExecuteCount.ShouldBe(1);
    }

    [Fact]
    public async Task Modal_after_Action_stops_without_dismissal_or_second_Action()
    {
        Harness h = CreateHarness();
        h.Native.OutcomeFactory = command =>
        {
            h.Locator.OwnedDialogs.Add(PhotoshopFakes.Dialog(title: "Unexpected"));
            return Success(command, h.Prepared.Actual, h.DocumentCount);
        };

        OperationResult<PhotoshopW1PreparedDocument> result = await Execute(h);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PhotoshopBlockingDialog);
        h.Native.ExecuteCount.ShouldBe(1);
    }

    [Fact]
    public async Task Successful_W1_changes_only_in_memory_and_creates_no_output_file()
    {
        Harness h = CreateHarness();
        ImmutableArray<string> beforeFiles = Snapshot(h.DocumentDirectory);
        h.Native.OutcomeFactory = command => Success(command, h.Prepared.Actual, h.DocumentCount);

        OperationResult<PhotoshopW1PreparedDocument> result = await Execute(h);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);
        result.Value.ColourMode.ShouldBe("DocumentMode.CMYK");
        result.Value.BitDepth.ShouldBe("BitsPerChannelType.EIGHT");
        result.Value.ProcessChannels.Length.ShouldBe(4);
        result.Value.W1.Type.ShouldBe("ChannelType.SPOTCOLOR");
        result.Value.W1.IsNonEmpty.ShouldBeTrue();
        h.Driver.ProbeCount.ShouldBe(1,
            "Part A probes before Action; native fullName is the factual post-CMYK identity");
        Hash(h.DocumentPath).ShouldBe(h.Prepared.BackingWorkingSha256);
        Snapshot(h.DocumentDirectory).ShouldBe(beforeFiles);
        Directory.EnumerateFiles(h.DocumentDirectory, "*.tif").ShouldBeEmpty();
    }

    private static Task<OperationResult<PhotoshopW1PreparedDocument>> Execute(Harness h) =>
        h.Adapter.ExecuteW1Async(h.Opened, h.Prepared, WhiteUnderbaseBranch.W1_1px,
            CancellationToken.None);

    private Harness CreateHarness()
    {
        string executable = Path.Combine(_root, Guid.NewGuid().ToString("N"), "Photoshop.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllBytes(executable, [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00]);
        FileVersionInfo version = FileVersionInfo.GetVersionInfo(executable);

        string artifactPath = Path.Combine(_root, Guid.NewGuid().ToString("N"), "PrintFlow-DTF-v1.atn");
        Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);
        File.WriteAllBytes(artifactPath, [0x41, 0x54, 0x4E, 0x01]);
        PhotoshopW1ActionContract contract = Contract(artifactPath, Hash(artifactPath));

        string directory = Path.Combine(_root, Guid.NewGuid().ToString("N"), "Working", "A_1");
        Directory.CreateDirectory(directory);
        string documentPath = Path.Combine(directory, "PF_B1B_WORKING.png");
        File.WriteAllBytes(documentPath, [1, 2, 3, 4, 5, 6, 7, 8]);
        Sha256 backing = Hash(documentPath);

        PhotoshopBaseline baseline = PhotoshopFakes.Baseline() with
        {
            ExecutablePath = executable,
            ExecutableSha256 = Hash(executable),
            AcceptedProductVersion = version.ProductVersion ?? "(unreadable)",
            AcceptedFileVersion = version.FileVersion ?? "(unreadable)",
            W1Action = contract,
        };
        ExternalProcessRef process = new(7777, executable,
            new DateTimeOffset(2026, 8, 31, 9, 0, 0, TimeSpan.Zero));
        ExternalWindowRef window = PhotoshopFakes.Window(
            title: PhotoshopFakes.TitleFor(Path.GetFileName(documentPath)));
        PhotoshopTarget target = new(process, window);
        FakeWindowLocator locator = new();
        locator.Register(process, window);
        StubW1Driver driver = new(documentPath, window.Title);
        RecordingW1Bridge native = new();
        ProductionPhotoshopOutputProcessor adapter = new(
            new StubPhotoshopBaselineProvider(baseline), locator, driver,
            new StubPhotoshopWorkspace(directory), new PhotoshopAutomationOptions(),
            TimeProvider.System, new UnusedPreparationBridge(), native);

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
        PhotoshopDocumentFacts actual = Rgb(documentPath, 600, 400, 300, documentCount: 6);
        PhotoshopPreparedDocument prepared = new(
            Rgb(documentPath, 1200, 800, 240, documentCount: 6),
            actual,
            ResizeDirection.Shrink,
            PhotoshopResizeMode.BicubicSharper,
            LimitingEdge.Width,
            OtherDocumentsMayBeOpen: true,
            backing);

        return new Harness(adapter, locator, driver, native, opened, prepared,
            documentPath, directory, artifactPath, DocumentCount: 6);
    }

    private static PhotoshopW1ActionContract Contract(string artifactPath, Sha256 sha256) => new(
        artifactPath,
        sha256,
        "PrintFlow DTF",
        [
            new(WhiteUnderbaseBranch.W1_0px, "W1_0px", ["转换模式", "设置 选区", "建立"]),
            new(WhiteUnderbaseBranch.W1_1px, "W1_1px", ["转换模式", "设置 选区", "收缩", "建立"]),
            new(WhiteUnderbaseBranch.W1_2px, "W1_2px", ["转换模式", "设置 选区", "收缩", "建立"]),
        ]);

    private static PhotoshopNativeW1Outcome Success(
        PhotoshopNativeW1Command command, PhotoshopDocumentFacts before, int documentCount)
    {
        PhotoshopDocumentFacts after = new(
            before.DocumentFullPath,
            before.PixelWidth,
            before.PixelHeight,
            before.ResolutionPpi,
            before.PhysicalWidthMm,
            before.PhysicalHeightMm,
            "DocumentMode.CMYK",
            "BitsPerChannelType.EIGHT",
            [
                new("Cyan", "ChannelType.COMPONENT"),
                new("Magenta", "ChannelType.COMPONENT"),
                new("Yellow", "ChannelType.COMPONENT"),
                new("Black", "ChannelType.COMPONENT"),
                new("W1", "ChannelType.SPOTCOLOR"),
            ],
            W1Exists: true,
            documentCount);
        return new PhotoshopNativeW1Outcome(
            true,
            1,
            null,
            before,
            after,
            new PhotoshopW1ChannelFacts("W1", "ChannelType.SPOTCOLOR", true,
                NonWhitePixelCount: 1200, SolidityPercent: 100, SpotColourComponents: [255, 0, 0]));
    }

    private static PhotoshopDocumentFacts Rgb(
        string path, int width, int height, double resolution, int documentCount) => new(
        path,
        width,
        height,
        resolution,
        width / resolution * 25.4,
        height / resolution * 25.4,
        "DocumentMode.RGB",
        "BitsPerChannelType.EIGHT",
        [
            new("Red", "ChannelType.COMPONENT"),
            new("Green", "ChannelType.COMPONENT"),
            new("Blue", "ChannelType.COMPONENT"),
        ],
        W1Exists: false,
        documentCount);

    private static Sha256 Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Sha256.FromBytes(SHA256.HashData(stream));
    }

    private static ImmutableArray<string> Snapshot(string directory) =>
        [.. Directory.EnumerateFiles(directory).Order(StringComparer.OrdinalIgnoreCase)];

    private sealed record Harness(
        ProductionPhotoshopOutputProcessor Adapter,
        FakeWindowLocator Locator,
        StubW1Driver Driver,
        RecordingW1Bridge Native,
        PhotoshopOpenedDocument Opened,
        PhotoshopPreparedDocument Prepared,
        string DocumentPath,
        string DocumentDirectory,
        string ArtifactPath,
        int DocumentCount);

    private sealed class RecordingW1Bridge : IPhotoshopW1NativeBridge
    {
        public int ExecuteCount { get; private set; }
        public List<PhotoshopNativeW1Command> Commands { get; } = [];
        public Func<PhotoshopNativeW1Command, PhotoshopNativeW1Outcome>? OutcomeFactory { get; set; }

        public OperationResult<PhotoshopNativeW1Outcome> ExecuteOnce(
            PhotoshopNativeW1Command command, PhotoshopBaseline baseline)
        {
            _ = baseline;
            ExecuteCount++;
            Commands.Add(command);
            return OperationResult.Ok(OutcomeFactory?.Invoke(command) ??
                throw new InvalidOperationException("The test must script the native result."));
        }
    }

    private sealed class UnusedPreparationBridge : IPhotoshopPreparationNativeBridge
    {
        public OperationResult<PhotoshopNativePreparationOutcome> ApplyOnce(
            PhotoshopNativePreparationCommand command, PhotoshopBaseline baseline) =>
            throw new NotSupportedException();
    }

    private sealed class StubW1Driver : IPhotoshopUiDriver
    {
        public StubW1Driver(string path, string title)
        {
            WindowTitle = title;
            Identity = new PhotoshopDocumentIdentity(
                Path.GetFileName(path), Path.GetDirectoryName(path)!, path, title);
        }

        public string WindowTitle { get; set; }
        public PhotoshopDocumentIdentity Identity { get; set; }
        public int ProbeCount { get; private set; }

        public Task<OperationResult<PhotoshopStateSnapshot>> InspectStateAsync(
            PhotoshopTarget target, string? expectedDocumentFileName, CancellationToken cancellationToken)
        {
            _ = target;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(OperationResult.Ok(new PhotoshopStateSnapshot(
                PhotoshopStartingState.KnownEditorWithOtherDocument,
                [],
                new PhotoshopObservation(WindowTitle, [], [], true, expectedDocumentFileName))));
        }

        public Task<OperationResult<PhotoshopTarget>> ActivateAsync(
            PhotoshopTarget target, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Ok(target));

        public Task<OperationResult<PhotoshopTarget>> OpenManagedDocumentAsync(
            PhotoshopTarget target, string managedAbsolutePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationResult<PhotoshopDocumentIdentity>> ProbeDocumentIdentityAsync(
            PhotoshopTarget target, CancellationToken cancellationToken)
        {
            _ = target;
            cancellationToken.ThrowIfCancellationRequested();
            ProbeCount++;
            return Task.FromResult(OperationResult.Ok(Identity));
        }

        public Task<OperationResult<PhotoshopTarget>> CloseExactDocumentAsync(
            PhotoshopTarget target, string expectedAbsolutePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public OperationResult<EvidenceRef> CaptureEvidence(PhotoshopTarget target, string reason) =>
            OperationResult.Fail<EvidenceRef>(FailureCode.WorkspaceError, "not used");
    }
}
