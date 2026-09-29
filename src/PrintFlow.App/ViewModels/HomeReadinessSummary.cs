using System.Globalization;
using PrintFlow.App.Resources;
using PrintFlow.App.Startup;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.App.ViewModels;

/// <summary>What Home says about automatic processing (SCRUM-11152). Presentation only.</summary>
public enum HomeReadinessState
{
    /// <summary>Nothing observed in this application run.</summary>
    NotChecked,

    /// <summary>An observation is running and has no result yet.</summary>
    Checking,

    /// <summary>The latest report is Verified.</summary>
    Ready,

    /// <summary>The latest report is not Verified and names a blocking check.</summary>
    Blocked,

    /// <summary>The latest observation did not finish, or its report names no blocking check.</summary>
    NotConfirmed,
}

/// <summary>
/// Home's one readiness summary, projected from the latest observation of this run (SCRUM-11152).
/// </summary>
/// <remarks>
/// It restates no readiness rule. Ready is the report's own <c>Verified</c>; the reason is the
/// first entry of the report's own <c>BlockingFailures</c>, worded by
/// <see cref="EnvironmentCheckRow"/> exactly as Production Readiness words it — except a live
/// check that did not run. That row can only come first when nothing before it failed, so the
/// readiness screen's "a prerequisite has not passed" would name a failure that does not exist;
/// Home says instead that the live check has no current result in this run. Advisories, the
/// startup preset result, timestamps and recovery counts never enter the state. The time is the
/// report's <c>ObservedAt</c>; nothing here reads the clock. Every text, including the check's own
/// name and explanation, is resolved on each read, so a language change rewords the same
/// observation without taking a new one.
/// <para>
/// Ready is worded as what the last check found: Home is not told when a later automatic step's
/// own gate check refuses, so it never presents the answer as live.
/// </para>
/// </remarks>
public sealed class HomeReadinessSummary
{
    private readonly EnvironmentReadinessReport? _report;
    private readonly EnvironmentCheckReport? _firstBlocker;

    public HomeReadinessSummary(ReadinessObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        _report = observation.State == ReadinessObservationState.Observed ? observation.Report : null;
        _firstBlocker = _report?.BlockingFailures.FirstOrDefault();
        State = observation.State switch
        {
            ReadinessObservationState.NotObserved => HomeReadinessState.NotChecked,
            ReadinessObservationState.InProgress => HomeReadinessState.Checking,
            ReadinessObservationState.Observed when _report?.Verified == true => HomeReadinessState.Ready,
            ReadinessObservationState.Observed when _firstBlocker is not null => HomeReadinessState.Blocked,
            _ => HomeReadinessState.NotConfirmed,
        };
    }

    public HomeReadinessState State { get; }

    public string Heading => Strings.Resolve("Home_ReadinessHeading");

    public string StatusText => Strings.Resolve(State switch
    {
        HomeReadinessState.NotChecked => "Home_ReadinessNotChecked",
        HomeReadinessState.Checking => "Home_ReadinessChecking",
        HomeReadinessState.Ready => "Home_ReadinessReady",
        HomeReadinessState.Blocked => "Home_ReadinessBlocked",
        _ => "Home_ReadinessNotConfirmed",
    });

    /// <summary>The first blocking reason, or why nothing is confirmed; empty when there is none to give.</summary>
    public string ReasonText
    {
        get
        {
            if (State == HomeReadinessState.NotConfirmed)
                return Strings.Resolve(_report is null ? "Home_ReadinessUnfinishedReason" : "Home_ReadinessNoReason");
            if (State != HomeReadinessState.Blocked) return string.Empty;
            if (IsLiveCheckNotRun(_firstBlocker!))
                return string.Format(CultureInfo.CurrentCulture,
                    Strings.Resolve("Home_ReadinessLiveCheckPending"), Strings.Environment_RunLiveChecks);

            EnvironmentCheckRow row = new(_firstBlocker!);
            return string.Format(CultureInfo.CurrentCulture,
                Strings.Resolve("Home_ReadinessFirstReason"), row.Name, row.Explanation);
        }
    }

    public bool HasReason => ReasonText.Length > 0;

    /// <summary>When the shown report was observed; empty when no report is shown.</summary>
    public string CheckedAtText => _report is { } report
        ? string.Format(CultureInfo.CurrentCulture, Strings.Resolve("Home_ReadinessCheckedAt"),
            report.ObservedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
        : string.Empty;

    public bool HasCheckedAt => CheckedAtText.Length > 0;

    public string HintText => Strings.Resolve(State switch
    {
        HomeReadinessState.NotChecked => "Home_ReadinessNotCheckedHint",
        HomeReadinessState.Checking => "Home_ReadinessCheckingHint",
        HomeReadinessState.Ready => "Home_ReadinessReadyHint",
        HomeReadinessState.Blocked => "Home_ReadinessBlockedHint",
        _ => "Home_ReadinessNotConfirmedHint",
    });

    public string DetailsLabel => Strings.Resolve("Environment_TechnicalDetails");

    /// <summary>The stable identifier of the first blocking check, for support; empty otherwise.</summary>
    public string TechnicalDetailText => _firstBlocker is { } check
        ? string.Format(CultureInfo.CurrentCulture, Strings.Resolve("Home_ReadinessTechnicalCheck"), check.CheckKey)
        : string.Empty;

    public bool HasTechnicalDetail => TechnicalDetailText.Length > 0;

    /// <summary>The report's own flags for "this live check did not run", as the readiness row reads them.</summary>
    private static bool IsLiveCheckNotRun(EnvironmentCheckReport check) =>
        check.Status == EnvironmentCheckStatus.Blocked && check.Phase == EnvironmentCheckPhase.LiveApplication;
}
