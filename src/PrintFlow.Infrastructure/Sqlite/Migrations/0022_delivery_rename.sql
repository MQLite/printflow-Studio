-- Keep the original delivery and request binding immutable. A pending rename is written
-- before changing the file name so recovery can verify the same file at the new path.
CREATE TABLE DeliveryRename (
    RenameId TEXT PRIMARY KEY NOT NULL,
    DeliveryId TEXT NOT NULL REFERENCES ArtifactDelivery(DeliveryId),
    FileName TEXT NOT NULL,
    FinalPath TEXT NOT NULL,
    DestinationKey TEXT NOT NULL,
    State TEXT NOT NULL CHECK (State IN ('Pending', 'Committed', 'Aborted')),
    FinalCreationUtc TEXT NULL,
    CommitOrdinal INTEGER NULL UNIQUE CHECK (CommitOrdinal > 0),
    CHECK ((State = 'Committed') = (FinalCreationUtc IS NOT NULL)),
    CHECK ((State = 'Committed') = (CommitOrdinal IS NOT NULL))
);
CREATE INDEX IX_DeliveryRename_Delivery ON DeliveryRename(DeliveryId, State);
CREATE UNIQUE INDEX UX_DeliveryRename_Pending ON DeliveryRename(DeliveryId) WHERE State = 'Pending';
CREATE UNIQUE INDEX UX_DeliveryRename_Destination ON DeliveryRename(DestinationKey) WHERE State = 'Pending';
