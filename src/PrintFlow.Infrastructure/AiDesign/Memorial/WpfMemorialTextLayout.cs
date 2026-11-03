using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.AiDesign.Memorial;
using PrintFlow.Workflow.AiDesign.Authoring;

namespace PrintFlow.Infrastructure.AiDesign.Memorial;

internal sealed record WpfMemorialTextCluster(int SourceStart, int SourceLength, int GlyphCount, double AngleDegrees)
{
    public Point OriginMm { get; init; }
}
internal sealed record WpfMemorialTextDrawing(Geometry Ink, IReadOnlyList<WpfMemorialTextCluster> Clusters);

public sealed record MemorialTextLayoutOptions(double MinimumFontSizeMm = 2, int MaximumTextLength = 4096);

/// <summary>Checks confirmed literal inputs, then measures the same shaped outlines B3 will draw. No font downloads or rewriting.</summary>
public sealed partial class WpfMemorialTextLayout : IMemorialTextLayout
{
    private readonly MemorialTextLayoutOptions _options;
    public WpfMemorialTextLayout(MemorialTextLayoutOptions? options = null)
    {
        _options = options ?? new();
        if (!double.IsFinite(_options.MinimumFontSizeMm) || _options.MinimumFontSizeMm <= 0 || _options.MaximumTextLength is < 1 or > 16384)
            throw new ArgumentOutOfRangeException(nameof(options));
    }

    public OperationResult<IReadOnlyList<MemorialTextLine>> Arrange(AiAuthoringDraft draft, MemorialCanvas canvas, IReadOnlyList<MemorialTextStyle> styles)
    {
        try
        {
            if (canvas is null) return Failed<IReadOnlyList<MemorialTextLine>>("InvalidCanvas", "");
            draft = AiAuthoringSnapshots.Draft(draft);
            if (!draft.TemplateSnapshot.Fields.Any(x => x.Kind != AiTemplateFieldKind.ImageReference && x.Semantic == AiTemplateSemantic.Name))
                return Failed<IReadOnlyList<MemorialTextLine>>("MissingNameSemantic", "name");
            if (styles is null || styles.Count > 32 || styles.Any(x => x is null)) return Failed<IReadOnlyList<MemorialTextLine>>("InvalidTextStyle", "");
            var frozen = styles.ToArray();
            if (frozen.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != frozen.Length) return Failed<IReadOnlyList<MemorialTextLine>>("DuplicateTextStyle", "");
            if (AiAuthoringConfirmationRules.NeedsReconciliation(draft)) return Failed<IReadOnlyList<MemorialTextLine>>("TextReconciliationRequired", "");
            var inputs = MemorialLayoutModule.TextInputs(draft);
            var keys = frozen.Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var field in AiDetailedReview.TextFields(draft))
            {
                string key = AiAuthoringConfirmationRules.TextKey(field);
                if ((field.Required || field.Semantic == AiTemplateSemantic.Name || !string.IsNullOrWhiteSpace(AiAuthoringConfirmationRules.Value(draft, field))) && !keys.Contains(key))
                    return Failed<IReadOnlyList<MemorialTextLine>>("MissingTextStyle", key);
                string labelKey = MemorialLayoutModule.DateLabelKey(field);
                if (AiAuthoringConfirmationRules.IsDate(field) && inputs.ContainsKey(labelKey) && !keys.Contains(labelKey))
                    return Failed<IReadOnlyList<MemorialTextLine>>("MissingTextStyle", labelKey);
            }
            List<MemorialTextLine> lines = [];
            foreach (var style in frozen)
            {
                if (style.Key is null || !inputs.TryGetValue(style.Key, out string? value)) return Failed<IReadOnlyList<MemorialTextLine>>("UnknownTextStyle", style.Key ?? "");
                var field = draft.TemplateSnapshot.Fields.SingleOrDefault(x => x.Kind != AiTemplateFieldKind.ImageReference &&
                    (AiAuthoringConfirmationRules.TextKey(x) == style.Key || AiAuthoringConfirmationRules.IsDate(x) && MemorialLayoutModule.DateLabelKey(x) == style.Key));
                if (field is null || !AiAuthoringConfirmationRules.CanConfirm(draft, AiAuthoringConfirmationRules.TextKey(field)) ||
                    !AiAuthoringConfirmationRules.IsConfirmed(draft, AiAuthoringConfirmationRules.TextKey(field))) return Failed<IReadOnlyList<MemorialTextLine>>("TextUnconfirmed", style.Key);
                lines.Add(new(style.Key, value, style.FontFamily, style.FontSizeMm, style.Bounds, style.Placement, style.ArcRadiusMm));
            }
            var geometryIssue = MemorialLayoutModule.Validate(draft, new(canvas, [], lines, true)).FirstOrDefault(x =>
                x.Key is "InvalidCanvas" or "InvalidGeometry" or "OutsideCanvas" or "OutsideSafeArea" or "InvalidTextStyle");
            if (geometryIssue is not null) return Failed<IReadOnlyList<MemorialTextLine>>(geometryIssue.Key, geometryIssue.FieldKey ?? "");
            foreach (var line in lines)
            {
                var prepared = Prepare(line);
                if (prepared.IsFailure) return OperationResult.Fail<IReadOnlyList<MemorialTextLine>>(prepared.Failure);
            }
            return OperationResult.Ok<IReadOnlyList<MemorialTextLine>>(lines.AsReadOnly());
        }
        catch (ArgumentException) { return Failed<IReadOnlyList<MemorialTextLine>>("InvalidTextStyle", ""); }
    }

    // Infrastructure-only seam. B3 reuses this after checking the frozen draft and layout;
    // the returned geometry is frozen, in millimetres, and never includes preview guides.
    internal OperationResult<WpfMemorialTextDrawing> Prepare(MemorialTextLine line)
    {
        OperationResult<WpfMemorialTextDrawing> Run()
        {
            try { return Shape(line); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or System.IO.IOException or COMException or OverflowException)
            { return Failed<WpfMemorialTextDrawing>("TextShapingUnavailable", line?.Key ?? "", ex.ToString()); }
        }
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA) return Run();
        OperationResult<WpfMemorialTextDrawing> result = default;
        var thread = new Thread(() => result = Run()) { IsBackground = true, Name = "Memorial text layout" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join(); return result;
    }

    private static OperationResult<T> Failed<T>(string issue, string field, string? diagnostic = null) => OperationResult.Fail<T>(OperationFailure.Create(
        FailureCode.PreconditionNotMet, $"{issue}: choose a supported font, explicit line breaks or another text layout; source text and size were preserved." + (diagnostic is null ? "" : " " + diagnostic),
        context: new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal) { ["issue"] = issue, ["field"] = field })));
}
