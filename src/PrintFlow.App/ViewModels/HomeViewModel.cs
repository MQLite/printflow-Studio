using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Navigation;
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
        StartupStatusAccessor startupStatus)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(previews);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(filePicker);
        ArgumentNullException.ThrowIfNull(startupStatus);

        _sessions = sessions;
        _previews = previews;
        _navigation = navigation;
        _filePicker = filePicker;
        _startupStatus = startupStatus;
    }

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
    public string EnvironmentLabel => Strings.Environment_Open;

    /// <summary>The way to Settings (SCRUM-11118).</summary>
    public string SettingsLabel => Strings.Settings_Open;

    /// <summary>True while the list is empty, so the view can say so rather than show nothing.</summary>
    public bool HasNoRecentSessions => RecentSessions.Count == 0 && !HasRecoverySessions;

    /// <summary>
    /// One line describing what startup recovery did, so a restart after a crash says so
    /// visibly rather than only in a report object (Part 3C1 §6, Part 3C2 §12).
    /// </summary>
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
    /// Navigation and nothing else. Home neither reads readiness nor decides anything from it —
    /// the screen it opens is the only thing that consults the diagnostics seam, and even that
    /// one can only look.
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

    /// <summary>
    /// Whether the signed workstation preset verified, in one line (Part 3C2 §13).
    /// </summary>
    /// <remarks>
    /// Deliberately a yes/no, and about startup's own preset check rather than about production
    /// readiness — which has its own screen, one click away (Epic 11500 Part C §3).
    /// </remarks>
    public string PresetStatus =>
        _startupStatus.Status?.PresetVerified == true
            ? Strings.Preset_Verified
            : Strings.Preset_NotVerified;

    /// <summary>Reloads Recent Processing from persistence.</summary>
    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var recovery = await _sessions.ListRecoveryAsync(cancellationToken).ConfigureAwait(true);
        if (recovery.IsFailure)
        {
            Notice = Describe(Strings.Home_RecoveryUnavailable, recovery.Failure);
            return;
        }
        RecoverySessions.Clear();
        foreach (RecoveryItem item in recovery.Value) RecoverySessions.Add(new RecoverySessionRow(item));
        OnPropertyChanged(nameof(HasRecoverySessions));
        OnPropertyChanged(nameof(StartupSummary));
        OnPropertyChanged(nameof(HasNoRecentSessions));

        OperationResult<IReadOnlyList<SessionListItem>> listed =
            await _sessions.ListRecentAsync(cancellationToken).ConfigureAwait(true);

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

    [RelayCommand]
    private Task AbandonRecoveryAsync(RecoverySessionRow? row, CancellationToken cancellationToken) =>
        RecoverAsync(row, RecoveryAction.Abandon, cancellationToken);

    [RelayCommand]
    private async Task OpenRecoveryAsync(RecoverySessionRow? row, CancellationToken cancellationToken)
    {
        if (row is null || IsBusy) return;
        var loaded = await _sessions.LoadAsync(row.Id, cancellationToken).ConfigureAwait(true);
        if (loaded.IsSuccess) _navigation.GoToSession(loaded.Value);
        else Notice = Describe(Strings.Home_ResumeFailed, loaded.Failure);
    }

    private async Task RecoverAsync(RecoverySessionRow? row, RecoveryAction action, CancellationToken cancellationToken)
    {
        if (row is null || IsBusy) return;
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
            if (result.IsFailure) Notice = Describe(Strings.Home_RecoveryFailed, result.Failure);
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
        if (row is null || IsBusy)
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
                Notice = Describe(Strings.Home_ResumeFailed, loaded.Failure);
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
    /// Abandons a listed session through the ordinary command path, then refreshes the list.
    /// </summary>
    /// <remarks>
    /// Nothing is deleted: the engine's <c>AbandonSession</c> records the decision and releases
    /// the automation lock, and the source snapshot, approved outputs and audit history stay
    /// exactly as they were (MVP design §6.6, Part 3C2 §10). A refusal is reported rather than
    /// worked around — the row's own <see cref="RecentSessionRow.CanAbandon"/> decides whether
    /// the button is offered, and the engine decides whether the command is accepted.
    /// </remarks>
    [RelayCommand]
    private async Task AbandonAsync(RecentSessionRow? row, CancellationToken cancellationToken)
    {
        if (row is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            Notice = null;
            OperationResult<SessionView> abandoned = await _sessions.ExecuteAsync(
                row.Id,
                new WorkflowCommand.AbandonSession(AbandonedFromHomeReason),
                Environment.UserName,
                cancellationToken).ConfigureAwait(true);

            Notice = abandoned.IsFailure
                ? Describe(Strings.Home_AbandonFailed, abandoned.Failure)
                : string.Format(CultureInfo.CurrentCulture, Strings.Home_AbandonDone, row.DisplayName);
        }
        finally
        {
            IsBusy = false;
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(true);
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
        if (row is null || IsBusy)
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
}
