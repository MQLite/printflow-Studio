using System.IO;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.Composition;
using PrintFlow.App.Navigation;
using PrintFlow.App.ViewModels;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Infrastructure.Configuration;
using PrintFlow.Infrastructure.Delivery;
using PrintFlow.Infrastructure.Sqlite;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Delivery;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Startup;

/// <summary>
/// SCRUM-11145 composition only: the session screen receives the final-save coordinator and
/// both App ports. A throwaway installed layout and a GUID-owned lease are used; no startup,
/// recovery, picker, shell call or workstation read happens.
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class FinalSaveCompositionTests
{
    [Fact]
    public void Session_screen_is_composed_with_the_final_save_coordinator_and_ports()
    {
        using TempApplication application = new();
        SqliteConnectionFactory factory = new(application.DatabasePath);
        using (SqliteConnection connection = factory.Open())
            MigrationRunner.Migrate(connection).IsSuccess.ShouldBeTrue();

        using ServiceProvider services = ServiceRegistration.BuildServiceProvider(
            PrintFlowConfiguration.LoadFromFile(application.ConfigurationFilePath), application.WorkspaceRoot, factory,
            collection => collection.AddSingleton<IWorkstationAutomationLeaseManager>(new SqliteWorkstationAutomationLeaseManager(
                Path.Combine(application.WorkspaceRoot, "TestAuthority", "workstation-lease.db"),
                "test.final-save-composition." + Guid.NewGuid().ToString("N"))));

        FinalSaveCoordinator coordinator = services.GetRequiredService<FinalSaveCoordinator>();
        coordinator.Delivery.ShouldBeSameAs(services.GetRequiredService<IApprovedArtifactDeliveryService>());
        services.GetRequiredService<IDeliveryFolderPicker>().ShouldBeOfType<OpenFolderDialogPicker>();
        services.GetRequiredService<IDeliveredFileShell>().ShouldBeOfType<WindowsDeliveredFileShell>();

        SessionViewModel screen = services.GetRequiredService<SessionViewModel>();
        Field(screen, "_finalSave").ShouldBeSameAs(coordinator);
        Field(screen, "_folderPicker").ShouldBeSameAs(services.GetRequiredService<IDeliveryFolderPicker>());
        Field(screen, "_fileShell").ShouldBeSameAs(services.GetRequiredService<IDeliveredFileShell>());
        Directory.Exists(Path.Combine(application.WorkspaceRoot, "TestAuthority")).ShouldBeFalse("composition opens no lease store");
    }

    private static object? Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
}
