using PrintFlow.Domain.Files;
using PrintFlow.Infrastructure.Delivery;
using PrintFlow.Workflow.Delivery;

namespace PrintFlow.WorkstationEntry;

public sealed class ContainedDelivery(OwnedPaths paths) : IDeliveryFileSystem
{
    private readonly WindowsDeliveryFileSystem inner = new();
    public DeliveryFileResult<IDeliveryFileGuard> Open(string sourcePath, Sha256 expectedSourceHash, long expectedSourceLength,
        string requestedFolder, string finalLeaf, ArtifactKind kind, IReadOnlyList<string> protectedRoots, string? protectedOriginalPath)
    {
        try
        {
            paths.Require(sourcePath, "workspace"); paths.Require(requestedFolder, "delivery");
            using NativePathLease source = paths.Read(sourcePath, "workspace");
            return inner.Open(sourcePath, expectedSourceHash, expectedSourceLength, requestedFolder, finalLeaf, kind, ProtectedRoots, protectedOriginalPath);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        { return DeliveryFileResult<IDeliveryFileGuard>.Fail(DeliveryCode.UnsupportedDestination, ex.Message); }
    }
    public string[] ProtectedRoots => new[] { "state", "workspace", "fixtures", "preset", "recycle", "evidence", "diagnostics" }.Select(area => paths.At(area)).ToArray();
    public DeliveryFileResult<string> CheckFolder(string folder, IReadOnlyList<string> protectedRoots)
    {
        try { paths.Require(folder, "delivery"); return inner.CheckFolder(folder, ProtectedRoots); }
        catch (IOException ex) { return DeliveryFileResult<string>.Fail(DeliveryCode.UnsupportedDestination, ex.Message); }
    }
    public DeliveryFileResult<string> ValidateLeaf(string candidate, ArtifactKind kind) => inner.ValidateLeaf(candidate, kind);
    public string? SuggestAlternative(string leaf, Func<string, bool> exists) => inner.SuggestAlternative(leaf, exists);
}
