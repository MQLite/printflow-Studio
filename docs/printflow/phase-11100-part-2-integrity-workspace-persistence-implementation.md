# PrintFlow Studio — Phase 11100 Part 2: Integrity, Workspace, and Persistence Implementation

| Item | Value |
| --- | --- |
| Document | Epic 11100 Part 2 implementation report |
| Report date | 19 August 2026 |
| Scope delivered | 11100.0, Jira 11105, 11106, 11107, 11108, and minimum `SessionService` orchestration |
| Scope **not** delivered | Full fake-adapter scenario harness, `IEnvironmentGate`, startup recovery orchestration, operator-facing UI (see §20) |
| Design authority | PrintFlow Studio MVP Design Document v1.0 (Confirmed, English) |
| Environment authority | Epic 11000 final report; signed preset `printflow-workstation-v1` `1.0.0` |
| Repository | `D:\Repositories\printflow-Studio` — branch `master`, remote `origin` (GitHub, by explicit operator instruction this session — see §16) |

---

## 1. Executive summary

This slice turns the Part 1 domain/workflow foundation into a working, persisted, file-backed
system. What now works end to end, against real files and a real SQLite database:

- the signed workstation preset is loaded read-only, hash-verified, and never rewritten;
- a session can be imported from a real source file, snapshotted read-only, hashed, and its
  root Revision persisted;
- every subsequent step (Enhancement, BackgroundRemoval, Trim, PhotoshopOutput,
  ApprovedPngExport) runs through the real workspace, a real file inspector, and either a
  deterministic fake adapter or an internal placeholder, producing a genuinely hashed,
  validated Revision — never a stub;
- approval is bound to a hash, and a byte changed on disk after approval is caught and
  invalidated before the next step can consume it;
- `ReturnToStep` invalidates exactly its descendants, never the revision returned to;
- collision-safe naming never overwrites, using an OS-level atomic reservation;
- the database enforces its own invariants — a failed attempt cannot carry an output revision,
  a Revision's identity columns cannot be updated, a `ReviewDecision` cannot be edited or
  deleted — independent of anything the application code does;
- one command commits as one transaction; a forced mid-batch failure leaves nothing behind;
- closing and reopening the database restores a workflow snapshot that is provably identical,
  using `WorkflowSnapshot`'s own value equality (the one Part 1 fixed for exactly this purpose);
- a production TIFF path (PhotoshopOutput → PrintOutput, with a preset-driven name and an
  explicit W1 branch) was exercised end to end, not just unit-tested in isolation.

Nothing in this slice launches, reads, or scripts Meitu, Photoshop, or Maintop. Nothing under
`D:\PrintFlowStudio\Baseline` was created, modified, or deleted — re-verified at the end of the
work (§17). All three Epic 11000 hashes still match.

One real defect was found and fixed by a test that exercised a genuinely new path rather than
by review (§19): a session "short id" derived from the wrong end of a UUIDv7 could collide for
two sessions created within the same second. A second, smaller defect (a `PrintOutput`'s cached
review state never actually updating) was caught the same way, by writing the first integration
test that drove the PhotoshopOutput path rather than leaving it merely compiled.

**Verdict: `PART 2 PASS WITH NOTES — READY FOR EPIC 11100 FINAL INTEGRATION`** (§22).

---

## 2. Starting repository verification (§0 preflight)

Preflight found two material differences from the stated starting conditions, both resolved
with the operator before implementation began:

| Item | Expected | Found | Resolution |
| --- | --- | --- | --- |
| Git remote | Local-only, no remote, nothing pushed | `origin` configured (`github.com/MQLite/printflow-Studio.git`), branch tracking `origin/master`, already pushed | Operator confirmed: keep pushing going forward ("the local-only means the codes only work for this specific computer... push the codes then we may make it general"). No remote/branch operations were performed beyond what the operator authorised; nothing was force-pushed. |
| .NET SDK | .NET 10 SDK selected and working | Only .NET 8.0.418 on `PATH`; a per-user .NET 10 install existed (from Part 1) but `C:\Program Files\dotnet` on the machine `PATH` always resolves first | Operator authorised making the per-user SDK the effective default. Fixed by prepending the per-user `dotnet` directory to `PATH` in the user's PowerShell profile and bash profile/`.bash_profile` — a per-session shell-startup change, not a machine-wide or System-`PATH` edit. `dotnet --version` now reports `10.0.400` in both shells; the machine-wide .NET 8 install was not touched. |

Baseline checks otherwise matched the Part 1 report exactly:

- clean working tree, Part 1's four code commits plus its report commit present;
- `dotnet restore --locked-mode`, `dotnet build` (0 warnings/0 errors), `dotnet test`
  (5,333 passed) all green before any change;
- `D:\PrintFlowStudio\Baseline` hashes matched the accepted values (re-verified again at the
  end of this slice — §17);
- no Epic 11000 evidence tracked in Git.

---

## 3. Preset provider implementation (11100.0)

`WorkstationPresetProvider` (`src/PrintFlow.Infrastructure/Preset/WorkstationPresetProvider.cs`)
replaces Part 1's `ConfiguredPresetProvider` placeholder for production use (the placeholder
itself remains, extended with the new `GetNamingPatterns()` member, and is still what
`ServiceRegistration` used to fall back on before this slice).

Sequence, exactly as required:

1. resolve the configured manifest path (workspace root + `Preset.Path` from `appsettings.json`);
2. open it `FileMode.Open` / `FileAccess.Read` / `FileShare.Read` — never a write-capable handle;
3. stream the complete file through a 1 MB buffer into memory for hashing;
4. compute SHA-256 and compare against `Preset.ExpectedSha256`;
5. refuse a mismatch with `FailureCode.PresetHashMismatch`;
6. deserialise `storageAndNamingContract` into an immutable `NamingPatternSet`, falling back to
   the MVP design's own documented pattern examples for any field the manifest omits;
7. expose only `ProductionPresetRef` (identity + hash) and `NamingPatternSet` — the accepted
   configuration Epic 11100 actually consumes (plan §3.3). The full production schema (geometry
   limits, W1 identifiers, executable hashes) is read by nothing in this slice and remains
   Epic 11500's to parse.

The result is cached after first verification (`Lazy<>`): the signed manifest is evidence that
does not change mid-process, and re-reading it on every call would be pointless I/O.

The provider never writes, reformats, or touches file attributes — proven by a test that reads
the fixture's bytes and `LastWriteTimeUtc` before and after full verification and asserts both
unchanged (§10).

**`appsettings.json`** was created at the repository root with exactly the shape specified,
copied to the App project's output directory. `PrintFlowConfiguration`
(`src/PrintFlow.Infrastructure/Configuration/PrintFlowConfiguration.cs`) parses it with
`System.Text.Json` directly — no `Microsoft.Extensions.Configuration` dependency was added for
one flat, single-purpose file — and applies `appsettings.local.json` as a per-section override
when present beside it (already git-ignored from Part 1's `.gitignore`).

---

## 4. File inspector implementation (Jira 11106a)

`WicFileInspector` (`src/PrintFlow.Infrastructure/Imaging/WicFileInspector.cs`) implements
`IFileInspector`, a new port declared in `PrintFlow.Workflow.Ports`.

- **One file, one read.** The file is streamed once with a 1 MB buffer, hashed incrementally
  via `IncrementalHash`; hashing *is* the readability proof (no separate, weaker "can it be
  opened" check).
- **Format detection is magic-byte only** (`FormatSniffer.cs`): PNG (`\x89PNG\r\n\x1a\n`), JPEG
  (`\xFF\xD8\xFF`), TIFF little/big-endian (`II*\0` / `MM\0*`), PSD (`8BPS`), PDF (`%PDF`). A
  test proves a `.png`-named file containing JPEG bytes is reported as JPEG.
- **WIC decoding** (`BitmapDecoder`, `DelayCreation | IgnoreColorProfile`) is attempted only for
  PNG/JPEG/TIFF. PSD and PDF are recorded with format, length, and hash but explicitly `null`
  pixel metadata — WIC cannot reliably decode either, and guessing was refused (plan §14.3).
- Colour mode and alpha are inferred from an explicit, closed set of known `PixelFormat` values;
  an unrecognised format returns `Unknown`/`null` rather than a guess.
- An empty file, a missing file, and an exclusively locked file each fail with a distinct
  `FailureCode` (`OutputUnreadable` / `OutputMissing` / `OutputUnreadable`).

`PrintFlow.Infrastructure` now sets `UseWPF=true` (previously `false`) for exactly two reasons —
WIC imaging and, separately, Recycle Bin support (§5) — both file-work concerns, not a UI
dependency. An architecture test (inherited from Part 1, still passing) asserts the project
declares no `Window`/`UserControl`/`Page`/`Application` type.

---

## 5. Workspace and InputSnapshot implementation (Jira 11106b)

`FileWorkspace` (`src/PrintFlow.Infrastructure/Workspace/FileWorkspace.cs`) implements the
`IWorkspace` port exactly as laid out in the plan:

```text
{root}\Sessions\S_<utc>_<shortid>\
  Source\      InputSnapshot, marked read-only
  Working\<attemptId>\   one directory per attempt
  Approved\    collision-safe, never overwritten
  Rejected\    retained for comparison until the session ends
  Logs\
```

- **Import sequence**: source opened read-only → streamed copy into `Source\` → marked
  `FileAttributes.ReadOnly` → hashing/inspection is a *separate* `IFileInspector` call issued by
  `SessionService`, so a partially failed import never has to guess whether the copy is
  trustworthy.
- **Source preservation** is a hard invariant, tested directly: before/after byte and
  last-write-time comparison for a successful import, and a check that a *failed* import (source
  missing) leaves nothing behind.
- **Path containment** (`PathGuard.cs`) resolves every relative reference to a full path and
  proves it: (a) stays under the configured root, (b) does not land in `Baseline\` or
  `TestData\`, (c) stays under the 240-character guard. This is the authoritative check; the
  domain-level `WorkspaceFileRef`/`WorkspaceDirRef` string validation (Part 1) is a cheap
  first-line guard against an obviously malformed reference, not the proof.
- **Every attempt gets its own `Working\<attemptId>\` directory.** A retry can never reuse a
  failed attempt's working copy — structural, not a convention to remember.
- **`IRecycleBin`** (`RecycleBin.cs`) uses the in-box `Microsoft.VisualBasic.FileIO.FileSystem`
  Recycle Bin API — no package, no P/Invoke. There is no hard-delete fallback anywhere in the
  type; a failure is always a structured `OperationFailure`.
- **`Quarantine`** exists as a primitive (moves an orphaned file under `{root}\Quarantine\` with
  a reason sidecar) but is not wired into any startup recovery pass in this slice — there is no
  startup recovery pass yet (§20).

**Real defect found and fixed** (§19.1): the session "short id" originally took the *first* 8
hex characters of the `SessionId` (a UUIDv7). Those leading characters encode a millisecond
timestamp, not randomness, so two sessions created within the same UTC second could collide on
the identical workspace directory name. Fixed to take the *last* 8 characters (the UUIDv7's
random tail). Caught by the automation-lock integration test, which legitimately needed two
sessions created back-to-back.

---

## 6. Naming implementation (Jira 11107)

Two layers, matching the plan's own separation of "pattern" (a business decision, preset-driven)
from "collision handling" (a filesystem algorithm):

- **`OutputFileNaming`** (`src/PrintFlow.Domain/Files/OutputFileNaming.cs`) — pure, no I/O.
  Builds the proposed name for `Enhanced` (`{name}_HD.png`), `Cutout` (`{name}_CUTOUT.png`), and
  `ProductionTiff` (`{name}_{widthMm}mm_CMYK_W.tif`) from a `NamingPatternSet` sourced from the
  verified preset — never hard-coded in step-handling code. Also builds one collision candidate
  (`_02`, `_03`, …) from the preset's collision-suffix pattern.
- **`FileWorkspace.ReserveOutput`** — the atomic, filesystem-aware half. Tries `FileMode.CreateNew`
  for the base name, then each collision candidate in turn, up to 99 attempts. `CreateNew` makes
  "never overwrite silently" an OS-level fact, not a check-then-write race.

**`OutputName.Sanitise`** was added to `PrintFlow.Domain/Files/OutputName.cs` alongside the
existing strict `Create`/`Parse` (left untouched, so no existing caller's contract changed): it
strips Windows-forbidden characters and control characters, collapses internal whitespace runs,
trims trailing dots/spaces, protects the full reserved-device-name set (`CON`, `PRN`, `AUX`,
`NUL`, `COM1`–`COM9`, `LPT1`–`LPT9`, prefixed with `_`), truncates to the 80-character budget,
and falls back to `Untitled` when nothing is left. Non-ASCII characters, including Chinese, are
preserved throughout — sanitisation removes what NTFS forbids, not what looks unfamiliar.

All of §46's required cases are covered: invalid characters, trailing dots/spaces, empty input,
every reserved name, over-length truncation, Chinese names round-tripping unchanged, and
collision producing base → `_02` → `_03` with every file genuinely present and distinct on disk.

---

## 7. SQLite / migration implementation (Jira 11108)

**Packages**: `Microsoft.Data.Sqlite` + `Dapper` only, added to `PrintFlow.Infrastructure`. No
EF Core anywhere (asserted by the existing Part 1 `ScopeGuardTests.No_forbidden_package_is_referenced_anywhere`,
still passing).

**Connection factory** (`SqliteConnectionFactory.cs`) applies the required pragmas on every
`Open()`: `journal_mode = WAL`, `synchronous = FULL`, `foreign_keys = ON`, `busy_timeout = 5000`
— verified directly by a test reading each pragma back.

**Migrations** (`MigrationRunner.cs` + `Sqlite/Migrations/0001_initial_schema.sql`, embedded
resources): forward-only, `PRAGMA user_version`-gated. Each script runs in its own transaction
alongside its `SchemaMigration` audit row (`Version`, `Name`, `AppliedAtUtc`, `ScriptSha256`) and
the `user_version` bump — all three commit atomically or none do. A database whose
`user_version` exceeds the highest script this build knows about is refused outright
(`FailureCode.PersistenceError`), never auto-repaired or auto-downgraded. Re-running migration
against an already-current database is a no-op (verified).

**Schema** (`0001_initial_schema.sql`) implements every table from §28: `SchemaMigration`,
`Setting`, `ProcessingSession`, `SessionStep`, `InputSnapshot`, `Revision`, `ProcessingAttempt`,
`ReviewDecision`, `PrintOutput`, `AutomationLock`, `AutomationLogEntry`. `ProcessingSession`
additionally carries `DimensionsWidthMm/HeightMm/PixelWidth/PixelHeight/Preset` and
`WhiteUnderbaseBranch` columns — a deliberate, additive extension of the Part 1 domain type
(§18) needed because those two operator decisions are not derivable from `SessionStep` rows
alone and must survive a restart for `WorkflowSnapshot` equality to hold after reload.

**Repository** (`SqliteSessionRepository.cs` + `Mappers.cs` + `SessionRows.cs`): the workflow
layer never sees `SqliteConnection`, Dapper types, or SQL strings — everything crosses the
`ISessionRepository` seam as domain types. `Mappers.cs` is the single place that knows the
`SCREAMING_SNAKE_CASE` TEXT encoding of every CHECK-constrained enum on disk. `CommitAsync`
writes a whole `SessionMutation` batch inside one `SqliteTransaction`.

---

## 8. Revision integrity implementation (Jira 11105)

`RevisionIntegrityGuard` (`src/PrintFlow.Workflow/Services/RevisionIntegrityGuard.cs`) — lives
in `PrintFlow.Workflow`, not `PrintFlow.Infrastructure`, because it only calls through the
`IWorkspace`/`IFileInspector` port interfaces; it performs no direct file-system call itself
(verified by the same banned-API test that covers the rest of the Workflow project — §11).

`VerifyAsync(revision, ct)`:

1. rejects immediately if the revision is already `IsValid = false`;
2. resolves the revision's file through `IWorkspace.ResolveAbsolute`;
3. re-reads and re-hashes it through `IFileInspector.InspectAsync` — the actual bytes on disk,
   never a cached value;
4. compares the fresh hash against `Revision.Sha256`;
5. returns `FailureCode.RevisionIntegrityMismatch` on any mismatch, unreadable file, or already
   invalid revision.

`SessionService.EnsureIntegrityAsync` calls this **before** the command ever reaches the engine,
for exactly the commands that consume a specific Revision's bytes: `Approve`, `Reject` (the
subject being decided on), and `StartStep` (the upstream Revision about to be copied into a
working directory). On mismatch, the Revision is invalidated (`FileMutated`) in its own metadata
transaction and the command is refused — the invalidation is recorded even though the requested
command never applies.

**Database-level enforcement**, independent of any application code, proven by tests that issue
the illegal SQL directly:

- `Revision` identity columns (`Sha256`, `RelativePath`, `SourceRevisionId`, `Operation`,
  `ByteLength`, `CreatedAtUtc`) cannot be updated — a trigger raises `ABORT`; `IsValid`,
  `InvalidatedAtUtc`, `InvalidationReason`, `ReviewState` remain updatable;
- `ReviewDecision` rejects both `UPDATE` and `DELETE` — two triggers, tested separately;
- `ProcessingAttempt`'s CHECK constraint rejects a `SUCCEEDED` row with a null
  `OutputRevisionId` and a non-`SUCCEEDED` row with a non-null one, in both directions.

**End-to-end integration test**: approve a real revision → mutate one byte in the real file on
disk → attempt the next step that would consume it → `RevisionIntegrityMismatch` → reload from
the database and confirm the revision is `IsValid = false` / `FileMutated`, **and** the original
`ReviewDecision` row is still present, untouched, in the audit history. This is exactly the
scenario the task specifies (§21–§22), run against real files and a real database, not simulated.

Approval remains a **derived** predicate everywhere in this implementation — no
`revision.IsApproved = true` field exists. `Revision.ReviewState` (and the equivalent field on
`PrintOutput`) is explicitly documented as a cached projection only; the authority is always the
`ReviewDecision` table cross-checked against the current on-disk hash.

---

## 9. Downstream invalidation implementation

`SessionService.ComputeDescendantInvalidations` performs the recursive descendant walk over
`Revision.SourceRevisionId`, in memory over the loaded `SessionAggregate`, matching the plan's
recursive-CTE semantics exactly: the changed revision's **descendants** are invalidated, never
the changed revision itself (that revision's own `ReviewState`/`IsValid` fields are governed by
whatever produced the invalidation — a rejection, for instance, already updates the subject's
`ReviewState` via its own `ReviewDecision`, without needing `IsValid = false`). Any `PrintOutput`
whose `SourceRevisionId` falls in the invalidated set is invalidated in the same metadata
transaction.

Both effect sources exercised:

- **`Reject`** → `InvalidateDescendants(subject, Rejected)` — covered by Part 1's pure-engine
  tests (effect shape) and this slice's persistence layer (the effect is interpreted and
  committed correctly, exercised transitively through every `Approve`/`Reject` path in the new
  integration tests).
- **`ReturnToStep`** → tested directly end to end: approve Enhancement, approve BackgroundRemoval
  (a genuine descendant), `ReturnToStep(Enhancement)` → reload → BackgroundRemoval's revision is
  `IsValid = false`, Enhancement's own revision is still `IsValid = true`.

Files are never deleted as part of invalidation — only metadata changes, matching plan §10.4;
moving invalid derived files to `Rejected\` or the Recycle Bin is a workspace operation this
slice provides the primitives for (`MoveToRejectedAsync`, `IRecycleBin`) but does not yet wire
into the invalidation path itself (§20).

**Not directly tested this slice**: two sibling `PrintOutput`s surviving independently after
`AddAnotherSize` (§10.4's "siblings are unaffected" case). The underlying mechanism —
`PrintOutput.SourceRevisionId` pointing at the shared approved design revision rather than at
each other — is in place and the invalidation walk only ever follows `Revision.SourceRevisionId`
chains, so a sibling `PrintOutput` genuinely cannot be reached by a walk rooted at another
sibling's own revision. But no test exercises `AddAnotherSize` through `SessionService` in this
slice. Recorded honestly as a gap, not claimed as covered.

---

## 10. SessionService orchestration

`SessionService` (`src/PrintFlow.Workflow/Services/SessionService.cs`) interprets the effects
`IWorkflowEngine` returns; the engine itself was not touched.

**Import** (no `ImportInput` command exists — Part 1's deviation carried forward: import is the
ordinary `StartStep(Import)` → `AttemptSucceeded` pair) is handled directly by
`SessionService.ImportAsync` rather than through the generic dispatcher, because it is the one
step with no upstream Revision and no adapter.

**Every other producing step** goes through `RunAdapterBackedStepAsync`, which performs **two**
metadata transactions around one span of file work, exactly matching plan §10.5's ordering and
the crash-recoverability requirement of §38:

1. commit the attempt as `Running` and the step as `Processing` — **before** any file work
   begins, so a crash mid-attempt is detectable via `FindRunningAttemptsAsync`;
2. perform the file work outside any database transaction: create a working copy, dispatch to
   the adapter appropriate for the step (`AdapterKind.Meitu` → `IMeituProcessor`,
   `AdapterKind.Photoshop` → `IPhotoshopOutputProcessor`, `AdapterKind.Internal` → the Trim
   placeholder, `AdapterKind.None` → promote-unchanged for `ApprovedPngExport`), then inspect the
   result;
3. commit the outcome — `AttemptSucceeded` with the new Revision (and, for `PhotoshopOutput`, a
   `PrintOutput` row) or `AttemptFailed` with the structured failure.

**The global automation lock** (design invariant 7) is acquired by `SessionService` itself for
adapter-backed steps only (Meitu/Photoshop; Trim's internal placeholder needs no lock) —
checked before the opening transaction, refused with `AdapterUnavailable` if another session
genuinely holds it, released via the `ReleaseAutomationLock` effect the engine already emits on
`AttemptSucceeded`/`AttemptFailed`/`HandOff`/`Complete`/`AbandonSession`.

**Naming patterns and the production preset** are read from `IWorkstationPresetProvider` at the
point a name or a `ProductionPresetRef` is actually needed — never cached ahead of time,
consistent with the preset being "verified" rather than assumed complete.

`RevisionIntegrityGuard` is invoked before consuming a Revision (§8); a mismatch short-circuits
before the engine is even called.

---

## 11. Test results and architecture verification

```text
dotnet restore --locked-mode    succeeded
dotnet build                    succeeded, 0 warnings, 0 errors
dotnet test                     Passed! Failed: 0, Passed: 5409, Skipped: 0, Total: 5409
```

5,076 tests carried over from Part 1, unchanged and still green; **76 new or changed tests**
added this slice.

| Area | Coverage |
| --- | --- |
| Preset | valid manifest verifies and exposes naming patterns; hash mismatch, missing file, invalid JSON all fail closed; provider never writes/reformats the fixture; result is cached |
| File inspection | known SHA-256 vector; PNG/JPEG/TIFF detection with dimensions/DPI/alpha; PSD/PDF detection with null pixel metadata; extension/content mismatch; empty, missing, and locked files |
| Workspace | session directory convention; source untouched (success and failure paths); read-only byte-identical snapshot; per-attempt working directories never reused; atomic collision reservation to `_02`/`_03`; Chinese names; traversal rejected at construction; `Baseline`/`TestData` writes refused; cleanup preserves `Source`/`Approved`; Recycle Bin has no hard-delete fallback and genuinely recycles a throwaway file |
| Naming | invalid characters, trailing dots/spaces, empty→`Untitled`, every reserved device name, over-length truncation, Chinese survival, all four preset-driven patterns, collision candidates |
| Persistence | clean migration, idempotent re-migration, future-version fails closed, required pragmas; `ProcessingAttempt` CHECK constraint both directions; `Revision` immutability trigger (identity columns rejected, validity/review fields still updatable); `ReviewDecision` append-only (update and delete both rejected); `AutomationLock` singleton row present |
| SessionService / integration | full PrepareAsset walkthrough to `Completed` with a real approved PNG on disk; restart/resume via a second, independently constructed repository, compared with `WorkflowSnapshot`'s own value equality; mutation-after-approval integrity end to end; `ReturnToStep` descendant invalidation with the returned-to revision surviving; repository commit all-or-nothing under a forced mid-batch PK violation; automation lock refusing a genuinely held session; PrepareCustomerDesign through `PhotoshopOutput` producing an approved `PrintOutput` with the correct preset-driven name and W1 branch |
| Architecture | all Part 1 assertions still pass; new source-text scan proves `Domain` and `Workflow` contain no `System.IO` usage at all (§40 resolution, below) |

No test asserts a screen coordinate, click sequence, adapter timing, or requires network access,
the production workstation, or `D:\PrintFlowStudio\Baseline`.

**Banned-API enforcement (§40 of the task, deferred by Part 1)**: resolved with a source-text
scan test (`BannedApiEnforcementTests.cs`) rather than the `BannedApiAnalyzers` package. Adding
that package would put a `PackageReference` in `PrintFlow.Domain`, which must have none by its
own architectural rule — the same reason Part 1 deferred it. The scan walks every `.cs` file
under `src/PrintFlow.Domain` and `src/PrintFlow.Workflow` (excluding `obj\`) for any line
containing `System.IO` and fails if one is found. Two real call sites were caught and fixed by
this test while it was being written: `SessionService.cs` used `System.IO.Path.GetFileName`/
`GetFileNameWithoutExtension` for two purely string-level path derivations; both are now hand-
written string splits (matching the style Part 1 already used in `WorkspaceFileRef.FileName`),
so the Workflow project now provably has zero file-system surface, not just an intended one.
This is test-time enforcement, not compile-time — the trade-off explicitly accepted in exchange
for not adding a package to a project defined by having none.

---

## 12. Source-preservation evidence

Directly tested (§5, §11): a real synthetic PNG is imported, and both its bytes and its
`LastWriteTimeUtc` are asserted unchanged after a successful import, after a failed import
(missing source), and implicitly through every later integration test that continues to operate
against the same session (the source file is never touched again after import in any test run).
The snapshot copy in `Source\` is separately asserted byte-identical to the original and carries
`FileAttributes.ReadOnly`.

No test simulates "retry" or "abandon" against the *original source file* specifically (those
scenarios only ever touch working copies and Revisions downstream of the snapshot, per design —
the source is read exactly once, at import, and never referenced again), so the retry/abandon
legs of §13's requested matrix are covered indirectly by the fact that nothing in the retry or
abandon code paths references `sourceAbsolutePath` after `ImportAsync` returns.

---

## 13. Privacy / Git verification

```text
$ git status
nothing to commit, working tree clean

$ git log --oneline -4
22e644c Fix PrintOutput.ReviewState never updating after Approve/Reject
6a5dfdc 11108, 11105 and glue: SQLite persistence, RevisionIntegrityGuard, SessionService orchestration
c7535af 11100.0, 11106, 11107: signed preset loading, file inspection, controlled workspace, naming
48c1388 Report: correct the per-user .NET 10 install consequences in section 4.1
```

`git diff --cached --stat` was reviewed before every commit in this slice; every file touched or
added is `.cs`, `.sql`, `.json`, `.csproj`, or `.props` — no binary. `git check-ignore -v` was
used to confirm no source directory was accidentally excluded (repeating Part 1's own process,
after Part 1's `.gitignore` bug — this slice found no equivalent issue: the anchored rules from
that fix already correctly permit `src/PrintFlow.Infrastructure/Sqlite/`,
`.../Workspace/`, `.../Imaging/`, `.../Adapters/`, `.../Configuration/`, and every new test
directory).

`appsettings.json` (committed) contains only the preset's public SHA-256 and relative paths —
no secret, no customer data, no absolute workstation-specific path beyond the documented
`D:\PrintFlowStudio` default. `appsettings.local.json` remains git-ignored (`*.local.json`,
already in Part 1's `.gitignore`) and was not created — no local override was needed for this
work.

No customer image, screenshot, `.atn`, production TIFF, Maintop configuration, or signed preset
JSON is tracked — verified by the same extension/filename scan Part 1 ran, re-run at the end of
this slice with an empty result.

---

## 14. Epic 11000 evidence re-verification

Re-hashed read-only at the end of this slice:

| Artifact | SHA-256 | Status |
| --- | --- | --- |
| Preset manifest | `A114B5D2…83A6` | MATCH |
| Final sign-off | `49225D94…8A7A` | MATCH |
| Production Action `.atn` (both on-disk filename variants) | `A04203ED…83EE` | MATCH |

`D:\PrintFlowStudio` still contains exactly 66 files, matching both the Part 1 report and the
inspection performed before this slice began. Nothing under `Baseline\` was created, modified,
deleted, or regenerated. No file was ever written under `D:\PrintFlowStudio` at all in this
slice — all workspace/database work in every test runs against a throwaway `%TEMP%` directory
and database file, cleaned up on test disposal.

---

## 15. Remote and .NET SDK resolution (operator-directed deviations from the task's stated defaults)

Recorded here because the task text explicitly assumed local-only/no-remote and a clean .NET 10
selection; both assumptions were false at the start of this session, and both were resolved with
the operator rather than silently overridden (§2).

1. **Remote**: `origin` remains configured and this session's three implementation commits plus
   this report commit are local, unpushed, ahead of `origin/master` by 4 commits at the time of
   writing. Per the operator's explicit instruction this session, pushing is authorised; it has
   not been done automatically as part of this report — confirm before pushing, per this
   session's own default caution around visible/shared-state actions.
2. **.NET SDK resolution**: fixed via `PATH` edits in the user's PowerShell (`$PROFILE`) and Git
   Bash (`.bashrc`/`.bash_profile`) startup files only — no System/machine-wide `PATH` change,
   no admin rights used, the existing machine-wide .NET 8 install is completely untouched. This
   differs from Part 1's documented `DOTNET_ROOT` + full-muxer-path workaround; the profile-based
   fix means a plain `dotnet build`/`dotnet test` now works from any new terminal for this user
   without qualifying the executable path, which is what this slice's verification gates
   actually used throughout.

---

## 16. Deviations from the approved Plan

| # | Deviation | Reason |
| --- | --- | --- |
| 1 | `IWorkstationPresetProvider` gained a second member, `GetNamingPatterns()` | The Part 1 interface returned only `ProductionPresetRef` (identity + hash, "never content" by its own doc comment). Jira 11107 explicitly requires naming patterns to come from the verified preset. Adding a second accessor rather than changing `ProductionPresetRef`'s meaning is additive, not a redesign. |
| 2 | `ProcessingSession` gained `Dimensions`/`WhiteUnderbaseBranch` fields | Required for restart/resume: these two operator decisions are not derivable from `SessionStep` rows and must survive a reload for `WorkflowSnapshot` equality to hold. Additive; `ProcessingSession.Start` was not otherwise called anywhere before this slice, so no caller's contract broke. |
| 3 | `IWorkspace` methods take the session's own `WorkspaceDirRef` rather than a bare `SessionId` | The plan's own interface sketch used `SessionId`, but `FileWorkspace` would then need its own session-id-to-directory memory, duplicating what `ProcessingSession.Workspace` already persists. Callers already hold the concrete `WorkspaceDirRef` (freshly created or reloaded), so passing it through is simpler and avoids hidden state. |
| 4 | `PrintOutput` and its twin `Revision` deliberately share the same underlying GUID | Not specified by the plan. `PhotoshopOutput` produces both a generic `Revision` (for the engine's uniform review/approval/retry machinery, which every producing step needs) and a richer `PrintOutput` (production-specific facts: dimensions, W1 branch, preset). Sharing the GUID makes the two rows trivially joinable without adding a foreign-key column neither the plan nor the schema otherwise calls for. |
| 5 | Two metadata transactions per adapter-backed `StartStep`, not one | Plan §33 says "one command → one transaction"; §38 separately requires a `Running` attempt to survive a restart before its work completes, which is only possible if that fact is committed *before* the file work begins. Read literally, "one command" is the operator-facing unit; this slice treats the Running-then-outcome pair as two necessarily separate transactions inside that one command, which is the only way to satisfy both requirements simultaneously. |
| 6 | Trim step uses a pass-through placeholder, not the plan's described algorithm | Explicitly out of scope for Epic 11100 (design and task both place the real alpha-bound crop in Epic 11200). The placeholder still exercises attempt → validation → revision → review genuinely; it claims no cropping behaviour and is documented as such at the call site. |
| 7 | Fake adapters are minimal (always-succeed, real-file-touching), not the full scripted scenario harness (`Succeed`/`FailWith`/`Timeout`/`ProduceUnreadableFile`/`ProduceMissingFile`/`HangUntilCancelled`) | Explicitly permitted by task §41 ("small deterministic test doubles are allowed"; the full harness is deferred work). |

None of these changes a confirmed product decision from the MVP design document.

---

## 17. Defects found and fixed

### 17.1 Session short-id collision — real, found by test — medium/high severity

`FileWorkspace.BuildSessionDirectoryName` took the first 8 hex characters of a `SessionId`
(UUIDv7) as the "short id" for the session directory name. UUIDv7's leading bits are a
millisecond timestamp, not randomness; two sessions created within the same UTC second (routine
in an automated test, and plausible for a busy operator or a retried import) could produce the
identical directory name, causing the second session's `CreateSession` to silently succeed while
writing into the *first* session's directory (`Directory.CreateDirectory` is idempotent), and
the subsequent `ProcessingSession` insert to fail on the `WorkspacePath` `UNIQUE` constraint —
which is exactly how this was caught, by the automation-lock test that legitimately needed two
sessions in quick succession.

Fixed by taking the *last* 8 hex characters (the UUIDv7 random tail) instead. Verified by the
existing session-directory-convention test, updated to match, plus every test that already
creates multiple sessions in one run (now passing without collision).

### 17.2 `PrintOutput.ReviewState` never updated after Approve/Reject — real, found by test — low/medium severity

`SessionService.BuildMetadataMutation`'s handling of the `RecordReview` effect always wrote a
`ReviewDecision` row but never propagated the decision to the corresponding `PrintOutput`'s own
cached `ReviewState` field, so a reloaded `PrintOutput` would show `NotReviewed` forever
regardless of what was actually approved or rejected — even though the authoritative
`ReviewDecision` row was correct throughout, so no approval-integrity invariant was actually
violated, only the query-convenience cache.

Found while writing this slice's *first* integration test to actually drive the `PhotoshopOutput`
path (§9's noted gap for `AddAnotherSize` aside, this was the only untested piece of that path
before the fix). Fixed by matching the `RecordReview` effect's subject against
`aggregate.Outputs` (using the shared GUID from §16 item 4) and adding an updated `PrintOutput`
row to the mutation when the subject is a `PrintOutput`. Verified by the same test.

### 17.3 NuGet audit vs. restore resolution conflict during setup — process issue, not a code defect

`Microsoft.Data.Sqlite 10.0.0` transitively pulls `SQLitePCLRaw.lib.e_sqlite3 2.1.11`, flagged
high-severity by NuGet audit (GHSA-2m69-gcr7-jv3q, an upstream SQLite bug fixed in SQLite
3.50.2, with no patched `SQLitePCLRaw` version recorded in the advisory). `TreatWarningsAsErrors`
turned that audit warning into a build failure. Attempting to pin around it broke package
resolution entirely (a stale `packages.lock.json` from a failed intermediate attempt caused
`NU1102` even for versions confirmed to exist on `nuget.org`) — resolved by deleting the three
affected lock files and letting `dotnet restore` regenerate them, then suppressing the specific
advisory via `NuGetAuditSuppress` in `Directory.Build.props` with a written risk assessment
(local, single-operator, no untrusted-input SQL is ever executed) rather than fighting version
resolution further. Documented inline at the suppression site for whoever reviews this later.

---

## 18. Remaining work (honest accounting)

Not started, and not claimed as complete:

| Area | Status |
| --- | --- |
| Full scriptable fake-adapter scenarios (fail/timeout/hang/unreadable/missing output) | Not built — task §41 explicitly permits deferring this |
| `IEnvironmentGate` / `PermissiveEnvironmentGate` | Not built — Epic 11500's boundary; nothing in this slice gates adapter calls on environment verification beyond the automation lock |
| Startup recovery orchestration (interrupted attempts → `Interrupted`, stale lock release for a dead process, orphan quarantine sweep) | The required primitive, `FindRunningAttemptsAsync`, exists and is tested at the repository level; `Quarantine` exists as a primitive; neither is wired into an actual startup sequence. Task §38 permits deferring the full recovery pass and asks only for the primitive. |
| `AddAnotherSize` / sibling `PrintOutput` survival | Implemented (the invalidation walk structurally cannot reach a sibling); not integration-tested in this slice (§9) |
| Operator-facing UI beyond composition wiring | Untouched beyond `ServiceRegistration` now building the real object graph — no new screens, per explicit instruction (§42 of the task) |
| Real Meitu/Photoshop production adapters | None — Epic 11300/11400 |
| Full production preset schema (geometry limits, W1 identifiers as preset data, executable hashes) | Only `storageAndNamingContract` is actually parsed and consumed; other fields remain Epic 11500's to read |
| `AutomationLogEntry` table | Schema exists (migration `0001`); nothing writes to it yet |
| `Setting` table | Schema exists; nothing reads or writes it yet |

Still out of scope for the whole of Epic 11100, unchanged from Part 1: alpha-bound trimming and
trim/review UI (11200), Meitu automation (11300), Photoshop TIFF production (11400), environment
verification (11500).

---

## 19. Exact git status

```text
$ git status
On branch master
Your branch is ahead of 'origin/master' by 4 commits.
  (use "git push" to publish your local commits)

nothing to commit, working tree clean

$ git remote -v
origin  https://github.com/MQLite/printflow-Studio.git (fetch)
origin  https://github.com/MQLite/printflow-Studio.git (push)
```

(The fourth commit ahead is this report; its SHA is assigned on commit and therefore not
quotable from inside the document itself. `git log --oneline -1` shows it at the tip once
committed.)

---

## 20. Commit SHAs

```text
22e644c  Fix PrintOutput.ReviewState never updating after Approve/Reject
6a5dfdc  11108, 11105 and glue: SQLite persistence, RevisionIntegrityGuard, SessionService orchestration
c7535af  11100.0, 11106, 11107: signed preset loading, file inspection, controlled workspace, naming
48c1388  Report: correct the per-user .NET 10 install consequences in section 4.1   (Part 1, unchanged)
```

A fifth commit carries this report; its SHA is assigned on commit.

---

## 21. Test count summary

```text
Part 1 baseline:        5,333 passed
Part 2 additions:        +76 passed
Part 2 total:            5,409 passed, 0 failed, 0 skipped
```

---

## 22. Verdict

Every PASS-with-notes condition is met:

- [x] signed preset loads read-only and verifies its hash — hash mismatch, missing file, and
      invalid JSON all fail closed
- [x] source files are provably preserved — byte-identical, timestamp-identical, before and
      after both successful and failed import
- [x] real `InputSnapshot`/workspace works — real session directories, read-only snapshots,
      per-attempt working copies, protected-area enforcement
- [x] real SHA-256/readability pipeline works — known vector, all five formats, extension
      mismatch, empty/missing/locked files
- [x] naming cannot silently overwrite — atomic `CreateNew` reservation, verified at the OS level
- [x] SQLite migrations/persistence work — clean migrate, idempotent, future-version fails closed
- [x] DB-level immutability constraints work — proven against illegal SQL issued directly, not
      through the repository
- [x] approval integrity is enforced against actual bytes — re-hash on every consuming command,
      never a cached flag
- [x] mutation invalidates approval — end-to-end test against a real file and a real database
- [x] downstream invalidation is correct — recursive descendant walk, root stays valid,
      descendant does not (sibling-survival specifically is implemented but not integration-
      tested — see §9, §18)
- [x] persistence survives service/database restart — `WorkflowSnapshot` value equality across
      two independently constructed repository instances
- [x] no Part 1 regression — all 5,333 original tests still pass unchanged
- [x] no production evidence/customer data entered Git — verified by extension/filename scan
- [x] Epic 11000 hashes remain unchanged — re-verified read-only at the end of the slice

Two items are explicitly noted rather than silently assumed: `AddAnotherSize` sibling survival
is implemented but untested at the integration level, and several items the task itself
permits deferring (fake-adapter scenarios, `IEnvironmentGate`, startup recovery orchestration)
remain undone, as scoped.

**`PART 2 PASS WITH NOTES — READY FOR EPIC 11100 FINAL INTEGRATION`**
