using System.IO;
using System.Security.Cryptography;
using System.Text;
using PrintFlow.Domain.Files;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// A synthetic workstation preset manifest — fake paths, fake hashes, never the signed
/// production manifest (task §43, §50).
/// </summary>
/// <remarks>
/// The identity is synthetic; the <c>storageAndNamingContract</c> patterns are not. They are
/// the accepted manifest's own spellings, and a fixture that used a different naming language
/// is precisely what let the R2 Final Gate defect through: every integration test in the suite
/// ran against positional patterns no accepted manifest has ever carried, so the whole suite
/// stayed green while the real preset crashed the shell (naming-contract fix §5, §9).
/// <para>
/// <see cref="AcceptedNamingContract"/> holds the values and the check that keeps them honest
/// against the manifest on this workstation.
/// </para>
/// </remarks>
internal static class PresetFixture
{
    public const string PresetId = "test-workstation-v1";
    public const string PresetVersion = "0.0.1";

    public static readonly string Json =
        $$"""
        {
          "presetId": "test-workstation-v1",
          "presetVersion": "0.0.1",
          "storageAndNamingContract": {
            "enhancedPattern": "{{AcceptedNamingContract.EnhancedPattern}}",
            "cutoutPattern": "{{AcceptedNamingContract.CutoutPattern}}",
            "productionTiffPattern": "{{AcceptedNamingContract.ProductionTiffPattern}}",
            "collisionPattern": "{{AcceptedNamingContract.CollisionPattern}}"
          }
        }
        """;

    /// <summary>Writes the synthetic manifest to <paramref name="directory"/> and returns its path and hash.</summary>
    public static (string Path, Sha256 Sha256) Write(string directory, string fileName = "synthetic-preset.json")
    {
        Directory.CreateDirectory(directory);
        byte[] bytes = Encoding.UTF8.GetBytes(Json);
        string path = System.IO.Path.Combine(directory, fileName);
        File.WriteAllBytes(path, bytes);
        return (path, Sha256.FromBytes(SHA256.HashData(bytes)));
    }
}
