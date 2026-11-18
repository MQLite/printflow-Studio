using System.IO;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

[Collection(SqliteCollection.Name)]
public sealed class ImageProcessingExecutionTests
{
    [Theory]
    [InlineData("valid")]
    [InlineData("tamper")]
    [InlineData("wrong-path")]
    [InlineData("invalid-png")]
    [InlineData("late")]
    [InlineData("recovered")]
    [InlineData("renamed")]
    [InlineData("renamed-failed")]
    [InlineData("renamed-stopped")]
    public async Task Only_current_validated_backend_output_can_enter_review(string scenario)
    {
        using SessionServiceHarness h = new();
        var backend = new Backend(h, scenario);
        ISessionService service = h.CreateService(imageProcessingBackends: [backend]);
        var imported = (await service.ImportAsync(WorkflowType.PrepareAsset,
            h.WriteSourcePng("processed.png"), null, "qa", default)).Value;
        await service.ExecuteAsync(imported.Id, new WorkflowCommand.ConfirmOriginal(), "qa", default);
        var selected = await service.ExecuteAsync(imported.Id, new WorkflowCommand.SetImageProcessingSelection(
            StepKind.Enhancement, SessionImageProcessingSelection.Compatible with { Enhancement = ImageProcessingMethod.LightSharpen }), "qa", default);
        selected.Value.ImageProcessingExecution!.Method.ShouldBe(ImageProcessingMethod.LightSharpen);
        selected.Value.ImageProcessingExecution.DrivesExternalApplication.ShouldBeFalse();

        h.Clock.Advance(TimeSpan.FromTicks(1234));
        Task<OperationResult<SessionView>> start = service.ExecuteAsync(imported.Id,
            new WorkflowCommand.StartStep(StepKind.Enhancement), "qa", default);
        try
        {
            await backend.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var runtime = service.GetAutomationRuntime(imported.Id);
            runtime.CanTakeOverAutomation.ShouldBeFalse();
            runtime.IsCancellableImageProcessing.ShouldBeTrue();
            (await h.Repository.GetAutomationLockAsync(default)).Value.IsHeld.ShouldBeFalse();
            var duplicate = await service.ExecuteAsync(imported.Id,
                new WorkflowCommand.StartStep(StepKind.Enhancement), "qa", default).WaitAsync(TimeSpan.FromSeconds(3));
            duplicate.IsFailure.ShouldBeTrue();
            service.RequestStop(imported.Id, AutomationStopMode.TakeOver).IsFailure.ShouldBeTrue();
            if (scenario == "recovered")
                (await h.CreateRecoveryService(new FakeProcessLiveness()).RecoverAsync(default))
                    .Value.InterruptedAttemptCount.ShouldBe(1);
            if (scenario.StartsWith("renamed", StringComparison.Ordinal))
            {
                var current = (await h.Repository.LoadAsync(imported.Id, default)).Value!;
                var renamed = current.Session with { OutputName = OutputName.Create("concurrent name").Name };
                (await h.Repository.CommitAsync(SessionMutation.Empty(renamed), default)).IsSuccess.ShouldBeTrue();
            }
            if (scenario is "late" or "renamed-stopped")
                service.RequestStop(imported.Id, AutomationStopMode.StopOperation).IsSuccess.ShouldBeTrue();
        }
        finally
        {
            backend.Release.TrySetResult();
            await start.WaitAsync(TimeSpan.FromSeconds(10));
        }
        (await start).IsSuccess.ShouldBe(scenario == "valid");

        var aggregate = (await h.Repository.LoadAsync(imported.Id, default)).Value!;
        var attempt = aggregate.Attempts.Single(attempt => attempt.Step == StepKind.Enhancement);
        attempt.AdapterId.ShouldBe("test-sharpen");
        attempt.ImageProcessingDetails!.Method.ShouldBe(ImageProcessingMethod.LightSharpen);
        attempt.ImageProcessingDetails.AlgorithmVersion.ShouldBe("v1");
        aggregate.Revisions.Any(revision => revision.Id == attempt.OutputRevisionId).ShouldBe(scenario == "valid");
        if (scenario == "valid")
            aggregate.Steps.Single(step => step.Step == StepKind.Enhancement).State.ShouldBe(StepState.ReviewRequired);
        if (scenario is "late" or "renamed-stopped")
            attempt.Status.ShouldBe(AttemptStatus.Cancelled);
        if (scenario == "recovered")
            attempt.Status.ShouldBe(AttemptStatus.Interrupted);
        if (scenario.StartsWith("renamed", StringComparison.Ordinal))
        {
            aggregate.Session.OutputName.Value.ShouldBe("concurrent name");
            attempt.Status.ShouldBe(scenario == "renamed-stopped" ? AttemptStatus.Cancelled : AttemptStatus.Failed);
        }
        if (scenario == "tamper")
        {
            var selectionChange = await service.ExecuteAsync(imported.Id,
                new WorkflowCommand.SetImageProcessingSelection(StepKind.Enhancement, SessionImageProcessingSelection.Compatible),
                "qa", default);
            selectionChange.IsSuccess.ShouldBeTrue();
            var retry = await service.ExecuteAsync(imported.Id, new WorkflowCommand.Retry(StepKind.Enhancement), "qa", default);
            retry.IsSuccess.ShouldBeTrue();
            var retried = await service.ExecuteAsync(imported.Id, new WorkflowCommand.StartStep(StepKind.Enhancement), "qa", default);
            retried.IsSuccess.ShouldBeTrue(retried.IsFailure ? retried.Failure.TechnicalDetail : null);
            var attempts = (await h.Repository.LoadAsync(imported.Id, default)).Value!.Attempts
                .Where(recorded => recorded.Step == StepKind.Enhancement).ToArray();
            attempts.Single(recorded => recorded.Id == attempt.Id).ImageProcessingDetails.ShouldBe(attempt.ImageProcessingDetails);
            var second = attempts.Single(recorded => recorded.Id != attempt.Id);
            second.RetryOfAttemptId.ShouldBe(attempt.Id);
            second.ImageProcessingDetails!.Method.ShouldBe(ImageProcessingMethod.MeituEnhancement);
        }
    }

    [Fact]
    public async Task Changing_the_background_method_withdraws_the_previous_content_authority()
    {
        using SessionServiceHarness h = new();
        ISessionService service = h.CreateService();
        var imported = (await service.ImportAsync(WorkflowType.PrepareAsset,
            h.WriteSourcePng("source.png"), null, "qa", default)).Value;
        (await service.ExecuteAsync(imported.Id, new WorkflowCommand.ConfirmOriginal(), "qa", default)).IsSuccess.ShouldBeTrue();
        var background = (await service.ExecuteAsync(imported.Id, new WorkflowCommand.Skip(StepKind.Enhancement), "qa", default)).Value;
        var input = background.CurrentArtefact.ShouldNotBeNull();
        var approved = await service.ExecuteAsync(imported.Id, new WorkflowCommand.SetBackgroundRemovalDecision(
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent, input.RevisionId, input.Sha256), "qa", default);
        approved.IsSuccess.ShouldBeTrue();

        var newSelection = SessionImageProcessingSelection.Compatible with { BackgroundRemoval = ImageProcessingMethod.BiRefNet };
        var changed = await service.ExecuteAsync(imported.Id, new WorkflowCommand.SetImageProcessingSelection(
            StepKind.BackgroundRemoval, newSelection), "qa", default);
        changed.IsSuccess.ShouldBeTrue();
        var restored = (await service.LoadAsync(imported.Id, default)).Value;
        restored.ImageProcessingSelection.ShouldBe(newSelection);
        restored.ImageProcessingExecution!.Unavailable.ShouldNotBeNull();
        restored.CanRunBackgroundRemoval.ShouldBeFalse();
        (await h.Repository.LoadAsync(imported.Id, default)).Value!.Session.BackgroundRemovalAuthority.ShouldBeNull();
        var stale = await service.ExecuteAsync(imported.Id, new WorkflowCommand.SetBackgroundRemovalDecision(
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent, input.RevisionId, input.Sha256), "qa", default);
        stale.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Local_processing_is_bounded_without_locking_or_displacing_other_sessions()
    {
        using SessionServiceHarness h = new();
        var backend = new Backend(h, "valid");
        ISessionService service = h.CreateService(imageProcessingBackends: [backend]);
        var ids = new List<SessionId>();
        for (int index = 0; index < 3; index++)
        {
            SessionId id = (await service.ImportAsync(WorkflowType.PrepareAsset, h.WriteSourcePng($"source-{index}.png"),
                null, "qa", default)).Value.Id;
            (await service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "qa", default)).IsSuccess.ShouldBeTrue();
            (await service.ExecuteAsync(id, new WorkflowCommand.SetImageProcessingSelection(StepKind.Enhancement,
                SessionImageProcessingSelection.Compatible with { Enhancement = ImageProcessingMethod.LightSharpen }),
                "qa", default)).IsSuccess.ShouldBeTrue();
            ids.Add(id);
        }

        var command = new WorkflowCommand.StartStep(StepKind.Enhancement);
        Task<OperationResult<SessionView>> first = service.ExecuteAsync(ids[0], command, "qa", default);
        Task<OperationResult<SessionView>> second = service.ExecuteAsync(ids[1], command, "qa", default);
        try
        {
            await backend.BothEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            service.GetAutomationRuntime(ids[0]).CanStopAutomation.ShouldBeTrue();
            service.GetAutomationRuntime(ids[1]).CanStopAutomation.ShouldBeTrue();
            var refused = await service.ExecuteAsync(ids[2], command, "qa", default);
            refused.Failure.Code.ShouldBe(FailureCode.AdapterUnavailable);
            (await h.Repository.LoadAsync(ids[2], default)).Value!.Attempts
                .ShouldNotContain(attempt => attempt.Step == StepKind.Enhancement);
            (await h.Repository.GetAutomationLockAsync(default)).Value.IsHeld.ShouldBeFalse();
        }
        finally
        {
            backend.Release.TrySetResult();
            (await Task.WhenAll(first, second)).ShouldAllBe(result => result.IsSuccess);
        }
    }

    private sealed class Backend(SessionServiceHarness harness, string scenario) : IImageProcessingBackend
    {
        private int _calls;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource BothEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ImageProcessingCapability? GetCapability(ImageProcessingMethod method) =>
            method == ImageProcessingMethod.LightSharpen ? new("test-sharpen", "v1") : null;

        public async Task<OperationResult<AdapterOutput>> ProcessAsync(ImageProcessingRequest request, CancellationToken token)
        {
            Entered.TrySetResult();
            if (Interlocked.Increment(ref _calls) == 2)
                BothEntered.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(30));
            string input = harness.FileWorkspace.ResolveAbsolute(request.Input);
            string output = harness.FileWorkspace.ResolveAbsolute(request.ExpectedOutput);
            if (scenario is "invalid-png" or "renamed-failed")
                await File.WriteAllTextAsync(output, "not a png");
            else
                File.Copy(input, output);
            if (scenario == "tamper")
                await File.WriteAllTextAsync(input, "changed");

            return OperationResult.Ok(new AdapterOutput(
                scenario == "wrong-path" ? request.Input : request.ExpectedOutput, TimeSpan.Zero, null));
        }
    }
}
