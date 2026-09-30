using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrintFlow.WorkstationEntry;

public sealed record ValidatedInput(EntryPlan Plan, string CandidateHash, string ScenarioHash) : IDisposable
{
    internal List<NativePathLease> CandidateLeases { get; } = [];
    public void Dispose() { foreach (NativePathLease lease in CandidateLeases) lease.Dispose(); CandidateLeases.Clear(); }
}

public static class ManifestReader
{
    public static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, WriteIndented = true };
    public static ValidatedInput Read(EntryOptions options)
    {
        (CandidateManifest candidate, string candidateHash) = ReadFile<CandidateManifest>(options.CandidateManifest);
        (ScenarioManifest scenario, string scenarioHash) = ReadFile<ScenarioManifest>(options.ScenarioManifest);
        if (candidate.RepositoryRoot is null || candidate.SourceHead is null || candidate.ExcludedRoots is null || candidate.Files is null ||
            scenario.RunId is null || scenario.AdapterMode is null || scenario.Families is null)
            throw new ArgumentException("Incomplete explicit manifests.");
        bool resume = options.Mode == "Interactive" || options.Resume;
        if (resume) OwnedRun.VerifyResume(options.Root, candidateHash, scenarioHash, options.Mode == "Interactive");
        EntryPlan plan = EntryPlan.Validate(options.Root, candidate, scenario, resume);
        if (candidate.Files.Length == 0 || candidate.Files.GroupBy(f => f.Path, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() != 1))
            throw new ArgumentException("Candidate must contain unique source and assembly identities.");
        string assembly = typeof(ManifestReader).Assembly.Location;
        string outputRoot = Path.GetDirectoryName(assembly)!;
        HashSet<string> declared = candidate.Files.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string required in EnumerateOrdinaryFiles(outputRoot).Concat(EnumerateOrdinaryFiles(Path.Combine(candidate.RepositoryRoot, "src")))
                     .Concat(EnumerateOrdinaryFiles(Path.Combine(candidate.RepositoryRoot, "tests", "PrintFlow.WorkstationEntry"))))
            if (!declared.Contains(required)) throw new ArgumentException("Candidate omits current source/resource/binary: " + Path.GetFileName(required));
        foreach (string product in new[] { "PrintFlow.WorkstationEntry.dll", "PrintFlow.App.dll", "PrintFlow.Domain.dll", "PrintFlow.Workflow.dll", "PrintFlow.Infrastructure.dll" })
            if (!candidate.Files.Any(file => string.Equals(file.Path, Path.Combine(outputRoot, product), StringComparison.OrdinalIgnoreCase) && file.Mvid is not null))
                throw new ArgumentException("Candidate is missing a required assembly identity: " + product);
        ValidatedInput input = new(plan, candidateHash, scenarioHash);
        try
        {
        foreach (CandidateFile file in candidate.Files)
        {
            string path = PathRules.Canonical(file.Path);
            if (!PathRules.Within(path, candidate.RepositoryRoot)) throw new ArgumentException("Candidate source/assembly must belong to this repository.");
            input.CandidateLeases.Add(NativePathLease.ReadFile(path));
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(stream)), file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Candidate bytes changed: " + Path.GetFileName(path));
            if (file.Mvid is not null)
            {
                stream.Position = 0;
                using PEReader pe = new(stream, PEStreamOptions.LeaveOpen);
                MetadataReader metadata = pe.GetMetadataReader();
                string mvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid).ToString();
                if (!string.Equals(mvid, file.Mvid, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Candidate MVID changed.");
            }
        }
        if (!candidate.Files.Any(f => string.Equals(f.Path, assembly, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Executing host assembly is absent from candidate manifest.");
        return input;
        }
        catch { input.Dispose(); throw; }
    }

    private static IEnumerable<string> EnumerateOrdinaryFiles(string directory)
    {
        using NativePathLease held = NativePathLease.Ancestors(directory);
        foreach (string file in Directory.EnumerateFiles(directory)) yield return file;
        foreach (string child in Directory.EnumerateDirectories(directory))
        {
            if (Path.GetFileName(child) is "bin" or "obj") continue;
            foreach (string file in EnumerateOrdinaryFiles(child)) yield return file;
        }
    }

    public static (T Value, string Sha256) ReadFile<T>(string path)
    {
        path = PathRules.Canonical(path);
        using NativePathLease held = NativePathLease.ReadFile(path);
        byte[] bytes = File.ReadAllBytes(path);
        using JsonDocument document = JsonDocument.Parse(bytes);
        RejectDuplicateKeys(document.RootElement);
        T value = JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new ArgumentException("Manifest is null.");
        return (value, Convert.ToHexString(SHA256.HashData(bytes)));
    }
    private static void RejectDuplicateKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in element.EnumerateObject())
            { if (!names.Add(property.Name)) throw new ArgumentException("Duplicate JSON key refused."); RejectDuplicateKeys(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (JsonElement child in element.EnumerateArray()) RejectDuplicateKeys(child);
    }
}
