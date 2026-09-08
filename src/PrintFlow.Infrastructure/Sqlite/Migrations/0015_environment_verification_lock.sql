ALTER TABLE AutomationLock ADD COLUMN Purpose TEXT NULL;
ALTER TABLE AutomationLock ADD COLUMN OwnerToken TEXT NULL;

UPDATE AutomationLock
SET Purpose = 'SESSION'
WHERE SessionId IS NOT NULL AND Purpose IS NULL;
