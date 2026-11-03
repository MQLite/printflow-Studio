using System.Text.Json.Serialization;

namespace PrintFlow.Domain.AiDesign.Authoring;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(AiTextPart), "text")]
[JsonDerivedType(typeof(AiImagePart), "image")]
public abstract record AiPromptPart(Guid PartId);
public sealed record AiTextPart(Guid PartId, string Text) : AiPromptPart(PartId);
public sealed record AiImagePart(Guid PartId, string SlotId) : AiPromptPart(PartId);
public sealed record AiPromptDocument(IReadOnlyList<AiPromptPart> Parts);
public sealed record AiReferenceSlot(string SlotId, string Role, bool Required);
public sealed record AiSlotBinding(string SlotId, Guid AssetId);
public sealed record AiAuthoringIssue(string Key, string? FieldKey, string? SlotId);
public sealed record AiPromptComposition(AiPromptDocument Document, IReadOnlyList<AiAuthoringIssue> Issues);
