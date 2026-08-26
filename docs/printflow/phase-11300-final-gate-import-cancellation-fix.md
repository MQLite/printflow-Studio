# Epic 11300 — Final Gate Blocker Fix 2: Workspace Import Cancellation

Follow-up to **Final Gate Closure R3**, which recorded this as open defect R3.9. This document
covers only that defect and its focused regression coverage. It does not restate, revise or
replace the R3 report, and no part of Final QA was resumed.

Baseline carried in from R3: 8,298 passed / 0 failed / 0 skipped, 0 warnings, 0 errors, no
vulnerable packages.

---

## 1. R3 crash and root cause

R3 terminated the WPF process with a `.NET Runtime` event 1026 / `Application Error` event 1000
pair naming:

```
System.Threading.Tasks.TaskCanceledException: A task was canceled.
   at PrintFlow.Infrastructure.Workspace.FileWorkspace.ImportSourceAsync (FileWorkspace.cs:101)
```

`FileWorkspace.cs:101` was `await input.CopyToAsync(output, cancellationToken)`. The `try` around
it caught `IOException` and `UnauthorizedAccessException` and nothing else, so when the token was
cancelled the `OperationCanceledException` left the workspace boundary and kept going:

```
FileWorkspace.ImportSourceAsync          no cancellation catch  → escapes
SessionService.ImportAsync               no cancellation catch  → escapes
HomeViewModel.ImportAsync                no cancellation catch  → escapes
AsyncRelayCommand execution continuation nothing left to catch  → process terminated
```

The trigger was a second execution of `ChooseFileCommand`. `AsyncRelayCommand.ExecuteAsync`
cancels the previous execution's `CancellationTokenSource` before starting a new one, so the
second invocation cancelled the first import while its copy was in flight.

The defect was reproduced directly before any code was changed: a probe that cancelled an
in-flight `ImportSourceAsync` returned `THREW TaskCanceledException`, with a 1,179,648-byte
partial file left in the session's `Source\` area under the exact name a successful import would
have used, carrying `Archive` rather than `ReadOnly` attributes.

## 2. Why the trigger was unusual but the defect is real

Two independent barriers stop a mouse- or keyboard-driven operator from reaching the trigger, and
both were verified:

- `OpenFileDialogPicker.PickSingleFile` calls `OpenFileDialog.ShowDialog()`, which is modal and
  disables its owner window. No click can reach the Choose File button while the picker is open.
- The generated `ChooseFileCommand` is an `IAsyncRelayCommand` created from a cancellable method
  with `CommunityToolkit.Mvvm` 8.4.2 defaults, so concurrent executions are disallowed and
  `CanExecute` is false for as long as the first import runs — the bound button is disabled
  regardless of the dialog. This is asserted, not assumed
  (`A_second_ChooseFile_is_refused_while_the_first_import_is_still_running`).

R3's second execution came from UI Automation invoking the command directly, which is a harness
capability rather than an operator one.

The **defect** is nonetheless real and independent of the trigger. `ChooseFileCommand` is
cancellable by construction, so any cancellation of a running import — a second execution, a
future Cancel affordance, or application shutdown — reaches the same unguarded frame. And it was a
deviation from this codebase's own convention: every other producing path already converts
cancellation into a structured failure. `ImportSourceAsync` was the outlier.

## 3. Existing project cancellation convention

Surveyed before writing anything, exactly as the brief required:

| Site | Shape |
|---|---|
| `FakeAdapterExecution` | `catch (OperationCanceledException)` → `FailureCode.Cancelled` |
| `FakeBackgroundRemovalPng` | `catch (OperationCanceledException)` → `FailureCode.Cancelled` |
| `ProductionMeituProcessor` | `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)` → `FailureCode.Cancelled`, `isRetryable: true`, context dictionary |
| `DeterministicAlphaTrimProcessor` | `catch (OperationCanceledException)` → `FailureCode.Cancelled` |
| `WicImagePreviewDecoder` | `catch (OperationCanceledException)` → `FailureCode.Cancelled` |
| `WicManualCropProcessor` | `catch (OperationCanceledException)` → `FailureCode.Cancelled` |
| `WicMeituTransparencyInspector` | `catch (OperationCanceledException)` → `FailureCode.Cancelled` |
| `RecycleBin` | exception filter including `OperationCanceledException` |
| `SessionService.RunProducingStepAsync` | `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)` → `FailureCode.Cancelled`, `isRetryable: true`, context dictionary |

The convention is: **convert at the boundary, use `FailureCode.Cancelled`, guard on the operation's
own token, never `catch (Exception)`.** The fix follows it. No second cancellation architecture was
introduced.

### Containment map established by the audit

| Workspace method | Production caller | Cancellation containment before this fix |
|---|---|---|
| `ImportSourceAsync` | `SessionService.ImportAsync` | **none — escaped to the shell** |
| `CreateWorkingCopyAsync` | `PerformStepWorkAsync` | contained by `RunProducingStepAsync` |
| `WriteReservedAsync` | `PerformStepWorkAsync` | contained by `RunProducingStepAsync` |
| `MoveToRejectedAsync` | *no production caller* | not reachable |

This is why import alone crashed, and why the sibling copy methods were left untouched: they are
already contained, and changing them would have been unrequested work on paths that behave
correctly today.

`SqliteSessionRepository` never observes its `CancellationToken` (Dapper is called without one),
so persistence cannot raise `OperationCanceledException` on this path. The only two awaits on the
import path that can are the workspace copy and `WicFileInspector.InspectAsync`.

## 4. The exact fix

Two changes, 125 added lines, no dependency, schema, migration, localisation or XAML change.

### 4a. `FileWorkspace.ImportSourceAsync` — the defect itself

```csharp
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
{
    return CancelledImport(targetAbsolute.Value, sourceAbsolutePath);
}
catch (IOException ex) { /* unchanged */ }
catch (UnauthorizedAccessException ex) { /* unchanged */ }
```

`OperationCanceledException` is the base of `TaskCanceledException`, so both are covered by the one
clause. The `when` filter is deliberate and required by §5 of the brief: an
`OperationCanceledException` that does not belong to this import is somebody else's failure and is
not reclassified as a cancellation.

### 4b. `SessionService.ImportAsync` — import-path containment

The two steps that establish a source — the copy and the inspection — were extracted into
`EstablishSourceAsync` and the call wrapped in the same guarded catch `RunProducingStepAsync`
already uses:

```csharp
try { established = await EstablishSourceAsync(...); }
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
{ established = OperationResult.Fail<...>(OperationFailure.Create(FailureCode.Cancelled, ...)); }

if (established.IsFailure) { return await FailImportAsync(...); }
```

Why this is in scope rather than scope creep: `WicFileInspector.InspectAsync` has the same
`IOException`/`UnauthorizedAccessException`-only shape as the workspace copy did, and it runs on
the import path under the same token. Fixing 4a alone would still leave the shell killable by a
cancellation that lands a few milliseconds later, during hashing. The PASS criterion is that import
cancellation cannot terminate the shell, so the import path had to be closed, not just one frame of
it.

`WicFileInspector` itself was **not** modified. It is called from every producing step, and
changing what it returns on cancellation would have changed which failure context those steps
persist — a cross-cutting behaviour change on paths that are already correctly contained. Catching
at the import path's own boundary keeps the blast radius to the one method that had a hole.

Note that nothing in `ImportAsync` can actually reach the 4b catch today, because 4a converts the
one reachable source. It is containment for the boundary, not a second policy: an
`OperationResult` is what the import path returns, whatever the frames below it do.

## 5. The structured cancellation result

```
Code            FailureCode.Cancelled
MessageKey      Failure_Cancelled            (existing localisation convention)
TechnicalDetail "The source import was cancelled before the copy completed. No source snapshot
                 was created, and the half-written destination was not kept as one."
IsRetryable     true
Context         sourceFileName             = the operator's file name
                partialDestination         = "quarantined" | "retained" | "none"
                partialDestinationDetail   = present only when quarantining failed
```

`FailureCode.Cancelled` already existed ("The operator or the application cancelled the
operation") and already maps to an operator-facing message key. No new failure code, and no change
to `FailureCode`, was needed.

Cancellation stays distinguishable from genuine filesystem failure, per §4 of the brief:
`IOException` and `UnauthorizedAccessException` still produce `FailureCode.WorkspaceError`, a
missing source still produces `FailureCode.OutputMissing`, and a destination collision still
produces `WorkspaceError`. This is asserted directly by
`A_destination_that_already_exists_is_still_a_WorkspaceError_and_not_a_cancellation`.

## 6. Partial destination behaviour — the exact chosen behaviour

**The partial file is moved to `{workspace root}\Quarantine\` with a timestamped name and a
`.reason.txt` beside it. It is never deleted, and `Source\` is left empty.**

The reason text is
`"Cancelled part-way through importing '<path>'. These bytes are a truncated copy, never a source snapshot."`

This uses the existing `IWorkspace.Quarantine` route rather than a new one. That route exists
precisely for "a file exists on disk with no corresponding metadata", which is exactly what a
cancelled copy leaves behind, and it moves rather than deletes because there is deliberately no
hard-delete path anywhere in the solution.

Safety of the move was verified rather than assumed: by the time the catch body runs, the
`await using` blocks have already unwound and disposed both `FileStream`s, so the move is against a
closed handle. The probe confirmed the partial file was immediately movable and deletable at that
point. If a scanner does still hold it, `Quarantine` reports a structured failure and the fix
records `partialDestination = "retained"` — there is no unsafe hard-delete fallback.

The three §6 requirements hold:

- **Never returned as successful imported content.** No `WorkspaceFileRef` to it is returned; the
  method returns a failure.
- **No session/source metadata treats it as authoritative.** `SessionService` routes the failure
  through `FailImportAsync`, which marks the Import attempt `Failed`. No `Revision` and no
  `InputSnapshot` is written. Asserted in
  `A_cancelled_import_is_reported_by_the_service_and_writes_no_source_state`.
- **No later workflow may consume it as a valid input.** It is no longer under the name a snapshot
  would have had, and startup recovery only ever enumerates `Working\` (`ListWorkingFiles`), so it
  is outside recovery's reach as well.

## 7. Direct cancellation regression

`tests/PrintFlow.Tests/Integration/Files/WorkspaceImportCancellationTests.cs`

`Cancelling_a_copy_in_flight_returns_a_structured_cancellation_and_throws_nothing` — cancellation
lands **during the copy**, not before it: the test waits for the copy's own observable effect, a
flushed chunk in the destination area, and cancels the instant it appears. That is a rendezvous
against real progress, not a sleep. The source is created with `FileStream.SetLength(64 MiB)`, so
making it costs no writes and no measurable time while copying it takes about 50 ms — against a
rendezvous that fires after 1–2 ms with the first megabyte on disk, measured. The rendezvous runs
on the test's own thread, never a pool thread, so a busy pool can delay the copy but not the
observer. If the copy were ever to win the race the test fails loudly, because every assertion
insists on the cancelled outcome; it can never pass quietly.

The already-cancelled-token case is a **separate** test, as the brief specifies:
`An_import_asked_for_with_an_already_cancelled_token_is_refused_the_same_way`.

## 8. Partial-file regression

`A_cancelled_copy_leaves_a_quarantined_partial_file_and_an_empty_Source_area` asserts the actual
chosen behaviour of §6, not merely that the operation failed:

- `Failure.Context["partialDestination"] == "quarantined"`
- the session's `Source\` area is empty
- exactly one matching file exists under `Quarantine\`
- its length is greater than zero **and** less than the source's — genuinely a truncated prefix,
  genuinely retained rather than deleted
- its `.reason.txt` records why
- the operator's own file is still its full original length

## 9. Shell-boundary regression

`tests/PrintFlow.Tests/Integration/Persistence/ImportCancellationShellBoundaryTests.cs`

- `A_cancelled_import_is_reported_by_the_service_and_writes_no_source_state` — the service-facing
  half: `ImportAsync` returns `FailureCode.Cancelled`, the Import attempt and step persist as
  `Failed`, and no `Revision` and no `InputSnapshot` exist.
- `A_cancelled_import_leaves_Home_reporting_a_failure_rather_than_terminating` — the R3 family
  itself, through the real `HomeViewModel` and the real `ChooseFileCommand`. The import is
  cancelled in flight through `IAsyncRelayCommand.Cancel()`, which is exactly what a second
  command execution did in R3. Awaiting the command's task is the line that used to throw
  `TaskCanceledException` out of the view model; it now completes, Home shows the failure in its
  notice line, no navigation occurs, and `IsBusy` returns to false.

Two real Windows file dialogs were not reproduced, and did not need to be: the defect is
cancellation propagation, not UI Automation duplicate-click behaviour.

## 10. Normal import regression

- `An_ordinary_import_under_a_live_token_still_produces_the_read_only_snapshot` — an ordinary
  import driven with a live, cancellable `CancellationTokenSource`: correct managed reference and
  area, byte-identical read-only snapshot, source unchanged, nothing quarantined. This guards
  against the new catch firing on a path that was not cancelled.
- `An_ordinary_import_through_Home_still_establishes_the_session_and_navigates` — end to end
  through the screen: attempt `Succeeded`, `InputSnapshot` written, exactly one `Import` revision,
  navigation to Workflow Selection, customer file byte-identical afterwards.
- The pre-existing suite is unchanged and still green, including
  `Import_never_modifies_the_operators_source_file`,
  `Snapshot_is_byte_identical_to_the_source_and_read_only` and
  `Failed_import_leaves_the_source_untouched`. No regression to source immutability.

## 11. Double-invocation audit

**Normal product UI already prevents it**, by two independent barriers (§2 above): the modal
picker disables the owner window, and the generated command disallows concurrent executions so
the bound button is disabled for the duration of the import.

Per the brief, **no new command-concurrency infrastructure was added** to chase the QA harness
artefact. The existing barrier is now asserted rather than assumed, at the moment it matters —
while a real import is in flight — by
`A_second_ChooseFile_is_refused_while_the_first_import_is_still_running`, which also confirms the
command is offered again once the import finishes.

No mouse- or keyboard-accessible double-invocation route was found, so there is no separate defect
to report and no reason to widen this slice.

## 12. Targeted tests run, and exact counts

Per the brief's test policy, the full suite was **not** run. The implementation required no
cross-cutting change to workflow, state-machine or persistence architecture, so the escape hatch
for wider testing was not needed.

| Filter | Result |
|---|---|
| `WorkspaceImportCancellationTests` (new) | 5 passed, 0 failed |
| `ImportCancellationShellBoundaryTests` (new) | 4 passed, 0 failed |
| `Integration.Files.WorkspaceTests` | 15 passed, 0 failed |
| `FileInspectorTests` | 12 passed, 0 failed |
| `SessionServiceTests` | 8 passed, 0 failed |
| `HomeAndWorkflowSelectionTests` | 19 passed, 0 failed |
| `StartupRecoveryTests` | 9 passed, 0 failed |
| `Architecture.BannedApiEnforcementTests` | 3 passed, 0 failed |
| `Architecture.DependencyRuleTests` | 9 passed, 0 failed |
| `Architecture.ScopeGuardTests` | 16 passed, 0 failed |
| **Combined run of all of the above** | **239 passed, 0 failed, 0 skipped** |

The 9 new tests were additionally run **12 consecutive times** to prove the copy rendezvous is not
flaky: 12/12 clean, 0 failures.

### Negative control — the tests fail without the fix

The two production files were stashed and the new tests re-run against the R3 code. **6 of the 9
failed** — precisely the six cancellation cases — and the three deliberate controls (ordinary
import, ordinary Home import, and the `WorkspaceError`-not-`Cancelled` distinction) passed. The
fix was then restored. The coverage demonstrably tests the defect rather than the fix.

### Architecture boundaries verified (§13)

- `FileWorkspace` remains Infrastructure-owned; `DependencyRuleTests` green.
- View models still contain no `System.IO`; `BannedApiEnforcementTests` green — this matters
  because `SessionService` lives in `PrintFlow.Workflow`, which is forbidden `System.IO` entirely,
  and the change added none.
- No cancellation-specific filesystem manipulation was added to `App`.
- No arbitrary path seam was introduced — the fix needed no test seam in production code at all;
  the copy rendezvous works entirely from the test side.
- No broad exception swallowing: neither changed file contains `catch (Exception`.

## 13. Build result

`dotnet build` — **build succeeded, 0 warnings, 0 errors**, both before the change (baseline
confirmation) and after.

## 14. Dependency / security audit

**Dependency graph unchanged — vulnerability audit deferred to Final Gate R4.**

No `.csproj`, `Directory.Packages.props`, lock file, `nuget.config` or `global.json` was modified,
so `dotnet restore --locked-mode` and `dotnet list package --vulnerable --include-transitive` were
not rerun. Restore reported "All projects are up-to-date for restore" on every build.

## 15. Meitu evidence

Not run, per §17 of the brief. No new preset was created. Confirmed by `git status` that **no**
change touched the v1.8.0 manifest, the `appsettings` preset pointer or hash, accepted evidence, or
Meitu binary configuration — the only modified files are the two source files and the two new test
files. Full 18/18 hash re-verification therefore belongs to R4 and was not necessary here.

## 16. Remaining Final Gate R4 scope

Untouched and still owed by R4:

- the complete suite (`dotnet test`, 8,298 + 9 new = 8,307 expected)
- `dotnet restore --locked-mode` and the vulnerable-package audit
- full Meitu evidence verification (18/18 hashes)
- zh-CN / en-US visual states A–I in both locales
- the controlled Production seam
- controlled Enhancement live regression
- controlled Background Removal live regression
- controlled Stop live regression
- controlled Take Over live regression

None of these were started in this slice.

## 17. Git state

Branch `master`, no history rewritten, nothing amended, nothing pushed. R3 and its report are
untouched.

Files changed:

```
M  src/PrintFlow.Infrastructure/Workspace/FileWorkspace.cs        (+68)
M  src/PrintFlow.Workflow/Services/SessionService.cs              (+67 -10)
A  tests/PrintFlow.Tests/Integration/Files/WorkspaceImportCancellationTests.cs
A  tests/PrintFlow.Tests/Integration/Persistence/ImportCancellationShellBoundaryTests.cs
A  docs/printflow/phase-11300-final-gate-import-cancellation-fix.md
```

No unrelated subsystem was changed: Meitu automation, the naming renderer, Background Removal, the
workflow state machine, the automation lock, startup recovery, the database schema, the migration
version, localisation and XAML/layout are all untouched.

---

## Verdict

**11300-FINAL-BLOCKER-FIX-2 PASS — READY FOR FINAL GATE CLOSURE R4**
