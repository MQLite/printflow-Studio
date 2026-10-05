using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Workflow.AiDesign;

namespace PrintFlow.Infrastructure.AiDesign;

/// <summary>Copies only explicit selections. Published snapshots have sealed identity and are reverified before every upload.</summary>
public sealed class FileAiDesignAttachments : IAiDesignAttachments
{
    public const long ImageLimit = 20L * 1024 * 1024, DocumentLimit = 40L * 1024 * 1024, TotalLimit = 80L * 1024 * 1024;
    private readonly string _workspaceRoot;
    private static readonly IReadOnlyDictionary<string,string> Types = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    { [".png"]="image/png",[".jpg"]="image/jpeg",[".jpeg"]="image/jpeg",[".webp"]="image/webp",[".pdf"]="application/pdf",
      [".txt"]="text/plain",[".md"]="text/markdown",[".csv"]="text/csv",[".docx"]="application/vnd.openxmlformats-officedocument.wordprocessingml.document",
      [".xlsx"]="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",[".pptx"]="application/vnd.openxmlformats-officedocument.presentationml.presentation" };
    public FileAiDesignAttachments(string workspaceRoot)
    {
        if (!Path.IsPathFullyQualified(workspaceRoot)) throw new ArgumentException("Absolute workspace root required.",nameof(workspaceRoot));
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }
    public async Task<OperationResult<IReadOnlyList<AiDesignAttachment>>> SnapshotAsync(Guid requestId, IReadOnlyList<SelectedAiDesignFile> selected,
        IReadOnlyList<AiDesignAttachment> existing, CancellationToken ct)
    {
        var created = new List<string>();
        try
        {
            string root = RequestRoot(requestId); ReparsePointGuard.RefuseAncestry(root); Directory.CreateDirectory(root);
            await using var gate = await Acquire(Path.Combine(root,".attachments.lock"),ct);
            if (existing.Count + selected.Count > 8) return Reject("AttachmentCountLimit");
            var verified = await VerifyAsync(requestId,existing,ct); if (verified.IsFailure) return OperationResult.Fail<IReadOnlyList<AiDesignAttachment>>(verified.Failure);
            long total = existing.Sum(a => a.ByteLength); var sizes = new List<long>();
            foreach (var selection in selected)
            {
                string path = SelectedPath(selection.AbsolutePath); string ext = Path.GetExtension(path).ToLowerInvariant();
                if (!Types.TryGetValue(ext,out var media)) return Reject("AttachmentFormatUnsupported");
                using var source = OpenRegular(path); long length = source.Length;
                if (length <= 0 || length > Limit(media)) return Reject("AttachmentFileSizeLimit");
                total = checked(total+length); if (total > TotalLimit) return Reject("AttachmentTotalSizeLimit"); sizes.Add(length);
            }
            var result = existing.ToList();
            for (int i=0;i<selected.Count;i++)
            {
                ct.ThrowIfCancellationRequested(); string path = SelectedPath(selected[i].AbsolutePath); string ext = Path.GetExtension(path).ToLowerInvariant();
                int order = await ReserveOrder(root,existing,ct); Guid id = Guid.NewGuid(); string name = $"attachment-{order:D2}{ext}";
                string relative = $"Attachments/{id:N}/{name}"; string folder = Path.Combine(root,"Attachments",id.ToString("N"));
                ReparsePointGuard.RefuseAncestry(folder); Directory.CreateDirectory(folder); created.Add(folder);
                string staging = Path.Combine(folder,"snapshot.tmp"), final = Path.Combine(folder,name);
                await using var input = OpenRegular(path);
                if (input.Length != sizes[i]) throw new IOException("Attachment changed after selection.");
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); long copied=0;
                await using (var output = new FileStream(staging,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.WriteThrough))
                {
                    byte[] buffer = new byte[65536]; int read;
                    while ((read=await input.ReadAsync(buffer,ct))>0)
                    { copied=checked(copied+read); if(copied>Limit(Types[ext]) || copied>sizes[i]) throw new IOException("Attachment size changed."); hash.AppendData(buffer,0,read); await output.WriteAsync(buffer.AsMemory(0,read),ct); }
                    if(copied!=sizes[i] || input.Length!=copied) throw new IOException("Incomplete attachment.");
                    await output.FlushAsync(ct); output.Flush(true);
                }
                ct.ThrowIfCancellationRequested(); ReparsePointGuard.RefuseAncestry(final); File.Move(staging,final,false);
                var attachment = new AiDesignAttachment(id,order,Path.GetFileName(path),name,selected[i].Purpose,Types[ext],copied,
                    Sha256.FromBytes(hash.GetHashAndReset()),new(requestId,relative));
                await using (var seal = new FileStream(Path.Combine(folder,"identity.json"),FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough))
                { await JsonSerializer.SerializeAsync(seal,attachment,FileAiDesignRequestStore.JsonOptions,ct); await seal.FlushAsync(ct); seal.Flush(true); }
                File.SetAttributes(final,File.GetAttributes(final)|FileAttributes.ReadOnly); result.Add(attachment);
            }
            return OperationResult.Ok<IReadOnlyList<AiDesignAttachment>>(result.ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException or OperationCanceledException or OverflowException)
        {
            foreach(string folder in created)
            {
                // All cleanup targets were created by this call and remain inside its owned request root.
                ReparsePointGuard.RefuseAncestry(folder);
                foreach(string file in Directory.EnumerateFiles(folder)) { File.SetAttributes(file,FileAttributes.Normal); File.Delete(file); }
                Directory.Delete(folder);
            }
            return Reject(ex is OperationCanceledException ? "AttachmentCancelled" : "AttachmentSnapshotFailed");
        }
    }
    public async Task<OperationResult<Unit>> VerifyAsync(Guid requestId,IReadOnlyList<AiDesignAttachment> attachments,CancellationToken ct)
    {
        try
        {
            if(attachments.Count>8 || attachments.Sum(a=>a.ByteLength)>TotalLimit || attachments.Select(a=>a.Id).Distinct().Count()!=attachments.Count)
                return OperationResult.Fail<Unit>(FailureCode.PreconditionNotMet,"AttachmentManifestInvalid");
            foreach(var a in attachments)
            {
                string path=ResolvePath(requestId,a); await using var stream=OpenRegular(path);
                if(stream.Length!=a.ByteLength || stream.Length<=0 || stream.Length>Limit(a.MediaType)) throw new IOException("Attachment length mismatch.");
                var actual=Sha256.FromBytes(await SHA256.HashDataAsync(stream,ct));
                if(actual!=a.Sha256 || stream.Position!=a.ByteLength || stream.Length!=a.ByteLength) throw new IOException("Attachment integrity mismatch.");
            }
            return OperationResult.Ok();
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException or OperationCanceledException)
        { return OperationResult.Fail<Unit>(FailureCode.RevisionIntegrityMismatch,"AttachmentIntegrityOrOwnershipMismatch"); }
    }
    /// <summary>Returns a read-only handle only after resolving the sealed manifest and rechecking length/hash on that same handle.</summary>
    public async Task<OperationResult<FileStream>> OpenVerifiedAsync(Guid requestId,AiDesignAttachment attachment,CancellationToken ct)
    {
        FileStream? stream=null;
        try
        {
            stream=OpenRegular(ResolvePath(requestId,attachment));
            if(stream.Length!=attachment.ByteLength || Sha256.FromBytes(await SHA256.HashDataAsync(stream,ct))!=attachment.Sha256) throw new IOException("Attachment changed.");
            stream.Position=0; return OperationResult.Ok(stream);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException or OperationCanceledException)
        { if(stream is not null) await stream.DisposeAsync(); return OperationResult.Fail<FileStream>(FailureCode.RevisionIntegrityMismatch,"AttachmentIntegrityOrOwnershipMismatch"); }
    }
    private string ResolvePath(Guid requestId,AiDesignAttachment a)
    {
        if(a.Id==Guid.Empty || a.Snapshot.RequestId!=requestId || a.Order<=0) throw new IOException("Attachment identity mismatch.");
        string ext=Path.GetExtension(a.UploadName).ToLowerInvariant();
        if(!Types.TryGetValue(ext,out var media) || media!=a.MediaType || a.UploadName!=$"attachment-{a.Order:D2}{ext}"
            || a.Snapshot.RelativePath!=$"Attachments/{a.Id:N}/{a.UploadName}") throw new IOException("Attachment path mismatch.");
        string root=RequestRoot(requestId), path=Path.Combine(root,"Attachments",a.Id.ToString("N"),a.UploadName);
        ReparsePointGuard.RefuseAncestry(path); string identity=Path.Combine(Path.GetDirectoryName(path)!,"identity.json");
        using var seal=OpenRegular(identity);
        if(seal.Length>65536) throw new IOException("Invalid attachment identity.");
        var saved=JsonSerializer.Deserialize<AiDesignAttachment>(seal,FileAiDesignRequestStore.JsonOptions);
        if(saved is null || saved with { Purpose=a.Purpose } != a) throw new IOException("Unselected attachment identity.");
        return path;
    }
    private string RequestRoot(Guid id) => id==Guid.Empty ? throw new IOException("Request identity required.") : Path.Combine(_workspaceRoot,"AiDesign","Requests",id.ToString("D"));
    private static long Limit(string media)=>media.StartsWith("image/",StringComparison.Ordinal)?ImageLimit:DocumentLimit;
    private static string SelectedPath(string path)
    {
        if(!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\.\",StringComparison.Ordinal) || path.StartsWith(@"\\?\",StringComparison.Ordinal)) throw new IOException("Ordinary absolute file required.");
        return Path.GetFullPath(path);
    }
    private static FileStream OpenRegular(string path)
    {
        ReparsePointGuard.RefuseAncestry(path); var attrs=File.GetAttributes(path);
        if((attrs&(FileAttributes.Directory|FileAttributes.ReparsePoint|FileAttributes.Device))!=0) throw new IOException("Regular file required.");
        return new(path,FileMode.Open,FileAccess.Read,FileShare.Read,65536,FileOptions.Asynchronous|FileOptions.SequentialScan);
    }
    private static async Task<FileStream> Acquire(string path,CancellationToken ct)
    {
        var until=DateTime.UtcNow.AddSeconds(30);
        while(true) { ct.ThrowIfCancellationRequested(); ReparsePointGuard.RefuseAncestry(path);
            try { return new(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None); }
            catch(IOException) when(DateTime.UtcNow<until) { await Task.Delay(20,ct); } }
    }
    private static async Task<int> ReserveOrder(string root,IReadOnlyList<AiDesignAttachment> existing,CancellationToken ct)
    {
        string path=Path.Combine(root,".attachment-sequence"); ReparsePointGuard.RefuseAncestry(path);
        int previous=existing.Count==0?0:existing.Max(a=>a.Order);
        if(File.Exists(path)) { string text=await File.ReadAllTextAsync(path,ct); if(!int.TryParse(text,out int saved)||saved<0) throw new IOException("Invalid attachment sequence."); previous=Math.Max(previous,saved); }
        int next=checked(previous+1); string tmp=Path.Combine(root,$".sequence-{Guid.NewGuid():N}.tmp");
        try
        {
            await using(var output=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough))
            { byte[] bytes=System.Text.Encoding.ASCII.GetBytes(next.ToString(System.Globalization.CultureInfo.InvariantCulture)); await output.WriteAsync(bytes,ct); await output.FlushAsync(ct); output.Flush(true); }
            if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path,false);
        }
        finally{if(File.Exists(tmp))File.Delete(tmp);}
        return next;
    }
    private static OperationResult<IReadOnlyList<AiDesignAttachment>> Reject(string reason)=>OperationResult.Fail<IReadOnlyList<AiDesignAttachment>>(FailureCode.PreconditionNotMet,reason);
}
