-- SCRUM-11121: retain the external source selected for a manual-result import as
-- explicit, queryable file authority. Existing attempts remain null because older builds
-- recorded only the managed copy and a human-readable filename in AdapterNotes.
ALTER TABLE ProcessingAttempt ADD COLUMN ManualResultSourcePath TEXT NULL
    CHECK (ManualResultSourcePath IS NULL OR Operation = 'MANUAL_RESULT_IMPORT');
