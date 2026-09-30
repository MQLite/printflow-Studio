namespace PrintFlow.WorkstationEntry;

public sealed record CandidateManifest(string RepositoryRoot, string SourceHead, string[] ExcludedRoots,
    CandidateFile[] Files);
public sealed record CandidateFile(string Path, string Sha256, string? Mvid);
public sealed record ScenarioManifest(string RunId, string AdapterMode, string[] Families);
public sealed record EntryPlan(string Root, CandidateManifest Candidate, ScenarioManifest Scenario)
{
    public static EntryPlan Validate(string root, CandidateManifest candidate, ScenarioManifest scenario, bool validatedResume = false)
    {
        string full = PathRules.Canonical(root);
        string repository = PathRules.Canonical(candidate.RepositoryRoot);
        string parent = Path.Combine(repository, "artifacts", "pf-opux-scrum11154-workstation-entry", "runs");
        if (!string.Equals(Path.GetDirectoryName(full), parent, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Run must be a direct child of the explicit repository-owned runs directory.");
        if (scenario.RunId != Path.GetFileName(full) || scenario.RunId.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Run identity does not match the root leaf.");
        if (scenario.AdapterMode != "Fake") throw new ArgumentException("Only synthetic Fake adapters are permitted; Production always refuses.");
        if (scenario.Families.Length != 6 || !scenario.Families.Order().SequenceEqual(new[] { "F1", "F2", "F3", "F4", "F5", "F6" }))
            throw new ArgumentException("Exactly the six distinct approved scenario families are required.");
        if (candidate.SourceHead.Length != 40 || !candidate.SourceHead.All(Uri.IsHexDigit))
            throw new ArgumentException("Candidate requires an explicit source commit.");
        string[] defaults = [@"D:\PrintFlowStudio", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrintFlow Studio"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PrintFlow Studio")];
        foreach (string denied in candidate.ExcludedRoots.Concat(defaults))
            if (PathRules.Overlaps(full, PathRules.Canonical(denied))) throw new ArgumentException("Run overlaps an installed, production or default resource root.");
        if (!validatedResume && Directory.Exists(full) && Directory.EnumerateFileSystemEntries(full).Any())
            throw new ArgumentException("Existing run content requires verified resume ownership; never adopt it as a new run.");
        using NativePathLease ancestry = NativePathLease.Ancestors(full);
        NativePathLease.RequireLocalNtfs(full);
        return new(full, candidate, scenario);
    }

    public string At(params string[] parts) => Path.Combine([Root, .. parts]);
}

public static class PathRules
{
    public static string Canonical(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length < 4 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\' ||
            path[3..].Contains(':') || path.Contains('/') || path.Contains('%') || path.Contains('~'))
            throw new ArgumentException("An explicit canonical local drive path is required; aliases and streams are refused.");
        if (path.Split('\\').Skip(1).Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith('.') || p.EndsWith(' ') || p.IndexOfAny(['*', '?', '"', '<', '>', '|']) >= 0))
            throw new ArgumentException("Ambiguous or traversing path components are refused.");
        string full = Path.GetFullPath(path);
        if (!string.Equals(full, path, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Path is not canonical.");
        return full;
    }

    public static bool Within(string path, string root) =>
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    public static bool Overlaps(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase) || Within(left, right) || Within(right, left);
}
