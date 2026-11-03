using PrintFlow.Domain.AiDesign.Authoring;

namespace PrintFlow.Workflow.AiDesign.Authoring;

/// <summary>Keep declared variables authoritative when a user names one like a date metadata alias.</summary>
public static class AiTemplateMetadata
{
    public static string LabelKey(AiTemplateContent template, AiTemplateField definition) => Key(template, definition, "label");
    public static string FormatKey(AiTemplateContent template, AiTemplateField definition) => Key(template, definition, "format");
    private static string Key(AiTemplateContent template, AiTemplateField definition, string kind)
    {
        string preferred = definition.Key + "." + kind;
        if (!template.Fields.Any(x => x.Key == preferred)) return preferred;
        var reserved = template.Fields.Select(x => x.Key).Concat(template.Fields.Where(AiAuthoringConfirmationRules.IsDate)
            .SelectMany(x => new[] { x.Key + ".label", x.Key + ".format" })).ToHashSet(StringComparer.Ordinal);
        string alternate = "@date-" + kind + ":" + definition.Key;
        while (reserved.Contains(alternate)) alternate = "@" + alternate;
        return alternate;
    }
}
