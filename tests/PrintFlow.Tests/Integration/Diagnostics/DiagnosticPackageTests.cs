using System.IO;
using System.IO.Compression;
using System.Text;
using PrintFlow.Domain.Automation;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Diagnostics;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Diagnostics;

/// <summary>SCRUM-11122's exact-attempt plan and local archive privacy boundary.</summary>
[Collection(SqliteCollection.Name)]
public sealed class DiagnosticPackageTests
{
    [Fact]
    public async Task Exact_attempt_plan_is_default_deny_and_includes_only_the_owned_failure_capture()
    {
        using SessionServiceHarness harness = new();
        string evidenceRoot = Path.Combine(harness.Workspace.Root, "Evidence");
        Directory.CreateDirectory(evidenceRoot);
        string screenshot = Path.Combine(evidenceRoot, "20260909T120000Z_meitu-failure_ABC.png");
        await File.WriteAllBytesAsync(screenshot, SyntheticImages.Png(11, 7));
        ISessionService sessions = harness.CreateServiceWithMeitu(new FailingMeitu(screenshot));
        SessionId sessionId = await ReadyForEnhancementAsync(harness, sessions, "customer-source.png");
        (await sessions.ExecuteAsync(sessionId, new WorkflowCommand.StartStep(StepKind.Enhancement),
            "tester", CancellationToken.None)).IsFailure.ShouldBeTrue();
        AttemptId attemptId = Accept(await sessions.LoadAsync(sessionId, CancellationToken.None))
            .CurrentFailureAttemptId.ShouldNotBeNull();

        IDiagnosticPackageService packages = CreatePackages(
            harness, sessions, evidenceRoot, Path.Combine(harness.Workspace.Root, "package-stage"));
        DiagnosticPackagePlan plan = Accept(await packages.BuildPlanAsync(
            sessionId, attemptId, CancellationToken.None));

        plan.Subject.SessionId.ShouldBe(sessionId);
        plan.Subject.AttemptId.ShouldBe(attemptId);
        plan.Subject.ProcessingName.ShouldBe("diagnostic-package");
        plan.Failure.LogStatus.ShouldBe(DiagnosticPackageLogStatus.Available);
        plan.Failure.LogEntryId.ShouldNotBeNull();
        plan.Items.Single(item => item.Role == DiagnosticPackageItemRole.FailureScreenshot)
            .ShouldSatisfyAllConditions(
                item => item.Disposition.ShouldBe(DiagnosticPackageItemDisposition.Included),
                item => item.Content.ShouldBe(DiagnosticPackageItemContent.EvidenceFile),
                item => item.ArchiveEntryName.ShouldBe("failure-screenshot.png"),
                item => item.File!.CanonicalPath.ShouldBe(Path.GetFullPath(screenshot)));
        plan.ArchiveEntryNames.ShouldBe(["manifest.txt", "failure-screenshot.png"]);

        DiagnosticPackageItemRole[] excluded = [
            DiagnosticPackageItemRole.CustomerSource,
            DiagnosticPackageItemRole.InputSnapshot,
            DiagnosticPackageItemRole.RevisionArtwork,
            DiagnosticPackageItemRole.ApprovedOutput,
            DiagnosticPackageItemRole.ProductionOutput,
            DiagnosticPackageItemRole.ManualArtwork,
            DiagnosticPackageItemRole.RecoveryEvidence,
            DiagnosticPackageItemRole.UnknownEvidence,
            DiagnosticPackageItemRole.DiagnosticDatabase,
        ];
        foreach (DiagnosticPackageItemRole role in excluded)
        {
            plan.Items.Single(item => item.Role == role).Disposition
                .ShouldBe(DiagnosticPackageItemDisposition.ExcludedByPolicy);
        }

        plan.Items.Where(item => item.Content == DiagnosticPackageItemContent.EvidenceFile)
            .ShouldHaveSingleItem().Role.ShouldBe(DiagnosticPackageItemRole.FailureScreenshot);
        plan.Items.Where(item => item.File is not null && item.File.CanonicalPath.Contains(
                "customer-source.png", StringComparison.OrdinalIgnoreCase))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task Historical_plan_never_substitutes_a_later_failure_when_its_log_and_capture_expired()
    {
        using SessionServiceHarness harness = new();
        string evidenceRoot = Path.Combine(harness.Workspace.Root, "Evidence");
        Directory.CreateDirectory(evidenceRoot);
        string firstShot = Path.Combine(evidenceRoot, "20260909T120001Z_first-failure_ABC.png");
        string secondShot = Path.Combine(evidenceRoot, "20260909T120002Z_second-failure_DEF.png");
        await File.WriteAllBytesAsync(firstShot, SyntheticImages.Png(5, 4));
        await File.WriteAllBytesAsync(secondShot, SyntheticImages.Png(6, 5));
        FailingMeitu meitu = new(firstShot);
        ISessionService sessions = harness.CreateServiceWithMeitu(meitu);
        SessionId sessionId = await ReadyForEnhancementAsync(harness, sessions, "history-source.png");
        (await sessions.ExecuteAsync(sessionId, new WorkflowCommand.StartStep(StepKind.Enhancement),
            "tester", CancellationToken.None)).IsFailure.ShouldBeTrue();
        AttemptId firstAttempt = Accept(await sessions.LoadAsync(sessionId, CancellationToken.None))
            .CurrentFailureAttemptId.ShouldNotBeNull();
        Accept(await sessions.ResolveErrorRecoveryAsync(
            sessionId, firstAttempt, ErrorRecoveryAction.Retry, "tester", CancellationToken.None));

        meitu.ScreenshotPath = secondShot;
        harness.Clock.Advance(TimeSpan.FromMinutes(1));
        (await sessions.ExecuteAsync(sessionId, new WorkflowCommand.StartStep(StepKind.Enhancement),
            "tester", CancellationToken.None)).IsFailure.ShouldBeTrue();
        AttemptId secondAttempt = Accept(await sessions.LoadAsync(sessionId, CancellationToken.None))
            .CurrentFailureAttemptId.ShouldNotBeNull();

        IReadOnlyList<AutomationLogEntry> logs =
            (await harness.Repository.LoadAutomationLogAsync(sessionId, CancellationToken.None)).Value;
        AutomationLogEntry firstLog = logs.Single(log => FailureEvidence.AttemptIdOf(log.Failure) == firstAttempt);
        SqliteDiagnosticRetentionRepository retention = new(harness.Database.Factory, harness.Workspace.Root);
        Accept(await retention.ExpireAsync(
            [firstLog.Id], harness.Clock.GetUtcNow().AddDays(1), CancellationToken.None));
        File.Delete(firstShot);

        IDiagnosticPackageService packages = CreatePackages(
            harness, sessions, evidenceRoot, Path.Combine(harness.Workspace.Root, "package-stage"));
        DiagnosticPackagePlan historical = Accept(await packages.BuildPlanAsync(
            sessionId, firstAttempt, CancellationToken.None));

        historical.Subject.AttemptId.ShouldBe(firstAttempt);
        historical.Subject.AttemptId.ShouldNotBe(secondAttempt);
        historical.Subject.IsCurrent.ShouldBeFalse();
        historical.Failure.ScreenshotPath.ShouldBe(firstShot);
        historical.Failure.ScreenshotPath.ShouldNotBe(secondShot);
        historical.Failure.LogStatus.ShouldBe(DiagnosticPackageLogStatus.Unavailable);
        historical.Failure.LogEntryId.ShouldBeNull();
        historical.Items.Single(item => item.Role == DiagnosticPackageItemRole.StructuredAutomationLog)
            .Disposition.ShouldBe(DiagnosticPackageItemDisposition.Unavailable);
        historical.Items.Single(item => item.Role == DiagnosticPackageItemRole.FailureScreenshot)
            .Disposition.ShouldBe(DiagnosticPackageItemDisposition.Unavailable);
        historical.ArchiveEntryNames.ShouldBe(["manifest.txt"]);
    }

    [Fact]
    public async Task Writer_publishes_an_exact_validated_archive_without_overwrite_or_source_mutation()
    {
        using SessionServiceHarness harness = new();
        string evidenceRoot = Path.Combine(harness.Workspace.Root, "Evidence");
        string output = Path.Combine(harness.Workspace.Root, "exports");
        string staging = Path.Combine(harness.Workspace.Root, "package-stage");
        Directory.CreateDirectory(evidenceRoot);
        Directory.CreateDirectory(output);
        string screenshot = Path.Combine(evidenceRoot, "20260909T120003Z_package-failure_123.png");
        byte[] screenshotBytes = SyntheticImages.Png(9, 6);
        await File.WriteAllBytesAsync(screenshot, screenshotBytes);
        string unrelatedEvidence = Path.Combine(evidenceRoot, "operator-note.txt");
        byte[] unrelatedEvidenceBefore = Encoding.UTF8.GetBytes("not package evidence");
        await File.WriteAllBytesAsync(unrelatedEvidence, unrelatedEvidenceBefore);
        string nestedEvidence = Path.Combine(evidenceRoot, "nested");
        Directory.CreateDirectory(nestedEvidence);
        string nestedCapture = Path.Combine(nestedEvidence, "20260909T120003Z_other-failure_999.png");
        byte[] nestedCaptureBefore = SyntheticImages.Png(4, 3);
        await File.WriteAllBytesAsync(nestedCapture, nestedCaptureBefore);
        string source = harness.WriteSourcePng("do-not-package.png");
        byte[] sourceBefore = await File.ReadAllBytesAsync(source);
        ISessionService sessions = harness.CreateServiceWithMeitu(new FailingMeitu(screenshot));
        SessionId sessionId = Accept(await sessions.ImportAsync(
            WorkflowType.PrepareAsset, source, "diagnostic-package", "tester", CancellationToken.None)).Id;
        Accept(await sessions.ExecuteAsync(sessionId, new WorkflowCommand.ConfirmOriginal(),
            "tester", CancellationToken.None));
        (await sessions.ExecuteAsync(sessionId, new WorkflowCommand.StartStep(StepKind.Enhancement),
            "tester", CancellationToken.None)).IsFailure.ShouldBeTrue();
        AttemptId attemptId = Accept(await sessions.LoadAsync(sessionId, CancellationToken.None))
            .CurrentFailureAttemptId.ShouldNotBeNull();
        IDiagnosticPackageService packages = CreatePackages(harness, sessions, evidenceRoot, staging);
        DiagnosticPackagePlan plan = Accept(await packages.BuildPlanAsync(
            sessionId, attemptId, CancellationToken.None));

        string requested = Path.Combine(output, "PrintFlow-Diagnostics.zip");
        byte[] existing = Encoding.UTF8.GetBytes("existing operator-owned file");
        await File.WriteAllBytesAsync(requested, existing);
        DiagnosticPackageExportResult exported = Accept(await packages.ExportAsync(
            plan, requested, CancellationToken.None));

        exported.SavedPath.ShouldBe(Path.Combine(output, "PrintFlow-Diagnostics (2).zip"));
        (await File.ReadAllBytesAsync(requested)).ShouldBe(existing);
        File.Exists(exported.SavedPath).ShouldBeTrue();
        await using (FileStream file = File.OpenRead(exported.SavedPath))
        using (ZipArchive archive = new(file, ZipArchiveMode.Read))
        {
            archive.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal)
                .ShouldBe(plan.ArchiveEntryNames.Order(StringComparer.Ordinal));
            ZipArchiveEntry manifest = archive.GetEntry("manifest.txt").ShouldNotBeNull();
            using StreamReader reader = new(manifest.Open(), Encoding.UTF8);
            string text = await reader.ReadToEndAsync();
            text.ShouldContain($"Session ID: {sessionId}");
            text.ShouldContain($"Attempt ID: {attemptId}");
            text.ShouldContain("Stable code: MeituTargetLost");
            text.ShouldContain("Local-only: This package was saved locally. Nothing was uploaded automatically.");
            text.ShouldContain("CustomerSource: ExcludedByPolicy");
            text.ShouldContain("RevisionArtwork: ExcludedByPolicy");
            text.ShouldContain("ProductionOutput: ExcludedByPolicy");
            archive.GetEntry(Path.GetFileName(unrelatedEvidence)).ShouldBeNull();
            archive.GetEntry(Path.GetFileName(nestedCapture)).ShouldBeNull();
            using MemoryStream captured = new();
            await archive.GetEntry("failure-screenshot.png").ShouldNotBeNull().Open().CopyToAsync(captured);
            captured.ToArray().ShouldBe(screenshotBytes);
        }

        (await File.ReadAllBytesAsync(source)).ShouldBe(sourceBefore);
        (await File.ReadAllBytesAsync(screenshot)).ShouldBe(screenshotBytes);
        (await File.ReadAllBytesAsync(unrelatedEvidence)).ShouldBe(unrelatedEvidenceBefore);
        (await File.ReadAllBytesAsync(nestedCapture)).ShouldBe(nestedCaptureBefore);
        Directory.Exists(staging).ShouldBeTrue();
        Directory.EnumerateFiles(staging).ShouldBeEmpty();
        Directory.EnumerateFiles(output, "*.tmp").ShouldBeEmpty();
    }

    [Fact]
    public async Task Evidence_change_after_preview_fails_without_a_partial_final_archive()
    {
        using SessionServiceHarness harness = new();
        string evidenceRoot = Path.Combine(harness.Workspace.Root, "Evidence");
        string output = Path.Combine(harness.Workspace.Root, "exports");
        string staging = Path.Combine(harness.Workspace.Root, "package-stage");
        Directory.CreateDirectory(evidenceRoot);
        Directory.CreateDirectory(output);
        string screenshot = Path.Combine(evidenceRoot, "20260909T120004Z_changed-failure_456.png");
        await File.WriteAllBytesAsync(screenshot, SyntheticImages.Png(8, 6));
        ISessionService sessions = harness.CreateServiceWithMeitu(new FailingMeitu(screenshot));
        SessionId sessionId = await ReadyForEnhancementAsync(harness, sessions, "changed-source.png");
        (await sessions.ExecuteAsync(sessionId, new WorkflowCommand.StartStep(StepKind.Enhancement),
            "tester", CancellationToken.None)).IsFailure.ShouldBeTrue();
        AttemptId attemptId = Accept(await sessions.LoadAsync(sessionId, CancellationToken.None))
            .CurrentFailureAttemptId.ShouldNotBeNull();
        IDiagnosticPackageService packages = CreatePackages(harness, sessions, evidenceRoot, staging);
        DiagnosticPackagePlan plan = Accept(await packages.BuildPlanAsync(
            sessionId, attemptId, CancellationToken.None));
        File.Delete(screenshot);

        string destination = Path.Combine(output, "must-not-exist.zip");
        OperationResult<DiagnosticPackageExportResult> result = await packages.ExportAsync(
            plan, destination, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.WorkspaceError);
        File.Exists(destination).ShouldBeFalse();
        Directory.EnumerateFiles(output).ShouldBeEmpty();
        Directory.EnumerateFiles(staging).ShouldBeEmpty();
    }

    private static IDiagnosticPackageService CreatePackages(
        SessionServiceHarness harness,
        ISessionService sessions,
        string evidenceRoot,
        string staging)
    {
        LocalDiagnosticPackageEvidence evidence = new(evidenceRoot);
        return new DiagnosticPackageService(
            sessions,
            new FixedDiagnostics(harness.Clock.GetUtcNow()),
            evidence,
            new DiagnosticPackageArchiveWriter(evidence, staging),
            new DiagnosticPackageApplicationInfo("PrintFlow Studio", "test-version"),
            new DiagnosticPackageStorageLocations(
                harness.Database.Factory.DatabasePath,
                evidenceRoot),
            harness.Clock);
    }

    private static async Task<SessionId> ReadyForEnhancementAsync(
        SessionServiceHarness harness,
        ISessionService service,
        string fileName)
    {
        SessionId id = Accept(await service.ImportAsync(
            WorkflowType.PrepareAsset,
            harness.WriteSourcePng(fileName),
            "diagnostic-package",
            "tester",
            CancellationToken.None)).Id;
        Accept(await service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(),
            "tester", CancellationToken.None));
        return id;
    }

    private static T Accept<T>(OperationResult<T> result)
    {
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);
        return result.Value;
    }

    private sealed class FailingMeitu(string screenshotPath) : IMeituProcessor
    {
        public string AdapterId => "scrum-11122-failing-meitu";
        public AdapterExecutionMode Mode => AdapterExecutionMode.Fake;
        public string ScreenshotPath { get; set; } = screenshotPath;

        public Task<OperationResult<AdapterOutput>> ProcessAsync(
            MeituRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Fail<AdapterOutput>(OperationFailure.Create(
                FailureCode.MeituTargetLost,
                "Synthetic failure for diagnostic package verification.",
                isRetryable: true,
                context: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [AutomationLogEntry.ScreenshotContextKey] = ScreenshotPath,
                })));
    }

    private sealed class FixedDiagnostics(DateTimeOffset observedAt) : IEnvironmentDiagnostics
    {
        public EnvironmentReadinessReport Read() => new(
            Verified: true,
            PresetIdentity: "synthetic-preset 1.0 (0123456789ab)",
            ObservedAt: observedAt,
            Checks:
            [
                new EnvironmentCheckReport(
                    "DisplayConfiguration",
                    EnvironmentCheckStatus.Passed,
                    IsBlocking: true,
                    "Failure_EnvironmentNotVerified",
                    "The synthetic display matches the accepted baseline."),
            ]);

        public Task<EnvironmentReadinessReport> RunLiveChecksAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Read());
    }
}
