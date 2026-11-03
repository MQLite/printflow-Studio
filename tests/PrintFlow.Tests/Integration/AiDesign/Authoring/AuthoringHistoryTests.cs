using System.IO;
using System.Text.Json.Nodes;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.AiDesign.Authoring;
using static PrintFlow.Tests.Integration.AiDesign.Authoring.AuthoringPersistenceTests;

namespace PrintFlow.Tests.Integration.AiDesign.Authoring;

public sealed class AuthoringHistoryTests
{
    [Fact]
    public async Task History_follows_committed_chain_and_restore_appends_without_overwriting()
    {
        using TempWorkspace root = new(); var module = Module(root.Root);
        var draft = (await module.CreateAsync(BuiltInAiTemplates.All[0].Reference, default)).Value;
        draft = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.ReplaceDocument(Document("旧稿\r\n")), default)).Value;
        long oldRevision = draft.Revision;
        draft = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.Recompose(true), default)).Value;
        // An orphan file is not part of the committed chain.
        File.WriteAllText(Path.Combine(DraftDirectory(root.Root, draft.Id), $"revision-{Guid.NewGuid():N}.json"), "invalid orphan");
        var history = (await module.ReadHistoryAsync(draft.Id, default)).Value;
        history.Select(x => x.Revision).ShouldBe(new long[] { 2, 1, 0 });
        Text(history.Single(x => x.Revision == oldRevision).FinalDocument).ShouldBe("旧稿\r\n");
        var restored = await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.RestoreRevision(oldRevision), default);
        restored.IsSuccess.ShouldBeTrue(); restored.Value.Revision.ShouldBe(3);
        Text(restored.Value.FinalDocument).ShouldBe("旧稿\r\n");
        (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.RestoreRevision(0), default)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Corrupt_or_cross_draft_historical_identity_is_refused()
    {
        using TempWorkspace root = new(); var module = Module(root.Root);
        var draft = (await module.CreateAsync(BuiltInAiTemplates.All[0].Reference, default)).Value;
        string oldFile = Directory.GetFiles(DraftDirectory(root.Root, draft.Id), "revision-*.json").Single();
        draft = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.ReplaceDocument(Document("current")), default)).Value;
        var json = JsonNode.Parse(File.ReadAllText(oldFile))!; json["value"]!["id"] = Guid.NewGuid();
        File.WriteAllText(oldFile, json.ToJsonString());
        (await module.ReadHistoryAsync(draft.Id, default)).IsFailure.ShouldBeTrue();
        (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.RestoreRevision(0), default)).IsFailure.ShouldBeTrue();
        Text((await module.ReadAsync(draft.Id, default)).Value.FinalDocument).ShouldBe("current");
    }
}
