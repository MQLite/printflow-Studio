using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Effects;
using PrintFlow.Workflow.Engine;

namespace PrintFlow.Tests.Unit.Workflow;

/// <summary>
/// The reducer half of the Background Removal decision (Epic 11300 Part C2B1 §5–§9).
/// </summary>
/// <remarks>
/// No file, no database, no adapter — so what is on trial here is the rule and nothing else. The
/// end-to-end behaviour has its own coverage in <c>BackgroundRemovalDecisionTests</c>; these
/// assert the two things only a pure reducer can state cleanly: that recording a decision starts
/// nothing, and that refusing one produces no effects at all.
/// </remarks>
public sealed class BackgroundRemovalAuthorityTests
{
    private const BackgroundRemovalDecision Authorised =
        BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent;

    /// <summary>Recording a decision emits its persistence effect and nothing else (§5).</summary>
    /// <remarks>
    /// The negative half is the point. A decision that started an attempt, created a working copy
    /// or called an adapter would be an action wearing a decision's name, and the operator would
    /// have committed to a run by answering a question.
    /// </remarks>
    [Fact]
    public void Recording_a_decision_persists_it_and_starts_nothing()
    {
        WorkflowScenario scenario = AtBackgroundRemoval(out RevisionId upstream);

        scenario.Must(new WorkflowCommand.SetBackgroundRemovalDecision(
            Authorised, upstream, WorkflowScenario.HashOf(upstream)));

        WorkflowEffect.PersistBackgroundRemovalDecision persisted =
            scenario.Effect<WorkflowEffect.PersistBackgroundRemovalDecision>();
        persisted.Authority.Decision.ShouldBe(Authorised);
        persisted.Authority.ReviewedRevisionId.ShouldBe(upstream);
        persisted.Authority.ReviewedSha256.ShouldBe(WorkflowScenario.HashOf(upstream));

        scenario.LastTransition.Effects.Count.ShouldBe(1);
        scenario.EffectCount<WorkflowEffect.RecordAttemptStarted>().ShouldBe(0);
        scenario.EffectCount<WorkflowEffect.RunAdapter>().ShouldBe(0);
        scenario.EffectCount<WorkflowEffect.CreateWorkingCopy>().ShouldBe(0);

        scenario.StateOf(StepKind.BackgroundRemoval).ShouldBe(StepState.Waiting);
        scenario.State.UsableBackgroundRemovalAuthority.ShouldNotBeNull();
    }

    /// <summary>
    /// Starting the step without a usable authority produces no effects whatsoever (§7).
    /// </summary>
    /// <remarks>
    /// Not merely "no <c>RunAdapter</c>": a rejected command must emit nothing, because every
    /// effect the reducer returns is work the service will actually perform. An implementation
    /// that emitted <c>RecordAttemptStarted</c> and then refused later would write the fake
    /// processing attempt §7 exists to prevent.
    /// </remarks>
    [Fact]
    public void Starting_without_an_authority_is_rejected_with_no_effects()
    {
        WorkflowScenario scenario = AtBackgroundRemoval(out _);
        WorkflowSnapshot before = scenario.State;

        WorkflowTransition refused = scenario.Apply(new WorkflowCommand.StartStep(StepKind.BackgroundRemoval));

        refused.IsRejected.ShouldBeTrue();
        refused.Rejection!.Code.ShouldBe(RejectionCode.PreconditionNotMet);
        refused.Effects.ShouldBeEmpty();
        scenario.State.ShouldBe(before);
    }

    /// <summary>
    /// An authority granted over one Revision does not carry to another (§8).
    /// </summary>
    /// <remarks>
    /// Reached by forcing the upstream step onto a different result rather than by editing the
    /// authority, because that is the real-world shape: the operator's decision is untouched and
    /// the world moved underneath it.
    /// </remarks>
    [Fact]
    public void An_authority_does_not_survive_its_upstream_being_replaced()
    {
        WorkflowScenario scenario = AtBackgroundRemoval(out RevisionId first);

        scenario.Must(new WorkflowCommand.SetBackgroundRemovalDecision(
            Authorised, first, WorkflowScenario.HashOf(first)));
        scenario.State.UsableBackgroundRemovalAuthority.ShouldNotBeNull();

        RevisionId replacement = scenario.NextRevision();
        scenario.ForceStep(
            StepKind.Enhancement, StepState.Approved, replacement, WorkflowScenario.HashOf(replacement));

        // Retained as history, and powerless — the whole of the invalidation strategy (§9).
        scenario.State.BackgroundRemovalAuthority.ShouldNotBeNull();
        scenario.State.UsableBackgroundRemovalAuthority.ShouldBeNull();

        scenario.Apply(new WorkflowCommand.StartStep(StepKind.BackgroundRemoval))
            .IsRejected.ShouldBeTrue();
    }

    /// <summary>
    /// The same bytes under a different Revision identity do not count as the same content (§4).
    /// </summary>
    /// <remarks>
    /// A re-run of an upstream step over an unchanged source produces byte-identical output under
    /// a new Revision, so hash equality alone would silently re-authorise it. The operator
    /// reviewed an artefact, not a digest, and the identity is half of what they decided about.
    /// </remarks>
    [Fact]
    public void Matching_bytes_under_a_different_revision_do_not_authorise()
    {
        RevisionId reviewed = RevisionId.From(Guid.CreateVersion7());
        RevisionId other = RevisionId.From(Guid.CreateVersion7());
        Sha256 shared = WorkflowScenario.HashOf(reviewed);

        BackgroundRemovalAuthority authority =
            BackgroundRemovalAuthority.For(Authorised, reviewed, shared);

        authority.Authorises(reviewed, shared).ShouldBeTrue();
        authority.Authorises(other, shared).ShouldBeFalse();
        authority.Authorises(reviewed, WorkflowScenario.ForeignHash).ShouldBeFalse();
    }

    /// <summary>An authority that authorises nothing cannot be constructed (§7).</summary>
    [Fact]
    public void An_authority_cannot_be_built_from_the_refusal_value()
    {
        RevisionId reviewed = RevisionId.From(Guid.CreateVersion7());

        Should.Throw<ArgumentOutOfRangeException>(() => BackgroundRemovalAuthority.For(
            BackgroundRemovalDecision.Unspecified, reviewed, WorkflowScenario.HashOf(reviewed)));
    }

    /// <summary>
    /// A decision cannot be recorded against a result already awaiting review (§6).
    /// </summary>
    /// <remarks>
    /// The window is "between attempts", and this is why it has to be: accepting one here would
    /// leave the session claiming an authority that the running or completed attempt's own row
    /// does not describe — the drift the attempt-level record exists to prevent (§11).
    /// </remarks>
    [Fact]
    public void A_decision_cannot_be_recorded_while_a_cutout_awaits_review()
    {
        WorkflowScenario scenario = AtBackgroundRemoval(out RevisionId upstream);

        RevisionId cutout = scenario.NextRevision();
        scenario.ForceStep(
            StepKind.BackgroundRemoval, StepState.ReviewRequired, cutout, WorkflowScenario.HashOf(cutout));

        WorkflowTransition refused = scenario.Apply(new WorkflowCommand.SetBackgroundRemovalDecision(
            Authorised, upstream, WorkflowScenario.HashOf(upstream)));

        refused.IsRejected.ShouldBeTrue();
        refused.Rejection!.Code.ShouldBe(RejectionCode.PreconditionNotMet);
        refused.Effects.ShouldBeEmpty();
    }

    /// <summary>The read model offers the decision exactly while the engine would accept it (§23).</summary>
    [Fact]
    public void The_available_command_list_tracks_the_engine_answer()
    {
        WorkflowScenario scenario = AtBackgroundRemoval(out RevisionId upstream);

        WorkflowEngine.Instance.AvailableCommands(scenario.State)
            .ShouldContain(CommandKind.SetBackgroundRemovalDecision);

        // StartStep is not on offer until the decision exists, and is once it does.
        WorkflowEngine.Instance.AvailableCommands(scenario.State)
            .ShouldNotContain(CommandKind.StartStep);

        scenario.Must(new WorkflowCommand.SetBackgroundRemovalDecision(
            Authorised, upstream, WorkflowScenario.HashOf(upstream)));

        WorkflowEngine.Instance.AvailableCommands(scenario.State)
            .ShouldContain(CommandKind.StartStep);
    }

    /// <summary>A workflow with no Background Removal step has no decision to record (§6).</summary>
    [Fact]
    public void A_workflow_without_the_step_refuses_the_command()
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.GeneratePrintTiff);
        RevisionId root = scenario.CompleteImport();

        WorkflowTransition refused = scenario.Apply(new WorkflowCommand.SetBackgroundRemovalDecision(
            Authorised, root, WorkflowScenario.HashOf(root)));

        refused.IsRejected.ShouldBeTrue();
        WorkflowEngine.Instance.AvailableCommands(scenario.State)
            .ShouldNotContain(CommandKind.SetBackgroundRemovalDecision);
    }

    /// <summary>
    /// Drives PREPARE_ASSET to the point where Background Removal is next, undecided.
    /// </summary>
    /// <remarks>
    /// Enhancement is completed rather than skipped so the upstream is a produced Revision that a
    /// later step can genuinely replace, which is what the stale-authority cases need.
    /// </remarks>
    private static WorkflowScenario AtBackgroundRemoval(out RevisionId upstream)
    {
        WorkflowScenario scenario = WorkflowScenario.For(WorkflowType.PrepareAsset);
        scenario.CompleteImport();
        scenario.Must(new WorkflowCommand.ConfirmOriginal());
        upstream = scenario.CompleteStep(StepKind.Enhancement);

        scenario.State.CurrentStep!.Step.ShouldBe(StepKind.BackgroundRemoval);
        scenario.State.UsableBackgroundRemovalAuthority.ShouldBeNull();
        return scenario;
    }
}
