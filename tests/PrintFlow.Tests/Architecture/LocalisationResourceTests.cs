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

    /// <summary>
    /// The Background Removal authority strings exist in both languages, by name
    /// (Epic 11300 Part C2B2 §22).
    /// </summary>
    /// <remarks>
    /// The two parity tests above already catch a key present in one file and missing from the
    /// other. What they cannot catch is both files losing a string together — a rename that
    /// tidied one side and then the other, leaving a screen with no wording for the operator
    /// action at all. Naming this slice's strings explicitly is what makes that a test failure
    /// rather than a silent gap.
    /// </remarks>
    [Fact]
    public void The_background_removal_authority_strings_exist_in_both_languages()
    {
        string[] required =
        [
            "Session_BackgroundRemovalHeading",
            "Session_BackgroundRemovalHint",
            "Session_BackgroundRemovalAuthorise",
            "Session_BackgroundRemovalConfirmQuestion",
            "Session_BackgroundRemovalConfirm",
            "Session_BackgroundRemovalCancel",
            "Session_BackgroundRemovalAuthorised",
            "Session_BackgroundRemovalNotAuthorised",
            "Session_BackgroundRemovalRunnable",
            "Session_BackgroundRemovalAttemptAudit",
        ];

        IReadOnlySet<string> english = KeysOf(NeutralResx);
        IReadOnlySet<string> chinese = KeysOf(ChineseResx);

        required.Except(english).ShouldBeEmpty("the operator action needs English wording.");
        required.Except(chinese).ShouldBeEmpty("...and zh-CN wording, on a Chinese workstation.");
    }

    /// <summary>
    /// The confirmation promises nothing about the result (§11).
    /// </summary>
    /// <remarks>
    /// A wording test, deliberately. The confirmation is where an operator decides to let Meitu
    /// choose the subject, and the one thing it must not do is imply the choice will be right —
    /// "guaranteed", "perfect", "always" and the rest are claims this product cannot make about
    /// an AI cutout, and a later edit that added one would be a promise nobody meant to give.
    /// </remarks>
    [Theory]
    [InlineData("guarantee")]
    [InlineData("guaranteed")]
    [InlineData("perfect")]
    [InlineData("always correct")]
    [InlineData("no need to check")]
    public void The_confirmation_claims_nothing_about_cutout_quality(string forbidden)
    {
        string confirmation = ValueOf(NeutralResx, "Session_BackgroundRemovalConfirmQuestion");

        confirmation.ShouldNotContain(forbidden, Case.Insensitive);
    }

    /// <summary>
    /// The Stop and Take Over strings exist in both languages, by name
    /// (Epic 11300 Part D2A §37).
    /// </summary>
    /// <remarks>
    /// Named explicitly for the same reason the Background Removal set is: the two parity tests
    /// above catch a key present in one file and missing from the other, but not both files
    /// losing a string together. An operator control that silently lost its wording would show a
    /// resource key on the button that stops Meitu.
    /// </remarks>
    [Fact]
    public void The_stop_and_take_over_strings_exist_in_both_languages()
    {
        string[] required =
        [
            "Session_Stop",
            "Session_StopHint",
            "Session_StoppingNotice",
            "Session_TakeOver",
            "Session_TakeOverHint",
            "Session_TakingOverNotice",
            "Session_TakeOverConfirmQuestion",
            "Session_TakeOverConfirm",
            "Session_TakeOverCancel",
            "Session_RetainedOperationRunning",
            "Session_RetainedProcessedResult",
            "Session_RetainedUnknown",
            "Session_ReenterAutomation",
            "Session_ReenterAutomationHint",
            "Failure_AutomationStopped",
            "Failure_AutomationHandedOff",
        ];

        IReadOnlySet<string> english = KeysOf(NeutralResx);
        IReadOnlySet<string> chinese = KeysOf(ChineseResx);

        required.Except(english).ShouldBeEmpty("the operator action needs English wording.");
        required.Except(chinese).ShouldBeEmpty("...and zh-CN wording, on a Chinese workstation.");
    }

    /// <summary>
    /// Both supported languages state the D2B manual-recovery policy for an unresponsive
    /// handed-off Meitu instance.
    /// </summary>
    [Fact]
    public void Take_over_guidance_has_force_close_and_manual_recovery_parity()
    {
        string english = ValueOf(NeutralResx, "Session_TakeOverHint");
        english.ShouldContain("will not force-close Meitu", Case.Insensitive);
        english.ShouldContain("unresponsive after takeover", Case.Insensitive);
        english.ShouldContain("close it manually", Case.Insensitive);
        english.ShouldContain("Windows", Case.Insensitive);
        english.ShouldContain("before returning to automation", Case.Insensitive);

        string chinese = ValueOf(ChineseResx, "Session_TakeOverHint");
        chinese.ShouldContain("不会强制关闭美图秀秀", Case.Sensitive);
        chinese.ShouldContain("接管后美图秀秀无响应", Case.Sensitive);
        chinese.ShouldContain("手动关闭", Case.Sensitive);
        chinese.ShouldContain("Windows", Case.Sensitive);
        chinese.ShouldContain("恢复自动处理", Case.Sensitive);
    }

    /// <summary>No operator action is labelled as a destructive process-control action.</summary>
    [Theory]
    [InlineData("kill")]
    [InlineData("terminate")]
    [InlineData("force close")]
    [InlineData("force-close")]
    public void Operator_action_labels_offer_no_force_termination(string forbidden)
    {
        string[] actionKeys =
        [
            "Session_Stop",
            "Session_TakeOver",
            "Session_TakeOverConfirm",
            "Session_TakeOverCancel",
            "Session_ReenterAutomation",
        ];

        foreach (string resource in new[] { NeutralResx, ChineseResx })
        {
            foreach (string key in actionKeys)
            {
                ValueOf(resource, key).ShouldNotContain(forbidden, Case.Insensitive);
            }
        }
    }

    /// <summary>
    /// The takeover confirmation states all four consequences and overstates none (§21, §26).
    /// </summary>
    /// <remarks>
    /// A wording test, deliberately, and the mirror of the cutout-quality one below. §26 lists
    /// four things the operator must be told, and §21 forbids implying the external application
    /// is safe or finished — which is precisely the reassurance a later edit would be tempted to
    /// add to a confirmation that currently sounds alarming.
    /// </remarks>
    [Theory]
    [InlineData("stop controlling Meitu")]
    [InlineData("left exactly as it is")]
    [InlineData("will not produce a Revision")]
    [InlineData("Return to automation")]
    public void The_take_over_confirmation_states_each_consequence(string required)
    {
        ValueOf(NeutralResx, "Session_TakeOverConfirmQuestion")
            .ShouldContain(required, Case.Insensitive);
    }

    /// <summary>
    /// Nothing PrintFlow says about a stop claims the external application is safe or finished
    /// (§21).
    /// </summary>
    /// <remarks>
    /// PrintFlow stopped looking at Meitu; it cannot establish either, and a message that said
    /// so would be the one piece of wording an operator would act on without checking. The scan
    /// covers every stop-related string in both languages so a translation cannot introduce a
    /// promise the English does not make.
    /// </remarks>
    [Theory]
    [InlineData("safely closed")]
    [InlineData("has finished")]
    [InlineData("is finished")]
    [InlineData("no longer running")]
    [InlineData("nothing further to do")]
    public void No_stop_message_claims_the_external_application_is_finished(string forbidden)
    {
        string[] stopStrings =
        [
            "Session_StopHint",
            "Session_StoppingNotice",
            "Session_TakeOverHint",
            "Session_TakingOverNotice",
            "Session_TakeOverConfirmQuestion",
            "Session_RetainedOperationRunning",
            "Session_RetainedProcessedResult",
            "Session_RetainedUnknown",
            "Failure_AutomationStopped",
            "Failure_AutomationHandedOff",
        ];

        foreach (string key in stopStrings)
        {
            ValueOf(NeutralResx, key).ShouldNotContain(forbidden, Case.Insensitive);
        }
    }

    /// <summary>
    /// Stop and Take Over are worded as different actions (§27).
    /// </summary>
    /// <remarks>
    /// The distinction §27 asks the operator to understand has to be visible in the words. Two
    /// labels that were near-synonyms would be the "one ambiguous button" split in two, which is
    /// no better.
    /// </remarks>
    [Fact]
    public void Stop_and_take_over_are_not_worded_as_the_same_action()
    {
        foreach (string resx in new[] { NeutralResx, ChineseResx })
        {
            string stop = ValueOf(resx, "Session_Stop");
            string takeOver = ValueOf(resx, "Session_TakeOver");

            stop.ShouldNotBe(takeOver);
            takeOver.Length.ShouldBeGreaterThan(
                stop.Length,
                "Take Over names an external application and an ownership change; Stop does not.");
        }
    }

    /// <summary>
    /// Neither label promises to close or kill the external application (§3, §27).
    /// </summary>
    [Theory]
    [InlineData("kill")]
    [InlineData("terminate")]
    [InlineData("force quit")]
    [InlineData("end task")]
    public void No_operator_label_offers_to_terminate_the_external_application(string forbidden)
    {
        foreach (string key in new[] { "Session_Stop", "Session_StopHint", "Session_TakeOver", "Session_TakeOverHint" })
        {
            ValueOf(NeutralResx, key).ShouldNotContain(forbidden, Case.Insensitive);
        }
    }

    private static string ValueOf(string relativePath, string key)
    {
        XDocument document = XDocument.Load(Path.Combine(ShellProjectDirectory(), relativePath));

        return document.Root!.Elements("data")
            .Single(data => data.Attribute("name")!.Value == key)
            .Element("value")!.Value;
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
