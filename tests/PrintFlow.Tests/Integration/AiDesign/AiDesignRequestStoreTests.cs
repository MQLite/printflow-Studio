using System.IO;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.AiDesign;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Unit.AiDesign;
using PrintFlow.Workflow.AiDesign;

namespace PrintFlow.Tests.Integration.AiDesign;

public sealed class AiDesignRequestStoreTests
{
    [Fact]
    public async Task Structural_corruption_is_rejected_without_mutating_authority()
    {
        using TempWorkspace files = new(); var store = new FileAiDesignRequestStore(files.Root); var d = AiDesignTransitionsTests.Draft();
        (await store.CreateAsync(d.RequestId, new(d.Prompt, []), default)).IsSuccess.ShouldBeTrue();
        string path = Path.Combine(files.Root, "AiDesign", "Requests", d.RequestId.ToString("D"), "request.json");
        var node = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!; node["prompt"] = null; node["attachments"] = null;
        string corrupt = node.ToJsonString(); await File.WriteAllTextAsync(path, corrupt);
        var read = await store.ReadAsync(d.RequestId, default); read.IsFailure.ShouldBeTrue(); read.Failure.Code.ShouldBe(FailureCode.PersistenceError);
        (await File.ReadAllTextAsync(path)).ShouldBe(corrupt); (await store.ListAsync(default)).IsFailure.ShouldBeTrue(); (await File.ReadAllTextAsync(path)).ShouldBe(corrupt);
    }
    [Fact]
    public async Task Submit_callback_requires_all_durable_upload_receipts_and_runs_once()
    {
        using TempWorkspace files = new(); var store = new FileAiDesignRequestStore(files.Root); var d = AiDesignTransitionsTests.Draft();
        var a = AiDesignTransitionsTests.Attachment(d.RequestId,1); var b = AiDesignTransitionsTests.Attachment(d.RequestId,2);
        var r = (await store.CreateAsync(d.RequestId,new(d.Prompt,[a,b]),default)).Value;
        r = (await store.ChangeAsync(r.Fence,new AiDesignEvent.Queued(Guid.NewGuid(),"token",1,1,Guid.NewGuid()),default)).Value;
        r = (await store.ChangeAsync(r.Fence,new AiDesignEvent.UploadStarted("tab","conversation"),default)).Value;
        int clicks = 0; Task<OperationResult<PrintFlow.Domain.Results.Unit>> Click(CancellationToken _) { clicks++; return Task.FromResult(OperationResult.Ok()); }
        (await store.SubmitOnceAsync(r.Fence,Click,default)).IsFailure.ShouldBeTrue(); clicks.ShouldBe(0);
        r = (await store.ChangeAsync(r.Fence,new AiDesignEvent.UploadConfirmed(new(a.Id,r.AttemptId,"conversation",AiDesignUploadStatus.Uploaded,"a")),default)).Value;
        (await store.SubmitOnceAsync(r.Fence,Click,default)).IsFailure.ShouldBeTrue(); clicks.ShouldBe(0);
        r = (await store.ChangeAsync(r.Fence,new AiDesignEvent.UploadConfirmed(new(b.Id,r.AttemptId,"conversation",AiDesignUploadStatus.Uploaded,"b")),default)).Value;
        var submitted = (await store.SubmitOnceAsync(r.Fence,Click,default)).Value; submitted.State.ShouldBe(AiDesignState.Submitted); clicks.ShouldBe(1);
        (await store.SubmitOnceAsync(submitted.Fence,Click,default)).IsFailure.ShouldBeTrue(); clicks.ShouldBe(1);
    }
    [Fact]
    public async Task Concurrent_CAS_has_one_winner_and_rejects_every_stale_identity()
    {
        using TempWorkspace files = new(); var store = new FileAiDesignRequestStore(files.Root);
        var draft = AiDesignTransitionsTests.Draft();
        var r = (await store.CreateAsync(draft.RequestId, new(draft.Prompt, []), default)).Value;
        var update = new AiDesignEvent.Queued(Guid.NewGuid(), "new-token", Environment.ProcessId, 1, Guid.NewGuid());
        var results = await Task.WhenAll(store.ChangeAsync(r.Fence, update, default), new FileAiDesignRequestStore(files.Root).ChangeAsync(r.Fence, update, default));
        results.Count(x => x.IsSuccess).ShouldBe(1);
        var read = (await store.ReadAsync(r.RequestId, default)).Value; read.StateVersion.ShouldBe(1);
        foreach (var fence in new[] { r.Fence, read.Fence with { RequestId = Guid.NewGuid() }, read.Fence with { AttemptId = Guid.NewGuid() }, read.Fence with { Token = "wrong" } })
            (await store.ChangeAsync(fence, new AiDesignEvent.StopAccepted(), default)).IsFailure.ShouldBeTrue();
        (await store.ReadAsync(r.RequestId, default)).Value.State.ShouldBe(AiDesignState.Queued);
    }

    [Fact]
    public async Task Uncertain_submit_is_durable_interrupted_and_never_replays()
    {
        using TempWorkspace files = new(); var store = new FileAiDesignRequestStore(files.Root);
        var d = AiDesignTransitionsTests.Draft(); var r = (await store.CreateAsync(d.RequestId, new(d.Prompt, []), default)).Value;
        r = (await store.ChangeAsync(r.Fence, new AiDesignEvent.Queued(Guid.NewGuid(), "token", Environment.ProcessId, 1, Guid.NewGuid()), default)).Value;
        int clicks = 0;
        r = (await store.ChangeAsync(r.Fence, new AiDesignEvent.UploadStarted("tab", "conversation"), default)).Value;
        var result = await store.SubmitOnceAsync(r.Fence, _ => { clicks++; return Task.FromResult(OperationResult.Fail<PrintFlow.Domain.Results.Unit>(FailureCode.AdapterUnavailable, "lost confirmation")); }, default);
        result.Value.State.ShouldBe(AiDesignState.Interrupted); result.Value.SubmissionReservedAtUtc.ShouldNotBeNull();
        (await new FileAiDesignRequestStore(files.Root).SubmitOnceAsync(result.Value.Fence, _ => { clicks++; return Task.FromResult(OperationResult.Ok()); }, default)).IsFailure.ShouldBeTrue();
        clicks.ShouldBe(1);
    }

    [Fact]
    public async Task Stop_and_import_race_is_serialized_and_corrupt_authority_is_not_reset()
    {
        using TempWorkspace files = new(); var store = new FileAiDesignRequestStore(files.Root); var d = AiDesignTransitionsTests.Draft();
        var r = (await store.CreateAsync(d.RequestId, new(d.Prompt, []), default)).Value;
        r = (await store.ChangeAsync(r.Fence, new AiDesignEvent.Queued(Guid.NewGuid(), "token", 1, 1, Guid.NewGuid()), default)).Value;
        r = (await store.ChangeAsync(r.Fence, new AiDesignEvent.SubmitReserved("tab", "conversation"), default)).Value;
        r = (await store.ChangeAsync(r.Fence, new AiDesignEvent.SubmitConfirmed(), default)).Value;
        r = (await store.ChangeAsync(r.Fence, new AiDesignEvent.GenerationStarted(), default)).Value;
        var download = new AiDesignDownload(r.RequestId, r.AttemptId, "tab", "conversation", "result", 1, "managed-later", false);
        r = (await store.ChangeAsync(r.Fence, new AiDesignEvent.DownloadRegistered(download), default)).Value;
        r = (await store.ChangeAsync(r.Fence, new AiDesignEvent.DownloadCompleted(download with { IsComplete = true }), default)).Value;
        r = (await store.ChangeAsync(r.Fence, new AiDesignEvent.ValidationStarted(), default)).Value;
        var candidate = new AiDesignCandidate(new(r.RequestId, "Candidates/result.png"), new(PrintFlow.Domain.Files.ImageFormat.Png, 1,
            PrintFlow.Domain.Files.Sha256.Parse(new string('A',64)), 1, 1, null, null, PrintFlow.Domain.Files.ColourMode.Rgb, true));
        r = (await store.ChangeAsync(r.Fence, new AiDesignEvent.CandidatePublished(candidate), default)).Value;
        var results = await Task.WhenAll(store.ChangeAsync(r.Fence, new AiDesignEvent.ImportStarted(), default), store.ChangeAsync(r.Fence, new AiDesignEvent.StopAccepted(), default));
        results.Count(x => x.IsSuccess).ShouldBe(1);
        var final = (await store.ReadAsync(r.RequestId, default)).Value;
        final.State.ShouldBeOneOf(AiDesignState.Importing, AiDesignState.Cancelled); final.Candidate.ShouldBe(candidate);
        var path = Path.Combine(files.Root, "AiDesign", "Requests", r.RequestId.ToString("D"), "request.json");
        await File.WriteAllTextAsync(path, "{broken");
        (await store.ReadAsync(r.RequestId, default)).IsFailure.ShouldBeTrue();
        (await store.CreateAsync(r.RequestId, new(d.Prompt, []), default)).IsFailure.ShouldBeTrue();
        (await File.ReadAllTextAsync(path)).ShouldBe("{broken");
    }
}
