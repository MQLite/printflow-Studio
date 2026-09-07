-- SCRUM-11082: source-space manual crop history. Old rows deliberately remain NULL.
ALTER TABLE ProcessingAttempt ADD COLUMN ManualSelectedLeft INTEGER NULL;
ALTER TABLE ProcessingAttempt ADD COLUMN ManualSelectedTop INTEGER NULL;
ALTER TABLE ProcessingAttempt ADD COLUMN ManualSelectedRight INTEGER NULL;
ALTER TABLE ProcessingAttempt ADD COLUMN ManualSelectedBottom INTEGER NULL;
ALTER TABLE ProcessingAttempt ADD COLUMN ManualAppliedLeft INTEGER NULL;
ALTER TABLE ProcessingAttempt ADD COLUMN ManualAppliedTop INTEGER NULL;
ALTER TABLE ProcessingAttempt ADD COLUMN ManualAppliedRight INTEGER NULL;
ALTER TABLE ProcessingAttempt ADD COLUMN ManualAppliedBottom INTEGER NULL;
ALTER TABLE ProcessingAttempt ADD COLUMN ManualMarginTop INTEGER NULL;
ALTER TABLE ProcessingAttempt ADD COLUMN ManualMarginRight INTEGER NULL;
ALTER TABLE ProcessingAttempt ADD COLUMN ManualMarginBottom INTEGER NULL;
ALTER TABLE ProcessingAttempt ADD COLUMN ManualMarginLeft INTEGER NULL;
ALTER TABLE ProcessingAttempt ADD COLUMN ManualMarginMode TEXT NULL;

CREATE TRIGGER ProcessingAttempt_ManualCrop_Coherent_INSERT
BEFORE INSERT ON ProcessingAttempt
WHEN NOT ((NEW.ManualSelectedLeft IS NULL AND NEW.ManualSelectedTop IS NULL AND NEW.ManualSelectedRight IS NULL AND NEW.ManualSelectedBottom IS NULL AND NEW.ManualAppliedLeft IS NULL AND NEW.ManualAppliedTop IS NULL AND NEW.ManualAppliedRight IS NULL AND NEW.ManualAppliedBottom IS NULL AND NEW.ManualMarginTop IS NULL AND NEW.ManualMarginRight IS NULL AND NEW.ManualMarginBottom IS NULL AND NEW.ManualMarginLeft IS NULL AND NEW.ManualMarginMode IS NULL) OR (NEW.ManualSelectedLeft IS NOT NULL AND NEW.ManualSelectedTop IS NOT NULL AND NEW.ManualSelectedRight IS NOT NULL AND NEW.ManualSelectedBottom IS NOT NULL AND NEW.ManualAppliedLeft IS NOT NULL AND NEW.ManualAppliedTop IS NOT NULL AND NEW.ManualAppliedRight IS NOT NULL AND NEW.ManualAppliedBottom IS NOT NULL AND NEW.ManualMarginTop IS NOT NULL AND NEW.ManualMarginRight IS NOT NULL AND NEW.ManualMarginBottom IS NOT NULL AND NEW.ManualMarginLeft IS NOT NULL AND NEW.ManualMarginMode IS NOT NULL AND NEW.Operation = 'MANUAL_IMPORT' AND NEW.ResultStatus = 'SUCCEEDED' AND NEW.OutputRevisionId IS NOT NULL
AND NEW.TrimContentLeft IS NULL AND NEW.TrimMode IS NULL
AND NEW.ManualSelectedLeft >= 0 AND NEW.ManualSelectedTop >= 0
AND NEW.ManualSelectedRight > NEW.ManualSelectedLeft AND NEW.ManualSelectedBottom > NEW.ManualSelectedTop
AND NEW.ManualAppliedLeft >= 0 AND NEW.ManualAppliedTop >= 0
AND NEW.ManualAppliedLeft <= NEW.ManualSelectedLeft AND NEW.ManualAppliedTop <= NEW.ManualSelectedTop
AND NEW.ManualAppliedRight >= NEW.ManualSelectedRight AND NEW.ManualAppliedBottom >= NEW.ManualSelectedBottom
AND NEW.ManualMarginTop >= 0 AND NEW.ManualMarginRight >= 0 AND NEW.ManualMarginBottom >= 0 AND NEW.ManualMarginLeft >= 0
AND NEW.ManualAppliedLeft = MAX(0, NEW.ManualSelectedLeft - NEW.ManualMarginLeft)
AND NEW.ManualAppliedTop = MAX(0, NEW.ManualSelectedTop - NEW.ManualMarginTop)
AND NEW.ManualAppliedRight <= NEW.ManualSelectedRight + NEW.ManualMarginRight
AND NEW.ManualAppliedBottom <= NEW.ManualSelectedBottom + NEW.ManualMarginBottom
AND ((NEW.ManualMarginMode = 'TIGHT_CROP' AND NEW.ManualMarginTop = 0 AND NEW.ManualMarginRight = 0 AND NEW.ManualMarginBottom = 0 AND NEW.ManualMarginLeft = 0)
 OR (NEW.ManualMarginMode = 'UNIFORM_MARGIN' AND NEW.ManualMarginTop = NEW.ManualMarginRight AND NEW.ManualMarginTop = NEW.ManualMarginBottom AND NEW.ManualMarginTop = NEW.ManualMarginLeft)
 OR NEW.ManualMarginMode = 'EDGE_SPECIFIC_MARGIN')))
BEGIN SELECT RAISE(ABORT, 'Manual crop geometry and margin must be complete, coherent manual history.'); END;

CREATE TRIGGER ProcessingAttempt_ManualCrop_Coherent_UPDATE
BEFORE UPDATE ON ProcessingAttempt
WHEN NOT ((NEW.ManualSelectedLeft IS NULL AND NEW.ManualSelectedTop IS NULL AND NEW.ManualSelectedRight IS NULL AND NEW.ManualSelectedBottom IS NULL AND NEW.ManualAppliedLeft IS NULL AND NEW.ManualAppliedTop IS NULL AND NEW.ManualAppliedRight IS NULL AND NEW.ManualAppliedBottom IS NULL AND NEW.ManualMarginTop IS NULL AND NEW.ManualMarginRight IS NULL AND NEW.ManualMarginBottom IS NULL AND NEW.ManualMarginLeft IS NULL AND NEW.ManualMarginMode IS NULL) OR (NEW.ManualSelectedLeft IS NOT NULL AND NEW.ManualSelectedTop IS NOT NULL AND NEW.ManualSelectedRight IS NOT NULL AND NEW.ManualSelectedBottom IS NOT NULL AND NEW.ManualAppliedLeft IS NOT NULL AND NEW.ManualAppliedTop IS NOT NULL AND NEW.ManualAppliedRight IS NOT NULL AND NEW.ManualAppliedBottom IS NOT NULL AND NEW.ManualMarginTop IS NOT NULL AND NEW.ManualMarginRight IS NOT NULL AND NEW.ManualMarginBottom IS NOT NULL AND NEW.ManualMarginLeft IS NOT NULL AND NEW.ManualMarginMode IS NOT NULL AND NEW.Operation = 'MANUAL_IMPORT' AND NEW.ResultStatus = 'SUCCEEDED' AND NEW.OutputRevisionId IS NOT NULL
AND NEW.TrimContentLeft IS NULL AND NEW.TrimMode IS NULL
AND NEW.ManualSelectedLeft >= 0 AND NEW.ManualSelectedTop >= 0
AND NEW.ManualSelectedRight > NEW.ManualSelectedLeft AND NEW.ManualSelectedBottom > NEW.ManualSelectedTop
AND NEW.ManualAppliedLeft >= 0 AND NEW.ManualAppliedTop >= 0
AND NEW.ManualAppliedLeft <= NEW.ManualSelectedLeft AND NEW.ManualAppliedTop <= NEW.ManualSelectedTop
AND NEW.ManualAppliedRight >= NEW.ManualSelectedRight AND NEW.ManualAppliedBottom >= NEW.ManualSelectedBottom
AND NEW.ManualMarginTop >= 0 AND NEW.ManualMarginRight >= 0 AND NEW.ManualMarginBottom >= 0 AND NEW.ManualMarginLeft >= 0
AND NEW.ManualAppliedLeft = MAX(0, NEW.ManualSelectedLeft - NEW.ManualMarginLeft)
AND NEW.ManualAppliedTop = MAX(0, NEW.ManualSelectedTop - NEW.ManualMarginTop)
AND NEW.ManualAppliedRight <= NEW.ManualSelectedRight + NEW.ManualMarginRight
AND NEW.ManualAppliedBottom <= NEW.ManualSelectedBottom + NEW.ManualMarginBottom
AND ((NEW.ManualMarginMode = 'TIGHT_CROP' AND NEW.ManualMarginTop = 0 AND NEW.ManualMarginRight = 0 AND NEW.ManualMarginBottom = 0 AND NEW.ManualMarginLeft = 0)
 OR (NEW.ManualMarginMode = 'UNIFORM_MARGIN' AND NEW.ManualMarginTop = NEW.ManualMarginRight AND NEW.ManualMarginTop = NEW.ManualMarginBottom AND NEW.ManualMarginTop = NEW.ManualMarginLeft)
 OR NEW.ManualMarginMode = 'EDGE_SPECIFIC_MARGIN')))
BEGIN SELECT RAISE(ABORT, 'Manual crop geometry and margin must be complete, coherent manual history.'); END;

CREATE TRIGGER ProcessingAttempt_ManualCrop_Immutable
BEFORE UPDATE ON ProcessingAttempt
WHEN OLD.ManualMarginMode IS NOT NULL AND (NEW.ManualSelectedLeft IS NOT OLD.ManualSelectedLeft OR NEW.ManualSelectedTop IS NOT OLD.ManualSelectedTop OR NEW.ManualSelectedRight IS NOT OLD.ManualSelectedRight OR NEW.ManualSelectedBottom IS NOT OLD.ManualSelectedBottom OR NEW.ManualAppliedLeft IS NOT OLD.ManualAppliedLeft OR NEW.ManualAppliedTop IS NOT OLD.ManualAppliedTop OR NEW.ManualAppliedRight IS NOT OLD.ManualAppliedRight OR NEW.ManualAppliedBottom IS NOT OLD.ManualAppliedBottom OR NEW.ManualMarginTop IS NOT OLD.ManualMarginTop OR NEW.ManualMarginRight IS NOT OLD.ManualMarginRight OR NEW.ManualMarginBottom IS NOT OLD.ManualMarginBottom OR NEW.ManualMarginLeft IS NOT OLD.ManualMarginLeft OR NEW.ManualMarginMode IS NOT OLD.ManualMarginMode)
BEGIN SELECT RAISE(ABORT, 'Manual crop history is immutable.'); END;

