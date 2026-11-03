using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Infrastructure.AiDesign;
using PrintFlow.Infrastructure.AiDesign.Authoring;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.AiDesign;
using PrintFlow.Workflow.AiDesign.Authoring;
using static PrintFlow.Tests.Integration.AiDesign.Authoring.AuthoringPersistenceTests;

namespace PrintFlow.Tests.Integration.AiDesign.Authoring;

public sealed class AuthoringReferenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reactivating_old_bindings_cannot_bypass_active_quota(bool recompose)
    {
        using TempWorkspace root = new(); using TempWorkspace source = new(); var module = WithImages(root.Root);
        var draft = await EightActive(module, root.Root, Png(source.Root, "one.png")); var original = draft.FinalDocument;
        Guid first = original.Parts.OfType<AiImagePart>().Single(x => x.SlotId == "photo-0").PartId;
        draft = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.RemoveReference(first), default)).Value;
        draft = await Bind(module, draft, "photo-8", Png(source.Root, "two.png"));
        AiAuthoringEdit edit = recompose ? new AiAuthoringEdit.Recompose(true) : new AiAuthoringEdit.ReplaceDocument(original);

        (await module.ApplyAsync(draft.Id, draft.Revision, edit, default)).IsFailure.ShouldBeTrue();
        var reopened = (await module.ReadAsync(draft.Id, default)).Value;
        reopened.Revision.ShouldBe(draft.Revision); module.Export(reopened).RequiredAttachments.Count.ShouldBe(8);
    }

    [Fact]
    public async Task Inactive_slot_selection_and_explicit_reuse_do_not_consume_active_quota()
    {
        using TempWorkspace root = new(); using TempWorkspace source = new(); var module = WithImages(root.Root);
        string photo = Png(source.Root, "one.png"); var draft = await EightActive(module, root.Root, photo);
        Guid unused = draft.FinalDocument.Parts.OfType<AiImagePart>().Single(x => x.SlotId == "photo-8").PartId;
        draft = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.RemoveReference(unused), default)).Value;
        draft = await Bind(module, draft, "photo-8", photo); var inactive = draft.Assets.Single(x => x.Order == 9);
        draft = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.RemoveImage(inactive.Id), default)).Value;
        var reused = await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.BindExistingImage("photo-8", inactive.Id), default);

        reused.IsSuccess.ShouldBeTrue(); module.Export(reused.Value).RequiredAttachments.Count.ShouldBe(8);
        reused.Value.Assets.Count.ShouldBe(9);
    }

    [Fact]
    public async Task Checked_source_handle_prevents_same_length_substitution_before_copy()
    {
        using TempWorkspace root = new(); using TempWorkspace source = new();
        string first = Png(source.Root, "first.png"), second = Png(source.Root, "second.png");
        byte[] before = File.ReadAllBytes(second), substitution = before.ToArray(); substitution[^1] ^= 1;
        bool attempted = false, blocked = false; Guid owner = Guid.NewGuid();
        var files = new FileAiDesignAttachments(root.Root, asset =>
        {
            if (asset.Order != 1) return;
            attempted = true;
            try { File.WriteAllBytes(second, substitution); } catch (IOException) { blocked = true; }
        });
        var result = await files.SnapshotAsync(owner, [new(first, "one"), new(second, "two")], [], default);
        result.IsSuccess.ShouldBeTrue(); attempted.ShouldBeTrue(); blocked.ShouldBeTrue();
        var opened = await new VerifiedAiAttachmentReader(root.Root).OpenAsync(owner, result.Value[1], default);
        await using var lease = opened.Value; using var copied = new MemoryStream(); await lease.Stream.CopyToAsync(copied);
        copied.ToArray().ShouldBe(before); File.ReadAllBytes(second).ShouldBe(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Renamed_nonimage_container_cannot_be_bound_as_png(bool bitmap)
    {
        using TempWorkspace root = new(); using TempWorkspace source = new(); var module = WithImages(root.Root);
        string path = bitmap ? BmpDisguisedAsPng(source.Root) : source.CreateSourceFile("person.png", System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\nnot a photo"));
        var draft = await Create(module);

        (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.BindImage("person-photo", new(path, "person")), default)).IsFailure.ShouldBeTrue();
        (await module.ReadAsync(draft.Id, default)).Value.Revision.ShouldBe(draft.Revision);
        module.Export((await module.ReadAsync(draft.Id, default)).Value).RequiredAttachments.ShouldBeEmpty();
    }

    [Fact]
    public async Task Preview_rejects_disguised_bitmap_from_general_attachment_store()
    {
        using TempWorkspace root = new(); using TempWorkspace source = new(); Guid owner = Guid.NewGuid();
        var asset = (await new FileAiDesignAttachments(root.Root).SnapshotAsync(owner, [new(BmpDisguisedAsPng(source.Root), "general reference")], [], default)).Value.Single();

        (await new FileAiAuthoringAssetPreview(root.Root).LoadAsync(owner, asset, default)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Replacing_person_image_preserves_manual_text()
    {
        using TempWorkspace root = new(); using TempWorkspace source = new(); var module = WithImages(root.Root);
        var draft = await Create(module);
        draft = await Bind(module, draft, "person-photo", Png(source.Root, "one.png"));
        var imagePart = draft.FinalDocument.Parts.OfType<AiImagePart>().First();
        var document = new AiPromptDocument([new AiTextPart(Guid.NewGuid(), "  ʻĀna\r\n陈 {{姓名}}  "), imagePart, new AiTextPart(Guid.NewGuid(), " 图3只是原文 ")]);
        draft = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.ReplaceDocument(document), default)).Value;
        var beforeParts = draft.FinalDocument.Parts.ToArray(); Guid oldAsset = draft.Bindings.Single().AssetId;

        var after = await Bind(module, draft, "person-photo", Png(source.Root, "two.png"));

        after.FinalDocument.Parts.ShouldBe(beforeParts);
        after.Bindings.Single().AssetId.ShouldNotBe(oldAsset);
        after.Assets.ShouldContain(x => x.Id == oldAsset);
        var export = module.Export(after);
        export.Text.ShouldBe("  ʻĀna\r\n陈 {{姓名}}  人物参考：attachment-02.png 图3只是原文 ");
        export.RequiredAttachments.ShouldHaveSingleItem().UploadName.ShouldBe("attachment-02.png");
    }

    [Fact]
    public async Task Same_names_and_reordering_do_not_rebind_slots()
    {
        using TempWorkspace root = new(); using TempWorkspace first = new(); using TempWorkspace second = new(); var module = WithImages(root.Root);
        var draft = await Bind(module, await Create(module), "person-photo", Png(first.Root, "same.png", 2, 2));
        draft = await Bind(module, draft, "layout-reference", Png(second.Root, "same.png", 3, 2));
        Guid person = draft.Bindings.Single(x => x.SlotId == "person-photo").AssetId;
        var reversed = new AiPromptDocument(draft.FinalDocument.Parts.Reverse().ToArray());
        draft = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.ReplaceDocument(reversed), default)).Value;
        var export = module.Export(draft with { Assets = draft.Assets.Reverse().ToArray() });

        draft.Bindings.Single(x => x.SlotId == "person-photo").AssetId.ShouldBe(person);
        export.Text.ShouldContain("人物参考：attachment-01.png"); export.Text.ShouldContain("版式参考：attachment-02.png");
        export.Text.ShouldNotContain(root.Root); export.Text.ShouldNotContain(first.Root); export.Text.ShouldNotContain(second.Root);
        export.RequiredAttachments.Select(x => x.Id).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task Removing_reference_does_not_upload_unused_asset()
    {
        using TempWorkspace root = new(); using TempWorkspace source = new(); var module = WithImages(root.Root);
        var draft = await Bind(module, await Create(module), "person-photo", Png(source.Root, "person.png"));
        var asset = draft.Assets.Single(); Guid part = draft.FinalDocument.Parts.OfType<AiImagePart>().Single(x => x.SlotId == "person-photo").PartId;
        draft = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.RemoveReference(part), default)).Value;

        module.Export(draft).RequiredAttachments.ShouldBeEmpty(); draft.Assets.ShouldContain(asset);
        (await new FileAiDesignAttachments(root.Root).VerifyAsync(draft.Id, [asset], default)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Changed_original_cannot_change_snapshot()
    {
        using TempWorkspace root = new(); using TempWorkspace source = new(); var module = WithImages(root.Root);
        string original = Png(source.Root, "person.png"); byte[] before = File.ReadAllBytes(original);
        var draft = await Bind(module, await Create(module), "person-photo", original); File.WriteAllBytes(original, [9, 8, 7]);
        var opened = await new VerifiedAiAttachmentReader(root.Root).OpenAsync(draft.Id, draft.Assets.Single(), default);
        opened.IsSuccess.ShouldBeTrue(); await using var lease = opened.Value;
        using var copy = new MemoryStream(); await lease.Stream.CopyToAsync(copy);
        copy.ToArray().ShouldBe(before);
    }

    [Fact]
    public async Task One_asset_can_fill_two_roles_once()
    {
        using TempWorkspace root = new(); using TempWorkspace source = new(); var module = WithImages(root.Root);
        var draft = await Bind(module, await Create(module), "person-photo", Png(source.Root, "one.png")); var asset = draft.Assets.Single();
        draft = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.BindExistingImage("layout-reference", asset.Id), default)).Value;
        var export = module.Export(draft);
        export.RequiredAttachments.ShouldHaveSingleItem().Id.ShouldBe(asset.Id);
        export.Text.ShouldContain("人物参考：attachment-01.png"); export.Text.ShouldContain("版式参考：attachment-01.png");
        export.RequiredAttachments[0].Purpose.ShouldContain("人物参考"); export.RequiredAttachments[0].Purpose.ShouldContain("版式参考");
    }

    [Fact]
    public async Task Inserted_decor_reference_keeps_explicit_role_after_roundtrip()
    {
        using TempWorkspace root = new(); using TempWorkspace source = new(); var module = WithImages(root.Root);
        var draft = await Create(module); Guid part = Guid.NewGuid(); var slot = new AiReferenceSlot("original-decor", "原样花框", true);
        draft = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.InsertImageReference(part, slot, null), default)).Value;
        draft = await Bind(module, draft, slot.SlotId, Png(source.Root, "decor.png"));
        draft = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.SwitchMode(AiAuthoringMode.Detailed), default)).Value;
        draft = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.SwitchMode(AiAuthoringMode.Quick), default)).Value;
        var reopened = (await WithImages(root.Root).ReadAsync(draft.Id, default)).Value;
        reopened.Slots.ShouldContain(slot); reopened.FinalDocument.Parts.ShouldContain(new AiImagePart(part, slot.SlotId));
        module.Export(reopened).Text.ShouldContain("原样花框：attachment-01.png");
        (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.InsertImageReference(Guid.NewGuid(), slot, null), default)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Removed_image_becomes_pending_without_deleting_history_or_guessing_typed_image_numbers()
    {
        using TempWorkspace root = new(); using TempWorkspace source = new(); var module = WithImages(root.Root);
        var draft = await Bind(module, await Create(module), "person-photo", Png(source.Root, "one.png")); var asset = draft.Assets.Single();
        draft = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.RemoveImage(asset.Id), default)).Value;
        var export = module.Export(draft);
        export.RequiredAttachments.ShouldBeEmpty(); export.Text.ShouldContain("人物参考：待选择图片"); export.Issues.ShouldContain(x => x.SlotId == "person-photo");
        draft.Assets.ShouldContain(asset);
        (await new FileAiDesignAttachments(root.Root).VerifyAsync(draft.Id, [asset], default)).IsSuccess.ShouldBeTrue();
        draft = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.ReplaceDocument(Document("图3、图1 都只是普通文字")), default)).Value;
        module.Export(draft).Text.ShouldBe("图3、图1 都只是普通文字"); module.Export(draft).RequiredAttachments.ShouldBeEmpty();
    }

    [Fact]
    public async Task Replacements_use_active_quota_and_monotonic_names_without_creating_request_authority()
    {
        using TempWorkspace root = new(); using TempWorkspace source = new(); var module = WithImages(root.Root);
        string photo = Png(source.Root, "same.png"); var draft = await Create(module);
        for (int i = 0; i < 10; i++) draft = await Bind(module, draft, "person-photo", photo);
        draft.Assets.Count.ShouldBe(10);
        module.Export(draft).RequiredAttachments.ShouldHaveSingleItem().UploadName.ShouldBe("attachment-10.png");
        (await new FileAiDesignRequestStore(root.Root).ListAsync(default)).Value.ShouldBeEmpty();
        File.Exists(Path.Combine(root.Root, "AiDesign", "Requests", draft.Id.ToString("D"), "request.json")).ShouldBeFalse();
    }

    [Fact]
    public async Task Ninth_active_image_and_nonimage_files_are_rejected_without_changing_binding()
    {
        using TempWorkspace root = new(); using TempWorkspace source = new(); var module = WithImages(root.Root);
        string photo = Png(source.Root, "one.png"); var draft = await Create(module);
        for (int i = 0; i < 9; i++)
        {
            string id = "extra-" + i;
            draft = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.InsertImageReference(Guid.NewGuid(), new(id, "explicit role " + i, true), null), default)).Value;
            if (i < 8) draft = await Bind(module, draft, id, photo);
        }
        (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.BindImage("extra-8", new(photo, "ignored")), default)).IsFailure.ShouldBeTrue();
        string pdf = source.CreateSourceFile("not-a-photo.pdf", [1, 2, 3]);
        (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.BindImage("extra-0", new(pdf, "ignored")), default)).IsFailure.ShouldBeTrue();
        var reopened = (await module.ReadAsync(draft.Id, default)).Value;
        reopened.Revision.ShouldBe(draft.Revision); reopened.Bindings.ShouldBe(draft.Bindings);
    }

    [Fact]
    public async Task Failed_draft_save_keeps_old_binding_and_stale_picker_cannot_attach_to_new_revision()
    {
        using TempWorkspace root = new(); using TempWorkspace source = new(); var module = WithImages(root.Root);
        string first = Png(source.Root, "one.png"), second = Png(source.Root, "two.png");
        var draft = await Bind(module, await Create(module), "person-photo", first);
        using (var block = new FileStream(Path.Combine(DraftDirectory(root.Root, draft.Id), "current.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.BindImage("person-photo", new(second, "ignored")), default)).IsFailure.ShouldBeTrue();
        var reopened = (await module.ReadAsync(draft.Id, default)).Value; reopened.Bindings.ShouldBe(draft.Bindings);
        draft = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.SetField("姓名", "changed"), default)).Value;
        (await module.ApplyAsync(draft.Id, reopened.Revision, new AiAuthoringEdit.BindImage("person-photo", new(second, "ignored")), default)).IsFailure.ShouldBeTrue();
        (await module.ReadAsync(draft.Id, default)).Value.Bindings.ShouldBe(reopened.Bindings);
    }

    [Fact]
    public async Task Verified_read_holds_snapshot_seal_and_ancestry_until_disposed()
    {
        using TempWorkspace root = new(); using TempWorkspace source = new(); var module = WithImages(root.Root);
        var draft = await Bind(module, await Create(module), "person-photo", Png(source.Root, "one.png")); var asset = draft.Assets.Single();
        string path = AssetPath(root.Root, draft.Id, asset), folder = Path.GetDirectoryName(path)!;
        File.SetAttributes(path, FileAttributes.Normal);
        var result = await new VerifiedAiAttachmentReader(root.Root).OpenAsync(draft.Id, asset, default);
        result.IsSuccess.ShouldBeTrue(); var lease = result.Value;
        try
        {
            lease.Stream.CanWrite.ShouldBeFalse(); lease.Stream.Position.ShouldBe(0);
            Should.Throw<IOException>(() => File.Move(path, path + ".moved"));
            Should.Throw<IOException>(() => File.WriteAllBytes(path, [9]));
            Should.Throw<IOException>(() => File.Move(Path.Combine(folder, "identity.json"), Path.Combine(folder, "old.json")));
            Should.Throw<IOException>(() => Directory.Move(folder, folder + ".moved"));
        }
        finally { await lease.DisposeAsync(); }
        Directory.Move(folder, folder + ".moved"); Directory.Move(folder + ".moved", folder);
    }

    [Fact]
    public async Task Tampered_cross_owner_and_unselected_assets_are_rejected()
    {
        using TempWorkspace root = new(); using TempWorkspace source = new(); var module = WithImages(root.Root);
        var draft = await Bind(module, await Create(module), "person-photo", Png(source.Root, "one.png")); var asset = draft.Assets.Single();
        var reader = new VerifiedAiAttachmentReader(root.Root);
        (await reader.OpenAsync(Guid.NewGuid(), asset, default)).IsFailure.ShouldBeTrue();
        (await reader.OpenAsync(draft.Id, asset with { Snapshot = new(draft.Id, "../../outside.png") }, default)).IsFailure.ShouldBeTrue();
        string path = AssetPath(root.Root, draft.Id, asset); File.SetAttributes(path, FileAttributes.Normal);
        byte[] bytes = File.ReadAllBytes(path); bytes[^1] ^= 1; File.WriteAllBytes(path, bytes);
        (await reader.OpenAsync(draft.Id, asset, default)).IsFailure.ShouldBeTrue();
        (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.BindExistingImage("layout-reference", asset.Id), default)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Preview_decodes_the_verified_stream_to_bounded_png_without_modifying_source()
    {
        using TempWorkspace root = new(); using TempWorkspace source = new(); var module = WithImages(root.Root);
        var draft = await Bind(module, await Create(module), "person-photo", Png(source.Root, "wide.png", 3072, 2)); var asset = draft.Assets.Single();
        string path = AssetPath(root.Root, draft.Id, asset); byte[] before = File.ReadAllBytes(path);
        var preview = await new FileAiAuthoringAssetPreview(root.Root).LoadAsync(draft.Id, asset, default);
        preview.IsSuccess.ShouldBeTrue(); var image = preview.Value;
        image.SourcePixelWidth.ShouldBe(3072); image.SourcePixelHeight.ShouldBe(2); image.PixelWidth.ShouldBe(2048); image.PixelHeight.ShouldBeLessThanOrEqualTo(2048);
        image.Payload.Span[..8].ToArray().ShouldBe(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        WpfRendering.OnStaThread(() =>
        {
            using var png = new MemoryStream(image.Payload.ToArray()); var decoded = new PngBitmapDecoder(png, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            decoded.Frames[0].PixelWidth.ShouldBe(image.PixelWidth); decoded.Frames[0].PixelHeight.ShouldBe(image.PixelHeight);
            byte[] pixel = new byte[4]; decoded.Frames[0].CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), pixel, 4, 0); pixel[3].ShouldBe((byte)128);
        });
        File.ReadAllBytes(path).ShouldBe(before);
    }

    [Fact]
    public async Task Cancelled_and_undecodable_previews_fail_without_touching_snapshot()
    {
        using TempWorkspace root = new(); using TempWorkspace source = new(); var module = WithImages(root.Root);
        byte[] truncatedPng = [137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3];
        string bad = source.CreateSourceFile("bad.png", truncatedPng); var draft = await Bind(module, await Create(module), "person-photo", bad); var asset = draft.Assets.Single();
        var preview = new FileAiAuthoringAssetPreview(root.Root);
        (await preview.LoadAsync(draft.Id, asset, default)).IsFailure.ShouldBeTrue();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        (await preview.LoadAsync(draft.Id, asset, cancelled.Token)).IsFailure.ShouldBeTrue();
        File.ReadAllBytes(AssetPath(root.Root, draft.Id, asset)).ShouldBe(truncatedPng);
    }

    private static AiAuthoringModule WithImages(string root) => new(new FileAiAuthoringDraftStore(root), new AiTemplateCatalog(new FileAiTemplateLibraryStore(root)), new FileAiDesignAttachments(root));
    private static async Task<AiAuthoringDraft> EightActive(AiAuthoringModule module, string root, string photo)
    {
        var fields = Enumerable.Range(0, 9).Select(i => new AiTemplateField("photo" + i, AiTemplateFieldKind.ImageReference, false, SlotId: "photo-" + i)).ToArray();
        var content = new AiTemplateContent("Nine explicit roles", AiProductKind.TextChange, string.Join("|", fields.Select(x => "{{" + x.Key + "}}")), fields);
        var template = (await new AiTemplateCatalog(new FileAiTemplateLibraryStore(root)).SaveAsync(null, content, false, default)).Value;
        var draft = (await module.CreateAsync(template.Reference, default)).Value;
        for (int i = 0; i < 8; i++) draft = await Bind(module, draft, "photo-" + i, photo);
        return draft;
    }
    private static string BmpDisguisedAsPng(string root)
    {
        string path = Path.Combine(root, "bitmap.png");
        WpfRendering.OnStaThread(() =>
        {
            var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgr32, null, new byte[16], 8);
            var encoder = new BmpBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(path); encoder.Save(output);
        });
        return path;
    }
    private static async Task<AiAuthoringDraft> Create(AiAuthoringModule module) => (await module.CreateAsync(BuiltInAiTemplates.All[0].Reference, default)).Value;
    private static async Task<AiAuthoringDraft> Bind(AiAuthoringModule module, AiAuthoringDraft draft, string slot, string path)
    {
        var result = await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.BindImage(slot, new SelectedAiDesignFile(path, "role comes from slot")), default);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.TechnicalDetail : ""); return result.Value;
    }
    private static string AssetPath(string root, Guid id, AiDesignAttachment asset) => Path.Combine(root, "AiDesign", "Requests", id.ToString("D"), asset.Snapshot.RelativePath);
    private static string Png(string root, string name, int width = 2, int height = 2)
    {
        string path = Path.Combine(root, name);
        WpfRendering.OnStaThread(() =>
        {
            byte[] pixels = new byte[width * height * 4];
            for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = 40; pixels[i + 1] = 80; pixels[i + 2] = 120; pixels[i + 3] = 128; }
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(path); encoder.Save(output);
        });
        return path;
    }
}
