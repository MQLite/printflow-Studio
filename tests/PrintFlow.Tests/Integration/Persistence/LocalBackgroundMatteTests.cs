using System.IO;
using ImageMagick;
using ImageMagick.Drawing;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Infrastructure.Verification;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

[Collection(SqliteCollection.Name)]
public sealed class LocalBackgroundMatteTests
{
    [Fact]
    public void Application_registers_a_local_birefnet_capability()
    {
        using var harness = new SessionServiceHarness();
        var config = PrintFlowConfiguration.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
        using var provider = ServiceRegistration.BuildServiceProvider(config, harness.Workspace.Root, harness.Database.Factory);

        var capability = provider.GetServices<IImageProcessingBackend>()
            .Select(backend => backend.GetCapability(ImageProcessingMethod.BiRefNet))
            .SingleOrDefault(candidate => candidate is not null);
        capability.ShouldNotBeNull();
        capability.AdapterId.ShouldBe("local-birefnet-cpu");
    }

    [Theory]
    [InlineData(8u)]
    [InlineData(16u)]
    public async Task Keeps_exact_rgb_and_icc_and_multiplies_alpha_on_the_original_canvas(uint depth)
    {
        using var temp = new TempWorkspace();
        var request = Request("aligned");
        string input = LocalEnhancementTests.Absolute(temp, request.Input);
        LocalEnhancementTests.WriteSample(input, ushort.MaxValue, depth, width: 13, height: 9);
        var backend = WithMatte(new FileWorkspace(temp.Root), AlignedMatte);
        await Must(backend.ProcessAsync(request, default));

        using var original = new MagickImage(input);
        using var cutout = new MagickImage(LocalEnhancementTests.Absolute(temp, request.ExpectedOutput));
        (cutout.Width, cutout.Height, cutout.Depth).ShouldBe((13u, 9u, depth));
        cutout.GetColorProfile()!.ToByteArray().ShouldBe(original.GetColorProfile()!.ToByteArray());
        ushort[] before = LocalEnhancementTests.Rgba(original);
        ushort[] after = LocalEnhancementTests.Rgba(cutout);
        AssertRgbEqual(before, after);
        int quantumScale = depth == 16 ? 1 : 257;
        for (int y = 0; y < 9; y++)
        for (int x = 0; x < 13; x++)
        {
            int index = (y * 13 + x) * 4;
            float coverage = (x < 3 && y < 6) || x == 8 ? 1f : y < 7 && x < 12 ? 0.5f : 0f;
            after[index + 3].ShouldBe((ushort)(MathF.Round(before[index + 3] * coverage / quantumScale) * quantumScale),
                $"Alpha at {x},{y}");
        }
    }

    [Theory]
    [InlineData(Orientation.TopRight)]
    [InlineData(Orientation.BottomRight)]
    [InlineData(Orientation.BottomLeft)]
    [InlineData(Orientation.LeftTop)]
    [InlineData(Orientation.RightTop)]
    [InlineData(Orientation.RightBottom)]
    [InlineData(Orientation.LeftBottom)]
    public async Task Oriented_jpeg_preserves_encoded_canvas_orientation_and_alignment(Orientation orientation)
    {
        using var temp = new TempWorkspace();
        var request = Request("oriented", "jpg");
        string input = LocalEnhancementTests.Absolute(temp, request.Input);
        using (var image = new MagickImage(MagickColors.White, 17, 9))
        {
            new Drawables().FillColor(MagickColors.Orange).Rectangle(0, 0, 7, 8).Draw(image);
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)orientation);
            image.SetProfile(exif);
            image.Orientation = orientation;
            image.Write(input, MagickFormat.Jpeg);
        }
        var backend = WithMatte(new FileWorkspace(temp.Root), AlignedMatte);
        await Must(backend.ProcessAsync(request, default));

        using var original = new MagickImage(input);
        using var cutout = new MagickImage(LocalEnhancementTests.Absolute(temp, request.ExpectedOutput));
        (cutout.Width, cutout.Height).ShouldBe((17u, 9u));
        cutout.Orientation.ShouldBe(orientation);
        AssertRgbEqual(LocalEnhancementTests.Rgba(original), LocalEnhancementTests.Rgba(cutout));
        cutout.AutoOrient();
        ushort[] displayed = LocalEnhancementTests.Rgba(cutout);
        displayed[3].ShouldBe(ushort.MaxValue);
        displayed[^1].ShouldBe((ushort)0);
    }

    [Theory]
    [InlineData("transparent")]
    [InlineData("opaque")]
    [InlineData("wrong-shape")]
    [InlineData("non-finite")]
    [InlineData("empty-composite")]
    [InlineData("flat-partial")]
    [InlineData("only-already-transparent")]
    [InlineData("uniform-on-visible-input")]
    public async Task Invalid_or_empty_matte_never_leaves_a_candidate(string scenario)
    {
        using var temp = new TempWorkspace();
        var request = Request("invalid");
        LocalEnhancementTests.WriteSample(LocalEnhancementTests.Absolute(temp, request.Input), 0);
        var backend = WithMatte(new FileWorkspace(temp.Root), () => scenario switch
        {
            "wrong-shape" => [0f],
            "opaque" => MakeMatte((_, _) => 100f),
            "flat-partial" => MakeMatte((_, _) => 0f),
            "only-already-transparent" => MakeMatte((x, _) => x < 160 ? -100f : 100f),
            "uniform-on-visible-input" => MakeMatte((x, _) => x < 160 ? -100f : 0f),
            "non-finite" => MakeMatte((x, _) => x == 0 ? float.NaN : 0f),
            "empty-composite" => MakeMatte((x, _) => x < 100 ? 100f : -100f),
            _ => MakeMatte((_, _) => -100f),
        });
        var result = await backend.ProcessAsync(request, default);
        result.Failure.Code.ShouldBe(FailureCode.OutputValidationFailed);
        result.Failure.MessageKey.ShouldBe("ImageProcessing_BiRefNetInvalidMatte");
        File.Exists(LocalEnhancementTests.Absolute(temp, request.ExpectedOutput)).ShouldBeFalse();
    }

    [Fact]
    public async Task Bad_png_and_oversized_input_are_rejected_before_inference()
    {
        using var temp = new TempWorkspace();
        var backend = WithMatte(new FileWorkspace(temp.Root), () => throw new InvalidOperationException("Unexpected inference"));
        var bad = Request("broken");
        File.WriteAllBytes(LocalEnhancementTests.Absolute(temp, bad.Input),
            [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0, 0, 0, 0, 0]);
        (await backend.ProcessAsync(bad, default)).Failure.MessageKey.ShouldBe("ImageProcessing_BiRefNetImageError");

        var large = Request("large");
        using (var image = new MagickImage(MagickColors.White, 8193, 7))
            image.Write(LocalEnhancementTests.Absolute(temp, large.Input), MagickFormat.Png);
        (await backend.ProcessAsync(large, default)).Failure.MessageKey.ShouldBe("ImageProcessing_BiRefNetTooLarge");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Workflow_binds_authority_persists_attempt_and_offers_review_or_manual_import(bool badMatte)
    {
        using var harness = new SessionServiceHarness();
        var backend = WithMatte(harness.FileWorkspace, badMatte ? () => MakeMatte((_, _) => -100f) : AlignedMatte);
        var (service, ready) = await AtBackgroundRemoval(harness, backend);
        var id = ready.Id;
        var command = new WorkflowCommand.StartStep(StepKind.BackgroundRemoval);
        (await service.ExecuteAsync(id, command, "qa", default)).Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        var source = ready.CurrentArtefact!;
        await Must(service.ExecuteAsync(id, new WorkflowCommand.SetBackgroundRemovalDecision(
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent, source.RevisionId, source.Sha256,
            BackgroundRemovalMode.Automatic, ImageProcessingMethod.BiRefNet), "qa", default));

        var result = await service.ExecuteAsync(id, command, "qa", default);
        var saved = (await Must(harness.Repository.LoadAsync(id, default)))!;
        var attempt = saved.Attempts.Single(a => a.Step == StepKind.BackgroundRemoval);
        attempt.BackgroundRemovalAuthority!.Authorises(source.RevisionId, source.Sha256).ShouldBeTrue();
        attempt.ImageProcessingDetails!.Method.ShouldBe(ImageProcessingMethod.BiRefNet);
        attempt.ImageProcessingDetails.AlgorithmVersion.ShouldBe(BiRefNetMatteModel.AlgorithmVersion);
        (await Must(harness.Repository.GetAutomationLockAsync(default))).IsHeld.ShouldBeFalse();
        if (badMatte)
        {
            result.Failure.MessageKey.ShouldBe("ImageProcessing_BiRefNetInvalidMatte");
            attempt.Status.ShouldBe(AttemptStatus.Failed);
            saved.Revisions.ShouldNotContain(r => r.Operation == OperationKind.RemoveBackground);
            await HandOffAndImport(harness, service, id);
        }
        else
        {
            result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : null);
            result.Value.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
            result.Value.CurrentArtefact!.Facts.HasAlpha.ShouldBe(true);
            result.Value.CurrentArtefact.Facts.PixelWidth.ShouldBe(source.Facts.PixelWidth);
            result.Value.CurrentArtefact.Facts.PixelHeight.ShouldBe(source.Facts.PixelHeight);
        }
    }

    [Fact]
    public async Task Unavailable_model_can_be_handed_off_without_an_attempt_or_releasing_an_unrelated_lock()
    {
        using var harness = new SessionServiceHarness();
        string path = Path.Combine(harness.Workspace.Root, "birefnet.onnx");
        var backend = new LocalBackgroundMatteBackend(harness.FileWorkspace, path);
        backend.GetCapability(ImageProcessingMethod.BiRefNet)!.Unavailable!.MessageKey.ShouldBe("ImageProcessing_BiRefNetModelMissing");
        File.WriteAllText(path, "invalid");
        var (service, ready) = await AtBackgroundRemoval(harness, backend);
        ready.ImageProcessingExecution!.Unavailable!.MessageKey.ShouldBe("ImageProcessing_BiRefNetModelInvalid");

        var verification = new SqliteEnvironmentAutomationLock(harness.Database.Factory, 101, "TEST");
        var lease = await Must(verification.TryAcquireAsync(harness.Clock.GetUtcNow(), default));
        try
        {
            await HandOffAndImport(harness, service, ready.Id);
            (await Must(verification.ReadAsync(default))).OwnerToken.ShouldBe(lease.OwnerToken);
        }
        finally
        {
            await Must(verification.ReleaseAsync(lease, default));
        }
        (await Must(harness.Repository.LoadAsync(ready.Id, default)))!.Attempts
            .ShouldNotContain(a => a.Operation == OperationKind.RemoveBackground);
    }

    [Fact]
    public async Task Stop_after_inference_leaves_no_candidate()
    {
        using var temp = new TempWorkspace();
        using var cancellation = new CancellationTokenSource();
        var request = Request("stopped");
        LocalEnhancementTests.WriteSample(LocalEnhancementTests.Absolute(temp, request.Input), 0, depth: 8);
        var backend = WithMatte(new FileWorkspace(temp.Root), () =>
        {
            cancellation.Cancel();
            return AlignedMatte();
        });

        await Should.ThrowAsync<OperationCanceledException>(() => backend.ProcessAsync(request, cancellation.Token));
        File.Exists(LocalEnhancementTests.Absolute(temp, request.ExpectedOutput)).ShouldBeFalse();
    }

    internal static ImageProcessingRequest Request(string name, string extension = "png")
    {
        var revision = new RevisionId(Guid.NewGuid());
        var hash = Sha256.Parse(new string('0', 64));
        return new(ImageProcessingMethod.BiRefNet, OperationKind.RemoveBackground, BiRefNetMatteModel.AlgorithmVersion,
            revision, hash,
            WorkspaceFileRef.Create($"Sessions/probe/Working/{name}.{extension}", WorkspaceArea.Working),
            WorkspaceFileRef.Create($"Sessions/probe/Working/{name}-result.png", WorkspaceArea.Working),
            BackgroundRemovalAuthority.For(BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent, revision, hash));
    }

    internal static async Task<T> Must<T>(Task<OperationResult<T>> operation)
    {
        var result = await operation;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : null);
        return result.Value;
    }

    private static void AssertRgbEqual(ushort[] source, ushort[] cutout)
    {
        cutout.Length.ShouldBe(source.Length);
        for (int index = 0; index < source.Length; index += 4)
            cutout.AsSpan(index, 3).SequenceEqual(source.AsSpan(index, 3)).ShouldBeTrue($"RGB at pixel {index / 4}");
    }

    private static LocalBackgroundMatteBackend WithMatte(IWorkspace workspace, Func<float[]> matte) =>
        new(workspace, () => null, (_, _) => OperationResult.Ok(matte()));

    private static float[] AlignedMatte() => MakeMatte((x, y) =>
        x < 236 && y < 675 || x is >= 621 and < 710 ? 100f : y < 797 && x < 945 ? 0f : -100f);

    private static float[] MakeMatte(Func<int, int, float> sample)
    {
        int edge = BiRefNetMatte.Edge;
        var matte = new float[BiRefNetMatte.Area];
        for (int y = 0; y < edge; y++)
        for (int x = 0; x < edge; x++)
            matte[y * edge + x] = sample(x, y);
        return matte;
    }

    private static async Task<(ISessionService Service, SessionView Ready)> AtBackgroundRemoval(
        SessionServiceHarness harness, IImageProcessingBackend backend)
    {
        var service = harness.CreateService(imageProcessingBackends: [backend]);
        string source = harness.Workspace.CreateSourceFile("source.png",
            SyntheticImages.PngWithAlpha(13, 9, (_, _) => byte.MaxValue));
        var id = (await Must(service.ImportAsync(WorkflowType.PrepareAsset, source, null, "qa", default))).Id;
        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "qa", default));
        await Must(service.ExecuteAsync(id, new WorkflowCommand.Skip(StepKind.Enhancement), "qa", default));
        var selection = SessionImageProcessingSelection.Compatible with { BackgroundRemoval = ImageProcessingMethod.BiRefNet };
        var ready = await Must(service.ExecuteAsync(id,
            new WorkflowCommand.SetImageProcessingSelection(StepKind.BackgroundRemoval, selection), "qa", default));
        return (service, ready);
    }

    private static async Task HandOffAndImport(SessionServiceHarness harness, ISessionService service, SessionId id)
    {
        var handedOff = await Must(service.ExecuteAsync(id,
            new WorkflowCommand.HandOff(StepKind.BackgroundRemoval, "Finish manually"), "qa", default));
        handedOff.CanSubmitManualResult.ShouldBeTrue();
        string manual = harness.Workspace.CreateSourceFile("hand-cut.png",
            SyntheticImages.PngWithAlpha(13, 9, (x, _) => x < 3 ? (byte)0 : byte.MaxValue));
        var review = await Must(service.ExecuteAsync(id,
            new WorkflowCommand.SubmitManualResult(StepKind.BackgroundRemoval, manual), "qa", default));
        review.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        review.CurrentArtefact!.IsManualProcessingResult.ShouldBeTrue();
    }
}
