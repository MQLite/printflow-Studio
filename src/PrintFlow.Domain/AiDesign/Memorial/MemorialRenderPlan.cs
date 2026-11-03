using PrintFlow.Domain.Files;

namespace PrintFlow.Domain.AiDesign.Memorial;

public enum MemorialRenderPurpose { Preview, DesignMaterial }
public sealed record MemorialRenderPlan(Guid DraftId, long DraftRevision, MemorialLayout Layout,
    IReadOnlyList<AiDesignAttachment> Assets, MemorialRenderPurpose Purpose, string SourceManifestSha256);
public sealed record MemorialRenderRef(Guid DraftId, Guid RenderId, MemorialRenderPurpose Purpose);
public sealed record MemorialRenderResult(MemorialRenderRef File, long DraftRevision, FileFacts Facts, string SourceManifestSha256);
