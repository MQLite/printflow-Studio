# SCRUM-11076 — Transactional SQLite Metadata Persistence

| Item | Value |
| --- | --- |
| Date | 8 September 2026 |
| Repository | `d:\Repositories\printflow-Studio`, branch `master` (no branch, worktree or clone created) |
| Baseline commit | `feab51e` — *Add the editable Output Name and source context to Workflow Selection* |
| Jira authority | `C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv`, read before any Product edit |
| Scope | The two record types the AC names that migration `0001` created and nothing wrote: `AutomationLog` and `Setting` |

---

## 1. The exact original acceptance criteria

The CSV holds 79 rows keyed `11000`–`11714`. The Jira keys follow the fixed offset the existing
re-audit established — **SCRUM key = row position, starting at SCRUM-11060** — so SCRUM-11076 is
CSV work item `11108` and its parent SCRUM-11068 is CSV work item `11100`. Both rows were read
from the CSV before any Product edit and are quoted verbatim below.

### SCRUM-11076 — CSV Work Item `11108`, Task, parent `11100`, 5 points, High

> **Implement Transactional SQLite Metadata Persistence**
>
> Persist ProcessingSession, Revision, ProcessingAttempt, ReviewDecision, PrintOutput,
> AutomationLog and Setting metadata in SQLite transactions while keeping images on the local
> file system. Acceptance: restart preserves valid Session state and history; persistence APIs
> are isolated from the UI; records remain consistent across failures; and schema decisions
> preserve the confirmed invariants without adding out-of-scope job or user-management concepts.

Labels: `printflow, mvp, sqlite, persistence, transactions, recovery`.

The CSV confirms the "seven record types" wording that the historical audit used: the AC names
`ProcessingSession`, `Revision`, `ProcessingAttempt`, `ReviewDecision`, `PrintOutput`,
`AutomationLog` and `Setting`, and nothing else. `SessionStep`, `InputSnapshot` and
`AutomationLock` are persisted by the Product but are not named by this AC — they belong to
SCRUM-11071/11073/11074 and to Epic 11500's automation lock.

### Parent SCRUM-11068 — CSV Work Item `11100`, Epic, High

> **Build PrintFlow Studio Core Desktop and Workflow Foundation**
>
> Build the WPF/.NET desktop foundation, fixed workflow state model, SQLite metadata persistence,
> immutable input snapshot and revision rules, controlled local file workspace and collision-safe
> naming required by the PrintFlow Studio MVP. The MVP supports one operator and exactly one input
> image per ProcessingSession and intentionally excludes Job, Order, Customer, multi-user,
> batch-processing and general configurable workflow concepts. Attempts and revisions must remain
> separate; only fully exported, readable and hashed files may become reviewable revisions;
> approvals are bound to exact hashes; and the UI must not directly mutate workflow state, execute
> SQL, delete files or control third-party applications.

Labels: `printflow, mvp, phase-2, wpf, dotnet, workflow, sqlite, revisions, file-workspace`.

### Supporting design authority (not a substitute for the AC)

`PRINTFLOW_STUDIO_MVP_DESIGN_EN.md` §17.6 is the only place the two disputed record types are
given meaning:

> AutomationLog stores structured errors and screenshot paths. Setting stores UI language,
> default output directory, production DPI, safety margin, fixed-environment details,
> colour-settings confirmation, and log retention.

§16.2 restates the AC's isolation clause: *"Persists Session, Revision, Attempt, ReviewDecision,
PrintOutput, AutomationLog, and Setting metadata in SQLite transactions. The UI does not execute
SQL directly."*

---

## 2. Pre-change entity matrix

Built by reading current source, not from the historical audit row. The audit's two claims were
re-verified: `grep` for `AutomationLogEntry` and `Setting` across `src/**/*.cs` returned **zero**
Product hits for either table — the only matches were unrelated English prose in
`IUiElementProvider.cs` and `UiaElementProvider.cs`.

| AC record type | Current Product implementation | Evidence | Already satisfied? | Remaining gap |
| --- | --- | --- | --- | --- |
| `ProcessingSession` | Full typed write/read through `ISessionRepository`, one transaction per command | `SqliteSessionRepository.CommitAsync` / `LoadAsync`; `SessionServiceTests` | **Yes** | None |
| `Revision` | Insert + invalidate + retention transition, all inside the shared transaction; DB triggers enforce immutability | `InsertRevisionAsync`, `InvalidateRevisionAsync`, `Revision_Immutable_Update` | **Yes** | None |
| `ProcessingAttempt` | Two-transaction lifecycle (Running, then terminal); structured failure in `FailureDetailJson`; DB CHECK binds output to `SUCCEEDED` | `UpsertAttemptAsync`; `ProductionFailurePersistenceTests` | **Yes** | None |
| `ReviewDecision` | Append-only insert in the same transaction; DB triggers refuse update and delete | `InsertReviewAsync`, `ReviewDecision_NoUpdate/NoDelete` | **Yes** | None |
| `PrintOutput` | Upsert with promotion/recycle/invalidation state | `UpsertOutputAsync`; `PhotoshopTiffWorkflowOutputTests` | **Yes** | None |
| `AutomationLog` | Table and index created by migration `0001`; **no writer, no reader, no Domain type** | Zero C# references | **No** | The whole record type: no Product path produced one |
| `Setting` | Table created by migration `0001`; **no reader, no writer, no Domain type** | Zero C# references | **No** | The whole record type: no typed read or write existed |

Classification requested by the task:

| Record | Classification before this slice |
| --- | --- |
| `ProcessingSession`, `Revision`, `ProcessingAttempt`, `ReviewDecision`, `PrintOutput` | Required by 11076 — **already genuinely persisted** |
| `AutomationLog` | Required by 11076 — **schema only / dead table** |
| `Setting` | Required by 11076 — **schema only / dead table** |
| `SessionStep`, `InputSnapshot`, `AutomationLock`, PSD/PDF inspection storage | Persisted, **out of scope for 11076** — owned by other tasks; untouched |

Nothing in the five satisfied rows was refactored. No mapper, transaction helper, migration or
Domain value object of theirs was changed.

---

## 3. Current migration and schema reality

The migration sequence is **`0001`–`0015`**, not the eight-migration snapshot the original audit
described. `0013_manual_crop_geometry`, `0014_revision_retention` and
`0015_environment_verification_lock` are all present and were inspected before any decision.

`0001_initial_schema.sql` already contains both tables in exactly the shape this task needs:

```sql
CREATE TABLE Setting (
    Key   TEXT PRIMARY KEY NOT NULL,
    Value TEXT NOT NULL
);

CREATE TABLE AutomationLogEntry (
    Id              TEXT PRIMARY KEY NOT NULL,
    SessionId       TEXT NULL REFERENCES ProcessingSession(Id) ON DELETE SET NULL,
    StepKind        TEXT NULL,
    AtUtc           TEXT NOT NULL,
    FailureCode     TEXT NOT NULL,
    MessageKey      TEXT NOT NULL,
    TechnicalDetail TEXT NOT NULL,
    ContextJson     TEXT NULL,
    ScreenshotPath  TEXT NULL
);
CREATE INDEX IX_AutomationLog_Session ON AutomationLogEntry(SessionId);
```

**No migration was added.** Every column the AC requires already exists, so adding a `0016`
would have created duplicate schema for no gain — and historical migrations are forward-only and
were not edited. `MigrationRunner.NewestKnownVersion` is unchanged, and every existing migration
test still runs a fresh database through the whole sequence.

The schema was also the authority for what these rows *mean*. `FailureCode`, `MessageKey` and
`TechnicalDetail` are all `NOT NULL`, so every row `AutomationLogEntry` can physically hold is a
structured error — not a general lifecycle breadcrumb. That single fact settled the design.

---

## 4. AutomationLogEntry semantics

### The role chosen

> One `AutomationLogEntry` row is appended whenever an automation attempt **stops carrying a
> structured `OperationFailure`** — that is, an attempt that ends `Failed` or `Cancelled`. The row
> records the stable English failure code, the message key, the technical detail, the structured
> context as JSON, and the local screenshot path as a first-class column.

### What was deliberately excluded

* **No logging framework.** No `ILogger`, no Serilog, no rolling text files, no retention sweep,
  no log-location setting, no logging UI. Those are SCRUM-11121 and remain untouched.
* **No invented codes.** A crash-recovered `Interrupted` attempt carries no `OperationFailure` at
  all (`ProcessingAttempt.Interrupt` copies the record with a status change and nothing else).
  Writing one would have required inventing a `FailureCode` that the Product does not have, so
  startup recovery writes **no** log row. `StartupRecoveryService` is unchanged.
* **No expansion of Meitu failure evidence.** SCRUM-11091's fields were not added; the entry
  carries exactly what the failure already carried.

### Why this is not redundant with `ProcessingAttempt.FailureDetailJson`

The overlap is real and intentional, and the two records are not the same record:

| | `ProcessingAttempt` | `AutomationLogEntry` |
| --- | --- | --- |
| Purpose | The workflow record — what the step did | The diagnostic record — what went wrong |
| Session | Required, `ON DELETE CASCADE` | Optional, `ON DELETE SET NULL` |
| Step | Required | Optional |
| Screenshot | Only as an ad-hoc `evidencePath` key inside a JSON blob | A queryable `ScreenshotPath` column |
| Shape | One row per attempt, rewritten as it progresses | Append-only; never updated in place |

The screenshot promotion is the concrete, non-duplicated gain: before this slice there was no
column anywhere in the database holding a failure capture's path, so "which failures have
screenshots?" could only be answered by re-parsing every attempt's failure JSON.

### Typed, closed model

`PrintFlow.Domain.Automation.AutomationLogEntry` is a closed record — `AutomationLogId`,
`SessionId?`, `StepKind?`, `DateTimeOffset`, `OperationFailure`, `string? ScreenshotPath`. It
reuses the existing `OperationFailure` rather than introducing a second structured-error shape,
and `AutomationLogEntry.ScreenshotContextKey` names the adapters' existing `evidencePath` key in
one place. No string dictionary crosses the persistence seam, and no raw SQL is exposed to
Workflow or App.

### Product writer

Three real Product events, all pre-existing paths in `SessionService`:

| Site | Product event | Attempt status |
| --- | --- | --- |
| `FailAttemptAsync` | An adapter or validation failure closed a running attempt | `Failed` |
| `FailImportAsync` | The imported source could not be established or read | `Failed` |
| `StopAttemptAsync` | The operator pressed Stop or took the application over | `Cancelled` |

Each builds its entry through one private helper, `SessionService.RecordAutomationStop`, and puts
it in the mutation the same command already commits. The identifier is minted from the service's
own `IIdGenerator` rather than added to `CommandContext`, so `WorkflowEngine` stays a pure reducer
that knows nothing about a diagnostic log.

### Reader

`ISessionRepository.LoadAutomationLogAsync(SessionId, …)` reads a session's entries oldest first.
It is stated plainly that this reader has **no Product caller today** — the operator surface that
will consume it is SCRUM-11120's Error Details page. It exists because the AC's *"restart preserves
valid Session state and history"* is not a claim that can be made about records nothing can read
back, and §8's restart proof is what exercises it. It is deliberately **not** folded into
`LoadAsync`: every command path loads an aggregate and none of them reasons about the error log,
so putting the query there would cost every session interaction a read for the benefit of no
caller. The interface's default implementation *refuses* rather than returning an empty list, so a
repository that cannot read the log says so instead of answering "no errors".

---

## 5. Setting semantics

### What `Setting` actually represents

Design §17.6 enumerates it exactly: UI language, default output directory, production DPI, trim
safety margin, fixed-environment details, Photoshop colour-settings confirmation, log retention.
That list is the closed `PrintFlow.Domain.Settings.SettingKey` vocabulary added here — seven
members, nothing invented, persisted by name exactly as `FailureCode` and `StepKind` are.

### Real reader and writer

`ISettingsRepository` (a Workflow port) offers `ReadAsync`, `ReadAllAsync` and a batch
`UpsertAsync`; `SqliteSettingsRepository` implements it over the same
`SqliteConnectionFactory` and the `Setting` table `0001` already created. It is registered in the
composition root beside `ISessionRepository`. `SettingEntry` carries typed factories and readers
(`Text`, `Integer`, `Boolean`, `AsInteger`, `AsBoolean`) so no caller ever formats or parses a
value under the operator's culture.

### Why no operator-facing value is wired in this slice — and why that is the correct reading

This is the one place where the task's "do not leave it as unused infrastructure" instruction and
its "do not invent business behaviour" instruction pull against each other, so the reasoning is
recorded explicitly rather than resolved silently.

Every value on the design's `Setting` list falls into one of two categories today:

1. **It already has a different authority, and the precedence is not yet decided.** The default
   output root comes from the signed preset's `storageAndNamingContract.defaultOutputRoot` and
   `appsettings.json`'s `Workspace.Root`; log retention comes from `appsettings.json`'s
   `Logging.RetentionDays`; production DPI and the workstation-preset details come from the signed
   preset. Wiring any of these to a `Setting` row now would create exactly the conflict the task
   forbids — *appsettings output root = A, Setting output root = B* — with no authorised
   precedence rule. The plan's own note (`phase-11100-…-plan.md` §"Root comes from the preset …,
   overridable in settings") states the intent but not the rule, and the rule belongs to
   SCRUM-11118.
2. **It has no current Product behaviour to preserve.** There is no default trim safety margin
   anywhere in the Product (`TrimMargin` has no configured default), and there is no runtime
   language switcher — `Strings.cs` resolves through `CultureInfo.CurrentUICulture` and its own
   remark records that a switcher is a later slice. Wiring either would be inventing business
   behaviour, and would change what an existing installation does.

The exact SCRUM-11076 AC asks for `Setting` **metadata persisted in SQLite transactions**. It
names no setting, requires no operator action, and its four acceptance bullets are all properties
of the persistence itself. SCRUM-11118's AC is where "provide Settings for default output root, UI
language, trim safety margin, …" lives. This slice therefore delivers the persistence capability —
typed, transactional, UI-isolated, restart-proven, DI-registered — and stops there.

**Absent-row semantics are strict** (`ISettingsRepository.ReadAsync` returns `null`, never a
fabricated value), so an installation upgraded to this build has no rows and behaves exactly as
before. No current default changed, and nothing in the running application reads a setting yet.

---

## 6. Transaction boundaries

| Operation | Boundary |
| --- | --- |
| Any session command that stops an attempt | **One** `SqliteTransaction`: session upsert, step upserts, attempt upsert, PSD/PDF inspection, reviews, outputs, automation-lock change **and the `AutomationLogEntry`** |
| A settings batch | **One** `SqliteTransaction` for every entry in the batch |
| Migrations | Unchanged: one transaction per script, plus its `SchemaMigration` row and `PRAGMA user_version` bump |

`SessionMutation.NewAutomationLog` is written by the existing `CommitAsync` loop, after the print
outputs and before the automation-lock change, using the same `transaction` handle every other
write uses. No transaction was widened to include file or UI work; the boundary is exactly the
one `CommitAsync` already had.

### Why the log commits atomically rather than best-effort

The entry describes **the same authoritative transition** as the attempt row beside it. If the two
could commit apart, a crash between them would leave either an attempt recorded as failed with no
durable record of why, or a recorded error for a failure the database never accepted. Both are the
inconsistency the AC's *"records remain consistent across failures"* forbids. The row adds no new
failure mode: it is derived entirely from the `OperationFailure` the attempt already carries, so
there is nothing in it that can fail independently.

The `Setting` store is deliberately **not** part of the session transaction: a setting belongs to
no session, and folding it in would make "change a setting" require a session aggregate.

---

## 7. Rollback behaviour

Two rollback proofs, both against real SQLite, both on real multi-record operations:

1. **`Log_row_lands_in_the_same_transaction_as_the_failed_attempt_it_describes`** — the existing
   `FaultingRepository` fails the *closing* commit of an enhancement run. After a restarted
   repository reads the file back: the attempt is still `Running` (for startup recovery to find)
   **and** the automation log is empty. Neither half of the pair survived alone.
2. **`A_settings_batch_whose_second_write_fails_commits_neither`** — a two-entry batch whose second
   entry violates the table's own `Value NOT NULL` constraint. The failure is a real write failing,
   not a fault injected around one. The first entry is gone with it, read back through a fresh
   repository.

No synthetic failure-injection points were added anywhere in the Product.

---

## 8. Restart and readback

Every readback in the new suite goes through a repository built on a **new**
`SqliteConnectionFactory` over the same database file, so no in-memory state can answer. Two
proofs go further:

* `Independent_SQLite_readback_finds_the_persisted_row_after_the_services_are_gone` disposes the
  entire harness — services, repositories, workspace — then opens the database file directly with
  a raw `SqliteConnection` and reads `SessionId`, `StepKind`, `FailureCode`, `MessageKey`,
  `TechnicalDetail` and `ScreenshotPath` out of `AutomationLogEntry` with its own SQL. Nothing
  mocked participates.
* `Settings_round_trip_typed_values_and_survive_a_restart` writes through one repository and reads
  through a second one built on a second connection factory.

---

## 9. Tests

New file: `tests/PrintFlow.Tests/Integration/Persistence/AutomationLogAndSettingPersistenceTests.cs`
— **12 cases**, each mapped to an AC clause or a real invariant.

| Case | Maps to |
| --- | --- |
| `Adapter_failure_writes_one_log_row_with_its_identity_time_detail_and_screenshot` | "Persist … AutomationLog"; typed identity/time/detail; screenshot promotion |
| `Log_row_lands_in_the_same_transaction_as_the_failed_attempt_it_describes` | "records remain consistent across failures" |
| `Unreadable_import_logs_its_failure_against_the_import_step` | Second real writer site, with no adapter behind it |
| `An_operator_stop_is_logged_as_the_structured_error_it_produced` | Third real writer site (`Cancelled`) |
| `A_session_that_never_failed_has_an_empty_automation_log` | The narrow role: successes write nothing |
| `Independent_SQLite_readback_finds_the_persisted_row_after_the_services_are_gone` | "restart preserves … history"; independent evidence |
| `An_unset_setting_reads_as_absent_so_existing_defaults_still_decide` | Absent-row semantics; migration preserves current behaviour |
| `Settings_round_trip_typed_values_and_survive_a_restart` | "Persist … Setting"; restart/readback |
| `Upserting_an_existing_key_replaces_its_value_rather_than_adding_a_second_row` | Upsert semantics |
| `A_settings_batch_whose_second_write_fails_commits_neither` | Transaction rollback |
| `One_batch_may_not_write_the_same_key_twice` | Refuses a batch whose last write would silently win |
| `A_key_this_build_does_not_recognise_is_skipped_rather_than_failing_the_read` | Forward compatibility of the closed vocabulary |

No defensive tests were added for states SQLite constraints or existing suites already make
impossible, and the log/settings cases were not multiplied across arbitrary values.

**Existing suites run:** the whole `PrintFlow.Tests.Integration.Persistence` namespace
(`MigrationTests`, `DbInvariantTests`, `SessionServiceTests`, `StartupRecoveryTests`,
`SessionHygieneAndRecoveryTests`, `ProductionFailurePersistenceTests`, `StopAndTakeOverTests`,
`RetentionCleanupTests` and the rest) plus the whole `PrintFlow.Tests.Architecture` namespace
(`DependencyRuleTests`, `BannedApiEnforcementTests`, `ScopeGuardTests` and the boundary suites) —
**917 passed, 0 failed, 0 skipped**.

**No WPF/UIA smoke was manufactured.** This slice changes no operator UI: no view, view model,
XAML file or resource string was touched. A direct service + real-SQLite restart/readback proof is
the stronger evidence here, and it is what was produced.

---

## 10. Build

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

`dotnet build PrintFlowStudio.sln` via the per-user .NET 10 SDK.

---

## 11. Full-suite decision and result

The full suite was run. The task's own criteria are met three times over: this slice changes the
**shared SQLite repository** (`CommitAsync` now writes an extra record type inside the shared
transaction), changes **transaction orchestration** (`SessionMutation` carries a new list), and
integrates a write into **the common attempt-failure path** used by every adapter-backed step.

One complete suite was run against final source, once, after the last edit:

```
Passed!  -  Failed:     0, Passed: 11553, Skipped:     0, Total: 11553, Duration: 4 m 28 s
```

Baseline was **11,541 passed / 0 failed / 0 skipped**. The delta is **+12**, exactly the twelve
new cases in §9 — no existing test was changed, removed, or made to pass differently. Targeted
tests were run first (the new file, then the persistence and architecture namespaces); the full
suite was run once at the end rather than repeatedly after small edits.

---

## 12. SCRUM-11076 reassessment

Clause by clause against the exact original AC:

| AC clause | Status | Evidence |
| --- | --- | --- |
| Persist `ProcessingSession`, `Revision`, `ProcessingAttempt`, `ReviewDecision`, `PrintOutput` in SQLite transactions | Satisfied before this slice, unchanged | `SqliteSessionRepository.CommitAsync` |
| Persist `AutomationLog` | **Now satisfied** | Typed Domain record, three real Product writer sites, `LoadAutomationLogAsync` reader, written inside the existing transaction |
| Persist `Setting` | **Now satisfied at the persistence boundary** | `ISettingsRepository` + `SqliteSettingsRepository`, typed closed vocabulary, transactional batch upsert, DI-registered. No operator value wired — see §5 |
| Images stay on the local file system | Unchanged | Nothing here writes image bytes; `IWorkspace` still owns files |
| Restart preserves valid Session state and history | Satisfied, now including the error log | §8 |
| Persistence APIs are isolated from the UI | Satisfied | `ISettingsRepository` is a Workflow port; SQL lives only in `PrintFlow.Infrastructure`; `ServiceRegistration` is the only App type that names an Infrastructure type; architecture tests green |
| Records remain consistent across failures | Satisfied, strengthened | §6, §7 |
| Schema preserves invariants, no job/user-management concepts | Satisfied | No migration added; `SettingKey` is seven design-listed values with no user, job, order or customer concept |

**SCRUM-11076: PARTIAL → FULL.**

The one judgement worth stating plainly: `Setting` is marked satisfied on the strength of the
AC's own words — it asks for `Setting` metadata to be persisted in SQLite transactions, and that
capability now exists, is typed, is transactional, is UI-isolated and is composed into the running
application. It is **not** marked satisfied because a table has methods: the write path is
exercised through the real repository against real SQLite, the read path answers "absent" honestly,
and both survive a restart. What is deliberately absent is any operator-facing setting *value*,
because every candidate belongs to SCRUM-11118's AC and three of them would create an unauthorised
precedence conflict with `appsettings.json` or the signed preset. That distinction is the reason
this report ends **PASS WITH NOTES** rather than a bare PASS.

---

## 13. Parent SCRUM-11068 reassessment

Reassessed clause by clause against the exact original Epic Description, not inferred from child
labels.

| Epic clause | Status | Evidence |
| --- | --- | --- |
| WPF/.NET desktop foundation | FULL | SCRUM-11069; four-project solution, architecture tests |
| Fixed workflow state model | FULL | SCRUM-11071/11072; `WorkflowCatalog`, `TransitionTable`, pure `WorkflowEngine` |
| **SQLite metadata persistence** | **FULL (closed by this slice)** | All seven AC record types now have typed, transactional Product persistence |
| Immutable input snapshot and revision rules | FULL | SCRUM-11073; `InputSnapshot`, `Revision_Immutable_Update` trigger |
| Controlled local file workspace | FULL | SCRUM-11074; `FileWorkspace`, `PathGuard` |
| Collision-safe naming | FULL | SCRUM-11075, closed in the previous slice; `OutputFileNaming`, `_02/_03` suffixes |
| One operator, exactly one input image per session | FULL | `IX_InputSnapshot_Session` is UNIQUE; Home refuses multiple files |
| Excludes Job, Order, Customer, multi-user, batch, general configurable workflow | FULL, re-verified for this slice | No such table or type exists; `SettingKey`'s seven members introduce none, and add no user-management concept |
| Attempts and revisions remain separate | FULL | Separate tables; `ProcessingAttempt` CHECK admits an output Revision only for `SUCCEEDED` |
| Only fully exported, readable, hashed files become reviewable revisions | FULL | `WicFileInspector` hashes as the readability proof; `Revision` requires `ByteLength > 0` and a 64-char SHA-256 |
| Approvals bound to exact hashes | FULL | `ReviewDecision.ReviewedSha256` CHECK; append-only triggers |
| UI must not mutate workflow state, execute SQL, delete files or control third-party applications | FULL | `ISessionService` is the App's only command seam; `DependencyRuleTests` and `BannedApiEnforcementTests`; this slice adds no SQL and no Infrastructure reference to any view model |

Children: 11069, 11070, 11071, 11072, 11073, 11074, 11075 were already FULL; 11076 is closed here.
No clause of the Epic remains unmet.

**SCRUM-11068: PARTIAL → FULL.**

---

## 14. What was intentionally left to SCRUM-11118 / SCRUM-11120 / SCRUM-11121

Factual prerequisite notes only. None of these Jira items is claimed, and none moves status.

| Item | Factual note | Status unchanged |
| --- | --- | --- |
| **SCRUM-11118** Settings and Production Preset Display | A typed `ISettingsRepository` with a closed seven-key vocabulary now exists and is registered in the composition root. No Settings screen, navigation, language selector, output-root editor, retention editor or preset presentation was built, and no precedence rule between a persisted setting and `appsettings.json` / the signed preset was chosen. | **NOT_IMPLEMENTED** |
| **SCRUM-11120** Structured Error Details and Recovery UI | `AutomationLogEntry` now has a real Product writer and a repository reader, which is the durable backing an Error Details page needs. No page, no bilingual description surface, no recovery-action UI, no retry information display was built. | **PARTIAL / NOT_IMPLEMENTED** |
| **SCRUM-11121** Local Log and Screenshot Retention | Failure screenshot paths are now recorded in a queryable column. No rolling text log, no `ILogger`/Serilog, no 30-day cleanup, no screenshot retention sweep, no operator-visible log location. | **NOT_IMPLEMENTED** |
| **SCRUM-11091** Meitu failure evidence | Not expanded. The log entry carries exactly the `OperationFailure` the adapter already produced; no new field was added to satisfy 11091. | **unchanged** |

Also unchanged by design: SCRUM-11114 completion retention (no settings or log row became a file
authority; no cleanup behaviour moved) and SCRUM-11112 startup recovery (`StartupRecoveryService`
is byte-for-byte unchanged; no new startup side effect reads a setting or a log entry).

No digital-signature, code-signing or certificate work was added. Existing SHA-256 and preset
integrity behaviour is untouched.

---

## 15. Files changed

**Added**

* `src/PrintFlow.Domain/Automation/AutomationLogEntry.cs`
* `src/PrintFlow.Domain/Settings/SettingKey.cs`
* `src/PrintFlow.Domain/Settings/SettingEntry.cs`
* `src/PrintFlow.Workflow/Ports/ISettingsRepository.cs`
* `src/PrintFlow.Infrastructure/Sqlite/SqliteSettingsRepository.cs`
* `tests/PrintFlow.Tests/Integration/Persistence/AutomationLogAndSettingPersistenceTests.cs`

**Modified**

* `src/PrintFlow.Domain/Ids/Identifiers.cs` — `AutomationLogId`
* `src/PrintFlow.Workflow/Services/SessionMutation.cs` — `NewAutomationLog`
* `src/PrintFlow.Workflow/Ports/ISessionRepository.cs` — `LoadAutomationLogAsync`
* `src/PrintFlow.Workflow/Services/SessionService.cs` — `RecordAutomationStop` and its three call sites
* `src/PrintFlow.Infrastructure/Sqlite/SessionRows.cs` — `AutomationLogRow`, `SettingRow`
* `src/PrintFlow.Infrastructure/Sqlite/Mappers.cs` — row ↔ domain for both record types
* `src/PrintFlow.Infrastructure/Sqlite/SqliteSessionRepository.cs` — `InsertAutomationLogAsync`, `LoadAutomationLogAsync`
* `src/PrintFlow.App/Composition/ServiceRegistration.cs` — `ISettingsRepository` registration
* `tests/PrintFlow.Tests/Fixtures/FinalReviewFaults.cs` — `FaultingRepository` delegates the new reader

**Not touched:** every migration script, `MigrationRunner`, `StartupRecoveryService`,
`SessionRetentionService`, `WorkflowEngine`, all adapters, all view models, all XAML, all resource
files, `appsettings.json`.

---

## 16. Git state

Work was done directly on `master` in `d:\Repositories\printflow-Studio`. No branch, worktree,
alternate clone or folder was created. Unrelated files were left untouched. Local commit only —
nothing amended, rebased or pushed, and no AI-attribution trailer was added.
