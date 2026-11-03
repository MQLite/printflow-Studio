namespace PrintFlow.App.Navigation;

public interface IAiAuthoringCloseGuard
{
    Task<bool> RequestCloseAsync(CancellationToken ct);
}
