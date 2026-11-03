using System.Windows;

namespace PrintFlow.App.ViewModels.AiDesign;

public interface IAiPromptClipboard
{
    void SetText(string text);
}

public sealed class SystemAiPromptClipboard : IAiPromptClipboard
{
    public void SetText(string text) => Clipboard.SetText(text);
}
