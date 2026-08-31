using System.IO;
using System.Reflection;
using PrintFlow.Domain.Files;
using PrintFlow.Infrastructure.Adapters.Photoshop;

namespace PrintFlow.Tests.Architecture;

/// <summary>Focused C1 containment checks.</summary>
public sealed class PhotoshopTiffBoundaryTests
{
    [Fact]
    public void TIFF_surface_accepts_only_factual_B1B_state_and_one_managed_reference()
    {
        MethodInfo method = typeof(IPhotoshopTiffAutomation).GetMethod(
            nameof(IPhotoshopTiffAutomation.SaveProductionTiffAsync))!;
        ParameterInfo[] parameters = method.GetParameters();

        parameters.Count(parameter => parameter.ParameterType == typeof(WorkspaceFileRef)).ShouldBe(1);
        parameters.Count(parameter => parameter.ParameterType == typeof(PhotoshopW1PreparedDocument)).ShouldBe(1);
        parameters.ShouldNotContain(parameter => parameter.ParameterType == typeof(string));
        parameters.ShouldNotContain(parameter =>
            parameter.Name!.Contains("format", StringComparison.OrdinalIgnoreCase));
        parameters.ShouldNotContain(parameter =>
            parameter.Name!.Contains("compression", StringComparison.OrdinalIgnoreCase));
        parameters.ShouldNotContain(parameter =>
            parameter.Name!.Contains("option", StringComparison.OrdinalIgnoreCase));
        parameters.ShouldNotContain(parameter =>
            parameter.Name!.Contains("script", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TIFF_candidate_is_factual_Infrastructure_state_not_workflow_output()
    {
        Type candidate = typeof(PhotoshopValidatedTiffCandidate);
        candidate.Assembly.GetName().Name.ShouldBe("PrintFlow.Infrastructure");
        string[] properties = [.. candidate.GetProperties().Select(property => property.Name)];

        properties.ShouldContain(nameof(PhotoshopValidatedTiffCandidate.Tiff));
        properties.ShouldContain(nameof(PhotoshopValidatedTiffCandidate.Facts));
        properties.ShouldContain(nameof(PhotoshopValidatedTiffCandidate.BackingWorkingSha256));
        properties.ShouldNotContain("AdapterOutput");
        properties.ShouldNotContain("PrintOutput");
        properties.ShouldNotContain("Revision");
        properties.ShouldNotContain("ReviewRequired");
        properties.ShouldNotContain("AttemptSucceeded");
    }

    [Fact]
    public void Workflow_and_App_cannot_name_the_C1_TIFF_types()
    {
        foreach (string project in new[] { "PrintFlow.Workflow", "PrintFlow.App" })
        {
            string source = string.Join("\n", Directory.EnumerateFiles(
                    ProjectDirectory(project), "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal))
                .Select(File.ReadAllText));
            source.ShouldNotContain(nameof(IPhotoshopTiffAutomation), Case.Sensitive);
            source.ShouldNotContain(nameof(PhotoshopValidatedTiffCandidate), Case.Sensitive);
            source.ShouldNotContain(nameof(ProductionTiffFacts), Case.Sensitive);
            source.ShouldNotContain("TiffSaveOptions", Case.Sensitive);
        }
    }

    /// <summary>
    /// C2A lets the adapter return an <c>AdapterOutput</c> and nothing further
    /// (Epic 11400 Part C2A §10, §30).
    /// </summary>
    /// <remarks>
    /// The <c>new AdapterOutput</c> assertion this test used to carry moved to
    /// <c>PhotoshopBoundaryTests</c>, where it now says "exactly once, from a validated
    /// candidate" rather than "never" — C2A is the slice that opens that seam. Everything else
    /// here is unchanged and matters more than it did before: now that the adapter can succeed,
    /// the tempting next step is for it to finish the job, and a Revision or a review decision
    /// written from Infrastructure would take Workflow's sole authority away without any single
    /// change looking like it did.
    /// </remarks>
    [Fact]
    public void Workflow_processor_still_constructs_no_revision_output_or_review_success()
    {
        string source = File.ReadAllText(Path.Combine(
            ProjectDirectory("PrintFlow.Infrastructure"), "Adapters", "Photoshop",
            "ProductionPhotoshopOutputProcessor.cs"));

        source.ShouldNotContain("new PrintOutput", Case.Sensitive);
        source.ShouldNotContain("new Revision", Case.Sensitive);
        source.ShouldNotContain("new ReviewRequired", Case.Sensitive);
        source.ShouldNotContain("AttemptSucceeded", Case.Sensitive);
    }

    [Fact]
    public void TIFF_inspector_is_an_internal_operation_specific_seam()
    {
        typeof(IProductionTiffInspector).IsNotPublic.ShouldBeTrue();
        MethodInfo method = typeof(IProductionTiffInspector).GetMethod("Inspect")!;
        method.GetParameters().Length.ShouldBe(1);
        method.ReturnType.GenericTypeArguments.ShouldContain(typeof(ProductionTiffFacts));
    }

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
