using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Adapters.Fake;

/// <summary>Which of the fixed set of deterministic behaviours a fake adapter call exercises.</summary>
public enum FakeAdapterScenarioKind
{
    /// <summary>Writes a real, readable output file (the default).</summary>
    Succeed,

    /// <summary>Fails immediately with a scripted <see cref="FailureCode"/>, no file written.</summary>
    FailWith,

    /// <summary>Fails immediately with <see cref="FailureCode.Timeout"/>, no file written.</summary>
    Timeout,

    /// <summary>Leaves a genuine zero-byte file at the expected output path.</summary>
    ProduceUnreadableFile,

    /// <summary>Leaves nothing at the expected output path.</summary>
    ProduceMissingFile,

    /// <summary>Waits until the supplied <see cref="CancellationToken"/> is cancelled, then fails with <see cref="FailureCode.Cancelled"/>.</summary>
    HangUntilCancelled,

    /// <summary>
    /// Reports a scripted <see cref="ExternalOperationPhase"/> and waits for an operator Stop
    /// or Take Over, then ends without producing a file (Epic 11300 Part D2A §36).
    /// </summary>
    /// <remarks>
    /// The deterministic stand-in for "an external operation is in flight and the operator can
    /// still act on it", which is otherwise only reachable by driving a real application. It
    /// exists because the workflow-level rules D2A adds — the attempt closes as Cancelled, no
    /// Revision is created, the automation lock is released, a takeover ends in HandedOff — are
    /// about what PrintFlow records, not about what Meitu shows, and testing them through a real
    /// desktop would make them untestable in CI.
    /// <para>
    /// It deliberately reports <b>no</b> signed cancel. A fake has no external application and
    /// therefore nothing to cancel, and a scenario that claimed otherwise would let a test
    /// assert "the cancel was invoked" about a run in which nothing was. The exactly-one-signed-
    /// invocation rules are tested against the driver with a fake automation tree instead, where
    /// the invocation is a real thing that can be counted.
    /// </para>
    /// </remarks>
    ReportPhaseAndWaitForStop,
}

/// <summary>
/// A scripted, deterministic instruction for a fake adapter's next call (Epic 11100 Part 3A §3).
/// </summary>
/// <remarks>
/// Every scenario still drives the real infrastructure pipeline downstream — the fake never
/// fabricates a Revision directly. <see cref="Succeed"/> writes a genuinely valid file;
/// <see cref="ProduceUnreadableFile"/> and <see cref="ProduceMissingFile"/> leave the file
/// system in a state the real <c>IFileInspector</c> genuinely rejects; the others fail before
/// any file is written at all.
/// </remarks>
public sealed record FakeAdapterScenario(FakeAdapterScenarioKind Kind, FailureCode? Code = null)
{
    /// <summary>
    /// The phase <see cref="FakeAdapterScenarioKind.ReportPhaseAndWaitForStop"/> reports before
    /// it waits (Epic 11300 Part D2A §4).
    /// </summary>
    /// <remarks>
    /// Ignored by every other scenario. Its default is
    /// <see cref="ExternalOperationPhase.Busy"/> because that is the phase the interesting rules
    /// hang off — it is the only one in which a stop may invoke anything at all.
    /// </remarks>
    public ExternalOperationPhase Phase { get; init; } = ExternalOperationPhase.Busy;

    public static FakeAdapterScenario Succeed { get; } = new(FakeAdapterScenarioKind.Succeed);

    /// <summary>Reports <paramref name="phase"/> and then waits for an operator Stop or Take Over.</summary>
    public static FakeAdapterScenario WaitForStopAt(ExternalOperationPhase phase) =>
        new(FakeAdapterScenarioKind.ReportPhaseAndWaitForStop) { Phase = phase };

    public static FakeAdapterScenario FailWith(FailureCode code) => new(FakeAdapterScenarioKind.FailWith, code);

    public static FakeAdapterScenario Timeout { get; } = new(FakeAdapterScenarioKind.Timeout);

    public static FakeAdapterScenario ProduceUnreadableFile { get; } = new(FakeAdapterScenarioKind.ProduceUnreadableFile);

    public static FakeAdapterScenario ProduceMissingFile { get; } = new(FakeAdapterScenarioKind.ProduceMissingFile);

    public static FakeAdapterScenario HangUntilCancelled { get; } = new(FakeAdapterScenarioKind.HangUntilCancelled);
}
