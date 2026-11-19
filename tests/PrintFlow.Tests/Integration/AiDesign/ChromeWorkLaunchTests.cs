using System.IO;
using PrintFlow.Infrastructure.AiDesign;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.AiDesign;

public sealed class ChromeWorkLaunchTests
{
    [Fact]
    public void Cold_launch_targets_only_the_registered_extension_and_named_profile()
    {
        using TempWorkspace files = new();
        var configuration = new AiDesignBridgeConfiguration(1, "1.0.0", NativeHostBoundaryTests.Origin,
            files.Root, files.Root, AiDesignBridgeConfiguration.TextSubmissionTrialContract, "Profile 2");
        string executable = files.CreateSourceFile("chrome.exe", [1]);
        const string nonce = "0123456789ABCDEF0123456789ABCDEF";
        var launch = ChromeWorkTextSubmission.CreateLaunchInfo(configuration, executable, nonce);
        launch.IsSuccess.ShouldBeTrue();
        launch.Value.FileName.ShouldBe(executable);
        launch.Value.UseShellExecute.ShouldBeFalse();
        launch.Value.ArgumentList.ShouldBe(new[]
        {
            "--profile-directory=Profile 2", "--new-window",
            NativeHostBoundaryTests.Origin + "connect.html#" + nonce
        });
        ChromeWorkTextSubmission.CreateLaunchInfo(configuration with { ChromeProfileDirectory = "..\\Other" }, executable, nonce)
            .IsFailure.ShouldBeTrue();
        ChromeWorkTextSubmission.CreateLaunchInfo(configuration with { VerifiedWorkContractHash = null }, executable, nonce)
            .IsFailure.ShouldBeTrue();
    }
}
