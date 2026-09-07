-- File lifetime is explicit. FormerWorkingPath is provenance, not live file authority.
-- ReleasedAt marks permission to remove rejected Meitu comparison bytes, not a claim
-- that an external filesystem operation has already succeeded.
ALTER TABLE Revision ADD COLUMN FormerWorkingPath TEXT NULL;
ALTER TABLE Revision ADD COLUMN RetentionReleasedAtUtc TEXT NULL;

CREATE TRIGGER Revision_Retention_Insert
BEFORE INSERT ON Revision
WHEN NEW.FormerWorkingPath IS NOT NULL OR NEW.RetentionReleasedAtUtc IS NOT NULL
BEGIN
    SELECT RAISE(ABORT, 'New Revisions cannot fabricate retention history');
END;

DROP TRIGGER Revision_Immutable_Update;
CREATE TRIGGER Revision_Immutable_Update
BEFORE UPDATE ON Revision
WHEN OLD.Id IS NOT NEW.Id OR OLD.SessionId IS NOT NEW.SessionId
  OR OLD.Sha256 IS NOT NEW.Sha256
  OR OLD.SourceRevisionId IS NOT NEW.SourceRevisionId
  OR OLD.Operation IS NOT NEW.Operation
  OR OLD.ByteLength IS NOT NEW.ByteLength
  OR OLD.CreatedAtUtc IS NOT NEW.CreatedAtUtc
  OR OLD.Format IS NOT NEW.Format
  OR OLD.PixelWidth IS NOT NEW.PixelWidth OR OLD.PixelHeight IS NOT NEW.PixelHeight
  OR OLD.DpiX IS NOT NEW.DpiX OR OLD.DpiY IS NOT NEW.DpiY
  OR OLD.ColourMode IS NOT NEW.ColourMode OR OLD.HasAlpha IS NOT NEW.HasAlpha
BEGIN
    SELECT RAISE(ABORT, 'Revision identity and pixel facts are immutable');
END;

CREATE TRIGGER Revision_Retention_Location
BEFORE UPDATE ON Revision
WHEN (OLD.RelativePath IS NOT NEW.RelativePath OR OLD.FormerWorkingPath IS NOT NEW.FormerWorkingPath)
 AND NOT (
    OLD.FormerWorkingPath IS NULL AND NEW.FormerWorkingPath IS OLD.RelativePath
    AND OLD.RetentionReleasedAtUtc IS NULL AND NEW.RetentionReleasedAtUtc IS NULL
    AND EXISTS (SELECT 1 FROM ProcessingSession s WHERE s.Id = OLD.SessionId
      AND s.State = 'COMPLETED' AND s.CompletedAtUtc IS NOT NULL
      AND substr(OLD.RelativePath, 1, length(s.WorkspacePath || '/Working/')) = s.WorkspacePath || '/Working/'
      AND substr(NEW.RelativePath, 1, length(s.WorkspacePath || '/Revisions/' || OLD.Id || '/'))
          = s.WorkspacePath || '/Revisions/' || OLD.Id || '/')
    AND instr(NEW.RelativePath, '/../') = 0 AND instr(NEW.RelativePath, '\') = 0
    AND NOT EXISTS (SELECT 1 FROM ProcessingAttempt a WHERE a.SessionId = OLD.SessionId AND a.ResultStatus = 'RUNNING')
 )
BEGIN
    SELECT RAISE(ABORT, 'Only completed-session retention may relocate a Working Revision');
END;

CREATE TRIGGER Revision_Retention_Release
BEFORE UPDATE ON Revision
WHEN OLD.RetentionReleasedAtUtc IS NOT NEW.RetentionReleasedAtUtc
 AND NOT (
    OLD.RetentionReleasedAtUtc IS NULL AND NEW.RetentionReleasedAtUtc IS NOT NULL
    AND OLD.Operation IN ('ENHANCE', 'REMOVE_BACKGROUND')
    AND EXISTS (SELECT 1 FROM ReviewDecision r WHERE r.SubjectId = OLD.Id AND r.Decision = 'REJECTED' AND r.ReviewedSha256 = OLD.Sha256)
    AND NEW.IsValid = 0 AND NEW.ReviewState = 'REJECTED'
    AND OLD.RelativePath = NEW.RelativePath AND OLD.FormerWorkingPath IS NULL
    AND EXISTS (SELECT 1 FROM ProcessingSession s WHERE s.Id = OLD.SessionId
        AND s.State = 'COMPLETED' AND s.CompletedAtUtc IS NOT NULL)
    AND NOT EXISTS (SELECT 1 FROM SessionStep s WHERE s.SessionId = OLD.SessionId AND s.CurrentRevisionId = OLD.Id)
    AND NOT EXISTS (SELECT 1 FROM ReviewDecision r WHERE r.SubjectId = OLD.Id AND r.Decision = 'APPROVED')
    AND NOT EXISTS (SELECT 1 FROM PrintOutput o WHERE o.SourceRevisionId = OLD.Id OR o.Id = OLD.Id)
    AND NOT EXISTS (SELECT 1 FROM ProcessingAttempt a WHERE a.SessionId = OLD.SessionId AND a.ResultStatus = 'RUNNING')
 )
BEGIN
    SELECT RAISE(ABORT, 'Only rejected Meitu comparison files may expire at completion');
END;

CREATE TRIGGER Revision_Retention_NoRevive
BEFORE UPDATE ON Revision
WHEN OLD.RetentionReleasedAtUtc IS NOT NULL AND (NEW.IsValid <> 0 OR NEW.ReviewState <> 'REJECTED')
BEGIN
    SELECT RAISE(ABORT, 'Expired comparison history cannot become a usable Revision');
END;

CREATE TRIGGER ReviewDecision_Retention_NoApproval
BEFORE INSERT ON ReviewDecision
WHEN NEW.Decision = 'APPROVED' AND NEW.SubjectKind = 'REVISION'
 AND EXISTS (SELECT 1 FROM Revision r WHERE r.Id = NEW.SubjectId AND r.RetentionReleasedAtUtc IS NOT NULL)
BEGIN
    SELECT RAISE(ABORT, 'Expired comparison bytes cannot be approved');
END;
