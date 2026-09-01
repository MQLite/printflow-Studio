using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using PrintFlow.Domain.Files;

namespace PrintFlow.Infrastructure.Verification;

/// <summary>
/// Reads signed artefacts from disk: full-file SHA-256, version resources, and the plain facts
/// of a directory (Epic 11500 Part A §12, §13, §14, §17).
/// </summary>
/// <remarks>
/// Every file is opened read-only with no write share and is never rewritten, on the same terms
/// as <see cref="Preset.VerifiedJsonFile"/> — these are signed baseline artefacts and a
/// verification pass must not be able to alter one. The read-only <i>attribute</i> is reported
/// and never set or cleared: §15 settles that SHA-256 is the authority and this slice changes no
/// attributes.
/// <para>
/// <b>Nothing here starts a process.</b> A version resource is data inside a PE file; reading it
/// does not run the program, so the accepted Meitu and Photoshop identities are established
/// without either application appearing on the operator's screen (§17).
/// </para>
/// </remarks>
internal sealed class FileSystemArtifactReader : IWorkstationArtifactReader
{
    /// <inheritdoc />
    public FileIdentityFacts ReadFile(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        FileInfo file;
        try
        {
            file = new FileInfo(absolutePath);
            if (!file.Exists)
            {
                return FileIdentityFacts.Absent;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return FileIdentityFacts.Absent;
        }

        Sha256? digest = HashOrNull(absolutePath);
        (string? product, string? fileVersion) = VersionOrNull(absolutePath);

        return new FileIdentityFacts(true, file.Length, digest, product, fileVersion, file.IsReadOnly);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Describes the directory itself and stops there. It is not enumerated, so no customer file
    /// name is read, and nothing is created, moved or deleted (§11).
    /// </remarks>
    public DirectoryFacts ReadDirectory(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        try
        {
            if (File.Exists(absolutePath))
            {
                return DirectoryFacts.Absent with { ExistsAsFile = true };
            }

            DirectoryInfo directory = new(absolutePath);
            if (!directory.Exists)
            {
                return DirectoryFacts.Absent;
            }

            return new DirectoryFacts(
                ExistsAsDirectory: true,
                ExistsAsFile: false,
                IsReparsePoint: directory.Attributes.HasFlag(FileAttributes.ReparsePoint),
                ResolvedFullPath: directory.FullName,
                FileSystem: FileSystemOrNull(directory.FullName));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException
                                       or IOException or UnauthorizedAccessException)
        {
            return DirectoryFacts.Absent;
        }
    }

    private static Sha256? HashOrNull(string absolutePath)
    {
        try
        {
            using FileStream stream = new(
                absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 1 << 20, useAsync: false);

            return Sha256.FromBytes(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static (string? ProductVersion, string? FileVersion) VersionOrNull(string absolutePath)
    {
        try
        {
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(absolutePath);
            return (info.ProductVersion, info.FileVersion);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, null);
        }
    }

    /// <summary>Names the file system of the volume a path sits on, for the §11 root check.</summary>
    private static string? FileSystemOrNull(string absolutePath)
    {
        try
        {
            string? root = Path.GetPathRoot(absolutePath);
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            DriveInfo drive = new(root);
            return drive.IsReady ? drive.DriveFormat : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
