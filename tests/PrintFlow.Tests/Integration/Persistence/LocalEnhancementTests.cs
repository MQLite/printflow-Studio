using System.IO;
using ImageMagick;
using ImageMagick.Formats;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Persistence;

[Collection(SqliteCollection.Name)]
public sealed class LocalEnhancementTests
{
    [Theory]
    [InlineData(1536, 512, 15)]
    [InlineData(3455, 512, 15)]
    [InlineData(3456, 768, 6)]
    [InlineData(6143, 768, 6)]
    [InlineData(6144, 1024, 2)]
    public void Large_upscale_uses_fewer_tiles_within_the_available_memory_budget(int availableMiB, int edge, int tileCount)
    {
        RealEsrganUpscaler.ModelEdgeFor((ulong)availableMiB * 1024 * 1024).ShouldBe(edge);
        (CountTiles(1536) * CountTiles(1024)).ShouldBe(tileCount);
        CountTiles(edge).ShouldBe(1);
        CountTiles(edge + 1).ShouldBe(2);

        int CountTiles(int length)
        {
            int count = 0;
            for (int start = 0; start < length; count++)
            {
                int end = RealEsrganUpscaler.TileEnd(start, length, edge);
                end.ShouldBeInRange(start + 1, length);
                int inputSize = Math.Min(length, end + 128) - Math.Max(0, start - 128);
                (inputSize + inputSize % 2).ShouldBeLessThanOrEqualTo(edge);
                if (end < length) (end % 2).ShouldBe(0);
                start = end;
            }
            return count;
        }
    }

    [Fact]
    public async Task Sharpening_preserves_canvas_16_bit_alpha_and_rgb_profile_without_hidden_colour_bleed()
    {
        using TempWorkspace temp = new();
        var backend = new LocalEnhancementBackend(new FileWorkspace(temp.Root), Path.Combine(temp.Root, "model.onnx"));
        var a = Request(ImageProcessingMethod.LightSharpen, "a");
        var b = Request(ImageProcessingMethod.LightSharpen, "b");
        WriteSample(Absolute(temp, a.Input), 0);
        WriteSample(Absolute(temp, b.Input), ushort.MaxValue);

        foreach (var request in new[] { a, b })
        {
            var result = await backend.ProcessAsync(request, default);
            result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : null);
            result.Value.AdapterNotes.ShouldNotBeNull().ShouldContain("12x8");
        }

        using var input = new MagickImage(Absolute(temp, a.Input));
        using var first = new MagickImage(Absolute(temp, a.ExpectedOutput));
        using var second = new MagickImage(Absolute(temp, b.ExpectedOutput));
        first.Format.ShouldBe(MagickFormat.Png);
        (first.Width, first.Height, first.Depth).ShouldBe((12u, 8u, 16u));
        first.GetColorProfile()!.ToByteArray().ShouldBe(input.GetColorProfile()!.ToByteArray());
        ushort[] source = Rgba(input);
        ushort[] sharp = Rgba(first);
        ushort[] alteredHidden = Rgba(second);
        bool sharpened = false;
        for (int pixel = 0; pixel < source.Length; pixel += 4)
        {
            sharp[pixel + 3].ShouldBe(source[pixel + 3]);
            if (source[pixel + 3] == 0)
                continue;
            sharp.AsSpan(pixel, 3).SequenceEqual(alteredHidden.AsSpan(pixel, 3)).ShouldBeTrue();
            sharpened |= sharp[pixel] != source[pixel];
        }
        sharpened.ShouldBeTrue();
    }

    [Fact]
    public async Task Sharpening_normalises_exif_orientation_and_keeps_the_displayed_geometry()
    {
        using TempWorkspace temp = new();
        var backend = new LocalEnhancementBackend(new FileWorkspace(temp.Root), Path.Combine(temp.Root, "model.onnx"));
        var request = Request(ImageProcessingMethod.LightSharpen, "oriented", "jpg");
        using (var input = new MagickImage(MagickColors.Orange, 12, 8))
        {
            var exif = new ExifProfile();
            exif.SetValue(ExifTag.Orientation, (ushort)Orientation.RightTop);
            input.SetProfile(exif);
            input.Orientation = Orientation.RightTop;
            input.Write(Absolute(temp, request.Input), MagickFormat.Jpeg);
        }

        var result = await backend.ProcessAsync(request, default);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : null);
        using var output = new MagickImage(Absolute(temp, request.ExpectedOutput));
        (output.Width, output.Height).ShouldBe((8u, 12u));
        output.Orientation.ShouldBeOneOf(Orientation.TopLeft, Orientation.Undefined);
    }

    [Fact]
    public async Task Missing_or_unverified_model_keeps_sharpen_available_and_writes_no_result()
    {
        using TempWorkspace temp = new();
        string model = Path.Combine(temp.Root, "model.onnx");
        var backend = new LocalEnhancementBackend(new FileWorkspace(temp.Root), model);
        backend.GetCapability(ImageProcessingMethod.LightSharpen)!.Unavailable.ShouldBeNull();
        backend.GetCapability(ImageProcessingMethod.AiUpscale)!.Unavailable!.MessageKey
            .ShouldBe("ImageProcessing_UpscaleModelMissing");
        File.WriteAllText(model, "not the model");
        backend.GetCapability(ImageProcessingMethod.AiUpscale)!.Unavailable!.MessageKey
            .ShouldBe("ImageProcessing_UpscaleModelInvalid");
        backend.GetCapability(ImageProcessingMethod.BiRefNet).ShouldBeNull();

        var request = Request(ImageProcessingMethod.AiUpscale, "source");
        WriteSample(Absolute(temp, request.Input), 0, depth: 8);
        var result = await backend.ProcessAsync(request, default);
        result.Failure.Code.ShouldBe(FailureCode.AdapterUnavailable);
        File.Exists(Absolute(temp, request.ExpectedOutput)).ShouldBeFalse();
    }

    [Fact]
    public async Task Upscale_refuses_oversized_or_16_bit_inputs_before_inference()
    {
        using TempWorkspace temp = new();
        var backend = new LocalEnhancementBackend(new FileWorkspace(temp.Root), Path.Combine(temp.Root, "model.onnx"));
        var large = Request(ImageProcessingMethod.AiUpscale, "large");
        using (var input = new MagickImage(MagickColors.Orange, 2049, 2048))
            input.Write(Absolute(temp, large.Input), MagickFormat.Png);
        var depth = Request(ImageProcessingMethod.AiUpscale, "depth");
        WriteSample(Absolute(temp, depth.Input), 0);

        (await backend.ProcessAsync(large, default)).Failure.MessageKey.ShouldBe("ImageProcessing_UpscaleTooLarge");
        (await backend.ProcessAsync(depth, default)).Failure.MessageKey.ShouldBe("ImageProcessing_UpscaleBitDepth");
        File.Exists(Absolute(temp, large.ExpectedOutput)).ShouldBeFalse();
        File.Exists(Absolute(temp, depth.ExpectedOutput)).ShouldBeFalse();
    }

    [Theory]
    [InlineData(513, 512, true)]
    [InlineData(4096, 1024, true)]
    [InlineData(2048, 2048, true)]
    [InlineData(2049, 2048, false)]
    [InlineData(1, 4097, false)]
    public void Upscale_bounds_total_pixels_and_panorama_edges(int width, int height, bool allowed) =>
        LocalUpscaleLimits.Fits(width, height).ShouldBe(allowed);

    [Fact]
    public async Task Cancelled_work_never_creates_a_candidate()
    {
        using TempWorkspace temp = new();
        var backend = new LocalEnhancementBackend(new FileWorkspace(temp.Root), Path.Combine(temp.Root, "model.onnx"));
        var request = Request(ImageProcessingMethod.LightSharpen, "cancelled");
        WriteSample(Absolute(temp, request.Input), 0);
        await Should.ThrowAsync<OperationCanceledException>(
            () => backend.ProcessAsync(request, new CancellationToken(canceled: true)));
        File.Exists(Absolute(temp, request.ExpectedOutput)).ShouldBeFalse();
    }

    [Fact]
    public async Task An_existing_task_can_switch_from_a_missing_upscale_model_to_sharpen()
    {
        using SessionServiceHarness harness = new();
        var backend = new LocalEnhancementBackend(harness.FileWorkspace, Path.Combine(harness.Workspace.Root, "model.onnx"));
        var service = harness.CreateService(imageProcessingBackends: [backend]);
        string source = Path.Combine(harness.Workspace.Root, "source.png");
        WriteSample(source, 0, 8);
        var id = (await service.ImportAsync(WorkflowType.PrepareAsset, source, null, "qa", default)).Value.Id;
        (await service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "qa", default)).IsSuccess.ShouldBeTrue();

        // Persisted selections can have become unavailable since the session was created.
        var upscale = SessionImageProcessingSelection.Compatible with { Enhancement = ImageProcessingMethod.AiUpscale };
        await Select(upscale);
        var denied = await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Enhancement), "qa", default);
        denied.Failure.MessageKey.ShouldBe("ImageProcessing_UpscaleModelMissing");
        (await harness.Repository.LoadAsync(id, default)).Value!.Attempts
            .ShouldNotContain(row => row.Step == StepKind.Enhancement);

        await Select(upscale with { Enhancement = ImageProcessingMethod.LightSharpen });
        var started = await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Enhancement), "qa", default);
        started.IsSuccess.ShouldBeTrue(started.IsFailure ? started.Failure.ToString() : null);
        started.Value.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        started.Value.CurrentArtefact!.Facts.PixelWidth.ShouldBe(12);
        started.Value.CurrentArtefact.Facts.PixelHeight.ShouldBe(8);

        async Task Select(SessionImageProcessingSelection selection) =>
            (await service.ExecuteAsync(id, new WorkflowCommand.SetImageProcessingSelection(StepKind.Enhancement, selection),
                "qa", default)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Real_sharpen_reaches_review_and_persists_size_and_version()
    {
        using SessionServiceHarness harness = new();
        var backend = new LocalEnhancementBackend(harness.FileWorkspace, Path.Combine(harness.Workspace.Root, "model.onnx"));
        var service = harness.CreateService(imageProcessingBackends: [backend]);
        string source = Path.Combine(harness.Workspace.Root, "source.png");
        WriteSample(source, 0);
        var imported = (await service.ImportAsync(WorkflowType.PrepareAsset, source, null, "qa", default)).Value;
        (await service.ExecuteAsync(imported.Id, new WorkflowCommand.ConfirmOriginal(), "qa", default)).IsSuccess
            .ShouldBeTrue();
        var selection = SessionImageProcessingSelection.Compatible with { Enhancement = ImageProcessingMethod.LightSharpen };
        (await service.ExecuteAsync(imported.Id, new WorkflowCommand.SetImageProcessingSelection(StepKind.Enhancement, selection),
            "qa", default)).IsSuccess.ShouldBeTrue();
        var started = await service.ExecuteAsync(imported.Id, new WorkflowCommand.StartStep(StepKind.Enhancement), "qa", default);
        started.IsSuccess.ShouldBeTrue(started.IsFailure ? started.Failure.ToString() : null);

        var loaded = (await harness.Repository.LoadAsync(imported.Id, default)).Value!;
        var attempt = loaded.Attempts.Single(row => row.Step == StepKind.Enhancement);
        attempt.ImageProcessingDetails!.AlgorithmVersion.ShouldBe(
            backend.GetCapability(ImageProcessingMethod.LightSharpen)!.AlgorithmVersion);
        attempt.AdapterNotes.ShouldNotBeNull().ShouldContain("12x8");
        loaded.Steps.Single(row => row.Step == StepKind.Enhancement).State.ShouldBe(StepState.ReviewRequired);
    }

    internal static ImageProcessingRequest Request(ImageProcessingMethod method, string name, string extension = "png") =>
        new(method, OperationKind.Enhance, LocalEnhancementBackend.VersionFor(method),
            new RevisionId(Guid.NewGuid()), Sha256.Parse(new string('0', 64)),
            WorkspaceFileRef.Create($"Sessions/probe/Working/{name}.{extension}", WorkspaceArea.Working),
            WorkspaceFileRef.Create($"Sessions/probe/Working/{name}-result.png", WorkspaceArea.Working), null);

    internal static string Absolute(TempWorkspace temp, WorkspaceFileRef reference)
    {
        string resolved = new FileWorkspace(temp.Root).ResolveAbsolute(reference);
        Directory.CreateDirectory(Path.GetDirectoryName(resolved)!);
        return resolved;
    }

    internal static ushort[] Rgba(MagickImage image)
    {
        using var pixels = image.GetPixels();
        return pixels.ToShortArray(PixelMapping.RGBA)!;
    }

    internal static void WriteSample(string path, ushort hiddenRed, uint depth = 16, uint width = 12, uint height = 8)
    {
        var values = new ushort[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int offset = (y * (int)width + x) * 4;
            values[offset] = x < 2 ? hiddenRed : x < 7 ? (ushort)16000 : (ushort)48000;
            values[offset + 1] = 22000;
            values[offset + 2] = 24000;
            values[offset + 3] = x < 2 ? (ushort)0 : x < 4 ? (ushort)29317 : ushort.MaxValue;
        }
        using var image = new MagickImage();
        image.ReadPixels(values, new PixelReadSettings(width, height, StorageType.Quantum, PixelMapping.RGBA));
        image.Depth = depth;
        image.SetProfile(ColorProfiles.AdobeRGB1998);
        image.Write(path, new PngWriteDefines { BitDepth = depth, PreserveiCCP = true });
    }
}
