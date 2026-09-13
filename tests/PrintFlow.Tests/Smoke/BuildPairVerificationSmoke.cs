using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Regression;
using Xunit.Abstractions;

namespace PrintFlow.Tests.Smoke;

/// <summary>Read-only local proof of the loaded build-pair consumer; no Product composition.</summary>
[Collection(EnvironmentVariableCollection.Name)]
public sealed class BuildPairVerificationSmoke(ITestOutputHelper output)
{
    /// <summary>Run from the receipt's concrete test DLL to check it without any live workflow.</summary>
    [Fact]
    public void Verify_the_loaded_build_pair_without_operational_setup()
    {
        string? receipt = Environment.GetEnvironmentVariable("PRINTFLOW_BUILD_PAIR_PROOF_RECEIPT");
        if (string.IsNullOrWhiteSpace(receipt)) { return; }
        string? candidate = Environment.GetEnvironmentVariable("PRINTFLOW_BUILD_PAIR_PROOF_CANDIDATE");
        candidate.ShouldNotBeNullOrWhiteSpace();
        RegressionBuildOrigin origin = RegressionBuildOrigin.Capture(receipt, candidate);
        output.WriteLine($"Verified loaded build pair {origin.PairId}; receipt SHA-256 {origin.ReceiptSha256}.");
    }
}
