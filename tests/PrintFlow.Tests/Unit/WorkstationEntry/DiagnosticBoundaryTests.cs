using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32.SafeHandles;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Automation;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Diagnostics;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Infrastructure.Preset;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;
using PrintFlow.WorkstationEntry;
using Xunit;

namespace PrintFlow.Tests.Unit.WorkstationEntry;

/// <summary>
/// Runtime-only source. These tests must not execute until the independent containment gate
/// admits a product graph. Every attempt is made by a real service and persisted repository;
/// only the external failure and screenshot bytes are synthetic.
/// </summary>
[Collection("WorkstationEntryRuntimeLease")]
public sealed class DiagnosticBoundaryTests
{
    private readonly OwnedRuntimeFixture fixture;
    public DiagnosticBoundaryTests(OwnedRuntimeFixture fixture) => this.fixture = fixture;

    [Fact]
    [Trait("WorkstationEntryPhase", "Runtime")]
    public async Task Persisted_correlated_log_and_failure_context_each_reach_full_details_and_package_plan()
    {
        using DiagnosticGraph graph = new(fixture);
        string screenshot = WriteOwnedPng(graph.Paths, "evidence", true);
        SessionService sessions = graph.Sessions(new EvidenceFailure(screenshot));
        (SessionId sessionId, AttemptId attemptId) = await FailedAttempt(graph, sessions, screenshot);
        SessionAggregate aggregate = Required(await graph.Repository.LoadAsync(sessionId, CancellationToken.None))
            ?? throw new InvalidOperationException("Persisted failed session disappeared.");
        ProcessingAttempt attempt = aggregate.Attempts.Single(x => x.Id == attemptId);
        Assert.Equal(screenshot, attempt.Failure!.Context[AutomationLogEntry.ScreenshotContextKey]);
        AutomationLogEntry log = Assert.Single(Required(await graph.Repository.LoadAutomationLogAsync(
            sessionId, CancellationToken.None)));
        Assert.Equal(screenshot, log.ScreenshotPath);

        await AssertDetailsAndPlan(sessions, graph.Package(sessions), sessionId, attemptId,
            screenshot, included: true);

        // The read-only wrapper removes the correlated-log read response, never its stored row.
        // This drives the persisted failure-context fallback without inserting review or log SQL.
        SessionService fallback = graph.Sessions(new EvidenceFailure(screenshot),
            new LogUnavailableRepository(graph.Repository));
        await AssertDetailsAndPlan(fallback, graph.Package(fallback), sessionId, attemptId,
            screenshot, included: true);
        Assert.Equal(screenshot, Assert.Single(Required(await graph.Repository.LoadAutomationLogAsync(
            sessionId, CancellationToken.None))).ScreenshotPath);
    }

    [Fact]
    [Trait("WorkstationEntryPhase", "Runtime")]
    public async Task Outside_reparse_and_hardlink_screenshot_paths_are_refused_in_details_and_planning()
    {
        using DiagnosticGraph graph = new(fixture);
        using ExternalCapture sentinel = ExternalSentinel(graph.Paths);
        string external = sentinel.Path;
        byte[] originalSentinel = File.ReadAllBytes(external);
        await RefusedScreenshot(graph, external);

        string junction = graph.Paths.At("evidence", "junction-" + Guid.NewGuid().ToString("N"));
        graph.Paths.Require(junction, "evidence");
        Directory.CreateDirectory(junction);
        SetJunction(junction, Path.GetDirectoryName(external)!);
        string reparse = Path.Combine(junction, Path.GetFileName(external));
        Assert.Throws<IOException>(() => NativePathLease.ReadFile(reparse));
        await RefusedScreenshot(graph, reparse);

        string hardlink = CapturePath(graph.Paths, "hardlink");
        if (!CreateHardLinkW(hardlink, external, IntPtr.Zero))
            throw new IOException("The required hardlink negative fixture could not be created.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        Assert.Throws<IOException>(() => NativePathLease.ReadFile(hardlink));
        await RefusedScreenshot(graph, hardlink);
        Assert.Equal(originalSentinel, File.ReadAllBytes(external));
    }

    [Fact]
    [Trait("WorkstationEntryPhase", "Runtime")]
    public async Task Admitted_screenshot_identity_cannot_be_replaced_before_details_or_package_plan()
    {
        using DiagnosticGraph graph = new(fixture);
        string screenshot = WriteOwnedPng(graph.Paths, "evidence", true);
        byte[] original = File.ReadAllBytes(screenshot);
        string replacement = WriteOwnedPng(graph.Paths, "evidence", false);
        SessionService sessions = graph.Sessions(new EvidenceFailure(screenshot));
        (SessionId sessionId, AttemptId attemptId) = await FailedAttempt(graph, sessions, screenshot);
        Exception? moveDenial = Record.Exception(() => File.Move(replacement, screenshot, overwrite: true));
        Assert.True(moveDenial is IOException or UnauthorizedAccessException,
            $"Expected a native file-identity denial, got {moveDenial?.GetType().FullName ?? "no exception"}.");
        Assert.Equal(original, File.ReadAllBytes(screenshot));
        await AssertDetailsAndPlan(sessions, graph.Package(sessions), sessionId, attemptId,
            screenshot, included: true);
    }

    [Fact]
    [Trait("WorkstationEntryPhase", "Runtime")]
    public async Task Real_archive_writer_cleans_missing_source_stage_preserves_collision_and_refuses_pre_cancel()
    {
        using DiagnosticGraph graph = new(fixture);
        // This test isolates the unchanged real writer, not the containment wrapper. Its source
        // and destination are still explicit task-owned R paths; no customer file is referenced.
        string screenshot = WriteOwnedPng(graph.Paths, "evidence", false);
        SessionService sessions = graph.Sessions(new EvidenceFailure(screenshot), decoder: graph.RawDecoder);
        (SessionId sessionId, AttemptId attemptId) = await FailedAttempt(graph, sessions, screenshot);
        IDiagnosticPackageService packages = graph.RawPackage(sessions);
        DiagnosticPackagePlan plan = Required(await packages.BuildPlanAsync(sessionId, attemptId,
            CancellationToken.None));
        Assert.Equal(DiagnosticPackageItemDisposition.Included,
            plan.Items.Single(x => x.Role == DiagnosticPackageItemRole.FailureScreenshot).Disposition);

        string export = graph.Paths.At("diagnostics", "export");
        string staging = graph.Paths.At("diagnostics", "staging");
        graph.Paths.EnsureDirectory(export);
        graph.Paths.EnsureDirectory(staging);
        graph.Paths.Require(export, "export");
        graph.Paths.Require(staging, "staging");
        using NativePathLease heldExport = NativePathLease.Ancestors(export);
        using NativePathLease heldStaging = NativePathLease.Ancestors(staging);
        string collision = Path.Combine(export, "diagnostic-collision.zip");
        byte[] sentinel = [1, 2, 3, 4, 5];
        using (FileStream file = new(collision, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            file.Write(sentinel);
        DiagnosticPackageExportResult saved = Required(await packages.ExportAsync(plan, collision,
            CancellationToken.None));
        Assert.Equal(Path.Combine(export, "diagnostic-collision (2).zip"), saved.SavedPath);
        Assert.Equal(sentinel, File.ReadAllBytes(collision));
        using (ZipArchive archive = ZipFile.OpenRead(saved.SavedPath))
            Assert.Contains(archive.Entries, x => x.FullName == "failure-screenshot.png");
        AssertNoWriterTemporaries(graph.Paths, staging, export);
        Assert.Empty(Directory.EnumerateFiles(export, "*.tmp"));

        // After a real plan, source loss occurs before the writer opens the planned screenshot.
        // The writer creates its stage first, fails its exact-file read and cleans the stage.
        File.Delete(screenshot);
        string failedDestination = Path.Combine(export, "missing-evidence.zip");
        OperationResult<DiagnosticPackageExportResult> failed = await packages.ExportAsync(plan,
            failedDestination, CancellationToken.None);
        Assert.True(failed.IsFailure);
        Assert.False(File.Exists(failedDestination));
        AssertNoWriterTemporaries(graph.Paths, staging, export);

        // Pre-cancel proves no mutation starts; it is not evidence of mid-copy cancellation
        // cleanup or of an adjacent-stage failure after the copy has begun.
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => packages.ExportAsync(plan,
            Path.Combine(export, "cancelled.zip"), cancelled.Token));
        Assert.False(File.Exists(Path.Combine(export, "cancelled.zip")));
        AssertNoWriterTemporaries(graph.Paths, staging, export);
    }

    private static void AssertNoWriterTemporaries(OwnedPaths paths, string staging, string export)
    {
        foreach (string entry in Directory.EnumerateFileSystemEntries(staging))
            Assert.True(paths.IsOwnedMarker(entry), "Unexpected staged writer residue: " + entry);
        Assert.Empty(Directory.EnumerateFiles(export, "*.tmp"));
    }

    private static async Task RefusedScreenshot(DiagnosticGraph graph, string screenshot)
    {
        CountingDecoder inner = new(graph.RawDecoder);
        IDiagnosticImagePreviewDecoder guarded = new ContainedDiagnosticDecoder(graph.Paths, inner);
        SessionService sessions = graph.Sessions(new EvidenceFailure(screenshot), decoder: guarded);
        (SessionId sessionId, AttemptId attemptId) = await FailedAttempt(graph, sessions, screenshot);
        await AssertDetailsAndPlan(sessions, graph.Package(sessions), sessionId, attemptId,
            screenshot, included: false);
        SessionService fallback = graph.Sessions(new EvidenceFailure(screenshot),
            new LogUnavailableRepository(graph.Repository), guarded);
        await AssertDetailsAndPlan(fallback, graph.Package(fallback), sessionId, attemptId,
            screenshot, included: false);
        Assert.Equal(0, inner.Calls);
    }

    private static async Task AssertDetailsAndPlan(ISessionService sessions,
        IDiagnosticPackageService packages, SessionId sessionId, AttemptId attemptId,
        string screenshot, bool included)
    {
        ErrorDetailsView details = Required(await sessions.LoadErrorDetailsAsync(sessionId, attemptId,
            CancellationToken.None));
        Assert.Equal(sessionId, details.SessionId);
        Assert.Equal(attemptId, details.AttemptId);
        Assert.Equal(screenshot, details.ScreenshotPath);
        Assert.Equal(included ? DiagnosticImageStatus.Available : DiagnosticImageStatus.Unavailable,
            details.ScreenshotStatus);
        Assert.Equal(included, details.Screenshot is not null);
        DiagnosticPackagePlan plan = Required(await packages.BuildPlanAsync(sessionId, attemptId,
            CancellationToken.None));
        DiagnosticPackageItem item = plan.Items.Single(x => x.Role == DiagnosticPackageItemRole.FailureScreenshot);
        Assert.Equal(included, item.Disposition == DiagnosticPackageItemDisposition.Included);
        Assert.Equal(included, plan.ArchiveEntryNames.Contains("failure-screenshot.png"));
        Assert.Equal(screenshot, plan.Failure.ScreenshotPath);
    }

    private static async Task<(SessionId, AttemptId)> FailedAttempt(DiagnosticGraph graph,
        ISessionService sessions, string screenshot)
    {
        string source = WriteOwnedPng(graph.Paths, "fixtures", true);
        SessionView imported = Required(await sessions.ImportAsync(WorkflowType.PrepareAsset,
            source, "d", "synthetic-entry-fixture", CancellationToken.None));
        Required(await sessions.ExecuteAsync(imported.Id, new WorkflowCommand.ConfirmOriginal(),
            "synthetic-entry-fixture", CancellationToken.None));
        OperationResult<SessionView> failed = await sessions.ExecuteAsync(imported.Id,
            new WorkflowCommand.StartStep(StepKind.Enhancement), "synthetic-entry-fixture",
            CancellationToken.None);
        Assert.True(failed.IsFailure);
        SessionView current = Required(await sessions.LoadAsync(imported.Id, CancellationToken.None));
        Assert.Equal(StepState.Failed, current.CurrentStep?.State);
        Assert.NotNull(current.CurrentFailureAttemptId);
        SessionAggregate persisted = Required(await graph.Repository.LoadAsync(imported.Id,
            CancellationToken.None)) ?? throw new InvalidOperationException("Failed attempt was not persisted.");
        ProcessingAttempt attempt = persisted.Attempts.Single(x => x.Id == current.CurrentFailureAttemptId.Value);
        Assert.Equal(FailureCode.MeituTargetLost, attempt.Failure?.Code);
        Assert.Equal(screenshot, attempt.Failure!.Context[AutomationLogEntry.ScreenshotContextKey]);
        return (imported.Id, current.CurrentFailureAttemptId.Value);
    }

    private static string WriteOwnedPng(OwnedPaths paths, string area, bool admit)
    {
        string path = area == "evidence" ? CapturePath(paths, "synthetic") :
            paths.At("fixtures", "inputs", "d-" + Guid.NewGuid().ToString("N")[..8] + ".png");
        paths.Require(path, area);
        byte[] bytes = SyntheticImages.Png(12, 8, alpha: true);
        using (FileStream file = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            file.Write(bytes);
        if (admit) paths.Admit(path);
        return path;
    }

    private static string CapturePath(OwnedPaths paths, string reason) =>
        paths.At("evidence", DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'") + "_" +
            reason + "_" + Guid.NewGuid().ToString("N") + ".png");

    private static ExternalCapture ExternalSentinel(OwnedPaths paths)
    {
        string task = Directory.GetParent(Directory.GetParent(paths.Root)!.FullName)!.FullName;
        string directory = Path.Combine(task, "diagnostic-sentinels-" + Guid.NewGuid().ToString("N"));
        using NativePathLease parent = NativePathLease.Ancestors(directory);
        if (Directory.Exists(directory) || File.Exists(directory))
            throw new IOException("Synthetic sentinel directory unexpectedly exists.");
        Directory.CreateDirectory(directory);
        string marker = Path.Combine(directory, ".entry-owned-directory");
        using (FileStream created = new(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            created.WriteByte(42);
        NativePathLease held = NativePathLease.Ancestors(directory);
        try
        {
            string path = Path.Combine(directory, "diagnostic-sentinel-" + Guid.NewGuid().ToString("N") + ".png");
            using (FileStream file = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                file.Write(SyntheticImages.Png(7, 5, alpha: true));
            return new(path, held);
        }
        catch { held.Dispose(); throw; }
    }

    private sealed record ExternalCapture(string Path, NativePathLease Directory) : IDisposable
    {
        public void Dispose() => Directory.Dispose();
    }

    private static void SetJunction(string path, string target)
    {
        using SafeFileHandle writer = CreateFileW(path, 0x40000000, 7, 0, 3, 0x02200000, 0);
        if (writer.IsInvalid)
            throw new IOException("Could not open the synthetic reparse fixture directory.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        byte[] substitute = Encoding.Unicode.GetBytes(@"\??\" + target);
        byte[] print = Encoding.Unicode.GetBytes(target);
        byte[] buffer = new byte[16 + substitute.Length + 2 + print.Length + 2];
        BitConverter.GetBytes(0xA0000003u).CopyTo(buffer, 0);
        BitConverter.GetBytes((ushort)(buffer.Length - 8)).CopyTo(buffer, 4);
        BitConverter.GetBytes((ushort)substitute.Length).CopyTo(buffer, 10);
        BitConverter.GetBytes((ushort)(substitute.Length + 2)).CopyTo(buffer, 12);
        BitConverter.GetBytes((ushort)print.Length).CopyTo(buffer, 14);
        substitute.CopyTo(buffer, 16);
        print.CopyTo(buffer, 18 + substitute.Length);
        if (!DeviceIoControl(writer, 0x000900A4, buffer, buffer.Length, 0, 0, out _, 0))
            throw new IOException("Could not establish the synthetic directory reparse fixture.",
                new Win32Exception(Marshal.GetLastWin32Error()));
    }

    private static T Required<T>(OperationResult<T> result)
    {
        Assert.True(result.IsSuccess, result.IsFailure ? result.Failure.ToString() : "");
        return result.Value;
    }

    private sealed class EvidenceFailure(string screenshot) : IMeituProcessor
    {
        public string AdapterId => "synthetic-diagnostic-failure";
        public AdapterExecutionMode Mode => AdapterExecutionMode.Fake;
        public Task<OperationResult<AdapterOutput>> ProcessAsync(MeituRequest request,
            CancellationToken cancellationToken) => Task.FromResult(
                OperationResult.Fail<AdapterOutput>(OperationFailure.Create(
                    FailureCode.MeituTargetLost, "Synthetic diagnostic failure.", isRetryable: true,
                    context: new Dictionary<string, string>(StringComparer.Ordinal)
                    { [AutomationLogEntry.ScreenshotContextKey] = screenshot })));
    }

    private sealed class CountingDecoder(IDiagnosticImagePreviewDecoder inner) : IDiagnosticImagePreviewDecoder
    {
        public int Calls { get; private set; }
        public Task<OperationResult<DecodedPreview>> DecodeDiagnosticAsync(string persistedAbsolutePath,
            CancellationToken cancellationToken)
        {
            Calls++;
            return inner.DecodeDiagnosticAsync(persistedAbsolutePath, cancellationToken);
        }
    }

    private sealed class DiagnosticGraph : IDisposable
    {
        private readonly ServiceProvider provider;
        private readonly SqliteConnectionFactory database;
        private readonly NativePathLease presetIdentity;
        public OwnedPaths Paths { get; }
        public ISessionRepository Repository => provider.GetRequiredService<ISessionRepository>();
        public IDiagnosticImagePreviewDecoder RawDecoder =>
            new WicImagePreviewDecoder(provider.GetRequiredService<IWorkspace>());

        public DiagnosticGraph(OwnedRuntimeFixture fixture)
        {
            OwnedPaths paths = fixture.Paths;
            Paths = paths;
            foreach (string area in new[] { "state", "workspace", "fixtures", "preset", "delivery",
                         "recycle", "evidence", "diagnostics" })
                paths.EnsureDirectory(paths.At(area));
            foreach (string nested in new[] { Path.Combine("fixtures", "inputs"),
                         Path.Combine("fixtures", "returns"), Path.Combine("diagnostics", "staging"),
                         Path.Combine("diagnostics", "export") })
                paths.EnsureDirectory(paths.At(nested));
            string databasePath = fixture.CreateDiagnosticDatabase();
            database = new(databasePath);
            using (SqliteConnection connection = database.Open())
                Assert.True(MigrationRunner.Migrate(connection).IsSuccess);
            string presetPath = paths.Require(paths.At("preset", "synthetic-preset.json"), "preset");
            if (!File.Exists(presetPath))
                using (FileStream presetFile = new(presetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (StreamWriter writer = new(presetFile)) writer.Write(PresetFixture.Json);
            NativePathLease heldPreset = NativePathLease.ReadFile(presetPath);
            try
            {
                if (File.ReadAllText(presetPath) != PresetFixture.Json)
                    throw new InvalidOperationException("Synthetic run preset bytes changed.");
                Sha256 presetHash = Sha256.FromBytes(SHA256.HashData(File.ReadAllBytes(presetPath)));
                WorkstationPresetProvider preset = new(presetPath, PresetFixture.PresetId,
                    PresetFixture.PresetVersion, presetHash);
                provider = EntryComposition.Build(paths, database, preset, new ContainedNativePorts(paths));
                presetIdentity = heldPreset;
            }
            catch { heldPreset.Dispose(); throw; }
        }

        public SessionService Sessions(IMeituProcessor meitu, ISessionRepository? repository = null,
            IDiagnosticImagePreviewDecoder? decoder = null)
        {
            ISessionRepository selected = repository ?? Repository;
            return new SessionService(provider.GetRequiredService<IWorkflowEngine>(), selected,
                provider.GetRequiredService<IWorkspace>(), provider.GetRequiredService<IRecycleBin>(),
                provider.GetRequiredService<IFileInspector>(), meitu,
                provider.GetRequiredService<IPhotoshopOutputProcessor>(),
                provider.GetRequiredService<ITrimProcessor>(),
                provider.GetRequiredService<IManualCropProcessor>(),
                provider.GetRequiredService<IWorkstationPresetProvider>(),
                provider.GetRequiredService<IEnvironmentGate>(),
                provider.GetRequiredService<IIdGenerator>(), TimeProvider.System,
                provider.GetRequiredService<IPdfPreparationProcessor>(),
                provider.GetRequiredService<IManualResultImporter>(),
                provider.GetRequiredService<ISettingsRepository>(),
                decoder ?? provider.GetRequiredService<IDiagnosticImagePreviewDecoder>(),
                null, provider.GetRequiredService<IWorkstationAutomationLeaseManager>(),
                provider.GetRequiredService<ICorrectionPackageStore>());
        }

        public IDiagnosticPackageService Package(ISessionService sessions) =>
            new DiagnosticPackageService(sessions,
                provider.GetRequiredService<IEnvironmentDiagnostics>(),
                provider.GetRequiredService<IDiagnosticPackageEvidenceInspector>(),
                provider.GetRequiredService<IDiagnosticPackageWriter>(),
                provider.GetRequiredService<DiagnosticPackageApplicationInfo>(),
                provider.GetRequiredService<DiagnosticPackageStorageLocations>(), TimeProvider.System);

        public IDiagnosticPackageService RawPackage(ISessionService sessions) =>
            new DiagnosticPackageService(sessions,
                provider.GetRequiredService<IEnvironmentDiagnostics>(),
                new LocalDiagnosticPackageEvidence(Paths.At("evidence")),
                new DiagnosticPackageArchiveWriter(new LocalDiagnosticPackageEvidence(Paths.At("evidence")),
                    Paths.At("diagnostics", "staging")),
                provider.GetRequiredService<DiagnosticPackageApplicationInfo>(),
                provider.GetRequiredService<DiagnosticPackageStorageLocations>(), TimeProvider.System);

        public void Dispose()
        {
            try
            {
                provider.Dispose();
                Paths.ProtectDatabase(database.DatabasePath, createNew: false);
                string connectionString = new SqliteConnectionStringBuilder
                    { DataSource = database.DatabasePath }.ToString();
                using SqliteConnection pool = new(connectionString);
                SqliteConnection.ClearPool(pool);
            }
            finally { presetIdentity.Dispose(); }
        }
    }

    private sealed class LogUnavailableRepository(ISessionRepository inner) : ISessionRepository
    {
        public Task<OperationResult<IReadOnlyList<SessionId>>> FindRecoveryCandidatesAsync(CancellationToken ct) =>
            inner.FindRecoveryCandidatesAsync(ct);
        public Task<OperationResult<SessionAggregate?>> LoadAsync(SessionId id, CancellationToken ct) =>
            inner.LoadAsync(id, ct);
        public Task<OperationResult<IReadOnlyList<SessionListItem>>> ListRecentAsync(int count,
            DateTimeOffset since, CancellationToken ct) => inner.ListRecentAsync(count, since, ct);
        public Task<OperationResult<PrintFlow.Domain.Results.Unit>> CommitAsync(SessionMutation mutation, CancellationToken ct) =>
            inner.CommitAsync(mutation, ct);
        public Task<OperationResult<IReadOnlyList<ProcessingAttempt>>> FindRunningAttemptsAsync(CancellationToken ct) =>
            inner.FindRunningAttemptsAsync(ct);
        public Task<OperationResult<IReadOnlyList<SessionId>>> FindCompletedSessionsAsync(CancellationToken ct) =>
            inner.FindCompletedSessionsAsync(ct);
        public Task<OperationResult<AutomationLockState>> GetAutomationLockAsync(CancellationToken ct) =>
            inner.GetAutomationLockAsync(ct);
        public Task<OperationResult<IReadOnlyList<AutomationLogEntry>>> LoadAutomationLogAsync(SessionId id,
            CancellationToken ct) => Task.FromResult(OperationResult.Fail<IReadOnlyList<AutomationLogEntry>>(
                FailureCode.PersistenceError, "Synthetic log read unavailable; persisted attempt fallback only."));
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string newFileName, string existingFileName,
        IntPtr securityAttributes);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share,
        nint security, uint mode, uint flags, nint template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle file, uint control, byte[] input,
        int size, nint output, int outputSize, out int returned, nint overlapped);
}
