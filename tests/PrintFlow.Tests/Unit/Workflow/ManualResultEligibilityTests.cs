using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;

namespace PrintFlow.Tests.Unit.Workflow;

/// <summary>SCRUM-11092 / SCRUM-11112: no eligibility inferred from a filename or ordinary failure.</summary>
public sealed class ManualResultEligibilityTests
{
    public static IEnumerable<object[]> States()
    {
        foreach (var step in Enum.GetValues<StepKind>())
        foreach (var state in Enum.GetValues<StepState>())
        foreach (var session in Enum.GetValues<SessionState>())
            yield return [step, state, session];
    }

    [Theory]
    [MemberData(nameof(States))]
    public void Only_explicit_handoff_of_supported_step_can_submit(StepKind step, StepState state, SessionState session)
    {
        var scenario = WorkflowScenario.For(WorkflowType.PrepareCustomerDesign);
        var root = scenario.CompleteImport();
        scenario.WithState(s => s with
        {
            SessionState = session,
            Steps = s.Steps.Select(entry => entry with
            {
                State = entry.Step == step ? state : entry.Step == StepKind.Import ? StepState.Approved : StepState.Skipped,
            }).ToArray(),
        });
        bool expected = session == SessionState.HandedOff &&
            step is StepKind.Enhancement or StepKind.BackgroundRemoval &&
            state is StepState.Failed or StepState.Interrupted or StepState.RetryRequired;
        var commands = WorkflowEngine.Instance.AvailableCommands(scenario.State);
        commands.Contains(CommandKind.SubmitManualResult).ShouldBe(expected);
        scenario.Apply(new WorkflowCommand.SubmitManualResult(step, "C:/result.png")).IsAccepted.ShouldBe(expected);
    }
}
