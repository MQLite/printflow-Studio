using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PrintFlow.App.Startup;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Sessions;
using PrintFlow.Infrastructure.Startup;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Commands;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class WpfUiSampleVisualEvidenceTests
{
    [Theory]
    [InlineData(1000, 700)]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public async Task Home_and_approved_png_save_use_real_bindings_and_workflow(int width, int height)
    {
        using OperatorCultureScope culture = new("zh-CN");
        using HomeScreenHarness home = new();
        home.StartupStatus.Publish(StartupStatus.Started(true, StartupRecoveryReport.Empty));
        string source = home.Inner.Workspace.CreateSourceFile("春日花束—合成示例.png", Flower());
        await home.Sessions.ImportAsync(WorkflowType.PrepareAsset, source, "春日花束 · 设计素材", "tester", default);
        await home.Home.RefreshCommand.ExecuteAsync(null); await home.Home.ThumbnailsLoaded;
        Capture(() => new HomeView { DataContext = home.Home }, new(width, height), "home");

        using SessionServiceHarness h = new();
        var workflow = h.CreateService();
        var imported = (await workflow.ImportAsync(WorkflowType.PrepareAsset,
            h.Workspace.CreateSourceFile("春日花束—合成示例.png", Flower()), "春日花束", "tester", default)).Value;
        foreach (WorkflowCommand command in new WorkflowCommand[] { new WorkflowCommand.ConfirmOriginal(),
            new WorkflowCommand.Skip(StepKind.Enhancement), new WorkflowCommand.Skip(StepKind.BackgroundRemoval), new WorkflowCommand.StartStep(StepKind.Trim) })
            (await workflow.ExecuteAsync(imported.Id, command, "tester", default)).IsSuccess.ShouldBeTrue();
        string folder = FinalSaveFixtures.NewFolder("wpf-ui-sample");
        try
        {
            var model = await GuidedCompletionSaveTests.Open(h, workflow, FinalSaveFixtures.Delivery(h),
                new GuidedCompletionSaveTests.Picker(folder), (await workflow.LoadAsync(imported.Id, default)).Value);
            await model.ApproveCommand.ExecuteAsync(null);
            await model.RunStepCommand.ExecuteAsync(null); // Existing approved-PNG preparation, never a fabricated result.
            await model.PreviewsLoaded; await model.FinalSaveFactsLoaded;
            model.ChangeLocationCommand.Execute(null);
            model.CanSaveApproved.ShouldBeTrue();
            model.GuidedPage.ShouldBe(GuidedSessionPage.Save);
            Capture(() => new SessionScreenView { DataContext = model }, new(width, height), "save-png");
        }
        finally { FinalSaveFixtures.Remove(folder); }
    }

    private static void Capture(Func<UserControl> create, Size viewport, string name)
    {
        WpfRendering.RenderExpectingNoBindingErrors(create, viewport, tree =>
        {
            try { return tree.OfType<Image>().Count(i => i.Source is not null); }
            finally { tree.Root.DataContext = null; tree.Root.UpdateLayout(); }
        });
        string? directory = Environment.GetEnvironmentVariable("PF_WPFUI_SAMPLE_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        WpfRendering.CapturePng(create, viewport, Path.Combine(directory, $"{name}-{viewport.Width:0}x{viewport.Height:0}.png"));
    }

    // Deterministic vector test artwork, rendered to a real transparent PNG input.
    // This is fixture content, not a screenshot retouch or a production output.
    private static byte[] Flower()
    {
        byte[] bytes = [];
        WpfRendering.OnStaThread(() =>
        {
        DrawingVisual visual = new();
        using (DrawingContext dc = visual.RenderOpen())
        {
            var green = new SolidColorBrush(Color.FromRgb(0x26, 0x69, 0x51));
            var leaf = new SolidColorBrush(Color.FromRgb(0x8A, 0xB8, 0x8D));
            var orange = new SolidColorBrush(Color.FromRgb(0xE4, 0x94, 0x5A));
            dc.DrawLine(new Pen(green, 14), new Point(400, 700), new Point(400, 310));
            dc.PushTransform(new RotateTransform(-35, 350, 570)); dc.DrawEllipse(leaf, null, new(350, 570), 105, 37); dc.Pop();
            dc.PushTransform(new RotateTransform(35, 458, 495)); dc.DrawEllipse(green, null, new(458, 495), 100, 36); dc.Pop();
            for (int index = 0; index < 8; index++)
            {
                dc.PushTransform(new RotateTransform(index * 45, 400, 300));
                dc.DrawEllipse(orange, null, new(400, 211), 46, 94); dc.Pop();
            }
            dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(0xF5, 0xDA, 0x92)), null, new(400, 300), 65, 65);
            dc.DrawEllipse(green, null, new(400, 300), 24, 24);
            dc.DrawEllipse(leaf, null, new(235, 400), 10, 10);
            dc.DrawEllipse(orange, null, new(570, 620), 14, 14);
        }
        RenderTargetBitmap bitmap = new(800, 800, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
        PngBitmapEncoder encoder = new(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using MemoryStream stream = new(); encoder.Save(stream); bytes = stream.ToArray();
        });
        return bytes;
    }
}
