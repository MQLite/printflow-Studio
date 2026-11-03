using System.Collections.ObjectModel;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.Files;
using PrintFlow.Workflow.AiDesign.Memorial;

namespace PrintFlow.Workflow.AiDesign.Authoring;

/// <summary>Validates and detaches collections at the authoring module and storage boundaries.</summary>
public static class AiAuthoringSnapshots
{
    public static AiTemplateContent Content(AiTemplateContent content)
    {
        if (content is null || string.IsNullOrWhiteSpace(content.Name) || content.Body is null ||
            content.Fields is null || !Enum.IsDefined(content.Product)) throw Invalid();
        var fields = content.Fields.ToArray();
        if (fields.Any(x => x is null || string.IsNullOrWhiteSpace(x.Key) || x.Key.IndexOfAny(['{', '}', '\r', '\n']) >= 0 ||
            !Enum.IsDefined(x.Kind) || !Enum.IsDefined(x.Semantic)) || fields.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != fields.Length)
            throw Invalid();
        if (fields.Any(x => (x.Kind == AiTemplateFieldKind.ImageReference && string.IsNullOrWhiteSpace(x.SlotId)) ||
            ((x.Kind == AiTemplateFieldKind.ImageReference || x.Semantic is AiTemplateSemantic.Name or AiTemplateSemantic.BirthDate or AiTemplateSemantic.DeathDate) && x.DefaultValue is not null)))
            throw Invalid();
        var images = fields.Where(x => x.Kind == AiTemplateFieldKind.ImageReference).ToArray();
        if (images.Select(x => x.SlotId).Distinct(StringComparer.Ordinal).Count() != images.Length) throw Invalid();
        foreach (var semantic in new[] { AiTemplateSemantic.Name, AiTemplateSemantic.BirthDate, AiTemplateSemantic.DeathDate, AiTemplateSemantic.PersonImage })
            if (fields.Count(x => x.Semantic == semantic) > 1) throw Invalid();
        return content with { Fields = Array.AsReadOnly(fields) };
    }

    public static AiPromptDocument Document(AiPromptDocument document)
    {
        if (document?.Parts is null) throw Invalid();
        var parts = document.Parts.ToArray();
        if (parts.Any(x => x is null || x.PartId == Guid.Empty || x switch
            {
                AiTextPart text => text.Text is null,
                AiImagePart image => string.IsNullOrWhiteSpace(image.SlotId),
                _ => true
            }) || parts.Select(x => x.PartId).Distinct().Count() != parts.Length) throw Invalid();
        return new(Array.AsReadOnly(parts));
    }

    public static AiAuthoringDraft Draft(AiAuthoringDraft draft)
    {
        if (draft is null || draft.Id == Guid.Empty || draft.Revision < 0 || draft.SchemaVersion is not (1 or 2) ||
            draft.SchemaVersion == 1 && draft.Layout is not null ||
            !Enum.IsDefined(draft.Product) || !Enum.IsDefined(draft.Mode) || draft.Assets is null || draft.Slots is null ||
            draft.Bindings is null || draft.Preservation is null || (draft.ParentDraftId.HasValue != draft.ParentDraftRevision.HasValue) ||
            draft.ParentDraftId == Guid.Empty || draft.ParentDraftRevision < 0) throw Invalid();
        ValidateReference(draft.Template);
        var content = Content(draft.TemplateSnapshot);
        if (content.Product != draft.Product) throw Invalid();
        var assets = draft.Assets.ToArray(); var slots = draft.Slots.ToArray(); var bindings = draft.Bindings.ToArray();
        var preservation = draft.Preservation.ToArray();
        if (assets.Any(x => x is null || x.Id == Guid.Empty || x.Snapshot is null || x.Snapshot.RequestId != draft.Id ||
            string.IsNullOrWhiteSpace(x.Snapshot.RelativePath) || x.Order < 1 || x.ByteLength <= 0 ||
            string.IsNullOrWhiteSpace(x.UploadName) || string.IsNullOrWhiteSpace(x.OriginalName) ||
            x.Purpose is null || string.IsNullOrWhiteSpace(x.MediaType) || !Sha256.TryParse(x.Sha256.Value, out _)) ||
            assets.Select(x => x.Id).Distinct().Count() != assets.Length || assets.Select(x => x.Order).Distinct().Count() != assets.Length ||
            slots.Any(x => x is null || string.IsNullOrWhiteSpace(x.SlotId) || string.IsNullOrWhiteSpace(x.Role)) ||
            slots.Select(x => x.SlotId).Distinct(StringComparer.Ordinal).Count() != slots.Length ||
            bindings.Any(x => x is null || !slots.Any(s => s.SlotId == x.SlotId) || !assets.Any(a => a.Id == x.AssetId)) ||
            bindings.Select(x => x.SlotId).Distinct(StringComparer.Ordinal).Count() != bindings.Length ||
            preservation.Any(x => x is null || !assets.Any(a => a.Id == x.AssetId)) ||
            preservation.Select(x => x.AssetId).Distinct().Count() != preservation.Length) throw Invalid();
        var initial = Document(draft.InitialDocument); var final = Document(draft.FinalDocument);
        if (initial.Parts.Concat(final.Parts).OfType<AiImagePart>().Any(x => !slots.Any(s => s.SlotId == x.SlotId))) throw Invalid();
        var frozen = draft with
        {
            SchemaVersion = 2, Layout = draft.Layout is null ? null : MemorialLayoutModule.Freeze(draft.Layout),
            TemplateSnapshot = content, Fields = Dictionary(draft.Fields), Confirmations = Dictionary(draft.Confirmations),
            InitialDocument = initial, FinalDocument = final, Assets = Array.AsReadOnly(assets), Slots = Array.AsReadOnly(slots),
            Bindings = Array.AsReadOnly(bindings), Preservation = Array.AsReadOnly(preservation)
        };
        // A persisted flag alone cannot certify geometry. Keep invalid drafts editable,
        // but do not expose their layout as confirmed, including through history reads.
        if (frozen.Layout is { ProductTemplateConfirmed: true } layout && MemorialLayoutModule.Validate(frozen, layout).Count > 0)
            frozen = frozen with { Layout = layout with { ProductTemplateConfirmed = false } };
        return frozen;
    }

    public static AiTemplateLibrary Library(AiTemplateLibrary library)
    {
        if (library is null || library.Revision < 0 || library.Templates is null || library.FavoriteIds is null || library.OrderedIds is null) throw Invalid();
        var templates = library.Templates.Select(x =>
        {
            if (x is null) throw Invalid(); ValidateReference(x.Reference);
            return x with { Content = Content(x.Content) };
        }).ToArray();
        var favorites = library.FavoriteIds.ToArray(); var order = library.OrderedIds.ToArray();
        var ids = templates.Select(x => x.Reference.Id).ToHashSet(StringComparer.Ordinal);
        if (ids.Count != templates.Length || !ids.Contains(library.DefaultId) || favorites.Any(x => !ids.Contains(x)) ||
            favorites.Distinct(StringComparer.Ordinal).Count() != favorites.Length || order.Length != ids.Count ||
            !ids.SetEquals(order) || order.Distinct(StringComparer.Ordinal).Count() != order.Length) throw Invalid();
        return library with { Templates = Array.AsReadOnly(templates), FavoriteIds = Array.AsReadOnly(favorites), OrderedIds = Array.AsReadOnly(order) };
    }

    private static IReadOnlyDictionary<string, string> Dictionary(IReadOnlyDictionary<string, string> dictionary)
    {
        if (dictionary is null || dictionary.Any(x => string.IsNullOrWhiteSpace(x.Key) || x.Value is null)) throw Invalid();
        return new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(dictionary, StringComparer.Ordinal));
    }

    private static void ValidateReference(AiTemplateRef reference)
    {
        if (reference is null || string.IsNullOrWhiteSpace(reference.Id) || reference.Revision < 1) throw Invalid();
    }

    private static ArgumentException Invalid() => new("Invalid authoring snapshot.");
}
