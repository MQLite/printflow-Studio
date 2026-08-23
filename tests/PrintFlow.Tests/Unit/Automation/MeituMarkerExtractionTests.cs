using System.Collections.Immutable;
using PrintFlow.Infrastructure.Adapters.Meitu;

namespace PrintFlow.Tests.Unit.Automation;

/// <summary>
/// The rule that turns signed prose into matchable labels (Epic 11300 Part A §2).
/// </summary>
/// <remarks>
/// The inputs below are the shapes Epic 11000 actually wrote — a slash-separated bilingual
/// entry, a label with an English suffix, a label buried mid-sentence, and a label with a Latin
/// prefix. They are reproduced here because the extraction is only worth anything if it handles
/// the real file, and a synthetic "简体中文 entry" would prove nothing about that.
/// </remarks>
public sealed class MeituMarkerExtractionTests
{
    [Theory]
    [InlineData("图片编辑 / PhotoEditor entry", "图片编辑")]
    [InlineData("抠图 entry", "抠图")]
    [InlineData("AI变清晰 entry", "AI变清晰")]
    [InlineData("美图秀秀 For Windows logo area", "美图秀秀")]
    [InlineData("bottom category navigation and 更多工具 area", "更多工具")]
    public void A_marker_description_yields_exactly_its_label(string description, string expected)
    {
        MeituMarkerExtraction.Extract([description]).ShouldBe([expected]);
    }

    /// <summary>
    /// English-only scaffolding produces no marker at all.
    /// </summary>
    /// <remarks>
    /// This is the property that matters most: a word like "entry" appearing as a marker would
    /// match almost any application's automation tree, and would quietly turn the welcome-page
    /// check into something that always passes.
    /// </remarks>
    [Fact]
    public void Description_text_carrying_no_CJK_produces_no_marker()
    {
        MeituMarkerExtraction.Extract(["bottom category navigation area", "logo", "search box"])
            .ShouldBeEmpty();
    }

    [Fact]
    public void Repeated_labels_are_collapsed_and_the_order_is_stable()
    {
        ImmutableArray<string> first = MeituMarkerExtraction.Extract(["抠图 entry", "抠图 again", "图片编辑"]);
        ImmutableArray<string> second = MeituMarkerExtraction.Extract(["图片编辑", "抠图 again", "抠图 entry"]);

        first.Length.ShouldBe(2);
        first.ShouldBe(second);
    }

    /// <summary>
    /// The complete marker list Epic 11000 signed, extracted end to end.
    /// </summary>
    /// <remarks>
    /// The strings below are copied verbatim from the workstation's
    /// <c>apps\meitu\clean-start.json</c>. Reproducing them here does not create the second
    /// hard-coded copy §7 warns about — the adapter still reads the hash-verified file at run
    /// time, and this fixture only pins that the extraction rule handles that file's real shape.
    /// </remarks>
    [Fact]
    public void The_real_Epic_11000_marker_list_yields_its_ten_labels()
    {
        string[] asSigned =
        [
            "美图秀秀 For Windows logo area",
            "图片编辑 / PhotoEditor entry",
            "海报设计 / Posters entry",
            "批处理 / Batch entry",
            "抠图 entry",
            "AI消除 entry",
            "AI变清晰 entry",
            "证件照 entry",
            "AI商品套图 entry",
            "bottom category navigation and 更多工具 area",
        ];

        MeituMarkerExtraction.Extract(asSigned).ShouldBe(
            [
                "美图秀秀", "图片编辑", "海报设计", "批处理", "抠图",
                "AI消除", "AI变清晰", "证件照", "AI商品套图", "更多工具",
            ],
            ignoreOrder: true);
    }
}
