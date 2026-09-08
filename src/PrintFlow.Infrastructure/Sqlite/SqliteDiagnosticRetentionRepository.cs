using Dapper;
using System.IO;
using Microsoft.Data.Sqlite;
using PrintFlow.Domain.Automation;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Sqlite;

/// <summary>SQLite's bounded, retention-only view of diagnostic records and file authorities.</summary>
public sealed class SqliteDiagnosticRetentionRepository : IDiagnosticRetentionRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;
    private readonly string _workspaceRoot;

    public SqliteDiagnosticRetentionRepository(
        SqliteConnectionFactory connectionFactory,
        string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _connectionFactory = connectionFactory;
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
    }

    public async Task<OperationResult<DiagnosticRetentionBatch>> ReadExpiredBatchAsync(
        DateTimeOffset cutoffUtc,
        DiagnosticRetentionCursor? after,
        int maximumCount,
        CancellationToken cancellationToken)
    {
        if (maximumCount < 1)
            return OperationResult.Fail<DiagnosticRetentionBatch>(
                FailureCode.PreconditionNotMet, "Diagnostic retention requires a positive batch size.");

        try
        {
            using SqliteConnection connection = _connectionFactory.Open();
            IEnumerable<AutomationLogRow> rows = await connection.QueryAsync<AutomationLogRow>(new CommandDefinition(
                """
                SELECT * FROM AutomationLogEntry
                WHERE AtUtc < @cutoff
                  AND (@afterAt IS NULL OR AtUtc > @afterAt OR (AtUtc = @afterAt AND Id > @afterId))
                ORDER BY AtUtc, Id
                LIMIT @maximumCount;
                """,
                new
                {
                    cutoff = Mappers.ToText(cutoffUtc),
                    afterAt = after is null ? null : Mappers.ToText(after.AtUtc),
                    afterId = after?.Id.ToString(),
                    maximumCount,
                }, cancellationToken: cancellationToken));
            List<AutomationLogEntry> entries = rows.Select(Mappers.ToDomain).ToList();
            DiagnosticRetentionCursor? next = entries.Count == 0
                ? null
                : new(entries[^1].AtUtc, entries[^1].Id);
            return OperationResult.Ok(new DiagnosticRetentionBatch(entries, next));
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            return OperationResult.Fail<DiagnosticRetentionBatch>(
                FailureCode.PersistenceError, $"Diagnostic retention candidates could not be read: {ex.Message}");
        }
    }

    public async Task<OperationResult<IReadOnlyList<DiagnosticFileReference>>> ReadFileReferencesAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
            return OperationResult.Ok<IReadOnlyList<DiagnosticFileReference>>([]);

        try
        {
            string[] keys = paths.Select(TryGetPathIdentityKey)
                .Where(path => path is not null)
                .Select(path => path!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (keys.Length == 0)
                return OperationResult.Ok<IReadOnlyList<DiagnosticFileReference>>([]);

            using SqliteConnection connection = _connectionFactory.Open();
            RegisterPathFunctions(connection);
            IEnumerable<AutomationLogRow> logs = await connection.QueryAsync<AutomationLogRow>(new CommandDefinition(
                "SELECT * FROM AutomationLogEntry " +
                "WHERE printflow_path_key(ScreenshotPath) IN @keys;",
                new { keys }, cancellationToken: cancellationToken));
            IEnumerable<AttemptRow> attempts = await connection.QueryAsync<AttemptRow>(new CommandDefinition(
                """
                SELECT * FROM ProcessingAttempt
                WHERE FailureDetailJson IS NOT NULL
                  AND printflow_path_key(
                      json_extract(FailureDetailJson, '$.Context.evidencePath')) IN @keys;
                """,
                new { keys }, cancellationToken: cancellationToken));

            List<DiagnosticFileReference> references = logs.Select(Mappers.ToDomain)
                .Where(entry => entry.ScreenshotPath is not null)
                .Select(entry => new DiagnosticFileReference(
                    entry.ScreenshotPath!, entry.AtUtc, entry.SessionId,
                    FailureEvidence.AttemptIdOf(entry.Failure), entry.Step))
                .ToList();
            references.AddRange(attempts.Select(Mappers.ToDomain)
                .Where(attempt => attempt.Failure?.Context.TryGetValue(
                    AutomationLogEntry.ScreenshotContextKey, out string? path) == true &&
                    !string.IsNullOrWhiteSpace(path))
                .Select(attempt => new DiagnosticFileReference(
                    attempt.Failure!.Context[AutomationLogEntry.ScreenshotContextKey],
                    attempt.EndedAtUtc ?? attempt.StartedAtUtc,
                    attempt.SessionId,
                    attempt.Id,
                    attempt.Step)));
            return OperationResult.Ok<IReadOnlyList<DiagnosticFileReference>>(references);
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            return OperationResult.Fail<IReadOnlyList<DiagnosticFileReference>>(
                FailureCode.PersistenceError, $"Diagnostic file references could not be read: {ex.Message}");
        }
    }

    public async Task<OperationResult<IReadOnlyList<string>>> FindAuthoritativePathsAsync(
        IReadOnlyList<string> paths,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
            return OperationResult.Ok<IReadOnlyList<string>>([]);

        try
        {
            // A malformed legacy diagnostic path is unknown, not a reason to stop expiry of
            // unrelated, positively owned candidates in the same batch. The pure plan keeps its
            // row; this authority lookup considers only paths that can be canonicalised.
            string[] absolute = paths.Select(TryGetFullPath)
                .Where(path => path is not null)
                .Select(path => path!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string[] keys = absolute.Select(PathIdentityKey)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            using SqliteConnection connection = _connectionFactory.Open();
            RegisterPathFunctions(connection);

            long unknownLegacyManualSources = await connection.QuerySingleAsync<long>(new CommandDefinition(
                """
                SELECT COUNT(*) FROM ProcessingAttempt
                WHERE Operation = 'MANUAL_RESULT_IMPORT' AND ManualResultSourcePath IS NULL;
                """,
                cancellationToken: cancellationToken));
            if (unknownLegacyManualSources != 0)
            {
                // Builds before migration 0016 recorded that a manual import occurred but not
                // which external file it copied. Any candidate capture could therefore be that
                // user's source. This uncertainty preserves bytes globally; old diagnostic rows
                // may still expire, and new imports carry exact path authority.
                return OperationResult.Ok<IReadOnlyList<string>>(absolute);
            }

            IEnumerable<string> sources = await connection.QueryAsync<string>(new CommandDefinition(
                """
                SELECT OriginalSourcePath FROM InputSnapshot
                    WHERE printflow_path_key(OriginalSourcePath) IN @keys
                UNION ALL
                SELECT ManualResultSourcePath FROM ProcessingAttempt
                    WHERE ManualResultSourcePath IS NOT NULL
                      AND printflow_path_key(ManualResultSourcePath) IN @keys;
                """,
                new { keys }, cancellationToken: cancellationToken));
            List<string> found = sources.Select(TryGetFullPath)
                .Where(path => path is not null)
                .Select(path => path!)
                .ToList();

            IEnumerable<string> managed = await connection.QueryAsync<string>(new CommandDefinition(
                """
                SELECT RelativePath FROM Revision
                    WHERE printflow_workspace_path_key(RelativePath) IN @keys
                UNION SELECT FormerWorkingPath FROM Revision
                    WHERE FormerWorkingPath IS NOT NULL
                      AND printflow_workspace_path_key(FormerWorkingPath) IN @keys
                UNION SELECT RelativePath FROM PrintOutput
                    WHERE printflow_workspace_path_key(RelativePath) IN @keys
                UNION SELECT PromotionReservedPath FROM PrintOutput
                    WHERE PromotionReservedPath IS NOT NULL
                      AND printflow_workspace_path_key(PromotionReservedPath) IN @keys;
                """,
                new { keys }, cancellationToken: cancellationToken));
            found.AddRange(managed.Select(TryGetWorkspaceFullPath)
                .Where(path => path is not null)
                .Select(path => path!));

            return OperationResult.Ok<IReadOnlyList<string>>(
                found.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        }
        catch (Exception ex) when (IsReadFailure(ex) || ex is ArgumentException or NotSupportedException)
        {
            return OperationResult.Fail<IReadOnlyList<string>>(
                FailureCode.PersistenceError, $"Product file authorities could not be checked: {ex.Message}");
        }
    }

    public async Task<OperationResult<Unit>> ExpireAsync(
        IReadOnlyList<AutomationLogId> ids,
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        string[] values = ids.Select(id => id.ToString()).Distinct(StringComparer.Ordinal).ToArray();
        if (values.Length == 0) return OperationResult.Ok();

        using SqliteConnection connection = _connectionFactory.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();
        try
        {
            int changed = await connection.ExecuteAsync(new CommandDefinition(
                "DELETE FROM AutomationLogEntry WHERE Id IN @values AND AtUtc < @cutoff;",
                new { values, cutoff = Mappers.ToText(cutoffUtc) }, transaction,
                cancellationToken: cancellationToken));
            if (changed != values.Length)
            {
                transaction.Rollback();
                return OperationResult.Fail<Unit>(
                    FailureCode.PersistenceError,
                    "Diagnostic rows changed while retention was applying; the row batch was rolled back.");
            }
            transaction.Commit();
            return OperationResult.Ok();
        }
        catch (SqliteException ex)
        {
            transaction.Rollback();
            return OperationResult.Fail<Unit>(
                FailureCode.PersistenceError, $"Diagnostic row expiry was rolled back: {ex.Message}");
        }
    }

    /// <summary>
    /// SQLite compares the canonical identity returned by these local, deterministic functions
    /// rather than raw persisted spelling. This preserves legacy paths containing alternate
    /// separators or dot segments without broadening any path into deletion authority.
    /// </summary>
    private void RegisterPathFunctions(SqliteConnection connection)
    {
        connection.CreateFunction<string?, string?>(
            "printflow_path_key",
            path => path is null ? null : TryGetPathIdentityKey(path),
            isDeterministic: true);
        connection.CreateFunction<string?, string?>(
            "printflow_workspace_path_key",
            path => path is null ? null : TryGetWorkspacePathIdentityKey(path),
            isDeterministic: true);
    }

    private string? TryGetWorkspaceFullPath(string path) =>
        TryGetFullPath(Path.Combine(_workspaceRoot, path.Replace('/', Path.DirectorySeparatorChar)));

    private string? TryGetWorkspacePathIdentityKey(string path) =>
        TryGetWorkspaceFullPath(path) is { } full ? PathIdentityKey(full) : null;

    private static string? TryGetPathIdentityKey(string path) =>
        TryGetFullPath(path) is { } full ? PathIdentityKey(full) : null;

    private static string PathIdentityKey(string fullPath) => fullPath.ToUpperInvariant();

    private static string? TryGetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsReadFailure(Exception ex) =>
        ex is SqliteException or InvalidOperationException or FormatException or System.Text.Json.JsonException;
}
