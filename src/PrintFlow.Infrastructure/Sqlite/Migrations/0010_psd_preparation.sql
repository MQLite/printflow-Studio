-- SCRUM-11099: widen the operation vocabulary without rewriting historical revisions.
-- MigrationRunner performs the transactional rebuild with FK checking suspended.
CREATE TABLE Revision_Psd (
    Id                 TEXT PRIMARY KEY NOT NULL,
    SessionId          TEXT NOT NULL REFERENCES ProcessingSession(Id) ON DELETE CASCADE,
    SourceRevisionId   TEXT NULL REFERENCES Revision(Id),
    Operation          TEXT NOT NULL CHECK (Operation IN
                           ('IMPORT', 'ENHANCE', 'REMOVE_BACKGROUND', 'TRIM',
                            'PROMOTE_APPROVED', 'MANUAL_IMPORT', 'PHOTOSHOP_OUTPUT', 'PREPARE_PSD')),
    RelativePath       TEXT NOT NULL,
    Format             TEXT NOT NULL,
    ByteLength         INTEGER NOT NULL CHECK (ByteLength > 0),
    Sha256             TEXT NOT NULL CHECK (length(Sha256) = 64),
    PixelWidth         INTEGER NULL,
    PixelHeight        INTEGER NULL,
    DpiX               REAL NULL,
    DpiY               REAL NULL,
    ColourMode         TEXT NOT NULL,
    HasAlpha           INTEGER NULL CHECK (HasAlpha IS NULL OR HasAlpha IN (0, 1)),
    CreatedAtUtc       TEXT NOT NULL,
    IsValid            INTEGER NOT NULL DEFAULT 1 CHECK (IsValid IN (0, 1)),
    InvalidatedAtUtc   TEXT NULL,
    InvalidationReason TEXT NULL CHECK (InvalidationReason IS NULL OR InvalidationReason IN
                           ('SUPERSEDED', 'UPSTREAM_CHANGED', 'FILE_MUTATED', 'REJECTED', 'SESSION_RESET')),
    ReviewState        TEXT NOT NULL DEFAULT 'NOT_REVIEWED'
                           CHECK (ReviewState IN ('NOT_REVIEWED', 'APPROVED', 'REJECTED')),
    UNIQUE (SessionId, RelativePath)
);
INSERT INTO Revision_Psd SELECT * FROM Revision;
DROP TABLE Revision;
ALTER TABLE Revision_Psd RENAME TO Revision;
CREATE INDEX IX_Revision_Session ON Revision(SessionId);
CREATE INDEX IX_Revision_Source ON Revision(SourceRevisionId);
CREATE TRIGGER Revision_Immutable_Update
BEFORE UPDATE ON Revision
WHEN  OLD.Sha256           <> NEW.Sha256
   OR OLD.RelativePath     <> NEW.RelativePath
   OR OLD.SourceRevisionId IS NOT NEW.SourceRevisionId
   OR OLD.Operation        <> NEW.Operation
   OR OLD.ByteLength       <> NEW.ByteLength
   OR OLD.CreatedAtUtc     <> NEW.CreatedAtUtc
BEGIN
    SELECT RAISE(ABORT, 'Revision identity columns are immutable');
END;
CREATE TABLE PsdInspection (
    AttemptId TEXT PRIMARY KEY NOT NULL REFERENCES ProcessingAttempt(Id) ON DELETE CASCADE,
    PixelWidth INTEGER NOT NULL CHECK (PixelWidth > 0),
    PixelHeight INTEGER NOT NULL CHECK (PixelHeight > 0),
    OriginalMode TEXT NOT NULL,
    BitDepth INTEGER NOT NULL CHECK (BitDepth > 0),
    HasRealMergedData INTEGER NOT NULL CHECK (HasRealMergedData IN (0,1)),
    HasTransparency INTEGER NULL CHECK (HasTransparency IS NULL OR HasTransparency IN (0,1)),
    PhotoshopVersion TEXT NOT NULL
);
CREATE TABLE PsdChannel (
    AttemptId TEXT NOT NULL REFERENCES PsdInspection(AttemptId) ON DELETE CASCADE,
    Ordinal INTEGER NOT NULL CHECK (Ordinal >= 0),
    Name TEXT NOT NULL,
    Kind TEXT NOT NULL,
    PRIMARY KEY (AttemptId, Ordinal)
);
CREATE TRIGGER PsdInspection_NoUpdate BEFORE UPDATE ON PsdInspection
BEGIN SELECT RAISE(ABORT, 'PSD inspection is immutable'); END;
CREATE TRIGGER PsdChannel_NoUpdate BEFORE UPDATE ON PsdChannel
BEGIN SELECT RAISE(ABORT, 'PSD channel inspection is immutable'); END;
