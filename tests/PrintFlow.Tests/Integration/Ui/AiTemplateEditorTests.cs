using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.AiDesign.Authoring;
using PrintFlow.App.Views.AiDesign;
using System.Windows;
using static PrintFlow.Tests.Integration.AiDesign.Authoring.AuthoringPersistenceTests;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class AiTemplateEditorTests
{
    [Fact]
    public async Task Saving_current_instance_preserves_manual_reusable_clause_and_restores_known_customer_variables()
    {
        using var f = new PromptWorkbenchFixture(); using TempWorkspace source = new();
        await f.Vm.OpenAsync(null, default);
        f.Vm.Fields.Single(x => x.Key == "姓名").Value = "CUSTOMER ĀNA";
        f.Vm.Fields.Single(x => x.Key == "出生日期行").Value = "12/06/1039";
        await f.Vm.ComposeCommand.ExecuteAsync(null);
        f.Picker.Pick = () => PromptWorkbenchFixture.Png(source.Root, 2);
        await f.Vm.PickSlotImageCommand.ExecuteAsync("person-photo");
        f.Vm.Document = new(f.Vm.Document.Parts.Append(new AiTextPart(Guid.NewGuid(), "\n新增通用要求：蓝色和金色，不增加白边。")).ToArray());
        await f.Vm.SaveInstanceTemplateCommand.ExecuteAsync(null);
        var editor = f.Vm.TemplateEditor!;
        editor.Body.ShouldContain("新增通用要求：蓝色和金色，不增加白边。");
        editor.Body.ShouldContain("{{姓名}}"); editor.Body.ShouldContain("{{出生日期行}}"); editor.Body.ShouldContain("{{人物参考}}");
        editor.Body.ShouldNotContain("CUSTOMER ĀNA"); editor.Body.ShouldNotContain("12/06/1039"); editor.Body.ShouldNotContain("attachment-01");
        editor.SaveAsNew.ShouldBeTrue(); editor.PreviewConfirmed.ShouldBeFalse();
        editor.Name = "本次另存通用模板"; editor.PreviewConfirmed = true;
        await editor.SaveCommand.ExecuteAsync(null);
        var newDraft = (await f.Module.CreateAsync(editor.SavedTemplate!.Reference, default)).Value;
        Text(newDraft.FinalDocument).ShouldContain("新增通用要求");
        newDraft.Fields.Values.ShouldNotContain("CUSTOMER ĀNA"); newDraft.Assets.ShouldBeEmpty();
    }
    [Fact]
    public async Task Reopening_saved_personal_template_edits_latest_version_without_rebuilding_current_draft()
    {
        using var f = new PromptWorkbenchFixture(); await f.Vm.OpenAsync(null, default);
        f.Vm.Document = Document("本次客户手稿"); await f.Vm.EditTemplateCommand.ExecuteAsync(null);
        var first = f.Vm.TemplateEditor!; first.Name = "个人模板"; first.Body += "\nfirst reusable edit";
        first.PreviewConfirmed = true; await first.SaveCommand.ExecuteAsync(null);
        var personal = first.SavedTemplate.ShouldNotBeNull();
        f.Vm.CloseTemplateCommand.Execute(null);
        await f.Vm.EditTemplateCommand.ExecuteAsync(null);
        var reopened = f.Vm.TemplateEditor!;
        reopened.Body.ShouldContain("first reusable edit");
        reopened.Body += "\nsecond reusable edit"; reopened.PreviewConfirmed = true;
        await reopened.SaveCommand.ExecuteAsync(null);
        var saved = (await f.Catalog.ListAsync(default)).Value.Single(x => x.Reference.Id == personal.Reference.Id);
        saved.Reference.Revision.ShouldBe(2); saved.Content.Body.ShouldContain("second reusable edit");
        Text(f.Vm.Document).ShouldBe("本次客户手稿");
        f.Vm.Draft!.Template.ShouldBe(BuiltInAiTemplates.All[0].Reference);
        // Reopening a draft pinned to a previous personal revision also uses the latest library head.
        var oldDraft = (await f.Module.CreateAsync(saved.Reference, default)).Value;
        await f.Vm.OpenAsync(oldDraft.Id, default); await f.Vm.EditTemplateCommand.ExecuteAsync(null);
        f.Vm.TemplateEditor!.Body += "\nthird"; f.Vm.TemplateEditor.PreviewConfirmed = true;
        await f.Vm.TemplateEditor.SaveCommand.ExecuteAsync(null);
        f.Vm.CloseTemplateCommand.Execute(null); await f.Vm.EditTemplateCommand.ExecuteAsync(null);
        f.Vm.TemplateEditor!.Body.ShouldContain("third");
    }
    [Fact]
    public async Task Template_preferences_apply_to_new_drafts_without_changing_current_prompt()
    {
        using var f = new PromptWorkbenchFixture(); await f.Vm.OpenAsync(null, default);
        f.Vm.Document = Document("当前手稿"); await f.Vm.FlushAsync(default);
        var badge = f.Vm.Templates.Single(x => x.Reference.Id == "memorial-badge-quick");
        await f.Vm.SetDefaultTemplateCommand.ExecuteAsync(badge);
        await f.Vm.ToggleFavoriteCommand.ExecuteAsync(badge);
        await f.Vm.MoveTemplateUpCommand.ExecuteAsync(badge);
        var library = (await f.Catalog.ReadLibraryAsync(default)).Value;
        library.DefaultId.ShouldBe(badge.Reference.Id); library.FavoriteIds.ShouldContain(badge.Reference.Id);
        library.OrderedIds[0].ShouldBe(badge.Reference.Id);
        Text(f.Vm.Document).ShouldBe("当前手稿");
        await f.Vm.OpenAsync(null, default);
        f.Vm.Draft!.Product.ShouldBe(AiProductKind.MemorialBadge);
    }
    [Fact]
    public async Task Template_dialog_renders_variable_preview_and_editable_fields_without_binding_errors()
    {
        using var f = new PromptWorkbenchFixture(); await f.Vm.OpenAsync(null, default);
        await f.Vm.EditTemplateCommand.ExecuteAsync(null);
        WpfRendering.RenderExpectingNoBindingErrors(() => new TemplateEditorView { DataContext = f.Vm.TemplateEditor }, new Size(900, 650));
        if (Environment.GetEnvironmentVariable("PRINTFLOW_A5_CAPTURE") is { Length: > 0 } directory)
            WpfRendering.CapturePng(() => new TemplateEditorView { DataContext = f.Vm.TemplateEditor }, new Size(900, 650), System.IO.Path.Combine(directory, "template-editor.png"));
    }
    [Fact]
    public async Task Template_save_requires_preview_confirmation_and_does_not_change_existing_draft()
    {
        using var f = new PromptWorkbenchFixture(); await f.Vm.OpenAsync(null, default);
        f.Vm.Fields.Single(x => x.Key == "姓名").Value = "客户姓名";
        f.Vm.Fields.Single(x => x.Key == "出生日期行").Value = "12/06/1039";
        f.Vm.Document = Document("客户的手改全文");
        await f.Vm.EditTemplateCommand.ExecuteAsync(null);
        var editor = f.Vm.TemplateEditor!;
        editor.Body.ShouldContain("{{姓名}}"); editor.Body.ShouldNotContain("客户");
        editor.Fields.Where(x => x.Semantic is AiTemplateSemantic.Name or AiTemplateSemantic.BirthDate or AiTemplateSemantic.DeathDate || x.Kind == AiTemplateFieldKind.ImageReference)
            .ShouldAllBe(x => string.IsNullOrEmpty(x.DefaultValue));
        editor.Name = "通用个人模板";
        editor.Body += "\r\n模板新增要求";
        await editor.SaveCommand.ExecuteAsync(null);
        (await f.Catalog.ListAsync(default)).Value.Count.ShouldBe(3);
        editor.PreviewConfirmed = true;
        await editor.SaveCommand.ExecuteAsync(null);
        var saved = (await f.Catalog.ListAsync(default)).Value.Single(x => x.Content.Name == "通用个人模板");
        saved.Reference.Id.ShouldStartWith("personal-");
        saved.Content.Body.ShouldContain("{{姓名}}"); saved.Content.Body.ShouldContain("模板新增要求");
        var draft = (await f.Module.ReadAsync(f.Vm.Draft!.Id, default)).Value;
        Text(draft.FinalDocument).ShouldBe("客户的手改全文");
        draft.Template.ShouldBe(BuiltInAiTemplates.All[0].Reference);
        var next = (await f.Module.CreateAsync(saved.Reference, default)).Value;
        next.Fields.Values.ShouldNotContain("客户姓名"); next.Assets.ShouldBeEmpty();
    }

    [Fact]
    public async Task Changing_template_editor_content_revokes_preview_confirmation()
    {
        using var f = new PromptWorkbenchFixture(); await f.Vm.OpenAsync(null, default);
        await f.Vm.EditTemplateCommand.ExecuteAsync(null);
        var editor = f.Vm.TemplateEditor!; editor.PreviewConfirmed = true;
        editor.Body += "changed";
        editor.PreviewConfirmed.ShouldBeFalse();
        editor.PreviewConfirmed = true; editor.Fields[0].Key = "新姓名变量";
        editor.PreviewConfirmed.ShouldBeFalse();
    }
}
