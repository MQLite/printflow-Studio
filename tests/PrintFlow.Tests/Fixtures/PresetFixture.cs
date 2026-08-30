using System.IO;
using System.Security.Cryptography;
using System.Text;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;

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
          },
          "productionGeometryContract": {
            "resize": {
              "resolutionPpi": 300,
              "limitsMillimetres": {
                "A3_LANDSCAPE": { "maxWidth": 360, "maxHeight": 280 },
                "A3_PORTRAIT": { "maxWidth": 280, "maxHeight": 400 },
                "A4": { "maxLongEdge": 280 },
                "A5": { "maxLongEdge": 135 }
              }
            }
          }
        }
        """;

    /// <summary>
    /// The recommendations <see cref="Json"/> configures, as the provider reads them back
    /// (Epic 11400 Part B1A.2D §3).
    /// </summary>
    /// <remarks>
    /// Written out beside the manifest rather than derived from it, so a test asserting "A4 uses
    /// the configured 280 mm long edge and not the 297 mm ISO page" is comparing against a value
    /// stated independently of the code that parses it.
    /// <para>
    /// Both forms are represented on purpose: A3 is a two-bound box and A4/A5 are single long
    /// edges, which is the shape of the accepted v1.11.0 contract. A fixture carrying only boxes
    /// would let a long-edge bug through the whole suite (§4).
    /// </para>
    /// <para>
    /// These are synthetic <i>test</i> values that happen to match the accepted contract's shape.
    /// They are not a production authority and no product code reads them.
    /// </para>
    /// </remarks>
    public static PresetPrintRecommendationSet Recommendations { get; } = new(
    [
        PresetPrintRecommendation.MaximumBox(SizePreset.A3Landscape, 360m, 280m),
        PresetPrintRecommendation.MaximumBox(SizePreset.A3Portrait, 280m, 400m),
        PresetPrintRecommendation.MaximumLongEdge(SizePreset.A4, 280m),
        PresetPrintRecommendation.MaximumLongEdge(SizePreset.A5, 135m),
    ]);

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
