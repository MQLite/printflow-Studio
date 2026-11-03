using System.Collections.ObjectModel;

namespace PrintFlow.Domain.AiDesign.Authoring;

public sealed record AiPreservationRule(Guid AssetId, bool KeepContent,
    bool AllowMove, bool AllowUniformScale, bool AllowCrop, bool AllowRedraw);

public sealed record AiAuthoringDraft
{
    public required Guid Id { get; init; }
    public long Revision { get; init; }
    public int SchemaVersion { get; init; } = 1;
    public required AiTemplateRef Template { get; init; }
    public required AiTemplateContent TemplateSnapshot { get; init; }
    public AiProductKind Product { get; init; }
    public AiAuthoringMode Mode { get; init; } = AiAuthoringMode.Quick;
    public required IReadOnlyDictionary<string, string> Fields { get; init; }
    public required AiPromptDocument InitialDocument { get; init; }
    public required AiPromptDocument FinalDocument { get; init; }
    public IReadOnlyList<AiDesignAttachment> Assets { get; init; } = [];
    public IReadOnlyList<AiReferenceSlot> Slots { get; init; } = [];
    public IReadOnlyList<AiSlotBinding> Bindings { get; init; } = [];
    public bool IsManuallyEdited { get; init; }
    public bool NeedsFieldReconcile { get; init; }
    public IReadOnlyDictionary<string, string> Confirmations { get; init; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));
    public IReadOnlyList<AiPreservationRule> Preservation { get; init; } = [];
    public Guid? ParentDraftId { get; init; }
    public long? ParentDraftRevision { get; init; }
}
