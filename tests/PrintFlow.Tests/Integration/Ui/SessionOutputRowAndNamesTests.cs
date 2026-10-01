using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Documents;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Ids;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// SCRUM-11154 F-V5 and F-V6, observed on the workstation in zh-CN: the output row's review state
/// was cut off at 1000×700 ("待审核（Wo"), and the reject reason, reject note, return-to selector
/// and TIFF metadata rows had no useful accessible name. Rendered off screen, never shown.
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class SessionOutputRowAndNamesTests
{
    private const string LongName = "long-operator-named-artwork-with-several-words-for-layout.png";

    [Theory]
    // Picture heights measured with the pre-change layout (94 / 449 px): wrapping must not cost them.
    [InlineData(1000, 700, 94)]
    [InlineData(1920, 1040, 449)]
    public async Task Output_row_text_wraps_inside_its_list_and_the_review_surface_stays_usable(double width, double height, double previousPictureHeight)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness harness = TiffFinalReviewFixture.Harness(out _);
        SessionId id = (await TiffFinalReviewFixture.ReviewRequiredAsync(harness, LongName)).Id;

        var facts = (await RenderAsync(harness, id, new Size(width, height), tree =>
        {
            ItemsControl list = tree.OfType<ItemsControl>().Single(l => AutomationProperties.GetAutomationId(l) == "Session.OutputList");
            SessionViewModel model = (SessionViewModel)tree.Root.DataContext;
            PrintOutputRow row = model.Outputs.Single();
            TextBlock[] texts = [.. SharedReviewSurfaceTests.Descendants<TextBlock>(list).Where(t => t.ActualWidth > 0)];
            string all = string.Join("\n", texts.Select(Text));
            return new
            {
                Overflow = texts.Where(t => t.TranslatePoint(new Point(t.ActualWidth, 0), list).X > list.ActualWidth + 0.5
                                            || t.DesiredSize.Width - t.Margin.Left - t.Margin.Right > t.ActualWidth + 0.5).Select(Text).ToArray(),
                ShowsReview = all.Contains(row.Review, StringComparison.Ordinal),
                ShowsLocation = all.Contains(row.Location, StringComparison.Ordinal),
                ShowsName = all.Contains(row.FileName, StringComparison.Ordinal),
                NameTip = texts.Single(t => AutomationProperties.GetAutomationId(t) == "Session.OutputFileName").ToolTip as string,
                StateText = Text(texts.Single(t => AutomationProperties.GetAutomationId(t) == "Session.OutputState")),
                PreviewHeight = tree.OfType<ScrollViewer>().Single(s => AutomationProperties.GetAutomationId(s) == "Session.TiffReviewImage").ActualHeight,
                FileName = row.FileName, ReviewText = row.Review,
                ApproveOnScreen = tree.OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b) == "Session.Approve" &&
                    b.TranslatePoint(new Point(b.ActualWidth, b.ActualHeight), tree.Root) is { } corner && corner.X <= width && corner.Y <= height),
            };
        })).Facts;

        facts.Overflow.ShouldBeEmpty();
        facts.ShowsReview.ShouldBeTrue();
        facts.ShowsLocation.ShouldBeTrue();
        facts.ShowsName.ShouldBeTrue();
        facts.NameTip.ShouldBe(facts.FileName, "a long name may yield with an ellipsis; the full name stays available");
        facts.StateText.ShouldContain(facts.ReviewText, Case.Sensitive, "the review state sits whole beside the name");
        facts.PreviewHeight.ShouldBeGreaterThanOrEqualTo(previousPictureHeight, "the wrapped row does not shrink the picture");
        facts.ApproveOnScreen.ShouldBeTrue();
    }

    [Fact]
    public async Task Reject_return_and_metadata_controls_carry_their_Chinese_labels_as_accessible_names()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness harness = TiffFinalReviewFixture.Harness(out _);
        SessionId id = (await TiffFinalReviewFixture.ReviewRequiredAsync(harness, "a11y-fv6.png")).Id;

        var facts = (await RenderAsync(harness, id, WpfRendering.ReviewViewport, tree =>
        {
            SessionViewModel model = (SessionViewModel)tree.Root.DataContext;
            string Name(string automationId) => tree.OfType<FrameworkElement>()
                .Where(e => AutomationProperties.GetAutomationId(e) == automationId)
                .Select(e => UIElementAutomationPeer.CreatePeerForElement(e)?.GetName() ?? string.Empty).Single();
            ItemsControl metadata = tree.OfType<ItemsControl>().Single(l => AutomationProperties.GetAutomationId(l) == "Session.TiffProductionMetadata");
            return new
            {
                RejectReason = Name("Session.RejectReason"), RejectReasonLabel = model.RejectReasonLabel,
                RejectNotes = Name("Session.RejectNotes"), RejectNotesLabel = model.RejectNotesLabel,
                ReturnTarget = model.CanReturnToStep ? Name("Session.ReturnTarget") : model.ReturnTargetLabel, ReturnTargetLabel = model.ReturnTargetLabel,
                MetadataNames = (UIElementAutomationPeer.CreatePeerForElement(metadata).GetChildren() ?? []).Select(p => p.GetName()).ToArray(),
                MetadataLabels = model.TiffMetadata.Select(r => r.Label).ToArray(),
                MetadataTabStop = metadata.IsTabStop,
                // A closed dropdown realises no item peers off screen, so assert both sources an item
                // peer reads its name from: the container style and the item's ToString fallback.
                ItemNameSetters = new[] { "Session.RejectReason", "Session.ReturnTarget" }.Select(id => tree.OfType<ComboBox>()
                    .Where(c => AutomationProperties.GetAutomationId(c) == id).Select(c => c.ItemContainerStyle?.Setters.OfType<Setter>()
                        .Any(s => s.Property == AutomationProperties.NameProperty) == true).SingleOrDefault(true)).ToArray(),
                ReasonItems = model.RejectionReasons.Select(r => r.ToString()).ToArray(),
                ReturnItems = model.ReturnTargets.Select(r => (r.ToString(), r.DisplayName)).ToArray(),
                ReasonLabels = model.RejectionReasons.Select(r => r.Label).ToArray(),
            };
        })).Facts;

        facts.RejectReason.ShouldBe(facts.RejectReasonLabel);
        facts.RejectNotes.ShouldBe(facts.RejectNotesLabel);
        facts.ReturnTarget.ShouldBe(facts.ReturnTargetLabel);
        new[] { facts.RejectReason, facts.RejectNotes, facts.ReturnTarget }.ShouldAllBe(name => name.Length > 0 && !name.Contains('_'));
        facts.MetadataNames.ShouldBe(facts.MetadataLabels);
        facts.MetadataNames.ShouldAllBe(name => !name.Contains("TiffMetadataRow"));
        facts.MetadataTabStop.ShouldBeFalse();
        facts.ItemNameSetters.ShouldAllBe(set => set, "each dropdown item container is named from its label");
        facts.ReasonItems.ShouldBe(facts.ReasonLabels, "dropdown items are announced by their labels, never the type name");
        facts.ReturnItems.ShouldAllBe(item => item.Item1 == item.Item2);
    }

    private static string Text(TextBlock text) =>
        text.Inlines.Count > 0 ? string.Concat(text.Inlines.OfType<Run>().Select(run => run.Text)) : text.Text;

    private static async Task<RenderResult<T>> RenderAsync<T>(HomeScreenHarness harness, SessionId id, Size viewport, Func<RenderedTree, T> inspect)
    {
        SessionViewModel fresh = harness.Session(new RecordingNavigation());
        fresh.Open((await harness.Sessions.LoadAsync(id, CancellationToken.None)).Value);
        await fresh.PreviewsLoaded;
        fresh.HasTiffReview.ShouldBeTrue();
        fresh.HasOutputs.ShouldBeTrue("the output row is the observed surface");
        return WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = fresh }, viewport, inspect);
    }
}
