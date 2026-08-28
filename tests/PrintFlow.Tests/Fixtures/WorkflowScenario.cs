using Microsoft.Extensions.Time.Testing;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Effects;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// Drives the pure engine over a synthetic snapshot.
/// </summary>
/// <remarks>
/// No file, no database, no adapter. Time and identifiers are deterministic, so a failing
/// assertion always points at a workflow rule rather than at the environment.
/// </remarks>
internal sealed class WorkflowScenario
{
    private static readonly DateTimeOffset Epoch = new(2026, 8, 18, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Epoch);
    private readonly SequentialIdGenerator _ids = new();
    private readonly IWorkflowEngine _engine = WorkflowEngine.Instance;

    private WorkflowScenario(WorkflowType type)
    {
        State = WorkflowSnapshot.Create(
            SessionId.From(Guid.CreateVersion7()),
            type,
            OutputName.Parse("test-artwork"),
            Epoch);
    }

    /// <summary>The current state after everything applied so far.</summary>
    public WorkflowSnapshot State { get; private set; }

    /// <summary>The result of the most recent <see cref="Apply"/>.</summary>
    public WorkflowTransition LastTransition { get; private set; } = WorkflowTransition.Accepted(null!);

    public static WorkflowScenario For(WorkflowType type) => new(type);

    /// <summary>Applies a command and, when accepted, adopts the new state.</summary>
    public WorkflowTransition Apply(WorkflowCommand command)
    {
        _clock.Advance(TimeSpan.FromSeconds(1));

        CommandContext context = CommandContext.Create(_clock, _ids, "test-operator");
        WorkflowTransition transition = _engine.Apply(State, command, context);
        LastTransition = transition;

        if (transition.IsAccepted)
        {
            State = transition.State;
        }

        return transition;
    }

    /// <summary>Applies a command that is expected to succeed, failing the test if it does not.</summary>
    public WorkflowScenario Must(WorkflowCommand command)
    {
        WorkflowTransition transition = Apply(command);
        transition.IsAccepted.ShouldBeTrue(
            $"{command.Kind} should have been accepted but was rejected: {transition.Rejection}");
        return this;
    }

    /// <summary>Forces the snapshot into a specific shape so one rule can be tested in isolation.</summary>
    public WorkflowScenario WithState(Func<WorkflowSnapshot, WorkflowSnapshot> mutate)
    {
        State = mutate(State);
        return this;
    }

    /// <summary>Puts one step directly into a state, bypassing the engine.</summary>
    public WorkflowScenario ForceStep(
        StepKind kind, StepState state, RevisionId? revision = null, Sha256? hash = null)
    {
        SessionStep step = State.Step(kind)
            ?? throw new InvalidOperationException($"Workflow {State.WorkflowType} has no step {kind}.");

        State = State.WithStep(step with
        {
            State = state,
            CurrentRevisionId = revision ?? step.CurrentRevisionId,
            CurrentRevisionSha256 = hash ?? step.CurrentRevisionSha256,
            EnteredStateAtUtc = _clock.GetUtcNow(),
        });

        return this;
    }

    /// <summary>Runs Import through to Approved and returns its revision.</summary>
    public RevisionId CompleteImport()
    {
        Must(new WorkflowCommand.StartStep(StepKind.Import));
        RevisionId root = NextRevision();
        Must(SystemCommands.Succeeded(NextAttempt(), StepKind.Import, root, HashOf(root)));
        return root;
    }

    /// <summary>Runs a producing step through start, success and approval.</summary>
    /// <remarks>
    /// Background Removal is authorised first, because since Epic 11300 Part C2B1 it cannot be
    /// started without an explicit reviewed-content decision (§7). Doing it here rather than in
    /// every caller keeps this helper meaning "run this step normally" -- the authorisation is
    /// part of what running Background Removal normally now involves, not an extra the tests
    /// invented. Tests about the decision itself issue the command directly.
    /// </remarks>
    public RevisionId CompleteStep(StepKind kind)
    {
        if (kind == StepKind.BackgroundRemoval)
        {
            AuthoriseBackgroundRemoval();
        }

        Must(new WorkflowCommand.StartStep(kind));
        RevisionId produced = NextRevision();
        Sha256 hash = HashOf(produced);
        Must(SystemCommands.Succeeded(NextAttempt(), kind, produced, hash));

        if (State.Step(kind)!.State == StepState.ReviewRequired)
        {
            Must(new WorkflowCommand.Approve(kind, hash));
        }

        return produced;
    }

    /// <summary>
    /// Records the reviewed-content authority for whatever Background Removal will currently
    /// consume (Epic 11300 Part C2B1 §5).
    /// </summary>
    /// <remarks>
    /// Reads the upstream result from the snapshot rather than taking it as an argument, so it
    /// authorises the artefact actually on offer and can never accidentally authorise a stale
    /// one. A test that wants to prove stale authority is refused issues the command itself with
    /// the id and hash it means.
    /// </remarks>
    public WorkflowScenario AuthoriseBackgroundRemoval()
    {
        (RevisionId Id, Sha256 Sha256) upstream = State.UpstreamResultOf(StepKind.BackgroundRemoval)
            ?? throw new InvalidOperationException(
                "BackgroundRemoval has no upstream result to authorise.");

        return Must(new WorkflowCommand.SetBackgroundRemovalDecision(
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
            upstream.Id,
            upstream.Sha256));
    }

    /// <summary>
    /// Records maximum bounds and the source-bound plan they produce, for whatever Photoshop
    /// output will currently consume (Epic 11400 Part B1A.2A §5, §14).
    /// </summary>
    /// <remarks>
    /// The engine half and the service half, in the order production performs them. The engine
    /// accepts the millimetres and stamps the semantics; the plan itself is calculated from the
    /// source's pixels, which a pure snapshot does not carry — in production <c>SessionService</c>
    /// reads them off the upstream Revision's <c>FileFacts</c>, and here they are supplied.
    /// <para>
    /// It goes through <see cref="PrintPreparationPlan.For"/> rather than assembling a record, so
    /// even a fixture cannot invent a limiting edge the one domain calculator would not have
    /// chosen (§6). The upstream pair is read from the snapshot rather than taken as an argument,
    /// so it always binds the artefact actually on offer; a test proving a stale plan is refused
    /// constructs one itself with the id and hash it means.
    /// </para>
    /// <para>
    /// The default source is 2000×1500 px, comfortably inside <see cref="A4Portrait"/>, so the
    /// ordinary path is a resolution-only plan and nothing is resampled. Tests about shrinking
    /// pass their own.
    /// </para>
    /// </remarks>
    public WorkflowScenario RecordMaximumBounds(
        PrintDimensions? limits = null, int sourcePixelWidth = 2000, int sourcePixelHeight = 1500)
    {
        (RevisionId Id, Sha256 Sha256) upstream = State.UpstreamResultOf(StepKind.PhotoshopOutput)
            ?? throw new InvalidOperationException(
                "PhotoshopOutput has no upstream result to fit bounds against.");

        PrintDimensions bounds = limits ?? A4Portrait;
        Must(new WorkflowCommand.SetPrintDimensions(bounds));

        State = State with
        {
            PrintPreparationPlan = PrintPreparationPlan.For(
                upstream.Id, upstream.Sha256, sourcePixelWidth, sourcePixelHeight, bounds),
        };

        return this;
    }

    public StepState StateOf(StepKind kind) =>
        State.Step(kind)?.State
        ?? throw new InvalidOperationException($"Workflow {State.WorkflowType} has no step {kind}.");

    public RevisionId NextRevision() => RevisionId.From(_ids.NewId());

    public AttemptId NextAttempt() => AttemptId.From(_ids.NewId());

    /// <summary>A deterministic synthetic hash for a revision; never a real file digest.</summary>
    public static Sha256 HashOf(RevisionId revision) =>
        Sha256.Parse(revision.Value.ToString("N") + revision.Value.ToString("N"));

    /// <summary>A hash that belongs to no revision, for stale-approval tests.</summary>
    public static Sha256 ForeignHash { get; } = Sha256.Parse(new string('A', 64));

    public static PrintDimensions A4Portrait { get; } =
        PrintDimensions.FromMillimetres(198, 280, SizePreset.A4);

    /// <summary>Counts effects of a given type in the last transition.</summary>
    public int EffectCount<T>() where T : WorkflowEffect
    {
        int count = 0;
        foreach (WorkflowEffect effect in LastTransition.Effects)
        {
            if (effect is T)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Returns the single effect of the given type from the last transition.</summary>
    public T Effect<T>() where T : WorkflowEffect
    {
        foreach (WorkflowEffect effect in LastTransition.Effects)
        {
            if (effect is T typed)
            {
                return typed;
            }
        }

        throw new InvalidOperationException(
            $"No {typeof(T).Name} effect was produced. Effects: " +
            (LastTransition.Effects.Count == 0
                ? "(none)"
                : string.Join(", ", LastTransition.Effects.Select(e => e.Kind))));
    }
}

/// <summary>Deterministic, ordered identifiers so a failure is always reproducible.</summary>
internal sealed class SequentialIdGenerator : IIdGenerator
{
    private int _next;

    public Guid NewId()
    {
        _next++;
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, _next);
        return new Guid(bytes);
    }
}

/// <summary>
/// Constructs system commands, which have internal constructors so the UI cannot raise them.
/// </summary>
internal static class SystemCommands
{
    public static WorkflowCommand Succeeded(
        AttemptId attempt, StepKind step, RevisionId revision, Sha256 hash) =>
        new WorkflowCommand.System.AttemptSucceeded(attempt, step, revision, hash);

    public static WorkflowCommand Failed(AttemptId attempt, StepKind step, FailureCode code) =>
        new WorkflowCommand.System.AttemptFailed(
            attempt, step, OperationFailure.Create(code, $"synthetic {code}", isRetryable: true));

    public static WorkflowCommand Interrupted(AttemptId attempt, StepKind step) =>
        new WorkflowCommand.System.AttemptInterrupted(attempt, step);

    /// <summary>
    /// A human stopping a running attempt, with the audit context the workflow layer writes
    /// (Epic 11300 Part D2A §12, §29).
    /// </summary>
    /// <remarks>
    /// Carries the real context keys rather than an empty dictionary, so a test that asserts on
    /// the persisted audit is asserting against the same shape production writes.
    /// </remarks>
    public static WorkflowCommand Cancelled(
        AttemptId attempt,
        StepKind step,
        AutomationStopMode mode = AutomationStopMode.StopOperation,
        ExternalOperationPhase phase = ExternalOperationPhase.Busy,
        bool cancelInvoked = false) =>
        new WorkflowCommand.System.AttemptCancelled(
            attempt,
            step,
            OperationFailure.Create(
                FailureCode.Cancelled,
                $"synthetic operator {mode} at {phase}",
                isRetryable: true,
                context: new Dictionary<string, string>
                {
                    [AutomationStopAudit.StopRequestedKey] = "true",
                    [AutomationStopAudit.ModeKey] = mode.ToString(),
                    [AutomationStopAudit.PhaseKey] = phase.ToString(),
                    [AutomationStopAudit.CancelInvokedKey] = cancelInvoked ? "true" : "false",
                    [AutomationStopAudit.RetainedKey] =
                        AutomationStopPolicy.RetainedFor(phase, cancelInvoked).ToString(),
                }));
}
