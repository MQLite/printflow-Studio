using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Engine;

namespace PrintFlow.Workflow.Services;

/// <summary>Closed manual result scope for SCRUM-11092 and SCRUM-11112.</summary>
public static class ManualResultEligibility
{
    public static bool Supports(StepKind step) =>
        step is StepKind.Enhancement or StepKind.BackgroundRemoval;

    public static bool CanSubmit(WorkflowSnapshot state) =>
        state.SessionState == SessionState.HandedOff &&
        state.CurrentStep is { State: StepState.Failed or StepState.Interrupted or StepState.RetryRequired } step &&
        Supports(step.Step) && state.UpstreamRevisionOf(step.Step) is not null;
}
