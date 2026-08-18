using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;

namespace PrintFlow.Domain.Sessions;

/// <summary>
/// The state of one workflow step within one session — the normalised workflow snapshot
/// (Epic 11100 plan decision 9: no JSON blob holds authority).
/// </summary>
/// <param name="Step">Which step this row describes.</param>
/// <param name="Ordinal">Position within this session's workflow definition.</param>
/// <param name="State">Current lifecycle state of the step.</param>
/// <param name="CurrentRevisionId">The Revision this step currently offers downstream, if any.</param>
/// <param name="CurrentRevisionSha256">
/// The hash of that Revision. Carried alongside the id so the engine can refuse an approval
/// whose reviewed hash no longer matches the result on offer, without reading a file
/// (MVP design invariants 2 and 3).
/// </param>
/// <param name="SkipReason">Why the step was skipped, when <see cref="State"/> is Skipped.</param>
/// <param name="AttemptCount">How many attempts have been started for this step.</param>
/// <param name="EnteredStateAtUtc">When the step entered <see cref="State"/>.</param>
public sealed record SessionStep(
    StepKind Step,
    int Ordinal,
    StepState State,
    RevisionId? CurrentRevisionId,
    Sha256? CurrentRevisionSha256,
    string? SkipReason,
    int AttemptCount,
    DateTimeOffset EnteredStateAtUtc)
{
    /// <summary>A step is finished when it is Approved or Skipped (plan §8.2).</summary>
    public bool IsFinished => State is StepState.Approved or StepState.Skipped;

    /// <summary>True when this step currently offers a Revision to downstream steps.</summary>
    public bool HasResult => CurrentRevisionId is not null;

    public SessionStep WithState(StepState state, DateTimeOffset atUtc) =>
        this with { State = state, EnteredStateAtUtc = atUtc };

    /// <summary>Returns the step reset to Waiting with its result cleared.</summary>
    public SessionStep Reset(DateTimeOffset atUtc) =>
        this with
        {
            State = StepState.Waiting,
            CurrentRevisionId = null,
            CurrentRevisionSha256 = null,
            SkipReason = null,
            EnteredStateAtUtc = atUtc,
        };
}
