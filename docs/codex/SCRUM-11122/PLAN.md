# SCRUM-11122 — Explicit Diagnostic Package Export Plan

Task ID: `SCRUM-11122`
Routing policy: Codex Global Development Routing & Context Policy v2.2 (2026-09-08)
Authorized scope: implement and verify SCRUM-11122 only in `D:\Repositories\printflow-Studio`, on
`master`; local commits only; no branch, worktree, alternate clone or checkout, amend, rebase,
push, deploy, installer, signing, cloud/upload, telemetry, email, or external-application work.
Starting Git state: clean `master`, `57e3282574a35e4318299b78baa2e58efecc154d`.

## Original Jira authority

The exact CSV rows were read from
`C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv` before Product edits.

### SCRUM-11122 (`11607`) — Implement Explicit Diagnostic Package Export

> Allow the operator to manually export a diagnostic package only after showing exactly which
> screenshots, logs, local paths and metadata will be included and receiving explicit
> confirmation. No diagnostic content or customer imagery may upload automatically; export is a
> deliberate local action suitable for later support sharing.

### SCRUM-11115 (`11600`) — Complete Operator UX, Localisation, Diagnostics and Offline Packaging

> Complete the production-facing PrintFlow Studio experience with the confirmed Home/Drop,
> workflow, review, dimensions, TIFF review, Recent Processing, Settings/Environment Check and
> Error Details surfaces; simplified Chinese default and English switchable without restart;
> local-only logs and screenshots; explicit diagnostic-package export; and a repeatable versioned
> offline installer with no automatic updates. Operator-facing UI must use practical production
> terminology and hide internal implementation terms such as Revision, Session and Adapter while
> preserving stable internal English states and error codes.

## Pre-change matrix

| Original AC clause | Current Product capability | Existing authority | Already satisfied? | Remaining gap |
|---|---|---|---|---|
| Operator manually exports a diagnostic package | No package plan, writer, destination picker, or export command exists | Error Details is the narrow diagnostic entry point | No | Add one exact-attempt export path |
| Show exactly which screenshots, logs, local paths and metadata will be included | Error Details shows one failure's screenshot/path and structured facts, but no package preview exists | `ErrorDetailsView`, exact `SessionId + AttemptId`, `AutomationLogEntry`, Settings diagnostic locations, readiness report | Facts exist only | Build one typed plan and render all dispositions before export |
| Receive explicit confirmation before export | Error Details actions are explicit, but no export confirmation exists | Existing command/UIA patterns | No | Preview screen with deliberate Save Package action |
| No diagnostic content or customer imagery uploads automatically | Diagnostics and evidence are local; no upload client exists | SCRUM-11121 composition and architecture boundaries | Yes | Preserve structurally and state it in both languages |
| Export is a deliberate local action | No export exists | Existing owned Windows common-dialog pattern is open-file only | No | Add an owned Save dialog and local ZIP destination |
| Suitable for later support sharing | Exact failure facts and local screenshot already exist; old log/screenshot may expire | `ProcessingAttempt`, `AutomationLogEntry`, failure context, readiness report | Partial | Produce a validated, self-contained manifest and optional captured screenshot |
| Parent: explicit diagnostic-package export | No operator-reachable export | SCRUM-11115 | No | Complete this task only; installer remains separate SCRUM-11123 work |

## Package subject and authority

- Subject: exactly one terminal `ProcessingAttempt`, addressed by the `SessionId + AttemptId`
  supplied by Error Details. Historical attempts remain historical; nothing guesses a latest
  failure.
- `ISessionService.LoadErrorDetailsAsync` remains the one attempt/log/screenshot correlation
  authority. The diagnostic plan adds no alternate failure-selection rule.
- One immutable `DiagnosticPackagePlan` contains a closed role vocabulary, typed attempt facts,
  current passive environment/preset facts, local diagnostic locations, and every item disposition.
- The same plan instance feeds the preview and the writer. The writer receives no session id,
  directory, wildcard, or arbitrary add-file list from the UI.

## Artefact inventory

| Candidate | Classification | Original-AC/support rationale |
|---|---|---|
| `manifest.txt` | `INCLUDE` | One human-readable authority enumerates subject, facts, paths, contents, omissions, and local-only invariant |
| Exact attempt failure facts from `ProcessingAttempt` | `INCLUDE_AS_METADATA_ONLY` | The named log/metadata need; survives diagnostic-row retention |
| Correlated `AutomationLogEntry` identity/timestamp | `INCLUDE_AS_METADATA_ONLY` when available; otherwise `CONDITIONAL`/unavailable | Exact log fact named by AC; no substitution after retention |
| Failure screenshot produced by `GdiWindowEvidenceSink` | `CONDITIONAL`: include only when available and positively classified | Screenshot is explicitly named diagnostic content; package-level preview plus deliberate Save is the exact AC consent |
| Screenshot stored path/status | `INCLUDE_AS_METADATA_ONLY` | Makes an expired/refused screenshot truthful rather than silently omitted |
| Managed input and expected output paths | `INCLUDE_AS_METADATA_ONLY` | Exact local paths are named by the AC; bytes remain excluded |
| Diagnostic SQLite and Evidence-root locations | `INCLUDE_AS_METADATA_ONLY` | Existing operator-visible support locations explain where retained diagnostics live |
| PrintFlow version and package creation time | `INCLUDE_AS_METADATA_ONLY` | Bounded application/package identity for support |
| Preset identity and passive readiness summary | `INCLUDE_AS_METADATA_ONLY` | Existing current metadata explains the workstation state; no live application check is run |
| Original customer source and `InputSnapshot` bytes | `EXCLUDE` | No Jira permission to package customer imagery; path metadata does not grant byte authority |
| Revision artwork, approved PNG, production TIFF | `EXCLUDE` | Production authorities are not diagnostic evidence and the AC grants no byte inclusion |
| Manually imported artwork | `EXCLUDE` | Operator selection for workflow import is not support-package consent |
| Recovery/quarantine evidence and sidecars | `EXCLUDE` | Separate recovery authority; not named by this exact-attempt package |
| Unknown/nested/arbitrary Evidence files | `EXCLUDE` | Default-deny; no directory recursion or name/extension inference |
| SQLite database/WAL/SHM bytes | `EXCLUDE` | Only the path is useful/required; database bytes would broaden personal/workflow data without AC authority |

## Screenshot consent decision

The exact AC explicitly names screenshots among content shown before confirmation; it does not
require a separate per-file checkbox. An available, positively owned failure capture is therefore
included in the immutable plan, visibly labelled as containing what was visible in the failed
application window, and exported only after the operator presses Save Package on the preview.
Missing/expired evidence is `Unavailable`; an unowned/reparse/unknown path is `ExcludedByPolicy`.
Customer production imagery is not an optional control because the AC never authorizes it.

## Manifest and path disclosure

The archive has one UTF-8 `manifest.txt`; there is no parallel JSON authority. It records only
typed facts the Product knows. The exact managed input, expected output, screenshot, database, and
Evidence-root paths are included because SCRUM-11122 explicitly names local paths and these are
already operator-visible diagnostic facts. No original customer-source path is recovered or
invented, and readiness expected/observed path pairs are not added beyond the bounded existing
readiness summary.

## Write safety, destination, retention, cancel and failure

- The operator chooses a `.zip` path using the Windows Save dialog after preview. A
  `PrintFlow-Diagnostics-<UTC timestamp>-<attempt prefix>.zip` name is suggested.
- Existing targets are never overwritten. The writer chooses a collision-safe numeric suffix and
  reports the actual saved path.
- Build in a PrintFlow-owned temporary directory, reopen and validate, verified-copy into an exact
  temporary sibling of the destination, reopen and validate again, move without overwrite, then
  independently reopen the final archive. Clean owned staging paths on success/failure.
- Exact archive entries must equal the plan: `manifest.txt` and, only when included,
  `failure-screenshot.png`. No recursive enumeration, wildcard copy, or arbitrary file role exists.
- The screenshot reader repeats direct-child capture-name, canonical path, reparse, length, and
  modification-time checks immediately before read and holds a non-delete-sharing read handle. If
  evidence changed/disappeared, export fails truthfully; it never substitutes another file.
- Export mutates no session, attempt, log, screenshot, source, Revision, review, or output.
  Internal retention continues normally. The successful ZIP is operator-owned and outside the
  internal retention store; no second cleanup policy is added.
- Back, preview cancellation, and Save-dialog cancellation write no final package and change no
  workflow state. A failed build leaves no partial final ZIP and returns a bounded localized error.
- UI text states: “The package is saved locally. Nothing is uploaded automatically.” in en-US and
  zh-CN.

## Executable stages

### 1. Discovery and architecture — complete

- Objective: read exact Jira authority and current implementations; establish the default-deny
  inventory, exact-attempt subject, plan/writer seam, and bounded tests.
- Planned/requested: Sol High / `gpt-5.6-sol` high.
- Actual: `UNVERIFIED`; this session exposes no runtime model/effort metadata or live switch.
- Tests: run existing Error Details, retention, localisation, and WPF/UI tests before Product edits.
- Result: exact Jira rows and current authorities were read; the matrix/inventory above was fixed
  before Product edits; the relevant pre-change baseline passed 46/46.
- Context result: `CONTINUE`.

### 2. Backend plan, evidence classification, and archive writer — complete

- Objective: typed plan and roles; exact-attempt facts; owned screenshot inspection; single-manifest
  renderer; staged, collision-safe, exact-entry writer with final reopen validation.
- Planned/requested: Sol High / `gpt-5.6-sol` high.
- Actual: `UNVERIFIED` until real runtime metadata exists; `MODEL_SWITCH_UNAVAILABLE` otherwise.
- Required tests: plan allowlist/default-deny/exact attempt; expired log/screenshot truth; no source,
  Revision, output, unknown Evidence, recursion, or network client; exact archive entries; collision;
  evidence-change and write-failure cleanup; unchanged source evidence.
- Explicitly unnecessary: package signing, cloud submission, database export, large filesystem
  permutation matrix, external applications.
- Result: immutable default-deny plan, exact-attempt facts, owned screenshot inspection, one
  manifest and staged/collision-safe/exact-entry/finally-reopened writer implemented. Focused final
  package/architecture/UI set passed 16/16; expanded affected set passed 168/168.
- Context result: `CONTINUE`.

### 3. Preview, Save dialog, localisation and accessibility — complete

- Objective: Error Details entry point; actual-plan preview; local-only notice; ordinary focusable
  controls; stable AutomationIds; Save and Back; en-US/zh-CN; owned Save dialog.
- Planned: bounded Astra High if and only if a real switch with runtime verification is available;
  otherwise continue in the current safe context under the user's conditional routing instruction
  and record `MODEL_SWITCH_UNAVAILABLE` without claiming Astra.
- Required tests: real graph from Error Details to preview to scripted destination to local ZIP;
  preview-plan identity; explicit Save; cancel writes nothing; bilingual rendering; keyboard/UIA;
  no binding errors.
- Actual routing: `UNVERIFIED / MODEL_SWITCH_UNAVAILABLE`; no Astra switch is claimed.
- Result: real composed Error Details → actual-plan preview → Save → scripted owned destination →
  local ZIP passed with en-US/zh-CN, UIA providers, keyboard traversal and no binding errors. Three
  rendered proof images were visually inspected under `artifacts/SCRUM-11122/wpf/`.
- Context result: `CONTINUE`.

### 4. Verification, reassessment and documentation — complete

- Objective: independently inspect produced ZIP; reread exact task and parent AC; record completion,
  append-only re-audit delta, final Git state and routing truth.
- Planned/requested: Sol High / `gpt-5.6-sol` high.
- Required validation: targeted tests, solution build, `git diff --check`. Run one final full suite
  because the implementation changes shared navigation/composition, Error Details projection, and
  filesystem/package safety boundaries; do not repeat it after minor edits.
- Result: Release build 0 warnings/0 errors; final suite 11,645/11,645 after one exact filename
  deletion-allowlist correction; `git diff --check` clean apart from line-ending notices. Fresh ZIP
  readers verified exact entries and privacy exclusions. SCRUM-11122 is FULL; SCRUM-11115 remains
  PARTIAL solely for SCRUM-11123. Completion report and append-only audit delta created.
- Review result: `SELF-REVIEW ONLY / INDEPENDENT REVIEW NOT COMPLETED`; no fresh reviewer approval
  is claimed. The local commit is created only after final checks; nothing is pushed.
- Context result: `COMPLETE`.

## Deliverables

- Product implementation and proportionate tests.
- `docs/printflow/scrum-11122-diagnostic-package-export-completion.md`.
- Dated append-only delta in `docs/printflow/original-jira-functional-coverage-reaudit.md`.
- `docs/codex/SCRUM-11122/HANDOFF.md` kept current at substantive boundaries.
- Local commits only; nothing pushed.
