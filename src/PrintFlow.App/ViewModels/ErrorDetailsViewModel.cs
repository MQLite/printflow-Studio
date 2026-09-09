using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Localisation;
using PrintFlow.App.Navigation;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels;

/// <summary>Presentation of one Workflow-owned diagnostics read model; no evidence joins or recovery rules.</summary>
public sealed partial class ErrorDetailsViewModel : ObservableObject
{
    private readonly ISessionService _sessions;
    private readonly INavigationService _navigation;
    private readonly IDiagnosticPackageService? _packages;
    private readonly IDiagnosticPackageDestinationPicker? _packageDestination;
    private SessionId? _sessionId;
    private AttemptId? _attemptId;
    private ErrorDetailsView? _details;
    private DiagnosticPackagePlan? _packagePlan;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isPackagePreview;

    private OperationFailure? _noticeFailure;
    private string? _noticeText;

    public string? Notice => _noticeText ?? (_noticeFailure is { } failure ? DisplayNames.Failure(failure) : null);

    public ErrorDetailsViewModel(ISessionService sessions, INavigationService navigation,
        ILocalisationService localisation,
        IDiagnosticPackageService? packages = null,
        IDiagnosticPackageDestinationPicker? packageDestination = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(localisation);
        _sessions = sessions;
        _navigation = navigation;
        _packages = packages;
        _packageDestination = packageDestination;
        WeakEventManager<ILocalisationService, EventArgs>.AddHandler(
            localisation, nameof(ILocalisationService.LanguageChanged), OnLanguageChanged);
    }

    public string Heading => Strings.ErrorDetails_Heading;
    public string WhatHappenedHeading => Strings.ErrorDetails_WhatHappened;
    public string ProcessingHeading => Strings.ErrorDetails_Processing;
    public string WorkflowLabel => Strings.ErrorDetails_Workflow;
    public string StepLabel => Strings.ErrorDetails_Step;
    public string CodeLabel => Strings.ErrorDetails_Code;
    public string Description => _details?.MessageKey is { } key ? Strings.Resolve(key)
        : _details?.AttemptStatus switch
        {
            AttemptStatus.Interrupted => Strings.ErrorDetails_Interrupted,
            AttemptStatus.Cancelled => Strings.ErrorDetails_Cancelled,
            _ => Strings.ErrorDetails_NotRecorded,
        };
    public string Code => _details?.StableCode ?? Strings.ErrorDetails_NotRecorded;
    public string Workflow => _details is { } details ? DisplayNames.Workflow(details.Workflow) : string.Empty;
    public string Step => _details is { } details ? DisplayNames.Step(details.Step) : string.Empty;
    public string RetryInformation => _details is { } details
        ? string.Format(CultureInfo.CurrentCulture, Strings.ErrorDetails_RetryInformation,
            details.AttemptNumber, details.PreviousRetries) : string.Empty;
    public string InputLabel => Strings.ErrorDetails_InputPath;
    public string ExpectedOutputLabel => Strings.ErrorDetails_ExpectedOutputPath;
    public string InputPath => PathText(_details?.ManagedInputPath, _details?.InputPathStatus);
    public string ExpectedOutputPath => PathText(_details?.ExpectedOutputPath, _details?.ExpectedOutputPathStatus);
    public string EvidenceHeading => Strings.ErrorDetails_Evidence;
    public string ScreenshotPathLabel => Strings.ErrorDetails_ScreenshotPath;
    public string ScreenshotPath => _details?.ScreenshotPath ?? Strings.ErrorDetails_NotCaptured;
    public bool HasScreenshot => _details is { ScreenshotStatus: DiagnosticImageStatus.Available, Screenshot: not null };
    public ReadOnlyMemory<byte> ScreenshotPayload => _details?.Screenshot?.Payload ?? ReadOnlyMemory<byte>.Empty;
    public string ScreenshotNotice => _details?.ScreenshotStatus == DiagnosticImageStatus.Unavailable
        ? Strings.ErrorDetails_ImageUnavailable : HasScreenshot ? string.Empty : Strings.ErrorDetails_NotCaptured;
    public string TechnicalHeading => Strings.ErrorDetails_TechnicalDetail;
    public string TechnicalDetail => _details?.TechnicalDetail ?? Strings.ErrorDetails_NotRecorded;
    public string HistoricalNotice => _details is { IsCurrent: false } ? Strings.ErrorDetails_Historical : string.Empty;
    public string RetryLabel => Strings.Session_Retry;
    public string ManualProcessingLabel => Strings.ErrorDetails_ManualProcessing;
    public string ReenterAutomationLabel => Strings.Session_ReenterAutomation;
    public string BackLabel => Strings.ErrorDetails_Back;
    public bool HasDetails => _details is not null;
    public bool CanRetry => Allows(ErrorRecoveryAction.Retry);
    public bool CanManualProcessing => Allows(ErrorRecoveryAction.ManualProcessing);
    public bool CanReenterAutomation => Allows(ErrorRecoveryAction.ReenterAutomation);
    public bool IsErrorDetails => !IsPackagePreview;
    public bool CanExportDiagnosticPackage =>
        !IsBusy && IsErrorDetails && HasDetails && _packages is not null && _packageDestination is not null;
    private bool CanAct => !IsBusy && IsErrorDetails;

    public string ExportDiagnosticPackageLabel => Strings.ErrorDetails_ExportDiagnosticPackage;
    public string PackageHeading => Strings.DiagnosticPackage_Heading;
    public string PackageLocalOnlyNotice => Strings.DiagnosticPackage_LocalOnlyNotice;
    public string PackageSubjectHeading => Strings.DiagnosticPackage_Subject;
    public string PackageProcessingNameLabel => Strings.DiagnosticPackage_ProcessingName;
    public string PackageWorkflowLabel => Strings.ErrorDetails_Workflow;
    public string PackageStepLabel => Strings.ErrorDetails_Step;
    public string PackageCodeLabel => Strings.ErrorDetails_Code;
    public string PackageReferenceLabel => Strings.DiagnosticPackage_FailureReference;
    public string PackageProcessingName => _packagePlan?.Subject.ProcessingName ?? string.Empty;
    public string PackageWorkflow => _packagePlan is { } plan ? DisplayNames.Workflow(plan.Subject.Workflow) : string.Empty;
    public string PackageStep => _packagePlan is { } plan ? DisplayNames.Step(plan.Subject.Step) : string.Empty;
    public string PackageCode => _packagePlan?.Failure.StableCode ?? Strings.ErrorDetails_NotRecorded;
    public string PackageReference => _packagePlan?.Subject.AttemptId.ToString() ?? string.Empty;
    public string PackageIncludedHeading => Strings.DiagnosticPackage_Included;
    public string PackageUnavailableHeading => Strings.DiagnosticPackage_Unavailable;
    public string PackageExcludedHeading => Strings.DiagnosticPackage_ExcludedByPolicy;
    public string PackagePathsHeading => Strings.DiagnosticPackage_LocalPaths;
    public string PackageDestinationHeading => Strings.DiagnosticPackage_Destination;
    public string PackageDestinationHint => Strings.DiagnosticPackage_DestinationHint;
    public string PackageNoOverwriteNotice => Strings.DiagnosticPackage_NoOverwrite;
    public string SavePackageLabel => Strings.DiagnosticPackage_Save;
    public string BackToDetailsLabel => Strings.DiagnosticPackage_Back;
    public string PackagePathPreview => _packagePlan is not { } plan ? string.Empty : string.Join(
        Environment.NewLine,
        string.Format(CultureInfo.CurrentCulture, Strings.DiagnosticPackage_PathManagedInput,
            PathText(plan.Failure.ManagedInputPath, plan.Failure.InputPathStatus)),
        string.Format(CultureInfo.CurrentCulture, Strings.DiagnosticPackage_PathExpectedOutput,
            PathText(plan.Failure.ExpectedOutputPath, plan.Failure.ExpectedOutputPathStatus)),
        string.Format(CultureInfo.CurrentCulture, Strings.DiagnosticPackage_PathScreenshot,
            plan.Failure.ScreenshotPath ?? Strings.ErrorDetails_NotCaptured),
        string.Format(CultureInfo.CurrentCulture, Strings.DiagnosticPackage_PathLocalLog,
            plan.Storage.LocalLogLocation),
        string.Format(CultureInfo.CurrentCulture, Strings.DiagnosticPackage_PathScreenshotFolder,
            plan.Storage.ScreenshotLocation));
    public IReadOnlyList<DiagnosticPackagePreviewItem> IncludedPackageItems =>
        PreviewItems(DiagnosticPackageItemDisposition.Included);
    public IReadOnlyList<DiagnosticPackagePreviewItem> UnavailablePackageItems =>
        PreviewItems(DiagnosticPackageItemDisposition.Unavailable);
    public IReadOnlyList<DiagnosticPackagePreviewItem> ExcludedPackageItems =>
        PreviewItems(DiagnosticPackageItemDisposition.ExcludedByPolicy);
    public bool HasUnavailablePackageItems => UnavailablePackageItems.Count > 0;
    public bool HasExcludedPackageItems => ExcludedPackageItems.Count > 0;

    public async Task OpenAsync(SessionId sessionId, AttemptId attemptId, CancellationToken cancellationToken)
    {
        _sessionId = sessionId;
        _attemptId = attemptId;
        _details = null;
        _packagePlan = null;
        IsPackagePreview = false;
        _noticeFailure = null;
        _noticeText = null;
        IsBusy = true;
        try
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task RetryAsync(CancellationToken cancellationToken) => RecoverAsync(ErrorRecoveryAction.Retry, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task ManualProcessingAsync(CancellationToken cancellationToken) => RecoverAsync(ErrorRecoveryAction.ManualProcessing, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task ReenterAutomationAsync(CancellationToken cancellationToken) => RecoverAsync(ErrorRecoveryAction.ReenterAutomation, cancellationToken);

    [RelayCommand(CanExecute = nameof(CanExportDiagnosticPackage))]
    private async Task OpenDiagnosticPackageAsync(CancellationToken cancellationToken)
    {
        if (!CanExportDiagnosticPackage || _packages is null ||
            _sessionId is not { } sessionId || _attemptId is not { } attemptId)
            return;

        IsBusy = true;
        _noticeFailure = null;
        _noticeText = null;
        try
        {
            OperationResult<DiagnosticPackagePlan> result = await _packages.BuildPlanAsync(
                sessionId, attemptId, cancellationToken).ConfigureAwait(true);
            if (result.IsSuccess)
            {
                _packagePlan = result.Value;
                IsPackagePreview = true;
            }
            else
            {
                _noticeText = string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.DiagnosticPackage_PreviewFailed,
                    result.Failure.Code);
            }
            OnPropertyChanged(string.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSavePackage =>
        !IsBusy && IsPackagePreview && _packagePlan is not null &&
        _packages is not null && _packageDestination is not null;

    [RelayCommand(CanExecute = nameof(CanSavePackage))]
    private async Task SavePackageAsync(CancellationToken cancellationToken)
    {
        if (!CanSavePackage || _packagePlan is not { } plan ||
            _packages is null || _packageDestination is null)
            return;

        string? destination = _packageDestination.PickDestination(
            Strings.DiagnosticPackage_SaveDialogTitle,
            Strings.DiagnosticPackage_SaveDialogFilter,
            plan.SuggestedFileName);
        if (string.IsNullOrWhiteSpace(destination))
            return;

        IsBusy = true;
        _noticeFailure = null;
        _noticeText = null;
        try
        {
            OperationResult<DiagnosticPackageExportResult> result = await _packages.ExportAsync(
                plan, destination, cancellationToken).ConfigureAwait(true);
            _noticeText = result.IsSuccess
                ? string.Format(CultureInfo.CurrentCulture, Strings.DiagnosticPackage_Saved, result.Value.SavedPath)
                : string.Format(CultureInfo.CurrentCulture, Strings.DiagnosticPackage_SaveFailed, result.Failure.Code);
            OnPropertyChanged(nameof(Notice));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanBackFromPackage => !IsBusy && IsPackagePreview;

    [RelayCommand(CanExecute = nameof(CanBackFromPackage))]
    private void BackFromPackage()
    {
        if (!CanBackFromPackage) return;
        _packagePlan = null;
        _noticeText = null;
        IsPackagePreview = false;
        OnPropertyChanged(string.Empty);
    }

    private async Task RecoverAsync(ErrorRecoveryAction action, CancellationToken cancellationToken)
    {
        if (IsBusy || !Allows(action) || _sessionId is not { } sessionId || _attemptId is not { } attemptId)
            return;
        IsBusy = true;
        try
        {
            OperationResult<SessionView> result = await _sessions.ResolveErrorRecoveryAsync(
                sessionId, attemptId, action, Environment.UserName, cancellationToken).ConfigureAwait(true);
            if (result.IsSuccess)
            {
                _navigation.GoToSession(result.Value);
            }
            else
            {
                _noticeFailure = result.Failure;
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task BackAsync(CancellationToken cancellationToken)
    {
        if (IsBusy || _sessionId is not { } sessionId)
            return;
        IsBusy = true;
        try
        {
            OperationResult<SessionView> result = await _sessions.LoadAsync(sessionId, cancellationToken).ConfigureAwait(true);
            if (result.IsSuccess)
                _navigation.GoToSession(result.Value);
            else
            {
                _noticeFailure = result.Failure;
                OnPropertyChanged(nameof(Notice));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (_sessionId is not { } sessionId || _attemptId is not { } attemptId)
            return;
        OperationResult<ErrorDetailsView> result = await _sessions.LoadErrorDetailsAsync(
            sessionId, attemptId, cancellationToken).ConfigureAwait(true);
        _details = result.IsSuccess ? result.Value : null;
        if (result.IsFailure)
            _noticeFailure = result.Failure;
        OnPropertyChanged(string.Empty);
    }

    private bool Allows(ErrorRecoveryAction action) => _details?.AvailableActions.Contains(action) == true;

    private static string PathText(string? path, ErrorPathStatus? status) => status switch
    {
        ErrorPathStatus.Available when path is not null => path,
        ErrorPathStatus.NotEstablished => Strings.ErrorDetails_NotEstablished,
        ErrorPathStatus.Unavailable => Strings.ErrorDetails_PathUnavailable,
        _ => Strings.ErrorDetails_NotRecorded,
    };

    partial void OnIsBusyChanged(bool value)
    {
        RetryCommand.NotifyCanExecuteChanged();
        ManualProcessingCommand.NotifyCanExecuteChanged();
        ReenterAutomationCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
        OpenDiagnosticPackageCommand.NotifyCanExecuteChanged();
        SavePackageCommand.NotifyCanExecuteChanged();
        BackFromPackageCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsPackagePreviewChanged(bool value)
    {
        OnPropertyChanged(nameof(IsErrorDetails));
        RetryCommand.NotifyCanExecuteChanged();
        ManualProcessingCommand.NotifyCanExecuteChanged();
        ReenterAutomationCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
        OpenDiagnosticPackageCommand.NotifyCanExecuteChanged();
        SavePackageCommand.NotifyCanExecuteChanged();
        BackFromPackageCommand.NotifyCanExecuteChanged();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);

    private IReadOnlyList<DiagnosticPackagePreviewItem> PreviewItems(
        DiagnosticPackageItemDisposition disposition) => _packagePlan?.Items
        .Where(item => item.Disposition == disposition)
        .Select(item => new DiagnosticPackagePreviewItem(ItemText(item)))
        .ToArray() ?? [];

    private string ItemText(DiagnosticPackageItem item) => item.Role switch
    {
        DiagnosticPackageItemRole.Manifest => Strings.DiagnosticPackage_ItemManifest,
        DiagnosticPackageItemRole.StructuredFailureSummary => Strings.DiagnosticPackage_ItemFailureSummary,
        DiagnosticPackageItemRole.StructuredAutomationLog
            when _packagePlan?.Failure.LogStatus == DiagnosticPackageLogStatus.NotRecorded =>
            Strings.DiagnosticPackage_ItemLogNotRecorded,
        DiagnosticPackageItemRole.StructuredAutomationLog when item.Disposition == DiagnosticPackageItemDisposition.Unavailable =>
            Strings.DiagnosticPackage_ItemLogUnavailable,
        DiagnosticPackageItemRole.StructuredAutomationLog => string.Format(
            CultureInfo.CurrentCulture,
            Strings.DiagnosticPackage_ItemAutomationLog,
            _packagePlan?.Failure.LogEntryId,
            _packagePlan?.Failure.LogAtUtc),
        DiagnosticPackageItemRole.EnvironmentSummary => string.Format(
            CultureInfo.CurrentCulture,
            Strings.DiagnosticPackage_ItemEnvironment,
            _packagePlan?.Environment.Verified == true
                ? Strings.DiagnosticPackage_EnvironmentReady
                : Strings.DiagnosticPackage_EnvironmentNotReady,
            _packagePlan?.Environment.PresetIdentity ?? Strings.ErrorDetails_NotRecorded,
            _packagePlan?.Environment.Checks.Count ?? 0),
        DiagnosticPackageItemRole.ApplicationInfo => string.Format(
            CultureInfo.CurrentCulture,
            Strings.DiagnosticPackage_ItemApplication,
            _packagePlan?.Application.Name,
            _packagePlan?.Application.Version),
        DiagnosticPackageItemRole.LocalPaths => Strings.DiagnosticPackage_ItemLocalPaths,
        DiagnosticPackageItemRole.FailureScreenshot when item.Disposition == DiagnosticPackageItemDisposition.Unavailable =>
            Strings.DiagnosticPackage_ItemScreenshotUnavailable,
        DiagnosticPackageItemRole.FailureScreenshot when item.Disposition == DiagnosticPackageItemDisposition.ExcludedByPolicy =>
            Strings.DiagnosticPackage_ItemScreenshotExcluded,
        DiagnosticPackageItemRole.FailureScreenshot => Strings.DiagnosticPackage_ItemFailureScreenshot,
        DiagnosticPackageItemRole.CustomerSource => Strings.DiagnosticPackage_ItemCustomerSource,
        DiagnosticPackageItemRole.InputSnapshot => Strings.DiagnosticPackage_ItemInputSnapshot,
        DiagnosticPackageItemRole.RevisionArtwork => Strings.DiagnosticPackage_ItemRevisionArtwork,
        DiagnosticPackageItemRole.ApprovedOutput => Strings.DiagnosticPackage_ItemApprovedOutput,
        DiagnosticPackageItemRole.ProductionOutput => Strings.DiagnosticPackage_ItemProductionOutput,
        DiagnosticPackageItemRole.ManualArtwork => Strings.DiagnosticPackage_ItemManualArtwork,
        DiagnosticPackageItemRole.RecoveryEvidence => Strings.DiagnosticPackage_ItemRecoveryEvidence,
        DiagnosticPackageItemRole.UnknownEvidence => Strings.DiagnosticPackage_ItemUnknownEvidence,
        DiagnosticPackageItemRole.DiagnosticDatabase => Strings.DiagnosticPackage_ItemDiagnosticDatabase,
        _ => item.Role.ToString(),
    };
}

public sealed record DiagnosticPackagePreviewItem(string Text);
