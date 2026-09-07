using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Definitions;
using PrintFlow.Workflow.Effects;
using PrintFlow.Workflow.Engine;

namespace PrintFlow.Tests.Unit.Workflow;

public sealed class KeepOriginalExtentTests
{
    public static TheoryData<WorkflowType, StepKind, StepState> States()
    {
        TheoryData<WorkflowType, StepKind, StepState> data = [];
        foreach (WorkflowDefinition workflow in WorkflowCatalog.All)
            foreach (StepDefinition step in workflow.Steps)
                foreach (StepState state in Enum.GetValues<StepState>())
                    data.Add(workflow.Type, step.Kind, state);
        return data;
    }

    [Theory]
    [MemberData(nameof(States))]
    public void Exact_legality_and_effects_across_all_workflows_steps_and_states(WorkflowType type, StepKind kind, StepState state)
    {
        WorkflowSnapshot snapshot = WorkflowSnapshot.Create(SessionId.From(Guid.NewGuid()), type,
            OutputName.Parse("trim-choice"), DateTimeOffset.UnixEpoch);
        int ordinal = snapshot.Definition.IndexOf(kind);
        snapshot = snapshot with
        {
            Steps = snapshot.Steps.Select(step => step with
            {
                State = step.Ordinal < ordinal ? StepState.Approved : step.Ordinal == ordinal ? state : StepState.Waiting,
                CurrentRevisionId = step.Ordinal <= ordinal && !(step.Ordinal == ordinal && state == StepState.Skipped) ? RevisionId.From(Guid.NewGuid()) : null,
                CurrentRevisionSha256 = step.Ordinal <= ordinal && !(step.Ordinal == ordinal && state == StepState.Skipped) ? Sha256.Parse(new string('A', 64)) : null,
            }).ToArray(),
        };
        bool legal = snapshot.CurrentStep is { Step: StepKind.Trim, State: StepState.Waiting or StepState.ReviewRequired
            or StepState.Failed or StepState.RetryRequired or StepState.Interrupted };
        WorkflowTransition result = WorkflowEngine.Instance.Apply(snapshot, new WorkflowCommand.KeepOriginalExtent(),
            new CommandContext(DateTimeOffset.UnixEpoch, "tester", default, default));
        result.IsAccepted.ShouldBe(legal);
        WorkflowEngine.Instance.AvailableCommands(snapshot).Contains(CommandKind.KeepOriginalExtent).ShouldBe(legal);
        if (!legal) { result.Effects.ShouldBeEmpty(); return; }
        result.State.Step(StepKind.Trim)!.State.ShouldBe(StepState.Skipped);
        result.State.Step(StepKind.Trim)!.CurrentRevisionId.ShouldBeNull();
        result.State.Step(StepKind.Trim)!.SkipReason.ShouldBe(WorkflowCommand.KeepOriginalExtent.Reason);
        StepKind next = type == WorkflowType.PrepareAsset ? StepKind.ApprovedPngExport : StepKind.PrintDimensions;
        result.State.CurrentStep!.Step.ShouldBe(next);
        result.State.UpstreamRevisionOf(next).ShouldBe(snapshot.UpstreamRevisionOf(StepKind.Trim));
        result.Effects.Count.ShouldBe(1);
        result.Effects.Single().ShouldBeOfType<WorkflowEffect.RecordSkip>();
    }

    [Theory]
    [InlineData(SessionState.Active, false)]
    [InlineData(SessionState.Completed, true)]
    [InlineData(SessionState.HandedOff, true)]
    [InlineData(SessionState.Abandoned, true)]
    public void Missing_upstream_or_inactive_session_is_refused(SessionState sessionState, bool hasUpstream)
    {
        WorkflowSnapshot snapshot = WorkflowSnapshot.Create(SessionId.From(Guid.NewGuid()), WorkflowType.PrepareAsset,
            OutputName.Parse("trim-choice"), DateTimeOffset.UnixEpoch);
        snapshot = snapshot with
        {
            SessionState = sessionState,
            Steps = snapshot.Steps.Select(step => step.Ordinal < 4 ? step with
            {
                State = StepState.Approved,
                CurrentRevisionId = hasUpstream ? RevisionId.From(Guid.NewGuid()) : null,
                CurrentRevisionSha256 = hasUpstream ? Sha256.Parse(new string('A', 64)) : null,
            } : step).ToArray(),
        };
        WorkflowEngine.Instance.Apply(snapshot, new WorkflowCommand.KeepOriginalExtent(),
            new CommandContext(DateTimeOffset.UnixEpoch, "tester", default, default)).IsAccepted.ShouldBeFalse();
    }
}
