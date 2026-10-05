using System.Diagnostics;
using System.IO;
using PrintFlow.Infrastructure.AiDesign;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Tests.Unit.AiDesign;

namespace PrintFlow.Tests.Integration.AiDesign;
public sealed class AiDesignOwnedStagingFilesTests
{
    [Fact]
    public async Task Readonly_only_staging_cleans_successfully_when_no_other_attribute_bits_remain()
    {
        using TempWorkspace root=new();var batch=AiDesignOwnedStagingFiles.BeginAttachments(root.Root,Guid.NewGuid());
        var a=batch.Create(1,".png");await a.Snapshot.Output.WriteAsync(new byte[]{7});await a.PublishSnapshotAsync(default);
        File.SetAttributes(a.Snapshot.Path,FileAttributes.ReadOnly);
        batch.Dispose();a.Snapshot.CleanupRefused.ShouldBeFalse();File.Exists(a.Snapshot.Path).ShouldBeFalse();
    }
    [Fact]
    public async Task Parent_directory_is_pinned_through_verified_publication()
    {
        using TempWorkspace root=new();var owned=AiDesignOwnedStagingFiles.CreateRequest(root.Root,Guid.NewGuid(),AiDesignOwnedStagingFiles.RequestPurpose.RequestAuthority);
        await owned.Output.WriteAsync(new byte[]{7});await owned.CloseFlushedAsync(default);
        string original=owned.Path,folder=Path.GetDirectoryName(original)!;bool attempted=false,swapped=false;
        owned.AfterVerificationForTest=()=>{attempted=true;try{Directory.Move(folder,folder+".swapped");Directory.CreateDirectory(folder);swapped=true;}catch(IOException){}};
        await owned.PublishAsync(default);attempted.ShouldBeTrue();swapped.ShouldBeFalse();File.Exists(original).ShouldBeFalse();
        File.ReadAllBytes(Path.Combine(folder,"request.json")).ShouldBe(new byte[]{7});owned.Dispose();owned.CleanupRefused.ShouldBeFalse();
    }
    [Theory]
    [InlineData("cleanup")][InlineData("publication")][InlineData("attachment-move")][InlineData("readonly")]
    public async Task A_swap_after_verification_cannot_redirect_the_owned_mutation(string operation)
    {
        using TempWorkspace root=new();using var batch=AiDesignOwnedStagingFiles.BeginAttachments(root.Root,Guid.NewGuid());
        AiDesignOwnedStagingFiles.Attachment? attachment=null;
        var owned=operation is "readonly" or "attachment-move" ? (attachment=batch.Create(1,".png")).Snapshot
            : AiDesignOwnedStagingFiles.CreateRequest(root.Root,Guid.NewGuid(),AiDesignOwnedStagingFiles.RequestPurpose.RequestAuthority);
        await owned.Output.WriteAsync(new byte[]{7});await owned.CloseFlushedAsync(default);
        if(operation=="readonly")await attachment!.PublishSnapshotAsync(default);
        string originalPath=owned.Path;bool attempted=false,swapped=false;
        owned.AfterVerificationForTest=()=>
        {
            attempted=true;
            try{File.Move(originalPath,originalPath+".original");File.WriteAllBytes(originalPath,[9]);swapped=true;}
            catch(IOException){ }
        };
        if(operation=="cleanup")owned.Dispose();
        else if(operation=="publication")await owned.PublishAsync(default);
        else if(operation=="attachment-move")await attachment!.PublishSnapshotAsync(default);
        else attachment!.MarkReadOnly();
        attempted.ShouldBeTrue();swapped.ShouldBeFalse("The file must remain pinned between verification and mutation.");
        if(operation=="cleanup")File.Exists(originalPath).ShouldBeFalse();
        if(operation=="readonly"){File.ReadAllBytes(owned.Path).ShouldBe(new byte[]{7});(File.GetAttributes(owned.Path)&FileAttributes.ReadOnly).ShouldBe(FileAttributes.ReadOnly);}
        if(operation=="publication"){File.Exists(originalPath).ShouldBeFalse();File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(originalPath)!,"request.json")).ShouldBe(new byte[]{7});}
        if(operation=="attachment-move"){File.Exists(originalPath).ShouldBeFalse();File.ReadAllBytes(owned.Path).ShouldBe(new byte[]{7});}
        owned.AfterVerificationForTest=null;owned.Dispose();
        owned.CleanupRefused.ShouldBeFalse();
    }
    [Fact]
    public async Task Removing_a_committed_attachment_from_the_draft_keeps_its_snapshot()
    {
        using TempWorkspace root=new();using TempWorkspace selected=new();var d=AiDesignTransitionsTests.Draft();
        string source=selected.CreateSourceFile("reference.png",[7]);var attachments=(await new FileAiDesignAttachments(root.Root).SnapshotAsync(d.RequestId,[new(source,"reference")],[],default)).Value;
        var store=new FileAiDesignRequestStore(root.Root);var r=(await store.CreateAsync(d.RequestId,new(d.Prompt,attachments),default)).Value;
        var removed=await store.ChangeAsync(r.Fence,new AiDesignEvent.DraftSaved(r.Prompt,[],r.NextAttachmentOrder),default);removed.IsSuccess.ShouldBeTrue();
        removed.Value.Attachments.ShouldBeEmpty();
        (await new FileAiDesignAttachments(root.Root).VerifyAsync(r.RequestId,attachments,default)).IsSuccess.ShouldBeTrue();
        File.ReadAllBytes(source).ShouldBe(new byte[]{7});
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Failure_or_cancel_removes_only_the_successfully_created_staging_file(bool cancel)
    {
        using TempWorkspace root=new();Guid id=Guid.NewGuid();
        var owned=AiDesignOwnedStagingFiles.CreateRequest(root.Root,id,AiDesignOwnedStagingFiles.RequestPurpose.RequestAuthority);
        string path=owned.Path,unknown=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!,"unknown.tmp");
        File.WriteAllBytes(unknown,[8]);await owned.Output.WriteAsync(new byte[]{7});
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        if(cancel)await Should.ThrowAsync<OperationCanceledException>(()=>owned.PublishAsync(cancelled.Token));
        owned.Dispose();File.Exists(path).ShouldBeFalse();File.ReadAllBytes(unknown).ShouldBe(new byte[]{8});
    }
    [Fact]
    public void Failed_CreateNew_collision_does_not_issue_cleanup_authority()
    {
        using TempWorkspace root=new();Guid id=Guid.NewGuid(),tempId=Guid.NewGuid();
        string folder=System.IO.Path.Combine(root.Root,"AiDesign","Requests",id.ToString("D"));Directory.CreateDirectory(folder);
        string collision=System.IO.Path.Combine(folder,$".request-{tempId:N}.tmp");File.WriteAllBytes(collision,[9]);File.SetAttributes(collision,FileAttributes.ReadOnly|FileAttributes.Hidden);
        FileAttributes before=File.GetAttributes(collision);
        Should.Throw<IOException>(()=>AiDesignOwnedStagingFiles.CreateRequestForTest(root.Root,id,AiDesignOwnedStagingFiles.RequestPurpose.RequestAuthority,tempId));
        File.ReadAllBytes(collision).ShouldBe(new byte[]{9});File.GetAttributes(collision).ShouldBe(before);
    }
    [Theory]
    [InlineData(AiDesignOwnedStagingFiles.RequestPurpose.RequestAuthority,"request.json")]
    [InlineData(AiDesignOwnedStagingFiles.RequestPurpose.AttachmentSequence,".attachment-sequence")]
    [InlineData(AiDesignOwnedStagingFiles.RequestPurpose.BridgeQueue,"bridge-queue.local.json")]
    public async Task Atomic_publication_consumes_cleanup_authority(AiDesignOwnedStagingFiles.RequestPurpose purpose,string destination)
    {
        using TempWorkspace root=new();Guid id=Guid.NewGuid();var owned=AiDesignOwnedStagingFiles.CreateRequest(root.Root,id,purpose);
        string temporary=owned.Path,folder=System.IO.Path.GetDirectoryName(owned.Path)!;await owned.Output.WriteAsync(new byte[]{7});await owned.PublishAsync(default);owned.Dispose();
        File.Exists(temporary).ShouldBeFalse();owned.CleanupRefused.ShouldBeFalse();
        File.ReadAllBytes(System.IO.Path.Combine(folder,destination)).ShouldBe(new byte[]{7});
        using var next=AiDesignOwnedStagingFiles.CreateRequest(root.Root,id,purpose);await next.Output.WriteAsync(new byte[]{8});await next.PublishAsync(default);
        File.ReadAllBytes(System.IO.Path.Combine(folder,destination)).ShouldBe(new byte[]{8});
    }
    [Fact]
    public async Task Connection_publication_is_confined_to_the_installation_heartbeat()
    {
        using TempWorkspace root=new();var owned=AiDesignOwnedStagingFiles.CreateConnection(root.Root);await owned.Output.WriteAsync(new byte[]{7});await owned.PublishAsync(default);owned.Dispose();
        File.ReadAllBytes(System.IO.Path.Combine(root.Root,"connection.local.json")).ShouldBe(new byte[]{7});
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Batch_rollback_retains_unknown_sibling_and_commit_retains_owned_files(bool commit)
    {
        using TempWorkspace root=new();var batch=AiDesignOwnedStagingFiles.BeginAttachments(root.Root,Guid.NewGuid());
        var a=batch.Create(1,".png");await a.Snapshot.Output.WriteAsync(new byte[]{7});await a.PublishSnapshotAsync(default);
        var seal=a.CreateIdentity();await seal.Output.WriteAsync(new byte[]{8});await seal.CloseFlushedAsync(default);a.MarkReadOnly();
        string unknown=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(a.Snapshot.Path)!,"unknown.tiff");File.WriteAllBytes(unknown,[9]);
        FileAttributes attributes=File.GetAttributes(a.Snapshot.Path);
        if(commit)batch.Commit();batch.Dispose();
        a.Snapshot.CleanupRefused.ShouldBeFalse();seal.CleanupRefused.ShouldBeFalse();
        File.Exists(a.Snapshot.Path).ShouldBe(commit);File.Exists(seal.Path).ShouldBe(commit);File.ReadAllBytes(unknown).ShouldBe(new byte[]{9});
        Directory.Exists(System.IO.Path.GetDirectoryName(unknown)).ShouldBeTrue();
        if(commit)File.GetAttributes(a.Snapshot.Path).ShouldBe(attributes);
    }
    [Fact]
    public async Task Replaced_file_identity_is_preserved_without_attribute_mutation()
    {
        using TempWorkspace root=new();var owned=AiDesignOwnedStagingFiles.CreateRequest(root.Root,Guid.NewGuid(),AiDesignOwnedStagingFiles.RequestPurpose.RequestAuthority);
        await owned.Output.WriteAsync(new byte[]{7});await owned.CloseFlushedAsync(default);
        File.Move(owned.Path,owned.Path+".original");File.WriteAllBytes(owned.Path,[9]);File.SetAttributes(owned.Path,FileAttributes.ReadOnly|FileAttributes.Hidden);
        var before=File.GetAttributes(owned.Path);owned.Dispose();owned.CleanupRefused.ShouldBeTrue();
        File.ReadAllBytes(owned.Path).ShouldBe(new byte[]{9});File.GetAttributes(owned.Path).ShouldBe(before);File.ReadAllBytes(owned.Path+".original").ShouldBe(new byte[]{7});
    }
    [Fact]
    public async Task Shared_hardlink_is_preserved_before_readonly_mutation()
    {
        using TempWorkspace root=new();var owned=AiDesignOwnedStagingFiles.CreateRequest(root.Root,Guid.NewGuid(),AiDesignOwnedStagingFiles.RequestPurpose.RequestAuthority);
        await owned.Output.WriteAsync(new byte[]{7});await owned.CloseFlushedAsync(default);string alias=System.IO.Path.Combine(root.Root,"external.tiff");
        await Link("/H",alias,owned.Path);File.SetAttributes(owned.Path,FileAttributes.ReadOnly|FileAttributes.Hidden);var before=File.GetAttributes(alias);
        owned.Dispose();owned.CleanupRefused.ShouldBeTrue();File.ReadAllBytes(alias).ShouldBe(new byte[]{7});File.GetAttributes(alias).ShouldBe(before);File.Exists(owned.Path).ShouldBeTrue();
    }
    [Fact]
    public async Task Reparse_replacement_preserves_external_file_and_original_error()
    {
        using TempWorkspace root=new();using TempWorkspace external=new();var owned=AiDesignOwnedStagingFiles.CreateRequest(root.Root,Guid.NewGuid(),AiDesignOwnedStagingFiles.RequestPurpose.RequestAuthority);
        await owned.Output.WriteAsync(new byte[]{7});await owned.CloseFlushedAsync(default);
        string folder=System.IO.Path.GetDirectoryName(owned.Path)!,saved=folder+"-saved",sentinel=System.IO.Path.Combine(external.Root,System.IO.Path.GetFileName(owned.Path));
        File.WriteAllBytes(sentinel,[9]);File.SetAttributes(sentinel,FileAttributes.ReadOnly|FileAttributes.Hidden);var before=File.GetAttributes(sentinel);
        Directory.Move(folder,saved);await Link("/J",folder,external.Root);
        try{owned.Dispose();owned.CleanupRefused.ShouldBeTrue();File.ReadAllBytes(sentinel).ShouldBe(new byte[]{9});File.GetAttributes(sentinel).ShouldBe(before);}
        finally{Directory.Delete(folder);Directory.Move(saved,folder);}
    }
    private static async Task Link(string flag,string link,string target)
    {
        var info=new ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in new[]{"/c","mklink",flag,link,target})info.ArgumentList.Add(arg);
        using var process=Process.Start(info)!;await process.WaitForExitAsync();process.ExitCode.ShouldBe(0,await process.StandardError.ReadToEndAsync());
    }
}
