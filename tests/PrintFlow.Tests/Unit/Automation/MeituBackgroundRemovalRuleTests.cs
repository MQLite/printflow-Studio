using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Tests.Unit.Automation;

public sealed class MeituBackgroundRemovalRuleTests
{
    private static MeituObservation Showing(params string[] texts) =>
        new("美图秀秀-图片编辑", [.. texts], [], true, "A.png");

    private static MeituBackgroundRemovalSignature Signature() => new(
        "抠图",
        PageShape(),
        "调整",
        PageShape(),
        "自动选择",
        MeituBackgroundRemovalModePolicy.OperatorOrReviewedContentDecision,
        true,
        new MeituBusySignature(["智能识别中...", "返回结果中...", "图片合成中...", "取消"], 2),
        new MeituCompletionSignature(["自动选择", "局部抠图", "手动修补", "反选", "移除背景"], 5, true));

    private static MeituOwnedControlShape PageShape() => new(
        "CheckBox", "PageButton", "", "CheckBox", "PageButton", "", "", 0, UiPatternKind.Invoke);

    [Theory]
    [InlineData("智能识别中...")]
    [InlineData("返回结果中...")]
    [InlineData("图片合成中...")]
    public void Every_operation_specific_progress_stage_plus_cancel_is_Busy(string stage)
    {
        MeituBackgroundRemovalRule.Classify(Signature(), Showing(stage, "取消"))
            .ShouldBe(MeituBackgroundRemovalPhase.Busy);
    }

    [Fact]
    public void Generic_editor_is_not_Busy()
    {
        MeituBackgroundRemovalRule.Classify(Signature(), Showing("图片编辑", "保存", "调整"))
            .ShouldBe(MeituBackgroundRemovalPhase.Unobserved);
    }

    [Fact]
    public void Enhancement_Busy_is_not_Background_Removal_Busy()
    {
        MeituBackgroundRemovalRule.Classify(Signature(), Showing("变清晰中，请稍候...", "取消"))
            .ShouldBe(MeituBackgroundRemovalPhase.Unobserved);
    }

    [Fact]
    public void Cancel_alone_is_not_Busy()
    {
        MeituBackgroundRemovalRule.Classify(Signature(), Showing("取消"))
            .ShouldBe(MeituBackgroundRemovalPhase.Unobserved);
    }

    [Fact]
    public void All_positive_result_controls_with_Busy_absent_are_complete()
    {
        MeituBackgroundRemovalRule.Classify(
            Signature(), Showing("自动选择", "局部抠图", "手动修补", "反选", "移除背景"))
            .ShouldBe(MeituBackgroundRemovalPhase.Complete);
    }

    [Fact]
    public void Busy_disappearing_without_positive_completion_is_unobserved()
    {
        MeituBackgroundRemovalRule.Classify(Signature(), Showing("图片编辑", "保存"))
            .ShouldBe(MeituBackgroundRemovalPhase.Unobserved);
    }

    [Fact]
    public void Completion_controls_visible_during_Busy_still_classify_as_Busy()
    {
        MeituBackgroundRemovalRule.Classify(
            Signature(),
            Showing("自动选择", "局部抠图", "手动修补", "反选", "移除背景", "智能识别中...", "取消"))
            .ShouldBe(MeituBackgroundRemovalPhase.Busy);
    }

    [Fact]
    public void Too_few_result_controls_do_not_complete()
    {
        MeituBackgroundRemovalRule.Classify(
            Signature(), Showing("自动选择", "局部抠图", "手动修补", "反选"))
            .ShouldBe(MeituBackgroundRemovalPhase.Unobserved);
    }

    [Fact]
    public void Empty_evidence_matches_nothing()
    {
        MeituBackgroundRemovalSignature broken = Signature() with
        {
            Busy = new MeituBusySignature([], 0),
            Completion = new MeituCompletionSignature([], 0, true),
        };
        MeituBackgroundRemovalRule.Classify(broken, Showing("智能识别中...", "取消"))
            .ShouldBe(MeituBackgroundRemovalPhase.Unobserved);
    }
}
