# SCRUM-11112 execution plan

Policy: installed Development Routing & Context Policy v2.1 (2026-09-08), PLAN_EXECUTE.
Scope: canonical `D:\Repositories\printflow-Studio`, `master`, initial HEAD
`8b904537c37c9179c03392f6f30c9a5d559bceba`, initially clean. Local commits only;
no branch, worktree, push, external application processing, signing, or unrelated work.

## Requirement authority and pre-change matrix

Source: `C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv`.
Original Work Item 11505 maps to SCRUM-11112 (the CSV does not contain SCRUM keys).
Exact Description (no separate AC field):

> At startup, detect unfinished ProcessingAttempts and stale automation-lock state and mark such Attempts INTERRUPTED. Offer restart of the step from a fresh working copy, inspection/import of a manually saved result or abandonment of the Attempt. Never resume screen automation from the previous mouse position, selector sequence or uncertain external-application state.

| Original AC clause | Current implementation | Evidence | Remaining gap |
| --- | --- | --- | --- |
| Detect unfinished attempts and stale locks; mark attempts INTERRUPTED | StartupRecoveryService repairs attempts/steps transactionally, preserves immutable history, checks process liveness | StartupRecoveryTests, SessionHygieneAndRecoveryTests, ApplicationStartupTests | New environment-verification purpose has no SessionId and is overlooked by startup; add purpose-specific token release on proven death only |
| Restart from fresh working copy | Retry resets Waiting; StartStep later creates new attempt/copy from approved upstream; ReenterAutomation handles handed-off state | WorkflowEngine.Retry/StartStep/ReenterAutomation, RecoveryAndBranchTests, RetryAndReviewTests | Explicit per-entry Home action and restart-before-Run persistence proof |
| Inspection/import of manually saved result | SubmitManualResult, ManualResultEligibility, WicManualResultImporter; only eligible handed-off Enhancement/BackgroundRemoval; managed validation/hash/provenance then ReviewRequired | ManualResultImportTests, ManualResultFileTests; SCRUM-11092 reports | Recovery entry must route through current handoff/import authority, retain unresolved state on failure, and offer nothing for unsupported steps |
| Abandonment | Existing AbandonSession preserves source/history | HomeViewModel.Abandon, HomeAndWorkflowSelectionTests | Explicit recovery entry action, immediate refresh and synthetic proof |
| Never resume previous mouse/selector/uncertain state | Startup repairs metadata and quarantines leftovers; no automation execution; normal run uses starting-state gate | StartupRecoveryService, startup/retry/production-gate tests | Preserve and demonstrate zero execution for recovery decisions |

ReviewRequired inspection after validated import satisfies the original combined inspection/import
option. The original row does not require a pre-import confirmation screen. Preserve closed manual
scope; Trim/PSD/PDF/production TIFF/final review gain no arbitrary file import.

## Execution

1. **CONTINUE — reconcile and implement**. Dependencies: workflow engine/session service, SQLite
   discovery, Home/Recent Processing, existing file picker and resources. One service read model
   computes actions and reconstructs unresolved entries without a new table or recent-list age
   limit. Mixed UI work requested on native isolated sub-agent `recovery_surface`,
   `gpt-6-astra` / `high`, as required by UI routing. Root handles the separable startup token-lock
   correction, original-AC/parent audit and reports. Root actual model/effort: UNVERIFIED; no
   claim of a live model switch. Requested sub-agent model is selectable in the native schema;
   actual runtime identity is UNVERIFIED unless separately attested.
2. **CONTINUE — bounded validation**. Target recovery/startup/manual-result/persistence/Home/UI,
   localisation/accessibility and affected locks during implementation. Run actual synthetic
   WPF/UIA A Restart, B real file-dialog manual import, C Abandon against SQLite/FileWorkspace/
   startup/Home/SessionService. Verify disk/state separately. No external app processing needed.
   Completion: required paths and history/persistence invariants pass. Avoid exhaustive
   workflow × step × action matrices and tests duplicating existing validation.
3. **FRESH_REQUIRED — independent recovery acceptance review**. Native agent with no inherited
   history, requested Astra High for critical recovery/data safety. Supply original request/CSV,
   current diff and navigable tests/evidence, not implementation self-evaluation. Root concurrently
   reassesses parent original Work Item 11500/SCRUM-11107 against actual clauses and latest deltas.
   Fix material findings and rerun affected validation.
4. **CONTINUE — final verification and local delivery**. Clean build, one complete suite against
   final source (accepted baseline 11,513/0/0); report honestly, append dated coverage delta without
   rewriting historical rows, update HANDOFF, and commit task files locally. Complete only with
   original AC, live A/B/C, persistence, bilingual/UIA, independent review and full-suite evidence.

Required report: `docs/printflow/scrum-11112-interrupted-attempt-startup-recovery-completion.md`.
Parent Epic status is independently judged; child FULL labels alone do not establish parent FULL.

## Completion

All stages completed on 8 September 2026. Implementation and the seven recovery integration
cases plus real synthetic WPF/UIA A/B/C are complete. Startup token regressions passed 23/23;
broad affected tests passed 849/849; the independent review's final-commit failure correction
passed its focused 36/36 filter. The new method is named ResolveRecoveryAsync to keep operator
actions distinct from the single startup RecoverAsync callsite; the historical startup safety
assertion was preserved.

Native no-history Astra High review completed, including independent read-only SQLite/file-hash
verification and visual QA. One P2 finding was fixed and re-reviewed: a manual closing transaction
failure must retain its unfinished Processing card without exposing recovery mutations.
No unresolved findings. Actual model/effort remains UNVERIFIED; no live switch is claimed.

Final clean build: 0 warnings/0 errors. One final complete suite: 11,525 passed, 0 failed, 0 skipped
(+12 against accepted baseline). Final raw artifacts: artifacts/scrum-11112-recovery-live/final.
SCRUM-11112 and independently reassessed parent SCRUM-11107 are FULL in the dated coverage delta.
Product/tests committed locally as 7899769a6259a21d57a0d72fa32596fd3902b51b; reports/checkpoint follow
in a local documentation commit. No push, branch, worktree, external Jira change or release.
