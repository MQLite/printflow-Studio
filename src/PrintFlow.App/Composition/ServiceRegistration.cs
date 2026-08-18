using Microsoft.Extensions.DependencyInjection;
using PrintFlow.App.ViewModels;

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
        services.AddTransient<ShellViewModel>();

        return services.BuildServiceProvider();
    }
}
