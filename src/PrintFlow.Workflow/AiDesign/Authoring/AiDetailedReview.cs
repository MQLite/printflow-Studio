using System.Globalization;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.AiDesign.Authoring;

namespace PrintFlow.Workflow.AiDesign.Authoring;

public static class AiDetailedReview
{
    public static IReadOnlyList<string> DateFormats { get; } = Array.AsReadOnly(new[] { "dd/MM/yyyy", "MM/dd/yyyy", "yyyy-MM-dd", "yyyy" });
    public static IEnumerable<AiTemplateField> TextFields(AiAuthoringDraft draft) => draft.TemplateSnapshot.Fields.Where(x =>
        x.Kind != AiTemplateFieldKind.ImageReference && (x.Required || x.Semantic == AiTemplateSemantic.Name || AiAuthoringConfirmationRules.IsDate(x)));
    public static IReadOnlyList<AiDesignAttachment> ActiveAssets(AiAuthoringDraft draft)
    {
        var slots = draft.FinalDocument.Parts.OfType<AiImagePart>().Select(x => x.SlotId).ToHashSet(StringComparer.Ordinal);
        var ids = draft.Bindings.Where(x => slots.Contains(x.SlotId)).Select(x => x.AssetId).ToHashSet();
        return draft.Assets.Where(x => ids.Contains(x.Id)).OrderBy(x => x.Order).ToArray();
    }
    public static int GetFirstIncompleteStep(AiAuthoringDraft draft)
    {
        if (!Enum.IsDefined(draft.Product)) return 1;
        var active = draft.FinalDocument.Parts.OfType<AiImagePart>().Select(x => x.SlotId).ToHashSet(StringComparer.Ordinal);
        var sourceSemantic = draft.Product == AiProductKind.TextChange ? AiTemplateSemantic.EditSourceImage : AiTemplateSemantic.PersonImage;
        bool hasSource = draft.TemplateSnapshot.Fields.Any(source => source.Semantic == sourceSemantic && source.SlotId is { } slot && active.Contains(slot) && draft.Bindings.Any(x => x.SlotId == slot));
        if (!hasSource ||
            active.Any(slot => !draft.Bindings.Any(b => b.SlotId == slot)) || draft.Slots.Any(x => x.Required && !draft.Bindings.Any(b => b.SlotId == x.SlotId))) return 2;
        if (ActiveAssets(draft).Any(x => !AiAuthoringConfirmationRules.IsConfirmed(draft, AiAuthoringConfirmationRules.PreservationKey(x.Id)))) return 3;
        if (draft.Product != AiProductKind.TextChange && !draft.TemplateSnapshot.Fields.Any(x => x.Semantic == AiTemplateSemantic.Name) ||
            AiAuthoringConfirmationRules.NeedsReconciliation(draft) || GetIssues(draft).Any(x => x.Key is "invalid-date" or "date-format-required" or "date-order") ||
            TextFields(draft).Any(x => !(AiAuthoringConfirmationRules.IsDate(x) && !x.Required && string.IsNullOrWhiteSpace(AiAuthoringConfirmationRules.Value(draft, x))) &&
                !AiAuthoringConfirmationRules.IsConfirmed(draft, AiAuthoringConfirmationRules.TextKey(x)))) return 4;
        // B owns actual geometry, crop/safe-area checks and strict composition. A6 cannot certify this step.
        return 5;
    }
    public static IReadOnlyList<AiAuthoringIssue> GetIssues(AiAuthoringDraft draft)
    {
        string Read(AiTemplateSemantic semantic, bool format) => draft.TemplateSnapshot.Fields.SingleOrDefault(x => x.Semantic == semantic) is { } field
            ? format ? AiAuthoringConfirmationRules.DateFormat(draft, field) : AiAuthoringConfirmationRules.Value(draft, field) : "";
        var issues = ValidateDates(Read(AiTemplateSemantic.BirthDate, false), Read(AiTemplateSemantic.DeathDate, false),
            Read(AiTemplateSemantic.BirthDate, true), Read(AiTemplateSemantic.DeathDate, true)).ToList();
        foreach (var field in TextFields(draft).Where(x => AiAuthoringConfirmationRules.IsDate(x) && x.Semantic is not (AiTemplateSemantic.BirthDate or AiTemplateSemantic.DeathDate)))
            CheckDate(AiAuthoringConfirmationRules.Value(draft, field), AiAuthoringConfirmationRules.DateFormat(draft, field), AiAuthoringConfirmationRules.TextKey(field), issues);
        return issues.AsReadOnly();
    }
    public static IReadOnlyList<AiAuthoringIssue> ValidateDates(string? birth, string? death, string birthFormat, string deathFormat)
    {
        List<AiAuthoringIssue> issues = [];
        var b = CheckDate(birth, birthFormat, "birth-date", issues); var d = CheckDate(death, deathFormat, "death-date", issues);
        if (b is { } bv && d is { } dv && bv.Start > dv.End) issues.Add(new("date-order", "death-date", null));
        return issues.AsReadOnly();
    }
    public static string? InterpretDate(string? value, string format) => Range(value, format) is { } range
        ? format == "yyyy" ? range.Start.Year.ToString("D4", CultureInfo.InvariantCulture) : range.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    private static (DateOnly Start, DateOnly End)? CheckDate(string? value, string format, string key, List<AiAuthoringIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!DateFormats.Contains(format, StringComparer.Ordinal)) { issues.Add(new("date-format-required", key, null)); return null; }
        var range = Range(value, format);
        if (range is not { } valid) { issues.Add(new("invalid-date", key, null)); return null; }
        if (valid.Start.Year is < 1800 or > 2100) issues.Add(new("unusual-year", key, null));
        return valid;
    }
    private static (DateOnly Start, DateOnly End)? Range(string? value, string format)
    {
        if (format == "yyyy")
        {
            if (value is not { Length: 4 } || !value.All(char.IsAsciiDigit) || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int year) || year < 1) return null;
            return (new(year, 1, 1), new(year, 12, 31));
        }
        if (!DateFormats.Contains(format, StringComparer.Ordinal) || !DateOnly.TryParseExact(value, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return null;
        return (date, date);
    }
}
