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
    private SessionId? _sessionId;
    private AttemptId? _attemptId;
    private ErrorDetailsView? _details;

    [ObservableProperty]
    private bool _isBusy;

    private OperationFailure? _noticeFailure;

    public string? Notice => _noticeFailure is { } failure ? DisplayNames.Failure(failure) : null;

    public ErrorDetailsViewModel(ISessionService sessions, INavigationService navigation,
        ILocalisationService localisation)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(localisation);
        _sessions = sessions;
        _navigation = navigation;
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
    private bool CanAct => !IsBusy;

    public async Task OpenAsync(SessionId sessionId, AttemptId attemptId, CancellationToken cancellationToken)
    {
        _sessionId = sessionId;
        _attemptId = attemptId;
        _details = null;
        _noticeFailure = null;
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
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
}
