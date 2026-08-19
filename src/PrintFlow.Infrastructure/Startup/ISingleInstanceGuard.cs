namespace PrintFlow.Infrastructure.Startup;

/// <summary>What an attempt to become the single running PrintFlow instance concluded.</summary>
public enum SingleInstanceOutcome
{
    /// <summary>This process owns the guard and is the primary instance.</summary>
    Acquired,

    /// <summary>Another PrintFlow instance already owns the guard. This process must exit.</summary>
    AlreadyRunning,

    /// <summary>
    /// The guard itself could not be evaluated — a denied or otherwise unusable operating-system
    /// primitive. Deliberately distinct from <see cref="AlreadyRunning"/>: it is an environment
    /// fault, not evidence of a second instance, and it fails startup closed rather than telling
    /// the operator something untrue.
    /// </summary>
    Unavailable,
}

/// <summary>
/// The machine-level claim that makes this process the one PrintFlow instance allowed to touch
/// application-owned state (Epic 11100 Part 3C1 §2).
/// </summary>
/// <remarks>
/// The invariant it exists to enforce: <b>startup crash recovery may run only from the process
/// that owns this guard</b>. Recovery reasons about "a Running attempt whose process is gone",
/// and a second instance starting while the first is mid-attempt would read exactly that shape
/// and interrupt work that is genuinely still running.
/// <para>
/// Ownership is held for the whole application lifetime and released only by
/// <see cref="IDisposable.Dispose"/> during ordinary shutdown. There is deliberately no public
/// "force release": a released guard while the owner keeps running is precisely the state the
/// guard exists to make impossible.
/// </para>
/// </remarks>
public interface ISingleInstanceGuard : IDisposable
{
    /// <summary>True once this process owns the guard, until it is disposed.</summary>
    bool IsHeld { get; }

    /// <summary>
    /// Claims the guard for this process. Idempotent: an owner that calls again stays the owner
    /// and does not re-enter the underlying primitive.
    /// </summary>
    SingleInstanceOutcome TryAcquire();
}
