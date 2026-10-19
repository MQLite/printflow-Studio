using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Sessions;

namespace PrintFlow.Workflow.Services;

/// <summary>Display facts only. A historical row grants no command or review authority.</summary>
public sealed record SessionHistoryView(StepKind Step, StepState State, IReadOnlyList<HistoricalRevisionView> Revisions);

public sealed record HistoricalRevisionView(RevisionId Id, RevisionId? SourceId, string FileName,
    FileFacts Facts, ReviewState ReviewState, bool IsValid, bool BytesReleased);
