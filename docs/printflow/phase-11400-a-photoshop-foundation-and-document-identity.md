# Epic 11400 Part A — Photoshop Production Foundation and Exact Document Identity

**Verdict: 11400-A PASS — READY FOR PHOTOSHOP W1 ACTION AUTOMATION**

Scope delivered: PrintFlow can verify the accepted Photoshop binary, attach to or launch it,
identify one accepted top-level window, classify the screen, open exactly one managed Working
file, and prove by **absolute path** which document Photoshop is holding — without a single blind
desktop input. No W1 Action runs, no resize or colour conversion happens, no TIFF is written and
no Revision is creatable.

---

## 1. Existing Photoshop architecture audit

What already existed, and was reused rather than duplicated:

| Concept | Where it lives | Part A's use |
| --- | --- | --- |
| `IPhotoshopOutputProcessor` (workflow seam) | `PrintFlow.Workflow/Ports/AdapterPorts.cs` | Implemented, **fail-closed** (§19) |
| `PhotoshopRequest` / `AdapterOutput` | same | Unchanged; no new request contract |
| `FakePhotoshopOutputProcessor` | `Infrastructure/Adapters/Fake` | Untouched; still the registered `Fake` adapter |
| `SessionService` `AdapterKind.Photoshop` path | `Workflow/Services/SessionService.cs:951` | Untouched; still gated by `IEnvironmentGate` |
| W1 selection state (`WhiteUnderbaseBranch`) | `Domain/Outputs` | Read-only; not consumed by Part A |
| Print Dimensions | `Domain/Outputs/PrintDimensions.cs` | Not consumed by Part A |
| Working/reference rules (`WorkspaceArea`, `IWorkspace`) | Domain + Workflow | The **only** path-resolution route (§8) |
| Output naming | `OutputFileNaming` + preset patterns | Not consumed by Part A |
| Epic 11000 Photoshop baseline | preset `photoshopContract` + 3 evidence files | Inherited unchanged |
| Automation seams (`IExternalAppWindowLocator`, `IScopedInputSink`, `IAutomationEvidenceSink`, `IUiElementProvider`) | `Infrastructure/Automation` | Reused as-is |
| Architecture tests | `tests/Architecture/AutomationBoundaryTests.cs` etc. | Extended, not replaced |

What Part A genuinely needed and did not exist:

- a Photoshop baseline/evidence provider (the Meitu one is Meitu-specific);
- Photoshop screen classification and a document-identity rule;
- a guarded Photoshop UI driver;
- **a way to read and actuate a positively identified Win32 child control** — forced by the live
  finding in §5 below, because `IUiElementProvider` cannot drive Photoshop's dialogs at all.

Nothing Meitu-specific was reused blindly. `GuardedMeituUiDriver`, `MeituStateClassifier` and
`MeituBaseline` were read for their *safety principles* and re-expressed for Photoshop; no Meitu
class is referenced from the Photoshop adapter.

---

## 2. Executable identity

Verified before any window is touched, all three together (`ProductionPhotoshopOutputProcessor.VerifyExecutableIdentity`):

| Fact | Accepted value | Live re-verification |
| --- | --- | --- |
| Path | `D:\Adobe Photoshop CC 2019\Photoshop.exe` | present |
| Product version | `20.0` | exact |
| File version | `20.0 (20200706.r.120 2020/07/06: 1208496)` | exact |
| SHA-256 | `81EE8930FC1E28637B501866A8B946FA0740C376CDA4302FEA61AA82806A80C5` | **exact** |

Fails closed on any difference, with `inputSent=false` in the failure context. A differing local
installation is a refusal, never a reason to update the accepted baseline. The preset's
`excludedInstallations` (the 2026 install and the stale Start-menu shortcut) are reported in the
failure so an operator can see *which* binary was found.

---

## 3. Process and window rule

Reuses the Epic 11300 safety principles, re-verified for Photoshop:

- PID plus process start time (`ExternalProcessRef`), and `IsAlive` before every interaction;
- **more than one candidate process is a refusal**, never a choice — the wrong pick would drive an
  instance holding someone else's work;
- window ownership decided by `GetWindowThreadProcessId`, never by title;
- the accepted window must additionally be of the signed class `Photoshop`; more than one match is
  a refusal. This matters concretely: Photoshop's process owns dozens of windows — `OWL.*` layer
  groups, `PSToolTip`, `CefBrowserWindow`, `NVOpenGLPbuffer`, `OleDdeWndClass`,
  `PlugPlugDummyWindow`, ~150 `ComboLBox` and IME stubs — and exactly one is the application frame;
- visible + enabled required; handle reuse refused by re-reading the owning PID;
- foreground verified immediately before every keystroke, inside `Win32ScopedInputSink` where a
  caller cannot skip or reorder it;
- every control read/write/press re-verifies the control's process, class, visibility and enabled
  state inside `Win32VerifiedControlSink.Verify`.

Shared-seam failure codes are translated into the Photoshop vocabulary at the adapter boundary, so
an operator never reads `MeituTargetLost` after a Photoshop step (§18). The shared Meitu classes
were not modified to achieve this.

---

## 4. Starting-state model

`PhotoshopStartingState`, decided by `PhotoshopStateClassifier` — a pure function of an observation
and the signed baseline, with no window, process or screen involved.

| State | Recognised by | Safe to open from |
| --- | --- | --- |
| `NotRunning` | no process from the accepted executable | — (launch) |
| `KnownStartScreen` | visible `OWL.WelcomeScreenView` + exact no-document title | yes |
| `KnownEditorNoDocument` | all editor chrome classes, **neither** marker, exact no-document title | yes |
| `KnownEditorWithExpectedDocument` | `OWL.Document` + title names the expected file + probe-supplied path | yes |
| `KnownEditorWithOtherDocument` | `OWL.Document`, anything else | yes (§21) |
| `Busy` | **declared but unreachable** — no signed busy signature exists | no |
| `KnownModal` | a **titled** owned visible window, or a disabled main window | no |
| `Unknown` | everything else | no |

Ordering is part of the rule: modal is decided **before** any content marker, because content stays
visible beneath a dialog. `Unknown` and `KnownModal` stop; nothing is dismissed, closed or
navigated. An empty editor is recognised *positively* — "nothing else matched" is never evidence of
an empty editor.

`Busy` unreachability is asserted by a test that runs every observation shape and requires none to
classify as `Busy`, so it cannot be wired up later without the evidence to back it.

---

## 5. Open mechanism — and the live finding that decided it

**Structural UI Automation was tried first and does not exist on this workstation.**

Photoshop CC 2019's main window exposes **exactly one** UIA element — the window itself — in both
the control view and the raw view: empty `AutomationId`, zero children, a degenerate bounding
rectangle. `FindAll(TreeScope.Subtree, TrueCondition)` returns 1. There is no menu bar, no
home-screen button, no document tab, no panel.

So the §9 keyboard route was evaluated against each of its conditions, and all were met:

| §9 condition | How it is met |
| --- | --- |
| exact process/window/foreground re-verified immediately before input | `SendGuarded` re-verifies the target; the foreground check is inside `Win32ScopedInputSink` |
| shortcut stable and specifically signed/documented | `Ctrl+O` recorded in `open-file-dialog.json`, read from the signed chain |
| no mouse coordinates | no coordinate anywhere; asserted by an architecture test |
| the file dialog positively owned/identified | owner is the verified main window, class `#32770`, exact title `打开`, same PID |
| exact full path written and read back before confirm | `WM_SETTEXT` → `WM_GETTEXT` → ordinal whole-string compare → `BM_CLICK` once |

The keystroke is never sent because Photoshop conventionally supports Ctrl+O; it is sent only after
the foreground has been read and found to be the verified window.

---

## 6. File-dialog authority

Photoshop raises a standard `#32770` Open dialog, owned by the main window and disabling it.
Signed in `open-file-dialog.json`:

| Element | Control id | Class |
| --- | --- | --- |
| filename field | 1148 | `ComboBoxEx32` |
| Open | 1 | `Button` |
| Cancel | 2 | `Button` |

**A second live finding forced the mechanism.** UIA exposes every one of these controls as a
pattern-less `Pane`: the filename field has no `ValuePattern` and the buttons have no
`InvokePattern`, so `IUiElementProvider` can neither write the path nor press the button. Each
element does carry a real `NativeWindowHandle`.

Hence the new `IVerifiedControlSink` seam: `WM_SETTEXT` / `WM_GETTEXT` / `BM_CLICK` addressed to a
specific child-control handle that has already been shown to belong to the verified process. This
is *safer* than a keystroke, not a weakening — unlike `SendInput`, a window message cannot be
delivered to whatever happens to hold focus. The seam is deliberately tiny: no coordinate, no
desktop enumeration, no free-form message primitive, and no method that takes a control the caller
has not located through it.

Sequence: locate filename → locate confirm → write exact path → **read back** → ordinal compare →
press Open once. A read-back mismatch cancels the dialog and refuses with `confirmPressed=false`;
without that check, pressing Open would act on whatever the dialog already had selected. No Recent
Files, no remembered-folder assumption, no Explorer navigation, no coordinates.

---

## 7. Exact document-identity mechanism

Opening is not success. Identity requires **two independent readings that must both agree**.

**Reading 1 — window title.** A loaded document retitles the frame to
`<basename> @ <zoom> (<layer>, <mode>)`, e.g.
`PF_PSFOUNDATION_5D365FCA2DDB.png @ 100% (图层 1, RGB/8)`. This corroborates Epic 11000's
`document-workspace.json`, which recorded the same shape in task 11003. It carries a **basename
only** — no folder.

**Reading 2 — read-only Save As identity probe.** `Ctrl+Shift+S` raises a `#32770` titled `另存为`,
owned by the main window:

| Element | Control id | Class | Reports |
| --- | --- | --- | --- |
| filename | 1001 | `Edit` | the document's exact basename |
| address bar | 1001 | `ToolbarWindow32` | `地址: <the document's own folder>` |
| Cancel | 2 | `Button` | the only control ever pressed |

Both carry control id **1001** and are distinguished by class — which is why the signature records
id *and* class, and why a class mismatch is a refusal.

The folder was proven live to **follow the active document**, not to be a remembered last-used
folder: two synthetic documents opened from two different directories produced two different
addresses (`…\PrintFlowPsDiscovery` and `…\PrintFlowPsDiscovery2`). The documented
`CDM_GETFILEPATH` / `CDM_GETFOLDERPATH` / `CDM_GETSPEC` messages returned nothing usable on this
dialog, which is why the address control is read instead.

Combining the two readings yields an **absolute path**, and identity is decided on that with a
whole-string, case-insensitive comparison (Windows filename semantics). No prefix, substring or
"contains" rule exists anywhere.

**Ambiguity analysis (§12).** Basename-only identity was considered and **rejected**. The
workflow's controlled unique working-copy naming makes a collision unlikely but not impossible — an
operator's own file of the same name, or a stale copy in Downloads, would satisfy it — and the
title alone cannot tell them apart. The folder reading removes the ambiguity, so identity is not
downgraded. A folder that cannot be read is a refusal, not a fallback to the basename.

**Read-only guarantees (§13).** The probe writes nothing, changes no filename/path/format, never
presses control 1 (Save), and cancels through the positively identified Cancel control — verified
live: the surface closed, the main window was re-enabled, the title was unchanged, and no file was
created. The cancel runs whatever the reads did, so a failure never leaves Photoshop modal.

---

## 8. Wrong and stale document refusals

| Case | Result |
| --- | --- |
| expected A / observed A | accepted (`KnownEditorWithExpectedDocument`) |
| expected A / observed B (different name) | refused — `PhotoshopDocumentIdentityUnconfirmed` |
| expected A / observed A **in a different folder** | refused — the case the two-reading design exists for |
| no document | refused — the title names no document; **no surface is even raised** |
| stale previous document | refused — same comparison |
| title matches but probe supplied no path | classifier stays at `KnownEditorWithOtherDocument`; never confirmed |

Every refusal carries `w1ActionInvoked=false` and `tiffWritten=false`.

---

## 9. Foreground and target-loss safety

- **Foreground.** An unrelated foreground owner produces `PhotoshopTargetLost` with
  `inputSent=false`, and the tests assert the input recorder and the control recorders are
  **empty** — a failure result alone would not prove nothing was typed.
- **Process exit** → `PhotoshopTargetLost`, no input.
- **Window disappears** → `PhotoshopWindowNotFound`, no input.
- **Handle reused by another process** → `PhotoshopTargetLost` naming both PIDs, no input.
- **Ownership lost mid-sequence** → refused before the write and before the confirm.

No process termination is introduced anywhere. An architecture test bans `TerminateProcess`,
`.Kill(`, `CloseMainWindow` and `taskkill` from the Photoshop adapter source.

---

## 10. Shared-process and operator-work risk

**This was not hypothetical — it happened during the live smoke.** The Photoshop instance PrintFlow
attached to was holding the operator's own production work:
`Papatoetoe Stake_Design2.tif @ 12.5% (图层 1, W1/16)` — a 16-bit document with a W1 spot channel.
The window was also minimised.

PrintFlow did not assume ownership of the process:

- the readiness state was reported as `KnownEditorWithOtherDocument`, and the smoke logged
  `operator work loaded: YES`;
- the open added a document alongside; the operator's was never touched;
- `PhotoshopOpenedDocument.OtherDocumentsMayBeOpen` carries the fact forward, because it decides
  what cleanup is permitted to do;
- close re-proves identity by absolute path **immediately before** the keystroke and closes the
  **active document only** (`Ctrl+W`);
- there is no close-all, no "close Photoshop", and no termination anywhere in the seam.

The live proof is in the Phase 3 result: after closing its own synthetic document, the frame
retitled to a *different* leftover synthetic — showing that exactly one document closed and the
others, including the operator's, remained loaded.

---

## 11. Targeted tests

**905 targeted tests pass, 0 failed, 0 skipped** (`Photoshop`, `Architecture`, `Automation`,
`Preset`, `Naming` filters). 116 are Photoshop-specific. The full 8,000+ suite was **not** run, per
§1; no shared core workflow, state-machine or persistence boundary changed.

| §22 requirement | Test |
| --- | --- |
| 1. accepted path/version/hash passes | `The_accepted_path_version_and_digest_pass_identity` |
| 2. wrong hash fails before input | `A_wrong_executable_hash_fails_before_any_input` (+ version, + absent path) |
| 3. wrong process/window ownership fails | `Two_running_instances_are_refused…`, `A_window_of_the_wrong_class_is_not_the_accepted_window`, `A_reused_handle_owned_by_another_process_is_refused` |
| 4. foreground refusal sends no input | `An_unrelated_foreground_owner_causes_no_input_at_all`, `…blocks_the_identity_probe_too` |
| 5. managed Working file may be opened | `A_managed_Working_file_is_opened_and_positively_identified` |
| 6. Source/InputSnapshot/arbitrary path refused | `Only_a_managed_Working_reference_may_be_opened` (Theory over 4 areas), `The_foundation_exposes_no_path_taking_open_overload` |
| 7. expected document A accepted | `A_loaded_document_with_a_probe_confirmed_path_is_the_expected_document` |
| 8. wrong document B refused | `A_wrong_document_is_refused_and_nothing_further_happens`, `The_same_file_name_in_a_different_folder_is_refused` |
| 9. no document refused | `No_document_after_the_open_is_refused`, `With_no_document_open_the_probe_sends_nothing` |
| 10. stale document refused | `A_stale_previous_document_is_refused` |
| 11. process/window loss produces no later input | `A_process_that_has_exited_produces_no_further_input`, `A_window_that_has_disappeared_produces_no_input`, `Ownership_lost_before_the_write_stops_before_the_confirm` |
| 12. no TIFF/output Revision | `The_workflow_seam_refuses_and_produces_no_output`, `The_opened_document_record_carries_no_output_or_revision_surface`, `The_production_Photoshop_workflow_seam_constructs_no_AdapterOutput` |
| 13. no W1 action invoked | `The_Photoshop_driver_exposes_no_action_resize_or_save_capability`, `The_Photoshop_adapter_source_names_no_action_or_TIFF_artefact` |
| 14. no process-termination API | `The_Photoshop_adapter_source_contains_no_banned_input_or_termination_API` |

Only the OS is faked. The classifier, identity rule, guarded driver and production adapter are the
real implementations. The executable-identity tests use a real file with its real digest and real
version information, because the rule under test is "what is on disk agrees with what was signed"
— a stubbed hash would assert nothing. The fakes use the **real** control ids and marker classes
discovery produced; invented ids would pass against an adapter that had them wrong.

---

## 12. Live smoke

Opt-in and inert by default: `PRINTFLOW_PHOTOSHOP_SMOKE`, `…_OPEN`, `…_CLOSE`. One fresh synthetic
320×240 PNG per run, under the OS temp directory. No customer artwork.

**Result: full sequence PASS.**

```
accepted digest      : 81EE8930…A80C5            (verified)
process id           : 6272   (attached, not launched)
window title / class : 'Papatoetoe Stake_Design2.tif @ 12.5% (图层 1, W1/16)' / Photoshop
state                : KnownEditorWithOtherDocument
operator work loaded : YES

Phase 2
state                : KnownEditorWithExpectedDocument
observed file name   : PF_PSFOUNDATION_5D365FCA2DDB.png
observed folder      : …\PrintFlowPhotoshopSmoke\a3027…\Sessions\S_PSSMOKE\Working\A_1
observed full path   : …\Working\A_1\PF_PSFOUNDATION_5D365FCA2DDB.png
expected full path   : …\Working\A_1\PF_PSFOUNDATION_5D365FCA2DDB.png
identity             : EXACT MATCH
other documents open : True

Phase 3
window title after   : 'PF_PSFOUNDATION_51604F98E10F.png @ 100% (图层 1, RGB/8)'
```

No Action invoked, no resize, no colour-mode conversion, no save, no export, no output file.
Photoshop was left running, holding only the operator's document, exactly as found. Photoshop was
never closed and never terminated.

### Two live findings that changed the code

1. **Untitled owned chrome is not a modal.** Photoshop keeps `OWL.ShadowView` and the `OWL.Dock` it
   owns as *owned top-level windows* that become visible whenever the frame is restored from
   minimised, and both carry an empty title. The first open attempt refused with
   `PhotoshopBlockingDialog` over a perfectly ordinary editor. That was a refusal about PrintFlow's
   own reading rather than about Photoshop — and it was a **safe** refusal: nothing was dismissed.
   The modal rule now requires a non-empty title (or a disabled main window); both signed dialogs
   satisfy both conditions. Recorded in `window-states.json` and regression-tested.

2. **A closed dialog is not the same as a ready application.** Photoshop re-enables its main window
   a moment *after* the dialog window disappears, so a keystroke sent on the dialog's disappearance
   alone was discarded by Windows — the second identity probe found no surface and timed out. The
   dialog-close wait is now a positive observation of both conditions: the dialog is gone **and**
   the host window is enabled again. A sleep was deliberately not used.

Both were caught because the smoke refuses rather than guesses.

---

## 13. Evidence and preset impact

The Epic 11000 accepted Photoshop baseline is **unmodified**. Reverified and exact:

| Artefact | SHA-256 |
| --- | --- |
| `Photoshop.exe` | `81EE8930FC1E28637B501866A8B946FA0740C376CDA4302FEA61AA82806A80C5` |
| `apps/photoshop-2019/window-policy.json` | `CF912E8B…3080BC` (unchanged) |
| `apps/photoshop-2019/clean-start.json` | `32CB5A27…69095E` (unchanged) |
| `apps/photoshop-2019/document-workspace.json` | `EC9E0EE3…D9DEB7` (unchanged) |
| canonical `.atn` | `A04203EDEA623C0737D911601A3A005033789BD095130F02A5F8C04CBFCD83EE` (unchanged, not consumed) |

Live discovery established UI structure that production code now relies on, so per §25 it was
captured as **new immutable evidence** in a **new preset version**, following the existing protocol
(`supersedes` block, rehashed integrity list, read-only after hashing):

| New evidence file | SHA-256 |
| --- | --- |
| `apps/photoshop-2019/window-states.json` | `C3882844AA3EC3841B40DBF284D19FBE6BB186613FB589064DFFDC40DB78E79E` |
| `apps/photoshop-2019/open-file-dialog.json` | `D085F2BBC3D206B6373CCAEBFF4E34FFBAAF5CF65607936909DC1853A788BBF6` |
| `apps/photoshop-2019/document-identity.json` | `E5F1D9E8BE5826F8D278854218886467C5267E664BDE9FC92C0489F9D317057B` |

**Preset `printflow-workstation-v1.9.0.json`** supersedes `1.8.0`
(`DE76464F…DB60E4`), manifest SHA-256
**`0DA89F8FE4067574FD7568F1FA8D1C0F2000469E3059FEE28BFDB0C867B5FB58`**. All **18 inherited**
integrity entries were rehashed and remained exact; 21 entries total, all verified. It adds
`photoshopContract.uiContract` and the three evidence files. It accepts **no** Action route, resize,
colour conversion, TIFF save, termination route or Revision. The Meitu contract and the Photoshop
action contract are inherited unchanged. `appsettings.json` points at it;
`Adapters.Mode` remains `Fake`.

The chain has one root of trust: `appsettings.json` names the manifest and its digest; everything
else is reached from inside the verified document. There is no control id, class name, dialog title
or window class hard-coded anywhere in the adapter — a preset that vouches for nothing produces an
adapter that does nothing, and that fail-closed behaviour is tested.

---

## 14. Remaining 11400-B scope

Not started, and structurally unreachable from this slice:

- W1 Action execution (`W1_0px` / `W1_1px` / `W1_2px` from the `PrintFlow DTF` set), including
  Actions-panel discovery, set/action integrity checking against the signed `.atn` digest, and
  positively observed completion;
- proportional sizing and 300 ppi conversion; canvas manipulation;
- CMYK conversion via `图像 > 模式 > CMYK 颜色` and the signed colour settings;
- TIFF Save As, collision naming, and output validation (spot channel, CMYK, dimensions);
- enabling `SessionService` `PhotoshopOutput` success and Revision creation;
- a busy/progress signature, which Part A had no operation long enough to observe;
- global `Adapters.Mode = Production` (Epic 11500 `EnvironmentGate` remains authoritative).

`IPhotoshopUiDriver` exposes exactly five operations and `IPhotoshopAutomationFoundation` exactly
three; architecture tests assert those lists exactly, so each item above must arrive as a reviewed
addition that breaks a test first.

---

## 15. Git state

Branch `master`, ahead of `origin/master`. No push, no amend, no history rewrite.

Committed: the source and test changes, `appsettings.json`.

**Not committed** (per §27): the synthetic images, the runtime workspace, screenshots, UI dumps,
the smoke transcript, and all external baseline evidence under `D:\PrintFlowStudio\Baseline` —
which is outside the repository and remains local, read-only and never uploaded.

Build: **0 warnings, 0 errors**. Dependency files were unchanged, so the vulnerability audit is
deferred to the Epic 11400 final gate (§24).

### Known housekeeping note

The smoke's temp workspace could not be deleted immediately after the run: Photoshop retains a
directory handle on the folder its Save As dialog last browsed. Only **empty directories** remain —
the synthetic image itself is gone, and no TIFF or output file exists anywhere. The smoke reports
this honestly as `RETAINED` rather than suppressing it.
