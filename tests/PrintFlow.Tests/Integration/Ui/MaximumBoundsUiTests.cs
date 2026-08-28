using System.Globalization;
using System.IO;
using System.Reflection;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// The session screen's maximum-bound controls: the millimetre limits an operator enters, the
/// projected plan the workflow layer answers with, and what the screen does when a recorded size
/// can no longer be executed (Epic 11400 Part B1A.2B §4–§19).
/// </summary>
/// <remarks>
/// Driven against the real session service, workspace, SQLite database and Fake adapters, exactly
/// as the sibling UI suites are. Nothing here runs Photoshop: Fake mode is what makes the whole
/// workflow observable, and production resizing, CMYK and the TIFF save are B1A.3's.
/// <para>
/// The assertions are deliberately of two kinds. What the operator is <i>told</i> is compared
/// against the <see cref="SessionView"/> the service returned, because the point of the slice is
/// that the screen restates none of the plan rules for itself. What was <i>done</i> is read back
/// through the repository, because a screen that says "reduced proportionally" while the database
/// holds something else is the defect this exists to catch.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class MaximumBoundsUiTests
{
    // -------------------------------------------------------------------------------------
    // §4, §5: maximum-bound wording, and nothing the operator must not be asked
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// The panel asks for two millimetre limits and says what happens between them (§4, §5).
    /// </summary>
    /// <remarks>
    /// The hint is checked for the fixed resolution rather than for prose, so it survives
    /// rewording in either language while still failing if the sentence stops saying what the
    /// operator is agreeing to.
    /// </remarks>
    [Fact]
    public async Task The_panel_asks_for_maximum_width_and_height_in_millimetres()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await AtBoundsAsync(harness, "labels.png");
        SessionViewModel screen = open.Screen;

        screen.CanSetMaximumBounds.ShouldBeTrue();

        screen.MaximumBoundsHeading.ShouldBe(Text.MaxBoundsHeading);
        screen.MaxWidthMmLabel.ShouldBe(Text.LabelMaxWidthMm);
        screen.MaxHeightMmLabel.ShouldBe(Text.LabelMaxHeightMm);
        screen.ConfirmMaximumBoundsLabel.ShouldBe(Text.MaxBoundsConfirm);

        // Millimetres are the operator-facing unit, in both languages.
        screen.MaxWidthMmLabel.ShouldContain("mm", Case.Insensitive);
        screen.MaxHeightMmLabel.ShouldContain("mm", Case.Insensitive);

        // The fixed production resolution is stated, never offered.
        screen.MaximumBoundsHint.ShouldContain("300");
        screen.PreparationResolution.ShouldContain("300");
    }

    /// <summary>
    /// The screen offers no axis selector and no resampling control (§4, §9).
    /// </summary>
    /// <remarks>
    /// Structural rather than a reading of the current XAML: the limiting edge and the resampling
    /// policy are decided by <c>FitWithinBounds</c> and by the accepted contract, so a bindable
    /// member that let an operator express an opinion about either would be the defect — whether
    /// or not any screen binds to it today.
    /// <para>
    /// <c>PreparationLimitingEdge</c> and <c>PreparationAttemptLimitingEdge</c> are read-outs of a
    /// decision already taken, so naming the edge is allowed; offering a <i>choice</i> of one is
    /// what the banned vocabulary catches.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("Axis")]
    [InlineData("Resample")]
    [InlineData("Resampling")]
    [InlineData("Bicubic")]
    [InlineData("Interpolation")]
    [InlineData("SelectedLimitingEdge")]
    [InlineData("PixelWidthText")]
    [InlineData("PixelHeightText")]
    public void The_screen_exposes_no_axis_or_resampling_control(string banned)
    {
        IEnumerable<string> members = typeof(SessionViewModel)
            .GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Select(member => member.Name);

        members.Where(name => name.Contains(banned, StringComparison.OrdinalIgnoreCase))
            .ShouldBeEmpty(
                "the operator chooses limits in millimetres; the governing edge and the " +
                "resampling policy are not theirs to select.");
    }

    // -------------------------------------------------------------------------------------
    // §6: preset and long-edge presentation
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Each preset states its limits as a maximum, from the Domain's own nominal values (§6).
    /// </summary>
    /// <remarks>
    /// The millimetres are compared against <c>PrintDimensions.NominalMillimetres</c> rather than
    /// against literals, which is what makes "no size is stated in the shell" a test rather than a
    /// promise.
    /// </remarks>
    [Fact]
    public async Task Each_preset_presents_its_bounds_as_maximum_limits()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await AtBoundsAsync(harness, "presets.png");
        SessionViewModel screen = open.Screen;

        screen.SizePresets.ShouldNotBeEmpty();

        foreach (SizePresetChoice choice in screen.SizePresets)
        {
            (double WidthMm, double HeightMm) nominal =
                PrintDimensions.NominalMillimetres(choice.Preset).ShouldNotBeNull();

            choice.WidthMm.ShouldBe(nominal.WidthMm);
            choice.HeightMm.ShouldBe(nominal.HeightMm);
            choice.BoundsLabel.ShouldNotBeNullOrWhiteSpace();
        }

        SizePresetChoice a3 = screen.SizePresets.Single(c => c.Preset == SizePreset.A3Landscape);
        a3.BoundsLabel.ShouldBe(Text.MaxBounds(420, 297));
        a3.BoundsLabel.ShouldContain("420");
        a3.BoundsLabel.ShouldContain("297");
    }

    /// <summary>
    /// A box with one limit is presented as a maximum long edge, not as a width × height pair
    /// (§6).
    /// </summary>
    /// <remarks>
    /// A square fit box and a long-edge limit are the same constraint: fitting proportionally
    /// inside it caps whichever source edge is longer and lets Photoshop derive the other. Saying
    /// "150 × 150 mm" would invite the reading the accepted contract removed — that both numbers
    /// are output dimensions.
    /// <para>
    /// Exercised through the real millimetre boxes because no <i>named</i> preset states a single
    /// limit today; the shop contract's long-edge limits for A4 and A5 are not yet a Domain
    /// authority, and wiring them in would change what a named preset persists.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_single_limit_is_presented_as_a_maximum_long_edge()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await AtBoundsAsync(harness, "long-edge.png");
        SessionViewModel screen = open.Screen;

        screen.WidthMmText = "150";
        screen.HeightMmText = "150";
        screen.PendingMaximumBounds.ShouldBe(Text.MaxLongEdge(150));

        // ...and two different limits stay a box, because both of them apply.
        screen.HeightMmText = "100";
        screen.PendingMaximumBounds.ShouldBe(Text.MaxBounds(150, 100));
    }

    // -------------------------------------------------------------------------------------
    // §7: confirming goes through the existing command path
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Confirming sends millimetres through <c>SetPrintDimensions</c> and the workflow layer
    /// produces the plan (§7).
    /// </summary>
    /// <remarks>
    /// The screen supplies two numbers. The plan's binding to the exact upstream Revision and
    /// hash, the limiting-edge selection and the projected pixels are all written by
    /// <c>SessionService</c>, which is why they are asserted against what was persisted rather
    /// than against what the screen displayed.
    /// </remarks>
    [Fact]
    public async Task Confirming_bounds_persists_max_bounds_semantics_and_a_source_bound_plan()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await AtBoundsAsync(harness, "confirm.png");
        SessionViewModel screen = open.Screen;

        screen.WidthMmText = "200";
        screen.HeightMmText = "150";
        await screen.SetMaximumBoundsCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        SessionAggregate persisted = await open.ReloadAsync();
        persisted.Session.DimensionSemantics.ShouldBe(PrintDimensionSemantics.MaxBoundsV1);

        PrintPreparationPlan plan = persisted.Session.PrintPreparationPlan.ShouldNotBeNull();
        plan.MaxWidthMm.ShouldBe(200);
        plan.MaxHeightMm.ShouldBe(150);
        plan.ProductionDpi.ShouldBe(PrintDimensions.ProductionDpi);

        // Bound to the artefact Photoshop would actually consume, not to anything the screen held.
        (RevisionId Id, Sha256 Sha256) upstream =
            persisted.ToSnapshot().UpstreamResultOf(StepKind.PhotoshopOutput)!.Value;
        plan.SourceRevisionId.ShouldBe(upstream.Id);
        plan.SourceSha256.ShouldBe(upstream.Sha256);
    }

    /// <summary>
    /// Millimetres the domain will not accept are refused, and nothing is persisted (§7).
    /// </summary>
    [Theory]
    [InlineData("0", "150")]
    [InlineData("200", "-2")]
    [InlineData("wide", "150")]
    public async Task Unusable_limits_are_refused_and_nothing_is_recorded(string width, string height)
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await AtBoundsAsync(harness, "invalid-bounds.png");
        SessionViewModel screen = open.Screen;

        screen.WidthMmText = width;
        screen.HeightMmText = height;
        screen.PendingMaximumBounds.ShouldBeEmpty();

        await screen.SetMaximumBoundsCommand.ExecuteAsync(null);
        screen.Notice.ShouldBe(Text.MaxBoundsInvalid);

        SessionAggregate persisted = await open.ReloadAsync();
        persisted.Session.Dimensions.ShouldBeNull();
        persisted.Session.PrintPreparationPlan.ShouldBeNull();
        screen.HasPreparationPlan.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------------------
    // §8, §22, §23: the plan summary, and where every figure in it comes from
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Every figure in the summary is the one the read model reported (§8).
    /// </summary>
    [Fact]
    public async Task The_plan_summary_shows_only_what_the_session_view_reported()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await AtBoundsAsync(harness, "summary.png", WideSource(harness));
        SessionViewModel screen = open.Screen;

        await ConfirmBoundsAsync(screen, 50, 50);

        SessionView view = await open.ViewAsync();
        view.PreparationMode.ShouldNotBeNull();

        screen.HasPreparationPlan.ShouldBeTrue();
        screen.ConfirmedMaximumBounds.ShouldBe(
            Text.MaxLongEdge(view.MaxWidthMm!.Value));
        screen.PreparationModeText.ShouldBe(Text.Mode(view.PreparationMode!.Value));
        screen.PreparationLimitingEdge.ShouldBe(Text.LimitingEdge(view.LimitingEdge!.Value));
        screen.PreparationProjectedSize.ShouldContain(
            view.ProjectedPixelWidth!.Value.ToString(CultureInfo.CurrentCulture));
        screen.PreparationProjectedSize.ShouldContain(
            view.ProjectedPixelHeight!.Value.ToString(CultureInfo.CurrentCulture));
    }

    /// <summary>
    /// A source already inside the limits is described as needing no resize (§22).
    /// </summary>
    /// <remarks>
    /// The 6×5 px synthetic source is far inside 200×150 mm, so the honest summary is "pixel
    /// dimensions unchanged": no limiting edge, no shrink sentence, and projected pixels equal to
    /// the source's own. Nothing is enlarged to fill the box.
    /// </remarks>
    [Fact]
    public async Task A_source_already_within_the_limits_is_shown_as_resolution_only()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await AtBoundsAsync(harness, "resolution-only.png");
        SessionViewModel screen = open.Screen;

        await ConfirmBoundsAsync(screen, 200, 150);

        screen.PreparationModeText.ShouldBe(Text.ModeResolutionOnly);
        screen.PreparationModeText.ShouldNotBe(Text.ModeProportionalShrink);

        // No edge is written, so no edge line is shown.
        screen.HasPreparationLimitingEdge.ShouldBeFalse();
        screen.PreparationLimitingEdge.ShouldBeEmpty();

        SessionView view = await open.ViewAsync();
        view.LimitingEdge.ShouldBe(Domain.Outputs.LimitingEdge.None);
        view.PreparationMode.ShouldBe(PrintPreparationMode.ResolutionOnly);

        // The source's own pixels, unchanged and never enlarged.
        view.ProjectedPixelWidth.ShouldBe(6);
        view.ProjectedPixelHeight.ShouldBe(5);
        screen.PreparationProjectedSize.ShouldContain("6");
        screen.PreparationProjectedSize.ShouldContain("5");
    }

    /// <summary>
    /// A source larger than the limits is described as a proportional reduction, on the edge the
    /// plan chose (§23).
    /// </summary>
    /// <remarks>
    /// 2000×1000 px is 169.3×84.7 mm at 300 ppi; against a 50×50 mm box the wider edge governs.
    /// The operator is told which one, and has no way to pick the other.
    /// </remarks>
    [Fact]
    public async Task A_source_larger_than_the_limits_is_shown_as_a_proportional_reduction()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await AtBoundsAsync(harness, "shrink.png", WideSource(harness));
        SessionViewModel screen = open.Screen;

        await ConfirmBoundsAsync(screen, 50, 50);

        screen.PreparationModeText.ShouldBe(Text.ModeProportionalShrink);
        screen.HasPreparationLimitingEdge.ShouldBeTrue();
        screen.PreparationLimitingEdge.ShouldContain(Text.EdgeWidth);

        SessionView view = await open.ViewAsync();
        view.PreparationMode.ShouldBe(PrintPreparationMode.ProportionalShrink);
        view.LimitingEdge.ShouldBe(Domain.Outputs.LimitingEdge.Width);

        // Both projected edges land inside the box, which is what the projection is evidence of.
        view.ProjectedPixelWidth!.Value.ShouldBeLessThanOrEqualTo(
            PrintDimensions.PixelsFromMillimetres(50));
        view.ProjectedPixelHeight!.Value.ShouldBeLessThanOrEqualTo(
            PrintDimensions.PixelsFromMillimetres(50));
    }

    // -------------------------------------------------------------------------------------
    // §10: run readiness
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Run readiness follows <c>CanRunPhotoshopOutput</c> at every stage (§10).
    /// </summary>
    /// <remarks>
    /// The negative half is the point: after the bounds are recorded the session <i>does</i> hold
    /// dimensions and <i>does</i> say MaxBoundsV1, and Run is still withheld until the W1 branch
    /// exists. Either of those two facts read as readiness would offer a button the engine
    /// refuses.
    /// </remarks>
    [Fact]
    public async Task Run_readiness_follows_the_workflow_layer_and_not_the_presence_of_a_size()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await AtBoundsAsync(harness, "readiness.png");
        SessionViewModel screen = open.Screen;

        screen.CanRunPhotoshopOutput.ShouldBeFalse();
        await AssertReadinessAgreesAsync(open);

        await ConfirmBoundsAsync(screen, 200, 150);

        SessionAggregate withBounds = await open.ReloadAsync();
        withBounds.Session.Dimensions.ShouldNotBeNull();
        withBounds.Session.DimensionSemantics.ShouldBe(PrintDimensionSemantics.MaxBoundsV1);
        screen.CanRunPhotoshopOutput.ShouldBeFalse("a branch has not been chosen yet");
        await AssertReadinessAgreesAsync(open);

        await ChooseBranchAsync(screen);

        screen.CanRunPhotoshopOutput.ShouldBeTrue();
        screen.CanRunStep.ShouldBeTrue();
        await AssertReadinessAgreesAsync(open);
    }

    // -------------------------------------------------------------------------------------
    // §11–§15: dimensions that cannot be executed
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// A legacy exact pair is warned about, kept as history, and blocks the run (§11, §15).
    /// </summary>
    /// <remarks>
    /// Not reported as a Photoshop failure, because none happened: the warning is about a size
    /// decision that has to be made again under the current contract. The millimetres stay on
    /// screen so the operator can see what was previously recorded, labelled as history and never
    /// as active limits.
    /// </remarks>
    [Fact]
    public async Task A_legacy_exact_pair_is_shown_as_needing_review_with_its_values_kept_as_history()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await LegacySessionAsync(harness, "legacy.png");
        SessionViewModel screen = open.Screen;

        screen.NeedsDimensionReview.ShouldBeTrue();
        screen.DimensionReviewWarning.ShouldBe(Text.DimensionReviewRequired);

        // Retained for reference, and labelled as such.
        screen.HasHistoricalBounds.ShouldBeTrue();
        screen.HistoricalBounds.ShouldContain("200");
        screen.HistoricalBoundsLabel.ShouldBe(Text.HistoricalBoundsLabel);

        // Never presented as an active decision.
        screen.HasPreparationPlan.ShouldBeFalse();
        screen.ConfirmedMaximumBounds.ShouldBe(Text.DimensionsNotSet);
        screen.CanRunPhotoshopOutput.ShouldBeFalse();
        screen.CanRunStep.ShouldBeFalse();

        // No attempt was created and no adapter ran: this is a size to redo, not a failure.
        SessionAggregate persisted = await open.ReloadAsync();
        persisted.Attempts.ShouldAllBe(a => a.Step != StepKind.PhotoshopOutput);
        persisted.Session.Dimensions.ShouldNotBeNull("the historical pair is retained");
    }

    /// <summary>
    /// The review action is the existing <c>ReturnToStep</c>, aimed at a target the workflow
    /// layer offered (§12, §13).
    /// </summary>
    /// <remarks>
    /// The destination is taken from <see cref="SessionView.ReturnTargets"/> rather than assumed:
    /// that <c>PrintDimensions</c> is usually behind the Photoshop step is not a licence to send a
    /// command the engine may refuse.
    /// </remarks>
    [Fact]
    public async Task Reviewing_the_bounds_opens_the_existing_return_confirmation()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await LegacySessionAsync(harness, "review-action.png");
        SessionViewModel screen = open.Screen;

        screen.CanReviewMaximumBounds.ShouldBeTrue();
        screen.ReturnTargets.ShouldContain(target => target.Step == StepKind.PrintDimensions);

        screen.ReviewMaximumBoundsCommand.Execute(null);

        // The ordinary confirmation, with the ordinary wording about what returning does.
        screen.IsConfirmingReturn.ShouldBeTrue();
        screen.SelectedReturnTarget.ShouldNotBeNull().Step.ShouldBe(StepKind.PrintDimensions);
        screen.ReturnConfirmQuestion.ShouldBe(Text.ReturnConfirmQuestion);
        screen.ReturnConfirmQuestion.ShouldNotContain("delete", Case.Insensitive);

        // Opening it changed nothing.
        SessionAggregate afterOpening = await open.ReloadAsync();
        afterOpening.Session.Dimensions.ShouldNotBeNull();
        afterOpening.ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.PhotoshopOutput);
    }

    /// <summary>
    /// Cancelling the confirmation issues no command and changes nothing (§14).
    /// </summary>
    [Fact]
    public async Task Cancelling_the_review_confirmation_leaves_the_session_untouched()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await LegacySessionAsync(harness, "review-cancel.png");
        SessionViewModel screen = open.Screen;

        SessionAggregate before = await open.ReloadAsync();

        screen.ReviewMaximumBoundsCommand.Execute(null);
        screen.CancelReturnCommand.Execute(null);
        screen.IsConfirmingReturn.ShouldBeFalse();

        SessionAggregate after = await open.ReloadAsync();
        after.Session.Dimensions.ShouldBe(before.Session.Dimensions);
        after.Session.DimensionSemantics.ShouldBe(before.Session.DimensionSemantics);
        after.Attempts.Count.ShouldBe(before.Attempts.Count);
        after.Revisions.Count.ShouldBe(before.Revisions.Count);
        after.ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.PhotoshopOutput);

        (await harness.Inner.Repository.GetAutomationLockAsync(CancellationToken.None))
            .Value.IsHeld.ShouldBeFalse("cancelling acquires no automation lock");

        screen.NeedsDimensionReview.ShouldBeTrue("nothing was resolved by looking at the warning");
    }

    /// <summary>
    /// Confirming the return reaches the size step, where a fresh decision makes the session
    /// runnable again (§12, §24).
    /// </summary>
    /// <remarks>
    /// The whole way back, through the ordinary structural route: no legacy conversion button,
    /// and nothing that adopts the old pair because it happens to suit the source.
    /// </remarks>
    [Fact]
    public async Task Confirming_the_return_reaches_the_size_step_and_a_fresh_decision_restores_the_run()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await LegacySessionAsync(harness, "review-confirm.png");
        SessionViewModel screen = open.Screen;

        screen.ReviewMaximumBoundsCommand.Execute(null);
        await screen.ConfirmReturnCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        SessionAggregate rewound = await open.ReloadAsync();
        rewound.ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.PrintDimensions);
        rewound.Session.Dimensions.ShouldBeNull();
        rewound.Session.DimensionSemantics.ShouldBeNull();

        screen.CanSetMaximumBounds.ShouldBeTrue();
        screen.NeedsDimensionReview.ShouldBeFalse();
        screen.HasHistoricalBounds.ShouldBeFalse();

        await ConfirmBoundsAsync(screen, 200, 150);
        await ChooseBranchAsync(screen);

        (await open.ReloadAsync()).Session.DimensionSemantics
            .ShouldBe(PrintDimensionSemantics.MaxBoundsV1);
        screen.HasPreparationPlan.ShouldBeTrue();
        screen.CanRunPhotoshopOutput.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------------------
    // §16: a plan bound to content that has since changed
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// A stale plan is never shown as active, and the run stays closed (§16, §25).
    /// </summary>
    /// <remarks>
    /// The row is still there — staleness is decided by matching rather than by deletion — so the
    /// screen has a plan it could display if it were reading the raw one. It reads the usable one,
    /// which is null, and shows the ordinary review state instead.
    /// </remarks>
    [Fact]
    public async Task A_plan_bound_to_other_content_is_not_shown_as_active()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await ReadyForPhotoshopAsync(harness, "stale.png");

        SessionAggregate ready = await open.ReloadAsync();
        PrintPreparationPlan usable = ready.Session.PrintPreparationPlan.ShouldNotBeNull();

        await CommitAsync(harness, ready, ready.Session with
        {
            PrintPreparationPlan = usable with
            {
                SourceRevisionId = RevisionId.From(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd")),
            },
        });

        SessionViewModel screen = await open.RefreshAsync();

        // The raw plan is retained in the database and invisible on screen.
        (await open.ReloadAsync()).Session.PrintPreparationPlan.ShouldNotBeNull();
        screen.HasPreparationPlan.ShouldBeFalse();
        screen.ConfirmedMaximumBounds.ShouldBe(Text.DimensionsNotSet);
        screen.PreparationLimitingEdge.ShouldBeEmpty();

        screen.CanRunPhotoshopOutput.ShouldBeFalse();
        screen.CanRunStep.ShouldBeFalse();
        screen.NeedsDimensionReview.ShouldBeTrue();
        screen.CanReviewMaximumBounds.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------------------
    // §17: a retry against unchanged content
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Rejecting and retrying against the same upstream keeps the plan and the run (§17).
    /// </summary>
    /// <remarks>
    /// No "always reconfirm on retry" policy: the plan names the artefact it was calculated from,
    /// that artefact has not changed, and so it still applies. A screen that demanded a fresh
    /// decision here would be inventing a rule the workflow layer does not have.
    /// </remarks>
    [Fact]
    public async Task A_retry_against_unchanged_content_keeps_the_plan_and_the_run_available()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await ReadyForPhotoshopAsync(harness, "retry.png");
        SessionViewModel screen = open.Screen;

        string boundsBefore = screen.ConfirmedMaximumBounds;

        await screen.RunStepCommand.ExecuteAsync(null);
        screen.IsReviewRequired.ShouldBeTrue();

        screen.SelectedRejectionReason =
            screen.RejectionReasons.Single(choice => choice.Reason == RejectionReason.DimensionIssue);
        await screen.RejectCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        await screen.RetryCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        screen.NeedsDimensionReview.ShouldBeFalse();
        screen.HasPreparationPlan.ShouldBeTrue();
        screen.ConfirmedMaximumBounds.ShouldBe(boundsBefore);
        screen.CanRunPhotoshopOutput.ShouldBeTrue();
        await AssertReadinessAgreesAsync(open);
    }

    // -------------------------------------------------------------------------------------
    // §18: another size
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Add Another Size presents a fresh maximum-size decision and carries no plan across (§18).
    /// </summary>
    [Fact]
    public async Task Add_another_size_requires_a_new_maximum_size_decision()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await CompletedFirstOutputAsync(harness, "another-size.png");
        SessionViewModel screen = open.Screen;

        await screen.AddAnotherSizeCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        screen.CanSetMaximumBounds.ShouldBeTrue();
        screen.HasPreparationPlan.ShouldBeFalse();
        screen.ConfirmedMaximumBounds.ShouldBe(Text.DimensionsNotSet);
        screen.CanRunPhotoshopOutput.ShouldBeFalse();

        // Not a review state either: nothing unexecutable is being carried forward (§11).
        screen.NeedsDimensionReview.ShouldBeFalse();
        screen.HasHistoricalBounds.ShouldBeFalse();

        SessionAggregate persisted = await open.ReloadAsync();
        persisted.Session.PrintPreparationPlan.ShouldBeNull();
        persisted.Session.DimensionSemantics.ShouldBeNull();

        // The output already made is still on screen, exactly as it was.
        screen.Outputs.ShouldHaveSingleItem().IsValid.ShouldBeTrue();
        persisted.Outputs.ShouldHaveSingleItem();
    }

    // -------------------------------------------------------------------------------------
    // §19, §21: the producing attempt's own plan
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// A Fake Photoshop result is audited against the plan that attempt ran under (§19, §21).
    /// </summary>
    /// <remarks>
    /// Every figure is compared with the attempt's immutable snapshot, which is what makes this an
    /// audit rather than a restatement of whatever the session currently holds. The projection
    /// notice is checked because it is the one line that must not imply a Photoshop read-back.
    /// </remarks>
    [Fact]
    public async Task The_review_audits_the_plan_the_producing_attempt_ran_under()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await ReadyForPhotoshopAsync(harness, "audit.png", WideSource(harness), 50, 50);
        SessionViewModel screen = open.Screen;

        await screen.RunStepCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();
        screen.IsReviewRequired.ShouldBeTrue();

        SessionAggregate persisted = await open.ReloadAsync();
        ProcessingAttempt attempt = persisted.Attempts.Single(a => a.Step == StepKind.PhotoshopOutput);
        attempt.Status.ShouldBe(AttemptStatus.Succeeded);

        PrintPreparationPlan ran = attempt.PrintPreparationPlan.ShouldNotBeNull();

        screen.HasPreparationAttemptAudit.ShouldBeTrue();
        screen.PreparationAttemptBounds.ShouldBe(Text.AttemptBounds(ran.MaxWidthMm, ran.MaxHeightMm));
        screen.PreparationAttemptMode.ShouldBe(Text.AuditMode(ran.Mode));
        screen.PreparationAttemptLimitingEdge.ShouldBe(Text.LimitingEdge(ran.LimitingEdge));
        screen.PreparationAttemptProjected.ShouldBe(
            Text.AttemptProjected(ran.ProjectedPixelWidth, ran.ProjectedPixelHeight, ran.ProductionDpi));

        // Fake mode says so, and claims nothing about an actual result.
        screen.HasPreparationAttemptProjectionNotice.ShouldBeTrue();
        screen.PreparationAttemptProjectionNotice.ShouldBe(Text.AttemptProjectionNotice);
        screen.PreparationAttemptProjected.ShouldNotContain("Actual", Case.Insensitive);

        // Fake adapters produced it; no Photoshop process was involved.
        attempt.AdapterId.ShouldBe(harness.Inner.FakePhotoshop.AdapterId);
        harness.Inner.FakePhotoshop.Mode.ShouldBe(AdapterExecutionMode.Fake);
        (await open.ViewAsync()).ProcessingMode.ShouldBe(AdapterExecutionMode.Fake);
    }

    /// <summary>
    /// The audit keeps quoting the producing attempt after the session's pending plan moves
    /// (§19).
    /// </summary>
    /// <remarks>
    /// The defect this exists to catch: showing the session's current plan beside a result it did
    /// not produce. The pending plan is rebound to a different fit box directly on the row, and
    /// the audit line must not follow it.
    /// </remarks>
    [Fact]
    public async Task The_attempt_audit_does_not_follow_the_sessions_pending_plan()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await ReadyForPhotoshopAsync(harness, "audit-immutable.png");

        await open.Screen.RunStepCommand.ExecuteAsync(null);
        open.Screen.IsReviewRequired.ShouldBeTrue();

        string auditBefore = open.Screen.PreparationAttemptBounds;
        auditBefore.ShouldContain("200");

        SessionAggregate reviewed = await open.ReloadAsync();
        PrintPreparationPlan ran = reviewed.Session.PrintPreparationPlan.ShouldNotBeNull();

        await CommitAsync(harness, reviewed, reviewed.Session with
        {
            PrintPreparationPlan = ran with { MaxWidthMm = 999, MaxHeightMm = 888 },
        });

        SessionViewModel screen = await open.RefreshAsync();

        screen.PreparationAttemptBounds.ShouldBe(auditBefore);
        screen.PreparationAttemptBounds.ShouldNotContain("999");

        // ...and the attempt row itself was never rewritten.
        (await open.ReloadAsync()).Attempts
            .Single(a => a.Step == StepKind.PhotoshopOutput)
            .PrintPreparationPlan!.MaxWidthMm.ShouldBe(200);
    }

    /// <summary>
    /// An artefact that no Photoshop attempt produced carries no preparation audit (§19).
    /// </summary>
    /// <remarks>
    /// The fallback that must not exist: with a plan sitting on the session, a screen that showed
    /// it beside every result would label the confirmed original with limits it was never
    /// prepared under.
    /// </remarks>
    [Fact]
    public async Task An_artefact_no_photoshop_attempt_produced_carries_no_preparation_audit()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await ReadyForPhotoshopAsync(harness, "no-audit.png");

        open.Screen.HasPreparationPlan.ShouldBeTrue("the session does hold a usable plan");
        open.Screen.HasPreparationAttemptAudit.ShouldBeFalse();
        open.Screen.PreparationAttemptBounds.ShouldBeEmpty();
        open.Screen.PreparationAttemptProjected.ShouldBeEmpty();
        open.Screen.HasPreparationAttemptProjectionNotice.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------------------
    // §21: the whole Fake workflow, end to end
    // -------------------------------------------------------------------------------------

    /// <summary>
    /// Import → maximum bounds → plan → run → review, through the screen's own controls (§21).
    /// </summary>
    /// <remarks>
    /// The journey an operator makes, against the real service, workspace, database and Fake
    /// adapters. The input file is checked to be untouched afterwards, because the output path is
    /// a separate controlled file and nothing in this slice edits what was imported.
    /// </remarks>
    [Fact]
    public async Task The_fake_workflow_runs_from_import_to_a_reviewed_output_with_the_input_untouched()
    {
        using HomeScreenHarness harness = new();
        OpenSession open = await AtBoundsAsync(harness, "smoke.png");
        SessionViewModel screen = open.Screen;

        Revision imported = (await open.ReloadAsync()).Revisions[0];
        Sha256 importedSha = imported.Facts.Sha256;

        // Choose limits, confirm, and read the plan back.
        screen.ApplyPresetCommand.Execute(screen.SizePresets.Single(c => c.Preset == SizePreset.A5));
        screen.WidthMmText.ShouldNotBeNullOrWhiteSpace();
        await screen.SetMaximumBoundsCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        screen.HasPreparationPlan.ShouldBeTrue();
        screen.CanRunPhotoshopOutput.ShouldBeFalse("the branch is still missing");

        await ChooseBranchAsync(screen);
        screen.CanRunPhotoshopOutput.ShouldBeTrue();

        await screen.RunStepCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        SessionAggregate persisted = await open.ReloadAsync();
        StepOf(persisted, StepKind.PhotoshopOutput).State.ShouldBe(StepState.ReviewRequired);
        screen.IsReviewRequired.ShouldBeTrue();
        screen.HasPreparationAttemptAudit.ShouldBeTrue();
        screen.HasPreparationAttemptProjectionNotice.ShouldBeTrue();

        // A separate controlled output file, and the import is byte-for-byte what it was.
        //
        // The Fake adapter copies the input to the reserved output path, so the two files share a
        // hash by construction — which is exactly why "unchanged" is asserted on the input's own
        // path and Revision rather than by comparing the two hashes.
        PrintOutput output = persisted.Outputs.ShouldHaveSingleItem();
        output.File.RelativePath.ShouldNotBe(imported.File.RelativePath);
        persisted.Revisions[0].Facts.Sha256.ShouldBe(importedSha);
        File.Exists(harness.ResolveInWorkspace(imported.File.RelativePath)).ShouldBeTrue();
        File.Exists(harness.ResolveInWorkspace(output.File.RelativePath)).ShouldBeTrue();

        // The synthetic-TIFF warning is on screen alongside the projected-plan note.
        screen.IsFakeTiffOutput.ShouldBeTrue();
    }

    // -------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------

    /// <summary>A session screen, plus the identity needed to read the same session back.</summary>
    private sealed record OpenSession(HomeScreenHarness Harness, SessionViewModel Screen, SessionId Id)
    {
        /// <summary>The session exactly as persistence currently holds it.</summary>
        public async Task<SessionAggregate> ReloadAsync() =>
            (await Harness.Inner.Repository.LoadAsync(Id, CancellationToken.None)).Value!;

        /// <summary>The read model the service would hand the screen right now.</summary>
        public async Task<SessionView> ViewAsync() =>
            (await Harness.Sessions.LoadAsync(Id, CancellationToken.None)).Value;

        /// <summary>Re-opens the screen on a freshly loaded read model.</summary>
        /// <remarks>
        /// What navigating back into the session does. Needed after a test writes a row directly:
        /// the screen shows what the service returned, so it has to be given a new one.
        /// </remarks>
        public async Task<SessionViewModel> RefreshAsync()
        {
            Screen.Open(await ViewAsync());
            return Screen;
        }
    }

    /// <summary>A 2000×1000 px source: 169.3×84.7 mm at 300 ppi, so a small box shrinks it.</summary>
    private static string WideSource(HomeScreenHarness harness) =>
        harness.Inner.Workspace.CreateSourceFile("wide.png", SyntheticImages.Png(2000, 1000, alpha: true));

    /// <summary>Imports a GENERATE_PRINT_TIFF session and opens the session screen.</summary>
    private static async Task<OpenSession> OpenAsync(
        HomeScreenHarness harness, string fileName, string? source = null)
    {
        harness.FilePicker.Path = source ?? harness.WriteSourceFile(fileName);
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        harness.Home.Notice.ShouldBeNull();

        RecordingNavigation navigation = new();
        WorkflowSelectionViewModel selection = harness.WorkflowSelection(navigation);
        selection.Open(harness.Navigation.WorkflowSelectionFor!);
        await selection.SelectCommand.ExecuteAsync(
            selection.Workflows.Single(choice => choice.Type == WorkflowType.GeneratePrintTiff));
        selection.Notice.ShouldBeNull();

        SessionView chosen = navigation.SessionFor.ShouldNotBeNull();
        SessionViewModel screen = harness.Session(new RecordingNavigation());
        screen.Open(chosen);

        return new OpenSession(harness, screen, chosen.Id);
    }

    /// <summary>...with the original confirmed, so the session is sitting on PrintDimensions.</summary>
    private static async Task<OpenSession> AtBoundsAsync(
        HomeScreenHarness harness, string fileName, string? source = null)
    {
        OpenSession open = await OpenAsync(harness, fileName, source);
        await open.Screen.ConfirmOriginalCommand.ExecuteAsync(null);
        open.Screen.Notice.ShouldBeNull();

        (await open.ReloadAsync()).ToSnapshot().CurrentStep!.Step.ShouldBe(StepKind.PrintDimensions);
        return open;
    }

    /// <summary>...with bounds and a branch recorded, so Photoshop output may start.</summary>
    private static async Task<OpenSession> ReadyForPhotoshopAsync(
        HomeScreenHarness harness,
        string fileName,
        string? source = null,
        double maxWidthMm = 200,
        double maxHeightMm = 150)
    {
        OpenSession open = await AtBoundsAsync(harness, fileName, source);
        await ConfirmBoundsAsync(open.Screen, maxWidthMm, maxHeightMm);
        await ChooseBranchAsync(open.Screen);
        return open;
    }

    /// <summary>...and completed, holding one approved output.</summary>
    private static async Task<OpenSession> CompletedFirstOutputAsync(
        HomeScreenHarness harness, string fileName)
    {
        OpenSession open = await ReadyForPhotoshopAsync(harness, fileName);

        await open.Screen.RunStepCommand.ExecuteAsync(null);
        await open.Screen.ApproveCommand.ExecuteAsync(null);
        await open.Screen.CompleteCommand.ExecuteAsync(null);
        open.Screen.Notice.ShouldBeNull();

        return open;
    }

    /// <summary>
    /// A session whose recorded millimetres were written under the previous exact-size rule.
    /// </summary>
    /// <remarks>
    /// Written straight onto the row, as migration 0005 leaves an upgraded database: dimensions,
    /// LEGACY_EXACT_PAIR, no plan. The command path cannot produce that state, which is precisely
    /// why the screen has to cope with it.
    /// </remarks>
    private static async Task<OpenSession> LegacySessionAsync(HomeScreenHarness harness, string fileName)
    {
        OpenSession open = await ReadyForPhotoshopAsync(harness, fileName);

        SessionAggregate ready = await open.ReloadAsync();
        await CommitAsync(harness, ready, ready.Session with
        {
            DimensionSemantics = PrintDimensionSemantics.LegacyExactPair,
            PrintPreparationPlan = null,
        });

        await open.RefreshAsync();
        return open;
    }

    private static async Task ConfirmBoundsAsync(
        SessionViewModel screen, double maxWidthMm, double maxHeightMm)
    {
        screen.WidthMmText = maxWidthMm.ToString(CultureInfo.CurrentCulture);
        screen.HeightMmText = maxHeightMm.ToString(CultureInfo.CurrentCulture);
        await screen.SetMaximumBoundsCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();
    }

    private static async Task ChooseBranchAsync(SessionViewModel screen)
    {
        screen.SelectedWhiteUnderbaseChoice = screen.WhiteUnderbaseChoices
            .Single(choice => choice.Branch == WhiteUnderbaseBranch.W1_1px);
        await screen.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();
    }

    private static async Task CommitAsync(
        HomeScreenHarness harness, SessionAggregate aggregate, ProcessingSession session)
    {
        SessionMutation mutation = new(session, aggregate.Steps, [], [], [], [], [], null, null);
        (await harness.Inner.Repository.CommitAsync(mutation, CancellationToken.None))
            .IsSuccess.ShouldBeTrue();
    }

    /// <summary>The screen's readiness and the read model's are the same answer (§10).</summary>
    private static async Task AssertReadinessAgreesAsync(OpenSession open)
    {
        SessionView view = await open.ViewAsync();
        open.Screen.CanRunPhotoshopOutput.ShouldBe(view.CanRunPhotoshopOutput);
        open.Screen.NeedsDimensionReview.ShouldBe(view.NeedsDimensionReview);
        open.Screen.CanSetMaximumBounds.ShouldBe(view.CanSetMaximumBounds);
    }

    private static SessionStep StepOf(SessionAggregate aggregate, StepKind kind) =>
        aggregate.Steps.Single(step => step.Step == kind);

    /// <summary>
    /// Localised text a test compares against, resolved the way the screen resolves it.
    /// </summary>
    /// <remarks>
    /// Composite formats are filled here rather than compared loosely, so the test says what the
    /// operator actually reads — and so it keeps working when the wording is translated.
    /// </remarks>
    private static class Text
    {
        public static string MaxBoundsHeading => Resolve("Session_MaxBoundsHeading");

        public static string LabelMaxWidthMm => Resolve("Session_LabelMaxWidthMm");

        public static string LabelMaxHeightMm => Resolve("Session_LabelMaxHeightMm");

        public static string MaxBoundsConfirm => Resolve("Session_MaxBoundsConfirm");

        public static string MaxBoundsInvalid => Resolve("Session_MaxBoundsInvalid");

        public static string DimensionsNotSet => Resolve("Session_DimensionsNotSet");

        // Two vocabularies on purpose. The pending summary explains what will happen to the
        // operator's image in a sentence; the attempt audit is a compact record line beside a
        // finished result. Neither of them names a resampling method (§8, §9, §19).
        public static string ModeResolutionOnly => Resolve("Session_PreparationResolutionOnly");

        public static string ModeProportionalShrink => Resolve("Session_PreparationProportionalShrink");

        public static string EdgeWidth => Resolve("LimitingEdge_Width");

        public static string DimensionReviewRequired => Resolve("Session_DimensionReviewRequired");

        public static string HistoricalBoundsLabel => Resolve("Session_HistoricalBoundsLabel");

        public static string ReturnConfirmQuestion => Resolve("Session_ReturnConfirmQuestion");

        public static string AttemptProjectionNotice =>
            Resolve("Session_PreparationAttemptProjectionNotice");

        public static string MaxBounds(double maxWidthMm, double maxHeightMm) => string.Format(
            CultureInfo.CurrentCulture,
            Resolve("Session_MaxBoundsSummary"),
            maxWidthMm.ToString("0.##", CultureInfo.CurrentCulture),
            maxHeightMm.ToString("0.##", CultureInfo.CurrentCulture));

        public static string MaxLongEdge(double maxEdgeMm) => string.Format(
            CultureInfo.CurrentCulture,
            Resolve("Session_MaxLongEdgeSummary"),
            maxEdgeMm.ToString("0.##", CultureInfo.CurrentCulture));

        public static string AttemptBounds(double maxWidthMm, double maxHeightMm) => string.Format(
            CultureInfo.CurrentCulture,
            Resolve("Session_PreparationAttemptBounds"),
            maxWidthMm.ToString("0.##", CultureInfo.CurrentCulture),
            maxHeightMm.ToString("0.##", CultureInfo.CurrentCulture));

        public static string AttemptProjected(int pixelWidth, int pixelHeight, int dpi) => string.Format(
            CultureInfo.CurrentCulture,
            Resolve("Session_PreparationAttemptProjected"),
            pixelWidth,
            pixelHeight,
            dpi);

        /// <summary>The sentence the pending plan summary uses.</summary>
        public static string Mode(PrintPreparationMode mode) => mode switch
        {
            PrintPreparationMode.ResolutionOnly => ModeResolutionOnly,
            PrintPreparationMode.ProportionalShrink => ModeProportionalShrink,
            _ => throw new InvalidOperationException($"No wording for {mode}."),
        };

        /// <summary>The compact label the attempt audit uses.</summary>
        public static string AuditMode(PrintPreparationMode mode) => Resolve($"PreparationMode_{mode}");

        public static string LimitingEdge(LimitingEdge edge) => string.Format(
            CultureInfo.CurrentCulture,
            Resolve("Session_PreparationLimitingEdge"),
            Resolve($"LimitingEdge_{edge}"));

        private static string Resolve(string key) =>
            new System.Resources.ResourceManager(
                    "PrintFlow.App.Resources.Strings", typeof(SessionViewModel).Assembly)
                .GetString(key, CultureInfo.CurrentUICulture)
            ?? throw new InvalidOperationException($"No resource '{key}'.");
    }
}
