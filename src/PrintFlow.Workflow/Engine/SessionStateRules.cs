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
}
