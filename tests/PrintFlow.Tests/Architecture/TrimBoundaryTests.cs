using System.Reflection;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Definitions;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

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

    /// <summary>
    /// The trim domain is exactly these types, and adding one is a deliberate act.
    /// </summary>
    /// <remarks>
    /// <c>TrimGeometry</c> joined them in SCRUM-11081: the pair of rectangles a produced trim
    /// established, which the processor had always computed and the orchestrator had always
    /// discarded. It belongs here rather than in the workflow layer for the reason every other
    /// name on this list does — it is geometry, it touches no file and no repository, and the
    /// next assertion in this file is what holds it to that.
    /// </remarks>
    [Fact]
    public void The_trim_domain_types_exist_where_they_are_supposed_to()
    {
        DomainTrimTypes.Select(t => t.Name).ShouldBe(
            ["TrimMode", "TrimOutcome", "TrimMargin", "TrimBounds", "TrimGeometry", "AlphaBounds", "ManualCropGeometry", "ManualCropMargin"],
            ignoreOrder: true);
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
    // Manual crop: the same boundaries, asserted separately (Epic 11200 Part C2 §36)
    // -----------------------------------------------------------------------------

    /// <summary>
    /// The manual-crop port is declared in the workflow layer and implemented only in
    /// Infrastructure (§36).
    /// </summary>
    /// <remarks>
    /// The WIC call has exactly one home. A view model, or the workflow layer itself, growing an
    /// implementation would mean pixel work had escaped the layer that is allowed to know what
    /// a decoder is.
    /// </remarks>
    [Fact]
    public void The_workflow_layer_declares_the_manual_crop_port_and_implements_it_only_in_infrastructure()
    {
        typeof(IManualCropProcessor).Assembly.ShouldBe(WorkflowLayer);

        CropImplementations(Domain).ShouldBeEmpty();
        CropImplementations(WorkflowLayer).ShouldBeEmpty();
        CropImplementations(Shell).ShouldBeEmpty();

        CropImplementations(Infrastructure).ShouldBe(
            ["PrintFlow.Infrastructure.Imaging.WicManualCropProcessor"]);
    }

    /// <summary>
    /// The manual-crop seam names no path (§36).
    /// </summary>
    /// <remarks>
    /// The same statement <c>PreviewBoundaryTests</c> makes about previews, for the seam that
    /// <i>writes</i>. A crop taking a path would be an arbitrary-file-write API by another name,
    /// and no amount of validation elsewhere would make it safe.
    /// </remarks>
    [Fact]
    public void The_manual_crop_seam_accepts_no_string_parameter()
    {
        IEnumerable<string> offenders = typeof(IManualCropProcessor).GetMethods()
            .SelectMany(method => method.GetParameters()
                .Where(parameter => parameter.ParameterType == typeof(string))
                .Select(parameter => $"{method.Name}({parameter.Name})"));

        offenders.ShouldBeEmpty();

        // Both files it names are workspace references, resolved by the workspace and nothing else.
        typeof(ManualCropRequest).GetProperties()
            .Where(p => p.Name is "Input" or "ExpectedOutput")
            .Select(p => p.PropertyType)
            .ShouldAllBe(type => type == typeof(WorkspaceFileRef));
    }

    /// <summary>
    /// The manual crop can reach no external application either (§36).
    /// </summary>
    /// <remarks>
    /// Structural, for the same reason the trim version is: "manual crop is not Photoshop" is a
    /// claim about what the code can do, and an absent type reference is the only form of that
    /// claim which cannot be talked around later.
    /// </remarks>
    [Fact]
    public void The_manual_crop_processor_can_reach_no_external_application_adapter()
    {
        Type processor = Infrastructure.GetType("PrintFlow.Infrastructure.Imaging.WicManualCropProcessor")!;
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

    /// <summary>
    /// Manual-crop eligibility is decided in the workflow layer and nowhere else (§3, §36).
    /// </summary>
    /// <remarks>
    /// The rule is a single public method, and both callers that matter are inside the workflow
    /// layer: the read model that reports whether to offer the control, and the service that
    /// refuses the command. The shell may read the answer but has no way to compute one — it
    /// cannot see attempt history at all, because <c>SessionView</c> does not carry any.
    /// </remarks>
    [Fact]
    public void Manual_crop_eligibility_is_decided_in_the_workflow_layer()
    {
        Type rule = WorkflowLayer.GetType("PrintFlow.Workflow.Services.ManualCropEligibility")!;
        rule.IsAbstract.ShouldBeTrue("the rule is a static class, so nothing can hold an instance of it");
        rule.IsSealed.ShouldBeTrue();

        // The shell is given the verdict, not the inputs: no view model names the rule, and the
        // read model it reads carries no attempts to re-derive it from.
        Shell.GetTypes()
            .Where(t => SignatureTypes(t).Contains(rule))
            .Select(t => t.FullName!)
            .ShouldBeEmpty();

        typeof(SessionView).GetProperties()
            .Select(p => p.PropertyType)
            .ShouldNotContain(typeof(IReadOnlyList<ProcessingAttempt>));

        typeof(SessionView).GetProperty(nameof(SessionView.CanManualCrop))!
            .PropertyType.ShouldBe(typeof(bool));
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
    /// The UI drives Trim and manual crop through <c>ISessionService</c>, exactly as it drives
    /// every other step.
    /// </summary>
    /// <remarks>
    /// A view model holding an <see cref="ITrimProcessor"/> or an
    /// <see cref="IManualCropProcessor"/> would mean the screen had acquired its own route to
    /// pixel work, bypassing the attempt, hash and review machinery entirely. The request and
    /// result types are listed too, because holding one is the same thing one step removed.
    /// <para>
    /// <b>What changed in Part C2, and why.</b> <see cref="TrimBounds"/> used to be on this list.
    /// It no longer is: a manual crop <i>is</i> an operator-supplied rectangle, so the screen
    /// that offers the tool has to be able to name one, and
    /// <c>WorkflowCommand.SubmitManualCrop</c> carries it. Reusing the existing half-open
    /// rectangle rather than inventing a parallel UI type is deliberate (Part C2 §6) — a second
    /// rectangle type would need a conversion at the seam, and a conversion is where an
    /// off-by-one clips a column of artwork. Nothing is weakened by the removal: a rectangle is
    /// pure geometry with no route to a file, and the test below states the property that
    /// actually matters, which is that the only thing the shell can do with one is put it in a
    /// command.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_view_model_holds_a_trim_or_manual_crop_processor()
    {
        Type[] forbidden =
        [
            typeof(ITrimProcessor), typeof(TrimRequest), typeof(TrimResult),
            typeof(IManualCropProcessor), typeof(ManualCropRequest), typeof(ManualCropResult),
        ];

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

    /// <summary>
    /// A crop rectangle leaves the shell only inside a <c>WorkflowCommand</c> (Part C2 §12).
    /// </summary>
    /// <remarks>
    /// The replacement for the blanket ban above, and a sharper statement than it was. It does
    /// not ask whether the shell mentions a rectangle — it must, to let an operator draw one —
    /// but whether the shell has any way to <i>use</i> one except by handing it to
    /// <c>ISessionService.ExecuteAsync</c>. Two halves:
    /// <list type="bullet">
    ///   <item>exactly one command carries a <see cref="TrimBounds"/>, so there is one command
    ///         shape a rectangle can travel in;</item>
    ///   <item>no type declared in the shell names the manual-crop port or its request/result
    ///         records, so there is no second route past the command at all.</item>
    /// </list>
    /// The port records themselves do carry a rectangle, and must: they are the seam
    /// <see cref="IManualCropProcessor"/> is defined in terms of. What matters is that the
    /// only code able to construct one is on the far side of the service, together with the
    /// eligibility guard, the attempt row, the inspection, the hash and the review.
    /// </remarks>
    [Fact]
    public void A_crop_rectangle_reaches_the_workflow_layer_only_as_a_command()
    {
        IEnumerable<string> commandsCarryingBounds = WorkflowLayer.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(WorkflowCommand)))
            .Where(t => t.GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Any(p => p.ParameterType == typeof(TrimBounds)))
            .Select(t => t.FullName!);

        commandsCarryingBounds.ShouldBe([typeof(WorkflowCommand.SubmitManualCrop).FullName!]);

        Type[] cropSeam = [typeof(IManualCropProcessor), typeof(ManualCropRequest), typeof(ManualCropResult)];

        IEnumerable<string> shellTypesNamingTheSeam = Shell.GetTypes()
            .Where(t => SignatureTypes(t).Intersect(cropSeam).Any())
            .Select(t => t.FullName!);

        shellTypesNamingTheSeam.ShouldBeEmpty(
            "the shell submits a rectangle through ISessionService and never performs the crop itself.");
    }

    // -----------------------------------------------------------------------------
    // Trim parameters and return targets (Epic 11200 Part C3 §27)
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A trim margin reaches the workflow layer only inside a <c>WorkflowCommand</c> (§13, §27).
    /// </summary>
    /// <remarks>
    /// The same statement <c>A_crop_rectangle_reaches_the_workflow_layer_only_as_a_command</c>
    /// makes about a rectangle, for the other value an operator now supplies. The shell must be
    /// able to name a <see cref="TrimMargin"/> — the operator types one — so a blanket ban would
    /// be the wrong shape. What matters is that there is exactly one command a margin can travel
    /// in, and no second route past it: the shell names no trim seam type at all, which
    /// <c>No_view_model_holds_a_trim_or_manual_crop_processor</c> above already asserts.
    /// </remarks>
    [Fact]
    public void A_trim_margin_reaches_the_workflow_layer_only_as_a_command()
    {
        IEnumerable<string> commandsCarryingMargin = WorkflowLayer.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(WorkflowCommand)))
            .Where(t => t.GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Any(p => p.ParameterType == typeof(TrimMargin)))
            .Select(t => t.FullName!);

        commandsCarryingMargin.ShouldBe([typeof(WorkflowCommand.SetTrimParameters).FullName!]);
    }

    /// <summary>
    /// An attempt's recorded trim parameters cannot be reassigned (§14, §15, §27).
    /// </summary>
    /// <remarks>
    /// The audit property made structural. <c>TrimParameters</c> is <c>init</c>-only, so the
    /// only way to change what an attempt says it ran with is to build a different attempt —
    /// which is what a retry does, under a new identity. An ordinary setter would make
    /// "the first attempt's settings are never overwritten" a matter of nobody having written
    /// the assignment yet.
    /// </remarks>
    [Fact]
    public void An_attempts_recorded_trim_parameters_are_init_only()
    {
        PropertyInfo property = typeof(ProcessingAttempt)
            .GetProperty(nameof(ProcessingAttempt.TrimParameters))!;

        property.PropertyType.ShouldBe(typeof(TrimMargin?));

        MethodInfo setter = property.SetMethod!;
        setter.ReturnParameter.GetRequiredCustomModifiers()
            .ShouldContain(typeof(System.Runtime.CompilerServices.IsExternalInit),
                "TrimParameters must be init-only, so a recorded attempt cannot be re-parameterised.");
    }

    /// <summary>
    /// Return destinations come from the workflow layer and cannot be invented by the shell
    /// (§4, §27).
    /// </summary>
    /// <remarks>
    /// Two halves. The engine owns the question — <c>AvailableReturnTargets</c> is on the
    /// interface, so the shell asks rather than derives — and the row the selector binds to can
    /// only be built from a <see cref="ReturnTargetView"/> the workflow layer produced. A row
    /// constructible from a bare <c>StepKind</c> would be a destination the screen made up, and
    /// the whole point of §4 is that it cannot.
    /// </remarks>
    [Fact]
    public void Return_targets_come_from_the_workflow_layer_and_cannot_be_invented_by_the_shell()
    {
        typeof(IWorkflowEngine)
            .GetMethod(nameof(IWorkflowEngine.AvailableReturnTargets))
            .ShouldNotBeNull("the engine, not a view model, decides where returning is legal.");

        typeof(SessionView).GetProperty(nameof(SessionView.ReturnTargets))!
            .PropertyType.ShouldBe(typeof(IReadOnlyList<ReturnTargetView>));

        Type row = Shell.GetType("PrintFlow.App.ViewModels.ReturnTargetRow")!;
        ConstructorInfo[] constructors = row.GetConstructors(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        constructors.Length.ShouldBe(1);
        constructors[0].GetParameters().Select(p => p.ParameterType)
            .ShouldBe([typeof(ReturnTargetView)]);
        constructors[0].IsPublic.ShouldBeFalse(
            "nothing outside the shell may construct a return destination.");
    }

    /// <summary>
    /// The read model carries no attempt history for the shell to re-derive parameters from
    /// (§27).
    /// </summary>
    /// <remarks>
    /// The same guarantee <c>Manual_crop_eligibility_is_decided_in_the_workflow_layer</c> makes
    /// about eligibility, extended to the two answers this slice added. Both are computed where
    /// the attempt rows live and handed over as values, so a screen cannot arrive at a second
    /// opinion about which margin produced the file it is displaying.
    /// </remarks>
    [Fact]
    public void The_read_model_reports_trim_parameters_rather_than_the_history_behind_them()
    {
        typeof(SessionView).GetProperty(nameof(SessionView.CurrentTrimParameters))!
            .PropertyType.ShouldBe(typeof(TrimMargin?));

        typeof(SessionView).GetProperty(nameof(SessionView.CanSetTrimParameters))!
            .PropertyType.ShouldBe(typeof(bool));

        typeof(SessionView).GetProperties()
            .Select(p => p.PropertyType)
            .ShouldNotContain(typeof(IReadOnlyList<ProcessingAttempt>));
    }

    private static string[] CropImplementations(Assembly assembly) =>
        [.. assembly.GetTypes()
            .Where(t => t is { IsInterface: false, IsAbstract: false } && typeof(IManualCropProcessor).IsAssignableFrom(t))
            .Select(t => t.FullName!)
            .Order(StringComparer.Ordinal)];

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
