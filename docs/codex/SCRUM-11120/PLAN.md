# SCRUM-11120 — Structured Error Details and Recovery UI

Policy: Codex Global Development Routing & Context Policy v2.2 (2026-09-08)  
Mode: `PLAN_EXECUTE`  
Repository: `D:\Repositories\printflow-Studio`  
Required branch: `master`  
Starting HEAD: `e4cfb05a2e0971d86f87dbae4d06c2608398e763`  
Starting worktree: clean  
Authorization boundary: SCRUM-11120 only; local commits; no branch, worktree, push, deploy, retention engine, general log viewer, or signing work.

## Original acceptance criterion

CSV source: `C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv`, work item 11605.

> Provide an Error Details page showing workflow and step, structured error code, bilingual description, captured screenshot, input path, expected output path, retry information and available recovery actions such as retry or manual processing. Do not expose a misleading Continue action when the underlying state is unrecognised or output has not validated.

The exact parent SCRUM-11115, SCRUM-11091 and SCRUM-11121 rows were also read before Product edits. Their status will be reassessed independently; they are not implementation authority for this slice.

## Pre-change matrix

| Original SCRUM-11120 clause | Current data/source | Already available? | Operator surface available? | Remaining gap |
|---|---|---:|---:|---|
| Error Details page | Existing transient view-model navigation and shell data templates | Navigation exists | No | Add one destination reached from the current failure surface; Back returns to refreshed processing context |
| Workflow and step | Session aggregate and exact `ProcessingAttempt.Step` | Yes | Only on processing screen | Project them for the selected attempt |
| Structured error code | `ProcessingAttempt.Failure` / `FailureDetailJson` | Yes for structured failures; interrupted attempts intentionally have no invented failure | Current failure line only | Show stable English code and truthful absence for interruption |
| Bilingual description | `OperationFailure.MessageKey`, `DisplayNames.Failure`, persisted/current operator culture | Yes | Current failure line only | Localise the page and description in en-US/zh-CN |
| Captured screenshot | Attempt failure context `evidencePath`; queryable `AutomationLogEntry.ScreenshotPath` | Usually; AutomationLog is diagnostic history | No | Correlate enrichment by persisted attempt identity, reuse WIC preview, keep missing files non-fatal |
| Input path | `InputRevisionId` → aggregate `Revision.File` → `IWorkspace.ResolveAbsolute` | Yes when an input Revision exists | No | Show the managed input path; never mislabel the original customer source |
| Expected output path | Exact runtime request destination; not generally frozen and unsafe to reconstruct from mutable output name/preset | Partial | No | Persist the exact path in closed failure context only after it exists; otherwise label it not established |
| Retry information | `RetrySequence`, `RetryOfAttemptId` | Yes | No | Show attempt number/prior attempts without exposing internal IDs |
| Recovery actions | Engine `AvailableCommands`; existing Retry, HandOff, Re-enter Automation and manual importer | Yes | Current failure controls | Typed service returns actions for the exact still-current failure and revalidates before dispatch |
| No misleading Continue | Existing command-specific controls and state machine | Yes | Current failure surface already avoids generic Continue | Add no generic Continue; historical failures expose no stale actions |

## Diagnostic authority

- Add one Workflow-owned typed Error Details service/read model addressed by `SessionId + AttemptId`.
- The service loads the aggregate and diagnostic log, validates exact membership/terminal status, resolves paths and image evidence, and asks the existing workflow engine/service authority what actions are legal now.
- The App must not query or join `ProcessingAttempt`, `AutomationLogEntry`, `Revision`, and `SessionStep` itself.
- The attempt/state machine remains authoritative for workflow state, currentness, retry legality and lineage. AutomationLog may enrich diagnostics only.
- Add a closed persisted failure-context relationship containing the exact attempt ID. Correlate a log row only when the persisted ID, session, step and failure agree. Legacy/unlinked rows are not guessed by recency or timestamp.
- Preserve crash-recovered `Interrupted` attempts without an invented `AutomationLogEntry` or invented `FailureCode`.
- Resolve managed input from `InputRevisionId`. Keep customer original source distinct if it is ever shown.
- Persist an exact expected-output path only after the output destination is actually constructed. Do not rebuild historical names from current naming/preset state. Missing legacy evidence is “Not recorded”; a stage that never established a destination is “Not established”.
- Reuse the existing bounded WIC decoder behind a diagnostics-only persisted-path port. Do not create a Revision, change the screenshot, or auto-open it.

## Execution stages and routing

### 1. Typed diagnostics and recovery boundary

- Objective: implement exact attempt selection, persisted correlation, path/evidence projection and stale-safe action dispatch through existing authority.
- Modules: Domain evidence context; Workflow read model/service and `SessionView` entry identity; Infrastructure WIC reuse; targeted service/persistence tests.
- Planned model/effort: Sol High — recovery, persistence and state-machine legality.
- Requested/actual: current primary task; starting model was user-described as Sol High, runtime identity otherwise `UNVERIFIED`.
- Tests: focused diagnostics, failure persistence, AutomationLog, retry/recovery/manual-processing boundaries.
- Explicitly unnecessary: general logging/retention/search/export matrices.
- Completion: exact/no-log/stale/path/screenshot/action representative cases pass.
- Next: WPF UI slice.
- Context: `CONTINUE`.

### 2. WPF Error Details destination

- Objective: add the page, view model, normal navigation entry, localised labels, copyable detail/path controls, keyboard/UIA identities and screenshot rendering.
- Modules: App navigation/composition/shell; Session failure controls; new Error Details VM/XAML; en-US/zh-CN resources; rendered/UIA tests.
- Planned model/effort: Astra High — mandatory UI/XAML/interaction route.
- Requested/actual: use a native alternate execution context with requested `gpt-6-astra` / `high`; actual execution must be recorded from available evidence, otherwise `UNVERIFIED` and no false switch claim.
- Tests: rendered representative failure with existing/missing image, localisation, focus/UIA, no binding errors, no Continue.
- Explicitly unnecessary: Home/Recent entry (not in original AC), coordinate automation, general log browser.
- Completion: UI is reachable, truthful, localised, keyboard-operable and invokes existing authority.
- Next: integrated validation.
- Context: `CONTINUE` across the natural backend/UI handoff.

### 3. Integrated validation

- Objective: prove the operator path and durable recovery outcome with deterministic adapters only.
- Planned model/effort: Sol High — test strategy and persistence readback.
- Evidence: targeted suites then build; rendered UIA failure → Error Details → Retry/manual action; SQLite/service readback proves Waiting/HandedOff semantics, retained failed history, no fabricated success/new attempt/external launch.
- Full suite decision: justified once shared Session projection, failure writing, navigation/composition and recovery plumbing change; run at most once after targeted checks are green.
- Completion: required checks pass with actual counts and no binding warnings.
- Next: independent acceptance review.
- Context: `CONTINUE`.

### 4. Independent acceptance review

- Objective: review original AC, current diff and direct evidence independently; reassess SCRUM-11120, SCRUM-11091, SCRUM-11121 and parent SCRUM-11115 factually.
- Planned model/effort: Astra High, fresh isolated agent context — recovery/AC/UI audit.
- Requested/actual: record native agent request and actual-verification limitation honestly.
- Completion: findings resolved or explicitly block PASS.
- Next: documentation and local commit.
- Context: `FRESH_REQUIRED`.

### 5. Completion records and local commit

- Objective: create `docs/printflow/scrum-11120-error-details-recovery-ui-completion.md`, append only to the historical reaudit, reread the exact CSV clauses, and commit locally.
- Planned model/effort: current task, bounded documentation/coordination.
- Completion: clean, factual report with exact AC, evidence, remaining retention gaps, Git state and no push.
- Context: `CONTINUE`.

## Stop conditions

Stop only for a genuine data-loss risk, unavailable required UI capability, missing permission, or impossible required independent review. Ordinary in-scope failures are investigated and repaired without asking for stage approval.
