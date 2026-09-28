using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using PrintFlow.Domain.Files;
using PrintFlow.Infrastructure.Delivery;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Delivery;
using Xunit.Abstractions;

namespace PrintFlow.Tests.Integration.Files;

public sealed class WindowsDeliveryFileSystemTests(ITestOutputHelper output)
{
    [Fact]
    public void Native_NTFS_absent_optional_evidence_descendant_does_not_block_external_delivery()
    {
        using TempWorkspace workspace = new();
        string managed = Path.Combine(workspace.Root, "managed");
        string database = Path.Combine(workspace.Root, "database");
        string external = Path.Combine(workspace.Root, "external");
        Directory.CreateDirectory(managed);
        Directory.CreateDirectory(database);
        Directory.CreateDirectory(external);
        string evidence = Path.Combine(managed, "Evidence");
        Directory.Exists(evidence).ShouldBeFalse();
        string source = Path.Combine(managed, "reviewed.png");
        byte[] bytes = [1, 2, 3, 4];
        File.WriteAllBytes(source, bytes);
        Sha256 hash = Sha256.FromBytes(SHA256.HashData(bytes));
        var protectedRoots = new[] { managed, database, evidence };
        WindowsDeliveryFileSystem fs = new();
        var opened = fs.Open(source, hash, bytes.Length, external, "copy.png",
            ArtifactKind.ApprovedAssetPng, protectedRoots, null);
        opened.IsSuccess.ShouldBeTrue(opened.Detail);
        opened.Value!.Dispose();
        Directory.Exists(evidence).ShouldBeFalse();
        fs.Open(source, hash, bytes.Length, managed, "copy.png",
            ArtifactKind.ApprovedAssetPng, protectedRoots, null).Code.ShouldBe(DeliveryCode.ProtectedDestination);
    }

    [Fact]
    public void Native_NTFS_junction_alias_to_protected_root_is_refused_without_following()
    {
        using TempWorkspace workspace = new();
        string managed = Path.Combine(workspace.Root, "managed");
        Directory.CreateDirectory(managed);
        string source = Path.Combine(managed, "approved.png");
        byte[] bytes = [7, 1, 1];
        File.WriteAllBytes(source, bytes);
        string alias = Path.Combine(workspace.Root, "alias");
        ProcessStartInfo start = new("cmd.exe") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink");
        start.ArgumentList.Add("/J"); start.ArgumentList.Add(alias); start.ArgumentList.Add(managed);
        using (Process process = Process.Start(start)!)
        {
            string result = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit(); process.ExitCode.ShouldBe(0, result);
        }
        try
        {
            File.GetAttributes(alias).HasFlag(FileAttributes.ReparsePoint).ShouldBeTrue();
            var opened = new WindowsDeliveryFileSystem().Open(source,
                Sha256.FromBytes(SHA256.HashData(bytes)), bytes.Length,
                alias, "copy.png", ArtifactKind.ApprovedAssetPng, [managed], null);
            opened.IsSuccess.ShouldBeFalse();
            opened.Code.ShouldBe(DeliveryCode.UnsupportedDestination);
            File.Exists(Path.Combine(managed, "copy.png")).ShouldBeFalse();
            File.ReadAllBytes(source).ShouldBe(bytes);
        }
        finally
        {
            File.GetAttributes(alias).HasFlag(FileAttributes.ReparsePoint).ShouldBeTrue();
            Directory.Delete(alias);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Native_NTFS_cancelled_or_bad_hash_stage_removes_only_owned_partial(bool cancel)
    {
        using TempWorkspace workspace = new();
        string managed = Path.Combine(workspace.Root, "managed");
        string destination = Path.Combine(workspace.Root, "delivery");
        Directory.CreateDirectory(managed); Directory.CreateDirectory(destination);
        byte[] bytes = [2, 4, 6, 8];
        string source = Path.Combine(managed, "approved.png");
        File.WriteAllBytes(source, bytes);
        Sha256 hash = Sha256.FromBytes(SHA256.HashData(bytes));
        var opened = new WindowsDeliveryFileSystem().Open(source, hash, bytes.Length,
            destination, "copy.png", ArtifactKind.ApprovedAssetPng, [managed], null);
        opened.IsSuccess.ShouldBeTrue(opened.Detail);
        using IDeliveryFileGuard guard = opened.Value!;
        string leaf = ".printflow-" + Guid.NewGuid().ToString("N") + ".partial";
        DeliveryFileIdentity stage = guard.CreateStaging(leaf).Value!;
        using CancellationTokenSource stop = new();
        if (cancel) stop.Cancel();
        var copied = await guard.CopyAndVerifyStageAsync(cancel ? hash : Sha256.Parse(new string('F', 64)),
            bytes.Length, null, stop.Token);
        copied.Code.ShouldBe(cancel ? DeliveryCode.Cancelled : DeliveryCode.VerificationFailed);
        guard.DeleteHeldStaging(stage).IsSuccess.ShouldBeTrue();
        File.Exists(Path.Combine(destination, leaf)).ShouldBeFalse();
        File.Exists(Path.Combine(destination, "copy.png")).ShouldBeFalse();
        File.ReadAllBytes(source).ShouldBe(bytes);
    }

    [Fact]
    public void Native_NTFS_protected_root_and_original_refuse_but_sibling_prefix_is_allowed()
    {
        using TempWorkspace workspace = new();
        string managed = Path.Combine(workspace.Root, "managed");
        string inside = Path.Combine(managed, "inside");
        string sibling = Path.Combine(workspace.Root, "managed-sibling");
        Directory.CreateDirectory(inside); Directory.CreateDirectory(sibling);
        byte[] bytes = [1, 4, 9];
        string source = Path.Combine(managed, "approved.png");
        File.WriteAllBytes(source, bytes);
        Sha256 hash = Sha256.FromBytes(SHA256.HashData(bytes));
        WindowsDeliveryFileSystem fs = new();
        fs.Open(source, hash, bytes.Length, inside, "copy.png", ArtifactKind.ApprovedAssetPng,
            [managed], null).Code.ShouldBe(DeliveryCode.ProtectedDestination);
        string protectedOriginal = Path.Combine(sibling, "original.png");
        fs.Open(source, hash, bytes.Length, sibling, "original.png", ArtifactKind.ApprovedAssetPng,
            [managed], protectedOriginal).Code.ShouldBe(DeliveryCode.ProtectedDestination);
        var permitted = fs.Open(source, hash, bytes.Length, sibling, "copy.png",
            ArtifactKind.ApprovedAssetPng, [managed], null);
        permitted.IsSuccess.ShouldBeTrue(permitted.Detail);
        permitted.Value!.Dispose();
        fs.Open(source, hash, bytes.Length, "managed-sibling", "copy.png",
            ArtifactKind.ApprovedAssetPng, [managed], null).Code.ShouldBe(DeliveryCode.UnsupportedDestination);
    }

    [Fact]
    public async Task Native_NTFS_stage_rename_and_final_handle_prove_same_object_and_hash()
    {
        using TempWorkspace workspace = new();
        string managed = Path.Combine(workspace.Root, "managed");
        string destination = Path.Combine(workspace.Root, "delivery");
        Directory.CreateDirectory(managed);
        Directory.CreateDirectory(destination);
        byte[] bytes = [1, 2, 3, 4, 5, 6, 7];
        string source = Path.Combine(managed, "approved.png");
        File.WriteAllBytes(source, bytes);
        Sha256 hash = Sha256.FromBytes(SHA256.HashData(bytes));
        WindowsDeliveryFileSystem fs = new();
        var opened = fs.Open(source, hash, bytes.Length, destination, "copy.png",
            ArtifactKind.ApprovedAssetPng, [managed], null);
        opened.IsSuccess.ShouldBeTrue(opened.Detail);
        using IDeliveryFileGuard guard = opened.Value!;
        output.WriteLine($"Synthetic root={workspace.Root}; filesystem={new DriveInfo(Path.GetPathRoot(workspace.Root)!).DriveFormat}; volume={guard.VolumeId}; directory={guard.DirectoryId}; pointerSize={IntPtr.Size}");
        string stage = ".printflow-" + Guid.NewGuid().ToString("N") + ".partial";
        var created = guard.CreateStaging(stage);
        created.IsSuccess.ShouldBeTrue(created.Detail);
        (await guard.CopyAndVerifyStageAsync(hash, bytes.Length, null, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        var publication = guard.PublishNoReplace(stage, created.Value!);
        publication.IsSuccess.ShouldBeTrue(publication.Detail);
        DeliveryFinalCheck final = guard.CheckFinal(created.Value);
        final.Presence.ShouldBe(DeliveryFilePresence.Present,
            $"Final path {guard.FinalPath}; files: {string.Join(',', Directory.EnumerateFiles(destination))}; detail: {final.Detail}");
        final.Identity.ShouldBe(created.Value);
        final.Length.ShouldBe(bytes.Length);
        final.Hash.ShouldBe(hash);
        ReadWhileStageHeld(guard.FinalPath).ShouldBe(bytes);
        File.Exists(Path.Combine(destination, stage)).ShouldBeFalse();
        guard.PublishNoReplace(stage, created.Value!).Code.ShouldBe(DeliveryCode.NeedsReconciliation);
        guard.DeleteHeldStaging(created.Value!).Code.ShouldBe(DeliveryCode.NeedsReconciliation);
        File.Exists(guard.FinalPath).ShouldBeTrue();
        output.WriteLine($"Published final={guard.FinalPath}; identity={final.Identity}; sha256={final.Hash}; length={final.Length}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_NTFS_equal_hash_foreign_final_is_collision_and_preserved(bool takeNameAfterPrecheck)
    {
        using TempWorkspace workspace = new();
        string managed = Path.Combine(workspace.Root, "managed");
        string destination = Path.Combine(workspace.Root, "delivery");
        Directory.CreateDirectory(managed); Directory.CreateDirectory(destination);
        byte[] bytes = [9, 8, 7, 6];
        string source = Path.Combine(managed, "approved.png");
        File.WriteAllBytes(source, bytes);
        if (!takeNameAfterPrecheck) File.WriteAllBytes(Path.Combine(destination, "copy.png"), bytes);
        Sha256 hash = Sha256.FromBytes(SHA256.HashData(bytes));
        WindowsDeliveryFileSystem fs = new();
        var opened = fs.Open(source, hash, bytes.Length, destination, "copy.png",
            ArtifactKind.ApprovedAssetPng, [managed], null);
        opened.IsSuccess.ShouldBeTrue(opened.Detail);
        using IDeliveryFileGuard guard = opened.Value!;
        string stage = ".printflow-" + Guid.NewGuid().ToString("N") + ".partial";
        var created = guard.CreateStaging(stage);
        created.IsSuccess.ShouldBeTrue(created.Detail);
        (await guard.CopyAndVerifyStageAsync(hash, bytes.Length, null, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        if (takeNameAfterPrecheck)
        {
            guard.CheckFinal().Presence.ShouldBe(DeliveryFilePresence.Absent);
            // A foreign writer takes the previously absent final name at publication's boundary.
            File.WriteAllBytes(Path.Combine(destination, "copy.png"), bytes);
        }
        var publication = guard.PublishNoReplace(stage, created.Value!);
        publication.IsSuccess.ShouldBeFalse();
        publication.Code.ShouldBe(DeliveryCode.Collision, publication.Detail);
        guard.CheckFinal(created.Value).Identity.ShouldNotBe(created.Value);
        File.ReadAllBytes(Path.Combine(destination, "copy.png")).ShouldBe(bytes);
        guard.DeleteHeldStaging(created.Value!).IsSuccess.ShouldBeTrue();
        File.Exists(Path.Combine(destination, stage)).ShouldBeFalse();
        File.ReadAllBytes(Path.Combine(destination, "copy.png")).ShouldBe(bytes);
        output.WriteLine($"Synthetic root={workspace.Root}; volume={guard.VolumeId}; foreignAfterPrecheck={takeNameAfterPrecheck}; collision preserved foreign bytes and removed only owned stage.");
    }

    [Fact]
    public async Task Native_NTFS_competing_publishers_have_one_winner_and_preserve_the_losing_stage()
    {
        using TempWorkspace workspace = new();
        string managed = Path.Combine(workspace.Root, "managed");
        string destination = Path.Combine(workspace.Root, "delivery");
        Directory.CreateDirectory(managed); Directory.CreateDirectory(destination);
        byte[] bytes = [3, 1, 4, 1, 5];
        string source = Path.Combine(managed, "approved.png");
        File.WriteAllBytes(source, bytes);
        Sha256 hash = Sha256.FromBytes(SHA256.HashData(bytes));
        WindowsDeliveryFileSystem fs = new();
        var openedA = fs.Open(source, hash, bytes.Length, destination, "copy.png", ArtifactKind.ApprovedAssetPng, [managed], null);
        var openedB = fs.Open(source, hash, bytes.Length, destination, "copy.png", ArtifactKind.ApprovedAssetPng, [managed], null);
        openedA.IsSuccess.ShouldBeTrue(openedA.Detail); openedB.IsSuccess.ShouldBeTrue(openedB.Detail);
        using IDeliveryFileGuard a = openedA.Value!;
        using IDeliveryFileGuard b = openedB.Value!;
        string stageA = ".printflow-" + Guid.NewGuid().ToString("N") + ".partial";
        string stageB = ".printflow-" + Guid.NewGuid().ToString("N") + ".partial";
        var createdA = a.CreateStaging(stageA); var createdB = b.CreateStaging(stageB);
        createdA.IsSuccess.ShouldBeTrue(createdA.Detail); createdB.IsSuccess.ShouldBeTrue(createdB.Detail);
        (await a.CopyAndVerifyStageAsync(hash, bytes.Length, null, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await b.CopyAndVerifyStageAsync(hash, bytes.Length, null, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        a.CheckFinal().Presence.ShouldBe(DeliveryFilePresence.Absent);
        b.CheckFinal().Presence.ShouldBe(DeliveryFilePresence.Absent);
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<DeliveryFileResult<bool>> first = Task.Run(async () => { await start.Task; return a.PublishNoReplace(stageA, createdA.Value!); });
        Task<DeliveryFileResult<bool>> second = Task.Run(async () => { await start.Task; return b.PublishNoReplace(stageB, createdB.Value!); });
        start.SetResult();
        DeliveryFileResult<bool>[] outcomes = await Task.WhenAll(first, second);
        outcomes.Count(x => x.IsSuccess).ShouldBe(1, string.Join("; ", outcomes.Select(x => x.Detail)));
        outcomes.Count(x => x.Code == DeliveryCode.Collision).ShouldBe(1);
        bool firstWon = outcomes[0].IsSuccess;
        var winningIdentity = firstWon ? createdA.Value! : createdB.Value!;
        DeliveryFinalCheck final = (firstWon ? a : b).CheckFinal(winningIdentity);
        final.Identity.ShouldBe(winningIdentity); final.Hash.ShouldBe(hash); final.Length.ShouldBe(bytes.Length);
        File.Exists(Path.Combine(destination, firstWon ? stageA : stageB)).ShouldBeFalse();
        ReadWhileStageHeld(Path.Combine(destination, firstWon ? stageB : stageA)).ShouldBe(bytes);
        File.ReadAllBytes(source).ShouldBe(bytes);
        output.WriteLine($"Synthetic root={workspace.Root}; volume={a.VolumeId}; firstWinner={firstWon}; finalIdentity={final.Identity}; outcomes={string.Join(',', outcomes.Select(x => x.Code))}");
    }

    [Fact]
    public async Task Native_NTFS_guards_block_source_stage_and_directory_mutation_until_selection_disposal()
    {
        using TempWorkspace workspace = new();
        string managed = Path.Combine(workspace.Root, "managed");
        string destination = Path.Combine(workspace.Root, "delivery");
        Directory.CreateDirectory(managed); Directory.CreateDirectory(destination);
        byte[] bytes = [2, 7, 1, 8];
        string source = Path.Combine(managed, "approved.png");
        File.WriteAllBytes(source, bytes);
        Sha256 hash = Sha256.FromBytes(SHA256.HashData(bytes));
        var opened = new WindowsDeliveryFileSystem().Open(source, hash, bytes.Length, destination, "copy.png",
            ArtifactKind.ApprovedAssetPng, [managed], null);
        opened.IsSuccess.ShouldBeTrue(opened.Detail);
        using IDeliveryFileGuard guard = opened.Value!;
        string stage = ".printflow-" + Guid.NewGuid().ToString("N") + ".partial";
        var created = guard.CreateStaging(stage);
        created.IsSuccess.ShouldBeTrue(created.Detail);
        Should.Throw<IOException>(() => File.WriteAllBytes(source, [0]));
        Should.Throw<IOException>(() => File.Delete(source));
        Should.Throw<IOException>(() => File.WriteAllBytes(Path.Combine(destination, stage), [0]));
        Should.Throw<IOException>(() => Directory.Move(destination, Path.Combine(workspace.Root, "moved")));
        Should.Throw<IOException>(() => Directory.Move(workspace.Root, workspace.Root + "-moved"));
        (await guard.CopyAndVerifyStageAsync(hash, bytes.Length, null, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        var publication = guard.PublishNoReplace(stage, created.Value!);
        publication.IsSuccess.ShouldBeTrue(publication.Detail);
        var selection = guard.AcquireSelection(Guid.NewGuid(), created.Value!, hash, bytes.Length);
        selection.Availability.ShouldBe(DeliveryAvailability.VerifiedNow);
        using var lease = selection.Lease!;
        guard.Dispose(); // Ownership has transferred; this cannot release the verified-selection guards.
        Should.Throw<IOException>(() => File.WriteAllBytes(lease.FinalPath, [0]));
        Should.Throw<IOException>(() => File.Delete(lease.FinalPath));
        Should.Throw<IOException>(() => Directory.Move(destination, Path.Combine(workspace.Root, "moved")));
        File.ReadAllBytes(source).ShouldBe(bytes);
        lease.Dispose();
        // Only synthetic owned files are modified after guards have been released.
        File.WriteAllBytes(Path.Combine(destination, "copy.png"), [0]);
        File.ReadAllBytes(Path.Combine(destination, "copy.png")).ShouldBe(new byte[] { 0 });
        output.WriteLine($"Synthetic root={workspace.Root}; volume={guard.VolumeId}; held source/stage/ancestor/final mutation refused; selection disposal released guards.");
    }

    private static byte[] ReadWhileStageHeld(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using MemoryStream bytes = new();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }
}
