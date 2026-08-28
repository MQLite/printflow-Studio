-- Epic 11400 Part B1A.2A §9, §11, §12, §13: make the maximum-bound decision durable, say
-- explicitly what an existing millimetre pair meant, and bind an executable plan to the exact
-- upstream artefact it was calculated from.
--
-- The problem this closes is not a missing column; it is a changed meaning. DimensionsWidthMm
-- and DimensionsHeightMm did not move, but under the accepted B1A.1 contract they are now a fit
-- box rather than two independently exact output dimensions. A stored pair that cannot say which
-- reading it was written under is a pair no code may act on -- and silently reading every old row
-- as a fit box would hand Photoshop a limiting edge nobody chose (§9).
--
-- So DimensionSemantics is added and existing rows are backfilled to LEGACY_EXACT_PAIR. That is
-- what they were, stated rather than inferred. Those rows stay fully readable for audit and
-- display; what they cannot do is execute, because a legacy pair has no plan and the workflow
-- refuses to start Photoshop output without one (§10). Which of the old two dimensions was
-- intended as the limiting edge is a question the record does not answer, and inferring it from
-- the source ratio -- even when the ratio happens to fit -- would be the software making the
-- operator's decision for them. The operator reconfirms the limits.
--
-- The plan columns follow the split the trim parameters and the background-removal authority
-- already use (Epic 11200 Part C3 §14; Epic 11300 Part C2B1 §11):
--
--   ProcessingSession  -> "what would the NEXT Photoshop output do". A pending plan, replaced
--                         whenever the operator records different limits, and needed so a
--                         decision made before a restart is still there afterwards.
--
--   ProcessingAttempt  -> "what did THIS output run under". Written once with the attempt's
--                         opening transaction, before the working copy and before Photoshop is
--                         touched, and never updated afterwards -- the attempt upsert
--                         deliberately leaves these columns out of its DO UPDATE clause, so a
--                         later change of limits cannot relabel an earlier attempt (§12).
--
-- Typed columns rather than a JSON blob, for the same reason the trim margin is typed: a support
-- query wants to read the limiting edge and the projected pixels directly.
--
-- The attempt's copy carries the bounds and the limit kind as well as the derived plan, so it is
-- self-contained. An audit row that had to be joined back to the session to say how big the box
-- was would be an audit row the session could still change out from under.
--
-- The plan columns are identical on both tables and are read by one mapper, so "what a plan is"
-- has one definition wherever it is stored.
--
-- Nullable with no default throughout. NULL across the plan columns means "no plan recorded" --
-- on a session, that nobody has recorded maximum bounds under the current contract; on an
-- attempt, that this attempt was not a Photoshop output at all (a Meitu call, a trim, a manual
-- crop, a promotion). It never means "the default was used". There is no default: 300 ppi is
-- fixed, the resampling policy is fixed, and the limiting edge is calculated, never guessed.
--
-- Nothing here stores a stale plan differently from a fresh one. Invalidation is not a write
-- (§16): a plan stops being usable when the artefact it names stops being the one Photoshop will
-- consume, which WorkflowSnapshot decides by comparing these columns against the current upstream
-- step result. That is why no UPDATE has to hunt down and clear these values when a Revision is
-- superseded, and why the row left behind is honest history rather than a lingering permission.

-- The semantics text mirrors the enum exactly, both members included, so the schema states the
-- same closed set the code does. There is deliberately no member meaning "unknown": a session
-- either recorded a reading or holds no dimensions at all, and the absence of dimensions is NULL.
ALTER TABLE ProcessingSession ADD COLUMN DimensionSemantics TEXT NULL
    CHECK (DimensionSemantics IS NULL OR DimensionSemantics IN ('LEGACY_EXACT_PAIR', 'MAX_BOUNDS_V1'));

-- Stated, not inferred. Every row that already holds a millimetre pair was written under the old
-- reading, and this is the one moment at which that fact is still certain.
UPDATE ProcessingSession
   SET DimensionSemantics = 'LEGACY_EXACT_PAIR'
 WHERE DimensionsWidthMm IS NOT NULL;

-- The source binding. Both halves are kept for the same reason every review decision keeps both
-- (MVP design invariants 2 and 3): the RevisionId answers "which artefact" and the SHA-256
-- answers "which bytes". An id alone would still match after the file underneath it was replaced
-- in place, which is precisely the case §7 requires to be refused.
ALTER TABLE ProcessingSession ADD COLUMN PrintPlanSourceRevisionId     TEXT    NULL;
ALTER TABLE ProcessingSession ADD COLUMN PrintPlanSourceSha256         TEXT    NULL
    CHECK (PrintPlanSourceSha256 IS NULL OR length(PrintPlanSourceSha256) = 64);

-- The source's own validated pixels: the other half of what makes a limiting edge calculable.
ALTER TABLE ProcessingSession ADD COLUMN PrintPlanSourcePixelWidth     INTEGER NULL
    CHECK (PrintPlanSourcePixelWidth  IS NULL OR PrintPlanSourcePixelWidth  > 0);
ALTER TABLE ProcessingSession ADD COLUMN PrintPlanSourcePixelHeight    INTEGER NULL
    CHECK (PrintPlanSourcePixelHeight IS NULL OR PrintPlanSourcePixelHeight > 0);

-- The fit box. Named Max* rather than reusing the Dimensions* columns because these are the
-- bounds the plan was calculated against, and the plan must stay readable as one value.
ALTER TABLE ProcessingSession ADD COLUMN PrintPlanMaxWidthMm           REAL    NULL
    CHECK (PrintPlanMaxWidthMm  IS NULL OR PrintPlanMaxWidthMm  > 0);
ALTER TABLE ProcessingSession ADD COLUMN PrintPlanMaxHeightMm          REAL    NULL
    CHECK (PrintPlanMaxHeightMm IS NULL OR PrintPlanMaxHeightMm > 0);
ALTER TABLE ProcessingSession ADD COLUMN PrintPlanLimitKind            TEXT    NULL
    CHECK (PrintPlanLimitKind IS NULL OR PrintPlanLimitKind IN
        ('A3_LANDSCAPE', 'A3_PORTRAIT', 'A4', 'A5', 'CUSTOM'));

-- The decision itself, in four columns that are four expressions of one thing. A resolution-only
-- plan writes no edge and no value; a proportional shrink writes exactly one edge, exactly one
-- millimetre value, and resamples with BICUBIC_SHARPER and nothing else. The mapper refuses any
-- combination in which they disagree (§13).
ALTER TABLE ProcessingSession ADD COLUMN PrintPlanMode                 TEXT    NULL
    CHECK (PrintPlanMode IS NULL OR PrintPlanMode IN ('RESOLUTION_ONLY', 'PROPORTIONAL_SHRINK'));
ALTER TABLE ProcessingSession ADD COLUMN PrintPlanLimitingEdge         TEXT    NULL
    CHECK (PrintPlanLimitingEdge IS NULL OR PrintPlanLimitingEdge IN ('NONE', 'WIDTH', 'HEIGHT'));
ALTER TABLE ProcessingSession ADD COLUMN PrintPlanLimitingValueMm      REAL    NULL
    CHECK (PrintPlanLimitingValueMm IS NULL OR PrintPlanLimitingValueMm > 0);

-- Planning evidence that the selected edge fits the other bound. Emphatically not a Photoshop
-- target pair and not a result: no column here is named Actual*, because nothing in this slice
-- has read anything back from Photoshop (§20).
ALTER TABLE ProcessingSession ADD COLUMN PrintPlanProjectedPixelWidth  INTEGER NULL
    CHECK (PrintPlanProjectedPixelWidth  IS NULL OR PrintPlanProjectedPixelWidth  > 0);
ALTER TABLE ProcessingSession ADD COLUMN PrintPlanProjectedPixelHeight INTEGER NULL
    CHECK (PrintPlanProjectedPixelHeight IS NULL OR PrintPlanProjectedPixelHeight > 0);

-- Fixed at 300 by the accepted contract, and constrained to it so a row claiming another
-- production resolution cannot exist (MVP design §8.3).
ALTER TABLE ProcessingSession ADD COLUMN PrintPlanProductionDpi        INTEGER NULL
    CHECK (PrintPlanProductionDpi IS NULL OR PrintPlanProductionDpi = 300);

-- The neutral policy, never a Photoshop COM value. Infrastructure maps NONE and BICUBIC_SHARPER
-- to ResampleMethod when a production resize exists; the database stores PrintFlow's vocabulary.
--
-- This column also carries the completeness constraint, because a column CHECK in SQLite may
-- reference the whole row. The twelve columns below are all-or-nothing: a half-written plan is
-- the one shape §13 requires the database to refuse, since every missing piece is one a reader
-- would otherwise have to invent. PrintPlanLimitingValueMm is deliberately outside the group --
-- its absence is meaningful rather than missing, and the mapper pairs it with the mode.
ALTER TABLE ProcessingSession ADD COLUMN PrintPlanResizePolicy         TEXT    NULL
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
    );

-- The immutable attempt snapshot. Same columns, same rules, written once (§12).
ALTER TABLE ProcessingAttempt ADD COLUMN PrintPlanSourceRevisionId     TEXT    NULL;
ALTER TABLE ProcessingAttempt ADD COLUMN PrintPlanSourceSha256         TEXT    NULL
    CHECK (PrintPlanSourceSha256 IS NULL OR length(PrintPlanSourceSha256) = 64);
ALTER TABLE ProcessingAttempt ADD COLUMN PrintPlanSourcePixelWidth     INTEGER NULL
    CHECK (PrintPlanSourcePixelWidth  IS NULL OR PrintPlanSourcePixelWidth  > 0);
ALTER TABLE ProcessingAttempt ADD COLUMN PrintPlanSourcePixelHeight    INTEGER NULL
    CHECK (PrintPlanSourcePixelHeight IS NULL OR PrintPlanSourcePixelHeight > 0);
ALTER TABLE ProcessingAttempt ADD COLUMN PrintPlanMaxWidthMm           REAL    NULL
    CHECK (PrintPlanMaxWidthMm  IS NULL OR PrintPlanMaxWidthMm  > 0);
ALTER TABLE ProcessingAttempt ADD COLUMN PrintPlanMaxHeightMm          REAL    NULL
    CHECK (PrintPlanMaxHeightMm IS NULL OR PrintPlanMaxHeightMm > 0);
ALTER TABLE ProcessingAttempt ADD COLUMN PrintPlanLimitKind            TEXT    NULL
    CHECK (PrintPlanLimitKind IS NULL OR PrintPlanLimitKind IN
        ('A3_LANDSCAPE', 'A3_PORTRAIT', 'A4', 'A5', 'CUSTOM'));
ALTER TABLE ProcessingAttempt ADD COLUMN PrintPlanMode                 TEXT    NULL
    CHECK (PrintPlanMode IS NULL OR PrintPlanMode IN ('RESOLUTION_ONLY', 'PROPORTIONAL_SHRINK'));
ALTER TABLE ProcessingAttempt ADD COLUMN PrintPlanLimitingEdge         TEXT    NULL
    CHECK (PrintPlanLimitingEdge IS NULL OR PrintPlanLimitingEdge IN ('NONE', 'WIDTH', 'HEIGHT'));
ALTER TABLE ProcessingAttempt ADD COLUMN PrintPlanLimitingValueMm      REAL    NULL
    CHECK (PrintPlanLimitingValueMm IS NULL OR PrintPlanLimitingValueMm > 0);
ALTER TABLE ProcessingAttempt ADD COLUMN PrintPlanProjectedPixelWidth  INTEGER NULL
    CHECK (PrintPlanProjectedPixelWidth  IS NULL OR PrintPlanProjectedPixelWidth  > 0);
ALTER TABLE ProcessingAttempt ADD COLUMN PrintPlanProjectedPixelHeight INTEGER NULL
    CHECK (PrintPlanProjectedPixelHeight IS NULL OR PrintPlanProjectedPixelHeight > 0);
ALTER TABLE ProcessingAttempt ADD COLUMN PrintPlanProductionDpi        INTEGER NULL
    CHECK (PrintPlanProductionDpi IS NULL OR PrintPlanProductionDpi = 300);
ALTER TABLE ProcessingAttempt ADD COLUMN PrintPlanResizePolicy         TEXT    NULL
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
    );
