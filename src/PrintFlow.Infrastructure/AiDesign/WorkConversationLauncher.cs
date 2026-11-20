using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Workflow.AiDesign;

namespace PrintFlow.Infrastructure.AiDesign;

/// <summary>Accepts only a conversation identity, never a caller-provided link or command.</summary>
public sealed class WorkConversationLauncher : IWorkConversationLauncher
{
    public async Task<bool> OpenInWorkProfileAsync(Guid conversationId, CancellationToken ct)
    {
        if (conversationId == Guid.Empty) return false;
        var config = await AiDesignBridgeConfiguration.LoadAsync(AiDesignBridgeConfiguration.ConfigurationPath, ct).ConfigureAwait(false);
        if (config.IsFailure || config.Value.ChromeProfileDirectory is not { } profile ||
            config.Value.VerifiedWorkContractHash != AiDesignBridgeConfiguration.TextSubmissionTrialContract ||
            ChromeWorkTextSubmission.LocateChrome() is not { } chrome) return false;

        try
        {
            ReparsePointGuard.RefuseAncestry(chrome);
            var start = new ProcessStartInfo(chrome) { UseShellExecute = false };
            start.ArgumentList.Add("--profile-directory=" + profile);
            start.ArgumentList.Add("--new-tab");
            start.ArgumentList.Add($"https://chatgpt.com/c/{conversationId:D}");
            using var process = Process.Start(start);
            return process is not null;
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or InvalidOperationException) { return false; }
    }

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
