using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Files;

public sealed class RetentionWorkspaceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Interrupted_copy_or_verified_destination_resumes_without_moving_original(bool destinationAlreadyComplete)
    {
        using TempWorkspace temp = new();
        FileWorkspace workspace = new(temp.Root);
        WorkspaceDirRef session = workspace.CreateSession(SessionId.From(Guid.NewGuid()), DateTimeOffset.UtcNow).Value;
        RetentionFile source = await WorkingCopy(temp, workspace, session);
        RevisionId id = RevisionId.From(Guid.NewGuid());
        string destination = Path.Combine(workspace.ResolveAbsoluteDirectory(session), "Revisions", id.ToString(), source.File.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string staged = destinationAlreadyComplete ? destination : Path.Combine(Path.GetDirectoryName(destination)!, ".retention-copy.partial");
        File.WriteAllBytes(staged, destinationAlreadyComplete ? File.ReadAllBytes(workspace.ResolveAbsolute(source.File)) : [1, 2, 3]);

        var promoted = await workspace.PromoteRevisionAsync(session, id, source, CancellationToken.None);
        promoted.IsSuccess.ShouldBeTrue(promoted.IsFailure ? promoted.Failure.ToString() : "");
        Hash(workspace.ResolveAbsolute(promoted.Value)).ShouldBe(source.Sha256);
        Hash(workspace.ResolveAbsolute(source.File)).ShouldBe(source.Sha256);
        promoted.Value.Area.ShouldBe(WorkspaceArea.Revisions);
        (await workspace.PromoteRevisionAsync(session, id, source, CancellationToken.None)).Value.ShouldBe(promoted.Value);
        Directory.GetFiles(Path.GetDirectoryName(destination)!).Length.ShouldBe(destinationAlreadyComplete ? 1 : 2);
        if (!destinationAlreadyComplete) File.ReadAllBytes(staged).ShouldBe(new byte[] { 1, 2, 3 });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Corrupt_source_or_existing_destination_never_switches_or_deletes_authority(bool corruptDestination)
    {
        using TempWorkspace temp = new();
        FileWorkspace workspace = new(temp.Root);
        WorkspaceDirRef session = workspace.CreateSession(SessionId.From(Guid.NewGuid()), DateTimeOffset.UtcNow).Value;
        RetentionFile source = await WorkingCopy(temp, workspace, session);
        RevisionId id = RevisionId.From(Guid.NewGuid());
        string destination = Path.Combine(workspace.ResolveAbsoluteDirectory(session), "Revisions", id.ToString(), source.File.FileName);
        string corrupt = corruptDestination ? destination : workspace.ResolveAbsolute(source.File);
        Directory.CreateDirectory(Path.GetDirectoryName(corrupt)!);
        File.WriteAllText(corrupt, "synthetic corruption");
        var result = await workspace.PromoteRevisionAsync(session, id, source, CancellationToken.None);
        result.IsFailure.ShouldBeTrue();
        File.Exists(workspace.ResolveAbsolute(source.File)).ShouldBeTrue();
        File.ReadAllText(corrupt).ShouldBe("synthetic corruption");
    }

    [Fact]
    public async Task Cross_session_source_and_area_spoofing_are_refused_before_mutation()
    {
        using TempWorkspace temp = new();
        FileWorkspace workspace = new(temp.Root);
        WorkspaceDirRef a = workspace.CreateSession(SessionId.From(Guid.NewGuid()), DateTimeOffset.UtcNow).Value;
        WorkspaceDirRef b = workspace.CreateSession(SessionId.From(Guid.NewGuid()), DateTimeOffset.UtcNow).Value;
        RetentionFile source = await WorkingCopy(temp, workspace, a);
        (await workspace.PromoteRevisionAsync(b, RevisionId.From(Guid.NewGuid()), source, CancellationToken.None))
            .IsFailure.ShouldBeTrue();
        workspace.CleanupWorking(b, new([], [source])).IsFailure.ShouldBeTrue();
        WorkspaceFileRef disguised = WorkspaceFileRef.Create(source.File.RelativePath.Replace("/Working/", "/Source/", StringComparison.Ordinal), WorkspaceArea.Working);
        workspace.CleanupWorking(a, new([], [new(disguised, source.Sha256)])).IsFailure.ShouldBeTrue();
        Hash(workspace.ResolveAbsolute(source.File)).ShouldBe(source.Sha256);
        // The lexical guard remains the first refusal, before any resolver can touch disk.
        Should.Throw<ArgumentException>(() => WorkspaceFileRef.Create("../outside.png", WorkspaceArea.Working));
    }

    [Fact]
    public async Task Complete_delete_plan_is_verified_before_first_deletion_and_unknown_files_remain()
    {
        using TempWorkspace temp = new();
        FileWorkspace workspace = new(temp.Root);
        WorkspaceDirRef session = workspace.CreateSession(SessionId.From(Guid.NewGuid()), DateTimeOffset.UtcNow).Value;
        RetentionFile first = await WorkingCopy(temp, workspace, session);
        RetentionFile second = new((await workspace.CreateWorkingCopyAsync(session, AttemptId.From(Guid.NewGuid()),
            first.File, CancellationToken.None)).Value, first.Sha256);
        string unknown = Path.Combine(workspace.ResolveAbsoluteDirectory(session), "Working", "unknown.png");
        File.WriteAllText(unknown, "keep");
        File.WriteAllText(workspace.ResolveAbsolute(second.File), "mismatch");
        workspace.CleanupWorking(session, new([], [first, second])).IsFailure.ShouldBeTrue();
        File.Exists(workspace.ResolveAbsolute(first.File)).ShouldBeTrue();
        File.Exists(workspace.ResolveAbsolute(second.File)).ShouldBeTrue();
        File.ReadAllText(unknown).ShouldBe("keep");
        workspace.CleanupWorking(session, new([first], [first])).IsFailure.ShouldBeTrue();
        File.Exists(workspace.ResolveAbsolute(first.File)).ShouldBeTrue();
    }

    [Fact]
    public async Task Deletion_interruption_leaves_durable_authority_intact_and_retry_is_idempotent()
    {
        using TempWorkspace temp = new();
        FileWorkspace workspace = new(temp.Root);
        WorkspaceDirRef session = workspace.CreateSession(SessionId.From(Guid.NewGuid()), DateTimeOffset.UtcNow).Value;
        RetentionFile first = await WorkingCopy(temp, workspace, session);
        RetentionFile second = new((await workspace.CreateWorkingCopyAsync(session, AttemptId.From(Guid.NewGuid()),
            first.File, CancellationToken.None)).Value, first.Sha256);
        WorkspaceFileRef durable = (await workspace.PromoteRevisionAsync(session, RevisionId.From(Guid.NewGuid()), first, CancellationToken.None)).Value;
        WorkingCleanupPlan plan = new([new(durable, first.Sha256)], [first, second]);
        using (FileStream locked = new(workspace.ResolveAbsolute(second.File), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failed = workspace.CleanupWorking(session, plan);
            failed.IsFailure.ShouldBeTrue();
            failed.Failure.TechnicalDetail.ShouldContain("1 deletions");
            File.Exists(workspace.ResolveAbsolute(first.File)).ShouldBeFalse();
            Hash(workspace.ResolveAbsolute(durable)).ShouldBe(first.Sha256);
        }
        workspace.CleanupWorking(session, plan).Value.DeletedCount.ShouldBe(1);
        workspace.CleanupWorking(session, plan).Value.DeletedCount.ShouldBe(0);
        Hash(workspace.ResolveAbsolute(durable)).ShouldBe(first.Sha256);
    }

    [Theory]
    [InlineData("synthetic.tif")]
    [InlineData("renamed.png")]
    public async Task TIFF_cannot_enter_hard_deletion_even_as_a_redundant_copy(string name)
    {
        using TempWorkspace temp = new();
        FileWorkspace workspace = new(temp.Root);
        WorkspaceDirRef session = workspace.CreateSession(SessionId.From(Guid.NewGuid()), DateTimeOffset.UtcNow).Value;
        RetentionFile source = await WorkingCopy(temp, workspace, session, name, [73, 73, 42, 0, 8, 0, 0, 0]);
        workspace.CleanupWorking(session, new([], [source])).IsFailure.ShouldBeTrue();
        Hash(workspace.ResolveAbsolute(source.File)).ShouldBe(source.Sha256);
    }

    [Fact]
    public void Readonly_hard_link_cannot_modify_external_file_attributes_during_cleanup()
    {
        using TempWorkspace temp = new();
        FileWorkspace workspace = new(temp.Root);
        WorkspaceDirRef session = workspace.CreateSession(SessionId.From(Guid.NewGuid()), DateTimeOffset.UtcNow).Value;
        string external = temp.CreateSourceFile("external.png", SyntheticImages.Png());
        File.SetAttributes(external, FileAttributes.ReadOnly);
        WorkspaceFileRef reference = WorkspaceFileRef.Create(session.RelativePath + "/Working/linked.png", WorkspaceArea.Working);
        string link = workspace.ResolveAbsolute(reference);
        CreateLink(link, external, "/H");
        try
        {
            var result = workspace.CleanupWorking(session, new([], [new(reference, Hash(external))]));
            result.IsFailure.ShouldBeTrue();
            File.GetAttributes(external).HasFlag(FileAttributes.ReadOnly).ShouldBeTrue();
            File.Exists(link).ShouldBeTrue();
        }
        finally
        {
            File.SetAttributes(external, FileAttributes.Normal);
            File.Delete(link);
        }
    }

    [Theory]
    [InlineData("Working")]
    [InlineData("Revisions")]
    public async Task Junction_to_another_synthetic_root_is_never_followed_for_cleanup_or_promotion(string area)
    {
        using TempWorkspace temp = new();
        using TempWorkspace outside = new();
        FileWorkspace workspace = new(temp.Root);
        WorkspaceDirRef session = workspace.CreateSession(SessionId.From(Guid.NewGuid()), DateTimeOffset.UtcNow).Value;
        RetentionFile source = await WorkingCopy(temp, workspace, session);
        string root = workspace.ResolveAbsoluteDirectory(session);
        RevisionId destinationId = RevisionId.From(Guid.NewGuid());
        string segment = area == "Working" ? "linked" : destinationId.ToString();
        string link = Path.Combine(root, area, segment);
        string external = outside.CreateSourceFile(source.File.FileName, SyntheticImages.Png());
        Sha256 hash = Hash(external);
        CreateLink(link, outside.Root, "/J");
        try
        {
            WorkspaceFileRef escaped = WorkspaceFileRef.Create($"{session.RelativePath}/{area}/{segment}/{source.File.FileName}",
                area == "Working" ? WorkspaceArea.Working : WorkspaceArea.Revisions);
            workspace.VerifyRetentionFiles(session, [new(escaped, hash)]).IsFailure.ShouldBeTrue();
            if (area == "Working")
            {
                workspace.ListWorkingFiles(session).IsFailure.ShouldBeTrue();
                workspace.CleanupWorking(session, new([], [new(escaped, hash)])).IsFailure.ShouldBeTrue();
                (await workspace.PromoteRevisionAsync(session, RevisionId.From(Guid.NewGuid()), new(escaped, hash), CancellationToken.None))
                    .IsFailure.ShouldBeTrue();
            }
            else
                (await workspace.PromoteRevisionAsync(session, destinationId, source, default)).IsFailure.ShouldBeTrue();
            Hash(external).ShouldBe(hash);
        }
        finally
        {
            // Delete only the synthetic junction itself, before TempWorkspace's normal disposal.
            File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint).ShouldBeTrue();
            Directory.Delete(link);
        }
    }

    private static void CreateLink(string link, string target, string kind)
    {
        ProcessStartInfo start = new("cmd.exe") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("mklink");
        start.ArgumentList.Add(kind);
        start.ArgumentList.Add(link);
        start.ArgumentList.Add(target);
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.ShouldBe(0, output);
    }

    private static async Task<RetentionFile> WorkingCopy(TempWorkspace temp, FileWorkspace workspace, WorkspaceDirRef session,
        string name = "input.png", byte[]? bytes = null)
    {
        string original = temp.CreateSourceFile(name, bytes ?? SyntheticImages.Png());
        WorkspaceFileRef snapshot = (await workspace.ImportSourceAsync(session, original, CancellationToken.None)).Value;
        WorkspaceFileRef working = (await workspace.CreateWorkingCopyAsync(session, AttemptId.From(Guid.NewGuid()), snapshot, CancellationToken.None)).Value;
        return new(working, Hash(original));
    }

    private static Sha256 Hash(string path) => Sha256.Parse(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
}
