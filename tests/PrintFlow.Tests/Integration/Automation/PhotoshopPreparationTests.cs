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
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Automation;

/// <summary>The focused B1A.3 production preparation contract against faked OS/native seams.</summary>
public sealed class PhotoshopPreparationTests : IDisposable
{
    private const int SourceWidth = 1200;
    private const int SourceHeight = 800;

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "PrintFlowPhotoshopPreparationTests", Guid.NewGuid().ToString("N"));

    public PhotoshopPreparationTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Theory]
    [InlineData("fit-resolution", TargetEdge.Width, 101.6, ResizeDirection.ResolutionOnly,
        PhotoshopResizeMode.None, LimitingEdge.None, 1200, 800)]
    [InlineData("fit-width-shrink", TargetEdge.Width, 50.8, ResizeDirection.Shrink,
        PhotoshopResizeMode.BicubicSharper, LimitingEdge.Width, 600, 400)]
    [InlineData("target-height-shrink", TargetEdge.Height, 33.8666666667, ResizeDirection.Shrink,
        PhotoshopResizeMode.BicubicSharper, LimitingEdge.Height, 600, 400)]
    [InlineData("target-width-enlarge", TargetEdge.Width, 203.2, ResizeDirection.Enlarge,
        PhotoshopResizeMode.PreserveDetails, LimitingEdge.Width, 2400, 1600)]
    [InlineData("target-height-enlarge", TargetEdge.Height, 135.4666666667, ResizeDirection.Enlarge,
        PhotoshopResizeMode.PreserveDetails, LimitingEdge.Height, 2400, 1600)]
    public async Task Closed_preparations_invoke_one_exact_native_operation_and_validate_actual_read_back(
        string variant,
        TargetEdge selectedEdge,
        double requestedMm,
        ResizeDirection direction,
        PhotoshopResizeMode policy,
        LimitingEdge commandedEdge,
        int projectedWidth,
        int projectedHeight)
    {
        Harness h = CreateHarness(hash => variant switch
        {
            "fit-resolution" => FitPreparation(hash, 200, 200),
            "fit-width-shrink" => FitPreparation(hash, requestedMm, 100),
            _ => TargetPreparation(hash, selectedEdge, (decimal)requestedMm),
        });
        h.Native.OutcomeFactory = command => SuccessOutcome(
            h.DocumentPath, command, projectedWidth, projectedHeight, h.DocumentCount);

        OperationResult<PhotoshopPreparedDocument> result = await h.Adapter.PrepareDocumentAsync(
            h.Opened, h.Preparation, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);
        result.Value.AppliedDirection.ShouldBe(direction);
        result.Value.AppliedResizePolicy.ShouldBe(policy);
        result.Value.CommandedEdge.ShouldBe(commandedEdge);
        result.Value.Actual.PixelWidth.ShouldBe(projectedWidth);
        result.Value.Actual.PixelHeight.ShouldBe(projectedHeight);
        result.Value.Actual.ResolutionPpi.ShouldBe(300);
        result.Value.Actual.ColourMode.ShouldBe(result.Value.Before.ColourMode);
        result.Value.Actual.BitDepth.ShouldBe(result.Value.Before.BitDepth);
        result.Value.Actual.Channels.ShouldBe(result.Value.Before.Channels);
        result.Value.Actual.W1Exists.ShouldBeFalse();
        result.Value.BackingWorkingSha256.ShouldBe(h.HashBefore);
        h.Native.ApplyCount.ShouldBe(1);
        h.Native.Commands.Single().Edge.ShouldBe(commandedEdge);
        h.Native.Commands.Single().ResampleMethod.ShouldBe(policy switch
        {
            PhotoshopResizeMode.None => PhotoshopNativeResampleMethod.NONE,
            PhotoshopResizeMode.BicubicSharper => PhotoshopNativeResampleMethod.BICUBICSHARPER,
            PhotoshopResizeMode.PreserveDetails => PhotoshopNativeResampleMethod.PRESERVEDETAILS,
            _ => throw new InvalidOperationException(),
        });
        Hash(h.DocumentPath).ShouldBe(h.HashBefore);
        Directory.EnumerateFiles(h.DocumentDirectory).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Same_basename_in_a_different_folder_is_refused_before_native_mutation()
    {
        Harness h = CreateHarness(hash => FitPreparation(hash, 200, 200));
        h.Driver.Identity = h.Driver.Identity with
        {
            ObservedDirectory = Path.Combine(_root, "operator"),
            ObservedFullPath = Path.Combine(_root, "operator", Path.GetFileName(h.DocumentPath)),
        };

        OperationResult<PhotoshopPreparedDocument> result = await h.Adapter.PrepareDocumentAsync(
            h.Opened, h.Preparation, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PhotoshopDocumentIdentityUnconfirmed);
        h.Native.ApplyCount.ShouldBe(0);
    }

    [Fact]
    public async Task Switched_active_document_is_refused_before_native_mutation()
    {
        Harness h = CreateHarness(hash => FitPreparation(hash, 200, 200));
        h.Driver.WindowTitle = "OPERATOR.png @ 100% (RGB/8)";

        OperationResult<PhotoshopPreparedDocument> result = await h.Adapter.PrepareDocumentAsync(
            h.Opened, h.Preparation, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PhotoshopUnknownState);
        h.Native.ApplyCount.ShouldBe(0);
    }

    [Fact]
    public async Task Pre_existing_W1_is_refused_by_the_native_precondition_without_resize()
    {
        Harness h = CreateHarness(hash => FitPreparation(hash, 200, 200));
        h.Native.OutcomeFactory = command => new PhotoshopNativePreparationOutcome(
            Succeeded: false,
            MutationInvoked: false,
            FailureDetail: "W1 already exists.",
            Before: Facts(h.DocumentPath, SourceWidth, SourceHeight, 240, h.DocumentCount, w1: true),
            After: null);

        OperationResult<PhotoshopPreparedDocument> result = await h.Adapter.PrepareDocumentAsync(
            h.Opened, h.Preparation, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        result.Failure.Context["mutationInvoked"].ShouldBe("false");
        h.Native.ApplyCount.ShouldBe(1);
        Hash(h.DocumentPath).ShouldBe(h.HashBefore);
    }

    [Fact]
    public async Task Actual_pixel_drift_is_a_failure_with_no_corrective_second_resize()
    {
        Harness h = CreateHarness(hash => TargetPreparation(hash, TargetEdge.Width, 50.8m));
        h.Native.OutcomeFactory = command => SuccessOutcome(
            h.DocumentPath, command, 601, 400, h.DocumentCount);

        OperationResult<PhotoshopPreparedDocument> result = await h.Adapter.PrepareDocumentAsync(
            h.Opened, h.Preparation, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.OutputValidationFailed);
        result.Failure.TechnicalDetail.ShouldContain("601×400");
        h.Native.ApplyCount.ShouldBe(1);
    }

    [Fact]
    public async Task Changed_mode_bit_depth_or_channels_is_not_accepted()
    {
        Harness h = CreateHarness(hash => TargetPreparation(hash, TargetEdge.Width, 50.8m));
        h.Native.OutcomeFactory = command =>
        {
            PhotoshopNativePreparationOutcome outcome = SuccessOutcome(
                h.DocumentPath, command, 600, 400, h.DocumentCount);
            return outcome with
            {
                After = outcome.After! with { ColourMode = "DocumentMode.CMYK" },
            };
        };

        OperationResult<PhotoshopPreparedDocument> result = await h.Adapter.PrepareDocumentAsync(
            h.Opened, h.Preparation, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.OutputValidationFailed);
        h.Native.ApplyCount.ShouldBe(1);
    }

    [Fact]
    public async Task Process_loss_after_mutation_returns_unknown_retained_state_not_success()
    {
        Harness h = CreateHarness(hash => TargetPreparation(hash, TargetEdge.Width, 50.8m));
        h.Native.OutcomeFactory = command =>
        {
            h.Locator.DeadProcessIds.Add(h.Opened.Target.Process.ProcessId);
            return SuccessOutcome(h.DocumentPath, command, 600, 400, h.DocumentCount);
        };

        OperationResult<PhotoshopPreparedDocument> result = await h.Adapter.PrepareDocumentAsync(
            h.Opened, h.Preparation, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PhotoshopTargetLost);
        result.Failure.Context["inMemoryPreparedMayBeRetained"].ShouldBe("true");
        h.Native.ApplyCount.ShouldBe(1);
    }

    [Fact]
    public async Task Modal_after_mutation_causes_no_later_identity_input()
    {
        Harness h = CreateHarness(hash => TargetPreparation(hash, TargetEdge.Width, 50.8m));
        h.Native.OutcomeFactory = command =>
        {
            h.Locator.OwnedDialogs.Add(PhotoshopFakes.Dialog(title: "Unexpected"));
            return SuccessOutcome(h.DocumentPath, command, 600, 400, h.DocumentCount);
        };
        int probesBeforePostGuard = h.Driver.ProbeCalls;

        OperationResult<PhotoshopPreparedDocument> result = await h.Adapter.PrepareDocumentAsync(
            h.Opened, h.Preparation, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PhotoshopBlockingDialog);
        h.Driver.ProbeCalls.ShouldBe(probesBeforePostGuard + 1);
        h.Native.ApplyCount.ShouldBe(1);
    }

    [Fact]
    public async Task Untitled_owned_Photoshop_chrome_is_not_misclassified_as_a_modal()
    {
        Harness h = CreateHarness(hash => TargetPreparation(hash, TargetEdge.Width, 50.8m));
        h.Locator.OwnedDialogs.Add(PhotoshopFakes.Dialog(title: string.Empty, className: "OWL.Dock"));
        h.Native.OutcomeFactory = command => SuccessOutcome(
            h.DocumentPath, command, 600, 400, h.DocumentCount);

        OperationResult<PhotoshopPreparedDocument> result = await h.Adapter.PrepareDocumentAsync(
            h.Opened, h.Preparation, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);
        h.Native.ApplyCount.ShouldBe(1);
    }

    [Fact]
    public async Task Cancellation_before_native_call_is_structured_and_has_zero_mutation()
    {
        Harness h = CreateHarness(hash => TargetPreparation(hash, TargetEdge.Width, 50.8m));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        OperationResult<PhotoshopPreparedDocument> result = await h.Adapter.PrepareDocumentAsync(
            h.Opened, h.Preparation, cancellation.Token);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.Cancelled);
        result.Failure.Context["mutationInvoked"].ShouldBe("false");
        h.Native.ApplyCount.ShouldBe(0);
        Hash(h.DocumentPath).ShouldBe(h.HashBefore);
    }

    [Fact]
    public async Task Cancellation_during_synchronous_call_finishes_readback_and_reports_retained_result()
    {
        Harness h = CreateHarness(hash => TargetPreparation(hash, TargetEdge.Height, 33.8666666667m));
        using CancellationTokenSource cancellation = new();
        h.Native.OutcomeFactory = command =>
        {
            cancellation.Cancel();
            return SuccessOutcome(h.DocumentPath, command, 600, 400, h.DocumentCount);
        };

        OperationResult<PhotoshopPreparedDocument> result = await h.Adapter.PrepareDocumentAsync(
            h.Opened, h.Preparation, cancellation.Token);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.Cancelled);
        result.Failure.Context["inMemoryPreparedMayBeRetained"].ShouldBe("true");
        result.Failure.Context["actualPixelWidth"].ShouldBe("600");
        h.Native.ApplyCount.ShouldBe(1);
        h.Driver.ProbeCalls.ShouldBe(2);
        Hash(h.DocumentPath).ShouldBe(h.HashBefore);
    }

    [Fact]
    public void Neutral_policy_mapping_is_exact_and_unsupported_values_refuse()
    {
        PhotoshopResizePolicyMapping.Map(PhotoshopResizeMode.None).Value
            .ShouldBe(PhotoshopNativeResampleMethod.NONE);
        PhotoshopResizePolicyMapping.Map(PhotoshopResizeMode.BicubicSharper).Value
            .ShouldBe(PhotoshopNativeResampleMethod.BICUBICSHARPER);
        PhotoshopResizePolicyMapping.Map(PhotoshopResizeMode.PreserveDetails).Value
            .ShouldBe(PhotoshopNativeResampleMethod.PRESERVEDETAILS);

        OperationResult<PhotoshopNativeResampleMethod> unsupported =
            PhotoshopResizePolicyMapping.Map((PhotoshopResizeMode)999);
        unsupported.IsFailure.ShouldBeTrue();
        unsupported.Failure.Context["fallbackUsed"].ShouldBe("false");
    }

    [Fact]
    public void Native_program_writes_only_the_resolved_edge_and_uses_no_automatic_method()
    {
        string width = PhotoshopPreparationProgram.Create(new PhotoshopNativePreparationCommand(
            @"C:\managed\working.png", 1200, 800, LimitingEdge.Width, 50.8, 300,
            PhotoshopNativeResampleMethod.BICUBICSHARPER));
        string height = PhotoshopPreparationProgram.Create(new PhotoshopNativePreparationCommand(
            @"C:\managed\working.png", 1200, 800, LimitingEdge.Height, 33.8666666667, 300,
            PhotoshopNativeResampleMethod.PRESERVEDETAILS));
        string resolution = PhotoshopPreparationProgram.Create(new PhotoshopNativePreparationCommand(
            @"C:\managed\working.png", 1200, 800, LimitingEdge.None, null, 300,
            PhotoshopNativeResampleMethod.NONE));

        width.ShouldContain("resizeImage(UnitValue(50.8, 'mm'), undefined, 300, ResampleMethod.BICUBICSHARPER)");
        height.ShouldContain("resizeImage(undefined, UnitValue(33.8666666667, 'mm'), 300, ResampleMethod.PRESERVEDETAILS)");
        resolution.ShouldContain("resizeImage(undefined, undefined, 300, ResampleMethod.NONE)");
        width.ShouldNotContain("AUTOMATIC", Case.Insensitive);
        width.ShouldNotContain("BICUBICSMOOTHER", Case.Insensitive);
        width.ShouldNotContain("projectedPixel", Case.Insensitive);
    }

    private Harness CreateHarness(Func<Sha256, PhotoshopPreparation> preparationFactory)
    {
        string executable = Path.Combine(_root, Guid.NewGuid().ToString("N"), "Photoshop.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllBytes(executable, [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00]);

        string directory = Path.Combine(_root, Guid.NewGuid().ToString("N"), "Working", "A_1");
        Directory.CreateDirectory(directory);
        string documentPath = Path.Combine(directory, "PF_B1A3_WORKING.png");
        File.WriteAllBytes(documentPath, [1, 2, 3, 4, 5, 6, 7, 8]);
        Sha256 hash = Hash(documentPath);
        PhotoshopPreparation preparation = preparationFactory(hash);

        FileVersionInfo version = FileVersionInfo.GetVersionInfo(executable);
        PhotoshopBaseline baseline = PhotoshopFakes.Baseline() with
        {
            ExecutablePath = executable,
            ExecutableSha256 = Hash(executable),
            AcceptedProductVersion = version.ProductVersion ?? "(unreadable)",
            AcceptedFileVersion = version.FileVersion ?? "(unreadable)",
        };

        ExternalProcessRef process = new(
            7777, executable, new DateTimeOffset(2026, 8, 31, 9, 0, 0, TimeSpan.Zero));
        ExternalWindowRef window = PhotoshopFakes.Window(
            title: PhotoshopFakes.TitleFor(Path.GetFileName(documentPath)));
        PhotoshopTarget target = new(process, window);
        FakeWindowLocator locator = new();
        locator.Register(process, window);

        StubPreparationDriver driver = new(documentPath, window.Title);
        RecordingNativeBridge native = new();
        ProductionPhotoshopOutputProcessor adapter = new(
            new StubPhotoshopBaselineProvider(baseline),
            locator,
            driver,
            new StubPhotoshopWorkspace(directory),
            new PhotoshopAutomationOptions(),
            TimeProvider.System,
            native);

        PhotoshopDocumentIdentity identity = new(
            Path.GetFileName(documentPath), directory, documentPath, window.Title);
        PhotoshopOpenedDocument opened = new(
            target,
            new PhotoshopStateSnapshot(
                PhotoshopStartingState.KnownEditorWithExpectedDocument,
                [],
                new PhotoshopObservation(window.Title, [], [], true, Path.GetFileName(documentPath), documentPath)),
            identity,
            OtherDocumentsMayBeOpen: true);

        return new Harness(
            adapter, locator, driver, native, opened, preparation, documentPath, directory, hash,
            DocumentCount: 6);
    }

    private static PhotoshopPreparation FitPreparation(Sha256 hash, double maxWidthMm, double maxHeightMm) =>
        new FitWithinBoundsPreparation(PrintPreparationPlan.For(
            new RevisionId(Guid.NewGuid()),
            hash,
            SourceWidth,
            SourceHeight,
            PrintDimensions.FromMillimetres(maxWidthMm, maxHeightMm, SizePreset.Custom)));

    private static PhotoshopPreparation TargetPreparation(
        Sha256 hash, TargetEdge edge, decimal millimetres)
    {
        TargetEdgePrintPreparationPlan plan = TargetEdgePrintPreparationPlan.For(
            new RevisionId(Guid.NewGuid()),
            hash,
            SourceWidth,
            SourceHeight,
            FlexibleSizeSelection.CustomTarget(edge, millimetres));
        return new TargetEdgePreparation(
            plan,
            plan.RequiresEnlargementAuthority ? EnlargementAuthority.For(plan) : null);
    }

    private static PhotoshopNativePreparationOutcome SuccessOutcome(
        string path,
        PhotoshopNativePreparationCommand command,
        int actualWidth,
        int actualHeight,
        int documentCount) => new(
            Succeeded: true,
            MutationInvoked: true,
            FailureDetail: null,
            Before: Facts(path, SourceWidth, SourceHeight, 240, documentCount),
            After: Facts(path, actualWidth, actualHeight, 300, documentCount));

    private static PhotoshopDocumentFacts Facts(
        string path,
        int width,
        int height,
        double resolution,
        int documentCount,
        bool w1 = false) => new(
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
                new("Alpha 1", "ChannelType.MASKEDAREA"),
            ],
            w1,
            documentCount);

    private static Sha256 Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Sha256.FromBytes(SHA256.HashData(stream));
    }

    private sealed record Harness(
        ProductionPhotoshopOutputProcessor Adapter,
        FakeWindowLocator Locator,
        StubPreparationDriver Driver,
        RecordingNativeBridge Native,
        PhotoshopOpenedDocument Opened,
        PhotoshopPreparation Preparation,
        string DocumentPath,
        string DocumentDirectory,
        Sha256 HashBefore,
        int DocumentCount);

    private sealed class RecordingNativeBridge : IPhotoshopPreparationNativeBridge
    {
        public int ApplyCount { get; private set; }
        public List<PhotoshopNativePreparationCommand> Commands { get; } = [];
        public Func<PhotoshopNativePreparationCommand, PhotoshopNativePreparationOutcome>? OutcomeFactory { get; set; }

        public OperationResult<PhotoshopNativePreparationOutcome> ApplyOnce(
            PhotoshopNativePreparationCommand command,
            PhotoshopBaseline baseline)
        {
            _ = baseline;
            ApplyCount++;
            Commands.Add(command);
            return OperationResult.Ok(OutcomeFactory?.Invoke(command) ?? throw new InvalidOperationException(
                "The test must script the factual native outcome."));
        }
    }

    private sealed class StubPreparationDriver : IPhotoshopUiDriver
    {
        public StubPreparationDriver(string path, string title)
        {
            WindowTitle = title;
            Identity = new PhotoshopDocumentIdentity(
                Path.GetFileName(path), Path.GetDirectoryName(path)!, path, title);
        }

        public string WindowTitle { get; set; }
        public PhotoshopDocumentIdentity Identity { get; set; }
        public int ProbeCalls { get; private set; }

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
            ProbeCalls++;
            return Task.FromResult(OperationResult.Ok(Identity));
        }

        public Task<OperationResult<PhotoshopTarget>> CloseExactDocumentAsync(
            PhotoshopTarget target, string expectedAbsolutePath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public OperationResult<EvidenceRef> CaptureEvidence(PhotoshopTarget target, string reason) =>
            OperationResult.Fail<EvidenceRef>(FailureCode.WorkspaceError, "not used");
    }
}
