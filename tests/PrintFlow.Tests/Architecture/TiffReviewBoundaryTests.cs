using System.IO;
using System.Reflection;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Architecture;

/// <summary>
/// Where the production-TIFF review slice is allowed to reach (SCRUM-11104 §43, §44).
/// </summary>
/// <remarks>
/// The same shape <c>PreviewBoundaryTests</c> asserts for the general preview seam, restated for
/// the specialist one — because the specialist one is the more tempting to widen. It reads a
/// production file, it knows the TIFF layout, and the shortest route to "just show me that other
/// file" would be a path parameter. Asserting the <i>shape</i> of the interface is what makes
/// that not expressible rather than merely not done.
/// </remarks>
public sealed class TiffReviewBoundaryTests
{
    private static Assembly Shell => typeof(global::PrintFlow.App.App).Assembly;

    /// <summary>Neither TIFF review seam accepts a path (§43).</summary>
    [Theory]
    [InlineData(typeof(IProductionTiffReviewService))]
    [InlineData(typeof(ITiffReviewDecoder))]
    public void The_TIFF_review_seams_accept_no_string_parameter(Type seam)
    {
        List<string> offenders = [];
        foreach (MethodInfo method in seam.GetMethods())
        {
            offenders.AddRange(method.GetParameters()
                .Where(parameter => parameter.ParameterType == typeof(string))
                .Select(parameter => $"{seam.Name}.{method.Name}({parameter.Name})"));
        }

        offenders.ShouldBeEmpty(
            "a review seam that took a path would be an arbitrary-file-read API by another name.");
    }

    /// <summary>The service names identities the caller already holds, and nothing else.</summary>
    [Fact]
    public void The_review_service_is_addressed_by_session_and_revision_identity()
    {
        MethodInfo request = typeof(IProductionTiffReviewService)
            .GetMethod(nameof(IProductionTiffReviewService.GetReviewAsync))!;

        request.GetParameters().Select(p => p.ParameterType).ShouldBe(
            [typeof(SessionId), typeof(RevisionId), typeof(CancellationToken)]);
    }

    /// <summary>
    /// The decoder is told which bytes it is being asked for (§16, §17).
    /// </summary>
    /// <remarks>
    /// The <see cref="Sha256"/> parameter is the binding, and it is asserted here rather than left
    /// to the implementation: a decoder whose signature did not name the expected hash could only
    /// ever decode "whatever is at this reference", which is exactly the state where one file is
    /// displayed and another approved.
    /// </remarks>
    [Fact]
    public void The_decoder_is_given_the_hash_it_is_expected_to_find()
    {
        MethodInfo decode = typeof(ITiffReviewDecoder)
            .GetMethod(nameof(ITiffReviewDecoder.DecodeAsync))!;

        decode.GetParameters().Select(p => p.ParameterType).ShouldBe(
            [typeof(WorkspaceFileRef), typeof(Sha256), typeof(CancellationToken)]);
    }

    /// <summary>Neither seam can change anything (§36).</summary>
    [Theory]
    [InlineData(typeof(IProductionTiffReviewService))]
    [InlineData(typeof(ITiffReviewDecoder))]
    public void The_TIFF_review_seams_name_no_mutating_type(Type seam)
    {
        string[] forbidden =
            ["SessionMutation", "WorkflowCommand", "ProcessingSession", "SessionAggregate", "ReviewDecision"];

        List<string> offenders = [];
        foreach (MethodInfo method in seam.GetMethods())
        {
            IEnumerable<Type> named = method.GetParameters()
                .Select(p => p.ParameterType)
                .Append(method.ReturnType)
                .SelectMany(Unwrap);

            offenders.AddRange(named
                .Where(type => forbidden.Contains(type.Name, StringComparer.Ordinal))
                .Select(type => $"{seam.Name}.{method.Name} -> {type.Name}"));
        }

        offenders.ShouldBeEmpty();
    }

    /// <summary>
    /// No WPF type crosses into the Workflow payload (§44).
    /// </summary>
    /// <remarks>
    /// <c>DependencyRuleTests</c> already proves the Workflow assembly references no WPF at all,
    /// so an <c>ImageSource</c> here could not compile. What this adds is the positive shape: the
    /// three representations are carried as encoded bytes, which is what lets the same payload be
    /// produced off the UI thread, compared in a test, and turned into a bitmap only by the view.
    /// </remarks>
    [Fact]
    public void The_review_payload_carries_encoded_bytes_rather_than_images()
    {
        foreach (string name in new[] { "ColourPayload", "WhiteInkPayload", "OverlayPayload" })
        {
            typeof(TiffReviewPayload).GetProperty(name)!.PropertyType
                .ShouldBe(typeof(ReadOnlyMemory<byte>), name);
            typeof(DecodedTiffReview).GetProperty(name)!.PropertyType
                .ShouldBe(typeof(ReadOnlyMemory<byte>), name);
        }
    }

    /// <summary>
    /// The App parses no TIFF (§43).
    /// </summary>
    /// <remarks>
    /// Asserted as source text because the failure mode is a well-meant helper: a view model that
    /// "just peeked at the fifth sample" to draw a legend would be a second TIFF reader with its
    /// own idea of the accepted layout, and the first one to disagree with validation would be the
    /// one drawing pictures. The tokens named here are ones only a TIFF reader has any use for.
    /// <para>
    /// It sits beside the accepted rule in <c>PhotoshopWorkflowOutputBoundaryTests</c> rather than
    /// replacing it, and that rule is why the payload says <c>InkChannelCount</c> where the parser
    /// says <c>SamplesPerPixel</c>: the review surface reports a production fact in PrintFlow's
    /// own vocabulary, and the TIFF's is Infrastructure's alone (§27, §43).
    /// </para>
    /// </remarks>
    [Fact]
    public void The_shell_contains_no_TIFF_parsing()
    {
        string source = string.Join("\n", Directory
            .EnumerateFiles(ProjectDirectory("PrintFlow.App"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText));

        foreach (string token in new[]
                 {
                     "PhotometricInterpretation", "ExtraSamples", "StripOffsets", "StripByteCounts",
                     "RowsPerStrip", "8BIM", "ImageSourceData", "BinaryPrimitives",
                 })
        {
            source.ShouldNotContain(token, Case.Sensitive,
                "the shell displays a review payload; it never reads the TIFF itself.");
        }
    }

    /// <summary>
    /// The specialist surface is its own control, not a widening of the shared one (§14).
    /// </summary>
    [Fact]
    public void The_shared_review_surface_knows_nothing_about_ink()
    {
        string shared = File.ReadAllText(Path.Combine(
            ProjectDirectory("PrintFlow.App"), "Views", "SharedReviewSurface.cs"));

        foreach (string token in new[] { "Tiff", "WhiteInk", "W1", "Cmyk" })
        {
            shared.ShouldNotContain(token, Case.Sensitive,
                "SCRUM-11079's general comparison surface must not grow a production-TIFF mode.");
        }

        Shell.GetTypes().ShouldContain(type => type.Name == "TiffReviewSurface");
    }

    /// <summary>Generic arguments count too: a <c>Task&lt;OperationResult&lt;T&gt;&gt;</c> hides three types.</summary>
    private static IEnumerable<Type> Unwrap(Type type)
    {
        yield return type;
        foreach (Type argument in type.GetGenericArguments())
        {
            foreach (Type nested in Unwrap(argument))
            {
                yield return nested;
            }
        }
    }

    private static string ProjectDirectory(string project)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PrintFlowStudio.sln")))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new InvalidOperationException("The repository root was not found from the test host.")
            : Path.Combine(directory.FullName, "src", project);
    }
}
