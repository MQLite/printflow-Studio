# Epic 11300 Part D2B — Force-Termination Policy Decision Gate

Date: 2026-08-26
SDK: .NET 10.0.400
Policy: **PrintFlow never force-terminates Meitu.**

D2A was checkpointed normally as `77e62e0` (`11300: add safe stop and operator takeover`) before
this slice. No amend, history rewrite or push was performed.

## 1. Remaining failure scenarios

| Scenario | Existing safe path |
|---|---|
| Signed Cancel succeeds | While the exact operation is positively Busy, resolve the one signed control, re-verify process/window/foreground, invoke once, and observe Meitu leave Busy. End the Attempt as Cancelled with no Revision. |
| Signed Cancel is unavailable, ambiguous, disabled or unsigned | Invoke nothing. Stop PrintFlow orchestration, record that the operation may still be running, and require the operator to inspect Meitu or Take Over. |
| Busy ends before the operator presses Stop | If processing is now complete but export has not begun, Stop exports nothing and reports the retained processed result. If validated success already committed, the run is unregistered and a late Stop is refused without rewriting success. An unrecognised transition fails closed. |
| Unknown state | Produce no input. Report retained state `Unknown`; offer Take Over while the external run is still in flight. |
| Blocking modal | Do not dismiss or navigate through it. Stop cannot prove a cancel; Take Over sends nothing and leaves the modal to the operator. |
| Target/window/foreground is lost | D1 returns structured `MeituTargetLost`, sends no later input, creates no Revision and releases the lock when the Attempt closes. A recycled HWND/PID is never trusted. |
| Meitu continues processing after Take Over | The session is `HandedOff`; PrintFlow has ended its run and sends no more input. The operator may wait, finish work, or close Meitu manually. |
| Meitu does not respond to the one signed Cancel | D2B now records two separate facts: Cancel was invoked, but Meitu did not positively leave Busy. Retained state stays `OperationMayStillBeRunning`, operator action is required, and no second invocation or termination follows. |
| Meitu process exits independently while controlled | D1's `process-exited` target-loss path is authoritative: no later input, no inferred success and no Revision. Startup recovery remains authoritative if PrintFlow dies before closing metadata. |
| Operator closes Meitu after Take Over | Nothing observes or adopts the manual result. The persisted session remains `HandedOff`; re-entry is explicit, starts nothing, and the next Run Step creates a new Attempt through normal readiness/launch verification. |

None of these scenarios establishes a required production workflow that needs destructive process
control.

## 2. Stop and Take Over coverage

The recovery ladder is:

1. Before external work, Stop ends PrintFlow orchestration and produces no operation input.
2. At Busy, only an exact-operation, signed Cancel may be invoked, once.
3. If Cancel cannot be proven, the state is Unknown/modal, or Cancel does not settle, PrintFlow
   reports operator action required and offers Take Over where a run is still active.
4. Take Over produces no input in every operation phase and persists `SessionState.HandedOff`.
5. After Take Over, the operator closes Meitu manually through Meitu or Windows if necessary.
6. Returning requires explicit `ReenterAutomation`; Run Step then creates a new Attempt through
   the normal safe-state verification/attach/launch path.

The stop vocabulary remains exactly `StopOperation` and `TakeOver`. There is no Kill, Terminate,
Force Close or Abort mode and no automatic escalation step.

## 3. Process and shared-work risk

PrintFlow can prove an external process by accepted executable path, PID and start time, and can
re-check that the verified window belongs to that process immediately before input. It can also
confirm the current working-copy identity for the editor surface it is controlling.

Those facts do **not** prove that the process contains only the current Attempt's document:

- `EnsureReadyAsync` deliberately attaches to one already-running accepted Meitu process when its
  visible state is safe; that process may have been started and used by the operator.
- Launching Meitu does not prevent the operator from opening or creating unrelated work in the
  same process later.
- One verified editor window/document does not enumerate every document, internal tab, unsaved
  buffer or other operator-owned state inside the process.
- Process ownership and exact PID identity identify the container, not everything the container
  owns.

Unrelated operator-owned work therefore cannot be positively excluded. Force termination could
destroy that work, so decision criterion 4 fails even if process identity at the instant of a
hypothetical termination could otherwise be re-proven.

## 4. Force-termination necessity assessment

Force termination was permitted for consideration only if all seven criteria were established.
They were not:

| Criterion | Assessment |
|---|---|
| 1. Required workflow cannot complete through Stop | Not established. Every evidenced failure has a Stop refusal/result plus operator recovery. |
| 2. Take Over cannot resolve it | Not established. Take Over reliably ends PrintFlow control and transfers ownership. |
| 3. Manual Meitu/Windows closure is not reasonable | Not established. Manual closure is the bounded recovery for an unresponsive handed-off application. |
| 4. Unrelated work cannot be destroyed | **Fails.** Shared-process work cannot be positively excluded. |
| 5. Exact process identity at termination | PID/start-time/executable identity can be re-verified for ordinary guarded work, but identity does not prove exclusive contents and no termination seam exists. |
| 6. Deterministic restart/recovery | Existing manual-loss/re-entry recovery is deterministic. Recovery after destructive termination of unknown shared state has no evidenced deterministic contract. |
| 7. Benefit outweighs destructive capability | Not established. Manual recovery solves the evidenced cases without adding an irreversible capability. |

Because the criteria are conjunctive, any missing condition rejects force termination. Conditions
1, 2, 3, 4, 6 and 7 are not established; criterion 4 positively fails.

## 5. Chosen production policy

**PrintFlow never force-terminates Meitu.**

There is no automatic path from Stop, Cancel failure, timeout, Unknown state, modal state, target
loss or Take Over to process termination. Failure remains fail-closed. No `Process.Kill`, native
termination call, `taskkill`, PowerShell process stop, window-close message or equivalent wrapper
was added.

`Adapters.Mode` remains `Fake`. EnvironmentGate, global Production registration, Photoshop/Epic
11400 and Epic 11500 are unchanged.

## 6. Operator and manual recovery

The operator-facing guidance now says:

- en-US: PrintFlow will not force-close Meitu. If Meitu is unresponsive after takeover, close it
  manually through Meitu or Windows before returning to automation.
- zh-CN: PrintFlow 不会强制关闭美图秀秀。如果接管后美图秀秀无响应，请通过美图秀秀或 Windows 手动关闭后再恢复自动处理。

The manual-closure sequence is structurally preserved:

`TakeOver` → Attempt Cancelled / step Interrupted → session `HandedOff` → no active runtime →
manual Meitu closure has no adoption callback → no Revision and no automatic restart → explicit
`ReenterAutomation` → session Active / step Waiting with no new Attempt yet → explicit Run Step
→ new Attempt → ordinary safe-state verification/attach/launch.

PrintFlow does not scan for, infer, or adopt manual Meitu output.

## 7. Automatic-escalation prohibition

All of the following remain impossible:

- Stop → Cancel failed → terminate Meitu;
- timeout → terminate Meitu;
- Unknown/modal → terminate Meitu;
- target/window loss → terminate Meitu;
- Take Over → terminate Meitu;
- signed Cancel invoked but Meitu remained Busy → retry Cancel or terminate Meitu.

The unresponsive case is now explicit rather than inferred: `OperationCancelWasInvoked=true`,
`meituLeftBusy=false`, retained state `OperationMayStillBeRunning`,
`operatorActionRequired=true`, `forceTerminationInvoked=false`.

## 8. Architecture enforcement

`ForceTerminationPolicyBoundaryTests` enforces the policy at four levels:

1. resolves compiled production IL calls and rejects `System.Diagnostics.Process.Kill`;
2. inspects P/Invoke entry points and rejects `TerminateProcess`, `NtTerminateProcess`,
   `ZwTerminateProcess` and `TerminateJobObject`;
3. rejects production-owned Kill/Terminate/ForceClose/AbortProcess wrapper member names;
4. scans executable production source for shell/native/window-close routes including `taskkill`,
   `Stop-Process`, `Win32_Process`, `CloseMainWindow`, `WM_CLOSE` and `SC_CLOSE`.

The compiled-call and native-entry-point checks make this stronger than a string-only test. The
existing exact two-mode vocabulary test remains, and `AutomationStopResolution` is asserted to
expose only signed-cancel/export/input/retained-state/success permissions—no escalation permission.

## 9. Preset-version provenance

No preset was created or edited for this policy slice. `appsettings.json` still points to immutable
v1.8.0 with SHA-256 `DE76464F011A54F80704BB6C32A2E0D00EFF9AB24834FF7D05EF8E9CF3DB60E4`.
No v1.9.0 was created and v1.8.0 was not modified.

The requested provenance check found no v1.6.0 or v1.7.0 file anywhere under
`D:\PrintFlowStudio\Baseline`; therefore there are no superseded immutable candidate files to
amend or explain. The external baseline/preset files remain outside the repository.

## 10. Tests and gates

Preflight actual results after the D2A checkpoint:

| Gate | Result |
|---|---|
| restore | passed in locked mode |
| build | passed; 0 warnings, 0 errors |
| test | **8,220 passed, 1 failed** out of 8,221: the D2A wording test assumed en-US while the test process rendered zh-CN |
| vulnerable packages | none in all five projects |

The locale leak was corrected by explicitly running the English UI wording assertion under en-US;
committed-resource tests independently assert both languages without relying on the host culture.

D2B focused proof includes:

| Required proof | Coverage |
|---|---|
| Stop failure never escalates | missing/ambiguous/disabled/wrong-process Cancel refusal tests + compiled absence of a termination capability |
| Timeout never escalates | `Timeout_scenario_fails_with_FailureCode_Timeout_and_releases_the_lock` + compiled/source boundary |
| Unknown/modal never escalates | `A_blocking_modal_over_the_operation_stops_the_cancel_and_dismisses_nothing`, Unknown-state driver tests + boundary |
| Take Over never terminates | every phase resolves to no input; `Take_over_ends_the_attempt_and_hands_the_session_to_the_operator` + boundary |
| HandedOff restart never launches/terminates | `A_restart_after_take_over_does_not_resume_automation`; runtime is Idle and Start Step is refused |
| Manual process disappearance follows D1 | both `Process_exit_while_Busy...` tests; after Take Over there is no monitor/adoption path and the session stays HandedOff |
| Re-entry creates a new Attempt | `Re_entry_creates_a_new_attempt_and_leaves_the_handed_off_one_immutable`; re-entry itself creates none |
| No destructive operator action | action-label tests plus the exact two-mode vocabulary and XAML/view-model surface |
| en-US/zh-CN parity | `Take_over_guidance_has_force_close_and_manual_recovery_parity` and resource-key parity |
| Existing happy paths | `The_enhancement_happy_path_is_unchanged`, `The_background_removal_happy_path_is_unchanged` |
| Unresponsive Meitu | guarded driver invokes once without retry; `An_unresponsive_cancel_outcome_requires_manual_recovery_and_never_escalates` persists the honest retained state |

Final gates after all product/test changes:

| Gate | Result |
|---|---|
| `dotnet restore --locked-mode` | passed |
| `dotnet build` | passed; 0 warnings, 0 errors |
| `dotnet test` | **8,242 passed, 0 failed, 0 skipped** |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages in all five projects |

No new UI automation evidence was required or created.

## 11. Remaining Final QA scope

No human visual inspection was performed. D2A and C2B2's existing note remains honest. Final QA
should inspect the Stop/Take Over wording once in en-US and zh-CN, including the longer
unresponsive/manual-closure guidance at the supported 1000×700 review viewport. This policy slice
does not claim Final QA completion.

## 12. Git state

- D2A checkpoint: `77e62e0`.
- D2B is a normal local policy checkpoint containing only source, resources, focused tests and
  this report.
- Expected final state after the checkpoint: clean `master...origin/master [ahead 5]`.
- No runtime database, smoke transcript, synthetic file, screenshot or external baseline artefact
  is tracked.
- No amend, history rewrite or push was performed.

11300-D2B PASS WITH NOTES — FORCE TERMINATION NOT IMPLEMENTED BY POLICY; READY FOR 11300 FINAL QA
