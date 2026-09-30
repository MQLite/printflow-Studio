using System.Diagnostics;
using System.Text.Json;
using PrintFlow.Infrastructure.Startup;

namespace PrintFlow.WorkstationEntry;

/// <summary>Serializes compliant entry owners before any mutable resource is opened.</summary>
public sealed class OwnedInstanceClaim : IDisposable
{
    private readonly FileStream manifest;
    private readonly SingleInstanceGuard guard;
    private readonly NativePathLease rootLease;
    private readonly OwnedDirectoryLease stateProtection;
    private readonly object sync = new();
    public RunOwnership Ownership { get; private set; }
    private OwnedInstanceClaim(FileStream manifest, SingleInstanceGuard guard, NativePathLease rootLease, OwnedDirectoryLease stateProtection, RunOwnership ownership)
    { this.manifest = manifest; this.guard = guard; this.rootLease = rootLease; this.stateProtection = stateProtection; Ownership = ownership; }

    public static OwnedInstanceClaim Acquire(string root, RunOwnership expected, bool createNew)
    {
        root = PathRules.Canonical(root);
        NativePathLease rootLease = NativePathLease.Ancestors(root);
        if (!Directory.Exists(root) || rootLease.Identity != expected.RootIdentity) { rootLease.Dispose(); throw new IOException("Run root identity changed before ownership acquisition."); }
        using NativePathLease restrictiveRoot = NativePathLease.RestrictDirectory(root);
        string manifestPath = Path.Combine(root, "ownership.json");
        FileStream? manifest = null;
        SingleInstanceGuard? guard = null;
        OwnedDirectoryLease? stateProtection = null;
        try
        {
            string? previousIdentity = null;
            if (!createNew) { using NativePathLease check = NativePathLease.ReadFile(manifestPath); previousIdentity = check.Identity; }
            manifest = new(manifestPath, createNew ? FileMode.CreateNew : FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            string actual = NativePathLease.IdentifyExclusiveFile(manifest, manifestPath);
            if (previousIdentity is not null && actual != previousIdentity) throw new IOException("Ownership file was replaced before exclusive acquisition.");
            if (!createNew)
            {
                RunOwnership observed = JsonSerializer.Deserialize<RunOwnership>(manifest, ManifestReader.Json) ?? throw new IOException("Missing ownership record.");
                if (observed != expected) throw new IOException("Ownership changed during acquisition; retry validation.");
            }
            else { JsonSerializer.Serialize(manifest, expected, ManifestReader.Json); manifest.Flush(true); }
            // The exclusive, validated manifest now pins a child and keeps R nonempty.
            restrictiveRoot.Dispose();
            // The exclusive manifest prevents another compliant host from entering this gap.
            // This is not a sandbox against deliberate external mutation during acquisition.
            string state = Path.Combine(root, "state");
            if (createNew) Directory.CreateDirectory(state);
            using NativePathLease ancestry = NativePathLease.Ancestors(state);
            if (!Directory.Exists(state)) throw new IOException("Existing run state directory is missing.");
            Dictionary<string, string> directoryIdentities = JsonSerializer.Deserialize<Dictionary<string, string>>(expected.DirectoryIdentitiesJson) ?? throw new IOException("Missing directory identity ledger.");
            if (!createNew && !directoryIdentities.ContainsKey(state)) throw new IOException("Run state directory lacks recorded marker ownership.");
            stateProtection = OwnedDirectoryLease.Acquire(state, expected.RunToken, createNew ? null : directoryIdentities[state]);
            directoryIdentities[state] = stateProtection.Identity;
            string lockPath = Path.Combine(state, "instance.lock");
            if (createNew) { using FileStream created = new(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None); }
            string lockIdentity;
            using (NativePathLease check = NativePathLease.ReadFile(lockPath)) lockIdentity = check.Identity;
            if (!createNew && lockIdentity != expected.LockIdentity) throw new IOException("Instance lock differs from the recorded owned identity.");
            guard = new(lockPath);
            if (guard.TryAcquire() != SingleInstanceOutcome.Acquired) throw new IOException("Real run instance guard refused.");
            using (NativePathLease observed = NativePathLease.ObserveFile(lockPath))
                if (observed.Identity != lockIdentity) throw new IOException("Lock identity changed at guard acquisition.");
            using Process self = Process.GetCurrentProcess();
            RunOwnership renewed = expected with { OwnerPid = Environment.ProcessId, OwnerStartUtcTicks = self.StartTime.ToUniversalTime().Ticks, OwnerToken = Guid.NewGuid().ToString("N"), LockIdentity = lockIdentity, DirectoryIdentitiesJson = JsonSerializer.Serialize(directoryIdentities) };
            manifest.Position = 0; manifest.SetLength(0); JsonSerializer.Serialize(manifest, renewed, ManifestReader.Json); manifest.Flush(true);
            return new(manifest, guard, rootLease, stateProtection, renewed);
        }
        catch { guard?.Dispose(); stateProtection?.Dispose(); manifest?.Dispose(); rootLease.Dispose(); throw; }
    }
    public void RecordMutableIdentities(Dictionary<string, string> identities)
    {
        lock (sync) { Ownership = Ownership with { MutableIdentitiesJson = JsonSerializer.Serialize(identities) }; Save(); }
    }
    public void RecordDirectory(string path, string identity)
    {
        lock (sync)
        {
            Dictionary<string, string> identities = JsonSerializer.Deserialize<Dictionary<string, string>>(Ownership.DirectoryIdentitiesJson)!;
            identities[path] = identity;
            Ownership = Ownership with { DirectoryIdentitiesJson = JsonSerializer.Serialize(identities) }; Save();
        }
    }
    public bool OwnsStateDirectory(string path) => string.Equals(Path.GetDirectoryName(stateProtection.MarkerPath), path, StringComparison.OrdinalIgnoreCase);
    public void VerifyStateMarker() => stateProtection.Verify();
    private void Save() { manifest.Position = 0; manifest.SetLength(0); JsonSerializer.Serialize(manifest, Ownership, ManifestReader.Json); manifest.Flush(true); }
    public void Dispose() { guard.Dispose(); stateProtection.Dispose(); manifest.Dispose(); rootLease.Dispose(); }
}
