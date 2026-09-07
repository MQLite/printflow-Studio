using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Definitions;
using PrintFlow.Workflow.Effects;
using PrintFlow.Workflow.Engine;

namespace PrintFlow.Tests.Unit.Workflow;

/// <summary>
/// Exhaustive coverage of <c>StepState × CommandKind</c> across all three workflows.
/// </summary>
/// <remarks>
/// The point is not that particular pairs are legal — those are asserted individually
/// elsewhere. The point is that <b>every</b> pair has an explicit, deliberate outcome, so a
/// combination can never fall through silently (Epic 11100 plan §8.1, §17.1).
/// </remarks>
public sealed class TransitionMatrixTests
{
    public static TheoryData<StepState, CommandKind> EveryPair()
    {
        TheoryData<StepState, CommandKind> data = [];
        foreach (StepState state in TransitionTable.AllStepStates)
        {
            foreach (CommandKind command in TransitionTable.AllCommands)
            {
                data.Add(state, command);
            }
        }

        return data;
    }

    /// <summary>Every pair resolves to one of the three declared outcomes — never a default.</summary>
    [Theory]
    [MemberData(nameof(EveryPair))]
    public void Every_state_command_pair_has_an_explicit_outcome(StepState state, CommandKind command)
    {
        TransitionOutcome outcome = TransitionTable.Lookup(state, command);

        outcome.ShouldBeOneOf(
            TransitionOutcome.Allowed,
            TransitionOutcome.Rejected,
            TransitionOutcome.SessionScoped);
    }

    /// <summary>Every step-scoped command that the table allows also has a declared destination.</summary>
    [Theory]
    [MemberData(nameof(EveryPair))]
    public void Every_allowed_pair_declares_a_destination_state(StepState state, CommandKind command)
    {
        if (TransitionTable.Lookup(state, command) != TransitionOutcome.Allowed)
        {
            return;
        }

        StepDefinition reviewed = WorkflowCatalog.PrepareAsset.Find(StepKind.Enhancement)!;
        StepDefinition unreviewed = WorkflowCatalog.PrepareAsset.Find(StepKind.ApprovedPngExport)!;

        TransitionTable.Destination(command, reviewed).ShouldBeOneOf(TransitionTable.AllStepStates.ToArray());
        TransitionTable.Destination(command, unreviewed).ShouldBeOneOf(TransitionTable.AllStepStates.ToArray());
    }

    /// <summary>
    /// The engine itself never falls through: for every workflow, every step, every state and
    /// every command, the result is either an accepted transition or a coded rejection.
    /// </summary>
    public static TheoryData<WorkflowType, StepKind, StepState, CommandKind> EveryEnginePair()
    {
        TheoryData<WorkflowType, StepKind, StepState, CommandKind> data = [];
        foreach (WorkflowType type in Enum.GetValues<WorkflowType>())
        {
            foreach (StepDefinition step in WorkflowCatalog.For(type).Steps)
            {
                foreach (StepState state in TransitionTable.AllStepStates)
                {
                    foreach (CommandKind command in TransitionTable.AllCommands)
                    {
                        data.Add(type, step.Kind, state, command);
                    }
                }
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryEnginePair))]
    public void Engine_returns_an_explicit_result_for_every_combination(
        WorkflowType type, StepKind step, StepState state, CommandKind command)
    {
        WorkflowScenario scenario = WorkflowScenario.For(type);

        // Put the target step in the requested state, with a result attached so that
        // review-bearing commands have something to act on.
        RevisionId revision = scenario.NextRevision();
        scenario.ForceStep(step, state, revision, WorkflowScenario.HashOf(revision));

        WorkflowTransition transition = scenario.Apply(BuildCommand(command, step, revision));

        if (transition.IsAccepted)
        {
            transition.NewState.ShouldNotBeNull();
            transition.Rejection.ShouldBeNull();
        }
        else
        {
            transition.NewState.ShouldBeNull();
            transition.Rejection.ShouldNotBeNull();
            transition.Effects.ShouldBeEmpty("a rejected command must produce no effects");
            transition.Rejection!.DebugMessage.ShouldNotBeNullOrWhiteSpace();
        }
    }

    /// <summary>A rejected command must leave the caller's state untouched.</summary>
    [Theory]
    [MemberData(nameof(EveryEnginePair))]
    public void Rejection_never_mutates_the_input_state(
        WorkflowType type, StepKind step, StepState state, CommandKind command)
    {
        WorkflowScenario scenario = WorkflowScenario.For(type);
        RevisionId revision = scenario.NextRevision();
        scenario.ForceStep(step, state, revision, WorkflowScenario.HashOf(revision));

        WorkflowSnapshot before = scenario.State;
        WorkflowTransition transition = scenario.Apply(BuildCommand(command, step, revision));

        if (transition.IsRejected)
        {
            scenario.State.ShouldBe(before);
        }
    }

    /// <summary>A step in a finished state accepts no step-scoped command.</summary>
    [Theory]
    [InlineData(StepState.Approved)]
    [InlineData(StepState.Skipped)]
    public void Finished_states_accept_no_step_scoped_command(StepState finished)
    {
        foreach (CommandKind command in TransitionTable.AllCommands)
        {
            if (TransitionTable.IsSessionScoped(command))
            {
                continue;
            }

            TransitionTable.Lookup(finished, command).ShouldBe(
                TransitionOutcome.Rejected,
                $"{command} must not be legal for a {finished} step");
        }
    }

    /// <summary>
    /// A running attempt is concluded only by the system, never by the operator.
    /// </summary>
    /// <remarks>
    /// <see cref="CommandKind.AttemptCancelled"/> joined this list in Epic 11300 Part D2A and
    /// does not weaken it. An operator Stop is not this command: the operator asks the run to
    /// stop, and the application layer raises this once the run has actually unwound and
    /// reported what it left behind — exactly as it raises <c>AttemptFailed</c> when an adapter
    /// fails. Its constructor is internal to <c>PrintFlow.Workflow</c> like every other system
    /// command, so no view model can close an attempt by constructing one (§12).
    /// </remarks>
    [Fact]
    public void Processing_accepts_only_system_commands()
    {
        CommandKind[] allowed = TransitionTable.AllCommands
            .Where(c => TransitionTable.Lookup(StepState.Processing, c) == TransitionOutcome.Allowed)
            .ToArray();

        allowed.ShouldBe(
        [
            CommandKind.AttemptSucceeded,
            CommandKind.AttemptFailed,
            CommandKind.AttemptInterrupted,
            CommandKind.AttemptCancelled,
        ], ignoreOrder: true);
    }

    private static WorkflowCommand BuildCommand(CommandKind kind, StepKind step, RevisionId revision)
    {
        Sha256 hash = WorkflowScenario.HashOf(revision);

        return kind switch
        {
            CommandKind.SelectWorkflow => new WorkflowCommand.SelectWorkflow(WorkflowType.PrepareAsset),
            CommandKind.SetOutputName => new WorkflowCommand.SetOutputName(OutputName.Parse("renamed")),
            CommandKind.ConfirmOriginal => new WorkflowCommand.ConfirmOriginal(),
            CommandKind.StartStep => new WorkflowCommand.StartStep(step),
            CommandKind.Approve => new WorkflowCommand.Approve(step, hash),
            CommandKind.Reject => new WorkflowCommand.Reject(step, hash, RejectionReason.Other),
            CommandKind.Retry => new WorkflowCommand.Retry(step),
            CommandKind.SubmitManualCrop => new WorkflowCommand.SubmitManualCrop(
                step, global::PrintFlow.Domain.Trimming.TrimBounds.Canvas(4, 4)),
            CommandKind.Skip => new WorkflowCommand.Skip(step),
            CommandKind.KeepOriginalExtent => new WorkflowCommand.KeepOriginalExtent(),
            CommandKind.HandOff => new WorkflowCommand.HandOff(step, "matrix probe"),
            CommandKind.SetPrintDimensions =>
                new WorkflowCommand.SetPrintDimensions(WorkflowScenario.CustomBox),

            // The three flexible-size commands probe with structurally valid payloads for the same
            // reason every other row here does: the matrix asserts that each combination has an
            // explicit outcome, so the payload must never be what decides the answer. Their real
            // millimetres are resolved by SessionService, which no matrix probe reaches
            // (Epic 11400 Part B1A.2D §15).
            CommandKind.SetPresetFitSize => new WorkflowCommand.SetPresetFitSize(
                global::PrintFlow.Domain.Outputs.SizePreset.A4),
            CommandKind.SetCustomTargetEdgeSize => new WorkflowCommand.SetCustomTargetEdgeSize(
                global::PrintFlow.Domain.Outputs.TargetEdge.LongEdge, 320m),

            // Probed with the revision the matrix forced onto the step, exactly as the
            // background-removal decision beside it is. A confirmation naming content no plan was
            // calculated from must still be refused rather than fall through (§10).
            CommandKind.AuthoriseEnlargement => new WorkflowCommand.AuthoriseEnlargement(
                revision,
                hash,
                global::PrintFlow.Domain.Outputs.TargetEdge.LongEdge,
                320m),
            CommandKind.SelectWhiteUnderbaseBranch => new WorkflowCommand.SelectWhiteUnderbaseBranch(
                global::PrintFlow.Domain.Outputs.WhiteUnderbaseBranch.W1_1px, "matrix probe"),
            CommandKind.SetTrimParameters => new WorkflowCommand.SetTrimParameters(
                global::PrintFlow.Domain.Trimming.TrimMargin.Uniform(2)),

            // The matrix probes with the revision it forced onto the step under test, which is
            // usually not the one BackgroundRemoval would consume. That is the point here: the
            // matrix asserts only that every combination has an explicit outcome, and a payload
            // naming unrelated content must still be refused rather than fall through
            // (Epic 11300 Part C2B1 §8).
            CommandKind.SetBackgroundRemovalDecision => new WorkflowCommand.SetBackgroundRemovalDecision(
                global::PrintFlow.Domain.Sessions.BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
                revision,
                hash),
            CommandKind.ReturnToStep => new WorkflowCommand.ReturnToStep(step),
            CommandKind.Complete => new WorkflowCommand.Complete(),
            CommandKind.AddAnotherSize => new WorkflowCommand.AddAnotherSize(),
            CommandKind.AbandonSession => new WorkflowCommand.AbandonSession("matrix probe"),
            CommandKind.AttemptSucceeded =>
                SystemCommands.Succeeded(AttemptId.From(Guid.CreateVersion7()), step, revision, hash),
            CommandKind.AttemptFailed => SystemCommands.Failed(
                AttemptId.From(Guid.CreateVersion7()), step, global::PrintFlow.Domain.Results.FailureCode.Timeout),
            CommandKind.AttemptInterrupted =>
                SystemCommands.Interrupted(AttemptId.From(Guid.CreateVersion7()), step),
            CommandKind.AttemptCancelled =>
                SystemCommands.Cancelled(AttemptId.From(Guid.CreateVersion7()), step),
            CommandKind.ReenterAutomation => new WorkflowCommand.ReenterAutomation(),
            CommandKind.SubmitManualResult => new WorkflowCommand.SubmitManualResult(step, @"C:\manual-result.png"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unhandled command kind."),
        };
    }
}
