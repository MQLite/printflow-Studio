using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PrintFlow.App.Navigation;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Delivery;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Delivery;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedCompletionSaveTests
{
    [Fact]
    public async Task Approved_png_preparation_is_part_of_the_save_step_without_becoming_a_delivery()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        var workflow = h.CreateService();
        var review = await FinalSaveFixtures.PngAtFinalReviewAsync(h, workflow);
        var delivery = new ObservedDeliveryService(FinalSaveFixtures.Delivery(h));
        var model = await Open(h, workflow, delivery, new Picker(null), review);
        await model.ApproveCommand.ExecuteAsync(null);
        await model.PreviewsLoaded; await model.FinalSaveFactsLoaded;
        model.GuidedPage.ShouldBe(GuidedSessionPage.Save,
            "the material route proceeds from final review into approved-PNG preparation and save");
        model.CanRunStep.ShouldBeTrue();
        model.CanSaveApproved.ShouldBeFalse();
        model.CanComplete.ShouldBeFalse();
        delivery.DeliverCalls.ShouldBe(0);
        var facts = Texts(model);
        facts.GetValueOrDefault("Session.Completion.Approval").ShouldBe("已批准 · 等待准备 PNG");
        facts.GetValueOrDefault("Session.Completion.Workflow").ShouldBe("任务尚未完成");
    }

    [Fact]
    public async Task Save_destination_and_name_are_visible_in_the_current_step_at_minimum_size()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        var workflow = FinalSaveFixtures.TiffService(h);
        var review = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
        var model = await Open(h, workflow, FinalSaveFixtures.Delivery(h), new Picker(null), review);
        await model.ApproveCommand.ExecuteAsync(null);
        await model.PreviewsLoaded; await model.FinalSaveFactsLoaded;
        var result = WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model },
            new Size(1000, 700), tree =>
            {
                try
                {
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
                    tree.Root.UpdateLayout();
                    var all = Descendants(tree.Root).OfType<FrameworkElement>().ToArray();
                    return new[] { "Session.FinalSave.FileName", "Session.FinalSave.Folder", "Session.FinalSave.ChangeLocation" }
                        .Select(id =>
                        {
                            var item = all.Single(e => AutomationProperties.GetAutomationId(e) == id);
                            var scroll = all.OfType<ScrollViewer>().First(s => s.IsAncestorOf(item));
                            var bounds = item.TransformToAncestor(scroll).TransformBounds(new Rect(item.RenderSize));
                            return (id, Shown: GuidedSessionProjectionTests.Shown(item), Fits: bounds.Top >= 0 && bounds.Bottom <= scroll.ViewportHeight + 0.5);
                        }).ToArray();
                }
                finally { tree.Root.DataContext = null; tree.Root.UpdateLayout(); }
            });
        result.Facts.ShouldAllBe(f => f.Shown && f.Fits,
            "the save step must expose the filename and destination without first scrolling past explanation");
    }

    // Catches a completion summary that equates approval, a recorded save, or completion.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Approved_result_enters_save_without_claiming_delivery(bool saved, bool completed)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        var workflow = FinalSaveFixtures.TiffService(h);
        var review = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
        string folder = FinalSaveFixtures.NewFolder("e5-states");
        try
        {
            var delivery = new ObservedDeliveryService(FinalSaveFixtures.Delivery(h));
            var model = await Open(h, workflow, delivery, new Picker(folder), review);
            if (saved)
            {
                model.ChangeLocationCommand.Execute(null);
                await model.ConfirmAndSaveCommand.ExecuteAsync(null);
            }
            else await model.ApproveCommand.ExecuteAsync(null);
            await model.FinalSaveFactsLoaded;
            if (completed) { await model.CompleteCommand.ExecuteAsync(null); await model.FinalSaveFactsLoaded; }
            model.GuidedPage.ShouldBe(completed ? GuidedSessionPage.Completed : GuidedSessionPage.Save);
            delivery.DeliverCalls.ShouldBe(saved ? 1 : 0);
            var facts = Texts(model);
            facts.GetValueOrDefault("Session.Completion.Approval").ShouldBe("结果已批准");
            facts.GetValueOrDefault("Session.Completion.Delivery").ShouldBe(saved ? "已核实保存" : "尚未保存到交付文件夹");
            facts.GetValueOrDefault("Session.Completion.Workflow").ShouldBe(completed ? "任务已完成" : "任务尚未完成");
            facts.GetValueOrDefault("Session.Completion.Notice").ShouldNotBeNullOrWhiteSpace();
            model.CanSaveApproved.ShouldBeFalse("without a destination or after the same save, no extra delivery is offered");
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    // Catches a selected item's state leaking into an older size's row.
    [Fact]
    public async Task Each_size_retains_its_own_review_and_delivery_summary()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        var workflow = FinalSaveFixtures.TiffService(h);
        var first = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
        string folder = FinalSaveFixtures.NewFolder("e5-sizes");
        try
        {
            var delivery = FinalSaveFixtures.Delivery(h);
            var saved = await new FinalSaveCoordinator(workflow, delivery, "tester").ConfirmAndSaveAsync(
                FinalSaveFixtures.Confirm(first, folder, "first.tif"), null, default);
            saved.Delivery!.Code.ShouldBe(DeliveryCode.Delivered);
            (await workflow.ExecuteAsync(first.Id, new WorkflowCommand.Complete(), "tester", default)).IsSuccess.ShouldBeTrue();
            (await workflow.ExecuteAsync(first.Id, new WorkflowCommand.AddAnotherSize(), "tester", default)).IsSuccess.ShouldBeTrue();
            var second = await FinalSaveFixtures.NextTiffSizeAsync(workflow, first.Id, 250);
            var model = await Open(h, workflow, delivery, new Picker(null), second);
            model.FinalSaveTargets.Count.ShouldBe(2);
            var rows = model.FinalSaveTargets.Select(row => new
            {
                row.Key,
                Delivery = row.GetType().GetProperty("DeliveryStatus")?.GetValue(row) as string,
            }).ToArray();
            rows.Single(r => r.Key == model.SelectedFinalSaveTarget!.Key).Delivery.ShouldBe("尚未批准");
            rows.Single(r => r.Key != model.SelectedFinalSaveTarget!.Key).Delivery.ShouldBe("有保存记录，当前文件待核实");
            model.CanApprove.ShouldBeTrue();
            model.CanOpenContainingFolder.ShouldBeFalse("the pending size is not the older delivered artifact");
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    [Fact]
    public async Task Saving_is_a_transient_state_and_does_not_claim_delivery_before_acknowledgment()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        var workflow = FinalSaveFixtures.TiffService(h);
        var review = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
        string folder = FinalSaveFixtures.NewFolder("e5-working");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? pending = null;
        try
        {
            var delivery = new ObservedDeliveryService(FinalSaveFixtures.Delivery(h)) { HoldDelivery = release };
            var model = await Open(h, workflow, delivery, new Picker(folder), review);
            model.ChangeLocationCommand.Execute(null);
            pending = model.ConfirmAndSaveCommand.ExecuteAsync(null);
            await delivery.DeliveryEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            model.IsFinalSaveWorking.ShouldBeTrue();
            (model.GetType().GetProperty("GuidedDeliveryState")?.GetValue(model) as string).ShouldBe("正在保存或核实，请等待");
            model.CanSaveApproved.ShouldBeFalse();
            model.CanComplete.ShouldBeFalse();
        }
        finally
        {
            release.TrySetResult(); if (pending is not null) await pending;
            FinalSaveFixtures.Remove(folder);
        }
    }

    // Catches an uncertainty or collision being summarized as saved after internal approval.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Uncertain_collision_and_picker_cancel_keep_their_real_save_state(bool uncertain)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        var workflow = FinalSaveFixtures.TiffService(h);
        var review = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
        string folder = FinalSaveFixtures.NewFolder("e5-adverse");
        try
        {
            var delivery = FinalSaveFixtures.Delivery(h,
                uncertain ? new LostPublicationAckFileSystem(new WindowsDeliveryFileSystem()) : null);
            var picker = new Picker(folder);
            var model = await Open(h, workflow, delivery, picker, review);
            model.SaveFileNameText = "result.tif";
            model.ChangeLocationCommand.Execute(null);
            string? identity = model.ConfirmAndSaveIdentity;
            picker.Next = null;
            model.ChangeLocationCommand.Execute(null);
            model.ConfirmAndSaveIdentity.ShouldBe(identity);
            if (!uncertain) File.WriteAllBytes(Path.Combine(folder, "result.tif"), [1, 2, 3]);
            await model.ConfirmAndSaveCommand.ExecuteAsync(null);
            await model.FinalSaveFactsLoaded;
            var facts = Texts(model);
            facts.GetValueOrDefault("Session.Completion.Approval").ShouldBe("结果已批准");
            facts.GetValueOrDefault("Session.Completion.Delivery").ShouldBe(uncertain ? "保存结果待核实" : "同名冲突，尚未保存");
            facts.GetValueOrDefault("Session.Completion.Workflow").ShouldBe("任务尚未完成");
            model.CanCheckSaveAgain.ShouldBe(uncertain);
            model.CanUseSuggestedName.ShouldBe(!uncertain);
            model.CanOpenContainingFolder.ShouldBeFalse();
            if (!uncertain) File.ReadAllBytes(Path.Combine(folder, "result.tif")).ShouldBe([1, 2, 3]);
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    internal static async Task<SessionViewModel> Open(SessionServiceHarness h, ISessionService workflow,
        IApprovedArtifactDeliveryService delivery, IDeliveryFolderPicker picker, SessionView view)
    {
        var model = new SessionViewModel(workflow, h.Previews, h.TiffReviews, new RecordingNavigation(),
            finalSave: new FinalSaveCoordinator(workflow, delivery, "tester"), folderPicker: picker);
        model.Open(view); await model.PreviewsLoaded; await model.FinalSaveFactsLoaded;
        return model;
    }

    internal static Dictionary<string, string> Texts(SessionViewModel model) => WpfRendering.RenderExpectingNoBindingErrors(
        () => new SessionScreenView { DataContext = model }, new Size(1000, 700), tree =>
        {
            try
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
                tree.Root.UpdateLayout();
                return Descendants(tree.Root).OfType<TextBlock>()
                    .Where(t => GuidedSessionProjectionTests.Shown(t) && AutomationProperties.GetAutomationId(t).StartsWith("Session.Completion."))
                    .ToDictionary(AutomationProperties.GetAutomationId, t => t.Text);
            }
            finally { tree.Root.DataContext = null; tree.Root.UpdateLayout(); }
        }).Facts;

    internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }

    internal sealed class Picker(string? next) : IDeliveryFolderPicker
    {
        public string? Next { get; set; } = next;
        public string? PickFolder(string title, string? initialFolder) => Next;
    }
}
