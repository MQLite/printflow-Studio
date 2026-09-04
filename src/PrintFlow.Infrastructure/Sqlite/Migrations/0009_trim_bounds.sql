-- SCRUM-11081: persist the crop geometry the deterministic trim already computes.
--
-- The alpha scan has always returned two rectangles, and the processor has always cropped to
-- the second of them. Both were then dropped on the floor between the processor result and the
-- attempt's closing transaction, so a Trim Revision could say how large it is but nothing could
-- say what part of the source it came from. These eight columns close that: the detected
-- graphic extent, and the rectangle actually cut out.
--
--   ContentBounds  -> "what did the alpha scan find", before any operator safety margin. The
--                     original detected graphic extent, and the value SCRUM-11094 needs on
--                     screen while a print size is being decided.
--
--   AppliedBounds  -> "what was actually cropped": the content rectangle grown by THIS
--                     attempt's TrimMargin and clamped to the source canvas. The produced PNG
--                     is this rectangle's width x height, by construction.
--
-- On ProcessingAttempt and nowhere else. The session already records the margin the NEXT run
-- would use; geometry is not a pending decision but a fact about one produced file, and an
-- operator who re-runs at a wider margin must not retrospectively relabel what the first
-- attempt cropped. Migration 0002 put the margin here for exactly that reason (Part C3 §15),
-- and the rectangle it produced belongs beside it.
--
-- Typed integer columns rather than a JSON blob, following 0002. Four edges per rectangle is
-- what a support query wants to read directly, and burying operator-visible geometry in an
-- opaque string would make "which pixels were kept?" unanswerable without a parser.
--
-- The coordinate convention is TrimBounds's, unchanged and unconverted: Left/Top inclusive,
-- Right/Bottom EXCLUSIVE -- the half-open [left, top -> right, bottom) spelling of
-- Int32Rect(x, y, width, height), so width is Right - Left with no correction anywhere. A
-- storage-only inclusive convention was rejected for the reason TrimBounds itself rejects one:
-- every conversion is a place for a missed +1 to silently clip a column of the operator's
-- artwork.
--
-- Plain ALTER TABLE ADD COLUMN, so no table is rebuilt and no existing constraint is touched.
--
-- Nullable with no default, and deliberately not backfilled. NULL on an attempt means "this
-- attempt established no trim geometry" -- an adapter call, a promotion, a manual crop, a trim
-- that ended in ManualCropRequired, and every attempt written before this migration. It never
-- means "the whole canvas was kept": the whole canvas is a rectangle these columns can state,
-- so the two readings must not collide.
--
-- Historical rows stay NULL because their geometry is genuinely unrecoverable. The output PNG's
-- dimensions give the applied rectangle's SIZE but not its origin, and nothing records where in
-- the source the content sat; inferring an origin from the size difference and the session's
-- current margin would invent a rectangle nobody measured. A NULL that reads "not recorded" is
-- worth more than a number that reads "measured" and was not.

-- Coordinates are pixel indices, so they are never negative, and each rectangle holds at least
-- one pixel -- the same rule TrimBounds.FromEdges enforces, restated here so a row that no code
-- path can honestly interpret cannot be written by any route.
ALTER TABLE ProcessingAttempt ADD COLUMN TrimContentLeft   INTEGER NULL CHECK (TrimContentLeft   IS NULL OR TrimContentLeft   >= 0);
ALTER TABLE ProcessingAttempt ADD COLUMN TrimContentTop    INTEGER NULL CHECK (TrimContentTop    IS NULL OR TrimContentTop    >= 0);
ALTER TABLE ProcessingAttempt ADD COLUMN TrimContentRight  INTEGER NULL CHECK (TrimContentRight  IS NULL OR TrimContentRight  >  0);
ALTER TABLE ProcessingAttempt ADD COLUMN TrimContentBottom INTEGER NULL CHECK (TrimContentBottom IS NULL OR TrimContentBottom >  0);

ALTER TABLE ProcessingAttempt ADD COLUMN TrimAppliedLeft   INTEGER NULL CHECK (TrimAppliedLeft   IS NULL OR TrimAppliedLeft   >= 0);
ALTER TABLE ProcessingAttempt ADD COLUMN TrimAppliedTop    INTEGER NULL CHECK (TrimAppliedTop    IS NULL OR TrimAppliedTop    >= 0);
ALTER TABLE ProcessingAttempt ADD COLUMN TrimAppliedRight  INTEGER NULL CHECK (TrimAppliedRight  IS NULL OR TrimAppliedRight  >  0);
ALTER TABLE ProcessingAttempt ADD COLUMN TrimAppliedBottom INTEGER NULL CHECK (TrimAppliedBottom IS NULL OR TrimAppliedBottom >  0);

-- The all-or-nothing rule, and the ordering rules a per-column CHECK cannot state.
--
-- Triggers rather than a table CHECK because SQLite cannot add one to an existing table, and
-- rebuilding ProcessingAttempt to gain a constraint that a BEFORE INSERT/UPDATE trigger states
-- exactly as well would mean copying every attempt row and re-establishing four foreign keys
-- for no additional guarantee.
--
-- Three things are refused:
--   * a half-written pair -- one rectangle without the other, or a rectangle missing an edge;
--   * a rectangle with no pixels in it (right <= left, or bottom <= top);
--   * an applied rectangle smaller than the content it was expanded from. A margin only ever
--     grows the crop and the clamp only ever stops it at the canvas edge, so an applied
--     rectangle that cuts INSIDE the detected content is not a trim this product performed.
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

-- Geometry, once written, is as immutable as the rest of an attempt's history. The closing
-- transaction writes it; nothing afterwards may edit or erase it, so a return upstream that
-- supersedes an attempt leaves that attempt's rectangles exactly as they were, and a retry at a
-- different margin has to be a new attempt rather than an edit to this one.
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
