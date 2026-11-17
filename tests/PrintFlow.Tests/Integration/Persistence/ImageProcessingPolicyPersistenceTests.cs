using System.IO;
using Microsoft.Data.Sqlite;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Domain.Trimming;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

[Collection(SqliteCollection.Name)]
public sealed class ImageProcessingPolicyPersistenceTests
{
    private const string DefaultsKey = "ImageProcessingDefaults";
    private const string ExactDefaults = """
        {"Version":1,"Selection":{"Enhancement":"MeituEnhancement","BackgroundRemoval":"MeituBackgroundRemoval",
        "Crop":"AlphaTrim","UseOpenAiCombined":false},"IgnoreSmallTrimNoise":false}
        """;

    [Fact]
    public async Task New_standard_and_verified_work_imports_snapshot_both_settings_and_relocation_does_not_change_existing_jobs()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        string source = harness.WriteBorderedSourcePng();
        SessionView previous = Must(await service.ImportAsync(WorkflowType.PrepareAsset, source, null, null, default));
        WriteDefaults(harness.Database, ExactDefaults, 12);
        SessionView imported = Must(await service.ImportAsync(WorkflowType.PrepareCustomerDesign, source, null, null, default));
        var facts = Must(await harness.FileInspector.InspectAsync(source, default));
        Guid request = Guid.NewGuid();
        SessionView work = Must(await service.ImportVerifiedWorkResultAsync(request, source, "Work", facts.Sha256, default));

        WriteDefaults(harness.Database,
            ExactDefaults.Replace("\"IgnoreSmallTrimNoise\":false", "\"IgnoreSmallTrimNoise\":true", StringComparison.Ordinal), 3);
        ISessionService restarted = harness.CreateService();
        foreach (SessionView original in new[] { imported, work })
        {
            SessionView loaded = Must(await restarted.LoadAsync(original.Id, default));
            loaded.TrimMargin.ShouldBe(TrimMargin.Uniform(12));
            loaded.IgnoreSmallTrimNoise.ShouldBeFalse();
        }
        Must(await restarted.LoadAsync(previous.Id, default)).TrimMargin.ShouldBe(TrimMargin.Tight);
        Must(await restarted.LoadAsync(previous.Id, default)).IgnoreSmallTrimNoise.ShouldBeTrue();
        Must(await restarted.ImportVerifiedWorkResultAsync(request, source, "Work", facts.Sha256, default))
            .TrimMargin.ShouldBe(TrimMargin.Uniform(12));

        using var connection = harness.Database.OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ProcessingSession WHERE ImageProcessingSelection IS NOT NULL";
        Convert.ToInt64(command.ExecuteScalar()).ShouldBe(3);
    }

    [Fact]
    public async Task Version_22_upgrade_preserves_active_review_geometry_and_historical_attempt()
    {
        using TempDatabase database = new(migrate: false);
        using var connection = database.OpenRaw();
        var assembly = typeof(MigrationRunner).Assembly;
        const string prefix = "PrintFlow.Infrastructure.Sqlite.Migrations.";
        foreach (string resource in assembly.GetManifestResourceNames()
                     .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))
                     .Where(name => string.CompareOrdinal(name[prefix.Length..][..4], "0022") <= 0)
                     .Order(StringComparer.Ordinal))
        {
            using Stream stream = assembly.GetManifestResourceStream(resource)!;
            using StreamReader script = new(stream);
            Execute(connection, script.ReadToEnd());
        }
        SessionId id = SessionId.From(Guid.NewGuid());
        string input = Guid.NewGuid().ToString();
        string output = Guid.NewGuid().ToString();
        string attempt = Guid.NewGuid().ToString();
        string inputSha = new('a', 64);
        string outputSha = new('b', 64);
        const string time = "2026-01-01T00:00:00.000Z";
        Execute(connection, $"""
            INSERT INTO ProcessingSession
                (Id, WorkflowType, OutputName, CurrentStep, State, WorkspacePath, CreatedAtUtc, UpdatedAtUtc,
                 TrimMode, TrimMarginTop, TrimMarginRight, TrimMarginBottom, TrimMarginLeft, IgnoreSmallTrimNoise)
            VALUES ('{id}', 'PREPARE_ASSET', 'legacy', 'Trim', 'ACTIVE', 'Sessions/legacy', '{time}', '{time}',
                    'EDGE_SPECIFIC_MARGIN', 1, 2, 3, 4, 0);
            INSERT INTO Revision
                (Id, SessionId, SourceRevisionId, Operation, RelativePath, Format, ByteLength,
                 Sha256, ColourMode, PixelWidth, PixelHeight, HasAlpha, CreatedAtUtc, ReviewState)
            VALUES ('{input}', '{id}', NULL, 'IMPORT', 'Sessions/legacy/Source/original.png', 'PNG', 100,
                    '{inputSha}', 'RGB', 30, 30, 1, '{time}', 'APPROVED'),
                   ('{output}', '{id}', '{input}', 'TRIM', 'Sessions/legacy/Working/trim.png', 'PNG', 75,
                    '{outputSha}', 'RGB', 23, 23, 1, '{time}', 'NOT_REVIEWED');
            INSERT INTO SessionStep
                (SessionId, StepKind, Ordinal, State, CurrentRevisionId, CurrentRevisionSha, AttemptCount, EnteredStateAtUtc)
            VALUES ('{id}', 'Import', 0, 'APPROVED', '{input}', '{inputSha}', 1, '{time}'),
                   ('{id}', 'Trim', 4, 'REVIEW_REQUIRED', '{output}', '{outputSha}', 1, '{time}');
            INSERT INTO ProcessingAttempt
                (Id, SessionId, StepKind, InputRevisionId, Operation, AdapterId, StartedAtUtc, EndedAtUtc, ResultStatus,
                 OutputRevisionId, TrimMode, TrimMarginTop, TrimMarginRight, TrimMarginBottom, TrimMarginLeft,
                 IgnoreSmallTrimNoise, TrimContentLeft, TrimContentTop, TrimContentRight, TrimContentBottom,
                 TrimAppliedLeft, TrimAppliedTop, TrimAppliedRight, TrimAppliedBottom)
            VALUES ('{attempt}', '{id}', 'Trim', '{input}', 'TRIM', 'internal-alpha-trim-v1', '{time}', '{time}', 'SUCCEEDED',
                    '{output}', 'EDGE_SPECIFIC_MARGIN', 1, 2, 3, 4, 0, 5, 5, 22, 24, 1, 4, 24, 27);
            PRAGMA user_version = 22;
            """);

        Must(MigrationRunner.Migrate(connection));
        Must(MigrationRunner.Migrate(connection));
        MigrationRunner.NewestKnownVersion.ShouldBe(23);
        var session = Must(await new SqliteSessionRepository(database.Factory).LoadAsync(id, default)).ShouldNotBeNull();
        session.Session.TrimMargin.ShouldBe(TrimMargin.PerEdge(1, 2, 3, 4));
        session.Session.IgnoreSmallTrimNoise.ShouldBeFalse();
        session.Steps.Single(s => s.Step == StepKind.Trim).State.ShouldBe(StepState.ReviewRequired);
        var historical = session.Attempts.ShouldHaveSingleItem();
        historical.AdapterId.ShouldBe("internal-alpha-trim-v1");
        historical.TrimGeometry.ShouldNotBeNull();
        historical.IgnoreSmallTrimNoise.ShouldBe(false);
        historical.OutputRevisionId.ShouldBe(RevisionId.From(Guid.Parse(output)));

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ImageProcessingMethod FROM ProcessingAttempt";
        command.ExecuteScalar().ShouldBe(DBNull.Value);
        command.CommandText = "PRAGMA foreign_key_check";
        using var reader = command.ExecuteReader();
        reader.Read().ShouldBeFalse();
    }

    [Theory]
    [InlineData("""{"Version":77}""")]
    [InlineData("""{"Version":1,"Selection":{},"IgnoreSmallTrimNoise":true}""")]
    [InlineData("invalid")]
    public async Task Invalid_defaults_block_new_image_jobs_without_changing_existing_jobs_or_print_only_jobs(string value)
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        string source = harness.WriteBorderedSourcePng();
        SessionId before = Must(await service.ImportAsync(WorkflowType.PrepareAsset, source, null, null, default)).Id;
        WriteDefaults(harness.Database, value, 9);

        var failed = await service.ImportAsync(WorkflowType.PrepareAsset, source, null, null, default);
        failed.IsFailure.ShouldBeTrue();
        failed.Failure.MessageKey.ShouldBe("ImageProcessing_InvalidDefaults");
        Must(await service.LoadAsync(before, default)).IgnoreSmallTrimNoise.ShouldBeTrue();
        Must(await service.ImportAsync(WorkflowType.GeneratePrintTiff, source, null, null, default))
            .WorkflowType.ShouldBe(WorkflowType.GeneratePrintTiff);
    }

    [Fact]
    public async Task Attempts_persist_actual_method_and_input_hash_and_keep_previous_attempt_evidence()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        string source = harness.WriteBorderedSourcePng();
        WriteDefaults(harness.Database, ExactDefaults, 2);
        SessionId id = Must(await service.ImportAsync(WorkflowType.PrepareAsset, source, null, null, default)).Id;

        async Task<SessionView> Perform(WorkflowCommand command) =>
            Must(await service.ExecuteAsync(id, command, "tester", default));

        await Perform(new WorkflowCommand.ConfirmOriginal());
        await Perform(new WorkflowCommand.Skip(StepKind.Enhancement, "Already sharp"));
        await Perform(new WorkflowCommand.Skip(StepKind.BackgroundRemoval, "Already transparent"));
        await Perform(new WorkflowCommand.StartStep(StepKind.Trim));
        using var connection = harness.Database.OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT a.ImageProcessingMethod, a.ImageProcessingInputSha256, r.Sha256,
                   a.ImageProcessingAlgorithmVersion, a.AdapterId, a.IgnoreSmallTrimNoise, a.TrimMarginTop
            FROM ProcessingAttempt a JOIN Revision r ON r.Id = a.InputRevisionId
            WHERE a.SessionId = $id AND a.StepKind = 'Trim'
            """;
        command.Parameters.AddWithValue("$id", id.ToString());
        using (var reader = command.ExecuteReader())
        {
            reader.Read().ShouldBeTrue();
            reader.GetString(0).ShouldBe("AlphaTrim");
            reader.GetString(1).ShouldBe(reader.GetString(2));
            reader.GetString(3).ShouldBe(reader.GetString(4));
            reader.GetBoolean(5).ShouldBeFalse();
            reader.GetInt32(6).ShouldBe(2);
        }
        command.CommandText = """
            UPDATE ProcessingAttempt SET ImageProcessingInputSha256 =
                'FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF'
            WHERE SessionId = $id AND StepKind = 'Trim'
            """;
        Should.Throw<SqliteException>(() => command.ExecuteNonQuery());
        Must(await harness.CreateService().LoadAsync(id, default)).CurrentTrimIgnoresSmallNoise.ShouldBe(false);
    }

    [Fact]
    public async Task A_legacy_route_change_keeps_its_own_trim_settings_when_the_workstation_default_is_damaged()
    {
        using SessionServiceHarness harness = new();
        ISessionService service = harness.CreateService();
        WriteDefaults(harness.Database, ExactDefaults, 7);
        SessionId id = Must(await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteBorderedSourcePng(), null, null, default)).Id;
        Must(await service.ExecuteAsync(id, new WorkflowCommand.SelectWorkflow(WorkflowType.GeneratePrintTiff), null, default));
        using (var connection = harness.Database.OpenRaw())
            Execute(connection, $"UPDATE ProcessingSession SET ImageProcessingSelection = NULL WHERE Id = '{id}'");
        WriteDefaults(harness.Database, "invalid", 20);

        SessionView selected = Must(await harness.CreateService().ExecuteAsync(
            id, new WorkflowCommand.SelectWorkflow(WorkflowType.PrepareAsset), null, default));
        selected.TrimMargin.ShouldBe(TrimMargin.Uniform(7));
        selected.IgnoreSmallTrimNoise.ShouldBeFalse();
    }

    private static void WriteDefaults(TempDatabase database, string value, int margin)
    {
        using var connection = database.OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Setting (Key, Value) VALUES ($key, $value), ('TrimSafetyMarginPixels', $margin)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value
            """;
        command.Parameters.AddWithValue("$key", DefaultsKey);
        command.Parameters.AddWithValue("$value", value);
        command.Parameters.AddWithValue("$margin", margin);
        command.ExecuteNonQuery();
    }

    private static T Must<T>(OperationResult<T> result)
    {
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : null);
        return result.Value;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
