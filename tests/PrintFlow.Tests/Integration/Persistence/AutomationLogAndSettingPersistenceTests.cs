using System.IO;
using Microsoft.Data.Sqlite;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Automation;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// SCRUM-11076 (original CSV work item 11108): the two record types the AC names that migration
/// <c>0001</c> created and nothing wrote — <c>AutomationLog</c> and <c>Setting</c>.
/// </summary>
/// <remarks>
/// Every case here runs the real object graph against a real temporary SQLite database, and each
/// readback goes through a repository built on a <b>new</b> connection factory over the same file
/// — the AC's "restart preserves ... history" is not a claim an in-memory cache may answer.
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class AutomationLogAndSettingPersistenceTests
{
    // -------------------------------------------------------------------------------------
    // AutomationLogEntry
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Adapter_failure_writes_one_log_row_with_its_identity_time_detail_and_screenshot()
    {
        using SessionServiceHarness harness = new();
        SessionId id = await ReadyForEnhancementAsync(harness, "log-write.png");

        OperationResult<SessionView> failed = await harness
            .CreateServiceWithMeitu(new FailingMeitu(FailureCode.MeituTargetLost, @"D:\Evidence\meitu-lost.png"))
            .ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);

        failed.IsFailure.ShouldBeTrue();
        failed.Failure.Code.ShouldBe(FailureCode.MeituTargetLost);

        AutomationLogEntry entry = (await Restarted(harness).LoadAutomationLogAsync(id, CancellationToken.None))
            .Value.ShouldHaveSingleItem();

        entry.SessionId.ShouldBe(id);
        entry.Step.ShouldBe(StepKind.Enhancement);
        entry.AtUtc.ShouldBe(harness.Clock.GetUtcNow());
        entry.Failure.Code.ShouldBe(FailureCode.MeituTargetLost);
        entry.Failure.MessageKey.ShouldBe("Failure_MeituTargetLost");
        entry.Failure.TechnicalDetail.ShouldBe("The Meitu window was lost mid-run.");

        // The screenshot is a column, not a key inside a JSON blob: that is the whole difference
        // between "the path is somewhere in the failure detail" and "the path is queryable".
        entry.ScreenshotPath.ShouldBe(@"D:\Evidence\meitu-lost.png");
        entry.Failure.Context["evidencePath"].ShouldBe(@"D:\Evidence\meitu-lost.png");
    }

    [Fact]
    public async Task Log_row_lands_in_the_same_transaction_as_the_failed_attempt_it_describes()
    {
        using SessionServiceHarness harness = new();
        SessionId id = await ReadyForEnhancementAsync(harness, "log-atomic.png");

        // Commit 1 records the attempt as Running. Commit 2 is the closing transaction that
        // would record Failed plus its log row together; it is the injected crash.
        FaultingRepository faulting = new(harness.Repository) { FailFromCommit = 2 };
        OperationResult<SessionView> failed = await harness
            .CreateServiceWithMeitu(new FailingMeitu(FailureCode.MeituTargetLost, null), repository: faulting)
            .ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);

        failed.IsFailure.ShouldBeTrue();
        failed.Failure.Code.ShouldBe(FailureCode.PersistenceError);

        ISessionRepository restarted = Restarted(harness);
        SessionAggregate after = (await restarted.LoadAsync(id, CancellationToken.None)).Value!;

        // Neither half landed: the attempt is still Running for startup recovery to find, and
        // there is no record of a failure the database never accepted.
        after.Attempts.Single(a => a.Step == StepKind.Enhancement).Status.ShouldBe(AttemptStatus.Running);
        (await restarted.LoadAutomationLogAsync(id, CancellationToken.None)).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Unreadable_import_logs_its_failure_against_the_import_step()
    {
        using SessionServiceHarness harness = new();

        // An empty file is unreadable, so the import attempt closes as Failed after its opening
        // transaction — the one failure path that has no adapter behind it at all.
        string unreadable = Path.Combine(harness.Workspace.Root, "empty.png");
        await File.WriteAllBytesAsync(unreadable, []);

        OperationResult<SessionView> imported = await harness.CreateService().ImportAsync(
            WorkflowType.PrepareAsset, unreadable, "unreadable", "tester", CancellationToken.None);
        imported.IsFailure.ShouldBeTrue();

        // The import failed before the caller ever received a SessionId, so the session is found
        // the way the Home screen finds it — which is also what proves the row is not orphaned.
        SessionListItem session = (await Restarted(harness).ListRecentAsync(
            10, harness.Clock.GetUtcNow().AddDays(-1), CancellationToken.None)).Value.Single();

        AutomationLogEntry entry = (await Restarted(harness)
            .LoadAutomationLogAsync(session.Id, CancellationToken.None)).Value.ShouldHaveSingleItem();
        entry.Step.ShouldBe(StepKind.Import);
        entry.ScreenshotPath.ShouldBeNull("no external application was driven, so nothing was captured");
    }

    [Fact]
    public async Task An_operator_stop_is_logged_as_the_structured_error_it_produced()
    {
        using SessionServiceHarness harness = new();
        SessionId id = await ReadyForEnhancementAsync(harness, "log-stop.png");
        ISessionService service = harness.CreateService();

        harness.FakeMeitu.SetScenario(FakeAdapterScenario.WaitForStopAt(ExternalOperationPhase.NotStarted));
        Task<OperationResult<SessionView>> run = service.ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);
        await harness.FakeMeitu.HangStarted;
        service.RequestStop(id, AutomationStopMode.StopOperation).IsSuccess.ShouldBeTrue();
        (await run).IsFailure.ShouldBeTrue();

        SessionAggregate after = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        after.Attempts.Single(a => a.Step == StepKind.Enhancement).Status.ShouldBe(AttemptStatus.Cancelled);

        // A human stopping a run is still a structured error the operator may need explained,
        // and it commits with the Cancelled attempt rather than beside it.
        AutomationLogEntry entry = (await Restarted(harness).LoadAutomationLogAsync(id, CancellationToken.None))
            .Value.ShouldHaveSingleItem();
        entry.Step.ShouldBe(StepKind.Enhancement);
        entry.Failure.Code.ShouldBe(FailureCode.Cancelled);
    }

    [Fact]
    public async Task A_session_that_never_failed_has_an_empty_automation_log()
    {
        using SessionServiceHarness harness = new();
        SessionId id = await ReadyForEnhancementAsync(harness, "log-quiet.png");
        await Must(harness.CreateService().ExecuteAsync(
            id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None));

        SessionAggregate after = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        after.Attempts.Single(a => a.Step == StepKind.Enhancement).Status.ShouldBe(AttemptStatus.Succeeded);

        // The log records structured errors, not lifecycle noise. A run that worked writes nothing.
        (await Restarted(harness).LoadAutomationLogAsync(id, CancellationToken.None)).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Independent_SQLite_readback_finds_the_persisted_row_after_the_services_are_gone()
    {
        string databasePath;
        SessionId id;

        using (SessionServiceHarness harness = new())
        {
            databasePath = harness.Database.Path;
            id = await ReadyForEnhancementAsync(harness, "log-independent.png");
            harness.Database.RetainForInspection = true;

            OperationResult<SessionView> failed = await harness
                .CreateServiceWithMeitu(new FailingMeitu(FailureCode.MeituTargetLost, @"D:\Evidence\shot.png"))
                .ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None);
            failed.IsFailure.ShouldBeTrue();
        }

        // Every service and repository above is disposed. This reads the file itself.
        try
        {
            SqliteConnection.ClearAllPools();
            using SqliteConnection connection = new SqliteConnectionFactory(databasePath).Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT SessionId, StepKind, FailureCode, MessageKey, TechnicalDetail, ScreenshotPath " +
                "FROM AutomationLogEntry;";
            using SqliteDataReader reader = command.ExecuteReader();

            reader.Read().ShouldBeTrue();
            reader.GetString(0).ShouldBe(id.ToString());
            reader.GetString(1).ShouldBe("Enhancement");
            reader.GetString(2).ShouldBe("MeituTargetLost");
            reader.GetString(3).ShouldBe("Failure_MeituTargetLost");
            reader.GetString(4).ShouldNotBeNullOrWhiteSpace();
            reader.GetString(5).ShouldBe(@"D:\Evidence\shot.png");
            reader.Read().ShouldBeFalse();
        }
        finally
        {
            Cleanup(databasePath);
        }
    }

    // -------------------------------------------------------------------------------------
    // Setting
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task An_unset_setting_reads_as_absent_so_existing_defaults_still_decide()
    {
        using TempDatabase database = new();
        ISettingsRepository settings = new SqliteSettingsRepository(database.Factory);

        (await settings.ReadAsync(SettingKey.LogRetentionDays, CancellationToken.None))
            .Value.ShouldBeNull("an upgraded installation has no rows, and must behave exactly as before");
        (await settings.ReadAllAsync(CancellationToken.None)).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Settings_round_trip_typed_values_and_survive_a_restart()
    {
        using TempDatabase database = new();

        Accept(await new SqliteSettingsRepository(database.Factory).UpsertAsync(
            [
                SettingEntry.Text(SettingKey.UiLanguage, "zh-CN"),
                SettingEntry.Integer(SettingKey.LogRetentionDays, 30),
                SettingEntry.Boolean(SettingKey.PhotoshopColourSettingsConfirmed, true),
            ],
            CancellationToken.None));

        // A new repository over a new connection factory: nothing in memory carries across.
        ISettingsRepository restarted = new SqliteSettingsRepository(new SqliteConnectionFactory(database.Path));

        Accept(await restarted.ReadAsync(SettingKey.UiLanguage, CancellationToken.None))!.Value.ShouldBe("zh-CN");
        Accept(await restarted.ReadAsync(SettingKey.LogRetentionDays, CancellationToken.None))!
            .AsInteger().ShouldBe(30);
        Accept(await restarted.ReadAsync(SettingKey.PhotoshopColourSettingsConfirmed, CancellationToken.None))!
            .AsBoolean().ShouldBe(true);
        (await restarted.ReadAllAsync(CancellationToken.None)).Value.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Upserting_an_existing_key_replaces_its_value_rather_than_adding_a_second_row()
    {
        using TempDatabase database = new();
        ISettingsRepository settings = new SqliteSettingsRepository(database.Factory);

        Accept(await settings.UpsertAsync([SettingEntry.Integer(SettingKey.LogRetentionDays, 30)], CancellationToken.None));
        Accept(await settings.UpsertAsync([SettingEntry.Integer(SettingKey.LogRetentionDays, 7)], CancellationToken.None));

        (await settings.ReadAllAsync(CancellationToken.None)).Value.ShouldHaveSingleItem().AsInteger().ShouldBe(7);
    }

    [Fact]
    public async Task A_settings_batch_whose_second_write_fails_commits_neither()
    {
        using TempDatabase database = new();
        ISettingsRepository settings = new SqliteSettingsRepository(database.Factory);

        // The second entry violates the table's own NOT NULL constraint — a real failure of a
        // real write, not an injected fault around one.
        OperationResult<PrintFlow.Domain.Results.Unit> refused = await settings.UpsertAsync(
            [
                SettingEntry.Text(SettingKey.UiLanguage, "en"),
                new SettingEntry(SettingKey.LogRetentionDays, null!),
            ],
            CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.PersistenceError);

        // The first write is gone with it, read back through a fresh repository.
        (await new SqliteSettingsRepository(new SqliteConnectionFactory(database.Path))
            .ReadAllAsync(CancellationToken.None)).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task One_batch_may_not_write_the_same_key_twice()
    {
        using TempDatabase database = new();
        ISettingsRepository settings = new SqliteSettingsRepository(database.Factory);

        OperationResult<PrintFlow.Domain.Results.Unit> refused = await settings.UpsertAsync(
            [
                SettingEntry.Text(SettingKey.UiLanguage, "en"),
                SettingEntry.Text(SettingKey.UiLanguage, "zh-CN"),
            ],
            CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        refused.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        (await settings.ReadAllAsync(CancellationToken.None)).Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_key_this_build_does_not_recognise_is_skipped_rather_than_failing_the_read()
    {
        using TempDatabase database = new();

        using (SqliteConnection connection = database.Factory.Open())
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText =
                "INSERT INTO Setting (Key, Value) VALUES ('UiLanguage', 'en'), ('SettingFromANewerBuild', 'x');";
            command.ExecuteNonQuery();
        }

        IReadOnlyList<SettingEntry> read = Accept(
            await new SqliteSettingsRepository(database.Factory).ReadAllAsync(CancellationToken.None));

        read.ShouldHaveSingleItem().Key.ShouldBe(SettingKey.UiLanguage);
    }

    // -------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------

    /// <summary>A repository over the same file but a new connection factory: a "restarted" reader.</summary>
    private static ISessionRepository Restarted(SessionServiceHarness harness) =>
        new SqliteSessionRepository(new SqliteConnectionFactory(harness.Database.Path));

    private static async Task<SessionId> ReadyForEnhancementAsync(SessionServiceHarness harness, string fileName)
    {
        ISessionService service = harness.CreateService();
        SessionId id = Accept(await service.ImportAsync(
            WorkflowType.PrepareAsset, harness.WriteSourcePng(fileName), "log", "tester",
            CancellationToken.None)).Id;
        await Must(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));
        return id;
    }

    private static T Accept<T>(OperationResult<T> result)
    {
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);
        return result.Value;
    }

    private static async Task Must(Task<OperationResult<SessionView>> task) => Accept(await task);

    private static void Cleanup(string databasePath)
    {
        SqliteConnection.ClearAllPools();
        foreach (string path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>A Meitu adapter that fails the way a production one does: structured, with evidence.</summary>
    private sealed class FailingMeitu(FailureCode code, string? evidencePath) : IMeituProcessor
    {
        public string AdapterId => "scrum-11076-failing-meitu";

        public AdapterExecutionMode Mode => AdapterExecutionMode.Fake;

        public Task<OperationResult<AdapterOutput>> ProcessAsync(
            MeituRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Fail<AdapterOutput>(OperationFailure.Create(
                code,
                "The Meitu window was lost mid-run.",
                isRetryable: true,
                context: evidencePath is null
                    ? null
                    : new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [AutomationLogEntry.ScreenshotContextKey] = evidencePath,
                    })));
    }
}
