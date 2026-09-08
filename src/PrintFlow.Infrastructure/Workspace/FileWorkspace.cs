using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Workspace;

/// <summary>
/// The real, disk-backed <see cref="IWorkspace"/> (Epic 11100 Task 11106b; plan §12).
/// </summary>
/// <remarks>
/// Layout, exactly as designed:
/// <code>
/// {root}\Sessions\S_&lt;utc&gt;_&lt;shortid&gt;\
///   Source\      InputSnapshot, marked read-only
///   Working\&lt;attemptId&gt;\   one directory per attempt
///   Revisions\&lt;revisionId&gt;\   durable retained history after completion
///   Approved\    collision-safe, never overwritten
///   Rejected\    retained for comparison until the session ends
///   Logs\
/// </code>
/// Every method resolves through <see cref="PathGuard"/>, so a path can never land outside the
/// configured root or inside the protected <c>Baseline</c>/<c>TestData</c> evidence areas —
/// this is the only module in the solution that joins a path at all.
/// </remarks>
public sealed class FileWorkspace : IWorkspace
{
    private const string SessionsFolder = "Sessions";

    private readonly string _rootAbsolute;

    public FileWorkspace(string workspaceRootAbsolute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRootAbsolute);
        _rootAbsolute = System.IO.Path.GetFullPath(workspaceRootAbsolute);
    }

    /// <inheritdoc />
    public OperationResult<WorkspaceDirRef> CreateSession(SessionId id, DateTimeOffset createdUtc)
    {
        string sessionRelative = $"{SessionsFolder}/{BuildSessionDirectoryName(id, createdUtc)}";

        OperationResult<string> sessionAbsolute = PathGuard.ResolveWithinRoot(_rootAbsolute, sessionRelative);
        if (sessionAbsolute.IsFailure)
        {
            return OperationResult.Fail<WorkspaceDirRef>(sessionAbsolute.Failure);
        }

        try
        {
            Directory.CreateDirectory(sessionAbsolute.Value);
            foreach (WorkspaceArea area in AllAreas)
            {
                Directory.CreateDirectory(System.IO.Path.Combine(sessionAbsolute.Value, AreaFolder(area)));
            }
        }
        catch (IOException ex)
        {
            return OperationResult.Fail<WorkspaceDirRef>(
                FailureCode.WorkspaceError, $"Could not create session workspace: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult.Fail<WorkspaceDirRef>(
                FailureCode.WorkspaceError, $"Could not create session workspace: {ex.Message}");
        }

        return OperationResult.Ok(WorkspaceDirRef.Create(sessionRelative));
    }

    /// <inheritdoc />
    public async Task<OperationResult<WorkspaceFileRef>> ImportSourceAsync(
        WorkspaceDirRef session, string sourceAbsolutePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceAbsolutePath);

        if (!File.Exists(sourceAbsolutePath))
        {
            return OperationResult.Fail<WorkspaceFileRef>(
                FailureCode.OutputMissing, $"Source file not found: '{sourceAbsolutePath}'.");
        }

        string fileName = System.IO.Path.GetFileName(sourceAbsolutePath);
        string targetRelative = $"{session.RelativePath}/{AreaFolder(WorkspaceArea.Source)}/{fileName}";

        OperationResult<string> targetAbsolute = PathGuard.ResolveWithinRoot(_rootAbsolute, targetRelative);
        if (targetAbsolute.IsFailure)
        {
            return OperationResult.Fail<WorkspaceFileRef>(targetAbsolute.Failure);
        }

        try
        {
            await using (FileStream input = new(
                sourceAbsolutePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true))
            await using (FileStream output = new(
                targetAbsolute.Value, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            File.SetAttributes(targetAbsolute.Value, FileAttributes.ReadOnly);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cancellation of an in-flight import is an ordinary operator-facing outcome, not a
            // programmer error, so it leaves this boundary as a result like every other adapter's
            // does (FakeBackgroundRemovalPng, DeterministicAlphaTrimProcessor, WicImagePreviewDecoder,
            // ProductionMeituProcessor). Guarded on the token because an OperationCanceledException
            // that does not belong to this import is somebody else's failure, not a cancellation.
            return CancelledImport(targetAbsolute.Value, sourceAbsolutePath);
        }
        catch (IOException ex)
        {
            return OperationResult.Fail<WorkspaceFileRef>(
                FailureCode.WorkspaceError, $"Could not import source file: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult.Fail<WorkspaceFileRef>(
                FailureCode.WorkspaceError, $"Could not import source file: {ex.Message}");
        }

        return OperationResult.Ok(WorkspaceFileRef.Create(targetRelative, WorkspaceArea.Source));
    }

    /// <inheritdoc />
    public async Task<OperationResult<WorkspaceFileRef>> CreateWorkingCopyAsync(
        WorkspaceDirRef session, AttemptId attemptId, WorkspaceFileRef source, CancellationToken cancellationToken)
    {
        OperationResult<string> sourceAbsolute = PathGuard.ResolveWithinRoot(_rootAbsolute, source.RelativePath);
        if (sourceAbsolute.IsFailure)
        {
            return OperationResult.Fail<WorkspaceFileRef>(sourceAbsolute.Failure);
        }

        if (!File.Exists(sourceAbsolute.Value))
        {
            return OperationResult.Fail<WorkspaceFileRef>(
                FailureCode.OutputMissing, $"Upstream revision file not found: '{sourceAbsolute.Value}'.");
        }

        string attemptFolder = attemptId.Value.ToString("D", CultureInfo.InvariantCulture);
        string targetRelative =
            $"{session.RelativePath}/{AreaFolder(WorkspaceArea.Working)}/{attemptFolder}/{source.FileName}";

        OperationResult<string> targetAbsolute = PathGuard.ResolveWithinRoot(_rootAbsolute, targetRelative);
        if (targetAbsolute.IsFailure)
        {
            return OperationResult.Fail<WorkspaceFileRef>(targetAbsolute.Failure);
        }

        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(targetAbsolute.Value)!);

            await using FileStream input = new(
                sourceAbsolute.Value, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
            await using FileStream output = new(
                targetAbsolute.Value, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
            await input.CopyToAsync(output, cancellationToken);
        }
        catch (IOException ex)
        {
            return OperationResult.Fail<WorkspaceFileRef>(
                FailureCode.WorkspaceError, $"Could not create working copy: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult.Fail<WorkspaceFileRef>(
                FailureCode.WorkspaceError, $"Could not create working copy: {ex.Message}");
        }

        return OperationResult.Ok(WorkspaceFileRef.Create(targetRelative, WorkspaceArea.Working));
    }

    /// <inheritdoc />
    public OperationResult<WorkspaceFileRef> ReserveOutput(
        WorkspaceDirRef session, WorkspaceArea area, string proposedFileName, NamingPatternSet patterns)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposedFileName);
        ArgumentNullException.ThrowIfNull(patterns);

        const int maxAttempts = 99;
        for (int sequence = 1; sequence <= maxAttempts; sequence++)
        {
            OperationResult<string> candidateName =
                OutputFileNaming.BuildCollisionCandidate(proposedFileName, patterns, sequence);
            if (candidateName.IsFailure)
            {
                return OperationResult.Fail<WorkspaceFileRef>(candidateName.Failure);
            }

            string relative = $"{session.RelativePath}/{AreaFolder(area)}/{candidateName.Value}";

            OperationResult<string> absolute = PathGuard.ResolveWithinRoot(_rootAbsolute, relative);
            if (absolute.IsFailure)
            {
                return OperationResult.Fail<WorkspaceFileRef>(absolute.Failure);
            }

            try
            {
                using FileStream reservation = new(
                    absolute.Value, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                return OperationResult.Ok(WorkspaceFileRef.Create(relative, area));
            }
            catch (IOException) when (File.Exists(absolute.Value))
            {
                // Another artefact already holds this name; try the next collision suffix.
            }
        }

        return OperationResult.Fail<WorkspaceFileRef>(
            FailureCode.WorkspaceError,
            $"Could not reserve a name for '{proposedFileName}' after {maxAttempts} collision attempts.");
    }

    /// <inheritdoc />
    public async Task<OperationResult<Unit>> WriteReservedAsync(
        WorkspaceFileRef reservedTarget, WorkspaceFileRef source, CancellationToken cancellationToken)
    {
        OperationResult<string> targetAbsolute = PathGuard.ResolveWithinRoot(_rootAbsolute, reservedTarget.RelativePath);
        if (targetAbsolute.IsFailure)
        {
            return OperationResult.Fail<Unit>(targetAbsolute.Failure);
        }

        OperationResult<string> sourceAbsolute = PathGuard.ResolveWithinRoot(_rootAbsolute, source.RelativePath);
        if (sourceAbsolute.IsFailure)
        {
            return OperationResult.Fail<Unit>(sourceAbsolute.Failure);
        }

        try
        {
            await using FileStream input = new(
                sourceAbsolute.Value, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
            await using FileStream output = new(
                targetAbsolute.Value, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
            await input.CopyToAsync(output, cancellationToken);
        }
        catch (IOException ex)
        {
            return OperationResult.Fail<Unit>(FailureCode.WorkspaceError, $"Could not write reserved output: {ex.Message}");
        }

        return OperationResult.Ok();
    }

    /// <inheritdoc />
    public async Task<OperationResult<WorkspaceFileRef>> MoveToRejectedAsync(
        WorkspaceDirRef session, WorkspaceFileRef source, string fileName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        OperationResult<string> sourceAbsolute = PathGuard.ResolveWithinRoot(_rootAbsolute, source.RelativePath);
        if (sourceAbsolute.IsFailure)
        {
            return OperationResult.Fail<WorkspaceFileRef>(sourceAbsolute.Failure);
        }

        for (int sequence = 1; sequence <= 99; sequence++)
        {
            string candidateName = sequence == 1
                ? fileName
                : InsertBeforeExtension(fileName, $"_{sequence:D2}");

            string targetRelative = $"{session.RelativePath}/{AreaFolder(WorkspaceArea.Rejected)}/{candidateName}";
            OperationResult<string> targetAbsolute = PathGuard.ResolveWithinRoot(_rootAbsolute, targetRelative);
            if (targetAbsolute.IsFailure)
            {
                return OperationResult.Fail<WorkspaceFileRef>(targetAbsolute.Failure);
            }

            try
            {
                File.Move(sourceAbsolute.Value, targetAbsolute.Value, overwrite: false);
                cancellationToken.ThrowIfCancellationRequested();
                return OperationResult.Ok(WorkspaceFileRef.Create(targetRelative, WorkspaceArea.Rejected));
            }
            catch (IOException) when (File.Exists(targetAbsolute.Value))
            {
                // Name taken; try the next suffix.
            }
        }

        return OperationResult.Fail<WorkspaceFileRef>(
            FailureCode.WorkspaceError, $"Could not move '{fileName}' to Rejected after 99 collision attempts.");
    }

    /// <inheritdoc />
    public OperationResult<WorkingCleanupResult> CleanupWorking(WorkspaceDirRef session, WorkingCleanupPlan plan)
    {
        int deleted = 0;
        try
        {
            OperationResult<Unit> verified = VerifyRetentionFiles(session, plan.Preserve);
            if (verified.IsFailure)
                return OperationResult.Fail<WorkingCleanupResult>(verified.Failure);

            HashSet<string> preserved = new(plan.Preserve.Select(f => f.File.RelativePath), StringComparer.OrdinalIgnoreCase);
            // Validate the entire destructive plan before deleting its first member.
            foreach (RetentionFile file in plan.Delete)
            {
                string absolute = RetentionPath(session, file.File, WorkspaceArea.Working);
                if (preserved.Contains(file.File.RelativePath) || IsTiff(absolute))
                    return OperationResult.Fail<WorkingCleanupResult>(FailureCode.WorkspaceError,
                        "Cleanup refused a preserved file or TIFF; production TIFF disposal requires the Recycle Bin.");
                if (File.Exists(absolute)) VerifyHash(absolute, file.Sha256);
            }

            foreach (RetentionFile file in plan.Delete.DistinctBy(f => f.File.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                // Re-prove containment and reparse ancestry immediately before each mutation.
                string absolute = RetentionPath(session, file.File, WorkspaceArea.Working);
                if (!File.Exists(absolute)) continue;
                VerifyHash(absolute, file.Sha256);
                absolute = RetentionPath(session, file.File, WorkspaceArea.Working);
                if ((File.GetAttributes(absolute) & FileAttributes.ReadOnly) != 0)
                {
                    // Manual-result imports are immutable/read-only. Their former copy is
                    // disposable only now, after promotion. Never change attributes through a
                    // hard link that might also name an external/customer file.
                    using (FileStream owned = new(absolute, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        if (!NativeMethods.GetFileInformationByHandle(owned.SafeFileHandle, out NativeMethods.FileInformation information) ||
                            information.NumberOfLinks != 1)
                            throw new IOException("Readonly retention copy has unverifiable or shared file identity.");
                    }
                    absolute = RetentionPath(session, file.File, WorkspaceArea.Working);
                    File.SetAttributes(absolute, File.GetAttributes(absolute) & ~FileAttributes.ReadOnly);
                }
                absolute = RetentionPath(session, file.File, WorkspaceArea.Working);
                File.Delete(absolute);
                deleted++;
            }
        }
        catch (Exception ex) when (IsRetentionFailure(ex))
        {
            return OperationResult.Fail<WorkingCleanupResult>(OperationFailure.Create(FailureCode.WorkspaceError,
                $"Session completed; cleanup pending after {deleted} deletions: {ex.Message}",
                context: new Dictionary<string, string>
                {
                    ["retentionDeletedCount"] = deleted.ToString(CultureInfo.InvariantCulture),
                }));
        }

        return OperationResult.Ok(new WorkingCleanupResult(deleted,
            plan.Preserve.Select(f => f.File.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count()));
    }

    public OperationResult<Unit> VerifyRetentionFiles(WorkspaceDirRef session, IReadOnlyList<RetentionFile> files)
    {
        try
        {
            foreach (RetentionFile file in files)
                VerifyHash(RetentionPath(session, file.File), file.Sha256);
            return OperationResult.Ok();
        }
        catch (Exception ex) when (IsRetentionFailure(ex))
        {
            return OperationResult.Fail<Unit>(FailureCode.WorkspaceError, $"Retention verification failed: {ex.Message}");
        }
    }

    public async Task<OperationResult<WorkspaceFileRef>> PromoteRevisionAsync(
        WorkspaceDirRef session, RevisionId revisionId, RetentionFile source, CancellationToken cancellationToken)
    {
        WorkspaceFileRef destination = WorkspaceFileRef.Create(
            $"{session.RelativePath}/Revisions/{revisionId}/{source.File.FileName}", WorkspaceArea.Revisions);
        string destinationDirectory = $"{session.RelativePath}/Revisions/{revisionId}";
        WorkspaceFileRef staging = WorkspaceFileRef.Create(destinationDirectory + "/.retention-copy.partial", WorkspaceArea.Revisions);
        try
        {
            string sourcePath = RetentionPath(session, source.File, WorkspaceArea.Working);
            string destinationPath = RetentionPath(session, destination, WorkspaceArea.Revisions);
            string stagingPath = RetentionPath(session, staging, WorkspaceArea.Revisions);
            VerifyHash(sourcePath, source.Sha256);
            if (File.Exists(destinationPath))
            {
                VerifyHash(destinationPath, source.Sha256);
                return OperationResult.Ok(destination);
            }

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destinationPath)!);
            sourcePath = RetentionPath(session, source.File, WorkspaceArea.Working);
            stagingPath = RetentionPath(session, staging, WorkspaceArea.Revisions);
            // Never truncate an existing file, including a partial copy or a manipulated hard
            // link. A crash leftover stays as evidence; retry uses a fresh staging name.
            if (File.Exists(stagingPath))
            {
                staging = WorkspaceFileRef.Create(
                    $"{destinationDirectory}/.retention-{Guid.NewGuid():N}.partial", WorkspaceArea.Revisions);
                stagingPath = RetentionPath(session, staging, WorkspaceArea.Revisions);
            }
            await using (FileStream input = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                1 << 20, useAsync: true))
            await using (FileStream output = new(stagingPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                1 << 20, useAsync: true))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }
            stagingPath = RetentionPath(session, staging, WorkspaceArea.Revisions);
            VerifyHash(stagingPath, source.Sha256);
            destinationPath = RetentionPath(session, destination, WorkspaceArea.Revisions);
            // Same destination directory/volume: only this last staging rename is a move.
            File.Move(stagingPath, destinationPath, overwrite: false);
            VerifyHash(RetentionPath(session, destination, WorkspaceArea.Revisions), source.Sha256);
            return OperationResult.Ok(destination);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return OperationResult.Fail<WorkspaceFileRef>(FailureCode.WorkspaceError,
                "Retention copy interrupted; original authority is unchanged and cleanup will retry.");
        }
        catch (Exception ex) when (IsRetentionFailure(ex))
        {
            return OperationResult.Fail<WorkspaceFileRef>(FailureCode.WorkspaceError,
                $"Retention promotion failed; original authority is unchanged: {ex.Message}");
        }
    }

    private string RetentionPath(WorkspaceDirRef session, WorkspaceFileRef file, WorkspaceArea? requiredArea = null)
    {
        string[] sessionParts = session.RelativePath.Split('/');
        if (sessionParts.Length != 2 || sessionParts[0] != SessionsFolder || !sessionParts[1].StartsWith("S_", StringComparison.Ordinal))
            throw new InvalidOperationException("Retention requires an exact managed session root.");
        if (requiredArea is { } required && file.Area != required)
            throw new InvalidOperationException($"Retention expected area {required}, got {file.Area}.");
        string areaPrefix = $"{session.RelativePath}/{AreaFolder(file.Area)}/";
        if (!file.RelativePath.StartsWith(areaPrefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Retention file does not belong to this session and declared area.");
        string root = ResolveAbsoluteDirectory(session);
        string absolute = ResolveAbsolute(file);
        if (!absolute.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Retention path escapes the exact session.");
        ReparsePointGuard.RefuseAncestry(absolute);
        return absolute;
    }

    private static void VerifyHash(string absolute, Sha256 expected)
    {
        using FileStream file = new(absolute, FileMode.Open, FileAccess.Read, FileShare.Read);
        string actual = Convert.ToHexString(SHA256.HashData(file));
        if (!string.Equals(actual, expected.Value, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"Retention hash mismatch for '{absolute}'. No deletion is authorised.");
    }

    private static bool IsTiff(string path)
    {
        if (System.IO.Path.GetExtension(path).Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
            System.IO.Path.GetExtension(path).Equals(".tiff", StringComparison.OrdinalIgnoreCase)) return true;
        if (!File.Exists(path)) return false;
        using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> header = stackalloc byte[4];
        if (file.Read(header) != 4) return false;
        // Refuse TIFF/BigTIFF bytes even if somebody renamed the file to a scratch extension.
        return (header[0] == 73 && header[1] == 73 && header[2] is 42 or 43 && header[3] == 0) ||
            (header[0] == 77 && header[1] == 77 && header[2] == 0 && header[3] is 42 or 43);
    }

    private static bool IsRetentionFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException;

    /// <inheritdoc />
    public OperationResult<IReadOnlyList<WorkingFileEntry>> ListWorkingFiles(WorkspaceDirRef session)
    {
        string workingRelative = $"{session.RelativePath}/{AreaFolder(WorkspaceArea.Working)}";

        OperationResult<string> workingAbsolute = PathGuard.ResolveWithinRoot(_rootAbsolute, workingRelative);
        if (workingAbsolute.IsFailure)
        {
            return OperationResult.Fail<IReadOnlyList<WorkingFileEntry>>(workingAbsolute.Failure);
        }

        if (!Directory.Exists(workingAbsolute.Value))
        {
            return OperationResult.Ok<IReadOnlyList<WorkingFileEntry>>([]);
        }

        List<WorkingFileEntry> entries = [];
        try
        {
            foreach (string absolute in EnumerateWithoutReparseTraversal(workingAbsolute.Value))
            {
                string relativeToWorking = System.IO.Path
                    .GetRelativePath(workingAbsolute.Value, absolute)
                    .Replace('\\', '/');

                // The first segment is the per-attempt folder CreateWorkingCopyAsync creates.
                // A file sitting directly in Working\ has none, and is reported rather than
                // attributed to anything.
                int separator = relativeToWorking.IndexOf('/');
                string attemptFolder = separator < 0 ? string.Empty : relativeToWorking[..separator];

                entries.Add(new WorkingFileEntry(
                    WorkspaceFileRef.Create($"{workingRelative}/{relativeToWorking}", WorkspaceArea.Working),
                    attemptFolder));
            }
        }
        catch (IOException ex)
        {
            return OperationResult.Fail<IReadOnlyList<WorkingFileEntry>>(
                FailureCode.WorkspaceError, $"Could not list working files: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return OperationResult.Fail<IReadOnlyList<WorkingFileEntry>>(
                FailureCode.WorkspaceError, $"Could not list working files: {ex.Message}");
        }

        return OperationResult.Ok<IReadOnlyList<WorkingFileEntry>>(entries);
    }

    private static IEnumerable<string> EnumerateWithoutReparseTraversal(string root)
    {
        Stack<string> directories = new();
        directories.Push(root);
        while (directories.TryPop(out string? directory))
        {
            ReparsePointGuard.RefuseAncestry(directory);
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                ReparsePointGuard.RefuseAncestry(entry);
                if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0) directories.Push(entry);
                else yield return entry;
            }
        }
    }

    /// <inheritdoc />
    public OperationResult<Unit> QuarantineWorkingFile(WorkspaceFileRef file, string reason)
    {
        if (file.Area != WorkspaceArea.Working)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.WorkspaceError,
                $"Only Working files may be quarantined through this route; '{file}' is {file.Area}.");
        }

        OperationResult<string> absolute = PathGuard.ResolveWithinRoot(_rootAbsolute, file.RelativePath);
        return absolute.IsFailure
            ? OperationResult.Fail<Unit>(absolute.Failure)
            : Quarantine(absolute.Value, reason);
    }

    /// <inheritdoc />
    public OperationResult<Unit> Quarantine(string absolutePath, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        string fullPath = System.IO.Path.GetFullPath(absolutePath);
        string rootWithSeparator = _rootAbsolute.EndsWith(System.IO.Path.DirectorySeparatorChar)
            ? _rootAbsolute
            : _rootAbsolute + System.IO.Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult.Fail<Unit>(
                FailureCode.WorkspaceError, $"Refusing to quarantine a path outside the workspace root: '{fullPath}'.");
        }

        if (!File.Exists(fullPath))
        {
            return OperationResult.Fail<Unit>(FailureCode.OutputMissing, $"Nothing to quarantine at '{fullPath}'.");
        }

        string quarantineDir = System.IO.Path.Combine(_rootAbsolute, "Quarantine");

        try
        {
            Directory.CreateDirectory(quarantineDir);

            string stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            string quarantinedName = $"{stamp}_{System.IO.Path.GetFileName(fullPath)}";
            string quarantinedPath = System.IO.Path.Combine(quarantineDir, quarantinedName);

            File.Move(fullPath, quarantinedPath, overwrite: false);
            File.WriteAllText(quarantinedPath + ".reason.txt", reason);
        }
        catch (IOException ex)
        {
            return OperationResult.Fail<Unit>(FailureCode.WorkspaceError, $"Could not quarantine orphan file: {ex.Message}");
        }

        return OperationResult.Ok();
    }

    /// <inheritdoc />
    public string ResolveAbsolute(WorkspaceFileRef reference)
    {
        OperationResult<string> result = PathGuard.ResolveWithinRoot(_rootAbsolute, reference.RelativePath);
        return result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException(
                $"Workspace file reference '{reference}' does not resolve within the workspace root: {result.Failure}.");
    }

    /// <inheritdoc />
    public string ResolveAbsoluteDirectory(WorkspaceDirRef reference)
    {
        OperationResult<string> result = PathGuard.ResolveWithinRoot(_rootAbsolute, reference.RelativePath);
        return result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException(
                $"Workspace directory reference '{reference}' does not resolve within the workspace root: {result.Failure}.");
    }

    private static readonly WorkspaceArea[] AllAreas =
    [
        WorkspaceArea.Source, WorkspaceArea.Working, WorkspaceArea.Approved,
        WorkspaceArea.Rejected, WorkspaceArea.Logs, WorkspaceArea.Revisions,
    ];

    private static string AreaFolder(WorkspaceArea area) => area switch
    {
        WorkspaceArea.Source => "Source",
        WorkspaceArea.Working => "Working",
        WorkspaceArea.Approved => "Approved",
        WorkspaceArea.Rejected => "Rejected",
        WorkspaceArea.Logs => "Logs",
        WorkspaceArea.Revisions => "Revisions",
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Unknown workspace area."),
    };

    /// <summary>
    /// Converts a cancelled in-flight source import into the structured failure the ordinary
    /// operator-facing surface already knows how to report, and disposes of the half-copied
    /// destination.
    /// </summary>
    /// <remarks>
    /// The partial destination is <b>quarantined, never deleted</b>. By the time this runs the
    /// copy's streams have already been disposed by the <c>await using</c> blocks unwinding, so
    /// the move is against a closed handle — and <see cref="Quarantine"/> reports a failure
    /// rather than falling back to a hard delete if some scanner still holds the file, which is
    /// this import boundary's rule that an uncertain source copy is quarantined, never hard-deleted.
    ///
    /// Quarantining is the right existing semantic rather than a new one: those bytes are
    /// precisely what <see cref="IWorkspace.Quarantine"/> exists for — a file on disk with no
    /// corresponding metadata. Nothing ever treats them as a source, because no
    /// <see cref="WorkspaceFileRef"/> to them is returned and the caller's Import attempt fails,
    /// so no Revision and no InputSnapshot is written; moving them out of <c>Source\</c> as well
    /// means a later reader of that directory cannot mistake a truncated file for the snapshot
    /// a successful import would have left under the very same name.
    /// </remarks>
    private OperationResult<WorkspaceFileRef> CancelledImport(string targetAbsolutePath, string sourceAbsolutePath)
    {
        string disposition;
        string? dispositionDetail = null;

        if (!File.Exists(targetAbsolutePath))
        {
            disposition = "none";
        }
        else
        {
            OperationResult<Unit> quarantined = Quarantine(
                targetAbsolutePath,
                $"Cancelled part-way through importing '{sourceAbsolutePath}'. These bytes are a " +
                "truncated copy, never a source snapshot.");

            disposition = quarantined.IsSuccess ? "quarantined" : "retained";
            dispositionDetail = quarantined.IsFailure ? quarantined.Failure.ToString() : null;
        }

        Dictionary<string, string> context = new()
        {
            ["sourceFileName"] = System.IO.Path.GetFileName(sourceAbsolutePath),
            ["partialDestination"] = disposition,
        };

        if (dispositionDetail is not null)
        {
            context["partialDestinationDetail"] = dispositionDetail;
        }

        return OperationResult.Fail<WorkspaceFileRef>(OperationFailure.Create(
            FailureCode.Cancelled,
            "The source import was cancelled before the copy completed. No source snapshot was " +
            "created, and the half-written destination was not kept as one.",
            isRetryable: true,
            context: context));
    }

    private static string InsertBeforeExtension(string fileName, string suffix)
    {
        int dot = fileName.LastIndexOf('.');
        return dot >= 0 ? fileName[..dot] + suffix + fileName[dot..] : fileName + suffix;
    }

    /// <summary>
    /// <c>S_&lt;UTC compact&gt;_&lt;last 8 hex chars of the session id&gt;</c> (plan §12.1).
    /// Carries no customer text; renaming the operator's output name never moves this directory.
    /// </summary>
    /// <remarks>
    /// Deliberately the <b>last</b> 8 hex characters, not the first: <see cref="SessionId"/> is
    /// a UUIDv7, whose leading bits are a millisecond timestamp rather than randomness. Two
    /// sessions created close together — well within the UTC-compact timestamp's one-second
    /// resolution — would share those leading characters and collide on the same directory
    /// name. The trailing bits of a UUIDv7 are the random tail, which is what a "short id"
    /// needs to actually be short <em>and</em> distinguishing.
    /// </remarks>
    internal static string BuildSessionDirectoryName(SessionId id, DateTimeOffset createdUtc)
    {
        string utcCompact = createdUtc.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        string hex = id.Value.ToString("N", CultureInfo.InvariantCulture);
        string shortId = hex[^8..];
        return $"S_{utcCompact}_{shortId}";
    }
}
