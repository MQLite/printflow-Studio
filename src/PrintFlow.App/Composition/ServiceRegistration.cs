using Microsoft.Extensions.DependencyInjection;
using PrintFlow.Domain.Ids;
using PrintFlow.Infrastructure.Preset;
using PrintFlow.App.ViewModels;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.App.Composition;

/// <summary>
/// The composition root: the only place in <c>PrintFlow.App</c> permitted to see
/// <c>PrintFlow.Infrastructure</c>.
/// </summary>
/// <remarks>
/// The standard composition-root exemption, made checkable: an architecture test asserts
/// that no type outside this namespace references an Infrastructure type
/// (Epic 11100 plan §4.2).
/// </remarks>
public static class ServiceRegistration
{
    /// <summary>Registers everything the shell needs to start.</summary>
    public static ServiceProvider BuildServiceProvider()
    {
        ServiceCollection services = new();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IIdGenerator>(SystemIdGenerator.Instance);
        services.AddSingleton<IWorkflowEngine>(WorkflowEngine.Instance);

        // Nothing has been hash-verified at this point, so the provider trusts nothing and
        // production adapters stay refused. Real preset loading is Task 11100.0.
        services.AddSingleton<IWorkstationPresetProvider>(new ConfiguredPresetProvider());

        services.AddTransient<ShellViewModel>();

        return services.BuildServiceProvider();
    }
}
