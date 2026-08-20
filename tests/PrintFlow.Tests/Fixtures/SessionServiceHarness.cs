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
/// real deterministic trim, deterministic fake adapters — against a throwaway temp workspace
/// and database.
/// </summary>
/// <remarks>
/// Used by the SessionService integration tests to prove the pieces work together: nothing
/// here is a mock of PrintFlow's own code, only Meitu/Photoshop are faked (Epic 11100 plan §35).
/// Trim is emphatically not faked — it is PrintFlow's own pixel work, so a double would prove
/// nothing about it (Epic 11200 Part B §23).
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

    /// <summary>
    /// The real deterministic trim processor, never a double: Epic 11200 Part B §23 requires
    /// the integration flow to produce an actual cropped file, not a scripted one.
    /// </summary>
    public ITrimProcessor Trim { get; }

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
        Trim = new DeterministicAlphaTrimProcessor(FileWorkspace);
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
        Trim,
        Preset,
        EnvironmentGate,
        SystemIdGenerator.Instance,
        Clock);

    /// <summary>
    /// Builds a fresh <see cref="IStartupRecoveryService"/> against the same workspace and
    /// database, with a scripted liveness answer (Epic 11100 Part 3B).
    /// </summary>
    /// <remarks>
    /// Built separately from <see cref="CreateService"/> on purpose: recovery is what the
    /// <b>next</b> process does after a crash, so a test that shares nothing with the service
    /// that crashed is the honest arrangement — only the database and the files on disk carry
    /// state across.
    /// </remarks>
    public IStartupRecoveryService CreateRecoveryService(FakeProcessLiveness liveness) =>
        new StartupRecoveryService(
            WorkflowEngine.Instance,
            Repository,
            FileWorkspace,
            liveness,
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
        Trim,
        Preset,
        EnvironmentGate,
        SystemIdGenerator.Instance,
        Clock);

    public string WriteSourcePng(string fileName = "source.png") =>
        Workspace.CreateSourceFile(fileName, SyntheticImages.Png(6, 5, alpha: true));

    /// <summary>
    /// A 12×10 PNG whose only alpha content is the 5×5 block from (3,2) to (7,6) inclusive.
    /// </summary>
    /// <remarks>
    /// The deliberately asymmetric border is what makes a trim assertion meaningful: a crop
    /// that transposed its axes, or that measured from the wrong corner, would produce a
    /// differently shaped result rather than an accidentally correct square.
    /// </remarks>
    public string WriteBorderedSourcePng(string fileName = "bordered.png") =>
        Workspace.CreateSourceFile(fileName, SyntheticImages.PngWithAlpha(
            12, 10, (x, y) => x is >= 3 and <= 7 && y is >= 2 and <= 6 ? (byte)255 : (byte)0));

    /// <summary>A PNG with an alpha channel in which nothing is visible — the manual-crop case.</summary>
    public string WriteFullyTransparentSourcePng(string fileName = "empty.png") =>
        Workspace.CreateSourceFile(fileName, SyntheticImages.PngWithAlpha(8, 8, (_, _) => 0));

    public void Dispose()
    {
        Workspace.Dispose();
        Database.Dispose();
    }
}
