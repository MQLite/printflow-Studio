using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Infrastructure.AiDesign;
using PrintFlow.Infrastructure.AiDesign.Authoring;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.AiDesign.Authoring;

namespace PrintFlow.Tests.Integration.AiDesign.Authoring;

public sealed class AuthoringPersistenceTests
{
    [Fact]
    public async Task Authoring_commit_pins_revision_identity_until_the_current_pointer_is_published()
    {
        using TempWorkspace root = new(); Guid draftId = Guid.NewGuid(), versionId = Guid.NewGuid();
        using var version = AiDesignOwnedStagingFiles.CreateAuthoring(root.Root, draftId, versionId);
        using var pointer = AiDesignOwnedStagingFiles.CreateAuthoring(root.Root, draftId, null);
        await version.Output.WriteAsync(new byte[] { 7 }); await pointer.Output.WriteAsync(new byte[] { 1 });
        bool attempted = false, renamed = false, overwritten = false;
        // Existing primitive hook pauses at the actual pointer-commit boundary, after the
        // version move. No timing, polling, or new test hook is involved.
        pointer.AfterVerificationForTest = () =>
        {
            attempted = true;
            try { File.Move(version.Path, version.Path + ".substituted"); renamed = true; } catch (IOException) { }
            try { File.WriteAllBytes(version.Path, [9]); overwritten = true; } catch (IOException) { }
        };

        await AiDesignOwnedStagingFiles.PublishAuthoringAsync(version, pointer, default);

        attempted.ShouldBeTrue(); renamed.ShouldBeFalse(); overwritten.ShouldBeFalse();
        File.ReadAllBytes(version.Path).ShouldBe(new byte[] { 7 });
        File.ReadAllBytes(Path.Combine(DraftDirectory(root.Root, draftId), "current.json")).ShouldBe(new byte[] { 1 });
    }

    [Fact]
    public async Task Null_version_payload_returns_failure_for_read_and_save()
    {
        using TempWorkspace root = new();
        var draft = (await Module(root.Root).CreateAsync(BuiltInAiTemplates.All[0].Reference, default)).Value;
        string path = Directory.GetFiles(DraftDirectory(root.Root, draft.Id), "revision-*.json").Single();
        var json = JsonNode.Parse(File.ReadAllText(path))!; json["value"] = null; File.WriteAllText(path, json.ToJsonString());
        var store = new FileAiAuthoringDraftStore(root.Root);

        (await store.ReadAsync(draft.Id, default)).IsFailure.ShouldBeTrue();
        (await store.SaveAsync(draft, draft.Revision, default)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task Recompose_keeps_the_first_composition_separate_from_the_final_document()
    {
        using TempWorkspace root = new(); var module = Module(root.Root);
        var draft = (await module.CreateAsync(BuiltInAiTemplates.All[0].Reference, default)).Value;
        var firstParts = draft.InitialDocument.Parts;
        draft = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.SetField("姓名", "first change"), default)).Value;
        draft = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.Recompose(true), default)).Value;
        Text(draft.FinalDocument).ShouldContain("first change"); draft.InitialDocument.Parts.ShouldBe(firstParts);
        draft = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.SetField("姓名", "second change"), default)).Value;
        draft = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.Recompose(true), default)).Value;
        var reopened = (await Module(root.Root).ReadAsync(draft.Id, default)).Value;
        Text(reopened.FinalDocument).ShouldContain("second change"); reopened.InitialDocument.Parts.ShouldBe(firstParts);
    }

    [Fact]
    public async Task Two_store_instances_cannot_overwrite_same_revision()
    {
        using TempWorkspace root = new();
        var draft = (await Module(root.Root).CreateAsync(BuiltInAiTemplates.All[0].Reference, default)).Value;
        var a = new FileAiAuthoringDraftStore(root.Root);
        var b = new FileAiAuthoringDraftStore(root.Root);
        var saves = await Task.WhenAll(
            a.SaveAsync(draft with { Mode = AiAuthoringMode.Detailed }, draft.Revision, default),
            b.SaveAsync(draft with { Fields = new Dictionary<string, string> { ["姓名"] = "Āna" } }, draft.Revision, default));

        saves.Count(x => x.IsSuccess).ShouldBe(1);
        var saved = (await new FileAiAuthoringDraftStore(root.Root).ReadAsync(draft.Id, default)).Value;
        saved.Revision.ShouldBe(1);
        (await a.SaveAsync(draft, null, default)).IsFailure.ShouldBeTrue();
        Directory.GetFiles(DraftDirectory(root.Root, draft.Id), "revision-*.json").Length.ShouldBe(2);
    }

    [Fact]
    public async Task Failed_publication_keeps_last_saved_draft()
    {
        using TempWorkspace root = new();
        var module = Module(root.Root);
        var draft = (await module.CreateAsync(BuiltInAiTemplates.All[0].Reference, default)).Value;
        var manual = Document("  ʻĀna\r\n陈 {{姓名}} e\u0301  ");
        draft = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.ReplaceDocument(manual), default)).Value;
        string directory = DraftDirectory(root.Root, draft.Id), unknown = Path.Combine(directory, "unknown.tmp");
        File.WriteAllText(unknown, "not owned by this save");
        using (var block = new FileStream(Path.Combine(directory, "current.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failed = await module.ApplyAsync(draft.Id, draft.Revision,
                new AiAuthoringEdit.SelectTemplate(BuiltInAiTemplates.All[1].Reference), default);
            failed.IsFailure.ShouldBeTrue();
        }

        var reopened = (await Module(root.Root).ReadAsync(draft.Id, default)).Value;
        Text(reopened.FinalDocument).ShouldBe("  ʻĀna\r\n陈 {{姓名}} e\u0301  ");
        reopened.Template.ShouldBe(draft.Template);
        reopened.Revision.ShouldBe(draft.Revision);
        File.ReadAllText(unknown).ShouldBe("not owned by this save");
        Directory.GetFiles(directory, ".authoring-*.tmp").ShouldBeEmpty();
        Directory.GetFiles(directory, "revision-*.json").Length.ShouldBe(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Corrupt_authority_is_a_failure_without_resetting_saved_content(bool corruptVersion)
    {
        using TempWorkspace root = new();
        var draft = (await Module(root.Root).CreateAsync(BuiltInAiTemplates.All[0].Reference, default)).Value;
        string directory = DraftDirectory(root.Root, draft.Id);
        string file = corruptVersion ? Directory.GetFiles(directory, "revision-*.json").Single() : Path.Combine(directory, "current.json");
        File.WriteAllText(file, "{broken JSON");

        (await new FileAiAuthoringDraftStore(root.Root).ReadAsync(draft.Id, default)).IsFailure.ShouldBeTrue();
        (await new FileAiAuthoringDraftStore(root.Root).SaveAsync(draft, draft.Revision, default)).IsFailure.ShouldBeTrue();
        File.ReadAllText(file).ShouldBe("{broken JSON");
    }

    [Fact]
    public async Task Cancellation_does_not_publish_or_remove_unowned_files()
    {
        using TempWorkspace root = new();
        var draft = (await Module(root.Root).CreateAsync(BuiltInAiTemplates.All[0].Reference, default)).Value;
        string directory = DraftDirectory(root.Root, draft.Id), pointer = Path.Combine(directory, "current.json");
        string before = File.ReadAllText(pointer);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();

        (await new FileAiAuthoringDraftStore(root.Root).SaveAsync(draft, draft.Revision, cancelled.Token)).IsFailure.ShouldBeTrue();
        File.ReadAllText(pointer).ShouldBe(before);
        Directory.GetFiles(directory, "revision-*.json").Length.ShouldBe(1);
    }

    [Fact]
    public async Task Recompose_requires_manual_preservation_and_keeps_recoverable_history()
    {
        using TempWorkspace root = new();
        var module = Module(root.Root);
        var draft = (await module.CreateAsync(BuiltInAiTemplates.All[0].Reference, default)).Value;
        draft = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.ReplaceDocument(Document("手改\r\n e\u0301")), default)).Value;
        draft = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.SetField("姓名", "new name"), default)).Value;
        Text(draft.FinalDocument).ShouldBe("手改\r\n e\u0301");
        module.Export(draft).Text.ShouldBe("手改\r\n e\u0301");
        (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.Recompose(false), default)).IsFailure.ShouldBeTrue();

        var recomposed = (await module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.Recompose(true), default)).Value;
        Text(recomposed.FinalDocument).ShouldContain("new name");
        SnapshotText(root.Root, draft.Id, draft.Revision).ShouldBe("手改\r\n e\u0301");
        var detailed = (await module.ApplyAsync(recomposed.Id, recomposed.Revision, new AiAuthoringEdit.SwitchMode(AiAuthoringMode.Detailed), default)).Value;
        detailed.Id.ShouldBe(draft.Id);
        detailed.FinalDocument.Parts.ShouldBe(recomposed.FinalDocument.Parts);
        detailed.Confirmations.ShouldBeEmpty();
    }

    [Fact]
    public async Task Saved_documents_freeze_callers_collections_and_roundtrip_image_parts()
    {
        using TempWorkspace root = new();
        var module = Module(root.Root);
        var draft = (await module.CreateAsync(BuiltInAiTemplates.All[0].Reference, default)).Value;
        var parts = new List<AiPromptPart> { new AiTextPart(Guid.NewGuid(), "原文\r\n"), new AiImagePart(Guid.NewGuid(), "person-photo") };
        var applying = module.ApplyAsync(draft.Id, draft.Revision, new AiAuthoringEdit.ReplaceDocument(new(parts)), default);
        parts.Clear();
        var saved = (await applying).Value;
        var reopened = (await Module(root.Root).ReadAsync(draft.Id, default)).Value;

        Text(saved.FinalDocument).ShouldBe("原文\r\n");
        reopened.FinalDocument.Parts.ShouldBe(saved.FinalDocument.Parts);
        reopened.FinalDocument.Parts.OfType<AiImagePart>().ShouldHaveSingleItem().SlotId.ShouldBe("person-photo");
        Should.Throw<NotSupportedException>(() => ((IList<AiPromptPart>)reopened.FinalDocument.Parts).Clear());
    }

    [Fact]
    public async Task Owned_replacement_cannot_publish_or_delete_a_substitute()
    {
        using TempWorkspace root = new(); Guid id = Guid.NewGuid();
        var owned = AiDesignOwnedStagingFiles.CreateAuthoring(root.Root, id, null);
        await owned.Output.WriteAsync(new byte[] { 7 }); await owned.CloseFlushedAsync(default);
        string original = owned.Path;
        File.Move(original, original + ".original"); File.WriteAllBytes(original, [9]);
        await Should.ThrowAsync<IOException>(() => owned.PublishAsync(default));
        owned.Dispose();

        File.ReadAllBytes(original).ShouldBe(new byte[] { 9 });
        owned.CleanupRefused.ShouldBeTrue();
        File.Exists(Path.Combine(DraftDirectory(root.Root, id), "current.json")).ShouldBeFalse();
    }

    [Fact]
    public async Task Reparse_ancestry_is_refused_without_writing_external_directory()
    {
        using TempWorkspace root = new(); using TempWorkspace external = new();
        string parent = Path.Combine(root.Root, "AiDesign"); Directory.CreateDirectory(parent);
        string link = Path.Combine(parent, "Authoring");
        var before = Directory.GetFileSystemEntries(external.Root);
        var info = new ProcessStartInfo("cmd") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "/c", "mklink", "/J", link, external.Root }) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!; await process.WaitForExitAsync(); process.ExitCode.ShouldBe(0);
        try
        {
            (await new FileAiTemplateLibraryStore(root.Root).ReadAsync(default)).IsFailure.ShouldBeTrue();
            Directory.GetFileSystemEntries(external.Root).ShouldBe(before);
        }
        finally { Directory.Delete(link); }
    }

    internal static AiAuthoringModule Module(string root) =>
        new(new FileAiAuthoringDraftStore(root), new AiTemplateCatalog(new FileAiTemplateLibraryStore(root)));
    internal static AiPromptDocument Document(string text) => new([new AiTextPart(Guid.NewGuid(), text)]);
    internal static string Text(AiPromptDocument document) => string.Concat(document.Parts.OfType<AiTextPart>().Select(x => x.Text));
    internal static string DraftDirectory(string root, Guid id) => Path.Combine(root, "AiDesign", "Authoring", "Drafts", id.ToString("D"));
    internal static string SnapshotText(string root, Guid id, long revision)
    {
        foreach (string path in Directory.GetFiles(DraftDirectory(root, id), "revision-*.json"))
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            if (json.RootElement.GetProperty("value").GetProperty("revision").GetInt64() != revision) continue;
            return string.Concat(json.RootElement.GetProperty("value").GetProperty("finalDocument").GetProperty("parts")
                .EnumerateArray().Where(x => x.TryGetProperty("text", out _)).Select(x => x.GetProperty("text").GetString()));
        }
        throw new InvalidOperationException("Expected saved historical revision.");
    }
}
