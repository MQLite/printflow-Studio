using System.ComponentModel;
using System.Diagnostics;
using PrintFlow.Workflow.AiDesign;

namespace PrintFlow.Infrastructure.AiDesign;

/// <summary>Accepts only a conversation identity, never a caller-provided link or command.</summary>
public sealed class WorkConversationLauncher : IWorkConversationLauncher
{
    public bool Open(Guid conversationId)
    {
        if (conversationId == Guid.Empty) return false;
        try
        {
            using var process = Process.Start(new ProcessStartInfo($"https://chatgpt.com/c/{conversationId:D}")
            { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException) { return false; }
    }
}
