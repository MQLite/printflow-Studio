using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
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

    /// <summary>
    /// The review commands are probed with the step's real hash, so availability and the
    /// operator's actual click ask the same question (Epic 11100 Part 3C3A §5).
    /// </summary>
    /// <remarks>
    /// The failure this guards against is a probe that synthesises a hash: Approve would then
    /// be reported available while the real, correctly-hashed command was refused — or worse,
    /// reported available on a step with no result at all.
    /// </remarks>
    [Fact]
    public void Approve_and_Reject_availability_tracks_the_step_actually_having_a_result()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());
        scenario.Must(new WorkflowCommand.StartStep(StepKind.Enhancement));

        // Processing: an attempt is running, so there is nothing to review yet.
        WorkflowEngine.Instance.AvailableCommands(scenario.State)
            .ShouldNotContain(CommandKind.Approve);

        RevisionId produced = scenario.NextRevision();
        Sha256 hash = WorkflowScenario.HashOf(produced);
        scenario.Must(SystemCommands.Succeeded(
            scenario.NextAttempt(), StepKind.Enhancement, produced, hash));

        IReadOnlyList<CommandKind> available = WorkflowEngine.Instance.AvailableCommands(scenario.State);
        available.ShouldContain(CommandKind.Approve);
        available.ShouldContain(CommandKind.Reject);

        // The command the UI would actually send — carrying the displayed hash — is accepted,
        // and a differently-hashed one is still refused. Probing did not weaken the guard.
        WorkflowSnapshot atReview = scenario.State;
        WorkflowEngine.Instance
            .Apply(atReview, new WorkflowCommand.Approve(StepKind.Enhancement, hash), FixedContext)
            .IsAccepted.ShouldBeTrue();

        WorkflowEngine.Instance
            .Apply(
                atReview,
                new WorkflowCommand.Approve(StepKind.Enhancement, WorkflowScenario.HashOf(scenario.NextRevision())),
                FixedContext)
            .IsRejected.ShouldBeTrue();
    }

    /// <summary>
    /// HandOff availability answers "may this be attempted here", while the reason guard still
    /// applies to the real command (Epic 11100 Part 3C3A §5).
    /// </summary>
    [Fact]
    public void HandOff_is_offered_at_review_but_an_empty_reason_is_still_refused()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());
        scenario.Must(new WorkflowCommand.StartStep(StepKind.Enhancement));

        RevisionId produced = scenario.NextRevision();
        scenario.Must(SystemCommands.Succeeded(
            scenario.NextAttempt(), StepKind.Enhancement, produced, WorkflowScenario.HashOf(produced)));

        WorkflowEngine.Instance.AvailableCommands(scenario.State).ShouldContain(CommandKind.HandOff);

        WorkflowEngine.Instance
            .Apply(scenario.State, new WorkflowCommand.HandOff(StepKind.Enhancement, "   "), FixedContext)
            .Rejection!.Code.ShouldBe(RejectionCode.InvalidPayload);

        scenario.Apply(new WorkflowCommand.HandOff(StepKind.Enhancement, "operator will finish by hand"))
            .IsAccepted.ShouldBeTrue();
    }

    /// <summary>
    /// <c>SetPrintDimensions</c> availability tracks the step, and probing has not made the
    /// payload guard any weaker (Epic 11100 Part 3C3B §8).
    /// </summary>
    /// <remarks>
    /// Two things must hold at once, and one without the other would be a defect: the screen
    /// gets a truthful answer to "may a size be confirmed now", and a size the domain would
    /// refuse is still refused. The probe's own stand-in size is never applied — the assertion
    /// that the session still has no dimensions after probing is what proves it.
    /// </remarks>
    [Fact]
    public void SetPrintDimensions_is_offered_only_on_its_step_and_still_validates_its_payload()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.GeneratePrintTiff);
        scenario.CompleteImport();

        // OriginalConfirmation is current: the dimensions step has not been reached.
        WorkflowEngine.Instance.AvailableCommands(scenario.State)
            .ShouldNotContain(CommandKind.SetPrintDimensions);

        scenario.Must(new WorkflowCommand.ConfirmOriginal());

        WorkflowEngine.Instance.AvailableCommands(scenario.State)
            .ShouldContain(CommandKind.SetPrintDimensions);

        // Probing answered the category question without recording a size of its own.
        scenario.State.Dimensions.ShouldBeNull();

        // And the real command's payload guard is untouched: the engine refuses a size the
        // domain would never have produced in the first place.
        WorkflowEngine.Instance
            .Apply(
                scenario.State,
                new WorkflowCommand.SetPrintDimensions(default),
                FixedContext)
            .Rejection!.Code.ShouldBe(RejectionCode.InvalidPayload);

        scenario.Apply(new WorkflowCommand.SetPrintDimensions(WorkflowScenario.A4Portrait))
            .IsAccepted.ShouldBeTrue();
    }

    /// <summary>
    /// <c>SelectWhiteUnderbaseBranch</c> availability follows the workflow, and probing it
    /// gives no session a branch (Part 3C3B §6, §8).
    /// </summary>
    /// <remarks>
    /// The second assertion is the important one. The probe has to name some branch to ask its
    /// question, and if that ever leaked into the state it would be a default — the exact thing
    /// MVP design §12 forbids. So: available, repeatedly probed, and still unchosen.
    /// </remarks>
    [Fact]
    public void SelectWhiteUnderbaseBranch_probing_never_leaves_a_branch_behind()
    {
        WorkflowScenario tiff = WorkflowScenario.For(WorkflowType.GeneratePrintTiff);
        tiff.CompleteImport();

        WorkflowEngine.Instance.AvailableCommands(tiff.State)
            .ShouldContain(CommandKind.SelectWhiteUnderbaseBranch);
        WorkflowEngine.Instance.AvailableCommands(tiff.State);
        WorkflowEngine.Instance.AvailableCommands(tiff.State);

        tiff.State.WhiteUnderbaseBranch.ShouldBeNull();

        // Still refused without a justification, so probing did not relax that guard either.
        WorkflowEngine.Instance
            .Apply(
                tiff.State,
                new WorkflowCommand.SelectWhiteUnderbaseBranch(WhiteUnderbaseBranch.W1_2px, "  "),
                FixedContext)
            .Rejection!.Code.ShouldBe(RejectionCode.InvalidPayload);

        // A workflow that produces no TIFF is never offered the decision at all.
        WorkflowScenario asset = WorkflowScenario.For(WorkflowType.PrepareAsset);
        asset.CompleteImport();

        WorkflowEngine.Instance.AvailableCommands(asset.State)
            .ShouldNotContain(CommandKind.SelectWhiteUnderbaseBranch);
        WorkflowEngine.Instance.AvailableCommands(asset.State)
            .ShouldNotContain(CommandKind.SetPrintDimensions);
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
