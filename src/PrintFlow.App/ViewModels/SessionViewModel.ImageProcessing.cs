using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;

namespace PrintFlow.App.ViewModels;

public sealed partial class SessionViewModel
{
    [ObservableProperty] private ImageProcessingMethod? _draftImageProcessingMethod;
    [ObservableProperty] private bool _isChangingImageProcessingMethod;

    public bool ShowsImageProcessingMethod => _session?.ImageProcessingExecution is not null;
    public string CurrentImageProcessingMethod => _session?.ImageProcessingExecution is { } execution
        ? string.Format(CultureInfo.CurrentCulture, Strings.Resolve("ImageProcessing_CurrentMethod"),
            ImageProcessingMethodOption.Name(execution.Method))
        : string.Empty;
    public string? CurrentImageProcessingUnavailable => _session?.ImageProcessingExecution?.Unavailable is { } failure
        ? ImageProcessingMethodOption.UnavailableHint(failure)
        : null;
    public string ChangeImageProcessingMethodLabel => Strings.Resolve("ImageProcessing_ChangeTaskMethod");
    public string ApplyImageProcessingMethodLabel => Strings.Resolve("ImageProcessing_ApplyTaskMethod");
    public bool CanChangeImageProcessingMethod => !IsBusy && Allows(CommandKind.SetImageProcessingSelection);
    public bool ShowsMeituRecognitionModes =>
        _session?.ImageProcessingExecution?.Method is null or ImageProcessingMethod.MeituBackgroundRemoval;
    public IReadOnlyList<ImageProcessingMethodOption> TaskImageProcessingMethods =>
        _session?.CurrentStep is { } step ? ImageProcessingMethodOption.ForStep(step.Step, _sessions) : [];

    private bool UsesCurrentImageWithoutProcessing =>
        _session?.ImageProcessingExecution?.Method is ImageProcessingMethod.SkipEnhancement or ImageProcessingMethod.KeepBackground;
    private bool CanRunSelectedImageMethod =>
        !UsesCurrentImageWithoutProcessing && _session?.ImageProcessingExecution?.Unavailable is null;

    private bool CanApplyImageProcessingMethod() => CanChangeImageProcessingMethod &&
        DraftImageProcessingMethod is { } method && method != _session?.ImageProcessingExecution?.Method &&
        TaskImageProcessingMethods.Any(option => option.Method == method && option.IsAvailable);

    [RelayCommand(CanExecute = nameof(CanApplyImageProcessingMethod))]
    private Task ApplyImageProcessingMethodAsync(CancellationToken cancellationToken)
    {
        if (!CanApplyImageProcessingMethod() || _session?.CurrentStep is not { } step ||
            _session.ImageProcessingSelection is not { } selection || DraftImageProcessingMethod is not { } method)
            return Task.CompletedTask;

        selection = step.Step switch
        {
            StepKind.Enhancement => selection with { Enhancement = method, UseOpenAiCombined = false },
            StepKind.BackgroundRemoval => selection with { BackgroundRemoval = method },
            StepKind.Trim => selection with { Crop = method },
            _ => selection,
        };
        return RunAsync(new WorkflowCommand.SetImageProcessingSelection(step.Step, selection),
            cancellationToken, _ => IsChangingImageProcessingMethod = false);
    }

    partial void OnDraftImageProcessingMethodChanged(ImageProcessingMethod? value) =>
        ApplyImageProcessingMethodCommand.NotifyCanExecuteChanged();

    partial void OnIsChangingImageProcessingMethodChanged(bool value)
    {
        if (!value)
            DraftImageProcessingMethod = _session?.ImageProcessingExecution?.Method;
    }

    private void ShowImageProcessingMethod()
    {
        IsChangingImageProcessingMethod = false;
        OnPropertyChanged(nameof(TaskImageProcessingMethods));
        DraftImageProcessingMethod = _session?.ImageProcessingExecution?.Method;
        OnPropertyChanged(nameof(CurrentImageProcessingMethod));
        OnPropertyChanged(nameof(CurrentImageProcessingUnavailable));
        OnPropertyChanged(nameof(ShowsImageProcessingMethod));
        OnPropertyChanged(nameof(ShowsMeituRecognitionModes));
    }
}
