using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using PrintFlow.App.Navigation;
using PrintFlow.App.Startup;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Delivery;
using PrintFlow.Workflow.Ports;
using PrintFlow.Workflow.Services;
using static PrintFlow.Tests.Fixtures.CorrectionFixtures;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// SCRUM-11148 on the Session and Home screens: the S0–S7 interaction, focus requests, the Home
/// navigation-only card and the Recent waiting line, with off-screen renders in both languages.
/// Stub picker, recording folder shell and off-screen WPF only — no window, UIA, Explorer or input.
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class ColleagueCorrectionUiTests
{
    [Fact]
    public async Task Ask_sits_beside_Approve_and_Reject_and_opening_or_cancelling_it_writes_nothing()
    {
        using OperatorCultureScope culture = new("en");
        using SessionServiceHarness h = new();
        (SessionService service, SessionViewModel screen, SessionView review, _, _) = await OpenAtReviewAsync(h);

        screen.CanAskColleague.ShouldBeTrue();
        screen.CanApprove.ShouldBeTrue();
        screen.CanReject.ShouldBeTrue();
        screen.CanHandOff.ShouldBeFalse("the generic hand-off is replaced on background-removal review (D4)");
        screen.IsRejectGestureGuarded.ShouldBeTrue();
        screen.RejectTargetIdentity.ShouldNotBeNull();
        string before = await FingerprintAsync(h, review.Id);

        screen.BeginAskColleagueCommand.Execute(null);

        screen.IsAskingColleague.ShouldBeTrue();
        screen.CorrectionFocusTarget.ShouldBe(CorrectionFocus.NoteBox);
        (screen.CanApprove, screen.CanReject, screen.CanRunStep, screen.CanReturnToStep, screen.CanAskColleague)
            .ShouldBe((false, false, false, false, false), "consequential review actions wait while the panel is open");
        screen.AskIdentity.ShouldNotBeNull();
        screen.NextStepText.ShouldContain(screen.CorrectionPrepareLabel);

        screen.CorrectionNote = "typed but not sent";
        screen.CancelAskColleagueCommand.Execute(null);

        screen.IsAskingColleague.ShouldBeFalse();
        screen.CorrectionFocusTarget.ShouldBe(CorrectionFocus.AskButton);
        screen.CanApprove.ShouldBeTrue();
        (await FingerprintAsync(h, review.Id)).ShouldBe(before, "opening, typing and cancelling write nothing");
    }

    [Theory]
    [InlineData("en", "do not edit", "Import corrected image")]
    [InlineData("zh-CN", "请勿修改", "导入修正后的图片")]
    public async Task Preparing_without_a_note_shows_the_files_folder_format_and_return_steps(string language, string doNotEdit, string import)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        (_, SessionViewModel screen, SessionView review, _, RecordingCorrectionShell shell) = await OpenAtReviewAsync(h);

        screen.BeginAskColleagueCommand.Execute(null);
        await screen.PrepareCorrectionFilesCommand.ExecuteAsync(null);

        screen.IsHandedOff.ShouldBeTrue();
        screen.IsAskingColleague.ShouldBeFalse();
        screen.ShowsCorrectionPanel.ShouldBeTrue();
        screen.CorrectionFocusTarget.ShouldBe(CorrectionFocus.HandedOffHeading);
        screen.CorrectionReferenceText.ShouldContain(doNotEdit);
        screen.CorrectionReferenceText.ShouldContain(screen.CorrectionHandoff!.ReferenceFileName);
        screen.CorrectionWorkingText.ShouldContain(screen.CorrectionHandoff.WorkingFileName);
        screen.CorrectionFolderText.ShouldContain(screen.CorrectionHandoff.FolderPath);
        screen.CorrectionStep3Text.ShouldContain("PNG");
        screen.CorrectionStep3Text.ShouldContain("6 × 5");
        screen.CorrectionStep2Text.ShouldContain(language == "en" ? "No note" : "无备注");
        screen.CorrectionStep4Text.ShouldContain(import);
        screen.NextStepText.ShouldContain(import);
        screen.RecommendedCommand.ShouldBe(screen.ImportCorrectedImageCommand);
        screen.CanSubmitManualResult.ShouldBeFalse("one import action only");
        screen.CanReject.ShouldBeTrue("the special handed-off Reject stays an existing alternative");
        screen.StatusHeading.ShouldBe(language == "en" ? "Handed off" : "已移交");

        screen.OpenCorrectionFolderCommand.Execute(null);
        shell.Opened.ShouldHaveSingleItem().ShouldBe((screen.CorrectionHandoff.FolderPath, screen.CorrectionHandoff.WorkingFileName));
        screen.IsHandedOff.ShouldBeTrue();
    }

    [Fact]
    public async Task Import_uses_the_folder_as_the_starting_place_and_a_cancelled_picker_writes_nothing()
    {
        using OperatorCultureScope culture = new("en");
        using SessionServiceHarness h = new();
        (_, SessionViewModel screen, SessionView review, RecordingPicker picker, _) = await OpenAtReviewAsync(h);
        screen.BeginAskColleagueCommand.Execute(null);
        await screen.PrepareCorrectionFilesCommand.ExecuteAsync(null);
        string before = await FingerprintAsync(h, review.Id);

        await screen.ImportCorrectedImageCommand.ExecuteAsync(null);

        picker.InitialFolders.ShouldHaveSingleItem().ShouldBe(screen.CorrectionHandoff!.FolderPath);
        screen.CorrectionFocusTarget.ShouldBe(CorrectionFocus.ImportButton);
        (await FingerprintAsync(h, review.Id)).ShouldBe(before);

        picker.Next = h.Workspace.CreateSourceFile("flat.png", SyntheticImages.PngWithAlpha(6, 5, (_, _) => 255));
        await screen.ImportCorrectedImageCommand.ExecuteAsync(null);

        screen.CorrectionMessage.ShouldNotBeNull();
        screen.CorrectionMessage.ShouldStartWith("Import refused:");
        screen.CorrectionMessage.ShouldContain("still handed off");
        screen.CorrectionFocusTarget.ShouldBe(CorrectionFocus.ImportButton);
        screen.IsHandedOff.ShouldBeTrue();
        (await FingerprintAsync(h, review.Id)).ShouldBe(before, "a refused return adds no attempt or Revision");
    }

    [Theory]
    [InlineData("en", "Trim")]
    [InlineData("zh-CN", "裁边")]
    public async Task A_successful_import_shows_R2_for_review_naming_the_next_step_and_exact_approval_continues(string language, string trim)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        (SessionService service, SessionViewModel screen, SessionView review, RecordingPicker picker, _) = await OpenAtReviewAsync(h);
        screen.BeginAskColleagueCommand.Execute(null);
        await screen.PrepareCorrectionFilesCommand.ExecuteAsync(null);
        picker.Next = CorrectedPng(h);

        await screen.ImportCorrectedImageCommand.ExecuteAsync(null);

        screen.IsHandedOff.ShouldBeFalse();
        screen.IsReviewRequired.ShouldBeTrue();
        screen.ShowsCorrectionPanel.ShouldBeFalse();
        screen.NextStepText.ShouldContain(trim);
        screen.IsCorrectionIdenticalToSent.ShouldBeFalse();
        screen.ReviewTargetIdentity.ShouldNotBeNull("the review of R2 takes focus through the ordinary new-review rule");
        screen.CanAskColleague.ShouldBeTrue("a new round may ask again with R2 as the working copy");

        await screen.ApproveCommand.ExecuteAsync(null);

        SessionView approved = (await service.LoadAsync(review.Id, default)).Value;
        approved.CurrentStep!.Step.ShouldBe(StepKind.Trim);
        approved.CurrentStep.State.ShouldBe(StepState.Waiting, "approval starts nothing");
        screen.CanAskColleague.ShouldBeFalse();
    }

    [Fact]
    public async Task A_stale_screen_of_R_cannot_approve_a_same_hash_R2()
    {
        using OperatorCultureScope culture = new("en");
        using SessionServiceHarness h = new();
        (SessionService service, SessionViewModel screen, SessionView review, _, _) = await OpenAtReviewAsync(h, opaqueSource: true);
        SessionViewModel staleApprove = new(service, h.Previews, h.TiffReviews, new RecordingNavigation());
        SessionViewModel staleReject = new(service, h.Previews, h.TiffReviews, new RecordingNavigation());
        staleApprove.Open(review);
        staleReject.Open(review);
        await staleApprove.PreviewsLoaded;
        await staleReject.PreviewsLoaded;
        SessionView handedOff = await MustAsync(RequestAsync(service, review));
        string unchanged = h.Workspace.CreateSourceFile("same.png",
            File.ReadAllBytes(Path.Combine(handedOff.Correction!.FolderPath, handedOff.Correction.WorkingFileName)));
        SessionView r2 = await MustAsync(service.ImportCorrectedImageAsync(review.Id, handedOff.Correction.RequestId,
            handedOff.Correction.HandedOutRevisionId, handedOff.Correction.HandedOutSha256, unchanged, "qa", default));
        r2.CurrentArtefact!.Sha256.ShouldBe(review.CurrentArtefact!.Sha256);

        await staleApprove.ApproveCommand.ExecuteAsync(null);
        await staleReject.RejectCommand.ExecuteAsync(null);

        SessionAggregate saved = await LoadAsync(h, review.Id);
        saved.Reviews.ShouldBeEmpty("neither the stale approve nor the stale reject applied to R2");
        saved.ToSnapshot().CurrentStep!.State.ShouldBe(StepState.ReviewRequired);
        staleApprove.Notice.ShouldNotBeNull("the refusal is said, not swallowed");
        staleApprove.ReviewTargetIdentity!.ShouldContain(r2.CurrentArtefact.RevisionId.ToString(), Case.Insensitive,
            "after the refusal the screen reloads and shows R2, the result now under review");
    }

    [Fact]
    public async Task Recent_and_Home_say_handed_off_and_waiting_and_the_card_only_opens_the_job()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        (SessionView handedOff, CorrectionHandoffView panel) = await CrashedImportAsync(h);
        SessionService service = Service(h);
        SessionView waiting = await MustAsync(RequestAsync(service, await AtBackgroundRemovalReviewAsync(h, service)));
        RecordingNavigation navigation = new();
        StubFilePicker picker = new(CorrectedPng(h));
        HomeViewModel home = new(service, h.Previews, navigation, picker, new StartupStatusAccessor(), new ReadinessObservationAccessor());
        await home.RefreshCommand.ExecuteAsync(null);

        home.RecentSessions.ShouldNotContain(r => r.Id == handedOff.Id, "a recovery job is shown once, on its card");
        RecentSessionRow recent = home.RecentSessions.Single(r => r.Id == waiting.Id);
        recent.State.ShouldBe("已移交");
        recent.HasOpenCorrection.ShouldBeTrue();
        recent.WaitingForCorrectionText.ShouldBe("等待同事修正后的图片");
        RecoverySessionRow card = home.RecoverySessions.Single();
        (card.HasOpenCorrection, card.ShowsPlainOpen, card.CanImport).ShouldBe((true, false, false));
        card.OpenCorrectionLabel.ShouldBe("打开任务以导入修正后的图片");

        await home.OpenRecoveryCommand.ExecuteAsync(card);

        picker.CallCount.ShouldBe(0);
        navigation.SessionFor!.Correction!.RequestId.ShouldBe(panel.RequestId);
    }

    // -------------------------------------------------------------------------------------
    // AC8: off-screen renders in both languages at both reference viewports
    // -------------------------------------------------------------------------------------

    public static TheoryData<string, string, double, double> SessionRenderCases => new()
    {
        { "ask", "en", 1000, 700 }, { "ask", "zh-CN", 1000, 700 }, { "ask", "en", 1920, 1040 }, { "ask", "zh-CN", 1920, 1040 },
        { "handed-off", "en", 1000, 700 }, { "handed-off", "zh-CN", 1000, 700 },
        { "handed-off", "en", 1920, 1040 }, { "handed-off", "zh-CN", 1920, 1040 },
        { "refused", "en", 1000, 700 }, { "refused", "zh-CN", 1000, 700 },
        { "review", "en", 1000, 700 }, { "review", "zh-CN", 1000, 700 },
    };

    [Theory]
    [MemberData(nameof(SessionRenderCases))]
    public async Task The_correction_panels_render_wrapped_reachable_and_worded_in_both_languages(
        string state, string language, double width, double height)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        (_, SessionViewModel screen, _, RecordingPicker picker, _) = await OpenAtReviewAsync(h);
        screen.BeginAskColleagueCommand.Execute(null);
        if (state != "ask") await screen.PrepareCorrectionFilesCommand.ExecuteAsync(null);
        if (state == "refused")
        {
            picker.Next = h.Workspace.CreateSourceFile("flat.png", SyntheticImages.PngWithAlpha(6, 5, (_, _) => 255));
            await screen.ImportCorrectedImageCommand.ExecuteAsync(null);
        }

        if (state == "review")
        {
            picker.Next = CorrectedPng(h);
            await screen.ImportCorrectedImageCommand.ExecuteAsync(null);
        }

        Size viewport = new(width, height);
        var facts = WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = screen }, viewport, tree =>
        {
            tree.Root.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
            tree.Root.UpdateLayout();
            Border? ask = tree.OfType<Border>().SingleOrDefault(b => AutomationProperties.GetAutomationId(b) == "Session.Correction.AskPanel" && Shown(b));
            Border? panel = tree.OfType<Border>().SingleOrDefault(b => AutomationProperties.GetAutomationId(b) == "Session.Correction.Panel" && Shown(b));
            FrameworkElement? shown = ask ?? panel;
            var tooWide = shown is null ? [] : tree.OfType<TextBlock>()
                .Where(t => Shown(t) && IsInside(t, shown) && t.ActualWidth > shown.ActualWidth + 0.5).ToList();
            var consequential = tree.OfType<ReviewApprovalButton>()
                .Where(b => AutomationProperties.GetAutomationId(b) is "Session.Correction.Prepare" or "Session.Correction.Import" && Shown(b))
                .Select(b => (Id: AutomationProperties.GetAutomationId(b), b.TargetIdentity, b.IsDefault, b.Focusable, b.IsTabStop)).ToList();
            ReviewApprovalButton reject = tree.OfType<ReviewApprovalButton>().Single(b => AutomationProperties.GetAutomationId(b) == "Session.Reject");
            TextBox? note = tree.OfType<TextBox>().SingleOrDefault(t => AutomationProperties.GetAutomationId(t) == "Session.Correction.Note" && Shown(t));
            return (
                Ask: ask is not null, Panel: panel is not null, TooWide: tooWide.Count, Consequential: consequential,
                AnyDefault: tree.OfType<Button>().Any(b => b.IsDefault && Shown(b)),
                RejectGuarded: reject.IsGestureGuarded,
                AskButton: tree.OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b) == "Session.AskColleague" && Shown(b)),
                HandOffButton: tree.OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b) == "Session.HandOff" && Shown(b)),
                NoteAcceptsReturn: note?.AcceptsReturn,
                Message: tree.OfType<TextBlock>().SingleOrDefault(t => AutomationProperties.GetAutomationId(t) == "Session.Correction.Message" && Shown(t))?.Text,
                Live: tree.OfType<TextBlock>().Where(t => AutomationProperties.GetAutomationId(t) == "Session.Correction.Message")
                    .Select(AutomationProperties.GetLiveSetting).SingleOrDefault(),
                Names: tree.OfType<ButtonBase>().Where(b => Shown(b) && AutomationProperties.GetAutomationId(b).StartsWith("Session.Correction.", StringComparison.Ordinal))
                    .Select(b => AutomationProperties.GetName(b)).ToList());
        });

        facts.Facts.AnyDefault.ShouldBeFalse("no implicit Enter submission anywhere");
        facts.Facts.TooWide.ShouldBe(0, "every line wraps inside its panel");
        facts.Facts.Names.ShouldAllBe(name => !string.IsNullOrWhiteSpace(name));
        facts.Facts.HandOffButton.ShouldBeFalse();
        facts.Facts.RejectGuarded.ShouldBeTrue();
        foreach (var button in facts.Facts.Consequential)
        {
            button.TargetIdentity.ShouldNotBeNull();
            (button.IsDefault, button.Focusable, button.IsTabStop).ShouldBe((false, true, true));
        }

        switch (state)
        {
            case "ask":
                (facts.Facts.Ask, facts.Facts.Panel, facts.Facts.AskButton).ShouldBe((true, false, false));
                facts.Facts.NoteAcceptsReturn.ShouldBe(false, "the note never submits on Enter");
                facts.Facts.Consequential.Select(b => b.Id).ShouldBe(["Session.Correction.Prepare"]);
                break;
            case "handed-off":
            case "refused":
                (facts.Facts.Ask, facts.Facts.Panel).ShouldBe((false, true));
                facts.Facts.Consequential.Select(b => b.Id).ShouldBe(["Session.Correction.Import"]);
                if (state == "refused")
                {
                    facts.Facts.Message.ShouldNotBeNullOrWhiteSpace();
                    facts.Facts.Live.ShouldBe(AutomationLiveSetting.Assertive);
                }

                break;
            default:
                (facts.Facts.Ask, facts.Facts.Panel, facts.Facts.AskButton).ShouldBe((false, false, true));
                break;
        }

        Capture(() => new SessionScreenView { DataContext = screen }, viewport, $"session-{state}-{language}-{width:0}x{height:0}.png");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public async Task The_Home_card_and_Recent_row_render_the_correction_wording_with_its_own_identities(string language)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        await CrashedImportAsync(h);
        await MustAsync(RequestAsync(Service(h), await AtBackgroundRemovalReviewAsync(h, Service(h))));
        HomeViewModel home = new(Service(h), h.Previews, new RecordingNavigation(), new StubFilePicker(), new StartupStatusAccessor(), new ReadinessObservationAccessor());
        await home.RefreshCommand.ExecuteAsync(null);

        var facts = WpfRendering.RenderExpectingNoBindingErrors(() => new HomeView { DataContext = home }, WpfRendering.ReviewViewport, tree =>
        {
            tree.Root.UpdateLayout();
            List<(string Id, bool Shown)> buttons = [.. tree.OfType<Button>()
                .Where(b => AutomationProperties.GetAutomationId(b).StartsWith("Home.Recovery.", StringComparison.Ordinal))
                .Select(b => (AutomationProperties.GetAutomationId(b), Shown(b)))];
            return (Buttons: buttons,
                Waiting: tree.OfType<TextBlock>().Count(t => AutomationProperties.GetAutomationId(t)
                    is "Home.Recovery.WaitingForCorrection" or "Home.RecentWaitingForCorrection" && Shown(t)));
        });

        facts.Facts.Buttons.Where(b => b.Shown).Select(b => b.Id).ShouldBe(
            ["Home.Recovery.OpenCorrection", "Home.Recovery.Restart", "Home.Recovery.Abandon"],
            "Open job to import corrected image first; no generic import, no plain Open");
        facts.Facts.Waiting.ShouldBe(2, "the card and the Recent row both say what the job waits for");
        Capture(() => new HomeView { DataContext = home }, WpfRendering.ReviewViewport, $"home-{language}-1000x700.png");
        string? recentCaptures = Environment.GetEnvironmentVariable("PF_SCRUM11153_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(recentCaptures))
            foreach (Size size in new[] { new Size(1000, 700), new Size(1920, 1040) })
                WpfRendering.CapturePng(() => new HomeView { DataContext = home }, size,
                    Path.Combine(recentCaptures, $"correction-recovery-{language}-{size.Width:0}x{size.Height:0}.png"));
    }

    [Fact]
    public void The_correction_code_has_no_watcher_timer_or_automatic_pickup()
    {
        string root = FindRoot();
        string[] files =
        [
            "src/PrintFlow.Workflow/Services/SessionService.Correction.cs",
            "src/PrintFlow.Workflow/Services/CorrectionRequestEligibility.cs",
            "src/PrintFlow.Workflow/Services/CorrectionClosing.cs",
            "src/PrintFlow.Workflow/Services/CorrectionRequest.cs",
            "src/PrintFlow.Infrastructure/Workspace/FileCorrectionPackageStore.cs",
            "src/PrintFlow.Infrastructure/Workspace/WindowsCorrectionFolderShell.cs",
            "src/PrintFlow.App/ViewModels/SessionViewModel.Correction.cs",
        ];
        foreach (string file in files)
        {
            string source = File.ReadAllText(Path.Combine(root, file));
            foreach (string banned in new[] { "FileSystemWatcher", "DispatcherTimer", "System.Threading.Timer", "PeriodicTimer", "new Timer(" })
                source.ShouldNotContain(banned, Case.Sensitive, $"{file} must not watch or poll (AC7).");
        }
    }

    // -------------------------------------------------------------------------------------

    private static async Task<(SessionService Service, SessionViewModel Screen, SessionView Review, RecordingPicker Picker, RecordingCorrectionShell Shell)>
        OpenAtReviewAsync(SessionServiceHarness h, bool opaqueSource = false)
    {
        SessionService service = Service(h);
        SessionView review = await AtBackgroundRemovalReviewAsync(h, service, opaqueSource);
        RecordingPicker picker = new();
        RecordingCorrectionShell shell = new();
        SessionViewModel screen = new(service, h.Previews, h.TiffReviews, new RecordingNavigation(),
            filePicker: picker, correctionShell: shell);
        screen.Open(review);
        await screen.PreviewsLoaded;
        return (service, screen, review, picker, shell);
    }

    private static async Task<(SessionView HandedOff, CorrectionHandoffView Panel)> CrashedImportAsync(SessionServiceHarness h)
    {
        SessionView handedOff = await MustAsync(RequestAsync(Service(h), await AtBackgroundRemovalReviewAsync(h, Service(h))));
        CorrectionHandoffView panel = handedOff.Correction!;
        ScriptedRepository crashing = new(h.Repository) { FailFromCommit = 2 };
        (await Service(h, repository: crashing).ImportCorrectedImageAsync(handedOff.Id, panel.RequestId,
            panel.HandedOutRevisionId, panel.HandedOutSha256, CorrectedPng(h), "qa", default)).IsFailure.ShouldBeTrue();
        (await h.CreateRecoveryService(new FakeProcessLiveness(ProcessLiveness.Alive)).RecoverAsync(default)).IsSuccess.ShouldBeTrue();
        return (handedOff, panel);
    }

    private static async Task<string> FingerprintAsync(SessionServiceHarness h, SessionId id)
    {
        SessionAggregate aggregate = await LoadAsync(h, id);
        return JsonSerializer.Serialize(new
        {
            aggregate.Session, aggregate.Steps, aggregate.Revisions, aggregate.Attempts, aggregate.Reviews, aggregate.CorrectionRequests,
            Files = Directory.GetFiles(h.FileWorkspace.ResolveAbsoluteDirectory(aggregate.Session.Workspace), "*", SearchOption.AllDirectories).Order(),
        });
    }

    private static void Capture(Func<UserControl> create, Size viewport, string name)
    {
        string? destination = Environment.GetEnvironmentVariable("PF_11148_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(destination))
            WpfRendering.CapturePng(create, viewport, Path.Combine(destination, name),
                tree => tree.Root.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { })));
    }

    private static bool Shown(DependencyObject element)
    {
        for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is UIElement { Visibility: not Visibility.Visible }) return false;
        return true;
    }

    private static bool IsInside(DependencyObject element, DependencyObject root)
    {
        for (DependencyObject? node = element; node is not null; node = VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, root)) return true;
        return false;
    }

    private static string FindRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "PrintFlowStudio.sln"))) current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    internal sealed class RecordingPicker : IFilePicker
    {
        public string? Next { get; set; }

        public List<string?> InitialFolders { get; } = [];

        public string? PickSingleFile(string dialogTitle, string filter) => PickSingleFile(dialogTitle, filter, null);

        public string? PickSingleFile(string dialogTitle, string filter, string? initialFolder)
        {
            InitialFolders.Add(initialFolder);
            string? next = Next;
            Next = null;
            return next;
        }
    }

    internal sealed class RecordingCorrectionShell : ICorrectionFolderShell
    {
        public List<(string Folder, string? File)> Opened { get; } = [];

        public ShellDispatchResult Open(string absoluteFolder, string? preferredFileName, string? fallbackFileName)
        {
            Opened.Add((absoluteFolder, preferredFileName));
            return new ShellDispatchResult(true);
        }
    }
}
