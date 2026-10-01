using System.Text.Json;

namespace PrintFlow.WorkstationEntry.Scenarios;

/// <summary>Runtime evidence only. A missing field means the action did not establish that fact.</summary>
public sealed class ScenarioLedger
{
    public string EvidenceKind { get; init; } = "SYNTHETIC_NONINTERACTIVE_SERVICE_SCENARIOS";
    public DateTimeOffset StartedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedUtc { get; set; }
    public List<ScenarioEntry> Scenarios { get; } = [];
    public List<CoverageEntry> Coverage { get; } = [];
    public bool AllVerified => Scenarios.Count == 8 &&
        Scenarios.All(x => x.Status == "VERIFIED_SYNTHETIC_SERVICE_FACTS");

    /// <summary>SHA-256 of the exact bytes <see cref="Save"/> wrote; the prepared record binds it.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? SavedSha256 { get; private set; }

    public string Save(string path, Action<string> validateOwnedWrite)
    {
        ArgumentNullException.ThrowIfNull(validateOwnedWrite);
        validateOwnedWrite(path);
        byte[] bytes = new System.Text.UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        using (FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(true);
        }
        return SavedSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
    }
}

public sealed class ScenarioEntry(string id, string purpose)
{
    public string Id { get; } = id;
    public string Purpose { get; } = purpose;
    public string Status { get; set; } = "NOT_RUN";
    public string? Limitation { get; set; }
    public List<ScenarioFact> Facts { get; } = [];
}

public sealed record ScenarioFact(string Action, Dictionary<string, string?> Values);

public sealed record CoverageEntry(string ChecklistRow, string ScenarioEvidence, string Limit);
