using PrintFlow.Domain.Files;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

/// <summary>Photoshop is running, identified, and sitting on a state PrintFlow recognises as safe.</summary>
/// <param name="Target">The verified process and window.</param>
/// <param name="State">The recognised safe state, with the markers it was recognised by.</param>
/// <param name="WasLaunched">
/// Whether PrintFlow started this instance. Recorded because reusing an operator's instance and
/// starting a fresh one are materially different situations to read about afterwards — and
/// because launching it is the <i>only</i> circumstance in which PrintFlow can be sure it is not
/// sharing the process with someone's unrelated work (§21).
/// </param>
public sealed record PhotoshopReadiness(
    PhotoshopTarget Target, PhotoshopStateSnapshot State, bool WasLaunched);

/// <summary>
/// The exact managed Working file is open in Photoshop, proved by absolute path.
/// </summary>
/// <param name="Target">The verified target, refreshed after the file was opened.</param>
/// <param name="State">
/// The confirming state — always
/// <see cref="PhotoshopStartingState.KnownEditorWithExpectedDocument"/>, because a screen that
/// merely stopped looking like the start page would not prove the right file is loaded.
/// </param>
/// <param name="Identity">The absolute path the identity probe read back out of Photoshop.</param>
/// <param name="OtherDocumentsMayBeOpen">
/// Whether Photoshop already held documents before this one was opened. Carried because it
/// changes what the caller is allowed to do afterwards: cleanup may close exactly this document
/// and must never assume the process is PrintFlow's to tidy (§21).
/// </param>
/// <remarks>
/// This record is the whole of what Part A can claim, and it is worth being explicit about what
/// it deliberately does not carry: no produced file, no output path, no revision, no elapsed
/// production time, no statement that anything was done <i>to</i> the document. It says the
/// accepted Photoshop is holding exactly the file that was asked for. An architecture test
/// asserts that it never grows a member that could be read as more than that (§18).
/// </remarks>
public sealed record PhotoshopOpenedDocument(
    PhotoshopTarget Target,
    PhotoshopStateSnapshot State,
    PhotoshopDocumentIdentity Identity,
    bool OtherDocumentsMayBeOpen);

/// <summary>
/// The Epic 11400 Part A foundation: get to a verified Photoshop in a known safe state, hand it
/// exactly one managed Working file, and prove it is the one that got loaded. Nothing beyond
/// that.
/// </summary>
/// <remarks>
/// Kept separate from <c>IPhotoshopOutputProcessor</c> on purpose, and the separation is the
/// honesty mechanism. That interface is the workflow seam, and a workflow step calling it
/// expects a validated production TIFF and a Revision; this foundation produces neither and must
/// never be mistaken for something that does (§18, §19). It is Infrastructure-only, exercised by
/// the controlled smoke and by tests, and is not reachable from <c>SessionService</c>.
///
/// There is no W1 method here, no resize, no colour-mode conversion and no export. Their absence
/// is what makes "Part A executes no Action and writes no file" a property of the code rather
/// than a claim in a report (§14, §15).
/// </remarks>
public interface IPhotoshopAutomationFoundation
{
    /// <summary>
    /// Identifies or launches the accepted Photoshop and confirms it is on a recognised safe
    /// state.
    /// </summary>
    /// <remarks>
    /// Fails closed on everything ambiguous: no accepted binary, a binary whose version or hash
    /// has moved, more than one candidate process, no identifiable window, a blocking dialog, or
    /// a screen that is not positively recognised. Nothing is closed, dismissed or dragged into
    /// a different state to make the answer come out safe.
    /// </remarks>
    Task<OperationResult<PhotoshopReadiness>> EnsureReadyAsync(CancellationToken cancellationToken);

    /// <summary>Re-observes a previously verified process without launching or changing it.</summary>
    Task<OperationResult<PhotoshopReadiness>> ReinspectAsync(
        PhotoshopReadiness previous, CancellationToken cancellationToken);

    /// <summary>
    /// Opens one managed Working file in the verified Photoshop and proves by absolute path that
    /// it is what got loaded.
    /// </summary>
    /// <param name="workingFile">
    /// Must be a <see cref="WorkspaceArea.Working"/> reference. Anything else is refused before
    /// a path is even resolved: the customer's original, the immutable source snapshot, and
    /// approved or rejected outputs are never handed to an external application (§8).
    /// </param>
    Task<OperationResult<PhotoshopOpenedDocument>> OpenManagedWorkingFileAsync(
        WorkspaceFileRef workingFile, CancellationToken cancellationToken);

    /// <summary>Optional bounded probe observations. An uninstrumented implementation records nothing.</summary>
    Task<OperationResult<PhotoshopOpenedDocument>> OpenManagedWorkingFileAsync(
        WorkspaceFileRef workingFile, Action<ReadinessProbeStage>? observe, CancellationToken cancellationToken) =>
        OpenManagedWorkingFileAsync(workingFile, cancellationToken);

    /// <summary>
    /// Closes exactly the document named by <paramref name="workingFile"/>, and nothing else.
    /// </summary>
    /// <remarks>
    /// Never called as part of the open path, and deliberately so. Photoshop is shared with the
    /// operator: this exists for a supervised caller that knows the document is its own file,
    /// and it re-proves identity immediately before acting. It closes one document. There is no
    /// close-all, and nothing anywhere in this adapter terminates the process (§21).
    /// </remarks>
    Task<OperationResult<PhotoshopTarget>> CloseExactDocumentAsync(
        PhotoshopOpenedDocument opened, WorkspaceFileRef workingFile, CancellationToken cancellationToken);

    Task<OperationResult<PhotoshopTarget>> CloseExactDocumentAsync(
        PhotoshopOpenedDocument opened, WorkspaceFileRef workingFile,
        Action<ReadinessProbeStage>? observe, CancellationToken cancellationToken) =>
        CloseExactDocumentAsync(opened, workingFile, cancellationToken);
}
