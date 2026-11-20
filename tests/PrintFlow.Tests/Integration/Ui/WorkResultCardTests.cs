using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.App.Navigation;
using PrintFlow.App.ViewModels.AiDesign;
using PrintFlow.App.Views.AiDesign;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.AiDesign;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class WorkResultCardTests
{
    [Fact]
    public async Task OpenAi_generation_requires_consent_and_reuses_the_imported_result_card()
    {
        var query = new TestQuery();
        var api = new DesignApi();
        using var fixture = new PromptWorkbenchFixture(tasks: query, api: api, consent: api);
        var vm = fixture.Vm;
        await vm.OpenAsync(null, default);
        vm.GenerationProvider = AiDesignProvider.OpenAiApi;
        vm.Document = new([new AiTextPart(Guid.NewGuid(), "Draw a floral border.")]);

        await vm.AutoGenerateCommand.ExecuteAsync(null);
        api.Submitted.ShouldBeNull();

        api.Consent = true;
        query.Items = [TaskFor(vm.Draft!.Id, WorkTaskStage.Imported) with
        {
            RequestId = api.RequestId,
            Provider = AiDesignProvider.OpenAiApi,
            ImportedSessionId = SessionId.From(api.RequestId),
            OpenAiReceipt = new("resp_ui_test", "req_ui_test", "ig_ui_test", 100, 200, 300)
        }];
        await vm.AutoGenerateCommand.ExecuteAsync(null);

        api.Submitted!.FinalDocument.Parts.OfType<AiTextPart>().ShouldHaveSingleItem().Text.ShouldBe("Draw a floral border.");
        vm.WorkCard!.CanContinue.ShouldBeTrue();
        vm.WorkCard.CanViewWork.ShouldBeFalse();
        string details = vm.WorkCard.OpenAiResponseDetails.ShouldNotBeNull();
        details.ShouldContain("resp_ui_test");
        details.ShouldContain("300");
        vm.CopyOpenAiResponseIdCommand.Execute(null);
        fixture.Clipboard.Text.ShouldBe("resp_ui_test");
        vm.GenerationMilestones.ShouldAllBe(step => step.Reached);
    }

    private sealed class DesignApi : IAiDesignApiGeneration, IAiDesignApiConsent
    {
        public Guid RequestId { get; } = Guid.NewGuid();
        public bool Consent { get; set; }
        public AiAuthoringDraft? Submitted { get; private set; }
        public bool Confirm() => Consent;
        public OperationFailure? GetUnavailable() => null;
        public Task<OperationResult<Guid>> GenerateAsync(AiAuthoringDraft draft, CancellationToken ct)
        {
            Submitted = draft;
            return Task.FromResult(OperationResult.Ok(RequestId));
        }
    }

    [Fact]
    public async Task Failed_new_generation_keeps_previous_imported_result_available()
    {
        var submission = new PausedSubmission();
        var query = new TestQuery();
        var tracker = new WorkTaskTracker();
        using var fixture = new PromptWorkbenchFixture(tasks: query, tracker: tracker, submission: submission);
        var vm = fixture.Vm;
        await vm.OpenAsync(null, default);
        var previous = TaskFor(vm.Draft!.Id, WorkTaskStage.Imported) with
        {
            ImportedSessionId = SessionId.From(Guid.NewGuid()), WorkConversationId = Guid.NewGuid()
        };
        query.Items = [previous];
        tracker.RequestChanged(previous.RequestId);
        await vm.WorkCardRefresh;
        vm.WorkCard!.CanContinue.ShouldBeTrue();
        var generation = vm.AutoGenerateCommand.ExecuteAsync(null);
        await submission.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var failed = TaskFor(vm.Draft.Id, WorkTaskStage.NeedsAttention);
        query.Items = [previous, failed];
        submission.Finish.SetResult(OperationResult.Fail<AiDesignWorkSubmission>(FailureCode.AdapterUnavailable, "WorkModeUnavailable"));
        await generation.WaitAsync(TimeSpan.FromSeconds(10));
        tracker.RequestChanged(failed.RequestId);
        await vm.WorkCardRefresh;

        vm.Error.ShouldNotBeNullOrEmpty();
        vm.WorkCard!.Snapshot.ShouldBe(previous);
        vm.WorkCard.CanContinue.ShouldBeTrue();
    }

    [Fact]
    public async Task Generate_expands_progress_before_receipt_and_tracks_observed_stages()
    {
        var submission = new PausedSubmission();
        var query = new TestQuery();
        var tracker = new WorkTaskTracker();
        using var fixture = new PromptWorkbenchFixture(tasks: query, tracker: tracker, submission: submission);
        var vm = fixture.Vm;
        await vm.OpenAsync(null, default);
        vm.ShowGenerationProgress.ShouldBeFalse();
        vm.Fields.Single(x => x.Key == "姓名").Value = "Current";

        var generation = vm.AutoGenerateCommand.ExecuteAsync(null);
        var submitted = await submission.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        vm.ShowGenerationProgress.ShouldBeTrue();
        vm.GenerationMilestones[0].IsCurrent.ShouldBeTrue();
        vm.AutoGenerateCommand.CanExecute(null).ShouldBeFalse();
        string.Concat(submitted.FinalDocument.Parts.OfType<AiTextPart>().Select(x => x.Text)).ShouldContain("姓名：Current");

        var awaiting = TaskFor(submitted.Id, WorkTaskStage.AwaitingReturn) with { WorkConversationId = Guid.NewGuid() };
        query.Items = [awaiting];
        submission.Finish.SetResult(OperationResult.Ok(new AiDesignWorkSubmission(awaiting.RequestId, awaiting.WorkConversationId.Value, null)));
        await generation.WaitAsync(TimeSpan.FromSeconds(10));
        vm.GenerationMilestones[0].Reached.ShouldBeTrue();
        vm.GenerationMilestones[1].IsCurrent.ShouldBeTrue();
        WpfRendering.RenderExpectingNoBindingErrors(() => new AiPromptWorkbenchView { DataContext = vm }, new Size(1000, 700), tree =>
        {
            var progress = tree.OfType<WorkResultCardView>().Single();
            var bounds = progress.TransformToAncestor(tree.Root).TransformBounds(new Rect(progress.RenderSize));
            progress.Visibility.ShouldBe(Visibility.Visible);
            bounds.Height.ShouldBeGreaterThan(0);
            bounds.Bottom.ShouldBeLessThan(700);
            return 0;
        });

        query.Items = [awaiting with { Stage = WorkTaskStage.Validating }];
        tracker.RequestChanged(awaiting.RequestId);
        await vm.WorkCardRefresh;
        vm.GenerationMilestones[2].IsCurrent.ShouldBeTrue();
        vm.GenerationMilestones[3].Reached.ShouldBeFalse();
    }

    private sealed class PausedSubmission : IAiDesignTextSubmission
    {
        public TaskCompletionSource<AiAuthoringDraft> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<OperationResult<AiDesignWorkSubmission>> Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<OperationResult<AiDesignWorkSubmission>> SubmitAsync(AiAuthoringDraft draft, CancellationToken ct)
        {
            Started.SetResult(draft);
            return Finish.Task;
        }
        public Task<OperationResult<PrintFlow.Domain.Results.Unit>> StopReceivingAsync(Guid requestId, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    [Fact]
    public async Task Current_draft_card_shows_observed_progress_and_only_imported_thumbnail()
    {
        var query = new TestQuery();
        var tracker = new WorkTaskTracker();
        var preview = new TestPreview();
        using var fixture = new PromptWorkbenchFixture(tasks: query, tracker: tracker, previews: preview);
        var vm = fixture.Vm;
        await vm.OpenAsync(null, default);
        var draft = vm.Draft!.Id;
        var waiting = TaskFor(draft, WorkTaskStage.AwaitingVerification);
        query.Items = [waiting, TaskFor(Guid.NewGuid(), WorkTaskStage.Validating)];
        tracker.RequestChanged(waiting.RequestId);
        await vm.WorkCardRefresh;
        vm.WorkCard!.Snapshot.ShouldBe(waiting);
        vm.AutoGenerateCommand.CanExecute(null).ShouldBeFalse();
        preview.Calls.ShouldBeEmpty();

        await vm.OpenAsync(draft, default);
        vm.AutoGenerateCommand.CanExecute(null).ShouldBeFalse();
        var session = SessionId.From(Guid.NewGuid());
        var imported = waiting with { Stage = WorkTaskStage.Imported, ImportedSessionId = session,
            Format = ImageFormat.Png, PixelWidth = 1254, PixelHeight = 1254 };
        query.Items = [imported];
        var held = new TaskCompletionSource<OperationResult<ImagePreview>>(TaskCreationOptions.RunContinuationsAsynchronously);
        preview.Decode = _ => held.Task;
        tracker.RequestChanged(imported.RequestId);
        await vm.WorkCardRefresh;
        var oldCard = vm.WorkCard!;
        oldCard.Snapshot.ShouldBe(imported);
        oldCard.ImageFacts!.ShouldContain("1254 × 1254");
        preview.Calls.ShouldBe([session]);

        var oldDecode = vm.WorkCardThumbnail;
        await vm.OpenAsync(null, default);
        held.SetResult(OperationResult.Ok(new ImagePreview(default, 32, 32, 1254, 1254, false, new byte[] { 1 })));
        await oldDecode;
        vm.WorkCard.ShouldBeNull();
        oldCard.HasThumbnail.ShouldBeFalse();
        vm.Deactivate();
    }

    [Fact]
    public async Task Imported_card_keeps_continue_if_thumbnail_is_missing()
    {
        var query = new TestQuery();
        using var fixture = new PromptWorkbenchFixture(tasks: query, tracker: new(), previews: new TestPreview());
        await fixture.Vm.OpenAsync(null, default);
        var draft = fixture.Vm.Draft!.Id;
        var session = SessionId.From(Guid.NewGuid());
        query.Items = [TaskFor(draft, WorkTaskStage.Imported) with { ImportedSessionId = session }];
        await fixture.Vm.OpenAsync(draft, default);
        await fixture.Vm.WorkCardThumbnail;
        fixture.Vm.WorkCard!.HasThumbnail.ShouldBeFalse();
        fixture.Vm.WorkCard.CanContinue.ShouldBeTrue();
        fixture.Vm.WorkCard.Snapshot.ImportedSessionId.ShouldBe(session);
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en")]
    public async Task Card_actions_are_within_the_window_and_keyboard_accessible(string language)
    {
        using var culture = new OperatorCultureScope(language);
        var tasks = new TestQuery();
        var tracker = new WorkTaskTracker();
        var preview = new TestPreview();
        using var fixture = new PromptWorkbenchFixture(tasks: tasks, tracker: tracker, previews: preview);
        var vm = fixture.Vm;
        await vm.OpenAsync(null, default);
        var task = TaskFor(vm.Draft!.Id, WorkTaskStage.AwaitingVerification) with
            { CanStopReceiving = true, WorkConversationId = Guid.NewGuid() };

        void Verify(string action, string capture)
        {
            var render = WpfRendering.Render(() => new AiPromptWorkbenchView { DataContext = vm }, new Size(1000, 700), tree =>
            {
                var button = tree.OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == action);
                var box = button.TransformToAncestor(tree.Root).TransformBounds(new Rect(button.RenderSize));
                return button.Visibility == Visibility.Visible && button.IsEnabled && button.IsTabStop &&
                    box.Width > 0 && box.Left >= 0 && box.Right <= 1000 && box.Bottom <= 700;
            });
            render.BindingErrors.ShouldBeEmpty();
            render.Facts.ShouldBeTrue();
            if (Environment.GetEnvironmentVariable("PRINTFLOW_WORK_UI_CAPTURE") is { Length: > 0 } directory)
                WpfRendering.CapturePng(() => new AiPromptWorkbenchView { DataContext = vm }, new Size(1000, 700),
                    Path.Combine(directory, $"{language}-{capture}.png"));
        }

        tasks.Items = [task];
        tracker.RequestChanged(task.RequestId);
        await vm.WorkCardRefresh;
        Verify("AiPrompt.ViewWork", "work-card-pending");
        vm.WorkCard!.CanContinue.ShouldBeFalse();

        var pixels = await File.ReadAllBytesAsync(WriteSyntheticReturn(fixture.Root.Root, task.RequestId));
        preview.Decode = _ => Task.FromResult(OperationResult.Ok(new ImagePreview(default, 64, 64, 1254, 1254, false, pixels)));
        tasks.Items = [task with { Stage = WorkTaskStage.Imported, ImportedSessionId = SessionId.From(task.RequestId),
            CanStopReceiving = false, Format = ImageFormat.Png, PixelWidth = 1254, PixelHeight = 1254 }];
        tracker.RequestChanged(task.RequestId);
        await vm.WorkCardRefresh;
        await vm.WorkCardThumbnail;
        vm.WorkCard!.HasThumbnail.ShouldBeTrue();
        Verify("AiPrompt.ContinueWorkResult", "work-card-imported");
        Verify("AiPrompt.OpenResultPreview", "work-card-preview");
        Verify("AiPrompt.RegenerateInWork", "work-card-regenerate");
        await vm.OpenWorkResultPreviewCommand.ExecuteAsync(null);
        vm.ShowWorkPreview.ShouldBeTrue();
        vm.WorkPreview.ShouldNotBeNull();
        preview.FullPreviewCalls.ShouldBe([SessionId.From(task.RequestId)]);
        WpfRendering.RenderExpectingNoBindingErrors(() => new AiPromptWorkbenchView { DataContext = vm }, new Size(1000, 700), tree =>
        {
            var overlay = tree.OfType<Border>().Single(x => AutomationProperties.GetAutomationId(x) == "AiPrompt.ResultPreview");
            overlay.Visibility.ShouldBe(Visibility.Visible);
            tree.OfType<Image>().Single(x => x.IsDescendantOf(overlay)).RenderSize.Height.ShouldBeGreaterThan(250);
            return 0;
        });
        vm.CloseWorkResultPreviewCommand.Execute(null);
        vm.ShowWorkPreview.ShouldBeFalse();
        vm.WorkPreview.ShouldBeNull();
    }

    [Fact]
    public async Task Regenerate_opens_the_editor_and_only_confirmation_submits_the_revision()
    {
        using var culture = new OperatorCultureScope("zh-CN");
        var query = new TestQuery();
        var tracker = new WorkTaskTracker();
        var launcher = new CapturingLauncher();
        var submission = new FollowUpSubmission();
        var preview = new TestPreview();
        using var fixture = new PromptWorkbenchFixture(tasks: query, tracker: tracker, submission: submission,
            launcher: launcher, previews: preview);
        var vm = fixture.Vm;
        await vm.OpenAsync(null, default);
        var source = TaskFor(vm.Draft!.Id, WorkTaskStage.Imported) with
            { WorkConversationId = Guid.NewGuid(), ImportedSessionId = SessionId.From(Guid.NewGuid()) };
        var pixels = await File.ReadAllBytesAsync(WriteSyntheticReturn(fixture.Root.Root, source.RequestId));
        preview.Decode = _ => Task.FromResult(OperationResult.Ok(new ImagePreview(default, 64, 64, 1254, 1254, false, pixels)));
        query.Items = [source];
        await vm.OpenAsync(vm.Draft.Id, default);
        var fresh = TaskFor(vm.Draft.Id, WorkTaskStage.AwaitingReturn) with
            { WorkConversationId = source.WorkConversationId, CanStopReceiving = true, FollowUpSourceRequestId = source.RequestId };
        submission.Send = (request, prompt) =>
        {
            request.ShouldBe(source.RequestId);
            prompt.ShouldBe($"{vm.FollowUpPrefix}\n把边框改成蓝色。");
            if (submission.Calls == 1) return OperationResult.Fail<AiDesignWorkSubmission>(FailureCode.AdapterUnavailable, "WorkFollowUpBridgeUnavailable");
            query.Items = [source, fresh];
            return OperationResult.Ok(new AiDesignWorkSubmission(fresh.RequestId, source.WorkConversationId!.Value, null));
        };
        await vm.RegenerateInWorkCommand.ExecuteAsync(null);
        vm.ShowFollowUpEditor.ShouldBeTrue();
        vm.WorkPreview.ShouldNotBeNull();
        preview.FullPreviewCalls.ShouldBe([source.ImportedSessionId!.Value]);
        vm.FollowUpPrefix.ShouldBe("做以下修改：");
        vm.SubmitFollowUpLabel.ShouldBe("再次生成");
        vm.FollowUpInstructions.ShouldBeEmpty();
        WpfRendering.RenderExpectingNoBindingErrors(() => new AiPromptWorkbenchView { DataContext = vm }, new Size(1000, 700), tree =>
        {
            var overlay = tree.OfType<Border>().Single(x => AutomationProperties.GetAutomationId(x) == "AiPrompt.FollowUpEditor");
            overlay.Visibility.ShouldBe(Visibility.Visible);
            var image = tree.OfType<Image>().Single(x => x.IsDescendantOf(overlay));
            var instructions = tree.OfType<TextBox>().Single(x => AutomationProperties.GetAutomationId(x) == "AiPrompt.FollowUpInstructions");
            var submit = tree.OfType<Button>().Single(x => AutomationProperties.GetAutomationId(x) == "AiPrompt.SubmitFollowUp");
            var imageBounds = image.TransformToAncestor(tree.Root).TransformBounds(new Rect(image.RenderSize));
            var instructionsBounds = instructions.TransformToAncestor(tree.Root).TransformBounds(new Rect(instructions.RenderSize));
            image.Source.ShouldNotBeNull();
            imageBounds.Height.ShouldBeGreaterThan(250);
            imageBounds.Right.ShouldBeLessThan(instructionsBounds.Left);
            instructions.Text.ShouldBeEmpty();
            instructions.AcceptsReturn.ShouldBeTrue();
            submit.IsEnabled.ShouldBeFalse();
            return 0;
        });
        vm.SubmitFollowUpCommand.CanExecute(null).ShouldBeFalse();
        await vm.SubmitFollowUpCommand.ExecuteAsync(null);
        submission.Calls.ShouldBe(0);
        launcher.Conversation.ShouldBeNull();
        string.IsNullOrEmpty(fixture.Clipboard.Text).ShouldBeTrue();
        vm.WorkCard!.Snapshot.RequestId.ShouldBe(source.RequestId);

        vm.FollowUpInstructions = "把边框改成蓝色。";
        vm.SubmitFollowUpCommand.CanExecute(null).ShouldBeTrue();
        await vm.SubmitFollowUpCommand.ExecuteAsync(null);
        vm.FollowUpFeedback.ShouldNotBeNull();
        vm.FollowUpInstructions.ShouldBe("把边框改成蓝色。");
        vm.ShowFollowUpEditor.ShouldBeTrue();
        await vm.SubmitFollowUpCommand.ExecuteAsync(null);
        submission.Calls.ShouldBe(2);
        vm.ShowFollowUpEditor.ShouldBeFalse();
        vm.WorkCard!.Snapshot.RequestId.ShouldBe(fresh.RequestId);
        vm.GenerationMilestones[0].Reached.ShouldBeTrue();
        vm.CanStopReceiving.ShouldBeTrue();
        query.Items = [source, fresh with { Stage = WorkTaskStage.Cancelled, CanStopReceiving = false }];
        tracker.RequestChanged(fresh.RequestId);
        await vm.WorkCardRefresh;
        vm.WorkCard!.Snapshot.RequestId.ShouldBe(source.RequestId);
    }

    private sealed class FollowUpSubmission : IAiDesignTextSubmission
    {
        public Func<Guid, string, OperationResult<AiDesignWorkSubmission>> Send { get; set; } = (_, _) => throw new InvalidOperationException();
        public int Calls { get; private set; }
        public Task<OperationResult<AiDesignWorkSubmission>> SubmitFollowUpAsync(Guid sourceRequestId, string prompt, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Send(sourceRequestId, prompt));
        }
        public Task<OperationResult<AiDesignWorkSubmission>> SubmitAsync(AiAuthoringDraft draft, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<OperationResult<PrintFlow.Domain.Results.Unit>> StopReceivingAsync(Guid requestId, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private sealed class CapturingLauncher : IWorkConversationLauncher
    {
        public Guid? Conversation { get; private set; }
        public bool Open(Guid conversationId) { Conversation = conversationId; return true; }
    }

    internal static string WriteSyntheticReturn(string root, Guid request)
    {
        string path = Path.Combine(root, $"return-{request:N}.png");
        WpfRendering.OnStaThread(() =>
        {
            const int edge = 64;
            byte[] pixels = Enumerable.Repeat((byte)255, edge * edge * 4).ToArray();
            for (int y = 10; y < 54; y++)
            for (int x = 32 - (y - 10) / 2; x <= 32 + (y - 10) / 2; x++)
            {
                int pixel = (y * edge + x) * 4;
                pixels[pixel] = 40;
                pixels[pixel + 1] = 50;
                pixels[pixel + 2] = 220;
            }
            var bitmap = BitmapSource.Create(edge, edge, 96, 96, PixelFormats.Bgra32, null, pixels, edge * 4);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(path);
            png.Save(file);
        });
        return path;
    }

    internal static WorkTaskSnapshot TaskFor(Guid? draft, WorkTaskStage stage) =>
        new(Guid.NewGuid(), Guid.NewGuid(), 1, draft, "Synthetic task", DateTimeOffset.UtcNow, stage);

    internal sealed class TestQuery : IWorkTaskQuery
    {
        public WorkTaskSnapshot[] Items { get; set; } = [];
        public bool Fail { get; set; }
        public Func<Guid, Task<OperationResult<WorkTaskSnapshot?>>>? FindOverride { get; set; }
        public Task<OperationResult<IReadOnlyList<WorkTaskSnapshot>>> ListAsync(CancellationToken ct) =>
            Task.FromResult(Fail ? OperationResult.Fail<IReadOnlyList<WorkTaskSnapshot>>(FailureCode.PersistenceError, "Unavailable")
                : OperationResult.Ok<IReadOnlyList<WorkTaskSnapshot>>(Items));
        public Task<OperationResult<WorkTaskSnapshot?>> FindAsync(Guid requestId, CancellationToken ct) =>
            FindOverride?.Invoke(requestId) ?? Task.FromResult(OperationResult.Ok<WorkTaskSnapshot?>(Items.FirstOrDefault(x => x.RequestId == requestId)));
    }

    internal sealed class TestPreview : IArtefactPreviewService
    {
        public List<SessionId> Calls { get; } = [];
        public List<SessionId> FullPreviewCalls { get; } = [];
        public Func<SessionId, Task<OperationResult<ImagePreview>>> Decode { get; set; } = _ =>
            Task.FromResult(OperationResult.Fail<ImagePreview>(FailureCode.PersistenceError, "No thumbnail"));
        public Task<OperationResult<ImagePreview>> GetRecentThumbnailAsync(SessionId id, CancellationToken ct)
        { Calls.Add(id); return Decode(id); }
        public Task<OperationResult<ImagePreview>> GetImportedOriginalPreviewAsync(SessionId id, CancellationToken ct)
        { FullPreviewCalls.Add(id); return Decode(id); }
        public Task<OperationResult<ImagePreview>> GetPreviewAsync(SessionId id, RevisionId revision, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
