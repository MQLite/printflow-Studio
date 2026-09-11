using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.App.Startup;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Automation;
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
    /// The shipped configuration selects Production against the accepted preset
    /// (Epic 11500 Part D §13).
    /// </summary>
    /// <remarks>
    /// Asserted against the committed <c>appsettings.json</c> rather than a fixture, and it was
    /// <c>Fake</c> here until Part D. That is the whole content of the activation: one value in
    /// one file, changed once every preceding gate had passed. The preset assertion is beside it
    /// on purpose — activation is not permitted to mint a new preset identity, so the version
    /// this installation is verified against is the version Part A accepted.
    /// </remarks>
    [Fact]
    public void The_shipped_configuration_runs_production_against_the_accepted_preset()
    {
        PrintFlowConfiguration configuration =
            PrintFlowConfiguration.LoadFromFile(Path.Combine(RepositoryRoot(), "appsettings.json"));

        configuration.Adapters.Mode.ShouldBe("Production");
        configuration.Preset.Version.ShouldBe("1.17.0");
    }

    /// <summary>
    /// The readiness screen resolves, and reads the same object the gate is
    /// (Epic 11500 Part C §2, §11.1).
    /// </summary>
    /// <remarks>
    /// Reference equality is the whole point. Two objects reading the workstation would be two
    /// answers about it, and the failure mode is the quiet one: a screen that says Ready beside
    /// a workflow that refuses, with nobody able to say which is right.
    /// </remarks>
    [Fact]
    public void The_readiness_screen_resolves_and_reads_the_gate_itself()
    {
        using TempApplication application = new();
        using ServiceProvider services = Compose(application);

        services.GetRequiredService<EnvironmentReadinessViewModel>().ShouldNotBeNull();
        services.GetRequiredService<IEnvironmentDiagnostics>()
            .ShouldBeSameAs(services.GetRequiredService<IEnvironmentGate>());
    }

    /// <summary>
    /// Opening the readiness screen changes no adapter (Epic 11500 Part C §12).
    /// </summary>
    /// <remarks>
    /// The composed application's adapters are Fake before a reading and Fake after one, and the
    /// same instances: reading readiness is observation, and nothing about it re-composes the
    /// graph, re-registers a port or promotes anything to production.
    /// </remarks>
    [Fact]
    public async Task Reading_readiness_changes_no_adapter()
    {
        using TempApplication application = new();
        using ServiceProvider services = Compose(application);

        IMeituProcessor meitu = services.GetRequiredService<IMeituProcessor>();
        IPhotoshopOutputProcessor photoshop = services.GetRequiredService<IPhotoshopOutputProcessor>();
        meitu.Mode.ShouldBe(AdapterExecutionMode.Fake);
        photoshop.Mode.ShouldBe(AdapterExecutionMode.Fake);

        EnvironmentReadinessViewModel screen =
            services.GetRequiredService<EnvironmentReadinessViewModel>();
        await screen.OpenAsync(CancellationToken.None);
        await screen.RefreshCommand.ExecuteAsync(null);

        screen.IsReady.ShouldBeFalse("the synthetic layout is not the accepted workstation.");

        services.GetRequiredService<IMeituProcessor>().ShouldBeSameAs(meitu);
        services.GetRequiredService<IPhotoshopOutputProcessor>().ShouldBeSameAs(photoshop);
        meitu.Mode.ShouldBe(AdapterExecutionMode.Fake);
        photoshop.Mode.ShouldBe(AdapterExecutionMode.Fake);
    }

    /// <summary>
    /// Fake work stays usable on a workstation that is not production-ready
    /// (Epic 11500 Part C §12).
    /// </summary>
    [Fact]
    public void Fake_adapters_stay_usable_when_readiness_fails()
    {
        using TempApplication application = new();
        using ServiceProvider services = Compose(application);

        services.GetRequiredService<IEnvironmentDiagnostics>().Read().Verified.ShouldBeFalse();

        services.GetRequiredService<IEnvironmentGate>()
            .Verify(AdapterExecutionMode.Fake).IsSuccess.ShouldBeTrue();
        services.GetRequiredService<IMeituProcessor>().Mode.ShouldBe(AdapterExecutionMode.Fake);
    }

    /// <summary>
    /// Composing with <c>Adapters.Mode = "Production"</c> does not authorise Production
    /// (Epic 11500 Part D §3, §5).
    /// </summary>
    /// <remarks>
    /// Part C asserted here that Production refused to compose at all, because that is what it
    /// did. Part D opened composition, and this is what has to remain true instead — the
    /// distinction the whole epic rests on. The adapters now exist in the graph; the gate is
    /// asked anyway, and on a workstation that cannot verify it says no. Composition is not
    /// permission, and <see cref="ServiceRegistration"/> pre-authorises nothing merely by having
    /// constructed something.
    /// </remarks>
    [Fact]
    public void Composing_for_production_does_not_authorise_production()
    {
        using TempApplication application = new();

        PrintFlowConfiguration configuration =
            PrintFlowConfiguration.LoadFromFile(application.ConfigurationFilePath) with
            {
                Adapters = new AdaptersConfiguration("Production"),
            };

        SqliteConnectionFactory factory = new(application.DatabasePath);
        using SqliteConnection connection = factory.Open();
        MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();

        using ServiceProvider services =
            ServiceRegistration.BuildServiceProvider(
                configuration, application.WorkspaceRoot, factory, testServices =>
                    testServices.AddSingleton<IWorkstationAutomationLeaseManager>(
                        new SqliteWorkstationAutomationLeaseManager(
                            Path.Combine(application.WorkspaceRoot, "TestAuthority", "workstation-lease.db"),
                            "test.environment-gate-composition." + Guid.NewGuid().ToString("N"))));

        services.GetRequiredService<IMeituProcessor>().Mode.ShouldBe(AdapterExecutionMode.Production);
        services.GetRequiredService<IPhotoshopOutputProcessor>().Mode
            .ShouldBe(AdapterExecutionMode.Production);

        OperationResult<PrintFlow.Domain.Results.Unit> production = services
            .GetRequiredService<IEnvironmentGate>().Verify(AdapterExecutionMode.Production);

        production.IsFailure.ShouldBeTrue(
            "having composed the adapters must never mean they may run.");
        production.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
    }

    private static ServiceProvider Compose(TempApplication application)
    {
        PrintFlowConfiguration configuration =
            PrintFlowConfiguration.LoadFromFile(application.ConfigurationFilePath);

        SqliteConnectionFactory factory = new(application.DatabasePath);
        using SqliteConnection connection = factory.Open();
        MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();

        return ServiceRegistration.BuildServiceProvider(
            configuration, application.WorkspaceRoot, factory, services =>
                services.AddSingleton<IWorkstationAutomationLeaseManager>(
                    new SqliteWorkstationAutomationLeaseManager(
                        Path.Combine(application.WorkspaceRoot, "TestAuthority", "workstation-lease.db"),
                        "test.environment-gate-composition." + Guid.NewGuid().ToString("N"))));
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
