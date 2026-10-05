using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;

namespace PrintFlow.Tests.Unit.AiDesign;

public sealed class AiDesignTransitionsTests
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);
    internal static AiDesignRequest Draft(params AiDesignAttachment[] attachments) => new()
    {
        RequestId = Guid.NewGuid(), WorkspaceIdentity = "test-root", Token = "draft-token", Prompt = new("subject", 1,
            new Dictionary<string, string> { ["subject"] = "猫\n\"literal\"" }, "edited literal prompt", [], "one downloadable image"),
        Attachments = attachments, CreatedAtUtc = Now, UpdatedAtUtc = Now
    };
    internal static AiDesignAttachment Attachment(Guid request, int order) => new(Guid.NewGuid(), order, "same.png",
        $"attachment-{order:D2}.png", "reference", "image/png", 3, Sha256.Parse(new string('A', 64)), new(request, $"Attachments/{Guid.NewGuid():N}.png"));
    internal static AiDesignRequest Queue(AiDesignRequest r) => AiDesignTransitions.Apply(r,
        new AiDesignEvent.Queued(Guid.NewGuid(), "attempt-token", 1, 1, Guid.NewGuid()), Now).Value;
    internal static AiDesignRequest Step(AiDesignRequest r, AiDesignEvent e) => AiDesignTransitions.Apply(r, e, Now).Value;

    [Fact]
    public void Retry_revokes_old_candidate_and_importing_failure_cannot_become_prompt_only()
    {
        var r = Queue(Draft()); var candidate = new AiDesignCandidate(new(r.RequestId, "Candidates/old.png"),
            new(ImageFormat.Png, 1, Sha256.Parse(new string('C',64)), 1, 1, null, null, ColourMode.Rgb, true));
        r = r with { State = AiDesignState.Validating, Candidate = candidate };
        var retry = Queue(Step(r, new AiDesignEvent.StopAccepted()));
        retry.Candidate.ShouldBeNull();
        AiDesignTransitions.Apply(retry with { State = AiDesignState.Validating }, new AiDesignEvent.ImportStarted(), Now).IsFailure.ShouldBeTrue();
        var importing = Step(r, new AiDesignEvent.ImportStarted());
        Step(importing, new AiDesignEvent.Failed("AdapterLost")).State.ShouldBe(AiDesignState.ImportFailed);
    }

    [Fact]
    public void Requires_every_upload_before_submit()
    {
        var draft = Draft(); draft = draft with { Attachments = [Attachment(draft.RequestId, 1), Attachment(draft.RequestId, 2)] };
        var r = Step(Queue(draft), new AiDesignEvent.UploadStarted("tab", "conversation"));
        AiDesignTransitions.Apply(r, new AiDesignEvent.SubmitReserved("tab", "conversation"), Now).IsFailure.ShouldBeTrue();
        r = Step(r, new AiDesignEvent.UploadConfirmed(new(r.Attachments[0].Id, r.AttemptId, "conversation", AiDesignUploadStatus.Uploaded, "receipt-1")));
        AiDesignTransitions.Apply(r, new AiDesignEvent.SubmitReserved("tab", "conversation"), Now).IsFailure.ShouldBeTrue();
        r = Step(r, new AiDesignEvent.UploadConfirmed(new(r.Attachments[1].Id, r.AttemptId, "conversation", AiDesignUploadStatus.Uploaded, "receipt-2")));
        var reserved = Step(r, new AiDesignEvent.SubmitReserved("tab", "conversation"));
        reserved.SubmissionReservedAtUtc.ShouldBe(Now);
        AiDesignTransitions.Apply(reserved, new AiDesignEvent.SubmitReserved("tab", "conversation"), Now).IsFailure.ShouldBeTrue();
        Step(reserved, new AiDesignEvent.SubmitConfirmed()).State.ShouldBe(AiDesignState.Submitted);
    }

    [Fact]
    public void Stop_and_import_have_one_winner_and_keep_candidate()
    {
        var r = Queue(Draft()) with { State = AiDesignState.Validating, Candidate = new(new(Guid.NewGuid(), "Candidates/one.png"),
            new(ImageFormat.Png, 1, Sha256.Parse(new string('B', 64)), 1, 1, null, null, ColourMode.Rgb, true)) };
        var cancelled = Step(r, new AiDesignEvent.StopAccepted());
        cancelled.State.ShouldBe(AiDesignState.Cancelled); cancelled.Token.ShouldNotBe(r.Token);
        cancelled.Candidate.ShouldBe(r.Candidate);
        AiDesignTransitions.Apply(cancelled, new AiDesignEvent.ImportStarted(), Now).IsFailure.ShouldBeTrue();
        var importing = Step(r, new AiDesignEvent.ImportStarted());
        AiDesignTransitions.Apply(importing, new AiDesignEvent.StopAccepted(), Now).IsFailure.ShouldBeTrue();
        var session = SessionId.From(Guid.NewGuid());
        var failed = Step(importing, new AiDesignEvent.Failed("ImportFailed", true, session));
        failed.State.ShouldBe(AiDesignState.ImportFailed); failed.ImportedSessionId.ShouldBe(session); failed.Candidate.ShouldBe(r.Candidate);
    }

    [Theory]
    [InlineData(AiDesignState.Uploading, 5)]
    [InlineData(AiDesignState.Generating, 10)]
    [InlineData(AiDesignState.Receiving, 2)]
    public void Deadline_rejects_late_work_preserving_input(AiDesignState state, int minutes)
    {
        var r = Queue(Draft()) with { State = state, DeadlineUtc = Now.AddMinutes(minutes) };
        var result = AiDesignTransitions.Apply(r, new AiDesignEvent.Failed("Timeout"), Now.AddMinutes(minutes));
        result.IsSuccess.ShouldBeTrue(); result.Value.State.ShouldBe(AiDesignState.PromptOnly); result.Value.Prompt.Text.ShouldBe("edited literal prompt");
    }

    [Fact]
    public void Restart_preserves_inputs_without_requeue_and_retry_clears_receipts()
    {
        var r = Queue(Draft()) with { State = AiDesignState.Generating, SubmissionReservedAtUtc = Now,
            UploadReceipts = [new(Guid.NewGuid(), Guid.NewGuid(), "old", AiDesignUploadStatus.Uploaded, "evidence")] };
        var interrupted = Step(r, new AiDesignEvent.Interrupted());
        interrupted.State.ShouldBe(AiDesignState.Interrupted); interrupted.Prompt.ShouldBe(r.Prompt);
        interrupted.SubmissionReservedAtUtc.ShouldBe(Now);
        var retry = Queue(interrupted); retry.AttemptId.ShouldNotBe(r.AttemptId); retry.UploadReceipts.ShouldBeEmpty(); retry.SubmissionReservedAtUtc.ShouldBeNull();
    }
}
