namespace PrintFlow.Infrastructure.Adapters.Photoshop;

/// <summary>
/// The timings the Photoshop automation foundation uses (Epic 11400 Part A §6).
/// </summary>
/// <remarks>
/// Every timeout is bounded and every wait is a poll, never a single fixed sleep: "wait N
/// seconds and assume" is not a continuation condition anywhere in this solution, and a poll
/// that observes the real state is also what lets cancellation take effect promptly.
///
/// There is deliberately no control name, control id, path or keystroke here. Those all come
/// from the signed baseline, so this record cannot become a back door for configuring what
/// PrintFlow presses.
/// </remarks>
public sealed record PhotoshopAutomationOptions
{
    /// <summary>How long a launched Photoshop has to reach an identifiable window in a recognised state.</summary>
    /// <remarks>
    /// Generous because Photoshop CC 2019 is genuinely slow to start on a cold cache — it
    /// presents a window well before the start screen it hosts has finished loading, and the
    /// classifier will not accept the window until it has.
    /// </remarks>
    public TimeSpan LaunchTimeout { get; init; } = TimeSpan.FromSeconds(120);

    /// <summary>How long an already-running Photoshop has to present an identifiable window.</summary>
    public TimeSpan AttachTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>How long to wait for the Open dialog after asking Photoshop to raise one.</summary>
    public TimeSpan DialogTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>How long to wait for the identity surface after asking Photoshop to raise one.</summary>
    public TimeSpan IdentityDialogTimeout { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>How long to wait for the managed document to appear after the Open is confirmed.</summary>
    public TimeSpan OpenConfirmationTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>How long to allow a raised dialog to close after its Cancel is pressed.</summary>
    public TimeSpan DialogCloseTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>How long to allow for a requested foreground change to take effect.</summary>
    public TimeSpan ActivationTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>The interval between observations while polling.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Maximum time for the one synchronous TIFF save to become stable and fully readable.</summary>
    public TimeSpan TiffSettleTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>How many distinct visible child classes one state observation reads.</summary>
    /// <remarks>
    /// A loaded Photoshop editor showed sixty-four visible children resolving to roughly a dozen
    /// distinct classes; two hundred is generous headroom that still bounds the walk.
    /// </remarks>
    public int ClassSnapshotLimit { get; init; } = 200;
}
