using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Workflow.Services;

public enum ErrorPathStatus
{
    Available,
    NotEstablished,
    NotRecorded,
    Unavailable,
}

public enum DiagnosticImageStatus
{
    NotCaptured,
    Available,
    Unavailable,
}

public enum ErrorRecoveryAction
{
    Retry,
    ManualProcessing,
    ReenterAutomation,
}

/// <summary>Everything the operator may know or do about one exact terminal attempt.</summary>
public sealed record ErrorDetailsView(
    SessionId SessionId,
    AttemptId AttemptId,
    WorkflowType Workflow,
    StepKind Step,
    AttemptStatus AttemptStatus,
    string? StableCode,
    string? MessageKey,
    string? TechnicalDetail,
    string? ManagedInputPath,
    ErrorPathStatus InputPathStatus,
    string? ExpectedOutputPath,
    ErrorPathStatus ExpectedOutputPathStatus,
    string? ScreenshotPath,
    DiagnosticImageStatus ScreenshotStatus,
    DecodedPreview? Screenshot,
    int AttemptNumber,
    int PreviousRetries,
    bool IsCurrent,
    IReadOnlyList<ErrorRecoveryAction> AvailableActions);

/// <summary>Selects the one terminal attempt the current failed/interrupted step is about.</summary>
internal static class ErrorDetailsSelection
{
    public static ProcessingAttempt? Current(
        SessionStep? currentStep, IReadOnlyList<ProcessingAttempt> attempts)
    {
        if (currentStep is not { State: StepState.Failed or StepState.RetryRequired or StepState.Interrupted })
        {
            return null;
        }

        ProcessingAttempt? newest = attempts
            .Where(candidate => candidate.Step == currentStep.Step)
            .OrderByDescending(candidate => candidate.RetrySequence)
            .ThenByDescending(candidate => candidate.StartedAtUtc)
            .ThenByDescending(candidate => candidate.Id.ToString(), StringComparer.Ordinal)
            .FirstOrDefault();

        return newest is { Status: AttemptStatus.Failed or AttemptStatus.Cancelled or AttemptStatus.Interrupted }
            ? newest
            : null;
    }
}
