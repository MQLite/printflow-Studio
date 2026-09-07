using System.IO;
using System.Reflection;
using PrintFlow.Domain.Outputs;
using PrintFlow.Infrastructure.Adapters.Photoshop;

namespace PrintFlow.Tests.Architecture;

/// <summary>Focused B1B capability and containment checks.</summary>
public sealed class PhotoshopW1BoundaryTests
{
    [Fact]
    public void Caller_facing_W1_surface_accepts_only_the_typed_branch_selector()
    {
        MethodInfo method = typeof(IPhotoshopW1Automation).GetMethod(
            nameof(IPhotoshopW1Automation.ExecuteW1Async))!;
        ParameterInfo[] parameters = method.GetParameters();

        parameters.Count(p => p.ParameterType == typeof(WhiteUnderbaseBranch)).ShouldBe(1);
        parameters.ShouldNotContain(p => p.ParameterType == typeof(string));
        parameters.ShouldNotContain(p => p.Name!.Contains("action", StringComparison.OrdinalIgnoreCase));
        parameters.ShouldNotContain(p => p.Name!.Contains("script", StringComparison.OrdinalIgnoreCase));
        parameters.ShouldNotContain(p => p.Name!.Contains("setName", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void W1_result_is_factual_infrastructure_state_not_output_or_revision()
    {
        Type type = typeof(PhotoshopW1PreparedDocument);
        type.Assembly.GetName().Name.ShouldBe("PrintFlow.Infrastructure");
        string[] properties = [.. type.GetProperties().Select(p => p.Name)];

        properties.ShouldNotContain("AdapterOutput");
        properties.ShouldNotContain("PrintOutput");
        properties.ShouldNotContain("Revision");
        properties.ShouldNotContain("OutputPath");
        properties.ShouldContain(nameof(PhotoshopW1PreparedDocument.BackingWorkingSha256));
        properties.ShouldContain(nameof(PhotoshopW1PreparedDocument.ActionInvocationOccurredExactlyOnce));
    }

    /// <summary>
    /// The production white-ink invariant, asserted structurally: for every production TIFF that
    /// carries white ink, the W1 origin is the PrintFlow validated production path — never a
    /// customer PSD and never a customer PDF.
    /// </summary>
    /// <remarks>
    /// PSD and PDF are visual design inputs. Source spot-colour and white-ink channels are not
    /// preserved as production channels, so there is no such thing as retained white ink, and the
    /// only document the TIFF saver accepts is one the signed Action produced here. The type
    /// system is what enforces this: <see cref="PhotoshopW1PreparedDocument"/> is the sole input
    /// to the saver, it is constructed in exactly one place, and it has no member capable of
    /// describing white ink from anywhere else.
    /// <para>
    /// SCRUM-11101-A modelled an open <c>Provenance</c> choice here — <c>Generated</c> or
    /// <c>Retained</c> — while the original SCRUM-11101 acceptance criterion still stood. The
    /// business contract that superseded SCRUM-11101 makes <c>Retained</c> business-invalid, so
    /// the capability was removed rather than left as unreachable future surface. This test is
    /// what stops it, or any equivalent, from returning by accident.
    /// </para>
    /// </remarks>
    [Fact]
    public void Production_W1_can_only_originate_from_the_validated_Action_path()
    {
        string[] properties = [.. typeof(PhotoshopW1PreparedDocument).GetProperties().Select(p => p.Name)];

        // The Action facts are unconditional members, not one case of a choice: a prepared
        // document cannot exist without naming the branch, the set, the action and the fact that
        // it was invoked exactly once.
        properties.ShouldContain(nameof(PhotoshopW1PreparedDocument.Branch));
        properties.ShouldContain(nameof(PhotoshopW1PreparedDocument.ActionSetName));
        properties.ShouldContain(nameof(PhotoshopW1PreparedDocument.ActionName));

        // No member can describe white ink that arrived from somewhere else.
        properties.ShouldNotContain("Provenance");
        properties.ShouldNotContain(name => name.Contains("Retain", StringComparison.OrdinalIgnoreCase));
        properties.ShouldNotContain(name => name.Contains("Carrier", StringComparison.OrdinalIgnoreCase));
        properties.ShouldNotContain(name => name.Contains("Existing", StringComparison.OrdinalIgnoreCase));

        // And no Retain vocabulary survives anywhere in the Infrastructure assembly's type names.
        typeof(PhotoshopW1PreparedDocument).Assembly.GetTypes()
            .Select(type => type.Name)
            .ShouldNotContain(name => name.Contains("WhiteInkProvenance", StringComparison.Ordinal) ||
                name.Contains("ExistingWhiteInk", StringComparison.Ordinal) ||
                name.Contains("RetainExisting", StringComparison.Ordinal));
    }

    /// <summary>
    /// No Product source anywhere offers the operator a Retain-versus-Regenerate choice, and no
    /// preparation or output rule treats a source spot/white-ink channel as authority.
    /// </summary>
    /// <remarks>
    /// The original SCRUM-11101 acceptance criterion required exactly that prompt. The business
    /// clarification that superseded it defines PSD and PDF as visual-only inputs, so the prompt
    /// must not exist — not merely be unwired. This reads the shipped source of all four Product
    /// projects rather than a chosen list of types, so a new decision surface in a view model, a
    /// workflow command or a resource string is caught wherever it is added.
    /// </remarks>
    [Fact]
    public void No_product_source_offers_a_retain_or_regenerate_white_ink_decision()
    {
        string[] projects = ["PrintFlow.Domain", "PrintFlow.Workflow", "PrintFlow.Infrastructure", "PrintFlow.App"];
        string[] forbidden =
        [
            "ExistingWhiteInkDecision", "RetainExistingWhiteInk", "RegenerateWhiteInk",
            "WhiteInkDecision", "RetainedWhiteInk", "IPdfSpotColourInspector",
        ];

        List<string> found = [];
        foreach (string project in projects)
        {
            foreach (string file in Directory.EnumerateFiles(
                ProjectDirectory(project), "*.*", SearchOption.AllDirectories))
            {
                if (Path.GetExtension(file) is not (".cs" or ".xaml" or ".resx")) continue;
                // bin/obj carry generated copies of the same sources and of stale builds.
                if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
                    file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)) continue;

                string text = File.ReadAllText(file);
                found.AddRange(forbidden
                    .Where(term => text.Contains(term, StringComparison.OrdinalIgnoreCase))
                    .Select(term => $"{term} in {Path.GetFileName(file)}"));
            }
        }

        found.ShouldBeEmpty(
            "PSD and PDF are visual-only inputs; PrintFlow does not ask the operator whether to " +
            "retain source white ink, so no Product surface may name that decision");
    }

    [Fact]
    public void W1_native_program_has_one_fixed_doAction_and_no_resize_save_or_generic_input_surface()
    {
        string directory = Path.Combine(
            ProjectDirectory("PrintFlow.Infrastructure"), "Adapters", "Photoshop");
        string native = File.ReadAllText(Path.Combine(directory, "PhotoshopW1NativeBridge.cs"));
        string seam = File.ReadAllText(Path.Combine(directory, "IPhotoshopW1Automation.cs"));

        Count(native, "app.doAction(").ShouldBe(1);
        native.ShouldNotContain("resizeImage(", Case.Insensitive);
        native.ShouldNotContain("changeMode(", Case.Insensitive);
        native.ShouldNotContain(".saveAs(", Case.Insensitive);
        native.ShouldNotContain("TiffSaveOptions", Case.Insensitive);
        seam.ShouldNotContain("ExecuteAction", Case.Sensitive);
        seam.ShouldNotContain("DoJavaScript", Case.Sensitive);
        seam.ShouldNotContain("ActionDescriptor", Case.Sensitive);
    }

    [Fact]
    public void Workflow_and_App_cannot_name_the_Infrastructure_W1_automation_types()
    {
        foreach (string project in new[] { "PrintFlow.Workflow", "PrintFlow.App" })
        {
            string source = string.Join("\n", Directory.EnumerateFiles(
                    ProjectDirectory(project), "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
                .Select(File.ReadAllText));
            source.ShouldNotContain(nameof(IPhotoshopW1Automation), Case.Sensitive);
            source.ShouldNotContain(nameof(PhotoshopW1PreparedDocument), Case.Sensitive);
            source.ShouldNotContain("DoJavaScript", Case.Sensitive);
        }
    }

    [Fact]
    public void View_models_still_contain_no_System_IO()
    {
        string source = string.Join("\n", Directory.EnumerateFiles(
                Path.Combine(ProjectDirectory("PrintFlow.App"), "ViewModels"), "*.cs",
                SearchOption.AllDirectories)
            .Select(File.ReadAllText));
        source.ShouldNotContain("System.IO", Case.Sensitive);
    }

    private static int Count(string source, string value) =>
        (source.Length - source.Replace(value, string.Empty, StringComparison.Ordinal).Length) / value.Length;

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
