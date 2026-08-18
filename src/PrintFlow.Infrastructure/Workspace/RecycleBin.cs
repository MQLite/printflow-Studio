using System.IO;
using Microsoft.VisualBasic.FileIO;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Workspace;

/// <summary>
/// The only deletion route in PrintFlow Studio (Epic 11100 Task 11106b; plan §12.2).
/// </summary>
/// <remarks>
/// Uses the in-box <c>Microsoft.VisualBasic.FileIO.FileSystem</c> Recycle Bin API — no
/// third-party package, no P/Invoke. There is no hard-delete fallback anywhere in this type:
/// every failure comes back as a structured <see cref="OperationFailure"/>.
/// </remarks>
public sealed class RecycleBin : IRecycleBin
{
    /// <inheritdoc />
    public OperationResult<Unit> SendToRecycleBin(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);

        if (!File.Exists(absolutePath))
        {
            return OperationResult.Fail<Unit>(
                FailureCode.OutputMissing, $"Nothing to recycle at '{absolutePath}'.");
        }

        try
        {
            FileSystem.DeleteFile(
                absolutePath,
                UIOption.OnlyErrorDialogs,
                RecycleOption.SendToRecycleBin,
                UICancelOption.ThrowException);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            // No hard-delete fallback: a Recycle-Bin failure is a structured failure, full stop.
            return OperationResult.Fail<Unit>(
                FailureCode.WorkspaceError, $"Could not send '{absolutePath}' to the Recycle Bin: {ex.Message}");
        }

        return OperationResult.Ok();
    }
}
