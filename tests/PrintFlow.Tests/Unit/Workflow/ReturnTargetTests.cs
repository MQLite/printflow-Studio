using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Definitions;
using PrintFlow.Workflow.Effects;
using PrintFlow.Workflow.Engine;

namespace PrintFlow.Tests.Unit.Workflow;

/// <summary>
/// <c>AvailableReturnTargets</c> offers exactly the steps <c>ReturnToStep</c> accepts
/// (Epic 11200 Part C3 §8).
/// </summary>
/// <remarks>
/// The reason this needs its own suite, rather than riding along with
/// <c>AvailableCommands</c>: <c>ReturnToStep</c> is the one command whose legality depends on
/// its payload, so it has no single probe and is deliberately absent from
/// <c>AvailableCommands</c> altogether. What replaces the missing probe is a list of real
/// targets, and the property that makes that list trustworthy is agreement in <b>both</b>
/// directions — every offered target is accepted, and every unoffered one is refused. Only
/// asserting the first direction would be satisfied by an empty list.
/// <para>
/// Exhaustive over workflow × step × step-state, because the interesting failures are at the
/// edges: a session on its first step has nothing behind it, and a finished session has no
/// current step at all.
/// </para>
/// </remarks>
public sealed class ReturnTargetTests
{
    private static readonly IWorkflowEngine Engine = WorkflowEngine.Instance;

    /// <summary>Every workflow × step × state, so no arrangement is left unprobed.</summary>
    public static TheoryData<WorkflowType, StepKind, StepState> EveryArrangement()
    {
        TheoryData<WorkflowType, StepKind, StepState> data = [];
        foreach (WorkflowType type in Enum.GetValues<WorkflowType>())
        {
            foreach (StepDefinition step in WorkflowCatalog.For(type).Steps)
            {
                foreach (StepState state in TransitionTable.AllStepStates)
                {
                    data.Add(type, step.Kind, state);
                }
            }
        }

        return data;
    }

    /// <summary>
    /// §8, first half: every target the UI would offer is one the real command accepts.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryArrangement))]
    public void Every_offered_target_is_accepted_by_the_real_command(
        WorkflowType type, StepKind step, StepState state)
    {
        WorkflowScenario scenario = Arrange(type, step, state);

        foreach (StepKind target in Engine.AvailableReturnTargets(scenario.State))
        {
            WorkflowScenario attempt = Arrange(type, step, state);
            WorkflowTransition transition = attempt.Apply(new WorkflowCommand.ReturnToStep(target));

            transition.IsAccepted.ShouldBeTrue(
                $"{type}/{step}/{state} offered {target}, but ReturnToStep({target}) was rejected: " +
                $"{transition.Rejection}");
        }
    }

    /// <summary>
    /// §8, second half: every target the UI would <i>not</i> offer is one the real command
    /// refuses.
    /// </summary>
    /// <remarks>
    /// The half that gives the first one its meaning. Without it, an implementation that
    /// offered nothing at all would pass — and an operator would simply never see the control.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryArrangement))]
    public void Every_target_not_offered_is_refused_by_the_real_command(
        WorkflowType type, StepKind step, StepState state)
    {
        WorkflowScenario scenario = Arrange(type, step, state);
        IReadOnlyList<StepKind> offered = Engine.AvailableReturnTargets(scenario.State);

        foreach (StepDefinition candidate in WorkflowCatalog.For(type).Steps)
        {
            if (offered.Contains(candidate.Kind))
            {
                continue;
            }

            WorkflowScenario attempt = Arrange(type, step, state);
            WorkflowTransition transition = attempt.Apply(new WorkflowCommand.ReturnToStep(candidate.Kind));

            transition.IsRejected.ShouldBeTrue(
                $"{type}/{step}/{state} did not offer {candidate.Kind}, yet ReturnToStep({candidate.Kind}) " +
                "was accepted — the selector would be hiding a legal destination.");
        }
    }

    /// <summary>A step that is not part of the workflow is never offered and never accepted.</summary>
    /// <remarks>
    /// The cross-workflow case the loop above cannot reach, since it only walks the steps the
    /// workflow has. GENERATE_PRINT_TIFF has no Trim, and asking to return to one must be a
    /// refusal rather than a no-op that appears to succeed.
    /// </remarks>
    [Fact]
    public void A_step_the_workflow_does_not_contain_is_neither_offered_nor_accepted()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.GeneratePrintTiff);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());

        WorkflowCatalog.GeneratePrintTiff.Contains(StepKind.Trim).ShouldBeFalse();
        Engine.AvailableReturnTargets(scenario.State).ShouldNotContain(StepKind.Trim);

        scenario.Apply(new WorkflowCommand.ReturnToStep(StepKind.Trim)).IsRejected.ShouldBeTrue();
    }

    /// <summary>Targets come back in workflow order, so a selector reads top to bottom.</summary>
    [Fact]
    public void Targets_are_returned_in_workflow_order()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());
        scenario.CompleteStep(StepKind.Enhancement);

        IReadOnlyList<StepKind> targets = Engine.AvailableReturnTargets(scenario.State);

        targets.ShouldBe([StepKind.Import, StepKind.OriginalConfirmation, StepKind.Enhancement]);
    }

    /// <summary>A session on its first step has nothing behind it (§4).</summary>
    [Fact]
    public void A_session_on_its_first_step_is_offered_nothing()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);

        scenario.State.CurrentStep!.Step.ShouldBe(StepKind.Import);
        Engine.AvailableReturnTargets(scenario.State).ShouldBeEmpty();
    }

    /// <summary>A session that can no longer progress is offered nothing (§4).</summary>
    /// <remarks>
    /// Completed, handed off and abandoned alike: returning is a way of continuing work, and
    /// none of the three is a session that is still being worked on. The empty list is what
    /// hides the control, so the screen never offers an action that would be refused.
    /// </remarks>
    [Theory]
    [InlineData(SessionState.Completed)]
    [InlineData(SessionState.HandedOff)]
    [InlineData(SessionState.Abandoned)]
    public void A_session_that_cannot_progress_is_offered_nothing(SessionState state)
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());
        scenario.WithState(snapshot => snapshot with { SessionState = state });

        Engine.AvailableReturnTargets(scenario.State).ShouldBeEmpty();
    }

    /// <summary>Asking for the targets changes nothing — it is a query, not a rehearsal.</summary>
    /// <remarks>
    /// Worth stating outright because the implementation genuinely applies the command to find
    /// out. It applies it to a copy and keeps only the verdict, and the engine is pure, so the
    /// caller's snapshot is untouched — but "we probe by really doing it" is exactly the shape
    /// of code that could stop being safe if the engine ever acquired a side effect.
    /// </remarks>
    [Fact]
    public void Asking_for_the_targets_leaves_the_snapshot_unchanged()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());
        scenario.CompleteStep(StepKind.Enhancement);

        WorkflowSnapshot before = scenario.State;
        Engine.AvailableReturnTargets(scenario.State).ShouldNotBeEmpty();

        scenario.State.ShouldBe(before);
    }

    /// <summary>Puts one step of one workflow into one state, leaving the rest untouched.</summary>
    private static WorkflowScenario Arrange(WorkflowType type, StepKind step, StepState state)
    {
        WorkflowScenario scenario = WorkflowScenario.For(type);
        RevisionId revision = scenario.NextRevision();
        scenario.ForceStep(step, state, revision, WorkflowScenario.HashOf(revision));
        return scenario;
    }
}
