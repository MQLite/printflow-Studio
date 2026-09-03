# Epic 11600 — Part C

## Production Failure Injection & Recovery Matrix

Date: 2026-09-03

## 1. Verdict

The deterministic and real-workflow portions of the failure matrix pass. Failures remain visible,
invalid output does not become a Revision, automation locks either release in the closing
transaction or are recovered only after the owner is proved dead, and every automated major
failure family has a successful restored-state retry.

Part C is nevertheless blocked. During the limited live proof, three successive synthetic
Photoshop jobs failed closed. The first two opened only their own synthetic Working PNG and then
refused because the signed identity control was not both visible and enabled. After each refusal,
the attempt released the automation lock and the exact synthetic document was closed manually.
Photoshop was then cleanly closed from its no-document start screen to exercise the recommended
between-job relaunch. Its process remained alive behind an Adobe crash-report window. The next
Production job refused with `PhotoshopWindowNotFound` because that process owned no visible signed
Photoshop main frame.

The specification forbids dismissing unknown dialogs and forbids process killing as recovery.
Consequently, the crash-report state was left for the operator and the required live sequence
`restore accepted state -> next clean Production job succeeds` could not be completed.

## 2. Starting accepted baseline

| Item | Accepted value |
| --- | --- |
| Configuration | committed `Adapters.Mode = Production`; no override |
| Preset | `printflow-workstation-v1 1.16.0` |
| Preset SHA-256 | `6396FB4EB87F69C6789304CE191453654B2B75E82A5A9AB0161F90556A6F1A80` |
| Integrity chain | 28/28 entries verified |
| Production authorisation | `VerifiedEnvironmentGate` only |
| Photoshop lifecycle | exact Working path, causal signed discard, post-cleanup TIFF re-read |
| Meitu | exact accepted `7.8.7.5` executable |
| Accepted soak | 40/40 jobs; 20 Photoshop, 20 Meitu; 0 adapter failures/retries/warnings |
| Accepted soak persistence | 40 `ReviewRequired`; lock free; 40 sessions; none lost |
| Accepted final Photoshop state | `KnownStartScreen`; `OWL.Document = 0` |
| Accepted suite/build | 10,121 passed; 0 failed/skipped; 0 warnings/errors |

This slice did not reopen or redesign B1 or B. The accepted Part B proof remains the authoritative
successful Production soak.

## 3. Matrix design

Three evidence levels were kept separate:

1. Level 1 uses recording process, window, control, filesystem, clock, and native-operation seams.
   Counts prove whether input, Action, save, discard, or launch was attempted.
2. Level 2 drives the real `SessionService`, workflow engine, SQLite repository, attempt rows,
   Revision boundary, global automation lock, startup recovery, and retry semantics. Only the
   unsafe external application boundary is replaced.
3. Level 3 uses the accepted live workstation, committed Production mode, the real gate and
   synthetic inputs only. No binary/evidence/display/customer-data corruption and no process kill
   was performed.

Existing accepted regression tests were reused where they already establish the requested fact.
Fifteen new deterministic cases fill the material gaps: unsupported remote-session side effects;
four Photoshop readiness/relaunch cases; six owned-cleanup postconditions; three persistence
commit/recovery cases; and unexpected adapter cancellation. The existing Action-drift test was
extended with a restored-byte successful retry. A fifth persistence test explicitly covers an
ordinary Photoshop adapter exception.

## 4. Environment and adapter matrix

`Input sent?` counts keystroke/control/native application input. `Lock after` describes the state
after normal closing or, for a simulated lost persistence commit, after named startup recovery.

| Fault | Failure/state | Input sent? | Output accepted? | Revision? | Lock after | Persisted attempt | Recovery / next job |
| --- | --- | ---: | ---: | ---: | --- | --- | --- |
| Display mismatch | `EnvironmentNotVerified` | 0 | no | no | never acquired | none | restore display facts; Production adapter reached |
| Non-interactive/service session | `EnvironmentNotVerified` | 0 | no | no | never acquired | none | restore local interactive facts; succeeds |
| Unsupported RDP session | `EnvironmentNotVerified` | 0 | no | no | never acquired | none | restore Console/non-remote facts; succeeds |
| Secure desktop | `EnvironmentNotVerified` | 0 | no | no | never acquired | none | restore `Default`; same process succeeds |
| Wrong workspace root | `EnvironmentNotVerified` | 0 | no | no | never acquired | none | restore configured root; succeeds |
| Manifest digest invalid | `EnvironmentNotVerified` | 0 | no | no | never acquired | none | restore immutable manifest/restart; succeeds |
| Evidence digest invalid | `EnvironmentNotVerified` | 0 | no | no | never acquired | none | restore evidence/restart; succeeds |
| Photoshop executable mismatch | `EnvironmentNotVerified` | 0 | no | no | never acquired | none | restore accepted binary/restart; succeeds |
| Meitu executable mismatch | `EnvironmentNotVerified` | 0 | no | no | never acquired | none | restore accepted binary/restart; succeeds |
| Action digest mismatch | `EnvironmentNotVerified` | 0 | no | no | never acquired | none | restore accepted `.atn`/restart; succeeds |
| Photoshop executable missing/changed at operation time | `PhotoshopNotInstalled` | 0 | no | no | released | Failed when through workflow | accepted bytes restored; later operation succeeds |
| Photoshop launch returns failure | `PhotoshopLaunchFailed` | 0 | no | no | released | Failed | scripted launch restored; next launch succeeds |
| Launch returns wrong process image | `PhotoshopNotInstalled` | 0 | no | no | released | Failed | correct process identity; succeeds |
| Launch process exits/no main frame | `PhotoshopWindowNotFound` | 0 | no | no | released | Failed | accepted process/window restored; succeeds |
| Multiple accepted Photoshop processes | `PhotoshopUnknownState` | 0 | no | no | released | Failed | ambiguity removed; succeeds |
| Wrong/invisible main window | `PhotoshopWindowNotFound` | 0 | no | no | released | Failed | signed frame restored; succeeds |
| Disabled Photoshop frame | `PhotoshopBlockingDialog` | 0 | no | no | released | Failed | frame/modal repaired; succeeds |
| Foreground acquisition refused | `PhotoshopTargetLost` | 0 | no | no | released | Failed | foreground restored; next guarded input succeeds |
| Accepted Meitu missing/digest changed | `MeituNotInstalled` | 0 | no | no | released | Failed | accepted executable restored; succeeds |
| Newer Meitu installed or running | excluded, never selected | 0 to newer | no | no | released | Failed if accepted instance unavailable | stop unaccepted holder; accepted 7.8.7.5 succeeds |
| Accepted launch hands off to newer holder | `MeituLaunchFailed` | 0 to newer | no | no | released | Failed | accepted slot restored; succeeds |
| Multiple accepted Meitu instances | refusal | 0 | no | no | released | Failed | ambiguity removed; succeeds |
| Meitu launch/window/ownership loss | launch/window/target failure | 0 after loss | no | no | released | Failed | accepted neutral target restored; succeeds |

Every gate row is exercised through the real workflow boundary. The adapter call count is zero,
the external recorder is unchanged, no attempt row exists, and the lock is never acquired.

## 5. Photoshop document, Action, save, and cleanup matrix

| Fault | Failure/state | Irreversible count | Output accepted? | Revision/review | Lock | Recovery |
| --- | --- | ---: | ---: | --- | --- | --- |
| Unknown/unowned active document | identity unconfirmed | close/save/discard 0 | no | none | released | remove unknown state; next open proves exact path |
| Same filename, wrong directory | identity unconfirmed | close/Action/save/discard 0 | no | none | released | exact Working path restored; succeeds |
| Identity probe missing/ambiguous/target lost | identity/target refusal | Action/save/discard 0 | no | none | released | signed surface/target restored; succeeds |
| Pre-existing modal | blocking dialog | all input 0 | no | none | released | operator clears state; next operation succeeds |
| Save-for-Web, Save As, arbitrary error after close | blocking dialog | discard 0 | valid TIFF remains where applicable | cleanup warning only | released | repair Photoshop; next operation re-inspects |
| `.atn` missing or digest drift after gate | environment refusal | Action 0 | no | none | released | restore exact bytes; next Action runs exactly once |
| Action set/name/transcript mismatch | precondition refusal | Action invocation 0 | no | none | released | restore accepted Action contract; succeeds |
| Action invocation throws/fails | unknown retained state | Action 1, retry 0 | no | none | released | repair external state; explicit new attempt succeeds |
| Target lost before Action | target refusal | Action 0 | no | none | released | target restored; succeeds |
| Target lost/invalid state after Action | retained-state failure | Action 1, save 0 | no | none | released | external repair; new attempt succeeds |
| Save surface absent/wrong or native save fails | save/output failure | save 0 or 1 factually | no | none | released | restore signed save path; new destination succeeds |
| Wrong folder/name or pre-existing destination | precondition refusal | overwrite 0 | no | none | released | correct attempt path; succeeds |
| Missing/zero/partial/unsettled/malformed TIFF | output missing/unreadable/validation failure | save <= 1 | no | no Revision, no `ReviewRequired` | released | retry gets new attempt directory/path |
| Wrong pixels/DPI/CMYK/channels/W1/alpha/layers/pyramid | validation failure | save 1 | no | no Revision, no `ReviewRequired` | released | corrected output contract; retry succeeds |
| Candidate changes/disappears before handoff | validation/integrity failure | no extra save | no | no Revision | released | retry never adopts old candidate |
| Clean owned close | success | close 1, discard 0 | yes | `ReviewRequired` | released | next job succeeds |
| Dirty causal signed prompt | success | close 1, discard 1 | yes | `ReviewRequired` | released | next job succeeds |
| Close requested, no prompt, document remains | bounded timeout | close 1, discard 0 | valid TIFF retained | cleanup warning | released | restore state; next job succeeds |
| Prompt existed before close | blocking dialog | close/discard 0 | valid TIFF retained if already validated | cleanup warning | released | operator repair |
| Prompt question names another document | blocking dialog | discard 0 | valid TIFF retained | cleanup warning | released | operator repair |
| Missing/changed discard id, text, or complete control set | blocking dialog | discard 0 | valid TIFF retained | cleanup warning | released | signed state restored |
| Foreground/target/ownership lost before discard | target refusal | discard 0 | valid TIFF retained | cleanup warning | released | restore accepted target |
| Discard invocation refused | unknown state | completed discard 0 | valid TIFF retained | cleanup warning | released | operator repair |
| Prompt remains after discard | blocking/bounded failure | discard 1 | valid TIFF retained | cleanup warning | released | operator repair |
| Main frame stays disabled | cleanup failure | discard 1 | valid TIFF retained | cleanup warning | released | operator repair |
| Expected document remains/unprovable | timeout | discard <= 1 | valid TIFF retained | cleanup warning | released | exact state restored |
| Same-name prior document appears | success only after different absolute path is proved | close 1, discard 1 max | yes | `ReviewRequired` | released | prior document untouched |

The critical prompt invariant is directly counted: an unknown, incomplete, pre-existing, wrong
document, wrong foreground, or otherwise non-causal prompt receives zero discard invocations.

For cleanup-output ordering, the controlled valid TIFF is hashed before cleanup and read again
after cleanup. A cleanup failure with unchanged bytes preserves the successful output plus an
explicit warning. Deliberate mutation during cleanup fails the post-cleanup hash comparison and
constructs no adapter output. No unrelated path changes in the recording workspace.

## 6. Meitu state, output, and cleanup matrix

| Fault | Failure/state | Input/action count | Output accepted? | Revision/review | Lock | Recovery |
| --- | --- | ---: | ---: | --- | --- | --- |
| Unexpected loaded asset/wrong start state | `MeituUnknownState` | 0 | no | none | released | restore signed empty/welcome state; succeeds |
| Blocking modal | `MeituBlockingDialog` | dismiss 0 | no | none | released | operator clears modal; succeeds |
| Missing/unrecognised enhancement/background control | precondition/unknown | operation 0 | no | none | released | restore signed tree; succeeds |
| Export/Save/destination control absent or wrong | open/export failure | confirm 0 | no | none | released | restore signed surface; succeeds |
| Target lost before import | target refusal | import 0 | no | none | released | target restored; succeeds |
| Target lost after import/during enhancement/export | target failure | no later input | no | none | released | neutral state/process restored; succeeds |
| Missing/zero/corrupt/non-PNG output | missing/unreadable/validation failure | export <= 1 | no | no Revision/review | released | fresh attempt path succeeds |
| Partial/unsettled/wrong dimensions/alpha/association | validation failure | no extra export | no | no Revision/review | released | corrected export; retry succeeds |
| Stale/pre-existing expected path | refusal before overwrite/adoption | export 0 | no | none | released | new attempt-scoped destination succeeds |
| Valid export, cleanup cannot return neutral | successful output + explicit warning | no arbitrary cleanup input | yes | Succeeded/`ReviewRequired` | released | next job refuses unknown state; neutral restore succeeds |
| Modal/missing close/target loss during cleanup | explicit cleanup warning | no generic dismissal | yes | Succeeded/`ReviewRequired` | released | repair neutral state; succeeds |

Meitu never follows “latest installed,” never attaches to an unaccepted executable, and never
imports a new session into an unknown editor context.

## 7. Session, cancellation, process death, storage, and persistence matrix

| Fault | Failure/state | Output accepted? | Revision? | Lock after | Persisted truth | Recovery / next job |
| --- | --- | ---: | ---: | --- | --- | --- |
| Meitu ordinary adapter exception | `AdapterUnavailable` | no | no | free | attempt Failed; bounded type/detail | restore adapter; retry/new session succeeds |
| Photoshop ordinary adapter exception | `AdapterUnavailable` | no | no | free | attempt Failed; bounded type/detail | restore adapter; retry succeeds |
| `OperationCanceledException`, caller token not cancelled | fault, not operator cancellation | no | no | free | Failed + `faultType` | normal adapter retry succeeds |
| Caller cancellation before attempt/input | `Cancelled`/no operation | no | no | never acquired or free | none/cancelled per boundary | no auto-retry; later operation succeeds |
| Caller cancellation after attempt/during wait/settle | `Cancelled` | no | no | free via cancellation-independent close | attempt Failed with cancellation code | explicit retry succeeds |
| PrintFlow dies with Running attempt/held lock | startup `Interrupted` | no | no | released only after owner proved dead | attempt Interrupted | new attempt/path succeeds |
| Lock owner alive or liveness unknown | retained | no new output | no new Revision | remains held | Running unchanged | no stealing; wait/operator investigation |
| Photoshop/Meitu dies at guarded stages | target/process failure | only already-observed side effects retained | no unless valid output already committed | free on normal close | Failed with stage facts | restore app/state; retry succeeds |
| Working copy/output write missing, denied, partial, or unreadable | storage/output failure | no | no | free | Failed | restore storage; retry on new path succeeds |
| Output disappears or changes before final handoff | integrity/missing failure | no | no | free | Failed | prior accepted outputs unchanged; retry succeeds |
| Attempt-start commit fails | `PersistenceError` | no | no | never acquired atomically | no attempt row | remove fault; same waiting step succeeds |
| Failure-close commit fails | `PersistenceError` supersedes unpersistable close | no | no | held until dead-owner recovery | Running (not fabricated Failed) | startup marks Interrupted/releases; retry succeeds |
| Success/Revision/lock-release commit fails | `PersistenceError` | no accepted output | no | held until dead-owner recovery | Running; no partial success rows | startup marks Interrupted/releases; retry succeeds |
| Session A holds global lock; B starts | lock refusal | no | no | A remains owner | B creates no adapter work | A completes/fails; B retry succeeds |

The persistence audit confirms effect ordering:

- opening attempt row and lock acquisition are one transaction;
- attempt success, Revision, `PrintOutput`, `ReviewRequired`, and lock release are one transaction;
- attempt failure and lock release are one transaction;
- a failed close commit is not caught-and-continued as though it landed;
- startup recovery requires dead ownership before converting Running to Interrupted and releasing
  the stale lock.

## 8. External application death by stage

| Stage family | Side effects legitimately retained | Persisted attempt | Revision allowed? | Lock | Clean retry |
| --- | --- | --- | ---: | --- | --- |
| Photoshop before input/readiness | none | Failed | no | released | yes after accepted process restored |
| Photoshop after open/before Action | owned Working document may remain | Failed | no | released | yes after state repair |
| Photoshop after Action/before save | in-memory CMYK/W1 may remain | Failed | no | released | yes, explicit new attempt only |
| Photoshop during save/settle | partial attempt-scoped TIFF may remain | Failed | no | released | yes; new path, no adoption |
| Photoshop during cleanup | independently valid TIFF may remain; document state unknown | success + warning only if post-cleanup TIFF re-read still matches | yes only for unchanged validated TIFF | released | yes after Photoshop repair |
| Meitu before import | none | Failed | no | released | yes |
| Meitu after import/enhancement | loaded Working asset or Busy state may remain | Failed | no | released | yes after neutral restore |
| Meitu during export | attempt-scoped partial PNG may remain | Failed | no | released | yes; new path |
| Meitu during cleanup | valid output retained; UI state unknown | success + warning under existing contract | yes for validated output | released | yes after neutral restore |

No mid-operation process death was manufactured live.

## 9. Operational recovery classification

| Recovery class | Representative failures |
| --- | --- |
| State settles automatically within bounded wait | ordinary launch/window appearance, output settle, prompt/frame postcondition |
| Operator `Check again` | dynamic desktop/session/display/workspace facts |
| Fix Photoshop | unknown document/modal, disabled frame, failed cleanup, crash-report window |
| Fix Meitu | unexpected asset/modal, unaccepted single-instance holder, failed neutral cleanup |
| PrintFlow restart | lost persistence close/success commit after the old owner terminates |
| External application restart | dead/identity-lost Photoshop or Meitu, or irreparable unknown application state |
| Workstation/preset repair | executable/Action/evidence/manifest digest or accepted-version drift |

No hidden retry, process kill, generic dialog dismissal, Production-to-Fake fallback, or acceptance
of a newer Meitu version was added.

## 10. Product and harness changes

No Production source, configuration, preset, evidence, dependency, `FailureCode`, or database
migration changed.

Test-only changes:

- the gate side-effect theory now includes unsupported remote sessions;
- Photoshop readiness covers launch failure with recovery, wrong launched process identity,
  process exit/no window, and a disabled main frame;
- the Action-drift case now restores the accepted `.atn` bytes and proves the next invocation;
- the cleanup recorder can inject a refused control press;
- cleanup tests now cover no prompt, wrong-document question, invocation refusal, retained prompt,
  disabled main frame, and an unclosed/unprovable expected document;
- the session harness can place the existing faulting repository around a supplied Meitu adapter;
- `ProductionFailurePersistenceTests` covers attempt-start, failure-close, success/Revision/
  lock-release commit failures, unexpected cancellation, and a Photoshop adapter exception.

The live smoke itself was not weakened after it failed. Its failed QA databases, synthetic inputs,
session directories, and screenshots remain in place as evidence.

## 11. Automated results

| Gate | Result |
| --- | --- |
| New gate + persistence focus | 20 passed / 0 failed |
| Photoshop readiness + Action focus | 58 passed / 0 failed |
| Photoshop cleanup driver focus | 37 passed / 0 failed |
| Final Automation + Verification + Persistence matrix | 771 passed / 0 failed / 0 skipped in 1m 2s |
| Build (`PrintFlowStudio.sln`) | 0 warnings / 0 errors in 1.08s |
| NuGet vulnerability audit | no vulnerable direct or transitive packages in all five projects |
| Deprecated packages | xUnit 2.9.3 test-only legacy advisory; unchanged; migration prohibited here |

No full 10,121-test suite was rerun: this slice changes only tests, test fixtures, and this report.
The directly affected automation, verification, and persistence suites were run instead, per the
testing policy.

## 12. Controlled live evidence

### 12.1 Preflight

The registered Production readiness smoke passed immediately before the fault proof:

```text
Adapters.Mode          Production
preset                 printflow-workstation-v1 1.16.0 / 6396FB4EB87F…
PresetIntegrity        passed
EvidenceIntegrity      28/28 passed
blocking checks        all passed
VerifiedEnvironmentGate ALLOWED
```

The two existing advisories remain non-blocking: 16/28 integrity-referenced files lack the
read-only attribute while every SHA-256 matches, and external-application UI language remains
advisory.

### 12.2 Failure, restore, and failed next-job proof

| Run token | Live outcome | Input/output truth | Lock | Restore |
| --- | --- | --- | --- | --- |
| `20260903-124612-7825AD2B` | Photoshop identity control not visible/enabled | no control read/driven; no adapter output; no Revision | free | exact synthetic document closed; start screen restored |
| `20260903-124755-6FBC5C06` | same refusal reproduced | no control read/driven; no adapter output; no Revision | free | exact synthetic document closed; start screen restored |
| `20260903-124857-8BCF683B` | post-close relaunch refused; old PID owned no signed main frame | input 0; output 0; Revision 0 | free | blocked by Adobe crash-report window |

The clean-close sequence was performed only after the window title exactly named the current
synthetic file. Photoshop reached title `Adobe Photoshop CC 2019` before the clean application
exit was requested. The remaining PID 20848 then exposed title `Crash Report for "Adobe Photoshop
CC"`. PrintFlow did not touch that window.

This is a safe failure but not a completed recovery proof. Operator action is required to close
the crash reporter/restart Photoshop and return the application to the accepted signed start
screen. A new synthetic Production Photoshop job must then succeed before Part C can pass.

### 12.3 Meitu live state

The final read-only Meitu smoke passed. It attached to PID 27660 at the exact accepted
`7.8.7.5` executable, classified `KnownWelcome`, resolved the signed start-page card, and produced
no input. Installed topology remains:

```text
accepted/running executable  7.8.7.5
additional installed version 7.8.8.0
stale non-executable dirs     7.6.0.2_tmp, 7.6.2.6_tmp
```

The newer version was not selected or attached to. `silentUpgrade` was not changed.

### 12.4 Filesystem and customer statement

The three failed live runs added three synthetic QA/session records, moving the session-directory
count from 94 to 97. `Comparison` remained 70 files and `Quarantine` remained 2 files. Failed
evidence was not deleted.

No customer artefact was opened, selected, closed, saved, discarded, overwritten, moved, renamed,
uploaded, or deleted. Every Photoshop document acted on by this slice carried the unique live-run
synthetic token and resolved to that run's own QA/session Working path. The unrelated CorelDRAW
customer document visible on the workstation was never activated or sent input.

## 13. Production Readiness and remaining risks

The environment gate still reports `ALLOWED` because executable, preset, evidence, desktop,
display, culture, and workspace facts remain accepted. That does not override the adapter's
operation-time UI refusal. Current Production operational state is blocked by the Photoshop crash
reporter and must not be used for a real order.

Before the first controlled real Production order:

1. the operator must close the Adobe crash reporter or restart Photoshop without process killing;
2. read-only preflight must show exactly one accepted Photoshop process, signed main frame,
   `KnownStartScreen`, no blocking dialog, and `OWL.Document = 0`;
3. rerun one synthetic Photoshop Production job and prove Succeeded/`ReviewRequired`, signed owned
   cleanup, unchanged TIFF re-read, zero cleanup warnings, and a free lock;
4. rerun the five-job restart smoke, or an equivalent bounded Photoshop/Meitu sequence, and reach
   the post-restart successful job;
5. retain Meitu exact-version monitoring because 7.8.8.0 remains installed beside accepted
   7.8.7.5 and in-process auto-update remains an availability risk.

Do not proceed to 11600-D while these items are outstanding.

## 14. Git state

Starting commit: `c1c9ce5` on `master`, clean. Current changes are the seven test/test-fixture files,
the new persistence matrix test, and this report. No amend, rebase, history rewrite, dependency
change, preset change, configuration change, push, or Production source change was made.

**11600-C BLOCKED — PRODUCTION FAILURE RECOVERY NOT VERIFIED**
