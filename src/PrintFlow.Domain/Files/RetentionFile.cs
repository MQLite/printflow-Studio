namespace PrintFlow.Domain.Files;

/// <summary>A managed file and the exact bytes a retention operation is allowed to handle.</summary>
public sealed record RetentionFile(WorkspaceFileRef File, Sha256 Sha256);

/// <summary>Closed deletion list, applied only after the preserved authority has been verified.</summary>
public sealed record WorkingCleanupPlan(
    IReadOnlyList<RetentionFile> Preserve, IReadOnlyList<RetentionFile> Delete);

public sealed record WorkingCleanupResult(int DeletedCount, int PreservedCount);
