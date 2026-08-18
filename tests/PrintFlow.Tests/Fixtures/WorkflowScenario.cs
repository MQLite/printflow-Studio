using Microsoft.Extensions.Time.Testing;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Effects;
using PrintFlow.Workflow.Engine;

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
    public RevisionId CompleteStep(StepKind kind)
    {
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
}
