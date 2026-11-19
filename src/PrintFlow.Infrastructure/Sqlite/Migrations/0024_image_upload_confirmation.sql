-- Record who authorised the exact Revision and hash already bound by migration 0023.
ALTER TABLE ProcessingAttempt ADD COLUMN ImageUploadConfirmedAtUtc TEXT NULL;
ALTER TABLE ProcessingAttempt ADD COLUMN ImageUploadConfirmedBy TEXT NULL
    CHECK (
        (ImageUploadConfirmedBy IS NULL AND ImageUploadConfirmedAtUtc IS NULL)
        OR (ImageUploadConfirmedBy IS NOT NULL AND ImageUploadConfirmedAtUtc IS NOT NULL
            AND ImageProcessingMethod IS NOT NULL
            AND ImageProcessingMethod IN ('OpenAiRestore', 'OpenAiCutout', 'OpenAiCombined'))
    );

CREATE TRIGGER ProcessingAttempt_ImageUploadConfirmation_Immutable
BEFORE UPDATE OF ImageUploadConfirmedAtUtc, ImageUploadConfirmedBy ON ProcessingAttempt
WHEN NEW.ImageUploadConfirmedAtUtc IS NOT OLD.ImageUploadConfirmedAtUtc
    OR NEW.ImageUploadConfirmedBy IS NOT OLD.ImageUploadConfirmedBy
BEGIN
    SELECT RAISE(ABORT, 'Image upload confirmation is immutable');
END;
