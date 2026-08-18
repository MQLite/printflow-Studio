using System.IO;
using System.Security.Cryptography;
using System.Text;
using PrintFlow.Domain.Files;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// A synthetic workstation preset manifest — fake paths, fake hashes, never the signed
/// production manifest (task §43, §50).
/// </summary>
internal static class PresetFixture
{
    public const string PresetId = "test-workstation-v1";
    public const string PresetVersion = "0.0.1";

    public static readonly string Json =
        """
        {
          "presetId": "test-workstation-v1",
          "presetVersion": "0.0.1",
          "storageAndNamingContract": {
            "enhancedPattern": "{0}_HD.png",
            "cutoutPattern": "{0}_CUTOUT.png",
            "productionTiffPattern": "{0}_{1}mm_CMYK_W.tif",
            "collisionPattern": "_{0:D2}"
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
