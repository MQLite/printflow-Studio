using Microsoft.Extensions.Time.Testing;
using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Tests.Fixtures;

/// <summary>
/// A Photoshop adapter that moves the controlled clock forward while it "works", then does
/// whatever the test scripted.
/// </summary>
/// <remarks>
/// The seam the attempt-timing tests need, and the only one that can prove the property in
/// question. Every other double in this repository completes inside a single clock tick, so a
/// run through them cannot distinguish "the closing timestamp was observed at the close" from
/// "the opening timestamp was copied to the close" — both readings produce the same two equal
/// values. Advancing the clock between the opening and closing transactions is what makes those
/// readings differ, and therefore what makes the defect visible (SCRUM-11137 prerequisite §2,
/// §15).
/// <para>
/// It advances the clock rather than sleeping, so the tests stay deterministic and instant: the
/// elapsed time is a fact the test states, never one it waits for.
/// </para>
/// <para>
/// <see cref="Advance"/> and <see cref="Behaviour"/> are settable between calls, because a retry
/// test has to prove two attempts of <i>different</i> durations keep their own timestamps —
/// which a fixed duration could not distinguish from one duration copied to both rows.
/// </para>
/// </remarks>
internal sealed class ClockAdvancingPhotoshopProcessor : IPhotoshopOutputProcessor
{
    private readonly IPhotoshopOutputProcessor _inner;
    private readonly FakeTimeProvider _clock;

    /// <param name="inner">The adapter that actually produces the file when no behaviour is scripted.</param>
    /// <param name="clock">The controlled clock the whole service graph reads.</param>
    /// <param name="advance">How far the run moves the clock forward before it answers.</param>
    public ClockAdvancingPhotoshopProcessor(
        IPhotoshopOutputProcessor inner, FakeTimeProvider clock, TimeSpan advance)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(clock);
        _inner = inner;
        _clock = clock;
        Advance = advance;
    }

    /// <summary>How far the next call moves the clock forward before it answers.</summary>
    public TimeSpan Advance { get; set; }

    /// <summary>
    /// What the run does once the clock has moved. Null delegates to the wrapped adapter, which
    /// is the success case; a test scripts a returned failure or a throw here.
    /// </summary>
    public Func<PhotoshopRequest, CancellationToken, Task<OperationResult<AdapterOutput>>>? Behaviour { get; set; }

    /// <summary>How many times the adapter was called.</summary>
    public int Calls { get; private set; }

    /// <inheritdoc />
    /// <remarks>
    /// Reports the wrapped adapter's identity, so the attempt row records the processor the run
    /// actually went through rather than naming this decorator.
    /// </remarks>
    public string AdapterId => _inner.AdapterId;

    /// <inheritdoc />
    public AdapterExecutionMode Mode => _inner.Mode;

    /// <inheritdoc />
    public Task<OperationResult<AdapterOutput>> GenerateAsync(
        PhotoshopRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Calls++;
        _clock.Advance(Advance);
        return Behaviour is null
            ? _inner.GenerateAsync(request, cancellationToken)
            : Behaviour(request, cancellationToken);
    }
}
