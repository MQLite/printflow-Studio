# Epic 11100 — Core Desktop & Workflow Foundation: Final Release Gate

| Item | Value |
| --- | --- |
| Document | Epic 11100 final QA / release gate |
| Gate date | 20 August 2026 |
| Scope assessed | Jira 11101–11108, plus 11100.0, glue, fakes, shell, recovery |
| Authorities | MVP Design v1.0 (Confirmed); Epic 11000 final report; Epic 11100 Plan §23 Definition of Done |
| Repository | `D:\Repositories\printflow-Studio`, branch `master` |

---

## 1. Verdict

**`EPIC 11100 PASS WITH NOTES — READY FOR EPIC 11200`**

Every core foundation invariant holds and is proven by automated coverage plus a real-window
pass on the workstation. One defect was found by the gate and fixed (§15). Three items are
recorded as notes; none blocks Epic 11200 (§18–§20).

---

## 2. Jira completion matrix

| Task | Verdict | Evidence |
| --- | --- | --- |
| **11101** Solution & build substrate | **PASS** | Five projects with the §5 dependency directions, proven by `DependencyRuleTests`. `TreatWarningsAsErrors=true` in `Directory.Build.props`; clean rebuild is 0 warnings / 0 errors. Central package management with lock files; `.editorconfig`, `nuget.config`, `.gitignore`, `.gitattributes` tracked. |
| **11102** Domain types | **PASS** | Immutable records/value objects under `PrintFlow.Domain`; zero `PackageReference` (`DependencyRuleTests.Domain_references_no_third_party_package`). `ScopeGuardTests.No_excluded_concept_is_declared_as_a_type` proves no `Job`/`Order`/`Customer`/`Asset`/`Artwork`/`User`/`Role`/`Batch` type exists. `ValueObjectTests` (incl. `PrintDimensions` 300 dpi derivation and preset nominals). |
| **11103** Workflow definitions | **PASS** | `WorkflowCatalog` holds exactly the three fixed workflows; `WorkflowShapeTests` asserts each shape against MVP design §6.2–§6.4. Verified visually in the real window (§16, screenshot 02): all three, correct step lists, correct skippable/review flags. |
| **11104** Workflow engine | **PASS** | Pure reducer; `EnginePurityTests` proves determinism, no mutation of input, time and ids from `CommandContext`, and effects returned as data. `TransitionMatrixTests` covers every `StepState × CommandKind` pair with no fall-through; `InvalidTransitionTests` pins the refusals. `ScopeGuardTests` proves no arbitrary state setter and no UI-constructible system command. |
| **11105** Immutability & hash-bound approval | **PASS** | `RevisionIntegrityGuard` re-hashes before every consuming command. `SessionServiceTests.Mutating_an_approved_file_on_disk_invalidates_it_and_refuses_consumption` and `SessionControlsTests.Approving_a_file_that_changed_after_it_was_displayed_is_refused_and_does_not_advance`. DB triggers enforce Revision identity immutability and `ReviewDecision` append-only (`DbInvariantTests`). Descendant invalidation without sibling damage: `ReturnToStep_invalidates_the_descendant_but_the_returned_to_revision_itself_stays_valid`, `AddAnotherSizeTests` (3). |
| **11106** File inspector & workspace | **PASS** | `FileInspectorTests` (12): magic-byte format detection, dimensions/DPI/alpha, SHA-256 known vector, missing/locked/empty handled. `WorkspaceTests` (15): source never modified, read-only byte-identical snapshot, per-attempt working directories, containment guard, `Baseline\` and `TestData\` writes refused, RecycleBin with no hard-delete fallback. |
| **11107** Naming | **PASS** | `SanitiserTests`; `WorkspaceTests.Collision_reservation_yields_base_then_02_then_03_and_never_overwrites` and `Chinese_output_names_survive_reservation`. Patterns come from the verified preset (`WorkstationPresetProviderTests`), never hard-coded — confirmed live: the produced file was named `qa-gate-design_200mm_CMYK_W.tif` from the preset's `productionTiffPattern`. |
| **11108** SQLite persistence | **PASS** | `MigrationTests`: transactional apply from empty, idempotent re-run, newer `user_version` fails closed, pragmas applied per connection. `SessionServiceTests.Repository_commit_is_all_or_nothing_a_mid_batch_failure_persists_nothing_from_that_batch` proves one command → one transaction. `Restart_and_reload_restores_a_structurally_identical_snapshot`. Metadata only — no image bytes in any column. |

No task is FAIL.

---

## 3. Build and test results

```text
git status -sb        master...origin/master [ahead 3], working tree clean
dotnet --version      10.0.400
dotnet restore --locked-mode   PASS (all 5 projects, lock files honoured)
dotnet build                   0 Warning(s), 0 Error(s)
dotnet test                    5546 passed, 0 failed, 0 skipped
```

Re-run after the §15 fix: identical (5546 / 0 / 0, 0 warnings). No test was modified, disabled
or weakened at any point in this gate.

---

## 4. Workflow end-to-end gate

All three workflows drive to `Completed` against the real object graph, real workspace, real
SQLite, real file inspector and the deterministic fakes, on synthetic files only.

| Workflow | Path proven | Evidence |
| --- | --- | --- |
| **PREPARE_ASSET** (run-every-step) | Import → Confirm → Enhance → Review → BackgroundRemoval → Review → Trim → Review → ApprovedPngExport → Complete | `SessionServiceTests.Full_walkthrough_reaches_Completed_with_a_real_approved_png_on_disk` — ends with a real approved PNG under `Approved\` |
| **PREPARE_ASSET** (skip path) | Confirm → Skip → Skip → Trim → ApprovedPngExport → Complete | `DimensionsW1AndOutputTests.Prepare_asset_completes_through_approved_png_export_without_dimensions_or_a_branch` (through the UI, to `Completed`); `SessionSmokeTests.Smoke_C` proves the skips create no Revision |
| **PREPARE_CUSTOMER_DESIGN** | Import → Confirm → fake Enhance → fake BackgroundRemoval → Trim → PrintDimensions → explicit W1 → fake PhotoshopOutput → Review → Complete | `SessionSmokeTests.Production_smoke_C` — every step run (none skipped), through the real composed application graph |
| **GENERATE_PRINT_TIFF** | Import → Original Confirmation → PrintDimensions → explicit W1 → fake PhotoshopOutput → Review → Complete | `DimensionsW1AndOutputTests.Generate_print_tiff_runs_from_import_to_completed_without_meitu_or_trim`; `SessionSmokeTests.Production_smoke_A`; and live in the real window (§16) |

**No Meitu or Trim attempt in GENERATE_PRINT_TIFF** is asserted three ways: the Meitu port is
wrapped in a counting decorator and `CallCount` is 0; no persisted attempt exists for
`Enhancement`, `BackgroundRemoval` or `Trim`; and the workflow contains no `Trim` step at all.
Counting at the seam matters — it does not rely on the assumption that an attempt row always
precedes an adapter call.

---

## 5. Failure and retry gate

| Requirement | Evidence |
| --- | --- |
| fake failure → Failed → Retry → fresh `AttemptId` → fresh Working directory → success | `RetryAndReviewTests.Retry_after_a_fake_failure_gets_a_fresh_attempt_and_working_directory_then_succeeds` |
| success → Reject → RetryRequired → Retry → new Revision → Approve | `RetryAndReviewTests.Reject_then_retry_keeps_the_rejected_Revision_audit_visible_and_produces_a_distinct_approved_one`; `SessionSmokeTests.Smoke_B` through the composed graph |
| audit history survives | The rejected Revision remains on record and valid in itself; both `ReviewDecision` rows persist (Smoke B asserts 2 reviews for the step). `DbInvariantTests` proves `ReviewDecision` update and delete are both rejected at the database. |
| every failure mode | `FakeAdapterScenarioTests` (5): explicit failure, timeout, missing output, unreadable output, hang-until-cancelled — each leaves no Revision and releases the automation lock |

Retry provably never reuses a failed attempt's working copy: the new Revision's path contains
the new `AttemptId` and not the old one.

---

## 6. Integrity gate

`SessionServiceTests.Mutating_an_approved_file_on_disk_invalidates_it_and_refuses_consumption`
approves a Revision, flips one byte on disk, then attempts downstream consumption:

- result is `FailureCode.RevisionIntegrityMismatch`;
- the Revision is marked invalid with `InvalidationReason.FileMutated`;
- the original approval decision remains in the audit history untouched;
- the invalid Revision cannot be consumed — the guard re-hashes on every consuming command, so
  the refusal repeats rather than being a one-off.

**No next adapter call** holds structurally, not just by absence of a row: `EnsureIntegrityAsync`
runs in `SessionService.ExecuteAsync` before `_engine.Apply`, and the attempt record and adapter
invocation both live inside `RunAdapterBackedStepAsync`, which is only reachable after an
accepted transition. An integrity failure therefore precludes both by construction.

The same rule is proven at the UI layer by
`SessionControlsTests.Approving_a_file_that_changed_after_it_was_displayed_is_refused_and_does_not_advance`.

---

## 7. Startup and recovery gate

`StartupRecoveryTests` (8):

| Requirement | Evidence |
| --- | --- |
| Attempt → Interrupted, no Revision fabricated | `A_crashed_Running_attempt_recovers_to_Interrupted_and_fabricates_no_Revision` |
| Step Interrupted where legal | same test; `RecoveryAndBranchTests` confirms Interrupted offers Retry/Skip/HandOff and that a non-skippable step still cannot be skipped as an escape |
| stale confirmed lock released | `A_lock_whose_owner_is_dead_is_released` |
| a live or unverifiable owner is never stolen from | `A_lock_whose_owner_is_alive_or_unverifiable_is_never_stolen` (Alive, Unknown) — `ProcessLivenessTests` (8) covers cross-machine, recycled-PID and missing-identity claims |
| orphan Working file quarantined safely | `A_partial_file_left_by_a_crash_is_quarantined_and_protected_areas_are_untouched` |
| retry creates fresh Attempt and Working directory | `Retry_after_recovery_gets_a_new_attempt_and_a_new_working_directory` |
| **idempotent — run twice** | `Running_recovery_twice_changes_nothing_the_second_time`; and `Recovery_against_a_cleanly_shut_down_workspace_does_nothing` |

Ordering is enforced and tested: `Migrations_are_applied_before_recovery_reads_persistence`,
`A_successful_startup_calls_startup_recovery_exactly_once`, and
`Startup_recovery_is_invoked_from_exactly_one_place_in_the_product`. Recovery failure stops
startup rather than proceeding into a half-usable shell.

Confirmed live in the real window: `RecoveryExecuted=True`, `IsNoOp=True`, and the operator
message rendered on Home as *"Startup recovery found nothing to recover."* / *"启动恢复未发现需要恢复的内容。"*

---

## 8. Single-instance gate

Automated: `SingleInstanceGuardTests` (4) — first acquires, second refused while held, owner
re-acquisition idempotent, released only by disposal. `ApplicationStartupTests` adds
`A_second_instance_is_refused_and_never_runs_recovery` and
`An_unevaluable_guard_refuses_startup_rather_than_claiming_a_second_instance` (a denied lock path
is an environment fault, never reported as "already running").

**Manual, with the real guard:** the §16 window pass ran the genuine `SingleInstanceGuard`
(`%LOCALAPPDATA%\PrintFlow Studio\printflow-studio.instance.lock`). While the first instance held
the guard and its window was open, a second guard was constructed and asked to acquire:

```text
SecondInstanceOutcome=AlreadyRunning
```

The first instance was unaffected and continued through the full journey. A refused instance
receives no service container at all, so it cannot reach an adapter, a repository or a session
even accidentally.

---

## 9. Resume gate

`SessionServiceTests.Restart_and_reload_restores_a_structurally_identical_snapshot` builds a
second, independent service instance over the same database and asserts `WorkflowSnapshot` value
equality (element-wise over the step list, not reference equality).

`HomeAndWorkflowSelectionTests.Resume_restores_the_session_from_the_database_after_a_restart`
does it through the operator path: progress a session, recreate Home and its services, refresh
Recent Processing, Resume. `SessionControlsTests.Going_back_to_home_changes_nothing_and_home_shows_the_persisted_state`
confirms the resumed screen shows the persisted mid-review state down to the hash it had been
displaying.

Confirmed live: after Back to Home, Recent Processing listed the session with its correct
workflow, state and current step (§16).

---

## 10. AddAnotherSize gate

| Requirement | Evidence |
| --- | --- |
| A approved → Complete → AddAnotherSize → B | `AddAnotherSizeTests.Approving_the_second_size_leaves_both_outputs_valid_and_approved`; `DimensionsW1AndOutputTests.A_second_size_is_produced_alongside_the_first_and_both_are_displayed`; `SessionSmokeTests.Production_smoke_B` |
| A remains valid, B independent | both outputs valid and approved; they share a source Revision as siblings rather than descending from one another; each carries its own explicitly chosen branch (A = `W1_1px`, B = `W1_2px`) |
| rejecting B does not invalidate A | `AddAnotherSizeTests.Rejecting_the_second_size_leaves_the_first_output_untouched`; `DimensionsW1AndOutputTests.Rejecting_the_second_size_leaves_the_first_output_valid_and_approved` (also asserts the operator can still see A intact on screen) |
| upstream invalidation invalidates both when appropriate | `AddAnotherSizeTests.Returning_upstream_of_the_shared_source_invalidates_both_dependent_outputs` — the root itself stays valid; both dependants do not |

Reopening clears both production decisions, so the next output makes its own: confirmed live —
after Add Another Size, `W1Selected=NONE`, `ConfirmedW1=Not chosen`, and Output A remained listed
on screen.

---

## 11. W1 gate

| Requirement | Evidence |
| --- | --- |
| no default branch | `SelectedWhiteUnderbaseChoice` starts null and is never assigned a starting value anywhere in the codebase. `The_white_underbase_selector_offers_all_three_branches_with_none_chosen`. Live: `W1Selected=NONE`, `ConfirmedW1=Not chosen`. |
| no hidden fallback | pressing Confirm with nothing selected sends no command and persists nothing (asserted). `EnginePurityTests.SelectWhiteUnderbaseBranch_probing_never_leaves_a_branch_behind` proves even repeated `AvailableCommands` probing — which must name some branch to ask its question — leaves the session's branch null. |
| 0px / 1px / 2px all selectable and persisted | `Each_white_underbase_branch_persists_exactly_as_chosen` (3 cases). Live labels: *0 px — fine details*, *1 px — ordinary artwork*, *2 px — solid / full rectangular artwork*. |
| PhotoshopOutput unavailable until the branch is explicitly selected | `Run_step_is_withheld_until_dimensions_and_the_branch_are_both_recorded` and `Photoshop_output_stays_unavailable_when_only_the_size_was_recorded` — the latter reaches the PhotoshopOutput step with a size but no branch and confirms the engine still refuses `StartStep` |
| never inferred from image content | nothing in the solution reads pixels to classify. The branch is a non-nullable input to `PhotoshopRequest` and a precondition in `WorkflowEngine.StartStep`; `InvalidTransitionTests.PrepareAsset_has_no_white_underbase_decision_to_make` pins the negative case |

Payload validation was not weakened to make probing possible: a blank justification is still
`InvalidPayload` after probing.

---

## 12. File and source safety gate

| Requirement | Evidence |
| --- | --- |
| original source unchanged | `WorkspaceTests.Import_never_modifies_the_operators_source_file`, `Failed_import_leaves_the_source_untouched` |
| snapshot read-only | `Snapshot_is_byte_identical_to_the_source_and_read_only` |
| retries use new Working directories | `Every_attempt_gets_its_own_working_directory_and_retry_never_reuses_it` |
| no silent overwrite | `Collision_reservation_yields_base_then_02_then_03_and_never_overwrites` |
| protected Baseline/TestData untouched | `Write_into_Baseline_is_refused`, `Write_into_TestData_is_refused`; containment guard rejects traversal at construction |
| no hard-delete fallback | `RecycleBin_has_no_hard_delete_fallback_on_failure` |
| invalidation does not delete audit history | invalidation flips `IsValid` and records a reason; `DbInvariantTests` proves `ReviewDecision` rows can be neither updated nor deleted, and Revision identity columns cannot be updated |
| cleanup is surgical | `Cleanup_removes_only_Working_and_preserves_Source_and_Approved` |

The one delete in the repository is the workflow re-shape step-row removal added in Part 3C3B;
it touches no Revision, attempt or review, so the record of what was actually done survives a
change of workflow.

---

## 13. Architecture gate

| Requirement | Result | Evidence |
| --- | --- | --- |
| Domain has no file/database/UI dependency | **PASS** | zero third-party references; independent grep for `System.IO`, `Sqlite`, `System.Windows`, `Dapper` in `PrintFlow.Domain` returns nothing |
| Workflow has no direct file I/O | **PASS** | `BannedApiEnforcementTests.Workflow_source_contains_no_System_IO_usage`; independent grep finds only a doc comment |
| Workflow does not reference Infrastructure | **PASS** | `DependencyRuleTests.Workflow_references_Domain_only` |
| UI executes no SQL | **PASS** | `DependencyRuleTests`; independent grep of `ViewModels/` for SQL verbs, Sqlite and Dapper returns only comments |
| UI does not call adapters directly | **PASS** | no adapter type is named anywhere under `ViewModels/`; every action goes through `ISessionService.ExecuteAsync` |
| UI cannot fabricate system-success commands | **PASS** | `ScopeGuardTests.System_commands_expose_no_public_constructor` — `AttemptSucceeded`/`Failed`/`Interrupted` constructors are internal to `PrintFlow.Workflow` |
| adapter execution passes `IEnvironmentGate` | **PASS** | `SessionService.RunAdapterBackedStepAsync` verifies the gate **before** the automation lock and any file work; `EnvironmentGateTests.SessionService_refuses_a_Production_mode_adapter_before_it_is_ever_invoked` proves the adapter is never called |
| no Job/Order/Customer scope creep | **PASS** | `ScopeGuardTests.No_excluded_concept_is_declared_as_a_type` (10 forbidden names) |
| no real Meitu/Photoshop/Maintop automation | **PASS** | independent scan for `Process.Start`, `ShellExecute`, `SendKeys`, `user32`, `FindWindow`, `SetForegroundWindow`, COM `ProgID`, `.atn` reading and `Photoshop.exe` finds no operative call anywhere. `ServiceRegistration` throws rather than starting if `Adapters:Mode` is `Production`, and `FoundationEnvironmentGate` refuses every Production adapter with `EnvironmentNotVerified`. |

The UI never restates a workflow rule: control availability is read from the engine's own
`AvailableCommands`, and two tests compare the screen's answers against the engine's for every
state the slices reach.

No test was weakened to pass this gate.

---

## 14. Dependency audit

```text
dotnet list package --vulnerable --include-transitive
```

| Field | Finding |
| --- | --- |
| Advisory | GHSA-2m69-gcr7-jv3q |
| Package | `SQLitePCLRaw.lib.e_sqlite3` **2.1.11** |
| Severity | **High** |
| Status | **Transitive** — pulled by `Microsoft.Data.Sqlite` 10.0.0 → `SQLitePCLRaw.bundle_e_sqlite3` 2.1.11. Not a direct reference. |
| Affected projects | `PrintFlow.Infrastructure`, `PrintFlow.App`, `PrintFlow.Tests`. `PrintFlow.Domain` and `PrintFlow.Workflow` are clean. |
| Currently suppressed | Yes — `NuGetAuditSuppress` in `Directory.Build.props`, with a written risk assessment |

**The recorded suppression rationale is now out of date.** It states that "NuGet's advisory record
lists no patched SQLitePCLRaw version, and every SQLitePCLRaw release compatible with net10.0
restores to the same flagged transitive version, so pinning around it is not possible." That was
checked against live NuGet during this gate, in throwaway probe projects outside the repository,
and it no longer holds:

| Probe | Result |
| --- | --- |
| `SQLitePCLRaw.lib.e_sqlite3` latest → **3.53.3** | audit clean (major-version line; not proposed) |
| `SQLitePCLRaw.lib.e_sqlite3` **2.1.12** pinned directly | **audit clean** |
| `Microsoft.Data.Sqlite` latest → **10.0.11** | pulls the whole SQLitePCLRaw family at **2.1.12**; **audit clean** |

**A compatible patched graph is therefore available**, and it is a patch-level bump within the
same minor line (`Microsoft.Data.Sqlite` 10.0.0 → 10.0.11), not a major-version migration.

Per the gate's instruction, no upgrade was applied in this QA task. The exploitability assessment
in the suppression remains sound for this deployment — the database is single-operator and
local-only, and every statement is fixed hand-written SQL behind the repository, so no
attacker-controlled query reaches SQLite. The advisory is consequently **not blocking**.

**Recommendation (note 3, §20):** as the first action of Epic 11200, bump `Microsoft.Data.Sqlite`
to 10.0.11, delete the `NuGetAuditSuppress` entry, and re-run the suite. If anything about that
bump proves unexpectedly disruptive, the fallback is to correct the suppression comment so it
states the true current position rather than the stale one.

---

## 15. Defect found and fixed by this gate

**The session screen told the operator that features it was displaying did not exist.**

`Session_PlaceholderNotice` still read *"Print dimensions, white underbase and session completion
are not part of this build."* Part 3C3B added all three. The notice therefore sat directly above a
working dimensions panel and a working W1 selector, and remained on screen while the operator
used them and pressed Complete — in both English and zh-CN.

No automated test could have caught this: every test asserts behaviour, and the rendering tests
assert only that bindings resolve, not that the prose is true. It was found by reading the real
window as an operator sees it, which is exactly what §17 of the gate exists for.

Fix: retargeted both resource values to what is genuinely still absent — image preview,
side-by-side comparison, returning to an earlier step, and the processing-history browser.
Resource values only; no code, no behaviour change. Re-verified in a fresh real-window pass in
both languages. Commit `a8d2c76`.

---

## 16. Real-window manual pass

Performed for real, not simulated. A throwaway harness in the scratchpad (never in the
repository) launched the **real composed application graph** through `ApplicationStartup` with the
**real `SingleInstanceGuard`**, created the real `MainWindow`, called `Show()`, and drove every
screen the gate names, capturing the live window at each stage.

```text
CanShowShell=True   IsPrimaryInstance=True   PresetVerified=True   RecoveryExecuted=True
WindowShown IsVisible=True  ActualWidth=1000  ActualHeight=700  Title=PrintFlow Studio
SecondInstanceOutcome=AlreadyRunning
```

Run twice end to end — once forced to `en-US`, once to `zh-CN` (the workstation's own OS UI
culture is `zh-CN`). Ten screens captured per run, no exception, no binding failure.

| Screen | Result |
| --- | --- |
| Home | correct; startup-recovery message and preset-verified line both shown |
| Workflow Selection | all three workflows with correct step lists and skippable/review flags |
| Session screen | identity, step list, current-file metadata (name only, no path) |
| Review state | Approve / Reject / Hand off offered; output listed as *Not reviewed* |
| Dimensions panel | four preset buttons, mm boxes, live pixel preview `200 × 150 mm (2362 × 1772 px at 300 dpi)` |
| W1 panel | three branches, **nothing pre-selected**, guidance text present |
| Completed / output list | `qa-gate-design_200mm_CMYK_W.tif` · 200 × 150 mm · 1 px — ordinary artwork · Approved · Valid |
| Add Another Size | offered only when completed; reopened with both decisions cleared and Output A still listed |
| Startup recovery message | *"Startup recovery found nothing to recover."* / *"启动恢复未发现需要恢复的内容。"* |
| Chinese localisation | every operator-visible string translated; no raw resource keys; no layout breakage |

The file name came from the verified preset's `productionTiffPattern`, confirming naming is
preset-driven rather than hard-coded. Synthetic files only — a generated 64×48 gradient PNG. The
harness used its own temp workspace; `D:\PrintFlowStudio` was never written to.

Two observations, neither a defect:

- The fake TIFF reports `Format PNG` in the metadata panel, because the fake adapter copies a PNG
  and names it `.tif`. That is honest for fake mode and consistent with the warning shown beside
  it; a real TIFF is Epic 11400.
- At the default 1000×700 window the right-hand column scrolls, so the rejection-reason control
  can sit below the fold. The baseline workstation is 1920×1080, where there is more room. Minor
  UX observation for later polish.

---

## 17. Git and privacy gate

Working tree clean. 179 tracked files; extensions are `.cs`, `.md`, `.json`, `.xaml`, `.csproj`,
`.resx`, `.props`, `.sql`, `.sln`, `.gitignore`, `.gitattributes`, `.editorconfig`, `.config`.

Scanned for and confirmed **absent** from tracking: customer images, any `.png`/`.jpg`/`.tif`/`.psd`/`.pdf`,
runtime SQLite databases, generated TIFFs, `.atn` files, the real signed preset JSON, sign-off
JSON, screenshots, and logs.

The tracked `appsettings.json` carries the preset's *path* and *expected SHA-256* — a verification
value, not the artefact; the manifest itself lives outside the repository under
`D:\PrintFlowStudio\Baseline\`. The tracked Epic 11000 report records accepted hashes, which is
its purpose. Neither is customer data.

The only production-looking strings in source are naming *patterns* (`{0}_HD.png`,
`{0}_CUTOUT.png`) and a synthetic Chinese test name (`客户设计_HD.png`) used to prove Chinese
characters survive reservation.

`.gitignore` remains deny-by-default with narrow, synthetic-only re-inclusions.

The QA gate's own screenshots and harness were written to the session scratchpad, never to the
repository.

---

## 18. Epic 11000 evidence integrity

Re-hashed read-only, before and after the gate. All three match the accepted values, with their
original August 2026 last-write timestamps intact:

| Artefact | SHA-256 | Result |
| --- | --- | --- |
| `preset\printflow-workstation-v1.0.0.json` (16,059 B) | `A114B5D2…918383A6` | **MATCH** |
| `signoff\workstation-preset-v1.0.0.json` (2,143 B) | `49225D94…BA9C8A7A` | **MATCH** |
| `actions\authoring\PrintFlow-DTF-v1.atn` (1,636 B) | `A04203ED…BFCD83EE` | **MATCH** |

Nothing under `D:\PrintFlowStudio\Baseline` was created, modified or deleted. No Epic 11000
discovery was rerun.

---

## 19. Remaining UX gap decisions

### A. ReturnToStep UI — **NON-BLOCKING**

The Definition of Done (plan §23) names `ReturnToStep` in exactly one place, under *Revision,
attempt, approval*:

> `ReturnToStep` invalidates the full descendant set and no siblings.

That is an engine and invalidation requirement, and it is met —
`SessionServiceTests.ReturnToStep_invalidates_the_descendant_but_the_returned_to_revision_itself_stays_valid`,
`AddAnotherSizeTests.Returning_upstream_of_the_shared_source_invalidates_both_dependent_outputs`,
and `InvalidTransitionTests.ReturnToStep_refuses_a_step_that_is_not_upstream`.

The DoD's *Shell and adapters* section lists the operator surface required, in full:

> start → import (single file only) → choose workflow → inspect state → run fake steps →
> approve/reject/skip/retry → close → reopen → resume

Operator rewind is not in that list. Epic 11100 is the *foundation*: the rule exists, is
enforced, is persisted and is tested; exposing it is UX work with real design questions
(confirming destructive invalidation, showing what will be lost) that belong with the review and
preview surfaces in Epic 11200.

Recorded as a note for later UX work. The operator is not stranded meanwhile: Retry, Skip and
Hand off all remain available, and Hand off is the designed escape when automation cannot
proceed.

### B. Fake-scenario selector UI — **NON-BLOCKING**

The DoD requires:

> Fake adapters write real files and can be scripted to succeed, fail, time out, hang, or produce
> unreadable output.

"Scripted" — which `FakeMeituProcessor.SetScenario` / `FakePhotoshopOutputProcessor.SetScenario`
provide, exercised by `FakeAdapterScenarioTests` across all five modes. Nothing in the DoD asks
for an operator-facing selector, and one would be actively undesirable in production: a control
that makes processing fail on purpose has no place on an operator's screen.

This is developer and test tooling. Not required for Epic 11100 completion; not implemented.

---

## 20. Notes carried forward

1. **ReturnToStep has no operator surface** (§19A). Non-blocking; Epic 11200 UX.
2. **No fake-scenario selector in WPF** (§19B). Non-blocking; deliberately test-only.
3. **`SQLitePCLRaw` High advisory is suppressed with a now-stale rationale, and a patched graph is
   available** (§14). Non-blocking given the deployment's risk profile, but the suppression text
   should not be left asserting something untrue. Recommended as the first action of Epic 11200.
4. **Banned-symbol enforcement is a source scan, not an analyzer.** The plan named
   `BannedApiAnalyzers` and `BannedSymbols.txt`; Part 1 deliberately substituted a test-time
   source scan because the analyzer would require a `PackageReference` in `PrintFlow.Domain`,
   which the same plan forbids. The substitution is documented in
   `BannedApiEnforcementTests`. One narrow gap: the scan covers `System.IO` but not
   `DateTime.Now`. There are currently **zero** uses of `DateTime.Now` anywhere, and the engine's
   time-from-context rule is proven by `EnginePurityTests`, so this is a missing regression guard
   rather than a live defect. The three `DateTimeOffset.UtcNow` uses are all in Infrastructure and
   all for audit or diagnostic stamps, never workflow decisions.
5. **PREPARE_ASSET's run-every-step completion is proven at the service layer** and its skip path
   at the UI layer, rather than both at the UI layer. The UI's Run/Approve behaviour for those
   same steps is separately proven by `SessionControlsTests` and Smoke A/B, so the coverage is
   complete in substance; noted for transparency.
6. **Right-hand column scrolling at small window sizes** (§16). Cosmetic; the baseline workstation
   is 1920×1080.

---

## 21. What Epic 11100 does not prove

Stated explicitly so nothing here is read as more than it is. Epic 11100 does **not** demonstrate:
real Meitu automation or enhancement quality; real background removal; real trim quality; real
Photoshop automation; CMYK correctness; W1 spot-channel correctness; Photoshop Action execution;
Maintop readiness; or physical DTF output.

Every result in this gate was produced by deterministic fakes writing real files through the real
validation pipeline. The application refuses to start in `Production` adapter mode, and
`FoundationEnvironmentGate` refuses every production adapter. Those capabilities belong to Epics
11300, 11400 and 11500.

---

## 22. Git state and commit SHAs

```text
Branch      master
Remote      origin  https://github.com/MQLite/printflow-Studio.git
Tracking    master...origin/master [ahead 3]
Worktree    clean
```

| SHA | Commit |
| --- | --- |
| `a8d2c76` | `11100: correct the stale deferred-scope notice on the session screen` (this gate's fix) |
| `9ceb791` | `Report: Epic 11100 Part 3C3B dimensions, W1 and output UI` |
| `8a792a9` | `11100: dimensions, W1 and production-output workflow UI` |
| `9c441d3` | `Report: Epic 11100 Part 3C3A session and review controls` — last commit on `origin/master` |

Three commits are ahead of `origin/master` and **not pushed**; this report will make a fourth.
Pushing is a normal fast-forward and remains the operator's call. No force push was performed or
is required.

---

## 23. Recommended next Epic

**Epic 11200 — Trim, review and image-comparison surfaces.**

It is the right next step because it is what the foundation was built to carry, and because the
notes above land naturally inside it:

- the deterministic alpha-bound trim algorithm and manual crop, which Epic 11100 defined as a
  workflow step but deliberately left as a placeholder;
- the image preview, side-by-side comparison and checkerboard the review panel is currently
  missing — the operator today reviews on metadata and a hash alone;
- the `ReturnToStep` operator surface (note 1), whose design questions are review-surface
  questions;
- the dependency bump in note 3, cheap to take before new work accumulates.

Epics 11300 (real Meitu), 11400 (real Photoshop and TIFF) and 11500 (environment verification)
all depend on seams Epic 11100 has already fixed in place, and can proceed in any order after
11200.

---

## 24. Final verdict

Every core foundation invariant holds: the layering is enforced, the engine is pure and
exhaustively tested, approval is hash-bound against real bytes, sources are never touched,
persistence is transactional and restores identically, recovery is idempotent, the single
instance is real, and all three workflows run end to end to `Completed` — verified both by 5,546
automated tests and by a real WPF window on the workstation in both languages.

One defect was found and fixed. Three notes and three observations are carried forward, none of
which blocks the next Epic.

`EPIC 11100 PASS WITH NOTES — READY FOR EPIC 11200`
