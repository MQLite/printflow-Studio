using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Navigation;
using PrintFlow.App.Resources;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Settings;
using PrintFlow.Workflow.Delivery;

namespace PrintFlow.App.ViewModels;

public sealed partial class WorkflowSaveFolderRow(WorkflowType workflow) : ObservableObject
{
    public string Label => DisplayNames.Workflow(workflow);
    public string FolderId => $"Settings.SaveFolder.{workflow}";
    public string BrowseId => $"Settings.BrowseSaveFolder.{workflow}";
    public string BrowseLabel => string.Format(CultureInfo.CurrentCulture,
        Strings.Resolve("Settings_WorkflowSaveFolderBrowseName"), Label);

    [ObservableProperty] private string _folder = string.Empty;

    internal SettingKey Key => WorkflowSaveFolderSettings.KeyFor(workflow);
    internal string SavedFolder { get; private set; } = string.Empty;

    internal void ApplySavedFolder(string folder) => Folder = SavedFolder = folder;
    internal void RefreshLanguage() => OnPropertyChanged(string.Empty);
}

public sealed partial class SettingsViewModel
{
    private readonly IDeliveryFolderPicker? _saveFolderPicker;
    private readonly IApprovedArtifactDeliveryService? _saveFolderDelivery;

    public IReadOnlyList<WorkflowSaveFolderRow> WorkflowSaveFolders { get; } =
    [
        new(WorkflowType.PrepareAsset),
        new(WorkflowType.PrepareCustomerDesign),
        new(WorkflowType.GeneratePrintTiff),
    ];

    public string WorkflowSaveFoldersHeading => Strings.Resolve("Settings_WorkflowSaveFolders");
    public string WorkflowSaveFoldersHint => Strings.Resolve("Settings_WorkflowSaveFoldersHint");
    public string WorkflowSaveFolderBrowseLabel => Strings.Resolve("Settings_WorkflowSaveFolderBrowse");

    [RelayCommand(CanExecute = nameof(CanBrowseWorkflowSaveFolder))]
    private void BrowseWorkflowSaveFolder(WorkflowSaveFolderRow? row)
    {
        if (row is not null && CanBrowseWorkflowSaveFolder() &&
            _saveFolderPicker!.PickFolder(row.BrowseLabel, row.Folder) is { } folder)
            row.Folder = folder;
    }

    private bool CanBrowseWorkflowSaveFolder() => CanApply() && _saveFolderPicker is not null;

    private (WorkflowSaveFolderRow Row, string Folder)[] ReadWorkflowSaveFolderUpdates() =>
        WorkflowSaveFolders.Select(row => (Row: row, Folder: row.Folder.Trim()))
            .Where(update => update.Folder != update.Row.SavedFolder).ToArray();

    private async Task<bool> ValidateWorkflowSaveFoldersAsync(
        IReadOnlyList<(WorkflowSaveFolderRow Row, string Folder)> updates, CancellationToken cancellationToken)
    {
        foreach (var (row, folder) in updates)
        {
            if (folder.Length == 0) continue;
            if (_saveFolderDelivery is null ||
                !await Task.Run(() => _saveFolderDelivery.CheckSaveFolder(folder).IsSuccess, cancellationToken).ConfigureAwait(true))
            {
                Notice = string.Format(CultureInfo.CurrentCulture,
                    Strings.Resolve("Settings_WorkflowSaveFolderInvalid"), row.Label);
                return false;
            }
        }
        return true;
    }
}
