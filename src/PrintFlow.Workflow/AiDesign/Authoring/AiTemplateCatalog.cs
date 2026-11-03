using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.Results;

namespace PrintFlow.Workflow.AiDesign.Authoring;

public sealed class AiTemplateCatalog(IAiTemplateLibraryStore store, string language = "zh-CN")
{
    public async Task<OperationResult<IReadOnlyList<AiTemplateDefinition>>> ListAsync(CancellationToken ct)
    {
        var result = await store.ReadAsync(ct);
        return result.Map<IReadOnlyList<AiTemplateDefinition>>(x =>
        {
            var library = AiAuthoringSnapshots.Library(x);
            var byId = library.Templates.ToDictionary(template => template.Reference.Id, StringComparer.Ordinal);
            return Array.AsReadOnly(library.OrderedIds.Select(id => byId[id]).ToArray());
        });
    }

    public async Task<OperationResult<AiTemplateDefinition>> SaveAsync(AiTemplateRef? source, AiTemplateContent content, bool saveAsNew, CancellationToken ct)
    {
        try
        {
            content = AiAuthoringSnapshots.Content(content); // Detach before any await.
            var read = await store.ReadAsync(ct); if (read.IsFailure) return OperationResult.Fail<AiTemplateDefinition>(read.Failure);
            var library = AiAuthoringSnapshots.Library(read.Value);
            var previous = source is null ? null : library.Templates.SingleOrDefault(x => x.Reference == source);
            if (source is not null && previous is null) return Fail<AiTemplateDefinition>("Template is missing or stale.");
            bool create = previous is null || saveAsNew || IsBuiltin(previous.Reference.Id);
            var reference = create ? new AiTemplateRef("personal-" + Guid.NewGuid().ToString("N"), 1)
                : previous!.Reference with { Revision = checked(previous.Reference.Revision + 1) };
            var value = new AiTemplateDefinition(reference, content, previous?.BuiltinOriginId);
            var templates = library.Templates.Where(x => create || x.Reference.Id != reference.Id).Append(value).ToArray();
            var saved = await store.SaveAsync(library with
            {
                Templates = templates, OrderedIds = create ? library.OrderedIds.Append(reference.Id).ToArray() : library.OrderedIds
            }, library.Revision, ct);
            return saved.IsFailure ? OperationResult.Fail<AiTemplateDefinition>(saved.Failure) : OperationResult.Ok(value);
        }
        catch (ArgumentException) { return Fail<AiTemplateDefinition>("Invalid template definition."); }
        catch (OverflowException) { return Fail<AiTemplateDefinition>("Template revision exhausted."); }
    }

    public async Task<OperationResult<AiTemplateLibrary>> UpdatePreferencesAsync(long expectedLibraryRevision, IReadOnlyList<string> favorites, IReadOnlyList<string> order, string defaultId, CancellationToken ct)
    {
        var frozenFavorites = favorites.ToArray(); var frozenOrder = order.ToArray();
        var read = await store.ReadAsync(ct); if (read.IsFailure) return read;
        if (read.Value.Revision != expectedLibraryRevision) return Fail<AiTemplateLibrary>("Template library is stale.");
        try
        {
            var value = AiAuthoringSnapshots.Library(read.Value with { FavoriteIds = frozenFavorites, OrderedIds = frozenOrder, DefaultId = defaultId });
            return await store.SaveAsync(value, expectedLibraryRevision, ct);
        }
        catch (ArgumentException) { return Fail<AiTemplateLibrary>("Invalid template preferences."); }
    }

    public async Task<OperationResult<AiTemplateLibrary>> DeletePersonalAsync(AiTemplateRef source, CancellationToken ct)
    {
        var read = await store.ReadAsync(ct); if (read.IsFailure) return read;
        var library = read.Value;
        if (source is null || IsBuiltin(source.Id) || !library.Templates.Any(x => x.Reference == source))
            return Fail<AiTemplateLibrary>("Only the current personal template version can be deleted.");
        var order = library.OrderedIds.Where(x => x != source.Id).ToArray();
        return await store.SaveAsync(library with
        {
            Templates = library.Templates.Where(x => x.Reference.Id != source.Id).ToArray(),
            FavoriteIds = library.FavoriteIds.Where(x => x != source.Id).ToArray(), OrderedIds = order,
            DefaultId = library.DefaultId == source.Id ? order[0] : library.DefaultId
        }, library.Revision, ct);
    }

    public async Task<OperationResult<AiTemplateLibrary>> RestoreBuiltinAsync(string builtinId, CancellationToken ct)
    {
        var builtin = BuiltInAiTemplates.ForLanguage(language).SingleOrDefault(x => x.Reference.Id == builtinId);
        if (builtin is null) return Fail<AiTemplateLibrary>("Unknown built-in template.");
        var read = await store.ReadAsync(ct); if (read.IsFailure) return read;
        var library = read.Value;
        return await store.SaveAsync(library with
        {
            Templates = library.Templates.Where(x => x.Reference.Id != builtinId).Append(builtin).ToArray(),
            OrderedIds = library.OrderedIds.Contains(builtinId) ? library.OrderedIds : library.OrderedIds.Append(builtinId).ToArray()
        }, library.Revision, ct);
    }

    private static bool IsBuiltin(string id) => BuiltInAiTemplates.All.Any(x => x.Reference.Id == id);
    private static OperationResult<T> Fail<T>(string message) => OperationResult.Fail<T>(FailureCode.PreconditionNotMet, message);
}
