using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PrintFlow.App.Localisation;
using PrintFlow.App.Resources;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Tests.Integration.Persistence;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// SCRUM-11150: the print-size step explained in plain words. Every expected value is read from the
/// real session service's plan and preflight, never recomputed here. Real service over the
/// harness's GUID-owned workspace and database, synthetic images, existing fakes and off-screen WPF
/// only; no window, UIA or input.
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class PrintSizeGuidanceUiTests
{
    // --- Maximum bounds (presets): AC1 ----------------------------------------------------

    [Theory]
    [InlineData(SizePreset.A3Landscape, 8000, 4000, "en", LimitingEdge.Width)] // box, wide source: width limits
    [InlineData(SizePreset.A3Portrait, 4000, 2000, "zh-CN", LimitingEdge.Width)] // box, landscape source in a portrait box
    [InlineData(SizePreset.A4, 2000, 4000, "en", LimitingEdge.Height)] // long edge, portrait: height limits
    [InlineData(SizePreset.A5, 4000, 2000, "zh-CN", LimitingEdge.Height)] // short edge, landscape: height limits
    [InlineData(SizePreset.A3Landscape, 300, 200, "en", LimitingEdge.None)] // already within the limits
    [InlineData(SizePreset.A3Landscape, 7200, 5600, "zh-CN", LimitingEdge.Width)] // same proportions as the box: the model reports Width
    public async Task A_preset_summary_matches_the_plan_in_millimetres_and_names_what_decided_it(
        SizePreset preset, int width, int height, string language, LimitingEdge intended)
    {
        using OperatorCultureScope culture = new(language);
        using HomeScreenHarness h = new();
        SessionViewModel screen = await PrintSizeGuidanceSetup.AtDimensionsAsync(h, width, height);
        SizePresetChoice choice = screen.SizePresets.Single(c => c.Preset == preset);

        await screen.UsePresetCommand.ExecuteAsync(choice);

        SessionView view = await LoadAsync(h, screen);
        PrintDimensionsPreflight facts = view.Preflight.ShouldNotBeNull();
        screen.Preflight.ShouldBe(facts);
        screen.IsDraftPreflight.ShouldBeFalse();

        // The preflight is the plan's projection: same pixels, same edge, same decision.
        facts.OutputPixelWidth.ShouldBe(view.ProjectedPixelWidth!.Value);
        facts.OutputPixelHeight.ShouldBe(view.ProjectedPixelHeight!.Value);
        facts.GoverningEdge.ShouldBe(view.LimitingEdge!.Value);
        view.LimitingEdge.ShouldBe(intended, "the fixture exercises the case it is named for");
        facts.Governor.ShouldBe(view.PreparationMode == PrintPreparationMode.ResolutionOnly
            ? PrintSizeGovernor.WithinLimits : PrintSizeGovernor.LimitReached);
        facts.SelectedTargetEdge.ShouldBeNull();

        string summary = screen.PrintSizeSummary;
        summary.ShouldStartWith(string.Format(OperatorCulture.Current, Strings.Session_SizeSummaryCurrent,
            facts.PhysicalWidthMm, facts.PhysicalHeightMm));
        summary.ShouldContain(MillimetresOfRow(screen), Case.Sensitive, "the sentence and the unchanged technical row agree");
        summary.ShouldContain(view.LimitingEdge switch
        {
            LimitingEdge.Width => Strings.Session_SizeGovernorLimitWidth,
            LimitingEdge.Height => Strings.Session_SizeGovernorLimitHeight,
            _ => Strings.Session_SizeGovernorWithinLimits,
        });
        summary.ShouldEndWith(Strings.Session_SizeSummaryProportions);
        AssertLanguage(summary, language);

        // The preset's limits are never presented as the projected print size.
        string bounds = string.Format(OperatorCulture.Current, "{0:0.0} × {1:0.0}",
            (double)choice.Recommendation.MaxWidthMm, (double)choice.Recommendation.MaxHeightMm);
        bool fillsBothLimits = Math.Abs(facts.PhysicalWidthMm - (double)choice.Recommendation.MaxWidthMm) < 0.05
            && Math.Abs(facts.PhysicalHeightMm - (double)choice.Recommendation.MaxHeightMm) < 0.05;
        if (!fillsBothLimits)
            summary.ShouldNotContain(bounds);
    }

    [Fact]
    public async Task Typed_maximum_bounds_are_summarised_as_a_proposal_from_the_same_projection()
    {
        using OperatorCultureScope culture = new("en");
        using HomeScreenHarness h = new();
        SessionViewModel screen = await PrintSizeGuidanceSetup.AtDimensionsAsync(h);
        screen.WidthMmText = "200";
        screen.HeightMmText = "200";
        await screen.PreflightLoaded;

        PrintDimensionsPreflight draft = screen.Preflight.ShouldNotBeNull();
        PrintDimensionsPreflight service = (await h.Sessions.PreviewPrintDimensionsAsync(screen.Id,
            new WorkflowCommand.SetPrintDimensions(PrintDimensions.FromMillimetres(200, 200, SizePreset.Custom)), CancellationToken.None)).Value;
        draft.ShouldBe(service);
        screen.IsDraftPreflight.ShouldBeTrue();
        screen.PrintSizeSummary.ShouldStartWith(string.Format(OperatorCulture.Current, Strings.Session_SizeSummaryDraft,
            draft.PhysicalWidthMm, draft.PhysicalHeightMm));
        screen.PrintSizeSummary.ShouldContain(Strings.Session_SizeGovernorLimitWidth);
        screen.PrintSizeSummary.ShouldNotContain("200.0 × 200.0", Case.Sensitive, "a 2:1 picture cannot fill a square box");
    }

    // --- Custom target edge ---------------------------------------------------------------

    [Theory]
    [InlineData(TargetEdge.Width, 4000, 2000, "en")]
    [InlineData(TargetEdge.Height, 4000, 2000, "zh-CN")]
    [InlineData(TargetEdge.LongEdge, 4000, 2000, "en")]
    [InlineData(TargetEdge.LongEdge, 2000, 4000, "zh-CN")]
    [InlineData(TargetEdge.LongEdge, 3000, 3000, "en")]
    public async Task A_custom_summary_names_the_selected_edge_from_the_plan_before_and_after_confirming(
        TargetEdge edge, int width, int height, string language)
    {
        using OperatorCultureScope culture = new(language);
        using HomeScreenHarness h = new();
        SessionViewModel screen = await PrintSizeGuidanceSetup.AtDimensionsAsync(h, width, height);
        string before = await FingerprintAsync(h, screen.Id);

        screen.ChooseCustomSizeCommand.Execute(null);
        screen.SelectedTargetEdgeChoice = screen.TargetEdgeChoices.Single(c => c.Edge == edge);
        screen.CustomMillimetresText = "250";
        await screen.PreflightLoaded;

        PrintDimensionsPreflight draft = screen.Preflight.ShouldNotBeNull();
        PrintDimensionsPreflight service = (await h.Sessions.PreviewPrintDimensionsAsync(screen.Id,
            new WorkflowCommand.SetCustomTargetEdgeSize(edge, 250m), CancellationToken.None)).Value;
        draft.ShouldBe(service);
        draft.Governor.ShouldBe(PrintSizeGovernor.SelectedEdge);
        draft.SelectedTargetEdge.ShouldBe(edge);
        screen.IsDraftPreflight.ShouldBeTrue();
        screen.PrintSizeSummary.ShouldStartWith(string.Format(OperatorCulture.Current, Strings.Session_SizeSummaryDraft,
            draft.PhysicalWidthMm, draft.PhysicalHeightMm));
        screen.PrintSizeSummary.ShouldContain(ExpectedEdgeSentence(edge, draft.GoverningEdge));
        AssertLanguage(screen.PrintSizeSummary, language);
        (await FingerprintAsync(h, screen.Id)).ShouldBe(before, "a draft summary records nothing");

        await screen.ConfirmCustomSizeCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        // The committed plan agrees with the draft sentence, edge for edge and pixel for pixel.
        SessionView view = await LoadAsync(h, screen);
        view.Sizing.RequestedTargetEdge.ShouldBe(edge);
        view.Sizing.ResolvedLimitingEdge.ShouldBe(draft.GoverningEdge);
        view.Sizing.ProjectedPixelWidth.ShouldBe(draft.OutputPixelWidth);
        view.Sizing.ProjectedPixelHeight.ShouldBe(draft.OutputPixelHeight);
        PrintDimensionsPreflight committed = view.Preflight.ShouldNotBeNull();
        committed.GoverningEdge.ShouldBe(draft.GoverningEdge);
        // Each load issues its own offer handle for an enlargement, so only that handle may differ.
        (screen.Preflight.ShouldNotBeNull() with { EnlargementOfferId = committed.EnlargementOfferId })
            .ShouldBe(committed, "the committed projection replaces the draft");
        screen.IsDraftPreflight.ShouldBeFalse();
        screen.PrintSizeSummary.ShouldStartWith(string.Format(OperatorCulture.Current, Strings.Session_SizeSummaryCurrent,
            committed.PhysicalWidthMm, committed.PhysicalHeightMm));
        screen.PrintSizeSummary.ShouldContain(ExpectedEdgeSentence(edge, committed.GoverningEdge));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public async Task On_the_customer_design_route_the_summary_sizes_the_whole_trimmed_canvas(string language)
    {
        using OperatorCultureScope culture = new(language);
        using HomeScreenHarness h = new();
        SessionId id = await KeepOriginalExtentPersistenceTests.AtTrim(h.Inner, h.Sessions, WorkflowType.PrepareCustomerDesign);
        await KeepOriginalExtentPersistenceTests.Execute(h.Sessions, id, new WorkflowCommand.SetTrimParameters(TrimMargin.Uniform(2)));
        SessionView trimmed = await KeepOriginalExtentPersistenceTests.RunTrim(h.Sessions, id, manual: false);
        SessionView atDimensions = await KeepOriginalExtentPersistenceTests.Execute(h.Sessions, id,
            new WorkflowCommand.Approve(StepKind.Trim, trimmed.CurrentArtefact!.Sha256));
        atDimensions.CurrentStep!.Step.ShouldBe(StepKind.PrintDimensions);
        SessionViewModel screen = h.Session(new RecordingNavigation());
        screen.Open(atDimensions);
        screen.CanChooseFlexibleSize.ShouldBeTrue();

        screen.ChooseCustomSizeCommand.Execute(null);
        screen.SelectedTargetEdgeChoice = screen.TargetEdgeChoices.Single(c => c.Edge == TargetEdge.Width);
        screen.CustomMillimetresText = "25.4";
        await screen.PreflightLoaded;

        // The plan sizes the trimmed Revision's whole canvas (content plus margin), which is what
        // "the whole picture (including any see-through edges)" says.
        PrintDimensionsPreflight facts = screen.Preflight.ShouldNotBeNull();
        facts.GraphicBoundsKind.ShouldBe(GraphicBoundsKind.AutomaticTrim);
        facts.FinalCanvasBounds.ShouldNotBeNull();
        (facts.SourcePixelWidth, facts.SourcePixelHeight).ShouldBe((facts.FinalCanvasBounds!.Value.Width, facts.FinalCanvasBounds.Value.Height));
        TrimBounds content = facts.ArtworkBounds.ShouldNotBeNull();
        (content.Width < facts.SourcePixelWidth && content.Height < facts.SourcePixelHeight)
            .ShouldBeTrue("the margin makes the sized canvas larger than the visible content");
        screen.PrintSizeSummary.ShouldStartWith(string.Format(OperatorCulture.Current, Strings.Session_SizeSummaryDraft,
            facts.PhysicalWidthMm, facts.PhysicalHeightMm));
        screen.PrintSizeSummary.ShouldContain(MillimetresOfRow(screen));
        screen.PrintSizeSummary.ShouldContain(Strings.Session_SizeGovernorEdgeWidth);
        AssertLanguage(screen.PrintSizeSummary, language);
    }

    // --- Invalid and partial input: AC3 -----------------------------------------------------

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public async Task Invalid_or_partial_input_removes_the_summary_and_shows_the_existing_validation_beside_it(string language)
    {
        using OperatorCultureScope culture = new(language);
        using HomeScreenHarness h = new();
        SessionViewModel screen = await PrintSizeGuidanceSetup.AtDimensionsAsync(h);
        string before = await FingerprintAsync(h, screen.Id);
        screen.ChooseCustomSizeCommand.Execute(null);
        screen.HasCustomSizeInputHint.ShouldBeFalse("nothing typed is not an error");

        screen.CustomMillimetresText = "250";
        screen.HasCustomSizeInputHint.ShouldBeTrue("a size without an edge is not usable");
        screen.CustomSizeInputHint.ShouldBe(Strings.Session_TargetSizeInvalid);
        screen.PrintSizeSummary.ShouldBeEmpty();

        screen.SelectedTargetEdgeChoice = screen.TargetEdgeChoices.Single(c => c.Edge == TargetEdge.Width);
        await screen.PreflightLoaded;
        screen.HasCustomSizeInputHint.ShouldBeFalse();
        screen.PrintSizeSummary.ShouldContain("250.0");

        foreach (string invalid in new[] { "abc", "0", "-5", "12..5" })
        {
            screen.CustomMillimetresText = invalid;
            await screen.PreflightLoaded;
            screen.Preflight.ShouldBeNull($"'{invalid}' supersedes the previous summary");
            screen.PrintSizeSummary.ShouldBeEmpty();
            screen.HasPreflightEnlargementNote.ShouldBeFalse();
            screen.CustomSizeInputHint.ShouldBe(Strings.Session_TargetSizeInvalid);
        }

        await screen.ConfirmCustomSizeCommand.ExecuteAsync(null);
        screen.Notice.ShouldBe(Strings.Session_TargetSizeInvalid, "Confirm keeps its existing refusal");

        screen.CustomMillimetresText = "";
        await screen.PreflightLoaded;
        (screen.HasCustomSizeInputHint, screen.PrintSizeSummary).ShouldBe((false, string.Empty));

        screen.CustomMillimetresText = "120";
        await screen.PreflightLoaded;
        screen.HasCustomSizeInputHint.ShouldBeFalse();
        screen.PrintSizeSummary.ShouldContain("120.0");
        (await FingerprintAsync(h, screen.Id)).ShouldBe(before, "no draft or refusal recorded a size");
    }

    // --- Stale responses --------------------------------------------------------------------

    [Fact]
    public async Task A_late_result_cannot_relabel_a_newer_size_edge_mode_or_session()
    {
        using OperatorCultureScope culture = new("en");
        using HomeScreenHarness h = new();
        SessionView state = (await LoadAsync(h, await PrintSizeGuidanceSetup.AtDimensionsAsync(h)));
        GatedPreflightService service = new(h.Sessions);
        SessionViewModel screen = new(service, h.Previews, h.TiffReviews, new RecordingNavigation());
        screen.Open(state);
        screen.ChooseCustomSizeCommand.Execute(null);
        screen.SelectedTargetEdgeChoice = screen.TargetEdgeChoices.Single(c => c.Edge == TargetEdge.Width);

        // A newer size.
        service.HoldNext();
        screen.CustomMillimetresText = "100";
        Task oldSize = screen.PreflightLoaded;
        await service.Started;
        screen.PrintSizeSummary.ShouldBeEmpty("a pending query shows no summary");
        screen.CustomMillimetresText = "300";
        await screen.PreflightLoaded;
        service.Release();
        await oldSize;
        screen.PrintSizeSummary.ShouldContain("300.0 × 150.0");

        // A newer edge.
        service.HoldNext();
        screen.CustomMillimetresText = "200";
        Task oldEdge = screen.PreflightLoaded;
        await service.Started;
        screen.SelectedTargetEdgeChoice = screen.TargetEdgeChoices.Single(c => c.Edge == TargetEdge.Height);
        await screen.PreflightLoaded;
        service.Release();
        await oldEdge;
        screen.Preflight!.SelectedTargetEdge.ShouldBe(TargetEdge.Height);
        screen.PrintSizeSummary.ShouldContain(Strings.Session_SizeGovernorEdgeHeight);
        screen.PrintSizeSummary.ShouldContain("400.0 × 200.0");

        // Leaving custom mode.
        service.HoldNext();
        screen.CustomMillimetresText = "150";
        Task oldMode = screen.PreflightLoaded;
        await service.Started;
        screen.IsChoosingCustomSize = false;
        service.Release();
        await oldMode;
        screen.Preflight.ShouldBeNull();
        screen.PrintSizeSummary.ShouldBeEmpty();

        // Another session opened while a query was held.
        screen.IsChoosingCustomSize = true;
        screen.SelectedTargetEdgeChoice = screen.TargetEdgeChoices.Single(c => c.Edge == TargetEdge.Width);
        service.HoldNext();
        screen.CustomMillimetresText = "120";
        Task oldSession = screen.PreflightLoaded;
        await service.Started;
        SessionView another = await LoadAsync(h, await PrintSizeGuidanceSetup.AtDimensionsAsync(h, 1000, 1000));
        screen.Open(another);
        service.Release();
        await oldSession;
        screen.Preflight.ShouldBeNull();
        screen.PrintSizeSummary.ShouldBeEmpty();
        screen.PreflightRows.ShouldBeEmpty();
    }

    // --- Enlargement: AC2 -------------------------------------------------------------------

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public async Task A_proposed_enlargement_is_described_in_plain_words_and_creates_no_offer(string language)
    {
        using OperatorCultureScope culture = new(language);
        using HomeScreenHarness h = new();
        SessionViewModel screen = await PrintSizeGuidanceSetup.AtDimensionsAsync(h, 40, 20);
        string before = await FingerprintAsync(h, screen.Id);
        screen.ChooseCustomSizeCommand.Execute(null);
        screen.SelectedTargetEdgeChoice = screen.TargetEdgeChoices.Single(c => c.Edge == TargetEdge.LongEdge);
        screen.CustomMillimetresText = "100";
        await screen.PreflightLoaded;

        screen.Preflight!.RequiresEnlargement.ShouldBeTrue();
        screen.Preflight.EnlargementOfferId.ShouldBeNull();
        screen.PreflightEnlargementNote.ShouldBe(Strings.Session_SizeDraftEnlargement);
        screen.PreflightEnlargementNote.ShouldContain(language == "en" ? "soft or blurry" : "发虚或模糊");
        (screen.NeedsEnlargementAuthority, screen.CanAuthoriseEnlargement, screen.HasUsableEnlargementAuthority)
            .ShouldBe((false, false, false));
        screen.EnlargementPlainWarning.ShouldBeEmpty();
        (await FingerprintAsync(h, screen.Id)).ShouldBe(before);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public async Task A_needed_enlargement_still_requires_the_explicit_confirmation_and_run_waits_for_it(string language)
    {
        using OperatorCultureScope culture = new(language);
        using HomeScreenHarness h = new();
        LocalisationService localisation = new(h.Inner.Settings);
        localisation.Use(language == "en" ? OperatorLanguage.English : OperatorLanguage.SimplifiedChinese);
        SessionViewModel screen = await PrintSizeGuidanceSetup.AtDimensionsAsync(h, 40, 20,
            create: () => new SessionViewModel(h.Sessions, h.Previews, h.TiffReviews, new RecordingNavigation(), null, localisation));
        await ChooseCustomAsync(screen, TargetEdge.LongEdge, "100");
        await ChooseBranchAsync(screen);

        screen.NeedsEnlargementAuthority.ShouldBeTrue();
        screen.CanAuthoriseEnlargement.ShouldBeTrue();
        screen.CanRunPhotoshopOutput.ShouldBeFalse();
        screen.PreflightEnlargementNote.ShouldBeEmpty("the committed offer speaks for itself");
        string warning = screen.EnlargementPlainWarning;
        warning.ShouldContain(screen.ChangeSizeLabel);
        warning.ShouldContain(screen.ContinueWithSizeLabel);
        warning.ShouldContain(screen.RunStepLabel);
        warning.ShouldContain(language == "en" ? "soft or blurry" : "发虚或模糊");
        warning.ShouldNotContain("PPI");
        warning.ShouldNotContain("sharp", Case.Insensitive);
        warning.ShouldNotContain("stretch", Case.Insensitive);
        AssertLanguage(warning, language);
        screen.EnlargementWarning.ShouldContain("300 PPI", Case.Sensitive, "the technical figure is kept for the details");

        // Rendering, opening every details section and switching language accept nothing.
        SessionView offered = await LoadAsync(h, screen);
        string before = await FingerprintAsync(h, screen.Id);
        RenderWithDetailsOpen(screen, new Size(1000, 700));
        localisation.Use(language == "en" ? OperatorLanguage.SimplifiedChinese : OperatorLanguage.English);
        localisation.Use(language == "en" ? OperatorLanguage.English : OperatorLanguage.SimplifiedChinese);
        (await FingerprintAsync(h, screen.Id)).ShouldBe(before);
        SessionView still = await LoadAsync(h, screen);
        still.Sizing.HasUsableEnlargementAuthority.ShouldBeFalse();
        still.Sizing.NeedsEnlargementAuthority.ShouldBeTrue();
        still.CanRunPhotoshopOutput.ShouldBeFalse();
        (screen.NeedsEnlargementAuthority, screen.CanRunPhotoshopOutput).ShouldBe((true, false));
        offered.Sizing.EnlargementOfferId.ShouldNotBeNull();

        // Only the existing explicit command accepts it. A freshly opened screen, because the
        // rendered one bound its collections on the render thread.
        SessionViewModel fresh = h.Session(new RecordingNavigation());
        fresh.Open(still);
        await fresh.PreviewsLoaded;
        fresh.EnlargementPlainWarning.ShouldBe(warning);
        await fresh.ContinueWithSizeCommand.ExecuteAsync(null);
        fresh.Notice.ShouldBeNull();
        (fresh.NeedsEnlargementAuthority, fresh.HasUsableEnlargementAuthority, fresh.CanRunPhotoshopOutput)
            .ShouldBe((false, true, true));
        fresh.EnlargementPlainWarning.ShouldBeEmpty();
        fresh.Preflight.ShouldNotBeNull().EnlargementAuthorised.ShouldBeTrue();
    }

    [Fact]
    public async Task Changing_an_authorised_size_removes_the_authority_and_brings_the_plain_warning_back()
    {
        using OperatorCultureScope culture = new("en");
        using HomeScreenHarness h = new();
        SessionViewModel screen = await PrintSizeGuidanceSetup.AtDimensionsAsync(h, 40, 20);
        await ChooseCustomAsync(screen, TargetEdge.Width, "100");
        await screen.ContinueWithSizeCommand.ExecuteAsync(null);
        screen.HasUsableEnlargementAuthority.ShouldBeTrue();
        screen.EnlargementPlainWarning.ShouldBeEmpty();

        await screen.ChangeSizeCommand.ExecuteAsync(null);
        screen.SelectedTargetEdgeChoice = screen.TargetEdgeChoices.Single(c => c.Edge == TargetEdge.Width);
        screen.CustomMillimetresText = "120";
        await screen.PreflightLoaded;
        screen.PreflightEnlargementNote.ShouldBe(Strings.Session_SizeDraftEnlargement);
        await screen.ConfirmCustomSizeCommand.ExecuteAsync(null);

        (screen.HasUsableEnlargementAuthority, screen.NeedsEnlargementAuthority, screen.CanRunPhotoshopOutput)
            .ShouldBe((false, true, false));
        screen.EnlargementPlainWarning.ShouldNotBeEmpty();
    }

    // --- Technical details and language: AC5 ------------------------------------------------

    [Fact]
    public async Task Every_prior_pixel_and_ppi_value_stays_in_the_details_with_unchanged_values()
    {
        using OperatorCultureScope culture = new("en");
        using HomeScreenHarness h = new();
        SessionViewModel screen = await PrintSizeGuidanceSetup.OpenStateAsync(h, "preset");
        PrintDimensionsPreflight facts = screen.Preflight.ShouldNotBeNull();

        screen.PreflightRows.Select(r => r.Key).ShouldBe(
            ["SourcePixels", "GraphicBounds", "FinalCanvas", "PrintSize", "OutputPixels", "EffectiveDpi", "OutputDpi", "EnlargementStatus"]);
        Row(screen, "SourcePixels").ShouldBe($"{facts.SourcePixelWidth} × {facts.SourcePixelHeight} px");
        Row(screen, "OutputPixels").ShouldBe($"{facts.OutputPixelWidth} × {facts.OutputPixelHeight} px");
        Row(screen, "PrintSize").ShouldBe(string.Format(OperatorCulture.Current, "{0:0.0} × {1:0.0} mm", facts.PhysicalWidthMm, facts.PhysicalHeightMm));
        Row(screen, "EffectiveDpi").ShouldBe(string.Format(OperatorCulture.Current, "{0:0} × {1:0} PPI", facts.EffectiveSourcePpiX, facts.EffectiveSourcePpiY));
        Row(screen, "OutputDpi").ShouldBe($"{facts.ProductionOutputPpi} PPI");
        Row(screen, "EffectiveDpi").ShouldNotBe(Row(screen, "OutputDpi"), "source detail and output PPI stay distinguishable");

        string before = await FingerprintAsync(h, screen.Id);
        var shown = RenderWithDetailsOpen(screen, new Size(1000, 700));

        shown.CollapsedByDefault.ShouldBeTrue("the details start collapsed");
        foreach (PrintDimensionsPreflightRow row in screen.PreflightRows)
            shown.Texts.ShouldContain(row.Value, $"the {row.Key} value is reachable under the details");
        shown.Texts.ShouldContain(screen.PreparationProjectedSize);
        shown.Texts.ShouldContain(screen.PreparationResolution);
        shown.Texts.ShouldContain(screen.PreparationLimitingEdge);
        (await FingerprintAsync(h, screen.Id)).ShouldBe(before, "opening the details writes nothing");
        screen.Preflight.ShouldBeSameAs(facts);
    }

    [Fact]
    public async Task A_language_change_rewrites_the_summary_and_rows_without_a_query_or_losing_input()
    {
        using OperatorCultureScope culture = new("en");
        using HomeScreenHarness h = new();
        LocalisationService localisation = new(h.Inner.Settings);
        localisation.Use(OperatorLanguage.English);
        SessionViewModel screen = await PrintSizeGuidanceSetup.AtDimensionsAsync(h,
            create: () => new SessionViewModel(h.Sessions, h.Previews, h.TiffReviews, new RecordingNavigation(), null, localisation));
        screen.ChooseCustomSizeCommand.Execute(null);
        TargetEdgeChoice edge = screen.TargetEdgeChoices.Single(c => c.Edge == TargetEdge.Width);
        screen.SelectedTargetEdgeChoice = edge;
        screen.CustomMillimetresText = "250";
        await screen.PreflightLoaded;
        PrintDimensionsPreflight facts = screen.Preflight.ShouldNotBeNull();
        Task loaded = screen.PreflightLoaded;
        string english = screen.PrintSizeSummary;
        string before = await FingerprintAsync(h, screen.Id);

        localisation.Use(OperatorLanguage.SimplifiedChinese);

        screen.Preflight.ShouldBeSameAs(facts, "no new query");
        screen.PreflightLoaded.ShouldBeSameAs(loaded);
        (screen.IsChoosingCustomSize, screen.CustomMillimetresText, screen.SelectedTargetEdgeChoice)
            .ShouldBe((true, "250", edge));
        screen.IsDraftPreflight.ShouldBeTrue();
        screen.PrintSizeSummary.ShouldNotBe(english);
        AssertLanguage(screen.PrintSizeSummary, "zh-CN");
        screen.PrintSizeSummary.ShouldContain("250.0");
        screen.PreflightRows.Single(r => r.Key == "PrintSize").Label.ShouldBe("印刷尺寸");
        Row(screen, "PrintSize").ShouldContain("250.0");
        AssertLanguage(screen.CustomModeHelp, "zh-CN");
        AssertLanguage(screen.PresetModeHelp, "zh-CN");
        (await FingerprintAsync(h, screen.Id)).ShouldBe(before);
    }

    // --- Mode help ---------------------------------------------------------------------------

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public async Task Each_existing_mode_says_when_to_choose_it_using_its_real_meaning(string language)
    {
        using OperatorCultureScope culture = new(language);
        using HomeScreenHarness h = new();
        SessionViewModel screen = await PrintSizeGuidanceSetup.AtDimensionsAsync(h);

        screen.CanChooseFlexibleSize.ShouldBeTrue();
        AssertLanguage(screen.PresetModeHelp, language);
        AssertLanguage(screen.CustomModeHelp, language);
        screen.PresetModeHelp.ShouldContain(language == "en" ? "maximum" : "上限");
        screen.PresetModeHelp.ShouldContain(language == "en" ? "never enlarged" : "不会被放大");
        screen.CustomModeHelp.ShouldContain(screen.CustomSizeLabel);
        foreach (TargetEdgeChoice choice in screen.TargetEdgeChoices)
            screen.CustomModeHelp.ShouldContain(choice.Label, Case.Sensitive, "every existing edge choice is named");
        screen.TargetEdgeChoices.Select(c => c.Edge).ShouldBe([TargetEdge.Width, TargetEdge.Height, TargetEdge.LongEdge]);
        foreach (string text in new[] { screen.PresetModeHelp, screen.CustomModeHelp })
        {
            text.ShouldNotContain("PPI");
            text.ShouldNotContain("{");
        }
    }

    // --- Asset route: AC4 ------------------------------------------------------------------

    [Fact]
    public async Task The_asset_route_never_shows_sizing_guidance()
    {
        using OperatorCultureScope culture = new("en");
        using SessionServiceHarness h = new();
        foreach (string step in new[] { "enhancement", "trim" })
        {
            SessionViewModel screen = (await ReviewGuidanceSetup.OpenAtAsync(h, step)).Screen;
            (screen.CanChooseFlexibleSize, screen.HasPrintDimensionsPreflight, screen.HasPreparationPlan)
                .ShouldBe((false, false, false));
            (screen.PrintSizeSummary, screen.EnlargementPlainWarning, screen.CustomSizeInputHint, screen.PreflightEnlargementNote)
                .ShouldBe((string.Empty, string.Empty, string.Empty, string.Empty));

            int visible = WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = screen },
                new Size(1000, 700), tree => tree.OfType<FrameworkElement>().Count(e =>
                    (AutomationProperties.GetAutomationId(e).StartsWith("Session.PrintDimensions.", StringComparison.Ordinal)
                     || AutomationProperties.GetAutomationId(e) == "Session.Preparation.TechnicalDetails")
                    && ReviewGuidanceLayoutTests.Shown(e))).Facts;
            visible.ShouldBe(0, $"the asset {step} screen shows no print-size element");
        }
    }

    // --- Helpers -----------------------------------------------------------------------------

    internal static (bool CollapsedByDefault, IReadOnlyList<string> Texts) RenderWithDetailsOpen(SessionViewModel screen, Size viewport) =>
        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = screen }, viewport, tree =>
        {
            List<Expander> details = tree.OfType<Expander>()
                .Where(e => AutomationProperties.GetAutomationId(e) is "Session.PrintDimensions.TechnicalDetails" or "Session.Preparation.TechnicalDetails")
                .ToList();
            bool collapsed = details.Count > 0 && details.All(e => !e.IsExpanded);
            foreach (Expander expander in details) expander.IsExpanded = true;
            tree.Root.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
            tree.Root.UpdateLayout();
            List<string> texts = [.. Descendants<TextBlock>(tree.Root).Where(ReviewGuidanceLayoutTests.Shown).Select(t => t.Text)];
            return (collapsed, (IReadOnlyList<string>)texts);
        }).Facts;

    internal static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (T nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static string Row(SessionViewModel screen, string key) => screen.PreflightRows.Single(r => r.Key == key).Value;

    /// <summary>The "W × H" part of the existing technical print-size row.</summary>
    private static string MillimetresOfRow(SessionViewModel screen)
    {
        string value = Row(screen, "PrintSize");
        return value[..value.LastIndexOf(' ')];
    }

    private static string ExpectedEdgeSentence(TargetEdge selected, LimitingEdge resolved) => (selected, resolved) switch
    {
        (TargetEdge.Width, _) => Strings.Session_SizeGovernorEdgeWidth,
        (TargetEdge.Height, _) => Strings.Session_SizeGovernorEdgeHeight,
        (TargetEdge.LongEdge, LimitingEdge.Width) => Strings.Session_SizeGovernorLongEdgeWidth,
        _ => Strings.Session_SizeGovernorLongEdgeHeight,
    };

    private static void AssertLanguage(string text, string language)
    {
        text.ShouldNotBeNullOrWhiteSpace();
        bool chinese = text.Any(c => c is >= '一' and <= '鿿');
        chinese.ShouldBe(language == "zh-CN", $"'{text}' is in the operator language");
        text.ShouldNotContain("。 ", Case.Sensitive, "Chinese sentences are not separated by spaces");
        text.ShouldNotContain("{");
    }

    private static async Task ChooseCustomAsync(SessionViewModel screen, TargetEdge edge, string millimetres)
    {
        screen.ChooseCustomSizeCommand.Execute(null);
        screen.SelectedTargetEdgeChoice = screen.TargetEdgeChoices.Single(c => c.Edge == edge);
        screen.CustomMillimetresText = millimetres;
        await screen.PreflightLoaded;
        await screen.ConfirmCustomSizeCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();
    }

    private static async Task ChooseBranchAsync(SessionViewModel screen)
    {
        screen.SelectedWhiteUnderbaseChoice = screen.WhiteUnderbaseChoices.Single(c => c.Branch == WhiteUnderbaseBranch.W1_1px);
        await screen.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();
    }

    private static async Task<SessionView> LoadAsync(HomeScreenHarness h, SessionViewModel screen) =>
        (await h.Sessions.LoadAsync(screen.Id, CancellationToken.None)).Value;

    private static async Task<string> FingerprintAsync(HomeScreenHarness h, SessionId id)
    {
        SessionAggregate aggregate = await FinalSaveFixtures.LoadAsync(h.Inner, id);
        return JsonSerializer.Serialize(new
        {
            aggregate.Session, aggregate.Steps, aggregate.Revisions, aggregate.Attempts, aggregate.Reviews, aggregate.Outputs,
            Files = Directory.GetFiles(h.Inner.FileWorkspace.ResolveAbsoluteDirectory(aggregate.Session.Workspace), "*", SearchOption.AllDirectories).Order(),
        });
    }

    /// <summary>Delays one real preflight result, without replacing its calculation or database.</summary>
    private sealed class GatedPreflightService(ISessionService inner) : ISessionService
    {
        private TaskCompletionSource? _gate;
        private TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _hold;
        public Task Started => _started.Task;
        public void HoldNext()
        {
            _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _hold = true;
        }
        public void Release() => _gate!.SetResult();
        public async Task<OperationResult<PrintDimensionsPreflight>> PreviewPrintDimensionsAsync(
            SessionId id, WorkflowCommand command, CancellationToken cancellationToken)
        {
            TaskCompletionSource? gate = _hold ? _gate : null;
            _hold = false;
            var result = await inner.PreviewPrintDimensionsAsync(id, command, cancellationToken);
            if (gate is not null)
            {
                _started.SetResult();
                await gate.Task;
            }
            return result;
        }
        public event EventHandler<AutomationRuntimeView>? AutomationRuntimeChanged
        {
            add => inner.AutomationRuntimeChanged += value;
            remove => inner.AutomationRuntimeChanged -= value;
        }
        public AutomationRuntimeView GetAutomationRuntime(SessionId id) => inner.GetAutomationRuntime(id);
        public OperationResult<PrintFlow.Domain.Results.Unit> RequestStop(SessionId id, AutomationStopMode mode) => inner.RequestStop(id, mode);
        public Task<OperationResult<SessionView>> LoadAsync(SessionId id, CancellationToken token) => inner.LoadAsync(id, token);
        public Task<OperationResult<SessionView>> ImportAsync(WorkflowType type, string path, string? name, string? op, CancellationToken token) => inner.ImportAsync(type, path, name, op, token);
        public Task<OperationResult<SessionView>> ExecuteAsync(SessionId id, WorkflowCommand command, string? op, CancellationToken token) => inner.ExecuteAsync(id, command, op, token);
        public Task<OperationResult<SessionView>> AuthoriseCurrentEnlargementAsync(SessionId id, Guid offer, string? op, CancellationToken token) => inner.AuthoriseCurrentEnlargementAsync(id, offer, op, token);
        public Task<OperationResult<IReadOnlyList<RecoveryItem>>> ListRecoveryAsync(CancellationToken token) => inner.ListRecoveryAsync(token);
        public Task<OperationResult<SessionView>> ResolveRecoveryAsync(SessionId id, RecoveryAction action, string? path, string? op, CancellationToken token) => inner.ResolveRecoveryAsync(id, action, path, op, token);
        public Task<OperationResult<PrintFlow.Workflow.Services.ErrorDetailsView>> LoadErrorDetailsAsync(SessionId id, AttemptId attempt, CancellationToken token) => inner.LoadErrorDetailsAsync(id, attempt, token);
        public Task<OperationResult<SessionView>> ResolveErrorRecoveryAsync(SessionId id, AttemptId attempt, ErrorRecoveryAction action, string? op, CancellationToken token) => inner.ResolveErrorRecoveryAsync(id, attempt, action, op, token);
        public Task<OperationResult<IReadOnlyList<SessionListItem>>> ListRecentAsync(CancellationToken token) => inner.ListRecentAsync(token);
        public Task<OperationResult<PrintFlow.Domain.Results.Unit>> RemoveFromRecentAsync(SessionId id, CancellationToken token) => inner.RemoveFromRecentAsync(id, token);
    }
}
