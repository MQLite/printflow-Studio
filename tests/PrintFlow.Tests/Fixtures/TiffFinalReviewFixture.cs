using System.Globalization;
using PrintFlow.App.ViewModels;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Sessions;
using PrintFlow.Workflow.Services;
using Shouldly;

namespace PrintFlow.Tests.Fixtures;

/// <summary>Which upstream route left the canvas Print Dimensions was calculated from (§46).</summary>
/// <remarks>
/// Top-level rather than nested so a <c>[Theory]</c> in a public test class can name it: three
/// routes leave three different canvases in front of Print Dimensions, which is the whole point
/// of the effective-resolution matrix.
/// </remarks>
public enum UpstreamRoute
{
    AutomaticTrim,
    KeepOriginalExtent,
    ManualCrop,
}

/// <summary>
/// Drives a session all the way to a <b>real</b> production TIFF awaiting final review
/// (SCRUM-11104 §42, §46).
/// </summary>
/// <remarks>
/// Shared rather than duplicated because two suites need the same journey for different
/// questions: one asks what the review says, the other asks what a keyboard and a UI Automation
/// driver can do with it. A second copy of a nine-command workflow drive would be a second place
/// for it to drift out of step with the product.
/// <para>
/// Every route here composes a <see cref="SyntheticProductionTiffProcessor"/>, so the file under
/// review is an accepted separated-CMYK + W1 TIFF rather than the ordinary fake's copy of the
/// input. The default fake is deliberately left alone: every existing test keeps exercising
/// exactly what it always did.
/// </para>
/// </remarks>
internal static class TiffFinalReviewFixture
{
    /// <summary>One open session screen, plus the identity needed to read the session back.</summary>
    internal sealed record Review(HomeScreenHarness Harness, SessionViewModel Screen, SessionId Id)
    {
        public async Task<SessionAggregate> ReloadAsync() =>
            (await Harness.Inner.Repository.LoadAsync(Id, CancellationToken.None)).Value!;
    }

    /// <summary>A harness whose Photoshop step writes a real accepted production TIFF (§42).</summary>
    internal static HomeScreenHarness Harness(out SyntheticProductionTiffProcessor photoshop)
    {
        SyntheticProductionTiffProcessor? created = null;
        HomeScreenHarness harness = new(
            photoshop: workspace => created = new SyntheticProductionTiffProcessor(workspace));

        photoshop = created ?? throw new InvalidOperationException("The harness composed no adapter.");
        return harness;
    }

    /// <summary>Drives a Generate Print TIFF session through the screen to a TIFF awaiting review.</summary>
    internal static async Task<Review> ReviewRequiredAsync(
        HomeScreenHarness harness,
        string fileName,
        int sourceWidth = 240,
        int sourceHeight = 180,
        double maxWidthMm = 200,
        double maxHeightMm = 150,
        Action<PrintDimensionsPreflight>? observePreflight = null)
    {
        Review review = await OpenAsync(
            harness, fileName, WorkflowType.GeneratePrintTiff, sourceWidth, sourceHeight);
        SessionViewModel screen = review.Screen;

        await screen.ConfirmOriginalCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        screen.WidthMmText = maxWidthMm.ToString(CultureInfo.CurrentCulture);
        screen.HeightMmText = maxHeightMm.ToString(CultureInfo.CurrentCulture);
        await screen.SetMaximumBoundsCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();
        observePreflight?.Invoke(screen.Preflight.ShouldNotBeNull());

        await ChooseBranchAsync(screen);
        await screen.RunStepCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();
        await screen.PreviewsLoaded;

        screen.IsReviewRequired.ShouldBeTrue();
        return review;
    }

    /// <summary>
    /// The same, at a custom target edge the source cannot fill — an enlargement the operator
    /// then authorises (§22).
    /// </summary>
    internal static async Task<Review> EnlargedReviewRequiredAsync(
        HomeScreenHarness harness, string fileName)
    {
        Review review = await OpenAsync(
            harness, fileName, WorkflowType.GeneratePrintTiff, sourceWidth: 120, sourceHeight: 90);
        SessionViewModel screen = review.Screen;

        await screen.ConfirmOriginalCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        screen.ChooseCustomSizeCommand.Execute(null);
        screen.SelectedTargetEdgeChoice = screen.TargetEdgeChoices
            .Single(choice => choice.Edge == TargetEdge.Width);
        screen.CustomMillimetresText = "40";
        await screen.ConfirmCustomSizeCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        await ChooseBranchAsync(screen);

        screen.NeedsEnlargementAuthority.ShouldBeTrue();
        await screen.ContinueWithSizeCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();
        screen.HasUsableEnlargementAuthority.ShouldBeTrue();

        await screen.RunStepCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();
        await screen.PreviewsLoaded;

        screen.IsReviewRequired.ShouldBeTrue();
        return review;
    }

    /// <summary>
    /// Drives Prepare Customer Design to a TIFF awaiting review, leaving a different canvas in
    /// front of Print Dimensions depending on <paramref name="route"/> (§46).
    /// </summary>
    internal static async Task<Review> CustomerDesignReviewRequiredAsync(
        HomeScreenHarness harness, UpstreamRoute route)
    {
        // Each route needs the source that reaches it. The two alpha routes share a 200 × 160
        // canvas whose artwork is a 120 × 80 block, so trimming and keeping the extent are
        // genuinely different sizes; the manual-crop route needs an opaque file, because that is
        // the case the deterministic trim honestly refuses.
        string path = route == UpstreamRoute.ManualCrop
            ? harness.Inner.Workspace.CreateSourceFile(
                $"customer-{route}.png",
                SyntheticImages.OpaqueRgbPng(
                    200, 160, (x, y) => ((byte)(x % 256), (byte)(y % 256), (byte)((x + y) % 256))))
            : harness.Inner.Workspace.CreateSourceFile(
                $"customer-{route}.png",
                SyntheticImages.PngWithAlpha(
                    200, 160, (x, y) => x is >= 40 and < 160 && y is >= 40 and < 120 ? (byte)255 : (byte)0));

        Review review = await OpenExistingAsync(harness, path, WorkflowType.PrepareCustomerDesign);
        SessionViewModel screen = review.Screen;

        await screen.ConfirmOriginalCommand.ExecuteAsync(null);
        await screen.SkipCommand.ExecuteAsync(null);   // Enhancement
        await screen.SkipCommand.ExecuteAsync(null);   // Background removal
        screen.Notice.ShouldBeNull();

        switch (route)
        {
            case UpstreamRoute.AutomaticTrim:
                await screen.RunStepCommand.ExecuteAsync(null);
                screen.Notice.ShouldBeNull();
                await screen.PreviewsLoaded;
                screen.IsReviewRequired.ShouldBeTrue();
                await screen.ApproveCommand.ExecuteAsync(null);
                break;

            case UpstreamRoute.KeepOriginalExtent:
                // Recorded as a Skipped Trim rather than a crop, so there is no result to review:
                // the canvas that goes forward is the one that was already there.
                screen.CanKeepOriginalExtent.ShouldBeTrue();
                await screen.KeepOriginalExtentCommand.ExecuteAsync(null);
                break;

            case UpstreamRoute.ManualCrop:
                await screen.RunStepCommand.ExecuteAsync(null);
                await screen.PreviewsLoaded;
                screen.IsManualCropRequired.ShouldBeTrue();
                screen.BeginManualCropCommand.Execute(null);
                screen.TrySetCropSelection(Surface(screen), 20, 20, 170, 140).ShouldBeTrue();
                await screen.ApplyManualCropCommand.ExecuteAsync(null);
                screen.Notice.ShouldBeNull();
                await screen.PreviewsLoaded;
                screen.IsReviewRequired.ShouldBeTrue();
                await screen.ApproveCommand.ExecuteAsync(null);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(route), route, "Unknown upstream route.");
        }

        screen.Notice.ShouldBeNull();
        await screen.PreviewsLoaded;

        screen.WidthMmText = 30d.ToString(CultureInfo.CurrentCulture);
        screen.HeightMmText = 30d.ToString(CultureInfo.CurrentCulture);
        await screen.SetMaximumBoundsCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();

        await ChooseBranchAsync(screen);
        await screen.RunStepCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();
        await screen.PreviewsLoaded;

        screen.IsReviewRequired.ShouldBeTrue();
        return review;
    }

    /// <summary>The crop surface geometry, taken from the pane the operator is dragging over.</summary>
    internal static CropSurfaceLayout Surface(SessionViewModel screen)
    {
        ArtefactPreviewPane pane = screen.CropPane
            ?? throw new InvalidOperationException("There is no crop pane to drag over.");

        return new CropSurfaceLayout(
            pane.PayloadPixelWidth,
            pane.PayloadPixelHeight,
            pane.PayloadPixelWidth,
            pane.PayloadPixelHeight,
            pane.SourcePixelWidth,
            pane.SourcePixelHeight,
            screen.IsFitToViewport,
            screen.ZoomScale);
    }

    internal static async Task ChooseBranchAsync(SessionViewModel screen)
    {
        screen.SelectedWhiteUnderbaseChoice = screen.WhiteUnderbaseChoices
            .Single(choice => choice.Branch == WhiteUnderbaseBranch.W1_1px);
        await screen.SelectWhiteUnderbaseCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();
    }

    internal static Task<Review> OpenAsync(
        HomeScreenHarness harness,
        string fileName,
        WorkflowType workflow,
        int sourceWidth,
        int sourceHeight) =>
        OpenExistingAsync(
            harness,
            harness.Inner.Workspace.CreateSourceFile(
                fileName, SyntheticImages.Png(sourceWidth, sourceHeight, alpha: true)),
            workflow);

    /// <summary>Imports a synthetic file and opens the session screen the way an operator does.</summary>
    internal static async Task<Review> OpenExistingAsync(
        HomeScreenHarness harness, string path, WorkflowType workflow)
    {
        harness.FilePicker.Path = path;
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        harness.Home.Notice.ShouldBeNull();

        RecordingNavigation navigation = new();
        WorkflowSelectionViewModel selection = harness.WorkflowSelection(navigation);
        selection.Open(harness.Navigation.WorkflowSelectionFor!);
        await selection.SelectCommand.ExecuteAsync(
            selection.Workflows.Single(choice => choice.Type == workflow));
        selection.Notice.ShouldBeNull();

        SessionView chosen = navigation.SessionFor.ShouldNotBeNull();
        SessionViewModel screen = harness.Session(navigation);
        screen.Open(chosen);
        await screen.PreviewsLoaded;

        return new Review(harness, screen, chosen.Id);
    }
}
