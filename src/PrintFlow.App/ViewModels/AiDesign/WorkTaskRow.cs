using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PrintFlow.App.Resources;
using PrintFlow.Domain.AiDesign;
using PrintFlow.Domain.Files;
using PrintFlow.Workflow.AiDesign;
using PrintFlow.Workflow.Services;

namespace PrintFlow.App.ViewModels.AiDesign;

public sealed class WorkTaskRow(WorkTaskSnapshot snapshot) : ObservableObject
{
    public WorkTaskSnapshot Snapshot { get; } = snapshot;
    public string Title => Snapshot.Title ?? Strings.Resolve("WorkTask_Title");
    public string Provider => Snapshot.Provider == AiDesignProvider.OpenAiApi ? "OpenAI API" : "ChatGPT Work";
    public string Stage => Strings.Resolve(Snapshot.Failure == WorkTaskFailure.UploadFailed ? "WorkTask_UploadFailed" : "WorkTask_" + Snapshot.Stage);
    public string Updated => Snapshot.UpdatedAtUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    public bool CanContinue => Snapshot.Stage == WorkTaskStage.Imported && Snapshot.ImportedSessionId is not null;
    public bool CanViewWork => Snapshot.WorkConversationId is { } id && id != Guid.Empty;
    public bool CanRegenerate => CanContinue && CanViewWork;
    public bool HasOpenAiReceipt => Snapshot.OpenAiReceipt is not null;
    public bool HasCloudPdf => CanContinue && Snapshot.CloudDesignAttempts > 0;
    public string? CloudReviewDetails => HasCloudPdf
        ? string.Format(CultureInfo.CurrentCulture, Strings.Resolve("AiPrompt_CloudReviewPassed"), Snapshot.CloudDesignAttempts)
        : null;
    public string? OpenAiResponseDetails => Snapshot.OpenAiReceipt is { } receipt
        ? receipt.TotalTokens is { } total
            ? string.Format(CultureInfo.CurrentCulture, Strings.Resolve("AiPrompt_OpenAiResponseUsage"), receipt.ResponseId, total)
            : receipt.ResponseId
        : null;
    public string Placeholder => Strings.Resolve(CanContinue ? "WorkTask_NoPreview" : "WorkTask_Protected");
    public string? ImageFacts => Snapshot.Format == ImageFormat.Pdf ? "PDF" :
        Snapshot.Format is { } format && Snapshot.PixelWidth is { } width && Snapshot.PixelHeight is { } height
            ? $"{format.ToString().ToUpperInvariant()} · {width} × {height}" : null;
    public string? Problem => Snapshot.Failure is { } failure ? Strings.Resolve(failure switch
    {
        WorkTaskFailure.UploadFailed => "AiPrompt_WorkUploadFailed",
        WorkTaskFailure.Setup => "AiPrompt_WorkSetup",
        WorkTaskFailure.ReceiverUnavailable => "AiPrompt_WorkReceiverUnavailable",
        WorkTaskFailure.TimedOut => "WorkTask_TimedOut",
        WorkTaskFailure.ValidationFailed => "WorkTask_ValidationFailed",
        WorkTaskFailure.ResultUnavailable => "WorkTask_ResultUnavailable",
        WorkTaskFailure.RecoveryDeferred => "WorkTask_RecoveryDeferred",
        WorkTaskFailure.ImageProcessingSettings => "WorkTask_RepairImageProcessingSettings",
        _ => "AiPrompt_WorkUnconfirmed"
    }) : null;

    private ReadOnlyMemory<byte> thumbnail;
    public ReadOnlyMemory<byte> Thumbnail => thumbnail;
    public bool HasThumbnail => !thumbnail.IsEmpty;
    public void SetThumbnail(ReadOnlyMemory<byte> pixels)
    {
        if (SetProperty(ref thumbnail, pixels, nameof(Thumbnail))) OnPropertyChanged(nameof(HasThumbnail));
    }

    internal async Task LoadThumbnailAsync(IArtefactPreviewService previews, CancellationToken ct)
    {
        if (!CanContinue || Snapshot.ImportedSessionId is not { } id) return;
        try
        {
            var result = await previews.GetRecentThumbnailAsync(id, ct);
            if (!ct.IsCancellationRequested && result.IsSuccess) SetThumbnail(result.Value.Payload);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    internal bool OpenWork(IWorkConversationLauncher? launcher) =>
        Snapshot.WorkConversationId is { } id && id != Guid.Empty && launcher?.Open(id) == true;
}
