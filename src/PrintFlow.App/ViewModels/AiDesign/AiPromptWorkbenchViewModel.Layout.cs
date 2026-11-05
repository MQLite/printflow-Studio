using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Resources;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.AiDesign.Memorial;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.AiDesign.Authoring;
using PrintFlow.Workflow.AiDesign.Memorial;

namespace PrintFlow.App.ViewModels.AiDesign;

public sealed partial class AiPromptWorkbenchViewModel
{
    private AiAuthoringDraft? layoutEditorDraft;
    private bool memorialLayoutDirty;

    public IReadOnlyList<MemorialImageInput> MemorialImages { get; private set; } = [];
    public IReadOnlyList<MemorialTextInput> MemorialText { get; private set; } = [];
    public bool IsMemorialLayoutAvailable => memorial is not null && MemorialLayoutEditor.Preset(Draft?.Product) is not null;
    public bool IsMemorialLayoutEditorOpen => layoutEditorDraft is not null;
    public bool CanPrepareMemorialLayout => IsMemorialLayoutAvailable && CanEdit && Draft is not null &&
        AiDetailedReview.GetFirstIncompleteStep(ReviewDraft()) == 5;
    public bool CanSaveMemorialLayout => CanEdit && memorialLayoutDirty;
    public bool CanConfirmMemorialLayout => CanEdit && IsMemorialLayoutAvailable && !HasUnsavedChanges &&
        Draft?.Layout is { ProductTemplateConfirmed: false } layout && CanConfirmLayout(Draft, layout);
    public bool CanPreviewLayout => CanEdit && HasCurrentConfirmedLayout;
    private bool HasCurrentConfirmedLayout => IsMemorialLayoutAvailable && !HasUnsavedChanges &&
        Draft?.Layout is { ProductTemplateConfirmed: true } layout && CanConfirmLayout(Draft, layout);
    public string? MemorialCanvasLabel => (Draft?.Layout?.Canvas ?? MemorialLayoutEditor.Preset(Draft?.Product)) is { } canvas
        ? CanvasLabel(canvas) : null;
    public string MemorialLayoutStatus => L(Draft?.Layout?.ProductTemplateConfirmed == true && !HasUnsavedChanges
        ? "LayoutConfirmed" : "LayoutPending");
    public string? MemorialLayoutIssues => Draft?.Layout is { } layout && Draft is { } value
        ? FormatLayoutIssues(MemorialLayoutModule.Validate(value, layout)) : null;

    public IAsyncRelayCommand PrepareMemorialLayoutCommand { get; private set; } = null!;
    public IAsyncRelayCommand SaveMemorialLayoutCommand { get; private set; } = null!;
    public IRelayCommand DiscardMemorialLayoutCommand { get; private set; } = null!;
    public IAsyncRelayCommand ConfirmMemorialLayoutCommand { get; private set; } = null!;

    private void InitializeMemorialLayoutCommands()
    {
        PrepareMemorialLayoutCommand = new AsyncRelayCommand(PrepareMemorialLayoutAsync);
        SaveMemorialLayoutCommand = new AsyncRelayCommand(async () => { if (CanSaveMemorialLayout) await FlushAsync(default); });
        DiscardMemorialLayoutCommand = new RelayCommand(ResetMemorialLayoutEditor);
        ConfirmMemorialLayoutCommand = new AsyncRelayCommand(ConfirmMemorialLayoutAsync);
    }

    private async Task PrepareMemorialLayoutAsync()
    {
        if (!CanPrepareMemorialLayout) return;
        await RunAsync(async () =>
        {
            if ((await FlushCoreAsync(default)).IsFailure || Draft is not { } value ||
                AiDetailedReview.GetFirstIncompleteStep(value) != 5 ||
                (value.Layout?.Canvas ?? MemorialLayoutEditor.Preset(value.Product)) is not { } canvas) return;
            var proposed = MemorialLayoutEditor.Propose(value, canvas, Photos, MemorialLayoutEdited);
            if (proposed.Images.Count != AiDetailedReview.ActiveAssets(value).Count)
            { Error = L("SourceUnavailable"); return; }
            layoutEditorDraft = value;
            MemorialImages = proposed.Images;
            MemorialText = proposed.Text;
            memorialLayoutDirty = value.Layout is null || !proposed.Text.Select(t => (t.Key, t.Text)).SequenceEqual(value.Layout.Text.Select(t => (t.Key, t.Text))) ||
                !proposed.Images.Select(i => i.ToLayer()).OfType<MemorialImageLayer>().SequenceEqual(value.Layout.Images);
            if (memorialLayoutDirty) Edited();
            NotifyMemorialLayoutEditor();
        }, default);
    }

    private void MemorialLayoutEdited()
    {
        if (layoutEditorDraft is null) return;
        memorialLayoutDirty = true;
        Edited();
    }

    // Called inside the workbench's serial save, after the exact text and permission inputs have been saved.
    private async Task<OperationResult<AiAuthoringDraft>> SaveMemorialLayoutCoreAsync(CancellationToken ct)
    {
        if (memorial is null || Draft is not { } current || layoutEditorDraft is not { } opened ||
            current.Id != opened.Id || !MemorialLayoutModule.SameInputs(current, opened) ||
            !MemorialLayoutModule.SameContent(current.Layout, opened.Layout) ||
            (current.Layout?.Canvas ?? MemorialLayoutEditor.Preset(current.Product)) is not { } canvas)
        { Error = L("PlacementInputsChanged"); return Refused(); }
        var images = MemorialImages.Select(i => i.ToLayer()).OfType<MemorialImageLayer>().ToArray();
        var styles = MemorialText.Select(t => t.ToStyle()).ToArray();
        if (images.Any(i => !ValidBounds(i.Bounds)) || styles.Any(t => !ValidBounds(t.Bounds) ||
            !double.IsFinite(t.FontSizeMm) || t.FontSizeMm <= 0 || t.ArcRadiusMm is { } r && (!double.IsFinite(r) || r <= 0)))
        { Error = L("InvalidPlacement"); return Refused(); }

        long capturedEpoch = epoch, generation = editGeneration;
        var text = await Task.Run(() => memorial.TextLayout.Arrange(current, canvas, styles), ct);
        if (!IsCurrent(capturedEpoch) || generation != editGeneration || Draft != current) return Refused();
        if (text.IsFailure)
        {
            string? issue = text.Failure.Context?.GetValueOrDefault("issue");
            string? field = text.Failure.Context?.GetValueOrDefault("field");
            Error = (issue is "TextDoesNotFit" or "MissingGlyph" ? L(issue) : L("TextLayoutFailed")) +
                (string.IsNullOrEmpty(field) ? "" : " · " + field);
            return Refused();
        }
        var layout = new MemorialLayout(canvas, images, text.Value, false);
        var issues = MemorialLayoutModule.Validate(current, layout);
        if (issues.Any(x => x.Key != "ProductTemplateUnconfirmed"))
        { Error = FormatLayoutIssues(issues); return Refused(); }
        var saved = await ApplyCoreAsync(new AiAuthoringEdit.SetLayout(layout), ct);
        if (saved.IsSuccess && IsCurrent(capturedEpoch) && generation == editGeneration)
        {
            layoutEditorDraft = saved.Value;
            memorialLayoutDirty = false;
            NotifyMemorialLayoutEditor();
        }
        return saved;
    }

    private async Task ConfirmMemorialLayoutAsync()
    {
        if (!CanConfirmMemorialLayout || Draft is not { Layout: { } layout } current) return;
        long capturedEpoch = epoch, generation = editGeneration;
        await RunAsync(async () =>
        {
            if (!IsCurrent(capturedEpoch) || Draft != current || generation != editGeneration || HasUnsavedChanges) return;
            var saved = await ApplyCoreAsync(new AiAuthoringEdit.SetLayout(layout with { ProductTemplateConfirmed = true }), default);
            if (saved.IsSuccess && IsCurrent(capturedEpoch) && generation == editGeneration)
            {
                if (layoutEditorDraft is not null) layoutEditorDraft = saved.Value;
                NotifyDetailed();
            }
        }, default);
    }

    private static bool CanConfirmLayout(AiAuthoringDraft value, MemorialLayout layout) =>
        AiDetailedReview.GetFirstIncompleteStep(value) == 5 &&
        MemorialLayoutModule.Validate(value, layout).All(x => x.Key == "ProductTemplateUnconfirmed");

    private static bool ValidBounds(RectMm rect) => double.IsFinite(rect.X) && double.IsFinite(rect.Y) &&
        double.IsFinite(rect.Width) && double.IsFinite(rect.Height) && rect.Width > 0 && rect.Height > 0;

    private void ResetMemorialLayoutEditor()
    {
        layoutEditorDraft = null; memorialLayoutDirty = false;
        MemorialImages = []; MemorialText = [];
        NotifyMemorialLayoutEditor();
    }

    private void NotifyMemorialLayoutEditor()
    {
        OnPropertyChanged(nameof(MemorialImages)); OnPropertyChanged(nameof(MemorialText));
        OnPropertyChanged(nameof(IsMemorialLayoutEditorOpen)); OnPropertyChanged(nameof(HasUnsavedChanges));
        NotifyMemorial();
    }

    private static string? FormatLayoutIssues(IEnumerable<AiAuthoringIssue> issues)
    {
        var messages = issues.Select(i => i.Key).Where(k => k != "ProductTemplateUnconfirmed").Distinct().Select(k => k switch
        {
            "OutsideSafeArea" or "OutsideCanvas" => L("OutsideArea"),
            "PreservationConflict" or "PreservationUnconfirmed" or "MissingPreservedLayer" or "MissingPersonLayer" => L("PlacementPermission"),
            "MissingText" or "TextMismatch" => L("TextLayoutFailed"),
            _ => L("InvalidPlacement"),
        }).Distinct().ToArray();
        return messages.Length == 0 ? null : string.Join("\n", messages);
    }

    private static string CanvasLabel(MemorialCanvas canvas) => canvas.SafeDiameterMm is { } safe
        ? string.Format(CultureInfo.CurrentCulture, L("BadgeSize"), canvas.OuterDiameterMm, safe)
        : string.Format(CultureInfo.CurrentCulture, L("ShirtSize"), canvas.WidthMm, canvas.HeightMm);

    private static string L(string key) => Strings.Resolve("AiMemorial_" + key);
}
