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
    private readonly INavigationService _navigation;
    private readonly IFilePicker _filePicker;
    private readonly StartupStatusAccessor _startupStatus;

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
        INavigationService navigation,
        IFilePicker filePicker,
        StartupStatusAccessor startupStatus)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(navigation);
        ArgumentNullException.ThrowIfNull(filePicker);
        ArgumentNullException.ThrowIfNull(startupStatus);

        _sessions = sessions;
        _navigation = navigation;
        _filePicker = filePicker;
        _startupStatus = startupStatus;
    }

    /// <summary>Recent Processing, newest first, exactly as the service returned it.</summary>
    public ObservableCollection<RecentSessionRow> RecentSessions { get; } = [];

    public string Title => Strings.App_Title;

    public string ImportHeading => Strings.Home_ImportHeading;

    public string ImportHint => Strings.Home_ImportHint;

    public string ChooseFileLabel => Strings.Home_ChooseFile;

    public string RecentHeading => Strings.Home_RecentHeading;

    public string RefreshLabel => Strings.Home_Refresh;

    public string AbandonLabel => Strings.Home_Abandon;

    public string EmptyRecentText => Strings.Home_NoRecentSessions;

    /// <summary>True while the list is empty, so the view can say so rather than show nothing.</summary>
    public bool HasNoRecentSessions => RecentSessions.Count == 0;

    /// <summary>
    /// One line describing what startup recovery did, so a restart after a crash says so
    /// visibly rather than only in a report object (Part 3C1 §6, Part 3C2 §12).
    /// </summary>
    /// <remarks>
    /// A summary and nothing more — the surface that lists individual recovery entries is a
    /// later slice.
    /// </remarks>
    public string StartupSummary
    {
        get
        {
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
    /// Whether the signed workstation preset verified, in one line (Part 3C2 §13).
    /// </summary>
    /// <remarks>
    /// Deliberately a yes/no. The manifest's contents are not operator information, and the
    /// environment validation that acts on this answer is Epic 11500.
    /// </remarks>
    public string PresetStatus =>
        _startupStatus.Status?.PresetVerified == true
            ? Strings.Preset_Verified
            : Strings.Preset_NotVerified;

    /// <summary>Reloads Recent Processing from persistence.</summary>
    [RelayCommand]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
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
            RecentSessions.Add(new RecentSessionRow(item));
        }

        OnPropertyChanged(nameof(HasNoRecentSessions));
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
                Notice = Describe(Strings.Home_ImportFailed, imported.Failure);
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
