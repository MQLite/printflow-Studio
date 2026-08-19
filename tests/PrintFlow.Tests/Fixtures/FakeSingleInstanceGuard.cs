using PrintFlow.Infrastructure.Startup;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// A scripted <see cref="ISingleInstanceGuard"/> for the startup-sequence tests
/// (Epic 11100 Part 3C1 §10).
/// </summary>
/// <remarks>
/// Racing two real WPF processes to prove "the second instance runs no recovery" is exactly the
/// unreliable test the rule against process races exists to prevent, so the guard's answer is
/// scripted and what gets tested is the sequence's reaction to it. The real
/// <see cref="SingleInstanceGuard"/> is proved separately, against real Windows primitives, in
/// <c>SingleInstanceGuardTests</c>.
/// </remarks>
internal sealed class FakeSingleInstanceGuard : ISingleInstanceGuard
{
    private readonly SingleInstanceOutcome _outcome;

    public FakeSingleInstanceGuard(SingleInstanceOutcome outcome = SingleInstanceOutcome.Acquired) =>
        _outcome = outcome;

    public int AcquireCallCount { get; private set; }

    public int DisposeCallCount { get; private set; }

    public bool IsHeld { get; private set; }

    public SingleInstanceOutcome TryAcquire()
    {
        AcquireCallCount++;
        IsHeld = _outcome == SingleInstanceOutcome.Acquired;
        return _outcome;
    }

    public void Dispose()
    {
        DisposeCallCount++;
        IsHeld = false;
    }
}
