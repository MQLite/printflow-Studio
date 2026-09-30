using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels;

/// <summary>Formatting only: every action flag comes from the service recovery read model.</summary>
public sealed class RecoverySessionRow(RecoveryItem item) : ObservableObject
{
    internal void RefreshLanguage() => OnPropertyChanged(string.Empty);
    public SessionId Id => item.Id;
    public string DisplayName => item.OutputName.Value;
    public string Description => string.Format(CultureInfo.CurrentCulture, Strings.Home_RecoveryDescription,
        DisplayNames.Workflow(item.Workflow), DisplayNames.Step(item.Step), DisplayNames.StepState(item.StepState),
        item.UpdatedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
    public bool CanRestart => item.Actions.Contains(RecoveryAction.Restart);
    public bool CanImport => item.Actions.Contains(RecoveryAction.ManualResult);
    public bool CanAbandon => item.Actions.Contains(RecoveryAction.Abandon);
    public bool HasNoActions => item.Actions.Count == 0;
    public string NoActions => Strings.Home_RecoveryNoActions;
    public bool HasUnfinishedManualImport => item.HasUnfinishedManualImport;
    public string UnfinishedImportHint => Strings.Home_RecoveryUnfinishedImport;
    public string RestartLabel => Strings.Home_RecoveryRestart;
    public string ManualResultLabel => Strings.Home_RecoveryManualResult;
    public string AbandonLabel => Strings.Home_Abandon;
    public string OpenLabel => Strings.Home_Resume;

    /// <summary>
    /// The job waits for a colleague's corrected picture (SCRUM-11148). Home then offers only to
    /// open the job, first, with the plain Open hidden; it never imports from here.
    /// </summary>
    public bool HasOpenCorrection => item.HasOpenCorrection;
    public bool ShowsPlainOpen => !item.HasOpenCorrection;
    public string OpenCorrectionLabel => Strings.Home_RecoveryOpenCorrection;
    public string WaitingForCorrectionText => Strings.Home_RecentWaitingForCorrection;
    internal string ManualFilter => item.Step == StepKind.BackgroundRemoval
        ? Strings.Session_ManualCutoutFilter : Strings.Session_ManualEnhancementFilter;
}
