-- Epic 11400 Part B1A.2D §5, §6, §7, §8, §9, §20, §21, §22, §24: make the flexible-size decision
-- durable, keep the operator's exact millimetres exact, and record permission to enlarge as the
-- specific permission it is rather than as a flag.
--
-- Three things are being persisted that did not exist before, and they are deliberately three
-- rather than one:
--
--   the SELECTION  -- what the operator chose: preset fit or a custom target edge, which
--                     configured recommendation they were shown, and whether they overrode it.
--
--   the PLAN       -- what that choice projects to against one exact source: the concrete
--                     Photoshop edge, the projected pixels, the reduced scale, the direction and
--                     the neutral resize policy.
--
--   the AUTHORITY  -- explicit permission to enlarge one exact source to one exact target.
--
-- Collapsing any two of them would lose a distinction the accepted contract turns on. A selection
-- without its plan cannot say which edge Photoshop is given; a plan without its selection cannot
-- say whether 320 mm was an override of A4's 280 mm or an ordinary custom size; and an authority
-- folded into either would become "enlargement is on for this session", which is exactly what §9
-- exists to prevent.
--
-- Split across the two tables the same way everything else in this schema is:
--
--   ProcessingSession -> "what would the NEXT Photoshop output do". Replaced whenever the
--                        operator records a different size, and cleared with the dimensions on
--                        ReturnToStep and AddAnotherSize (§31, §32).
--
--   ProcessingAttempt -> "what did THIS output run under". Written once with the attempt's
--                        opening transaction, before the working copy and before Photoshop is
--                        touched, and never updated afterwards -- the attempt upsert leaves these
--                        columns out of its DO UPDATE clause, so a later size change or a later
--                        enlargement decision cannot relabel an earlier attempt (§24).
--
-- Nothing here backfills meaning into old rows (§21). Every new column is NULL for every existing
-- row, and NULL means "this session made no flexible-size decision" -- never "PresetFit was
-- assumed", never "enlargement was allowed". A valid MaxBoundsV1 session stays a MaxBoundsV1
-- session under its original accepted semantics; it does not become a TargetEdgeV1 session
-- because v1.11.0 is now configured (§18, §21).

------------------------------------------------------------------------------------------------
-- 1. Widening the semantics vocabulary, which SQLite can only do by rebuilding the table.
------------------------------------------------------------------------------------------------
--
-- 0005 added DimensionSemantics with CHECK (... IN ('LEGACY_EXACT_PAIR', 'MAX_BOUNDS_V1')), and a
-- CHECK constraint cannot be altered or dropped in SQLite. TARGET_EDGE_V1 is a third reading of
-- the same field, so the choice was between rebuilding the table and adding a second semantics
-- column beside the first.
--
-- A second column was rejected. Two columns answering "what does this size mean" is two sources
-- of truth for one fact, and the old one would go stale the moment a session recorded a target
-- edge -- leaving a schema in which the honest answer and the readable answer are different
-- columns. The rebuild keeps one field with one meaning, and the meaning it had is preserved
-- exactly: LEGACY_EXACT_PAIR and MAX_BOUNDS_V1 rows copy across unchanged and untouched (§20).
--
-- Every column, type, default and CHECK below is reproduced verbatim from 0001, 0002, 0003 and
-- 0005. The only differences are the widened DimensionSemantics CHECK and the flexible-size
-- columns appended at the end. Nothing is renamed, retyped, dropped or given a new default.
--
-- MigrationRunner suspends foreign keys for the migration pass, which is what makes the DROP
-- below safe: with enforcement on it would fire the ON DELETE CASCADE rules pointing at this
-- table and take the sessions' steps, revisions, attempts and outputs with it.

CREATE TABLE ProcessingSession_rebuilt (
    Id                     TEXT PRIMARY KEY NOT NULL,
    WorkflowType           TEXT NOT NULL CHECK (WorkflowType IN
                               ('PREPARE_ASSET', 'PREPARE_CUSTOMER_DESIGN', 'GENERATE_PRINT_TIFF')),
    OutputName             TEXT NOT NULL,
    CurrentStep            TEXT NOT NULL,
    State                  TEXT NOT NULL CHECK (State IN ('ACTIVE', 'HANDED_OFF', 'COMPLETED', 'ABANDONED')),
    WorkspacePath          TEXT NOT NULL UNIQUE,
    CreatedAtUtc           TEXT NOT NULL,
    UpdatedAtUtc           TEXT NOT NULL,
    CompletedAtUtc         TEXT NULL,
    HandedOffAtUtc         TEXT NULL,
    HandOffReason          TEXT NULL,
    AbandonedAtUtc         TEXT NULL,
    AbandonReason          TEXT NULL,
    DimensionsWidthMm      REAL NULL,
    DimensionsHeightMm     REAL NULL,
    DimensionsPixelWidth   INTEGER NULL,
    DimensionsPixelHeight  INTEGER NULL,
    DimensionsPreset       TEXT NULL,
    WhiteUnderbaseBranch   TEXT NULL CHECK (WhiteUnderbaseBranch IS NULL OR WhiteUnderbaseBranch IN
                               ('W1_0PX', 'W1_1PX', 'W1_2PX')),

    -- 0002
    TrimMode         TEXT    NULL CHECK (TrimMode IS NULL OR TrimMode IN
                         ('TIGHT_CROP', 'UNIFORM_MARGIN', 'EDGE_SPECIFIC_MARGIN')),
    TrimMarginTop    INTEGER NULL CHECK (TrimMarginTop    IS NULL OR TrimMarginTop    >= 0),
    TrimMarginRight  INTEGER NULL CHECK (TrimMarginRight  IS NULL OR TrimMarginRight  >= 0),
    TrimMarginBottom INTEGER NULL CHECK (TrimMarginBottom IS NULL OR TrimMarginBottom >= 0),
    TrimMarginLeft   INTEGER NULL CHECK (TrimMarginLeft   IS NULL OR TrimMarginLeft   >= 0),

    -- 0003
    BackgroundRemovalDecision    TEXT NULL
        CHECK (BackgroundRemovalDecision IS NULL OR BackgroundRemovalDecision IN
            ('UNSPECIFIED', 'USE_AUTOMATIC_SELECTION_FOR_REVIEWED_CONTENT')),
    BackgroundRemovalRevisionId  TEXT NULL,
    BackgroundRemovalReviewedSha TEXT NULL
        CHECK (BackgroundRemovalReviewedSha IS NULL OR length(BackgroundRemovalReviewedSha) = 64),

    -- 0005, with TARGET_EDGE_V1 added. There is still deliberately no member meaning "unknown".
    DimensionSemantics TEXT NULL
        CHECK (DimensionSemantics IS NULL OR DimensionSemantics IN
            ('LEGACY_EXACT_PAIR', 'MAX_BOUNDS_V1', 'TARGET_EDGE_V1')),

    PrintPlanSourceRevisionId     TEXT    NULL,
    PrintPlanSourceSha256         TEXT    NULL
        CHECK (PrintPlanSourceSha256 IS NULL OR length(PrintPlanSourceSha256) = 64),
    PrintPlanSourcePixelWidth     INTEGER NULL
        CHECK (PrintPlanSourcePixelWidth  IS NULL OR PrintPlanSourcePixelWidth  > 0),
    PrintPlanSourcePixelHeight    INTEGER NULL
        CHECK (PrintPlanSourcePixelHeight IS NULL OR PrintPlanSourcePixelHeight > 0),
    PrintPlanMaxWidthMm           REAL    NULL
        CHECK (PrintPlanMaxWidthMm  IS NULL OR PrintPlanMaxWidthMm  > 0),
    PrintPlanMaxHeightMm          REAL    NULL
        CHECK (PrintPlanMaxHeightMm IS NULL OR PrintPlanMaxHeightMm > 0),
    PrintPlanLimitKind            TEXT    NULL
        CHECK (PrintPlanLimitKind IS NULL OR PrintPlanLimitKind IN
            ('A3_LANDSCAPE', 'A3_PORTRAIT', 'A4', 'A5', 'CUSTOM')),
    PrintPlanMode                 TEXT    NULL
        CHECK (PrintPlanMode IS NULL OR PrintPlanMode IN ('RESOLUTION_ONLY', 'PROPORTIONAL_SHRINK')),
    PrintPlanLimitingEdge         TEXT    NULL
        CHECK (PrintPlanLimitingEdge IS NULL OR PrintPlanLimitingEdge IN ('NONE', 'WIDTH', 'HEIGHT')),
    PrintPlanLimitingValueMm      REAL    NULL
        CHECK (PrintPlanLimitingValueMm IS NULL OR PrintPlanLimitingValueMm > 0),
    PrintPlanProjectedPixelWidth  INTEGER NULL
        CHECK (PrintPlanProjectedPixelWidth  IS NULL OR PrintPlanProjectedPixelWidth  > 0),
    PrintPlanProjectedPixelHeight INTEGER NULL
        CHECK (PrintPlanProjectedPixelHeight IS NULL OR PrintPlanProjectedPixelHeight > 0),
    PrintPlanProductionDpi        INTEGER NULL
        CHECK (PrintPlanProductionDpi IS NULL OR PrintPlanProductionDpi = 300),
    PrintPlanResizePolicy         TEXT    NULL
        CHECK (
            (PrintPlanResizePolicy IS NULL OR PrintPlanResizePolicy IN ('NONE', 'BICUBIC_SHARPER'))
            AND (
                (PrintPlanSourceRevisionId     IS NULL AND PrintPlanSourceSha256         IS NULL
                 AND PrintPlanSourcePixelWidth IS NULL AND PrintPlanSourcePixelHeight    IS NULL
                 AND PrintPlanMaxWidthMm       IS NULL AND PrintPlanMaxHeightMm          IS NULL
                 AND PrintPlanLimitKind        IS NULL AND PrintPlanMode                 IS NULL
                 AND PrintPlanLimitingEdge     IS NULL AND PrintPlanProjectedPixelWidth  IS NULL
                 AND PrintPlanProjectedPixelHeight IS NULL AND PrintPlanProductionDpi    IS NULL
                 AND PrintPlanResizePolicy     IS NULL)
                OR
                (PrintPlanSourceRevisionId     IS NOT NULL AND PrintPlanSourceSha256         IS NOT NULL
                 AND PrintPlanSourcePixelWidth IS NOT NULL AND PrintPlanSourcePixelHeight    IS NOT NULL
                 AND PrintPlanMaxWidthMm       IS NOT NULL AND PrintPlanMaxHeightMm          IS NOT NULL
                 AND PrintPlanLimitKind        IS NOT NULL AND PrintPlanMode                 IS NOT NULL
                 AND PrintPlanLimitingEdge     IS NOT NULL AND PrintPlanProjectedPixelWidth  IS NOT NULL
                 AND PrintPlanProjectedPixelHeight IS NOT NULL AND PrintPlanProductionDpi    IS NOT NULL
                 AND PrintPlanResizePolicy     IS NOT NULL)
            )
        ),

    ----------------------------------------------------------------------------------------------
    -- The operator's flexible-size selection (§6).
    ----------------------------------------------------------------------------------------------
    --
    -- SizingMode is the group's anchor: NULL means no flexible-size decision was made, which is
    -- true of every historical row and of a session whose maximum bounds were typed rather than
    -- chosen from a named preset. A typed custom fit box is deliberately not retrofitted into this
    -- vocabulary -- it predates it, and inventing a mode for it would be inventing a decision.
    SizingMode TEXT NULL
        CHECK (SizingMode IS NULL OR SizingMode IN ('PRESET_FIT', 'CUSTOM_TARGET_EDGE')),

    -- The configured recommendation the operator was shown, kept in the form it was configured in.
    -- A box and a long edge are different statements about a size, and which one the shop wrote is
    -- part of the decision -- so the kind is stored rather than inferred from the two values being
    -- equal (§6).
    --
    -- The millimetres are TEXT, and every millimetre column in this migration is, for the reason
    -- §7 gives: REAL is a binary double, and the accepted target-edge calculation is exact. A
    -- requested 137.5 mm that came back as 137.49999999999999 would decide a midpoint case
    -- somewhere other than where the contract decides it, and would stop matching the enlargement
    -- authority granted for it. TEXT round-trips the operator's decimal exactly, and the exact
    -- arithmetic stays where it already lives -- there is no second implementation here (§7).
    SizingPreset TEXT NULL
        CHECK (SizingPreset IS NULL OR SizingPreset IN ('A3_LANDSCAPE', 'A3_PORTRAIT', 'A4', 'A5')),
    SizingRecommendationKind TEXT NULL
        CHECK (SizingRecommendationKind IS NULL OR SizingRecommendationKind IN
            ('MAXIMUM_BOX', 'MAXIMUM_LONG_EDGE')),
    SizingRecommendationMaxWidthMm  TEXT NULL
        CHECK (SizingRecommendationMaxWidthMm IS NULL OR CAST(SizingRecommendationMaxWidthMm AS REAL) > 0),
    SizingRecommendationMaxHeightMm TEXT NULL
        CHECK (SizingRecommendationMaxHeightMm IS NULL OR CAST(SizingRecommendationMaxHeightMm AS REAL) > 0),

    -- Whether the recommendation above was explicitly replaced. Stored beside the recommendation
    -- rather than instead of it: the original is never overwritten by the override, so "the
    -- operator went past A4's 280 mm" stays readable afterwards (§6).
    SizingPresetOverridden INTEGER NULL
        CHECK (SizingPresetOverridden IS NULL OR SizingPresetOverridden IN (0, 1)),

    SizingTargetEdge TEXT NULL
        CHECK (SizingTargetEdge IS NULL OR SizingTargetEdge IN ('WIDTH', 'HEIGHT', 'LONG_EDGE')),
    -- Text for exactness, but still a size: a stored zero, a negative, or a value that is not a
    -- number at all is refused here as well as by the Domain. CAST is a validity check on the
    -- stored text, never the arithmetic -- the exact calculation reads the decimal itself (§7, §22).
    SizingRequestedMm TEXT NULL
        CHECK (SizingRequestedMm IS NULL OR CAST(SizingRequestedMm AS REAL) > 0),

    ----------------------------------------------------------------------------------------------
    -- The TargetEdgeV1 plan (§8).
    ----------------------------------------------------------------------------------------------
    --
    -- Separate columns from PrintPlan* above, and that is the point of them. An existing
    -- maximum-bound plan is never re-read as a target-edge plan, and a session can hold one or the
    -- other but not both -- which the completeness CHECK at the end of the group enforces (§5).
    --
    -- The requested millimetres are not repeated here: they live once, on the selection above, and
    -- the plan is rehydrated with the selection it belongs to. Two copies of one number are two
    -- numbers that can disagree.
    --
    -- PresetLimitExceeded and SourceCapacityExceeded are likewise absent, and deliberately. Both
    -- are exact functions of what is stored -- the first of the requested millimetres against the
    -- recommendation, the second of the direction -- so persisting them would add a second place
    -- for the answer to live and a row that could contradict itself. They are reconstructed by the
    -- Domain, which is the only thing that decides them (§8, §23).
    TargetPlanSourceRevisionId TEXT NULL,
    TargetPlanSourceSha256     TEXT NULL
        CHECK (TargetPlanSourceSha256 IS NULL OR length(TargetPlanSourceSha256) = 64),
    TargetPlanSourcePixelWidth  INTEGER NULL
        CHECK (TargetPlanSourcePixelWidth  IS NULL OR TargetPlanSourcePixelWidth  > 0),
    TargetPlanSourcePixelHeight INTEGER NULL
        CHECK (TargetPlanSourcePixelHeight IS NULL OR TargetPlanSourcePixelHeight > 0),

    -- The concrete edge Photoshop would be given. NONE is not permitted: a target-edge plan always
    -- writes exactly one edge, and a LONG_EDGE request that has not been resolved against real
    -- source pixels is not executable (§8, §23).
    TargetPlanPhotoshopEdge TEXT NULL
        CHECK (TargetPlanPhotoshopEdge IS NULL OR TargetPlanPhotoshopEdge IN ('WIDTH', 'HEIGHT')),

    TargetPlanProjectedPixelWidth  INTEGER NULL
        CHECK (TargetPlanProjectedPixelWidth  IS NULL OR TargetPlanProjectedPixelWidth  > 0),
    TargetPlanProjectedPixelHeight INTEGER NULL
        CHECK (TargetPlanProjectedPixelHeight IS NULL OR TargetPlanProjectedPixelHeight > 0),

    -- The projected scale as the exact reduced integer ratio the contract compares, never as a
    -- percentage. A percentage has already lost the answer, and the enlargement authority is
    -- matched on this value exactly (§7, §9).
    TargetPlanScaleNumerator   INTEGER NULL
        CHECK (TargetPlanScaleNumerator   IS NULL OR TargetPlanScaleNumerator   > 0),
    TargetPlanScaleDenominator INTEGER NULL
        CHECK (TargetPlanScaleDenominator IS NULL OR TargetPlanScaleDenominator > 0),

    TargetPlanProductionDpi INTEGER NULL
        CHECK (TargetPlanProductionDpi IS NULL OR TargetPlanProductionDpi = 300),
    TargetPlanDirection TEXT NULL
        CHECK (TargetPlanDirection IS NULL OR TargetPlanDirection IN
            ('RESOLUTION_ONLY', 'SHRINK', 'ENLARGE')),

    -- The neutral policy, never a Photoshop COM value. The direction fixes it -- one policy per
    -- direction, by the accepted contract -- so a row pairing ENLARGE with BICUBIC_SHARPER, or
    -- SHRINK with PRESERVE_DETAILS, is refused here as well as by the mapper (§23).
    --
    -- This column also carries the group's completeness constraint, because a column CHECK in
    -- SQLite may reference the whole row. The selection and the plan are one all-or-nothing group
    -- together with SizingMode: a partial flexible row must not hydrate into a plan by defaulting
    -- what is missing (§22).
    TargetPlanResizePolicy TEXT NULL
        CHECK (
            (TargetPlanResizePolicy IS NULL OR TargetPlanResizePolicy IN
                ('NONE', 'BICUBIC_SHARPER', 'PRESERVE_DETAILS'))
            AND (TargetPlanDirection IS NULL OR TargetPlanResizePolicy IS
                CASE TargetPlanDirection
                    WHEN 'RESOLUTION_ONLY' THEN 'NONE'
                    WHEN 'SHRINK'          THEN 'BICUBIC_SHARPER'
                    ELSE                        'PRESERVE_DETAILS'
                END)
            AND (
                (TargetPlanSourceRevisionId     IS NULL AND TargetPlanSourceSha256         IS NULL
                 AND TargetPlanSourcePixelWidth IS NULL AND TargetPlanSourcePixelHeight    IS NULL
                 AND TargetPlanPhotoshopEdge    IS NULL AND TargetPlanProjectedPixelWidth  IS NULL
                 AND TargetPlanProjectedPixelHeight IS NULL AND TargetPlanScaleNumerator   IS NULL
                 AND TargetPlanScaleDenominator IS NULL AND TargetPlanProductionDpi        IS NULL
                 AND TargetPlanDirection        IS NULL AND TargetPlanResizePolicy         IS NULL)
                OR
                (TargetPlanSourceRevisionId     IS NOT NULL AND TargetPlanSourceSha256         IS NOT NULL
                 AND TargetPlanSourcePixelWidth IS NOT NULL AND TargetPlanSourcePixelHeight    IS NOT NULL
                 AND TargetPlanPhotoshopEdge    IS NOT NULL AND TargetPlanProjectedPixelWidth  IS NOT NULL
                 AND TargetPlanProjectedPixelHeight IS NOT NULL AND TargetPlanScaleNumerator   IS NOT NULL
                 AND TargetPlanScaleDenominator IS NOT NULL AND TargetPlanProductionDpi        IS NOT NULL
                 AND TargetPlanDirection        IS NOT NULL AND TargetPlanResizePolicy         IS NOT NULL)
            )
            -- A target-edge plan is exactly a CUSTOM_TARGET_EDGE selection with an edge and a
            -- request, and the two are written together or not at all.
            AND (TargetPlanResizePolicy IS NULL OR
                 (SizingMode IS 'CUSTOM_TARGET_EDGE' AND SizingTargetEdge IS NOT NULL
                  AND SizingRequestedMm IS NOT NULL AND DimensionSemantics IS 'TARGET_EDGE_V1'))
            -- And a maximum-bound plan and a target-edge plan are two different decisions; no row
            -- holds both (§5).
            AND NOT (TargetPlanResizePolicy IS NOT NULL AND PrintPlanResizePolicy IS NOT NULL)
        ),

    ----------------------------------------------------------------------------------------------
    -- The enlargement authority (§9, §22).
    ----------------------------------------------------------------------------------------------
    --
    -- Not a bool, for the reason §9 gives: "enlargement was authorised" answers nothing without
    -- saying WHICH enlargement. Every fact the Domain matches on is stored, so a changed source,
    -- a changed request or a changed projection simply stops matching -- which is why no UPDATE
    -- anywhere has to hunt an authority down and revoke it (§10, §30).
    EnlargementAuthoritySourceRevisionId TEXT NULL,
    EnlargementAuthoritySourceSha256     TEXT NULL
        CHECK (EnlargementAuthoritySourceSha256 IS NULL OR length(EnlargementAuthoritySourceSha256) = 64),
    EnlargementAuthoritySizingMode TEXT NULL
        CHECK (EnlargementAuthoritySizingMode IS NULL OR EnlargementAuthoritySizingMode = 'CUSTOM_TARGET_EDGE'),
    EnlargementAuthorityTargetEdge TEXT NULL
        CHECK (EnlargementAuthorityTargetEdge IS NULL OR EnlargementAuthorityTargetEdge IN
            ('WIDTH', 'HEIGHT', 'LONG_EDGE')),
    EnlargementAuthorityRequestedMm TEXT NULL
        CHECK (EnlargementAuthorityRequestedMm IS NULL OR CAST(EnlargementAuthorityRequestedMm AS REAL) > 0),
    EnlargementAuthorityScaleNumerator   INTEGER NULL,
    EnlargementAuthorityScaleDenominator INTEGER NULL
        CHECK (EnlargementAuthorityScaleDenominator IS NULL OR EnlargementAuthorityScaleDenominator > 0),
    EnlargementAuthorityProjectedPixelWidth  INTEGER NULL
        CHECK (EnlargementAuthorityProjectedPixelWidth  IS NULL OR EnlargementAuthorityProjectedPixelWidth  > 0),
    EnlargementAuthorityProjectedPixelHeight INTEGER NULL
        CHECK (
            (EnlargementAuthorityProjectedPixelHeight IS NULL OR EnlargementAuthorityProjectedPixelHeight > 0)
            AND (
                (EnlargementAuthoritySourceRevisionId IS NULL AND EnlargementAuthoritySourceSha256 IS NULL
                 AND EnlargementAuthoritySizingMode   IS NULL AND EnlargementAuthorityTargetEdge   IS NULL
                 AND EnlargementAuthorityRequestedMm  IS NULL AND EnlargementAuthorityScaleNumerator IS NULL
                 AND EnlargementAuthorityScaleDenominator IS NULL
                 AND EnlargementAuthorityProjectedPixelWidth  IS NULL
                 AND EnlargementAuthorityProjectedPixelHeight IS NULL)
                OR
                (EnlargementAuthoritySourceRevisionId IS NOT NULL AND EnlargementAuthoritySourceSha256 IS NOT NULL
                 AND EnlargementAuthoritySizingMode   IS NOT NULL AND EnlargementAuthorityTargetEdge   IS NOT NULL
                 AND EnlargementAuthorityRequestedMm  IS NOT NULL AND EnlargementAuthorityScaleNumerator IS NOT NULL
                 AND EnlargementAuthorityScaleDenominator IS NOT NULL
                 AND EnlargementAuthorityProjectedPixelWidth  IS NOT NULL
                 AND EnlargementAuthorityProjectedPixelHeight IS NOT NULL)
            )
            -- An authority exists only beside an ENLARGE target-edge plan, and only as an
            -- enlarging ratio. Permission attached to a shrink would read as permission for a run
            -- nobody asked for (§9, §22).
            AND (EnlargementAuthoritySourceRevisionId IS NULL OR
                 (TargetPlanDirection IS 'ENLARGE'
                  AND EnlargementAuthorityScaleNumerator > EnlargementAuthorityScaleDenominator))
        )
);

INSERT INTO ProcessingSession_rebuilt (
    Id, WorkflowType, OutputName, CurrentStep, State, WorkspacePath, CreatedAtUtc, UpdatedAtUtc,
    CompletedAtUtc, HandedOffAtUtc, HandOffReason, AbandonedAtUtc, AbandonReason,
    DimensionsWidthMm, DimensionsHeightMm, DimensionsPixelWidth, DimensionsPixelHeight,
    DimensionsPreset, WhiteUnderbaseBranch,
    TrimMode, TrimMarginTop, TrimMarginRight, TrimMarginBottom, TrimMarginLeft,
    BackgroundRemovalDecision, BackgroundRemovalRevisionId, BackgroundRemovalReviewedSha,
    DimensionSemantics,
    PrintPlanSourceRevisionId, PrintPlanSourceSha256, PrintPlanSourcePixelWidth,
    PrintPlanSourcePixelHeight, PrintPlanMaxWidthMm, PrintPlanMaxHeightMm, PrintPlanLimitKind,
    PrintPlanMode, PrintPlanLimitingEdge, PrintPlanLimitingValueMm, PrintPlanProjectedPixelWidth,
    PrintPlanProjectedPixelHeight, PrintPlanProductionDpi, PrintPlanResizePolicy)
SELECT
    Id, WorkflowType, OutputName, CurrentStep, State, WorkspacePath, CreatedAtUtc, UpdatedAtUtc,
    CompletedAtUtc, HandedOffAtUtc, HandOffReason, AbandonedAtUtc, AbandonReason,
    DimensionsWidthMm, DimensionsHeightMm, DimensionsPixelWidth, DimensionsPixelHeight,
    DimensionsPreset, WhiteUnderbaseBranch,
    TrimMode, TrimMarginTop, TrimMarginRight, TrimMarginBottom, TrimMarginLeft,
    BackgroundRemovalDecision, BackgroundRemovalRevisionId, BackgroundRemovalReviewedSha,
    DimensionSemantics,
    PrintPlanSourceRevisionId, PrintPlanSourceSha256, PrintPlanSourcePixelWidth,
    PrintPlanSourcePixelHeight, PrintPlanMaxWidthMm, PrintPlanMaxHeightMm, PrintPlanLimitKind,
    PrintPlanMode, PrintPlanLimitingEdge, PrintPlanLimitingValueMm, PrintPlanProjectedPixelWidth,
    PrintPlanProjectedPixelHeight, PrintPlanProductionDpi, PrintPlanResizePolicy
FROM ProcessingSession;

DROP TABLE ProcessingSession;
ALTER TABLE ProcessingSession_rebuilt RENAME TO ProcessingSession;

------------------------------------------------------------------------------------------------
-- 2. The immutable attempt snapshot (§24).
------------------------------------------------------------------------------------------------
--
-- The same columns and the same rules, written once. ProcessingAttempt needs no rebuild: 0005
-- gave it plan columns but no semantics column, so nothing here has to widen a CHECK.
--
-- Which contract an attempt ran under is stated by which group is populated -- PrintPlan* for a
-- maximum-bound run, TargetPlan* for a target-edge one -- and no row holds both. That is a fact
-- about the run, not a separate field to keep in step with the columns beside it.

ALTER TABLE ProcessingAttempt ADD COLUMN SizingMode TEXT NULL
    CHECK (SizingMode IS NULL OR SizingMode IN ('PRESET_FIT', 'CUSTOM_TARGET_EDGE'));
ALTER TABLE ProcessingAttempt ADD COLUMN SizingPreset TEXT NULL
    CHECK (SizingPreset IS NULL OR SizingPreset IN ('A3_LANDSCAPE', 'A3_PORTRAIT', 'A4', 'A5'));
ALTER TABLE ProcessingAttempt ADD COLUMN SizingRecommendationKind TEXT NULL
    CHECK (SizingRecommendationKind IS NULL OR SizingRecommendationKind IN
        ('MAXIMUM_BOX', 'MAXIMUM_LONG_EDGE'));
ALTER TABLE ProcessingAttempt ADD COLUMN SizingRecommendationMaxWidthMm  TEXT NULL
    CHECK (SizingRecommendationMaxWidthMm IS NULL OR CAST(SizingRecommendationMaxWidthMm AS REAL) > 0);
ALTER TABLE ProcessingAttempt ADD COLUMN SizingRecommendationMaxHeightMm TEXT NULL
    CHECK (SizingRecommendationMaxHeightMm IS NULL OR CAST(SizingRecommendationMaxHeightMm AS REAL) > 0);
ALTER TABLE ProcessingAttempt ADD COLUMN SizingPresetOverridden INTEGER NULL
    CHECK (SizingPresetOverridden IS NULL OR SizingPresetOverridden IN (0, 1));
ALTER TABLE ProcessingAttempt ADD COLUMN SizingTargetEdge TEXT NULL
    CHECK (SizingTargetEdge IS NULL OR SizingTargetEdge IN ('WIDTH', 'HEIGHT', 'LONG_EDGE'));
ALTER TABLE ProcessingAttempt ADD COLUMN SizingRequestedMm TEXT NULL
    CHECK (SizingRequestedMm IS NULL OR CAST(SizingRequestedMm AS REAL) > 0);

ALTER TABLE ProcessingAttempt ADD COLUMN TargetPlanSourceRevisionId TEXT NULL;
ALTER TABLE ProcessingAttempt ADD COLUMN TargetPlanSourceSha256     TEXT NULL
    CHECK (TargetPlanSourceSha256 IS NULL OR length(TargetPlanSourceSha256) = 64);
ALTER TABLE ProcessingAttempt ADD COLUMN TargetPlanSourcePixelWidth  INTEGER NULL
    CHECK (TargetPlanSourcePixelWidth  IS NULL OR TargetPlanSourcePixelWidth  > 0);
ALTER TABLE ProcessingAttempt ADD COLUMN TargetPlanSourcePixelHeight INTEGER NULL
    CHECK (TargetPlanSourcePixelHeight IS NULL OR TargetPlanSourcePixelHeight > 0);
ALTER TABLE ProcessingAttempt ADD COLUMN TargetPlanPhotoshopEdge TEXT NULL
    CHECK (TargetPlanPhotoshopEdge IS NULL OR TargetPlanPhotoshopEdge IN ('WIDTH', 'HEIGHT'));
ALTER TABLE ProcessingAttempt ADD COLUMN TargetPlanProjectedPixelWidth  INTEGER NULL
    CHECK (TargetPlanProjectedPixelWidth  IS NULL OR TargetPlanProjectedPixelWidth  > 0);
ALTER TABLE ProcessingAttempt ADD COLUMN TargetPlanProjectedPixelHeight INTEGER NULL
    CHECK (TargetPlanProjectedPixelHeight IS NULL OR TargetPlanProjectedPixelHeight > 0);
ALTER TABLE ProcessingAttempt ADD COLUMN TargetPlanScaleNumerator   INTEGER NULL
    CHECK (TargetPlanScaleNumerator   IS NULL OR TargetPlanScaleNumerator   > 0);
ALTER TABLE ProcessingAttempt ADD COLUMN TargetPlanScaleDenominator INTEGER NULL
    CHECK (TargetPlanScaleDenominator IS NULL OR TargetPlanScaleDenominator > 0);
ALTER TABLE ProcessingAttempt ADD COLUMN TargetPlanProductionDpi INTEGER NULL
    CHECK (TargetPlanProductionDpi IS NULL OR TargetPlanProductionDpi = 300);
ALTER TABLE ProcessingAttempt ADD COLUMN TargetPlanDirection TEXT NULL
    CHECK (TargetPlanDirection IS NULL OR TargetPlanDirection IN
        ('RESOLUTION_ONLY', 'SHRINK', 'ENLARGE'));
ALTER TABLE ProcessingAttempt ADD COLUMN TargetPlanResizePolicy TEXT NULL
    CHECK (
        (TargetPlanResizePolicy IS NULL OR TargetPlanResizePolicy IN
            ('NONE', 'BICUBIC_SHARPER', 'PRESERVE_DETAILS'))
        AND (TargetPlanDirection IS NULL OR TargetPlanResizePolicy IS
            CASE TargetPlanDirection
                WHEN 'RESOLUTION_ONLY' THEN 'NONE'
                WHEN 'SHRINK'          THEN 'BICUBIC_SHARPER'
                ELSE                        'PRESERVE_DETAILS'
            END)
        AND (
            (TargetPlanSourceRevisionId     IS NULL AND TargetPlanSourceSha256         IS NULL
             AND TargetPlanSourcePixelWidth IS NULL AND TargetPlanSourcePixelHeight    IS NULL
             AND TargetPlanPhotoshopEdge    IS NULL AND TargetPlanProjectedPixelWidth  IS NULL
             AND TargetPlanProjectedPixelHeight IS NULL AND TargetPlanScaleNumerator   IS NULL
             AND TargetPlanScaleDenominator IS NULL AND TargetPlanProductionDpi        IS NULL
             AND TargetPlanDirection        IS NULL AND TargetPlanResizePolicy         IS NULL)
            OR
            (TargetPlanSourceRevisionId     IS NOT NULL AND TargetPlanSourceSha256         IS NOT NULL
             AND TargetPlanSourcePixelWidth IS NOT NULL AND TargetPlanSourcePixelHeight    IS NOT NULL
             AND TargetPlanPhotoshopEdge    IS NOT NULL AND TargetPlanProjectedPixelWidth  IS NOT NULL
             AND TargetPlanProjectedPixelHeight IS NOT NULL AND TargetPlanScaleNumerator   IS NOT NULL
             AND TargetPlanScaleDenominator IS NOT NULL AND TargetPlanProductionDpi        IS NOT NULL
             AND TargetPlanDirection        IS NOT NULL AND TargetPlanResizePolicy         IS NOT NULL)
        )
        AND (TargetPlanResizePolicy IS NULL OR
             (SizingMode IS 'CUSTOM_TARGET_EDGE' AND SizingTargetEdge IS NOT NULL
              AND SizingRequestedMm IS NOT NULL))
        AND NOT (TargetPlanResizePolicy IS NOT NULL AND PrintPlanResizePolicy IS NOT NULL)
    );

ALTER TABLE ProcessingAttempt ADD COLUMN EnlargementAuthoritySourceRevisionId TEXT NULL;
ALTER TABLE ProcessingAttempt ADD COLUMN EnlargementAuthoritySourceSha256     TEXT NULL
    CHECK (EnlargementAuthoritySourceSha256 IS NULL OR length(EnlargementAuthoritySourceSha256) = 64);
ALTER TABLE ProcessingAttempt ADD COLUMN EnlargementAuthoritySizingMode TEXT NULL
    CHECK (EnlargementAuthoritySizingMode IS NULL OR EnlargementAuthoritySizingMode = 'CUSTOM_TARGET_EDGE');
ALTER TABLE ProcessingAttempt ADD COLUMN EnlargementAuthorityTargetEdge TEXT NULL
    CHECK (EnlargementAuthorityTargetEdge IS NULL OR EnlargementAuthorityTargetEdge IN
        ('WIDTH', 'HEIGHT', 'LONG_EDGE'));
ALTER TABLE ProcessingAttempt ADD COLUMN EnlargementAuthorityRequestedMm TEXT NULL
    CHECK (EnlargementAuthorityRequestedMm IS NULL OR CAST(EnlargementAuthorityRequestedMm AS REAL) > 0);
ALTER TABLE ProcessingAttempt ADD COLUMN EnlargementAuthorityScaleNumerator   INTEGER NULL;
ALTER TABLE ProcessingAttempt ADD COLUMN EnlargementAuthorityScaleDenominator INTEGER NULL
    CHECK (EnlargementAuthorityScaleDenominator IS NULL OR EnlargementAuthorityScaleDenominator > 0);
ALTER TABLE ProcessingAttempt ADD COLUMN EnlargementAuthorityProjectedPixelWidth INTEGER NULL
    CHECK (EnlargementAuthorityProjectedPixelWidth IS NULL OR EnlargementAuthorityProjectedPixelWidth > 0);
ALTER TABLE ProcessingAttempt ADD COLUMN EnlargementAuthorityProjectedPixelHeight INTEGER NULL
    CHECK (
        (EnlargementAuthorityProjectedPixelHeight IS NULL OR EnlargementAuthorityProjectedPixelHeight > 0)
        AND (
            (EnlargementAuthoritySourceRevisionId IS NULL AND EnlargementAuthoritySourceSha256 IS NULL
             AND EnlargementAuthoritySizingMode   IS NULL AND EnlargementAuthorityTargetEdge   IS NULL
             AND EnlargementAuthorityRequestedMm  IS NULL AND EnlargementAuthorityScaleNumerator IS NULL
             AND EnlargementAuthorityScaleDenominator IS NULL
             AND EnlargementAuthorityProjectedPixelWidth  IS NULL
             AND EnlargementAuthorityProjectedPixelHeight IS NULL)
            OR
            (EnlargementAuthoritySourceRevisionId IS NOT NULL AND EnlargementAuthoritySourceSha256 IS NOT NULL
             AND EnlargementAuthoritySizingMode   IS NOT NULL AND EnlargementAuthorityTargetEdge   IS NOT NULL
             AND EnlargementAuthorityRequestedMm  IS NOT NULL AND EnlargementAuthorityScaleNumerator IS NOT NULL
             AND EnlargementAuthorityScaleDenominator IS NOT NULL
             AND EnlargementAuthorityProjectedPixelWidth  IS NOT NULL
             AND EnlargementAuthorityProjectedPixelHeight IS NOT NULL)
        )
        AND (EnlargementAuthoritySourceRevisionId IS NULL OR
             (TargetPlanDirection IS 'ENLARGE'
              AND EnlargementAuthorityScaleNumerator > EnlargementAuthorityScaleDenominator))
    );
