using System.Windows;
using System.Windows.Controls;
using PrintFlow.App.ViewModels;

namespace PrintFlow.App.Views;

/// <summary>
/// Home. The code-behind exists only for drag-and-drop, which WPF exposes as events rather
/// than as bindable state.
/// </summary>
/// <remarks>
/// It reads the dropped paths and hands them straight to the view model's command; it makes no
/// decision about them. In particular the "exactly one file" rule is not applied here — the
/// view model applies it, so the rule is testable without a window (Part 3C2 §4, §15).
/// </remarks>
public partial class HomeView : UserControl
{
    public HomeView() => InitializeComponent();

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
