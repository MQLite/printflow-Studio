-- PrintFlow Studio — initial schema (Epic 11100 Task 11108; plan §11).
-- Applied inside one transaction by MigrationRunner, followed by PRAGMA user_version = 1
-- in the same transaction. Forward-only: there is no corresponding "down" script.

CREATE TABLE SchemaMigration (
    Version      INTEGER PRIMARY KEY NOT NULL,
    Name         TEXT NOT NULL,
    AppliedAtUtc TEXT NOT NULL,
    ScriptSha256 TEXT NOT NULL
);

CREATE TABLE Setting (
    Key   TEXT PRIMARY KEY NOT NULL,
    Value TEXT NOT NULL
);

CREATE TABLE ProcessingSession (
    Id                     TEXT PRIMARY KEY NOT NULL,
    WorkflowType           TEXT NOT NULL CHECK (WorkflowType IN
                               ('PREPARE_ASSET', 'PREPARE_CUSTOMER_DESIGN', 'GENERATE_PRINT_TIFF')),
    OutputName             TEXT NOT NULL,
    CurrentStep            TEXT NOT NULL,
    State                  TEXT NOT NULL CHECK (State IN ('ACTIVE', 'HANDED_OFF', 'COMPLETED', 'ABANDONED')),
    WorkspacePath          TEXT NOT NULL UNIQUE,
    CreatedAtUtc           TEXT NOT NULL,
    UpdatedAtUtc           TEXT NOT NULL,
    CompletedAtUtc         TEXT NULL,
    HandedOffAtUtc         TEXT NULL,
    HandOffReason          TEXT NULL,
    AbandonedAtUtc         TEXT NULL,
    AbandonReason          TEXT NULL,
    -- Operator decisions that are not tied to any single Revision and must still survive a
    -- restart, so a reloaded WorkflowSnapshot is byte-for-byte the same as before shutdown
    -- (Epic 11100 plan §17.3 "restart/resume").
    DimensionsWidthMm      REAL NULL,
    DimensionsHeightMm     REAL NULL,
    DimensionsPixelWidth   INTEGER NULL,
    DimensionsPixelHeight  INTEGER NULL,
    DimensionsPreset       TEXT NULL,
    WhiteUnderbaseBranch   TEXT NULL CHECK (WhiteUnderbaseBranch IS NULL OR WhiteUnderbaseBranch IN
                               ('W1_0PX', 'W1_1PX', 'W1_2PX'))
);

CREATE TABLE SessionStep (
    SessionId          TEXT NOT NULL REFERENCES ProcessingSession(Id) ON DELETE CASCADE,
    StepKind           TEXT NOT NULL,
    Ordinal            INTEGER NOT NULL,
    State              TEXT NOT NULL CHECK (State IN
                           ('WAITING', 'PROCESSING', 'REVIEW_REQUIRED', 'APPROVED',
                            'RETRY_REQUIRED', 'SKIPPED', 'FAILED', 'INTERRUPTED')),
    CurrentRevisionId  TEXT NULL,
    CurrentRevisionSha TEXT NULL,
    SkipReason         TEXT NULL,
    AttemptCount       INTEGER NOT NULL DEFAULT 0,
    EnteredStateAtUtc  TEXT NOT NULL,
    PRIMARY KEY (SessionId, StepKind)
);
CREATE INDEX IX_SessionStep_Session ON SessionStep(SessionId);

CREATE TABLE InputSnapshot (
    Id                 TEXT PRIMARY KEY NOT NULL,
    SessionId          TEXT NOT NULL REFERENCES ProcessingSession(Id) ON DELETE CASCADE,
    RootRevisionId     TEXT NOT NULL,
    OriginalSourcePath TEXT NOT NULL,
    OriginalFileName   TEXT NOT NULL,
    ImportedAtUtc      TEXT NOT NULL
);
CREATE UNIQUE INDEX IX_InputSnapshot_Session ON InputSnapshot(SessionId);

CREATE TABLE Revision (
    Id                 TEXT PRIMARY KEY NOT NULL,
    SessionId          TEXT NOT NULL REFERENCES ProcessingSession(Id) ON DELETE CASCADE,
    SourceRevisionId   TEXT NULL REFERENCES Revision(Id),
    Operation          TEXT NOT NULL CHECK (Operation IN
                           ('IMPORT', 'ENHANCE', 'REMOVE_BACKGROUND', 'TRIM',
                            'PROMOTE_APPROVED', 'MANUAL_IMPORT', 'PHOTOSHOP_OUTPUT')),
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
CREATE INDEX IX_Revision_Session ON Revision(SessionId);
CREATE INDEX IX_Revision_Source  ON Revision(SourceRevisionId);

CREATE TABLE ProcessingAttempt (
    Id                TEXT PRIMARY KEY NOT NULL,
    SessionId         TEXT NOT NULL REFERENCES ProcessingSession(Id) ON DELETE CASCADE,
    StepKind          TEXT NOT NULL,
    InputRevisionId   TEXT NULL REFERENCES Revision(Id),
    Operation         TEXT NOT NULL,
    AdapterId         TEXT NOT NULL,
    StartedAtUtc      TEXT NOT NULL,
    EndedAtUtc        TEXT NULL,
    ResultStatus      TEXT NOT NULL CHECK (ResultStatus IN
                           ('RUNNING', 'SUCCEEDED', 'FAILED', 'INTERRUPTED', 'CANCELLED')),
    OutputRevisionId  TEXT NULL REFERENCES Revision(Id),
    FailureCode       TEXT NULL,
    FailureDetailJson TEXT NULL,
    RetryOfAttemptId  TEXT NULL REFERENCES ProcessingAttempt(Id),
    RetrySequence     INTEGER NOT NULL DEFAULT 0,
    -- MVP design invariant 4/5, made structural: a failed/running/interrupted/cancelled
    -- attempt can never carry an output Revision.
    CHECK ((ResultStatus = 'SUCCEEDED' AND OutputRevisionId IS NOT NULL)
        OR (ResultStatus <> 'SUCCEEDED' AND OutputRevisionId IS NULL))
);
CREATE INDEX IX_Attempt_Session ON ProcessingAttempt(SessionId);
CREATE INDEX IX_Attempt_Running ON ProcessingAttempt(ResultStatus) WHERE ResultStatus = 'RUNNING';

CREATE TABLE ReviewDecision (
    Id             TEXT PRIMARY KEY NOT NULL,
    SessionId      TEXT NOT NULL REFERENCES ProcessingSession(Id) ON DELETE CASCADE,
    StepKind       TEXT NOT NULL,
    SubjectKind    TEXT NOT NULL CHECK (SubjectKind IN ('REVISION', 'PRINT_OUTPUT')),
    SubjectId      TEXT NOT NULL,
    ReviewedSha256 TEXT NOT NULL CHECK (length(ReviewedSha256) = 64),
    Operator       TEXT NOT NULL,
    DecidedAtUtc   TEXT NOT NULL,
    Decision       TEXT NOT NULL CHECK (Decision IN ('APPROVED', 'REJECTED')),
    QuickReason    TEXT NULL,
    Notes          TEXT NULL
);
CREATE INDEX IX_Review_Subject ON ReviewDecision(SubjectId, DecidedAtUtc);
CREATE INDEX IX_Review_Session ON ReviewDecision(SessionId);

CREATE TABLE PrintOutput (
    Id                     TEXT PRIMARY KEY NOT NULL,
    SessionId              TEXT NOT NULL REFERENCES ProcessingSession(Id) ON DELETE CASCADE,
    SourceRevisionId       TEXT NOT NULL REFERENCES Revision(Id),
    TargetWidthMm          REAL NOT NULL,
    TargetHeightMm         REAL NOT NULL,
    PixelWidth             INTEGER NOT NULL,
    PixelHeight            INTEGER NOT NULL,
    Dpi                    INTEGER NOT NULL DEFAULT 300,
    SizePresetId           TEXT NOT NULL,
    WhiteUnderbaseBranch   TEXT NOT NULL CHECK (WhiteUnderbaseBranch IN ('W1_0PX', 'W1_1PX', 'W1_2PX')),
    ProductionPresetId     TEXT NOT NULL,
    ProductionPresetSha256 TEXT NOT NULL,
    RelativePath           TEXT NOT NULL,
    ByteLength             INTEGER NOT NULL,
    Sha256                 TEXT NOT NULL CHECK (length(Sha256) = 64),
    ReviewState            TEXT NOT NULL DEFAULT 'NOT_REVIEWED'
                               CHECK (ReviewState IN ('NOT_REVIEWED', 'APPROVED', 'REJECTED')),
    IsValid                INTEGER NOT NULL DEFAULT 1 CHECK (IsValid IN (0, 1)),
    InvalidationReason     TEXT NULL,
    RecycledAtUtc          TEXT NULL,
    CreatedAtUtc           TEXT NOT NULL
);
CREATE INDEX IX_PrintOutput_Session ON PrintOutput(SessionId);

CREATE TABLE AutomationLock (
    Id            INTEGER PRIMARY KEY CHECK (Id = 1),
    SessionId     TEXT NULL REFERENCES ProcessingSession(Id),
    AcquiredAtUtc TEXT NULL,
    ProcessId     INTEGER NULL,
    MachineName   TEXT NULL
);
INSERT INTO AutomationLock (Id, SessionId, AcquiredAtUtc, ProcessId, MachineName)
VALUES (1, NULL, NULL, NULL, NULL);

CREATE TABLE AutomationLogEntry (
    Id              TEXT PRIMARY KEY NOT NULL,
    SessionId       TEXT NULL REFERENCES ProcessingSession(Id) ON DELETE SET NULL,
    StepKind        TEXT NULL,
    AtUtc           TEXT NOT NULL,
    FailureCode     TEXT NOT NULL,
    MessageKey      TEXT NOT NULL,
    TechnicalDetail TEXT NOT NULL,
    ContextJson     TEXT NULL,
    ScreenshotPath  TEXT NULL
);
CREATE INDEX IX_AutomationLog_Session ON AutomationLogEntry(SessionId);

-- ---------------------------------------------------------------------------------------
-- Immutability and append-only enforcement (Jira 11105; plan §11.3).
-- These hold even if a future bug bypasses the repository layer entirely.
-- ---------------------------------------------------------------------------------------

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

CREATE TRIGGER ReviewDecision_NoUpdate BEFORE UPDATE ON ReviewDecision
BEGIN SELECT RAISE(ABORT, 'ReviewDecision is append-only'); END;

CREATE TRIGGER ReviewDecision_NoDelete BEFORE DELETE ON ReviewDecision
BEGIN SELECT RAISE(ABORT, 'ReviewDecision is append-only'); END;
