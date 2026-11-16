namespace PrintFlow.Domain.AiDesign.Memorial;

public enum MemorialReviewItem { Person, Decor, Text, Layout, Transparency }

public sealed record MemorialReviewState(MemorialRenderResult Render, long Revision,
    IReadOnlyDictionary<MemorialReviewItem, bool> Checks, bool Accepted);
