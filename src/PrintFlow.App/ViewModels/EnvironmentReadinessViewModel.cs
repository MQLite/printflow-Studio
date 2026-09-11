using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Navigation;
using PrintFlow.App.Resources;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.App.ViewModels;

/// <summary>
/// One workstation check, already localised and classified for display
/// (Epic 11500 Part C §3, §5).
/// </summary>
/// <remarks>
/// A projection of <see cref="EnvironmentCheckReport"/> and nothing more. It re-derives no rule:
/// <see cref="IsBlocking"/> is the flag the readiness report carried, and
/// <see cref="Classification"/> is a label over that flag rather than a second opinion about
/// which checks matter (§5).
/// <para>
/// <see cref="Name"/> is a neutral subject — "Display configuration" — and is what a passing row
/// shows. <see cref="Explanation"/> is the report's own <c>MessageKey</c>, which reads as the
/// problem it describes and is therefore shown only where there is one: on a failure or on an
/// advisory (§6). A passing check that displayed its failure sentence would tell the operator
/// the opposite of the truth.
/// </para>
/// </remarks>
public sealed class EnvironmentCheckRow
{
    /// <summary>The resource-key prefix for a check's neutral localised subject (§3).</summary>
    private const string NamePrefix = "EnvironmentCheckName_";

    internal EnvironmentCheckRow(EnvironmentCheckReport report, ReadinessEvidenceLifecycle? lifecycle = null)
    {
        ArgumentNullException.ThrowIfNull(report);

        SupportKey = report.CheckKey;
        AutomationId = "Environment.Check." + report.CheckKey;
        IsBlocking = report.IsBlocking;
        IsFailure = report.Status == EnvironmentCheckStatus.Failed;
        IsBlocked = report.Status == EnvironmentCheckStatus.Blocked;
        IsAdvisory = report.Status == EnvironmentCheckStatus.Advisory;
        Phase = report.Phase;

        Name = Strings.Resolve(NamePrefix + report.CheckKey);
        Status = report.Status switch
        {
            EnvironmentCheckStatus.Passed => Strings.Environment_StatusPassed,
            EnvironmentCheckStatus.Failed => Strings.Environment_StatusFailed,
            EnvironmentCheckStatus.Blocked => Strings.Environment_StatusBlocked,
            _ => Strings.Environment_StatusAdvisory,
        };
        Classification = IsBlocking ? Strings.Environment_Blocking : Strings.Environment_Advisory;
        Explanation = IsFailure || IsBlocked || IsAdvisory ? Strings.Resolve(report.MessageKey) : string.Empty;
        Detail = report.Detail;
        if (report.CheckKey == "PhotoshopTestImageRoundTrip" && lifecycle is not null)
        {
            string unknown = Strings.Resolve("Environment_ProbeNotRecorded");
            Detail += Environment.NewLine + string.Format(CultureInfo.CurrentCulture,
                Strings.Resolve("Environment_LiveEvidenceDetail"),
                lifecycle.LastSuccessfulLiveAt?.ToString("u", CultureInfo.CurrentCulture) ?? unknown,
                lifecycle.EvidenceAvailable, lifecycle.CurrentObservationDeferred);
            if (lifecycle.LatestProbe is { } probe)
            {
                Detail += Environment.NewLine + string.Format(CultureInfo.CurrentCulture,
                    Strings.Resolve("Environment_ProbeProgressDetail"),
                    lifecycle.LatestAttemptAt?.ToString("u", CultureInfo.CurrentCulture) ?? unknown,
                    probe.OperationId, string.Join(", ", probe.Stages),
                    probe.LastAttemptedStage, probe.LastConfirmedStage, probe.CleanupOutcome,
                    probe.PrimaryFailure?.Code.ToString() ?? unknown,
                    string.Join(", ", probe.SecondaryFailures.Select(failure => $"{failure.Phase}:{failure.Code}")));
            }
        }
        Expected = report.Expected ?? string.Empty;
        Current = report.Current ?? string.Empty;
    }

    /// <summary>
    /// The stable English name of the checked fact, for a support call to quote.
    /// </summary>
    /// <remarks>
    /// Shown beside the localised <see cref="Name"/> and never instead of it, the same rule a
    /// <c>FailureCode</c> follows: stable identifiers are quotable, not readable (§3).
    /// </remarks>
    public string SupportKey { get; }

    public string AutomationId { get; }

    /// <summary>The localised subject of the check, shown whatever the outcome.</summary>
    public string Name { get; }

    /// <summary>Passed, Failed or Note.</summary>
    public string Status { get; }

    /// <summary>Required or Advisory, taken from the report's own flag.</summary>
    public string Classification { get; }

    /// <summary>Whether this check can close production work. False for every advisory.</summary>
    public bool IsBlocking { get; }

    /// <summary>Whether this check failed. An advisory is never a failure (§5).</summary>
    public bool IsFailure { get; }

    /// <summary>Whether a prerequisite prevented this check from running.</summary>
    public bool IsBlocked { get; }

    /// <summary>Whether this check is an advisory that reports and never blocks.</summary>
    public bool IsAdvisory { get; }

    public EnvironmentCheckPhase Phase { get; }

    public string Expected { get; }

    public string Current { get; }

    public bool HasExpected => Expected.Length > 0;

    public bool HasCurrent => Current.Length > 0;

    public string ExpectedDisplay => $"{Strings.Environment_Expected}: {Expected}";

    public string CurrentDisplay => $"{Strings.Environment_Current}: {Current}";

    /// <summary>The localised sentence, on failures and advisories only.</summary>
    public string Explanation { get; }

    /// <summary>The bounded English detail the report carried, for support.</summary>
    public string Detail { get; }

    /// <summary>True where there is an <see cref="Explanation"/> to show.</summary>
    public bool HasExplanation => Explanation.Length > 0;
}

/// <summary>
/// Production Readiness: what the workstation was checked for and what production work still
/// needs (Epic 11500 Part C §3).
/// </summary>
/// <remarks>
/// <b>It observes and explains. It does not repair and it does not authorise.</b> Refresh asks
/// <see cref="IEnvironmentDiagnostics.Read"/> for a passive observation. The separate, explicit
/// live-check command may launch or attach to the accepted applications and run one contained
/// synthetic Photoshop round trip. There is no enable, continue-anyway, ignore,
/// retry-as-production or adapter switch on this screen (§2, §13).
/// <para>
/// <b>One authority, read from one seam.</b> The shell never names the workstation verifier and
/// never re-derives which checks matter. <c>VerifiedEnvironmentGate</c> answers both questions —
/// the workflow's "may Production run" and this screen's "why" — from the same object, so a
/// screen showing Ready and a gate refusing cannot disagree (§2).
/// </para>
/// <para>
/// <b>Refreshing re-observes; it does not re-run the live probe (§4, §8).</b> Each read asks the gate afresh,
/// so a restored display or an unlocked screen shows as ready without restarting PrintFlow. The
/// accepted files behind the baseline are read once per run, which is why
/// <see cref="RestartRequirement"/> is on the screen rather than in a report nobody on the shop
/// floor reads.
/// </para>
/// </remarks>
public sealed partial class EnvironmentReadinessViewModel : ObservableObject
{
    private readonly IEnvironmentDiagnostics _diagnostics;
    private readonly INavigationService _navigation;

    [ObservableProperty]
    private bool _isBusy;

    private EnvironmentReadinessReport? _report;

    public EnvironmentReadinessViewModel(
        IEnvironmentDiagnostics diagnostics, INavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(navigation);

        _diagnostics = diagnostics;
        _navigation = navigation;
    }

    /// <summary>Every check that ran, in evaluation order.</summary>
    public ObservableCollection<EnvironmentCheckRow> Checks { get; } = [];

    public ObservableCollection<EnvironmentCheckRow> AutomaticChecks { get; } = [];

    public ObservableCollection<EnvironmentCheckRow> LiveApplicationChecks { get; } = [];

    /// <summary>The blocking checks that failed, listed on their own so all of them are visible.</summary>
    public ObservableCollection<EnvironmentCheckRow> BlockingFailures { get; } = [];

    /// <summary>The advisories, listed separately from anything that blocks (§5).</summary>
    public ObservableCollection<EnvironmentCheckRow> Advisories { get; } = [];

    public string Heading => Strings.Environment_Heading;

    public string Hint => Strings.Environment_Hint;

    public string RefreshLabel => Strings.Environment_Refresh;

    public string RunLiveChecksLabel => Strings.Environment_RunLiveChecks;

    public string RunLiveChecksHint => Strings.Environment_RunLiveChecksHint;

    public string CheckActivityText => IsBusy ? Strings.Environment_Checking : RunLiveChecksHint;

    public string BackLabel => Strings.Nav_BackToHome;

    public string PresetLabel => Strings.Environment_Preset;

    public string ObservedAtLabel => Strings.Environment_ObservedAt;

    public string ChecksHeading => Strings.Environment_ChecksHeading;

    public string AutomaticChecksHeading => Strings.Environment_AutomaticChecksHeading;

    public string LiveChecksHeading => Strings.Environment_LiveChecksHeading;

    public string ExpectedLabel => Strings.Environment_Expected;

    public string CurrentLabel => Strings.Environment_Current;

    public string BlockingHeading => Strings.Environment_BlockingHeading;

    public string AdvisoriesHeading => Strings.Environment_Advisories;

    /// <summary>
    /// What "Check again" actually re-reads, stated so the button never implies more (§8).
    /// </summary>
    public string RefreshScope => Strings.Environment_RefreshScope;

    /// <summary>
    /// The restart boundary, in the operator's own language (§8).
    /// </summary>
    /// <remarks>
    /// Always shown, not only after a failure. It is a standing operating rule about what to do
    /// after replacing an accepted file, and an operator who only meets it at the moment
    /// production is already refused has met it too late.
    /// </remarks>
    public string RestartRequirement => Strings.Environment_RestartRequired;

    /// <summary>True when every blocking check passed, exactly as the report reported it.</summary>
    public bool IsReady => _report?.Verified == true;

    /// <summary>
    /// Ready or not ready, in one localised line.
    /// </summary>
    /// <remarks>
    /// Derived from <see cref="EnvironmentReadinessReport.Verified"/> alone. An advisory does not
    /// enter this sentence, because an advisory does not close production work (§5).
    /// </remarks>
    public string StatusText => IsReady ? Strings.Environment_Verified : Strings.Environment_NotVerified;

    /// <summary>Whether any advisory was reported.</summary>
    public bool HasAdvisories => Advisories.Count > 0;

    /// <summary>
    /// "Advisories: present" or "Advisories: none", beside the readiness line and never instead
    /// of it (§5).
    /// </summary>
    public string AdvisorySummary =>
        HasAdvisories ? Strings.Environment_AdvisoriesPresent : Strings.Environment_AdvisoriesNone;

    /// <summary>Whether anything is currently stopping production work.</summary>
    public bool HasBlockingFailures => BlockingFailures.Count > 0;

    /// <summary>
    /// The inverse, so the view can say "nothing is stopping production work" rather than show
    /// an empty list.
    /// </summary>
    public bool HasNoBlockingFailures => BlockingFailures.Count == 0;

    /// <summary>Shown in place of the blocking list when there is nothing in it.</summary>
    public string NoBlockingFailuresText => Strings.Environment_NoBlockingFailures;

    /// <summary>
    /// The accepted preset's id, version and short digest — or a sentence saying it states
    /// nothing, when the preset itself did not pass.
    /// </summary>
    public string PresetIdentity => _report?.PresetIdentity ?? Strings.Environment_PresetUnavailable;

    /// <summary>
    /// When this reading was taken, in the workstation's local time.
    /// </summary>
    /// <remarks>
    /// Information about the reading and never a criterion: nothing on this screen or behind it
    /// treats a recent timestamp as permission, and the gate re-asks on every production request
    /// rather than consulting anything displayed here (§4).
    /// </remarks>
    public string ObservedAt => _report is { } report
        ? report.ObservedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
        : string.Empty;

    /// <summary>True once a reading exists, so the screen shows nothing it has not read.</summary>
    public bool HasReport => _report is not null;

    /// <summary>Takes the screen's first reading.</summary>
    public Task OpenAsync(CancellationToken cancellationToken) => ReadAsync(cancellationToken);

    /// <summary>
    /// Asks the diagnostics seam to look again.
    /// </summary>
    /// <remarks>
    /// The whole point of the screen being useful after a repair: an operator who restored the
    /// accepted display, unlocked the workstation or left a remote session presses this and sees
    /// the current answer, without restarting PrintFlow (§4). It carries no state forward — the
    /// previous reading is discarded and replaced, so a check that has stopped failing stops
    /// being listed.
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanRunCheckCommand))]
    private Task RefreshAsync(CancellationToken cancellationToken) => ReadAsync(cancellationToken);

    /// <summary>Runs the only mutating diagnostic phase, solely on explicit operator request.</summary>
    [RelayCommand(CanExecute = nameof(CanRunCheckCommand))]
    private async Task RunLiveChecksAsync(CancellationToken cancellationToken)
    {
        if (IsBusy) return;

        IsBusy = true;
        try
        {
            Apply(await _diagnostics.RunLiveChecksAsync(cancellationToken).ConfigureAwait(true));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRunCheckCommand() => !IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        RunLiveChecksCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CheckActivityText));
    }

    [RelayCommand]
    private async Task BackToHomeAsync(CancellationToken cancellationToken) =>
        await _navigation.GoHomeAsync(cancellationToken).ConfigureAwait(true);

    /// <summary>
    /// One reading, replacing the last.
    /// </summary>
    /// <remarks>
    /// Off the UI thread, because the first reading of a run hashes the accepted installations
    /// and every evidence file the preset vouches for. Later readings are cheap — the dynamic
    /// facts are all that is re-observed — but the screen must not freeze the shell to take the
    /// first one.
    /// </remarks>
    private async Task ReadAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            EnvironmentReadinessReport report = await Task
                .Run(_diagnostics.Read, cancellationToken)
                .ConfigureAwait(true);

            Apply(report);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Apply(EnvironmentReadinessReport report)
    {
        _report = report;

        Checks.Clear();
        AutomaticChecks.Clear();
        LiveApplicationChecks.Clear();
        BlockingFailures.Clear();
        Advisories.Clear();

        foreach (EnvironmentCheckReport check in report.Checks)
        {
            EnvironmentCheckRow row = new(check, report.Lifecycle);
            Checks.Add(row);
            if (check.Phase == EnvironmentCheckPhase.LiveApplication)
                LiveApplicationChecks.Add(row);
            else
                AutomaticChecks.Add(row);
        }

        // Both projections come off the report rather than off a rule restated here: the report
        // already separates what closes production from what merely reports (§5).
        foreach (EnvironmentCheckReport failure in report.BlockingFailures)
        {
            BlockingFailures.Add(new EnvironmentCheckRow(failure, report.Lifecycle));
        }

        foreach (EnvironmentCheckReport advisory in report.Advisories)
        {
            Advisories.Add(new EnvironmentCheckRow(advisory));
        }

        OnPropertyChanged(nameof(HasReport));
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(HasAdvisories));
        OnPropertyChanged(nameof(AdvisorySummary));
        OnPropertyChanged(nameof(HasBlockingFailures));
        OnPropertyChanged(nameof(HasNoBlockingFailures));
        OnPropertyChanged(nameof(PresetIdentity));
        OnPropertyChanged(nameof(ObservedAt));
    }
}
