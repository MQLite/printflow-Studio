using System.IO;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Outputs;

namespace PrintFlow.Tests.Unit.Naming;

public sealed class ProductionTiffNamingTests
{
    [Theory]
    [InlineData(420, "Job_420mm_CMYK_W.tif")]
    [InlineData(297, "Job_297mm_CMYK_W.tif")]
    [InlineData(280.49, "Job_280mm_CMYK_W.tif")]
    [InlineData(280.5, "Job_281mm_CMYK_W.tif")]
    public void Existing_authority_renders_confirmed_target_width_for_any_sizing_mode(
        double confirmedTargetWidthMm, string expected)
    {
        OutputFileNaming.BuildProposedFileName(
                NamingArtifactKind.ProductionTiff,
                OutputName.Parse("Job"),
                NamingPatternSet.DesignDefault,
                confirmedTargetWidthMm)
            .Value.ShouldBe(expected);
    }

    [Fact]
    public void Workflow_call_site_supplies_confirmed_PrintDimensions_width()
    {
        string source = File.ReadAllText(Path.Combine(
            ProjectDirectory("PrintFlow.Workflow"), "Services", "SessionService.cs"));

        source.ShouldContain(
            "NamingArtifactKind.ProductionTiff, aggregate.Session.OutputName, patterns.Value, dimensions.WidthMm");
        source.ShouldNotContain("preparation.Maximum", Case.Insensitive);
    }

    private static string ProjectDirectory(string project)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }
        current.ShouldNotBeNull();
        return Path.Combine(current.FullName, "src", project);
    }
}
