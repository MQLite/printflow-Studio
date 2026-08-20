using System.Reflection;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;
using PrintFlow.Workflow.Definitions;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Architecture;

/// <summary>
/// Where the trim slice is allowed to live, asserted rather than hoped for
/// (Epic 11200 Part B §26).
/// </summary>
/// <remarks>
/// Two of these are worth more than the rest. The trim processor must not be able to reach an
/// external application — an "improved" trim that quietly delegates a hard case to Meitu would
/// be both a scope breach and an unlocked automation call. And the Trim step must stay
/// <c>Internal</c> and not adapter-backed, because that flag is what decides whether the
/// automation lock and the environment gate apply to it.
/// </remarks>
public sealed class TrimBoundaryTests
{
    private static Assembly Domain => typeof(TrimBounds).Assembly;

    private static Assembly WorkflowLayer => typeof(WorkflowEngine).Assembly;

    private static Assembly Infrastructure =>
        typeof(Infrastructure.Preset.ConfiguredPresetProvider).Assembly;

    private static Assembly Shell => typeof(global::PrintFlow.App.App).Assembly;

    private static IEnumerable<Type> DomainTrimTypes => Domain.GetTypes()
        .Where(t => t.Namespace == "PrintFlow.Domain.Trimming");

    // -----------------------------------------------------------------------------
    // Domain: geometry only
    // -----------------------------------------------------------------------------

    [Fact]
    public void The_trim_domain_types_exist_where_they_are_supposed_to()
    {
        DomainTrimTypes.Select(t => t.Name).ShouldBe(
            ["TrimMode", "TrimOutcome", "TrimMargin", "TrimBounds", "AlphaBounds"], ignoreOrder: true);
    }

    /// <summary>
    /// Nothing in the trim domain names a file, a stream, a workspace or a pixel format.
    /// </summary>
    /// <remarks>
    /// The whole reason the bounds scan is a pure function over an alpha plane is that it can
    /// then be tested exhaustively with no disk at all. A single <c>WorkspaceFileRef</c>
    /// parameter would undo that quietly.
    /// </remarks>
    [Fact]
    public void The_trim_domain_types_name_no_file_workspace_or_imaging_type()
    {
        string[] forbiddenNamespaces = ["System.IO", "System.Windows", "PrintFlow.Workflow", "PrintFlow.Infrastructure"];

        List<string> offenders = [];
        foreach (Type type in DomainTrimTypes)
        {
            foreach (Type referenced in SignatureTypes(type))
            {
                string? ns = (referenced.IsByRef || referenced.IsArray
                    ? referenced.GetElementType() ?? referenced
                    : referenced).Namespace;

                if (ns is not null &&
                    forbiddenNamespaces.Any(f => ns.StartsWith(f, StringComparison.Ordinal)))
                {
                    offenders.Add($"{type.Name} -> {referenced.FullName}");
                }
            }
        }

        offenders.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------
    // Workflow: the port, and only the port
    // -----------------------------------------------------------------------------

    [Fact]
    public void The_workflow_layer_declares_the_trim_port_and_implements_it_nowhere()
    {
        typeof(ITrimProcessor).Assembly.ShouldBe(WorkflowLayer);

        Implementations(Domain).ShouldBeEmpty();
        Implementations(WorkflowLayer).ShouldBeEmpty();
        Implementations(Shell).ShouldBeEmpty();

        Implementations(Infrastructure).ShouldBe(["PrintFlow.Infrastructure.Imaging.DeterministicAlphaTrimProcessor"]);
    }

    /// <summary>The port carries no decoder, stride, codec or session object across the seam.</summary>
    [Fact]
    public void The_trim_request_exposes_no_imaging_or_orchestration_detail()
    {
        string[] forbidden = ["ProcessingSession", "WorkflowSnapshot", "SessionAggregate", "BitmapSource", "PixelFormat"];

        IEnumerable<string> names = typeof(TrimRequest).GetProperties().Select(p => p.PropertyType.Name)
            .Concat(typeof(TrimResult).GetProperties().Select(p => p.PropertyType.Name));

        names.Intersect(forbidden).ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------
    // Infrastructure: no route to an external application
    // -----------------------------------------------------------------------------

    /// <summary>
    /// The deterministic trim cannot reach Meitu, Photoshop or the environment gate.
    /// </summary>
    /// <remarks>
    /// Structural rather than a source-text search on purpose: a comment saying "never calls
    /// Meitu" proves nothing, whereas an absent type reference cannot be talked around.
    /// </remarks>
    [Fact]
    public void The_trim_processor_can_reach_no_external_application_adapter()
    {
        Type processor = Infrastructure.GetType("PrintFlow.Infrastructure.Imaging.DeterministicAlphaTrimProcessor")!;
        Type[] forbidden =
        [
            typeof(IMeituProcessor),
            typeof(IPhotoshopOutputProcessor),
            typeof(IEnvironmentGate),
            typeof(MeituRequest),
            typeof(PhotoshopRequest),
            typeof(AdapterOutput),
        ];

        SignatureTypes(processor).Intersect(forbidden).ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------
    // The step definition itself
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Trim stays <c>Internal</c> and not adapter-backed in every workflow that has it.
    /// </summary>
    /// <remarks>
    /// <c>IsAdapterBacked</c> is what gates the environment check and the automation lock. If
    /// Trim ever flipped to an adapter kind, deterministic in-process pixel work would start
    /// serialising behind Meitu and refusing to run on an unverified workstation, for no
    /// reason (§17).
    /// </remarks>
    [Fact]
    public void The_trim_step_is_internal_and_never_adapter_backed()
    {
        List<StepDefinition> trims = [.. WorkflowCatalog.All
            .Select(w => w.Find(StepKind.Trim))
            .Where(s => s is not null)
            .Select(s => s!)];

        trims.ShouldNotBeEmpty();
        foreach (StepDefinition trim in trims)
        {
            trim.Adapter.ShouldBe(AdapterKind.Internal);
            trim.IsAdapterBacked.ShouldBeFalse();
            trim.RequiresReview.ShouldBeTrue();
            trim.ProducesRevision.ShouldBeTrue();
            trim.IsSkippable.ShouldBeFalse();
            trim.Operation.ShouldBe(OperationKind.Trim);
        }
    }

    // -----------------------------------------------------------------------------
    // The shell
    // -----------------------------------------------------------------------------

    /// <summary>
    /// The UI drives Trim through <c>ISessionService</c>, exactly as it drives every other step.
    /// </summary>
    /// <remarks>
    /// Part B adds no review surface. A view model holding an <see cref="ITrimProcessor"/>
    /// would mean the screen had acquired its own route to pixel work, bypassing the attempt,
    /// hash and review machinery entirely.
    /// </remarks>
    [Fact]
    public void No_view_model_holds_a_trim_processor_or_trim_geometry()
    {
        Type[] forbidden = [typeof(ITrimProcessor), typeof(TrimRequest), typeof(TrimResult), typeof(TrimBounds)];

        List<string> offenders = [];
        foreach (Type type in Shell.GetTypes()
                     .Where(t => t.Namespace?.StartsWith("PrintFlow.App.ViewModels", StringComparison.Ordinal) == true))
        {
            if (SignatureTypes(type).Intersect(forbidden).Any())
            {
                offenders.Add(type.FullName!);
            }
        }

        offenders.ShouldBeEmpty();
    }

    private static string[] Implementations(Assembly assembly) =>
        [.. assembly.GetTypes()
            .Where(t => t is { IsInterface: false, IsAbstract: false } && typeof(ITrimProcessor).IsAssignableFrom(t))
            .Select(t => t.FullName!)
            .Order(StringComparer.Ordinal)];

    /// <summary>Types appearing in a type's own signatures: bases, fields, properties and members.</summary>
    private static IEnumerable<Type> SignatureTypes(Type type)
    {
        const BindingFlags all =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        if (type.BaseType is not null)
        {
            yield return type.BaseType;
        }

        foreach (FieldInfo field in type.GetFields(all))
        {
            yield return field.FieldType;
        }

        foreach (PropertyInfo property in type.GetProperties(all))
        {
            yield return property.PropertyType;
        }

        foreach (MethodInfo method in type.GetMethods(all).Where(m => m.DeclaringType == type))
        {
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
