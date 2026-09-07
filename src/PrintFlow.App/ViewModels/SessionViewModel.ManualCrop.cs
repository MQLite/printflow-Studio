using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Trimming;

namespace PrintFlow.App.ViewModels;

public sealed partial class SessionViewModel
{
    // Draft input belongs only to this editor. Applying sends the decision to the producing
    // attempt; neither editing nor cancelling changes the session's automatic Trim margin.
    [ObservableProperty] private TrimMode _manualCropMode;
    [ObservableProperty] private string _manualCropUniformMargin = "0";
    [ObservableProperty] private string _manualCropMarginLeft = "0";
    [ObservableProperty] private string _manualCropMarginTop = "0";
    [ObservableProperty] private string _manualCropMarginRight = "0";
    [ObservableProperty] private string _manualCropMarginBottom = "0";

    public bool IsManualCropTight { get => ManualCropMode == TrimMode.TightCrop; set { if (value) ManualCropMode = TrimMode.TightCrop; } }
    public bool IsManualCropUniform { get => ManualCropMode == TrimMode.UniformMargin; set { if (value) ManualCropMode = TrimMode.UniformMargin; } }
    public bool IsManualCropPerEdge { get => ManualCropMode == TrimMode.EdgeSpecificMargin; set { if (value) ManualCropMode = TrimMode.EdgeSpecificMargin; } }

    public string ManualCropAdjustmentLabel => Strings.Session_ManualCropAdjustment;
    public string ManualCropTightLabel => Strings.Session_ManualCropTight;
    public string ManualCropUniformLabel => Strings.Session_ManualCropUniform;
    public string ManualCropPerEdgeLabel => Strings.Session_ManualCropPerEdge;
    public string ManualCropSelectedLabel => Strings.Session_ManualCropSelected;
    public string ManualCropAppliedLabel => Strings.Session_ManualCropApplied;
    public string ManualCropMarginInvalidNotice => Strings.Session_ManualCropMarginInvalid;
    public bool IsManualCropMarginInvalid => !TryReadManualCropMargin(out _);

    public ManualCropGeometry? DraftManualCropGeometry =>
        CropSelection is { IsEmpty: false } selection && CropPane is { } pane &&
        selection.FitsWithin(pane.SourcePixelWidth, pane.SourcePixelHeight) && TryReadManualCropMargin(out var margin)
            ? ManualCropGeometry.Create(selection, margin, pane.SourcePixelWidth, pane.SourcePixelHeight)
            : null;

    public TrimBounds? CropAppliedBounds => DraftManualCropGeometry?.AppliedBounds;
    public string CropAppliedSummary => CropAppliedBounds is { } bounds
        ? $"{ManualCropAppliedLabel}: {ManualBounds(bounds)}"
        : string.Empty;

    public bool HasManualCropGeometry => _session?.HasManualCropGeometry == true;
    public string ManualCropReviewSelected => _session?.CurrentManualCropGeometry is { } g
        ? $"{ManualCropSelectedLabel}: {ManualBounds(g.SelectedBounds)}" : string.Empty;
    public string ManualCropReviewApplied => _session?.CurrentManualCropGeometry is { } g
        ? $"{ManualCropAppliedLabel}: {ManualBounds(g.AppliedBounds)}" : string.Empty;
    public string ManualCropReviewMargin => _session?.CurrentManualCropGeometry is { } g
        ? g.Margin.Mode switch
        {
            TrimMode.TightCrop => ManualCropTightLabel,
            TrimMode.UniformMargin => $"{ManualCropUniformLabel}: {g.Margin.Top} px",
            _ => $"{ManualCropPerEdgeLabel}: {TrimLeftLabel} {g.Margin.Left}, {TrimTopLabel} {g.Margin.Top}, {TrimRightLabel} {g.Margin.Right}, {TrimBottomLabel} {g.Margin.Bottom} px",
        } : string.Empty;

    private static string ManualBounds(TrimBounds bounds) => string.Format(
        CultureInfo.CurrentCulture, "[{0}, {1} → {2}, {3}) · {4} × {5} px",
        bounds.Left, bounds.Top, bounds.RightExclusive, bounds.BottomExclusive, bounds.Width, bounds.Height);

    private bool TryReadManualCropMargin(out ManualCropMargin margin)
    {
        margin = ManualCropMargin.Tight;
        if (IsManualCropTight) return true;
        if (IsManualCropUniform && PixelValue(ManualCropUniformMargin, out int uniform))
        {
            margin = ManualCropMargin.Uniform(uniform);
            return true;
        }
        if (IsManualCropPerEdge && PixelValue(ManualCropMarginTop, out int top) &&
            PixelValue(ManualCropMarginRight, out int right) && PixelValue(ManualCropMarginBottom, out int bottom) &&
            PixelValue(ManualCropMarginLeft, out int left))
        {
            margin = ManualCropMargin.PerEdge(top, right, bottom, left);
            return true;
        }
        return false;
    }

    private static bool PixelValue(string text, out int pixels) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out pixels) && pixels >= 0;

    private void ResetManualCropAdjustment()
    {
        ManualCropMode = TrimMode.TightCrop;
        ManualCropUniformMargin = ManualCropMarginLeft = ManualCropMarginTop = ManualCropMarginRight = ManualCropMarginBottom = "0";
    }

    private void NotifyManualCropDraft()
    {
        OnPropertyChanged(nameof(DraftManualCropGeometry));
        OnPropertyChanged(nameof(CropAppliedBounds));
        OnPropertyChanged(nameof(CropAppliedSummary));
        OnPropertyChanged(nameof(IsManualCropMarginInvalid));
        OnPropertyChanged(nameof(CanApplyManualCrop));
    }

    partial void OnManualCropModeChanged(TrimMode value)
    {
        OnPropertyChanged(nameof(IsManualCropTight));
        OnPropertyChanged(nameof(IsManualCropUniform));
        OnPropertyChanged(nameof(IsManualCropPerEdge));
        NotifyManualCropDraft();
    }

    partial void OnManualCropUniformMarginChanged(string value) => NotifyManualCropDraft();
    partial void OnManualCropMarginLeftChanged(string value) => NotifyManualCropDraft();
    partial void OnManualCropMarginTopChanged(string value) => NotifyManualCropDraft();
    partial void OnManualCropMarginRightChanged(string value) => NotifyManualCropDraft();
    partial void OnManualCropMarginBottomChanged(string value) => NotifyManualCropDraft();
}
