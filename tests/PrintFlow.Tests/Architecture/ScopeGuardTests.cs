using System.Reflection;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Engine;

namespace PrintFlow.Tests.Architecture;

/// <summary>
/// Guards against the two ways this codebase could quietly stop being what the design says
/// it is: scope creep into concepts the MVP excludes, and the workflow engine acquiring a
/// side effect.
/// </summary>
public sealed class ScopeGuardTests
{
    private static readonly Assembly[] ProductAssemblies =
    [
        typeof(ProcessingSession).Assembly,
        typeof(WorkflowEngine).Assembly,
        typeof(Infrastructure.Preset.ConfiguredPresetProvider).Assembly,
        typeof(global::PrintFlow.App.App).Assembly,
    ];

    /// <summary>
    /// Job, Order, Customer, Asset and Artwork are explicit MVP exclusions (design §2.2, §5).
    /// A type with one of those names appearing anywhere means the scope has drifted.
    /// </summary>
    [Theory]
    [InlineData("Job")]
    [InlineData("Order")]
    [InlineData("Customer")]
    [InlineData("Asset")]
    [InlineData("Artwork")]
    [InlineData("User")]
    [InlineData("Role")]
    [InlineData("Permission")]
    [InlineData("ProductionQueue")]
    [InlineData("Batch")]
    public void No_excluded_concept_is_declared_as_a_type(string excluded)
    {
        List<string> offenders = [];
        foreach (Assembly assembly in ProductAssemblies)
        {
            offenders.AddRange(assembly.GetTypes()
                .Where(t => string.Equals(t.Name, excluded, StringComparison.Ordinal))
                .Select(t => t.FullName!));
        }

        offenders.ShouldBeEmpty();
    }

    /// <summary>
    /// The engine is stateless. A mutable static field would make transitions depend on
    /// history invisible to the caller and quietly break test isolation.
    /// </summary>
    [Fact]
    public void The_engine_holds_no_mutable_state()
    {
        const BindingFlags all =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        typeof(WorkflowEngine).GetFields(all)
            .Where(f => !f.IsInitOnly && !f.IsLiteral)
            .ShouldBeEmpty();
    }

    /// <summary>
    /// The UI must not be able to declare that an adapter succeeded. System command
    /// constructors are internal to <c>PrintFlow.Workflow</c>, so only that assembly and the
    /// test project (via <c>InternalsVisibleTo</c>) can raise them.
    /// </summary>
    [Theory]
    [InlineData(typeof(WorkflowCommand.System.AttemptSucceeded))]
    [InlineData(typeof(WorkflowCommand.System.AttemptFailed))]
    [InlineData(typeof(WorkflowCommand.System.AttemptInterrupted))]
    public void System_commands_expose_no_public_constructor(Type systemCommand)
    {
        systemCommand.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Where(c => c.GetParameters().Length > 0 || c.IsPublic)
            .Where(c => c.GetParameters().Length != 1 ||
                        c.GetParameters()[0].ParameterType != systemCommand)
            .ShouldBeEmpty($"{systemCommand.Name} must not be constructible from the UI");
    }

    /// <summary>
    /// There is no escape hatch that sets a state or moves a step directly; every change
    /// goes through a command the engine validates (MVP design invariant 12).
    /// </summary>
    [Fact]
    public void The_engine_exposes_no_arbitrary_state_setter()
    {
        string[] forbidden = ["SetState", "MoveToStep", "ForceState", "SetStep", "OverrideState"];

        typeof(IWorkflowEngine).GetMethods().Select(m => m.Name)
            .Intersect(forbidden)
            .ShouldBeEmpty();

        typeof(WorkflowEngine).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Select(m => m.Name)
            .Intersect(forbidden)
            .ShouldBeEmpty();
    }

    /// <summary>
    /// Nothing in the solution may reference an automation, image-processing or ORM library
    /// this Epic explicitly excludes.
    /// </summary>
    [Fact]
    public void No_forbidden_package_is_referenced_anywhere()
    {
        string[] forbidden =
        [
            "Microsoft.EntityFrameworkCore",
            "SixLabors.ImageSharp",
            "FluentAssertions",
            "Selenium.WebDriver",
            "Microsoft.Playwright",
            "Stateless",
            "Workflow.Core",
            "FlaUI.Core",
            "FlaUI.UIA3",
            "TestStack.White",
            "Interop.Photoshop",
        ];

        List<string> offenders = [];
        foreach (Assembly assembly in ProductAssemblies)
        {
            offenders.AddRange(assembly.GetReferencedAssemblies()
                .Select(a => a.Name!)
                .Intersect(forbidden)
                .Select(name => $"{assembly.GetName().Name} -> {name}"));
        }

        offenders.ShouldBeEmpty();
    }
}
