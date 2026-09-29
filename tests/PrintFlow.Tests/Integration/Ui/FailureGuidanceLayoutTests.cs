using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PrintFlow.App.Resources;
using PrintFlow.App.Settings;
using PrintFlow.App.Startup;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Settings;
using Localisation = PrintFlow.App.Localisation;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// SCRUM-11151 layout: every touched failure notice wraps inside its region, its in-place Error
/// details can be opened to the exact code, and both stay reachable. Off-screen WPF at 96 DPI only,
/// 1000×700 and 1920×1040, en and zh-CN; no window, UIA or input.
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class FailureGuidanceLayoutTests
{
    [Fact]
    public async Task A_new_failure_is_brought_into_view_from_a_scrolled_details_column()
    {
        using OperatorCultureScope culture = new("en");
        using SessionServiceHarness h = new();
        var (service, screen, _) = await FailureGuidanceUiTests.AtOriginalConfirmationAsync(h);
        service.NextExecuteFailure = OperationFailure.Create(FailureCode.OutputMissing, "Scripted refusal.");
        await screen.ConfirmOriginalCommand.ExecuteAsync(null);

        var facts = WpfRendering.RenderExpectingNoBindingErrors(
            () => new SessionScreenView { DataContext = screen }, new Size(1000, 700), tree =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                TextBlock notice = tree.OfType<TextBlock>().Single(t => t.Text == screen.Notice && Shown(t));
                ScrollViewer scroll = Ancestor<ScrollViewer>(notice)!;
                scroll.ScrollToEnd();
                Settle(tree);
                double before = scroll.VerticalOffset;
                service.NextExecuteFailure = OperationFailure.Create(FailureCode.PersistenceError, "A later failure.");
                Task action = screen.ConfirmOriginalCommand.ExecuteAsync(null);
                DispatcherFrame frame = new();
                action.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
                Dispatcher.PushFrame(frame);
                action.GetAwaiter().GetResult();
                Settle(tree);
                return (Before: before, After: scroll.VerticalOffset);
            }).Facts;
        facts.Before.ShouldBeGreaterThan(0, "the test starts with the failure above the visible scroll position");
        facts.After.ShouldBe(0, 0.01, "a fresh failure must be discoverable without searching above the current position");
    }

    public static TheoryData<string, string, double, double> Cases()
    {
        TheoryData<string, string, double, double> cases = new();
        foreach (string surface in new[] { "session", "session-specialized", "session-trim", "correction-prepare", "correction-import", "home", "selection", "settings", "details" })
            foreach (string language in new[] { "en", "zh-CN" })
                foreach ((double w, double h) in new[] { (1000d, 700d), (1920d, 1040d) })
                    cases.Add(surface, language, w, h);
        return cases;
    }

    /// <summary>
    /// The notice these screens showed for the same Timeout before SCRUM-11151 (resource values at
    /// 91a21ea), rendered in the same test for a like-for-like picture-row comparison.
    /// </summary>
    private static string BaselineTimeoutNotice(string language) => language == "en"
        ? "That action did not complete. The operation did not finish in time. (Timeout)"
        : "该操作未能完成。操作未在规定时间内完成。（Timeout）";

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task The_failure_notice_wraps_and_its_error_details_open_to_the_exact_code(
        string surface, string language, double width, double height)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        using SettingsScreenHarness? settingsHarness = surface == "settings" ? new SettingsScreenHarness() : null;
        (Func<UserControl> view, object viewModel, string code, Func<int> mutating) =
            await ArrangeAsync(surface, h, settingsHarness);
        string notice = Notice(viewModel);
        int mutatingBefore = mutating();
        Size viewport = new(width, height);

        var result = WpfRendering.RenderExpectingNoBindingErrors(view, viewport, tree =>
        {
            Settle(tree);
            TextBlock text = tree.OfType<TextBlock>().Single(t => t.Text == notice && Shown(t));
            Expander details = tree.OfType<Expander>().Single(e => AutomationProperties.GetAutomationId(e) == "NoticeErrorDetails" && Shown(e));
            bool collapsedAtFirst = !details.IsExpanded;
            double collapsedPictureRow = PictureRowHeight(tree);
            Rect textBounds = Bounds(text, tree.Root);
            FrameworkElement region = (FrameworkElement)VisualTreeHelper.GetParent(text);
            bool wraps = text.ActualWidth <= region.ActualWidth + 0.5 && textBounds.Right <= width + 0.5 &&
                text.ActualHeight >= text.DesiredSize.Height - text.Margin.Top - text.Margin.Bottom - 0.5;

            details.IsExpanded = true;
            Settle(tree);
            TextBox codeBox = Descendants(details).OfType<TextBox>()
                .Single(t => AutomationProperties.GetAutomationId(t) == "NoticeErrorDetails.Code");
            ScrollViewer? scroller = Ancestor<ScrollViewer>(codeBox);
            codeBox.BringIntoView();
            Settle(tree);
            Rect codeBounds = Bounds(codeBox, tree.Root);
            Rect visible = scroller is null ? new Rect(0, 0, width, height) : Bounds(scroller, tree.Root);
            return new Facts(
                Wraps: wraps, TextBounds: textBounds, DetailsShown: Shown(details), CollapsedAtFirst: collapsedAtFirst,
                Header: details.Header as string ?? string.Empty, CodeText: codeBox.Text, CodeShown: Shown(codeBox),
                CodeBounds: codeBounds, CodeReachable: codeBounds.Top >= visible.Top - 0.5 && codeBounds.Bottom <= visible.Bottom + 0.5 && codeBounds.Right <= width + 0.5,
                PictureRow: collapsedPictureRow, ExpandedPictureRow: PictureRowHeight(tree), NoticeHeight: text.ActualHeight);
        });

        Facts facts = result.Facts;
        Record(surface, language, width, height, facts);
        facts.Wraps.ShouldBeTrue("the sentence wraps inside its region");
        facts.DetailsShown.ShouldBeTrue();
        facts.CollapsedAtFirst.ShouldBeTrue("the code stays out of the main sentence until asked for");
        facts.Header.ShouldBe(Strings.ErrorDetails_Heading);
        facts.CodeShown.ShouldBeTrue();
        facts.CodeText.ShouldBe(code);
        facts.CodeReachable.ShouldBeTrue("the code can be reached, by scrolling where the screen scrolls");
        mutating().ShouldBe(mutatingBefore, "rendering and opening Error details sends nothing");
        Capture(view, viewport, $"failure-{surface}-{language}-{width:0}x{height:0}.png", expand: true);

        if (surface.StartsWith("session", StringComparison.Ordinal))
        {
            SessionViewModel screen = (SessionViewModel)viewModel;
            screen.Notice = BaselineTimeoutNotice(language);
            var baseline = WpfRendering.RenderExpectingNoBindingErrors(view, viewport, tree => { Settle(tree); return (Row: PictureRowHeight(tree), Notice: tree.OfType<TextBlock>().Single(t => t.Text == screen.Notice && Shown(t)).ActualHeight); }).Facts;
            screen.Notice = null;
            double none = WpfRendering.RenderExpectingNoBindingErrors(view, viewport, tree => { Settle(tree); return PictureRowHeight(tree); }).Facts;
            Record(surface + "-picture", language, width, height, new { WithNewNotice = facts.PictureRow, WithNewNoticeDetailsOpen = facts.ExpandedPictureRow, NewNoticeHeight = facts.NoticeHeight, WithOldNotice = baseline.Row, OldNoticeHeight = baseline.Notice, WithoutNotice = none });
            facts.PictureRow.ShouldBeGreaterThan(0);
            facts.PictureRow.ShouldBe(baseline.Row, 0.01,
                "failure guidance must not reduce the preview/crop region");
            facts.ExpandedPictureRow.ShouldBe(baseline.Row, 0.01,
                "opening the code must not reduce the preview/crop region");
            none.ShouldBe(baseline.Row, 0.01, "an empty notice already occupies one line");
        }

    }

    private sealed record Facts(bool Wraps, Rect TextBounds, bool DetailsShown, bool CollapsedAtFirst, string Header,
        string CodeText, bool CodeShown, Rect CodeBounds, bool CodeReachable, double PictureRow, double ExpandedPictureRow,
        double NoticeHeight);

    /// <summary>One representative failure per touched screen class: the longest wording it can show.</summary>
    private static async Task<(Func<UserControl>, object, string, Func<int>)> ArrangeAsync(
        string surface, SessionServiceHarness h, SettingsScreenHarness? settingsHarness)
    {
        switch (surface)
        {
            case "session":
            case "session-specialized":
            {
                var (service, screen, _) = await FailureGuidanceUiTests.AtOriginalConfirmationAsync(h);
                FailureCode code = surface == "session-specialized" ? FailureCode.AdapterUnavailable : FailureCode.Cancelled;
                service.NextExecuteFailure = OperationFailure.Create(code, "Scripted processor failure.", isRetryable: true,
                    messageKey: surface == "session-specialized" ? "Failure_OperationFaulted" : null);
                await screen.ConfirmOriginalCommand.ExecuteAsync(null);
                return (() => new SessionScreenView { DataContext = screen }, screen, code.ToString(), () => service.MutatingCalls);
            }
            case "session-trim":
            {
                var (_, screen, review) = await TrimAdjustmentUiTests.OpenAsync(h, enhance: true);
                var aggregate = (await h.Repository.LoadAsync(review.Id, default)).Value!;
                var source = aggregate.Revisions.Single(r => r.Id == review.TrimAdjustment!.PreTrimRevisionId);
                screen.BeginTrimAdjustCommand.Execute(null);
                string path = h.FileWorkspace.ResolveAbsolute(source.File);
                byte[] bytes = File.ReadAllBytes(path);
                bytes[^1] ^= 0xFF;
                File.WriteAllBytes(path, bytes);
                await screen.UseThisTrimCommand.ExecuteAsync(null);
                screen.Notice!.ShouldContain(Strings.Session_TrimAdjustClosed);
                screen.NoticeErrorCode.ShouldBe(nameof(FailureCode.RevisionIntegrityMismatch));
                return (() => new SessionScreenView { DataContext = screen }, screen, nameof(FailureCode.RevisionIntegrityMismatch), () => 0);
            }
            case "correction-prepare":
            case "correction-import":
            {
                SessionService real = CorrectionFixtures.Service(h);
                SessionView review = await CorrectionFixtures.AtBackgroundRemovalReviewAsync(h, real);
                SessionView initial = surface == "correction-import"
                    ? await CorrectionFixtures.MustAsync(CorrectionFixtures.RequestAsync(real, review)) : review;
                ScriptedFailureSessionService service = new(real);
                ColleagueCorrectionUiTests.RecordingPicker picker = new() { Next = h.WriteSourcePng("correction.png") };
                SessionViewModel screen = new(service, h.Previews, h.TiffReviews, new RecordingNavigation(), picker);
                screen.Open(initial);
                await screen.PreviewsLoaded;
                if (surface == "correction-prepare")
                {
                    screen.BeginAskColleagueCommand.Execute(null);
                    service.NextPrepareFailure = OperationFailure.Create(FailureCode.WorkspaceError, "Scripted prepare failure.");
                    await screen.PrepareCorrectionFilesCommand.ExecuteAsync(null);
                }
                else
                {
                    service.NextCorrectionImportFailure = OperationFailure.Create(FailureCode.WorkspaceError, "Scripted import failure.");
                    await screen.ImportCorrectedImageCommand.ExecuteAsync(null);
                }
                screen.CorrectionMessage.ShouldNotBeNullOrWhiteSpace();
                return (() => new SessionScreenView { DataContext = screen }, screen, nameof(FailureCode.WorkspaceError), () => service.MutatingCalls);
            }
            case "home":
            {
                SessionId id = await RecoverySurfaceTests.Seed(h, "layout");
                ScriptedFailureSessionService service = new(h.CreateService());
                HomeViewModel home = new(service, h.Previews, new RecordingNavigation(), new StubFilePicker(), new StartupStatusAccessor(), new ReadinessObservationAccessor());
                await home.RefreshCommand.ExecuteAsync(null);
                service.NextRecoveryFailure = OperationFailure.Create(FailureCode.PersistenceError, "Scripted commit failure.");
                await home.RestartRecoveryCommand.ExecuteAsync(home.RecoverySessions.Single(r => r.Id == id));
                return (() => new HomeView { DataContext = home }, home, nameof(FailureCode.PersistenceError), () => service.MutatingCalls);
            }
            case "selection":
            {
                SessionView imported = await FailureGuidanceUiTests.ImportAsync(h, "layout");
                ScriptedFailureSessionService service = new(h.CreateService());
                WorkflowSelectionViewModel selection = new(service, h.Previews, new RecordingNavigation());
                selection.Open(imported);
                service.NextExecuteFailure = OperationFailure.Create(FailureCode.PreconditionNotMet, "Scripted refusal.");
                await selection.SelectCommand.ExecuteAsync(selection.Workflows.First());
                return (() => new WorkflowSelectionView { DataContext = selection }, selection,
                    nameof(FailureCode.PreconditionNotMet), () => service.MutatingCalls);
            }
            case "settings":
            {
                settingsHarness!.Localisation.Use(PrintFlow.App.Resources.OperatorCulture.Current.Name.StartsWith("zh", StringComparison.Ordinal)
                    ? Localisation.OperatorLanguage.SimplifiedChinese : Localisation.OperatorLanguage.English);
                SettingsViewModel screen = await settingsHarness.OpenAsync(new RefusingSettings(settingsHarness.Settings));
                await screen.ApplyCommand.ExecuteAsync(null);
                return (() => new SettingsView { DataContext = screen }, screen, nameof(FailureCode.PersistenceError), () => 0);
            }
            default:
            {
                SessionId id = await FailureGuidanceUiTests.FailedEnhancementAsync(h);
                ScriptedFailureSessionService service = new(h.CreateService());
                AttemptId attempt = (await service.LoadAsync(id, default)).Value.CurrentFailureAttemptId!.Value;
                ErrorDetailsViewModel details = new(service, new RecordingNavigation(), new Localisation.LocalisationService(h.Settings));
                await details.OpenAsync(id, attempt, default);
                service.NextErrorRecoveryFailure = OperationFailure.Create(FailureCode.PreconditionNotMet, "Scripted refusal.");
                await details.RetryCommand.ExecuteAsync(null);
                return (() => new PrintFlow.App.Views.ErrorDetailsView { DataContext = details }, details, nameof(FailureCode.PreconditionNotMet),
                    () => service.MutatingCalls);
            }
        }
    }

    private static string Notice(object viewModel) => viewModel switch
    {
        SessionViewModel s => s.Notice ?? s.CorrectionMessage!,
        HomeViewModel s => s.Notice!,
        WorkflowSelectionViewModel s => s.Notice!,
        SettingsViewModel s => s.Notice!,
        ErrorDetailsViewModel s => s.Notice!,
        _ => throw new ArgumentOutOfRangeException(nameof(viewModel)),
    };

    /// <summary>The session screen's picture-and-details row (the root grid's star row).</summary>
    private static double PictureRowHeight(RenderedTree tree)
    {
        if (tree.Root is not SessionScreenView) return 0;
        Grid root = tree.OfType<Grid>().First(g => g.RowDefinitions.Count == 6);
        return root.Children.OfType<Grid>().Single(g => Grid.GetRow(g) == 4).ActualHeight;
    }

    private static void Settle(RenderedTree tree)
    {
        tree.Root.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
        tree.Root.UpdateLayout();
    }

    private static Rect Bounds(FrameworkElement element, Visual root) =>
        element.TransformToAncestor(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

    private static bool Shown(DependencyObject element)
    {
        for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is UIElement { Visibility: not Visibility.Visible }) return false;
        return true;
    }

    private static T? Ancestor<T>(DependencyObject element) where T : DependencyObject
    {
        for (DependencyObject? node = VisualTreeHelper.GetParent(element); node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is T found) return found;
        return null;
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (DependencyObject descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Record(string surface, string language, double width, double height, object facts)
    {
        string? destination = Environment.GetEnvironmentVariable("PF_11151_LAYOUT_DIR");
        if (string.IsNullOrWhiteSpace(destination)) return;
        Directory.CreateDirectory(destination);
        File.WriteAllText(Path.Combine(destination, $"layout-{surface}-{language}-{width:0}x{height:0}.json"),
            JsonSerializer.Serialize(new { surface, language, width, height, facts = facts.ToString() }));
    }

    private static void Capture(Func<UserControl> view, Size viewport, string name, bool expand)
    {
        string? destination = Environment.GetEnvironmentVariable("PF_11151_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(destination)) return;
        WpfRendering.CapturePng(view, viewport, Path.Combine(destination, name), tree =>
        {
            Settle(tree);
            if (expand)
                foreach (Expander e in tree.OfType<Expander>().Where(e => AutomationProperties.GetAutomationId(e) == "NoticeErrorDetails"))
                    e.IsExpanded = true;
            Settle(tree);
            foreach (Expander e in tree.OfType<Expander>().Where(e => AutomationProperties.GetAutomationId(e) == "NoticeErrorDetails" && Shown(e)))
                e.BringIntoView();
            Settle(tree);
        });
    }

    /// <summary>The real store for reads; the write fails as a rolled-back SQLite write reports.</summary>
    private sealed class RefusingSettings(ISettingsRepository inner) : ISettingsRepository
    {
        public Task<OperationResult<SettingEntry?>> ReadAsync(SettingKey key, CancellationToken cancellationToken) =>
            inner.ReadAsync(key, cancellationToken);

        public Task<OperationResult<IReadOnlyList<SettingEntry>>> ReadAllAsync(CancellationToken cancellationToken) =>
            inner.ReadAllAsync(cancellationToken);

        public Task<OperationResult<PrintFlow.Domain.Results.Unit>> UpsertAsync(
            IReadOnlyList<SettingEntry> entries, CancellationToken cancellationToken) =>
            Task.FromResult(OperationResult.Fail<PrintFlow.Domain.Results.Unit>(
                FailureCode.PersistenceError, "Scripted: the settings write failed and was rolled back."));
    }
}
