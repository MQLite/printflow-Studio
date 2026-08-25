-- Epic 11300 Part D1: preserve successful adapter runtime evidence on the attempt.
--
-- A validated output can cross the success boundary before Meitu cleanup fails. That cleanup
-- failure is a warning, not grounds to discard the Revision, but it must survive restart so the
-- audit does not falsely claim Meitu was returned to neutral state. Existing rows correctly
-- read as having no recorded adapter note.
ALTER TABLE ProcessingAttempt ADD COLUMN AdapterNotes TEXT NULL;
