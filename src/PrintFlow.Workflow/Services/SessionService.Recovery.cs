using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Workflow.Services;

public enum RecoveryAction { Restart, ManualResult, Abandon }

/// <summary>Persisted identity and engine-authorised actions; contains no UI text or external paths.</summary>
public sealed record RecoveryItem(SessionId Id, OutputName OutputName, WorkflowType Workflow,
    StepKind Step, StepState StepState, SessionState State, DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<RecoveryAction> Actions)
{
    public bool HasUnfinishedManualImport => StepState == StepState.Processing;
}

public sealed partial class SessionService
{
    public async Task<OperationResult<IReadOnlyList<RecoveryItem>>> ListRecoveryAsync(CancellationToken cancellationToken)
    {
        var candidates = await _repository.FindRecoveryCandidatesAsync(cancellationToken);
        if (candidates.IsFailure) return OperationResult.Fail<IReadOnlyList<RecoveryItem>>(candidates.Failure);
        List<RecoveryItem> items = [];
        foreach (SessionId id in candidates.Value)
        {
            var loaded = await _repository.LoadAsync(id, cancellationToken);
            if (loaded.IsFailure) return OperationResult.Fail<IReadOnlyList<RecoveryItem>>(loaded.Failure);
            if (loaded.Value is { } aggregate && RecoveryOf(aggregate) is { } item) items.Add(item);
        }
        return OperationResult.Ok<IReadOnlyList<RecoveryItem>>(items
            .OrderByDescending(item => item.UpdatedAtUtc).ThenBy(item => item.Id.ToString()).ToList());
    }

    private RecoveryItem? RecoveryOf(SessionAggregate aggregate)
    {
        WorkflowSnapshot state = aggregate.ToSnapshot(ConfiguredRecommendations());
        if (state.SessionState is not (SessionState.Active or SessionState.HandedOff) ||
            state.CurrentStep is not { State: StepState.Interrupted or StepState.Failed or StepState.RetryRequired or StepState.Processing } step ||
            !HasUnresolvedInterruption(aggregate, step.Step)) return null;

        IReadOnlyList<CommandKind> commands = _engine.AvailableCommands(state);
        // A persisted Running importer may still be live, or its closing commit may have failed.
        // Persistence alone cannot distinguish them. Keep it visible, but require startup repair
        // before recovery can mutate it; even engine-level Abandon would be unsafe during import.
        if (step.State == StepState.Processing) commands = [];
        List<RecoveryAction> actions = [];
        if (commands.Contains(CommandKind.Retry) || commands.Contains(CommandKind.ReenterAutomation))
            actions.Add(RecoveryAction.Restart);
        // Probe the real HandOff transition; only its accepted resulting state can grant import.
        WorkflowSnapshot manualState = state;
        if (commands.Contains(CommandKind.HandOff))
        {
            var handoff = _engine.Apply(state, new WorkflowCommand.HandOff(step.Step, RecoveryManualReason),
                new CommandContext(DateTimeOffset.UnixEpoch, CommandContext.UnknownOperator, default, default));
            if (handoff.IsAccepted) manualState = handoff.State;
        }
        if (_manualResults is not null && ManualResultEligibility.CanSubmit(manualState) &&
            _engine.AvailableCommands(manualState).Contains(CommandKind.SubmitManualResult))
            actions.Add(RecoveryAction.ManualResult);
        if (commands.Contains(CommandKind.AbandonSession)) actions.Add(RecoveryAction.Abandon);
        return new RecoveryItem(aggregate.Session.Id, aggregate.Session.OutputName, state.WorkflowType,
            step.Step, step.State, state.SessionState, aggregate.Session.UpdatedAtUtc, actions);
    }

    private const string RecoveryManualReason = "Operator chose a manually saved result after interruption.";

    // Failed imports and imports whose terminal metadata did not commit keep the interruption
    // unresolved. A still-Processing entry exposes no recovery mutations while the import
    // could still be live. An ordinary retry or a successful import resolves the interruption,
    // so a later unrelated failure/rejection must not resurrect the old card.
    private static bool HasUnresolvedInterruption(SessionAggregate aggregate, StepKind step)
    {
        var attempts = aggregate.Attempts.Where(a => a.Step == step).ToDictionary(a => a.Id);
        ProcessingAttempt? current = attempts.Values.OrderByDescending(a => a.RetrySequence)
            .ThenByDescending(a => a.StartedAtUtc).FirstOrDefault();
        for (int remaining = attempts.Count; current is not null && remaining > 0; remaining--)
        {
            if (current.Status == AttemptStatus.Interrupted) return true;
            if (current.Operation != PrintFlow.Domain.Revisions.OperationKind.ManualResultImport || current.Status is not (AttemptStatus.Failed or AttemptStatus.Running) ||
                current.RetryOfAttemptId is not { } previous) return false;
            attempts.TryGetValue(previous, out current);
        }
        return false;
    }

    public async Task<OperationResult<SessionView>> ResolveRecoveryAsync(SessionId id, RecoveryAction action,
        string? selectedPath, string? operatorName, CancellationToken cancellationToken)
    {
        var loaded = await _repository.LoadAsync(id, cancellationToken);
        if (loaded.IsFailure) return OperationResult.Fail<SessionView>(loaded.Failure);
        if (loaded.Value is not { } aggregate || RecoveryOf(aggregate) is not { } item || !item.Actions.Contains(action))
            return OperationResult.Fail<SessionView>(FailureCode.PreconditionNotMet, "This recovery action is no longer available.");

        if (action == RecoveryAction.ManualResult)
        {
            if (string.IsNullOrWhiteSpace(selectedPath))
                return OperationResult.Fail<SessionView>(FailureCode.PreconditionNotMet, "Choose a saved result first.");
            if (item.State == SessionState.Active)
            {
                var handedOff = await ExecuteAsync(id, new WorkflowCommand.HandOff(item.Step, RecoveryManualReason), operatorName, cancellationToken);
                if (handedOff.IsFailure) return handedOff;
            }
            return await ExecuteAsync(id, new WorkflowCommand.SubmitManualResult(item.Step, selectedPath), operatorName, cancellationToken);
        }
        WorkflowCommand command = action == RecoveryAction.Abandon
            ? new WorkflowCommand.AbandonSession("Abandoned by the operator from Home recovery.")
            : item.State == SessionState.HandedOff ? new WorkflowCommand.ReenterAutomation() : new WorkflowCommand.Retry(item.Step);
        return await ExecuteAsync(id, command, operatorName, cancellationToken);
    }
}
