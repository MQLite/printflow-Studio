using PrintFlow.Domain.Results;

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
    public static FakeAdapterScenario Succeed { get; } = new(FakeAdapterScenarioKind.Succeed);

    public static FakeAdapterScenario FailWith(FailureCode code) => new(FakeAdapterScenarioKind.FailWith, code);

    public static FakeAdapterScenario Timeout { get; } = new(FakeAdapterScenarioKind.Timeout);

    public static FakeAdapterScenario ProduceUnreadableFile { get; } = new(FakeAdapterScenarioKind.ProduceUnreadableFile);

    public static FakeAdapterScenario ProduceMissingFile { get; } = new(FakeAdapterScenarioKind.ProduceMissingFile);

    public static FakeAdapterScenario HangUntilCancelled { get; } = new(FakeAdapterScenarioKind.HangUntilCancelled);
}
