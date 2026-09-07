using System.IO;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>SCRUM-11114: real SQLite + FileWorkspace + completion interpreter, synthetic bytes only.</summary>
[Collection(SqliteCollection.Name)]
public sealed class RetentionCleanupTests
{
    [Fact]
    public async Task Bounded_live_filesystem_proof_with_real_TIFF_and_SQLite_survives_restart()
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService(photoshop: new SyntheticProductionTiffProcessor(h.FileWorkspace));
        string source = h.WriteSourcePng();
        string sourceHash = Hash(source);
        SessionId id = await Import(h, service, WorkflowType.PrepareCustomerDesign, source);
        h.FakeMeitu.SetScenario(FakeAdapterScenario.Timeout);
        (await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Enhancement), "live-synthetic-proof", default))
            .IsFailure.ShouldBeTrue();
        SessionAggregate failed = await Load(h, id);
        ProcessingAttempt attempt = failed.Attempts.Single(a => a.Status == AttemptStatus.Failed);
        string evidence = Path.Combine(h.FileWorkspace.ResolveAbsoluteDirectory(failed.Session.Workspace),
            "Working", attempt.Id.ToString(), "failure-evidence.png");
        File.WriteAllBytes(evidence, SyntheticImages.Png());
        string evidenceHash = Hash(evidence);
        attempt = attempt with
        {
            Failure = attempt.Failure! with
            {
                Context = new Dictionary<string, string>(attempt.Failure.Context) { ["screenshotPath"] = evidence },
            },
        };
        (await h.Repository.CommitAsync(SessionMutation.Empty(failed.Session) with { UpsertAttempts = [attempt] }, default))
            .IsSuccess.ShouldBeTrue();
        h.FakeMeitu.SetScenario(FakeAdapterScenario.Succeed);
        await Command(service, id, new WorkflowCommand.Retry(StepKind.Enhancement));
        await RunAndApprove(service, id, StepKind.Enhancement);
        await RunAndApprove(service, id, StepKind.BackgroundRemoval);
        await RunAndApprove(service, id, StepKind.Trim);
        await ProduceTiff(service, id, 40);
        SessionAggregate before = await Load(h, id);
        var working = h.FileWorkspace.ListWorkingFiles(before.Session.Workspace).Value;
        SessionRetentionPlan plan = SessionRetentionPlan.Create(before with
        {
            Session = before.Session with { State = SessionState.Completed, CompletedAtUtc = h.Clock.GetUtcNow() },
        }, working).Value;
        string[] disposable = plan.Delete.Select(f => f.File.RelativePath)
            .Where(path => working.Any(w => w.File.RelativePath == path)).ToArray();
        disposable.ShouldNotBeEmpty();
        var identityBefore = before.Revisions.Select(r => new
        {
            Id = r.Id.ToString(), Sha256 = r.Sha256.Value, r.Facts.ByteLength,
            r.Facts.PixelWidth, r.Facts.PixelHeight, BeforePath = r.File.RelativePath,
        }).ToArray();
        SessionView completed = await Command(service, id, new WorkflowCommand.Complete());
        completed.CompletionCleanup!.IsComplete.ShouldBeTrue(completed.CompletionCleanup.Failure?.ToString());
        SessionAggregate after = await AssertAuthority(h, id);
        AssertIdentity(before, after);
        foreach (string path in disposable)
            File.Exists(h.FileWorkspace.ResolveAbsolute(WorkspaceFileRef.Create(path, WorkspaceArea.Working))).ShouldBeFalse();
        Hash(source).ShouldBe(sourceHash);
        Hash(evidence).ShouldBe(evidenceHash);
        await Restart(h);
        AssertIdentity(after, await AssertAuthority(h, id));
        (await h.Repository.GetAutomationLockAsync(default)).Value.IsHeld.ShouldBeFalse();
        h.RecycleBin.Recycled.ShouldBeEmpty();

        // The opt-in run leaves the original synthetic DB/files available to a separate,
        // read-only Python/SQLite verifier. Ordinary full-suite runs leave no proof workspace.
        string? manifest = Environment.GetEnvironmentVariable("PRINTFLOW_RETENTION_PROOF_MANIFEST");
        if (!string.IsNullOrWhiteSpace(manifest))
        {
            File.WriteAllText(manifest, System.Text.Json.JsonSerializer.Serialize(new
            {
                RunAtUtc = DateTimeOffset.UtcNow, SessionId = id.ToString(), WorkspaceRoot = h.Workspace.Root,
                Database = h.Database.Path, Source = source, SourceSha256 = sourceHash,
                Evidence = evidence, EvidenceSha256 = evidenceHash, IdentityBefore = identityBefore,
                RemovedWorkingFiles = disposable, Cleanup = completed.CompletionCleanup,
                RestartVerified = true, AutomationLockFree = true,
                Adapter = "SyntheticProductionTiffProcessor (real TIFF bytes; no Photoshop/Meitu automation)",
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            h.Workspace.RetainForInspection = true;
            h.Database.RetainForInspection = true;
        }
    }

    [Theory]
    [InlineData(WorkflowType.PrepareAsset)]
    [InlineData(WorkflowType.PrepareCustomerDesign)]
    [InlineData(WorkflowType.GeneratePrintTiff)]
    public async Task Completion_preserves_all_authority_and_identity_removes_classified_copies_and_restarts(WorkflowType workflow)
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        string source = h.WriteSourcePng();
        string sourceHash = Hash(source);
        SessionId id = await ReadyAsync(h, service, workflow, source);
        SessionAggregate before = await Load(h, id);
        var working = h.FileWorkspace.ListWorkingFiles(before.Session.Workspace).Value;
        SessionRetentionPlan plan = SessionRetentionPlan.Create(
            before with { Session = before.Session with { State = SessionState.Completed, CompletedAtUtc = h.Clock.GetUtcNow() } }, working).Value;
        plan.Promote.ShouldNotBeEmpty();
        plan.Delete.ShouldNotBeEmpty();

        SessionView completed = await Command(service, id, new WorkflowCommand.Complete());
        completed.State.ShouldBe(SessionState.Completed);
        completed.CompletionCleanup.ShouldNotBeNull();
        completed.CompletionCleanup.IsComplete.ShouldBeTrue(completed.CompletionCleanup.Failure?.ToString());
        completed.CompletionCleanup.PromotedCount.ShouldBe(plan.Promote.Count);
        completed.CompletionCleanup.DeletedCount.ShouldBeGreaterThan(0);
        SessionAggregate after = await AssertAuthority(h, id);
        AssertIdentity(before, after);
        Hash(source).ShouldBe(sourceHash);
        File.GetAttributes(source).HasFlag(FileAttributes.ReadOnly).ShouldBeFalse();
        File.GetAttributes(h.FileWorkspace.ResolveAbsolute(after.Revisions.Single(r => r.IsRoot).File))
            .HasFlag(FileAttributes.ReadOnly).ShouldBeTrue();
        foreach (Revision promoted in after.Revisions.Where(r => r.FormerWorkingFile is not null))
        {
            promoted.File.Area.ShouldBe(WorkspaceArea.Revisions);
            if (!IsTiff(promoted.FormerWorkingFile!.Value))
                File.Exists(h.FileWorkspace.ResolveAbsolute(promoted.FormerWorkingFile.Value)).ShouldBeFalse();
        }

        SessionCleanupResult twice = await new SessionRetentionService(h.Repository, h.FileWorkspace, h.Clock)
            .CleanupAsync(id, CancellationToken.None);
        twice.IsComplete.ShouldBeTrue(twice.Failure?.ToString());
        twice.PromotedCount.ShouldBe(0);
        twice.DeletedCount.ShouldBe(0);
        await Restart(h);
        AssertIdentity(after, await AssertAuthority(h, id));
        (await h.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.IsHeld.ShouldBeFalse();
        h.RecycleBin.Recycled.ShouldBeEmpty();
    }

    [Fact]
    public async Task Three_independent_approved_TIFFs_survive_completion_and_add_another_size_after_promotion()
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        SessionId id = await ReadyAsync(h, service, WorkflowType.PrepareCustomerDesign, h.WriteSourcePng());
        for (int i = 0; i < 3; i++)
        {
            if (i != 0)
            {
                service = h.CreateService();
                await Command(service, id, new WorkflowCommand.AddAnotherSize());
                await ProduceTiff(service, id, 120 + i * 20);
            }
            SessionView view = await Command(service, id, new WorkflowCommand.Complete());
            view.CompletionCleanup!.IsComplete.ShouldBeTrue(view.CompletionCleanup.Failure?.ToString());
        }
        SessionAggregate after = await AssertAuthority(h, id);
        after.Outputs.Count.ShouldBe(3);
        after.Outputs.All(o => o.File.Area == WorkspaceArea.Approved && o.ReviewState == ReviewState.Approved).ShouldBeTrue();
        after.Outputs.Select(o => o.File).Distinct().Count().ShouldBe(3);
        after.Outputs.Select(o => o.Dimensions.WidthMm).Distinct().Count().ShouldBe(3);
        await Restart(h);
        await AssertAuthority(h, id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rejected_Meitu_bytes_expire_only_at_completion_and_history_explicitly_records_release(bool interruptDeletion)
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        SessionId id = await Import(h, service, WorkflowType.PrepareAsset, h.WriteSourcePng());
        SessionView first = await Command(service, id, new WorkflowCommand.StartStep(StepKind.Enhancement));
        SessionStep step = first.Steps.Single(s => s.Step == StepKind.Enhancement);
        await Command(service, id, new WorkflowCommand.Reject(StepKind.Enhancement,
            step.CurrentRevisionSha256!.Value, RejectionReason.Other));
        Revision rejected = (await Load(h, id)).Revisions.Single(r => r.Id == step.CurrentRevisionId);
        File.Exists(h.FileWorkspace.ResolveAbsolute(rejected.File)).ShouldBeTrue();
        await Command(service, id, new WorkflowCommand.Retry(StepKind.Enhancement));
        await RunAndApprove(service, id, StepKind.Enhancement);
        await FinishAsset(service, id);
        File.Exists(h.FileWorkspace.ResolveAbsolute(rejected.File)).ShouldBeTrue();

        if (interruptDeletion)
            service = h.CreateService(workspace: new FaultingWorkspace(h.FileWorkspace) { RetentionCleanupFails = true });
        SessionView completed = await Command(service, id, new WorkflowCommand.Complete());
        completed.CompletionCleanup!.IsComplete.ShouldBe(!interruptDeletion, completed.CompletionCleanup.Failure?.ToString());
        if (interruptDeletion)
        {
            (await Load(h, id)).Revisions.Single(r => r.Id == rejected.Id).RetentionReleasedAtUtc.ShouldNotBeNull();
            File.Exists(h.FileWorkspace.ResolveAbsolute(rejected.File)).ShouldBeTrue();
            await Restart(h);
        }
        SessionAggregate after = await AssertAuthority(h, id);
        Revision expired = after.Revisions.Single(r => r.Id == rejected.Id);
        expired.RetentionReleasedAtUtc.ShouldNotBeNull();
        expired.IsValid.ShouldBeFalse();
        expired.ReviewState.ShouldBe(ReviewState.Rejected);
        expired.File.ShouldBe(rejected.File);
        expired.Sha256.ShouldBe(rejected.Sha256);
        File.Exists(h.FileWorkspace.ResolveAbsolute(expired.File)).ShouldBeFalse();
        using (SqliteConnection connection = h.Database.Factory.Open())
        using (SqliteCommand revive = connection.CreateCommand())
        {
            revive.CommandText = "UPDATE Revision SET IsValid = 1 WHERE Id = @id";
            revive.Parameters.AddWithValue("@id", expired.Id.ToString());
            Should.Throw<SqliteException>(() => revive.ExecuteNonQuery());
        }
        after.Reviews.ShouldContain(r => r.SubjectId == expired.Id.Value && !r.IsApproved && r.ReviewedSha256 == expired.Sha256);
        (await h.Previews.GetPreviewAsync(id, expired.Id, CancellationToken.None)).IsFailure.ShouldBeTrue();
        await Restart(h);
        (await Load(h, id)).Revisions.Single(r => r.Id == rejected.Id).RetentionReleasedAtUtc.ShouldBe(expired.RetentionReleasedAtUtc);
        h.RecycleBin.Recycled.ShouldBeEmpty();
    }

    [Fact]
    public async Task Failed_attempt_evidence_and_clean_retry_chain_survive_later_completion()
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        SessionId id = await Import(h, service, WorkflowType.PrepareAsset, h.WriteSourcePng());
        h.FakeMeitu.SetScenario(FakeAdapterScenario.Timeout);
        await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Enhancement), "retention-test", CancellationToken.None);
        SessionAggregate failed = await Load(h, id);
        ProcessingAttempt attempt = failed.Attempts.Single(a => a.Status == AttemptStatus.Failed);
        string evidence = Path.Combine(h.FileWorkspace.ResolveAbsoluteDirectory(failed.Session.Workspace),
            "Working", attempt.Id.ToString(), "failure-screenshot.png");
        File.WriteAllBytes(evidence, SyntheticImages.Png());
        string evidenceHash = Hash(evidence);
        var failedFiles = h.FileWorkspace.ListWorkingFiles(failed.Session.Workspace).Value
            .Where(f => f.AttemptFolderName == attempt.Id.ToString()).Select(f => f.File).ToArray();
        h.FakeMeitu.SetScenario(FakeAdapterScenario.Succeed);
        await Command(service, id, new WorkflowCommand.Retry(StepKind.Enhancement));
        await RunAndApprove(service, id, StepKind.Enhancement);
        await FinishAsset(service, id);
        SessionView view = await Command(service, id, new WorkflowCommand.Complete());
        view.CompletionCleanup!.IsComplete.ShouldBeTrue(view.CompletionCleanup.Failure?.ToString());
        SessionAggregate after = await AssertAuthority(h, id);
        after.Attempts.Single(a => a.Id == attempt.Id).ShouldBe(attempt);
        after.Attempts.ShouldContain(a => a.RetryOfAttemptId == attempt.Id && a.RetrySequence == 1);
        foreach (WorkspaceFileRef file in failedFiles) File.Exists(h.FileWorkspace.ResolveAbsolute(file)).ShouldBeTrue();
        Hash(evidence).ShouldBe(evidenceHash);
        await Restart(h);
        Hash(evidence).ShouldBe(evidenceHash);
    }

    [Fact]
    public async Task Unknown_file_is_preserved_and_reported()
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        SessionId id = await ReadyAsync(h, service, WorkflowType.PrepareAsset, h.WriteSourcePng());
        SessionAggregate before = await Load(h, id);
        string unknown = Path.Combine(h.FileWorkspace.ResolveAbsoluteDirectory(before.Session.Workspace), "Working", "operator-note.bin");
        File.WriteAllText(unknown, "unclassified synthetic evidence");
        SessionView view = await Command(service, id, new WorkflowCommand.Complete());
        view.CompletionCleanup!.IsComplete.ShouldBeTrue(view.CompletionCleanup.Failure?.ToString());
        view.CompletionCleanup.Warnings.ShouldContain(w => w.Contains("operator-note.bin", StringComparison.Ordinal));
        File.ReadAllText(unknown).ShouldBe("unclassified synthetic evidence");
        await AssertAuthority(h, id);
    }

    [Fact]
    public async Task Corrupt_authority_fails_closed_and_does_not_relabel_completed_workflow()
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        SessionId id = await ReadyAsync(h, service, WorkflowType.PrepareAsset, h.WriteSourcePng());
        SessionAggregate before = await Load(h, id);
        Revision intermediate = before.Revisions.First(r => r.File.Area == WorkspaceArea.Working);
        string path = h.FileWorkspace.ResolveAbsolute(intermediate.File);
        byte[] original = File.ReadAllBytes(path);
        File.WriteAllText(path, "corruption");
        var working = h.FileWorkspace.ListWorkingFiles(before.Session.Workspace).Value.Select(f => f.File).ToArray();
        SessionView view = await Command(service, id, new WorkflowCommand.Complete());
        view.State.ShouldBe(SessionState.Completed);
        view.CompletionCleanup!.IsComplete.ShouldBeFalse();
        view.CompletionCleanup.PromotedCount.ShouldBe(0);
        foreach (WorkspaceFileRef file in working) File.Exists(h.FileWorkspace.ResolveAbsolute(file)).ShouldBeTrue();
        (await Load(h, id)).Revisions.Select(r => r.File).ShouldBe(before.Revisions.Select(r => r.File));
        StartupRecoveryReport report = await Restart(h);
        report.Entries.ShouldContain(e => e.Action == StartupRecoveryAction.RecoveryFailed);
        // Restore only this test's synthetic corruption, then retry the existing cleanup debt.
        File.WriteAllBytes(path, original);
        await Restart(h);
        await AssertAuthority(h, id);
    }

    [Fact]
    public async Task Crash_after_verified_copies_before_metadata_commit_keeps_original_authority_and_resumes()
    {
        using SessionServiceHarness h = new();
        SessionId id = await ReadyAsync(h, h.CreateService(), WorkflowType.PrepareAsset, h.WriteSourcePng());
        SessionAggregate before = await Load(h, id);
        FaultingRepository fault = new(h.Repository) { FailFromCommit = 2 };
        SessionView completed = await Command(h.CreateService(repository: fault), id, new WorkflowCommand.Complete());
        completed.State.ShouldBe(SessionState.Completed);
        completed.CompletionCleanup!.IsComplete.ShouldBeFalse();
        SessionAggregate interrupted = await AssertAuthority(h, id);
        interrupted.Revisions.Select(r => r.File).ShouldBe(before.Revisions.Select(r => r.File));
        Directory.GetFiles(Path.Combine(h.FileWorkspace.ResolveAbsoluteDirectory(before.Session.Workspace), "Revisions"),
            "*", SearchOption.AllDirectories).ShouldNotBeEmpty();
        await Restart(h);
        SessionAggregate recovered = await AssertAuthority(h, id);
        recovered.Revisions.ShouldContain(r => r.FormerWorkingFile != null);
        AssertIdentity(before, recovered);
    }

    [Fact]
    public async Task Crash_after_metadata_before_deletion_resumes_from_former_working_locations()
    {
        using SessionServiceHarness h = new();
        SessionId id = await ReadyAsync(h, h.CreateService(), WorkflowType.PrepareAsset, h.WriteSourcePng());
        FaultingWorkspace fault = new(h.FileWorkspace) { RetentionCleanupFails = true };
        SessionView completed = await Command(h.CreateService(workspace: fault), id, new WorkflowCommand.Complete());
        completed.CompletionCleanup!.IsComplete.ShouldBeFalse();
        SessionAggregate interrupted = await AssertAuthority(h, id);
        Revision promoted = interrupted.Revisions.First(r => r.FormerWorkingFile != null);
        File.Exists(h.FileWorkspace.ResolveAbsolute(promoted.FormerWorkingFile!.Value)).ShouldBeTrue();
        await Restart(h);
        File.Exists(h.FileWorkspace.ResolveAbsolute(promoted.FormerWorkingFile.Value)).ShouldBeFalse();
        await AssertAuthority(h, id);
    }

    [Fact]
    public async Task All_location_switches_roll_back_together_if_one_expected_identity_changed()
    {
        using SessionServiceHarness h = new();
        SessionId id = await ReadyAsync(h, h.CreateService(), WorkflowType.PrepareAsset, h.WriteSourcePng());
        await Command(h.CreateService(workspace: new FaultingWorkspace(h.FileWorkspace) { RetentionListingFails = true }),
            id, new WorkflowCommand.Complete());
        SessionAggregate before = await Load(h, id);
        Revision[] working = before.Revisions.Where(r => r.File.Area == WorkspaceArea.Working).Take(2).ToArray();
        List<RevisionRetentionChange> changes = [];
        foreach (Revision revision in working)
        {
            WorkspaceFileRef destination = (await h.FileWorkspace.PromoteRevisionAsync(before.Session.Workspace,
                revision.Id, new(revision.File, revision.Sha256), default)).Value;
            changes.Add(new(revision.Id, revision.File, revision.Sha256, destination, null));
        }
        changes[1] = changes[1] with { ExpectedHash = Sha256.Parse(new string('0', 64)) };
        var result = await h.Repository.CommitAsync(SessionMutation.Empty(before.Session) with
        {
            IsRetentionMaintenance = true, RevisionRetentionChanges = changes,
        }, default);
        result.IsFailure.ShouldBeTrue();
        var after = await AssertAuthority(h, id);
        after.Revisions.Select(r => r.File).ShouldBe(before.Revisions.Select(r => r.File));
        await Restart(h);
        await AssertAuthority(h, id);
    }

    [Fact]
    public async Task Database_rejects_path_switch_without_provenance_and_immutable_fact_changes()
    {
        using SessionServiceHarness h = new();
        SessionId id = await ReadyAsync(h, h.CreateService(), WorkflowType.PrepareAsset, h.WriteSourcePng());
        await Command(h.CreateService(workspace: new FaultingWorkspace(h.FileWorkspace) { RetentionListingFails = true }),
            id, new WorkflowCommand.Complete());
        SessionAggregate before = await Load(h, id);
        Revision revision = before.Revisions.First(r => r.File.Area == WorkspaceArea.Working);
        using SqliteConnection connection = h.Database.Factory.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.Parameters.AddWithValue("@id", revision.Id.ToString());
        command.Parameters.AddWithValue("@destination", $"{before.Session.Workspace.RelativePath}/Revisions/{revision.Id}/{revision.File.FileName}");
        foreach (string assignment in new[]
        {
            "RelativePath = @destination", "PixelWidth = PixelWidth + 1", "Sha256 = '" + new string('a', 64) + "'",
            "FormerWorkingPath = RelativePath", "RetentionReleasedAtUtc = '2026-09-08T00:00:00Z'",
        })
        {
            command.CommandText = "UPDATE Revision SET " + assignment + " WHERE Id = @id";
            Should.Throw<SqliteException>(() => command.ExecuteNonQuery());
        }
        await AssertAuthority(h, id);
    }

    [Fact]
    public async Task Stale_completed_session_cannot_be_reapplied_over_AddAnotherSize()
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        SessionId id = await ReadyAsync(h, service, WorkflowType.GeneratePrintTiff, h.WriteSourcePng());
        await Command(service, id, new WorkflowCommand.Complete());
        SessionAggregate completed = await Load(h, id);
        await Command(service, id, new WorkflowCommand.AddAnotherSize());
        (await h.Repository.CommitAsync(SessionMutation.Empty(completed.Session) with { IsRetentionMaintenance = true }, default))
            .IsFailure.ShouldBeTrue();
        (await new SessionRetentionService(h.Repository, h.FileWorkspace, h.Clock).CleanupAsync(id, default)).IsComplete.ShouldBeFalse();
        (await AssertAuthority(h, id)).Session.State.ShouldBe(SessionState.Active);
    }

    [Fact]
    public async Task AddAnotherSize_waits_for_in_flight_retention_and_loads_the_committed_locations()
    {
        using SessionServiceHarness h = new();
        SessionId id = await ReadyAsync(h, h.CreateService(), WorkflowType.PrepareCustomerDesign, h.WriteSourcePng());
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FaultingWorkspace workspace = new(h.FileWorkspace)
        {
            BeforeRetentionPromotionAsync = () => { entered.TrySetResult(); return release.Task; },
        };
        Task<SessionView> completion = Command(h.CreateService(workspace: workspace), id, new WorkflowCommand.Complete());
        Task<SessionView>? reopen = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            reopen = Command(h.CreateService(), id, new WorkflowCommand.AddAnotherSize());
            reopen.IsCompleted.ShouldBeFalse();
            (await Load(h, id)).Session.State.ShouldBe(SessionState.Completed);
        }
        finally { release.TrySetResult(); }
        (await completion).CompletionCleanup!.IsComplete.ShouldBeTrue();
        (await reopen!).State.ShouldBe(SessionState.Active);
        await ProduceTiff(h.CreateService(), id, 140);
        (await AssertAuthority(h, id)).Outputs.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Crash_before_cleanup_is_found_at_startup_even_outside_Home_recent_limits()
    {
        using SessionServiceHarness h = new();
        SessionId id = await ReadyAsync(h, h.CreateService(), WorkflowType.PrepareAsset, h.WriteSourcePng());
        SessionAggregate before = await Load(h, id);
        FaultingWorkspace fault = new(h.FileWorkspace) { RetentionListingFails = true };
        SessionView completed = await Command(h.CreateService(workspace: fault), id, new WorkflowCommand.Complete());
        completed.State.ShouldBe(SessionState.Completed);
        completed.CompletionCleanup!.IsComplete.ShouldBeFalse();
        (await AssertAuthority(h, id)).Revisions.Select(r => r.File).ShouldBe(before.Revisions.Select(r => r.File));
        h.Clock.Advance(TimeSpan.FromDays(365));
        StartupRecoveryReport report = await Restart(h);
        report.Entries.ShouldContain(e => e.Action == StartupRecoveryAction.CompletedSessionCleanup && e.SessionId == id);
        (await AssertAuthority(h, id)).Revisions.ShouldContain(r => r.FormerWorkingFile != null);
    }

    [Fact]
    public async Task Rejected_TIFF_remains_explicitly_recycled_without_double_recycle_after_successful_retry()
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        SessionId id = await Import(h, service, WorkflowType.GeneratePrintTiff, h.WriteSourcePng());
        await Command(service, id, new WorkflowCommand.SetPrintDimensions(PrintDimensions.FromMillimetres(120, 80, SizePreset.Custom)));
        await Command(service, id, new WorkflowCommand.SelectWhiteUnderbaseBranch(WhiteUnderbaseBranch.W1_1px, "test"));
        SessionView review = await Command(service, id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput));
        await Command(service, id, new WorkflowCommand.Reject(StepKind.PhotoshopOutput,
            review.CurrentStep!.CurrentRevisionSha256!.Value, RejectionReason.WhiteInkIssue));
        PrintOutput rejected = (await Load(h, id)).Outputs.Single();
        rejected.RecycledAtUtc.ShouldNotBeNull();
        string recycled = h.RecycleBin.Recycled.Single();
        await Command(service, id, new WorkflowCommand.Retry(StepKind.PhotoshopOutput));
        await RunAndApprove(service, id, StepKind.PhotoshopOutput);
        SessionView completed = await Command(service, id, new WorkflowCommand.Complete());
        completed.CompletionCleanup!.IsComplete.ShouldBeTrue(completed.CompletionCleanup.Failure?.ToString());
        await Restart(h);
        SessionAggregate after = await AssertAuthority(h, id);
        after.Outputs.Single(o => o.Id == rejected.Id).ShouldBe(rejected);
        h.RecycleBin.Recycled.ShouldBe([recycled]);
        File.Exists(recycled).ShouldBeFalse();
    }

    [Fact]
    public async Task ReturnToStep_keeps_invalidated_revision_review_and_trim_geometry_history_after_completion()
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        SessionId id = await ReadyAsync(h, service, WorkflowType.PrepareAsset, h.WriteSourcePng());
        await Command(service, id, new WorkflowCommand.ReturnToStep(StepKind.BackgroundRemoval));
        SessionAggregate invalidated = await Load(h, id);
        invalidated.Revisions.ShouldContain(r => !r.IsValid);
        await FinishAsset(service, id);
        SessionAggregate before = await Load(h, id);
        SessionView completed = await Command(service, id, new WorkflowCommand.Complete());
        completed.CompletionCleanup!.IsComplete.ShouldBeTrue(completed.CompletionCleanup.Failure?.ToString());
        SessionAggregate after = await AssertAuthority(h, id);
        AssertIdentity(before, after);
        after.Attempts.Where(a => a.Operation == OperationKind.Trim).ShouldAllBe(a => a.TrimGeometry != null);
        foreach (Revision revision in invalidated.Revisions.Where(r => !r.IsValid))
            after.Revisions.Single(r => r.Id == revision.Id).IsValid.ShouldBeFalse();
        await Restart(h);
        await AssertAuthority(h, id);
    }

    [Fact]
    public async Task Manual_result_import_has_the_same_durable_retention_as_automated_revisions()
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        SessionId id = await ManualResultImportTests.HandedOff(h, service);
        SessionAggregate handedOff = await Load(h, id);
        Revision upstream = handedOff.Revisions.Single(r => r.IsRoot);
        string external = h.Workspace.CreateSourceFile("manual-result.png", SyntheticImages.Png(
            upstream.Facts.PixelWidth!.Value, upstream.Facts.PixelHeight!.Value));
        string externalHash = Hash(external);
        SessionView imported = await Command(service, id, new WorkflowCommand.SubmitManualResult(StepKind.Enhancement, external));
        RevisionId importedId = imported.CurrentStep!.CurrentRevisionId!.Value;
        await Command(service, id, new WorkflowCommand.Approve(StepKind.Enhancement, imported.CurrentStep.CurrentRevisionSha256!.Value));
        h.FakeMeitu.SetScenario(FakeAdapterScenario.Succeed);
        await FinishAsset(service, id);
        SessionAggregate before = await Load(h, id);
        SessionView complete = await Command(service, id, new WorkflowCommand.Complete());
        complete.CompletionCleanup!.IsComplete.ShouldBeTrue(complete.CompletionCleanup.Failure?.ToString());
        SessionAggregate after = await AssertAuthority(h, id);
        Revision retained = after.Revisions.Single(r => r.Id == importedId);
        retained.Operation.ShouldBe(OperationKind.ManualResultImport);
        retained.File.Area.ShouldBe(WorkspaceArea.Revisions);
        Hash(external).ShouldBe(externalHash);
        AssertIdentity(before, after);
        await Restart(h);
        await AssertAuthority(h, id);
    }

    [Theory]
    [InlineData(StepState.Processing)]
    [InlineData(StepState.ReviewRequired)]
    [InlineData(StepState.RetryRequired)]
    [InlineData(StepState.Interrupted)]
    public async Task Nonterminal_step_refuses_cleanup_even_if_session_claims_completed(StepState state)
    {
        using SessionServiceHarness h = new();
        SessionId id = await ReadyAsync(h, h.CreateService(), WorkflowType.PrepareAsset, h.WriteSourcePng());
        SessionAggregate before = await Load(h, id);
        var files = h.FileWorkspace.ListWorkingFiles(before.Session.Workspace).Value;
        SessionAggregate malformed = before with
        {
            Session = before.Session with { State = SessionState.Completed, CompletedAtUtc = h.Clock.GetUtcNow() },
            Steps = before.Steps.Select(s => s.Step == StepKind.Enhancement ? s with { State = state } : s).ToList(),
        };
        SessionRetentionPlan.Create(malformed, files).IsFailure.ShouldBeTrue();
        (await h.Repository.CommitAsync(SessionMutation.Empty(malformed.Session) with { UpsertSteps = malformed.Steps },
            CancellationToken.None)).IsSuccess.ShouldBeTrue();
        SessionCleanupResult result = await new SessionRetentionService(h.Repository, h.FileWorkspace, h.Clock)
            .CleanupAsync(id, CancellationToken.None);
        result.IsComplete.ShouldBeFalse();
        foreach (WorkingFileEntry entry in files) File.Exists(h.FileWorkspace.ResolveAbsolute(entry.File)).ShouldBeTrue();
    }

    [Theory]
    [InlineData(SessionState.Active)]
    [InlineData(SessionState.HandedOff)]
    [InlineData(SessionState.Abandoned)]
    public async Task Noncompleted_sessions_cannot_enter_retention(SessionState state)
    {
        using SessionServiceHarness h = new();
        SessionId id = await ReadyAsync(h, h.CreateService(), WorkflowType.PrepareAsset, h.WriteSourcePng());
        SessionAggregate before = await Load(h, id);
        var files = h.FileWorkspace.ListWorkingFiles(before.Session.Workspace).Value;
        SessionRetentionPlan.Create(before with { Session = before.Session with { State = state } }, files)
            .IsFailure.ShouldBeTrue();
        (await h.Repository.CommitAsync(SessionMutation.Empty(before.Session with
        {
            State = state,
            HandedOffAtUtc = state == SessionState.HandedOff ? h.Clock.GetUtcNow() : null,
            HandOffReason = state == SessionState.HandedOff ? "synthetic manual processing" : null,
            AbandonedAtUtc = state == SessionState.Abandoned ? h.Clock.GetUtcNow() : null,
            AbandonReason = state == SessionState.Abandoned ? "synthetic abandon" : null,
        }), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        SessionCleanupResult result = await new SessionRetentionService(h.Repository, h.FileWorkspace, h.Clock)
            .CleanupAsync(id, CancellationToken.None);
        result.IsComplete.ShouldBeFalse();
        foreach (WorkingFileEntry file in files) File.Exists(h.FileWorkspace.ResolveAbsolute(file.File)).ShouldBeTrue();
    }

    internal static async Task<SessionAggregate> AssertAuthority(SessionServiceHarness h, SessionId id)
    {
        // Rebuild the repository, and independently hash disk bytes rather than asking the
        // retention verifier to assert its own correctness.
        SessionAggregate a = (await new SqliteSessionRepository(h.Database.Factory).LoadAsync(id, CancellationToken.None)).Value!;
        foreach (Revision r in a.Revisions)
        {
            if (r.RetentionReleasedAtUtc != null || a.Outputs.Any(o => o.Id.Value == r.Id.Value && o.RecycledAtUtc != null)) continue;
            string absolute = h.FileWorkspace.ResolveAbsolute(r.File);
            File.Exists(absolute).ShouldBeTrue(r.File.RelativePath);
            Hash(absolute).ShouldBe(r.Sha256.Value.ToUpperInvariant());
        }
        foreach (PrintOutput o in a.Outputs.Where(o => o.RecycledAtUtc == null))
        {
            Hash(h.FileWorkspace.ResolveAbsolute(o.File)).ShouldBe(o.Sha256.Value.ToUpperInvariant());
            if (o.ReviewState == ReviewState.Approved)
                a.Reviews.ShouldContain(r => r.SubjectId == o.Id.Value && r.IsApproved && r.ReviewedSha256 == o.Sha256);
        }
        a.Snapshot.ShouldNotBeNull();
        a.Revisions.ShouldContain(r => r.Id == a.Snapshot.RootRevisionId && r.File.Area == WorkspaceArea.Source);
        return a;
    }

    private static void AssertIdentity(SessionAggregate before, SessionAggregate after)
    {
        after.Revisions.Select(r => (r.Id, r.Sha256, r.Facts, r.SourceRevisionId, r.Operation, r.CreatedAtUtc))
            .ShouldBe(before.Revisions.Select(r => (r.Id, r.Sha256, r.Facts, r.SourceRevisionId, r.Operation, r.CreatedAtUtc)));
        after.Reviews.ShouldBe(before.Reviews);
        System.Text.Json.JsonSerializer.Serialize(after.Attempts)
            .ShouldBe(System.Text.Json.JsonSerializer.Serialize(before.Attempts));
    }

    internal static async Task<SessionId> ReadyAsync(SessionServiceHarness h, ISessionService service,
        WorkflowType workflow, string source)
    {
        SessionId id = await Import(h, service, workflow, source);
        if (workflow != WorkflowType.GeneratePrintTiff)
        {
            await RunAndApprove(service, id, StepKind.Enhancement);
            await RunAndApprove(service, id, StepKind.BackgroundRemoval);
            await RunAndApprove(service, id, StepKind.Trim);
        }
        if (workflow == WorkflowType.PrepareAsset)
            await Command(service, id, new WorkflowCommand.StartStep(StepKind.ApprovedPngExport));
        else await ProduceTiff(service, id, 120);
        return id;
    }

    private static async Task<SessionId> Import(SessionServiceHarness h, ISessionService service, WorkflowType workflow, string source)
    {
        var imported = await service.ImportAsync(workflow, source, "retention", "retention-test", CancellationToken.None);
        imported.IsSuccess.ShouldBeTrue(imported.IsFailure ? imported.Failure.ToString() : "");
        await Command(service, imported.Value.Id, new WorkflowCommand.ConfirmOriginal());
        return imported.Value.Id;
    }

    private static async Task FinishAsset(ISessionService service, SessionId id)
    {
        await RunAndApprove(service, id, StepKind.BackgroundRemoval);
        await RunAndApprove(service, id, StepKind.Trim);
        await Command(service, id, new WorkflowCommand.StartStep(StepKind.ApprovedPngExport));
    }

    internal static async Task ProduceTiff(ISessionService service, SessionId id, double width)
    {
        await Command(service, id, new WorkflowCommand.SetPrintDimensions(PrintDimensions.FromMillimetres(width, 80, SizePreset.Custom)));
        await Command(service, id, new WorkflowCommand.SelectWhiteUnderbaseBranch(WhiteUnderbaseBranch.W1_1px, "synthetic retention proof"));
        await RunAndApprove(service, id, StepKind.PhotoshopOutput);
    }

    private static async Task RunAndApprove(ISessionService service, SessionId id, StepKind step)
    {
        if (step == StepKind.BackgroundRemoval) await SessionServiceHarness.AuthoriseBackgroundRemovalAsync(service, id);
        SessionView view = await Command(service, id, new WorkflowCommand.StartStep(step));
        await Command(service, id, new WorkflowCommand.Approve(step, view.Steps.Single(s => s.Step == step).CurrentRevisionSha256!.Value));
    }

    internal static async Task<SessionView> Command(ISessionService service, SessionId id, WorkflowCommand command)
    {
        var result = await service.ExecuteAsync(id, command, "retention-test", CancellationToken.None);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
        return result.Value;
    }

    internal static async Task<SessionAggregate> Load(SessionServiceHarness h, SessionId id) =>
        (await h.Repository.LoadAsync(id, CancellationToken.None)).Value!;

    internal static async Task<StartupRecoveryReport> Restart(SessionServiceHarness h)
    {
        var result = await new StartupRecoveryService(WorkflowEngine.Instance,
            new SqliteSessionRepository(h.Database.Factory), new FileWorkspace(h.Workspace.Root),
            new FakeProcessLiveness(ProcessLiveness.Dead), SystemIdGenerator.Instance, h.Clock)
            .RecoverAsync(CancellationToken.None);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
        return result.Value;
    }

    internal static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static bool IsTiff(WorkspaceFileRef f) => f.FileName.EndsWith(".tif", StringComparison.OrdinalIgnoreCase);
}
