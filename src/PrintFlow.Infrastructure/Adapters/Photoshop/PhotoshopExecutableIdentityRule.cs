using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

/// <summary>The accepted Photoshop path, version and SHA-256 check reused before every mutation.</summary>
internal static class PhotoshopExecutableIdentityRule
{
    internal static OperationResult<Unit> Verify(PhotoshopBaseline baseline)
    {
        if (!File.Exists(baseline.ExecutablePath))
        {
            return OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.PhotoshopNotInstalled,
                $"The accepted Photoshop executable is not present at '{baseline.ExecutablePath}'.",
                context: new Dictionary<string, string>
                {
                    ["expectedPath"] = baseline.ExecutablePath,
                    ["excludedInstallations"] = baseline.ExcludedInstallations.IsDefaultOrEmpty
                        ? "(none recorded)"
                        : string.Join(" | ", baseline.ExcludedInstallations),
                    ["inputSent"] = "false",
                }));
        }

        FileVersionInfo version;
        try
        {
            version = FileVersionInfo.GetVersionInfo(baseline.ExecutablePath);
        }
        catch (FileNotFoundException ex)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.PhotoshopNotInstalled,
                $"The accepted Photoshop executable could not be read: {ex.Message}");
        }

        string actualProductVersion = version.ProductVersion ?? "(unreadable)";
        string actualFileVersion = version.FileVersion ?? "(unreadable)";
        if (!string.Equals(actualProductVersion, baseline.AcceptedProductVersion, StringComparison.Ordinal) ||
            !string.Equals(actualFileVersion, baseline.AcceptedFileVersion, StringComparison.Ordinal))
        {
            return OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.PhotoshopNotInstalled,
                $"The binary at '{baseline.ExecutablePath}' reports version " +
                $"'{actualProductVersion}' / '{actualFileVersion}', not the accepted " +
                $"'{baseline.AcceptedProductVersion}' / '{baseline.AcceptedFileVersion}'. A Photoshop " +
                "upgrade requires preset revalidation before automation resumes.",
                context: new Dictionary<string, string>
                {
                    ["expectedProductVersion"] = baseline.AcceptedProductVersion,
                    ["actualProductVersion"] = actualProductVersion,
                    ["expectedFileVersion"] = baseline.AcceptedFileVersion,
                    ["actualFileVersion"] = actualFileVersion,
                    ["inputSent"] = "false",
                }));
        }

        Sha256 actual;
        try
        {
            using FileStream stream = new(
                baseline.ExecutablePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 1 << 20, useAsync: false);
            actual = Sha256.FromBytes(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.PhotoshopNotInstalled,
                $"The accepted Photoshop executable could not be read: {ex.Message}");
        }

        return actual.Equals(baseline.ExecutableSha256)
            ? OperationResult.Ok()
            : OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.PhotoshopNotInstalled,
                $"The binary at '{baseline.ExecutablePath}' hashes to {actual}, not the accepted " +
                $"{baseline.ExecutableSha256}. A Photoshop upgrade requires preset revalidation before " +
                "automation resumes.",
                context: new Dictionary<string, string>
                {
                    ["expectedSha256"] = baseline.ExecutableSha256.ToString(),
                    ["actualSha256"] = actual.ToString(),
                    ["inputSent"] = "false",
                }));
    }
}
