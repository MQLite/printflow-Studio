using System.IO;
using System.Text.Json;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Workspace;

namespace PrintFlow.Infrastructure.AiDesign.Authoring;

/// <summary>One locked CAS publication: immutable version first, then a fixed atomic current pointer.</summary>
internal sealed class AuthoringVersionedFileStore<T>(string workspaceRoot, Guid? draftId,
    Func<T, T> freeze, Func<T, long> revision, Func<T, long, T> withRevision, Func<T>? initialize = null)
{
    private const int MaxSnapshotBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(FileAiDesignRequestStore.JsonOptions);
    private sealed record Pointer(int SchemaVersion, long Revision, Guid VersionId);
    private sealed record Version(int SchemaVersion, Guid VersionId, Guid? PreviousVersionId, T Value);

    public async Task<OperationResult<IReadOnlyList<T>>> ReadHistoryAsync(CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            using var parents = AiDesignOwnedStagingFiles.PinAuthoringDirectory(workspaceRoot, draftId);
            await using var gate = await LockAsync(ct);
            var current = await ReadCurrentAsync(ct) ?? throw new IOException("Saved authoring data is missing.");
            List<T> values = []; HashSet<Guid> seen = [];
            Guid? nextId = current.Pointer.VersionId; long expected = current.Pointer.Revision;
            while (nextId is { } id && values.Count < 50)
            {
                if (id == Guid.Empty || !seen.Add(id)) throw new JsonException("Invalid authoring history link.");
                var version = await ReadJsonAsync<Version>(Path.Combine(DirectoryPath, $"revision-{id:N}.json"), MaxSnapshotBytes, ct);
                if (version.SchemaVersion != 1 || version.VersionId != id || version.Value is null ||
                    version.PreviousVersionId == Guid.Empty || version.PreviousVersionId == id)
                    throw new JsonException("Invalid authoring history identity.");
                var value = freeze(version.Value);
                if (revision(value) != expected || (expected == 0) != (version.PreviousVersionId is null))
                    throw new JsonException("Invalid authoring history revision.");
                values.Add(value); nextId = version.PreviousVersionId; expected--;
            }
            return OperationResult.Ok<IReadOnlyList<T>>(values.AsReadOnly());
        }
        catch (Exception ex) when (IsBoundaryFailure(ex))
        { return OperationResult.Fail<IReadOnlyList<T>>(ex is OperationCanceledException ? FailureCode.Cancelled : FailureCode.PersistenceError, "Authoring history could not be read."); }
    }

    public async Task<OperationResult<T>> ReadAsync(CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            using var parents = AiDesignOwnedStagingFiles.PinAuthoringDirectory(workspaceRoot, draftId);
            await using var gate = await LockAsync(ct);
            var current = await ReadCurrentAsync(ct);
            if (current is null)
            {
                if (initialize is null) throw new IOException("Saved authoring data is missing.");
                var value = freeze(initialize());
                await PublishAsync(value, null, ct);
                return OperationResult.Ok(value);
            }
            return OperationResult.Ok(current.Value.Value);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex)) { return BoundaryFailure(ex); }
    }

    public async Task<OperationResult<T>> SaveAsync(T value, long? expectedRevision, CancellationToken ct)
    {
        try
        {
            value = freeze(value); // Detach all collections before waiting for the lock.
            ct.ThrowIfCancellationRequested();
            using var parents = AiDesignOwnedStagingFiles.PinAuthoringDirectory(workspaceRoot, draftId);
            await using var gate = await LockAsync(ct);
            var current = await ReadCurrentAsync(ct);
            if (current is null && initialize is not null)
            {
                var initial = freeze(initialize());
                var initialId = await PublishAsync(initial, null, ct);
                current = (new Pointer(1, revision(initial), initialId), initial);
            }
            if (expectedRevision is null ? current is not null || revision(value) != 0 :
                current is null || current.Value.Pointer.Revision != expectedRevision || revision(value) != expectedRevision)
                return OperationResult.Fail<T>(FailureCode.PreconditionNotMet, "Authoring data is missing, already exists or is stale.");
            long nextRevision = expectedRevision is null ? 0 : checked(expectedRevision.Value + 1);
            value = freeze(withRevision(value, nextRevision));
            await PublishAsync(value, current?.Pointer.VersionId, ct);
            return OperationResult.Ok(value);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex)) { return BoundaryFailure(ex); }
    }

    private string DirectoryPath => AiDesignOwnedStagingFiles.AuthoringDirectory(workspaceRoot, draftId);

    private async Task<FileStream> LockAsync(CancellationToken ct)
    {
        string path = Path.Combine(DirectoryPath, ".lock"); DateTime end = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            ct.ThrowIfCancellationRequested(); ReparsePointGuard.RefuseAncestry(path);
            try { return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < end) { await Task.Delay(20, ct); }
        }
    }

    private async Task<(Pointer Pointer, T Value)?> ReadCurrentAsync(CancellationToken ct)
    {
        string root = DirectoryPath; Pointer pointer;
        try { pointer = await ReadJsonAsync<Pointer>(Path.Combine(root, "current.json"), 4096, ct); }
        catch (FileNotFoundException)
        {
            // Never invent a fresh library over history whose authority pointer was lost.
            if (Directory.EnumerateFiles(root, "revision-*.json").Any()) throw new IOException("Authoring current pointer is missing from existing history.");
            return null;
        }
        if (pointer.SchemaVersion != 1 || pointer.Revision < 0 || pointer.VersionId == Guid.Empty) throw new JsonException("Invalid authoring pointer.");
        var version = await ReadJsonAsync<Version>(Path.Combine(root, $"revision-{pointer.VersionId:N}.json"), MaxSnapshotBytes, ct);
        if (version.Value is null || version.SchemaVersion != 1 || version.VersionId != pointer.VersionId || version.PreviousVersionId == Guid.Empty ||
            version.PreviousVersionId == version.VersionId) throw new JsonException("Invalid authoring version identity.");
        var value = freeze(version.Value);
        if (revision(value) != pointer.Revision) throw new JsonException("Authoring revision mismatch.");
        return (pointer, value);
    }

    private static async Task<TJson> ReadJsonAsync<TJson>(string path, int maxBytes, CancellationToken ct)
    {
        ReparsePointGuard.RefuseAncestry(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        if (stream.Length is <= 0 || stream.Length > maxBytes) throw new IOException("Invalid authoring JSON size.");
        return await JsonSerializer.DeserializeAsync<TJson>(stream, Json, ct) ?? throw new JsonException("Authoring JSON is null.");
    }

    private async Task<Guid> PublishAsync(T value, Guid? previousVersionId, CancellationToken ct)
    {
        Guid id = Guid.NewGuid();
        using var staged = AiDesignOwnedStagingFiles.CreateAuthoring(workspaceRoot, draftId, id);
        await JsonSerializer.SerializeAsync(staged.Output, new Version(1, id, previousVersionId, value), Json, ct);
        if (staged.Output.Length > MaxSnapshotBytes) throw new IOException("Authoring snapshot is too large.");
        // Retain creation identity until the pointer commits. A caught failure may roll back
        // exactly this uncommitted version; successful or unknown historical files are never removed.
        using var pointer = AiDesignOwnedStagingFiles.CreateAuthoring(workspaceRoot, draftId, null);
        await JsonSerializer.SerializeAsync(pointer.Output, new Pointer(1, revision(value), id), Json, ct);
        await AiDesignOwnedStagingFiles.PublishAuthoringAsync(staged, pointer, ct);
        return id;
    }

    private static bool IsBoundaryFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or
        JsonException or OperationCanceledException or ArgumentException or OverflowException or NotSupportedException;
    private static OperationResult<T> BoundaryFailure(Exception ex) => OperationResult.Fail<T>(
        ex is OperationCanceledException ? FailureCode.Cancelled : FailureCode.PersistenceError, "Authoring data could not be read or saved.");
}
