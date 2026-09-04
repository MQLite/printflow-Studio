-- SCRUM-11092 / SCRUM-11112: additive vocabulary; retain all historical values and constraints.
-- Transactional table rebuild under MigrationRunner, with foreign keys restored afterwards.

CREATE TABLE Revision_ManualResult (
    Id                 TEXT PRIMARY KEY NOT NULL,
    SessionId          TEXT NOT NULL REFERENCES ProcessingSession(Id) ON DELETE CASCADE,
    SourceRevisionId   TEXT NULL REFERENCES Revision(Id),
    Operation          TEXT NOT NULL CHECK (Operation IN
                           ('IMPORT', 'ENHANCE', 'REMOVE_BACKGROUND', 'TRIM',
                            'PROMOTE_APPROVED', 'MANUAL_IMPORT', 'PHOTOSHOP_OUTPUT', 'PREPARE_PSD', 'PREPARE_PDF', 'MANUAL_RESULT_IMPORT')),
    RelativePath       TEXT NOT NULL,
    Format             TEXT NOT NULL,
    ByteLength         INTEGER NOT NULL CHECK (ByteLength > 0),
    Sha256             TEXT NOT NULL CHECK (length(Sha256) = 64),
    PixelWidth         INTEGER NULL,
    PixelHeight        INTEGER NULL,
    DpiX               REAL NULL,
    DpiY               REAL NULL,
    ColourMode         TEXT NOT NULL,
    HasAlpha           INTEGER NULL CHECK (HasAlpha IS NULL OR HasAlpha IN (0, 1)),
    CreatedAtUtc       TEXT NOT NULL,
    IsValid            INTEGER NOT NULL DEFAULT 1 CHECK (IsValid IN (0, 1)),
    InvalidatedAtUtc   TEXT NULL,
    InvalidationReason TEXT NULL CHECK (InvalidationReason IS NULL OR InvalidationReason IN
                           ('SUPERSEDED', 'UPSTREAM_CHANGED', 'FILE_MUTATED', 'REJECTED', 'SESSION_RESET')),
    ReviewState        TEXT NOT NULL DEFAULT 'NOT_REVIEWED'
                           CHECK (ReviewState IN ('NOT_REVIEWED', 'APPROVED', 'REJECTED')),
    UNIQUE (SessionId, RelativePath)
);
INSERT INTO Revision_ManualResult SELECT * FROM Revision;
DROP TABLE Revision;
ALTER TABLE Revision_ManualResult RENAME TO Revision;
CREATE INDEX IX_Revision_Session ON Revision(SessionId);
CREATE INDEX IX_Revision_Source ON Revision(SourceRevisionId);
CREATE TRIGGER Revision_Immutable_Update
BEFORE UPDATE ON Revision
WHEN  OLD.Sha256           <> NEW.Sha256
   OR OLD.RelativePath     <> NEW.RelativePath
   OR OLD.SourceRevisionId IS NOT NEW.SourceRevisionId
   OR OLD.Operation        <> NEW.Operation
   OR OLD.ByteLength       <> NEW.ByteLength
   OR OLD.CreatedAtUtc     <> NEW.CreatedAtUtc
BEGIN
    SELECT RAISE(ABORT, 'Revision identity columns are immutable');
END;
CREATE TABLE ProcessingAttempt_ManualResult (
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
            ('UNSPECIFIED', 'USE_AUTOMATIC_SELECTION_FOR_REVIEWED_CONTENT', 'MANUAL_RESULT_FOR_REVIEWED_CONTENT')),
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
        ), TrimContentLeft   INTEGER NULL CHECK (TrimContentLeft   IS NULL OR TrimContentLeft   >= 0), TrimContentTop    INTEGER NULL CHECK (TrimContentTop    IS NULL OR TrimContentTop    >= 0), TrimContentRight  INTEGER NULL CHECK (TrimContentRight  IS NULL OR TrimContentRight  >  0), TrimContentBottom INTEGER NULL CHECK (TrimContentBottom IS NULL OR TrimContentBottom >  0), TrimAppliedLeft   INTEGER NULL CHECK (TrimAppliedLeft   IS NULL OR TrimAppliedLeft   >= 0), TrimAppliedTop    INTEGER NULL CHECK (TrimAppliedTop    IS NULL OR TrimAppliedTop    >= 0), TrimAppliedRight  INTEGER NULL CHECK (TrimAppliedRight  IS NULL OR TrimAppliedRight  >  0), TrimAppliedBottom INTEGER NULL CHECK (TrimAppliedBottom IS NULL OR TrimAppliedBottom >  0),

    -- MVP design invariant 4/5, made structural: a failed/running/interrupted/cancelled
    -- attempt can never carry an output Revision.
    CHECK ((ResultStatus = 'SUCCEEDED' AND OutputRevisionId IS NOT NULL)
        OR (ResultStatus <> 'SUCCEEDED' AND OutputRevisionId IS NULL))
);
INSERT INTO ProcessingAttempt_ManualResult SELECT * FROM ProcessingAttempt;
DROP TABLE ProcessingAttempt;
ALTER TABLE ProcessingAttempt_ManualResult RENAME TO ProcessingAttempt;
CREATE INDEX IX_Attempt_Session ON ProcessingAttempt(SessionId);
CREATE INDEX IX_Attempt_Running ON ProcessingAttempt(ResultStatus) WHERE ResultStatus = 'RUNNING';
CREATE TRIGGER ProcessingAttempt_TrimBounds_Coherent_Insert
BEFORE INSERT ON ProcessingAttempt
WHEN NOT (
    (NEW.TrimContentLeft   IS NULL AND NEW.TrimContentTop    IS NULL
     AND NEW.TrimContentRight IS NULL AND NEW.TrimContentBottom IS NULL
     AND NEW.TrimAppliedLeft  IS NULL AND NEW.TrimAppliedTop    IS NULL
     AND NEW.TrimAppliedRight IS NULL AND NEW.TrimAppliedBottom IS NULL)
    OR
    (NEW.TrimContentLeft   IS NOT NULL AND NEW.TrimContentTop    IS NOT NULL
     AND NEW.TrimContentRight IS NOT NULL AND NEW.TrimContentBottom IS NOT NULL
     AND NEW.TrimAppliedLeft  IS NOT NULL AND NEW.TrimAppliedTop    IS NOT NULL
     AND NEW.TrimAppliedRight IS NOT NULL AND NEW.TrimAppliedBottom IS NOT NULL
     AND NEW.TrimContentRight  > NEW.TrimContentLeft
     AND NEW.TrimContentBottom > NEW.TrimContentTop
     AND NEW.TrimAppliedRight  > NEW.TrimAppliedLeft
     AND NEW.TrimAppliedBottom > NEW.TrimAppliedTop
     AND NEW.TrimAppliedLeft   <= NEW.TrimContentLeft
     AND NEW.TrimAppliedTop    <= NEW.TrimContentTop
     AND NEW.TrimAppliedRight  >= NEW.TrimContentRight
     AND NEW.TrimAppliedBottom >= NEW.TrimContentBottom)
)
BEGIN
    SELECT RAISE(ABORT, 'Trim bounds must be a complete non-empty content/applied pair whose applied rectangle contains the content rectangle.');
END;
CREATE TRIGGER ProcessingAttempt_TrimBounds_Coherent_Update
BEFORE UPDATE ON ProcessingAttempt
WHEN NOT (
    (NEW.TrimContentLeft   IS NULL AND NEW.TrimContentTop    IS NULL
     AND NEW.TrimContentRight IS NULL AND NEW.TrimContentBottom IS NULL
     AND NEW.TrimAppliedLeft  IS NULL AND NEW.TrimAppliedTop    IS NULL
     AND NEW.TrimAppliedRight IS NULL AND NEW.TrimAppliedBottom IS NULL)
    OR
    (NEW.TrimContentLeft   IS NOT NULL AND NEW.TrimContentTop    IS NOT NULL
     AND NEW.TrimContentRight IS NOT NULL AND NEW.TrimContentBottom IS NOT NULL
     AND NEW.TrimAppliedLeft  IS NOT NULL AND NEW.TrimAppliedTop    IS NOT NULL
     AND NEW.TrimAppliedRight IS NOT NULL AND NEW.TrimAppliedBottom IS NOT NULL
     AND NEW.TrimContentRight  > NEW.TrimContentLeft
     AND NEW.TrimContentBottom > NEW.TrimContentTop
     AND NEW.TrimAppliedRight  > NEW.TrimAppliedLeft
     AND NEW.TrimAppliedBottom > NEW.TrimAppliedTop
     AND NEW.TrimAppliedLeft   <= NEW.TrimContentLeft
     AND NEW.TrimAppliedTop    <= NEW.TrimContentTop
     AND NEW.TrimAppliedRight  >= NEW.TrimContentRight
     AND NEW.TrimAppliedBottom >= NEW.TrimContentBottom)
)
BEGIN
    SELECT RAISE(ABORT, 'Trim bounds must be a complete non-empty content/applied pair whose applied rectangle contains the content rectangle.');
END;
CREATE TRIGGER ProcessingAttempt_TrimBounds_Immutable
BEFORE UPDATE ON ProcessingAttempt
WHEN OLD.TrimContentLeft IS NOT NULL
 AND (NEW.TrimContentLeft   IS NOT OLD.TrimContentLeft
   OR NEW.TrimContentTop    IS NOT OLD.TrimContentTop
   OR NEW.TrimContentRight  IS NOT OLD.TrimContentRight
   OR NEW.TrimContentBottom IS NOT OLD.TrimContentBottom
   OR NEW.TrimAppliedLeft   IS NOT OLD.TrimAppliedLeft
   OR NEW.TrimAppliedTop    IS NOT OLD.TrimAppliedTop
   OR NEW.TrimAppliedRight  IS NOT OLD.TrimAppliedRight
   OR NEW.TrimAppliedBottom IS NOT OLD.TrimAppliedBottom)
BEGIN
    SELECT RAISE(ABORT, 'The trim geometry recorded for an attempt is immutable history and cannot be rewritten.');
END;
