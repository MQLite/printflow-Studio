-- SCRUM-11100: widen the operation vocabulary without rewriting historical revisions.
-- MigrationRunner performs the transactional rebuild with FK checking suspended.
CREATE TABLE Revision_Pdf (
    Id                 TEXT PRIMARY KEY NOT NULL,
    SessionId          TEXT NOT NULL REFERENCES ProcessingSession(Id) ON DELETE CASCADE,
    SourceRevisionId   TEXT NULL REFERENCES Revision(Id),
    Operation          TEXT NOT NULL CHECK (Operation IN
                           ('IMPORT', 'ENHANCE', 'REMOVE_BACKGROUND', 'TRIM',
                            'PROMOTE_APPROVED', 'MANUAL_IMPORT', 'PHOTOSHOP_OUTPUT', 'PREPARE_PSD', 'PREPARE_PDF')),
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
INSERT INTO Revision_Pdf SELECT * FROM Revision;
DROP TABLE Revision;
ALTER TABLE Revision_Pdf RENAME TO Revision;
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
CREATE TABLE PdfInspection (
    AttemptId TEXT PRIMARY KEY NOT NULL REFERENCES ProcessingAttempt(Id) ON DELETE CASCADE,
    IsReadable INTEGER NOT NULL,
    IsEncrypted INTEGER NULL,
    PageCount INTEGER NULL CHECK (PageCount >= 0),
    PreparedPageNumber INTEGER NULL CHECK (PreparedPageNumber IS NULL OR (PreparedPageNumber = 1 AND PageCount = 1)),
    MediaX REAL NULL,
    MediaY REAL NULL,
    MediaWidth REAL NULL,
    MediaHeight REAL NULL,
    CropX REAL NULL,
    CropY REAL NULL,
    CropWidth REAL NULL,
    CropHeight REAL NULL,
    RotationDegrees INTEGER NULL,
    PageWidth REAL NULL,
    PageHeight REAL NULL,
    RequestedRasterDpi INTEGER NOT NULL CHECK (RequestedRasterDpi = 300),
    PixelWidth INTEGER NULL,
    PixelHeight INTEGER NULL,
    HasTransparency INTEGER NULL,
    Provider TEXT NOT NULL
);
CREATE TRIGGER PdfInspection_NoUpdate BEFORE UPDATE ON PdfInspection
BEGIN SELECT RAISE(ABORT, 'PDF inspection is immutable'); END;
