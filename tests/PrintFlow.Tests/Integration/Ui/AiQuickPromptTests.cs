using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.App.Navigation;
using PrintFlow.App.ViewModels.AiDesign;
using PrintFlow.App.Views.AiDesign;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Infrastructure.AiDesign;
using PrintFlow.Infrastructure.AiDesign.Authoring;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.AiDesign.Authoring;
using static PrintFlow.Tests.Integration.AiDesign.Authoring.AuthoringPersistenceTests;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class AiQuickPromptTests
{
    [Fact]
    public async Task Stale_and_manual_notices_survive_save_copy_and_reopening()
    {
        using var f = new PromptWorkbenchFixture(); await f.Vm.OpenAsync(null, default);
        f.Vm.Fields.Single(x => x.Key == "姓名").Value = "First";
        await f.Vm.ComposeCommand.ExecuteAsync(null);
        f.Vm.PromptNotice.ShouldBeNullOrEmpty();
        f.Vm.Fields.Single(x => x.Key == "姓名").Value = "Second";
        f.Vm.PromptNotice.ShouldNotBeNullOrEmpty(); await f.Vm.CopyCommand.ExecuteAsync(null);
        f.Clipboard.Text.ShouldNotBeNull().ShouldContain("First");
        f.Vm.PromptNotice.ShouldNotBeNullOrEmpty();
        await f.Vm.OpenAsync(f.Vm.Draft!.Id, default); f.Vm.PromptNotice.ShouldNotBeNullOrEmpty();
        f.Vm.Document = Document("手改保留"); await f.Vm.FlushAsync(default);
        f.Vm.PromptNotice.ShouldNotBeNullOrEmpty();
        await f.Vm.OpenAsync(f.Vm.Draft.Id, default); f.Vm.PromptNotice.ShouldNotBeNullOrEmpty();
    }
    [Fact]
    public async Task History_restore_refuses_changed_image_bytes_and_keeps_current_version()
    {
        using var f = new PromptWorkbenchFixture(); using TempWorkspace source = new();
        f.Picker.Pick = () => PromptWorkbenchFixture.Png(source.Root, 2);
        await f.Vm.OpenAsync(null, default);
        await f.Vm.PickSlotImageCommand.ExecuteAsync("person-photo");
        var historical = f.Vm.Draft!; var asset = historical.Assets.Single();
        await f.Vm.RemovePhotoCommand.ExecuteAsync(f.Vm.Photos.Single(x => x.SlotId == "person-photo"));
        var currentRevision = f.Vm.Draft!.Revision;
        string snapshot = Path.Combine(f.Root.Root, "AiDesign", "Requests", historical.Id.ToString("D"), asset.Snapshot.RelativePath);
        File.SetAttributes(snapshot, FileAttributes.Normal);
        File.WriteAllBytes(snapshot, [1, 2, 3]);
        await f.Vm.RestoreHistoryAsync(historical.Revision, default);
        f.Vm.Draft.Revision.ShouldBe(currentRevision); f.Vm.Error.ShouldNotBeNullOrEmpty();
        (await f.Module.ReadAsync(historical.Id, default)).Value.Bindings.ShouldBeEmpty();
    }

    [Fact]
    public async Task Reopening_same_draft_or_switching_drafts_during_picker_rejects_the_late_photo()
    {
        using var f = new PromptWorkbenchFixture(); using TempWorkspace source = new();
        string path = PromptWorkbenchFixture.Png(source.Root, 2);
        await f.Vm.OpenAsync(null, default); var firstId = f.Vm.Draft!.Id;
        f.Picker.Pick = () => { f.Vm.OpenAsync(firstId, default).GetAwaiter().GetResult(); return path; };
        await f.Vm.PickSlotImageCommand.ExecuteAsync("person-photo");
        f.Vm.Draft!.Assets.ShouldBeEmpty();
        f.Picker.Pick = () => { f.Vm.OpenAsync(null, default).GetAwaiter().GetResult(); return path; };
        await f.Vm.PickSlotImageCommand.ExecuteAsync("person-photo");
        f.Vm.Draft.Id.ShouldNotBe(firstId); f.Vm.Draft.Assets.ShouldBeEmpty();
        (await f.Module.ReadAsync(firstId, default)).Value.Assets.ShouldBeEmpty();
    }
    [Fact]
    public async Task Same_named_images_bind_distinct_slots_and_preserve_manual_document()
    {
        using var f = new PromptWorkbenchFixture(); using TempWorkspace a = new(), b = new();
        string first = PromptWorkbenchFixture.Png(a.Root, 2), second = PromptWorkbenchFixture.Png(b.Root, 3);
        await f.Vm.OpenAsync(null, default);
        var original = f.Vm.Document;
        f.Vm.Document = new(original.Parts.Append(new AiTextPart(Guid.NewGuid(), "  手改\r\ne\u0301  ")).ToArray());
        var manualParts = f.Vm.Document.Parts.ToArray();
        f.Picker.Pick = () => first; await f.Vm.PickSlotImageCommand.ExecuteAsync("person-photo");
        f.Vm.Draft!.Assets.Count.ShouldBe(1, f.Vm.Error);
        f.Picker.Pick = () => second; await f.Vm.PickSlotImageCommand.ExecuteAsync("layout-reference");
        f.Vm.Document.Parts.ShouldBe(manualParts);
        await f.Vm.CopyCommand.ExecuteAsync(null);
        f.Clipboard.Text.ShouldNotBeNull().ShouldContain("人物参考：attachment-01.png");
        f.Clipboard.Text.ShouldNotBeNull().ShouldContain("版式参考：attachment-02.png");
        f.Vm.AttachmentManifest.Select(x => x.Id).Distinct().Count().ShouldBe(2);
        f.Vm.Photos.ShouldAllBe(x => x.Preview != null);
        f.Clipboard.Text.ShouldNotBeNull().ShouldNotContain(a.Root);
    }

    [Theory]
    [InlineData(1000, 700)]
    [InlineData(1920, 1080)]
    public async Task Quick_page_binds_real_editor_and_keeps_primary_controls_within_viewport(int width, int height)
    {
        using var f = new PromptWorkbenchFixture(); await f.Vm.OpenAsync(null, default);
        var result = WpfRendering.Render(() => new AiPromptWorkbenchView { DataContext = f.Vm }, new Size(width, height), tree =>
        {
            var editor = tree.OfType<PrintFlow.App.Controls.AiPromptEditor>().Single();
            var box = (RichTextBox)editor.FindName("Editor");
            var paragraph = (Paragraph)box.Document.Blocks.FirstBlock;
            paragraph.Inlines.Add(new Run("实际编辑 e\u0301\r\n"));
            Text(f.Vm.Document).ShouldEndWith("实际编辑 e\u0301\r\n");
            var copy = tree.OfType<Button>().Single(x => System.Windows.Automation.AutomationProperties.GetAutomationId(x) == "AiPrompt.Copy");
            var position = copy.TransformToAncestor(tree.Root).Transform(new Point());
            return (copy.IsEnabled, position.X >= 0 && position.Y >= 0 && position.X + copy.ActualWidth <= width && position.Y + copy.ActualHeight <= height,
                tree.OfType<Button>().Count(x => x.Command == f.Vm.PickSlotImageCommand));
        });
        result.BindingErrors.ShouldBeEmpty(); result.Facts.Item1.ShouldBeTrue(); result.Facts.Item2.ShouldBeTrue();
        result.Facts.Item3.ShouldBe(2);
        if (Environment.GetEnvironmentVariable("PRINTFLOW_A5_CAPTURE") is { Length: > 0 } directory)
            WpfRendering.CapturePng(() => new AiPromptWorkbenchView { DataContext = f.Vm }, new Size(width, height), Path.Combine(directory, $"quick-{width}x{height}.png"));
        await f.Vm.CopyCommand.ExecuteAsync(null);
        f.Clipboard.Text.ShouldNotBeNull().ShouldEndWith("实际编辑 e\u0301\r\n");
    }
    [Fact]
    public async Task Manual_document_survives_field_edits_save_copy_and_reopen()
    {
        using var f = new PromptWorkbenchFixture(); await f.Vm.OpenAsync(null, default);
        f.Vm.Fields.Single(x => x.Key == "姓名").Value = "Āna";
        await f.Vm.ComposeCommand.ExecuteAsync(null);
        Text(f.Vm.Document).ShouldContain("Āna");
        const string literal = "  ʻĀna\r\n陈 e\u0301 {{姓名}} 图3  \nlast\r";
        f.Vm.Document = Document(literal);
        f.Vm.Fields.Single(x => x.Key == "姓名").Value = "另一个姓名";
        await f.Vm.CopyCommand.ExecuteAsync(null);
        f.Clipboard.Text.ShouldBe(literal);
        var id = f.Vm.Draft!.Id;
        await f.Vm.OpenAsync(id, default);
        Text(f.Vm.Document).ShouldBe(literal);
        f.Vm.Draft!.Fields["姓名"].ShouldBe("另一个姓名");
        f.Vm.Draft.Mode.ShouldBe(AiAuthoringMode.Quick);
    }

    [Fact]
    public async Task Missing_photos_can_be_copied_with_manifest_and_explicit_placeholders()
    {
        using var f = new PromptWorkbenchFixture(); await f.Vm.OpenAsync(null, default);
        await f.Vm.CopyCommand.ExecuteAsync(null);
        f.Clipboard.Text.ShouldNotBeNull().ShouldContain("待选择图片");
        f.Vm.AttachmentManifest.ShouldBeEmpty();
        f.Vm.Issues.ShouldContain(x => x.Key == "missing-image");
    }

    [Fact]
    public async Task Rebuild_requires_choice_and_preserves_recoverable_manual_version()
    {
        using var f = new PromptWorkbenchFixture(); await f.Vm.OpenAsync(null, default);
        f.Vm.Document = Document("手改旧稿\r\n");
        await f.Vm.ComposeCommand.ExecuteAsync(null);
        f.Vm.IsChangePending.ShouldBeTrue(); Text(f.Vm.Document).ShouldBe("手改旧稿\r\n");
        f.Vm.CancelChangeCommand.Execute(null);
        Text(f.Vm.Document).ShouldBe("手改旧稿\r\n");
        await f.Vm.ComposeCommand.ExecuteAsync(null); await f.Vm.ConfirmChangeCommand.ExecuteAsync(null);
        Text(f.Vm.Document).ShouldNotBe("手改旧稿\r\n");
        await f.Vm.RefreshHistoryAsync(default);
        var old = f.Vm.History.Single(x => Text(x.FinalDocument) == "手改旧稿\r\n");
        await f.Vm.RestoreHistoryAsync(old.Revision, default);
        Text(f.Vm.Document).ShouldBe("手改旧稿\r\n");
        f.Vm.Draft!.Revision.ShouldBeGreaterThan(old.Revision);
    }

    [Fact]
    public async Task Save_failure_blocks_template_change_and_copy_without_losing_editor_content()
    {
        using var f = new PromptWorkbenchFixture(); await f.Vm.OpenAsync(null, default);
        var original = f.Vm.Draft!; f.Vm.Document = Document("未保存原文");
        using (var block = new FileStream(Path.Combine(DraftDirectory(f.Root.Root, original.Id), "current.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await f.Vm.SelectTemplateAsync(BuiltInAiTemplates.All[1].Reference, true, default);
            await f.Vm.CopyCommand.ExecuteAsync(null);
            f.Vm.Draft!.Template.ShouldBe(original.Template); f.Clipboard.Text.ShouldBeNull();
            Text(f.Vm.Document).ShouldBe("未保存原文"); f.Vm.HasUnsavedChanges.ShouldBeTrue();
            f.Vm.Error.ShouldNotBeNullOrEmpty();
        }
        (await f.Vm.FlushAsync(default)).IsSuccess.ShouldBeTrue();
        Text((await f.Module.ReadAsync(original.Id, default)).Value.FinalDocument).ShouldBe("未保存原文");
    }

    [Fact]
    public async Task Cancelled_and_late_picker_results_do_not_bind_to_inactive_or_changed_page()
    {
        using var f = new PromptWorkbenchFixture(); await f.Vm.OpenAsync(null, default);
        await f.Vm.PickSlotImageCommand.ExecuteAsync("person-photo");
        f.Vm.Draft!.Assets.ShouldBeEmpty();
        f.Picker.Pick = () => { f.Vm.Deactivate(); return "late.png"; };
        await f.Vm.PickSlotImageCommand.ExecuteAsync("person-photo");
        (await f.Module.ReadAsync(f.Vm.Draft.Id, default)).Value.Assets.ShouldBeEmpty();
        await f.Vm.OpenAsync(f.Vm.Draft.Id, default);
        f.Picker.Pick = () => { f.Vm.Fields[0].Value = "changed while choosing"; return "late.png"; };
        await f.Vm.PickSlotImageCommand.ExecuteAsync("person-photo");
        f.Vm.Draft.Assets.ShouldBeEmpty();
    }

    [Fact]
    public async Task External_revision_change_while_picker_is_open_is_refused()
    {
        using var f = new PromptWorkbenchFixture(); await f.Vm.OpenAsync(null, default);
        var draft = f.Vm.Draft!;
        f.Picker.Pick = () =>
        {
            f.Module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.SetField("姓名", "external"), default).GetAwaiter().GetResult();
            return "late.png";
        };
        await f.Vm.PickSlotImageCommand.ExecuteAsync("person-photo");
        (await f.Module.ReadAsync(draft.Id, default)).Value.Assets.ShouldBeEmpty();
    }
}

internal sealed class PromptWorkbenchFixture : IDisposable
{
    public TempWorkspace Root { get; } = new();
    public ControlledPromptPicker Picker { get; } = new();
    public PromptClipboard Clipboard { get; } = new();
    public AiTemplateCatalog Catalog { get; }
    public AiAuthoringModule Module { get; }
    public AiPromptWorkbenchViewModel Vm { get; }
    public PromptWorkbenchFixture()
    {
        Catalog = new(new FileAiTemplateLibraryStore(Root.Root));
        Module = new(new FileAiAuthoringDraftStore(Root.Root), Catalog, new FileAiDesignAttachments(Root.Root));
        Vm = new(Module, Catalog, new FileAiAuthoringAssetPreview(Root.Root), Picker, Clipboard);
    }
    public void Dispose() => Root.Dispose();
    public static string Png(string root, int width)
    {
        string path = Path.Combine(root, "same.png");
        WpfRendering.OnStaThread(() =>
        {
            var pixels = Enumerable.Repeat((byte)128, width * 2 * 4).ToArray();
            var bitmap = BitmapSource.Create(width, 2, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(path); encoder.Save(output);
        });
        return path;
    }
}
internal sealed class ControlledPromptPicker : IFilePicker
{
    public Func<string?> Pick { get; set; } = () => null;
    public string? PickSingleFile(string dialogTitle, string filter) => Pick();
}
internal sealed class PromptClipboard : IAiPromptClipboard
{
    public string? Text { get; private set; }
    public void SetText(string text) => Text = text;
}
