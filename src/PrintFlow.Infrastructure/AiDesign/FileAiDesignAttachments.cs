using System.IO;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Infrastructure.AiDesign.Authoring;
using PrintFlow.Workflow.AiDesign;

namespace PrintFlow.Infrastructure.AiDesign;

/// <summary>Copies only explicit selections. Published snapshots have sealed identity and are reverified before every upload.</summary>
public sealed class FileAiDesignAttachments : IAiDesignAttachments
{
    public const long ImageLimit = AiDesignAttachmentLimits.ImageBytes, DocumentLimit = AiDesignAttachmentLimits.DocumentBytes, TotalLimit = AiDesignAttachmentLimits.TotalBytes;
    private readonly string _workspaceRoot;
    private readonly Action<AiDesignAttachment>? _afterPrepared;
    internal static readonly IReadOnlyDictionary<string,string> Types = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    { [".png"]="image/png",[".jpg"]="image/jpeg",[".jpeg"]="image/jpeg",[".webp"]="image/webp",[".pdf"]="application/pdf",
      [".txt"]="text/plain",[".md"]="text/markdown",[".csv"]="text/csv",[".docx"]="application/vnd.openxmlformats-officedocument.wordprocessingml.document",
      [".xlsx"]="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",[".pptx"]="application/vnd.openxmlformats-officedocument.presentationml.presentation" };
    public FileAiDesignAttachments(string workspaceRoot)
    {
        if (!Path.IsPathFullyQualified(workspaceRoot)) throw new ArgumentException("Absolute workspace root required.",nameof(workspaceRoot));
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }
    internal FileAiDesignAttachments(string workspaceRoot,Action<AiDesignAttachment> afterPrepared):this(workspaceRoot)
        =>_afterPrepared=afterPrepared;
    public async Task<OperationResult<IReadOnlyList<AiDesignAttachment>>> SnapshotAsync(Guid requestId, IReadOnlyList<SelectedAiDesignFile> selected,
        IReadOnlyList<AiDesignAttachment> existing, CancellationToken ct)
    {
        var heldSources = new List<IDisposable>();
        try
        {
            selected = selected.ToArray(); existing = existing.ToArray();
            string root = RequestRoot(requestId); ReparsePointGuard.RefuseAncestry(root); Directory.CreateDirectory(root);
            await using var gate = await Acquire(Path.Combine(root,".attachments.lock"),ct);
            if (existing.Count + selected.Count > AiDesignAttachmentLimits.MaximumFiles) return Reject("AttachmentCountLimit");
            var verified = await VerifyAsync(requestId,existing,ct); if (verified.IsFailure) return OperationResult.Fail<IReadOnlyList<AiDesignAttachment>>(verified.Failure);
            long total = existing.Sum(a => a.ByteLength); var sizes = new List<long>(); var inputs = new List<FileStream>();
            foreach (var selection in selected)
            {
                string path = SelectedPath(selection.AbsolutePath); string ext = Path.GetExtension(path).ToLowerInvariant();
                if (!Types.TryGetValue(ext,out var media)) return Reject("AttachmentFormatUnsupported");
                VerifiedAiAttachmentReader.PinParents(Path.GetDirectoryName(path)!, heldSources);
                var source = VerifiedAiAttachmentReader.OpenRegular(path); heldSources.Add(source); inputs.Add(source); long length = source.Length;
                if (length <= 0 || length > Limit(media)) return Reject("AttachmentFileSizeLimit");
                total = checked(total+length); if (total > TotalLimit) return Reject("AttachmentTotalSizeLimit"); sizes.Add(length);
            }
            var result = existing.ToList();
            using var batch=AiDesignOwnedStagingFiles.BeginAttachments(_workspaceRoot,requestId);
            for (int i=0;i<selected.Count;i++)
            {
                ct.ThrowIfCancellationRequested(); string path = SelectedPath(selected[i].AbsolutePath); string ext = Path.GetExtension(path).ToLowerInvariant();
                int order = await ReserveOrder(requestId,existing,ct);
                var staged=batch.Create(order,ext);
                var input = inputs[i]; // The exact checked selection stays pinned through copying.
                if (input.Length != sizes[i]) throw new IOException("Attachment changed after selection.");
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long copied=0;
                var output=staged.Snapshot.Output;
                {
                    byte[] buffer = new byte[65536]; int read;
                    while ((read=await input.ReadAsync(buffer,ct))>0)
                    { copied=checked(copied+read); if(copied>Limit(Types[ext]) || copied>sizes[i]) throw new IOException("Attachment size changed."); hash.AppendData(buffer,0,read); await output.WriteAsync(buffer.AsMemory(0,read),ct); }
                    if(copied!=sizes[i] || input.Length!=copied) throw new IOException("Incomplete attachment.");
                    await output.FlushAsync(ct); output.Flush(true);
                }
                await staged.PublishSnapshotAsync(ct);
                var attachment = new AiDesignAttachment(staged.Id,order,Path.GetFileName(path),staged.UploadName,selected[i].Purpose,Types[ext],copied,
                    Sha256.FromBytes(hash.GetHashAndReset()),new(requestId,staged.RelativePath));
                var seal=staged.CreateIdentity();
                await JsonSerializer.SerializeAsync(seal.Output,attachment,FileAiDesignRequestStore.JsonOptions,ct);
                await seal.CloseFlushedAsync(ct);
                staged.MarkReadOnly();result.Add(attachment);_afterPrepared?.Invoke(attachment);
            }
            batch.Commit();
            return OperationResult.Ok<IReadOnlyList<AiDesignAttachment>>(result.ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException or OperationCanceledException or OverflowException or Win32Exception or NotSupportedException)
        {
            return Reject(ex is OperationCanceledException ? "AttachmentCancelled" : "AttachmentSnapshotFailed");
        }
        finally { for (int i = heldSources.Count - 1; i >= 0; i--) heldSources[i].Dispose(); }
    }
    public async Task<OperationResult<Unit>> VerifyAsync(Guid requestId,IReadOnlyList<AiDesignAttachment> attachments,CancellationToken ct)
    {
        try
        {
            if(attachments.Count>AiDesignAttachmentLimits.MaximumFiles || attachments.Sum(a=>a.ByteLength)>TotalLimit || attachments.Select(a=>a.Id).Distinct().Count()!=attachments.Count)
                return OperationResult.Fail<Unit>(FailureCode.PreconditionNotMet,"AttachmentManifestInvalid");
            foreach(var a in attachments)
            {
                var opened = await new VerifiedAiAttachmentReader(_workspaceRoot).OpenAsync(requestId, a, ct);
                if (opened.IsFailure) return OperationResult.Fail<Unit>(opened.Failure);
                await using var lease = opened.Value;
            }
            return OperationResult.Ok();
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException or OperationCanceledException)
        { return OperationResult.Fail<Unit>(FailureCode.RevisionIntegrityMismatch,"AttachmentIntegrityOrOwnershipMismatch"); }
    }
    public async Task<OperationResult<Unit>> VerifyImagesAsync(Guid ownerId, IReadOnlyList<AiDesignAttachment> images, CancellationToken ct)
    {
        if (images.Count > AiDesignAttachmentLimits.MaximumFiles || images.Any(x => x.ByteLength <= 0 || x.ByteLength > ImageLimit) ||
            images.Sum(x => x.ByteLength) > TotalLimit) return OperationResult.Fail<Unit>(FailureCode.PreconditionNotMet, "ImageAttachmentLimit");
        foreach (var asset in images)
        {
            var opened = await new VerifiedAiAttachmentReader(_workspaceRoot).OpenAsync(ownerId, asset, ct);
            if (opened.IsFailure) return OperationResult.Fail<Unit>(opened.Failure);
            await using var lease = opened.Value;
            try
            {
                if (!await VerifiedAiAttachmentReader.HasImageHeaderAsync(lease.Stream, asset.MediaType, ct))
                    return OperationResult.Fail<Unit>(FailureCode.PreconditionNotMet, "ImageContainerUnsupported");
            }
            catch (OperationCanceledException) { return OperationResult.Fail<Unit>(FailureCode.Cancelled, "Image verification cancelled."); }
            catch (IOException) { return OperationResult.Fail<Unit>(FailureCode.RevisionIntegrityMismatch, "Image source could not be read."); }
        }
        return OperationResult.Ok();
    }
    /// <summary>Returns a read-only handle only after resolving the sealed manifest and rechecking length/hash on that same handle.</summary>
    public Task<OperationResult<VerifiedAiAttachmentRead>> OpenVerifiedAsync(Guid requestId,AiDesignAttachment attachment,CancellationToken ct)
        => new VerifiedAiAttachmentReader(_workspaceRoot).OpenAsync(requestId, attachment, ct);
    private string RequestRoot(Guid id) => id==Guid.Empty ? throw new IOException("Request identity required.") : Path.Combine(_workspaceRoot,"AiDesign","Requests",id.ToString("D"));
    internal static long Limit(string media)=>media.StartsWith("image/",StringComparison.Ordinal)?ImageLimit:DocumentLimit;
    private static string SelectedPath(string path)
    {
        if(!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\.\",StringComparison.Ordinal) || path.StartsWith(@"\\?\",StringComparison.Ordinal)) throw new IOException("Ordinary absolute file required.");
        return Path.GetFullPath(path);
    }
    private static async Task<FileStream> Acquire(string path,CancellationToken ct)
    {
        var until=DateTime.UtcNow.AddSeconds(30);
        while(true) { ct.ThrowIfCancellationRequested(); ReparsePointGuard.RefuseAncestry(path);
            try { return new(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None); }
            catch(IOException) when(DateTime.UtcNow<until) { await Task.Delay(20,ct); } }
    }
    private async Task<int> ReserveOrder(Guid requestId,IReadOnlyList<AiDesignAttachment> existing,CancellationToken ct)
    {
        string root=RequestRoot(requestId);
        string path=Path.Combine(root,".attachment-sequence"); ReparsePointGuard.RefuseAncestry(path);
        int previous=existing.Count==0?0:existing.Max(a=>a.Order);
        if(File.Exists(path)) { string text=await File.ReadAllTextAsync(path,ct); if(!int.TryParse(text,out int saved)||saved<0) throw new IOException("Invalid attachment sequence."); previous=Math.Max(previous,saved); }
        int next=checked(previous+1);
        using var staged=AiDesignOwnedStagingFiles.CreateRequest(_workspaceRoot,requestId,AiDesignOwnedStagingFiles.RequestPurpose.AttachmentSequence);
        byte[] bytes=System.Text.Encoding.ASCII.GetBytes(next.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await staged.Output.WriteAsync(bytes,ct);await staged.PublishAsync(ct);
        return next;
    }
    private static OperationResult<IReadOnlyList<AiDesignAttachment>> Reject(string reason)=>OperationResult.Fail<IReadOnlyList<AiDesignAttachment>>(FailureCode.PreconditionNotMet,reason);
}
