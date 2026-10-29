using System.IO;
using System.Reflection;
using System.Text.Json;
using PrintFlow.App.Navigation;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Delivery;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedSessionSaveOwnershipTests
{
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, false, true)]
    public async Task Opening_a_presentation_retires_old_save_success_failure_and_finally(bool sameId, bool fail, bool newBusy, bool holdBeforeDelivery)
    {
        using SessionServiceHarness h = new();
        var inner = h.CreateService();
        var review = await FinalSaveFixtures.PngAtFinalReviewAsync(h, inner);
        var service = DispatchProxy.Create<ISessionService, GuidedSessionInputTests.DeferredService>();
        var deferred = (GuidedSessionInputTests.DeferredService)(object)service; deferred.Inner = inner;
        string folder = FinalSaveFixtures.NewFolder("guided-save-owner");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? saving = null; Task? newCommand = null;
        GuidedSessionInputTests.Gate? newGate = null;
        try
        {
            var storage = FinalSaveFixtures.Delivery(h);
            var observed = new ObservedDeliveryService(storage) { HoldDelivery = release, ThrowOnDeliver = fail };
            var completed = DispatchProxy.Create<IApprovedArtifactDeliveryService, HeldDeliveryCompletion>();
            var held = (HeldDeliveryCompletion)(object)completed;
            held.Inner = storage; held.Release = release; held.ThrowAfterDelivery = fail;
            held.HoldBeforeDelivery = holdBeforeDelivery;
            IApprovedArtifactDeliveryService delivery = sameId ? completed : observed;
            var m = await Open(h, service, delivery, new FolderPicker(folder), review);
            m.SaveFileNameText = "old-presentation.png"; m.ChangeLocationCommand.Execute(null);
            saving = m.ConfirmAndSaveCommand.ExecuteAsync(null);
            await (sameId ? held.Entered.Task : observed.DeliveryEntered.Task).WaitAsync(TimeSpan.FromSeconds(10));
            var next = sameId ? (await inner.LoadAsync(review.Id, default)).Value :
                (await inner.ImportAsync(WorkflowType.GeneratePrintTiff, h.WriteBorderedSourcePng(), "new-presentation", "tester", default)).Value;
            m.Open(next); await m.PreviewsLoaded; await m.FinalSaveFactsLoaded;
            m.IsBusy.ShouldBeFalse("an old delivery does not own the newly opened presentation");
            m.IsFinalSaveWorking.ShouldBeFalse();
            if (sameId) m.SaveFileNameText = "new-presentation-draft.png";
            if (newBusy)
            {
                newGate = deferred.HoldNext();
                newCommand = m.ConfirmOriginalCommand.ExecuteAsync(null);
                await newGate.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                m.IsBusy.ShouldBeTrue();
            }
            var before = (m.Id, m.GuidedPage, m.Notice, m.SaveFileNameText, m.FinalSaveAlert, m.FinalSaveSavedPath);
            release.TrySetResult(); await saving;
            m.IsBusy.ShouldBe(newBusy, "retired save finally cannot clear the new operation's busy flag");
            (m.Id, m.GuidedPage, m.Notice, m.SaveFileNameText, m.FinalSaveAlert, m.FinalSaveSavedPath).ShouldBe(before);
            if (newGate is not null)
            {
                newGate.Release.TrySetResult(); await newCommand!;
                m.GuidedPage.ShouldBe(GuidedSessionPage.Dimensions);
                m.IsBusy.ShouldBeFalse();
            }
            // For same-ID reopen, hold the return after durable delivery: a fresh offer before
            // validation would correctly retire the old offer and refuse it as RequestConflict.
            bool saved = sameId ? !holdBeforeDelivery : !fail;
            if (holdBeforeDelivery) held.Outcome!.Code.ShouldBe(DeliveryCode.RequestConflict);
            File.Exists(Path.Combine(folder, "old-presentation.png")).ShouldBe(saved);
            var deliveries = (await delivery.GetDeliveryStateAsync(review.Id, null, default)).Value;
            deliveries.Count.ShouldBe(saved ? 1 : 0);
            deliveries.ShouldAllBe(d => d.Artifact.SessionId == review.Id);
            if (!sameId) (await delivery.GetDeliveryStateAsync(next.Id, null, default)).Value.ShouldBeEmpty();
        }
        finally
        {
            release.TrySetResult(); newGate?.Release.TrySetResult();
            if (saving is not null) await saving;
            if (newCommand is not null) await newCommand;
            FinalSaveFixtures.Remove(folder);
        }
    }

    [Fact]
    public async Task A_saved_name_and_folder_are_the_baseline_for_the_next_discard()
    {
        using SessionServiceHarness h = new();
        var service = h.CreateService();
        var review = await FinalSaveFixtures.PngAtFinalReviewAsync(h, service);
        string confirmed = FinalSaveFixtures.NewFolder("guided-confirmed");
        string edited = FinalSaveFixtures.NewFolder("guided-edited");
        try
        {
            var delivery = new ObservedDeliveryService(FinalSaveFixtures.Delivery(h));
            var picker = new FolderPicker(confirmed);
            var m = await Open(h, service, delivery, picker, review);
            m.SaveFileNameText = "confirmed-output.png"; m.ChangeLocationCommand.Execute(null);
            await m.ConfirmAndSaveCommand.ExecuteAsync(null); await m.FinalSaveFactsLoaded;
            string path = Path.Combine(confirmed, "confirmed-output.png");
            File.Exists(path).ShouldBeTrue();
            string persisted = JsonSerializer.Serialize((await h.Repository.LoadAsync(m.Id, default)).Value);
            m.SaveFileNameText = "unfinished-second-name.png";
            picker.Folder = edited; m.ChangeLocationCommand.Execute(null);
            m.ViewHistoryCommand.Execute(m.Steps.First());
            m.IsConfirmingDraftLeave.ShouldBeTrue();
            await m.DiscardDraftAndLeaveCommand.ExecuteAsync(null); m.CloseHistoryCommand.Execute(null);
            m.SaveFileNameText.ShouldBe("confirmed-output.png");
            m.SaveFolderText.ShouldBe(confirmed);
            m.FinalSaveSavedPath.ShouldBe(path);
            JsonSerializer.Serialize((await h.Repository.LoadAsync(m.Id, default)).Value).ShouldBe(persisted);
            delivery.DeliverCalls.ShouldBe(1);
        }
        finally { FinalSaveFixtures.Remove(confirmed, edited); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_pending_or_failed_save_does_not_consume_the_unconfirmed_draft(bool fail)
    {
        using SessionServiceHarness h = new();
        var service = h.CreateService();
        var review = await FinalSaveFixtures.PngAtFinalReviewAsync(h, service);
        string folder = FinalSaveFixtures.NewFolder("guided-pending");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? pending = null;
        try
        {
            var delivery = new ObservedDeliveryService(FinalSaveFixtures.Delivery(h)) { HoldDelivery = release, ThrowOnDeliver = fail };
            var m = await Open(h, service, delivery, new FolderPicker(folder), review);
            string original = m.SaveFileNameText;
            m.SaveFileNameText = "pending-output.png"; m.ChangeLocationCommand.Execute(null);
            pending = m.ConfirmAndSaveCommand.ExecuteAsync(null);
            await delivery.DeliveryEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await m.BackToHomeCommand.ExecuteAsync(null);
            m.IsConfirmingDraftLeave.ShouldBeFalse(); // No navigation while publication is outstanding.
            m.SaveFileNameText.ShouldBe("pending-output.png"); m.SaveFolderText.ShouldBe(folder);
            release.TrySetResult(); await pending; await m.FinalSaveFactsLoaded;
            if (fail)
            {
                m.ViewHistoryCommand.Execute(m.Steps.First());
                m.IsConfirmingDraftLeave.ShouldBeTrue();
                m.KeepDraftCommand.Execute(null);
                m.SaveFileNameText.ShouldBe("pending-output.png"); m.SaveFolderText.ShouldBe(folder);
                m.ViewHistoryCommand.Execute(m.Steps.First());
                await m.DiscardDraftAndLeaveCommand.ExecuteAsync(null); m.CloseHistoryCommand.Execute(null);
                m.SaveFileNameText.ShouldBe(original);
                File.Exists(Path.Combine(folder, "pending-output.png")).ShouldBeFalse();
            }
            else
            {
                m.ViewHistoryCommand.Execute(m.Steps.First());
                m.IsConfirmingDraftLeave.ShouldBeFalse(); m.IsViewingHistory.ShouldBeTrue();
                m.CloseHistoryCommand.Execute(null);
                m.SaveFileNameText.ShouldBe("pending-output.png"); m.SaveFolderText.ShouldBe(folder);
                File.Exists(Path.Combine(folder, "pending-output.png")).ShouldBeTrue();
            }
        }
        finally
        {
            release.TrySetResult(); if (pending is not null) await pending;
            FinalSaveFixtures.Remove(folder);
        }
    }

    private static async Task<SessionViewModel> Open(SessionServiceHarness h, ISessionService service,
        IApprovedArtifactDeliveryService delivery, IDeliveryFolderPicker picker, SessionView session)
    {
        var m = new SessionViewModel(service, h.Previews, h.TiffReviews, new RecordingNavigation(),
            finalSave: new FinalSaveCoordinator(service, delivery, "tester"), folderPicker: picker);
        m.Open(session); await m.PreviewsLoaded; await m.FinalSaveFactsLoaded;
        return m;
    }

    private sealed class FolderPicker(string folder) : IDeliveryFolderPicker
    {
        public string Folder { get; set; } = folder;
        public string? PickFolder(string title, string? initial) => Folder;
    }

    public class HeldDeliveryCompletion : DispatchProxy
    {
        public IApprovedArtifactDeliveryService Inner { get; set; } = null!;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; set; } = null!;
        public bool ThrowAfterDelivery { get; set; }
        public bool HoldBeforeDelivery { get; set; }
        public DeliveryOutcome? Outcome { get; private set; }
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            return method!.Name == nameof(IApprovedArtifactDeliveryService.DeliverAsync)
                ? Hold(method, args) : method.Invoke(Inner, args);
        }
        private async Task<DeliveryOutcome> Hold(MethodInfo method, object?[]? args)
        {
            if (HoldBeforeDelivery) { Entered.TrySetResult(); await Release.Task.ConfigureAwait(false); }
            var outcome = await ((Task<DeliveryOutcome>)method.Invoke(Inner, args)!).ConfigureAwait(false);
            Outcome = outcome;
            if (!HoldBeforeDelivery) { Entered.TrySetResult(); await Release.Task.ConfigureAwait(false); }
            if (ThrowAfterDelivery) throw new IOException("Controlled lost delivery acknowledgement");
            return outcome;
        }
    }
}
