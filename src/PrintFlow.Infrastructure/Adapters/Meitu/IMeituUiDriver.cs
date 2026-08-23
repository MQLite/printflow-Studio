using PrintFlow.Domain.Results;
using PrintFlow.Infrastructure.Automation;

namespace PrintFlow.Infrastructure.Adapters.Meitu;

/// <summary>
/// The controls this slice is allowed to look for, named rather than described
/// (Epic 11300 Part A §11).
/// </summary>
/// <remarks>
/// A closed set, like <see cref="KnownShortcut"/>: the adapter can ask for the photo-editor
/// entry or a part of the file dialog, and nothing else. There is no
/// <c>FindElement(string name)</c> that a future caller could hand an arbitrary label to.
/// </remarks>
public enum KnownMeituElement
{
    /// <summary>The clean start page's photo-editor entry, selected from the signed markers.</summary>
    WelcomeOpenEntry,

    /// <summary>The file-name field of the Windows common file dialog Meitu opened.</summary>
    FileDialogFileName,

    /// <summary>The Open button of that dialog.</summary>
    FileDialogOpenButton,
}

/// <summary>
/// The single boundary through which every physical interaction with Meitu passes
/// (Epic 11300 Part A §11).
/// </summary>
/// <remarks>
/// Everything above this interface — the production adapter, and later the enhancement and
/// background-removal flows — speaks in terms of a verified <see cref="MeituTarget"/>, a named
/// element and a named shortcut. No caller can express a screen coordinate, a raw key code, or
/// a window it has not had verified, because those types do not appear in this signature.
///
/// The implementation's obligation, which its callers therefore do not have to remember: every
/// method that produces input re-establishes that the target still belongs to the expected
/// process and still holds the foreground <b>before</b> the input is produced, and returns
/// <see cref="FailureCode.MeituTargetLost"/> with nothing sent otherwise (§3, §10, §19).
/// </remarks>
public interface IMeituUiDriver
{
    /// <summary>
    /// Reads the current screen and classifies it. Sends nothing and changes nothing.
    /// </summary>
    /// <param name="target">The verified process and window.</param>
    /// <param name="expectedWorkingCopyFileName">
    /// The name of the working copy this attempt handed over, so
    /// <see cref="MeituStartingState.KnownEditorWithExpectedWorkingCopy"/> can be distinguished
    /// from some other document being open. <c>null</c> before anything has been handed over.
    /// </param>
    Task<OperationResult<MeituStateSnapshot>> InspectStateAsync(
        MeituTarget target, string? expectedWorkingCopyFileName, CancellationToken cancellationToken);

    /// <summary>
    /// Brings the target window to the foreground and confirms it actually got there.
    /// </summary>
    /// <remarks>
    /// Returns a refreshed target rather than the one passed in: activation can restore a
    /// minimised window, which changes its bounds and its state flags, and a later decision must
    /// rest on what is true now.
    /// </remarks>
    Task<OperationResult<MeituTarget>> ActivateAsync(MeituTarget target, CancellationToken cancellationToken);

    /// <summary>Locates a named control beneath the verified window.</summary>
    OperationResult<UiElementRef> FindKnownElement(MeituTarget target, KnownMeituElement element);

    /// <summary>Invokes a named control, after re-verifying the target.</summary>
    Task<OperationResult<Unit>> InvokeKnownElementAsync(
        MeituTarget target, KnownMeituElement element, CancellationToken cancellationToken);

    /// <summary>Sends a named shortcut, after re-verifying that the target holds the foreground.</summary>
    Task<OperationResult<Unit>> SendVerifiedShortcutAsync(
        MeituTarget target, KnownShortcut shortcut, CancellationToken cancellationToken);

    /// <summary>
    /// Hands <paramref name="workingCopyAbsolutePath"/> to Meitu through a positively identified
    /// Open control and file dialog.
    /// </summary>
    /// <remarks>
    /// The path is never typed into whatever dialog happens to hold focus. The dialog must be
    /// owned by the verified Meitu process and carry the Windows common-dialog class before its
    /// file-name field is written, and the field is set through the value pattern rather than
    /// keystrokes (§17).
    /// </remarks>
    Task<OperationResult<Unit>> OpenWorkingCopyAsync(
        MeituTarget target, string workingCopyAbsolutePath, CancellationToken cancellationToken);

    /// <summary>Captures the target window as local failure evidence.</summary>
    OperationResult<EvidenceRef> CaptureEvidence(MeituTarget target, string reason);
}
