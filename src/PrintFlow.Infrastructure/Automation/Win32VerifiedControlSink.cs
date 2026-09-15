using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Automation;

/// <summary>
/// The Windows implementation of <see cref="IVerifiedControlSink"/>.
/// </summary>
/// <remarks>
/// One rule holds in every method and is implemented in exactly one place
/// (<see cref="Verify"/>): before a control is read, written or pressed, its handle is
/// re-checked against the live desktop — the window still exists, it still reports the expected
/// process, it still carries the expected class, and it is visible and enabled. A handle is a
/// number that keeps looking valid after the window it named has been destroyed and reissued,
/// so the cached record is never what a decision rests on.
///
/// Failures are reported with the Meitu-era codes that the shared automation seam already uses;
/// an adapter that needs its own vocabulary translates them at its own boundary rather than
/// this class guessing which application it is serving.
/// </remarks>
public sealed class Win32VerifiedControlSink : IVerifiedControlSink
{
    private const int MaxTextLength = 4096;
    private const int MaxClassNameLength = 256;

    /// <inheritdoc />
    public OperationResult<VerifiedControlRef> Locate(
        ExternalProcessRef owner, WindowHandle host, int controlId, string expectedClassName)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedClassName);

        OperationResult<Unit> hostOwnership = VerifyOwnership(owner, host, "host window");
        if (hostOwnership.IsFailure)
        {
            return OperationResult.Fail<VerifiedControlRef>(hostOwnership.Failure);
        }

        // Descendants, not just immediate children: the standard file dialog nests its filename
        // edit two levels down inside a ComboBoxEx32, and the control id is what identifies it
        // at whatever depth the shell happens to place it.
        List<nint> matches = [];
        foreach (nint candidate in Descendants(host.Value))
        {
            if (NativeMethods.GetDlgCtrlID(candidate) == controlId &&
                string.Equals(ClassOf(candidate), expectedClassName, StringComparison.Ordinal))
            {
                matches.Add(candidate);
            }
        }

        if (matches.Count == 0)
        {
            return OperationResult.Fail<VerifiedControlRef>(OperationFailure.Create(
                FailureCode.MeituUnknownState,
                $"No control with id {controlId} and class '{expectedClassName}' exists beneath " +
                $"window {host}. Nothing was written or pressed.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["controlId"] = controlId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["expectedClass"] = expectedClassName,
                    ["inputSent"] = "false",
                }));
        }

        if (matches.Count > 1)
        {
            // Which of several identically-shaped controls is "the" one is not a question this
            // seam may answer by taking the first. A caller that legitimately expects more than
            // one uses LocateByClass and resolves the ambiguity against signed evidence.
            return OperationResult.Fail<VerifiedControlRef>(OperationFailure.Create(
                FailureCode.MeituUnknownState,
                $"{matches.Count} controls beneath window {host} carry id {controlId} and class " +
                $"'{expectedClassName}'. PrintFlow will not choose between them.",
                isRetryable: false,
                context: new Dictionary<string, string>
                {
                    ["controlId"] = controlId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["expectedClass"] = expectedClassName,
                    ["candidates"] = matches.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["inputSent"] = "false",
                }));
        }

        return OperationResult.Ok(
            new VerifiedControlRef(new WindowHandle(matches[0]), host, controlId, expectedClassName));
    }

    /// <inheritdoc />
    public OperationResult<IReadOnlyList<VerifiedControlRef>> LocateByClass(
        ExternalProcessRef owner, WindowHandle host, string expectedClassName)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedClassName);

        OperationResult<Unit> hostOwnership = VerifyOwnership(owner, host, "host window");
        if (hostOwnership.IsFailure)
        {
            return OperationResult.Fail<IReadOnlyList<VerifiedControlRef>>(hostOwnership.Failure);
        }

        List<VerifiedControlRef> matches = [];
        foreach (nint candidate in Descendants(host.Value))
        {
            if (string.Equals(ClassOf(candidate), expectedClassName, StringComparison.Ordinal))
            {
                matches.Add(new VerifiedControlRef(
                    new WindowHandle(candidate), host, NativeMethods.GetDlgCtrlID(candidate), expectedClassName));
            }
        }

        return OperationResult.Ok<IReadOnlyList<VerifiedControlRef>>(matches);
    }

    /// <inheritdoc />
    public OperationResult<IReadOnlyList<string>> LocateVisibleClasses(
        ExternalProcessRef owner, WindowHandle host, int maxItems)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxItems);

        OperationResult<Unit> hostOwnership = VerifyOwnership(owner, host, "host window");
        if (hostOwnership.IsFailure)
        {
            return OperationResult.Fail<IReadOnlyList<string>>(hostOwnership.Failure);
        }

        HashSet<string> classes = new(StringComparer.Ordinal);
        foreach (nint candidate in Descendants(host.Value))
        {
            if (classes.Count >= maxItems)
            {
                break;
            }

            if (NativeMethods.IsWindowVisible(candidate))
            {
                string name = ClassOf(candidate);
                if (name.Length > 0)
                {
                    classes.Add(name);
                }
            }
        }

        return OperationResult.Ok<IReadOnlyList<string>>([.. classes]);
    }

    /// <inheritdoc />
    public OperationResult<string> ReadText(ExternalProcessRef owner, VerifiedControlRef control)
    {
        OperationResult<Unit> verified = Verify(owner, control);
        if (verified.IsFailure)
        {
            return OperationResult.Fail<string>(verified.Failure);
        }

        ushort[] buffer = new ushort[MaxTextLength];
        NativeMethods.SendGetText(control.Handle.Value, NativeMethods.WM_GETTEXT, MaxTextLength, buffer);
        return OperationResult.Ok(Decode(buffer));
    }

    /// <inheritdoc />
    public OperationResult<Unit> VerifyActionable(ExternalProcessRef owner, VerifiedControlRef control) =>
        Verify(owner, control);

    /// <inheritdoc />
    public OperationResult<Unit> WriteText(
        ExternalProcessRef owner, VerifiedControlRef control, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        OperationResult<Unit> verified = Verify(owner, control);
        if (verified.IsFailure)
        {
            return verified;
        }

        NativeMethods.SendSetText(control.Handle.Value, NativeMethods.WM_SETTEXT, 0, value);
        return OperationResult.Ok();
    }

    /// <inheritdoc />
    public OperationResult<Unit> Press(ExternalProcessRef owner, VerifiedControlRef control) =>
        Press(owner, control, dispatching: null);

    public OperationResult<Unit> Press(ExternalProcessRef owner, VerifiedControlRef control, Action? dispatching)
    {
        OperationResult<Unit> verified = Verify(owner, control);
        if (verified.IsFailure)
        {
            return verified;
        }

        dispatching?.Invoke();
        NativeMethods.SendControlMessage(control.Handle.Value, NativeMethods.BM_CLICK, 0, 0);
        return OperationResult.Ok();
    }

    /// <summary>
    /// The one guard, applied identically before every read, write and press.
    /// </summary>
    private static OperationResult<Unit> Verify(ExternalProcessRef owner, VerifiedControlRef control)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(control);

        OperationResult<Unit> ownership = VerifyOwnership(owner, control.Handle, "control");
        if (ownership.IsFailure)
        {
            return ownership;
        }

        string actualClass = ClassOf(control.Handle.Value);
        if (!string.Equals(actualClass, control.ClassName, StringComparison.Ordinal))
        {
            return OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.MeituTargetLost,
                $"Control {control.Handle} is now class '{actualClass}', not the '{control.ClassName}' " +
                "it was located as. The handle has been reused; nothing was written or pressed.",
                isRetryable: true,
                context: new Dictionary<string, string>
                {
                    ["expectedClass"] = control.ClassName,
                    ["actualClass"] = actualClass,
                    ["inputSent"] = "false",
                }));
        }

        if (!NativeMethods.IsWindowVisible(control.Handle.Value) ||
            !NativeMethods.IsWindowEnabled(control.Handle.Value))
        {
            return OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.MeituTargetLost,
                $"Control {control.Handle} is not both visible and enabled, so it is not something " +
                "PrintFlow may read or drive. Nothing was written or pressed.",
                isRetryable: true,
                context: new Dictionary<string, string> { ["inputSent"] = "false" }));
        }

        return OperationResult.Ok();
    }

    private static OperationResult<Unit> VerifyOwnership(
        ExternalProcessRef owner, WindowHandle handle, string what)
    {
        if (handle.IsNone || !NativeMethods.IsWindow(handle.Value))
        {
            return OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.MeituTargetLost,
                $"The {what} {handle} no longer exists. Nothing was read, written or pressed.",
                isRetryable: true,
                context: new Dictionary<string, string> { ["inputSent"] = "false" }));
        }

        NativeMethods.GetWindowThreadProcessId(handle.Value, out uint actualProcessId);
        if (actualProcessId != (uint)owner.ProcessId)
        {
            return OperationResult.Fail<Unit>(OperationFailure.Create(
                FailureCode.MeituTargetLost,
                $"The {what} {handle} belongs to process {actualProcessId}, not the verified " +
                $"{owner.ProcessId}. Nothing was read, written or pressed.",
                isRetryable: true,
                context: new Dictionary<string, string>
                {
                    ["expectedProcessId"] =
                        owner.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["actualProcessId"] =
                        actualProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["inputSent"] = "false",
                }));
        }

        return OperationResult.Ok();
    }

    /// <summary>Every descendant window of <paramref name="parent"/>, at any depth.</summary>
    private static List<nint> Descendants(nint parent)
    {
        List<nint> found = [];
        NativeMethods.EnumChildWindows(
            parent,
            (handle, _) =>
            {
                found.Add(handle);
                return true;
            },
            0);

        return found;
    }

    private static string ClassOf(nint handle)
    {
        ushort[] buffer = new ushort[MaxClassNameLength];
        int length = NativeMethods.GetClassName(handle, buffer, buffer.Length);
        return length <= 0 ? string.Empty : Decode(buffer, length);
    }

    private static string Decode(ushort[] buffer)
    {
        int length = Array.IndexOf(buffer, (ushort)0);
        return Decode(buffer, length < 0 ? buffer.Length : length);
    }

    private static string Decode(ushort[] buffer, int length)
    {
        ReadOnlySpan<char> text = System.Runtime.InteropServices.MemoryMarshal
            .Cast<ushort, char>(buffer.AsSpan(0, Math.Min(length, buffer.Length)));
        return new string(text);
    }
}
