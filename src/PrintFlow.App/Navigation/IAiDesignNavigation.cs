namespace PrintFlow.App.Navigation;

public interface IAiDesignNavigation
{
    Task GoToPromptWorkbenchAsync(Guid? draftId, CancellationToken ct);
}
