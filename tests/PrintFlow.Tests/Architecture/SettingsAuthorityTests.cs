using System.IO;
using System.Text.RegularExpressions;
using PrintFlow.App.Localisation;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Settings;
using PrintFlow.Infrastructure.Gate;
using PrintFlow.Infrastructure.Verification;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Architecture;

/// <summary>
/// The boundaries SCRUM-11118's precedence decision rests on.
/// </summary>
/// <remarks>
/// Each case here guards a property that is invisible at run time until it is already wrong: a
/// renamed verification check would turn a production row blank rather than fail, a stray write
/// of a preset-owned key would create a second authority nobody notices until it disagrees, and
/// a language list that grew a third member would promise a satellite resource that does not
/// exist.
/// </remarks>
public sealed class SettingsAuthorityTests
{
    /// <summary>
    /// The two production facts Settings displays are published by the verification authority
    /// itself, not picked out of the check list by a screen.
    /// </summary>
    /// <remarks>
    /// The shell may not name a verification check — readiness policy lives in one place, and
    /// the shell is not it (Epic 11500 Part B §11.10). So <c>VerifiedEnvironmentGate</c> lifts
    /// the accepted output root and the colour-setup outcome onto the report as named facts,
    /// beside <c>PresetIdentity</c>, and this asserts that it actually does — over the real
    /// gate and the synthetic accepted workstation, not over a stub.
    /// </remarks>
    [Fact]
    public void The_readiness_report_publishes_the_production_facts_settings_displays()
    {
        using WorkstationVerificationFixture fixture = new();

        EnvironmentReadinessReport report =
            new VerifiedEnvironmentGate(fixture.CreateVerifier()).Read();

        report.AcceptedOutputRoot.ShouldBe(fixture.WorkspaceRoot);
        report.PresetIdentity.ShouldNotBeNullOrWhiteSpace();

        // A passive reading runs no live application phase, so it states nothing about the
        // colour setup — and null is how it says so, rather than by omitting a row a screen
        // would then have to interpret.
        report.PhotoshopColourSetup.ShouldBeNull();
    }

    /// <summary>No shell view model names any workstation verification check.</summary>
    /// <remarks>
    /// <c>EnvironmentDiagnosticsBoundaryTests</c> already asserts this for the readiness screen's
    /// classification. Restated here because SCRUM-11118 is where the temptation reappeared: a
    /// Settings screen that wanted two particular verified values, and could have got them by
    /// string-matching two check keys.
    /// </remarks>
    [Fact]
    public void The_settings_screen_names_no_verification_check()
    {
        string source = File.ReadAllText(
            Path.Combine(ShellProjectDirectory(), @"ViewModels\SettingsViewModel.cs"));

        foreach (string check in Enum.GetNames<WorkstationVerificationCheck>())
        {
            foreach (string line in source.Split('\n'))
            {
                if (line.TrimStart().StartsWith("///", StringComparison.Ordinal)
                    || line.TrimStart().StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                line.ShouldNotContain(check);
            }
        }
    }

    /// <summary>
    /// No production fact is ever written as a setting.
    /// </summary>
    /// <remarks>
    /// Asserted over the shipped source rather than over one screen's behaviour, because the
    /// rule is about the whole product: the verified preset is the single authority for the
    /// production DPI, the accepted output root, the preset's own details and the Photoshop
    /// colour settings, and a persisted copy of any of them would be a second number able to
    /// disagree with the files PrintFlow actually produced.
    /// </remarks>
    [Theory]
    [InlineData(nameof(SettingKey.ProductionDpi))]
    [InlineData(nameof(SettingKey.DefaultOutputRoot))]
    [InlineData(nameof(SettingKey.WorkstationPresetDetails))]
    [InlineData(nameof(SettingKey.PhotoshopColourSettingsConfirmed))]
    public void No_shipped_code_constructs_a_setting_entry_for_a_production_fact(string key)
    {
        List<string> offenders = [];

        foreach (string file in Directory.EnumerateFiles(
                     SourceRoot(), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            {
                continue;
            }

            string source = File.ReadAllText(file);
            if (Regex.IsMatch(
                    source,
                    $@"SettingEntry\.(Text|Integer|Boolean)\(\s*SettingKey\.{key}\b",
                    RegexOptions.None,
                    TimeSpan.FromSeconds(5)))
            {
                offenders.Add(file);
            }
        }

        offenders.ShouldBeEmpty(
            $"SettingKey.{key} states a fact of the verified production preset. It is displayed " +
            "read-only from the verification authority and is never persisted as a second copy.");
    }

    /// <summary>
    /// Exactly two languages, each with a satellite resource behind it.
    /// </summary>
    /// <remarks>
    /// The AC names two. A third member would be a claim that a reviewed translation exists,
    /// which is a piece of work and not an enum edit (SCRUM-11119).
    /// </remarks>
    [Fact]
    public void Exactly_two_operator_languages_are_offered()
    {
        Enum.GetValues<OperatorLanguage>().ShouldBe(
            [OperatorLanguage.SimplifiedChinese, OperatorLanguage.English], ignoreOrder: true);

        OperatorLanguages.All.Count.ShouldBe(2);
        OperatorLanguages.FirstRunDefault.ShouldBe(OperatorLanguage.SimplifiedChinese);

        // The English culture is answered by the neutral resources; zh-CN needs its own file.
        File.Exists(Path.Combine(ShellProjectDirectory(), @"Resources\Strings.zh-CN.resx"))
            .ShouldBeTrue();
    }

    /// <summary>
    /// Nothing resolves the operator's language from the operating system.
    /// </summary>
    /// <remarks>
    /// The first-run default is a Product decision. A read of the machine's own UI culture in
    /// the shell would quietly reintroduce the behaviour SCRUM-11119 replaced — and it is a read
    /// nobody would notice on the one supported workstation, whose Windows is already Chinese.
    /// </remarks>
    [Fact]
    public void The_shell_never_derives_the_operator_language_from_Windows()
    {
        List<string> offenders = [];

        foreach (string file in Directory.EnumerateFiles(
                     ShellProjectDirectory(), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            {
                continue;
            }

            string source = File.ReadAllText(file);
            if (source.Contains("InstalledUICulture", StringComparison.Ordinal)
                || source.Contains("GetUserDefaultUILanguage", StringComparison.Ordinal))
            {
                offenders.Add(file);
            }
        }

        offenders.ShouldBeEmpty(
            "the first-run language is the Product's own default, never the workstation's.");
    }

    /// <summary>The repository's <c>src</c>, found by walking up to the solution file.</summary>
    private static string SourceRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }

        return current is null
            ? throw new InvalidOperationException(
                "Could not locate the repository root (PrintFlowStudio.sln) above " + AppContext.BaseDirectory)
            : Path.Combine(current.FullName, "src");
    }

    private static string ShellProjectDirectory() =>
        Path.Combine(SourceRoot(), "PrintFlow.App");
}
