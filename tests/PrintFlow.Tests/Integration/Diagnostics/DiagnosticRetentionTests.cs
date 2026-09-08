using System.IO;
using System.Diagnostics;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Data.Sqlite;
using PrintFlow.Domain.Automation;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Settings;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Diagnostics;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Diagnostics;

/// <summary>SCRUM-11121's real SQLite/filesystem diagnostic-retention boundary.</summary>
[Collection(SqliteCollection.Name)]
public sealed class DiagnosticRetentionTests
{
    [Fact]
    public async Task Expired_owned_historical_diagnostic_is_removed_without_erasing_attempt_history()
    {
        using SessionServiceHarness harness = new();
        DateTimeOffset recordedAt = harness.Clock.GetUtcNow();
        string evidenceRoot = Path.Combine(harness.Workspace.Root, "Evidence");
        string screenshot = WriteCapture(evidenceRoot, recordedAt, "historical-failure", "ABC123");
        byte[] sourceBefore = File.ReadAllBytes(harness.WriteSourcePng("retention-source.png"));
        ISessionService service = harness.CreateServiceWithMeitu(new FailingMeitu(screenshot));
        SessionId id = await ReadyForEnhancementAsync(
            harness, service, "retention-source.png", sourceAlreadyWritten: true);

        (await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Enhancement),
            "tester", CancellationToken.None)).IsFailure.ShouldBeTrue();
        SessionAggregate before = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        string revisionPath = harness.FileWorkspace.ResolveAbsolute(before.Revisions.Single().File);
        byte[] revisionBefore = File.ReadAllBytes(revisionPath);
        AttemptId attemptId = Accept(await service.LoadAsync(id, CancellationToken.None))
            .CurrentFailureAttemptId.ShouldNotBeNull();
        Accept(await service.ResolveErrorRecoveryAsync(
            id, attemptId, ErrorRecoveryAction.Retry, "tester", CancellationToken.None));

        harness.Clock.Advance(TimeSpan.FromDays(31));
        DiagnosticRetentionReport report = await CreateRetention(harness, evidenceRoot, configuredDays: 30)
            .MaintainAsync(CancellationToken.None);

        report.Succeeded.ShouldBeTrue(report.Warning?.ToString() ?? string.Empty);
        report.DeletedDiagnosticFiles.ShouldBe(1);
        report.ExpiredDatabaseDiagnostics.ShouldBe(1);
        File.Exists(screenshot).ShouldBeFalse();
        (await harness.Repository.LoadAutomationLogAsync(id, CancellationToken.None)).Value.ShouldBeEmpty();

        SessionAggregate after = (await harness.Repository.LoadAsync(id, CancellationToken.None)).Value!;
        after.Attempts.Single(attempt => attempt.Id == attemptId).Status.ShouldBe(
            PrintFlow.Domain.Attempts.AttemptStatus.Failed);
        File.ReadAllBytes(Path.Combine(harness.Workspace.Root, "retention-source.png")).ShouldBe(sourceBefore);
        File.ReadAllBytes(revisionPath).ShouldBe(revisionBefore);

        using (SqliteConnection connection = harness.Database.Factory.Open())
        using (SqliteCommand integrity = connection.CreateCommand())
        {
            integrity.CommandText = "PRAGMA integrity_check;";
            integrity.ExecuteScalar().ShouldBe("ok");
        }

        ErrorDetailsView details = Accept(await service.LoadErrorDetailsAsync(
            id, attemptId, CancellationToken.None));
        details.ScreenshotPath.ShouldBe(screenshot);
        details.ScreenshotStatus.ShouldBe(DiagnosticImageStatus.Unavailable);

        DiagnosticRetentionReport second = await CreateRetention(harness, evidenceRoot, configuredDays: 30)
            .MaintainAsync(CancellationToken.None);
        second.Succeeded.ShouldBeTrue();
        second.ExpiredDatabaseDiagnostics.ShouldBe(0);
        second.DeletedDiagnosticFiles.ShouldBe(0);
    }

    [Fact]
    public async Task Old_active_failure_and_a_shared_newer_reference_preserve_the_capture()
    {
        using SessionServiceHarness harness = new();
        DateTimeOffset firstAt = harness.Clock.GetUtcNow();
        string evidenceRoot = Path.Combine(harness.Workspace.Root, "Équipe", "Evidence");
        string screenshot = WriteCapture(evidenceRoot, firstAt, "shared-failure", "BEEF");
        // The newer row deliberately spells the same Windows path through a dot segment.
        // It also changes non-ASCII casing: reference identity is Windows path identity, not
        // SQLite's raw or ASCII-NOCASE string equality.
        FailingMeitu meitu = new(screenshot, UnicodeCaseAliasOf(screenshot));
        ISessionService service = harness.CreateServiceWithMeitu(meitu);
        SessionId id = await ReadyForEnhancementAsync(harness, service, "active.png");

        (await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Enhancement),
            "tester", CancellationToken.None)).IsFailure.ShouldBeTrue();
        AttemptId first = Accept(await service.LoadAsync(id, CancellationToken.None))
            .CurrentFailureAttemptId.ShouldNotBeNull();

        harness.Clock.Advance(TimeSpan.FromDays(31));
        DiagnosticRetentionReport active = await CreateRetention(harness, evidenceRoot, 30)
            .MaintainAsync(CancellationToken.None);
        active.ExpiredDatabaseDiagnostics.ShouldBe(0);
        File.Exists(screenshot).ShouldBeTrue();

        Accept(await service.ResolveErrorRecoveryAsync(
            id, first, ErrorRecoveryAction.Retry, "tester", CancellationToken.None));
        (await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Enhancement),
            "tester", CancellationToken.None)).IsFailure.ShouldBeTrue();

        DiagnosticRetentionReport shared = await CreateRetention(harness, evidenceRoot, 30)
            .MaintainAsync(CancellationToken.None);
        shared.Succeeded.ShouldBeTrue();
        shared.ExpiredDatabaseDiagnostics.ShouldBe(1);
        shared.DeletedDiagnosticFiles.ShouldBe(0);
        File.Exists(screenshot).ShouldBeTrue();
        (await harness.Repository.LoadAutomationLogAsync(id, CancellationToken.None)).Value.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Unknown_and_source_files_are_preserved_even_when_old_and_referenced()
    {
        using SessionServiceHarness harness = new();
        DateTimeOffset old = harness.Clock.GetUtcNow();
        string evidenceRoot = Path.Combine(harness.Workspace.Root, "Équipe", "Evidence");
        Directory.CreateDirectory(evidenceRoot);
        string unknown = Path.Combine(evidenceRoot, "old-but-unclassified.png");
        File.WriteAllBytes(unknown, SyntheticImages.Png(2, 2));
        File.SetLastWriteTimeUtc(unknown, old.UtcDateTime);

        // The original source is deliberately capture-shaped and directly under Evidence.
        // Filename/root classification alone would grant deletion, so only the persisted
        // InputSnapshot authority can veto it.
        string source = WriteCapture(evidenceRoot, old, "authority", "FEED");
        byte[] sourceBefore = File.ReadAllBytes(source);
        ISessionService service = harness.CreateServiceWithMeitu(new FailingMeitu(source));
        SessionId id = Accept(await service.ImportAsync(
            WorkflowType.PrepareAsset, UnicodeCaseAliasOf(source), "retention", "tester", CancellationToken.None)).Id;
        Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));
        (await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Enhancement),
            "tester", CancellationToken.None)).IsFailure.ShouldBeTrue();
        AttemptId attemptId = Accept(await service.LoadAsync(id, CancellationToken.None))
            .CurrentFailureAttemptId.ShouldNotBeNull();
        Accept(await service.ResolveErrorRecoveryAsync(
            id, attemptId, ErrorRecoveryAction.Retry, "tester", CancellationToken.None));

        harness.Clock.Advance(TimeSpan.FromDays(31));
        DiagnosticRetentionReport report = await CreateRetention(harness, evidenceRoot, 30)
            .MaintainAsync(CancellationToken.None);

        report.DeletedDiagnosticFiles.ShouldBe(0);
        File.Exists(unknown).ShouldBeTrue();
        File.ReadAllBytes(source).ShouldBe(sourceBefore);
        (await harness.Repository.LoadAutomationLogAsync(id, CancellationToken.None)).Value.ShouldBeEmpty(
            "the old diagnostic row may expire even though overlapping source authority keeps the bytes");
    }

    [Fact]
    public async Task A_successful_manual_result_source_is_durable_authority_not_a_retention_deletion()
    {
        using SessionServiceHarness harness = new();
        DateTimeOffset old = harness.Clock.GetUtcNow();
        string evidenceRoot = Path.Combine(harness.Workspace.Root, "Evidence");
        string screenshot = WriteCapture(evidenceRoot, old, "manual-source", "FACE");
        ISessionService service = harness.CreateServiceWithMeitu(new FailingMeitu(screenshot));
        SessionId id = await ReadyForEnhancementAsync(harness, service, "manual-source-input.png");

        (await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Enhancement),
            "tester", CancellationToken.None)).IsFailure.ShouldBeTrue();
        AttemptId failed = Accept(await service.LoadAsync(id, CancellationToken.None))
            .CurrentFailureAttemptId.ShouldNotBeNull();
        Accept(await service.ResolveErrorRecoveryAsync(
            id, failed, ErrorRecoveryAction.ManualProcessing, "tester", CancellationToken.None));
        Accept(await service.ExecuteAsync(
            id,
            new WorkflowCommand.SubmitManualResult(StepKind.Enhancement, AliasOf(screenshot)),
            "tester",
            CancellationToken.None));

        using (SqliteConnection connection = harness.Database.Factory.Open())
        using (SqliteCommand source = connection.CreateCommand())
        {
            source.CommandText =
                "SELECT ManualResultSourcePath FROM ProcessingAttempt WHERE Operation = 'MANUAL_RESULT_IMPORT';";
            Path.GetFullPath((string)source.ExecuteScalar()!).ShouldBe(Path.GetFullPath(screenshot));
        }

        harness.Clock.Advance(TimeSpan.FromDays(31));
        DiagnosticRetentionReport report = await CreateRetention(harness, evidenceRoot, 30)
            .MaintainAsync(CancellationToken.None);

        report.Succeeded.ShouldBeTrue(report.Warning?.ToString() ?? string.Empty);
        report.ExpiredDatabaseDiagnostics.ShouldBe(1);
        report.DeletedDiagnosticFiles.ShouldBe(0);
        File.Exists(screenshot).ShouldBeTrue();
    }

    [Fact]
    public async Task A_legacy_manual_result_with_unknown_source_preserves_candidate_files()
    {
        using SessionServiceHarness harness = new();
        DateTimeOffset old = harness.Clock.GetUtcNow();
        string evidenceRoot = Path.Combine(harness.Workspace.Root, "Evidence");
        string screenshot = WriteCapture(evidenceRoot, old, "legacy-manual-source", "CAFE");
        ISessionService service = harness.CreateServiceWithMeitu(new FailingMeitu(screenshot));
        SessionId id = await ReadyForEnhancementAsync(harness, service, "legacy-manual-input.png");

        (await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Enhancement),
            "tester", CancellationToken.None)).IsFailure.ShouldBeTrue();
        AttemptId failed = Accept(await service.LoadAsync(id, CancellationToken.None))
            .CurrentFailureAttemptId.ShouldNotBeNull();
        Accept(await service.ResolveErrorRecoveryAsync(
            id, failed, ErrorRecoveryAction.ManualProcessing, "tester", CancellationToken.None));
        Accept(await service.ExecuteAsync(
            id,
            new WorkflowCommand.SubmitManualResult(StepKind.Enhancement, screenshot),
            "tester",
            CancellationToken.None));

        // This is the exact state migration 0016 produces for a successful manual import made
        // by an older build: the operation exists, but that build had no path column to populate.
        using (SqliteConnection connection = harness.Database.Factory.Open())
        using (SqliteCommand legacy = connection.CreateCommand())
        {
            legacy.CommandText =
                "UPDATE ProcessingAttempt SET ManualResultSourcePath = NULL " +
                "WHERE Operation = 'MANUAL_RESULT_IMPORT';";
            legacy.ExecuteNonQuery().ShouldBe(1);
        }

        harness.Clock.Advance(TimeSpan.FromDays(31));
        DiagnosticRetentionReport report = await CreateRetention(harness, evidenceRoot, 30)
            .MaintainAsync(CancellationToken.None);

        report.Succeeded.ShouldBeTrue(report.Warning?.ToString() ?? string.Empty);
        report.ExpiredDatabaseDiagnostics.ShouldBe(1);
        report.DeletedDiagnosticFiles.ShouldBe(0);
        File.Exists(screenshot).ShouldBeTrue(
            "unknown legacy manual-source ownership must narrow deletion, never broaden it");
    }

    [Fact]
    public async Task Retention_uses_persisted_then_configured_then_product_default_days()
    {
        using SessionServiceHarness harness = new();
        DateTimeOffset old = harness.Clock.GetUtcNow();
        string evidenceRoot = Path.Combine(harness.Workspace.Root, "Evidence");
        string screenshot = WriteCapture(evidenceRoot, old, "duration", "C0FFEE");
        ISessionService service = harness.CreateServiceWithMeitu(new FailingMeitu(screenshot));
        SessionId id = await ReadyForEnhancementAsync(harness, service, "duration.png");
        (await service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Enhancement),
            "tester", CancellationToken.None)).IsFailure.ShouldBeTrue();
        AttemptId attemptId = Accept(await service.LoadAsync(id, CancellationToken.None))
            .CurrentFailureAttemptId.ShouldNotBeNull();
        Accept(await service.ResolveErrorRecoveryAsync(
            id, attemptId, ErrorRecoveryAction.Retry, "tester", CancellationToken.None));
        harness.Clock.Advance(TimeSpan.FromDays(45));

        DiagnosticRetentionReport configured = await CreateRetention(harness, evidenceRoot, 60)
            .MaintainAsync(CancellationToken.None);
        configured.ExpiredDatabaseDiagnostics.ShouldBe(0);
        File.Exists(screenshot).ShouldBeTrue();

        await harness.Settings.UpsertAsync(
            [SettingEntry.Integer(SettingKey.LogRetentionDays, 5000)], CancellationToken.None);
        DiagnosticRetentionReport invalidLegacy = await CreateRetention(harness, evidenceRoot, 60)
            .MaintainAsync(CancellationToken.None);
        invalidLegacy.ExpiredDatabaseDiagnostics.ShouldBe(0,
            "a persisted value outside Settings validation falls back to configured authority");
        File.Exists(screenshot).ShouldBeTrue();

        await harness.Settings.UpsertAsync(
            [SettingEntry.Integer(SettingKey.LogRetentionDays, 30)], CancellationToken.None);
        DiagnosticRetentionReport persisted = await CreateRetention(harness, evidenceRoot, 60)
            .MaintainAsync(CancellationToken.None);
        persisted.ExpiredDatabaseDiagnostics.ShouldBe(1);
        File.Exists(screenshot).ShouldBeFalse();

        // A fresh database with no row and unusable configured input falls through to the
        // Product's 30-day constant rather than inventing a destructive zero-day policy.
        using SessionServiceHarness fallbackHarness = new();
        string fallbackRoot = Path.Combine(fallbackHarness.Workspace.Root, "Evidence");
        string fallbackShot = WriteCapture(
            fallbackRoot, fallbackHarness.Clock.GetUtcNow(), "fallback", "D00D");
        ISessionService fallbackService = fallbackHarness.CreateServiceWithMeitu(new FailingMeitu(fallbackShot));
        SessionId fallbackId = await ReadyForEnhancementAsync(fallbackHarness, fallbackService, "fallback.png");
        (await fallbackService.ExecuteAsync(fallbackId,
            new WorkflowCommand.StartStep(StepKind.Enhancement), "tester", CancellationToken.None))
            .IsFailure.ShouldBeTrue();
        AttemptId fallbackAttempt = Accept(await fallbackService.LoadAsync(fallbackId, CancellationToken.None))
            .CurrentFailureAttemptId.ShouldNotBeNull();
        Accept(await fallbackService.ResolveErrorRecoveryAsync(
            fallbackId, fallbackAttempt, ErrorRecoveryAction.Retry, "tester", CancellationToken.None));
        fallbackHarness.Clock.Advance(TimeSpan.FromDays(31));

        DiagnosticRetentionReport fallback = await CreateRetention(fallbackHarness, fallbackRoot, configuredDays: 0)
            .MaintainAsync(CancellationToken.None);
        fallback.ExpiredDatabaseDiagnostics.ShouldBe(1);
        File.Exists(fallbackShot).ShouldBeFalse();
    }

    [Fact]
    public async Task A_reparse_evidence_root_is_refused_without_touching_its_target()
    {
        using TempWorkspace workspace = new();
        using TempWorkspace outside = new();
        string evidenceRoot = Path.Combine(workspace.Root, "Evidence");
        CreateJunction(evidenceRoot, outside.Root);
        string capture = Path.Combine(evidenceRoot, "20260101T010203Z_reparse_ABC.png");
        File.WriteAllBytes(Path.Combine(outside.Root, Path.GetFileName(capture)), SyntheticImages.Png(2, 2));
        File.SetLastWriteTimeUtc(Path.Combine(outside.Root, Path.GetFileName(capture)),
            new DateTime(2026, 1, 1, 1, 2, 3, DateTimeKind.Utc));

        try
        {
            OperationResult<DiagnosticFileDeletion> result = await new LocalDiagnosticFileStore(evidenceRoot)
                .DeleteExpiredAsync(
                    capture,
                    new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
                    CancellationToken.None);

            result.IsFailure.ShouldBeTrue();
            File.Exists(Path.Combine(outside.Root, Path.GetFileName(capture))).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(evidenceRoot);
        }
    }

    [Fact]
    public async Task A_still_held_automation_lease_defers_retention_without_acquiring_or_clearing_it()
    {
        using SessionServiceHarness harness = new();
        using (SqliteConnection connection = harness.Database.Factory.Open())
        using (SqliteCommand hold = connection.CreateCommand())
        {
            hold.CommandText = "UPDATE AutomationLock SET Purpose = 'ENVIRONMENT_VERIFICATION', " +
                "OwnerToken = 'retention-held', ProcessId = 42, MachineName = 'synthetic' WHERE Id = 1;";
            hold.ExecuteNonQuery().ShouldBe(1);
        }

        DiagnosticRetentionReport report = await CreateRetention(
            harness, Path.Combine(harness.Workspace.Root, "Evidence"), 30)
            .MaintainAsync(CancellationToken.None);

        report.Succeeded.ShouldBeFalse();
        report.Warning!.Code.ShouldBe(FailureCode.AdapterUnavailable);
        AutomationLockState after = (await harness.Repository.GetAutomationLockAsync(CancellationToken.None)).Value;
        after.IsHeld.ShouldBeTrue();
        after.OwnerToken.ShouldBe("retention-held");
    }

    private static IDiagnosticRetentionService CreateRetention(
        SessionServiceHarness harness,
        string evidenceRoot,
        int configuredDays) =>
        new DiagnosticRetentionService(
            new SqliteDiagnosticRetentionRepository(harness.Database.Factory, harness.Workspace.Root),
            harness.Repository,
            harness.Settings,
            new LocalDiagnosticFileStore(evidenceRoot),
            new DiagnosticRetentionOptions(evidenceRoot, configuredDays, 30, 3650, BatchSize: 2),
            harness.Clock);

    private static string WriteCapture(
        string evidenceRoot,
        DateTimeOffset at,
        string reason,
        string handle)
    {
        Directory.CreateDirectory(evidenceRoot);
        string path = Path.Combine(evidenceRoot, $"{at:yyyyMMdd'T'HHmmss'Z'}_{reason}_{handle}.png");
        File.WriteAllBytes(path, SyntheticImages.Png(3, 2));
        File.SetLastWriteTimeUtc(path, at.UtcDateTime);
        return path;
    }

    private static string AliasOf(string path) =>
        Path.Combine(Path.GetDirectoryName(path)!, ".", Path.GetFileName(path));

    private static string UnicodeCaseAliasOf(string path) =>
        AliasOf(path).Replace(
            $"{Path.DirectorySeparatorChar}Équipe{Path.DirectorySeparatorChar}",
            $"{Path.DirectorySeparatorChar}équipe{Path.DirectorySeparatorChar}",
            StringComparison.Ordinal);

    private static void CreateJunction(string link, string target)
    {
        ProcessStartInfo start = new("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("/c");
        start.ArgumentList.Add("mklink");
        start.ArgumentList.Add("/J");
        start.ArgumentList.Add(link);
        start.ArgumentList.Add(target);
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.ShouldBe(0, output);
    }

    private static async Task<SessionId> ReadyForEnhancementAsync(
        SessionServiceHarness harness,
        ISessionService service,
        string fileName,
        bool sourceAlreadyWritten = false)
    {
        string source = sourceAlreadyWritten
            ? Path.Combine(harness.Workspace.Root, fileName)
            : harness.WriteSourcePng(fileName);
        SessionId id = Accept(await service.ImportAsync(
            WorkflowType.PrepareAsset, source, "retention", "tester", CancellationToken.None)).Id;
        Accept(await service.ExecuteAsync(
            id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None));
        return id;
    }

    private static T Accept<T>(OperationResult<T> result)
    {
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);
        return result.Value;
    }

    private sealed class FailingMeitu(params string[] screenshotPaths) : IMeituProcessor
    {
        private int _next;

        public string AdapterId => "scrum-11121-failing-meitu";
        public AdapterExecutionMode Mode => AdapterExecutionMode.Fake;

        public Task<OperationResult<AdapterOutput>> ProcessAsync(
            MeituRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Fail<AdapterOutput>(OperationFailure.Create(
                FailureCode.MeituTargetLost,
                "The deterministic failure used by retention tests.",
                isRetryable: true,
                context: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [AutomationLogEntry.ScreenshotContextKey] =
                        screenshotPaths[Math.Min(_next++, screenshotPaths.Length - 1)],
                })));
    }
}
