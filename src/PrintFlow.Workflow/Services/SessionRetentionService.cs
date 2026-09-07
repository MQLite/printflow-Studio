using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Workflow.Services;

/// <summary>Post-completion maintenance result, separate from workflow success.</summary>
public sealed record SessionCleanupResult(
    int PromotedCount, int DeletedCount, int PreservedCount,
    IReadOnlyList<string> Warnings, OperationFailure? Failure)
{
    public bool IsComplete => Failure is null;
}

/// <summary>
/// One orchestration boundary shared by the completion interpreter and startup recovery.
/// Copy + verify all files, commit locations/expiry atomically, re-read, then delete only
/// classified redundant copies. A failure always leaves the completed workflow completed.
/// </summary>
public sealed class SessionRetentionService(ISessionRepository repository, IWorkspace workspace, TimeProvider timeProvider)
{
    public async Task<SessionCleanupResult> CleanupAsync(SessionId id, CancellationToken cancellationToken)
    {
        using IDisposable lease = await SessionCompletionGate.EnterAsync(id, cancellationToken);
        return await CleanupUnderCompletionGateAsync(id, cancellationToken);
    }

    internal async Task<SessionCleanupResult> CleanupUnderCompletionGateAsync(SessionId id, CancellationToken cancellationToken)
    {
        int promoted = 0;
        List<string> warnings = [];
        OperationResult<SessionAggregate?> read = await repository.LoadAsync(id, cancellationToken);
        if (read.IsFailure) return Failed(read.Failure);
        if (read.Value is not { } aggregate)
            return Failed(OperationFailure.Create(FailureCode.PreconditionNotMet, "Retention session does not exist."));

        OperationResult<AutomationLockState> automation = await repository.GetAutomationLockAsync(cancellationToken);
        if (automation.IsFailure) return Failed(automation.Failure);
        if (automation.Value.SessionId == id)
            return Failed(OperationFailure.Create(FailureCode.PreconditionNotMet, "Retention refused an automation lock holder."));

        OperationResult<IReadOnlyList<WorkingFileEntry>> listed = workspace.ListWorkingFiles(aggregate.Session.Workspace);
        if (listed.IsFailure) return Failed(listed.Failure);
        OperationResult<SessionRetentionPlan> planned = SessionRetentionPlan.Create(aggregate, listed.Value);
        if (planned.IsFailure) return Failed(planned.Failure);
        SessionRetentionPlan plan = planned.Value;
        warnings.AddRange(plan.Unclassified.Select(f => $"Unclassified managed file preserved: {f.RelativePath}"));
        OperationResult<Unit> verified = workspace.VerifyRetentionFiles(aggregate.Session.Workspace, plan.Required);
        if (verified.IsFailure) return Failed(verified.Failure);

        List<RevisionRetentionChange> changes = [];
        List<PrintOutput> outputs = [];
        foreach (Revision revision in plan.Promote)
        {
            OperationResult<WorkspaceFileRef> copied = await workspace.PromoteRevisionAsync(
                aggregate.Session.Workspace, revision.Id, new(revision.File, revision.Sha256), cancellationToken);
            if (copied.IsFailure) return Failed(copied.Failure);
            changes.Add(new(revision.Id, revision.File, revision.Sha256, copied.Value, null));
            foreach (PrintOutput output in aggregate.Outputs.Where(o =>
                o.RecycledAtUtc is null && o.File == revision.File))
                outputs.Add(output with { File = copied.Value });
        }
        changes.AddRange(plan.Release.Select(r => new RevisionRetentionChange(
            r.Id, r.File, r.Sha256, null, timeProvider.GetUtcNow())));

        // Even a deletion-only retry re-proves eligibility transactionally. No session upsert,
        // lock mutation, or arbitrary JSON cleanup state is involved.
        OperationResult<Unit> committed = await repository.CommitAsync(SessionMutation.Empty(aggregate.Session) with
        {
            IsRetentionMaintenance = true, RevisionRetentionChanges = changes, UpsertOutputs = outputs,
        }, cancellationToken);
        if (committed.IsFailure) return Failed(committed.Failure);
        promoted = changes.Count(c => c.PromotedFile is not null);

        read = await repository.LoadAsync(id, cancellationToken);
        if (read.IsFailure) return Failed(read.Failure);
        if (read.Value is not { } after)
            return Failed(OperationFailure.Create(FailureCode.PersistenceError, "Retention readback failed."));
        OperationResult<SessionRetentionPlan> remaining = SessionRetentionPlan.Create(after, listed.Value);
        if (remaining.IsFailure) return Failed(remaining.Failure);
        // Replanning from committed metadata is also the restart algorithm. FormerWorkingFile
        // remembers exactly which promoted copy may still be present after a crash.
        OperationResult<WorkingCleanupResult> cleaned = workspace.CleanupWorking(after.Session.Workspace,
            new(remaining.Value.Required, remaining.Value.Delete));
        return cleaned.IsFailure ? Failed(cleaned.Failure) : new SessionCleanupResult(
            promoted, cleaned.Value.DeletedCount, cleaned.Value.PreservedCount, warnings, null);

        SessionCleanupResult Failed(OperationFailure failure)
        {
            int deleted = failure.Context.TryGetValue("retentionDeletedCount", out string? count) &&
                int.TryParse(count, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int observed)
                ? observed : 0;
            return new(promoted, deleted, 0, warnings, failure);
        }
    }
}
