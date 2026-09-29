using System.Windows;
using System.Windows.Controls;
using PrintFlow.App.ViewModels;

namespace PrintFlow.App.Views;

/// <summary>
/// Home. The code-behind exists only for drag-and-drop, which WPF exposes as events rather
/// than as bindable state, and for one layout proportion XAML cannot express.
/// </summary>
/// <remarks>
/// It reads the dropped paths and hands them straight to the view model's command; it makes no
/// decision about them. In particular the "exactly one file" rule is not applied here — the
/// view model applies it, so the rule is testable without a window (Part 3C2 §4, §15).
/// </remarks>
public partial class HomeView : UserControl
{
    /// <summary>
    /// The most of the screen the part above the lists may take before it scrolls on its own
    /// (SCRUM-11152).
    /// </summary>
    /// <remarks>
    /// With every disclosure open, the readiness summary, startup warnings, import box and a
    /// failure notice together outgrow a 1000×700 window, and the Recovery list below would be
    /// squeezed to nothing. Capping the upper part keeps both halves reachable by scrolling.
    /// Layout only: it decides nothing and binds nothing.
    /// </remarks>
    private const double UpperShare = 0.65;

    public HomeView() => InitializeComponent();

    private void OnLayoutSizeChanged(object sender, SizeChangedEventArgs e) =>
        Upper.MaxHeight = e.NewSize.Height * UpperShare;

    private static string[] DroppedPaths(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop)
            ? (string[])e.Data.GetData(DataFormats.FileDrop)!
            : [];

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (DataContext is not HomeViewModel home)
        {
            return;
        }

        // Every dropped path is passed on, including the ones a multi-file drop produced:
        // the refusal has to be able to say how many arrived.
        home.DropFilesCommand.Execute(DroppedPaths(e));
    }
}
