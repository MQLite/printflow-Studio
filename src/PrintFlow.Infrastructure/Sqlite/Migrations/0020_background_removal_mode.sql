-- NULL preserves the legacy Automatic recogniser without rewriting historical rows.
ALTER TABLE ProcessingSession ADD COLUMN BackgroundRemovalMode TEXT NULL
    CHECK (BackgroundRemovalMode IS NULL OR BackgroundRemovalMode IN
        ('AUTOMATIC', 'PORTRAIT_PETS', 'PRODUCTS', 'LOGOS_STAMPS'));
ALTER TABLE ProcessingAttempt ADD COLUMN BackgroundRemovalMode TEXT NULL
    CHECK (BackgroundRemovalMode IS NULL OR BackgroundRemovalMode IN
        ('AUTOMATIC', 'PORTRAIT_PETS', 'PRODUCTS', 'LOGOS_STAMPS'));

-- The opening transaction owns the recogniser, including a legacy NULL.
CREATE TRIGGER ProcessingAttempt_BackgroundRemovalMode_Immutable
BEFORE UPDATE OF BackgroundRemovalMode ON ProcessingAttempt
WHEN OLD.BackgroundRemovalMode IS NOT NEW.BackgroundRemovalMode
BEGIN SELECT RAISE(ABORT, 'Background removal attempt mode is immutable'); END;
