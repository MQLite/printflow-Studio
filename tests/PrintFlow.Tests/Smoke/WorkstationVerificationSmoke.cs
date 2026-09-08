using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.Domain.Files;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Infrastructure.Verification;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;
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
/// nothing: Part B's <c>VerifiedEnvironmentGate</c> is what turns a result like this into
/// permission, it re-asks the verifier itself on every Production request, and nothing this
/// observation does could change what it would find (§20, §24).
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
        // test to fail (§24).
        //
        // The configured mode is printed above and no longer asserted. It read `ShouldBe("Fake")`
        // while the point was that observing the workstation could not enable Production; since
        // Epic 11500 Part D the installation ships Production, and the claim that survives is the
        // one this whole file rests on — nothing here composes an adapter, so nothing here can
        // change what runs, in either direction.
    }

    /// <summary>
    /// Explicitly drives the bounded live phase. This is separate from the passive smoke so an
    /// ordinary readiness observation can never launch an application by surprise.
    /// </summary>
    [Fact]
    public async Task Run_the_explicit_live_application_verification()
    {
        if (Environment.GetEnvironmentVariable("PRINTFLOW_WORKSTATION_LIVE_VERIFY") != "1") return;

        string repositoryRoot = RepositoryRoot();
        PrintFlowConfiguration configuration = PrintFlowConfiguration.LoadFromFile(
            Path.Combine(repositoryRoot, "appsettings.json"));
        string scratch = Path.Combine(
            Path.GetTempPath(), "printflow-live-readiness-" + Guid.NewGuid().ToString("N"));
        string database = Path.Combine(scratch, "smoke.db");
        Directory.CreateDirectory(scratch);

        try
        {
            SqliteConnectionFactory connections = new(database);
            using (SqliteConnection connection = connections.Open())
                MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();
            using ServiceProvider services = ServiceRegistration.BuildServiceProvider(
                configuration, Path.GetFullPath(configuration.Workspace.Root), connections);

            IEnvironmentDiagnostics diagnostics = services.GetRequiredService<IEnvironmentDiagnostics>();
            EnvironmentReadinessReport report = await diagnostics.RunLiveChecksAsync(CancellationToken.None);

            output.WriteLine($"verified          : {report.Verified}");
            output.WriteLine($"preset            : {report.PresetIdentity ?? "(unverified)"}");
            output.WriteLine($"observed           : {report.ObservedAt:u}");
            foreach (EnvironmentCheckReport check in report.Checks)
            {
                output.WriteLine($"[{check.Status,-8}] {check.Phase,-15} {check.CheckKey}");
                output.WriteLine($"             expected: {check.Expected ?? "-"}");
                output.WriteLine($"             current : {check.Current ?? "-"}");
                output.WriteLine($"             {check.Detail}");
            }

            AutomationLockState lockState = (await services.GetRequiredService<ISessionRepository>()
                .GetAutomationLockAsync(CancellationToken.None)).Value;
            output.WriteLine($"lock free          : {!lockState.IsHeld}");
            lockState.IsHeld.ShouldBeFalse("every terminal live-verification path must release the shared lock");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                output.WriteLine($"scratch retained   : {scratch} ({ex.Message})");
            }
        }
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
