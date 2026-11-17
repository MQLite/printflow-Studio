using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Resources;
using PrintFlow.App.Settings;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Settings;

namespace PrintFlow.App.ViewModels;

public sealed partial class SettingsViewModel
{
    private ImageProcessingPolicy? _savedImageProcessing;
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

    public string ImageProcessingHeading => Strings.Resolve("ImageProcessing_Heading");
    public string ImageProcessingHint => Strings.Resolve("ImageProcessing_Hint");
    public string ImageProcessingBreadcrumb => Strings.Resolve("ImageProcessing_Breadcrumb");
    public string EnhancementMethodLabel => Strings.Resolve("ImageProcessing_Enhancement");
    public string BackgroundRemovalMethodLabel => Strings.Resolve("ImageProcessing_BackgroundRemoval");
    public string CropMethodLabel => Strings.Resolve("ImageProcessing_Crop");
    public string IgnoreSmallTrimNoiseLabel => Strings.Resolve("ImageProcessing_IgnoreNoise");
    public string ImageProcessingAvailableHint => Strings.Resolve("ImageProcessing_Available");
    public string ImageProcessingRestoreLabel => Strings.Resolve("ImageProcessing_Restore");
    public string ImageProcessingSaveLabel => Strings.Resolve("ImageProcessing_Save");
    public string ImageProcessingBackLabel => Strings.Resolve("ImageProcessing_Back");
    public string ImageProcessingLeaveQuestion => Strings.Resolve("ImageProcessing_LeaveQuestion");
    public string ImageProcessingDiscardLabel => Strings.Resolve("ImageProcessing_Discard");
    public string ImageProcessingContinueEditingLabel => Strings.Resolve("ImageProcessing_KeepEditing");

    public sealed record MethodOption(ImageProcessingMethod Method, string Label, bool IsAvailable);

    public IReadOnlyList<MethodOption> EnhancementMethods => [
        Option(ImageProcessingMethod.MeituEnhancement, "Meitu", true),
        Option(ImageProcessingMethod.LightSharpen, "LightSharpen"),
        Option(ImageProcessingMethod.AiUpscale, "AiUpscale"),
        Option(ImageProcessingMethod.OpenAiRestore, "OpenAiRestore"),
        Option(ImageProcessingMethod.SkipEnhancement, "SkipEnhancement"),
    ];

    public IReadOnlyList<MethodOption> BackgroundRemovalMethods => [
        Option(ImageProcessingMethod.MeituBackgroundRemoval, "Meitu", true),
        Option(ImageProcessingMethod.BiRefNet, "BiRefNet"),
        Option(ImageProcessingMethod.OpenAiCutout, "OpenAiCutout"),
        Option(ImageProcessingMethod.KeepBackground, "KeepBackground"),
    ];

    public IReadOnlyList<MethodOption> CropMethods => [
        Option(ImageProcessingMethod.AlphaTrim, "AlphaTrim", true),
        Option(ImageProcessingMethod.ManualCrop, "ManualCrop"),
        Option(ImageProcessingMethod.KeepCanvas, "KeepCanvas"),
    ];

    private static MethodOption Option(ImageProcessingMethod method, string resource, bool available = false) =>
        new(method, Strings.Resolve("ImageProcessing_Method_" + resource), available);

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
            else if (!policy.Value.Selection.CanRunInCurrentBuild)
                ImageProcessingNotice = Strings.Resolve("ImageProcessing_UnavailableDefaults");
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
        if (!CanSaveImageProcessing() || ImageProcessingDraft() is not { } policy) return;
        if (!policy.Selection.CanRunInCurrentBuild)
        {
            ImageProcessingNotice = Strings.Resolve("ImageProcessing_UnavailableDefaults");
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
        ImageProcessingDraft() is not null;
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
        DiscardImageProcessingCommand.NotifyCanExecuteChanged();
        KeepEditingImageProcessingCommand.NotifyCanExecuteChanged();
    }
}
