using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Navigation;
using PrintFlow.App.Resources;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.AiDesign.Authoring;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.App.ViewModels.AiDesign;

/// <summary>One editable draft. Module commands own persistence; this host retains unsaved UI text.</summary>
public sealed partial class AiPromptWorkbenchViewModel : ObservableObject, IAiAuthoringCloseGuard, IEntryScreenLifetime
{
    private readonly AiAuthoringModule module;
    private readonly AiTemplateCatalog catalog;
    private readonly IAiAuthoringAssetPreview preview;
    private readonly IFilePicker picker;
    private readonly IAiPromptClipboard clipboard;
    private readonly AiMemorialServices? memorial;
    private readonly SemaphoreSlim operations = new(1, 1);
    private long epoch, editGeneration;
    private bool active, busy;
    private AiAuthoringDraft? draft;
    private AiPromptDocument document = new([]);
    private string? error, status;
    private AiTemplateRef? pendingTemplate;
    private long? pendingRestore;
    private bool changePending;
    private AiTemplateEditorViewModel? templateEditor;

    public AiAuthoringDraft? Draft
    {
        get => draft;
        private set
        {
            bool changedOwner = draft?.Id != value?.Id;
            if (!SetProperty(ref draft, value)) return;
            if (changedOwner) { ResetMemorialLayoutEditor(); ResetMemorialResult(); }
            else NotifyMemorial();
        }
    }
    public AiPromptDocument Document
    {
        get => document;
        set
        {
            var frozen = AiAuthoringSnapshots.Document(value);
            if (document.Parts.SequenceEqual(frozen.Parts)) return;
            document = frozen; Edited(); OnPropertyChanged();
        }
    }
    public ObservableCollection<AiPromptFieldInput> Fields { get; } = [];
    public ObservableCollection<AiPromptPhotoSlot> Photos { get; } = [];
    public IReadOnlyList<AiTemplateDefinition> Templates { get; private set; } = [];
    public IReadOnlyList<AiDesignAttachment> AttachmentManifest { get; private set; } = [];
    public IReadOnlyList<AiAuthoringIssue> Issues { get; private set; } = [];
    public IReadOnlyList<AiAuthoringDraft> History { get; private set; } = [];
    public IReadOnlyDictionary<string, string> ReferenceLabels { get; private set; } = new Dictionary<string, string>();
    public AiPromptPhotoSlot? ExpandedPhoto { get; private set; }
    public AiTemplateEditorViewModel? TemplateEditor { get => templateEditor; private set => SetProperty(ref templateEditor, value); }
    public bool IsChangePending { get => changePending; private set => SetProperty(ref changePending, value); }
    public bool IsBusy { get => busy; private set { SetProperty(ref busy, value); OnPropertyChanged(nameof(CanEdit)); NotifyMemorial(); } }
    public bool CanEdit => !IsBusy && !isClosing;
    public bool HasUnsavedChanges => Draft is not null && (!Document.Parts.SequenceEqual(Draft.FinalDocument.Parts) ||
        Fields.Any(x => x.Value != Draft.Fields.GetValueOrDefault(x.Key, x.InitialValue)) || HasUnsavedPreservationChanges || memorialLayoutDirty);
    public string? Error { get => error; private set => SetProperty(ref error, value); }
    public string? Status { get => status; private set => SetProperty(ref status, value); }
    public string? PromptNotice
    {
        get
        {
            if (Draft is null) return null;
            if (Draft.IsManuallyEdited || !Document.Parts.SequenceEqual(Draft.FinalDocument.Parts)) return Strings.AiPrompt_ManualNotice;
            var values = Fields.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            var composed = PromptDocumentComposer.Compose(Draft.TemplateSnapshot, values).Document;
            static (string?, string?) Content(AiPromptPart part) => part is AiTextPart text ? (text.Text, null) : (null, ((AiImagePart)part).SlotId);
            return composed.Parts.Select(Content).SequenceEqual(Document.Parts.Select(Content)) ? null : Strings.AiPrompt_FieldsChangedNotice;
        }
    }
    public IAsyncRelayCommand SaveInstanceTemplateCommand { get; }
    public string ReferenceRole { get; set; } = "";
    public IAsyncRelayCommand ComposeCommand { get; }
    public IAsyncRelayCommand CopyCommand { get; }
    public IAsyncRelayCommand SaveDraftCommand { get; }
    public IAsyncRelayCommand EditTemplateCommand { get; }
    public IAsyncRelayCommand<string> PickSlotImageCommand { get; }
    public IAsyncRelayCommand ConfirmChangeCommand { get; }
    public IRelayCommand CancelChangeCommand { get; }
    public IAsyncRelayCommand<AiTemplateDefinition> SelectTemplateCommand { get; }
    public IAsyncRelayCommand RefreshHistoryCommand { get; }
    public IAsyncRelayCommand<AiAuthoringDraft> RestoreHistoryCommand { get; }
    public IAsyncRelayCommand<AiPromptPhotoSlot> RemovePhotoCommand { get; }
    public IAsyncRelayCommand<AiPromptPhotoSlot> ReusePhotoCommand { get; }
    public IAsyncRelayCommand AddReferenceCommand { get; }
    public IRelayCommand<AiPromptPhotoSlot> ExpandPhotoCommand { get; }
    public IRelayCommand ClosePhotoCommand { get; }
    public IAsyncRelayCommand CloseTemplateCommand { get; }

    public AiPromptWorkbenchViewModel(AiAuthoringModule module, AiTemplateCatalog catalog, IAiAuthoringAssetPreview preview,
        IFilePicker picker, IAiPromptClipboard clipboard, IAiDraftLeavePrompt? leavePrompt = null, INavigationService? navigation = null,
        AiMemorialServices? memorial = null)
    {
        this.module = module; this.catalog = catalog; this.preview = preview; this.picker = picker; this.clipboard = clipboard;
        this.leavePrompt = leavePrompt; this.navigation = navigation;
        this.memorial = memorial;
        ReturnHomeCommand = new AsyncRelayCommand(async () => { if (this.navigation is not null) await this.navigation.GoHomeAsync(default); });
        ShowSavedDraftsCommand = new AsyncRelayCommand(async () => await RunAsync(async () => { await ListSavedDraftsCoreAsync(default); }, default));
        OpenSavedDraftCommand = new AsyncRelayCommand<AiSavedDraftChoice>(OpenSavedDraftAsync);
        CloseSavedDraftsCommand = new AsyncRelayCommand(async () =>
        { if (Draft is null && navigation is not null) await navigation.GoHomeAsync(default); else if (Draft is not null) IsSavedDraftsVisible = false; });
        InitializeTemplateCommands();
        InitializeDetailedCommands();
        InitializeMemorialLayoutCommands();
        InitializeMemorialResultCommands();
        ComposeCommand = new AsyncRelayCommand(() => ComposeAsync(false, default));
        CopyCommand = new AsyncRelayCommand(CopyAsync);
        SaveDraftCommand = new AsyncRelayCommand(async () => { await FlushAsync(default); });
        EditTemplateCommand = new AsyncRelayCommand(EditTemplateAsync);
        SaveInstanceTemplateCommand = new AsyncRelayCommand(SaveInstanceTemplateAsync);
        PickSlotImageCommand = new AsyncRelayCommand<string>(PickSlotImageAsync);
        ConfirmChangeCommand = new AsyncRelayCommand(ConfirmChangeAsync);
        CancelChangeCommand = new RelayCommand(ClearPending);
        SelectTemplateCommand = new AsyncRelayCommand<AiTemplateDefinition>(async value =>
        { if (value is not null) await SelectTemplateAsync(value.Reference, false, default); });
        RefreshHistoryCommand = new AsyncRelayCommand(() => RefreshHistoryAsync(default));
        RestoreHistoryCommand = new AsyncRelayCommand<AiAuthoringDraft>(async value =>
        {
            if (value is null || (await FlushAsync(default)).IsFailure) return;
            pendingRestore = value.Revision; pendingTemplate = null; IsChangePending = true;
        });
        RemovePhotoCommand = new AsyncRelayCommand<AiPromptPhotoSlot>(async value =>
        { if (value?.Asset is { } asset) await EditAsync(new AiAuthoringEdit.RemoveImage(asset.Id)); });
        ReusePhotoCommand = new AsyncRelayCommand<AiPromptPhotoSlot>(async value =>
        { if (value?.SelectedAsset is { } asset) await EditAsync(new AiAuthoringEdit.BindExistingImage(value.SlotId, asset.Id)); });
        AddReferenceCommand = new AsyncRelayCommand(async () =>
        {
            if (string.IsNullOrWhiteSpace(ReferenceRole)) return;
            await EditAsync(new AiAuthoringEdit.InsertImageReference(Guid.NewGuid(), new("extra-" + Guid.NewGuid().ToString("N"), ReferenceRole, false), null));
        });
        ExpandPhotoCommand = new RelayCommand<AiPromptPhotoSlot>(value => { ExpandedPhoto = value; OnPropertyChanged(nameof(ExpandedPhoto)); });
        ClosePhotoCommand = new RelayCommand(() => { ExpandedPhoto = null; OnPropertyChanged(nameof(ExpandedPhoto)); });
        CloseTemplateCommand = new AsyncRelayCommand(async () =>
        { if (TemplateEditor?.HasUnsavedChanges != true || await RequestCloseAsync(default)) TemplateEditor = null; });
    }

    public async Task OpenAsync(Guid? draftId, CancellationToken ct)
    {
        long opening = ++epoch; active = true;
        await RunAsync(async () =>
        {
            if (Draft is not null && HasUnsavedChanges && (await FlushCoreAsync(ct)).IsFailure) return;
            if (!IsCurrent(opening)) return;
            var library = await catalog.ReadLibraryAsync(ct);
            if (library.IsFailure) { await ShowRecoveryAfterOpenFailureAsync(ct); return; }
            SetTemplates(library.Value);
            var result = draftId is { } id ? await module.ReadAsync(id, ct) :
                await module.CreateAsync(Templates.Single(x => x.Reference.Id == library.Value.DefaultId).Reference, ct);
            if (result.IsFailure) { await ShowRecoveryAfterOpenFailureAsync(ct); return; }
            if (!IsCurrent(opening)) return;
            ClearPending(); TemplateEditor = null; History = []; OnPropertyChanged(nameof(History));
            ResetMemorialLayoutEditor(); ResetMemorialResult();
            await PresentAsync(result.Value, ct);
        }, ct);
    }

    public void Deactivate()
    {
        active = false; epoch++; ClearPending();
        CancelMemorialTaskCommand.Execute(null);
        ResetMemorialLayoutEditor(); ResetMemorialResult();
    }
    private bool IsCurrent(long capturedEpoch) => active && epoch == capturedEpoch;
    private void Edited()
    {
        editGeneration++; Status = null; ClearPending(); ClearMemorialConversion();
        OnPropertyChanged(nameof(HasUnsavedChanges)); OnPropertyChanged(nameof(PromptNotice)); NotifyDetailed();
    }
    internal void PreservationEdited() => Edited();
    private void FieldEdited(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(AiPromptFieldInput.Value)) Edited(); }
    private void ClearPending() { IsChangePending = false; pendingTemplate = null; pendingRestore = null; }
    private static OperationResult<AiAuthoringDraft> Refused() => OperationResult.Fail<AiAuthoringDraft>(FailureCode.PreconditionNotMet, "Draft is unavailable or changed; retry the action.");

    public async Task<OperationResult<AiAuthoringDraft>> FlushAsync(CancellationToken ct)
    {
        var result = Refused();
        await RunAsync(async () => { result = await FlushCoreAsync(ct); }, ct);
        return result;
    }
    private async Task<OperationResult<AiAuthoringDraft>> FlushCoreAsync(CancellationToken ct)
    {
        if (Draft is null || !active) return Refused();
        long generation = editGeneration, capturedEpoch = epoch;
        var capturedDocument = Document;
        var fields = Fields.Select(x => (x.Key, x.Value, x.InitialValue)).ToArray();
        var rules = PreservationChecks.Where(x => x.IsEdited).Select(x => x.Rule).ToArray();
        if (!Draft.FinalDocument.Parts.SequenceEqual(capturedDocument.Parts))
        {
            var saved = await ApplyCoreAsync(new AiAuthoringEdit.ReplaceDocument(capturedDocument), ct);
            if (saved.IsFailure) return saved;
        }
        foreach (var (key, value, initialValue) in fields)
        {
            if (!IsCurrent(capturedEpoch)) return Refused();
            if (Draft.Fields.GetValueOrDefault(key, initialValue) == value) continue;
            var dateFormat = Fields.SingleOrDefault(x => x.Key == key && x.IsDateFormat);
            AiAuthoringEdit change = dateFormat is not null ? new AiAuthoringEdit.SetDateInterpretation(dateFormat.DateFieldKey!, value) : new AiAuthoringEdit.SetField(key, value);
            var saved = await ApplyCoreAsync(change, ct);
            if (saved.IsFailure) return saved;
        }
        foreach (var rule in rules)
        {
            if (!IsCurrent(capturedEpoch)) return Refused();
            if (Draft.Preservation.Contains(rule)) continue;
            var saved = await ApplyCoreAsync(new AiAuthoringEdit.SetPreservation(rule), ct);
            if (saved.IsFailure) return saved;
        }
        if (memorialLayoutDirty)
        {
            var saved = await SaveMemorialLayoutCoreAsync(ct);
            if (saved.IsFailure) return saved;
        }
        OnPropertyChanged(nameof(HasUnsavedChanges));
        if (!IsCurrent(capturedEpoch) || generation != editGeneration) { Error = Strings.AiPrompt_Changed; return Refused(); }
        RefreshExport(); Status = Strings.AiPrompt_Saved;
        NotifyDetailed();
        return OperationResult.Ok(Draft);
    }
    private async Task<OperationResult<AiAuthoringDraft>> ApplyCoreAsync(AiAuthoringEdit edit, CancellationToken ct)
    {
        if (Draft is null) return Refused();
        var saved = await module.ApplyAsync(Draft.Id, Draft.Revision, edit, ct);
        if (saved.IsFailure) { Status = null; Error = Strings.AiPrompt_SaveFailed; return saved; }
        if (saved.Value.Template != Draft.Template) editingTemplate = null;
        Draft = saved.Value; OnPropertyChanged(nameof(PromptNotice));
        return saved;
    }
    private async Task RunAsync(Func<Task> action, CancellationToken ct)
    {
        try
        {
            await operations.WaitAsync(ct);
            try { IsBusy = true; Error = null; await action(); }
            finally { IsBusy = false; operations.Release(); }
        }
        catch (OperationCanceledException) { Error = Strings.AiPrompt_SaveFailed; }
    }

    private async Task PresentAsync(AiAuthoringDraft value, CancellationToken ct)
    {
        long capturedEpoch = epoch;
        if (Draft?.Id != value.Id)
        {
            ReferenceRole = ""; OnPropertyChanged(nameof(ReferenceRole)); Status = null;
        }
        if (Draft?.Id != value.Id || Draft.Template != value.Template) editingTemplate = null;
        Draft = value;
        document = value.FinalDocument; OnPropertyChanged(nameof(Document));
        foreach (var input in Fields) input.PropertyChanged -= FieldEdited;
        Fields.Clear();
        foreach (var definition in value.TemplateSnapshot.Fields.Where(x => x.Kind != AiTemplateFieldKind.ImageReference))
        {
            var input = new AiPromptFieldInput(definition, value.Fields.GetValueOrDefault(definition.Key, ""));
            input.PropertyChanged += FieldEdited; Fields.Add(input);
            if (definition.Kind == AiTemplateFieldKind.OptionalDateLine)
            {
                string labelKey = AiTemplateMetadata.LabelKey(value.TemplateSnapshot, definition);
                var label = new AiPromptFieldInput(definition with { Key = labelKey }, value.Fields.GetValueOrDefault(labelKey, definition.DateLabel ?? ""), false, definition.Key);
                label.PropertyChanged += FieldEdited; Fields.Add(label);
            }
            if (AiAuthoringConfirmationRules.IsDate(definition))
            {
                string formatKey = AiTemplateMetadata.FormatKey(value.TemplateSnapshot, definition);
                var format = new AiPromptFieldInput(definition with { Key = formatKey }, value.Fields.GetValueOrDefault(formatKey, ""), true, definition.Key);
                format.PropertyChanged += FieldEdited; Fields.Add(format);
            }
        }
        Photos.Clear(); ExpandedPhoto = null; OnPropertyChanged(nameof(ExpandedPhoto));
        var activeSlots = value.FinalDocument.Parts.OfType<AiImagePart>().Select(x => x.SlotId).ToHashSet(StringComparer.Ordinal);
        foreach (var slot in value.Slots.Where(x => activeSlots.Contains(x.SlotId)))
        {
            var assetId = value.Bindings.SingleOrDefault(x => x.SlotId == slot.SlotId)?.AssetId;
            Photos.Add(new(slot, value.Assets.SingleOrDefault(x => x.Id == assetId), value.Assets));
        }
        RefreshExport(); OnPropertyChanged(nameof(HasUnsavedChanges)); OnPropertyChanged(nameof(PromptNotice));
        RefreshDetailed();
        foreach (var photo in Photos.ToArray())
        {
            if (photo.Asset is not { } asset) continue;
            var loaded = await preview.LoadAsync(value.Id, asset, ct);
            if (!IsCurrent(capturedEpoch) || Draft.Id != value.Id || Draft.Revision != value.Revision) return;
            if (loaded.IsSuccess) photo.Preview = loaded.Value;
            else photo.PreviewError = Strings.AiPrompt_PreviewFailed;
        }
        NotifyMemorial();
    }
    private void RefreshExport()
    {
        if (Draft is null) return;
        var exported = module.Export(Draft with { FinalDocument = Document });
        AttachmentManifest = exported.RequiredAttachments; Issues = exported.Issues;
        ReferenceLabels = Draft.Slots.ToDictionary(slot => slot.SlotId, slot =>
        {
            var assetId = Draft.Bindings.SingleOrDefault(x => x.SlotId == slot.SlotId)?.AssetId;
            var asset = Draft.Assets.SingleOrDefault(x => x.Id == assetId);
            return slot.Role + " · " + (asset?.UploadName ?? Strings.AiPrompt_ChoosePhoto);
        }, StringComparer.Ordinal);
        OnPropertyChanged(nameof(ReferenceLabels)); OnPropertyChanged(nameof(AttachmentManifest)); OnPropertyChanged(nameof(Issues));
    }
    private void SetTemplates(AiTemplateLibrary library)
    {
        var byId = library.Templates.ToDictionary(x => x.Reference.Id, StringComparer.Ordinal);
        Templates = Array.AsReadOnly(library.OrderedIds.Select(id => byId[id]).ToArray());
        DefaultTemplateName = byId[library.DefaultId].Content.Name;
        FavoriteTemplateNames = string.Join(" · ", library.FavoriteIds.Select(id => byId[id].Content.Name));
        OnPropertyChanged(nameof(DefaultTemplateName)); OnPropertyChanged(nameof(FavoriteTemplateNames));
        OnPropertyChanged(nameof(Templates));
    }
}

public sealed class AiPromptFieldInput(AiTemplateField definition, string initialValue, bool isDateFormat = false, string? dateFieldKey = null) : ObservableObject
{
    private string value = initialValue;
    public string Key => definition.Key;
    public string Label => dateFieldKey is not null && !isDateFormat ? dateFieldKey + " · " + Strings.AiPrompt_DateLabel : definition.Semantic switch
    {
        AiTemplateSemantic.Name => Strings.AiPrompt_NameField,
        AiTemplateSemantic.BirthDate => Strings.AiPrompt_BirthField,
        AiTemplateSemantic.DeathDate => Strings.AiPrompt_DeathField,
        _ => definition.Key
    };
    public string InitialValue { get; } = initialValue;
    public bool IsDateFormat => isDateFormat;
    public string? DateFieldKey => dateFieldKey;
    public string Value { get => value; set => SetProperty(ref this.value, value); }
}

public sealed class AiPromptPhotoSlot(AiReferenceSlot slot, AiDesignAttachment? asset, IReadOnlyList<AiDesignAttachment> available) : ObservableObject
{
    private DecodedPreview? preview;
    private string? previewError;
    public string SlotId => slot.SlotId;
    public string Role => slot.Role;
    public AiDesignAttachment? Asset => asset;
    public string FileName => asset?.OriginalName ?? Strings.AiPrompt_ChoosePhoto;
    public IReadOnlyList<AiDesignAttachment> AvailableAssets => available;
    public AiDesignAttachment? SelectedAsset { get; set; }
    public DecodedPreview? Preview { get => preview; set => SetProperty(ref preview, value); }
    public string? PreviewError { get => previewError; set => SetProperty(ref previewError, value); }
}
