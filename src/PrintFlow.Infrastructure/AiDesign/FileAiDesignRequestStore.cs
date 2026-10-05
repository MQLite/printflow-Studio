using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Workflow.AiDesign;

namespace PrintFlow.Infrastructure.AiDesign;

public sealed class FileAiDesignRequestStore : IAiDesignRequestStore
{
    private readonly TimeProvider _clock;
    internal static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    public string WorkspaceRoot { get; }
    public string WorkspaceIdentity { get; }
    public string RequestsRoot => Path.Combine(WorkspaceRoot, "AiDesign", "Requests");
    public FileAiDesignRequestStore(string workspaceRoot, TimeProvider? timeProvider = null)
    {
        if (!Path.IsPathFullyQualified(workspaceRoot)) throw new ArgumentException("Absolute workspace root required.", nameof(workspaceRoot));
        WorkspaceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        WorkspaceIdentity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(WorkspaceRoot.ToUpperInvariant())));
        _clock = timeProvider ?? TimeProvider.System;
    }
    public Task<OperationResult<AiDesignRequest>> CreateAsync(Guid id, AiDesignDraft draft, CancellationToken ct) => Guard(async () =>
    {
        await using var gate = await LockAsync(id, ct);
        if (File.Exists(Authority(id))) return Fail("Request authority already exists.");
        if (!ValidDraft(id, draft)) return Fail("Invalid draft attachment or reference identity.");
        var now = _clock.GetUtcNow();
        var r = new AiDesignRequest { RequestId = id, WorkspaceIdentity = WorkspaceIdentity,
            Prompt = AiDesignTransitions.FreezePrompt(draft.Prompt), Attachments = draft.Attachments.ToArray(), Token = AiDesignTransitions.NewToken(),
            NextAttachmentOrder = draft.Attachments.Count == 0 ? 1 : draft.Attachments.Max(a => a.Order) + 1,
            CreatedAtUtc = now, UpdatedAtUtc = now };
        await WriteAsync(r, ct); return OperationResult.Ok(r);
    });
    public Task<OperationResult<AiDesignRequest>> ReadAsync(Guid id, CancellationToken ct) => Guard(async () =>
    { await using var gate = await LockAsync(id, ct); return OperationResult.Ok(await ReadCoreAsync(id, ct)); });
    public Task<OperationResult<AiDesignRequest>> ChangeAsync(AiDesignFence expected, AiDesignEvent change, CancellationToken ct) => Guard(async () =>
    {
        await using var gate = await LockAsync(expected.RequestId, ct); var r = await ReadCoreAsync(expected.RequestId, ct);
        if (!Matches(r, expected)) return Fail("Stale AI request fence.");
        if (change is AiDesignEvent.DraftSaved d && !ValidDraftEdit(r, new(d.Prompt, d.Attachments))) return Fail("Draft cannot substitute an unowned attachment.");
        var next = AiDesignTransitions.Apply(r, change, _clock.GetUtcNow());
        if (next.IsSuccess) await WriteAsync(next.Value, ct); return next;
    });
    public async Task<OperationResult<IReadOnlyList<AiDesignRequest>>> ListAsync(CancellationToken ct)
    {
        try
        {
            ReparsePointGuard.RefuseAncestry(RequestsRoot);
            if (!Directory.Exists(RequestsRoot)) return OperationResult.Ok<IReadOnlyList<AiDesignRequest>>([]);
            var results = new List<AiDesignRequest>();
            foreach (string path in Directory.EnumerateDirectories(RequestsRoot))
            {
                ct.ThrowIfCancellationRequested();
                if (Guid.TryParseExact(Path.GetFileName(path), "D", out var id) && File.Exists(Authority(id)))
                { await using var gate = await LockAsync(id, ct); results.Add(await ReadCoreAsync(id, ct)); }
            }
            return OperationResult.Ok<IReadOnlyList<AiDesignRequest>>(results);
        }
        catch (Exception ex) when (IsBoundaryFailure(ex)) { return OperationResult.Fail<IReadOnlyList<AiDesignRequest>>(FailureCode.PersistenceError, "AI request listing unavailable."); }
    }
    public Task<OperationResult<AiDesignRequest>> SubmitOnceAsync(AiDesignFence expected, Func<CancellationToken, Task<OperationResult<Unit>>> click, CancellationToken ct) => Guard(async () =>
    {
        await using var gate = await LockAsync(expected.RequestId, ct); var r = await ReadCoreAsync(expected.RequestId, ct);
        if (!Matches(r, expected)) return Fail("Stale AI submit fence.");
        var reservation = r.SubmissionReservedAtUtc is null
            ? AiDesignTransitions.Apply(r, new AiDesignEvent.SubmitReserved(r.TabId ?? "", r.ConversationId ?? ""), _clock.GetUtcNow())
            : Fail("Submission already reserved; never replay.");
        if (reservation.IsFailure) return reservation;
        r = reservation.Value; await WriteAsync(r, ct);
        bool confirmed;
        using var bound = CancellationTokenSource.CreateLinkedTokenSource(ct); bound.CancelAfter(TimeSpan.FromSeconds(15));
        try { confirmed = (await click(bound.Token).WaitAsync(bound.Token)).IsSuccess; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { confirmed = false; }
        var result = AiDesignTransitions.Apply(r, confirmed ? new AiDesignEvent.SubmitConfirmed() : new AiDesignEvent.Interrupted(), _clock.GetUtcNow());
        if (result.IsSuccess) await WriteAsync(result.Value, CancellationToken.None); return result;
    });
    public Task<OperationResult<AiDesignRequest>> EditDraftAsync(AiDesignFence expected,
        Func<AiDesignRequest, CancellationToken, Task<OperationResult<AiDesignDraft>>> edit, CancellationToken ct) => Guard(async () =>
    {
        await using var gate = await LockAsync(expected.RequestId, ct); var r = await ReadCoreAsync(expected.RequestId, ct);
        if (!Matches(r, expected) || AiDesignTransitions.IsRunning(r.State)) return Fail("Draft is busy or stale.");
        var draft = await edit(r, ct); if (draft.IsFailure) return OperationResult.Fail<AiDesignRequest>(draft.Failure);
        if (!ValidDraft(r.RequestId, draft.Value)) return Fail("Draft reference mismatch.");
        var next = AiDesignTransitions.Apply(r, new AiDesignEvent.DraftSaved(draft.Value.Prompt, draft.Value.Attachments,
            Math.Max(r.NextAttachmentOrder, draft.Value.Attachments.Count == 0 ? 1 : draft.Value.Attachments.Max(a => a.Order) + 1)), _clock.GetUtcNow());
        if (next.IsSuccess) await WriteAsync(next.Value, ct); return next;
    });
    public static bool Matches(AiDesignRequest r, AiDesignFence f) => r.RequestId == f.RequestId && r.AttemptId == f.AttemptId
        && r.StateVersion == f.StateVersion && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(r.Token), Encoding.UTF8.GetBytes(f.Token));
    private string DirectoryFor(Guid id) => id == Guid.Empty ? throw new IOException("Invalid request identity.") : Path.Combine(RequestsRoot, id.ToString("D"));
    private string Authority(Guid id) => Path.Combine(DirectoryFor(id), "request.json");
    private async Task<FileStream> LockAsync(Guid id, CancellationToken ct)
    {
        string dir = DirectoryFor(id); ReparsePointGuard.RefuseAncestry(dir); Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, ".lock"); var end = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            ct.ThrowIfCancellationRequested(); ReparsePointGuard.RefuseAncestry(path);
            try { return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < end) { await Task.Delay(20, ct); }
        }
    }
    private async Task<AiDesignRequest> ReadCoreAsync(Guid id, CancellationToken ct)
    {
        string path = Authority(id); ReparsePointGuard.RefuseAncestry(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length is <= 0 or > 4 * 1024 * 1024) throw new IOException("Invalid request authority size.");
        var r = await JsonSerializer.DeserializeAsync<AiDesignRequest>(stream, JsonOptions, ct) ?? throw new JsonException();
        if (r.RequestId != id || r.WorkspaceIdentity != WorkspaceIdentity || string.IsNullOrWhiteSpace(r.Token) || !Enum.IsDefined(r.State) || r.StateVersion < 0 || !ValidShape(r))
            throw new JsonException("Invalid request authority identity.");
        return r;
    }
    private static bool ValidShape(AiDesignRequest r)
    {
        if (r.Prompt is null || r.Prompt.Fields is null || r.Prompt.AttachmentReferences is null || r.Attachments is null
            || r.UploadReceipts is null || r.RetainedCandidates is null || string.IsNullOrWhiteSpace(r.Prompt.TemplateId)
            || string.IsNullOrWhiteSpace(r.Prompt.Text) || r.Prompt.HandoffInstructions is null || r.NextAttachmentOrder < 1
            || r.Prompt.Fields.Any(kv => kv.Key is null || kv.Value is null) || r.Attachments.Any(a => a is null || a.Snapshot is null)
            || r.UploadReceipts.Any(u => u is null || !Enum.IsDefined(u.Status))) return false;
        if (!ValidDraft(r.RequestId, new(r.Prompt, r.Attachments))) return false;
        if (r.Attachments.Any(a => a.Order >= r.NextAttachmentOrder || string.IsNullOrWhiteSpace(a.UploadName) || a.Purpose is null
            || string.IsNullOrWhiteSpace(a.OriginalName) || string.IsNullOrWhiteSpace(a.MediaType) || a.ByteLength <= 0
            || !Sha256.TryParse(a.Sha256.Value, out _) || string.IsNullOrWhiteSpace(a.Snapshot.RelativePath))) return false;
        if (AiDesignTransitions.IsRunning(r.State) && (r.AttemptId == Guid.Empty || r.OwnerProcessId is not > 0
            || r.OwnerProcessStartUtcTicks is not > 0 || r.ConnectionId is null || r.ConnectionId == Guid.Empty)) return false;
        if (r.State is >= AiDesignState.Submitted and <= AiDesignState.Importing
            && (string.IsNullOrWhiteSpace(r.TabId) || string.IsNullOrWhiteSpace(r.ConversationId) || r.SubmissionReservedAtUtc is null)) return false;
        if (r.Failure is not null && (string.IsNullOrWhiteSpace(r.Failure.Code) || r.Failure.DetailArguments is null)) return false;
        return true;
    }
    private async Task WriteAsync(AiDesignRequest r, CancellationToken ct)
    {
        string authority = Authority(r.RequestId), temporary = Path.Combine(DirectoryFor(r.RequestId), $".request-{Guid.NewGuid():N}.tmp");
        ReparsePointGuard.RefuseAncestry(authority);
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
            { await JsonSerializer.SerializeAsync(stream, r, JsonOptions, ct); await stream.FlushAsync(ct); stream.Flush(true); }
            ct.ThrowIfCancellationRequested(); ReparsePointGuard.RefuseAncestry(authority);
            if (File.Exists(authority)) File.Replace(temporary, authority, null); else File.Move(temporary, authority, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static bool ValidDraft(Guid id, AiDesignDraft draft) => !string.IsNullOrWhiteSpace(draft.Prompt.Text)
        && draft.Attachments.All(a => a.Snapshot.RequestId == id && a.Id != Guid.Empty && a.Order > 0)
        && draft.Attachments.Select(a => a.Id).Distinct().Count() == draft.Attachments.Count
        && draft.Attachments.Select(a => a.Order).Distinct().Count() == draft.Attachments.Count
        && draft.Prompt.AttachmentReferences.All(reference => draft.Attachments.Any(a => a.Id == reference));
    private static bool ValidDraftEdit(AiDesignRequest r, AiDesignDraft draft) => ValidDraft(r.RequestId, draft)
        && draft.Attachments.All(a => r.Attachments.Any(old => old with { Purpose = a.Purpose } == a));
    private static OperationResult<AiDesignRequest> Fail(string detail) => OperationResult.Fail<AiDesignRequest>(FailureCode.PreconditionNotMet, detail);
    private static bool IsBoundaryFailure(Exception ex) => ex is IOException or UnauthorizedAccessException or JsonException or OperationCanceledException or ArgumentException;
    private static async Task<OperationResult<AiDesignRequest>> Guard(Func<Task<OperationResult<AiDesignRequest>>> action)
    { try { return await action(); } catch (Exception ex) when (IsBoundaryFailure(ex)) { return OperationResult.Fail<AiDesignRequest>(FailureCode.PersistenceError, "AI request authority unavailable."); } }
    private static JsonSerializerOptions CreateJsonOptions()
    { var options = new JsonSerializerOptions(JsonSerializerDefaults.Web); options.Converters.Add(new HashConverter()); return options; }
    private sealed class HashConverter : JsonConverter<Sha256>
    {
        public override Sha256 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Sha256.Parse(reader.GetString() ?? "");
        public override void Write(Utf8JsonWriter writer, Sha256 value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
    }
}
