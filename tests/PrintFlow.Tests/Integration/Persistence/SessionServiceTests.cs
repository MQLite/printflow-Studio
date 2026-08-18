using System.IO;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// The "glue" row of Epic 11100 plan §20.2: <see cref="SessionService"/> driving the real
/// workspace, file inspector and SQLite repository together, proving restart/resume,
/// hash-bound approval integrity, downstream invalidation, transactional commit, and the
/// automation lock (task §37, §47–§49).
/// </summary>
public sealed class SessionServiceTests
{
    [Fact]
    public async Task Import_produces_an_approved_root_revision_and_a_read_only_snapshot()
    {
        using SessionServiceHarness harness = new();
        string source = harness.WriteSourcePng();

        OperationResult<SessionView> result = await harness.CreateService().ImportAsync(
            WorkflowType.PrepareAsset, source, "客户设计", "tester", CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
        SessionView view = result.Value;
        view.Steps.Single(s => s.Step == StepKind.Import).State.ShouldBe(StepState.Approved);
        view.Steps.Single(s => s.Step == StepKind.OriginalConfirmation).State.ShouldBe(StepState.Waiting);
        view.OutputName.Value.ShouldBe("客户设计");

        OperationResult<SessionAggregate?> aggregate = await harness.Repository.LoadAsync(view.Id, CancellationToken.None);
        Revision root = aggregate.Value!.Revisions.Single();
        string snapshotAbsolute = harness.FileWorkspace.ResolveAbsolute(root.File);
        File.Exists(snapshotAbsolute).ShouldBeTrue();
        File.GetAttributes(snapshotAbsolute).HasFlag(FileAttributes.ReadOnly).ShouldBeTrue();

        // The operator's original file is completely untouched.
        File.Exists(source).ShouldBeTrue();
        File.GetAttributes(source).HasFlag(FileAttributes.ReadOnly).ShouldBeFalse();
    }

    [Fact]
    public async Task Full_walkthrough_reaches_Completed_with_a_real_approved_png_on_disk()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        string source = harness.WriteSourcePng();

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, source, "art", "tester", CancellationToken.None)).Value.Id;

        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));
        await RunAndApprove(service, id, StepKind.Enhancement);
        await RunAndApprove(service, id, StepKind.BackgroundRemoval);
        await RunAndApprove(service, id, StepKind.Trim);
        await Must(service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.ApprovedPngExport), "tester", CancellationToken.None));

        OperationResult<SessionView> completed =
            await service.ExecuteAsync(id, new WorkflowCommand.Complete(), "tester", CancellationToken.None);

        completed.IsSuccess.ShouldBeTrue(completed.IsFailure ? completed.Failure.ToString() : "");
        completed.Value.State.ShouldBe(SessionState.Completed);

        OperationResult<SessionAggregate?> aggregate = await harness.Repository.LoadAsync(id, CancellationToken.None);
        Revision approvedExport = aggregate.Value!.Revisions.Single(r => r.Operation == OperationKind.PromoteApproved);
        string approvedAbsolute = harness.FileWorkspace.ResolveAbsolute(approvedExport.File);
        File.Exists(approvedAbsolute).ShouldBeTrue();
        approvedAbsolute.ShouldContain($"{Path.DirectorySeparatorChar}Approved{Path.DirectorySeparatorChar}");
    }

    [Fact]
    public async Task Restart_and_reload_restores_a_structurally_identical_snapshot()
    {
        using SessionServiceHarness harness = new();
        ISessionService firstService = harness.CreateService();
        string source = harness.WriteSourcePng();

        SessionId id = (await firstService.ImportAsync(
            WorkflowType.PrepareAsset, source, "resume-test", "tester", CancellationToken.None)).Value.Id;
        await Must(firstService.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));
        await RunAndApprove(firstService, id, StepKind.Enhancement);

        WorkflowSnapshot before = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!.ToSnapshot();

        // Simulate closing and reopening the application: a brand-new repository/service pair
        // against the same on-disk database, sharing nothing in memory with the first.
        RestartedRepositoryProbe probe = new(harness.Database.Factory);
        WorkflowSnapshot after = (await probe.LoadAsync(id)).ToSnapshot();

        after.ShouldBe(before);
    }

    [Fact]
    public async Task Mutating_an_approved_file_on_disk_invalidates_it_and_refuses_consumption()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        string source = harness.WriteSourcePng();

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, source, "art", "tester", CancellationToken.None)).Value.Id;
        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));
        SessionView afterEnhancement = await RunAndApprove(service, id, StepKind.Enhancement);

        SessionStep enhancementStep = afterEnhancement.Steps.Single(s => s.Step == StepKind.Enhancement);
        Revision enhancementRevision = (await harness.Repository.LoadAsync(id, CancellationToken.None))
            .Value!.Revisions.Single(r => r.Id == enhancementStep.CurrentRevisionId!.Value);
        string approvedFilePath = harness.FileWorkspace.ResolveAbsolute(enhancementRevision.File);

        // Mutate one byte on disk, exactly as an operator editing the file outside PrintFlow would.
        byte[] bytes = File.ReadAllBytes(approvedFilePath);
        bytes[^1] ^= 0xFF;
        File.WriteAllBytes(approvedFilePath, bytes);

        OperationResult<SessionView> attempt = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.BackgroundRemoval), "tester", CancellationToken.None);

        attempt.IsFailure.ShouldBeTrue();
        attempt.Failure.Code.ShouldBe(FailureCode.RevisionIntegrityMismatch);

        SessionAggregate reloaded = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision invalidated = reloaded.Revisions.Single(r => r.Id == enhancementRevision.Id);
        invalidated.IsValid.ShouldBeFalse();
        invalidated.InvalidationReason.ShouldBe(InvalidationReason.FileMutated);

        // The original approval decision remains in the audit history, untouched.
        reloaded.Reviews.ShouldContain(r => r.Step == StepKind.Enhancement && r.IsApproved);
    }

    [Fact]
    public async Task ReturnToStep_invalidates_the_descendant_but_the_returned_to_revision_itself_stays_valid()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        string source = harness.WriteSourcePng();

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, source, "art", "tester", CancellationToken.None)).Value.Id;
        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));
        SessionView afterEnhancement = await RunAndApprove(service, id, StepKind.Enhancement);
        SessionView afterBackground = await RunAndApprove(service, id, StepKind.BackgroundRemoval);

        RevisionId enhancementRevisionId = afterEnhancement.Steps.Single(s => s.Step == StepKind.Enhancement).CurrentRevisionId!.Value;
        RevisionId backgroundRevisionId = afterBackground.Steps.Single(s => s.Step == StepKind.BackgroundRemoval).CurrentRevisionId!.Value;

        OperationResult<SessionView> returned = await service.ExecuteAsync(
            id, new WorkflowCommand.ReturnToStep(StepKind.Enhancement), "tester", CancellationToken.None);
        returned.IsSuccess.ShouldBeTrue(returned.IsFailure ? returned.Failure.ToString() : "");

        SessionAggregate reloaded = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        reloaded.Revisions.Single(r => r.Id == backgroundRevisionId).IsValid.ShouldBeFalse();
        reloaded.Revisions.Single(r => r.Id == enhancementRevisionId).IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task Repository_commit_is_all_or_nothing_a_mid_batch_failure_persists_nothing_from_that_batch()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        string source = harness.WriteSourcePng();

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, source, "art", "tester", CancellationToken.None)).Value.Id;

        SessionAggregate aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision existingRoot = aggregate.Revisions.Single();

        Revision freshRevision = Revision.Create(
            RevisionId.From(Guid.CreateVersion7()), id, existingRoot.Id, OperationKind.Enhance,
            existingRoot.File, existingRoot.Facts, DateTimeOffset.UtcNow);

        // The second "new" revision deliberately reuses an Id that already exists, forcing a
        // primary-key violation partway through the batch.
        Revision conflictingRevision = existingRoot with { CreatedAtUtc = existingRoot.CreatedAtUtc };

        SessionMutation mutation = new(
            aggregate.Session, aggregate.Steps, [freshRevision, conflictingRevision], [], [], [], [], null, null);

        OperationResult<PrintFlow.Domain.Results.Unit> committed = await harness.Repository.CommitAsync(mutation, CancellationToken.None);
        committed.IsFailure.ShouldBeTrue();

        SessionAggregate afterFailedCommit = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        afterFailedCommit.Revisions.ShouldNotContain(r => r.Id == freshRevision.Id);
        afterFailedCommit.Revisions.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_step_that_needs_the_automation_lock_is_refused_while_another_session_holds_it()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        string source = harness.WriteSourcePng();

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, source, "art", "tester", CancellationToken.None)).Value.Id;
        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));

        // The lock's SessionId column is foreign-keyed to ProcessingSession, so "another
        // session holds it" needs a second real, persisted session — not an arbitrary guid.
        string otherSource = harness.WriteSourcePng("other-source.png");
        SessionId foreignHolder = (await service.ImportAsync(
            WorkflowType.PrepareAsset, otherSource, "other", "tester", CancellationToken.None)).Value.Id;

        SessionAggregate aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        AutomationLockChange acquireForOther = new(
            AutomationLockAction.Acquire, foreignHolder, DateTimeOffset.UtcNow, 99999, "OTHER-MACHINE");
        SessionMutation lockOnly = new(aggregate.Session, [], [], [], [], [], [], null, acquireForOther);
        (await harness.Repository.CommitAsync(lockOnly, CancellationToken.None)).IsSuccess.ShouldBeTrue();

        OperationResult<SessionView> blocked = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);

        blocked.IsFailure.ShouldBeTrue();
        blocked.Failure.Code.ShouldBe(FailureCode.AdapterUnavailable);
    }

    private static async Task<SessionView> RunAndApprove(ISessionService service, SessionId id, StepKind step)
    {
        OperationResult<SessionView> started =
            await service.ExecuteAsync(id, new WorkflowCommand.StartStep(step), "tester", CancellationToken.None);
        started.IsSuccess.ShouldBeTrue(started.IsFailure ? started.Failure.ToString() : "");

        SessionStep current = started.Value.Steps.Single(s => s.Step == step);
        Sha256 hash = current.CurrentRevisionSha256!.Value;

        OperationResult<SessionView> approved = await service.ExecuteAsync(
            id, new WorkflowCommand.Approve(step, hash), "tester", CancellationToken.None);
        approved.IsSuccess.ShouldBeTrue(approved.IsFailure ? approved.Failure.ToString() : "");
        return approved.Value;
    }

    private static async Task Must(Task<OperationResult<SessionView>> resultTask)
    {
        OperationResult<SessionView> result = await resultTask;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
    }
}

/// <summary>A throwaway repository handle standing in for "a freshly started application process".</summary>
file sealed class RestartedRepositoryProbe(PrintFlow.Infrastructure.Sqlite.SqliteConnectionFactory factory)
{
    private readonly PrintFlow.Infrastructure.Sqlite.SqliteSessionRepository _repository = new(factory);

    public async Task<SessionAggregate> LoadAsync(SessionId id)
    {
        OperationResult<SessionAggregate?> loaded = await _repository.LoadAsync(id, CancellationToken.None);
        return loaded.Value!;
    }
}
