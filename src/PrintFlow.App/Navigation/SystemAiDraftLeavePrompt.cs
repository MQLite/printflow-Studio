using System.Windows;
using System.Windows.Controls;
using PrintFlow.App.Resources;

namespace PrintFlow.App.Navigation;

/// <summary>Closing this local decision dialog means Cancel.</summary>
public sealed class SystemAiDraftLeavePrompt : IAiDraftLeavePrompt
{
    public async Task<AiDraftLeaveChoice> ChooseAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Application.Current is not { } app) return AiDraftLeaveChoice.Cancel;
        var result = await app.Dispatcher.InvokeAsync(() =>
        {
            ct.ThrowIfCancellationRequested();
            var choice = AiDraftLeaveChoice.Cancel;
            var panel = new StackPanel { Margin = new Thickness(24) };
            var dialog = new Window
            {
                Title = Strings.Resolve("AiPrompt_LeaveTitle"), Content = panel,
                Width = 540, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = app.MainWindow
            };
            panel.Children.Add(new TextBlock { Text = Strings.Resolve("AiPrompt_LeaveMessage"), TextWrapping = TextWrapping.Wrap, FontSize = 16 });
            var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
            foreach (var (value, key) in new[] { (AiDraftLeaveChoice.Save, "AiPrompt_Save"), (AiDraftLeaveChoice.Discard, "AiPrompt_Discard"), (AiDraftLeaveChoice.Cancel, "AiPrompt_Cancel") })
            {
                var button = new Button { Content = Strings.Resolve(key), Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(6, 0, 0, 0), IsDefault = value == AiDraftLeaveChoice.Save, IsCancel = value == AiDraftLeaveChoice.Cancel };
                System.Windows.Automation.AutomationProperties.SetAutomationId(button, "AiPrompt.Leave." + value);
                button.Click += (_, _) => { choice = value; dialog.DialogResult = true; };
                buttons.Children.Add(button);
            }
            panel.Children.Add(buttons); dialog.ShowDialog(); return choice;
        });
        ct.ThrowIfCancellationRequested();
        return result;
    }
}
