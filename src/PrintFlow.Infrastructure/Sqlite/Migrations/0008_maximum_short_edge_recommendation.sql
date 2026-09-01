-- Epic 11400 post-final A5 correction §15, §16, §17: add MAXIMUM_SHORT_EDGE to the persisted
-- recommendation-kind vocabulary.
--
-- v1.15.0 changes what A5 means as a print size: from a 135 mm maximum LONG edge to a 135 mm
-- maximum SHORT edge. That is a third configured form, not a re-spelling of an existing one, and
-- the schema has to be able to say so — a decision persisted as MAXIMUM_LONG_EDGE 135 and one
-- persisted as MAXIMUM_SHORT_EDGE 135 carry the same number and mean different geometry.
--
-- This is a CONTRACT-VERSION change, not a data correction (§16). Nothing here rewrites a value:
--
--   * every existing MAXIMUM_LONG_EDGE row keeps the meaning it was written with, A5's included.
--     A session or attempt recorded under v1.14 really was made against a maximum long edge, and
--     relabelling it would be this migration inventing a decision nobody made;
--
--   * no row is backfilled, defaulted, or reinterpreted. The vocabulary is widened and the data
--     is copied across untouched;
--
--   * new rows may write MAXIMUM_SHORT_EDGE. That is the only difference this migration makes.
--
-- A pending v1.14 A5 plan is handled in the Domain rather than here, and deliberately: its rows
-- stay exactly as they are, and WorkflowSnapshot.UsablePrintPreparationPlan simply stops returning
-- it because the recommendation it names is no longer the one this installation configures. The
-- operator reconfirms the size through the ordinary ReturnToStep(PrintDimensions) route. Nothing
-- is deleted, mutated or migrated to make that happen (§13, §16).
--
-- SQLite cannot alter or drop a CHECK constraint, so widening the two SizingRecommendationKind
-- constraints means the documented twelve-step rebuild that 0005 and 0006 already established:
-- create the table afresh, copy the rows, drop the original, rename, recreate the indexes. Every
-- other column, type, default, CHECK and foreign key below is reproduced verbatim from the schema
-- as 0006 and 0007 left it; the only textual difference in each table is the one widened IN list
-- (§17).
--
-- A second recommendation-kind column was rejected for the reason 0006 rejected a second
-- semantics column: two columns answering "which form was configured" is two sources of truth for
-- one fact, and the older one goes stale the moment a short edge is recorded (§15).
--
-- MigrationRunner suspends foreign keys for the migration pass, which is what makes both DROPs
-- below safe: with enforcement on, dropping ProcessingSession would fire the ON DELETE CASCADE
-- rules pointing at it and take the sessions' steps, revisions, attempts, outputs, reviews,
-- snapshots and automation log with them (§17).

------------------------------------------------------------------------------------------------
-- 1. ProcessingSession — the pending decision.
------------------------------------------------------------------------------------------------

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

    -- The one column this migration exists for. MAXIMUM_SHORT_EDGE joins the vocabulary; the two
    -- members already there keep exactly the meaning they had, and no stored value is rewritten.
    -- A row reading MAXIMUM_LONG_EDGE 135 against A5 is a v1.14 decision and stays one (§16).
    SizingRecommendationKind TEXT NULL
        CHECK (SizingRecommendationKind IS NULL OR SizingRecommendationKind IN
            ('MAXIMUM_BOX', 'MAXIMUM_LONG_EDGE', 'MAXIMUM_SHORT_EDGE')),
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
    PrintPlanProjectedPixelHeight, PrintPlanProductionDpi, PrintPlanResizePolicy,
    SizingMode, SizingPreset, SizingRecommendationKind, SizingRecommendationMaxWidthMm,
    SizingRecommendationMaxHeightMm, SizingPresetOverridden, SizingTargetEdge, SizingRequestedMm,
    TargetPlanSourceRevisionId, TargetPlanSourceSha256, TargetPlanSourcePixelWidth,
    TargetPlanSourcePixelHeight, TargetPlanPhotoshopEdge, TargetPlanProjectedPixelWidth,
    TargetPlanProjectedPixelHeight, TargetPlanScaleNumerator, TargetPlanScaleDenominator,
    TargetPlanProductionDpi, TargetPlanDirection, TargetPlanResizePolicy,
    EnlargementAuthoritySourceRevisionId, EnlargementAuthoritySourceSha256,
    EnlargementAuthoritySizingMode, EnlargementAuthorityTargetEdge, EnlargementAuthorityRequestedMm,
    EnlargementAuthorityScaleNumerator, EnlargementAuthorityScaleDenominator,
    EnlargementAuthorityProjectedPixelWidth, EnlargementAuthorityProjectedPixelHeight)
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
    PrintPlanProjectedPixelHeight, PrintPlanProductionDpi, PrintPlanResizePolicy,
    SizingMode, SizingPreset, SizingRecommendationKind, SizingRecommendationMaxWidthMm,
    SizingRecommendationMaxHeightMm, SizingPresetOverridden, SizingTargetEdge, SizingRequestedMm,
    TargetPlanSourceRevisionId, TargetPlanSourceSha256, TargetPlanSourcePixelWidth,
    TargetPlanSourcePixelHeight, TargetPlanPhotoshopEdge, TargetPlanProjectedPixelWidth,
    TargetPlanProjectedPixelHeight, TargetPlanScaleNumerator, TargetPlanScaleDenominator,
    TargetPlanProductionDpi, TargetPlanDirection, TargetPlanResizePolicy,
    EnlargementAuthoritySourceRevisionId, EnlargementAuthoritySourceSha256,
    EnlargementAuthoritySizingMode, EnlargementAuthorityTargetEdge, EnlargementAuthorityRequestedMm,
    EnlargementAuthorityScaleNumerator, EnlargementAuthorityScaleDenominator,
    EnlargementAuthorityProjectedPixelWidth, EnlargementAuthorityProjectedPixelHeight
FROM ProcessingSession;

DROP TABLE ProcessingSession;
ALTER TABLE ProcessingSession_rebuilt RENAME TO ProcessingSession;

------------------------------------------------------------------------------------------------
-- 2. ProcessingAttempt — the immutable audit snapshot (§18).
------------------------------------------------------------------------------------------------
--
-- The same widening, and the same absolute refusal to touch a value. A v1.14 A5 attempt says
-- MAXIMUM_LONG_EDGE 135 and goes on saying it forever: what an output actually ran under is not
-- something a later preset change may edit (§12, §18).
--
-- Reproduced from the schema as 0001, 0002, 0003, 0004, 0005 and 0006 left it, in the column
-- order those migrations produced, so the copy below is positionally as well as nominally exact.
-- The self-referencing RetryOfAttemptId foreign key, both Revision references, the SessionId
-- cascade and the SUCCEEDED/OutputRevisionId table CHECK are all carried across unchanged.

CREATE TABLE ProcessingAttempt_rebuilt (
    -- 0001
    Id                TEXT PRIMARY KEY NOT NULL,
    SessionId         TEXT NOT NULL REFERENCES ProcessingSession(Id) ON DELETE CASCADE,
    StepKind          TEXT NOT NULL,
    InputRevisionId   TEXT NULL REFERENCES Revision(Id),
    Operation         TEXT NOT NULL,
    AdapterId         TEXT NOT NULL,
    StartedAtUtc      TEXT NOT NULL,
    EndedAtUtc        TEXT NULL,
    ResultStatus      TEXT NOT NULL CHECK (ResultStatus IN
                           ('RUNNING', 'SUCCEEDED', 'FAILED', 'INTERRUPTED', 'CANCELLED')),
    OutputRevisionId  TEXT NULL REFERENCES Revision(Id),
    FailureCode       TEXT NULL,
    FailureDetailJson TEXT NULL,
    RetryOfAttemptId  TEXT NULL REFERENCES ProcessingAttempt(Id),
    RetrySequence     INTEGER NOT NULL DEFAULT 0,

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

    -- 0004
    AdapterNotes TEXT NULL,

    -- 0005
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

    -- 0006, with MAXIMUM_SHORT_EDGE added to SizingRecommendationKind and nothing else changed.
    SizingMode TEXT NULL
        CHECK (SizingMode IS NULL OR SizingMode IN ('PRESET_FIT', 'CUSTOM_TARGET_EDGE')),
    SizingPreset TEXT NULL
        CHECK (SizingPreset IS NULL OR SizingPreset IN ('A3_LANDSCAPE', 'A3_PORTRAIT', 'A4', 'A5')),
    SizingRecommendationKind TEXT NULL
        CHECK (SizingRecommendationKind IS NULL OR SizingRecommendationKind IN
            ('MAXIMUM_BOX', 'MAXIMUM_LONG_EDGE', 'MAXIMUM_SHORT_EDGE')),
    SizingRecommendationMaxWidthMm  TEXT NULL
        CHECK (SizingRecommendationMaxWidthMm IS NULL OR CAST(SizingRecommendationMaxWidthMm AS REAL) > 0),
    SizingRecommendationMaxHeightMm TEXT NULL
        CHECK (SizingRecommendationMaxHeightMm IS NULL OR CAST(SizingRecommendationMaxHeightMm AS REAL) > 0),
    SizingPresetOverridden INTEGER NULL
        CHECK (SizingPresetOverridden IS NULL OR SizingPresetOverridden IN (0, 1)),
    SizingTargetEdge TEXT NULL
        CHECK (SizingTargetEdge IS NULL OR SizingTargetEdge IN ('WIDTH', 'HEIGHT', 'LONG_EDGE')),
    SizingRequestedMm TEXT NULL
        CHECK (SizingRequestedMm IS NULL OR CAST(SizingRequestedMm AS REAL) > 0),

    TargetPlanSourceRevisionId TEXT NULL,
    TargetPlanSourceSha256     TEXT NULL
        CHECK (TargetPlanSourceSha256 IS NULL OR length(TargetPlanSourceSha256) = 64),
    TargetPlanSourcePixelWidth  INTEGER NULL
        CHECK (TargetPlanSourcePixelWidth  IS NULL OR TargetPlanSourcePixelWidth  > 0),
    TargetPlanSourcePixelHeight INTEGER NULL
        CHECK (TargetPlanSourcePixelHeight IS NULL OR TargetPlanSourcePixelHeight > 0),
    TargetPlanPhotoshopEdge TEXT NULL
        CHECK (TargetPlanPhotoshopEdge IS NULL OR TargetPlanPhotoshopEdge IN ('WIDTH', 'HEIGHT')),
    TargetPlanProjectedPixelWidth  INTEGER NULL
        CHECK (TargetPlanProjectedPixelWidth  IS NULL OR TargetPlanProjectedPixelWidth  > 0),
    TargetPlanProjectedPixelHeight INTEGER NULL
        CHECK (TargetPlanProjectedPixelHeight IS NULL OR TargetPlanProjectedPixelHeight > 0),
    TargetPlanScaleNumerator   INTEGER NULL
        CHECK (TargetPlanScaleNumerator   IS NULL OR TargetPlanScaleNumerator   > 0),
    TargetPlanScaleDenominator INTEGER NULL
        CHECK (TargetPlanScaleDenominator IS NULL OR TargetPlanScaleDenominator > 0),
    TargetPlanProductionDpi INTEGER NULL
        CHECK (TargetPlanProductionDpi IS NULL OR TargetPlanProductionDpi = 300),
    TargetPlanDirection TEXT NULL
        CHECK (TargetPlanDirection IS NULL OR TargetPlanDirection IN
            ('RESOLUTION_ONLY', 'SHRINK', 'ENLARGE')),
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
            AND (TargetPlanResizePolicy IS NULL OR
                 (SizingMode IS 'CUSTOM_TARGET_EDGE' AND SizingTargetEdge IS NOT NULL
                  AND SizingRequestedMm IS NOT NULL))
            AND NOT (TargetPlanResizePolicy IS NOT NULL AND PrintPlanResizePolicy IS NOT NULL)
        ),

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
    EnlargementAuthorityProjectedPixelWidth INTEGER NULL
        CHECK (EnlargementAuthorityProjectedPixelWidth IS NULL OR EnlargementAuthorityProjectedPixelWidth > 0),
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
            AND (EnlargementAuthoritySourceRevisionId IS NULL OR
                 (TargetPlanDirection IS 'ENLARGE'
                  AND EnlargementAuthorityScaleNumerator > EnlargementAuthorityScaleDenominator))
        ),

    -- MVP design invariant 4/5, made structural: a failed/running/interrupted/cancelled
    -- attempt can never carry an output Revision.
    CHECK ((ResultStatus = 'SUCCEEDED' AND OutputRevisionId IS NOT NULL)
        OR (ResultStatus <> 'SUCCEEDED' AND OutputRevisionId IS NULL))
);

INSERT INTO ProcessingAttempt_rebuilt (
    Id, SessionId, StepKind, InputRevisionId, Operation, AdapterId, StartedAtUtc, EndedAtUtc,
    ResultStatus, OutputRevisionId, FailureCode, FailureDetailJson, RetryOfAttemptId, RetrySequence,
    TrimMode, TrimMarginTop, TrimMarginRight, TrimMarginBottom, TrimMarginLeft,
    BackgroundRemovalDecision, BackgroundRemovalRevisionId, BackgroundRemovalReviewedSha,
    AdapterNotes,
    PrintPlanSourceRevisionId, PrintPlanSourceSha256, PrintPlanSourcePixelWidth,
    PrintPlanSourcePixelHeight, PrintPlanMaxWidthMm, PrintPlanMaxHeightMm, PrintPlanLimitKind,
    PrintPlanMode, PrintPlanLimitingEdge, PrintPlanLimitingValueMm, PrintPlanProjectedPixelWidth,
    PrintPlanProjectedPixelHeight, PrintPlanProductionDpi, PrintPlanResizePolicy,
    SizingMode, SizingPreset, SizingRecommendationKind, SizingRecommendationMaxWidthMm,
    SizingRecommendationMaxHeightMm, SizingPresetOverridden, SizingTargetEdge, SizingRequestedMm,
    TargetPlanSourceRevisionId, TargetPlanSourceSha256, TargetPlanSourcePixelWidth,
    TargetPlanSourcePixelHeight, TargetPlanPhotoshopEdge, TargetPlanProjectedPixelWidth,
    TargetPlanProjectedPixelHeight, TargetPlanScaleNumerator, TargetPlanScaleDenominator,
    TargetPlanProductionDpi, TargetPlanDirection, TargetPlanResizePolicy,
    EnlargementAuthoritySourceRevisionId, EnlargementAuthoritySourceSha256,
    EnlargementAuthoritySizingMode, EnlargementAuthorityTargetEdge, EnlargementAuthorityRequestedMm,
    EnlargementAuthorityScaleNumerator, EnlargementAuthorityScaleDenominator,
    EnlargementAuthorityProjectedPixelWidth, EnlargementAuthorityProjectedPixelHeight)
SELECT
    Id, SessionId, StepKind, InputRevisionId, Operation, AdapterId, StartedAtUtc, EndedAtUtc,
    ResultStatus, OutputRevisionId, FailureCode, FailureDetailJson, RetryOfAttemptId, RetrySequence,
    TrimMode, TrimMarginTop, TrimMarginRight, TrimMarginBottom, TrimMarginLeft,
    BackgroundRemovalDecision, BackgroundRemovalRevisionId, BackgroundRemovalReviewedSha,
    AdapterNotes,
    PrintPlanSourceRevisionId, PrintPlanSourceSha256, PrintPlanSourcePixelWidth,
    PrintPlanSourcePixelHeight, PrintPlanMaxWidthMm, PrintPlanMaxHeightMm, PrintPlanLimitKind,
    PrintPlanMode, PrintPlanLimitingEdge, PrintPlanLimitingValueMm, PrintPlanProjectedPixelWidth,
    PrintPlanProjectedPixelHeight, PrintPlanProductionDpi, PrintPlanResizePolicy,
    SizingMode, SizingPreset, SizingRecommendationKind, SizingRecommendationMaxWidthMm,
    SizingRecommendationMaxHeightMm, SizingPresetOverridden, SizingTargetEdge, SizingRequestedMm,
    TargetPlanSourceRevisionId, TargetPlanSourceSha256, TargetPlanSourcePixelWidth,
    TargetPlanSourcePixelHeight, TargetPlanPhotoshopEdge, TargetPlanProjectedPixelWidth,
    TargetPlanProjectedPixelHeight, TargetPlanScaleNumerator, TargetPlanScaleDenominator,
    TargetPlanProductionDpi, TargetPlanDirection, TargetPlanResizePolicy,
    EnlargementAuthoritySourceRevisionId, EnlargementAuthoritySourceSha256,
    EnlargementAuthoritySizingMode, EnlargementAuthorityTargetEdge, EnlargementAuthorityRequestedMm,
    EnlargementAuthorityScaleNumerator, EnlargementAuthorityScaleDenominator,
    EnlargementAuthorityProjectedPixelWidth, EnlargementAuthorityProjectedPixelHeight
FROM ProcessingAttempt;

DROP TABLE ProcessingAttempt;
ALTER TABLE ProcessingAttempt_rebuilt RENAME TO ProcessingAttempt;

-- Dropping a table drops its indexes with it, so 0001's two are recreated verbatim. Without them
-- the startup crash-recovery scan and every per-session attempt read would fall back to a table
-- scan, which is a regression a rebuild has no business introducing.
CREATE INDEX IX_Attempt_Session ON ProcessingAttempt(SessionId);
CREATE INDEX IX_Attempt_Running ON ProcessingAttempt(ResultStatus) WHERE ResultStatus = 'RUNNING';
