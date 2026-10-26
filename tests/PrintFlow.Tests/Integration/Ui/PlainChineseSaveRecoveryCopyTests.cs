using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Delivery;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Delivery;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class PlainChineseSaveRecoveryCopyTests
{
    public static TheoryData<string, int, int> SaveCases() => Cases("approved", "failed", "uncertain", "collision", "saved", "history");
    public static TheoryData<string, int, int> CorrectionCases() => Cases("prepare", "cancel", "waiting", "returned");

    [Theory]
    [MemberData(nameof(SaveCases))]
    public async Task Save_wording_keeps_approval_file_history_uncertainty_and_completion_distinct(string state, int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        var workflow = FinalSaveFixtures.TiffService(h);
        var review = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
        string folder = FinalSaveFixtures.NewFolder("c04-save");
        try
        {
            var delivery = new ObservedDeliveryService(FinalSaveFixtures.Delivery(h,
                state == "uncertain" ? new LostPublicationAckFileSystem(new WindowsDeliveryFileSystem()) : null));
            var picker = new GuidedCompletionSaveTests.Picker(state == "failed" ? Path.Combine(folder, "unavailable") : folder);
            var model = await GuidedCompletionSaveTests.Open(h, workflow, delivery, picker, review);
            model.SaveFileNameText = "result.tif";
            model.ChangeLocationCommand.Execute(null);
            if (state is "approved" or "failed")
            {
                await model.ApproveCommand.ExecuteAsync(null);
                await model.FinalSaveFactsLoaded;
                // Reopen the now-approved target, as existing failure fixtures do, before
                // selecting the destination for its independent save operation.
                model = await GuidedCompletionSaveTests.Open(h, workflow, delivery, picker,
                    (await workflow.LoadAsync(review.Id, default)).Value);
                model.SaveFileNameText = "result.tif";
                model.ChangeLocationCommand.Execute(null);
                if (state == "failed")
                {
                    await model.SaveApprovedCommand.ExecuteAsync(null);
                    delivery.DeliverCalls.ShouldBe(0, "the unavailable destination is refused before any file copy");
                    model.FinalSaveAlert.ShouldContain("文件夹当前不可用");
                }
            }
            else
            {
                if (state == "collision") File.WriteAllBytes(Path.Combine(folder, "result.tif"), [1, 2, 3]);
                await model.ConfirmAndSaveCommand.ExecuteAsync(null);
            }
            await model.FinalSaveFactsLoaded;
            if (state == "history")
                model = await GuidedCompletionSaveTests.Open(h, workflow, delivery, picker, (await workflow.LoadAsync(review.Id, default)).Value);

            model.ChangeLocationLabel.ShouldContain("文件夹");
            model.CompleteLabel.ShouldContain("任务");
            if (state is "approved" or "failed")
            {
                model.FinalSaveStatus.ShouldContain("尚未保存");
                model.CanSaveApproved.ShouldBeTrue();
                File.Exists(Path.Combine(folder, "result.tif")).ShouldBeFalse();
                if (state == "failed") model.FinalSaveAlert.ShouldContain("无需重新处理");
            }
            if (state == "uncertain")
            {
                model.FinalSaveStatus.ShouldContain("同一次保存");
                model.CheckAgainLabel.ShouldContain("这次保存");
                model.CanCheckSaveAgain.ShouldBeTrue();
                model.CanOpenContainingFolder.ShouldBeFalse();
                delivery.DeliverCalls.ShouldBe(1);
            }
            if (state == "collision")
            {
                model.FinalSaveStatus.ShouldContain("不会覆盖");
                model.CanUseSuggestedName.ShouldBeTrue();
                File.ReadAllBytes(Path.Combine(folder, "result.tif")).ShouldBe([1, 2, 3]);
            }
            if (state == "history")
            {
                model.FinalSaveStatus.ShouldContain("有保存记录");
                model.FinalSaveStatus.ShouldContain("尚未核实");
            }
            if (state == "saved") model.FinalSaveStatus.ShouldContain("已保存");
            model.IsReviewRequired.ShouldBeFalse();
            (await workflow.LoadAsync(review.Id, default)).Value.State.ShouldBe(SessionState.Active);
            string before = JsonSerializer.Serialize((await h.Repository.LoadAsync(review.Id, default)).Value);
            int calls = delivery.DeliverCalls;
            var viewport = new Size(width, height);
            Render(() => new SessionScreenView { DataContext = model }, viewport, tree =>
            {
                var status = tree.OfType<TextBlock>().Where(GuidedSessionProjectionTests.Shown).Select(t => t.Text).ToArray();
                status.ShouldContain("任务尚未完成");
                foreach (Button button in tree.OfType<Button>().Where(b => GuidedSessionProjectionTests.Shown(b) &&
                    AutomationProperties.GetAutomationId(b).StartsWith("Session.FinalSave.")))
                {
                    if (button.Content is string label)
                    {
                        new ButtonAutomationPeer(button).GetName().ShouldBe(label);
                        Fits(button, label);
                    }
                }
            });
            Capture(() => new SessionScreenView { DataContext = model }, viewport, "save-" + state);
            delivery.DeliverCalls.ShouldBe(calls, "reading/rendering does not retry or publish another file");
            JsonSerializer.Serialize((await h.Repository.LoadAsync(review.Id, default)).Value).ShouldBe(before);
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    [Fact]
    public async Task A_definite_stage_verification_failure_directs_retry_without_offering_reconciliation()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        var workflow = FinalSaveFixtures.TiffService(h);
        var review = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
        string folder = FinalSaveFixtures.NewFolder("c04-stage-verification");
        try
        {
            var delivery = new ObservedDeliveryService(FinalSaveFixtures.Delivery(h,
                new BadStageHashFileSystem(new WindowsDeliveryFileSystem())));
            var model = await GuidedCompletionSaveTests.Open(h, workflow, delivery,
                new GuidedCompletionSaveTests.Picker(folder), review);
            model.ChangeLocationCommand.Execute(null);
            await model.ConfirmAndSaveCommand.ExecuteAsync(null);
            await model.FinalSaveFactsLoaded;
            delivery.DeliverCalls.ShouldBe(1);
            Directory.GetFiles(folder).ShouldBeEmpty("failed staging is cleaned before publication");
            model.CanSaveApproved.ShouldBeTrue();
            model.SaveApprovedLabel.ShouldContain("重试保存");
            model.CanCheckSaveAgain.ShouldBeFalse("a definite prepublication failure is not uncertain");
            model.FinalSaveStatus.ShouldContain("尚未保存");
            model.FinalSaveStatus.ShouldNotContain("核实", Case.Sensitive, "the instructions must match the available retry action");
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    [Fact]
    public async Task Checking_an_uncertain_save_reconciles_the_same_operation_without_another_delivery()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        var workflow = FinalSaveFixtures.TiffService(h);
        var review = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
        string folder = FinalSaveFixtures.NewFolder("c04-reconcile");
        try
        {
            var delivery = new ObservedDeliveryService(FinalSaveFixtures.Delivery(h,
                new LostPublicationAckFileSystem(new WindowsDeliveryFileSystem())));
            var model = await GuidedCompletionSaveTests.Open(h, workflow, delivery, new GuidedCompletionSaveTests.Picker(folder), review);
            model.ChangeLocationCommand.Execute(null);
            await model.ConfirmAndSaveCommand.ExecuteAsync(null);
            await model.FinalSaveFactsLoaded;
            model.FinalSaveStatus.ShouldContain("同一次保存");
            var files = Directory.GetFiles(folder).Order().ToArray();
            await model.CheckSaveAgainCommand.ExecuteAsync(null);
            await model.FinalSaveFactsLoaded;
            delivery.DeliverCalls.ShouldBe(1);
            delivery.ReconcileCalls.ShouldBe(1);
            Directory.GetFiles(folder).Order().ShouldBe(files);
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    [Theory]
    [MemberData(nameof(CorrectionCases))]
    public async Task Correction_copy_cancel_handoff_and_returned_review_are_separate(string stage, int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        var opened = await ReviewGuidanceSetup.OpenAtAsync(h, "background");
        var model = opened.Screen;
        string before = JsonSerializer.Serialize((await h.Repository.LoadAsync(model.Id, default)).Value);
        model.BeginAskColleagueCommand.Execute(null);
        model.CorrectionPrepareLabel.ShouldContain("副本");
        model.CorrectionCancelLabel.ShouldContain("本次");
        if (stage is "waiting" or "returned")
        {
            await model.PrepareCorrectionFilesCommand.ExecuteAsync(null);
            model.CorrectionStatusText.ShouldContain("自行交接");
            model.CorrectionOtherComputerText.ShouldContain("不会自动发送");
            model.CorrectionStep4Text.ShouldContain("导入后仍须审核");
        }
        if (stage == "returned")
        {
            opened.Picker.Next = CorrectionFixtures.CorrectedPng(h);
            await model.ImportCorrectedImageCommand.ExecuteAsync(null);
            model.IsReviewRequired.ShouldBeTrue();
            model.CanApprove.ShouldBeTrue();
            model.NextStepText.ShouldContain("不会重新去除背景");
        }
        if (stage == "cancel") model.CancelAskColleagueCommand.Execute(null);
        if (stage is "prepare" or "cancel")
            JsonSerializer.Serialize((await h.Repository.LoadAsync(model.Id, default)).Value).ShouldBe(before);
        var viewport = new Size(width, height);
        Render(() => new SessionScreenView { DataContext = model }, viewport, tree =>
        {
            string id = stage switch
            {
                "prepare" => "Session.Correction.Prepare", "waiting" => "Session.Correction.Import", _ => "Session.Approve",
            };
            Button action = GuidedSessionShellTests.Find<Button>(tree, id);
            GuidedSessionProjectionTests.Shown(action).ShouldBeTrue();
            Fits(action, (string)action.Content);
            Inside(action, tree.Root, viewport);
        });
        Capture(() => new SessionScreenView { DataContext = model }, viewport, "correction-" + stage);
    }

    [Theory]
    [InlineData(984, 661)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public async Task Settings_action_is_explicit_and_validation_preserves_readonly_production(int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SettingsScreenHarness h = new();
        var model = await h.OpenAsync();
        model.ApplyLabel.ShouldContain("设置");
        model.ProductionHint.ShouldContain("不能在这里修改");
        model.LogRetentionDays = "-1";
        await model.ApplyCommand.ExecuteAsync(null);
        (await h.Settings.ReadAllAsync(default)).Value.ShouldBeEmpty();
        var viewport = new Size(width, height);
        Render(() => new SettingsView { DataContext = model }, viewport, tree =>
        {
            Button action = GuidedSessionShellTests.Find<Button>(tree, "Settings.Apply");
            new ButtonAutomationPeer(action).GetName().ShouldBe(model.ApplyLabel);
            Fits(action, model.ApplyLabel);
            Inside(action, tree.Root, viewport);
            foreach (string id in new[] { "Settings.OutputRoot", "Settings.ProductionDpi", "Settings.Preset", "Settings.PhotoshopColourSettings" })
                GuidedSessionShellTests.Find<TextBox>(tree, id).IsReadOnly.ShouldBeTrue();
        });
        Capture(() => new SettingsView { DataContext = model }, viewport, "settings-invalid");
    }

    [Theory]
    [InlineData(984, 661)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public async Task Abandon_question_keeps_the_exact_task_and_cancel_is_not_abandon(int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        var id = await FailureGuidanceUiTests.FailedEnhancementAsync(h);
        var home = RecoverySurfaceTests.Home(h, h.CreateService());
        await home.RefreshCommand.ExecuteAsync(null);
        var row = home.RecentSessions.Single(r => r.Id == id);
        await home.AbandonCommand.ExecuteAsync(row);
        home.PendingAbandon!.Question.ShouldContain(row.DisplayName);
        home.PendingAbandon.Question.ShouldContain("不能继续处理");
        home.PendingAbandon.Question.ShouldContain("不会删除");
        home.KeepJobCommand.Execute(null);
        (await h.Repository.LoadAsync(id, default)).Value!.Session.State.ShouldBe(SessionState.Active);
        await home.AbandonCommand.ExecuteAsync(row);
        var viewport = new Size(width, height);
        Render(() => new HomeView { DataContext = home }, viewport, tree =>
        {
            foreach (string actionId in new[] { "Home.AbandonConfirmation.Keep", "Home.AbandonConfirmation.Confirm" })
            {
                var action = GuidedSessionShellTests.Find<Button>(tree, actionId);
                Fits(action, (string)action.Content);
                Inside(action, tree.Root, viewport);
                action.IsDefault.ShouldBeFalse();
            }
        });
        Capture(() => new HomeView { DataContext = home }, viewport, "abandon-confirmation");
    }

    private static TheoryData<string, int, int> Cases(params string[] states)
    {
        TheoryData<string, int, int> cases = new();
        foreach (string state in states)
            foreach (var (width, height) in new[] { (984, 661), (1280, 720), (1920, 1080) }) cases.Add(state, width, height);
        return cases;
    }

    private static void Render(Func<UserControl> create, Size viewport, Action<RenderedTree> inspect) =>
        WpfRendering.RenderExpectingNoBindingErrors(create, viewport, tree =>
        {
            try { inspect(tree); return 0; }
            finally { tree.Root.DataContext = null; tree.Root.UpdateLayout(); }
        });

    private static void Fits(Control control, string label)
    {
        var text = new FormattedText(label, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
            new Typeface(control.FontFamily, control.FontStyle, control.FontWeight, control.FontStretch), control.FontSize, Brushes.Black, 1);
        (text.WidthIncludingTrailingWhitespace + control.Padding.Left + control.Padding.Right).ShouldBeLessThanOrEqualTo(control.ActualWidth + 1, label);
    }

    private static void Inside(FrameworkElement element, Visual root, Size viewport)
    {
        Rect bounds = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
        bounds.Left.ShouldBeGreaterThanOrEqualTo(-0.5);
        bounds.Top.ShouldBeGreaterThanOrEqualTo(-0.5);
        bounds.Right.ShouldBeLessThanOrEqualTo(viewport.Width + 0.5);
        bounds.Bottom.ShouldBeLessThanOrEqualTo(viewport.Height + 0.5);
    }

    private static void Capture(Func<UserControl> create, Size viewport, string state)
    {
        string? directory = Environment.GetEnvironmentVariable("PF_C04_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(directory)) WpfRendering.CapturePng(create, viewport,
            Path.Combine(directory, $"{state}-{viewport.Width}x{viewport.Height}.png"));
    }

    // Use the real Windows staging verifier with a deliberately wrong expected stage hash.
    // Source identity, publication guard and cleanup remain the real implementation.
    private sealed class BadStageHashFileSystem(IDeliveryFileSystem inner) : IDeliveryFileSystem
    {
        public DeliveryFileResult<IDeliveryFileGuard> Open(string sourcePath, Sha256 expectedSourceHash, long expectedSourceLength,
            string requestedFolder, string finalLeaf, ArtifactKind kind, IReadOnlyList<string> protectedRoots, string? protectedOriginalPath)
        {
            var result = inner.Open(sourcePath, expectedSourceHash, expectedSourceLength, requestedFolder, finalLeaf, kind, protectedRoots, protectedOriginalPath);
            return result.IsSuccess ? DeliveryFileResult<IDeliveryFileGuard>.Ok(new Guard(result.Value!)) : result;
        }
        public DeliveryFileResult<string> ValidateLeaf(string candidate, ArtifactKind kind) => inner.ValidateLeaf(candidate, kind);
        public DeliveryFileResult<string> CheckFolder(string folder, IReadOnlyList<string> protectedRoots) => inner.CheckFolder(folder, protectedRoots);
        public string? SuggestAlternative(string leaf, Func<string, bool> exists) => inner.SuggestAlternative(leaf, exists);

        private sealed class Guard(IDeliveryFileGuard inner) : IDeliveryFileGuard
        {
            public string ResolvedFolder => inner.ResolvedFolder;
            public string FinalPath => inner.FinalPath;
            public string VolumeId => inner.VolumeId;
            public string DirectoryId => inner.DirectoryId;
            public string DestinationKey => inner.DestinationKey;
            public DeliveryFileIdentity SourceIdentity => inner.SourceIdentity;
            public DeliveryFileResult<bool> VerifySource() => inner.VerifySource();
            public DeliveryFinalCheck CheckFinal(DeliveryFileIdentity? expectedIdentity = null) => inner.CheckFinal(expectedIdentity);
            public DeliveryFileResult<DeliveryFileIdentity> CreateStaging(string stagingLeaf) => inner.CreateStaging(stagingLeaf);
            public DeliveryFileResult<DeliveryFileIdentity> OpenOwnedStaging(string stagingLeaf, DeliveryFileIdentity expectedIdentity) => inner.OpenOwnedStaging(stagingLeaf, expectedIdentity);
            public Task<DeliveryFileResult<bool>> CopyAndVerifyStageAsync(Sha256 expectedHash, long expectedLength,
                IProgress<long>? copied, CancellationToken cancellationToken) =>
                inner.CopyAndVerifyStageAsync(Sha256.Parse(new string('F', 64)), expectedLength, copied, cancellationToken);
            public DeliveryFileResult<bool> VerifyStage(Sha256 expectedHash, long expectedLength) => inner.VerifyStage(expectedHash, expectedLength);
            public DeliveryFileResult<bool> PublishNoReplace(string stagingLeaf, DeliveryFileIdentity expectedIdentity) => inner.PublishNoReplace(stagingLeaf, expectedIdentity);
            public DeliveryFileResult<bool> DeleteHeldStaging(DeliveryFileIdentity expectedIdentity) => inner.DeleteHeldStaging(expectedIdentity);
            public SelectionLeaseResult AcquireSelection(Guid deliveryId, DeliveryFileIdentity expectedIdentity, Sha256 expectedHash,
                long expectedLength) => inner.AcquireSelection(deliveryId, expectedIdentity, expectedHash, expectedLength);
            public void Dispose() => inner.Dispose();
        }
    }
}
