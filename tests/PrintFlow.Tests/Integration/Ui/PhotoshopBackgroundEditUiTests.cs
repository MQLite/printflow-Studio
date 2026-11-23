using System.Runtime.CompilerServices;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using static PrintFlow.Tests.Fixtures.CorrectionFixtures;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class PhotoshopBackgroundEditUiTests
{
    [Fact]
    public async Task Confirmation_starts_editing_and_a_save_refreshes_the_unapproved_preview()
    {
        using SessionServiceHarness h = new();
        var service = Service(h);
        var review = await AtBackgroundRemovalReviewAsync(h, service, opaqueSource: true);
        var editor = new SavedImageEditor();
        var screen = new SessionViewModel(service, h.Previews, h.TiffReviews, new RecordingNavigation(),
            photoshopBackgroundEditor: editor);
        screen.Open(review);
        await screen.PreviewsLoaded;

        screen.CanBeginPhotoshopEdit.ShouldBeTrue();
        screen.BeginPhotoshopEditCommand.Execute(null);
        screen.IsChoosingPhotoshopEdit.ShouldBeTrue();
        screen.CanApprove.ShouldBeFalse();
        screen.StopPhotoshopEditCommand.Execute(null);
        screen.CanApprove.ShouldBeTrue();
        editor.OpenCount.ShouldBe(0);

        screen.BeginPhotoshopEditCommand.Execute(null);
        Task editing = screen.OpenPhotoshopEditCommand.ExecuteAsync(null);
        var source = await editor.Opened.Task.WaitAsync(TimeSpan.FromSeconds(5));
        source.Facts.Sha256.ShouldBe(review.CurrentArtefact!.Sha256);
        screen.IsWaitingForPhotoshop.ShouldBeTrue();
        screen.CanReject.ShouldBeFalse();
        editor.Save.SetResult(CorrectedPng(h));
        await editing.WaitAsync(TimeSpan.FromSeconds(5));

        var current = (await service.LoadAsync(review.Id, default)).Value;
        current.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        current.CurrentArtefact!.RevisionId.ShouldNotBe(review.CurrentArtefact.RevisionId);
        screen.PreviewPanes.ShouldContain(p => p.RevisionId == current.CurrentArtefact.RevisionId && p.HasImage);
        screen.CanApprove.ShouldBeTrue();
        screen.IsWaitingForPhotoshop.ShouldBeFalse();
    }

    [Fact]
    public async Task Leaving_the_task_cancels_save_detection()
    {
        using SessionServiceHarness h = new();
        var service = Service(h);
        var review = await AtBackgroundRemovalReviewAsync(h, service);
        var editor = new SavedImageEditor();
        var screen = new SessionViewModel(service, h.Previews, h.TiffReviews, new RecordingNavigation(),
            photoshopBackgroundEditor: editor);
        screen.Open(review);
        screen.BeginPhotoshopEditCommand.Execute(null);
        Task editing = screen.OpenPhotoshopEditCommand.ExecuteAsync(null);
        await editor.Opened.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await screen.BackToHomeCommand.ExecuteAsync(null);
        await editing.WaitAsync(TimeSpan.FromSeconds(5));
        editor.Save.SetResult(CorrectedPng(h));

        var current = (await service.LoadAsync(review.Id, default)).Value;
        current.CurrentArtefact!.RevisionId.ShouldBe(review.CurrentArtefact!.RevisionId);
        screen.IsWaitingForPhotoshop.ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manual_edit_uses_the_input_before_processing_and_after_rejection(bool rejected)
    {
        using SessionServiceHarness h = new();
        var service = Service(h);
        string jpeg = h.Workspace.CreateSourceFile("original.jpg", SyntheticImages.Jpeg(6, 5));
        var waiting = await AtBackgroundRemovalWaitingAsync(service, jpeg);
        var editor = new SavedImageEditor();
        var screen = new SessionViewModel(service, h.Previews, h.TiffReviews, new RecordingNavigation(),
            photoshopBackgroundEditor: editor);
        var position = waiting;
        string savedPng = CorrectedPng(h);
        if (rejected)
        {
            await SessionServiceHarness.AuthoriseBackgroundRemovalAsync(service, waiting.Id);
            var reviewed = await MustAsync(service.ExecuteAsync(waiting.Id,
                new WorkflowCommand.StartStep(StepKind.BackgroundRemoval), "qa", default));
            var result = reviewed.CurrentArtefact!;
            screen.Open(reviewed);
            await screen.PreviewsLoaded;
            screen.BeginPhotoshopEditCommand.Execute(null);
            screen.IsChoosingPhotoshopEdit.ShouldBeTrue();
            screen.StopPhotoshopEditCommand.Execute(null);
            h.Clock.Advance(TimeSpan.FromTicks(4_567));
            await screen.RejectCommand.ExecuteAsync(null);
            position = await MustAsync(service.LoadAsync(waiting.Id, default));
            position.CurrentArtefact!.Sha256.ShouldBe(waiting.CurrentArtefact!.Sha256);
            var staleSave = await service.ImportPhotoshopBackgroundEditAsync(waiting.Id, waiting.PhotoshopBackgroundEdit!,
                new PhotoshopBackgroundSave(savedPng, result.Sha256, savedPng), "qa", default);
            staleSave.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        }
        else
        {
            screen.Open(waiting);
        }
        position.CurrentStep!.State.ShouldBe(rejected ? StepState.RetryRequired : StepState.Waiting);

        screen.CanBeginPhotoshopEdit.ShouldBeTrue();
        screen.BeginPhotoshopEditCommand.Execute(null);
        screen.CanRunStep.ShouldBeFalse();
        screen.CanRetry.ShouldBeFalse();
        screen.CanConfirmAutomaticSelection.ShouldBeFalse();
        var editing = screen.OpenPhotoshopEditCommand.ExecuteAsync(null);
        var first = await Task.WhenAny(editor.Opened.Task, editing).WaitAsync(TimeSpan.FromSeconds(5));
        first.ShouldBeSameAs(editor.Opened.Task, screen.Notice);
        var source = await editor.Opened.Task;
        source.Facts.Sha256.ShouldBe(waiting.CurrentArtefact!.Sha256);
        editor.Save.SetResult(savedPng);
        await editing.WaitAsync(TimeSpan.FromSeconds(5));

        var current = (await service.LoadAsync(waiting.Id, default)).Value;
        current.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        current.CurrentArtefact!.IsManualProcessingResult.ShouldBeTrue();
        current.CurrentArtefact.SourceRevisionId.ShouldBe(waiting.CurrentArtefact.RevisionId);
        screen.PreviewPanes.ShouldContain(p => p.RevisionId == current.CurrentArtefact.RevisionId && p.HasImage);
    }

    private sealed class SavedImageEditor : IPhotoshopBackgroundEditor
    {
        public int OpenCount { get; private set; }
        public TaskCompletionSource<PhotoshopBackgroundEditSource> Opened { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> Save { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<OperationResult<PhotoshopBackgroundSave>> WatchSavesAsync(PhotoshopBackgroundEditSource source,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            OpenCount++;
            Opened.SetResult(source);
            string path = await Save.Task.WaitAsync(cancellationToken);
            yield return OperationResult.Ok(new PhotoshopBackgroundSave(path, source.Facts.Sha256, path));
        }
    }
}
