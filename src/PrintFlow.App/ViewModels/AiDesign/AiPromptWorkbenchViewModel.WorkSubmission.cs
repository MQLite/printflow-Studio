using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Resources;
using PrintFlow.App.Navigation;
using PrintFlow.Workflow.AiDesign;

namespace PrintFlow.App.ViewModels.AiDesign;

public sealed partial class AiPromptWorkbenchViewModel
{
    private Guid? submittedWorkRequest;
    private bool generationStarted;
    private bool submittingGeneration;
    private string? generationFeedback;
    public string AutoGenerateLabel => Strings.Resolve("AiPrompt_AutoGenerate");
    public string GenerationProgressLabel => Strings.Resolve("AiPrompt_GenerationProgress");
    public bool ShowGenerationProgress => generationStarted || HasWorkCard;
    public string? GenerationFeedback => WorkCard?.Stage ?? generationFeedback;
    public IReadOnlyList<GenerationMilestone> GenerationMilestones
    {
        get
        {
            var snapshot = WorkCard?.Snapshot;
            var stage = snapshot?.Stage;
            bool isVerifying = snapshot?.IsVerifyingResult == true;
            int reached = stage switch
            {
                WorkTaskStage.Submitting or WorkTaskStage.AwaitingFollowUp => 0,
                WorkTaskStage.Imported => 4,
                _ when isVerifying => 2,
                WorkTaskStage.AwaitingReturn or WorkTaskStage.SubmittedOnly => 1,
                _ => WorkCard?.CanViewWork == true || submittedWorkRequest is not null ? 1 : 0
            };
            int current = stage switch
            {
                WorkTaskStage.Submitting or WorkTaskStage.AwaitingFollowUp => 0,
                WorkTaskStage.AwaitingReturn => 1,
                _ when isVerifying => 2,
                null when submittingGeneration => 0,
                _ => -1
            };
            string[] labels =
            [
                reached >= 1 ? "WorkTask_Sent" : "AiPrompt_GenerationSubmit",
                reached >= 2 ? "WorkTask_Returned" : current == 1 ? "AiPrompt_GenerationWaiting" : "AiPrompt_GenerationReturn",
                reached >= 3 ? "WorkTask_Verified" : "AiPrompt_GenerationVerify",
                reached >= 4 ? "WorkTask_Imported" : "AiPrompt_GenerationImport"
            ];
            return labels.Select((label, index) => new GenerationMilestone(Strings.Resolve(label), index < reached,
                index == current, index > 0, index < labels.Length - 1)).ToArray();
        }
    }
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
        if (!active || Draft is null || IsWaitingForWorkResult || isLoadingWorkCard || workCardUnavailable) return;
        long submittingEpoch = epoch;
        generationStarted = true;
        submittingGeneration = true;
        generationFeedback = Strings.Resolve("AiPrompt_WorkConnecting");
        submittedWorkRequest = null;
        WorkCard = null;
        NotifyGenerationProgress();
        try { await SubmitGenerationCoreAsync(submittingEpoch); }
        finally
        {
            if (IsCurrent(submittingEpoch))
            {
                submittingGeneration = false;
                if (Error is not null) generationFeedback = Error;
                NotifyGenerationProgress();
            }
        }
    }, default);

    private void NotifyGenerationProgress()
    {
        OnPropertyChanged(nameof(ShowGenerationProgress));
        OnPropertyChanged(nameof(GenerationFeedback));
        OnPropertyChanged(nameof(GenerationMilestones));
    }

    private async Task SubmitGenerationCoreAsync(long submittingEpoch)
    {
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

        if (!IsCurrent(submittingEpoch)) return;
        Status = null;
        var result = await textSubmission.SubmitAsync(Draft!, default);
        if (!IsCurrent(submittingEpoch)) return;
        if (result.IsSuccess)
        {
            submittedWorkRequest = result.Value.RequestId;
            Status = null;
            generationFeedback = Strings.Resolve("AiPrompt_WorkSubmitted");
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
    }

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

public sealed record GenerationMilestone(string Label, bool Reached, bool IsCurrent, bool HasPrevious, bool HasNext);
