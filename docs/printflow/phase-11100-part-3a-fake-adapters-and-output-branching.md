# PrintFlow Studio — Phase 11100 Part 3A: Fake Adapters and Output Branching

| Item | Value |
| --- | --- |
| Document | Epic 11100 Part 3A implementation report |
| Report date | 19 August 2026 |
| Scope delivered | Full deterministic fake-adapter scenario harness; `IEnvironmentGate` foundation seam; `AddAnotherSize` sibling-output integration coverage; one defect fix exposed by that coverage |
| Scope **not** delivered | Startup recovery, WPF UI, real Meitu/Photoshop, real trimming — all explicitly deferred |
| Design authority | PrintFlow Studio MVP Design Document v1.0 (Confirmed, English); Epic 11100 Plan; Part 1 and Part 2 reports |
| Repository | `D:\Repositories\printflow-Studio` — branch `master`, remote `origin` |

---

## 1. Preflight

`git status -sb` showed `master...origin/master`, clean, nothing ahead or behind — Part 2's report
commit was already pushed, resolving the "may or may not be pushed" uncertainty this task flagged.
`dotnet --version` reported `10.0.400`. Baseline gates before any change:

```text
dotnet restore --locked-mode   succeeded
dotnet build                   succeeded, 0 warnings, 0 errors
dotnet test                    Passed! Failed: 0, Passed: 5409, Skipped: 0, Total: 5409
```

5,409 matches Part 2's own final count exactly.

---

## 2. Part 2 report correction

Part 2 §11 stated "5,076 tests carried over from Part 1"; Part 2 §21 and this session's own
baseline both confirm the Part 1 baseline was 5,333, and 5,333 + 76 = 5,409 (the stated total).
5,076 was a typo. Corrected to 5,333 in `phase-11100-part-2-integrity-workspace-persistence-implementation.md`
§11; no other line in that report was touched, and no test or code was altered to chase the number.

---

## 3. Full fake-adapter scenarios

`FakeMeituProcessor` and `FakePhotoshopOutputProcessor` (`src/PrintFlow.Infrastructure/Adapters/Fake/`)
keep their stable ids (`fake-meitu-v1`, `fake-photoshop-v1`) and their default real-file-touching
`Succeed` behaviour, and gain a scriptable scenario set via `SetScenario(FakeAdapterScenario)`:

```text
Succeed                  — unchanged default; writes/edits a real file
FailWith(FailureCode)    — fails immediately with the given code, no file written
Timeout                  — fails immediately with FailureCode.Timeout, no file written
ProduceUnreadableFile    — leaves a genuine zero-byte file; the real WicFileInspector
                            deterministically reports OutputUnreadable for it
ProduceMissingFile       — leaves nothing at the expected output path; the real inspector
                            deterministically reports OutputMissing
HangUntilCancelled       — awaits the real CancellationToken, then fails with Cancelled
```

The scripted behaviour lives in one shared `FakeAdapterExecution` helper (`Succeed` is the only
case that differs between the two adapters — Meitu edits its working copy in place, Photoshop
copies into a freshly reserved path — so that one case is supplied by the caller). Every scenario
still runs through the real workspace and `IFileInspector`; nothing fabricates a Revision
directly, matching the constraint the fakes have carried since Part 2.

`HangUntilCancelled` is synchronised deterministically, not by sleeping: the fake exposes a
`HangStarted` task that completes only once it has genuinely entered its wait, so a test can
`await` that signal and then cancel, with no race and no arbitrary delay. `Timeout` and the
scripted failures are intentionally instantaneous — Epic 11100 owns no real timer, so "timeout"
here is a scripted outcome, not a wall-clock wait, per the task's own "deterministic short test
timing" instruction.

Both adapters now take `IWorkspace` in their constructor (Meitu previously took none, since it
edited files in place without ever resolving a path); `ServiceRegistration` and the test harness
were updated accordingly, and `AdapterExecutionMode Mode => AdapterExecutionMode.Fake` was added
to both (§4).

---

## 4. `IEnvironmentGate` foundation seam

`IEnvironmentGate` (`src/PrintFlow.Workflow/Ports/IEnvironmentGate.cs`) is a one-method port:

```csharp
OperationResult<Unit> Verify(AdapterExecutionMode mode);
```

`AdapterExecutionMode` (`Fake` / `Production`) is declared by the adapter itself
(`IMeituProcessor.Mode`, `IPhotoshopOutputProcessor.Mode`) rather than inferred from
`AdapterId`, so the gate never has to string-sniff an identifier.

`FoundationEnvironmentGate` (`src/PrintFlow.Infrastructure/Gate/`) is the Epic 11100
implementation: it inspects nothing — no Meitu/Photoshop executable, no resolution/DPI, no
display, no Action hash — and simply allows `Fake` and refuses `Production` with
`FailureCode.EnvironmentNotVerified`. Epic 11500 replaces this type; the port itself does not
change shape when it does.

`SessionService.RunAdapterBackedStepAsync` calls the gate for every adapter-backed step
(`definition.IsAdapterBacked`, i.e. Meitu/Photoshop only), immediately after resolving the step
definition and **before** the automation-lock check, the `Running`-attempt commit, and any file
work — matching the ordering the task specified:

```text
Workflow allows step -> Revision integrity verified -> EnvironmentGate -> automation lock -> adapter execution
```

A refusal returns immediately with no attempt persisted and no lock touched, the same pattern
already used for "another session holds the lock". `ServiceRegistration` registers
`FoundationEnvironmentGate` as the composition root's `IEnvironmentGate`, alongside the existing
static fail-closed check in `RegisterAdapters` (which refuses `Adapters:Mode = Production`
outright because no production adapter type exists yet). The two checks are independent and
deliberately redundant: the composition-root check stops a misconfigured `appsettings.json`
today; the gate is what stops a *future* production adapter, once one exists, from running
without passing through it — the actual invariant this task asked to make structural.

`EnvironmentGateTests.cs` proves both halves: the gate itself allows `Fake`/refuses `Production`,
and `SessionService`, given a test double declaring `AdapterExecutionMode.Production`, refuses
`StartStep` with `EnvironmentNotVerified` — with the adapter's own method never invoked (asserted
via a call counter) and no `Running` attempt or lock change persisted.

---

## 5. Failure and retry behaviour

`FakeAdapterScenarioTests.cs` and `RetryAndReviewTests.cs` (both under
`tests/PrintFlow.Tests/Integration/Persistence/`) drive every required path through the real
`SessionService`/workspace/SQLite pipeline:

| Scenario | Result | No Revision | Lock released |
| --- | --- | --- | --- |
| Explicit adapter failure | Step `Failed`, `FailureCode.OutputValidationFailed` | ✅ | ✅ |
| Missing output | `FailureCode.OutputMissing`; retry remains legal | ✅ | — |
| Unreadable output | `FailureCode.OutputUnreadable` | ✅ | — |
| Timeout | `FailureCode.Timeout` | ✅ | ✅ |
| HangUntilCancelled | `Running` persisted first, then cancelled → `FailureCode.Cancelled` | ✅ | ✅ |

**Retry after failure**: `FailWith` → `Failed` → `Retry` → `StartStep` (fresh `AttemptId`,
succeed) → `Approve`. The failed and succeeded `ProcessingAttempt` rows are both verified
present, with distinct ids; the failed one carries a null `OutputRevisionId` (the DB `CHECK`
constraint's own guarantee, confirmed at the domain level too); the succeeded Revision's file
path contains the succeeding attempt's own id and not the failed attempt's — proving the retry's
`Working\<attemptId>\` copy is structurally fresh, never a reuse of the failed attempt's copy.

**Reject then retry**: `Succeed` → Revision A → `Reject` → `RetryRequired` → `Retry` → `Succeed`
→ Revision B → `Approve`. Both Revisions are persisted and distinct by id (the fake Meitu adapter
edits nothing, so A and B are genuinely byte-identical — the same hash but two separate rows,
which is exactly what proves identity is by `RevisionId`, not content). Both share the same
`SourceRevisionId` (siblings of the same upstream, never a chain A→B); the rejection decision on
A and the approval decision on B are both present in `aggregate.Reviews`; Revision A's own
`IsValid` stays `true` (only *descendants* of a rejected revision are invalidated, and A has
none — the existing, correct design).

---

## 6. `AddAnotherSize` sibling-output coverage

`AddAnotherSizeTests.cs` closes the gap Part 2 §9/§18 recorded honestly as untested. All three
tests run a real `GENERATE_PRINT_TIFF` session (Import → confirmed → `PrintDimensions` →
`PhotoshopOutput`, twice) against a real temporary workspace, real SQLite, real fake TIFF files,
and real hashes:

- **Case 1 — approve B.** Both `PrintOutput`s end `IsValid = true`, `ReviewState = Approved`,
  sharing one `SourceRevisionId`, at their own distinct dimensions (200 mm / 150 mm), with both
  files genuinely present on disk.
- **Case 2 — reject B.** A is completely untouched (`IsValid = true`, still `Approved`); B's
  `ReviewState` becomes `Rejected` and its step returns to `RetryRequired`. Rejecting B never
  reaches A — proven, not assumed.
- **Case 3 — return upstream of the shared source.** `ReturnToStep(OriginalConfirmation)`
  invalidates the root revision's descendants; since `GENERATE_PRINT_TIFF` has no
  Enhancement/Trim step between Import and `PhotoshopOutput`, those descendants *are* A's and
  B's own Revisions. The root Revision itself stays valid (correct — only descendants are ever
  invalidated); both `PrintOutput`s end `IsValid = false`.

---

## 7. Defect found and fixed

**`PrintOutput` invalidation missed the case with no intermediate producing step — real, found by
Case 3 above.**

`SessionService.ComputeDescendantInvalidations` matched a `PrintOutput` for invalidation only via
`descendants.Contains(o.SourceRevisionId)`. That is correct whenever a producing step (Enhancement,
Trim) sits between the shared source and `PhotoshopOutput`, because then `SourceRevisionId` is
itself a genuine descendant of whatever was invalidated. But `PrintOutput.SourceRevisionId` is
documented as "the upstream design it was produced from", never its own identity — and for
`GENERATE_PRINT_TIFF`, which has no such intermediate step, `SourceRevisionId` is literally the
root Revision itself. The walk's own root-exclusion rule ("the changed revision's descendants are
invalidated, never the changed revision itself") then meant a `PrintOutput` sourced directly from
the invalidation root was never matched, even though its own twin Revision — found in the walk
under the shared GUID a `PrintOutput` and its twin Revision are deliberately given (Part 2 §16
item 4) — plainly had been.

First caught by Case 3, which is exactly the situation `GENERATE_PRINT_TIFF`'s shape creates and
no prior test exercised (the one existing `ReturnToStep` integration test used `PREPARE_ASSET`,
which has no `PrintOutput` at all). Fixed by also matching
`descendants.Contains(RevisionId.From(o.Id.Value))` — the `PrintOutput`'s own id, reinterpreted
as its twin Revision's id. Purely additive: every previously-correct match still matches; the fix
only adds the case that was silently missed. Verified by Case 3 passing, with Cases 1 and 2
(which do not depend on this path) unaffected.

---

## 8. Build and test result

```text
dotnet restore --locked-mode    succeeded
dotnet build                    succeeded, 0 warnings, 0 errors
dotnet test                     Passed! Failed: 0, Passed: 5421, Skipped: 0, Total: 5421
```

5,409 carried over from Part 2 unchanged, plus 12 new tests this slice (5 fake-adapter-scenario,
2 retry/reject, 2 environment-gate, 3 `AddAnotherSize`). No existing test was modified to make
this slice pass.

No NuGet package was added, removed, or upgraded; the existing `NuGetAuditSuppress` in
`Directory.Build.props` for `SQLitePCLRaw.lib.e_sqlite3` is unchanged, and `dotnet restore
--locked-mode` reported no new advisory during this slice.

---

## 9. Git state

Working tree changes this slice, none of them touching `D:\PrintFlowStudio` or any customer/
evidence file:

```text
new:      src/PrintFlow.Workflow/Ports/IEnvironmentGate.cs
new:      src/PrintFlow.Infrastructure/Gate/FoundationEnvironmentGate.cs
new:      src/PrintFlow.Infrastructure/Adapters/Fake/FakeAdapterScenario.cs
new:      src/PrintFlow.Infrastructure/Adapters/Fake/FakeAdapterExecution.cs
modified: src/PrintFlow.Workflow/Ports/AdapterPorts.cs
modified: src/PrintFlow.Infrastructure/Adapters/Fake/FakeMeituProcessor.cs
modified: src/PrintFlow.Infrastructure/Adapters/Fake/FakePhotoshopOutputProcessor.cs
modified: src/PrintFlow.Workflow/Services/SessionService.cs
modified: src/PrintFlow.App/Composition/ServiceRegistration.cs
modified: tests/PrintFlow.Tests/Fixtures/SessionServiceHarness.cs
new:      tests/PrintFlow.Tests/Integration/Persistence/FakeAdapterScenarioTests.cs
new:      tests/PrintFlow.Tests/Integration/Persistence/RetryAndReviewTests.cs
new:      tests/PrintFlow.Tests/Integration/Persistence/EnvironmentGateTests.cs
new:      tests/PrintFlow.Tests/Integration/Persistence/AddAnotherSizeTests.cs
modified: docs/printflow/phase-11100-part-2-integrity-workspace-persistence-implementation.md
new:      docs/printflow/phase-11100-part-3a-fake-adapters-and-output-branching.md
```

Every file is `.cs` or `.md`. `git diff --cached --stat`/`--cached` were reviewed before each
commit for exactly this list; no binary, no real preset/sign-off, no runtime DB, no production
TIFF. Two implementation commits were made, per the task's own preferred split, with one
unavoidable overlap noted honestly: the `ComputeDescendantInvalidations` fix (§7) was discovered
by the `AddAnotherSize` tests (commit 2's subject) but lives in `SessionService.cs`, the same file
the `IEnvironmentGate` wiring (commit 1's subject) had to touch to compile at all — the two
changes were not in adjacent hunks and a clean textual split was attempted and abandoned after it
proved unreliable against this checkout's line-ending normalisation, rather than risk the
working tree over a cosmetic commit boundary. Commit 1 therefore carries both the gate wiring and
the fix; commit 2 carries the tests that found it, the `AddAnotherSize` coverage, and this report.
No force push; `origin/master` was not touched beyond a normal fast-forward push, run only after
both gates below were green.

---

## 10. Remaining Epic 11100 Final Integration work

Unchanged from Part 2 §18, minus what this slice closed:

| Area | Status |
| --- | --- |
| Startup recovery orchestration (interrupted attempts, stale lock release, orphan quarantine sweep) | Still not wired into a startup sequence; `FindRunningAttemptsAsync` and `Quarantine` remain the tested primitives only |
| Operator-facing UI (Home, Session, review, dimensions, fake-scenario screens) | Untouched |
| Real Meitu/Photoshop production adapters, real environment verification | Epic 11300/11400/11500, unstarted |
| Full production preset schema (geometry limits, W1 identifiers, executable hashes) | Only `storageAndNamingContract` is parsed |
| `AutomationLogEntry` / `Setting` tables | Schema exists; nothing reads or writes either yet |

---

## 11. Verdict

- [x] fake success/failure/missing/unreadable/timeout/cancellation paths all work, against the
      real workspace and file inspector, never fabricating a Revision
- [x] failed attempts never produce a usable Revision — checked at both the domain (`OutputRevisionId`
      null) and database (`CHECK` constraint) level
- [x] retry uses a fresh `AttemptId` and a structurally fresh `Working\<attemptId>\` directory,
      never reusing a failed attempt's working copy
- [x] `IEnvironmentGate` allows Fake and blocks Production, ahead of the automation lock and any
      file work, with a test proving the adapter itself is never called
- [x] `AddAnotherSize` sibling behaviour is integration-tested — approve, reject, and the
      shared-source invalidation case — against real files and a real database, not a pure
      workflow unit test
- [x] all previous tests remain green (5,409 → 5,421, zero modified)
- [x] no real external automation introduced; nothing under `D:\PrintFlowStudio` touched

**`PART 3A PASS — READY FOR STARTUP RECOVERY`**
