using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Resources;
using PrintFlow.App.Navigation;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels;

public sealed partial class SessionViewModel
{
    private readonly IOpenAiUploadPrompt? _openAiUploadPrompt;

    [ObservableProperty] private ImageProcessingMethod? _draftImageProcessingMethod;
    private long? _imageProcessingAdvanceGeneration;
    private IReadOnlyList<ImageProcessingMethodOption> _taskImageProcessingMethods = [];

    public bool ShowsImageProcessingMethod => _session?.ImageProcessingExecution is not null;
    public string CurrentImageProcessingMethod =>
        (ShowsImageProcessingMethodSelection ? SelectedImageProcessingMethod : _session?.ImageProcessingExecution?.Method) is { } method
        ? string.Format(CultureInfo.CurrentCulture, Strings.Resolve("ImageProcessing_CurrentMethod"),
            ImageProcessingMethodOption.Name(method))
        : string.Empty;
    public string? CurrentImageProcessingUnavailable => SelectedImageProcessingUnavailable;
    private string? SelectedImageProcessingUnavailable =>
        ShowsImageProcessingMethodSelection && SelectedImageProcessingOption is { IsAvailable: false } option ? option.Description :
        _session?.ImageProcessingExecution is { } execution &&
        SelectedImageProcessingMethod == execution.Method && execution.Unavailable is { } failure
        ? ImageProcessingMethodOption.UnavailableHint(failure)
        : ExceedsLocalUpscaleLimit ? Strings.Resolve("ImageProcessing_UpscaleTooLarge") : null;

    private (int Width, int Height)? LocalUpscaleInputSize =>
        _session?.CurrentArtefact is { IsCurrentStepResult: false, Facts: { PixelWidth: int width, PixelHeight: int height } }
            ? (width, height) : null;

    private bool ExceedsLocalUpscaleLimit => SelectedImageProcessingMethod == ImageProcessingMethod.AiUpscale &&
        InputExceedsLocalUpscaleLimit;

    private bool InputExceedsLocalUpscaleLimit => LocalUpscaleInputSize is { } size &&
        !LocalUpscaleLimits.Fits(size.Width, size.Height);

    private string? LocalUpscaleSizeAdvice => SelectedImageProcessingMethod == ImageProcessingMethod.AiUpscale &&
        LocalUpscaleInputSize is { } size &&
        (long)size.Width * size.Height > LocalUpscaleLimits.RecommendedPixels
            ? string.Format(CultureInfo.CurrentCulture, Strings.Resolve("ImageProcessing_UpscaleSizeAdvice"),
                size.Width, size.Height) : null;
    public string ImageProcessingMethodHeading => Strings.Resolve("ImageProcessing_TaskMethods");
    public bool CanChangeImageProcessingMethod => !IsBusy && Allows(CommandKind.SetImageProcessingSelection);
    public bool CanEditImageProcessingMethods => CanChangeImageProcessingMethod && !IsAdvancingImageProcessing;
    public bool ShowsImageProcessingMethodSelection =>
        GuidedPage is GuidedSessionPage.Ready or GuidedSessionPage.Failure &&
        (Offers(CommandKind.SetImageProcessingSelection) || IsCombinedBackgroundRemoval && GuidedPage == GuidedSessionPage.Ready);
    public bool ShowsMeituRecognitionModes =>
        SelectedImageProcessingMethod is null or ImageProcessingMethod.MeituBackgroundRemoval;
    public IReadOnlyList<ImageProcessingMethodOption> TaskImageProcessingMethods => _taskImageProcessingMethods;

    private ImageProcessingMethod? SelectedImageProcessingMethod =>
        DraftImageProcessingMethod ?? _session?.ImageProcessingExecution?.Method;
    private ImageProcessingMethodOption? SelectedImageProcessingOption =>
        TaskImageProcessingMethods.FirstOrDefault(option => option.Method == SelectedImageProcessingMethod);

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
        !UsesCurrentImageWithoutProcessing && _session?.ImageProcessingExecution?.Unavailable is null &&
        !(_session?.ImageProcessingExecution?.Method == ImageProcessingMethod.AiUpscale && InputExceedsLocalUpscaleLimit);
    private bool CanConfirmSelectedKeepCanvas =>
        CanKeepOriginalExtent && _session?.ImageProcessingExecution?.Method == ImageProcessingMethod.KeepCanvas;

    private bool IsAdvancingImageProcessing => _imageProcessingAdvanceGeneration == _openGeneration;
    private bool HasImageProcessingMethodChange => SelectedImageProcessingMethod != _session?.ImageProcessingExecution?.Method;
    private bool UsesImageProcessingAdvance => ShowsImageProcessingMethodSelection &&
        (GuidedPage == GuidedSessionPage.Ready || HasImageProcessingMethodChange);
    private bool ShowsImageProcessingAdvance => UsesImageProcessingAdvance && !ShowsGuidedAutomaticConfirmation;
    public ICommand GuidedRunCommand => ShowsImageProcessingAdvance ? AdvanceImageProcessingCommand : RunStepCommand;
    public string GuidedRunLabel => !ShowsImageProcessingAdvance ? RunStepLabel : SelectedImageProcessingMethod switch
    {
        ImageProcessingMethod.OpenAiCombined when IsCombinedBackgroundRemoval => RunStepLabel,
        ImageProcessingMethod.OpenAiCombined => Strings.Resolve("ImageProcessing_ContinueCombined"),
        ImageProcessingMethod.ManualCrop => BeginManualCropLabel,
        ImageProcessingMethod.KeepCanvas => KeepOriginalExtentLabel,
        ImageProcessingMethod.SkipEnhancement or ImageProcessingMethod.KeepBackground =>
            ImageProcessingMethodOption.Name(SelectedImageProcessingMethod.Value),
        { } method => string.Format(CultureInfo.CurrentCulture, Strings.Resolve("ImageProcessing_Continue"),
            ImageProcessingMethodOption.Name(method)),
        _ => RunStepLabel,
    };
    public bool ShowsSeparateSkip => CanSkip && !UsesImageProcessingAdvance;
    public bool ShowsSeparateKeepOriginalExtent => CanKeepOriginalExtent && !UsesImageProcessingAdvance;
    public bool ShowsSeparateRetry => CanRetry && !UsesImageProcessingAdvance;

    private bool CanAdvanceImageProcessing() =>
        !IsBusy && !IsAdvancingImageProcessing && CanInteractWithLivePage && ShowsImageProcessingAdvance &&
        SelectedImageProcessingOption is { IsAvailable: true } option &&
        (option.Method == _session?.ImageProcessingExecution?.Method
            ? CanAdvanceWithCurrentImageProcessingMethod(option.Method)
            : CanChangeImageProcessingMethod);

    private bool CanAdvanceWithCurrentImageProcessingMethod(ImageProcessingMethod method) => method switch
    {
        ImageProcessingMethod.OpenAiCombined when ContinuesToCombinedProcessing => CanSkip,
        ImageProcessingMethod.SkipEnhancement or ImageProcessingMethod.KeepBackground => CanSkip,
        ImageProcessingMethod.ManualCrop => CanManualCrop,
        ImageProcessingMethod.KeepCanvas => CanKeepOriginalExtent,
        _ => CanRunStep,
    };

    [RelayCommand(CanExecute = nameof(CanAdvanceImageProcessing))]
    private async Task AdvanceImageProcessingAsync()
    {
        if (!CanAdvanceImageProcessing() || SelectedImageProcessingMethod is not { } method)
            return;
        if (method == ImageProcessingMethod.AlphaTrim && !ValidateAutomaticTrimParameters())
            return;

        CancellationToken cancellationToken = CancellationToken.None;
        long generation = _openGeneration;
        _imageProcessingAdvanceGeneration = generation;
        try
        {
            NotifyImageProcessingAdvance();
            bool changingMethod = HasImageProcessingMethodChange;
            if (!await ChooseImageProcessingMethodAsync(method, cancellationToken).ConfigureAwait(true))
                return;
            if (changingMethod && !CanAdvanceWithCurrentImageProcessingMethod(method) && CanRetry &&
                !await RetryChangedImageProcessingMethodAsync(method, cancellationToken).ConfigureAwait(true))
                return;
            if (_session is not { CurrentStep: { } step } view || !CanAdvanceWithCurrentImageProcessingMethod(method) ||
                DisplayedImageProcessingInput(view) is not { } expectedInput)
                return;

            switch (method)
            {
                case ImageProcessingMethod.OpenAiCombined when step.Step == StepKind.Enhancement:
                case ImageProcessingMethod.SkipEnhancement:
                case ImageProcessingMethod.KeepBackground:
                    await RunAsync(new WorkflowCommand.Skip(step.Step, CombinedSkipReason) { ImageInput = expectedInput },
                        cancellationToken).ConfigureAwait(true);
                    break;
                case ImageProcessingMethod.ManualCrop:
                    BeginManualCrop();
                    break;
                case ImageProcessingMethod.KeepCanvas:
                    await RunAsync(new WorkflowCommand.KeepOriginalExtent { ImageInput = expectedInput },
                        cancellationToken).ConfigureAwait(true);
                    break;
                default:
                    await RunStepAsync(cancellationToken).ConfigureAwait(true);
                    break;
            }
        }
        finally
        {
            if (_imageProcessingAdvanceGeneration == generation)
                _imageProcessingAdvanceGeneration = null;
            NotifyImageProcessingAdvance();
        }
    }

    private async Task<bool> RetryChangedImageProcessingMethodAsync(ImageProcessingMethod method, CancellationToken cancellationToken)
    {
        if (_session is not { CurrentStep: { } step } view ||
            DisplayedImageProcessingInput(view) is not { } input || input.Method != method)
            return false;

        SessionView? retried = null;
        await RunAsync(new WorkflowCommand.Retry(step.Step) { ExpectedImageTarget = new(input, step.AttemptCount) }, cancellationToken,
            succeeded => retried = succeeded).ConfigureAwait(true);
        return !cancellationToken.IsCancellationRequested && ReferenceEquals(retried, _session) && retried is
        {
            ImageProcessingExecution.Method: var retriedMethod,
            CurrentStep: { State: StepState.Waiting } retriedStep,
            CurrentArtefact: { } retriedInput,
        } && retried.Id == view.Id && retriedStep.Step == step.Step && retriedMethod == method &&
            retriedStep.AttemptCount == step.AttemptCount &&
            retriedInput.RevisionId == input.RevisionId && retriedInput.Sha256 == input.Sha256;
    }

    private async Task<bool> ChooseImageProcessingMethodAsync(ImageProcessingMethod method, CancellationToken cancellationToken)
    {
        if (_session is not { CurrentStep: { } step, CurrentArtefact: { } input, ImageProcessingSelection: { } selection } view ||
            cancellationToken.IsCancellationRequested || SelectedImageProcessingOption is not { IsAvailable: true } option ||
            option.Method != method)
            return false;
        if (_sessions.GetImageProcessingUnavailable(method) is { } unavailable)
        {
            RefreshTaskImageProcessingMethods();
            DraftImageProcessingMethod = method;
            OnPropertyChanged(nameof(CurrentImageProcessingUnavailable));
            Notice = ImageProcessingMethodOption.UnavailableHint(unavailable);
            return false;
        }
        if (method == view.ImageProcessingExecution?.Method) return true;
        if (!CanChangeImageProcessingMethod || DisplayedImageProcessingInput(view) is not { } expectedInput) return false;

        selection = step.Step switch
        {
            StepKind.Enhancement when method == ImageProcessingMethod.OpenAiCombined => selection with { UseOpenAiCombined = true },
            StepKind.Enhancement => selection with { Enhancement = method, UseOpenAiCombined = false },
            StepKind.BackgroundRemoval => selection with { BackgroundRemoval = method },
            StepKind.Trim => selection with { Crop = method },
            _ => selection,
        };
        SessionView? applied = null;
        await RunAsync(new WorkflowCommand.SetImageProcessingSelection(step.Step, selection) { ImageInput = expectedInput }, cancellationToken,
            succeeded => applied = succeeded).ConfigureAwait(true);

        return ReferenceEquals(applied, _session) && applied is
        {
            ImageProcessingExecution.Method: var appliedMethod,
            CurrentStep: { } appliedStep,
            CurrentArtefact: { } appliedInput,
        } && applied.Id == view.Id && appliedMethod == method && appliedStep.Step == step.Step &&
            appliedStep.State == step.State && appliedStep.AttemptCount == step.AttemptCount &&
            appliedInput.RevisionId == input.RevisionId && appliedInput.Sha256 == input.Sha256;
    }

    private static WorkflowCommand.ImageProcessingInput? DisplayedImageProcessingInput(SessionView? view) =>
        view is { ImageProcessingExecution.Method: { } method, CurrentArtefact: { IsCurrentStepResult: false } input }
            ? new(method, input.RevisionId, input.Sha256)
            : null;

    partial void OnDraftImageProcessingMethodChanged(ImageProcessingMethod? value)
    {
        OnPropertyChanged(nameof(CurrentImageProcessingMethod));
        OnPropertyChanged(nameof(CurrentImageProcessingUnavailable));
        OnPropertyChanged(nameof(ShowsMeituRecognitionModes));
        NotifyBackgroundRemovalMode();
    }

    private void NotifyImageProcessingAdvance()
    {
        OnPropertyChanged(nameof(CanEditImageProcessingMethods));
        OnPropertyChanged(nameof(ShowsImageProcessingMethodSelection));
        OnPropertyChanged(nameof(GuidedRunCommand));
        OnPropertyChanged(nameof(GuidedRunLabel));
        OnPropertyChanged(nameof(ShowsSeparateSkip));
        OnPropertyChanged(nameof(ShowsSeparateKeepOriginalExtent));
        OnPropertyChanged(nameof(ShowsSeparateRetry));
        OnPropertyChanged(nameof(CanRunGuidedStep));
        OnPropertyChanged(nameof(CanConfirmAutomaticSelection));
        AdvanceImageProcessingCommand.NotifyCanExecuteChanged();
    }

    private void ShowImageProcessingMethod()
    {
        RefreshTaskImageProcessingMethods();
        DraftImageProcessingMethod = _session?.ImageProcessingExecution?.Method;
        OnPropertyChanged(nameof(CurrentImageProcessingMethod));
        OnPropertyChanged(nameof(CurrentImageProcessingUnavailable));
        OnPropertyChanged(nameof(ShowsImageProcessingMethod));
        OnPropertyChanged(nameof(ShowsMeituRecognitionModes));
    }

    private void RefreshTaskImageProcessingMethods()
    {
        _taskImageProcessingMethods = BuildTaskImageProcessingMethods();
        OnPropertyChanged(nameof(TaskImageProcessingMethods));
    }

    private IReadOnlyList<ImageProcessingMethodOption> BuildTaskImageProcessingMethods()
    {
        if (IsCombinedBackgroundRemoval)
            return [ImageProcessingMethodOption.Create(ImageProcessingMethod.OpenAiCombined, _sessions)];
        if (_session?.CurrentStep is not { } step) return [];
        var options = ImageProcessingMethodOption.ForStep(step.Step, _sessions, includeCombined: true);
        return InputExceedsLocalUpscaleLimit && step.Step == StepKind.Enhancement
            ? options.Select(option => option.Method == ImageProcessingMethod.AiUpscale && option.IsAvailable
                ? option with { IsAvailable = false, Description = Strings.Resolve("ImageProcessing_UpscaleTooLarge") }
                : option).ToArray()
            : options;
    }
}
