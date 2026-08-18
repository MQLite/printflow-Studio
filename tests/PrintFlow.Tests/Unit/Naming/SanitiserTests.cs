using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;

namespace PrintFlow.Tests.Unit.Naming;

/// <summary>
/// Jira 11107: output-name sanitisation and preset-driven artefact naming, as pure functions
/// with no file-system access (task §19, §46).
/// </summary>
public sealed class SanitiserTests
{
    [Theory]
    [InlineData("My<Design>.png", "MyDesign.png")]
    [InlineData("bad:name?.png", "badname.png")]
    [InlineData("pipe|slash/back\\.png", "pipeslashback.png")]
    public void Invalid_Windows_characters_are_stripped(string input, string expected)
    {
        OutputName.Sanitise(input).Value.ShouldBe(expected);
    }

    [Fact]
    public void Trailing_dots_and_spaces_are_removed()
    {
        OutputName.Sanitise("design...   ").Value.ShouldBe("design");
    }

    [Fact]
    public void Internal_whitespace_runs_collapse_to_a_single_space()
    {
        OutputName.Sanitise("my    design   name").Value.ShouldBe("my design name");
    }

    [Fact]
    public void Empty_after_sanitisation_falls_back_to_Untitled()
    {
        OutputName.Sanitise("   ...   ").Value.ShouldBe("Untitled");
        OutputName.Sanitise("").Value.ShouldBe("Untitled");
        OutputName.Sanitise(null).Value.ShouldBe("Untitled");
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("com9")]
    [InlineData("LPT1")]
    [InlineData("lpt9")]
    public void Reserved_device_names_are_protected(string reserved)
    {
        OutputName sanitised = OutputName.Sanitise(reserved);
        sanitised.Value.ShouldBe("_" + reserved);
    }

    [Fact]
    public void Over_length_stems_are_truncated_to_the_budget()
    {
        string overLong = new('设', 200);
        OutputName sanitised = OutputName.Sanitise(overLong);
        sanitised.Value.Length.ShouldBeLessThanOrEqualTo(OutputName.MaxLength);
    }

    [Fact]
    public void Chinese_characters_survive_sanitisation()
    {
        OutputName.Sanitise("客户设计稿_v2").Value.ShouldBe("客户设计稿_v2");
    }

    [Fact]
    public void Valid_names_pass_through_unchanged()
    {
        OutputName.Sanitise("Normal Design Name").Value.ShouldBe("Normal Design Name");
    }
}

/// <summary>Pure preset-pattern-driven file naming (Epic 11100 plan §13.2).</summary>
public sealed class OutputFileNamingTests
{
    private static readonly NamingPatternSet Patterns = NamingPatternSet.DesignDefault;

    [Fact]
    public void Enhanced_pattern_matches_the_design_example()
    {
        OutputFileNaming.BuildProposedFileName(NamingArtifactKind.Enhanced, OutputName.Parse("Name"), Patterns)
            .ShouldBe("Name_HD.png");
    }

    [Fact]
    public void Cutout_pattern_matches_the_design_example()
    {
        OutputFileNaming.BuildProposedFileName(NamingArtifactKind.Cutout, OutputName.Parse("Name"), Patterns)
            .ShouldBe("Name_CUTOUT.png");
    }

    [Fact]
    public void Production_tiff_pattern_matches_the_design_example()
    {
        OutputFileNaming.BuildProposedFileName(
                NamingArtifactKind.ProductionTiff, OutputName.Parse("Name"), Patterns, targetWidthMm: 280)
            .ShouldBe("Name_280mm_CMYK_W.tif");
    }

    [Fact]
    public void Production_tiff_requires_a_target_width()
    {
        Should.Throw<ArgumentException>(() =>
            OutputFileNaming.BuildProposedFileName(NamingArtifactKind.ProductionTiff, OutputName.Parse("Name"), Patterns));
    }

    [Fact]
    public void Collision_candidates_follow_base_02_03()
    {
        OutputFileNaming.BuildCollisionCandidate("Name.png", Patterns, 1).ShouldBe("Name.png");
        OutputFileNaming.BuildCollisionCandidate("Name.png", Patterns, 2).ShouldBe("Name_02.png");
        OutputFileNaming.BuildCollisionCandidate("Name.png", Patterns, 3).ShouldBe("Name_03.png");
    }
}
