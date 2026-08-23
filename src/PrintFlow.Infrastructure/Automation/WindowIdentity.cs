namespace PrintFlow.Infrastructure.Automation;

/// <summary>
/// An opaque top-level window handle.
/// </summary>
/// <remarks>
/// Wrapped rather than passed as a bare <see cref="nint"/> so that "a window PrintFlow has
/// identified" and "any integer" are different types. Nothing outside
/// <c>PrintFlow.Infrastructure</c> can name this type, which is what keeps raw handles out of
/// Workflow, the view models and the shell (Epic 11300 Part A §8, §26).
/// </remarks>
public readonly record struct WindowHandle(nint Value)
{
    /// <summary>No window.</summary>
    public static readonly WindowHandle None = new(0);

    public bool IsNone => Value == 0;

    public override string ToString() => $"0x{Value:X}";
}

/// <summary>Screen bounds of a window, recorded as evidence rather than used as a click target.</summary>
public readonly record struct WindowBounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public override string ToString() => $"{Left},{Top} {Width}x{Height}";
}

/// <summary>
/// A running process PrintFlow has positively identified by the executable it was launched
/// from.
/// </summary>
/// <param name="ProcessId">The OS process id.</param>
/// <param name="ExecutablePath">
/// The full path of the process's main module, read from the live process — not the path
/// PrintFlow asked for. Comparing the two is what turns "we launched this" into "this is what
/// is actually running".
/// </param>
/// <param name="StartedUtc">When the process started, so a restart is never mistaken for the same instance.</param>
public sealed record ExternalProcessRef(int ProcessId, string ExecutablePath, DateTimeOffset StartedUtc);

/// <summary>
/// A top-level window that has been shown to belong to a specific
/// <see cref="ExternalProcessRef"/>.
/// </summary>
/// <remarks>
/// <see cref="Title"/>, <see cref="ClassName"/> and <see cref="Bounds"/> are evidence recorded
/// alongside the decision, never the sole authority for it (Epic 11300 Part A §9): a title can
/// be copied by any application, so ownership by the verified process id is what actually
/// identifies the target.
/// </remarks>
public sealed record ExternalWindowRef(
    WindowHandle Handle,
    int OwningProcessId,
    string Title,
    string ClassName,
    WindowBounds Bounds,
    bool IsVisible,
    bool IsMinimised,
    bool IsEnabled);

/// <summary>What currently holds the foreground, as read from the OS at one instant.</summary>
/// <param name="Handle">The foreground window, or <see cref="WindowHandle.None"/> when there is none.</param>
/// <param name="ProcessId">The process that owns it, or 0 when unknown.</param>
/// <param name="ProcessName">
/// The owning process name, for the failure record. Present so an operator reading
/// <c>MeituTargetLost</c> can see that Explorer — not Meitu — was in front.
/// </param>
public readonly record struct ForegroundIdentity(WindowHandle Handle, int ProcessId, string ProcessName);
