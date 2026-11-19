using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Workflow.Engine;

namespace PrintFlow.Workflow.Services;

/// <summary>Closed manual result scope for SCRUM-11092 and SCRUM-11112.</summary>
public static class ManualResultEligibility
{
    public static bool Supports(StepKind step) =>
        step is StepKind.Enhancement or StepKind.BackgroundRemoval;

    // A missing model can block local cutout before an automatic attempt exists.
    internal static bool CanHandOffWaitingCutout(WorkflowSnapshot state) =>
        state.CurrentStep is { Step: StepKind.BackgroundRemoval, State: StepState.Waiting } &&
        state.ProcessingMethodFor(StepKind.BackgroundRemoval) == ImageProcessingMethod.BiRefNet &&
        state.UpstreamRevisionOf(StepKind.BackgroundRemoval) is not null;

    internal static bool IsManualResultPosition(WorkflowSnapshot state) =>
        state.CurrentStep is { State: StepState.Failed or StepState.Interrupted or StepState.RetryRequired } ||
        CanHandOffWaitingCutout(state);

    public static bool CanSubmit(WorkflowSnapshot state) =>
        state.SessionState == SessionState.HandedOff &&
        state.CurrentStep is { } step && Supports(step.Step) &&
        state.ProcessingMethodFor(step.Step) != ImageProcessingMethod.OpenAiCombined &&
        IsManualResultPosition(state) && state.UpstreamRevisionOf(step.Step) is not null;
}
