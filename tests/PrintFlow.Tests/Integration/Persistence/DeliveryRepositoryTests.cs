using Microsoft.Data.Sqlite;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Delivery;

namespace PrintFlow.Tests.Integration.Persistence;

[Collection(SqliteCollection.Name)]
public sealed class DeliveryRepositoryTests
{
    [Fact]
    public async Task Older_intent_committing_late_does_not_replace_newer_successful_preference()
    {
        using TempDatabase database = new();
        Seed(database);
        SqliteDeliveryRepository repository = new(database.Factory);
        DeliveryRecord older = Proposed("C:\\older", "old.png", Guid.NewGuid());
        DeliveryRecord newer = Proposed("C:\\newer", "new.png", Guid.NewGuid());
        DeliveryAttemptRecord oldAttempt = Attempt(older);
        DeliveryAttemptRecord newAttempt = Attempt(newer);
        (await repository.CreateOrCoalesceAsync(older, oldAttempt, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await repository.CreateOrCoalesceAsync(newer, newAttempt, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        await CommitReadyAsync(repository, newAttempt);
        await CommitReadyAsync(repository, oldAttempt);
        using SqliteConnection connection = database.Factory.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Setting WHERE Key='LAST_SUCCESSFUL_DELIVERY_DESTINATION';";
        using var json = System.Text.Json.JsonDocument.Parse((string)command.ExecuteScalar()!);
        json.RootElement.GetProperty("DisplayFolder").GetString().ShouldBe("C:\\newer");
    }

    [Fact]
    public async Task Invalid_preference_rolls_back_both_delivered_and_attempt_transition()
    {
        using TempDatabase database = new();
        Seed(database);
        SqliteDeliveryRepository repository = new(database.Factory);
        DeliveryRecord proposed = Proposed("C:\\rollback", "rollback.png", Guid.NewGuid());
        DeliveryAttemptRecord attempt = Attempt(proposed);
        (await repository.CreateOrCoalesceAsync(proposed, attempt, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        var staged = await repository.MarkStagingAsync(attempt.AttemptId, "file-rollback",
            DateTimeOffset.UtcNow, CancellationToken.None);
        staged.IsSuccess.ShouldBeTrue();
        (await repository.MarkReadyAsync(attempt.AttemptId, DateTimeOffset.UtcNow,
            CancellationToken.None)).IsSuccess.ShouldBeTrue();
        using (SqliteConnection connection = database.Factory.Open())
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO Setting(Key,Value) VALUES('LAST_SUCCESSFUL_DELIVERY_DESTINATION','not-json');";
            command.ExecuteNonQuery();
        }
        (await repository.MarkDeliveredAsync(proposed.DeliveryId, attempt.AttemptId,
            "file-rollback", staged.Value.StagingCreationUtc!.Value, DateTimeOffset.UtcNow,
            CancellationToken.None)).IsFailure.ShouldBeTrue();
        var read = await repository.FindByIdAsync(proposed.DeliveryId, CancellationToken.None);
        read.Value!.Delivery.Status.ShouldBe("Pending");
        read.Value.Attempt!.State.ShouldBe(DeliveryAttemptState.ReadyToPublish);
    }

    private static async Task CommitReadyAsync(SqliteDeliveryRepository repository, DeliveryAttemptRecord attempt)
    {
        var stage = await repository.MarkStagingAsync(attempt.AttemptId, attempt.AttemptId.ToString("N"),
            DateTimeOffset.UtcNow, CancellationToken.None);
        stage.IsSuccess.ShouldBeTrue();
        (await repository.MarkReadyAsync(attempt.AttemptId, DateTimeOffset.UtcNow, CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        (await repository.MarkDeliveredAsync(attempt.DeliveryId, attempt.AttemptId,
            attempt.AttemptId.ToString("N"), stage.Value.StagingCreationUtc!.Value,
            DateTimeOffset.UtcNow, CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Same_destination_coalesces_and_delivery_preference_commits_with_verified_evidence()
    {
        using TempDatabase database = new();
        Seed(database);
        SqliteDeliveryRepository repository = new(database.Factory);
        DeliveryRecord first = Proposed("C:\\jobs", "art.png", Guid.NewGuid());
        DeliveryAttemptRecord attempt = Attempt(first);
        var created = await repository.CreateOrCoalesceAsync(first, attempt, CancellationToken.None);
        created.IsSuccess.ShouldBeTrue();

        DeliveryRecord second = Proposed("c:\\JOBS", "ART.PNG", Guid.NewGuid());
        var coalesced = await repository.CreateOrCoalesceAsync(second, Attempt(second), CancellationToken.None);
        coalesced.IsSuccess.ShouldBeTrue();
        coalesced.Value.Delivery.DeliveryId.ShouldBe(first.DeliveryId);
        var reopened = new SqliteDeliveryRepository(database.Factory);
        (await reopened.FindByRequestAsync(second.RequestId, CancellationToken.None)).Value!
            .Delivery.DeliveryId.ShouldBe(first.DeliveryId);
        (await reopened.FindByRequestAsync(second.RequestId, CancellationToken.None)).Value!
            .RequestBinding!.RequestedFolder.ShouldBe("c:\\JOBS");
        DeliveryRecord differentDestination = Proposed("C:\\other", "other.png", second.RequestId);
        (await reopened.CreateOrCoalesceAsync(differentDestination,
            Attempt(differentDestination), CancellationToken.None))
            .IsFailure.ShouldBeTrue();
        DeliveryRecord differentSource = second with { ApprovedSha256 = Sha256.Parse(new string('B', 64)) };
        (await reopened.CreateOrCoalesceAsync(differentSource, Attempt(differentSource), CancellationToken.None))
            .IsFailure.ShouldBeTrue();

        var staged = await repository.MarkStagingAsync(attempt.AttemptId, "file-1", DateTimeOffset.UtcNow, CancellationToken.None);
        staged.IsSuccess.ShouldBeTrue();
        (await repository.MarkReadyAsync(attempt.AttemptId, DateTimeOffset.UtcNow, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        var delivered = await repository.MarkDeliveredAsync(first.DeliveryId, attempt.AttemptId,
            "file-1", staged.Value.StagingCreationUtc!.Value, DateTimeOffset.UtcNow, CancellationToken.None);
        delivered.IsSuccess.ShouldBeTrue();
        delivered.Value.Delivery.Status.ShouldBe("Delivered");
        using SqliteConnection connection = database.Factory.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Setting WHERE Key = 'LAST_SUCCESSFUL_DELIVERY_DESTINATION'";
        string? preference = command.ExecuteScalar() as string;
        preference.ShouldNotBeNull();
        using var preferenceJson = System.Text.Json.JsonDocument.Parse(preference);
        preferenceJson.RootElement.GetProperty("DisplayFolder").GetString().ShouldBe("C:\\jobs");
        using SqliteCommand overwrite = connection.CreateCommand();
        overwrite.CommandText = $"UPDATE ArtifactDelivery SET ApprovedSha256='{new string('B', 64)}' " +
            $"WHERE DeliveryId='{first.DeliveryId:D}';";
        Should.Throw<SqliteException>(() => overwrite.ExecuteNonQuery());
        using SqliteCommand remove = connection.CreateCommand();
        remove.CommandText = $"DELETE FROM ArtifactDelivery WHERE DeliveryId='{first.DeliveryId:D}';";
        Should.Throw<SqliteException>(() => remove.ExecuteNonQuery());
    }

    private static DeliveryRecord Proposed(string folder, string leaf, Guid requestId)
    {
        SessionId sid = SessionId.From(new Guid("00000000-0000-0000-0000-000000000001"));
        Guid revisionId = new("00000000-0000-0000-0000-000000000002");
        return new DeliveryRecord(Guid.NewGuid(), requestId, 0,
            new ArtifactKey(sid, ArtifactKind.ApprovedAssetPng, revisionId),
            ReviewId.From(new Guid("00000000-0000-0000-0000-000000000003")),
            "Revision", revisionId, RevisionId.From(revisionId),
            Sha256.Parse(new string('A', 64)), 12,
            folder, leaf, folder, folder + "\\" + leaf, "volume", "directory",
            "volume|directory|" + leaf.ToUpperInvariant(), null, "Pending", DateTimeOffset.UtcNow);
    }

    private static DeliveryAttemptRecord Attempt(DeliveryRecord delivery) =>
        new(Guid.NewGuid(), delivery.DeliveryId, 1, delivery.DestinationKey,
            DeliveryAttemptState.Intent, ".printflow-" + Guid.NewGuid().ToString("N") + ".partial",
            delivery.DirectoryId, null, null, delivery.ApprovedSha256, delivery.ApprovedLength,
            null, null, DateTimeOffset.UtcNow, null);

    private static void Seed(TempDatabase database)
    {
        using SqliteConnection connection = database.Factory.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO ProcessingSession
                (Id,WorkflowType,OutputName,CurrentStep,State,WorkspacePath,CreatedAtUtc,UpdatedAtUtc)
            VALUES ('00000000-0000-0000-0000-000000000001','PREPARE_ASSET','art','ApprovedPngExport',
                    'ACTIVE','Sessions/test','2026-01-01T00:00:00Z','2026-01-01T00:00:00Z');
            INSERT INTO Revision
                (Id,SessionId,Operation,RelativePath,Format,ByteLength,Sha256,ColourMode,CreatedAtUtc)
            VALUES ('00000000-0000-0000-0000-000000000002','00000000-0000-0000-0000-000000000001',
                    'PROMOTE_APPROVED','Sessions/test/Approved/art.png','PNG',12,'{new string('A', 64)}',
                    'RGB','2026-01-01T00:00:00Z');
            INSERT INTO ReviewDecision
                (Id,SessionId,StepKind,SubjectKind,SubjectId,ReviewedSha256,Operator,DecidedAtUtc,Decision)
            VALUES ('00000000-0000-0000-0000-000000000003','00000000-0000-0000-0000-000000000001',
                    'Trim','REVISION','00000000-0000-0000-0000-000000000002',
                    '{new string('A', 64)}','test','2026-01-01T00:00:00Z','APPROVED');
            """;
        command.ExecuteNonQuery();
    }
}
