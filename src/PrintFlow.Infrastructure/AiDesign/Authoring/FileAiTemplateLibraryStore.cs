using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.AiDesign.Authoring;

namespace PrintFlow.Infrastructure.AiDesign.Authoring;

public sealed class FileAiTemplateLibraryStore(string workspaceRoot, string language = "zh-CN") : IAiTemplateLibraryStore
{
    private readonly AuthoringVersionedFileStore<AiTemplateLibrary> _store = new(workspaceRoot, null,
        AiAuthoringSnapshots.Library, value => value.Revision, (value, revision) => value with { Revision = revision },
        () => new AiTemplateLibrary
        {
            Templates = BuiltInAiTemplates.ForLanguage(language),
            OrderedIds = BuiltInAiTemplates.ForLanguage(language).Select(x => x.Reference.Id).ToArray(),
            DefaultId = "memorial-tshirt-quick"
        });

    public Task<OperationResult<AiTemplateLibrary>> ReadAsync(CancellationToken ct) => _store.ReadAsync(ct);
    public Task<OperationResult<AiTemplateLibrary>> SaveAsync(AiTemplateLibrary value, long expectedRevision, CancellationToken ct) => _store.SaveAsync(value, expectedRevision, ct);
}
