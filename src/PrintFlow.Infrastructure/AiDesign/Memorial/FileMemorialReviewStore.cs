using System.Collections.ObjectModel;
using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.AiDesign.Authoring;
using PrintFlow.Workflow.AiDesign.Memorial;

namespace PrintFlow.Infrastructure.AiDesign.Memorial;

public sealed class FileMemorialReviewStore(string workspaceRoot) : IMemorialReviewStore
{
    public Task<OperationResult<MemorialReviewState>> ReadAsync(MemorialRenderRef file, CancellationToken ct) =>
        Store(file).ReadAsync(ct);

    public Task<OperationResult<MemorialReviewState>> SaveAsync(MemorialReviewState value, long? expectedRevision, CancellationToken ct)
    {
        if (value?.Render?.File is not { } file || value.Checks is null ||
            expectedRevision is null && (value.Accepted || value.Checks.Values.Any(check => check)))
            return Task.FromResult(OperationResult.Fail<MemorialReviewState>(FailureCode.PreconditionNotMet, "New review must start unchecked."));
        return Store(file).SaveAsync(value, expectedRevision, ct);
    }

    public Task<OperationResult<T>> WithCurrentRevisionAsync<T>(MemorialRenderRef file, long expectedRevision,
        Func<MemorialReviewState, Task<OperationResult<T>>> publish, CancellationToken ct) =>
        Store(file).WithCurrentRevisionAsync(expectedRevision, publish, ct);

    private AuthoringVersionedFileStore<MemorialReviewState> Store(MemorialRenderRef file) => new(workspaceRoot, file.DraftId,
        value => Freeze(file, value), value => value.Revision, (value, revision) => value with { Revision = revision },
        reviewId: file.RenderId, sameIdentity: (current, next) => current.Render == next.Render);

    private static MemorialReviewState Freeze(MemorialRenderRef file, MemorialReviewState value)
    {
        if (file.Purpose != MemorialRenderPurpose.DesignMaterial || value is null || value.Revision < 0 ||
            value.Render is not { DraftRevision: >= 0, Facts: { Format: ImageFormat.Png, ByteLength: > 0, HasPixelDimensions: true } facts } render ||
            render.File != file || !Sha256.TryParse(facts.Sha256.Value, out _) || !Sha256.TryParse(render.SourceManifestSha256, out _) ||
            value.Checks is null)
            throw new ArgumentException("Invalid memorial review identity.");

        var checks = new Dictionary<MemorialReviewItem, bool>(value.Checks);
        var items = Enum.GetValues<MemorialReviewItem>();
        if (checks.Count != items.Length || items.Any(item => !checks.ContainsKey(item)) ||
            value.Accepted && checks.Values.Any(check => !check))
            throw new ArgumentException("Invalid memorial review checklist.");
        return value with { Checks = new ReadOnlyDictionary<MemorialReviewItem, bool>(checks) };
    }
}
