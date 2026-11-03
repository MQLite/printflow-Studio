using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Resources;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Workflow.AiDesign.Authoring;

namespace PrintFlow.App.ViewModels.AiDesign;

public sealed class AiTemplateEditorViewModel : ObservableObject
{
    private readonly AiTemplateCatalog catalog;
    private readonly AiTemplateRef? source;
    private readonly AiProductKind product;
    private readonly Func<AiTemplateDefinition, Task> saved;
    private string name, body;
    private bool confirmed, saveAsNew, busy, instancePreview;
    private long generation;
    private string? error, status;
    public string Name { get => name; set { if (SetProperty(ref name, value)) Changed(); } }
    public string Body { get => body; set { if (SetProperty(ref body, value)) Changed(); } }
    public bool SaveAsNew { get => saveAsNew; set { if (SetProperty(ref saveAsNew, value)) Changed(); } }
    public bool PreviewConfirmed { get => confirmed; set => SetProperty(ref confirmed, value); }
    public bool IsBusy { get => busy; private set => SetProperty(ref busy, value); }
    public string? Error { get => error; private set => SetProperty(ref error, value); }
    public string? Status { get => status; private set => SetProperty(ref status, value); }
    public ObservableCollection<AiTemplateFieldInput> Fields { get; } = [];
    public IReadOnlyList<AiTemplateOption<AiTemplateFieldKind>> FieldKinds { get; } =
    [new(AiTemplateFieldKind.Text, Strings.AiPrompt_TextKind), new(AiTemplateFieldKind.OptionalDateLine, Strings.AiPrompt_DateKind), new(AiTemplateFieldKind.ImageReference, Strings.AiPrompt_ImageKind)];
    public IReadOnlyList<AiTemplateOption<AiTemplateSemantic>> Semantics { get; } =
    [new(AiTemplateSemantic.FreeText, Strings.AiPrompt_FreeText), new(AiTemplateSemantic.Name, Strings.AiPrompt_NameField),
        new(AiTemplateSemantic.BirthDate, Strings.AiPrompt_BirthField), new(AiTemplateSemantic.DeathDate, Strings.AiPrompt_DeathField),
        new(AiTemplateSemantic.PersonImage, Strings.AiPrompt_PersonImage), new(AiTemplateSemantic.LayoutImage, Strings.AiPrompt_LayoutImage), new(AiTemplateSemantic.EditSourceImage, Strings.AiPrompt_EditSourceImage)];
    public IAsyncRelayCommand SaveCommand { get; }
    public IRelayCommand AddFieldCommand { get; }
    public IRelayCommand<AiTemplateFieldInput> RemoveFieldCommand { get; }
    public AiTemplateDefinition? SavedTemplate { get; private set; }
    public bool IsInstancePreview { get => instancePreview; set { if (SetProperty(ref instancePreview, value)) OnPropertyChanged(nameof(Notice)); } }
    public string Notice => IsInstancePreview ? Strings.AiPrompt_InstancePreviewNotice : Strings.AiPrompt_TemplateNotice;

    public AiTemplateEditorViewModel(AiTemplateCatalog catalog, AiTemplateRef? source, AiTemplateContent content, Func<AiTemplateDefinition, Task> saved)
    {
        this.catalog = catalog; this.source = source; this.product = content.Product; this.saved = saved;
        name = content.Name; body = content.Body;
        foreach (var definition in content.Fields) Add(definition);
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        AddFieldCommand = new RelayCommand(() =>
        {
            Add(new("field-" + Guid.NewGuid().ToString("N")[..6], AiTemplateFieldKind.Text, false)); Changed();
        });
        RemoveFieldCommand = new RelayCommand<AiTemplateFieldInput>(input =>
        {
            if (input is null) return;
            input.PropertyChanged -= FieldChanged; Fields.Remove(input); Changed();
        });
    }
    private void Add(AiTemplateField definition)
    {
        var input = new AiTemplateFieldInput(definition);
        input.PropertyChanged += FieldChanged; Fields.Add(input);
    }
    private void FieldChanged(object? sender, PropertyChangedEventArgs e) => Changed();
    private void Changed() { generation++; PreviewConfirmed = false; Status = null; }
    private async Task SaveAsync()
    {
        Error = null;
        if (!PreviewConfirmed) { Error = Strings.AiPrompt_TemplateConfirmRequired; return; }
        long captured = generation;
        var content = new AiTemplateContent(Name, product, Body, Fields.Select(x => x.ToDefinition()).ToArray());
        IsBusy = true;
        try
        {
            var result = await catalog.SaveAsync(SavedTemplate?.Reference ?? source, content, SaveAsNew, default);
            if (result.IsFailure) { Error = Strings.AiPrompt_TemplateSaveFailed; return; }
            SavedTemplate = result.Value; OnPropertyChanged(nameof(SavedTemplate));
            if (captured == generation) Status = Strings.AiPrompt_Saved;
            await saved(result.Value);
        }
        finally { IsBusy = false; }
    }
}

public sealed record AiTemplateOption<T>(T Value, string Label);

public sealed class AiTemplateFieldInput : ObservableObject
{
    private string key;
    private string? defaultValue, slotId, dateLabel;
    private AiTemplateFieldKind kind;
    private AiTemplateSemantic semantic;
    private bool required;
    public AiTemplateFieldInput(AiTemplateField definition)
    {
        key = definition.Key; kind = definition.Kind; semantic = definition.Semantic; required = definition.Required;
        defaultValue = definition.DefaultValue; slotId = definition.SlotId; dateLabel = definition.DateLabel;
    }
    public string Key { get => key; set => SetProperty(ref key, value); }
    public AiTemplateFieldKind Kind { get => kind; set => SetProperty(ref kind, value); }
    public AiTemplateSemantic Semantic { get => semantic; set => SetProperty(ref semantic, value); }
    public bool Required { get => required; set => SetProperty(ref required, value); }
    public string? DefaultValue { get => defaultValue; set => SetProperty(ref defaultValue, value); }
    public string? SlotId { get => slotId; set => SetProperty(ref slotId, value); }
    public string? DateLabel { get => dateLabel; set => SetProperty(ref dateLabel, value); }
    public AiTemplateField ToDefinition() => new(Key, Kind, Required,
        string.IsNullOrEmpty(DefaultValue) ? null : DefaultValue, SlotId, DateLabel, Semantic);
}
