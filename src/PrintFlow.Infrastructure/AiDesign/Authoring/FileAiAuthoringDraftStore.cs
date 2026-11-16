using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.AiDesign.Authoring;

namespace PrintFlow.Infrastructure.AiDesign.Authoring;

public sealed class FileAiAuthoringDraftStore(string workspaceRoot) : IAiAuthoringDraftStore
{
    public async Task<OperationResult<AiAuthoringDraftListing>> ListAsync(CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            using var parents = AiDesignOwnedStagingFiles.PinAuthoringDraftsRoot(workspaceRoot);
            string root = System.IO.Path.Combine(workspaceRoot, "AiDesign", "Authoring", "Drafts");
            var values = new List<AiSavedAuthoringDraft>(); int unavailable = 0;
            foreach (string directory in System.IO.Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                if (!Guid.TryParseExact(System.IO.Path.GetFileName(directory), "D", out var id) || id == Guid.Empty) continue;
                try
                {
                    using var draftParents = AiDesignOwnedStagingFiles.PinAuthoringDirectory(workspaceRoot, id);
                    string pointer = System.IO.Path.Combine(directory, "current.json");
                    if (!System.IO.File.Exists(pointer)) continue;
                    // Each entry validates its own committed pointer. Never promote orphaned
                    // versions or let one unreadable customer draft hide the other saved work.
                    var read = await ReadAsync(id, ct);
                    ct.ThrowIfCancellationRequested();
                    if (read.IsFailure) { unavailable++; continue; }
                    values.Add(new(read.Value, System.IO.File.GetLastWriteTimeUtc(pointer)));
                }
                catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException)
                { unavailable++; }
            }
            return OperationResult.Ok(new AiAuthoringDraftListing(values.OrderByDescending(x => x.SavedAtUtc).ThenBy(x => x.Draft.Id).ToArray(), unavailable));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException or OperationCanceledException)
        { return OperationResult.Fail<AiAuthoringDraftListing>(ex is OperationCanceledException ? FailureCode.Cancelled : FailureCode.PersistenceError, "Saved drafts could not be listed."); }
    }
    public Task<OperationResult<AiAuthoringDraft>> ReadAsync(Guid id, CancellationToken ct) => Store(id).ReadAsync(ct);
    public Task<OperationResult<IReadOnlyList<AiAuthoringDraft>>> ReadHistoryAsync(Guid id, CancellationToken ct) => Store(id).ReadHistoryAsync(ct);
    public Task<OperationResult<AiAuthoringDraft>> SaveAsync(AiAuthoringDraft value, long? expectedRevision, CancellationToken ct) => Store(value.Id).SaveAsync(value, expectedRevision, ct);
    public Task<OperationResult<T>> WithCurrentRevisionAsync<T>(Guid id, long expectedRevision,
        Func<AiAuthoringDraft, Task<OperationResult<T>>> publish, CancellationToken ct) =>
        Store(id).WithCurrentRevisionAsync(expectedRevision, publish, ct);

    private AuthoringVersionedFileStore<AiAuthoringDraft> Store(Guid id) => new(workspaceRoot, id,
        value => value is not null && value.Id == id ? AiAuthoringSnapshots.Draft(value) : throw new ArgumentException("Draft identity mismatch."),
        value => value.Revision, (value, revision) => value with { Revision = revision });
}
