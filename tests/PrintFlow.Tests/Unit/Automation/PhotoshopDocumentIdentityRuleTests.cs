using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Adapters.Photoshop;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Unit.Automation;

/// <summary>
/// The document-identity rule, exercised as a pure function (Epic 11400 Part A §11, §12).
/// </summary>
/// <remarks>
/// This is the rule that decides whether PrintFlow believes the right file is open, so it gets
/// the most adversarial tests in the slice: same name in the wrong folder, a name that is a
/// prefix of the expected one, an address bar that lost its signed prefix, and a probe that
/// returned nothing at all. Every one of them must refuse.
/// </remarks>
public sealed class PhotoshopDocumentIdentityRuleTests
{
    private static PhotoshopDocumentIdentitySignature Signature => PhotoshopFakes.DocumentIdentity();

    // -----------------------------------------------------------------------------------
    // Reading the document name out of the window title
    // -----------------------------------------------------------------------------------

    [Fact]
    public void The_title_of_a_loaded_document_yields_its_basename()
    {
        PhotoshopDocumentIdentityRule
            .DocumentNameInTitle(Signature, PhotoshopFakes.TitleFor("A_WORKING.png"))
            .ShouldBe("A_WORKING.png");
    }

    /// <summary>The bare application title names no document, which is how "nothing open" reads.</summary>
    [Fact]
    public void The_application_title_names_no_document()
    {
        PhotoshopDocumentIdentityRule
            .DocumentNameInTitle(Signature, PhotoshopFakes.NoDocumentTitle)
            .ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData(" @ 100% (RGB/8)")]
    public void A_title_with_no_leading_name_names_no_document(string? title)
    {
        PhotoshopDocumentIdentityRule.DocumentNameInTitle(Signature, title).ShouldBeNull();
    }

    /// <summary>
    /// A filename containing the separator still resolves to the whole filename.
    /// </summary>
    /// <remarks>
    /// Splitting on the <i>first</i> separator is what makes this work: Photoshop wrote the name
    /// into the leading segment, so a later occurrence belongs to the zoom-and-mode suffix.
    /// A last-occurrence split would silently truncate a legal filename.
    /// </remarks>
    [Fact]
    public void The_first_separator_bounds_the_name()
    {
        PhotoshopDocumentIdentityRule
            .DocumentNameInTitle(Signature, "ODD @ NAME.png @ 100% (图层 1, RGB/8)")
            .ShouldBe("ODD");
    }

    [Fact]
    public void A_matching_title_names_the_expected_document()
    {
        PhotoshopDocumentIdentityRule.TitleNamesExpectedDocument(
                Signature,
                PhotoshopFakes.TitleFor(PhotoshopFakes.ExpectedFileName),
                PhotoshopFakes.ExpectedFileName)
            .ShouldBeTrue();
    }

    /// <summary>Windows filenames are case-insensitive, so identity must be too.</summary>
    [Fact]
    public void Case_differences_in_the_name_still_match()
    {
        PhotoshopDocumentIdentityRule.TitleNamesExpectedDocument(
                Signature, PhotoshopFakes.TitleFor("pftest-a-0001_working.PNG"),
                PhotoshopFakes.ExpectedFileName)
            .ShouldBeTrue();
    }

    /// <summary>A name the expected one merely starts with is a different file.</summary>
    [Fact]
    public void A_prefix_of_the_expected_name_is_refused()
    {
        PhotoshopDocumentIdentityRule.TitleNamesExpectedDocument(
                Signature, PhotoshopFakes.TitleFor("PFTEST-A-0001_WORKING.png.bak"),
                PhotoshopFakes.ExpectedFileName)
            .ShouldBeFalse();

        PhotoshopDocumentIdentityRule.TitleNamesExpectedDocument(
                Signature, PhotoshopFakes.TitleFor("PFTEST-A-0001_WORK.png"),
                PhotoshopFakes.ExpectedFileName)
            .ShouldBeFalse();
    }

    // -----------------------------------------------------------------------------------
    // Reading the folder out of the address bar
    // -----------------------------------------------------------------------------------

    [Fact]
    public void The_signed_prefix_is_stripped_to_leave_the_folder()
    {
        OperationResult<string> folder = PhotoshopDocumentIdentityRule.FolderFromAddressText(
            Signature, $"地址: {PhotoshopFakes.WorkingDirectory}");

        folder.IsSuccess.ShouldBeTrue();
        folder.Value.ShouldBe(PhotoshopFakes.WorkingDirectory);
    }

    /// <summary>
    /// Address text without the signed prefix is refused, not searched for something path-shaped.
    /// </summary>
    /// <remarks>
    /// The whole value of this reading is that it stops being trusted the moment the surface
    /// stops being the one that was signed. A tolerant parser would keep producing confident
    /// answers from a dialog that had changed shape.
    /// </remarks>
    [Fact]
    public void Address_text_without_the_signed_prefix_is_refused()
    {
        OperationResult<string> folder = PhotoshopDocumentIdentityRule.FolderFromAddressText(
            Signature, PhotoshopFakes.WorkingDirectory);

        folder.IsFailure.ShouldBeTrue();
        folder.Failure.Code.ShouldBe(FailureCode.PhotoshopDocumentIdentityUnconfirmed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("地址: ")]
    public void Address_text_with_no_folder_after_the_prefix_is_refused(string? text)
    {
        PhotoshopDocumentIdentityRule.FolderFromAddressText(Signature, text)
            .IsFailure.ShouldBeTrue();
    }

    // -----------------------------------------------------------------------------------
    // Combining the two readings
    // -----------------------------------------------------------------------------------

    [Fact]
    public void The_folder_and_the_name_combine_into_the_document_path()
    {
        OperationResult<string> path = PhotoshopDocumentIdentityRule.ResolveObservedPath(
            PhotoshopFakes.WorkingDirectory, PhotoshopFakes.ExpectedFileName);

        path.IsSuccess.ShouldBeTrue();
        path.Value.ShouldBe(PhotoshopFakes.ExpectedPath);
    }

    /// <summary>
    /// A reported "file name" that is really a path means the surface is not the signed one.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Elsewhere\A.png")]
    [InlineData(@"sub\A.png")]
    [InlineData("sub/A.png")]
    public void A_reported_name_that_is_really_a_path_is_refused(string reported)
    {
        PhotoshopDocumentIdentityRule
            .ResolveObservedPath(PhotoshopFakes.WorkingDirectory, reported)
            .IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void An_empty_reported_name_is_refused()
    {
        PhotoshopDocumentIdentityRule
            .ResolveObservedPath(PhotoshopFakes.WorkingDirectory, "  ")
            .IsFailure.ShouldBeTrue();
    }

    [Theory]
    [InlineData("PFTEST-A-0001_WORKING.png", "PFTEST-A-0001_WORKING.png")]
    [InlineData("PFTEST-A-0001_WORKING.tif", "PFTEST-A-0001_WORKING.png")]
    public void The_signed_Save_As_Copy_filename_variant_corroborates_the_title(
        string saveAsName, string titleName)
    {
        PhotoshopDocumentIdentityRule
            .SaveAsCopyFileNameCorroboratesTitle(saveAsName, titleName)
            .ShouldBeTrue();
    }

    [Theory]
    [InlineData("OTHER.tif", "PFTEST-A-0001_WORKING.png")]
    [InlineData("PFTEST-A-0001_WORKING.tif", "OTHER.png")]
    [InlineData(@"C:\Elsewhere\PFTEST-A-0001_WORKING.tif", "PFTEST-A-0001_WORKING.png")]
    [InlineData("PFTEST-A-0001_WORKING", "PFTEST-A-0001_WORKING.png")]
    public void Anything_beyond_the_signed_extension_substitution_is_refused(
        string saveAsName, string titleName)
    {
        PhotoshopDocumentIdentityRule
            .SaveAsCopyFileNameCorroboratesTitle(saveAsName, titleName)
            .ShouldBeFalse();
    }

    // -----------------------------------------------------------------------------------
    // The comparison identity actually rests on
    // -----------------------------------------------------------------------------------

    [Fact]
    public void The_expected_path_matches_itself()
    {
        PhotoshopDocumentIdentityRule
            .MatchesExpectedDocument(PhotoshopFakes.ExpectedPath, PhotoshopFakes.ExpectedPath)
            .ShouldBeTrue();
    }

    /// <summary>
    /// The case the whole two-reading design exists for: right name, wrong folder.
    /// </summary>
    /// <remarks>
    /// Both documents produce an identical Photoshop window title. If identity had been allowed
    /// to rest on the basename, this would have been accepted — PrintFlow would have gone on to
    /// run an irreversible Action against a file it was never given (§12).
    /// </remarks>
    [Fact]
    public void The_same_file_name_in_a_different_folder_is_refused()
    {
        PhotoshopDocumentIdentityRule.MatchesExpectedDocument(
                PhotoshopFakes.ExpectedPath,
                $@"C:\Users\admin\Downloads\{PhotoshopFakes.ExpectedFileName}")
            .ShouldBeFalse();
    }

    [Fact]
    public void A_different_file_in_the_same_folder_is_refused()
    {
        PhotoshopDocumentIdentityRule.MatchesExpectedDocument(
                PhotoshopFakes.ExpectedPath,
                System.IO.Path.Combine(PhotoshopFakes.WorkingDirectory, "PFTEST-B-0002_WORKING.png"))
            .ShouldBeFalse();
    }

    /// <summary>No document at all is a refusal, never a pass by absence.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_absent_observation_is_refused(string? observed)
    {
        PhotoshopDocumentIdentityRule
            .MatchesExpectedDocument(PhotoshopFakes.ExpectedPath, observed)
            .ShouldBeFalse();
    }

    /// <summary>
    /// Redundant separators and same-directory segments are normalised, so they do not become
    /// spurious refusals.
    /// </summary>
    [Fact]
    public void Equivalent_spellings_of_the_same_path_match()
    {
        string awkward = System.IO.Path.Combine(
            PhotoshopFakes.WorkingDirectory, ".", PhotoshopFakes.ExpectedFileName);

        PhotoshopDocumentIdentityRule
            .MatchesExpectedDocument(PhotoshopFakes.ExpectedPath, awkward)
            .ShouldBeTrue();
    }

    [Fact]
    public void Case_differences_in_the_path_still_match()
    {
        PhotoshopDocumentIdentityRule
            .MatchesExpectedDocument(
                PhotoshopFakes.ExpectedPath, PhotoshopFakes.ExpectedPath.ToUpperInvariant())
            .ShouldBeTrue();
    }
}
