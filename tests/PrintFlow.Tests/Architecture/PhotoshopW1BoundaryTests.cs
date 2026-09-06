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
        properties.ShouldContain(nameof(PhotoshopW1PreparedDocument.Provenance));
    }

    /// <summary>
    /// A prepared document names where its white ink came from, and does not assert an origin it
    /// cannot know.
    /// </summary>
    /// <remarks>
    /// SCRUM-11101 allows an operator to keep white ink that arrived in the customer's own file.
    /// A prepared document must therefore be able to represent that case truthfully, which it
    /// cannot do while it carries an unconditional action name, branch and invocation flag. The
    /// origin is a closed choice instead: exactly two cases, both declared inside
    /// <see cref="PhotoshopWhiteInkProvenance"/>, and no way to add a third from outside it.
    /// </remarks>
    [Fact]
    public void W1_result_names_its_provenance_and_no_longer_assumes_the_Action_produced_it()
    {
        string[] properties = [.. typeof(PhotoshopW1PreparedDocument).GetProperties().Select(p => p.Name)];
        properties.ShouldNotContain("ActionInvocationOccurredExactlyOnce");
        properties.ShouldNotContain("ActionName");
        properties.ShouldNotContain("ActionSetName");
        properties.ShouldNotContain("Branch");

        Type provenance = typeof(PhotoshopWhiteInkProvenance);
        provenance.IsAbstract.ShouldBeTrue();
        // Every constructor a derived record could chain to is private. The one exception is the
        // compiler-generated copy constructor, which takes the record type itself and cannot be
        // used as a base initialiser, so it does not open the hierarchy.
        provenance.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(constructor =>
            {
                ParameterInfo[] parameters = constructor.GetParameters();
                return parameters.Length != 1 || parameters[0].ParameterType != provenance;
            })
            .ShouldAllBe(constructor => constructor.IsPrivate,
                "a non-private constructor would let a case be declared outside this hierarchy");

        Type[] cases = [.. provenance.Assembly.GetTypes()
            .Where(type => type.BaseType == provenance)
            .OrderBy(type => type.Name, StringComparer.Ordinal)];
        cases.Select(type => type.Name).ShouldBe(["Generated", "Retained"]);

        // The Action-specific facts still exist — they are simply confined to the case that can
        // truthfully claim them.
        typeof(PhotoshopWhiteInkProvenance.Generated).GetProperties().Select(p => p.Name)
            .ShouldContain(nameof(PhotoshopWhiteInkProvenance.Generated.ActionName));
        typeof(PhotoshopWhiteInkProvenance.Retained).GetProperties().Select(p => p.Name)
            .ShouldNotContain(name => name.Contains("Action", StringComparison.Ordinal));
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
