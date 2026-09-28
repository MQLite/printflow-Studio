using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Files;

public sealed class CorrectionPackageStoreTests
{
    [Fact]
    public async Task Existing_writable_partial_is_preserved_while_a_verified_copy_is_created()
    {
        using TempWorkspace temp = new();
        (FileWorkspace workspace, WorkspaceDirRef folder, WorkspaceFileRef source, Sha256 hash) =
            await PackageFixtureAsync(temp);
        FileCorrectionPackageStore store = new(workspace);
        string final = Path.Combine(workspace.ResolveAbsoluteDirectory(folder), "working.png");
        string collision = final + ".partial-12345678";
        byte[] existingBytes = [11, 22, 33, 44];
        File.WriteAllBytes(collision, existingBytes);

        var result = await store.PlaceVerifiedCopyAsync(folder, "working.png", source, hash,
            "partial-12345678", markReadOnly: false, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
        result.Value.ShouldBe(CorrectionFileOutcome.Created);
        File.ReadAllBytes(collision).ShouldBe(existingBytes);
        Hash(final).ShouldBe(hash);
        Hash(workspace.ResolveAbsolute(source)).ShouldBe(hash);
    }

    [Fact]
    public async Task Same_hash_hardlinked_final_is_not_accepted_as_an_independent_copy()
    {
        using TempWorkspace temp = new();
        (FileWorkspace workspace, WorkspaceDirRef folder, WorkspaceFileRef source, Sha256 hash) =
            await PackageFixtureAsync(temp);
        FileCorrectionPackageStore store = new(workspace);
        string final = Path.Combine(workspace.ResolveAbsoluteDirectory(folder), "reference.png");
        string external = temp.CreateSourceFile("external.png", File.ReadAllBytes(workspace.ResolveAbsolute(source)));
        CreateHardLink(final, external);
        FileAttributes before = File.GetAttributes(external);

        store.Exists(folder, "reference.png").ShouldBeFalse();
        var result = await store.PlaceVerifiedCopyAsync(folder, "reference.png", source, hash,
            "partial-12345678", markReadOnly: true, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        File.Exists(final).ShouldBeTrue();
        Hash(final).ShouldBe(hash);
        Hash(external).ShouldBe(hash);
        File.GetAttributes(external).ShouldBe(before);
    }

    [Fact]
    public async Task Retry_after_a_mismatched_copy_preserves_the_interrupted_staging_file()
    {
        using TempWorkspace temp = new();
        (FileWorkspace workspace, WorkspaceDirRef folder, WorkspaceFileRef source, Sha256 hash) =
            await PackageFixtureAsync(temp);
        FileCorrectionPackageStore store = new(workspace);
        string directory = workspace.ResolveAbsoluteDirectory(folder);
        string final = Path.Combine(directory, "working.png");

        var interrupted = await store.PlaceVerifiedCopyAsync(folder, "working.png", source,
            Sha256.Parse(new string('0', 64)), "partial-12345678", markReadOnly: false, CancellationToken.None);
        interrupted.IsFailure.ShouldBeTrue();
        File.Exists(final).ShouldBeFalse();
        string staged = Directory.GetFiles(directory).Single();
        byte[] stagedBytes = File.ReadAllBytes(staged);

        var resumed = await store.PlaceVerifiedCopyAsync(folder, "working.png", source, hash,
            "partial-12345678", markReadOnly: false, CancellationToken.None);
        resumed.IsSuccess.ShouldBeTrue(resumed.IsFailure ? resumed.Failure.ToString() : "");
        resumed.Value.ShouldBe(CorrectionFileOutcome.Created);
        File.ReadAllBytes(staged).ShouldBe(stagedBytes);
        Hash(final).ShouldBe(hash);

        var repeated = await store.PlaceVerifiedCopyAsync(folder, "working.png", source, hash,
            "partial-12345678", markReadOnly: false, CancellationToken.None);
        repeated.IsSuccess.ShouldBeTrue(repeated.IsFailure ? repeated.Failure.ToString() : "");
        repeated.Value.ShouldBe(CorrectionFileOutcome.AlreadyPresent);
        File.ReadAllBytes(staged).ShouldBe(stagedBytes);
    }

    private static async Task<(FileWorkspace Workspace, WorkspaceDirRef Folder, WorkspaceFileRef Source, Sha256 Hash)>
        PackageFixtureAsync(TempWorkspace temp)
    {
        FileWorkspace workspace = new(temp.Root);
        WorkspaceDirRef session = workspace.CreateSession(SessionId.From(Guid.NewGuid()), DateTimeOffset.UtcNow).Value;
        string original = temp.CreateSourceFile("source.png", SyntheticImages.Png());
        WorkspaceFileRef source = (await workspace.ImportSourceAsync(session, original, CancellationToken.None)).Value;
        WorkspaceDirRef folder = WorkspaceDirRef.Create(session.RelativePath + "/Correction/Test-12345678");
        Directory.CreateDirectory(workspace.ResolveAbsoluteDirectory(folder));
        return (workspace, folder, source, Hash(original));
    }

    private static Sha256 Hash(string path) =>
        Sha256.Parse(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));

    private static void CreateHardLink(string link, string target)
    {
        ProcessStartInfo start = new("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("mklink");
        start.ArgumentList.Add("/H");
        start.ArgumentList.Add(link);
        start.ArgumentList.Add(target);
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.ShouldBe(0, output);
    }
}
