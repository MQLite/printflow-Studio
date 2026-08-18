namespace PrintFlow.Infrastructure.Sqlite;

// Flat row shapes matching the SQLite schema exactly, used only as Dapper's materialisation
// target. Domain types are never used directly for I/O — Mappers.cs is the single place that
// converts each row to and from its domain record (Epic 11100 Task 11108).

internal sealed class SessionRow
{
    public string Id { get; set; } = "";
    public string WorkflowType { get; set; } = "";
    public string OutputName { get; set; } = "";
    public string CurrentStep { get; set; } = "";
    public string State { get; set; } = "";
    public string WorkspacePath { get; set; } = "";
    public string CreatedAtUtc { get; set; } = "";
    public string UpdatedAtUtc { get; set; } = "";
    public string? CompletedAtUtc { get; set; }
    public string? HandedOffAtUtc { get; set; }
    public string? HandOffReason { get; set; }
    public string? AbandonedAtUtc { get; set; }
    public string? AbandonReason { get; set; }
    public double? DimensionsWidthMm { get; set; }
    public double? DimensionsHeightMm { get; set; }
    public int? DimensionsPixelWidth { get; set; }
    public int? DimensionsPixelHeight { get; set; }
    public string? DimensionsPreset { get; set; }
    public string? WhiteUnderbaseBranch { get; set; }
}

internal sealed class StepRow
{
    public string SessionId { get; set; } = "";
    public string StepKind { get; set; } = "";
    public int Ordinal { get; set; }
    public string State { get; set; } = "";
    public string? CurrentRevisionId { get; set; }
    public string? CurrentRevisionSha { get; set; }
    public string? SkipReason { get; set; }
    public int AttemptCount { get; set; }
    public string EnteredStateAtUtc { get; set; } = "";
}

internal sealed class SnapshotRow
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string RootRevisionId { get; set; } = "";
    public string OriginalSourcePath { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string ImportedAtUtc { get; set; } = "";
}

internal sealed class RevisionRow
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string? SourceRevisionId { get; set; }
    public string Operation { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public string Format { get; set; } = "";
    public long ByteLength { get; set; }
    public string Sha256 { get; set; } = "";
    public int? PixelWidth { get; set; }
    public int? PixelHeight { get; set; }
    public double? DpiX { get; set; }
    public double? DpiY { get; set; }
    public string ColourMode { get; set; } = "";
    public bool? HasAlpha { get; set; }
    public string CreatedAtUtc { get; set; } = "";
    public bool IsValid { get; set; }
    public string? InvalidatedAtUtc { get; set; }
    public string? InvalidationReason { get; set; }
    public string ReviewState { get; set; } = "";
}

internal sealed class AttemptRow
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string StepKind { get; set; } = "";
    public string? InputRevisionId { get; set; }
    public string Operation { get; set; } = "";
    public string AdapterId { get; set; } = "";
    public string StartedAtUtc { get; set; } = "";
    public string? EndedAtUtc { get; set; }
    public string ResultStatus { get; set; } = "";
    public string? OutputRevisionId { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureDetailJson { get; set; }
    public string? RetryOfAttemptId { get; set; }
    public int RetrySequence { get; set; }
}

internal sealed class ReviewRow
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string StepKind { get; set; } = "";
    public string SubjectKind { get; set; } = "";
    public string SubjectId { get; set; } = "";
    public string ReviewedSha256 { get; set; } = "";
    public string Operator { get; set; } = "";
    public string DecidedAtUtc { get; set; } = "";
    public string Decision { get; set; } = "";
    public string? QuickReason { get; set; }
    public string? Notes { get; set; }
}

internal sealed class OutputRow
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string SourceRevisionId { get; set; } = "";
    public double TargetWidthMm { get; set; }
    public double TargetHeightMm { get; set; }
    public int PixelWidth { get; set; }
    public int PixelHeight { get; set; }
    public int Dpi { get; set; }
    public string SizePresetId { get; set; } = "";
    public string WhiteUnderbaseBranch { get; set; } = "";
    public string ProductionPresetId { get; set; } = "";
    public string ProductionPresetSha256 { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public long ByteLength { get; set; }
    public string Sha256 { get; set; } = "";
    public string ReviewState { get; set; } = "";
    public bool IsValid { get; set; }
    public string? InvalidationReason { get; set; }
    public string? RecycledAtUtc { get; set; }
    public string CreatedAtUtc { get; set; } = "";
}

internal sealed class AutomationLockRow
{
    public string? SessionId { get; set; }
    public string? AcquiredAtUtc { get; set; }
    public int? ProcessId { get; set; }
    public string? MachineName { get; set; }
}
