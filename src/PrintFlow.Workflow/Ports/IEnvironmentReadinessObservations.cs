namespace PrintFlow.Workflow.Ports;

/// <summary>
/// One-way, in-process publication of already performed authoritative observations.
/// This port neither verifies nor authorises anything; consumers must never affect the source.
/// </summary>
public interface IEnvironmentReadinessObservations
{
    long Begin();
    void Complete(long ticket, EnvironmentReadinessReport report);
    void Abandon(long ticket);
}
