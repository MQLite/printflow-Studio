using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Resources;
using PrintFlow.App.Navigation;
using PrintFlow.Workflow.AiDesign;

namespace PrintFlow.App.ViewModels.AiDesign;

public sealed partial class AiPromptWorkbenchViewModel
{
    private Guid? submittedWorkRequest;
    public string AutoGenerateLabel => Strings.Resolve("AiPrompt_AutoGenerate");
    public string ContinueWorkResultLabel => Strings.Resolve("WorkTask_Continue");
    public string ContinueWorkResultHint => Strings.Resolve("WorkTask_NextStep");
    public IAsyncRelayCommand ContinueWorkResultCommand { get; }

    private async Task ContinueWorkResultAsync(CancellationToken ct)
    {
        if (navigation is not IAiDesignNavigation ai || WorkCard is not { CanContinue: true } card) return;
        var result = await ai.OpenWorkResultAsync(card.Snapshot.RequestId, ct);
        if (active && result.IsFailure) Error = Strings.Resolve("WorkTask_OpenResultFailed");
    }

    public string StopReceivingLabel => Strings.Resolve("AiPrompt_WorkStopReceiving");
    public bool IsWaitingForWorkResult => WorkCard?.Snapshot.BlocksNewSubmission == true;
    public bool CanStopReceiving => WorkCard?.Snapshot.CanStopReceiving == true;
    public CommunityToolkit.Mvvm.Input.IAsyncRelayCommand StopReceivingCommand { get; }
    private async Task SubmitToWorkAsync() => await RunAsync(async () =>
    {
        if (IsWaitingForWorkResult || isLoadingWorkCard || workCardUnavailable) return;
        if (textSubmission is null)
        {
            Error = Strings.Resolve("AiPrompt_WorkSetup");
            return;
        }
        if (HasOutdatedMemorialSpecification)
        {
            Error = L("OutdatedPromptSpecification");
            return;
        }
        if ((await FlushCoreAsync(default, saveLayout: false)).IsFailure)
            return;

        Status = Strings.Resolve("AiPrompt_WorkConnecting");
        var result = await textSubmission.SubmitAsync(Draft!, default);
        if (result.IsSuccess)
        {
            submittedWorkRequest = result.Value.RequestId;
            Status = workTasks is null ? Strings.Resolve("AiPrompt_WorkSubmitted") : null;
            QueueWorkCardRefresh();
            await WorkCardRefresh;
            return;
        }

        Status = null;
        Error = Strings.Resolve(result.Failure.TechnicalDetail switch
        {
            "BridgeNotConfigured" or "BridgeRootMismatch" or "WorkContractUnverified" or "BridgeConfigurationChanged" => "AiPrompt_WorkSetup",
            "BridgeHandshakeTimedOut" or "ChromeNotFound" or "ChromeLaunchFailed" => "AiPrompt_WorkChromeFailed",
            "WorkModeUnavailable" => "AiPrompt_WorkModeUnavailable",
            "WorkPromptAttachmentRequired" => "AiPrompt_WorkAttachmentRequired",
            "WorkUploadFailed" or "WorkUploadReceiptInvalid" => "AiPrompt_WorkUploadFailed",
            "WorkRequestActive" => "AiPrompt_WorkActive",
            "WorkReceiverUnavailable" => "AiPrompt_WorkReceiverUnavailable",
            _ => "AiPrompt_WorkUnconfirmed"
        });
    }, default);

    private async Task StopReceivingAsync() => await RunAsync(async () =>
    {
        if (textSubmission is null || !CanStopReceiving || WorkCard is not { } card) return;
        var request = card.Snapshot.RequestId;
        var stopped = await textSubmission.StopReceivingAsync(request, default);
        if (stopped.IsFailure) return;
        QueueWorkCardRefresh();
        await WorkCardRefresh;
        Status = Strings.Resolve("AiPrompt_WorkReceivingStopped");
    }, default);
}
