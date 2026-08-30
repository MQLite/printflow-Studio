using System.Globalization;
using System.IO;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Engine;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// The session screen's production controls — print dimensions, the W1 branch, fake Photoshop
/// output, Complete, Add Another Size and the output list — driven against the real session
/// service, workspace, fake adapters and SQLite database (Epic 11100 Part 3C3B §17–§20).
/// </summary>
/// <remarks>
/// As in Part 3C3A, every assertion about an outcome reads what was actually <b>persisted</b>,
/// through the repository rather than through the view model's own properties: a screen that
/// says "200 mm" while the database holds something else is exactly the defect these exist to
/// catch.
/// <para>
/// The sibling-invalidation rules themselves are already covered end to end at the service
/// level by <c>AddAnotherSizeTests</c> and are not repeated here. What is new in this slice is
/// that the controls reach those rules correctly and that the operator can see both outputs, so
/// that is what these cover.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class DimensionsW1AndOutputTests
{
    // -------------------------------------------------------------------------------------
    // §3–§5: print dimensions
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Entered_dimensions_are_persisted_through_the_command_path()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await AtDimensionsAsync(harness, "dimensions.png");
        SessionViewModel screen = open.Screen;

        screen.CanSetMaximumBounds.ShouldBeTrue();
        screen.ConfirmedMaximumBounds.ShouldBe(Strings.DimensionsNotSet);

        screen.WidthMmText = "200";
        screen.HeightMmText = "150";

        // The preview states the limits and nothing else. Under the maximum-bound contract the
        // independent millimetre-to-pixel conversion is not what the image would become, so a
        // pixel figure here would be a number the plan never uses (Epic 11400 Part B1A.2B §7).
        screen.PendingMaximumBounds.ShouldContain("200");
        screen.PendingMaximumBounds.ShouldContain("150");
        screen.PendingMaximumBounds.ShouldNotContain("2362");
        screen.PendingMaximumBounds.ShouldNotContain("1772");

        await screen.SetMaximumBoundsCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        SessionAggregate persisted = await open.ReloadAsync();
        PrintDimensions stored = persisted.Session.Dimensions.ShouldNotBeNull();
        stored.WidthMm.ShouldBe(200);
        stored.HeightMm.ShouldBe(150);
        stored.Dpi.ShouldBe(PrintDimensions.ProductionDpi);
        stored.PixelWidth.ShouldBe(2362);
        stored.PixelHeight.ShouldBe(1772);

        // The step it belongs to is confirmed, and the session has moved on to Photoshop.
        StepOf(persisted, StepKind.PrintDimensions).State.ShouldBe(StepState.Approved);
        persisted.ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.PhotoshopOutput);

        screen.CanSetMaximumBounds.ShouldBeFalse();
        screen.ConfirmedMaximumBounds.ShouldContain("200");
    }

    /// <summary>
    /// A size the domain will not make is refused, and nothing is persisted or adjusted
    /// (Part 3C3B §4).
    /// </summary>
    /// <remarks>
    /// The screen must not round a zero up, substitute a preset, or invent a size that would
    /// have been acceptable. The check that the session still holds no dimensions afterwards
    /// is what says so.
    /// </remarks>
    [Theory]
    [InlineData("0", "150")]
    [InlineData("200", "0")]
    [InlineData("-5", "150")]
    [InlineData("not a number", "150")]
    [InlineData("", "150")]
    [InlineData("200", "")]
    public async Task Invalid_dimensions_are_refused_and_nothing_is_persisted(string width, string height)
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await AtDimensionsAsync(harness, "invalid.png");
        SessionViewModel screen = open.Screen;

        screen.WidthMmText = width;
        screen.HeightMmText = height;
        screen.PendingMaximumBounds.ShouldBeEmpty();

        await screen.SetMaximumBoundsCommand.ExecuteAsync(null);

        screen.Notice.ShouldNotBeNullOrWhiteSpace();

        SessionAggregate persisted = await open.ReloadAsync();
        persisted.Session.Dimensions.ShouldBeNull();
        StepOf(persisted, StepKind.PrintDimensions).State.ShouldBe(StepState.Waiting);
        screen.CanSetMaximumBounds.ShouldBeTrue();
    }

    /// <summary>
    /// A preset shows the <b>configured</b> limits and nothing more; confirming is still a
    /// separate act (Part 3C3B §5; Epic 11400 Part B1A.2D §3, §4).
    /// </summary>
    /// <remarks>
    /// The millimetres are the point of this test now. A4 shows 280 mm, which is what the verified
    /// preset configures as its maximum long edge — not 210 × 297, the ISO page the preset is
    /// <i>named after</i>. A build that read the nominal size as the production limit would print
    /// every A4 job 17 mm too long on the long edge and call it configured behaviour.
    /// </remarks>
    [Fact]
    public async Task A_preset_shows_its_configured_limits_and_the_operator_can_still_change_them()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await AtDimensionsAsync(harness, "preset.png");
        SessionViewModel screen = open.Screen;

        screen.SizePresets.Select(choice => choice.Preset).ShouldBe(
            [SizePreset.A3Landscape, SizePreset.A3Portrait, SizePreset.A4, SizePreset.A5]);

        SizePresetChoice a4 = screen.SizePresets.Single(choice => choice.Preset == SizePreset.A4);
        a4.Recommendation.Kind.ShouldBe(PresetRecommendationKind.MaximumLongEdge);
        a4.Recommendation.MaxLongEdgeMm.ShouldBe(280m);
        screen.ApplyPresetCommand.Execute(a4);

        // The configured recommendation, never PrintDimensions.NominalMillimetres.
        screen.WidthMmText.ShouldBe(280d.ToString(CultureInfo.CurrentCulture));
        screen.HeightMmText.ShouldBe(280d.ToString(CultureInfo.CurrentCulture));
        (await open.ReloadAsync()).Session.Dimensions.ShouldBeNull();

        await screen.SetMaximumBoundsCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        PrintDimensions stored = (await open.ReloadAsync()).Session.Dimensions.ShouldNotBeNull();
        stored.WidthMm.ShouldBe(280);
        stored.HeightMm.ShouldBe(280);
        stored.Preset.ShouldBe(SizePreset.A4);
    }

    /// <summary>
    /// Typing over a preset records the size as Custom, not as the preset it started from.
    /// </summary>
    [Fact]
    public async Task Editing_a_preset_size_records_it_as_custom()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await AtDimensionsAsync(harness, "edited-preset.png");
        SessionViewModel screen = open.Screen;

        screen.ApplyPresetCommand.Execute(screen.SizePresets.Single(c => c.Preset == SizePreset.A5));
        screen.WidthMmText = "120";

        await screen.SetMaximumBoundsCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        PrintDimensions stored = (await open.ReloadAsync()).Session.Dimensions.ShouldNotBeNull();
        stored.WidthMm.ShouldBe(120);
        stored.Preset.ShouldBe(SizePreset.Custom);
    }

    // -------------------------------------------------------------------------------------
    // §6–§7: the white-underbase branch
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Nothing is pre-selected, so no session can acquire a branch it was not given
    /// (Part 3C3B §6; MVP design §12).
    /// </summary>
    [Fact]
    public async Task The_white_underbase_selector_offers_all_three_branches_with_none_chosen()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await AtDimensionsAsync(harness, "w1-default.png");
        SessionViewModel screen = open.Screen;

        screen.CanSelectWhiteUnderbase.ShouldBeTrue();
        screen.WhiteUnderbaseChoices.Select(choice => choice.Branch)
            .ShouldBe(Enum.GetValues<WhiteUnderbaseBranch>());
        screen.WhiteUnderbaseChoices.ShouldAllBe(choice => !string.IsNullOrWhiteSpace(choice.Label));

        // The point of the slice: no starting selection, and therefore nothing to confirm.
        screen.SelectedWhiteUnderbaseChoice.ShouldBeNull();
        screen.CanConfirmWhiteUnderbase.ShouldBeFalse();
        screen.ConfirmedWhiteUnderbase.ShouldBe(Strings.W1NotChosen);

        // Pressing Confirm with nothing selected persists nothing rather than falling back.
        await screen.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
        (await open.ReloadAsync()).Session.WhiteUnderbaseBranch.ShouldBeNull();
    }

    [Theory]
    [InlineData(WhiteUnderbaseBranch.W1_0px)]
    [InlineData(WhiteUnderbaseBranch.W1_1px)]
    [InlineData(WhiteUnderbaseBranch.W1_2px)]
    public async Task Each_white_underbase_branch_persists_exactly_as_chosen(WhiteUnderbaseBranch branch)
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await AtDimensionsAsync(harness, $"w1-{branch}.png");
        SessionViewModel screen = open.Screen;

        screen.SelectedWhiteUnderbaseChoice =
            screen.WhiteUnderbaseChoices.Single(choice => choice.Branch == branch);
        screen.CanConfirmWhiteUnderbase.ShouldBeTrue();

        await screen.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        (await open.ReloadAsync()).Session.WhiteUnderbaseBranch.ShouldBe(branch);
        screen.ConfirmedWhiteUnderbase.ShouldNotBe(Strings.W1NotChosen);
    }

    // -------------------------------------------------------------------------------------
    // §9: fake Photoshop output
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Photoshop output is not offered until both production decisions exist
    /// (Part 3C3B §9; MVP design §12).
    /// </summary>
    [Fact]
    public async Task Run_step_is_withheld_until_dimensions_and_the_branch_are_both_recorded()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await AtDimensionsAsync(harness, "gating.png");
        SessionViewModel screen = open.Screen;

        // On PrintDimensions, which produces no Revision: there is nothing to run.
        screen.CanRunStep.ShouldBeFalse();

        screen.SelectedWhiteUnderbaseChoice =
            screen.WhiteUnderbaseChoices.Single(choice => choice.Branch == WhiteUnderbaseBranch.W1_1px);
        await screen.SelectWhiteUnderbaseCommand.ExecuteAsync(null);

        // A branch alone is not enough: the session is still waiting for a size.
        screen.CanRunStep.ShouldBeFalse();
        screen.CanSetMaximumBounds.ShouldBeTrue();

        screen.WidthMmText = "200";
        screen.HeightMmText = "150";
        await screen.SetMaximumBoundsCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        // Both recorded: now, and only now, the step may start.
        (await open.ReloadAsync()).ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.PhotoshopOutput);
        screen.CanRunStep.ShouldBeTrue();
    }

    /// <summary>
    /// The branch on its own leaves Photoshop output unstartable even when the step is current
    /// (Part 3C3B §9).
    /// </summary>
    /// <remarks>
    /// The test above stops before the step becomes current. This one reaches PhotoshopOutput
    /// with a size but no branch, which is the state the "there is no default" rule actually
    /// has to hold in, and asks the engine directly.
    /// </remarks>
    [Fact]
    public async Task Photoshop_output_stays_unavailable_when_only_the_size_was_recorded()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await AtDimensionsAsync(harness, "size-only.png");
        SessionViewModel screen = open.Screen;

        screen.WidthMmText = "200";
        screen.HeightMmText = "150";
        await screen.SetMaximumBoundsCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        SessionAggregate persisted = await open.ReloadAsync();
        persisted.ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.PhotoshopOutput);
        persisted.Session.WhiteUnderbaseBranch.ShouldBeNull();

        screen.CanRunStep.ShouldBeFalse();
        WorkflowEngine.Instance.AvailableCommands(persisted.ToSnapshot())
            .ShouldNotContain(CommandKind.StartStep);
    }

    [Fact]
    public async Task Running_photoshop_output_reaches_review_required_through_the_real_pipeline()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await ReadyForPhotoshopAsync(harness, "photoshop.png");

        open.Screen.CanRunStep.ShouldBeTrue();
        await open.Screen.RunStepCommand.ExecuteAsync(null);
        open.Screen.Notice.ShouldBeNull();

        SessionAggregate persisted = await open.ReloadAsync();
        StepOf(persisted, StepKind.PhotoshopOutput).State.ShouldBe(StepState.ReviewRequired);
        open.Screen.IsReviewRequired.ShouldBeTrue();

        // A real attempt, a real file on disk, and a hash that could only come from reading it.
        ProcessingAttempt attempt = persisted.Attempts.Single(a => a.Step == StepKind.PhotoshopOutput);
        attempt.Status.ShouldBe(AttemptStatus.Succeeded);
        attempt.AdapterId.ShouldBe(harness.Inner.FakePhotoshop.AdapterId);

        PrintOutput output = persisted.Outputs.ShouldHaveSingleItem();
        output.ReviewState.ShouldBe(ReviewState.NotReviewed);
        output.Dimensions.WidthMm.ShouldBe(200);
        output.Branch.ShouldBe(WhiteUnderbaseBranch.W1_1px);
        File.Exists(harness.ResolveInWorkspace(output.File.RelativePath)).ShouldBeTrue();

        // The warning that this TIFF is not production-ready is on screen (§10).
        open.Screen.IsFakeTiffOutput.ShouldBeTrue();
        open.Screen.FakeTiffNotice.ShouldNotBeNullOrWhiteSpace();
    }

    // -------------------------------------------------------------------------------------
    // §11–§12: review and Complete
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// The full GENERATE_PRINT_TIFF journey through the screen, with nothing else touched
    /// (Part 3C3B §17).
    /// </summary>
    /// <remarks>
    /// The negative assertions carry as much weight as the positive ones. This workflow has no
    /// Meitu and no Trim step, so a session that somehow reached either would mean the screen
    /// or the definition had drifted — and the Meitu port is counted at the seam rather than
    /// inferred from the absence of a row.
    /// </remarks>
    [Fact]
    public async Task Generate_print_tiff_runs_from_import_to_completed_without_meitu_or_trim()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await ReadyForPhotoshopAsync(harness, "tiff-flow.png");
        SessionViewModel screen = open.Screen;

        await screen.RunStepCommand.ExecuteAsync(null);
        screen.IsReviewRequired.ShouldBeTrue();

        // Complete is not on offer while the produced TIFF is still awaiting review (§12).
        screen.CanComplete.ShouldBeFalse();

        await screen.ApproveCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        screen.CanComplete.ShouldBeTrue();
        await screen.CompleteCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        SessionAggregate persisted = await open.ReloadAsync();
        persisted.Session.State.ShouldBe(SessionState.Completed);
        StepOf(persisted, StepKind.PhotoshopOutput).State.ShouldBe(StepState.Approved);

        PrintOutput output = persisted.Outputs.ShouldHaveSingleItem();
        output.ReviewState.ShouldBe(ReviewState.Approved);
        output.IsValid.ShouldBeTrue();

        // Approval bound to the exact bytes reviewed, as for any other artefact (§11).
        ReviewDecision decision = persisted.Reviews.Single(r => r.Step == StepKind.PhotoshopOutput);
        decision.IsApproved.ShouldBeTrue();
        decision.ReviewedSha256.ShouldBe(output.Sha256);

        // No Meitu, no Trim — neither as an attempt nor as a call.
        harness.Meitu.CallCount.ShouldBe(0);
        persisted.Attempts.ShouldAllBe(a =>
            a.Step != StepKind.Enhancement && a.Step != StepKind.BackgroundRemoval && a.Step != StepKind.Trim);
        persisted.Steps.ShouldNotContain(s => s.Step == StepKind.Trim);
    }

    /// <summary>
    /// Complete is not offered while a required step is unfinished (Part 3C3B §12).
    /// </summary>
    [Fact]
    public async Task Complete_is_unavailable_while_any_required_step_remains()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await TiffSessionAsync(harness, "too-early.png");
        SessionViewModel screen = open.Screen;

        screen.CanComplete.ShouldBeFalse();                     // OriginalConfirmation

        await screen.ConfirmOriginalCommand.ExecuteAsync(null);
        screen.CanComplete.ShouldBeFalse();                     // PrintDimensions

        screen.WidthMmText = "200";
        screen.HeightMmText = "150";
        await screen.SetMaximumBoundsCommand.ExecuteAsync(null);
        screen.CanComplete.ShouldBeFalse();                     // PhotoshopOutput, not started

        // Asking anyway is refused by the engine, and nothing completes.
        await screen.CompleteCommand.ExecuteAsync(null);
        (await open.ReloadAsync()).Session.State.ShouldBe(SessionState.Active);
    }

    /// <summary>
    /// PREPARE_ASSET finishes through its own terminal step, with no size and no branch
    /// (Part 3C3B §13).
    /// </summary>
    [Fact]
    public async Task Prepare_asset_completes_through_approved_png_export_without_dimensions_or_a_branch()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await OpenAsync(harness, "asset-complete.png");
        SessionViewModel screen = open.Screen;

        // Neither production decision is ever offered: this workflow produces no TIFF.
        screen.CanSetMaximumBounds.ShouldBeFalse();
        screen.CanSelectWhiteUnderbase.ShouldBeFalse();
        screen.IsFakeTiffOutput.ShouldBeFalse();

        await screen.ConfirmOriginalCommand.ExecuteAsync(null);
        await screen.SkipCommand.ExecuteAsync(null);            // Enhancement
        await screen.SkipCommand.ExecuteAsync(null);            // BackgroundRemoval

        await screen.RunStepCommand.ExecuteAsync(null);         // Trim
        await screen.ApproveCommand.ExecuteAsync(null);

        // ApprovedPngExport promotes the approved bytes and needs no second review.
        screen.CanRunStep.ShouldBeTrue();
        await screen.RunStepCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        screen.CanComplete.ShouldBeTrue();
        await screen.CompleteCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        SessionAggregate persisted = await open.ReloadAsync();
        persisted.Session.State.ShouldBe(SessionState.Completed);
        StepOf(persisted, StepKind.ApprovedPngExport).State.ShouldBe(StepState.Approved);
        persisted.Session.Dimensions.ShouldBeNull();
        persisted.Session.WhiteUnderbaseBranch.ShouldBeNull();
        persisted.Outputs.ShouldBeEmpty();

        // A completed PNG workflow has no additional size to add.
        screen.CanAddAnotherSize.ShouldBeFalse();
    }

    /// <summary>
    /// PREPARE_CUSTOMER_DESIGN's production tail, from an approved Trim onwards
    /// (Part 3C3B §18).
    /// </summary>
    /// <remarks>
    /// The earlier Session-control behaviour of this workflow is Part 3C3A's ground and is not
    /// walked again; what matters here is that the same production controls appear and work
    /// when the size and branch follow a real upstream chain rather than the import root.
    /// </remarks>
    [Fact]
    public async Task Prepare_customer_design_finishes_through_dimensions_w1_and_photoshop_output()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await OpenAsync(
            harness, "customer-design.png", WorkflowType.PrepareCustomerDesign);
        SessionViewModel screen = open.Screen;

        await screen.ConfirmOriginalCommand.ExecuteAsync(null);
        await screen.SkipCommand.ExecuteAsync(null);            // Enhancement
        await screen.SkipCommand.ExecuteAsync(null);            // BackgroundRemoval

        await screen.RunStepCommand.ExecuteAsync(null);         // Trim
        await screen.ApproveCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        SessionAggregate atDimensions = await open.ReloadAsync();
        atDimensions.ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.PrintDimensions);
        RevisionId trimmed = StepOf(atDimensions, StepKind.Trim).CurrentRevisionId.ShouldNotBeNull();

        await ConfirmProductionDecisionsAsync(screen, widthMm: 240);
        await screen.RunStepCommand.ExecuteAsync(null);
        await screen.ApproveCommand.ExecuteAsync(null);
        await screen.CompleteCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        SessionAggregate persisted = await open.ReloadAsync();
        persisted.Session.State.ShouldBe(SessionState.Completed);

        PrintOutput output = persisted.Outputs.ShouldHaveSingleItem();
        output.ReviewState.ShouldBe(ReviewState.Approved);
        output.Dimensions.WidthMm.ShouldBe(240);

        // The TIFF was produced from the approved trim, not from the imported original.
        output.SourceRevisionId.ShouldBe(trimmed);
    }

    // -------------------------------------------------------------------------------------
    // §14–§16, §19: Add Another Size and the output list
    // -------------------------------------------------------------------------------------

    [Fact]
    public async Task Add_another_size_is_offered_only_once_a_production_session_is_completed()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await ReadyForPhotoshopAsync(harness, "reopen.png");
        SessionViewModel screen = open.Screen;

        screen.CanAddAnotherSize.ShouldBeFalse();

        await screen.RunStepCommand.ExecuteAsync(null);
        screen.CanAddAnotherSize.ShouldBeFalse();               // awaiting review

        await screen.ApproveCommand.ExecuteAsync(null);
        screen.CanAddAnotherSize.ShouldBeFalse();               // approved, not completed

        await screen.CompleteCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();
        screen.CanAddAnotherSize.ShouldBeTrue();

        await screen.AddAnotherSizeCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        // Reopened at PrintDimensions, with both production decisions to be made again.
        SessionAggregate persisted = await open.ReloadAsync();
        persisted.Session.State.ShouldBe(SessionState.Active);
        persisted.ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.PrintDimensions);
        persisted.Session.Dimensions.ShouldBeNull();
        persisted.Session.WhiteUnderbaseBranch.ShouldBeNull();

        screen.CanSetMaximumBounds.ShouldBeTrue();
        screen.SelectedWhiteUnderbaseChoice.ShouldBeNull();
        screen.ConfirmedWhiteUnderbase.ShouldBe(Strings.W1NotChosen);

        // Output A is still there, and still shown, while B is being made (§15, §16).
        persisted.Outputs.ShouldHaveSingleItem().ReviewState.ShouldBe(ReviewState.Approved);
        screen.HasOutputs.ShouldBeTrue();
        screen.Outputs.ShouldHaveSingleItem();
    }

    /// <summary>
    /// Two sizes, made one after the other through the screen, both survive (Part 3C3B §19).
    /// </summary>
    [Fact]
    public async Task A_second_size_is_produced_alongside_the_first_and_both_are_displayed()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await CompletedFirstOutputAsync(harness, "two-sizes.png");
        SessionViewModel screen = open.Screen;

        await screen.AddAnotherSizeCommand.ExecuteAsync(null);
        await ConfirmProductionDecisionsAsync(screen, widthMm: 150, WhiteUnderbaseBranch.W1_2px);
        await screen.RunStepCommand.ExecuteAsync(null);
        await screen.ApproveCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        SessionAggregate persisted = await open.ReloadAsync();
        persisted.Outputs.Count.ShouldBe(2);

        PrintOutput outputA = persisted.Outputs.Single(o => o.Dimensions.WidthMm == 200);
        PrintOutput outputB = persisted.Outputs.Single(o => o.Dimensions.WidthMm == 150);

        outputA.IsValid.ShouldBeTrue();
        outputA.ReviewState.ShouldBe(ReviewState.Approved);
        outputB.IsValid.ShouldBeTrue();
        outputB.ReviewState.ShouldBe(ReviewState.Approved);

        // Siblings from the same approved source, and each with its own explicit branch.
        outputA.SourceRevisionId.ShouldBe(outputB.SourceRevisionId);
        outputA.Branch.ShouldBe(WhiteUnderbaseBranch.W1_1px);
        outputB.Branch.ShouldBe(WhiteUnderbaseBranch.W1_2px);

        screen.Outputs.Count.ShouldBe(2);
        screen.Outputs.ShouldAllBe(row => row.IsValid);
    }

    /// <summary>
    /// Rejecting the second size leaves the first exactly as it was (Part 3C3B §16, §19).
    /// </summary>
    [Fact]
    public async Task Rejecting_the_second_size_leaves_the_first_output_valid_and_approved()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await CompletedFirstOutputAsync(harness, "reject-b.png");
        SessionViewModel screen = open.Screen;

        await screen.AddAnotherSizeCommand.ExecuteAsync(null);
        await ConfirmProductionDecisionsAsync(screen, widthMm: 150);
        await screen.RunStepCommand.ExecuteAsync(null);

        screen.SelectedRejectionReason =
            screen.RejectionReasons.Single(choice => choice.Reason == RejectionReason.DimensionIssue);
        await screen.RejectCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        SessionAggregate persisted = await open.ReloadAsync();
        PrintOutput outputA = persisted.Outputs.Single(o => o.Dimensions.WidthMm == 200);
        PrintOutput outputB = persisted.Outputs.Single(o => o.Dimensions.WidthMm == 150);

        outputA.IsValid.ShouldBeTrue();
        outputA.ReviewState.ShouldBe(ReviewState.Approved);
        outputB.ReviewState.ShouldBe(ReviewState.Rejected);

        StepOf(persisted, StepKind.PhotoshopOutput).State.ShouldBe(StepState.RetryRequired);

        // The operator can still see A is intact while deciding what to do about B.
        screen.Outputs.Count.ShouldBe(2);
        PrintOutputRow rowA = screen.Outputs.Single(row => row.Size.Contains("200", StringComparison.Ordinal));
        rowA.Review.ShouldBe(Strings.ReviewApproved);
        rowA.Validity.ShouldBe(Strings.OutputValid);
        rowA.IsValid.ShouldBeTrue();
    }

    /// <summary>
    /// The output list carries the concise metadata §15 asks for, and no path.
    /// </summary>
    [Fact]
    public async Task The_output_list_shows_size_branch_review_validity_and_a_bare_file_name()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await CompletedFirstOutputAsync(harness, "output-list.png");

        PrintOutputRow row = open.Screen.Outputs.ShouldHaveSingleItem();

        row.Size.ShouldContain("200");
        row.Size.ShouldContain("150");
        row.Size.ShouldContain("2362");                       // the domain's 300 dpi conversion
        row.Branch.ShouldNotBeNullOrWhiteSpace();
        row.Review.ShouldBe(Strings.ReviewApproved);
        row.Validity.ShouldBe(Strings.OutputValid);
        row.IsValid.ShouldBeTrue();

        // A bare workspace file name: nothing that discloses where anything is stored (§15).
        row.FileName.ShouldNotBeNullOrWhiteSpace();
        row.FileName.ShouldNotContain(Path.DirectorySeparatorChar.ToString());
        row.FileName.ShouldNotContain("/");
        row.FileName.ShouldNotContain(":");

        PrintOutput persisted = (await open.ReloadAsync()).Outputs.ShouldHaveSingleItem();
        row.FileName.ShouldBe(persisted.File.FileName);
    }

    // -------------------------------------------------------------------------------------
    // §8: availability comes from the workflow layer
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Every production control the screen offers agrees with <c>AvailableCommands</c>, in each
    /// state a TIFF session passes through (Part 3C3B §8).
    /// </summary>
    [Fact]
    public async Task Production_control_availability_agrees_with_the_engine_at_every_stage()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await TiffSessionAsync(harness, "agreement.png");
        SessionViewModel screen = open.Screen;

        await AssertAgreesAsync(open);                          // OriginalConfirmation

        await screen.ConfirmOriginalCommand.ExecuteAsync(null);
        await AssertAgreesAsync(open);                          // PrintDimensions

        await ConfirmProductionDecisionsAsync(screen, widthMm: 200);
        await AssertAgreesAsync(open);                          // PhotoshopOutput / Waiting

        await screen.RunStepCommand.ExecuteAsync(null);
        await AssertAgreesAsync(open);                          // PhotoshopOutput / ReviewRequired

        await screen.ApproveCommand.ExecuteAsync(null);
        await AssertAgreesAsync(open);                          // all steps finished

        await screen.CompleteCommand.ExecuteAsync(null);
        await AssertAgreesAsync(open);                          // Completed

        await screen.AddAnotherSizeCommand.ExecuteAsync(null);
        await AssertAgreesAsync(open);                          // reopened at PrintDimensions
    }

    // -------------------------------------------------------------------------------------

    /// <summary>Localised text a test compares against, resolved the same way the screen does.</summary>
    private static class Strings
    {
        public static string DimensionsNotSet => Resolve("Session_DimensionsNotSet");

        public static string W1NotChosen => Resolve("Session_W1NotChosen");

        public static string ReviewApproved => Resolve("ReviewState_Approved");

        public static string OutputValid => Resolve("Session_OutputValid");

        private static string Resolve(string key) =>
            new System.Resources.ResourceManager(
                    "PrintFlow.App.Resources.Strings", typeof(SessionViewModel).Assembly)
                .GetString(key, CultureInfo.CurrentUICulture)
            ?? throw new InvalidOperationException($"No resource '{key}'.");
    }

    /// <summary>A session screen, plus the identity needed to read the same session back.</summary>
    private sealed record OpenSession(HomeScreenHarness Harness, SessionViewModel Screen, SessionId Id)
    {
        /// <summary>The session exactly as persistence currently holds it.</summary>
        public async Task<SessionAggregate> ReloadAsync() =>
            (await Harness.Inner.Repository.LoadAsync(Id, CancellationToken.None)).Value!;
    }

    /// <summary>
    /// Imports a synthetic file, chooses <paramref name="workflow"/> on the selection screen,
    /// and opens the session screen the way an operator reaches it.
    /// </summary>
    private static async Task<OpenSession> OpenAsync(
        HomeScreenHarness harness, string fileName, WorkflowType workflow = WorkflowType.PrepareAsset)
    {
        harness.FilePicker.Path = harness.WriteSourceFile(fileName);
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        harness.Home.Notice.ShouldBeNull();

        RecordingNavigation navigation = new();
        WorkflowSelectionViewModel selection = harness.WorkflowSelection(navigation);
        selection.Open(harness.Navigation.WorkflowSelectionFor!);
        await selection.SelectCommand.ExecuteAsync(
            selection.Workflows.Single(choice => choice.Type == workflow));
        selection.Notice.ShouldBeNull();

        SessionView chosen = navigation.SessionFor.ShouldNotBeNull();
        chosen.WorkflowType.ShouldBe(workflow);

        SessionViewModel screen = harness.Session(new RecordingNavigation());
        screen.Open(chosen);

        return new OpenSession(harness, screen, chosen.Id);
    }

    /// <summary>A GENERATE_PRINT_TIFF session, freshly imported.</summary>
    private static Task<OpenSession> TiffSessionAsync(HomeScreenHarness harness, string fileName) =>
        OpenAsync(harness, fileName, WorkflowType.GeneratePrintTiff);

    /// <summary>...confirmed, so the session is sitting on PrintDimensions.</summary>
    private static async Task<OpenSession> AtDimensionsAsync(HomeScreenHarness harness, string fileName)
    {
        OpenSession open = await TiffSessionAsync(harness, fileName);
        await open.Screen.ConfirmOriginalCommand.ExecuteAsync(null);
        open.Screen.Notice.ShouldBeNull();

        (await open.ReloadAsync()).ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.PrintDimensions);
        return open;
    }

    /// <summary>...with both production decisions made, so Photoshop output may start.</summary>
    private static async Task<OpenSession> ReadyForPhotoshopAsync(HomeScreenHarness harness, string fileName)
    {
        OpenSession open = await AtDimensionsAsync(harness, fileName);
        await ConfirmProductionDecisionsAsync(open.Screen, widthMm: 200);
        return open;
    }

    /// <summary>...and completed, holding one approved 200 mm output.</summary>
    private static async Task<OpenSession> CompletedFirstOutputAsync(
        HomeScreenHarness harness, string fileName)
    {
        OpenSession open = await ReadyForPhotoshopAsync(harness, fileName);

        await open.Screen.RunStepCommand.ExecuteAsync(null);
        await open.Screen.ApproveCommand.ExecuteAsync(null);
        await open.Screen.CompleteCommand.ExecuteAsync(null);
        open.Screen.Notice.ShouldBeNull();

        (await open.ReloadAsync()).Session.State.ShouldBe(SessionState.Completed);
        return open;
    }

    /// <summary>Confirms a size and a branch through the screen's own controls.</summary>
    private static async Task ConfirmProductionDecisionsAsync(
        SessionViewModel screen,
        double widthMm,
        WhiteUnderbaseBranch branch = WhiteUnderbaseBranch.W1_1px)
    {
        screen.WidthMmText = widthMm.ToString(CultureInfo.CurrentCulture);
        screen.HeightMmText = 150d.ToString(CultureInfo.CurrentCulture);
        await screen.SetMaximumBoundsCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        screen.SelectedWhiteUnderbaseChoice =
            screen.WhiteUnderbaseChoices.Single(choice => choice.Branch == branch);
        await screen.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();
    }

    private static SessionStep StepOf(SessionAggregate aggregate, StepKind kind) =>
        aggregate.Steps.Single(step => step.Step == kind);

    private static async Task AssertAgreesAsync(OpenSession open)
    {
        SessionAggregate persisted = await open.ReloadAsync();
        IReadOnlyList<CommandKind> legal =
            WorkflowEngine.Instance.AvailableCommands(persisted.ToSnapshot());

        SessionViewModel screen = open.Screen;
        screen.CanSetMaximumBounds.ShouldBe(legal.Contains(CommandKind.SetPrintDimensions));
        screen.CanSelectWhiteUnderbase.ShouldBe(legal.Contains(CommandKind.SelectWhiteUnderbaseBranch));
        screen.CanComplete.ShouldBe(legal.Contains(CommandKind.Complete));
        screen.CanAddAnotherSize.ShouldBe(legal.Contains(CommandKind.AddAnotherSize));
        screen.CanRunStep.ShouldBe(legal.Contains(CommandKind.StartStep));
        screen.CanApprove.ShouldBe(legal.Contains(CommandKind.Approve));
    }
}
