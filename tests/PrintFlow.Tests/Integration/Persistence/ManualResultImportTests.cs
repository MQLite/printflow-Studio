using System.IO;
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
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>SCRUM-11092 / SCRUM-11112: real SQLite, managed files, decoder and Session screen.</summary>
[Collection(SqliteCollection.Name)]
public sealed class ManualResultImportTests
{
    [Theory]
    [InlineData(StepKind.Enhancement)]
    [InlineData(StepKind.BackgroundRemoval)]
    public async Task Takeover_import_review_restart_and_downstream_preserve_truth(StepKind step)
    {
        using SessionServiceHarness h = new();
        var service = h.CreateService();
        var id = await AtStep(h, service, step);
        h.FakeMeitu.SetScenario(FakeAdapterScenario.WaitForStopAt(ExternalOperationPhase.Busy));
        var run = service.ExecuteAsync(id, new WorkflowCommand.StartStep(step), "qa", default);
        await h.FakeMeitu.HangStarted;
        service.RequestStop(id, AutomationStopMode.TakeOver).IsSuccess.ShouldBeTrue();
        (await run).IsFailure.ShouldBeTrue();
        var prior = await Load(h, id);
        var automation = prior.Attempts.Last();
        automation.Status.ShouldBe(AttemptStatus.Cancelled);
        var upstream = prior.ToSnapshot().UpstreamRevisionOf(step)!.Value;
        var source = prior.Revisions.Single(r => r.Id == upstream);
        byte[] original = File.ReadAllBytes(h.FileWorkspace.ResolveAbsolute(source.File));
        string selected = h.Workspace.CreateSourceFile("operator-result.png", SyntheticImages.PngWithAlpha(
            source.Facts.PixelWidth!.Value, source.Facts.PixelHeight!.Value, (x, _) => x == 0 ? (byte)0 : (byte)255));
        byte[] external = File.ReadAllBytes(selected);
        var restarted = h.CreateService();
        (await restarted.LoadAsync(id, default)).Value.CanSubmitManualResult.ShouldBeTrue();
        var review = await Must(restarted, id, new WorkflowCommand.SubmitManualResult(step, selected));
        review.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        review.CanSubmitManualResult.ShouldBeFalse();
        review.RequiresAutomationReentry.ShouldBeFalse();
        review.CurrentArtefact!.IsManualProcessingResult.ShouldBeTrue();
        review.UpstreamArtefact!.RevisionId.ShouldBe(upstream);
        var saved = await Load(h, id);
        var attempt = saved.Attempts.Single(a => a.Operation == OperationKind.ManualResultImport);
        var revision = saved.Revisions.Single(r => r.Id == attempt.OutputRevisionId);
        System.Text.Json.JsonSerializer.Serialize(saved.Attempts.Single(a => a.Id == automation.Id))
            .ShouldBe(System.Text.Json.JsonSerializer.Serialize(automation));
        attempt.RetryOfAttemptId.ShouldBe(automation.Id);
        attempt.RetrySequence.ShouldBe(automation.RetrySequence + 1);
        attempt.EndedAtUtc.ShouldNotBeNull();
        attempt.AdapterId.ShouldBe("manual-result-import-v1");
        attempt.AdapterNotes!.ShouldContain("operator qa");
        revision.SourceRevisionId.ShouldBe(upstream);
        revision.File.Area.ShouldBe(WorkspaceArea.Working);
        revision.File.RelativePath.ShouldStartWith(saved.Session.Workspace.RelativePath);
        revision.File.RelativePath.ShouldContain(attempt.Id.ToString());
        revision.Operation.ShouldBe(OperationKind.ManualResultImport);
        File.ReadAllBytes(selected).ShouldBe(external);
        File.ReadAllBytes(h.FileWorkspace.ResolveAbsolute(source.File)).ShouldBe(original);
        if (step == StepKind.BackgroundRemoval)
        {
            attempt.BackgroundRemovalAuthority!.Decision.ShouldBe(BackgroundRemovalDecision.ManualResultForReviewedContent);
            var screen = new SessionViewModel(restarted, h.Previews, h.TiffReviews, new RecordingNavigation());
            screen.Open(review);
            await screen.PreviewsLoaded;
            screen.BackgroundRemovalAttemptAudit.ShouldNotContain("Automatic Selection");
            screen.BackgroundRemovalAttemptAudit.ShouldNotBeEmpty();
        }

        // The external input can disappear; load is persistence-only and review uses the managed file.
        File.Delete(selected);
        var resumed = await h.CreateService().LoadAsync(id, default);
        resumed.Value.CurrentArtefact.ShouldBe(review.CurrentArtefact);
        resumed.Value.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        (await restarted.ExecuteAsync(id, new WorkflowCommand.ReenterAutomation(), "qa", default)).IsFailure.ShouldBeTrue();
        await Must(restarted, id, new WorkflowCommand.Approve(step, revision.Sha256));
        var approved = await Load(h, id);
        StepKind next = step == StepKind.Enhancement ? StepKind.BackgroundRemoval : StepKind.Trim;
        approved.ToSnapshot().UpstreamRevisionOf(next).ShouldBe(revision.Id);
        if (next == StepKind.BackgroundRemoval) await SessionServiceHarness.AuthoriseBackgroundRemovalAsync(restarted, id);
        h.FakeMeitu.SetScenario(FakeAdapterScenario.Succeed);
        await Must(restarted, id, new WorkflowCommand.StartStep(next));
        (await Load(h, id)).Attempts.Last().InputRevisionId.ShouldBe(revision.Id);
        await Must(restarted, id, new WorkflowCommand.ReturnToStep(step));
        var returned = await Load(h, id);
        returned.Revisions.Single(r => r.Id == revision.Id).Sha256.ShouldBe(revision.Sha256);
        File.ReadAllBytes(h.FileWorkspace.ResolveAbsolute(revision.File)).ShouldBe(external);
    }

    [Fact]
    public async Task Reject_handoff_resubmit_retains_both_files_attempts_and_decisions()
    {
        using SessionServiceHarness h = new();
        var service = h.CreateService();
        var id = await HandedOff(h, service);
        string selected = h.WriteBorderedSourcePng("result.png");
        var first = await Must(service, id, new WorkflowCommand.SubmitManualResult(StepKind.Enhancement, selected));
        await Must(service, id, new WorkflowCommand.Reject(StepKind.Enhancement, first.CurrentArtefact!.Sha256, RejectionReason.EdgeError));
        (await service.LoadAsync(id, default)).Value.CanSubmitManualResult.ShouldBeFalse();
        await Must(service, id, new WorkflowCommand.HandOff(StepKind.Enhancement, "Continue manual processing"));
        var second = await Must(service, id, new WorkflowCommand.SubmitManualResult(StepKind.Enhancement, selected));
        second.CurrentArtefact!.RevisionId.ShouldNotBe(first.CurrentArtefact.RevisionId);
        var saved = await Load(h, id);
        var manual = saved.Attempts.Where(a => a.Operation == OperationKind.ManualResultImport).ToArray();
        manual.Length.ShouldBe(2);
        manual[0].Id.ShouldNotBe(manual[1].Id);
        var revisions = saved.Revisions.Where(r => r.Operation == OperationKind.ManualResultImport).ToArray();
        revisions.Select(r => r.File).Distinct().Count().ShouldBe(2);
        foreach (var revision in revisions) File.Exists(h.FileWorkspace.ResolveAbsolute(revision.File)).ShouldBeTrue();
        saved.Reviews.ShouldContain(r => r.SubjectId == first.CurrentArtefact.RevisionId.Value && !r.IsApproved);
        saved.Steps.Single(s => s.Step == StepKind.Enhancement).CurrentRevisionId.ShouldBe(second.CurrentArtefact.RevisionId);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("wrong-format")]
    [InlineData("opaque")]
    [InlineData("empty-alpha")]
    [InlineData("wrong-canvas")]
    [InlineData("locked")]
    public async Task Invalid_cutout_records_failure_and_preserves_manual_state(string kind)
    {
        using SessionServiceHarness h = new();
        var service = h.CreateService();
        var id = await HandedOff(h, service, StepKind.BackgroundRemoval);
        string path = h.Workspace.CreateSourceFile("invalid.png", kind switch
        {
            "malformed" => [137, 80, 78, 71, 13, 10, 26, 10],
            "wrong-format" => SyntheticImages.Jpeg(6, 5),
            "opaque" => SyntheticImages.PngWithAlpha(6, 5, (_, _) => 255),
            "empty-alpha" => SyntheticImages.PngWithAlpha(6, 5, (_, _) => 0),
            "wrong-canvas" => SyntheticImages.PngWithAlpha(7, 5, (x, _) => x == 0 ? (byte)0 : (byte)255),
            _ => SyntheticImages.PngWithAlpha(6, 5, (x, _) => x == 0 ? (byte)0 : (byte)255),
        });
        if (kind == "missing") File.Delete(path);
        using var locked = kind == "locked" ? new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None) : null;
        var before = await Load(h, id);
        var result = await service.ExecuteAsync(id, new WorkflowCommand.SubmitManualResult(StepKind.BackgroundRemoval, path), "qa", default);
        result.IsFailure.ShouldBeTrue();
        var saved = await Load(h, id);
        saved.Revisions.Count.ShouldBe(before.Revisions.Count);
        saved.Attempts.Last().Status.ShouldBe(AttemptStatus.Failed);
        saved.Attempts.Last().Operation.ShouldBe(OperationKind.ManualResultImport);
        saved.Attempts.Last().OutputRevisionId.ShouldBeNull();
        saved.Attempts.Last().EndedAtUtc.ShouldNotBeNull();
        saved.Session.State.ShouldBe(SessionState.HandedOff);
        (await service.LoadAsync(id, default)).Value.CanSubmitManualResult.ShouldBeTrue();
    }

    [Fact]
    public async Task Picker_cancel_is_harmless_and_success_updates_the_existing_review_surface()
    {
        using SessionServiceHarness h = new();
        var service = h.CreateService();
        var id = await HandedOff(h, service);
        var picker = new StubFilePicker();
        var screen = new SessionViewModel(service, h.Previews, h.TiffReviews, new RecordingNavigation(), picker);
        screen.Open((await service.LoadAsync(id, default)).Value);
        await screen.PreviewsLoaded;
        var before = await Load(h, id);
        var files = Directory.GetFiles(h.Workspace.Root, "*", SearchOption.AllDirectories);
        await screen.SubmitManualResultCommand.ExecuteAsync(null);
        picker.CallCount.ShouldBe(1);
        (await Load(h, id)).Attempts.ShouldBe(before.Attempts);
        Directory.GetFiles(h.Workspace.Root, "*", SearchOption.AllDirectories).ShouldBe(files);
        screen.CanSubmitManualResult.ShouldBeTrue();
        picker.Path = h.WriteBorderedSourcePng("selected.png");
        await screen.SubmitManualResultCommand.ExecuteAsync(null);
        screen.IsReviewRequired.ShouldBeTrue();
        screen.IsManualProcessingResult.ShouldBeTrue();
        screen.CanSubmitManualResult.ShouldBeFalse();
        screen.PreviewPanes.Count.ShouldBe(2);
        screen.ArtefactFileName.ShouldBe("selected.png");
        await screen.SubmitManualResultCommand.ExecuteAsync(null);
        picker.CallCount.ShouldBe(2);
    }

    [Fact]
    public async Task Other_session_lock_is_untouched_and_identical_names_are_copied_across_session_boundary()
    {
        using SessionServiceHarness h = new();
        var service = h.CreateService();
        var a = await HandedOff(h, service);
        var b = await HandedOff(h, service);
        var holder = await Load(h, b);
        (await h.Repository.CommitAsync(SessionMutation.Empty(holder.Session) with
        {
            LockChange = new AutomationLockChange(AutomationLockAction.Acquire, b, h.Clock.GetUtcNow(), Environment.ProcessId, Environment.MachineName),
        }, default)).IsSuccess.ShouldBeTrue();
        string path = h.WriteBorderedSourcePng("same.png");
        await Must(service, a, new WorkflowCommand.SubmitManualResult(StepKind.Enhancement, path));
        (await h.Repository.GetAutomationLockAsync(default)).Value.SessionId.ShouldBe(b);
        var first = (await Load(h, a)).Revisions.Last();
        await Must(service, b, new WorkflowCommand.SubmitManualResult(StepKind.Enhancement, h.FileWorkspace.ResolveAbsolute(first.File)));
        var second = (await Load(h, b)).Revisions.Last();
        first.File.FileName.ShouldBe(second.File.FileName);
        first.File.ShouldNotBe(second.File);
        second.SessionId.ShouldBe(b);
        second.File.RelativePath.ShouldStartWith(holder.Session.Workspace.RelativePath);
        (await h.Repository.GetAutomationLockAsync(default)).Value.SessionId.ShouldBe(b);
    }

    [Fact]
    public async Task Interrupted_recovery_offers_restart_manual_import_and_abandonment()
    {
        using SessionServiceHarness h = new();
        var service = h.CreateService();
        var id = await AtStep(h, service, StepKind.Enhancement);
        h.FakeMeitu.SetScenario(FakeAdapterScenario.HangUntilCancelled);
        _ = service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Enhancement), "qa", default);
        await h.FakeMeitu.HangStarted;
        var recovery = await h.CreateRecoveryService(new FakeProcessLiveness()).RecoverAsync(default);
        recovery.Value.InterruptedAttemptCount.ShouldBe(1);
        var resumed = (await h.CreateService().LoadAsync(id, default)).Value;
        resumed.AvailableCommands.ShouldContain(CommandKind.Retry);
        resumed.AvailableCommands.ShouldContain(CommandKind.HandOff);
        resumed.AvailableCommands.ShouldContain(CommandKind.AbandonSession);
        resumed.CanSubmitManualResult.ShouldBeFalse();
        await Must(service, id, new WorkflowCommand.HandOff(StepKind.Enhancement, "Inspect saved manual result"));
        await Must(service, id, new WorkflowCommand.SubmitManualResult(StepKind.Enhancement, h.WriteBorderedSourcePng("saved.png")));
        (await Load(h, id)).Attempts.ShouldContain(a => a.Operation == OperationKind.Enhance && a.Status == AttemptStatus.Interrupted);
    }

    [Fact]
    public async Task Reenter_before_submission_runs_new_automation()
    {
        using SessionServiceHarness h = new();
        var service = h.CreateService();
        var id = await HandedOff(h, service);
        await Must(service, id, new WorkflowCommand.ReenterAutomation());
        h.FakeMeitu.SetScenario(FakeAdapterScenario.Succeed);
        var reviewed = await Must(service, id, new WorkflowCommand.StartStep(StepKind.Enhancement));
        reviewed.CurrentArtefact!.IsManualProcessingResult.ShouldBeFalse();
        reviewed.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
    }

    [Fact]
    public async Task A_handed_off_review_offer_must_be_rejected_before_manual_replacement()
    {
        using SessionServiceHarness h = new();
        var service = h.CreateService();
        var id = await AtStep(h, service, StepKind.Enhancement);
        var review = await Must(service, id, new WorkflowCommand.StartStep(StepKind.Enhancement));
        var handedOff = await Must(service, id, new WorkflowCommand.HandOff(StepKind.Enhancement, "Finish manually"));
        handedOff.CanSubmitManualResult.ShouldBeFalse();
        handedOff.AvailableCommands.ShouldContain(CommandKind.Reject);
        var rejected = await Must(service, id, new WorkflowCommand.Reject(StepKind.Enhancement,
            review.CurrentArtefact!.Sha256, RejectionReason.InsufficientResult));
        rejected.CanSubmitManualResult.ShouldBeTrue();
        await Must(service, id, new WorkflowCommand.SubmitManualResult(StepKind.Enhancement, h.WriteBorderedSourcePng("replacement.png")));
    }

    [Fact]
    public async Task Closing_persistence_failure_creates_no_revision_and_recovers_as_interrupted()
    {
        using SessionServiceHarness h = new();
        var id = await HandedOff(h, h.CreateService());
        var faulty = new FaultingRepository(h.Repository) { FailFromCommit = 2 };
        var service = h.CreateService(repository: faulty);
        var result = await service.ExecuteAsync(id, new WorkflowCommand.SubmitManualResult(
            StepKind.Enhancement, h.WriteBorderedSourcePng("result.png")), "qa", default);
        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PersistenceError);
        var saved = await Load(h, id);
        saved.Revisions.ShouldNotContain(r => r.Operation == OperationKind.ManualResultImport);
        saved.Attempts.Last().Status.ShouldBe(AttemptStatus.Running);
        var recovered = await h.CreateRecoveryService(new FakeProcessLiveness()).RecoverAsync(default);
        recovered.Value.InterruptedAttemptCount.ShouldBe(1);
        await Must(h.CreateService(), id, new WorkflowCommand.HandOff(StepKind.Enhancement, "Recover interrupted manual submission"));
        (await h.CreateService().LoadAsync(id, default)).Value.CanSubmitManualResult.ShouldBeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manual_attempt_terminal_time_is_observed_after_file_work(bool fail)
    {
        using SessionServiceHarness h = new();
        var id = await HandedOff(h, h.CreateService());
        var service = new SessionService(WorkflowEngine.Instance, h.Repository, h.FileWorkspace,
            h.RecycleBin, h.FileInspector, h.FakeMeitu, h.FakePhotoshop, h.Trim, h.ManualCrop,
            h.Preset, h.EnvironmentGate, SystemIdGenerator.Instance, h.Clock,
            manualResults: new TimedImporter(h, fail));
        var result = await service.ExecuteAsync(id, new WorkflowCommand.SubmitManualResult(
            StepKind.Enhancement, h.WriteBorderedSourcePng("timed.png")), "qa", default);
        result.IsFailure.ShouldBe(fail);
        var attempt = (await Load(h, id)).Attempts.Last();
        (attempt.EndedAtUtc!.Value - attempt.StartedAtUtc).ShouldBe(TimeSpan.FromSeconds(17));
    }

    private sealed class TimedImporter(SessionServiceHarness h, bool fail) : IManualResultImporter
    {
        public async Task<OperationResult<ManualResult>> ImportAsync(WorkspaceDirRef session, AttemptId attempt,
            StepKind step, FileFacts upstream, string selectedPath, CancellationToken cancellationToken)
        {
            var result = await new PrintFlow.Infrastructure.Imaging.WicManualResultImporter(h.FileWorkspace, h.FileInspector)
                .ImportAsync(session, attempt, step, upstream, selectedPath, cancellationToken);
            h.Clock.Advance(TimeSpan.FromSeconds(17));
            return fail ? OperationResult.Fail<ManualResult>(FailureCode.OutputValidationFailed, "Controlled failure after elapsed file work") : result;
        }
    }

    internal static async Task<SessionId> AtStep(SessionServiceHarness h, ISessionService service, StepKind step)
    {
        var imported = await service.ImportAsync(WorkflowType.PrepareAsset, h.WriteSourcePng(Guid.NewGuid() + ".png"), null, "qa", default);
        imported.IsSuccess.ShouldBeTrue();
        var id = imported.Value.Id;
        await Must(service, id, new WorkflowCommand.ConfirmOriginal());
        if (step == StepKind.BackgroundRemoval)
        {
            await Must(service, id, new WorkflowCommand.Skip(StepKind.Enhancement));
            await SessionServiceHarness.AuthoriseBackgroundRemovalAsync(service, id);
        }
        return id;
    }

    internal static async Task<SessionId> HandedOff(SessionServiceHarness h, ISessionService service, StepKind step = StepKind.Enhancement)
    {
        var id = await AtStep(h, service, step);
        h.FakeMeitu.SetScenario(FakeAdapterScenario.Timeout);
        (await service.ExecuteAsync(id, new WorkflowCommand.StartStep(step), "qa", default)).IsFailure.ShouldBeTrue();
        await Must(service, id, new WorkflowCommand.HandOff(step, "Operator will finish manually"));
        return id;
    }

    internal static async Task<SessionAggregate> Load(SessionServiceHarness h, SessionId id) =>
        (await h.Repository.LoadAsync(id, default)).Value!;

    internal static async Task<SessionView> Must(ISessionService service, SessionId id, WorkflowCommand command)
    {
        var result = await service.ExecuteAsync(id, command, "qa", default);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
        return result.Value;
    }
}
