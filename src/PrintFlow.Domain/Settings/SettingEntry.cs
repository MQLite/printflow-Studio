using System.Globalization;

namespace PrintFlow.Domain.Settings;

/// <summary>
/// One persisted setting: a <see cref="SettingKey"/> and its value, as stable English text
/// (Jira 11108; MVP design §17.6).
/// </summary>
/// <remarks>
/// Text, because that is what the <c>Setting</c> table stores and because every persisted value
/// in this database is culture-invariant stable English (MVP design §13.4). The typed factories
/// and readers below exist so that no caller ever formats or parses one by hand — an integer
/// setting written under the operator's culture and read back under another is exactly the class
/// of defect a typed seam removes.
/// <para>
/// Absent is a distinct answer from present-and-empty: a reader returns null for "no row", so a
/// caller can always fall back to its existing default rather than to a blank value.
/// </para>
/// </remarks>
public sealed record SettingEntry(SettingKey Key, string Value)
{
    /// <summary>A textual setting.</summary>
    public static SettingEntry Text(SettingKey key, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new SettingEntry(key, value);
    }

    /// <summary>An integer setting, written culture-invariantly.</summary>
    public static SettingEntry Integer(SettingKey key, int value) =>
        new(key, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>A boolean setting, written as the stable English <c>true</c>/<c>false</c>.</summary>
    public static SettingEntry Boolean(SettingKey key, bool value) =>
        new(key, value ? "true" : "false");

    /// <summary>Reads this setting as an integer, or null when its text is not one.</summary>
    public int? AsInteger() =>
        int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : null;

    /// <summary>Reads this setting as a boolean, or null when its text is neither value.</summary>
    public bool? AsBoolean() => Value switch
    {
        "true" => true,
        "false" => false,
        _ => null,
    };
}
