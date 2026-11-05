-- Pending settings default to conservative filtering. Historical attempts remain NULL:
-- their output was produced by the exact alpha > 0 rule, never by today's default.
ALTER TABLE ProcessingSession ADD COLUMN IgnoreSmallTrimNoise INTEGER NOT NULL DEFAULT 1
    CHECK (IgnoreSmallTrimNoise IN (0, 1));
ALTER TABLE ProcessingAttempt ADD COLUMN IgnoreSmallTrimNoise INTEGER NULL
    CHECK (IgnoreSmallTrimNoise IS NULL OR IgnoreSmallTrimNoise IN (0, 1));
