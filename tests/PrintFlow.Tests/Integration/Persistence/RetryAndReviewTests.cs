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
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// Retry-after-failure and reject-then-retry, driven end to end through
/// <see cref="SessionService"/> against real files and a real database (Epic 11100 Part 3A §5–§6).
/// </summary>
public sealed class RetryAndReviewTests
{
    [Fact]
    public async Task Retry_after_a_fake_failure_gets_a_fresh_attempt_and_working_directory_then_succeeds()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        string source = harness.WriteSourcePng();

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, source, "art", "tester", CancellationToken.None)).Value.Id;
        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));

        harness.FakeMeitu.SetScenario(FakeAdapterScenario.FailWith(FailureCode.OutputValidationFailed));
        OperationResult<SessionView> failed = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);
        failed.IsFailure.ShouldBeTrue();

        await Must(service.ExecuteAsync(id, new WorkflowCommand.Retry(StepKind.Enhancement), "tester", CancellationToken.None));

        harness.FakeMeitu.SetScenario(FakeAdapterScenario.Succeed);
        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);
        started.IsSuccess.ShouldBeTrue(started.IsFailure ? started.Failure.ToString() : "");

        SessionStep enhancement = started.Value.Steps.Single(s => s.Step == StepKind.Enhancement);
        Sha256 hash = enhancement.CurrentRevisionSha256!.Value;
        OperationResult<SessionView> approved = await service.ExecuteAsync(
            id, new WorkflowCommand.Approve(StepKind.Enhancement, hash), "tester", CancellationToken.None);
        approved.IsSuccess.ShouldBeTrue(approved.IsFailure ? approved.Failure.ToString() : "");
        approved.Value.Steps.Single(s => s.Step == StepKind.Enhancement).State.ShouldBe(StepState.Approved);

        SessionAggregate aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        List<ProcessingAttempt> enhancementAttempts =
            [.. aggregate.Attempts.Where(a => a.Step == StepKind.Enhancement)];

        // The failed attempt remains persisted, distinct from the retry.
        enhancementAttempts.Count.ShouldBe(2);
        ProcessingAttempt failedAttempt = enhancementAttempts.Single(a => a.Status == AttemptStatus.Failed);
        ProcessingAttempt succeededAttempt = enhancementAttempts.Single(a => a.Status == AttemptStatus.Succeeded);
        failedAttempt.Id.ShouldNotBe(succeededAttempt.Id);
        failedAttempt.OutputRevisionId.ShouldBeNull();

        Revision enhancementRevision = aggregate.Revisions.Single(r => r.Operation == OperationKind.Enhance);
        succeededAttempt.OutputRevisionId.ShouldBe(enhancementRevision.Id);

        // The retry worked from a fresh Working\<attemptId>\ directory tied to the succeeded
        // attempt's own id — structurally distinct from whatever the failed attempt used,
        // never reusing the failed attempt's working copy (MVP design invariant 8).
        enhancementRevision.File.RelativePath.ShouldContain(succeededAttempt.Id.Value.ToString("D"));
        enhancementRevision.File.RelativePath.ShouldNotContain(failedAttempt.Id.Value.ToString("D"));
    }

    [Fact]
    public async Task Reject_then_retry_keeps_the_rejected_Revision_audit_visible_and_produces_a_distinct_approved_one()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        string source = harness.WriteSourcePng();

        SessionId id = (await service.ImportAsync(
            WorkflowType.PrepareAsset, source, "art", "tester", CancellationToken.None)).Value.Id;
        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));

        OperationResult<SessionView> startedA = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);
        startedA.IsSuccess.ShouldBeTrue(startedA.IsFailure ? startedA.Failure.ToString() : "");
        SessionStep stepA = startedA.Value.Steps.Single(s => s.Step == StepKind.Enhancement);
        RevisionId revisionIdA = stepA.CurrentRevisionId!.Value;
        Sha256 hashA = stepA.CurrentRevisionSha256!.Value;

        OperationResult<SessionView> rejected = await service.ExecuteAsync(
            id, new WorkflowCommand.Reject(StepKind.Enhancement, hashA, RejectionReason.InsufficientResult, "not sharp enough"),
            "tester", CancellationToken.None);
        rejected.IsSuccess.ShouldBeTrue(rejected.IsFailure ? rejected.Failure.ToString() : "");
        rejected.Value.Steps.Single(s => s.Step == StepKind.Enhancement).State.ShouldBe(StepState.RetryRequired);

        await Must(service.ExecuteAsync(id, new WorkflowCommand.Retry(StepKind.Enhancement), "tester", CancellationToken.None));

        OperationResult<SessionView> startedB = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);
        startedB.IsSuccess.ShouldBeTrue(startedB.IsFailure ? startedB.Failure.ToString() : "");
        SessionStep stepB = startedB.Value.Steps.Single(s => s.Step == StepKind.Enhancement);
        RevisionId revisionIdB = stepB.CurrentRevisionId!.Value;
        Sha256 hashB = stepB.CurrentRevisionSha256!.Value;

        OperationResult<SessionView> approvedB = await service.ExecuteAsync(
            id, new WorkflowCommand.Approve(StepKind.Enhancement, hashB), "tester", CancellationToken.None);
        approvedB.IsSuccess.ShouldBeTrue(approvedB.IsFailure ? approvedB.Failure.ToString() : "");

        SessionAggregate aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        List<Revision> enhancementRevisions = [.. aggregate.Revisions.Where(r => r.Operation == OperationKind.Enhance)];
        enhancementRevisions.Count.ShouldBe(2);

        // The fake Meitu adapter edits nothing, so A and B are genuinely byte-identical (the
        // same hash) but remain two distinct Revision rows — identity is by RevisionId, not by
        // content, which is exactly what "A is never reused as B" needs to mean here.
        revisionIdA.ShouldNotBe(revisionIdB);
        Revision revisionA = enhancementRevisions.Single(r => r.Id == revisionIdA);
        Revision revisionB = enhancementRevisions.Single(r => r.Id == revisionIdB);

        // Revision A is never reused as B's input; both are independent siblings of the same
        // upstream (the imported root), not a chain A -> B.
        revisionB.SourceRevisionId.ShouldBe(revisionA.SourceRevisionId);

        // The rejection decision on A survives in full, alongside B's approval.
        aggregate.Reviews.ShouldContain(r =>
            r.Step == StepKind.Enhancement && r.SubjectId == revisionA.Id.Value && !r.IsApproved);
        aggregate.Reviews.ShouldContain(r =>
            r.Step == StepKind.Enhancement && r.SubjectId == revisionB.Id.Value && r.IsApproved);

        // Revision A itself is untouched by its own rejection — only descendants of a rejected
        // revision are invalidated (plan §10.4), and A has none.
        revisionA.IsValid.ShouldBeTrue();

        aggregate.Steps.Single(s => s.Step == StepKind.Enhancement).CurrentRevisionId.ShouldBe(revisionB.Id);
    }

    private static async Task Must(Task<OperationResult<SessionView>> resultTask)
    {
        OperationResult<SessionView> result = await resultTask;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
    }
}
