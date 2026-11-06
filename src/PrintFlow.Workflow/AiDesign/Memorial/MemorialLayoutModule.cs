using System.Collections.ObjectModel;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Workflow.AiDesign.Authoring;

namespace PrintFlow.Workflow.AiDesign.Memorial;

public static class MemorialLayoutModule
{
    public static MemorialCanvas BadgePreset { get; } = new(86, 86, 86, 72);
    public static MemorialCanvas TshirtPreset { get; } = new(280, 390, null, null);
    public static string DateLabelKey(AiTemplateField field) => "date-label:" + AiAuthoringConfirmationRules.TextKey(field);

    // Reserve only the preset's optional specification fields; custom literal and date fields
    // (including an imported field with a reused name) keep their normal authoring behavior.
    public static string? FixedPromptSpecification(AiProductKind product, AiTemplateField field) =>
        field is not { Kind: AiTemplateFieldKind.Text, Required: false, Semantic: AiTemplateSemantic.FreeText } ? null :
        (product, field.Key) switch
        {
            (AiProductKind.MemorialBadge, "画布外圆") => FormattableString.Invariant($"{BadgePreset.OuterDiameterMm}mm"),
            (AiProductKind.MemorialBadge, "内容安全区") => FormattableString.Invariant($"{BadgePreset.SafeDiameterMm}mm"),
            (AiProductKind.MemorialTshirt, "设计规格") => FormattableString.Invariant($"{TshirtPreset.WidthMm}×{TshirtPreset.HeightMm}mm"),
            _ => null,
        };

    /// <summary>Literal layer inputs. Date labels share the date field's content confirmation, not an unchecked literal.</summary>
    public static IReadOnlyDictionary<string, string> TextInputs(AiAuthoringDraft draft)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in draft.TemplateSnapshot.Fields.Where(x => x.Kind != AiTemplateFieldKind.ImageReference))
        {
            string value = AiAuthoringConfirmationRules.Value(draft, field);
            bool date = AiAuthoringConfirmationRules.IsDate(field);
            if (date && string.IsNullOrWhiteSpace(value)) continue;
            values.Add(AiAuthoringConfirmationRules.TextKey(field), value);
            if (date && AiAuthoringConfirmationRules.DateLabel(draft, field) is { Length: > 0 } label)
                values.Add(DateLabelKey(field), label);
        }
        return new ReadOnlyDictionary<string, string>(values);
    }
    /// <summary>Geometry and permission checks only. This does not certify glyph fit, decoded pixels or a production template.</summary>
    public static IReadOnlyList<AiAuthoringIssue> Validate(AiAuthoringDraft draft, MemorialLayout layout)
    {
        List<AiAuthoringIssue> issues = [];
        void Add(string key, string? field = null) => issues.Add(new(key, field, null));
        if (layout is null || layout.Canvas is null || layout.Images is null || layout.Text is null)
        { Add("InvalidLayout"); return issues.AsReadOnly(); }
        var canvas = layout.Canvas;
        bool validCanvas = Positive(canvas.WidthMm) && Positive(canvas.HeightMm);
        if (draft.Product == AiProductKind.MemorialBadge)
            validCanvas &= canvas.OuterDiameterMm is { } outer && Positive(outer) && canvas.SafeDiameterMm is { } safe &&
                Positive(safe) && safe <= outer && canvas.WidthMm == outer && canvas.HeightMm == outer;
        else
            validCanvas &= draft.Product == AiProductKind.MemorialTshirt && canvas.OuterDiameterMm is null && canvas.SafeDiameterMm is null;
        if (!validCanvas) Add("InvalidCanvas");
        if (!layout.ProductTemplateConfirmed) Add("ProductTemplateUnconfirmed");

        var active = AiDetailedReview.ActiveAssets(draft);
        var layerIdentities = new HashSet<(Guid, MemorialLayerRole)>();
        foreach (var layer in layout.Images)
        {
            if (layer is null || !Enum.IsDefined(layer.Role)) { Add("InvalidLayer"); continue; }
            if (!layerIdentities.Add((layer.AssetId, layer.Role))) Add("DuplicateLayer");
            var asset = active.SingleOrDefault(x => x.Id == layer.AssetId);
            if (asset is null) Add("MissingAsset");
            var rule = draft.Preservation.SingleOrDefault(x => x.AssetId == layer.AssetId);
            bool protectedContent = layer.Role is MemorialLayerRole.Person or MemorialLayerRole.OriginalDecor || rule?.KeepContent == true;
            bool validBounds = BoundsValid(layer.Bounds);
            if (!validBounds) Add("InvalidGeometry");
            else if (validCanvas)
            {
                if (!InsideCanvas(layer.Bounds, canvas)) Add("OutsideCanvas");
                bool mayExtend = !protectedContent && (layer.Role == MemorialLayerRole.Background ||
                    layer.Role == MemorialLayerRole.GeneratedDecor && layer.AllowEdgeExtension);
                if (canvas.SafeDiameterMm is { } safeDiameter && !mayExtend && !InsideCircle(layer.Bounds, canvas.WidthMm / 2, canvas.HeightMm / 2, safeDiameter / 2))
                    Add("OutsideSafeArea");
            }

            var extent = layer.SourceExtent;
            bool validExtent = extent is { X: 0, Y: 0, Width: > 0, Height: > 0 };
            if (!validExtent) Add("SourceExtentRequired");
            var crop = layer.SourceCrop;
            bool validCrop = crop is null || validExtent && crop.X >= 0 && crop.Y >= 0 && crop.Width > 0 && crop.Height > 0 &&
                (long)crop.X + crop.Width <= extent!.Width && (long)crop.Y + crop.Height <= extent.Height;
            if (!validCrop) Add("InvalidCrop");
            if (protectedContent && (rule is null || !rule.KeepContent || rule.AllowRedraw)) Add("PreservationConflict");
            if (rule is null || !AiAuthoringConfirmationRules.IsConfirmed(draft, AiAuthoringConfirmationRules.PreservationKey(layer.AssetId))) Add("PreservationUnconfirmed");
            // A decision not to preserve content does not grant crop, move or scale permission.
            if (crop is not null && rule?.AllowCrop != true) Add("PreservationConflict");
            if (!validBounds) continue;
            if (validBounds && validExtent && validCrop)
            {
                var source = crop ?? extent!;
                if (!NearlyEqual(layer.Bounds.Width / source.Width, layer.Bounds.Height / source.Height)) Add("PreservationConflict");
            }
            var previous = draft.Layout?.Images.SingleOrDefault(x => x.AssetId == layer.AssetId && x.Role == layer.Role);
            if (rule?.AllowMove != true && (previous is null || previous.Bounds.X != layer.Bounds.X || previous.Bounds.Y != layer.Bounds.Y)) Add("PreservationConflict");
            if (rule?.AllowUniformScale != true)
            {
                var beforeSource = previous?.SourceCrop ?? previous?.SourceExtent;
                var afterSource = crop ?? extent;
                if (previous is null || beforeSource is null || afterSource is null ||
                    !NearlyEqual(previous.Bounds.Width / beforeSource.Width, layer.Bounds.Width / afterSource.Width) ||
                    !NearlyEqual(previous.Bounds.Height / beforeSource.Height, layer.Bounds.Height / afterSource.Height)) Add("PreservationConflict");
            }
        }

        foreach (var group in layout.Images.Where(x => x is not null).GroupBy(x => x.AssetId))
            if (group.Count() > 1 && (group.Any(x => x.Role is MemorialLayerRole.Person or MemorialLayerRole.OriginalDecor) ||
                draft.Preservation.Any(x => x.AssetId == group.Key && x.KeepContent))) Add("PreservationConflict");

        foreach (var asset in active.Where(a => draft.Preservation.Any(r => r.AssetId == a.Id && r.KeepContent)))
            if (!layout.Images.Any(x => x is not null && x.AssetId == asset.Id)) Add("MissingPreservedLayer");
        foreach (var field in draft.TemplateSnapshot.Fields.Where(x => x.Semantic == AiTemplateSemantic.PersonImage))
        {
            var binding = draft.Bindings.SingleOrDefault(x => x.SlotId == field.SlotId);
            if (binding is not null && active.Any(x => x.Id == binding.AssetId) &&
                !layout.Images.Any(x => x is not null && x.AssetId == binding.AssetId && x.Role == MemorialLayerRole.Person)) Add("MissingPersonLayer");
        }

        var textKeys = new HashSet<string>(StringComparer.Ordinal);
        var textInputs = TextInputs(draft);
        foreach (var line in layout.Text)
        {
            if (line is null || string.IsNullOrWhiteSpace(line.Key)) { Add("InvalidTextStyle"); continue; }
            if (!textKeys.Add(line.Key)) Add("DuplicateText", line.Key);
            if (!textInputs.TryGetValue(line.Key, out var expected) || !string.Equals(line.Text, expected, StringComparison.Ordinal)) Add("TextMismatch", line.Key);
            if (string.IsNullOrWhiteSpace(line.FontFamily) || !Positive(line.FontSizeMm) || !Enum.IsDefined(line.Placement) ||
                (line.Placement == MemorialTextPlacement.Arc ? line.ArcRadiusMm is not { } radius || !Positive(radius) : line.ArcRadiusMm is not null)) Add("InvalidTextStyle", line.Key);
            if (!BoundsValid(line.Bounds)) Add("InvalidGeometry", line.Key);
            else if (validCanvas)
            {
                if (!InsideCanvas(line.Bounds, canvas)) Add("OutsideCanvas", line.Key);
                if (canvas.SafeDiameterMm is { } safe && !InsideCircle(line.Bounds, canvas.WidthMm / 2, canvas.HeightMm / 2, safe / 2)) Add("OutsideSafeArea", line.Key);
            }
        }
        foreach (var field in AiDetailedReview.TextFields(draft))
        {
            string value = AiAuthoringConfirmationRules.Value(draft, field);
            if ((field.Required || field.Semantic == AiTemplateSemantic.Name || !string.IsNullOrWhiteSpace(value)) &&
                !textKeys.Contains(AiAuthoringConfirmationRules.TextKey(field))) Add("MissingText", AiAuthoringConfirmationRules.TextKey(field));
            if (AiAuthoringConfirmationRules.IsDate(field) && !string.IsNullOrWhiteSpace(value) &&
                textInputs.ContainsKey(DateLabelKey(field)) && !textKeys.Contains(DateLabelKey(field))) Add("MissingText", DateLabelKey(field));
        }
        return issues.Distinct().ToList().AsReadOnly();
    }

    /// <summary>Detach caller collections before any async boundary, also when storing an incomplete layout.</summary>
    public static MemorialLayout Freeze(MemorialLayout layout)
    {
        if (layout?.Canvas is not { } canvas || layout.Images is null || layout.Text is null ||
            !Positive(canvas.WidthMm) || !Positive(canvas.HeightMm) ||
            canvas.OuterDiameterMm is { } outer && !Positive(outer) || canvas.SafeDiameterMm is { } safe && !Positive(safe)) throw Invalid();
        var images = layout.Images.ToArray(); var text = layout.Text.ToArray();
        if (images.Any(x => x is null || x.AssetId == Guid.Empty || !Enum.IsDefined(x.Role) || !BoundsValid(x.Bounds)) ||
            images.Select(x => (x.AssetId, x.Role)).Distinct().Count() != images.Length ||
            text.Any(x => x is null || string.IsNullOrWhiteSpace(x.Key) || x.Text is null || string.IsNullOrWhiteSpace(x.FontFamily) ||
                !Positive(x.FontSizeMm) || !BoundsValid(x.Bounds) || !Enum.IsDefined(x.Placement) || x.ArcRadiusMm is { } r && !Positive(r)) ||
            text.Select(x => x.Key).Distinct(StringComparer.Ordinal).Count() != text.Length) throw Invalid();
        return layout with { Images = Array.AsReadOnly(images), Text = Array.AsReadOnly(text) };
    }

    public static bool SameContent(MemorialLayout? left, MemorialLayout? right) =>
        left is null ? right is null : right is not null && left.Canvas == right.Canvas &&
            left.Images.SequenceEqual(right.Images) && left.Text.SequenceEqual(right.Text);

    public static bool SameInputs(AiAuthoringDraft left, AiAuthoringDraft right) => left.Template == right.Template && left.Product == right.Product &&
        left.Fields.Count == right.Fields.Count && left.Fields.All(x => right.Fields.TryGetValue(x.Key, out var value) && value == x.Value) &&
        left.Assets.SequenceEqual(right.Assets) && left.Bindings.SequenceEqual(right.Bindings) && left.Slots.SequenceEqual(right.Slots) &&
        left.Preservation.SequenceEqual(right.Preservation) && left.FinalDocument.Parts.SequenceEqual(right.FinalDocument.Parts);

    private static bool Positive(double value) => double.IsFinite(value) && value > 0;
    private static bool BoundsValid(RectMm bounds) => bounds is not null && double.IsFinite(bounds.X) && double.IsFinite(bounds.Y) &&
        Positive(bounds.Width) && Positive(bounds.Height) && double.IsFinite(bounds.X + bounds.Width) && double.IsFinite(bounds.Y + bounds.Height);
    private static bool InsideCanvas(RectMm bounds, MemorialCanvas canvas) => bounds.X >= 0 && bounds.Y >= 0 &&
        bounds.X + bounds.Width <= canvas.WidthMm && bounds.Y + bounds.Height <= canvas.HeightMm;
    private static bool InsideCircle(RectMm bounds, double centerX, double centerY, double radius)
    {
        // Farthest corner tests all four corners without treating a rectangle's center as its extent.
        double dx = Math.Max(Math.Abs(bounds.X - centerX), Math.Abs(bounds.X + bounds.Width - centerX));
        double dy = Math.Max(Math.Abs(bounds.Y - centerY), Math.Abs(bounds.Y + bounds.Height - centerY));
        double normalized = Math.Pow(dx / radius, 2) + Math.Pow(dy / radius, 2);
        return normalized <= 1 + 1e-12;
    }
    private static bool NearlyEqual(double a, double b) => Math.Abs(a - b) <= 1e-9 * Math.Max(Math.Abs(a), Math.Abs(b));
    private static ArgumentException Invalid() => new("Invalid memorial layout snapshot.");
}
