# Epic 11300 Part D2A — Stop, Meitu Cancel and Operator Takeover

**Verdict: 11300-D2A PASS WITH NOTES — READY FOR FORCE-TERMINATION POLICY**

Baseline entering this slice (verified, not assumed): 7467 passed, 0 failed, 0 warnings, 0 errors,
no vulnerable packages; working tree clean, `master` ahead of `origin/master` by 3.

Final gates: **8221 passed, 0 failed, 0 warnings, 0 errors, no vulnerable packages.** Nothing pushed.

This slice adds operator control *around* D1's rules. It does not redesign the runtime failure
taxonomy, structured cancellation, startup recovery, retry linkage, path isolation,
cleanup-warning persistence, Background Removal authority, the Enhancement/Background Removal
success boundaries, or the guarded process/window/foreground verification. It introduces **no**
process-termination route of any kind.

---

## 1. Stop semantics by phase

Stop is defined as *"stop this PrintFlow automated operation safely."* It never means "terminate
Meitu.exe".

The eight phases are modelled explicitly as `ExternalOperationPhase` and resolved by one pure
function, `AutomationStopPolicy.Resolve(mode, phase)`, in `PrintFlow.Workflow`. There is no
generic "cancel everything" path: a phase with no rule fails closed to *unknown*.

| § | Phase | Stop (`StopOperation`) | Retained external state |
|---|---|---|---|
| 5 | `NotStarted` — before any operation input | Cancel PrintFlow orchestration. **No** Meitu operation input, no export, no Revision, lock released. | `None` |
| 6/7 | `OperationRequested` — invoked, Busy not yet observed | **No** cancel is eligible: the cancel affordance belongs to the Busy surface and current-load correlation is absent. No input. | `OperationMayStillBeRunning` |
| 9 | `Busy` | The **only** phase in which the signed cancel may be invoked, and only for `StopOperation`. Verify operation + Busy → resolve exact signed control → final foreground/process/window guard → invoke **once** → observe exit from Busy. No retry. | `OperationCancelled` if it took effect, else `OperationMayStillBeRunning` |
| 13 | `CompletedBeforeExport` | Do not export. No Save, no Save As, no Revision. The processed result is **not** discarded. | `ProcessedResultRetained` |
| 14 | `ExportPrepared` — save surface/destination dialog open, no confirm | Back out through the **already signed** cancel/close control for that exact surface, so no modal is left blocking. No confirm, no output, no Revision. | `ProcessedResultRetained` |
| 15 | `ExportConfirmed` | PrintFlow does **not** claim it can cancel the filesystem write. It stops producing input and observes. | `OutputWriteConfirmed` |
| 16 | `OutputValidated` | Success stands. Stop cannot rewrite it. | `OutputWriteConfirmed` |
| 10/20 | `UnknownOrBlocked` | No input of any kind. Nothing is dismissed, closed or guessed at. | `Unknown` |

`AutomationStopPolicyTests` asserts every mode × phase pair (93 cases), including that the
operation cancel is eligible in **exactly one** pair out of sixteen and that a phase value the
table does not recognise fails closed.

---

## 2. Signed Meitu Cancel target

Established by three supervised **read-only** discovery runs (§6). The cancel was never clicked
during discovery — a discovery pass that also cancelled would have proven nothing and cancelled
something.

| Property | Value |
|---|---|
| Exact marker | `取消` (exact automation name, never a substring) |
| ControlType | `Button` |
| ClassName | `QPushButton` |
| AutomationId | `MainWindow.MaskDialog.MaskCenterWidget.LoadingMaskWidget.cancel` |
| Owning actionable element | **itself** — owner depth 0 |
| Owner depth rationale | Unlike `关闭图片`/`AI变清晰`, the name belongs to the button, not a label above it. A marker-to-owner walk would climb past it to `LoadingMaskWidget`, which also advertises `InvokePattern` and cancels nothing. |
| Ancestry (innermost first) | `LoadingMaskWidget` → `SpecialMaskWidget` → `MaskDialog` → `MainWindow` |
| Process ownership | Re-verified against the verified Meitu process immediately before invocation |
| Supported patterns | `Invoke`, `Value` |
| Enabled / visible | `enabled=true`, `offscreen=false` throughout |

**One control, both operations.** 抠图 and AI变清晰 raise the *same* shared `LoadingMaskWidget`
progress mask and produced the identical automation id. This is recorded rather than papered
over: signing two per-operation controls would fabricate a distinction the workstation does not
have.

**The live decoy (§8).** The open picker's own Cancel is named **exactly** `取消`
(`Button id='2' class='Button'` under `#32770` `打开图片`) and was present in every discovery run.
`FindElementByName("取消") → Invoke` would have dismissed a file dialog instead of cancelling an
operation. It is refused by the automation id and, independently, by the class.

**Eligibility (§7)** requires all three, and none substitutes for another:
1. the operation appears in the evidence's `confirmedForOperations`;
2. that operation's **own** signed Busy signature matches the same observation; and
3. the signed control resolves to **exactly one** element beneath the one verified window.

There is no `FindCancelByName`, no substring match, no "first invokable element", no coordinate,
no mouse event, no blind Escape, and no keyboard shortcut anywhere on this path —
`StopAndTakeOverBoundaryTests` asserts each of those as an absence.

Evidence: `D:\PrintFlowStudio\Baseline\workstation-v1\apps\meitu\editor-busy-cancel.json`, signed
into preset **v1.8.0** (`DE76464F…`). Not committed (§41).

---

## 3. Post-cancel state — observed, never assumed

§11 required this to be determined live. It was, and the two operations differ:

- **AI变清晰 cancelled** → returns to the **ordinary editor**. Document still loaded
  (`关闭图片`, `保存`); the ordinary `基础调整` tool list is showing; the AI变清晰 module panel is
  **still selected**; canvas at 100%, not the reduced zoom a completed upscale produces.
- **抠图 cancelled** → **remains inside the 抠图 page**, with its own controls present
  (`自动选择`, `局部抠图`, `手动修补`, `重置`, `反选`, `应用到背景`). Document still loaded.

No prompt, modal or partial-result dialog appeared in either run. **Neither is
`KnownEditorWithExpectedWorkingCopy`**, which is exactly why §11 forbids assuming it.
`MeituCancelOutcome` carries no `Succeeded`, `DocumentIntact` or `ReadyForRetry` member — asserted
structurally — so nothing can read "left Busy" as "safe to send input". The next attempt re-enters
through the ordinary readiness path and proves the state for itself.

One caveat is documented on the type: `StateAfterCancel` comes from an operation-scoped read, so
`Unknown` there means *"not this operation's Busy"*, never *"Meitu is on an unrecognisable
screen"*.

---

## 4. Late-stop success boundary

§16 is enforced by the **transition table**, not by an `if`:
`CommandKind.AttemptCancelled` is legal only from `StepState.Processing`. A step whose attempt has
succeeded is no longer `Processing`, so a late Stop cannot reach it.

Operationally the run is unregistered before `ExecuteAsync` returns, so `RequestStop` against a
finished run is **refused**. The success transaction is committed on `CancellationToken.None` when
a stop is pending, so a validated output can never end up on disk with no Revision.

`A_stop_requested_after_a_validated_success_cannot_erase_it` drives a real successful run, requests
a Stop afterwards, and asserts the attempt stays `Succeeded` with its `OutputRevisionId` intact.

---

## 5. Take Over semantics

`AutomationStopMode.TakeOver` resolves to **no input at all, from every phase** — asserted across
all eight phases. It does not click Cancel first: "take over" does not mean "cancel then take
over" (§19). From a blocking modal or an unrecognised screen it dismisses nothing, which is
precisely why §20 permits a takeover where a Stop-with-cancel is refused.

Live: the Take Over smoke produced **zero** Meitu input, Meitu kept running (same pid), the
enhancement completed on its own, and the operator owned it.

---

## 6. HandedOff and re-entry

The existing `SessionState.HandedOff` is the authority; **no second "manual mode" state** was
created. Audit of the existing behaviour first: `HandOff` was legal only from `ReviewRequired`,
`RetryRequired`, `Failed` and `Interrupted`, leaves the step's own state untouched, releases the
automation lock, and its `CreateWorkingCopy`/`OpenForManualWork` effects have no interpreter — so
it performs no file work and no external input.

A takeover therefore composes two existing things rather than inventing one:

1. `System.AttemptCancelled` closes the running attempt → attempt `Cancelled`, step `Interrupted`,
   lock released;
2. the ordinary `HandOff` command is applied to the state that produced, from `Interrupted`, where
   it was already legal → session `HandedOff`.

**Chosen state mapping (documented as §12/§19 require):** attempt `AttemptStatus.Cancelled`
("a human ended this attempt"), step `StepState.Interrupted` ("did not finish, produced nothing").
`Cancelled` is deliberately distinguished from `Interrupted`-the-attempt-status, which startup
recovery writes for a crash: "the computer stopped" and "a human stopped it" stay separable.
Reusing `Interrupted` for the *step* means a crash **during** a stop lands the step in the same
place a completed stop would — no duplicate recovery state (§31).

**Re-entry (§22)** is explicit and unavoidable. `SessionStateRules.AllowsProgress` already
admitted only `Active`, so every progression command is refused while `HandedOff`. The new
`ReenterAutomation` command is the *only* thing that lifts it: it returns the session to `Active`
and the step to `Waiting`, **starts nothing**, creates no attempt and no Revision. The operator
then presses Run Step, and the ordinary `StartStep` path produces a **new** attempt from a fresh
working copy with normal safe-state verification.

**Nothing is adopted (§23).** There is no folder scan, no filename inference and no payload naming
a file anywhere on the re-entry path. The handed-off attempt row is closed history and is never
rewritten — asserted field by field after a restart.

---

## 7. Retained external state

Reported honestly and never optimistically. `RetainedExternalState` has **no** member meaning
"safe" or "finished", because PrintFlow cannot establish either once it has stopped looking.

`None` · `Unknown` · `OperationMayStillBeRunning` · `OperationCancelled` ·
`ProcessedResultRetained` · `OutputWriteConfirmed`

`OperationCancelled` is reachable **only** when a signed cancel was positively invoked *and* the
operation left Busy — asserted for every phase. The operator-facing warning is absent after a
clean cancel and present otherwise, and its wording is guarded: no stop message may contain
"safely closed", "has finished", "no longer running" or "nothing further to do".

---

## 8. Audit

No migration was required. `AttemptStatus.CANCELLED` was already in the schema's CHECK constraint
and in the mappers, and `OperationFailure.Context` is already persisted whole as the attempt's
`FailureDetailJson`.

The six §29 cases are distinguishable from the persisted row:

| Case | How |
|---|---|
| user requested Stop | `stopRequested=true`, `stopMode=StopOperation` |
| Meitu Cancel positively invoked | `meituCancelInvoked=true` (+ `meituLeftBusy`) |
| Stop requested but Cancel unavailable | `stopRequested=true`, `meituCancelInvoked=false` |
| operator Takeover | `stopMode=TakeOver` |
| external process may still be running | `retainedExternalState=OperationMayStillBeRunning`, `operatorActionRequired=true` |
| external process gone / nothing retained | `retainedExternalState=None` or `OperationCancelled` |

The keys are written and read in one file (`AutomationStopAudit`) so the two halves of the
contract cannot drift; a round-trip test covers every mode × phase pair. `FailureCode.Cancelled`
alone is **not** read as an operator stop — a shutdown carries it too — so the explicit
`stopRequested` key is required.

---

## 9. Restart

- **After Take Over:** the session reloads `HandedOff`; `CanContinueProcessing` is false;
  `StartStep` is refused; the runtime registry is `Idle`. Startup recovery produces **no**
  `AttemptInterrupted` entry, because the takeover already closed the attempt — there is no
  `Running` row to recover, so no duplicate state is created.
- **After Stop:** the attempt is closed `Cancelled` and the step `Interrupted`; there is no
  `Running` attempt at restart, and Retry is available through the ordinary workflow.
- **If orchestration died before closing metadata:** D1's startup `Running → Interrupted` recovery
  remains authoritative and untouched.

---

## 10. Automation lock

Released on every stopping path — `AttemptCancelled` emits `ReleaseAutomationLock`
unconditionally, unlike `AttemptFailed` which first asks whether the step is adapter-backed. A
stopped run is over however it was performed.

Verified for: clean Stop before invoke, Meitu Cancel success, Meitu Cancel unavailable, Take Over,
and after restart.

Surrendering the lock creates **no shortcut**: a handed-off session still cannot start another run,
because the engine refuses progression regardless of the lock. §32's real requirement — "the next
automation attempt must still pass normal safe-start inspection" — is asserted directly.

The in-process `AutomationRunRegistry` is **not** the automation lock and is asserted not to touch
it: the lock is persisted and machine-wide and is what startup recovery reasons about; the registry
is in-process and disappears with the process.

---

## 11. UI and localisation

Two controls, never one ambiguous button (§27):

| | EN | zh-CN |
|---|---|---|
| Stop | `Stop` | `停止` |
| Take Over | `Take Over in Meitu` | `在美图秀秀中手动接管` |
| Re-entry | `Return to automation` | `恢复自动处理` |

Both are bound to the workflow layer's own `CanStopAutomation` / `CanTakeOverAutomation`. **Neither
is bound to `IsBusy`** — `IsBusy` is true for every command the screen issues, including an
approval, and a Stop offered beside a review decision would offer to stop nothing. No step-state
logic is duplicated in XAML. Take Over is additionally gated on the run actually driving an
external application, so a deterministic trim offers Stop but not Take Over.

The confirmation (§26) states all four consequences — automation stops, Meitu is left as it is,
this attempt produces no Revision, returning needs an explicit re-entry — and each is asserted by
name. It does not claim the external application is safe or finished.

16 new strings, full en-US/zh-CN parity, guarded by name so both files cannot lose one together.
All three new states render at **1000×700** with zero WPF binding errors.

**Human visual inspection: not performed.** The rendering is asserted programmatically (measure,
arrange, binding-error trace escalated to error) but no human looked at the screens. Reported
honestly per §37.

---

## 12. Tests

+754 tests (7467 → 8221). All 19 items of §36 are covered:

| § | Test |
|---|---|
| 1 | `Stop_before_the_operation_is_invoked_records_no_operation_input` |
| 2 | `A_stop_during_enhancement_busy_invokes_the_signed_cancel_exactly_once` |
| 3 | `A_stop_during_background_removal_busy_invokes_the_signed_cancel_exactly_once` |
| 4 | `The_file_pickers_own_cancel_is_never_invoked`, `Two_structurally_valid_cancels_are_refused`, `A_missing_cancel_control_produces_no_input`, `A_disabled_cancel_control_produces_no_input`, `A_cancel_outside_the_signed_ancestry_is_refused`, `A_cancel_of_the_wrong_class_is_refused`, `An_unsigned_cancel_is_never_invoked` |
| 5 | `Losing_the_target_before_the_cancel_produces_no_input`, `A_cancel_belonging_to_another_process_is_refused` |
| 6 | `A_stopped_attempt_is_cancelled_and_creates_no_revision`, `A_stopped_background_removal_attempt_creates_no_revision` |
| 7 | `A_stop_after_completion_and_before_export_reports_a_retained_result`, `After_completion_and_before_export_the_result_is_reported_as_retained` |
| 8 | `Before_the_export_confirm_the_signed_dialog_cancel_is_permitted` |
| 9 | `A_stop_requested_after_a_validated_success_cannot_erase_it`, `A_validated_output_is_preserved`, `No_other_phase_claims_to_preserve_success` |
| 10 | `A_take_over_never_permits_any_input` (all 8 phases) |
| 11 | `Take_over_ends_the_attempt_and_hands_the_session_to_the_operator` |
| 12 | `Take_over_from_an_unknown_or_blocked_screen_records_unknown_state`, `A_blocking_modal_over_the_operation_stops_the_cancel_and_dismisses_nothing` |
| 13 | `A_restart_after_take_over_does_not_resume_automation` |
| 14 | `Re_entry_creates_a_new_attempt_and_leaves_the_handed_off_one_immutable` |
| 15 | same test — field-by-field immutability after restart |
| 16 | `Stopping_releases_the_global_automation_lock`, `Releasing_the_lock_does_not_let_a_handed_off_session_start_another_run` |
| 17 | `The_enhancement_happy_path_is_unchanged` |
| 18 | `The_background_removal_happy_path_is_unchanged` |
| 19 | `D2A_introduces_no_force_process_termination_API`, `The_stop_vocabulary_names_no_termination_mode` |

Every refusal test asserts the invocation list is **empty**, not merely that a failure code came
back: a code proves PrintFlow reported a problem, a zero invocation count proves it did not click
something and report the problem afterwards.

---

## 13. Live Stop smoke (§33)

Supervised, synthetic images only, controlled workspace under the OS temp directory. Meitu was
never killed.

**AI变清晰:** open → invoke → positively observed Busy → operator Stop → exact signed cancel
resolved → **one** guarded invocation → Meitu positively left Busy → no export → no Revision →
retained state `OperationCancelled`.

**抠图:** identical, same control, same single invocation, Busy exited.

Two safe refusals along the way are worth recording because they are the rules working:

- A run in which **Chrome**, and later **Photoshop**, took the foreground was refused with
  `MeituTargetLost` and **nothing sent**.
- Two runs refused with *"current-load Busy correlation absent"* — see the note below.

### Note — the observation-mechanism finding

This is the one substantive thing the live runs taught, and it produced two rounds of safe
refusals before it was understood:

1. Reading the correlation with the **full Qt-tree walk** made 抠图 unstoppable: the walk takes
   longer than the cutout's entire ~1.5 s Busy window, so by the time it returned the operation had
   finished.
2. Narrowing the read to Part C1's **fast exact-name query** fixed 抠图 and silently broke
   AI变清晰: the enhancement's signed markers are *fragments* (`变清晰中` vs the on-screen
   `变清晰中，请稍候…`), so an exact-name query finds only `取消` — one marker where the signature
   requires two.

Both mistakes produced *safe* refusals — nothing was ever invoked — but they were refusals about
PrintFlow's own reading rather than about Meitu. The correlation read now dispatches **per
operation** to the mechanism that operation's own observation loop already uses. **No rule was
relaxed:** the correlation requirement is unchanged, and it still refuses whenever the running
operation cannot be positively identified. Neither observation loop was modified, so §2's
preserved success boundaries are untouched.

---

## 14. Live Take Over smoke (§34)

Separate synthetic attempt. Operation started and driven to Busy → Take Over requested → **zero
further PrintFlow UI input** (`inputSent=false`, `meituCancelInvoked=false`) → Meitu left running,
same pid, and observed to complete the enhancement on its own → attempt closed `Cancelled`, session
`HandedOff`, lock released → explicit re-entry required. The operator was not asked to finish the
image.

---

## 15. Remaining D2B scope

Not implemented here, deliberately:

- force termination of Meitu (`Process.Kill`/`TerminateProcess`/`taskkill`) and the policy
  governing when it could ever be justified;
- what to do when a Stop cannot prove a cancel **and** the operator cannot resolve Meitu manually —
  D2A stops and reports, and that is where it ends;
- discarding a retained processed result (§13 explicitly leaves it alone; a separately signed safe
  action would be needed);
- any automatic escalation from Stop to termination. There is no ladder to climb: the stop
  vocabulary has exactly two values and a test forbids a third named `Kill`, `Terminate`, `Force`
  or `Abort`.

Global Production mode remains **off** (`Adapters.Mode = "Fake"`); only the controlled production
seam was used for the live smokes. Epic 11500 untouched. Photoshop untouched.

---

## 16. Git state

Not pushed. No history rewrite. Nothing staged.

Modified: 37 source/test files. Added: 8 files (3 product, 5 test).

Not committed, per §41: synthetic images, partial outputs, screenshots, the runtime DB, smoke
transcripts, and the external baseline artefacts (`editor-busy-cancel.json` and preset v1.8.0 live
under `D:\PrintFlowStudio\`, outside the repository). `appsettings.json` carries the preset pointer
and hash, which is configuration rather than an artefact.

---

## Verdict

**11300-D2A PASS WITH NOTES — READY FOR FORCE-TERMINATION POLICY**

PASS criteria, each met:

- Stop semantics are phase-specific — eight phases, one pure policy function, exhaustively tested.
- Busy Stop uses only an exact signed Meitu Cancel control, resolved structurally and invoked once.
- No guessed Cancel input exists — no name search, substring, coordinate, mouse event, blind
  Escape or shortcut is reachable, asserted as absences.
- Stop never creates a fake Revision — enforced by the type, the workflow, and a database CHECK.
- Late Stop cannot erase a validated success — enforced by the transition table.
- Take Over sends no further automation input, from any phase.
- External state is recorded honestly, with no member meaning "safe" or "finished".
- Restart does not silently resume handed-off work.
- Re-entry is explicit and uses a new Attempt; the handed-off attempt stays immutable.
- The global lock remains correct, and releasing it grants no shortcut.
- No process-kill API exists anywhere in the product source.
- Both production happy paths remain green.
- All gates green: 8221 passed, 0 failed, 0 warnings, 0 errors, no vulnerable packages.

**Notes** (why "with notes" rather than a bare PASS):

1. **No human visual inspection of the rendered screens.** Rendering at 1000×700 is asserted
   programmatically with binding errors escalated to failures, but nobody looked at them (§37).
2. **The correlation read is per-operation**, for the live reason recorded in §13. It is correct
   and tested, but it means the cancel's ability to correlate is coupled to how each operation's
   observation loop reads Meitu — if a future slice changes either loop's read mechanism, the
   matching branch here must change with it.
3. **Post-cancel state is `Unknown` to the classifier** in both operations, because the read is
   operation-scoped rather than a full classification. This is honest and documented, but it means
   the audit records "PrintFlow last saw: not-this-operation's-Busy" rather than a positively named
   screen. The screens themselves are now signed evidence.
4. **The live 抠图 Busy window is ~1.5 s**, which is genuinely tight for a human pressing Stop. The
   mechanism works, but an operator may often find the cutout has already finished — in which case
   PrintFlow correctly refuses rather than cancelling something else.
