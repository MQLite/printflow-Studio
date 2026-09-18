# PF-ACCEPT-A2 — Publish the bound revalidation and verify the normal Product gate

## Publication and normal gate closed — 18 September, 11:53 local (Claude): A2 admission OPEN

**Current mainline state.** The sections below are historical. Evidence in [RECOVERY.md](RECOVERY.md),
outcome in [HANDOFF.md](HANDOFF.md). HEAD verified as `fcaa324` (documentation-only) first. One
exclusive-window confirmation covered the replacement and every ordinary live stage.

Every step of the remaining A2 procedure ran, in order, and each reached its own result:

1. **Qualification and candidate re-verified offline.** Result `4083F1FB…DA1A` is Passed 7/7, 59/59
   assertions, 0 Pending, CandidateProblems empty, one non-synthetic Operator review. The candidate
   was resolved through the run's `Binding.BuildOrigin` and receipt `52E6DBC5…8FBC` to pair
   `d915b1a6-4c06-4b16-9a20-5e1341d1ae9c`; its four assemblies, the seven v3 manifests and preset
   1.18.0 `8484F0AA…C8E0F` all match. 30 comparisons, zero mismatches. No review re-recorded, no
   re-aggregation, no content comparison redone, no standard-set rerun, no build, no new pair.
2. **Old active record preserved**, byte-identical (`78B0464C…B0FF`), before anything was written.
3. **Replacement published** by the existing unmodified writer under the already-supported
   PowerShell 7 runtime, with the explicit manifests folder, explicit result and exact candidate
   folder. Writer exit 0. No omitted result, no revocation route, no hand-edited approval.
4. **Record read back independently** from disk: `E6A7D7EA…F3B9`, 30 fields checked against the run
   binding, the preset file and the actual candidate bytes.
5. **The exact candidate's own `PrintFlow.App.exe` was launched normally** (no arguments, PID 4864,
   path confirmed from `Win32_Process`) and its ordinary readiness checks were run through the
   normal UI. No testhost, bootstrap, diagnostic omission or fabricated result.
6. **The gate passed on the second ordinary run**, after a diagnosed and Operator-assisted recovery
   of a Photoshop automation-registration fault that had nothing to do with PrintFlow. Final status
   **本工作站已通过生产环境校验。** — 7/7 live checks and 11/11 blocking automatic checks Passed.

The single failure encountered was not retried unchanged and not left at a recoverable state. It was
diagnosed to a missing Photoshop entry in the COM Running Object Table, several candidate causes
were ruled out by evidence, the one repair that needed a stopped application was refused by the
environment's tool restriction and was not worked around, and the Operator was asked for the exact
plain normal action instead. Its result was then verified rather than assumed.

Probe `435e5d5f…` completed through CleanupCompleted with outcome Succeeded and retained nothing;
the nine historical probe directories are untouched. The canonical lease read all owner fields null
before, between and after. The active record, run evidence, receipt, preset, A1 result, both pair
inventories and the operator prompt bundle are all unchanged.

No Product or preset change, new qualification cycle, A3, customer processing, MSI installation,
deployment, push, signing or online Jira change. A3 remains unauthorized.

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
## Current continuation — 17 September 2026: Meitu Save identity

The user authorizes scoped Save-surface diagnosis, safe recovery, minimal ordinary Product/test
correction and complete v3 requalification after demonstrated relevant correction or positively
verified state recovery and focused proof. This supersedes historical prompt19 section7's one-run
limit below. Every actual run needs a fresh RunId and a matched immutable pair, explicit
`D:\PrintFlowStudio\TestData\v3`, no category filter, executable override, diagnostic-unbound
mode, build/restore within the complete run, or outer competing lease. Preserve all earlier runs.

Started clean on master at `905263abdf00bbcc2cbe069b3d1633abb4d34e37`; no reset.
At startup, the retained pair0298 source revision had the same Product/test source as HEAD
(intervening changes were A2 documentation only). Subsequent pairs are recorded below.

Sequence: inspect retained evidence and current source; establish current exclusive availability;
prove native/UIA Save-to-editor/document relationship and caller operation; reproduce at the
appropriate seam; make only evidence-supported Product/test corrections; perform focused proof
and final-source QA; then run the complete qualification using its verified pair. Stop for exact
new Operator visual decisions when needed, with paths/hashes/questions; do not transfer approvals.

Routing: EXECUTE_HANDOFF, NormalRoute/RequestedRoute Astra High for tightly coupled guarded UI
identity/recovery diagnosis, RouteOffset0 (explicit), AdjustmentResult UNCHANGED. ActualRoute
UNVERIFIED; MODEL_SWITCH_UNAVAILABLE in the active context. No model switch is claimed. Reassess
at the diagnosis/implementation boundary; separable safety implementation targets Sol High and
any required independent review needs a genuinely fresh context. This was the startup routing assessment; implementation and review are recorded below.

The operator continued after the one availability request; that confirmation is reused while
valid. Current native evidence proves a standalone directly owned Save dialog, not an embedded
panel. Exact foreground remains required. Correction98534a4/pair680dde3e passed188 targeted
tests and independent safety re-review; explicit retained-synthetic cancellation plus fresh
identity/close passed, and a fresh focused enhancement/export/close passed. Full paired suite
and complete v3 qualification followed. Pair680dde3e passed11951/11951 tests, but its complete run failed on a second-read Save activation race. The corrected source95bfa1b/paird2a0a55e passed190 targeted tests, independent safety re-review, pinned retained-fixture recovery and fresh focused enhancement/export/close. Final paired suite and complete v3 are now next. See RECOVERY.md. No A3 or publication.

## Authority and scope

User-authorized A2 only, 16 September 2026. Codex is the A2 executor; Claude Code is the next
A3 executor, only after separate authorization. One checkout/desktop executor. The operator
confirmed the exclusive A2 window in this execution before publication or live verification.
No renewed preset/artwork approval, A1 rerun/review/reaggregation, Product/test/preset edits,
candidate replacement/build, customer work, installation, deployment, signing, push or Jira change.

Starting checkout: `D:\Repositories\printflow-Studio`, `master`, clean at
`c48387c657ff1439ece6f47428e110b05ffb5df6`; no reset.

## Installed routing

Installed policy `C:\Users\admin\.codex\workflows\development-routing.md`, v2.3.
`CODEX_HOME` was unset in the shell; the installed default-home policy was read directly.
Mode EXECUTE_HANDOFF; explicit RouteOffset 0; AdjustmentResult UNCHANGED. CONTINUE for the
dependent evidence/publication/live-check work. No global routing changes.

The normal persistence/binding route is Sol High; mechanical documentation is Luna Low;
acceptance reassessment requires the critical-review capability described by policy (Astra High).
These are policy targets, not claims about runtime execution. RequestedRoute follows those
normal routes at offset 0; ExecutionTarget is the current Codex context as a disclosed fallback.
ActualRoute/model/effort: UNVERIFIED. MODEL_SWITCH_UNAVAILABLE in this active context;
native child model selectors do not establish the parent's running model. The user expressly
allows execution despite unverified runtime metadata. Reassess at the publication/live-check
and documentation boundaries without claiming a switch. No UI implementation is involved.

## Execution sequence and completion criteria

1. Read current A1 PLAN/HANDOFF and actual result/readiness/receipt; verify fixed hashes,
   seven categories, 59 assertions, three non-synthetic Operator decisions, artifacts,
   manifests/inputs, both Product inventories, retained harness and effective configuration.
2. Use the exact original candidate and supported writer. Preserve an existing active record
   before replacement. Supply explicit v3 **manifests** and result arguments. Use the verified
   A1 readiness record as the user-authorized operator attestation, distinct from step 4.
3. Independently read persisted JSON from disk and compare actual binding fields and candidate
   assembly bytes. Publication exit 0 alone is insufficient.
4. Launch the original candidate's normal `PrintFlow.App.exe`; open ordinary Production readiness,
   observe ProductionRevalidation, explicitly run live checks, and read final admission,
   probe lifecycle/cleanup and canonical lease outcome. Use permitted Computer Use; request
   exact operator actions when its UI input cannot run. No substitute verifier or test host.
5. Reassess the relevant SCRUM-11065 and SCRUM-11123 clauses against actual evidence, preserve
   historical reports/operator bundle, update this scoped documentation and commit locally.
   Stop after A2; HANDOFF names Claude Code for later A3 and states actual gate/application state.

No build or test suite is needed for this evidence/publication/UI task. Required validation is
file integrity, persisted-record readback, actual normal-App checks and final preservation checks.

## Progress

Evidence integrity and publication/readback passed. The original normal candidate recognized
ProductionRevalidation as Passed. Fresh live verification ran and failed PhotoshopTestImageRoundTrip
at CloseGuard (PhotoshopUnknownState); six other live checks passed. Admission is CLOSED,
probe cleanup NotRun with the exact probe retained/open, canonical lease Free. A2 is partially
complete / BLOCKED; no retry or out-of-scope repair. See HANDOFF.md for actual application state.
Local raw evidence is under `artifacts/pf-accept-a2/` (ignored; no customer pixels in Git).
The first writer invocation under Windows PowerShell failed parsing UTF-8 preset text before
writing. The unchanged writer succeeded under the installed PowerShell 7 runtime. Both attempts
are preserved separately. No revocation route or hand-edited passing record was used.

At the publication/live boundary, route_offset 0 remained unchanged and actual route UNVERIFIED;
the work stayed in this context. At the acceptance boundary, one isolated read-only native reviewer
was requested with the policy critical-review target gpt-6-astra/high. It independently confirmed
the evidence and the live completion blocker; returned runtime metadata did not verify its actual
model/effort. No second checkout/desktop executor was used. Documentation stayed in the current
context with MODEL_SWITCH_UNAVAILABLE disclosed rather than claiming the Luna Low target ran.
Scoped reassessment is in ASSESSMENT.md. No A3 or broader release acceptance follows.

## Recovery continuation — 16 September 2026

Prompt 19 supersedes the earlier execution-only repair prohibition. Start: clean master
`7be50a7092cbb5dacf1eb651dff132a88f821f34`. The operator confirmed exclusive checkout/desktop
availability and saved work once in this execution. That confirmation remains applicable to
the pending normal-App check; do not ask again unless availability changes.

Read current state before mutation; retain A1/publication; use the original candidate unless a
demonstrated Product correction requires replacement qualification. No historical probe input
has been sent. Current window observations supersede old assertions about what is open, but
are not a complete document census. See RECOVERY.md for evidence and the pending operator click.

Routing: EXECUTE_HANDOFF, policy v2.3, NormalRoute/RequestedRoute Sol High for bounded recovery
diagnosis, RouteOffset 0, AdjustmentResult UNCHANGED, CONTINUE. ActualRoute UNVERIFIED;
MODEL_SWITCH_UNAVAILABLE in this active context, current safe execution retained without
claiming a switch. No UI implementation or independent acceptance review has occurred.

Targeted existing tests passed 88/88 using the installed per-user SDK. No Product/test bytes
changed, so no full suite, candidate rebuild, A1 rerun or publication is warranted at this point.

### Revised execution after the repeated normal failure

The operator's normal check at 05:01:58Z reproduced the same rejecting condition. Complete
Product facts proved the original probe absent before it, while a later getter-only census
proved the new exact probe present as the sole saved active document. Therefore proceed with
the authorized corrected-candidate branch; earlier no-change test limits above are historical.

1. Implement exact runtime identity/reconciliation at the existing Product readiness boundary,
   preserve the real lease and explicit action, and add focused regression tests. Ordinary UI
   explains the last attempt and offers safe recovery/recheck with collapsed support details.
2. One fresh read-only native reviewer audits the stable diff; correct findings and run affected
   focused checks. No second reviewer, no desktop delegation.
3. Clean Release build and one settled-source full suite are justified by shared Product changes.
   Commit only stable local work, then create a new controlled harness/candidate pair. Frozen
   A1 pair and active publication remain untouched.
4. Use existing ReadinessDiagnosticSmoke from the new paired harness for non-authorizing live
   proof: the normal report remains denied for mismatched revalidation; the diagnostic must
   recover the observed owned probe and complete a fresh whole live check under the real lease.
5. Only after repair/QA and live proof stabilize, run one complete unchanged v3 set for the new
   pair. Stop at actual new Operator artwork decisions. Do not import old approvals, repeat
   publication, start A3, install, deploy or push. Observe the actual normal candidate gate and
   report its qualification requirement separately from recovery capability.

Supported Computer Use keyboard navigation was verified through a normal Tab focus change.
This supplies a permitted way to navigate the ordinary app despite the mouse geometry error.
Native screenshots remain unavailable (0x80004002 after one fresh-window retry); source-rendered
WPF evidence is explicitly synthetic and uses the existing test fixture, not an app screenshot.

### Execution outcome and remaining boundary

Backend runtime identity/recovery and ordinary recovery UI were implemented and reviewed by
the same scoped reviewer. Two final-source full suites followed actual shared Product changes:
pair a05 passed11933 but live proof exposed the remaining initial SaveAs transition; pair fa0c
at commit1b948b7 passed11939 and verified complete recovery/fresh round trip live. Raw failed
attempts and both pairs remain frozen; exact chronology is in RECOVERY.md.

The single authorized complete new v3 run used pairfa0c. Its own readiness passed, but the set
FAILED2/7 at a separate Meitu Save-surface exact-foreground guard, with later external cases
blocked. New Operator visual review is not the next step yet. No publication occurred. A final
normal-App action still refused unqualified replacement bytes. Its unexecuted live rows exposed
misleading failure prose, prompting one last bounded display-only correction with targeted tests;
no shared Product logic change or additional full-suite/v3 run is warranted for that wording.

Qualification remains unfulfilled. Further Meitu guarded-recovery work and another full v3 run
must preserve this failed result and all old authority; prompt19 authorized only one new complete
execution. Do not start A3 or treat the successful non-authorizing diagnosis as ordinary A2 PASS.

The display-only correction passed 94/94 screen/localization checks. Final source f27fce1
has its own verified pair 0298e108-5572-47ba-a3af-9bb5592cf6ff. Its ordinary recheck at 18:30
correctly returned failed revalidation, unrun live checks and CLOSED admission with truthful
plain-language messages. The shared runtime source is unchanged from fa0c; no full suite or
v3 run was repeated for this display change. Final identities and state are in HANDOFF.md.
