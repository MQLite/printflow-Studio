using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Services;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// An <see cref="IStartupRecoveryService"/> that records how often startup called it, and with
/// what already true of the world at that moment (Epic 11100 Part 3C1 §9, §10, §11).
/// </summary>
/// <remarks>
/// Substituted into the composition root through its test seam so the assertions are about the
/// startup sequence's behaviour — call count, ordering, and how it treats a returned failure —
/// rather than about recovery itself, which Part 3B already proves against real state.
/// </remarks>
internal sealed class RecordingStartupRecoveryService : IStartupRecoveryService
{
    private readonly Func<StartupRecoveryReport>? _reportFactory;
    private readonly OperationFailure? _failure;
    private readonly Action? _onCalled;

    public RecordingStartupRecoveryService(
        Func<StartupRecoveryReport>? reportFactory = null,
        OperationFailure? failure = null,
        Action? onCalled = null)
    {
        _reportFactory = reportFactory;
        _failure = failure;
        _onCalled = onCalled;
    }

    public int CallCount { get; private set; }

    public Task<OperationResult<StartupRecoveryReport>> RecoverAsync(CancellationToken cancellationToken)
    {
        CallCount++;
        _onCalled?.Invoke();

        return Task.FromResult(_failure is not null
            ? OperationResult<StartupRecoveryReport>.Failed(_failure)
            : OperationResult.Ok(_reportFactory?.Invoke() ?? StartupRecoveryReport.Empty));
    }
}
