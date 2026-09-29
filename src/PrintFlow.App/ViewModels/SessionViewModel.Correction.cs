using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels;

/// <summary>Where the correction interaction asks the view to put keyboard focus.</summary>
public enum CorrectionFocus
{
    None,

    /// <summary>The optional note box, when the Ask panel opens.</summary>
    NoteBox,

    /// <summary>The Ask button, when the Ask panel is cancelled.</summary>
    AskButton,

    /// <summary>The non-activating handed-off heading, after the files were prepared.</summary>
    HandedOffHeading,

    /// <summary>Import corrected image, after the picker closes or an import was refused.</summary>
    ImportButton,
}

/// <summary>What the correction panel is doing right now.</summary>
public enum CorrectionActivity
{
    Idle,
    Preparing,
    Checking,
    Repairing,
}

/// <summary>
/// Asking a colleague to correct a background-removal result, and bringing the correction back
/// (SCRUM-11148, design §7, §8.5).
/// </summary>
/// <remarks>
/// Opening the Ask panel, typing a note, cancelling, refreshing, changing language and opening the
/// folder reach no service that changes anything. The three consequential actions — Prepare files,
/// Import corrected image and Prepare files again — each call one dedicated service entry with the
/// identities captured when the operator acted, and a late answer about a different session or
/// request is discarded with a reload. Whether anything is offered comes from the read model; the
/// service re-checks everything.
/// </remarks>
public sealed partial class SessionViewModel
{
    private PrintFlow.Workflow.Ports.ICorrectionFolderShell? _correctionShell;

    // A correction interaction owns only the target on which it began. A same-target refresh
    // keeps it; showing a different request/review retires it, including its focus and busy work.
    private CorrectionInteraction? _correctionInteraction;

    private sealed class CorrectionInteraction(string target)
    {
        public string Target { get; set; } = target;
        public bool OwnsBusy { get; set; }
    }

    /// <summary>The request identity minted for the current Ask intent, reused on Try again.</summary>
    private Guid? _pendingCorrectionRequestId;

    /// <summary>The review the pending request identity was minted for.</summary>
    private string? _pendingCorrectionTarget;

    /// <summary>The request a shown correction message is about; a different request clears it.</summary>
    private Guid? _correctionMessageRequest;
    private readonly NoticeFailure _correctionFailure = new();
    private string? _correctionFailureWrapperKey;

    public string? CorrectionErrorCode => _correctionFailure.Code;

    /// <summary>Whether the inline Ask panel (S1) is open. Opening or cancelling it writes nothing.</summary>
    [ObservableProperty]
    private bool _isAskingColleague;

    /// <summary>The optional note. Means nothing until Prepare files is pressed.</summary>
    [ObservableProperty]
    private string? _correctionNote;

    [ObservableProperty]
    private CorrectionActivity _correctionActivity;

    /// <summary>A preparation failure or import refusal, in words; announced by the view.</summary>
    [ObservableProperty]
    private string? _correctionMessage;

    /// <summary>Where focus should go next; <see cref="CorrectionFocusToken"/> changes with each request.</summary>
    public CorrectionFocus CorrectionFocusTarget { get; private set; }

    [ObservableProperty]
    private int _correctionFocusToken;

    /// <summary>The request panel's read model, or null.</summary>
    public CorrectionHandoffView? CorrectionHandoff => _session?.Correction;

    // --- Offers ---------------------------------------------------------------------------

    /// <summary>"Ask a colleague to correct this image" beside Approve and Reject.</summary>
    public bool CanAskColleague => _session?.CanAskColleague == true && !IsBusy && !IsAdjustingTrim &&
        !IsAskingColleague && !IsCropping && _finalSaveOperation is null;

    public bool CanPrepareCorrection => IsAskingColleague && !IsBusy && _session?.CanAskColleague == true;

    public bool CanCancelAskColleague => IsAskingColleague && !IsBusy;

    /// <summary>The handed-off panel (S3): the request still grants an import.</summary>
    public bool ShowsCorrectionPanel => CorrectionHandoff is { CanImport: true };

    /// <summary>The history line for a request that no longer grants anything.</summary>
    public bool ShowsCorrectionObsolete => CorrectionHandoff is { IsObsolete: true };

    public bool CanImportCorrectedImage => CorrectionHandoff is { CanImport: true } && !IsBusy && !IsAdjustingTrim;

    /// <summary>Read-only: stays available in S3 while nothing is running.</summary>
    public bool CanOpenCorrectionFolder => ShowsCorrectionPanel && _correctionShell is not null;

    public bool CanRepairCorrectionFiles => CorrectionHandoff is { CanImport: true, MissingFiles: true } && !IsBusy;

    public bool ShowsCorrectionMissingFiles => CorrectionHandoff is { CanImport: true, MissingFiles: true };

    public bool ShowsLastImportUnfinished =>
        CorrectionHandoff is { Mode: CorrectionImportMode.AfterUnfinishedImport };

    /// <summary>"Return to automation" moves under Other options while a correction can be imported.</summary>
    public bool ShowsReenterAutomationBlock => CanReenterAutomation && !ShowsCorrectionPanel;

    public bool ShowsReenterAutomationInCorrection => CanReenterAutomation && ShowsCorrectionPanel;

    public bool IsCorrectionBusy => CorrectionActivity != CorrectionActivity.Idle;

    public bool HasCorrectionMessage => !string.IsNullOrWhiteSpace(CorrectionMessage);

    /// <summary>Input identity for Prepare files: a gesture begun on another review never submits.</summary>
    public string? AskIdentity => IsAskingColleague && _session is
    {
        State: SessionState.Active,
        CurrentStep: { Step: StepKind.BackgroundRemoval, State: StepState.ReviewRequired },
        CurrentArtefact: { IsCurrentStepResult: true } shown,
    } session
        ? $"ask|{session.Id}|{shown.RevisionId}|{shown.Sha256.Value}"
        : null;

    /// <summary>Input identity for Import corrected image, bound to the request and R.</summary>
    public string? ImportIdentity => CorrectionHandoff is { CanImport: true } handoff
        ? $"import|{handoff.RequestId}|{handoff.HandedOutRevisionId}|{handoff.HandedOutSha256.Value}"
        : null;

    // --- Wording ---------------------------------------------------------------------------

    public string AskColleagueLabel => Strings.Session_AskColleague;
    public string AskColleagueHint => Strings.Session_AskColleagueHint;
    public string CorrectionNoteLabel => Strings.Session_CorrectionNoteLabel;
    public string CorrectionPrepareLabel => HasCorrectionMessage && IsAskingColleague
        ? Strings.Session_CorrectionTryAgain : Strings.Session_CorrectionPrepare;
    public string CorrectionCancelLabel => Strings.Session_CorrectionCancel;
    public string CorrectionStatusText => Strings.Session_CorrectionStatus;
    public string CorrectionStep1Text => Strings.Session_CorrectionStep1;
    public string CorrectionOpenFolderLabel => Strings.Session_CorrectionOpenFolder;
    public string CorrectionStep4Text => Strings.Session_CorrectionStep4;
    public string CorrectionOtherComputerText => Strings.Session_CorrectionOtherComputer;
    public string CorrectionMissingFilesText => Strings.Session_CorrectionMissingFiles;
    public string CorrectionRepairLabel => Strings.Session_CorrectionRepair;
    public string CorrectionImportLabel => Strings.Session_CorrectionImport;
    public string CorrectionLastImportText => Strings.Session_CorrectionLastImportFailed;
    public string CorrectionOtherOptionsLabel => Strings.Session_CorrectionOtherOptions;

    public string CorrectionReferenceText => CorrectionHandoff is { } h
        ? Format(Strings.Session_CorrectionReference, h.ReferenceFileName) : string.Empty;

    public string CorrectionWorkingText => CorrectionHandoff is { } h
        ? Format(Strings.Session_CorrectionWorking, h.WorkingFileName) : string.Empty;

    public string CorrectionFolderText => CorrectionHandoff is { } h
        ? Format(Strings.Session_CorrectionFolder, h.FolderPath) : string.Empty;

    /// <summary>The folder path alone, selectable for copying.</summary>
    public string CorrectionFolderPath => CorrectionHandoff?.FolderPath ?? string.Empty;

    public string CorrectionStep2Text => CorrectionHandoff is { } h
        ? Format(Strings.Session_CorrectionStep2, string.IsNullOrWhiteSpace(h.Note) ? Strings.Session_CorrectionNoNote : h.Note)
        : string.Empty;

    public string CorrectionStep3Text => CorrectionHandoff is { } h
        ? Format(Strings.Session_CorrectionStep3, h.RequiredPixelWidth, h.RequiredPixelHeight, h.SuggestedReturnName)
        : string.Empty;

    public string CorrectionObsoleteText => CorrectionHandoff is { IsObsolete: true } h
        ? Format(Strings.Session_CorrectionObsolete, h.FolderPath) : string.Empty;

    /// <summary>What the panel is doing, in words; never only a colour or a spinner.</summary>
    public string CorrectionActivityText => CorrectionActivity switch
    {
        CorrectionActivity.Preparing => Strings.Session_CorrectionPreparing,
        CorrectionActivity.Checking => Strings.Session_CorrectionChecking,
        CorrectionActivity.Repairing => Strings.Session_CorrectionPreparing,
        _ => string.Empty,
    };

    /// <summary>Shown while the imported correction is under review and identical to what was sent.</summary>
    public bool IsCorrectionIdenticalToSent => _session?.CorrectionReturn is { IdenticalToSent: true } && IsReviewRequired;

    public string CorrectionIdenticalText => Strings.Session_CorrectionIdentical;

    // --- Commands --------------------------------------------------------------------------

    /// <summary>Opens the Ask panel (S1). Sends nothing.</summary>
    [RelayCommand]
    private void BeginAskColleague()
    {
        if (!CanAskColleague || _session?.CurrentArtefact is not { } shown)
        {
            return;
        }

        string target = $"{_session.Id}|{shown.RevisionId}|{shown.Sha256.Value}";
        if (_pendingCorrectionTarget != target)
        {
            // A fresh request for this review; a retry after a failure keeps the same identity.
            _pendingCorrectionTarget = target;
            _pendingCorrectionRequestId = Guid.NewGuid();
            CorrectionNote = null;
        }

        CorrectionMessage = null;
        IsAskingColleague = true;
        RequestCorrectionFocus(CorrectionFocus.NoteBox);
    }

    /// <summary>Closes the Ask panel. Writes nothing and re-enables the review actions.</summary>
    [RelayCommand]
    private void CancelAskColleague()
    {
        if (!IsAskingColleague || IsBusy)
        {
            return;
        }

        IsAskingColleague = false;
        CorrectionMessage = null;
        RequestCorrectionFocus(CorrectionFocus.AskButton);
    }

    /// <summary>
    /// Prepares and verifies the two copies, then hands the job off (S2). Reported as handed off
    /// only from the view the service returns after its commit; an unknown outcome is settled by
    /// an authoritative reload, never guessed.
    /// </summary>
    [RelayCommand]
    private async Task PrepareCorrectionFilesAsync(CancellationToken cancellationToken)
    {
        if (!CanPrepareCorrection || _session is not { } session ||
            session.CurrentArtefact is not { IsCurrentStepResult: true } shown ||
            _pendingCorrectionRequestId is not { } requestId)
        {
            return;
        }

        SessionId id = session.Id;
        CorrectionInteraction interaction = BeginCorrectionInteraction(session);
        string? note = string.IsNullOrWhiteSpace(CorrectionNote) ? null : CorrectionNote.Trim();
        CorrectionFileNaming naming = CorrectionNaming(note, shown.Facts.PixelWidth ?? 0, shown.Facts.PixelHeight ?? 0);

        interaction.OwnsBusy = true;
        IsBusy = true;
        CorrectionActivity = CorrectionActivity.Preparing;
        CorrectionMessage = null;
        Notice = null;
        try
        {
            OperationResult<SessionView> result = await _sessions.RequestColleagueCorrectionAsync(
                id, requestId, shown.RevisionId, shown.Sha256, note, naming, Environment.UserName, cancellationToken)
                .ConfigureAwait(true);
            if (!IsCurrentCorrectionInteraction(interaction))
            {
                return;
            }

            if (result.IsFailure)
            {
                await ReloadCorrectionAsync(interaction, id, cancellationToken).ConfigureAwait(true);
                if (!IsCurrentCorrectionInteraction(interaction)) return;
                if (_session?.Correction is { CanImport: true } landed && landed.RequestId == requestId)
                {
                    // The commit landed although the answer said otherwise: the database decides.
                    CompleteAsk();
                    return;
                }

                if (IsAskingColleague && _pendingCorrectionRequestId == requestId)
                    SetCorrectionFailure(result.Failure, nameof(Strings.Session_CorrectionPrepareFailed), requestId);
                return;
            }

            ShowCorrectionResult(interaction, result.Value);
            await PreviewsLoaded.ConfigureAwait(true);
            if (IsCurrentCorrectionInteraction(interaction) &&
                _session?.Correction is { CanImport: true } prepared && prepared.RequestId == requestId)
            {
                CompleteAsk();
            }
        }
        finally
        {
            EndCorrectionInteraction(interaction);
        }
    }

    /// <summary>
    /// Chooses the corrected picture, then asks the service to check and import it (S4, S5). The
    /// picker is open with nothing held; a cancelled picker writes nothing.
    /// </summary>
    [RelayCommand]
    private async Task ImportCorrectedImageAsync(CancellationToken cancellationToken)
    {
        if (!CanImportCorrectedImage || _session is not { } session || CorrectionHandoff is not { } handoff)
        {
            return;
        }

        SessionId id = session.Id;
        Guid requestId = handoff.RequestId;
        bool review = handoff.Mode == CorrectionImportMode.Review;
        CorrectionInteraction interaction = BeginCorrectionInteraction(session);
        try
        {
            string? selected = _filePicker?.PickSingleFile(
                Strings.Session_CorrectionPickerTitle, Strings.Session_ManualCutoutFilter, handoff.FolderPath);
            // A modal picker can dispatch navigation/refresh before returning its selection.
            if (!IsCurrentCorrectionInteraction(interaction) || !CanImportCorrectedImage) return;
            RequestCorrectionFocus(CorrectionFocus.ImportButton);
            if (string.IsNullOrWhiteSpace(selected)) return;

            interaction.OwnsBusy = true;
            IsBusy = true;
            CorrectionActivity = CorrectionActivity.Checking;
            CorrectionMessage = null;
            Notice = null;
            OperationResult<SessionView> result = await _sessions.ImportCorrectedImageAsync(
                id, requestId,
                review ? handoff.HandedOutRevisionId : null,
                review ? handoff.HandedOutSha256 : null,
                selected, Environment.UserName, cancellationToken).ConfigureAwait(true);
            if (!IsCurrentCorrectionInteraction(interaction))
            {
                return;
            }

            if (result.IsFailure)
            {
                await ReloadCorrectionAsync(interaction, id, cancellationToken).ConfigureAwait(true);
                await PreviewsLoaded.ConfigureAwait(true);
                if (!IsCurrentCorrectionInteraction(interaction)) return;
                if (_session?.Correction is { CanImport: true } still && still.RequestId == requestId)
                {
                    SetCorrectionFailure(result.Failure, nameof(Strings.Session_CorrectionRefused), requestId);
                    RequestCorrectionFocus(CorrectionFocus.ImportButton);
                }

                return;
            }

            // The review of R2 takes focus through the ordinary new-review rule.
            ShowCorrectionResult(interaction, result.Value);
            await PreviewsLoaded.ConfigureAwait(true);
        }
        finally
        {
            EndCorrectionInteraction(interaction);
        }
    }

    /// <summary>Recreates only the missing files; a colleague's edited file is never touched.</summary>
    [RelayCommand]
    private async Task RepairCorrectionFilesAsync(CancellationToken cancellationToken)
    {
        if (!CanRepairCorrectionFiles || _session is not { } session || CorrectionHandoff is not { } handoff)
        {
            return;
        }

        Guid requestId = handoff.RequestId;
        CorrectionInteraction interaction = BeginCorrectionInteraction(session);
        CorrectionFileNaming naming = CorrectionNaming(handoff.Note, handoff.RequiredPixelWidth, handoff.RequiredPixelHeight);
        interaction.OwnsBusy = true;
        IsBusy = true;
        CorrectionActivity = CorrectionActivity.Repairing;
        Notice = null;
        try
        {
            OperationResult<SessionView> result = await _sessions.RepairCorrectionFilesAsync(
                session.Id, requestId, naming, cancellationToken).ConfigureAwait(true);
            if (!IsCurrentCorrectionInteraction(interaction)) return;
            if (result.IsFailure)
            {
                bool reloaded = await ReloadCorrectionAsync(interaction, session.Id, cancellationToken).ConfigureAwait(true);
                if (!IsCurrentCorrectionInteraction(interaction)) return;
                if (_session?.Correction is { CanImport: true } still && still.RequestId == requestId)
                    ShowActionFailure(result.Failure, reloaded ? ActionFailureScreen.Current : ActionFailureScreen.Stale);
            }
            else
            {
                ShowCorrectionResult(interaction, result.Value);
            }

            await PreviewsLoaded.ConfigureAwait(true);
        }
        finally
        {
            EndCorrectionInteraction(interaction);
        }
    }

    /// <summary>Opens the correction folder. Read-only: no request, import, review or run.</summary>
    [RelayCommand]
    private void OpenCorrectionFolder()
    {
        if (!CanOpenCorrectionFolder || CorrectionHandoff is not { } handoff)
        {
            return;
        }

        if (!_correctionShell!.Open(handoff.FolderPath, handoff.WorkingFileName, handoff.ReferenceFileName).Dispatched)
        {
            Notice = Strings.Session_CorrectionOpenFolderFailed;
        }
    }

    // --- Plumbing --------------------------------------------------------------------------

    private static string CorrectionTarget(SessionView session) =>
        $"{session.Id}|{session.State}|{session.CurrentStep?.Step}|{session.CurrentStep?.State}|" +
        $"{session.CurrentArtefact?.RevisionId}|{session.CurrentArtefact?.Sha256.Value}|{session.CurrentArtefact?.IsCurrentStepResult}|" +
        $"{session.Correction?.RequestId}|{session.Correction?.HandedOutRevisionId}|{session.Correction?.HandedOutSha256.Value}|{session.Correction?.Mode}";

    private CorrectionInteraction BeginCorrectionInteraction(SessionView session)
    {
        CorrectionInteraction interaction = new(CorrectionTarget(session));
        _correctionInteraction = interaction;
        return interaction;
    }

    private bool IsCurrentCorrectionInteraction(CorrectionInteraction interaction) =>
        ReferenceEquals(_correctionInteraction, interaction);

    private void EndCorrectionInteraction(CorrectionInteraction interaction)
    {
        if (!IsCurrentCorrectionInteraction(interaction)) return;
        _correctionInteraction = null;
        if (interaction.OwnsBusy)
        {
            CorrectionActivity = CorrectionActivity.Idle;
            IsBusy = false;
        }
    }

    private void ShowCorrectionResult(CorrectionInteraction interaction, SessionView session)
    {
        // This operation may move its own target forward. External Show calls cannot do so.
        interaction.Target = CorrectionTarget(session);
        Show(session);
    }

    /// <returns>Whether the job was read again; a failure-notice line depends on it (SCRUM-11151).</returns>
    private async Task<bool> ReloadCorrectionAsync(CorrectionInteraction interaction, SessionId id, CancellationToken cancellationToken)
    {
        // Unknown outcomes still require the database, but a delayed reload has no authority
        // over a target shown after it began, just like a delayed command response.
        OperationResult<SessionView> reloaded = await _sessions.LoadAsync(id, cancellationToken).ConfigureAwait(true);
        if (IsCurrentCorrectionInteraction(interaction) && reloaded.IsSuccess)
            ShowCorrectionResult(interaction, reloaded.Value);
        return reloaded.IsSuccess;
    }

    /// <summary>
    /// The operator-language names and the bilingual instructions for a package. File names are
    /// completed by the service, which substitutes <c>{reference}</c>, <c>{working}</c> and
    /// <c>{return}</c> in the instructions with the names it actually persisted.
    /// </summary>
    private static CorrectionFileNaming CorrectionNaming(string? note, int width, int height)
    {
        string Instructions(CultureInfo culture)
        {
            string In(string key) => Strings.InCulture(key, culture);
            string noteText = string.IsNullOrWhiteSpace(note) ? In(nameof(Strings.Session_CorrectionNoNote)) : note!;
            return string.Join(Environment.NewLine,
                In(nameof(Strings.Session_CorrectionInstructionsTitle)),
                string.Empty,
                In(nameof(Strings.Session_CorrectionStep1)),
                "  " + string.Format(culture, In(nameof(Strings.Session_CorrectionReference)), "{reference}"),
                "  " + string.Format(culture, In(nameof(Strings.Session_CorrectionWorking)), "{working}"),
                string.Format(culture, In(nameof(Strings.Session_CorrectionStep2)), noteText),
                string.Format(culture, In(nameof(Strings.Session_CorrectionStep3)), width, height, "{return}"),
                In(nameof(Strings.Session_CorrectionStep4)),
                In(nameof(Strings.Session_CorrectionOtherComputer)));
        }

        string instructions = Instructions(CultureInfo.GetCultureInfo("en")) + Environment.NewLine + Environment.NewLine +
            "————" + Environment.NewLine + Environment.NewLine + Instructions(CultureInfo.GetCultureInfo("zh-CN")) +
            Environment.NewLine;

        return new CorrectionFileNaming(
            Strings.Session_CorrectionFileReference,
            Strings.Session_CorrectionFileWorking,
            Strings.Session_CorrectionFileReturn,
            instructions);
    }

    private void CompleteAsk()
    {
        IsAskingColleague = false;
        _pendingCorrectionRequestId = null;
        _pendingCorrectionTarget = null;
        CorrectionNote = null;
        CorrectionMessage = null;
        RequestCorrectionFocus(CorrectionFocus.HandedOffHeading);
    }

    private void SetCorrectionFailure(OperationFailure failure, string wrapperKey, Guid requestId)
    {
        _correctionMessageRequest = requestId;
        _correctionFailureWrapperKey = wrapperKey;
        CorrectionMessage = _correctionFailure.Describe(
            Format(Strings.Resolve(wrapperKey), DisplayNames.FailureNotice(failure)), failure);
        OnPropertyChanged(nameof(CorrectionErrorCode));
    }

    private void RefreshCorrectionFailureLanguage()
    {
        if (_correctionFailure.Failure is { } failure && _correctionFailureWrapperKey is { } key &&
            _correctionMessageRequest is { } requestId)
            SetCorrectionFailure(failure, key, requestId);
    }

    private void RequestCorrectionFocus(CorrectionFocus target)
    {
        CorrectionFocusTarget = target;
        CorrectionFocusToken++;
    }

    /// <summary>
    /// Keeps the Ask panel only while the same review is on screen, and a message only while it is
    /// about the request on screen. Called from <see cref="Show"/>.
    /// </summary>
    private void ShowCorrection(SessionView session)
    {
        if (_correctionInteraction is { } interaction && interaction.Target != CorrectionTarget(session))
            EndCorrectionInteraction(interaction);

        bool sameReview = session is
        {
            State: SessionState.Active,
            CurrentStep: { Step: StepKind.BackgroundRemoval, State: StepState.ReviewRequired },
            CurrentArtefact: { IsCurrentStepResult: true } shown,
        } && _pendingCorrectionTarget == $"{session.Id}|{shown.RevisionId}|{shown.Sha256.Value}" && session.CanAskColleague;
        if (IsAskingColleague && !sameReview)
        {
            IsAskingColleague = false;
        }

        if (CorrectionMessage is not null && !IsAskingColleague &&
            session.Correction?.RequestId != _correctionMessageRequest)
        {
            CorrectionMessage = null;
        }

        NotifyCorrectionState();
    }

    partial void OnIsAskingColleagueChanged(bool value)
    {
        NotifyCorrectionState();
        OnPropertyChanged(nameof(CanConfirmOriginal));
        OnPropertyChanged(nameof(CanRunStep));
        OnPropertyChanged(nameof(CanApprove));
        OnPropertyChanged(nameof(CanReject));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanSkip));
        OnPropertyChanged(nameof(CanHandOff));
        OnPropertyChanged(nameof(CanKeepOriginalExtent));
        KeepOriginalExtentCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanReturnToStep));
        OnPropertyChanged(nameof(CanBeginReturn));
        NotifyOperatorStatusChanged();
    }

    partial void OnCorrectionActivityChanged(CorrectionActivity value)
    {
        OnPropertyChanged(nameof(IsCorrectionBusy));
        OnPropertyChanged(nameof(CorrectionActivityText));
        NotifyOperatorStatusChanged();
    }

    partial void OnCorrectionMessageChanged(string? value)
    {
        _correctionFailure.Track(value);
        OnPropertyChanged(nameof(CorrectionErrorCode));
        OnPropertyChanged(nameof(HasCorrectionMessage));
        OnPropertyChanged(nameof(CorrectionPrepareLabel));
    }

    private void NotifyCorrectionState()
    {
        OnPropertyChanged(nameof(CorrectionHandoff));
        OnPropertyChanged(nameof(CanAskColleague));
        OnPropertyChanged(nameof(CanPrepareCorrection));
        OnPropertyChanged(nameof(CanCancelAskColleague));
        OnPropertyChanged(nameof(ShowsCorrectionPanel));
        OnPropertyChanged(nameof(ShowsCorrectionObsolete));
        OnPropertyChanged(nameof(CanImportCorrectedImage));
        OnPropertyChanged(nameof(CanOpenCorrectionFolder));
        OnPropertyChanged(nameof(CanRepairCorrectionFiles));
        OnPropertyChanged(nameof(ShowsCorrectionMissingFiles));
        OnPropertyChanged(nameof(ShowsLastImportUnfinished));
        OnPropertyChanged(nameof(ShowsReenterAutomationBlock));
        OnPropertyChanged(nameof(ShowsReenterAutomationInCorrection));
        OnPropertyChanged(nameof(AskIdentity));
        OnPropertyChanged(nameof(ImportIdentity));
        OnPropertyChanged(nameof(CorrectionPrepareLabel));
        OnPropertyChanged(nameof(CorrectionReferenceText));
        OnPropertyChanged(nameof(CorrectionWorkingText));
        OnPropertyChanged(nameof(CorrectionFolderText));
        OnPropertyChanged(nameof(CorrectionFolderPath));
        OnPropertyChanged(nameof(CorrectionStep2Text));
        OnPropertyChanged(nameof(CorrectionStep3Text));
        OnPropertyChanged(nameof(CorrectionObsoleteText));
        OnPropertyChanged(nameof(IsCorrectionIdenticalToSent));
        OnPropertyChanged(nameof(IsRejectGestureGuarded));
        OnPropertyChanged(nameof(RejectTargetIdentity));
    }

    /// <summary>Background-removal Reject uses the fresh-gesture guard; other steps are unchanged.</summary>
    public bool IsRejectGestureGuarded => _session?.CurrentStep?.Step == StepKind.BackgroundRemoval;

    /// <summary>Input identity for background-removal Reject, in S0, S3 (Review) and R2's review.</summary>
    public string? RejectTargetIdentity => _session is
    {
        CurrentStep: { Step: StepKind.BackgroundRemoval, State: StepState.ReviewRequired } step,
        CurrentArtefact: { IsCurrentStepResult: true } shown,
    } session
        ? $"reject|{session.Id}|{step.Step}|{shown.RevisionId}|{shown.Sha256.Value}"
        : null;
}
