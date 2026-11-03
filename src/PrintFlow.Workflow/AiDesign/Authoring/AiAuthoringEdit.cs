using PrintFlow.Domain.AiDesign.Authoring;

namespace PrintFlow.Workflow.AiDesign.Authoring;

public abstract record AiAuthoringEdit
{
    public sealed record RestoreRevision(long Revision) : AiAuthoringEdit;
    public sealed record SetField(string Key, string Value) : AiAuthoringEdit;
    public sealed record ReplaceDocument(AiPromptDocument Document) : AiAuthoringEdit;
    public sealed record Recompose(bool PreservePrevious) : AiAuthoringEdit;
    public sealed record BindImage(string SlotId, SelectedAiDesignFile Selection) : AiAuthoringEdit;
    public sealed record BindExistingImage(string SlotId, Guid AssetId) : AiAuthoringEdit;
    public sealed record InsertImageReference(Guid PartId, AiReferenceSlot Slot, Guid? AfterPartId) : AiAuthoringEdit;
    public sealed record RemoveReference(Guid PartId) : AiAuthoringEdit;
    public sealed record RemoveImage(Guid AssetId) : AiAuthoringEdit;
    public sealed record SwitchMode(AiAuthoringMode Mode) : AiAuthoringEdit;
    public sealed record SelectTemplate(AiTemplateRef Template) : AiAuthoringEdit;
}
