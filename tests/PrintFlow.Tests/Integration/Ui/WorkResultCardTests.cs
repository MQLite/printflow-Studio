using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.App.ViewModels.AiDesign;
using PrintFlow.App.Views.AiDesign;
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
        public Func<SessionId, Task<OperationResult<ImagePreview>>> Decode { get; set; } = _ =>
            Task.FromResult(OperationResult.Fail<ImagePreview>(FailureCode.PersistenceError, "No thumbnail"));
        public Task<OperationResult<ImagePreview>> GetRecentThumbnailAsync(SessionId id, CancellationToken ct)
        { Calls.Add(id); return Decode(id); }
        public Task<OperationResult<ImagePreview>> GetPreviewAsync(SessionId id, RevisionId revision, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
