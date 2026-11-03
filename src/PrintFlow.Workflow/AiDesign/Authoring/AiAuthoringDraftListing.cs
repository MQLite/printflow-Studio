using PrintFlow.Domain.AiDesign.Authoring;

namespace PrintFlow.Workflow.AiDesign.Authoring;

/// <summary>Saved time is a presentation hint; identity and contents come from the committed pointer.</summary>
public sealed record AiSavedAuthoringDraft(AiAuthoringDraft Draft, DateTimeOffset SavedAtUtc);
public sealed record AiAuthoringDraftListing(IReadOnlyList<AiSavedAuthoringDraft> Drafts, int UnavailableCount);
