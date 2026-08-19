using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PrintFlow.Tests.Architecture;

/// <summary>
/// The English and zh-CN resource files stay in step with each other and with the typed
/// accessors (Epic 11100 Part 3C3A §17).
/// </summary>
/// <remarks>
/// A missing translation is silent at compile time and silent at run time:
/// <c>Strings.Get</c> falls back to the resource key, so a Chinese operator sees
/// <c>Session_Approve</c> on a button and nothing anywhere reports a problem. That fallback is
/// the right behaviour — a translation gap must not fail startup in front of an operator — but
/// it means the gap has to be caught here instead.
/// <para>
/// The <c>.resx</c> files are read as XML rather than through <c>ResourceManager</c> on
/// purpose: this asserts what is committed, and it does not depend on satellite assemblies
/// having been built or on the test host's current culture.
/// </para>
/// </remarks>
public sealed class LocalisationResourceTests
{
    private const string NeutralResx = @"Resources\Strings.resx";
    private const string ChineseResx = @"Resources\Strings.zh-CN.resx";

    [Fact]
    public void Every_English_string_has_a_zh_CN_translation()
    {
        IReadOnlySet<string> english = KeysOf(NeutralResx);
        IReadOnlySet<string> chinese = KeysOf(ChineseResx);

        english.Except(chinese).OrderBy(k => k, StringComparer.Ordinal).ShouldBeEmpty(
            "every operator-visible string must have a zh-CN translation; a missing one shows " +
            "the raw resource key on a Chinese workstation.");
    }

    [Fact]
    public void The_zh_CN_file_carries_no_string_the_English_file_has_dropped()
    {
        IReadOnlySet<string> english = KeysOf(NeutralResx);
        IReadOnlySet<string> chinese = KeysOf(ChineseResx);

        chinese.Except(english).OrderBy(k => k, StringComparer.Ordinal).ShouldBeEmpty(
            "a zh-CN entry with no English counterpart is a translation of a string that no " +
            "longer exists.");
    }

    [Fact]
    public void Every_typed_accessor_resolves_to_a_real_resource()
    {
        IReadOnlySet<string> english = KeysOf(NeutralResx);

        string source = File.ReadAllText(Path.Combine(ShellProjectDirectory(), @"Resources\Strings.cs"));
        List<string> accessors = [.. Regex
            .Matches(source, @"Get\(nameof\((?<key>\w+)\)\)", RegexOptions.None, TimeSpan.FromSeconds(5))
            .Select(match => match.Groups["key"].Value)];

        accessors.ShouldNotBeEmpty();
        accessors.Except(english).OrderBy(k => k, StringComparer.Ordinal).ShouldBeEmpty(
            "a typed accessor with no resource behind it would display its own key.");
    }

    [Fact]
    public void No_resource_value_is_left_empty()
    {
        foreach (string relativePath in new[] { NeutralResx, ChineseResx })
        {
            XDocument document = XDocument.Load(Path.Combine(ShellProjectDirectory(), relativePath));

            IEnumerable<string> blank = document.Root!.Elements("data")
                .Where(data => string.IsNullOrWhiteSpace(data.Element("value")?.Value))
                .Select(data => $"{relativePath}: {data.Attribute("name")!.Value}");

            blank.ShouldBeEmpty();
        }
    }

    private static IReadOnlySet<string> KeysOf(string relativePath)
    {
        XDocument document = XDocument.Load(Path.Combine(ShellProjectDirectory(), relativePath));

        // Only <data> carries strings; <resheader> and <metadata> are file bookkeeping.
        return document.Root!.Elements("data")
            .Select(data => data.Attribute("name")!.Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Walks up to the repository root, then into <c>src\PrintFlow.App</c>.</summary>
    private static string ShellProjectDirectory()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }

        return current is null
            ? throw new InvalidOperationException(
                "Could not locate the repository root (PrintFlowStudio.sln) above " + AppContext.BaseDirectory)
            : Path.Combine(current.FullName, "src", "PrintFlow.App");
    }
}
