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
            // What is asserted is that asking the gate changed nothing: the application composes
            // the adapters the committed configuration named, whatever the answer above was. This
            // read "still Fake" until Epic 11500 Part D activated Production, and it is the same
            // claim in the stronger direction now — the gate reports on the composition, it does
            // not decide it.
            services.GetRequiredService<IMeituProcessor>().Mode.ShouldBe(ComposedMode(configuration));
            services.GetRequiredService<IPhotoshopOutputProcessor>().Mode
                .ShouldBe(ComposedMode(configuration));
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

    /// <summary>
    /// The controlled live readiness-screen proof: what an operator on this workstation would
    /// actually read (Epic 11500 Part C §14).
    /// </summary>
    /// <remarks>
    /// Same opt-in, same real graph, same throwaway database. What this adds to the gate proof
    /// above is the operator's own view of it — the composed
    /// <see cref="IEnvironmentDiagnostics"/>, resolved into the real screen, rendered into the
    /// strings a person reads, and refreshed once to show the reading really is repeatable.
    /// <para>
    /// <b>It touches no external application.</b> Nothing here launches Photoshop or Meitu, sends
    /// automation input, runs a production workflow, modifies a workstation setting or closes a
    /// document. Reading readiness is a file-and-Win32 question, which is the whole reason the
    /// gate was built to answer it without bringing an application onto the screen.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task Read_the_production_readiness_screen_on_this_workstation()
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
            Path.GetTempPath(), "printflow-readiness-smoke-" + Guid.NewGuid().ToString("N"), "smoke.db");
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

            PrintFlow.App.ViewModels.EnvironmentReadinessViewModel screen =
                services.GetRequiredService<PrintFlow.App.ViewModels.EnvironmentReadinessViewModel>();

            await screen.OpenAsync(CancellationToken.None);

            output.WriteLine($"{screen.Heading}");
            output.WriteLine($"{screen.StatusText}");
            output.WriteLine($"{screen.AdvisorySummary}");
            output.WriteLine($"{screen.PresetLabel}: {screen.PresetIdentity}");
            output.WriteLine($"{screen.ObservedAtLabel}: {screen.ObservedAt}");
            output.WriteLine(string.Empty);

            output.WriteLine($"{screen.BlockingHeading}");
            if (screen.HasBlockingFailures)
            {
                foreach (PrintFlow.App.ViewModels.EnvironmentCheckRow row in screen.BlockingFailures)
                {
                    output.WriteLine($"  {row.Name} [{row.SupportKey}] — {row.Status}");
                    output.WriteLine($"    {row.Explanation}");
                }
            }
            else
            {
                output.WriteLine($"  {screen.NoBlockingFailuresText}");
            }

            output.WriteLine(string.Empty);
            output.WriteLine($"{screen.AdvisoriesHeading}");
            foreach (PrintFlow.App.ViewModels.EnvironmentCheckRow row in screen.Advisories)
            {
                output.WriteLine($"  {row.Name} [{row.SupportKey}] — {row.Status}");
                output.WriteLine($"    {row.Explanation}");
            }

            output.WriteLine(string.Empty);
            output.WriteLine($"{screen.ChecksHeading}");
            foreach (PrintFlow.App.ViewModels.EnvironmentCheckRow row in screen.Checks)
            {
                output.WriteLine(
                    $"  [{row.Status,-8}] {row.Classification,-9} {row.Name} [{row.SupportKey}]");
                output.WriteLine($"      {row.Detail}");
            }

            output.WriteLine(string.Empty);
            output.WriteLine(screen.RefreshScope);
            output.WriteLine(screen.RestartRequirement);

            // Looking again is the only thing the screen can do, and it must be repeatable.
            string firstReading = screen.ObservedAt;
            await screen.RefreshCommand.ExecuteAsync(null);
            output.WriteLine(string.Empty);
            output.WriteLine($"after refresh     : {screen.StatusText} / {screen.ObservedAt} (was {firstReading})");

            // Asserted rather than reported: the screen resolved real wording rather than raw
            // resource keys, and it produced a reading at all.
            screen.HasReport.ShouldBeTrue();
            screen.Heading.ShouldNotBe("Environment_Heading");
            screen.RestartRequirement.ShouldNotBe("Environment_RestartRequired");
            screen.Checks.ShouldNotBeEmpty();
            screen.Checks.ShouldAllBe(
                row => !row.Name.StartsWith("EnvironmentCheckName_", StringComparison.Ordinal));

            // And reading it changed nothing: the same adapters the committed configuration
            // composed, before the reading and after it.
            services.GetRequiredService<IMeituProcessor>().Mode.ShouldBe(ComposedMode(configuration));
            services.GetRequiredService<IPhotoshopOutputProcessor>().Mode
                .ShouldBe(ComposedMode(configuration));
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

    /// <summary>
    /// The execution mode the committed configuration composes, whatever it currently says.
    /// </summary>
    /// <remarks>
    /// Derived rather than hard-coded so this smoke states a claim about the shipped
    /// configuration instead of a claim about one particular value of it: whether the
    /// installation ships Fake or Production, observing readiness must leave it exactly there.
    /// </remarks>
    private static AdapterExecutionMode ComposedMode(PrintFlowConfiguration configuration) =>
        configuration.Adapters.Mode == "Production"
            ? AdapterExecutionMode.Production
            : AdapterExecutionMode.Fake;

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
