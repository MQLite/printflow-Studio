namespace PrintFlow.App.Settings;

/// <summary>
/// The configured fallbacks a Settings value falls back to when the operator has never set it
/// (SCRUM-11118).
/// </summary>
/// <remarks>
/// This is the middle rung of the precedence SCRUM-11118 decides for <b>operator preferences</b>:
/// the persisted <c>Setting</c> row wins; with no row, <c>appsettings.json</c> answers; with
/// neither, the hard Product constant does. It carries only the values <c>appsettings.json</c>
/// actually states — the trim safety margin has no configured rung, because zero (crop exactly
/// to the alpha content) is a Product constant and not an installation choice.
/// <para>
/// It carries nothing about the production contract. Production DPI, the workstation preset,
/// the accepted output root and the Photoshop colour settings are facts of the verified preset,
/// are read from the environment verification authority at display time, and are never
/// defaulted, configured or persisted here.
/// </para>
/// </remarks>
/// <param name="LogRetentionDays">
/// <c>Logging:RetentionDays</c> from <c>appsettings.json</c>. Displayed and editable on
/// Settings; the cleanup that enforces it is SCRUM-11121 and is not in this slice.
/// </param>
public sealed record SettingsDefaults(int LogRetentionDays)
{
    /// <summary>The Product constant, used when configuration states nothing usable.</summary>
    public const int FallbackLogRetentionDays = 30;

    /// <summary>
    /// The trim safety margin a session starts with when nothing is persisted: zero pixels,
    /// which is <c>TrimMargin.Tight</c> — the behaviour every trim has had since Epic 11200
    /// Part B.
    /// </summary>
    public const int FallbackTrimSafetyMarginPixels = 0;

    /// <summary>The largest retention period Settings will accept, so the field is bounded.</summary>
    public const int MaximumLogRetentionDays = 3650;
}
