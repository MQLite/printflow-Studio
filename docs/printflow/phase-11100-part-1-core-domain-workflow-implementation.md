# PrintFlow Studio — Phase 11100 Part 1: Core Domain and Workflow Implementation

| Item | Value |
| --- | --- |
| Document | Epic 11100 Part 1 implementation report |
| Report date | 18 August 2026 |
| Scope delivered | Repository bootstrap, Jira 11101, 11102, 11103, 11104 |
| Scope **not** delivered | Jira 11105–11108 (see §14) |
| Design authority | PrintFlow Studio MVP Design Document v1.0 (Confirmed, English) |
| Environment authority | Epic 11000 final report; signed preset `printflow-workstation-v1` `1.0.0` |
| Repository | `D:\Repositories\printflow-Studio` — local-only, no remote |
| Branch | `master` (Git installation default; not renamed) |

---

## 1. Executive summary

The first coding slice of PrintFlow Studio is complete. The repository is under version
control with a privacy-first ignore posture, the .NET 10 solution exists with its five
project boundaries enforced by tests, and the deterministic core of the product — the domain
model, the three fixed workflows, and the pure workflow engine — is implemented and covered
by 5,333 passing tests.

What now works end to end, entirely in memory:

- a session can be created against any of the three fixed workflows and driven through every
  legal command to completion;
- every illegal command is refused with a stable code, changes nothing, and produces no
  effects — asserted exhaustively across every workflow, step, state and command;
- approvals bind to the hash of the result actually reviewed, and a stale hash is refused;
- Photoshop output cannot begin without an explicitly chosen W1 branch, and nothing infers one;
- the engine is pure: no clock, no disk, no identifiers, no adapters, no mutable state.

Nothing in this slice touches Meitu, Photoshop or Maintop, and nothing under
`D:\PrintFlowStudio\Baseline` was created, modified or deleted. All three Epic 11000 hashes
were re-verified read-only at the start and end of the work and still match.

Two defects were found and fixed during the work, both by verification rather than by review
(§13). One of them — `.gitignore` rules silently excluding two source directories — would
have produced a repository that built locally and was broken for everyone else.

**Verdict: `PART 1 PASS — READY FOR 11105–11108`** (§16).

---

## 2. Repository reconciliation performed

### 2.1 Inspection before any change

| Item | Finding | Matches approved Plan |
| --- | --- | --- |
| `D:\Repositories\printflow-Studio` | No `.git`, no `.gitignore`, no solution. 4 Markdown files, no binaries. | Yes (plan §2.1) |
| `C:\Users\admin\Documents\ChatGPT\Printflow Studio` | Git repo, branch `master`, **zero commits**, everything untracked, no remote. 30 files / 166,430 bytes. | Yes (plan §2.2) |
| Chinese design document | Present only in the stale `C:` tree | Yes (plan §1.1 finding 3) |
| `D:\PrintFlowStudio` baseline | 66 files; preset manifest and sign-off read-only | Yes (plan §2.4) |
| Epic 11000 hashes | All three recomputed and matched | Yes (plan §1.1 finding 4) |
| .NET SDK | `8.0.418` only — no .NET 10 | Yes (plan §2.5) |
| Git | `2.53.0.windows.1` | Yes |

No material difference from the approved Plan was found, so implementation proceeded.

### 2.2 Chinese design reference recovered

`PRINTFLOW_STUDIO_MVP_DESIGN.md` (27,268 B) was copied from the stale tree into the
authoritative repository and annotated immediately under its title, in both languages:

> **Reference translation.**
> `PRINTFLOW_STUDIO_MVP_DESIGN_EN.md` is the implementation authority if the two documents differ.
>
> **参考译本。** 若两份文档存在差异，以 `PRINTFLOW_STUDIO_MVP_DESIGN_EN.md`（英文确认版）为实现依据。

No substantive translated content was altered — the file grew from 27,268 to 27,530 bytes,
which is exactly the annotation.

The stale English design (32,559 B, older) and the stale half-length 11000 plan (39,549 B)
were **not** copied over the authoritative versions (35,081 B and 75,451 B respectively).

### 2.3 Stale tree archived

`C:\Users\admin\Documents\ChatGPT\Printflow Studio`
→ `C:\Users\admin\Documents\ChatGPT\Printflow Studio_ARCHIVE_20260818`

Contents preserved and verified: **30 files / 166,430 bytes**, byte-identical to the
pre-move inventory, including the `.git` directory with its `refs/codex` entry.

A directory-level `Move-Item` was refused because another process held a handle on the
folder, so the archive was produced by moving each child and copying the locked `.git`
directory, then removing the emptied original. Post-move verification confirms the archive
is complete and the original path no longer exists. Its git metadata was **not** merged into
the authoritative repository; the new repository starts from an empty history.

### 2.4 Documentation reconciled

All PrintFlow planning and report documents now live under `docs/printflow/`:

```text
docs/printflow/
  epic-11000-production-environment-baseline-final-report.md   (moved from docs/)
  phase-11000-production-environment-baseline-plan.md          (moved from docs/)
  phase-11100-core-desktop-workflow-foundation-plan.md
  phase-11100-part-1-core-domain-workflow-implementation.md    (this report)
```

No substantive evidence was altered by the move.

### 2.5 Epic boundary correction applied to the Plan

Plan v1.0 stated in several places that the real trimming implementation belongs to Epic
11300. That is incorrect. A new **§1.3 Epic boundary correction** records the confirmed map
(11100 foundation, 11200 review/trimming, 11300 Meitu, 11400 Photoshop, 11500 environment),
and every misattributed reference was corrected:

| Location | Was | Now |
| --- | --- | --- |
| §1 executive summary | "screen automation, Epics 11200/11300/11400" | "Epics 11300/11400; deterministic trimming and image review, Epic 11200" |
| §7.3 PREPARE_ASSET table | Trim adapter "internal (Epic 11300)" | "internal (Epic 11200)" |
| §9.3 `OperationFailure` | "screenshots from Epic 11200" | "from Epics 11300/11400" |
| §15.1 environment gate | "Epics 11200/11300 replace it" | "Epic 11500 replaces it, with adapter readiness from 11300/11400" |
| §22.1 risk R13 | "no timing claims until Epic 11200" | "until Epic 11300" |
| §22.2 Q6 and verdict item 6 | "trim belongs to Epic 11300" | "belongs to **Epic 11200** (corrected — see §1.3)" |

No other part of the approved Plan was rewritten.

---

## 3. Git initialisation and commit history

`git init` was run in `D:\Repositories\printflow-Studio`. The branch is `master` — the Git
installation default, deliberately not renamed. **No remote is configured and nothing was
pushed.** `git remote -v` returns nothing.

`.gitignore` was written **before** the first `git add`.

### 3.1 Commits

| # | SHA | Subject | Files |
| --- | --- | --- | --- |
| 1 | `c8733ebdfe140576d5d699fd1f3049b21ffcc75c` | Bootstrap repository: authority docs, privacy-first ignore rules | 8 |
| 2 | `0a35824c924cb6af778af6eefad75bcc2d809b85` | 11101: .NET 10 solution, five projects, build and package configuration | 26 |
| 3 | `9be2e23ef33f224a09ab3f35a0d5979dbac6c3c6` | 11102: core domain model, typed identifiers, value objects and results | 23 |
| 4 | `09d07e4665bc7d220f575b7060e01de7b0abce8f` | 11103 and 11104: fixed workflow definitions and the pure workflow engine | 27 |
| 5 | *(assigned on commit)* | Report: Epic 11100 Part 1 implementation — this document | 1 |

Each code commit was verified in isolation before being made: the index was exported to a scratch
directory and `dotnet build` plus `dotnet test` were run against that exported tree, so every
commit in this history compiles and its tests pass on its own.

Commit 1 was amended once, before any later commit existed, to carry the corrected
`.gitignore` described in §13.1.

The commit messages were subsequently rewritten to drop a `Co-Authored-By` trailer, at the
operator's request. Only message text changed — every tree is byte-identical — but the
rewrite reassigned all SHAs, and the values above are the post-rewrite ones. The rewrite was
safe because the repository is local-only and nothing had been pushed.

---

## 4. .NET SDK selected

**.NET 10 LTS — SDK `10.0.400`, runtime `10.0.11`.** Target framework `net10.0` for the two
portable projects and `net10.0-windows` for Infrastructure, App and Tests.

Only `8.0.418` was installed, so the SDK had to be added. It was installed using Microsoft's
official `dotnet-install.ps1` from `https://dot.net/v1/dotnet-install.ps1`:

```text
C:\Users\admin\AppData\Local\Microsoft\dotnet
  sdk       10.0.400
  runtimes  Microsoft.NETCore.App 10.0.11
            Microsoft.WindowsDesktop.App 10.0.11
            Microsoft.AspNetCore.App 10.0.11
```

This is a **per-user** install because the session has no administrator rights and a
machine-wide MSI requires a UAC prompt that a non-interactive session cannot answer. The
existing machine-wide .NET 8 was left completely untouched. Two user-scope environment
variables were set so the SDK is discoverable: `DOTNET_ROOT`, and the install directory
prepended to the user `Path`.

Nothing else was installed or changed. Meitu, Photoshop, Maintop, the production Actions,
Photoshop colour settings and Windows display settings were not touched.

`global.json` pins the repository:

```json
{ "sdk": { "version": "10.0.400", "rollForward": "latestFeature" } }
```

### 4.1 Known consequence of the per-user install — operator action recommended

The per-user install has two consequences, both environment/tooling items rather than defects
in the solution. Neither affects the code, and both disappear once the runtime and SDK are
installed machine-wide.

**1. A plain `dotnet` command does not find the .NET 10 SDK.** Windows composes a process
`PATH` as machine entries **then** user entries, and `C:\Program Files\dotnet` is on the
machine `PATH`. A user-scope entry can therefore never precede it. In a fresh terminal,
`dotnet` resolves to the machine-wide .NET 8 muxer, which cannot satisfy the `global.json`
pin to SDK 10.0.400 and fails with *"A compatible .NET SDK was not found."*

Until the SDK is installed machine-wide, invoke the .NET 10 muxer by full path:

```powershell
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
& "$env:DOTNET_ROOT\dotnet.exe" build   PrintFlowStudio.sln
& "$env:DOTNET_ROOT\dotnet.exe" test    PrintFlowStudio.sln
& "$env:DOTNET_ROOT\dotnet.exe" run --project src\PrintFlow.App
```

Verified: `run` starts the application and the window opens titled **PrintFlow Studio**.

**2. Double-clicking `PrintFlow.App.exe` shows the Windows "download .NET" prompt,** because
the executable's launcher looks for a machine-wide .NET 10 Desktop Runtime and finds only
.NET 8. Unlike case 1 this one *is* fixed by the `DOTNET_ROOT` user variable, which the
launcher honours — but only in processes started after it was set, so Explorer needs a sign-out
and back in to inherit it.

| Option | Needs admin | Fixes case 1 | Fixes case 2 |
| --- | --- | --- | --- |
| Use the full muxer path, as above | No | Yes | n/a — run from a shell instead |
| Sign out and back in | No | **No** — machine `PATH` still wins | Yes |
| `winget install Microsoft.DotNet.SDK.10` from an elevated prompt | Yes | Yes | Yes |

The last row is the recommended end state: it installs both the SDK and the Desktop Runtime
machine-wide, after which plain `dotnet` commands and double-click launching both work with
no environment variable at all.

---

## 5. Jira 11101 — solution and module boundaries

```text
PrintFlowStudio.sln           classic .sln format (the .NET 10 default is .slnx)
Directory.Build.props         TFM policy, nullable, implicit usings, deterministic,
                              TreatWarningsAsErrors, central package management, lock files
Directory.Packages.props      central package versions
nuget.config                  nuget.org only, with package source mapping
.editorconfig                 style and analyzer severities
global.json                   SDK 10.0.400, rollForward latestFeature
src/
  PrintFlow.Domain/           net10.0
  PrintFlow.Workflow/         net10.0
  PrintFlow.Infrastructure/   net10.0-windows
  PrintFlow.App/              net10.0-windows, WPF
tests/
  PrintFlow.Tests/            net10.0-windows
```

Exactly five projects, with the responsibilities and exclusions from plan §4.2. Reference
directions are asserted by tests (§10), not merely intended.

### 5.1 Package set

| Package | Version | Used by | Justification |
| --- | --- | --- | --- |
| CommunityToolkit.Mvvm | 8.4.2 | App | Plan decision 3 |
| Microsoft.Extensions.DependencyInjection | 10.0.11 | App | Composition root |
| Microsoft.Extensions.Logging.Abstractions | 10.0.11 | App | Plan §4.3 |
| Microsoft.NET.Test.Sdk | 18.9.0 | Tests | Test host |
| xunit | 2.9.3 | Tests | Plan §17 |
| xunit.runner.visualstudio | 3.1.5 | Tests | Test discovery |
| Shouldly | 4.3.0 | Tests | Plan §17 licence note — **not** FluentAssertions v8+ |
| Microsoft.Extensions.TimeProvider.Testing | 10.9.0 | Tests | `FakeTimeProvider`, plan §6.7 |

`PrintFlow.Domain` declares **no** `PackageReference` and **no** `ProjectReference` at all.

None of EF Core, ImageSharp, FluentAssertions v8+, Selenium, Playwright, a state-machine
framework, or any Meitu/Photoshop automation library is referenced — asserted by a test.

Versions are managed centrally and locked with `packages.lock.json` in every project;
`dotnet restore --locked-mode` succeeds, so restore is reproducible on a controlled
workstation.

### 5.2 Minimal WPF shell

`App.xaml.cs` builds the service provider, resolves `ShellViewModel`, and shows
`MainWindow`. The window lists the three fixed workflows with their steps and flags, drawn
from `WorkflowCatalog` through `PrintFlow.Workflow` only, and displays a notice that this
build proves composition rather than function.

Nothing from plan §16.2 was built: no image preview, drag/drop, crop UI, slider comparison,
checkerboard, TIFF review, recent processing, full navigation, environment checker, or error
screenshot UI.

Operator-visible strings live in `Resources/Strings.resx` with a `zh-CN` satellite from the
first commit, so the Chinese-first localisation later is a translation job rather than a
string-extraction refactor across every view. Internal state names, failure codes and
adapter identifiers stay stable English (design §13.4).

---

## 6. Jira 11102 — domain model

All types are immutable `sealed record` or `readonly record struct` in `PrintFlow.Domain`.

| Concept | Type | Notes |
| --- | --- | --- |
| ProcessingSession | `Sessions/ProcessingSession.cs` | Workflow type, output name, current step, session state, workspace ref, created/updated/completed, handoff and abandon facts |
| InputSnapshot | `Sessions/InputSnapshot.cs` | Provenance only; bytes live in the root Revision. No file copying |
| SessionStep | `Sessions/SessionStep.cs` | One per step; carries the current Revision **and its hash** |
| Revision | `Revisions/Revision.cs` | Source chain, operation, file ref, facts, SHA-256, validity, invalidation, cached review state |
| ProcessingAttempt | `Attempts/ProcessingAttempt.cs` | Output Revision attachable only on success; retry chain and sequence |
| ReviewDecision | `Reviews/ReviewDecision.cs` | Append-only, hash-bound, operator, quick reason, notes |
| PrintOutput | `Outputs/PrintOutput.cs` | Minimal and forward-compatible for Epic 11400 |

No `Job`, `Order`, `Customer`, `Asset`, `Artwork`, `User`, `Role`, `Permission`,
`ProductionQueue` or `Batch` type exists anywhere — asserted by a test.

### 6.1 Value objects

`SessionId`, `RevisionId`, `AttemptId`, `ReviewId`, `PrintOutputId`, `SnapshotId`, `Sha256`,
`OutputName`, `WorkspaceFileRef`, `WorkspaceDirRef`, `PrintDimensions`, `ProductionPresetRef`,
plus `FileFacts`, `WorkspaceArea`, `ImageFormat`, `ColourMode`.

Each earns its place by protecting a boundary rather than by style: `Sha256` normalises case
so a comparison cannot silently fail; workspace references refuse rooted paths and `..`
traversal; `OutputName` refuses characters Windows forbids while preserving Chinese
characters; typed identifiers make a `RevisionId`/`SessionId` mix-up a compile error.

### 6.2 W1 branch

`WhiteUnderbaseBranch` offers exactly `W1_0px`, `W1_1px`, `W1_2px`. There is **no default and
no member usable as one**, and `PhotoshopRequest.Branch` is non-nullable, so a production
TIFF cannot even be requested without the decision having been made. Nothing infers fine
detail, ordinary design or solid rectangle from image content.

### 6.3 IDs and time

UUIDv7 via the in-box `Guid.CreateVersion7()` on .NET 10, behind `IIdGenerator` purely so
tests are deterministic. All instants are UTC `DateTimeOffset` supplied through
`TimeProvider` or `CommandContext`. No `DateTime.Now` appears anywhere in the solution.

### 6.4 Results and failures

`OperationResult<T>`, `OperationFailure`, `Unit`, and `FailureCode` with exactly the thirteen
planned values. Hand-written, roughly 100 lines total; no functional-programming package.
A test asserts the failure-code names, because they are persisted and renaming one would
silently break stored history and recovery routing.

---

## 7. Jira 11103 — the three fixed workflow definitions

Static code in `WorkflowCatalog`. No configuration file, no database rows, no designer.

### PREPARE_ASSET

| # | Step | Skippable | Review | Produces | Adapter |
| --- | --- | --- | --- | --- | --- |
| 1 | Import | no | no | Revision (root) | — |
| 2 | OriginalConfirmation | no | no | — | — |
| 3 | Enhancement | **yes** | yes | Revision | Meitu |
| 4 | BackgroundRemoval | **yes** | yes | Revision | Meitu |
| 5 | Trim | no | yes | Revision | internal |
| 6 | ApprovedPngExport | no | no | Revision (promote) | — |

No print dimensions, no TIFF, no Photoshop adapter anywhere in the definition.

### PREPARE_CUSTOMER_DESIGN

Import → OriginalConfirmation → Enhancement (skippable) → BackgroundRemoval (skippable) →
Trim → PrintDimensions → PhotoshopOutput. Enhancement always precedes background removal.

### GENERATE_PRINT_TIFF

Import → OriginalConfirmation (**review required** — design readiness) → PrintDimensions →
PhotoshopOutput. No Meitu step and no automatic trim exist in the definition.

### 7.1 Reviews are phases, not steps

A review is the `ReviewRequired` phase of the step that produced the result. No
`EnhancementReview`, `BackgroundRemovalReview`, `TrimReview`, `TiffValidation` or
`FinalReview` step kind exists, and a test asserts none is added later. Validation is the
gate that decides whether an attempt yields a valid artefact at all — never a node, because
a node would permit the illegal state "output exists, unvalidated, but the workflow advanced".

`ApprovedPngExport` needs no second review: the promotion copies approved bytes unchanged, so
the existing hash-bound approval covers the promoted file by construction. No approval is
fabricated.

### 7.2 Trim boundary

Epic 11100 defines `StepKind.Trim`, its state, its `OperationKind.Trim`, its workflow
position and its `AdapterKind.Internal` marker. There is **no** alpha-bound trimming, no crop
algorithm, no manual crop UI and no trim comparison or review UI. Those are Epic 11200.

---

## 8. Jira 11104 — the pure workflow engine

```text
IWorkflowEngine   WorkflowEngine    WorkflowSnapshot   WorkflowTransition
WorkflowCommand   WorkflowEffect    CommandContext     CommandRejection
TransitionTable   CommandKind       RejectionCode      TransitionOutcome
```

### 8.1 Purity

The engine performs no SQL, no disk access, no clock read, no identifier generation, no
logging, no adapter invocation, no WPF, and holds no mutable state. Time and identifiers
arrive in `CommandContext`; work is returned as `WorkflowEffect` records. Tests assert
determinism, non-mutation of the input state, and that timestamps and identifiers come from
the context. A separate test asserts the engine type has no mutable static or instance field.

### 8.2 Effects returned, never executed

`CreateWorkingCopy`, `RunAdapter`, `RecordAttemptStarted`, `PersistRevision`, `RecordReview`,
`RecordSkip`, `InvalidateDescendants`, `ResetStepsFrom`, `PersistPrintDimensions`,
`PersistWhiteUnderbaseBranch`, `PersistOutputName`, `PersistWorkflowSelection`,
`OpenForManualWork`, `ReleaseAutomationLock`, `CleanupWorking`, `MarkSessionCompleted`,
`MarkSessionHandedOff`, `MarkSessionAbandoned`, `BeginAdditionalOutput`,
`RecordAttemptFailure`, `RecordAttemptInterrupted`.

None is executed in this slice. `ReturnToStep` in particular returns
`InvalidateDescendants` + `ResetStepsFrom` as data; the recursive descendant walk and the
file moves are Tasks 11105 and 11108.

### 8.3 States

Step states: `Waiting`, `Processing`, `ReviewRequired`, `Approved`, `RetryRequired`,
`Skipped`, `Failed`, `Interrupted`.
Session states: `Active`, `HandedOff`, `Completed`, `Abandoned`.
They are separate enums on separate types and are never conflated.

### 8.4 Commands

Operator commands: `SelectWorkflow`, `SetOutputName`, `ConfirmOriginal`, `StartStep`,
`Approve`, `Reject`, `Retry`, `Skip`, `HandOff`, `SetPrintDimensions`,
`SelectWhiteUnderbaseBranch`, `ReturnToStep`, `Complete`, `AddAnotherSize`, `AbandonSession`.

System commands: `AttemptSucceeded`, `AttemptFailed`, `AttemptInterrupted` — nested under
`WorkflowCommand.System` with **internal constructors**, so no WPF view model can synthesise
"the adapter succeeded". `InternalsVisibleTo` grants the test project alone the access needed
to drive those transitions. Import is driven through the ordinary
`StartStep` → `AttemptSucceeded` pair rather than a bespoke command, so there is one code
path for producing a Revision.

There is no `SetState(...)` and no `MoveToStep(...)`, asserted by a test.

### 8.5 Rules implemented

| Rule | Behaviour |
| --- | --- |
| Workflow selection | Free until any derived Revision exists; then `WorkflowLocked`. A completed Import survives the change |
| StartStep | Only the current step, only producing steps, only from Waiting/RetryRequired/Failed/Interrupted |
| Skip | Only skippable steps; creates no Revision; default reason "File already satisfies this step"; downstream falls through to the last upstream Revision with no special case |
| Approve | Only from ReviewRequired, and only when the reviewed hash matches the result on offer |
| Reject | ReviewRequired → RetryRequired, plus an append-only decision and `InvalidateDescendants(Rejected)` |
| Retry | Back to Waiting; the following StartStep works from the upstream Revision, never the rejected artefact |
| HandOff | From ReviewRequired/RetryRequired/Failed/Interrupted → session HandedOff; lock released; no further automatic progression legal |
| Completion | Only when every step is Approved or Skipped **and** the terminal artefact is Approved |
| ReturnToStep | Target and all later steps reset; production decisions cleared when rewinding to or before PrintDimensions; invalidation returned as an effect |
| AddAnotherSize | Only from a Completed production session; reopens at PrintDimensions; existing approved outputs are siblings and are left untouched |
| W1 | `StartStep(PhotoshopOutput)` refused without both confirmed dimensions and an explicit branch |

---

## 9. Test results

```text
dotnet restore --locked-mode    succeeded
dotnet build                    succeeded, 0 warnings, 0 errors
dotnet test                     Passed! Failed: 0, Passed: 5333, Skipped: 0, Total: 5333
```

| Area | Coverage |
| --- | --- |
| Workflow shape | Exact steps, order, ordinals, skippable and review flags for all three workflows; Meitu/Photoshop/Trim absence rules; no review-mirroring step kind |
| Exhaustive matrix | Every `StepState × CommandKind` at table level, and every `Workflow × Step × StepState × CommandKind` at engine level — each must yield an explicit allow or a coded rejection |
| Rejection integrity | Every rejection produces no effects, no new state, a non-empty debug message, and leaves the input state untouched |
| Valid transitions | Import → confirm → enhance → review → approve → advance; effects emitted on start; hash-bound approval; unreviewed steps approved directly; both production workflows end to end |
| Invalid transitions | Approve while Processing; Reject while Waiting; Skip Trim; Skip PrintDimensions; workflow change after a derived Revision; Complete early; start a non-current step; start a step outside the workflow; start an operator-confirmed step; stale-hash approval; any command on a completed or abandoned session |
| Skip | Enhancement and BackgroundRemoval only; no Revision; pass-through to the next step |
| Reject and retry | ReviewRequired → RetryRequired → retry → fresh upstream input, retry sequence increments |
| Failure and interruption | Failed and Interrupted create no Revision and release the lock; recovery offers retry, start, skip and handoff; a non-skippable interrupted step still cannot be skipped |
| Handoff | Legal from all four positions; session becomes HandedOff; automatic progression refused afterwards; abandon remains the exit |
| W1 | Output blocked without a branch; all three branches selectable; justification required; not applicable to PREPARE_ASSET |
| Completion | Blocked while awaiting final review; allowed once approved; a fully skipped Meitu path still completes |
| Engine purity | Determinism, input immutability, context-sourced time and identifiers, effects as data, `AvailableCommands` agreeing with what is accepted |
| Domain | Sha256 (including the published empty-input vector), OutputName, workspace path containment, PrintDimensions at 300 DPI, typed ids, UUIDv7 version, results, failure-code names, W1 members |
| Architecture | §10 below |

No test asserts a screen coordinate, click sequence, window title or adapter timing. The
suite needs no network, no database, no file system and no production workstation.

---

## 10. Architecture-boundary verification

All asserted by tests in `tests/PrintFlow.Tests/Architecture/`, using reflection over
assembly references and type graphs rather than an additional package.

| Assertion | Result |
| --- | --- |
| Domain references no other PrintFlow assembly | Pass |
| Domain references no third-party package | Pass |
| Domain references no WPF assembly | Pass |
| Workflow references Domain and nothing else | Pass |
| Workflow does not reference Infrastructure | Pass |
| Workflow references no WPF, SQLite, EF Core, ImageSharp or UI-automation assembly | Pass |
| Infrastructure does not reference App | Pass |
| Infrastructure declares no `Window`, `UserControl`, `Page` or `Application` | Pass |
| Only `PrintFlow.App.Composition` touches Infrastructure types | Pass |
| No `Job`, `Order`, `Customer`, `Asset`, `Artwork`, `User`, `Role`, `Permission`, `ProductionQueue` or `Batch` type exists | Pass |
| No forbidden package referenced by any project | Pass |
| The engine holds no mutable state | Pass |
| System commands expose no public constructor | Pass |
| No `SetState`/`MoveToStep`-style escape hatch exists | Pass |

Compile-time banning of `System.IO` and `DateTime.Now` via `BannedApiAnalyzers` was
**deliberately deferred** — see §12.

---

## 11. Privacy and Git hygiene verification

### 11.1 Tracked content

53 `.cs`, 6 `.md`, 6 `.json` (five `packages.lock.json` + `global.json`), 5 `.csproj`,
2 `.xaml`, 2 `.resx`, 2 `.props`, 1 each of `.sln`, `.config`, `.gitignore`,
`.gitattributes`, `.editorconfig`. **Every tracked file is text.**

Explicit scans over the tracked set:

| Check | Result |
| --- | --- |
| Customer images (`.png .jpg .jpeg .tif .tiff .psd .psb .pdf`) | none tracked |
| Screenshots of customer work | none tracked |
| TIFF production evidence | none tracked |
| Photoshop Action `.atn` | none tracked |
| Photoshop settings or preferences | none tracked |
| Maintop configuration | none tracked |
| Real preset JSON / sign-off JSON | none tracked |
| Database files (`.db`, `-wal`, `-shm`, `.sqlite`) | none tracked |

Largest tracked file: the 11100 plan at 102 KB of Markdown. No tracked file is a binary.

### 11.2 Ignore posture

Deny-by-default. Customer and production formats are denied at **any** depth by extension;
the Epic 11000 artifacts are additionally denied by filename pattern
(`printflow-workstation-*.json`, `workstation-preset-*.json`, `workstation.json`,
`displays.json`) because they are plain JSON that no extension rule would catch. Local
runtime and evidence **directories** are anchored to the repository root — see §13.1 for why
that anchoring matters.

Verified denied: `preset/printflow-workstation-v1.0.0.json`,
`docs/printflow-workstation-v1.0.0.json`, `foo/customer.tif`, `a/b/design.psd`,
`x/PrintFlow-DTF-v1.atn`, `data/printflow.db`, `shot.png`.
Verified **not** denied: all four source and test directories.

Re-inclusions are limited to `src/PrintFlow.App/Assets/*.png` and
`tests/PrintFlow.Tests/Fixtures/synthetic/*.png`. Neither directory has any content yet.

### 11.3 Baseline evidence

Re-verified read-only at the end of the work:

| Artifact | SHA-256 | Status |
| --- | --- | --- |
| Preset manifest | `A114B5D2…83A6` | MATCH, still read-only |
| Final sign-off | `49225D94…8A7A` | MATCH, still read-only |
| Production Action `.atn` | `A04203ED…83EE` | MATCH |

`D:\PrintFlowStudio` still contains exactly 66 files. Nothing under `Baseline` was created,
modified, deleted, regenerated, normalised or migrated.

### 11.4 Remote

No remote is configured. Nothing was pushed anywhere.

---

## 12. Deviations from the approved Plan

| # | Deviation | Reason |
| --- | --- | --- |
| 1 | **.NET 10 SDK installed per-user, not machine-wide** | No administrator rights in this session and a machine-wide MSI needs a UAC prompt. Existing .NET 8 untouched. Consequence and remedy in §4.1 |
| 2 | **`BannedApiAnalyzers` not added** (plan §5, §17.4) | It would put a `PackageReference` in `PrintFlow.Domain`, which the plan also requires to have none, and no `System.IO` or `DateTime.Now` call site exists yet anywhere in the slice. Deferred to Task 11106, when the workspace module introduces the first real file access and the analyzer has something to guard |
| 3 | **No `NetArchTest` package** (plan §17.4) | The composition-root rule is asserted with a ~40-line reflection walk. Fewer dependencies, and the assertion reads more directly than a fluent rule chain. Same guarantee |
| 4 | **`SessionStep` carries `CurrentRevisionSha256`** (not in plan §6.2) | Needed so the engine can refuse an approval whose reviewed hash no longer matches the result on offer, without reading a file. Without it, stale-hash refusal could not be a pure-engine rule at all |
| 5 | **`WorkflowCommand.System.AttemptSucceeded` carries the output hash** | Same reason as #4: the hash must reach the snapshot when the Revision is recorded |
| 6 | **No `ImportInput` command** (plan §9.1) | Import is driven by the ordinary `StartStep` → `AttemptSucceeded` pair, so there is one code path for producing a Revision instead of two. The source-path capture that `ImportInput` carried belongs with the real file work in Task 11106 |
| 7 | **`appsettings.json` not created** (plan §3.2) | Its shape is agreed but nothing reads configuration yet; it lands with the real preset loader in Task 11100.0 rather than sitting unused |
| 8 | **Classic `.sln`, not the .NET 10 default `.slnx`** | The task specifies `PrintFlowStudio.sln` |
| 9 | **`OperationKind.PhotoshopOutput` added** (not in plan §6.4) | The plan's operation list had no value for the TIFF-producing step, and every producing step must declare one |

None of these changes a confirmed product decision. Items 4–6 and 9 are model refinements
the Plan's own tests demanded once written.

---

## 13. Defects found and fixed

### 13.1 `.gitignore` silently excluded two source directories — **high severity**

Inspecting the staged set before commit 2 showed `src/PrintFlow.Domain/Sessions/` and
`src/PrintFlow.Infrastructure/Preset/` missing from the untracked list. `git check-ignore -v`
confirmed:

```text
.gitignore:53:Sessions/   src/PrintFlow.Domain/Sessions/ProcessingSession.cs
.gitignore:45:preset/     src/PrintFlow.Infrastructure/Preset/ConfiguredPresetProvider.cs
```

Two separate causes: an unanchored ignore rule matches at **any** depth, and this workstation
has `core.ignorecase=true`, so `preset/` also matched `Preset/`. Four domain types and the
preset provider would have been left out of the repository, producing a tree that built on
this machine and was broken for anyone who cloned it.

Fixed by anchoring the local runtime and evidence directory rules to the repository root
(`/preset/`, `/signoff/`, `/Sessions/`, …) and compensating with filename-pattern denies for
the Epic 11000 JSON artifacts, which are the one evidence class no extension rule catches.
Verified both directions afterwards: all source visible, every evidence class still denied.
Commit 1 was amended so no commit in the history carries the broken rules.

*Process note:* this is exactly the failure the "inspect the staged set explicitly" gate
exists to catch, and it was caught by that gate rather than by review.

### 13.2 `WorkflowSnapshot` compared its step list by reference — medium severity

`EnginePurityTests.The_same_state_and_command_always_produce_the_same_result` failed with two
snapshots that printed identically. The compiler-generated record equality used reference
equality for `IReadOnlyList<SessionStep>`, so two snapshots describing an identical session
compared unequal whenever their lists were built separately.

Left unfixed this would have quietly weakened every "the state did not change" assertion and
every future "reload restores the same state" test in Task 11108. Fixed by writing
`Equals`/`GetHashCode` explicitly on `WorkflowSnapshot`, comparing steps element by element.
The test was **not** adjusted to accommodate the type — the type was corrected.

### 13.3 Verification mistake corrected during the work

An early check reported the WPF shell as launching successfully. It had not: the visible
window was the Windows *"download .NET"* prompt, whose title is the executable name. The
operator noticed and said so. Re-verified properly by enumerating the process's top-level
windows, which now reports a single visible window titled **PrintFlow Studio**. The
underlying install-scope issue is documented in §4.1.

---

## 14. Remaining work for 11105–11108

**Not started, and not claimed as complete.** No persistence, no file workspace, no hashing
of real files, no naming service, no fake adapters, no environment gate, no session service,
and no startup recovery exist.

| Task | Remaining work |
| --- | --- |
| **11100.0** | Real `IWorkstationPresetProvider`: read-only load of the signed manifest, SHA-256 verification against `appsettings.json`, typed immutable preset graph, synthetic test fixture. Today `ConfiguredPresetProvider` opens no file and fails closed with `EnvironmentNotVerified` |
| **11105** | `RevisionIntegrityGuard`, hash-bound approval end to end, recursive descendant invalidation, immutability triggers, mutation-after-approval test |
| **11106** | `IFileInspector` over WIC (hash, format sniffing, dimensions, DPI, alpha); `IWorkspace` and `IRecycleBin`: session directories, read-only snapshots, per-attempt working copies, path-containment guard, retention |
| **11107** | Naming: sanitiser, preset-driven patterns, atomic `_02`/`_03` collision reservation, reserved device names, Chinese characters |
| **11108** | SQLite: schema, CHECK constraints, immutability triggers, `PRAGMA user_version` migrations, Dapper repositories, one-transaction-per-command |
| **glue** | `SessionService` interpreting the effects this engine already returns, with the file/metadata ordering of plan §10.5 and the global automation lock |
| **fakes** | Deterministic fake Meitu and Photoshop adapters writing real files, scriptable to succeed, fail, time out, hang or produce unreadable output; `IEnvironmentGate` |
| **shell** | Home, workflow selection, session and preset screens; import → run → approve → close → reopen → resume |
| **recovery** | Interrupted attempts, stale lock release, orphan quarantine at startup |

Also deferred: `BannedApiAnalyzers` (§12 item 2) and `appsettings.json` (§12 item 7).

Still out of scope for the whole of Epic 11100: alpha-bound trimming and trim/review UI
(11200), Meitu automation (11300), Photoshop TIFF production (11400), environment
verification (11500).

---

## 15. Exact git status

```text
$ git status
On branch master
nothing to commit, working tree clean

$ git remote -v
(no output — no remote configured)
```

Ignored local-only content present in the working tree: `bin/` and `obj/` build output under
the five projects. Nothing else.

---

## 16. Commit SHAs

```text
09d07e4665bc7d220f575b7060e01de7b0abce8f  11103 and 11104: fixed workflow definitions and the pure workflow engine
9be2e23ef33f224a09ab3f35a0d5979dbac6c3c6  11102: core domain model, typed identifiers, value objects and results
0a35824c924cb6af778af6eefad75bcc2d809b85  11101: .NET 10 solution, five projects, build and package configuration
c8733ebdfe140576d5d699fd1f3049b21ffcc75c  Bootstrap repository: authority docs, privacy-first ignore rules
```

A fifth commit carries this report; its SHA is assigned when the report is committed and is
therefore not quotable from inside the document. `git log --oneline` shows it at the tip.

Branch `master`, no remote, nothing pushed.

---

## 17. Verdict

Every PASS condition is met:

- [x] authoritative repository established at `D:\Repositories\printflow-Studio`
- [x] stale tree archived intact as `Printflow Studio_ARCHIVE_20260818`, 30 files / 166,430 bytes verified
- [x] no sensitive material tracked — every tracked file is text, all evidence classes denied
- [x] .NET solution builds: `dotnet build`, 0 warnings, 0 errors
- [x] all tests pass: 5,333 passed, 0 failed, 0 skipped
- [x] domain and workflow boundaries match the approved Plan, asserted by architecture tests
- [x] the three fixed workflows are correct per MVP design §6.2–§6.4
- [x] the pure workflow reducer works, with an exhaustive transition matrix and no fall-through
- [x] no real third-party automation was introduced — nothing launches, reads or modifies
      Meitu, Photoshop or Maintop
- [x] Epic 11000 evidence untouched; all three hashes re-verified

One operator action is recommended but not blocking: install the .NET 10 Desktop Runtime
machine-wide so the built executable launches from Explorer (§4.1).

**PART 1 PASS — READY FOR 11105–11108**
