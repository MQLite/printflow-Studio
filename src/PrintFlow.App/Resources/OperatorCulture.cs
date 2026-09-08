using System.Globalization;

namespace PrintFlow.App.Resources;

/// <summary>
/// The culture every operator-visible string resolves against (SCRUM-11119).
/// </summary>
/// <remarks>
/// <b>Why this is not simply <see cref="CultureInfo.CurrentUICulture"/>.</b> That property is
/// carried by <c>ExecutionContext</c>: a value assigned inside an <c>async</c> continuation is
/// restored to the caller's when the continuation's context is popped. The Settings screen
/// applies the language after awaiting a database write, so relying on the ambient property
/// would give a switch that appears to work and silently reverts — the worst possible failure
/// for something whose whole requirement is "no restart".
/// <para>
/// So the selected culture is held explicitly, in one place, by the one authority allowed to set
/// it: <c>ILocalisationService</c>. Nothing else assigns it, and the fallback until it has been
/// selected is the ambient culture, which is exactly the behaviour every build before this one
/// had.
/// </para>
/// <para>
/// <c>volatile</c> because the value is written on whichever thread applied the language and
/// read on every thread that renders a string — the shell's UI thread, and the background
/// threads a screen takes its readings on.
/// </para>
/// </remarks>
internal static class OperatorCulture
{
    private static volatile CultureInfo? _selected;

    /// <summary>The selected culture, or the ambient one until a language has been selected.</summary>
    internal static CultureInfo Current => _selected ?? CultureInfo.CurrentUICulture;

    /// <summary>
    /// Selects the culture strings resolve against. Null returns to the ambient culture.
    /// </summary>
    /// <remarks>
    /// Null is not a Product state — the application selects a culture during startup and never
    /// unselects — but a test that switched the language must be able to put the process back as
    /// it found it, and a reset is the honest way to do that.
    /// </remarks>
    internal static void Select(CultureInfo? culture) => _selected = culture;
}
