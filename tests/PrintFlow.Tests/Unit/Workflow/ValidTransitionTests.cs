using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Effects;

namespace PrintFlow.Tests.Unit.Workflow;

/// <summary>The happy paths, walked end to end through the pure engine.</summary>
public sealed class ValidTransitionTests
{
    [Fact]
    public void Import_then_confirm_then_enhance_reaches_review_and_approves()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);

        scenario.CompleteImport();
        scenario.StateOf(StepKind.Import).ShouldBe(StepState.Approved);

        scenario.Must(new WorkflowCommand.ConfirmOriginal());
        scenario.StateOf(StepKind.OriginalConfirmation).ShouldBe(StepState.Approved);

        scenario.Must(new WorkflowCommand.StartStep(StepKind.Enhancement));
        scenario.StateOf(StepKind.Enhancement).ShouldBe(StepState.Processing);

        RevisionId enhanced = scenario.NextRevision();
        scenario.Must(SystemCommands.Succeeded(
            scenario.NextAttempt(), StepKind.Enhancement, enhanced, WorkflowScenario.HashOf(enhanced)));
        scenario.StateOf(StepKind.Enhancement).ShouldBe(StepState.ReviewRequired);

        scenario.Must(new WorkflowCommand.Approve(StepKind.Enhancement, WorkflowScenario.HashOf(enhanced)));
        scenario.StateOf(StepKind.Enhancement).ShouldBe(StepState.Approved);

        // The session has moved on to the next step by itself; nothing sets it explicitly.
        scenario.State.CurrentStep!.Step.ShouldBe(StepKind.BackgroundRemoval);
    }

    [Fact]
    public void Starting_a_step_emits_a_working_copy_an_attempt_record_and_an_adapter_call()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        RevisionId root = scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());

        scenario.Apply(new WorkflowCommand.StartStep(StepKind.Enhancement));

        scenario.Effect<WorkflowEffect.CreateWorkingCopy>().SourceRevision.ShouldBe(root);
        scenario.Effect<WorkflowEffect.RecordAttemptStarted>().Step.ShouldBe(StepKind.Enhancement);
        scenario.Effect<WorkflowEffect.RunAdapter>().Adapter.ShouldBe(
            global::PrintFlow.Workflow.Definitions.AdapterKind.Meitu);
    }

    [Fact]
    public void Approving_records_a_hash_bound_review_decision()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());
        scenario.Must(new WorkflowCommand.StartStep(StepKind.Enhancement));

        RevisionId enhanced = scenario.NextRevision();
        scenario.Must(SystemCommands.Succeeded(
            scenario.NextAttempt(), StepKind.Enhancement, enhanced, WorkflowScenario.HashOf(enhanced)));

        scenario.Apply(new WorkflowCommand.Approve(
            StepKind.Enhancement, WorkflowScenario.HashOf(enhanced), "looks right"));

        WorkflowEffect.RecordReview review = scenario.Effect<WorkflowEffect.RecordReview>();
        review.IsApproved.ShouldBeTrue();
        review.ReviewedHash.ShouldBe(WorkflowScenario.HashOf(enhanced));
        review.SubjectId.ShouldBe(enhanced.Value);
        review.Notes.ShouldBe("looks right");
    }

    [Fact]
    public void A_step_that_needs_no_review_is_approved_straight_from_a_successful_attempt()
    {
        WorkflowScenario scenario = PrepareAssetUpToExport();

        scenario.Must(new WorkflowCommand.StartStep(StepKind.ApprovedPngExport));
        RevisionId promoted = scenario.NextRevision();
        scenario.Must(SystemCommands.Succeeded(
            scenario.NextAttempt(),
            StepKind.ApprovedPngExport,
            promoted,
            WorkflowScenario.HashOf(promoted)));

        // The bytes are unchanged, so the upstream hash-bound approval already covers them.
        scenario.StateOf(StepKind.ApprovedPngExport).ShouldBe(StepState.Approved);
    }

    [Fact]
    public void PrepareAsset_completes_when_every_step_is_finished()
    {
        WorkflowScenario scenario = PrepareAssetUpToExport();
        scenario.CompleteStep(StepKind.ApprovedPngExport);

        scenario.State.AllStepsFinished.ShouldBeTrue();
        scenario.Apply(new WorkflowCommand.Complete());

        scenario.LastTransition.IsAccepted.ShouldBeTrue();
        scenario.State.SessionState.ShouldBe(SessionState.Completed);
        scenario.EffectCount<WorkflowEffect.CleanupWorking>().ShouldBe(1);
        scenario.EffectCount<WorkflowEffect.MarkSessionCompleted>().ShouldBe(1);
    }

    [Fact]
    public void GeneratePrintTiff_runs_import_confirm_dimensions_output_and_completes()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.GeneratePrintTiff);

        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal("finished design"));
        scenario.Must(new WorkflowCommand.SetPrintDimensions(WorkflowScenario.A4Portrait));
        scenario.Must(new WorkflowCommand.SelectWhiteUnderbaseBranch(
            WhiteUnderbaseBranch.W1_1px, "ordinary design"));
        scenario.CompleteStep(StepKind.PhotoshopOutput);

        scenario.Must(new WorkflowCommand.Complete());
        scenario.State.SessionState.ShouldBe(SessionState.Completed);
        scenario.State.ApprovedPrintOutputCount.ShouldBe(1);
    }

    [Fact]
    public void Confirming_the_original_in_GeneratePrintTiff_records_a_design_readiness_review()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.GeneratePrintTiff);
        scenario.CompleteImport();

        scenario.Apply(new WorkflowCommand.ConfirmOriginal("design is print-ready"));

        WorkflowEffect.RecordReview review = scenario.Effect<WorkflowEffect.RecordReview>();
        review.Step.ShouldBe(StepKind.OriginalConfirmation);
        review.IsApproved.ShouldBeTrue();
    }

    [Fact]
    public void Confirming_the_original_in_PrepareAsset_records_no_review()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();

        scenario.Apply(new WorkflowCommand.ConfirmOriginal());

        scenario.LastTransition.IsAccepted.ShouldBeTrue();
        scenario.EffectCount<WorkflowEffect.RecordReview>().ShouldBe(0);
    }

    [Fact]
    public void Workflow_may_be_changed_before_any_derived_revision_exists()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();

        scenario.Must(new WorkflowCommand.SelectWorkflow(WorkflowType.GeneratePrintTiff));

        scenario.State.WorkflowType.ShouldBe(WorkflowType.GeneratePrintTiff);
        scenario.State.Steps.Select(s => s.Step).ShouldBe(
            [StepKind.Import, StepKind.OriginalConfirmation, StepKind.PrintDimensions, StepKind.PhotoshopOutput]);

        // The completed import survives the change; the operator does not re-import.
        scenario.StateOf(StepKind.Import).ShouldBe(StepState.Approved);
    }

    [Fact]
    public void Setting_the_output_name_never_touches_a_source_file()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);

        scenario.Apply(new WorkflowCommand.SetOutputName(global::PrintFlow.Domain.Files.OutputName.Parse("Renamed_HD")));

        scenario.State.OutputName.Value.ShouldBe("Renamed_HD");
        scenario.Effect<WorkflowEffect.PersistOutputName>().Name.Value.ShouldBe("Renamed_HD");

        // Nothing in the effect set may rename or move anything belonging to the operator.
        scenario.LastTransition.Effects.Count.ShouldBe(1);
    }

    internal static WorkflowScenario PrepareAssetUpToExport()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());
        scenario.CompleteStep(StepKind.Enhancement);
        scenario.CompleteStep(StepKind.BackgroundRemoval);
        scenario.CompleteStep(StepKind.Trim);
        return scenario;
    }
}
