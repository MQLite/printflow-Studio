using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Win32.SafeHandles;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Infrastructure.Startup;
using PrintFlow.WorkstationEntry;

namespace PrintFlow.Tests.Unit.WorkstationEntry;

// Independently released prerequisite fixtures only: no provider, product schema or UI.
[Trait("WorkstationEntryPhase", "Prerequisite")]
public sealed class EntryOwnershipPrerequisiteTests
{
    [Fact]
    public void Exclusive_owner_and_real_guard_preserve_lock_identity_and_refuse_competitor()
    {
        using Fixture fixture = new();
        using OwnedInstanceClaim claim = OwnedInstanceClaim.Acquire(fixture.Root, fixture.Owner, true);
        string lockPath = Path.Combine(fixture.Root, "state", "instance.lock");
        string before = File.ReadAllText(Path.Combine(fixture.Root, "sentinel.txt"));
        Should.Throw<IOException>(() => OwnedInstanceClaim.Acquire(fixture.Root, claim.Ownership, false));
        using SingleInstanceGuard contender = new(lockPath);
        contender.TryAcquire().ShouldNotBe(SingleInstanceOutcome.Acquired);
        using NativePathLease observed = NativePathLease.ObserveFile(lockPath);
        observed.Identity.ShouldBe(claim.Ownership.LockIdentity);
        File.ReadAllText(Path.Combine(fixture.Root, "sentinel.txt")).ShouldBe(before);
    }

    [Fact]
    public void Replaced_or_hardlinked_lock_refuses_before_truncation()
    {
        using Fixture fixture = new();
        RunOwnership recorded;
        using (OwnedInstanceClaim claim = OwnedInstanceClaim.Acquire(fixture.Root, fixture.Owner, true)) recorded = claim.Ownership;
        string path = Path.Combine(fixture.Root, "state", "instance.lock");
        File.Move(path, path + ".original");
        string sentinel = fixture.OutsideSentinel;
        CreateHardLinkW(path, sentinel, 0).ShouldBeTrue();
        Should.Throw<IOException>(() => OwnedInstanceClaim.Acquire(fixture.Root, recorded, false));
        File.ReadAllText(sentinel).ShouldBe("outside-run sentinel");
        File.Delete(path); File.WriteAllText(path, "replacement remains unchanged");
        Should.Throw<IOException>(() => OwnedInstanceClaim.Acquire(fixture.Root, recorded, false));
        File.ReadAllText(path).ShouldBe("replacement remains unchanged");
    }

    [Fact]
    public void Real_sqlite_wal_write_checkpoint_close_reopen_work_with_held_identity()
    {
        using Fixture fixture = new();
        using OwnedInstanceClaim claim = OwnedInstanceClaim.Acquire(fixture.Root, fixture.Owner, true);
        using OwnedPaths paths = new(fixture.Root);
        string path = paths.At("state", "compatibility.db");
        paths.ProtectDatabase(path, true);
        claim.RecordMutableIdentities(paths.MutableIdentities());
        SqliteConnectionFactory factory = new(path);
        using (SqliteConnection connection = factory.Open())
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE fixture (value TEXT NOT NULL); INSERT INTO fixture VALUES ('owned'); PRAGMA wal_checkpoint(TRUNCATE);";
            command.ExecuteNonQuery();
            Should.Throw<IOException>(() => File.Move(path, path + ".moved"));
            Should.Throw<IOException>(() => File.Move(path + "-wal", path + "-wal.moved"));
            SqliteConnection.ClearPool(connection);
        }
        using (SqliteConnection reopened = factory.Open())
        {
            using SqliteCommand command = reopened.CreateCommand(); command.CommandText = "SELECT value FROM fixture";
            command.ExecuteScalar().ShouldBe("owned"); SqliteConnection.ClearPool(reopened);
        }
    }

    [Fact]
    public void Held_marker_prevents_reparse_and_replacement_while_permitting_child_operations()
    {
        using Fixture fixture = new();
        string target = Path.Combine(fixture.Root, "marker-target"); string directory = Path.Combine(fixture.Root, "marked");
        Directory.CreateDirectory(target); Directory.CreateDirectory(directory);
        string marker = Path.Combine(directory, ".entry-owned-directory");
        using SafeFileHandle restrictive = CreateFileW(directory, 0x81, 1, 0, 3, 0x02200000, 0);
        restrictive.IsInvalid.ShouldBeFalse();
        using FileStream markerHold = new(marker, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read);
        markerHold.WriteByte(42); markerHold.Flush(true);
        using NativePathLease compatible = NativePathLease.Ancestors(directory);
        restrictive.Dispose();
        using SafeFileHandle writer = CreateFileW(directory, 0x40000000, 7, 0, 3, 0x02200000, 0);
        writer.IsInvalid.ShouldBeFalse();
        byte[] buffer = JunctionBuffer(target);
        DeviceIoControl(writer, 0x000900A4, buffer, buffer.Length, 0, 0, out _, 0).ShouldBeFalse();
        Marshal.GetLastWin32Error().ShouldBe(145, "NTFS refuses a nonempty directory, not an invalid control buffer");
        Should.Throw<IOException>(() => File.Delete(marker));
        Should.Throw<IOException>(() => Directory.Move(directory, directory + "-replaced"));
        string child = Path.Combine(directory, "child.txt"); File.WriteAllText(child, "owned");
        File.Move(child, child + ".moved"); File.Delete(child + ".moved");
        markerHold.Length.ShouldBe(1);
    }

    [Fact]
    public void Owned_directory_rejects_setting_reparse_point_with_successful_empty_control()
    {
        using Fixture fixture = new();
        string target = Path.Combine(fixture.Root, "target"); string heldPath = Path.Combine(fixture.Root, "held");
        Directory.CreateDirectory(target);
        string control = Path.Combine(fixture.Root, "control"); Directory.CreateDirectory(control);
        byte[] buffer = JunctionBuffer(target);
        using (SafeFileHandle unheld = CreateFileW(control, 0x40000000, 7, 0, 3, 0x02200000, 0))
        {
            unheld.IsInvalid.ShouldBeFalse();
            DeviceIoControl(unheld, 0x000900A4, buffer, buffer.Length, 0, 0, out _, 0).ShouldBeTrue("the unheld control proves this is a valid junction mutation");
        }
        using OwnedInstanceClaim claim = OwnedInstanceClaim.Acquire(fixture.Root, fixture.Owner, true);
        using OwnedPaths paths = new(fixture.Root);
        paths.AttachOwner(claim); paths.EnsureDirectory(heldPath);
        using NativePathLease held = NativePathLease.Ancestors(heldPath);
        using SafeFileHandle writer = CreateFileW(heldPath, 0x40000000, 7, 0, 3, 0x02200000, 0);
        if (writer.IsInvalid) return; // OS refused the mutation-capable open.
        DeviceIoControl(writer, 0x000900A4, buffer, buffer.Length, 0, 0, out _, 0).ShouldBeFalse("integrated owned-directory protection must reject an in-place junction mutation");
        (File.GetAttributes(heldPath) & FileAttributes.ReparsePoint).ShouldBe((FileAttributes)0);
    }

    [Fact]
    public void Recorded_marker_is_verified_on_reopen_and_replacement_refuses()
    {
        using Fixture fixture = new(); RunOwnership owner;
        string directory = Path.Combine(fixture.Root, "workspace");
        using (OwnedInstanceClaim claim = OwnedInstanceClaim.Acquire(fixture.Root, fixture.Owner, true))
        using (OwnedPaths paths = new(fixture.Root))
        {
            paths.AttachOwner(claim); paths.EnsureDirectory(directory);
            string marker = Path.Combine(directory, OwnedDirectoryLease.MarkerName);
            paths.IsOwnedMarker(marker).ShouldBeTrue();
            Should.Throw<IOException>(() => File.WriteAllText(marker, "changed"));
            owner = claim.Ownership;
        }
        using (OwnedInstanceClaim claim = OwnedInstanceClaim.Acquire(fixture.Root, owner, false))
        using (OwnedPaths paths = new(fixture.Root)) { paths.AttachOwner(claim); paths.IsOwnedMarker(Path.Combine(directory, OwnedDirectoryLease.MarkerName)).ShouldBeTrue(); owner = claim.Ownership; }
        string markerPath = Path.Combine(directory, OwnedDirectoryLease.MarkerName);
        File.Move(markerPath, markerPath + ".original"); File.WriteAllText(markerPath, owner.RunToken);
        using OwnedInstanceClaim last = OwnedInstanceClaim.Acquire(fixture.Root, owner, false);
        using OwnedPaths rejected = new(fixture.Root);
        Should.Throw<IOException>(() => rejected.AttachOwner(last));
    }

    [Fact]
    public void Unexpected_existing_directory_is_not_adopted_or_marked()
    {
        using Fixture fixture = new(); using OwnedInstanceClaim claim = OwnedInstanceClaim.Acquire(fixture.Root, fixture.Owner, true);
        using OwnedPaths paths = new(fixture.Root); paths.AttachOwner(claim);
        string unexpected = Path.Combine(fixture.Root, "unexpected"); Directory.CreateDirectory(unexpected);
        Should.Throw<IOException>(() => paths.EnsureDirectory(unexpected));
        Directory.EnumerateFileSystemEntries(unexpected).ShouldBeEmpty();
    }

    private static byte[] JunctionBuffer(string target)
    {
        byte[] substitute = Encoding.Unicode.GetBytes(@"\??\" + target);
        byte[] print = Encoding.Unicode.GetBytes(target);
        byte[] buffer = new byte[16 + substitute.Length + 2 + print.Length + 2];
        BitConverter.GetBytes(0xA0000003u).CopyTo(buffer, 0);
        BitConverter.GetBytes((ushort)(buffer.Length - 8)).CopyTo(buffer, 4);
        BitConverter.GetBytes((ushort)substitute.Length).CopyTo(buffer, 10);
        BitConverter.GetBytes((ushort)(substitute.Length + 2)).CopyTo(buffer, 12);
        BitConverter.GetBytes((ushort)print.Length).CopyTo(buffer, 14);
        substitute.CopyTo(buffer, 16); print.CopyTo(buffer, 18 + substitute.Length);
        return buffer;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly NativePathLease chain;
        public string Root { get; }
        public RunOwnership Owner { get; }
        public string OutsideSentinel { get; }
        public Fixture()
        {
            string id = "prerequisite-" + Guid.NewGuid().ToString("N")[..12];
            Root = Path.Combine(EntryTestPaths.Repository, "artifacts", "pf-opux-scrum11154-workstation-entry", "runs", id);
            EntryPlan.Validate(Root, new(EntryTestPaths.Repository, "25a93f551f27f9e918fdcbbec701a1371ccc23d0", [@"D:\PrintFlowStudio"], []), new(id, "Fake", ["F1", "F2", "F3", "F4", "F5", "F6"]));
            using NativePathLease before = NativePathLease.Ancestors(Root);
            Directory.CreateDirectory(Root); chain = NativePathLease.Ancestors(Root);
            using Process self = Process.GetCurrentProcess();
            Owner = new(id, chain.Identity, "fixture-candidate", "fixture-scenarios", Environment.ProcessId, self.StartTime.ToUniversalTime().Ticks, Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"));
            string sentinelDirectory = Path.Combine(EntryTestPaths.Repository, "artifacts", "pf-opux-scrum11154-workstation-entry", "prerequisite-sentinels");
            using NativePathLease sentinelParent = NativePathLease.Ancestors(sentinelDirectory);
            Directory.CreateDirectory(sentinelDirectory);
            OutsideSentinel = Path.Combine(sentinelDirectory, id + ".txt");
            using (FileStream created = new(OutsideSentinel, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (StreamWriter writer = new(created)) writer.Write("outside-run sentinel");
            File.WriteAllText(Path.Combine(Root, "sentinel.txt"), "outside-state sentinel");
            File.WriteAllText(Path.Combine(Root, "fixture-build.json"), JsonSerializer.Serialize(new { Assembly = typeof(EntryOwnershipPrerequisiteTests).Assembly.ManifestModule.ModuleVersionId, Host = typeof(OwnedRun).Assembly.ManifestModule.ModuleVersionId, SourceHead = "25a93f551f27f9e918fdcbbec701a1371ccc23d0", Scope = "PREREQUISITE_ONLY" }));
        }
        public void Dispose() => chain.Dispose();
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLinkW(string path, string target, nint security);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security, uint mode, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeviceIoControl(SafeFileHandle file, uint control, byte[] input, int size, nint output, int outputSize, out int returned, nint overlapped);
}
