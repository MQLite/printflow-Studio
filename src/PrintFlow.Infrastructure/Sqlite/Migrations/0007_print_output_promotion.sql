-- Epic 11400 Part C2B: crash-safe promotion of an approved production TIFF into Approved\.
--
-- Reserving the Approved destination and copying the bytes into it is file-system work; recording
-- the approval is a database transaction. The two cannot be one atomic act, so the reservation is
-- persisted before the copy begins. A process that dies between the copy and the review commit
-- therefore leaves a record of the destination it had already claimed, and the second attempt at
-- that approval resumes into the same file instead of reserving a second name — which is what
-- keeps a crash from producing a `_02` duplicate of one approved TIFF (Part C2B §10, §11, §33).
--
-- Existing rows correctly read as having no promotion in flight.
ALTER TABLE PrintOutput ADD COLUMN PromotionReservedPath TEXT NULL;

-- Promotion moves a PrintOutput's file reference and nothing else. RelativePath is therefore the
-- one identity column that may change after the row is written, and the guard below states that
-- out loud rather than leaving it to the repository's UPDATE list: approval copies bytes that were
-- already validated, so an approval that altered the hash, the byte length, the source Revision or
-- the creation instant would be describing a different file (§8).
--
-- The counterpart for a Revision is Revision_Immutable_Update in 0001, which forbids RelativePath
-- changing at all. That difference is the file-location model: the Revision records what was
-- produced and where it was produced and never moves, while the PrintOutput carries where the
-- deliverable now lives (§4).
CREATE TRIGGER PrintOutput_Identity_Immutable
BEFORE UPDATE ON PrintOutput
WHEN  OLD.Sha256           <> NEW.Sha256
   OR OLD.ByteLength       <> NEW.ByteLength
   OR OLD.SourceRevisionId <> NEW.SourceRevisionId
   OR OLD.CreatedAtUtc     <> NEW.CreatedAtUtc
BEGIN
    SELECT RAISE(ABORT, 'PrintOutput identity columns are immutable; only its location and review state may change');
END;
