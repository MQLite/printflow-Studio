using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Unit.Workflow;

/// <summary>
/// The attempt-history half of trim adjustment (SCRUM-11147): which source, which bounds, which
/// suggestion — and every refusal that stops a guess.
/// </summary>
public sealed class TrimAdjustmentEligibilityTests
{
    private static readonly TrimBounds Content = TrimBounds.FromEdges(1200, 800, 2800, 2200);
    private static readonly TrimBounds Applied = TrimBounds.FromEdges(1180, 780, 2820, 2220);

    [Fact]
    public void An_automatic_trim_under_review_is_adjusted_over_its_own_source_with_the_stored_suggestion()
    {
        Case c = new();

        TrimAdjustmentView view = c.Resolve().ShouldNotBeNull();

        view.ResultRevisionId.ShouldBe(c.R1.Id);
        view.ResultSha256.ShouldBe(c.R1.Sha256);
        view.PreTrimRevisionId.ShouldBe(c.U.Id, "the pre-trim source, never the cropped result");
        view.PreTrimSha256.ShouldBe(c.U.Sha256);
        (view.SourcePixelWidth, view.SourcePixelHeight).ShouldBe((4000, 3000));
        view.CurrentBounds.ShouldBe(Applied);
        view.AutomaticSuggestion.ShouldBe(Applied, "the stored applied rectangle, margin already included");
        view.AutomaticSuggestionAttemptId.ShouldBe(c.Automatic.Id);
    }

    [Fact]
    public void A_manual_result_under_review_keeps_referring_back_to_the_automatic_attempt_on_the_same_source()
    {
        Case c = new();
        TrimBounds kept = TrimBounds.FromEdges(1132, 780, 2820, 2125);
        c.ReplaceReviewWithManualCrop(kept);

        TrimAdjustmentView view = c.Resolve().ShouldNotBeNull();

        view.CurrentBounds.ShouldBe(kept);
        view.PreTrimRevisionId.ShouldBe(c.U.Id);
        view.AutomaticSuggestion.ShouldBe(Applied);
    }

    [Fact]
    public void Without_a_genuine_automatic_trim_of_this_source_there_is_no_suggestion()
    {
        Case c = new();
        c.ReplaceReviewWithManualCrop(TrimBounds.FromEdges(10, 10, 20, 20));
        c.Attempts.Remove(c.Automatic);

        c.Resolve().ShouldNotBeNull().AutomaticSuggestion.ShouldBeNull();
        c.Resolve()!.AutomaticSuggestionAttemptId.ShouldBeNull();
    }

    [Fact]
    public void The_newest_automatic_trim_of_the_source_is_the_suggestion()
    {
        Case c = new();
        TrimBounds wider = TrimBounds.FromEdges(1100, 700, 2900, 2300);
        ProcessingAttempt newer = c.AutomaticAttempt(c.NewRevision(OperationKind.Trim).Id, wider, retrySequence: 3);
        c.ReplaceReviewWithManualCrop(TrimBounds.FromEdges(10, 10, 20, 20));
        c.Attempts.Add(newer);

        c.Resolve()!.AutomaticSuggestion.ShouldBe(wider);
    }

    [Fact]
    public void A_full_canvas_automatic_result_is_a_genuine_suggestion()
    {
        Case c = new(applied: TrimBounds.Canvas(4000, 3000));

        c.Resolve()!.AutomaticSuggestion.ShouldBe(TrimBounds.Canvas(4000, 3000));
    }

    public static TheoryData<string> Refusals() =>
    [
        "descendant revision", "sourced print output", "invalid source", "released source", "invalid review",
        "lineage mismatch", "attempt input mismatch", "psd source", "colleague import", "geometry missing",
        "hash mismatch", "not current", "handed off",
    ];

    [Theory]
    [MemberData(nameof(Refusals))]
    public void Anything_that_would_need_a_guess_or_an_invalidation_is_refused(string refusal)
    {
        Case c = new();
        switch (refusal)
        {
            case "descendant revision":
                c.Revisions.Add(Revision.Create(RevisionId.From(Guid.NewGuid()), c.Session, c.R1.Id,
                    OperationKind.PromoteApproved, File("child.png"), Facts(1640, 1440, 'C'), DateTimeOffset.UnixEpoch));
                break;
            case "sourced print output":
                c.Outputs.Add(PrintOutput.Create(PrintOutputId.From(Guid.NewGuid()), c.Session, c.R1.Id,
                    PrintDimensions.FromMillimetres(100d, 100d, SizePreset.Custom), WhiteUnderbaseBranch.W1_1px,
                    new ProductionPresetRef("preset", "1", Sha256.Parse(new string('D', 64))),
                    File("out.tif"), 10, Sha256.Parse(new string('D', 64)), DateTimeOffset.UnixEpoch));
                break;
            case "invalid source":
                c.Replace(c.U, c.U with { IsValid = false, InvalidationReason = InvalidationReason.FileMutated });
                break;
            case "released source":
                c.Replace(c.U, c.U with { RetentionReleasedAtUtc = DateTimeOffset.UnixEpoch });
                break;
            case "invalid review":
                c.Replace(c.R1, c.R1 with { IsValid = false, InvalidationReason = InvalidationReason.UpstreamChanged });
                break;
            case "lineage mismatch":
                c.Replace(c.R1, c.R1 with { SourceRevisionId = RevisionId.From(Guid.NewGuid()) });
                break;
            case "attempt input mismatch":
                c.Attempts[c.Attempts.IndexOf(c.Automatic)] = c.Automatic with { InputRevisionId = RevisionId.From(Guid.NewGuid()) };
                break;
            case "psd source":
                c.Replace(c.U, c.U with { Facts = c.U.Facts with { Format = ImageFormat.Psd } });
                break;
            case "colleague import":
                c.Attempts[c.Attempts.IndexOf(c.Automatic)] = ProcessingAttempt.Start(c.Automatic.Id, c.Session, StepKind.Trim,
                        c.U.Id, OperationKind.ManualResultImport, "manual", DateTimeOffset.UnixEpoch)
                    with { Status = AttemptStatus.Succeeded, OutputRevisionId = c.R1.Id, EndedAtUtc = DateTimeOffset.UnixEpoch };
                break;
            case "geometry missing":
                c.Attempts[c.Attempts.IndexOf(c.Automatic)] = c.Automatic with { TrimGeometry = null };
                break;
            case "hash mismatch":
                c.State = c.State.WithStep(c.State.Step(StepKind.Trim)! with { CurrentRevisionSha256 = Sha256.Parse(new string('F', 64)) });
                break;
            case "not current":
                c.State = c.State.WithStep(c.State.Step(StepKind.Trim)! with { State = StepState.Approved });
                break;
            case "handed off":
                c.State = c.State with { SessionState = SessionState.HandedOff };
                break;
        }

        c.Resolve().ShouldBeNull(refusal);
    }

    private static WorkspaceFileRef File(string name) => WorkspaceFileRef.Create("w/" + name, WorkspaceArea.Working);

    private static FileFacts Facts(int width, int height, char hash) =>
        new(ImageFormat.Png, 100, Sha256.Parse(new string(hash, 64)), width, height, 72, 72, ColourMode.Rgb, true);

    /// <summary>
    /// PREPARE_ASSET with Enhancement and Background removal skipped: U is the imported original,
    /// R1 the automatic trim of it, under review.
    /// </summary>
    private sealed class Case
    {
        private int _hash = 1;

        public SessionId Session { get; } = SessionId.From(Guid.NewGuid());
        public WorkflowSnapshot State { get; set; }
        public List<Revision> Revisions { get; } = [];
        public List<ProcessingAttempt> Attempts { get; } = [];
        public List<PrintOutput> Outputs { get; } = [];
        public Revision U { get; private set; }
        public Revision R1 { get; private set; }
        public ProcessingAttempt Automatic { get; }

        public Case(TrimBounds? applied = null)
        {
            U = Revision.Create(RevisionId.From(Guid.NewGuid()), Session, null, OperationKind.Import,
                File("source.png"), Facts(4000, 3000, 'A'), DateTimeOffset.UnixEpoch);
            Revisions.Add(U);
            R1 = NewRevision(OperationKind.Trim);
            Automatic = AutomaticAttempt(R1.Id, applied ?? Applied, retrySequence: 0);
            Attempts.Add(Automatic);

            WorkflowSnapshot snapshot = WorkflowSnapshot.Create(Session, WorkflowType.PrepareAsset,
                OutputName.Parse("adjust"), DateTimeOffset.UnixEpoch);
            State = snapshot with
            {
                Steps = snapshot.Steps.Select(step => step.Step switch
                {
                    StepKind.Import => step with { State = StepState.Approved, CurrentRevisionId = U.Id, CurrentRevisionSha256 = U.Sha256 },
                    StepKind.OriginalConfirmation => step with { State = StepState.Approved },
                    StepKind.Enhancement or StepKind.BackgroundRemoval => step with { State = StepState.Skipped },
                    StepKind.Trim => step with { State = StepState.ReviewRequired, CurrentRevisionId = R1.Id, CurrentRevisionSha256 = R1.Sha256 },
                    _ => step,
                }).ToArray(),
            };
        }

        public Revision NewRevision(OperationKind operation)
        {
            Revision revision = Revision.Create(RevisionId.From(Guid.NewGuid()), Session, U.Id, operation,
                File($"r{_hash}.png"), Facts(1640, 1440, (char)('0' + _hash++)), DateTimeOffset.UnixEpoch);
            Revisions.Add(revision);
            return revision;
        }

        public ProcessingAttempt AutomaticAttempt(RevisionId output, TrimBounds applied, int retrySequence) =>
            (ProcessingAttempt.Start(AttemptId.From(Guid.NewGuid()), Session, StepKind.Trim, U.Id, OperationKind.Trim,
                    "trim", DateTimeOffset.UnixEpoch, retrySequence: retrySequence)
                with { Status = AttemptStatus.Succeeded, OutputRevisionId = output, EndedAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(retrySequence) })
            .WithTrimGeometry(TrimGeometry.Create(
                applied.Left <= Content.Left && applied.Top <= Content.Top ? Content : applied, applied));

        /// <summary>Puts a manual crop of U under review instead of the automatic result.</summary>
        public void ReplaceReviewWithManualCrop(TrimBounds kept)
        {
            Revision manual = NewRevision(OperationKind.ManualImport);
            Attempts.Add((ProcessingAttempt.Start(AttemptId.From(Guid.NewGuid()), Session, StepKind.Trim, U.Id,
                        OperationKind.ManualImport, "crop", DateTimeOffset.UnixEpoch, retrySequence: 1)
                    with { Status = AttemptStatus.Succeeded, OutputRevisionId = manual.Id, EndedAtUtc = DateTimeOffset.UnixEpoch.AddMinutes(1) })
                .WithManualCropGeometry(ManualCropGeometry.Create(kept, ManualCropMargin.Tight, 4000, 3000)));
            R1 = manual;
            State = State.WithStep(State.Step(StepKind.Trim)! with { CurrentRevisionId = manual.Id, CurrentRevisionSha256 = manual.Sha256 });
        }

        public void Replace(Revision old, Revision replacement)
        {
            Revisions[Revisions.IndexOf(old)] = replacement;
            if (old.Id == U.Id) U = replacement;
            if (old.Id == R1.Id) R1 = replacement;
        }

        public TrimAdjustmentView? Resolve() => TrimAdjustmentEligibility.Resolve(State, Revisions, Attempts, Outputs);
    }
}
