using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;

namespace PrintFlow.Tests.Unit.Domain;

/// <summary>
/// The value objects that protect boundaries. Each test covers a rule that, if broken,
/// would let a bad value travel far from where it was created.
/// </summary>
public sealed class ValueObjectTests
{
    // -----------------------------------------------------------------------------
    // Sha256
    // -----------------------------------------------------------------------------

    [Fact]
    public void Sha256_normalises_to_uppercase_so_comparison_is_never_case_dependent()
    {
        Sha256 lower = Sha256.Parse(new string('a', 64));
        Sha256 upper = Sha256.Parse(new string('A', 64));

        lower.ShouldBe(upper);
        lower.Value.ShouldBe(new string('A', 64));
    }

    [Theory]
    [InlineData("")]
    [InlineData("A114B5D2")]
    [InlineData("not-a-hash-not-a-hash-not-a-hash-not-a-hash-not-a-hash-not-a-h")]
    public void Sha256_rejects_anything_that_is_not_a_64_character_digest(string candidate)
    {
        Sha256.TryParse(candidate, out _).ShouldBeFalse();
        Should.Throw<ArgumentException>(() => Sha256.Parse(candidate));
    }

    [Fact]
    public void Sha256_from_bytes_matches_the_canonical_hex_form()
    {
        // The SHA-256 of the empty input, a standard published vector.
        byte[] digest = Convert.FromHexString(
            "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855");

        Sha256.FromBytes(digest).Value.ShouldBe(
            "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855");
    }

    [Fact]
    public void Sha256_short_form_is_for_display_only()
    {
        Sha256 hash = Sha256.Parse("A114B5D2B1D7BF793001DA13CFA429D84270EA816033C3A851317275918383A6");

        hash.ShortForm.ShouldBe("A114B5D2B1D7");
        hash.ShortForm.Length.ShouldBeLessThan(Sha256.HexLength);
    }

    // -----------------------------------------------------------------------------
    // OutputName
    // -----------------------------------------------------------------------------

    [Theory]
    [InlineData("Portrait_HD")]
    [InlineData("客户设计稿")]
    [InlineData("logo 2026")]
    public void OutputName_accepts_ordinary_and_Chinese_names(string candidate)
    {
        OutputName.Create(candidate).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData("bad/name")]
    [InlineData("bad:name")]
    [InlineData("bad*name")]
    public void OutputName_rejects_empty_and_Windows_forbidden_names(string candidate)
    {
        OutputName.Create(candidate).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void OutputName_rejects_a_stem_beyond_the_length_budget()
    {
        OutputName.Create(new string('x', OutputName.MaxLength + 1)).IsValid.ShouldBeFalse();
        OutputName.Create(new string('x', OutputName.MaxLength)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void OutputName_trims_trailing_dots_and_spaces_that_Windows_would_drop()
    {
        OutputName.Parse("  Portrait_HD.  ").Value.ShouldBe("Portrait_HD");
    }

    // -----------------------------------------------------------------------------
    // Workspace references
    // -----------------------------------------------------------------------------

    [Fact]
    public void A_workspace_reference_normalises_separators_and_keeps_its_area()
    {
        WorkspaceFileRef file = WorkspaceFileRef.Create(
            @"Sessions\S_20260818T090000Z_a1b2c3\Approved\Portrait_HD.png", WorkspaceArea.Approved);

        file.RelativePath.ShouldBe("Sessions/S_20260818T090000Z_a1b2c3/Approved/Portrait_HD.png");
        file.FileName.ShouldBe("Portrait_HD.png");
        file.Area.ShouldBe(WorkspaceArea.Approved);
    }

    [Theory]
    [InlineData(@"..\..\Windows\System32")]
    [InlineData("Sessions/../../secret")]
    [InlineData(@"D:\PrintFlowStudio\Baseline\preset.json")]
    [InlineData("/rooted/path")]
    [InlineData("")]
    public void A_workspace_reference_refuses_to_escape_the_workspace_root(string candidate)
    {
        Should.Throw<ArgumentException>(() =>
            WorkspaceFileRef.Create(candidate, WorkspaceArea.Working));
    }

    // -----------------------------------------------------------------------------
    // PrintDimensions
    // -----------------------------------------------------------------------------

    [Fact]
    public void PrintDimensions_derive_pixels_at_the_fixed_production_resolution()
    {
        PrintDimensions dimensions = PrintDimensions.FromMillimetres(280, 400, SizePreset.A3Portrait);

        dimensions.Dpi.ShouldBe(300);
        PrintDimensions.ProductionDpi.ShouldBe(300);

        // 280 mm at 300 dpi is 280 / 25.4 * 300 = 3307 px.
        dimensions.PixelWidth.ShouldBe(3307);
        dimensions.PixelHeight.ShouldBe(4724);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-5, 100)]
    [InlineData(double.NaN, 100)]
    public void PrintDimensions_reject_non_positive_or_non_finite_millimetres(double width, double height)
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
            PrintDimensions.FromMillimetres(width, height, SizePreset.Custom));
    }

    /// <summary>
    /// The non-throwing factory refuses exactly what the throwing one does
    /// (Epic 11100 Part 3C3B §4).
    /// </summary>
    /// <remarks>
    /// The operator-input path uses <c>TryFromMillimetres</c>, so if the two ever disagreed the
    /// UI would accept a size the domain considers unusable. Asserting them against each other
    /// — rather than restating the rule a third time — is what keeps that from happening.
    /// </remarks>
    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-5, 100)]
    [InlineData(100, -5)]
    [InlineData(double.NaN, 100)]
    [InlineData(100, double.PositiveInfinity)]
    public void PrintDimensions_try_factory_refuses_what_the_throwing_factory_refuses(double width, double height)
    {
        PrintDimensions.TryFromMillimetres(width, height, SizePreset.Custom, out PrintDimensions refused)
            .ShouldBeFalse();
        refused.ShouldBe(default);

        Should.Throw<ArgumentOutOfRangeException>(() =>
            PrintDimensions.FromMillimetres(width, height, SizePreset.Custom));
    }

    [Fact]
    public void PrintDimensions_try_factory_produces_the_same_value_as_the_throwing_one()
    {
        PrintDimensions.TryFromMillimetres(210, 297, SizePreset.A4, out PrintDimensions accepted)
            .ShouldBeTrue();

        accepted.ShouldBe(PrintDimensions.FromMillimetres(210, 297, SizePreset.A4));
    }

    /// <summary>
    /// Every named preset has a nominal size and produces dimensions tagged with itself
    /// (Part 3C3B §5).
    /// </summary>
    [Theory]
    [InlineData(SizePreset.A3Landscape, 420, 297)]
    [InlineData(SizePreset.A3Portrait, 297, 420)]
    [InlineData(SizePreset.A4, 210, 297)]
    [InlineData(SizePreset.A5, 148, 210)]
    public void PrintDimensions_from_preset_uses_the_presets_nominal_millimetres(
        SizePreset preset, double widthMm, double heightMm)
    {
        PrintDimensions.NominalMillimetres(preset).ShouldBe((widthMm, heightMm));

        PrintDimensions dimensions = PrintDimensions.FromPreset(preset);
        dimensions.WidthMm.ShouldBe(widthMm);
        dimensions.HeightMm.ShouldBe(heightMm);
        dimensions.Preset.ShouldBe(preset);

        // Pixels still come from the one conversion, never from a table of pixel sizes.
        dimensions.ShouldBe(PrintDimensions.FromMillimetres(widthMm, heightMm, preset));
    }

    /// <summary>Custom is what typing produces, so it has no nominal size to offer.</summary>
    [Fact]
    public void PrintDimensions_custom_has_no_nominal_size()
    {
        PrintDimensions.NominalMillimetres(SizePreset.Custom).ShouldBeNull();
        Should.Throw<ArgumentOutOfRangeException>(() => PrintDimensions.FromPreset(SizePreset.Custom));
    }

    // -----------------------------------------------------------------------------
    // Identifiers and results
    // -----------------------------------------------------------------------------

    [Fact]
    public void Typed_identifiers_of_different_kinds_are_different_types()
    {
        Guid raw = Guid.CreateVersion7();

        SessionId session = SessionId.From(raw);
        RevisionId revision = RevisionId.From(raw);

        session.Value.ShouldBe(revision.Value);
        session.GetType().ShouldNotBe(revision.GetType());
    }

    [Fact]
    public void Generated_identifiers_are_UUIDv7_and_therefore_time_ordered()
    {
        Guid first = SystemIdGenerator.Instance.NewId();
        Guid second = SystemIdGenerator.Instance.NewId();

        first.Version.ShouldBe(7);
        second.Version.ShouldBe(7);
        first.ShouldNotBe(second);
    }

    [Fact]
    public void A_successful_result_carries_its_value_and_refuses_to_yield_a_failure()
    {
        OperationResult<int> result = OperationResult.Ok(42);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
        Should.Throw<InvalidOperationException>(() => result.Failure);
    }

    [Fact]
    public void A_failed_result_carries_its_failure_and_refuses_to_yield_a_value()
    {
        OperationResult<int> result = OperationResult.Fail<int>(
            FailureCode.OutputMissing, "the expected TIFF was not written");

        result.IsFailure.ShouldBeTrue();
        result.Failure.Code.ShouldBe(FailureCode.OutputMissing);
        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void Failure_codes_keep_stable_English_names_because_they_are_persisted()
    {
        // Renaming any of these silently breaks stored history and recovery routing.
        string[] required =
        [
            "AdapterUnavailable", "EnvironmentNotVerified", "PresetHashMismatch", "UnknownDialog",
            "Timeout", "Cancelled", "OutputMissing", "OutputUnreadable", "OutputValidationFailed",
            "RevisionIntegrityMismatch", "WorkspaceError", "PersistenceError", "PreconditionNotMet",
            "ManualCropRequired",

            // Epic 11300 Part A §21 — the minimum stable set for Meitu automation.
            "MeituNotInstalled", "MeituLaunchFailed", "MeituWindowNotFound", "MeituTargetLost",
            "MeituUnknownState", "MeituBlockingDialog", "MeituOpenInputFailed",

            // Epic 11400 Part A — the same stable set for Photoshop automation, plus the one
            // code that has no Meitu counterpart: PrintFlow will not proceed against a Photoshop
            // document it cannot positively identify as the file it handed over.
            "PhotoshopNotInstalled", "PhotoshopLaunchFailed", "PhotoshopWindowNotFound",
            "PhotoshopTargetLost", "PhotoshopUnknownState", "PhotoshopBlockingDialog",
            "PhotoshopOpenInputFailed", "PhotoshopDocumentIdentityUnconfirmed",
            "PsdUnsupported", "PsdCompositeMissing", "PsdUnreadable", "PsdPreparationFailed",
            "PdfUnreadable", "PdfEncrypted", "PdfMultiplePages", "PdfPreparationFailed",

            // SCRUM-11116 — the two ways a chosen file is refused at import, kept apart because
            // they mean different things to an operator: a container this product has no
            // production path for, and an accepted container carrying no readable image.
            "SourceFormatUnsupported", "SourceImageUnreadable",
        ];

        Enum.GetNames<FailureCode>().ShouldBe(required, ignoreOrder: true);
    }

    [Fact]
    public void The_W1_enum_offers_exactly_the_three_validated_branches()
    {
        Enum.GetNames<WhiteUnderbaseBranch>().ShouldBe(["W1_0px", "W1_1px", "W1_2px"], ignoreOrder: true);
    }
}
