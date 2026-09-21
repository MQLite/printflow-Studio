# SCRUM-11130 — Prepare Design Asset fixed-workstation E2E

Date: 10 September 2026

Work item: SCRUM-11130 / CSV Work Item 11706

Verdict: **PARTIAL — the required real operator-facing golden path did not complete**

## 1. Acceptance authority

The complete acceptance criterion from
`C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv` is:

> Execute the confirmed path JPG photo to enhancement to background removal to trimming to
> approved transparent PNG on the fixed workstation. Include rejected review, automatic retry,
> manual takeover, restart recovery, unknown dialog and output-validation failure cases. Verify
> the source remains untouched and the completed approved PNG is bound to the reviewed hash.

This report does not reinterpret that sentence as an automated-test-only requirement. Completion
requires one continuous real Product run through the operator-facing workflow. Supporting contract
and WPF/UIA tests can prove individual failure and recovery behaviours, but cannot substitute for
that run.

## 2. Executive result

The task found and fixed one reproducible Product defect in the live Meitu JPG-to-PNG export route.
Meitu 1.17.0 can retain its signed start-page window beside its editor. During Save, the retained
window becomes disabled because the signed Save surface is application-modal. PrintFlow previously
treated every extra same-process top-level window as unknown, so it refused the format-popup route
before Save or Save As. A new regression test reproduced that exact window shape before the fix.

The driver now freshly reads every extra same-process window and ignores only a sibling that still
matches the complete signed `KnownWelcome` identity. Unreadable, minimised, empty, changed or other
unknown windows continue to stop the route. The disabled bit caused by the already-accounted-for
Save surface is excluded only from the sibling's identity observation; it does not make that
sibling an input target. Existing pre-existing/new unknown-window safeguards remain green.

The final Release suite passed **11,779 / 11,779**, with **0 failed and 0 skipped**. Nevertheless,
the real golden path could not be rerun after the fix. The signed readiness gate repeatedly failed
at `PhotoshopTestImageRoundTrip`: Photoshop first presented its exact known
`Generator encountered a problem` dialog, and on a later attempt retained the positively
identified PrintFlow probe after PrintFlow asked it to close. The Product correctly sent nothing
further. Its built-in Generator log records an Adobe Generator `Unknown JavaScript error`; this is
a workstation Photoshop failure, not a Meitu or PrintFlow workflow result.

No preset, signed evidence, Photoshop setting, revalidation record or unknown dialog was altered or
answered to bypass the gate. Therefore the required completed transparent PNG and its
reviewed-hash binding do not exist, and SCRUM-11130 remains **PARTIAL**.

## 3. Fixed workstation and accepted authority

| Fact | Observed result |
|---|---|
| Workstation | `DESKTOP-0BG8884`, Windows 10 Pro build 19045, one 1920×1080 display at 100% |
| Adapter mode | `Production` |
| Preset | `printflow-workstation-v1` 1.17.0 |
| Manifest SHA-256 | `A2E1936B355C28CDC9905EBF63107A7B4229B71D7586B3C304B7F284B59FCFA9` |
| Evidence integrity | 29/29 signed evidence digests matched |
| Meitu executable | Accepted 7.8.7.5 executable, digest prefix `D65C6D823232` |
| Photoshop executable | `D:\Adobe Photoshop CC 2019\Photoshop.exe`, digest prefix `81EE8930FC1E` |
| Photoshop Action | Canonical `PrintFlow-DTF-v1.atn`, digest prefix `A04203EDEA62` |
| Production revalidation | Absent before and after this task; ordinary Production gate remains closed |
| Raw task evidence | `D:\PrintFlowStudio\Evidence\SCRUM-11130-20260910` |
| Standard-run evidence | `D:\PrintFlowStudio\TestData\v1\runs\scrum-11130-*` |

The verifier's non-blocking read-only advisory remains: 16 of 29 evidence files lack the Windows
read-only attribute, while all 29 SHA-256 values match. The hashes are the accepted integrity
authority. This task did not edit those files or the signed preset.

## 4. Real execution chronology

### 4.1 Pre-fix live execution

`scrum-11130-prereq-20260910`, `scrum-11130-prereq-final-20260910` and the first
`scrum-11130-fixed-workstation-20260910` reached the real portrait Meitu route. The portrait case
failed with:

> The accepted Meitu process already had 1 unrecognised visible top-level window before the
> format combo was opened. The popup route was not entered. Neither Save nor Save As was invoked.

Live Win32 and UI Automation census identified that sibling as Meitu's retained start page:
title `美图秀秀`, class `Qt51517QWindowIcon`, UIA class `StartupWidget`, with the full signed welcome
markers. That made the refusal reproducible as a Product-classification defect rather than an
unknown-window acceptance request.

The first fixed-workstation result was **2 passed / 5 failed**. The transparent-PNG category passed
its deterministic internal trim at 2724×3685 with alpha and SHA-256
`62BFF224A8AB52337F3DA59712A42DB677406B9B390500D39FAFADBBAF0876B9`; the reference production
TIFF also passed. The portrait source and working copy remained byte-identical, SHA-256
`F4CAD2A1EC7994E42E2A77CC6F30E29DE91D344821E4EE7AC01C7E712D9A4634`. No Meitu output was written.

### 4.2 Regression and bounded fix

The regression
`A_retained_recognised_welcome_window_does_not_block_the_JPG_popup_route` was added with the live
disabled-welcome shape. It failed before the final implementation and passes after it. The fix is
confined to `GuardedMeituUiDriver`'s signed export-window classification; it does not modify the
preset or loosen the popup, Save, output-validation or document-identity rules.

Final focused evidence:

| Evidence | Result |
|---|---|
| `guarded-meitu-export-regression-disabled-welcome.trx` | 40 passed, 0 failed, 0 skipped |
| Unknown sibling tests inside the same suite | Existing unknown, unreadable, minimised, empty and changed shapes remain fail-closed |
| `full-suite-after-meitu-fix.trx` | 11,779 passed, 0 failed, 0 skipped |
| Final pinned-SDK Release build | 0 warnings, 0 errors |

Local implementation commit: `fee557e` (`fix: recognise retained Meitu welcome during export`).
Documentation follows in a separate local commit. Nothing was pushed.

### 4.3 Post-fix attempts and the readiness blocker

| Run | Result | Exact stopping class |
|---|---|---|
| `scrum-11130-fixed-workstation-b-20260910` | Blocked, 0/7 executed | `PhotoshopTestImageRoundTrip` failed while Photoshop displayed its exact known Generator problem dialog |
| `scrum-11130-fixed-workstation-c-20260910` | Blocked, 0/7 executed | A clean Photoshop restart had not yet exposed an attachable safe editor; safe-state failed and dependent checks were blocked |
| `scrum-11130-fixed-workstation-d-20260910` | Blocked, 0/7 executed | The Generator problem recurred during the Photoshop round trip |
| `scrum-11130-fixed-workstation-e-20260910` | Blocked, 0/7 executed | The PrintFlow-owned probe remained visible after the requested no-save close |

The last readiness report passed preset/evidence/OS/executable/action/workspace/session/display/UI
culture, global automation lock, Meitu launch/safe state, Photoshop launch/safe state and the four
accepted Photoshop working spaces. Only `PhotoshopTestImageRoundTrip` failed. Its exact guard result
was: *Photoshop still shows the document PrintFlow asked to close. Nothing further was sent; the
document may still be loaded.*

The retained file is a positively identified, 68-byte PrintFlow environment probe below
`D:\PrintFlowStudio\EnvironmentVerification`; all such retained probes observed here hash to
`431CED6916A2A21A156E38701AFE55BBD7F88969FBBFC56D7FE099D47F265460`. They are evidence of the
failed readiness operation, not completed workflow output. The task did not improvise clicks or
delete them while Photoshop still reported one open.

Read-only diagnosis found Adobe's built-in Generator loading `generator-assets` and `crema`, a
warning that Crema's user-settings file could not be created, followed by Photoshop's
`Unknown JavaScript error`. The same error appears repeatedly in Generator exception logs. No
persistent permission, plugin or Photoshop configuration change was authorised, so diagnosis
stopped there.

## 5. Acceptance matrix

| Required case | Evidence actually obtained | Status |
|---|---|---|
| Real JPG → enhancement → background removal → trim → approved transparent PNG | Pre-fix live route reached Meitu and exposed/fixed the retained-welcome defect; post-fix reruns were stopped by Photoshop readiness before any case ran | **NOT COMPLETED** |
| Source remains untouched | Live failed portrait attempt independently retained identical source and working-copy hashes | **PASS for failed attempt only; golden-path proof absent** |
| Approved PNG bound to reviewed hash | No completed real golden path and therefore no approved output authority | **NOT COMPLETED** |
| Rejected review | SessionService retry/review contract evidence in the 53-test variant filter | **HARNESS-LABELLED SUPPORTING PASS** |
| Automatic retry | Fresh attempt/working-copy and clean approved-upstream contract evidence in the 53-test variant filter | **HARNESS-LABELLED SUPPORTING PASS** |
| Manual takeover | Manual-result provenance/validation contracts plus live synthetic WPF/UIA recovery surface | **HARNESS-LABELLED SUPPORTING PASS** |
| Restart recovery | Three real WPF/UIA phases A/B/C, each 1/1, with screenshots, transcripts and independent SQLite/file assertions | **SYNTHETIC LIVE SUPPORTING PASS** |
| Unknown dialog | Real pre-fix retained start page was positively identified, not accepted as unknown; production negative contracts prove unknown states stop without speculative input | **HARNESS-LABELLED SUPPORTING PASS; no fabricated live dialog** |
| Output-validation failure | Missing/unreadable/changing/invalid-alpha failure, persistence and clean-retry contracts in the 53-test filter | **HARNESS-LABELLED SUPPORTING PASS** |

`variant-contracts.trx` passed **53 / 53**. `recovery-live-A.trx`, `recovery-live-B.trx` and
`recovery-live-C.trx` each passed **1 / 1**. Their screenshots and transcripts are retained below
`D:\PrintFlowStudio\Evidence\SCRUM-11130-20260910\recovery-live`. These results establish the
individual contracts but do not turn the missing Production golden path into a pass.

## 6. Safety and non-actions

- No Save or Save As was invoked after the unrecognised Meitu sibling failure.
- No unknown dialog was answered, dismissed or classified by guesswork.
- No operator/customer document was closed or modified.
- No output Revision or review authority was created from either failed Meitu attempt or failed
  readiness probe.
- The global automation lock was released after every bounded run.
- No signed preset, evidence digest, Photoshop working-space setting, Action or Product mode was
  changed.
- No revalidation writer was invoked; `production-revalidation.json` remains absent.
- No Jira service, push, deploy, branch, worktree, rebase or amend operation was performed.

## 7. Review

The implementation, tests, result JSON, readiness reports, raw TRX counters, source hashes and
scope claims were self-reviewed against the complete CSV acceptance criterion. The regression is
specific to the observed live shape and the code remains fail-closed for every sibling that is not
freshly recognised as the signed welcome page.

The requested independent reviewer was unavailable in this execution context. This is
**SELF-REVIEW ONLY / INDEPENDENT REVIEW NOT COMPLETED** and is an additional reason not to claim
FULL acceptance.

## 8. Jira reassessment and next authority boundary

| Item | Reassessed status | Reason |
|---|---|---|
| SCRUM-11130 | **PARTIAL** | Product defect fixed and variants supported, but no post-fix real operator-facing golden path or reviewed-hash binding |
| SCRUM-11065 | **PARTIAL, unchanged** | No seven-category fixed-workstation run passed |
| SCRUM-11123 | **PARTIAL, unchanged** | No passing standard set, no revalidation record and no normal Production-gate acceptance |
| SCRUM-11136 | **PARTIAL / not executed, unchanged** | Repeated success-rate measurement was outside this acceptance and its baseline is not green |
| Parent SCRUM-11124 | **Not closed** | Its broader release gate, other fixed E2Es, Maintop, print and benchmark clauses remain separate |

The next legitimate step is to restore the accepted Photoshop installation so its Generator error
no longer interrupts the signed test-image round trip, then rerun readiness and the real golden
path from a clean state. Changing Photoshop permissions, disabling Generator/Crema, altering the
accepted preset, or manually forcing the probe closed is a new workstation-configuration decision
and was not inferred from this task.

**PARTIAL — PRODUCT DEFECT FIXED AND SUPPORTING VARIANTS PASS; THE REAL OPERATOR-FACING GOLDEN PATH
REMAINS BLOCKED BY PHOTOSHOP READINESS, SO SCRUM-11130 IS NOT FULL.**

---

## 9. Addendum — 21 September 2026: the golden path completed

Recorded under Prompt 24 Revision 3 (PF-ACCEPT-A3). Everything above is the state as of its own
observation and is **unchanged**; this section adds one later observation and does not rewrite any
earlier failure. Full detail, identities and evidence paths are in
`docs/remediation/PF-ACCEPT-A3/HANDOFF.md`, section "PF-ACCEPT-A3 rerun handoff — 21 September 2026".

### What changed

The 18 September blocker was **not** baseline drift. Meitu's Save panel had been left on
保存路径 = 覆盖原图, in which it pre-fills the bare document base name; in 自定义 it supplies
`<basename>_副本`, exactly as the signed baseline
`apps/meitu/editor-with-working-copy.json` (`outputBaseNameSuffix: "_副本"`) records. Restoring
自定义 through Meitu's ordinary UI, verified on a disposable synthetic copy, removed the refusal.
No preset, signed evidence or Product byte was changed.

The §8 next-step sentence about restoring the accepted Photoshop installation still applies, for a
newly observed reason: an unregistered copy of the accepted build at
`C:\ps2019\Adobe Photoshop CC 2019\Photoshop.exe` occupied the single-instance slot and failed
`Photoshop 启动能力`. After the Operator started the accepted `D:\Adobe Photoshop CC 2019` instance,
the ordinary gate passed at 10:45 — 本工作站已通过生产环境校验。

### Acceptance matrix rows that move

| Required case | Evidence actually obtained | Status |
|---|---|---|
| Real JPG → enhancement → background removal → trim → approved transparent PNG | Session `A3-R3-FINE-HAIR-20260921` (`01a0c106-1035-77b2-98ae-432229bec71b`), one continuous ordinary run, five attempts all SUCCEEDED, zero retries, `State = COMPLETED`; deliverable `Approved/A3-R3-FINE-HAIR-20260921.png`, `8D94D320…80C1`, 1200 × 1600 `Format32bppArgb` with sampled real transparency | **PASS** |
| Source remains untouched | `FIX-FINE-HAIR-001.jpg` `5A705FE3…8D8E` byte-identical before and after the completed run; the session's own Source snapshot hashes identically | **PASS** |
| Approved PNG bound to reviewed hash | Three APPROVED `ReviewDecision` rows for this session's own revisions (`98F0136C…9F6C`, `A52512A5…10DC`, `8D94D320…80C1`), and the `PROMOTE_APPROVED` revision carries the identical SHA-256 of the reviewed Trim revision | **PASS** |

Every other row in §5 is **unchanged**. `variant-contracts.trx` 53 / 53 and the three
`recovery-live-*.trx` 1 / 1 results were not rerun and are not re-claimed.

Note on the trim row: this fixture's cutout reaches all four edges, so `TIGHT_CROP` detected content
bounds `0, 0, 1200, 1600` and applied the same, keeping the full canvas. That is a permitted outcome
for these alpha bounds, not a trim defect — but it means this run does **not** demonstrate a
non-trivial crop. A fixture whose subject does not touch the frame would be needed for that, and
that is separate, separately authorised work.

### Reassessment

| Item | Reassessed status | Reason |
|---|---|---|
| SCRUM-11130 | **PARTIAL** | The operator-facing golden path and its reviewed-hash binding now have real evidence, but the variant cases still rest on harness-labelled and synthetic-live supporting evidence, and independent review is still not completed |

**SELF-REVIEW ONLY / INDEPENDENT REVIEW NOT COMPLETED** continues to apply and remains an
independent reason not to claim FULL.

Not performed under this addendum: no build, no suite run, no qualification, no revalidation
publication or revocation, no preset or signed-evidence edit, no install, deploy, push or Jira
transition. Prompt 25 was not executed.

**PARTIAL — THE REAL OPERATOR-FACING GOLDEN PATH NOW PASSES WITH A REVIEWED-HASH-BOUND APPROVED PNG;
THE VARIANT CASES AND INDEPENDENT REVIEW KEEP SCRUM-11130 SHORT OF FULL.**

---

## 10. Addendum — 21 September 2026: evidence closure and independent review

Recorded under Prompt 26. Sections 1–9 are the state as of their own observations and are
**unchanged**; this section adds the independent review, one corrected provenance claim, one
repaired evidence gap and one supplementary execution. It does not rewrite any earlier failure and
it does not re-record the 21 September approvals. Full detail and the nine-column clause matrix are
in `docs/remediation/PF-ACCEPT-A3/HANDOFF.md`, section "PF-ACCEPT-A3 closure handoff —
21 September 2026".

### What changed

**The golden path was independently reviewed, not rerun.** A scoped read-only reviewer in an
isolated context verified, from the underlying database record rather than from this report, that
session `01a0c106-1035-77b2-98ae-432229bec71b` is one completed `PREPARE_ASSET` session with five
SUCCEEDED attempts, zero retries and `State = COMPLETED`; that its three APPROVED `ReviewDecision`
rows belong to this session's own revisions, with ids falling inside the session's own time window;
and that the `PROMOTE_APPROVED` revision carries the identical SHA-256 as the reviewed Trim
revision. The deliverable, the three intermediate revisions and the source were independently
re-hashed and all matched, including `trimmed.png` and `Approved/A3-R3-FINE-HAIR-20260921.png` being
byte-identical at `8D94D320…80C1`.

**One provenance claim is corrected.** The 2026-09-17 full suite (11,965 / 0 / 0) was run from build
pair `393c45f8` (`SourceRevision b2cb93b…`), **not** from the accepted candidate pair `d915b1a6`
(`SourceRevision 6818757…`). Applicability to the candidate is established instead by an exact
comparison of the two pairs' manifests: 614 inputs each, differing in exactly one file
(`A2MeituCutoutValidationRecoverySmoke.cs`, an opt-in live smoke no row of the matrix cites). Every
`src/` file and every variant test file is byte-identical.

**The §5 unknown-dialog row was an evidence-staging failure, not an evidence absence.** Its
supporting TRX, `guarded-meitu-export-regression-disabled-welcome.trx` (40 / 40), exists and its
tests all appear as `Passed` in the retained suite. Read at assertion level, they establish that an
unknown popup, a wrong-class popup, a foreign-process popup, a popup replaced between recognition
and input, a mid-popup foreground change, and a missing/disabled/wrong-process item each end with
zero clicks and zero Save As invocations — and that the 温馨提示「当前图片已修改，是否保存？」prompt,
built with the correct surface class, is still not dismissed.

**"Automatic retry" was resolved against the design rather than reinterpreted.** `自动重试` in
`PRINTFLOW_STUDIO_MVP_DESIGN.md` §20 means the retry of an **automated step**: §7.2 makes retry a
state transition, invariant 8 constrains only how a retry starts, and the metrics list records
`自动化重试率` beside `人工接管率`. No automatic re-invocation policy exists anywhere in `src/`. An
earlier reading of `IMeituUiDriver.cs:321` as a product-wide prohibition was an overstatement: that
comment documents the cancel control under Epic 11300 Part D2A §9, whose own text says "Retry is
available through the ordinary workflow".

**One supplementary check was executed.** The opt-in synthetic-live recovery surface
(`RecoverySurfaceLiveSmoke`, phases A/B/C) was re-run non-building from the accepted candidate
pair's own retained `PrintFlow.Tests.dll`, 1/1 passed each, with fresh sessions, temp storage and
the canonical lease store hash unchanged before and after. It is the first variant evidence pinned
to the accepted candidate pair. It remains synthetic-live: the WPF window is the test's own and no
external application is involved.

### Acceptance matrix rows that move

| Required case | Evidence status after this review | Status |
|---|---|---|
| Rejected review | Harness contracts, applicable to the candidate by manifest file identity; assertions read at source level | **HARNESS/CONTRACT SUPPORTING PASS** (unchanged tier, now verified) |
| Automatic retry | Harness contracts; clause resolved as the retry of an automated step, which those contracts prove — fresh attempt, fresh working copy, auditable failed attempt | **HARNESS/CONTRACT SUPPORTING PASS** (clause reading now settled) |
| Manual takeover | Harness contracts **plus** candidate-pinned synthetic-live phase B | **HARNESS + SYNTHETIC-LIVE SUPPORTING PASS** (provenance upgraded) |
| Restart recovery | 13 harness cases **plus** candidate-pinned synthetic-live phases A and C | **HARNESS + SYNTHETIC-LIVE SUPPORTING PASS** (provenance upgraded) |
| Unknown dialog | 40 focused `GuardedMeituExportTests` (41 in the retained suite) plus ~37 unit classifier rows, all fail-closed | **HARNESS/CONTRACT SUPPORTING PASS** (upgraded from unreadable evidence) |
| Output-validation failure | Harness contracts plus 27 `MeituOutputValidationTests` rows; missing / unreadable / changing / invalid-alpha all covered | **HARNESS/CONTRACT SUPPORTING PASS** (the "changing" sub-claim is now evidenced) |

The three §9 golden-path rows are unchanged and are not re-claimed. No test counts from different
runs are summed.

### What still keeps SCRUM-11130 short of FULL

1. **Not one variant clause is an ordinary production observation on the fixed workstation.** Four
   rest on harness/contract evidence and two add candidate-pinned synthetic-live evidence. A harness
   test stays harness evidence and synthetic-live stays synthetic-live.
2. **"Source remains untouched" is not provable from the Product record.** `InputSnapshot` stores
   the original path and import time but no hash of the original file; the record hashes only the
   session's own copy. The clause rests on file-level measurement outside the Product.
3. **Independent *re-execution* has not occurred.** The review was read-only: it could not execute,
   build or hash anything.

Accordingly the status line for work from this task onward is **INDEPENDENT READ-ONLY RECORD REVIEW
COMPLETED; INDEPENDENT RE-EXECUTION NOT PERFORMED.** The historical
`SELF-REVIEW ONLY / INDEPENDENT REVIEW NOT COMPLETED` banners in §7 and §9 stay as they were
written for their own observations.

### Reassessment

| Item | Reassessed status | Reason |
|---|---|---|
| SCRUM-11130 | **PARTIAL** | Golden path and reviewed-hash binding independently reviewed and confirmed; all six variant clauses now supported but none at production-observation level; source-untouched unprovable from the record; independent re-execution not performed |

Not performed under this addendum: no build, no suite run, no standard-set replay, no qualification,
no revalidation publication or revocation, no preset or signed-evidence edit, no Product or test
source change, no install, deploy, push or Jira transition. Prompt 24 was not rerun and Prompt 25
was not executed.

**PARTIAL — THE GOLDEN PATH IS INDEPENDENTLY REVIEWED AND HOLDS; ALL SIX VARIANT CLAUSES ARE NOW
SUPPORTED BUT NONE AT PRODUCTION-OBSERVATION LEVEL, SO SCRUM-11130 IS NOT FULL. NOTHING FOUND HERE
BLOCKS SCRUM-11131.**
