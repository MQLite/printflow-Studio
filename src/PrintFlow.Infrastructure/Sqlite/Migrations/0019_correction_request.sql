-- SCRUM-11148 (owner decision D1). One additive table binding a colleague-correction request to
-- the exact background-removal result handed out (R) and its input (U), the prepared package and
-- the returned Revision. No existing table, column, constraint or value changes, and no legacy
-- handoff is backfilled: a session without a row keeps every existing behaviour.
CREATE TABLE CorrectionRequest (
    Id                  TEXT PRIMARY KEY NOT NULL,
    SessionId           TEXT NOT NULL REFERENCES ProcessingSession(Id) ON DELETE CASCADE,
    StepKind            TEXT NOT NULL CHECK (StepKind = 'BackgroundRemoval'),
    HandedOutRevisionId TEXT NOT NULL REFERENCES Revision(Id),
    HandedOutSha256     TEXT NOT NULL CHECK (length(HandedOutSha256) = 64),
    ReferenceRevisionId TEXT NOT NULL REFERENCES Revision(Id),
    ReferenceSha256     TEXT NOT NULL CHECK (length(ReferenceSha256) = 64),
    FolderRelativePath  TEXT NOT NULL UNIQUE,
    ReferenceFileName   TEXT NOT NULL CHECK (length(ReferenceFileName) > 0),
    WorkingFileName     TEXT NOT NULL CHECK (length(WorkingFileName) > 0),
    SuggestedReturnName TEXT NOT NULL CHECK (length(SuggestedReturnName) > 0),
    Note                TEXT NULL,
    EffectiveReason     TEXT NOT NULL CHECK (length(trim(EffectiveReason)) > 0),
    Status              TEXT NOT NULL CHECK (Status IN ('PREPARING', 'READY', 'RETURNED', 'SUPERSEDED')),
    CreatedAtUtc        TEXT NOT NULL,
    ReadyAtUtc          TEXT NULL,
    ClosedAtUtc         TEXT NULL,
    LastImportAttemptId TEXT NULL REFERENCES ProcessingAttempt(Id),
    ResultRevisionId    TEXT NULL REFERENCES Revision(Id),
    CHECK (HandedOutRevisionId <> ReferenceRevisionId),
    CHECK (ReferenceFileName <> WorkingFileName),
    CHECK ((Status = 'PREPARING' AND ReadyAtUtc IS NULL AND ClosedAtUtc IS NULL
             AND LastImportAttemptId IS NULL AND ResultRevisionId IS NULL)
        OR (Status = 'READY' AND ReadyAtUtc IS NOT NULL AND ClosedAtUtc IS NULL AND ResultRevisionId IS NULL)
        OR (Status = 'RETURNED' AND ReadyAtUtc IS NOT NULL AND ClosedAtUtc IS NOT NULL
             AND LastImportAttemptId IS NOT NULL AND ResultRevisionId IS NOT NULL)
        OR (Status = 'SUPERSEDED' AND ClosedAtUtc IS NOT NULL AND ResultRevisionId IS NULL))
);

-- At most one open (PREPARING or READY) request per session. "Open" here is storage only;
-- whether a READY row still grants anything is decided by CorrectionRequestEligibility.
CREATE UNIQUE INDEX UX_CorrectionRequest_OneOpenPerSession ON CorrectionRequest(SessionId)
    WHERE Status IN ('PREPARING', 'READY');
CREATE INDEX IX_CorrectionRequest_Session ON CorrectionRequest(SessionId, CreatedAtUtc);

-- Identity is immutable, following Revision_Immutable_Update. ReadyAtUtc is immutable once set,
-- and a closed row (RETURNED or SUPERSEDED) cannot change at all.
CREATE TRIGGER CorrectionRequest_Identity_Immutable BEFORE UPDATE ON CorrectionRequest
WHEN OLD.Id <> NEW.Id OR OLD.SessionId <> NEW.SessionId OR OLD.StepKind <> NEW.StepKind
  OR OLD.HandedOutRevisionId <> NEW.HandedOutRevisionId OR OLD.HandedOutSha256 <> NEW.HandedOutSha256
  OR OLD.ReferenceRevisionId <> NEW.ReferenceRevisionId OR OLD.ReferenceSha256 <> NEW.ReferenceSha256
  OR OLD.FolderRelativePath <> NEW.FolderRelativePath
  OR OLD.ReferenceFileName <> NEW.ReferenceFileName OR OLD.WorkingFileName <> NEW.WorkingFileName
  OR OLD.SuggestedReturnName <> NEW.SuggestedReturnName
  OR OLD.Note IS NOT NEW.Note OR OLD.EffectiveReason <> NEW.EffectiveReason
  OR OLD.CreatedAtUtc <> NEW.CreatedAtUtc
  OR (OLD.ReadyAtUtc IS NOT NULL AND OLD.ReadyAtUtc IS NOT NEW.ReadyAtUtc)
  OR (OLD.Status IN ('RETURNED', 'SUPERSEDED') AND (NEW.Status IS NOT OLD.Status
      OR NEW.ClosedAtUtc IS NOT OLD.ClosedAtUtc OR NEW.LastImportAttemptId IS NOT OLD.LastImportAttemptId
      OR NEW.ResultRevisionId IS NOT OLD.ResultRevisionId))
BEGIN SELECT RAISE(ABORT, 'Correction request identity is immutable'); END;

CREATE TRIGGER CorrectionRequest_ForwardOnly BEFORE UPDATE OF Status ON CorrectionRequest
WHEN NOT ((OLD.Status = 'PREPARING' AND NEW.Status IN ('PREPARING', 'READY', 'SUPERSEDED'))
       OR (OLD.Status = 'READY' AND NEW.Status IN ('READY', 'RETURNED', 'SUPERSEDED'))
       OR (OLD.Status IN ('RETURNED', 'SUPERSEDED') AND NEW.Status = OLD.Status))
BEGIN SELECT RAISE(ABORT, 'Correction request status cannot move backwards'); END;
