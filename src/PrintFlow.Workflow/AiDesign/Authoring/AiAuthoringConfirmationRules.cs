using System.Security.Cryptography;
using System.Text.Json;
using PrintFlow.Domain.AiDesign.Authoring;

namespace PrintFlow.Workflow.AiDesign.Authoring;

/// <summary>Exact input identities. An acknowledgement of two views is never a text confirmation.</summary>
public static class AiAuthoringConfirmationRules
{
    internal const string ReconciledDocument = "reconcile-document";
    internal const string ReconciledFields = "reconcile-fields";
    public static string PreservationKey(Guid assetId) => "preservation:" + assetId.ToString("N");
    public static string TextKey(AiTemplateField definition) => definition.Semantic switch
    {
        AiTemplateSemantic.Name => "name", AiTemplateSemantic.BirthDate => "birth-date",
        AiTemplateSemantic.DeathDate => "death-date", _ => "field:" + definition.Key
    };
    public static AiTemplateField? TextField(AiAuthoringDraft draft, string key) =>
        draft.TemplateSnapshot.Fields.SingleOrDefault(x => x.Kind != AiTemplateFieldKind.ImageReference && TextKey(x) == key);
    public static string Value(AiAuthoringDraft draft, AiTemplateField definition) =>
        draft.Fields.TryGetValue(definition.Key, out var value) ? value : definition.DefaultValue ?? "";
    public static string DateLabel(AiAuthoringDraft draft, AiTemplateField definition) =>
        draft.Fields.TryGetValue(AiTemplateMetadata.LabelKey(draft.TemplateSnapshot, definition), out var value) ? value : definition.DateLabel ?? "";
    public static string DateFormat(AiAuthoringDraft draft, AiTemplateField definition) => draft.Fields.GetValueOrDefault(AiTemplateMetadata.FormatKey(draft.TemplateSnapshot, definition), "");
    public static bool IsDate(AiTemplateField definition) => definition.Kind == AiTemplateFieldKind.OptionalDateLine || definition.Semantic is AiTemplateSemantic.BirthDate or AiTemplateSemantic.DeathDate;

    public static string? ContentHash(AiAuthoringDraft draft, string key)
    {
        if (TextField(draft, key) is { } definition)
            return Hash(new[] { key, definition.Key, Value(draft, definition), IsDate(definition) ? DateLabel(draft, definition) : "", IsDate(definition) ? DateFormat(draft, definition) : "" });
        var asset = AiDetailedReview.ActiveAssets(draft).SingleOrDefault(x => PreservationKey(x.Id) == key);
        var rule = asset is null ? null : draft.Preservation.SingleOrDefault(x => x.AssetId == asset.Id);
        if (asset is null || rule is null) return null;
        var active = draft.FinalDocument.Parts.OfType<AiImagePart>().Select(x => x.SlotId).ToHashSet(StringComparer.Ordinal);
        var roles = draft.Slots.Where(x => active.Contains(x.SlotId) && draft.Bindings.Any(b => b.SlotId == x.SlotId && b.AssetId == asset.Id))
            .OrderBy(x => x.SlotId, StringComparer.Ordinal).ToArray();
        return Hash(new { asset.Id, Source = asset.Sha256.Value, Rule = rule, Roles = roles });
    }
    public static bool IsConfirmed(AiAuthoringDraft draft, string key) => ContentHash(draft, key) is { } hash &&
        draft.Confirmations.TryGetValue(key, out var saved) && string.Equals(saved, hash, StringComparison.Ordinal);
    public static bool CanConfirm(AiAuthoringDraft draft, string key)
    {
        if (ContentHash(draft, key) is null) return false;
        if (TextField(draft, key) is not { } definition)
        {
            var rule = draft.Preservation.SingleOrDefault(x => PreservationKey(x.AssetId) == key);
            return rule is not null && !(rule.KeepContent && rule.AllowRedraw);
        }
        if ((definition.Required || definition.Semantic == AiTemplateSemantic.Name) && string.IsNullOrWhiteSpace(Value(draft, definition))) return false;
        return !IsDate(definition) || !AiDetailedReview.GetIssues(draft).Any(x => x.Key == "date-order" || x.FieldKey == key && x.Key is "invalid-date" or "date-format-required");
    }
    public static string DocumentHash(AiPromptDocument document) => Hash(document.Parts.Select(x => x switch
    {
        AiTextPart text => new[] { "text", text.PartId.ToString("N"), text.Text },
        AiImagePart image => new[] { "image", image.PartId.ToString("N"), image.SlotId },
        _ => throw new ArgumentException("Unsupported document part.")
    }).ToArray());
    public static string FieldsHash(AiAuthoringDraft draft) => Hash(new
    {
        draft.Template, Values = draft.Fields.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new[] { x.Key, x.Value }).ToArray()
    });
    public static bool NeedsReconciliation(AiAuthoringDraft draft)
    {
        if (draft.Confirmations.GetValueOrDefault(ReconciledDocument) == DocumentHash(draft.FinalDocument) &&
            draft.Confirmations.GetValueOrDefault(ReconciledFields) == FieldsHash(draft)) return false;
        if (draft.IsManuallyEdited) return true;
        static (string?, string?) Content(AiPromptPart part) => part is AiTextPart text ? (text.Text, null) : (null, ((AiImagePart)part).SlotId);
        var composed = PromptDocumentComposer.Compose(draft.TemplateSnapshot, draft.Fields).Document;
        return !composed.Parts.Select(Content).SequenceEqual(draft.FinalDocument.Parts.Select(Content));
    }
    internal static AiAuthoringDraft Refresh(AiAuthoringDraft draft)
    {
        var confirmations = draft.Confirmations.Where(x => x.Key switch
        {
            ReconciledDocument => x.Value == DocumentHash(draft.FinalDocument), ReconciledFields => x.Value == FieldsHash(draft),
            _ => ContentHash(draft, x.Key) is { } hash && x.Value == hash
        }).ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        draft = draft with { Confirmations = confirmations };
        return draft with { NeedsFieldReconcile = NeedsReconciliation(draft) };
    }
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
}
