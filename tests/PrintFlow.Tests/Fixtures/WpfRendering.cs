using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// What one real WPF layout pass produced: the binding errors it reported, and the visual tree
/// it left behind (Epic 11100 Part 3C2 §17; Epic 11200 Part C1 §25, §27).
/// </summary>
/// <param name="BindingErrors">
/// Everything WPF's data-binding trace source said. Empty is the only acceptable result for a
/// screen under test — a mistyped path is silent everywhere else.
/// </param>
/// <param name="DesiredSize">
/// What the control asked for. Compared against the viewport it was given, this is the honest
/// automated answer to "does anything obviously clip at 1000×700".
/// </param>
/// <param name="Facts">
/// Whatever the caller's inspector pulled out of the arranged tree, as plain values.
/// </param>
/// <remarks>
/// The tree itself is deliberately <b>not</b> handed back. Every WPF element belongs to the STA
/// thread that built it, so a result carrying live elements would throw the moment a test
/// touched one — the inspector runs on that thread instead and returns numbers and strings,
/// which are safe to assert on anywhere.
/// </remarks>
internal sealed record RenderResult<T>(
    IReadOnlyList<string> BindingErrors,
    Size DesiredSize,
    T Facts);

/// <summary>The arranged tree, as seen from inside the render thread.</summary>
/// <remarks>
/// Passed to an inspector rather than returned, for the thread-affinity reason above. The
/// <c>Root</c> is exposed as well as the flattened list because resource lookup
/// (<c>FindResource</c>) is a question about the element that owns the resources, not about the
/// tree beneath it.
/// </remarks>
internal sealed record RenderedTree(UserControl Root, IReadOnlyList<DependencyObject> Elements)
{
    /// <summary>Every element of type <typeparamref name="T"/> in the arranged tree.</summary>
    public IEnumerable<T> OfType<T>()
        where T : DependencyObject => Elements.OfType<T>();
}

/// <summary>
/// Measures and arranges a WPF control on its own STA thread, collecting binding traces.
/// </summary>
/// <remarks>
/// Shared rather than duplicated because two suites need it for different questions:
/// <c>ViewRenderingTests</c> asks "did anything fail to bind", and the image-review smoke pass
/// asks "what is on the screen". Both need the same STA thread, the same escalated trace
/// source, and the same real measure/arrange — and a second copy of that machinery would be a
/// second place for it to be subtly wrong.
/// <para>
/// Nothing is shown. Layout alone resolves every template, item container and binding, which is
/// what makes this usable on a build agent with no interactive desktop.
/// </para>
/// </remarks>
internal static class WpfRendering
{
    /// <summary>The window size the operator screens are signed off against (Part C1 §27).</summary>
    public static readonly Size ReviewViewport = new(1000, 700);

    /// <summary>Renders, and lets <paramref name="inspect"/> read the tree on the render thread.</summary>
    public static RenderResult<T> Render<T>(
        Func<UserControl> create, Size viewport, Func<RenderedTree, T> inspect)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(inspect);

        List<string> errors = [];
        Size desired = default;
        T facts = default!;

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
                view.Measure(viewport);
                view.Arrange(new Rect(0, 0, viewport.Width, viewport.Height));
                view.UpdateLayout();

                desired = view.DesiredSize;

                List<DependencyObject> elements = [];
                Collect(view, elements);
                facts = inspect(new RenderedTree(view, elements));

                // Detach the view from the view model before the STA thread ends.
                //
                // A rendered Button subscribes to its bound ICommand's CanExecuteChanged, and
                // the view model outlives this thread — so a command completing later would
                // raise that event and the Button, owned by a thread that no longer exists,
                // would throw a cross-thread InvalidOperationException on a thread-pool thread
                // and take the test host down with it. Clearing the DataContext re-evaluates
                // every binding against null, which unsubscribes the buttons. Only a test needs
                // this: in the application the view and its view model share one UI thread for
                // as long as both exist.
                view.DataContext = null;
                view.UpdateLayout();
            }
            finally
            {
                PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
                PresentationTraceSources.DataBindingSource.Switch.Level = previous;
                errors.AddRange(listener.Errors);
            }
        });

        return new RenderResult<T>(errors, desired, facts);
    }

    /// <summary>Renders when only the binding errors matter.</summary>
    public static IReadOnlyList<string> Render(Func<UserControl> create, Size viewport) =>
        Render(create, viewport, _ => 0).BindingErrors;

    /// <summary>Renders and fails on any binding error, which is the usual expectation.</summary>
    public static RenderResult<T> RenderExpectingNoBindingErrors<T>(
        Func<UserControl> create, Size viewport, Func<RenderedTree, T> inspect)
    {
        RenderResult<T> result = Render(create, viewport, inspect);
        result.BindingErrors.ShouldBeEmpty();
        return result;
    }

    /// <inheritdoc cref="RenderExpectingNoBindingErrors{T}" />
    public static void RenderExpectingNoBindingErrors(Func<UserControl> create, Size viewport) =>
        Render(create, viewport).ShouldBeEmpty();

    /// <summary>
    /// Captures one already-testable operator state for the narrow human visual check required by
    /// a correction. This is opt-in evidence generation, not a golden-image assertion.
    /// </summary>
    public static void CapturePng(
        Func<UserControl> create,
        Size viewport,
        string absolutePath,
        Action<RenderedTree>? prepare = null)
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        string path = Path.GetFullPath(absolutePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        OnStaThread(() =>
        {
            UserControl view = create();
            // A UserControl normally inherits the Window's opaque surface. RenderTargetBitmap has
            // no Window, so provide that surface explicitly instead of turning transparent pixels
            // black in the review PNG.
            view.Background = Brushes.White;
            view.Measure(viewport);
            view.Arrange(new Rect(0, 0, viewport.Width, viewport.Height));
            view.UpdateLayout();

            if (prepare is not null)
            {
                List<DependencyObject> elements = [];
                Collect(view, elements);
                prepare(new RenderedTree(view, elements));
                view.UpdateLayout();
            }

            RenderTargetBitmap bitmap = new(
                checked((int)viewport.Width),
                checked((int)viewport.Height),
                96,
                96,
                PixelFormats.Pbgra32);
            bitmap.Render(view);

            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream output = File.Create(path);
            encoder.Save(output);

            // Match Render's command-unsubscription discipline before the STA thread ends.
            view.DataContext = null;
            view.UpdateLayout();
        });
    }

    /// <summary>Runs <paramref name="action"/> on a fresh STA thread and rethrows what it threw.</summary>
    public static void OnStaThread(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

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

    /// <summary>
    /// Walks both the visual tree and the logical one.
    /// </summary>
    /// <remarks>
    /// Both, because neither alone is enough: an <c>ItemsControl</c>'s generated containers live
    /// in the visual tree, while a <c>ContentPresenter</c>'s content can be reachable only
    /// logically until it is realised. A smoke pass asking "is the image there" must not get a
    /// false negative from having looked in one place.
    /// </remarks>
    private static void Collect(DependencyObject root, List<DependencyObject> into)
    {
        if (into.Contains(root))
        {
            return;
        }

        into.Add(root);

        int visualChildren = root is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetChildrenCount(root)
            : 0;

        for (int i = 0; i < visualChildren; i++)
        {
            Collect(VisualTreeHelper.GetChild(root, i), into);
        }

        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DependencyObject dependencyObject)
            {
                Collect(dependencyObject, into);
            }
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
