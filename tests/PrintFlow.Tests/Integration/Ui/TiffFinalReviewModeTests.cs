using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Services;
using static PrintFlow.Tests.Fixtures.TiffFinalReviewFixture;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// The production-TIFF final review an operator actually meets: three modes over one validated
/// file, the production facts beside them, and a decision that is still bound to the exact bytes
/// (SCRUM-11104 §5–§12, §18–§28, §35–§40, §46–§49).
/// </summary>
/// <remarks>
/// Every session here is driven through a <see cref="SyntheticProductionTiffProcessor"/>, which
/// writes a <b>real</b> accepted production TIFF rather than the ordinary fake's copy of the
/// input. That is the whole reason these tests can say anything: a preview of a PNG named
/// <c>.tif</c> would prove nothing about separated CMYK or a W1 spot channel, and the ordinary
/// fake is deliberately left alone so every existing test keeps exercising what it always did.
/// <para>
/// The numbers are never taken from the screen alone. Effective resolution is recomputed here
/// from the <b>persisted</b> preparation, the output path is re-hashed from disk, and the
/// white-ink count is compared with what the file's own fifth sample holds — so a view model that
/// agreed with itself and with nothing else would fail (§51, §52, §53).
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class TiffFinalReviewModeTests
{
    private const double MillimetresPerInch = 25.4;

    // -----------------------------------------------------------------------------------
    // §5–§12, §35 — three modes over one canvas
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The specialist surface offers Colour, White ink and Colour + white over the reviewed file,
    /// and all three describe the same canvas (§5, §6, §8, §11, §35).
    /// </summary>
    [Fact]
    public async Task Final_review_offers_three_distinct_modes_over_the_one_TIFF_canvas()
    {
        using HomeScreenHarness harness = Harness(out SyntheticProductionTiffProcessor photoshop);
        Review review = await ReviewRequiredAsync(harness, "three-modes.png");
        SessionViewModel screen = review.Screen;

        screen.IsProductionTiffReview.ShouldBeTrue();
        screen.HasTiffReview.ShouldBeTrue();
        screen.IsTiffReviewUnavailable.ShouldBeFalse();
        screen.TiffReviewMode.ShouldBe(TiffReviewMode.Colour);

        byte[] colour = screen.TiffColourPayload.ToArray();
        byte[] white = screen.TiffWhiteInkPayload.ToArray();
        byte[] overlay = screen.TiffOverlayPayload.ToArray();

        colour.ShouldNotBeEmpty();
        white.ShouldNotBeEmpty();
        overlay.ShouldNotBeEmpty();
        colour.ShouldNotBe(white, "the white-ink mode must not be the colour image again");
        colour.ShouldNotBe(overlay, "the overlay must mark something the colour mode does not");
        white.ShouldNotBe(overlay);

        // One canvas: three payloads, one geometry, and the geometry is the TIFF's (§35).
        SessionAggregate persisted = await review.ReloadAsync();
        PrintOutput output = persisted.Outputs.ShouldHaveSingleItem();
        Revision tiff = persisted.Revisions.Last(r => r.Operation == OperationKind.PhotoshopOutput);

        foreach (ReadOnlyMemory<byte> payload in new[]
                 { screen.TiffColourPayload, screen.TiffWhiteInkPayload, screen.TiffOverlayPayload })
        {
            SyntheticImages.DecodePayloadBgra(payload, out int width, out int height);
            (width, height).ShouldBe((screen.TiffPreviewPixelWidth, screen.TiffPreviewPixelHeight));
        }

        screen.TiffPreviewPixelWidth.ShouldBe(tiff.Facts.PixelWidth!.Value);
        screen.TiffPreviewPixelHeight.ShouldBe(tiff.Facts.PixelHeight!.Value);

        // And it is this output's payload, not some other file's (§16).
        screen.TiffReviewPrintOutputId.ShouldBe(output.Id.Value.ToString());
        screen.TiffReviewSha256.ShouldBe(output.Sha256.Value);

        photoshop.GenerateCount.ShouldBe(1);
    }

    /// <summary>
    /// The white-ink preview is the file's own fifth sample, region by region (§8, §9, §10, §51).
    /// </summary>
    /// <remarks>
    /// The synthetic design writes three vertical bands of stored W1 — 255, 128, 0 — so the
    /// preview must run black, mid grey, white across the canvas. Nothing about the colour
    /// content could produce that, and neither could the alpha byte: there isn't one.
    /// </remarks>
    [Fact]
    public async Task The_white_ink_mode_shows_the_TIFFs_own_fifth_sample()
    {
        using HomeScreenHarness harness = Harness(out _);
        Review review = await ReviewRequiredAsync(harness, "white-ink.png");

        byte[] white = SyntheticImages.DecodePayloadBgra(
            review.Screen.TiffWhiteInkPayload, out int width, out int height);

        int y = height / 2;
        Grey(white, width, width / 6, y).ShouldBeLessThan((byte)8, "no stored ink must read as black");
        Grey(white, width, width / 2, y).ShouldBeInRange((byte)118, (byte)137, "half ink reads as mid grey");
        Grey(white, width, width * 5 / 6, y).ShouldBeGreaterThan((byte)247, "full ink reads as white");

        // The same count the inspector established from the same sample.
        TiffMetadataRow row = review.Screen.TiffMetadata.Single(r => r.Key == "WhiteInk");
        SessionAggregate persisted = await review.ReloadAsync();
        Revision tiff = persisted.Revisions.Last(r => r.Operation == OperationKind.PhotoshopOutput);

        long expected = (long)tiff.Facts.PixelWidth! / 3 * 2 * tiff.Facts.PixelHeight!.Value;
        row.Value.ShouldContain(expected.ToString("N0", CultureInfo.CurrentCulture));
    }

    // -----------------------------------------------------------------------------------
    // §36, §49 — a mode is a way of looking, never a decision
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Switching modes changes nothing about the output, the file, the step or the review (§36, §49).
    /// </summary>
    [Fact]
    public async Task Switching_mode_changes_nothing_that_an_approval_is_bound_to()
    {
        using HomeScreenHarness harness = Harness(out SyntheticProductionTiffProcessor photoshop);
        Review review = await ReviewRequiredAsync(harness, "authority.png");
        SessionViewModel screen = review.Screen;

        SessionAggregate before = await review.ReloadAsync();
        PrintOutput output = before.Outputs.ShouldHaveSingleItem();
        string path = harness.Inner.FileWorkspace.ResolveAbsolute(output.File);
        byte[] bytes = await File.ReadAllBytesAsync(path);
        int runs = photoshop.GenerateCount;

        foreach (TiffReviewMode mode in new[]
                 { TiffReviewMode.WhiteInk, TiffReviewMode.Overlay, TiffReviewMode.Colour })
        {
            screen.TiffReviewMode = mode;

            screen.TiffReviewPrintOutputId.ShouldBe(output.Id.Value.ToString());
            screen.TiffReviewSha256.ShouldBe(output.Sha256.Value);
            screen.CanApprove.ShouldBeTrue();
            screen.CanReject.ShouldBeTrue();
        }

        SessionAggregate after = await review.ReloadAsync();
        after.Outputs.ShouldHaveSingleItem().ShouldBe(output);
        after.Reviews.ShouldNotContain(r => r.Step == StepKind.PhotoshopOutput);
        after.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State.ShouldBe(StepState.ReviewRequired);
        (await File.ReadAllBytesAsync(path)).ShouldBe(bytes, "looking at a TIFF must never rewrite it");
        photoshop.GenerateCount.ShouldBe(runs, "a mode switch must never regenerate anything");

        // And the decision itself still works, from whichever mode the operator was in.
        await screen.ApproveCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        PrintOutput approved = (await review.ReloadAsync()).Outputs.ShouldHaveSingleItem();
        approved.ReviewState.ShouldBe(ReviewState.Approved);
        approved.Sha256.ShouldBe(output.Sha256);
    }

    // -----------------------------------------------------------------------------------
    // §23, §25–§28, §47, §53 — the production metadata block
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Every required fact is present, and each is the one that was recorded (§25–§28, §47, §53).
    /// </summary>
    [Fact]
    public async Task The_metadata_block_states_the_recorded_production_facts()
    {
        using HomeScreenHarness harness = Harness(out _);
        Review review = await ReviewRequiredAsync(harness, "metadata.png");
        SessionViewModel screen = review.Screen;

        SessionAggregate persisted = await review.ReloadAsync();
        PrintOutput output = persisted.Outputs.ShouldHaveSingleItem();
        Revision tiff = persisted.Revisions.Last(r => r.Operation == OperationKind.PhotoshopOutput);
        PhotoshopPreparation preparation = persisted.Attempts
            .Single(a => a.OutputRevisionId == tiff.Id).Preparation!;

        string[] keys = [.. screen.TiffMetadata.Select(row => row.Key)];
        keys.ShouldBe(
            ["OutputFile", "OutputPath", "Pixels", "Physical", "Resolution", "EffectiveDpi", "Colour", "WhiteInk", "Preset", "Sha256"],
            "the block states every available fact, in a stable order, and invents none");

        string expectedPath = harness.Inner.FileWorkspace.ResolveAbsolute(output.File);
        Value(screen, "OutputFile").ShouldBe(output.File.FileName);
        Value(screen, "OutputPath").ShouldBe(expectedPath);
        screen.TiffOutputPath.ShouldBe(expectedPath);
        screen.TiffOutputFileName.ShouldBe(output.File.FileName);

        // Independently: the file at the displayed path really is the reviewed one (§53).
        HashOf(expectedPath).ShouldBe(output.Sha256.Value);

        Value(screen, "Pixels").ShouldContain(tiff.Facts.PixelWidth!.Value.ToString(CultureInfo.CurrentCulture));
        Value(screen, "Pixels").ShouldContain(tiff.Facts.PixelHeight!.Value.ToString(CultureInfo.CurrentCulture));

        double widthMm = preparation.ProjectedPixelWidth * MillimetresPerInch / preparation.ProductionDpi;
        Value(screen, "Physical").ShouldContain(widthMm.ToString("0.0", CultureInfo.CurrentCulture));

        Value(screen, "Resolution").ShouldContain("300");
        Value(screen, "Resolution").ShouldContain("PPI");

        Value(screen, "Colour").ShouldContain("CMYK");
        Value(screen, "Colour").ShouldContain("8");

        Value(screen, "WhiteInk").ShouldContain("W1");

        // The signed preset this output was made with, by identity and manifest hash — and never
        // the literal "unknown" the persistence layer carries for the version it does not store.
        string preset = Value(screen, "Preset");
        preset.ShouldContain(output.Preset.PresetId);
        preset.ShouldContain(output.Preset.ManifestSha256.ShortForm);
        preset.ShouldNotContain("unknown", Case.Insensitive);

        // Readable, and not the operator's job to compare (§28).
        string hash = Value(screen, "Sha256");
        hash.ShouldContain("…");
        hash.ShouldStartWith(output.Sha256.Value[..8]);
        hash.ShouldEndWith(output.Sha256.Value[^8..]);
    }

    // -----------------------------------------------------------------------------------
    // §18–§22, §46, §52 — effective source resolution
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Effective resolution is source pixels over the requested physical size, verified
    /// independently from what was persisted (§19, §46, §52).
    /// </summary>
    [Fact]
    public async Task Effective_source_resolution_is_source_pixels_over_the_requested_inches()
    {
        using HomeScreenHarness harness = Harness(out _);
        Review review = await ReviewRequiredAsync(
            harness, "effective-dpi.png", sourceWidth: 1200, sourceHeight: 900,
            maxWidthMm: 60, maxHeightMm: 60);

        SessionAggregate persisted = await review.ReloadAsync();
        Revision tiff = persisted.Revisions.Last(r => r.Operation == OperationKind.PhotoshopOutput);
        PhotoshopPreparation preparation = persisted.Attempts
            .Single(a => a.OutputRevisionId == tiff.Id).Preparation!;

        // Recomputed here from the persisted preparation, not read back from the screen.
        preparation.SourcePixelWidth.ShouldBe(1200);
        preparation.SourcePixelHeight.ShouldBe(900);
        double widthMm = preparation.ProjectedPixelWidth * MillimetresPerInch / preparation.ProductionDpi;
        double heightMm = preparation.ProjectedPixelHeight * MillimetresPerInch / preparation.ProductionDpi;
        double expectedX = 1200 / (widthMm / MillimetresPerInch);
        double expectedY = 900 / (heightMm / MillimetresPerInch);

        // The source was larger than 60 mm at 300 PPI, so this is above the production
        // resolution and the row must not suggest otherwise.
        expectedX.ShouldBeGreaterThan(300);

        string value = Value(review.Screen, "EffectiveDpi");
        value.ShouldContain(Math.Round(expectedX).ToString("0", CultureInfo.CurrentCulture));
        value.ShouldContain(Math.Round(expectedY).ToString("0", CultureInfo.CurrentCulture));
        value.ShouldContain("1200");
        value.ShouldContain("900");
        value.ShouldNotContain("below");

        // The output's own resolution is a separate row and says 300 (§19).
        Value(review.Screen, "Resolution").ShouldContain("300");

        // Nothing was enlarged, so no enlargement row exists to mislead anyone (§22, §25).
        review.Screen.TiffMetadata.ShouldNotContain(row => row.Key == "Enlargement");
    }

    /// <summary>
    /// An authorised enlargement reports the factual effective resolution and says the authority
    /// was given — without implying the number is therefore acceptable (§21, §22).
    /// </summary>
    [Fact]
    public async Task An_authorised_enlargement_reports_the_factual_resolution_and_the_authority()
    {
        using EnglishUiScope culture = new();
        using HomeScreenHarness harness = Harness(out _);
        Review review = await EnlargedReviewRequiredAsync(harness, "enlarged.png");

        SessionAggregate persisted = await review.ReloadAsync();
        Revision tiff = persisted.Revisions.Last(r => r.Operation == OperationKind.PhotoshopOutput);
        PhotoshopPreparation preparation = persisted.Attempts
            .Single(a => a.OutputRevisionId == tiff.Id).Preparation!;

        preparation.ShouldBeOfType<TargetEdgePreparation>()
            .Plan.RequiresEnlargementAuthority.ShouldBeTrue();

        double widthMm = preparation.ProjectedPixelWidth * MillimetresPerInch / preparation.ProductionDpi;
        double expectedX = preparation.SourcePixelWidth / (widthMm / MillimetresPerInch);
        expectedX.ShouldBeLessThan(300);

        string effective = Value(review.Screen, "EffectiveDpi");
        effective.ShouldContain(Math.Round(expectedX).ToString("0", CultureInfo.CurrentCulture));
        effective.ShouldContain("below");
        effective.ShouldContain("300");

        // Stated as an authority given, never as a quality verdict, and with no invented band
        // (§21). Matched as whole words, so the "red" inside "required" is not a false positive.
        string enlargement = Value(review.Screen, "Enlargement");
        enlargement.ShouldContain("authorised", Case.Insensitive);

        Regex bands = new(
            @"\b(green|yellow|amber|red|acceptable|unacceptable|good|poor|ok)\b",
            RegexOptions.IgnoreCase, TimeSpan.FromSeconds(5));

        bands.IsMatch(enlargement).ShouldBeFalse(enlargement);
        bands.IsMatch(effective).ShouldBeFalse(effective);
    }

    /// <summary>
    /// The metric follows the Revision the preparation was actually bound to, whichever upstream
    /// route produced it (§20, §46).
    /// </summary>
    /// <remarks>
    /// Three routes through Prepare Customer Design leave three different canvases in front of
    /// Print Dimensions: an automatic trim crops to the artwork, Keep Original Extent keeps the
    /// full canvas, and a manual crop takes the operator's own rectangle. If effective resolution
    /// were computed from the imported file, all three would report the same number — so the
    /// distinctness assertion at the end is the real content of this test.
    /// </remarks>
    [Theory]
    [InlineData(UpstreamRoute.AutomaticTrim)]
    [InlineData(UpstreamRoute.KeepOriginalExtent)]
    [InlineData(UpstreamRoute.ManualCrop)]
    public async Task Effective_source_resolution_follows_the_bound_upstream_Revision(UpstreamRoute route)
    {
        using HomeScreenHarness harness = Harness(out _);
        Review review = await CustomerDesignReviewRequiredAsync(harness, route);

        SessionAggregate persisted = await review.ReloadAsync();
        Revision tiff = persisted.Revisions.Last(r => r.Operation == OperationKind.PhotoshopOutput);
        PhotoshopPreparation preparation = persisted.Attempts
            .Single(a => a.OutputRevisionId == tiff.Id).Preparation!;

        // The plan is bound to the Revision that was current at Print Dimensions, and its pixel
        // figures are that Revision's own — not the import's.
        Revision bound = persisted.Revisions.Single(r => r.Id == preparation.SourceRevisionId);
        bound.Facts.PixelWidth.ShouldBe(preparation.SourcePixelWidth);
        bound.Facts.PixelHeight.ShouldBe(preparation.SourcePixelHeight);
        bound.Sha256.ShouldBe(preparation.SourceSha256);

        double widthMm = preparation.ProjectedPixelWidth * MillimetresPerInch / preparation.ProductionDpi;
        double expected = preparation.SourcePixelWidth / (widthMm / MillimetresPerInch);

        Value(review.Screen, "EffectiveDpi")
            .ShouldContain(Math.Round(expected).ToString("0", CultureInfo.CurrentCulture));
        Value(review.Screen, "EffectiveDpi")
            .ShouldContain(preparation.SourcePixelWidth.ToString(CultureInfo.CurrentCulture));

        // Keep Original Extent alone keeps the full imported canvas; both crops reduce it.
        Revision imported = persisted.Revisions.First(r => r.Operation == OperationKind.Import);
        if (route == UpstreamRoute.KeepOriginalExtent)
        {
            preparation.SourcePixelWidth.ShouldBe(imported.Facts.PixelWidth!.Value);
        }
        else
        {
            preparation.SourcePixelWidth.ShouldBeLessThan(imported.Facts.PixelWidth!.Value);
        }
    }

    // -----------------------------------------------------------------------------------
    // §17, §39, §40, §54 — integrity and restart
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// A TIFF replaced after review began shows no preview and cannot be approved (§17, §40).
    /// </summary>
    /// <remarks>
    /// The replacement is itself a perfectly valid production TIFF, so nothing structural refuses
    /// it. What refuses it is that its bytes are not the reviewed bytes — which is exactly the
    /// case a preview that silently refreshed would hide, leaving an operator approving one file
    /// having looked at another.
    /// </remarks>
    [Fact]
    public async Task A_TIFF_replaced_after_validation_shows_no_preview_and_cannot_be_approved()
    {
        using HomeScreenHarness harness = Harness(out _);
        Review review = await ReviewRequiredAsync(harness, "mutated.png");

        SessionAggregate persisted = await review.ReloadAsync();
        PrintOutput output = persisted.Outputs.ShouldHaveSingleItem();
        string path = harness.Inner.FileWorkspace.ResolveAbsolute(output.File);

        review.Screen.HasTiffReview.ShouldBeTrue();

        ProductionTiffFixture.WriteAt(path, new ProductionTiffFixtureOptions(
            PixelWidth: 40, PixelHeight: 30, W1VerticalBands: [0, 0, 0]));
        HashOf(path).ShouldNotBe(output.Sha256.Value);

        // Reopening is what a restart or a refresh does; the payload must not come back.
        SessionViewModel reopened = harness.Session(new RecordingNavigation());
        reopened.Open((await harness.Sessions.LoadAsync(review.Id, CancellationToken.None)).Value);
        await reopened.PreviewsLoaded;

        reopened.HasTiffReview.ShouldBeFalse();
        reopened.IsTiffReviewUnavailable.ShouldBeTrue();
        reopened.TiffMetadata.ShouldBeEmpty();
        reopened.TiffOutputPath.ShouldBeNull();

        // The workflow's own integrity contract is untouched: the decision is still refused.
        await reopened.ApproveCommand.ExecuteAsync(null);
        reopened.Notice.ShouldNotBeNullOrWhiteSpace();

        SessionAggregate after = await review.ReloadAsync();
        after.Reviews.ShouldNotContain(r => r.Step == StepKind.PhotoshopOutput);
        after.Outputs.ShouldHaveSingleItem().ReviewState.ShouldBe(ReviewState.NotReviewed);
    }

    /// <summary>
    /// A restart rebuilds the whole review from the TIFF on disk, with no Photoshop run and no
    /// regeneration (§39, §54).
    /// </summary>
    [Fact]
    public async Task A_restart_rebuilds_the_review_without_another_Photoshop_run()
    {
        using HomeScreenHarness harness = Harness(out SyntheticProductionTiffProcessor photoshop);
        Review review = await ReviewRequiredAsync(harness, "restart.png");

        SessionAggregate before = await review.ReloadAsync();
        PrintOutput output = before.Outputs.ShouldHaveSingleItem();
        string path = harness.Inner.FileWorkspace.ResolveAbsolute(output.File);
        byte[] bytes = await File.ReadAllBytesAsync(path);
        string[] metadata = [.. review.Screen.TiffMetadata.Select(row => row.Key + "=" + row.Value)];
        byte[] whiteInk = review.Screen.TiffWhiteInkPayload.ToArray();
        photoshop.GenerateCount.ShouldBe(1);

        RestartedSession restarted = harness.RestartSession(new RecordingNavigation());
        restarted.Screen.Open((await restarted.Sessions.LoadAsync(review.Id, CancellationToken.None)).Value);
        await restarted.Screen.PreviewsLoaded;

        restarted.Screen.IsProductionTiffReview.ShouldBeTrue();
        restarted.Screen.HasTiffReview.ShouldBeTrue();
        restarted.Screen.TiffReviewPrintOutputId.ShouldBe(output.Id.Value.ToString());
        restarted.Screen.TiffReviewSha256.ShouldBe(output.Sha256.Value);
        restarted.Screen.TiffWhiteInkPayload.ToArray().ShouldBe(whiteInk);
        restarted.Screen.TiffMetadata
            .Select(row => row.Key + "=" + row.Value).ShouldBe(metadata);

        // Every mode still works, and nothing was produced to make that true.
        restarted.Screen.TiffReviewMode = TiffReviewMode.Overlay;
        restarted.Screen.TiffOverlayPayload.ToArray().ShouldNotBeEmpty();

        photoshop.GenerateCount.ShouldBe(1, "reopening a review must never rerun Photoshop");
        (await File.ReadAllBytesAsync(path)).ShouldBe(bytes, "and must never regenerate the TIFF");
    }

    // -----------------------------------------------------------------------------------
    // §37, §38, §47 — rejection, and several sizes at once
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Using the new modes does not interfere with rejection or its Recycle Bin disposal (§37).
    /// </summary>
    [Fact]
    public async Task Rejecting_after_using_the_modes_still_recycles_the_TIFF()
    {
        using HomeScreenHarness harness = Harness(out _);
        Review review = await ReviewRequiredAsync(harness, "reject-modes.png");
        SessionViewModel screen = review.Screen;

        SessionAggregate before = await review.ReloadAsync();
        string path = harness.Inner.FileWorkspace.ResolveAbsolute(
            before.Revisions.Last(r => r.Operation == OperationKind.PhotoshopOutput).File);

        screen.TiffReviewMode = TiffReviewMode.WhiteInk;
        screen.TiffReviewMode = TiffReviewMode.Overlay;

        screen.SelectedRejectionReason = screen.RejectionReasons
            .Single(choice => choice.Reason == RejectionReason.WhiteInkIssue);
        await screen.RejectCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        harness.Inner.RecycleBin.Recycled.ShouldBe([path]);
        File.Exists(path).ShouldBeFalse();

        // And the specialist surface goes with the bytes rather than lingering over a gap.
        screen.HasTiffReview.ShouldBeFalse();
        screen.TiffMetadata.ShouldBeEmpty();

        SessionAggregate after = await review.ReloadAsync();
        after.Reviews.Single(r => r.Step == StepKind.PhotoshopOutput).IsApproved.ShouldBeFalse();
        after.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State.ShouldBe(StepState.RetryRequired);
    }

    /// <summary>
    /// A second size is reviewed as itself: its own payload, its own path, its own hash
    /// (§38, §47).
    /// </summary>
    [Fact]
    public async Task Each_output_size_is_reviewed_against_its_own_file()
    {
        using HomeScreenHarness harness = Harness(out SyntheticProductionTiffProcessor photoshop);
        Review review = await ReviewRequiredAsync(
            harness, "sizes.png", sourceWidth: 1200, sourceHeight: 900,
            maxWidthMm: 60, maxHeightMm: 60);
        SessionViewModel screen = review.Screen;

        string firstPath = screen.TiffOutputPath.ShouldNotBeNull();
        string firstHash = screen.TiffReviewSha256.ShouldNotBeNull();
        byte[] firstWhite = screen.TiffWhiteInkPayload.ToArray();

        await screen.ApproveCommand.ExecuteAsync(null);
        await screen.CompleteCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        // A second size, deliberately different in both geometry and white-ink pattern.
        photoshop.Bands = [0, 255, 0];
        await screen.AddAnotherSizeCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        screen.WidthMmText = 40d.ToString(CultureInfo.CurrentCulture);
        screen.HeightMmText = 40d.ToString(CultureInfo.CurrentCulture);
        await screen.SetMaximumBoundsCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        screen.SelectedWhiteUnderbaseChoice = screen.WhiteUnderbaseChoices
            .Single(choice => choice.Branch == WhiteUnderbaseBranch.W1_2px);
        await screen.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        await screen.RunStepCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();
        await screen.PreviewsLoaded;

        screen.IsReviewRequired.ShouldBeTrue();
        screen.HasTiffReview.ShouldBeTrue();

        string secondPath = screen.TiffOutputPath.ShouldNotBeNull();
        secondPath.ShouldNotBe(firstPath, "each size has its own managed file");
        screen.TiffReviewSha256.ShouldNotBe(firstHash);
        screen.TiffWhiteInkPayload.ToArray().ShouldNotBe(firstWhite);

        // Both outputs exist, and the screen is showing the one under review.
        SessionAggregate persisted = await review.ReloadAsync();
        persisted.Outputs.Count.ShouldBe(2);

        PrintOutput second = persisted.Outputs.Single(o => o.ReviewState == ReviewState.NotReviewed);
        screen.TiffReviewPrintOutputId.ShouldBe(second.Id.Value.ToString());
        secondPath.ShouldBe(harness.Inner.FileWorkspace.ResolveAbsolute(second.File));
        HashOf(secondPath).ShouldBe(second.Sha256.Value);

        // The first size's approved deliverable is untouched by any of it.
        PrintOutput first = persisted.Outputs.Single(o => o.ReviewState == ReviewState.Approved);
        first.Sha256.Value.ShouldBe(firstHash);
    }

    // -----------------------------------------------------------------------------------
    // §32 — the operator's own language
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Every new operator-facing concept has wording in both supported languages, and the two
    /// differ (§32).
    /// </summary>
    /// <remarks>
    /// The culture is pinned explicitly rather than inherited from the workstation, and the
    /// English/Chinese comparison is what catches a zh-CN entry that was copied rather than
    /// translated.
    /// </remarks>
    [Fact]
    public async Task The_new_review_wording_exists_in_both_operator_languages()
    {
        using HomeScreenHarness harness = Harness(out _);
        Review review = await ReviewRequiredAsync(harness, "localisation.png");

        (string[] labels, string[] modes, string legend) english = InCulture("en-US", review.Screen);
        (string[] labels, string[] modes, string legend) chinese = InCulture("zh-CN", review.Screen);

        foreach (string[] set in new[] { english.labels, english.modes, chinese.labels, chinese.modes })
        {
            set.ShouldAllBe(value => !string.IsNullOrWhiteSpace(value));
            set.ShouldNotContain(value => value.StartsWith("Session_", StringComparison.Ordinal),
                "a resource key on screen means a missing translation");
        }

        english.modes.ShouldBe(["Colour", "White ink", "Colour + white overlay"]);
        chinese.modes.ShouldNotBe(english.modes, "zh-CN wording must be translated, not copied");
        chinese.legend.ShouldNotBe(english.legend);
        english.labels.ShouldNotBe(chinese.labels);

        static (string[] Labels, string[] Modes, string Legend) InCulture(string culture, SessionViewModel screen)
        {
            CultureInfo previous = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                return (
                    [screen.TiffProductionHeading, screen.TiffOutputPathLabel, screen.TiffReviewModeHeading],
                    [screen.TiffColourModeLabel, screen.TiffWhiteInkModeLabel, screen.TiffOverlayModeLabel],
                    screen.TiffPreviewLegend);
            }
            finally
            {
                CultureInfo.CurrentUICulture = previous;
            }
        }
    }

    // -----------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------

    private sealed class EnglishUiScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;
        public EnglishUiScope() => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        public void Dispose() => CultureInfo.CurrentUICulture = _previous;
    }

    private static string Value(SessionViewModel screen, string key) =>
        screen.TiffMetadata.Single(row => row.Key == key).Value;

    private static byte Grey(byte[] bgra, int width, int x, int y)
    {
        int index = ((y * width) + x) * 4;
        return bgra[index + 2];
    }

    private static string HashOf(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Sha256.FromBytes(SHA256.HashData(stream)).Value;
    }
}
