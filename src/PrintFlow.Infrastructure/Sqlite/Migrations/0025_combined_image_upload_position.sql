-- Each visit to combined Background Removal requires its own explicit upload confirmation.
ALTER TABLE SessionStep ADD COLUMN CombinedUploadPositionId TEXT NULL
    CHECK (CombinedUploadPositionId IS NULL OR
        (StepKind = 'BackgroundRemoval' AND State = 'WAITING' AND length(CombinedUploadPositionId) = 36));
