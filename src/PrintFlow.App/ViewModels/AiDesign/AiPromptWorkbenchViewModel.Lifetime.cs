using CommunityToolkit.Mvvm.Input;
using PrintFlow.App.Navigation;
using PrintFlow.App.Resources;
using PrintFlow.Domain.AiDesign.Authoring;
using System.Globalization;

namespace PrintFlow.App.ViewModels.AiDesign;

public sealed partial class AiPromptWorkbenchViewModel
{
    private readonly IAiDraftLeavePrompt? leavePrompt;
    private readonly INavigationService? navigation;
    private Task<bool>? closeTask;
    private bool isClosing;
    public string ReturnHomeLabel => Strings.Resolve("AiPrompt_ReturnHome");
    public IAsyncRelayCommand ReturnHomeCommand { get; }
    public IReadOnlyList<AiSavedDraftChoice> SavedDrafts { get; private set; } = [];
    public string SavedDraftsLabel => Strings.Resolve("AiPrompt_SavedDrafts");
    public string? SavedDraftsNotice { get; private set; }
    private bool savedDraftsVisible;
    public bool IsSavedDraftsVisible { get => savedDraftsVisible; private set => SetProperty(ref savedDraftsVisible, value); }
    public IAsyncRelayCommand ShowSavedDraftsCommand { get; }
    public IAsyncRelayCommand<AiSavedDraftChoice> OpenSavedDraftCommand { get; }
    public IAsyncRelayCommand CloseSavedDraftsCommand { get; }

    private async Task<bool> ListSavedDraftsCoreAsync(CancellationToken ct)
    {
        var saved = await module.ListAsync(ct);
        if (saved.IsFailure) { Error = Strings.Resolve("AiPrompt_DraftReadFailed"); return false; }
        SavedDrafts = saved.Value.Drafts.Select(entry =>
        {
            var value = entry.Draft;
            var nameField = value.TemplateSnapshot.Fields.FirstOrDefault(x => x.Semantic == AiTemplateSemantic.Name);
            string name = nameField is null ? "" : value.Fields.GetValueOrDefault(nameField.Key, "");
            string label = string.Format(CultureInfo.CurrentCulture, Strings.Resolve("AiPrompt_DraftLabel"), name,
                entry.SavedAtUtc.ToLocalTime(), value.Id.ToString("N")[..8]);
            return new AiSavedDraftChoice(value.Id, value.TemplateSnapshot.Name,
                string.Concat(value.FinalDocument.Parts.OfType<AiTextPart>().Select(p => p.Text)).TruncatePreview(), label);
        }).ToArray();
        SavedDraftsNotice = saved.Value.UnavailableCount > 0
            ? string.Format(CultureInfo.CurrentCulture, Strings.Resolve("AiPrompt_DraftsPartial"), saved.Value.UnavailableCount)
            : SavedDrafts.Count == 0 ? Strings.Resolve("AiPrompt_DraftsEmpty") : null;
        OnPropertyChanged(nameof(SavedDrafts)); OnPropertyChanged(nameof(SavedDraftsNotice));
        IsSavedDraftsVisible = true; return true;
    }

    private async Task ShowRecoveryAfterOpenFailureAsync(CancellationToken ct)
    {
        await ListSavedDraftsCoreAsync(ct);
        Error = Strings.Resolve("AiPrompt_NewDraftFailed");
    }

    private async Task OpenSavedDraftAsync(AiSavedDraftChoice? value)
    {
        if (value is null || !await RequestCloseAsync(default)) return;
        await RunAsync(async () =>
        {
            var read = await module.ReadAsync(value.Id, default);
            if (read.IsFailure) { Error = Strings.Resolve("AiPrompt_DraftReadFailed"); return; }
            epoch++; ClearPending(); TemplateEditor = null; History = []; OnPropertyChanged(nameof(History));
            IsSavedDraftsVisible = false; Status = null;
            await PresentAsync(read.Value, default);
        }, default);
    }

    // Share the entire decision and publication, including a pending modal prompt.
    public Task<bool> RequestCloseAsync(CancellationToken ct) => closeTask is { IsCompleted: false }
        ? closeTask : closeTask = RequestCloseCoreAsync(ct);

    private async Task<bool> RequestCloseCoreAsync(CancellationToken ct)
    {
        bool allowed = false;
        isClosing = true; OnPropertyChanged(nameof(CanEdit));
        try
        {
            await RunAsync(async () =>
            {
                ct.ThrowIfCancellationRequested();
                if (!active || TemplateEditor?.IsBusy == true) return;
                if (!HasUnsavedChanges && TemplateEditor?.HasUnsavedChanges != true) { allowed = true; return; }
                long generation = editGeneration;
                var editor = TemplateEditor; long templateGeneration = editor?.EditGeneration ?? 0;
                var choice = leavePrompt is null ? AiDraftLeaveChoice.Cancel : await leavePrompt.ChooseAsync(ct);
                ct.ThrowIfCancellationRequested();
                if (choice == AiDraftLeaveChoice.Cancel || generation != editGeneration ||
                    editor != TemplateEditor || templateGeneration != (editor?.EditGeneration ?? 0)) return;
                if (choice == AiDraftLeaveChoice.Discard) { allowed = true; return; }
                if (choice != AiDraftLeaveChoice.Save) return;
                if ((await FlushCoreAsync(ct)).IsFailure) return;
                if (editor?.HasUnsavedChanges == true)
                {
                    await editor.SaveCommand.ExecuteAsync(null);
                    if (editor.HasUnsavedChanges) { Error = Strings.AiPrompt_TemplateSaveFailed; return; }
                }
                allowed = !HasUnsavedChanges;
            }, ct);
            return allowed;
        }
        finally { isClosing = false; OnPropertyChanged(nameof(CanEdit)); }
    }
}

public sealed record AiSavedDraftChoice(Guid Id, string Name, string TextPreview, string DisplayLabel = "");

internal static class AiSavedDraftPreview
{
    public static string TruncatePreview(this string text) => text.Length <= 160 ? text : text[..160] + "…";
}
