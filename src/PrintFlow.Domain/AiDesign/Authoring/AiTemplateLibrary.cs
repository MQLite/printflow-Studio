namespace PrintFlow.Domain.AiDesign.Authoring;

public sealed record AiTemplateLibrary
{
    public long Revision { get; init; }
    public required IReadOnlyList<AiTemplateDefinition> Templates { get; init; }
    public IReadOnlyList<string> FavoriteIds { get; init; } = [];
    public required IReadOnlyList<string> OrderedIds { get; init; }
    public required string DefaultId { get; init; }
}
