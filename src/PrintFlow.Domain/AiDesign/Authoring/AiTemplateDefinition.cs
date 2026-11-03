namespace PrintFlow.Domain.AiDesign.Authoring;

public enum AiAuthoringMode { Quick, Detailed }
public enum AiProductKind { MemorialTshirt, MemorialBadge, TextChange }
public enum AiTemplateFieldKind { Text, OptionalDateLine, ImageReference }
public enum AiTemplateSemantic { FreeText, Name, BirthDate, DeathDate, PersonImage, LayoutImage, EditSourceImage }

public sealed record AiTemplateRef(string Id, int Revision);
public sealed record AiTemplateField(string Key, AiTemplateFieldKind Kind, bool Required,
    string? DefaultValue = null, string? SlotId = null, string? DateLabel = null,
    AiTemplateSemantic Semantic = AiTemplateSemantic.FreeText);
public sealed record AiTemplateContent(string Name, AiProductKind Product, string Body,
    IReadOnlyList<AiTemplateField> Fields);
public sealed record AiTemplateDefinition(AiTemplateRef Reference, AiTemplateContent Content, string? BuiltinOriginId);
