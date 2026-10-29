using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedSessionReviewFixTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Draft_modal_blocks_retained_and_fresh_crop_entry_without_losing_the_selection(bool discard)
    {
        using HomeScreenHarness h = new();
        var imported = (await h.Sessions.ImportAsync(WorkflowType.PrepareAsset,
            h.Inner.WriteOpaqueSourcePng(), "modal-crop", "tester", default)).Value;
        var m = h.Session(h.Navigation); m.Open(imported);
        await m.ConfirmOriginalCommand.ExecuteAsync(null);
        await m.SkipCommand.ExecuteAsync(null); await m.SkipCommand.ExecuteAsync(null);
        await m.RunStepCommand.ExecuteAsync(null); await m.PreviewsLoaded;
        m.BeginManualCropCommand.Execute(null);
        var layout = new CropSurfaceLayout(12, 10, 12, 10, 12, 10, m.IsFitToViewport, m.ZoomScale);
        m.TrySetCropSelection(layout, 3, 2, 9, 7).ShouldBeTrue();
        var selection = m.CropSelection;
        string persisted = JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(m.Id, default)).Value);
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = m }, new Size(1000, 700), tree =>
        {
            var button = GuidedSessionShellTests.Find<Button>(tree, "Session.BeginManualCrop");
            var retained = GuidedSessionInputTests.Provider(button);
            m.BackToHomeCommand.Execute(null);
            m.IsConfirmingDraftLeave.ShouldBeTrue();
            tree.Root.UpdateLayout();
            bool enabledDuringPrompt = button.IsEnabled;
            retained.Invoke();
            GuidedSessionInputTests.Provider(button).Invoke();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
            m.BeginManualCropCommand.Execute(null); // Command callers obey the same modal boundary.
            m.CancelManualCropCommand.Execute(null);
            m.TrySetCropSelection(layout, 1, 1, 10, 9).ShouldBeFalse();
            m.CropSelection.ShouldBe(selection);
            enabledDuringPrompt.ShouldBeFalse();
            if (discard) m.DiscardDraftAndLeaveCommand.Execute(null);
            else m.KeepDraftCommand.Execute(null);
            m.IsConfirmingDraftLeave.ShouldBeFalse();
            m.IsCropping.ShouldBe(!discard);
            m.CropSelection.ShouldBe(discard ? null : selection);
            return true;
        });
        JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(m.Id, default)).Value).ShouldBe(persisted);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Reconfirming_white_ink_finishes_only_a_successful_edit(bool changeBranch, bool refuse)
    {
        using HomeScreenHarness h = new();
        var (m, fault) = await Open(h, WorkflowType.GeneratePrintTiff);
        await m.ConfirmOriginalCommand.ExecuteAsync(null);
        m.WidthMmText = "200"; m.HeightMmText = "150";
        await m.SetMaximumBoundsCommand.ExecuteAsync(null);
        m.SelectedWhiteUnderbaseChoice = m.WhiteUnderbaseChoices.Single(c => c.Branch == WhiteUnderbaseBranch.W1_1px);
        await m.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
        m.GuidedPage.ShouldBe(GuidedSessionPage.Preflight);
        m.ChangeGuidedWhiteInkCommand.Execute(null);
        var branch = changeBranch ? WhiteUnderbaseBranch.W1_2px : WhiteUnderbaseBranch.W1_1px;
        m.SelectedWhiteUnderbaseChoice = m.WhiteUnderbaseChoices.Single(c => c.Branch == branch);
        fault.Refuse = refuse;
        string before = JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(m.Id, default)).Value);
        await m.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
        m.GuidedPage.ShouldBe(refuse ? GuidedSessionPage.WhiteInk : GuidedSessionPage.Preflight);
        m.ShowsGuidedRunStep.ShouldBe(!refuse);
        if (refuse)
        {
            m.SelectedWhiteUnderbaseChoice!.Branch.ShouldBe(branch);
            JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(m.Id, default)).Value).ShouldBe(before);
        }
        else (await h.Sessions.LoadAsync(m.Id, default)).Value.WhiteUnderbaseBranch.ShouldBe(branch);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Applied_trim_becomes_the_navigation_baseline_without_erasing_later_edits(bool laterEdit)
    {
        using HomeScreenHarness h = new();
        var (m, _) = await Open(h, WorkflowType.PrepareAsset);
        await m.ConfirmOriginalCommand.ExecuteAsync(null);
        await m.SkipCommand.ExecuteAsync(null); await m.SkipCommand.ExecuteAsync(null);
        m.SelectedTrimMode = m.TrimModes.Single(c => c.Mode == TrimMode.UniformMargin);
        m.UniformMarginText = "2";
        await m.ApplyTrimMarginCommand.ExecuteAsync(null);
        string saved = JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(m.Id, default)).Value);
        if (laterEdit)
        {
            m.UniformMarginText = "3";
            m.Open((await h.Sessions.LoadAsync(m.Id, default)).Value);
            m.UniformMarginText.ShouldBe("3");
        }
        m.ViewHistoryCommand.Execute(m.Steps.First());
        m.IsConfirmingDraftLeave.ShouldBe(laterEdit);
        if (laterEdit) await m.DiscardDraftAndLeaveCommand.ExecuteAsync(null);
        m.IsViewingHistory.ShouldBeTrue();
        m.CloseHistoryCommand.Execute(null);
        m.SelectedTrimMode.Mode.ShouldBe(TrimMode.UniformMargin);
        m.UniformMarginText.ShouldBe("2");
        (await h.Sessions.LoadAsync(m.Id, default)).Value.TrimMargin.ShouldBe(TrimMargin.Uniform(2));
        JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(m.Id, default)).Value).ShouldBe(saved);
    }

    [Fact]
    public async Task Refused_trim_keeps_the_draft_and_does_not_move_the_confirmed_baseline()
    {
        using HomeScreenHarness h = new();
        var (m, fault) = await Open(h, WorkflowType.PrepareAsset);
        await m.ConfirmOriginalCommand.ExecuteAsync(null);
        await m.SkipCommand.ExecuteAsync(null); await m.SkipCommand.ExecuteAsync(null);
        string before = JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(m.Id, default)).Value);
        m.SelectedTrimMode = m.TrimModes.Single(c => c.Mode == TrimMode.UniformMargin);
        m.UniformMarginText = "2";
        fault.Refuse = true;
        await m.ApplyTrimMarginCommand.ExecuteAsync(null);
        m.UniformMarginText.ShouldBe("2");
        m.ViewHistoryCommand.Execute(m.Steps.First());
        m.IsConfirmingDraftLeave.ShouldBeTrue();
        await m.DiscardDraftAndLeaveCommand.ExecuteAsync(null);
        m.CloseHistoryCommand.Execute(null);
        m.SelectedTrimMode.Mode.ShouldBe(TrimMode.TightCrop);
        m.UniformMarginText.ShouldBe("0");
        JsonSerializer.Serialize((await h.Inner.Repository.LoadAsync(m.Id, default)).Value).ShouldBe(before);
    }

    private static async Task<(SessionViewModel Model, RefusingService Fault)> Open(HomeScreenHarness h, WorkflowType route)
    {
        var session = (await h.Sessions.ImportAsync(route, h.Inner.WriteBorderedSourcePng(), "review-fix", "tester", default)).Value;
        var service = DispatchProxy.Create<ISessionService, RefusingService>();
        var fault = (RefusingService)(object)service; fault.Inner = h.Sessions;
        var model = new SessionViewModel(service, h.Previews, h.TiffReviews, h.Navigation);
        model.Open(session); await model.PreviewsLoaded;
        return (model, fault);
    }

    public class RefusingService : DispatchProxy
    {
        public ISessionService Inner { get; set; } = null!;
        public bool Refuse { get; set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            Refuse && method!.Name == nameof(ISessionService.ExecuteAsync)
                ? Task.FromResult(OperationResult<SessionView>.Failed(FailureCode.EnvironmentNotVerified, "Controlled review regression refusal"))
                : method!.Invoke(Inner, args);
    }
}
