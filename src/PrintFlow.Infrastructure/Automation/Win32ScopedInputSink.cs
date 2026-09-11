using System.Runtime.InteropServices;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Automation;

/// <summary>
/// Sends a named keystroke through <c>SendInput</c>, but only after confirming that the
/// intended window is the one that will receive it.
/// </summary>
/// <remarks>
/// <c>SendInput</c> delivers to whatever holds the foreground; that is the exact hazard
/// recorded in Epic 11300 Part A §3, where a stray keystroke once reached an Explorer rename
/// field. The mitigation is structural rather than procedural: this class reads the foreground
/// immediately before the call and returns <see cref="FailureCode.MeituTargetLost"/> —
/// having sent nothing — whenever it is not the verified target. The guard is inside the
/// primitive, so it cannot be forgotten by a caller and cannot be reordered by one.
///
/// The check and the send are not atomic; nothing on Windows can make them so. What the
/// ordering guarantees is that PrintFlow never types <i>because it assumed</i>, only ever
/// after it looked.
/// </remarks>
public sealed class Win32ScopedInputSink : IScopedInputSink
{
    private const ushort VkControl = 0x11;
    private const ushort VkShift = 0x10;
    private const ushort VkEscape = 0x1B;
    private const ushort VkO = 0x4F;
    private const ushort VkS = 0x53;
    private const ushort VkW = 0x57;

    private readonly IExternalAppWindowLocator _locator;
    private readonly Func<uint, NativeMethods.KEYBOARDINPUT[], int, uint> _sendInput;

    public Win32ScopedInputSink(IExternalAppWindowLocator locator)
        : this(locator, NativeMethods.SendInput)
    {
    }

    /// <summary>Recording native boundary for isolated guard/partial-dispatch verification.</summary>
    internal Win32ScopedInputSink(
        IExternalAppWindowLocator locator, Func<uint, NativeMethods.KEYBOARDINPUT[], int, uint> sendInput)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(sendInput);
        _locator = locator;
        _sendInput = sendInput;
    }

    /// <inheritdoc />
    public OperationResult<Unit> SendShortcut(WindowHandle verifiedTarget, KnownShortcut shortcut) =>
        SendShortcut(verifiedTarget, shortcut, dispatching: null);

    public OperationResult<Unit> SendShortcut(WindowHandle verifiedTarget, KnownShortcut shortcut, Action? dispatching)
    {
        if (verifiedTarget.IsNone)
        {
            return OperationResult.Fail<Unit>(
                FailureCode.MeituTargetLost, "No target window was supplied; no key was sent.");
        }

        OperationResult<ForegroundIdentity> foreground = _locator.ReadForeground();
        if (foreground.IsFailure)
        {
            return OperationResult.Fail<Unit>(foreground.Failure);
        }

        if (foreground.Value.Handle != verifiedTarget)
        {
            return OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.MeituTargetLost,
                $"The foreground window is {foreground.Value.Handle} owned by " +
                $"'{foreground.Value.ProcessName}' (process {foreground.Value.ProcessId}), not the " +
                $"verified target {verifiedTarget}. No key was sent.",
                isRetryable: true,
                context: new Dictionary<string, string>
                {
                    ["expectedWindow"] = verifiedTarget.ToString(),
                    ["actualWindow"] = foreground.Value.Handle.ToString(),
                    ["actualProcess"] = foreground.Value.ProcessName,
                    ["inputSent"] = "false",
                }));
        }

        NativeMethods.KEYBOARDINPUT[] sequence = Build(shortcut);
        dispatching?.Invoke();
        uint sent = _sendInput(
            (uint)sequence.Length, sequence, Marshal.SizeOf<NativeMethods.KEYBOARDINPUT>());

        return sent == sequence.Length
            ? OperationResult.Ok()
            : OperationResult.Fail<Unit>(
                FailureCode.MeituOpenInputFailed,
                $"SendInput accepted {sent} of {sequence.Length} events for {shortcut}.");
    }

    private static NativeMethods.KEYBOARDINPUT[] Build(KnownShortcut shortcut) => shortcut switch
    {
        KnownShortcut.OpenFile =>
        [
            Key(VkControl, down: true),
            Key(VkO, down: true),
            Key(VkO, down: false),
            Key(VkControl, down: false),
        ],
        KnownShortcut.Escape => [Key(VkEscape, down: true), Key(VkEscape, down: false)],
        KnownShortcut.SaveAsProbe =>
        [
            Key(VkControl, down: true),
            Key(VkShift, down: true),
            Key(VkS, down: true),
            Key(VkS, down: false),
            Key(VkShift, down: false),
            Key(VkControl, down: false),
        ],
        KnownShortcut.CloseActiveDocument =>
        [
            Key(VkControl, down: true),
            Key(VkW, down: true),
            Key(VkW, down: false),
            Key(VkControl, down: false),
        ],
        _ => throw new ArgumentOutOfRangeException(
            nameof(shortcut), shortcut, "Unknown shortcut; no key sequence is defined for it."),
    };

    private static NativeMethods.KEYBOARDINPUT Key(ushort virtualKey, bool down) => new()
    {
        type = NativeMethods.INPUT_KEYBOARD,
        ki = new NativeMethods.KEYBDINPUT
        {
            wVk = virtualKey,
            dwFlags = down ? 0 : NativeMethods.KEYEVENTF_KEYUP,
        },
    };
}
