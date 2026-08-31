using System.IO;
using Microsoft.Extensions.Time.Testing;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Infrastructure.Gate;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Infrastructure.Preset;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Workflow.Commands;
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
    /// The recording stand-in for the Windows Recycle Bin every service built here uses
    /// (Epic 11400 Part C2B §30). A test asserts on <see cref="FakeRecycleBin.Recycled"/>, or sets
    /// <see cref="FakeRecycleBin.FailsWith"/> to prove a failed disposal records no rejection.
    /// </summary>
    public FakeRecycleBin RecycleBin { get; }

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

    /// <summary>
    /// The real WIC manual-crop processor, never a double: Epic 11200 Part C2 §27 requires the
    /// integration flow to produce an actual cropped file whose pixels can be read back.
    /// </summary>
    public IManualCropProcessor ManualCrop { get; }

    /// <summary>
    /// The real WIC preview decoder, for the same reason Trim is real: a doubled decoder would
    /// prove nothing about whether an operator can actually see the file (Epic 11200 Part C1 §22).
    /// </summary>
    public IImagePreviewDecoder PreviewDecoder { get; }

    /// <summary>The read-only image seam over the same workspace and database.</summary>
    public IArtefactPreviewService Previews { get; }

    public SessionServiceHarness()
    {
        Workspace = new TempWorkspace();
        Database = new TempDatabase();
        Clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero));
        FileWorkspace = new FileWorkspace(Workspace.Root);
        RecycleBin = new FakeRecycleBin(Path.Combine(Workspace.Root, "RecycleBin"));

        (string presetPath, Sha256 hash) = PresetFixture.Write(Workspace.Root);
        Preset = new WorkstationPresetProvider(presetPath, PresetFixture.PresetId, PresetFixture.PresetVersion, hash);

        Repository = new SqliteSessionRepository(Database.Factory);
        FakeMeitu = new FakeMeituProcessor(FileWorkspace);
        FakePhotoshop = new FakePhotoshopOutputProcessor(FileWorkspace);
        Trim = new DeterministicAlphaTrimProcessor(FileWorkspace);
        ManualCrop = new WicManualCropProcessor(FileWorkspace);
        PreviewDecoder = new WicImagePreviewDecoder(FileWorkspace);
        Previews = new ArtefactPreviewService(Repository, PreviewDecoder);
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
    /// <param name="preset">
    /// A preset provider in place of the fixture-backed one, for tests about what the workflow
    /// does with naming patterns it cannot render (naming-contract fix §6).
    /// </param>
    /// <param name="workspace">
    /// A workspace in place of the real one, for the deterministic promotion-failure and
    /// crash-point tests (Epic 11400 Part C2B §32, §33). Every other test keeps the real
    /// <see cref="Infrastructure.Workspace.FileWorkspace"/>.
    /// </param>
    /// <param name="repository">
    /// A repository in place of the real one, so a test can fail exactly the commit an approval
    /// or a rejection would have crashed before (§33, §34).
    /// </param>
    public ISessionService CreateService(
        IWorkstationPresetProvider? preset = null,
        IWorkspace? workspace = null,
        ISessionRepository? repository = null) => new SessionService(
        WorkflowEngine.Instance,
        repository ?? Repository,
        workspace ?? FileWorkspace,
        RecycleBin,
        FileInspector,
        FakeMeitu,
        FakePhotoshop,
        Trim,
        ManualCrop,
        preset ?? Preset,
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
    public ISessionService CreateServiceWithMeitu(
        IMeituProcessor meitu, IWorkstationPresetProvider? preset = null) => new SessionService(
        WorkflowEngine.Instance,
        Repository,
        FileWorkspace,
        RecycleBin,
        FileInspector,
        meitu,
        FakePhotoshop,
        Trim,
        ManualCrop,
        preset ?? Preset,
        EnvironmentGate,
        SystemIdGenerator.Instance,
        Clock);

    /// <summary>
    /// Builds a service with a caller-supplied Photoshop adapter, and optionally a caller-supplied
    /// gate (Epic 11400 Part C2A §26).
    /// </summary>
    /// <remarks>
    /// The gate parameter is what makes the controlled production seam possible without touching
    /// the application's own composition, and it is deliberately a parameter rather than a
    /// mutable property on the harness: a caller has to state, at the call site, that it is
    /// supplying its own gate. Every other test keeps the real
    /// <see cref="FoundationEnvironmentGate"/>, which still refuses every Production adapter.
    /// </remarks>
    public ISessionService CreateServiceWithPhotoshop(
        IPhotoshopOutputProcessor photoshop,
        IEnvironmentGate? environmentGate = null,
        IWorkstationPresetProvider? preset = null) => new SessionService(
        WorkflowEngine.Instance,
        Repository,
        FileWorkspace,
        RecycleBin,
        FileInspector,
        FakeMeitu,
        photoshop,
        Trim,
        ManualCrop,
        preset ?? Preset,
        environmentGate ?? EnvironmentGate,
        SystemIdGenerator.Instance,
        Clock);

    /// <summary>
    /// Records the reviewed-content authority for whatever Background Removal is currently about
    /// to consume (Epic 11300 Part C2B1 §5).
    /// </summary>
    /// <remarks>
    /// Built from <c>SessionView.CurrentArtefact</c> — the artefact the read model says is on
    /// screen — rather than from a revision the test dug out of the database, because that is
    /// exactly what the operator UI in C2B2 will have to hand. Authorising anything else would be
    /// testing a path no screen can take.
    /// <para>
    /// Tests that mean to prove stale or mismatched authority is refused construct the command
    /// themselves with the id and hash they intend.
    /// </para>
    /// </remarks>
    public static async Task AuthoriseBackgroundRemovalAsync(ISessionService service, SessionId id)
    {
        OperationResult<SessionView> loaded = await service.LoadAsync(id, CancellationToken.None);
        loaded.IsSuccess.ShouldBeTrue(loaded.IsFailure ? loaded.Failure.ToString() : "");

        ArtefactView reviewed = loaded.Value.CurrentArtefact
            ?? throw new InvalidOperationException("There is no artefact on screen to authorise.");

        OperationResult<SessionView> decided = await service.ExecuteAsync(
            id,
            new WorkflowCommand.SetBackgroundRemovalDecision(
                BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
                reviewed.RevisionId,
                reviewed.Sha256),
            "tester",
            CancellationToken.None);

        decided.IsSuccess.ShouldBeTrue(decided.IsFailure ? decided.Failure.ToString() : "");
    }

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

    /// <summary>
    /// A 12×10 opaque PNG with no alpha channel at all — the primary manual-crop scenario.
    /// </summary>
    /// <remarks>
    /// The honest version of "the operator brought a photo". <c>Bgr24</c> carries no alpha, so
    /// the deterministic trim reports <c>ManualCropRequired</c> rather than guessing a
    /// background from colour, and the only way forward is a human-drawn rectangle
    /// (Epic 11200 Part C2 §28). The colour is a per-pixel gradient so a crop that took the
    /// wrong rectangle is detectable pixel by pixel.
    /// </remarks>
    public string WriteOpaqueSourcePng(string fileName = "opaque.png") =>
        Workspace.CreateSourceFile(fileName, SyntheticImages.OpaqueRgbPng(
            12, 10, (x, y) => ((byte)(x * 20), (byte)(y * 25), (byte)((x + y) * 10))));

    public void Dispose()
    {
        Workspace.Dispose();
        Database.Dispose();
    }
}
