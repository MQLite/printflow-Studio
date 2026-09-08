using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Automation;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Workflow.Services;

public sealed partial class SessionService
{
    /// <inheritdoc />
    public async Task<OperationResult<ErrorDetailsView>> LoadErrorDetailsAsync(
        SessionId sessionId, AttemptId attemptId, CancellationToken cancellationToken)
    {
        OperationResult<SessionAggregate?> loaded = await _repository.LoadAsync(sessionId, cancellationToken);
        if (loaded.IsFailure)
        {
            return OperationResult.Fail<ErrorDetailsView>(loaded.Failure);
        }

        if (loaded.Value is not { } aggregate)
        {
            return OperationResult.Fail<ErrorDetailsView>(
                FailureCode.PreconditionNotMet, $"No processing record {sessionId} exists.");
        }

        ProcessingAttempt? attempt = aggregate.Attempts.FirstOrDefault(candidate => candidate.Id == attemptId);
        if (attempt is null || attempt.Status is AttemptStatus.Running or AttemptStatus.Succeeded)
        {
            return OperationResult.Fail<ErrorDetailsView>(
                FailureCode.PreconditionNotMet,
                $"Attempt {attemptId} is not a failed, cancelled or interrupted attempt of this processing record.");
        }

        Workflow.Engine.WorkflowSnapshot snapshot = aggregate.ToSnapshot(ConfiguredRecommendations());
        ProcessingAttempt? current = ErrorDetailsSelection.Current(snapshot.CurrentStep, aggregate.Attempts);
        bool isCurrent = current?.Id == attempt.Id;
        IReadOnlyList<ErrorRecoveryAction> actions = isCurrent
            ? RecoveryActions(snapshot, attempt)
            : [];

        Revision? input = attempt.InputRevisionId is { } inputId
            ? aggregate.Revisions.FirstOrDefault(candidate => candidate.Id == inputId)
            : null;
        string? managedInputPath = input is null ? null : _workspace.ResolveAbsolute(input.File);
        ErrorPathStatus inputStatus = attempt.InputRevisionId is null
            ? ErrorPathStatus.NotEstablished
            : input is null
                ? ErrorPathStatus.Unavailable
                : ErrorPathStatus.Available;

        (string? expectedPath, ErrorPathStatus expectedStatus) = ExpectedOutputOf(attempt.Failure);

        AutomationLogEntry? correlatedLog = null;
        OperationResult<IReadOnlyList<AutomationLogEntry>> log =
            await _repository.LoadAutomationLogAsync(sessionId, cancellationToken);
        if (log.IsSuccess && attempt.Failure is { } attemptFailure)
        {
            AutomationLogEntry[] matches = log.Value.Where(candidate =>
                    candidate.SessionId == sessionId &&
                    candidate.Step == attempt.Step &&
                    candidate.Failure.Code == attemptFailure.Code &&
                    candidate.Failure.MessageKey == attemptFailure.MessageKey &&
                    FailureEvidence.AttemptIdOf(candidate.Failure) == attempt.Id)
                .ToArray();
            if (matches.Length == 1)
            {
                correlatedLog = matches[0];
            }
        }

        string? screenshotPath = correlatedLog?.ScreenshotPath;
        if (string.IsNullOrWhiteSpace(screenshotPath) &&
            attempt.Failure?.Context.TryGetValue(AutomationLogEntry.ScreenshotContextKey, out string? attemptPath) == true)
        {
            screenshotPath = string.IsNullOrWhiteSpace(attemptPath) ? null : attemptPath;
        }

        DiagnosticImageStatus screenshotStatus = DiagnosticImageStatus.NotCaptured;
        DecodedPreview? screenshot = null;
        if (screenshotPath is not null)
        {
            if (_diagnosticImages is null)
            {
                screenshotStatus = DiagnosticImageStatus.Unavailable;
            }
            else
            {
                OperationResult<DecodedPreview> decoded =
                    await _diagnosticImages.DecodeDiagnosticAsync(screenshotPath, cancellationToken);
                if (decoded.IsSuccess)
                {
                    screenshotStatus = DiagnosticImageStatus.Available;
                    screenshot = decoded.Value;
                }
                else
                {
                    screenshotStatus = DiagnosticImageStatus.Unavailable;
                }
            }
        }

        return OperationResult.Ok(new ErrorDetailsView(
            sessionId,
            attempt.Id,
            aggregate.Session.WorkflowType,
            attempt.Step,
            attempt.Status,
            attempt.Failure?.Code.ToString(),
            attempt.Failure?.MessageKey,
            attempt.Failure?.TechnicalDetail,
            managedInputPath,
            inputStatus,
            expectedPath,
            expectedStatus,
            screenshotPath,
            screenshotStatus,
            screenshot,
            checked(attempt.RetrySequence + 1),
            attempt.RetrySequence,
            isCurrent,
            actions));
    }

    /// <inheritdoc />
    public async Task<OperationResult<SessionView>> ResolveErrorRecoveryAsync(
        SessionId sessionId,
        AttemptId attemptId,
        ErrorRecoveryAction action,
        string? operatorName,
        CancellationToken cancellationToken)
    {
        OperationResult<ErrorDetailsView> details =
            await LoadErrorDetailsAsync(sessionId, attemptId, cancellationToken);
        if (details.IsFailure)
        {
            return OperationResult.Fail<SessionView>(details.Failure);
        }

        if (!details.Value.AvailableActions.Contains(action))
        {
            return OperationResult.Fail<SessionView>(
                FailureCode.PreconditionNotMet,
                "This recovery action is no longer available for the error that was opened.");
        }

        WorkflowCommand command = action switch
        {
            ErrorRecoveryAction.Retry => new WorkflowCommand.Retry(details.Value.Step),
            ErrorRecoveryAction.ManualProcessing => new WorkflowCommand.HandOff(
                details.Value.Step, "Handed off to the operator from Error Details."),
            ErrorRecoveryAction.ReenterAutomation => new WorkflowCommand.ReenterAutomation(),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
        };

        return await ExecuteCoreAsync(
            sessionId, command, operatorName, expectedFailureAttemptId: attemptId, cancellationToken);
    }

    private IReadOnlyList<ErrorRecoveryAction> RecoveryActions(
        Workflow.Engine.WorkflowSnapshot snapshot, ProcessingAttempt attempt)
    {
        IReadOnlyList<Workflow.Engine.CommandKind> commands = _engine.AvailableCommands(snapshot);
        List<ErrorRecoveryAction> actions = [];

        if (commands.Contains(Workflow.Engine.CommandKind.Retry) && attempt.Failure?.Code is not
            (FailureCode.PdfMultiplePages or FailureCode.PdfUnreadable or FailureCode.PdfEncrypted))
        {
            actions.Add(ErrorRecoveryAction.Retry);
        }

        if (commands.Contains(Workflow.Engine.CommandKind.HandOff))
        {
            actions.Add(ErrorRecoveryAction.ManualProcessing);
        }

        if (commands.Contains(Workflow.Engine.CommandKind.ReenterAutomation))
        {
            actions.Add(ErrorRecoveryAction.ReenterAutomation);
        }

        return actions;
    }

    private static (string? Path, ErrorPathStatus Status) ExpectedOutputOf(OperationFailure? failure)
    {
        if (failure is null ||
            !failure.Context.TryGetValue(FailureEvidence.ExpectedOutputEstablishedKey, out string? established))
        {
            return (null, ErrorPathStatus.NotRecorded);
        }

        if (!string.Equals(established, "true", StringComparison.OrdinalIgnoreCase))
        {
            return (null, ErrorPathStatus.NotEstablished);
        }

        return failure.Context.TryGetValue(FailureEvidence.ExpectedOutputPathKey, out string? path) &&
               !string.IsNullOrWhiteSpace(path)
            ? (path, ErrorPathStatus.Available)
            : (null, ErrorPathStatus.Unavailable);
    }
}
