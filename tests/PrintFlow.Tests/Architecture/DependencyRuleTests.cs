using System.Reflection;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Engine;

namespace PrintFlow.Tests.Architecture;

/// <summary>
/// The dependency directions from Epic 11100 plan §5, asserted rather than hoped for.
/// </summary>
/// <remarks>
/// Reflection over assembly references is used in preference to an architecture-test package:
/// the rules are few and blunt, and a direct assertion is easier to read than a fluent rule
/// engine — with one fewer dependency in a repository that deliberately keeps its package
/// list short.
/// </remarks>
public sealed class DependencyRuleTests
{
    private const string DomainAssembly = "PrintFlow.Domain";
    private const string WorkflowAssembly = "PrintFlow.Workflow";
    private const string InfrastructureAssembly = "PrintFlow.Infrastructure";
    private const string AppAssembly = "PrintFlow.App";

    private static Assembly Domain => typeof(ProcessingSession).Assembly;

    private static Assembly WorkflowLayer => typeof(WorkflowEngine).Assembly;

    private static Assembly Infrastructure =>
        typeof(Infrastructure.Preset.ConfiguredPresetProvider).Assembly;

    private static Assembly Shell => typeof(global::PrintFlow.App.App).Assembly;

    private static string[] ReferencesOf(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

    // -----------------------------------------------------------------------------
    // Domain
    // -----------------------------------------------------------------------------

    [Fact]
    public void Domain_references_no_other_PrintFlow_assembly()
    {
        ReferencesOf(Domain)
            .Where(name => name.StartsWith("PrintFlow", StringComparison.Ordinal))
            .ShouldBeEmpty();
    }

    [Fact]
    public void Domain_references_no_third_party_package()
    {
        string[] allowedPrefixes = ["System", "Microsoft.CSharp", "netstandard", "mscorlib", "WindowsBase"];

        ReferencesOf(Domain)
            .Where(name => !allowedPrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)))
            .ShouldBeEmpty();
    }

    [Fact]
    public void Domain_contains_no_Windows_UI_type()
    {
        ReferencesOf(Domain).ShouldNotContain("PresentationFramework");
        ReferencesOf(Domain).ShouldNotContain("PresentationCore");
    }

    // -----------------------------------------------------------------------------
    // Workflow
    // -----------------------------------------------------------------------------

    [Fact]
    public void Workflow_references_Domain_only()
    {
        ReferencesOf(WorkflowLayer)
            .Where(name => name.StartsWith("PrintFlow", StringComparison.Ordinal))
            .ShouldBe([DomainAssembly]);
    }

    [Fact]
    public void Workflow_does_not_reference_Infrastructure()
    {
        ReferencesOf(WorkflowLayer).ShouldNotContain(InfrastructureAssembly);
    }

    [Fact]
    public void Workflow_references_no_WPF_SQLite_or_automation_library()
    {
        string[] forbidden =
        [
            "PresentationFramework",
            "PresentationCore",
            "System.Windows.Forms",
            "Microsoft.Data.Sqlite",
            "SQLitePCLRaw.core",
            "Microsoft.EntityFrameworkCore",
            "UIAutomationClient",
            "UIAutomationTypes",
            "FlaUI.Core",
            "SixLabors.ImageSharp",
        ];

        string[] actual = ReferencesOf(WorkflowLayer);
        actual.Intersect(forbidden).ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------
    // Infrastructure and App
    // -----------------------------------------------------------------------------

    [Fact]
    public void Infrastructure_does_not_reference_the_shell()
    {
        ReferencesOf(Infrastructure).ShouldNotContain(AppAssembly);
    }

    [Fact]
    public void Infrastructure_declares_no_WPF_window_or_control()
    {
        string[] wpfBaseTypes = ["Window", "UserControl", "Page", "Application"];

        IEnumerable<Type> offenders = Infrastructure.GetTypes().Where(type =>
        {
            for (Type? b = type.BaseType; b is not null; b = b.BaseType)
            {
                if (b.Namespace?.StartsWith("System.Windows", StringComparison.Ordinal) == true &&
                    wpfBaseTypes.Contains(b.Name))
                {
                    return true;
                }
            }

            return false;
        });

        offenders.ShouldBeEmpty();
    }

    /// <summary>
    /// The composition-root exemption, made checkable: <c>App</c> may reference
    /// Infrastructure, but only from <c>PrintFlow.App.Composition</c>.
    /// </summary>
    [Fact]
    public void Only_the_composition_root_touches_Infrastructure()
    {
        const string compositionNamespace = "PrintFlow.App.Composition";

        List<string> offenders = [];
        foreach (Type type in Shell.GetTypes())
        {
            if (type.Namespace?.StartsWith(compositionNamespace, StringComparison.Ordinal) == true)
            {
                continue;
            }

            if (ReferencedTypes(type).Any(t =>
                    t.Assembly.GetName().Name == InfrastructureAssembly))
            {
                offenders.Add(type.FullName!);
            }
        }

        offenders.ShouldBeEmpty();
    }

    /// <summary>Types appearing in a type's signatures: bases, fields, properties and members.</summary>
    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        const BindingFlags all =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        if (type.BaseType is not null)
        {
            yield return type.BaseType;
        }

        foreach (Type i in type.GetInterfaces())
        {
            yield return i;
        }

        foreach (FieldInfo field in type.GetFields(all))
        {
            yield return field.FieldType;
        }

        foreach (PropertyInfo property in type.GetProperties(all))
        {
            yield return property.PropertyType;
        }

        foreach (MethodInfo method in type.GetMethods(all))
        {
            if (method.DeclaringType != type)
            {
                continue;
            }

            yield return method.ReturnType;
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (ConstructorInfo constructor in type.GetConstructors(all))
        {
            foreach (ParameterInfo parameter in constructor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }
    }
}
