namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>
/// The timings and control names the Meitu automation foundation uses
/// (Epic 11300 Part A §14, §17).
/// </summary>
/// <remarks>
/// Every timeout here is bounded and every wait is a poll, never a single fixed sleep: MVP
/// design §11.5 rules out "wait N seconds and assume" as a continuation condition, and a poll
/// that observes the real state is also what lets cancellation take effect promptly.
///
/// <see cref="WelcomeOpenEntryName"/> is not a hard-coded UI assumption. It selects which of
/// the <i>signed</i> clean-start markers is the photo-editor entry, and
/// <c>GuardedMeituUiDriver</c> refuses to look for it unless the verified baseline actually
/// lists it — so a Meitu build that renames the entry produces a structured failure rather than
/// a click on whatever else happens to be there.
/// </remarks>
public sealed record MeituAutomationOptions
{
    /// <summary>How long a launched Meitu has to reach an identifiable window in a recognised state.</summary>
    public TimeSpan LaunchTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>How long an already-running Meitu has to present an identifiable window.</summary>
    public TimeSpan AttachTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>How long to wait for the file dialog after asking Meitu to open a file.</summary>
    public TimeSpan DialogTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>How long to wait for the working copy to become visible in Meitu after opening it.</summary>
    public TimeSpan OpenConfirmationTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The interval between observations while polling.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>How long to allow for a requested foreground change to take effect.</summary>
    public TimeSpan ActivationTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>The signed clean-start marker that opens the photo editor.</summary>
    public string WelcomeOpenEntryName { get; init; } = "图片编辑";

    /// <summary>
    /// The window class of the Windows common file dialog.
    /// </summary>
    /// <remarks>
    /// This is a Windows contract, not a Meitu one — which is why it can be named here without
    /// inventing a Meitu assumption. It is checked <i>in addition to</i> the dialog belonging to
    /// the verified Meitu process, never instead of it (§17).
    /// </remarks>
    public string FileDialogClassName { get; init; } = "#32770";

    /// <summary>The common-dialog automation id of the file-name edit.</summary>
    public string FileDialogFileNameAutomationId { get; init; } = "1148";

    /// <summary>The common-dialog automation id of the Open button.</summary>
    public string FileDialogOpenButtonAutomationId { get; init; } = "1";

    /// <summary>How many automation names one state observation reads.</summary>
    public int SnapshotItemLimit { get; init; } = 400;
}
