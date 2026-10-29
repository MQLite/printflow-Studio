using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using PrintFlow.App.Controls;
using PrintFlow.App.Views;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class PlainChineseReviewCopyTests
{
    [Fact]
    public async Task Returned_correction_guidance_names_the_actual_review_action()
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        var opened = await ReviewGuidanceSetup.OpenAtAsync(h, "background");
        var model = opened.Screen;
        model.BeginAskColleagueCommand.Execute(null);
        await model.PrepareCorrectionFilesCommand.ExecuteAsync(null);
        opened.Picker.Next = CorrectionFixtures.CorrectedPng(h);
        await model.ImportCorrectedImageCommand.ExecuteAsync(null);
        model.NextStepText.ShouldContain(model.ApproveLabel);
        model.NextStepText.ShouldContain("不会重新去除背景");
        model.IsReviewRequired.ShouldBeTrue();
        model.CanApprove.ShouldBeTrue();
    }

    public static TheoryData<string, int, int> Reviews()
    {
        TheoryData<string, int, int> cases = new();
        foreach (string step in ReviewGuidanceSetup.Steps)
            foreach ((int width, int height) in new[] { (984, 661), (1280, 720), (1920, 1080) })
                cases.Add(step, width, height);
        return cases;
    }

    [Theory]
    [MemberData(nameof(Reviews))]
    public async Task Review_action_names_its_object_fits_and_keeps_exact_target(string step, int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using SessionServiceHarness h = new();
        var opened = await ReviewGuidanceSetup.OpenAtAsync(h, step);
        var model = opened.Screen;
        await model.PreviewsLoaded;
        await model.FinalSaveFactsLoaded;
        string? identity = model.ReviewTargetIdentity;

        // An approval must say which object was checked, independently of any save action.
        model.ApproveLabel.ShouldContain("确认");
        model.ApproveLabel.ShouldContain(step == "tiff" ? "TIFF" : "图");
        model.ApproveLabel.ShouldContain("检查");
        model.ReviewGuidanceApprove.ShouldContain(model.ApproveLabel);
        model.ReviewGuidanceApprove.ShouldContain("不会把文件保存");

        WpfRendering.RenderExpectingNoBindingErrors(() => new SessionScreenView { DataContext = model }, new(width, height), tree =>
        {
            var button = GuidedSessionShellTests.Find<ReviewApprovalButton>(tree, "Session.Approve");
            GuidedSessionProjectionTests.Shown(button).ShouldBeTrue();
            button.Command.ShouldBeSameAs(model.ApproveCommand);
            button.TargetIdentity.ShouldBe(identity);
            button.IsDefault.ShouldBeFalse();
            new ButtonAutomationPeer(button).GetName().ShouldBe(model.ApproveLabel);
            var text = new FormattedText(model.ApproveLabel, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                new Typeface(button.FontFamily, button.FontStyle, button.FontWeight, button.FontStretch), button.FontSize,
                Brushes.Black, 1);
            (text.WidthIncludingTrailingWhitespace + button.Padding.Left + button.Padding.Right)
                .ShouldBeLessThanOrEqualTo(button.ActualWidth + 1);
            Point position = button.TranslatePoint(new Point(), tree.Root);
            position.X.ShouldBeGreaterThanOrEqualTo(0);
            position.Y.ShouldBeGreaterThanOrEqualTo(0);
            (position.X + button.ActualWidth).ShouldBeLessThanOrEqualTo(width + 1);
            (position.Y + button.ActualHeight).ShouldBeLessThanOrEqualTo(height + 1);
            return 0;
        });

        model.ReviewTargetIdentity.ShouldBe(identity, "rendering must not approve or change the result");
        model.CanApprove.ShouldBeTrue();
        string? capture = Environment.GetEnvironmentVariable("PF_C02_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(capture))
            WpfRendering.CapturePng(() => new SessionScreenView { DataContext = model }, new(width, height),
                Path.Combine(capture, $"review-{step}-{width}x{height}.png"));
    }
}
