namespace PrintFlow.Domain.Settings;

/// <summary>
/// The closed vocabulary of persisted settings (Jira 11108; MVP design §17.6 — "Setting stores
/// UI language, default output directory, production DPI, safety margin, fixed-environment
/// details, colour-settings confirmation, and log retention").
/// </summary>
/// <remarks>
/// An enum rather than free-text keys, for the reason every other identifier in this codebase is
/// typed: a key/value store with an open key space is a store whose contents nothing can
/// enumerate, validate or migrate. These seven values are the design's list — the same list
/// SCRUM-11118 will offer the operator — and nothing else is storable.
/// <para>
/// Values are persisted <b>by name</b>, exactly like <c>FailureCode</c> and <c>StepKind</c>, so a
/// member may be added but never renamed or renumbered (MVP design §13.4).
/// </para>
/// <para>
/// <b>SCRUM-11118 made the precedence decision this vocabulary deferred, and it is not one
/// rule.</b> Three of these are <i>operator preferences</i>: <see cref="UiLanguage"/>,
/// <see cref="TrimSafetyMarginPixels"/> and <see cref="LogRetentionDays"/>. For those, a
/// persisted row wins, then <c>appsettings.json</c>, then the hard Product constant, and the
/// operator may change them from the Settings screen.
/// </para>
/// <para>
/// The other four state <i>facts of the verified production preset</i>:
/// <see cref="DefaultOutputRoot"/>, <see cref="ProductionDpi"/>,
/// <see cref="WorkstationPresetDetails"/> and <see cref="PhotoshopColourSettingsConfirmed"/>.
/// The signed preset and the workstation verifier own all four, Settings displays them
/// read-only from that authority, and <b>PrintFlow writes no row for any of them</b>. A
/// persisted copy would be a second number able to disagree with the preset the outputs were
/// actually made under, which is exactly the disagreement a fixed-workstation product must not
/// be able to have. The members stay in the vocabulary because a persisted name is never
/// removed (MVP design §13.4), not because anything writes them.
/// </para>
/// </remarks>
public enum SettingKey
{
    /// <summary>
    /// Operator UI language, as a stable culture name (<c>zh-CN</c> / <c>en-US</c>).
    /// </summary>
    /// <remarks>
    /// Absent means "the operator has never chosen", which SCRUM-11119 answers with the
    /// Product default of Simplified Chinese — deliberately not with the workstation's Windows
    /// UI culture.
    /// </remarks>
    UiLanguage,

    /// <summary>Default output directory. Absent means the configured/preset root.</summary>
    DefaultOutputRoot,

    /// <summary>Fixed production DPI, as displayed to the operator.</summary>
    ProductionDpi,

    /// <summary>Default trim safety margin, in pixels.</summary>
    TrimSafetyMarginPixels,

    /// <summary>The fixed-environment (workstation preset) details shown in Settings.</summary>
    WorkstationPresetDetails,

    /// <summary>Whether the operator has confirmed the Photoshop colour settings.</summary>
    PhotoshopColourSettingsConfirmed,

    /// <summary>Local log retention, in days.</summary>
    LogRetentionDays,
}
