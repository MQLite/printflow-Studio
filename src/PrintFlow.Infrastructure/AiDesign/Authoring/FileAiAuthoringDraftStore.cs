using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.AiDesign.Authoring;

namespace PrintFlow.Infrastructure.AiDesign.Authoring;

public sealed class FileAiAuthoringDraftStore(string workspaceRoot) : IAiAuthoringDraftStore
{
    public Task<OperationResult<AiAuthoringDraft>> ReadAsync(Guid id, CancellationToken ct) => Store(id).ReadAsync(ct);
    public Task<OperationResult<AiAuthoringDraft>> SaveAsync(AiAuthoringDraft value, long? expectedRevision, CancellationToken ct) => Store(value.Id).SaveAsync(value, expectedRevision, ct);

    private AuthoringVersionedFileStore<AiAuthoringDraft> Store(Guid id) => new(workspaceRoot, id,
        value => value is not null && value.Id == id ? AiAuthoringSnapshots.Draft(value) : throw new ArgumentException("Draft identity mismatch."),
        value => value.Revision, (value, revision) => value with { Revision = revision });
}
