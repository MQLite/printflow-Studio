using System.Collections.Immutable;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Tests.Unit.Automation;

public sealed class MeituBackgroundRemovalTargetRuleTests
{
    private const int Pid = 4242;

    private static MeituOwnedControlShape Shape() => new(
        "CheckBox", "PageButton", string.Empty,
        "CheckBox", "PageButton", string.Empty,
        string.Empty, 0, UiPatternKind.Invoke);

    private static UiElementIdentity Page(
        string name = "抠图",
        int processId = Pid,
        bool enabled = true,
        bool offscreen = false,
        string controlType = "CheckBox",
        string className = "PageButton",
        string id = "",
        UiBounds? bounds = null,
        ImmutableArray<UiPatternKind>? patterns = null) =>
        new(controlType, id, name, className, processId,
            patterns ?? [UiPatternKind.Invoke, UiPatternKind.Toggle],
            bounds ?? new UiBounds(10, 10, 56, 56), enabled, offscreen);

    private static OperationResult<int> Select(params MeituCardCandidate[] candidates) =>
        MeituBackgroundRemovalTargetRule.SelectActionOwner(Shape(), "抠图", Pid, candidates);

    [Fact]
    public void The_exact_actionable_page_marker_is_selected()
    {
        UiElementIdentity page = Page();
        Select(new MeituCardCandidate(page, page)).Value.ShouldBe(0);
    }

    [Theory]
    [InlineData("智能抠图")]
    [InlineData("AI换背景")]
    [InlineData("AI变清晰")]
    [InlineData("抠图工具")]
    [InlineData("")]
    public void Near_match_and_unrelated_names_are_refused(string name)
    {
        UiElementIdentity page = Page(name: name);
        AssertNoInput(Select(new MeituCardCandidate(page, page)));
    }

    [Fact]
    public void A_text_marker_that_is_not_actionable_is_refused()
    {
        UiElementIdentity marker = Page(controlType: "Text", className: "QLabel");
        AssertNoInput(Select(new MeituCardCandidate(marker, marker)));
    }

    [Fact]
    public void A_wrong_fixed_depth_owner_is_refused()
    {
        UiElementIdentity marker = Page();
        UiElementIdentity ancestor = Page(
            name: string.Empty, controlType: "Group", className: "QWidget",
            bounds: new UiBounds(0, 0, 100, 100));
        AssertNoInput(Select(new MeituCardCandidate(marker, ancestor)));
    }

    [Fact]
    public void An_intervening_invokable_QWidget_is_refused()
    {
        UiElementIdentity marker = Page();
        UiElementIdentity widget = Page(
            name: string.Empty, controlType: "Group", className: "QWidget",
            patterns: [UiPatternKind.Invoke], bounds: new UiBounds(0, 0, 100, 100));
        AssertNoInput(Select(new MeituCardCandidate(marker, widget)));
    }

    [Fact]
    public void Duplicate_valid_candidates_are_ambiguous()
    {
        UiElementIdentity first = Page();
        UiElementIdentity second = Page(bounds: new UiBounds(100, 10, 56, 56));
        OperationResult<int> result = Select(
            new MeituCardCandidate(first, first), new MeituCardCandidate(second, second));
        AssertNoInput(result);
        result.Failure.Context["accepted"].ShouldBe("2");
    }

    [Fact]
    public void Wrong_process_is_refused()
    {
        UiElementIdentity page = Page(processId: 999);
        AssertNoInput(Select(new MeituCardCandidate(page, page)));
    }

    [Fact]
    public void Disabled_is_refused()
    {
        UiElementIdentity page = Page(enabled: false);
        AssertNoInput(Select(new MeituCardCandidate(page, page)));
    }

    [Fact]
    public void Offscreen_is_refused()
    {
        UiElementIdentity page = Page(offscreen: true);
        AssertNoInput(Select(new MeituCardCandidate(page, page)));
    }

    [Fact]
    public void Missing_Invoke_is_refused()
    {
        UiElementIdentity page = Page(patterns: [UiPatternKind.Toggle]);
        AssertNoInput(Select(new MeituCardCandidate(page, page)));
    }

    [Fact]
    public void No_candidate_is_refused()
    {
        AssertNoInput(Select());
    }

    private static void AssertNoInput(OperationResult<int> result)
    {
        result.IsFailure.ShouldBeTrue();
        result.Failure.Context["inputSent"].ShouldBe("false");
    }
}
