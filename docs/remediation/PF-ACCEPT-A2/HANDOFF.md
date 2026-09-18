# PF-ACCEPT-A2 — Handoff

## Publication and normal gate closed — 18 September, 11:53 local (Claude): A2 admission OPEN

**Current mainline state.** Sections below are historical. Full evidence in [RECOVERY.md](RECOVERY.md).
One exclusive-window confirmation was obtained for this execution and covered the replacement and
every ordinary live stage. HEAD was verified as `fcaa324` (documentation-only) before starting; no
review was re-recorded, no result re-aggregated, no content comparison redone, no standard set
rerun, no build and no new pair.

**Pre-publication verification — PASSED.** Result
`D:\PrintFlowStudio\TestData\v3\runs\a2-v3-20260917-154736-d915b1a6\result.json` rehashed to
`4083F1FB…DA1A` (28,592 bytes): Status Passed, 7/7 categories, 59/59 assertions held, 0 Pending,
CandidateProblems empty, one non-synthetic review `3685ceef` by `DESKTOP-0BG8884\admin` at
`2026-09-17T17:14:11+12:00` with all three manual decisions Passed. The candidate was resolved
through the run's own `Binding.BuildOrigin` — receipt
`…\build-pairs\d915b1a6-4c06-4b16-9a20-5e1341d1ae9c\build-pair.json`, rehashed `52E6DBC5…8FBC`,
PairId `d915b1a6-4c06-4b16-9a20-5e1341d1ae9c` — not from the old A1 candidate, a conventional
`bin/Release` or a newly built folder. All four candidate assemblies match the binding
byte-for-byte on disk, all seven v3 manifests match `Binding.SetManifests`, and preset 1.18.0
(`…\Baseline\workstation-v1\preset\printflow-workstation-v1.18.0.json`) rehashed
`8484F0AA…C8E0F`. Zero mismatches across 30 comparisons.

**Publication — PASSED.** The old active record `78B0464C…B0FF` (A1 candidate `a39846d8…`,
attested `2026-09-16T15:08:54`) was preserved first, byte-identical, to
`artifacts/pf-accept-a2/publish-prior-active-record.json`. The existing unmodified writer then ran
under the already-supported PowerShell 7 runtime with the explicit v3 manifests folder, the
explicit v3 result and the exact candidate folder. **Writer exit 0**, "This installation is
revalidated. Production may resume." The result was not omitted, revocation was not tested on real
state and no record was hand-edited.

```powershell
pwsh -NoProfile -File tools\installer\Set-PrintFlowProductionRevalidation.ps1 `
  -InstallFolder 'D:\Repositories\printflow-Studio\artifacts\pf-accept-a2\build-pairs\d915b1a6-4c06-4b16-9a20-5e1341d1ae9c\candidate' `
  -WorkspaceRoot 'D:\PrintFlowStudio' -EnvironmentReadinessPassed `
  -StandardRegressionSetPath 'D:\PrintFlowStudio\TestData\v3\manifests' `
  -StandardRegressionSetResult 'D:\PrintFlowStudio\TestData\v3\runs\a2-v3-20260917-154736-d915b1a6\result.json'
```

`pwsh` on this workstation is PowerShell 7.6.5 Core at
`C:\Users\admin\.cache\codex-runtimes\codex-primary-runtime\dependencies\native\powershell\pwsh.exe`
(SHA-256 `362A356C…D139`), the only PowerShell 7 present and the same runtime A1 evidence records
as bare `pwsh`. It is a shell binary, not an executor: no executor was switched and no global
setting was changed.

**Record binding — PASSED.** Persisted record
`D:\PrintFlowStudio\Revalidation\production-revalidation.json`, SHA-256
`E6A7D7EAA9C370AFACF0769B927B4D5AD735ADAC8203B614F3EE9320D975F3B9`, 1,952 bytes, attested by
`DESKTOP-0BG8884\admin` at `2026-09-18T11:26:13.3632212+12:00`. An independent readback of the
bytes on disk compared 30 fields: schema 2, product version, all four assembly hashes and build
identities (`0.1.0+6818757b…`) against both the run binding **and** the actual candidate bytes,
preset id/version/hash against the preset file itself, Windows build, Meitu and Photoshop hashes,
the readiness flag, and set id/status/run/invocation/binding version/content digest/evidence
path/completion. All matched. The one non-equal row was the comparison's own shape: `attestedBy`
is `machine\user`, as in the prior record and the review's `DecidedBy`, not the bare workstation
name. The readiness basis is this run's `readiness.json` (`32CDA8EC…7BB6`, Verified true, 17
Passed, 2 Advisory, 0 blocking failures), accepted by the user as the basis of the existing
operator EnvironmentReadiness attestation — historical evidence, **not** the fresh live checks
below.

**ProductionRevalidation — PASSED.** The running candidate's own report states: "PrintFlow Studio
0.1.0 (candidate `13A73BEFAE17…`) on this preset and Windows build was revalidated by
`DESKTOP-0BG8884\admin` at `2026-09-18T11:26:13.3632212+12:00`", with expected and current both
"PrintFlow 0.1.0, preset printflow-workstation-v1 1.18.0, Windows build 19045". That composite was
recomputed independently — SHA-256 over the canonical `name:sha256` list — from the actual
candidate bytes on disk and again from the published record: both
`13A73BEFAE17B18E61E8B975CF030E7156315E9AEBDE661E391550C52BEEDD5F`. The record therefore binds to
the exact qualified candidate.

**Fresh normal live checks — PASSED, after one explained recovery.** The exact
`PrintFlow.App.exe` from the candidate folder was launched normally with no arguments, PID 4864;
its executable path was read back independently from `Win32_Process`. No testhost identity,
bootstrap, diagnostic omission or fabricated result was used at any point; every reading below is a
native accessibility read of the production `EnvironmentReadinessViewModel` report.

The first run of the ordinary action **安全恢复并重新检查** at `11:33:24` returned four live checks
Passed and **PhotoshopSafeStartingState Failed** — "Photoshop live settings could not be read:
操作无法使用 (0x800401E3 (MK_E_UNAVAILABLE))" — leaving colour settings and the round trip NotRun.
The displayed generic wording mentions unsaved work or a dialog; that is not what happened. The
state could not be read: Photoshop PID 16148 was the exact accepted binary launched with no
arguments, fully started and idle, with the "Modal Layer" window not visible, `IsModal` false, the
"Document Layer" not visible and the title exactly `Adobe Photoshop CC 2019`. A typed enumeration
of the COM Running Object Table found 13 entries and **zero** Photoshop entries, so the Product's
read-only attach could not succeed. Elevation mismatch was ruled out (all processes Medium
integrity, same user and desktop); foreground activation and dismissing the CEF Home Screen with a
normal Esc each changed nothing across roughly 12 minutes of idle uptime. Retrying unchanged was
therefore not done. Closing and restarting Photoshop directly was refused by the environment's tool
restriction and was **not** worked around; the Operator was asked for the plain normal action
instead and reopened Photoshop themselves (PID 21768, same accepted binary, not a child of
PrintFlow). That instance also never registered, so the child-process launch was not the cause; the
condition is machine-wide and remains unexplained at the Photoshop level.

With that materially changed precondition — a settled, operator-launched Photoshop present before
the check rather than launched by it — the ordinary action was run **once** more at `11:53:06`.
Overall status: **本工作站已通过生产环境校验。** All seven live checks Passed, all eleven blocking
automatic checks Passed, zero Failed, zero NotRun. PhotoshopSafeStartingState now reads
"KnownStartScreen; No document is open."; colour settings match the preset on all four working
spaces (sRGB IEC61966-2.1; Coated FOGRA39 (ISO 12647-2:2004); Gray and Spot Dot Gain 15%); the
canonical automation lock was acquired for the bounded verification.

| Item | Actual result |
|---|---|
| ProductionRevalidation | **Passed**, candidate `13A73BEFAE17…` recognized against the published record |
| ExternalApplicationAutomationLock | **Passed**, canonical lease acquired for the bounded check |
| Meitu launchability / safe starting state | **Passed**, PID 3648, welcome state |
| Photoshop launchability | **Passed**, PID 21768, exact accepted binary |
| Photoshop safe starting state | **Passed**, KnownStartScreen, no document open |
| Photoshop colour settings | **Passed**, all four working spaces match the preset |
| PhotoshopTestImageRoundTrip | **Passed**, opened, identified, closed and cleaned |
| Final ordinary readiness/admission | **Verified / OPEN** for this observation, 7/7 live checks Passed |
| Probe cleanup | **Completed**, CleanupCompleted confirmed, outcome Succeeded, nothing retained |
| Lease after completion | **Free**, all owner fields null |

**Final admission — OPEN for this observation.** The gate is the production
`VerifiedEnvironmentGate` report of the exact published candidate, not a harness identity check.
Two nonblocking advisories remain: some accepted evidence files lack read-only attributes while all
signatures match, and external-application UI-language information. Each later production request
still re-evaluates current conditions, so this publication is not perpetual admission, and the
Photoshop registration fault above occurred twice today and could recur.

**Probe cleanup — COMPLETED.** Probe `435e5d5f6c3f4e14b8a7186ff7b2916a`, diagnostic attempt
`2026-09-17T23:53:06Z`, last complete live verification `2026-09-17T23:53:12Z`. Recorded stages:
ProbeCreation, ProbeCreated, OpenGuard, OpenRequested, OpenConfirmed, IdentityCheck,
IdentityConfirmed, CloseGuard, CloseRequested, CloseConfirmed, PriorStateCheck, PriorStateRestored,
CleanupAttempted, CleanupCompleted. Last attempted CleanupAttempted, last confirmed
CleanupCompleted, cleanup outcome **Succeeded**, no primary failure recorded. This is the first time
in A2 that the round trip reached CloseConfirmed and cleanup. `EnvironmentVerification` holds the
same nine historical probe directories as before this run: no new directory was retained, and none
of the historical retained probe files was deleted or altered. The earlier `6eff4159…` and
`310afeea…` directories remain exactly as they were.

**Lease outcome — FREE.** A read-only query of the canonical store
`…\PrintFlow Studio\workstation-automation-v1.db`, resource
`printflow-studio.external-automation.v1`, read all owner fields null before publication, after the
first live run, and finally at `2026-09-17T23:54:55.541Z`. No synthetic lease manager or outer
lease was used.

**Preservation.** After all live operations the active record still hashes `E6A7D7EA…F3B9`, and the
run result, run readiness, receipt, preset 1.18.0, the preserved prior record, the A1 result
`97E422FA…600A` and the visual-decisions input are all unchanged. Both pair inventories are intact
(d915b1a6 1,273 files; f0ac92e2 1,275 files) and the four candidate assemblies still hash as
published. Original evidence, approvals and the operator prompt bundle are untouched. New local
evidence, ignored by Git, is under `artifacts/pf-accept-a2/publish-*`.

**Current desktop state.** PrintFlow PID 4864 rests on the passing Production readiness report.
Photoshop PID 21768 is open at `Adobe Photoshop CC 2019` with no document. Meitu PID 3648 is open
at `美图秀秀`. Nothing was processed, resumed or abandoned; the interrupted Home items were not
touched.

**Boundary.** A2's procedure is complete and its actual outcome is admission OPEN for this
observation. No Product or preset change, new qualification cycle, A3, customer processing, MSI
installation, deployment, push, signing or online Jira change was performed. A3 remains
unauthorized and must not be started from this handoff. The open follow-up worth scoping separately
is the Photoshop automation-registration fault: it is an environment condition, not a PrintFlow
defect, and the Product correctly refused rather than inventing a result.

## Qualification review closed — 17 September, 17:14 local (Claude): v3 run PASSED 7/7

**Current mainline state.** The Pending section below is this run's historical pre-review state.
Full evidence is in [RECOVERY.md](RECOVERY.md).

**Operator's scope clarification (verbatim):** "Meitu产生的结果我们无法做修改，单纯接受即可，只要确保确实产生了改动；"
PrintFlow must invoke the intended Meitu operation, get back that operation's actual result
and validate its technical integrity. Improving Meitu's AI output, or scoring hair/skin/edge
quality, is not a PrintFlow repair task, and these artifacts are not retouched. This is not an
agent claim that every pixel is correct. It does not waive size, transparency, TIFF or W1
requirements or Photoshop's contract, and it creates no decisions for future artifacts.

**Operator decisions:** `三项判断全部通过` (verbatim) → PORTRAIT-VISUAL-001, FINE-HAIR-VISUAL-001
(pre-Trim cutout `F690AF0D…04C1`), CUSTOMER-DESIGN-VISUAL-001 all **Passed**. Recorded as one
non-synthetic review `3685ceef-98ee-4bb1-9811-26bda34ebad9`, `DESKTOP-0BG8884\admin`,
`2026-09-17T17:14:11+12:00`. Route: `-SetRoot 'D:\PrintFlowStudio\TestData\v3' -RunId
'a2-v3-20260917-154736-d915b1a6' -RecordVisualReview <artifacts\pf-accept-a2\a2-v3-154736-visual-decisions.json>`.
Original pair `d915b1a6` reviewer, no receipt, filter or override. **Wrapper exit 0.** The agent
did not view the images.

**Readback:** result SHA256 `4083F1FB7F1BDA4D9B4F6A0EE7C9F95B8A4CF891087956F845D0B2CDDC5FDA1A`,
**Status Passed, 7/7, 0 Pending, 59/59 assertions held**. Unchanged: CandidateProblems (empty),
invocation, binding, timestamps and case records. Only result.json changed (18/19 preservation).
Active revalidation record `78B0464C…` is unchanged.

**Meitu actual change (post-run observation, kept outside result.json):** inputs taken from DB
lineage. Background Removal: opaque input → 379,845 alpha-0 + 431,254 partial-alpha pixels, and
the opaque subject is retained. **Verified.** Enhancement: decoded RGB differs in 1,291,315 of
1,920,000 pixels (mean 0.996, max 22), with a local same-direction shift in 600/1,900 blocks.
**Verified changed, modest.** Meitu's own JPEG decode cannot be separated offline (the GDI+
control is not independent), so attribution rests on the `meitu:enhance` trace. No contradiction.

**Next:** A2 has **not** passed and production admission is **not** open. Still to do, each
needing its own authorization: publish the replacement revalidation from this result (the
wrapper's printed command was not run), read it back, then verify the ordinary Product gate
normally on candidate `d915b1a6`. No A3.

## Qualification outcome — 17 September (Claude): PENDING Operator visual review, no failures

After the stop above, the operator gave a fresh exclusive-use confirmation. All with pair
`d915b1a6-4c06-4b16-9a20-5e1341d1ae9c` (source `6818757`, receipt verified again before the run).

**Focused real proof** `a2-focus-finehair-20260917-154626-d915b1a6`, fine-hair category only
(partial by design; can never report Passed; not qualification): readiness allowed; Background
Removal authorised → Meitu → ReviewRequired; PNG with real alpha; trim matches alpha bounds;
source unchanged; automation lock free; Meitu returned to the empty editor. Cutout 1200x1600.
The synthetic 4x refusal did not recur on the real fixture.

**Complete v3 run** `a2-v3-20260917-154736-d915b1a6`, invocation
`eb7c35c7-78fa-4881-8b4e-d43c6ab98021`, explicit `D:\PrintFlowStudio\TestData\v3`, all seven
categories, no filter, executable override, unbound mode or in-run build. Recorded
CandidateProblems empty (unedited). Result
`D:\PrintFlowStudio\TestData\v3\runs\a2-v3-20260917-154736-d915b1a6\result.json`, SHA256
`A6060D503A7AE0C79FE59E52821B995C9168E7D29E698FB10C56A0F48027B702`.
**Status Pending: 4 Passed, 3 Pending, 0 Failed.** Lock free after every case.

- Passed: TRANSPARENT_PNG, PSD_WITH_COMPOSITE_PREVIEW, SINGLE_PAGE_PDF, REFERENCE_PRODUCTION_TIFF.
- `PORTRAIT-VISUAL-001` — `FIX-PORTRAIT-001-enhanced.png` (1200x1600), SHA256
  `F580164E013AF13216DBFA8A5FD57A8E409BE3191930A18C867DA375AE7C5BB6`. Does the enhanced export
  still look like a correctly enhanced portrait — subject sharp, skin tone unshifted, no visible
  artefact introduced along the hair or shoulder edges?
- `FINE-HAIR-VISUAL-001` — `FIX-FINE-HAIR-001-cutout.png`, SHA256
  `F690AF0D9861773B64E0E63FD5F95104F692222B2B75501C004DF9ED33CD04C1` (trimmed
  `83534D2F9A960E9F12FE1CD5A91C0E4FE13971B0441F3F58D99335D330D2B8B1`). Are individual hair
  strands still retained at the boundary, without a hard halo, and is the foliage background
  fully removed rather than partly retained as coloured fringing?
- `CUSTOMER-DESIGN-VISUAL-001` — `D:\PrintFlowStudio\Sessions\S_20260917T034836Z_b210ba6d\Working\01a0ad7b-0b40-7f36-9ce7-2179c95eb481\FIX-CUSTOMER-DESIGN-001_51mm_CMYK_W.tif`,
  SHA256 `FFCFABF0194CA00F92AC2C61BDC34FA353AFD74A95990946C9CB10725869D902`. Does the produced
  TIFF show the complete design at the requested size, with the W1 channel covering the
  intended ink region?

Decisions must be the Operator's own for these exact bytes, recorded with
`-RunId a2-v3-20260917-154736-d915b1a6 -RecordVisualReview <decisions.json>`. No old approval
(including the 131609 portrait question) is transferred. Nothing is published; this is not a PASS.

Final observations: Meitu PID 5980 on the empty editor; Photoshop now PID 37916 titled
`Adobe Photoshop CC 2019` (differs from the 24172 seen at 14:00; restart cause not claimed).
Preservation 34/34 (`claude-preservation-final.json`). No A3, preset/set change, install,
deploy, push, signing or Jira change.

## Claude takeover (see RECOVERY.md for detail) — 17 September: marker-read correction, stopped at changed availability

Executor: Claude Code (claude-opus-5, high effort as supplied by the host; no global settings
changed, no Codex routing emulated). Started clean on master at actual HEAD
`d2851415bfc892e41b71c788939ec6c9b377333d`; no reset, no startup suite rerun. **Complete v3 qualification followed; see the outcome section above. Ordinary A2 admission remains CLOSED.**

### Diagnosis of `a2-v3-20260917-131609-d2a0a55e` fine-hair failure

- Producer: `UiaElementProvider.ReadMatchingTextSnapshot`, catching `ElementNotAvailableException`
  (empty Message) from `AutomationElement.FindAll(TreeScope.Descendants, OrCondition(Name==...))`
  over the signed Background Removal Busy + Completion markers.
- Caller/phase: `GuardedMeituUiDriver.ReadBackgroundRemovalPhaseSnapshot` inside
  `AwaitBackgroundRemovalPhaseAsync`, after the single action invoke; `RefreshOwnedWindow` had
  just succeeded. Phases are not persisted, so Busy-wait versus Complete-wait is not proven;
  the capture shows the completion page.
- Target: editor `0x1D0376`, Meitu PID 5980 (start 2026-09-16T03:38:11.2951111Z).
- The old catch labelled any element vanishing mid-walk as "window disappeared". Discriminating
  evidence that the root survived: two seconds later the next case's readiness freshly read the
  same handle as an unrecognised live screen (`20260917T011711Z_unknown-state_1D0376.png`), and
  at 02:00:46Z the same HWND/PID still existed (then on the empty editor). Signed Busy markers
  are short-lived overlays Meitu removes when presenting the result.
- Existing successor-editor rebinding (post-open window replacement) and markerless-repaint wait
  (identity probe) do not apply: same handle, and this loop returned any read failure at once.
- Historical cutout: not present in Meitu at 02:00Z and never written to disk; dismissal cause
  unknown (not claimed). Nothing recoverable remained.

### Product correction `a5cc906`

`UiReadInterruption` preserves exception type, HResult and message (`(empty)`), re-reads the root
from the same handle, and tags only a readable-root descendant change (code stays
`MeituTargetLost`). The Background Removal wait discards such a read whole and takes a complete
fresh observation on the next poll within the unchanged deadline. Unreadable root, window gone,
process exit, handle reuse, modal and unknown screens still stop at once; a stop during an
interrupted pass returns Cancelled with no input and never invokes Meitu's cancel; a timeout
reports `lastReadInterruption`. No budget, sleep, union, stale evidence or extra action/return.
Save-dialog exact-foreground protections untouched. Red→green: 4 red before the change; later
review-driven regression test verified red without its guard. One scoped independent read-only
reviewer, three passes: two medium and one low finding plus one introduced false-stop regression
were fixed; final pass nothing blocking. Evidence `claude-marker-read-red-green.txt`,
`claude-marker-read-review.txt`. Focused slice 1122/1122.

### Pairs, suites and live proof (all preserved)

| Pair | Source | Full suite | Use |
|---|---|---|---|
| `08f4d554-bb76-4193-a489-3f536a9086a1` | `a5cc906` | 11964/11964 | focused seam proof |
| `393c45f8-0a1a-483f-9586-be843c681627` | `b2cb93b` (+recovery smoke) | 11965/11965 | recovery stopped on smoke logging bug before input |
| `d915b1a6-4c06-4b16-9a20-5e1341d1ae9c` | `6818757` | 11965/11965 | current; receipt SHA256 `52E6DBC5E894EA95CB859CF9CF0BB0D6D42533B17AE26D90DAC0EB41E8DE8FBC` |

The operator gave one exclusive-use confirmation for this Claude execution. Focused Background
Removal production-seam smoke (pair 08f4, 14:20 local, synthetic 480x360) passed identity,
action, Busy, completion, return and identity, and exported, but Product output validation
**refused**: cutout canvas 1920x1440 (4x). This is not a proof pass. The same smoke preserved
480x360 historically; the real v3 fine-hair and portrait fixtures are 1200x1600 and exported 1:1
in A1 and at 13:16 today; Meitu settings files were last written 12:01. Cause of the synthetic 4x
is unproven; the validation guard was not weakened. Transcript
`a2-meitu-cutout-focused-20260917-08f4d554-transcript.md` (SHA256 `031A4C75...35CE3`).

Adapter correctly left the synthetic document for the operator. Opt-in bound recovery smoke
(`A2MeituCutoutValidationRecoverySmoke`) attempt 1 (pair 393c) failed on its own log line before
input; attempt 2 (pair d915, after its full suite; log `a2-meitu-cutout-recovery-20260917-d915b1a6.log`) found no save-result surface and stopped before identity or
close input. At 14:39 local, read-only observation: Meitu minimized on the **empty editor** (the
synthetic document had been closed by someone else) and **CorelDRAW in the foreground with an
open work file**. Exclusive availability therefore no longer holds; no further Meitu/Photoshop
input or qualification was started. Synthetic workspace retained under
`%TEMP%\PrintFlowMeituCutoutSeam\8511371b71894eb0a26239d39f2dc69f`.

Preservation: 29 original hashes plus three prior failed results, the pending portrait artifact
and active revalidation record match (34/34) at start and stop (`claude-preservation-*.json`).
Lease was acquired only by Product/smoke scopes and released with them; no separate lease read.
No publication, preset/set change, approval transfer, A3, install, deploy, push or Jira change.

**Next boundary:** a new current exclusive-use confirmation. Then, with pair d915: focused real
Background Removal proof on the fine-hair product path, then the complete explicit v3 run with a
fresh RunId, no filter/override/unbound mode. If the synthetic 4x recurs on the real fixture, it
is a new blocker to diagnose, not a guard to relax.

## Final outcome — 17 September: qualification FAILED

Current Product/test source `95bfa1bc817db7717d870fbaade2df4301b4616c`; pair
`d2a0a55e-6f02-43f9-bd89-8e0ee3138dde`. Targeted190/190, independent safety re-review,
incident-bound recovery, fresh focused live proof, and full suite11953/11953 passed.
Complete v3 run `a2-v3-20260917-131609-d2a0a55e` FAILED:2 Passed,1 Pending,4 Failed.
Portrait completed the repaired Save path and awaits its own visual review. Fine-hair failed
in BackgroundRemoval signed-marker reading; remaining external categories were blocked.
The retained screenshot shows a visible cutout editor, so actual process/handle destruction
is not proven. No unchanged retry or post-outcome input. Stop at the requested new outcome.

See RECOVERY.md's final section for exact result/artifact paths, hashes, pending review question,
and unresolved failure evidence. A portrait approval cannot turn this failed run into a pass.
Original A1 PASS/pair, both prior failed results, accepted1.18.0 and active record are preserved;
all29 preservation hashes match and shared lease is released. No replacement publication or A3.
Historical pending statements below describe earlier phases, not current status.
## Current state — final transition correction under qualification

Source `95bfa1bc817db7717d870fbaade2df4301b4616c`, immutable pair
`d2a0a55e-6f02-43f9-bd89-8e0ee3138dde`. Pair680dde3e's complete run failed at a proven
second-read discovery race and is preserved. The correction accepts only the exact Save
foreground transition after re-verification. Targeted190/190 and independent safety re-review
passed. Pinned recovery cancelled the retained fixture probe, then used fresh ordinary identity
before close; a new focused enhancement/export/close passed. The full paired suite and complete
v3 outcome remain pending. See RECOVERY.md for precise evidence. No publication or A3.
## Latest continuation — 17 September: correction and focused proof complete

The operator continued after the one availability request. Native evidence proved a standalone
owned Save dialog; the Product correction retains exact foreground and adds exact owner checking
plus bounded activation/reacquisition. Source98534a4, immutable pair680dde3e, targeted188/188,
independent safety re-review without remaining blocking findings. Incident-bound cancellation,
fresh ordinary identity probe and empty-editor close passed; a fresh full Meitu focused
open/enhance/export/close passed. Full paired suite and v3 outcome are pending; see RECOVERY.md.
No generic editable-Save-field recovery was accepted, no admission opened and no publication.

### Earlier startup observation, preserved for chronology

New user authorization supersedes all historical statements below that prompt19's numerical
one-run limit prevents another qualification. Proceed after demonstrated relevant correction or
positively verified recovery and focused proof; preserve prior results and use fresh RunIds and
matched immutable pairs. A3 remains separately authorized later Claude Code work.

Started at actual HEAD `905263abdf00bbcc2cbe069b3d1633abb4d34e37`, clean master, no reset.
Read retained records/evidence and current source. All29 preservation hashes and primary failed
result hash match. The current source has no Product/test changes relative to pair0298's source.
Evidence: `artifacts/pf-accept-a2/meitu-save-20260917-preservation.json`.

Caller identified: Save document-identity probe; both filename read and cancellation reject the
same foreground mismatch. Not an export or format-popup call. Nested UIA SaveMaskWidget is known,
but native embedded-vs-owned-dialog relationship is not yet proven. `FindOwnedDialogs` only
checks a nonzero owner on same-process top-level windows, not exact editor ownership; do not use
that as authorization to weaken the foreground requirement. Full detail is in RECOVERY.md.

Current permitted Computer Use inventory returned a different, minimized Meitu window; capture
refused until activation. No activation or input sent. Photoshop has restarted since the prior
confirmation. One current exclusive-window/saved-customer-work confirmation was requested and
is pending. Resume from that answer with fresh observations; do not ask again once confirmed.
No new code/test changes, pair, live recovery, tests or complete run yet. No publication.
Actual model/effort UNVERIFIED; no parent model switch claimed. See PLAN.md for route targets.

**Latest continuation: Photoshop recovery and complete fresh round trip are verified after
Product repair. Replacement qualification FAILED at a separate Meitu Save-surface foreground
guard (2/7 categories passed). Ordinary A2 admission is CLOSED. A1's original PASS and
published record remain unchanged. See the final evidence in [RECOVERY.md](RECOVERY.md).**

The older sections below are historical observations and authorization, not a statement that
their named probes are still open. Prompt 19 and the current user request authorize scoped
repair/recovery and corrected-candidate qualification; no A3 is authorized.

## Executor and authority

Codex executed A2 only on `DESKTOP-0BG8884`. Started clean on `master` at
`c48387c657ff1439ece6f47428e110b05ffb5df6`, matching the supplied A1 closure. No reset.
Installed routing policy v2.3, explicit route_offset 0, actual model/effort UNVERIFIED;
MODEL_SWITCH_UNAVAILABLE for the active context. See PLAN.md for targets versus actual execution.
The operator explicitly confirmed the current exclusive A2 checkout/desktop window before the
write/live check. Claude Code was not used as an executor. No A3 was started or delegated.

## Exact retained identity

- SetRoot: `D:\PrintFlowStudio\TestData\v3`.
- RunId: `a1-v3-20260916-121334-903986e7`.
- Result: `D:\PrintFlowStudio\TestData\v3\runs\a1-v3-20260916-121334-903986e7\result.json`.
- Result SHA-256: `97E422FA53002725F8222A4C38075A8E6B43C7A169442DF3F1CBBC501DB9600A`.
- Original pair: `f0ac92e2-853e-4d2f-a6e4-c0af1a24e0e8`, source `a39846d8e0398e061e445fc79fe219b9bb195401`.
- Receipt: `D:\Repositories\printflow-Studio\artifacts\pf-accept-a1\build-pairs\f0ac92e2-853e-4d2f-a6e4-c0af1a24e0e8\build-pair.json`.
- Receipt SHA-256: `BEBEDFFC235F40AE66554F13D0BD0354C20DE75F41FA654341621DA836BFBDC4`.
- **Exact candidate folder:** `D:\Repositories\printflow-Studio\artifacts\pf-accept-a1\build-pairs\f0ac92e2-853e-4d2f-a6e4-c0af1a24e0e8\candidate`.
- Exact executable: that folder's `PrintFlow.App.exe`; sibling `harness` is retained but was not
  used for normal-App verification.
- Preset: `printflow-workstation-v1` 1.18.0; manifest SHA-256
  `8484F0AA18728FAC58B58ACD8E811C7058743B61271506647179CA0D0A6C8E0F`.
- Effective candidate configuration: workspace `D:\PrintFlowStudio`, Production adapters,
  `Baseline\workstation-v1\preset\printflow-workstation-v1.18.0.json`, expected hash above;
  no `appsettings.local.json`. Product version 0.1.0, Windows build 19045.

The supported VerifyOnly route revalidated four Product assemblies on each side plus all 182
retained harness files. Result/receipt/preset/readiness, manifests/inputs and actual outputs were
rehashed. Seven unique categories Passed, 59 held assertions, zero failed, three distinct real
Operator decisions across two non-synthetic reviews, CandidateProblems empty. The claim, database,
fine-hair case, A1 live log/command and both decision inputs match A1 closure hashes too. No A1
review, reaggregation or execution occurred. Older Pending/Failed reports remain historical.

## Publication and independent disk readback — PASSED

The user expressly authorized the named A1 readiness as the existing operator EnvironmentReadiness
attestation. Its file is the run's `readiness.json`, SHA-256
`7F746BBD507A4693B4499B126E6EDD861DBEB7538E448D7CD8487AB879742E52`, Verified true, all blocking
checks Passed, recorded at `2026-09-16T00:13:59.7992809+00:00`. It is historical A1 evidence;
it is **not** the fresh normal-App verification reported below.

No active record existed before publication, so no record required replacement/backup. The first
attempt under Windows PowerShell stopped at preset JSON parsing before writing (UTF-8 text was
misdecoded); absence of an active record was checked. No file or script was repaired. The same
successful-publication route under installed PowerShell 7 exited 0:

```powershell
pwsh -NoProfile -File tools\installer\Set-PrintFlowProductionRevalidation.ps1 `
  -InstallFolder 'D:\Repositories\printflow-Studio\artifacts\pf-accept-a1\build-pairs\f0ac92e2-853e-4d2f-a6e4-c0af1a24e0e8\candidate' `
  -WorkspaceRoot 'D:\PrintFlowStudio' -EnvironmentReadinessPassed `
  -StandardRegressionSetPath 'D:\PrintFlowStudio\TestData\v3\manifests' `
  -StandardRegressionSetResult 'D:\PrintFlowStudio\TestData\v3\runs\a1-v3-20260916-121334-903986e7\result.json'
```

`StandardRegressionSetPath` is the manifests folder, unlike the review wrapper's `SetRoot`.
No omitted-result/revocation route, fake reader or hand-edited passing record was used.

Persisted record: `D:\PrintFlowStudio\Revalidation\production-revalidation.json`, SHA-256
`78B0464C6A10F43DF8D9B2AEF669D0B429D03A02911513CBCDBA2C252030B0FF`;
attested by `DESKTOP-0BG8884\admin` at `2026-09-16T15:08:54.4314947+12:00`.
Independent JSON disk readback compared schema 2, product version, four candidate hashes/build
identities, preset id/version/hash, Windows build, Meitu/Photoshop hashes, readiness flag and
regression set id/status/run/invocation/binding version/content digest/evidence path/completion.
All matched the original evidence and actual candidate bytes. Record remains unchanged after the
live failure; publication approval and current live admissibility are separate facts.

## Ordinary Product result — BLOCKED

The exact candidate was launched normally at `2026-09-16T15:09:34+12:00`, PID **35440**, no
arguments. Process executable path was independently read from Win32_Process. Home showed
existing interrupted/recent work; none was opened, resumed, abandoned or processed.

Current callable Computer Use `node_repl` + installed `@oai/sky` read the normal WPF UI. Its click
failed with `coordinate input geometry is unavailable`; Photoshop screenshot capture separately
failed with `SetIsBorderRequired ... 0x80004002`. No alternate UI injection was used. The operator
opened **生产就绪状态**, then was asked to click **运行实时应用检查** once. The actual report
establishes the following results independently of the operator's confirmation.

| Item | Actual result |
|---|---|
| Before live checks | ProductionRevalidation Passed; overall not verified, seven live checks NotRun in this process |
| ProductionRevalidation after live attempt | Passed, original candidate recognized against the persisted record |
| ExternalApplicationAutomationLock | Passed, canonical lease acquired for the bounded check |
| Meitu launchability / safe starting state | Passed, attached PID 11484, KnownWelcome |
| Photoshop launchability / starting state | Passed, attached PID 1488, KnownStartScreen; no document open at the initial check |
| Photoshop colour settings | Passed: sRGB IEC61966-2.1; Coated FOGRA39 (ISO 12647-2:2004); Gray/Spot Dot Gain 15% |
| PhotoshopTestImageRoundTrip | **Failed**, PhotoshopUnknownState at CloseGuard |
| Final ordinary readiness/admission | **Not verified / CLOSED**, six of seven live checks Passed |
| Probe cleanup | **NotRun**; exact close and prior-state restoration not confirmed, backing file retained |
| Lease after completion | **Free**; read-only canonical SQLite query found null OwnerToken and all ownership fields null |

The displayed failure was: `No window owned by Photoshop matching the signed '另存为' signature
appeared within 20s. Nothing further was sent.` This is the Product's existing message; A2 added
no signing requirement. Diagnostic attempt `2026-09-16 03:12:03Z`, probe
`6eff41591db34c0b91177b34d398d103`. Recorded stages: ProbeCreation, ProbeCreated, OpenGuard,
OpenRequested, OpenConfirmed, IdentityCheck, IdentityConfirmed, CloseGuard. Last attempted
CloseGuard; last confirmed IdentityConfirmed; no CloseConfirmed, PriorStateRestored or cleanup.
No second failure was listed. No live retry, blind dialog handling or Product repair occurred.

Final gate evidence is the ordinary EnvironmentReadinessViewModel report from the production
`VerifiedEnvironmentGate` registered as the same `IEnvironmentDiagnostics` singleton. It is not
a harness identity check, injected result or customer-operation test. Each later production
request still re-evaluates current conditions; publication is not perpetual admission.

Two nonblocking advisories remain: 16/30 accepted evidence files lack read-only attributes while
all hashes match; external-app UI-language information. Neither explains away the live failure.

## Current application and probe state — preserve for follow-up

At final observation PrintFlow PID 35440 remains on the failed Production readiness report.
Photoshop PID 1488 remains open with window title
`PF_ENV_PROBE_6eff41591db34c0b91177b34d398d103.png @ 100% (图层 1, 灰色/8)`.
Meitu PID 11484 remains open, title `美图秀秀`; the live check identified KnownWelcome.
Do not claim Photoshop was restored to document-free state.

Retained probe:
`D:\PrintFlowStudio\EnvironmentVerification\6eff41591db34c0b91177b34d398d103\Working\PF_ENV_PROBE_6eff41591db34c0b91177b34d398d103.png`,
68 bytes, SHA-256 `431CED6916A2A21A156E38701AFE55BBD7F88969FBBFC56D7FE099D47F265460`.
It was not manually deleted or closed. All seven historical retained probe files rehashed
unchanged. Canonical lease store:
`C:\Users\admin\AppData\Local\PrintFlow Studio\workstation-automation-v1.db`, resource
`printflow-studio.external-automation.v1`; no synthetic lease manager or outer lease was used.

## Evidence, reassessment and next boundary

Raw local evidence: `artifacts/pf-accept-a2/` — prepublication-verification.json, both publication
attempt logs/outcomes, published-record.json, record-readback.json, candidate-process.json,
normal-app-prelive.txt, normal-app-live-result.txt, lease-before/after.json, retained-probe.json,
probe-files-before.json, final-processes.json, final-preservation.json and A1 closure hash checks.
These are ignored local evidence, not committed customer data. A1 operator bundle and historical
records remain in place. See [clause reassessment](ASSESSMENT.md).

**Next A3 executor: Claude Code, only after separate authorization and resolution of the A2 live
gate blocker. Do not start A3 from this handoff.** Its candidate is the exact folder above, actual
gate is CLOSED and current Photoshop state contains the retained probe. A separately scoped
follow-up must resolve the real CloseGuard failure and safely handle that exact probe before a
fresh normal-App verification can establish admission. That repair is not authorized by this A2
publication task. No installation, deployment, push, full suite, standard-set rerun, new pair,
customer job, online Jira transition or final whole-project release was performed.

## Latest continuation — current-state reconciliation pending normal check

The earlier final probe-open statement describes the earlier observation only. The current
operator reports Photoshop empty and confirmed exclusive availability/saved work for this
execution. After restoring the same Photoshop process's minimized window through permitted
Computer Use, its refreshed title was `Adobe Photoshop CC 2019`, with no document surface in
the accessibility tree. The initial minimized title named `Choppers.tif`; it is insufficient
evidence of current documents. A complete current document read has NOT yet been obtained.
Neither probe presence nor AlreadyAbsent is asserted from a title or retained file.

The exact original candidate is now open normally on Home. Permitted `sky.click` failed with
`coordinate input geometry is unavailable`; no alternate injection channel was used. The
operator has been asked to open **生产就绪状态**, click **运行实时应用检查** once, and report
completion. Resume this same task by reading its actual normal report. No new check result,
automatic historical close, recovery success or A2 PASS is claimed. Do not repeat publication.

All 29 primary hashes, four candidate assemblies and 182 harness files match. Targeted tests
passed 88/88. The canonical lease was free at 2026-09-16T04:44:15.953Z; this is a timestamped
observation, not a perpetual claim. Product bytes remain unchanged. See [RECOVERY.md](RECOVERY.md).
No A3 is authorized.

## Final corrected-candidate continuation — 16 September, 18:23 local

User prompt 19 superseded the old repair ban. Exclusive desktop/saved-work confirmation was
reused within this execution. Root was the sole desktop executor. Bounded backend/UI workers
and the same sole read-only reviewer completed the offline changes; actual route metadata is
UNVERIFIED, offset0 unchanged. No claim of a parent model switch.

The original `6eff...` probe was proved absent before the operator's repeated check; its cause
of disappearance remains unknown. Original candidate repeated the SaveAs CloseGuard failure
with `60f...`. Product now recovers a census-observed canonical owned probe under the actual
shared lease and uses getter-only exact identity for runtime-backed open and close, avoiding
the unnecessary SaveAs modal transitions. Wrong/unreadable/dirty/shared recovery targets,
unknown modals, process changes and final exact-title mismatches still refuse input.

Core repair commit `1b948b7`, pair `fa0c9a3e-987d-4b32-befb-71ed9281ddeb` (full identity in
RECOVERY.md), passed final paired full suite **11,939/11,939**. Live diagnostic recovered b87a,
completed fresh d93d probe through cleanup, and passed all live checks. Its normal report and
ProductionAuthorised remained false. New v3 run `a2-v3-20260916-181700-fa0c9a3e` used this exact
pair, passed its own fresh readiness round trip with7f87, but FAILED at Meitu portrait identity:
the Save surface did not equal the foreground editor window, so no dialog control was used.
Subsequent external cases were blocked. Transparent PNG/reference TIFF passed. No new artwork
reviews are ready and no old decisions were transferred. The failed result remains Failed.

The ordinary candidate's **安全恢复并重新检查** action was executed at18:23. Its automatic
ProductionRevalidation check refused the unqualified bytes, so the seven live items were not
run. Their generic failure descriptions were found misleading. The final display-only fix
passed94/94 screen/localization tests and now truthfully describes them as unexecuted; this
does not open the gate or authorize another v3 run.

Current confirmed desktop: Photoshop PID1488 has a complete zero-document census at
06:19:15.8298154Z; Meitu PID5980 editor holds the visible **保存图片** panel from the failed
qualification, observed by native accessibility. No save/discard/dismiss input was sent to it.
Canonical lease all owner fields null at06:20:15.734Z. Original pair inventories and29 primary
hashes match after the run; accepted preset1.18.0 and original production record unchanged.

Next genuine boundary: scoped Meitu Save-surface foreground diagnosis/recovery, then a newly
authorized complete qualification run after a stable correction if needed. Prompt19 section7
authorized one new complete v3 run, which has been used; do not silently repeat it, publish the
failed replacement or present it as Pending artwork review. No A3/install/deploy/push/Jira or
customer work. Original A1 remains Passed for its original candidate only.

### Latest retained UI candidate and actual ordinary gate

Final source `f27fce130d63d9db697348d4bb52eb77a6826a17`, pair
`0298e108-5572-47ba-a3af-9bb5592cf6ff`, receipt SHA-256
`7AB33AF3B5E3203C5BCBFAC12FDD890F2D61C5454845E40838602F9FA5691053`.
Candidate: `artifacts/pf-accept-a2/build-pairs/0298e108-5572-47ba-a3af-9bb5592cf6ff/candidate`.
The complete pair inventory verifies. Relative to fa0c, bound source differences are only
two App resource files, the readiness row projection and its screen tests. Core runtime source
is unchanged; fa0c's full-suite/live proof and failed qualification keep their original identity.
No complete regression run or authorizing record exists for0298, and none is implied.

The actual0298 executable was launched normally, PID37024. At18:30 the ordinary primary action
was executed and returned **ProductionRevalidation Failed / live checks NotRun / admission
CLOSED**. All seven live rows now say, "前置检查未通过，本项未运行。请先处理未通过的检查项，再重新检查。"
Technical details remain collapsed and available. No duplicate probe input occurred because
the automatic gate refused before live execution. App is left on this normal readiness page.
Final lease read at06:30:57.914Z shows all owner fields null. Evidence:
`recovery-final-ui-identity.json`, `recovery-final-ui-normal-recheck.txt`,
`recovery-final-ui-processes.json`, `recovery-final-ui-lease.json`.

Ordinary operators have a plain recovery/recheck action; actual owned-probe recovery was proved
through the same Product route in fa0c. **They cannot yet complete production admission through
the ordinary app**, because replacement qualification has not passed and Meitu's Save panel
remains unresolved. Do not describe this as released/integrated into the original accepted
candidate. The one new complete v3 authorization has been consumed; request a new bounded
qualification authorization before another such execution. No A3.
