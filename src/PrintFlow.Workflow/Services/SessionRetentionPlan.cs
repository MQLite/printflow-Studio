using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;

namespace PrintFlow.Workflow.Services;

/// <summary>
/// Pure classification of completed-session files. Working is a location, never a deletion
/// category. Unknown files and all failed-attempt evidence are retained by default.
/// </summary>
public sealed record SessionRetentionPlan(
    IReadOnlyList<Revision> Promote,
    IReadOnlyList<Revision> Release,
    IReadOnlyList<RetentionFile> Required,
    IReadOnlyList<RetentionFile> Delete,
    IReadOnlyList<WorkspaceFileRef> Unclassified)
{
    public static OperationResult<SessionRetentionPlan> Create(
        SessionAggregate aggregate, IReadOnlyList<WorkingFileEntry> working)
    {
        if (aggregate.Session.State != SessionState.Completed || aggregate.Session.CompletedAtUtc is null ||
            aggregate.Attempts.Any(a => a.Status == AttemptStatus.Running) ||
            aggregate.Steps.Any(s => s.State is not (StepState.Approved or StepState.Skipped)))
            return OperationResult.Fail<SessionRetentionPlan>(FailureCode.PreconditionNotMet,
                "Only a committed, fully completed session may enter retention cleanup.");

        string root = aggregate.Session.Workspace.RelativePath + "/";
        IEnumerable<WorkspaceFileRef> references = aggregate.Revisions.Select(r => r.File)
            .Concat(aggregate.Revisions.Where(r => r.FormerWorkingFile is not null).Select(r => r.FormerWorkingFile!.Value))
            .Concat(aggregate.Outputs.Select(o => o.File))
            .Concat(aggregate.Outputs.Where(o => o.PromotionReservation is not null).Select(o => o.PromotionReservation!.Value))
            .Concat(working.Select(f => f.File));
        if (references.Any(f => !f.RelativePath.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
            return OperationResult.Fail<SessionRetentionPlan>(FailureCode.WorkspaceError,
                "Retention refused a file reference outside this exact session.");

        List<Revision> promote = [];
        List<Revision> release = [];
        List<RetentionFile> required = [];
        Dictionary<string, RetentionFile> delete = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> authority = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> classified = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> productionFiles = new(aggregate.Revisions
            .Where(r => r.Operation == OperationKind.PhotoshopOutput || r.Facts.Format == ImageFormat.Tiff)
            .SelectMany(r => r.FormerWorkingFile is { } former ? new[] { r.File.RelativePath, former.RelativePath } : [r.File.RelativePath]),
            StringComparer.OrdinalIgnoreCase);

        foreach (Revision revision in aggregate.Revisions)
        {
            classified.Add(revision.File.RelativePath);
            // The existing explicit RecycledAt state also explains the producing TIFF twin's
            // missing bytes. Retention neither deletes nor recycles that file again.
            bool recycled = aggregate.Outputs.Any(o => o.Id.Value == revision.Id.Value && o.RecycledAtUtc is not null);
            if (recycled) continue;

            bool expires = revision.RetentionReleasedAtUtc is not null || CanRelease(aggregate, revision);
            if (expires)
            {
                if (revision.RetentionReleasedAtUtc is null)
                {
                    release.Add(revision);
                    required.Add(new(revision.File, revision.Sha256));
                }
                AddDelete(revision.File, revision.Sha256);
                continue;
            }

            required.Add(new(revision.File, revision.Sha256));
            authority.Add(revision.File.RelativePath);
            if (revision.File.Area == WorkspaceArea.Working)
            {
                promote.Add(revision);
                // After the location switch this exact copy is redundant. TIFFs deliberately
                // remain: CleanupWorking cannot bypass the production Recycle Bin boundary.
                AddDelete(revision.File, revision.Sha256, allowCurrentAuthority: true);
            }
            if (revision.FormerWorkingFile is { } former)
            {
                classified.Add(former.RelativePath);
                AddDelete(former, revision.Sha256);
            }
        }

        foreach (var output in aggregate.Outputs)
        {
            classified.Add(output.File.RelativePath);
            if (output.RecycledAtUtc is null)
            {
                required.Add(new(output.File, output.Sha256));
                authority.Add(output.File.RelativePath);
                delete.Remove(output.File.RelativePath);
            }
            if (output.PromotionReservation is { } reservation)
            {
                authority.Add(reservation.RelativePath);
                delete.Remove(reservation.RelativePath);
            }
        }

        // CreateWorkingCopyAsync uses exactly this attempt ID and upstream basename. A GUID
        // folder or a familiar extension alone proves nothing. Only successful, known producing
        // operations with a persisted upstream can establish an input copy as disposable.
        foreach (ProcessingAttempt attempt in aggregate.Attempts.Where(a => a.Status == AttemptStatus.Succeeded))
        {
            if (attempt.Operation is not (OperationKind.Enhance or OperationKind.RemoveBackground or
                OperationKind.Trim or OperationKind.ManualImport or OperationKind.PhotoshopOutput or
                OperationKind.PreparePsd or OperationKind.PreparePdf)) continue;
            Revision? upstream = aggregate.Revisions.SingleOrDefault(r => r.Id == attempt.InputRevisionId);
            if (upstream is null) continue;
            WorkspaceFileRef copy = WorkspaceFileRef.Create(
                $"{root}Working/{attempt.Id}/{upstream.File.FileName}", WorkspaceArea.Working);
            if (!authority.Contains(copy.RelativePath)) AddDelete(copy, upstream.Sha256);
        }

        foreach (WorkingFileEntry entry in working)
        {
            if (IsEvidence(aggregate, entry.File)) classified.Add(entry.File.RelativePath);
        }
        return OperationResult.Ok(new SessionRetentionPlan(promote, release, required,
            delete.Values.ToList(), working.Where(f => !classified.Contains(f.File.RelativePath))
                .Select(f => f.File).ToList()));

        void AddDelete(WorkspaceFileRef file, Sha256 hash, bool allowCurrentAuthority = false)
        {
            if (file.Area != WorkspaceArea.Working || productionFiles.Contains(file.RelativePath) ||
                IsTiff(file) || IsEvidence(aggregate, file)) return;
            if (!allowCurrentAuthority && authority.Contains(file.RelativePath)) return;
            classified.Add(file.RelativePath);
            delete[file.RelativePath] = new(file, hash);
        }
    }

    private static bool CanRelease(SessionAggregate aggregate, Revision revision) =>
        revision.File.Area == WorkspaceArea.Working && revision.FormerWorkingFile is null &&
        revision.Operation is OperationKind.Enhance or OperationKind.RemoveBackground &&
        aggregate.Reviews.Any(r => r.SubjectId == revision.Id.Value && !r.IsApproved && r.ReviewedSha256 == revision.Sha256) &&
        !aggregate.Steps.Any(s => s.CurrentRevisionId == revision.Id) &&
        !aggregate.Reviews.Any(r => r.SubjectId == revision.Id.Value && r.IsApproved) &&
        !aggregate.Outputs.Any(o => o.SourceRevisionId == revision.Id || o.Id.Value == revision.Id.Value) &&
        !IsEvidence(aggregate, revision.File);

    private static bool IsEvidence(SessionAggregate aggregate, WorkspaceFileRef file)
    {
        string folder = aggregate.Session.Workspace.RelativePath + "/Working/";
        foreach (ProcessingAttempt attempt in aggregate.Attempts)
        {
            if (attempt.Status != AttemptStatus.Succeeded &&
                file.RelativePath.StartsWith(folder + attempt.Id + "/", StringComparison.OrdinalIgnoreCase)) return true;
            // Context is intentionally extensible. Match absolute or relative path mentions
            // without guessing which context keys carry paths. An unqualified filename value
            // is also preserved, but a different attempt's full path is not this file's authority.
            IEnumerable<string> evidence = (attempt.Failure?.Context.Values ?? [])
                .Concat(new[] { attempt.Failure?.TechnicalDetail ?? "", attempt.AdapterNotes ?? "" });
            if (evidence.Any(value => value.Replace('\\', '/').Contains(file.RelativePath, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value.Trim(), file.FileName, StringComparison.OrdinalIgnoreCase))) return true;
        }
        return false;
    }

    private static bool IsTiff(WorkspaceFileRef file) =>
        file.FileName.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) ||
        file.FileName.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase);
}
