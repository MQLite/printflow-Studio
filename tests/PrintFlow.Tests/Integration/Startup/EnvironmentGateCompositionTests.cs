using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.App.Startup;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Infrastructure.Startup;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Startup;

/// <summary>
/// How the verified environment gate is wired into the application graph
/// (Epic 11500 Part B §14, §15, §28.18–§28.22).
/// </summary>
/// <remarks>
/// The subject is the real <see cref="ServiceRegistration"/> and the real
/// <see cref="ApplicationStartup"/>, against a throwaway installed layout. The synthetic
/// <c>TempApplication</c> preset is a naming preset rather than a workstation manifest, which
/// makes it the honest test of §15: on this "workstation" Production verification cannot pass,
/// and the point is that the shell opens anyway.
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class EnvironmentGateCompositionTests
{
    /// <summary>
    /// Exactly one <see cref="IEnvironmentGate"/> is registered, and it is the verified one
    /// (§14, §28.19).
    /// </summary>
    /// <remarks>
    /// Two competing registrations would be the worst outcome available here: the container
    /// resolves the last one, so a permissive gate added in a hurry would win silently while the
    /// verified one stayed in the file where everybody could see it and believe it.
    /// </remarks>
    [Fact]
    public void Exactly_one_environment_gate_is_registered_and_it_consults_the_verifier()
    {
        using TempApplication application = new();
        using ServiceProvider services = Compose(application);

        IEnvironmentGate gate = services.GetRequiredService<IEnvironmentGate>();

        services.GetServices<IEnvironmentGate>().ShouldHaveSingleItem();
        gate.ShouldBeOfType<PrintFlow.Infrastructure.Gate.VerifiedEnvironmentGate>();

        // The diagnostics seam is the same object, so the process has exactly one consumer of
        // workstation verification rather than two that could drift apart.
        services.GetRequiredService<IEnvironmentDiagnostics>().ShouldBeSameAs(gate);
    }

    /// <summary>
    /// The registered gate authorises Fake and refuses Production on an unverified workstation
    /// (§11, §14).
    /// </summary>
    [Fact]
    public void The_registered_gate_allows_fake_and_refuses_production_here()
    {
        using TempApplication application = new();
        using ServiceProvider services = Compose(application);

        IEnvironmentGate gate = services.GetRequiredService<IEnvironmentGate>();

        gate.Verify(AdapterExecutionMode.Fake).IsSuccess.ShouldBeTrue();

        OperationResult<PrintFlow.Domain.Results.Unit> production = gate.Verify(AdapterExecutionMode.Production);
        production.IsFailure.ShouldBeTrue(
            "the synthetic layout is not the accepted production workstation.");
        production.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
    }

    /// <summary>
    /// Startup succeeds and the shell opens even though Production verification cannot pass
    /// (§15, §28.18).
    /// </summary>
    /// <remarks>
    /// The rule this pins is small and easy to lose: a display mismatch must not become "PrintFlow
    /// cannot start". Verification failure closes Production; it does not close the application,
    /// which is exactly the application an operator needs open in order to see why the workstation
    /// is failing — and, with <c>Adapters.Mode</c> still Fake, the application they are using for
    /// everything else.
    /// </remarks>
    [Fact]
    public async Task Startup_succeeds_and_the_shell_opens_when_production_verification_cannot_pass()
    {
        using TempApplication application = new();
        using FakeSingleInstanceGuard guard = new(SingleInstanceOutcome.Acquired);

        using StartupResult result = await new ApplicationStartup(
                guard, application.ConfigurationFilePath)
            .RunAsync(CancellationToken.None);

        result.Status.CanShowShell.ShouldBeTrue();
        result.Status.Failure.ShouldBeNull();
        result.Services.ShouldNotBeNull();

        // The gate is present and refusing; the shell is present and usable.
        result.Services!.GetRequiredService<IEnvironmentGate>()
            .Verify(AdapterExecutionMode.Production).IsFailure.ShouldBeTrue();
        result.Services.GetRequiredService<WorkflowSelectionViewModel>().Workflows.Count.ShouldBe(3);
    }

    /// <summary>
    /// Composing the graph verifies nothing; the first read happens on the first request
    /// (§14, §15).
    /// </summary>
    /// <remarks>
    /// Registration that hashed two application binaries and a manifest would put a second of
    /// file I/O into every application launch to answer a question nobody has asked yet — and,
    /// worse, would tempt a later change to remember the answer.
    /// </remarks>
    [Fact]
    public void Composing_the_graph_reads_no_workstation_file()
    {
        using TempApplication application = new();
        using ServiceProvider services = Compose(application);

        // Resolving is construction only. The manifest under the synthetic layout is not a
        // workstation manifest at all, so a registration that read it eagerly would have thrown.
        services.GetRequiredService<IEnvironmentGate>().ShouldNotBeNull();
        services.GetRequiredService<IEnvironmentDiagnostics>().ShouldNotBeNull();
    }

    /// <summary>
    /// The shipped configuration still selects Fake and the accepted preset (§27, §28.22, §33).
    /// </summary>
    /// <remarks>
    /// A hard PASS condition of this slice, asserted against the committed
    /// <c>appsettings.json</c> rather than against a fixture: nothing in Part B enables Production,
    /// and nothing in it mints a new preset for gate wiring.
    /// </remarks>
    [Fact]
    public void The_shipped_configuration_still_runs_fake_against_the_accepted_preset()
    {
        PrintFlowConfiguration configuration =
            PrintFlowConfiguration.LoadFromFile(Path.Combine(RepositoryRoot(), "appsettings.json"));

        configuration.Adapters.Mode.ShouldBe("Fake");
        configuration.Preset.Version.ShouldBe("1.15.0");
    }

    private static ServiceProvider Compose(TempApplication application)
    {
        PrintFlowConfiguration configuration =
            PrintFlowConfiguration.LoadFromFile(application.ConfigurationFilePath);

        SqliteConnectionFactory factory = new(application.DatabasePath);
        using SqliteConnection connection = factory.Open();
        MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();

        return ServiceRegistration.BuildServiceProvider(
            configuration, application.WorkspaceRoot, factory);
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
