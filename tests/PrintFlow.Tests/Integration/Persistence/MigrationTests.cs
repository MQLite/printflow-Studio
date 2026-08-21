using System.IO;
using Microsoft.Data.Sqlite;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// Jira 11108: forward-only, <c>PRAGMA user_version</c>-gated migrations (task §27, §48).
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class MigrationTests
{
    [Fact]
    public void Empty_database_migrates_successfully()
    {
        using TempDatabase database = new();

        using SqliteConnection connection = database.Factory.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";

        // Against the newest script this build carries rather than a literal, so adding a
        // migration cannot leave this passing while meaning something weaker.
        Convert.ToInt64(command.ExecuteScalar()).ShouldBe(MigrationRunner.NewestKnownVersion);

        using SqliteCommand tables = connection.CreateCommand();
        tables.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = 'Revision';";
        tables.ExecuteScalar().ShouldNotBeNull();
    }

    [Fact]
    public void Repeated_migration_is_a_no_op()
    {
        using TempDatabase database = new();

        using SqliteConnection connection = database.Factory.Open();
        var second = MigrationRunner.Migrate(connection);

        second.IsSuccess.ShouldBeTrue();

        // One audit row per script, and re-running adds none: the second Migrate call above
        // found every version already applied and did nothing.
        using SqliteCommand count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM SchemaMigration;";
        Convert.ToInt64(count.ExecuteScalar()).ShouldBe(MigrationRunner.NewestKnownVersion);
    }

    [Fact]
    public void A_database_user_version_ahead_of_this_build_fails_closed()
    {
        using TempDatabase database = new();

        using (SqliteConnection connection = database.Factory.Open())
        using (SqliteCommand bump = connection.CreateCommand())
        {
            bump.CommandText = "PRAGMA user_version = 999;";
            bump.ExecuteNonQuery();
        }

        using SqliteConnection reopened = database.Factory.Open();
        var result = MigrationRunner.Migrate(reopened);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(PrintFlow.Domain.Results.FailureCode.PersistenceError);
    }

    [Fact]
    public void Required_pragmas_are_applied_to_every_connection()
    {
        using TempDatabase database = new();
        using SqliteConnection connection = database.Factory.Open();

        AssertPragma(connection, "journal_mode", "wal");
        AssertPragma(connection, "synchronous", "2"); // FULL
        AssertPragma(connection, "foreign_keys", "1");
    }

    /// <summary>
    /// A database written before 0002 gains the trim parameter columns and keeps its rows
    /// (Epic 11200 release gate §18).
    /// </summary>
    /// <remarks>
    /// The upgrade path no other test covered. <c>Empty_database_migrates_successfully</c> only
    /// ever exercises a database that had every script applied in one pass, so it would still
    /// pass if 0002 were unrunnable against an existing schema — an <c>ALTER TABLE</c> that only
    /// ever runs against a table created moments earlier is not evidence that it runs against
    /// the operator's real database.
    /// <para>
    /// The pre-0002 state is built from the 0001 script itself rather than a hand-copied
    /// <c>CREATE TABLE</c>, so the starting point cannot drift away from what shipped. The
    /// session row written before the upgrade is the point of the test: it must survive, and its
    /// new columns must read NULL — "this row recorded no margin", never "the default was used"
    /// (migration 0002).
    /// </para>
    /// </remarks>
    [Fact]
    public void A_pre_0002_database_upgrades_and_keeps_its_rows()
    {
        using TempDatabase database = new(migrate: false);

        using (SqliteConnection seeded = database.OpenRaw())
        {
            Execute(seeded, ReadMigrationScript("0001_initial_schema.sql"));
            Execute(
                seeded,
                "INSERT INTO SchemaMigration (Version, Name, AppliedAtUtc, ScriptSha256) " +
                "VALUES (1, 'initial_schema', '2026-01-01T00:00:00.000Z', 'SEED');");
            Execute(seeded, "PRAGMA user_version = 1;");

            // A session that existed before the trim columns did.
            Execute(
                seeded,
                """
                INSERT INTO ProcessingSession
                    (Id, WorkflowType, OutputName, CurrentStep, State, WorkspacePath,
                     CreatedAtUtc, UpdatedAtUtc)
                VALUES
                    ('legacy-session', 'PREPARE_ASSET', 'legacy', 'Trim', 'ACTIVE', 'Sessions/legacy',
                     '2026-01-01T00:00:00.000Z', '2026-01-01T00:00:00.000Z');
                """);
        }

        using SqliteConnection upgraded = database.OpenRaw();
        var result = MigrationRunner.Migrate(upgraded);

        result.IsSuccess.ShouldBeTrue();
        ReadUserVersion(upgraded).ShouldBe(MigrationRunner.NewestKnownVersion);

        foreach (string table in new[] { "ProcessingSession", "ProcessingAttempt" })
        {
            IReadOnlyList<string> columns = ColumnsOf(upgraded, table);
            foreach (string column in new[]
                     {
                         "TrimMode", "TrimMarginTop", "TrimMarginRight",
                         "TrimMarginBottom", "TrimMarginLeft",
                     })
            {
                columns.ShouldContain(column, $"{table} is missing {column} after the upgrade.");
            }
        }

        // The row is still there, and says nothing about a margin nobody recorded.
        using SqliteCommand row = upgraded.CreateCommand();
        row.CommandText =
            "SELECT TrimMode, TrimMarginTop FROM ProcessingSession WHERE Id = 'legacy-session';";
        using SqliteDataReader reader = row.ExecuteReader();
        reader.Read().ShouldBeTrue();
        reader.IsDBNull(0).ShouldBeTrue();
        reader.IsDBNull(1).ShouldBeTrue();
    }

    /// <summary>
    /// Reads a migration back off the shipped assembly, so a test's "before" state is the script
    /// that actually shipped rather than a copy that can quietly fall behind it.
    /// </summary>
    private static string ReadMigrationScript(string fileName)
    {
        using Stream stream = typeof(MigrationRunner).Assembly.GetManifestResourceStream(
                "PrintFlow.Infrastructure.Sqlite.Migrations." + fileName)
            ?? throw new InvalidOperationException($"Migration resource '{fileName}' is not embedded.");

        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long ReadUserVersion(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static IReadOnlyList<string> ColumnsOf(SqliteConnection connection, string table)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\");";

        List<string> names = [];
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(1));
        }

        return names;
    }

    private static void AssertPragma(SqliteConnection connection, string pragma, string expected)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragma};";
        string actual = command.ExecuteScalar()!.ToString()!;
        actual.ShouldBe(expected, StringCompareShould.IgnoreCase);
    }
}
