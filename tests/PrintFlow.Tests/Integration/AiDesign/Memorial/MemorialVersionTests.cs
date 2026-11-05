using System.IO;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.AiDesign;
using PrintFlow.Infrastructure.AiDesign.Authoring;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.AiDesign.Authoring;
using PrintFlow.Workflow.AiDesign.Memorial;

namespace PrintFlow.Tests.Integration.AiDesign.Memorial;

public sealed class MemorialVersionTests
{
    private static readonly byte[] ImageBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a0XcAAAAASUVORK5CYII=");

    [Theory]
    [InlineData(AiProductKind.MemorialTshirt, AiProductKind.MemorialBadge, "Round memorial badge")]
    [InlineData(AiProductKind.MemorialBadge, AiProductKind.MemorialTshirt, "Memorial T-shirt")]
    public async Task Fork_preserves_parent_and_reowns_active_bytes_in_each_direction(AiProductKind from, AiProductKind to, string template)
    {
        using TempWorkspace root = new();
        var drafts = new FileAiAuthoringDraftStore(root.Root);
        var source = await SaveSourceAsync(root, drafts, from);
        source.Layout!.ProductTemplateConfirmed.ShouldBeTrue();
        string pointer = Path.Combine(root.Root, "AiDesign", "Authoring", "Drafts", source.Id.ToString("D"), "current.json");
        byte[] originalPointer = await File.ReadAllBytesAsync(pointer);

        var child = (await new MemorialVersionModule(drafts, new FileAiAuthoringAssetFork(root.Root), "en")
            .ForkAsync(source.Id, source.Revision, to, default)).Value;

        child.ParentDraftId.ShouldBe(source.Id);
        child.ParentDraftRevision.ShouldBe(source.Revision);
        child.Id.ShouldNotBe(source.Id);
        child.Product.ShouldBe(to);
        child.TemplateSnapshot.Name.ShouldBe(template);
        child.TemplateSnapshot.Fields.ShouldNotContain(field => field.Key == (from == AiProductKind.MemorialTshirt ? "设计规格" : "画布外圆"));
        child.Layout.ShouldBeNull();
        child.Fields["姓名"].ShouldBe("ʻĀna  DEMO 陈");
        child.Fields["出生日期行.label"].ShouldBe("Exact Label");
        AiAuthoringConfirmationRules.IsConfirmed(child, "name").ShouldBeTrue();
        AiAuthoringConfirmationRules.IsConfirmed(child, "birth-date").ShouldBeTrue();
        child.FinalDocument.Parts.OfType<AiImagePart>().Select(x => x.SlotId).ShouldContain("medal");
        var childAsset = child.Assets.Single();
        childAsset.Id.ShouldNotBe(source.Assets[0].Id);
        childAsset.Snapshot.RequestId.ShouldBe(child.Id);
        childAsset.Sha256.ShouldBe(source.Assets[0].Sha256);
        child.Bindings.Select(x => x.AssetId).Distinct().ShouldBe([childAsset.Id]);
        child.Preservation.Single().AssetId.ShouldBe(childAsset.Id);
        AiAuthoringConfirmationRules.IsConfirmed(child, AiAuthoringConfirmationRules.PreservationKey(childAsset.Id)).ShouldBeTrue();
        var reader = new VerifiedAiAttachmentReader(root.Root);
        await using var read = (await reader.OpenAsync(child.Id, childAsset, default)).Value;
        using MemoryStream actual = new();
        await read.Stream.CopyToAsync(actual);
        actual.ToArray().ShouldBe(ImageBytes);
        (await File.ReadAllBytesAsync(pointer)).ShouldBe(originalPointer);
        await using var original = (await reader.OpenAsync(source.Id, source.Assets[0], default)).Value;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancelled_or_stale_fork_rolls_back_owned_copies_without_publishing_child(bool cancel)
    {
        using TempWorkspace root = new();
        using CancellationTokenSource cancellation = new();
        var drafts = new FileAiAuthoringDraftStore(root.Root);
        var source = await SaveSourceAsync(root, drafts, AiProductKind.MemorialTshirt);
        var assets = new BeforePublishFork(new FileAiAuthoringAssetFork(root.Root), async () =>
        {
            if (cancel) cancellation.Cancel();
            else (await drafts.SaveAsync(source, source.Revision, default)).IsSuccess.ShouldBeTrue();
        });

        var failed = await new MemorialVersionModule(drafts, assets).ForkAsync(source.Id, source.Revision,
            AiProductKind.MemorialBadge, cancellation.Token);

        failed.Failure.Code.ShouldBe(cancel ? FailureCode.Cancelled : FailureCode.PreconditionNotMet);
        var current = (await drafts.ReadAsync(source.Id, default)).Value;
        current.Revision.ShouldBe(source.Revision + (cancel ? 0 : 1));
        (await drafts.ListAsync(default)).Value.Drafts.Select(x => x.Draft.Id).ShouldBe([source.Id]);
        var files = Directory.GetFiles(Path.Combine(root.Root, "AiDesign", "Requests"), "identity.json", SearchOption.AllDirectories);
        files.Length.ShouldBe(1);
        (await new FileAiDesignAttachments(root.Root).VerifyAsync(source.Id, source.Assets, default)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Confirmed_custom_date_field_keeps_exact_value_label_and_interpretation_on_target()
    {
        using TempWorkspace root = new();
        var drafts = new FileAiAuthoringDraftStore(root.Root);
        var source = await SaveSourceAsync(root, drafts, AiProductKind.MemorialTshirt, customDate: true);
        AiAuthoringConfirmationRules.IsConfirmed(source, "birth-date").ShouldBeTrue();

        var child = (await new MemorialVersionModule(drafts, new FileAiAuthoringAssetFork(root.Root))
            .ForkAsync(source.Id, source.Revision, AiProductKind.MemorialBadge, default)).Value;

        child.Fields["出生日期行"].ShouldBe("1039");
        child.Fields["出生日期行.label"].ShouldBe("Exact Label");
        child.Fields["出生日期行.format"].ShouldBe("yyyy");
        AiAuthoringConfirmationRules.IsConfirmed(child, "birth-date").ShouldBeTrue();
    }

    [Fact]
    public async Task Fork_rebuilds_target_prompt_with_confirmed_custom_literals_without_shadowing_preset_or_date_metadata()
    {
        using TempWorkspace root = new();
        var drafts = new FileAiAuthoringDraftStore(root.Root);
        var source = await SaveSourceAsync(root, drafts, AiProductKind.MemorialTshirt);
        var phrase = new AiTemplateField("出生日期行.label", AiTemplateFieldKind.Text, true);
        var date = new AiTemplateField("画布外圆", AiTemplateFieldKind.OptionalDateLine, true);
        var sourceTemplate = source.TemplateSnapshot with
        {
            Body = source.TemplateSnapshot.Body + "\n{{出生日期行.label}}\n{{画布外圆}}",
            Fields = source.TemplateSnapshot.Fields.Concat([phrase, date]).ToArray()
        };
        var birthday = sourceTemplate.Fields.Single(field => field.Semantic == AiTemplateSemantic.BirthDate);
        var sourceFields = new Dictionary<string, string>(source.Fields)
        {
            [AiTemplateMetadata.LabelKey(sourceTemplate, birthday)] = "Exact Label",
            [phrase.Key] = "Still  with us — ʻĀna",
            [date.Key] = "04/03/2005",
            [AiTemplateMetadata.LabelKey(sourceTemplate, date)] = "Rā Maumahara",
            [AiTemplateMetadata.FormatKey(sourceTemplate, date)] = "dd/MM/yyyy"
        };
        var composed = PromptDocumentComposer.Compose(sourceTemplate, sourceFields).Document;
        var custom = source with
        {
            Template = new("fixture-custom-literals", 1), TemplateSnapshot = sourceTemplate, Fields = sourceFields,
            InitialDocument = composed, FinalDocument = new(composed.Parts.Append(new AiTextPart(Guid.NewGuid(), "Source-only manual prose")).ToArray()),
            IsManuallyEdited = true, Layout = null
        };
        custom = custom with
        {
            Confirmations = new[] { AiAuthoringConfirmationRules.TextKey(phrase), AiAuthoringConfirmationRules.TextKey(date), "birth-date" }
                .ToDictionary(key => key, key => AiAuthoringConfirmationRules.ContentHash(custom, key)!)
        };
        source = (await drafts.SaveAsync(custom, source.Revision, default)).Value;

        var child = (await new MemorialVersionModule(drafts, new FileAiAuthoringAssetFork(root.Root), "en")
            .ForkAsync(source.Id, source.Revision, AiProductKind.MemorialBadge, default)).Value;

        child.Fields["画布外圆"].ShouldBe("86mm");
        child.Fields.GetValueOrDefault(phrase.Key).ShouldBe("Still  with us — ʻĀna");
        var inheritedDate = child.TemplateSnapshot.Fields.Single(field => field.Semantic == AiTemplateSemantic.FreeText && field.Kind == AiTemplateFieldKind.OptionalDateLine);
        inheritedDate.Key.ShouldNotBe(date.Key);
        AiAuthoringConfirmationRules.Value(child, inheritedDate).ShouldBe("04/03/2005");
        AiAuthoringConfirmationRules.DateLabel(child, inheritedDate).ShouldBe("Rā Maumahara");
        AiAuthoringConfirmationRules.DateFormat(child, inheritedDate).ShouldBe("dd/MM/yyyy");
        var childBirthday = child.TemplateSnapshot.Fields.Single(field => field.Semantic == AiTemplateSemantic.BirthDate);
        AiAuthoringConfirmationRules.DateLabel(child, childBirthday).ShouldBe("Exact Label");
        AiAuthoringConfirmationRules.IsConfirmed(child, AiAuthoringConfirmationRules.TextKey(phrase)).ShouldBeTrue();
        AiAuthoringConfirmationRules.IsConfirmed(child, AiAuthoringConfirmationRules.TextKey(inheritedDate)).ShouldBeTrue();
        AiAuthoringConfirmationRules.IsConfirmed(child, "birth-date").ShouldBeTrue();
        var layoutInputs = MemorialLayoutModule.TextInputs(child);
        layoutInputs[AiAuthoringConfirmationRules.TextKey(phrase)].ShouldBe("Still  with us — ʻĀna");
        layoutInputs[AiAuthoringConfirmationRules.TextKey(inheritedDate)].ShouldBe("04/03/2005");
        layoutInputs[MemorialLayoutModule.DateLabelKey(inheritedDate)].ShouldBe("Rā Maumahara");
        foreach (var document in new[] { child.FinalDocument, PromptDocumentComposer.Compose(child.TemplateSnapshot, child.Fields).Document })
        {
            var text = string.Concat(document.Parts.OfType<AiTextPart>().Select(part => part.Text));
            text.ShouldContain("Still  with us — ʻĀna");
            text.ShouldContain("Rā Maumahara: 04/03/2005");
            text.ShouldNotContain("Source-only manual prose");
        }
        (await drafts.ReadAsync(source.Id, default)).Value.FinalDocument.Parts.OfType<AiTextPart>()
            .ShouldContain(part => part.Text == "Source-only manual prose");
    }

    [Fact]
    public async Task Verified_source_bytes_cannot_be_replaced_between_copy_and_child_publication()
    {
        using TempWorkspace root = new();
        var drafts = new FileAiAuthoringDraftStore(root.Root);
        var source = await SaveSourceAsync(root, drafts, AiProductKind.MemorialTshirt);
        string file = Path.Combine(root.Root, "AiDesign", "Requests", source.Id.ToString("D"), source.Assets.Single().Snapshot.RelativePath);
        File.SetAttributes(file, FileAttributes.Normal);
        var assets = new BeforePublishFork(new FileAiAuthoringAssetFork(root.Root), () =>
        {
            Should.Throw<IOException>(() => File.Move(file, file + ".replaced"));
            return Task.CompletedTask;
        });

        var result = await new MemorialVersionModule(drafts, assets).ForkAsync(source.Id, source.Revision, AiProductKind.MemorialBadge, default);

        result.IsSuccess.ShouldBeTrue();
        (await File.ReadAllBytesAsync(file)).ShouldBe(ImageBytes);
    }

    private static async Task<AiAuthoringDraft> SaveSourceAsync(TempWorkspace root, FileAiAuthoringDraftStore drafts,
        AiProductKind kind, bool customDate = false)
    {
        var definition = BuiltInAiTemplates.ForLanguage("en").Single(x => x.Content.Product == kind);
        string dateKey = customDate ? "actual-birthday" : "出生日期行";
        if (customDate)
            definition = new(new("test-custom-template", 1), definition.Content with
            {
                Body = definition.Content.Body.Replace("出生日期行", dateKey, StringComparison.Ordinal),
                Fields = definition.Content.Fields.Select(field => field.Semantic == AiTemplateSemantic.BirthDate
                    ? field with { Key = dateKey, Kind = AiTemplateFieldKind.Text } : field).ToArray()
            }, null);
        var id = Guid.NewGuid();
        var selected = root.CreateSourceFile("synthetic.png", ImageBytes);
        var asset = (await new FileAiDesignAttachments(root.Root).SnapshotAsync(id, [new(selected, "person")], [], default)).Value.Single();
        var fields = new Dictionary<string, string>
        {
            ["姓名"] = "ʻĀna  DEMO 陈", [dateKey] = "1039", [dateKey + ".label"] = "Exact Label", [dateKey + ".format"] = "yyyy"
        };
        var document = PromptDocumentComposer.Compose(definition.Content, fields).Document;
        var draft = new AiAuthoringDraft
        {
            Id = id, Template = definition.Reference, TemplateSnapshot = definition.Content, Product = kind, Mode = AiAuthoringMode.Detailed,
            Fields = fields, InitialDocument = document, FinalDocument = new(document.Parts.Append(new AiImagePart(Guid.NewGuid(), "medal")).ToArray()),
            Slots = [new("person-photo", "人物参考", false), new("layout-reference", "版式参考", false), new("medal", "胸章", false)],
            Bindings = [new("person-photo", asset.Id), new("layout-reference", asset.Id), new("medal", asset.Id)], Assets = [asset],
            Preservation = [new(asset.Id, true, true, true, false, false)],
            Layout = new(kind == AiProductKind.MemorialBadge ? MemorialLayoutModule.BadgePreset : MemorialLayoutModule.TshirtPreset,
                [new(asset.Id, MemorialLayerRole.Person, new(35, 32, 6, 6), null, 0) { SourceExtent = new(0, 0, 1, 1) }],
                [Line("name", fields["姓名"], 20), Line("birth-date", fields[dateKey], 44), Line("date-label:birth-date", fields[dateKey + ".label"], 51)], true)
        };
        draft = draft with
        {
            Confirmations = new[] { "name", "birth-date", AiAuthoringConfirmationRules.PreservationKey(asset.Id) }
                .ToDictionary(x => x, x => AiAuthoringConfirmationRules.ContentHash(draft, x)!)
        };
        return (await drafts.SaveAsync(draft, null, default)).Value;

        static MemorialTextLine Line(string key, string value, double y) =>
            new(key, value, "Arial", 3, new(24, y, 24, 5), MemorialTextPlacement.Flat, null);
    }

    private sealed class BeforePublishFork(IAiAuthoringAssetFork inner, Func<Task> beforePublish) : IAiAuthoringAssetFork
    {
        public Task<OperationResult<IReadOnlyList<AiDesignAttachment>>> CopyAsync(Guid sourceOwnerId, Guid targetOwnerId,
            IReadOnlyList<AiDesignAttachment> assets, CancellationToken ct) => inner.CopyAsync(sourceOwnerId, targetOwnerId, assets, ct);

        public Task<OperationResult<T>> CopyAndPublishAsync<T>(Guid sourceOwnerId, Guid targetOwnerId, IReadOnlyList<AiDesignAttachment> assets,
            Func<IReadOnlyList<AiDesignAttachment>, Task<OperationResult<T>>> publish, CancellationToken ct) =>
            inner.CopyAndPublishAsync(sourceOwnerId, targetOwnerId, assets, async copied =>
            {
                await beforePublish();
                return await publish(copied);
            }, ct);
    }
}
