using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Definitions;
using PrintFlow.Workflow.Effects;
using PrintFlow.Workflow.Engine;

namespace PrintFlow.Tests.Unit.Workflow;

/// <summary>
/// The engine half of adjusting a trim review (SCRUM-11147): legal only from Trim under review,
/// exact on both identities, and nothing but the existing manual-crop effects.
/// </summary>
public sealed class AdjustTrimFromReviewTests
{
    private static readonly TrimBounds Kept = TrimBounds.FromEdges(1132, 780, 2820, 2125);

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
    public void Legal_only_from_a_trim_under_review_and_the_probe_agrees(WorkflowType type, StepKind kind, StepState state)
    {
        WorkflowSnapshot snapshot = At(type, kind, state);
        bool legal = snapshot.CurrentStep is { Step: StepKind.Trim, State: StepState.ReviewRequired };

        WorkflowTransition result = Apply(snapshot, ExactCommand(snapshot));

        result.IsAccepted.ShouldBe(legal);
        WorkflowEngine.Instance.AvailableCommands(snapshot).Contains(CommandKind.AdjustTrimFromReview).ShouldBe(legal);
        if (!legal) result.Effects.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(WorkflowType.PrepareAsset)]
    [InlineData(WorkflowType.PrepareCustomerDesign)]
    public void Accepted_adjustment_starts_an_ordinary_manual_crop_of_the_pre_trim_source_and_writes_nothing_about_the_review(
        WorkflowType type)
    {
        WorkflowSnapshot snapshot = At(type, StepKind.Trim, StepState.ReviewRequired);
        SessionStep before = snapshot.Step(StepKind.Trim)!;
        (RevisionId sourceId, Sha256 _) = snapshot.UpstreamResultOf(StepKind.Trim)!.Value;
        AttemptId attempt = AttemptId.From(Guid.CreateVersion7());

        WorkflowTransition result = WorkflowEngine.Instance.Apply(snapshot, ExactCommand(snapshot),
            new CommandContext(DateTimeOffset.UnixEpoch.AddHours(1), "tester", default, attempt));

        result.IsAccepted.ShouldBeTrue();
        SessionStep after = result.State.Step(StepKind.Trim)!;
        after.State.ShouldBe(StepState.Processing);
        after.CurrentRevisionId.ShouldBeNull("the result under review is no longer the step's offer");
        after.CurrentRevisionSha256.ShouldBeNull();
        after.AttemptCount.ShouldBe(before.AttemptCount + 1);

        // Exactly the three manual-crop effects: no review, no rejection, no invalidation.
        result.Effects.Count.ShouldBe(3);
        result.Effects[0].ShouldBe(new WorkflowEffect.CreateWorkingCopy(StepKind.Trim, sourceId, WorkspaceArea.Working));
        result.Effects[1].ShouldBe(new WorkflowEffect.RecordAttemptStarted(
            attempt, StepKind.Trim, OperationKind.ManualImport, sourceId, before.AttemptCount));
        result.Effects[2].ShouldBe(new WorkflowEffect.RunManualCrop(attempt, StepKind.Trim, sourceId, Kept, ManualCropMargin.Tight));

        // Every other step is untouched: approved upstream work stays exactly as it was.
        foreach (SessionStep step in snapshot.Steps.Where(s => s.Step != StepKind.Trim))
            result.State.Step(step.Step).ShouldBe(step);
    }

    [Fact]
    public void A_stale_reviewed_revision_is_refused_even_when_its_hash_matches()
    {
        WorkflowSnapshot snapshot = At(WorkflowType.PrepareAsset, StepKind.Trim, StepState.ReviewRequired);
        var exact = (WorkflowCommand.AdjustTrimFromReview)ExactCommand(snapshot);

        Apply(snapshot, exact with { ReviewedRevision = RevisionId.From(Guid.NewGuid()) }).IsRejected.ShouldBeTrue();
        Apply(snapshot, exact with { ReviewedHash = Sha256.Parse(new string('F', 64)) }).IsRejected.ShouldBeTrue();
        Apply(snapshot, exact with { SourceRevision = RevisionId.From(Guid.NewGuid()) }).IsRejected.ShouldBeTrue();
        Apply(snapshot, exact with { SourceHash = Sha256.Parse(new string('F', 64)) }).IsRejected.ShouldBeTrue();

        // The reviewed result itself is never accepted as the thing to crop.
        Apply(snapshot, exact with { SourceRevision = exact.ReviewedRevision, SourceHash = exact.ReviewedHash })
            .IsRejected.ShouldBeTrue();
        Apply(snapshot, exact with { Crop = default }).IsRejected.ShouldBeTrue();
        Apply(snapshot, exact).IsAccepted.ShouldBeTrue();
    }

    [Theory]
    [InlineData(SessionState.HandedOff)]
    [InlineData(SessionState.Completed)]
    [InlineData(SessionState.Abandoned)]
    public void A_session_that_is_not_progressing_cannot_be_adjusted(SessionState sessionState)
    {
        WorkflowSnapshot snapshot = At(WorkflowType.PrepareAsset, StepKind.Trim, StepState.ReviewRequired) with
        {
            SessionState = sessionState,
        };

        Apply(snapshot, ExactCommand(snapshot)).IsRejected.ShouldBeTrue();
        WorkflowEngine.Instance.AvailableCommands(snapshot).ShouldNotContain(CommandKind.AdjustTrimFromReview);
    }

    /// <summary>
    /// Steps before <paramref name="kind"/> approved with a result, <paramref name="kind"/> in
    /// <paramref name="state"/>, later steps waiting. Every step's hash is distinct so an
    /// identity mix-up is visible.
    /// </summary>
    private static WorkflowSnapshot At(WorkflowType type, StepKind kind, StepState state)
    {
        WorkflowSnapshot snapshot = WorkflowSnapshot.Create(SessionId.From(Guid.NewGuid()), type,
            OutputName.Parse("adjust"), DateTimeOffset.UnixEpoch);
        int ordinal = snapshot.Definition.IndexOf(kind);
        return snapshot with
        {
            Steps = snapshot.Steps.Select(step =>
            {
                bool hasResult = step.Ordinal < ordinal || (step.Ordinal == ordinal && state != StepState.Skipped);
                return step with
                {
                    State = step.Ordinal < ordinal ? StepState.Approved : step.Ordinal == ordinal ? state : StepState.Waiting,
                    CurrentRevisionId = hasResult ? RevisionId.From(Guid.NewGuid()) : null,
                    CurrentRevisionSha256 = hasResult ? Sha256.Parse(new string((char)('0' + step.Ordinal), 64)) : null,
                    AttemptCount = step.Ordinal == ordinal ? 2 : step.AttemptCount,
                };
            }).ToArray(),
        };
    }

    /// <summary>The command a screen would send for the snapshot's own trim review, or a stand-in elsewhere.</summary>
    private static WorkflowCommand ExactCommand(WorkflowSnapshot snapshot)
    {
        SessionStep? trim = snapshot.Step(StepKind.Trim);
        (RevisionId Id, Sha256 Sha256)? source = snapshot.Definition.Contains(StepKind.Trim)
            ? snapshot.UpstreamResultOf(StepKind.Trim)
            : null;
        RevisionId fallback = RevisionId.From(Guid.NewGuid());
        Sha256 fallbackHash = Sha256.Parse(new string('E', 64));
        return new WorkflowCommand.AdjustTrimFromReview(
            trim?.CurrentRevisionId ?? fallback, trim?.CurrentRevisionSha256 ?? fallbackHash,
            source?.Id ?? fallback, source?.Sha256 ?? fallbackHash, Kept);
    }

    private static WorkflowTransition Apply(WorkflowSnapshot snapshot, WorkflowCommand command) =>
        WorkflowEngine.Instance.Apply(snapshot, command, new CommandContext(DateTimeOffset.UnixEpoch, "tester", default, default));
}
