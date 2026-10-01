# SCRUM-11154 workstation observations

**Result: WORKSTATION_OBSERVATION_PARTIAL** (2026-09-30T21:32Z – 2026-10-01T00:46Z). Two visible synthetic sessions on the shared workstation, zh-CN only, with the owner present and performing every application input. The structured per-assertion record is [SCRUM-11154_WORKSTATION_OBSERVATION_LEDGER.json](SCRUM-11154_WORKSTATION_OBSERVATION_LEDGER.json). The [workstation checklist](SCRUM-11154_WORKSTATION_CHECKLIST.md) is unchanged; the run sheet below maps its W-IDs to this run.

## What ran

| | Session 1 | Session 2 |
|---|---|---|
| Run root | `runs/v-20260930T213235Z` | `runs/v-20260930T225032Z` |
| Source | `596bc05` (reviewed entry code `a39b3bd`, docs-only since) | `596bc05` + test-entry fix, committed unchanged as `20747aa` |
| Candidate SHA-256 | `42ABEFF2…A3ED` | `E46AA520…8AAE` |
| Host assembly SHA-256 | `78030B20…BA12` | `185F502C…A2F4` |
| Preparation | build 0/0, static 39/39, prerequisite 7/7, Validate 0, PrepareAndSmoke 0, all bindings match | fresh build 0/0, static 47/47, prerequisite 7/7, architecture 474/474, Validate 0, PrepareAndSmoke 0, all bindings match |
| Owner acknowledgment | 21:39:01Z, bound to this run | 23:00:45Z, bound to this run; after a ~68 min pause one Approve (00:36:02–08Z) preceded the reconfirmation recorded at 00:37:08Z |
| Window | 1000×700, owner resized to 1066×1047 | 1000×700, owner resized to 1000×989 |
| End | **exit 2**, harness refusal escaped (F-V1); no settled shutdown | owner closed window; **exit 0**; quiescence settled; 12 fixtures admitted, 3 native dispatches, 0 refusals |

Workstation, measured read-only: one 1920×1080 screen, 1920×1040 work area, 96 DPI system and window. No display setting changed. Scroll offsets were not recorded. Runtime7, owned restart, standalone Resume and negative fixtures were not run on either visible root; no marker was rewritten. A third root (`v-20260930T224610Z`) was prepared from a pre-review version of the fix and never launched. All roots are preserved.

### The session 1 stop and the fix

Importing a prepared return fixture through the native dialog terminated the host: fixture admission existed only in the PrepareAndSmoke process, and the containment `IOException` escaped the product command before the settled shutdown path. The owner directed a fix and a continuation. The change touches only test code: `tests/PrintFlow.WorkstationEntry` plus a new test file in `tests/PrintFlow.Tests`. Interactive admits exactly the fixtures its own preparation recorded in the scenario ledger (path and SHA-256, inputs/returns only, held before hashing), and a native selection outside its role or not admitted becomes "no selection" with a visible SYNTHETIC notice, while identity, alias and directory-protection faults still refuse the run. Eight new tests. One independent code reviewer, two rounds, PASS WITH FINDINGS each; the P2 was corrected and rechecked, and the remaining P3s are listed under F-V7. Session 2 repeated Ask-a-colleague and Prepare only as setup, then continued from the interrupted import.

## Evidence categories

1. **Previously supplied tests** — unchanged; 474 architecture checks rerun on the fix.
2. **New noninteractive preparation** — as tabulated above, plus 8 new picker-containment tests.
3. **Native automation** — read-only UIA tree/focus observation bound to the exact PID, and of the owned `delivery` Explorer window. One agent UIA Invoke was attempted and denied by the executor's permission classifier; no agent input reached the application.
4. **Human physical input and workstation visuals** — every keystroke and click by the owner, reported per round and corroborated by the focus trace where focus moved. 22 owner captures: 20 window-only captures for the packet, saved unmodified with SHA-256; 2 private-only (one full-screen, one Explorer with personal Quick Access names), excluded.
5. **Owner bilingual copy acceptance** — NOT RUN.
6. **Novice validation** — NOT RUN.

## Run sheet: W01–W10 for this run

| W-ID | Observed (zh-CN) | Outcome |
|---|---|---|
| W01 | Home with "not checked" readiness and 12 Recent rows; keyboard traversal with owner-reported visible focus; simulated readiness refusal screen; keyboard back to Home | Reachability PASS; F-V2 FAIL; "checking", "last-checked pass", "unconfirmed" states, Recovery list and scroll-end captures NOT RUN; en NOT RUN |
| W02 | — | NOT RUN |
| W03 | New TIFF review, background review and crop editor: focus lands on the non-activating status panel / editor heading; first Tab goes to Zoom out, not Approve | Landing PASS; F-V3 slider orientation FAIL; F-V5 clipping FAIL; refresh/language-switch focus and TIFF Reject→recycle NOT RUN |
| W04 | Positive control (Tab + one Enter); held Enter into a new review; held Enter on Approve with the successor Run step at the same position; mouse double-click remainder; picker Cancel | Those subchecks PASS (physical keyboard/mouse). Held Space, stale release and a keyboard-only whole journey NOT RUN |
| W05 | Editor over the pre-trim image; arrow and Shift+arrow nudges (+1/+10); Compare; Restore; Cancel retains R1 `8081b587` (the first reported Cancel did not take effect; a second did); Use creates R2 `96c5e2d8` for its own review | Those subchecks PASS; Ctrl nudge and earlier-delivery attachment NOT RUN |
| W06 | Ask-a-colleague note and package; owned Correction folder selected in Explorer (owner report + private capture); wrong-size return refused with the legal path kept; SIMULATED_COLLEAGUE_RETURN creates review `981165b7` bound to `be198077` without rerunning background removal | PASS after fix; session 1 import BLOCKED (F-V1); note-draft-after-refresh and interrupted bound import NOT RUN |
| W07 | Output row 200×150 mm versus 6×5 px TIFF (~0.5×0.4 mm) | **KNOWN NEGATIVE F6**; size modes, hints and enlargement acceptance NOT RUN |
| W08 | Approve records review only; Run step, then **"保存已审核结果" (Save approved result)** wrote the exact R2 bytes (full SHA-256 `871D721F…BEE32`, equal to the workspace revision) to the owned delivery folder; Complete stays separate; Explorer selects the saved file (UIA); Home later shows "saved previously" | Those subchecks PASS; the "Confirm result and save" name/folder path, Complete/Completed state, previous-size selection, failed/uncertain retry and a fresh recheck NOT RUN; F-V8 FAIL |
| W09 | Keyboard route Session → Error Details for simulated `MeituLaunchFailed` with attempt data; after Back, focus landed on the window root, not on the Details control | Reachable part PASS; F-V9 wording; combined notices NOT RUN |
| W10 | Remove from list removes rows; 12 session folders keep identical file counts; distinct same-name jobs stay separate; **Abandon acts on a single click with no dialog** | Remove PASS (file-count basis); F-V4 FAIL; running-attempt row and confirmation dialogs NOT RUN |

## Findings

- **F-V1** test-entry defect, fixed in `20747aa` (see above).
- **F-V2** readiness screen: raw key `EnvironmentCheckName_SyntheticEntry` as the row title; headline "did not pass" versus row "not run"; row accessible name is the view-model type name; two unnamed lists are Tab stops.
- **F-V3** slider comparison shows After left / Before right, contradicting its "Before / After" label and the side-by-side layout (owner-reported, capture-confirmed).
- **F-V4** Recent "Abandon" acts on a single click with no confirmation.
- **F-V5** output-row status text clipped at the launched 1000×700 window ("待审核（Wo").
- **F-V6** empty accessible names on the reject-reason ComboBox, reject note and "return to" step ComboBox; TIFF metadata rows expose record `ToString()` text as names.
- **F-V7** remaining entry limits: choosing a newly created folder in a folder/save dialog would still terminate the Interactive host (operator avoided it); the ledger hash is not bound in `PreparedRun`; other unhandled command exceptions still bypass the settled shutdown path.
- **F-V8** after saving, Session shows every step passed with Complete next, while the Recent row for the same job says "needs your action · export reviewed PNG".
- **F-V9** Error Details sentence "Meitu started but was not ready in time" versus code `MeituLaunchFailed` (simulated failure).
- **F6** known negative size display, unchanged.

No product code was changed. Findings F-V2 to F-V6, F-V8 and F-V9 await owner disposition.

## Acceptance criteria

- **AC1** (same wording in zh-CN and en across the journey): **OPEN.** Observed zh-CN screens contain wording/presentation inconsistencies (F-V2, F-V3, F-V8, F-V9); F6 has no accepted disposition; en was not run.
- **AC2** (keyboard-only primary actions, safe review focus, no stale or repeated approval): **PARTIAL.** Physical keyboard evidence passes for the positive control, neutral review landing, held Enter into a new review and on Approve with successor protection, and crop nudges. A mouse double-click remainder and picker dismissal also passed. Held Space, stale release and a keyboard-only whole journey were not shown; en not run.
- **AC3** (workstation screenshot set without clipping): **OPEN.** Real zh-CN workstation captures exist at 96 DPI; F-V5 is an unfixed clipping finding; scroll-end captures and en were not taken.
- **AC4** (findings listed): recorded here and in the ledger.

SCRUM-11154 is not Done. No production data, installed application, real processor, display change, forced termination or SCRUM-11155 work occurred.
