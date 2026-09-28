using System.IO;
using Microsoft.Data.Sqlite;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Persistence;

[Collection(SqliteCollection.Name)]
public sealed class DeliverySchemaTests
{
    [Fact]
    public void Synthetic_version_17_database_upgrades_additively_and_keeps_existing_session()
    {
        using TempDatabase database = new(migrate: false);
        using SqliteConnection connection = database.OpenRaw();
        Execute(connection, "PRAGMA foreign_keys=OFF;");
        var assembly = typeof(MigrationRunner).Assembly;
        string[] scripts = assembly.GetManifestResourceNames()
            .Where(name => name.Contains(".Sqlite.Migrations.", StringComparison.Ordinal) &&
                           int.TryParse(name.Split(".Migrations.")[1].Split('_')[0], out int version) && version <= 17)
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();
        scripts.Length.ShouldBe(17);
        foreach (string name in scripts)
        {
            using Stream stream = assembly.GetManifestResourceStream(name)!;
            using StreamReader reader = new(stream);
            Execute(connection, reader.ReadToEnd());
        }
        Execute(connection, "PRAGMA foreign_keys=ON; PRAGMA user_version=17;");
        InsertSession(connection);
        MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();
        // SCRUM-11148 adds migration 0019 after the delivery schema; both remain additive.
        Scalar(connection, "PRAGMA user_version").ShouldBe(19L);
        Scalar(connection, "SELECT COUNT(*) FROM ProcessingSession WHERE Id='s'").ShouldBe(1L);
        Scalar(connection, "SELECT COUNT(*) FROM ArtifactDelivery").ShouldBe(0L);
        Scalar(connection, "SELECT COUNT(*) FROM CorrectionRequest").ShouldBe(0L);
    }

    [Fact]
    public void Migration_creates_delivery_journal_without_preference_or_backfill()
    {
        using TempDatabase database = new();
        using SqliteConnection connection = database.Factory.Open();
        Scalar(connection, "SELECT count(*) FROM ArtifactDelivery").ShouldBe(0L);
        Scalar(connection, "SELECT count(*) FROM DeliveryAttempt").ShouldBe(0L);
        Scalar(connection, "SELECT count(*) FROM Setting WHERE Key = 'LAST_SUCCESSFUL_DELIVERY_DESTINATION'").ShouldBe(0L);
    }

    [Fact]
    public void Delivery_discriminator_and_required_verified_evidence_are_enforced()
    {
        using TempDatabase database = new();
        using SqliteConnection connection = database.Factory.Open();
        InsertSession(connection);
        const string insert = """
            INSERT INTO ArtifactDelivery
              (DeliveryId, RequestId, SessionId, Kind, RevisionId, PrintOutputId, ReviewId,
               ApprovedSha256, ApprovedLength, RequestedFolder, RequestedFileName,
               ResolvedFolder, FinalPath, VolumeId, DirectoryId, DestinationKey,
               Status, CreatedAtUtc)
            VALUES
              ('d', 'r', 's', 'ApprovedAssetPng', NULL, NULL, NULL,
               'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA', 1,
               'C:\\out', 'x.png', 'C:\\out', 'C:\\out\\x.png', 'v', 'i', 'k',
               'Pending', '2026-01-01T00:00:00Z');
            """;
        Should.Throw<SqliteException>(() => Execute(connection, insert));
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void InsertSession(SqliteConnection connection) => Execute(connection, """
        INSERT INTO ProcessingSession
          (Id, WorkflowType, OutputName, CurrentStep, State, WorkspacePath, CreatedAtUtc, UpdatedAtUtc)
        VALUES ('s', 'PREPARE_ASSET', 'x', 'Import', 'ACTIVE', 'Sessions/s',
                '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z');
        """);
}
