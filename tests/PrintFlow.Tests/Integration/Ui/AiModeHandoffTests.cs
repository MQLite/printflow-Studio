using System.IO;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.AiDesign.Authoring;
using PrintFlow.App.Views.AiDesign;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Documents;
using static PrintFlow.Tests.Integration.AiDesign.Authoring.AuthoringPersistenceTests;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class AiModeHandoffTests
{
    [Theory]
    [InlineData("DOB.format")]
    [InlineData("DOB.label")]
    public async Task Date_metadata_does_not_shadow_a_real_template_field_with_the_same_name(string realKey)
    {
        using var f = new PromptWorkbenchFixture(); await f.Vm.OpenAsync(null, default);
        var template = (await f.Catalog.SaveAsync(null, new AiTemplateContent("Date aliases", AiProductKind.TextChange,
            "{{DOB}}\r\nordinary: {{" + realKey + "}}", [new("DOB", AiTemplateFieldKind.OptionalDateLine, false, DateLabel: "BORN", Semantic: AiTemplateSemantic.BirthDate),
                new(realKey, AiTemplateFieldKind.Text, false)]), false, default)).Value;
        await f.Vm.SelectTemplateAsync(template.Reference, false, default);
        f.Vm.Fields.Single(x => x.Key == realKey).Value = "print code A";
        f.Vm.Fields.Single(x => x.Key == "DOB").Value = "12/06/1939";
        await f.Vm.SwitchToDetailedCommand.ExecuteAsync(null);
        var date = f.Vm.TextChecks.Single(x => x.Key == "birth-date");
        date.SelectedFormat = "dd/MM/yyyy"; date.LabelInput!.Value = "NÉE";
        await f.Vm.ComposeCommand.ExecuteAsync(null);
        f.Vm.Draft!.Fields[realKey].ShouldBe("print code A");
        Text(f.Vm.Document).ShouldBe("NÉE: 12/06/1939\r\nordinary: print code A");
        f.Vm.TextChecks.Single(x => x.Key == "birth-date").DateInterpretation.ShouldBe("1939-06-12");
        await f.Vm.ConfirmTextCommand.ExecuteAsync(f.Vm.TextChecks.Single(x => x.Key == "birth-date"));
        AiAuthoringConfirmationRules.IsConfirmed(f.Vm.Draft, "birth-date").ShouldBeTrue();
    }
    [Fact]
    public async Task Personal_text_change_template_with_two_source_roles_can_be_opened_in_both_modes()
    {
        using var f = new PromptWorkbenchFixture(); await f.Vm.OpenAsync(null, default);
        var content = new AiTemplateContent("Two artwork sources", AiProductKind.TextChange, "{{front}}\r\n{{back}}\r\n{{replacement}}",
        [new("front", AiTemplateFieldKind.ImageReference, false, SlotId: "front-source", Semantic: AiTemplateSemantic.EditSourceImage),
            new("back", AiTemplateFieldKind.ImageReference, false, SlotId: "back-source", Semantic: AiTemplateSemantic.EditSourceImage),
            new("replacement", AiTemplateFieldKind.Text, true)]);
        var template = (await f.Catalog.SaveAsync(null, content, false, default)).Value;
        await f.Vm.SelectTemplateAsync(template.Reference, false, default);
        f.Vm.Draft!.Template.ShouldBe(template.Reference); AiDetailedReview.GetFirstIncompleteStep(f.Vm.Draft).ShouldBe(2);
        await f.Vm.SwitchToDetailedCommand.ExecuteAsync(null);
        f.Vm.IsDetailed.ShouldBeTrue(); f.Vm.Photos.Count.ShouldBe(2);
        using TempWorkspace source = new(); f.Picker.Pick = () => PromptWorkbenchFixture.Png(source.Root, 2);
        await f.Vm.PickSlotImageCommand.ExecuteAsync("front-source");
        var back = f.Vm.Document.Parts.OfType<AiImagePart>().Single(x => x.SlotId == "back-source");
        f.Vm.Document = new(f.Vm.Document.Parts.Where(x => x.PartId != back.PartId).ToArray());
        await f.Vm.FlushAsync(default);
        AiDetailedReview.GetFirstIncompleteStep(f.Vm.Draft).ShouldBe(3, "an explicitly removed optional source does not make a second primary source mandatory");
    }
    [Fact]
    public async Task Conflicting_preservation_remains_a_saveable_copyable_draft_but_cannot_be_confirmed()
    {
        using var f = new PromptWorkbenchFixture(); using TempWorkspace source = new(); await f.Vm.OpenAsync(null, default);
        f.Picker.Pick = () => PromptWorkbenchFixture.Png(source.Root, 2); await f.Vm.PickSlotImageCommand.ExecuteAsync("person-photo");
        await f.Vm.SwitchToDetailedCommand.ExecuteAsync(null);
        f.Vm.PreservationChecks.Single().AllowRedraw = true;
        f.Vm.PreservationChecks.Single().HasConflict.ShouldBeTrue();
        await f.Vm.CopyCommand.ExecuteAsync(null); f.Clipboard.Text.ShouldNotBeNullOrEmpty();
        f.Vm.Draft!.Preservation.Single().ShouldBe(new AiPreservationRule(f.Vm.Draft.Assets.Single().Id, true, true, true, false, true));
        await f.Vm.ConfirmPreservationCommand.ExecuteAsync(f.Vm.PreservationChecks.Single());
        f.Vm.PreservationChecks.Single().IsConfirmed.ShouldBeFalse();
        f.Vm.Draft.Confirmations.ShouldNotContainKey(f.Vm.PreservationChecks.Single().Key);
        await f.Vm.SwitchToQuickCommand.ExecuteAsync(null); f.Vm.IsDetailed.ShouldBeFalse();
    }

    [Fact]
    public async Task Date_correction_diff_survives_same_draft_mode_toggles()
    {
        using var f = new PromptWorkbenchFixture(); await f.Vm.OpenAsync(null, default);
        f.Vm.Fields.Single(x => x.Key == "出生日期行").Value = "12/06/1039";
        f.Vm.Fields.Single(x => x.Key == "出生日期行.format").Value = "dd/MM/yyyy";
        await f.Vm.SwitchToDetailedCommand.ExecuteAsync(null);
        f.Vm.TextChecks.Single(x => x.Key == "birth-date").OriginalValue.ShouldBe("12/06/1039");
        f.Vm.TextChecks.Single(x => x.Key == "birth-date").Input.Value = "12/06/1939";
        await f.Vm.SwitchToQuickCommand.ExecuteAsync(null); await f.Vm.SwitchToDetailedCommand.ExecuteAsync(null);
        var date = f.Vm.TextChecks.Single(x => x.Key == "birth-date");
        date.OriginalValue.ShouldBe("12/06/1039"); date.Input.Value.ShouldBe("12/06/1939"); date.IsConfirmed.ShouldBeFalse();
    }
    [Fact]
    public async Task Rendered_mode_buttons_flush_actual_editor_edits_on_one_UI_dispatcher()
    {
        using var f = new PromptWorkbenchFixture(); using TempWorkspace source = new();
        await f.Vm.OpenAsync(null, default); f.Picker.Pick = () => PromptWorkbenchFixture.Png(source.Root, 2);
        await f.Vm.PickSlotImageCommand.ExecuteAsync("person-photo"); var id = f.Vm.Draft!.Id; var binding = f.Vm.Draft.Bindings.Single();
        WpfRendering.RenderExpectingNoBindingErrors(() => new AiPromptWorkbenchView { DataContext = f.Vm }, new Size(1000, 700), tree =>
        {
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            try
            {
                var editor = tree.OfType<PrintFlow.App.Controls.AiPromptEditor>().Single();
                var box = (RichTextBox)editor.FindName("Editor");
                ((Paragraph)box.Document.Blocks.FirstBlock).Inlines.Add(new Run("\r\n真实全文 e\u0301  "));
                string manual = Text(f.Vm.Document);
                CompleteOnDispatcher(f.Vm.SwitchToDetailedCommand.ExecuteAsync(null));
                tree.Root.Measure(new Size(1000, 700)); tree.Root.Arrange(new Rect(0, 0, 1000, 700)); tree.Root.UpdateLayout();
                f.Vm.IsDetailed.ShouldBeTrue(); Text(f.Vm.Document).ShouldBe(manual);
                f.Vm.Draft!.Id.ShouldBe(id); f.Vm.Draft.Bindings.ShouldContain(binding);
                CompleteOnDispatcher(f.Vm.SwitchToQuickCommand.ExecuteAsync(null));
                tree.Root.UpdateLayout(); f.Vm.IsDetailed.ShouldBeFalse(); Text(f.Vm.Document).ShouldBe(manual);
                return 0;
            }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        });
        Text((await f.Module.ReadAsync(id, default)).Value.FinalDocument).ShouldEndWith("\r\n真实全文 e\u0301  ");
    }

    [Fact]
    public async Task Confirmed_text_and_images_stop_at_unavailable_layout_instead_of_claiming_completion()
    {
        using var f = new PromptWorkbenchFixture(); using TempWorkspace source = new();
        await f.Vm.OpenAsync(null, default); f.Picker.Pick = () => PromptWorkbenchFixture.Png(source.Root, 2);
        await f.Vm.PickSlotImageCommand.ExecuteAsync("person-photo");
        var layout = f.Vm.Photos.Single(x => x.SlotId == "layout-reference"); layout.SelectedAsset = f.Vm.Draft!.Assets.Single();
        await f.Vm.ReusePhotoCommand.ExecuteAsync(layout);
        f.Vm.Fields.Single(x => x.Key == "姓名").Value = "Āna";
        await f.Vm.ComposeCommand.ExecuteAsync(null); await f.Vm.SwitchToDetailedCommand.ExecuteAsync(null);
        AiDetailedReview.GetFirstIncompleteStep(f.Vm.Draft!).ShouldBe(3);
        await f.Vm.ConfirmPreservationCommand.ExecuteAsync(f.Vm.PreservationChecks.Single());
        AiDetailedReview.GetFirstIncompleteStep(f.Vm.Draft!).ShouldBe(4);
        await f.Vm.ConfirmTextCommand.ExecuteAsync(f.Vm.TextChecks.Single(x => x.Key == "name"));
        AiDetailedReview.GetFirstIncompleteStep(f.Vm.Draft!).ShouldBe(5);
        f.Vm.CanPreviewLayout.ShouldBeFalse();
    }

    [Theory]
    [InlineData(1000, 700)]
    [InlineData(1920, 1080)]
    public async Task Detailed_page_renders_all_steps_and_real_text_edits_without_binding_errors(int width, int height)
    {
        using var f = new PromptWorkbenchFixture(); using TempWorkspace source = new(); await f.Vm.OpenAsync(null, default);
        f.Picker.Pick = () => PromptWorkbenchFixture.Png(source.Root, 2); await f.Vm.PickSlotImageCommand.ExecuteAsync("person-photo");
        var layout = f.Vm.Photos.Single(x => x.SlotId == "layout-reference"); layout.SelectedAsset = f.Vm.Draft!.Assets.Single();
        await f.Vm.ReusePhotoCommand.ExecuteAsync(layout);
        f.Vm.Fields.Single(x => x.Key == "出生日期行").Value = "12/06/1039";
        f.Vm.Fields.Single(x => x.Key == "出生日期行.format").Value = "dd/MM/yyyy";
        await f.Vm.SwitchToDetailedCommand.ExecuteAsync(null);
        for (int step = 1; step <= 6; step++)
        {
            f.Vm.SelectDetailedStepCommand.Execute(step);
            var render = WpfRendering.Render(() => new AiPromptWorkbenchView { DataContext = f.Vm }, new Size(width, height), tree =>
            {
                tree.OfType<DetailedPromptView>().Count().ShouldBe(1);
                tree.OfType<PrintFlow.App.Controls.AiPromptEditor>().Count().ShouldBe(1);
                var copy = tree.OfType<Button>().Single(x => System.Windows.Automation.AutomationProperties.GetAutomationId(x) == "AiPrompt.Copy");
                var point = copy.TransformToAncestor(tree.Root).Transform(new Point());
                if (step == 4)
                {
                    var name = tree.OfType<TextBox>().Single(x => System.Windows.Automation.AutomationProperties.GetAutomationId(x) == "AiDetail.Value.name");
                    name.Text = "  ʻĀna e\u0301 陈\r\nJR  ";
                    name.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                    f.Vm.Fields.Single(x => x.Key == "姓名").Value.ShouldBe("  ʻĀna e\u0301 陈\r\nJR  ");
                }
                return point.X >= 0 && point.Y >= 0 && point.X + copy.ActualWidth <= width && point.Y + copy.ActualHeight <= height;
            });
            render.BindingErrors.ShouldBeEmpty(); render.Facts.ShouldBeTrue();
            if (Environment.GetEnvironmentVariable("PRINTFLOW_A6_CAPTURE") is { Length: > 0 } directory)
                WpfRendering.CapturePng(() => new AiPromptWorkbenchView { DataContext = f.Vm }, new Size(width, height), Path.Combine(directory, $"detail-{step}-{width}x{height}.png"));
        }
        f.Vm.CanPreviewLayout.ShouldBeFalse();
        f.Vm.Fields.Single(x => x.Key == "出生日期行.format").Value.ShouldBe("dd/MM/yyyy", "detaching a date-format selector must not erase the stored interpretation");
        (await f.Vm.FlushAsync(default)).IsSuccess.ShouldBeTrue();
        (await f.Module.ReadAsync(f.Vm.Draft!.Id, default)).Value.Fields["姓名"].ShouldBe("  ʻĀna e\u0301 陈\r\nJR  ");
    }

    private static void CompleteOnDispatcher(Task task)
    {
        Task bounded = task.WaitAsync(TimeSpan.FromSeconds(20)); var frame = new DispatcherFrame();
        bounded.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.PushFrame(frame); bounded.GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Permissions_are_flushed_on_copy_and_only_the_changed_image_confirmation_is_revoked()
    {
        using var f = new PromptWorkbenchFixture(); using TempWorkspace source = new(); await f.Vm.OpenAsync(null, default);
        f.Picker.Pick = () => PromptWorkbenchFixture.Png(source.Root, 2);
        await f.Vm.PickSlotImageCommand.ExecuteAsync("person-photo");
        await f.Vm.SwitchToDetailedCommand.ExecuteAsync(null);
        await f.Vm.ConfirmPreservationCommand.ExecuteAsync(f.Vm.PreservationChecks.Single());
        f.Vm.PreservationChecks.Single().AllowCrop = true;
        f.Vm.PreservationChecks.Single().IsConfirmed.ShouldBeFalse();
        f.Vm.HasUnsavedChanges.ShouldBeTrue(); await f.Vm.CopyCommand.ExecuteAsync(null);
        f.Vm.Draft!.Preservation.Single().AllowCrop.ShouldBeTrue();
        f.Vm.Draft.Confirmations.ShouldNotContainKey(AiAuthoringConfirmationRules.PreservationKey(f.Vm.Draft.Assets.Single().Id));
    }
    [Fact]
    public async Task Quick_detail_roundtrip_keeps_id_images_and_manual_document()
    {
        using var f = new PromptWorkbenchFixture(); using TempWorkspace source = new();
        await f.Vm.OpenAsync(null, default); f.Picker.Pick = () => PromptWorkbenchFixture.Png(source.Root, 2);
        await f.Vm.PickSlotImageCommand.ExecuteAsync("person-photo");
        var quick = f.Vm.Draft!;
        var manual = new AiPromptDocument(quick.FinalDocument.Parts.Append(new AiTextPart(Guid.NewGuid(), "\r\n  ʻĀna e\u0301 陈 {{姓名}}  ")).ToArray());
        f.Vm.Document = manual;
        await f.Vm.SwitchToDetailedCommand.ExecuteAsync(null);
        f.Vm.Draft!.Id.ShouldBe(quick.Id); f.Vm.Draft.Mode.ShouldBe(AiAuthoringMode.Detailed);
        f.Vm.Draft.Bindings.ShouldBe(quick.Bindings); f.Vm.Document.Parts.ShouldBe(manual.Parts);
        f.Vm.Draft.NeedsFieldReconcile.ShouldBeTrue(); f.Vm.Draft.Confirmations.ShouldBeEmpty();
        f.Vm.DetailedStep.ShouldBe(2); // The visible layout reference still needs a photo or explicit removal.
        await f.Vm.SwitchToQuickCommand.ExecuteAsync(null);
        f.Vm.Draft.Id.ShouldBe(quick.Id); f.Vm.Document.Parts.ShouldBe(manual.Parts);
        await f.Vm.CopyCommand.ExecuteAsync(null); f.Clipboard.Text.ShouldNotBeNull().ShouldEndWith("\r\n  ʻĀna e\u0301 陈 {{姓名}}  ");
    }

    [Fact]
    public async Task Manual_name_conflict_is_not_auto_parsed_or_confirmed()
    {
        using var f = new PromptWorkbenchFixture(); await f.Vm.OpenAsync(null, default);
        f.Vm.Fields.Single(x => x.Key == "姓名").Value = "old form";
        f.Vm.Document = Document("姓名：different manual name");
        await f.Vm.SwitchToDetailedCommand.ExecuteAsync(null);
        f.Vm.Draft!.Fields["姓名"].ShouldBe("old form"); f.Vm.Draft.Confirmations.ShouldNotContainKey("name");
        f.Vm.ReconciliationNeeded.ShouldBeTrue();
        await f.Vm.ReconcileCommand.ExecuteAsync(null);
        f.Vm.ReconciliationNeeded.ShouldBeFalse(); f.Vm.Draft.Confirmations.ShouldNotContainKey("name");
        await f.Vm.ConfirmTextCommand.ExecuteAsync(f.Vm.TextChecks.Single(x => x.Key == "name"));
        AiAuthoringConfirmationRules.IsConfirmed(f.Vm.Draft, "name").ShouldBeTrue();
        f.Vm.Document = Document("a later manual edit");
        f.Vm.ReconciliationNeeded.ShouldBeTrue(); await f.Vm.CopyCommand.ExecuteAsync(null);
        f.Clipboard.Text.ShouldBe("a later manual edit");
    }

    [Fact]
    public async Task Save_failure_blocks_mode_change_and_keeps_latest_editor_text()
    {
        using var f = new PromptWorkbenchFixture(); await f.Vm.OpenAsync(null, default);
        var quick = f.Vm.Draft!; f.Vm.Document = Document("未保存 e\u0301\r\n");
        using (var block = new FileStream(Path.Combine(DraftDirectory(f.Root.Root, quick.Id), "current.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await f.Vm.SwitchToDetailedCommand.ExecuteAsync(null);
            f.Vm.Draft!.Mode.ShouldBe(AiAuthoringMode.Quick); f.Vm.Error.ShouldNotBeNullOrEmpty();
            f.Vm.HasUnsavedChanges.ShouldBeTrue(); Text(f.Vm.Document).ShouldBe("未保存 e\u0301\r\n");
        }
        await f.Vm.SwitchToDetailedCommand.ExecuteAsync(null);
        f.Vm.Draft!.Mode.ShouldBe(AiAuthoringMode.Detailed);
        await f.Vm.OpenAsync(quick.Id, default); Text(f.Vm.Document).ShouldBe("未保存 e\u0301\r\n");
    }

    [Fact]
    public async Task Preservation_confirmation_is_explicit_and_replacement_does_not_revoke_name()
    {
        using var f = new PromptWorkbenchFixture(); using TempWorkspace source = new(); await f.Vm.OpenAsync(null, default);
        f.Vm.Fields.Single(x => x.Key == "姓名").Value = "Āna";
        f.Picker.Pick = () => PromptWorkbenchFixture.Png(source.Root, 2);
        await f.Vm.PickSlotImageCommand.ExecuteAsync("person-photo");
        f.Vm.Document = new(f.Vm.Document.Parts.Where(x => x is not AiImagePart image || image.SlotId != "layout-reference").ToArray());
        await f.Vm.SwitchToDetailedCommand.ExecuteAsync(null);
        f.Vm.PreservationChecks.Single().IsConfirmed.ShouldBeFalse();
        await f.Vm.ConfirmPreservationCommand.ExecuteAsync(f.Vm.PreservationChecks.Single());
        f.Vm.PreservationChecks.Single().IsConfirmed.ShouldBeTrue();
        await f.Vm.ConfirmTextCommand.ExecuteAsync(f.Vm.TextChecks.Single(x => x.Key == "name"));
        f.Picker.Pick = () => PromptWorkbenchFixture.Png(source.Root, 3);
        await f.Vm.PickSlotImageCommand.ExecuteAsync("person-photo");
        f.Vm.PreservationChecks.Single().IsConfirmed.ShouldBeFalse();
        AiAuthoringConfirmationRules.IsConfirmed(f.Vm.Draft!, "name").ShouldBeTrue();
        f.Vm.CanPreviewLayout.ShouldBeFalse();
        await f.Vm.CopyCommand.ExecuteAsync(null); f.Clipboard.Text.ShouldNotBeNullOrEmpty();
    }
}
