# SCRUM-11153 — Recent processing status plan

Planning-ID: `PF-OPUX-v1-recent-row-status`. Baseline: `master` / `origin/master` at `1f9a93dfb4f3b90b6357b86a5fbb03dfbd75b4ec`; Jira `To Do` → `In Progress` (actual transition 21). Scope is the approved bounded interaction brief in the task prompt. Routing policy 2.4, `route_offset: 0`; UI route requested Astra 6.0 High, separable read projection route Sol 6.0 High. Current coordinator model/effort is not independently verifiable; model switch unavailable in this thread.

## Source and status precedence before editing

| Source | Fact | Authority / display precedence |
|---|---|---|
| `ProcessingSession.State` | HandedOff, Completed, Abandoned | Terminal/handoff presentation wins; no old step/attempt can make it active. |
| `SessionStep` at `ProcessingSession.CurrentStep` | ReviewRequired, Failed, RetryRequired, Waiting, Processing | Active rows only; ReviewRequired → review, Failed/RetryRequired → stopped, Waiting/Approved/Skipped → input or neutral. |
| Current-step `ProcessingAttempt` | Running | Processing only with an actually running current-step attempt and step Processing; Active alone is not evidence. |
| Recovery query and Home membership filter | Unresolved interruption | Recovery remains authoritative; no Recent duplication or navigation change. |
| `ArtifactDelivery` + exact approved artifact/review/hash/length | Historical Delivered | Separate, qualified history. Never replaces primary status and never claims file availability. TIFF counts are per output ID/size. Failed/unavailable read is unknown, not no record. |

## Implementation checklist

1. Add narrow list read fields for current step and exact approved-result delivery history. Keep the 30-day/100-row query and existing eligibility, ordering, thumbnail and recovery contracts.
2. Add localized Recent status and history text, with text wrapping and accessible names. Keep Resume/Details, Remove and Abandon commands unchanged.
3. Add focused behavioral tests first, observe RED, implement, then run affected isolated service/UI/resource/architecture/export tests. Exclude native desktop, startup, broad production composition and known hanging recovery paths.
4. Render distinct synthetic states in en/zh-CN at 1000×700 and 1920×1040, 96 DPI; inspect representative images. Obtain one fresh independent read-only review; correct in-scope findings and reverify.
5. Stage only this task, scan, commit and fast-forward push to `origin/master`; verify remote readback. Transition only 11153 to In Review, add one deduplicated evidence comment, authenticate final initiative readback, export and validate the exact 23-column CSV.
6. Assemble the local owner packet, hash and verify ZIP, update `LATEST.md`, and leave owner copy/physical acceptance open.

AC1–4 require code/tests and existing command assurance. AC5 requires 96-DPI synthetic layout evidence; real input and other scaling remain open until performed.

## Execution record

- The 12-path source/test delta was reviewed, committed as `7b37f24175df5d6efd530683fc52876c6446750a` and verified on `origin/master` by fetch, ls-remote and authenticated GitHub commit readback. No feature branch, PR, hook bypass or deployment.
- The bounded SQLite recent query reads the current step and current running attempt in one deferred transaction, then one metadata-only history query for selected IDs. Exact delivered approved PNG/TIFF identities, hash, length, review, producing/promotion attempt and valid source lineage determine a qualified historical count. Query failure remains unknown.
- The active row gives current review, stopped, input, supported processing or unknown; terminal/handoff presentation wins. Save history is a separate line. Language changes reword existing rows; a stale earlier refresh cannot replace a newer result. Recovery/correction navigation and list commands remain intact.
- Test-first RED was observed for current review text and exact historical save, then for source invalidation and existing-row language change. The final affected safe set passed 197/197; delivery/recovery/architecture set passed 162/162; screenshot/focused set passed 20/20 plus a later 2/2 long-name capture; the final build reported zero warnings/errors. These counts overlap.
- One fresh independent read-only reviewer reported three P2 findings (transaction mode, source validity, language rewording); fixes were rechecked and no actionable code finding remained. The reviewer inspected ten representative PNGs and then the two small long-name captures; it did not run tests.
- Jira `SCRUM-11153`: To Do → In Progress (transition 21) at implementation start, then In Review (transition 31) after code publication. Comment `10188`, marker `PF-OPUX-v1-SCRUM-11153-impl-v1`. Other issues were read only.
- Final authenticated readback: 2026-09-30T00:16:56.330Z; 17 issues, 26 Blocks, 51 string labels, complete comments, exact 23-column UTF-8 BOM CSV, raw-to-snapshot-to-CSV fidelity PASS. The previous snapshot comparison found only 11153's direct status change and its expected nested linked-status changes.

## AC disposition

| AC | Technical disposition | Owner boundary |
|---|---|---|
| 1 — review state and step | PASS: current-step ReviewRequired row and no processor start on open. | Copy acceptance open. |
| 2 — failure and Resume | PASS: Failed/RetryRequired mapping; existing exact-job Resume and recovery tests retained. | Human navigation check open. |
| 3 — handoff | PASS: handed-off precedence and correction-bound navigation; interrupted job remains Recovery-only. | Human correction flow check open. |
| 4 — completed/abandoned and Remove meaning | PASS: panel wording; historical save is separate and exact; existing Remove/Abandon record/file tests pass. | Save destination availability is not claimed. |
| 5 — readable without colour/clipping | PARTIAL: synthetic off-screen en/zh-CN at 1000×700 and 1920×1040, 96 DPI, reviewed. | Real input, non-96-DPI and novice check NOT RUN. |
