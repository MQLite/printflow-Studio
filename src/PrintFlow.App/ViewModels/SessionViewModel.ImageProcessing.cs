using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Resources;
using PrintFlow.App.Navigation;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;

namespace PrintFlow.App.ViewModels;

public sealed partial class SessionViewModel
{
    private readonly IOpenAiUploadPrompt? _openAiUploadPrompt;

    [ObservableProperty] private ImageProcessingMethod? _draftImageProcessingMethod;
    [ObservableProperty] private bool _isChangingImageProcessingMethod;

    public bool ShowsImageProcessingMethod => _session?.ImageProcessingExecution is not null;
    public string CurrentImageProcessingMethod => _session?.ImageProcessingExecution is { } execution
        ? string.Format(CultureInfo.CurrentCulture, Strings.Resolve("ImageProcessing_CurrentMethod"),
            ImageProcessingMethodOption.Name(execution.Method))
        : string.Empty;
    public string? CurrentImageProcessingUnavailable => _session?.ImageProcessingExecution?.Unavailable is { } failure
        ? ImageProcessingMethodOption.UnavailableHint(failure)
        : ExceedsLocalUpscaleLimit ? Strings.Resolve("ImageProcessing_UpscaleTooLarge") : null;

    private (int Width, int Height)? LocalUpscaleInputSize =>
        _session?.ImageProcessingExecution?.Method == ImageProcessingMethod.AiUpscale &&
        _session.CurrentArtefact is { IsCurrentStepResult: false, Facts: { PixelWidth: int width, PixelHeight: int height } }
            ? (width, height) : null;

    private bool ExceedsLocalUpscaleLimit => LocalUpscaleInputSize is { } size &&
        !LocalUpscaleLimits.Fits(size.Width, size.Height);

    private string? LocalUpscaleSizeAdvice => LocalUpscaleInputSize is { } size &&
        (long)size.Width * size.Height > LocalUpscaleLimits.RecommendedPixels
            ? string.Format(CultureInfo.CurrentCulture, Strings.Resolve("ImageProcessing_UpscaleSizeAdvice"),
                size.Width, size.Height) : null;
    public string ChangeImageProcessingMethodLabel => Strings.Resolve("ImageProcessing_ChangeTaskMethod");
    public string ApplyImageProcessingMethodLabel => Strings.Resolve("ImageProcessing_ApplyTaskMethod");
    public bool CanChangeImageProcessingMethod => !IsBusy && Allows(CommandKind.SetImageProcessingSelection);
    public bool ShowsMeituRecognitionModes =>
        _session?.ImageProcessingExecution?.Method is null or ImageProcessingMethod.MeituBackgroundRemoval;
    public IReadOnlyList<ImageProcessingMethodOption> TaskImageProcessingMethods =>
        _session?.CurrentStep is { } step ? ImageProcessingMethodOption.ForStep(step.Step, _sessions, includeCombined: true) : [];
    public bool ShowsCombinedImageProcessingChoice => CanChangeImageProcessingMethod &&
        _session?.CurrentStep?.Step == StepKind.Enhancement;
    public string CombinedImageProcessingChoiceLabel => Strings.Resolve(IsCombinedImageProcessing
        ? "ImageProcessing_ChooseSeparate" : "ImageProcessing_ChooseCombined");
    public string? CombinedImageProcessingUnavailable => ShowsCombinedImageProcessingChoice && !IsCombinedImageProcessing &&
        _sessions.GetImageProcessingUnavailable(ImageProcessingMethod.OpenAiCombined) is { } failure
        ? ImageProcessingMethodOption.UnavailableHint(failure)
        : null;

    private bool IsCombinedImageProcessing => _session?.ImageProcessingExecution?.Method == ImageProcessingMethod.OpenAiCombined;

    private bool ContinuesToCombinedProcessing => IsCombinedImageProcessing &&
        _session?.CurrentStep?.Step == StepKind.Enhancement;

    private bool IsCombinedBackgroundRemoval => IsCombinedImageProcessing &&
        _session?.CurrentStep?.Step == StepKind.BackgroundRemoval;

    public bool CanReturnToSeparateProcessing => IsCombinedBackgroundRemoval && CanReturnToStep && !IsBusy &&
        ReturnTargets.Any(target => target.Step == StepKind.Enhancement);

    public string ReturnToSeparateProcessingLabel => Strings.Resolve("ImageProcessing_ReturnToSeparate");

    [RelayCommand]
    private void ReturnToSeparateProcessing()
    {
        if (CanReturnToSeparateProcessing)
            CaptureReturn(ReturnTargets.Single(target => target.Step == StepKind.Enhancement), useStepByStepImageProcessing: true);
    }

    private bool UsesCurrentImageWithoutProcessing =>
        ContinuesToCombinedProcessing ||
        _session?.ImageProcessingExecution?.Method is ImageProcessingMethod.SkipEnhancement or ImageProcessingMethod.KeepBackground;
    private bool CanRunSelectedImageMethod =>
        !UsesCurrentImageWithoutProcessing && _session?.ImageProcessingExecution?.Unavailable is null && !ExceedsLocalUpscaleLimit;
    private bool CanConfirmSelectedKeepCanvas =>
        CanKeepOriginalExtent && _session?.ImageProcessingExecution?.Method == ImageProcessingMethod.KeepCanvas;

    private bool CanApplyImageProcessingMethod() =>
        DraftImageProcessingMethod is { } method && CanSelectImageProcessingMethod(method);

    private bool CanSelectImageProcessingMethod(ImageProcessingMethod method) => CanChangeImageProcessingMethod &&
        method != _session?.ImageProcessingExecution?.Method &&
        TaskImageProcessingMethods.Any(option => option.Method == method && option.IsAvailable);

    private bool CanSwitchCombinedImageProcessing() =>
        ShowsCombinedImageProcessingChoice && CombinedImageProcessingUnavailable is null;

    [RelayCommand(CanExecute = nameof(CanSwitchCombinedImageProcessing))]
    private Task SwitchCombinedImageProcessingAsync(CancellationToken cancellationToken) =>
        CanSwitchCombinedImageProcessing() && _session?.ImageProcessingSelection is { } selection
            ? SetTaskImageProcessingSelectionAsync(StepKind.Enhancement,
                selection with { UseOpenAiCombined = !selection.UseOpenAiCombined }, cancellationToken)
            : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanApplyImageProcessingMethod))]
    private Task ApplyImageProcessingMethodAsync(CancellationToken cancellationToken) =>
        DraftImageProcessingMethod is { } method
            ? ChooseImageProcessingMethodAsync(method, cancellationToken)
            : Task.CompletedTask;

    private Task ChooseImageProcessingMethodAsync(ImageProcessingMethod method, CancellationToken cancellationToken)
    {
        if (!CanSelectImageProcessingMethod(method) || _session?.CurrentStep is not { } step ||
            _session.ImageProcessingSelection is not { } selection)
            return Task.CompletedTask;

        selection = step.Step switch
        {
            StepKind.Enhancement when method == ImageProcessingMethod.OpenAiCombined => selection with { UseOpenAiCombined = true },
            StepKind.Enhancement => selection with { Enhancement = method, UseOpenAiCombined = false },
            StepKind.BackgroundRemoval => selection with { BackgroundRemoval = method },
            StepKind.Trim => selection with { Crop = method },
            _ => selection,
        };
        return SetTaskImageProcessingSelectionAsync(step.Step, selection, cancellationToken);
    }

    private Task SetTaskImageProcessingSelectionAsync(StepKind step, SessionImageProcessingSelection selection,
        CancellationToken cancellationToken) =>
        RunAsync(new WorkflowCommand.SetImageProcessingSelection(step, selection), cancellationToken,
            _ => IsChangingImageProcessingMethod = false);

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
