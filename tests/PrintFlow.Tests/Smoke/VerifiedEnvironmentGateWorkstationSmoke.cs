using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Workflow.Ports;
using Xunit.Abstractions;

namespace PrintFlow.Tests.Smoke;

/// <summary>
/// The controlled live gate proof: the normal registered <see cref="IEnvironmentGate"/>, asked
/// for Production authorisation on this workstation (Epic 11500 Part B §25).
/// </summary>
/// <remarks>
/// Opt-in and inert by default, for the same reason Part A's observation smoke is: the signed
/// preset and the accepted binaries exist on exactly one machine, and a suite that depended on
/// them would fail everywhere else. Set <c>PRINTFLOW_WORKSTATION_VERIFY=1</c> to run it.
/// <para>
/// <b>What makes this the gate rather than a re-creation of it.</b> The graph is built by the
/// real <see cref="ServiceRegistration"/> from the real committed <c>appsettings.json</c> and the
/// real workspace root, and the gate is resolved from that graph — so what answers here is the
/// object the application composes, wired the way the application wires it. The one substitution
/// is the database, redirected to a throwaway file: a read-only readiness question has no
/// business opening the production database, and the environment gate does not consult it.
/// </para>
/// <para>
/// <b>It enables nothing.</b> <c>Adapters.Mode</c> is not touched and is asserted to still be
/// <c>Fake</c>, so the application this smoke has just proved would authorise Production goes on
/// composing fake adapters and never asks. No Photoshop or Meitu job is run to prove the gate;
/// the gate's whole contract is that it decides without launching anything (§16, §27).
/// </para>
/// </remarks>
public sealed class VerifiedEnvironmentGateWorkstationSmoke(ITestOutputHelper output)
{
    private const string EnableVariable = "PRINTFLOW_WORKSTATION_VERIFY";

    [Fact]
    public void Ask_the_registered_gate_for_production_authorisation_on_this_workstation()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            // Inert by design; see the class remarks.
            return;
        }

        string repositoryRoot = RepositoryRoot();
        PrintFlowConfiguration configuration =
            PrintFlowConfiguration.LoadFromFile(Path.Combine(repositoryRoot, "appsettings.json"));

        string scratchDatabase = Path.Combine(
            Path.GetTempPath(), "printflow-gate-smoke-" + Guid.NewGuid().ToString("N"), "smoke.db");
        Directory.CreateDirectory(Path.GetDirectoryName(scratchDatabase)!);

        try
        {
            SqliteConnectionFactory factory = new(scratchDatabase);
            using (SqliteConnection connection = factory.Open())
            {
                MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();
            }

            using ServiceProvider services = ServiceRegistration.BuildServiceProvider(
                configuration, Path.GetFullPath(configuration.Workspace.Root), factory);

            IEnvironmentGate gate = services.GetRequiredService<IEnvironmentGate>();
            IEnvironmentDiagnostics diagnostics = services.GetRequiredService<IEnvironmentDiagnostics>();

            EnvironmentReadinessReport report = diagnostics.Read();
            OperationResult<PrintFlow.Domain.Results.Unit> production =
                gate.Verify(AdapterExecutionMode.Production);
            OperationResult<PrintFlow.Domain.Results.Unit> fake =
                gate.Verify(AdapterExecutionMode.Fake);

            output.WriteLine($"configured preset : {configuration.Preset.Id} {configuration.Preset.Version}");
            output.WriteLine($"configured mode   : Adapters.Mode = {configuration.Adapters.Mode}");
            output.WriteLine($"gate type         : {gate.GetType().Name}");
            output.WriteLine($"preset identity   : {report.PresetIdentity ?? "(unverified)"}");
            output.WriteLine($"observed at       : {report.ObservedAt:u}");
            output.WriteLine(string.Empty);

            foreach (EnvironmentCheckReport check in report.Checks)
            {
                output.WriteLine(
                    $"[{check.Status,-8}] {(check.IsBlocking ? "blocking" : "advisory")} {check.CheckKey}");
                output.WriteLine($"             {check.Detail}");
            }

            output.WriteLine(string.Empty);
            output.WriteLine($"verified          : {report.Verified}");
            output.WriteLine($"gate(Production)  : {(production.IsSuccess ? "ALLOWED" : "REFUSED")}");
            if (production.IsFailure)
            {
                output.WriteLine($"  code            : {production.Failure.Code}");
                output.WriteLine($"  messageKey      : {production.Failure.MessageKey}");
                foreach ((string key, string value) in production.Failure.Context.OrderBy(c => c.Key, StringComparer.Ordinal))
                {
                    output.WriteLine($"  {key} = {value}");
                }
            }

            output.WriteLine($"gate(Fake)        : {(fake.IsSuccess ? "ALLOWED" : "REFUSED")}");

            // Fake is allowed unconditionally, and this is the half that is asserted rather than
            // reported: it must hold on every machine, including one that fails verification.
            fake.IsSuccess.ShouldBeTrue();

            // The Production verdict is reported, not asserted. A drift this run found on the
            // accepted workstation is a fact to investigate, not a suite to fail — and asserting
            // it here would make the suite unrunnable anywhere else.
            //
            // What is asserted is that observing the gate enabled nothing: the configured
            // application still composes fake adapters, whatever the answer above was.
            configuration.Adapters.Mode.ShouldBe("Fake");
            services.GetRequiredService<IMeituProcessor>().Mode.ShouldBe(AdapterExecutionMode.Fake);
            services.GetRequiredService<IPhotoshopOutputProcessor>().Mode.ShouldBe(AdapterExecutionMode.Fake);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(Path.GetDirectoryName(scratchDatabase)!, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A temp database that outlives one smoke run is not worth failing it over.
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
