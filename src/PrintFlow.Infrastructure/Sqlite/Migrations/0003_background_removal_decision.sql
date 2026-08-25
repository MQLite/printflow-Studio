-- Epic 11300 Part C2B1 §10, §11: make the Background Removal product decision durable and
-- auditable.
--
-- Part C2A introduced BackgroundRemovalDecision but had no workflow caller that could supply an
-- authorised value, so the ordinary session route passed Unspecified and nothing was ever
-- stored. This slice adds the caller, and with it two records that answer two different
-- questions -- the same split the trim parameters use (Epic 11200 Part C3 §14):
--
--   ProcessingSession  -> "what would the NEXT background removal be allowed to do". A pending
--                         decision, replaced whenever the operator authorises different
--                         content, and needed only so a decision made before a restart is still
--                         there afterwards (§20).
--
--   ProcessingAttempt  -> "what was THIS cutout authorised by". Written once with the attempt's
--                         opening transaction, before the working copy and before Meitu is
--                         touched, and never updated afterwards -- the attempt upsert
--                         deliberately leaves these columns out of its DO UPDATE clause, so a
--                         later decision cannot rewrite what an earlier attempt ran under
--                         (§11, §18).
--
-- Three typed columns rather than one blob, because the decision alone is not the record. The
-- authority means "automatic selection is authorised for THIS reviewed content", so the
-- artefact it was granted over is part of the value, not context around it (§4). Both halves of
-- that artefact's identity are kept: the RevisionId answers "which artefact" and the SHA-256
-- answers "which bytes". An id on its own would still match after the file underneath it was
-- replaced, which is precisely the case §24 requires to be refused.
--
-- Nullable with no default on both tables, and the three columns always move together. NULL
-- across all three means "no decision" -- on a session, that nobody has authorised anything
-- yet; on an attempt, that this attempt was not an authorised background removal at all (an
-- enhancement, a trim, a promotion, a manual crop). It never means "the default was used".
-- There is no default: an Unspecified decision is a refusal, and the workflow will not start
-- background removal without an explicit authority (§7). Rows written before this migration
-- therefore read back correctly as "no decision recorded", which is what they were.
--
-- Nothing here stores a stale authority differently from a fresh one. Invalidation is not a
-- write (§9): an authority stops being usable when the artefact it names stops being the one
-- background removal will consume, which WorkflowSnapshot decides by comparing these columns
-- against the current upstream step result. That is why no UPDATE ever has to hunt down and
-- clear these values when a Revision is superseded, and why the row that stays behind is
-- honest history rather than a lingering permission.

-- The decision text mirrors the enum exactly, both members included, so the schema states the
-- same closed set the code does. Writing UNSPECIFIED here is still impossible in practice --
-- BackgroundRemovalAuthority.For refuses to construct an authority that authorises nothing --
-- but a CHECK that silently omitted a member would be a schema claiming an enum it does not
-- have.
ALTER TABLE ProcessingSession ADD COLUMN BackgroundRemovalDecision    TEXT NULL
    CHECK (BackgroundRemovalDecision IS NULL OR BackgroundRemovalDecision IN
        ('UNSPECIFIED', 'USE_AUTOMATIC_SELECTION_FOR_REVIEWED_CONTENT'));
ALTER TABLE ProcessingSession ADD COLUMN BackgroundRemovalRevisionId  TEXT NULL;
ALTER TABLE ProcessingSession ADD COLUMN BackgroundRemovalReviewedSha TEXT NULL
    CHECK (BackgroundRemovalReviewedSha IS NULL OR length(BackgroundRemovalReviewedSha) = 64);

ALTER TABLE ProcessingAttempt ADD COLUMN BackgroundRemovalDecision    TEXT NULL
    CHECK (BackgroundRemovalDecision IS NULL OR BackgroundRemovalDecision IN
        ('UNSPECIFIED', 'USE_AUTOMATIC_SELECTION_FOR_REVIEWED_CONTENT'));
ALTER TABLE ProcessingAttempt ADD COLUMN BackgroundRemovalRevisionId  TEXT NULL;
ALTER TABLE ProcessingAttempt ADD COLUMN BackgroundRemovalReviewedSha TEXT NULL
    CHECK (BackgroundRemovalReviewedSha IS NULL OR length(BackgroundRemovalReviewedSha) = 64);
