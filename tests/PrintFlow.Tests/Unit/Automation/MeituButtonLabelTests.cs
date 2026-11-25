using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Tests.Unit.Automation;

public sealed class MeituButtonLabelTests
{
    [Theory]
    [InlineData("商品\n货物", "商品货物全部\u200B")]
    [InlineData("商品\n货物", "商品 货物")]
    [InlineData("商品\n货物", "商\n品货物")]
    [InlineData("商品\n货物", "商品\r货物")]
    [InlineData("商品\n货物", "商品\u200B货物")]
    [InlineData("保存", "\u200B保存")]
    [InlineData("保存", "保存 ")]
    [InlineData("保存", "保存\uFEFF")]
    [InlineData("保存", "保存\n")]
    [InlineData("保存\n图片", "保存图片")]
    public void Only_documented_label_variants_match(string expected, string observed) =>
        MeituButtonLabel.Matches(observed, expected).ShouldBeFalse();

    [Theory]
    [InlineData("\uE0EA\u200B")]
    [InlineData("\uE0E6\u200B")]
    public void Icon_glyphs_keep_their_exact_spelling(string observed)
    {
        MeituControlSignature signature = new("\uE0E6", ".SaveMaskWidget.widgetRight.titleFrame.closeButton",
            "Button", "proui::IconFontButton", UiPatternKind.Invoke);
        signature.MatchesName(observed).ShouldBeFalse();
    }
}
