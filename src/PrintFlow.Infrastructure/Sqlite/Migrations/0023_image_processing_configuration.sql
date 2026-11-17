-- Legacy session and attempt rows retain NULL so compatibility does not create new history.
ALTER TABLE ProcessingSession ADD COLUMN ImageProcessingSelection TEXT NULL
    CHECK (ImageProcessingSelection IS NULL OR json_valid(ImageProcessingSelection));
ALTER TABLE ProcessingSession ADD COLUMN ImageProcessingDefaultsDeferred INTEGER NOT NULL DEFAULT 0
    CHECK (ImageProcessingDefaultsDeferred IN (0, 1));

ALTER TABLE ProcessingAttempt ADD COLUMN ImageProcessingMethod TEXT NULL;
ALTER TABLE ProcessingAttempt ADD COLUMN ImageProcessingAlgorithmVersion TEXT NULL;
ALTER TABLE ProcessingAttempt ADD COLUMN ImageProcessingInputSha256 TEXT NULL
    CHECK (
        (ImageProcessingMethod IS NULL AND ImageProcessingInputSha256 IS NULL AND ImageProcessingAlgorithmVersion IS NULL)
        OR (ImageProcessingMethod IS NOT NULL AND ImageProcessingInputSha256 IS NOT NULL
            AND length(ImageProcessingInputSha256) = 64
            AND ImageProcessingInputSha256 NOT GLOB '*[^0-9A-F]*' AND InputRevisionId IS NOT NULL)
    );

CREATE TRIGGER ProcessingAttempt_ImageProcessingDetails_Immutable
BEFORE UPDATE OF ImageProcessingMethod, ImageProcessingInputSha256, ImageProcessingAlgorithmVersion ON ProcessingAttempt
WHEN NEW.ImageProcessingMethod IS NOT OLD.ImageProcessingMethod
    OR NEW.ImageProcessingInputSha256 IS NOT OLD.ImageProcessingInputSha256
    OR NEW.ImageProcessingAlgorithmVersion IS NOT OLD.ImageProcessingAlgorithmVersion
BEGIN
    SELECT RAISE(ABORT, 'Image processing attempt evidence is immutable');
END;
