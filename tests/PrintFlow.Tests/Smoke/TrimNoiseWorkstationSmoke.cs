using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Trimming;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Smoke;

/// <summary>Opt-in local image proof; reads the supplied original and writes only a temporary working copy.</summary>
public sealed class TrimNoiseWorkstationSmoke
{
    [Fact]
    public async Task Reported_input_crops_to_independently_measured_bounds_without_changing_retained_pixels()
    {
        string? original = Environment.GetEnvironmentVariable("PRINTFLOW_TRIM_PROBE_INPUT");
        if (string.IsNullOrWhiteSpace(original)) return;
        string expectedText = Environment.GetEnvironmentVariable("PRINTFLOW_TRIM_PROBE_EXPECTED_BOUNDS")
            ?? throw new InvalidOperationException("Independently measured expected bounds are required.");
        int[] edges = expectedText.Split(',').Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();
        edges.Length.ShouldBe(4);
        TrimBounds expected = TrimBounds.FromEdges(edges[0], edges[1], edges[2], edges[3]);
        byte[] originalBytes = File.ReadAllBytes(original);

        using TempWorkspace temporary = new() { RetainForInspection = true };
        FileWorkspace workspace = new(temporary.Root);
        WorkspaceFileRef input = WorkspaceFileRef.Create("Sessions/probe/Working/input.png", WorkspaceArea.Working);
        WorkspaceFileRef filteredOutput = WorkspaceFileRef.Create("Sessions/probe/Working/filtered.png", WorkspaceArea.Working);
        WorkspaceFileRef exactOutput = WorkspaceFileRef.Create("Sessions/probe/Working/exact.png", WorkspaceArea.Working);
        Directory.CreateDirectory(Path.GetDirectoryName(workspace.ResolveAbsolute(input))!);
        File.WriteAllBytes(workspace.ResolveAbsolute(input), originalBytes);
        DeterministicAlphaTrimProcessor processor = new(workspace);
        Stopwatch elapsed = Stopwatch.StartNew();
        var filtered = await processor.TrimAsync(new TrimRequest(input, filteredOutput, TrimMargin.Tight), CancellationToken.None);
        filtered.IsSuccess.ShouldBeTrue(filtered.IsFailure ? filtered.Failure.TechnicalDetail : "");
        Console.WriteLine($"Filtered in {elapsed.Elapsed.TotalMilliseconds:0} ms: {filtered.Value.ContentBounds}");
        filtered.Value.ContentBounds.ShouldBe(expected);

        var exact = await processor.TrimAsync(new TrimRequest(input, exactOutput, TrimMargin.Tight, false), CancellationToken.None);
        exact.IsSuccess.ShouldBeTrue(exact.IsFailure ? exact.Failure.TechnicalDetail : "");
        Console.WriteLine($"Exact bounds: {exact.Value.ContentBounds}");
        (filtered.Value.ResultWidth < exact.Value.ResultWidth || filtered.Value.ResultHeight < exact.Value.ResultHeight).ShouldBeTrue();

        byte[] sourcePixels = SyntheticImages.ReadBgra(workspace.ResolveAbsolute(input), out int sourceWidth, out _);
        byte[] croppedPixels = SyntheticImages.ReadBgra(workspace.ResolveAbsolute(filteredOutput), out int width, out int height);
        for (int y = 0; y < height; y++)
            croppedPixels.AsSpan(y * width * 4, width * 4).SequenceEqual(sourcePixels.AsSpan(
                ((y + expected.Top) * sourceWidth + expected.Left) * 4, width * 4)).ShouldBeTrue($"row {y}");
        SHA256.HashData(File.ReadAllBytes(original)).ShouldBe(SHA256.HashData(originalBytes));
        Console.WriteLine("All retained BGRA samples and original file bytes are unchanged.");
        Console.WriteLine("Filtered output: " + workspace.ResolveAbsolute(filteredOutput));
        Console.WriteLine("Exact output: " + workspace.ResolveAbsolute(exactOutput));

        // A pessimistic production-sized case: an alpha-1 sheet touching one stronger pixel.
        // It must be retained, and the traversal must stay bounded and cancellable.
        byte[] large = new byte[5000 * 5000];
        Array.Fill(large, (byte)1);
        large[2500 * 5000 + 2500] = 255;
        elapsed.Restart();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        NoiseTolerantAlphaBounds.Compute(5000, 5000, large).ShouldBe(TrimBounds.Canvas(5000, 5000));
        Console.WriteLine($"25M-pixel connected faint sheet: {elapsed.Elapsed.TotalMilliseconds:0} ms; " +
            $"{(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / 1024 / 1024} MiB allocated.");
    }
}
