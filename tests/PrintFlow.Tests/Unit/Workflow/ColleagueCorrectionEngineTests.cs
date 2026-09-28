using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Definitions;
using PrintFlow.Workflow.Effects;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Unit.Workflow;

/// <summary>
/// SCRUM-11148, the pure halves: the engine legality of the two correction commands, and the
/// closing builder's bound, fail-closed and unbound outcomes (addendum T9).
/// </summary>
public sealed class ColleagueCorrectionEngineTests
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
    public void Asking_is_legal_only_on_an_active_background_removal_review_and_the_probe_agrees(
        WorkflowType type, StepKind kind, StepState state)
    {
        WorkflowSnapshot snapshot = At(type, kind, state);
        bool legal = snapshot.CurrentStep is { Step: StepKind.BackgroundRemoval, State: StepState.ReviewRequired };

        WorkflowTransition result = Apply(snapshot, Ask(snapshot));

        result.IsAccepted.ShouldBe(legal);
        WorkflowEngine.Instance.AvailableCommands(snapshot).Contains(CommandKind.RequestColleagueCorrection).ShouldBe(legal);
        if (!legal) result.Effects.ShouldBeEmpty();
        WorkflowSnapshot handedOff = snapshot with { SessionState = SessionState.HandedOff };
        Apply(handedOff, Ask(handedOff)).IsAccepted.ShouldBeFalse("a request needs an active session");
    }

    [Fact]
    public void Asking_hands_the_session_off_and_nothing_else()
    {
        WorkflowSnapshot snapshot = At(WorkflowType.PrepareAsset, StepKind.BackgroundRemoval, StepState.ReviewRequired);

        WorkflowTransition result = WorkflowEngine.Instance.Apply(snapshot, Ask(snapshot, note: null),
            new CommandContext(DateTimeOffset.UnixEpoch.AddHours(2), "tester", default, default));

        result.IsAccepted.ShouldBeTrue();
        result.State.SessionState.ShouldBe(SessionState.HandedOff);
        result.State.Steps.ShouldBe(snapshot.Steps, "the result stays under review: no step changes at all");
        result.Effects.ShouldBe([new WorkflowEffect.MarkSessionHandedOff(
            DateTimeOffset.UnixEpoch.AddHours(2), WorkflowCommand.RequestColleagueCorrection.DefaultReason)]);
        result.Effects.OfType<WorkflowEffect.ReleaseAutomationLock>().ShouldBeEmpty();
        result.Effects.OfType<WorkflowEffect.CreateWorkingCopy>().ShouldBeEmpty();

        // The generic HandOff still refuses an empty reason; only the new command has a default.
        Apply(snapshot, new WorkflowCommand.HandOff(StepKind.BackgroundRemoval, "  ")).IsAccepted.ShouldBeFalse();
    }

    [Fact]
    public void Asking_about_a_stale_result_or_a_changed_input_is_refused_even_with_matching_hashes()
    {
        WorkflowSnapshot snapshot = At(WorkflowType.PrepareAsset, StepKind.BackgroundRemoval, StepState.ReviewRequired);
        var ask = (WorkflowCommand.RequestColleagueCorrection)Ask(snapshot);

        Apply(snapshot, ask with { ReviewedRevision = RevisionId.From(Guid.NewGuid()) }).IsAccepted.ShouldBeFalse();
        Apply(snapshot, ask with { ReferenceRevision = RevisionId.From(Guid.NewGuid()) }).IsAccepted.ShouldBeFalse();
        Apply(snapshot, ask with { ReviewedHash = Sha256.Parse(new string('f', 64)) }).IsAccepted.ShouldBeFalse();
        Apply(snapshot, ask with { CorrectionRequestId = Guid.Empty }).IsAccepted.ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(States))]
    public void Importing_is_legal_only_on_a_handed_off_background_removal_review(WorkflowType type, StepKind kind, StepState state)
    {
        WorkflowSnapshot handedOff = At(type, kind, state) with { SessionState = SessionState.HandedOff };
        bool legal = handedOff.CurrentStep is { Step: StepKind.BackgroundRemoval, State: StepState.ReviewRequired };

        WorkflowTransition result = Apply(handedOff, Import(handedOff));

        result.IsAccepted.ShouldBe(legal);
        if (!legal) result.Effects.ShouldBeEmpty();
        WorkflowSnapshot active = handedOff with { SessionState = SessionState.Active };
        Apply(active, Import(active)).IsAccepted.ShouldBeFalse("only a handed-off session imports");
    }

    [Fact]
    public void Importing_has_exactly_the_manual_result_effects_and_takes_R_off_the_step()
    {
        WorkflowSnapshot snapshot = At(WorkflowType.PrepareAsset, StepKind.BackgroundRemoval, StepState.ReviewRequired)
            with { SessionState = SessionState.HandedOff };
        RevisionId u = snapshot.UpstreamRevisionOf(StepKind.BackgroundRemoval)!.Value;
        AttemptId attempt = AttemptId.From(Guid.CreateVersion7());

        WorkflowTransition result = WorkflowEngine.Instance.Apply(snapshot, Import(snapshot),
            new CommandContext(DateTimeOffset.UnixEpoch, "tester", default, attempt));

        result.IsAccepted.ShouldBeTrue();
        result.State.SessionState.ShouldBe(SessionState.Active);
        SessionStep step = result.State.Step(StepKind.BackgroundRemoval)!;
        (step.State, step.CurrentRevisionId).ShouldBe((StepState.Processing, (RevisionId?)null));
        result.Effects.ShouldBe(
        [
            new WorkflowEffect.RecordAttemptStarted(attempt, StepKind.BackgroundRemoval, OperationKind.ManualResultImport, u, 2),
            new WorkflowEffect.ImportManualResult(attempt, StepKind.BackgroundRemoval, u, @"C:\return\corrected.png"),
        ]);
        ManualResultEligibility.CanSubmit(snapshot).ShouldBeFalse("CanSubmit is unchanged for a handed-off review");
    }

    // -------------------------------------------------------------------------------------
    // T9: the closing builder
    // -------------------------------------------------------------------------------------

    public static TheoryData<string> Mismatches => new() { "none", "other-attempt", "input-is-R", "returned", "other-session", "unknown-id" };

    [Theory]
    [MemberData(nameof(Mismatches))]
    public void The_closing_builder_binds_only_on_the_full_predicate_and_otherwise_fails_closed(string mismatch)
    {
        (SessionAggregate afterStart, ProcessingAttempt attempt, CorrectionRequest request) = Bound();
        Guid requestId = request.Id;
        switch (mismatch)
        {
            case "other-attempt": afterStart = With(afterStart, request with { LastImportAttemptId = AttemptId.From(Guid.NewGuid()) }); break;
            case "input-is-R": attempt = attempt with { InputRevisionId = request.HandedOutRevisionId }; break;
            case "returned": afterStart = With(afterStart, request with { Status = CorrectionRequestStatus.Returned }); break;
            case "other-session": afterStart = With(afterStart, request with { SessionId = SessionId.From(Guid.NewGuid()) }); break;
            case "unknown-id": requestId = Guid.NewGuid(); break;
        }

        UnfinishedCorrectionClose unfinished = CorrectionClosing.Unfinished(requestId, afterStart, attempt)!;
        SucceededCorrectionClose succeeded = CorrectionClosing.Succeeded(
            requestId, afterStart, attempt, RevisionId.From(Guid.NewGuid()), DateTimeOffset.UnixEpoch);

        succeeded.MayHandOffAfterSuccess.ShouldBeFalse("a correction import is never handed off after success");
        if (mismatch == "none")
        {
            unfinished.IsBound.ShouldBeTrue();
            unfinished.HandOffReason.ShouldBe(request.EffectiveReason);
            unfinished.Changes.ShouldBe([new CorrectionRequestChange.AssertBound(request.Id, attempt.Id)]);
            succeeded.Changes.Single().ShouldBeOfType<CorrectionRequestChange.MarkReturned>().Attempt.ShouldBe(attempt.Id);
        }
        else
        {
            unfinished.IsBound.ShouldBeFalse();
            unfinished.HandOffReason.ShouldBe(CorrectionClosing.UnfinishedReason);
            unfinished.Changes.ShouldBeEmpty("no request change is made for a binding that does not hold");
            succeeded.IsBound.ShouldBeFalse();
            succeeded.Changes.ShouldBeEmpty("no RETURNED is fabricated");
        }
    }

    [Fact]
    public void Without_a_request_the_builder_keeps_todays_unbound_paths()
    {
        (SessionAggregate afterStart, ProcessingAttempt attempt, _) = Bound();

        CorrectionClosing.Unfinished(null, afterStart, attempt).ShouldBeNull();
        SucceededCorrectionClose succeeded = CorrectionClosing.Succeeded(
            null, afterStart, attempt, RevisionId.From(Guid.NewGuid()), DateTimeOffset.UnixEpoch);
        succeeded.MayHandOffAfterSuccess.ShouldBeTrue();
        succeeded.Changes.ShouldBeEmpty();
    }

    // -------------------------------------------------------------------------------------

    private static (SessionAggregate AfterStart, ProcessingAttempt Attempt, CorrectionRequest Request) Bound()
    {
        SessionId session = SessionId.From(Guid.NewGuid());
        RevisionId r = RevisionId.From(Guid.NewGuid());
        RevisionId u = RevisionId.From(Guid.NewGuid());
        ProcessingAttempt attempt = ProcessingAttempt.Start(AttemptId.From(Guid.NewGuid()), session, StepKind.BackgroundRemoval,
            u, OperationKind.ManualResultImport, "manual-result-import-v1", DateTimeOffset.UnixEpoch);
        CorrectionRequest request = new(Guid.NewGuid(), session, StepKind.BackgroundRemoval, r, Sha256.Parse(new string('1', 64)),
            u, Sha256.Parse(new string('2', 64)), WorkspaceDirRef.Create("Sessions/S_x/Correction/logo-1"),
            "ref.png", "work.png", "back.png", "Fix the hair", "Fix the hair", CorrectionRequestStatus.Ready,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, attempt.Id, null);
        ProcessingSession processing = ProcessingSession.Start(session, WorkflowType.PrepareAsset, OutputName.Parse("logo"),
            WorkspaceDirRef.Create("Sessions/S_x"), DateTimeOffset.UnixEpoch);
        SessionAggregate afterStart = new(processing, null, [], [], [attempt], [], []) { CorrectionRequests = [request] };
        return (afterStart, attempt, request);
    }

    private static SessionAggregate With(SessionAggregate aggregate, CorrectionRequest request) =>
        aggregate with { CorrectionRequests = [request] };

    private static WorkflowSnapshot At(WorkflowType type, StepKind kind, StepState state)
    {
        WorkflowSnapshot snapshot = WorkflowSnapshot.Create(SessionId.From(Guid.NewGuid()), type,
            OutputName.Parse("correct"), DateTimeOffset.UnixEpoch);
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

    /// <summary>The command a screen would send for the snapshot's own review, or stand-ins elsewhere.</summary>
    private static WorkflowCommand Ask(WorkflowSnapshot snapshot, string? note = "fix")
    {
        SessionStep? step = snapshot.Step(StepKind.BackgroundRemoval);
        (RevisionId Id, Sha256 Sha256)? input = snapshot.Definition.Contains(StepKind.BackgroundRemoval)
            ? snapshot.UpstreamResultOf(StepKind.BackgroundRemoval)
            : null;
        RevisionId fallback = RevisionId.From(Guid.NewGuid());
        Sha256 fallbackHash = Sha256.Parse(new string('e', 64));
        return new WorkflowCommand.RequestColleagueCorrection(Guid.NewGuid(),
            step?.CurrentRevisionId ?? fallback, step?.CurrentRevisionSha256 ?? fallbackHash,
            input?.Id ?? fallback, input?.Sha256 ?? fallbackHash, note);
    }

    private static WorkflowCommand Import(WorkflowSnapshot snapshot)
    {
        SessionStep? step = snapshot.Step(StepKind.BackgroundRemoval);
        return new WorkflowCommand.ImportCorrectedImage(Guid.NewGuid(),
            step?.CurrentRevisionId ?? RevisionId.From(Guid.NewGuid()),
            step?.CurrentRevisionSha256 ?? Sha256.Parse(new string('e', 64)),
            @"C:\return\corrected.png");
    }

    private static WorkflowTransition Apply(WorkflowSnapshot snapshot, WorkflowCommand command) =>
        WorkflowEngine.Instance.Apply(snapshot, command, new CommandContext(DateTimeOffset.UnixEpoch, "tester", default, default));
}
