using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Resources;
using PrintFlow.App.Navigation;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Workflow.AiDesign;

namespace PrintFlow.App.ViewModels.AiDesign;

public sealed partial class AiPromptWorkbenchViewModel
{
    private Guid? submittedWorkRequest;
    private bool generationStarted;
    private bool submittingGeneration;
    private bool generationUsesOpenAi;
    private string? generationFeedback;
    public string AutoGenerateLabel => Strings.Resolve("AiPrompt_AutoGenerate");
    public string GenerationProviderLabel => Strings.Resolve("AiPrompt_GenerationProvider");
    public IReadOnlyList<AiDesignGenerationOption> GenerationOptions =>
    [
        new(AiDesignProvider.ChatGptWork, "ChatGPT Work"),
        new(AiDesignProvider.OpenAiApi, apiGeneration?.UsesCloudReview == true
            ? Strings.Resolve("AiPrompt_CloudProvider") : "OpenAI API")
    ];
    public AiDesignProvider GenerationProvider { get; set; }
    public string CancelOpenAiGenerationLabel => Strings.Resolve("AiPrompt_OpenAiCancel");
    public bool CanCancelOpenAiGeneration => submittingGeneration && generationUsesOpenAi;
    public IRelayCommand CancelOpenAiGenerationCommand { get; }
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
                WorkTaskStage.PortraitReview => 2,
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
            if (snapshot?.Provider == AiDesignProvider.OpenAiApi || snapshot is null && generationUsesOpenAi)
                labels = stage == WorkTaskStage.PortraitReview ? ["AiPrompt_OpenAiGenerate", "AiPrompt_GenerationImport"] :
                    ["AiPrompt_OpenAiGenerate", "AiPrompt_GenerationVerify", "AiPrompt_GenerationImport"];

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
    private async Task GenerateAsync(CancellationToken ct) => await RunAsync(async () =>
    {
        if (!active || Draft is null || IsWaitingForWorkResult || isLoadingWorkCard || workCardUnavailable) return;
        if (referencePath is not null)
        {
            Error = ReferencePendingHint;
            return;
        }
        bool useApi = GenerationProvider == AiDesignProvider.OpenAiApi;
        if (useApi && !ConfirmOpenAiGeneration()) return;

        long submittingEpoch = epoch;
        var previousResult = WorkCard is { CanContinue: true } imported ? imported : null;
        generationStarted = true;
        submittingGeneration = true;
        generationUsesOpenAi = useApi;
        generationFeedback = Strings.Resolve(useApi
            ? apiGeneration?.UsesCloudReview == true ? "AiPrompt_CloudWorking" : "AiPrompt_OpenAiGenerating"
            : "AiPrompt_WorkConnecting");
        submittedWorkRequest = null;
        WorkCard = null;
        NotifyGenerationProgress();
        try { await SubmitGenerationCoreAsync(submittingEpoch, useApi, ct); }
        finally
        {
            if (IsCurrent(submittingEpoch))
            {
                submittingGeneration = false;
                if (Error is not null)
                {
                    generationFeedback = Error;
                    if (submittedWorkRequest is null && previousResult is not null)
                    {
                        submittedWorkRequest = previousResult.Snapshot.RequestId;
                        WorkCard = previousResult;
                    }
                }
                NotifyGenerationProgress();
            }
        }
    }, default);

    private void NotifyGenerationProgress()
    {
        OnPropertyChanged(nameof(ShowGenerationProgress));
        OnPropertyChanged(nameof(GenerationFeedback));
        OnPropertyChanged(nameof(GenerationMilestones));
        OnPropertyChanged(nameof(CanCancelOpenAiGeneration));
    }

    private bool ConfirmOpenAiGeneration()
    {
        if (apiGeneration is null || apiConsent is null)
        {
            Error = Strings.Resolve("AiPrompt_OpenAiUnavailable");
            return false;
        }
        if (apiGeneration.GetUnavailable() is { } failure)
        {
            Error = Strings.Resolve(failure.MessageKey);
            return false;
        }
        return apiConsent.Confirm(apiGeneration.UsesCloudReview);
    }

    private async Task SubmitGenerationCoreAsync(long submittingEpoch, bool useApi, CancellationToken ct)
    {
        if (!useApi && textSubmission is null)
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
        if (useApi)
        {
            var generated = await apiGeneration!.GenerateAsync(Draft!, ct);
            if (!IsCurrent(submittingEpoch)) return;
            if (generated.IsFailure)
            {
                var failure = generated.Failure;
                Error = failure.Context.TryGetValue(CloudDesignLimits.ReviewReasonKey, out var reason)
                    ? string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Resolve(failure.MessageKey), reason)
                    : Strings.Resolve(failure.MessageKey);
                return;
            }
            await ShowSubmittedResultAsync(generated.Value, "WorkTask_Imported");
            return;
        }

        var result = await textSubmission!.SubmitAsync(Draft!, default);
        if (!IsCurrent(submittingEpoch)) return;
        if (result.IsSuccess)
        {
            await ShowSubmittedResultAsync(result.Value.RequestId, "AiPrompt_WorkSubmitted");
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
            "WorkTunnelUnavailable" => "AiPrompt_WorkTunnelUnavailable",
            "WorkRuntimeStartupFailed" => "AiPrompt_WorkRuntimeStartupFailed",
            _ => "AiPrompt_WorkUnconfirmed"
        });
    }

    private async Task ShowSubmittedResultAsync(Guid requestId, string feedbackKey)
    {
        submittedWorkRequest = requestId;
        generationFeedback = Strings.Resolve(feedbackKey);
        QueueWorkCardRefresh();
        await WorkCardRefresh;
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
public sealed record AiDesignGenerationOption(AiDesignProvider Provider, string Label);
