using System.Text;
using System.Text.RegularExpressions;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Workflow.AiDesign.Memorial;

namespace PrintFlow.Workflow.AiDesign.Authoring;

public static partial class PromptDocumentComposer
{
    // Only the template is parsed. Substituted operator text, including {{...}}, is opaque.
    [GeneratedRegex(@"\{\{([^{}\r\n]+)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex VariablePattern();

    public static bool HasOutdatedProductSpecification(AiAuthoringDraft draft)
    {
        var fixedValues = draft.TemplateSnapshot.Fields
            .Select(definition => (definition.Key, Value: MemorialLayoutModule.FixedPromptSpecification(draft.Product, definition)))
            .Where(entry => entry.Value is not null)
            .ToDictionary(entry => entry.Key, entry => entry.Value!, StringComparer.Ordinal);
        if (fixedValues.Count == 0) return false;
        if (fixedValues.Any(entry => draft.Fields.TryGetValue(entry.Key, out string? saved) && saved != entry.Value)) return true;
        if (draft.IsManuallyEdited) return false;

        // The old editor could restore the form value after composing a different size.
        // Compare generated specification-only lines without treating a changed name or date
        // as a changed product size, or inspecting manually authored prose.
        static string[] Lines(string text) => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var savedLines = draft.FinalDocument.Parts.OfType<AiTextPart>().SelectMany(part => Lines(part.Text)).ToHashSet(StringComparer.Ordinal);
        foreach (string line in Lines(draft.TemplateSnapshot.Body))
        {
            MatchCollection matches = VariablePattern().Matches(line);
            if (!matches.Any(match => fixedValues.ContainsKey(match.Groups[1].Value))) continue;
            if (matches.All(match => fixedValues.ContainsKey(match.Groups[1].Value)))
            {
                string expected = VariablePattern().Replace(line, match => fixedValues[match.Groups[1].Value]);
                if (!savedLines.Contains(expected)) return true;
                continue;
            }

            // A custom template can mix a size with editable text. If that generated prompt
            // is stale, regenerate it explicitly rather than guessing which part changed.
            var current = Compose(draft.TemplateSnapshot, draft.Fields).Document;
            return !current.Parts.Select(Content).SequenceEqual(draft.FinalDocument.Parts.Select(Content));
        }
        return false;

        static (string? Text, string? Slot) Content(AiPromptPart part) =>
            part is AiTextPart text ? (text.Text, null) : (null, ((AiImagePart)part).SlotId);
    }

    public static AiPromptComposition Compose(AiTemplateContent template, IReadOnlyDictionary<string, string> fields)
    {
        var definitions = template.Fields.ToDictionary(x => x.Key, StringComparer.Ordinal);
        var values = new Dictionary<string, string>(fields, StringComparer.Ordinal);
        var parts = new List<AiPromptPart>();
        var issues = new List<AiAuthoringIssue>();
        var text = new StringBuilder();

        // Walk original template lines so omitting a date never consumes a newline inside a value.
        int start = 0;
        while (start < template.Body.Length)
        {
            int end = start;
            while (end < template.Body.Length && template.Body[end] is not ('\r' or '\n')) end++;
            int next = end;
            if (next < template.Body.Length && template.Body[next] == '\r') next++;
            if (next < template.Body.Length && template.Body[next] == '\n') next++;

            string line = template.Body[start..end];
            var matches = VariablePattern().Matches(line);
            bool omitLine = matches.Any(match =>
                definitions.TryGetValue(match.Groups[1].Value, out var field) &&
                field.Kind == AiTemplateFieldKind.OptionalDateLine &&
                string.IsNullOrWhiteSpace(Value(field)));

            if (!omitLine)
            {
                int cursor = 0;
                foreach (Match match in matches)
                {
                    text.Append(line, cursor, match.Index - cursor);
                    string key = match.Groups[1].Value;
                    if (!definitions.TryGetValue(key, out var field))
                    {
                        text.Append(match.Value);
                        issues.Add(new("unknown-variable", key, null));
                    }
                    else if (field.Kind == AiTemplateFieldKind.ImageReference)
                    {
                        if (string.IsNullOrWhiteSpace(field.SlotId))
                        {
                            text.Append(match.Value);
                            issues.Add(new("missing-slot", key, field.SlotId));
                        }
                        else
                        {
                            FlushText();
                            parts.Add(new AiImagePart(Guid.NewGuid(), field.SlotId));
                        }
                    }
                    else if (string.IsNullOrWhiteSpace(Value(field)))
                    {
                        text.Append(match.Value);
                        issues.Add(new("missing-field", key, null));
                    }
                    else
                    {
                        if (field.Kind == AiTemplateFieldKind.OptionalDateLine)
                        {
                            string? label = values.TryGetValue(AiTemplateMetadata.LabelKey(template, field), out string? overriddenLabel)
                                ? overriddenLabel : field.DateLabel;
                            if (!string.IsNullOrEmpty(label)) text.Append(label).Append(": ");
                        }
                        text.Append(Value(field));
                    }
                    cursor = match.Index + match.Length;
                }
                text.Append(line, cursor, line.Length - cursor);
                text.Append(template.Body, end, next - end);
            }
            start = next;
        }
        FlushText();

        return new(new AiPromptDocument(parts.AsReadOnly()), issues.AsReadOnly());

        string? Value(AiTemplateField field) =>
            MemorialLayoutModule.FixedPromptSpecification(template.Product, field) ??
            (values.TryGetValue(field.Key, out string? value) ? value : field.DefaultValue);

        void FlushText()
        {
            if (text.Length == 0) return;
            parts.Add(new AiTextPart(Guid.NewGuid(), text.ToString()));
            text.Clear();
        }
    }
}
