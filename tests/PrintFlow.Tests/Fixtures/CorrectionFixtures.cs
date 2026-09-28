using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Imaging;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// Test doubles and builders for colleague correction (SCRUM-11148). Everything runs against the
/// harness's GUID-owned temporary workspace and database; only the importer's timing, the Meitu
/// call count and repository commit outcomes are scripted.
/// </summary>
internal static class CorrectionFixtures
{
    /// <summary>A session service with the real package store, as the application composes it.</summary>
    public static SessionService Service(
        SessionServiceHarness h,
        ISessionRepository? repository = null,
        IManualResultImporter? importer = null,
        IMeituProcessor? meitu = null) => new(
        WorkflowEngine.Instance,
        repository ?? h.Repository,
        h.FileWorkspace,
        h.RecycleBin,
        h.FileInspector,
        meitu ?? h.FakeMeitu,
        h.FakePhotoshop,
        h.Trim,
        h.ManualCrop,
        h.Preset,
        h.EnvironmentGate,
        SystemIdGenerator.Instance,
        h.Clock,
        manualResults: importer ?? new WicManualResultImporter(h.FileWorkspace, h.FileInspector),
        settings: h.Settings,
        diagnosticImages: (IDiagnosticImagePreviewDecoder)h.PreviewDecoder,
        automationLeases: h.AutomationLeases,
        correctionPackages: new FileCorrectionPackageStore(h.FileWorkspace));

    /// <summary>
    /// Imports a 6×5 source, confirms it, skips Enhancement, authorises and runs background removal
    /// with the fake cutout: background removal is under review with R current.
    /// </summary>
    /// <param name="opaqueSource">
    /// An opaque source, so the fake cutout (R) differs from its input (U); an alpha source is kept
    /// unchanged by the fake, which makes R and U the same bytes.
    /// </param>
    public static async Task<SessionView> AtBackgroundRemovalReviewAsync(
        SessionServiceHarness h, ISessionService service, bool opaqueSource = false)
    {
        string name = Guid.NewGuid().ToString("N")[..8] + ".png";
        string source = opaqueSource
            ? h.Workspace.CreateSourceFile(name, SyntheticImages.OpaqueRgbPng(6, 5, (x, y) => ((byte)(x * 30), (byte)(y * 40), 90)))
            : h.WriteSourcePng(name);
        var imported = await service.ImportAsync(WorkflowType.PrepareAsset, source, "logo", "qa", default);
        imported.IsSuccess.ShouldBeTrue(imported.IsFailure ? imported.Failure.ToString() : "");
        SessionId id = imported.Value.Id;
        await MustAsync(service.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "qa", default));
        await MustAsync(service.ExecuteAsync(id, new WorkflowCommand.Skip(StepKind.Enhancement), "qa", default));
        await SessionServiceHarness.AuthoriseBackgroundRemovalAsync(service, id);
        h.FakeMeitu.SetScenario(PrintFlow.Infrastructure.Adapters.Fake.FakeAdapterScenario.Succeed);
        SessionView review = await MustAsync(service.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.BackgroundRemoval), "qa", default));
        review.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        review.CurrentArtefact!.IsCurrentStepResult.ShouldBeTrue();
        return review;
    }

    /// <summary>Asks for correction of the result on screen with a fresh request id.</summary>
    public static Task<OperationResult<SessionView>> RequestAsync(
        ISessionService service, SessionView review, string? note = null, Guid? requestId = null) =>
        service.RequestColleagueCorrectionAsync(
            review.Id, requestId ?? Guid.NewGuid(), review.CurrentArtefact!.RevisionId, review.CurrentArtefact.Sha256,
            note, CorrectionFileNaming.English with { Instructions = "Reference {reference}; working {working}; return {return}." },
            "qa", default);

    /// <summary>A valid corrected cutout at the 6×5 canvas, distinct from R and U.</summary>
    public static string CorrectedPng(SessionServiceHarness h, string name = "corrected.png") =>
        h.Workspace.CreateSourceFile(name, SyntheticImages.PngWithAlpha(6, 5, (x, y) => (x + y) % 2 == 0 ? (byte)0 : (byte)200));

    public static async Task<SessionView> MustAsync(Task<OperationResult<SessionView>> pending)
    {
        OperationResult<SessionView> result = await pending;
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : "");
        return result.Value;
    }

    public static async Task<SessionAggregate> LoadAsync(SessionServiceHarness h, SessionId id) =>
        (await h.Repository.LoadAsync(id, default)).Value!;

    /// <summary>Takes the automation-lock row for another session, as a live foreign holder would.</summary>
    public static async Task<AutomationLockState> HoldLockForOtherSessionAsync(SessionServiceHarness h, ISessionService service)
    {
        var other = await service.ImportAsync(WorkflowType.PrepareAsset, h.WriteSourcePng("other-holder.png"), "other", "qa", default);
        other.IsSuccess.ShouldBeTrue();
        SessionAggregate holder = await LoadAsync(h, other.Value.Id);
        (await h.Repository.CommitAsync(SessionMutation.Empty(holder.Session) with
        {
            LockChange = new AutomationLockChange(AutomationLockAction.Acquire, other.Value.Id, h.Clock.GetUtcNow(), 424242, "OTHER-PC"),
        }, default)).IsSuccess.ShouldBeTrue();
        return (await h.Repository.GetAutomationLockAsync(default)).Value;
    }

    /// <summary>Takes the automation-lock row for environment verification.</summary>
    public static async Task<AutomationLockState> HoldLockForVerificationAsync(SessionServiceHarness h)
    {
        PrintFlow.Infrastructure.Verification.SqliteEnvironmentAutomationLock verification = new(h.Database.Factory, 101, "TEST");
        (await verification.TryAcquireAsync(h.Clock.GetUtcNow(), default)).IsSuccess.ShouldBeTrue();
        return (await h.Repository.GetAutomationLockAsync(default)).Value;
    }
}

/// <summary>Counts every call that would drive Meitu, delegating to the scriptable fake.</summary>
internal sealed class CountingMeitu(IMeituProcessor inner) : IMeituProcessor
{
    public int Calls { get; private set; }

    public string AdapterId => inner.AdapterId;

    public AdapterExecutionMode Mode => inner.Mode;

    public Task<OperationResult<AdapterOutput>> ProcessAsync(MeituRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        return inner.ProcessAsync(request, cancellationToken);
    }
}

/// <summary>
/// The real importer, held at the start of the import until the test releases it with a scripted
/// outcome. Stop does not reach it: exactly like the real importer, it sees only the caller's token,
/// so a Stop takes effect only if the import then fails (addendum S17).
/// </summary>
internal sealed class GatedImporter(SessionServiceHarness h) : IManualResultImporter
{
    private readonly WicManualResultImporter _real = new(h.FileWorkspace, h.FileInspector);
    private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes once an import is being held.</summary>
    public Task Started => _started.Task;

    /// <summary>Lets the held import finish: success imports for real, failure refuses.</summary>
    public void Release(bool succeed) => _release.TrySetResult(succeed);

    /// <summary>Scripts <see cref="HashSelectedAsync"/> to report different bytes (the "changed" case).</summary>
    public bool ReportChangedHash { get; set; }

    public Task<OperationResult<ManualResult>> ImportAsync(
        WorkspaceDirRef session, AttemptId attempt, StepKind step, FileFacts upstream, string selectedPath,
        CancellationToken cancellationToken) =>
        ImportAsync(session, attempt, step, upstream, selectedPath, null, cancellationToken);

    public async Task<OperationResult<ManualResult>> ImportAsync(
        WorkspaceDirRef session, AttemptId attempt, StepKind step, FileFacts upstream, string selectedPath,
        Sha256? expectedSelectedHash, CancellationToken cancellationToken)
    {
        _started.TrySetResult();
        bool succeed = await _release.Task;
        return succeed
            ? await _real.ImportAsync(session, attempt, step, upstream, selectedPath, expectedSelectedHash, cancellationToken)
            : OperationResult.Fail<ManualResult>(OperationFailure.Create(
                FailureCode.OutputValidationFailed, "Scripted late validation failure.", messageKey: "Failure_ManualResultInvalid"));
    }

    public Task<OperationResult<ManualResultPreflight>> PreflightAsync(
        StepKind step, FileFacts upstream, string selectedPath, CancellationToken cancellationToken) =>
        _real.PreflightAsync(step, upstream, selectedPath, cancellationToken);

    public async Task<OperationResult<Sha256>> HashSelectedAsync(string selectedPath, CancellationToken cancellationToken)
    {
        OperationResult<Sha256> real = await _real.HashSelectedAsync(selectedPath, cancellationToken);
        return ReportChangedHash && real.IsSuccess
            ? OperationResult.Ok(Sha256.FromBytes(new byte[32]))
            : real;
    }
}

/// <summary>
/// Records every committed mutation, and can fail commits from the n-th on (a crash that loses them)
/// or commit and then report failure (an unknown commit outcome).
/// </summary>
internal sealed class ScriptedRepository(ISessionRepository inner) : ISessionRepository
{
    private int _commits;

    public List<SessionMutation> Mutations { get; } = [];

    /// <summary>The 1-based commit that fails, and every commit after it, without landing.</summary>
    public int? FailFromCommit { get; set; }

    /// <summary>The 1-based commit that lands but is reported as failed.</summary>
    public int? LieAboutCommit { get; set; }

    /// <summary>T9 only: simulate loss of the opening change in memory after its real commit.</summary>
    public bool LoseInMemoryOpeningBinding { get; set; }

    public async Task<OperationResult<PrintFlow.Domain.Results.Unit>> CommitAsync(SessionMutation mutation, CancellationToken cancellationToken)
    {
        _commits++;
        if (FailFromCommit is int from && _commits >= from)
            return OperationResult.Fail<PrintFlow.Domain.Results.Unit>(FailureCode.PersistenceError, $"Simulated crash: commit {_commits} never landed.");
        Mutations.Add(mutation with { });
        OperationResult<PrintFlow.Domain.Results.Unit> committed = await inner.CommitAsync(mutation, cancellationToken);
        if (committed.IsSuccess && LoseInMemoryOpeningBinding &&
            mutation.CorrectionRequestChanges.Any(change => change is CorrectionRequestChange.SetLastImportAttempt))
        {
            // Deliberately break an internal invariant without a production seam or changing SQLite.
            // A persisted-only mutation would test assertion rollback, not the live fail-closed path.
            typeof(SessionMutation).GetProperty(nameof(SessionMutation.CorrectionRequestChanges))!
                .SetValue(mutation, Array.Empty<CorrectionRequestChange>());
        }
        return LieAboutCommit == _commits && committed.IsSuccess
            ? OperationResult.Fail<PrintFlow.Domain.Results.Unit>(FailureCode.PersistenceError, "Simulated lost acknowledgement.")
            : committed;
    }

    public Task<OperationResult<IReadOnlyList<SessionId>>> FindRecoveryCandidatesAsync(CancellationToken cancellationToken) =>
        inner.FindRecoveryCandidatesAsync(cancellationToken);

    public Task<OperationResult<SessionAggregate?>> LoadAsync(SessionId id, CancellationToken cancellationToken) =>
        inner.LoadAsync(id, cancellationToken);

    public Task<OperationResult<IReadOnlyList<SessionListItem>>> ListRecentAsync(
        int maxCount, DateTimeOffset since, CancellationToken cancellationToken) =>
        inner.ListRecentAsync(maxCount, since, cancellationToken);

    public Task<OperationResult<IReadOnlyList<ProcessingAttempt>>> FindRunningAttemptsAsync(CancellationToken cancellationToken) =>
        inner.FindRunningAttemptsAsync(cancellationToken);

    public Task<OperationResult<IReadOnlyList<SessionId>>> FindCompletedSessionsAsync(CancellationToken cancellationToken) =>
        inner.FindCompletedSessionsAsync(cancellationToken);

    public Task<OperationResult<AutomationLockState>> GetAutomationLockAsync(CancellationToken cancellationToken) =>
        inner.GetAutomationLockAsync(cancellationToken);
}
