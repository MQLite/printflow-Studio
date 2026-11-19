using PrintFlow.Domain.Sessions;

namespace PrintFlow.Domain.Settings;

public static class WorkflowSaveFolderSettings
{
    public static SettingKey KeyFor(WorkflowType workflow) => workflow switch
    {
        WorkflowType.PrepareAsset => SettingKey.PrepareAssetSaveFolder,
        WorkflowType.PrepareCustomerDesign => SettingKey.PrepareCustomerDesignSaveFolder,
        WorkflowType.GeneratePrintTiff => SettingKey.GeneratePrintTiffSaveFolder,
        _ => throw new ArgumentOutOfRangeException(nameof(workflow)),
    };
}
