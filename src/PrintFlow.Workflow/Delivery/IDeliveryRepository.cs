using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.Delivery;

public interface IDeliveryRepository
{
    Task<OperationResult<IReadOnlyList<DeliveryJournalEntry>>> ListAsync(
        SessionId sessionId, ArtifactKey? artifact, CancellationToken cancellationToken);
    Task<OperationResult<DeliveryJournalEntry?>> FindByIdAsync(Guid deliveryId, CancellationToken cancellationToken);
    Task<OperationResult<DeliveryJournalEntry?>> FindByRequestAsync(Guid requestId, CancellationToken cancellationToken);
    Task<OperationResult<DeliveryJournalEntry?>> FindSuccessorAsync(Guid priorDeliveryId, CancellationToken cancellationToken);
    Task<OperationResult<DeliveryJournalEntry?>> FindByDestinationAsync(
        string destinationKey, CancellationToken cancellationToken);
    Task<OperationResult<DeliveryJournalEntry>> CreateOrCoalesceAsync(
        DeliveryRecord proposed, DeliveryAttemptRecord attempt, CancellationToken cancellationToken);
    Task<OperationResult<DeliveryAttemptRecord>> BeginNextAttemptAsync(
        Guid deliveryId, string directoryId, string destinationKey, CancellationToken cancellationToken);
    Task<OperationResult<DeliveryAttemptRecord>> MarkStagingAsync(
        Guid attemptId, string stagedFileId, DateTimeOffset stagedCreationUtc, CancellationToken cancellationToken);
    Task<OperationResult<DeliveryAttemptRecord>> MarkReadyAsync(
        Guid attemptId, DateTimeOffset verifiedAtUtc, CancellationToken cancellationToken);
    Task<OperationResult<DeliveryAttemptRecord>> EndAttemptAsync(
        Guid attemptId, DeliveryAttemptState state, string? failureCode, CancellationToken cancellationToken);
    Task<OperationResult<DeliveryJournalEntry>> MarkDeliveredAsync(
        Guid deliveryId, Guid attemptId, string finalFileId,
        DateTimeOffset finalCreationUtc, DateTimeOffset verifiedAtUtc,
        CancellationToken cancellationToken);

    /// <summary>Reads the preference row written only by <see cref="MarkDeliveredAsync"/>.</summary>
    Task<OperationResult<DeliveryDestinationPreferenceView?>> ReadDestinationPreferenceAsync(
        CancellationToken cancellationToken) =>
        Task.FromResult(OperationResult.Fail<DeliveryDestinationPreferenceView?>(
            FailureCode.PreconditionNotMet, "Destination preference is unavailable."));
}
