using PrintFlow.Domain.Files;
using PrintFlow.Infrastructure.Adapters.Meitu;

namespace PrintFlow.Tests.Unit.Automation;

public sealed class MeituCutoutOutputRuleTests
{
    private static readonly WorkspaceFileRef Input = WorkspaceFileRef.Create(
        "Sessions/S/Working/A_1/Name.png", WorkspaceArea.Working);

    [Fact]
    public void Exact_CUTOUT_PNG_sibling_is_accepted()
    {
        WorkspaceFileRef output = WorkspaceFileRef.Create(
            "Sessions/S/Working/A_1/Name_CUTOUT.png", WorkspaceArea.Working);

        MeituCutoutOutputRule.ValidateDestination(Input, output).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Name_CUTOUT.jpg")]
    [InlineData("Name_cutout.png")]
    [InlineData("Name_CUTOUT.PNG")]
    [InlineData("_CUTOUT.png")]
    [InlineData("Name_HD.png")]
    public void Wrong_name_or_extension_is_refused(string fileName)
    {
        WorkspaceFileRef output = WorkspaceFileRef.Create(
            $"Sessions/S/Working/A_1/{fileName}", WorkspaceArea.Working);

        MeituCutoutOutputRule.ValidateDestination(Input, output).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_different_attempt_directory_is_refused()
    {
        WorkspaceFileRef output = WorkspaceFileRef.Create(
            "Sessions/S/Working/A_2/Name_CUTOUT.png", WorkspaceArea.Working);

        MeituCutoutOutputRule.ValidateDestination(Input, output).IsFailure.ShouldBeTrue();
    }
}
