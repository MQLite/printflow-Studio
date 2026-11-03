using System.Globalization;
using System.Text;
using PrintFlow.Domain.AiDesign.Authoring;

namespace PrintFlow.Workflow.AiDesign.Authoring;

/// <summary>An explicit, reviewable conversion of the current document to reusable template prose.</summary>
public static class AiTemplateInstancePreview
{
    public static AiTemplateContent Create(AiAuthoringDraft draft)
    {
        draft = AiAuthoringSnapshots.Draft(draft);
        var fields = draft.TemplateSnapshot.Fields.ToList();
        foreach (var image in draft.FinalDocument.Parts.OfType<AiImagePart>())
        {
            if (fields.Any(x => x.Kind == AiTemplateFieldKind.ImageReference && x.SlotId == image.SlotId)) continue;
            var slot = draft.Slots.Single(x => x.SlotId == image.SlotId);
            string key = slot.Role.IndexOfAny(['{', '}', '\r', '\n']) >= 0 ? "image-" + image.PartId.ToString("N")[..8] : slot.Role;
            string baseKey = key; int suffix = 2;
            while (fields.Any(x => x.Key == key)) key = baseKey + "-" + suffix++;
            fields.Add(new(key, AiTemplateFieldKind.ImageReference, slot.Required, SlotId: slot.SlotId));
        }
        List<(string Literal, string Key)> candidates = [];
        foreach (var definition in fields.Where(x => x.Kind != AiTemplateFieldKind.ImageReference))
        {
            string? value = draft.Fields.TryGetValue(definition.Key, out var entered) ? entered : definition.DefaultValue;
            if (string.IsNullOrWhiteSpace(value)) continue;
            candidates.Add((value, definition.Key));
            if (definition.Kind == AiTemplateFieldKind.OptionalDateLine)
            {
                string? label = draft.Fields.TryGetValue(definition.Key + ".label", out var enteredLabel) ? enteredLabel : definition.DateLabel;
                if (!string.IsNullOrEmpty(label)) candidates.Add((label + ": " + value, definition.Key));
            }
        }
        // Duplicate values for different variables have no unambiguous reverse mapping.
        // Leave those visible for the operator to restore in the preview instead of guessing.
        var replacements = candidates.GroupBy(x => x.Literal, StringComparer.Ordinal)
            .Where(group => group.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() == 1)
            .Select(group => group.First()).OrderByDescending(x => x.Literal.Length).ToArray();
        StringBuilder body = new();
        foreach (var part in draft.FinalDocument.Parts)
        {
            if (part is AiImagePart image)
            {
                body.Append("{{").Append(fields.Single(x => x.Kind == AiTemplateFieldKind.ImageReference && x.SlotId == image.SlotId).Key).Append("}}");
                continue;
            }
            var text = ((AiTextPart)part).Text;
            for (int index = 0; index < text.Length;)
            {
                var match = replacements.FirstOrDefault(x => text.AsSpan(index).StartsWith(x.Literal, StringComparison.Ordinal) && Boundary(text, index, x.Literal.Length));
                if (match.Literal is null) { body.Append(text[index++]); continue; }
                body.Append("{{").Append(match.Key).Append("}}"); index += match.Literal.Length;
            }
        }
        return AiAuthoringSnapshots.Content(draft.TemplateSnapshot with { Body = body.ToString(), Fields = fields.AsReadOnly() });
    }
    private static bool Boundary(string text, int start, int length) =>
        !(start > 0 && Word(text[start]) && Word(text[start - 1])) &&
        !(start + length < text.Length && Word(text[start + length - 1]) && Word(text[start + length]));
    private static bool Word(char value) => char.IsLetterOrDigit(value) || char.GetUnicodeCategory(value) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark;
}
