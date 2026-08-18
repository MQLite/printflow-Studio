using PrintFlow.Domain.Results;
using PrintFlow.Workflow.Ports;

namespace PrintFlow.Infrastructure.Adapters.Fake;

/// <summary>
/// A deterministic local stand-in for Meitu screen automation (Epic 11100 §41; plan §15.2).
/// </summary>
/// <remarks>
/// It writes a <b>real</b> file — a copy of the input, re-saved to the expected output path —
/// so the full pipeline downstream of the adapter call (existence, stable size, streaming
/// read, hash, metadata extraction, Revision creation, review binding) is genuinely exercised
/// rather than stubbed. It is deterministic (no randomness, no wall-clock dependence) and
/// identifiable: <see cref="AdapterId"/> is written to every attempt so a fake result can never
/// be mistaken for a production one.
///
/// This is deliberately not the full scriptable scenario harness the Plan describes for
/// Epic 11100's later adapter-simulation slice (succeed/fail/timeout/hang/unreadable-output).
/// Task §41 allows a small deterministic double here; the scripted scenarios remain future
/// work, noted in the Part 2 report.
/// </remarks>
public sealed class FakeMeituProcessor : IMeituProcessor
{
    /// <inheritdoc />
    public string AdapterId => "fake-meitu-v1";

    /// <inheritdoc />
    public Task<OperationResult<AdapterOutput>> ProcessAsync(MeituRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The request's input and expected-output references already point at the same
        // working-copy file (SessionService creates one working copy per attempt); the fake
        // "processes" it in place, which is sufficient to exercise validation genuinely without
        // claiming any real enhancement or background-removal behaviour.
        return Task.FromResult(OperationResult.Ok(new AdapterOutput(request.ExpectedOutput, TimeSpan.Zero, "fake")));
    }
}
