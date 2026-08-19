using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// A scripted <see cref="IProcessLiveness"/> for the stale-lock tests (Epic 11100 Part 3B §13).
/// </summary>
/// <remarks>
/// Racing two real operating-system processes to prove "a live owner is never stolen from"
/// would produce exactly the flaky test the rule exists to prevent, so the answer is scripted
/// and the recovery service's decision is what gets tested. <see cref="LastProcessId"/> and
/// <see cref="LastMachineName"/> record the arguments, so a test can also assert that recovery
/// asked about the owner the lock actually names.
/// </remarks>
internal sealed class FakeProcessLiveness : IProcessLiveness
{
    private ProcessLiveness _answer;

    public FakeProcessLiveness(ProcessLiveness answer = ProcessLiveness.Dead) => _answer = answer;

    public int? LastProcessId { get; private set; }

    public string? LastMachineName { get; private set; }

    public int CallCount { get; private set; }

    public void Answer(ProcessLiveness answer) => _answer = answer;

    public ProcessLiveness Check(int processId, string? machineName)
    {
        LastProcessId = processId;
        LastMachineName = machineName;
        CallCount++;
        return _answer;
    }
}
