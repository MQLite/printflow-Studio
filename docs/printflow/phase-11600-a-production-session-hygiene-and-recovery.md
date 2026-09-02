# Epic 11600 — Part A

## Production Session Hygiene & Recovery

---

## 1. Preflight

Starting point: Epic 11500 accepted and closed, `master` clean at `618fe9d`.

| Check | Result |
| --- | --- |
| Working tree | clean |
| Build (`PrintFlowStudio.sln`) | **0 warnings / 0 errors** |
| Committed `Adapters.Mode` | `Production` |
| Configured preset | `printflow-workstation-v1 1.15.0` |
| Baseline suite (before this slice) | 10059 passed / 0 failed / 0 skipped |

The workstation this slice ran on is the accepted Production workstation
(`D:\PrintFlowStudio`, preset `3392873ED0CA…`).

Nothing in Epic 11500 was reopened, amended or redesigned.

---

## 2. Actual current post-operation state

Audited from the accepted adapters before anything was changed. What follows is what the code
*does*, not what would be convenient.

### 2.1 Photoshop — after a successful operation

`ProductionPhotoshopOutputProcessor.GenerateAsync` composes five stages: readiness → open managed
Working file → size/resolution preparation → W1 Action → Save As Copy + independent TIFF
validation. **There is no sixth stage.** It never calls `CloseExactDocumentAsync`, and the only
callers of that seam are the Part A workstation smoke and the tests.

| Question (§3) | Actual answer |
| --- | --- |
| What document remains open? | The session's own Working document, the one this attempt handed over |
| Is it the session's own working document? | Yes — proved by absolute path through the signed Save-As identity probe |
| Are unsaved changes expected? | **Yes.** The resize and the W1 Action are in-memory edits; the TIFF is a *Save As Copy*, so the open document stays dirty |
| Which dialogs/panels may remain? | None. Every surface PrintFlow raises (Open, identity probe) is closed or cancelled on the way out; a dialog still standing is a failure, not a residue |
| May Photoshop remain running? | Yes, and it is expected to |
| What must exist before the next Photoshop operation? | Exactly one process from the accepted executable, presenting one identifiable window in a state on the safe-starting allow-list |

The safe-starting allow-list is `KnownStartScreen`, `KnownEditorNoDocument`,
`KnownEditorWithExpectedDocument`, `KnownEditorWithOtherDocument`. The fourth member is what makes
the residual document harmless: the next job proceeds from it, opens its own file, and settles
identity against the absolute path — never against the title.

Observed live after the smoke's last job:

```text
PF_11600A_20260902-175639-FA4F6589_E.png @ 100% (图层 1, W1/8) *
```

PrintFlow's own synthetic working document, W1 applied, unsaved — precisely the state described
above.

### 2.2 Meitu — after a successful operation

`ProductionMeituProcessor` ends both `ProcessEnhancementAsync` and `ProcessBackgroundRemovalAsync`
with `ReturnToNeutralStateAsync`, which dismisses the export-result surface and closes the
document, and records the outcome in the attempt's adapter notes
(`cleanup the editor was returned to its signed empty state`, or a `WARNING:` line if it could
not be).

| Question (§3) | Actual answer |
| --- | --- |
| Is the editor returned to its signed empty state? | Yes, on the success path, and the attempt records whether it actually got there |
| May a source/result remain loaded? | Only as a recorded warning after a *valid* output — cleanup failure never discards a validated result |
| May any modal remain? | No. A modal left standing is reported as a warning and the next attempt refuses |
| May Meitu remain running? | Yes |
| What must exist before the next operation? | `KnownWelcome`, `KnownEditorEmpty` or `KnownEditorWithExpectedWorkingCopy` — the safe-starting allow-list |

Meitu's allow-list has **no** "editor with some other document" member. An unexpected loaded asset
is `Unknown` and stops.

### 2.3 Refused before adapter execution

`VerifiedEnvironmentGate.Verify` runs in `RunProducingStepAsync` **before** the automation lock is
read, before the attempt row is written and before any adapter is called. A gate refusal therefore
creates no attempt, takes no lock and produces no external input. Already covered by
`ProductionGateSideEffectTests` and `ProductionAdapterGateTests`; unchanged by this slice.

### 2.4 Recoverable adapter failure

Every adapter stage returns `OperationResult`. `SessionService` closes the attempt through
`FailAttemptAsync`, the engine emits `ReleaseAutomationLock` for adapter-backed steps, and the
failure detail plus structured context (`inputSent`, `w1ActionInvoked`, `tiffWritten`,
`revisionCreated`, `retainedExternalState`) is written to the immutable attempt row.

### 2.5 Unexpected external application state

Photoshop: `PhotoshopUnknownState`, `PhotoshopBlockingDialog`,
`PhotoshopDocumentIdentityUnconfirmed`. Meitu: `MeituUnknownState`, `MeituBlockingDialog`,
`MeituLaunchFailed`, `MeituTargetLost`. All fail closed; none answers a dialog or closes a
document.

---

## 3. Chosen Photoshop residual-document policy

**Policy A — the owned working document may remain open.**

This is not a new decision; it is the accepted lifecycle written down, and the reasons it is the
right one:

* ownership *is* provable, and is proved on every entry — the identity probe reads the absolute
  path, so a leftover can never satisfy "this is the document I asked for";
* the next operation has a safe, already-accepted transition from that state
  (`KnownEditorWithOtherDocument` → open → prove identity);
* Policy B would mean closing a document with unsaved in-memory changes, which raises a discard
  prompt — and answering a prompt PrintFlow did not raise is exactly what §2 forbids. A "close
  only what we own" rule would still have to decide what to do when the prompt appears, and the
  honest answer is "nothing", which leaves the document open anyway.

The `CloseExactDocumentAsync` seam is retained and still refuses any document whose absolute path
it cannot match. It is simply not part of the composed workflow operation, and
`The_composed_production_operation_closes_no_document` asserts that over the source so the policy
cannot drift silently.

**Operational cost, recorded honestly:** Photoshop is left holding a dirty PrintFlow document, so
an operator closing Photoshop later will see a save prompt for it. That prompt is about a Working
copy whose validated TIFF already exists as a separate file; discarding is always correct. This is
unchanged from 11500-D and is not a regression.

## 4. Chosen Meitu post-operation policy

**Policy B — return to the signed empty state**, exactly as the accepted adapter already does.

Kept as-is because Meitu's recognition vocabulary has no way to describe "the editor is holding
something else, and that is fine": there is no signed evidence for such a screen, so leaving a
document loaded would make the *next* operation refuse. Photoshop can afford Policy A because it
has a positively recognised state for a foreign document; Meitu cannot.

Cleanup failure after a valid output remains a warning on the attempt, never a lost result.

---

## 5. Session-isolation findings

Audited: `Sessions\<session>\Working`, revisions, step attempt artefacts, review outputs,
temporary source copies, Photoshop working documents, Meitu enhanced outputs, evidence paths and
automation-lock ownership.

Findings:

* Every mutable artefact is addressed through a `WorkspaceFileRef`/`WorkspaceDirRef` rooted at the
  session's own `Session.Workspace`. Nothing resolves a path by name.
* Each attempt writes into `Working\<attemptId>\`, so a retry's destination is new by construction
  rather than by collision handling.
* An adapter is only ever handed a `WorkspaceArea.Working` reference; both adapters restate that
  check at their own boundary rather than inheriting it.
* The automation lock is a singleton row keyed by session id, PID and machine name. A second
  session is refused with a message naming the current holder.

Proved by test with two sessions given the **same** operator-facing output name, so filename
similarity is useless as a separator:

* their artefact path sets are disjoint;
* every path each owns starts with its own session directory;
* running session B leaves every one of session A's files byte-identical (compared by digest, not
  by existence) and every one of its records unchanged.

No historical session was altered by any synthetic test — the integration tests run against a
throwaway temp workspace, and the live smoke's census (§11 below) confirms nothing existing moved.

---

## 6. Automation-lock audit

Every path by which an adapter-backed operation can end, and what it does with the lock:

| Path | Lock outcome | Where |
| --- | --- | --- |
| Success | Released — `AttemptSucceeded` emits `ReleaseAutomationLock` for adapter-backed steps | `WorkflowEngine:1397` |
| Gate refusal | **Never acquired** — the gate runs before the lock is read | `SessionService.RunProducingStepAsync` |
| Adapter readiness failure | Released via `FailAttemptAsync` → `AttemptFailed` | `WorkflowEngine:1423` |
| External launch failure | Released, same path | — |
| Ownership loss (`*TargetLost`) | Released, same path | — |
| Unexpected UI state / blocking dialog | Released, same path | — |
| Operator Stop / Take Over | Released via `AttemptCancelled`; committed on `CancellationToken.None` | `StopAttemptAsync` |
| Cancellation | Released; closing transaction forced onto `CancellationToken.None` | `RunProducingStepAsync` |
| **Unhandled exception** | **Was not released — see §7** | — |
| Application shutdown / crash | Released by startup recovery, but only once the owning PID is confirmed dead | `StartupRecoveryService.VerifyLock` |

Startup recovery's stale-lock semantics were tested rather than replaced: liveness is asked about
the **PrintFlow** process the lock names and nothing else, so Photoshop or Meitu outliving a
restart neither keeps a lock alive nor resurrects the session that held it. `Alive` and
`Unverifiable` both fail closed and retain the lock.

There is no timeout-based lock expiry anywhere, and none was added.

---

## 7. The one real defect found, and the fix

`RunProducingStepAsync` caught `OperationCanceledException` and nothing else. Any other exception
from a step's work — an adapter, the workspace, the file inspector — unwound straight out of
`ISessionService.ExecuteAsync`.

Reproduced before changing anything, with a throwing Meitu adapter through the real service:

```text
thrown  = InvalidOperationException
lockHeld= True
attempts= Import:Succeeded, Enhancement:Running
steps   = Enhancement:Processing
```

Three consequences, all of which §9 forbids:

1. the automation lock stayed **held**, by a process that was still alive — so startup recovery's
   liveness check would have refused to release it even after a restart;
2. every later adapter-backed step, in **every** session, was blocked by a run that had ended;
3. the attempt was frozen at `Running` and the step at `Processing`, which accepts no `Retry`.

### The change

`src/PrintFlow.Workflow/Services/SessionService.cs` — one added `catch`, following the containment
`ImportAsync` already gives the only other producing path:

* the exception is converted to an `OperationFailure`, not swallowed: its type goes into
  `context["faultType"]` and its message into the technical detail, which lands on the immutable
  attempt row;
* the attempt closes through the existing `FailAttemptAsync`, so the engine releases the lock via
  the effect it already emits;
* the closing transaction is forced onto `CancellationToken.None`, for the same reason the
  cancelled path is: a closing transaction that gave up would leave the held lock the containment
  exists to prevent;
* the filter also takes an `OperationCanceledException` raised while the caller's token is *not*
  cancelled — a step throwing cancellation nobody asked for is a fault, and must not be reported
  to the operator as though they had stopped the run.

### Failure vocabulary

**No new `FailureCode` was minted.** The code is the existing `AdapterUnavailable`, with a distinct
`messageKey` — the mechanism `DescribeStop` already uses to say something specific without
enlarging the enum. `Failure_OperationFaulted` was added to `Strings.resx` and `Strings.zh-CN.resx`
with a typed accessor, because `AdapterUnavailable`'s own wording ("the required application is
unavailable or already in use") would send an operator to check an installation that is fine, when
what they need to do is look at what Photoshop or Meitu is showing.

Nothing else in production code changed.

---

## 8. Abandoned attempt / interrupted operation

Two distinct boundaries, both audited:

**Process died mid-attempt** (already accepted, Epic 11100 Part 3B, re-tested here): the attempt
row stays `Running` on disk; the next start moves it to `Interrupted`, releases the lock if its
owner is confirmed dead, and quarantines only leftovers of attempts *this pass* interrupted.

**Fault inside a live process** (new): the attempt closes as `Failed` with the exception recorded.

Proved for both:

* no successful revision is fabricated — no `Revision` with the step's `OperationKind` exists;
* `ReviewRequired` is never reached;
* the attempt records failure/interruption consistently, and the failure detail survives;
* a previously approved result is untouched — a Background Removal fault after an approved
  Enhancement leaves the Enhancement's file byte-identical and its step still `Approved`;
* a later retry creates a distinguishable new attempt: new `AttemptId`, `RetrySequence` 1,
  `RetryOfAttemptId` pointing at the failed one, and a new `Working\<attemptId>\` destination;
* failed-attempt history is never deleted to tidy the session.

---

## 9. Partial-output behaviour

Validation, not the adapter's word, decides. Proved for zero-byte output, missing output and an
adapter that gave up before writing:

* the step lands on `Failed`, never `ReviewRequired`;
* no `Revision` and no `PrintOutput` is created; the attempt's `OutputRevisionId` is null;
* the lock is released.

A retry after a genuine zero-byte write was checked specifically: the leftover file is still on
disk in the failed attempt's folder when the retry runs, the retry writes into its own attempt
folder, and the leftover is still exactly where it was afterwards — nothing deleted, moved or
promoted. Existing quarantine/retention contract untouched; no retention subsystem introduced.

---

## 10. PrintFlow restart recovery

Tested with a second service instance over the same database and disk, sharing nothing in memory,
with recovery run first exactly as `ApplicationStartup` orders it.

**Completed `ReviewRequired` session:** session, revisions, attempt ids and statuses, validated
output bytes and step state all survive, and the restarted process can still *act* — the approval
goes through. Recovery interrupts nothing and quarantines nothing.

**Faulted session:** the failure stays a failure. Recovery reports 0 interrupted attempts and does
not release a lock that was already free. A retry afterwards is a clean second attempt, and the
failed one is still in history.

**Startup is not destructive:** with a finished session and a failed session (with a real leftover
on disk) present and nothing to recover, a full recovery pass moves no file, deletes no session,
quarantines nothing, and leaves the failed attempt failed. The whole-workspace file list is
identical before and after.

---

## 11. External-application restart recovery

Proved against the **real adapters** with a faked operating system:

| Scenario | Result |
| --- | --- |
| Job A's document still loaded, job B starts | B opens its own file, identity settled against the absolute path, application reused not relaunched |
| Photoshop closed between jobs | Relaunched — and the relaunched process is still identity checked; a launch producing a different binary is refused `PhotoshopNotInstalled` with `inputSent=false` |
| Meitu closed between jobs | Relaunched, identity still checked; a changed binary is refused `MeituNotInstalled` |
| PrintFlow restarts, Photoshop still running | New adapter instance attaches to the same PID, `WasLaunched=false`, `LaunchCount=0`, and re-inspects the screen rather than inheriting a belief about it |
| PrintFlow restarts, Meitu still running | Same |
| Photoshop holding an unprovable document | `PhotoshopDocumentIdentityUnconfirmed`; the only shortcuts sent are `OpenFile` and `SaveAsProbe` (asserted as an allow-list over everything sent, not a deny-list); nothing closed, nothing saved, no dialog left standing; the very next operation succeeds once an accepted state is restored |
| Photoshop already showing a dialog | `PhotoshopBlockingDialog`, zero input of any kind, dialog still there afterwards |
| Meitu editor holding an unexpected asset | `MeituUnknownState`, zero input, zero invocations; ready again as soon as the editor is empty |
| Meitu already showing a dialog | Refused, zero input, dialog untouched |

Executable identity verification was not weakened anywhere; the relaunch tests exist specifically
to prove recovery is not a route around it.

---

## 12. Controlled live recovery smoke

`PRINTFLOW_RECOVERY_SMOKE=1`, on the accepted Production workstation, committed configuration, no
mode override, synthetic input only.

### 12.1 First attempt — a real environment finding

The first run **stopped at job C** (the first Meitu job):

```text
running …_A (PhotoshopOutput) ...        → succeeded
running …_B (PhotoshopOutput) ...        → succeeded
running …_C (Enhancement) ...
  REFUSED : MeituLaunchFailed
  detail  : Meitu process 21068 exited before presenting a window.
```

Diagnosis: **Meitu auto-updated on this workstation on 2026-09-02 12:15**, to
`…\MeituApp\XiuXiu\7.8.8.0\XiuXiu.exe`. The accepted preset names `…\7.8.7.5\XiuXiu.exe`, which is
still on disk and whose digest still matches (executable identity *passed*). But with a 7.8.8.0
instance already running, launching the accepted 7.8.7.5 binary hands off to it through Meitu's
own single-instance mechanism and exits immediately.

This is an environment drift, not a PrintFlow defect, and PrintFlow behaved exactly as §9 and §13
require: it refused, created no Revision, and **released the automation lock** (the smoke asserts
that on the refusal path before it fails).

Confirmed by controlled experiment: with the 7.8.8.0 instance running, launching 7.8.7.5 produces
no process of its own; with nothing running, 7.8.7.5 launches and presents its own window
normally. The accepted binary was left running.

Nothing was reinstalled, no preset was re-signed, no identity check was relaxed.

### 12.2 Second attempt — PASS

```text
=== workstation before ===
external apps running   : 2
sessions                : 28    Comparison: 70    Quarantine: 2

=== process 1 ===
gate                    : VerifiedEnvironmentGate
Meitu adapter           : meitu-xiuxiu-production-v1 / Production
Photoshop adapter       : photoshop-cc2019-production-v1 / Production
preset identity         : printflow-workstation-v1 1.15.0 (3392873ED0CA)
verified: True   blocking failures: 0   gate(Production): ALLOWED
```

| Job | Step | Adapter | Apps | Output | SHA-256 | Pixels / bytes | Attempts | Lock |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| A | PhotoshopOutput | `photoshop-cc2019-production-v1` | 2→2 reused | `Sessions/S_20260902T055641Z_69e26cd1/Working/01a060b0-…/…_A_51mm_CMYK_W.tif` | `BD73B074…C59CB22` | 600×400 / 2 452 724 | 1 | free → free |
| B | PhotoshopOutput | `photoshop-cc2019-production-v1` | 2→2 reused | `Sessions/S_20260902T055654Z_2624191c/Working/01a060b1-…/…_B_51mm_CMYK_W.tif` | `94C3D63A…57ACB973` | 600×400 / 2 452 724 | 1 | free → free |
| C | Enhancement | `meitu-xiuxiu-production-v1` | 2→2 reused | `Sessions/S_20260902T055706Z_2255e6a7/Working/01a060b1-…/…_C_HD.png` | `5207F744…F643ECC` | 1280×960 / 1 159 183 | 1 | free → free |
| D | Enhancement | `meitu-xiuxiu-production-v1` | 2→2 reused | `Sessions/S_20260902T055726Z_798a50f2/Working/01a060b1-…/…_D_HD.png` | `5207F744…F643ECC` | 1280×960 / 1 159 183 | 1 | free → free |
| E | PhotoshopOutput | `photoshop-cc2019-production-v1` | 2→2 reused | `Sessions/S_20260902T055741Z_31469f29/Working/01a060b1-…/…_E_51mm_CMYK_W.tif` | `7AC4C260…56A8210A` | 600×400 / 2 452 724 | 1 | free → free |

All five reached `ReviewRequired`. **No external application was launched at any point** (2 → 2
throughout): every job attached to the instance the previous one left running, which is the
reuse/re-attachment observation §16 asks for.

C and D share a digest: identical synthetic input, deterministic enhancement, identical output —
at two different paths under two different sessions, which is the isolation claim rather than a
contradiction of it.

**Restart** (between jobs D and E), with Photoshop and Meitu deliberately left running across the
boundary:

```text
external apps still up  : 2
lock before recovery    : free
lock after recovery     : free
attempts interrupted    : 0
files quarantined       : 0
recovery failures       : 0

A  PhotoshopOutput  ReviewRequired  sha BD73B074…  attempts 1
B  PhotoshopOutput  ReviewRequired  sha 94C3D63A…  attempts 1
C  Enhancement      ReviewRequired  sha 5207F744…  attempts 1
D  Enhancement      ReviewRequired  sha 5207F744…  attempts 1
```

Every earlier output was still on disk and still hash-identical, read through a graph that shared
nothing with the one that produced it. Readiness re-verified `Ready` in process 2, and job E ran
through it.

### 12.3 Live evidence — workstation before/after

```text
external apps running   : 2 (was 2)
sessions before/after   : 28 / 33
session directories new : 5
  S_20260902T055641Z_69e26cd1   S_20260902T055654Z_2624191c
  S_20260902T055706Z_2255e6a7   S_20260902T055726Z_798a50f2
  S_20260902T055741Z_31469f29
session directories lost: 0
Comparison files        : 70 (was 70)
Quarantine files        : 2 (was 2)
```

Residual Photoshop state afterwards, as Policy A predicts:

```text
PF_11600A_20260902-175639-FA4F6589_E.png @ 100% (图层 1, W1/8) *
```

PrintFlow's own synthetic working document, W1 applied, unsaved. The validated TIFF for job E is a
separate, already-written file.

Only bounded operational evidence was recorded: session directory names, step states, adapter ids,
launched-or-reused, workspace-relative output paths, hashes, dimensions, byte lengths, attempt
counts, lock state, process counts and the census. **No screenshots, no machine inventory, no
registry dump, no raw evidence chain, no unrelated session content.**

### 12.4 New synthetic artefacts created

* From the **failed** first run: `D:\PrintFlowStudio\QA\Epic11600A\20260902-175419-33E52D03\` and
  3 session directories (jobs A and B succeeded; job C's session holds the refused attempt, no
  Revision). Left in place — §18 forbids introducing cleanup to tidy test count.
* From the **passing** run: `D:\PrintFlowStudio\QA\Epic11600A\20260902-175639-FA4F6589\` and the
  5 session directories listed above.

All sessions carry the `PF_11600A_` prefix. Both QA directories hold their own throwaway SQLite
database; **no row was written to the operator's installation database.**

### 12.5 Customer artefacts

**No customer artefact was touched.** Every input was a PNG generated at run time inside the QA
directory. No existing session was read, written, moved or removed (0 lost, census identical for
Comparison and Quarantine). Photoshop was holding an unrelated operator document
(`color_calibration_ (1).pdf`) throughout the first run — it was never opened, closed, saved or
made active by PrintFlow, and the second run proceeded past it through the ordinary
`KnownEditorWithOtherDocument` path. The unknown/unsaved-document scenario was **not** manufactured
live; it is proved synthetically, as §16 requires.

---

## 13. Tests added / changed

### Added — `tests/PrintFlow.Tests/Integration/Persistence/SessionHygieneAndRecoveryTests.cs` (18)

Workflow level, two pieces of work through one service instance, one workspace, one database.

* Photoshop → Photoshop, Meitu → Meitu, and Photoshop ⇄ Meitu in both orders
* identically named sessions share no artefact path and no record
* running a second session mutates nothing the first owns (digest comparison)
* an unhandled fault closes the attempt and releases the lock
* a faulted job does not block the next session
* retry after a fault is a new distinguishable attempt; the fault stays in history
* a fault never overwrites an already approved result
* no incomplete output reaches `ReviewRequired` (zero-byte / missing / timeout)
* retry after a partial write never adopts the leftover
* restart preserves a `ReviewRequired` session, which can still be approved
* restart after a fault keeps the failure; the next attempt is clean
* a startup with nothing to recover deletes and quarantines nothing
* a lock left by a dead PrintFlow process is released on the next start

### Added — `tests/PrintFlow.Tests/Integration/Automation/ExternalStateHygieneTests.cs` (10)

Adapter level, production adapters, faked operating system. Covers everything in §11's table plus
the structural assertion that the composed production operation closes no document.

### Added — `tests/PrintFlow.Tests/Smoke/ProductionRecoveryWorkstationSmoke.cs` (1, inert by default)

The §16 sequence, opt-in via `PRINTFLOW_RECOVERY_SMOKE=1`.

### Changed

`src/PrintFlow.Workflow/Services/SessionService.cs`, `Strings.resx`, `Strings.zh-CN.resx`,
`Strings.cs` — the §7 containment and its operator wording. No test was changed to accommodate it;
the whole existing suite passed unmodified.

---

## 14. Results

| Gate | Result |
| --- | --- |
| Targeted — `SessionHygieneAndRecoveryTests` | 18 passed / 0 failed |
| Targeted — `ExternalStateHygieneTests` | 10 passed / 0 failed |
| Build (`PrintFlowStudio.sln`) | **0 warnings / 0 errors** |
| **Complete suite** | **10088 passed / 0 failed / 0 skipped** (2 m 10 s) |
| Live recovery smoke | **PASS** (second attempt; see §12) |

### Why a complete suite was run

§20 makes it mandatory here: the change touches `SessionService`'s producing-step path, which is
shared automation-lock, recovery and persistence infrastructure common to every adapter-backed
step. Baseline was 10059; the 29 added tests account for the whole difference, and no existing
test needed modification.

### Dependency / security

`dotnet list package --vulnerable --include-transitive`: **no vulnerable packages** in any project.

`--deprecated`: `xunit 2.9.3` (Legacy → `xunit.v3`), test project only. Unchanged and deliberately
out of scope — §22 keeps the xunit.v3 migration out of this slice.

### Adapter mode

`appsettings.json` is untouched:

```json
"Adapters": { "Mode": "Production" }
```

Never overridden, not even to make a recovery test easier. The live smoke asserts the committed
value is `Production` before it runs anything.

### Git state

Branch `master`, based on `618fe9d` (Epic 11500 Part D report). No amend, no rebase, no rewrite, no
push. One new local commit for 11600-A.

---

## 15. Remaining risks before soak testing

1. **Meitu version drift is live and unresolved.** The workstation now has 7.8.8.0 installed while
   the accepted preset names 7.8.7.5. Both binaries are on disk and the accepted one still matches
   its signature, but **if a 7.8.8.0 instance is running, every Meitu operation will fail
   `MeituLaunchFailed`** — the accepted binary hands off and exits. PrintFlow fails safely, but the
   shop loses the ability to enhance until someone closes the newer instance. This needs a decision
   outside this slice: either suppress Meitu auto-update on the workstation, or revalidate and
   re-sign the preset against 7.8.8.0. **This is the highest-priority follow-up.**
2. **Photoshop accumulates dirty PrintFlow documents.** Policy A leaves one open per job, and
   nothing closes them. Over a long soak Photoshop will hold many unsaved working documents; memory
   pressure and the eventual close-time prompt storm are untested. A bounded "close documents this
   process itself opened, at a safe point, never answering a prompt" rule is the obvious follow-up,
   but it is a lifecycle change and does not belong here.
3. **Session and QA directory growth.** 33 sessions and two QA trees now sit under
   `D:\PrintFlowStudio`. Deliberately not addressed (§18). Retention is a separate Epic.
4. **The fault containment is proved synthetically only.** By construction — a live unhandled
   exception cannot be manufactured on the production workstation without corrupting it, which §16
   forbids.
5. **Multi-instance external applications remain a hard refusal.** Two Photoshop or two Meitu
   processes from the accepted executable stop everything with "PrintFlow will not choose between
   them". Correct, but under soak conditions an orphaned instance would halt the shop until an
   operator noticed. Worth a diagnostics surface, not a policy change.
6. **Mid-operation application death is not covered live.** Killing Photoshop during a W1 Action on
   the production workstation was judged out of bounds. The synthetic seams cover launch failure,
   target loss and unknown state; genuine mid-Action process death is soak-test territory.

---

**11600-A PASS WITH NOTES — SESSION HYGIENE AND RECOVERY VERIFIED**

Notes: one real defect was found and fixed (an unhandled fault left the automation lock
permanently held by a live process); and the live smoke exposed a Meitu version drift on the
accepted workstation which PrintFlow handles safely but which will block production enhancement
whenever the updated instance is running, and which requires an environment or preset decision
before soak testing.
