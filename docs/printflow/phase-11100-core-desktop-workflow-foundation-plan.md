# PrintFlow Studio — Phase 11100 Core Desktop Workflow Foundation Plan

| Item | Value |
| --- | --- |
| Document | Epic 11100 implementation plan |
| Plan version | 1.0 (draft for approval) |
| Plan date | 18 August 2026 |
| Scope | Tasks 11101–11108 |
| Design authority | PrintFlow Studio MVP Design Document v1.0 (Confirmed) |
| Environment authority | Epic 11000 Production Environment Baseline Final Report; preset `printflow-workstation-v1` `1.0.0` |
| Status | PLAN ONLY — no code written, nothing committed |

---

## 1. Executive summary

Epic 11100 builds the deterministic, testable foundation on which the fragile parts of PrintFlow Studio (Meitu and Photoshop screen automation, Epics 11300/11400; deterministic trimming and image review, Epic 11200) will later be mounted. Nothing in this Epic touches a third-party application.

The deliverable of Epic 11100 is a WPF solution in which:

- a `ProcessingSession` can be created from one imported image, snapshotted, hashed, and persisted;
- one of the three fixed workflows can be selected and driven entirely through validated commands;
- every file the application manages is a hashed, validated `Revision` on disk with metadata in SQLite;
- every approval is bound to a SHA-256, and modifying an approved file provably invalidates its approval;
- deterministic **fake** Meitu/Photoshop adapters exercise the full attempt → validation → revision → review pipeline without any desktop automation;
- the application can be closed mid-session, reopened, and resumed, with interrupted attempts correctly recovered.

### 1.1 Headline findings from repository inspection

Four findings change the shape of the first coding phase and must be resolved before or during Task 11101.

1. **The working directory is not under version control.** `D:\Repositories\printflow-Studio` has no `.git`, no `.gitignore`, and no remote. Epic 11100 will create the entire source tree; starting that without VCS is the single largest process risk in this plan. Repository initialisation is promoted to the first task.
2. **A second, stale copy of the project exists** at `C:\Users\admin\Documents\ChatGPT\Printflow Studio`. It *is* a git repository (branch `master`, **zero commits**, everything untracked, no remote) and holds an older design document (32,559 bytes vs the current 35,081) and a half-length 11000 plan (39,549 bytes vs 75,451). The working copy on `D:` is authoritative. The two copies must be reconciled before any commit, or the divergence becomes permanent history.
3. **The Chinese design document is missing from the authoritative copy.** `PRINTFLOW_STUDIO_MVP_DESIGN.md` exists only in the stale `C:` copy. The MVP is Chinese-first at runtime (design §13.4); losing the Chinese design authority is a real risk.
4. **The Epic 11000 "remaining administrative item" is already complete.** The task brief states that consolidating the accepted values into an immutable preset manifest and obtaining sign-off is outstanding. It is not. Both artifacts exist and were re-verified during this inspection:

   | Artifact | Path | Recomputed SHA-256 | Matches report |
   | --- | --- | --- | --- |
   | Preset manifest | `D:\PrintFlowStudio\Baseline\workstation-v1\preset\printflow-workstation-v1.0.0.json` | `A114B5D2…83A6` | ✅ |
   | Final sign-off | `D:\PrintFlowStudio\Baseline\workstation-v1\signoff\workstation-preset-v1.0.0.json` | `49225D94…8A7A` | ✅ |
   | Action artifact | `…\actions\authoring\PrintFlow-DTF-v1.atn` | `A04203ED…83EE` | ✅ |

   The pre-implementation task therefore shrinks from "produce and sign a preset" to "verify the signed preset and wire it in as configuration". No part of Epic 11000 is reopened.

### 1.2 Headline design decisions

| # | Decision | Choice |
| --- | --- | --- |
| 1 | .NET LTS | **.NET 10 LTS** (`net10.0-windows`); requires an SDK install — only 8.0.418 is present. `net8.0-windows` is an acceptable interim (one property change). |
| 2 | Projects | **5 projects**, not 7: `Domain`, `Workflow`, `Infrastructure`, `App`, `Tests`. |
| 3 | MVVM | **CommunityToolkit.Mvvm** (source-generated, MIT, no framework lock-in). |
| 4 | SQLite | **Microsoft.Data.Sqlite + Dapper**, hand-written SQL. No EF Core. |
| 5 | Migrations | `PRAGMA user_version` + embedded ordered `.sql` scripts, forward-only, transactional. |
| 6 | IDs | **UUIDv7** stored as 36-char TEXT. |
| 7 | Timestamps | UTC ISO-8601 `…fffZ` TEXT via `TimeProvider`. |
| 8 | Workflow model | **Pure reducer over an explicit transition table**, driven by three static workflow definitions. |
| 9 | Snapshot persistence | Normalised `ProcessingSession` + `SessionStep` rows. No JSON blob authority. |
| 10 | Invalidation | Recursive descendant walk over `Revision.SourceRevisionId`; approval is a *derived*, hash-bound predicate. |
| 11 | Workspace | `D:\PrintFlowStudio\Sessions\S_<utc>_<shortid>\{Source,Working,Approved,Rejected,Logs}`; DB paths stored **relative to root**. |
| 12 | File inspection | `IFileInspector` over WIC (`BitmapDecoder`); hashing doubles as the readability proof. |
| 13 | Errors | `OperationResult<T>` + `OperationFailure(FailureCode, …)`; exceptions only for programmer error. |
| 14 | Fake adapters | Ports in `Workflow`, fakes in `Infrastructure`, selected by `AdapterMode` config; they write **real** files so validation is genuinely exercised. |
| 15 | Preset | Loaded from the signed JSON at startup, hash-verified against `appsettings.json`; **never embedded in code**; fails closed for production adapters. |

### 1.3 Epic boundary correction (applied 18 August 2026)

Plan v1.0 as drafted stated in several places that the real trimming implementation belongs to **Epic 11300**. That is incorrect. The confirmed Epic map is:

| Epic | Scope |
| --- | --- |
| **11100** | Core Desktop & Workflow Foundation |
| **11200** | Image Review, Comparison & Deterministic Trimming |
| **11300** | Meitu Automation Adapter |
| **11400** | Photoshop TIFF Output |
| **11500** | Automation Safety / Environment Check |

Consequences for Epic 11100 — unchanged in substance, corrected in attribution:

- Epic 11100 **may** define `StepKind.Trim`, the Trim workflow state, Trim-related commands/effects/ports, and whatever fake behaviour the workflow tests need.
- Epic 11100 **must not** implement alpha-bound trimming, crop algorithms, manual crop UI, or trim comparison/review UI. Those belong to **Epic 11200**.
- Screen automation remains outside 11100 entirely: Meitu in **11300**, Photoshop in **11400**, environment verification in **11500**.

All affected references in §7.3, §9.3, §15.1, §22.1 and §22.2 have been updated. No other part of the approved plan was rewritten.

---

## 2. Repository inspection

Performed 18 August 2026 against `D:\Repositories\printflow-Studio` and the production root `D:\PrintFlowStudio`. Nothing was modified.

### 2.1 Working repository

| Item | Status | Evidence |
| --- | --- | --- |
| Version control | **ABSENT** | `git status` → `fatal: not a git repository (or any of the parent directories)`. No parent repo either. |
| Branch / history / remote | N/A | No repository exists. |
| `.gitignore` | **ABSENT** | No `.gitignore` at any level. |
| Tracked evidence leakage | **NONE** | Non-`.md` file count in the working tree: 0. |
| Files present | CONFIRMED | `PRINTFLOW_STUDIO_MVP_DESIGN_EN.md` (35,081 B); `docs/epic-11000-production-environment-baseline-final-report.md` (10,979 B); `docs/phase-11000-production-environment-baseline-plan.md` (75,451 B). |
| Solution / project files | **NONE** | No `.sln`, `.csproj`, `.editorconfig`, `Directory.Build.props`, `nuget.config`, or source files. |
| Prior prototypes or automation files | **NONE** | Greenfield for code. |
| Conflicting scope | **NONE** | Nothing implements Job/Order/Customer, TeeNova, batching, nesting, RIP/printer control, cloud upload, or a workflow designer. |

### 2.2 Second (stale) copy

| Item | Finding |
| --- | --- |
| Path | `C:\Users\admin\Documents\ChatGPT\Printflow Studio` |
| Git | Repository exists; branch `master`; **no commits**; all files untracked; no `remote.origin.url` |
| Contents | `PRINTFLOW_STUDIO_MVP_DESIGN.md` (zh, 27,268 B — **not present on D:**), `PRINTFLOW_STUDIO_MVP_DESIGN_EN.md` (32,559 B, older), `docs/printflow/phase-11000-…-plan.md` (39,549 B, older) |
| Divergence | The `D:` English design doc is newer: it adds shrink-only sizing, the four shop size limits, the manual-crop fallback note, and replaces "Photoshop production Action" with "validated Photoshop production procedure" throughout (91 diff lines). |
| Evidence leakage | None; no non-`.md` files. |

The `C:` copy is a superseded snapshot from 17 August. It must not become the project history.

### 2.3 Documentation convention drift

The Epic 11000 plan (§13) specifies `docs/printflow/` for planning documents. The two existing 11000 documents actually live in `docs/`. This plan is written to the requested `docs/printflow/` path. **Recommendation:** move the two 11000 documents into `docs/printflow/` as part of the first commit so one convention holds. Not done in this pass.

### 2.4 Production root and baseline evidence

| Item | Finding |
| --- | --- |
| `D:\PrintFlowStudio` | Exists; contains `Baseline\` and `TestData\` only. 66 files total. |
| Baseline evidence | `workstation.json`, `displays.json`, `apps\`, `actions\`, `screenshots\` (31 files), `preset\`, `signoff\` (11 files) |
| Preset manifest | Present, marked read-only (`-r--r--r--`), 16,059 B, hash verified ✅ |
| Final sign-off | Present, marked read-only, hash verified ✅ |
| Action `.atn` | Present, hash verified ✅ |
| Test data | `TestData\v1\{inputs,expected,manifests}` — 1 input JPEG, 2 expected PNGs, 1 manifest. Sufficient for import/hash fixtures; **not** sufficient as the seven-category regression set (explicitly waived by signed decision 11005). |
| Leakage | All customer/evidence material is under `D:\PrintFlowStudio`, entirely outside both repository copies. ✅ |

### 2.5 Toolchain

| Item | Finding | Implication |
| --- | --- | --- |
| .NET SDK | `8.0.418` only | .NET 10 SDK install required for the recommended target (§4.1) |
| Runtimes | `Microsoft.WindowsDesktop.App 8.0.24` present | WPF development is possible today on .NET 8 |
| Git | `2.53.0.windows.1` | Available |

### 2.6 Inspection conclusion

The code base is still greenfield; the *process* substrate (VCS, ignore rules, doc layout, SDK) is not ready. Fixing that is cheap and must precede Task 11101.

---

## 3. Epic 11000 closure dependency

### 3.1 Status

Epic 11000 is **closed**. Tasks 11001–11007 are signed, the immutable preset manifest exists, and the final preset sign-off exists. All three hashes re-verified during this inspection. There is no outstanding administrative item.

### 3.2 Replacement pre-task — 11100.0 "Preset verification and wire-in"

A small gate remains, but it is an Epic 11100 engineering task, not Epic 11000 rework:

1. Re-verify the manifest and sign-off SHA-256 values (already done once here; the implementation repeats it in code).
2. Record `presetId`, `presetVersion`, `presetPath`, `presetSha256` in `appsettings.json`.
3. Implement `IWorkstationPresetProvider` that loads, hash-verifies, and deserialises the manifest into an immutable typed record graph.
4. Check in a **synthetic** fixture preset (fake paths, fake hashes) for tests. The real manifest is never copied into Git.

Estimated effort: half a day. It is a dependency of Task 11101 only in that the `appsettings.json` shape must be agreed; the rest can land alongside 11106.

### 3.3 Values Epic 11100 consumes from the preset

Taken from the signed manifest; **none of these may be hard-coded** in application logic:

| Preset path | Used by |
| --- | --- |
| `storageAndNamingContract.defaultOutputRoot` | Workspace root resolution |
| `storageAndNamingContract.enhancedPattern` / `cutoutPattern` / `productionTiffPattern` / `collisionPattern` | Naming service |
| `storageAndNamingContract.neverSilentlyOverwrite` / `neverRenameSource` | Workspace safety assertions |
| `productionGeometryContract.resize.resolutionPpi` (300), `proportional`, `shrinkOnly`, `limitsMillimetres.{A3_LANDSCAPE,A3_PORTRAIT,A4,A5}` | `PrintDimensions` value object and size-preset table |
| `whiteUnderbaseContract.branches.{W1_0px,W1_1px,W1_2px}` + `branchDecision` | `WhiteUnderbaseBranch` enum, operator-decision requirement |
| `photoshopActionContract.setName` / `artifactSha256` / `integrityRule` | Photoshop port request + future environment gate |
| `photoshopContract.executablePath` / `executableSha256`, `meituContract.executablePath` / `executableSha256` | Future environment gate (recorded, unused in 11100) |
| `displayContract`, `workstation.sessionContract` | Future environment gate (recorded, unused in 11100) |

Values in `workstation.computerName` and `workstation.operatorIdentity` are read but **never logged, exported, or committed**.

---

## 4. Proposed solution structure

### 4.1 Decision 1 — .NET LTS version

**Recommendation: .NET 10 LTS**, target framework `net10.0-windows`.

Justification:

- .NET 10 is the current LTS (Nov 2025 → Nov 2028). .NET 8 LTS support ends **November 2026 — roughly three months from now**. Starting a multi-Epic desktop product on a runtime that leaves support before the MVP ships is not defensible.
- `Guid.CreateVersion7()` is in-box from .NET 9, which removes a hand-rolled ID generator (§6.6).
- `TimeProvider` (in-box since .NET 8) and `Microsoft.Extensions.TimeProvider.Testing` give deterministic clocks in tests.

Cost: the .NET 10 SDK and Windows Desktop runtime must be installed on the workstation. That is a developer-tooling install, not a change to Meitu, Photoshop, Maintop, Actions, or colour settings, so it does not violate the "do not modify production applications" constraint — but it is an **operator decision** (§22).

Fallback: if the operator declines the SDK install now, start on `net8.0-windows`. The cost of moving later is one `<TargetFramework>` value in `Directory.Build.props` plus a ~20-line UUIDv7 generator behind `IIdGenerator`. Do not let this block the Epic.

### 4.2 Decision 2 — solution and project structure

The brief suggests seven projects. **Five is better**, and the reduction is justified per boundary rather than by fiat.

```text
PrintFlowStudio.sln
Directory.Build.props          TFM, LangVersion, Nullable, TreatWarningsAsErrors, deterministic build
Directory.Packages.props       central package version management
nuget.config                   pinned feed
.editorconfig                  style + analyzer severities
src/
  PrintFlow.Domain/            net10.0        — no I/O, no Windows, no packages
  PrintFlow.Workflow/          net10.0        — workflow engine, application services, ports
  PrintFlow.Infrastructure/    net10.0-windows— persistence, workspace, file inspection, preset, fake adapters
  PrintFlow.App/               net10.0-windows, WPF — shell, view models, composition root
tests/
  PrintFlow.Tests/             net10.0-windows— unit + integration + architecture
```

| Project | Contains | Deliberately excluded |
| --- | --- | --- |
| `PrintFlow.Domain` | `ProcessingSession`, `InputSnapshot`, `Revision`, `ProcessingAttempt`, `ReviewDecision`, `PrintOutput`, value objects (`Sha256`, `WorkspaceFileRef`, `OutputName`, `PrintDimensions`), enums, `OperationResult<T>`, `OperationFailure` | Any `System.IO`, any SQL, any package reference |
| `PrintFlow.Workflow` | Three `WorkflowDefinition`s, `IWorkflowEngine` (pure reducer), transition table, `WorkflowCommand`/`WorkflowEffect`, `SessionService` (orchestration), **all ports**: `ISessionRepository`, `IWorkspace`, `IFileInspector`, `IMeituProcessor`, `IPhotoshopOutputProcessor`, `IWorkstationPresetProvider`, `IRecycleBin`, `IEnvironmentGate` | Any implementation of those ports |
| `PrintFlow.Infrastructure` | `Sqlite/` (connection factory, migrations, Dapper repositories), `Workspace/` (paths, naming, snapshots, recycle bin), `Imaging/` (`WicFileInspector`), `Preset/` (loader + verifier), `Adapters/Fake/` | Any WPF view, any workflow rule |
| `PrintFlow.App` | `App.xaml(.cs)` with the **composition root**, `MainWindow`, `HomeViewModel`, `SessionViewModel`, `PresetStatusViewModel`, converters, `.resx` | Any SQL, any `System.IO` on production paths, any workflow rule |
| `PrintFlow.Tests` | `Unit/`, `Integration/`, `Architecture/`, `Fixtures/` | Screen-coordinate or click-sequence tests |

**Why `Persistence` and `Workspace` are not separate projects.** Both are outward adapters implementing ports declared in `Workflow`; they share a lifetime, a configuration source, and a single consumer (the composition root). Splitting them yields three extra `.csproj` files, three extra package graphs, and zero additional enforcement — the boundary that matters (`Workflow` must not reference either implementation) is already enforced at assembly level by `Workflow` having no reference to `Infrastructure`. Internal separation is by namespace and folder, and is enforced by architecture tests (§17.4).

**Why `Workflow` *is* separate from `Domain`.** The workflow engine is the deepest module in the system and the one most likely to be reasoned about in isolation. A separate assembly makes "Domain knows nothing about workflows" mechanically true rather than aspirational, and makes the pure-reducer test suite obviously free of I/O.

**Why `App` may reference `Infrastructure`.** Something must compose the object graph. `App` references `Infrastructure` **only** from `PrintFlow.App.Composition`; an architecture test asserts that no type outside that namespace references `PrintFlow.Infrastructure.*` or `Microsoft.Data.Sqlite` (§17.4). This is the standard composition-root exemption, made checkable.

### 4.3 Decision 3 — MVVM approach

**CommunityToolkit.Mvvm 8.x.**

- Source generators (`[ObservableProperty]`, `[RelayCommand]`) remove the `INotifyPropertyChanged` boilerplate at compile time, with no runtime reflection and no startup cost — appropriate for an offline desktop installer.
- MIT licensed, maintained by Microsoft, no opinions about navigation, DI, or application lifetime, so it cannot leak into `Workflow` or `Domain`.
- Rejected: hand-rolled base class (pure boilerplate for no benefit); Prism/Caliburn.Micro (heavy, imposes container and navigation models the MVP does not need); ReactiveUI (reactive complexity unjustified for a single-session shell).

DI/logging: `Microsoft.Extensions.DependencyInjection` (plain `ServiceCollection` in `App.OnStartup`; no generic host) + `Microsoft.Extensions.Logging.Abstractions` in modules, with a **local file sink only** (Serilog.Sinks.File) configured in `App`. No network sink of any kind — privacy is an invariant, not a setting.

### 4.4 Decision 4 — SQLite library / ORM

**Microsoft.Data.Sqlite + Dapper, with hand-written SQL and hand-written migrations.**

Justification:

- The schema is small (11 tables) and **append-mostly**: `Revision`, `ProcessingAttempt`, and `ReviewDecision` are immutable once written. EF Core's change tracking, identity map, and navigation fix-up are built for mutable object graphs; here they add a layer to fight rather than a layer to use.
- Several invariants are best enforced in the database itself (CHECK constraints and immutability triggers, §11.3). Hand-written DDL makes those reviewable in one file; EF Core migrations would either lose them or require raw-SQL escape hatches anyway.
- The application layer must sequence file-system work and metadata commits precisely (§10.5). Explicit `SqliteTransaction` scopes make that ordering visible; EF Core's `SaveChanges` hides it.
- Deployment: Dapper is ~100 KB and has no native assets beyond `SQLitePCLRaw`; EF Core adds several MB and a model-build cost to an offline installer.
- Rejected: raw ADO.NET without Dapper (hand-written materialisation for 11 entities is pure boilerplate); EF Core (above); `sqlite-net` (weaker transaction and migration story).

Trade-off accepted: no LINQ query composition, and queries are hand-written. At this size that is a feature — all SQL lives in one reviewable place behind repository interfaces, and the UI never sees it.

Connection settings applied to **every** connection:

```sql
PRAGMA journal_mode = WAL;      -- crash resilience for a desktop app
PRAGMA synchronous  = FULL;     -- metadata writes are rare and small; durability wins
PRAGMA foreign_keys = ON;       -- OFF by default in SQLite; must be set per connection
PRAGMA busy_timeout = 5000;
```

### 4.5 Decision 5 — migration strategy

Forward-only, `PRAGMA user_version`-gated, embedded SQL:

```text
src/PrintFlow.Infrastructure/Sqlite/Migrations/
  0001_initial_schema.sql
  0002_….sql
```

- Each script is an embedded resource, applied in a single transaction, followed by `PRAGMA user_version = N` in the same transaction.
- A `SchemaMigration` table records `(Version, Name, AppliedAtUtc, ScriptSha256)` as an audit trail; `user_version` remains the gate.
- **Fail closed**: if `user_version` exceeds the highest script the binary knows, startup aborts with a clear message ("this database was created by a newer PrintFlow"). Never downgrade, never auto-repair.
- No down migrations. For a single-user local database, roll-back means restoring a file copy.
- Migrations run before any repository is resolved (§18).

---

## 5. Dependency graph

```text
                    ┌───────────────────────┐
                    │   PrintFlow.App       │  WPF, ViewModels
                    │   (composition root)  │
                    └───────────┬───────────┘
                                │ references
              ┌─────────────────┼─────────────────────────────┐
              │                 │                             │
              ▼                 ▼                  (composition root only)
   ┌───────────────────┐  ┌────────────────┐        ┌─────────────────────────┐
   │ PrintFlow.Workflow│  │PrintFlow.Domain│◀───────│ PrintFlow.Infrastructure│
   │  engine + ports   │  │ types, no I/O  │        │ SQLite | Workspace |    │
   └─────────┬─────────┘  └────────────────┘        │ Imaging | Preset | Fakes│
             │  references                          └────────────┬────────────┘
             ▼                                                   │ implements ports
   ┌────────────────┐                                            │
   │ PrintFlow.Domain│◀──────────────────────────────────────────┘
   └────────────────┘
```

Rules, each enforced by an architecture test (§17.4):

| Rule | Enforcement |
| --- | --- |
| `Domain` references no other PrintFlow project and no third-party package | assembly-reference assertion |
| `Workflow` references `Domain` only | assembly-reference assertion |
| `Workflow` never references `Infrastructure`, `Microsoft.Data.Sqlite`, `System.Windows.*`, or any automation library | assembly-reference assertion |
| `Infrastructure` references `Domain` + `Workflow` (to implement ports), never `App` | assembly-reference assertion |
| `App` types outside `PrintFlow.App.Composition` reference no `Infrastructure` or SQLite type | type-level rule (NetArchTest) |
| `System.IO` file/directory APIs appear only in `Infrastructure.Workspace`, `Infrastructure.Imaging`, `Infrastructure.Preset`, and tests | `BannedApiAnalyzers` with per-project `BannedSymbols.txt` — compile-time, not test-time |

The last rule is how invariant 14 ("other modules do not construct arbitrary production paths") becomes mechanical: outside the workspace module, the type system offers only `WorkspaceFileRef`, and `Path.Combine`/`File.*`/`Directory.*` do not compile.

---

## 6. Domain model

`PrintFlow.Domain` contains immutable records and value objects with no I/O. Everything below is `sealed record` unless noted.

### 6.1 Aggregate map

```text
ProcessingSession (root)
├── InputSnapshot        1:1   provenance of the imported original
├── SessionStep          1:N   one row per step of this session's workflow definition
├── Revision             1:N   chained via SourceRevisionId (tree, root = the snapshot revision)
├── ProcessingAttempt    1:N   one per adapter/manual invocation
├── ReviewDecision       1:N   append-only, hash-bound
└── PrintOutput          1:N   produced from an approved Revision (Epic 11400 fills the behaviour)
```

No `Job`, `Order`, `Customer`, `Asset`, or `Artwork` type exists anywhere in the solution. An architecture test asserts that no type with those names is declared (cheap guard against scope creep).

### 6.2 ProcessingSession

| Member | Type | Notes |
| --- | --- | --- |
| `Id` | `SessionId` (UUIDv7 wrapper) | |
| `WorkflowType` | `WorkflowType` enum | `PrepareAsset`, `PrepareCustomerDesign`, `GeneratePrintTiff` |
| `OutputName` | `OutputName` value object | Sanitised; defaults to the source filename stem; editing never touches the source file |
| `CurrentStep` | `StepKind` | Denormalised for query/resume; authority is the `SessionStep` set |
| `State` | `SessionState` | `Active`, `HandedOff`, `Completed`, `Abandoned` |
| `WorkspacePath` | `WorkspaceDirRef` | **Relative** to the workspace root |
| `CreatedAtUtc` / `UpdatedAtUtc` / `CompletedAtUtc?` | `DateTimeOffset` | |
| `HandedOffAtUtc?`, `HandOffReason?` | | |
| `AbandonedAtUtc?`, `AbandonReason?` | | |

`WorkflowType` is fixed after the first derived `Revision` exists (design §6.1). The engine rejects `SelectWorkflow` after that point.

### 6.3 InputSnapshot

Distinct concept, distinct table — but it is **not** a second file model. It is the provenance record attached to the root `Revision`:

| Member | Notes |
| --- | --- |
| `Id`, `SessionId` | |
| `RootRevisionId` | The `Revision` whose `Operation = Import` and `SourceRevisionId = NULL` |
| `OriginalSourcePath` | Absolute path of the user's file, **informational only**; the application never opens it for writing and never deletes it |
| `OriginalFileName` | |
| `ImportedAtUtc` | |

Rationale: keeping the snapshot inside the `Revision` chain means every managed file in the system — without exception — has a hash, dimensions, validity, and a place in the derivation tree. One rule, no special cases (invariant 2).

The snapshot file is written into `Source\`, hashed, and then marked `FileAttributes.ReadOnly` as a cheap physical defence.

### 6.4 Revision

| Member | Notes |
| --- | --- |
| `Id`, `SessionId`, `SourceRevisionId?` | Tree edge; `NULL` only for the import root |
| `Operation` | `Import`, `Enhance`, `RemoveBackground`, `Trim`, `PromoteApproved`, `ManualImport` |
| `File` | `WorkspaceFileRef` (relative), `Format`, `ByteLength`, `Sha256` |
| `Metadata` | `PixelWidth?`, `PixelHeight?`, `DpiX?`, `DpiY?`, `ColourMode`, `HasAlpha?` |
| `CreatedAtUtc` | |
| `IsValid`, `InvalidatedAtUtc?`, `InvalidationReason?` | `Superseded`, `UpstreamChanged`, `FileMutated`, `Rejected`, `SessionReset` |
| `ReviewState` | Cached projection: `NotReviewed`, `Approved`, `Rejected` |

Once written, only `IsValid`, `InvalidatedAtUtc`, `InvalidationReason`, and `ReviewState` may change. Everything else is immutable, enforced by a SQLite trigger (§11.3) as well as by the repository API.

### 6.5 ProcessingAttempt, ReviewDecision, PrintOutput

**ProcessingAttempt** — `Id`, `SessionId`, `Step`, `InputRevisionId`, `Operation`, `AdapterId` (e.g. `fake-meitu-v1`), `StartedAtUtc`, `EndedAtUtc?`, `ResultStatus` ∈ {`Running`, `Succeeded`, `Failed`, `Interrupted`, `Cancelled`}, `OutputRevisionId?`, `FailureCode?`, `FailureDetailJson?`, `RetryOfAttemptId?`, `RetrySequence`.

`OutputRevisionId` is non-null **only** when `ResultStatus = 'Succeeded'` — enforced by a table CHECK. That is invariant 5 made structural.

**ReviewDecision** — `Id`, `SessionId`, `Step`, `SubjectKind` ∈ {`Revision`, `PrintOutput`}, `SubjectId`, `ReviewedSha256`, `Operator`, `DecidedAtUtc`, `Decision` ∈ {`Approved`, `Rejected`}, `QuickReason?`, `Notes?`. Append-only; UPDATE and DELETE blocked by trigger. `Operator` is the Windows username, falling back to `Operator` (design §4.1).

**PrintOutput** (modelled now, exercised in Epic 11400) — `Id`, `SessionId`, `SourceRevisionId`, `TargetWidthMm`, `TargetHeightMm`, `PixelWidth`, `PixelHeight`, `Dpi` (300), `SizePresetId` (`A3_LANDSCAPE` | `A3_PORTRAIT` | `A4` | `A5` | `CUSTOM`), `WhiteUnderbaseBranch`, `ProductionPresetId` + `ProductionPresetSha256`, `File` (`WorkspaceFileRef`, `ByteLength`, `Sha256`), `ValidationResultJson?`, `ReviewState`, `IsValid`, `InvalidationReason?`, `RecycledAtUtc?`. Kept deliberately thin: 11100 creates rows via the fake Photoshop adapter and never inspects CMYK or spot channels.

### 6.6 Decision 6 — ID strategy

**UUIDv7, stored as canonical 36-character lowercase TEXT.**

- Time-ordered, so primary-key inserts are sequential (good B-tree locality) and IDs sort chronologically — genuinely useful when reading the database by hand during MVP debugging and when resuming "the most recent session".
- Generated client-side, so an entity is fully formed before it touches the database — required because file-system work precedes the metadata commit (§10.5).
- `Guid.CreateVersion7()` is in-box on .NET 9+; no package, no bespoke code. On .NET 8, one ~20-line implementation behind `IIdGenerator`.
- TEXT rather than BLOB: 11 small tables, low row counts, and human readability in a DB browser is worth more than 20 bytes per row. Wrapper structs (`SessionId`, `RevisionId`, …) prevent cross-entity ID mix-ups at compile time.
- Rejected: ULID (needs a package for a marginal encoding gain); sequential INTEGER (requires a DB round-trip before the entity exists); GUIDv4 (random insert order, no chronological signal).

### 6.7 Decision 7 — timestamp strategy

- Domain uses `DateTimeOffset`; all instants are **UTC**.
- Persisted as TEXT `yyyy-MM-ddTHH:mm:ss.fffZ` — fixed width, lexicographically sortable, unambiguous, readable.
- Obtained exclusively from `TimeProvider` injected into services; `FakeTimeProvider` in tests. No `DateTime.Now` anywhere (banned symbol).
- Local time is a **display** concern: the UI converts using the workstation time zone. The preset records `timeZoneId` for evidence purposes; the application does not store local timestamps.

---

## 7. The three workflow definitions

### 7.1 Modelling decision — reviews are step *phases*, not steps

The brief draws review nodes inline (`Enhancement → Enhancement Review`). The state list simultaneously contains `Review Required` as a **state**. Modelling both would duplicate the concept.

**Decision: a review is the `ReviewRequired` phase of the step that produced the result.** Consequences:

- "Enhancement → Enhancement Review" is one `Enhancement` step whose lifecycle is `Waiting → Processing → ReviewRequired → Approved | RetryRequired`.
- "Photoshop Output → TIFF Validation → Final Review" is one `PhotoshopOutput` step: automated validation is the gate that decides whether the attempt produces a `PrintOutput` at all, and `FinalReview` is that step's `ReviewRequired` phase.
- **Validation is never a step.** It is the phase of every producing step that turns a raw adapter result into a valid artefact — exactly invariant 6. Making it a step would allow the illegal intermediate state "output exists, unvalidated, but the workflow has advanced".

The UI still shows a dedicated review *screen* per reviewed step; screens and steps need not be 1:1.

### 7.2 Step kinds

`Import`, `OriginalConfirmation`, `Enhancement`, `BackgroundRemoval`, `Trim`, `ApprovedPngExport`, `PrintDimensions`, `PhotoshopOutput`.

### 7.3 PREPARE_ASSET

| # | Step | Skippable | Requires review | Produces Revision | Adapter |
| --- | --- | --- | --- | --- | --- |
| 1 | `Import` | no | no | yes (root) | — |
| 2 | `OriginalConfirmation` | no | no (confirm only) | no | — |
| 3 | `Enhancement` | **yes** | yes | yes | Meitu |
| 4 | `BackgroundRemoval` | **yes** | yes | yes | Meitu |
| 5 | `Trim` | no | yes | yes | internal (Epic 11200) |
| 6 | `ApprovedPngExport` | no | no | yes (`PromoteApproved`) | — |

Terminal: `Complete` → `SessionState.Completed`. No print dimensions, no TIFF.

`ApprovedPngExport` copies the approved trimmed revision into `Approved\` under the final output name. Because the bytes are unchanged, the hash is unchanged, so the existing hash-bound approval covers the promoted file by construction — no second review is needed and none is fabricated. The promotion is still recorded as a `Revision` so the chain stays complete.

### 7.4 PREPARE_CUSTOMER_DESIGN

| # | Step | Skippable | Requires review | Produces | Adapter |
| --- | --- | --- | --- | --- | --- |
| 1 | `Import` | no | no | Revision (root) | — |
| 2 | `OriginalConfirmation` | no | no | — | — |
| 3 | `Enhancement` | **yes** | yes | Revision | Meitu |
| 4 | `BackgroundRemoval` | **yes** | yes | Revision | Meitu |
| 5 | `Trim` | no | yes | Revision | internal |
| 6 | `PrintDimensions` | no | no (explicit confirm) | — | — |
| 7 | `PhotoshopOutput` | no | yes (final review) | **PrintOutput** | Photoshop |

Enhancement always precedes background removal (fixed order, not configurable).

### 7.5 GENERATE_PRINT_TIFF

| # | Step | Skippable | Requires review | Produces | Adapter |
| --- | --- | --- | --- | --- | --- |
| 1 | `Import` | no | no | Revision (root) | — |
| 2 | `OriginalConfirmation` | no | **yes** (design readiness review) | — | — |
| 3 | `PrintDimensions` | no | no | — | — |
| 4 | `PhotoshopOutput` | no | yes (final review) | **PrintOutput** | Photoshop |

No Meitu step exists in this definition, and no automatic trim. If the design is not ready, the operator abandons the session and re-imports through another workflow (design §6.4) — the engine offers no cross-workflow conversion.

### 7.6 Representation

```csharp
public sealed record StepDefinition(
    StepKind Kind,
    int Ordinal,
    bool IsSkippable,
    bool RequiresReview,
    bool ProducesRevision,
    OperationKind? Operation,
    AdapterKind? Adapter);

public sealed record WorkflowDefinition(
    WorkflowType Type,
    IReadOnlyList<StepDefinition> Steps);

public static class WorkflowCatalog
{
    public static readonly WorkflowDefinition PrepareAsset          = /* … */;
    public static readonly WorkflowDefinition PrepareCustomerDesign = /* … */;
    public static readonly WorkflowDefinition GeneratePrintTiff     = /* … */;
    public static WorkflowDefinition For(WorkflowType type) => /* switch */;
}
```

Three static readonly definitions in code. **No configuration file, no database-driven definitions, no designer.** Adding a fourth workflow is a code change and a code review — which is the intended cost.

---

## 8. State transition design

### 8.1 Decision 8 — workflow modelling strategy

**A pure reducer whose legality is expressed as an explicit transition table.**

```csharp
public interface IWorkflowEngine
{
    WorkflowTransition Apply(WorkflowSnapshot state, WorkflowCommand command, CommandContext context);
}

public sealed record WorkflowTransition(
    WorkflowSnapshot? NewState,
    IReadOnlyList<WorkflowEffect> Effects,
    CommandRejection? Rejection);
```

Properties that matter:

- **Pure.** No I/O, no clock, no randomness (time and IDs arrive via `CommandContext`). Every rule is unit-testable without a database, a disk, or a mock.
- **Effects are data, not calls.** The engine returns `RunAdapter`, `CreateWorkingCopy`, `PersistRevision`, `InvalidateDescendants`, `RecordReview`, `ReleaseAutomationLock`, `CleanupWorking` as records. `SessionService` interprets them, which is where transactions and file ordering live (§10.5). The engine cannot accidentally perform a side effect.
- **Table, not scattered `if`s.** Legality is one `(StepState × CommandKind) → Outcome` table plus step-definition guards. A test enumerates every pair and asserts each has an explicit outcome, so no combination can fall through silently.
- Rejected: a general state-machine library (a configurable engine is explicitly out of scope); event sourcing (an audit log we would then have to project — the append-only `Revision`/`Attempt`/`Review` tables already provide the audit trail without the replay machinery).

### 8.2 Two state levels

The ten named states live at two levels. Conflating them is the classic error here.

| Brief's state | Level | Enum value |
| --- | --- | --- |
| Waiting | step | `StepState.Waiting` |
| Processing | step | `StepState.Processing` |
| Review Required | step | `StepState.ReviewRequired` |
| Approved | step | `StepState.Approved` |
| Retry Required | step | `StepState.RetryRequired` |
| Skipped | step | `StepState.Skipped` |
| Failed | step | `StepState.Failed` |
| Interrupted | step | `StepState.Interrupted` |
| Handed Off | **session** | `SessionState.HandedOff` |
| Completed | **session** | `SessionState.Completed` |

Plus `SessionState.Active` and `SessionState.Abandoned`, which the brief implies but does not name. A step is *finished* when its state is `Approved` or `Skipped`.

### 8.3 Step transition table

Rows = current `StepState`; columns = command. `—` = rejected with a `CommandRejection` (no state change, nothing persisted).

| | ConfirmOriginal | StartStep | Approve | Reject | Retry | Skip | HandOff | SetDimensions |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **Waiting** | →Approved¹ | →Processing | — | — | — | →Skipped² | — | →Approved³ |
| **Processing** | — | — | — | — | — | — | — | — |
| **ReviewRequired** | — | — | →Approved | →RetryRequired | — | — | →(session HandedOff) | — |
| **Approved** | — | — | — | — | — | — | — | — |
| **RetryRequired** | — | →Processing⁴ | — | — | →Waiting⁴ | →Skipped² | →(session HandedOff) | — |
| **Skipped** | — | — | — | — | — | — | — | — |
| **Failed** | — | →Processing⁴ | — | — | →Waiting⁴ | →Skipped² | →(session HandedOff) | — |
| **Interrupted** | — | →Processing⁴ | — | — | →Waiting⁴ | →Skipped² | →(session HandedOff) | — |

¹ only for `StepKind.OriginalConfirmation`; writes a `ReviewDecision` when `RequiresReview` (GENERATE_PRINT_TIFF).
² only when `StepDefinition.IsSkippable`; records the reason (default "File already satisfies this step"); creates **no** Revision.
³ only for `StepKind.PrintDimensions`; requires a valid `PrintDimensions` payload.
⁴ always from a **fresh working copy** of the latest approved upstream revision (design §7.2).

System-originated transitions, never issued by the UI:

| Command | From | To | Precondition |
| --- | --- | --- | --- |
| `AttemptSucceeded` | Processing | ReviewRequired (or Approved when `!RequiresReview`) | A **validated** Revision exists |
| `AttemptFailed` | Processing | Failed | `OperationFailure` recorded |
| `AttemptInterrupted` | Processing | Interrupted | Issued by startup recovery only |

### 8.4 Session-level transitions

| Command | From | To | Effects |
| --- | --- | --- | --- |
| `ImportInput` | (none) | Active | create workspace, snapshot, hash, root Revision |
| `SelectWorkflow` | Active | Active | rejected once any non-root Revision exists |
| `HandOff` | Active | HandedOff | working copy from the latest approved revision; open folder; release automation lock; automated flow ends |
| `ReturnToStep(target)` | Active | Active | target→Waiting; all downstream steps→Waiting; **invalidate all descendants** (§10.4) |
| `Complete` | Active | Completed | requires every non-skipped step `Approved`/`Skipped` and the terminal artefact approved; cleans `Working\` |
| `AddAnotherSize` | Completed | Active | reopens at `PrintDimensions`; **does not** invalidate existing approved PrintOutputs (siblings, not descendants) |
| `AbandonSession` | Active/HandedOff | Abandoned | releases lock; retains all files |

### 8.5 Global automation lock

Design invariant 7 ("only one Session may control Meitu or Photoshop"). Modelled now even though nothing is controlled yet:

- Singleton row `AutomationLock(Id = 1, SessionId, AcquiredAtUtc, ProcessId, MachineName)`.
- `StartStep` on an adapter-backed step acquires it; `AttemptSucceeded/Failed/Interrupted`, `HandOff`, `Complete`, and `AbandonSession` release it.
- Startup recovery releases a lock whose `ProcessId` is not a live PrintFlow process.
- A single-instance `Mutex` in `App` prevents two PrintFlow instances from contending for the database and the lock at all.

---

## 9. Command model

### 9.1 Command catalogue

```csharp
public abstract record WorkflowCommand
{
    // Operator commands — issued by the UI
    public sealed record ImportInput(string SourceAbsolutePath, WorkflowType? PreselectedWorkflow) : WorkflowCommand;
    public sealed record SelectWorkflow(WorkflowType Type)                                          : WorkflowCommand;
    public sealed record SetOutputName(OutputName Name)                                             : WorkflowCommand;
    public sealed record ConfirmOriginal(string? Notes)                                             : WorkflowCommand;
    public sealed record StartStep(StepKind Step)                                                   : WorkflowCommand;
    public sealed record Approve(StepKind Step, Sha256 ReviewedHash, string? Notes)                 : WorkflowCommand;
    public sealed record Reject(StepKind Step, Sha256 ReviewedHash, RejectionReason Reason, string? Notes) : WorkflowCommand;
    public sealed record Retry(StepKind Step)                                                       : WorkflowCommand;
    public sealed record Skip(StepKind Step, string Reason)                                         : WorkflowCommand;
    public sealed record HandOff(StepKind Step, string Reason)                                      : WorkflowCommand;
    public sealed record SetPrintDimensions(PrintDimensions Dimensions)                             : WorkflowCommand;
    public sealed record SelectWhiteUnderbaseBranch(WhiteUnderbaseBranch Branch, string Justification) : WorkflowCommand;
    public sealed record ReturnToStep(StepKind Target)                                              : WorkflowCommand;
    public sealed record Complete()                                                                 : WorkflowCommand;
    public sealed record AddAnotherSize()                                                           : WorkflowCommand;
    public sealed record AbandonSession(string Reason)                                              : WorkflowCommand;

    // System commands — never reachable from the UI
    internal sealed record AttemptSucceeded(AttemptId Id, RevisionId OutputRevision)                : WorkflowCommand;
    internal sealed record AttemptFailed(AttemptId Id, OperationFailure Failure)                    : WorkflowCommand;
    internal sealed record AttemptInterrupted(AttemptId Id)                                         : WorkflowCommand;
}
```

`Approve`/`Reject` carry the hash the operator actually reviewed. `SessionService` re-hashes the file and rejects the command if it no longer matches — invariants 7 and 8 enforced at the point of decision, not merely recorded.

`SelectWhiteUnderbaseBranch` has **no default value** and is a precondition of `StartStep(PhotoshopOutput)`. The 0/1/2 px classification is therefore structurally an explicit operator decision that a later UI step must collect (Epic 11400) — the model refuses to infer it, exactly as the baseline requires.

### 9.2 How the UI issues commands (invariants 11–13)

```text
ViewModel  →  ISessionService.ExecuteAsync(SessionId, WorkflowCommand)  →  OperationResult<SessionView>
```

- The UI never constructs a `WorkflowSnapshot`, never sets a `StepState`, never writes SQL, never touches a file path, and never references an adapter.
- `SessionView` is a flattened read model for binding (step list with labels and states, available commands, current artefact metadata). It exposes **no** setters.
- The set of currently legal commands is computed by the engine and returned in `SessionView.AvailableCommands`, so buttons are enabled from the engine's own rules rather than a duplicated UI opinion. One source of truth for legality.

### 9.3 Decision 13 — structured error representation

Two distinct failure concepts, deliberately not merged:

| Concept | Type | Meaning | Handling |
| --- | --- | --- | --- |
| Illegal command | `CommandRejection(RejectionCode, string DebugMessage)` | The UI offered something the engine forbids — a **defect**, since the UI renders `AvailableCommands` | Logged at Warning, surfaced as a generic message; asserted never to occur in tests |
| Operation failure | `OperationFailure` | An expected production event: adapter timeout, unknown dialog, unreadable output, hash mismatch | Persisted on the attempt, shown on the Error Details screen with recovery actions |

```csharp
public sealed record OperationFailure(
    FailureCode Code,                 // stable English enum, persisted
    string MessageKey,                // .resx key → localised operator text
    string TechnicalDetail,           // English, for the log; never shown raw
    IReadOnlyDictionary<string,string> Context,
    WorkspaceFileRef? ScreenshotRef,  // populated by real adapters from Epics 11300/11400
    bool IsRetryable);

public enum FailureCode
{
    AdapterUnavailable, EnvironmentNotVerified, PresetHashMismatch, UnknownDialog,
    Timeout, Cancelled, OutputMissing, OutputUnreadable, OutputValidationFailed,
    RevisionIntegrityMismatch, WorkspaceError, PersistenceError, PreconditionNotMet
}
```

- Module seams return `OperationResult<T>` (a ~40-line hand-rolled discriminated result in `Domain`, not a package — the shape is small and adding OneOf/LanguageExt buys nothing here).
- Exceptions are reserved for programmer error and genuine infrastructure faults; each adapter and repository converts them to `OperationFailure` at its own boundary, logging the exception locally.
- Enum names are stable English and persisted as TEXT (design §13.4); operator text is resolved from `.resx` at display time.

---

## 10. Revision / Attempt / Approval invariants

### 10.1 The validation pipeline (invariants 4, 5, 6)

An adapter result becomes a `Revision` only by passing every stage. Any failure leaves the attempt `Failed` and creates **no** Revision row:

```text
adapter returns  →  file exists at the reserved path
                 →  byte length > 0 and stable across two reads
                 →  full stream read succeeds  ── the SHA-256 computation *is* the readability proof
                 →  format sniffed from magic bytes and permitted for this step
                 →  pixel metadata extracted (or explicitly marked unavailable for PSD/PDF)
                 →  Revision row written inside the same transaction that marks the attempt Succeeded
```

Hashing subsumes "complete readability validation": a file that cannot be streamed to the end cannot be hashed. There is no separate, weaker readability check to get out of step with the hash.

### 10.2 Attempt vs Revision separation (invariant 4)

- Every adapter invocation and every manual handoff creates a `ProcessingAttempt` row *before* the work starts (`ResultStatus = 'Running'`). That row is what makes a crash detectable.
- `ProcessingAttempt.OutputRevisionId` is non-null only when `ResultStatus = 'Succeeded'`, enforced by a table CHECK.
- N attempts may precede one revision. Retries are chained through `RetryOfAttemptId` so the failure history of a step is a queryable list.

### 10.3 Hash-bound approval (invariants 7, 8)

Approval is **not** a mutable flag on a file. It is a derived predicate:

```text
IsApproved(revision) ⇔
      revision.IsValid
  AND ∃ latest ReviewDecision d for (session, step, revision)
        where d.Decision = Approved
  AND d.ReviewedSha256 = SHA256(current bytes on disk)
```

`Revision.ReviewState` exists only as a cached projection for list queries. Before any step consumes an upstream revision — starting an adapter, promoting to `Approved\`, generating a TIFF — `RevisionIntegrityGuard` **re-hashes the file** and fails with `RevisionIntegrityMismatch` on mismatch, additionally marking the revision `IsValid = 0, InvalidationReason = FileMutated`.

Consequence: editing an approved PNG outside PrintFlow cannot silently carry its approval forward. This is directly testable (§17.2) and is one of the acceptance tests for Task 11105.

### 10.4 Decision 10 — downstream invalidation (invariant 10)

Revisions form a tree via `SourceRevisionId`. Invalidation is a recursive descendant walk executed in **one** transaction:

```sql
WITH RECURSIVE descendants(Id) AS (
    SELECT Id FROM Revision WHERE Id = @changedRevisionId
    UNION ALL
    SELECT r.Id FROM Revision r JOIN descendants d ON r.SourceRevisionId = d.Id
)
UPDATE Revision
   SET IsValid = 0, InvalidatedAtUtc = @now, InvalidationReason = @reason
 WHERE Id IN (SELECT Id FROM descendants WHERE Id <> @changedRevisionId);
```

Then, in the same transaction: dependent `PrintOutput` rows are invalidated, and the `SessionStep` rows for every step at or after the changed step are reset to `Waiting`.

Key properties:

- `ReviewDecision` rows are **never** deleted or edited — the audit survives. They simply stop satisfying the `IsApproved` predicate because the revision is no longer valid.
- Files are **not** deleted during invalidation. Invalid derived files move to `Rejected\` (retained for comparison until the session ends); a rejected PrintFlow-generated TIFF goes to the Recycle Bin (design §10) via `IRecycleBin`.
- Siblings are unaffected: `AddAnotherSize` produces a second `PrintOutput` from the *same* approved revision, which is not a descendant of the first, so approving or rejecting one leaves the other alone.

### 10.5 Ordering of file work and metadata commits

SQLite transactions cannot roll back the file system, so the ordering is fixed and explicit:

1. **Reserve** the destination path (per-attempt subdirectory in `Working\`, or `CreateNew` reservation in `Approved\`).
2. **Perform** the file work (adapter writes; or copy/move).
3. **Validate and hash** (§10.1).
4. **Commit metadata** in one transaction: attempt result + revision + step states + session `UpdatedAtUtc`.

If step 4 fails, the file on disk is **orphaned, not usable**: nothing in the system can reference a file without a `Revision` row. The recovery pass moves such files to `Quarantine\` and logs them. Deletions and Recycle Bin operations never occur inside a database transaction.

---

## 11. SQLite schema

Location: `D:\PrintFlowStudio\Data\printflow.db` (plus `-wal` / `-shm` sidecars). Rationale: all PrintFlow-managed state on one volume, one backup unit, and no cross-volume permission surprises. **No image binaries in the database, ever** (invariant 15) — files hold pixels, SQLite holds metadata.

### 11.1 Tables

| Table | Purpose |
| --- | --- |
| `SchemaMigration` | applied migration audit |
| `Setting` | key/value: UI language, adapter mode, log retention |
| `ProcessingSession` | session root |
| `SessionStep` | normalised workflow snapshot, one row per step |
| `InputSnapshot` | provenance of the imported original |
| `Revision` | every managed file, chained |
| `ProcessingAttempt` | every adapter/manual invocation |
| `ReviewDecision` | append-only hash-bound decisions |
| `PrintOutput` | production TIFF records (thin in 11100) |
| `AutomationLock` | singleton global lock |
| `AutomationLogEntry` | structured failures + screenshot refs |

### 11.2 Core DDL (migration `0001_initial_schema.sql`, abridged)

```sql
CREATE TABLE ProcessingSession (
    Id              TEXT PRIMARY KEY NOT NULL,
    WorkflowType    TEXT NOT NULL CHECK (WorkflowType IN
                        ('PREPARE_ASSET','PREPARE_CUSTOMER_DESIGN','GENERATE_PRINT_TIFF')),
    OutputName      TEXT NOT NULL,
    CurrentStep     TEXT NOT NULL,
    State           TEXT NOT NULL CHECK (State IN ('ACTIVE','HANDED_OFF','COMPLETED','ABANDONED')),
    WorkspacePath   TEXT NOT NULL UNIQUE,          -- relative to workspace root
    CreatedAtUtc    TEXT NOT NULL,
    UpdatedAtUtc    TEXT NOT NULL,
    CompletedAtUtc  TEXT NULL,
    HandedOffAtUtc  TEXT NULL,
    HandOffReason   TEXT NULL,
    AbandonedAtUtc  TEXT NULL,
    AbandonReason   TEXT NULL
);

CREATE TABLE SessionStep (
    SessionId        TEXT NOT NULL REFERENCES ProcessingSession(Id) ON DELETE CASCADE,
    StepKind         TEXT NOT NULL,
    Ordinal          INTEGER NOT NULL,
    State            TEXT NOT NULL CHECK (State IN
                        ('WAITING','PROCESSING','REVIEW_REQUIRED','APPROVED',
                         'RETRY_REQUIRED','SKIPPED','FAILED','INTERRUPTED')),
    ActiveRevisionId TEXT NULL REFERENCES Revision(Id),
    SkipReason       TEXT NULL,
    UpdatedAtUtc     TEXT NOT NULL,
    PRIMARY KEY (SessionId, StepKind)
);

CREATE TABLE Revision (
    Id                 TEXT PRIMARY KEY NOT NULL,
    SessionId          TEXT NOT NULL REFERENCES ProcessingSession(Id) ON DELETE CASCADE,
    SourceRevisionId   TEXT NULL REFERENCES Revision(Id),
    Operation          TEXT NOT NULL CHECK (Operation IN
                          ('IMPORT','ENHANCE','REMOVE_BACKGROUND','TRIM','PROMOTE_APPROVED','MANUAL_IMPORT')),
    RelativePath       TEXT NOT NULL,
    Format             TEXT NOT NULL,
    ByteLength         INTEGER NOT NULL CHECK (ByteLength > 0),
    Sha256             TEXT NOT NULL CHECK (length(Sha256) = 64),
    PixelWidth         INTEGER NULL,
    PixelHeight        INTEGER NULL,
    DpiX               REAL    NULL,
    DpiY               REAL    NULL,
    ColourMode         TEXT NOT NULL,
    HasAlpha           INTEGER NULL,
    CreatedAtUtc       TEXT NOT NULL,
    IsValid            INTEGER NOT NULL DEFAULT 1 CHECK (IsValid IN (0,1)),
    InvalidatedAtUtc   TEXT NULL,
    InvalidationReason TEXT NULL CHECK (InvalidationReason IS NULL OR InvalidationReason IN
                          ('SUPERSEDED','UPSTREAM_CHANGED','FILE_MUTATED','REJECTED','SESSION_RESET')),
    ReviewState        TEXT NOT NULL DEFAULT 'NOT_REVIEWED'
                          CHECK (ReviewState IN ('NOT_REVIEWED','APPROVED','REJECTED')),
    UNIQUE (SessionId, RelativePath)
);
CREATE INDEX IX_Revision_Session ON Revision(SessionId);
CREATE INDEX IX_Revision_Source  ON Revision(SourceRevisionId);

CREATE TABLE ProcessingAttempt (
    Id                TEXT PRIMARY KEY NOT NULL,
    SessionId         TEXT NOT NULL REFERENCES ProcessingSession(Id) ON DELETE CASCADE,
    StepKind          TEXT NOT NULL,
    InputRevisionId   TEXT NULL REFERENCES Revision(Id),
    Operation         TEXT NOT NULL,
    AdapterId         TEXT NOT NULL,                 -- e.g. 'fake-meitu-v1'
    StartedAtUtc      TEXT NOT NULL,
    EndedAtUtc        TEXT NULL,
    ResultStatus      TEXT NOT NULL CHECK (ResultStatus IN
                          ('RUNNING','SUCCEEDED','FAILED','INTERRUPTED','CANCELLED')),
    OutputRevisionId  TEXT NULL REFERENCES Revision(Id),
    FailureCode       TEXT NULL,
    FailureDetailJson TEXT NULL,
    RetryOfAttemptId  TEXT NULL REFERENCES ProcessingAttempt(Id),
    RetrySequence     INTEGER NOT NULL DEFAULT 0,
    -- Invariant 5, enforced structurally:
    CHECK ((ResultStatus =  'SUCCEEDED' AND OutputRevisionId IS NOT NULL)
        OR (ResultStatus <> 'SUCCEEDED' AND OutputRevisionId IS NULL))
);

CREATE TABLE ReviewDecision (
    Id             TEXT PRIMARY KEY NOT NULL,
    SessionId      TEXT NOT NULL REFERENCES ProcessingSession(Id) ON DELETE CASCADE,
    StepKind       TEXT NOT NULL,
    SubjectKind    TEXT NOT NULL CHECK (SubjectKind IN ('REVISION','PRINT_OUTPUT')),
    SubjectId      TEXT NOT NULL,
    ReviewedSha256 TEXT NOT NULL CHECK (length(ReviewedSha256) = 64),
    Operator       TEXT NOT NULL,
    DecidedAtUtc   TEXT NOT NULL,
    Decision       TEXT NOT NULL CHECK (Decision IN ('APPROVED','REJECTED')),
    QuickReason    TEXT NULL,
    Notes          TEXT NULL
);
CREATE INDEX IX_Review_Subject ON ReviewDecision(SubjectId, DecidedAtUtc);

CREATE TABLE PrintOutput (
    Id                     TEXT PRIMARY KEY NOT NULL,
    SessionId              TEXT NOT NULL REFERENCES ProcessingSession(Id) ON DELETE CASCADE,
    SourceRevisionId       TEXT NOT NULL REFERENCES Revision(Id),
    TargetWidthMm          REAL NOT NULL,
    TargetHeightMm         REAL NOT NULL,
    PixelWidth             INTEGER NOT NULL,
    PixelHeight            INTEGER NOT NULL,
    Dpi                    INTEGER NOT NULL DEFAULT 300,
    SizePresetId           TEXT NOT NULL,
    WhiteUnderbaseBranch   TEXT NOT NULL CHECK (WhiteUnderbaseBranch IN ('W1_0PX','W1_1PX','W1_2PX')),
    ProductionPresetId     TEXT NOT NULL,
    ProductionPresetSha256 TEXT NOT NULL,
    RelativePath           TEXT NOT NULL,
    ByteLength             INTEGER NOT NULL,
    Sha256                 TEXT NOT NULL CHECK (length(Sha256) = 64),
    ValidationResultJson   TEXT NULL,
    ReviewState            TEXT NOT NULL DEFAULT 'NOT_REVIEWED',
    IsValid                INTEGER NOT NULL DEFAULT 1,
    InvalidationReason     TEXT NULL,
    RecycledAtUtc          TEXT NULL,
    CreatedAtUtc           TEXT NOT NULL
);

CREATE TABLE AutomationLock (
    Id            INTEGER PRIMARY KEY CHECK (Id = 1),
    SessionId     TEXT NULL REFERENCES ProcessingSession(Id),
    AcquiredAtUtc TEXT NULL,
    ProcessId     INTEGER NULL,
    MachineName   TEXT NULL
);
INSERT INTO AutomationLock (Id) VALUES (1);
```

`WhiteUnderbaseBranch` is `NOT NULL` with no default — a `PrintOutput` cannot exist without a recorded 0/1/2 px decision.

### 11.3 Immutability triggers (Task 11105)

```sql
CREATE TRIGGER Revision_Immutable_Update
BEFORE UPDATE ON Revision
WHEN  OLD.Sha256           <> NEW.Sha256
   OR OLD.RelativePath     <> NEW.RelativePath
   OR OLD.SourceRevisionId IS NOT NEW.SourceRevisionId
   OR OLD.Operation        <> NEW.Operation
   OR OLD.ByteLength       <> NEW.ByteLength
   OR OLD.CreatedAtUtc     <> NEW.CreatedAtUtc
BEGIN
    SELECT RAISE(ABORT, 'Revision identity columns are immutable');
END;

CREATE TRIGGER ReviewDecision_NoUpdate BEFORE UPDATE ON ReviewDecision
BEGIN SELECT RAISE(ABORT, 'ReviewDecision is append-only'); END;

CREATE TRIGGER ReviewDecision_NoDelete BEFORE DELETE ON ReviewDecision
BEGIN SELECT RAISE(ABORT, 'ReviewDecision is append-only'); END;
```

Only `IsValid`, `InvalidatedAtUtc`, `InvalidationReason`, and `ReviewState` remain updatable on `Revision`. The rule holds even if a future bug bypasses the repository, and it is directly testable.

### 11.4 Repository interfaces (ports in `Workflow`)

```csharp
public interface ISessionRepository
{
    Task<SessionAggregate?> LoadAsync(SessionId id, CancellationToken ct);
    Task<IReadOnlyList<SessionListItem>> ListRecentAsync(int maxCount, DateTimeOffset since, CancellationToken ct);
    Task<OperationResult<Unit>> CommitAsync(SessionMutation mutation, CancellationToken ct); // ONE transaction
    Task<IReadOnlyList<ProcessingAttempt>> FindRunningAttemptsAsync(CancellationToken ct);   // crash recovery
}
```

`SessionMutation` is a batch of row changes produced by `SessionService` from the engine's effects. One command → one transaction. There is no `SaveRevision`-style API that could half-apply a transition.

---

## 12. File workspace design

### 12.1 Decision 11 — layout

Root comes from the preset (`storageAndNamingContract.defaultOutputRoot` = `D:\PrintFlowStudio`), overridable in settings.

```text
D:\PrintFlowStudio\
  Baseline\                       ← Epic 11000 evidence. READ-ONLY to the application. Never written.
  TestData\                       ← fixtures. READ-ONLY to the application.
  Data\
    printflow.db (+ -wal, -shm)
  Logs\                           ← application logs, 30-day retention
  Quarantine\                     ← orphaned files whose metadata commit failed
  Sessions\
    S_20260818T143012Z_a1b2c3d4\  ← S_<UTC compact>_<first 8 chars of session id>
      Source\                     ← InputSnapshot, read-only attribute set
      Working\
        <attemptId>\              ← every attempt gets its own directory
      Approved\
      Rejected\
      Logs\                       ← per-session failure detail and (later) screenshots
```

Decisions and why:

- **The session directory name carries no customer text.** It is created at import, before the operator edits the output name; renaming a session must never require moving a directory, because stored paths would break. Human-readable naming lives in the *file* names inside `Approved\` and in the database.
- **UTC compact timestamp** (`20260818T143012Z`) sorts correctly in Explorer and avoids DST ambiguity.
- **`Working\<attemptId>\` per attempt** makes working-copy name collisions structurally impossible and makes "every retry starts from a clean working copy" (design invariant 8) a property of the layout rather than of remembering to clean up.
- **`Baseline\` and `TestData\` are asserted read-only** by the workspace module: any resolved write path under them is a hard error. The application shares a root with signed evidence, so this guard is not optional.
- **Paths are stored relative to the root** in the database. The root can move (drive letter change, restore from backup) without a data migration. Only the workspace module ever joins root + relative path.

### 12.2 Module interface

```csharp
public interface IWorkspace
{
    OperationResult<SessionWorkspace> CreateSession(SessionId id, DateTimeOffset createdUtc);
    OperationResult<StoredFile>       ImportSource(SessionId id, string sourceAbsolutePath);
    OperationResult<StoredFile>       CreateWorkingCopy(SessionId id, AttemptId attempt, WorkspaceFileRef source);
    OperationResult<ReservedPath>     ReserveOutput(SessionId id, WorkspaceArea area, string proposedFileName);
    OperationResult<StoredFile>       PromoteToApproved(SessionId id, WorkspaceFileRef source, string finalFileName);
    OperationResult<StoredFile>       MoveToRejected(SessionId id, WorkspaceFileRef source);
    OperationResult<Unit>             CleanupWorking(SessionId id);
    OperationResult<Unit>             Quarantine(WorkspaceFileRef orphan, string reason);
    string                            ResolveAbsolute(WorkspaceFileRef reference);  // the ONLY path join in the system
}

public interface IRecycleBin   // separate seam: the only deletion route
{
    OperationResult<Unit> SendToRecycleBin(WorkspaceFileRef file);
}
```

Everything outside the workspace module speaks `WorkspaceFileRef` (a relative-path value object), never `string`. Combined with the banned-symbol analyzer (§5), invariant 14 is enforced at compile time.

`IRecycleBin` is implemented with in-box `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(..., RecycleOption.SendToRecycleBin)` — no package, no P/Invoke. In 11100 it is wired but exercised only by a fake in unit tests plus one opt-in integration test on temporary files.

### 12.3 Path-containment guard

Every path the workspace produces passes:

```text
fullPath = Path.GetFullPath(root + relative)
assert fullPath starts with (root + DirectorySeparator)
assert fullPath is not under Baseline\ or TestData\
assert path length < 240 characters
```

This defends against traversal via operator-entered names (`..\..\`), against writing outside the root, and against Windows path-length failures mid-operation.

### 12.4 Retention

Design §10 rules, implemented as workspace operations invoked by workflow effects:

| Rule | Implementation |
| --- | --- |
| Never overwrite/delete the user's source | The source path is opened read-only exactly once, at import |
| Never auto-delete a valid InputSnapshot | `Source\` is excluded from all cleanup paths |
| Never auto-delete approved PNG/TIFF | `Approved\` is excluded from all cleanup paths |
| Rejected PrintFlow TIFF → Recycle Bin | `IRecycleBin` on final-review rejection |
| Retain rejected Meitu-derived files until session end | `Rejected\` cleared only on `Complete`/`Abandon` |
| Clean `Working\` after completion | `CleanupWorking` effect on `Complete` |

---

## 13. Naming design (Task 11107)

### 13.1 Sanitisation

Applied to the operator-editable output name; the user's source file is never renamed.

1. Strip `< > : " / \ | ? *` and control characters U+0000–U+001F.
2. Collapse runs of whitespace to a single space; trim.
3. Trim trailing dots and spaces (Windows silently drops them).
4. If the stem matches a reserved device name (`CON`, `PRN`, `AUX`, `NUL`, `COM1`–`COM9`, `LPT1`–`LPT9`, case-insensitive), prefix with `_`.
5. Truncate the stem to 80 characters, deterministically (no hashes in the visible name).
6. If empty after all of the above, fall back to `Untitled`.

Non-ASCII characters — including Chinese — are **preserved**. Sanitisation removes what NTFS forbids, not what looks unfamiliar.

### 13.2 Patterns

Read from the preset, never hard-coded:

| Artefact | Preset key | Result |
| --- | --- | --- |
| Enhanced PNG | `enhancedPattern` | `Name_HD.png` |
| Cut-out PNG | `cutoutPattern` | `Name_CUTOUT.png` |
| Production TIFF | `productionTiffPattern` | `Name_280mm_CMYK_W.tif` |
| Collision suffix | `collisionPattern` | `_02`, `_03`, … |

### 13.3 Collision handling

Only `Approved\` and `Rejected\` need collision handling (`Working\` is per-attempt and structurally unique).

Reservation is **atomic**, not check-then-write:

```text
for sequence in [none, 02, 03, … 99]:
    candidate = stem + suffix(sequence) + extension
    try: open candidate with FileMode.CreateNew   → reserved; return
    catch IOException (file exists): continue
fail with FailureCode.WorkspaceError after 99 attempts
```

`File.Exists` followed by a write is a time-of-check/time-of-use race and would violate "never overwrite silently" under antivirus or an unexpected second process. `CreateNew` makes the guarantee atomic at the OS level. The zero-byte reservation is only ever created in `Approved\`/`Rejected\`, where PrintFlow itself performs the copy — external applications only ever write into their own `Working\<attemptId>\` directory, so no third-party "Save As" ever meets a placeholder.

---

## 14. Hash and file metadata design

### 14.1 Decision 12 — service boundary

```csharp
public interface IFileInspector
{
    Task<OperationResult<FileInspection>> InspectAsync(string absolutePath, CancellationToken ct);
    Task<OperationResult<Sha256>>         HashAsync(string absolutePath, CancellationToken ct);
}

public sealed record FileInspection(
    Sha256      Sha256,
    long        ByteLength,
    ImageFormat Format,          // Png, Jpeg, Tiff, Psd, Pdf, Unknown
    int?        PixelWidth,
    int?        PixelHeight,
    double?     DpiX,
    double?     DpiY,
    ColourMode  ColourMode,      // Rgb, Cmyk, Grayscale, Indexed, Unknown
    bool?       HasAlpha,
    int?        ChannelCount);
```

- **One call, one file read.** Hash and metadata come from the same pass, so they cannot describe different bytes.
- **Nullable fields are honest.** For PSD and single-page PDF the inspector returns format, length, and hash with null pixel metadata rather than guessing or throwing. Callers that need dimensions check for null; the type makes that unavoidable.
- **Hashing is the readability proof** (§10.1). There is no second, weaker "can it be opened" check.

### 14.2 Implementation for 11100

- **Hash:** streaming `SHA256` over a `FileStream` with a 1 MB buffer and `FileShare.Read`. Formatted as 64 lowercase hex characters; compared case-insensitively against preset values (which are uppercase).
- **Format:** magic-byte sniffing, independent of any decoder — `\x89PNG`, `\xFF\xD8\xFF`, `II*\0` / `MM\0*`, `8BPS`, `%PDF`. Never trust the file extension.
- **Dimensions / DPI / alpha:** Windows Imaging Component via `System.Windows.Media.Imaging.BitmapDecoder` with `DelayCreation | IgnoreColorProfile`, reading `PixelWidth`, `PixelHeight`, `DpiX`, `DpiY`, and deriving alpha from the decoded `PixelFormat`. Zero extra dependency: the WPF assemblies are already present.

`Infrastructure` therefore sets `<UseWPF>true</UseWPF>` for imaging APIs only. It contains no `Window`, `UserControl`, `Page`, or `App` type; an architecture test asserts that.

### 14.3 Deliberately out of scope

CMYK channel enumeration, spot-channel detection, ICC profile handling, and TIFF tag inspection are **not** built in 11100. They are needed only when a real TIFF must be validated (Epic 11400). At that point a stronger decoder (Magick.NET, Apache-2.0, handles CMYK TIFF, spot channels, and PSD) can be introduced **behind the same `IFileInspector` interface** without touching a single caller. The interface is designed for that substitution; the implementation is not written speculatively.

No colour-management engine is built. Colour correctness belongs to Photoshop and the validated production preset.

---

## 15. Fake adapter seams

### 15.1 Ports (declared in `PrintFlow.Workflow`)

```csharp
public interface IMeituProcessor
{
    string AdapterId { get; }                       // persisted on every attempt
    Task<OperationResult<AdapterOutput>> ProcessAsync(MeituRequest request, CancellationToken ct);
}

public sealed record MeituRequest(
    WorkspaceFileRef Input,
    MeituOperation   Operation,        // Enhance | RemoveBackground
    WorkspaceDirRef  WorkingDirectory,
    WorkspaceFileRef ExpectedOutput);

public interface IPhotoshopOutputProcessor
{
    string AdapterId { get; }
    Task<OperationResult<AdapterOutput>> GenerateAsync(PhotoshopRequest request, CancellationToken ct);
}

public sealed record PhotoshopRequest(
    WorkspaceFileRef     ApprovedInput,
    PrintDimensions      Dimensions,          // mm + px + 300 dpi, proportional, shrink-only
    ProductionPresetRef  Preset,              // preset id + manifest SHA-256
    WhiteUnderbaseBranch Branch,              // REQUIRED — no default
    string               OutputFileName,
    WorkspaceDirRef      WorkingDirectory,
    WorkspaceFileRef     ExpectedOutput);

public sealed record AdapterOutput(WorkspaceFileRef ProducedFile, TimeSpan Elapsed, string? AdapterNotes);
```

What the ports deliberately do **not** expose: window handles, coordinates, keystrokes, PAD flow names, Action names, dialog titles, timeouts, retry policy, screenshots. When Meitu 7.8.7.5 becomes 7.9, or PAD is replaced by UI Automation, these signatures do not change. That is the whole point of the seam, and it is exactly what the baseline's "theme colour and advertising banners must never be recognition signals" warning demands.

`IEnvironmentGate.EnsureReadyAsync(AdapterKind, CancellationToken)` sits in front of every adapter call. In 11100 the implementation is `PermissiveEnvironmentGate` (allows fake adapters, refuses production adapter kinds with `EnvironmentNotVerified`). Epic 11500 replaces it with the real preset/hash/window/dialog checks (with adapter-specific readiness contributed by Epics 11300/11400) without touching the workflow.

### 15.2 Fake adapters (built in 11100)

`PrintFlow.Infrastructure.Adapters.Fake`:

- **They write real files.** `FakeMeituProcessor` copies the input to the expected output path (re-encoding to PNG for `RemoveBackground` so alpha genuinely appears). `FakePhotoshopOutputProcessor` writes a small real TIFF at the requested pixel dimensions. This matters: the full pipeline — existence, stable size, streaming read, hash, metadata extraction, revision creation, review binding — is genuinely exercised, not stubbed.
- **They are scriptable.** A `FakeAdapterScenario` (from `Setting` or a test fixture) selects: `Succeed`, `FailWith(code)`, `Timeout`, `ProduceUnreadableFile`, `ProduceMissingFile`, `HangUntilCancelled`. This is how the failure, retry, and interruption paths are tested without breaking a real application.
- **They are deterministic.** No randomness, no wall-clock dependence (delays come from `TimeProvider`), so tests are reproducible.
- **They are identifiable.** `AdapterId = "fake-meitu-v1"` / `"fake-photoshop-v1"` is written to every `ProcessingAttempt`, so no fake-produced artefact can ever be mistaken for a production one in the database.

Selection is by configuration: `AdapterMode` ∈ {`Fake`, `Production`}, default `Fake` in 11100 (production implementations do not exist yet; the composition root throws at startup if `Production` is requested).

### 15.3 What is not built

No PAD flows, no UI Automation, no screen capture, no window search, no keyboard/mouse injection, no Photoshop scripting, no `.atn` loading. Nothing in Epic 11100 launches, focuses, or reads Meitu, Photoshop, or Maintop.

---

## 16. Minimal WPF shell scope

### 16.1 In scope

| Screen | Contents |
| --- | --- |
| **Home** | Single-file drop target (rejects multi-file drops explicitly); "Recent Processing" list (30 days / 100 sessions); Resume and Abandon actions; preset status banner |
| **Workflow selection** | Three buttons; disabled once a derived revision exists |
| **Session** | Left: step list with live states; right: current-step metadata (path, hash prefix, dimensions, DPI, format); bottom: buttons bound to `SessionView.AvailableCommands`; a `Run (fake adapter)` action for adapter-backed steps |
| **Environment / preset** | Preset id, version, expected vs actual hash, verified/unverified, workspace root, adapter mode |

Behaviour proven by the shell: start → import → choose workflow → run steps through fake adapters → approve/reject/skip/retry → close the application → reopen → resume with identical state.

### 16.2 Explicitly out of scope (later Epics)

Image preview, side-by-side/slider comparison, zoom/pan, checkerboard backgrounds, crop UI, TIFF/white-channel preview, the print-dimensions calculator UI, the error-details screen with screenshots, the diagnostic package exporter, the language switcher.

### 16.3 Localisation posture

Shell strings live in `.resx` from the first commit (neutral English + a `zh-CN` stub). No runtime switcher in 11100. This costs almost nothing now and prevents a later string-extraction refactor across every view. Internal state and error names stay stable English regardless (design §13.4).

---

## 17. Test architecture

One test project, four folders. xUnit + **Shouldly** + `FakeTimeProvider`.

> **Licence note:** FluentAssertions v8+ moved to a paid commercial licence (Xceed). This is a commercial shop, so the plan uses **Shouldly** (BSD-3) instead. If FluentAssertions is preferred for familiarity, it must be pinned to **v7.x (Apache-2.0)**.

### 17.1 Domain / workflow unit tests (no I/O)

| Area | Cases |
| --- | --- |
| Valid transitions | Every arrow in §8.3 and §8.4, for all three workflows |
| Invalid transitions | Approve while Processing; StartStep while Approved; Skip a non-skippable step; SelectWorkflow after a derived revision; Complete with an unfinished step; any command on a Completed session |
| Exhaustiveness | Enumerate every (StepState × CommandKind) pair and assert an explicit outcome — no implicit fall-through |
| Skipping | Enhancement and BackgroundRemoval skip; skip creates no Revision; the downstream input becomes the last approved upstream revision |
| Approval | Approve binds the reviewed hash; approving with a stale hash is refused |
| Rejection | Reject → RetryRequired; the revision is invalidated with reason `Rejected`; the decision persists in history |
| Retry | Retry always starts from a fresh working copy; `RetrySequence` increments; `RetryOfAttemptId` chains |
| Handoff | HandOff from RetryRequired/Failed/Interrupted → session HandedOff; automated flow ends; lock released |
| Interruption | A `Running` attempt at startup → `Interrupted`; the step becomes retryable; no Revision was created |
| Downstream invalidation | `ReturnToStep(Trim)` invalidates every descendant; siblings from `AddAnotherSize` survive |
| Completion | Complete only when all required steps are Approved/Skipped and the terminal artefact is approved |
| Workflow shape | Each of the three definitions has exactly the steps in §7.3–§7.5, in order, with the stated flags |

### 17.2 File integrity tests (temp directories)

- SHA-256 matches a known vector and a known fixture from `TestData\v1`.
- Import never modifies or moves the source (bytes, timestamps, and path asserted unchanged afterwards).
- The snapshot is byte-identical to the source and carries the read-only attribute.
- Working copies are created per attempt and are independent of the snapshot.
- Collision: three artefacts of the same name yield `Name.png`, `Name_02.png`, `Name_03.png`; no file is overwritten.
- Invalid characters, reserved device names, trailing dots, empty-after-sanitisation, over-length stems, and Chinese characters (must survive).
- **File mutation after approval:** approve a revision, mutate one byte, attempt to consume it → `RevisionIntegrityMismatch`, the revision is marked `FileMutated`, and the approval no longer holds.
- Path containment: `..\` in an output name cannot escape the session directory; a write path under `Baseline\` or `TestData\` is refused.

### 17.3 Persistence integration tests (temp SQLite + temp directories)

- Migration from an empty file produces the expected schema; re-running is a no-op; a future `user_version` aborts startup.
- Create session → persist snapshot, revision, attempt, review; reload and compare.
- Restart/resume: dispose everything, reopen from the same file, and assert an identical `WorkflowSnapshot`.
- Transactional consistency: inject a failure mid-commit and assert that neither the step state nor the revision was written (no half-applied transition).
- Crash recovery: leave a `Running` attempt, restart, assert it becomes `Interrupted` and the automation lock is released.
- Invariant enforcement at the DB level: `Attempt(Failed)` with an `OutputRevisionId` is rejected; updating `Revision.Sha256` is rejected; updating or deleting a `ReviewDecision` is rejected.
- Recursive invalidation marks exactly the descendant set and nothing else.

### 17.4 Architecture tests

| Assertion | Mechanism |
| --- | --- |
| `Domain` references no PrintFlow project and no third-party package | `Assembly.GetReferencedAssemblies()` |
| `Workflow` does not reference `Infrastructure`, `Microsoft.Data.Sqlite`, `System.Windows.*` | assembly references |
| `App` types outside `PrintFlow.App.Composition` reference no `Infrastructure`/SQLite type | NetArchTest type rules |
| `Infrastructure` declares no WPF `Window`/`UserControl`/`Page` | type scan |
| No type named `Job`, `Order`, `Customer`, `Asset`, or `Artwork` exists | type scan (scope-creep guard) |
| `System.IO` file APIs and `DateTime.Now` appear only where permitted | `BannedApiAnalyzers` + `BannedSymbols.txt` — **compile-time**, per project |

### 17.5 Not tested

Screen coordinates, click sequences, window titles, pixel-comparison of adapter output, real Meitu/Photoshop behaviour, and physical print results. None of that exists in Epic 11100, and asserting it would create tests that break when the automation is written.

---

## 18. Migration and bootstrap strategy

Startup sequence in `App.OnStartup`, fail-closed at every stage:

1. **Single-instance mutex** — a second instance shows a message and exits.
2. **Configuration** — `appsettings.json` (committed, no secrets) plus optional `appsettings.local.json` (git-ignored, developer overrides).
3. **Root resolution** — resolve the workspace root; create `Data\`, `Sessions\`, `Quarantine\`, `Logs\` if missing; verify write access. Failure → clear error, no session UI.
4. **Database** — open, apply pragmas, apply migrations transactionally. A newer `user_version` aborts.
5. **Preset** — load the signed manifest, verify SHA-256 against `appsettings.json`, deserialise. The result is `Verified` or `Unverified(reason)`; **startup continues either way**, but `IEnvironmentGate` refuses production adapters while unverified, and the shell shows a banner. This lets development proceed off the production workstation while making production automation impossible without a verified preset.
6. **Recovery pass** — `Running` attempts → `Interrupted`; release an automation lock held by a dead process; move orphaned files (present on disk, absent from `Revision`) to `Quarantine\`; log everything.
7. **Compose** the service graph and show Home.

Configuration shape:

```json
{
  "Workspace":  { "Root": "D:\\PrintFlowStudio" },
  "Database":   { "RelativePath": "Data\\printflow.db" },
  "Preset": {
    "Id": "printflow-workstation-v1",
    "Version": "1.0.0",
    "Path": "Baseline\\workstation-v1\\preset\\printflow-workstation-v1.0.0.json",
    "ExpectedSha256": "A114B5D2B1D7BF793001DA13CFA429D84270EA816033C3A851317275918383A6"
  },
  "Adapters":   { "Mode": "Fake" },
  "Logging":    { "RetentionDays": 30 }
}
```

The manifest hash is the only accepted-value fact that appears in the repository. It is a public integrity check, not sensitive content — it contains no path, name, or customer data.

---

## 19. Security, privacy, and file-safety rules

### 19.1 File safety (invariants 1–3)

1. The user's source file is opened exactly once, read-only, at import. It is never written, renamed, moved, or deleted — including on abandon, failure, or crash.
2. Every managed file lives under the workspace root; the path-containment guard (§12.3) makes escaping it a hard error.
3. `Baseline\` and `TestData\` are read-only to the application; a write there is a hard error, not a warning.
4. Deletion happens only through `IRecycleBin` (Recycle Bin, recoverable). There is no hard-delete path in Epic 11100.
5. `Source\` and `Approved\` are excluded from every cleanup routine.
6. `CreateNew` reservation means "never overwrite silently" is an OS guarantee, not a convention.
7. External applications only ever receive paths inside `Working\<attemptId>\`.

### 19.2 Privacy

- Customer images, snapshots, working copies, outputs, screenshots, logs, and the database are local-only. No network sink of any kind is referenced, configured, or permitted in the solution.
- The preset's `workstation.computerName` and `operatorIdentity` are loaded but never logged, exported, or committed.
- Logs record hashes, relative paths, step names, and failure codes — not image content, and never absolute customer source paths in exportable form.
- A diagnostic package is a later Epic and remains manual, confirmed, and local.

### 19.3 Repository hygiene — `.gitignore` plan

Written before the first commit:

```gitignore
# .NET build output
bin/
obj/
*.user
.vs/
artifacts/
TestResults/

# Local configuration and databases
appsettings.local.json
*.db
*.db-wal
*.db-shm

# Production evidence and customer material — must never enter Git
*.atn
*.psd
*.tif
*.tiff
*.png
*.jpg
*.jpeg
*.pdf
signoff/
preset/
evidence/
screenshots/
```

Then narrow re-inclusions for genuinely safe assets, so the deny-by-default posture stays intact:

```gitignore
!src/PrintFlow.App/Assets/*.png
!tests/PrintFlow.Tests/Fixtures/synthetic/*.png
```

Additional rules:

- The signed preset manifest and sign-off JSON stay at `D:\PrintFlowStudio\Baseline\…`. Only the manifest **hash** appears in `appsettings.json`.
- Test fixtures committed to Git are **synthetic** (generated PNG/TIFF), never customer artwork. Real fixtures stay in `D:\PrintFlowStudio\TestData\v1`, referenced by configured path and skipped when absent.
- A pre-commit check (or, at minimum, a review habit) rejects any staged binary over ~200 KB.

### 19.4 Repository reconciliation (must precede the first commit)

1. Recover `PRINTFLOW_STUDIO_MVP_DESIGN.md` (Chinese) from the `C:` copy into the `D:` working tree, after confirming it is the intended Chinese authority.
2. `git init` at `D:\Repositories\printflow-Studio`; add `.gitignore` **first**; then commit the design documents and plans.
3. Move `docs/*.md` into `docs/printflow/` so one documentation convention holds.
4. Archive or delete the `C:\Users\admin\Documents\ChatGPT\Printflow Studio` copy so no one edits the stale tree. It has zero commits, so nothing is lost.
5. Decide the remote question (§22) before pushing anything — the repository currently has no remote at all.

---

## 20. Implementation sequence for 11101–11108

### 20.1 Challenge to the proposed order

The brief's hypothesis is sound but has two ordering problems that repository evidence exposes:

1. **Version control is missing entirely.** Creating the whole source tree with no VCS and no ignore rules risks both losing work and committing evidence later. `git init` + `.gitignore` must be step 0, before any code.
2. **Persistence is listed before workspace and hashing.** But a `Revision` row cannot be written without a hash, metadata, and a workspace-relative path. Building persistence first guarantees schema churn. Hashing and workspace are dependency-free leaves; they should come first, and persistence should store their finished output shapes.

### 20.2 Revised sequence

| Order | Task | Work | Exit criterion |
| --- | --- | --- | --- |
| 0 | **pre** | Repository reconciliation (§19.4): recover the Chinese doc, `git init`, `.gitignore`, doc move, archive the stale copy | Clean repo, one authority, nothing sensitive tracked |
| 1 | **11100.0** | Preset verification (§3.2) + `appsettings.json` shape + `IWorkstationPresetProvider` + synthetic fixture preset | Hash verified in code; typed preset loads |
| 2 | **11101** | Solution, 5 projects, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `nuget.config`, banned-symbol files, empty test project, local build script | `dotnet build` and `dotnet test` both succeed on an empty suite |
| 3 | **11102** | Domain types: entities, value objects, enums, `OperationResult`, `OperationFailure` | Domain compiles with zero package references; architecture test passes |
| 4 | **11103** | Three `WorkflowDefinition`s + `WorkflowCatalog` | Workflow-shape tests pass for all three |
| 5 | **11104** | `IWorkflowEngine` reducer, transition table, commands, effects | Full transition matrix green, including the exhaustiveness test |
| 6 | **11106a** | `IFileInspector` (hash, format, dimensions, DPI, alpha) | File-integrity tests pass against `TestData\v1` |
| 7 | **11106b** | `IWorkspace` + `IRecycleBin`: session dirs, snapshot, working copies, areas, containment guard | Source-preservation and containment tests pass |
| 8 | **11107** | Naming: sanitiser, preset patterns, atomic collision reservation | Naming tests pass, including Chinese and reserved names |
| 9 | **11108** | SQLite: schema, triggers, migrations, Dapper repositories, `SessionMutation` transactions | Persistence integration tests pass, including DB-level invariant tests |
| 10 | **11105** | Immutability + hash-bound approval end to end: `RevisionIntegrityGuard`, invalidation cascade | Mutation-after-approval and cascade tests pass |
| 11 | **glue** | `SessionService`: effect interpretation, transaction ordering (§10.5), automation lock | One command → one transaction, proven by an injected mid-commit failure |
| 12 | **fakes** | Fake Meitu/Photoshop adapters + scenarios + `IEnvironmentGate` | Failure/timeout/interruption paths covered |
| 13 | **shell** | Minimal WPF shell (§16), composition root, `.resx` | Manual walkthrough: import → run → approve → close → reopen → resume |
| 14 | **recovery** | Startup recovery: interrupted attempts, stale lock, orphan quarantine | Kill the process mid-attempt; restart yields `Interrupted` and a clean lock |
| 15 | **review** | Full suite + architecture review against §23 | All boxes ticked |

Tasks 11105 and 11106 are split because their two halves have different dependencies; the Jira task IDs still map cleanly (11105 → row 10, 11106 → rows 6–7).

---

## 21. Expected files and projects to be created

```text
.gitignore                                   .editorconfig
PrintFlowStudio.sln                          Directory.Build.props
Directory.Packages.props                     nuget.config
appsettings.json                             README.md

docs/printflow/
  phase-11100-core-desktop-workflow-foundation-plan.md   (this document)
  epic-11000-production-environment-baseline-final-report.md   (moved)
  phase-11000-production-environment-baseline-plan.md          (moved)

src/PrintFlow.Domain/
  Sessions/{ProcessingSession,InputSnapshot,SessionStep}.cs
  Revisions/{Revision,RevisionOperation,InvalidationReason}.cs
  Attempts/{ProcessingAttempt,AttemptStatus}.cs
  Reviews/{ReviewDecision,RejectionReason}.cs
  Outputs/{PrintOutput,PrintDimensions,SizePreset,WhiteUnderbaseBranch}.cs
  Files/{WorkspaceFileRef,WorkspaceDirRef,Sha256,OutputName,ImageFormat,ColourMode,FileInspection}.cs
  Results/{OperationResult,OperationFailure,FailureCode,Unit}.cs
  Ids/{SessionId,RevisionId,AttemptId,ReviewId,PrintOutputId}.cs
  BannedSymbols.txt

src/PrintFlow.Workflow/
  Definitions/{WorkflowType,StepKind,StepDefinition,WorkflowDefinition,WorkflowCatalog}.cs
  Engine/{IWorkflowEngine,WorkflowEngine,TransitionTable,WorkflowSnapshot,StepState,SessionState}.cs
  Commands/{WorkflowCommand,CommandContext,CommandRejection,RejectionCode}.cs
  Effects/{WorkflowEffect,WorkflowTransition}.cs
  Services/{ISessionService,SessionService,SessionView,SessionMutation,RevisionIntegrityGuard}.cs
  Ports/{ISessionRepository,IWorkspace,IRecycleBin,IFileInspector,IMeituProcessor,
         IPhotoshopOutputProcessor,IWorkstationPresetProvider,IEnvironmentGate,IIdGenerator}.cs
  BannedSymbols.txt

src/PrintFlow.Infrastructure/
  Sqlite/{SqliteConnectionFactory,MigrationRunner,SessionRepository,Mappers}.cs
  Sqlite/Migrations/0001_initial_schema.sql
  Workspace/{FileWorkspace,WorkspacePaths,PathGuard,FileNameSanitiser,NamingService,RecycleBin}.cs
  Imaging/{WicFileInspector,FormatSniffer,Sha256Hasher}.cs
  Preset/{WorkstationPresetProvider,WorkstationPreset,PresetVerification}.cs
  Adapters/Fake/{FakeMeituProcessor,FakePhotoshopOutputProcessor,FakeAdapterScenario}.cs
  Environment/PermissiveEnvironmentGate.cs

src/PrintFlow.App/
  App.xaml(.cs)                MainWindow.xaml(.cs)
  Composition/{ServiceRegistration,StartupSequence,SingleInstanceGuard}.cs
  Views/{HomeView,WorkflowSelectionView,SessionView,PresetStatusView}.xaml(.cs)
  ViewModels/{HomeViewModel,WorkflowSelectionViewModel,SessionViewModel,PresetStatusViewModel}.cs
  Resources/{Strings.resx,Strings.zh-CN.resx}
  BannedSymbols.txt

tests/PrintFlow.Tests/
  Unit/Workflow/{TransitionTableTests,SkipTests,ApprovalTests,RetryTests,
                 HandoffTests,InvalidationTests,CompletionTests,WorkflowShapeTests}.cs
  Unit/Naming/{SanitiserTests,CollisionTests}.cs
  Integration/Files/{HashTests,SnapshotTests,WorkingCopyTests,MutationAfterApprovalTests,PathGuardTests}.cs
  Integration/Persistence/{MigrationTests,SessionPersistenceTests,TransactionTests,
                           ResumeTests,DbInvariantTests,RecoveryTests}.cs
  Architecture/{DependencyRuleTests,ScopeGuardTests}.cs
  Fixtures/{TempWorkspace,TempDatabase,SyntheticImages,FakePreset.json}
```

Approximately 90 source files and 5 projects. No file in this tree launches, reads, or modifies Meitu, Photoshop, or Maintop.

---

## 22. Risks and open questions

### 22.1 Risks

| # | Risk | Severity | Mitigation |
| --- | --- | --- | --- |
| R1 | **No version control.** The entire Epic would be written with no undo and no review history | **High** | `git init` + `.gitignore` as step 0 (§19.4) |
| R2 | **Two divergent project copies.** Someone edits the stale `C:` tree | **High** | Reconcile and archive before the first commit |
| R3 | **Chinese design document absent** from the authoritative copy | Medium | Recover from the `C:` copy; confirm it is the intended authority |
| R4 | **Evidence leaking into Git** once the tree contains code and fixtures | Medium | Deny-by-default `.gitignore`; synthetic fixtures only; binary-size check |
| R5 | **.NET 8 support ends November 2026** | Medium | Target .NET 10 LTS; requires an SDK install decision |
| R6 | Assertion-library licence trap (FluentAssertions v8+ is commercial) | Medium | Use Shouldly, or pin FluentAssertions 7.x |
| R7 | The app writes into the same root as signed Epic 11000 evidence | Medium | `Baseline\` and `TestData\` are read-only to the application, asserted in code and tested |
| R8 | Antivirus locks or delays newly written files, causing false validation failures | Medium | Retry-with-backoff on transient IO in the workspace; "stable size across two reads" before hashing |
| R9 | Windows path-length limits with long Chinese output names | Low | 80-character stem cap + 240-character total guard |
| R10 | WAL sidecar files on `D:` complicate backup/copy of the database | Low | Documented; `wal_checkpoint(TRUNCATE)` on clean shutdown |
| R11 | The Recycle Bin may be disabled or unavailable on `D:` | Low | `IRecycleBin` returns a structured failure; the file stays in `Rejected\` rather than being hard-deleted |
| R12 | Modelling reviews as step phases diverges from the brief's node diagram | Low | Documented in §7.1 with an explicit node→step mapping; the UI still shows per-review screens |
| R13 | Fake adapters give false confidence about real automation timing | Low | Scenarios include timeout/hang/interrupt; no timing claims are made until Epic 11300 |
| R14 | NuGet restore needs network access on a deliberately controlled workstation | Low | `packages.lock.json` + pinned `nuget.config`; restore once, then build offline |

### 22.2 Open questions requiring an operator decision

| # | Question | Default if no answer |
| --- | --- | --- |
| Q1 | Install the **.NET 10 SDK** on the workstation, or start on .NET 8? | Start on .NET 8, move to .NET 10 before the MVP ships (one property change) |
| Q2 | Should the repository have a **remote** (private GitHub/Azure DevOps), or stay local-only? This determines how strict the ignore rules must be | Local-only until a decision is made; ignore rules written as if a remote exists |
| Q3 | Is `PRINTFLOW_STUDIO_MVP_DESIGN.md` (Chinese, `C:` copy) still an authority, or is the English document now the single source? | Recover it, mark it "reference translation; English is authoritative" |
| Q4 | Confirm `D:\PrintFlowStudio\Data\printflow.db` as the database location (vs `%LOCALAPPDATA%`) | Use `D:\PrintFlowStudio\Data\` |
| Q5 | Confirm the session directory convention `S_<UTC>_<shortid>` with no customer text in the folder name | Proceed as specified |
| Q6 | Confirm that Epic 11100 need **not** deliver the trim implementation (design §16.2 lists a Trimming Module; the brief's 11100 scope does not) | Trim is a step *definition* in 11100 with a fake adapter; the real trim implementation belongs to **Epic 11200** (corrected — see §1.3) |
| Q7 | Move `docs/*.md` into `docs/printflow/`? | Yes, in the first commit |

Q6 is the only genuine scope ambiguity in the brief. The plan assumes trim is defined but not implemented in 11100.

---

## 23. Definition of Done for Epic 11100

Epic 11100 is complete when every box below is true.

**Repository and process**

- [ ] `D:\Repositories\printflow-Studio` is a git repository with a meaningful first commit.
- [ ] `.gitignore` denies binaries, databases, evidence, and local configuration by default.
- [ ] No customer image, screenshot, `.atn`, TIFF, preference artifact, Maintop configuration, or sign-off JSON is tracked.
- [ ] The stale `C:` copy is archived or removed; the Chinese design document is recovered.
- [ ] All documentation lives under `docs/printflow/`.

**Solution**

- [ ] `dotnet build` succeeds with zero warnings (`TreatWarningsAsErrors`).
- [ ] Five projects exist with the dependency directions in §5, proven by architecture tests.
- [ ] `Domain` has no third-party package reference.
- [ ] Banned-symbol analyzers prevent `System.IO` file APIs and `DateTime.Now` outside their permitted homes.

**Domain and workflow (11102, 11103, 11104)**

- [ ] All six modelled concepts exist as immutable types; no `Job`/`Order`/`Customer`/`Asset`/`Artwork` type exists.
- [ ] The three workflow definitions match §7.3–§7.5 exactly.
- [ ] The workflow engine is pure; every (state × command) pair has an explicit outcome; arbitrary state assignment is impossible from outside.

**Revision, attempt, approval (11105)**

- [ ] A failed, timed-out, or interrupted attempt provably creates no Revision — enforced by a DB CHECK as well as by code.
- [ ] A Revision exists only after successful creation, full readability, metadata extraction, and SHA-256.
- [ ] Revision identity columns and `ReviewDecision` rows are immutable, enforced by triggers.
- [ ] Every approval stores the reviewed hash; mutating an approved file invalidates the approval, demonstrated by a passing test.
- [ ] `ReturnToStep` invalidates the full descendant set and no siblings.

**Workspace and naming (11106, 11107)**

- [ ] Source files are provably never modified, moved, or deleted.
- [ ] Every import produces a read-only snapshot inside the session workspace.
- [ ] All paths are produced by the workspace module; no other module joins a path.
- [ ] `Baseline\` and `TestData\` are unwritable by the application.
- [ ] Collision handling yields `_02`/`_03` atomically and never overwrites.
- [ ] Invalid characters, reserved names, and over-length names are handled; Chinese characters survive.

**Persistence (11108)**

- [ ] Migrations apply transactionally from empty and are idempotent; a newer schema aborts startup.
- [ ] One command commits in exactly one transaction; an injected mid-commit failure leaves no partial state.
- [ ] SQLite stores metadata only; no image bytes in any column.
- [ ] Close and reopen restores an identical workflow snapshot.
- [ ] Interrupted attempts and a stale automation lock are recovered at startup.

**Shell and adapters**

- [ ] The shell supports: start → import (single file only) → choose workflow → inspect state → run fake steps → approve/reject/skip/retry → close → reopen → resume.
- [ ] The UI mutates no state directly, executes no SQL, and references no adapter.
- [ ] Fake adapters write real files and can be scripted to succeed, fail, time out, hang, or produce unreadable output.
- [ ] The preset is loaded from the signed manifest and hash-verified; production adapters are refused while unverified.
- [ ] Nothing in the solution launches, reads, or modifies Meitu, Photoshop, or Maintop.

**Testing**

- [ ] All tests green locally; the suite runs without network access, without the production workstation, and without touching `D:\PrintFlowStudio\Baseline`.
- [ ] Every category in §17.1–§17.4 has passing coverage.
- [ ] No test asserts screen coordinates or click sequences.

---

## Recommended Implementation Verdict

**READY WITH NOTES**

The design authority, the environment baseline, and the signed workstation preset are all in place and independently re-verified. The technical plan is complete and every required decision is made. Epic 11100 can begin as soon as the repository-hygiene items below are settled — none of them is a design question, and none requires reopening Epic 11000.

### Operator input required before implementation starts

| # | Required from the operator | Blocking? |
| --- | --- | --- |
| 1 | **Approve `git init`** at `D:\Repositories\printflow-Studio` with the §19.3 `.gitignore`, plus the decision to archive the stale `C:\Users\admin\Documents\ChatGPT\Printflow Studio` copy | **Yes** — writing ~90 files with no version control is not acceptable |
| 2 | **Confirm the Chinese design document's status** and approve recovering `PRINTFLOW_STUDIO_MVP_DESIGN.md` into the authoritative tree | **Yes** — it is the only copy, in a tree slated for archival |
| 3 | **Decide .NET 10 vs .NET 8** (Q1). .NET 10 requires an SDK install on the workstation | No — .NET 8 is a safe start; the change is one property |
| 4 | **Decide the git remote question** (Q2): private remote or local-only | No — local-only until decided |
| 5 | **Confirm** the database location, session directory convention, and the doc move (Q4, Q5, Q7) | No — the stated defaults apply |
| 6 | **Confirm the trim scope reading** (Q6): defined in 11100, implemented in Epic 11200 | No — the stated default applies |

### Correction to the task brief

The brief lists "consolidate the accepted hashes/settings into one immutable workstation preset manifest and obtain final preset sign-off" as an outstanding Epic 11000 item. It is already complete: the manifest and sign-off exist, are marked read-only, and all three referenced hashes were recomputed and matched during this inspection (§1.1). The pre-implementation task has been reduced accordingly to verification and wire-in (§3.2). No Epic 11000 discovery was rerun.

### Confirmation of plan-only status

No code was written. No solution or project was created. No file under `D:\PrintFlowStudio` was created, modified, or deleted. Meitu, Photoshop, Maintop, the production Actions, colour settings, and customer files were not touched. Nothing was committed or pushed — the working directory is still not a git repository.
