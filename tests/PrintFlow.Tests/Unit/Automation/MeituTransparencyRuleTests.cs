using PrintFlow.Infrastructure.Adapters.Meitu;

namespace PrintFlow.Tests.Unit.Automation;

public sealed class MeituTransparencyRuleTests
{
    [Fact]
    public void Alpha_endpoints_are_literal_and_have_no_hidden_midpoint_threshold()
    {
        byte[] alpha = [0, 1, 127, 128, 254, 255];

        MeituTransparencyFacts facts = MeituTransparencyRule.Summarise(alpha);

        facts.PixelCount.ShouldBe(6);
        facts.TransparentPixelCount.ShouldBe(5);
        facts.VisiblePixelCount.ShouldBe(5);
        facts.HasTransparentPixels.ShouldBeTrue();
        facts.HasVisiblePixels.ShouldBeTrue();
        MeituTransparencyRule.Validate(facts).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Every_alpha_255_is_rejected_as_opaque()
    {
        MeituTransparencyFacts facts = MeituTransparencyRule.Summarise([255, 255, 255]);

        facts.HasTransparentPixels.ShouldBeFalse();
        MeituTransparencyRule.Validate(facts).Failure.TechnicalDetail.ShouldContain("opaque");
    }

    [Fact]
    public void Every_alpha_zero_is_rejected_as_foreground_loss()
    {
        MeituTransparencyFacts facts = MeituTransparencyRule.Summarise([0, 0, 0]);

        facts.HasVisiblePixels.ShouldBeFalse();
        MeituTransparencyRule.Validate(facts).Failure.TechnicalDetail.ShouldContain("transparent");
    }

    [Fact]
    public void No_pixels_is_not_a_transparency_claim()
    {
        var result = MeituTransparencyRule.Validate(MeituTransparencyRule.Summarise([]));

        result.IsFailure.ShouldBeTrue();
    }
}
