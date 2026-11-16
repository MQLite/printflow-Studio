using System.IO;
using System.Text.Json.Nodes;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Infrastructure.AiDesign.Authoring;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Unit.AiDesign.Memorial;
using PrintFlow.Workflow.AiDesign.Authoring;

namespace PrintFlow.Tests.Integration.AiDesign.Authoring;

public sealed class AuthoringSchemaUpgradeTests
{
    [Fact]
    public async Task Schema1_keeps_manual_text_and_requires_layout()
    {
        using TempWorkspace root = new(); var store = new FileAiAuthoringDraftStore(root.Root);
        var original = MemorialLayoutTests.Draft() with { IsManuallyEdited = true, NeedsFieldReconcile = true };
        const string manualText = "  手改 ʻĀna\r\n e\u0301 陈 {{姓名}}  ";
        original = original with { FinalDocument = new([new AiTextPart(Guid.NewGuid(), manualText), .. original.FinalDocument.Parts]) };
        var saved = (await store.SaveAsync(original, null, default)).Value;
        string version = Directory.GetFiles(Path.Combine(root.Root, "AiDesign", "Authoring", "Drafts", original.Id.ToString("D")), "revision-*.json").Single();
        var json = JsonNode.Parse(File.ReadAllText(version))!;
        json["value"]!["schemaVersion"] = 1; json["value"]!.AsObject().Remove("layout");
        File.WriteAllText(version, json.ToJsonString()); string before = File.ReadAllText(version);

        var migrated = (await new FileAiAuthoringDraftStore(root.Root).ReadAsync(original.Id, default)).Value;
        migrated.SchemaVersion.ShouldBe(2); migrated.Layout.ShouldBeNull(); migrated.Revision.ShouldBe(saved.Revision);
        migrated.FinalDocument.Parts.ShouldBe(original.FinalDocument.Parts);
        migrated.FinalDocument.Parts.OfType<AiTextPart>().ShouldHaveSingleItem().Text.ShouldBe(manualText);
        migrated.Assets.ShouldBe(original.Assets); migrated.Bindings.ShouldBe(original.Bindings);
        migrated.Confirmations.ShouldBe(original.Confirmations); migrated.Fields.ShouldBe(original.Fields);
        migrated.IsManuallyEdited.ShouldBeTrue(); migrated.NeedsFieldReconcile.ShouldBeTrue();
        File.ReadAllText(version).ShouldBe(before); // migration does not mutate immutable history
        var next = (await store.SaveAsync(migrated, migrated.Revision, default)).Value;
        next.SchemaVersion.ShouldBe(2); next.Revision.ShouldBe(saved.Revision + 1);
        (await store.ReadHistoryAsync(original.Id, default)).Value.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Layout_roundtrips_detaches_collections_and_keeps_revision_CAS()
    {
        using TempWorkspace root = new(); var module = Module(root.Root); var store = new FileAiAuthoringDraftStore(root.Root);
        var draft = (await store.SaveAsync(MemorialLayoutTests.Draft(), null, default)).Value;
        var layout = MemorialLayoutTests.Layout(draft); var images = layout.Images.ToList(); var text = layout.Text.ToList();
        var applying = module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.SetLayout(layout with { Images = images, Text = text }), default);
        images.Clear(); text.Clear();
        var result = await applying; result.IsSuccess.ShouldBeTrue();
        var saved = result.Value; saved.Layout!.ProductTemplateConfirmed.ShouldBeFalse();
        var read = (await store.ReadAsync(saved.Id, default)).Value;
        read.Layout!.Images.ShouldBe(layout.Images); read.Layout.Text.ShouldBe(layout.Text);
        Should.Throw<NotSupportedException>(() => ((IList<MemorialImageLayer>)read.Layout.Images).Clear());
        (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.SetLayout(layout), default)).IsFailure.ShouldBeTrue();
        var confirmed = (await module.ApplyAsync(read.Id, read.Revision, new AiAuthoringEdit.SetLayout(read.Layout with { ProductTemplateConfirmed = true }), default)).Value;
        confirmed.Layout!.ProductTemplateConfirmed.ShouldBeTrue();
    }

    [Fact]
    public async Task Geometry_and_field_changes_revoke_layout_but_preserve_unchanged_text_confirmation()
    {
        using TempWorkspace root = new(); var module = Module(root.Root); var store = new FileAiAuthoringDraftStore(root.Root);
        var draft = MemorialLayoutTests.Draft(); var nameKey = "name";
        draft = draft with { Confirmations = new Dictionary<string, string>(draft.Confirmations) { [nameKey] = AiAuthoringConfirmationRules.ContentHash(draft, nameKey)! } };
        draft = draft with { Layout = MemorialLayoutTests.Layout(draft) };
        draft = (await store.SaveAsync(draft, null, default)).Value;
        var layout = draft.Layout!;
        var changed = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.SetLayout(layout with { Images = [layout.Images[0] with { Bounds = new(31, 30, 20, 20) }] }), default)).Value;
        changed.Layout!.ProductTemplateConfirmed.ShouldBeFalse();
        AiAuthoringConfirmationRules.IsConfirmed(changed, nameKey).ShouldBeTrue();
        changed = (await module.ApplyAsync(changed.Id, changed.Revision, new AiAuthoringEdit.SetLayout(changed.Layout with { ProductTemplateConfirmed = true }), default)).Value;
        changed.Layout!.ProductTemplateConfirmed.ShouldBeTrue();
        var renamed = (await module.ApplyAsync(draft.Id, changed.Revision, new AiAuthoringEdit.SetField("姓名", "different"), default)).Value;
        renamed.Layout!.ProductTemplateConfirmed.ShouldBeFalse();
        AiAuthoringConfirmationRules.IsConfirmed(renamed, nameKey).ShouldBeFalse();
    }

    [Fact]
    public async Task SetLayout_cannot_change_literal_text_or_confirm_invalid_safe_geometry()
    {
        using TempWorkspace root = new(); var store = new FileAiAuthoringDraftStore(root.Root); var module = Module(root.Root);
        var draft = (await store.SaveAsync(MemorialLayoutTests.Draft(), null, default)).Value;
        var layout = MemorialLayoutTests.Layout(draft);
        (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.SetLayout(layout with { Text = [layout.Text[0] with { Text = "rewritten" }] }), default)).IsFailure.ShouldBeTrue();
        var outside = layout with { Images = [layout.Images[0] with { Bounds = new(0, 0, 20, 20) }] };
        var pending = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.SetLayout(outside), default)).Value;
        (await module.ApplyAsync(pending.Id, pending.Revision, new AiAuthoringEdit.SetLayout(pending.Layout! with { ProductTemplateConfirmed = true }), default)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Future_schema_and_layout_smuggled_in_schema1_are_refused()
    {
        using TempWorkspace root = new(); var store = new FileAiAuthoringDraftStore(root.Root); var draft = MemorialLayoutTests.Draft();
        (await store.SaveAsync(draft with { SchemaVersion = 999 }, null, default)).IsFailure.ShouldBeTrue();
        (await store.SaveAsync(draft with { SchemaVersion = 1, Layout = MemorialLayoutTests.Layout(draft) }, null, default)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Initial_placement_cannot_establish_its_own_unauthorized_movement_baseline()
    {
        using TempWorkspace root = new(); var store = new FileAiAuthoringDraftStore(root.Root); var module = Module(root.Root);
        var draft = MemorialLayoutTests.Draft();
        draft = draft with { Preservation = [draft.Preservation[0] with { AllowMove = false, AllowUniformScale = false }] };
        var key = AiAuthoringConfirmationRules.PreservationKey(draft.Assets[0].Id);
        draft = draft with { Confirmations = new Dictionary<string, string> { [key] = AiAuthoringConfirmationRules.ContentHash(draft, key)! } };
        draft = (await store.SaveAsync(draft, null, default)).Value;
        var result = await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.SetLayout(MemorialLayoutTests.Layout(draft)), default);
        result.IsFailure.ShouldBeTrue();
        (await store.ReadAsync(draft.Id, default)).Value.Layout.ShouldBeNull();
    }

    [Fact]
    public async Task Restoring_different_historical_geometry_requires_current_confirmation()
    {
        using TempWorkspace root = new(); var store = new FileAiAuthoringDraftStore(root.Root); var module = Module(root.Root);
        var draft = MemorialLayoutTests.Draft();
        // Geometry-only text placement isolates restore invalidation from A's separately
        // tested historical-attachment verification (this module has no attachment adapter).
        var original = MemorialLayoutTests.Layout(draft) with { Images = [] };
        draft = draft with { Assets = [], Bindings = [], Preservation = [], FinalDocument = new([]), Layout = original };
        draft = (await store.SaveAsync(draft, null, default)).Value;
        var layout = draft.Layout!;
        var changed = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.SetLayout(layout with { Text = [layout.Text[0] with { Bounds = new(31, 20, 26, 5) }] }), default)).Value;
        var restored = (await module.ApplyAsync(changed.Id, changed.Revision, new AiAuthoringEdit.RestoreRevision(draft.Revision), default)).Value;
        restored.Layout!.Text.ShouldBe(layout.Text); restored.Layout.ProductTemplateConfirmed.ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Storage_demotes_invalid_confirmed_snapshots_without_losing_editable_drafts(bool mutateCommittedFlag)
    {
        using TempWorkspace root = new(); var store = new FileAiAuthoringDraftStore(root.Root);
        var draft = MemorialLayoutTests.Draft(); var layout = MemorialLayoutTests.Layout(draft);
        var outside = layout with { Images = [layout.Images[0] with { Bounds = new(0, 0, 20, 20) }] };
        var saved = (await store.SaveAsync(draft with { Layout = outside with { ProductTemplateConfirmed = !mutateCommittedFlag } }, null, default)).Value;
        if (!mutateCommittedFlag) { saved.Layout!.ProductTemplateConfirmed.ShouldBeFalse(); return; }
        string version = Directory.GetFiles(Path.Combine(root.Root, "AiDesign", "Authoring", "Drafts", draft.Id.ToString("D")), "revision-*.json").Single();
        var json = JsonNode.Parse(File.ReadAllText(version))!;
        json["value"]!["layout"]!["productTemplateConfirmed"] = true;
        File.WriteAllText(version, json.ToJsonString()); string before = File.ReadAllText(version);
        var read = (await store.ReadAsync(draft.Id, default)).Value;
        read.Layout!.ProductTemplateConfirmed.ShouldBeFalse(); read.Layout.Images.ShouldBe(outside.Images);
        (await store.ReadHistoryAsync(draft.Id, default)).Value[0].Layout!.ProductTemplateConfirmed.ShouldBeFalse();
        File.ReadAllText(version).ShouldBe(before);
    }

    private static AiAuthoringModule Module(string root) => new(new FileAiAuthoringDraftStore(root), new AiTemplateCatalog(new FileAiTemplateLibraryStore(root)));
}
