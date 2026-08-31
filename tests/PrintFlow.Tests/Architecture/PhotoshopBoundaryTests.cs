using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Infrastructure.Automation;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Architecture;

/// <summary>
/// Where the Photoshop automation is allowed to live, and what it is structurally unable to do
/// (Epic 11400 Part A §14, §15, §18, §23).
/// </summary>
/// <remarks>
/// The rules below are blunt on purpose. Several of them assert the <i>absence</i> of something,
/// which is the only way to state a scope boundary as a checkable fact: "Part A runs no Action
/// and writes no TIFF" is a promise until there is a test that fails when a method appears that
/// could.
/// </remarks>
public sealed class PhotoshopBoundaryTests
{
    private static Assembly Domain => typeof(FailureCode).Assembly;

    private static Assembly WorkflowLayer => typeof(WorkflowEngine).Assembly;

    private static Assembly Infrastructure => typeof(GuardedPhotoshopUiDriver).Assembly;

    private static Assembly Shell => typeof(global::PrintFlow.App.App).Assembly;

    /// <summary>The Photoshop automation types that must never be nameable outside Infrastructure.</summary>
    private static readonly string[] InfrastructureOnlyTypeNames =
    [
        nameof(IPhotoshopUiDriver),
        nameof(GuardedPhotoshopUiDriver),
        nameof(IPhotoshopAutomationFoundation),
        nameof(PhotoshopTarget),
        nameof(PhotoshopStartingState),
        nameof(PhotoshopBaseline),
        nameof(PhotoshopOpenDialogSignature),
        nameof(PhotoshopDocumentIdentitySignature),
        nameof(PhotoshopDocumentIdentity),
        nameof(IPhotoshopTiffAutomation),
        nameof(PhotoshopValidatedTiffCandidate),
        nameof(IVerifiedControlSink),
        nameof(VerifiedControlRef),
    ];

    // -----------------------------------------------------------------------------------
    // Containment
    // -----------------------------------------------------------------------------------

    [Fact]
    public void The_Photoshop_automation_types_are_declared_only_in_Infrastructure()
    {
        foreach (string typeName in InfrastructureOnlyTypeNames)
        {
            Infrastructure.GetTypes().ShouldContain(
                t => t.Name == typeName, $"{typeName} should be declared in Infrastructure.");

            foreach (Assembly other in new[] { Domain, WorkflowLayer, Shell })
            {
                other.GetTypes().Where(t => t.Name == typeName).Select(t => t.FullName!)
                    .ShouldBeEmpty($"{typeName} must not be declared in {other.GetName().Name}.");
            }
        }
    }

    /// <summary>
    /// Nothing above Infrastructure can name the raw Photoshop driver.
    /// </summary>
    /// <remarks>
    /// Source text rather than reflection, because the interesting failure is a
    /// <c>using PrintFlow.Infrastructure.Adapters.Photoshop;</c> that someone adds while wiring
    /// something up — which reflection over the compiled assembly would only catch once the type
    /// reached a signature.
    /// </remarks>
    [Theory]
    [InlineData("PrintFlow.Domain", null)]
    [InlineData("PrintFlow.Workflow", null)]
    [InlineData("PrintFlow.App", "ViewModels")]
    [InlineData("PrintFlow.App", "Views")]
    [InlineData("PrintFlow.App", "Navigation")]
    public void No_Photoshop_automation_type_is_named_outside_Infrastructure(
        string project, string? subdirectory)
    {
        List<string> offenders = [];

        foreach ((string file, string[] lines) in SourceOf(project, subdirectory))
        {
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (string typeName in InfrastructureOnlyTypeNames)
                {
                    if (Regex.IsMatch(
                            lines[i], $@"\b{Regex.Escape(typeName)}\b", RegexOptions.None,
                            TimeSpan.FromSeconds(5)))
                    {
                        offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                    }
                }
            }
        }

        offenders.ShouldBeEmpty(
            "Photoshop automation belongs to PrintFlow.Infrastructure; nothing above it may name one.");
    }

    /// <summary>View models perform no file-system work of their own.</summary>
    [Fact]
    public void View_models_contain_no_System_IO()
    {
        List<string> offenders = [];

        foreach ((string file, string[] lines) in SourceOf("PrintFlow.App", "ViewModels"))
        {
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                if (Regex.IsMatch(
                        lines[i], @"\bSystem\.IO\b|\bFile\.|\bDirectory\.|\bPath\.", RegexOptions.None,
                        TimeSpan.FromSeconds(5)))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {trimmed}");
                }
            }
        }

        offenders.ShouldBeEmpty("view models describe state; the workspace owns every path.");
    }

    // -----------------------------------------------------------------------------------
    // Scope: no Action, no TIFF, no Revision
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The Photoshop driver seam offers no way to run an Action, resize, or save.
    /// </summary>
    /// <remarks>
    /// A capability that cannot be named cannot be invoked by accident in the next slice's
    /// hurry. When W1 arrives it will arrive as a reviewed addition to this list, which is the
    /// point of asserting the list exactly (§14, §15).
    /// </remarks>
    [Fact]
    public void The_Photoshop_driver_exposes_no_action_resize_or_save_capability()
    {
        string[] methods = [.. typeof(IPhotoshopUiDriver).GetMethods().Select(m => m.Name)];

        methods.ShouldBe(
        [
            nameof(IPhotoshopUiDriver.InspectStateAsync),
            nameof(IPhotoshopUiDriver.ActivateAsync),
            nameof(IPhotoshopUiDriver.OpenManagedDocumentAsync),
            nameof(IPhotoshopUiDriver.ProbeDocumentIdentityAsync),
            nameof(IPhotoshopUiDriver.CloseExactDocumentAsync),
            nameof(IPhotoshopUiDriver.CaptureEvidence),
        ], ignoreOrder: true);

        foreach (string banned in new[]
                 {
                     "Action", "Play", "Run", "Resize", "Canvas", "Cmyk", "Convert",
                     "Save", "Export", "Tiff", "Revision", "W1",
                 })
        {
            methods.ShouldNotContain(
                name => name.Contains(banned, StringComparison.OrdinalIgnoreCase),
                $"Part A must expose no '{banned}' capability on the Photoshop driver.");
        }
    }

    /// <summary>The foundation seam offers exactly three operations and no output surface.</summary>
    [Fact]
    public void The_Photoshop_foundation_exposes_no_output_producing_operation()
    {
        string[] methods = [.. typeof(IPhotoshopAutomationFoundation).GetMethods().Select(m => m.Name)];

        methods.ShouldBe(
        [
            nameof(IPhotoshopAutomationFoundation.EnsureReadyAsync),
            nameof(IPhotoshopAutomationFoundation.OpenManagedWorkingFileAsync),
            nameof(IPhotoshopAutomationFoundation.CloseExactDocumentAsync),
        ], ignoreOrder: true);
    }

    /// <summary>
    /// The Part A success record cannot be read as an output, a revision, or a completed step.
    /// </summary>
    /// <remarks>
    /// Asserted on the type rather than left to review, because the temptation in Part B will be
    /// to add a produced-file path here rather than to a new type — and the moment this record
    /// carries one, a caller can read a success from it as "the TIFF exists" (§18).
    /// </remarks>
    [Fact]
    public void The_opened_document_record_carries_no_output_or_revision_surface()
    {
        string[] members = [.. typeof(PhotoshopOpenedDocument).GetProperties().Select(p => p.Name)];

        foreach (string banned in new[]
                 {
                     "Output", "Path", "Revision", "Succeed", "Export", "Tiff", "Produced", "File",
                 })
        {
            members.ShouldNotContain(
                name => name.Contains(banned, StringComparison.OrdinalIgnoreCase),
                $"a Part A success must not carry a '{banned}' member.");
        }
    }

    /// <summary>The one reviewed TIFF Save As exists only in C1's fixed native bridge.</summary>
    [Fact]
    public void The_only_Photoshop_TIFF_save_call_is_the_closed_C1_program()
    {
        string directory = Path.Combine(
            ProjectDirectory("PrintFlow.Infrastructure"), "Adapters", "Photoshop");
        List<string> saveCallFiles = Directory.EnumerateFiles(directory, "*.cs")
            .Where(file => File.ReadAllText(file).Contains(".saveAs(", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList()!;

        saveCallFiles.ShouldBe(["PhotoshopTiffNativeBridge.cs"]);
        string native = File.ReadAllText(Path.Combine(directory, saveCallFiles[0]));
        Regex.Matches(native, Regex.Escape("doc.saveAs("), RegexOptions.None,
            TimeSpan.FromSeconds(5)).Count.ShouldBe(1);
        native.ShouldContain("doc.saveAs(output, options, true, Extension.LOWERCASE);");
    }

    // -----------------------------------------------------------------------------------
    // Input safety
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The Photoshop adapter uses no coordinate, no mouse, and no process termination.
    /// </summary>
    [Theory]
    [InlineData("SendKeys")]
    [InlineData("keybd_event")]
    [InlineData("mouse_event")]
    [InlineData("SetCursorPos")]
    [InlineData("Cursor")]
    [InlineData("TerminateProcess")]
    [InlineData(".Kill(")]
    [InlineData("CloseMainWindow")]
    [InlineData("taskkill")]
    public void The_Photoshop_adapter_source_contains_no_banned_input_or_termination_API(
        string bannedToken)
    {
        List<string> offenders = [];

        foreach ((string file, string[] lines) in PhotoshopAdapterSource())
        {
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                    trimmed.StartsWith("///", StringComparison.Ordinal))
                {
                    continue;
                }

                if (lines[i].Contains(bannedToken, StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {trimmed}");
                }
            }
        }

        offenders.ShouldBeEmpty($"'{bannedToken}' has no correct use in the Photoshop adapter.");
    }

    /// <summary>
    /// The control seam declares no coordinate-bearing member anywhere in its surface.
    /// </summary>
    /// <remarks>
    /// <c>IUiElementProvider</c> reports element rectangles as structural evidence, which is a
    /// justified exception there. This seam has no such need and must never acquire one: a
    /// control here is addressed by handle, and a rectangle would be the first thing a future
    /// click primitive would reach for.
    /// </remarks>
    [Fact]
    public void The_verified_control_seam_exposes_no_coordinate()
    {
        IEnumerable<string> names = typeof(IVerifiedControlSink).GetMethods()
            .SelectMany(m => m.GetParameters().Select(p => p.Name!).Append(m.Name))
            .Concat(typeof(VerifiedControlRef).GetProperties().Select(p => p.Name));

        foreach (string name in names)
        {
            foreach (string banned in new[] { "x", "y", "point", "coordinate", "bounds", "rect" })
            {
                string.Equals(name, banned, StringComparison.OrdinalIgnoreCase)
                    .ShouldBeFalse($"'{name}' names a coordinate on a seam that must not have one.");
            }
        }
    }

    /// <summary>
    /// The keystroke vocabulary stays a small, reviewed, closed set.
    /// </summary>
    /// <remarks>
    /// Asserted exactly rather than by exclusion, because the hazard is addition rather than
    /// misuse: every value here is a capability that PrintFlow can direct at a live desktop, and
    /// a new one should have to break a test before it can be used.
    /// </remarks>
    [Fact]
    public void The_known_shortcut_set_is_exactly_the_reviewed_one()
    {
        Enum.GetNames<KnownShortcut>().ShouldBe(
        [
            nameof(KnownShortcut.OpenFile),
            nameof(KnownShortcut.Escape),
            nameof(KnownShortcut.SaveAsProbe),
            nameof(KnownShortcut.CloseActiveDocument),
        ], ignoreOrder: true);
    }

    /// <summary>
    /// The Photoshop adapter constructs workflow output in exactly one place, and that place
    /// cannot be reached without a validated TIFF candidate (Epic 11400 Part C2A §8).
    /// </summary>
    /// <remarks>
    /// Part A and C1 asserted that <c>new AdapterOutput</c> appeared nowhere in this adapter,
    /// because the seam was fail-closed and any construction at all would have been a fabricated
    /// success. C2A opens the seam, so the property that replaces it is not "never" but "once,
    /// and only from a candidate": the hazard is now a second construction site — a shortcut
    /// path built from a save return or a file path — appearing later without anyone noticing.
    /// <para>
    /// Asserted at source level for the same reason the old one was: the boundary is exactly the
    /// kind of thing that gets broken quietly by a helpful edit, and a compile error is a much
    /// better outcome than a review that has to spot it.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_production_Photoshop_workflow_seam_constructs_AdapterOutput_only_from_a_validated_candidate()
    {
        string directory = Path.Combine(
            ProjectDirectory("PrintFlow.Infrastructure"), "Adapters", "Photoshop");

        List<string> constructingFiles = [.. Directory
            .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("new AdapterOutput", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Select(name => name!)];

        constructingFiles.ShouldBe(["PhotoshopAdapterOutputFactory.cs"]);

        // The processor composes the stages; it does not build the result itself.
        File.ReadAllText(Path.Combine(directory, "ProductionPhotoshopOutputProcessor.cs"))
            .ShouldNotContain("new AdapterOutput", Case.Sensitive);

        // And every way through that construction site takes the validated candidate, not a
        // path, a save return or an in-memory W1 result. There is no overload — success or
        // refusal — that could be reached with anything weaker.
        MethodInfo[] creators = [.. typeof(PhotoshopAdapterOutputFactory)
            .GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(method => method.ReturnType == typeof(OperationResult<AdapterOutput>))];

        creators.ShouldNotBeEmpty();
        creators.ShouldAllBe(method => method.GetParameters()
            .Any(parameter => parameter.ParameterType == typeof(PhotoshopValidatedTiffCandidate)));
    }

    // -----------------------------------------------------------------------------------
    // Source helpers
    // -----------------------------------------------------------------------------------

    private static IEnumerable<(string File, string[] Lines)> PhotoshopAdapterSource()
    {
        string directory = Path.Combine(
            ProjectDirectory("PrintFlow.Infrastructure"), "Adapters", "Photoshop");

        foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            yield return (file, File.ReadAllLines(file));
        }
    }

    private static IEnumerable<(string File, string[] Lines)> SourceOf(
        string project, string? subdirectory)
    {
        string directory = ProjectDirectory(project);
        if (subdirectory is not null)
        {
            directory = Path.Combine(directory, subdirectory);
        }

        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (string file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
            {
                continue;
            }

            yield return (file, File.ReadAllLines(file));
        }
    }

    private static string ProjectDirectory(string project)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln")))
        {
            current = current.Parent;
        }

        current.ShouldNotBeNull("the repository root should be reachable from the test output directory.");
        return Path.Combine(current.FullName, "src", project);
    }
}
