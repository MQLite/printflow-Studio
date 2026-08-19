using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// Closes the known Part 2 gap (Part 2 report §9, §18): <c>AddAnotherSize</c> sibling survival,
/// driven end to end through <see cref="SessionService"/> against a real temporary workspace,
/// real SQLite, real fake output files, and real hashes (Epic 11100 Part 3A §9).
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class AddAnotherSizeTests
{
    [Fact]
    public async Task Approving_the_second_size_leaves_both_outputs_valid_and_approved()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        (SessionId id, PrintOutputId outputAId) = await CompleteFirstOutputAsync(harness, service);
        await AddAnotherSizeAsync(service, id);

        SessionStep photoshopStepB = await StartPhotoshopOutputAsync(service, id, widthMm: 150);
        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.Approve(StepKind.PhotoshopOutput, photoshopStepB.CurrentRevisionSha256!.Value),
            "tester", CancellationToken.None));

        OperationResult<SessionView> completed =
            await service.ExecuteAsync(id, new WorkflowCommand.Complete(), "tester", CancellationToken.None);
        completed.IsSuccess.ShouldBeTrue(completed.IsFailure ? completed.Failure.ToString() : "");

        SessionAggregate aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        aggregate.Outputs.Count.ShouldBe(2);

        PrintOutput outputA = aggregate.Outputs.Single(o => o.Id == outputAId);
        PrintOutput outputB = aggregate.Outputs.Single(o => o.Id != outputAId);

        outputA.IsValid.ShouldBeTrue();
        outputA.ReviewState.ShouldBe(ReviewState.Approved);
        outputB.IsValid.ShouldBeTrue();
        outputB.ReviewState.ShouldBe(ReviewState.Approved);

        // Siblings, not a chain: both derive directly from the same shared source Revision.
        outputA.SourceRevisionId.ShouldBe(outputB.SourceRevisionId);
        outputA.Dimensions.WidthMm.ShouldBe(200);
        outputB.Dimensions.WidthMm.ShouldBe(150);

        System.IO.File.Exists(harness.FileWorkspace.ResolveAbsolute(outputA.File)).ShouldBeTrue();
        System.IO.File.Exists(harness.FileWorkspace.ResolveAbsolute(outputB.File)).ShouldBeTrue();
    }

    [Fact]
    public async Task Rejecting_the_second_size_leaves_the_first_output_untouched()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        (SessionId id, PrintOutputId outputAId) = await CompleteFirstOutputAsync(harness, service);
        await AddAnotherSizeAsync(service, id);

        SessionStep photoshopStepB = await StartPhotoshopOutputAsync(service, id, widthMm: 150);
        OperationResult<SessionView> rejectedB = await service.ExecuteAsync(
            id,
            new WorkflowCommand.Reject(
                StepKind.PhotoshopOutput, photoshopStepB.CurrentRevisionSha256!.Value,
                RejectionReason.DimensionIssue, "wrong size after all"),
            "tester", CancellationToken.None);
        rejectedB.IsSuccess.ShouldBeTrue(rejectedB.IsFailure ? rejectedB.Failure.ToString() : "");

        SessionAggregate aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        PrintOutput outputA = aggregate.Outputs.Single(o => o.Id == outputAId);
        PrintOutput outputB = aggregate.Outputs.Single(o => o.Id != outputAId);

        // B's rejection does not touch A at all: still valid, still approved.
        outputA.IsValid.ShouldBeTrue();
        outputA.ReviewState.ShouldBe(ReviewState.Approved);

        outputB.ReviewState.ShouldBe(ReviewState.Rejected);
        aggregate.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State.ShouldBe(StepState.RetryRequired);
    }

    [Fact]
    public async Task Returning_upstream_of_the_shared_source_invalidates_both_dependent_outputs()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();

        (SessionId id, PrintOutputId outputAId) = await CompleteFirstOutputAsync(harness, service);
        await AddAnotherSizeAsync(service, id);

        SessionStep photoshopStepB = await StartPhotoshopOutputAsync(service, id, widthMm: 150);
        OperationResult<SessionView> approvedB = await service.ExecuteAsync(
            id, new WorkflowCommand.Approve(StepKind.PhotoshopOutput, photoshopStepB.CurrentRevisionSha256!.Value),
            "tester", CancellationToken.None);
        approvedB.IsSuccess.ShouldBeTrue(approvedB.IsFailure ? approvedB.Failure.ToString() : "");
        RevisionId outputBRevisionId = photoshopStepB.CurrentRevisionId!.Value;

        // Currently on PrintDimensions/PhotoshopOutput's shared upstream: OriginalConfirmation,
        // which is upstream of the Import revision both A and B were built from.
        OperationResult<SessionView> returned = await service.ExecuteAsync(
            id, new WorkflowCommand.ReturnToStep(StepKind.OriginalConfirmation), "tester", CancellationToken.None);
        returned.IsSuccess.ShouldBeTrue(returned.IsFailure ? returned.Failure.ToString() : "");

        SessionAggregate aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        Revision rootRevision = aggregate.Revisions.Single(r => r.IsRoot);
        rootRevision.IsValid.ShouldBeTrue();

        Revision revisionA = aggregate.Revisions.Single(r => r.Id == RevisionId.From(outputAId.Value));
        Revision revisionB = aggregate.Revisions.Single(r => r.Id == outputBRevisionId);
        revisionA.IsValid.ShouldBeFalse();
        revisionB.IsValid.ShouldBeFalse();

        PrintOutput outputA = aggregate.Outputs.Single(o => o.Id == outputAId);
        PrintOutput outputB = aggregate.Outputs.Single(o => o.Id != outputAId);
        outputA.IsValid.ShouldBeFalse();
        outputB.IsValid.ShouldBeFalse();
    }

    // -----------------------------------------------------------------------------------
    // Shared setup: a completed GeneratePrintTiff session with one approved size (A).
    // -----------------------------------------------------------------------------------

    private static async Task<(SessionId Id, PrintOutputId OutputAId)> CompleteFirstOutputAsync(
        SessionServiceHarness harness, ISessionService service)
    {
        string source = harness.WriteSourcePng();

        SessionId id = (await service.ImportAsync(
            WorkflowType.GeneratePrintTiff, source, "sibling-test", "tester", CancellationToken.None)).Value.Id;

        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));

        SessionStep photoshopStepA = await StartPhotoshopOutputAsync(service, id, widthMm: 200);
        OperationResult<SessionView> approvedA = await service.ExecuteAsync(
            id, new WorkflowCommand.Approve(StepKind.PhotoshopOutput, photoshopStepA.CurrentRevisionSha256!.Value),
            "tester", CancellationToken.None);
        approvedA.IsSuccess.ShouldBeTrue(approvedA.IsFailure ? approvedA.Failure.ToString() : "");

        OperationResult<SessionView> completed =
            await service.ExecuteAsync(id, new WorkflowCommand.Complete(), "tester", CancellationToken.None);
        completed.IsSuccess.ShouldBeTrue(completed.IsFailure ? completed.Failure.ToString() : "");
        completed.Value.State.ShouldBe(SessionState.Completed);

        PrintOutputId outputAId = PrintOutputId.From(photoshopStepA.CurrentRevisionId!.Value.Value);
        return (id, outputAId);
    }

    private static async Task AddAnotherSizeAsync(ISessionService service, SessionId id)
    {
        OperationResult<SessionView> reopened =
            await service.ExecuteAsync(id, new WorkflowCommand.AddAnotherSize(), "tester", CancellationToken.None);
        reopened.IsSuccess.ShouldBeTrue(reopened.IsFailure ? reopened.Failure.ToString() : "");
        reopened.Value.State.ShouldBe(SessionState.Active);
    }

    private static async Task<SessionStep> StartPhotoshopOutputAsync(
        ISessionService service, SessionId id, int widthMm)
    {
        PrintDimensions dimensions = PrintDimensions.FromMillimetres(widthMm, 150, SizePreset.Custom);
        await Must(service.ExecuteAsync(
            id, new WorkflowCommand.SetPrintDimensions(dimensions), "tester", CancellationToken.None));
        await Must(service.ExecuteAsync(
            id,
            new WorkflowCommand.SelectWhiteUnderbaseBranch(WhiteUnderbaseBranch.W1_1px, "ordinary design"),
            "tester", CancellationToken.None));

        OperationResult<SessionView> started = await service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None);
        started.IsSuccess.ShouldBeTrue(started.IsFailure ? started.Failure.ToString() : "");

        SessionStep photoshopStep = started.Value.Steps.Single(s => s.Step == StepKind.PhotoshopOutput);
        photoshopStep.State.ShouldBe(StepState.ReviewRequired);
        return photoshopStep;
    }

    private static async Task Must(Task<OperationResult<SessionView>> resultTask)
    {
        OperationResult<SessionView> result = await resultTask;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
    }
}
