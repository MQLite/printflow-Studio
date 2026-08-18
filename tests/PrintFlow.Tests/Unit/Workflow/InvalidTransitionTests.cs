using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Effects;

namespace PrintFlow.Tests.Unit.Workflow;

/// <summary>
/// The refusals. These matter more than the happy paths: they are what stops the system
/// silently producing an incorrect production file.
/// </summary>
public sealed class InvalidTransitionTests
{
    [Fact]
    public void Approve_is_rejected_while_the_step_is_processing()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());
        scenario.Must(new WorkflowCommand.StartStep(StepKind.Enhancement));

        WorkflowTransition transition = scenario.Apply(
            new WorkflowCommand.Approve(StepKind.Enhancement, WorkflowScenario.ForeignHash));

        transition.IsRejected.ShouldBeTrue();
        transition.Rejection!.Code.ShouldBe(RejectionCode.IllegalStateTransition);
    }

    [Fact]
    public void Reject_is_rejected_while_the_step_is_waiting()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());

        WorkflowTransition transition = scenario.Apply(new WorkflowCommand.Reject(
            StepKind.Enhancement, WorkflowScenario.ForeignHash, RejectionReason.EdgeError));

        transition.IsRejected.ShouldBeTrue();
        transition.Rejection!.Code.ShouldBe(RejectionCode.IllegalStateTransition);
    }

    [Fact]
    public void Trim_cannot_be_skipped()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());
        scenario.CompleteStep(StepKind.Enhancement);
        scenario.CompleteStep(StepKind.BackgroundRemoval);

        WorkflowTransition transition = scenario.Apply(new WorkflowCommand.Skip(StepKind.Trim));

        transition.IsRejected.ShouldBeTrue();
        transition.Rejection!.Code.ShouldBe(RejectionCode.StepNotSkippable);
    }

    [Fact]
    public void PrintDimensions_cannot_be_skipped()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.GeneratePrintTiff);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());

        WorkflowTransition transition = scenario.Apply(new WorkflowCommand.Skip(StepKind.PrintDimensions));

        transition.IsRejected.ShouldBeTrue();
        transition.Rejection!.Code.ShouldBe(RejectionCode.StepNotSkippable);
    }

    [Fact]
    public void Workflow_cannot_be_changed_once_a_derived_revision_exists()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());
        scenario.CompleteStep(StepKind.Enhancement);

        scenario.State.HasDerivedRevision.ShouldBeTrue();

        WorkflowTransition transition = scenario.Apply(
            new WorkflowCommand.SelectWorkflow(WorkflowType.GeneratePrintTiff));

        transition.IsRejected.ShouldBeTrue();
        transition.Rejection!.Code.ShouldBe(RejectionCode.WorkflowLocked);
        scenario.State.WorkflowType.ShouldBe(WorkflowType.PrepareAsset);
    }

    [Fact]
    public void Complete_is_rejected_while_a_step_is_unfinished()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());

        WorkflowTransition transition = scenario.Apply(new WorkflowCommand.Complete());

        transition.IsRejected.ShouldBeTrue();
        transition.Rejection!.Code.ShouldBe(RejectionCode.WorkflowNotComplete);
    }

    [Fact]
    public void A_step_that_is_not_current_cannot_be_started()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());

        // Enhancement is current; Trim is three steps away.
        WorkflowTransition transition = scenario.Apply(new WorkflowCommand.StartStep(StepKind.Trim));

        transition.IsRejected.ShouldBeTrue();
        transition.Rejection!.Code.ShouldBe(RejectionCode.NotCurrentStep);
    }

    [Fact]
    public void A_step_outside_the_workflow_cannot_be_started()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.GeneratePrintTiff);
        scenario.CompleteImport();

        WorkflowTransition transition = scenario.Apply(new WorkflowCommand.StartStep(StepKind.Enhancement));

        transition.IsRejected.ShouldBeTrue();
        transition.Rejection!.Code.ShouldBe(RejectionCode.StepNotInWorkflow);
    }

    [Fact]
    public void An_operator_confirmed_step_cannot_be_started_like_an_adapter_step()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.GeneratePrintTiff);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());

        WorkflowTransition transition = scenario.Apply(new WorkflowCommand.StartStep(StepKind.PrintDimensions));

        transition.IsRejected.ShouldBeTrue();
        transition.Rejection!.Code.ShouldBe(RejectionCode.CommandNotApplicable);
    }

    [Fact]
    public void Approving_with_a_stale_hash_is_refused()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());
        scenario.Must(new WorkflowCommand.StartStep(StepKind.Enhancement));

        RevisionId enhanced = scenario.NextRevision();
        scenario.Must(SystemCommands.Succeeded(
            scenario.NextAttempt(), StepKind.Enhancement, enhanced, WorkflowScenario.HashOf(enhanced)));

        // The operator reviewed something other than what the step is currently offering.
        WorkflowTransition transition = scenario.Apply(
            new WorkflowCommand.Approve(StepKind.Enhancement, WorkflowScenario.ForeignHash));

        transition.IsRejected.ShouldBeTrue();
        transition.Rejection!.Code.ShouldBe(RejectionCode.PreconditionNotMet);
        scenario.StateOf(StepKind.Enhancement).ShouldBe(StepState.ReviewRequired);
    }

    [Fact]
    public void No_command_progresses_a_completed_session()
    {
        WorkflowScenario scenario = ValidTransitionTests.PrepareAssetUpToExport();
        scenario.CompleteStep(StepKind.ApprovedPngExport);
        scenario.Must(new WorkflowCommand.Complete());

        scenario.Apply(new WorkflowCommand.StartStep(StepKind.Trim))
            .Rejection!.Code.ShouldBe(RejectionCode.SessionNotActive);
        scenario.Apply(new WorkflowCommand.ConfirmOriginal())
            .Rejection!.Code.ShouldBe(RejectionCode.SessionNotActive);
        scenario.Apply(new WorkflowCommand.SelectWorkflow(WorkflowType.GeneratePrintTiff))
            .Rejection!.Code.ShouldBe(RejectionCode.SessionNotActive);
        scenario.Apply(new WorkflowCommand.Complete())
            .Rejection!.Code.ShouldBe(RejectionCode.SessionNotActive);
    }

    [Fact]
    public void An_abandoned_session_accepts_nothing_further()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.AbandonSession("operator ended the job"));

        scenario.State.SessionState.ShouldBe(SessionState.Abandoned);
        scenario.Apply(new WorkflowCommand.ConfirmOriginal()).IsRejected.ShouldBeTrue();
        scenario.Apply(new WorkflowCommand.AbandonSession("again")).IsRejected.ShouldBeTrue();
    }

    [Fact]
    public void Abandoning_without_a_reason_is_refused()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);

        scenario.Apply(new WorkflowCommand.AbandonSession("   "))
            .Rejection!.Code.ShouldBe(RejectionCode.InvalidPayload);
    }

    [Fact]
    public void PrepareAsset_has_no_white_underbase_decision_to_make()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);

        WorkflowTransition transition = scenario.Apply(new WorkflowCommand.SelectWhiteUnderbaseBranch(
            WhiteUnderbaseBranch.W1_1px, "not applicable"));

        transition.IsRejected.ShouldBeTrue();
        transition.Rejection!.Code.ShouldBe(RejectionCode.CommandNotApplicable);
    }

    [Fact]
    public void ReturnToStep_refuses_a_step_that_is_not_upstream()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());

        // Enhancement is the current step, so there is nothing to rewind to.
        WorkflowTransition transition = scenario.Apply(new WorkflowCommand.ReturnToStep(StepKind.Enhancement));

        transition.IsRejected.ShouldBeTrue();
        transition.Rejection!.Code.ShouldBe(RejectionCode.PreconditionNotMet);
    }

    [Fact]
    public void AddAnotherSize_is_refused_on_an_active_session()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.GeneratePrintTiff);
        scenario.CompleteImport();

        scenario.Apply(new WorkflowCommand.AddAnotherSize())
            .Rejection!.Code.ShouldBe(RejectionCode.PreconditionNotMet);
    }

    [Fact]
    public void Rejected_commands_never_produce_effects()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);

        WorkflowTransition transition = scenario.Apply(new WorkflowCommand.Complete());

        transition.IsRejected.ShouldBeTrue();
        transition.Effects.ShouldBeEmpty();
        transition.NewState.ShouldBeNull();
    }
}
