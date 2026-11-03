using System.Text;
using System.Text.RegularExpressions;
using PrintFlow.Domain.AiDesign.Authoring;

namespace PrintFlow.Workflow.AiDesign.Authoring;

public static partial class PromptDocumentComposer
{
    // Only the template is parsed. Substituted operator text, including {{...}}, is opaque.
    [GeneratedRegex(@"\{\{([^{}\r\n]+)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex VariablePattern();

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
                            string? label = values.TryGetValue(key + ".label", out string? overriddenLabel)
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
            values.TryGetValue(field.Key, out string? value) ? value : field.DefaultValue;

        void FlushText()
        {
            if (text.Length == 0) return;
            parts.Add(new AiTextPart(Guid.NewGuid(), text.ToString()));
            text.Clear();
        }
    }
}
