using System.Runtime.InteropServices;
using PrintFlow.App.Resources;
using PrintFlow.Domain.AiDesign.Authoring;
using PrintFlow.Workflow.AiDesign;
using PrintFlow.Workflow.AiDesign.Authoring;

namespace PrintFlow.App.ViewModels.AiDesign;

public sealed partial class AiPromptWorkbenchViewModel
{
    private async Task ComposeAsync(bool preserveManual, CancellationToken ct) => await RunAsync(async () =>
    {
        if ((await FlushCoreAsync(ct, saveLayout: false)).IsFailure) return;
        if (Draft!.IsManuallyEdited && !preserveManual) { IsChangePending = true; return; }
        long capturedEpoch = epoch, generation = editGeneration;
        var result = await ApplyCoreAsync(new AiAuthoringEdit.Recompose(preserveManual), ct);
        if (result.IsSuccess && IsCurrent(capturedEpoch) && generation == editGeneration) await PresentAsync(result.Value, ct);
        ClearPending();
    }, ct);

    private async Task CopyAsync() => await RunAsync(async () =>
    {
        if (HasOutdatedMemorialSpecification) { Error = L("OutdatedPromptSpecification"); return; }
        if ((await FlushCoreAsync(default, saveLayout: false)).IsFailure) return;
        try { clipboard.SetText(module.Export(Draft!).Text); Status = Strings.AiPrompt_Copied; }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
        { Error = Strings.AiPrompt_CopyFailed; }
    }, default);

    private async Task PickSlotImageAsync(string? slotId)
    {
        if (slotId is null || (await FlushAsync(default, saveLayout: false)).IsFailure || Draft is null) return;
        var selectedDraft = Draft; long capturedEpoch = epoch, generation = editGeneration;
        if (!selectedDraft.Slots.Any(x => x.SlotId == slotId) || !selectedDraft.FinalDocument.Parts.OfType<AiImagePart>().Any(x => x.SlotId == slotId)) return;
        string? path = picker.PickSingleFile(Strings.AiPrompt_ChoosePhoto, "Images|*.png;*.jpg;*.jpeg;*.webp");
        if (path is null) return;
        await RunAsync(async () =>
        {
            if (!IsCurrent(capturedEpoch) || Draft.Id != selectedDraft.Id || Draft.Revision != selectedDraft.Revision || generation != editGeneration) return;
            var result = await ApplyCoreAsync(new AiAuthoringEdit.BindImage(slotId, new SelectedAiDesignFile(path, "")), default);
            if (result.IsSuccess && IsCurrent(capturedEpoch) && generation == editGeneration) await PresentAsync(result.Value, default);
        }, default);
    }

    private async Task EditAsync(AiAuthoringEdit edit) => await RunAsync(async () =>
    {
        if ((await FlushCoreAsync(default, saveLayout: false)).IsFailure) return;
        long capturedEpoch = epoch, generation = editGeneration;
        var result = await ApplyCoreAsync(edit, default);
        if (result.IsSuccess && IsCurrent(capturedEpoch) && generation == editGeneration) await PresentAsync(result.Value, default);
    }, default);

    public async Task SelectTemplateAsync(AiTemplateRef template, bool preserveManual, CancellationToken ct) => await RunAsync(async () =>
    {
        if ((await FlushCoreAsync(ct)).IsFailure || Draft!.Template == template) return;
        if (Draft.IsManuallyEdited && !preserveManual) { pendingTemplate = template; pendingRestore = null; IsChangePending = true; return; }
        long capturedEpoch = epoch, generation = editGeneration;
        var result = await ApplyCoreAsync(new AiAuthoringEdit.SelectTemplate(template), ct);
        if (result.IsSuccess && IsCurrent(capturedEpoch) && generation == editGeneration) await PresentAsync(result.Value, ct);
        ClearPending();
    }, ct);

    private async Task ConfirmChangeAsync()
    {
        if (!IsChangePending) return;
        if (pendingRestore is { } revision) await RestoreHistoryAsync(revision, default);
        else if (pendingTemplate is { } template) await SelectTemplateAsync(template, true, default);
        else await ComposeAsync(true, default);
    }
    public async Task RefreshHistoryAsync(CancellationToken ct) => await RunAsync(async () =>
    {
        if ((await FlushCoreAsync(ct)).IsFailure) return;
        var history = await module.ReadHistoryAsync(Draft!.Id, ct);
        if (history.IsFailure) { Error = Strings.AiPrompt_SaveFailed; return; }
        History = history.Value; OnPropertyChanged(nameof(History));
    }, ct);
    public async Task RestoreHistoryAsync(long revision, CancellationToken ct) => await RunAsync(async () =>
    {
        if ((await FlushCoreAsync(ct)).IsFailure) return;
        long capturedEpoch = epoch, generation = editGeneration;
        var result = await ApplyCoreAsync(new AiAuthoringEdit.RestoreRevision(revision), ct);
        if (result.IsSuccess && IsCurrent(capturedEpoch) && generation == editGeneration) await PresentAsync(result.Value, ct);
        ClearPending();
    }, ct);
}
