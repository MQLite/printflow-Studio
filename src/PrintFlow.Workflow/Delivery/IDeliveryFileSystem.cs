using PrintFlow.Domain.Files;

namespace PrintFlow.Workflow.Delivery;

public sealed record DeliveryFileIdentity(string VolumeId, string FileId, DateTimeOffset CreatedAtUtc);
public enum DeliveryFilePresence { Absent, Present, Unavailable }

public sealed record DeliveryFileResult<T>(bool IsSuccess, T? Value, DeliveryCode Code, string? Detail = null)
{
    public static DeliveryFileResult<T> Ok(T value) => new(true, value, DeliveryCode.Delivered);
    public static DeliveryFileResult<T> Fail(DeliveryCode code, string detail) => new(false, default, code, detail);
}

public sealed record DeliveryFinalCheck(
    DeliveryFilePresence Presence, DeliveryFileIdentity? Identity,
    long? Length, Sha256? Hash, DeliveryCode? Failure = null, string? Detail = null);

/// <summary>A held destination used to verify a previously recorded file.</summary>
public interface IDeliveryDestinationGuard : IDisposable
{
    string ResolvedFolder { get; }
    string FinalPath { get; }
    string VolumeId { get; }
    string DirectoryId { get; }
    string DestinationKey { get; }
    DeliveryFinalCheck CheckFinal(DeliveryFileIdentity? expectedIdentity = null);
    string? ReadHeldFileName() => null;
}

/// <summary>Windows handle authority over an exact source, folder and final leaf.</summary>
public interface IDeliveryFileGuard : IDeliveryDestinationGuard
{
    DeliveryFileIdentity SourceIdentity { get; }
    DeliveryFileResult<bool> VerifySource();
    DeliveryFileResult<DeliveryFileIdentity> CreateStaging(string stagingLeaf);
    DeliveryFileResult<DeliveryFileIdentity> OpenOwnedStaging(
        string stagingLeaf, DeliveryFileIdentity expectedIdentity);
    Task<DeliveryFileResult<bool>> CopyAndVerifyStageAsync(
        Sha256 expectedHash, long expectedLength, IProgress<long>? copied, CancellationToken cancellationToken);
    DeliveryFileResult<bool> VerifyStage(Sha256 expectedHash, long expectedLength);
    DeliveryFileResult<bool> PublishNoReplace(string stagingLeaf, DeliveryFileIdentity expectedIdentity);
    DeliveryFileResult<bool> DeleteHeldStaging(DeliveryFileIdentity expectedIdentity);
    SelectionLeaseResult AcquireSelection(Guid deliveryId, DeliveryFileIdentity expectedIdentity,
        Sha256 expectedHash, long expectedLength);
    Task<DeliveryFileResult<DeliveryFileIdentity>> RenameFinalNoReplaceAsync(string newLeaf, ArtifactKind kind,
        DeliveryFileIdentity identity, Sha256 hash, long length, Func<Task<DeliveryFileResult<bool>>> recordIntent) =>
        Task.FromResult(DeliveryFileResult<DeliveryFileIdentity>.Fail(DeliveryCode.UnsupportedDestination,
            "Guarded rename is unavailable."));
}

public interface IDeliveryFileSystem
{
    DeliveryFileResult<IDeliveryFileGuard> Open(
        string sourcePath, Sha256 expectedSourceHash, long expectedSourceLength,
        string requestedFolder, string finalLeaf, ArtifactKind kind,
        IReadOnlyList<string> protectedRoots, string? protectedOriginalPath);
    DeliveryFileResult<string> ValidateLeaf(string candidate, ArtifactKind kind);
    DeliveryFileResult<IDeliveryDestinationGuard> OpenForRenameRecovery(string folder, string leaf, ArtifactKind kind,
        IReadOnlyList<string> protectedRoots) => DeliveryFileResult<IDeliveryDestinationGuard>.Fail(
            DeliveryCode.UnsupportedDestination, "Rename recovery is unavailable.");

    /// <summary>
    /// Read-only pre-approval check that <paramref name="folder"/> is inside the supported
    /// envelope and outside protected storage. Holds nothing afterwards; Open decides again.
    /// </summary>
    DeliveryFileResult<string> CheckFolder(string folder, IReadOnlyList<string> protectedRoots) =>
        DeliveryFileResult<string>.Fail(DeliveryCode.UnsupportedDestination, "Folder checking is unavailable.");
    string? SuggestAlternative(string leaf, Func<string, bool> exists);
}
