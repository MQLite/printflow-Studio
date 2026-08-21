-- Epic 11200 Part C3 §14: make the deterministic trim's parameters auditable.
--
-- Part A recorded that a Trim Revision could not be traced back to the settings that produced
-- it. Two additions close that, and they answer two different questions:
--
--   ProcessingSession  -> "what will the NEXT trim run use". A pending decision, changed
--                         whenever the operator changes their mind, and needed only so the
--                         choice survives a restart the way the print size and the W1 branch
--                         already do.
--
--   ProcessingAttempt  -> "how was THIS Trim Revision produced". Written once with the
--                         attempt's opening transaction, before any pixel work, and never
--                         updated afterwards — the attempt upsert deliberately leaves these
--                         columns out of its DO UPDATE clause, so a retry with a different
--                         margin cannot rewrite what the first attempt did (§15).
--
-- Typed columns rather than a JSON blob: a trim margin is four small integers and a mode, all
-- of which a support query wants to read directly (§14).
--
-- Nullable with no default on both tables. NULL on an attempt means "this attempt had no trim
-- margin" — an adapter call, a promotion, or a manual crop, whose rectangle a human drew and
-- to which an automatic margin means nothing (§17). It never means "the default was used", and
-- rows written before this migration correctly read as NULL rather than claiming a setting
-- nobody recorded.

-- The non-negativity rule is stated here as well as in TrimMargin's factories, deliberately.
-- "A margin expands the crop, it never crops further in" is the reason a negative value is
-- refused rather than read as "crop deeper" (Part B §5), and a stored -4 would be a row that
-- no code path can honestly interpret.

ALTER TABLE ProcessingSession ADD COLUMN TrimMode         TEXT    NULL CHECK (TrimMode IS NULL OR TrimMode IN
                                                                       ('TIGHT_CROP', 'UNIFORM_MARGIN', 'EDGE_SPECIFIC_MARGIN'));
ALTER TABLE ProcessingSession ADD COLUMN TrimMarginTop    INTEGER NULL CHECK (TrimMarginTop    IS NULL OR TrimMarginTop    >= 0);
ALTER TABLE ProcessingSession ADD COLUMN TrimMarginRight  INTEGER NULL CHECK (TrimMarginRight  IS NULL OR TrimMarginRight  >= 0);
ALTER TABLE ProcessingSession ADD COLUMN TrimMarginBottom INTEGER NULL CHECK (TrimMarginBottom IS NULL OR TrimMarginBottom >= 0);
ALTER TABLE ProcessingSession ADD COLUMN TrimMarginLeft   INTEGER NULL CHECK (TrimMarginLeft   IS NULL OR TrimMarginLeft   >= 0);

ALTER TABLE ProcessingAttempt ADD COLUMN TrimMode         TEXT    NULL CHECK (TrimMode IS NULL OR TrimMode IN
                                                                       ('TIGHT_CROP', 'UNIFORM_MARGIN', 'EDGE_SPECIFIC_MARGIN'));
ALTER TABLE ProcessingAttempt ADD COLUMN TrimMarginTop    INTEGER NULL CHECK (TrimMarginTop    IS NULL OR TrimMarginTop    >= 0);
ALTER TABLE ProcessingAttempt ADD COLUMN TrimMarginRight  INTEGER NULL CHECK (TrimMarginRight  IS NULL OR TrimMarginRight  >= 0);
ALTER TABLE ProcessingAttempt ADD COLUMN TrimMarginBottom INTEGER NULL CHECK (TrimMarginBottom IS NULL OR TrimMarginBottom >= 0);
ALTER TABLE ProcessingAttempt ADD COLUMN TrimMarginLeft   INTEGER NULL CHECK (TrimMarginLeft   IS NULL OR TrimMarginLeft   >= 0);
