using PrintFlow.Domain.Sessions;

namespace PrintFlow.Workflow.Engine;

/// <summary>
/// The two session-level legality rules, stated once (Epic 11100 plan §8.2).
/// </summary>
/// <remarks>
/// <see cref="WorkflowEngine"/> guards its commands with these, and the Home screen's
/// "Recent Processing" rows report them so a listed session offers exactly the entry actions
/// the engine would accept. Extracting them is what keeps that from becoming a second,
/// UI-only copy of the rule that can drift (MVP design invariant 12).
/// <para>
/// They answer only the session-level half of legality. Whether a <i>particular</i> command is
/// legal still depends on step state and is decided by <see cref="WorkflowEngine.Apply"/> — a
/// row saying <see cref="AllowsAbandon"/> is a reason to offer the button, never a reason to
/// skip the command path.
/// </para>
/// </remarks>
public static class SessionStateRules
{
    /// <summary>
    /// Whether ordinary workflow progress is legal: only an <see cref="SessionState.Active"/>
    /// session can be driven forward. A handed-off session ended automated progression, and a
    /// completed or abandoned one is terminal.
    /// </summary>
    public static bool AllowsProgress(SessionState state) => state is SessionState.Active;

    /// <summary>
    /// Whether <c>AbandonSession</c> is legal. A handed-off session may still be abandoned —
    /// the work left automation, not the record (MVP design §6.5).
    /// </summary>
    public static bool AllowsAbandon(SessionState state) =>
        state is SessionState.Active or SessionState.HandedOff;

    /// <summary>
    /// Whether the operator may take this session's record off Recent Processing
    /// (Jira 11602: "record management must respect active or interrupted processing safety").
    /// </summary>
    /// <remarks>
    /// Only a session that has finished one way or the other. <see cref="SessionState.Active"/>
    /// is work in progress and <see cref="SessionState.HandedOff"/> is work the operator is
    /// still finishing by hand — both are exactly the entries an interrupted workstation needs
    /// to find again, so neither may be taken off the list. A session that has to be tidied away
    /// first is abandoned through the ordinary command path and can be removed afterwards; that
    /// is why Abandon and Remove are two actions and not two names for one
    /// (MVP design §6.5, §6.6).
    /// <para>
    /// Session-level legality only, exactly like its two neighbours. Whether a <i>particular</i>
    /// session may be removed also depends on facts no state name carries — an attempt still
    /// running or interrupted, or the automation lock still held — and those are re-checked
    /// against persisted rows by <c>SessionService.RemoveFromRecentAsync</c> and once more in
    /// the repository's own guarded statement. A row saying true here is a reason to offer the
    /// button, never a reason to skip that path.
    /// </para>
    /// </remarks>
    public static bool AllowsRecordRemoval(SessionState state) =>
        state is SessionState.Completed or SessionState.Abandoned;
}
