using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using ImageMagick;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Automation;

public sealed class PhotoshopBackgroundEditorTests
{
    [Fact]
    public async Task Opaque_jpeg_input_opens_as_an_editable_layer_with_its_original_canvas()
    {
        using TempWorkspace temp = new();
        string input = temp.CreateSourceFile("original.jpg", SyntheticImages.Jpeg(6, 5));
        var facts = (await new WicFileInspector().InspectAsync(input, default)).Value;
        string psd = Path.Combine(temp.Root, "Cutout.psd");
        using (var source = File.OpenRead(input))
            PhotoshopBackgroundEditor.WriteWorkingDocument(source, psd, facts, default);

        using var layers = new MagickImageCollection(psd);
        layers.Count.ShouldBe(2);
        layers[1].Label.ShouldBe("Cutout");
        (layers[1].Width, layers[1].Height).ShouldBe((6U, 5U));
    }

    [Fact]
    public async Task Watcher_ignores_the_initial_copy_and_reads_an_atomic_save_at_the_launched_path()
    {
        using TempWorkspace temp = new();
        var workspace = new FileWorkspace(temp.Root);
        var session = workspace.CreateSession(SessionId.From(Guid.NewGuid()), DateTimeOffset.UtcNow).Value;
        var input = WorkspaceFileRef.Create($"{session.RelativePath}/Working/cutout.png", WorkspaceArea.Working);
        string sourcePath = workspace.ResolveAbsolute(input);
        File.WriteAllBytes(sourcePath, SyntheticImages.PngWithAlpha(6, 5, (x, _) => x == 0 ? (byte)0 : (byte)255));
        var facts = (await new WicFileInspector().InspectAsync(sourcePath, default)).Value;
        string executable = temp.CreateSourceFile("Photoshop.exe", []);
        byte[] preset = JsonSerializer.SerializeToUtf8Bytes(new { photoshopContract = new { executablePath = executable } });
        var opened = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var leases = new SqliteWorkstationAutomationLeaseManager(Path.Combine(temp.Root, "lease.db"));
        var editor = new PhotoshopBackgroundEditor(workspace, leases, temp.CreateSourceFile("preset.json", preset),
            Sha256.FromBytes(SHA256.HashData(preset)), launch =>
            {
                launch.FileName.ShouldBe(executable);
                opened.SetResult(launch.ArgumentList.Single());
                return true;
            });
        var target = new PhotoshopBackgroundEditSource(session, input, facts, Guid.NewGuid());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        string psd;
        string preview;
        await using (var watcher = editor.WatchSavesAsync(target, timeout.Token).GetAsyncEnumerator())
        {
            var next = watcher.MoveNextAsync().AsTask();
            psd = await opened.Task.WaitAsync(timeout.Token);
            Path.GetFileName(psd).ShouldBe(target.PsdFileName);
            await Task.Delay(TimeSpan.FromMilliseconds(1250), timeout.Token);
            next.IsCompleted.ShouldBeFalse("opening the initial PSD is not a save");

            using var saved = CompatiblePsd(File.ReadAllBytes(psd));
            byte[] savedBytes = saved.ToArray();
            string replacement = temp.CreateSourceFile("replacement.psd", savedBytes);
            File.Move(replacement, psd, overwrite: true);

            (await next.WaitAsync(timeout.Token)).ShouldBeTrue();
            watcher.Current.IsSuccess.ShouldBeTrue(watcher.Current.IsFailure ? watcher.Current.Failure.ToString() : null);
            var result = watcher.Current.Value;
            result.PsdPath.ShouldBe(psd);
            result.PsdHash.ShouldBe(Sha256.FromBytes(SHA256.HashData(savedBytes)));
            preview = result.PreviewPath;
            File.Exists(preview).ShouldBeTrue();
        }

        File.Exists(preview).ShouldBeFalse();
        File.Exists(psd).ShouldBeTrue();
        (await leases.ObserveAsync(null, default)).Status.ShouldBe(WorkstationAutomationLeaseStatus.Free);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Editable_PSD_and_compatible_saved_preview_preserve_the_canvas_and_transparency(bool indexed)
    {
        using TempWorkspace workspace = new();
        // A 6×5 indexed PNG with two palette entries and tRNS marking the first column transparent.
        byte[] artwork = indexed
            ? Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAYAAAAFCAMAAABRhm3UAAAABlBMVEUAAAAgQICA6zMKAAAAAXRSTlMAQObYZgAAAC5JREFUeAEBIwDc/wAAAQEBAQEAAAEBAQEBAAABAQEBAQAAAQEBAQEAAAEBAQEBAcwAGm1MdzUAAAAASUVORK5CYII=")
            : SyntheticImages.PngWithAlpha(6, 5, (x, _) => x == 0 ? (byte)0 : (byte)255);
        var original = SyntheticImages.DecodePayloadBgra(artwork, out _, out _);
        original[3].ShouldBe((byte)0);
        original[7].ShouldBe((byte)255);
        string png = workspace.CreateSourceFile("cutout.png", artwork);
        var facts = (await new WicFileInspector().InspectAsync(png, default)).Value;
        string psd = Path.Combine(workspace.Root, "Cutout.psd");
        using (var source = File.OpenRead(png))
            PhotoshopBackgroundEditor.WriteWorkingDocument(source, psd, facts, default);
        byte[] bytes = File.ReadAllBytes(psd);
        BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(24)).ShouldBe((ushort)3, "the PSD should be in RGB mode");

        using var layers = new MagickImageCollection(psd);
        layers.Count.ShouldBe(2);
        layers[1].Label.ShouldBe("Cutout");
        (layers[1].Width, layers[1].Height).ShouldBe((6U, 5U));
        layers[1].HasAlpha.ShouldBeTrue();

        using var saved = CompatiblePsd(bytes);
        string output = Path.Combine(workspace.Root, "saved.png");
        var preview = PhotoshopBackgroundEditor.ExtractSavedPreview(saved, output, facts, default);
        preview.IsSuccess.ShouldBeTrue(preview.IsFailure ? preview.Failure.ToString() : null);
        var inspected = (await new WicFileInspector().InspectAsync(output, default)).Value;
        (inspected.PixelWidth, inspected.PixelHeight).ShouldBe((6, 5));
        var pixels = SyntheticImages.DecodePayloadBgra(File.ReadAllBytes(output), out _, out _);
        pixels[3].ShouldBe((byte)0);
        pixels[7].ShouldBe((byte)255);
    }

    // A synthetic Adobe version-info resource marks the generated composite as present.
    private static MemoryStream CompatiblePsd(byte[] bytes)
    {
        int resourceLengthOffset = 30 + BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(26));
        int originalLength = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(resourceLengthOffset));
        byte[] versionResource =
        [
            .. "8BIM"u8, 4, 33, 0, 0, 0, 0, 0, 17,
            0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0,
        ];
        var saved = new MemoryStream();
        saved.Write(bytes.AsSpan(0, resourceLengthOffset));
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, originalLength + versionResource.Length);
        saved.Write(length);
        saved.Write(versionResource);
        saved.Write(bytes.AsSpan(resourceLengthOffset + sizeof(int)));
        saved.Position = 0;
        return saved;
    }
}
