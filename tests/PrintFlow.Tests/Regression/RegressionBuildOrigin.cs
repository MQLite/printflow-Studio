using System.Collections.Immutable;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using PrintFlow.Infrastructure.Verification;

namespace PrintFlow.Tests.Regression;

/// <summary>The immutable reference captured by an execution, never supplied by a later reviewer.</summary>
public sealed record RegressionBuildOrigin(string ReceiptPath, string ReceiptSha256, string PairId)
{
    /// <summary>Validates the controlled outputs against this loaded test host before operational setup.</summary>
    public static RegressionBuildOrigin Capture(string receiptPath, string candidateFolder)
    {
        try
        {
            string path = Path.GetFullPath(receiptPath);
            Require(!path.EndsWith(".pending", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".incomplete", StringComparison.OrdinalIgnoreCase), "receipt is not complete");
            byte[] bytes = File.ReadAllBytes(path);
            // Windows PowerShell 5 writes UTF-8 with a BOM. Hash the original file, but the JSON
            // reader consumes JSON bytes after that optional encoding marker.
            int offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            Pair receipt = JsonSerializer.Deserialize<Pair>(bytes.AsSpan(offset),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Build origin: empty receipt.");
            Require(receipt.Version == 1 && Guid.TryParse(receipt.PairId, out _) &&
                !string.IsNullOrWhiteSpace(receipt.CompletedAtUtc) &&
                IsDigest(receipt.InputDigest) && !string.IsNullOrWhiteSpace(receipt.SourceRevision) &&
                !string.IsNullOrWhiteSpace(receipt.SdkVersion) && !string.IsNullOrWhiteSpace(receipt.DotnetPath) &&
                !receipt.Inputs.IsDefaultOrEmpty && !receipt.HarnessCommand.IsDefaultOrEmpty &&
                !receipt.CandidateCommand.IsDefaultOrEmpty,
                "receipt is not a completed supported build pair");
            Require(Path.IsPathFullyQualified(receipt.HarnessFolder) &&
                Path.IsPathFullyQualified(receipt.CandidateFolder), "output folders must be absolute");

            Compare(receipt.HarnessProductAssemblies, ProductBuildIdentity.FromFolder(receipt.HarnessFolder), "staged harness");
            Compare(receipt.CandidateProductAssemblies, ProductBuildIdentity.FromFolder(receipt.CandidateFolder), "staged candidate");
            Compare(receipt.HarnessProductAssemblies, ProductBuildIdentity.Running(), "loaded harness");
            Compare(receipt.CandidateProductAssemblies, ProductBuildIdentity.FromFolder(candidateFolder), "selected candidate");
            CheckRuntime(receipt.HarnessFolder, receipt.HarnessArtifacts);
            CheckRuntime(Path.GetDirectoryName(typeof(RegressionBuildOrigin).Assembly.Location)!, receipt.HarnessArtifacts);
            return new RegressionBuildOrigin(path, Convert.ToHexString(SHA256.HashData(bytes)), receipt.PairId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            throw new InvalidDataException("Build origin refused before operational setup: " + ex.Message, ex);
        }
    }

    private static void Compare(ImmutableArray<ProductAssemblyIdentity> expected,
        ImmutableArray<ProductAssemblyIdentity> actual, string side)
    {
        Require(!expected.IsDefault && expected.Length == ProductBuildIdentity.ProductAssemblyFileNames.Length &&
            expected.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == expected.Length,
            side + " inventory is incomplete or duplicated");
        ImmutableArray<string> differences = ProductBuildIdentity.CompareBytes(expected, actual);
        Require(differences.IsEmpty, side + ": " + string.Join("; ", differences) +
            " Matching version labels do not establish origin.");
    }

    private static void CheckRuntime(string folder, ImmutableArray<PairFile> artifacts)
    {
        Require(!artifacts.IsDefaultOrEmpty, "missing test-host artifacts");
        string[] names = artifacts.Select(x => x.Name).ToArray();
        string[] required = ["PrintFlow.Tests.dll", "PrintFlow.Tests.deps.json", "PrintFlow.Tests.runtimeconfig.json", "testhost.dll",
            .. ProductBuildIdentity.ProductAssemblyFileNames];
        Require(required.All(x => names.Contains(x, StringComparer.OrdinalIgnoreCase)), "incomplete test-host artifacts");
        string[] actual = Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .Select(x => Path.GetRelativePath(folder, x).Replace('\\', '/')).ToArray();
        Require(names.Length == actual.Length && names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Length &&
            names.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(actual), "test-host runtime file set changed");
        foreach (PairFile artifact in artifacts)
        {
            Require(!Path.IsPathRooted(artifact.Name) && !artifact.Name.Replace('\\', '/').Split('/').Contains(".."),
                "artifact escapes output folder");
            Require(IsDigest(artifact.Sha256) && string.Equals(artifact.Sha256,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(folder, artifact.Name)))),
                StringComparison.OrdinalIgnoreCase), "test-host bytes differ for " + artifact.Name);
        }
    }

    private static bool IsDigest(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidDataException("Build origin: " + message); }
    }

    private sealed record PairFile(string Name, string Sha256);

    private sealed record Pair(int Version, string PairId, string SourceRevision, string InputDigest,
        string SdkVersion, string DotnetPath, ImmutableArray<PairFile> Inputs,
        ImmutableArray<string> HarnessCommand, ImmutableArray<string> CandidateCommand,
        string HarnessFolder, string CandidateFolder,
        ImmutableArray<ProductAssemblyIdentity> HarnessProductAssemblies,
        ImmutableArray<ProductAssemblyIdentity> CandidateProductAssemblies,
        ImmutableArray<PairFile> HarnessArtifacts, string CompletedAtUtc);
}
