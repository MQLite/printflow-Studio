namespace PrintFlow.App.Startup;

/// <summary>
/// The one place the shell reads what startup concluded (Epic 11100 Part 3C1 §6, §13).
/// </summary>
/// <remarks>
/// A tiny mutable holder exists because the service graph is composed <i>before</i> recovery
/// runs — recovery is resolved from that graph — so the status cannot be a registered instance.
/// Write-once and set only by the startup sequence: a view model can read it and nothing else.
/// </remarks>
public sealed class StartupStatusAccessor
{
    /// <summary>What startup concluded, or null while startup is still running.</summary>
    public StartupStatus? Status { get; private set; }

    /// <summary>Records the outcome. Called exactly once, by the startup sequence.</summary>
    public void Publish(StartupStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);

        if (Status is not null)
        {
            throw new InvalidOperationException(
                "The startup status has already been published. One startup produces one status.");
        }

        Status = status;
    }
}
