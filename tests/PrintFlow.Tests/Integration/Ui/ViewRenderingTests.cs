using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using PrintFlow.App;
using PrintFlow.App.ViewModels;
using PrintFlow.App.Views;
using PrintFlow.Domain.Sessions;
using PrintFlow.Tests.Fixtures;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Integration.Ui;

/// <summary>
/// Renders each view against a real view model and fails on any WPF data-binding error
/// (Epic 11100 Part 3C2 §17, §18).
/// </summary>
/// <remarks>
/// A mistyped binding path is silent at compile time and silent at run time — WPF writes a
/// line to a trace source and shows an empty control. That is the one class of defect the view
/// model tests cannot see, and the one a manual smoke pass is most likely to miss on a screen
/// full of text, so it is asserted here instead: the views are measured and arranged for real,
/// with <see cref="PresentationTraceSources.DataBindingSource"/> escalated to error level.
/// <para>
/// Each case runs on its own STA thread because WPF elements require one. Nothing is shown:
/// layout alone is enough to resolve every template, item container and binding on the screen.
/// </para>
/// </remarks>
[Collection(SqliteCollection.Name)]
public sealed class ViewRenderingTests
{
    [Fact]
    public async Task Home_renders_with_a_recent_session_and_no_binding_errors()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.WriteSourceFile("rendered.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);
        await harness.Home.RefreshCommand.ExecuteAsync(null);
        harness.Home.RecentSessions.ShouldNotBeEmpty();

        RenderOnStaThread(() => new HomeView { DataContext = harness.Home });
    }

    [Fact]
    public async Task Workflow_selection_renders_with_no_binding_errors()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.WriteSourceFile("selection.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        WorkflowSelectionViewModel selection = harness.WorkflowSelection(new RecordingNavigation());
        selection.Open(harness.Navigation.WorkflowSelectionFor!);

        RenderOnStaThread(() => new WorkflowSelectionView { DataContext = selection });
    }

    [Fact]
    public async Task The_session_screen_renders_with_no_binding_errors()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.WriteSourceFile("session.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        SessionViewModel session = harness.Session(new RecordingNavigation());
        SessionView opened = harness.Navigation.WorkflowSelectionFor!;
        session.Open(opened);
        session.Steps.Count.ShouldBe(opened.Steps.Count);
        opened.WorkflowType.ShouldBe(WorkflowType.PrepareAsset);

        RenderOnStaThread(() => new SessionScreenView { DataContext = session });
    }

    /// <summary>
    /// The review state renders too, with the artefact panel and the decision controls
    /// populated (Epic 11100 Part 3C3A §20).
    /// </summary>
    /// <remarks>
    /// The screen above shows a freshly imported session, where the review panel and most of
    /// the metadata grid are collapsed — so it would not have exercised their bindings at all.
    /// This one drives the session to <c>ReviewRequired</c> through the view model's own
    /// commands first, which is the state the operator spends the most time looking at.
    /// </remarks>
    [Fact]
    public async Task The_session_screen_renders_the_review_state_with_no_binding_errors()
    {
        using HomeScreenHarness harness = new();
        harness.FilePicker.Path = harness.WriteSourceFile("review.png");
        await harness.Home.ChooseFileCommand.ExecuteAsync(null);

        SessionViewModel session = harness.Session(new RecordingNavigation());
        session.Open(harness.Navigation.WorkflowSelectionFor!);
        await session.ConfirmOriginalCommand.ExecuteAsync(null);
        await session.RunStepCommand.ExecuteAsync(null);

        session.Notice.ShouldBeNull();
        session.IsReviewRequired.ShouldBeTrue();
        session.HasArtefact.ShouldBeTrue();

        RenderOnStaThread(() => new SessionScreenView { DataContext = session });
    }

    [Fact]
    public void The_shell_window_and_its_screen_templates_parse()
    {
        // Constructing the window runs the compiled XAML, which is where a bad DataTemplate
        // type reference or a missing view would surface.
        OnStaThread(() =>
        {
            MainWindow window = new();
            window.Resources.Count.ShouldBeGreaterThanOrEqualTo(3);
            window.Close();
        });
    }

    /// <summary>
    /// The negative control: proves the listener above would actually notice.
    /// </summary>
    /// <remarks>
    /// Without this, four passing "no binding errors" tests could equally mean "binding errors
    /// are never reported here" — an assertion that cannot fail is not an assertion.
    /// </remarks>
    [Fact]
    public void A_deliberately_wrong_binding_path_is_reported()
    {
        using HomeScreenHarness harness = new();

        List<string> errors = Render(() => new UserControl
        {
            DataContext = harness.Session(new RecordingNavigation()),
            Content = new TextBlock().WithBinding(
                TextBlock.TextProperty, new System.Windows.Data.Binding("NoSuchProperty")),
        });

        errors.ShouldNotBeEmpty();
    }

    // -------------------------------------------------------------------------------------

    private static void RenderOnStaThread(Func<UserControl> create) =>
        Render(create).ShouldBeEmpty();

    /// <summary>Measures and arranges the control on an STA thread, collecting binding traces.</summary>
    private static List<string> Render(Func<UserControl> create)
    {
        List<string> errors = [];

        OnStaThread(() =>
        {
            BindingErrorListener listener = new();
            SourceLevels previous = PresentationTraceSources.DataBindingSource.Switch.Level;
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error | SourceLevels.Warning;

            try
            {
                UserControl view = create();
                view.Measure(new Size(1200, 900));
                view.Arrange(new Rect(0, 0, 1200, 900));
                view.UpdateLayout();
            }
            finally
            {
                PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
                PresentationTraceSources.DataBindingSource.Switch.Level = previous;
                errors.AddRange(listener.Errors);
            }
        });

        return errors;
    }

    private static void OnStaThread(Action action)
    {
        Exception? failure = null;
        Thread thread = new(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw new InvalidOperationException("The view failed to render.", failure);
        }
    }

    /// <summary>Collects whatever WPF's data-binding trace source reports.</summary>
    private sealed class BindingErrorListener : TraceListener
    {
        public List<string> Errors { get; } = [];

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                Errors.Add(message);
            }
        }
    }
}

/// <summary>Small helper so a binding can be attached inline in a test expression.</summary>
internal static class BindingTestExtensions
{
    internal static T WithBinding<T>(this T element, DependencyProperty property, System.Windows.Data.BindingBase binding)
        where T : FrameworkElement
    {
        element.SetBinding(property, binding);
        return element;
    }
}
