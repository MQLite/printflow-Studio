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

    /// <summary>How long Enhancement has to present its signed Busy state after being invoked.</summary>
    /// <remarks>
    /// Separate from the completion budget, and much shorter, because the two timeouts answer
    /// different questions. This one asks "did the control PrintFlow invoked actually start
    /// anything?" — and a control that produced no observable work within half a minute did not
    /// do what the evidence says it does. The other asks "how long may the work take?", which is
    /// a property of the image (Part B2A §17).
    /// </remarks>
    public TimeSpan EnhancementBusyTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a running Enhancement has to reach its signed completion state.</summary>
    public TimeSpan EnhancementCompletionTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>How long 抠图 has to expose its operation-specific Busy signature.</summary>
    public TimeSpan BackgroundRemovalBusyTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long a running 抠图 operation has to reach positive result controls.</summary>
    public TimeSpan BackgroundRemovalCompletionTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long to watch, read-only, for an Enhancement Meitu starts by itself after a document
    /// is opened (Epic 11300 Part B2B §20).
    /// </summary>
    /// <remarks>
    /// Meitu retains its selected module across document loads, so opening a file can start work
    /// with no PrintFlow input at all. Observed live, the unrequested run began within half a
    /// second of the document appearing; five seconds is ten polls of margin on that, and it is
    /// only ever spent when the module is already selected — a first reading that shows no module
    /// panel ends the watch immediately, because nothing can auto-start without one.
    ///
    /// Erring long is the safe direction here, and it is worth being explicit about why: this
    /// budget decides whether an auto-started enhancement is <i>seen</i>. Missing it does not
    /// cause a second enhancement — the pre-invoke guard still refuses over running work — but it
    /// turns a run that would have succeeded into a refusal.
    /// </remarks>
    public TimeSpan AutoEnhancementWatchTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long the controlled output has to appear and settle after the export is confirmed.</summary>
    /// <remarks>
    /// Separate from every UI timeout because it measures something else entirely: not whether a
    /// control responded, but how long Meitu takes to finish writing an upscaled PNG. The live
    /// 1.1 MB result was complete within a second; two minutes is the allowance for a production
    /// image several times larger on a busy machine.
    /// </remarks>
    public TimeSpan OutputStabilityTimeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>The interval between observations while waiting for the controlled output to settle.</summary>
    /// <remarks>
    /// Faster than <see cref="PollInterval"/> because it costs almost nothing — a file-system
    /// stat rather than an automation tree walk — and because the stability rule counts
    /// observations rather than seconds, so a shorter interval makes the same rule settle sooner
    /// without weakening it.
    /// </remarks>
    public TimeSpan OutputPollInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>The interval between observations while polling.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>How long to allow for a requested foreground change to take effect.</summary>
    public TimeSpan ActivationTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>The signed clean-start marker that titles the photo-editor card.</summary>
    /// <remarks>
    /// A marker, not a target. Part B1 uses this name only to <i>anchor</i> a structural walk:
    /// the element it matches is a title label, and what PrintFlow invokes is the card that owns
    /// it, resolved against the shape the signed evidence records (§3). The driver still refuses
    /// any name the signed clean-start evidence does not list.
    /// </remarks>
    public string WelcomeOpenEntryName { get; init; } = "图片编辑";

    /// <summary>How many automation names one state observation reads.</summary>
    public int SnapshotItemLimit { get; init; } = 400;
}
