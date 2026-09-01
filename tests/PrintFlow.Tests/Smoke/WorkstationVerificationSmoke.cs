using System.IO;
using PrintFlow.Domain.Files;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Verification;
using Xunit.Abstractions;

namespace PrintFlow.Tests.Smoke;

/// <summary>
/// The live factual workstation observation (Epic 11500 Part A §24).
/// </summary>
/// <remarks>
/// Opt-in and inert by default: an ordinary <c>dotnet test</c> run does nothing here, because
/// the signed preset and the accepted binaries exist on exactly one machine and a suite that
/// depended on them would fail everywhere else.
/// <para>
/// Set <c>PRINTFLOW_WORKSTATION_VERIFY=1</c> to run it. What it then does is read: it hashes the
/// configured manifest, the evidence it vouches for, the two accepted binaries and the canonical
/// Action file, and it reads the current session, display and user UI language. It starts no
/// application, sends no input, writes nothing, and changes no file attribute. It also enables
/// nothing — <c>FoundationEnvironmentGate</c> refuses Production regardless of what it reports,
/// and this observation is not wired to anything that could change that (§20, §24).
/// </para>
/// </remarks>
public sealed class WorkstationVerificationSmoke(ITestOutputHelper output)
{
    private const string EnableVariable = "PRINTFLOW_WORKSTATION_VERIFY";

    [Fact]
    public void Observe_the_current_accepted_workstation()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            // Inert by design; see the class remarks.
            return;
        }

        string repositoryRoot = RepositoryRoot();
        PrintFlowConfiguration configuration =
            PrintFlowConfiguration.LoadFromFile(Path.Combine(repositoryRoot, "appsettings.json"));

        string manifestPath = Path.Combine(configuration.Workspace.Root, configuration.Preset.Path);

        ProductionWorkstationVerifier verifier = ProductionWorkstationVerifier.ForWorkstation(
            manifestPath,
            configuration.Preset.Id,
            configuration.Preset.Version,
            Sha256.Parse(configuration.Preset.ExpectedSha256),
            configuration.Workspace.Root,
            TimeProvider.System);

        WorkstationVerificationResult result = verifier.Verify();

        output.WriteLine($"configured preset : {configuration.Preset.Id} {configuration.Preset.Version}");
        output.WriteLine($"configured mode   : Adapters.Mode = {configuration.Adapters.Mode}");
        output.WriteLine($"manifest          : {manifestPath}");
        output.WriteLine(string.Empty);

        foreach (WorkstationCheckResult check in result.Checks)
        {
            output.WriteLine($"[{check.Outcome,-8}] {check.Kind,-9} {check.Check}");
            output.WriteLine($"             expected: {check.Expected ?? "-"}");
            output.WriteLine($"             observed: {check.Observed ?? "-"}");
            output.WriteLine($"             {check.Explanation}");
        }

        output.WriteLine(string.Empty);
        output.WriteLine(result.Describe());

        // Observation only. The verdict is reported, never asserted: this run exists to state
        // what the workstation is, and a drift it found would be a fact to report rather than a
        // test to fail (§24). The one thing that is asserted is that the run stayed read-only —
        // Production is not enabled by observing it.
        configuration.Adapters.Mode.ShouldBe("Fake");
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
