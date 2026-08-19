using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Navigation;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels;

/// <summary>One step of the open session, flattened for display.</summary>
public sealed class SessionStepRow
{
    internal SessionStepRow(SessionStep step, bool isCurrent)
    {
        ArgumentNullException.ThrowIfNull(step);

        Ordinal = step.Ordinal + 1;
        Name = DisplayNames.Step(step.Step);
        State = DisplayNames.StepState(step.State);
        IsCurrent = isCurrent;
    }

    public int Ordinal { get; }

    public string Name { get; }

    public string State { get; }

    /// <summary>Whether this is the step the operator is expected to act on.</summary>
    public bool IsCurrent { get; }
}

/// <summary>
/// One quick rejection reason, offered in the review panel (MVP design §7.3).
/// </summary>
/// <remarks>
/// <see cref="Reason"/> is the stable enum value that is persisted; <see cref="Label"/> is the
/// only part an operator reads.
/// </remarks>
public sealed class RejectionReasonChoice
{
    internal RejectionReasonChoice(RejectionReason reason)
    {
        Reason = reason;
        Label = DisplayNames.RejectionReason(reason);
    }

    /// <summary>The persisted value. Never displayed.</summary>
    public RejectionReason Reason { get; }

    /// <summary>The localised operator label.</summary>
    public string Label { get; }
}

/// <summary>
/// The session processing screen: what the session is, what file it is holding, and the
/// actions the workflow currently permits (Epic 11100 Part 3C3A §3–§16).
/// </summary>
/// <remarks>
/// Every action goes through <see cref="ISessionService.ExecuteAsync"/> and nothing else. This
/// file performs no file-system access, issues no SQL, references no adapter, and assigns no
/// step state — the closest it comes to a workflow rule is reading
/// <see cref="SessionView.AvailableCommands"/>, which is the engine's own answer rather than a
/// second copy of it (MVP design invariant 12, Part 3C3A §4, §18).
/// <para>
/// After every command the screen shows the <see cref="SessionView"/> the service returned,
/// which is reconstructed from what was actually persisted. There is no local "what I think
/// happened" state to drift out of step with the database.
/// </para>
/// <para>
/// Print dimensions, the white-underbase branch, Complete and AddAnotherSize are deliberately
/// absent: they are Part 3C3B, and an inert button that looks like it works is worse than a
/// screen that plainly has none yet.
/// </para>
/// </remarks>
public sealed partial class SessionViewModel : ObservableObject
{
    private readonly ISessionService _sessions;
    private readonly INavigationService _navigation;

    /// <summary>
    /// The reason recorded when the operator hands a session over from this screen.
    /// </summary>
    /// <remarks>
    /// Stable English, not a resource, for the same reason
    /// <see cref="WorkflowCommand.Skip.DefaultReason"/> is: it is persisted as audit history,
    /// and a record whose text changes with the workstation's language would be a poor audit
    /// trail (MVP design §13.4).
    /// </remarks>
    private const string HandedOffFromSessionReason =
        "Handed off to the operator from the session screen.";

    [ObservableProperty]
    private string? _notice;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>The quick reason sent with a rejection. Never null once the list is built.</summary>
    [ObservableProperty]
    private RejectionReasonChoice _selectedRejectionReason;

    [ObservableProperty]
    private string? _rejectionNotes;

    private SessionView? _session;

    public SessionViewModel(ISessionService sessions, INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(navigation);

        _sessions = sessions;
        _navigation = navigation;

        RejectionReasons = new ReadOnlyCollection<RejectionReasonChoice>(
            Enum.GetValues<RejectionReason>().Select(reason => new RejectionReasonChoice(reason)).ToList());
        _selectedRejectionReason = RejectionReasons[0];
    }

    /// <summary>The open session's steps, in workflow order.</summary>
    public ObservableCollection<SessionStepRow> Steps { get; } = [];

    /// <summary>Every quick rejection reason, in enum order.</summary>
    public IReadOnlyList<RejectionReasonChoice> RejectionReasons { get; }

    // --- Labels --------------------------------------------------------------------------

    public string BackLabel => Strings.Nav_BackToHome;

    public string StepsHeading => Strings.Session_StepsHeading;

    public string PlaceholderNotice => Strings.Session_PlaceholderNotice;

    public string ConfirmOriginalLabel => Strings.Session_ConfirmOriginal;

    public string RunStepLabel => Strings.Session_RunStep;

    public string ApproveLabel => Strings.Session_Approve;

    public string RejectLabel => Strings.Session_Reject;

    public string RetryLabel => Strings.Session_Retry;

    public string SkipLabel => Strings.Session_Skip;

    public string HandOffLabel => Strings.Session_HandOff;

    public string ReviewHeading => Strings.Session_ReviewHeading;

    public string RejectReasonLabel => Strings.Session_RejectReasonLabel;

    public string RejectNotesLabel => Strings.Session_RejectNotesLabel;

    public string ArtefactHeading => Strings.Session_ArtefactHeading;

    public string ArtefactNoneText => Strings.Session_ArtefactNone;

    public string ArtefactIsInputText => Strings.Session_ArtefactIsInput;

    public string FileNameLabel => Strings.Session_LabelFileName;

    public string FormatLabel => Strings.Session_LabelFormat;

    public string PixelsLabel => Strings.Session_LabelPixels;

    public string DpiLabel => Strings.Session_LabelDpi;

    public string HashLabel => Strings.Session_LabelHash;

    public string RevisionLabel => Strings.Session_LabelRevision;

    /// <summary>
    /// The unmissable warning that this installation produces synthetic results
    /// (Part 3C3A §8).
    /// </summary>
    /// <remarks>
    /// Whether to show it is read from <see cref="SessionView.ProcessingMode"/>, which the
    /// service derives from the adapters actually wired up. A view model that guessed from
    /// configuration could disagree with what really ran.
    /// </remarks>
    public string FakeModeNotice => Strings.Session_FakeModeNotice;

    public bool IsFakeProcessing => _session?.IsFakeProcessing == true;

    // --- Session identity ----------------------------------------------------------------

    /// <summary>The output name of the open session.</summary>
    public string SessionName => _session?.OutputName.Value ?? string.Empty;

    /// <summary>The localised workflow of the open session.</summary>
    public string Workflow => _session is null ? string.Empty : DisplayNames.Workflow(_session.WorkflowType);

    /// <summary>The localised session state of the open session.</summary>
    public string State => _session is null ? string.Empty : DisplayNames.SessionState(_session.State);

    /// <summary>The localised step the session is waiting on, or a "finished" line.</summary>
    public string CurrentStep => _session?.CurrentStep is { } step
        ? DisplayNames.Step(step.Step)
        : Strings.Session_AllStepsFinished;

    /// <summary>
    /// True for a completed, handed-off or abandoned session, which is a record to read rather
    /// than work to continue (Part 3C2 §11).
    /// </summary>
    public bool IsReadOnly => _session?.CanContinueProcessing != true;

    /// <summary>
    /// True once automation has ended for this session (Part 3C3A §14).
    /// </summary>
    /// <remarks>
    /// It drives a sentence, not a workflow: nothing here resumes automation, watches a folder
    /// or launches an application. The operator continues in whatever tool they choose.
    /// </remarks>
    public bool IsHandedOff => _session?.State == SessionState.HandedOff;

    public string HandedOffNotice => Strings.Session_HandedOffNotice;

    // --- Current artefact ----------------------------------------------------------------

    public bool HasArtefact => _session?.CurrentArtefact is not null;

    /// <summary>True when the file shown is the step's input rather than its result.</summary>
    public bool ArtefactIsInput => _session?.CurrentArtefact is { IsCurrentStepResult: false };

    public string ArtefactFileName => _session?.CurrentArtefact?.FileName ?? string.Empty;

    public string ArtefactFormat => _session?.CurrentArtefact is { } artefact
        ? DisplayNames.ImageFormat(artefact.Facts.Format)
        : string.Empty;

    /// <summary>Pixel dimensions, or a plain "not determined" for a PSD/PDF import.</summary>
    public string ArtefactPixels => _session?.CurrentArtefact?.Facts is { HasPixelDimensions: true } facts
        ? string.Create(CultureInfo.CurrentCulture, $"{facts.PixelWidth} x {facts.PixelHeight}")
        : Strings.Session_ValueUnknown;

    public string ArtefactDpi => _session?.CurrentArtefact?.Facts is { DpiX: > 0, DpiY: > 0 } facts
        ? string.Create(CultureInfo.CurrentCulture, $"{facts.DpiX:0.##} x {facts.DpiY:0.##}")
        : Strings.Session_ValueUnknown;

    /// <summary>The first 12 hex characters of the hash. Never used for comparison.</summary>
    public string ArtefactHash => _session?.CurrentArtefact?.Sha256.ShortForm ?? string.Empty;

    /// <summary>A short revision identifier, enough to tell two results apart on screen.</summary>
    public string ArtefactRevision => _session?.CurrentArtefact is { } artefact
        ? artefact.RevisionId.Value.ToString("N", CultureInfo.InvariantCulture)[..8]
        : string.Empty;

    // --- Command availability ------------------------------------------------------------
    //
    // Every one of these is a reading of the engine's own AvailableCommands. There is no
    // "if the step is ReviewRequired then Approve" anywhere in this file: that rule lives in
    // WorkflowEngine, and restating it here would create a second copy that could disagree
    // with the one that actually accepts the click (Part 3C3A §4).

    public bool CanConfirmOriginal => Allows(CommandKind.ConfirmOriginal);

    public bool CanRunStep => Allows(CommandKind.StartStep);

    public bool CanApprove => Allows(CommandKind.Approve);

    public bool CanReject => Allows(CommandKind.Reject);

    public bool CanRetry => Allows(CommandKind.Retry);

    public bool CanSkip => Allows(CommandKind.Skip);

    public bool CanHandOff => Allows(CommandKind.HandOff);

    /// <summary>
    /// Whether the review panel is shown.
    /// </summary>
    /// <remarks>
    /// Derived from the review commands being legal rather than from the step state, for the
    /// same reason as above: the panel exists to carry Approve and Reject, so "is either of
    /// them offered" is the honest condition, and it cannot drift from the buttons inside it.
    /// </remarks>
    public bool IsReviewRequired => CanApprove || CanReject;

    /// <summary>Shows <paramref name="session"/> exactly as the service returned it.</summary>
    public void Open(SessionView session)
    {
        ArgumentNullException.ThrowIfNull(session);

        Show(session);
        Notice = null;
    }

    // --- Commands ------------------------------------------------------------------------

    /// <summary>
    /// Confirms the imported original through the ordinary command path (Part 3C3A §6).
    /// </summary>
    /// <remarks>
    /// Note what this does <i>not</i> do: it does not set the step to Approved. Whether
    /// confirmation is a bare acknowledgement or a hash-bound design-readiness review depends
    /// on the workflow definition, and only the engine knows which.
    /// </remarks>
    [RelayCommand]
    private Task ConfirmOriginalAsync(CancellationToken cancellationToken) =>
        RunAsync(_ => new WorkflowCommand.ConfirmOriginal(), cancellationToken);

    /// <summary>
    /// Starts an attempt for the current step (Part 3C3A §7).
    /// </summary>
    /// <remarks>
    /// The environment gate, the automation lock, the adapter call, output validation, hashing
    /// and the two metadata transactions all happen behind
    /// <see cref="ISessionService.ExecuteAsync"/>. This screen supplies the step and receives
    /// the refreshed session.
    /// </remarks>
    [RelayCommand]
    private Task RunStepAsync(CancellationToken cancellationToken) =>
        RunAsync(step => new WorkflowCommand.StartStep(step), cancellationToken);

    /// <summary>
    /// Approves the result currently on screen, bound to its exact hash (Part 3C3A §10).
    /// </summary>
    /// <remarks>
    /// The hash comes from the artefact this screen displayed, not from a value cached when the
    /// session was first opened, and only when that artefact is the step's own result. If the
    /// file changed after it was shown, the service's integrity re-check refuses the command
    /// with <c>RevisionIntegrityMismatch</c> and the session does not advance — there is no
    /// automatic re-approval anywhere in this path.
    /// </remarks>
    [RelayCommand]
    private Task ApproveAsync(CancellationToken cancellationToken) =>
        RunAsync(
            step => ReviewedHash is Sha256 hash ? new WorkflowCommand.Approve(step, hash) : null,
            cancellationToken);

    /// <summary>Rejects the result currently on screen with a quick reason and optional notes.</summary>
    [RelayCommand]
    private Task RejectAsync(CancellationToken cancellationToken) =>
        RunAsync(
            step => ReviewedHash is Sha256 hash
                ? new WorkflowCommand.Reject(step, hash, SelectedRejectionReason.Reason, Trimmed(RejectionNotes))
                : null,
            cancellationToken);

    /// <summary>
    /// Returns a rejected, failed or interrupted step to a state where a new attempt is legal.
    /// </summary>
    /// <remarks>
    /// Deliberately not combined with Run Step. Retry moves the step to Waiting and stops
    /// there, so the operator sees the state progression rather than a single button that
    /// silently does two things (Part 3C3A §12).
    /// </remarks>
    [RelayCommand]
    private Task RetryAsync(CancellationToken cancellationToken) =>
        RunAsync(step => new WorkflowCommand.Retry(step), cancellationToken);

    /// <summary>
    /// Skips the current step, recording the stable default reason.
    /// </summary>
    /// <remarks>
    /// Which steps may be skipped is the workflow definition's answer, not this screen's: the
    /// button is offered only when <c>AvailableCommands</c> contains Skip, and the engine still
    /// refuses a non-skippable step if it is asked anyway.
    /// </remarks>
    [RelayCommand]
    private Task SkipAsync(CancellationToken cancellationToken) =>
        RunAsync(step => new WorkflowCommand.Skip(step), cancellationToken);

    /// <summary>Ends automated processing and transfers the work to the operator.</summary>
    [RelayCommand]
    private Task HandOffAsync(CancellationToken cancellationToken) =>
        RunAsync(step => new WorkflowCommand.HandOff(step, HandedOffFromSessionReason), cancellationToken);

    /// <summary>Returns to Home. Changes nothing about the session (Part 3C3A §16).</summary>
    [RelayCommand]
    private async Task BackToHomeAsync(CancellationToken cancellationToken) =>
        await _navigation.GoHomeAsync(cancellationToken).ConfigureAwait(true);

    // --- Plumbing ------------------------------------------------------------------------

    /// <summary>
    /// The hash a review decision must be bound to: the hash of the artefact actually
    /// displayed, and only when that artefact is the current step's own result.
    /// </summary>
    private Sha256? ReviewedHash =>
        _session?.CurrentArtefact is { IsCurrentStepResult: true } artefact ? artefact.Sha256 : null;

    private bool Allows(CommandKind kind) => _session?.AvailableCommands.Contains(kind) == true;

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Builds the command for the current step, executes it, and shows whatever came back.
    /// </summary>
    /// <remarks>
    /// One path for every button, so no action can quietly skip the refresh: the screen is
    /// always rebuilt from the <see cref="SessionView"/> the service returned, and a failure is
    /// reported rather than swallowed or worked around.
    /// </remarks>
    private async Task RunAsync(Func<StepKind, WorkflowCommand?> build, CancellationToken cancellationToken)
    {
        if (_session is null || IsBusy || _session.CurrentStep is not { } step)
        {
            return;
        }

        WorkflowCommand? command = build(step.Step);
        if (command is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            Notice = null;
            OperationResult<SessionView> result = await _sessions
                .ExecuteAsync(_session.Id, command, Environment.UserName, cancellationToken)
                .ConfigureAwait(true);

            if (result.IsFailure)
            {
                Notice = Describe(result.Failure);

                // The command did not apply, but the session may still have moved — an
                // integrity mismatch invalidates the Revision it was about, and a failed
                // attempt is persisted before the failure returns. Re-reading is what keeps the
                // screen showing the database rather than the last thing that worked.
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
                return;
            }

            Show(result.Value);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (_session is null)
        {
            return;
        }

        OperationResult<SessionView> reloaded =
            await _sessions.LoadAsync(_session.Id, cancellationToken).ConfigureAwait(true);

        if (reloaded.IsSuccess)
        {
            Show(reloaded.Value);
        }
    }

    /// <summary>Rebuilds every displayed value from <paramref name="session"/>.</summary>
    private void Show(SessionView session)
    {
        _session = session;

        Steps.Clear();
        foreach (SessionStep step in session.Steps)
        {
            Steps.Add(new SessionStepRow(step, isCurrent: step == session.CurrentStep));
        }

        OnPropertyChanged(nameof(SessionName));
        OnPropertyChanged(nameof(Workflow));
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(IsReadOnly));
        OnPropertyChanged(nameof(IsHandedOff));
        OnPropertyChanged(nameof(IsFakeProcessing));

        OnPropertyChanged(nameof(HasArtefact));
        OnPropertyChanged(nameof(ArtefactIsInput));
        OnPropertyChanged(nameof(ArtefactFileName));
        OnPropertyChanged(nameof(ArtefactFormat));
        OnPropertyChanged(nameof(ArtefactPixels));
        OnPropertyChanged(nameof(ArtefactDpi));
        OnPropertyChanged(nameof(ArtefactHash));
        OnPropertyChanged(nameof(ArtefactRevision));

        OnPropertyChanged(nameof(CanConfirmOriginal));
        OnPropertyChanged(nameof(CanRunStep));
        OnPropertyChanged(nameof(CanApprove));
        OnPropertyChanged(nameof(CanReject));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(CanSkip));
        OnPropertyChanged(nameof(CanHandOff));
        OnPropertyChanged(nameof(IsReviewRequired));
    }

    /// <summary>
    /// A localised sentence plus the stable failure code.
    /// </summary>
    /// <remarks>
    /// <see cref="OperationFailure.TechnicalDetail"/> is never shown: it is English log text
    /// that can name a path. The code is a stable identifier a support call can quote, and no
    /// stack trace reaches this screen (Part 3C3A §15).
    /// </remarks>
    private static string Describe(OperationFailure failure) => string.Format(
        CultureInfo.CurrentCulture,
        Strings.Session_ActionFailed,
        DisplayNames.Failure(failure.Code),
        failure.Code);
}
