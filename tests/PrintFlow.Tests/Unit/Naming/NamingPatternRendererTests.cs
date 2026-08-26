using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;

namespace PrintFlow.Tests.Unit.Naming;

/// <summary>
/// The naming renderer's own contract: which token spellings it honours, and how it refuses
/// everything else (naming-contract fix §4, §6, §7).
/// </summary>
/// <remarks>
/// The defect these tests stand against is specific. Accepted manifest patterns are named-token
/// patterns; they used to be handed to <c>string.Format</c>, which reads <c>{Name}</c> as a
/// malformed argument index and throws <see cref="FormatException"/> — a crash rather than a
/// failure, and one that reached the WPF process at the R2 Final Gate. Every case below is
/// either "the accepted syntax works" or "a pattern that cannot work says so".
/// </remarks>
public sealed class NamingPatternRendererTests
{
    private static readonly OutputName Example = OutputName.Parse("Example");

    // -----------------------------------------------------------------------------
    // §4: {Name} means the already-established output name
    // -----------------------------------------------------------------------------

    [Theory]
    [InlineData("{Name}_HD.png", "Example_HD.png")]
    [InlineData("{Name}_CUTOUT.png", "Example_CUTOUT.png")]
    [InlineData("{Name}.png", "Example.png")]
    [InlineData("PF_{Name}_{Name}.png", "PF_Example_Example.png")]
    [InlineData("no-tokens-at-all.png", "no-tokens-at-all.png")]
    public void The_name_token_stands_for_the_established_output_name(string pattern, string expected)
    {
        Rendered(pattern, NamingPatternRenderer.ForName(Example)).ShouldBe(expected);
    }

    /// <summary>Non-ASCII names survive rendering exactly as they survive sanitisation.</summary>
    [Fact]
    public void A_chinese_name_renders_unchanged()
    {
        Rendered("{Name}_CUTOUT.png", NamingPatternRenderer.ForName(OutputName.Parse("客户设计稿")))
            .ShouldBe("客户设计稿_CUTOUT.png");
    }

    [Theory]
    [InlineData(280.0, "Example_280mm_CMYK_W.tif")]
    [InlineData(279.5, "Example_280mm_CMYK_W.tif")]
    public void The_size_token_renders_whole_millimetres(double widthMm, string expected)
    {
        Rendered(
                "{Name}_{SizeMm}mm_CMYK_W.tif",
                NamingPatternRenderer.ForName(Example),
                NamingPatternRenderer.ForSizeMm(widthMm))
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData("_{Sequence:00}", 2, "_02")]
    [InlineData("_{Sequence:00}", 13, "_13")]
    [InlineData("_{Sequence}", 7, "_7")]
    public void The_sequence_token_renders_both_accepted_spellings(
        string pattern, int sequence, string expected)
    {
        Rendered(pattern, NamingPatternRenderer.ForSequence(sequence)).ShouldBe(expected);
    }

    // -----------------------------------------------------------------------------
    // §5: positional syntax is not a second production language
    // -----------------------------------------------------------------------------

    /// <summary>
    /// <c>{0}</c> is refused, deliberately (naming-contract fix §5).
    /// </summary>
    /// <remarks>
    /// No accepted workstation manifest — v1.0.0 through v1.8.0 — has ever written a positional
    /// pattern. It existed only in the source-level fallback and in synthetic test fixtures, so
    /// there is no persisted or runtime configuration to stay compatible with, and keeping a
    /// second undocumented naming language alive purely to spare old fixtures would be the
    /// mistake that produced this defect in the first place.
    /// </remarks>
    [Theory]
    [InlineData("{0}_HD.png")]
    [InlineData("{0}_CUTOUT.png")]
    [InlineData("{0}_{1}mm_CMYK_W.tif")]
    [InlineData("_{0:D2}")]
    public void Positional_syntax_is_not_supported(string pattern)
    {
        Refused(pattern).TechnicalDetail.ShouldContain("unsupported token");
    }

    // -----------------------------------------------------------------------------
    // §6: fail closed on anything unrenderable
    // -----------------------------------------------------------------------------

    [Theory]
    [InlineData("{Foo}_HD.png")]
    [InlineData("{name}_HD.png")]
    [InlineData("{ Name }_HD.png")]
    [InlineData("{Name:X}_HD.png")]
    [InlineData("{SizeMm}_HD.png")]
    public void An_unknown_token_is_refused(string pattern)
    {
        Refused(pattern).TechnicalDetail.ShouldContain("unsupported token");
    }

    [Theory]
    [InlineData("{Name_HD.png")]
    [InlineData("{Name}_HD.png}")]
    [InlineData("}{Name}_HD.png")]
    [InlineData("{{Name}_HD.png")]
    [InlineData("{}_HD.png")]
    public void Malformed_braces_are_refused(string pattern)
    {
        Refused(pattern);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_pattern_is_refused(string? pattern)
    {
        Refused(pattern).TechnicalDetail.ShouldContain("empty");
    }

    /// <summary>A pattern whose only content is a token that renders to nothing is refused.</summary>
    [Fact]
    public void A_pattern_that_renders_to_nothing_is_refused()
    {
        OperationResult<string> result =
            NamingPatternRenderer.Render("{Name}", new NamingToken("Name", string.Empty));

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
    }

    // -----------------------------------------------------------------------------
    // §7: this names files, never paths
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A pattern cannot smuggle a directory, a drive, or a traversal into the rendered name
    /// (naming-contract fix §7).
    /// </summary>
    /// <remarks>
    /// The renderer sits upstream of the workspace's own containment checks, and it is not a
    /// replacement for them — but a naming authority that could emit <c>..\..\x.png</c> would
    /// be a path-template facility, which is exactly what this fix must not create.
    /// </remarks>
    [Theory]
    [InlineData("../{Name}_HD.png")]
    [InlineData("..\\{Name}_HD.png")]
    [InlineData("Sub/{Name}_HD.png")]
    [InlineData("C:{Name}_HD.png")]
    [InlineData("{Name}|HD.png")]
    [InlineData("{Name}?.png")]
    public void A_pattern_cannot_render_anything_but_a_bare_file_name(string pattern)
    {
        Refused(pattern).TechnicalDetail.ShouldContain("never paths");
    }

    /// <summary>
    /// Failures carry the ordinary configuration failure code, so they reach the operator
    /// through the surface every other preset problem uses (§6).
    /// </summary>
    [Fact]
    public void A_refusal_uses_the_existing_configuration_failure_model()
    {
        OperationFailure failure = Refused("{Foo}.png");
        failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        failure.TechnicalDetail.ShouldContain("{Foo}");
        failure.TechnicalDetail.ShouldContain("{Name}");
    }

    private static string Rendered(string pattern, params NamingToken[] tokens)
    {
        OperationResult<string> result = NamingPatternRenderer.Render(pattern, tokens);
        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Failure.ToString() : string.Empty);
        return result.Value;
    }

    /// <summary>Renders with the Enhancement token set, and asserts a structured refusal.</summary>
    private static OperationFailure Refused(string? pattern)
    {
        OperationResult<string> result =
            NamingPatternRenderer.Render(pattern, NamingPatternRenderer.ForName(Example));

        result.IsFailure.ShouldBeTrue($"'{pattern}' must not render.");
        result.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
        return result.Failure;
    }
}
