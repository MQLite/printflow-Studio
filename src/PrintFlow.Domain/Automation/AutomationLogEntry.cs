using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Results;
using PrintFlow.Domain.Sessions;

namespace PrintFlow.Domain.Automation;

/// <summary>
/// One structured automation error, recorded durably (Jira 11108; MVP design §17.6 —
/// "AutomationLog stores structured errors and screenshot paths").
/// </summary>
/// <remarks>
/// Deliberately narrow. This is <b>not</b> a general lifecycle log and not a logging framework:
/// the <c>AutomationLogEntry</c> table declares <c>FailureCode</c>, <c>MessageKey</c> and
/// <c>TechnicalDetail</c> NOT NULL, so every row it can hold is a structured error. An event
/// that carries no <see cref="OperationFailure"/> — a crash-recovered <c>Interrupted</c> attempt,
/// for instance — therefore has no row here rather than an invented code.
/// <para>
/// It overlaps <c>ProcessingAttempt.FailureDetailJson</c> on purpose, and the two are not the
/// same record. The attempt row is the <i>workflow</i> record: it belongs to a session, is
/// <c>ON DELETE CASCADE</c> with it, and always names a step. This is the <i>diagnostic</i>
/// record: <see cref="SessionId"/> and <see cref="Step"/> are both optional so an error that
/// belongs to no session can still be recorded, the row is <c>ON DELETE SET NULL</c> so it
/// outlives the session it came from, and the screenshot is a first-class field rather than an
/// ad-hoc key buried in a JSON blob.
/// </para>
/// <para>
/// The operator-facing Error Details surface that will present these rows is SCRUM-11120, and
/// rolling text logs and their retention are SCRUM-11121. Neither is implemented here.
/// </para>
/// </remarks>
/// <param name="Id">Identity of this log row.</param>
/// <param name="SessionId">The session the error belongs to, or null when it belongs to none.</param>
/// <param name="Step">The workflow step being attempted, or null when the error had no step.</param>
/// <param name="AtUtc">When the error was recorded. Always UTC.</param>
/// <param name="Failure">The structured error: stable English code, message key, detail, context.</param>
/// <param name="ScreenshotPath">
/// The local failure capture, when the adapter took one. Local path only — the capture itself is
/// never copied, committed or uploaded (Epic 11300 Part A §20).
/// </param>
public sealed record AutomationLogEntry(
    AutomationLogId Id,
    SessionId? SessionId,
    StepKind? Step,
    DateTimeOffset AtUtc,
    OperationFailure Failure,
    string? ScreenshotPath)
{
    /// <summary>
    /// The <see cref="OperationFailure.Context"/> key the Meitu and Photoshop adapters already
    /// use for a window capture they attached to a failure.
    /// </summary>
    /// <remarks>
    /// Named once, here, rather than repeated at each call site: promoting that value to
    /// <see cref="ScreenshotPath"/> is exactly what makes the screenshot queryable instead of
    /// reachable only by re-parsing an attempt's failure JSON.
    /// </remarks>
    public const string ScreenshotContextKey = "evidencePath";

    /// <summary>
    /// Records an automation stop — an attempt that ended <c>Failed</c> or <c>Cancelled</c> —
    /// as one log row, lifting the adapter's capture path out of the failure context.
    /// </summary>
    public static AutomationLogEntry ForStop(
        AutomationLogId id,
        SessionId sessionId,
        StepKind step,
        DateTimeOffset atUtc,
        OperationFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        string? screenshot =
            failure.Context.TryGetValue(ScreenshotContextKey, out string? path) &&
            !string.IsNullOrWhiteSpace(path)
                ? path
                : null;

        return new AutomationLogEntry(id, sessionId, step, atUtc, failure, screenshot);
    }
}
