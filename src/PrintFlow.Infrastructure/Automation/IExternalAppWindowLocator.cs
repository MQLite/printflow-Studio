using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Automation;

/// <summary>
/// Finds and identifies the process and top-level window of one specific external application
/// (Epic 11300 Part A §8).
/// </summary>
/// <remarks>
/// The shape of this interface is the safety property. Every lookup is anchored to an
/// executable path the caller must already have obtained from signed configuration, and every
/// window it returns has been shown to belong to a process it returned first. There is
/// deliberately no "find any window whose title contains X", no "enumerate the desktop", and —
/// most importantly — no method that clicks, types or moves the mouse. Input lives behind
/// <see cref="IScopedInputSink"/> and <see cref="IUiElementProvider"/>, both of which require a
/// window this locator has already vouched for.
///
/// Every method converts its OS faults into an <see cref="OperationResult{T}"/> at this
/// boundary; no Win32 exception escapes into the adapter (Epic 11300 Part A §21).
/// </remarks>
public interface IExternalAppWindowLocator
{
    /// <summary>
    /// Returns every live process whose main module is exactly
    /// <paramref name="executableAbsolutePath"/>.
    /// </summary>
    /// <remarks>
    /// An empty list is a success carrying no elements, not a failure: "Meitu is not running"
    /// is an ordinary starting condition, and the caller — not this seam — decides whether that
    /// warrants a launch (Epic 11300 Part A §14, §15).
    /// </remarks>
    OperationResult<IReadOnlyList<ExternalProcessRef>> FindProcessesByExecutable(string executableAbsolutePath);

    /// <summary>
    /// Starts <paramref name="executableAbsolutePath"/> and returns the process that was
    /// created.
    /// </summary>
    /// <remarks>
    /// Takes no arguments and no working directory on purpose: this slice launches the accepted
    /// binary and nothing else, so there is no surface through which a command line could be
    /// assembled from operator input.
    /// </remarks>
    OperationResult<ExternalProcessRef> Launch(string executableAbsolutePath);

    /// <summary>Whether the identified process is still alive under the same identity.</summary>
    bool IsAlive(ExternalProcessRef process);

    /// <summary>
    /// Returns the visible top-level windows owned by <paramref name="process"/>, largest
    /// first.
    /// </summary>
    /// <remarks>
    /// Ownership is established from the window's own thread/process, not from a title match,
    /// so a decoy window in another process can never be returned here.
    /// </remarks>
    OperationResult<IReadOnlyList<ExternalWindowRef>> FindTopLevelWindows(ExternalProcessRef process);

    /// <summary>
    /// Returns the windows owned by <paramref name="process"/> that are modal-shaped — owned
    /// pop-ups sitting above <paramref name="mainWindow"/>.
    /// </summary>
    /// <remarks>
    /// Reported so a blocking dialog produces <see cref="FailureCode.MeituBlockingDialog"/>
    /// rather than an unexplained failure to find a known control. Nothing here dismisses one.
    /// </remarks>
    OperationResult<IReadOnlyList<ExternalWindowRef>> FindOwnedDialogs(
        ExternalProcessRef process, ExternalWindowRef mainWindow);

    /// <summary>Re-reads the live state of a window previously returned by this locator.</summary>
    /// <remarks>
    /// Used before every interaction: a handle is a number that stays valid-looking after the
    /// window it named has been destroyed, so the current read — not the cached record — is
    /// what a decision is allowed to rest on.
    /// </remarks>
    OperationResult<ExternalWindowRef> Refresh(WindowHandle handle);

    /// <summary>Brings <paramref name="window"/> to the foreground, restoring it if minimised.</summary>
    /// <remarks>
    /// Success here means the call was made without error; it does <b>not</b> mean the window is
    /// now in front. Windows may refuse a foreground change, so the caller must still verify
    /// through <see cref="ReadForeground"/> before sending anything (Epic 11300 Part A §10).
    /// </remarks>
    OperationResult<Unit> Activate(ExternalWindowRef window);

    /// <summary>Reads what holds the foreground right now.</summary>
    OperationResult<ForegroundIdentity> ReadForeground();
}
