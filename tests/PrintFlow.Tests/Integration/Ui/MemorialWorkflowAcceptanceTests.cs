using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PrintFlow.App.Navigation;
using PrintFlow.App.ViewModels.AiDesign;
using PrintFlow.App.Views.AiDesign;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Domain.Files;
using PrintFlow.Infrastructure.AiDesign;
using PrintFlow.Infrastructure.AiDesign.Authoring;
using PrintFlow.Infrastructure.AiDesign.Memorial;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.AiDesign.Authoring;
using PrintFlow.Workflow.AiDesign.Memorial;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class MemorialWorkflowAcceptanceTests
{
    [Fact]
    public async Task Offline_background_and_confirmed_layout_can_be_reviewed_and_exported_as_exact_png()
    {
        using var f = new Fixture();
        await f.OpenReadyAsync();
        var vm = f.Vm;
        await vm.PrepareMemorialLayoutCommand.ExecuteAsync(null);
        var background = vm.MemorialImages.Single(x => x.Asset.OriginalName == "background.png");
        background.SelectedRole = background.Roles.Single(x => x.Role == MemorialLayerRole.Background);
        await vm.SaveMemorialLayoutCommand.ExecuteAsync(null);
        vm.CanPreviewLayout.ShouldBeFalse();
        await vm.ConfirmMemorialLayoutCommand.ExecuteAsync(null);
        vm.CanPreviewLayout.ShouldBeTrue();
        await vm.RenderMemorialPreviewCommand.ExecuteAsync(null);
        vm.MemorialResult!.File.Purpose.ShouldBe(MemorialRenderPurpose.Preview);
        vm.MemorialResult.Facts.PixelWidth.ShouldBe(326);
        vm.CanAcceptMemorialResult.ShouldBeFalse();

        await vm.RenderMemorialMaterialCommand.ExecuteAsync(null);
        vm.MemorialResult!.Facts.PixelWidth.ShouldBe(1016);
        vm.MemorialResult.File.Purpose.ShouldBe(MemorialRenderPurpose.DesignMaterial);
        vm.CanExportMemorialPng.ShouldBeFalse();
        WpfRendering.RenderExpectingNoBindingErrors(() => new DetailedPromptView { DataContext = vm }, WpfRendering.ReviewViewport, tree =>
        {
            tree.OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "Memorial.Export").IsEnabled.ShouldBeFalse();
            return 0;
        });
        WpfRendering.RenderExpectingNoBindingErrors(() => new MemorialProofView { DataContext = vm }, new Size(1100, 700), tree =>
        {
            tree.OfType<Image>().Count(image => image.Source is not null).ShouldBe(2);
            var slider = tree.OfType<Slider>().Single();
            slider.Value = 720;
            tree.Root.UpdateLayout();
            tree.OfType<Image>().All(image => image.Height == 720).ShouldBeTrue();
            return 0;
        });
        if (Environment.GetEnvironmentVariable("PRINTFLOW_MEMORIAL_UI_EVIDENCE") is { Length: > 0 } evidence)
        {
            WpfRendering.CapturePng(() => new DetailedPromptView { DataContext = vm }, WpfRendering.ReviewViewport, Path.Combine(evidence, "result-review.png"));
            WpfRendering.CapturePng(() => new MemorialProofView { DataContext = vm }, new Size(1100, 700), Path.Combine(evidence, "enlarged-proof.png"));
        }
        foreach (var check in vm.MemorialChecks.ToArray())
            await vm.ToggleMemorialCheckCommand.ExecuteAsync(vm.MemorialChecks.Single(x => x.Item == check.Item));
        vm.CanAcceptMemorialResult.ShouldBeTrue();
        await vm.AcceptMemorialResultCommand.ExecuteAsync(null);
        vm.CanExportMemorialPng.ShouldBeTrue();
        vm.CanImportMemorialResult.ShouldBeFalse();

        string destination = Path.Combine(f.External.Root, "material.png");
        f.SavePicker.Destination = destination;
        await vm.ExportMemorialPngCommand.ExecuteAsync(null);
        Sha256.FromBytes(SHA256.HashData(await File.ReadAllBytesAsync(destination))).ShouldBe(vm.MemorialResult.Facts.Sha256);
        Directory.Exists(Path.Combine(f.Workspace.Root, "Sessions")).ShouldBeFalse();

        var result = vm.MemorialResult;
        var reopened = f.NewWorkbench();
        await reopened.OpenAsync(vm.Draft!.Id, default);
        await reopened.RefreshMemorialResultsCommand.ExecuteAsync(null);
        await reopened.OpenMemorialResultCommand.ExecuteAsync(reopened.MemorialResults.Single(x => x.Result.File == result.File));
        reopened.CanExportMemorialPng.ShouldBeTrue("the exact saved review is reopened with its render");
        reopened.Fields.Single(x => x.Key == "姓名").Value = "Other name";
        reopened.CanAcceptMemorialResult.ShouldBeFalse();
        reopened.CanExportMemorialPng.ShouldBeFalse();
        f.SavePicker.Destination = Path.Combine(f.External.Root, "stale.png");
        await reopened.ExportMemorialPngCommand.ExecuteAsync(null);
        File.Exists(f.SavePicker.Destination).ShouldBeFalse();
    }

    [Fact]
    public async Task Conversion_requires_confirmation_and_keeps_source_intact()
    {
        using var f = new Fixture();
        await f.OpenReadyAsync();
        var vm = f.Vm;
        var before = vm.Draft!;
        int draftCount = (await f.Module.ListAsync(default)).Value.Drafts.Count;
        vm.BeginMemorialConversionCommand.Execute(null);
        vm.IsMemorialConversionPending.ShouldBeTrue();
        vm.CancelMemorialConversionCommand.Execute(null);
        vm.Draft!.Id.ShouldBe(before.Id);
        (await f.Module.ListAsync(default)).Value.Drafts.Count.ShouldBe(draftCount, "cancellation adds no draft");
        vm.BeginMemorialConversionCommand.Execute(null);
        await vm.ConfirmMemorialConversionCommand.ExecuteAsync(null);
        vm.Draft!.ParentDraftId.ShouldBe(before.Id);
        vm.Draft.ParentDraftRevision.ShouldBe(before.Revision);
        vm.Draft.Product.ShouldBe(AiProductKind.MemorialTshirt);
        vm.Draft.Layout.ShouldBeNull();
        vm.Draft.Assets.All(x => x.Snapshot.RequestId == vm.Draft.Id).ShouldBeTrue();
        (await f.Module.ReadAsync(before.Id, default)).Value.Revision.ShouldBe(before.Revision);
    }

    [Theory]
    [InlineData(AiProductKind.MemorialBadge, "画布外圆", "100mm", "86mm")]
    [InlineData(AiProductKind.MemorialBadge, "内容安全区", "80mm", "72mm")]
    [InlineData(AiProductKind.MemorialTshirt, "设计规格", "300×400mm", "280×390mm")]
    public async Task Preset_dimensions_are_read_only_and_regeneration_uses_the_layout_spec(
        AiProductKind product, string fieldKey, string priorValue, string expectedValue)
    {
        using var f = new Fixture();
        await f.Vm.OpenAsync(null, default);
        var template = f.Vm.Templates.Single(x => x.Content.Product == product);
        // Simulate a draft stored by a build that allowed editing the physical preset.
        var draft = (await f.Module.CreateAsync(template.Reference, default)).Value;
        var saved = await new FileAiAuthoringDraftStore(f.Workspace.Root).SaveAsync(draft with
        {
            Fields = new Dictionary<string, string>(draft.Fields, StringComparer.Ordinal) { [fieldKey] = priorValue }
        }, draft.Revision, default);
        saved.IsSuccess.ShouldBeTrue();
        await f.Vm.OpenAsync(draft.Id, default);
        await f.Vm.ComposeCommand.ExecuteAsync(null);
        string composed = string.Concat(f.Vm.Document.Parts.OfType<AiTextPart>().Select(part => part.Text));
        composed.ShouldContain(expectedValue);
        composed.ShouldNotContain(priorValue);

        WpfRendering.RenderExpectingNoBindingErrors(() => new QuickPromptView { DataContext = f.Vm }, WpfRendering.ReviewViewport, tree =>
        {
            var field = tree.OfType<TextBox>().Single(box => AutomationProperties.GetName(box) == fieldKey);
            field.IsReadOnly.ShouldBeTrue();
            field.Text.ShouldBe(expectedValue);
            return 0;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saved_preset_value_does_not_make_an_older_generated_prompt_safe_to_copy(bool mixedTemplateLine)
    {
        using var f = new Fixture();
        await f.Vm.OpenAsync(null, default);
        var badge = f.Vm.Templates.Single(x => x.Content.Product == AiProductKind.MemorialBadge);
        var draft = (await f.Module.CreateAsync(badge.Reference, default)).Value;
        if (mixedTemplateLine)
        {
            var template = draft.TemplateSnapshot;
            template = template with { Body = template.Body.Replace("本次历史预设", "{{姓名}} · 本次历史预设", StringComparison.Ordinal) };
            var fields = new Dictionary<string, string>(draft.Fields, StringComparer.Ordinal) { ["姓名"] = "Ava" };
            draft = draft with { TemplateSnapshot = template, Fields = fields, FinalDocument = PromptDocumentComposer.Compose(template, fields).Document };
        }
        // An older build could save 86 mm in the form after generating a 100 mm prompt.
        var oldPrompt = new AiPromptDocument(draft.FinalDocument.Parts.Select(part => part is AiTextPart text
            ? text with { Text = text.Text.Replace("86mm", "100mm", StringComparison.Ordinal) } : part).ToArray());
        var saved = await new FileAiAuthoringDraftStore(f.Workspace.Root).SaveAsync(draft with { FinalDocument = oldPrompt },
            draft.Revision, default);
        saved.IsSuccess.ShouldBeTrue();
        await f.Vm.OpenAsync(draft.Id, default);

        await f.Vm.CopyCommand.ExecuteAsync(null);
        f.Clipboard.Text.ShouldBeNull();
        f.Vm.Error.ShouldNotBeNullOrEmpty();
        await f.Vm.ComposeCommand.ExecuteAsync(null);
        await f.Vm.CopyCommand.ExecuteAsync(null);
        f.Clipboard.Text.ShouldNotBeNull().ShouldContain("86mm");
        f.Clipboard.Text.ShouldNotContain("100mm");
        var history = (await f.Module.ReadHistoryAsync(draft.Id, default)).Value;
        history.ShouldContain(item => item.FinalDocument.Parts.SequenceEqual(oldPrompt.Parts));
    }

    [Fact]
    public async Task Confirming_changed_text_leaves_old_placement_unpublished_and_allows_reopening_it()
    {
        using var f = new Fixture();
        await f.OpenReadyAsync();
        var vm = f.Vm;
        await vm.PrepareMemorialLayoutCommand.ExecuteAsync(null);
        await vm.SaveMemorialLayoutCommand.ExecuteAsync(null);
        await vm.ConfirmMemorialLayoutCommand.ExecuteAsync(null);
        vm.CanPreviewLayout.ShouldBeTrue();
        var saved = vm.Draft!.Layout!;
        vm.MemorialText.Single(x => x.Key == "name").FontSize = "8";

        vm.DetailedStep = 4;
        var name = vm.TextChecks.Single(x => x.Key == "name");
        name.Input.Value = "Ava";
        await vm.ConfirmTextCommand.ExecuteAsync(name);

        vm.TextChecks.Single(x => x.Key == "name").IsConfirmed.ShouldBeTrue();
        vm.Draft!.Layout!.Text.ShouldBe(saved.Text);
        vm.CanSaveMemorialLayout.ShouldBeFalse();
        vm.CanPreviewLayout.ShouldBeFalse();

        // The prompt still contains the previous wording. Preserve the existing requirement
        // to acknowledge that comparison before composing from the newly confirmed text.
        vm.ReconciliationNeeded.ShouldBeTrue();
        await vm.ReconcileCommand.ExecuteAsync(null);
        vm.DetailedStep = 5;
        vm.CanPrepareMemorialLayout.ShouldBeTrue();
        await vm.PrepareMemorialLayoutCommand.ExecuteAsync(null);
        vm.MemorialText.Single(x => x.Key == "name").Text.ShouldBe("Ava");
        await vm.SaveMemorialLayoutCommand.ExecuteAsync(null);
        await vm.ConfirmMemorialLayoutCommand.ExecuteAsync(null);
        vm.CanPreviewLayout.ShouldBeTrue();
        vm.Draft!.Layout!.Text.Single(x => x.Key == "name").Text.ShouldBe("Ava");
    }

    internal sealed class Fixture : IDisposable
    {
        public TempWorkspace Workspace { get; } = new();
        public TempWorkspace External { get; } = new();
        public AiAuthoringModule Module { get; }
        public AiPromptWorkbenchViewModel Vm { get; }
        public PromptClipboard Clipboard { get; } = new();
        public TestSavePicker SavePicker { get; } = new();
        private readonly ControlledPromptPicker picker = new();
        private readonly AiTemplateCatalog catalog;
        private readonly AiMemorialServices services;

        public Fixture(IMemorialRenderer? renderer = null)
        {
            string root = Workspace.Root;
            var drafts = new FileAiAuthoringDraftStore(root);
            catalog = new AiTemplateCatalog(new FileAiTemplateLibraryStore(root, "zh-CN"));
            Module = new(drafts, catalog, new FileAiDesignAttachments(root));
            var renders = new FileMemorialRenderStore(root);
            var text = new WpfMemorialTextLayout();
            var composition = new MemorialCompositionModule(drafts, renderer ?? new WpfMemorialRenderer(new VerifiedAiAttachmentReader(root), text, renders, drafts));
            var reviewStore = new FileMemorialReviewStore(root);
            services = new AiMemorialServices(text, composition, renders, new FileMemorialRenderPreview(renders),
                new MemorialVersionModule(drafts, new FileAiAuthoringAssetFork(root)), new MemorialReviewChecklist(drafts, renders, reviewStore),
                new FileMemorialMaterialExporter(renders, reviewStore, drafts, [root]), SavePicker);
            Vm = NewWorkbench();
        }

        public AiPromptWorkbenchViewModel NewWorkbench() => new(Module, catalog, new FileAiAuthoringAssetPreview(Workspace.Root),
            picker, Clipboard, memorial: services);

        public async Task OpenReadyAsync()
        {
            await Vm.OpenAsync(null, default);
            var badge = Vm.Templates.Single(x => x.Content.Product == AiProductKind.MemorialBadge);
            await Vm.SelectTemplateAsync(badge.Reference, false, default);
            var person = PromptWorkbenchFixture.Png(External.Root, 2);
            picker.Pick = () => person;
            await Vm.PickSlotImageCommand.ExecuteAsync("person-photo");
            var background = Path.Combine(External.Root, "background.png");
            File.Copy(person, background);
            var reference = Vm.Photos.Single(x => x.SlotId == "layout-reference");
            picker.Pick = () => background;
            await Vm.PickSlotImageCommand.ExecuteAsync(reference.SlotId);
            Vm.Fields.Single(x => x.Key == "姓名").Value = "Āna";
            await Vm.ComposeCommand.ExecuteAsync(null);
            await Vm.SwitchToDetailedCommand.ExecuteAsync(null);
            var backgroundCheck = Vm.PreservationChecks.Single(x => x.Asset.OriginalName == "background.png");
            backgroundCheck.KeepContent = false;
            await Vm.ConfirmPreservationCommand.ExecuteAsync(backgroundCheck);
            await Vm.ConfirmPreservationCommand.ExecuteAsync(Vm.PreservationChecks.Single(x => x.Asset.OriginalName == "same.png"));
            await Vm.ConfirmTextCommand.ExecuteAsync(Vm.TextChecks.Single(x => x.Key == "name"));
            AiDetailedReview.GetFirstIncompleteStep(Vm.Draft!).ShouldBe(5);
        }

        public void Dispose() { External.Dispose(); Workspace.Dispose(); }
    }

    internal sealed class TestSavePicker : IAiDesignPngSavePicker
    {
        public string? Destination { get; set; }
        public string? PickDestination(string suggestedFileName) => Destination;
    }
}
