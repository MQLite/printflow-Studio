using System.IO;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Delivery;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Delivery;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// SCRUM-11145 synthetic setup through the real session service, SQLite journal and the
/// implemented Windows NTFS delivery adapter. Every folder is GUID-owned under the temp root.
/// </summary>
internal static class FinalSaveFixtures
{
    public static IReadOnlyList<string> ProtectedRoots(SessionServiceHarness h) =>
        [h.Workspace.Root, Path.GetDirectoryName(h.Database.Path)!, Path.Combine(h.Workspace.Root, "Evidence")];

    public static ApprovedArtifactDeliveryService Delivery(SessionServiceHarness h, IDeliveryFileSystem? files = null) =>
        new(h.Repository, new SqliteDeliveryRepository(h.Database.Factory), h.FileWorkspace,
            files ?? new WindowsDeliveryFileSystem(), ProtectedRoots(h), h.Clock, h.TiffReviewDecoder);

    public static string NewFolder(string label)
    {
        string folder = Path.Combine(Path.GetTempPath(), $"pf-11145-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    public static void Remove(params string[] folders)
    {
        foreach (string folder in folders)
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    /// <summary>A PrepareAsset session whose deterministic Trim result waits for final review.</summary>
    public static async Task<SessionView> PngAtFinalReviewAsync(SessionServiceHarness h, ISessionService workflow)
    {
        SessionId id = await Integration.Persistence.KeepOriginalExtentPersistenceTests.AtTrim(h, workflow, WorkflowType.PrepareAsset);
        OperationResult<SessionView> trim = await workflow.ExecuteAsync(id, new WorkflowCommand.StartStep(StepKind.Trim),
            "tester", CancellationToken.None);
        trim.IsSuccess.ShouldBeTrue(trim.IsFailure ? trim.Failure.TechnicalDetail : null);
        trim.Value.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        return trim.Value;
    }

    /// <summary>A print-TIFF session whose synthetic production TIFF waits for final review.</summary>
    public static async Task<SessionView> TiffAtFinalReviewAsync(SessionServiceHarness h, ISessionService workflow,
        int widthMm = 200, string outputName = "final-tiff")
    {
        SessionId id = (await workflow.ImportAsync(WorkflowType.GeneratePrintTiff, h.WriteSourcePng(),
            outputName, "tester", CancellationToken.None)).Value.Id;
        (await workflow.ExecuteAsync(id, new WorkflowCommand.ConfirmOriginal(), "tester", CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        return await NextTiffSizeAsync(workflow, id, widthMm);
    }

    public static async Task<SessionView> NextTiffSizeAsync(ISessionService workflow, SessionId id, int widthMm)
    {
        (await workflow.ExecuteAsync(id, new WorkflowCommand.SetPrintDimensions(
            PrintDimensions.FromMillimetres(widthMm, 150, SizePreset.Custom)), "tester", CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
        (await workflow.ExecuteAsync(id, new WorkflowCommand.SelectWhiteUnderbaseBranch(
            WhiteUnderbaseBranch.W1_1px, "ordinary design"), "tester", CancellationToken.None)).IsSuccess.ShouldBeTrue();
        OperationResult<SessionView> run = await workflow.ExecuteAsync(id,
            new WorkflowCommand.StartStep(StepKind.PhotoshopOutput), "tester", CancellationToken.None);
        run.IsSuccess.ShouldBeTrue(run.IsFailure ? run.Failure.TechnicalDetail : null);
        run.Value.CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        return run.Value;
    }

    public static ISessionService TiffService(SessionServiceHarness h) =>
        h.CreateService(photoshop: new SyntheticProductionTiffProcessor(h.FileWorkspace));

    public static ReviewedResultIdentity Reviewed(SessionView view) =>
        new(view.Id, view.CurrentStep!.Step, view.CurrentArtefact!.RevisionId, view.CurrentArtefact.Sha256);

    public static FinalSaveRequest Confirm(SessionView view, string folder, string fileName) =>
        new(Guid.NewGuid(), Guid.NewGuid(), view.Id,
            view.CurrentStep!.Step == StepKind.Trim ? ArtifactKind.ApprovedAssetPng : ArtifactKind.ApprovedPrintTiff,
            Reviewed(view), null, view.CurrentArtefact!.Sha256, folder, fileName, 1);

    public static FinalSaveRequest SaveApproved(ArtifactKey key, string folder, string fileName, Guid? requestId = null) =>
        new(Guid.NewGuid(), requestId ?? Guid.NewGuid(), key.SessionId, key.Kind, null, key, null, folder, fileName, 1);

    public static async Task<SessionAggregate> LoadAsync(SessionServiceHarness h, SessionId id) =>
        (await h.Repository.LoadAsync(id, CancellationToken.None)).Value!;

    /// <summary>Counts every durable fact a retry must never add: reviews, attempts and promotions.</summary>
    public static async Task<(int Reviews, int Attempts, int Promotions)> WorkCountsAsync(SessionServiceHarness h, SessionId id)
    {
        SessionAggregate aggregate = await LoadAsync(h, id);
        return (aggregate.Reviews.Count, aggregate.Attempts.Count,
            aggregate.Revisions.Count(r => r.Operation == OperationKind.PromoteApproved));
    }
}

/// <summary>Delegates to a real session service; lets a test fail or lose an approval acknowledgement.</summary>
public sealed class ScriptedApprovalSessionService(ISessionService inner) : ISessionService
{
    public enum Mode { Normal, RefuseWithoutCommit, CommitThenReportFailure, RefuseAndFailReload }

    public Mode Approval { get; set; }
    public int ApproveCalls { get; private set; }
    public int PromoteCalls { get; private set; }
    public bool FailPromotion { get; set; }
    private bool _failLoads;

    /// <summary>Every later session load fails, as when the database stops answering.</summary>
    public bool FailLoads { get => _failLoads; set => _failLoads = value; }

    public async Task<OperationResult<SessionView>> ApproveExactReviewAsync(SessionId id, StepKind step, RevisionId revision,
        Sha256 reviewedHash, string? operatorName, CancellationToken cancellationToken)
    {
        ApproveCalls++;
        switch (Approval)
        {
            case Mode.RefuseWithoutCommit:
                return OperationResult.Fail<SessionView>(FailureCode.PersistenceError, "scripted refusal");
            case Mode.RefuseAndFailReload:
                _failLoads = true;
                return OperationResult.Fail<SessionView>(FailureCode.PersistenceError, "scripted unknown commit");
            case Mode.CommitThenReportFailure:
                OperationResult<SessionView> committed = await inner.ApproveExactReviewAsync(id, step, revision, reviewedHash,
                    operatorName, cancellationToken);
                committed.IsSuccess.ShouldBeTrue();
                return OperationResult.Fail<SessionView>(FailureCode.PersistenceError, "scripted lost acknowledgement");
            default:
                return await inner.ApproveExactReviewAsync(id, step, revision, reviewedHash, operatorName, cancellationToken);
        }
    }

    public Task<OperationResult<SessionView>> PromoteReviewedPngAsync(SessionId id, RevisionId reviewedRevision,
        Sha256 reviewedHash, string? operatorName, CancellationToken cancellationToken)
    {
        PromoteCalls++;
        if (FailPromotion)
            return Task.FromResult(OperationResult.Fail<SessionView>(FailureCode.PersistenceError, "scripted preparation failure"));
        return inner.PromoteReviewedPngAsync(id, reviewedRevision, reviewedHash, operatorName, cancellationToken);
    }

    public Task<OperationResult<SessionView>> LoadAsync(SessionId id, CancellationToken cancellationToken) =>
        _failLoads ? Task.FromResult(OperationResult.Fail<SessionView>(FailureCode.PersistenceError, "scripted reload failure"))
            : inner.LoadAsync(id, cancellationToken);

    public Task<OperationResult<IReadOnlyList<RecoveryItem>>> ListRecoveryAsync(CancellationToken cancellationToken) =>
        inner.ListRecoveryAsync(cancellationToken);
    public Task<OperationResult<SessionView>> ResolveRecoveryAsync(SessionId id, RecoveryAction action, string? selectedPath,
        string? operatorName, CancellationToken cancellationToken) =>
        inner.ResolveRecoveryAsync(id, action, selectedPath, operatorName, cancellationToken);
    public Task<OperationResult<SessionView>> ImportAsync(WorkflowType workflowType, string sourceAbsolutePath,
        string? outputName, string? operatorName, CancellationToken cancellationToken) =>
        inner.ImportAsync(workflowType, sourceAbsolutePath, outputName, operatorName, cancellationToken);
    public Task<OperationResult<SessionView>> ExecuteAsync(SessionId id, WorkflowCommand command, string? operatorName,
        CancellationToken cancellationToken) => inner.ExecuteAsync(id, command, operatorName, cancellationToken);
    public Task<OperationResult<ErrorDetailsView>> LoadErrorDetailsAsync(SessionId sessionId, AttemptId attemptId,
        CancellationToken cancellationToken) => inner.LoadErrorDetailsAsync(sessionId, attemptId, cancellationToken);
    public Task<OperationResult<SessionView>> ResolveErrorRecoveryAsync(SessionId sessionId, AttemptId attemptId,
        ErrorRecoveryAction action, string? operatorName, CancellationToken cancellationToken) =>
        inner.ResolveErrorRecoveryAsync(sessionId, attemptId, action, operatorName, cancellationToken);
    public Task<OperationResult<SessionView>> AuthoriseCurrentEnlargementAsync(SessionId id, Guid enlargementOfferId,
        string? operatorName, CancellationToken cancellationToken) =>
        inner.AuthoriseCurrentEnlargementAsync(id, enlargementOfferId, operatorName, cancellationToken);
    public Task<OperationResult<IReadOnlyList<SessionListItem>>> ListRecentAsync(CancellationToken cancellationToken) =>
        inner.ListRecentAsync(cancellationToken);
    public Task<OperationResult<PrintFlow.Domain.Results.Unit>> RemoveFromRecentAsync(SessionId id, CancellationToken cancellationToken) =>
        inner.RemoveFromRecentAsync(id, cancellationToken);
    public AutomationRuntimeView GetAutomationRuntime(SessionId id) => inner.GetAutomationRuntime(id);
    public event EventHandler<AutomationRuntimeView>? AutomationRuntimeChanged
    {
        add => inner.AutomationRuntimeChanged += value;
        remove => inner.AutomationRuntimeChanged -= value;
    }
    public OperationResult<PrintFlow.Domain.Results.Unit> RequestStop(SessionId id, AutomationStopMode mode) => inner.RequestStop(id, mode);
}

/// <summary>Delegates to the real delivery service while counting, gating or failing selected calls.</summary>
internal sealed class ObservedDeliveryService(IApprovedArtifactDeliveryService inner) : IApprovedArtifactDeliveryService
{
    public int DeliverCalls { get; private set; }
    public int ReplaceCalls { get; private set; }
    public int ReconcileCalls { get; private set; }
    public bool FailHistory { get; set; }
    public bool ThrowOnReconcile { get; set; }
    public TaskCompletionSource? HoldReconcile { get; set; }
    public TaskCompletionSource ReconcileEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource? HoldDelivery { get; set; }
    public TaskCompletionSource DeliveryEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool ThrowOnDeliver { get; set; }
    /// <summary>The progress sink each delivery was given, in call order; a test can report through an old one late.</summary>
    public List<IProgress<DeliveryProgress>?> DeliverySinks { get; } = [];
    public SemaphoreSlim DeliveryStarted { get; } = new(0);

    public Task<OperationResult<IReadOnlyList<DeliveryState>>> GetDeliveryStateAsync(SessionId sessionId,
        ArtifactKey? artifact, CancellationToken cancellationToken) => FailHistory
        ? Task.FromResult(OperationResult.Fail<IReadOnlyList<DeliveryState>>(FailureCode.PersistenceError, "scripted history failure"))
        : inner.GetDeliveryStateAsync(sessionId, artifact, cancellationToken);
    public Task<(DeliveryOffer? Offer, ArtifactRefusal Refusal)> GetOfferAsync(ArtifactKey artifact, CancellationToken cancellationToken) =>
        inner.GetOfferAsync(artifact, cancellationToken);
    public async Task<DeliveryOutcome> DeliverAsync(DeliveryRequest request, IProgress<DeliveryProgress>? progress,
        CancellationToken cancellationToken)
    {
        DeliverCalls++;
        lock (DeliverySinks) DeliverySinks.Add(progress);
        DeliveryEntered.TrySetResult();
        DeliveryStarted.Release();
        if (HoldDelivery is { } hold) await hold.Task;
        if (ThrowOnDeliver) throw new IOException("scripted delivery failure");
        return await inner.DeliverAsync(request, progress, cancellationToken);
    }
    public async Task<DeliveryOutcome> ReconcileAsync(Guid deliveryId, IProgress<DeliveryProgress>? progress, CancellationToken cancellationToken)
    {
        ReconcileCalls++;
        ReconcileEntered.TrySetResult();
        if (HoldReconcile is { } hold) await hold.Task;
        if (ThrowOnReconcile) throw new IOException("scripted reconcile failure");
        return await inner.ReconcileAsync(deliveryId, progress, cancellationToken);
    }
    public Task<DeliveryFileObservation> CheckDeliveredFileAsync(Guid deliveryId, CancellationToken cancellationToken) =>
        inner.CheckDeliveredFileAsync(deliveryId, cancellationToken);
    public Task<DeliveryOutcome> ReplaceMissingAsync(MissingDeliveryReplacementRequest request, IProgress<DeliveryProgress>? progress,
        CancellationToken cancellationToken)
    {
        ReplaceCalls++;
        return inner.ReplaceMissingAsync(request, progress, cancellationToken);
    }
    public Task<SelectionLeaseResult> AcquireDeliveredSelectionAsync(Guid deliveryId, CancellationToken cancellationToken) =>
        inner.AcquireDeliveredSelectionAsync(deliveryId, cancellationToken);
    public Task<OperationResult<DeliveryDestinationPreferenceView?>> GetLastSuccessfulDestinationAsync(CancellationToken cancellationToken) =>
        inner.GetLastSuccessfulDestinationAsync(cancellationToken);
    public DeliveryDraftCheck CheckDraft(string folder, string fileName, ArtifactKind kind) => inner.CheckDraft(folder, fileName, kind);
}

/// <summary>Records what the shell port was handed and whether the lease was still live at dispatch.</summary>
internal sealed class RecordingDeliveredFileShell : IDeliveredFileShell
{
    public List<(string Path, bool LeaseLive)> Dispatches { get; } = [];
    public IDeliveredSelectionLease? LastLease { get; private set; }
    public bool Throw { get; set; }

    public ShellDispatchResult SelectInFolder(IDeliveredSelectionLease lease)
    {
        LastLease = lease;
        Dispatches.Add((lease.FinalPath, !lease.IsDisposed));
        if (Throw) throw new InvalidOperationException("scripted shell failure");
        return new(true);
    }
}

/// <summary>
/// Wraps the real Windows adapter: the first no-replace publication really happens, but its
/// acknowledgement is reported as lost, leaving a complete but unrecorded final file.
/// </summary>
internal sealed class LostPublicationAckFileSystem(IDeliveryFileSystem inner) : IDeliveryFileSystem
{
    private int _remaining = 1;

    public DeliveryFileResult<IDeliveryFileGuard> Open(string sourcePath, Sha256 expectedSourceHash, long expectedSourceLength,
        string requestedFolder, string finalLeaf, ArtifactKind kind, IReadOnlyList<string> protectedRoots, string? protectedOriginalPath)
    {
        DeliveryFileResult<IDeliveryFileGuard> opened = inner.Open(sourcePath, expectedSourceHash, expectedSourceLength,
            requestedFolder, finalLeaf, kind, protectedRoots, protectedOriginalPath);
        return opened.IsSuccess ? DeliveryFileResult<IDeliveryFileGuard>.Ok(new Guard(opened.Value!, this)) : opened;
    }

    public DeliveryFileResult<string> ValidateLeaf(string candidate, ArtifactKind kind) => inner.ValidateLeaf(candidate, kind);
    public DeliveryFileResult<string> CheckFolder(string folder, IReadOnlyList<string> protectedRoots) => inner.CheckFolder(folder, protectedRoots);
    public string? SuggestAlternative(string leaf, Func<string, bool> exists) => inner.SuggestAlternative(leaf, exists);

    private sealed class Guard(IDeliveryFileGuard inner, LostPublicationAckFileSystem owner) : IDeliveryFileGuard
    {
        public string ResolvedFolder => inner.ResolvedFolder;
        public string FinalPath => inner.FinalPath;
        public string VolumeId => inner.VolumeId;
        public string DirectoryId => inner.DirectoryId;
        public string DestinationKey => inner.DestinationKey;
        public DeliveryFileIdentity SourceIdentity => inner.SourceIdentity;
        public DeliveryFileResult<bool> VerifySource() => inner.VerifySource();
        public DeliveryFinalCheck CheckFinal(DeliveryFileIdentity? expectedIdentity = null) => inner.CheckFinal(expectedIdentity);
        public DeliveryFileResult<DeliveryFileIdentity> CreateStaging(string stagingLeaf) => inner.CreateStaging(stagingLeaf);
        public DeliveryFileResult<DeliveryFileIdentity> OpenOwnedStaging(string stagingLeaf, DeliveryFileIdentity expectedIdentity) =>
            inner.OpenOwnedStaging(stagingLeaf, expectedIdentity);
        public Task<DeliveryFileResult<bool>> CopyAndVerifyStageAsync(Sha256 expectedHash, long expectedLength,
            IProgress<long>? copied, CancellationToken cancellationToken) =>
            inner.CopyAndVerifyStageAsync(expectedHash, expectedLength, copied, cancellationToken);
        public DeliveryFileResult<bool> VerifyStage(Sha256 expectedHash, long expectedLength) => inner.VerifyStage(expectedHash, expectedLength);
        public DeliveryFileResult<bool> PublishNoReplace(string stagingLeaf, DeliveryFileIdentity expectedIdentity)
        {
            DeliveryFileResult<bool> published = inner.PublishNoReplace(stagingLeaf, expectedIdentity);
            return published.IsSuccess && Interlocked.Exchange(ref owner._remaining, 0) == 1
                ? DeliveryFileResult<bool>.Fail(DeliveryCode.DestinationUnavailable, "scripted lost publication acknowledgement")
                : published;
        }
        public DeliveryFileResult<bool> DeleteHeldStaging(DeliveryFileIdentity expectedIdentity) => inner.DeleteHeldStaging(expectedIdentity);
        public SelectionLeaseResult AcquireSelection(Guid deliveryId, DeliveryFileIdentity expectedIdentity, Sha256 expectedHash,
            long expectedLength) => inner.AcquireSelection(deliveryId, expectedIdentity, expectedHash, expectedLength);
        public void Dispose() => inner.Dispose();
    }
}

/// <summary>
/// Wraps the real Windows adapter. The first publication is refused before anything is renamed,
/// leaving a verified staged copy and an unresolved attempt; on the next publication another
/// program creates the final name first, so the real no-replace rename meets a collision.
/// </summary>
internal sealed class RacedPublicationFileSystem(IDeliveryFileSystem inner) : IDeliveryFileSystem
{
    private int _publications;

    public byte[] ForeignBytes { get; } = [9, 9, 9];

    public DeliveryFileResult<IDeliveryFileGuard> Open(string sourcePath, Sha256 expectedSourceHash, long expectedSourceLength,
        string requestedFolder, string finalLeaf, ArtifactKind kind, IReadOnlyList<string> protectedRoots, string? protectedOriginalPath)
    {
        DeliveryFileResult<IDeliveryFileGuard> opened = inner.Open(sourcePath, expectedSourceHash, expectedSourceLength,
            requestedFolder, finalLeaf, kind, protectedRoots, protectedOriginalPath);
        return opened.IsSuccess ? DeliveryFileResult<IDeliveryFileGuard>.Ok(new Guard(opened.Value!, this)) : opened;
    }

    public DeliveryFileResult<string> ValidateLeaf(string candidate, ArtifactKind kind) => inner.ValidateLeaf(candidate, kind);
    public DeliveryFileResult<string> CheckFolder(string folder, IReadOnlyList<string> protectedRoots) => inner.CheckFolder(folder, protectedRoots);
    public string? SuggestAlternative(string leaf, Func<string, bool> exists) => inner.SuggestAlternative(leaf, exists);

    private sealed class Guard(IDeliveryFileGuard inner, RacedPublicationFileSystem owner) : IDeliveryFileGuard
    {
        public string ResolvedFolder => inner.ResolvedFolder;
        public string FinalPath => inner.FinalPath;
        public string VolumeId => inner.VolumeId;
        public string DirectoryId => inner.DirectoryId;
        public string DestinationKey => inner.DestinationKey;
        public DeliveryFileIdentity SourceIdentity => inner.SourceIdentity;
        public DeliveryFileResult<bool> VerifySource() => inner.VerifySource();
        public DeliveryFinalCheck CheckFinal(DeliveryFileIdentity? expectedIdentity = null) => inner.CheckFinal(expectedIdentity);
        public DeliveryFileResult<DeliveryFileIdentity> CreateStaging(string stagingLeaf) => inner.CreateStaging(stagingLeaf);
        public DeliveryFileResult<DeliveryFileIdentity> OpenOwnedStaging(string stagingLeaf, DeliveryFileIdentity expectedIdentity) =>
            inner.OpenOwnedStaging(stagingLeaf, expectedIdentity);
        public Task<DeliveryFileResult<bool>> CopyAndVerifyStageAsync(Sha256 expectedHash, long expectedLength,
            IProgress<long>? copied, CancellationToken cancellationToken) =>
            inner.CopyAndVerifyStageAsync(expectedHash, expectedLength, copied, cancellationToken);
        public DeliveryFileResult<bool> VerifyStage(Sha256 expectedHash, long expectedLength) => inner.VerifyStage(expectedHash, expectedLength);
        public DeliveryFileResult<bool> PublishNoReplace(string stagingLeaf, DeliveryFileIdentity expectedIdentity)
        {
            if (Interlocked.Increment(ref owner._publications) == 1)
                return DeliveryFileResult<bool>.Fail(DeliveryCode.DestinationUnavailable, "scripted: destination went away before the rename");
            File.WriteAllBytes(inner.FinalPath, owner.ForeignBytes);
            return inner.PublishNoReplace(stagingLeaf, expectedIdentity);
        }
        public DeliveryFileResult<bool> DeleteHeldStaging(DeliveryFileIdentity expectedIdentity) => inner.DeleteHeldStaging(expectedIdentity);
        public SelectionLeaseResult AcquireSelection(Guid deliveryId, DeliveryFileIdentity expectedIdentity, Sha256 expectedHash,
            long expectedLength) => inner.AcquireSelection(deliveryId, expectedIdentity, expectedHash, expectedLength);
        public void Dispose() => inner.Dispose();
    }
}

/// <summary>
/// Wraps the real Windows adapter and scripts the staged copy of the first attempt only: an I/O
/// failure, or a copy that waits until the operation is cancelled. Later attempts copy normally.
/// </summary>
public sealed class ScriptedCopyFileSystem(IDeliveryFileSystem inner, ScriptedCopyFileSystem.Mode mode) : IDeliveryFileSystem
{
    public enum Mode { FailOnce, HoldUntilCancelledOnce }

    private int _remaining = 1;
    public TaskCompletionSource CopyEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public DeliveryFileResult<IDeliveryFileGuard> Open(string sourcePath, Sha256 expectedSourceHash, long expectedSourceLength,
        string requestedFolder, string finalLeaf, ArtifactKind kind, IReadOnlyList<string> protectedRoots, string? protectedOriginalPath)
    {
        DeliveryFileResult<IDeliveryFileGuard> opened = inner.Open(sourcePath, expectedSourceHash, expectedSourceLength,
            requestedFolder, finalLeaf, kind, protectedRoots, protectedOriginalPath);
        return opened.IsSuccess ? DeliveryFileResult<IDeliveryFileGuard>.Ok(new Guard(opened.Value!, this)) : opened;
    }

    public DeliveryFileResult<string> ValidateLeaf(string candidate, ArtifactKind kind) => inner.ValidateLeaf(candidate, kind);
    public DeliveryFileResult<string> CheckFolder(string folder, IReadOnlyList<string> protectedRoots) => inner.CheckFolder(folder, protectedRoots);
    public string? SuggestAlternative(string leaf, Func<string, bool> exists) => inner.SuggestAlternative(leaf, exists);

    private sealed class Guard(IDeliveryFileGuard inner, ScriptedCopyFileSystem owner) : IDeliveryFileGuard
    {
        public string ResolvedFolder => inner.ResolvedFolder;
        public string FinalPath => inner.FinalPath;
        public string VolumeId => inner.VolumeId;
        public string DirectoryId => inner.DirectoryId;
        public string DestinationKey => inner.DestinationKey;
        public DeliveryFileIdentity SourceIdentity => inner.SourceIdentity;
        public DeliveryFileResult<bool> VerifySource() => inner.VerifySource();
        public DeliveryFinalCheck CheckFinal(DeliveryFileIdentity? expectedIdentity = null) => inner.CheckFinal(expectedIdentity);
        public DeliveryFileResult<DeliveryFileIdentity> CreateStaging(string stagingLeaf) => inner.CreateStaging(stagingLeaf);
        public DeliveryFileResult<DeliveryFileIdentity> OpenOwnedStaging(string stagingLeaf, DeliveryFileIdentity expectedIdentity) =>
            inner.OpenOwnedStaging(stagingLeaf, expectedIdentity);
        public async Task<DeliveryFileResult<bool>> CopyAndVerifyStageAsync(Sha256 expectedHash, long expectedLength,
            IProgress<long>? copied, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref owner._remaining, 0) == 1)
            {
                owner.CopyEntered.TrySetResult();
                copied?.Report(0);
                if (owner._mode == Mode.FailOnce)
                    return DeliveryFileResult<bool>.Fail(DeliveryCode.CopyFailed, "scripted copy I/O failure");
                try { await Task.Delay(Timeout.Infinite, cancellationToken); }
                catch (OperationCanceledException) { }
                return DeliveryFileResult<bool>.Fail(DeliveryCode.Cancelled, "scripted cancellation during copy");
            }
            return await inner.CopyAndVerifyStageAsync(expectedHash, expectedLength, copied, cancellationToken);
        }
        public DeliveryFileResult<bool> VerifyStage(Sha256 expectedHash, long expectedLength) => inner.VerifyStage(expectedHash, expectedLength);
        public DeliveryFileResult<bool> PublishNoReplace(string stagingLeaf, DeliveryFileIdentity expectedIdentity) =>
            inner.PublishNoReplace(stagingLeaf, expectedIdentity);
        public DeliveryFileResult<bool> DeleteHeldStaging(DeliveryFileIdentity expectedIdentity) => inner.DeleteHeldStaging(expectedIdentity);
        public SelectionLeaseResult AcquireSelection(Guid deliveryId, DeliveryFileIdentity expectedIdentity, Sha256 expectedHash,
            long expectedLength) => inner.AcquireSelection(deliveryId, expectedIdentity, expectedHash, expectedLength);
        public void Dispose() => inner.Dispose();
    }

    private readonly Mode _mode = mode;
}
