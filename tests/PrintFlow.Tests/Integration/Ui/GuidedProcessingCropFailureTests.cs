using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PrintFlow.App.Resources;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Results;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class GuidedProcessingCropFailureTests
{
    [Theory]
    [InlineData("zh-CN", false)]
    [InlineData("zh-CN", true)]
    [InlineData("en", false)]
    [InlineData("en", true)]
    public async Task A_refused_crop_keeps_its_draft_and_exposes_the_reason_and_code(string language, bool reloadFails)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        var (_, _, review) = await TrimAdjustmentUiTests.OpenAsync(h);
        var before = (await h.Repository.LoadAsync(review.Id, default)).Value!;
        // The real crop command reaches its first repository commit and is refused there.
        // Only the optional post-failure reload is intercepted at the screen's read boundary.
        FaultingRepository repository = new(h.Repository) { FailFromCommit = 1 };
        ScriptedFailureSessionService service = new(h.CreateService(repository: repository));
        SessionViewModel model = new(service, h.Previews, h.TiffReviews, new RecordingNavigation());
        model.Open(review);
        await model.PreviewsLoaded;
        model.BeginTrimAdjustCommand.Execute(null);
        model.TryNudgeTrimHandle(CropHandle.Left, 1, 0, false, false).ShouldBeTrue();
        var draft = model.CropSelection;
        string? identity = model.UseThisTrimIdentity;
        var reviewIdentity = model.ReviewTargetIdentity;
        if (reloadFails) service.LoadFailure = OperationFailure.Create(FailureCode.PersistenceError, "Synthetic reload failure.");

        await model.UseThisTrimCommand.ExecuteAsync(null);

        repository.AttemptedCommits.ShouldBe(1, "the real service attempted the refused crop write");
        model.IsAdjustingTrim.ShouldBeTrue();
        model.GuidedPage.ShouldBe(GuidedSessionPage.Crop);
        model.CropSelection.ShouldBe(draft);
        model.UseThisTrimIdentity.ShouldBe(identity);
        model.ReviewTargetIdentity.ShouldBe(reviewIdentity);
        model.CanApprove.ShouldBeFalse();
        model.NoticeErrorCode.ShouldBe(nameof(FailureCode.PersistenceError));
        model.Notice!.ShouldContain(reloadFails ? Strings.Session_ActionFailedNextStale : Strings.Session_ActionFailedNext);
        string notice = model.Notice!;
        Size viewport = new(1000, 700);
        Func<UserControl> create = () => new SessionScreenView { DataContext = model };
        var facts = WpfRendering.RenderExpectingNoBindingErrors(create, viewport, tree =>
        {
            try
            {
                Settle(tree.Root);
                var all = All(tree.Root).ToArray();
                var notices = all.OfType<TextBlock>().Where(t => t.Text == notice && GuidedSessionProjectionTests.Shown(t)).ToArray();
                var details = all.OfType<Expander>().SingleOrDefault(e => AutomationProperties.GetAutomationId(e) == "Session.Crop.ErrorDetails");
                bool detailsShown = details is not null && GuidedSessionProjectionTests.Shown(details);
                bool initiallyCollapsed = details is not null && !details.IsExpanded;
                Rect noticeBounds = notices.Length == 1 ? Bounds(notices[0], tree.Root) : Rect.Empty;
                Rect detailsBounds = detailsShown ? Bounds(details!, tree.Root) : Rect.Empty;
                if (details is not null) details.IsExpanded = true;
                Settle(tree.Root);
                all = All(tree.Root).ToArray();
                var code = all.OfType<TextBox>().SingleOrDefault(t => AutomationProperties.GetAutomationId(t) == "Session.Crop.ErrorCode");
                return new
                {
                    NoticeCount = notices.Length, NoticeBounds = noticeBounds, DetailsShown = detailsShown,
                    InitiallyCollapsed = initiallyCollapsed, DetailsBounds = detailsBounds,
                    CodeShown = code is not null && GuidedSessionProjectionTests.Shown(code), Code = code?.Text,
                    CodeReadOnly = code?.IsReadOnly, CodeBounds = code is not null ? Bounds(code, tree.Root) : Rect.Empty,
                    Actions = all.OfType<Button>().Where(b => AutomationProperties.GetAutomationId(b) is "Session.TrimAdjust.Cancel" or "Session.TrimAdjust.Use")
                        .Select(b => new { Id = AutomationProperties.GetAutomationId(b), Shown = GuidedSessionProjectionTests.Shown(b), Bounds = Bounds(b, tree.Root) }).ToArray(),
                    CropIds = all.OfType<FrameworkElement>().Select(AutomationProperties.GetAutomationId)
                        .Where(id => id.StartsWith("Session.Crop.")).ToArray(),
                    UnrelatedForms = all.OfType<FrameworkElement>().Any(e => AutomationProperties.GetAutomationId(e) is "Session.TechnicalDetails" or "Session.FinalSave" && GuidedSessionProjectionTests.Shown(e)),
                };
            }
            finally { tree.Root.DataContext = null; tree.Root.UpdateLayout(); }
        }).Facts;
        facts.NoticeCount.ShouldBe(1, "a refused crop must show its coded failure while the same draft remains open");
        facts.DetailsShown.ShouldBeTrue();
        facts.InitiallyCollapsed.ShouldBeTrue();
        facts.CodeShown.ShouldBeTrue();
        facts.Code.ShouldBe(nameof(FailureCode.PersistenceError));
        facts.CodeReadOnly.ShouldBe(true);
        facts.UnrelatedForms.ShouldBeFalse();
        facts.CropIds.Distinct().Count().ShouldBe(facts.CropIds.Length, "crop failure automation IDs remain unique");
        foreach (Rect bounds in new[] { facts.NoticeBounds, facts.DetailsBounds, facts.CodeBounds }) Inside(bounds, viewport);
        facts.Actions.Length.ShouldBe(2);
        foreach (var action in facts.Actions) { action.Shown.ShouldBeTrue(); Inside(action.Bounds, viewport); }
        var after = (await h.Repository.LoadAsync(review.Id, default)).Value!;
        after.Steps.ShouldBe(before.Steps);
        after.Attempts.ShouldBe(before.Attempts);
        after.Revisions.ShouldBe(before.Revisions);
        after.Reviews.ShouldBe(before.Reviews);
        repository.AttemptedCommits.ShouldBe(1, "opening diagnostic details must not write, retry or approve");

        string? directory = Environment.GetEnvironmentVariable("PF_11171_CAPTURE_DIR");
        if (language != "zh-CN" || string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        string state = reloadFails ? "crop-error-reload-failed" : "crop-error-reloaded";
        string path = Path.Combine(directory, state + "-1000x700.png");
        WpfRendering.CapturePng(create, viewport, path, tree =>
        {
            Settle(tree.Root);
            All(tree.Root).OfType<Expander>().Single(e => AutomationProperties.GetAutomationId(e) == "Session.Crop.ErrorDetails").IsExpanded = true;
            Settle(tree.Root);
        });
        File.WriteAllText(Path.ChangeExtension(path, ".json"), JsonSerializer.Serialize(new
        {
            State = state, Language = language, WidthDip = 1000, HeightDip = 700, Dpi = 96, ReloadFails = reloadFails,
            CaptureKind = "Synthetic offscreen WPF; real crop command with faulting repository commit; no application launch",
            ScreenshotSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), Measurements = facts,
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static IEnumerable<DependencyObject> All(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var item in All(VisualTreeHelper.GetChild(root, i))) yield return item;
    }
    private static Rect Bounds(FrameworkElement element, Visual root) => element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
    private static void Inside(Rect bounds, Size viewport)
    {
        bounds.IsEmpty.ShouldBeFalse(); bounds.Height.ShouldBeGreaterThan(0);
        bounds.Left.ShouldBeGreaterThanOrEqualTo(-0.5); bounds.Top.ShouldBeGreaterThanOrEqualTo(-0.5);
        bounds.Right.ShouldBeLessThanOrEqualTo(viewport.Width + 0.5); bounds.Bottom.ShouldBeLessThanOrEqualTo(viewport.Height + 0.5);
    }
    private static void Settle(UserControl root)
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.SystemIdle);
        root.UpdateLayout();
    }
}
