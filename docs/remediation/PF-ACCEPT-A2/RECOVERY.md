# A2 recovery continuation — 16 September 2026

## Qualification review closed — 17 September, 17:14 local (Claude): v3 run PASSED 7/7

Same Claude Code session and host-supplied configuration; offline only. No application, desktop
input, lease, readiness, processing/export, rerun, build, pair, test or publication.
The Pending section below is the historical pre-review state of this same run.

### Operator's decision and responsibility boundary (verbatim)

> Meitu产生的结果我们无法做修改，单纯接受即可，只要确保确实产生了改动；
> 三项判断全部通过

The second line approves the three checks listed below for exactly these bytes. The first
line sets the scope: PrintFlow must invoke the intended Meitu operation, get back that
operation's actual result and validate its technical integrity. Improving Meitu's AI output,
or scoring hair/skin/edge quality, is not a PrintFlow repair task, and these artifacts are not
retouched. This is the Operator accepting Meitu's output. It does not certify that every
pixel is correct, and it does not waive the existing size, transparency, TIFF or W1
requirements or Photoshop's production contract. It applies to this run only. No decision was
generated for future artifacts, and no manifest, schema, preset or Product behaviour changed.

### Decisions recorded (review-only route, original paired reviewer)

Input `artifacts/pf-accept-a2/a2-v3-154736-visual-decisions.json` (ignored), SHA256
`A33E04ADBF93C9D0E6ECDABFF19A1E2F6F8F5913A246AA9524B150439BD7FA73`: `DESKTOP-0BG8884\admin`
(A1's verified convention), `2026-09-17T17:14:11+12:00` (the actual recording time), three
decisions, note `三项判断全部通过` verbatim. Before recording, `Reviews` was empty and all three
checks were Pending, so nothing was re-decided. The agent did not view the images.

```powershell
tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1 `
    -SetRoot 'D:\PrintFlowStudio\TestData\v3' `
    -RunId 'a2-v3-20260917-154736-d915b1a6' `
    -RecordVisualReview 'D:\Repositories\printflow-Studio\artifacts\pf-accept-a2\a2-v3-154736-visual-decisions.json'
```

Run under Windows PowerShell 5.1, as in A1. No receipt, category filter, executable override,
CandidateInstallFolder or new RunId was supplied. The wrapper selected pair `d915b1a6`'s
retained harness through the run's BuildOrigin. Review host-results invocation:
`89834f26-e25f-40ba-93ad-9e560308e9d9`. Host test 1/1. **Wrapper exit 0.** Log:
`a2-v3-154736-record-visual-review.log` (SHA256 `5D66CB56…6914`).

### Independent disk readback

Result SHA256 **`4083F1FB7F1BDA4D9B4F6A0EE7C9F95B8A4CF891087956F845D0B2CDDC5FDA1A`**
(28,592 bytes; pre-review `A6060D50…B702`). **Status Passed, "Passed. 7/7 required categories
passed."** Missing categories: none. Every assertion held (59/59). Pending: none.

- One review `3685ceef-98ee-4bb1-9811-26bda34ebad9`, `Synthetic: false`, same identity/time.
  It contains three distinct decisions, each concluded once, with the note above:
  `PORTRAIT-VISUAL-001` → `F580164E…5BB6` (enhanced PNG); `FINE-HAIR-VISUAL-001` →
  `F690AF0D…04C1` (the **pre-Trim cutout**, not trimmed `83534D2F…`);
  `CUSTOMER-DESIGN-VISUAL-001` → `FFCFABF0…D902` (session TIFF). Evidence rehashed equal.
- Unchanged: RunId, StartedAt `15:47:43.7904106+12:00`, CompletedAt `15:49:42.8903271+12:00`,
  invocation `eb7c35c7…`, binding v2, pair/receipt `52E6DBC5…8FBC`, four candidate and harness
  assembly hashes, preset, set digest `75DA6EC6…`, CandidateProblems empty. For all seven cases,
  steps, produced artifacts and assertions match the execution-time `case-*.json` files.
- Preservation 18/19 (`review-a2v3-preservation-{before,after}.json`). The only change is
  result.json. Byte-identical: all four run images, seven case files, execution claim,
  readiness, `regression-run.db`, customer TIFF, receipt, A1 result `97E422FA…600A` and the
  active `production-revalidation.json` `78B0464C…B0FF` (still A1's candidate).

### Meitu content-change observation (separate from assertions and decisions)

This is a post-run, read-only observation and was not added to result.json. The run database's
lineage gives the exact inputs. Enhancement attempt `01a0ad7a-63a0…`, SUCCEEDED, took IMPORT
Revision `01a0ad7a-62eb…` (`Source\FIX-PORTRAIT-001.jpg`, `F4CAD2A1…4634` = v3 input) and
produced `01a0ad7a-a9c9…` (`…_HD.png` `F580164E…`), which was promoted unchanged. Its trace
reads "meitu:enhance … enhancement invoked by PrintFlow; … editor returned to its signed empty
state". Background Removal attempt `01a0ad7a-ac11…`, SUCCEEDED, decision
UseAutomaticSelectionForReviewedContent, took IMPORT Revision `01a0ad7a-ab46…`
(`FIX-FINE-HAIR-001.jpg`, `5A705FE3…8D8E` = v3 input) and produced `01a0ad7b-025e…`
(`…_CUTOUT.png` `F690AF0D…`). Trim then used that Revision as its input.

Both sides were decoded with WPF (ignore colour profile → Bgra32), at the same 1200x1600,
with no EXIF orientation on either side and no resampling. Sidecars:
`review-a2v3-meitu-content-change.json`, `review-a2v3-enhancement-decoder-control.json`.

- **Background Removal — verified changed content.** The input is opaque (0 pixels with
  alpha≠255). In the output, 379,845 pixels have alpha 0, 431,254 have partial alpha and
  1,108,901 have alpha 255. This matches the Product-recorded 811,099 transparent and 1,540,155
  visible. Pixels still fully opaque stay close to the source (mean |ΔRGB| 1.852 per channel), so
  the visible subject is retained. The change is in alpha/background content, not merely a PNG
  container with an alpha channel.
- **Enhancement — verified changed decoded RGB, modest in size.** 1,291,315 of 1,920,000
  pixels differ. Largest channel difference per pixel: 0 → 628,685 px; 1 → 487,733; 2 → 317,978;
  3 → 275,826; 4–7 → 207,341; 8–15 → 2,415; 16+ → 22. The mean is 0.996 and the maximum 22.
  Output alpha is 255 everywhere, so this is not just an added opaque alpha channel. 600 of 1,900
  32×32 blocks show a same-direction mean shift of ≥1 level (maximum 4.08). That is a local
  tonal change, which zero-mean rounding does not produce.
  **Limitation:** the GDI+ control decoded the JPEG identically to WPF because both use WIC,
  so it does not give an independent measure of decoder noise. Offline, Meitu's own JPEG decode
  cannot be separated from its enhancement; attribution rests on the recorded `meitu:enhance`
  trace.
- Scope of this observation: a decoded difference shows the content changed. It does not show
  the output is better or that the AI action alone caused every difference. No threshold,
  PSNR/SSIM or quality score was applied. Trim and export are not expected to change pixels
  (Trim here kept the full extent with identical pixels, as recorded).
- No contradiction and no gap found for either operation.

### Next boundary

Qualification review for pair `d915b1a6` is **closed: Passed**. That does **not** mean A2 passed
or production admission is open. The wrapper printed its standard
`Set-PrintFlowProductionRevalidation.ps1` publication command, which was **not** executed. The
active record still names the A1 candidate. Remaining A2 work, which needs its own
authorization: publish the replacement revalidation for this candidate from result
`4083F1FB…DA1A`, read it back independently, then verify the ordinary Product gate for that exact
candidate with a current normal live check (which needs exclusive desktop confirmation). Normal
production admission was not reverified here. No A3, install, deploy, signing, push or Jira change.

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

## Claude takeover — 17 September: marker-read correction, stopped at changed availability

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

## Final qualification outcome — 17 September 2026

**FAILED; ordinary A2 admission remains closed. Stop at this new qualification outcome.**
Final source `95bfa1bc817db7717d870fbaade2df4301b4616c`, pair
`d2a0a55e-6f02-43f9-bd89-8e0ee3138dde`. Full immutable-harness suite passed
11953/11953, zero failures/skips (5m38s). Recovery and focused live proof passed as recorded below.

Complete run `a2-v3-20260917-131609-d2a0a55e`, invocation
`838fa069-00bc-4eae-87aa-f07bfac1be92`, explicitly selected `D:\PrintFlowStudio\TestData\v3`.
No category filter, executable override, diagnostic-unbound mode, build/restore inside the run,
or outer lease. Pair receipt verified; recorded CandidateProblems remains `[]`, unedited.
Result: `D:\PrintFlowStudio\TestData\v3\runs\a2-v3-20260917-131609-d2a0a55e\result.json`.
Result SHA256: `87637FAC27ED031646CB11997079274A0B9B6FB11179C251CE73F765603BE2FD`.
Readiness at01:16:25Z was Verified, including the repaired Photoshop test-image round trip.

- NORMAL_JPG_PORTRAIT: Pending visual review; the repaired Save identity path completed,
  enhancement produced1200x1600 and the same bytes were promoted unchanged.
- COMPLEX_BACKGROUND_FINE_HAIR: Failed during BackgroundRemoval signed-marker reading,
  `MeituTargetLost: Window 0x1D0376 disappeared while reading signed markers:`.
- TRANSPARENT_PNG and REFERENCE_PRODUCTION_TIFF: Passed.
- COMPLETE_CUSTOMER_DESIGN, PSD_WITH_COMPOSITE_PREVIEW and SINGLE_PAGE_PDF: Failed/blocked
  by subsequent workstation readiness checks. They did not qualify.

The failed run's DB ties the error to BackgroundRemoval at01:17:09.455Z, session
`01a0acf0-0579-791e-b293-528b18ec72d8`, attempt `01a0acf0-0657-7225-91e3-4ac80f615fe7`.
It records expected output
`D:\PrintFlowStudio\Sessions\S_20260917T011646Z_18ec72d8\Working\01a0acf0-0657-7225-91e3-4ac80f615fe7\FIX-FINE-HAIR-001_CUTOUT.png`.
No fine-hair artifact was produced in the result. Retained failure screenshot:
`D:\PrintFlowStudio\Evidence\20260917T011709Z_background-removal-c1-failed_1D0376.png`.
Inspection shows the Meitu cutout screen and fixture still visible. The failure text therefore
must not be promoted to proof of process crash or native HWND destruction; the exact marker-read
failure cause remains unproven. Historical HWNDs remain evidence only. No unchanged retry,
new input, Save/discard, cancellation or close was attempted after this outcome.

### Exact pending visual decision (not sufficient to qualify this failed run)

ID `PORTRAIT-VISUAL-001`; no decision recorded or old approval transferred.
Artifact: `D:\PrintFlowStudio\TestData\v3\runs\a2-v3-20260917-131609-d2a0a55e\FIX-PORTRAIT-001-enhanced.png`.
Recomputed SHA256: `05500FA29028CC9228800C7C477CEDC77041CE3DB2C8AD2EF9AE72D567888825`.
Question: Does the enhanced export still look like a correctly enhanced portrait — subject sharp,
skin tone unshifted, no visible artefact introduced along the hair or shoulder edges?
A positive portrait decision alone cannot repair the other failed categories or authorize publication.

All29 original preservation hashes still match after qualification. Both prior failed results are
unchanged, original A1 pair inventory verifies, accepted1.18.0 and active revalidation are unchanged.
Final read-only shared-lease observation has null owner/process/acquisition fields. Evidence:
`artifacts/pf-accept-a2/meitu-save-final-outcome-preservation.json`,
`meitu-save-transition-final-lease.json`, `meitu-save-transition-final-review.json`,
`meitu-save-transition-qualification-automation-log.json`, and the fresh run log.
Source/test commits6e97aed,98534a4,95bfa1b are retained locally. No preset/set edits, publication,
install, deploy, push, signing, Jira changes, unrelated repair or A3. No numerical allowance is
requested: this stop follows the requested new qualification outcome, not a lifetime run limit.

Routing reassessed at outcome/recordkeeping: safety analysis remains Astra High target at offset0;
mechanical records would target Luna Low. ActualRoute remains UNVERIFIED with
MODEL_SWITCH_UNAVAILABLE; no downgrade or model switch is claimed. Independent review used a
fresh reviewer context as documented; no additional agent or task was created for recordkeeping.
## Latest continuation — 17 September: verified discovery transition

The first corrected pair680dde3e passed the full paired suite (11951/11951) and focused
live proof, then complete run `a2-v3-20260917-125900-680dde3e` FAILED (2/7). Its result
SHA256 is `1BA3E562900AE35C11E1F4CB90762D9E87C61984EBC792FC33DBF68E62864F11`.
It remains immutable. Binding.CandidateProblems is the recorded empty array; nothing was removed.
The portrait failure records expected Save HWND equal to actual foreground HWND: Qt completed
activation during signed-control discovery, between the two foreground reads. The second check
incorrectly required the editor even after the desired exact Save transition had completed.
Getter-only observation confirmed the same directly owned standalone native dialog, with the
retained portrait filename. No embedded-panel exception was introduced.

Final Product source `95bfa1bc817db7717d870fbaade2df4301b4616c` accepts that observed exact
Save handle/PID transition only by rerunning the existing owner and exact-foreground verification.
It does not relax another-foreground rejection or change timeouts. The regression reproduced the
failure before the correction; final targeted tests passed190/190. Fresh independent safety
re-review found no blocking findings in the change or the incident-bound recovery wrapper.

New immutable pair: `d2a0a55e-6f02-43f9-bd89-8e0ee3138dde`; receipt SHA256
`78880DAF3AF5F81F71741B7A0EAE6525FCF6FBC56F43D0326199200B84FF0BFF`.
The gated recovery validated the failed result, its receipt, run DB, IMPORT identity and exact
working file, plus current executable/process/native owner/control identity, under the real
shared lease. It cancelled only that interrupted identity probe, then generated a fresh ordinary
identity probe before closing the verified fixture to KnownEditorEmpty. No Save/discard/cleanup.
Working file SHA256 remained `F4CAD2A1EC7994E42E2A77CC6F30E29DE91D344821E4EE7AC01C7E712D9A4634`.
Recovery log: `artifacts/pf-accept-a2/a2-meitu-recovery-20260917-1310-d2a0a55e.log`
(actual observation at01:09:01Z; the log label is not a timestamp authority).
Fresh focused proof `a2-meitu-focused-20260917-1309-d2a0a55e` completed open/identity,
enhancement completion, destination/read-back/PNG export, unchanged source, result dismissal
and empty-editor close. Its UTF-8 transcript is retained next to the log. Full paired suite and
fresh complete v3 qualification are in progress; no replacement revalidation has been published.
## Current execution — 17 September: owned dialog proved and corrected

The operator replied "Please continue" to the one pending availability/saved-work request;
execution resumed within that exclusive session. No repeat confirmation is required while valid.
Restoring the freshly returned Meitu window showed the welcome page, not the retained panel.
Its prior dismissal cause is unknown. Current state was saved in
`meitu-save-20260917-current-welcome.json` under `artifacts/pf-accept-a2`.

Verified retained pair0298 and ran fresh synthetic focused attempt
`a2-meitu-focused-20260917-123444-0298e108` under the canonical shared lease. Initial open/identity
succeeded; the pre-enhancement identity probe reproduced the foreground failure. No enhancement
or export occurred. The legacy smoke returns a passing xUnit test even for a guarded refusal;
the transcript, not that wrapper exit code, establishes this FAILED focused operation.
Its transcript is retained unchanged, SHA256
`4208DE5118CED3C4C9A5CFDCA95791CA77048BB519D005616CDC18F27792E886`.

Getter-only observation pair `0f848376-ffe7-4bf7-a586-a35573917eb9`, source `6e97aed`,
at00:37:55Z proves Save HWND0x420CF0 has direct GW_OWNER0x1D0376, style0x96080000
(WS_POPUP, not WS_CHILD), title Form, class Qt51517QWindowToolSaveBits, visible/enabled.
The exact editor0x1D0376 was disabled; UIA shows MainWindow.MaskDialog directly under MainWindow.
Its default filename was the exact retained synthetic `PF_BACKGROUND_C1_826D74B4797B_副本`.
Thus this is a standalone native owned dialog even though UIA nests it beneath the editor;
it is not authority for an embedded-panel foreground exception. These HWNDs are evidence only.
Raw log: `a2-meitu-readonly-20260917-0037-0f848376.log`; UIA snapshot:
`meitu-save-20260917-focused-failure-uia.json`.

Product source `98534a4ba73afedfcfb2094703175dc5d02737d2` preserves exact-dialog foreground.
It records direct native ownership, verifies the exact editor host and signed Save controls,
and activates/reacquires only that standalone dialog while the exact editor retains foreground,
within the original DialogTimeout. It re-enumerates replacement HWNDs and rechecks before read
and cancel. Same-process alternate owners/foregrounds, ambiguous dialogs and failed activation
refuse input. No timeout or preset changes; export destination/read-back/output guards unchanged.

A fresh independent safety reviewer rejected an intermediate generic pending-dialog recovery:
an editable filename must not become document identity. That branch was removed before any live
execution. Ordinary Confirm continues to refuse unsolicited pending dialogs. Re-review found no
remaining blocking code finding. A separate incident-bound, opt-in smoke validates the recorded
failed transcript hash, exact synthetic source path/hash, accepted executable and process start,
fresh native owner and signed controls; it calls Product's guarded cancellation only, then a
fresh ordinary identity probe before close. It is not general recovery authorization or ordinary
admission. The normal Product correction is the dialog transition handling, not that smoke.

Final-source targeted tests passed188/188, including red→green regression, wrong owner, unrelated
same-process foreground, refused activation, replacement dialog, unsolicited pending Save and
cancelled cancellation. The independent review used an isolated native sub-agent, requested Astra
High at offset0; actual model/effort metadata UNVERIFIED, no parent switch claimed. The UI/native
safety work remains tightly coupled; no downgrade is claimed. Mechanical recordkeeping remains
in this context as a disclosed fallback (MODEL_SWITCH_UNAVAILABLE).

Corrected pair `680dde3e-c12a-4d10-9d8f-d19d6a625241` was built and verified from source98534a4.
`a2-meitu-recovery-20260917-0052-680dde3e` passed at00:51:55Z: cancel-only, fresh exact identity,
KnownEditorEmpty. Retained synthetic backing file unchanged, SHA256
`C2BFBF036791E041BAA05E229992D84816BD5F3D9DD76997CEB386187FC20E65`; no save/discard/cleanup.
Fresh focused attempt `a2-meitu-focused-20260917-125218-680dde3e` then completed identity,
enhancement, PNG export1280x960, source unchanged, result dismissal and empty-editor close.
Output hash5207F744E04267CE1AA68BEAC7C0602CF5FBA62A8D17A2EA9B3139417F643ECC is smoke proof
only; its normal temporary cleanup ran. Raw log and UTF-8 transcript are retained in A2 artifacts.
v3 preflight passed7/7 with explicit set root. Final paired full-suite and qualification outcome
will be recorded below; no replacement revalidation has been published.

## Earlier observation — 17 September: availability pending before operator continuation

The new user authorization supersedes the one-complete-run limit in the historical entries below.
Further complete runs are authorized after demonstrated relevant correction or positively verified
state recovery plus focused proof, with a fresh RunId and matched immutable pair each time.
No numerical allowance needs to be requested merely because an earlier run failed.

Actual starting HEAD: `905263abdf00bbcc2cbe069b3d1633abb4d34e37`, master, clean; no reset.
Current source was inspected after the latest RECOVERY/PLAN/HANDOFF. Relative to retained pair0298's
source f27fce1, HEAD changes only these A2 documents, not Product/test source.

Primary incident remains `a2-v3-20260916-181700-fa0c9a3e`, Failed. Its result hash still matches
`1BCB9EBB2477201E2E06AC40A40029A40CB1F278395C6E1AC3521E971234272D`.
All29 primary preservation hashes rechecked with zero mismatches, including original A1 evidence,
accepted preset and active record. New local evidence:
`artifacts/pf-accept-a2/meitu-save-20260917-preservation.json`.

The exact failure text maps to `GuardedMeituUiDriver.VerifyIdentityDialog`, called by
`ReadIdentityValue` and `CancelIdentityDialogAsync` within `ConfirmWorkingCopyIdentityAsync`.
The operation opens Save as a document-identity probe, attempts a filename read, then attempts
cancel even if the read failed. The same foreground mismatch rejects both before a value read or
cancel invocation. The returned failure is the cancel failure, masking the identical earlier read
failure. This is not evidence of an export or format-popup operation. No Save/SaveAs is invoked
by this identity route; its initial action is the editor control that presents the Save surface.

The retained UIA tree proves a nested `MainWindow.MaskDialog.MaskCenterWidget.SaveMaskWidget`
presentation, but does not record sufficient native owner/parent/style identity to distinguish an
embedded panel from an owned top-level dialog. Current `FindOwnedDialogs` enumerates top-level
same-process windows having any nonzero GW_OWNER; it does not compare that owner to the expected
editor. Therefore that lookup alone cannot authorize an embedded-surface foreground exception.
No guard has been relaxed based on PID, title prefix or broad ancestry.

Permitted Computer Use initialization and list_apps succeeded. Its currently returned unique
Meitu window id is12455402, title 美图秀秀; read-only get_window_state refused because the window
is minimized. No activation/input followed. Process metadata shows Meitu PID5980 still started
16 September15:38:11 local, while Photoshop is now PID24172, started17 September10:43:52 local.
Yesterday's desktop/empty-document observations are therefore not current proof. Historical HWNDs
were not used as selectors. One current exclusive-window/customer-work-saved confirmation is
pending; no duplicate request has been issued.

Next: after that confirmation, freshly reacquire/observe the current Meitu target, prove the
native/UIA relationship and exact document, and construct focused proof before any correction or
complete run. If the retained panel is absent, record that fact without inventing its dismissal
cause or inferring document safety. Use the real shared lease for Product execution. No lease was
acquired in this read-only continuation; yesterday's free-lease result is not a current claim.
No code/test change, build, test execution, recovery input, complete run or publication occurred.
Qualification remains unresolved and admission has not been reopened. No A3.

Status: Photoshop recovery and fresh round trip verified; replacement qualification FAILED
at a separate Meitu Save-surface foreground guard. Ordinary A2 admission remains CLOSED.
This report appends current evidence without replacing the historical failed attempt.

## Observed state and bounded diagnosis

Photoshop PID 1488 has creation time 2026-09-15 11:03:56 local and executable
`D:\Adobe Photoshop CC 2019\Photoshop.exe`, consistent with the earlier accepted process.
Computer Use initially returned a minimized window naming `Choppers.tif`; accessibility capture
refused because it was minimized. Supported activation, window refresh and accessibility read
then returned `Adobe Photoshop CC 2019` with no document surface. This corroborates the
operator's empty-screen observation but does not constitute a full document census. No Close,
Escape or Save As input was sent. No old probe was deleted. Closure cause remains unknown.

Source and original failure agree: the round trip reached CloseGuard; the close helper invoked
ProbeDocumentIdentityAsync, requesting SaveAsProbe again before CloseActiveDocument. The owned
Save As window did not appear within 20 seconds. The previous identity probe had returned only
after CancelDialogAsync/AwaitDialogClosedAsync reported its surface gone and host enabled.
The source does not establish why the subsequent request produced no matching window. There is
no evidence that a Close request occurred. The existing filename-field settling fix is intact.
No speculative change to guards, timeouts or input dispatch has been made.

## Verification and preserved identity

The original candidate was launched through supported Computer Use and read on its normal Home
screen; no interrupted or customer operation was selected. All 29 primary evidence hashes,
four candidate Product assemblies, and 182 retained harness files match their recorded values.
A1 remains Passed for the original pair. Preset 1.18.0 and the active published record are
unchanged. There was no writer invocation, replacement pair, A1 rerun, or Product source edit.

Targeted command (installed SDK, no live smoke opt-in):

```powershell
& 'C:\Users\admin\AppData\Local\Microsoft\dotnet\dotnet.exe' test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --no-restore --filter "FullyQualifiedName~GuardedPhotoshopUiDriverTests|FullyQualifiedName~ProductionLiveWorkstationVerifierTests|FullyQualifiedName~EnvironmentReadinessScreenTests" --verbosity quiet
```

Result: 88 passed, 0 failed, 0 skipped. Initial unqualified `dotnet` resolved to the system SDK 8
and failed SDK selection before testing; the installed per-user SDK resolved this without install
or repository configuration changes. No full suite is justified by documentation-only changes.

Canonical lease read using a read-only SQLite connection at 2026-09-16T04:44:15.953Z found the
resource row with all ownership fields null. No lease was acquired, fabricated or evicted here.
The normal Product live action must acquire its real lease when it runs.

Local raw evidence: `artifacts/pf-accept-a2/recovery-window-observations.json`,
`recovery-processes.json`, `recovery-preservation.json`, `recovery-harness-preservation.json`,
`recovery-lease-observation.json`, and `recovery-normal-app-latest.txt`.

## Pending ordinary action and honest gate result

The permitted click on Home.ShowEnvironment failed with `coordinate input geometry is unavailable`.
The operator was asked to open **生产就绪状态**, click **运行实时应用检查** once and report completion.
This is a tooling limitation, not a proved Product defect. Do not substitute an injection script
or test host for the normal gate. Reuse the already received exclusive-window confirmation.

Current fresh gate result: NOT RUN in the newly launched normal candidate. Last completed normal
gate remains the historical failed round trip; no current readiness success is claimed. Probe
absence still needs a complete successful Product document read. Historical A1 publication is
preserved and does not itself grant current admission.

An ordinary recheck button exists and requires no technical operator instructions. Whether that
action completes this recovery is still unverified. No Product recovery correction or integrated
operator-friendly recovery capability is claimed. Continue after the narrow operator action;
if the failure persists, diagnose its specific current condition and apply the authorized repair
branch rather than repeating identical attempts. A3 remains out of scope.

## Operator check completed — 2026-09-16 05:01:58Z

The operator supplied the actual repeated-failure screenshot; supported Computer Use read and
retained the full ordinary report as `recovery-normal-app-repeat-failed.txt`. ProductionRevalidation
still passed for the frozen original candidate. The complete pre-probe Product runtime read
reported `KnownStartScreen; No document is open.` This positively establishes the original
`6eff41591db34c0b91177b34d398d103` probe was AlreadyAbsent before this attempt. Its historical
failure is retained; the cause/time of its disappearance and any automatic close remain unproven.

The new attempt `60f7def57ed14ae1bee7a9cc8cfbb45e` failed at the same CloseGuard identity-dialog
wait. No CloseRequested or CloseConfirmed occurred. Fresh window observation named this new
probe. A getter-only opt-in smoke using the existing Product runtime reader under the actual
shared lease read a complete census at 05:06:44.8272459Z: accepted Photoshop PID 1488, same
start time, exactly one document, exact new managed probe path, saved and active. No unrelated
document was reported. This reader produced no input, cleanup or authorizing readiness result.
The normal logger omitted successful-test output on the first read, so a second read with
detailed logging captured the facts; neither invocation mutated Photoshop. Raw output is
`recovery-readonly-census-detailed.log`; the lease was independently free at 05:07:32.888Z.

Repeated original-gate result: FAILED, admission CLOSED. This repeat is discriminating evidence
against treating the earlier failure as only stale display state. Candidate changes are now
being developed separately: normal Product recovery/identity boundary and compact readiness UI.
No frozen pair, original result, preset or publication has been patched.

Routing at the natural backend/UI boundary: offset 0 unchanged; requested native backend worker
`readiness_backend` with `gpt-5.6-sol/high` and UI worker `readiness_ui` with `gpt-6-astra/high`,
each given bounded fresh instructions and separate file ownership. These are implementation
workers, not independent reviewers. Runtime model/effort metadata remains UNVERIFIED. Root
retains sole desktop/executor ownership; workers run offline work only. Root owns the getter-only
diagnostic smoke, evidence, integration and final qualification. There is no claimed parent
model switch. One scoped independent read-only reviewer will follow stable changes.

## Product correction and independent review

The normal readiness command now reconciles only one census-observed, exact canonical probe in
the reserved workspace path; it never enumerates old probe files. It requires the real shared
lease, sole active saved document, canonical bytes, no reparse traversal, accepted process
continuity and known modal state. It re-proves the stricter recovery conditions at the final
runtime census. A missing reader, unknown state, changed file, other work or contradictory
current title refuses input. Close settlement and post-close observation remain under ownership,
including cancellation; only the exact confirmed-unheld canonical file may be removed.

The normal foundation close uses the already-supported getter-only runtime identity instead of
raising a second Save As dialog. It retains the final refreshed-window, process, foreground and
modal guards and existing no-save close behavior. Ordinary dirty-owned-document cleanup remains
distinct from recovery's stricter saved/sole condition. The demonstrated defect is the repeated
modal identity route failing before close; the underlying Windows/Photoshop timing reason for
the missing second dialog is not claimed as proven. No timeout inflation or broad guard removal.

An already-absent known probe records AlreadyAbsent, not a historical close. Missing files and
removed empty token directories are idempotent; unreadable state is never empty. Recovery is
separate from fresh probe success. Previous diagnostics, when available, retain their failure;
old probe timestamps are not relabelled by a new preflight refusal. Restart recovery records
current identity provenance rather than inventing a previous attempt.

The existing ordinary action is labelled **安全恢复并重新检查 / Safe recovery and recheck**.
Passive **刷新状态 / Refresh status** stays read-only. Processing and cancellation are visible;
overlapping actions and Back stay disabled until awaited work settles. Operator status comes
from typed recovery keys, and readiness only from the authoritative report. Technical detail,
full checks and restart help start collapsed. English/Chinese offscreen source renders were
inspected at 1000x700 with no clipping or binding error; these are not native live screenshots.

One isolated native reviewer, `a2_recovery_review` (requested gpt-6-astra/high, offset 0; actual
runtime metadata UNVERIFIED), read source and evidence without builds, tests or desktop actions.
It found two P1s: recovery's final census initially allowed a newly dirty/shared document, and
title corroboration initially used a pre-activation snapshot. Both were fixed with red-to-green
sequenced tests; the same reviewer re-read the final dispatch guard and reported both closed,
with no remaining blocker in its affected scope. This is source review, not live acceptance.

Focused backend checks: 101 passed, covering lifecycle/recovery and foundation/guarded close.
UI/localization/diagnostics boundary slice: 133 passed; final wording/render subset: 5 passed.
The final clean build/full suite and live qualification are recorded below when completed.

Clean Release build passed with zero warnings/errors. The first full suite completed with
11,932 passed and one failure: its lexical Process.Start prohibition also matched two new
diagnostic StartedUtc getter expressions. No launch occurred. The diagnostic code now names the
immutable observed instance explicitly; the prohibition/assertion is unchanged. A first alias
still contained the banned substring and failed the focused check; the final name removed the
collision. The affected architecture/live-verifier slice then passed 67/67. Both failure logs
remain under artifacts/pf-accept-a2. Runtime recovery behavior was not changed by this correction.

The reviewed, focused-tested fix is committed before creating the isolated controlled pair.
Its clean paired harness build will supply the final complete suite, before any live diagnostic
or new standard-set run. This verifies the same retained build used for qualification; no
further conventional-output rebuild or tests are needed unless a real failure requires repair.

## Controlled pair and new discriminating live evidence

Stable correction commit `38d102985cd1e73eec589b12f7f3a0a72eb3a07a` produced controlled pair
`a05e8547-e10a-4154-9834-516035d29632`. Its receipt and complete harness/candidate inventories
verified. Final paired suite: **11,933 passed, zero failed/skipped**, 5m45s; raw log
`recovery-final-paired-suite.log`, TRX in `final-paired-test-results`. The normal candidate was
launched without arguments and its ordinary readiness page showed ProductionRevalidation
as the blocking check. The recovery button, plain-language status and collapsed details were
observed through supported native accessibility. It was exited normally before live diagnosis.

The first repaired diagnostic **did recover** exact probe `60f7def57ed14ae1bee7a9cc8cfbb45e`:
Product recorded ClosedExactProbe, same accepted process 1488/start time, sole saved active
canonical document, close plus complete empty census, then exact backing-file removal. This
is a current automatic close observation; it does not revise the original `6eff...` history.
However, fresh probe `310afeea7de9492a8f52185c8be701f2` failed Open-field exact readback. Product
did not press Open, cancelled its dialog and retained the backing file. Getter-only complete
census at **05:52:00.7358051Z** proved zero documents in the same process.

One evidence-supported recheck from that confirmed settled empty state passed Open and initial
identity but failed at CloseRequested: fresh probe `b87a8bf95a02444796b8ff3f53831f9c` remained
after the existing close timeout. Complete getter-only census at **05:54:04.1560696Z** confirmed
it remains the sole active saved document at its exact managed path. Native window observation
corroborates the title with no visible modal. Thus this is a real retained document, not merely
a stale title. Both retained fresh files have the canonical 68-byte probe hash. No blind repeat
Close or SaveAs input was sent. Diagnosis now compares recovery's successful runtime-only close
against the fresh route's remaining initial SaveAs identity/cancel transition. Both diagnostic
envelopes retain ProductionAuthorised=false; neither is a readiness pass. No v3 run or new
publication has occurred. Logs: `recovery-repaired-live-diagnostic.log`,
`recovery-settled-live-diagnostic.log`, `recovery-post-cancel-census.log`, and
`recovery-post-close-timeout-census.log`.

The follow-up correction is confined to the runtime-backed initial-open identity boundary.
It avoids the remaining SaveAs/cancel transition by using the existing complete getter-only
census, verifies unique same-process continuity before/after it, exact active path/name and
final modal/window state, and retains the legacy reader-null route. It does not claim the
underlying Windows timing mechanism is proven. Five new red failures established the missing
runtime-identity branch. The same independent reviewer found a coarse final title-prefix
comparison; a dedicated staged shared-prefix drift test went red, then green after applying
the existing exact-name signature rule and missing-signature refusal. Reviewer recheck found
no remaining blocker in this narrow scope. Related tests passed 107/107, architecture boundary
tests 34/34. Evidence summary: `runtime-open-identity-red-green.txt`. Source is frozen for a
new controlled pair/final suite before further live work. Pair a05 and its evidence stay intact.

## Final repair verification and qualification boundary

Final Product commit `1b948b7e273720ba24574b5206bcc3ce7afa7cf7` produced controlled pair
`fa0c9a3e-987d-4b32-befb-71ed9281ddeb`; receipt SHA-256
`8A2BED95894DAB5BFDB08FA525AC5D2FECCEB1A85601710303650B4CB569A102`.
Final clean paired build succeeded and its complete suite passed **11,939/11,939**, zero
failures/skips, 6m30s. Log `recovery-runtime-identity-final-suite.log`; TRX under
`final-runtime-identity-test-results`. The same reviewer confirmed the final exact-name rule.

At **06:16:56Z**, diagnostic `b8ba7c7298c840549793cd22a3cb41e9` recovered exact retained
`b87a8bf95a02444796b8ff3f53831f9c` through Product with the real shared lease. It recorded
ClosedExactProbe, same Photoshop process instance, complete post-close empty census and exact
file cleanup. Fresh probe `d93d40470f944d7ca17360694aac3045` then completed every required stage
through CloseConfirmed, PriorStateRestored and CleanupCompleted. All seven live checks passed.
The envelope retains **NormalReport.Verified=false, DiagnosticReport.Verified=true,
ProductionAuthorised=false**. This is repair proof, not an ordinary A2 PASS or a publishable
qualification result. Raw log and extracted envelope: `recovery-final-live-diagnostic.log` and
`recovery-final-live-envelope.json`.

The one authorized new complete v3 execution ran as
`a2-v3-20260916-181700-fa0c9a3e`, invocation `7f348016-68a7-4099-bf5a-2bb782a92427`, with the
exact final pair and candidate, no category filter, version override or unbound mode. Preflight
passed all seven categories/hashes. Its own fresh readiness probe
`7f87d8d887bd45cf95cc7c14b6a5a97e` also completed and cleaned successfully; bootstrap readiness
was Verified with no blocking failures at 06:17:51Z. This separately corroborates the Photoshop
repair from an empty start; the bootstrap is not normal Product authorization.

The full run is honestly **Failed, 2/7 categories passed**, not Pending artwork review.
Portrait failed at Meitu's existing Save identity guard: expected Save surface `0x1A30E82`,
actual foreground editor `0xB020BA`, both owned by accepted Meitu PID 5980. No dialog control
was used. `GuardedMeituUiDriver.VerifyIdentityDialog` requires exact foreground-window equality
before identity read or cancel; the same refusal therefore leaves the owned Save surface open.
Fine-hair then failed Meitu live readiness; remaining external cases were blocked by lost live
readiness. Transparent PNG and reference TIFF passed. No portrait output or manual decisions
were produced. CandidateProblems is empty and run-origin verification passes. Result SHA-256:
`1BCB9EBB2477201E2E06AC40A40029A40CB1F278395C6E1AC3521E971234272D`.

Fresh native accessibility at 06:19Z shows Meitu's editor with nested **保存图片** SaveMaskWidget,
its path/name/format fields and save/close controls. This establishes the actual visible surface,
not why Windows foreground differs or authority to relax the guard. No speculative Meitu fix,
unknown-modal input, save, discard, or second full v3 run was performed. Current evidence:
`recovery-qualification-meitu-current.txt`, `recovery-final-v3-qualification.log`, and the run's
seven case files. The next technical step is a scoped Meitu Save-surface/foreground diagnosis
and guarded recovery; a further complete qualification run requires expanded authorization
because prompt 19 section 7 authorized one new complete v3 execution. Do not reuse this Failed
result, substitute old A1 artwork decisions, or publish either replacement pair.

Final getter-only complete Photoshop census at **06:19:15.8298154Z**: PID1488, original start
time unchanged, **zero documents**. Lease observation at **06:20:15.734Z**: canonical resource
has all owner fields null. Both original and final pair inventories verify; all 29 preserved
primary hashes still match, including original A1 PASS/result, original publication and preset
1.18.0. Recovered60f/b87 and successful d93/7f probe files are absent by verified cleanup;
original6eff and the cancelled-open310af backing files remain as historical evidence. No disk
sweep occurred. Final evidence: `recovery-final-photoshop-census.log`, `recovery-final-lease.json`,
`recovery-final-preservation-and-binding.json`.

The ordinary new candidate's recovery button and concise Chinese UI were observed; English and
Chinese source renders/tests cover layout and localization. Its ordinary gate correctly refuses
unqualified replacement bytes. A nontechnical operator has the normal recovery/recheck action
implemented, with the same Product route verified by diagnostics, but cannot currently finish
admission in this unqualified build. Neither the old accepted candidate nor an installed copy
has been patched. No install/deploy/push/Jira change, publication repeat, customer job or A3.

The final ordinary recheck at18:23 exposed a display-only defect: live Blocked/NotRun rows
used failure prose despite the automatic revalidation refusal preventing those checks from
running. Their text now says the prerequisite has not passed and the check has not run, in
English/Chinese. Actual Failed reports retain their failure/recovery wording; report status,
blocking flags and production authority are unchanged. Two locale tests failed before the fix;
the screen/localization slice then passed94/94 with no skips. Evidence:
`ui-notrun-followup-tests.txt`. This last App presentation change does not justify repeating the
shared Product full suite or the one v3 execution; it requires a separate retained build identity.
The fa0c core repair/qualification evidence is not relabelled as evidence for those new bytes.

Final UI source `f27fce130d63d9db697348d4bb52eb77a6826a17` produced verified controlled pair
`0298e108-5572-47ba-a3af-9bb5592cf6ff`, receipt SHA-256
`7AB33AF3B5E3203C5BCBFAC12FDD890F2D61C5454845E40838602F9FA5691053`. Source-input comparison
against fa0c confirms only the two resource files, readiness row projection and screen tests
changed; no shared runtime source changed. At18:30 its normally launched executable PID37024
executed the primary recheck: ProductionRevalidation Failed, live checks NotRun, admission
CLOSED. All seven live rows use the truthful prerequisite/not-run explanation; technical
details remain collapsed. No live probe was dispatched by this refused normal action. App is
left on the readiness page; canonical lease remained free at06:30:57.914Z. Latest raw evidence
is `recovery-final-ui-normal-recheck.txt`, `recovery-final-ui-identity.json`,
`recovery-final-ui-processes.json`, `recovery-final-ui-lease.json`. This final UI candidate is
unqualified; the prior fa0c failed run is retained rather than relabelled or rerun.
