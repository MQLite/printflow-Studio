using Dapper;
using Microsoft.Data.Sqlite;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Settings;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Sqlite;

/// <summary>
/// The real, Dapper-backed <see cref="ISettingsRepository"/> over the <c>Setting</c> table
/// migration <c>0001</c> already created (Jira 11108; MVP design §17.6).
/// </summary>
/// <remarks>
/// A second small repository rather than more surface on <see cref="SqliteSessionRepository"/>:
/// a setting has no session, no workflow meaning and no place in a session's transaction, and
/// putting it there would have made every settings write travel through a session aggregate it
/// has nothing to do with.
/// <para>
/// <see cref="UpsertAsync"/> opens one transaction for the whole batch, so a Settings screen
/// that changes three values changes three or none. Reads take no transaction: a single SELECT
/// is already atomic, and wrapping it would only hold a connection longer.
/// </para>
/// </remarks>
public sealed class SqliteSettingsRepository : ISettingsRepository
{
    private readonly SqliteConnectionFactory _connectionFactory;

    public SqliteSettingsRepository(SqliteConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<OperationResult<SettingEntry?>> ReadAsync(
        SettingKey key, CancellationToken cancellationToken)
    {
        try
        {
            using SqliteConnection connection = _connectionFactory.Open();
            string? value = await connection.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
                "SELECT Value FROM Setting WHERE Key = @key;",
                new { key = Mappers.ToText(key) }, cancellationToken: cancellationToken));

            // Null is the answer "the operator has never set this", which every caller must
            // resolve to the default behaviour it already has. It is deliberately not an error.
            return OperationResult.Ok(value is null ? null : new SettingEntry(key, value));
        }
        catch (SqliteException ex)
        {
            return OperationResult.Fail<SettingEntry?>(
                FailureCode.PersistenceError, $"Setting '{key}' could not be read: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<SettingEntry>>> ReadAllAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            using SqliteConnection connection = _connectionFactory.Open();
            IEnumerable<SettingRow> rows = await connection.QueryAsync<SettingRow>(new CommandDefinition(
                "SELECT Key, Value FROM Setting ORDER BY Key;", cancellationToken: cancellationToken));

            // A row this build's vocabulary does not contain is skipped rather than thrown on,
            // so a database written by a newer build stays readable by an older one.
            List<SettingEntry> entries = [];
            foreach (SettingRow row in rows)
            {
                if (Mappers.ToSettingKeyOrNull(row.Key) is { } key)
                {
                    entries.Add(new SettingEntry(key, row.Value));
                }
            }

            return OperationResult.Ok<IReadOnlyList<SettingEntry>>(entries);
        }
        catch (SqliteException ex)
        {
            return OperationResult.Fail<IReadOnlyList<SettingEntry>>(
                FailureCode.PersistenceError, $"The settings could not be read: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<Unit>> UpsertAsync(
        IReadOnlyList<SettingEntry> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
        {
            return OperationResult.Ok();
        }

        if (entries.Select(entry => entry.Key).Distinct().Count() != entries.Count)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.PreconditionNotMet,
                "One settings batch may not write the same key twice; the last write would silently win.");
        }

        using SqliteConnection connection = _connectionFactory.Open();
        using SqliteTransaction transaction = connection.BeginTransaction();

        try
        {
            foreach (SettingEntry entry in entries)
            {
                await connection.ExecuteAsync(
                    """
                    INSERT INTO Setting (Key, Value) VALUES (@Key, @Value)
                    ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
                    """,
                    new { Key = Mappers.ToText(entry.Key), entry.Value }, transaction);
            }

            transaction.Commit();
            return OperationResult.Ok();
        }
        catch (SqliteException ex)
        {
            transaction.Rollback();
            return OperationResult.Fail<Unit>(
                FailureCode.PersistenceError, $"The settings write failed and was rolled back: {ex.Message}");
        }
    }
}
