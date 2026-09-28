-- SCRUM-11144. Delivery evidence is append-preserving and separate from workflow approval.
-- There is deliberately no cascade from a session, review, revision or output into history.
CREATE TABLE ArtifactDelivery (
    DeliveryId TEXT PRIMARY KEY NOT NULL,
    RequestId TEXT NOT NULL UNIQUE,
    IntentOrdinal INTEGER NOT NULL UNIQUE CHECK (IntentOrdinal > 0),
    SessionId TEXT NOT NULL REFERENCES ProcessingSession(Id),
    Kind TEXT NOT NULL CHECK (Kind IN ('ApprovedAssetPng', 'ApprovedPrintTiff')),
    RevisionId TEXT NULL REFERENCES Revision(Id),
    PrintOutputId TEXT NULL REFERENCES PrintOutput(Id),
    ReviewId TEXT NOT NULL REFERENCES ReviewDecision(Id),
    ApprovalSubjectKind TEXT NOT NULL CHECK (ApprovalSubjectKind IN ('Revision', 'PrintOutput')),
    ApprovalSubjectId TEXT NOT NULL,
    PromotionSourceRevisionId TEXT NULL REFERENCES Revision(Id),
    ApprovedSha256 TEXT NOT NULL CHECK (length(ApprovedSha256) = 64),
    ApprovedLength INTEGER NOT NULL CHECK (ApprovedLength > 0),
    RequestedFolder TEXT NOT NULL,
    RequestedFileName TEXT NOT NULL,
    ResolvedFolder TEXT NOT NULL,
    FinalPath TEXT NOT NULL,
    VolumeId TEXT NOT NULL,
    DirectoryId TEXT NOT NULL,
    DestinationKey TEXT NOT NULL,
    ReplacementOfDeliveryId TEXT NULL REFERENCES ArtifactDelivery(DeliveryId),
    Status TEXT NOT NULL DEFAULT 'Pending' CHECK (Status IN ('Pending', 'Delivered')),
    CreatedAtUtc TEXT NOT NULL,
    VerifiedAtUtc TEXT NULL,
    VerifiedSha256 TEXT NULL,
    VerifiedLength INTEGER NULL,
    FinalFileId TEXT NULL,
    FinalCreationUtc TEXT NULL,
    WinningAttemptId TEXT NULL REFERENCES DeliveryAttempt(AttemptId),
    CHECK ((Kind = 'ApprovedAssetPng' AND RevisionId IS NOT NULL AND PrintOutputId IS NULL
             AND PromotionSourceRevisionId IS NOT NULL AND ApprovalSubjectKind = 'Revision')
        OR (Kind = 'ApprovedPrintTiff' AND RevisionId IS NULL AND PrintOutputId IS NOT NULL
             AND PromotionSourceRevisionId IS NULL AND ApprovalSubjectKind = 'PrintOutput')),
    CHECK ((Status = 'Pending' AND VerifiedAtUtc IS NULL AND VerifiedSha256 IS NULL
             AND VerifiedLength IS NULL AND FinalFileId IS NULL AND FinalCreationUtc IS NULL
             AND WinningAttemptId IS NULL)
        OR (Status = 'Delivered' AND VerifiedAtUtc IS NOT NULL
             AND VerifiedSha256 = ApprovedSha256 AND VerifiedLength = ApprovedLength
             AND FinalFileId IS NOT NULL AND FinalCreationUtc IS NOT NULL
             AND WinningAttemptId IS NOT NULL))
);
CREATE INDEX IX_ArtifactDelivery_SessionArtifact ON ArtifactDelivery(SessionId, Kind, RevisionId, PrintOutputId);
CREATE INDEX IX_ArtifactDelivery_Destination ON ArtifactDelivery(DestinationKey, IntentOrdinal DESC);
CREATE UNIQUE INDEX UX_ArtifactDelivery_Replacement ON ArtifactDelivery(ReplacementOfDeliveryId)
    WHERE ReplacementOfDeliveryId IS NOT NULL;

CREATE TABLE DeliveryAttempt (
    AttemptId TEXT PRIMARY KEY NOT NULL,
    DeliveryId TEXT NOT NULL REFERENCES ArtifactDelivery(DeliveryId),
    AttemptNumber INTEGER NOT NULL CHECK (AttemptNumber > 0),
    DestinationKey TEXT NOT NULL,
    State TEXT NOT NULL CHECK (State IN
        ('Intent', 'Staging', 'ReadyToPublish', 'Delivered', 'Failed', 'Cancelled', 'NeedsReconciliation')),
    StagingLeaf TEXT NOT NULL,
    DirectoryId TEXT NOT NULL,
    StagingFileId TEXT NULL,
    StagingCreationUtc TEXT NULL,
    ExpectedSha256 TEXT NOT NULL CHECK (length(ExpectedSha256) = 64),
    ExpectedLength INTEGER NOT NULL CHECK (ExpectedLength > 0),
    StageVerifiedAtUtc TEXT NULL,
    FailureCode TEXT NULL,
    StartedAtUtc TEXT NOT NULL,
    FinishedAtUtc TEXT NULL,
    UNIQUE (DeliveryId, AttemptNumber),
    CHECK ((State IN ('Staging', 'ReadyToPublish', 'Delivered')
              AND StagingFileId IS NOT NULL AND StagingCreationUtc IS NOT NULL)
        OR State IN ('Intent', 'Failed', 'Cancelled', 'NeedsReconciliation')),
    CHECK (State <> 'ReadyToPublish' OR StageVerifiedAtUtc IS NOT NULL)
);
CREATE UNIQUE INDEX UX_DeliveryAttempt_ActiveDelivery ON DeliveryAttempt(DeliveryId)
    WHERE State IN ('Intent', 'Staging', 'ReadyToPublish', 'NeedsReconciliation');
CREATE UNIQUE INDEX UX_DeliveryAttempt_ActiveDestination ON DeliveryAttempt(DestinationKey)
    WHERE State IN ('Intent', 'Staging', 'ReadyToPublish', 'NeedsReconciliation');

-- A second invocation that coalesces onto an existing delivery still owns an immutable
-- RequestId. Without this binding, reusing that ID later with another destination could
-- silently create a new delivery even though the first invocation already succeeded.
CREATE TABLE DeliveryRequestAlias (
    RequestId TEXT PRIMARY KEY NOT NULL,
    DeliveryId TEXT NOT NULL REFERENCES ArtifactDelivery(DeliveryId),
    RequestedFolder TEXT NOT NULL,
    RequestedFileName TEXT NOT NULL,
    RequestFingerprint TEXT NOT NULL CHECK (length(RequestFingerprint) = 64)
);
CREATE INDEX IX_DeliveryRequestAlias_Delivery ON DeliveryRequestAlias(DeliveryId);
CREATE TRIGGER DeliveryRequestAlias_Immutable BEFORE UPDATE ON DeliveryRequestAlias
BEGIN SELECT RAISE(ABORT, 'Coalesced request binding is immutable'); END;
CREATE TRIGGER DeliveryRequestAlias_NoDelete BEFORE DELETE ON DeliveryRequestAlias
BEGIN SELECT RAISE(ABORT, 'Coalesced request binding is retained'); END;

CREATE TRIGGER ArtifactDelivery_Immutable BEFORE UPDATE ON ArtifactDelivery
WHEN OLD.DeliveryId <> NEW.DeliveryId OR OLD.RequestId <> NEW.RequestId
  OR OLD.IntentOrdinal <> NEW.IntentOrdinal OR OLD.SessionId <> NEW.SessionId
  OR OLD.Kind <> NEW.Kind OR OLD.RevisionId IS NOT NEW.RevisionId
  OR OLD.PrintOutputId IS NOT NEW.PrintOutputId OR OLD.ReviewId <> NEW.ReviewId
  OR OLD.ApprovalSubjectKind <> NEW.ApprovalSubjectKind
  OR OLD.ApprovalSubjectId <> NEW.ApprovalSubjectId
  OR OLD.PromotionSourceRevisionId IS NOT NEW.PromotionSourceRevisionId
  OR OLD.ApprovedSha256 <> NEW.ApprovedSha256 OR OLD.ApprovedLength <> NEW.ApprovedLength
  OR OLD.RequestedFolder <> NEW.RequestedFolder OR OLD.RequestedFileName <> NEW.RequestedFileName
  OR OLD.ResolvedFolder <> NEW.ResolvedFolder OR OLD.FinalPath <> NEW.FinalPath
  OR OLD.VolumeId <> NEW.VolumeId OR OLD.DirectoryId <> NEW.DirectoryId
  OR OLD.DestinationKey <> NEW.DestinationKey
  OR OLD.ReplacementOfDeliveryId IS NOT NEW.ReplacementOfDeliveryId
  OR OLD.CreatedAtUtc <> NEW.CreatedAtUtc OR OLD.Status = 'Delivered'
BEGIN SELECT RAISE(ABORT, 'Delivery request/evidence is immutable'); END;
CREATE TRIGGER ArtifactDelivery_NoDelete BEFORE DELETE ON ArtifactDelivery
BEGIN SELECT RAISE(ABORT, 'Delivery evidence is retained'); END;
CREATE TRIGGER DeliveryAttempt_Immutable BEFORE UPDATE ON DeliveryAttempt
WHEN OLD.AttemptId <> NEW.AttemptId OR OLD.DeliveryId <> NEW.DeliveryId
  OR OLD.AttemptNumber <> NEW.AttemptNumber OR OLD.DestinationKey <> NEW.DestinationKey
  OR OLD.StagingLeaf <> NEW.StagingLeaf OR OLD.DirectoryId <> NEW.DirectoryId
  OR OLD.ExpectedSha256 <> NEW.ExpectedSha256 OR OLD.ExpectedLength <> NEW.ExpectedLength
  OR OLD.StartedAtUtc <> NEW.StartedAtUtc OR OLD.State = 'Delivered'
  OR (OLD.StagingFileId IS NOT NULL AND OLD.StagingFileId IS NOT NEW.StagingFileId)
  OR (OLD.StagingCreationUtc IS NOT NULL AND OLD.StagingCreationUtc IS NOT NEW.StagingCreationUtc)
  OR (OLD.StageVerifiedAtUtc IS NOT NULL AND OLD.StageVerifiedAtUtc IS NOT NEW.StageVerifiedAtUtc)
BEGIN SELECT RAISE(ABORT, 'Delivery attempt identity is immutable'); END;
CREATE TRIGGER DeliveryAttempt_ForwardOnly BEFORE UPDATE OF State ON DeliveryAttempt
WHEN NOT ((OLD.State='Intent' AND NEW.State IN ('Intent','Staging','Failed','Cancelled','NeedsReconciliation'))
       OR (OLD.State='Staging' AND NEW.State IN ('Staging','ReadyToPublish','Failed','Cancelled','NeedsReconciliation'))
       OR (OLD.State='ReadyToPublish' AND NEW.State IN ('ReadyToPublish','Delivered','Failed','Cancelled','NeedsReconciliation'))
       OR (OLD.State='NeedsReconciliation' AND NEW.State IN ('NeedsReconciliation','Delivered','Failed','Cancelled'))
       OR (OLD.State IN ('Failed','Cancelled','Delivered') AND NEW.State=OLD.State))
BEGIN SELECT RAISE(ABORT, 'Delivery attempt cannot move backwards'); END;
CREATE TRIGGER DeliveryAttempt_NoDelete BEFORE DELETE ON DeliveryAttempt
BEGIN SELECT RAISE(ABORT, 'Delivery attempt evidence is retained'); END;
