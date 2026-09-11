
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.App.ViewModels;
using PrintFlow.Infrastructure.Adapters.Fake;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Startup;

/// <summary>
/// What the one configured adapter mode composes (Epic 11500 Part D §2, §4, §16).
/// </summary>
/// <remarks>
/// The subject is the real <see cref="ServiceRegistration"/> against a throwaway installed
/// layout, and the mode is the only thing that changes between cases. That matters more here
/// than anywhere else in the epic: this is the slice that opens the Production branch, and the
/// failure everybody would rather not discover in production is the quiet one — a graph that
/// composes without complaint and resolves a fake where a real adapter was configured, or a real
/// adapter where a fake was.
/// <para>
/// <b>Nothing here activates anything.</b> Production is reached by handing
/// <see cref="ServiceRegistration"/> a configuration record with the mode set, exactly as §4
/// requires while the committed <c>appsettings.json</c> is still being proved. Composition is
/// also not permission: every case below composes the Production adapters on a synthetic layout
/// that could never pass workstation verification, and none of them can run anything, because
/// <c>IEnvironmentGate</c> is asked separately and on every adapter-backed step.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class ProductionCompositionTests
{
    // ---------------------------------------------------------------- §2, §16 Composition

    /// <summary>Fake composes both fake adapters and nothing else (§2).</summary>
    [Fact]
    public void Fake_mode_composes_the_fake_pair()
    {
        using TempApplication application = new("Fake");
        using ServiceProvider services = Compose(application);

        services.GetRequiredService<IMeituProcessor>().ShouldBeOfType<FakeMeituProcessor>();
        services.GetRequiredService<IPhotoshopOutputProcessor>()
            .ShouldBeOfType<FakePhotoshopOutputProcessor>();

        services.GetRequiredService<IMeituProcessor>().Mode.ShouldBe(AdapterExecutionMode.Fake);
        services.GetRequiredService<IPhotoshopOutputProcessor>().Mode.ShouldBe(AdapterExecutionMode.Fake);
    }

    /// <summary>
    /// Production composes the accepted production adapters, by concrete type (§2, §7).
    /// </summary>
    /// <remarks>
    /// The concrete types are asserted rather than the declared <c>Mode</c> alone, and that is
    /// the point of the test: a new class that returned
    /// <see cref="AdapterExecutionMode.Production"/> would satisfy a mode check while being a
    /// second production adapter nobody accepted. What must resolve here are the two
    /// implementations Epics 11300 and 11400 proved on the live workstation.
    /// </remarks>
    [Fact]
    public void Production_mode_composes_the_accepted_production_pair()
    {
        using TempApplication application = new("Production");
        using ServiceProvider services = Compose(application);

        services.GetRequiredService<IMeituProcessor>().ShouldBeOfType<ProductionMeituProcessor>();
        services.GetRequiredService<IPhotoshopOutputProcessor>()
            .ShouldBeOfType<ProductionPhotoshopOutputProcessor>();

        services.GetRequiredService<IMeituProcessor>().Mode.ShouldBe(AdapterExecutionMode.Production);
        services.GetRequiredService<IPhotoshopOutputProcessor>().Mode
            .ShouldBe(AdapterExecutionMode.Production);
    }

    /// <summary>
    /// One mode decides both processors; there is no arrangement that mixes them (§2, §7).
    /// </summary>
    /// <remarks>
    /// The gate asks one question about one workstation (Part B §13). A composition that could
    /// answer it for Photoshop and not for Meitu would make that single answer meaningless, so
    /// the rule is asserted as the thing it actually is: whatever the mode, the two adapters
    /// agree with each other and with the configuration.
    /// </remarks>
    [Theory]
    [InlineData("Fake", AdapterExecutionMode.Fake)]
    [InlineData("Production", AdapterExecutionMode.Production)]
    public void Both_processors_follow_the_same_configured_mode(string mode, AdapterExecutionMode expected)
    {
        using TempApplication application = new(mode);
        using ServiceProvider services = Compose(application);

        IMeituProcessor meitu = services.GetRequiredService<IMeituProcessor>();
        IPhotoshopOutputProcessor photoshop = services.GetRequiredService<IPhotoshopOutputProcessor>();

        meitu.Mode.ShouldBe(expected);
        photoshop.Mode.ShouldBe(expected);
        meitu.Mode.ShouldBe(photoshop.Mode);
    }

    /// <summary>
    /// Exactly one implementation of each port is registered (§2, §7).
    /// </summary>
    /// <remarks>
    /// Two registrations would resolve to the last one added, so a fallback added "just in case"
    /// would win silently while the intended registration stayed in the file for everybody to
    /// read and believe.
    /// </remarks>
    [Theory]
    [InlineData("Fake")]
    [InlineData("Production")]
    public void Exactly_one_adapter_is_registered_per_port(string mode)
    {
        using TempApplication application = new(mode);
        using ServiceProvider services = Compose(application);

        services.GetServices<IMeituProcessor>().ShouldHaveSingleItem();
        services.GetServices<IPhotoshopOutputProcessor>().ShouldHaveSingleItem();
    }

    // ---------------------------------------------------------------- §2, §4.3

    /// <summary>
    /// An unknown, empty or absent mode fails closed (§2, §4.3).
    /// </summary>
    /// <remarks>
    /// Every one of these is a plausible edit: a typo, a case mistake, a half-finished rename, a
    /// deleted section. None of them may resolve to a working application, and in particular none
    /// may resolve to Fake — an installation whose configuration no longer says what it means
    /// must stop, not guess.
    /// </remarks>
    [Theory]
    [InlineData("production")]
    [InlineData("PRODUCTION")]
    [InlineData("Prod")]
    [InlineData("Hybrid")]
    [InlineData("")]
    [InlineData("   ")]
    public void An_unrecognised_mode_refuses_to_compose(string mode)
    {
        using TempApplication application = new();
        PrintFlowConfiguration configuration = ConfigurationFor(application, mode);

        Should.Throw<NotSupportedException>(() => Build(application, configuration))
            .Message.ShouldContain("Adapters:Mode");
    }

    /// <summary>An absent <c>Adapters</c> section fails closed too (§2, §4.3).</summary>
    /// <remarks>
    /// The one case that cannot be expressed as a wrong string. A configuration file with the
    /// section deleted parses to a null record, and the composition root has to treat that as a
    /// refusal rather than dereference it into an accident.
    /// </remarks>
    [Fact]
    public void An_absent_adapters_section_refuses_to_compose()
    {
        using TempApplication application = new();
        PrintFlowConfiguration configuration =
            PrintFlowConfiguration.LoadFromFile(application.ConfigurationFilePath) with
            {
                Adapters = null!,
            };

        Should.Throw<NotSupportedException>(() => Build(application, configuration))
            .Message.ShouldContain("(absent)");
    }

    /// <summary>
    /// Nothing is registered for either port when the mode is refused (§2).
    /// </summary>
    /// <remarks>
    /// Belt and braces on the shape of the refusal rather than only its type: the throw happens
    /// before either <c>AddSingleton</c>, so there is no partially-built collection anywhere for
    /// a future <c>catch</c> to be tempted into completing with a fake.
    /// </remarks>
    [Fact]
    public void A_refused_mode_registers_no_adapter_at_all()
    {
        List<ServiceDescriptor> captured = [];

        using TempApplication application = new();
        PrintFlowConfiguration configuration = ConfigurationFor(application, "Nonsense");

        Should.Throw<NotSupportedException>(() =>
            Build(application, configuration, services =>
            {
                foreach (ServiceDescriptor descriptor in services)
                {
                    captured.Add(descriptor);
                }
            }));

        captured.ShouldBeEmpty("the overrides hook is never reached once the mode is refused.");
    }

    // ---------------------------------------------------------------- §4.4, §5

    /// <summary>
    /// Composing Production starts no external application (§4.4, §5).
    /// </summary>
    /// <remarks>
    /// Observed directly rather than argued: the count of running Photoshop and Meitu processes
    /// is the same before composition and after both adapters have been resolved. On a machine
    /// where one of them happens to be open already the count is simply unchanged, which is the
    /// assertion either way.
    /// </remarks>
    [Fact]
    public void Composing_production_launches_no_external_application()
    {
        int before = ExternalApplicationProbe.RunningCount();

        using TempApplication application = new("Production");
        using ServiceProvider services = Compose(application);

        services.GetRequiredService<IMeituProcessor>().ShouldNotBeNull();
        services.GetRequiredService<IPhotoshopOutputProcessor>().ShouldNotBeNull();

        ExternalApplicationProbe.RunningCount().ShouldBe(before,
            "constructing an adapter must never put an application on the operator's screen.");
    }

    /// <summary>
    /// Composing Production writes nothing into the workspace (§4.4, §5).
    /// </summary>
    /// <remarks>
    /// The evidence directory is the one thing Production composition could plausibly create
    /// eagerly, and it must not: a capture directory is created when there is a capture to put in
    /// it, so an installation that never fails never grows one.
    /// </remarks>
    [Fact]
    public void Composing_production_creates_no_evidence_directory()
    {
        using TempApplication application = new("Production");
        using ServiceProvider services = Compose(application);

        services.GetRequiredService<IMeituProcessor>().ShouldNotBeNull();
        services.GetRequiredService<IPhotoshopOutputProcessor>().ShouldNotBeNull();

        Directory.Exists(Path.Combine(application.WorkspaceRoot, "Evidence")).ShouldBeFalse();
    }

    [Fact]
    public void Composing_and_resolving_the_lease_manager_does_not_initialize_its_store()
    {
        using TempApplication application = new("Production");
        string authorityDirectory = Path.Combine(application.WorkspaceRoot, "lease-authority", "nested");
        SqliteWorkstationAutomationLeaseManager manager = new(
            Path.Combine(authorityDirectory, "lease.db"),
            "test.composition." + Guid.NewGuid().ToString("N"));

        // Reading the production default is safe; the test never opens, removes or modifies it.
        SqliteWorkstationAutomationLeaseManager.DefaultDatabasePath.ShouldNotBeNullOrWhiteSpace();
        using ServiceProvider services = Compose(application, collection =>
            collection.AddSingleton<IWorkstationAutomationLeaseManager>(manager));

        services.GetRequiredService<IWorkstationAutomationLeaseManager>().ShouldBeSameAs(manager);
        services.GetRequiredService<IEnvironmentDiagnostics>().Read();
        Directory.Exists(authorityDirectory).ShouldBeFalse();
    }

    /// <summary>
    /// Composing Production reads no workstation file at all (§4.4, §5).
    /// </summary>
    /// <remarks>
    /// The strongest available form of "it launched nothing", and the reason it is worth having
    /// beside the process count: the manifest is where the accepted executable paths live, so a
    /// composition that never opens it cannot have launched the accepted applications even in
    /// principle. Proved by pointing the preset at a path that does not exist and composing
    /// anyway — the manifest, the evidence chain and the binaries are read on the first
    /// operation, not on the way up.
    /// </remarks>
    [Fact]
    public void Composing_production_reads_no_workstation_file_at_all()
    {
        using TempApplication application = new("Production");

        PrintFlowConfiguration loaded =
            PrintFlowConfiguration.LoadFromFile(application.ConfigurationFilePath);
        PrintFlowConfiguration configuration = loaded with
        {
            Preset = loaded.Preset with { Path = @"preset\this-file-does-not-exist.json" },
        };

        using ServiceProvider services = Build(application, configuration);

        services.GetRequiredService<IMeituProcessor>()
            .ShouldBeOfType<ProductionMeituProcessor>();
        services.GetRequiredService<IPhotoshopOutputProcessor>()
            .ShouldBeOfType<ProductionPhotoshopOutputProcessor>();
    }

    /// <summary>
    /// A Production installation whose workstation fails verification still opens (§5).
    /// </summary>
    /// <remarks>
    /// The rule this slice must not break while opening the Production branch. The synthetic
    /// layout can never pass workstation verification, so this is the honest shape of the case:
    /// the graph composes, the readiness screen resolves, the gate refuses Production, and the
    /// application an operator needs in order to see <i>why</i> is the application that opened.
    /// </remarks>
    [Fact]
    public void A_production_graph_still_composes_on_a_workstation_that_cannot_verify()
    {
        using TempApplication application = new("Production");
        using ServiceProvider services = Compose(application);

        services.GetRequiredService<EnvironmentReadinessViewModel>().ShouldNotBeNull();
        services.GetRequiredService<IEnvironmentDiagnostics>().Read().Verified.ShouldBeFalse();

        services.GetRequiredService<IEnvironmentGate>()
            .Verify(AdapterExecutionMode.Production).IsFailure.ShouldBeTrue();
        services.GetRequiredService<IEnvironmentGate>()
            .Verify(AdapterExecutionMode.Fake).IsSuccess.ShouldBeTrue();
    }

    // ---------------------------------------------------------------- §4.7, §16 Gate

    /// <summary>
    /// Reading diagnostics under Production changes no adapter (§4.7).
    /// </summary>
    /// <remarks>
    /// Part C proved this while the only composable mode was Fake, which left the interesting
    /// half untested: a readiness screen open beside a Production graph is exactly where somebody
    /// would later be tempted to "re-resolve" or "re-apply" a mode after a refresh. The same
    /// instances before and after are what say that never happens.
    /// </remarks>
    [Fact]
    public async Task Reading_readiness_under_production_re_composes_nothing()
    {
        using TempApplication application = new("Production");
        using ServiceProvider services = Compose(application);

        IMeituProcessor meitu = services.GetRequiredService<IMeituProcessor>();
        IPhotoshopOutputProcessor photoshop = services.GetRequiredService<IPhotoshopOutputProcessor>();

        EnvironmentReadinessViewModel screen =
            services.GetRequiredService<EnvironmentReadinessViewModel>();
        await screen.OpenAsync(CancellationToken.None);
        await screen.RefreshCommand.ExecuteAsync(null);

        screen.IsReady.ShouldBeFalse("the synthetic layout is not the accepted workstation.");

        services.GetRequiredService<IMeituProcessor>().ShouldBeSameAs(meitu);
        services.GetRequiredService<IPhotoshopOutputProcessor>().ShouldBeSameAs(photoshop);
        meitu.Mode.ShouldBe(AdapterExecutionMode.Production);
        photoshop.Mode.ShouldBe(AdapterExecutionMode.Production);
    }

    // ---------------------------------------------------------------- §14, §16 Rollback

    /// <summary>
    /// Changing the mode back to Fake and recomposing restores the fake pair (§14).
    /// </summary>
    /// <remarks>
    /// The rollback proof, expressed the way an operator performs it: the same installation, the
    /// same workspace, the same database, one configuration value changed and the process
    /// recomposed. Nothing is migrated and nothing is rewritten — which is what makes rolling
    /// back an edit and a restart rather than a recovery exercise.
    /// </remarks>
    [Fact]
    public void Rolling_the_mode_back_to_fake_restores_the_fake_pair()
    {
        using TempApplication application = new();

        using (ServiceProvider production =
               Build(application, ConfigurationFor(application, "Production")))
        {
            production.GetRequiredService<IMeituProcessor>()
                .ShouldBeOfType<ProductionMeituProcessor>();
            production.GetRequiredService<IPhotoshopOutputProcessor>()
                .ShouldBeOfType<ProductionPhotoshopOutputProcessor>();
        }

        int before = ExternalApplicationProbe.RunningCount();

        using ServiceProvider fake = Build(application, ConfigurationFor(application, "Fake"));

        fake.GetRequiredService<IMeituProcessor>().ShouldBeOfType<FakeMeituProcessor>();
        fake.GetRequiredService<IPhotoshopOutputProcessor>()
            .ShouldBeOfType<FakePhotoshopOutputProcessor>();

        ExternalApplicationProbe.RunningCount().ShouldBe(before,
            "rolling back is a recomposition, not an interaction with an external application.");
    }

    /// <summary>
    /// A composed graph's mode cannot be changed without recomposing (§14, §16 Rollback).
    /// </summary>
    /// <remarks>
    /// Asserted as the absence of a seam rather than as a failed attempt: the container exposes
    /// the adapters as singletons and offers nothing that would replace one, so "restart to
    /// change mode" is a property of the graph rather than an instruction somebody has to
    /// remember. A second provider built from a different configuration is a second graph — the
    /// first one goes on resolving what it was built with.
    /// </remarks>
    [Fact]
    public void A_running_graph_keeps_the_mode_it_was_composed_with()
    {
        using TempApplication application = new();

        using ServiceProvider first = Build(application, ConfigurationFor(application, "Fake"));
        IMeituProcessor fake = first.GetRequiredService<IMeituProcessor>();

        using ServiceProvider second = Build(application, ConfigurationFor(application, "Production"));
        second.GetRequiredService<IMeituProcessor>().Mode.ShouldBe(AdapterExecutionMode.Production);

        first.GetRequiredService<IMeituProcessor>().ShouldBeSameAs(fake);
        first.GetRequiredService<IMeituProcessor>().Mode.ShouldBe(AdapterExecutionMode.Fake);
    }

    // -----------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------

    private static PrintFlowConfiguration ConfigurationFor(TempApplication application, string mode) =>
        PrintFlowConfiguration.LoadFromFile(application.ConfigurationFilePath) with
        {
            Adapters = new AdaptersConfiguration(mode),
        };

    private static ServiceProvider Compose(TempApplication application) =>
        Build(application, PrintFlowConfiguration.LoadFromFile(application.ConfigurationFilePath));

    private static ServiceProvider Compose(
        TempApplication application,
        Action<IServiceCollection> overrides) =>
        Build(application, PrintFlowConfiguration.LoadFromFile(application.ConfigurationFilePath), overrides);

    private static ServiceProvider Build(
        TempApplication application,
        PrintFlowConfiguration configuration,
        Action<IServiceCollection>? overrides = null)
    {
        SqliteConnectionFactory factory = new(application.DatabasePath);
        using (SqliteConnection connection = factory.Open())
        {
            MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();
        }

        return ServiceRegistration.BuildServiceProvider(
            configuration, application.WorkspaceRoot, factory, services =>
            {
                services.AddSingleton<IWorkstationAutomationLeaseManager>(
                    new SqliteWorkstationAutomationLeaseManager(
                        Path.Combine(application.WorkspaceRoot, "TestAuthority", "workstation-lease.db"),
                        "test.production-composition." + Guid.NewGuid().ToString("N")));
                overrides?.Invoke(services);
            });
    }
}
