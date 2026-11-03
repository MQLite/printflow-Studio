using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Resources;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Workflow.AiDesign.Authoring;

namespace PrintFlow.App.ViewModels.AiDesign;

public sealed partial class AiPromptWorkbenchViewModel
{
    private AiTemplateDefinition? editingTemplate;
    public IAsyncRelayCommand<AiTemplateDefinition> SetDefaultTemplateCommand { get; private set; } = null!;
    public IAsyncRelayCommand<AiTemplateDefinition> ToggleFavoriteCommand { get; private set; } = null!;
    public IAsyncRelayCommand<AiTemplateDefinition> MoveTemplateUpCommand { get; private set; } = null!;
    public IAsyncRelayCommand<AiTemplateDefinition> MoveTemplateDownCommand { get; private set; } = null!;
    public IAsyncRelayCommand<AiTemplateDefinition> DeleteTemplateCommand { get; private set; } = null!;
    public IAsyncRelayCommand<AiTemplateDefinition> RestoreBuiltinCommand { get; private set; } = null!;
    public string DefaultTemplateName { get; private set; } = "";
    public string FavoriteTemplateNames { get; private set; } = "";

    private void InitializeTemplateCommands()
    {
        SetDefaultTemplateCommand = new AsyncRelayCommand<AiTemplateDefinition>(x => UpdatePreferenceAsync(x, "default"));
        ToggleFavoriteCommand = new AsyncRelayCommand<AiTemplateDefinition>(x => UpdatePreferenceAsync(x, "favorite"));
        MoveTemplateUpCommand = new AsyncRelayCommand<AiTemplateDefinition>(x => UpdatePreferenceAsync(x, "up"));
        MoveTemplateDownCommand = new AsyncRelayCommand<AiTemplateDefinition>(x => UpdatePreferenceAsync(x, "down"));
        DeleteTemplateCommand = new AsyncRelayCommand<AiTemplateDefinition>(x => UpdatePreferenceAsync(x, "delete"));
        RestoreBuiltinCommand = new AsyncRelayCommand<AiTemplateDefinition>(x => UpdatePreferenceAsync(x, "restore"));
    }
    private async Task UpdatePreferenceAsync(AiTemplateDefinition? template, string action) => await RunAsync(async () =>
    {
        if (template is null || (await FlushCoreAsync(default)).IsFailure) return;
        var read = await catalog.ReadLibraryAsync(default);
        if (read.IsFailure) { Error = Strings.AiPrompt_TemplateSaveFailed; return; }
        var library = read.Value;
        var favorites = library.FavoriteIds.ToList(); var order = library.OrderedIds.ToList(); string defaultId = library.DefaultId;
        var id = template.Reference.Id;
        if (action == "default") defaultId = id;
        if (action == "favorite") { if (!favorites.Remove(id)) favorites.Add(id); }
        if (action is "up" or "down")
        {
            int index = order.IndexOf(id), target = index + (action == "up" ? -1 : 1);
            if (index >= 0 && target >= 0 && target < order.Count) (order[index], order[target]) = (order[target], order[index]);
        }
        var savedLibrary = action switch
        {
            "delete" => await catalog.DeletePersonalAsync(template.Reference, default),
            "restore" => await catalog.RestoreBuiltinAsync(template.BuiltinOriginId ?? id, default),
            _ => await catalog.UpdatePreferencesAsync(library.Revision, favorites, order, defaultId, default)
        };
        if (savedLibrary.IsFailure) { Error = Strings.AiPrompt_TemplateSaveFailed; return; }
        SetTemplates(savedLibrary.Value);
    }, default);

    private async Task EditTemplateAsync() => await RunAsync(async () =>
    {
        if ((await FlushCoreAsync(default)).IsFailure) return;
        var library = await catalog.ReadLibraryAsync(default);
        if (library.IsFailure) { Error = Strings.AiPrompt_TemplateSaveFailed; return; }
        var current = library.Value.Templates.SingleOrDefault(x => x.Reference.Id == (editingTemplate?.Reference.Id ?? Draft!.Template.Id));
        TemplateEditor = CreateTemplateEditor(current?.Reference, current?.Content ?? editingTemplate?.Content ?? Draft!.TemplateSnapshot);
    }, default);

    private async Task SaveInstanceTemplateAsync() => await RunAsync(async () =>
    {
        if ((await FlushCoreAsync(default)).IsFailure) return;
        TemplateEditor = CreateTemplateEditor(null, AiTemplateInstancePreview.Create(Draft!));
        TemplateEditor.SaveAsNew = true;
        TemplateEditor.IsInstancePreview = true;
    }, default);

    private AiTemplateEditorViewModel CreateTemplateEditor(AiTemplateRef? source, AiTemplateContent content)
    {
        long capturedEpoch = epoch; var draftId = Draft!.Id; var template = Draft.Template;
        return new(catalog, source, content, async saved =>
        {
            var library = await catalog.ReadLibraryAsync(default);
            if (!IsCurrent(capturedEpoch) || Draft.Id != draftId || Draft.Template != template) return;
            editingTemplate = saved;
            if (library.IsSuccess) SetTemplates(library.Value);
        });
    }
}
