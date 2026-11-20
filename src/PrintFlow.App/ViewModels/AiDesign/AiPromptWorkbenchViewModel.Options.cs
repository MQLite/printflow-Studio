using CommunityToolkit.Mvvm.ComponentModel;
using PrintFlow.App.Resources;
using PrintFlow.Domain.AiDesign.Authoring;

namespace PrintFlow.App.ViewModels.AiDesign;

public sealed partial class AiPromptWorkbenchViewModel
{
    private (Guid DraftId, AiTemplateRef Template)? optionOwner;
    public IReadOnlyList<AiPromptOption> FieldOptions { get; private set; } = [];
    public IReadOnlyList<AiPromptFieldInput> VisibleFields { get; private set; } = [];
    public IReadOnlyList<AiPromptTemplateChoice> TemplateChoices { get; private set; } = [];

    private void RefreshTemplateChoices()
    {
        TemplateChoices = Templates.Select(x => new AiPromptTemplateChoice(x, x.Reference == Draft?.Template)).ToArray();
        OnPropertyChanged(nameof(TemplateChoices));
    }

    private void RefreshFieldOptions()
    {
        if (Draft is not { } value) return;
        var owner = (value.Id, value.Template);
        var previous = FieldOptions;
        foreach (var option in previous) option.PropertyChanged -= FieldOptionChanged;
        var groups = Fields.Where(x => !x.IsReadOnly && x.DateFieldKey is null)
            .GroupBy(x => (Lifespan: x.Semantic is AiTemplateSemantic.BirthDate or AiTemplateSemantic.DeathDate,
                Key: x.Semantic is AiTemplateSemantic.BirthDate or AiTemplateSemantic.DeathDate ? "" : x.Key));
        FieldOptions = groups.Select(group =>
        {
            var inputs = group.ToArray();
            var option = optionOwner == owner ? previous.FirstOrDefault(x => x.Inputs[0].Key == inputs[0].Key) : null;
            return new AiPromptOption(inputs, group.Key.Lifespan ? Strings.Resolve("AiPrompt_Lifespan") : inputs[0].WorkbenchLabel,
                option?.IsSelected ?? (group.Key.Lifespan || inputs.Any(x => x.Required || !string.IsNullOrWhiteSpace(x.Value))));
        }).ToArray();
        optionOwner = owner;
        foreach (var option in FieldOptions) option.PropertyChanged += FieldOptionChanged;
        OnPropertyChanged(nameof(FieldOptions));
        RefreshVisibleFields();
        RefreshTemplateChoices();
    }

    private void FieldOptionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AiPromptOption.IsSelected)) RefreshVisibleFields();
    }

    private void RefreshVisibleFields()
    {
        var selected = FieldOptions.Where(x => x.IsSelected).SelectMany(x => x.Inputs).ToHashSet();
        VisibleFields = Fields.Where(selected.Contains).ToArray();
        OnPropertyChanged(nameof(VisibleFields));
    }
}

public sealed class AiPromptOption(IReadOnlyList<AiPromptFieldInput> inputs, string label, bool selected) : ObservableObject
{
    private bool isSelected = selected;
    public IReadOnlyList<AiPromptFieldInput> Inputs { get; } = inputs;
    public string AutomationId => "AiPrompt.FieldOption." + Inputs[0].Key;
    public string Label { get; } = label;
    public bool IsSelected { get => isSelected; set => SetProperty(ref isSelected, value); }
}

public sealed record AiPromptTemplateChoice(AiTemplateDefinition Template, bool IsSelected);
