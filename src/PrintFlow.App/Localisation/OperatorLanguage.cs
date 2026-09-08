using System.Globalization;

namespace PrintFlow.App.Localisation;

/// <summary>
/// The operator-interface languages this MVP supports (SCRUM-11119).
/// </summary>
/// <remarks>
/// Exactly two, because the AC names exactly two and the accepted workstation preset records
/// one Windows UI culture. A third member would be a claim that some third
/// <c>Strings.&lt;culture&gt;.resx</c> exists and has been reviewed by someone who prints for a
/// living, which is a different piece of work from adding an enum member.
/// </remarks>
public enum OperatorLanguage
{
    /// <summary>Simplified Chinese — the Product's first-run default (SCRUM-11119).</summary>
    SimplifiedChinese,

    /// <summary>English.</summary>
    English,
}

/// <summary>
/// The mapping between <see cref="OperatorLanguage"/> and the stable culture names the
/// satellite resources and the persisted setting both use.
/// </summary>
/// <remarks>
/// The culture name is what is written to <c>Setting</c>, for the same reason a
/// <c>FailureCode</c> is persisted by name: it is stable English, it is already the resource
/// convention (<c>Strings.zh-CN.resx</c>), and it is the vocabulary the accepted preset itself
/// uses for <c>workstation.operatingSystem.uiCulture</c>.
/// <para>
/// <see cref="FirstRunDefault"/> is a Product constant and not an observation of the operating
/// system. Following Windows would make the shipped default a property of whichever machine
/// PrintFlow happens to start on, which is precisely what SCRUM-11119 asks it not to be.
/// </para>
/// </remarks>
public static class OperatorLanguages
{
    /// <summary>The culture the Simplified Chinese satellite resources are built for.</summary>
    public const string SimplifiedChineseCultureName = "zh-CN";

    /// <summary>The culture English is selected as. The neutral resources answer it.</summary>
    public const string EnglishCultureName = "en-US";

    /// <summary>
    /// What PrintFlow uses when the operator has never chosen: Simplified Chinese, always.
    /// </summary>
    public const OperatorLanguage FirstRunDefault = OperatorLanguage.SimplifiedChinese;

    /// <summary>Every supported language, in the order Settings offers them.</summary>
    public static IReadOnlyList<OperatorLanguage> All { get; } =
        [OperatorLanguage.SimplifiedChinese, OperatorLanguage.English];

    /// <summary>The stable culture name persisted and used to resolve resources.</summary>
    public static string CultureNameOf(OperatorLanguage language) => language switch
    {
        OperatorLanguage.SimplifiedChinese => SimplifiedChineseCultureName,
        OperatorLanguage.English => EnglishCultureName,
        _ => throw new ArgumentOutOfRangeException(
            nameof(language), language, "Unknown operator language."),
    };

    /// <summary>The <see cref="CultureInfo"/> resources are resolved through.</summary>
    public static CultureInfo CultureOf(OperatorLanguage language) =>
        CultureInfo.GetCultureInfo(CultureNameOf(language));

    /// <summary>
    /// Reads a persisted culture name back, or null when it names no supported language.
    /// </summary>
    /// <remarks>
    /// Null rather than a throw: a row written by a build that supported a third language must
    /// leave this one usable, and the caller answers it with
    /// <see cref="FirstRunDefault"/> — the same answer an absent row gets.
    /// </remarks>
    public static OperatorLanguage? FromCultureName(string? cultureName) => cultureName switch
    {
        SimplifiedChineseCultureName => OperatorLanguage.SimplifiedChinese,
        EnglishCultureName => OperatorLanguage.English,
        _ => null,
    };
}
