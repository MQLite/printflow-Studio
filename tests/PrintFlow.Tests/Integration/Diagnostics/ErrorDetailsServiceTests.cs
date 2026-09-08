using System.IO;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Automation;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Diagnostics;

/// <summary>SCRUM-11120's exact-attempt diagnostic and recovery boundary.</summary>
[Collection(SqliteCollection.Name)]
public sealed class ErrorDetailsServiceTests
{
    [Fact]
    public async Task Failed_attempt_resolves_its_authoritative_paths_log_and_renderable_evidence()
    {
        using SessionServiceHarness harness = new();
        string screenshot = harness.Workspace.CreateSourceFile(
            "failure-evidence.png", SyntheticImages.Png(7, 4, alpha: true));
        FailingMeitu meitu = new(FailureCode.MeituTargetLost, screenshot);
        ISessionService service = harness.CreateServiceWithMeitu(meitu);
        SessionId id = await ReadyForEnhancementAsync(harness, service, "details.png");

        (await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Enhancement),
            "tester", CancellationToken.None)).IsFailure.ShouldBeTrue();

        SessionView current = Accept(await service.LoadAsync(id, CancellationToken.None));
        AttemptId attemptId = current.CurrentFailureAttemptId.ShouldNotBeNull();
        ErrorDetailsView details = Accept(await service.LoadErrorDetailsAsync(
            id, attemptId, CancellationToken.None));
        SessionAggregate aggregate = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        ProcessingAttempt attempt = aggregate.Attempts.Single(candidate => candidate.Id == attemptId);
        Revision input = aggregate.Revisions.Single(candidate => candidate.Id == attempt.InputRevisionId);
        AutomationLogEntry log = (await harness.Repository.LoadAutomationLogAsync(id, CancellationToken.None))
            .Value.ShouldHaveSingleItem();

        details.AttemptId.ShouldBe(attemptId);
        details.Workflow.ShouldBe(WorkflowType.PrepareAsset);
        details.Step.ShouldBe(StepKind.Enhancement);
        details.StableCode.ShouldBe(nameof(FailureCode.MeituTargetLost));
        details.MessageKey.ShouldBe("Failure_MeituTargetLost");
        details.TechnicalDetail.ShouldBe("The deterministic Meitu failure used by Error Details.");
        details.ManagedInputPath.ShouldBe(harness.FileWorkspace.ResolveAbsolute(input.File));
        details.InputPathStatus.ShouldBe(ErrorPathStatus.Available);
        details.ExpectedOutputPathStatus.ShouldBe(ErrorPathStatus.Available);
        Path.IsPathFullyQualified(details.ExpectedOutputPath!).ShouldBeTrue();
        details.ExpectedOutputPath.ShouldNotBeNull().ShouldContain(attemptId.ToString());
        details.ScreenshotPath.ShouldBe(screenshot);
        details.ScreenshotStatus.ShouldBe(DiagnosticImageStatus.Available);
        details.Screenshot.ShouldNotBeNull().Payload.IsEmpty.ShouldBeFalse();
        details.AttemptNumber.ShouldBe(1);
        details.PreviousRetries.ShouldBe(0);
        details.IsCurrent.ShouldBeTrue();
        details.AvailableActions.ShouldContain(ErrorRecoveryAction.Retry);
        details.AvailableActions.ShouldContain(ErrorRecoveryAction.ManualProcessing);
        FailureEvidence.AttemptIdOf(attempt.Failure!).ShouldBe(attemptId);
        FailureEvidence.AttemptIdOf(log.Failure).ShouldBe(attemptId);
        log.ScreenshotPath.ShouldBe(screenshot);
    }

    [Fact]
    public async Task Historical_failure_keeps_its_own_log_but_loses_recovery_authority()
    {
        using SessionServiceHarness harness = new();
        string firstShot = harness.Workspace.CreateSourceFile(
            "first-failure.png", SyntheticImages.Png(3, 2));
        string secondShot = harness.Workspace.CreateSourceFile(
            "second-failure.png", SyntheticImages.Png(4, 3));
        FailingMeitu meitu = new(FailureCode.MeituTargetLost, firstShot);
        ISessionService service = harness.CreateServiceWithMeitu(meitu);
        SessionId id = await ReadyForEnhancementAsync(harness, service, "history.png");

        (await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Enhancement),
            "tester", CancellationToken.None)).IsFailure.ShouldBeTrue();
        AttemptId firstAttempt = Accept(await service.LoadAsync(id, CancellationToken.None))
            .CurrentFailureAttemptId.ShouldNotBeNull();
        int attemptsBeforeRetry = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!.Attempts.Count;

        SessionView waiting = Accept(await service.ResolveErrorRecoveryAsync(
            id, firstAttempt, ErrorRecoveryAction.Retry, "tester", CancellationToken.None));
        waiting.CurrentStep!.State.ShouldBe(StepState.Waiting);
        (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!.Attempts.Count
            .ShouldBe(attemptsBeforeRetry, "Retry resets the step but does not run an application or create an attempt");
        meitu.CallCount.ShouldBe(1);

        ErrorDetailsView stale = Accept(await service.LoadErrorDetailsAsync(
            id, firstAttempt, CancellationToken.None));
        stale.IsCurrent.ShouldBeFalse();
        stale.AvailableActions.ShouldBeEmpty();
        (await service.ResolveErrorRecoveryAsync(id, firstAttempt, ErrorRecoveryAction.Retry,
            "tester", CancellationToken.None)).Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);

        meitu.ScreenshotPath = secondShot;
        (await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Enhancement),
            "tester", CancellationToken.None)).IsFailure.ShouldBeTrue();
        AttemptId secondAttempt = Accept(await service.LoadAsync(id, CancellationToken.None))
            .CurrentFailureAttemptId.ShouldNotBeNull();

        (await service.LoadErrorDetailsAsync(id, firstAttempt, CancellationToken.None)).Value
            .ScreenshotPath.ShouldBe(firstShot, "a later log row must not relabel the failure that was opened");
        (await service.LoadErrorDetailsAsync(id, secondAttempt, CancellationToken.None)).Value
            .ScreenshotPath.ShouldBe(secondShot);
    }

    [Fact]
    public async Task Crash_interruption_without_log_still_opens_truthful_details()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        SessionId id = await ReadyForEnhancementAsync(harness, service, "interrupted.png");
        harness.FakeMeitu.SetScenario(PrintFlow.Infrastructure.Adapters.Fake.FakeAdapterScenario.HangUntilCancelled);
        _ = service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Enhancement),
            "tester", CancellationToken.None);
        await harness.FakeMeitu.HangStarted;
        AttemptId attemptId = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!
            .Attempts.Single(candidate => candidate.Step == StepKind.Enhancement && candidate.Status == AttemptStatus.Running).Id;

        OperationResult<StartupRecoveryReport> recovered = await harness
            .CreateRecoveryService(new FakeProcessLiveness(ProcessLiveness.Dead))
            .RecoverAsync(CancellationToken.None);
        recovered.IsSuccess.ShouldBeTrue(recovered.IsFailure ? recovered.Failure.ToString() : string.Empty);
        (await harness.Repository.LoadAutomationLogAsync(id, CancellationToken.None)).Value.ShouldBeEmpty();

        ErrorDetailsView details = Accept(await harness.CreateService().LoadErrorDetailsAsync(
            id, attemptId, CancellationToken.None));
        details.AttemptStatus.ShouldBe(AttemptStatus.Interrupted);
        details.StableCode.ShouldBeNull();
        details.MessageKey.ShouldBeNull();
        details.TechnicalDetail.ShouldBeNull();
        details.ExpectedOutputPathStatus.ShouldBe(ErrorPathStatus.NotRecorded);
        details.ScreenshotStatus.ShouldBe(DiagnosticImageStatus.NotCaptured);
        details.IsCurrent.ShouldBeTrue();
        details.AvailableActions.ShouldContain(ErrorRecoveryAction.Retry);
    }

    [Fact]
    public async Task Missing_screenshot_is_nonfatal_and_manual_processing_uses_handoff()
    {
        using SessionServiceHarness harness = new();
        string missing = Path.Combine(harness.Workspace.Root, "already-removed.png");
        FailingMeitu meitu = new(FailureCode.MeituTargetLost, missing);
        ISessionService service = harness.CreateServiceWithMeitu(meitu);
        SessionId id = await ReadyForEnhancementAsync(harness, service, "manual.png");
        (await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Enhancement),
            "tester", CancellationToken.None)).IsFailure.ShouldBeTrue();
        AttemptId attemptId = Accept(await service.LoadAsync(id, CancellationToken.None))
            .CurrentFailureAttemptId.ShouldNotBeNull();

        ErrorDetailsView details = Accept(await service.LoadErrorDetailsAsync(
            id, attemptId, CancellationToken.None));
        details.ScreenshotPath.ShouldBe(missing);
        details.ScreenshotStatus.ShouldBe(DiagnosticImageStatus.Unavailable);
        details.Screenshot.ShouldBeNull();

        int attemptCount = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!.Attempts.Count;
        SessionView handedOff = Accept(await service.ResolveErrorRecoveryAsync(
            id, attemptId, ErrorRecoveryAction.ManualProcessing, "tester", CancellationToken.None));
        handedOff.State.ShouldBe(SessionState.HandedOff);
        handedOff.AvailableCommands.ShouldContain(PrintFlow.Workflow.Engine.CommandKind.SubmitManualResult);
        (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!.Attempts.Count.ShouldBe(attemptCount);
        meitu.CallCount.ShouldBe(1);
    }

    [Fact]
    public async Task Failure_before_an_output_destination_exists_says_not_established()
    {
        using SessionServiceHarness harness = new();
        string unreadable = Path.Combine(harness.Workspace.Root, "empty.png");
        File.WriteAllBytes(unreadable, []);
        ISessionService service = harness.CreateService();
        (await service.ImportAsync(WorkflowType.PrepareAsset, unreadable, "empty", "tester",
            CancellationToken.None)).IsFailure.ShouldBeTrue();
        SessionListItem session = (await harness.Repository.ListRecentAsync(
            10, harness.Clock.GetUtcNow().AddDays(-1), CancellationToken.None)).Value.Single();
        SessionAggregate aggregate = (await harness.Repository.LoadAsync(session.Id, CancellationToken.None)).Value!;
        AttemptId attemptId = aggregate.Attempts.Single().Id;

        ErrorDetailsView details = Accept(await service.LoadErrorDetailsAsync(
            session.Id, attemptId, CancellationToken.None));
        details.InputPathStatus.ShouldBe(ErrorPathStatus.NotEstablished);
        details.ExpectedOutputPathStatus.ShouldBe(ErrorPathStatus.NotEstablished);
        details.ManagedInputPath.ShouldBeNull();
        details.ExpectedOutputPath.ShouldBeNull();
    }

    [Fact]
    public async Task Exception_after_destination_construction_keeps_the_authoritative_expected_path()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateServiceWithMeitu(new ThrowingMeitu());
        SessionId id = await ReadyForEnhancementAsync(harness, service, "throwing.png");

        (await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Enhancement),
            "tester", CancellationToken.None)).IsFailure.ShouldBeTrue();

        SessionView failed = Accept(await service.LoadAsync(id, CancellationToken.None));
        ErrorDetailsView details = Accept(await service.LoadErrorDetailsAsync(
            id, failed.CurrentFailureAttemptId.ShouldNotBeNull(), CancellationToken.None));
        details.StableCode.ShouldBe(nameof(FailureCode.AdapterUnavailable));
        details.ExpectedOutputPathStatus.ShouldBe(ErrorPathStatus.Available);
        Path.IsPathFullyQualified(details.ExpectedOutputPath!).ShouldBeTrue();
    }

    [Fact]
    public async Task Operator_stop_preserves_adapter_screenshot_and_established_output_evidence()
    {
        using SessionServiceHarness harness = new();
        string screenshot = harness.Workspace.CreateSourceFile(
            "stopped-evidence.png", SyntheticImages.Png(5, 3));
        EvidenceStopMeitu meitu = new(screenshot);
        ISessionService service = harness.CreateServiceWithMeitu(meitu);
        SessionId id = await ReadyForEnhancementAsync(harness, service, "stopped.png");

        Task<OperationResult<SessionView>> run = service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);
        await meitu.Started;
        service.RequestStop(id, AutomationStopMode.StopOperation).IsSuccess.ShouldBeTrue();
        (await run).Failure.Code.ShouldBe(FailureCode.Cancelled);

        SessionView stopped = Accept(await service.LoadAsync(id, CancellationToken.None));
        ErrorDetailsView details = Accept(await service.LoadErrorDetailsAsync(
            id, stopped.CurrentFailureAttemptId.ShouldNotBeNull(), CancellationToken.None));
        details.ScreenshotPath.ShouldBe(screenshot);
        details.ScreenshotStatus.ShouldBe(DiagnosticImageStatus.Available);
        details.ExpectedOutputPathStatus.ShouldBe(ErrorPathStatus.Available);
        Path.IsPathFullyQualified(details.ExpectedOutputPath!).ShouldBeTrue();
    }

    [Fact]
    public async Task Recovery_rechecks_the_exact_attempt_on_the_command_aggregate()
    {
        using SessionServiceHarness harness = new();
        FailingMeitu meitu = new(FailureCode.MeituTargetLost, screenshotPath: null);
        ISessionService service = harness.CreateServiceWithMeitu(meitu);
        SessionId id = await ReadyForEnhancementAsync(harness, service, "race.png");

        (await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Enhancement),
            "tester", CancellationToken.None)).IsFailure.ShouldBeTrue();
        SessionAggregate firstState = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        AttemptId firstAttempt = firstState.Attempts.Single(a => a.Step == StepKind.Enhancement).Id;

        Accept(await service.ResolveErrorRecoveryAsync(
            id, firstAttempt, ErrorRecoveryAction.Retry, "tester", CancellationToken.None));
        (await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Enhancement),
            "tester", CancellationToken.None)).IsFailure.ShouldBeTrue();
        SessionAggregate secondState = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        AttemptId secondAttempt = secondState.Attempts
            .Single(a => a.Step == StepKind.Enhancement && a.Id != firstAttempt).Id;

        ISessionRepository switching = new FirstLoadOverrideRepository(harness.Repository, firstState);
        ISessionService stalePage = harness.CreateServiceWithMeitu(meitu, repository: switching);
        OperationResult<SessionView> refused = await stalePage.ResolveErrorRecoveryAsync(
            id, firstAttempt, ErrorRecoveryAction.Retry, "tester", CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        SessionAggregate unchanged = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        unchanged.Attempts.Count.ShouldBe(secondState.Attempts.Count);
        unchanged.Attempts.Single(a => a.Id == secondAttempt).Status.ShouldBe(AttemptStatus.Failed);
        unchanged.ToSnapshot().CurrentStep!.State.ShouldBe(StepState.Failed);
    }

    private static async Task<SessionId> ReadyForEnhancementAsync(
        SessionServiceHarness harness, ISessionService service, string fileName)
    {
        SessionId id = Accept(await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteSourcePng(fileName), "error-details", "tester",
            CancellationToken.None)).Id;
        Accept(await service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(),
            "tester", CancellationToken.None));
        return id;
    }

    private static T Accept<T>(OperationResult<T> result)
    {
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);
        return result.Value;
    }

    private sealed class FailingMeitu(FailureCode code, string? screenshotPath) : IMeituProcessor
    {
        public string AdapterId => "scrum-11120-failing-meitu";
        public AdapterExecutionMode Mode => AdapterExecutionMode.Fake;
        public int CallCount { get; private set; }
        public string? ScreenshotPath { get; set; } = screenshotPath;

        public Task<OperationResult<AdapterOutput>> ProcessAsync(
            MeituRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            IReadOnlyDictionary<string, string>? context = ScreenshotPath is null
                ? null
                : new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [AutomationLogEntry.ScreenshotContextKey] = ScreenshotPath,
                };
            return Task.FromResult(OperationResult.Fail<AdapterOutput>(OperationFailure.Create(
                code,
                "The deterministic Meitu failure used by Error Details.",
                isRetryable: true,
                context: context)));
        }
    }

    private sealed class ThrowingMeitu : IMeituProcessor
    {
        public string AdapterId => "scrum-11120-throwing-meitu";
        public AdapterExecutionMode Mode => AdapterExecutionMode.Fake;

        public Task<OperationResult<AdapterOutput>> ProcessAsync(
            MeituRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Deterministic exception after request construction.");
    }

    private sealed class EvidenceStopMeitu(string screenshotPath) : IMeituProcessor
    {
        private readonly TaskCompletionSource _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string AdapterId => "scrum-11120-stopped-meitu";
        public AdapterExecutionMode Mode => AdapterExecutionMode.Fake;
        public Task Started => _started.Task;

        public async Task<OperationResult<AdapterOutput>> ProcessAsync(
            MeituRequest request, CancellationToken cancellationToken)
        {
            request.Stop.ReportPhase(ExternalOperationPhase.NotStarted);
            _started.TrySetResult();
            while (request.Stop.RequestedMode is null)
                await Task.Delay(5, cancellationToken);

            return OperationResult.Fail<AdapterOutput>(OperationFailure.Create(
                FailureCode.Cancelled,
                "The deterministic adapter stopped with captured evidence.",
                isRetryable: true,
                context: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [AutomationLogEntry.ScreenshotContextKey] = screenshotPath,
                }));
        }
    }

    private sealed class FirstLoadOverrideRepository(
        ISessionRepository inner,
        SessionAggregate first) : ISessionRepository
    {
        private int _loadCount;

        public Task<OperationResult<IReadOnlyList<SessionId>>> FindRecoveryCandidatesAsync(
            CancellationToken cancellationToken) => inner.FindRecoveryCandidatesAsync(cancellationToken);

        public Task<OperationResult<SessionAggregate?>> LoadAsync(
            SessionId id, CancellationToken cancellationToken) =>
            Interlocked.Increment(ref _loadCount) == 1
                ? Task.FromResult(OperationResult.Ok<SessionAggregate?>(first))
                : inner.LoadAsync(id, cancellationToken);

        public Task<OperationResult<IReadOnlyList<SessionListItem>>> ListRecentAsync(
            int maxCount, DateTimeOffset since, CancellationToken cancellationToken) =>
            inner.ListRecentAsync(maxCount, since, cancellationToken);

        public Task<OperationResult<PrintFlow.Domain.Results.Unit>> CommitAsync(
            SessionMutation mutation, CancellationToken cancellationToken) =>
            inner.CommitAsync(mutation, cancellationToken);

        public Task<OperationResult<IReadOnlyList<ProcessingAttempt>>> FindRunningAttemptsAsync(
            CancellationToken cancellationToken) => inner.FindRunningAttemptsAsync(cancellationToken);

        public Task<OperationResult<IReadOnlyList<SessionId>>> FindCompletedSessionsAsync(
            CancellationToken cancellationToken) => inner.FindCompletedSessionsAsync(cancellationToken);

        public Task<OperationResult<AutomationLockState>> GetAutomationLockAsync(
            CancellationToken cancellationToken) => inner.GetAutomationLockAsync(cancellationToken);

        public Task<OperationResult<IReadOnlyList<AutomationLogEntry>>> LoadAutomationLogAsync(
            SessionId sessionId, CancellationToken cancellationToken) =>
            inner.LoadAutomationLogAsync(sessionId, cancellationToken);

        public Task<OperationResult<PrintFlow.Domain.Results.Unit>> ReleaseEnvironmentVerificationLockAsync(
            AutomationLockState observed, CancellationToken cancellationToken) =>
            inner.ReleaseEnvironmentVerificationLockAsync(observed, cancellationToken);
    }
}
