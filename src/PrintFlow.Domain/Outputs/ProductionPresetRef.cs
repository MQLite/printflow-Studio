using PrintFlow.Domain.Files;

namespace PrintFlow.Domain.Outputs;

/// <summary>
/// An immutable pointer to the signed workstation preset a production output was made with.
/// </summary>
/// <remarks>
/// Identity plus hash, never content. The preset manifest itself lives outside the
/// repository under <c>D:\PrintFlowStudio\Baseline</c>; PrintFlow reads it, verifies its
/// SHA-256, and records this reference against every output so a produced file can always
/// be traced to the exact signed configuration that made it.
/// </remarks>
public sealed record ProductionPresetRef(string PresetId, string PresetVersion, Sha256 ManifestSha256)
{
    public override string ToString() => $"{PresetId} {PresetVersion} ({ManifestSha256.ShortForm})";
}
