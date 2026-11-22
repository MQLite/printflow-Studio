-- An overwrite can move the imported original only after its file identity is durable.
-- Its volume and creation time are shared with the staging checkpoint for this attempt.
ALTER TABLE DeliveryAttempt ADD COLUMN OriginalFileId TEXT NULL CHECK (
    OriginalFileId IS NULL OR (length(OriginalFileId) = 32 AND StagingCreationUtc IS NOT NULL));

CREATE TRIGGER DeliveryAttempt_OriginalFileImmutable
BEFORE UPDATE OF OriginalFileId ON DeliveryAttempt
WHEN OLD.OriginalFileId IS NOT NULL OR OLD.State <> 'Staging' OR NEW.State <> 'ReadyToPublish'
BEGIN SELECT RAISE(ABORT, 'Original file identity belongs to the ready checkpoint'); END;
