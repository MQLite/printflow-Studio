using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Automation;

/// <summary>
/// A keystroke PrintFlow is allowed to send, named rather than described.
/// </summary>
/// <remarks>
/// An enum instead of a string or a key code is the point: there is no way to express
/// "whatever the caller feels like typing", so no path exists by which operator text, a file
/// path, or a generated sequence could be typed into whatever holds focus. A new value is a
/// deliberate, reviewed addition backed by workstation evidence
/// (Epic 11300 Part A §4 priority 2).
/// </remarks>
public enum KnownShortcut
{
    /// <summary><c>Ctrl+O</c> — the Windows-wide Open convention.</summary>
    OpenFile,

    /// <summary><c>Escape</c> — used only to abandon a control PrintFlow itself opened.</summary>
    Escape,

    /// <summary>
    /// <c>Ctrl+Shift+S</c> — raises the Save As surface, used <b>only</b> as a read-only
    /// document-identity probe that is then cancelled (Epic 11400 Part A §13).
    /// </summary>
    /// <remarks>
    /// Added because live discovery established that Photoshop CC 2019 publishes no UI
    /// Automation tree at all — its entire main window is a single element with no children —
    /// so there is no structural surface anywhere that names the loaded document. The Save As
    /// dialog is the strongest safe identity source the workstation actually exposes: it
    /// pre-fills the document's own basename and its own containing folder, and it can be
    /// abandoned through a positively identified Cancel control without writing anything.
    /// <para>
    /// Naming it <c>SaveAs<b>Probe</b></c> rather than <c>SaveAs</c> is deliberate. Nothing in
    /// this solution may use this value to save a file: raising the surface and cancelling it is
    /// the whole of its permitted use, and the name says so at every call site.
    /// </para>
    /// </remarks>
    SaveAsProbe,

    /// <summary>
    /// <c>Ctrl+W</c> — closes the <i>active</i> document only, never the application.
    /// </summary>
    /// <remarks>
    /// The scoping is a live-observed fact rather than an assumption: with two documents open,
    /// this closed exactly the active one and left the other loaded (Epic 11400 Part A §21).
    /// That is precisely why it is the only close route offered — there is no "close all" value
    /// here, and an operator's unrelated document cannot be closed by a caller that has proved
    /// which document is active.
    /// </remarks>
    CloseActiveDocument,
}

/// <summary>
/// The one and only keyboard-input primitive in the solution (Epic 11300 Part A §3).
/// </summary>
/// <remarks>
/// It takes the window the keystroke is for, and every implementation must re-read the
/// foreground and compare it against that window <b>before</b> the key leaves — never after.
/// A mismatch is <see cref="FailureCode.MeituTargetLost"/> with nothing sent.
///
/// There is no mouse counterpart, by design. Clicks go through
/// <see cref="IUiElementProvider"/>, which invokes a named element via UI Automation and
/// therefore never needs a screen coordinate (Epic 11300 Part A §4 priority 1).
/// </remarks>
public interface IScopedInputSink
{
    /// <summary>
    /// Sends <paramref name="shortcut"/> to <paramref name="verifiedTarget"/>, or sends nothing.
    /// </summary>
    /// <param name="verifiedTarget">
    /// A window the caller has already established belongs to the expected process. This
    /// implementation checks that it also currently holds the foreground.
    /// </param>
    OperationResult<Unit> SendShortcut(WindowHandle verifiedTarget, KnownShortcut shortcut);

    /// <summary>Observes dispatch after the final foreground guard. Legacy failures leave it unknown.</summary>
    OperationResult<Unit> SendShortcut(WindowHandle verifiedTarget, KnownShortcut shortcut, Action? dispatching)
    {
        OperationResult<Unit> result = SendShortcut(verifiedTarget, shortcut);
        if (result.IsSuccess) dispatching?.Invoke();
        return result;
    }
}
