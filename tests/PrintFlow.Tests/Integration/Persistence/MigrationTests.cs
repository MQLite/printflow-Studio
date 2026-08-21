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

    private static void AssertPragma(SqliteConnection connection, string pragma, string expected)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragma};";
        string actual = command.ExecuteScalar()!.ToString()!;
        actual.ShouldBe(expected, StringCompareShould.IgnoreCase);
    }
}
