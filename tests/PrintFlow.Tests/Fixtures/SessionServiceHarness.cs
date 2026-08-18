using Microsoft.Extensions.Time.Testing;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Infrastructure.Gate;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Infrastructure.Preset;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;
using FileWorkspace = PrintFlow.Infrastructure.Workspace.FileWorkspace;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// Wires the real object graph — real workspace, real file inspector, real SQLite repository,
/// deterministic fake adapters — against a throwaway temp workspace and database.
/// </summary>
/// <remarks>
/// Used by the SessionService integration tests to prove the pieces work together: nothing
/// here is a mock of PrintFlow's own code, only Meitu/Photoshop are faked (Epic 11100 plan §35).
/// </remarks>
internal sealed class SessionServiceHarness : IDisposable
{
    public TempWorkspace Workspace { get; }

    public TempDatabase Database { get; }

    public FakeTimeProvider Clock { get; }

    public IWorkspace FileWorkspace { get; }

    public IFileInspector FileInspector { get; } = new WicFileInspector();

    public IWorkstationPresetProvider Preset { get; }

    public ISessionRepository Repository { get; }

    public IEnvironmentGate EnvironmentGate { get; } = new FoundationEnvironmentGate();

    /// <summary>
    /// The scriptable fake adapters used by <see cref="CreateService"/>, exposed as their
    /// concrete type so a test can call <c>SetScenario</c> (Epic 11100 Part 3A §3) before
    /// issuing a command.
    /// </summary>
    public FakeMeituProcessor FakeMeitu { get; }

    public FakePhotoshopOutputProcessor FakePhotoshop { get; }

    public SessionServiceHarness()
    {
        Workspace = new TempWorkspace();
        Database = new TempDatabase();
        Clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero));
        FileWorkspace = new FileWorkspace(Workspace.Root);

        (string presetPath, Sha256 hash) = PresetFixture.Write(Workspace.Root);
        Preset = new WorkstationPresetProvider(presetPath, PresetFixture.PresetId, PresetFixture.PresetVersion, hash);

        Repository = new SqliteSessionRepository(Database.Factory);
        FakeMeitu = new FakeMeituProcessor(FileWorkspace);
        FakePhotoshop = new FakePhotoshopOutputProcessor(FileWorkspace);
    }

    /// <summary>
    /// Builds a fresh <see cref="ISessionService"/> against the same workspace and database.
    /// A new instance (with a fresh in-memory identity) is what "restart/resume" tests need:
    /// nothing about the previous service is reused except the persisted state on disk.
    /// </summary>
    /// <remarks>
    /// Uses the real UUIDv7 generator rather than the deterministic
    /// <see cref="SequentialIdGenerator"/>: a restart test builds a second service instance to
    /// simulate reopening the app, and a counter-based generator would restart at the same
    /// values and collide with rows the first instance already wrote.
    /// </remarks>
    public ISessionService CreateService() => new SessionService(
        WorkflowEngine.Instance,
        Repository,
        FileWorkspace,
        FileInspector,
        FakeMeitu,
        FakePhotoshop,
        Preset,
        EnvironmentGate,
        SystemIdGenerator.Instance,
        Clock);

    /// <summary>
    /// Builds a service with a caller-supplied Meitu adapter in place of the scriptable fake —
    /// for tests that need to prove something about a non-fake <see cref="AdapterExecutionMode"/>
    /// (Epic 11100 Part 3A §8: <see cref="IEnvironmentGate"/> blocking a production adapter).
    /// </summary>
    public ISessionService CreateServiceWithMeitu(IMeituProcessor meitu) => new SessionService(
        WorkflowEngine.Instance,
        Repository,
        FileWorkspace,
        FileInspector,
        meitu,
        FakePhotoshop,
        Preset,
        EnvironmentGate,
        SystemIdGenerator.Instance,
        Clock);

    public string WriteSourcePng(string fileName = "source.png") =>
        Workspace.CreateSourceFile(fileName, SyntheticImages.Png(6, 5, alpha: true));

    public void Dispose()
    {
        Workspace.Dispose();
        Database.Dispose();
    }
}
