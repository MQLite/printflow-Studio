using System.Globalization;
using PrintFlow.Domain.Results;

namespace PrintFlow.Infrastructure.Automation;

/// <summary>
/// Describes a UI Automation read that was interrupted because elements beneath the root
/// window changed while the walk was in progress, and tells that apart from losing the root.
/// </summary>
/// <remarks>
/// A descendant search raises <c>ElementNotAvailableException</c> both when the root window
/// is destroyed and when any element the walk reaches is removed mid-walk. Meitu removes its
/// short-lived processing overlay when it presents a result, so the second case is an ordinary
/// transition. Run a2-v3-20260917-131609-d2a0a55e reported it as "window disappeared" even though
/// the same handle was freshly read as a live, unrecognised screen two seconds later.
/// <para>
/// The failure keeps <see cref="FailureCode.MeituTargetLost"/>, because callers that do not
/// re-observe must still stop. It is not evidence about what is on screen. A caller may only
/// act on it by discarding the whole read and taking a complete new observation of the same
/// verified target within its existing budget.
/// </para>
/// </remarks>
public static class UiReadInterruption
{
    public const string ContextKey = "readInterruption";
    public const string DescendantUnavailable = "descendant-unavailable";
    public const string RootWindowReadableKey = "rootWindowReadable";

    /// <summary>Builds the failure, preserving the original exception even when its message is empty.</summary>
    public static OperationFailure Create(
        WindowHandle root, string activity, Exception exception, bool rootWindowReadable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activity);
        ArgumentNullException.ThrowIfNull(exception);

        string message = string.IsNullOrEmpty(exception.Message) ? "(empty)" : exception.Message;
        string original =
            $"{exception.GetType().FullName} HResult=0x{exception.HResult:X8} Message={message}";
        string detail = rootWindowReadable
            ? $"Elements beneath window {root} changed while {activity}; the window itself was " +
              $"still readable afterwards. The read was discarded. Original: {original}"
            : $"Window {root} disappeared while {activity}. Original: {original}";

        return OperationFailure.Create(
            FailureCode.MeituTargetLost,
            detail,
            isRetryable: true,
            context: new Dictionary<string, string>
            {
                [ContextKey] = rootWindowReadable ? DescendantUnavailable : "root-unavailable",
                [RootWindowReadableKey] = rootWindowReadable ? "true" : "false",
                ["windowHandle"] = root.ToString(),
                ["activity"] = activity,
                ["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name,
                ["exceptionHResult"] =
                    "0x" + exception.HResult.ToString("X8", CultureInfo.InvariantCulture),
                ["exceptionMessage"] = message,
                ["inputSent"] = "false",
            });
    }

    /// <summary>
    /// True only for a failure built by <see cref="Create"/> whose root window was still readable.
    /// </summary>
    public static bool IsDescendantChange(OperationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return failure.Code == FailureCode.MeituTargetLost &&
            failure.Context.TryGetValue(ContextKey, out string? kind) &&
            string.Equals(kind, DescendantUnavailable, StringComparison.Ordinal) &&
            failure.Context.TryGetValue(RootWindowReadableKey, out string? readable) &&
            string.Equals(readable, "true", StringComparison.Ordinal);
    }
}
