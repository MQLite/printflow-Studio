# SCRUM-11121 Handoff

Date: 9 September 2026
Task: `SCRUM-11121` — Implement Local Log and Screenshot Retention
Policy: Codex Global Development Routing & Context Policy v2.2
Repository: `D:\Repositories\printflow-Studio`
Branch/start: `master` at `b3dda15ab7ecf1865197abe77d352954f51aa906`, initially clean

## Outcome

SCRUM-11121 is implemented and verified. Existing structured `AutomationLogEntry` rows are the
local log; no second text log or third-party framework was required by the exact AC. The configured
retention preference now drives bounded expiry of eligible rows and positively owned local failure
captures. Active/recoverable/shared diagnostic references and every overlapping Product file
authority are respected. Unknown or unsafe files are preserved.

Settings displays the actual SQLite database and Evidence-root locations as bilingual, read-only,
focusable/copyable values. Startup runs maintenance after successful recovery and before shell
publication. Maintenance warnings preserve evidence and do not refuse an otherwise safe startup.
No external application, cloud/upload path, package export, timer, or broad directory cleanup was
added.

## Safety decisions

- Resolution: valid persisted `LogRetentionDays` → valid configured value → Product default 30;
  unreadable Settings warns and preserves.
- Scope: old diagnostic rows and positively classified direct capture files only. Attempts,
  sessions, failure context, reviews, Revisions, and outputs remain durable.
- References: current failure, running attempt, RetryRequired/Interrupted/Processing state, and
  retry/manual-import ancestry are protected from persisted facts, not window visibility.
- Files: exact Evidence root, closed capture-name contract, canonical path identity, complete
  reference read, authority veto, reparse/read-only/mtime recheck, and single-file deletion.
- Manual result: migration `0016_manual_result_source_authority.sql` persists the exact external
  source path. A legacy import with unknown source conservatively preserves candidate bytes.
- Ordering: authorised file deletion precedes one bounded row transaction. Deletion failure retains
  the row; missing files are idempotent success; row-count mismatch rolls back the page.
- Concurrency: a held automation lease defers maintenance; retention never acquires or clears it.

## Verification

- New tests: 12 (8 retention, 2 startup, 1 Settings fallback, 1 rendered WPF locations).
- First-review regression proof: 4/4 failed before fixes, then 4/4 passed.
- Second-review regression proof: 3/3 failed before fixes, then 3/3 passed.
- Expanded affected set including migrations/manual-result import: 581 passed, 0 failed, 0 skipped.
- Final solution build: 0 warnings, 0 errors.
- First complete suite: 11,605 passed, 1 failed, 0 skipped; only the deliberate direct-deletion
  architecture allowlist, corrected by exact filename for the guarded store.
- Final complete suite: **11,609 passed, 0 failed, 0 skipped**.
- Raw final TRX:
  `artifacts/SCRUM-11121/full-suite-final/scrum-11121-full-final.trx`.
- `git diff --check`: passed before documentation/commit; rerun after final documentation.

## Independent review

- First full review: actual verified `gpt-6-astra` high, session
  `01a08327-cbfb-7991-bb42-f561c23d803c`; found raw path aliases, missing durable manual-source
  authority, and Settings maximum inconsistency.
- Re-review: actual verified `gpt-6-astra` high, session
  `01a08336-d831-7f41-b9c5-107039ce6629`; found Unicode-case matching and legacy unknown
  manual-source preservation gaps.
- Final scoped review: actual verified `gpt-6-astra` high, session
  `01a0833d-a4d4-7fa0-b087-aa2dcfc8320c`; accepted both corrections with no material finding or
  blocker.

## Routing record

- Plan/architecture: requested and actual verified `gpt-6-astra` high, isolated session
  `01a082ed-28ba-7743-809b-b2ad94a83479`.
- Backend/integration/corrections/final verification: requested and actual verified
  `gpt-5.6-sol` high in the coordinating task.
- Settings/WPF slice: requested and actual verified `gpt-6-astra` high, isolated session
  `01a08310-b093-72f3-b0f8-e2751845aaed`. This corrects the earlier interim `UNVERIFIED` note,
  which was written before coordinating-session verification was available.
- Independent review: fresh/read-only actual verified `gpt-6-astra` high as listed above.

## Jira reassessment

- SCRUM-11121: PARTIAL → FULL.
- SCRUM-11091: PARTIAL → FULL; configured local retention was its last material gap.
- Parent SCRUM-11085: PARTIAL → FULL after independent clause-by-clause reassessment; retention was
  its last recorded gap.
- Parent SCRUM-11115: remains PARTIAL because SCRUM-11116 format feedback, SCRUM-11117 thumbnail and
  delete-record action, SCRUM-11122 diagnostic-package export, and SCRUM-11123 offline installer
  remain open.

## Deliverables and Git discipline

- Plan: `docs/codex/SCRUM-11121/PLAN.md`.
- Completion report:
  `docs/printflow/scrum-11121-local-log-screenshot-retention-completion.md`.
- Append-only audit delta: `docs/printflow/original-jira-functional-coverage-reaudit.md`.
- Product/tests are committed locally on `master` as `66d57dd`; documentation is recorded in the
  following local commit, whose hash is reported by the coordinating task after final verification.
- No branch, worktree, alternate clone, amend, rebase, push, deployment, Co-Authored-By trailer,
  or AI attribution.

**PASS — SCRUM-11121 LOCAL LOG AND SCREENSHOT RETENTION VERIFIED**
