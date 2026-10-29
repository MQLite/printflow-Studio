using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Infrastructure.AiDesign;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.AiDesign;

namespace PrintFlow.Tests.Integration.AiDesign;

public sealed class AiDesignAttachmentTests
{
    [Fact]
    public async Task Refused_batch_cleanup_preserves_replaced_file_and_original_snapshot_failure()
    {
        using TempWorkspace source=new();using TempWorkspace owned=new();Guid id=Guid.NewGuid();
        string input=Source(source.Root,"reference.png",3);string? replacement=null;
        var files=new FileAiDesignAttachments(owned.Root,a=>
        {
            replacement=Path.Combine(owned.Root,"AiDesign","Requests",id.ToString("D"),a.Snapshot.RelativePath.Replace('/',Path.DirectorySeparatorChar));
            File.Move(replacement,replacement+".original");File.WriteAllBytes(replacement,[9]);File.SetAttributes(replacement,FileAttributes.ReadOnly|FileAttributes.Hidden);
            throw new IOException("synthetic original snapshot failure");
        });
        var result=await files.SnapshotAsync(id,[new(input,"reference")],[],default);
        result.IsFailure.ShouldBeTrue();result.Failure.TechnicalDetail.ShouldBe("AttachmentSnapshotFailed");
        File.ReadAllBytes(replacement!).ShouldBe(new byte[]{9});
        (File.GetAttributes(replacement!)&(FileAttributes.ReadOnly|FileAttributes.Hidden)).ShouldBe(FileAttributes.ReadOnly|FileAttributes.Hidden);
        File.ReadAllBytes(replacement+".original").ShouldBe(new byte[3]);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Mid_batch_failure_or_cancel_preserves_original_old_snapshot_unknowns_and_production_sentinels(bool cancel)
    {
        using TempWorkspace source=new();using TempWorkspace owned=new();Guid id=Guid.NewGuid();using var stopped=new CancellationTokenSource();
        string first=Source(source.Root,"first.png",3),second=Source(source.Root,"second.txt",4);
        var old=(await new FileAiDesignAttachments(owned.Root).SnapshotAsync(id,[new(first,"old")],[],default)).Value;
        string oldPath=Path.Combine(owned.Root,"AiDesign","Requests",id.ToString("D"),old[0].Snapshot.RelativePath.Replace('/',Path.DirectorySeparatorChar));
        var sentinels=new List<(string Path,byte[] Bytes,FileAttributes Attributes)>();
        foreach(string area in new[]{"Revision","Approved","Rejected","Production"})
        {string folder=Path.Combine(owned.Root,area);Directory.CreateDirectory(folder);string path=Path.Combine(folder,"preserve.tiff");File.WriteAllBytes(path,[5]);File.SetAttributes(path,FileAttributes.ReadOnly|FileAttributes.Hidden);sentinels.Add((path,[5],File.GetAttributes(path)));}
        string? currentSnapshot=null,seal=null,unknown=null;
        var failing=new FileAiDesignAttachments(owned.Root,a=>
        {
            currentSnapshot=Path.Combine(owned.Root,"AiDesign","Requests",id.ToString("D"),a.Snapshot.RelativePath.Replace('/',Path.DirectorySeparatorChar));
            seal=Path.Combine(Path.GetDirectoryName(currentSnapshot)!,"identity.json");File.Exists(seal).ShouldBeTrue();
            unknown=Path.Combine(Path.GetDirectoryName(currentSnapshot)!,"unknown.tiff");File.WriteAllBytes(unknown,[9]);File.SetAttributes(unknown,FileAttributes.ReadOnly|FileAttributes.Hidden);
            sentinels.Add((unknown,[9],File.GetAttributes(unknown)));
            if(cancel)stopped.Cancel();else File.Delete(second);
        });
        var result=await failing.SnapshotAsync(id,[new(first,"new"),new(second,"later")],old,stopped.Token);
        result.IsFailure.ShouldBeTrue();result.Failure.TechnicalDetail.ShouldBe(cancel?"AttachmentCancelled":"AttachmentSnapshotFailed");
        File.Exists(currentSnapshot).ShouldBeFalse();File.Exists(seal).ShouldBeFalse();
        foreach(var s in sentinels){File.ReadAllBytes(s.Path).ShouldBe(s.Bytes);File.GetAttributes(s.Path).ShouldBe(s.Attributes);}
        File.ReadAllBytes(first).ShouldBe(new byte[3]);File.ReadAllBytes(oldPath).ShouldBe(new byte[3]);
        (await new FileAiDesignAttachments(owned.Root).VerifyAsync(id,old,default)).IsSuccess.ShouldBeTrue();
    }
    private const long MiB = 1024 * 1024;
    private static string Source(string root, string name, long length)
    { string path = Path.Combine(root,name); using var stream = new FileStream(path,FileMode.CreateNew,FileAccess.Write); stream.SetLength(length); return path; }
    [Theory]
    [InlineData(".png")][InlineData(".jpg")][InlineData(".jpeg")][InlineData(".webp")][InlineData(".pdf")]
    [InlineData(".txt")][InlineData(".md")][InlineData(".csv")][InlineData(".docx")][InlineData(".xlsx")][InlineData(".pptx")]
    public async Task Allowed_formats_are_owned_snapshots(string extension)
    {
        using TempWorkspace source = new(); using TempWorkspace owned = new();
        var id = Guid.NewGuid(); string original = Source(source.Root,"input"+extension,3);
        var files = new FileAiDesignAttachments(owned.Root); var result = await files.SnapshotAsync(id,[new(original,"purpose")],[],default);
        result.IsSuccess.ShouldBeTrue(); var a = result.Value.Single(); a.Snapshot.RequestId.ShouldBe(id); a.OriginalName.ShouldBe("input"+extension);
        a.UploadName.ShouldBe("attachment-01"+extension); a.ByteLength.ShouldBe(3); a.Purpose.ShouldBe("purpose");
        (await files.VerifyAsync(id,result.Value,default)).IsSuccess.ShouldBeTrue();
    }
    [Theory]
    [InlineData(".png",20,false)][InlineData(".png",20,true)][InlineData(".pdf",40,false)][InlineData(".pdf",40,true)]
    public async Task Single_file_exact_limit_is_accepted_and_plus_one_rejected(string ext,int limit,bool extra)
    {
        using TempWorkspace source = new(); using TempWorkspace owned = new(); var files = new FileAiDesignAttachments(owned.Root);
        var result = await files.SnapshotAsync(Guid.NewGuid(),[new(Source(source.Root,"input"+ext,limit*MiB+(extra?1:0)),"purpose")],[],default);
        result.IsSuccess.ShouldBe(!extra);
    }
    [Theory]
    [InlineData(8,false)][InlineData(9,true)]
    public async Task Count_limit_is_enforced(int count,bool rejected)
    {
        using TempWorkspace source = new(); using TempWorkspace owned = new();
        var selected = Enumerable.Range(1,count).Select(i => new SelectedAiDesignFile(Source(source.Root,$"{i}.txt",1),"purpose")).ToArray();
        var result = await new FileAiDesignAttachments(owned.Root).SnapshotAsync(Guid.NewGuid(),selected,[],default); result.IsFailure.ShouldBe(rejected);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Total_limit_is_enforced_without_publishing_partial_manifest(bool extra)
    {
        using TempWorkspace source = new(); using TempWorkspace owned = new(); var files = new FileAiDesignAttachments(owned.Root); var id = Guid.NewGuid();
        var selected = new[] { new SelectedAiDesignFile(Source(source.Root,"1.pdf",40*MiB),"a"), new SelectedAiDesignFile(Source(source.Root,"2.pdf",40*MiB),"b") };
        var first = await files.SnapshotAsync(id,selected,[],default); first.IsSuccess.ShouldBeTrue();
        if (extra) (await files.SnapshotAsync(id,[new(Source(source.Root,"3.txt",1),"c")],first.Value,default)).IsFailure.ShouldBeTrue();
        (await files.VerifyAsync(id,first.Value,default)).IsSuccess.ShouldBeTrue();
    }
    [Fact]
    public async Task Coordinator_snapshots_explicit_selection_and_blocks_running_edits_and_tampered_retry()
    {
        using TempWorkspace source=new(); using TempWorkspace owned=new(); var store=new FileAiDesignRequestStore(owned.Root);
        var attachments=new FileAiDesignAttachments(owned.Root); var templates=new PromptTemplateService();
        using var coordinator=new AiDesignCoordinator(store,new TestChannel(store.WorkspaceIdentity),new AiDesignBrowserLease(owned.Root),attachments:attachments);
        var prompt=templates.Compose("subject",new Dictionary<string,string>{{"subject","literal cat"}},[],"en").Value;
        var draft=(await coordinator.CreateDraftAsync(prompt,default)).Value;
        var updated=(await coordinator.AddAttachmentsAsync(draft.Request.RequestId,[new(Source(source.Root,"ref.png",2),"subject reference")],default)).Value;
        updated.Request.Attachments.Count.ShouldBe(1);updated.ManualHandoffText.ShouldContain("attachment-01.png");
        (await coordinator.StartAsync(draft.Request.RequestId,default)).Value.Request.State.ShouldBe(AiDesignState.Queued);
        (await coordinator.AddAttachmentsAsync(draft.Request.RequestId,[new(Source(source.Root,"second.txt",1),"text")],default)).IsFailure.ShouldBeTrue();
        (await coordinator.SaveDraftAsync(draft.Request.RequestId,new(prompt,[]),default)).IsFailure.ShouldBeTrue();
        (await coordinator.StopAsync(draft.Request.RequestId,default)).IsSuccess.ShouldBeTrue();
        var a=updated.Request.Attachments[0];string path=Path.Combine(owned.Root,"AiDesign","Requests",draft.Request.RequestId.ToString("D"),a.Snapshot.RelativePath);
        File.SetAttributes(path,FileAttributes.Normal);await File.WriteAllBytesAsync(path,new byte[]{8,9});
        (await coordinator.RetryAsync(draft.Request.RequestId,default)).IsFailure.ShouldBeTrue();
    }
    private sealed class TestChannel(string root):IAiDesignChannel
    {
        public Task<PrintFlow.Domain.Results.OperationResult<AiDesignBridgeReadiness>> CheckAsync(CancellationToken ct)=>Task.FromResult(PrintFlow.Domain.Results.OperationResult.Ok(new AiDesignBridgeReadiness(true,1,"1","test",root,null,Guid.NewGuid())));
        public Task<PrintFlow.Domain.Results.OperationResult<PrintFlow.Domain.Results.Unit>> EnqueueAsync(AiDesignRequest r,CancellationToken ct)=>Task.FromResult(PrintFlow.Domain.Results.OperationResult.Ok());
        public Task<PrintFlow.Domain.Results.OperationResult<PrintFlow.Domain.Results.Unit>> StopAsync(AiDesignFence f,CancellationToken ct)=>Task.FromResult(PrintFlow.Domain.Results.OperationResult.Fail<PrintFlow.Domain.Results.Unit>(PrintFlow.Domain.Results.FailureCode.AdapterUnavailable,"Remote stop unverified"));
    }
    [Fact]
    public async Task Original_unchanged_and_snapshot_tamper_rejected()
    {
        using TempWorkspace source = new(); using TempWorkspace owned = new(); var id = Guid.NewGuid();
        var path = Source(source.Root,"same.png",5); var before = SHA256.HashData(await File.ReadAllBytesAsync(path));
        var files = new FileAiDesignAttachments(owned.Root); var result = (await files.SnapshotAsync(id,[new(path,"one")],[],default)).Value;
        var snapshot = Path.Combine(owned.Root,"AiDesign","Requests",id.ToString("D"),result[0].Snapshot.RelativePath);
        (await File.ReadAllBytesAsync(path)).ShouldBe(new byte[5]); SHA256.HashData(await File.ReadAllBytesAsync(path)).ShouldBe(before);
        File.SetAttributes(snapshot,FileAttributes.Normal); await File.WriteAllBytesAsync(snapshot,new byte[]{1,2,3,4,5});
        (await files.VerifyAsync(id,result,default)).IsFailure.ShouldBeTrue();
    }
    [Fact]
    public async Task Names_do_not_rebind_after_remove_even_if_all_previous_files_are_removed()
    {
        using TempWorkspace source = new(); using TempWorkspace owned = new(); var id = Guid.NewGuid(); var files = new FileAiDesignAttachments(owned.Root);
        var first = (await files.SnapshotAsync(id,[new(Source(source.Root,"same.png",1),"a")],[],default)).Value;
        var next = (await files.SnapshotAsync(id,[new(Source(source.Root,"other.png",1),"b")],[],default)).Value;
        next[0].Order.ShouldBe(2); next[0].UploadName.ShouldBe("attachment-02.png"); next[0].Id.ShouldNotBe(first[0].Id);
    }
    [Theory]
    [InlineData("empty.txt",0)][InlineData("bad.exe",1)]
    public async Task Empty_and_unsupported_files_are_rejected(string name,long size)
    {
        using TempWorkspace source = new(); using TempWorkspace owned = new();
        (await new FileAiDesignAttachments(owned.Root).SnapshotAsync(Guid.NewGuid(),[new(Source(source.Root,name,size),"x")],[],default)).IsFailure.ShouldBeTrue();
    }
    [Fact]
    public async Task Traversal_cross_request_unselected_and_reparse_files_are_rejected()
    {
        using TempWorkspace source = new(); using TempWorkspace owned = new(); var id = Guid.NewGuid(); var files = new FileAiDesignAttachments(owned.Root);
        var path = Source(source.Root,"same.txt",1); var a = (await files.SnapshotAsync(id,[new(path,"purpose")],[],default)).Value[0];
        (await files.VerifyAsync(Guid.NewGuid(),[a],default)).IsFailure.ShouldBeTrue();
        (await files.VerifyAsync(id,[a with { Snapshot = new(id,"../../outside.txt") }],default)).IsFailure.ShouldBeTrue();
        (await files.VerifyAsync(id,[a with { Snapshot = new(id,path) }],default)).IsFailure.ShouldBeTrue();
        // Windows directory junction requires no developer-mode privilege.
        string junction = Path.Combine(source.Root,"linked"); Directory.CreateDirectory(Path.Combine(source.Root,"target"));
        Source(Path.Combine(source.Root,"target"),"file.txt",1);
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        psi.ArgumentList.Add("/c"); psi.ArgumentList.Add("mklink"); psi.ArgumentList.Add("/J"); psi.ArgumentList.Add(junction); psi.ArgumentList.Add(Path.Combine(source.Root,"target"));
        using var p = System.Diagnostics.Process.Start(psi)!; await p.WaitForExitAsync(); p.ExitCode.ShouldBe(0);
        try { (await files.SnapshotAsync(id,[new(Path.Combine(junction,"file.txt"),"x")],[a],default)).IsFailure.ShouldBeTrue(); }
        finally { Directory.Delete(junction); }
    }
}
