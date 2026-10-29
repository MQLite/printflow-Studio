using System.IO;
using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Workspace;
using PrintFlow.Workflow.AiDesign;

namespace PrintFlow.Infrastructure.AiDesign;

/// <summary>Installation-level authority, deliberately independent of production workstation automation.</summary>
public sealed class AiDesignBrowserLease(string installationDirectory) : IAiDesignBrowserLease
{
    public OperationResult<IDisposable> TryAcquire(Guid requestId)
    {
        if (requestId == Guid.Empty) return OperationResult.Fail<IDisposable>(FailureCode.PreconditionNotMet, "Request identity required.");
        try
        {
            ReparsePointGuard.RefuseAncestry(_directory); Directory.CreateDirectory(_directory);
            string path = Path.Combine(_directory, "browser.lock"); ReparsePointGuard.RefuseAncestry(path);
            return OperationResult.Ok<IDisposable>(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return OperationResult.Fail<IDisposable>(FailureCode.AdapterUnavailable, "AI browser occupied or lease unavailable."); }
    }
    private readonly string _directory = Path.GetFullPath(installationDirectory);
}
