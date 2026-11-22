using System.IO;
using System.Globalization;
using ImageMagick;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.Persistence;
using PrintFlow.Workflow.Commands;

namespace PrintFlow.Tests.Smoke;

[Collection(SqliteCollection.Name)]
public sealed class LocalEnhancementModelSmoke
{
    public sealed class ProvisionedModelFactAttribute : FactAttribute
    {
        public ProvisionedModelFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PRINTFLOW_B1_MODEL")))
                Skip = "Requires the provisioned Real-ESRGAN 2× model (PRINTFLOW_B1_MODEL).";
        }
    }

    private static string Model => Environment.GetEnvironmentVariable("PRINTFLOW_B1_MODEL")!;

    public sealed class ProvisionedSamplesFactAttribute : FactAttribute
    {
        public ProvisionedSamplesFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Model) ||
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PRINTFLOW_B1_SAMPLES")))
                Skip = "Requires the provisioned model and a portrait.png and logo.png sample directory.";
        }
    }

    [ProvisionedSamplesFact]
    public async Task Real_backends_render_the_same_portrait_and_logo_for_review()
    {
        using TempWorkspace temp = new();
        var backend = new LocalEnhancementBackend(new FileWorkspace(temp.Root), Model);
        string sampleDirectory = Environment.GetEnvironmentVariable("PRINTFLOW_B1_SAMPLES")!;
        foreach (string sample in new[] { "portrait", "logo" })
        {
            string samplePath = Path.Combine(sampleDirectory, sample + ".png");
            using var original = new MagickImage(samplePath);
            foreach (var method in new[] { ImageProcessingMethod.LightSharpen, ImageProcessingMethod.AiUpscale })
            {
                var request = LocalEnhancementTests.Request(method, sample + "-" + method);
                File.Copy(samplePath, LocalEnhancementTests.Absolute(temp, request.Input));
                var result = await backend.ProcessAsync(request, default);
                result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : null);
                string path = LocalEnhancementTests.Absolute(temp, request.ExpectedOutput);
                using var output = new MagickImage(path);
                uint factor = method == ImageProcessingMethod.AiUpscale ? 2u : 1u;
                (output.Width, output.Height).ShouldBe((original.Width * factor, original.Height * factor));
                output.HasAlpha.ShouldBe(original.HasAlpha);
                File.Copy(path, Path.Combine(sampleDirectory, Path.GetFileName(path)), overwrite: true);
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{sample} {method}: {result.Value.AdapterNotes} {result.Value.Elapsed.TotalMilliseconds:F0} ms"));
            }
        }
    }

    [ProvisionedModelFact]
    public async Task Native_model_doubles_odd_dimensions_preserves_profile_and_resamples_alpha_independently()
    {
        using TempWorkspace temp = new();
        var backend = new LocalEnhancementBackend(new FileWorkspace(temp.Root), Model);
        backend.GetCapability(ImageProcessingMethod.AiUpscale)!.Unavailable.ShouldBeNull();

        var first = LocalEnhancementTests.Request(ImageProcessingMethod.AiUpscale, "alpha-black");
        var second = LocalEnhancementTests.Request(ImageProcessingMethod.AiUpscale, "alpha-red");
        LocalEnhancementTests.WriteSample(LocalEnhancementTests.Absolute(temp, first.Input), 0, 8, 13, 9);
        LocalEnhancementTests.WriteSample(LocalEnhancementTests.Absolute(temp, second.Input), ushort.MaxValue, 8, 13, 9);
        foreach (var request in new[] { first, second })
        {
            var result = await backend.ProcessAsync(request, default);
            result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : null);
            result.Value.AdapterNotes.ShouldNotBeNull().ShouldContain("26x18");
        }

        using var input = new MagickImage(LocalEnhancementTests.Absolute(temp, first.Input));
        using var output = new MagickImage(LocalEnhancementTests.Absolute(temp, first.ExpectedOutput));
        using var otherOutput = new MagickImage(LocalEnhancementTests.Absolute(temp, second.ExpectedOutput));
        (output.Width, output.Height, output.Depth).ShouldBe((26u, 18u, 8u));
        output.GetColorProfile().ShouldNotBeNull().ToByteArray()
            .ShouldBe(input.GetColorProfile().ShouldNotBeNull().ToByteArray());
        ushort[] samples = LocalEnhancementTests.Rgba(output);
        samples.ShouldBe(LocalEnhancementTests.Rgba(otherOutput));
        ushort[] alpha = samples.Where((_, index) => index % 4 == 3).ToArray();
        alpha[0].ShouldBe((ushort)0);
        alpha[3].ShouldBeInRange((ushort)1, (ushort)29000);
        alpha[5].ShouldBeInRange((ushort)28000, (ushort)30000);
        alpha[25].ShouldBe(ushort.MaxValue);
    }

    [ProvisionedModelFact]
    public async Task Tiled_upscale_keeps_a_periodic_pattern_aligned_across_both_seams_and_the_last_pixel()
    {
        using TempWorkspace temp = new();
        var backend = new LocalEnhancementBackend(new FileWorkspace(temp.Root), Model);
        var request = LocalEnhancementTests.Request(ImageProcessingMethod.AiUpscale, "tiled");
        const int size = 513;
        var rgba = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int i = (y * size + x) * 4;
            rgba[i] = (byte)(40 + x % 8 * 20);
            rgba[i + 1] = (byte)(40 + y % 8 * 20);
            rgba[i + 2] = 90;
            rgba[i + 3] = (byte)(x == size - 1 && y == size - 1 ? 0 : 255);
        }
        using (var input = new MagickImage())
        {
            input.ReadPixels(rgba, new PixelReadSettings(size, size, StorageType.Char, PixelMapping.RGBA));
            input.Write(LocalEnhancementTests.Absolute(temp, request.Input), MagickFormat.Png32);
        }

        var result = await backend.ProcessAsync(request, default);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : null);
        using var output = new MagickImage(LocalEnhancementTests.Absolute(temp, request.ExpectedOutput));
        (output.Width, output.Height).ShouldBe((1026u, 1026u));
        using var pixels = output.GetPixels();
        var samples = pixels.ToByteArray(PixelMapping.RGBA)!;
        samples[^1].ShouldBe((byte)0);
        int maxDifference = 0;
        for (int y = 504; y < 520; y++)
        for (int x = 504; x < 520; x++)
        for (int c = 0; c < 3; c++)
        {
            int seam = (y * 1026 + x) * 4 + c;
            int interior = ((y - 256) * 1026 + x - 256) * 4 + c;
            maxDifference = Math.Max(maxDifference, Math.Abs(samples[seam] - samples[interior]));
        }
        maxDifference.ShouldBeLessThanOrEqualTo(6);
        Console.WriteLine($"513 × 513 tiled inference and PNG output: {result.Value.Elapsed}, maximum seam difference: {maxDifference}/255.");
    }

    [ProvisionedModelFact]
    public async Task Low_system_memory_refuses_this_run_and_allows_retry_after_memory_recovers()
    {
        using TempWorkspace temp = new();
        var workspace = new FileWorkspace(temp.Root);
        ulong availableMemory = RealEsrganUpscaler.MinimumAvailableMemory - 1;
        var backend = new LocalEnhancementBackend(workspace, new RealEsrganUpscaler(Model, () => availableMemory));
        var request = LocalEnhancementTests.Request(ImageProcessingMethod.AiUpscale, "low-memory");
        LocalEnhancementTests.WriteSample(LocalEnhancementTests.Absolute(temp, request.Input), 0, 8);
        (await backend.ProcessAsync(request, default)).Failure.MessageKey.ShouldBe("ImageProcessing_UpscaleLowMemory");
        File.Exists(LocalEnhancementTests.Absolute(temp, request.ExpectedOutput)).ShouldBeFalse();
        backend.GetCapability(ImageProcessingMethod.LightSharpen)!.Unavailable.ShouldBeNull();
        backend.GetCapability(ImageProcessingMethod.AiUpscale)!.Unavailable.ShouldBeNull();
        availableMemory++;
        var retried = await backend.ProcessAsync(request, default);
        retried.IsSuccess.ShouldBeTrue(retried.IsFailure ? retried.Failure.ToString() : null);
    }

    [ProvisionedModelFact]
    public async Task A_failed_upscale_can_switch_to_sharpen_and_preserves_both_attempts()
    {
        using SessionServiceHarness harness = new();
        var backend = new LocalEnhancementBackend(harness.FileWorkspace, Model);
        var service = harness.CreateService(imageProcessingBackends: [backend]);
        string source = Path.Combine(harness.Workspace.Root, "oversized.png");
        LocalEnhancementTests.WriteSample(source, 0, 8, 4097, 9);
        var id = (await service.ImportAsync(WorkflowType.PrepareAsset, source, null, "qa", default)).Value.Id;
        (await service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "qa", default)).IsSuccess.ShouldBeTrue();
        var command = new WorkflowCommand.StartStep(StepKind.Enhancement);

        await Select(ImageProcessingMethod.AiUpscale);
        (await service.ExecuteAsync(id, command, "qa", default)).Failure.MessageKey
            .ShouldBe("ImageProcessing_UpscaleTooLarge");
        await Select(ImageProcessingMethod.LightSharpen);
        var result = await service.ExecuteAsync(id, command, "qa", default);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : null);
        result.Value.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);

        var attempts = (await harness.Repository.LoadAsync(id, default)).Value!.Attempts
            .Where(row => row.Step == StepKind.Enhancement).ToArray();
        var failed = attempts.Single(row => row.ImageProcessingDetails!.Method == ImageProcessingMethod.AiUpscale);
        failed.Status.ShouldBe(AttemptStatus.Failed);
        failed.OutputRevisionId.ShouldBeNull();
        var recovered = attempts.Single(row => row.ImageProcessingDetails!.Method == ImageProcessingMethod.LightSharpen);
        recovered.RetryOfAttemptId.ShouldBe(failed.Id);

        async Task Select(ImageProcessingMethod method)
        {
            var selection = SessionImageProcessingSelection.Compatible with { Enhancement = method };
            (await service.ExecuteAsync(id, new WorkflowCommand.SetImageProcessingSelection(StepKind.Enhancement, selection),
                "qa", default)).IsSuccess.ShouldBeTrue();
        }
    }

    [ProvisionedModelFact]
    public async Task Cancelling_dispatched_upscaling_leaves_no_candidate()
    {
        using TempWorkspace temp = new();
        var backend = new LocalEnhancementBackend(new FileWorkspace(temp.Root), Model);
        backend.GetCapability(ImageProcessingMethod.AiUpscale)!.Unavailable.ShouldBeNull();
        var request = LocalEnhancementTests.Request(ImageProcessingMethod.AiUpscale, "cancel");
        LocalEnhancementTests.WriteSample(LocalEnhancementTests.Absolute(temp, request.Input), 0, 8, 512, 512);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await Should.ThrowAsync<OperationCanceledException>(() => backend.ProcessAsync(request, cancel.Token));
        File.Exists(LocalEnhancementTests.Absolute(temp, request.ExpectedOutput)).ShouldBeFalse();
        Directory.EnumerateFiles(Path.GetDirectoryName(LocalEnhancementTests.Absolute(temp, request.Input))!, "*.tmp")
            .ShouldBeEmpty();
    }
}
