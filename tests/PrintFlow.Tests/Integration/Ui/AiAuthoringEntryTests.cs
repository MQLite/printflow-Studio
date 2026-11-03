using System.IO;
using PrintFlow.App.Navigation;
using PrintFlow.Domain.AiDesign.Authoring;
using Shouldly;
using Xunit;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Threading;
using PrintFlow.App;
using PrintFlow.App.Views;
using PrintFlow.App.Views.AiDesign;
using PrintFlow.App.ViewModels;
using PrintFlow.App.ViewModels.AiDesign;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.AiDesign.Authoring;
using Microsoft.Extensions.DependencyInjection;

namespace PrintFlow.Tests.Integration.Ui;

public sealed class AiAuthoringEntryTests
{
    [Fact]
    public void Invisible_main_window_awaits_one_close_decision_and_cancel_keeps_the_window()
    {
        WpfRendering.OnStaThread(() =>
        {
            using var app = new TempApplication(); var prompt = new ControlledLeavePrompt();
            using var services = AuthoringGraphTests.Compose(app, prompt);
            var nav = services.GetRequiredService<INavigationService>(); Complete(nav.GoHomeAsync(default));
            Complete(services.GetRequiredService<IAiDesignNavigation>().GoToPromptWorkbenchAsync(null, default));
            var vm = nav.Current.ShouldBeOfType<AiPromptWorkbenchViewModel>(); vm.Document = Document("close through real WPF window");
            var window = new MainWindow { DataContext = services.GetRequiredService<ShellViewModel>() };
            bool closed = false; window.Closed += (_, _) => closed = true;
            var decision = new TaskCompletionSource<AiDraftLeaveChoice>(); prompt.Pending = decision.Task;
            window.Close(); window.Close(); prompt.Count.ShouldBe(1); closed.ShouldBeFalse();
            var pendingClose = vm.RequestCloseAsync(default);
            decision.SetResult(AiDraftLeaveChoice.Cancel); Complete(pendingClose); Drain();
            closed.ShouldBeFalse(); vm.HasUnsavedChanges.ShouldBeTrue();
            bool vetoSavedClose = true;
            window.Closing += (_, e) => { if (vetoSavedClose && !vm.HasUnsavedChanges) { e.Cancel = true; vetoSavedClose = false; } };
            prompt.Pending = null; prompt.Choice = AiDraftLeaveChoice.Save; window.Close(); Complete(vm.RequestCloseAsync(default)); Drain();
            closed.ShouldBeFalse();
            vm.Document = Document("a later unsaved edit"); prompt.Choice = AiDraftLeaveChoice.Cancel;
            window.Close(); Complete(vm.RequestCloseAsync(default)); Drain(); closed.ShouldBeFalse();
            prompt.Choice = AiDraftLeaveChoice.Discard; window.Close(); Complete(vm.RequestCloseAsync(default)); Drain();
            closed.ShouldBeTrue();
            Text((services.GetRequiredService<PrintFlow.Workflow.AiDesign.Authoring.AiAuthoringModule>().ReadAsync(vm.Draft!.Id, default).GetAwaiter().GetResult()).Value.FinalDocument).ShouldBe("close through real WPF window");
        });
    }

    [Theory]
    [InlineData(1000, 700, "zh-CN")]
    [InlineData(1920, 1080, "zh-CN")]
    [InlineData(1000, 700, "en")]
    [InlineData(1920, 1080, "en")]
    public async Task Home_and_registered_workbench_template_render_with_keyboard_actions_in_view(int width, int height, string language)
    {
        using var culture = new OperatorCultureScope(language);
        using var app = new TempApplication(); using var services = AuthoringGraphTests.Compose(app, new());
        var nav = services.GetRequiredService<INavigationService>(); await nav.GoHomeAsync(default);
        var homeRender = WpfRendering.Render(() => new HomeView { DataContext = nav.Current }, new Size(width, height), tree =>
        {
            var entry = tree.OfType<Button>().Single(x => AutomationProperties.GetAutomationId(x) == "Home.PromptWorkbench");
            return entry.IsEnabled && entry.IsTabStop && entry.Focusable;
        }); homeRender.BindingErrors.ShouldBeEmpty(); homeRender.Facts.ShouldBeTrue();
        await services.GetRequiredService<IAiDesignNavigation>().GoToPromptWorkbenchAsync(null, default);
        var render = WpfRendering.Render(() =>
        {
            var window = new MainWindow(); var key = new DataTemplateKey(typeof(AiPromptWorkbenchViewModel));
            var host = new UserControl { Content = new ContentControl { Content = nav.Current } };
            host.Resources.Add(key, window.Resources[key]); return host;
        }, new Size(width, height), tree =>
        {
            tree.OfType<AiPromptWorkbenchView>().Count().ShouldBe(1);
            foreach (string id in new[] { "AiPrompt.Copy", "AiPrompt.ReturnHome", "AiPrompt.SavedDrafts" })
            {
                var action = tree.OfType<Button>().Single(x => AutomationProperties.GetAutomationId(x) == id);
                var bounds = action.TransformToAncestor(tree.Root).TransformBounds(new Rect(action.RenderSize));
                (action.IsTabStop && action.Focusable && action.IsEnabled).ShouldBeTrue();
                bounds.Top.ShouldBeGreaterThanOrEqualTo(0); bounds.Bottom.ShouldBeLessThanOrEqualTo(height);
            }
            var tokens = tree.OfType<RichTextBox>().Single().Document.Blocks.OfType<System.Windows.Documents.Paragraph>()
                .SelectMany(x => x.Inlines.OfType<System.Windows.Documents.InlineUIContainer>()).Select(x => (Button)x.Child).ToArray();
            tokens.Length.ShouldBe(2);
            tokens.All(x => x.Focusable && x.IsTabStop && x.IsEnabled).ShouldBeTrue();
            var save = tree.OfType<Button>().Single(x => ReferenceEquals(x.Command, ((AiPromptWorkbenchViewModel)nav.Current!).SaveDraftCommand));
            (save.IsEnabled && save.IsTabStop && save.Focusable).ShouldBeTrue();
            save.TransformToAncestor(tree.Root).TransformBounds(new Rect(save.RenderSize)).Bottom.ShouldBeLessThanOrEqualTo(height);
            return true;
        }); render.BindingErrors.ShouldBeEmpty(); render.Facts.ShouldBeTrue();
        if (Environment.GetEnvironmentVariable("PRINTFLOW_A7_CAPTURE") is { Length: > 0 } capture)
        {
            WpfRendering.CapturePng(() => new AiPromptWorkbenchView { DataContext = nav.Current }, new Size(width, height), Path.Combine(capture, $"workbench-{language}-{width}x{height}.png"));
            WpfRendering.CapturePng(() => new HomeView { DataContext = services.GetRequiredService<HomeViewModel>() }, new Size(width, height), Path.Combine(capture, $"home-{language}-{width}x{height}.png"));
        }
    }

    private static void Complete(Task task)
    {
        var bounded = task.WaitAsync(TimeSpan.FromSeconds(15)); var frame = new DispatcherFrame();
        bounded.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.PushFrame(frame); bounded.GetAwaiter().GetResult();
    }
    private static void Drain()
    {
        var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false)); Dispatcher.PushFrame(frame);
    }

    [Fact]
    public async Task Close_cancel_keeps_latest_editor_content()
    {
        var prompt = new ControlledLeavePrompt { Choice = AiDraftLeaveChoice.Cancel };
        using var f = new PromptWorkbenchFixture(prompt); await f.Vm.OpenAsync(null, default);
        f.Vm.Document = Document("  ʻĀna e\u0301 陈\r\n{{姓名}}  ");
        (await f.Vm.RequestCloseAsync(default)).ShouldBeFalse();
        Text(f.Vm.Document).ShouldBe("  ʻĀna e\u0301 陈\r\n{{姓名}}  ");
        f.Vm.HasUnsavedChanges.ShouldBeTrue();
        (await f.Module.ReadAsync(f.Vm.Draft!.Id, default)).Value.FinalDocument.Parts.ShouldNotBe(f.Vm.Document.Parts);
    }

    [Fact]
    public async Task Save_failure_blocks_close_and_mode_change()
    {
        using var f = new PromptWorkbenchFixture(new ControlledLeavePrompt()); await f.Vm.OpenAsync(null, default);
        var id = f.Vm.Draft!.Id; f.Vm.Document = Document("未保存\r\ne\u0301");
        using (var block = new FileStream(Path.Combine(f.Root.Root, "AiDesign", "Authoring", "Drafts", id.ToString("D"), "current.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            (await f.Vm.RequestCloseAsync(default)).ShouldBeFalse();
            await f.Vm.SwitchToDetailedCommand.ExecuteAsync(null);
            f.Vm.Draft.Mode.ShouldBe(AiAuthoringMode.Quick);
            f.Vm.HasUnsavedChanges.ShouldBeTrue(); f.Vm.Error.ShouldNotBeNullOrEmpty();
        }
        (await f.Vm.RequestCloseAsync(default)).ShouldBeTrue();
        await f.Vm.OpenAsync(id, default); Text(f.Vm.Document).ShouldBe("未保存\r\ne\u0301");
    }

    [Fact]
    public async Task Double_close_awaits_one_save()
    {
        var decision = new TaskCompletionSource<AiDraftLeaveChoice>();
        var prompt = new ControlledLeavePrompt { Pending = decision.Task };
        using var f = new PromptWorkbenchFixture(prompt); await f.Vm.OpenAsync(null, default);
        long revision = f.Vm.Draft!.Revision; f.Vm.Document = Document("one durable save");
        var first = f.Vm.RequestCloseAsync(default); var second = f.Vm.RequestCloseAsync(default);
        first.IsCompleted.ShouldBeFalse(); second.ShouldBeSameAs(first);
        decision.SetResult(AiDraftLeaveChoice.Save);
        (await first).ShouldBeTrue(); (await second).ShouldBeTrue();
        prompt.Count.ShouldBe(1); f.Vm.Draft.Revision.ShouldBe(revision + 1);
        (await f.Module.ReadAsync(f.Vm.Draft.Id, default)).Value.FinalDocument.Parts.ShouldBe(f.Vm.Document.Parts);
    }

    [Fact]
    public async Task Discard_leaves_last_successful_snapshot_and_clean_close_does_not_prompt()
    {
        var prompt = new ControlledLeavePrompt { Choice = AiDraftLeaveChoice.Discard };
        using var f = new PromptWorkbenchFixture(prompt); await f.Vm.OpenAsync(null, default);
        (await f.Vm.RequestCloseAsync(default)).ShouldBeTrue(); prompt.Count.ShouldBe(0);
        var saved = f.Vm.Draft!; f.Vm.Document = Document("discard this edit");
        (await f.Vm.RequestCloseAsync(default)).ShouldBeTrue(); prompt.Count.ShouldBe(1);
        (await f.Module.ReadAsync(saved.Id, default)).Value.FinalDocument.Parts.ShouldBe(saved.FinalDocument.Parts);
    }

    [Fact]
    public async Task Template_editor_changes_are_protected_from_close()
    {
        var prompt = new ControlledLeavePrompt { Choice = AiDraftLeaveChoice.Cancel };
        using var f = new PromptWorkbenchFixture(prompt); await f.Vm.OpenAsync(null, default);
        await f.Vm.EditTemplateCommand.ExecuteAsync(null); f.Vm.TemplateEditor!.Body += "\r\nextra";
        (await f.Vm.RequestCloseAsync(default)).ShouldBeFalse(); prompt.Count.ShouldBe(1);
        f.Vm.TemplateEditor.Body.ShouldEndWith("\r\nextra");
    }

    [Fact]
    public async Task Restart_lists_saved_drafts_and_recovers_only_the_committed_editor_content()
    {
        var prompt = new ControlledLeavePrompt { Choice = AiDraftLeaveChoice.Cancel };
        using var f = new PromptWorkbenchFixture(prompt); await f.Vm.OpenAsync(null, default);
        f.Vm.Document = Document("last saved ʻĀna\r\n"); await f.Vm.FlushAsync(default);
        var id = f.Vm.Draft!.Id; f.Vm.Document = Document("lost during process failure"); f.Vm.Deactivate();
        var restarted = new PrintFlow.App.ViewModels.AiDesign.AiPromptWorkbenchViewModel(f.Module, f.Catalog,
            new PrintFlow.Infrastructure.AiDesign.Authoring.FileAiAuthoringAssetPreview(f.Root.Root), f.Picker, f.Clipboard, prompt);
        await restarted.OpenAsync(null, default); await restarted.ShowSavedDraftsCommand.ExecuteAsync(null);
        var saved = restarted.SavedDrafts.Single(x => x.Id == id);
        restarted.Document = Document("keep this new edit");
        await restarted.OpenSavedDraftCommand.ExecuteAsync(saved);
        Text(restarted.Document).ShouldBe("keep this new edit");
        prompt.Choice = AiDraftLeaveChoice.Discard;
        await restarted.OpenSavedDraftCommand.ExecuteAsync(saved);
        restarted.Draft!.Id.ShouldBe(id); Text(restarted.Document).ShouldBe("last saved ʻĀna\r\n");
    }

    [Fact]
    public async Task Template_popup_cancel_and_save_preserve_or_publish_template_edits()
    {
        var prompt = new ControlledLeavePrompt { Choice = AiDraftLeaveChoice.Cancel };
        using var f = new PromptWorkbenchFixture(prompt); await f.Vm.OpenAsync(null, default);
        await f.Vm.EditTemplateCommand.ExecuteAsync(null); var editor = f.Vm.TemplateEditor!; editor.Body += "\r\nextra";
        await f.Vm.CloseTemplateCommand.ExecuteAsync(null); f.Vm.TemplateEditor.ShouldBeSameAs(editor);
        prompt.Choice = AiDraftLeaveChoice.Save;
        await f.Vm.CloseTemplateCommand.ExecuteAsync(null); f.Vm.TemplateEditor.ShouldBeSameAs(editor);
        editor.PreviewConfirmed = true;
        await f.Vm.CloseTemplateCommand.ExecuteAsync(null); f.Vm.TemplateEditor.ShouldBeNull();
        (await f.Catalog.ListAsync(default)).Value.ShouldContain(x => x.Content.Body.EndsWith("\r\nextra"));
    }

    [Fact]
    public async Task One_locked_saved_draft_does_not_hide_healthy_choices_and_reports_partial_discovery()
    {
        using var f = new PromptWorkbenchFixture(); await f.Vm.OpenAsync(null, default); var lockedId = f.Vm.Draft!.Id;
        await f.Vm.OpenAsync(null, default); var healthyId = f.Vm.Draft!.Id;
        using var block = new FileStream(Pointer(f, lockedId), FileMode.Open, FileAccess.Read, FileShare.None);
        await f.Vm.ShowSavedDraftsCommand.ExecuteAsync(null);
        f.Vm.SavedDrafts.ShouldContain(x => x.Id == healthyId);
        f.Vm.SavedDraftsNotice.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Saved_choices_distinguish_names_with_identical_final_prose_and_stable_identity()
    {
        using var f = new PromptWorkbenchFixture(); await f.Vm.OpenAsync(null, default);
        f.Vm.Fields.Single(x => x.Key == "姓名").Value = "ʻĀna"; await f.Vm.FlushAsync(default); var first = f.Vm.Draft!.Id;
        await f.Vm.OpenAsync(null, default);
        f.Vm.Fields.Single(x => x.Key == "姓名").Value = "陈"; await f.Vm.FlushAsync(default); var second = f.Vm.Draft!.Id;
        await f.Vm.ShowSavedDraftsCommand.ExecuteAsync(null);
        var a = f.Vm.SavedDrafts.Single(x => x.Id == first); var b = f.Vm.SavedDrafts.Single(x => x.Id == second);
        a.DisplayLabel.ShouldContain("ʻĀna"); b.DisplayLabel.ShouldContain("陈");
        a.DisplayLabel.ShouldNotBe(b.DisplayLabel);
    }

    [Fact]
    public async Task Unavailable_template_library_still_exposes_readable_saved_drafts()
    {
        using var f = new PromptWorkbenchFixture(); await f.Vm.OpenAsync(null, default);
        f.Vm.Document = Document("recover without a new write"); await f.Vm.FlushAsync(default); var id = f.Vm.Draft!.Id;
        var restarted = new AiPromptWorkbenchViewModel(f.Module, f.Catalog, new PrintFlow.Infrastructure.AiDesign.Authoring.FileAiAuthoringAssetPreview(f.Root.Root), f.Picker, f.Clipboard);
        using var block = new FileStream(Path.Combine(f.Root.Root, "AiDesign", "Authoring", "Templates", "current.json"), FileMode.Open, FileAccess.Read, FileShare.None);
        await restarted.OpenAsync(null, default); restarted.IsSavedDraftsVisible.ShouldBeTrue();
        await restarted.OpenSavedDraftCommand.ExecuteAsync(restarted.SavedDrafts.Single(x => x.Id == id));
        restarted.Draft!.Id.ShouldBe(id); Text(restarted.Document).ShouldBe("recover without a new write");
    }

    [Fact]
    public async Task Dirty_template_cannot_be_replaced_by_a_background_command()
    {
        var prompt = new ControlledLeavePrompt { Choice = AiDraftLeaveChoice.Cancel };
        using var f = new PromptWorkbenchFixture(prompt); await f.Vm.OpenAsync(null, default); await f.Vm.EditTemplateCommand.ExecuteAsync(null);
        var editor = f.Vm.TemplateEditor!; editor.Body += " keep me";
        var render = WpfRendering.Render(() => new AiPromptWorkbenchView { DataContext = f.Vm }, new Size(1000, 700), tree =>
            tree.OfType<Button>().Where(x => ReferenceEquals(x.Command, f.Vm.EditTemplateCommand) || ReferenceEquals(x.Command, f.Vm.SaveInstanceTemplateCommand)).All(x => !x.IsEnabled));
        render.Facts.ShouldBeTrue(); render.BindingErrors.ShouldBeEmpty();
        await f.Vm.EditTemplateCommand.ExecuteAsync(null); f.Vm.TemplateEditor.ShouldBeSameAs(editor);
        await f.Vm.SaveInstanceTemplateCommand.ExecuteAsync(null); f.Vm.TemplateEditor.ShouldBeSameAs(editor);
        prompt.Count.ShouldBe(2); editor.Body.ShouldEndWith(" keep me");
    }

    [Fact]
    public async Task Chooser_failure_is_visible()
    {
        using var f = new PromptWorkbenchFixture(); await f.Vm.OpenAsync(null, default); var first = f.Vm.Draft!.Id;
        await f.Vm.OpenAsync(null, default); f.Vm.ReferenceRole = "customer one secret"; await f.Vm.FlushAsync(default);
        await f.Vm.ShowSavedDraftsCommand.ExecuteAsync(null); var row = f.Vm.SavedDrafts.Single(x => x.Id == first);
        using (var block = new FileStream(Pointer(f, first), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await f.Vm.OpenSavedDraftCommand.ExecuteAsync(row); f.Vm.Error.ShouldNotBeNullOrEmpty();
            var render = WpfRendering.Render(() => new AiPromptWorkbenchView { DataContext = f.Vm }, new Size(1000, 700), tree =>
                tree.OfType<TextBlock>().Any(x => AutomationProperties.GetAutomationId(x) == "AiPrompt.SavedDraftError" && x.Text == f.Vm.Error && x.Visibility == Visibility.Visible));
            render.Facts.ShouldBeTrue();
            if (Environment.GetEnvironmentVariable("PRINTFLOW_A7_CAPTURE") is { Length: > 0 } capture)
                WpfRendering.CapturePng(() => new AiPromptWorkbenchView { DataContext = f.Vm }, new Size(1000, 700), Path.Combine(capture, "saved-chooser-error-1000x700.png"));
        }
    }

    [Fact]
    public async Task Recovering_another_draft_clears_previous_role_and_status()
    {
        using var f = new PromptWorkbenchFixture(); await f.Vm.OpenAsync(null, default); var first = f.Vm.Draft!.Id;
        await f.Vm.OpenAsync(null, default); f.Vm.ReferenceRole = "customer one secret"; await f.Vm.FlushAsync(default);
        await f.Vm.ShowSavedDraftsCommand.ExecuteAsync(null);
        await f.Vm.OpenSavedDraftCommand.ExecuteAsync(f.Vm.SavedDrafts.Single(x => x.Id == first));
        f.Vm.ReferenceRole.ShouldBeEmpty(); f.Vm.Status.ShouldBeNull();
    }

    private static string Pointer(PromptWorkbenchFixture f, Guid id) => Path.Combine(f.Root.Root, "AiDesign", "Authoring", "Drafts", id.ToString("D"), "current.json");
    private static AiPromptDocument Document(string text) => new([new AiTextPart(Guid.NewGuid(), text)]);
    private static string Text(AiPromptDocument document) => string.Concat(document.Parts.OfType<AiTextPart>().Select(x => x.Text));
}

internal sealed class ControlledLeavePrompt : IAiDraftLeavePrompt
{
    public AiDraftLeaveChoice Choice { get; set; } = AiDraftLeaveChoice.Save;
    public Task<AiDraftLeaveChoice>? Pending { get; set; }
    public int Count { get; private set; }
    public Task<AiDraftLeaveChoice> ChooseAsync(CancellationToken ct) { Count++; return Pending ?? Task.FromResult(Choice); }
}
