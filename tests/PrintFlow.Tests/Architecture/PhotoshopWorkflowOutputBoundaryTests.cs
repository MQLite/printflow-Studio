using System.IO;
using System.Reflection;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Gate;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Architecture;

/// <summary>
/// Where C2A's new capability stops (Epic 11400 Part C2A §10, §27, §28, §30).
/// </summary>
/// <remarks>
/// C2A is the slice in which the Photoshop adapter first becomes able to succeed, and every rule
/// here exists because of that. An adapter that can return an <c>AdapterOutput</c> is one step
/// from an adapter that writes the Revision itself, one step from one that promotes the file into
/// <c>Approved\</c>, and one step from one that decides its own review — each of which would look
/// locally reasonable and would take Workflow's authority away.
/// </remarks>
public sealed class PhotoshopWorkflowOutputBoundaryTests
{
    // -----------------------------------------------------------------------------------
    // §10 — SessionService remains the sole Revision authority
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Revisions, PrintOutputs and review decisions are constructed in exactly one place, and it
    /// is not Infrastructure (§10, §30).
    /// </summary>
    /// <remarks>
    /// The SQLite mapper is excluded, and only it: rehydrating a stored row is reading a Revision
    /// somebody else already decided to create, which is the opposite of the authority under
    /// test. Everything else in Infrastructure — the adapters above all — must be unable to bring
    /// one into existence.
    /// </remarks>
    [Fact]
    public void Only_SessionService_creates_a_Revision_a_PrintOutput_or_a_review_decision()
    {
        string[] creation =
            ["Revision.Create(", "new Revision(", "PrintOutput.Create(", "new ReviewDecision("];

        List<string> sites = [];
        foreach (string project in new[] { "PrintFlow.Infrastructure", "PrintFlow.Workflow", "PrintFlow.App" })
        {
            foreach (string file in SourceFiles(project))
            {
                string source = File.ReadAllText(file);
                if (creation.Any(token => source.Contains(token, StringComparison.Ordinal)))
                {
                    sites.Add(Path.GetFileName(file));
                }
            }
        }

        sites.Order().ShouldBe(["Mappers.cs", "SessionService.cs"]);
    }

    /// <summary>
    /// The Photoshop adapter uses none of the operations that would let it finish the job itself
    /// (§10, §28).
    /// </summary>
    /// <remarks>
    /// Each banned token is a call or a construction rather than a bare type name, because the
    /// adapter's own commentary talks about Revisions and the Approved area at length — it has
    /// to, since explaining what it must not do is most of what those comments are for. Banning
    /// the words would make the honest documentation fail the test and invite someone to delete
    /// the explanation rather than the capability.
    /// </remarks>
    [Fact]
    public void The_Photoshop_adapter_invokes_no_revision_review_or_promotion_operation()
    {
        string source = AdapterSource();

        foreach (string banned in new[]
        {
            "Revision.Create(",
            "new Revision(",
            "PrintOutput.Create(",
            "new PrintOutput(",
            "new ReviewDecision(",
            "WorkspaceArea.Approved",
            "WorkspaceArea.Rejected",
            "MoveToRejectedAsync(",
            "ReserveOutput(",
            "WriteReservedAsync(",
            "CleanupWorking(",
        })
        {
            source.ShouldNotContain(banned, Case.Sensitive);
        }
    }

    // -----------------------------------------------------------------------------------
    // §6 — the adapter consumes a resolved request and decides none of it
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The workflow seam takes the typed immutable request and nothing else: no path, no
    /// filename, no preset id, no geometry (§6, §30).
    /// </summary>
    [Fact]
    public void The_workflow_seam_consumes_only_the_typed_request()
    {
        ParameterInfo[] parameters = typeof(IPhotoshopOutputProcessor)
            .GetMethod(nameof(IPhotoshopOutputProcessor.GenerateAsync))!
            .GetParameters();

        parameters.Length.ShouldBe(2);
        parameters[0].ParameterType.ShouldBe(typeof(PhotoshopRequest));
        parameters[1].ParameterType.ShouldBe(typeof(CancellationToken));
    }

    /// <summary>
    /// The adapter resolves no preset, re-decides no branch and recalculates no geometry: those
    /// arrive already settled on the request (§6).
    /// </summary>
    /// <remarks>
    /// Asserted against the composed processor's own source rather than the whole adapter
    /// directory, because this is specifically about the method that now has a request in its
    /// hand — the one place where reaching for a fresh decision would be easy and wrong.
    /// </remarks>
    [Fact]
    public void The_production_processor_resolves_no_preset_branch_or_geometry_of_its_own()
    {
        string source = File.ReadAllText(Path.Combine(
            ProjectDirectory("PrintFlow.Infrastructure"), "Adapters", "Photoshop",
            "ProductionPhotoshopOutputProcessor.cs"));

        foreach (string banned in new[]
        {
            "IWorkstationPresetProvider",
            "GetVerifiedPreset",
            "GetNamingPatterns",
            "OutputFileNaming",
            "NamingPatternRenderer",
            "ISessionRepository",
            "PrintPreparationPlan.For",
            "FitWithinBounds.Calculate",
        })
        {
            source.ShouldNotContain(banned, Case.Sensitive);
        }
    }

    // -----------------------------------------------------------------------------------
    // §30 — the TIFF stays inside Infrastructure
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Workflow and App parse no TIFF bytes of their own, and never see the C1 candidate as a
    /// filesystem object (§30).
    /// </summary>
    /// <remarks>
    /// The type-name check lives in <c>PhotoshopTiffBoundaryTests</c>; this is the byte-level
    /// half of the same rule. The hazard it guards is a review screen that decides to read the
    /// TIFF's IFD itself to show a channel count — a second parser, disagreeing with the
    /// accepted one, in the layer least able to be sure it is right.
    /// </remarks>
    [Fact]
    public void Workflow_and_App_parse_no_TIFF_bytes()
    {
        foreach (string project in new[] { "PrintFlow.Workflow", "PrintFlow.App" })
        {
            foreach (string file in SourceFiles(project))
            {
                string source = File.ReadAllText(file);
                foreach (string banned in new[]
                {
                    "BinaryPrimitives",
                    "PhotometricInterpretation",
                    "SamplesPerPixel",
                    "PlanarConfiguration",
                    "ImageFileDirectory",
                    "8BIM",
                })
                {
                    source.ShouldNotContain(banned, Case.Sensitive,
                        $"{Path.GetFileName(file)} reads TIFF structure outside Infrastructure.");
                }
            }
        }
    }

    // -----------------------------------------------------------------------------------
    // §27 — the gate is exactly what it was
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The foundation gate still refuses every Production adapter, and C2A neither weakened nor
    /// consulted it (§27).
    /// </summary>
    /// <remarks>
    /// Both halves matter. The behavioural half is the guarantee itself. The structural half is
    /// the one that would rot quietly: an adapter that asked the gate about itself would leave
    /// this behaviour intact while removing the reason to trust it, because a component that can
    /// consult its own permission is a component that can decide it has permission.
    /// </remarks>
    [Fact]
    public void The_foundation_gate_still_refuses_production_and_no_adapter_consults_it()
    {
        FoundationEnvironmentGate gate = new();

        OperationResult<PrintFlow.Domain.Results.Unit> production = gate.Verify(AdapterExecutionMode.Production);
        production.IsFailure.ShouldBeTrue();
        production.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
        gate.Verify(AdapterExecutionMode.Fake).IsSuccess.ShouldBeTrue();

        // No type in the Photoshop adapter takes, holds or returns the gate: it declares what it
        // is and lets SessionService apply the gate before calling.
        foreach (Type type in typeof(ProductionPhotoshopOutputProcessor).Assembly
            .GetTypes()
            .Where(type => type.Namespace == typeof(ProductionPhotoshopOutputProcessor).Namespace))
        {
            type.GetFields(BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic)
                .ShouldNotContain(field => field.FieldType == typeof(IEnvironmentGate));

            type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(constructor => constructor.GetParameters())
                .ShouldNotContain(parameter => parameter.ParameterType == typeof(IEnvironmentGate));
        }
    }

    /// <summary>
    /// The production Photoshop adapter still declares itself Production, so the gate keeps
    /// applying to it (§27).
    /// </summary>
    [Fact]
    public void The_production_processor_still_declares_itself_production()
    {
        typeof(IPhotoshopOutputProcessor)
            .IsAssignableFrom(typeof(ProductionPhotoshopOutputProcessor)).ShouldBeTrue();

        // Declared on the type rather than inferred from the adapter id, which is what lets the
        // gate recognise it without string-sniffing.
        PropertyInfo mode = typeof(ProductionPhotoshopOutputProcessor)
            .GetProperty(nameof(IPhotoshopOutputProcessor.Mode))!;
        mode.PropertyType.ShouldBe(typeof(AdapterExecutionMode));
    }

    // -----------------------------------------------------------------------------------
    // Source helpers
    // -----------------------------------------------------------------------------------

    private static string AdapterSource() => string.Join(
        "\n",
        Directory.EnumerateFiles(
                Path.Combine(ProjectDirectory("PrintFlow.Infrastructure"), "Adapters", "Photoshop"),
                "*.cs",
                SearchOption.AllDirectories)
            .Select(File.ReadAllText));

    private static IEnumerable<string> SourceFiles(string project) =>
        Directory.EnumerateFiles(ProjectDirectory(project), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string ProjectDirectory(string project)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }

        current.ShouldNotBeNull();
        return Path.Combine(current.FullName, "src", project);
    }
}
