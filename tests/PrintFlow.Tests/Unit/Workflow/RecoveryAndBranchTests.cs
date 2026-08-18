using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Effects;
using PrintFlow.Workflow.Engine;

namespace PrintFlow.Tests.Unit.Workflow;

/// <summary>Skip, reject/retry, handoff, interruption, return-to-step, W1 and completion.</summary>
public sealed class RecoveryAndBranchTests
{
    // -----------------------------------------------------------------------------
    // Skip
    // -----------------------------------------------------------------------------

    [Theory]
    [InlineData(StepKind.Enhancement)]
    [InlineData(StepKind.BackgroundRemoval)]
    public void Skippable_steps_can_be_skipped_and_create_no_revision(StepKind kind)
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());

        if (kind == StepKind.BackgroundRemoval)
        {
            scenario.Must(new WorkflowCommand.Skip(StepKind.Enhancement));
        }

        scenario.Apply(new WorkflowCommand.Skip(kind));

        scenario.StateOf(kind).ShouldBe(StepState.Skipped);
        scenario.State.Step(kind)!.CurrentRevisionId.ShouldBeNull();
        scenario.EffectCount<WorkflowEffect.PersistRevision>().ShouldBe(0);
        scenario.Effect<WorkflowEffect.RecordSkip>().Reason
            .ShouldBe(WorkflowCommand.Skip.DefaultReason);
    }

    [Fact]
    public void A_skipped_step_passes_its_upstream_revision_through_to_the_next_step()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        RevisionId root = scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());
        scenario.Must(new WorkflowCommand.Skip(StepKind.Enhancement));

        scenario.Apply(new WorkflowCommand.StartStep(StepKind.BackgroundRemoval));

        // Background removal works from the import root, because enhancement produced nothing.
        scenario.Effect<WorkflowEffect.CreateWorkingCopy>().SourceRevision.ShouldBe(root);
    }

    // -----------------------------------------------------------------------------
    // Reject and retry
    // -----------------------------------------------------------------------------

    [Fact]
    public void Reject_moves_the_step_to_RetryRequired_and_invalidates_the_result()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());
        scenario.Must(new WorkflowCommand.StartStep(StepKind.Enhancement));

        RevisionId enhanced = scenario.NextRevision();
        scenario.Must(SystemCommands.Succeeded(
            scenario.NextAttempt(), StepKind.Enhancement, enhanced, WorkflowScenario.HashOf(enhanced)));

        scenario.Apply(new WorkflowCommand.Reject(
            StepKind.Enhancement, WorkflowScenario.HashOf(enhanced), RejectionReason.EdgeError, "halo"));

        scenario.StateOf(StepKind.Enhancement).ShouldBe(StepState.RetryRequired);

        WorkflowEffect.RecordReview review = scenario.Effect<WorkflowEffect.RecordReview>();
        review.IsApproved.ShouldBeFalse();
        review.QuickReason.ShouldBe(RejectionReason.EdgeError);

        WorkflowEffect.InvalidateDescendants invalidation =
            scenario.Effect<WorkflowEffect.InvalidateDescendants>();
        invalidation.FromRevision.ShouldBe(enhanced);
        invalidation.Reason.ShouldBe(InvalidationReason.Rejected);
    }

    [Fact]
    public void Retry_returns_the_step_to_a_startable_state_and_the_next_attempt_uses_a_fresh_copy()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        RevisionId root = scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());
        scenario.Must(new WorkflowCommand.StartStep(StepKind.Enhancement));

        RevisionId enhanced = scenario.NextRevision();
        scenario.Must(SystemCommands.Succeeded(
            scenario.NextAttempt(), StepKind.Enhancement, enhanced, WorkflowScenario.HashOf(enhanced)));
        scenario.Must(new WorkflowCommand.Reject(
            StepKind.Enhancement, WorkflowScenario.HashOf(enhanced), RejectionReason.InsufficientResult));

        scenario.Must(new WorkflowCommand.Retry(StepKind.Enhancement));
        scenario.StateOf(StepKind.Enhancement).ShouldBe(StepState.Waiting);

        scenario.Apply(new WorkflowCommand.StartStep(StepKind.Enhancement));
        scenario.StateOf(StepKind.Enhancement).ShouldBe(StepState.Processing);

        // A retry never resumes from the rejected artefact: it starts again from upstream.
        scenario.Effect<WorkflowEffect.CreateWorkingCopy>().SourceRevision.ShouldBe(root);
        scenario.Effect<WorkflowEffect.RecordAttemptStarted>().RetrySequence.ShouldBe(1);
    }

    [Fact]
    public void A_rejected_step_may_also_be_started_again_directly()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());
        scenario.ForceStep(StepKind.Enhancement, StepState.RetryRequired);

        scenario.Apply(new WorkflowCommand.StartStep(StepKind.Enhancement));

        scenario.LastTransition.IsAccepted.ShouldBeTrue();
        scenario.StateOf(StepKind.Enhancement).ShouldBe(StepState.Processing);
    }

    // -----------------------------------------------------------------------------
    // Failure, interruption and recovery
    // -----------------------------------------------------------------------------

    [Fact]
    public void A_failed_attempt_creates_no_revision_and_leaves_the_step_retryable()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());
        scenario.Must(new WorkflowCommand.StartStep(StepKind.Enhancement));

        scenario.Apply(SystemCommands.Failed(
            scenario.NextAttempt(), StepKind.Enhancement, FailureCode.UnknownDialog));

        scenario.StateOf(StepKind.Enhancement).ShouldBe(StepState.Failed);
        scenario.State.Step(StepKind.Enhancement)!.CurrentRevisionId.ShouldBeNull();
        scenario.EffectCount<WorkflowEffect.PersistRevision>().ShouldBe(0);
        scenario.Effect<WorkflowEffect.RecordAttemptFailure>().Failure.Code
            .ShouldBe(FailureCode.UnknownDialog);
        scenario.EffectCount<WorkflowEffect.ReleaseAutomationLock>().ShouldBe(1);
    }

    [Fact]
    public void An_interrupted_attempt_offers_retry_skip_and_handoff_as_recovery()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());
        scenario.Must(new WorkflowCommand.StartStep(StepKind.Enhancement));

        scenario.Apply(SystemCommands.Interrupted(scenario.NextAttempt(), StepKind.Enhancement));

        scenario.StateOf(StepKind.Enhancement).ShouldBe(StepState.Interrupted);
        scenario.EffectCount<WorkflowEffect.PersistRevision>().ShouldBe(0);
        scenario.EffectCount<WorkflowEffect.ReleaseAutomationLock>().ShouldBe(1);

        IReadOnlyList<CommandKind> available = WorkflowEngine.Instance.AvailableCommands(scenario.State);
        available.ShouldContain(CommandKind.Retry);
        available.ShouldContain(CommandKind.StartStep);
        available.ShouldContain(CommandKind.Skip);
        available.ShouldContain(CommandKind.HandOff);
    }

    [Fact]
    public void An_interrupted_non_skippable_step_cannot_be_skipped_as_a_way_out()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());
        scenario.Must(new WorkflowCommand.Skip(StepKind.Enhancement));
        scenario.Must(new WorkflowCommand.Skip(StepKind.BackgroundRemoval));
        scenario.Must(new WorkflowCommand.StartStep(StepKind.Trim));
        scenario.Must(SystemCommands.Interrupted(scenario.NextAttempt(), StepKind.Trim));

        scenario.Apply(new WorkflowCommand.Skip(StepKind.Trim))
            .Rejection!.Code.ShouldBe(RejectionCode.StepNotSkippable);
    }

    // -----------------------------------------------------------------------------
    // Handoff
    // -----------------------------------------------------------------------------

    [Theory]
    [InlineData(StepState.ReviewRequired)]
    [InlineData(StepState.RetryRequired)]
    [InlineData(StepState.Failed)]
    [InlineData(StepState.Interrupted)]
    public void HandOff_is_legal_from_review_failure_and_interruption(StepState from)
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        RevisionId root = scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());
        scenario.ForceStep(StepKind.Enhancement, from);

        scenario.Apply(new WorkflowCommand.HandOff(StepKind.Enhancement, "Meitu edge is wrong"));

        scenario.LastTransition.IsAccepted.ShouldBeTrue();
        scenario.State.SessionState.ShouldBe(SessionState.HandedOff);
        scenario.Effect<WorkflowEffect.CreateWorkingCopy>().SourceRevision.ShouldBe(root);
        scenario.Effect<WorkflowEffect.OpenForManualWork>().Reason.ShouldBe("Meitu edge is wrong");
        scenario.EffectCount<WorkflowEffect.ReleaseAutomationLock>().ShouldBe(1);
        scenario.EffectCount<WorkflowEffect.MarkSessionHandedOff>().ShouldBe(1);
    }

    [Fact]
    public void A_handed_off_session_allows_no_further_automatic_progression()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());
        scenario.ForceStep(StepKind.Enhancement, StepState.Failed);
        scenario.Must(new WorkflowCommand.HandOff(StepKind.Enhancement, "manual takeover"));

        scenario.Apply(new WorkflowCommand.StartStep(StepKind.Enhancement))
            .Rejection!.Code.ShouldBe(RejectionCode.SessionNotActive);
        scenario.Apply(new WorkflowCommand.Retry(StepKind.Enhancement))
            .Rejection!.Code.ShouldBe(RejectionCode.SessionNotActive);
        scenario.Apply(new WorkflowCommand.Complete())
            .Rejection!.Code.ShouldBe(RejectionCode.SessionNotActive);

        // Abandoning remains the one legal exit.
        scenario.Apply(new WorkflowCommand.AbandonSession("work finished manually")).IsAccepted.ShouldBeTrue();
    }

    [Fact]
    public void HandOff_from_Waiting_is_refused()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());

        scenario.Apply(new WorkflowCommand.HandOff(StepKind.Enhancement, "no reason yet"))
            .Rejection!.Code.ShouldBe(RejectionCode.IllegalStateTransition);
    }

    // -----------------------------------------------------------------------------
    // W1 white underbase
    // -----------------------------------------------------------------------------

    [Fact]
    public void Photoshop_output_cannot_start_without_an_explicit_W1_branch()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.GeneratePrintTiff);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());
        scenario.Must(new WorkflowCommand.SetPrintDimensions(WorkflowScenario.A4Portrait));

        scenario.State.WhiteUnderbaseBranch.ShouldBeNull("there is no default branch");

        WorkflowTransition transition = scenario.Apply(new WorkflowCommand.StartStep(StepKind.PhotoshopOutput));

        transition.IsRejected.ShouldBeTrue();
        transition.Rejection!.Code.ShouldBe(RejectionCode.PreconditionNotMet);
        transition.Rejection.DebugMessage.ShouldContain("white-underbase");
    }

    [Fact]
    public void Photoshop_output_cannot_start_without_confirmed_dimensions()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.GeneratePrintTiff);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());
        scenario.Must(new WorkflowCommand.SelectWhiteUnderbaseBranch(
            WhiteUnderbaseBranch.W1_2px, "large solid rectangle"));

        // PrintDimensions is still the current step, so output cannot be reached at all.
        scenario.Apply(new WorkflowCommand.StartStep(StepKind.PhotoshopOutput))
            .Rejection!.Code.ShouldBe(RejectionCode.NotCurrentStep);
    }

    [Theory]
    [InlineData(WhiteUnderbaseBranch.W1_0px)]
    [InlineData(WhiteUnderbaseBranch.W1_1px)]
    [InlineData(WhiteUnderbaseBranch.W1_2px)]
    public void All_three_W1_branches_are_selectable_and_none_is_a_default(WhiteUnderbaseBranch branch)
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareCustomerDesign);

        scenario.Apply(new WorkflowCommand.SelectWhiteUnderbaseBranch(branch, "operator classification"));

        scenario.LastTransition.IsAccepted.ShouldBeTrue();
        scenario.State.WhiteUnderbaseBranch.ShouldBe(branch);
        scenario.Effect<WorkflowEffect.PersistWhiteUnderbaseBranch>().Branch.ShouldBe(branch);
    }

    [Fact]
    public void A_W1_branch_without_a_justification_is_refused()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareCustomerDesign);

        scenario.Apply(new WorkflowCommand.SelectWhiteUnderbaseBranch(WhiteUnderbaseBranch.W1_1px, "  "))
            .Rejection!.Code.ShouldBe(RejectionCode.InvalidPayload);
    }

    // -----------------------------------------------------------------------------
    // ReturnToStep and AddAnotherSize
    // -----------------------------------------------------------------------------

    [Fact]
    public void ReturnToStep_resets_the_target_and_everything_after_it()
    {
        WorkflowScenario scenario = ValidTransitionTests.PrepareAssetUpToExport();

        scenario.Apply(new WorkflowCommand.ReturnToStep(StepKind.BackgroundRemoval));

        scenario.LastTransition.IsAccepted.ShouldBeTrue();
        scenario.StateOf(StepKind.BackgroundRemoval).ShouldBe(StepState.Waiting);
        scenario.StateOf(StepKind.Trim).ShouldBe(StepState.Waiting);

        // Upstream work is untouched.
        scenario.StateOf(StepKind.Import).ShouldBe(StepState.Approved);
        scenario.StateOf(StepKind.Enhancement).ShouldBe(StepState.Approved);

        scenario.Effect<WorkflowEffect.ResetStepsFrom>().FromStep.ShouldBe(StepKind.BackgroundRemoval);
        scenario.EffectCount<WorkflowEffect.InvalidateDescendants>().ShouldBe(1);
    }

    [Fact]
    public void ReturnToStep_before_dimensions_clears_the_production_decisions()
    {
        WorkflowScenario scenario = CompletedTiffSession();
        scenario.Must(new WorkflowCommand.AddAnotherSize());
        scenario.Must(new WorkflowCommand.SetPrintDimensions(WorkflowScenario.A4Portrait));

        scenario.Must(new WorkflowCommand.ReturnToStep(StepKind.PrintDimensions));

        scenario.State.Dimensions.ShouldBeNull();
        scenario.State.WhiteUnderbaseBranch.ShouldBeNull(
            "each output requires its own explicit W1 decision");
    }

    [Fact]
    public void AddAnotherSize_reopens_at_dimensions_and_keeps_the_approved_output()
    {
        WorkflowScenario scenario = CompletedTiffSession();
        int approvedBefore = scenario.State.ApprovedPrintOutputCount;

        scenario.Apply(new WorkflowCommand.AddAnotherSize());

        scenario.LastTransition.IsAccepted.ShouldBeTrue();
        scenario.State.SessionState.ShouldBe(SessionState.Active);
        scenario.State.CurrentStep!.Step.ShouldBe(StepKind.PrintDimensions);
        scenario.State.Dimensions.ShouldBeNull();
        scenario.State.WhiteUnderbaseBranch.ShouldBeNull();

        // Siblings, not descendants: the existing approved output is left alone.
        scenario.State.ApprovedPrintOutputCount.ShouldBe(approvedBefore);
        scenario.EffectCount<WorkflowEffect.InvalidateDescendants>().ShouldBe(0);
        scenario.EffectCount<WorkflowEffect.BeginAdditionalOutput>().ShouldBe(1);
    }

    [Fact]
    public void AddAnotherSize_does_not_apply_to_PrepareAsset()
    {
        WorkflowScenario scenario = ValidTransitionTests.PrepareAssetUpToExport();
        scenario.CompleteStep(StepKind.ApprovedPngExport);
        scenario.Must(new WorkflowCommand.Complete());

        scenario.Apply(new WorkflowCommand.AddAnotherSize())
            .Rejection!.Code.ShouldBe(RejectionCode.CommandNotApplicable);
    }

    // -----------------------------------------------------------------------------
    // Completion
    // -----------------------------------------------------------------------------

    [Fact]
    public void Completion_requires_the_terminal_artefact_to_be_approved()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.GeneratePrintTiff);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());
        scenario.Must(new WorkflowCommand.SetPrintDimensions(WorkflowScenario.A4Portrait));
        scenario.Must(new WorkflowCommand.SelectWhiteUnderbaseBranch(
            WhiteUnderbaseBranch.W1_0px, "fine white detail"));
        scenario.Must(new WorkflowCommand.StartStep(StepKind.PhotoshopOutput));

        RevisionId tiff = scenario.NextRevision();
        scenario.Must(SystemCommands.Succeeded(
            scenario.NextAttempt(), StepKind.PhotoshopOutput, tiff, WorkflowScenario.HashOf(tiff)));

        // Awaiting final review: completion must not be possible yet.
        scenario.StateOf(StepKind.PhotoshopOutput).ShouldBe(StepState.ReviewRequired);
        scenario.Apply(new WorkflowCommand.Complete())
            .Rejection!.Code.ShouldBe(RejectionCode.WorkflowNotComplete);

        scenario.Must(new WorkflowCommand.Approve(StepKind.PhotoshopOutput, WorkflowScenario.HashOf(tiff)));
        scenario.Must(new WorkflowCommand.Complete());
        scenario.State.SessionState.ShouldBe(SessionState.Completed);
    }

    [Fact]
    public void A_session_with_both_Meitu_steps_skipped_still_completes()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());
        scenario.Must(new WorkflowCommand.Skip(StepKind.Enhancement));
        scenario.Must(new WorkflowCommand.Skip(StepKind.BackgroundRemoval));
        scenario.CompleteStep(StepKind.Trim);
        scenario.CompleteStep(StepKind.ApprovedPngExport);

        scenario.Must(new WorkflowCommand.Complete());

        scenario.State.SessionState.ShouldBe(SessionState.Completed);
        scenario.StateOf(StepKind.Enhancement).ShouldBe(StepState.Skipped);
    }

    internal static WorkflowScenario CompletedTiffSession()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.GeneratePrintTiff);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());
        scenario.Must(new WorkflowCommand.SetPrintDimensions(WorkflowScenario.A4Portrait));
        scenario.Must(new WorkflowCommand.SelectWhiteUnderbaseBranch(
            WhiteUnderbaseBranch.W1_1px, "ordinary design"));
        scenario.CompleteStep(StepKind.PhotoshopOutput);
        scenario.Must(new WorkflowCommand.Complete());
        return scenario;
    }
}
