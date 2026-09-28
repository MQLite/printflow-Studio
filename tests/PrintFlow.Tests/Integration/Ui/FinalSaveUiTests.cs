using System.Globalization;
using System.IO;
using System.Resources;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PrintFlow.App.Localisation;
using PrintFlow.App.Navigation;
using PrintFlow.App.Resources;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.Persistence;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Delivery;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// SCRUM-11145 final-review/save screen state through the real session service, SQLite journal
/// and NTFS delivery adapter, with a fake folder picker and a recording shell port. Rendering is
/// off-screen measure/arrange; nothing is shown and no OS input is sent.
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class FinalSaveUiTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public async Task Asset_final_review_shows_a_png_draft_without_print_size_and_asks_for_a_folder(string language)
    {
        using CultureScope culture = new(language);
        using SessionServiceHarness h = new();
        ISessionService workflow = h.CreateService();
        SessionView review = await FinalSaveFixtures.PngAtFinalReviewAsync(h, workflow);
        var delivery = new ObservedDeliveryService(FinalSaveFixtures.Delivery(h));
        var picker = new StubFolderPicker();
        SessionViewModel screen = await OpenAsync(h, workflow, delivery, picker, new RecordingDeliveredFileShell(), review);

        screen.HasFinalSave.ShouldBeTrue();
        screen.SaveFileNameText.ShouldBe("keep-extent.png");
        screen.SaveSummary.ShouldContain("keep-extent.png");
        screen.SaveSummary.ShouldContain("PNG");
        screen.SaveSummary.ShouldNotContain("TIFF");
        screen.SaveSummary.ShouldNotContain(language == "en" ? " mm" : "毫米");
        screen.SaveDraftNotice.ShouldBe(Strings.FinalSave_NoFolder);
        screen.SaveFolderText.ShouldBe(Strings.FinalSave_NoFolderValue);
        screen.CanConfirmAndSave.ShouldBeFalse();
        screen.CanApprove.ShouldBeTrue("a missing destination never disables the approval-only action");
        screen.FinalSaveStatus.ShouldContain(screen.ConfirmAndSaveLabel);
        screen.FinalSaveSecondary.ShouldContain(screen.ApproveLabel);
        screen.NextStepText.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.Session_NextFinalReviewNeedsDraft, screen.ApproveLabel),
            "the status panel never names the hidden combined action");
        screen.CanOpenContainingFolder.ShouldBeFalse("a pending result is never opened as a finished file");
        screen.CanSaveApproved.ShouldBeFalse("a pending result has no direct export");

        // Draft edits survive a refresh from the same view; nothing is approved or saved by it.
        screen.SaveFileNameText = "logo";
        screen.SaveDraftNotice.ShouldContain("logo.png");
        screen.SaveSummary.ShouldContain("logo.png");
        screen.Open((await workflow.LoadAsync(review.Id, CancellationToken.None)).Value);
        await screen.FinalSaveFactsLoaded;
        screen.SaveFileNameText.ShouldBe("logo");
        screen.SaveFileNameText = "logo.jpg";
        screen.SaveDraftNotice.ShouldContain(".png");
        picker.Calls.ShouldBe(0);
        delivery.DeliverCalls.ShouldBe(0);
        (await FinalSaveFixtures.LoadAsync(h, review.Id)).Reviews.ShouldBeEmpty();
    }

    [Fact]
    public async Task Picker_dismissal_changes_nothing_then_confirm_saves_and_opens_only_the_delivered_file()
    {
        using CultureScope culture = new("en");
        using SessionServiceHarness h = new();
        ISessionService workflow = h.CreateService();
        SessionView review = await FinalSaveFixtures.PngAtFinalReviewAsync(h, workflow);
        var delivery = new ObservedDeliveryService(FinalSaveFixtures.Delivery(h));
        var picker = new StubFolderPicker();
        var shell = new RecordingDeliveredFileShell();
        SessionViewModel screen = await OpenAsync(h, workflow, delivery, picker, shell, review);
        string folder = FinalSaveFixtures.NewFolder("ui-png");
        try
        {
            string? identity = screen.ConfirmAndSaveIdentity;
            screen.ChangeLocationCommand.Execute(null);
            picker.Calls.ShouldBe(1);
            screen.SaveFolderText.ShouldBe(Strings.FinalSave_NoFolderValue);
            screen.ConfirmAndSaveIdentity.ShouldBe(identity, "a dismissed picker is not a draft change");
            screen.CanConfirmAndSave.ShouldBeFalse();

            picker.Next = folder;
            screen.ChangeLocationCommand.Execute(null);
            screen.SaveFolderText.ShouldBe(folder);
            screen.ConfirmAndSaveIdentity.ShouldNotBe(identity, "a changed destination draft needs a fresh gesture");
            screen.CanConfirmAndSave.ShouldBeTrue();
            screen.RecommendedCommand.ShouldBeSameAs(screen.ConfirmAndSaveCommand);
            (await FinalSaveFixtures.LoadAsync(h, review.Id)).Reviews.ShouldBeEmpty("picking a folder approves nothing");

            await screen.ConfirmAndSaveCommand.ExecuteAsync(null);
            await screen.FinalSaveFactsLoaded;

            string saved = Path.Combine(folder, "keep-extent.png");
            screen.FinalSaveStatus.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_Saved, "keep-extent.png", folder));
            screen.FinalSaveSavedPath.ShouldBe(saved);
            File.Exists(saved).ShouldBeTrue();
            screen.CanOpenContainingFolder.ShouldBeTrue();
            screen.CanSaveApproved.ShouldBeFalse("the same name and folder are already saved");
            picker.Calls.ShouldBe(2, "only the explicit Change location opened a dialog");
            var counts = await FinalSaveFixtures.WorkCountsAsync(h, review.Id);
            counts.Promotions.ShouldBe(1);

            await screen.OpenContainingFolderCommand.ExecuteAsync(null);
            await screen.OpenContainingFolderCommand.ExecuteAsync(null);
            shell.Dispatches.ShouldBe([(saved, true), (saved, true)]);
            shell.LastLease!.IsDisposed.ShouldBeTrue();
            screen.FinalSaveOpenNotice.ShouldContain("keep-extent.png");
            (await FinalSaveFixtures.WorkCountsAsync(h, review.Id)).ShouldBe(counts, "Open starts no production work");
            delivery.DeliverCalls.ShouldBe(1);

            // Completed is a separate lawful command and never implies Saved or the reverse.
            await screen.CompleteCommand.ExecuteAsync(null);
            await screen.FinalSaveFactsLoaded;
            screen.NextStepText.ShouldBe(Strings.Session_NextCompleted);
            screen.FinalSaveStatus.ShouldContain(Path.GetFileName(saved));
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    [Fact]
    public async Task Next_image_shows_the_remembered_folder_first_and_a_collision_needs_explicit_acceptance()
    {
        using CultureScope culture = new("en");
        using SessionServiceHarness h = new();
        ISessionService workflow = h.CreateService();
        var delivery = new ObservedDeliveryService(FinalSaveFixtures.Delivery(h));
        string folder = FinalSaveFixtures.NewFolder("ui-next");
        try
        {
            SessionView first = await FinalSaveFixtures.PngAtFinalReviewAsync(h, workflow);
            (await new FinalSaveCoordinator(workflow, delivery, "tester").ConfirmAndSaveAsync(
                FinalSaveFixtures.Confirm(first, folder, "keep-extent.png"), null, CancellationToken.None))
                .Delivery!.Code.ShouldBe(DeliveryCode.Delivered);

            SessionView second = await FinalSaveFixtures.PngAtFinalReviewAsync(h, workflow);
            var picker = new StubFolderPicker();
            SessionViewModel screen = await OpenAsync(h, workflow, delivery, picker, new RecordingDeliveredFileShell(), second);
            screen.SaveFolderText.ShouldBe(folder, "the last successful folder is visible before confirmation");
            screen.SaveDraftNotice.ShouldBe(Strings.FinalSave_RememberedFolder);
            screen.CanConfirmAndSave.ShouldBeTrue();

            await screen.ConfirmAndSaveCommand.ExecuteAsync(null);
            await screen.FinalSaveFactsLoaded;
            picker.Calls.ShouldBe(0, "no dialog is forced when the shown folder is accepted");
            screen.FinalSaveStatus.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_ApprovedNotSavedReason,
                string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_Collision, "keep-extent.png", "keep-extent (2).png")));
            screen.NextStepText.ShouldStartWith(screen.FinalSaveStatus, Case.Sensitive, "the status panel agrees with the save section");
            screen.RecommendedCommand.ShouldBeSameAs(screen.UseSuggestedNameCommand);
            screen.CanUseSuggestedName.ShouldBeTrue();
            screen.CanSaveApproved.ShouldBeFalse("retrying the colliding name unchanged cannot succeed");
            var counts = await FinalSaveFixtures.WorkCountsAsync(h, second.Id);
            counts.Reviews.ShouldBe(1);
            counts.Promotions.ShouldBe(1);

            screen.UseSuggestedNameCommand.Execute(null);
            screen.SaveFileNameText.ShouldBe("keep-extent (2).png");
            File.Exists(Path.Combine(folder, "keep-extent (2).png")).ShouldBeFalse("a suggestion is not a save");
            screen.CanSaveApproved.ShouldBeTrue();
            screen.SaveApprovedLabel.ShouldBe(Strings.FinalSave_SaveApproved);
            await screen.SaveApprovedCommand.ExecuteAsync(null);
            await screen.FinalSaveFactsLoaded;
            screen.FinalSaveSavedPath.ShouldBe(Path.Combine(folder, "keep-extent (2).png"));
            (await FinalSaveFixtures.WorkCountsAsync(h, second.Id)).ShouldBe(counts, "a save retry never reapproves or reprocesses");
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    [Fact]
    public async Task Tiff_shows_backend_physical_size_keeps_older_size_selectable_and_late_results_stay_with_their_item()
    {
        using CultureScope culture = new("en");
        using SessionServiceHarness h = new();
        ISessionService workflow = FinalSaveFixtures.TiffService(h);
        SessionView sizeA = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow, 200);
        (await workflow.ExecuteAsync(sizeA.Id, new WorkflowCommand.Approve(StepKind.PhotoshopOutput, sizeA.CurrentArtefact!.Sha256),
            "tester", CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await workflow.ExecuteAsync(sizeA.Id, new WorkflowCommand.Complete(), "tester", CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await workflow.ExecuteAsync(sizeA.Id, new WorkflowCommand.AddAnotherSize(), "tester", CancellationToken.None)).IsSuccess.ShouldBeTrue();
        SessionView sizeB = await FinalSaveFixtures.NextTiffSizeAsync(workflow, sizeA.Id, 250);
        var delivery = new ObservedDeliveryService(FinalSaveFixtures.Delivery(h));
        string folder = FinalSaveFixtures.NewFolder("ui-tiff");
        var picker = new StubFolderPicker { Next = folder };
        SessionViewModel screen = await OpenAsync(h, workflow, delivery, picker, new RecordingDeliveredFileShell(), sizeB);
        try
        {
            screen.FinalSaveTargets.Count.ShouldBe(2);
            screen.HasMultipleFinalSaveTargets.ShouldBeTrue();
            FinalSaveTargetRow pending = screen.SelectedFinalSaveTarget!;
            pending.Label.ShouldContain("waiting for your review");
            FinalSaveTargetRow older = screen.FinalSaveTargets.Single(r => r != pending);
            older.Label.ShouldContain("approved");

            DeliveryOffer offer = (await delivery.GetOfferAsync(new ArtifactKey(sizeA.Id, ArtifactKind.ApprovedPrintTiff,
                sizeA.CurrentArtefact.RevisionId.Value), CancellationToken.None)).Offer!;
            screen.SelectedFinalSaveTarget = older;
            await RefreshFactsAsync(screen, workflow, sizeA.Id);
            screen.SelectedFinalSaveTarget!.Key.ShouldBe(older.Key, "a refresh keeps the item the operator chose");
            screen.SaveSummary.ShouldContain(string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_TypeTiff,
                offer.Artifact.PhysicalWidthMm!.Value, offer.Artifact.PhysicalHeightMm!.Value));
            screen.FinalSaveStatus.ShouldBe(Strings.FinalSave_ApprovedNotSaved,
                "an earlier size is not obsolete merely because a newer size is pending");
            screen.ChangeLocationCommand.Execute(null);
            screen.CanSaveApproved.ShouldBeTrue();
            screen.SaveApprovedIdentity.ShouldNotBeNull();

            delivery.HoldDelivery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task saving = screen.SaveApprovedCommand.ExecuteAsync(null);
            await delivery.DeliveryEntered.Task;
            screen.IsFinalSaveWorking.ShouldBeTrue();
            screen.CanCancelSave.ShouldBeTrue();

            // The operator moves to the pending size while the older size is still saving.
            FinalSaveTargetRow pendingNow = screen.FinalSaveTargets.Single(r => r.Key == pending.Key);
            screen.SelectedFinalSaveTarget = pendingNow;
            screen.IsFinalSaveWorking.ShouldBeFalse();
            screen.CanConfirmAndSave.ShouldBeFalse("one save at a time");
            delivery.HoldDelivery.SetResult();
            await saving;
            await screen.FinalSaveFactsLoaded;

            screen.SelectedFinalSaveTarget!.Key.ShouldBe(pending.Key, "a late result never retargets the selection");
            screen.FinalSaveStatus.ShouldContain(screen.ConfirmAndSaveLabel);
            screen.FinalSaveSavedPath.ShouldBeEmpty();
            screen.SelectedFinalSaveTarget = screen.FinalSaveTargets.Single(r => r.Key == older.Key);
            screen.FinalSaveSavedPath.ShouldBe(Path.Combine(folder, offer.Artifact.SuggestedFileName));
            (await FinalSaveFixtures.LoadAsync(h, sizeA.Id)).Outputs.Single(o => o.Id.Value == sizeB.CurrentArtefact!.RevisionId.Value)
                .ReviewState.ShouldBe(ReviewState.NotReviewed, "saving one size never approves another");
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    [Fact]
    public async Task Reopened_screen_restores_the_original_saved_destination_and_unreadable_history_claims_nothing()
    {
        using CultureScope culture = new("en");
        using SessionServiceHarness h = new();
        ISessionService workflow = FinalSaveFixtures.TiffService(h);
        var delivery = new ObservedDeliveryService(FinalSaveFixtures.Delivery(h));
        string original = FinalSaveFixtures.NewFolder("ui-original");
        string newer = FinalSaveFixtures.NewFolder("ui-newer");
        try
        {
            SessionView first = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow, outputName: "first");
            var coordinator = new FinalSaveCoordinator(workflow, delivery, "tester");
            FinalSaveResult saved = await coordinator.ConfirmAndSaveAsync(
                FinalSaveFixtures.Confirm(first, original, "first.tif"), null, CancellationToken.None);
            SessionView second = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow, outputName: "second");
            (await coordinator.ConfirmAndSaveAsync(FinalSaveFixtures.Confirm(second, newer, "second.tif"), null, CancellationToken.None))
                .Delivery!.Code.ShouldBe(DeliveryCode.Delivered);

            SessionView reopened = (await workflow.LoadAsync(first.Id, CancellationToken.None)).Value;
            SessionViewModel screen = await OpenAsync(h, workflow, delivery, new StubFolderPicker(), new RecordingDeliveredFileShell(), reopened);
            screen.FinalSaveStatus.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_SavedPreviously, "first.tif", original));
            screen.FinalSaveSavedPath.ShouldBe(Path.Combine(original, "first.tif"));
            screen.SaveFolderText.ShouldBe(original, "an untouched draft shows the recorded save, not the newer default");
            screen.SaveFileNameText.ShouldBe("first.tif");
            screen.CanSaveApproved.ShouldBeFalse("reopening never proposes a silent second copy");
            (await delivery.GetLastSuccessfulDestinationAsync(CancellationToken.None)).Value!.Folder.ShouldBe(newer);
            screen.CanCheckSavedFile.ShouldBeTrue();
            screen.CanOpenContainingFolder.ShouldBeTrue();
            int reconciles = delivery.ReconcileCalls;

            await screen.CheckSavedFileCommand.ExecuteAsync(null);
            screen.FinalSaveStatus.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_Saved, "first.tif", original));
            File.Delete(Path.Combine(original, "first.tif"));
            await screen.CheckSavedFileCommand.ExecuteAsync(null);
            screen.FinalSaveStatus.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_Missing, Path.Combine(original, "first.tif")));
            screen.CanSaveAnotherCopy.ShouldBeTrue();
            delivery.ReplaceCalls.ShouldBe(0, "a missing copy is never replaced automatically");
            delivery.ReconcileCalls.ShouldBe(reconciles, "opening and checking never reconcile");
            await screen.SaveAnotherCopyCommand.ExecuteAsync(null);
            await screen.FinalSaveFactsLoaded;
            delivery.ReplaceCalls.ShouldBe(1);
            File.Exists(Path.Combine(original, "first.tif")).ShouldBeTrue();
            screen.FinalSaveStatus.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_Saved, "first.tif", original));

            delivery.FailHistory = true;
            SessionViewModel unreadable = await OpenAsync(h, workflow, delivery, new StubFolderPicker(), new RecordingDeliveredFileShell(), reopened);
            unreadable.FinalSaveStatus.ShouldBe(Strings.FinalSave_HistoryUnavailable);
            unreadable.FinalSaveStatus.ShouldNotContain("not saved");
            saved.Delivery!.Code.ShouldBe(DeliveryCode.Delivered);
        }
        finally { FinalSaveFixtures.Remove(original, newer); }
    }

    [Fact]
    public async Task Unreviewed_png_is_visibly_blocked_without_a_rejected_label_or_any_action()
    {
        using CultureScope culture = new("en");
        using SessionServiceHarness h = new();
        ISessionService workflow = h.CreateService();
        SessionId id = await KeepOriginalExtentPersistenceTests.AtTrim(h, workflow, WorkflowType.PrepareAsset);
        await KeepOriginalExtentPersistenceTests.Execute(workflow, id, new WorkflowCommand.KeepOriginalExtent());
        SessionView promoted = await KeepOriginalExtentPersistenceTests.Execute(workflow, id, new WorkflowCommand.StartStep(StepKind.ApprovedPngExport));
        SessionViewModel screen = await OpenAsync(h, workflow, FinalSaveFixtures.Delivery(h), new StubFolderPicker(),
            new RecordingDeliveredFileShell(), promoted);
        screen.FinalSaveStatus.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_Ineligible,
            Strings.FinalSave_ReasonApprovalEvidenceMissing));
        screen.FinalSaveStatus.ShouldNotContain(Strings.FinalSave_ReasonRejected);
        screen.ShowsSaveDraft.ShouldBeFalse();
        screen.SaveDraftNotice.ShouldBeEmpty();
        (screen.CanSaveApproved || screen.CanConfirmAndSave || screen.CanOpenContainingFolder || screen.CanChangeLocation ||
         screen.CanRetryRecordedSave || screen.CanCheckSaveAgain).ShouldBeFalse();
    }

    [Fact]
    public void New_final_save_strings_exist_in_both_languages_with_matching_placeholders()
    {
        var manager = new ResourceManager("PrintFlow.App.Resources.Strings", typeof(SessionViewModel).Assembly);
        string[] keys = typeof(Strings).GetProperties(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Select(p => p.Name).Where(n => n.StartsWith("FinalSave_", StringComparison.Ordinal) ||
                                            n is "Session_NextFinalReview" or "Session_NextFinalReviewNeedsDraft" or "Session_NextCompletedNotSaved").ToArray();
        keys.Length.ShouldBe(95); // 89 + the closeout's record list and not-refreshed history + SCRUM-11147 FinalSave_TrimAdjustOpen
        foreach (string key in keys)
        {
            string en = manager.GetString(key, CultureInfo.GetCultureInfo("en"))!;
            string zh = manager.GetString(key, CultureInfo.GetCultureInfo("zh-CN"))!;
            en.ShouldNotBeNullOrWhiteSpace(key);
            zh.ShouldNotBeNullOrWhiteSpace(key);
            if (key != "FinalSave_TypePng") zh.ShouldNotBe(en, key);
            Placeholders(zh).ShouldBe(Placeholders(en), key);
        }

        static string[] Placeholders(string text) =>
            Regex.Matches(text, @"\{\d+(:[^}]*)?\}").Select(m => m.Value).Order(StringComparer.Ordinal).ToArray();
    }

    [Fact]
    public async Task A_second_size_on_the_same_screen_defaults_to_the_folder_just_saved_to()
    {
        using CultureScope culture = new("en");
        using SessionServiceHarness h = new();
        ISessionService workflow = FinalSaveFixtures.TiffService(h);
        SessionView sizeA = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow, 200);
        string folder = FinalSaveFixtures.NewFolder("ui-same-screen");
        var picker = new StubFolderPicker { Next = folder };
        SessionViewModel screen = await OpenAsync(h, workflow, FinalSaveFixtures.Delivery(h), picker,
            new RecordingDeliveredFileShell(), sizeA);
        try
        {
            screen.ChangeLocationCommand.Execute(null);
            await screen.ConfirmAndSaveCommand.ExecuteAsync(null);
            await screen.FinalSaveFactsLoaded;
            screen.FinalSaveSavedPath.ShouldNotBeEmpty();

            await screen.CompleteCommand.ExecuteAsync(null);
            await screen.AddAnotherSizeCommand.ExecuteAsync(null);
            SessionView sizeB = await FinalSaveFixtures.NextTiffSizeAsync(workflow, sizeA.Id, 250);
            screen.Open(sizeB);
            await screen.PreviewsLoaded;
            await screen.FinalSaveFactsLoaded;

            screen.SelectedFinalSaveTarget!.Label.ShouldContain("waiting for your review");
            screen.SaveFolderText.ShouldBe(folder, "no dialog is forced for each output");
            screen.SaveDraftNotice.ShouldBe(Strings.FinalSave_RememberedFolder);
            screen.CanConfirmAndSave.ShouldBeTrue();
            picker.Calls.ShouldBe(1);
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    [Fact]
    public async Task A_size_with_a_save_attempt_keeps_its_destination_when_another_size_changes_the_remembered_folder()
    {
        using CultureScope culture = new("en");
        using SessionServiceHarness h = new();
        ISessionService workflow = FinalSaveFixtures.TiffService(h);
        string remembered = FinalSaveFixtures.NewFolder("ui-r2-remembered");
        string chosen = FinalSaveFixtures.NewFolder("ui-r2-chosen");
        try
        {
            SessionView earlier = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow, outputName: "earlier");
            (await new FinalSaveCoordinator(workflow, FinalSaveFixtures.Delivery(h), "tester").ConfirmAndSaveAsync(
                FinalSaveFixtures.Confirm(earlier, remembered, "earlier.tif"), null, CancellationToken.None))
                .Delivery!.Code.ShouldBe(DeliveryCode.Delivered);

            SessionView sizeA = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow, 200, "job");
            File.WriteAllBytes(Path.Combine(remembered, sizeA.CurrentArtefact!.FileName), [5]);
            var picker = new StubFolderPicker();
            SessionViewModel screen = await OpenAsync(h, workflow, FinalSaveFixtures.Delivery(h), picker,
                new RecordingDeliveredFileShell(), sizeA);
            screen.SaveFolderText.ShouldBe(remembered);
            await screen.ConfirmAndSaveCommand.ExecuteAsync(null);
            await screen.FinalSaveFactsLoaded;
            string keyA = screen.SelectedFinalSaveTarget!.Key;
            string collided = screen.FinalSaveStatus;
            string? identityA = screen.SaveApprovedIdentity;

            await screen.CompleteCommand.ExecuteAsync(null);
            await screen.AddAnotherSizeCommand.ExecuteAsync(null);
            screen.Open(await FinalSaveFixtures.NextTiffSizeAsync(workflow, sizeA.Id, 250));
            await screen.PreviewsLoaded;
            await screen.FinalSaveFactsLoaded;
            picker.Next = chosen;
            screen.ChangeLocationCommand.Execute(null);
            await screen.ConfirmAndSaveCommand.ExecuteAsync(null);
            await screen.FinalSaveFactsLoaded;
            (await FinalSaveFixtures.Delivery(h).GetLastSuccessfulDestinationAsync(CancellationToken.None)).Value!.Folder.ShouldBe(chosen);

            screen.SelectedFinalSaveTarget = screen.FinalSaveTargets.Single(r => r.Key == keyA);
            screen.SaveFolderText.ShouldBe(remembered, "a result with a save attempt keeps its own destination");
            screen.SaveApprovedIdentity.ShouldBe(identityA, "its draft generation did not move");
            screen.FinalSaveStatus.ShouldBe(collided);
        }
        finally { FinalSaveFixtures.Remove(remembered, chosen); }
    }

    [Fact]
    public async Task History_shows_the_verified_or_unresolved_record_rather_than_only_the_newest()
    {
        using CultureScope culture = new("en");
        using SessionServiceHarness h = new();
        ISessionService workflow = FinalSaveFixtures.TiffService(h);
        string saved = FinalSaveFixtures.NewFolder("ui-hist-saved");
        string failed = FinalSaveFixtures.NewFolder("ui-hist-failed");
        string maybe = FinalSaveFixtures.NewFolder("ui-hist-maybe");
        string later = FinalSaveFixtures.NewFolder("ui-hist-later");
        try
        {
            // A verified save followed by a newer save that collided and saved nothing.
            SessionView first = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow, outputName: "first");
            var coordinator = new FinalSaveCoordinator(workflow, FinalSaveFixtures.Delivery(h), "tester");
            FinalSaveResult delivered = await coordinator.ConfirmAndSaveAsync(
                FinalSaveFixtures.Confirm(first, saved, "a.tif"), null, CancellationToken.None);
            File.WriteAllBytes(Path.Combine(failed, "b.tif"), [7]);
            (await coordinator.SaveApprovedAsync(FinalSaveFixtures.SaveApproved(delivered.Artifact!.Value, failed, "b.tif"),
                null, CancellationToken.None)).Delivery!.Code.ShouldBe(DeliveryCode.Collision);
            SessionViewModel reopened = await OpenAsync(h, workflow, FinalSaveFixtures.Delivery(h), new StubFolderPicker(),
                new RecordingDeliveredFileShell(), (await workflow.LoadAsync(first.Id, CancellationToken.None)).Value);
            reopened.FinalSaveStatus.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_SavedPreviously, "a.tif", saved));
            reopened.CanOpenContainingFolder.ShouldBeTrue();

            // An unresolved save behind a newer verified one is still offered for checking.
            SessionView second = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow, outputName: "second");
            var lossy = new FinalSaveCoordinator(workflow,
                FinalSaveFixtures.Delivery(h, new LostPublicationAckFileSystem(new Infrastructure.Delivery.WindowsDeliveryFileSystem())), "tester");
            FinalSaveResult uncertain = await lossy.ConfirmAndSaveAsync(
                FinalSaveFixtures.Confirm(second, maybe, "maybe.tif"), null, CancellationToken.None);
            uncertain.Delivery!.Code.ShouldBe(DeliveryCode.NeedsReconciliation);
            (await coordinator.SaveApprovedAsync(FinalSaveFixtures.SaveApproved(uncertain.Artifact!.Value, later, "later.tif"),
                null, CancellationToken.None)).Delivery!.Code.ShouldBe(DeliveryCode.Delivered);
            var delivery = new ObservedDeliveryService(FinalSaveFixtures.Delivery(h));
            SessionViewModel check = await OpenAsync(h, workflow, delivery, new StubFolderPicker(),
                new RecordingDeliveredFileShell(), (await workflow.LoadAsync(second.Id, CancellationToken.None)).Value);
            check.FinalSaveStatus.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_Uncertain, maybe));
            check.CanCheckSaveAgain.ShouldBeTrue();
            check.SaveFolderText.ShouldBe(maybe, "the draft shows the unresolved recorded destination");
            check.RecommendedCommand.ShouldBeSameAs(check.CheckSaveAgainCommand);

            // A recorded action that fails keeps "may have been saved"; it cannot be cancelled.
            delivery.ThrowOnReconcile = true;
            delivery.HoldReconcile = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task checking = check.CheckSaveAgainCommand.ExecuteAsync(null);
            await delivery.ReconcileEntered.Task;
            check.IsFinalSaveWorking.ShouldBeTrue();
            check.CanCancelSave.ShouldBeFalse("a recorded save may publish; it is never cancelled from the screen");
            delivery.HoldReconcile.SetResult();
            await checking;
            await check.FinalSaveFactsLoaded;
            check.FinalSaveStatus.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_Uncertain, maybe));
            check.FinalSaveAlert.ShouldBe(Strings.FinalSave_ActionIncomplete);
            check.CanCheckSaveAgain.ShouldBeTrue();

            delivery.ThrowOnReconcile = false;
            delivery.HoldReconcile = null;
            // A recheck that cannot reach the recorded folder still says "may have been saved".
            string away = maybe + "-away";
            Directory.Move(maybe, away);
            await check.CheckSaveAgainCommand.ExecuteAsync(null);
            await check.FinalSaveFactsLoaded;
            check.FinalSaveStatus.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_Uncertain, maybe));
            check.FinalSaveAlert.ShouldBe(Strings.FinalSave_ActionIncomplete);
            check.CanCheckSaveAgain.ShouldBeTrue();
            check.CanSaveApproved.ShouldBeFalse("no new request to the unresolved destination");
            Directory.Move(away, maybe);
            await check.CheckSaveAgainCommand.ExecuteAsync(null);
            check.FinalSaveStatus.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_Saved, "maybe.tif", maybe));
            check.FinalSaveAlert.ShouldBeEmpty();
        }
        finally { FinalSaveFixtures.Remove(saved, failed, maybe, later); }
    }

    [Theory]
    [InlineData(ScriptedCopyFileSystem.Mode.FailOnce)]
    [InlineData(ScriptedCopyFileSystem.Mode.HoldUntilCancelledOnce)]
    public async Task A_failed_or_cancelled_copy_keeps_approval_and_retry_reuses_the_same_request(ScriptedCopyFileSystem.Mode mode)
    {
        using CultureScope culture = new("en");
        using SessionServiceHarness h = new();
        ISessionService workflow = FinalSaveFixtures.TiffService(h);
        SessionView review = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
        SessionView approved = await KeepOriginalExtentPersistenceTests.Execute(workflow, review.Id,
            new WorkflowCommand.Approve(StepKind.PhotoshopOutput, review.CurrentArtefact!.Sha256));
        var files = new ScriptedCopyFileSystem(new Infrastructure.Delivery.WindowsDeliveryFileSystem(), mode);
        IApprovedArtifactDeliveryService delivery = FinalSaveFixtures.Delivery(h, files);
        string folder = FinalSaveFixtures.NewFolder("ui-copy");
        var picker = new StubFolderPicker { Next = folder };
        SessionViewModel screen = await OpenAsync(h, workflow, delivery, picker, new RecordingDeliveredFileShell(), approved);
        try
        {
            screen.ChangeLocationCommand.Execute(null);
            var counts = await FinalSaveFixtures.WorkCountsAsync(h, review.Id);
            Task saving = screen.SaveApprovedCommand.ExecuteAsync(null);
            await files.CopyEntered.Task;
            if (mode == ScriptedCopyFileSystem.Mode.HoldUntilCancelledOnce)
            {
                for (int i = 0; i < 200 && !screen.CanCancelSave; i++) await Task.Delay(10);
                screen.CanCancelSave.ShouldBeTrue("cancelling is offered while copying, before publication");
                screen.CancelSaveCommand.Execute(null);
            }
            await saving;
            await screen.FinalSaveFactsLoaded;

            string reason = mode == ScriptedCopyFileSystem.Mode.FailOnce ? Strings.FinalSave_FailCopy : Strings.FinalSave_FailCancelled;
            screen.FinalSaveStatus.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_ApprovedNotSavedReason, reason));
            screen.SaveApprovedLabel.ShouldBe(Strings.FinalSave_RetrySave);
            Directory.EnumerateFileSystemEntries(folder).ShouldBeEmpty("no partial or final file");
            DeliveryState first = (await delivery.GetDeliveryStateAsync(review.Id, null, CancellationToken.None)).Value.Single();

            await screen.SaveApprovedCommand.ExecuteAsync(null);
            await screen.FinalSaveFactsLoaded;
            screen.FinalSaveSavedPath.ShouldNotBeEmpty();
            DeliveryState retried = (await delivery.GetDeliveryStateAsync(review.Id, null, CancellationToken.None)).Value.Single();
            retried.RequestId.ShouldBe(first.RequestId, "the unchanged captured intent keeps its RequestId");
            retried.Status.ShouldBe("Delivered");
            (await FinalSaveFixtures.WorkCountsAsync(h, review.Id)).ShouldBe(counts, "no approval or processing on retry");
            picker.Calls.ShouldBe(1);
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    [Fact]
    public async Task Failed_png_preparation_after_confirmation_stays_approved_and_is_finished_by_the_existing_step()
    {
        using CultureScope culture = new("en");
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        var workflow = new ScriptedApprovalSessionService(service) { FailPromotion = true };
        SessionView review = await FinalSaveFixtures.PngAtFinalReviewAsync(h, service);
        string folder = FinalSaveFixtures.NewFolder("ui-png-prep");
        var delivery = new ObservedDeliveryService(FinalSaveFixtures.Delivery(h));
        SessionViewModel screen = await OpenAsync(h, workflow, delivery, new StubFolderPicker { Next = folder },
            new RecordingDeliveredFileShell(), review);
        try
        {
            screen.ChangeLocationCommand.Execute(null);
            await screen.ConfirmAndSaveCommand.ExecuteAsync(null);
            await screen.FinalSaveFactsLoaded;
            screen.FinalSaveStatus.ShouldBe(Strings.FinalSave_PngPreparationPending);
            screen.ShowsSaveDraft.ShouldBeFalse();
            screen.CanRunStep.ShouldBeTrue("the existing lawful preparation step finishes it");
            delivery.DeliverCalls.ShouldBe(0);
            (await FinalSaveFixtures.LoadAsync(h, review.Id)).Steps.Single(s => s.Step == StepKind.Trim).State.ShouldBe(StepState.Approved);

            workflow.FailPromotion = false;
            await screen.RunStepCommand.ExecuteAsync(null);
            await screen.FinalSaveFactsLoaded;
            screen.SaveFolderText.ShouldBe(folder, "the draft survives preparation");
            screen.CanSaveApproved.ShouldBeTrue();
            await screen.SaveApprovedCommand.ExecuteAsync(null);
            await screen.FinalSaveFactsLoaded;
            screen.FinalSaveSavedPath.ShouldBe(Path.Combine(folder, "keep-extent.png"));
            workflow.ApproveCalls.ShouldBe(1);
            (await FinalSaveFixtures.WorkCountsAsync(h, review.Id)).Promotions.ShouldBe(1);
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    // --- SCRUM-11145 closeout: P3 reassessment against AC4/AC7 and design §7.2 ---------------

    [Fact]
    public async Task Every_unresolved_save_of_a_result_stays_reachable_with_its_own_destination()
    {
        using CultureScope culture = new("en");
        using SessionServiceHarness h = new();
        ISessionService workflow = FinalSaveFixtures.TiffService(h);
        string first = FinalSaveFixtures.NewFolder("ui-two-unresolved-first");
        string second = FinalSaveFixtures.NewFolder("ui-two-unresolved-second");
        try
        {
            SessionView review = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow, outputName: "twice");
            FinalSaveResult earlier = await Lossy(h, workflow).ConfirmAndSaveAsync(
                FinalSaveFixtures.Confirm(review, first, "twice.tif"), null, CancellationToken.None);
            earlier.Delivery!.Code.ShouldBe(DeliveryCode.NeedsReconciliation);
            (await Lossy(h, workflow).SaveApprovedAsync(FinalSaveFixtures.SaveApproved(earlier.Artifact!.Value, second, "twice.tif"),
                null, CancellationToken.None)).Delivery!.Code.ShouldBe(DeliveryCode.NeedsReconciliation);

            var delivery = new ObservedDeliveryService(FinalSaveFixtures.Delivery(h));
            SessionViewModel screen = await OpenAsync(h, workflow, delivery, new StubFolderPicker(),
                new RecordingDeliveredFileShell(), (await workflow.LoadAsync(review.Id, CancellationToken.None)).Value);
            IReadOnlyList<DeliveryState> records = (await delivery.GetDeliveryStateAsync(review.Id, null, CancellationToken.None)).Value;
            Guid newer = records.Single(r => r.RequestedFolder == second).DeliveryId;
            Guid older = records.Single(r => r.RequestedFolder == first).DeliveryId;

            // The compact default speaks about one save; the other stays listed with its own destination.
            screen.FinalSaveStatus.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_Uncertain, second));
            screen.HasFinalSaveRecords.ShouldBeTrue();
            screen.FinalSaveRecords.Select(r => r.DeliveryId).ShouldBe([newer, older]);
            screen.FinalSaveRecords.Single(r => r.DeliveryId == older).Label.ShouldBe(
                string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_RecordUnverified, "twice.tif", first));
            screen.SelectedFinalSaveRecord!.DeliveryId.ShouldBe(newer);
            int reconciles = delivery.ReconcileCalls;

            // Choosing the earlier save is only a view change; its own action then acts on it.
            screen.SelectedFinalSaveRecord = screen.FinalSaveRecords.Single(r => r.DeliveryId == older);
            delivery.ReconcileCalls.ShouldBe(reconciles, "choosing a record checks nothing");
            screen.FinalSaveStatus.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_Uncertain, first));
            screen.SaveFolderText.ShouldBe(first, "the draft shows that save's recorded destination");
            screen.RecordedActionIdentity.ShouldNotBeNull().ShouldContain(older.ToString("N"));
            screen.CanCheckSaveAgain.ShouldBeTrue();
            await screen.CheckSaveAgainCommand.ExecuteAsync(null);
            await screen.FinalSaveFactsLoaded;
            screen.FinalSaveStatus.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_Saved, "twice.tif", first));
            screen.SelectedFinalSaveRecord!.DeliveryId.ShouldBe(older);

            // The newer unresolved save did not disappear behind the verified one.
            screen.SelectedFinalSaveRecord = screen.FinalSaveRecords.Single(r => r.DeliveryId == newer);
            screen.FinalSaveStatus.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_Uncertain, second));
            await screen.CheckSaveAgainCommand.ExecuteAsync(null);
            await screen.FinalSaveFactsLoaded;
            screen.FinalSaveStatus.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_Saved, "twice.tif", second));
            delivery.ReconcileCalls.ShouldBe(reconciles + 2);
            delivery.DeliverCalls.ShouldBe(0, "no new copy was made");
            screen.FinalSaveRecords.Single(r => r.DeliveryId == older).Label.ShouldStartWith("Saved ");
        }
        finally { FinalSaveFixtures.Remove(first, second); }
    }

    // One thread throughout, as on the UI thread: the language event is a thread-affine weak event.
    [Fact]
    public void The_recorded_saves_list_follows_a_language_switch_without_a_reload() =>
        SingleThreadContext.Run(RecordedSavesFollowTheLanguageAsync);

    private static async Task RecordedSavesFollowTheLanguageAsync()
    {
        using OperatorCultureScope culture = new("en");
        using SessionServiceHarness h = new();
        ISessionService workflow = FinalSaveFixtures.TiffService(h);
        string first = FinalSaveFixtures.NewFolder("ui-records-language-first");
        string second = FinalSaveFixtures.NewFolder("ui-records-language-second");
        try
        {
            SessionView review = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow, outputName: "words");
            FinalSaveResult verified = await new FinalSaveCoordinator(workflow, FinalSaveFixtures.Delivery(h), "tester")
                .ConfirmAndSaveAsync(FinalSaveFixtures.Confirm(review, first, "words.tif"), null, CancellationToken.None);
            await Lossy(h, workflow).SaveApprovedAsync(FinalSaveFixtures.SaveApproved(verified.Artifact!.Value, second, "words.tif"),
                null, CancellationToken.None);
            var localisation = new LocalisationService(h.Settings);
            localisation.Use(OperatorLanguage.English);
            var screen = new SessionViewModel(workflow, h.Previews, h.TiffReviews, new RecordingNavigation(), null, localisation,
                new FinalSaveCoordinator(workflow, FinalSaveFixtures.Delivery(h), "tester"), new StubFolderPicker(), new RecordingDeliveredFileShell());
            screen.Open((await workflow.LoadAsync(review.Id, CancellationToken.None)).Value);
            await screen.PreviewsLoaded;
            await screen.FinalSaveFactsLoaded;
            FinalSaveRecordRow unverified = screen.FinalSaveRecords.Single(r => !r.Label.StartsWith("Saved", StringComparison.Ordinal));
            unverified.Label.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_RecordUnverified, "words.tif", second));
            List<string?> changed = [], screenChanged = [];
            unverified.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            screen.PropertyChanged += (_, e) => screenChanged.Add(e.PropertyName);

            localisation.Use(OperatorLanguage.SimplifiedChinese);

            screenChanged.ShouldContain(string.Empty, "the screen received the language event");
            changed.ShouldContain(nameof(FinalSaveRecordRow.Label));
            unverified.Label.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_RecordUnverified, "words.tif", second));
            unverified.Label.ShouldStartWith("尚未核实");
        }
        finally { FinalSaveFixtures.Remove(first, second); }
    }

    [Fact]
    public async Task A_collision_found_while_checking_an_unresolved_save_keeps_its_check_again_action()
    {
        using CultureScope culture = new("en");
        using SessionServiceHarness h = new();
        ISessionService workflow = FinalSaveFixtures.TiffService(h);
        SessionView review = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
        SessionView approved = await KeepOriginalExtentPersistenceTests.Execute(workflow, review.Id,
            new WorkflowCommand.Approve(StepKind.PhotoshopOutput, review.CurrentArtefact!.Sha256));
        var files = new RacedPublicationFileSystem(new Infrastructure.Delivery.WindowsDeliveryFileSystem());
        var delivery = new ObservedDeliveryService(FinalSaveFixtures.Delivery(h, files));
        string folder = FinalSaveFixtures.NewFolder("ui-raced");
        SessionViewModel screen = await OpenAsync(h, workflow, delivery, new StubFolderPicker { Next = folder },
            new RecordingDeliveredFileShell(), approved);
        try
        {
            screen.ChangeLocationCommand.Execute(null);
            await screen.SaveApprovedCommand.ExecuteAsync(null);
            await screen.FinalSaveFactsLoaded;
            screen.CanCheckSaveAgain.ShouldBeTrue();
            Guid recorded = (await delivery.GetDeliveryStateAsync(review.Id, null, CancellationToken.None)).Value.Single().DeliveryId;
            string name = screen.SaveFileNameText;
            string suggestion = Path.GetFileNameWithoutExtension(name) + " (2)" + Path.GetExtension(name);

            await screen.CheckSaveAgainCommand.ExecuteAsync(null);
            await screen.FinalSaveFactsLoaded;

            screen.FinalSaveStatus.ShouldContain(string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_Collision, name, suggestion));
            screen.CanUseSuggestedName.ShouldBeTrue("a new name is a safe next action");
            screen.CanCheckSaveAgain.ShouldBeTrue("the recorded save is still unresolved; its recovery action stays without a reload");
            screen.RecordedActionIdentity.ShouldNotBeNull().ShouldContain(recorded.ToString("N"));
            File.ReadAllBytes(Path.Combine(folder, name)).ShouldBe(files.ForeignBytes, "the other program's file is never overwritten");
            delivery.DeliverCalls.ShouldBe(1);
            delivery.ReconcileCalls.ShouldBe(1);
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_save_that_throws_while_nothing_can_be_refreshed_shows_only_dated_history(bool savedBefore)
    {
        using CultureScope culture = new("en");
        using SessionServiceHarness h = new();
        var workflow = new ScriptedApprovalSessionService(FinalSaveFixtures.TiffService(h));
        SessionView review = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
        SessionView approved = await KeepOriginalExtentPersistenceTests.Execute(workflow, review.Id,
            new WorkflowCommand.Approve(StepKind.PhotoshopOutput, review.CurrentArtefact!.Sha256));
        var delivery = new ObservedDeliveryService(FinalSaveFixtures.Delivery(h));
        string earlier = FinalSaveFixtures.NewFolder("ui-dated-earlier");
        string folder = FinalSaveFixtures.NewFolder("ui-dated");
        var picker = new StubFolderPicker { Next = earlier };
        SessionViewModel screen = await OpenAsync(h, workflow, delivery, picker, new RecordingDeliveredFileShell(), approved);
        try
        {
            if (savedBefore)
            {
                screen.ChangeLocationCommand.Execute(null);
                await screen.SaveApprovedCommand.ExecuteAsync(null);
                await screen.FinalSaveFactsLoaded;
                screen.FinalSaveSavedPath.ShouldNotBeEmpty();
            }
            picker.Next = folder;
            screen.ChangeLocationCommand.Execute(null);
            delivery.ThrowOnDeliver = true;
            workflow.FailLoads = true;
            await screen.SaveApprovedCommand.ExecuteAsync(null);
            await screen.FinalSaveFactsLoaded;

            screen.FinalSaveAlert.ShouldBe(Strings.FinalSave_ActionIncomplete);
            screen.FinalSaveStatus.ShouldNotBe(Strings.FinalSave_ApprovedNotSaved,
                "history read before the failed save is not evidence that nothing was saved");
            screen.IsFinalSaveUnsaved.ShouldBeFalse("the status panel makes no unsaved claim either");
            string shown = string.Join("\n", screen.FinalSaveStatus, screen.FinalSaveSecondary);
            if (savedBefore)
                screen.FinalSaveStatus.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_SavedPreviously,
                    screen.SaveFileNameText, earlier), "the earlier verified save stays, dated and unchecked");
            shown.ShouldNotContain(string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_Saved, screen.SaveFileNameText, folder));
            shown.ShouldContain("refreshed", Case.Insensitive, "retained history is marked as not refreshed");
            screen.SaveFolderText.ShouldBe(folder, "the draft stays separate from the retained history");
        }
        finally { FinalSaveFixtures.Remove(earlier, folder); }
    }

    [Fact]
    public async Task A_rejected_size_has_no_save_action_and_its_output_row_says_why()
    {
        using CultureScope culture = new("en");
        using SessionServiceHarness h = new();
        ISessionService workflow = FinalSaveFixtures.TiffService(h);
        SessionView sizeA = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow, 200);
        (await workflow.ExecuteAsync(sizeA.Id, new WorkflowCommand.Approve(StepKind.PhotoshopOutput, sizeA.CurrentArtefact!.Sha256),
            "tester", CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await workflow.ExecuteAsync(sizeA.Id, new WorkflowCommand.Complete(), "tester", CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await workflow.ExecuteAsync(sizeA.Id, new WorkflowCommand.AddAnotherSize(), "tester", CancellationToken.None)).IsSuccess.ShouldBeTrue();
        SessionView sizeB = await FinalSaveFixtures.NextTiffSizeAsync(workflow, sizeA.Id, 250);
        OperationResultShould(await workflow.ExecuteAsync(sizeA.Id, new WorkflowCommand.Reject(StepKind.PhotoshopOutput,
            sizeB.CurrentArtefact!.Sha256, RejectionReason.DimensionIssue), "tester", CancellationToken.None));
        SessionViewModel screen = await OpenAsync(h, workflow, FinalSaveFixtures.Delivery(h), new StubFolderPicker(),
            new RecordingDeliveredFileShell(), (await workflow.LoadAsync(sizeA.Id, CancellationToken.None)).Value);

        string rejectedName = sizeB.CurrentArtefact.FileName;
        screen.FinalSaveTargets.Select(r => r.Label).ShouldAllBe(label => !label.Contains(rejectedName),
            "an ineligible output has no save or open action");
        PrintOutputRow row = screen.Outputs.Single(o => o.FileName == rejectedName);
        row.Review.ShouldBe(Strings.ReviewState_Rejected, "the output's own status says why it cannot be saved");
        screen.FinalSaveTargets.Count.ShouldBe(1, "the approved size stays saveable");

        static void OperationResultShould(PrintFlow.Domain.Results.OperationResult<SessionView> result) =>
            result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);
    }

    [Fact]
    public void Late_progress_from_a_finished_save_never_changes_the_save_now_running()
    {
        SingleThreadContext.Run(async () =>
        {
            using CultureScope culture = new("en");
            using SessionServiceHarness h = new();
            ISessionService workflow = FinalSaveFixtures.TiffService(h);
            SessionView sizeA = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow, 200);
            (await workflow.ExecuteAsync(sizeA.Id, new WorkflowCommand.Approve(StepKind.PhotoshopOutput, sizeA.CurrentArtefact!.Sha256),
                "tester", CancellationToken.None)).IsSuccess.ShouldBeTrue();
            (await workflow.ExecuteAsync(sizeA.Id, new WorkflowCommand.Complete(), "tester", CancellationToken.None)).IsSuccess.ShouldBeTrue();
            (await workflow.ExecuteAsync(sizeA.Id, new WorkflowCommand.AddAnotherSize(), "tester", CancellationToken.None)).IsSuccess.ShouldBeTrue();
            SessionView sizeB = await FinalSaveFixtures.NextTiffSizeAsync(workflow, sizeA.Id, 250);
            SessionView both = await KeepOriginalExtentPersistenceTests.Execute(workflow, sizeA.Id,
                new WorkflowCommand.Approve(StepKind.PhotoshopOutput, sizeB.CurrentArtefact!.Sha256));
            var delivery = new ObservedDeliveryService(FinalSaveFixtures.Delivery(h));
            string folderA = FinalSaveFixtures.NewFolder("ui-late-a");
            string folderB = FinalSaveFixtures.NewFolder("ui-late-b");
            var picker = new StubFolderPicker { Next = folderA };
            SessionViewModel screen = await OpenAsync(h, workflow, delivery, picker, new RecordingDeliveredFileShell(), both);
            try
            {
                screen.FinalSaveTargets.Count.ShouldBe(2);
                FinalSaveTargetRow rowA = screen.FinalSaveTargets[0], rowB = screen.FinalSaveTargets[1];
                DeliveryProgress Copied(long bytes) => new(Guid.Empty, default, Guid.Empty, DeliveryPhase.Copying, bytes, 100);

                // Operation A: its own progress is shown, then it finishes.
                screen.SelectedFinalSaveTarget = rowA;
                screen.ChangeLocationCommand.Execute(null);
                delivery.HoldDelivery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Task savingA = screen.SaveApprovedCommand.ExecuteAsync(null);
                await delivery.DeliveryStarted.WaitAsync();
                IProgress<DeliveryProgress> sinkA = delivery.DeliverySinks[0]!;
                sinkA.Report(Copied(10));
                await Task.Yield();
                screen.FinalSaveProgress.ShouldBe(0.1, "a matching current event is applied");
                delivery.HoldDelivery.SetResult();
                await savingA;
                await screen.FinalSaveFactsLoaded;
                string savedA = screen.FinalSaveSavedPath;
                savedA.ShouldStartWith(folderA);

                // Operation B on the other size, held while A's sink reports late.
                screen.SelectedFinalSaveTarget = rowB;
                picker.Next = folderB;
                screen.ChangeLocationCommand.Execute(null);
                (string Name, string Folder, string? Identity) draftB = (screen.SaveFileNameText, screen.SaveFolderText, screen.SaveApprovedIdentity);
                delivery.HoldDelivery = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Task savingB = screen.SaveApprovedCommand.ExecuteAsync(null);
                await delivery.DeliveryStarted.WaitAsync();
                IProgress<DeliveryProgress> sinkB = delivery.DeliverySinks[1]!;
                sinkB.Report(Copied(25));
                await Task.Yield();
                screen.FinalSaveProgress.ShouldBe(0.25);
                string savingStatus = screen.FinalSaveStatus;
                savingStatus.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_StageSaving, folderB));

                sinkA.Report(Copied(90));
                sinkA.Report(new DeliveryProgress(Guid.Empty, default, Guid.Empty, DeliveryPhase.Publishing));
                await Task.Yield();
                screen.FinalSaveProgress.ShouldBe(0.25, "progress from the finished operation is ignored");
                screen.FinalSaveStatus.ShouldBe(savingStatus, "its phase is ignored too");
                screen.SelectedFinalSaveTarget!.Key.ShouldBe(rowB.Key);

                sinkB.Report(Copied(50));
                await Task.Yield();
                screen.FinalSaveProgress.ShouldBe(0.5, "the running operation's own progress still applies");

                delivery.HoldDelivery.SetResult();
                await savingB;
                await screen.FinalSaveFactsLoaded;
                screen.FinalSaveSavedPath.ShouldStartWith(folderB);
                screen.SaveFolderText.ShouldBe(draftB.Folder);
                screen.SaveFileNameText.ShouldBe(draftB.Name);
                screen.SelectedFinalSaveTarget = screen.FinalSaveTargets.Single(r => r.Key == rowA.Key);
                screen.FinalSaveSavedPath.ShouldBe(savedA, "the earlier result stays with its own item");
            }
            finally { FinalSaveFixtures.Remove(folderA, folderB); }
        });
    }

    private static FinalSaveCoordinator Lossy(SessionServiceHarness h, ISessionService workflow) => new(workflow,
        FinalSaveFixtures.Delivery(h, new LostPublicationAckFileSystem(new Infrastructure.Delivery.WindowsDeliveryFileSystem())), "tester");

    /// <summary>
    /// A single-threaded context: every posted callback, including a Progress&lt;T&gt; report,
    /// runs in the order it was posted, so a late report is applied before a later await resumes.
    /// </summary>
    private sealed class SingleThreadContext : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

        public override void Post(SendOrPostCallback d, object? state)
        {
            try { _queue.Add((d, state)); }
            catch (InvalidOperationException) { } // posted after the test finished
        }

        public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();

        public static void Run(Func<Task> body)
        {
            SynchronizationContext? previous = Current;
            SingleThreadContext context = new();
            SetSynchronizationContext(context);
            try
            {
                Task task = body();
                task.ContinueWith(_ => context._queue.CompleteAdding(), TaskScheduler.Default);
                foreach ((SendOrPostCallback callback, object? state) in context._queue.GetConsumingEnumerable()) callback(state);
                task.GetAwaiter().GetResult();
            }
            finally { SetSynchronizationContext(previous); }
        }
    }

    public static TheoryData<string, string, double, double> RenderCases => new()
    {
        { "pending-no-folder", "en", 1000, 700 }, { "pending-no-folder", "zh-CN", 1000, 700 },
        { "collision", "en", 1000, 700 }, { "collision", "zh-CN", 1000, 700 },
        { "saved-tiff", "en", 1920, 1040 }, { "saved-tiff", "zh-CN", 1920, 1040 },
        { "saved-tiff", "zh-CN", 1000, 700 },
        { "blocked", "en", 1000, 700 }, { "blocked", "zh-CN", 1000, 700 },
        { "history", "en", 1920, 1040 }, { "history", "zh-CN", 1000, 700 },
        { "pending-remembered", "en", 1000, 700 }, { "pending-remembered", "zh-CN", 1920, 1040 },
        { "save-failed", "en", 1000, 700 }, { "save-failed", "zh-CN", 1000, 700 },
        { "records", "en", 1000, 700 }, { "records", "zh-CN", 1000, 700 }, { "records", "zh-CN", 1920, 1040 },
    };

    [Theory]
    [MemberData(nameof(RenderCases))]
    public async Task Rendered_final_save_section_binds_wraps_and_keeps_the_review_layout(string state, string language,
        double width, double height)
    {
        using CultureScope culture = new(language);
        using SessionServiceHarness h = new();
        string folder = FinalSaveFixtures.NewFolder("render");
        try
        {
            (SessionViewModel screen, SessionViewModel baseline) = await ArrangeAsync(h, state, folder);
            Size viewport = new(width, height);
            var facts = WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = screen }, viewport, tree =>
            {
                Border panel = tree.OfType<Border>().Single(b => AutomationProperties.GetAutomationId(b) == "Session.FinalSave");
                List<DependencyObject> inside = [];
                Collect(panel, inside);
                var buttons = inside.OfType<Button>().Where(b => Shown(b, panel)).ToList();
                var tooWide = inside.OfType<TextBlock>().Where(t => Shown(t, panel) && t.ActualWidth > panel.ActualWidth + 0.5).ToList();
                ReviewApprovalButton? combined = inside.OfType<ReviewApprovalButton>().SingleOrDefault(b =>
                    AutomationProperties.GetAutomationId(b) == "Session.FinalSave.ConfirmAndSave");
                Grid content = tree.OfType<Grid>().First(g => g.ColumnDefinitions.Count == 2 && g.ColumnDefinitions[1].Width.Value == 360);
                return (PanelWidth: panel.ActualWidth, RightWidth: content.ColumnDefinitions[1].ActualWidth,
                    LeftWidth: content.ColumnDefinitions[0].ActualWidth, VisibleButtons: buttons.Count,
                    AllTabStops: buttons.All(b => b.Focusable && KeyboardNavigationTabStop(b)), AnyDefault: tree.OfType<Button>().Any(b => b.IsDefault),
                    TooWide: tooWide.Count, CombinedIdentity: combined?.TargetIdentity,
                    Names: buttons.Select(b => AutomationProperties.GetName(b)).ToList(),
                    Records: inside.OfType<ComboBox>().Where(c => Shown(c, panel) &&
                        AutomationProperties.GetAutomationId(c) == "Session.FinalSave.Record")
                        .Select(c => (Name: AutomationProperties.GetName(c), c.Items.Count, c.Focusable, Width: c.ActualWidth)).ToList(),
                    ExistingApprove: tree.OfType<ReviewApprovalButton>().Count(b => AutomationProperties.GetAutomationId(b) == "Session.Approve"));
            });
            var baselineFacts = WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = baseline }, viewport,
                tree =>
                {
                    Grid content = tree.OfType<Grid>().First(g => g.ColumnDefinitions.Count == 2 && g.ColumnDefinitions[1].Width.Value == 360);
                    return (Left: content.ColumnDefinitions[0].ActualWidth, Panels: tree.OfType<Border>()
                        .Count(b => AutomationProperties.GetAutomationId(b) == "Session.FinalSave"));
                });

            baselineFacts.Facts.Panels.ShouldBe(0, "without the coordinator nothing is instantiated");
            facts.Facts.LeftWidth.ShouldBe(baselineFacts.Facts.Left, "the preview/crop column is unchanged");
            facts.Facts.RightWidth.ShouldBe(360);
            facts.Facts.PanelWidth.ShouldBeLessThanOrEqualTo(360);
            facts.Facts.TooWide.ShouldBe(0);
            facts.Facts.AnyDefault.ShouldBeFalse("no implicit Enter approval or save");
            facts.Facts.AllTabStops.ShouldBeTrue();
            facts.Facts.Names.ShouldAllBe(n => !string.IsNullOrWhiteSpace(n));
            facts.Facts.ExistingApprove.ShouldBe(1);
            if (state == "pending-no-folder")
            {
                facts.Facts.CombinedIdentity.ShouldBe(screen.ConfirmAndSaveIdentity);
                screen.CanConfirmAndSave.ShouldBeFalse();
            }
            if (state == "pending-remembered")
            {
                screen.CanConfirmAndSave.ShouldBeTrue();
                facts.Facts.Names.ShouldContain(screen.ConfirmAndSaveLabel);
                facts.Facts.CombinedIdentity.ShouldBe(screen.ConfirmAndSaveIdentity);
            }
            if (state == "save-failed")
            {
                screen.FinalSaveAlert.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.FinalSave_ApprovedNotSavedReason,
                    Strings.FinalSave_FailUnavailable));
                facts.Facts.Names.ShouldContain(screen.ChangeLocationLabel);
                screen.NextStepText.ShouldContain(screen.SaveApprovedLabel, Case.Sensitive, "the status panel agrees with the save section");
                screen.RecommendedCommand.ShouldBeSameAs(screen.SaveApprovedCommand);
            }
            if (state == "records")
            {
                facts.Facts.Records.Count.ShouldBe(1, "more than one recorded save shows the record list");
                facts.Facts.Records[0].Name.ShouldBe(screen.FinalSaveRecordsLabel);
                facts.Facts.Records[0].Count.ShouldBe(2);
                facts.Facts.Records[0].Focusable.ShouldBeTrue();
                facts.Facts.Records[0].Width.ShouldBeLessThanOrEqualTo(facts.Facts.PanelWidth);
                facts.Facts.Names.ShouldContain(screen.CheckAgainLabel);
            }
            else facts.Facts.Records.ShouldBeEmpty("a single recorded save needs no list");
            if (state == "blocked") facts.Facts.VisibleButtons.ShouldBe(0);
            if (state is "saved-tiff" or "history") facts.Facts.Names.ShouldContain(screen.OpenFolderLabel);
            if (state == "collision") facts.Facts.Names.ShouldContain(screen.UseSuggestionLabel);

            string? destination = Environment.GetEnvironmentVariable("PF_11145_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(destination))
                WpfRendering.CapturePng(() => new SessionScreenView { DataContext = screen }, viewport,
                    Path.Combine(destination, $"final-save-{state}-{language}-{width:0}x{height:0}.png"));
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    // Off-screen trees have no presentation source, so IsVisible is always false there.
    private static bool Shown(DependencyObject element, DependencyObject root)
    {
        for (DependencyObject? node = element; node is not null; node = System.Windows.Media.VisualTreeHelper.GetParent(node))
        {
            if (node is UIElement { Visibility: not Visibility.Visible }) return false;
            if (ReferenceEquals(node, root)) return true;
        }
        return false;
    }

    private static bool KeyboardNavigationTabStop(DependencyObject element) =>
        (bool)element.GetValue(System.Windows.Input.KeyboardNavigation.IsTabStopProperty);

    private static void Collect(DependencyObject node, List<DependencyObject> into)
    {
        into.Add(node);
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
            Collect(System.Windows.Media.VisualTreeHelper.GetChild(node, i), into);
    }

    private static async Task<(SessionViewModel Screen, SessionViewModel Baseline)> ArrangeAsync(
        SessionServiceHarness h, string state, string folder)
    {
        ISessionService workflow = state is "saved-tiff" or "history" or "save-failed" or "records" ? FinalSaveFixtures.TiffService(h) : h.CreateService();
        IApprovedArtifactDeliveryService delivery = FinalSaveFixtures.Delivery(h);
        var coordinator = new FinalSaveCoordinator(workflow, delivery, "tester");
        SessionView view;
        switch (state)
        {
            case "pending-no-folder":
                view = await FinalSaveFixtures.PngAtFinalReviewAsync(h, workflow);
                break;
            case "pending-remembered":
            case "collision":
                SessionView first = await FinalSaveFixtures.PngAtFinalReviewAsync(h, workflow);
                await coordinator.ConfirmAndSaveAsync(FinalSaveFixtures.Confirm(first, folder, "keep-extent.png"), null, CancellationToken.None);
                view = await FinalSaveFixtures.PngAtFinalReviewAsync(h, workflow);
                break;
            case "save-failed":
                SessionView approvedTiff = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
                view = await KeepOriginalExtentPersistenceTests.Execute(workflow, approvedTiff.Id,
                    new WorkflowCommand.Approve(StepKind.PhotoshopOutput, approvedTiff.CurrentArtefact!.Sha256));
                break;
            case "saved-tiff":
            case "history":
                SessionView tiff = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
                await coordinator.ConfirmAndSaveAsync(FinalSaveFixtures.Confirm(tiff, folder, "final-tiff_A4.tif"), null, CancellationToken.None);
                view = (await workflow.LoadAsync(tiff.Id, CancellationToken.None)).Value;
                break;
            case "records":
                SessionView twice = await FinalSaveFixtures.TiffAtFinalReviewAsync(h, workflow);
                FinalSaveResult verified = await coordinator.ConfirmAndSaveAsync(
                    FinalSaveFixtures.Confirm(twice, folder, "final-tiff_A4.tif"), null, CancellationToken.None);
                string elsewhere = Directory.CreateDirectory(Path.Combine(folder, "second folder")).FullName;
                await Lossy(h, workflow).SaveApprovedAsync(FinalSaveFixtures.SaveApproved(verified.Artifact!.Value, elsewhere,
                    "final-tiff_A4.tif"), null, CancellationToken.None);
                view = (await workflow.LoadAsync(twice.Id, CancellationToken.None)).Value;
                break;
            default:
                SessionId id = await KeepOriginalExtentPersistenceTests.AtTrim(h, workflow, WorkflowType.PrepareAsset);
                await KeepOriginalExtentPersistenceTests.Execute(workflow, id, new WorkflowCommand.KeepOriginalExtent());
                view = await KeepOriginalExtentPersistenceTests.Execute(workflow, id, new WorkflowCommand.StartStep(StepKind.ApprovedPngExport));
                break;
        }
        SessionViewModel screen = await OpenAsync(h, workflow, delivery, new StubFolderPicker(), new RecordingDeliveredFileShell(), view);
        if (state == "collision") await screen.ConfirmAndSaveCommand.ExecuteAsync(null);
        if (state == "save-failed")
        {
            var picker = new StubFolderPicker { Next = Path.Combine(folder, "was-removed") };
            screen = await OpenAsync(h, workflow, delivery, picker, new RecordingDeliveredFileShell(), view);
            screen.ChangeLocationCommand.Execute(null);
            await screen.SaveApprovedCommand.ExecuteAsync(null);
        }
        if (state == "saved-tiff") await screen.CheckSavedFileCommand.ExecuteAsync(null);
        await screen.FinalSaveFactsLoaded;
        var baseline = new SessionViewModel(workflow, h.Previews, h.TiffReviews, new RecordingNavigation());
        baseline.Open(view);
        await baseline.PreviewsLoaded;
        return (screen, baseline);
    }

    private static async Task<SessionViewModel> OpenAsync(SessionServiceHarness h, ISessionService workflow,
        IApprovedArtifactDeliveryService delivery, IDeliveryFolderPicker picker, IDeliveredFileShell shell, SessionView view)
    {
        var screen = new SessionViewModel(workflow, h.Previews, h.TiffReviews, new RecordingNavigation(), null, null,
            new FinalSaveCoordinator(workflow, delivery, "tester"), picker, shell);
        screen.Open(view);
        await screen.PreviewsLoaded;
        await screen.FinalSaveFactsLoaded;
        return screen;
    }

    private static async Task RefreshFactsAsync(SessionViewModel screen, ISessionService workflow, SessionId id)
    {
        screen.Open((await workflow.LoadAsync(id, CancellationToken.None)).Value);
        await screen.PreviewsLoaded;
        await screen.FinalSaveFactsLoaded;
    }

    private sealed class StubFolderPicker : IDeliveryFolderPicker
    {
        public string? Next { get; set; }
        public int Calls { get; private set; }
        public string? PickFolder(string dialogTitle, string? initialFolder)
        {
            Calls++;
            dialogTitle.ShouldNotBeNullOrWhiteSpace();
            return Next;
        }
    }

    /// <summary>
    /// Restores the raw operator selection (normally none), never the resolved fallback: turning
    /// the thread UI culture into an explicit selection would leak into later tests.
    /// </summary>
    private sealed class CultureScope : IDisposable
    {
        private static readonly System.Reflection.FieldInfo Selected = typeof(OperatorCulture).GetField(
            "_selected", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        private readonly CultureInfo? _previous = (CultureInfo?)Selected.GetValue(null);
        public CultureScope(string language) => OperatorCulture.Select(CultureInfo.GetCultureInfo(language));
        public void Dispose() => OperatorCulture.Select(_previous);
    }
}
