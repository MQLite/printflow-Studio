using System.ComponentModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Delivery;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Workspace;

/// <summary>
/// Colleague-correction package files on the local file system (SCRUM-11148, design §5.3, §6.2).
/// </summary>
/// <remarks>
/// Every path is resolved through the workspace, so a folder can never leave the workspace root,
/// and only folders inside a session's <c>Correction\</c> area are accepted. A copy is always a new
/// file with its own bytes: a create-new temporary, flushed and re-hashed, then moved into place
/// without replacement. A temporary is left inert on interruption; a retry creates a new one,
/// since a predictable name alone cannot establish ownership. Nothing is deleted.
/// </remarks>
public sealed class FileCorrectionPackageStore(IWorkspace workspace) : ICorrectionPackageStore
{
    private const string CorrectionSegment = "/Correction/";

    /// <inheritdoc />
    public async Task<OperationResult<CorrectionFileOutcome>> PlaceVerifiedCopyAsync(
        WorkspaceDirRef folder, string fileName, WorkspaceFileRef source, Sha256 expected,
        string partialSuffix, bool markReadOnly, CancellationToken cancellationToken)
    {
        string? partial = null;
        try
        {
            if (Resolve(folder, fileName) is not { } target)
                return Refused("The correction file name or folder is not inside the correction area.");
            if (!IsSuffix(partialSuffix))
                return Refused("The temporary-file suffix is not this request's own.");

            string sourcePath = workspace.ResolveAbsolute(source);
            ReparsePointGuard.RefuseAncestry(target.Folder);
            Directory.CreateDirectory(target.Folder);

            if (File.Exists(target.Final))
                return await ExistingAsync(target.Final, fileName, expected, cancellationToken);

            // Neither the request suffix nor a file's writability proves ownership. Even an
            // interrupted copy remains untouched; a retry gets a fresh, create-new staging name.
            partial = Path.Combine(target.Folder, $".{partialSuffix}-{Guid.NewGuid():N}.partial");

            await using (FileStream input = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true))
            await using (FileStream output = new(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
            {
                await input.CopyToAsync(output, cancellationToken);
                output.Flush(flushToDisk: true);
            }

            if (await HashStandaloneAsync(partial, cancellationToken) != expected)
            {
                return OperationResult.Fail<CorrectionFileOutcome>(OperationFailure.Create(
                    FailureCode.RevisionIntegrityMismatch,
                    $"The copy written for '{fileName}' does not have the recorded hash; it was not put in place.",
                    isRetryable: true,
                    context: new Dictionary<string, string> { ["copyMismatch"] = "true", ["file"] = fileName }));
            }

            if (markReadOnly)
                File.SetAttributes(partial, File.GetAttributes(partial) | FileAttributes.ReadOnly);

            try
            {
                File.Move(partial, target.Final, overwrite: false);
            }
            catch (IOException) when (File.Exists(target.Final))
            {
                // Someone else's file arrived first. Theirs is never touched; our temporary
                // stays inert and a later try uses another fresh staging name.
                return await ExistingAsync(target.Final, fileName, expected, cancellationToken);
            }

            partial = null;
            return OperationResult.Ok(CorrectionFileOutcome.Created);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Fail<CorrectionFileOutcome>(OperationFailure.Create(
                FailureCode.Cancelled, "Preparing the correction files was cancelled.", isRetryable: true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or Win32Exception)
        {
            return OperationResult.Fail<CorrectionFileOutcome>(OperationFailure.Create(
                FailureCode.WorkspaceError,
                $"The correction file '{fileName}' could not be prepared: {ex.Message}",
                isRetryable: true,
                context: new Dictionary<string, string> { ["file"] = fileName, ["partialKept"] = partial is null ? "false" : "true" }));
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<CorrectionFileOutcome>> WriteTextOnceAsync(
        WorkspaceDirRef folder, string fileName, string content, CancellationToken cancellationToken)
    {
        try
        {
            if (Resolve(folder, fileName) is not { } target)
                return Refused("The correction file name or folder is not inside the correction area.");
            ReparsePointGuard.RefuseAncestry(target.Folder);
            Directory.CreateDirectory(target.Folder);
            if (File.Exists(target.Final))
                return ExistingInstructions(folder, fileName);

            // With a byte-order mark, so Notepad shows the Chinese steps correctly.
            byte[] bytes = [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(content)];
            await using FileStream output = new(target.Final, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await output.WriteAsync(bytes, cancellationToken);
            return OperationResult.Ok(CorrectionFileOutcome.Created);
        }
        catch (IOException) when (Resolve(folder, fileName) is { } existing && File.Exists(existing.Final))
        {
            return ExistingInstructions(folder, fileName);
        }
        catch (OperationCanceledException)
        {
            return OperationResult.Fail<CorrectionFileOutcome>(OperationFailure.Create(
                FailureCode.Cancelled, "Preparing the correction files was cancelled.", isRetryable: true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return OperationResult.Fail<CorrectionFileOutcome>(OperationFailure.Create(
                FailureCode.WorkspaceError, $"The instructions file could not be written: {ex.Message}", isRetryable: true));
        }
    }

    /// <inheritdoc />
    public bool Exists(WorkspaceDirRef folder, string fileName)
    {
        try
        {
            if (Resolve(folder, fileName) is not { } target || !File.Exists(target.Final))
                return false;
            ReparsePointGuard.RefuseAncestry(target.Folder);
            using var handle = WindowsDeliveryNative.OpenFinal(target.Final, stagedHandleIsHeld: false);
            return NativeMethods.GetFileInformationByHandle(handle, out NativeMethods.FileInformation information) &&
                information.NumberOfLinks == 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException or Win32Exception)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public string ResolveFolder(WorkspaceDirRef folder) => workspace.ResolveAbsoluteDirectory(folder);

    /// <summary>
    /// The folder and final path, or null when the folder is not a correction folder or the name
    /// is not a plain file name that stays in it.
    /// </summary>
    private (string Folder, string Final)? Resolve(WorkspaceDirRef folder, string fileName)
    {
        if (!folder.RelativePath.Contains(CorrectionSegment, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            fileName is "." or "..")
        {
            return null;
        }

        string folderPath = Path.GetFullPath(workspace.ResolveAbsoluteDirectory(folder));
        string finalPath = Path.GetFullPath(Path.Combine(folderPath, fileName));
        return string.Equals(Path.GetDirectoryName(finalPath), folderPath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            ? (folderPath, finalPath)
            : null;
    }

    private static async Task<OperationResult<CorrectionFileOutcome>> ExistingAsync(
        string finalPath, string fileName, Sha256 expected, CancellationToken cancellationToken)
    {
        try
        {
            if (await HashStandaloneAsync(finalPath, cancellationToken) == expected)
                return OperationResult.Ok(CorrectionFileOutcome.AlreadyPresent);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or Win32Exception)
        {
            // A same-byte hard link or reparse point is still not an independent package copy.
        }

        return OperationResult.Fail<CorrectionFileOutcome>(OperationFailure.Create(
                FailureCode.WorkspaceError,
                $"A different or linked file named '{fileName}' is already in the correction folder; it was left untouched.",
                isRetryable: true,
                context: new Dictionary<string, string> { ["file"] = fileName, ["collision"] = "true" },
                messageKey: "Session_CorrectionNameTaken"));
    }

    private static async Task<Sha256> HashStandaloneAsync(string path, CancellationToken cancellationToken)
    {
        // OPEN_REPARSE_POINT inspects the named file itself. Keep that same handle through the
        // hash, and refuse a hard link even when its bytes match the recorded revision hash.
        await using FileStream read = new(WindowsDeliveryNative.OpenFinal(path, stagedHandleIsHeld: false),
            FileAccess.Read, 1 << 20, isAsync: false);
        if (!NativeMethods.GetFileInformationByHandle(read.SafeFileHandle, out NativeMethods.FileInformation information) ||
            information.NumberOfLinks != 1)
            throw new IOException("A correction-package file has unverifiable or shared file identity.");
        return Sha256.FromBytes(await SHA256.HashDataAsync(read, cancellationToken));
    }

    private OperationResult<CorrectionFileOutcome> ExistingInstructions(WorkspaceDirRef folder, string fileName) =>
        Exists(folder, fileName)
            ? OperationResult.Ok(CorrectionFileOutcome.AlreadyPresent)
            : Refused("An instructions file with a linked or unverifiable identity was left untouched.");

    private static bool IsSuffix(string suffix) =>
        suffix.StartsWith("partial-", StringComparison.Ordinal) && suffix.Length is > 8 and <= 40 &&
        suffix.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    private static OperationResult<CorrectionFileOutcome> Refused(string detail) =>
        OperationResult.Fail<CorrectionFileOutcome>(FailureCode.PreconditionNotMet, detail);
}
