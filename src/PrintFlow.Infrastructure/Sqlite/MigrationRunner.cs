using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Sqlite;

/// <summary>
/// Applies forward-only, embedded SQL migrations, gated by <c>PRAGMA user_version</c>
/// (Epic 11100 Task 11108; plan §4.5, §18).
/// </summary>
/// <remarks>
/// Each script runs in its own transaction alongside its <c>SchemaMigration</c> audit row and
/// the <c>user_version</c> bump, so a script can never apply partially. A database whose
/// <c>user_version</c> is higher than the newest script this build knows about is refused
/// outright — never auto-repaired, never auto-downgraded.
/// </remarks>
public static class MigrationRunner
{
    private const string ResourcePrefix = "PrintFlow.Infrastructure.Sqlite.Migrations.";

    /// <summary>
    /// The newest schema version this build carries a script for.
    /// </summary>
    /// <remarks>
    /// Exposed so a caller — startup diagnostics, and the tests that assert the database really
    /// was migrated before anything read it — can state "the schema is current" rather than a
    /// literal that has to be edited every time a migration is added. A stale literal in a test
    /// is a test that stops meaning what it says.
    /// </remarks>
    public static long NewestKnownVersion { get; } = HighestVersionOf(LoadEmbeddedMigrations());

    public static OperationResult<Unit> Migrate(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        IReadOnlyList<Migration> migrations = LoadEmbeddedMigrations();
        long highestKnown = HighestVersionOf(migrations);

        long currentVersion = ReadUserVersion(connection);
        if (currentVersion > highestKnown)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.PersistenceError,
                $"This database was created by a newer PrintFlow (schema version {currentVersion}); " +
                $"this build only knows schema versions up to {highestKnown}. Refusing to open.");
        }

        // Foreign keys are suspended for the migration pass and restored before anything else
        // runs (Epic 11400 Part B1A.2D §20).
        //
        // SQLite cannot alter or drop a CHECK constraint, so widening one means the documented
        // twelve-step rebuild: create the table afresh, copy the rows, drop the original, rename.
        // With enforcement on, that DROP performs an implicit delete which fires every
        // ON DELETE CASCADE pointing at the table — it would take the session's steps, revisions,
        // attempts and outputs with it. The pragma is therefore not a convenience; a rebuild is
        // not correctly expressible without it.
        //
        // It is set here rather than inside a script because SQLite silently ignores
        // `PRAGMA foreign_keys` inside a transaction, and every script runs in one. Suspension
        // lasts exactly as long as the pass: the restore below runs on both the success and the
        // failure path, so no connection is handed back to the application with enforcement off,
        // and a rebuild that copies rows incorrectly still fails its own script's transaction.
        bool foreignKeysWereOn = ReadForeignKeys(connection);
        if (foreignKeysWereOn)
        {
            Execute(connection, transaction: null, "PRAGMA foreign_keys = OFF;");
        }

        try
        {
            return Apply(connection, migrations, currentVersion);
        }
        finally
        {
            if (foreignKeysWereOn)
            {
                Execute(connection, transaction: null, "PRAGMA foreign_keys = ON;");
            }
        }
    }

    private static OperationResult<Unit> Apply(
        SqliteConnection connection, IReadOnlyList<Migration> migrations, long currentVersion)
    {
        foreach (Migration migration in migrations)
        {
            if (migration.Version <= currentVersion)
            {
                continue;
            }

            using SqliteTransaction transaction = connection.BeginTransaction();
            try
            {
                Execute(connection, transaction, migration.Sql);

                using (SqliteCommand audit = connection.CreateCommand())
                {
                    audit.Transaction = transaction;
                    audit.CommandText =
                        "INSERT INTO SchemaMigration (Version, Name, AppliedAtUtc, ScriptSha256) " +
                        "VALUES ($version, $name, $appliedAtUtc, $sha256);";
                    audit.Parameters.AddWithValue("$version", migration.Version);
                    audit.Parameters.AddWithValue("$name", migration.Name);
                    audit.Parameters.AddWithValue(
                        "$appliedAtUtc", DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
                    audit.Parameters.AddWithValue("$sha256", migration.Sha256);
                    audit.ExecuteNonQuery();
                }

                // PRAGMA user_version is a transactional write in SQLite, so the version bump
                // commits atomically with the schema change and its audit row.
                Execute(connection, transaction, $"PRAGMA user_version = {migration.Version};");

                transaction.Commit();
            }
            catch (SqliteException ex)
            {
                transaction.Rollback();
                return OperationResult.Fail<Unit>(
                    FailureCode.PersistenceError,
                    $"Migration {migration.Version:0000}_{migration.Name} failed: {ex.Message}");
            }
        }

        return OperationResult.Ok();
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static bool ReadForeignKeys(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys;";
        object? result = command.ExecuteScalar();
        return result is not null && Convert.ToInt64(result, CultureInfo.InvariantCulture) != 0;
    }

    private static long ReadUserVersion(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        object? result = command.ExecuteScalar();
        return result is long value ? value : Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static long HighestVersionOf(IReadOnlyList<Migration> migrations) =>
        migrations.Count == 0 ? 0 : migrations[^1].Version;

    private readonly record struct Migration(int Version, string Name, string Sql, string Sha256);

    private static IReadOnlyList<Migration> LoadEmbeddedMigrations()
    {
        Assembly assembly = typeof(MigrationRunner).Assembly;
        List<Migration> migrations = [];

        foreach (string resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(ResourcePrefix, StringComparison.Ordinal) ||
                !resourceName.EndsWith(".sql", StringComparison.Ordinal))
            {
                continue;
            }

            string fileName = resourceName[ResourcePrefix.Length..];
            int underscore = fileName.IndexOf('_');
            if (underscore <= 0 || !int.TryParse(
                    fileName[..underscore], NumberStyles.None, CultureInfo.InvariantCulture, out int version))
            {
                throw new InvalidOperationException(
                    $"Migration resource '{resourceName}' does not follow the 'NNNN_name.sql' convention.");
            }

            string name = fileName[(underscore + 1)..^".sql".Length];

            using Stream stream = assembly.GetManifestResourceStream(resourceName)!;
            using MemoryStream buffer = new();
            stream.CopyTo(buffer);
            byte[] bytes = buffer.ToArray();

            string sql = Encoding.UTF8.GetString(bytes);
            string sha256 = Convert.ToHexString(SHA256.HashData(bytes));

            migrations.Add(new Migration(version, name, sql, sha256));
        }

        migrations.Sort((a, b) => a.Version.CompareTo(b.Version));
        return migrations;
    }
}
