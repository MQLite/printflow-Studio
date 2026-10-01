using System.IO;

namespace PrintFlow.WorkstationEntry;

public sealed class OwnedPaths : IDisposable
{
    private readonly Dictionary<string, string> identities = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NativePathLease> directories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NativePathLease> fixtures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, NativePathLease> mutable = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> expectedMutable = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OwnedDirectoryLease> protectedDirectories = new(StringComparer.OrdinalIgnoreCase);
    private OwnedInstanceClaim? owner;
    private readonly object sync = new();
    public OwnedPaths(string root) => Root = PathRules.Canonical(root);
    public string Root { get; }
    public SemaphoreSlim DiagnosticMutation { get; } = new(1, 1);
    public void AttachOwner(OwnedInstanceClaim claim)
    {
        lock (sync)
        {
            if (owner is not null) throw new IOException("Paths already attached to an owner.");
            owner = claim;
            Dictionary<string, string> recorded = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(claim.Ownership.DirectoryIdentitiesJson)!;
            foreach (var pair in recorded.OrderBy(pair => pair.Key.Length))
            {
                if (!PathRules.Within(pair.Key, Root)) throw new IOException("Recorded directory escapes run.");
                if (claim.OwnsStateDirectory(pair.Key)) { claim.VerifyStateMarker(); continue; }
                protectedDirectories.Add(pair.Key, OwnedDirectoryLease.Acquire(pair.Key, claim.Ownership.RunToken, pair.Value));
            }
        }
    }
    public void EnsureDirectory(string path)
    {
        string full = PathRules.Canonical(path);
        if (!PathRules.Within(full, Root)) throw new IOException("Directory creation escapes claimed root.");
        lock (sync)
        {
            if (owner is null) throw new IOException("Directory mutation requires an active owned claim.");
            string current = Root;
            foreach (string component in full[(Root.Length + 1)..].Split(Path.DirectorySeparatorChar))
            {
                current = Path.Combine(current, component);
                if (owner.OwnsStateDirectory(current)) { owner.VerifyStateMarker(); continue; }
                if (protectedDirectories.TryGetValue(current, out OwnedDirectoryLease? existing)) { existing.Verify(); continue; }
                if (Directory.Exists(current) || File.Exists(current)) throw new IOException("Unexpected unrecorded directory cannot be adopted.");
                // Parent is already nonempty and pinned. Verify/open the new leaf restrictively
                // before creating any marker or invoking a product filesystem operation.
                Directory.CreateDirectory(current);
                OwnedDirectoryLease held = OwnedDirectoryLease.Acquire(current, owner.Ownership.RunToken, null);
                protectedDirectories.Add(current, held);
                owner.RecordDirectory(current, held.Identity);
            }
        }
    }
    public bool IsOwnedMarker(string path)
    {
        lock (sync)
        {
            string directory = Path.GetDirectoryName(path)!;
            if (Path.GetFileName(path) != OwnedDirectoryLease.MarkerName) return false;
            if (owner?.OwnsStateDirectory(directory) == true) { owner.VerifyStateMarker(); return true; }
            if (!protectedDirectories.TryGetValue(directory, out OwnedDirectoryLease? held)) return false;
            held.Verify(); return true;
        }
    }
    public void ExpectMutable(IReadOnlyDictionary<string, string> expected)
    { lock (sync) foreach (var pair in expected) expectedMutable.Add(pair.Key, pair.Value); }
    public Dictionary<string, string> MutableIdentities()
    { lock (sync) return mutable.ToDictionary(pair => pair.Key, pair => pair.Value.Identity, StringComparer.OrdinalIgnoreCase); }
    public string At(params string[] parts) => Path.Combine([Root, .. parts]);

    public string Require(string path, string role)
    {
        string full;
        try { full = PathRules.Canonical(path); }
        catch (ArgumentException ex) { throw new IOException("Noncanonical owned path refused.", ex); }
        string[] allowed = role switch
        {
            "read" => ["workspace", "fixtures", "evidence", "preset"],
            "workspace" => ["workspace"], "fixtures" => ["fixtures"], "evidence" => ["evidence"],
            "delivery" => ["delivery"], "export" => [Path.Combine("diagnostics", "export")],
            "staging" => [Path.Combine("diagnostics", "staging")], "state" => ["state"],
            "recycle" => ["recycle"], "preset" => ["preset"],
            _ => throw new IOException("Unknown resource role.")
        };
        if (!allowed.Any(area => string.Equals(full, At(area), StringComparison.OrdinalIgnoreCase) || PathRules.Within(full, At(area))))
            throw new IOException("Path is outside its owned resource role.");
        PinAncestors(full);
        return full;
    }

    /// <summary>
    /// Why an operator selection is not usable for <paramref name="role"/> (noncanonical, outside the
    /// role, or an unadmitted fixture), or null when it may proceed to the full identity checks.
    /// Integrity faults are deliberately not classified here; they still refuse the run.
    /// </summary>
    public string? SelectionRefusal(string path, string role)
    {
        string full;
        try { full = PathRules.Canonical(path); }
        catch (ArgumentException ex) { return ex.Message; }
        string area = role switch { "fixtures" => At("fixtures"), "delivery" => At("delivery"), "export" => At("diagnostics", "export"),
            _ => throw new IOException("Unknown selection role.") };
        if (!string.Equals(full, area, StringComparison.OrdinalIgnoreCase) && !PathRules.Within(full, area))
            return "Path is outside its owned resource role.";
        if (role == "fixtures") lock (sync) if (!fixtures.ContainsKey(full)) return "Fixture was not admitted by this run.";
        if (role is "delivery" or "export" && UnpreparedDirectory(role == "delivery" ? full : Path.GetDirectoryName(full)!))
            return "Folder was not prepared by this run.";
        return null;
    }

    /// <summary>
    /// True for an ordinary directory a native dialog created inside a role (its "New folder"
    /// button): no recorded owned directory lies between it and its first protected ancestor.
    /// Creating a folder grants no authority, so it is refused as a selection. A reparse point or
    /// a missing component is not classified here; the following Require still refuses the run.
    /// </summary>
    private bool UnpreparedDirectory(string directory)
    {
        lock (sync)
        {
            if (owner is null) return false;
            bool unprepared = false;
            for (string? current = directory; current is not null && PathRules.Within(current, Root); current = Path.GetDirectoryName(current))
            {
                // The protected ancestor is verified before any nonfatal answer: a tampered marker
                // or lost ownership is an integrity fault and throws instead of being a refusal.
                if (owner.OwnsStateDirectory(current)) { owner.VerifyStateMarker(); break; }
                if (protectedDirectories.TryGetValue(current, out OwnedDirectoryLease? held)) { held.Verify(); break; }
                if (!Directory.Exists(current) || (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return false;
                unprepared = true;
            }
            return unprepared;
        }
    }

    public NativePathLease Read(string path, string role)
    {
        string full = Require(path, role);
        lock (sync)
        {
            if ((PathRules.Within(full, At("fixtures")) || PathRules.Within(full, At("evidence"))) && !fixtures.ContainsKey(full))
                throw new IOException("Fixture or diagnostic evidence was not admitted by this run.");
            NativePathLease held = NativePathLease.ReadFile(full);
            if (identities.TryGetValue(full, out string? expected) && expected != held.Identity)
            { held.Dispose(); throw new IOException("Admitted file identity changed."); }
            identities[full] = held.Identity;
            return held;
        }
    }

    public void Admit(string path)
    {
        string full = Require(path, PathRules.Within(path, At("evidence")) ? "evidence" : "fixtures");
        if (!string.Equals(Path.GetExtension(full), ".png", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Path.GetExtension(full), ".tif", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Only declared synthetic PNG/TIFF files may be admitted.");
        lock (sync)
        {
            if (fixtures.ContainsKey(full)) throw new IOException("Fixture already admitted; identity cannot be silently replaced.");
            NativePathLease held = NativePathLease.ReadFile(full);
            fixtures.Add(full, held);
            identities.Add(full, held.Identity);
        }
    }

    /// <summary>
    /// Interactive only: admits the picker fixtures this run's preparation recorded in its
    /// scenario ledger, by exact path and SHA-256. Admission holds each file before hashing it,
    /// so a changed or replaced fixture refuses instead of becoming an import authority.
    /// The ledger bytes themselves must match the digest the completed preparation bound into
    /// its prepared record; nothing here recomputes a digest to bless changed contents.
    /// </summary>
    public int AdmitPreparedFixtures(string ledgerPath, string? preparedLedgerSha256)
    {
        string ledger = Require(ledgerPath, "evidence");
        if (!string.Equals(ledger, At("evidence", "scenario-ledger.json"), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Only this run's scenario ledger can name prepared fixtures.");
        if (string.IsNullOrEmpty(preparedLedgerSha256)) throw new IOException("No scenario ledger digest is bound to this preparation.");
        byte[] bytes;
        using (NativePathLease held = NativePathLease.ReadFile(ledger)) bytes = File.ReadAllBytes(ledger);
        if (!string.Equals(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), preparedLedgerSha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Scenario ledger bytes differ from the digest bound at preparation; prepare a fresh root.");
        using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(bytes);
        Dictionary<string, string> recorded = new(StringComparer.OrdinalIgnoreCase);
        foreach (System.Text.Json.JsonElement scenario in document.RootElement.GetProperty("Scenarios").EnumerateArray())
        foreach (System.Text.Json.JsonElement fact in scenario.GetProperty("Facts").EnumerateArray())
        {
            if (fact.GetProperty("Action").GetString() != "immutable synthetic fixture") continue;
            System.Text.Json.JsonElement values = fact.GetProperty("Values");
            string path = values.GetProperty("path").GetString() ?? throw new IOException("Prepared fixture lacks a path.");
            string sha = values.GetProperty("sha256").GetString() ?? throw new IOException("Prepared fixture lacks a hash.");
            if (!PathRules.Within(path, At("fixtures", "inputs")) && !PathRules.Within(path, At("fixtures", "returns")))
                throw new IOException("Prepared fixture is outside the picker subtrees.");
            if (recorded.TryGetValue(path, out string? earlier) && earlier != sha) throw new IOException("Prepared fixture has conflicting hashes.");
            recorded[path] = sha;
        }
        foreach ((string path, string sha) in recorded)
        {
            Admit(path);
            if (!string.Equals(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))), sha, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Prepared fixture bytes differ from the recorded hash.");
        }
        return recorded.Count;
    }

    public void CheckWritable(string path, string role)
    {
        string full = Require(path, role);
        if (Directory.Exists(full)) throw new IOException("Output leaf is a directory.");
        if (File.Exists(full)) { using NativePathLease existing = Read(full, role); }
    }

    public void ProtectMutable(string path, bool createNew)
    {
        string full = Require(path, "state");
        lock (sync)
        {
            if (mutable.ContainsKey(full))
            {
                using NativePathLease verify = NativePathLease.MutableFile(full);
                if (verify.Identity != mutable[full].Identity) throw new IOException("Mutable resource identity changed.");
                return;
            }
            if (createNew)
            {
                using FileStream created = new(full, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
            }
            NativePathLease held = NativePathLease.MutableFile(full);
            if (!createNew && (!expectedMutable.TryGetValue(full, out string? expected) || expected != held.Identity))
            { held.Dispose(); throw new IOException("Mutable resource does not match recorded ownership identity."); }
            mutable.Add(full, held);
        }
    }

    public void ProtectDatabase(string path, bool createNew)
    {
        ProtectMutable(path, createNew);
        foreach (string suffix in new[] { "-wal", "-shm" }) ProtectMutable(path + suffix, createNew);
    }

    public void PinAncestors(string path)
    {
        lock (sync)
        {
            string directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path)!;
            while (!Directory.Exists(directory)) directory = Path.GetDirectoryName(directory) ?? throw new IOException("No resolved ancestor.");
            if (owner is not null && PathRules.Within(directory, Root))
            {
                if (owner.OwnsStateDirectory(directory)) owner.VerifyStateMarker();
                else if (protectedDirectories.TryGetValue(directory, out OwnedDirectoryLease? protectedDirectory)) protectedDirectory.Verify();
                else throw new IOException("Product directory was not protected before the operation.");
            }
            if (directories.TryGetValue(directory, out NativePathLease? existing))
            {
                using NativePathLease refreshed = NativePathLease.Ancestors(directory);
                if (refreshed.Identity != existing.Identity) throw new IOException("Owned directory identity changed.");
                return;
            }
            directories.Add(directory, NativePathLease.Ancestors(directory));
        }
    }

    public void Dispose()
    {
        foreach (NativePathLease held in fixtures.Values) held.Dispose();
        foreach (NativePathLease held in mutable.Values) held.Dispose();
        foreach (NativePathLease held in directories.Values.Reverse()) held.Dispose();
        foreach (OwnedDirectoryLease held in protectedDirectories.Values.Reverse()) held.Dispose();
        fixtures.Clear(); mutable.Clear(); directories.Clear(); DiagnosticMutation.Dispose();
    }
}
