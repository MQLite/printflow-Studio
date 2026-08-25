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
        OperationKind.Import => "IMPORT",
        OperationKind.Enhance => "ENHANCE",
        OperationKind.RemoveBackground => "REMOVE_BACKGROUND",
        OperationKind.Trim => "TRIM",
        OperationKind.PromoteApproved => "PROMOTE_APPROVED",
        OperationKind.ManualImport => "MANUAL_IMPORT",
        OperationKind.PhotoshopOutput => "PHOTOSHOP_OUTPUT",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static OperationKind ToOperationKind(string text) => text switch
    {
        "IMPORT" => OperationKind.Import,
        "ENHANCE" => OperationKind.Enhance,
        "REMOVE_BACKGROUND" => OperationKind.RemoveBackground,
        "TRIM" => OperationKind.Trim,
        "PROMOTE_APPROVED" => OperationKind.PromoteApproved,
        "MANUAL_IMPORT" => OperationKind.ManualImport,
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

    public static string ToText(BackgroundRemovalDecision value) => value switch
    {
        BackgroundRemovalDecision.Unspecified => "UNSPECIFIED",
        BackgroundRemovalDecision.UseAutomaticSelectionForReviewedContent =>
            "USE_AUTOMATIC_SELECTION_FOR_REVIEWED_CONTENT",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static BackgroundRemovalDecision ToBackgroundRemovalDecision(string text) => text switch
    {
        "UNSPECIFIED" => BackgroundRemovalDecision.Unspecified,
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
    };

    public static ProcessingSession ToDomain(SessionRow row)
    {
        PrintDimensions? dimensions = row.DimensionsWidthMm is double width
            ? PrintDimensions.FromMillimetres(
                width, row.DimensionsHeightMm!.Value, ToSizePreset(row.DimensionsPreset!))
            : null;

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
            ToReviewState(row.ReviewState));
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
        AdapterNotes = attempt.AdapterNotes,
    };

    public static ProcessingAttempt ToDomain(AttemptRow row)
    {
        Domain.Results.OperationFailure? failure = null;
        if (row.FailureCode is not null)
        {
            Domain.Results.FailureCode code = Enum.Parse<Domain.Results.FailureCode>(row.FailureCode);
            failure = ReadFailure(code, row.FailureDetailJson);
        }

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
            WorkspaceFileRef.Create(row.RelativePath, WorkspaceArea.Approved),
            row.ByteLength,
            Sha256.Parse(row.Sha256),
            ToDateTimeOffset(row.CreatedAtUtc),
            ToReviewState(row.ReviewState),
            row.IsValid,
            row.InvalidationReason is string ir ? ToInvalidationReason(ir) : null,
            ToDateTimeOffsetOrNull(row.RecycledAtUtc));
    }
}
