using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>Focused operator/UI coverage for Epic 11400 Part B1A.2E.</summary>
[Collection(SqliteCollection.Name)]
public sealed class FlexibleSizeUiTests
{
    [Fact]
    public async Task Missing_density_does_not_offer_the_decoders_default_as_an_original_size()
    {
        using HomeScreenHarness harness = new();
        byte[] png = SyntheticImages.Png();
        int densityChunk = png.AsSpan().IndexOf("pHYs"u8);
        densityChunk.ShouldBeGreaterThan(4);
        // Remove the length, type, nine-byte pHYs payload and CRC.
        byte[] untagged = [.. png[..(densityChunk - 4)], .. png[(densityChunk + 17)..]];
        string path = harness.Inner.Workspace.CreateSourceFile("untagged.png", untagged);
        var screen = await AtDimensionsAsync(harness, path);
        screen.CanUseOriginalPrintSize.ShouldBeFalse();
        screen.CanChooseFlexibleSize.ShouldBeTrue();
    }

    [Theory]
    [InlineData(150, 600, 300)]
    [InlineData(300, 300, 150)]
    [InlineData(600, 150, 75)]
    public async Task No_size_correction_preserves_physical_size_at_production_resolution(
        int dpi, int expectedWidth, int expectedHeight)
    {
        using HomeScreenHarness harness = new();
        string path = harness.Inner.Workspace.CreateSourceFile("physical-size.png",
            SyntheticImages.Png(300, 150, dpi, alpha: true));
        var screen = await AtDimensionsAsync(harness, path);
        screen.CanUseOriginalPrintSize.ShouldBeTrue();
        await screen.UseOriginalPrintSizeCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        var view = await LoadAsync(harness, screen);
        var preflight = view.Preflight.ShouldNotBeNull();
        preflight.ProductionOutputPpi.ShouldBe(300);
        preflight.OutputPixelWidth.ShouldBe(expectedWidth);
        preflight.OutputPixelHeight.ShouldBe(expectedHeight);
        preflight.RequiresEnlargement.ShouldBe(dpi < 300);
        preflight.EnlargementAuthorised.ShouldBeFalse();
    }

    [Fact]
    public async Task Preset_cards_use_the_configured_recommendations_and_the_preset_command()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel screen = await AtDimensionsAsync(harness, LargeSource(harness, "preset.png"));

        SizePresetChoice a4 = screen.SizePresets.Single(choice => choice.Preset == SizePreset.A4);
        a4.Recommendation.MaxWidthMm.ShouldBe(280m);
        a4.RecommendationLabel.ShouldContain("280");
        a4.RecommendationLabel.ShouldNotContain("297");

        await screen.UsePresetCommand.ExecuteAsync(a4);

        screen.HasPresetSizeSelection.ShouldBeTrue();
        screen.CurrentSizeSummary.ShouldContain("A4");
        screen.CurrentRecommendation.ShouldContain("280");
        screen.HasPreparationPlan.ShouldBeTrue();
    }

    [Theory]
    [InlineData(TargetEdge.Width)]
    [InlineData(TargetEdge.Height)]
    [InlineData(TargetEdge.LongEdge)]
    public async Task Custom_size_records_exactly_one_explicit_edge(TargetEdge edge)
    {
        using HomeScreenHarness harness = new();
        SessionViewModel screen = await AtDimensionsAsync(harness, LargeSource(harness, $"{edge}.png"));

        screen.ChooseCustomSizeCommand.Execute(null);
        screen.SelectedTargetEdgeChoice = screen.TargetEdgeChoices.Single(choice => choice.Edge == edge);
        screen.CustomMillimetresText = "100.25";
        await screen.ConfirmCustomSizeCommand.ExecuteAsync(null);

        screen.Notice.ShouldBeNull();
        screen.HasCustomSizeSelection.ShouldBeTrue();
        SessionView view = await LoadAsync(harness, screen);
        view.Sizing.RequestedTargetEdge.ShouldBe(edge);
        view.Sizing.RequestedMillimetres.ShouldBe(100.25m);
    }

    [Fact]
    public async Task Adjusting_a_preset_preserves_context_and_is_not_itself_an_enlargement()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel screen = await AtDimensionsAsync(harness, LargeSource(harness, "override.png"));
        SizePresetChoice a4 = screen.SizePresets.Single(choice => choice.Preset == SizePreset.A4);

        await screen.UsePresetCommand.ExecuteAsync(a4);
        await screen.AdjustSizeCommand.ExecuteAsync(null);
        screen.IsConfirmingReturn.ShouldBeTrue();
        await screen.ConfirmReturnCommand.ExecuteAsync(null);

        screen.IsChoosingCustomSize.ShouldBeTrue();
        screen.HasCustomPresetContext.ShouldBeTrue();
        screen.CustomPresetContext.ShouldContain("A4");
        screen.CustomPresetRecommendation.ShouldContain("280");

        screen.SelectedTargetEdgeChoice = screen.TargetEdgeChoices
            .Single(choice => choice.Edge == TargetEdge.LongEdge);
        screen.CustomMillimetresText = "300";
        await screen.ConfirmCustomSizeCommand.ExecuteAsync(null);

        SessionView view = await LoadAsync(harness, screen);
        view.Sizing.Preset.ShouldBe(SizePreset.A4);
        view.Sizing.PresetOverride.ShouldBeTrue();
        view.Sizing.PresetLimitExceeded.ShouldBeTrue();
        view.Sizing.SourceCapacityExceeded.ShouldBeFalse();
        screen.HasPresetLimitNotice.ShouldBeTrue();
        screen.CurrentPresetContext.ShouldContain("A4");
        screen.NeedsEnlargementAuthority.ShouldBeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Enlargement_is_gated_until_the_service_confirms_the_current_offer(bool usePreset)
    {
        using HomeScreenHarness harness = new();
        SessionViewModel screen = await AtDimensionsAsync(harness, TinySource(harness, "enlarge.png"));
        if (usePreset)
        {
            await screen.UsePresetCommand.ExecuteAsync(
                screen.SizePresets.Single(choice => choice.Preset == SizePreset.A4));
            screen.HasPresetSizeSelection.ShouldBeTrue();
            screen.CurrentSizeSummary.ShouldContain("A4");
        }
        else
        {
            await ChooseCustomAsync(screen, TargetEdge.LongEdge, "100");
        }
        await ChooseBranchAsync(screen);

        screen.NeedsEnlargementAuthority.ShouldBeTrue();
        screen.CanRunPhotoshopOutput.ShouldBeFalse();
        screen.EnlargementWarning.ShouldContain("300 PPI");

        await screen.ContinueWithSizeCommand.ExecuteAsync(null);

        screen.Notice.ShouldBeNull();
        screen.NeedsEnlargementAuthority.ShouldBeFalse();
        screen.HasUsableEnlargementAuthority.ShouldBeTrue();
        screen.CanRunPhotoshopOutput.ShouldBeTrue();
    }

    [Fact]
    public async Task Changing_an_authorised_target_removes_the_old_authority_and_requires_a_new_one()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel screen = await AtDimensionsAsync(harness, TinySource(harness, "stale.png"));
        await ChooseCustomAsync(screen, TargetEdge.Width, "100");
        await screen.ContinueWithSizeCommand.ExecuteAsync(null);
        screen.HasUsableEnlargementAuthority.ShouldBeTrue();

        await screen.ChangeSizeCommand.ExecuteAsync(null);

        screen.IsConfirmingReturn.ShouldBeTrue();

        await screen.ConfirmReturnCommand.ExecuteAsync(null);
        screen.SelectedTargetEdgeChoice = screen.TargetEdgeChoices
            .Single(choice => choice.Edge == TargetEdge.Width);
        screen.CustomMillimetresText = "120";
        await screen.ConfirmCustomSizeCommand.ExecuteAsync(null);

        screen.HasUsableEnlargementAuthority.ShouldBeFalse();
        screen.NeedsEnlargementAuthority.ShouldBeTrue();
        screen.CanRunPhotoshopOutput.ShouldBeFalse();
    }

    [Fact]
    public async Task A_displayed_offer_is_refused_if_the_persisted_target_changes_before_the_click()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel screen = await AtDimensionsAsync(harness, TinySource(harness, "offer-race.png"));
        await ChooseCustomAsync(screen, TargetEdge.LongEdge, "100");
        SessionView displayed = await LoadAsync(harness, screen);
        Guid offer = displayed.Sizing.EnlargementOfferId.ShouldNotBeNull();

        (await harness.Sessions.ExecuteAsync(
            screen.Id,
            new WorkflowCommand.ReturnToStep(StepKind.PrintDimensions),
            "other operator",
            CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await harness.Sessions.ExecuteAsync(
            screen.Id,
            new WorkflowCommand.SetCustomTargetEdgeSize(TargetEdge.LongEdge, 120m),
            "other operator",
            CancellationToken.None)).IsSuccess.ShouldBeTrue();

        var refused = await harness.Sessions.AuthoriseCurrentEnlargementAsync(
            screen.Id, offer, "tester", CancellationToken.None);

        refused.IsFailure.ShouldBeTrue();
        SessionView current = await LoadAsync(harness, screen);
        current.Sizing.RequestedMillimetres.ShouldBe(120m);
        current.Sizing.HasUsableEnlargementAuthority.ShouldBeFalse();
    }

    [Fact]
    public async Task Fake_target_edge_attempt_audit_is_immutable_projected_and_records_confirmation()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel screen = await AtDimensionsAsync(harness, TinySource(harness, "audit-target.png"));
        await screen.UsePresetCommand.ExecuteAsync(
            screen.SizePresets.Single(choice => choice.Preset == SizePreset.A4));
        await screen.AdjustSizeCommand.ExecuteAsync(null);
        screen.IsConfirmingReturn.ShouldBeTrue();
        await screen.ConfirmReturnCommand.ExecuteAsync(null);
        screen.SelectedTargetEdgeChoice = screen.TargetEdgeChoices
            .Single(choice => choice.Edge == TargetEdge.LongEdge);
        screen.CustomMillimetresText = "100";
        await screen.ConfirmCustomSizeCommand.ExecuteAsync(null);
        await ChooseBranchAsync(screen);
        await screen.ContinueWithSizeCommand.ExecuteAsync(null);
        await screen.RunStepCommand.ExecuteAsync(null);

        screen.IsReviewRequired.ShouldBeTrue();
        screen.PreparationAttemptSelection.ShouldContain("100");
        screen.PreparationAttemptPresetContext.ShouldContain("A4");
        screen.PreparationAttemptRecommendation.ShouldContain("280");
        screen.PreparationAttemptPresetOverride.ShouldNotBeNullOrWhiteSpace();
        screen.PreparationAttemptResize.ShouldNotBeNullOrWhiteSpace();
        screen.PreparationAttemptEnlargementConfirmation.ShouldNotBeNullOrWhiteSpace();
        screen.HasPreparationAttemptProjectionNotice.ShouldBeTrue();
        screen.PreparationAttemptProjectionNotice.ShouldContain("Photoshop");
    }

    [Fact]
    public async Task Fake_preset_attempt_audit_uses_the_producing_attempt_recommendation()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel screen = await AtDimensionsAsync(harness, LargeSource(harness, "audit-preset.png"));
        SizePresetChoice a4 = screen.SizePresets.Single(choice => choice.Preset == SizePreset.A4);
        await screen.UsePresetCommand.ExecuteAsync(a4);
        await ChooseBranchAsync(screen);
        await screen.RunStepCommand.ExecuteAsync(null);

        screen.PreparationAttemptSelection.ShouldContain("A4");
        screen.PreparationAttemptRecommendation.ShouldContain("280");
        screen.PreparationAttemptResize.ShouldNotBeNullOrWhiteSpace();
        screen.PreparationAttemptProjectionNotice.ShouldContain("Photoshop");
    }

    [Fact]
    public async Task Retry_keeps_matching_authority_and_add_another_size_opens_fresh()
    {
        using HomeScreenHarness harness = new();
        SessionViewModel screen = await AtDimensionsAsync(harness, TinySource(harness, "retry.png"));
        await ChooseCustomAsync(screen, TargetEdge.Height, "100");
        await ChooseBranchAsync(screen);
        await screen.ContinueWithSizeCommand.ExecuteAsync(null);
        await screen.RunStepCommand.ExecuteAsync(null);

        await screen.RejectCommand.ExecuteAsync(null);
        await screen.RetryCommand.ExecuteAsync(null);
        screen.HasUsableEnlargementAuthority.ShouldBeTrue();
        screen.NeedsEnlargementAuthority.ShouldBeFalse();
        screen.CanRunPhotoshopOutput.ShouldBeTrue();

        await screen.RunStepCommand.ExecuteAsync(null);
        await screen.ApproveCommand.ExecuteAsync(null);
        await screen.CompleteCommand.ExecuteAsync(null);
        await screen.AddAnotherSizeCommand.ExecuteAsync(null);

        screen.CanChooseFlexibleSize.ShouldBeTrue();
        screen.HasPreparationPlan.ShouldBeFalse();
        screen.HasUsableEnlargementAuthority.ShouldBeFalse();
        screen.IsChoosingCustomSize.ShouldBeFalse();
        screen.Outputs.ShouldNotBeEmpty();
    }

    private static async Task<SessionViewModel> AtDimensionsAsync(
        HomeScreenHarness harness, string source)
    {
        SessionView imported = (await harness.Sessions.ImportAsync(
            WorkflowType.GeneratePrintTiff,
            source,
            "flexible-size",
            "tester",
            CancellationToken.None)).Value;
        SessionView atDimensions = (await harness.Sessions.ExecuteAsync(
            imported.Id,
            new WorkflowCommand.ConfirmOriginal("finished design"),
            "tester",
            CancellationToken.None)).Value;

        SessionViewModel screen = harness.Session(new RecordingNavigation());
        screen.Open(atDimensions);
        return screen;
    }

    private static async Task ChooseCustomAsync(
        SessionViewModel screen, TargetEdge edge, string millimetres)
    {
        screen.ChooseCustomSizeCommand.Execute(null);
        screen.SelectedTargetEdgeChoice = screen.TargetEdgeChoices.Single(choice => choice.Edge == edge);
        screen.CustomMillimetresText = millimetres;
        await screen.ConfirmCustomSizeCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();
    }

    private static async Task ChooseBranchAsync(SessionViewModel screen)
    {
        screen.SelectedWhiteUnderbaseChoice = screen.WhiteUnderbaseChoices
            .Single(choice => choice.Branch == WhiteUnderbaseBranch.W1_1px);
        await screen.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();
    }

    private static async Task<SessionView> LoadAsync(
        HomeScreenHarness harness, SessionViewModel screen) =>
        (await harness.Sessions.LoadAsync(screen.Id, CancellationToken.None)).Value;

    private static string LargeSource(HomeScreenHarness harness, string name) =>
        harness.Inner.Workspace.CreateSourceFile(
            name, SyntheticImages.Png(4000, 2000, alpha: true));

    private static string TinySource(HomeScreenHarness harness, string name) =>
        harness.Inner.Workspace.CreateSourceFile(name, SyntheticImages.Png(40, 20, alpha: true));
}
