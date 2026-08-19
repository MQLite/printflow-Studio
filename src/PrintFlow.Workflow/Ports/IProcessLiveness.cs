namespace PrintFlow.Workflow.Ports;

/// <summary>What could be established about a recorded process owner (Epic 11100 Part 3B §5).</summary>
public enum ProcessLiveness
{
    /// <summary>The owning PrintFlow process is confirmed gone. Its claims may be recovered.</summary>
    Dead,

    /// <summary>The owning PrintFlow process is confirmed still running. Its claims are untouchable.</summary>
    Alive,

    /// <summary>Ownership could not be established. Treated exactly like <see cref="Alive"/>: fail closed.</summary>
    Unknown,
}

/// <summary>
/// Answers whether the process recorded as owning something is still that process
/// (Epic 11100 Part 3B §5).
/// </summary>
/// <remarks>
/// Behind an interface for two reasons. First, so business code never hard-codes
/// <c>Process.GetProcessById</c> — the workflow layer must stay free of the OS. Second, so a
/// test can assert the "dead owner releases / live owner is never stolen from" rules without
/// racing real operating-system processes, which would be exactly the kind of flaky test the
/// rule exists to prevent.
///
/// A recorded process id alone is not identity: PIDs are recycled. An implementation must
/// therefore confirm that the id still names a <b>PrintFlow</b> process, and must answer
/// <see cref="ProcessLiveness.Unknown"/> — never <see cref="ProcessLiveness.Dead"/> — whenever
/// it cannot tell.
/// </remarks>
public interface IProcessLiveness
{
    /// <summary>Checks the process recorded as <paramref name="processId"/> on <paramref name="machineName"/>.</summary>
    ProcessLiveness Check(int processId, string? machineName);
}
