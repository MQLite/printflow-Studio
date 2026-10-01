using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Navigation;
using PrintFlow.App.Localisation;
using PrintFlow.App.Resources;
using PrintFlow.App.Startup;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels;

/// <summary>
/// The operator's starting point: what startup concluded, one way in, and the recent work
/// (Epic 11100 Part 3C2 §3).
/// </summary>
/// <remarks>
/// Everything this view model does goes through <see cref="ISessionService"/>. It opens no
/// database, copies no file, computes no hash, creates no directory and constructs no domain
/// record — the closest it comes to the file system is passing the path a dialog or a drop
/// handed it straight to <see cref="ISessionService.ImportAsync"/> (plan §17.4, Part 3C2 §5).
/// </remarks>
public sealed partial class HomeViewModel : ObservableObject
{
    private readonly ISessionService _sessions;
    private readonly IArtefactPreviewService _previews;
    private readonly INavigationService _navigation;
    private readonly IFilePicker _filePicker;
    private readonly StartupStatusAccessor _startupStatus;

    /// <summary>
    /// Cancels the thumbnail load belonging to the list that is being replaced.
    /// </summary>
    /// <remarks>
    /// The whole of this screen's asynchronous bookkeeping, deliberately. Each refresh builds a
    /// new set of row objects and a new token; the previous load is cancelled and, because the
    /// rows it was filling are no longer in <see cref="RecentSessions"/>, anything it still
    /// writes reaches an object nobody is looking at. That is why there is no scheduler and no
    /// per-row state machine here — a stale result cannot land on the wrong row when no two
    /// loads ever share a row (Jira 11602).
    /// </remarks>
    private CancellationTokenSource? _thumbnails;

    // A slow earlier refresh must never replace the rows from a later visit or Refresh action.
    private int _refreshGeneration;

    /// <summary>
    /// The workflow a session is imported under before the operator chooses.
    /// </summary>
    /// <remarks>
    /// Import must record <i>some</i> workflow, because a session's steps are its workflow's
    /// steps. Choosing here and confirming on the next screen is the engine's own supported
    /// path: <c>SelectWorkflow</c> re-shapes the session onto the chosen definition and carries
    /// the completed import across, and it stays legal until a derived Revision exists
    /// (MVP design §6.1). The alternative — a half-created session with no workflow — would
    /// need a state the domain deliberately does not have.
    /// </remarks>
    private const WorkflowType ProvisionalWorkflow = WorkflowType.PrepareAsset;

    /// <summary>
    /// The reason recorded when a session is abandoned from Home.
    /// </summary>
    /// <remarks>
    /// Stable English, not a resource, because it is persisted to <c>AbandonReason</c> and read
    /// back as audit history — the same choice <see cref="WorkflowCommand.Skip.DefaultReason"/>
    /// makes. A record whose text changes with the workstation's language would be a poor
    /// audit trail (MVP design §13.4).
    /// </remarks>
    private const string AbandonedFromHomeReason = "Abandoned by the operator from the Home screen.";

    [ObservableProperty]
    private string? _notice;

    [ObservableProperty]
    private bool _isBusy;

    public HomeViewModel(
        ISessionService sessions,
        IArtefactPreviewService previews,
        INavigationService navigation,
        IFilePicker filePicker,
        StartupStatusAccessor startupStatus,
        ReadinessObservationAccessor readiness,
        ILocalisationService? localisation = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(previews);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(filePicker);
        ArgumentNullException.ThrowIfNull(startupStatus);
        ArgumentNullException.ThrowIfNull(readiness);

        _sessions = sessions;
        _previews = previews;
        _navigation = navigation;
        _filePicker = filePicker;
        _startupStatus = startupStatus;
        _readinessObservations = readiness;
        Readiness = new HomeReadinessSummary(readiness.Current);
        if (localisation is not null)
            System.Windows.WeakEventManager<ILocalisationService, EventArgs>.AddHandler(
                localisation, nameof(ILocalisationService.LanguageChanged), OnLanguageChanged);
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        foreach (RecentSessionRow row in RecentSessions) row.RefreshLanguage();
        foreach (RecoverySessionRow row in RecoverySessions) row.RefreshLanguage();
        OnPropertyChanged(string.Empty);
    }

    // --- SCRUM-11152: the readiness summary and the startup details behind it ------------------

    private readonly ReadinessObservationAccessor _readinessObservations;

    /// <summary>
    /// What the latest readiness observation of this run found, as Home's one summary.
    /// </summary>
    /// <remarks>
    /// A snapshot of <see cref="ReadinessObservationAccessor"/> updated while the view is loaded
    /// and on ordinary refresh. Taking it reads memory only: Home never asks the
    /// diagnostics seam, never starts a check and gates no command on the answer.
    /// </remarks>
    [ObservableProperty]
    private HomeReadinessSummary _readiness;

    /// <summary>The collapsed heading over the startup facts that need no action (SCRUM-11152).</summary>
    public string StartupDetailsLabel => Strings.Resolve("Home_StartupDetails");

    /// <summary>
    /// What startup recovery counted, always in full: interrupted attempts, released locks and
    /// quarantined files, or that it found nothing or did not run.
    /// </summary>
    public string RecoveryCountsText
    {
        get
        {
            StartupStatus? status = _startupStatus.Status;
            if (status is null || !status.RecoveryExecuted) return Strings.Startup_RecoveryNotRun;
            if (status.RecoveryReport is { IsNoOp: true }) return Strings.Startup_RecoveryClean;
            return string.Format(CultureInfo.CurrentCulture, Strings.Startup_RecoverySummary,
                status.RecoveredAttemptCount, status.ReleasedStaleLockCount, status.QuarantinedFileCount);
        }
    }

    /// <summary>How many listed jobs wait for a recovery decision; empty when none do.</summary>
    public string RecoveryPendingText => HasRecoverySessions
        ? string.Format(CultureInfo.CurrentCulture, Strings.Home_RecoveryPending, RecoverySessions.Count)
        : string.Empty;

    /// <summary>Startup's own preset check, worded as the production setup file and nothing more.</summary>
    public string PresetDetailText => Strings.Resolve(_startupStatus.Status?.PresetVerified == true
        ? "Home_StartupPresetVerified" : "Home_StartupPresetNotVerified");

    /// <summary>Kept visible, not folded into details: startup could not accept the preset.</summary>
    public bool HasPresetWarning => _startupStatus.Status?.PresetVerified != true;

    /// <summary>Kept visible, not folded into details: startup recovery did not run.</summary>
    public bool HasRecoveryNotRunWarning => _startupStatus.Status is not { RecoveryExecuted: true };

    public string RecoveryNotRunText => Strings.Startup_RecoveryNotRun;

    /// <summary>Kept visible, not folded into details: local diagnostic cleanup did not finish.</summary>
    public bool HasRetentionWarning => _startupStatus.Status?.DiagnosticRetentionReport?.Warning is not null;

    public string RetentionWarningText => Strings.Resolve("Home_DiagnosticRetentionWarning");

    /// <summary>Takes a fresh snapshot of what has already been observed. Observes nothing itself.</summary>
    internal void ReadReadiness() => Readiness = new HomeReadinessSummary(_readinessObservations.Current);

    internal ReadinessObservationAccessor ReadinessObservations => _readinessObservations;

    /// <summary>Recent Processing, newest first, exactly as the service returned it.</summary>
    public ObservableCollection<RecentSessionRow> RecentSessions { get; } = [];

    public ObservableCollection<RecoverySessionRow> RecoverySessions { get; } = [];
    public bool HasRecoverySessions => RecoverySessions.Count > 0;
    public string RecoveryHeading => Strings.Home_RecoveryHeading;

    public string Title => Strings.App_Title;

    public string ImportHeading => Strings.Home_ImportHeading;

    public string ImportHint => Strings.Home_ImportHint;

    public string ChooseFileLabel => Strings.Home_ChooseFile;

    public string RecentHeading => Strings.Home_RecentHeading;

    public string RefreshLabel => Strings.Home_Refresh;

    public string AbandonLabel => Strings.Home_Abandon;

    /// <summary>The label for taking a finished job's record off the list (Jira 11602).</summary>
    /// <remarks>
    /// "Remove from list" rather than "Delete", because that is exactly what it does and no
    /// more: the imported file, every approved output and the whole processing history stay
    /// where they are. Labelling a durable dismissal "Delete" would invite an operator to
    /// believe they had cleaned up production files, which is the one thing this must never do.
    /// </remarks>
    public string RemoveLabel => Strings.Home_RemoveRecord;

    public string EmptyRecentText => Strings.Home_NoRecentSessions;

    /// <summary>The way to the Production Readiness screen (Epic 11500 Part C §3).</summary>
    public string EnvironmentLabel => Strings.Resolve("Home_ViewWorkstationChecks");

    /// <summary>The way to Settings (SCRUM-11118).</summary>
    public string SettingsLabel => Strings.Settings_Open;

    /// <summary>True while the list is empty, so the view can say so rather than show nothing.</summary>
    public bool HasNoRecentSessions => RecentSessions.Count == 0 && !HasRecoverySessions;

    /// <summary>
    /// One line describing what startup recovery did, so a restart after a crash says so
    /// visibly rather than only in a report object (Part 3C1 §6, Part 3C2 §12).
    /// </summary>
    /// <remarks>
    /// The screen itself now splits this line (SCRUM-11152): the counts sit under
    /// <see cref="StartupDetailsLabel"/> as <see cref="RecoveryCountsText"/>, and the warnings stay
    /// visible on their own. The combined form is unchanged for the startup checks that read it.
    /// </remarks>
    public string StartupSummary
        => _startupStatus.Status?.DiagnosticRetentionReport?.Warning is not null
            ? RecoverySummary + " " + Strings.Startup_DiagnosticRetentionWarning
            : RecoverySummary;

    private string RecoverySummary
    {
        get
        {
            if (HasRecoverySessions) return string.Format(CultureInfo.CurrentCulture, Strings.Home_RecoveryPending, RecoverySessions.Count);
            StartupStatus? status = _startupStatus.Status;
            if (status is null || !status.RecoveryExecuted)
            {
                return Strings.Startup_RecoveryNotRun;
            }

            if (status.RecoveryReport is { IsNoOp: true })
            {
                return Strings.Startup_RecoveryClean;
            }

            return string.Format(
                CultureInfo.CurrentCulture,
                Strings.Startup_RecoverySummary,
                status.RecoveredAttemptCount,
                status.ReleasedStaleLockCount,
                status.QuarantinedFileCount);
        }
    }

    /// <summary>
    /// Opens Production Readiness (Epic 11500 Part C §3).
    /// </summary>
    /// <remarks>
    /// Navigation and nothing else. Home shows what was last observed but decides nothing from
    /// it and never consults the diagnostics seam — the screen it opens does, and opening it
    /// starts no live check (SCRUM-11152).
    /// </remarks>
    [RelayCommand]
    private async Task ShowEnvironmentAsync(CancellationToken cancellationToken) =>
        await _navigation.GoToEnvironmentReadinessAsync(cancellationToken).ConfigureAwait(true);

    /// <summary>
    /// Opens Settings (SCRUM-11118).
    /// </summary>
    /// <remarks>
    /// Navigation and nothing else, exactly as the readiness link above. Home reads no setting
    /// and decides nothing from one; the screen it opens is the only place a preference is read
    /// or written.
    /// </remarks>
    [RelayCommand]
    private async Task ShowSettingsAsync(CancellationToken cancellationToken) =>
        await _navigation.GoToSettingsAsync(cancellationToken).ConfigureAwait(true);

    /// <summary>Reloads Recent Processing from persistence.</summary>
    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        int generation = ++_refreshGeneration;
        // The list is about to be rebuilt from current facts: a confirmation opened against the
        // old rows no longer describes them (SCRUM-11154 F-V4).
        PendingAbandon = null;
        ReadReadiness();
        var recovery = await _sessions.ListRecoveryAsync(cancellationToken).ConfigureAwait(true);
        if (generation != _refreshGeneration) return;
        if (recovery.IsFailure)
        {
            Notice = Describe(Strings.Home_RecoveryUnavailable, recovery.Failure);
            return;
        }
        RecoverySessions.Clear();
        foreach (RecoveryItem item in recovery.Value) RecoverySessions.Add(new RecoverySessionRow(item));
        OnPropertyChanged(nameof(HasRecoverySessions));
        OnPropertyChanged(nameof(StartupSummary));
        OnPropertyChanged(nameof(RecoveryPendingText));
        OnPropertyChanged(nameof(HasNoRecentSessions));

        OperationResult<IReadOnlyList<SessionListItem>> listed =
            await _sessions.ListRecentAsync(cancellationToken).ConfigureAwait(true);
        if (generation != _refreshGeneration) return;

        RecentSessions.Clear();

        if (listed.IsFailure)
        {
            Notice = Describe(Strings.Home_RecentUnavailable, listed.Failure);
            OnPropertyChanged(nameof(HasNoRecentSessions));
            return;
        }

        foreach (SessionListItem item in listed.Value)
        {
            if (!recovery.Value.Any(entry => entry.Id == item.Id)) RecentSessions.Add(new RecentSessionRow(item));
        }

        OnPropertyChanged(nameof(HasNoRecentSessions));

        ThumbnailsLoaded = StartThumbnailLoad([.. RecentSessions]);
    }

    /// <summary>
    /// The thumbnail load belonging to the list currently on screen.
    /// </summary>
    /// <remarks>
    /// Exposed so a test can wait for the pictures rather than poll for them. Nothing in the
    /// application awaits it: the list is usable the moment it is built, and the pictures arrive
    /// into rows that are already on screen (Jira 11602).
    /// </remarks>
    public Task ThumbnailsLoaded { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Fills the rows' pictures, one at a time, off the UI thread.
    /// </summary>
    /// <remarks>
    /// One at a time is the whole of the performance design. Home may list up to a hundred jobs,
    /// and asking for a hundred decodes at once — synchronously or not — is the decode storm §G
    /// forbids; a sequential walk gives a bounded degree of one without a queue, a semaphore or a
    /// scheduler to get wrong. Each decode itself is bounded twice over: the preview seam reduces
    /// to <c>IImagePreviewDecoder.ThumbnailEdge</c>, and the decode runs on a worker thread, so
    /// nothing large is ever built on the dispatcher.
    /// <para>
    /// Every outcome is non-fatal. A row whose artefact is missing, unreadable, or not yet
    /// prepared keeps its neutral no-picture state and the walk continues to the next row; a
    /// cancelled load — the operator refreshed, or left Home while a database the test host owns
    /// went away — stops silently. A picture is not information the operator needs in order to
    /// act, so nothing here may become a notice, a failure, or a reason for the screen to stop
    /// working (Epic 11200 Part C1 §21).
    /// </para>
    /// </remarks>
    private async Task StartThumbnailLoad(IReadOnlyList<RecentSessionRow> rows)
    {
        CancellationTokenSource? previous = _thumbnails;
        CancellationTokenSource current = new();
        _thumbnails = current;

        if (previous is not null)
        {
            await previous.CancelAsync().ConfigureAwait(true);
            previous.Dispose();
        }

        if (rows.Count == 0) return;

        try
        {
            foreach (RecentSessionRow row in rows)
            {
                if (current.IsCancellationRequested) return;

                OperationResult<ImagePreview> thumbnail =
                    await _previews.GetRecentThumbnailAsync(row.Id, current.Token).ConfigureAwait(true);

                if (current.IsCancellationRequested) return;
                if (thumbnail.IsSuccess) row.Thumbnail = thumbnail.Value.Payload;
            }
        }
        catch (OperationCanceledException)
        {
            // The list this load belonged to was replaced. Nothing to report and nothing to undo:
            // a thumbnail load creates no record and holds no resource beyond the bytes it drops.
        }
        catch (Exception)
        {
            // Deliberately everything, and deliberately silent — the same rule
            // PreviewPayloadConverter follows for the same reason: a picture that cannot be
            // produced must never take a screen down (Epic 11200 Part C1 §21). The realistic
            // case is a database or workspace torn down while a read was in flight, which is
            // what leaving Home looks like from inside this loop. Nothing here has produced a
            // record, held a lock or changed a file, so there is nothing to undo and nothing an
            // operator could do with the news.
        }
    }

    [RelayCommand]
    private Task RestartRecoveryAsync(RecoverySessionRow? row, CancellationToken cancellationToken) =>
        RecoverAsync(row, RecoveryAction.Restart, cancellationToken);

    [RelayCommand]
    private Task ImportRecoveryAsync(RecoverySessionRow? row, CancellationToken cancellationToken) =>
        RecoverAsync(row, RecoveryAction.ManualResult, cancellationToken);

    /// <summary>Opens the Abandon confirmation for a recovery card. Changes nothing (SCRUM-11154 F-V4).</summary>
    [RelayCommand]
    private Task AbandonRecoveryAsync(RecoverySessionRow? row, CancellationToken cancellationToken)
    {
        if (row is not null && row.CanAbandon && !ListActionsBlocked) PendingAbandon = new AbandonConfirmation(row.Id, row.DisplayName, FromRecovery: true);
        return Task.CompletedTask;
    }

    // --- SCRUM-11154 F-V4: an explicit confirmation before Abandon ----------------------------

    /// <summary>
    /// The one Abandon waiting for the operator's explicit confirmation, or null.
    /// </summary>
    /// <remarks>
    /// It captures the job's identity and name when the confirmation opens; nothing later reads a
    /// row index or the current selection. Any list refresh withdraws it, because the job it
    /// describes may have changed.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfirmingAbandon))]
    [NotifyPropertyChangedFor(nameof(AreListsInteractive))]
    private AbandonConfirmation? _pendingAbandon;

    public bool IsConfirmingAbandon => PendingAbandon is not null;

    /// <summary>
    /// False while a confirmation is open. Opening it moves the lists down, so the second click of
    /// the initiating double-click could otherwise land on another job's action; the lists stay
    /// inert until the operator keeps or confirms (SCRUM-11154 F-V4).
    /// </summary>
    public bool AreListsInteractive => PendingAbandon is null;

    /// <summary>Busy, or waiting for the operator's answer to an open Abandon confirmation.</summary>
    private bool ListActionsBlocked => IsBusy || PendingAbandon is not null;

    public string AbandonKeepLabel => Strings.Resolve("Home_AbandonKeep");

    public string AbandonConfirmLabel => Strings.Resolve("Home_AbandonConfirm");

    /// <summary>Closes the confirmation. Reaches no service and changes nothing.</summary>
    [RelayCommand]
    private void KeepJob() => PendingAbandon = null;

    /// <summary>
    /// The single Abandon, for exactly the confirmed job.
    /// </summary>
    /// <remarks>
    /// Only the confirmation currently shown counts: a stale or repeated activation for an earlier
    /// one is ignored, and the pending state is cleared before the first await so a second
    /// activation cannot submit twice. Eligibility is re-read through the existing authority —
    /// the Recent listing, or the recovery resolution's own guard — before the one command.
    /// </remarks>
    [RelayCommand]
    private async Task ConfirmAbandonAsync(AbandonConfirmation? confirmation, CancellationToken cancellationToken)
    {
        if (confirmation is null || !ReferenceEquals(confirmation, PendingAbandon) || IsBusy) return;
        PendingAbandon = null;
        IsBusy = true;
        try
        {
            Notice = null;
            if (confirmation.FromRecovery)
            {
                OperationResult<SessionView> resolved = await _sessions.ResolveRecoveryAsync(confirmation.Id, RecoveryAction.Abandon,
                    null, Environment.UserName, cancellationToken).ConfigureAwait(true);
                if (resolved.IsFailure && resolved.Failure.Code == FailureCode.PreconditionNotMet)
                    Notice = Format(Strings.Resolve("Home_AbandonStale"), confirmation.DisplayName);
                else if (resolved.IsFailure) ShowFailureNotice(Strings.Home_RecoveryFailed, resolved.Failure);
                else Notice = Format(Strings.Home_AbandonDone, confirmation.DisplayName);
            }
            else
            {
                OperationResult<IReadOnlyList<SessionListItem>> current =
                    await _sessions.ListRecentAsync(cancellationToken).ConfigureAwait(true);
                if (current.IsFailure) ShowFailureNotice(Strings.Home_AbandonFailed, current.Failure);
                else if (current.Value.FirstOrDefault(item => item.Id == confirmation.Id) is not { CanAbandon: true })
                    Notice = Format(Strings.Resolve("Home_AbandonStale"), confirmation.DisplayName);
                else
                {
                    OperationResult<SessionView> abandoned = await _sessions.ExecuteAsync(confirmation.Id,
                        new WorkflowCommand.AbandonSession(AbandonedFromHomeReason), Environment.UserName,
                        cancellationToken).ConfigureAwait(true);
                    if (abandoned.IsFailure) ShowFailureNotice(Strings.Home_AbandonFailed, abandoned.Failure);
                    else Notice = Format(Strings.Home_AbandonDone, confirmation.DisplayName);
                }
            }
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task OpenRecoveryAsync(RecoverySessionRow? row, CancellationToken cancellationToken)
    {
        if (row is null || ListActionsBlocked) return;
        var loaded = await _sessions.LoadAsync(row.Id, cancellationToken).ConfigureAwait(true);
        if (loaded.IsSuccess) _navigation.GoToSession(loaded.Value);
        else ShowFailureNotice(Format(Strings.Home_ResumeFailed,
            row.HasOpenCorrection ? row.OpenCorrectionLabel : row.OpenLabel), loaded.Failure);
    }

    private async Task RecoverAsync(RecoverySessionRow? row, RecoveryAction action, CancellationToken cancellationToken)
    {
        if (row is null || ListActionsBlocked) return;
        IsBusy = true;
        try
        {
            Notice = null;
            string? path = null;
            if (action == RecoveryAction.ManualResult)
            {
                path = _filePicker.PickSingleFile(Strings.Home_RecoveryManualResult, row.ManualFilter);
                if (path is null) return;
            }
            var result = await _sessions.ResolveRecoveryAsync(row.Id, action, path, Environment.UserName, cancellationToken).ConfigureAwait(true);
            await RefreshAsync(cancellationToken).ConfigureAwait(true);
            if (result.IsFailure) ShowFailureNotice(Strings.Home_RecoveryFailed, result.Failure);
            else if (action != RecoveryAction.Abandon) _navigation.GoToSession(result.Value);
        }
        finally { IsBusy = false; }
    }

    /// <summary>Asks the operator for one file and imports it.</summary>
    [RelayCommand]
    private async Task ChooseFileAsync(CancellationToken cancellationToken)
    {
        string? chosen = _filePicker.PickSingleFile(Strings.Home_ChooseFile, Strings.Home_ImportFilter);
        if (chosen is null)
        {
            return;
        }

        await ImportAsync(chosen, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Accepts a drop of exactly one file.
    /// </summary>
    /// <remarks>
    /// More than one path is refused outright rather than silently reduced to the first.
    /// Quietly processing one of several dropped files would mean the operator believes work
    /// is under way for files that no session exists for — there is no batching in the MVP,
    /// so saying so is the only honest answer (Part 3C2 §4).
    /// </remarks>
    [RelayCommand]
    private async Task DropFilesAsync(IReadOnlyList<string>? paths, CancellationToken cancellationToken)
    {
        if (paths is null || paths.Count == 0)
        {
            Notice = Strings.Home_DropNothing;
            return;
        }

        if (paths.Count > 1)
        {
            Notice = string.Format(CultureInfo.CurrentCulture, Strings.Home_DropSingleFileOnly, paths.Count);
            return;
        }

        await ImportAsync(paths[0], cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Opens a listed session from its real persisted state.</summary>
    /// <remarks>
    /// The row is only an identifier here: what gets shown is whatever
    /// <see cref="ISessionService.LoadAsync"/> reconstructs from SQLite, never the row's own
    /// display text (Part 3C2 §9).
    /// </remarks>
    [RelayCommand]
    private async Task ResumeAsync(RecentSessionRow? row, CancellationToken cancellationToken)
    {
        if (row is null || ListActionsBlocked)
        {
            return;
        }

        IsBusy = true;
        try
        {
            Notice = null;
            OperationResult<SessionView> loaded =
                await _sessions.LoadAsync(row.Id, cancellationToken).ConfigureAwait(true);

            if (loaded.IsFailure)
            {
                ShowFailureNotice(Format(Strings.Home_ResumeFailed, row.OpenActionLabel), loaded.Failure);
                return;
            }

            _navigation.GoToSession(loaded.Value);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Opens the Abandon confirmation for a listed session; <see cref="ConfirmAbandonAsync"/> then
    /// abandons it through the ordinary command path (SCRUM-11154 F-V4).
    /// </summary>
    /// <remarks>
    /// Nothing is deleted: the engine's <c>AbandonSession</c> records the decision and releases
    /// the automation lock, and the source snapshot, approved outputs and audit history stay
    /// exactly as they were (MVP design §6.6, Part 3C2 §10). A refusal is reported rather than
    /// worked around — the row's own <see cref="RecentSessionRow.CanAbandon"/> decides whether
    /// the button is offered, and the engine decides whether the command is accepted. Opening the
    /// confirmation issues no command.
    /// </remarks>
    [RelayCommand]
    private Task AbandonAsync(RecentSessionRow? row, CancellationToken cancellationToken)
    {
        if (row is not null && row.CanAbandon && !ListActionsBlocked) PendingAbandon = new AbandonConfirmation(row.Id, row.DisplayName, FromRecovery: false);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Takes one finished job's record off Recent Processing, then rebuilds the list
    /// (Jira 11602).
    /// </summary>
    /// <remarks>
    /// Record management, not cleanup, and the wording says so: nothing is deleted. The imported
    /// file, every approved output and the whole processing history stay exactly where they are;
    /// what ends is the entry's place on this screen, and it stays ended after a restart because
    /// the service persisted it.
    /// <para>
    /// The row's <see cref="RecentSessionRow.CanRemoveRecord"/> decides whether the button is
    /// offered; the service decides whether the action is accepted, re-reading the session's
    /// real state and its unresolved attempts. A refusal is reported rather than worked around —
    /// exactly the division <see cref="AbandonAsync"/> keeps, and the reason an unfinished or
    /// recoverable job cannot be made to disappear from here.
    /// </para>
    /// </remarks>
    [RelayCommand]
    private async Task RemoveRecordAsync(RecentSessionRow? row, CancellationToken cancellationToken)
    {
        if (row is null || ListActionsBlocked)
        {
            return;
        }

        IsBusy = true;
        try
        {
            Notice = null;
            OperationResult<Unit> removed =
                await _sessions.RemoveFromRecentAsync(row.Id, cancellationToken).ConfigureAwait(true);

            Notice = removed.IsFailure
                ? Describe(Strings.Home_RemoveFailed, removed.Failure)
                : string.Format(CultureInfo.CurrentCulture, Strings.Home_RemoveDone, row.DisplayName);
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task ImportAsync(string sourceAbsolutePath, CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            Notice = null;
            OperationResult<SessionView> imported = await _sessions.ImportAsync(
                ProvisionalWorkflow,
                sourceAbsolutePath,
                outputName: null,
                operatorName: Environment.UserName,
                cancellationToken).ConfigureAwait(true);

            if (imported.IsFailure)
            {
                // The refusal sentence comes from the failure the import path produced, never
                // from a format list this screen keeps: what PrintFlow accepts is decided by
                // SupportedInputFormats and established from the file's own magic bytes, and a
                // second opinion here is exactly how a screen ends up calling a corrupt PNG an
                // unsupported one (Jira 11201, 11601).
                Notice = DisplayNames.ImportRefusal(imported.Failure);
                return;
            }

            _navigation.GoToWorkflowSelection(imported.Value);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Localised sentence plus the stable failure code.
    /// </summary>
    /// <remarks>
    /// <see cref="OperationFailure.TechnicalDetail"/> is never shown: it is English log text
    /// that can name a path. The code is a stable identifier that a support call can quote
    /// (MVP design §13.4).
    /// </remarks>
    private static string Describe(string localisedSentence, OperationFailure failure) =>
        string.Format(CultureInfo.CurrentCulture, localisedSentence, failure.Code);

    // --- SCRUM-11151: failure notices whose code sits under Error details ------------------

    private readonly NoticeFailure _noticeFailure = new();

    /// <summary>The exact stable code of the failure the notice describes; null when it describes none.</summary>
    public string? NoticeErrorCode => _noticeFailure.Code;

    public bool HasNoticeErrorCode => NoticeErrorCode is not null;

    public string NoticeErrorDetailsLabel => Strings.ErrorDetails_Heading;

    public string NoticeErrorCodeLabel => Strings.ErrorDetails_Code;

    public string NoticeErrorCodeHint => Strings.ErrorDetails_CodeHint;

    partial void OnNoticeChanged(string? value)
    {
        _noticeFailure.Track(value);
        OnPropertyChanged(nameof(NoticeErrorCode));
        OnPropertyChanged(nameof(HasNoticeErrorCode));
    }

    /// <summary>
    /// Shows a plain sentence for <paramref name="failure"/> and keeps its exact code under Error
    /// details. Home has no failed attempt to open, so the code is shown in place (SCRUM-11151).
    /// </summary>
    private void ShowFailureNotice(string sentence, OperationFailure failure)
    {
        Notice = _noticeFailure.Describe(sentence, failure);
        OnPropertyChanged(nameof(NoticeErrorCode));
        OnPropertyChanged(nameof(HasNoticeErrorCode));
    }

    private static string Format(string format, string label) =>
        string.Format(CultureInfo.CurrentCulture, format, label);
}
