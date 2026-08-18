using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Effects;
using PrintFlow.Workflow.Engine;

namespace PrintFlow.Tests.Unit.Workflow;

/// <summary>
/// The engine must be a function of its inputs. If these fail, every other workflow test
/// becomes unreliable, because a transition could then depend on something the test cannot see.
/// </summary>
public sealed class EnginePurityTests
{
    private static readonly CommandContext FixedContext = new(
        new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero),
        "test-operator",
        ReviewId.From(Guid.Parse("00000000-0000-7000-8000-000000000001")),
        AttemptId.From(Guid.Parse("00000000-0000-7000-8000-000000000002")));

    [Fact]
    public void The_same_state_and_command_always_produce_the_same_result()
    {
        WorkflowSnapshot state = ImportedAsset();
        WorkflowCommand command = new WorkflowCommand.ConfirmOriginal("same notes");

        WorkflowTransition first = WorkflowEngine.Instance.Apply(state, command, FixedContext);
        WorkflowTransition second = WorkflowEngine.Instance.Apply(state, command, FixedContext);

        first.IsAccepted.ShouldBeTrue();
        second.State.ShouldBe(first.State);
        second.Effects.Count.ShouldBe(first.Effects.Count);
    }

    [Fact]
    public void Applying_a_command_never_mutates_the_state_passed_in()
    {
        WorkflowSnapshot state = ImportedAsset();
        WorkflowSnapshot copy = state with { };

        WorkflowEngine.Instance.Apply(state, new WorkflowCommand.ConfirmOriginal(), FixedContext);

        state.ShouldBe(copy);
        state.Step(StepKind.OriginalConfirmation)!.State.ShouldBe(StepState.Waiting);
    }

    [Fact]
    public void Timestamps_come_from_the_context_rather_than_from_the_clock()
    {
        WorkflowSnapshot state = ImportedAsset();

        WorkflowTransition transition =
            WorkflowEngine.Instance.Apply(state, new WorkflowCommand.ConfirmOriginal(), FixedContext);

        transition.State.Step(StepKind.OriginalConfirmation)!.EnteredStateAtUtc
            .ShouldBe(FixedContext.NowUtc);
    }

    [Fact]
    public void Identifiers_come_from_the_context_rather_than_being_generated()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());

        WorkflowSnapshot state = scenario.State;
        WorkflowTransition transition = WorkflowEngine.Instance.Apply(
            state, new WorkflowCommand.StartStep(StepKind.Enhancement), FixedContext);

        transition.Effect<WorkflowEffect.RecordAttemptStarted>().AttemptId
            .ShouldBe(FixedContext.NewAttemptId);
        transition.Effect<WorkflowEffect.RunAdapter>().AttemptId
            .ShouldBe(FixedContext.NewAttemptId);
    }

    [Fact]
    public void Every_accepted_transition_returns_its_work_as_data_rather_than_performing_it()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());

        scenario.Apply(new WorkflowCommand.StartStep(StepKind.Enhancement));

        // The adapter call is described, never made: it is a record in the effect list.
        WorkflowEffect.RunAdapter run = scenario.Effect<WorkflowEffect.RunAdapter>();
        run.ShouldBeOfType<WorkflowEffect.RunAdapter>();
        run.Step.ShouldBe(StepKind.Enhancement);
    }

    [Fact]
    public void AvailableCommands_agrees_with_what_the_engine_actually_accepts()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());

        IReadOnlyList<CommandKind> available = WorkflowEngine.Instance.AvailableCommands(scenario.State);

        available.ShouldContain(CommandKind.StartStep);
        available.ShouldContain(CommandKind.Skip);
        available.ShouldNotContain(CommandKind.Approve);
        available.ShouldNotContain(CommandKind.Complete);
    }

    [Fact]
    public void AvailableCommands_is_empty_of_progression_once_the_session_ends()
    {
        WorkflowScenario scenario = RecoveryAndBranchTests.CompletedTiffSession();

        IReadOnlyList<CommandKind> available = WorkflowEngine.Instance.AvailableCommands(scenario.State);

        available.ShouldNotContain(CommandKind.StartStep);
        available.ShouldNotContain(CommandKind.Approve);
        available.ShouldNotContain(CommandKind.Complete);
        available.ShouldContain(CommandKind.AddAnotherSize);
    }

    private static WorkflowSnapshot ImportedAsset()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        return scenario.State;
    }
}

internal static class TransitionEffectExtensions
{
    internal static T Effect<T>(this WorkflowTransition transition) where T : WorkflowEffect
    {
        foreach (WorkflowEffect effect in transition.Effects)
        {
            if (effect is T typed)
            {
                return typed;
            }
        }

        throw new InvalidOperationException($"No {typeof(T).Name} effect was produced.");
    }
}
