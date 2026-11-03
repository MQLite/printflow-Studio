namespace PrintFlow.App.Navigation;

public enum AiDraftLeaveChoice { Save, Discard, Cancel }

public interface IAiDraftLeavePrompt
{
    Task<AiDraftLeaveChoice> ChooseAsync(CancellationToken ct);
}
