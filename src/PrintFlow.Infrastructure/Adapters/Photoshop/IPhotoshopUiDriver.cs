using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Infrastructure.Adapters.Photoshop;

/// <summary>
/// Every interaction PrintFlow may have with a Photoshop window, named rather than described
/// (Epic 11400 Part A §4, §23).
/// </summary>
/// <remarks>
/// The shape of this interface is the safety property, and what it omits is the point. There is
/// no method that takes a control name, a control id, a coordinate, a keystroke or an arbitrary
/// path. There is no "run this action", no "click that", and no way for a caller above
/// Infrastructure to reach any of it. A caller can ask for exactly five things, and each one is
/// a complete guarded sequence rather than a step someone else has to remember to guard.
///
/// Note in particular what is <b>not</b> here in Part A: nothing runs a Photoshop Action,
/// nothing resizes, nothing converts a colour mode and nothing saves. Those belong to the next
/// slice, and their absence from this seam is what makes that a structural fact rather than a
/// promise (§14, §15).
/// </remarks>
public interface IPhotoshopUiDriver
{
    /// <summary>Reads and classifies the current screen. Sends nothing and changes nothing.</summary>
    /// <param name="expectedDocumentFileName">
    /// The managed file name this attempt is about, or <c>null</c> for an observation that is
    /// not about any particular document.
    /// </param>
    Task<OperationResult<PhotoshopStateSnapshot>> InspectStateAsync(
        PhotoshopTarget target, string? expectedDocumentFileName, CancellationToken cancellationToken);

    /// <summary>Brings the verified window to the foreground and confirms that it got there.</summary>
    /// <remarks>
    /// Success means the window is now verified to hold the foreground, not merely that the
    /// request was made. Windows may refuse a foreground change, and a caller that treated the
    /// request as the outcome would then send input to whatever actually holds it.
    /// </remarks>
    Task<OperationResult<PhotoshopTarget>> ActivateAsync(
        PhotoshopTarget target, CancellationToken cancellationToken);

    /// <summary>
    /// Opens exactly one file at <paramref name="managedAbsolutePath"/> through the signed Open
    /// dialog.
    /// </summary>
    /// <param name="managedAbsolutePath">
    /// An absolute path the caller has already established belongs to the managed workspace.
    /// This seam does not re-derive that — the area check lives at the foundation boundary,
    /// where a <c>WorkspaceFileRef</c> is still available to check (§8).
    /// </param>
    /// <remarks>
    /// The sequence is fixed and every step of it is a guard: re-verify the target, re-acquire
    /// and re-verify the foreground, raise the dialog, confirm the dialog is owned by the
    /// verified process and carries the signed shape, write the exact path to the signed
    /// filename control, <i>read it back</i>, and only then press the signed Open control once.
    /// A read-back that does not match the exact string written is a refusal with the dialog
    /// cancelled, because pressing Open on an unverified field means opening whatever the dialog
    /// already had selected — a file PrintFlow did not choose (§9, §10).
    /// </remarks>
    Task<OperationResult<PhotoshopTarget>> OpenManagedDocumentAsync(
        PhotoshopTarget target, string managedAbsolutePath, CancellationToken cancellationToken);

    /// <summary>
    /// Establishes the absolute path of the document Photoshop is currently holding, through the
    /// signed read-only identity probe.
    /// </summary>
    /// <remarks>
    /// Raises the signed identity surface, reads the document's own file name and its own
    /// containing folder, and cancels through the signed Cancel control. It writes nothing,
    /// changes no filename, path or format, and saves nothing — no file can be created by
    /// calling this (§13).
    /// <para>
    /// It returns what Photoshop is holding rather than a yes/no answer about what was expected.
    /// The comparison is the caller's, and keeping it there is deliberate: a probe that returned
    /// a boolean could quietly start returning <c>true</c> for a weaker reason, whereas a probe
    /// that returns a path cannot express "close enough" at all.
    /// </para>
    /// </remarks>
    Task<OperationResult<PhotoshopDocumentIdentity>> ProbeDocumentIdentityAsync(
        PhotoshopTarget target, CancellationToken cancellationToken);

    /// <summary>
    /// Closes the active document, and only after re-proving it is exactly
    /// <paramref name="expectedAbsolutePath"/>.
    /// </summary>
    /// <remarks>
    /// Never called on PrintFlow's own initiative. Closing a document is a decision about
    /// someone's work: Photoshop is shared with the operator and may be holding documents
    /// PrintFlow knows nothing about, so this exists for a supervised caller that knows the
    /// loaded document is its own file, and it re-runs the full identity probe immediately
    /// beforehand rather than trusting an earlier one (§21).
    /// <para>
    /// It closes the <i>active</i> document only. There is no close-all here and no route that
    /// closes Photoshop itself.
    /// </para>
    /// </remarks>
    Task<OperationResult<PhotoshopTarget>> CloseExactDocumentAsync(
        PhotoshopTarget target, string expectedAbsolutePath, CancellationToken cancellationToken);

    /// <summary>Captures the verified window for a failure record. Local only, never uploaded.</summary>
    OperationResult<EvidenceRef> CaptureEvidence(PhotoshopTarget target, string reason);
}
