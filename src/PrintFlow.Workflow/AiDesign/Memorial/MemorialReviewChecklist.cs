using System.Collections.ObjectModel;
using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.AiDesign.Authoring;

namespace PrintFlow.Workflow.AiDesign.Memorial;

public sealed class MemorialReviewChecklist(IAiAuthoringDraftStore drafts, IMemorialRenderStore renders, IMemorialReviewStore reviews)
{
    public Task<OperationResult<MemorialReviewState>> ReadAsync(MemorialRenderResult expected, CancellationToken ct) =>
        WithVerifiedRenderAsync(expected, () => ReadOrInitializeAsync(expected, ct), ct);

    public Task<OperationResult<MemorialReviewState>> SetCheckAsync(MemorialRenderResult expected, long expectedReviewRevision,
        MemorialReviewItem item, bool value, CancellationToken ct)
    {
        if (!Enum.IsDefined(item)) return Task.FromResult(Fail("Choose a recognized review item."));
        return UpdateAsync(expected, expectedReviewRevision, state =>
        {
            var checks = new Dictionary<MemorialReviewItem, bool>(state.Checks) { [item] = value };
            return OperationResult.Ok(state with
            {
                Checks = new ReadOnlyDictionary<MemorialReviewItem, bool>(checks),
                Accepted = state.Accepted && state.Checks[item] == value
            });
        }, ct);
    }

    public Task<OperationResult<MemorialReviewState>> AcceptAsync(MemorialRenderResult expected, long expectedReviewRevision, CancellationToken ct) =>
        UpdateAsync(expected, expectedReviewRevision, state => state.Checks.Values.All(value => value)
            ? OperationResult.Ok(state with { Accepted = true })
            : Fail("Complete all five checks before accepting this render."), ct);

    private Task<OperationResult<MemorialReviewState>> UpdateAsync(MemorialRenderResult expected, long expectedRevision,
        Func<MemorialReviewState, OperationResult<MemorialReviewState>> edit, CancellationToken ct) =>
        WithVerifiedRenderAsync(expected, async () =>
        {
            var read = await ReadOrInitializeAsync(expected, ct);
            if (read.IsFailure) return read;
            if (read.Value.Revision != expectedRevision) return Fail("The review changed. Reload its current checks.");
            var next = edit(read.Value);
            return next.IsFailure ? next : await reviews.SaveAsync(next.Value, expectedRevision, ct);
        }, ct);

    private async Task<OperationResult<MemorialReviewState>> ReadOrInitializeAsync(MemorialRenderResult expected, CancellationToken ct)
    {
        var read = await reviews.ReadAsync(expected.File, ct);
        if (read.IsFailure && read.Failure.Code == FailureCode.OutputMissing)
        {
            var uncheckedItems = new ReadOnlyDictionary<MemorialReviewItem, bool>(
                Enum.GetValues<MemorialReviewItem>().ToDictionary(item => item, _ => false));
            read = await reviews.SaveAsync(new(expected, 0, uncheckedItems, false), null, ct);
        }
        return read.IsSuccess && read.Value.Render != expected ? Fail("The saved review belongs to a different render.") : read;
    }

    private async Task<OperationResult<MemorialReviewState>> WithVerifiedRenderAsync(MemorialRenderResult expected,
        Func<Task<OperationResult<MemorialReviewState>>> action, CancellationToken ct)
    {
        if (expected?.File is not { Purpose: MemorialRenderPurpose.DesignMaterial } file ||
            file.DraftId == Guid.Empty || file.RenderId == Guid.Empty || expected.DraftRevision < 0)
            return Fail("Select a design material render to review.");
        var opened = await renders.OpenVerifiedAsync(expected, ct);
        if (opened.IsFailure) return OperationResult.Fail<MemorialReviewState>(opened.Failure);
        await using var render = opened.Value;

        return await drafts.WithCurrentRevisionAsync(file.DraftId, expected.DraftRevision, async draft =>
        {
            var current = MemorialCompositionModule.CreatePlan(draft, file.Purpose);
            if (current.IsFailure || current.Value.SourceManifestSha256 != expected.SourceManifestSha256)
                return Fail("The render no longer matches the current draft sources and layout.");
            return await action();
        }, ct);
    }

    private static OperationResult<MemorialReviewState> Fail(string detail) =>
        OperationResult.Fail<MemorialReviewState>(FailureCode.PreconditionNotMet, detail);
}
