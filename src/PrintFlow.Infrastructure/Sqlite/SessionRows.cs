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
    public string? TrimMode { get; set; }
    public int? TrimMarginTop { get; set; }
    public int? TrimMarginRight { get; set; }
    public int? TrimMarginBottom { get; set; }
    public int? TrimMarginLeft { get; set; }

    // The pending reviewed-content authority for background removal: what the NEXT run would be
    // allowed to do (Epic 11300 Part C2B1 §10). All three move together, or all three are null.
    public string? BackgroundRemovalDecision { get; set; }

    public string? BackgroundRemovalRevisionId { get; set; }

    public string? BackgroundRemovalReviewedSha { get; set; }

    // What the Dimensions* columns above actually mean on this row (Epic 11400 Part B1A.2A §9).
    // NULL exactly when no dimensions are recorded; LEGACY_EXACT_PAIR on every row written before
    // the maximum-bound contract.
    public string? DimensionSemantics { get; set; }

    // The pending source-bound preparation plan: what the NEXT Photoshop output would run with
    // (§11). All thirteen move together, or all thirteen are null -- except
    // PrintPlanLimitingValueMm, whose absence is meaningful and is paired with the mode.
    public string? PrintPlanSourceRevisionId { get; set; }

    public string? PrintPlanSourceSha256 { get; set; }

    public int? PrintPlanSourcePixelWidth { get; set; }

    public int? PrintPlanSourcePixelHeight { get; set; }

    public double? PrintPlanMaxWidthMm { get; set; }

    public double? PrintPlanMaxHeightMm { get; set; }

    public string? PrintPlanLimitKind { get; set; }

    public string? PrintPlanMode { get; set; }

    public string? PrintPlanLimitingEdge { get; set; }

    public double? PrintPlanLimitingValueMm { get; set; }

    public int? PrintPlanProjectedPixelWidth { get; set; }

    public int? PrintPlanProjectedPixelHeight { get; set; }

    public int? PrintPlanProductionDpi { get; set; }

    public string? PrintPlanResizePolicy { get; set; }

    // The flexible-size decision, its TargetEdgeV1 plan and its enlargement authority
    // (Epic 11400 Part B1A.2D §6, §8, §9). Each of the three is an all-or-nothing group, and
    // null across all of them is the honest "no flexible-size decision was recorded" -- never
    // "PresetFit was assumed" and never "enlargement was allowed" (§21).
    //
    // Every millimetre value is text, because the accepted target-edge calculation is exact and a
    // REAL round trip through a binary double would not be (§7).
    public string? SizingMode { get; set; }

    public string? SizingPreset { get; set; }

    public string? SizingRecommendationKind { get; set; }

    public string? SizingRecommendationMaxWidthMm { get; set; }

    public string? SizingRecommendationMaxHeightMm { get; set; }

    public bool? SizingPresetOverridden { get; set; }

    public string? SizingTargetEdge { get; set; }

    public string? SizingRequestedMm { get; set; }

    public string? TargetPlanSourceRevisionId { get; set; }

    public string? TargetPlanSourceSha256 { get; set; }

    public int? TargetPlanSourcePixelWidth { get; set; }

    public int? TargetPlanSourcePixelHeight { get; set; }

    public string? TargetPlanPhotoshopEdge { get; set; }

    public int? TargetPlanProjectedPixelWidth { get; set; }

    public int? TargetPlanProjectedPixelHeight { get; set; }

    public int? TargetPlanScaleNumerator { get; set; }

    public int? TargetPlanScaleDenominator { get; set; }

    public int? TargetPlanProductionDpi { get; set; }

    public string? TargetPlanDirection { get; set; }

    public string? TargetPlanResizePolicy { get; set; }

    public string? EnlargementAuthoritySourceRevisionId { get; set; }

    public string? EnlargementAuthoritySourceSha256 { get; set; }

    public string? EnlargementAuthoritySizingMode { get; set; }

    public string? EnlargementAuthorityTargetEdge { get; set; }

    public string? EnlargementAuthorityRequestedMm { get; set; }

    public int? EnlargementAuthorityScaleNumerator { get; set; }

    public int? EnlargementAuthorityScaleDenominator { get; set; }

    public int? EnlargementAuthorityProjectedPixelWidth { get; set; }

    public int? EnlargementAuthorityProjectedPixelHeight { get; set; }
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

    // How this attempt's deterministic trim was parameterised (Epic 11200 Part C3 §14).
    // Null for anything that is not a deterministic trim, including a manual crop.
    public string? TrimMode { get; set; }
    public int? TrimMarginTop { get; set; }
    public int? TrimMarginRight { get; set; }
    public int? TrimMarginBottom { get; set; }
    public int? TrimMarginLeft { get; set; }

    // What THIS attempt was authorised by, when it was an authorised background removal
    // (Epic 11300 Part C2B1 §11). Null for everything else, including an enhancement.
    public string? BackgroundRemovalDecision { get; set; }

    public string? BackgroundRemovalRevisionId { get; set; }

    public string? BackgroundRemovalReviewedSha { get; set; }

    // Successful runtime evidence, including cleanup warnings (Epic 11300 Part D1).
    public string? AdapterNotes { get; set; }

    // The rectangles THIS attempt's deterministic trim established (SCRUM-11081). Content is
    // what the alpha scan found before any margin; Applied is what was actually cropped out.
    // TrimBounds's half-open convention, unconverted: Left/Top inclusive, Right/Bottom exclusive.
    // All eight null together for anything that is not a produced automatic trim, and for every
    // attempt written before migration 0009 -- never "the whole canvas was kept".
    public int? TrimContentLeft { get; set; }

    public int? TrimContentTop { get; set; }

    public int? TrimContentRight { get; set; }

    public int? TrimContentBottom { get; set; }

    public int? TrimAppliedLeft { get; set; }

    public int? TrimAppliedTop { get; set; }

    public int? TrimAppliedRight { get; set; }

    public int? TrimAppliedBottom { get; set; }

    // The immutable snapshot of the plan THIS Photoshop output ran under (Epic 11400 Part
    // B1A.2A §12). Null for everything else -- a Meitu call, a trim, a manual crop, a promotion.
    // Self-contained: the bounds and limit kind are here too, so the audit row never has to be
    // joined back to a session that may since have changed its mind.
    public string? PrintPlanSourceRevisionId { get; set; }

    public string? PrintPlanSourceSha256 { get; set; }

    public int? PrintPlanSourcePixelWidth { get; set; }

    public int? PrintPlanSourcePixelHeight { get; set; }

    public double? PrintPlanMaxWidthMm { get; set; }

    public double? PrintPlanMaxHeightMm { get; set; }

    public string? PrintPlanLimitKind { get; set; }

    public string? PrintPlanMode { get; set; }

    public string? PrintPlanLimitingEdge { get; set; }

    public double? PrintPlanLimitingValueMm { get; set; }

    public int? PrintPlanProjectedPixelWidth { get; set; }

    public int? PrintPlanProjectedPixelHeight { get; set; }

    public int? PrintPlanProductionDpi { get; set; }

    public string? PrintPlanResizePolicy { get; set; }

    // The flexible-size decision, its TargetEdgeV1 plan and its enlargement authority
    // (Epic 11400 Part B1A.2D §6, §8, §9). Each of the three is an all-or-nothing group, and
    // null across all of them is the honest "no flexible-size decision was recorded" -- never
    // "PresetFit was assumed" and never "enlargement was allowed" (§21).
    //
    // Every millimetre value is text, because the accepted target-edge calculation is exact and a
    // REAL round trip through a binary double would not be (§7).
    public string? SizingMode { get; set; }

    public string? SizingPreset { get; set; }

    public string? SizingRecommendationKind { get; set; }

    public string? SizingRecommendationMaxWidthMm { get; set; }

    public string? SizingRecommendationMaxHeightMm { get; set; }

    public bool? SizingPresetOverridden { get; set; }

    public string? SizingTargetEdge { get; set; }

    public string? SizingRequestedMm { get; set; }

    public string? TargetPlanSourceRevisionId { get; set; }

    public string? TargetPlanSourceSha256 { get; set; }

    public int? TargetPlanSourcePixelWidth { get; set; }

    public int? TargetPlanSourcePixelHeight { get; set; }

    public string? TargetPlanPhotoshopEdge { get; set; }

    public int? TargetPlanProjectedPixelWidth { get; set; }

    public int? TargetPlanProjectedPixelHeight { get; set; }

    public int? TargetPlanScaleNumerator { get; set; }

    public int? TargetPlanScaleDenominator { get; set; }

    public int? TargetPlanProductionDpi { get; set; }

    public string? TargetPlanDirection { get; set; }

    public string? TargetPlanResizePolicy { get; set; }

    public string? EnlargementAuthoritySourceRevisionId { get; set; }

    public string? EnlargementAuthoritySourceSha256 { get; set; }

    public string? EnlargementAuthoritySizingMode { get; set; }

    public string? EnlargementAuthorityTargetEdge { get; set; }

    public string? EnlargementAuthorityRequestedMm { get; set; }

    public int? EnlargementAuthorityScaleNumerator { get; set; }

    public int? EnlargementAuthorityScaleDenominator { get; set; }

    public int? EnlargementAuthorityProjectedPixelWidth { get; set; }

    public int? EnlargementAuthorityProjectedPixelHeight { get; set; }
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

    public string? PromotionReservedPath { get; set; }

    public string CreatedAtUtc { get; set; } = "";
}

internal sealed class AutomationLockRow
{
    public string? SessionId { get; set; }
    public string? AcquiredAtUtc { get; set; }
    public int? ProcessId { get; set; }
    public string? MachineName { get; set; }
}
