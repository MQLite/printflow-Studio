using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using PrintFlow.App.Startup;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Effects;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;
using static PrintFlow.Tests.Fixtures.CorrectionFixtures;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// SCRUM-11148: the request-bound colleague-correction path against the real SQLite repository,
/// the real workspace, the real package store and the real WIC importer, with GUID-owned temporary
/// databases and folders. Only Meitu (fake, counted), importer timing and commit outcomes are
/// scripted. No window, picker, Explorer or process is touched.
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class ColleagueCorrectionTests
{
    // -------------------------------------------------------------------------------------
    // AC1, AC3: asking hands off with a default reason, verified independent copies only
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Ask_without_a_note_hands_off_with_the_default_reason_and_independent_verified_copies()
    {
        using SessionServiceHarness h = new();
        CountingMeitu meitu = new(h.FakeMeitu);
        SessionService service = Service(h, meitu: meitu);
        SessionView review = await AtBackgroundRemovalReviewAsync(h, service);
        review.CanAskColleague.ShouldBeTrue();
        SessionAggregate before = await LoadAsync(h, review.Id);
        Dictionary<string, string> managed = ManagedFileHashes(h, before);
        AutomationLockState lockBefore = (await h.Repository.GetAutomationLockAsync(default)).Value;
        int meituBefore = meitu.Calls;

        SessionView handedOff = await MustAsync(RequestAsync(service, review));

        handedOff.State.ShouldBe(SessionState.HandedOff);
        handedOff.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        handedOff.CurrentArtefact!.RevisionId.ShouldBe(review.CurrentArtefact!.RevisionId, "R stays under review, unjudged");
        handedOff.Correction!.Mode.ShouldBe(CorrectionImportMode.Review);
        handedOff.CanImportCorrectedImage.ShouldBeTrue();
        handedOff.CanAskColleague.ShouldBeFalse();
        handedOff.CanSubmitManualResult.ShouldBeFalse();

        SessionAggregate after = await LoadAsync(h, review.Id);
        after.Session.HandOffReason.ShouldBe(WorkflowCommand.RequestColleagueCorrection.DefaultReason);
        after.Attempts.Count.ShouldBe(before.Attempts.Count);
        after.Revisions.Count.ShouldBe(before.Revisions.Count);
        after.Reviews.Count.ShouldBe(before.Reviews.Count);
        CorrectionRequest row = after.CorrectionRequests.Single();
        row.Status.ShouldBe(CorrectionRequestStatus.Ready);
        Revision r = after.Revisions.Single(x => x.Id == review.CurrentArtefact.RevisionId);
        Revision u = after.Revisions.Single(x => x.Id == r.SourceRevisionId);
        (row.HandedOutRevisionId, row.HandedOutSha256).ShouldBe((r.Id, r.Sha256));
        (row.ReferenceRevisionId, row.ReferenceSha256).ShouldBe((u.Id, u.Sha256));
        row.Note.ShouldBeNull();
        row.Folder.RelativePath.ShouldStartWith(after.Session.Workspace.RelativePath + "/Correction/");

        string folder = handedOff.Correction.FolderPath;
        string reference = Path.Combine(folder, row.ReferenceFileName);
        string working = Path.Combine(folder, row.WorkingFileName);
        row.ReferenceFileName.ShouldContain("REFERENCE (do not edit)");
        Hash(reference).ShouldBe(u.Sha256.Value);
        Hash(working).ShouldBe(r.Sha256.Value);
        (File.GetAttributes(reference) & FileAttributes.ReadOnly).ShouldBe(FileAttributes.ReadOnly);
        File.ReadAllText(Path.Combine(folder, "Instructions.txt")).ShouldContain(row.WorkingFileName);

        // Copies, not links: changing the working copy leaves R's managed file exactly as it was.
        File.WriteAllBytes(working, SyntheticImages.PngWithAlpha(6, 5, (_, _) => 17));
        ManagedFileHashes(h, after).ShouldBe(managed, "Source, InputSnapshot, Revisions and Approved are unchanged");
        (await h.Repository.GetAutomationLockAsync(default)).Value.ShouldBe(lockBefore);
        meitu.Calls.ShouldBe(meituBefore);

        // AC4: nothing resumes it — not Run, not re-entry, not an approval.
        (await service.ExecuteAsync(review.Id, new WorkflowCommand.StartStep(StepKind.BackgroundRemoval), "qa", default)).IsFailure.ShouldBeTrue();
        (await service.ExecuteAsync(review.Id, new WorkflowCommand.ReenterAutomation(), "qa", default)).IsFailure.ShouldBeTrue();
        (await service.ApproveExactReviewAsync(review.Id, StepKind.BackgroundRemoval, r.Id, r.Sha256, "qa", default)).IsFailure.ShouldBeTrue();
        (await LoadAsync(h, review.Id)).Session.State.ShouldBe(SessionState.HandedOff);
    }

    [Fact]
    public async Task A_note_becomes_the_recorded_reason_and_the_row_keeps_it()
    {
        using SessionServiceHarness h = new();
        SessionService service = Service(h);
        SessionView review = await AtBackgroundRemovalReviewAsync(h, service);

        await MustAsync(RequestAsync(service, review, note: "  Restore the hair on the left.  "));

        SessionAggregate saved = await LoadAsync(h, review.Id);
        saved.Session.HandOffReason.ShouldBe("Restore the hair on the left.");
        saved.CorrectionRequests.Single().Note.ShouldBe("Restore the hair on the left.");
        (await service.LoadAsync(review.Id, default)).Value.Correction!.Note.ShouldBe("Restore the hair on the left.");
    }

    [Fact]
    public async Task Execute_async_refuses_both_correction_commands_including_on_a_legacy_generic_handoff()
    {
        using SessionServiceHarness h = new();
        SessionService service = Service(h);
        SessionView review = await AtBackgroundRemovalReviewAsync(h, service);
        ArtefactView r = review.CurrentArtefact!;
        RevisionId u = review.UpstreamArtefact!.RevisionId;
        Sha256 uHash = review.UpstreamArtefact.Sha256;

        (await service.ExecuteAsync(review.Id, new WorkflowCommand.RequestColleagueCorrection(
            Guid.NewGuid(), r.RevisionId, r.Sha256, u, uHash), "qa", default)).IsFailure.ShouldBeTrue();
        (await LoadAsync(h, review.Id)).Session.State.ShouldBe(SessionState.Active);

        // A legacy generic handoff from review, even with the default reason text, grants nothing.
        await MustAsync(service.ExecuteAsync(review.Id, new WorkflowCommand.HandOff(
            StepKind.BackgroundRemoval, WorkflowCommand.RequestColleagueCorrection.DefaultReason), "qa", default));
        SessionView legacy = (await service.LoadAsync(review.Id, default)).Value;
        legacy.AvailableCommands.ShouldContain(CommandKind.ImportCorrectedImage, "the engine half alone would accept it");
        legacy.CanImportCorrectedImage.ShouldBeFalse("the projection needs a persisted request");
        legacy.Correction.ShouldBeNull();
        (await service.ExecuteAsync(review.Id, new WorkflowCommand.ImportCorrectedImage(
            Guid.NewGuid(), r.RevisionId, r.Sha256, CorrectedPng(h)), "qa", default)).IsFailure.ShouldBeTrue();
        (await LoadAsync(h, review.Id)).Attempts.ShouldNotContain(a => a.Operation == OperationKind.ManualResultImport);
    }

    [Fact]
    public async Task A_repeat_of_the_same_intent_or_a_second_intent_never_makes_a_second_package()
    {
        using SessionServiceHarness h = new();
        SessionService service = Service(h);
        SessionView review = await AtBackgroundRemovalReviewAsync(h, service);
        Guid intent = Guid.NewGuid();
        SessionView first = await MustAsync(RequestAsync(service, review, requestId: intent));

        SessionView repeat = await MustAsync(RequestAsync(service, review, requestId: intent));
        SessionView another = await MustAsync(RequestAsync(service, review, requestId: Guid.NewGuid()));

        repeat.Correction!.RequestId.ShouldBe(intent);
        another.Correction!.RequestId.ShouldBe(intent);
        (await LoadAsync(h, review.Id)).CorrectionRequests.Count.ShouldBe(1);
        Directory.GetDirectories(Path.GetDirectoryName(first.Correction!.FolderPath)!).Length.ShouldBe(1);
    }

    // -------------------------------------------------------------------------------------
    // Preparation failures, resumption and unknown outcomes (design §6.2)
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task A_different_file_at_a_final_name_stops_preparation_untouched_and_a_retry_resumes_the_same_row()
    {
        using SessionServiceHarness h = new();
        SessionService service = Service(h);
        SessionView review = await AtBackgroundRemovalReviewAsync(h, service);
        Guid intent = Guid.NewGuid();
        SessionAggregate before = await LoadAsync(h, review.Id);
        string folder = Path.Combine(h.FileWorkspace.ResolveAbsoluteDirectory(before.Session.Workspace), "Correction", $"logo-{intent:N}"[..^24]);
        Directory.CreateDirectory(folder);
        string squatter = Path.Combine(folder, "logo - REFERENCE (do not edit).png");
        byte[] theirs = SyntheticImages.PngWithAlpha(6, 5, (_, _) => 9);
        File.WriteAllBytes(squatter, theirs);

        OperationResult<SessionView> refused = await RequestAsync(service, review, requestId: intent);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.MessageKey.ShouldBe("Session_CorrectionNameTaken");
        File.ReadAllBytes(squatter).ShouldBe(theirs, "a colleague's or anyone's file is never overwritten");
        SessionAggregate afterRefusal = await LoadAsync(h, review.Id);
        afterRefusal.Session.State.ShouldBe(SessionState.Active, "nothing was handed off");
        afterRefusal.CorrectionRequests.Single().Status.ShouldBe(CorrectionRequestStatus.Preparing);
        (await service.LoadAsync(review.Id, default)).Value.CanAskColleague.ShouldBeTrue();

        File.Move(squatter, squatter + ".moved-away");
        SessionView resumed = await MustAsync(RequestAsync(service, review, requestId: intent));

        resumed.State.ShouldBe(SessionState.HandedOff);
        SessionAggregate saved = await LoadAsync(h, review.Id);
        saved.CorrectionRequests.Single().Id.ShouldBe(intent);
        saved.CorrectionRequests.Single().Status.ShouldBe(CorrectionRequestStatus.Ready);
    }

    [Fact]
    public async Task A_lost_handoff_commit_leaves_the_job_reviewable_and_the_same_intent_resumes_without_duplicates()
    {
        using SessionServiceHarness h = new();
        SessionView review = await AtBackgroundRemovalReviewAsync(h, Service(h));
        Guid intent = Guid.NewGuid();
        ScriptedRepository crashing = new(h.Repository) { FailFromCommit = 2 };

        (await RequestAsync(Service(h, repository: crashing), review, requestId: intent)).IsFailure.ShouldBeTrue();

        SessionAggregate crashed = await LoadAsync(h, review.Id);
        crashed.Session.State.ShouldBe(SessionState.Active);
        crashed.CorrectionRequests.Single().Status.ShouldBe(CorrectionRequestStatus.Preparing);
        crashed.ToSnapshot().CurrentStep!.CurrentRevisionId.ShouldBe(review.CurrentArtefact!.RevisionId);

        SessionView resumed = await MustAsync(RequestAsync(Service(h), review, requestId: intent));
        resumed.State.ShouldBe(SessionState.HandedOff);
        (await LoadAsync(h, review.Id)).CorrectionRequests.ShouldHaveSingleItem().Status.ShouldBe(CorrectionRequestStatus.Ready);
        Directory.GetFiles(resumed.Correction!.FolderPath, "*.png").Length.ShouldBe(2);
    }

    [Fact]
    public async Task An_unknown_handoff_acknowledgement_is_settled_by_the_database_and_a_blind_repeat_returns_it()
    {
        using SessionServiceHarness h = new();
        SessionView review = await AtBackgroundRemovalReviewAsync(h, Service(h));
        Guid intent = Guid.NewGuid();
        ScriptedRepository lying = new(h.Repository) { LieAboutCommit = 2 };

        (await RequestAsync(Service(h, repository: lying), review, requestId: intent)).IsFailure.ShouldBeTrue();
        (await Service(h).LoadAsync(review.Id, default)).Value.Correction!.RequestId.ShouldBe(intent);

        SessionView repeat = await MustAsync(RequestAsync(Service(h), review, requestId: intent));
        repeat.State.ShouldBe(SessionState.HandedOff);
        (await LoadAsync(h, review.Id)).CorrectionRequests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_mutated_source_refuses_the_request_through_the_existing_integrity_invalidation()
    {
        using SessionServiceHarness h = new();
        SessionService service = Service(h);
        SessionView review = await AtBackgroundRemovalReviewAsync(h, service);
        SessionAggregate before = await LoadAsync(h, review.Id);
        Revision r = before.Revisions.Single(x => x.Id == review.CurrentArtefact!.RevisionId);
        string rPath = h.FileWorkspace.ResolveAbsolute(r.File);
        File.SetAttributes(rPath, FileAttributes.Normal);
        File.WriteAllBytes(rPath, SyntheticImages.PngWithAlpha(6, 5, (_, _) => 33));

        OperationResult<SessionView> refused = await RequestAsync(service, review);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.RevisionIntegrityMismatch);
        SessionAggregate saved = await LoadAsync(h, review.Id);
        saved.CorrectionRequests.ShouldBeEmpty();
        saved.Session.State.ShouldBe(SessionState.Active);
        saved.Revisions.Single(x => x.Id == r.Id).InvalidationReason.ShouldBe(InvalidationReason.FileMutated);
    }

    [Fact]
    public async Task Preparing_files_again_recreates_only_what_is_missing_and_never_touches_the_colleagues_edit()
    {
        using SessionServiceHarness h = new();
        SessionService service = Service(h);
        SessionView handedOff = await MustAsync(RequestAsync(service, await AtBackgroundRemovalReviewAsync(h, service)));
        CorrectionHandoffView panel = handedOff.Correction!;
        string reference = Path.Combine(panel.FolderPath, panel.ReferenceFileName);
        string working = Path.Combine(panel.FolderPath, panel.WorkingFileName);
        string referenceHash = Hash(reference);
        File.SetAttributes(reference, FileAttributes.Normal);
        File.Delete(reference);
        byte[] edited = SyntheticImages.PngWithAlpha(6, 5, (x, _) => (byte)(x * 40));
        File.WriteAllBytes(working, edited);

        SessionView missing = (await service.LoadAsync(handedOff.Id, default)).Value;
        missing.Correction!.MissingFiles.ShouldBeTrue();
        SessionView repaired = await MustAsync(service.RepairCorrectionFilesAsync(handedOff.Id, panel.RequestId, CorrectionFileNaming.English, default));

        repaired.Correction!.MissingFiles.ShouldBeFalse();
        Hash(reference).ShouldBe(referenceHash);
        File.ReadAllBytes(working).ShouldBe(edited);
        repaired.State.ShouldBe(SessionState.HandedOff);
    }

    // -------------------------------------------------------------------------------------
    // AC6: explicit preflight refusals keep the handoff and write nothing
    // -------------------------------------------------------------------------------------

    [Theory]
    [InlineData("wrong-extension")]
    [InlineData("jpeg-as-png")]
    [InlineData("malformed")]
    [InlineData("opaque")]
    [InlineData("empty-alpha")]
    [InlineData("wrong-canvas")]
    [InlineData("locked")]
    [InlineData("missing")]
    [InlineData("reference-copy")]
    [InlineData("changed")]
    public async Task An_unusable_return_is_refused_with_a_plain_reason_before_any_attempt(string kind)
    {
        using SessionServiceHarness h = new();
        GatedImporter importer = new(h) { ReportChangedHash = kind == "changed" };
        importer.Release(succeed: true);
        SessionService service = Service(h, importer: importer);
        SessionView handedOff = await MustAsync(RequestAsync(service, await AtBackgroundRemovalReviewAsync(h, service, opaqueSource: true)));
        CorrectionHandoffView panel = handedOff.Correction!;
        string path = kind switch
        {
            "wrong-extension" => h.Workspace.CreateSourceFile("corrected.jpg", SyntheticImages.Jpeg(6, 5)),
            "jpeg-as-png" => h.Workspace.CreateSourceFile("corrected.png", SyntheticImages.Jpeg(6, 5)),
            "malformed" => h.Workspace.CreateSourceFile("corrected.png", [137, 80, 78, 71, 13, 10, 26, 10]),
            "opaque" => h.Workspace.CreateSourceFile("corrected.png", SyntheticImages.PngWithAlpha(6, 5, (_, _) => 255)),
            "empty-alpha" => h.Workspace.CreateSourceFile("corrected.png", SyntheticImages.PngWithAlpha(6, 5, (_, _) => 0)),
            "wrong-canvas" => h.Workspace.CreateSourceFile("corrected.png", SyntheticImages.PngWithAlpha(7, 5, (x, _) => x == 0 ? (byte)0 : (byte)255)),
            "reference-copy" => Path.Combine(panel.FolderPath, panel.ReferenceFileName),
            _ => CorrectedPng(h),
        };
        if (kind == "missing") File.Delete(path);
        using FileStream? locked = kind == "locked" ? new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None) : null;
        SessionAggregate before = await LoadAsync(h, handedOff.Id);

        OperationResult<SessionView> refused = await service.ImportCorrectedImageAsync(
            handedOff.Id, panel.RequestId, panel.HandedOutRevisionId, panel.HandedOutSha256, path, "qa", default);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.MessageKey.ShouldBe(kind switch
        {
            "opaque" or "empty-alpha" => "Failure_ManualResultTransparency",
            "wrong-canvas" => "Failure_ManualResultCanvas",
            "reference-copy" => "Session_CorrectionIsReference",
            "changed" => "Session_CorrectionChanged",
            _ => "Failure_ManualResultInvalid",
        });
        SessionAggregate after = await LoadAsync(h, handedOff.Id);
        after.Attempts.Count.ShouldBe(before.Attempts.Count);
        after.Revisions.Count.ShouldBe(before.Revisions.Count);
        after.Session.State.ShouldBe(SessionState.HandedOff);
        after.ToSnapshot().CurrentStep!.CurrentRevisionId.ShouldBe(panel.HandedOutRevisionId);
        after.CorrectionRequests.ShouldBe(before.CorrectionRequests);
    }

    // -------------------------------------------------------------------------------------
    // AC5: a successful return is a new Revision under its own review; next step named
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task A_corrected_picture_becomes_R2_under_review_with_Trim_next_and_no_upstream_processing()
    {
        using SessionServiceHarness h = new();
        CountingMeitu meitu = new(h.FakeMeitu);
        SessionService service = Service(h, meitu: meitu);
        SessionView handedOff = await MustAsync(RequestAsync(service, await AtBackgroundRemovalReviewAsync(h, service)));
        CorrectionHandoffView panel = handedOff.Correction!;
        SessionAggregate before = await LoadAsync(h, handedOff.Id);
        int meituBefore = meitu.Calls;
        string selected = CorrectedPng(h);
        byte[] colleague = File.ReadAllBytes(selected);

        SessionView review = await MustAsync(service.ImportCorrectedImageAsync(
            handedOff.Id, panel.RequestId, panel.HandedOutRevisionId, panel.HandedOutSha256, selected, "qa", default));

        SessionAggregate after = await LoadAsync(h, handedOff.Id);
        Revision r2 = after.Revisions.Single(x => x.Id == review.CurrentArtefact!.RevisionId);
        CorrectionRequest row = after.CorrectionRequests.Single();
        r2.Id.ShouldNotBe(panel.HandedOutRevisionId);
        r2.Operation.ShouldBe(OperationKind.ManualResultImport);
        r2.SourceRevisionId.ShouldBe(row.ReferenceRevisionId, "R2 hangs off U, the input background removal consumed");
        r2.ReviewState.ShouldBe(ReviewState.NotReviewed);
        ProcessingAttempt attempt = after.Attempts.Except(before.Attempts).ShouldHaveSingleItem();
        (attempt.Operation, attempt.Status, attempt.InputRevisionId, attempt.OutputRevisionId)
            .ShouldBe((OperationKind.ManualResultImport, AttemptStatus.Succeeded, row.ReferenceRevisionId, r2.Id));
        row.Status.ShouldBe(CorrectionRequestStatus.Returned);
        row.ResultRevisionId.ShouldBe(r2.Id);
        row.LastImportAttemptId.ShouldBe(attempt.Id);
        meitu.Calls.ShouldBe(meituBefore, "no processor is asked to redo background removal");
        File.ReadAllBytes(selected).ShouldBe(colleague);

        review.State.ShouldBe(SessionState.Active);
        review.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        review.CorrectionReturn!.NextStep.ShouldBe(StepKind.Trim);
        review.CorrectionReturn.IdenticalToSent.ShouldBeFalse();
        Revision r = after.Revisions.Single(x => x.Id == panel.HandedOutRevisionId);
        (r.IsValid, r.ReviewState).ShouldBe((true, ReviewState.NotReviewed), "R stays valid, unjudged history");

        (await service.ImportCorrectedImageAsync(handedOff.Id, panel.RequestId, panel.HandedOutRevisionId, panel.HandedOutSha256,
            CorrectedPng(h, "again.png"), "qa", default)).Failure.MessageKey.ShouldBe("Session_CorrectionAlreadyImported");

        SessionView approved = await MustAsync(service.ApproveExactReviewAsync(
            handedOff.Id, StepKind.BackgroundRemoval, r2.Id, r2.Sha256, "qa", default));
        approved.CurrentStep!.Step.ShouldBe(StepKind.Trim);
        approved.CurrentStep.State.ShouldBe(StepState.Waiting, "nothing starts automatically");
        (await LoadAsync(h, handedOff.Id)).Attempts.Count(a => a.Step == StepKind.BackgroundRemoval)
            .ShouldBe(after.Attempts.Count(a => a.Step == StepKind.BackgroundRemoval));
        meitu.Calls.ShouldBe(meituBefore);
    }

    [Fact]
    public async Task A_return_identical_to_R_is_a_distinct_R2_and_a_stale_exact_decision_about_R_is_refused()
    {
        using SessionServiceHarness h = new();
        SessionService service = Service(h);
        SessionView handedOff = await MustAsync(RequestAsync(service, await AtBackgroundRemovalReviewAsync(h, service, opaqueSource: true)));
        handedOff.Correction!.HandedOutSha256.ShouldNotBe(handedOff.UpstreamArtefact?.Sha256 ?? default);
        CorrectionHandoffView panel = handedOff.Correction!;
        string unchanged = h.Workspace.CreateSourceFile("unchanged.png",
            File.ReadAllBytes(Path.Combine(panel.FolderPath, panel.WorkingFileName)));

        SessionView review = await MustAsync(service.ImportCorrectedImageAsync(
            handedOff.Id, panel.RequestId, panel.HandedOutRevisionId, panel.HandedOutSha256, unchanged, "qa", default));

        review.CurrentArtefact!.Sha256.ShouldBe(panel.HandedOutSha256);
        review.CurrentArtefact.RevisionId.ShouldNotBe(panel.HandedOutRevisionId);
        review.CorrectionReturn!.IdenticalToSent.ShouldBeTrue();
        (await service.ApproveExactReviewAsync(review.Id, StepKind.BackgroundRemoval, panel.HandedOutRevisionId,
            panel.HandedOutSha256, "qa", default)).IsFailure.ShouldBeTrue();
        (await service.RejectExactReviewAsync(review.Id, StepKind.BackgroundRemoval, panel.HandedOutRevisionId,
            panel.HandedOutSha256, RejectionReason.Other, null, "qa", default)).IsFailure.ShouldBeTrue();
        SessionAggregate saved = await LoadAsync(h, review.Id);
        saved.Reviews.ShouldNotContain(x => x.SubjectId == panel.HandedOutRevisionId.Value || x.SubjectId == review.CurrentArtefact.RevisionId.Value);
        await MustAsync(service.RejectExactReviewAsync(review.Id, StepKind.BackgroundRemoval, review.CurrentArtefact.RevisionId,
            review.CurrentArtefact.Sha256, RejectionReason.EdgeError, "still wrong", "qa", default));
        (await LoadAsync(h, review.Id)).ToSnapshot().CurrentStep!.State.ShouldBe(StepState.RetryRequired);
    }

    // -------------------------------------------------------------------------------------
    // D5: bound unfinished closes re-hand off with the request's reason; no lock change
    // -------------------------------------------------------------------------------------

    public static TheoryData<string, string> UnfinishedCases => new()
    {
        { "fail", "none" }, { "fail", "session" }, { "fail", "verification" },
        { "stop", "none" }, { "stop", "session" }, { "stop", "verification" },
        { "takeover", "none" }, { "takeover", "session" }, { "takeover", "verification" },
    };

    /// <summary>T3/T6 (live): Stop takes effect only because the gated import then fails.</summary>
    [Theory]
    [MemberData(nameof(UnfinishedCases))]
    public async Task A_bound_import_that_fails_or_is_stopped_then_fails_stays_handed_off_without_touching_the_lock(
        string outcome, string lockHolder)
    {
        using SessionServiceHarness h = new();
        GatedImporter importer = new(h);
        CountingMeitu meitu = new(h.FakeMeitu);
        SessionService service = Service(h, importer: importer, meitu: meitu);
        SessionView handedOff = await MustAsync(RequestAsync(service, await AtBackgroundRemovalReviewAsync(h, service), note: "Fix the edges"));
        CorrectionHandoffView panel = handedOff.Correction!;
        AutomationLockState held = lockHolder switch
        {
            "session" => await HoldLockForOtherSessionAsync(h, service),
            "verification" => await HoldLockForVerificationAsync(h),
            _ => (await h.Repository.GetAutomationLockAsync(default)).Value,
        };
        int meituBefore = meitu.Calls;

        Task<OperationResult<SessionView>> import = service.ImportCorrectedImageAsync(
            handedOff.Id, panel.RequestId, panel.HandedOutRevisionId, panel.HandedOutSha256, CorrectedPng(h), "qa", default);
        await importer.Started;
        if (outcome != "fail")
            service.RequestStop(handedOff.Id, outcome == "stop" ? AutomationStopMode.StopOperation : AutomationStopMode.TakeOver).IsSuccess.ShouldBeTrue();
        importer.Release(succeed: false);
        (await import).IsFailure.ShouldBeTrue();

        SessionAggregate saved = await LoadAsync(h, handedOff.Id);
        ProcessingAttempt attempt = saved.Attempts.Last(a => a.Operation == OperationKind.ManualResultImport);
        attempt.Status.ShouldBe(outcome == "fail" ? AttemptStatus.Failed : AttemptStatus.Cancelled);
        saved.ToSnapshot().CurrentStep!.State.ShouldBe(outcome == "fail" ? StepState.Failed : StepState.Interrupted);
        saved.Session.State.ShouldBe(SessionState.HandedOff);
        saved.Session.HandOffReason.ShouldBe("Fix the edges");
        CorrectionRequest row = saved.CorrectionRequests.Single();
        (row.Status, row.LastImportAttemptId).ShouldBe((CorrectionRequestStatus.Ready, attempt.Id));
        (await h.Repository.GetAutomationLockAsync(default)).Value.ShouldBe(held, "no lock row is released or changed");
        meitu.Calls.ShouldBe(meituBefore);

        SessionView view = (await service.LoadAsync(handedOff.Id, default)).Value;
        view.Correction!.Mode.ShouldBe(CorrectionImportMode.AfterUnfinishedImport);
        view.CanImportCorrectedImage.ShouldBeTrue();
        view.CanSubmitManualResult.ShouldBeFalse();
        (await service.ExecuteAsync(handedOff.Id, new WorkflowCommand.StartStep(StepKind.BackgroundRemoval), "qa", default)).IsFailure.ShouldBeTrue();
        (await service.ExecuteAsync(handedOff.Id, new WorkflowCommand.Retry(StepKind.BackgroundRemoval), "qa", default)).IsFailure.ShouldBeTrue();
        OperationResult<SessionView> generic = await service.ExecuteAsync(handedOff.Id,
            new WorkflowCommand.SubmitManualResult(StepKind.BackgroundRemoval, CorrectedPng(h, "generic.png")), "qa", default);
        generic.Failure.MessageKey.ShouldBe("Session_CorrectionUseImport");
    }

    [Theory]
    [InlineData(AutomationStopMode.StopOperation)]
    [InlineData(AutomationStopMode.TakeOver)]
    public async Task A_bound_import_that_succeeds_with_a_stop_pending_is_not_handed_off_again(AutomationStopMode mode)
    {
        using SessionServiceHarness h = new();
        GatedImporter importer = new(h);
        SessionService service = Service(h, importer: importer);
        SessionView handedOff = await MustAsync(RequestAsync(service, await AtBackgroundRemovalReviewAsync(h, service)));
        CorrectionHandoffView panel = handedOff.Correction!;
        AutomationLockState held = await HoldLockForOtherSessionAsync(h, service);

        Task<OperationResult<SessionView>> import = service.ImportCorrectedImageAsync(
            handedOff.Id, panel.RequestId, panel.HandedOutRevisionId, panel.HandedOutSha256, CorrectedPng(h), "qa", default);
        await importer.Started;
        service.RequestStop(handedOff.Id, mode).IsSuccess.ShouldBeTrue();
        importer.Release(succeed: true);
        SessionView review = await MustAsync(import);

        review.State.ShouldBe(SessionState.Active);
        review.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        SessionAggregate saved = await LoadAsync(h, handedOff.Id);
        saved.Session.State.ShouldBe(SessionState.Active);
        saved.CorrectionRequests.Single().Status.ShouldBe(CorrectionRequestStatus.Returned);
        (await h.Repository.GetAutomationLockAsync(default)).Value.ShouldBe(held);
    }

    /// <summary>T9: the live closing seams fail closed when post-opening memory loses its binding.</summary>
    [Theory]
    [InlineData("fail")]
    [InlineData("takeover-fail")]
    [InlineData("takeover-success")]
    public async Task A_lost_in_memory_binding_uses_neutral_closure_without_mutating_request_or_foreign_lock(string outcome)
    {
        using SessionServiceHarness h = new();
        SessionService setup = Service(h);
        SessionView handedOff = await MustAsync(RequestAsync(setup, await AtBackgroundRemovalReviewAsync(h, setup)));
        CorrectionHandoffView panel = handedOff.Correction!;
        AutomationLockState held = await HoldLockForOtherSessionAsync(h, setup);
        GatedImporter importer = new(h);
        ScriptedRepository repository = new(h.Repository) { LoseInMemoryOpeningBinding = true };
        SessionService service = Service(h, repository: repository, importer: importer);
        Task<OperationResult<SessionView>> import = service.ImportCorrectedImageAsync(
            handedOff.Id, panel.RequestId, panel.HandedOutRevisionId, panel.HandedOutSha256, CorrectedPng(h), "qa", default);
        await importer.Started;
        if (outcome != "fail")
            service.RequestStop(handedOff.Id, AutomationStopMode.TakeOver).IsSuccess.ShouldBeTrue();
        bool success = outcome == "takeover-success";
        importer.Release(success);
        (await import).IsSuccess.ShouldBe(success);

        SessionAggregate saved = await LoadAsync(h, handedOff.Id);
        ProcessingAttempt attempt = saved.Attempts.Single(a => a.Operation == OperationKind.ManualResultImport);
        attempt.Status.ShouldBe(success ? AttemptStatus.Succeeded : outcome == "fail" ? AttemptStatus.Failed : AttemptStatus.Cancelled);
        saved.Session.State.ShouldBe(success ? SessionState.Active : SessionState.HandedOff);
        saved.ToSnapshot().CurrentStep!.State.ShouldBe(success ? StepState.ReviewRequired : outcome == "fail" ? StepState.Failed : StepState.Interrupted);
        if (!success) saved.Session.HandOffReason.ShouldBe(CorrectionClosing.UnfinishedReason);
        else saved.ToSnapshot().CurrentStep!.CurrentRevisionId.ShouldNotBe(panel.HandedOutRevisionId);
        CorrectionRequest row = saved.CorrectionRequests.Single();
        row.Status.ShouldBe(CorrectionRequestStatus.Ready, "no RETURNED is fabricated for an invalid live binding");
        row.LastImportAttemptId.ShouldBe(attempt.Id, "the real opening commit did bind the persisted row");
        row.ResultRevisionId.ShouldBeNull();
        repository.Mutations.Count.ShouldBe(2, "one opening and one closing, no post-success handoff");
        repository.Mutations.Last().CorrectionRequestChanges.ShouldBeEmpty();
        repository.Mutations.Last().LockChange.ShouldBeNull();
        (await h.Repository.GetAutomationLockAsync(default)).Value.ShouldBe(held);
    }

    // -------------------------------------------------------------------------------------
    // C1 (T1, T2) and the crash route (T3, T4, T8 restart)
    // -------------------------------------------------------------------------------------

    /// <summary>A bound import whose closing commit is lost, then startup recovery over the same disk.</summary>
    private static async Task<(SessionView HandedOff, CorrectionHandoffView Panel, AttemptId Crashed)> CrashedCorrectionImportAsync(
        SessionServiceHarness h, Func<Task>? afterHandOff = null)
    {
        SessionView handedOff = await MustAsync(RequestAsync(Service(h), await AtBackgroundRemovalReviewAsync(h, Service(h))));
        CorrectionHandoffView panel = handedOff.Correction!;
        if (afterHandOff is not null) await afterHandOff();
        ScriptedRepository crashing = new(h.Repository) { FailFromCommit = 2 };
        (await Service(h, repository: crashing).ImportCorrectedImageAsync(handedOff.Id, panel.RequestId,
            panel.HandedOutRevisionId, panel.HandedOutSha256, CorrectedPng(h), "qa", default)).IsFailure.ShouldBeTrue();
        SessionAggregate lost = await LoadAsync(h, handedOff.Id);
        ProcessingAttempt running = lost.Attempts.Single(a => a.Status == AttemptStatus.Running);
        lost.CorrectionRequests.Single().LastImportAttemptId.ShouldBe(running.Id, "the opening commit bound the attempt");

        (await h.CreateRecoveryService(new FakeProcessLiveness(ProcessLiveness.Alive)).RecoverAsync(default)).IsSuccess.ShouldBeTrue();
        return (handedOff, panel, running.Id);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("session")]
    [InlineData("verification")]
    public async Task Startup_recovery_hands_a_crashed_bound_import_back_with_the_requests_reason_and_leaves_foreign_locks(string lockHolder)
    {
        using SessionServiceHarness h = new();
        AutomationLockState? held = null;
        (SessionView handedOff, _, AttemptId crashed) = await CrashedCorrectionImportAsync(h, async () => held = lockHolder switch
        {
            "session" => await HoldLockForOtherSessionAsync(h, Service(h)),
            "verification" => await HoldLockForVerificationAsync(h),
            _ => (await h.Repository.GetAutomationLockAsync(default)).Value,
        });

        SessionAggregate recovered = await LoadAsync(h, handedOff.Id);
        recovered.Attempts.Single(a => a.Id == crashed).Status.ShouldBe(AttemptStatus.Interrupted);
        recovered.Session.State.ShouldBe(SessionState.HandedOff);
        recovered.Session.HandOffReason.ShouldBe(WorkflowCommand.RequestColleagueCorrection.DefaultReason);
        recovered.Session.HandedOffAtUtc.ShouldNotBeNull();
        recovered.ToSnapshot().CurrentStep!.State.ShouldBe(StepState.Interrupted);
        recovered.CorrectionRequests.Single().Status.ShouldBe(CorrectionRequestStatus.Ready);
        (await h.Repository.GetAutomationLockAsync(default)).Value.ShouldBe(held);
    }

    /// <summary>T1 and T2: Home navigates and writes nothing; every generic import is refused.</summary>
    [Fact]
    public async Task Home_offers_only_navigation_for_a_correction_job_and_both_generic_imports_are_refused()
    {
        using SessionServiceHarness h = new();
        (SessionView handedOff, CorrectionHandoffView panel, _) = await CrashedCorrectionImportAsync(h);
        SessionService service = Service(h);

        RecoveryItem item = (await service.ListRecoveryAsync(default)).Value.Single();
        item.HasOpenCorrection.ShouldBeTrue();
        item.Actions.ShouldBe([RecoveryAction.Restart, RecoveryAction.Abandon]);

        string before = Fingerprint(await LoadAsync(h, handedOff.Id));
        AutomationLockState lockBefore = (await h.Repository.GetAutomationLockAsync(default)).Value;
        StubFilePicker picker = new(CorrectedPng(h));
        RecordingNavigation navigation = new();
        HomeViewModel home = new(service, h.Previews, navigation, picker, new StartupStatusAccessor());
        await home.RefreshCommand.ExecuteAsync(null);
        RecoverySessionRow row = home.RecoverySessions.Single();
        row.HasOpenCorrection.ShouldBeTrue();
        row.ShowsPlainOpen.ShouldBeFalse();
        row.CanImport.ShouldBeFalse();

        await home.OpenRecoveryCommand.ExecuteAsync(row);

        navigation.SessionFor!.Correction!.RequestId.ShouldBe(panel.RequestId);
        navigation.SessionFor.CanImportCorrectedImage.ShouldBeTrue();
        picker.CallCount.ShouldBe(0);
        Fingerprint(await LoadAsync(h, handedOff.Id)).ShouldBe(before, "navigation writes nothing");
        (await h.Repository.GetAutomationLockAsync(default)).Value.ShouldBe(lockBefore);

        (await service.ResolveRecoveryAsync(handedOff.Id, RecoveryAction.ManualResult, CorrectedPng(h, "home.png"), "qa", default))
            .Failure.TechnicalDetail.ShouldContain("no longer available");
        (await service.ExecuteAsync(handedOff.Id, new WorkflowCommand.SubmitManualResult(StepKind.BackgroundRemoval,
            CorrectedPng(h, "api.png")), "qa", default)).Failure.MessageKey.ShouldBe("Session_CorrectionUseImport");
        Fingerprint(await LoadAsync(h, handedOff.Id)).ShouldBe(before, "both refusals happen before any attempt");
    }

    public static TheoryData<string> SecondAttemptOutcomes => new() { "fail", "stop", "takeover", "crash", "success", "stop-success", "takeover-success" };

    /// <summary>T3, T4 and T8 (restart): the second, post-crash import binds A2, never A1.</summary>
    [Theory]
    [MemberData(nameof(SecondAttemptOutcomes))]
    public async Task After_a_crash_the_dedicated_import_binds_the_new_attempt_for_every_outcome(string outcome)
    {
        using SessionServiceHarness h = new();
        (SessionView handedOff, CorrectionHandoffView panel, AttemptId first) = await CrashedCorrectionImportAsync(h);
        GatedImporter importer = new(h);
        CountingMeitu meitu = new(h.FakeMeitu);
        ScriptedRepository repository = new(h.Repository) { FailFromCommit = outcome == "crash" ? 2 : null };
        SessionService service = Service(h, repository: repository, importer: importer, meitu: meitu);
        SessionView unfinished = (await service.LoadAsync(handedOff.Id, default)).Value;
        unfinished.Correction!.Mode.ShouldBe(CorrectionImportMode.AfterUnfinishedImport);

        Task<OperationResult<SessionView>> import = service.ImportCorrectedImageAsync(
            handedOff.Id, panel.RequestId, null, null, CorrectedPng(h), "qa", default);
        await importer.Started;
        if (outcome.StartsWith("stop", StringComparison.Ordinal))
            service.RequestStop(handedOff.Id, AutomationStopMode.StopOperation).IsSuccess.ShouldBeTrue();
        if (outcome.StartsWith("takeover", StringComparison.Ordinal))
            service.RequestStop(handedOff.Id, AutomationStopMode.TakeOver).IsSuccess.ShouldBeTrue();
        importer.Release(succeed: outcome.EndsWith("success", StringComparison.Ordinal) || outcome == "crash");
        OperationResult<SessionView> result = await import;
        if (outcome == "crash")
            (await h.CreateRecoveryService(new FakeProcessLiveness(ProcessLiveness.Alive)).RecoverAsync(default)).IsSuccess.ShouldBeTrue();

        SessionAggregate saved = await LoadAsync(h, handedOff.Id);
        ProcessingAttempt second = saved.Attempts.Where(a => a.Operation == OperationKind.ManualResultImport)
            .OrderBy(a => a.StartedAtUtc).Last();
        second.Id.ShouldNotBe(first);
        CorrectionRequest row = saved.CorrectionRequests.Single();
        row.LastImportAttemptId.ShouldBe(second.Id);
        meitu.Calls.ShouldBe(0);
        if (outcome.EndsWith("success", StringComparison.Ordinal))
        {
            result.IsSuccess.ShouldBeTrue();
            row.Status.ShouldBe(CorrectionRequestStatus.Returned);
            Revision r2 = saved.Revisions.Single(x => x.Id == row.ResultRevisionId);
            r2.SourceRevisionId.ShouldBe(row.ReferenceRevisionId);
            saved.Revisions.Count(x => x.Operation == OperationKind.ManualResultImport).ShouldBe(1, "one request, one result");
            saved.Session.State.ShouldBe(SessionState.Active);
            result.Value.CorrectionReturn!.NextStep.ShouldBe(StepKind.Trim);
            return;
        }

        result.IsFailure.ShouldBeTrue();
        row.Status.ShouldBe(CorrectionRequestStatus.Ready);
        saved.Session.State.ShouldBe(SessionState.HandedOff);
        saved.Session.HandOffReason.ShouldBe(row.EffectiveReason);
        (await Service(h).LoadAsync(handedOff.Id, default)).Value.CanImportCorrectedImage.ShouldBeTrue();
        (await Service(h).ExecuteAsync(handedOff.Id, new WorkflowCommand.StartStep(StepKind.BackgroundRemoval), "qa", default)).IsFailure.ShouldBeTrue();
        (await Service(h).ExecuteAsync(handedOff.Id, new WorkflowCommand.Retry(StepKind.BackgroundRemoval), "qa", default)).IsFailure.ShouldBeTrue();
        if (outcome is "fail" or "stop" or "takeover")
        {
            // T8 (live): the closing transaction asserted the post-opening binding, A2, not A1.
            CorrectionRequestChange.AssertBound assertion = repository.Mutations.Last().CorrectionRequestChanges
                .OfType<CorrectionRequestChange.AssertBound>().ShouldHaveSingleItem();
            assertion.Attempt.ShouldBe(second.Id);
            assertion.Attempt.ShouldNotBe(first);
            repository.Mutations.Last().LockChange.ShouldBeNull();
        }
    }

    /// <summary>T5: an explicit Restart makes the request history; the old id then imports nothing.</summary>
    [Fact]
    public async Task An_explicit_restart_makes_the_request_history_and_no_generic_import_follows()
    {
        using SessionServiceHarness h = new();
        (SessionView handedOff, CorrectionHandoffView panel, _) = await CrashedCorrectionImportAsync(h);
        SessionService service = Service(h);
        RecordingNavigation navigation = new();
        HomeViewModel staleHome = new(service, h.Previews, navigation, new StubFilePicker(), new StartupStatusAccessor());
        await staleHome.RefreshCommand.ExecuteAsync(null);
        RecoverySessionRow staleCard = staleHome.RecoverySessions.Single();

        await MustAsync(service.ResolveRecoveryAsync(handedOff.Id, RecoveryAction.Restart, null, "qa", default));
        SessionAggregate restarted = await LoadAsync(h, handedOff.Id);

        OperationResult<SessionView> old = await service.ImportCorrectedImageAsync(
            handedOff.Id, panel.RequestId, null, null, CorrectedPng(h), "qa", default);
        old.Failure.MessageKey.ShouldBe("Session_CorrectionNotAvailable");
        SessionView reloaded = (await service.LoadAsync(handedOff.Id, default)).Value;
        reloaded.Correction!.IsObsolete.ShouldBeTrue();
        reloaded.CanImportCorrectedImage.ShouldBeFalse();
        Fingerprint(await LoadAsync(h, handedOff.Id)).ShouldBe(Fingerprint(restarted), "no import of any kind happened");

        await staleHome.OpenRecoveryCommand.ExecuteAsync(staleCard);
        navigation.SessionFor!.Correction!.IsObsolete.ShouldBeTrue("a card built earlier only navigates");
        Fingerprint(await LoadAsync(h, handedOff.Id)).ShouldBe(Fingerprint(restarted));
    }

    // -------------------------------------------------------------------------------------
    // T7: unbound and legacy behaviour is unchanged
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task A_legacy_handoff_that_looks_like_a_correction_keeps_the_generic_route_and_generic_stop()
    {
        using SessionServiceHarness h = new();
        SessionService service = Service(h);
        SessionId id = await ManualResultImportTests.AtStep(h, service, StepKind.BackgroundRemoval);

        // A background-removal run that was still Running when its process died (persisted, as in
        // RecoverySurfaceTests.Seed), then ordinary startup recovery.
        SessionAggregate atStep = await LoadAsync(h, id);
        CommandContext crash = new(h.Clock.GetUtcNow(), "synthetic crash", ReviewId.From(Guid.NewGuid()), AttemptId.From(Guid.NewGuid()));
        WorkflowTransition started = WorkflowEngine.Instance.Apply(atStep.ToSnapshot(), new WorkflowCommand.StartStep(StepKind.BackgroundRemoval), crash);
        started.IsAccepted.ShouldBeTrue();
        ProcessingAttempt dead = ProcessingAttempt.Start(crash.NewAttemptId, id, StepKind.BackgroundRemoval,
            atStep.ToSnapshot().UpstreamRevisionOf(StepKind.BackgroundRemoval), OperationKind.RemoveBackground,
            "synthetic-no-external-app", h.Clock.GetUtcNow());
        (await h.Repository.CommitAsync(new SessionMutation(atStep.Session, started.State.Steps, [], [], [dead], [], [], null, null), default))
            .IsSuccess.ShouldBeTrue();
        (await h.CreateRecoveryService(new FakeProcessLiveness()).RecoverAsync(default)).IsSuccess.ShouldBeTrue();
        SessionService fresh = Service(h);
        await MustAsync(fresh.ExecuteAsync(id, new WorkflowCommand.HandOff(StepKind.BackgroundRemoval,
            WorkflowCommand.RequestColleagueCorrection.DefaultReason), "qa", default));

        SessionView legacy = (await fresh.LoadAsync(id, default)).Value;
        legacy.Correction.ShouldBeNull();
        legacy.CanSubmitManualResult.ShouldBeTrue();
        RecoveryItem item = (await fresh.ListRecoveryAsync(default)).Value.Single(x => x.Id == id);
        item.HasOpenCorrection.ShouldBeFalse();
        item.Actions.ShouldContain(RecoveryAction.ManualResult);

        // A generic import of a file named like a correction return, stopped then failed: today's
        // generic Stop close — Active + Interrupted, with the lock release it always emitted.
        GatedImporter importer = new(h);
        ScriptedRepository repository = new(h.Repository);
        SessionService generic = Service(h, repository: repository, importer: importer);
        string named = h.Workspace.CreateSourceFile("logo - CORRECTED.png",
            SyntheticImages.PngWithAlpha(6, 5, (x, _) => x == 0 ? (byte)0 : (byte)255));
        Task<OperationResult<SessionView>> import = generic.ExecuteAsync(id,
            new WorkflowCommand.SubmitManualResult(StepKind.BackgroundRemoval, named), "qa", default);
        await importer.Started;
        generic.RequestStop(id, AutomationStopMode.StopOperation).IsSuccess.ShouldBeTrue();
        importer.Release(succeed: false);
        (await import).IsFailure.ShouldBeTrue();

        SessionAggregate saved = await LoadAsync(h, id);
        saved.Session.State.ShouldBe(SessionState.Active);
        saved.ToSnapshot().CurrentStep!.State.ShouldBe(StepState.Interrupted);
        repository.Mutations.Last().LockChange!.Action.ShouldBe(AutomationLockAction.Release);
        repository.Mutations.Last().CorrectionRequestChanges.ShouldBeEmpty();
        saved.CorrectionRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_generic_manual_import_failure_keeps_the_existing_reason_text()
    {
        using SessionServiceHarness h = new();
        GatedImporter importer = new(h);
        SessionService service = Service(h, importer: importer);
        SessionId id = await ManualResultImportTests.HandedOff(h, service, StepKind.BackgroundRemoval);

        Task<OperationResult<SessionView>> import = service.ExecuteAsync(id,
            new WorkflowCommand.SubmitManualResult(StepKind.BackgroundRemoval, CorrectedPng(h)), "qa", default);
        await importer.Started;
        importer.Release(succeed: false);
        (await import).IsFailure.ShouldBeTrue();

        (await LoadAsync(h, id)).Session.HandOffReason.ShouldBe("Manual result validation failed; manual processing remains authorised.");
    }

    // -------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------

    private static string Hash(string path) => Sha256.FromBytes(SHA256.HashData(File.ReadAllBytes(path))).Value;

    /// <summary>Every managed file of the session outside its correction folders, by hash.</summary>
    private static Dictionary<string, string> ManagedFileHashes(SessionServiceHarness h, SessionAggregate aggregate)
    {
        string root = h.FileWorkspace.ResolveAbsoluteDirectory(aggregate.Session.Workspace);
        return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "Correction" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToDictionary(file => Path.GetRelativePath(root, file), Hash);
    }

    /// <summary>A byte-for-byte fingerprint of every row the session owns.</summary>
    private static string Fingerprint(SessionAggregate aggregate) => JsonSerializer.Serialize(new
    {
        aggregate.Session, aggregate.Steps, aggregate.Revisions, aggregate.Attempts, aggregate.Reviews,
        aggregate.Outputs, aggregate.CorrectionRequests,
    });
}
