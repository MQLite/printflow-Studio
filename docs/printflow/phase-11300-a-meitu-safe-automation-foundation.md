# PrintFlow Studio — Epic 11300 Part A: Meitu safe automation foundation

**Report date:** 24 August 2026
**Scope:** production Meitu adapter shell, process/window identity, guarded input, safe-state model,
working-copy open path, evidence seam, focused tests, controlled workstation smoke.
**Not in scope:** Enhancement, Background Removal, completion detection, export, Revision creation.

---

## 1. Production adapter shell

`ProductionMeituProcessor` implements the existing `IMeituProcessor` seam and declares
`AdapterExecutionMode.Production`, so `IEnvironmentGate` stays authoritative. `AdapterId` is
`meitu-xiuxiu-production-v1`, distinct from `fake-meitu-v1`; the fake adapter is unchanged.

`ProcessAsync` — the workflow seam — **always fails**, with `AdapterUnavailable` and context naming
the implemented scope. That is deliberate, not an omission: a success from it would create a
Revision on the strength of having opened a file (§24). The Part A capability lives behind a
separate, Infrastructure-only `IMeituAutomationFoundation` (`EnsureReadyAsync`,
`OpenWorkingCopyAsync`) that no workflow code can name.

Committed configuration still reads `Adapters:Mode = Fake`. `ServiceRegistration` still throws for
`Production`, now saying why: the Meitu adapter is foundation-only and no production Photoshop
adapter exists. Nothing production runs merely because the class exists.

Meitu's executable path, SHA-256, accepted version, UI language and accepted window titles are read
from the signed workstation preset (`meituContract`). Nothing is hard-coded and no installed
executable is searched for. `EnsureReadyAsync` hashes the binary at the accepted path and refuses it
if the digest has moved — so a silent Meitu upgrade stops automation instead of running it against an
unvalidated UI.

## 2. Process and window identity

`IExternalAppWindowLocator` (Infrastructure) finds processes **by executable path**, returns
top-level windows attributed by `GetWindowThreadProcessId`, reads the foreground, and activates a
window. Before any interaction PrintFlow establishes: process alive under the same id *and start
time*, window handle still valid, window still owned by that process id, window visible and enabled.
Title, class and bounds are recorded as evidence, never as the authority — a title can be copied by
any application, and a handle can be reused by an unrelated one after its window is destroyed.

There is no "find any window and click it" API, and no window type is nameable outside
`PrintFlow.Infrastructure`.

## 3. Guarded input strategy

Priority order per §4: UI Automation element first, verified shortcut second, and no coordinate path
at all. `NativeMethods` declares `SendInput` and nothing else that produces input — `keybd_event`,
`mouse_event`, `SetCursorPos` and `SendKeys` appear nowhere in `src/`, asserted by test.

The ordering rule is **verify, then act**. `GuardedMeituUiDriver.VerifyTargetAsync` re-reads process
liveness, window ownership and the foreground from the OS immediately before every input-producing
operation and abandons the input on any mismatch, returning `MeituTargetLost` with
`inputSent=false`. `Win32ScopedInputSink` repeats the foreground check inside the primitive itself,
so a future caller that forgets cannot produce an unguarded keystroke. Element invocation verifies
twice — once before the tree walk and once after, because a walk takes long enough for focus to move.

Activation never trusts its own return value: `SetForegroundWindow` may be refused by Windows, so the
driver polls `ReadForeground` until the OS agrees, and fails closed if it never does.

## 4. Safe-state model

`MeituStartingState`: `NotRunning`, `KnownWelcome`, `KnownEditorEmpty`,
`KnownEditorWithExpectedWorkingCopy`, `Busy`, `KnownModal`, `Unknown`. Safety is an **allow-list**
(`IsSafeStartingState`), so a value added later is unsafe by default.

`MeituStateClassifier` is a pure function of a `MeituObservation` and the signed baseline, so every
recognition rule is unit-tested with no window and no screen. Classification order is the safety
order: blocking states are decided before content states, then the handed-over working copy, then the
welcome page, then `Unknown`.

Recognition inputs come from the signed evidence chain, not from invented assumptions.
`appsettings.json` names the preset and its digest; the preset names `apps\meitu\clean-start.json`
**and its SHA-256**; that file supplies the exact clean-start window title and the stable structural
markers. Editing the evidence file without breaking the manifest hash is therefore not possible.
Markers are extracted from Epic 11000's prose by a deterministic rule (`MeituMarkerExtraction`) with
its own tests, rather than transcribed into a second copy that could drift.

`KnownWelcome` requires the **exact** signed clean-start title plus at least four distinct markers.
The exact-title rule came out of the smoke and matters: Meitu's editor is titled with the start
page's title plus a feature suffix and keeps showing much of the same navigation, so markers alone
would read an operator's open document as a clean start page.

Unknown, `Busy` and `KnownModal` all stop. Nothing is closed, dismissed or dragged into a different
state to make the answer come out safe. A blocking dialog produces `MeituBlockingDialog` and an
operator action.

## 5. Working-copy open path

`OpenWorkingCopyAsync` refuses anything that is not a `WorkspaceArea.Working` reference **before**
resolving a path, so the customer original, the source snapshot and approved outputs never reach an
external application. The path is then resolved through `IWorkspace`, the only path join in the
system.

The open sequence: verify target → invoke the signed start-page entry (falling back to a verified
`Ctrl+O` only when the element exposes no automation pattern, i.e. when nothing has happened) → wait
for a file dialog that is **both** a Windows common dialog (`#32770`) **and** owned by the verified
Meitu process → write the path through the value pattern, not keystrokes → re-verify the dialog still
belongs to Meitu → invoke Open. Confirmation is a positive observation of PrintFlow's own file name
in Meitu's UI, never "the dialog closed".

## 6. Evidence and failure behaviour

`IAutomationEvidenceSink` captures a single identified window via `PrintWindow` — not the desktop —
so an unrelated application or a customer's open file is never photographed as a side effect. It
writes a PNG locally through WIC and returns a path; it has no upload, no copy-to-workspace and no
attach-to-report capability. A failed capture never replaces the failure it was documenting
(asserted by test). Evidence lives outside the session workspace, is deleted by the smoke, and is not
committed.

Failure codes added (§21), each with English and zh-CN operator messages and stable English names:
`MeituNotInstalled`, `MeituLaunchFailed`, `MeituWindowNotFound`, `MeituTargetLost`,
`MeituUnknownState`, `MeituBlockingDialog`, `MeituOpenInputFailed`. Raw Win32 text is converted to
structured failures at the Infrastructure boundary and never shown to the operator.

## 7. Explorer / wrong-target regression

The explicit invariant from §19 — if Explorer is foreground, automation input is **none** — is
covered by tests that assert a *recorder is empty*, not merely that a failure was returned. The
`RecordingInputSink` used to prove the driver's guard is deliberately unguarded, so a regression that
removed the driver's check would fail loudly rather than be masked by the sink's own.

Covered: Explorer foreground (keystroke and element invoke), handle reused by another process,
process exited, activation refused by Windows, and a perfectly convincing `#32770` file dialog owned
by a *different* process — the file-dialog form of the same accident.

## 8. Automated tests

6752 tests pass, up from the 6660 baseline (+92). New coverage:

| Area | Cases |
| --- | --- |
| Guarded driver (`GuardedMeituUiDriverTests`) | correct target allowed; Explorer foreground → no keystroke, no invoke; `inputSent=false` recorded; reused handle; exited process; activation refused; dialog owned by another process never typed into; cancellation; element name absent from signed markers |
| Input primitive (`ScopedInputSinkTests`) | the real `Win32ScopedInputSink` refuses when another app, nothing, or no target holds the foreground |
| Production adapter (`ProductionMeituProcessorTests`) | `ProcessAsync` always fails for both operations; missing executable; changed executable hash; existing safe instance reused with zero launches; two candidates never chosen between; unrecognised screen; blocking dialog never dismissed; failed evidence capture; launch timeout; launch success; cancellation; Working-copy-only (Source/Approved/Rejected/Logs all refused); confirmation by name; missing working file |
| Classifier (`MeituStateClassifierTests`) | marker thresholds; look-alike titles; feature-suffixed title never a start page; owned dialog and disabled window outrank content; expected-file recognition; editor is `Unknown`, not `KnownEditorEmpty`; safe-state allow-list over the whole enum |
| Marker extraction, preset chain | the real Epic 11000 marker list; English-only text yields nothing; manifest hash mismatch; evidence file edited after signing; missing evidence file; contract without a digest; evidence without a title |
| Gate (`ProductionAdapterGateTests`) | the **real** production adapter behind the real gate in the real session flow — refused with `EnvironmentNotVerified`, zero calls, no lock taken, no attempt row |
| Architecture (`AutomationBoundaryTests`) | automation types declared and named only in Infrastructure; Domain/Workflow see no UIA assembly; no blind-input API anywhere in `src/`; P/Invoke only in `NativeMethods`; no hand-written `unsafe`; Workflow's Meitu vocabulary is exactly `IMeituProcessor`/`MeituOperation`/`MeituRequest`; composition root registers no production adapter |

## 9. Controlled workstation smoke

Opt-in and inert by default (`PRINTFLOW_MEITU_SMOKE=1` for the read-only half,
`PRINTFLOW_MEITU_SMOKE_OPEN=1` to allow the open step), so a routine `dotnet test` can never drive
Meitu. Run via the §22 composition seam (`MeituAutomationComposition.CreateFoundation`), which
produces only an `IMeituAutomationFoundation` — no session, no repository, no `IMeituProcessor`
registration — rather than by weakening `FoundationEnvironmentGate`. Synthetic 320×240 PNG under
`%TEMP%\PrintFlowMeituSmoke\<guid>\Sessions\S_SMOKE\Working\A_1\`; Documents, Desktop, Downloads and
`D:\PrintFlowStudio` were never navigated or written (the preset manifest was read, read-only).

**Result — identify and classify: PASS.**

```
accepted executable  : C:\Users\admin\AppData\Local\MeituApp\XiuXiu\7.8.7.5\XiuXiu.exe
accepted version     : 7.8.7.5 (zh-CN)   binary SHA-256 matched the signed preset
signed markers (10)  : AI变清晰, AI商品套图, AI消除, 图片编辑, 批处理, 抠图, 更多工具, 海报设计, 美图秀秀, 证件照
process id           : 16844
window handle        : 0x1302CE
window title / class : '美图秀秀' / Qt51517QWindowIcon
window bounds        : 428,148 1064x744
state                : KnownWelcome
matched markers      : 9 of 10
launched by PrintFlow: True
```

A separate run against an already-open Meitu reported `launched by PrintFlow: False` with zero
launches, confirming the §15 reuse rule on the live workstation.

**Result — open the working copy: STOPPED, failed closed.**

```
RESULT : MeituOpenInputFailed
detail : No file dialog owned by Meitu process 16844 appeared within 15 s; nothing was typed.
```

The smoke stopped there. No Enhancement, no Background Removal, no export, no Revision. No customer
or source file was read or modified. Evidence was captured to the local temp evidence directory and
deleted with the rest of the synthetic workspace; nothing was committed or uploaded.

## 10. Defects found

1. **UIA snapshot depth was far too shallow (fixed).** The first run classified the live start page
   as `Unknown` because a depth-6 walk surfaced only 3 of the 10 signed markers. Meitu is a Qt
   application whose start-page entries sit well below that. The depth limit is now a constructor
   parameter defaulting to 24, which surfaces 9 of 10. The failure direction was safe throughout —
   too-shallow reading made PrintFlow refuse to act, never act on an unrecognised screen.

2. **A feature-suffixed title could have been read as a clean start page (fixed).** The classifier
   originally accepted any title with an accepted prefix. Meitu's editor is `美图秀秀-图片编辑` and
   retains much of the start page's navigation, so markers plus a prefix match could have declared an
   operator's open document a safe starting state. `KnownWelcome` now requires the exact title the
   signed clean-start evidence records.

3. **The open path does not work on Meitu 7.8.7.5 (open, deferred to Part B).** Read-only diagnostics
   pinned the cause exactly: the element matching the signed marker `图片编辑` is
   `ControlType.Text` with automation id
   `StartupWidget…functionWidget.CardButton.titleLabel` — the card's *title label*, not the card
   button. It advertises `InvokePattern`, so invoking it reports success while doing nothing, and no
   file dialog ever appears. The `Ctrl+O` fallback did not produce one either. The fix is to target
   the invokable `CardButton` ancestor, but what that card actually opens — a `#32770` common dialog
   or Meitu's own picker — has no signed evidence, and guessing is exactly what §2 forbids.

4. **`KnownEditorEmpty` is unreachable (known gap, by design).** Epic 11000 signed a clean-start
   signature for the welcome page only. An editor that merely looks empty is an absence of evidence,
   so it classifies as `Unknown` and stops. A test pins this so it cannot be made reachable without
   the evidence.

5. **`Busy` is likewise unreachable.** The processing-overlay captures (`变清晰中，请稍候...`,
   `智能识别中...`) live in Epic 11000 workflow evidence files that the preset does not vouch for, so
   they are outside the verified chain. A processing overlay currently classifies as `Unknown`, which
   stops — the conservative direction.

6. **Smoke-harness artefact, not adapter behaviour.** A Meitu instance launched from inside a
   `dotnet test` host is a child of that host and is killed when its job object closes. One
   intermediate run therefore showed Meitu vanishing mid-smoke. This does not apply to the WPF
   application and is a property of how the smoke is invoked.

7. **Meitu was left open.** Per §28: PrintFlow launched instance 16844 during the final smoke and left
   it running on the start page. Nothing force-terminates Meitu, by design.

## 11. Deferred to 11300-B

* Resolve the invokable `CardButton` ancestor for the start-page entry, with operator-supervised
  capture of what it opens and of the resulting dialog's identity (defect 3).
* Capture signed evidence for the empty-editor and processing-overlay signatures so
  `KnownEditorEmpty` and `Busy` become reachable (defects 4 and 5).
* Verify the editor's UIA marker set against the welcome threshold once that evidence exists.
* Enhancement button automation, completion detection and export; Background Removal and cutout
  export; Meitu-specific retry; Stop / force-termination UX; production success benchmark; the
  20-example timing benchmark; Photoshop; Maintop.
* Full workstation verification remains Epic 11500; `FoundationEnvironmentGate` was not weakened.

## 12. Git state

Branch `master`, 16 commits ahead of `origin/master`, nothing pushed, no history rewritten.

Changed: `FailureCode.cs`, `ServiceRegistration.cs`, `Strings.cs` / `Strings.resx` /
`Strings.zh-CN.resx` / `DisplayNames.cs`, `PrintFlow.Infrastructure.csproj`
(`AllowUnsafeBlocks` for the `LibraryImport` generator), `ValueObjectTests.cs`.

Added: `src/PrintFlow.Infrastructure/Automation/` (7 files),
`src/PrintFlow.Infrastructure/Adapters/Meitu/` (10 files),
`src/PrintFlow.Infrastructure/Preset/VerifiedJsonFile.cs`, and the test files listed in §8.

Not committed: screenshots, window captures, the synthetic working image, smoke transcripts, the
runtime database, logs, and any machine-local Meitu configuration. No screenshot exists in the
repository and none left the workstation.

### Gates

```
dotnet restore --locked-mode   OK
dotnet build                   0 warnings, 0 errors
dotnet test                    6752 passed, 0 failed, 0 skipped
dotnet list package --vulnerable --include-transitive   no vulnerable packages
```

---

## Verdict

The exact Meitu process and window can be identified and are re-verified before every interaction; no
input is ever sent to an unverified foreground target; unknown, busy and modal states fail closed
without anything being clicked or dismissed; the working-copy boundary is enforced before a path is
even resolved; no customer or source file was read or modified; no Revision can be produced by this
adapter; Production mode remains blocked by the environment gate and unregistered in the composition
root; and all build, test and audit gates are green.

The one goal not demonstrated end to end is opening the working copy on the live workstation, which
stopped — correctly and with nothing typed — on a precisely diagnosed Meitu UI-tree issue that needs
signed evidence Part A does not have.

**11300-A PASS WITH NOTES — READY FOR MEITU ENHANCEMENT AUTOMATION**
