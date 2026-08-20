using System.Reflection;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Architecture;

/// <summary>
/// Where the image-preview slice is allowed to reach (Epic 11200 Part C1 §4, §29).
/// </summary>
/// <remarks>
/// The first test is the one that matters. §22 asks for "arbitrary path reads are impossible or
/// rejected", and the honest way to deliver that is the first of the two: an API that takes a
/// <c>SessionId</c> and a <c>RevisionId</c> has no arbitrary-path case to reject, because there
/// is no path to pass. Asserting the <i>shape</i> of the interface is therefore a stronger
/// statement than any number of "and this bad path is refused" cases would be — those could
/// only ever cover the paths someone thought of.
/// </remarks>
public sealed class PreviewBoundaryTests
{
    private static Assembly Shell => typeof(global::PrintFlow.App.App).Assembly;

    /// <summary>
    /// Neither preview seam accepts a string. There is no <c>ReadAnyFile(string)</c> (§4, §22).
    /// </summary>
    /// <remarks>
    /// <see cref="IImagePreviewDecoder"/> is included even though it is an infrastructure-facing
    /// port: it takes a <see cref="WorkspaceFileRef"/>, so the containment guarantee holds one
    /// level further down than the service, and a later implementation cannot quietly start
    /// resolving paths of its own.
    /// </remarks>
    [Theory]
    [InlineData(typeof(IArtefactPreviewService))]
    [InlineData(typeof(IImagePreviewDecoder))]
    public void The_preview_seams_accept_no_string_parameter(Type seam)
    {
        List<string> offenders = [];
        foreach (MethodInfo method in seam.GetMethods())
        {
            offenders.AddRange(method.GetParameters()
                .Where(parameter => parameter.ParameterType == typeof(string))
                .Select(parameter => $"{seam.Name}.{method.Name}({parameter.Name})"));
        }

        offenders.ShouldBeEmpty(
            "a preview seam that took a path would be an arbitrary-file-read API by another name.");
    }

    /// <summary>The service names identities the caller already holds, and nothing else (§4).</summary>
    [Fact]
    public void The_preview_service_is_addressed_by_session_and_revision_identity()
    {
        MethodInfo request = typeof(IArtefactPreviewService)
            .GetMethod(nameof(IArtefactPreviewService.GetPreviewAsync))!;

        request.GetParameters().Select(p => p.ParameterType).ShouldBe(
            [typeof(SessionId), typeof(RevisionId), typeof(CancellationToken)]);
    }

    /// <summary>
    /// The preview path cannot change anything (§29).
    /// </summary>
    /// <remarks>
    /// Checked at the seam rather than by reading the implementation: neither the service nor
    /// the decoder may name a repository mutation, a workflow command or a workspace write in
    /// its signature, so a preview can never be the thing that advanced a session.
    /// </remarks>
    [Theory]
    [InlineData(typeof(IArtefactPreviewService))]
    [InlineData(typeof(IImagePreviewDecoder))]
    public void The_preview_seams_name_no_mutating_type(Type seam)
    {
        string[] forbidden = ["SessionMutation", "WorkflowCommand", "ProcessingSession", "SessionAggregate"];

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
    /// The one stream in the preview path lives in the view layer, not in a view model.
    /// </summary>
    /// <remarks>
    /// <c>BannedApiEnforcementTests</c> already proves no <c>System.IO</c> text appears under
    /// <c>ViewModels\</c>. This says the positive half: the converter that does need a stream
    /// exists, is in <c>PrintFlow.App.Views</c>, and is therefore the reason the view models can
    /// stay clean rather than an accident of them not needing one yet (§29).
    /// </remarks>
    [Fact]
    public void The_payload_converter_lives_in_the_view_layer()
    {
        Type converter = Shell.GetTypes().Single(t => t.Name == "PreviewPayloadConverter");

        converter.Namespace.ShouldBe("PrintFlow.App.Views");
        typeof(System.Windows.Data.IValueConverter).IsAssignableFrom(converter).ShouldBeTrue();
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
}
