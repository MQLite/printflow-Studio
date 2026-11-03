using System.IO;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Infrastructure.AiDesign.Authoring;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.AiDesign.Authoring;
using static PrintFlow.Tests.Integration.AiDesign.Authoring.AuthoringPersistenceTests;

namespace PrintFlow.Tests.Integration.AiDesign.Authoring;

public sealed class TemplateCatalogTests
{
    [Fact]
    public async Task Reopened_catalog_lists_saved_order_even_after_a_personal_template_is_edited()
    {
        using TempWorkspace root = new(); var store = new FileAiTemplateLibraryStore(root.Root); var catalog = new AiTemplateCatalog(store);
        var personal = (await catalog.SaveAsync(null, new("Personal", AiProductKind.TextChange, "verbatim", []), false, default)).Value;
        var library = (await store.ReadAsync(default)).Value;
        string[] order = [personal.Reference.Id, "memorial-text-change-quick", "memorial-badge-quick", "memorial-tshirt-quick"];
        (await catalog.UpdatePreferencesAsync(library.Revision, [], order, personal.Reference.Id, default)).IsSuccess.ShouldBeTrue();
        (await catalog.SaveAsync(personal.Reference, personal.Content with { Name = "Renamed" }, false, default)).IsSuccess.ShouldBeTrue();

        var reopened = (await new AiTemplateCatalog(new FileAiTemplateLibraryStore(root.Root)).ListAsync(default)).Value;
        reopened.Select(x => x.Reference.Id).ShouldBe(order);
    }

    [Fact]
    public async Task Builtin_edit_preserves_original_and_frozen_old_draft()
    {
        using TempWorkspace root = new();
        var catalog = new AiTemplateCatalog(new FileAiTemplateLibraryStore(root.Root));
        var original = (await catalog.ListAsync(default)).Value[0];
        var draft = (await Module(root.Root).CreateAsync(original.Reference, default)).Value;
        var fields = original.Content.Fields.ToList();
        var saving = catalog.SaveAsync(original.Reference, original.Content with { Body = "新版 {{姓名}}", Fields = fields }, false, default);
        fields.Clear();
        var personal = (await saving).Value;

        personal.BuiltinOriginId.ShouldBe("memorial-tshirt-quick");
        personal.Reference.Id.ShouldNotBe(original.Reference.Id);
        personal.Content.Fields.ShouldNotBeEmpty();
        var listed = (await new AiTemplateCatalog(new FileAiTemplateLibraryStore(root.Root)).ListAsync(default)).Value;
        listed.Single(x => x.Reference == original.Reference).Content.Body.ShouldBe(original.Content.Body);
        (await Module(root.Root).ReadAsync(draft.Id, default)).Value.TemplateSnapshot.Body.ShouldBe(original.Content.Body);
        var edited = (await catalog.SaveAsync(personal.Reference, personal.Content with { Name = "Renamed" }, false, default)).Value;
        edited.Reference.Id.ShouldBe(personal.Reference.Id); edited.Reference.Revision.ShouldBe(2);
        (await catalog.SaveAsync(personal.Reference, personal.Content, false, default)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Deleting_template_does_not_erase_saved_prompt()
    {
        using TempWorkspace root = new();
        var catalog = new AiTemplateCatalog(new FileAiTemplateLibraryStore(root.Root));
        var personal = (await catalog.SaveAsync(null, new("普通模板", AiProductKind.TextChange, "精确原文\r\n", []), false, default)).Value;
        var draft = (await Module(root.Root).CreateAsync(personal.Reference, default)).Value;
        draft = (await Module(root.Root).ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.ReplaceDocument(Document("手改客户原文\r\n")), default)).Value;
        (await catalog.DeletePersonalAsync(personal.Reference, default)).IsSuccess.ShouldBeTrue();

        var reopened = (await Module(root.Root).ReadAsync(draft.Id, default)).Value;
        Module(root.Root).Export(reopened).Text.ShouldBe("手改客户原文\r\n");
        reopened.TemplateSnapshot.Body.ShouldBe("精确原文\r\n");
        (await Module(root.Root).CreateAsync(personal.Reference, default)).IsFailure.ShouldBeTrue();
        (await catalog.DeletePersonalAsync(BuiltInAiTemplates.All[0].Reference, default)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Preferences_restore_and_save_as_new_are_durable_without_removing_personal_versions()
    {
        using TempWorkspace root = new(); var store = new FileAiTemplateLibraryStore(root.Root); var catalog = new AiTemplateCatalog(store);
        var original = (await catalog.ListAsync(default)).Value[0];
        var personal = (await catalog.SaveAsync(original.Reference, original.Content with { Name = "Personal" }, false, default)).Value;
        var copy = (await catalog.SaveAsync(personal.Reference, personal.Content with { Name = "Copy" }, true, default)).Value;
        copy.Reference.Id.ShouldNotBe(personal.Reference.Id); copy.Reference.Revision.ShouldBe(1);
        var library = (await store.ReadAsync(default)).Value;
        var order = library.Templates.Select(x => x.Reference.Id).Reverse().ToArray();
        var preferences = (await catalog.UpdatePreferencesAsync(library.Revision, [personal.Reference.Id], order, personal.Reference.Id, default)).Value;
        var reopened = (await new FileAiTemplateLibraryStore(root.Root).ReadAsync(default)).Value;
        reopened.FavoriteIds.ShouldBe(new[] { personal.Reference.Id }); reopened.OrderedIds.ShouldBe(order); reopened.DefaultId.ShouldBe(personal.Reference.Id);
        (await catalog.UpdatePreferencesAsync(library.Revision, [], order, original.Reference.Id, default)).IsFailure.ShouldBeTrue();
        (await catalog.RestoreBuiltinAsync(original.Reference.Id, default)).IsSuccess.ShouldBeTrue();
        (await catalog.ListAsync(default)).Value.ShouldContain(x => x.Reference == personal.Reference);
        (await catalog.DeletePersonalAsync(personal.Reference, default)).IsSuccess.ShouldBeTrue();
        reopened = (await store.ReadAsync(default)).Value;
        reopened.FavoriteIds.ShouldBeEmpty(); reopened.OrderedIds.ShouldNotContain(personal.Reference.Id); reopened.DefaultId.ShouldNotBe(personal.Reference.Id);
        Directory.GetFiles(Path.Combine(root.Root, "AiDesign", "Authoring", "Templates"), "revision-*.json").Length.ShouldBe(6);
        preferences.Revision.ShouldBe(3);
    }

    [Fact]
    public async Task Two_library_store_instances_cannot_overwrite_same_revision()
    {
        using TempWorkspace root = new();
        var a = new FileAiTemplateLibraryStore(root.Root); var b = new FileAiTemplateLibraryStore(root.Root);
        var library = (await a.ReadAsync(default)).Value;
        var saves = await Task.WhenAll(a.SaveAsync(library with { DefaultId = library.Templates[1].Reference.Id }, library.Revision, default),
            b.SaveAsync(library with { DefaultId = library.Templates[2].Reference.Id }, library.Revision, default));
        saves.Count(x => x.IsSuccess).ShouldBe(1);
        (await b.ReadAsync(default)).Value.Revision.ShouldBe(1);
    }

    [Fact]
    public async Task Failed_library_publication_preserves_preferences_and_templates()
    {
        using TempWorkspace root = new(); var store = new FileAiTemplateLibraryStore(root.Root); var catalog = new AiTemplateCatalog(store);
        var library = (await store.ReadAsync(default)).Value;
        string pointer = Path.Combine(root.Root, "AiDesign", "Authoring", "Templates", "current.json");
        using (var block = new FileStream(pointer, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            (await catalog.SaveAsync(null, new("New", AiProductKind.TextChange, "plain text", []), false, default)).IsFailure.ShouldBeTrue();
        }
        var reopened = (await new FileAiTemplateLibraryStore(root.Root).ReadAsync(default)).Value;
        reopened.Revision.ShouldBe(library.Revision); reopened.Templates.Count.ShouldBe(3); reopened.DefaultId.ShouldBe(library.DefaultId);
        Directory.GetFiles(Path.GetDirectoryName(pointer)!, "revision-*.json").Length.ShouldBe(1);
    }

    [Fact]
    public async Task Invalid_template_and_corrupt_library_fail_without_reinitialization()
    {
        using TempWorkspace root = new(); var catalog = new AiTemplateCatalog(new FileAiTemplateLibraryStore(root.Root));
        var original = (await catalog.ListAsync(default)).Value[0];
        (await catalog.SaveAsync(original.Reference, original.Content with { Fields = [original.Content.Fields[0], original.Content.Fields[0]] }, false, default)).IsFailure.ShouldBeTrue();
        (await catalog.SaveAsync(original.Reference, original.Content with
        {
            Fields = original.Content.Fields.Select(x => x.Semantic == AiTemplateSemantic.Name ? x with { DefaultValue = "customer name" } : x).ToArray()
        }, false, default)).IsFailure.ShouldBeTrue();
        string pointer = Path.Combine(root.Root, "AiDesign", "Authoring", "Templates", "current.json"); File.WriteAllText(pointer, "not JSON");
        (await new FileAiTemplateLibraryStore(root.Root).ReadAsync(default)).IsFailure.ShouldBeTrue();
        File.ReadAllText(pointer).ShouldBe("not JSON");
    }
}
