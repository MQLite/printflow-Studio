using PrintFlow.Infrastructure.Diagnostics;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Integration.Persistence;

/// <summary>
/// The real <see cref="SystemProcessLiveness"/>, in the cases that can be decided without
/// racing a real process (Epic 11100 Part 3B §5, §13).
/// </summary>
/// <remarks>
/// The <see cref="ProcessLiveness.Alive"/> answer is deliberately not asserted here: proving it
/// would mean spawning a second process named like this one and hoping it outlives the
/// assertion, which is exactly the flakiness Part 3B §13 rules out. What matters and is
/// testable is the fail-closed half — that nothing unverifiable is ever reported as dead.
/// </remarks>
public sealed class ProcessLivenessTests
{
    [Fact]
    public void A_claim_from_another_machine_cannot_be_verified_and_is_not_called_dead()
    {
        SystemProcessLiveness liveness = new();

        liveness.Check(Environment.ProcessId + 1, Environment.MachineName + "-ELSEWHERE")
            .ShouldBe(ProcessLiveness.Unknown);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_claim_with_no_machine_name_cannot_be_verified_and_is_not_called_dead(string? machineName)
    {
        SystemProcessLiveness liveness = new();

        liveness.Check(Environment.ProcessId + 1, machineName).ShouldBe(ProcessLiveness.Unknown);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_claim_with_no_usable_process_id_is_not_called_dead(int processId)
    {
        SystemProcessLiveness liveness = new();

        liveness.Check(processId, Environment.MachineName).ShouldBe(ProcessLiveness.Unknown);
    }

    [Fact]
    public void A_process_id_nothing_is_running_under_is_dead()
    {
        SystemProcessLiveness liveness = new();

        // Windows process ids are multiples of four; an odd id is never allocated, so this
        // asks about an id that provably names nothing rather than gambling on a free one.
        liveness.Check(999_999_997, Environment.MachineName).ShouldBe(ProcessLiveness.Dead);
    }

    [Fact]
    public void A_claim_naming_this_very_process_is_a_recycled_id_from_an_earlier_run()
    {
        SystemProcessLiveness liveness = new();

        // Recovery runs before this process claims anything, so a claim carrying its id was
        // left by a previous run whose id Windows has since handed out again.
        liveness.Check(Environment.ProcessId, Environment.MachineName).ShouldBe(ProcessLiveness.Dead);
    }
}
