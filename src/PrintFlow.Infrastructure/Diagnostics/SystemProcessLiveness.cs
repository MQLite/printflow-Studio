using System.ComponentModel;
using System.Diagnostics;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Diagnostics;

/// <summary>
/// The real <see cref="IProcessLiveness"/>, answered from the operating system's process
/// table (Epic 11100 Part 3B §5).
/// </summary>
/// <remarks>
/// A process id on its own proves nothing, because Windows recycles them: the id recorded by
/// a crashed PrintFlow can belong to an unrelated program minutes later. Every "still alive"
/// answer here therefore requires the id to name a process whose name matches this
/// application's own — a different name means the id was recycled and the original owner is
/// gone.
///
/// The bias is deliberately asymmetric. Anything that cannot be established — a different
/// machine, a missing machine name, an access-denied query — answers
/// <see cref="ProcessLiveness.Unknown"/>, which callers treat exactly like
/// <see cref="ProcessLiveness.Alive"/>. Wrongly declaring a live owner dead would let two
/// processes drive Meitu at once; wrongly declaring a dead owner alive only leaves a lock
/// held until an operator intervenes.
/// </remarks>
public sealed class SystemProcessLiveness : IProcessLiveness
{
    private readonly string _machineName;
    private readonly string _ownProcessName;
    private readonly int _ownProcessId;

    public SystemProcessLiveness()
    {
        _machineName = Environment.MachineName;
        _ownProcessId = Environment.ProcessId;

        using Process self = Process.GetCurrentProcess();
        _ownProcessName = self.ProcessName;
    }

    /// <inheritdoc />
    public ProcessLiveness Check(int processId, string? machineName)
    {
        // A claim recorded on another workstation cannot be verified from here, and an
        // unrecorded machine name is no better. Neither is evidence of death.
        if (string.IsNullOrWhiteSpace(machineName) ||
            !string.Equals(machineName, _machineName, StringComparison.OrdinalIgnoreCase))
        {
            return ProcessLiveness.Unknown;
        }

        if (processId <= 0)
        {
            return ProcessLiveness.Unknown;
        }

        // Recovery runs at startup, before this process has claimed anything, so a claim
        // naming our own id can only be a recycled id left behind by an earlier run.
        if (processId == _ownProcessId)
        {
            return ProcessLiveness.Dead;
        }

        try
        {
            using Process owner = Process.GetProcessById(processId);

            if (owner.HasExited)
            {
                return ProcessLiveness.Dead;
            }

            return string.Equals(owner.ProcessName, _ownProcessName, StringComparison.OrdinalIgnoreCase)
                ? ProcessLiveness.Alive
                : ProcessLiveness.Dead;
        }
        catch (ArgumentException)
        {
            // No process carries this id: the owner is gone.
            return ProcessLiveness.Dead;
        }
        catch (InvalidOperationException)
        {
            // The process exited between lookup and inspection.
            return ProcessLiveness.Dead;
        }
        catch (Win32Exception)
        {
            // The process exists but cannot be inspected. Not evidence of death.
            return ProcessLiveness.Unknown;
        }
    }
}
