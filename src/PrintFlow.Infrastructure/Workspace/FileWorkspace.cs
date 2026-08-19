using System.Globalization;
using System.IO;
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
            string candidateName = OutputFileNaming.BuildCollisionCandidate(proposedFileName, patterns, sequence);
            string relative = $"{session.RelativePath}/{AreaFolder(area)}/{candidateName}";

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
    public OperationResult<Unit> CleanupWorking(WorkspaceDirRef session)
    {
        string workingRelative = $"{session.RelativePath}/{AreaFolder(WorkspaceArea.Working)}";
        OperationResult<string> workingAbsolute = PathGuard.ResolveWithinRoot(_rootAbsolute, workingRelative);
        if (workingAbsolute.IsFailure)
        {
            return OperationResult.Fail<Unit>(workingAbsolute.Failure);
        }

        try
        {
            if (Directory.Exists(workingAbsolute.Value))
            {
                Directory.Delete(workingAbsolute.Value, recursive: true);
            }

            Directory.CreateDirectory(workingAbsolute.Value);
        }
        catch (IOException ex)
        {
            return OperationResult.Fail<Unit>(FailureCode.WorkspaceError, $"Could not clean up working copies: {ex.Message}");
        }

        return OperationResult.Ok();
    }

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
            foreach (string absolute in Directory.EnumerateFiles(
                workingAbsolute.Value, "*", SearchOption.AllDirectories))
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
        WorkspaceArea.Rejected, WorkspaceArea.Logs,
    ];

    private static string AreaFolder(WorkspaceArea area) => area switch
    {
        WorkspaceArea.Source => "Source",
        WorkspaceArea.Working => "Working",
        WorkspaceArea.Approved => "Approved",
        WorkspaceArea.Rejected => "Rejected",
        WorkspaceArea.Logs => "Logs",
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "Unknown workspace area."),
    };

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
