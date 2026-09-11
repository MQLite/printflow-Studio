using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Automation;

/// <summary>
/// A child control that has been shown to live beneath one identified window and to belong to
/// one identified process.
/// </summary>
/// <param name="Handle">The control's own window handle.</param>
/// <param name="Host">The window it was located beneath.</param>
/// <param name="ControlId">Its dialog control id, which is what the caller asked for.</param>
/// <param name="ClassName">Its window class, re-read from the live control rather than assumed.</param>
/// <remarks>
/// Both halves of the identity are carried because a control id alone is not an identity: the
/// standard Open and Save As dialogs both expose a control with id <c>1001</c>, and only the
/// class tells the filename field apart from the address bar. Every method on
/// <see cref="IVerifiedControlSink"/> re-checks the class it was given, so a control that has
/// been replaced by a different one under the same id is refused rather than driven.
/// </remarks>
public sealed record VerifiedControlRef(
    WindowHandle Handle, WindowHandle Host, int ControlId, string ClassName);

/// <summary>
/// Reads and actuates Win32 child controls that belong to a verified process, addressed by
/// control id and exact window class (Epic 11400 Part A §10).
/// </summary>
/// <remarks>
/// This seam exists because of a live finding, not a preference. Photoshop CC 2019 publishes no
/// UI Automation tree whatsoever — its entire main window is a single element with no children —
/// and the standard <c>#32770</c> file dialog it raises exposes its filename field and its
/// buttons as pattern-less <c>Pane</c>s, so <see cref="IUiElementProvider"/> can neither write
/// the path nor press the button. Without something in this shape the only remaining route
/// would be typing a path blind and pressing Enter, which is precisely what Epic 11300 Part A §3
/// rules out.
///
/// What makes it safe is that every operation is addressed to a specific window handle that has
/// already been attributed to the expected process, so — unlike <see cref="IScopedInputSink"/>,
/// which necessarily delivers to whatever holds the foreground — nothing here can land on
/// another application. The verification is inside each method rather than left to the caller,
/// for the same reason the foreground check lives inside the input sink: a guard a caller can
/// forget is not a guard.
///
/// The surface is deliberately tiny, and it is not a step towards a general desktop automation
/// framework. There is no coordinate anywhere in it, no way to enumerate the desktop, no
/// free-form message primitive, and no method that takes a control the caller has not already
/// located through <see cref="Locate"/> or <see cref="LocateVisibleClasses"/>.
/// </remarks>
public interface IVerifiedControlSink
{
    /// <summary>
    /// Finds the control with <paramref name="controlId"/> beneath <paramref name="host"/>,
    /// requiring it to be of class <paramref name="expectedClassName"/> and owned by
    /// <paramref name="owner"/>.
    /// </summary>
    /// <remarks>
    /// A class mismatch is a failure, never a "close enough" match. The signed evidence records
    /// both the id and the class precisely so that a dialog which has changed shape produces a
    /// structured refusal instead of a write to whatever now sits at that id.
    /// </remarks>
    OperationResult<VerifiedControlRef> Locate(
        ExternalProcessRef owner, WindowHandle host, int controlId, string expectedClassName);

    /// <summary>
    /// Returns every control beneath <paramref name="host"/> whose class is exactly
    /// <paramref name="expectedClassName"/>, in enumeration order.
    /// </summary>
    /// <remarks>
    /// Exists so ambiguity can be seen rather than silently resolved: the Save As dialog carries
    /// two controls under id <c>1001</c>, and a rule that must pick the right one has to be able
    /// to observe both. Reading invokes nothing.
    /// </remarks>
    OperationResult<IReadOnlyList<VerifiedControlRef>> LocateByClass(
        ExternalProcessRef owner, WindowHandle host, string expectedClassName);

    /// <summary>
    /// Reads the distinct window classes of the <i>visible</i> children of
    /// <paramref name="host"/>, for state recognition.
    /// </summary>
    /// <remarks>
    /// Classes rather than text, and that is a privacy property as much as a robustness one.
    /// Photoshop's window tree contains the loaded document's own measurements and, on some
    /// screens, its name; a state classifier that reads class names alone cannot accidentally
    /// record a customer's file into a failure report (Epic 11300 Part A §31).
    /// </remarks>
    OperationResult<IReadOnlyList<string>> LocateVisibleClasses(
        ExternalProcessRef owner, WindowHandle host, int maxItems);

    /// <summary>Reads a located control's text.</summary>
    /// <remarks>
    /// A read: it changes nothing, focuses nothing, and cannot start anything. This is how the
    /// identity probe learns which document Photoshop is holding.
    /// </remarks>
    OperationResult<string> ReadText(ExternalProcessRef owner, VerifiedControlRef control);

    /// <summary>Sets a located control's text.</summary>
    /// <remarks>
    /// Preferred over typing for the reason the whole seam exists: the value is delivered to a
    /// named control already shown to live inside the verified window, so a focus change
    /// part-way through cannot redirect it into another application. The caller is still
    /// expected to read it back before acting on it — <see cref="ReadText"/> is what makes
    /// "the write landed" a checked fact rather than a bet.
    /// </remarks>
    OperationResult<Unit> WriteText(
        ExternalProcessRef owner, VerifiedControlRef control, string value);

    /// <summary>Presses a located button.</summary>
    /// <remarks>
    /// Sends the button's own click message to the button's own handle. No coordinate is
    /// computed, so there is no arrangement of windows in which this can press something else.
    /// </remarks>
    OperationResult<Unit> Press(ExternalProcessRef owner, VerifiedControlRef control);

    /// <summary>Observes click dispatch after the final control guard. Legacy failures leave it unknown.</summary>
    OperationResult<Unit> Press(ExternalProcessRef owner, VerifiedControlRef control, Action? dispatching)
    {
        OperationResult<Unit> result = Press(owner, control);
        if (result.IsSuccess) dispatching?.Invoke();
        return result;
    }
}
