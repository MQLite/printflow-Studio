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
/// Which of these the operator may actually change, and what wins when a persisted value and
/// <c>appsettings.json</c> or the signed preset disagree, is SCRUM-11118's decision and is not
/// made here. This task supplies the storage; it wires no runtime precedence.
/// </para>
/// </remarks>
public enum SettingKey
{
    /// <summary>Operator UI language. Absent means "follow Windows", which is current behaviour.</summary>
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
