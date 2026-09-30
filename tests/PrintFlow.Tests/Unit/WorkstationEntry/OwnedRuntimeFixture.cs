using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PrintFlow.WorkstationEntry;
using Xunit;

namespace PrintFlow.Tests.Unit.WorkstationEntry;

/// <summary>
/// A single, reviewed-run claim shared by runtime-only lease and diagnostic tests. Construction
/// fails closed before any SQLite write when the explicit run authority does not match.
/// </summary>
public sealed class OwnedRuntimeFixture : IDisposable
{
    private readonly NativePathLease rootIdentity;
    private readonly OwnedInstanceClaim claim;
    private readonly List<NativePathLease> candidateLeases = [];
    private readonly List<string> ownedDatabases = [];
    public OwnedPaths Paths { get; }

    public OwnedRuntimeFixture()
    {
        string root = PathRules.Canonical(Required("PRINTFLOW_ENTRY_RUN_ROOT"));
        string repository = FindRepositoryRoot();
        string runs = Path.Combine(repository, "artifacts", "pf-opux-scrum11154-workstation-entry", "runs");
        if (!string.Equals(Path.GetDirectoryName(root), runs, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Runtime fixture root is not a direct child of the task-owned runs area.");
        NativePathLease.RequireLocalNtfs(root);
        rootIdentity = NativePathLease.Ancestors(root);
        OwnedPaths? owned = null;
        OwnedInstanceClaim? ownerClaim = null;
        List<NativePathLease> recordedLeaves = [];
        try
        {
            string expectedCandidateHash = Required("PRINTFLOW_ENTRY_CANDIDATE_HASH");
            string expectedScenarioHash = Required("PRINTFLOW_ENTRY_SCENARIO_HASH");
            (CandidateManifest candidate, string candidateHash) = ManifestReader.ReadFile<CandidateManifest>(
                PathRules.Canonical(Required("PRINTFLOW_ENTRY_CANDIDATE_MANIFEST")));
            (ScenarioManifest scenario, string scenarioHash) = ManifestReader.ReadFile<ScenarioManifest>(
                PathRules.Canonical(Required("PRINTFLOW_ENTRY_SCENARIO_MANIFEST")));
            if (!string.Equals(candidateHash, expectedCandidateHash, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(scenarioHash, expectedScenarioHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Runtime manifests differ from supplied exact hashes.");
            EntryPlan.Validate(root, candidate, scenario, validatedResume: true);
            VerifyCandidateFiles(candidate);
            string testAssembly = typeof(OwnedRuntimeFixture).Assembly.Location;
            string actualTestAssemblyHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(testAssembly)));
            if (!string.Equals(actualTestAssemblyHash,
                Required("PRINTFLOW_ENTRY_TEST_ASSEMBLY_SHA256"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The executing runtime test assembly hash differs from the reviewed candidate.");
            RunOwnership owner = OwnedRun.VerifyResume(root, candidateHash, scenarioHash,
                requirePrepared: false);
            if (owner.RootIdentity != rootIdentity.Identity ||
                owner.RunToken != Required("PRINTFLOW_ENTRY_RUN_TOKEN") ||
                owner.OwnerToken != Required("PRINTFLOW_ENTRY_OWNER_TOKEN"))
                throw new InvalidOperationException("Runtime fixture run ownership or tokens mismatch.");
            Dictionary<string, string> recordedMutable = VerifyRecordedMutableLeaves(root,
                owner.MutableIdentitiesJson, recordedLeaves);
            ownerClaim = OwnedInstanceClaim.Acquire(root, owner, createNew: false);
            owned = new(root);
            owned.AttachOwner(ownerClaim);
            owned.ExpectMutable(recordedMutable);
            owned.Require(owned.At("state"), "state");
            owned.ProtectDatabase(owned.At("state", "app.db"), createNew: false);
            owned.ProtectDatabase(owned.At("state", "automation-lease.db"), createNew: false);
            foreach (NativePathLease leaf in recordedLeaves) leaf.Dispose();
            recordedLeaves.Clear();
            ownedDatabases.Add(owned.At("state", "app.db"));
            ownedDatabases.Add(owned.At("state", "automation-lease.db"));
            Paths = owned;
            claim = ownerClaim;
        }
        catch
        {
            owned?.Dispose();
            ownerClaim?.Dispose();
            foreach (NativePathLease leaf in recordedLeaves) leaf.Dispose();
            foreach (NativePathLease lease in candidateLeases) lease.Dispose();
            rootIdentity.Dispose();
            throw;
        }
    }

    public string CreateLeaseDatabase() => CreateDatabase("automation-lease-fixture-");
    public string CreateDiagnosticDatabase() => CreateDatabase("diagnostic-fixture-");

    private string CreateDatabase(string prefix)
    {
        string database = Paths.At("state", prefix + Guid.NewGuid().ToString("N") + ".db");
        Paths.ProtectDatabase(database, createNew: true);
        ownedDatabases.Add(database);
        return database;
    }

    public void Dispose()
    {
        try
        {
            foreach (string path in ownedDatabases)
            {
                Paths.ProtectDatabase(path, createNew: false);
                string connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
                using SqliteConnection pool = new(connectionString);
                SqliteConnection.ClearPool(pool);
            }
        }
        finally
        {
            try { Paths.Dispose(); }
            finally
            {
                try { claim.Dispose(); }
                finally
                {
                    foreach (NativePathLease lease in candidateLeases) lease.Dispose();
                    rootIdentity.Dispose();
                }
            }
        }
    }

    private void VerifyCandidateFiles(CandidateManifest candidate)
    {
        if (candidate.Files.Length == 0 || candidate.Files.GroupBy(file => file.Path,
                StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1))
            throw new InvalidOperationException("Runtime candidate has missing or duplicate file identities.");
        string repository = PathRules.Canonical(candidate.RepositoryRoot);
        HashSet<string> declared = candidate.Files.Select(file => file.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in new[] { Path.Combine(repository, "src"),
                     Path.Combine(repository, "tests", "PrintFlow.WorkstationEntry"),
                     Path.Combine(repository, "tests", "PrintFlow.Tests", "Unit", "WorkstationEntry"),
                     Path.Combine(repository, "tools") })
            foreach (string current in OrdinaryFiles(directory))
                if (!declared.Contains(current))
                    throw new InvalidOperationException("Runtime candidate omitted current source: " + current);
        foreach (CandidateFile file in candidate.Files)
        {
            string path = PathRules.Canonical(file.Path);
            if (!PathRules.Within(path, repository))
                throw new InvalidOperationException("Runtime candidate file is outside its repository.");
            NativePathLease held = NativePathLease.ReadFile(path);
            candidateLeases.Add(held);
            using FileStream bytes = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), file.Sha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Runtime candidate file changed: " + Path.GetFileName(path));
            if (file.Mvid is null) continue;
            bytes.Position = 0;
            using PEReader image = new(bytes, PEStreamOptions.LeaveOpen);
            MetadataReader metadata = image.GetMetadataReader();
            if (!string.Equals(metadata.GetGuid(metadata.GetModuleDefinition().Mvid).ToString(),
                    file.Mvid, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Runtime candidate module identity changed: " + Path.GetFileName(path));
        }
        string loadedHost = typeof(OwnedRun).Assembly.Location;
        string hostHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(loadedHost)));
        string hostMvid = typeof(OwnedRun).Assembly.ManifestModule.ModuleVersionId.ToString();
        if (!candidate.Files.Any(file => Path.GetFileName(file.Path).Equals("PrintFlow.WorkstationEntry.dll",
                StringComparison.OrdinalIgnoreCase) && file.Sha256.Equals(hostHash, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(file.Mvid, hostMvid, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Loaded host assembly is not the reviewed candidate module.");
        foreach (string required in new[]
                 {
                     Path.Combine(repository, "tests", "PrintFlow.Tests", "Unit", "WorkstationEntry", "OwnedRuntimeFixture.cs"),
                     Path.Combine(repository, "tests", "PrintFlow.Tests", "Unit", "WorkstationEntry", "DiagnosticBoundaryTests.cs")
                 })
            if (!candidate.Files.Any(file => string.Equals(file.Path, required, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Runtime candidate omitted required source or host assembly: " + required);
    }

    private static Dictionary<string, string> VerifyRecordedMutableLeaves(string root,
        string ledgerJson, List<NativePathLease> held)
    {
        Dictionary<string, string> recorded = JsonSerializer.Deserialize<Dictionary<string, string>>(
            ledgerJson) ?? throw new InvalidOperationException("Owned mutable identity ledger is missing.");
        if (recorded.Count == 0)
            throw new InvalidOperationException("Owned mutable identity ledger has no database leaves.");
        string state = Path.Combine(root, "state");
        foreach ((string path, string expected) in recorded)
        {
            string full = PathRules.Canonical(path);
            if (!PathRules.Within(full, state))
                throw new InvalidOperationException("Recorded mutable leaf escapes run state.");
            NativePathLease leaf = NativePathLease.ReadFile(full);
            held.Add(leaf);
            if (leaf.Identity != expected)
                throw new InvalidOperationException("Recorded mutable leaf identity changed: " + full);
        }
        return recorded;
    }

    private static IEnumerable<string> OrdinaryFiles(string directory)
    {
        using NativePathLease held = NativePathLease.Ancestors(directory);
        foreach (string file in Directory.EnumerateFiles(directory)) yield return file;
        foreach (string child in Directory.EnumerateDirectories(directory))
        {
            if (Path.GetFileName(child) is "bin" or "obj") continue;
            foreach (string file in OrdinaryFiles(child)) yield return file;
        }
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value :
            throw new InvalidOperationException("Explicit reviewed run input missing: " + name);

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PrintFlowStudio.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Could not resolve repository root.");
    }
}

[CollectionDefinition("WorkstationEntryRuntimeLease", DisableParallelization = true)]
public sealed class WorkstationEntryRuntimeLeaseCollection : ICollectionFixture<OwnedRuntimeFixture> { }
