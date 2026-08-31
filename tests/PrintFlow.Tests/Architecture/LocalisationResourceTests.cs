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
    /// The maximum-bound strings exist in both languages, by name
    /// (Epic 11400 Part B1A.2B §26).
    /// </summary>
    /// <remarks>
    /// Named explicitly for the same reason the two sets above are: the parity tests catch a key
    /// present in one file and missing from the other, but not both files losing a string
    /// together. These are the whole operator vocabulary of the maximum-bound decision, so a
    /// silent gap would leave the size panel showing resource keys.
    /// </remarks>
    [Fact]
    public void The_maximum_bound_strings_exist_in_both_languages()
    {
        string[] required =
        [
            "Session_MaxBoundsHeading",
            "Session_MaxBoundsHint",
            "Session_LabelMaxWidthMm",
            "Session_LabelMaxHeightMm",
            "Session_LabelMaxLongEdgeMm",
            "Session_MaxBoundsConfirm",
            "Session_MaxBoundsInvalid",
            "Session_MaxBoundsSummary",
            "Session_MaxLongEdgeSummary",
            "Session_PreparationHeading",
            "Session_PreparationResolutionOnly",
            "Session_PreparationProportionalShrink",
            "Session_PreparationLimitingEdge",
            "Session_PreparationProjectedSize",
            "Session_PreparationResolution",
            "Session_PreparationAttemptHeading",
            "Session_PreparationAttemptBounds",
            "Session_PreparationAttemptProjected",
            "Session_PreparationAttemptProjectionNotice",
            "Session_RunReady",
            "Session_RunNotReady",
            "Session_DimensionReviewRequired",
            "Session_ReviewMaximumBounds",
            "Session_HistoricalBoundsLabel",
            "PreparationMode_ResolutionOnly",
            "PreparationMode_ProportionalShrink",
            "LimitingEdge_None",
            "LimitingEdge_Width",
            "LimitingEdge_Height",
        ];

        IReadOnlySet<string> english = KeysOf(NeutralResx);
        IReadOnlySet<string> chinese = KeysOf(ChineseResx);

        required.Except(english).ShouldBeEmpty("the operator action needs English wording.");
        required.Except(chinese).ShouldBeEmpty("...and zh-CN wording, on a Chinese workstation.");
    }

    [Fact]
    public void The_flexible_size_operator_strings_exist_in_both_languages()
    {
        string[] required =
        [
            "Session_UsePreset",
            "Session_CustomSize",
            "Session_AdjustSize",
            "Session_RecommendedMaximum",
            "Session_RecommendedLongEdge",
            "Session_TargetEdge",
            "TargetEdge_Width",
            "TargetEdge_Height",
            "TargetEdge_LongEdge",
            "Session_TargetSizeMm",
            "Session_PresetExceeded",
            "Session_CustomResolutionOnly",
            "Session_CustomShrink",
            "Session_EnlargementWarning",
            "Session_ChangeSize",
            "Session_ContinueWithSize",
            "Session_EnlargementConfirmed",
            "Session_PresetOverrideYes",
            "Session_ProjectedPlan",
            "Session_PhotoshopNotRun",
        ];

        IReadOnlySet<string> english = KeysOf(NeutralResx);
        IReadOnlySet<string> chinese = KeysOf(ChineseResx);
        required.Except(english).ShouldBeEmpty();
        required.Except(chinese).ShouldBeEmpty();
    }

    [Fact]
    public void Enlargement_wording_requires_a_second_choice_without_promising_clarity()
    {
        string warning = ValueOf(NeutralResx, "Session_EnlargementWarning");
        warning.ShouldContain("enlarg", Case.Insensitive);
        warning.ShouldContain("300 PPI", Case.Insensitive);
        warning.ShouldContain("may reduce", Case.Insensitive);
        warning.ShouldNotContain("sharp", Case.Insensitive);

        ValueOf(NeutralResx, "Session_ContinueWithSize")
            .ShouldNotBe(ValueOf(NeutralResx, "Session_ChangeSize"));
    }

    /// <summary>
    /// No operator-facing string offers an axis or a resampling method
    /// (Epic 11400 Part B1A.2B §4, §9).
    /// </summary>
    /// <remarks>
    /// A wording test with teeth: the accepted contract settled that the limiting edge is chosen
    /// from the source pixels and that the resampling policy is fixed, so a resource that named
    /// either as something to pick would be an operator-selectable resample arriving by the back
    /// door — through the one file nobody re-reads.
    /// <para>
    /// The scan covers both languages, because a translator adding "双三次" to a Chinese string
    /// would put it on a Chinese workstation's screen and nowhere else.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("Bicubic")]
    [InlineData("双三次")]
    [InlineData("Resample")]
    [InlineData("重新取样")]
    [InlineData("Interpolation")]
    [InlineData("插值")]
    public void No_operator_string_offers_a_resampling_method(string forbidden)
    {
        foreach (string relativePath in new[] { NeutralResx, ChineseResx })
        {
            XDocument document = XDocument.Load(Path.Combine(ShellProjectDirectory(), relativePath));

            IEnumerable<string> offenders = document.Root!.Elements("data")
                .Where(data => (data.Element("value")?.Value ?? string.Empty)
                    .Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                .Select(data => $"{relativePath}: {data.Attribute("name")!.Value}");

            offenders.ShouldBeEmpty(
                "the resampling policy is fixed by the accepted contract and is not shop-floor " +
                "vocabulary.");
        }
    }

    /// <summary>
    /// The maximum-bound wording says limits rather than exact output dimensions
    /// (Epic 11400 Part B1A.2B §4, §5).
    /// </summary>
    /// <remarks>
    /// The defect this catches is a rewording that quietly restores the pre-contract reading —
    /// "enter the finished size" — while every other test still passes, because nothing else
    /// looks at what the sentence claims.
    /// </remarks>
    [Fact]
    public void The_maximum_bound_wording_describes_limits_rather_than_an_exact_size()
    {
        string english = ValueOf(NeutralResx, "Session_MaxBoundsHint");
        english.ShouldContain("maximum", Case.Insensitive);
        english.ShouldContain("proportion", Case.Insensitive);
        english.ShouldContain("300", Case.Insensitive);
        english.ShouldNotContain("exact", Case.Insensitive);

        string chinese = ValueOf(ChineseResx, "Session_MaxBoundsHint");
        chinese.ShouldContain("最大", Case.Sensitive);
        chinese.ShouldContain("比例", Case.Sensitive);
        chinese.ShouldContain("300", Case.Sensitive);

        // The confirm action is about limits, not about setting an exact size.
        ValueOf(NeutralResx, "Session_MaxBoundsConfirm")
            .ShouldNotContain("exact", Case.Insensitive);
    }

    /// <summary>
    /// The attempt audit calls its figures projected and never actual
    /// (Epic 11400 Part B1A.2B §19).
    /// </summary>
    /// <remarks>
    /// Nothing in this slice has read geometry back from Photoshop, so "actual" or "final" beside
    /// those numbers would be a claim no code could honestly make — and the wording is the only
    /// place that claim could appear.
    /// </remarks>
    [Theory]
    [InlineData("Session_PreparationAttemptProjected")]
    [InlineData("Session_PreparationProjectedSize")]
    public void The_projected_figures_are_never_described_as_an_actual_photoshop_result(string key)
    {
        string english = ValueOf(NeutralResx, key);

        english.ShouldContain("Projected", Case.Insensitive);
        english.ShouldNotContain("actual", Case.Insensitive);
        english.ShouldNotContain("final", Case.Insensitive);

        ValueOf(ChineseResx, key).ShouldContain("预计", Case.Sensitive);
    }

    /// <summary>
    /// The dimension-review warning is about a size to redo, not about a failure or lost work
    /// (Epic 11400 Part B1A.2B §11, §13).
    /// </summary>
    [Fact]
    public void The_dimension_review_warning_reports_no_failure_and_no_deletion()
    {
        foreach (string forbidden in new[] { "failed", "failure", "error", "deleted", "lost" })
        {
            ValueOf(NeutralResx, "Session_DimensionReviewRequired")
                .ShouldNotContain(forbidden, Case.Insensitive);
        }

        ValueOf(NeutralResx, "Session_DimensionReviewRequired")
            .ShouldContain("review", Case.Insensitive);
        ValueOf(ChineseResx, "Session_DimensionReviewRequired")
            .ShouldContain("确认", Case.Sensitive);
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
