using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PrintFlow.App.Resources;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Workflow.AiDesign.Authoring;
using PrintFlow.Workflow.AiDesign.Memorial;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.App.ViewModels.AiDesign;

// These are editable projections of a proposed layout. Only the workflow may confirm it.
public sealed record MemorialImageRoleChoice(MemorialLayerRole? Role, string Label);
public sealed record MemorialTextPlacementChoice(MemorialTextPlacement Placement, string Label);

public sealed class MemorialImageInput : ObservableObject
{
    private readonly Action edited;
    private MemorialImageRoleChoice selectedRole;
    private string x, y, width;
    private readonly RectPx? crop;
    private readonly int order;
    private readonly bool allowEdgeExtension;

    internal MemorialImageInput(AiDesignAttachment asset, DecodedPreview image, bool person, bool keep,
        MemorialImageLayer? previous, RectMm suggestion, int order, Action edited)
    {
        Asset = asset; Image = image; this.edited = edited;
        Source = new(0, 0, image.SourcePixelWidth, image.SourcePixelHeight);
        crop = previous?.SourceCrop; this.order = previous?.ZOrder ?? order;
        allowEdgeExtension = previous?.AllowEdgeExtension == true;
        var choices = person ? new MemorialLayerRole?[] { MemorialLayerRole.Person }
            : keep ? [MemorialLayerRole.OriginalDecor]
            : new MemorialLayerRole?[] { null, MemorialLayerRole.GeneratedDecor, MemorialLayerRole.Background };
        Roles = choices.Select(r => new MemorialImageRoleChoice(r, Strings.Resolve("AiMemorial_Role" + (r?.ToString() ?? "Reference")))).ToArray();
        selectedRole = Roles.FirstOrDefault(r => r.Role == previous?.Role) ?? Roles[0];
        var bounds = previous?.Bounds ?? suggestion;
        x = MemorialLayoutEditor.Number(bounds.X); y = MemorialLayoutEditor.Number(bounds.Y); width = MemorialLayoutEditor.Number(bounds.Width);
    }

    public AiDesignAttachment Asset { get; }
    public DecodedPreview Image { get; }
    public RectPx Source { get; }
    public IReadOnlyList<MemorialImageRoleChoice> Roles { get; }
    public MemorialImageRoleChoice SelectedRole
    {
        get => selectedRole;
        set { if (value is not null && Roles.Contains(value) && SetProperty(ref selectedRole, value)) edited(); }
    }
    public string X { get => x; set { if (SetProperty(ref x, value)) edited(); } }
    public string Y { get => y; set { if (SetProperty(ref y, value)) edited(); } }
    public string Width { get => width; set { if (SetProperty(ref width, value)) { OnPropertyChanged(nameof(Height)); edited(); } } }
    public double Height => MemorialLayoutEditor.Parse(Width) * (crop ?? Source).Height / (crop ?? Source).Width;

    internal MemorialImageLayer? ToLayer() => SelectedRole.Role is not { } role ? null :
        new(Asset.Id, role, new(MemorialLayoutEditor.Parse(X), MemorialLayoutEditor.Parse(Y), MemorialLayoutEditor.Parse(Width), Height), crop, order)
        { SourceExtent = Source, AllowEdgeExtension = role == MemorialLayerRole.GeneratedDecor && allowEdgeExtension };
}

public sealed class MemorialTextInput : ObservableObject
{
    private readonly Action edited;
    private string fontFamily;
    private string fontSize, x, y, width, height, arcRadius;
    private MemorialTextPlacementChoice placement;

    internal MemorialTextInput(string key, string value, MemorialTextStyle style, double defaultRadius, Action edited)
    {
        Key = key; Text = value; this.edited = edited;
        fontFamily = style.FontFamily; fontSize = MemorialLayoutEditor.Number(style.FontSizeMm);
        (x, y, width, height) = (MemorialLayoutEditor.Number(style.Bounds.X), MemorialLayoutEditor.Number(style.Bounds.Y),
            MemorialLayoutEditor.Number(style.Bounds.Width), MemorialLayoutEditor.Number(style.Bounds.Height));
        arcRadius = MemorialLayoutEditor.Number(style.ArcRadiusMm ?? defaultRadius);
        placement = Placements.Single(choice => choice.Placement == style.Placement);
    }
    public string Key { get; }
    public string Text { get; }
    public IReadOnlyList<MemorialTextPlacementChoice> Placements { get; } = Enum.GetValues<MemorialTextPlacement>()
        .Select(p => new MemorialTextPlacementChoice(p, Strings.Resolve("AiMemorial_Text" + p))).ToArray();
    public MemorialTextPlacementChoice Placement
    {
        get => placement;
        set { if (value is not null && Placements.Contains(value) && SetProperty(ref placement, value)) { OnPropertyChanged(nameof(IsArc)); edited(); } }
    }
    public bool IsArc => Placement.Placement == MemorialTextPlacement.Arc;
    public string FontFamily { get => fontFamily; set { if (SetProperty(ref fontFamily, value)) edited(); } }
    public string FontSize { get => fontSize; set { if (SetProperty(ref fontSize, value)) edited(); } }
    public string X { get => x; set { if (SetProperty(ref x, value)) edited(); } }
    public string Y { get => y; set { if (SetProperty(ref y, value)) edited(); } }
    public string Width { get => width; set { if (SetProperty(ref width, value)) edited(); } }
    public string Height { get => height; set { if (SetProperty(ref height, value)) edited(); } }
    public string ArcRadius { get => arcRadius; set { if (SetProperty(ref arcRadius, value)) edited(); } }
    internal MemorialTextStyle ToStyle() => new(Key, FontFamily, MemorialLayoutEditor.Parse(FontSize),
        new(MemorialLayoutEditor.Parse(X), MemorialLayoutEditor.Parse(Y), MemorialLayoutEditor.Parse(Width), MemorialLayoutEditor.Parse(Height)),
        Placement.Placement, IsArc ? MemorialLayoutEditor.Parse(ArcRadius) : null);
}

internal static class MemorialLayoutEditor
{
    internal static string Number(double number) => number.ToString("G", CultureInfo.CurrentCulture);
    internal static double Parse(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out double number) ? number : double.NaN;
    internal static MemorialCanvas? Preset(AiProductKind? product) => product switch
    {
        AiProductKind.MemorialBadge => MemorialLayoutModule.BadgePreset,
        AiProductKind.MemorialTshirt => MemorialLayoutModule.TshirtPreset,
        _ => null,
    };

    internal static RectMm Fit(int width, int height, RectMm area)
    {
        double scale = Math.Min(area.Width / width, area.Height / height);
        return new(area.X + (area.Width - width * scale) / 2, area.Y + (area.Height - height * scale) / 2, width * scale, height * scale);
    }

    internal static RectMm SafeRectangle(MemorialCanvas canvas)
    {
        double w = canvas.SafeDiameterMm is { } safe ? safe / Math.Sqrt(2) * .98 : canvas.WidthMm * .9;
        double h = canvas.SafeDiameterMm.HasValue ? w : canvas.HeightMm * .9;
        return new((canvas.WidthMm - w) / 2, (canvas.HeightMm - h) / 2, w, h);
    }

    internal static (IReadOnlyList<MemorialImageInput> Images, IReadOnlyList<MemorialTextInput> Text) Propose(
        AiAuthoringDraft draft, MemorialCanvas canvas, IEnumerable<AiPromptPhotoSlot> photos, Action edited)
    {
        var safe = SafeRectangle(canvas);
        var personSlots = draft.TemplateSnapshot.Fields.Where(f => f.Semantic == AiTemplateSemantic.PersonImage).Select(f => f.SlotId).ToHashSet();
        var people = draft.Bindings.Where(b => personSlots.Contains(b.SlotId)).Select(b => b.AssetId).ToHashSet();
        var assets = AiDetailedReview.ActiveAssets(draft);
        var keepDecor = assets.Where(a => !people.Contains(a.Id) && draft.Preservation.Any(r => r.AssetId == a.Id && r.KeepContent)).ToArray();
        var images = new List<MemorialImageInput>();
        foreach (var asset in assets)
        {
            var image = photos.FirstOrDefault(p => p.Asset?.Id == asset.Id && p.Preview is not null)?.Preview;
            if (image is not { SourcePixelWidth: > 0, SourcePixelHeight: > 0 }) continue;
            bool person = people.Contains(asset.Id), keep = keepDecor.Contains(asset);
            RectMm area = person ? new(safe.X, safe.Y + safe.Height * .22, safe.Width, safe.Height * (keepDecor.Length == 0 ? .5 : .36))
                : keep ? new(safe.X + safe.Width / keepDecor.Length * Array.IndexOf(keepDecor, asset), safe.Y + safe.Height * .59,
                    safe.Width / keepDecor.Length, safe.Height * .13) : new(0, 0, canvas.WidthMm, canvas.HeightMm);
            var previous = draft.Layout?.Images.SingleOrDefault(i => i.AssetId == asset.Id);
            images.Add(new(asset, image, person, keep, previous, Fit(image.SourcePixelWidth, image.SourcePixelHeight, area), asset.Order, edited));
        }
        var inputs = MemorialLayoutModule.TextInputs(draft);
        var fields = AiDetailedReview.TextFields(draft).ToArray();
        var keys = fields.SelectMany(f => AiAuthoringConfirmationRules.IsDate(f)
            ? new[] { MemorialLayoutModule.DateLabelKey(f), AiAuthoringConfirmationRules.TextKey(f) }
            : [AiAuthoringConfirmationRules.TextKey(f)]).Where(k => inputs.TryGetValue(k, out var text) && !string.IsNullOrEmpty(text)).ToArray();
        int lower = 0, lowerCount = Math.Max(1, keys.Count(k => k != "name"));
        var textRows = keys.Select(k =>
        {
            double row = safe.Height * .26 / lowerCount;
            bool name = k == "name";
            var bounds = name ? new RectMm(safe.X, safe.Y, safe.Width, safe.Height * .2)
                : new(safe.X, safe.Y + safe.Height * .74 + row * lower++, safe.Width, row);
            var previous = draft.Layout?.Text.SingleOrDefault(t => t.Key == k);
            var style = previous is not null ? new MemorialTextStyle(k, previous.FontFamily, previous.FontSizeMm, previous.Bounds, previous.Placement, previous.ArcRadiusMm)
                : new(k, "Segoe UI", Math.Clamp(bounds.Height * .48, 2, name ? 12 : 6), bounds, MemorialTextPlacement.Flat, null);
            return new MemorialTextInput(k, inputs[k], style, safe.Width / 2, edited);
        }).ToArray();
        return (images.AsReadOnly(), textRows);
    }
}
