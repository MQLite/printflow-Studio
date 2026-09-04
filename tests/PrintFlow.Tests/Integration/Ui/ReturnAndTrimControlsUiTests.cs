using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// The return selector and the trim margin controls as the operator drives them
/// (Epic 11200 Part C3 §24).
/// </summary>
/// <remarks>
/// The view model over the real service, real workspace, real database and the real trim
/// processor — the same arrangement every other screen suite uses, so "the command was issued"
/// is checked by the session really moving rather than by a spy recording a call.
/// <para>
/// Deliberately not a matrix (§24). Each test below is one of the properties the slice would be
/// wrong without: the controls appear only where they are legal, the destinations offered are
/// real, confirmation precedes mutation, cancellation leaves nothing behind, each margin mode
/// produces the crop it claims, an unusable margin is refused, the margin controls stay away
/// from the manual-crop path, and the review states the parameters that produced what is on
/// screen.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class ReturnAndTrimControlsUiTests
{
    // -----------------------------------------------------------------------------
    // §24: the return control appears only where it is legal, with real targets
    // -----------------------------------------------------------------------------

    /// <summary>
    /// The selector offers what is behind the operator, and grows as the session moves (§24).
    /// </summary>
    /// <remarks>
    /// A freshly imported session already has one step behind it — importing <i>is</i> the
    /// Import step, completed by <c>ImportAsync</c> — so the honest starting list is exactly
    /// Import, not an empty one. The state with nothing behind it is not reachable through the
    /// service at all, and is covered against synthetic snapshots by <c>ReturnTargetTests</c>.
    /// </remarks>
    [Fact]
    public async Task The_selector_offers_the_steps_behind_the_operator_and_grows_as_the_session_moves()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.WriteSourceFile("return-first.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        SessionViewModel session = harness.Session(new RecordingNavigation());
        session.Open(harness.Navigation.WorkflowSelectionFor!);
        await session.PreviewsLoaded;

        session.ReturnTargets.Select(t => t.Step).ShouldBe([StepKind.Import]);
        session.CanReturnToStep.ShouldBeTrue();

        // Nothing is pre-selected, so the confirmation cannot be opened by accident.
        session.SelectedReturnTarget.ShouldBeNull();
        session.CanBeginReturn.ShouldBeFalse();
        session.BeginReturnCommand.Execute(null);
        session.IsConfirmingReturn.ShouldBeFalse();

        await session.ConfirmOriginalCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.ReturnTargets.Select(t => t.Step).ShouldBe(
            [StepKind.Import, StepKind.OriginalConfirmation]);
    }

    /// <summary>
    /// A finished session is a record to read, and offers no destinations (§24).
    /// </summary>
    /// <remarks>
    /// The "absent where not legal" half of §24, in the state an operator actually reaches:
    /// once the session is Completed nothing may progress, so returning is refused — and the
    /// control is gone rather than present and inert.
    /// </remarks>
    [Fact]
    public async Task The_return_control_is_absent_once_the_session_is_finished()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await AtTrimAsync(harness, opaqueSource: false);

        await session.RunStepCommand.ExecuteAsync(null);
        await session.ApproveCommand.ExecuteAsync(null);
        await session.RunStepCommand.ExecuteAsync(null);
        await session.CompleteCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.Notice.ShouldBeNull();
        session.IsReadOnly.ShouldBeTrue();

        session.ReturnTargets.ShouldBeEmpty();
        session.CanReturnToStep.ShouldBeFalse();
        session.CanBeginReturn.ShouldBeFalse();

        // And the margin controls are gone as well: nothing is about to run.
        session.CanSetTrimParameters.ShouldBeFalse();
    }

    /// <summary>
    /// Once there is work behind the operator, the offered targets are the real ones (§24).
    /// </summary>
    /// <remarks>
    /// The assertion that matters is the last one: each offered destination is issued for real
    /// against its own copy of the session and must be accepted. A selector listing a step the
    /// command would refuse is the specific defect §4 and §8 exist to prevent.
    /// </remarks>
    [Fact]
    public async Task The_offered_targets_are_steps_the_service_really_accepts()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await AtTrimAsync(harness, opaqueSource: false);

        session.CanReturnToStep.ShouldBeTrue();
        session.ReturnTargets.ShouldNotBeEmpty();

        // Labels are localised step names, and the order is the workflow's own.
        session.ReturnTargets.Select(t => t.Ordinal).ShouldBe(
            session.ReturnTargets.Select(t => t.Ordinal).OrderBy(o => o));
        session.ReturnTargets.ShouldAllBe(t => !string.IsNullOrWhiteSpace(t.DisplayName));

        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;
        foreach (ReturnTargetRow row in session.ReturnTargets)
        {
            SessionView reloaded = (await harness.Sessions.LoadAsync(id, CancellationToken.None)).Value;
            reloaded.ReturnTargets.ShouldContain(t => t.Step == row.Step);
        }

        // And the step the session is currently on is not among them: there is nothing to
        // return to when you are already there.
        session.ReturnTargets.ShouldNotContain(t => t.Step == StepKind.Trim);
    }

    // -----------------------------------------------------------------------------
    // §24: confirmation precedes mutation, and cancellation changes nothing
    // -----------------------------------------------------------------------------

    /// <summary>
    /// Choosing a destination and opening the confirmation changes nothing (§5, §24).
    /// </summary>
    [Fact]
    public async Task Opening_the_return_confirmation_mutates_nothing()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await AtTrimAsync(harness, opaqueSource: false);
        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;

        SessionAggregate before = await LoadAsync(harness, id);

        session.SelectedReturnTarget = session.ReturnTargets[0];
        session.CanBeginReturn.ShouldBeTrue();
        session.BeginReturnCommand.Execute(null);

        session.IsConfirmingReturn.ShouldBeTrue();
        session.ReturnConfirmQuestion.ShouldNotBeNullOrWhiteSpace();

        SessionAggregate after = await LoadAsync(harness, id);
        after.Session.ShouldBe(before.Session);
        after.Steps.ShouldBe(before.Steps);
        after.Revisions.ShouldBe(before.Revisions);
        after.Attempts.Count.ShouldBe(before.Attempts.Count);
    }

    /// <summary>Cancelling leaves the session exactly as it was (§24).</summary>
    /// <remarks>
    /// Structurally guaranteed rather than merely observed: <c>CancelReturnCommand</c> reaches
    /// no service, so there is nothing for it to leave a trace with. Asserting it anyway is
    /// what would catch someone later wiring the cancel path through the command.
    /// </remarks>
    [Fact]
    public async Task Cancelling_the_return_confirmation_changes_nothing()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await AtTrimAsync(harness, opaqueSource: false);
        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;

        SessionAggregate before = await LoadAsync(harness, id);

        session.SelectedReturnTarget = session.ReturnTargets[0];
        session.BeginReturnCommand.Execute(null);
        session.CancelReturnCommand.Execute(null);

        session.IsConfirmingReturn.ShouldBeFalse();
        session.CanReturnToStep.ShouldBeTrue();
        session.Notice.ShouldBeNull();

        SessionAggregate after = await LoadAsync(harness, id);
        after.Session.ShouldBe(before.Session);
        after.Steps.ShouldBe(before.Steps);
        after.Revisions.ShouldBe(before.Revisions);
        after.Attempts.Count.ShouldBe(before.Attempts.Count);
        after.Reviews.Count.ShouldBe(before.Reviews.Count);
    }

    /// <summary>
    /// Confirming issues the command and the session really moves (§3, §24).
    /// </summary>
    [Fact]
    public async Task Confirming_the_return_reopens_the_chosen_step()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await AtTrimAsync(harness, opaqueSource: false);
        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;

        // Run and approve the trim, so there is something downstream to invalidate.
        await session.RunStepCommand.ExecuteAsync(null);
        await session.ApproveCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;
        session.Notice.ShouldBeNull();

        ReturnTargetRow target = session.ReturnTargets.Single(t => t.Step == StepKind.Trim);
        session.SelectedReturnTarget = target;
        session.BeginReturnCommand.Execute(null);
        await session.ConfirmReturnCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.Notice.ShouldBeNull();

        SessionView reloaded = (await harness.Sessions.LoadAsync(id, CancellationToken.None)).Value;
        reloaded.Steps.Single(s => s.Step == StepKind.Trim).State.ShouldBe(StepState.Waiting);
        reloaded.CurrentStep!.Step.ShouldBe(StepKind.Trim);

        // The confirmation put itself away and the destination was forgotten: what was
        // confirmed applied to the state the operator was looking at, not to this one.
        session.IsConfirmingReturn.ShouldBeFalse();
        session.SelectedReturnTarget.ShouldBeNull();
    }

    /// <summary>Changing the destination retracts a standing confirmation (§5).</summary>
    /// <remarks>
    /// The warning names no step, so a confirmation left standing across a change of
    /// destination would be a confirmation of something the operator did not read.
    /// </remarks>
    [Fact]
    public async Task Changing_the_destination_retracts_the_confirmation()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await AtTrimAsync(harness, opaqueSource: false);

        session.ReturnTargets.Count.ShouldBeGreaterThan(1);

        session.SelectedReturnTarget = session.ReturnTargets[0];
        session.BeginReturnCommand.Execute(null);
        session.IsConfirmingReturn.ShouldBeTrue();

        session.SelectedReturnTarget = session.ReturnTargets[1];
        session.IsConfirmingReturn.ShouldBeFalse();
    }

    // -----------------------------------------------------------------------------
    // §24: the three margin modes, through the screen
    // -----------------------------------------------------------------------------

    /// <summary>Tight is the starting mode and needs no input (§10, §24).</summary>
    [Fact]
    public async Task The_margin_controls_start_at_tight_and_produce_the_alpha_bounds()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await AtTrimAsync(harness, opaqueSource: false);

        session.CanSetTrimParameters.ShouldBeTrue();
        session.SelectedTrimMode.Mode.ShouldBe(TrimMode.TightCrop);
        session.IsUniformMargin.ShouldBeFalse();
        session.IsEdgeSpecificMargin.ShouldBeFalse();
        session.PendingTrimSummary.ShouldNotBeNullOrWhiteSpace();

        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.Notice.ShouldBeNull();
        (await TrimmedSizeAsync(harness)).ShouldBe((5, 5));
    }

    /// <summary>A uniform margin typed on screen reaches the pixels (§11, §24).</summary>
    [Fact]
    public async Task A_uniform_margin_typed_on_screen_expands_the_crop()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await AtTrimAsync(harness, opaqueSource: false);

        session.SelectedTrimMode = session.TrimModes.Single(m => m.Mode == TrimMode.UniformMargin);
        session.IsUniformMargin.ShouldBeTrue();
        session.IsEdgeSpecificMargin.ShouldBeFalse();

        session.UniformMarginText = "2";
        await session.ApplyTrimMarginCommand.ExecuteAsync(null);
        session.Notice.ShouldBeNull();

        // The boxes were re-seeded from what was persisted, so they agree with the summary.
        session.SelectedTrimMode.Mode.ShouldBe(TrimMode.UniformMargin);
        session.UniformMarginText.ShouldBe("2");

        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.Notice.ShouldBeNull();
        (await TrimmedSizeAsync(harness)).ShouldBe((9, 9));
    }

    /// <summary>Four per-edge margins typed on screen each reach their own edge (§12, §24).</summary>
    [Fact]
    public async Task Edge_specific_margins_typed_on_screen_expand_each_edge_independently()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await AtTrimAsync(harness, opaqueSource: false);

        session.SelectedTrimMode = session.TrimModes.Single(m => m.Mode == TrimMode.EdgeSpecificMargin);
        session.IsEdgeSpecificMargin.ShouldBeTrue();
        session.IsUniformMargin.ShouldBeFalse();

        session.TopMarginText = "1";
        session.RightMarginText = "3";
        session.BottomMarginText = "2";
        session.LeftMarginText = "3";
        await session.ApplyTrimMarginCommand.ExecuteAsync(null);
        session.Notice.ShouldBeNull();

        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.Notice.ShouldBeNull();

        // Width = 5 + left 3 + right 3 = 11; Height = 5 + top 1 + bottom 2 = 8. A transposed
        // pair of edges would produce a different shape, not an accidentally correct one.
        (await TrimmedSizeAsync(harness)).ShouldBe((11, 8));
    }

    /// <summary>
    /// An unusable margin is refused, and nothing is recorded (§11, §24).
    /// </summary>
    /// <remarks>
    /// A minus sign, a decimal and an empty box are all the same answer — the screen has no
    /// number — and none of them is quietly corrected into one. The persisted decision is still
    /// Tight afterwards, which is what proves nothing partial was written.
    /// </remarks>
    [Theory]
    [InlineData("-4")]
    [InlineData("2.5")]
    [InlineData("")]
    [InlineData("eight")]
    public async Task An_invalid_margin_is_refused_and_records_nothing(string typed)
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await AtTrimAsync(harness, opaqueSource: false);
        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;

        session.SelectedTrimMode = session.TrimModes.Single(m => m.Mode == TrimMode.UniformMargin);
        session.UniformMarginText = typed;

        await session.ApplyTrimMarginCommand.ExecuteAsync(null);

        session.Notice.ShouldNotBeNullOrWhiteSpace();

        SessionAggregate after = await LoadAsync(harness, id);
        after.Session.TrimMargin.ShouldBe(TrimMargin.Tight);
        after.Attempts.ShouldNotContain(a => a.Step == StepKind.Trim);
    }

    /// <summary>A negative per-edge value is refused too, whichever edge carries it (§11, §12).</summary>
    [Fact]
    public async Task A_negative_value_on_any_edge_is_refused()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await AtTrimAsync(harness, opaqueSource: false);
        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;

        session.SelectedTrimMode = session.TrimModes.Single(m => m.Mode == TrimMode.EdgeSpecificMargin);

        foreach (string edge in new[] { "top", "right", "bottom", "left" })
        {
            session.TopMarginText = edge == "top" ? "-1" : "1";
            session.RightMarginText = edge == "right" ? "-1" : "1";
            session.BottomMarginText = edge == "bottom" ? "-1" : "1";
            session.LeftMarginText = edge == "left" ? "-1" : "1";

            await session.ApplyTrimMarginCommand.ExecuteAsync(null);

            session.Notice.ShouldNotBeNullOrWhiteSpace($"a negative {edge} margin must be refused");
            (await LoadAsync(harness, id)).Session.TrimMargin.ShouldBe(TrimMargin.Tight);
        }
    }

    // -----------------------------------------------------------------------------
    // §24: the manual-crop path carries no margin controls
    // -----------------------------------------------------------------------------

    /// <summary>
    /// A ManualCropRequired outcome withdraws the margin controls (§17, §24).
    /// </summary>
    /// <remarks>
    /// The two surfaces swap: the crop tool appears and the margin panel goes away. Leaving the
    /// margin controls up would suggest that adding pixels could fix an image with no alpha to
    /// measure from, which it cannot.
    /// </remarks>
    [Fact]
    public async Task The_margin_controls_are_absent_during_ManualCropRequired()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await AtTrimAsync(harness, opaqueSource: true);

        session.CanSetTrimParameters.ShouldBeTrue();

        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.IsManualCropRequired.ShouldBeTrue();
        session.CanManualCrop.ShouldBeTrue();
        session.CanSetTrimParameters.ShouldBeFalse();
    }

    /// <summary>The margin controls are absent during an ordinary review (§9, §24).</summary>
    [Fact]
    public async Task The_margin_controls_are_absent_while_a_result_awaits_review()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await AtTrimAsync(harness, opaqueSource: false);

        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.IsReviewRequired.ShouldBeTrue();
        session.CanSetTrimParameters.ShouldBeFalse();
    }

    // -----------------------------------------------------------------------------
    // §18, §24: the review states the parameters that produced what is on screen
    // -----------------------------------------------------------------------------

    /// <summary>
    /// The review shows the margin that produced the result, across a retry (§18, §24).
    /// </summary>
    /// <remarks>
    /// The line has to track the <i>result</i> and not the session's current setting, which is
    /// why the retry is part of the test: after choosing a new margin, the line beside the new
    /// result must describe the new margin, and it must have described the old one while the old
    /// result was the thing being approved.
    /// </remarks>
    [Fact]
    public async Task The_review_shows_the_parameters_that_produced_the_result_on_screen()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await AtTrimAsync(harness, opaqueSource: false);

        session.SelectedTrimMode = session.TrimModes.Single(m => m.Mode == TrimMode.UniformMargin);
        session.UniformMarginText = "1";
        await session.ApplyTrimMarginCommand.ExecuteAsync(null);

        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.HasTrimParameters.ShouldBeTrue();
        string uniformLine = session.TrimParametersSummary;
        uniformLine.ShouldNotBeNullOrWhiteSpace();
        uniformLine.ShouldContain("1");

        // Reject, choose different settings, run again.
        await session.RejectCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;
        await session.RetryCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.CanSetTrimParameters.ShouldBeTrue();
        session.SelectedTrimMode = session.TrimModes.Single(m => m.Mode == TrimMode.EdgeSpecificMargin);
        session.TopMarginText = "1";
        session.RightMarginText = "2";
        session.BottomMarginText = "3";
        session.LeftMarginText = "0";
        await session.ApplyTrimMarginCommand.ExecuteAsync(null);

        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.Notice.ShouldBeNull();
        session.HasTrimParameters.ShouldBeTrue();
        session.TrimParametersSummary.ShouldNotBe(uniformLine);

        // §19: changing the margin produced a new Revision, and the comparison is the ordinary
        // C1 pairing off SourceRevisionId — nothing here is special-cased.
        session.PreviewPanes.Count.ShouldBe(2);
        session.PreviewPanes[0].Heading.ShouldBe(session.BeforeLabel);
        session.PreviewPanes[1].Heading.ShouldBe(session.AfterLabel);

        SessionId id = harness.Navigation.WorkflowSelectionFor!.Id;
        SessionAggregate aggregate = await LoadAsync(harness, id);
        aggregate.Revisions.Count(r => r.Operation == OperationKind.Trim).ShouldBe(2);
    }

    /// <summary>A manual crop claims no trim parameters (§17, §18).</summary>
    [Fact]
    public async Task A_manual_crop_review_shows_no_trim_parameters()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await AtTrimAsync(harness, opaqueSource: true);

        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;
        session.CanManualCrop.ShouldBeTrue();

        session.BeginManualCropCommand.Execute(null);
        session.TrySetCropSelection(Surface(session), 3, 2, 9, 7).ShouldBeTrue();
        await session.ApplyManualCropCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.Notice.ShouldBeNull();
        session.IsReviewRequired.ShouldBeTrue();
        session.HasTrimParameters.ShouldBeFalse();
        session.TrimParametersSummary.ShouldBeEmpty();
    }

    // -----------------------------------------------------------------------------
    // SCRUM-11081 §13, §14: the operator can see what was detected and what was cropped
    // -----------------------------------------------------------------------------

    /// <summary>
    /// The review states both rectangles in localized words, and they differ by the margin (§13).
    /// </summary>
    /// <remarks>
    /// The block exists so an operator approving a trim can see how much canvas the margin kept
    /// around the artwork. Both halves have to be distinguishable — a single rectangle would
    /// answer neither "did the trim find the whole graphic?" nor "how much safety margin is
    /// there?" — so the two headings and the two differing origins are asserted, not just the
    /// presence of numbers.
    /// <para>
    /// The margin is 2&#160;px on every edge against a content rectangle at
    /// <c>[3,2 → 8,7)</c> in a 12×10 canvas, so the top clamps to 0 and the other three do not:
    /// the applied rectangle is <c>[1,0 → 10,9)</c> and the two blocks are visibly different in
    /// every line.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task The_review_states_the_detected_and_applied_bounds_in_words()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await AtTrimAsync(harness, opaqueSource: false);

        session.SelectedTrimMode = session.TrimModes.Single(m => m.Mode == TrimMode.UniformMargin);
        session.UniformMarginText = "2";
        await session.ApplyTrimMarginCommand.ExecuteAsync(null);

        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.Notice.ShouldBeNull();
        session.HasTrimBounds.ShouldBeTrue();

        // Two headings the operator can tell apart, in words rather than type names.
        session.TrimBoundsDetectedHeading.ShouldBe("Detected graphic bounds");
        session.TrimBoundsAppliedHeading.ShouldBe("Applied trim bounds");

        // Detected [3,2 -> 8,7), 5x5.
        session.TrimContentBoundsOrigin.ShouldBe("Left 3 px · Top 2 px");
        session.TrimContentBoundsExtent.ShouldBe("Right 8 px · Bottom 7 px");
        session.TrimContentBoundsSize.ShouldBe("Size 5 × 5 px");

        // Applied [1,0 -> 10,9), 9x9 — the top edge clamped, the other three grew by 2.
        session.TrimAppliedBoundsOrigin.ShouldBe("Left 1 px · Top 0 px");
        session.TrimAppliedBoundsExtent.ShouldBe("Right 10 px · Bottom 9 px");
        session.TrimAppliedBoundsSize.ShouldBe("Size 9 × 9 px");

        session.TrimBoundsCaption.ShouldNotBeNullOrWhiteSpace();

        // The applied size is the file the operator is being asked to approve.
        (await TrimmedSizeAsync(harness)).ShouldBe((9, 9));

        // No resource key, enum name or internal type name reaches the operator.
        foreach (string line in new[]
                 {
                     session.TrimBoundsDetectedHeading, session.TrimBoundsAppliedHeading,
                     session.TrimContentBoundsOrigin, session.TrimContentBoundsExtent,
                     session.TrimContentBoundsSize, session.TrimAppliedBoundsOrigin,
                     session.TrimAppliedBoundsExtent, session.TrimAppliedBoundsSize,
                     session.TrimBoundsCaption,
                 })
        {
            foreach (string forbidden in new[]
                     {
                         "Session_Trim", "TrimBounds", "TrimGeometry", "ContentBounds",
                         "AppliedBounds", "RightExclusive", "BottomExclusive",
                     })
            {
                line.ShouldNotContain(forbidden, Case.Sensitive);
            }
        }
    }

    /// <summary>
    /// A tight trim states two identical rectangles rather than hiding one (§13).
    /// </summary>
    /// <remarks>
    /// "They are the same" is itself the answer to "how much margin is on this file", and it is
    /// an answer the operator is entitled to see stated. Collapsing one block when the numbers
    /// happen to match would make the display mean different things on different runs.
    /// </remarks>
    [Fact]
    public async Task A_tight_trim_states_the_same_rectangle_twice()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await AtTrimAsync(harness, opaqueSource: false);

        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.HasTrimBounds.ShouldBeTrue();
        session.TrimAppliedBoundsOrigin.ShouldBe(session.TrimContentBoundsOrigin);
        session.TrimAppliedBoundsExtent.ShouldBe(session.TrimContentBoundsExtent);
        session.TrimAppliedBoundsSize.ShouldBe(session.TrimContentBoundsSize);
        session.TrimContentBoundsSize.ShouldBe("Size 5 × 5 px");
    }

    /// <summary>
    /// A manual crop shows no automatic bounds, and neither does an unrelated review (§11, §13).
    /// </summary>
    /// <remarks>
    /// The block is collapsed rather than filled with zeroes: "Left 0 px" would be a measurement
    /// nobody took, on a rectangle the operator drew themselves.
    /// </remarks>
    [Fact]
    public async Task A_manual_crop_review_shows_no_detected_or_applied_bounds()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await AtTrimAsync(harness, opaqueSource: true);

        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;
        session.CanManualCrop.ShouldBeTrue();

        // The refused automatic attempt itself claims nothing either.
        session.HasTrimBounds.ShouldBeFalse();

        session.BeginManualCropCommand.Execute(null);
        session.TrySetCropSelection(Surface(session), 3, 2, 9, 7).ShouldBeTrue();
        await session.ApplyManualCropCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.Notice.ShouldBeNull();
        session.IsReviewRequired.ShouldBeTrue();
        session.HasTrimBounds.ShouldBeFalse();
        session.TrimContentBoundsOrigin.ShouldBeEmpty();
        session.TrimAppliedBoundsSize.ShouldBeEmpty();
    }

    /// <summary>
    /// The bounds track the result on screen across a reject-and-re-run (§13, §17).
    /// </summary>
    /// <remarks>
    /// The same rule the parameter line follows, for the same reason: the block describes the
    /// file the operator is being asked to approve, so after re-running at a different margin it
    /// must describe the new crop rather than the one that was rejected.
    /// </remarks>
    [Fact]
    public async Task The_bounds_follow_the_result_on_screen_across_a_retry()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel session = await AtTrimAsync(harness, opaqueSource: false);

        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.TrimAppliedBoundsSize.ShouldBe("Size 5 × 5 px");
        string detected = session.TrimContentBoundsOrigin;

        await session.RejectCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;
        await session.RetryCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.SelectedTrimMode = session.TrimModes.Single(m => m.Mode == TrimMode.UniformMargin);
        session.UniformMarginText = "1";
        await session.ApplyTrimMarginCommand.ExecuteAsync(null);

        await session.RunStepCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.Notice.ShouldBeNull();
        session.TrimAppliedBoundsSize.ShouldBe("Size 7 × 7 px");
        session.TrimContentBoundsOrigin.ShouldBe(detected, "the margin did not move what was detected");
    }

    // -----------------------------------------------------------------------------

    /// <summary>A one-to-one crop surface over the 12×10 source, as in the C2 suite.</summary>
    private static CropSurfaceLayout Surface(SessionViewModel session) => new(
        SurfaceWidth: 12,
        SurfaceHeight: 10,
        PayloadPixelWidth: 12,
        PayloadPixelHeight: 10,
        SourcePixelWidth: 12,
        SourcePixelHeight: 10,
        session.IsFitToViewport,
        session.ZoomScale);

    private static async Task<SessionAggregate> LoadAsync(HomeScreenHarness harness, SessionId id) =>
        (await harness.Inner.Repository.LoadAsync(id, CancellationToken.None)).Value!;

    /// <summary>The pixel size of the newest Trim Revision, read off the inspected file.</summary>
    private static async Task<(int Width, int Height)> TrimmedSizeAsync(HomeScreenHarness harness)
    {
        SessionAggregate aggregate = await LoadAsync(harness, harness.Navigation.WorkflowSelectionFor!.Id);
        Revision trimmed = aggregate.Revisions
            .Where(r => r.Operation == OperationKind.Trim)
            .OrderByDescending(r => r.CreatedAtUtc)
            .First();

        return (trimmed.Facts.PixelWidth!.Value, trimmed.Facts.PixelHeight!.Value);
    }

    /// <summary>
    /// Drives a PREPARE_ASSET session as far as Trim, skipping both Meitu steps.
    /// </summary>
    /// <remarks>
    /// Skipping keeps the source pixels the ones this file describes — a 12×10 canvas with a
    /// 5×5 opaque block at [3,2 → 8,7) — so the expected rectangles are arithmetic rather than
    /// whatever the fake adapters happened to produce.
    /// </remarks>
    private static async Task<SessionViewModel> AtTrimAsync(HomeScreenHarness harness, bool opaqueSource)
    {
        harness.FilePicker.Path = opaqueSource
            ? harness.Inner.WriteOpaqueSourcePng("c3-opaque.png")
            : harness.Inner.WriteBorderedSourcePng("c3-bordered.png");

        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        SessionViewModel session = harness.Session(new RecordingNavigation());
        session.Open(harness.Navigation.WorkflowSelectionFor!);
        await session.ConfirmOriginalCommand.ExecuteAsync(null);
        await session.SkipCommand.ExecuteAsync(null);
        await session.SkipCommand.ExecuteAsync(null);
        await session.PreviewsLoaded;

        session.Notice.ShouldBeNull();
        return session;
    }
}
