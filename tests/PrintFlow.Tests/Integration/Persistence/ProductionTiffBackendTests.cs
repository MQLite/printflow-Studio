using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;
using Shouldly;

namespace PrintFlow.Tests.Integration.Persistence;

public sealed class ProductionTiffBackendTests
{
    [Theory]
    [InlineData(WorkflowType.GeneratePrintTiff)]
    [InlineData(WorkflowType.PrepareCustomerDesign)]
    public async Task Resumed_local_export_reaches_review_while_the_desktop_is_owned(WorkflowType workflow)
    {
        using SessionServiceHarness harness = new();
        LocalTiffProcessor local = new(harness.FileWorkspace);
        ISessionService service = harness.CreateService(localProductionTiff: local);
        SessionId id = await ReadyToExportAsync(harness, service, workflow);
        (await service.LoadAsync(id, CancellationToken.None)).Value.ProductionTiffBackend
            .ShouldBe(ProductionTiffBackend.Photoshop);
        await ExecuteAsync(service, id, new WorkflowCommand.SetProductionTiffBackend(ProductionTiffBackend.MagickNet));

        SessionId otherId = (await service.ImportAsync(WorkflowType.PrepareAsset, harness.WriteBorderedSourcePng(),
            "other", "tester", CancellationToken.None)).Value.Id;
        var other = (await harness.Repository.LoadAsync(otherId, CancellationToken.None)).Value!;
        var desktop = (await harness.AutomationLeases.TryAcquireAsync(null, CancellationToken.None)).Value;
        try
        {
            var committed = await harness.Repository.CommitAsync(SessionMutation.Empty(other.Session) with
            {
                LockChange = new(AutomationLockAction.Acquire, otherId, harness.Clock.GetUtcNow(),
                    Environment.ProcessId, Environment.MachineName),
            }, CancellationToken.None);
            committed.IsSuccess.ShouldBeTrue();

            ISessionService resumed = harness.CreateService(localProductionTiff: local);
            (await resumed.LoadAsync(id, CancellationToken.None)).Value.ProductionTiffBackend
                .ShouldBe(ProductionTiffBackend.MagickNet);
            SessionView review = await ExecuteAsync(resumed, id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput));

            review.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
            review.AvailableCommands.ShouldNotContain(CommandKind.SetProductionTiffBackend);
            var aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
            var attempt = aggregate.Attempts.Single(a => a.Step == StepKind.PhotoshopOutput);
            attempt.AdapterId.ShouldBe("test-local-cmyk-w1");
            aggregate.Outputs.Single().File.Area.ShouldBe(WorkspaceArea.Working);
            var reviewed = review.CurrentArtefact!;
            await ExecuteAsync(resumed, id, new WorkflowCommand.Approve(StepKind.PhotoshopOutput, reviewed.Sha256));
            await ExecuteAsync(resumed, id, new WorkflowCommand.Complete());
            aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
            aggregate.Outputs.Single().File.Area.ShouldBe(WorkspaceArea.Approved);
            (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value.SessionId.ShouldBe(otherId);
        }
        finally
        {
            await desktop.ReleaseAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(ProductionTiffBackend.Photoshop)]
    [InlineData(ProductionTiffBackend.MagickNet)]
    public async Task Final_review_can_reserve_and_approve_after_the_task_timestamp_advances(ProductionTiffBackend backend)
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService(
            photoshop: new SyntheticProductionTiffProcessor(harness.FileWorkspace),
            localProductionTiff: new LocalTiffProcessor(harness.FileWorkspace));
        SessionId id = await ReadyToExportAsync(harness, service, WorkflowType.GeneratePrintTiff);
        if (backend == ProductionTiffBackend.MagickNet)
            await ExecuteAsync(service, id, new WorkflowCommand.SetProductionTiffBackend(backend));

        SessionView review = await ExecuteAsync(service, id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput));
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        await ExecuteAsync(service, id, new WorkflowCommand.Approve(StepKind.PhotoshopOutput, review.CurrentArtefact!.Sha256));

        var aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        var output = aggregate.Outputs.Single();
        output.File.Area.ShouldBe(WorkspaceArea.Approved);
        output.PromotionReservation.ShouldBeNull();
        aggregate.Steps.Single(step => step.Step == StepKind.PhotoshopOutput).State.ShouldBe(StepState.Approved);
    }

    [Fact]
    public async Task A_local_file_changed_after_validation_never_enters_review()
    {
        using SessionServiceHarness harness = new();
        LocalTiffProcessor local = new(harness.FileWorkspace) { TamperAfterValidation = true };
        ISessionService service = harness.CreateService(localProductionTiff: local);
        SessionId id = await ReadyToExportAsync(harness, service, WorkflowType.GeneratePrintTiff);
        await ExecuteAsync(service, id, new WorkflowCommand.SetProductionTiffBackend(ProductionTiffBackend.MagickNet));

        var result = await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput),
            "tester", CancellationToken.None);

        result.Failure.Code.ShouldBe(FailureCode.OutputValidationFailed);
        var aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        aggregate.Outputs.ShouldBeEmpty();
        aggregate.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State.ShouldBe(StepState.Failed);
    }

    [Fact]
    public async Task Unavailable_local_export_requires_an_explicit_backend_change()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService(localProductionTiff: new LocalTiffProcessor(harness.FileWorkspace));
        SessionId id = await ReadyToExportAsync(harness, service, WorkflowType.GeneratePrintTiff);
        await ExecuteAsync(service, id, new WorkflowCommand.SetProductionTiffBackend(ProductionTiffBackend.MagickNet));

        ISessionService resumed = harness.CreateService();
        SessionView view = (await resumed.LoadAsync(id, CancellationToken.None)).Value;
        view.ProductionTiffBackend.ShouldBe(ProductionTiffBackend.MagickNet);
        view.MagickNetTiffUnavailable!.Code.ShouldBe(FailureCode.AdapterUnavailable);
        view.AvailableCommands.ShouldNotContain(CommandKind.StartStep);
        var refused = await resumed.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput),
            "tester", CancellationToken.None);
        refused.IsFailure.ShouldBeTrue();
        var aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        aggregate.Attempts.ShouldNotContain(a => a.Step == StepKind.PhotoshopOutput);

        await ExecuteAsync(resumed, id, new WorkflowCommand.SetProductionTiffBackend(ProductionTiffBackend.Photoshop));
        await ExecuteAsync(resumed, id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput));
        aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        aggregate.Attempts.Single(a => a.Step == StepKind.PhotoshopOutput).AdapterId.ShouldBe(harness.FakePhotoshop.AdapterId);
    }

    [Fact]
    public async Task A_new_size_beyond_the_local_limit_is_refused_before_opening_an_attempt()
    {
        using SessionServiceHarness harness = new();
        LocalTiffProcessor local = new(harness.FileWorkspace) { MaximumProjectedWidth = 16 };
        ISessionService service = harness.CreateService(localProductionTiff: local);
        SessionId id = await ReadyToExportAsync(harness, service, WorkflowType.GeneratePrintTiff);
        await ExecuteAsync(service, id, new WorkflowCommand.SetProductionTiffBackend(ProductionTiffBackend.MagickNet));

        await ExecuteAsync(service, id, new WorkflowCommand.ReturnToStep(StepKind.PrintDimensions));
        SessionView sized = await ExecuteAsync(service, id, new WorkflowCommand.SetCustomTargetEdgeSize(TargetEdge.Width, 2m));
        Guid offer = sized.Preflight!.EnlargementOfferId.ShouldNotBeNull();
        (await service.AuthoriseCurrentEnlargementAsync(id, offer, "tester", CancellationToken.None)).IsSuccess.ShouldBeTrue();
        await ExecuteAsync(service, id, new WorkflowCommand.SelectWhiteUnderbaseBranch(WhiteUnderbaseBranch.W1_1px, "finished design"));

        SessionView view = (await service.LoadAsync(id, CancellationToken.None)).Value;
        view.ProductionTiffBackend.ShouldBe(ProductionTiffBackend.MagickNet);
        view.MagickNetTiffUnavailable!.MessageKey.ShouldBe("ProductionTiff_MagickDimensionsLimit");
        view.AvailableCommands.ShouldNotContain(CommandKind.StartStep);

        var refused = await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput),
            "tester", CancellationToken.None);
        refused.Failure.MessageKey.ShouldBe("ProductionTiff_MagickDimensionsLimit");
        var aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        aggregate.Attempts.ShouldNotContain(a => a.Step == StepKind.PhotoshopOutput);
    }

    [Fact]
    public async Task Stopped_local_export_has_no_desktop_takeover_and_preserves_attempt_identity_on_retry()
    {
        using SessionServiceHarness harness = new();
        LocalTiffProcessor local = new(harness.FileWorkspace) { WaitForStop = true };
        ISessionService service = harness.CreateService(localProductionTiff: local);
        SessionId id = await ReadyToExportAsync(harness, service, WorkflowType.GeneratePrintTiff);
        await ExecuteAsync(service, id, new WorkflowCommand.SetProductionTiffBackend(ProductionTiffBackend.MagickNet));

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
        var running = service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", timeout.Token);
        await local.Started.Task.WaitAsync(timeout.Token);
        var change = await service.ExecuteAsync(id, new WorkflowCommand.SetProductionTiffBackend(ProductionTiffBackend.Photoshop),
            "tester", timeout.Token);
        change.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        service.GetAutomationRuntime(id).CanTakeOverAutomation.ShouldBeFalse();
        service.RequestStop(id, AutomationStopMode.TakeOver).IsFailure.ShouldBeTrue();
        service.RequestStop(id, AutomationStopMode.StopOperation).IsSuccess.ShouldBeTrue();
        (await running).Failure.Code.ShouldBe(FailureCode.Cancelled);

        await ExecuteAsync(service, id, new WorkflowCommand.Retry(StepKind.PhotoshopOutput));
        await ExecuteAsync(service, id, new WorkflowCommand.SetProductionTiffBackend(ProductionTiffBackend.Photoshop));
        await ExecuteAsync(service, id, new WorkflowCommand.StartStep(StepKind.PhotoshopOutput));
        var aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        var attempts = aggregate.Attempts.Where(a => a.Step == StepKind.PhotoshopOutput).OrderBy(a => a.RetrySequence).ToArray();
        attempts.Select(a => a.AdapterId).ShouldBe([local.AdapterId, harness.FakePhotoshop.AdapterId]);
        attempts[1].RetryOfAttemptId.ShouldBe(attempts[0].Id);
    }

    private static async Task<SessionId> ReadyToExportAsync(
        SessionServiceHarness harness, ISessionService service, WorkflowType workflow)
    {
        SessionId id = workflow == WorkflowType.PrepareCustomerDesign
            ? await KeepOriginalExtentPersistenceTests.AtTrim(harness, service, workflow)
            : (await service.ImportAsync(workflow, harness.WriteBorderedSourcePng(), "export", "tester", CancellationToken.None)).Value.Id;
        if (workflow == WorkflowType.PrepareCustomerDesign)
            await ExecuteAsync(service, id, new WorkflowCommand.KeepOriginalExtent());
        else
            await ExecuteAsync(service, id, new WorkflowCommand.ConfirmOriginal("finished design"));
        await ExecuteAsync(service, id, new WorkflowCommand.SetPrintDimensions(
            PrintDimensions.FromMillimetres(200, 150, SizePreset.Custom)));
        await ExecuteAsync(service, id, new WorkflowCommand.SelectWhiteUnderbaseBranch(WhiteUnderbaseBranch.W1_1px, "finished design"));
        return id;
    }

    private static async Task<SessionView> ExecuteAsync(ISessionService service, SessionId id, WorkflowCommand command)
    {
        var result = await service.ExecuteAsync(id, command, "tester", CancellationToken.None);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);
        return result.Value;
    }

    private sealed class LocalTiffProcessor(IWorkspace workspace) : ILocalProductionTiffProcessor
    {
        private readonly SyntheticProductionTiffProcessor _fixture = new(workspace);

        public string AdapterId => "test-local-cmyk-w1";
        public bool WaitForStop { get; init; }
        public bool TamperAfterValidation { get; init; }
        public int MaximumProjectedWidth { get; init; } = int.MaxValue;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public OperationFailure? GetAvailability(FileFacts input, PhotoshopPreparation? preparation) =>
            preparation is not null && preparation.ProjectedPixelWidth > MaximumProjectedWidth
                ? OperationFailure.Create(FailureCode.PreconditionNotMet, "Test local TIFF width limit exceeded.",
                    messageKey: "ProductionTiff_MagickDimensionsLimit")
                : null;

        public async Task<OperationResult<AdapterOutput>> GenerateAsync(PhotoshopRequest request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            if (WaitForStop)
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            var result = await _fixture.GenerateAsync(request, cancellationToken);
            string path = workspace.ResolveAbsolute(result.Value.ProducedFile);
            Sha256 validatedHash = Sha256.FromBytes(SHA256.HashData(await File.ReadAllBytesAsync(path, cancellationToken)));
            if (TamperAfterValidation)
                await File.AppendAllTextAsync(path, "changed", cancellationToken);
            return OperationResult.Ok(result.Value with { ValidatedImageSha256 = validatedHash });
        }
    }
}
