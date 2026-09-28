using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.Delivery;

public enum DeliveryCode
{
    Delivered, AlreadyDelivered, NotDelivered, Cancelled, Collision, NeedsReconciliation,
    ApprovalEvidenceMissing, IneligibleArtifact, SourceMissing, SourceChanged,
    InvalidName, ProtectedDestination, UnsupportedDestination, DestinationUnavailable,
    DestinationNotWritable, CopyFailed, VerificationFailed, PersistenceFailed,
    RequestConflict, DeliveredFileMissing, DeliveredFileChanged, Unavailable,
}

public enum DeliveryPhase { Validating, Copying, Verifying, Publishing, Recording, Reconciling }
public enum DeliveryAvailability { NotChecked, VerifiedNow, Missing, Changed, Unavailable, Ineligible, Uncertain }
public enum DeliveryAttemptState { Intent, Staging, ReadyToPublish, Delivered, Failed, Cancelled, NeedsReconciliation }

public sealed record DeliveryRequest(
    Guid RequestId, ArtifactKey Artifact, Sha256 ExpectedSha256, ReviewId ExpectedReviewId,
    string Folder, string FileName, Guid SelectionVersion);

public sealed record MissingDeliveryReplacementRequest(
    Guid RequestId, Guid PriorDeliveryId, DeliveryFileObservation MissingObservation,
    Guid SelectionVersion);

public sealed record DeliveryProgress(
    Guid RequestId, ArtifactKey Artifact, Guid SelectionVersion, DeliveryPhase Phase,
    long BytesCopied = 0, long TotalBytes = 0);

public sealed record DeliveryOutcome(
    Guid RequestId, ArtifactKey Artifact, Guid SelectionVersion, DeliveryCode Code,
    Guid? DeliveryId = null, string? FinalPath = null, string? SuggestedFileName = null,
    string? Detail = null);

public sealed record DeliveryFileObservation(
    Guid DeliveryId, Guid RequestId, ArtifactKey Artifact, DeliveryAvailability Availability,
    DateTimeOffset ObservedAtUtc, string FinalPath, string? Detail = null);

public sealed record DeliveryState(
    Guid DeliveryId, Guid RequestId, ArtifactKey Artifact, ReviewId ReviewId,
    string RequestedFolder, string RequestedFileName, string ResolvedFolder, string FinalPath,
    string Status, DeliveryAttemptState? AttemptState, Guid? AttemptId,
    DateTimeOffset? VerifiedAtUtc, Guid? ReplacementOfDeliveryId,
    Guid StateVersion, DeliveryAvailability Availability = DeliveryAvailability.NotChecked);

public sealed record DeliveryOffer(ApprovedArtifact Artifact, Guid OfferVersion);

/// <summary>The last destination whose delivery was durably verified; display/default only.</summary>
public sealed record DeliveryDestinationPreferenceView(string Folder, long IntentOrdinal);

/// <summary>
/// A pre-approval check of the displayed draft. It grants nothing: every delivery revalidates
/// the name, folder, protection and support envelope again under its own guards.
/// </summary>
public sealed record DeliveryDraftCheck(DeliveryCode? Refusal, string? EffectiveFileName, string? Detail = null)
{
    public bool IsValid => Refusal is null;
}

/// <summary>A short-lived verified final-file authority held through immediate shell dispatch.</summary>
public interface IDeliveredSelectionLease : IDisposable
{
    Guid DeliveryId { get; }
    string FinalPath { get; }
    bool IsDisposed { get; }
}

public sealed record SelectionLeaseResult(
    DeliveryAvailability Availability, IDeliveredSelectionLease? Lease, string? Detail = null);

public interface IApprovedArtifactDeliveryService
{
    Task<OperationResult<IReadOnlyList<DeliveryState>>> GetDeliveryStateAsync(
        SessionId sessionId, ArtifactKey? artifact, CancellationToken cancellationToken);
    Task<(DeliveryOffer? Offer, ArtifactRefusal Refusal)> GetOfferAsync(
        ArtifactKey artifact, CancellationToken cancellationToken);
    Task<DeliveryOutcome> DeliverAsync(DeliveryRequest request,
        IProgress<DeliveryProgress>? progress, CancellationToken cancellationToken);
    Task<DeliveryOutcome> ReconcileAsync(Guid deliveryId,
        IProgress<DeliveryProgress>? progress, CancellationToken cancellationToken);
    Task<DeliveryFileObservation> CheckDeliveredFileAsync(Guid deliveryId, CancellationToken cancellationToken);
    Task<DeliveryOutcome> ReplaceMissingAsync(MissingDeliveryReplacementRequest request,
        IProgress<DeliveryProgress>? progress, CancellationToken cancellationToken);
    Task<SelectionLeaseResult> AcquireDeliveredSelectionAsync(Guid deliveryId, CancellationToken cancellationToken);

    /// <summary>Reads the remembered last successful destination; no file-system access.</summary>
    Task<OperationResult<DeliveryDestinationPreferenceView?>> GetLastSuccessfulDestinationAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult.Fail<DeliveryDestinationPreferenceView?>(
            FailureCode.PreconditionNotMet, "Destination preference is unavailable."));

    /// <summary>Checks the displayed filename and folder before any approval is attempted.</summary>
    DeliveryDraftCheck CheckDraft(string folder, string fileName, ArtifactKind kind) =>
        new(DeliveryCode.UnsupportedDestination, null, "Draft checking is unavailable.");
}

public sealed record DeliveryRecord(
    Guid DeliveryId, Guid RequestId, long IntentOrdinal, ArtifactKey Artifact,
    ReviewId ReviewId, string ApprovalSubjectKind, Guid ApprovalSubjectId,
    RevisionId? PromotionSourceRevisionId, Sha256 ApprovedSha256, long ApprovedLength,
    string RequestedFolder, string RequestedFileName, string ResolvedFolder,
    string FinalPath, string VolumeId, string DirectoryId, string DestinationKey,
    Guid? ReplacementOfDeliveryId, string Status, DateTimeOffset CreatedAtUtc,
    DateTimeOffset? VerifiedAtUtc = null, Sha256? VerifiedSha256 = null,
    long? VerifiedLength = null, string? FinalFileId = null,
    DateTimeOffset? FinalCreationUtc = null, Guid? WinningAttemptId = null);

public sealed record DeliveryAttemptRecord(
    Guid AttemptId, Guid DeliveryId, int AttemptNumber, string DestinationKey,
    DeliveryAttemptState State, string StagingLeaf, string DirectoryId,
    string? StagingFileId, DateTimeOffset? StagingCreationUtc,
    Sha256 ExpectedSha256, long ExpectedLength,
    DateTimeOffset? StageVerifiedAtUtc, string? FailureCode,
    DateTimeOffset StartedAtUtc, DateTimeOffset? FinishedAtUtc);

public sealed record DeliveryRequestBinding(string RequestedFolder, string RequestedFileName);
public sealed record DeliveryJournalEntry(DeliveryRecord Delivery, DeliveryAttemptRecord? Attempt,
    DeliveryRequestBinding? RequestBinding = null);
