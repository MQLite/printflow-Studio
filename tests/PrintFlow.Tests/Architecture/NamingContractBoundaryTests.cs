using System.IO;
using System.Text.RegularExpressions;
using PrintFlow.Domain.Outputs;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Architecture;

/// <summary>
/// Naming patterns reach exactly one renderer, and no positional composite-format surface is
/// left for a future caller to fall into (naming-contract fix §4, §5).
/// </summary>
/// <remarks>
/// A source-text scan, for the reason <see cref="BannedApiEnforcementTests"/> gives for its
/// own: the rule is about what may be written, and stating it in text catches it wherever
/// someone writes it rather than only where a type happens to be referenced.
/// <para>
/// The rule is narrow on purpose. <c>string.Format</c> is not banned — it is a perfectly
/// ordinary way to build a message. What is banned is applying it to a naming <i>pattern</i>,
/// because the accepted manifest's patterns are named-token patterns and composite formatting
/// reads <c>{Name}</c> as a malformed argument index. That is the exact line of code that
/// terminated the WPF process at the R2 Final Gate.
/// </para>
/// </remarks>
public sealed class NamingContractBoundaryTests
{
    /// <summary>A naming pattern is never an argument to <c>string.Format</c>.</summary>
    [Fact]
    public void No_source_formats_a_naming_pattern_positionally()
    {
        Regex offender = new(@"string\.Format\([^;]*Pattern\b", RegexOptions.Singleline);

        List<string> offenders = [];
        foreach (string file in SolutionSourceFiles())
        {
            string text = File.ReadAllText(file);
            foreach (Match match in offender.Matches(text))
            {
                offenders.Add($"{Path.GetFileName(file)}: {match.Value.Trim()}");
            }
        }

        offenders.ShouldBeEmpty(
            "naming patterns are rendered by NamingPatternRenderer, never by composite formatting — " +
            "the accepted manifest writes {Name}, and string.Format throws FormatException on it.");
    }

    /// <summary>
    /// The domain's own fallback speaks the accepted naming language, not a second one.
    /// </summary>
    /// <remarks>
    /// <c>DesignDefault</c> stands in for the preset until one is loaded, so a fallback written
    /// in a syntax no accepted manifest uses is a fallback that disagrees with the very
    /// configuration it substitutes for. It used to be positional; the audit behind §5 found
    /// that syntax nowhere in any accepted manifest from v1.0.0 to v1.8.0.
    /// </remarks>
    [Fact]
    public void The_domain_fallback_patterns_are_the_accepted_syntax()
    {
        NamingPatternSet fallback = NamingPatternSet.DesignDefault;

        fallback.EnhancedPattern.ShouldBe(AcceptedNamingContract.EnhancedPattern);
        fallback.CutoutPattern.ShouldBe(AcceptedNamingContract.CutoutPattern);
        fallback.ProductionTiffPattern.ShouldBe(AcceptedNamingContract.ProductionTiffPattern);
        fallback.CollisionSuffixPattern.ShouldBe(AcceptedNamingContract.CollisionPattern);
    }

    /// <summary>
    /// Every <c>.cs</c> file under <c>src\</c> and <c>tests\</c>, excluding generated output.
    /// </summary>
    /// <remarks>
    /// Tests are in scope deliberately: the positional naming patterns this fix removed lived
    /// in a fixture, not in product code, and a rule that skipped fixtures would not have
    /// caught them.
    /// </remarks>
    private static IEnumerable<string> SolutionSourceFiles()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }

        if (current is null)
        {
            throw new InvalidOperationException(
                "Could not locate the repository root (PrintFlowStudio.sln) above " + AppContext.BaseDirectory);
        }

        string separator = Path.DirectorySeparatorChar.ToString();
        foreach (string folder in new[] { "src", "tests" })
        {
            string root = Path.Combine(current.FullName, folder);
            foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{separator}obj{separator}", StringComparison.Ordinal) ||
                    file.Contains($"{separator}bin{separator}", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return file;
            }
        }
    }
}
