using System.Globalization;
using PrintFlow.Domain.Attempts;
using PrintFlow.Domain.Files;
using PrintFlow.Domain.Ids;
using PrintFlow.Domain.Outputs;
using PrintFlow.Domain.Revisions;
using PrintFlow.Domain.Reviews;
using PrintFlow.Domain.Sessions;
using PrintFlow.Domain.Trimming;

namespace PrintFlow.Infrastructure.Sqlite;

/// <summary>
/// Converts between domain records and the flat rows SQLite stores, in both directions
/// (Epic 11100 Task 11108). The only place in the solution that knows the TEXT encoding of an
/// enum on disk.
/// </summary>
internal static class Mappers
{
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    // ------------------------------------------------------------------------------------
    // Timestamps
    // ------------------------------------------------------------------------------------

    public static string ToText(DateTimeOffset value) =>
        value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);

    public static string? ToTextOrNull(DateTimeOffset? value) => value is null ? null : ToText(value.Value);

    public static DateTimeOffset ToDateTimeOffset(string text) =>
        DateTimeOffset.ParseExact(text, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    public static DateTimeOffset? ToDateTimeOffsetOrNull(string? text) =>
        text is null ? null : ToDateTimeOffset(text);

    // ------------------------------------------------------------------------------------
    // Enums with a database CHECK constraint (must match exactly)
    // ------------------------------------------------------------------------------------

    public static string ToText(WorkflowType value) => value switch
    {
        WorkflowType.PrepareAsset => "PREPARE_ASSET",
        WorkflowType.PrepareCustomerDesign => "PREPARE_CUSTOMER_DESIGN",
        WorkflowType.GeneratePrintTiff => "GENERATE_PRINT_TIFF",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static WorkflowType ToWorkflowType(string text) => text switch
    {
        "PREPARE_ASSET" => WorkflowType.PrepareAsset,
        "PREPARE_CUSTOMER_DESIGN" => WorkflowType.PrepareCustomerDesign,
        "GENERATE_PRINT_TIFF" => WorkflowType.GeneratePrintTiff,
        _ => throw new InvalidOperationException($"Unknown WorkflowType '{text}' in database."),
    };

    public static string ToText(SessionState value) => value switch
    {
        SessionState.Active => "ACTIVE",
        SessionState.HandedOff => "HANDED_OFF",
        SessionState.Completed => "COMPLETED",
        SessionState.Abandoned => "ABANDONED",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static SessionState ToSessionState(string text) => text switch
    {
        "ACTIVE" => SessionState.Active,
        "HANDED_OFF" => SessionState.HandedOff,
        "COMPLETED" => SessionState.Completed,
        "ABANDONED" => SessionState.Abandoned,
        _ => throw new InvalidOperationException($"Unknown SessionState '{text}' in database."),
    };

    public static string ToText(StepState value) => value switch
    {
        StepState.Waiting => "WAITING",
        StepState.Processing => "PROCESSING",
        StepState.ReviewRequired => "REVIEW_REQUIRED",
        StepState.Approved => "APPROVED",
        StepState.RetryRequired => "RETRY_REQUIRED",
        StepState.Skipped => "SKIPPED",
        StepState.Failed => "FAILED",
        StepState.Interrupted => "INTERRUPTED",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static StepState ToStepState(string text) => text switch
    {
        "WAITING" => StepState.Waiting,
        "PROCESSING" => StepState.Processing,
        "REVIEW_REQUIRED" => StepState.ReviewRequired,
        "APPROVED" => StepState.Approved,
        "RETRY_REQUIRED" => StepState.RetryRequired,
        "SKIPPED" => StepState.Skipped,
        "FAILED" => StepState.Failed,
        "INTERRUPTED" => StepState.Interrupted,
        _ => throw new InvalidOperationException($"Unknown StepState '{text}' in database."),
    };

    public static string ToText(OperationKind value) => value switch
    {
        OperationKind.PreparePsd => "PREPARE_PSD",
        OperationKind.PreparePdf => "PREPARE_PDF",
        OperationKind.Import => "IMPORT",
        OperationKind.Enhance => "ENHANCE",
        OperationKind.RemoveBackground => "REMOVE_BACKGROUND",
        OperationKind.Trim => "TRIM",
        OperationKind.PromoteApproved => "PROMOTE_APPROVED",
        OperationKind.ManualImport => "MANUAL_IMPORT",
        OperationKind.ManualResultImport => "MANUAL_RESULT_IMPORT",
        OperationKind.PhotoshopOutput => "PHOTOSHOP_OUTPUT",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static OperationKind ToOperationKind(string text) => text switch
    {
        "PREPARE_PSD" => OperationKind.PreparePsd,
        "PREPARE_PDF" => OperationKind.PreparePdf,
        "IMPORT" => OperationKind.Import,
        "ENHANCE" => OperationKind.Enhance,
        "REMOVE_BACKGROUND" => OperationKind.RemoveBackground,
        "TRIM" => OperationKind.Trim,
        "PROMOTE_APPROVED" => OperationKind.PromoteApproved,
        "MANUAL_IMPORT" => OperationKind.ManualImport,
        "MANUAL_RESULT_IMPORT" => OperationKind.ManualResultImport,
        "PHOTOSHOP_OUTPUT" => OperationKind.PhotoshopOutput,
        _ => throw new InvalidOperationException($"Unknown OperationKind '{text}' in database."),
    };

    public static string ToText(InvalidationReason value) => value switch
    {
        InvalidationReason.Superseded => "SUPERSEDED",
        InvalidationReason.UpstreamChanged => "UPSTREAM_CHANGED",
        InvalidationReason.FileMutated => "FILE_MUTATED",
        InvalidationReason.Rejected => "REJECTED",
        InvalidationReason.SessionReset => "SESSION_RESET",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static InvalidationReason ToInvalidationReason(string text) => text switch
    {
        "SUPERSEDED" => InvalidationReason.Superseded,
        "UPSTREAM_CHANGED" => InvalidationReason.UpstreamChanged,
        "FILE_MUTATED" => InvalidationReason.FileMutated,
        "REJECTED" => InvalidationReason.Rejected,
        "SESSION_RESET" => InvalidationReason.SessionReset,
        _ => throw new InvalidOperationException($"Unknown InvalidationReason '{text}' in database."),
    };

    public static string ToText(ReviewState value) => value switch
    {
        ReviewState.NotReviewed => "NOT_REVIEWED",
        ReviewState.Approved => "APPROVED",
        ReviewState.Rejected => "REJECTED",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static ReviewState ToReviewState(string text) => text switch
    {
        "NOT_REVIEWED" => ReviewState.NotReviewed,
        "APPROVED" => ReviewState.Approved,
        "REJECTED" => ReviewState.Rejected,
        _ => throw new InvalidOperationException($"Unknown ReviewState '{text}' in database."),
    };

    public static string ToText(AttemptStatus value) => value switch
    {
        AttemptStatus.Running => "RUNNING",
        AttemptStatus.Succeeded => "SUCCEEDED",
        AttemptStatus.Failed => "FAILED",
        AttemptStatus.Interrupted => "INTERRUPTED",
        AttemptStatus.Cancelled => "CANCELLED",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static AttemptStatus ToAttemptStatus(string text) => text switch
    {
        "RUNNING" => AttemptStatus.Running,
        "SUCCEEDED" => AttemptStatus.Succeeded,
        "FAILED" => AttemptStatus.Failed,
        "INTERRUPTED" => AttemptStatus.Interrupted,
        "CANCELLED" => AttemptStatus.Cancelled,
        _ => throw new InvalidOperationException($"Unknown AttemptStatus '{text}' in database."),
    };

    public static string ToText(ReviewSubjectKind value) => value switch
    {
        ReviewSubjectKind.Revision => "REVISION",
        ReviewSubjectKind.PrintOutput => "PRINT_OUTPUT",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static ReviewSubjectKind ToReviewSubjectKind(string text) => text switch
    {
        "REVISION" => ReviewSubjectKind.Revision,
        "PRINT_OUTPUT" => ReviewSubjectKind.PrintOutput,
        _ => throw new InvalidOperationException($"Unknown ReviewSubjectKind '{text}' in database."),
    };

    public static string ToText(TrimMode value) => value switch
    {
        TrimMode.TightCrop => "TIGHT_CROP",
        TrimMode.UniformMargin => "UNIFORM_MARGIN",
        TrimMode.EdgeSpecificMargin => "EDGE_SPECIFIC_MARGIN",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static TrimMode ToTrimMode(string text) => text switch
    {
        "TIGHT_CROP" => TrimMode.TightCrop,
        "UNIFORM_MARGIN" => TrimMode.UniformMargin,
        "EDGE_SPECIFIC_MARGIN" => TrimMode.EdgeSpecificMargin,
        _ => throw new InvalidOperationException($"Unknown TrimMode '{text}' in database."),
    };

    /// <summary>
    /// Rebuilds a <see cref="TrimMargin"/> from its five columns, or null when none was stored.
    /// </summary>
    /// <remarks>
    /// Goes back through the domain factories rather than reconstructing the value directly, so
    /// a row that somehow held a negative margin is refused here rather than becoming a
    /// <see cref="TrimMargin"/> the factories would never have produced. Reading is the last
    /// place that invariant can still be enforced, and the database CHECK is the first
    /// (Epic 11200 Part C3 §11, §14).
    /// <para>
    /// <see cref="TrimMode.EdgeSpecificMargin"/> keeps its four numbers even when they happen to
    /// be equal, because the mode is what the operator asked for and not a restatement of the
    /// numbers (Part B).
    /// </para>
    /// </remarks>
    public static TrimMargin? ToTrimMargin(
        string? mode, int? top, int? right, int? bottom, int? left)
    {
        if (mode is null)
        {
            return null;
        }

        return ToTrimMode(mode) switch
        {
            TrimMode.TightCrop => TrimMargin.Tight,
            TrimMode.UniformMargin => TrimMargin.Uniform(top ?? 0),
            _ => TrimMargin.PerEdge(top ?? 0, right ?? 0, bottom ?? 0, left ?? 0),
        };
    }

    /// <summary>Restores only explicitly recorded manual crop facts.</summary>
    private static ManualCropGeometry? ToManualCropGeometry(AttemptRow row)
    {
        if (row.ManualMarginMode is null) return null;
        ManualCropMargin margin = ToTrimMode(row.ManualMarginMode) switch
        {
            TrimMode.TightCrop => ManualCropMargin.Tight,
            TrimMode.UniformMargin => ManualCropMargin.Uniform(row.ManualMarginTop!.Value),
            _ => ManualCropMargin.PerEdge(row.ManualMarginTop!.Value, row.ManualMarginRight!.Value,
                row.ManualMarginBottom!.Value, row.ManualMarginLeft!.Value),
        };
        return ManualCropGeometry.Restore(
            TrimBounds.FromEdges(row.ManualSelectedLeft!.Value, row.ManualSelectedTop!.Value,
                row.ManualSelectedRight!.Value, row.ManualSelectedBottom!.Value),
            TrimBounds.FromEdges(row.ManualAppliedLeft!.Value, row.ManualAppliedTop!.Value,
                row.ManualAppliedRight!.Value, row.ManualAppliedBottom!.Value), margin);
    }

    /// <summary>
    /// Rebuilds a <see cref="TrimGeometry"/> from its eight edge columns, or null when none was
    /// stored (SCRUM-11081).
    /// </summary>
    /// <remarks>
    /// All eight are required together, and a row holding only some of them is refused rather
    /// than patched up with zeros. A missing edge is not a rectangle that starts at the canvas
    /// corner — it is a row nobody can honestly read, and defaulting it would put a fabricated
    /// crop into an operator-visible audit line. The 0009 trigger refuses to write such a row in
    /// the first place, so reaching this throw means the file was edited outside PrintFlow.
    /// <para>
    /// Both rectangles go through <see cref="TrimBounds.FromEdges"/> and
    /// <see cref="TrimGeometry.Create"/>, so a stored pair that could not have come from a
    /// margin expansion fails to load rather than loading as a crop that never happened.
    /// </para>
    /// </remarks>
    public static TrimGeometry? ToTrimGeometry(
        int? contentLeft, int? contentTop, int? contentRight, int? contentBottom,
        int? appliedLeft, int? appliedTop, int? appliedRight, int? appliedBottom)
    {
        if (contentLeft is null && contentTop is null && contentRight is null && contentBottom is null &&
            appliedLeft is null && appliedTop is null && appliedRight is null && appliedBottom is null)
        {
            return null;
        }

        if (contentLeft is not { } cl || contentTop is not { } ct ||
            contentRight is not { } cr || contentBottom is not { } cb ||
            appliedLeft is not { } al || appliedTop is not { } at ||
            appliedRight is not { } ar || appliedBottom is not { } ab)
        {
            throw new InvalidOperationException(
                "A ProcessingAttempt row holds a partial trim geometry: the detected and applied " +
                "rectangles are written together or not at all.");
        }

        return TrimGeometry.Create(
            TrimBounds.FromEdges(cl, ct, cr, cb),
            TrimBounds.FromEdges(al, at, ar, ab));
    }

    public static string ToText(BackgroundRemovalDecision value) => value switch
    {
        BackgroundRemovalDecision.Unspecified => "UNSPECIFIED",
        BackgroundRemovalDecision.ManualResultForReviewedContent => "MANUAL_RESULT_FOR_REVIEWED_CONTENT",
        BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent =>
            "USE_AUTOMATIC_SELECTION_FOR_REVIEWED_CONTENT",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static BackgroundRemovalDecision ToBackgroundRemovalDecision(string text) => text switch
    {
        "UNSPECIFIED" => BackgroundRemovalDecision.Unspecified,
        "MANUAL_RESULT_FOR_REVIEWED_CONTENT" => BackgroundRemovalDecision.ManualResultForReviewedContent,
        "USE_AUTOMATIC_SELECTION_FOR_REVIEWED_CONTENT" =>
            BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent,
        _ => throw new InvalidOperationException($"Unknown BackgroundRemovalDecision '{text}' in database."),
    };

    /// <summary>
    /// Rebuilds a <see cref="BackgroundRemovalAuthority"/> from its three columns, or null when
    /// none was stored (Epic 11300 Part C2B1 §10).
    /// </summary>
    /// <remarks>
    /// All three columns are required together, and a row holding only some of them is refused
    /// rather than patched up. The authority means "automatic selection is authorised for THIS
    /// reviewed content", so a decision with no artefact attached is not a weaker authority --
    /// it is the session-wide permission this design exists to prevent (§4).
    /// <para>
    /// Goes back through <see cref="BackgroundRemovalAuthority.For"/> rather than constructing
    /// the record directly, so a stored <c>UNSPECIFIED</c> is refused here exactly as the command
    /// refuses it. Reading is the last place that invariant can still be enforced, and the
    /// database CHECK is the first.
    /// </para>
    /// </remarks>
    public static BackgroundRemovalAuthority? ToBackgroundRemovalAuthority(
        string? decision, string? revisionId, string? reviewedSha)
    {
        if (decision is null && revisionId is null && reviewedSha is null)
        {
            return null;
        }

        if (decision is null || revisionId is null || reviewedSha is null)
        {
            throw new InvalidOperationException(
                "A background-removal authority row is missing part of its content binding; " +
                "a decision without the reviewed Revision and hash authorises nothing specific.");
        }

        return BackgroundRemovalAuthority.For(
            ToBackgroundRemovalDecision(decision),
            RevisionId.From(Guid.Parse(revisionId)),
            Sha256.Parse(reviewedSha));
    }

    // ---------------------------------------------------------------------------------------
    // Maximum-bound print preparation (Epic 11400 Part B1A.2A §11, §13)
    // ---------------------------------------------------------------------------------------

    public static string ToText(PrintDimensionSemantics value) => value switch
    {
        PrintDimensionSemantics.LegacyExactPair => "LEGACY_EXACT_PAIR",
        PrintDimensionSemantics.MaxBoundsV1 => "MAX_BOUNDS_V1",
        PrintDimensionSemantics.TargetEdgeV1 => "TARGET_EDGE_V1",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    /// <summary>
    /// Reads a stored dimension reading, refusing one this build does not understand.
    /// </summary>
    /// <remarks>
    /// The forward half of §13's fail-closed rule. A database written by a newer PrintFlow is
    /// already refused wholesale by <see cref="MigrationRunner"/>, but a value this build cannot
    /// interpret must not become a default here either: silently reading an unknown marker as
    /// maximum bounds is exactly the silent reinterpretation §9 exists to prevent.
    /// </remarks>
    public static PrintDimensionSemantics ToPrintDimensionSemantics(string text) => text switch
    {
        "LEGACY_EXACT_PAIR" => PrintDimensionSemantics.LegacyExactPair,
        "MAX_BOUNDS_V1" => PrintDimensionSemantics.MaxBoundsV1,
        "TARGET_EDGE_V1" => PrintDimensionSemantics.TargetEdgeV1,
        _ => throw new InvalidOperationException(
            $"Unsupported dimension semantics '{text}' in database; this build cannot say what those " +
            "millimetres mean, and will not guess."),
    };

    public static string ToText(PrintPreparationMode value) => value switch
    {
        PrintPreparationMode.ResolutionOnly => "RESOLUTION_ONLY",
        PrintPreparationMode.ProportionalShrink => "PROPORTIONAL_SHRINK",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static PrintPreparationMode ToPrintPreparationMode(string text) => text switch
    {
        "RESOLUTION_ONLY" => PrintPreparationMode.ResolutionOnly,
        "PROPORTIONAL_SHRINK" => PrintPreparationMode.ProportionalShrink,
        _ => throw new InvalidOperationException($"Unknown PrintPreparationMode '{text}' in database."),
    };

    public static string ToText(LimitingEdge value) => value switch
    {
        LimitingEdge.None => "NONE",
        LimitingEdge.Width => "WIDTH",
        LimitingEdge.Height => "HEIGHT",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static LimitingEdge ToLimitingEdge(string text) => text switch
    {
        "NONE" => LimitingEdge.None,
        "WIDTH" => LimitingEdge.Width,
        "HEIGHT" => LimitingEdge.Height,
        _ => throw new InvalidOperationException($"Unknown LimitingEdge '{text}' in database."),
    };

    /// <summary>
    /// The neutral resampling policy. Deliberately PrintFlow's vocabulary, never Photoshop's.
    /// </summary>
    /// <remarks>
    /// <c>ResampleMethod.NONE</c> and <c>ResampleMethod.BICUBICSHARPER</c> are a COM detail of one
    /// adapter, and a database that stored them would have made the schema depend on an
    /// application's automation surface. The mapping to those values belongs beside the Photoshop
    /// driver when a production resize exists (Epic 11400 Part B1A.2A §5).
    /// </remarks>
    public static string ToText(PhotoshopResizeMode value) => value switch
    {
        PhotoshopResizeMode.None => "NONE",
        PhotoshopResizeMode.BicubicSharper => "BICUBIC_SHARPER",
        PhotoshopResizeMode.PreserveDetails => "PRESERVE_DETAILS",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static PhotoshopResizeMode ToPhotoshopResizeMode(string text) => text switch
    {
        "NONE" => PhotoshopResizeMode.None,
        "BICUBIC_SHARPER" => PhotoshopResizeMode.BicubicSharper,
        "PRESERVE_DETAILS" => PhotoshopResizeMode.PreserveDetails,
        _ => throw new InvalidOperationException($"Unknown PhotoshopResizeMode '{text}' in database."),
    };

    /// <summary>
    /// Rebuilds a <see cref="PrintPreparationPlan"/> from its columns, or null when none was
    /// stored (Epic 11400 Part B1A.2A §13).
    /// </summary>
    /// <remarks>
    /// The last defence, behind the database CHECK. Every column of the group is required
    /// together and a row holding only some of them is refused rather than patched up: each
    /// missing piece is one a reader would otherwise have to invent, and a plan with an invented
    /// limiting edge is a plan that hands Photoshop an edge nobody calculated.
    /// <para>
    /// Goes back through <see cref="PrintPreparationPlan.Rehydrate"/> rather than constructing the
    /// record directly, so a self-contradictory row — a shrink with no edge, an edge with no
    /// value, projected pixels larger than the source — is refused here exactly as the domain
    /// refuses it. It deliberately does <b>not</b> recalculate the fit: recomputing on read would
    /// repair a bad row rather than reject it.
    /// </para>
    /// <para>
    /// <paramref name="limitingValueMm"/> is outside the all-or-nothing group on purpose. Its
    /// absence is meaningful — a resolution-only plan writes no millimetre value — and
    /// <c>Rehydrate</c> is what pairs it with the mode.
    /// </para>
    /// </remarks>
    public static PrintPreparationPlan? ToPrintPreparationPlan(
        string? sourceRevisionId,
        string? sourceSha256,
        int? sourcePixelWidth,
        int? sourcePixelHeight,
        double? maxWidthMm,
        double? maxHeightMm,
        string? limitKind,
        string? mode,
        string? limitingEdge,
        double? limitingValueMm,
        int? projectedPixelWidth,
        int? projectedPixelHeight,
        int? productionDpi,
        string? resizePolicy)
    {
        bool anyPresent =
            sourceRevisionId is not null || sourceSha256 is not null ||
            sourcePixelWidth is not null || sourcePixelHeight is not null ||
            maxWidthMm is not null || maxHeightMm is not null ||
            limitKind is not null || mode is not null ||
            limitingEdge is not null || limitingValueMm is not null ||
            projectedPixelWidth is not null || projectedPixelHeight is not null ||
            productionDpi is not null || resizePolicy is not null;

        if (!anyPresent)
        {
            return null;
        }

        if (sourceRevisionId is null || sourceSha256 is null ||
            sourcePixelWidth is null || sourcePixelHeight is null ||
            maxWidthMm is null || maxHeightMm is null ||
            limitKind is null || mode is null ||
            limitingEdge is null ||
            projectedPixelWidth is null || projectedPixelHeight is null ||
            productionDpi is null || resizePolicy is null)
        {
            throw new InvalidOperationException(
                "A print preparation plan row is missing part of its content; a plan without its source " +
                "binding, its bounds, its limiting edge and its projected pixels describes no executable " +
                "operation, and nothing here fills the gaps in.");
        }

        if (productionDpi.Value != PrintDimensions.ProductionDpi)
        {
            throw new InvalidOperationException(
                $"A print preparation plan row claims {productionDpi.Value} ppi; production resolution is " +
                $"fixed at {PrintDimensions.ProductionDpi} and is never operator-selected.");
        }

        return PrintPreparationPlan.Rehydrate(
            RevisionId.From(Guid.Parse(sourceRevisionId)),
            Sha256.Parse(sourceSha256),
            sourcePixelWidth.Value,
            sourcePixelHeight.Value,
            maxWidthMm.Value,
            maxHeightMm.Value,
            ToSizePreset(limitKind),
            ToPrintPreparationMode(mode),
            ToLimitingEdge(limitingEdge),
            limitingValueMm,
            projectedPixelWidth.Value,
            projectedPixelHeight.Value,
            ToPhotoshopResizeMode(resizePolicy));
    }


    // ---------------------------------------------------------------------------------------
    // Flexible size, target-edge plan and enlargement authority (Epic 11400 Part B1A.2D §7, §23)
    // ---------------------------------------------------------------------------------------

    public static string ToText(OperatorSizingMode value) => value switch
    {
        OperatorSizingMode.PresetFit => "PRESET_FIT",
        OperatorSizingMode.CustomTargetEdge => "CUSTOM_TARGET_EDGE",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static OperatorSizingMode ToOperatorSizingMode(string text) => text switch
    {
        "PRESET_FIT" => OperatorSizingMode.PresetFit,
        "CUSTOM_TARGET_EDGE" => OperatorSizingMode.CustomTargetEdge,
        _ => throw new InvalidOperationException(
            $"Unsupported sizing mode '{text}' in database; this build cannot say how that size was " +
            "chosen, and will not guess."),
    };

    public static string ToText(TargetEdge value) => value switch
    {
        TargetEdge.Width => "WIDTH",
        TargetEdge.Height => "HEIGHT",
        TargetEdge.LongEdge => "LONG_EDGE",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static TargetEdge ToTargetEdge(string text) => text switch
    {
        "WIDTH" => TargetEdge.Width,
        "HEIGHT" => TargetEdge.Height,
        "LONG_EDGE" => TargetEdge.LongEdge,
        _ => throw new InvalidOperationException($"Unknown TargetEdge '{text}' in database."),
    };

    public static string ToText(ResizeDirection value) => value switch
    {
        ResizeDirection.ResolutionOnly => "RESOLUTION_ONLY",
        ResizeDirection.Shrink => "SHRINK",
        ResizeDirection.Enlarge => "ENLARGE",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static ResizeDirection ToResizeDirection(string text) => text switch
    {
        "RESOLUTION_ONLY" => ResizeDirection.ResolutionOnly,
        "SHRINK" => ResizeDirection.Shrink,
        "ENLARGE" => ResizeDirection.Enlarge,
        _ => throw new InvalidOperationException($"Unknown ResizeDirection '{text}' in database."),
    };

    public static string ToText(PresetRecommendationKind value) => value switch
    {
        PresetRecommendationKind.MaximumBox => "MAXIMUM_BOX",
        PresetRecommendationKind.MaximumLongEdge => "MAXIMUM_LONG_EDGE",
        PresetRecommendationKind.MaximumShortEdge => "MAXIMUM_SHORT_EDGE",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static PresetRecommendationKind ToPresetRecommendationKind(string text) => text switch
    {
        "MAXIMUM_BOX" => PresetRecommendationKind.MaximumBox,
        "MAXIMUM_LONG_EDGE" => PresetRecommendationKind.MaximumLongEdge,
        "MAXIMUM_SHORT_EDGE" => PresetRecommendationKind.MaximumShortEdge,
        _ => throw new InvalidOperationException($"Unknown PresetRecommendationKind '{text}' in database."),
    };

    /// <summary>
    /// Writes millimetres as text, exactly (Epic 11400 Part B1A.2D §7).
    /// </summary>
    /// <remarks>
    /// The accepted target-edge calculation is exact: it converts the operator's decimal to a
    /// rational and rounds on integer remainders. Storing that decimal in a SQLite REAL would put
    /// it through a binary double on the way out and back, and 137.5 mm returning as
    /// 137.49999999999999 would decide a midpoint case somewhere other than where the contract
    /// decides it — and would stop matching the enlargement authority granted for it.
    /// <para>
    /// <c>decimal.ToString</c> under the invariant culture round-trips a decimal exactly,
    /// including its scale, so what comes back is the number the operator typed rather than the
    /// nearest representable one. No arithmetic happens here; this is transport (§7).
    /// </para>
    /// </remarks>
    public static string ToMillimetreText(decimal millimetres) =>
        millimetres.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc cref="ToMillimetreText" />
    public static decimal ToMillimetres(string text) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
            ? value
            : throw new InvalidOperationException(
                $"'{text}' is not an exact millimetre value; a stored size that cannot be read back " +
                "exactly is not a size this build will act on.");

    /// <summary>
    /// Rebuilds a <see cref="FlexibleSizeSelection"/> from its columns, or null when none was
    /// stored (Epic 11400 Part B1A.2D §22, §23).
    /// </summary>
    /// <remarks>
    /// The last defence behind the database CHECK, and it refuses rather than repairs. A stored
    /// preset fit that also carries a requested edge, an override with no recommendation behind
    /// it, or a custom target with no millimetres are each a row describing a decision no operator
    /// could have made — and every one of them goes back through
    /// <see cref="FlexibleSizeSelection.Rehydrate"/>, which is where those rules already live.
    /// <para>
    /// Null means no flexible-size decision was recorded — a historical row, or a session whose
    /// bounds were typed rather than chosen from a named preset. It never means "PresetFit was
    /// assumed" (§21).
    /// </para>
    /// </remarks>
    public static FlexibleSizeSelection? ToFlexibleSizeSelection(
        string? sizingMode,
        string? preset,
        string? recommendationKind,
        string? recommendationMaxWidthMm,
        string? recommendationMaxHeightMm,
        bool? presetOverridden,
        string? targetEdge,
        string? requestedMm)
    {
        if (sizingMode is null)
        {
            bool anyPresent =
                preset is not null || recommendationKind is not null ||
                recommendationMaxWidthMm is not null || recommendationMaxHeightMm is not null ||
                presetOverridden is not null || targetEdge is not null || requestedMm is not null;

            return anyPresent
                ? throw new InvalidOperationException(
                    "A flexible-size row carries a preset, an edge or a request without saying which " +
                    "sizing mode produced them; nothing here infers the mode from what is beside it.")
                : null;
        }

        PresetPrintRecommendation? recommendation = null;
        if (preset is not null || recommendationKind is not null ||
            recommendationMaxWidthMm is not null || recommendationMaxHeightMm is not null)
        {
            if (preset is null || recommendationKind is null ||
                recommendationMaxWidthMm is null || recommendationMaxHeightMm is null)
            {
                throw new InvalidOperationException(
                    "A stored preset recommendation is missing part of its content; a named size without " +
                    "its configured kind and limits is not an executable recommendation.");
            }

            PresetRecommendationKind kind = ToPresetRecommendationKind(recommendationKind);
            decimal width = ToMillimetres(recommendationMaxWidthMm);
            decimal height = ToMillimetres(recommendationMaxHeightMm);

            recommendation = kind switch
            {
                PresetRecommendationKind.MaximumLongEdge => width == height
                    ? PresetPrintRecommendation.MaximumLongEdge(ToSizePreset(preset), width)
                    : throw new InvalidOperationException(
                        $"A stored long-edge recommendation carries two different limits ({width} and " +
                        $"{height} mm); a long edge is one number."),
                PresetRecommendationKind.MaximumShortEdge => width == height
                    ? PresetPrintRecommendation.MaximumShortEdge(ToSizePreset(preset), width)
                    : throw new InvalidOperationException(
                        $"A stored short-edge recommendation carries two different limits ({width} and " +
                        $"{height} mm); a short edge is one number."),
                _ => PresetPrintRecommendation.MaximumBox(ToSizePreset(preset), width, height),
            };
        }

        return FlexibleSizeSelection.Rehydrate(
            ToOperatorSizingMode(sizingMode),
            recommendation,
            presetOverridden ?? false,
            targetEdge is null ? null : ToTargetEdge(targetEdge),
            requestedMm is null ? null : ToMillimetres(requestedMm));
    }

    /// <summary>
    /// Rebuilds a <see cref="TargetEdgePrintPreparationPlan"/> from its columns, or null when none
    /// was stored (Epic 11400 Part B1A.2D §8, §23).
    /// </summary>
    /// <remarks>
    /// The same rule the maximum-bound mapper follows, applied to the second contract: the whole
    /// group is required together, and it goes back through
    /// <see cref="TargetEdgePrintPreparationPlan.Rehydrate"/> so a self-contradictory row — an
    /// unresolved long edge, an enlargement claiming BicubicSharper, a scale that is not the ratio
    /// between the stored pixels — is refused here exactly as the Domain refuses it. It
    /// deliberately does <b>not</b> recalculate the projection: recomputing on read would repair a
    /// bad row rather than reject it.
    /// <para>
    /// The requested millimetres come from <paramref name="selection"/> rather than from a column
    /// of their own. They are the operator's decision and they live once; a second copy beside the
    /// plan would be a second number that could disagree with it (§7).
    /// </para>
    /// </remarks>
    public static TargetEdgePrintPreparationPlan? ToTargetEdgePlan(
        FlexibleSizeSelection? selection,
        string? sourceRevisionId,
        string? sourceSha256,
        int? sourcePixelWidth,
        int? sourcePixelHeight,
        string? photoshopEdge,
        int? projectedPixelWidth,
        int? projectedPixelHeight,
        int? scaleNumerator,
        int? scaleDenominator,
        int? productionDpi,
        string? direction,
        string? resizePolicy)
    {
        bool anyPresent =
            sourceRevisionId is not null || sourceSha256 is not null ||
            sourcePixelWidth is not null || sourcePixelHeight is not null ||
            photoshopEdge is not null ||
            projectedPixelWidth is not null || projectedPixelHeight is not null ||
            scaleNumerator is not null || scaleDenominator is not null ||
            productionDpi is not null || direction is not null || resizePolicy is not null;

        if (!anyPresent)
        {
            return null;
        }

        if (sourceRevisionId is null || sourceSha256 is null ||
            sourcePixelWidth is null || sourcePixelHeight is null ||
            photoshopEdge is null ||
            projectedPixelWidth is null || projectedPixelHeight is null ||
            scaleNumerator is null || scaleDenominator is null ||
            productionDpi is null || direction is null || resizePolicy is null)
        {
            throw new InvalidOperationException(
                "A target-edge plan row is missing part of its content; a plan without its source " +
                "binding, its resolved edge, its projected pixels and its exact scale describes no " +
                "executable operation, and nothing here fills the gaps in.");
        }

        if (selection is null)
        {
            throw new InvalidOperationException(
                "A target-edge plan row carries no sizing selection; the requested edge and millimetres " +
                "are the operator's decision and the plan is meaningless without them.");
        }

        if (productionDpi.Value != PrintDimensions.ProductionDpi)
        {
            throw new InvalidOperationException(
                $"A target-edge plan row claims {productionDpi.Value} ppi; production resolution is " +
                $"fixed at {PrintDimensions.ProductionDpi} and is never operator-selected.");
        }

        return TargetEdgePrintPreparationPlan.Rehydrate(
            RevisionId.From(Guid.Parse(sourceRevisionId)),
            Sha256.Parse(sourceSha256),
            sourcePixelWidth.Value,
            sourcePixelHeight.Value,
            selection,
            ToLimitingEdge(photoshopEdge),
            projectedPixelWidth.Value,
            projectedPixelHeight.Value,
            ResizeScale.FromReduced(scaleNumerator.Value, scaleDenominator.Value),
            ToResizeDirection(direction),
            ToPhotoshopResizeMode(resizePolicy));
    }

    /// <summary>
    /// Rebuilds an <see cref="EnlargementAuthority"/> from its columns, or null when none was
    /// stored (Epic 11400 Part B1A.2D §9, §23).
    /// </summary>
    /// <remarks>
    /// Completeness is the whole rule. An authority missing any one of the facts it binds — the
    /// hash above all — is not a weaker permission but a permission for something unspecified, so
    /// a partial row is refused rather than read as covering whatever sits beside it.
    /// </remarks>
    public static EnlargementAuthority? ToEnlargementAuthority(
        string? sourceRevisionId,
        string? sourceSha256,
        string? sizingMode,
        string? targetEdge,
        string? requestedMm,
        int? scaleNumerator,
        int? scaleDenominator,
        int? projectedPixelWidth,
        int? projectedPixelHeight)
    {
        bool anyPresent =
            sourceRevisionId is not null || sourceSha256 is not null || sizingMode is not null ||
            targetEdge is not null || requestedMm is not null ||
            scaleNumerator is not null || scaleDenominator is not null ||
            projectedPixelWidth is not null || projectedPixelHeight is not null;

        if (!anyPresent)
        {
            return null;
        }

        if (sourceRevisionId is null || sourceSha256 is null || sizingMode is null ||
            targetEdge is null || requestedMm is null ||
            scaleNumerator is null || scaleDenominator is null ||
            projectedPixelWidth is null || projectedPixelHeight is null)
        {
            throw new InvalidOperationException(
                "An enlargement authority row is missing part of its binding; a permission that cannot " +
                "name the exact source, edge, request and projection it covers permits nothing specific.");
        }

        return EnlargementAuthority.Rehydrate(
            RevisionId.From(Guid.Parse(sourceRevisionId)),
            Sha256.Parse(sourceSha256),
            ToOperatorSizingMode(sizingMode),
            ToTargetEdge(targetEdge),
            ToMillimetres(requestedMm),
            ResizeScale.FromReduced(scaleNumerator.Value, scaleDenominator.Value),
            projectedPixelWidth.Value,
            projectedPixelHeight.Value);
    }

    /// <summary>
    /// Rebuilds the immutable preparation one attempt ran under, or null when it was not a
    /// Photoshop output (Epic 11400 Part B1A.2D §24).
    /// </summary>
    /// <remarks>
    /// Which contract the attempt ran under is stated by which column group is populated, and a
    /// row holding both is refused — the database says so too, and saying it twice is deliberate:
    /// an attempt that claimed two different geometries would be an audit row with no single
    /// answer to "what produced this file".
    /// <para>
    /// A target-edge enlargement is rebuilt <i>with</i> its authority, through
    /// <see cref="TargetEdgePreparation"/>'s constructor, which refuses an enlargement whose
    /// authority does not match. So a tampered row claiming an unauthorised enlargement fails to
    /// load rather than loading as permitted.
    /// </para>
    /// </remarks>
    public static PhotoshopPreparation? ToPhotoshopPreparation(
        PrintPreparationPlan? boundsPlan,
        TargetEdgePrintPreparationPlan? targetEdgePlan,
        EnlargementAuthority? authority,
        FlexibleSizeSelection? selection = null)
    {
        if (boundsPlan is not null && targetEdgePlan is not null)
        {
            throw new InvalidOperationException(
                "An attempt row holds both a maximum-bound plan and a target-edge plan; one run has one " +
                "geometry, and this build will not choose between them.");
        }

        if (boundsPlan is not null)
        {
            return authority is null
                // The ordinary preset fit the run recorded, when it recorded one. A row written
                // before the post-final A5 correction carries none, and stays a plan with no
                // selection rather than acquiring an invented one (§12, §18).
                ? new FitWithinBoundsPreparation(
                    boundsPlan,
                    selection is { Mode: OperatorSizingMode.PresetFit } fit ? fit : null)
                : throw new InvalidOperationException(
                    "An attempt row holds an enlargement authority beside a maximum-bound plan, which " +
                    "can never enlarge; the row describes a permission for a run nobody asked for.");
        }

        return targetEdgePlan is null ? null : new TargetEdgePreparation(targetEdgePlan, authority);
    }
    public static string ToText(WhiteUnderbaseBranch value) => value switch
    {
        WhiteUnderbaseBranch.W1_0px => "W1_0PX",
        WhiteUnderbaseBranch.W1_1px => "W1_1PX",
        WhiteUnderbaseBranch.W1_2px => "W1_2PX",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static WhiteUnderbaseBranch ToWhiteUnderbaseBranch(string text) => text switch
    {
        "W1_0PX" => WhiteUnderbaseBranch.W1_0px,
        "W1_1PX" => WhiteUnderbaseBranch.W1_1px,
        "W1_2PX" => WhiteUnderbaseBranch.W1_2px,
        _ => throw new InvalidOperationException($"Unknown WhiteUnderbaseBranch '{text}' in database."),
    };

    public static string ToText(SizePreset value) => value switch
    {
        SizePreset.A3Landscape => "A3_LANDSCAPE",
        SizePreset.A3Portrait => "A3_PORTRAIT",
        SizePreset.A4 => "A4",
        SizePreset.A5 => "A5",
        SizePreset.Custom => "CUSTOM",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static SizePreset ToSizePreset(string text) => text switch
    {
        "A3_LANDSCAPE" => SizePreset.A3Landscape,
        "A3_PORTRAIT" => SizePreset.A3Portrait,
        "A4" => SizePreset.A4,
        "A5" => SizePreset.A5,
        "CUSTOM" => SizePreset.Custom,
        _ => throw new InvalidOperationException($"Unknown SizePreset '{text}' in database."),
    };

    // Not database-constrained: no fixed enumeration exists in the schema, so the plain enum
    // name round-trips safely and needs no translation table to keep in sync.
    public static string ToText(StepKind value) => value.ToString();

    public static StepKind ToStepKind(string text) => Enum.Parse<StepKind>(text);

    public static string ToText(ImageFormat value) => value.ToString().ToUpperInvariant();

    public static ImageFormat ToImageFormat(string text) =>
        Enum.Parse<ImageFormat>(text, ignoreCase: true);

    public static string ToText(ColourMode value) => value.ToString().ToUpperInvariant();

    public static ColourMode ToColourMode(string text) => Enum.Parse<ColourMode>(text, ignoreCase: true);

    // ------------------------------------------------------------------------------------
    // Row <-> domain record
    // ------------------------------------------------------------------------------------

    public static SessionRow ToRow(ProcessingSession session) => new()
    {
        Id = session.Id.ToString(),
        WorkflowType = ToText(session.WorkflowType),
        OutputName = session.OutputName.Value,
        CurrentStep = ToText(session.CurrentStep),
        State = ToText(session.State),
        WorkspacePath = session.Workspace.RelativePath,
        CreatedAtUtc = ToText(session.CreatedAtUtc),
        UpdatedAtUtc = ToText(session.UpdatedAtUtc),
        CompletedAtUtc = ToTextOrNull(session.CompletedAtUtc),
        HandedOffAtUtc = ToTextOrNull(session.HandedOffAtUtc),
        HandOffReason = session.HandOffReason,
        AbandonedAtUtc = ToTextOrNull(session.AbandonedAtUtc),
        AbandonReason = session.AbandonReason,
        DimensionsWidthMm = session.Dimensions?.WidthMm,
        DimensionsHeightMm = session.Dimensions?.HeightMm,
        DimensionsPixelWidth = session.Dimensions?.PixelWidth,
        DimensionsPixelHeight = session.Dimensions?.PixelHeight,
        DimensionsPreset = session.Dimensions is { } d ? ToText(d.Preset) : null,
        WhiteUnderbaseBranch = session.WhiteUnderbaseBranch is { } b ? ToText(b) : null,

        // Always written, unlike the attempt's copy: a session always has a pending trim
        // decision, and Tight is a decision rather than an absence (Epic 11200 Part C3 §10).
        TrimMode = ToText(session.TrimMargin.Mode),
        TrimMarginTop = session.TrimMargin.Top,
        TrimMarginRight = session.TrimMargin.Right,
        TrimMarginBottom = session.TrimMargin.Bottom,
        TrimMarginLeft = session.TrimMargin.Left,

        // Written only when one exists, unlike the trim margin: a session always has a pending
        // trim decision, and never has a background-removal authority until a human grants one
        // (Epic 11300 Part C2B1 §10).
        BackgroundRemovalDecision = session.BackgroundRemovalAuthority is { } bra ? ToText(bra.Decision) : null,
        BackgroundRemovalRevisionId = session.BackgroundRemovalAuthority?.ReviewedRevisionId.ToString(),
        BackgroundRemovalReviewedSha = session.BackgroundRemovalAuthority?.ReviewedSha256.Value,

        // Written whenever dimensions are, because it is half of what those millimetres say
        // rather than metadata about them (Epic 11400 Part B1A.2A §9).
        DimensionSemantics = session.DimensionSemantics is { } semantics ? ToText(semantics) : null,

        // The session's *pending* plan, so it does update on conflict: it is what the next run
        // would do, and the operator may record different limits. The attempt's copy is the one
        // that must never be rewritten (§12).
        PrintPlanSourceRevisionId = session.PrintPreparationPlan?.SourceRevisionId.ToString(),
        PrintPlanSourceSha256 = session.PrintPreparationPlan?.SourceSha256.Value,
        PrintPlanSourcePixelWidth = session.PrintPreparationPlan?.SourcePixelWidth,
        PrintPlanSourcePixelHeight = session.PrintPreparationPlan?.SourcePixelHeight,
        PrintPlanMaxWidthMm = session.PrintPreparationPlan?.MaxWidthMm,
        PrintPlanMaxHeightMm = session.PrintPreparationPlan?.MaxHeightMm,
        PrintPlanLimitKind = session.PrintPreparationPlan is { } sp ? ToText(sp.LimitKind) : null,
        PrintPlanMode = session.PrintPreparationPlan is { } sm ? ToText(sm.Mode) : null,
        PrintPlanLimitingEdge = session.PrintPreparationPlan is { } se ? ToText(se.LimitingEdge) : null,
        PrintPlanLimitingValueMm = session.PrintPreparationPlan?.LimitingValueMm,
        PrintPlanProjectedPixelWidth = session.PrintPreparationPlan?.ProjectedPixelWidth,
        PrintPlanProjectedPixelHeight = session.PrintPreparationPlan?.ProjectedPixelHeight,
        PrintPlanProductionDpi = session.PrintPreparationPlan?.ProductionDpi,
        PrintPlanResizePolicy = session.PrintPreparationPlan is { } sr ? ToText(sr.ResizePolicy) : null,

        // The pending flexible-size decision, written only when one exists. The recommendation is
        // written beside the override rather than replaced by it, so an override stays readable as
        // an override of a specific configured limit (Epic 11400 Part B1A.2D §6).
        SizingMode = session.SizeSelection is { } ss ? ToText(ss.Mode) : null,
        SizingPreset = session.SizeSelection?.Recommendation is { } sc ? ToText(sc.Preset) : null,
        SizingRecommendationKind =
            session.SizeSelection?.Recommendation is { } sk ? ToText(sk.Kind) : null,
        SizingRecommendationMaxWidthMm = session.SizeSelection?.Recommendation is { } sw
            ? ToMillimetreText(sw.MaxWidthMm)
            : null,
        SizingRecommendationMaxHeightMm = session.SizeSelection?.Recommendation is { } sh
            ? ToMillimetreText(sh.MaxHeightMm)
            : null,
        SizingPresetOverridden = session.SizeSelection?.PresetOverridden,
        SizingTargetEdge =
            session.SizeSelection?.SelectedTargetEdge is { } ste ? ToText(ste) : null,
        SizingRequestedMm = session.SizeSelection?.RequestedMillimetres is { } srm
            ? ToMillimetreText(srm)
            : null,

        TargetPlanSourceRevisionId = session.TargetEdgePlan?.SourceRevisionId.ToString(),
        TargetPlanSourceSha256 = session.TargetEdgePlan?.SourceSha256.Value,
        TargetPlanSourcePixelWidth = session.TargetEdgePlan?.SourcePixelWidth,
        TargetPlanSourcePixelHeight = session.TargetEdgePlan?.SourcePixelHeight,
        TargetPlanPhotoshopEdge = session.TargetEdgePlan is { } tpe
            ? ToText(tpe.Projection.PhotoshopTargetEdge)
            : null,
        TargetPlanProjectedPixelWidth = session.TargetEdgePlan?.Projection.ProjectedPixelWidth,
        TargetPlanProjectedPixelHeight = session.TargetEdgePlan?.Projection.ProjectedPixelHeight,
        TargetPlanScaleNumerator = session.TargetEdgePlan?.Projection.ProjectedScale.Numerator,
        TargetPlanScaleDenominator = session.TargetEdgePlan?.Projection.ProjectedScale.Denominator,
        TargetPlanProductionDpi = session.TargetEdgePlan?.ProductionDpi,
        TargetPlanDirection = session.TargetEdgePlan is { } tpd
            ? ToText(tpd.Projection.Direction)
            : null,
        TargetPlanResizePolicy = session.TargetEdgePlan is { } tpp
            ? ToText(tpp.Projection.ResizePolicy)
            : null,

        EnlargementAuthoritySourceRevisionId =
            session.EnlargementAuthority?.SourceRevisionId.ToString(),
        EnlargementAuthoritySourceSha256 = session.EnlargementAuthority?.SourceSha256.Value,
        EnlargementAuthoritySizingMode =
            session.EnlargementAuthority is { } eam ? ToText(eam.SizingMode) : null,
        EnlargementAuthorityTargetEdge =
            session.EnlargementAuthority is { } eae ? ToText(eae.SelectedTargetEdge) : null,
        EnlargementAuthorityRequestedMm = session.EnlargementAuthority is { } ear
            ? ToMillimetreText(ear.RequestedMillimetres)
            : null,
        EnlargementAuthorityScaleNumerator = session.EnlargementAuthority?.ProjectedScale.Numerator,
        EnlargementAuthorityScaleDenominator =
            session.EnlargementAuthority?.ProjectedScale.Denominator,
        EnlargementAuthorityProjectedPixelWidth =
            session.EnlargementAuthority?.ProjectedTargetPixelWidth,
        EnlargementAuthorityProjectedPixelHeight =
            session.EnlargementAuthority?.ProjectedTargetPixelHeight,
    };

    public static ProcessingSession ToDomain(SessionRow row)
    {
        PrintDimensions? dimensions = row.DimensionsWidthMm is double width
            ? PrintDimensions.FromMillimetres(
                width, row.DimensionsHeightMm!.Value, ToSizePreset(row.DimensionsPreset!))
            : null;

        // Read once and used twice: the target-edge plan is rehydrated with the very selection the
        // session holds, so the requested edge and millimetres exist in exactly one place and the
        // plan cannot disagree with the decision it came from (Epic 11400 Part B1A.2D §7).
        FlexibleSizeSelection? selection = ToFlexibleSizeSelection(
            row.SizingMode, row.SizingPreset, row.SizingRecommendationKind,
            row.SizingRecommendationMaxWidthMm, row.SizingRecommendationMaxHeightMm,
            row.SizingPresetOverridden, row.SizingTargetEdge, row.SizingRequestedMm);

        return new ProcessingSession(
            SessionId.From(Guid.Parse(row.Id)),
            ToWorkflowType(row.WorkflowType),
            OutputName.Parse(row.OutputName),
            ToStepKind(row.CurrentStep),
            ToSessionState(row.State),
            WorkspaceDirRef.Create(row.WorkspacePath),
            ToDateTimeOffset(row.CreatedAtUtc),
            ToDateTimeOffset(row.UpdatedAtUtc),
            ToDateTimeOffsetOrNull(row.CompletedAtUtc),
            ToDateTimeOffsetOrNull(row.HandedOffAtUtc),
            row.HandOffReason,
            ToDateTimeOffsetOrNull(row.AbandonedAtUtc),
            row.AbandonReason,
            dimensions,
            row.WhiteUnderbaseBranch is string wub ? ToWhiteUnderbaseBranch(wub) : null)
        {
            // A row written before migration 0002 has no trim columns at all, and reads back as
            // Tight — which is exactly what it ran with, because Tight was the only behaviour
            // before this slice (Epic 11200 Part C3 §10).
            TrimMargin = ToTrimMargin(
                row.TrimMode, row.TrimMarginTop, row.TrimMarginRight,
                row.TrimMarginBottom, row.TrimMarginLeft) ?? TrimMargin.Tight,

            // A row written before migration 0003 has no background-removal columns and reads
            // back as null, which is exactly what it meant: no authority was ever recorded, so
            // background removal on that session still needs an explicit decision (§7).
            BackgroundRemovalAuthority = ToBackgroundRemovalAuthority(
                row.BackgroundRemovalDecision, row.BackgroundRemovalRevisionId,
                row.BackgroundRemovalReviewedSha),

            // A row written before migration 0005 was backfilled to LEGACY_EXACT_PAIR by that
            // migration, so a resumed pre-contract session reads back saying exactly what it was:
            // two independently exact dimensions, kept for audit and not executable as a fit box
            // until the operator reconfirms them (Epic 11400 Part B1A.2A §9, §10). A row with no
            // dimensions at all reads back null here, which is the honest absence.
            DimensionSemantics = row.DimensionSemantics is string ds
                ? ToPrintDimensionSemantics(ds)
                : null,

            // Null on every legacy row, and on every session that has not recorded bounds under
            // the current contract. Null never means "the default plan"; there is none.
            PrintPreparationPlan = ToPrintPreparationPlan(
                row.PrintPlanSourceRevisionId, row.PrintPlanSourceSha256,
                row.PrintPlanSourcePixelWidth, row.PrintPlanSourcePixelHeight,
                row.PrintPlanMaxWidthMm, row.PrintPlanMaxHeightMm,
                row.PrintPlanLimitKind, row.PrintPlanMode,
                row.PrintPlanLimitingEdge, row.PrintPlanLimitingValueMm,
                row.PrintPlanProjectedPixelWidth, row.PrintPlanProjectedPixelHeight,
                row.PrintPlanProductionDpi, row.PrintPlanResizePolicy),

            // Null on every row written before migration 0006, and on every session that recorded
            // maximum bounds rather than a flexible size. That row is not upgraded: a valid
            // MaxBoundsV1 session stays a MaxBoundsV1 session under its original accepted
            // semantics, and nothing here invents a target edge it never had (§18, §21).
            SizeSelection = selection,
            TargetEdgePlan = ToTargetEdgePlan(
                selection,
                row.TargetPlanSourceRevisionId, row.TargetPlanSourceSha256,
                row.TargetPlanSourcePixelWidth, row.TargetPlanSourcePixelHeight,
                row.TargetPlanPhotoshopEdge,
                row.TargetPlanProjectedPixelWidth, row.TargetPlanProjectedPixelHeight,
                row.TargetPlanScaleNumerator, row.TargetPlanScaleDenominator,
                row.TargetPlanProductionDpi, row.TargetPlanDirection, row.TargetPlanResizePolicy),

            // Restored as a record, never as permission. Whether it still authorises anything is
            // an exact match against the plan on offer, which WorkflowSnapshot decides — so
            // reopening the app is not a way to acquire permission (§30).
            EnlargementAuthority = ToEnlargementAuthority(
                row.EnlargementAuthoritySourceRevisionId, row.EnlargementAuthoritySourceSha256,
                row.EnlargementAuthoritySizingMode, row.EnlargementAuthorityTargetEdge,
                row.EnlargementAuthorityRequestedMm,
                row.EnlargementAuthorityScaleNumerator, row.EnlargementAuthorityScaleDenominator,
                row.EnlargementAuthorityProjectedPixelWidth,
                row.EnlargementAuthorityProjectedPixelHeight),
        };
    }

    public static StepRow ToRow(SessionId sessionId, SessionStep step) => new()
    {
        SessionId = sessionId.ToString(),
        StepKind = ToText(step.Step),
        Ordinal = step.Ordinal,
        State = ToText(step.State),
        CurrentRevisionId = step.CurrentRevisionId?.ToString(),
        CurrentRevisionSha = step.CurrentRevisionSha256?.Value,
        SkipReason = step.SkipReason,
        AttemptCount = step.AttemptCount,
        EnteredStateAtUtc = ToText(step.EnteredStateAtUtc),
    };

    public static SessionStep ToDomain(StepRow row) => new(
        ToStepKind(row.StepKind),
        row.Ordinal,
        ToStepState(row.State),
        row.CurrentRevisionId is string id ? RevisionId.From(Guid.Parse(id)) : null,
        row.CurrentRevisionSha is string sha ? Sha256.Parse(sha) : null,
        row.SkipReason,
        row.AttemptCount,
        ToDateTimeOffset(row.EnteredStateAtUtc));

    public static SnapshotRow ToRow(InputSnapshot snapshot) => new()
    {
        Id = snapshot.Id.ToString(),
        SessionId = snapshot.SessionId.ToString(),
        RootRevisionId = snapshot.RootRevisionId.ToString(),
        OriginalSourcePath = snapshot.OriginalSourcePath,
        OriginalFileName = snapshot.OriginalFileName,
        ImportedAtUtc = ToText(snapshot.ImportedAtUtc),
    };

    public static InputSnapshot ToDomain(SnapshotRow row) => new(
        SnapshotId.From(Guid.Parse(row.Id)),
        SessionId.From(Guid.Parse(row.SessionId)),
        RevisionId.From(Guid.Parse(row.RootRevisionId)),
        row.OriginalSourcePath,
        row.OriginalFileName,
        ToDateTimeOffset(row.ImportedAtUtc));

    public static RevisionRow ToRow(Revision revision) => new()
    {
        Id = revision.Id.ToString(),
        SessionId = revision.SessionId.ToString(),
        SourceRevisionId = revision.SourceRevisionId?.ToString(),
        Operation = ToText(revision.Operation),
        RelativePath = revision.File.RelativePath,
        Format = ToText(revision.Facts.Format),
        ByteLength = revision.Facts.ByteLength,
        Sha256 = revision.Facts.Sha256.Value,
        PixelWidth = revision.Facts.PixelWidth,
        PixelHeight = revision.Facts.PixelHeight,
        DpiX = revision.Facts.DpiX,
        DpiY = revision.Facts.DpiY,
        ColourMode = ToText(revision.Facts.ColourMode),
        HasAlpha = revision.Facts.HasAlpha,
        CreatedAtUtc = ToText(revision.CreatedAtUtc),
        IsValid = revision.IsValid,
        InvalidatedAtUtc = ToTextOrNull(revision.InvalidatedAtUtc),
        InvalidationReason = revision.InvalidationReason is { } r ? ToText(r) : null,
        ReviewState = ToText(revision.ReviewState),
    };

    public static Revision ToDomain(RevisionRow row)
    {
        FileFacts facts = new(
            ToImageFormat(row.Format),
            row.ByteLength,
            Sha256.Parse(row.Sha256),
            row.PixelWidth,
            row.PixelHeight,
            row.DpiX,
            row.DpiY,
            ToColourMode(row.ColourMode),
            row.HasAlpha);

        WorkspaceArea area = InferArea(row.RelativePath);

        return new Revision(
            RevisionId.From(Guid.Parse(row.Id)),
            SessionId.From(Guid.Parse(row.SessionId)),
            row.SourceRevisionId is string src ? RevisionId.From(Guid.Parse(src)) : null,
            ToOperationKind(row.Operation),
            WorkspaceFileRef.Create(row.RelativePath, area),
            facts,
            ToDateTimeOffset(row.CreatedAtUtc),
            row.IsValid,
            ToDateTimeOffsetOrNull(row.InvalidatedAtUtc),
            row.InvalidationReason is string ir ? ToInvalidationReason(ir) : null,
            ToReviewState(row.ReviewState))
        {
            FormerWorkingFile = row.FormerWorkingPath is { } former
                ? WorkspaceFileRef.Create(former, WorkspaceArea.Working) : null,
            RetentionReleasedAtUtc = ToDateTimeOffsetOrNull(row.RetentionReleasedAtUtc),
        };
    }

    /// <summary>
    /// A <see cref="Revision"/> row does not persist <see cref="WorkspaceArea"/> separately —
    /// the layout itself encodes it, exactly as <see cref="Infrastructure.Workspace.FileWorkspace"/>
    /// lays sessions out (<c>Source/</c>, <c>Working/</c>, <c>Approved/</c>, <c>Rejected/</c>).
    /// </summary>
    private static WorkspaceArea InferArea(string relativePath)
    {
        string[] segments = relativePath.Split('/');
        foreach (string segment in segments)
        {
            switch (segment)
            {
                case "Source": return WorkspaceArea.Source;
                case "Working": return WorkspaceArea.Working;
                case "Approved": return WorkspaceArea.Approved;
                case "Rejected": return WorkspaceArea.Rejected;
                case "Logs": return WorkspaceArea.Logs;
                case "Revisions": return WorkspaceArea.Revisions;
            }
        }

        return WorkspaceArea.Working;
    }

    public static AttemptRow ToRow(ProcessingAttempt attempt) => new()
    {
        Id = attempt.Id.ToString(),
        SessionId = attempt.SessionId.ToString(),
        StepKind = ToText(attempt.Step),
        InputRevisionId = attempt.InputRevisionId?.ToString(),
        Operation = ToText(attempt.Operation),
        AdapterId = attempt.AdapterId,
        StartedAtUtc = ToText(attempt.StartedAtUtc),
        EndedAtUtc = ToTextOrNull(attempt.EndedAtUtc),
        ResultStatus = ToText(attempt.Status),
        OutputRevisionId = attempt.OutputRevisionId?.ToString(),
        FailureCode = attempt.Failure?.Code.ToString(),
        FailureDetailJson = attempt.Failure is { } f
            ? System.Text.Json.JsonSerializer.Serialize(new
            {
                f.Code,
                f.MessageKey,
                f.TechnicalDetail,
                f.IsRetryable,
                Context = f.Context,
            })
            : null,
        RetryOfAttemptId = attempt.RetryOfAttemptId?.ToString(),
        RetrySequence = attempt.RetrySequence,

        // Null for anything that is not a deterministic trim, which is the honest reading:
        // "this attempt had no trim margin", never "it used the default" (Part C3 §14, §17).
        TrimMode = attempt.TrimParameters is { } margin ? ToText(margin.Mode) : null,
        TrimMarginTop = attempt.TrimParameters?.Top,
        TrimMarginRight = attempt.TrimParameters?.Right,
        TrimMarginBottom = attempt.TrimParameters?.Bottom,
        TrimMarginLeft = attempt.TrimParameters?.Left,

        // Null for anything that was not an authorised background removal, which is the honest
        // reading: "this attempt had no reviewed-content authority", never "it used the default"
        // (Epic 11300 Part C2B1 §11).
        BackgroundRemovalDecision = attempt.BackgroundRemovalAuthority is { } bra ? ToText(bra.Decision) : null,
        BackgroundRemovalRevisionId = attempt.BackgroundRemovalAuthority?.ReviewedRevisionId.ToString(),
        BackgroundRemovalReviewedSha = attempt.BackgroundRemovalAuthority?.ReviewedSha256.Value,

        // The rectangles this attempt's trim established, in the source image's own pixel
        // coordinates and in TrimBounds's half-open spelling — stored as the four edges each
        // rectangle already has, with no conversion (SCRUM-11081). All eight are null together
        // for anything that is not a produced automatic trim, which the 0009 trigger enforces
        // as well as this mapper does.
        TrimContentLeft = attempt.TrimGeometry?.ContentBounds.Left,
        TrimContentTop = attempt.TrimGeometry?.ContentBounds.Top,
        TrimContentRight = attempt.TrimGeometry?.ContentBounds.RightExclusive,
        TrimContentBottom = attempt.TrimGeometry?.ContentBounds.BottomExclusive,
        TrimAppliedLeft = attempt.TrimGeometry?.AppliedBounds.Left,
        TrimAppliedTop = attempt.TrimGeometry?.AppliedBounds.Top,
        TrimAppliedRight = attempt.TrimGeometry?.AppliedBounds.RightExclusive,
        TrimAppliedBottom = attempt.TrimGeometry?.AppliedBounds.BottomExclusive,
        ManualSelectedLeft = attempt.ManualCropGeometry?.SelectedBounds.Left,
        ManualSelectedTop = attempt.ManualCropGeometry?.SelectedBounds.Top,
        ManualSelectedRight = attempt.ManualCropGeometry?.SelectedBounds.RightExclusive,
        ManualSelectedBottom = attempt.ManualCropGeometry?.SelectedBounds.BottomExclusive,
        ManualAppliedLeft = attempt.ManualCropGeometry?.AppliedBounds.Left,
        ManualAppliedTop = attempt.ManualCropGeometry?.AppliedBounds.Top,
        ManualAppliedRight = attempt.ManualCropGeometry?.AppliedBounds.RightExclusive,
        ManualAppliedBottom = attempt.ManualCropGeometry?.AppliedBounds.BottomExclusive,
        ManualMarginTop = attempt.ManualCropGeometry?.Margin.Top,
        ManualMarginRight = attempt.ManualCropGeometry?.Margin.Right,
        ManualMarginBottom = attempt.ManualCropGeometry?.Margin.Bottom,
        ManualMarginLeft = attempt.ManualCropGeometry?.Margin.Left,
        ManualMarginMode = attempt.ManualCropGeometry is { } manual ? ToText(manual.Margin.Mode) : null,

        // What THIS attempt ran under. Written once with the opening transaction and left out of
        // the upsert's DO UPDATE clause, so a later change of size — or a later enlargement
        // decision — cannot relabel it (Epic 11400 Part B1A.2A §12; Part B1A.2D §24).
        //
        // Exactly one of the two plan groups is written, whichever contract the run was made
        // under. The maximum-bound group is populated only by a FitWithinBoundsPreparation and the
        // target-edge group only by a TargetEdgePreparation, so an audit row always has one answer
        // to "what produced this file".
        PrintPlanSourceRevisionId = BoundsOf(attempt)?.SourceRevisionId.ToString(),
        PrintPlanSourceSha256 = BoundsOf(attempt)?.SourceSha256.Value,
        PrintPlanSourcePixelWidth = BoundsOf(attempt)?.SourcePixelWidth,
        PrintPlanSourcePixelHeight = BoundsOf(attempt)?.SourcePixelHeight,
        PrintPlanMaxWidthMm = BoundsOf(attempt)?.MaxWidthMm,
        PrintPlanMaxHeightMm = BoundsOf(attempt)?.MaxHeightMm,
        PrintPlanLimitKind = BoundsOf(attempt) is { } ap ? ToText(ap.LimitKind) : null,
        PrintPlanMode = BoundsOf(attempt) is { } am ? ToText(am.Mode) : null,
        PrintPlanLimitingEdge = BoundsOf(attempt) is { } ae ? ToText(ae.LimitingEdge) : null,
        PrintPlanLimitingValueMm = BoundsOf(attempt)?.LimitingValueMm,
        PrintPlanProjectedPixelWidth = BoundsOf(attempt)?.ProjectedPixelWidth,
        PrintPlanProjectedPixelHeight = BoundsOf(attempt)?.ProjectedPixelHeight,
        PrintPlanProductionDpi = BoundsOf(attempt)?.ProductionDpi,
        PrintPlanResizePolicy = BoundsOf(attempt) is { } ar ? ToText(ar.ResizePolicy) : null,

        SizingMode = SelectionOf(attempt) is { } ts ? ToText(ts.Mode) : null,
        SizingPreset = SelectionOf(attempt)?.Recommendation is { } tc
            ? ToText(tc.Preset)
            : null,
        SizingRecommendationKind = SelectionOf(attempt)?.Recommendation is { } tk
            ? ToText(tk.Kind)
            : null,
        SizingRecommendationMaxWidthMm = SelectionOf(attempt)?.Recommendation is { } tw
            ? ToMillimetreText(tw.MaxWidthMm)
            : null,
        SizingRecommendationMaxHeightMm = SelectionOf(attempt)?.Recommendation is { } th
            ? ToMillimetreText(th.MaxHeightMm)
            : null,
        SizingPresetOverridden = SelectionOf(attempt)?.PresetOverridden,
        SizingTargetEdge = TargetOf(attempt) is { } te
            ? ToText(te.Plan.Projection.SelectedTargetEdge)
            : null,
        SizingRequestedMm = TargetOf(attempt) is { } tr
            ? ToMillimetreText(tr.Plan.Projection.RequestedMillimetres)
            : null,

        TargetPlanSourceRevisionId = TargetOf(attempt)?.Plan.SourceRevisionId.ToString(),
        TargetPlanSourceSha256 = TargetOf(attempt)?.Plan.SourceSha256.Value,
        TargetPlanSourcePixelWidth = TargetOf(attempt)?.Plan.SourcePixelWidth,
        TargetPlanSourcePixelHeight = TargetOf(attempt)?.Plan.SourcePixelHeight,
        TargetPlanPhotoshopEdge = TargetOf(attempt) is { } tp
            ? ToText(tp.Plan.Projection.PhotoshopTargetEdge)
            : null,
        TargetPlanProjectedPixelWidth = TargetOf(attempt)?.Plan.Projection.ProjectedPixelWidth,
        TargetPlanProjectedPixelHeight = TargetOf(attempt)?.Plan.Projection.ProjectedPixelHeight,
        TargetPlanScaleNumerator = TargetOf(attempt)?.Plan.Projection.ProjectedScale.Numerator,
        TargetPlanScaleDenominator = TargetOf(attempt)?.Plan.Projection.ProjectedScale.Denominator,
        TargetPlanProductionDpi = TargetOf(attempt)?.Plan.ProductionDpi,
        TargetPlanDirection = TargetOf(attempt) is { } td
            ? ToText(td.Plan.Projection.Direction)
            : null,
        TargetPlanResizePolicy = TargetOf(attempt) is { } tz
            ? ToText(tz.Plan.Projection.ResizePolicy)
            : null,

        // The exact permission this run went ahead under, when it needed one. Written here rather
        // than left on the session, so a later change of mind cannot make an authorised run read
        // as unauthorised, or an unauthorised one as permitted (§24).
        EnlargementAuthoritySourceRevisionId =
            TargetOf(attempt)?.Authority?.SourceRevisionId.ToString(),
        EnlargementAuthoritySourceSha256 = TargetOf(attempt)?.Authority?.SourceSha256.Value,
        EnlargementAuthoritySizingMode = TargetOf(attempt)?.Authority is { } aam
            ? ToText(aam.SizingMode)
            : null,
        EnlargementAuthorityTargetEdge = TargetOf(attempt)?.Authority is { } aae
            ? ToText(aae.SelectedTargetEdge)
            : null,
        EnlargementAuthorityRequestedMm = TargetOf(attempt)?.Authority is { } aar
            ? ToMillimetreText(aar.RequestedMillimetres)
            : null,
        EnlargementAuthorityScaleNumerator =
            TargetOf(attempt)?.Authority?.ProjectedScale.Numerator,
        EnlargementAuthorityScaleDenominator =
            TargetOf(attempt)?.Authority?.ProjectedScale.Denominator,
        EnlargementAuthorityProjectedPixelWidth =
            TargetOf(attempt)?.Authority?.ProjectedTargetPixelWidth,
        EnlargementAuthorityProjectedPixelHeight =
            TargetOf(attempt)?.Authority?.ProjectedTargetPixelHeight,

        AdapterNotes = attempt.AdapterNotes,
        ManualResultSourcePath = attempt.ManualResultSourcePath,
    };

    private static PrintPreparationPlan? BoundsOf(ProcessingAttempt attempt) =>
        (attempt.Preparation as FitWithinBoundsPreparation)?.Plan;

    private static TargetEdgePreparation? TargetOf(ProcessingAttempt attempt) =>
        attempt.Preparation as TargetEdgePreparation;

    /// <summary>
    /// The flexible-size selection the run recorded, from whichever preparation form it took
    /// (post-final A5 correction §18).
    /// </summary>
    /// <remarks>
    /// The <c>Sizing*</c> columns are one group describing one decision, so they are written from
    /// one place regardless of which plan group sits beside them. Before this correction they were
    /// written only for a target-edge run, which left an ordinary preset fit's audit row silent
    /// about the recommendation it ran under — readable only by inferring a kind from the stored
    /// bounds, which a maximum short edge makes impossible (§19).
    /// </remarks>
    private static FlexibleSizeSelection? SelectionOf(ProcessingAttempt attempt) =>
        attempt.Preparation switch
        {
            TargetEdgePreparation target => target.Plan.Selection,
            FitWithinBoundsPreparation bounds => bounds.Selection,
            _ => null,
        };

    public static ProcessingAttempt ToDomain(AttemptRow row)
    {
        Domain.Results.OperationFailure? failure = null;
        if (row.FailureCode is not null)
        {
            Domain.Results.FailureCode code = Enum.Parse<Domain.Results.FailureCode>(row.FailureCode);
            failure = ReadFailure(code, row.FailureDetailJson);
        }

        // The attempt's own copy of the selection, self-contained exactly as its plan is. An audit
        // row that had to be joined back to the session to say what was requested would be an
        // audit row the session could still change out from under (Part B1A.2D §24).
        FlexibleSizeSelection? attemptSelection = ToFlexibleSizeSelection(
            row.SizingMode, row.SizingPreset, row.SizingRecommendationKind,
            row.SizingRecommendationMaxWidthMm, row.SizingRecommendationMaxHeightMm,
            row.SizingPresetOverridden, row.SizingTargetEdge, row.SizingRequestedMm);

        return new ProcessingAttempt(
            AttemptId.From(Guid.Parse(row.Id)),
            SessionId.From(Guid.Parse(row.SessionId)),
            ToStepKind(row.StepKind),
            row.InputRevisionId is string ir ? RevisionId.From(Guid.Parse(ir)) : null,
            ToOperationKind(row.Operation),
            row.AdapterId,
            ToDateTimeOffset(row.StartedAtUtc),
            ToDateTimeOffsetOrNull(row.EndedAtUtc),
            ToAttemptStatus(row.ResultStatus),
            row.OutputRevisionId is string orid ? RevisionId.From(Guid.Parse(orid)) : null,
            failure,
            row.RetryOfAttemptId is string roa ? AttemptId.From(Guid.Parse(roa)) : null,
            row.RetrySequence)
        {
            TrimParameters = ToTrimMargin(
                row.TrimMode, row.TrimMarginTop, row.TrimMarginRight,
                row.TrimMarginBottom, row.TrimMarginLeft),
            BackgroundRemovalAuthority = ToBackgroundRemovalAuthority(
                row.BackgroundRemovalDecision, row.BackgroundRemovalRevisionId,
                row.BackgroundRemovalReviewedSha),
            AdapterNotes = row.AdapterNotes,
            ManualResultSourcePath = row.ManualResultSourcePath,

            // Null on every attempt that established no trim geometry, and on every attempt
            // written before migration 0009 — which is exactly what those rows were: attempts
            // whose crop rectangle was never recorded, never attempts that kept the whole
            // canvas. Nothing here reconstructs a rectangle from the output's dimensions
            // (SCRUM-11081).
            ManualCropGeometry = ToManualCropGeometry(row),
            TrimGeometry = ToTrimGeometry(
                row.TrimContentLeft, row.TrimContentTop, row.TrimContentRight, row.TrimContentBottom,
                row.TrimAppliedLeft, row.TrimAppliedTop, row.TrimAppliedRight, row.TrimAppliedBottom),

            // Null on every attempt that was not a Photoshop output, and on every attempt written
            // before migration 0005 — which is exactly what those rows were: attempts that had no
            // preparation, never attempts that used a default (Epic 11400 Part B1A.2A §12).
            //
            // Which contract the run was made under is stated by which column group is populated,
            // and a row holding both is refused. A target-edge enlargement is rebuilt with its
            // authority through TargetEdgePreparation, which refuses one that does not match — so
            // a row claiming an unauthorised enlargement fails to load rather than loading as
            // permitted (Part B1A.2D §24).
            Preparation = ToPhotoshopPreparation(
                ToPrintPreparationPlan(
                    row.PrintPlanSourceRevisionId, row.PrintPlanSourceSha256,
                    row.PrintPlanSourcePixelWidth, row.PrintPlanSourcePixelHeight,
                    row.PrintPlanMaxWidthMm, row.PrintPlanMaxHeightMm,
                    row.PrintPlanLimitKind, row.PrintPlanMode,
                    row.PrintPlanLimitingEdge, row.PrintPlanLimitingValueMm,
                    row.PrintPlanProjectedPixelWidth, row.PrintPlanProjectedPixelHeight,
                    row.PrintPlanProductionDpi, row.PrintPlanResizePolicy),
                ToTargetEdgePlan(
                    attemptSelection,
                    row.TargetPlanSourceRevisionId, row.TargetPlanSourceSha256,
                    row.TargetPlanSourcePixelWidth, row.TargetPlanSourcePixelHeight,
                    row.TargetPlanPhotoshopEdge,
                    row.TargetPlanProjectedPixelWidth, row.TargetPlanProjectedPixelHeight,
                    row.TargetPlanScaleNumerator, row.TargetPlanScaleDenominator,
                    row.TargetPlanProductionDpi, row.TargetPlanDirection,
                    row.TargetPlanResizePolicy),
                ToEnlargementAuthority(
                    row.EnlargementAuthoritySourceRevisionId, row.EnlargementAuthoritySourceSha256,
                    row.EnlargementAuthoritySizingMode, row.EnlargementAuthorityTargetEdge,
                    row.EnlargementAuthorityRequestedMm,
                    row.EnlargementAuthorityScaleNumerator,
                    row.EnlargementAuthorityScaleDenominator,
                    row.EnlargementAuthorityProjectedPixelWidth,
                    row.EnlargementAuthorityProjectedPixelHeight),
                attemptSelection),
        };
    }

    private static Domain.Results.OperationFailure ReadFailure(
        Domain.Results.FailureCode code, string? detailJson)
    {
        if (string.IsNullOrWhiteSpace(detailJson))
        {
            return Domain.Results.OperationFailure.Create(code, code.ToString());
        }

        try
        {
            using System.Text.Json.JsonDocument document =
                System.Text.Json.JsonDocument.Parse(detailJson);
            System.Text.Json.JsonElement root = document.RootElement;

            string messageKey = root.TryGetProperty("MessageKey", out System.Text.Json.JsonElement key)
                ? key.GetString() ?? $"Failure_{code}"
                : $"Failure_{code}";
            string technicalDetail = root.TryGetProperty(
                    "TechnicalDetail", out System.Text.Json.JsonElement detail)
                ? detail.GetString() ?? code.ToString()
                : code.ToString();
            bool retryable = root.TryGetProperty(
                    "IsRetryable", out System.Text.Json.JsonElement retry) &&
                retry.ValueKind is System.Text.Json.JsonValueKind.True;

            Dictionary<string, string> context = new(StringComparer.Ordinal);
            if (root.TryGetProperty("Context", out System.Text.Json.JsonElement values) &&
                values.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (System.Text.Json.JsonProperty property in values.EnumerateObject())
                {
                    context[property.Name] = property.Value.GetString() ?? property.Value.ToString();
                }
            }

            return Domain.Results.OperationFailure.Create(
                code, technicalDetail, retryable, context.Count == 0 ? null : context, messageKey);
        }
        catch (System.Text.Json.JsonException)
        {
            // Rows written by the earliest builds stored plain detail text. Preserve it rather
            // than making a legacy audit unreadable merely because it predates structured JSON.
            return Domain.Results.OperationFailure.Create(code, detailJson);
        }
    }

    public static ReviewRow ToRow(ReviewDecision review) => new()
    {
        Id = review.Id.ToString(),
        SessionId = review.SessionId.ToString(),
        StepKind = ToText(review.Step),
        SubjectKind = ToText(review.SubjectKind),
        SubjectId = review.SubjectId.ToString(),
        ReviewedSha256 = review.ReviewedSha256.Value,
        Operator = review.Operator,
        DecidedAtUtc = ToText(review.DecidedAtUtc),
        Decision = review.IsApproved ? "APPROVED" : "REJECTED",
        QuickReason = review.QuickReason?.ToString(),
        Notes = review.Notes,
    };

    public static ReviewDecision ToDomain(ReviewRow row) => new(
        ReviewId.From(Guid.Parse(row.Id)),
        SessionId.From(Guid.Parse(row.SessionId)),
        ToStepKind(row.StepKind),
        ToReviewSubjectKind(row.SubjectKind),
        Guid.Parse(row.SubjectId),
        Sha256.Parse(row.ReviewedSha256),
        row.Operator,
        ToDateTimeOffset(row.DecidedAtUtc),
        row.Decision == "APPROVED",
        row.QuickReason is string qr ? Enum.Parse<RejectionReason>(qr) : null,
        row.Notes);

    public static OutputRow ToRow(PrintOutput output) => new()
    {
        Id = output.Id.ToString(),
        SessionId = output.SessionId.ToString(),
        SourceRevisionId = output.SourceRevisionId.ToString(),
        TargetWidthMm = output.Dimensions.WidthMm,
        TargetHeightMm = output.Dimensions.HeightMm,
        PixelWidth = output.Dimensions.PixelWidth,
        PixelHeight = output.Dimensions.PixelHeight,
        Dpi = output.Dimensions.Dpi,
        SizePresetId = ToText(output.Dimensions.Preset),
        WhiteUnderbaseBranch = ToText(output.Branch),
        ProductionPresetId = output.Preset.PresetId,
        ProductionPresetSha256 = output.Preset.ManifestSha256.Value,
        RelativePath = output.File.RelativePath,
        ByteLength = output.ByteLength,
        Sha256 = output.Sha256.Value,
        ReviewState = ToText(output.ReviewState),
        IsValid = output.IsValid,
        InvalidationReason = output.InvalidationReason is { } r ? ToText(r) : null,
        RecycledAtUtc = ToTextOrNull(output.RecycledAtUtc),
        PromotionReservedPath = output.PromotionReservation?.RelativePath,
        CreatedAtUtc = ToText(output.CreatedAtUtc),
    };

    public static PrintOutput ToDomain(OutputRow row)
    {
        PrintDimensions dimensions = PrintDimensions.FromMillimetres(
            row.TargetWidthMm, row.TargetHeightMm, ToSizePreset(row.SizePresetId));

        return new PrintOutput(
            PrintOutputId.From(Guid.Parse(row.Id)),
            SessionId.From(Guid.Parse(row.SessionId)),
            RevisionId.From(Guid.Parse(row.SourceRevisionId)),
            dimensions,
            ToWhiteUnderbaseBranch(row.WhiteUnderbaseBranch),
            new ProductionPresetRef(row.ProductionPresetId, "unknown", Sha256.Parse(row.ProductionPresetSha256)),
            // Inferred from the layout, exactly as a Revision's area is, and never assumed to be
            // Approved. A production TIFF is produced into the attempt's own Working directory and
            // moves to Approved only when an operator approves it, so a hardcoded area would have
            // reported every unreviewed TIFF as already approved (Epic 11400 Part C2B §4, §24).
            WorkspaceFileRef.Create(row.RelativePath, InferArea(row.RelativePath)),
            row.ByteLength,
            Sha256.Parse(row.Sha256),
            ToDateTimeOffset(row.CreatedAtUtc),
            ToReviewState(row.ReviewState),
            row.IsValid,
            row.InvalidationReason is string ir ? ToInvalidationReason(ir) : null,
            ToDateTimeOffsetOrNull(row.RecycledAtUtc),
            row.PromotionReservedPath is string reserved
                ? WorkspaceFileRef.Create(reserved, InferArea(reserved))
                : null);
    }

    // ------------------------------------------------------------------------------------
    // AutomationLogEntry (Jira 11108; MVP design §17.6)
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// Flattens one structured automation error into its row.
    /// </summary>
    /// <remarks>
    /// The failure's structured context is written as a JSON object under <c>ContextJson</c>,
    /// using the same shape <c>ProcessingAttempt.FailureDetailJson</c> already uses for the
    /// context half — one encoding of a context map on disk, not two. An empty context is
    /// written as NULL rather than <c>{}</c>, so "this error carried no context" reads the same
    /// way everywhere else a nullable column does.
    /// </remarks>
    public static AutomationLogRow ToRow(PrintFlow.Domain.Automation.AutomationLogEntry entry) => new()
    {
        Id = entry.Id.ToString(),
        SessionId = entry.SessionId?.ToString(),
        StepKind = entry.Step is { } step ? ToText(step) : null,
        AtUtc = ToText(entry.AtUtc),
        FailureCode = entry.Failure.Code.ToString(),
        MessageKey = entry.Failure.MessageKey,
        TechnicalDetail = entry.Failure.TechnicalDetail,
        ContextJson = entry.Failure.Context.Count == 0
            ? null
            : System.Text.Json.JsonSerializer.Serialize(entry.Failure.Context),
        ScreenshotPath = entry.ScreenshotPath,
    };

    public static PrintFlow.Domain.Automation.AutomationLogEntry ToDomain(AutomationLogRow row)
    {
        Dictionary<string, string> context = new(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(row.ContextJson))
        {
            using System.Text.Json.JsonDocument document =
                System.Text.Json.JsonDocument.Parse(row.ContextJson);
            if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                foreach (System.Text.Json.JsonProperty property in document.RootElement.EnumerateObject())
                {
                    context[property.Name] = property.Value.GetString() ?? property.Value.ToString();
                }
            }
        }

        // IsRetryable is deliberately not persisted here: it is a property of the operation the
        // attempt row already records, not of the historical error, and a stored copy would be a
        // second answer to "may the operator retry?" that could disagree with the first.
        Domain.Results.OperationFailure failure = Domain.Results.OperationFailure.Create(
            Enum.Parse<Domain.Results.FailureCode>(row.FailureCode),
            row.TechnicalDetail,
            isRetryable: false,
            context: context.Count == 0 ? null : context,
            messageKey: row.MessageKey);

        return new PrintFlow.Domain.Automation.AutomationLogEntry(
            AutomationLogId.From(Guid.Parse(row.Id)),
            row.SessionId is string sid ? SessionId.From(Guid.Parse(sid)) : null,
            row.StepKind is string step ? ToStepKind(step) : null,
            ToDateTimeOffset(row.AtUtc),
            failure,
            row.ScreenshotPath);
    }

    // ------------------------------------------------------------------------------------
    // Setting (Jira 11108; MVP design §17.6)
    // ------------------------------------------------------------------------------------

    /// <summary>
    /// The stable on-disk text of a <see cref="Domain.Settings.SettingKey"/>.
    /// </summary>
    /// <remarks>
    /// The enum name, exactly as <see cref="StepKind"/> is stored: the <c>Setting</c> table
    /// carries no CHECK constraint to keep a translation table in sync with, and one convention
    /// beats two. A member may be added but never renamed — a renamed key is an orphaned row.
    /// </remarks>
    public static string ToText(Domain.Settings.SettingKey value) => value.ToString();

    /// <summary>Reads a stored key, or null when the row predates or postdates this build's vocabulary.</summary>
    /// <remarks>
    /// Null rather than a throw: an unknown key is a row this build has no business interpreting,
    /// and refusing to read the whole settings table because one row is unrecognised would make a
    /// downgrade unrecoverable.
    /// </remarks>
    public static Domain.Settings.SettingKey? ToSettingKeyOrNull(string text) =>
        Enum.TryParse(text, ignoreCase: false, out Domain.Settings.SettingKey key) ? key : null;
}
