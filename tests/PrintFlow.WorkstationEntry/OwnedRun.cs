using System.Diagnostics;
using System.Text.Json;
using PrintFlow.Infrastructure.Startup;

namespace PrintFlow.WorkstationEntry;

public sealed record RunOwnership(string RunId, string RootIdentity, string CandidateHash, string ScenarioHash,
    int OwnerPid, long OwnerStartUtcTicks, string RunToken, string OwnerToken, string LockIdentity = "", string MutableIdentitiesJson = "{}", string DirectoryIdentitiesJson = "{}");
/// <param name="ScenarioLedgerSha256">
/// SHA-256 of the exact scenario-ledger bytes this preparation wrote: the immutable admission
/// manifest Interactive reads. Added 2026-10-01 (SCRUM-11154 F-V7); a record without it is an
/// older preparation that cannot authorize Interactive and is never upgraded in place.
/// </param>
public sealed record PreparedRun(string CandidateHash, string ScenarioHash, string RunToken, string OwnerToken, DateTimeOffset PreparedUtc, string Evidence,
    string? ScenarioLedgerSha256);

public sealed class OwnedRun : IDisposable
{
    private readonly NativePathLease rootLease;
    private readonly OwnedInstanceClaim guard;
    public OwnedPaths Paths { get; }
    public RunOwnership Ownership { get; }
    private OwnedRun(OwnedPaths paths, RunOwnership ownership, NativePathLease root, OwnedInstanceClaim instance)
    { Paths = paths; Ownership = ownership; rootLease = root; guard = instance; }

    public static OwnedRun Claim(ValidatedInput input, bool resume)
    {
        EntryPlan plan = input.Plan;
        // Repeat the full root/ancestor validation at the write boundary; caller validation grants
        // no lasting filesystem authority. No configuration is loaded from normal App startup.
        EntryPlan.Validate(plan.Root, plan.Candidate, plan.Scenario, resume);
        using NativePathLease ancestors = NativePathLease.Ancestors(plan.Root);
        using NativePathLease parentCreation = NativePathLease.RestrictDirectory(Path.GetDirectoryName(plan.Root)!);
        Directory.CreateDirectory(plan.Root);
        NativePathLease root = NativePathLease.Ancestors(plan.Root);
        OwnedPaths paths = new(plan.Root);
        OwnedInstanceClaim? guard = null;
        try
        {
            RunOwnership ownership;
            if (resume) ownership = VerifyResume(plan.Root, input.CandidateHash, input.ScenarioHash, false);
            else
            {
                using Process self = Process.GetCurrentProcess();
                ownership = new(plan.Scenario.RunId, root.Identity, input.CandidateHash, input.ScenarioHash,
                    Environment.ProcessId, self.StartTime.ToUniversalTime().Ticks, Guid.NewGuid().ToString("N"), Guid.NewGuid().ToString("N"));
            }
            guard = OwnedInstanceClaim.Acquire(plan.Root, ownership, createNew: !resume);
            parentCreation.Dispose();
            ownership = guard.Ownership;
            paths.AttachOwner(guard);
            paths.ExpectMutable(JsonSerializer.Deserialize<Dictionary<string, string>>(ownership.MutableIdentitiesJson) ?? throw new IOException("Missing mutable identity ledger."));
            foreach (string area in new[] { "state", "workspace", "fixtures", "preset", "delivery", "recycle", "evidence", "diagnostics" })
            { paths.EnsureDirectory(paths.At(area)); }
            foreach (string nested in new[] { Path.Combine("fixtures", "inputs"), Path.Combine("fixtures", "returns"), Path.Combine("diagnostics", "staging"), Path.Combine("diagnostics", "export") })
            { paths.EnsureDirectory(paths.At(nested)); }
            paths.EnsureDirectory(paths.At("workspace", "Quarantine"));
            paths.ProtectDatabase(paths.At("state", "app.db"), createNew: !resume);
            paths.ProtectDatabase(paths.At("state", "automation-lease.db"), createNew: !resume);
            guard.RecordMutableIdentities(paths.MutableIdentities());
            ownership = guard.Ownership;
            return new(paths, ownership, root, guard);
        }
        catch { guard?.Dispose(); paths.Dispose(); root.Dispose(); throw; }
    }

    public static RunOwnership VerifyResume(string root, string candidateHash, string scenarioHash, bool requirePrepared)
    {
        root = PathRules.Canonical(root);
        using NativePathLease held = NativePathLease.Ancestors(root);
        RunOwnership owner = ManifestReader.ReadFile<RunOwnership>(Path.Combine(root, "ownership.json")).Value;
        if (owner.RootIdentity != held.Identity || owner.CandidateHash != candidateHash || owner.ScenarioHash != scenarioHash || owner.RunId != Path.GetFileName(root))
            throw new ArgumentException("Resume ownership/candidate/scenario identity mismatch.");
        string[] allowed = ["ownership.json", "state", "workspace", "fixtures", "preset", "delivery", "recycle", "evidence", "diagnostics"];
        if (Directory.EnumerateFileSystemEntries(root).Any(path => !allowed.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)))
            throw new ArgumentException("Unexpected root content refuses resume.");
        try
        {
            using Process previous = Process.GetProcessById(owner.OwnerPid);
            if (previous.StartTime.ToUniversalTime().Ticks == owner.OwnerStartUtcTicks && !previous.HasExited)
                throw new IOException("Previous run owner is still alive.");
        }
        catch (ArgumentException) { /* Exact recorded PID no longer exists. */ }
        catch (System.ComponentModel.Win32Exception ex) { throw new IOException("Previous owner liveness is unknown.", ex); }
        if (requirePrepared) VerifyPrepared(root, owner, candidateHash, scenarioHash);
        return owner;
    }

    /// <summary>The completed preparation of <paramref name="owner"/>, including its bound ledger digest.</summary>
    public static PreparedRun VerifyPrepared(string root, RunOwnership owner, string candidateHash, string scenarioHash)
    {
        PreparedRun prepared = ManifestReader.ReadFile<PreparedRun>(Path.Combine(PathRules.Canonical(root), "state", "prepared.json")).Value;
        if (prepared.CandidateHash != candidateHash || prepared.ScenarioHash != scenarioHash || prepared.RunToken != owner.RunToken || prepared.OwnerToken != owner.OwnerToken || prepared.Evidence != "NONINTERACTIVE_ONLY")
            throw new ArgumentException("Interactive requires a completed matching prepared run.");
        if (prepared.ScenarioLedgerSha256 is not { Length: 64 } digest || !digest.All(Uri.IsHexDigit))
            throw new ArgumentException("Interactive requires a fresh preparation that bound its scenario ledger digest; older prepared roots cannot be upgraded.");
        return prepared;
    }

    public void Dispose() { guard.Dispose(); Paths.Dispose(); rootLease.Dispose(); }
}
