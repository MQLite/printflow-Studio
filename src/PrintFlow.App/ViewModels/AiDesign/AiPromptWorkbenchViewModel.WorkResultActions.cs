using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Navigation;
using PrintFlow.App.Resources;
using PrintFlow.Workflow.AiDesign;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels.AiDesign;

public sealed partial class AiPromptWorkbenchViewModel
{
    private int workPreviewVersion;
    private bool showWorkPreview;
    private ImagePreview? workPreview;
    private string? workPreviewNotice;
    private WorkTaskRow? followUpSource;
    private string followUpInstructions = "";
    private string? followUpFeedback;

    public IAsyncRelayCommand OpenWorkResultPreviewCommand { get; }
    public IRelayCommand CloseWorkResultPreviewCommand { get; }
    public IRelayCommand CopyOpenAiResponseIdCommand { get; }
    public string CopyOpenAiResponseIdLabel => Strings.Resolve("AiPrompt_OpenAiCopyResponseId");
    private readonly ICloudDesignPdfExport? pdfExport;
    private readonly ICloudDesignPdfSavePicker? pdfPicker;
    public IAsyncRelayCommand SaveCloudPdfCommand { get; }
    public string SaveCloudPdfLabel => Strings.Resolve("AiPrompt_SaveCloudPdf");

    private async Task SaveCloudPdfAsync(CancellationToken ct)
    {
        if (!active || WorkCard is not { HasCloudPdf: true } card || pdfExport is null ||
            pdfPicker?.PickDestination() is not { } destination) return;
        var result = await pdfExport.SaveAsync(card.Snapshot.RequestId, destination, ct);
        if (!active || ct.IsCancellationRequested) return;
        if (result.IsSuccess) Status = Strings.Resolve("AiPrompt_CloudPdfSaved");
        else Error = Strings.Resolve("AiPrompt_CloudPdfSaveFailed");
    }

    private void CopyOpenAiResponseId()
    {
        if (WorkCard?.Snapshot.OpenAiReceipt is not { } receipt) return;
        try
        {
            clipboard.SetText(receipt.ResponseId);
            Status = Strings.Resolve("AiPrompt_OpenAiResponseIdCopied");
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or InvalidOperationException)
        {
            Error = Strings.AiPrompt_CopyFailed;
        }
    }
    public IAsyncRelayCommand RegenerateInWorkCommand { get; }
    public IAsyncRelayCommand SubmitFollowUpCommand { get; }
    public IRelayCommand CloseFollowUpEditorCommand { get; }
    public string OpenWorkResultPreviewLabel => Strings.Resolve("WorkTask_Preview");
    public string RegenerateInWorkLabel => Strings.Resolve("WorkTask_Regenerate");
    public string WorkPreviewTitle => Strings.Resolve("WorkTask_PreviewTitle");
    public string FollowUpPrefix => Strings.Resolve("WorkTask_FollowUpPrefix");
    public string SubmitFollowUpLabel => Strings.Resolve("WorkTask_FollowUpSubmit");
    public bool ShowFollowUpEditor => followUpSource is not null;
    public string FollowUpInstructions
    {
        get => followUpInstructions;
        set
        {
            if (SetProperty(ref followUpInstructions, value)) SubmitFollowUpCommand.NotifyCanExecuteChanged();
        }
    }
    public string? FollowUpFeedback { get => followUpFeedback; private set => SetProperty(ref followUpFeedback, value); }
    public bool ShowWorkPreview { get => showWorkPreview; private set => SetProperty(ref showWorkPreview, value); }
    public ImagePreview? WorkPreview { get => workPreview; private set => SetProperty(ref workPreview, value); }
    public string? WorkPreviewNotice { get => workPreviewNotice; private set => SetProperty(ref workPreviewNotice, value); }

    private async Task OpenWorkResultPreviewAsync(CancellationToken ct)
    {
        if (!active || workPreviews is null || WorkCard is not { CanContinue: true } card ||
            card.Snapshot.ImportedSessionId is not { } session) return;
        ShowWorkPreview = true;
        await LoadWorkPreviewAsync(session, ct);
    }

    private async Task LoadWorkPreviewAsync(PrintFlow.Domain.Ids.SessionId session, CancellationToken ct)
    {
        int version = ++workPreviewVersion;
        WorkPreview = null;
        WorkPreviewNotice = Strings.Resolve("WorkTask_PreviewLoading");
        if (workPreviews is null)
        {
            WorkPreviewNotice = Strings.Resolve("WorkTask_PreviewFailed");
            return;
        }
        try
        {
            var previewResult = await workPreviews.GetImportedOriginalPreviewAsync(session, ct);
            if (ct.IsCancellationRequested || version != workPreviewVersion) return;
            WorkPreview = previewResult.IsSuccess ? previewResult.Value : null;
            WorkPreviewNotice = previewResult.IsSuccess ? null : Strings.Resolve("WorkTask_PreviewFailed");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private void CloseWorkPreview()
    {
        workPreviewVersion++;
        OpenWorkResultPreviewCommand.Cancel();
        ShowWorkPreview = false;
        WorkPreview = null;
        WorkPreviewNotice = null;
    }

    private async Task RegenerateInWorkAsync(CancellationToken ct)
    {
        if (!active || ShowFollowUpEditor || WorkCard is not { CanRegenerate: true } card ||
            card.Snapshot.ImportedSessionId is not { } session || textSubmission is null) return;
        CloseWorkPreview();
        followUpSource = card;
        FollowUpInstructions = "";
        FollowUpFeedback = null;
        OnPropertyChanged(nameof(ShowFollowUpEditor));
        await LoadWorkPreviewAsync(session, ct);
    }

    private void CloseFollowUpEditor()
    {
        if (followUpSource is null) return;
        followUpSource = null;
        RegenerateInWorkCommand.Cancel();
        CloseWorkPreview();
        FollowUpInstructions = "";
        FollowUpFeedback = null;
        OnPropertyChanged(nameof(ShowFollowUpEditor));
    }

    private bool CanSubmitFollowUp() => ShowFollowUpEditor && !string.IsNullOrWhiteSpace(FollowUpInstructions);

    private async Task SubmitFollowUpAsync() => await RunAsync(async () =>
    {
        if (!active || !CanSubmitFollowUp() || followUpSource is not { } card || textSubmission is null) return;
        long capturedEpoch = epoch;
        FollowUpFeedback = Strings.Resolve("AiPrompt_WorkConnecting");
        var result = await textSubmission.SubmitFollowUpAsync(card.Snapshot.RequestId,
            $"{FollowUpPrefix}\n{FollowUpInstructions.Trim()}", default);
        if (!IsCurrent(capturedEpoch)) return;
        if (result.IsFailure)
        {
            FollowUpFeedback = Strings.Resolve(result.Failure.TechnicalDetail switch
            {
                "WorkRequestActive" => "AiPrompt_WorkActive",
                "WorkReceiverUnavailable" => "AiPrompt_WorkReceiverUnavailable",
                "WorkTunnelUnavailable" => "AiPrompt_WorkTunnelUnavailable",
                "WorkRuntimeStartupFailed" => "AiPrompt_WorkRuntimeStartupFailed",
                "WorkFollowUpBridgeUnavailable" => "WorkTask_FollowUpBridgeUnavailable",
                "WorkResultPromptTooLong" or "WorkPromptRejected" => "WorkTask_FollowUpTooLong",
                _ => "WorkTask_RegenerateFailed"
            });
            return;
        }

        submittedWorkRequest = result.Value.RequestId;
        generationStarted = true;
        generationFeedback = Strings.Resolve("AiPrompt_WorkSubmitted");
        CloseFollowUpEditor();
        NotifyGenerationProgress();
        QueueWorkCardRefresh();
        await WorkCardRefresh;
    }, default);
}
