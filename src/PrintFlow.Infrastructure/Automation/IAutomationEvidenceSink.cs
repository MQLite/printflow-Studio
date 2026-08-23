using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Automation;

/// <summary>One captured piece of local failure evidence.</summary>
/// <param name="AbsolutePath">Where the capture was written on this workstation.</param>
/// <param name="Reason">Why it was taken, in stable English.</param>
/// <param name="CapturedUtc">When it was taken.</param>
/// <param name="WindowTitle">The title of the window that was captured, for the log line.</param>
public sealed record EvidenceRef(
    string AbsolutePath, string Reason, DateTimeOffset CapturedUtc, string WindowTitle);

/// <summary>
/// Captures a picture of the window PrintFlow was looking at when automation stopped
/// (Epic 11300 Part A §20).
/// </summary>
/// <remarks>
/// Two constraints are part of the contract, not of one implementation. The capture is scoped
/// to a single identified window rather than the desktop, so an unrelated application — or a
/// customer's file open in one — is not photographed as a side effect of a Meitu failure. And
/// the result stays local: this seam writes a file and returns its path. It has no upload,
/// no copy-to-workspace and no attach-to-report capability, which is what keeps
/// "evidence remains local, never committed, never uploaded" a property of the code rather
/// than a rule in a document.
/// </remarks>
public interface IAutomationEvidenceSink
{
    /// <summary>Captures <paramref name="window"/> and records <paramref name="reason"/> against it.</summary>
    /// <remarks>
    /// A capture failure is never allowed to replace the failure being investigated: callers
    /// attach a successful capture to their own structured failure and otherwise carry on
    /// reporting the original problem.
    /// </remarks>
    OperationResult<EvidenceRef> CaptureWindow(ExternalWindowRef window, string reason);
}
