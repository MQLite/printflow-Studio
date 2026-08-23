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
}
