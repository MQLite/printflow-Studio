using System.Windows.Input;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.Persistence;
using PrintFlow.Workflow.Services;
using PrintFlow.Workflow.Delivery;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedSessionNavigationTests
{
    [Fact]
    public async Task Return_confirmation_cannot_be_retargeted_or_reused()
    {
        using HomeScreenHarness h = new();
        var m = await GuidedSessionProjectionTests.Open(h, WorkflowType.PrepareAsset);
        await m.ConfirmOriginalCommand.ExecuteAsync(null);
        await m.SkipCommand.ExecuteAsync(null);
        await m.SkipCommand.ExecuteAsync(null);
        var before = (await h.Inner.Repository.LoadAsync(m.Id, default)).Value!;
        m.SelectedReturnTarget = m.ReturnTargets.First();
        m.BeginReturnCommand.Execute(null);
        m.SelectedReturnTarget = m.ReturnTargets.Last();
        await m.ConfirmReturnCommand.ExecuteAsync(null);
        var after = (await h.Inner.Repository.LoadAsync(m.Id, default)).Value!;
        after.Steps.ShouldBe(before.Steps);
        m.BeginReturnCommand.Execute(null);
        m.CancelReturnCommand.Execute(null);
        await m.ConfirmReturnCommand.ExecuteAsync(null);
        (await h.Inner.Repository.LoadAsync(m.Id, default)).Value!.Steps.ShouldBe(before.Steps);
    }

    [Fact]
    public async Task History_is_readonly_and_cannot_approve_the_live_result()
    {
        using HomeScreenHarness h = new();
        var m = await GuidedSessionProjectionTests.Open(h, WorkflowType.PrepareAsset);
        await m.ConfirmOriginalCommand.ExecuteAsync(null);
        await m.RunStepCommand.ExecuteAsync(null);
        var before = (await h.Inner.Repository.LoadAsync(m.Id, default)).Value!;
        Command(m, "ViewHistoryCommand").Execute(m.Steps.First());
        Read<bool>(m, "IsViewingHistory").ShouldBeTrue();
        await m.ApproveCommand.ExecuteAsync(null);
        var after = (await h.Inner.Repository.LoadAsync(m.Id, default)).Value!;
        after.Steps.ShouldBe(before.Steps);
        after.Reviews.ShouldBe(before.Reviews);
        after.Revisions.ShouldBe(before.Revisions);
        Command(m, "CloseHistoryCommand").Execute(null);
        m.CanApprove.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Dirty_dimensions_require_keep_or_discard_before_home(bool discard)
    {
        using HomeScreenHarness h = new();
        var nav = new RecordingNavigation();
        var source = h.Inner.WriteBorderedSourcePng("draft.png");
        var imported = (await h.Sessions.ImportAsync(WorkflowType.GeneratePrintTiff, source, "draft", "tester", default)).Value;
        var m = h.Session(nav);
        m.Open(imported);
        await m.ConfirmOriginalCommand.ExecuteAsync(null);
        m.WidthMmText = "321";
        await m.BackToHomeCommand.ExecuteAsync(null);
        nav.GoHomeCount.ShouldBe(0);
        Read<bool>(m, "IsConfirmingDraftLeave").ShouldBeTrue();
        if (discard)
        {
            await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)Command(m, "DiscardDraftAndLeaveCommand")).ExecuteAsync(null);
            nav.GoHomeCount.ShouldBe(1);
            m.WidthMmText.ShouldNotBe("321");
        }
        else
        {
            Command(m, "KeepDraftCommand").Execute(null);
            m.WidthMmText.ShouldBe("321");
            nav.GoHomeCount.ShouldBe(0);
        }
        (await h.Sessions.LoadAsync(m.Id, default)).Value.Dimensions.ShouldBeNull();
    }

    [Fact]
    public async Task Same_context_reload_preserves_dimension_draft()
    {
        using HomeScreenHarness h = new();
        var m = await GuidedSessionProjectionTests.Open(h, WorkflowType.GeneratePrintTiff);
        await m.ConfirmOriginalCommand.ExecuteAsync(null);
        m.WidthMmText = "321";
        m.ChooseCustomSizeCommand.Execute(null);
        m.CustomMillimetresText = "199";
        m.Open((await h.Sessions.LoadAsync(m.Id, default)).Value);
        m.WidthMmText.ShouldBe("321");
        m.CustomMillimetresText.ShouldBe("199");
        m.IsChoosingCustomSize.ShouldBeTrue();
    }

    [Fact]
    public async Task Return_capture_does_not_survive_opening_another_task()
    {
        using HomeScreenHarness h = new();
        var m = await GuidedSessionProjectionTests.Open(h, WorkflowType.PrepareAsset);
        await m.ConfirmOriginalCommand.ExecuteAsync(null);
        m.SelectedReturnTarget = m.ReturnTargets.First();
        m.BeginReturnCommand.Execute(null);
        m.IsConfirmingReturn.ShouldBeTrue();
        var second = (await h.Sessions.ImportAsync(WorkflowType.GeneratePrintTiff,
            h.Inner.WriteBorderedSourcePng("second.png"), "second", "tester", default)).Value;
        m.Open(second);
        await m.ConfirmReturnCommand.ExecuteAsync(null);
        m.Id.ShouldBe(second.Id);
        m.IsConfirmingReturn.ShouldBeFalse();
        (await h.Sessions.LoadAsync(second.Id, default)).Value.Steps.ShouldBe(second.Steps);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task White_ink_draft_keep_and_discard_never_record_a_default(bool discard)
    {
        using HomeScreenHarness h = new();
        var imported = (await h.Sessions.ImportAsync(WorkflowType.GeneratePrintTiff,
            h.Inner.WriteBorderedSourcePng("white-draft.png"), "white", "tester", default)).Value;
        var nav = new RecordingNavigation();
        var m = h.Session(nav); m.Open(imported);
        await m.ConfirmOriginalCommand.ExecuteAsync(null);
        m.WidthMmText = "200"; m.HeightMmText = "150";
        await m.SetMaximumBoundsCommand.ExecuteAsync(null);
        m.SelectedWhiteUnderbaseChoice = m.WhiteUnderbaseChoices.Single(c => c.Branch == WhiteUnderbaseBranch.W1_1px);
        string before = JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(m.Id, default)).Value);
        await Leave(m, nav, discard);
        if (discard) m.SelectedWhiteUnderbaseChoice.ShouldBeNull();
        else m.SelectedWhiteUnderbaseChoice!.Branch.ShouldBe(WhiteUnderbaseBranch.W1_1px);
        JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(m.Id, default)).Value).ShouldBe(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Correction_note_keep_and_discard_are_transient(bool discard)
    {
        using SessionServiceHarness h = new();
        var service = CorrectionFixtures.Service(h);
        var review = await CorrectionFixtures.AtBackgroundRemovalReviewAsync(h, service);
        var nav = new RecordingNavigation();
        var m = new SessionViewModel(service, h.Previews, h.TiffReviews, nav);
        m.Open(review);
        m.BeginAskColleagueCommand.Execute(null);
        m.CorrectionNote = "请保留左边细线";
        string before = JsonSerializer.Serialize((await h.Repository.LoadAsync(m.Id, default)).Value);
        await Leave(m, nav, discard);
        if (discard) { m.CorrectionNote.ShouldBeNull(); m.IsAskingColleague.ShouldBeFalse(); }
        else { m.CorrectionNote.ShouldBe("请保留左边细线"); m.IsAskingColleague.ShouldBeTrue(); }
        JsonSerializer.Serialize((await h.Repository.LoadAsync(m.Id, default)).Value).ShouldBe(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manual_crop_selection_survives_reload_and_keep_but_discard_writes_nothing(bool discard)
    {
        using HomeScreenHarness h = new();
        var imported = (await h.Sessions.ImportAsync(WorkflowType.PrepareAsset,
            h.Inner.WriteOpaqueSourcePng("crop-draft.png"), "crop", "tester", default)).Value;
        var nav = new RecordingNavigation(); var m = h.Session(nav); m.Open(imported);
        await m.ConfirmOriginalCommand.ExecuteAsync(null);
        await m.SkipCommand.ExecuteAsync(null); await m.SkipCommand.ExecuteAsync(null);
        await m.RunStepCommand.ExecuteAsync(null);
        await m.PreviewsLoaded;
        m.BeginManualCropCommand.Execute(null);
        m.TrySetCropSelection(new CropSurfaceLayout(12, 10, 12, 10, 12, 10, m.IsFitToViewport, m.ZoomScale), 3, 2, 9, 7).ShouldBeTrue();
        var selection = m.CropSelection;
        m.Open((await h.Sessions.LoadAsync(m.Id, default)).Value);
        m.CropSelection.ShouldBe(selection);
        string before = JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(m.Id, default)).Value);
        await Leave(m, nav, discard);
        if (discard) { m.CropSelection.ShouldBeNull(); m.IsCropping.ShouldBeFalse(); }
        else { m.CropSelection.ShouldBe(selection); m.IsCropping.ShouldBeTrue(); }
        JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(m.Id, default)).Value).ShouldBe(before);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Final_save_draft_keep_or_discard_preserves_review_and_delivery_facts(bool discard)
    {
        using SessionServiceHarness h = new();
        var service = h.CreateService();
        var review = await FinalSaveFixtures.PngAtFinalReviewAsync(h, service);
        var nav = new RecordingNavigation();
        var m = new SessionViewModel(service, h.Previews, h.TiffReviews, nav, finalSave:
            new FinalSaveCoordinator(service, FinalSaveFixtures.Delivery(h), "tester"));
        m.Open(review); await m.PreviewsLoaded; await m.FinalSaveFactsLoaded;
        string originalName = m.SaveFileNameText;
        m.SaveFileNameText = "unfinished-name";
        string before = JsonSerializer.Serialize((await h.Repository.LoadAsync(m.Id, default)).Value);
        await Leave(m, nav, discard);
        m.SaveFileNameText.ShouldBe(discard ? originalName : "unfinished-name");
        JsonSerializer.Serialize((await h.Repository.LoadAsync(m.Id, default)).Value).ShouldBe(before);
    }

    [Fact]
    public async Task History_and_draft_dialog_render_without_binding_errors_and_hide_live_actions()
    {
        using HomeScreenHarness h = new();
        var m = await GuidedSessionProjectionTests.Open(h, WorkflowType.PrepareAsset);
        await m.ConfirmOriginalCommand.ExecuteAsync(null);
        m.ViewHistoryCommand.Execute(m.Steps.First());
        await m.HistoryPreviewLoaded;
        m.HistoryFacts.ShouldNotBeEmpty();
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = m }, new Size(1000, 700), tree =>
        {
            GuidedSessionProjectionTests.Shown(GuidedSessionShellTests.Find<FrameworkElement>(tree, "Session.HistoryWorkspace")).ShouldBeTrue();
            GuidedSessionProjectionTests.Shown(GuidedSessionShellTests.Find<FrameworkElement>(tree, "Session.CurrentWorkspace")).ShouldBeFalse();
            GuidedSessionProjectionTests.Shown(GuidedSessionShellTests.Find<FrameworkElement>(tree, "Session.ActionBar")).ShouldBeFalse();
            return true;
        });
    }

    private static async Task Leave(SessionViewModel m, RecordingNavigation nav, bool discard)
    {
        await m.BackToHomeCommand.ExecuteAsync(null);
        nav.GoHomeCount.ShouldBe(0);
        m.IsConfirmingDraftLeave.ShouldBeTrue();
        if (discard) await m.DiscardDraftAndLeaveCommand.ExecuteAsync(null);
        else m.KeepDraftCommand.Execute(null);
        nav.GoHomeCount.ShouldBe(discard ? 1 : 0);
        m.IsConfirmingDraftLeave.ShouldBeFalse();
    }

    internal static T Read<T>(object target, string property)
    {
        var info = target.GetType().GetProperty(property);
        info.ShouldNotBeNull(property + " must exist");
        return (T)info!.GetValue(target)!;
    }
    internal static ICommand Command(object target, string property) => Read<ICommand>(target, property);
}
