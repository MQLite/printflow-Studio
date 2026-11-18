using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Resources;
using PrintFlow.App.Settings;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Workflow.Services;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.App.ViewModels;

public sealed partial class SettingsViewModel
{
    private readonly ISessionService _imageProcessingSessions;
    private ImageProcessingPolicy? _savedImageProcessing;
    private readonly IOpenAiImageCredentials? _openAiCredentials;
    private string _savedImageMargin = "0";
    private TaskCompletionSource<bool>? _leaveImageSettings;
    private bool _imageSettingsReadable;
    private bool _useOpenAiCombined;

    [ObservableProperty] private bool _isEditingImageProcessing;
    [ObservableProperty] private bool _isImageProcessingLeavePending;
    [ObservableProperty] private string? _imageProcessingNotice;
    [ObservableProperty] private ImageProcessingMethod? _selectedEnhancementMethod;
    [ObservableProperty] private ImageProcessingMethod? _selectedBackgroundRemovalMethod;
    [ObservableProperty] private ImageProcessingMethod? _selectedCropMethod;
    [ObservableProperty] private bool _ignoreSmallTrimNoise = true;

    public bool ShowOpenAiCredentials => _openAiCredentials is not null;
    public string OpenAiCredentialHeading => Strings.Resolve("ImageProcessing_OpenAiCredentialHeading");
    public string OpenAiCredentialSaveLabel => Strings.Resolve("ImageProcessing_OpenAiCredentialSave");
    public string OpenAiCredentialRemoveLabel => Strings.Resolve("ImageProcessing_OpenAiCredentialRemove");
    public string OpenAiCredentialStatus
    {
        get
        {
            if (_openAiCredentials is null)
                return string.Empty;
            var result = _openAiCredentials.Read();
            return result.IsFailure ? ImageProcessingMethodOption.UnavailableHint(result.Failure)
                : Strings.Resolve(result.Value is null
                    ? "ImageProcessing_OpenAiKeyMissing" : "ImageProcessing_OpenAiConfigured");
        }
    }

    internal void SaveOpenAiCredential(string key)
    {
        if (_openAiCredentials is not null && CanBackFromImageProcessing())
            ApplyOpenAiCredentialResult(_openAiCredentials.Save(key));
    }

    [RelayCommand(CanExecute = nameof(CanBackFromImageProcessing))]
    private void RemoveOpenAiCredential()
    {
        if (_openAiCredentials is not null && CanBackFromImageProcessing())
            ApplyOpenAiCredentialResult(_openAiCredentials.Delete());
    }

    private void ApplyOpenAiCredentialResult(OperationResult<Unit> result)
    {
        if (result.IsFailure)
        {
            ImageProcessingNotice = ImageProcessingMethodOption.UnavailableHint(result.Failure);
            return;
        }

        var draft = ImageProcessingDraft();
        string margin = TrimSafetyMarginPixels;
        OnPropertyChanged(nameof(OpenAiCredentialStatus));
        OnPropertyChanged(nameof(EnhancementMethods));
        OnPropertyChanged(nameof(BackgroundRemovalMethods));
        if (draft is not null)
            ApplyImageProcessingDraft(draft, margin);
        ImageProcessingNotice = draft is not null && UnavailableDefault(draft.Selection) is { } unavailable
            ? DescribeUnavailableDefaults(unavailable) : null;
    }

    public string ImageProcessingHeading => Strings.Resolve("ImageProcessing_Heading");
    public string ImageProcessingHint => Strings.Resolve("ImageProcessing_Hint");
    public string ImageProcessingBreadcrumb => Strings.Resolve("ImageProcessing_Breadcrumb");
    public string EnhancementMethodLabel => Strings.Resolve("ImageProcessing_Enhancement");
    public string BackgroundRemovalMethodLabel => Strings.Resolve("ImageProcessing_BackgroundRemoval");
    public string CropMethodLabel => Strings.Resolve("ImageProcessing_Crop");
    public string IgnoreSmallTrimNoiseLabel => Strings.Resolve("ImageProcessing_IgnoreNoise");
    public string ImageProcessingRestoreLabel => Strings.Resolve("ImageProcessing_Restore");
    public string ImageProcessingSaveLabel => Strings.Resolve("ImageProcessing_Save");
    public string ImageProcessingBackLabel => Strings.Resolve("ImageProcessing_Back");
    public string ImageProcessingLeaveQuestion => Strings.Resolve("ImageProcessing_LeaveQuestion");
    public string ImageProcessingDiscardLabel => Strings.Resolve("ImageProcessing_Discard");
    public string ImageProcessingContinueEditingLabel => Strings.Resolve("ImageProcessing_KeepEditing");

    public IReadOnlyList<ImageProcessingMethodOption> EnhancementMethods =>
        ImageProcessingMethodOption.ForStep(StepKind.Enhancement, _imageProcessingSessions);
    public IReadOnlyList<ImageProcessingMethodOption> BackgroundRemovalMethods =>
        ImageProcessingMethodOption.ForStep(StepKind.BackgroundRemoval, _imageProcessingSessions);
    public IReadOnlyList<ImageProcessingMethodOption> CropMethods =>
        ImageProcessingMethodOption.ForStep(StepKind.Trim, _imageProcessingSessions);

    [RelayCommand(CanExecute = nameof(CanOpenImageProcessing))]
    private async Task OpenImageProcessingAsync(CancellationToken cancellationToken)
    {
        if (!CanOpenImageProcessing()) return;
        IsBusy = true;
        try
        {
            IsEditingImageProcessing = true;
            ImageProcessingNotice = null;
            _imageSettingsReadable = false;
            _savedImageProcessing = null;
            var entries = await _settings.ReadAllAsync(cancellationToken).ConfigureAwait(true);
            if (entries.IsFailure)
            {
                ImageProcessingNotice = Describe(Strings.Settings_LoadFailed, entries.Failure);
                ApplyImageProcessingDraft(null, "0");
                return;
            }

            _imageSettingsReadable = true;
            _savedImageMargin = ImageProcessingPolicy.ReadTrimMarginPixels(entries.Value).ToString(CultureInfo.InvariantCulture);
            var policy = ImageProcessingPolicy.FromSettings(entries.Value);
            _savedImageProcessing = policy.IsSuccess ? policy.Value : null;
            ApplyImageProcessingDraft(_savedImageProcessing, _savedImageMargin);
            if (policy.IsFailure)
                ImageProcessingNotice = Strings.Resolve("ImageProcessing_InvalidDefaults");
            else if (UnavailableDefault(policy.Value.Selection) is { } unavailable)
                ImageProcessingNotice = DescribeUnavailableDefaults(unavailable);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanRestoreImageProcessing))]
    private void RestoreImageProcessingDefaults()
    {
        ApplyImageProcessingDraft(ImageProcessingPolicy.CompatibleDefault,
            SettingsDefaults.FallbackTrimSafetyMarginPixels.ToString(CultureInfo.InvariantCulture));
        ImageProcessingNotice = null;
    }

    [RelayCommand(CanExecute = nameof(CanSaveImageProcessing))]
    private async Task SaveImageProcessingAsync(CancellationToken cancellationToken)
    {
        if (!CanApply() || !IsEditingImageProcessing || !_imageSettingsReadable ||
            ImageProcessingDraft() is not { Selection.HasValidMethods: true } policy)
            return;
        if (UnavailableDefault(policy.Selection) is { } unavailable)
        {
            ImageProcessingNotice = DescribeUnavailableDefaults(unavailable);
            return;
        }
        if (!TryReadPixels(TrimSafetyMarginPixels, out int margin))
        {
            ImageProcessingNotice = Strings.Settings_TrimMarginInvalid;
            return;
        }

        IsBusy = true;
        try
        {
            OperationResult<Unit> saved = await _settings.UpsertAsync(
                [policy.ToSetting(), SettingEntry.Integer(SettingKey.TrimSafetyMarginPixels, margin)],
                cancellationToken).ConfigureAwait(true);
            if (saved.IsFailure)
            {
                ImageProcessingNotice = Describe(
                    string.Format(CultureInfo.CurrentCulture, Strings.Settings_SaveFailed, ImageProcessingSaveLabel),
                    saved.Failure);
                return;
            }

            _savedImageProcessing = policy;
            _savedImageMargin = margin.ToString(CultureInfo.InvariantCulture);
            TrimSafetyMarginPixels = _savedImageMargin;
            ImageProcessingNotice = Strings.Settings_Saved;
            _leaveImageSettings?.TrySetResult(true);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanBackFromImageProcessing))]
    private async Task BackFromImageProcessingAsync(CancellationToken cancellationToken)
    {
        if (await RequestImageSettingsCloseAsync(cancellationToken).ConfigureAwait(true))
            IsEditingImageProcessing = false;
    }

    [RelayCommand(CanExecute = nameof(CanRespondToImageProcessingLeave))]
    private void DiscardImageProcessing() => _leaveImageSettings?.TrySetResult(true);

    [RelayCommand(CanExecute = nameof(CanRespondToImageProcessingLeave))]
    private void KeepEditingImageProcessing() => _leaveImageSettings?.TrySetResult(false);

    internal async Task<bool> RequestImageSettingsCloseAsync(CancellationToken cancellationToken)
    {
        if (IsBusy || _leaveImageSettings is not null) return false;
        if (!_imageSettingsReadable || (ImageProcessingDraft() == _savedImageProcessing &&
                                        TrimSafetyMarginPixels == _savedImageMargin))
            return true;

        _leaveImageSettings = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        IsImageProcessingLeavePending = true;
        try
        {
            return await _leaveImageSettings.Task.WaitAsync(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            _leaveImageSettings = null;
            IsImageProcessingLeavePending = false;
        }
    }

    private void ApplyImageProcessingDraft(ImageProcessingPolicy? policy, string margin)
    {
        _useOpenAiCombined = policy?.Selection.UseOpenAiCombined ?? false;
        SelectedEnhancementMethod = policy?.Selection.Enhancement;
        SelectedBackgroundRemovalMethod = policy?.Selection.BackgroundRemoval;
        SelectedCropMethod = policy?.Selection.Crop;
        IgnoreSmallTrimNoise = policy?.IgnoreSmallTrimNoise ?? true;
        TrimSafetyMarginPixels = margin;
        SaveImageProcessingCommand.NotifyCanExecuteChanged();
    }

    private ImageProcessingPolicy? ImageProcessingDraft() =>
        SelectedEnhancementMethod is { } enhancement &&
        SelectedBackgroundRemovalMethod is { } backgroundRemoval &&
        SelectedCropMethod is { } crop
            ? new(new SessionImageProcessingSelection(enhancement, backgroundRemoval, crop, _useOpenAiCombined),
                IgnoreSmallTrimNoise)
            : null;

    private bool CanOpenImageProcessing() => CanApply() && !IsEditingImageProcessing;
    private bool CanSaveImageProcessing() => CanApply() && IsEditingImageProcessing && _imageSettingsReadable &&
        ImageProcessingDraft() is { Selection.HasValidMethods: true } draft && UnavailableDefault(draft.Selection) is null;

    private OperationFailure? UnavailableDefault(SessionImageProcessingSelection selection) =>
        (selection.UseOpenAiCombined
            ? _imageProcessingSessions.GetImageProcessingUnavailable(ImageProcessingMethod.OpenAiCombined)
            : _imageProcessingSessions.GetImageProcessingUnavailable(selection.Enhancement)
                ?? _imageProcessingSessions.GetImageProcessingUnavailable(selection.BackgroundRemoval))
        ?? _imageProcessingSessions.GetImageProcessingUnavailable(selection.Crop);

    private static string DescribeUnavailableDefaults(OperationFailure failure) =>
        Strings.Resolve(ImageProcessingPolicy.UnavailableDefaultsMessage) + " " +
        ImageProcessingMethodOption.UnavailableHint(failure);
    private bool CanRestoreImageProcessing() => CanBackFromImageProcessing() && _imageSettingsReadable;
    private bool CanBackFromImageProcessing() => CanApply() && IsEditingImageProcessing && !IsImageProcessingLeavePending;
    private bool CanRespondToImageProcessingLeave() => CanApply() && IsImageProcessingLeavePending;

    partial void OnIsEditingImageProcessingChanged(bool value) => RefreshImageProcessingCommands();
    partial void OnIsImageProcessingLeavePendingChanged(bool value) => RefreshImageProcessingCommands();
    partial void OnSelectedEnhancementMethodChanged(ImageProcessingMethod? value) => SaveImageProcessingCommand.NotifyCanExecuteChanged();
    partial void OnSelectedBackgroundRemovalMethodChanged(ImageProcessingMethod? value) => SaveImageProcessingCommand.NotifyCanExecuteChanged();
    partial void OnSelectedCropMethodChanged(ImageProcessingMethod? value) => SaveImageProcessingCommand.NotifyCanExecuteChanged();

    private void RefreshImageProcessingCommands()
    {
        OpenImageProcessingCommand.NotifyCanExecuteChanged();
        RestoreImageProcessingDefaultsCommand.NotifyCanExecuteChanged();
        SaveImageProcessingCommand.NotifyCanExecuteChanged();
        BackFromImageProcessingCommand.NotifyCanExecuteChanged();
        RemoveOpenAiCredentialCommand.NotifyCanExecuteChanged();
        DiscardImageProcessingCommand.NotifyCanExecuteChanged();
        KeepEditingImageProcessingCommand.NotifyCanExecuteChanged();
    }
}
