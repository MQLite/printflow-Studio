# SCRUM-11121 — Local Log and Screenshot Retention Completion

Date: 9 September 2026
Repository: `D:\Repositories\printflow-Studio`
Task: `SCRUM-11121` — Implement Local Log and Screenshot Retention

## Original Jira authority

The exact source rows were read from
`C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv` before Product edits. The
CSV maps the Jira keys below to Work Item `11606`, `11306`, `11300`, and `11600` respectively.

### SCRUM-11121 (`11606`)

> Store customer-processing logs and failure screenshots locally only, with default 30-day
> retention and no automatic cloud upload. Ensure cleanup respects active diagnostic references
> and never deletes approved production files or the user's source. Retention settings and stored
> locations must be visible to the operator.

### SCRUM-11091 (`11306`)

> On Meitu automation failure, persist Session, workflow step, timestamp, current application,
> screenshot path, input path, expected output path, structured error code, bilingual description
> and retry count. Keep evidence local under the configured retention policy and provide enough
> context for the operator or developer to diagnose failures without silently uploading customer
> images or screenshots.

### Parent SCRUM-11085 (`11300`)

> Implement the replaceable Meitu Processing Adapter used for AI enhancement and background removal
> on the validated fixed workstation. The workflow layer must know only an input file, operation and
> working directory and receive either a validated output or structured failure; Power Automate
> Desktop or Windows UI automation may be an internal production implementation detail. Every
> attempt starts from a fresh working copy, proceeds only through recognised UI states, validates
> exported files before creating a Revision, captures local failure evidence, and supports safe stop
> or manual takeover without resuming mid-click.

### Parent SCRUM-11115 (`11600`)

> Complete the production-facing PrintFlow Studio experience with the confirmed Home/Drop,
> workflow, review, dimensions, TIFF review, Recent Processing, Settings/Environment Check and
> Error Details surfaces; simplified Chinese default and English switchable without restart;
> local-only logs and screenshots; explicit diagnostic-package export; and a repeatable versioned
> offline installer with no automatic updates. Operator-facing UI must use practical production
> terminology and hide internal implementation terms such as Revision, Session and Adapter while
> preserving stable internal English states and error codes.

## Pre-change matrix

| Original AC clause | Current implementation before this slice | Existing authority | Already satisfied? | Remaining gap before this slice |
|---|---|---|---|---|
| Customer-processing logs stay local | Structured failures were transactionally stored in SQLite `AutomationLogEntry` | `SessionService`, `ISessionRepository`, SQLite schema | Yes | Enforce retention on eligible rows |
| Failure screenshots stay local | Meitu/Photoshop capture wrote under the configured local `Evidence` root and persisted its path | `GdiWindowEvidenceSink`, attempt failure context, `AutomationLogEntry.ScreenshotPath` | Yes | Positively classify which captures may expire |
| Default 30-day retention | Product/configuration default and persisted Settings preference existed | `SettingKey.LogRetentionDays`, `LoggingConfiguration`, `SettingsDefaults` | Setting only | Consume the resolved value in real maintenance |
| No automatic cloud upload | Diagnostic storage and capture paths were local only | Existing composition and architecture boundaries | Yes | Preserve structurally |
| Respect active diagnostic references | Current failure and recovery relationships were persisted | Session/step/attempt state, retry ancestry, Error Details selection | No | Protect current/recoverable references during cleanup |
| Never delete approved production files or source | File authorities and SCRUM-11114 safety existed | `InputSnapshot`, Revision/review/output records and workspace guards | Existing authorities only | Veto any diagnostic candidate that overlaps them |
| Retention setting visible | Editable, validated, persisted Settings field existed | Settings view/model/repository | Yes | Make its execution wording truthful |
| Stored locations visible | Neither actual database nor Evidence path was displayed | Composition resolved both paths | No | Add typed read-only bilingual Settings facts |
| SCRUM-11091 retention | Exact-attempt Error Details and structured evidence existed | `ProcessingAttempt`, `FailureEvidence`, `AutomationLogEntry` | Partial | Enforce the configured local retention policy |

## Diagnostic artefact inventory

| Artefact | Classification | Implemented rule |
|---|---|---|
| Eligible historical `AutomationLogEntry` rows | `RETENTION_MANAGED` | Expire by `AtUtc` after reference and file planning |
| Rows for current or recoverable diagnostics | `PRESERVE_UNTIL_SAFE` | Persisted active/current/recovery state overrides age |
| Positively referenced direct Meitu/Photoshop capture under the exact Evidence root | `RETENTION_MANAGED` or `PRESERVE_UNTIL_SAFE` | Delete only when every relevant reference is old/safe and no Product authority overlaps |
| Unknown, malformed, unattributed, nested, read-only, or reparse-backed Evidence content | `PRESERVE_UNTIL_SAFE` | Keep; directory, age, extension, or filename alone does not grant authority |
| `ProcessingAttempt.FailureDetailJson`, attempts, retry history, sessions | `OUTSIDE_THIS_POLICY` | Durable workflow history; never expired here |
| Source/InputSnapshot, Revisions, approved PNGs, PrintOutput TIFFs, review subjects, reservations, former authoritative paths, manual-result sources | `OUTSIDE_THIS_POLICY` | Exact path overlap vetoes byte deletion |
| Quarantine, sidecars, preset/evidence contracts, test evidence, Photoshop Actions | `OUTSIDE_THIS_POLICY` | Owned by a different lifecycle contract |
| SQLite database/WAL/SHM and future export packages | `OUTSIDE_THIS_POLICY` | Never file candidates |

## Local-log meaning and implementation

“Customer-processing logs” means the existing durable, structured SQLite
`AutomationLogEntry` records. The original AC does not require a rolling text file, daily file,
third-party logger, or second workflow authority. No `ILocalDiagnosticLog`, Serilog, NLog, cloud
logger, telemetry exporter, HTTP upload, email, or diagnostic-package export was added.

Retention now expires eligible old `AutomationLogEntry` rows through the narrow
`IDiagnosticRetentionRepository`. `ProcessingAttempt.FailureDetailJson`, the failure identity,
session/step/attempt history, reviews, Revisions, and outputs are deliberately retained.

The SQLite database containing those structured records is therefore the operator's **Local log
location**. The separately displayed **Screenshot/evidence location** is the actual resolved local
Evidence root.

## Retention-duration authority

`DiagnosticRetentionService` resolves the number of days in the already-established order:

1. a valid persisted SQLite `Setting(LogRetentionDays)`;
2. valid `Logging.RetentionDays` from `appsettings.json`;
3. the Product default, **30 days**.

Values must remain within the existing `1..3650` Settings boundary. An unreadable settings store
does not become “setting absent”: maintenance warns and preserves. Invalid legacy values fall
through to valid configuration/default, matching Settings display semantics.

The cutoff comes from the injected `TimeProvider`. Candidate reads are stable `(AtUtc, Id)` pages
of 128 and row expiry repeats the cutoff check in SQLite.

## Active-reference definition

A diagnostic is protected when persisted state shows a running attempt, or the exact current
terminal attempt selected for the current `Failed`, `RetryRequired`, `Interrupted`, or `Processing`
step of an `Active` or `HandedOff` session. Retry/manual-import ancestry back to the unresolved
attempt is also protected. A legacy row without exact attempt identity is protected only when it
names the current unresolved step.

Protection does not depend on whether an Error Details window happens to be open. Historical
provenance alone does not make a file immortal. Every log and attempt failure-context reference to
a candidate path is read; a shared capture is deleted only when every relevant reference is old and
none is current/recoverable.

Path matching uses canonical absolute Windows identity, including alternate separators, dot
segments, and Unicode case aliases. SQLite receives deterministic `printflow_path_key` functions
that apply `Path.GetFullPath(...).ToUpperInvariant()` rather than ASCII-only `NOCASE` matching.

## Database-versus-file retention

Old structured diagnostic rows and positively owned capture files are retention managed. Durable
workflow records are not. A historical Error Details subject therefore continues to exist after its
diagnostic row or screenshot expires; if the file is gone, the existing preview path reports
“Evidence image unavailable” while retaining the truthful historical path stored on the attempt.

A capture is eligible only when all of the following hold:

- it has a durable diagnostic reference;
- its canonical path is a direct child of the exact Evidence root;
- its name matches the closed layout emitted by the capture writer;
- all references are older than the cutoff and not protected;
- it is not any source, InputSnapshot authority, Revision/current or former working path,
  PrintOutput, output reservation, or manual-result source;
- immediate deletion checks still prove containment, non-reparse ancestry, non-read-only state,
  and file modification time older than the cutoff.

Migration `0016_manual_result_source_authority.sql` adds the exact immutable external source path to
`MANUAL_RESULT_IMPORT` attempts, because human-readable adapter notes cannot safely carry file
authority. New manual imports persist it with the opening attempt transaction. If a legacy manual
import has no recorded source path, cleanup conservatively treats every current file candidate as
potential source bytes: diagnostic rows may expire, but the bytes remain.

## Ordering and crash behaviour

For each bounded page, the service reads all references and Product authorities, loads relevant
session aggregates, and builds a pure plan. It then deletes each individually authorised file
**before** deleting the associated database rows. Successful and already-missing file results add
their row IDs to one bounded SQLite transaction. A failed/preserved file keeps the row for a later
safe retry. Rows whose file is retained by another Product authority, or whose shared file is still
needed by a newer/current diagnostic, can expire without deleting those bytes.

This ordering accepts one truthful crash state: the filesystem deletion may complete while the old
diagnostic row remains. Error Details already treats an unavailable evidence file as normal. The
reverse—removing the only durable ownership/reference record before attempting destructive
filesystem work—is avoided. No filesystem operation is placed inside an impossible-to-rollback
database transaction. A row-count mismatch rolls back the whole page.

Repeated maintenance is idempotent. Already-missing eligible files are success; rows deleted in an
earlier transaction no longer appear; unknown or unsafe files remain untouched.

## Execution cadence and startup behaviour

Maintenance runs once on primary application startup after migrations and successful startup
recovery, and before localisation restoration and shell publication. Recovery therefore settles
persisted workflow and lock facts before classification, while no operator command or external-app
automation can start concurrently.

Retention never starts Meitu or Photoshop and never acquires, clears, or repairs the automation
lease. If recovery leaves the lease held, retention defers. Read/classification/filesystem/SQLite
failure produces a localised non-technical warning and startup continues with uncertain evidence
preserved. Cancellation remains cancellation. No timer, scheduler, session-completion hook, or
manual cleanup button was added; the next safe application start provides eventual cleanup.

## Operator-visible locations

Settings now renders the real `SqliteConnectionFactory.DatabasePath` and the resolved Evidence root
as read-only, focusable, selectable/copyable text boxes. The stable automation IDs are:

- `Settings.LocalLogLocation`
- `Settings.ScreenshotLocation`

Both labels and hints are present in en-US and zh-CN. The retention hint now says bounded cleanup
runs after recovery at safe startup and that a saved change applies on the next startup. It does not
claim that pressing Apply immediately deletes anything. No Explorer launch or log-browser page was
added.

## Scope exclusions

This slice does not modify SCRUM-11114 session/completion cleanup, recursively clean directories,
delete quarantine, export a diagnostic package, upload diagnostics, add a general log browser,
run an external application, change production output disposal, or add signing/certificate
infrastructure. It does not infer deletion from extension, age, or presence under a broad folder.

## Targeted, red/green, and independent-review evidence

Twelve new tests answer distinct AC or destructive-boundary questions:

- eight real SQLite/filesystem retention cases cover old deletion with attempt-history survival;
  active and shared-newer preservation; unknown/source/Revision/output protection; exact durable
  manual-result source authority; legacy unknown manual source; three-level duration precedence;
  reparse-root refusal; held-lease deferral; and second-run idempotence within the representative
  expiry test;
- two startup cases prove after-recovery ordering and warn/continue behaviour for a returned failure
  or unexpected exception;
- one Settings case proves an invalid legacy value shows the same configured fallback consumed by
  retention;
- one composed, rendered WPF case proves both actual paths in en-US and zh-CN, read-only state,
  keyboard reachability, selection/copy-command availability, stable IDs, and viewport bounds.

The first independent review produced four focused regressions for canonical path aliases,
manual-result source authority, and the Settings maximum boundary. Those cases failed **4/4** before
the corrections and passed **4/4** afterward. A second review produced three regressions for a
Unicode-case source/active alias and legacy manual-source uncertainty; they failed **3/3** before
the corrections and passed **3/3** afterward. The expanded affected set, including migrations and
manual-result import, then passed **581/581**.

The final fresh, read-only Astra High review accepted both corrections with no material finding or
blocker. This review materially strengthened path identity, legacy-source preservation, and
Settings/retention consistency.

## SQLite, filesystem, Error Details, and WPF proof

The representative integration path uses real `SessionService`, real SQLite repositories, real
settings rows, real diagnostic events, real Evidence files, and the production retention service.
Independent assertions after cleanup prove that the expired owned capture and its log row are gone,
the `ProcessingAttempt` and failure context remain, active/shared/source/manual-source bytes remain,
and a second cleanup makes no further authority change. Repository reads and SQLite transactions
remain valid.

The existing Error Details preview contract remains unchanged and truthful for an expired/missing
capture. The WPF proof uses the repository's rendered windowless harness and UI Automation
providers; it uses no coordinate input and launches no external application.

## Build and complete-suite evidence

The final solution build with the repository's .NET SDK completed with **0 warnings / 0 errors**.

One complete suite was justified because this slice changes startup lifecycle, shared SQLite
semantics, and a destructive filesystem boundary. The first run recorded **11,605 passed / 1 failed
/ 0 skipped**; the only failure was the architecture allowlist that deliberately confines direct
file deletion. The allowlist was extended by exact filename for `LocalDiagnosticFileStore`, with its
retention safety enforced by the integration tests. The final raw result is:

> **11,609 passed / 0 failed / 0 skipped** in 5m29s

Raw evidence:
`artifacts/SCRUM-11121/full-suite-final/scrum-11121-full-final.trx`. The accepted baseline was
11,597; the increase is exactly the twelve new tests. No test was removed, skipped, or weakened.

## Jira reassessment

### SCRUM-11121: PARTIAL → FULL

Every original clause is now met: existing customer-processing logs and failure screenshots remain
local only; the effective setting drives default 30-day bounded cleanup; active/recoverable and
shared references override age; source, approved, Revision, output, review, and manual-result
authorities veto byte deletion; unknown files are preserved; and Settings shows both the retention
preference and actual stored locations.

### SCRUM-11091: PARTIAL → FULL

The exact-attempt record already persisted Session, step, timestamp, current application, managed
input, established expected output, screenshot path/display, structured code, bilingual guidance,
and retry sequence. SCRUM-11121 closes its only remaining material clause: those local Meitu
diagnostics now follow the configured retention policy without silent upload.

### Parent SCRUM-11085: PARTIAL → FULL

Reassessed independently against Work Item `11300`, not inferred from child labels. Current Product
code and accepted completion deltas establish the replaceable Meitu adapter boundary; workflow-only
input/operation/working-directory contract; fresh working copies; recognised-state guards; export
validation before Revision creation; structured local failure evidence; clean stop; manual takeover;
validated manual-result import; and no mid-click resume. SCRUM-11121 closes the final recorded
evidence-retention gap. No parent clause remains open.

### Parent SCRUM-11115: remains PARTIAL

Its local-only logs/screenshots clause is now complete, but the Epic is not complete. Current open
clauses remain SCRUM-11116's format-specific unsupported-file feedback concern; SCRUM-11117's
thumbnail and delete-record action; SCRUM-11122's absent operator-controlled diagnostic-package
export/preview/consent flow; and SCRUM-11123's absent repeatable versioned offline installer and
install/configure/rollback procedure.

## Routing and Git state

The installed global routing workflow was followed. Planning/architecture ran as requested and
actually verified on `gpt-6-astra` high in isolated session
`01a082ed-28ba-7743-809b-b2ad94a83479`. Backend, integration, correction, and final verification ran
as requested and actually verified on `gpt-5.6-sol` high in the main task. The bounded Settings/WPF
slice ran as requested and actually verified on `gpt-6-astra` high in isolated session
`01a08310-b093-72f3-b0f8-e2751845aaed`. Independent reviews ran fresh/read-only as actual verified
`gpt-6-astra` high; the final accepting session was
`01a0833d-a4d4-7fa0-b087-aa2dcfc8320c`.

Work started from clean `master` at `b3dda15ab7ecf1865197abe77d352954f51aa906`. Product/tests are
committed locally as `66d57dd` and this documentation is recorded in the following local commit on
the same branch. No branch, worktree,
alternate clone, amend, rebase, push, deployment, Co-Authored-By trailer, or AI attribution was
created.

**PASS — SCRUM-11121 LOCAL LOG AND SCREENSHOT RETENTION VERIFIED**
