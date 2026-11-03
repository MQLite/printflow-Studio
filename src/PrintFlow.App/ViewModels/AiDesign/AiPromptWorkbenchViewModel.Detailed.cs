using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Resources;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Workflow.AiDesign.Authoring;

namespace PrintFlow.App.ViewModels.AiDesign;

public sealed partial class AiPromptWorkbenchViewModel
{
    private int detailedStep = 1;
    private Guid? detailedDraftId;
    private AiTemplateRef? detailedTemplate;
    private bool hasDetailedBaseline;
    private readonly Dictionary<string, string> detailedOriginalValues = new(StringComparer.Ordinal);
    public IAsyncRelayCommand SwitchToDetailedCommand { get; private set; } = null!;
    public IAsyncRelayCommand SwitchToQuickCommand { get; private set; } = null!;
    public IAsyncRelayCommand ReconcileCommand { get; private set; } = null!;
    public IAsyncRelayCommand<AiDetailedTextCheck> ConfirmTextCommand { get; private set; } = null!;
    public IAsyncRelayCommand<AiPreservationCheck> ConfirmPreservationCommand { get; private set; } = null!;
    public IRelayCommand<int> SelectDetailedStepCommand { get; private set; } = null!;
    public IRelayCommand NextDetailedStepCommand { get; private set; } = null!;
    public IReadOnlyList<AiDetailedStep> DetailedSteps { get; private set; } = [];
    public IReadOnlyList<AiDetailedTextCheck> TextChecks { get; private set; } = [];
    public IReadOnlyList<AiPreservationCheck> PreservationChecks { get; private set; } = [];
    public IEnumerable<AiPromptFieldInput> QuickFields => Fields.Where(x => !x.IsDateFormat);
    public bool IsDetailed => Draft?.Mode == AiAuthoringMode.Detailed;
    public bool CanPreviewLayout => false;
    public bool ReconciliationNeeded => Draft is not null && AiAuthoringConfirmationRules.NeedsReconciliation(ReviewDraft());
    public bool HasUnsavedPreservationChanges => Draft is not null && PreservationChecks.Any(x => x.IsEdited && !Draft.Preservation.Contains(x.Rule));
    public int DetailedStep
    {
        get => detailedStep;
        set { if (value is >= 1 and <= 6 && SetProperty(ref detailedStep, value)) NotifyDetailed(); }
    }
    public string DetailedSummary => Draft is null ? "" : Draft.TemplateSnapshot.Name + " · " + Strings.AiDetail_FirstPending + " " + AiDetailedReview.GetFirstIncompleteStep(ReviewDraft());
    public IReadOnlyList<string> DateFormats => AiDetailedReview.DateFormats;
    public bool IsStep1 => DetailedStep == 1;
    public bool IsStep2 => DetailedStep == 2;
    public bool IsStep3 => DetailedStep == 3;
    public bool IsStep4 => DetailedStep == 4;
    public bool IsStep5 => DetailedStep == 5;
    public bool IsStep6 => DetailedStep == 6;

    private void InitializeDetailedCommands()
    {
        SwitchToDetailedCommand = new AsyncRelayCommand(() => SwitchModeAsync(AiAuthoringMode.Detailed));
        SwitchToQuickCommand = new AsyncRelayCommand(() => SwitchModeAsync(AiAuthoringMode.Quick));
        ReconcileCommand = new AsyncRelayCommand(ReconcileAsync);
        ConfirmTextCommand = new AsyncRelayCommand<AiDetailedTextCheck>(ConfirmTextAsync);
        ConfirmPreservationCommand = new AsyncRelayCommand<AiPreservationCheck>(ConfirmPreservationAsync);
        SelectDetailedStepCommand = new RelayCommand<int>(step => DetailedStep = step);
        NextDetailedStepCommand = new RelayCommand(() => DetailedStep = Math.Min(6, DetailedStep + 1));
        DetailedSteps = Enumerable.Range(1, 6).Select(number => new AiDetailedStep(this, number)).ToArray();
    }
    private async Task SwitchModeAsync(AiAuthoringMode mode) => await RunAsync(async () =>
    {
        if ((await FlushCoreAsync(default)).IsFailure) return;
        if (Draft!.Mode == mode) return;
        long capturedEpoch = epoch, generation = editGeneration;
        var result = await ApplyCoreAsync(new AiAuthoringEdit.SwitchMode(mode), default);
        if (result.IsFailure || !IsCurrent(capturedEpoch) || generation != editGeneration) return;
        epoch++; ClearPending();
        if (mode == AiAuthoringMode.Detailed)
        {
            if (detailedDraftId != result.Value.Id || detailedTemplate != result.Value.Template || !hasDetailedBaseline)
            {
                detailedOriginalValues.Clear(); detailedDraftId = result.Value.Id; detailedTemplate = result.Value.Template;
                foreach (var input in Fields) detailedOriginalValues[input.Key] = input.Value;
                hasDetailedBaseline = true;
            }
            DetailedStep = AiDetailedReview.GetFirstIncompleteStep(result.Value);
        }
        await PresentAsync(result.Value, default);
    }, default);

    internal AiAuthoringDraft ReviewDraft()
    {
        var value = Draft!; var fields = new Dictionary<string, string>(value.Fields, StringComparer.Ordinal);
        foreach (var input in Fields)
            if (fields.ContainsKey(input.Key) || input.Value != input.InitialValue) fields[input.Key] = input.Value;
        var rules = value.Preservation.ToDictionary(x => x.AssetId);
        foreach (var input in PreservationChecks.Where(x => x.IsEdited)) rules[input.Asset.Id] = input.Rule;
        return value with { Fields = fields, Preservation = rules.Values.ToArray(), FinalDocument = Document,
            IsManuallyEdited = value.IsManuallyEdited || !Document.Parts.SequenceEqual(value.FinalDocument.Parts) };
    }
    internal AiAuthoringDraft ReviewWithRule(AiPreservationRule rule)
    {
        var value = ReviewDraft();
        return value with { Preservation = value.Preservation.Where(x => x.AssetId != rule.AssetId).Append(rule).ToArray() };
    }
    private void RefreshDetailed()
    {
        if (Draft is null) return;
        if (detailedDraftId != Draft.Id || detailedTemplate != Draft.Template)
        {
            detailedDraftId = Draft.Id; detailedTemplate = Draft.Template; detailedOriginalValues.Clear(); hasDetailedBaseline = false;
        }
        if (IsDetailed && !hasDetailedBaseline)
        {
            foreach (var input in Fields) detailedOriginalValues[input.Key] = input.Value;
            hasDetailedBaseline = true;
        }
        TextChecks = AiDetailedReview.TextFields(Draft).Select(definition => new AiDetailedTextCheck(this, definition,
            Fields.Single(x => x.Key == definition.Key), Fields.SingleOrDefault(x => x.IsDateFormat && x.DateFieldKey == definition.Key),
            Fields.SingleOrDefault(x => !x.IsDateFormat && x.DateFieldKey == definition.Key), detailedOriginalValues.GetValueOrDefault(definition.Key, Fields.Single(x => x.Key == definition.Key).Value), epoch, Draft.Id)).ToArray();
        PreservationChecks = AiDetailedReview.ActiveAssets(Draft).Select(asset => new AiPreservationCheck(this, asset,
            Draft.Preservation.SingleOrDefault(x => x.AssetId == asset.Id) ?? new(asset.Id, true, true, true, false, false), epoch, Draft.Id,
            string.Join(" / ", Photos.Where(x => x.Asset?.Id == asset.Id).Select(x => x.Role)), Photos.FirstOrDefault(x => x.Asset?.Id == asset.Id))).ToArray();
        OnPropertyChanged(nameof(TextChecks)); OnPropertyChanged(nameof(PreservationChecks)); OnPropertyChanged(nameof(IsDetailed)); OnPropertyChanged(nameof(QuickFields));
        NotifyDetailed();
    }
    private void NotifyDetailed()
    {
        OnPropertyChanged(nameof(ReconciliationNeeded)); OnPropertyChanged(nameof(DetailedSummary));
        foreach (string property in new[] { nameof(IsStep1), nameof(IsStep2), nameof(IsStep3), nameof(IsStep4), nameof(IsStep5), nameof(IsStep6) }) OnPropertyChanged(property);
        foreach (var check in TextChecks) check.Refresh();
        foreach (var check in PreservationChecks) check.Refresh();
        foreach (var step in DetailedSteps) step.Refresh();
    }
    private async Task ConfirmTextAsync(AiDetailedTextCheck? check)
    {
        if (check is null || !IsCurrent(check.Epoch) || Draft?.Id != check.DraftId) return;
        string? expected = AiAuthoringConfirmationRules.ContentHash(ReviewDraft(), check.Key);
        await ConfirmDetailedAsync(new AiAuthoringEdit.ConfirmField(check.Key, expected ?? ""));
    }
    private async Task ConfirmPreservationAsync(AiPreservationCheck? check)
    {
        if (check is null || !IsCurrent(check.Epoch) || Draft?.Id != check.DraftId) return;
        var rule = check.Rule;
        string? expected = AiAuthoringConfirmationRules.ContentHash(ReviewWithRule(rule), check.Key);
        await ConfirmDetailedAsync(new AiAuthoringEdit.ConfirmField(check.Key, expected ?? ""), rule);
    }
    private async Task ReconcileAsync()
    {
        if (Draft is null) return;
        var displayed = ReviewDraft();
        await ConfirmDetailedAsync(new AiAuthoringEdit.AcknowledgeDocumentReconciliation(
            AiAuthoringConfirmationRules.DocumentHash(displayed.FinalDocument), AiAuthoringConfirmationRules.FieldsHash(displayed)));
    }
    private async Task ConfirmDetailedAsync(AiAuthoringEdit edit, AiPreservationRule? rule = null)
    {
        long requestedEpoch = epoch; var requestedDraft = Draft?.Id;
        await RunAsync(async () =>
        {
            if (!IsCurrent(requestedEpoch) || Draft?.Id != requestedDraft || (await FlushCoreAsync(default)).IsFailure) return;
            long generation = editGeneration;
            if (rule is not null && !Draft!.Preservation.Contains(rule) && (await ApplyCoreAsync(new AiAuthoringEdit.SetPreservation(rule), default)).IsFailure) return;
            if (!IsCurrent(requestedEpoch) || generation != editGeneration) return;
            var result = await ApplyCoreAsync(edit, default);
            if (result.IsSuccess && IsCurrent(requestedEpoch) && generation == editGeneration) await PresentAsync(result.Value, default);
        }, default);
    }
}

public sealed class AiDetailedStep(AiPromptWorkbenchViewModel owner, int number) : ObservableObject
{
    public int Number => number;
    public string Title => number switch { 1 => Strings.AiDetail_Step1, 2 => Strings.AiDetail_Step2, 3 => Strings.AiDetail_Step3, 4 => Strings.AiDetail_Step4, 5 => Strings.AiDetail_Step5, _ => Strings.AiDetail_Step6 };
    public string Status => owner.DetailedStep == number ? Strings.AiDetail_Current : owner.Draft is not null && number < AiDetailedReview.GetFirstIncompleteStep(owner.ReviewDraft()) ? Strings.AiDetail_Confirmed : Strings.AiDetail_Pending;
    internal void Refresh() => OnPropertyChanged(nameof(Status));
}

public sealed class AiDetailedTextCheck(AiPromptWorkbenchViewModel owner, AiTemplateField definition, AiPromptFieldInput input,
    AiPromptFieldInput? format, AiPromptFieldInput? label, string original, long epoch, Guid draftId) : ObservableObject
{
    public string Key => AiAuthoringConfirmationRules.TextKey(definition);
    public string ValueAutomationId => "AiDetail.Value." + Key;
    public AiPromptFieldInput Input => input;
    public AiPromptFieldInput? FormatInput => format;
    public string? SelectedFormat
    {
        get => string.IsNullOrEmpty(format?.Value) ? null : format.Value;
        set { if (format is not null && value is not null && AiDetailedReview.DateFormats.Contains(value, StringComparer.Ordinal)) format.Value = value; }
    }
    public AiPromptFieldInput? LabelInput => label;
    public bool HasLabel => label is not null;
    public bool IsDate => AiAuthoringConfirmationRules.IsDate(definition);
    public string OriginalValue => original;
    public long Epoch => epoch;
    public Guid DraftId => draftId;
    public bool IsConfirmed => AiAuthoringConfirmationRules.IsConfirmed(owner.ReviewDraft(), Key);
    public bool CanConfirm => AiAuthoringConfirmationRules.CanConfirm(owner.ReviewDraft(), Key);
    public string Status => IsDate && !definition.Required && string.IsNullOrWhiteSpace(input.Value) ? Strings.AiDetail_Omitted : IsConfirmed ? Strings.AiDetail_Confirmed : Strings.AiDetail_Pending;
    public string? DateInterpretation => IsDate ? AiDetailedReview.InterpretDate(input.Value, format?.Value ?? "") : null;
    public string? Warning => string.Join(" ", AiDetailedReview.GetIssues(owner.ReviewDraft()).Where(x => x.FieldKey == Key || x.Key == "date-order" && IsDate)
        .Select(x => x.Key switch { "unusual-year" => Strings.AiDetail_UnusualYear, "date-format-required" => Strings.AiDetail_FormatRequired, "date-order" => Strings.AiDetail_DateOrder, _ => Strings.AiDetail_InvalidDate }));
    internal void Refresh() { foreach (string property in new[] { nameof(IsConfirmed), nameof(CanConfirm), nameof(Status), nameof(DateInterpretation), nameof(Warning), nameof(SelectedFormat) }) OnPropertyChanged(property); }
}

public sealed class AiPreservationCheck : ObservableObject
{
    private readonly AiPromptWorkbenchViewModel owner;
    private bool keep, move, scale, crop, redraw;
    public AiPreservationCheck(AiPromptWorkbenchViewModel owner, AiDesignAttachment asset, AiPreservationRule rule, long epoch, Guid draftId, string roles, AiPromptPhotoSlot? photo)
    {
        this.owner = owner; Asset = asset; Epoch = epoch; DraftId = draftId; Roles = roles; Photo = photo;
        keep = rule.KeepContent; move = rule.AllowMove; scale = rule.AllowUniformScale; crop = rule.AllowCrop; redraw = rule.AllowRedraw;
    }
    public AiDesignAttachment Asset { get; }
    public AiPromptPhotoSlot? Photo { get; }
    public string Roles { get; }
    public long Epoch { get; }
    public Guid DraftId { get; }
    public string Key => AiAuthoringConfirmationRules.PreservationKey(Asset.Id);
    public bool IsEdited { get; private set; }
    public bool KeepContent { get => keep; set { if (SetProperty(ref keep, value)) Changed(); } }
    public bool AllowMove { get => move; set { if (SetProperty(ref move, value)) Changed(); } }
    public bool AllowUniformScale { get => scale; set { if (SetProperty(ref scale, value)) Changed(); } }
    public bool AllowCrop { get => crop; set { if (SetProperty(ref crop, value)) Changed(); } }
    public bool AllowRedraw { get => redraw; set { if (SetProperty(ref redraw, value)) Changed(); } }
    public AiPreservationRule Rule => new(Asset.Id, KeepContent, AllowMove, AllowUniformScale, AllowCrop, AllowRedraw);
    public bool IsConfirmed => AiAuthoringConfirmationRules.IsConfirmed(owner.ReviewWithRule(Rule), Key);
    public bool HasConflict => KeepContent && AllowRedraw;
    public bool CanConfirm => !HasConflict;
    public string Status => HasConflict ? Strings.AiDetail_PreservationConflict : IsConfirmed ? Strings.AiDetail_Confirmed : Strings.AiDetail_Pending;
    private void Changed() { IsEdited = true; owner.PreservationEdited(); Refresh(); }
    internal void Refresh() { OnPropertyChanged(nameof(IsConfirmed)); OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(HasConflict)); OnPropertyChanged(nameof(CanConfirm)); }
}
