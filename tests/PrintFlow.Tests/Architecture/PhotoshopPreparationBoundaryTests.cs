using System.Reflection;
using System.IO;
using PrintFlow.Domain.Outputs;
using PrintFlow.Infrastructure.Adapters.Photoshop;

namespace PrintFlow.Tests.Architecture;

/// <summary>Structural B1A.3 boundaries that runtime tests cannot prove by outcome alone.</summary>
public sealed class PhotoshopPreparationBoundaryTests
{
    [Fact]
    public void Public_preparation_seam_is_one_typed_closed_operation()
    {
        MethodInfo method = typeof(IPhotoshopPreparationAutomation).GetMethod(
            nameof(IPhotoshopPreparationAutomation.PrepareDocumentAsync)).ShouldNotBeNull();
        method.GetParameters().Select(p => p.ParameterType).ShouldBe(
        [
            typeof(PhotoshopOpenedDocument),
            typeof(PhotoshopPreparation),
            typeof(CancellationToken),
        ]);

        typeof(IPhotoshopPreparationAutomation).GetMethods()
            .SelectMany(m => m.GetParameters())
            .Select(p => p.ParameterType)
            .ShouldNotContain(typeof(string));
    }

    [Fact]
    public void Native_command_carries_source_preconditions_but_no_projected_target_pair()
    {
        string[] properties = typeof(PhotoshopNativePreparationCommand).GetProperties()
            .Select(p => p.Name)
            .ToArray();

        properties.ShouldBe(
        [
            nameof(PhotoshopNativePreparationCommand.ExpectedDocumentFullPath),
            nameof(PhotoshopNativePreparationCommand.ExpectedSourcePixelWidth),
            nameof(PhotoshopNativePreparationCommand.ExpectedSourcePixelHeight),
            nameof(PhotoshopNativePreparationCommand.Edge),
            nameof(PhotoshopNativePreparationCommand.EdgeMillimetres),
            nameof(PhotoshopNativePreparationCommand.ResolutionPpi),
            nameof(PhotoshopNativePreparationCommand.ResampleMethod),
        ], ignoreOrder: true);
        properties.ShouldNotContain(p => p.Contains("Projected", StringComparison.Ordinal));
    }

    [Fact]
    public void Preparation_implementation_has_no_session_authority_or_output_path()
    {
        string source = PreparationSource();
        source.ShouldNotContain("EnlargementAuthority", Case.Sensitive);
        source.ShouldNotContain("ProcessingSession", Case.Sensitive);
        source.ShouldNotContain("SessionService", Case.Sensitive);
        source.ShouldNotContain("doAction(", Case.Insensitive);
        source.ShouldNotContain("PrintFlow DTF", Case.Sensitive);
        source.ShouldNotContain("changeMode(", Case.Insensitive);
        source.ShouldNotContain(".saveAs(", Case.Insensitive);
        source.ShouldNotContain("new AdapterOutput", Case.Sensitive);
        source.ShouldNotContain("new Revision", Case.Sensitive);
        source.ShouldNotContain("new PrintOutput", Case.Sensitive);
    }

    [Fact]
    public void Factual_preparation_result_cannot_be_mistaken_for_workflow_output()
    {
        Type type = typeof(PhotoshopPreparedDocument);
        type.Assembly.GetName().Name.ShouldBe("PrintFlow.Infrastructure");
        type.GetProperties().Select(p => p.Name).ShouldBe(
        [
            nameof(PhotoshopPreparedDocument.Before),
            nameof(PhotoshopPreparedDocument.Actual),
            nameof(PhotoshopPreparedDocument.AppliedDirection),
            nameof(PhotoshopPreparedDocument.AppliedResizePolicy),
            nameof(PhotoshopPreparedDocument.CommandedEdge),
            nameof(PhotoshopPreparedDocument.OtherDocumentsMayBeOpen),
            nameof(PhotoshopPreparedDocument.BackingWorkingSha256),
        ], ignoreOrder: true);
    }

    [Fact]
    public void Native_resampling_vocabulary_is_exactly_the_three_accepted_identifiers()
    {
        Enum.GetNames<PhotoshopNativeResampleMethod>().ShouldBe(
        ["NONE", "BICUBICSHARPER", "PRESERVEDETAILS"], ignoreOrder: true);

        string source = File.ReadAllText(Path.Combine(
            ProjectDirectory("PrintFlow.Infrastructure"),
            "Adapters", "Photoshop", "PhotoshopPreparationNativeBridge.cs"));
        source.ShouldNotContain("ResampleMethod.AUTOMATIC", Case.Insensitive);
        source.ShouldNotContain("ResampleMethod.BICUBICSMOOTHER", Case.Insensitive);
        source.ShouldNotContain("JSON.stringify", Case.Sensitive);
        source.ShouldContain("PF-B1A3-1", Case.Sensitive);
    }

    private static string PreparationSource()
    {
        string directory = Path.Combine(
            ProjectDirectory("PrintFlow.Infrastructure"), "Adapters", "Photoshop");
        return string.Join("\n", new[]
        {
            "IPhotoshopPreparationAutomation.cs",
            "GuardedPhotoshopDocumentPreparer.cs",
            "PhotoshopPreparationNativeBridge.cs",
        }.Select(file => File.ReadAllText(Path.Combine(directory, file))));
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
