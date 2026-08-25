using System.Collections.Immutable;
using System.IO;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Meitu;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Tests.Unit.Automation;

/// <summary>
/// The pure parts of the export route: what a controlled destination has to look like, and how
/// Meitu's post-save surface is told apart from its Save surface
/// (Epic 11300 Part B2B §4, §11, §24).
/// </summary>
public sealed class MeituExportRuleTests
{
    private static string Rooted(string relative) =>
        Path.Combine(Path.GetTempPath(), "PrintFlowExportRule", relative);

    // -----------------------------------------------------------------------------
    // Destination resolution
    // -----------------------------------------------------------------------------

    [Fact]
    public void A_controlled_destination_is_taken_apart_into_the_pieces_each_control_needs()
    {
        string path = Rooted(Path.Combine("A_1", "PF_JOB_123_HD.png"));

        OperationResult<MeituExportDestination> resolved =
            MeituExportRule.ResolveDestination(path, "png");

        resolved.IsSuccess.ShouldBeTrue();
        resolved.Value.AbsolutePath.ShouldBe(path);
        resolved.Value.BaseName.ShouldBe("PF_JOB_123_HD");
        resolved.Value.Extension.ShouldBe("png");
        resolved.Value.Directory.ShouldBe(Path.GetDirectoryName(path));
    }

    /// <summary>
    /// The base name excludes the extension, because Meitu's field does.
    /// </summary>
    /// <remarks>
    /// Worth its own test because getting it wrong is silent: writing the whole file name into
    /// the field produces <c>Name_HD.png.png</c>, which is still a valid PNG at a path nothing
    /// downstream is looking for.
    /// </remarks>
    [Fact]
    public void The_base_name_excludes_the_extension_the_format_selector_supplies()
    {
        OperationResult<MeituExportDestination> resolved =
            MeituExportRule.ResolveDestination(Rooted("Name_HD.png"), "png");

        resolved.Value.BaseName.ShouldNotContain(".png");
        resolved.Value.BaseName.ShouldBe("Name_HD");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_destination_is_refused(string path)
    {
        MeituExportRule.ResolveDestination(path, "png").IsFailure.ShouldBeTrue();
    }

    /// <summary>
    /// A relative destination is refused: it would depend on a current directory PrintFlow does
    /// not control and Meitu does not share.
    /// </summary>
    [Theory]
    [InlineData("Working/A_1/a_HD.png")]
    [InlineData("a_HD.png")]
    [InlineData(@"..\a_HD.png")]
    public void A_destination_that_is_not_fully_qualified_is_refused(string path)
    {
        OperationResult<MeituExportDestination> resolved = MeituExportRule.ResolveDestination(path, "png");

        resolved.IsFailure.ShouldBeTrue();
        resolved.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
    }

    [Fact]
    public void A_destination_with_no_file_name_is_refused()
    {
        string directoryOnly = Path.Combine(Path.GetTempPath(), "PrintFlowExportRule") +
            Path.DirectorySeparatorChar;

        MeituExportRule.ResolveDestination(directoryOnly, "png").IsFailure.ShouldBeTrue();
    }

    /// <summary>
    /// An extension that disagrees with the signed format is refused before anything is invoked
    /// (§11).
    /// </summary>
    /// <remarks>
    /// The two are separate facts on this surface — the selector decides what Meitu encodes, the
    /// name decides what the file is called — and letting them disagree produces a file named
    /// <c>.png</c> containing a JPEG, which every downstream check that trusts the name then
    /// gets wrong.
    /// </remarks>
    [Theory]
    [InlineData("a_HD.jpg")]
    [InlineData("a_HD.webp")]
    [InlineData("a_HD.tif")]
    [InlineData("a_HD")]
    public void A_destination_whose_extension_disagrees_with_the_signed_format_is_refused(string name)
    {
        OperationResult<MeituExportDestination> resolved =
            MeituExportRule.ResolveDestination(Rooted(name), "png");

        resolved.IsFailure.ShouldBeTrue();
        resolved.Failure.Code.ShouldBe(FailureCode.PreconditionNotMet);
    }

    /// <summary>Windows extensions are case-insensitive, and so is this comparison.</summary>
    [Theory]
    [InlineData("a_HD.PNG")]
    [InlineData("a_HD.Png")]
    public void An_extension_matching_the_signed_format_in_any_casing_is_accepted(string name)
    {
        MeituExportRule.ResolveDestination(Rooted(name), "png").IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// Evidence that records no required format refuses the whole export rather than defaulting.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void A_signed_format_that_is_missing_refuses_the_export(string format)
    {
        OperationResult<MeituExportDestination> resolved =
            MeituExportRule.ResolveDestination(Rooted("a_HD.png"), format);

        resolved.IsFailure.ShouldBeTrue();
        resolved.Failure.Code.ShouldBe(FailureCode.EnvironmentNotVerified);
    }

    // -----------------------------------------------------------------------------
    // Recognising the post-save surface
    // -----------------------------------------------------------------------------

    private static MeituExportResultSignature Result(
        ImmutableArray<string>? markers = null, int minimum = 2) =>
        new(markers ?? ["EXP-SAVED", "EXP-OPEN-FOLDER"], minimum,
            new MeituControlSignature("", ".closeButton", "Button", "IconFontButton", UiPatternKind.Invoke));

    [Fact]
    public void The_signed_result_markers_identify_the_result_surface()
    {
        MeituExportRule.ShowsResultSurface(Result(), ["EXP-SAVED", "EXP-OPEN-FOLDER", "other"])
            .ShouldBeTrue();
    }

    [Fact]
    public void Fewer_markers_than_the_threshold_is_not_the_result_surface()
    {
        MeituExportRule.ShowsResultSurface(Result(), ["EXP-SAVED"]).ShouldBeFalse();
    }

    /// <summary>
    /// The Save surface is not the result surface, even though it shares a class and a title.
    /// </summary>
    /// <remarks>
    /// The whole reason this rule exists. Dismissing by window shape alone would click the close
    /// control of whichever surface happened to be up — including the Save surface on a run that
    /// failed before the export.
    /// </remarks>
    [Fact]
    public void The_save_surface_contents_do_not_read_as_the_result_surface()
    {
        MeituExportRule.ShowsResultSurface(Result(), ["保存图片", "保存路径", "文件名称与格式", "另存为", "保存"])
            .ShouldBeFalse();
    }

    [Fact]
    public void An_empty_observation_is_not_the_result_surface()
    {
        MeituExportRule.ShowsResultSurface(Result(), []).ShouldBeFalse();
    }

    /// <summary>
    /// Evidence naming no marker matches nothing rather than everything.
    /// </summary>
    /// <remarks>
    /// The inversion that keeps a fail-closed default: if an empty marker list matched, missing
    /// evidence would license PrintFlow to dismiss any owned Meitu surface it could find.
    /// </remarks>
    [Fact]
    public void Evidence_that_names_no_marker_matches_nothing()
    {
        MeituExportRule.ShowsResultSurface(Result([], minimum: 1), ["EXP-SAVED", "anything"])
            .ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3)]
    public void An_unsatisfiable_threshold_matches_nothing(int minimum)
    {
        MeituExportRule.ShowsResultSurface(
            Result(["EXP-SAVED", "EXP-OPEN-FOLDER"], minimum),
            ["EXP-SAVED", "EXP-OPEN-FOLDER"]).ShouldBeFalse();
    }

    /// <summary>One marker seen twice is one marker, not two.</summary>
    [Fact]
    public void A_repeated_marker_does_not_count_twice_toward_the_threshold()
    {
        MeituExportRule.ShowsResultSurface(Result(), ["EXP-SAVED", "EXP-SAVED", "EXP-SAVED"])
            .ShouldBeFalse();
    }
}
