using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using PrintFlow.App.Localisation;
using PrintFlow.App.Resources;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// SCRUM-11149: the "What to check" section beside each review. It is read-only guidance — two to
/// four checks that use the tools already on screen, what Approve and Reject actually do, and a
/// help line whose button, where there is one, is the existing entry with its own eligibility.
/// Real session service over the harness's GUID-owned workspace and database, existing fakes and
/// off-screen WPF only.
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class ReviewGuidanceUiTests
{
    public static TheoryData<string, string> StepsAndLanguages()
    {
        TheoryData<string, string> cases = new();
        foreach (string step in ReviewGuidanceSetup.Steps)
        {
            cases.Add(step, "en");
            cases.Add(step, "zh-CN");
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(StepsAndLanguages))]
    public async Task Each_review_shows_its_checks_decision_explanation_and_help_in_the_operator_language(string step, string language)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        SessionViewModel screen = (await ReviewGuidanceSetup.OpenAtAsync(h, step)).Screen;

        screen.ShowsReviewGuidance.ShouldBeTrue();
        screen.ReviewGuidance.ShouldBe(step switch
        {
            "enhancement" => ReviewGuidanceStep.Enhancement,
            "background" => ReviewGuidanceStep.BackgroundRemoval,
            "trim" => ReviewGuidanceStep.Trim,
            _ => ReviewGuidanceStep.ProductionTiff,
        });
        screen.ReviewGuidanceHeading.ShouldBe(language == "en" ? "What to check" : "检查要点");
        screen.ReviewGuidanceChecks.Count.ShouldBeInRange(2, 4);
        screen.ReviewGuidanceChecks.ShouldAllBe(check => !string.IsNullOrWhiteSpace(check));
        screen.ReviewGuidanceChecks.Distinct().Count().ShouldBe(screen.ReviewGuidanceChecks.Count);
        screen.ReviewGuidanceApprove.ShouldContain(screen.ApproveLabel);
        screen.ReviewGuidanceReject.ShouldContain(screen.RejectLabel);
        screen.ReviewGuidanceHelp.ShouldNotBeNullOrWhiteSpace();

        string[] all = [.. screen.ReviewGuidanceChecks, screen.ReviewGuidanceApprove, screen.ReviewGuidanceReject, screen.ReviewGuidanceHelp];
        foreach (string text in all)
        {
            bool chinese = text.Any(c => c is >= '一' and <= '鿿');
            chinese.ShouldBe(language == "zh-CN", $"'{text}' is in the operator language");
            text.ShouldNotContain("{");
            text.ShouldNotContain(screen.ReviewTargetIdentity!, Case.Insensitive, "no internal identity is an instruction");
        }

        // Every approval is described as a review decision, never as a save or a quality promise.
        screen.ReviewGuidanceApprove.ShouldContain(language == "en" ? "does not save" : "不会把文件保存");

        switch (step)
        {
            case "enhancement":
                screen.ReviewGuidanceChecks.ShouldContain(c => c.Contains(Strings.Session_ReviewModeSideBySide) && c.Contains(Strings.Session_ReviewModeSlider));
                screen.ReviewGuidanceHelp.ShouldNotContain(screen.AskColleagueLabel, Case.Insensitive, "colleague correction is background removal only");
                (screen.ShowsGuidanceAskColleague, screen.ShowsGuidanceAdjustTrim).ShouldBe((false, false));
                break;
            case "background":
                screen.ReviewGuidanceChecks.ShouldContain(c => c.Contains(Strings.Session_ReviewBackgroundWhite) && c.Contains(Strings.Session_ReviewBackgroundBlack));
                screen.ShowsGuidanceAskColleague.ShouldBeTrue("the delivered colleague correction is the help action (AC3)");
                screen.ShowsGuidanceAdjustTrim.ShouldBeFalse();
                screen.ReviewGuidanceHelp.ShouldNotContain(screen.HandOffLabel);
                break;
            case "trim":
                screen.ReviewGuidanceChecks.ShouldContain(c => c.Contains(screen.BeforeLabel) && c.Contains(screen.AfterLabel));
                screen.ShowsGuidanceAdjustTrim.ShouldBeTrue();
                screen.ShowsGuidanceAskColleague.ShouldBeFalse();
                screen.ReviewGuidanceHelp.ShouldContain(screen.TrimAdjustBeginLabel);
                screen.ReviewGuidanceHelp.ShouldNotContain(screen.RejectLabel, Case.Sensitive, "Reject is not the way to open the editor");
                break;
            default:
                screen.ReviewGuidanceChecks.ShouldContain(c => c.Contains(Strings.Session_TiffModeWhiteInk));
                screen.ReviewGuidanceChecks.ShouldContain(c => c.Contains(Strings.Session_TiffModeOverlay));
                screen.ReviewGuidanceChecks.ShouldContain(c => c.Contains(Strings.Session_TiffLabelPhysicalSize));
                (screen.ShowsGuidanceAskColleague, screen.ShowsGuidanceAdjustTrim).ShouldBe((false, false));
                screen.ReviewGuidanceApprove.ShouldContain(language == "en" ? "print quality" : "打印质量");
                screen.ReviewGuidanceHelp.ShouldContain(screen.ReturnHeading, Case.Sensitive, "the print-size return target is offered here");
                screen.ReviewGuidanceHelp.ShouldNotContain("  ");
                screen.ReviewGuidanceHelp.ShouldNotContain("。 ");
                break;
        }
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("zh-CN", false)]
    public async Task Tiff_reject_wording_matches_recycle_first_and_no_new_tiff_until_run_step(string language, bool retryFirst)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        (_, SessionViewModel screen, SessionView review) = await ReviewGuidanceSetup.OpenAtAsync(h, "tiff");

        screen.ReviewGuidanceReject.ShouldContain(screen.RejectLabel);
        screen.ReviewGuidanceReject.ShouldContain(screen.RetryLabel);
        screen.ReviewGuidanceReject.ShouldContain(screen.RunStepLabel);
        screen.ReviewGuidanceReject.ShouldContain(language == "en" ? "Recycle Bin" : "回收站");
        screen.ReviewGuidanceReject.ShouldContain(language == "en" ? "does not make the new TIFF straight away" : "不会立即生成新的 TIFF");

        // A refused disposal records nothing and leaves the TIFF under review — the last sentence.
        h.RecycleBin.FailsWith = FailureCode.OutputMissing;
        await screen.RejectCommand.ExecuteAsync(null);
        SessionAggregate refused = await FinalSaveFixtures.LoadAsync(h, review.Id);
        refused.Reviews.ShouldNotContain(r => r.Step == StepKind.PhotoshopOutput, "a refused disposal records no rejection");
        File.Exists(h.FileWorkspace.ResolveAbsolute(refused.Revisions.Single(r => r.Id == review.CurrentArtefact!.RevisionId).File)).ShouldBeTrue("the TIFF stays where it is");
        refused.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State.ShouldBe(StepState.ReviewRequired);
        screen.ShowsReviewGuidance.ShouldBeTrue("the same TIFF is still under review");

        // Recycled and rejected; no new TIFF exists until Run step, whether or not Retry came first.
        h.RecycleBin.FailsWith = null;
        int attempts = refused.Attempts.Count;
        await screen.RejectCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();
        SessionAggregate rejected = await FinalSaveFixtures.LoadAsync(h, review.Id);
        rejected.Steps.Single(s => s.Step == StepKind.PhotoshopOutput).State.ShouldBe(StepState.RetryRequired);
        rejected.Attempts.Count.ShouldBe(attempts, "Reject makes no new TIFF");
        rejected.Outputs.ShouldHaveSingleItem().RecycledAtUtc.ShouldNotBeNull();
        screen.ShowsReviewGuidance.ShouldBeFalse("nothing is under review now");
        (screen.CanRetry, screen.CanRunStep).ShouldBe((true, true), "both existing ways on are offered");

        if (retryFirst)
        {
            await screen.RetryCommand.ExecuteAsync(null);
            (await FinalSaveFixtures.LoadAsync(h, review.Id)).Attempts.Count.ShouldBe(attempts, "Retry alone makes no new TIFF");
            screen.CanRunStep.ShouldBeTrue();
        }

        await screen.RunStepCommand.ExecuteAsync(null);
        screen.Notice.ShouldBeNull();
        (await FinalSaveFixtures.LoadAsync(h, review.Id)).Attempts.Count.ShouldBe(attempts + 1, "Run step makes the next TIFF");
        screen.ReviewGuidance.ShouldBe(ReviewGuidanceStep.ProductionTiff, "the new TIFF is reviewed with the same guidance");
        screen.ReviewTargetIdentity.ShouldNotBeNull().ShouldNotContain(review.CurrentArtefact!.RevisionId.ToString(), Case.Insensitive);
    }

    [Fact]
    public async Task Exclusive_modes_hide_the_guidance_and_closing_them_brings_back_the_same_review()
    {
        using OperatorCultureScope culture = new("en");
        using SessionServiceHarness h = new();
        SessionViewModel background = (await ReviewGuidanceSetup.OpenAtAsync(h, "background")).Screen;
        List<string?> changed = [];
        background.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        background.BeginAskColleagueCommand.Execute(null);
        background.ShowsReviewGuidance.ShouldBeFalse("the Ask panel is the task while it is open");
        background.ShowsGuidanceAskColleague.ShouldBeFalse();
        changed.ShouldContain(nameof(SessionViewModel.ShowsReviewGuidance));
        background.CancelAskColleagueCommand.Execute(null);
        background.ShowsReviewGuidance.ShouldBeTrue();

        using SessionServiceHarness t = new();
        SessionViewModel trim = (await ReviewGuidanceSetup.OpenAtAsync(t, "trim")).Screen;
        trim.BeginTrimAdjustCommand.Execute(null);
        trim.IsAdjustingTrim.ShouldBeTrue();
        trim.ShowsReviewGuidance.ShouldBeFalse("the trim editor is the task while it is open");
        trim.ShowsGuidanceAdjustTrim.ShouldBeFalse();
        trim.CancelTrimAdjustCommand.Execute(null);
        trim.ShowsReviewGuidance.ShouldBeTrue();
        trim.ReviewGuidance.ShouldBe(ReviewGuidanceStep.Trim);
    }

    [Fact]
    public async Task Approving_moves_the_guidance_off_the_old_review_and_it_never_describes_a_non_review_state()
    {
        using OperatorCultureScope culture = new("en");
        using SessionServiceHarness h = new();
        SessionViewModel screen = (await ReviewGuidanceSetup.OpenAtAsync(h, "enhancement")).Screen;
        screen.ReviewGuidance.ShouldBe(ReviewGuidanceStep.Enhancement);

        await screen.ApproveCommand.ExecuteAsync(null);

        screen.Notice.ShouldBeNull();
        screen.CurrentStep.ShouldNotBe(DisplayNames.Step(StepKind.Enhancement));
        screen.IsReviewRequired.ShouldBeFalse();
        (screen.ShowsReviewGuidance, screen.ReviewGuidance).ShouldBe((false, ReviewGuidanceStep.None));
        screen.ReviewGuidanceChecks.ShouldBeEmpty();
        (screen.ShowsGuidanceAskColleague, screen.ShowsGuidanceAdjustTrim).ShouldBe((false, false));
    }

    [Fact]
    public async Task A_language_change_refreshes_the_guidance_without_touching_the_session()
    {
        using OperatorCultureScope culture = new("en");
        using SessionServiceHarness h = new();
        LocalisationService localisation = new(h.Settings);
        localisation.Use(OperatorLanguage.English);
        (_, SessionViewModel screen, SessionView review) = await ReviewGuidanceSetup.OpenAtAsync(h, "background", localisation);
        string before = await FingerprintAsync(h, review.Id);
        IReadOnlyList<string> english = screen.ReviewGuidanceChecks;
        List<string?> changed = [];
        screen.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        localisation.Use(OperatorLanguage.SimplifiedChinese);

        changed.ShouldContain(string.Empty, "the screen received the language event");
        screen.ReviewGuidanceHeading.ShouldBe("检查要点");
        screen.ReviewGuidanceChecks.ShouldNotBe(english);
        screen.ReviewGuidanceChecks.ShouldAllBe(c => c.Any(ch => ch >= '一' && ch <= '鿿'));
        screen.ReviewGuidanceHelp.ShouldContain("同事");
        (screen.IsAskingColleague, screen.IsAdjustingTrim, screen.IsBusy).ShouldBe((false, false, false));
        (await FingerprintAsync(h, review.Id)).ShouldBe(before);
    }

    [Theory]
    [InlineData("background")]
    [InlineData("trim")]
    public async Task The_help_button_is_the_existing_entry_and_opening_it_writes_nothing(string step)
    {
        using OperatorCultureScope culture = new("en");
        using SessionServiceHarness h = new();
        (_, SessionViewModel screen, SessionView review) = await ReviewGuidanceSetup.OpenAtAsync(h, step);
        string before = await FingerprintAsync(h, review.Id);
        string guidanceId = step == "background" ? "Session.ReviewGuidance.AskColleague" : "Session.ReviewGuidance.AdjustTrim";
        string barId = step == "background" ? "Session.AskColleague" : "Session.TrimAdjust.Begin";

        var facts = WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = screen }, WpfRendering.ReviewViewport, tree =>
        {
            tree.Root.UpdateLayout();
            Button help = tree.OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == guidanceId);
            Button bar = tree.OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == barId);
            bool shown = ReviewGuidanceLayoutTests.Shown(help) && help.IsEnabled;
            bool sameCommand = ReferenceEquals(help.Command, bar.Command);
            bool sameName = AutomationProperties.GetName(help) == AutomationProperties.GetName(bar) && Equals(help.Content, bar.Content);
            bool plain = help is not ReviewApprovalButton && !help.IsDefault;
            bool inside = ReviewGuidanceLayoutTests.IsInside(help,
                tree.OfType<FrameworkElement>().Single(e => AutomationProperties.GetAutomationId(e) == "Session.ReviewGuidance"));
            help.Command.Execute(help.CommandParameter);
            return (shown, sameCommand, sameName, plain, inside);
        });

        facts.Facts.ShouldBe((true, true, true, true, true));
        if (step == "background")
        {
            screen.IsAskingColleague.ShouldBeTrue("the button opens the existing Ask panel");
            screen.CorrectionFocusTarget.ShouldBe(CorrectionFocus.NoteBox);
            screen.IsHandedOff.ShouldBeFalse("opening the panel prepares and hands off nothing");
        }
        else
        {
            screen.IsAdjustingTrim.ShouldBeTrue("the button opens the existing trim editor");
        }

        (screen.CanApprove, screen.CanReject).ShouldBe((false, false), "the open mode holds the review decisions, as it always has");
        (await FingerprintAsync(h, review.Id)).ShouldBe(before, "opening help approves, rejects, prepares, crops and saves nothing");
    }

    [Fact]
    public async Task While_a_command_is_in_flight_the_help_action_is_unavailable_and_approval_binding_is_unchanged()
    {
        using OperatorCultureScope culture = new("en");
        using SessionServiceHarness h = new();
        SessionViewModel screen = (await ReviewGuidanceSetup.OpenAtAsync(h, "background")).Screen;
        string? approveTarget = screen.ReviewTargetIdentity;
        string? rejectTarget = screen.RejectTargetIdentity;

        screen.IsBusy = true;
        screen.ShowsReviewGuidance.ShouldBeTrue("the review on screen is still the review");
        screen.ShowsGuidanceAskColleague.ShouldBeTrue();
        screen.CanAskColleague.ShouldBeFalse("the button is disabled through the entry's own eligibility");
        screen.BeginAskColleagueCommand.Execute(null);
        screen.IsAskingColleague.ShouldBeFalse("the entry refuses while busy");
        screen.IsBusy = false;

        (screen.ReviewTargetIdentity, screen.RejectTargetIdentity).ShouldBe((approveTarget, rejectTarget));
        screen.IsRejectGestureGuarded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("en", "Trim")]
    [InlineData("zh-CN", "裁边")]
    public async Task The_returned_correction_review_keeps_the_11148_next_step_line_and_the_guidance_does_not_repeat_it(
        string language, string trim)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        ReviewGuidanceSetup.Opened opened = await ReviewGuidanceSetup.OpenAtAsync(h, "background");
        SessionViewModel screen = opened.Screen;
        screen.BeginAskColleagueCommand.Execute(null);
        await screen.PrepareCorrectionFilesCommand.ExecuteAsync(null);
        screen.ShowsReviewGuidance.ShouldBeFalse("handed off: the correction panel is the task, not a review");
        opened.Picker.Next = CorrectionFixtures.CorrectedPng(h);

        await screen.ImportCorrectedImageCommand.ExecuteAsync(null);

        screen.IsReviewRequired.ShouldBeTrue();
        screen.ReviewGuidance.ShouldBe(ReviewGuidanceStep.BackgroundRemoval);
        screen.NextStepText.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.Session_CorrectionReview, trim),
            "the SCRUM-11148 review-then-next-step line is unchanged");
        string[] guidance = [.. screen.ReviewGuidanceChecks, screen.ReviewGuidanceApprove, screen.ReviewGuidanceReject, screen.ReviewGuidanceHelp];
        guidance.ShouldAllBe(text => !text.Contains(trim), "the next step is named once, by the status line");
        screen.ReviewGuidanceReject.ShouldContain(language == "en" ? "Nothing runs again by itself" : "不会自动重新处理");
        screen.ShowsGuidanceAskColleague.ShouldBe(screen.CanAskColleague, "a new round is offered exactly when the entry is");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public async Task Without_the_colleague_entry_the_background_help_names_only_Reject(string language)
    {
        using OperatorCultureScope culture = new(language);
        using SessionServiceHarness h = new();
        SessionViewModel screen = (await ReviewGuidanceSetup.OpenAtAsync(h, "background-no-colleague")).Screen;

        screen.ReviewGuidance.ShouldBe(ReviewGuidanceStep.BackgroundRemoval);
        (screen.CanAskColleague, screen.CanHandOff, screen.ShowsGuidanceAskColleague).ShouldBe((false, false, false));
        screen.ReviewGuidanceHelp.ShouldBe(string.Format(CultureInfo.CurrentCulture, Strings.Session_GuidanceHelpNoColleague, screen.RejectLabel));
        screen.ReviewGuidanceHelp.ShouldNotContain(screen.AskColleagueLabel);
        screen.ReviewGuidanceHelp.ShouldNotContain(screen.HandOffLabel);
    }

    private static async Task<string> FingerprintAsync(SessionServiceHarness h, SessionId id)
    {
        SessionAggregate aggregate = await FinalSaveFixtures.LoadAsync(h, id);
        return JsonSerializer.Serialize(new
        {
            aggregate.Session, aggregate.Steps, aggregate.Revisions, aggregate.Attempts, aggregate.Reviews,
            aggregate.CorrectionRequests, aggregate.Outputs,
            Files = Directory.GetFiles(h.FileWorkspace.ResolveAbsoluteDirectory(aggregate.Session.Workspace), "*", SearchOption.AllDirectories).Order(),
        });
    }
}
