using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.AiDesign.Memorial;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.AiDesign.Memorial;

namespace PrintFlow.Tests.Integration.AiDesign.Memorial;

public sealed class MemorialRenderOwnershipTests
{
    [Fact]
    public async Task Restart_lists_independent_preview_and_material_with_real_pixel_facts()
    {
        using var fixture = new MemorialRenderFixture();
        var draft = await fixture.CreateAsync();
        var preview = await fixture.RenderAsync(draft);
        var material = await fixture.RenderAsync(draft, MemorialRenderPurpose.DesignMaterial);
        var reopened = new FileMemorialRenderStore(fixture.Root);
        var list = (await reopened.ListAsync(draft.Id, default)).Value;

        preview.File.RenderId.ShouldNotBe(material.File.RenderId);
        preview.SourceManifestSha256.ShouldNotBe(material.SourceManifestSha256);
        list.Count.ShouldBe(2);
        list.ShouldContain(preview);
        list.ShouldContain(material);
        (await reopened.ReadAsync(preview.File with { Purpose = material.File.Purpose }, default)).IsFailure.ShouldBeTrue();
        material.Facts.PixelWidth.ShouldBe(591);
        material.Facts.PixelHeight.ShouldBe(591);
        material.Facts.DpiX!.Value.ShouldBe(300, 0.03);
        material.Facts.HasAlpha.ShouldBe(true);
        var displayed = (await new FileMemorialRenderPreview(reopened).LoadAsync(material, default)).Value;
        displayed.SourcePixelWidth.ShouldBe(591);
        displayed.HasTransparency.ShouldBeTrue();
    }

    [Fact]
    public async Task Open_stream_refuses_fabricated_facts_holds_path_and_rejects_later_corruption()
    {
        using var fixture = new MemorialRenderFixture();
        var draft = await fixture.CreateAsync();
        var render = await fixture.RenderAsync(draft);
        string png = Path.Combine(fixture.RenderPath(render), "render.png");

        (await fixture.Renders.OpenVerifiedAsync(render with { Facts = render.Facts with { PixelWidth = 7 } }, default))
            .Failure.Code.ShouldBe(FailureCode.RevisionIntegrityMismatch);
        await using (Stream stream = (await fixture.Renders.OpenVerifiedAsync(render, default)).Value)
        {
            stream.CanWrite.ShouldBeFalse();
            Should.Throw<IOException>(() => File.Move(png, png + ".moved"));
            Should.Throw<IOException>(() => Directory.Move(fixture.RenderPath(render), fixture.RenderPath(render) + ".moved"));
        }
        File.SetAttributes(png, FileAttributes.Normal);
        byte[] broken = File.ReadAllBytes(png);
        broken[^1] ^= 1;
        File.WriteAllBytes(png, broken);

        (await fixture.Renders.ReadAsync(render.File, default)).IsFailure.ShouldBeTrue();
        (await new FileMemorialRenderPreview(fixture.Renders).LoadAsync(render, default)).IsFailure.ShouldBeTrue();
        (await fixture.Renders.ListAsync(draft.Id, default)).Value.ShouldBeEmpty();
        File.ReadAllBytes(png).ShouldBe(broken);
    }

    [Fact]
    public async Task Invalid_png_and_cancellation_during_encoding_leave_no_ready_result_or_unknown_deletion()
    {
        using var fixture = new MemorialRenderFixture();
        var draft = await fixture.CreateAsync();
        var plan = MemorialCompositionModule.CreatePlan(draft, MemorialRenderPurpose.Preview).Value;
        var prior = await fixture.RenderAsync(draft);
        string unknown = Path.Combine(fixture.DraftRenderPath(draft.Id), "foreign.txt");
        File.WriteAllText(unknown, "do not touch");
        using var cancellation = new CancellationTokenSource();

        var invalid = fixture.Renders.Publish(plan, output => output.Write("bad png"u8), default);
        invalid.IsFailure.ShouldBeTrue();
        var cancelled = fixture.Renders.Publish(plan, output =>
        {
            output.Write(MemorialRenderFixture.Png(4, (_, _) => Colors.Blue));
            cancellation.Cancel();
        }, cancellation.Token);

        cancelled.Failure.Code.ShouldBe(FailureCode.Cancelled);
        (await fixture.Renders.ListAsync(draft.Id, default)).Value.ShouldBe([prior]);
        Directory.EnumerateFiles(fixture.DraftRenderPath(draft.Id), "*.tmp", SearchOption.AllDirectories).ShouldBeEmpty();
        File.ReadAllText(unknown).ShouldBe("do not touch");
    }

    [Fact]
    public async Task Byte_limit_and_identical_replacement_cannot_create_a_ready_render()
    {
        using var fixture = new MemorialRenderFixture();
        var draft = await fixture.CreateAsync();
        var plan = MemorialCompositionModule.CreatePlan(draft, MemorialRenderPurpose.Preview).Value;
        var tooLarge = fixture.Renders.Publish(plan, output => output.SetLength(40L * 1024 * 1024 + 1), default);
        tooLarge.Failure.TechnicalDetail.ShouldContain("PNG byte limit");
        (await fixture.Renders.ListAsync(draft.Id, default)).Value.ShouldBeEmpty();

        var rendered = await fixture.RenderAsync(draft);
        string png = Path.Combine(fixture.RenderPath(rendered), "render.png");
        File.SetAttributes(png, FileAttributes.Normal);
        File.Move(png, png + ".previous");
        File.Copy(png + ".previous", png);
        File.ReadAllBytes(png).ShouldBe(File.ReadAllBytes(png + ".previous"));
        (await fixture.Renders.OpenVerifiedAsync(rendered, default)).Failure.Code.ShouldBe(FailureCode.RevisionIntegrityMismatch);
        File.Exists(png + ".previous").ShouldBeTrue();
    }

    [Fact]
    public async Task Reparse_render_root_never_publishes_into_target()
    {
        using var fixture = new MemorialRenderFixture();
        using var foreign = new TempWorkspace();
        var draft = await fixture.CreateAsync();
        string link = Directory.GetParent(fixture.DraftRenderPath(draft.Id))!.FullName;
        string sentinel = foreign.CreateSourceFile("keep.txt", "keep"u8.ToArray());
        var originalEntries = Directory.EnumerateFileSystemEntries(foreign.Root).Order().ToArray();
        var info = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (string arg in new[] { "/d", "/c", "mklink", "/J", link, foreign.Root }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        await process.WaitForExitAsync();
        process.ExitCode.ShouldBe(0, await process.StandardOutput.ReadToEndAsync());
        try
        {
            (await fixture.Compose.RenderAsync(draft.Id, draft.Revision, MemorialRenderPurpose.Preview, default)).IsFailure.ShouldBeTrue();
            (await fixture.Renders.ListAsync(draft.Id, default)).IsFailure.ShouldBeTrue();
            Directory.EnumerateFileSystemEntries(foreign.Root).Order().ShouldBe(originalEntries);
            File.ReadAllText(sentinel).ShouldBe("keep");
        }
        finally { Directory.Delete(link); }
    }
}
