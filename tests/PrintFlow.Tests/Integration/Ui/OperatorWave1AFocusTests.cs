using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Tests.Fixtures;

namespace PrintFlow.Tests.Integration.Ui;

[Collection(SqliteCollection.Name)]
public sealed class OperatorWave1AFocusTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Initial_review_focus_follows_attachment_into_the_window_scope(bool modelBeforeAttachment)
    {
        using HomeScreenHarness harness = new();
        SessionViewModel screen = harness.Session(new RecordingNavigation());
        screen.Open(await OperatorWave1Tests.Imported(harness));
        await screen.ConfirmOriginalCommand.ExecuteAsync(null);
        await screen.RunStepCommand.ExecuteAsync(null);
        await screen.PreviewsLoaded;
        string? target = screen.ReviewTargetIdentity;
        target.ShouldNotBeNull();

        WpfRendering.OnStaThread(() =>
        {
            SessionScreenView view = new();
            Border panel = (Border)view.FindName("OperatorStatusPanel");
            if (modelBeforeAttachment) view.DataContext = screen;
            Button ordinary = new() { Content = "Unrelated non-approval action" };
            StackPanel content = new();
            content.Children.Add(ordinary);
            content.Children.Add(view);
            Window window = new() { Content = content };
            try
            {
                // No Show, native HWND, activation, or input. Exercise the real Loaded handler
                // after the same logical-tree scope change as the visible host's first entry.
                if (!modelBeforeAttachment) view.DataContext = screen;
                FocusManager.GetFocusScope(view).ShouldBeSameAs(window);
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                FocusManager.GetFocusedElement(window).ShouldBeSameAs(panel,
                    "the new review must carry its non-activating focus into its attached scope");

                FocusManager.SetFocusedElement(window, ordinary);
                view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                FocusManager.GetFocusedElement(window).ShouldBeSameAs(ordinary,
                    "an unchanged target in the same scope must not steal valid focus");
                screen.ReviewTargetIdentity.ShouldBe(target);
                screen.CanApprove.ShouldBeTrue();
            }
            finally
            {
                view.DataContext = null;
                window.Content = null;
                window.Close();
            }
        });
    }
}
