# SCRUM-11121 — Local Log and Screenshot Retention Plan

Task ID: `SCRUM-11121`
Routing policy: Codex Global Development Routing & Context Policy v2.2 (2026-09-08)
Authorized scope: implement and verify SCRUM-11121 only in `D:\Repositories\printflow-Studio`, on `master`; local commits only; no branch, worktree, alternate clone, amend, rebase, push, deploy, export feature, external-app launch, cloud diagnostics, or signing work.
Starting Git state: clean `master`, `b3dda15ab7ecf1865197abe77d352954f51aa906`.

## Original Jira authority

The CSV maps Scrum keys by row position; the exact `Description` values were read from `C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv` before Product edits.

### SCRUM-11121 (`11606`) — Implement Local Log and Screenshot Retention

> Store customer-processing logs and failure screenshots locally only, with default 30-day retention and no automatic cloud upload. Ensure cleanup respects active diagnostic references and never deletes approved production files or the user's source. Retention settings and stored locations must be visible to the operator.

### SCRUM-11091 (`11306`) — Capture Structured Meitu Automation Failure Evidence

> On Meitu automation failure, persist Session, workflow step, timestamp, current application, screenshot path, input path, expected output path, structured error code, bilingual description and retry count. Keep evidence local under the configured retention policy and provide enough context for the operator or developer to diagnose failures without silently uploading customer images or screenshots.

### SCRUM-11085 (`11300`) — Automate Meitu Enhancement and Background Removal Safely

> Implement the replaceable Meitu Processing Adapter used for AI enhancement and background removal on the validated fixed workstation. The workflow layer must know only an input file, operation and working directory and receive either a validated output or structured failure; Power Automate Desktop or Windows UI automation may be an internal production implementation detail. Every attempt starts from a fresh working copy, proceeds only through recognised UI states, validates exported files before creating a Revision, captures local failure evidence, and supports safe stop or manual takeover without resuming mid-click.

### SCRUM-11115 (`11600`) — Complete Operator UX, Localisation, Diagnostics and Offline Packaging

> Complete the production-facing PrintFlow Studio experience with the confirmed Home/Drop, workflow, review, dimensions, TIFF review, Recent Processing, Settings/Environment Check and Error Details surfaces; simplified Chinese default and English switchable without restart; local-only logs and screenshots; explicit diagnostic-package export; and a repeatable versioned offline installer with no automatic updates. Operator-facing UI must use practical production terminology and hide internal implementation terms such as Revision, Session and Adapter while preserving stable internal English states and error codes.

## Pre-change matrix

| Original AC clause | Current implementation | Existing authority | Already satisfied? | Remaining gap |
|---|---|---|---|---|
| Customer-processing logs stay local | Structured automation failures are transactionally written to SQLite `AutomationLogEntry` | `SessionService.RecordAutomationStop`, `ISessionRepository`, SQLite schema | Yes for durable structured events | Enforce retention on eligible rows |
| Failure screenshots stay local | Meitu/Photoshop captures use the configured local `Evidence` root and persist paths | `GdiWindowEvidenceSink`, `AutomationLogEntry.ScreenshotPath`, attempt context | Yes for capture | Positively classified safe expiry |
| Default 30-day retention | Configuration default and Product fallback are 30; operator preference persists | `SettingKey.LogRetentionDays`, `LoggingConfiguration`, `SettingsDefaults` | Setting only | A maintenance service must consume the resolved value |
| No automatic cloud upload | Diagnostic persistence/capture paths are local-only | Existing composition and architecture boundaries | Yes | Preserve structurally |
| Cleanup respects active diagnostic references | Exact current failure and recovery relationships exist | Persisted session/step/attempt state, retry ancestry, `ErrorDetailsSelection` | No | Build a persisted-reference protection projection |
| Never delete approved production files or source | File authorities and SCRUM-11114 safety exist | `InputSnapshot`, `Revision`, `ReviewDecision`, `PrintOutput`, workspace guards | Existing systems satisfy their own scope | Exclude every overlapping authority from diagnostic cleanup |
| Retention setting visible | Editable, validated, persisted Settings field exists | Settings view/model/repository | Yes | Reuse unchanged semantics |
| Stored locations visible | No rows show actual database/evidence paths | Composition already resolves both paths | No | Typed read-only Settings projection, bilingual labels, stable IDs |
| SCRUM-11091 evidence under configured retention | Exact-attempt Error Details and structured evidence exist | `ProcessingAttempt`, `FailureEvidence`, `AutomationLogEntry` | Partial | SCRUM-11121 retention enforcement |

## Requirements decisions

- “Logs” means the existing durable structured `AutomationLogEntry` store. The exact Jira text and primary design do not require a second text file, rolling files, rotation, or a third-party logging framework. No `ILocalDiagnosticLog`, Serilog, or NLog is added.
- Retention applies to eligible `AutomationLogEntry` rows and positively owned screenshot files. `ProcessingAttempt.FailureDetailJson`, attempts, sessions, retry links, reviews, Revisions, and outputs remain durable workflow history.
- Precedence remains: valid persisted `Setting(LogRetentionDays)` → valid `appsettings Logging.RetentionDays` → Product default 30. A settings read failure skips destructive maintenance; it is not treated as absence.
- Cleanup runs once during primary startup after successful recovery and before the shell is published. It never starts or locks Meitu/Photoshop. Failure is reported as a warning and startup continues with preservation.
- File deletion precedes the bounded database-row transaction. A crash can leave a truthful historical path to an unavailable expired file; a failed file deletion keeps its row for retry. The database transaction never encloses filesystem mutation.
- Startup cadence gives eventual expiry on the next safe application start; there is no timer or manual-cleanup button.

## Diagnostic artefact inventory

| Artefact | Classification | Rule |
|---|---|---|
| Eligible historical `AutomationLogEntry` rows | `RETENTION_MANAGED` | Expire by `AtUtc` only after reference/file planning |
| Current/recoverable diagnostic rows | `PRESERVE_UNTIL_SAFE` | Persisted active/current/recovery state overrides age |
| Positively referenced Meitu/Photoshop failure screenshots under the exact capture root | `RETENTION_MANAGED` or `PRESERVE_UNTIL_SAFE` | Delete only if every relevant reference is eligible and no Product authority overlaps |
| Ambiguous/unattributed captures and unknown `Evidence` files | `PRESERVE_UNTIL_SAFE` | Keep; name, extension, age, or broad directory is insufficient |
| Nested audit/manual-result/visual proof in `Evidence` | `OUTSIDE_THIS_POLICY` | Keep; never recurse |
| `ProcessingAttempt.FailureDetailJson`, retry history, adapter notes | `OUTSIDE_THIS_POLICY` | Durable workflow history |
| Source/InputSnapshot, all Revisions, approved PNGs, PrintOutput TIFFs, review subjects, manual-result Revisions, reservations | `OUTSIDE_THIS_POLICY` | Any overlap vetoes diagnostic deletion |
| Quarantine and reason sidecars | `OUTSIDE_THIS_POLICY` | Separate recovery contract |
| Preset/evidence contracts, Baseline/TestData, Photoshop Actions, environment probes | `OUTSIDE_THIS_POLICY` | Separate integrity/lifecycle authority |
| SQLite database/WAL/SHM, repository evidence/artifacts, future export packages | `OUTSIDE_THIS_POLICY` | Never diagnostic-file candidates |

## Active/reference definition

Protect persisted diagnostics associated with a running attempt or with the exact terminal attempt selected for the current `Failed`, `RetryRequired`, or `Interrupted` step of an `Active` or `HandedOff` session. Preserve unresolved interruption/manual-import/retry ancestry using existing recovery facts. Historical provenance alone is not active authority. Canonicalize every screenshot reference from diagnostic rows and attempt failure contexts; a shared path may be deleted only when all references are eligible. Any ambiguous relationship is preserved.

## Executable stages

### 1. Architecture and plan — complete

- Objective: establish exact AC, authorities, classifications, ordering, seams, and bounded tests.
- Planned/requested: Astra High.
- Actual: `gpt-6-astra`, `high`, verified from the isolated CLI session metadata (`01a082ed-28ba-7743-809b-b2ad94a83479`).
- Tests: not run; read-only investigation.
- Completion: this plan records decisions and pre-change evidence.
- Next: backend implementation.
- Context: `FRESH_PREFERRED`; satisfied by returning to this concise main context.

### 2. Backend retention and startup integration — complete

- Objective: add a pure classifier, narrow retention repository/filesystem seams, SQLite candidate/reference reads and bounded expiry, safe single-file deletion, typed duration authority, and fail-safe startup invocation/result.
- Dependencies: existing `AutomationLogEntry`, settings repository semantics, session/attempt/revision/output mapping, `PathGuard`/reparse conventions, `ApplicationStartup`.
- Planned/requested/actual: Sol High / `gpt-5.6-sol` high / verified in this main context.
- Required tests: duration precedence; old/recent/active/shared/unknown/overlapping authority; containment/reparse; idempotence; row transaction and unchanged attempt history; startup ordering/warning.
- Explicitly unnecessary: timers, text-log rotation, cloud/export, external-app execution, combinatorial filesystem matrix.
- Completion: targeted backend suites passed with real SQLite and real filesystem fixtures;
  independent-review path/authority corrections are included.
- Next: Settings UI.
- Context: `CONTINUE`.

### 3. Settings UI and localisation — complete

- Objective: show actual local-record and screenshot locations as read-only/copyable fields, update retention wording truthfully, and preserve bilingual/localisation behavior and stable AutomationIds.
- Planned/requested/actual: Astra High / `gpt-6-astra` high / verified isolated CLI session
  `01a08310-b093-72f3-b0f8-e2751845aaed`.
- Required tests: one bounded rendered WPF scenario across en-US/zh-CN plus localisation/architecture checks.
- Completion: both actual paths and truthful cadence rendered read-only/copyable in en-US and
  zh-CN, with stable AutomationIds.
- Next: integrated verification.
- Context: `CONTINUE`.

### 4. Verification and documentation — complete

- Objective: run directly affected suites, prove SQLite/filesystem/Error Details invariants, then run one build and one full suite because startup, repository semantics, and destructive filesystem behavior changed.
- Planned/requested/actual: Sol High / `gpt-5.6-sol` high / verified in this main context, unless evidence requires escalation.
- Completion: final build has zero warnings/errors; affected set passed 581/581; final full suite
  passed 11,609/11,609; completion report and dated re-audit delta are recorded.
- Next: independent review.
- Context: `FRESH_REQUIRED` for review only.

### 5. Independent acceptance/data-safety review — complete

- Objective: review original AC, current diff, and raw test/evidence output without implementer exploration history; report findings before any fixes.
- Planned/requested/actual: Astra High / `gpt-6-astra` high / verified fresh read-only CLI sessions.
- Completion: two review rounds produced focused path/authority regressions that failed before and
  passed after correction; final scoped review session `01a0833d-a4d4-7fa0-b087-aa2dcfc8320c`
  found no material issue or blocker.
- Context: `FRESH_REQUIRED`.

## Deliverables

- Product implementation and proportionate tests.
- `docs/printflow/scrum-11121-local-log-screenshot-retention-completion.md`.
- A dated append-only delta in `docs/printflow/original-jira-functional-coverage-reaudit.md`.
- `docs/codex/SCRUM-11121/HANDOFF.md`, kept current at substantive boundaries.
- Local commits only; nothing pushed.
