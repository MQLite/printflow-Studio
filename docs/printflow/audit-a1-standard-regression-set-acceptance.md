# PF-ACCEPT-A1 — Standard set acceptance

**RUN INCOMPLETE — PF-ACCEPT-A1: standard set Failed under the authorized Meitu 7.8.8.2 exception**

## 14 September 2026 — actual execution after the requested runner change

The user explicitly authorized changing the runner to continue on 7.8.8.2. Commit
920304fce09cb5b1d84b3afef5c6393cbbecf7d2 adds -MeituExecutablePath to the supported wrapper and
test harness. It records a run-local derived preset and actual executable identity, leaving the
accepted preset and src/ code unchanged. Other guards remain. The binding permanently records the
version exception in CandidateProblems, so even a Passed set would not authorize publication.
This is not acceptance of the original frozen 7.8.7.5 workstation. No result was hand-edited.

The new harness requires a fresh controlled pair. Both builds succeeded, harness build 0 warnings /
0 errors; VerifyOnly passed. The original ef6182db pair remains intact and reverified afterwards.
No full suite; focused tests passed 66/0/0. The actual old-pair override command refused before host
execution with exit 2. This historical refusal and its log remain preserved.

| Identity | Actual value |
|---|---|
| New pair | b5f2e3b8-d47a-4ece-b914-9260f5eb9fc1 |
| Source | 920304fce09cb5b1d84b3afef5c6393cbbecf7d2 |
| Pair root | D:\Repositories\printflow-Studio\artifacts\pf-accept-a1\build-pairs\b5f2e3b8-d47a-4ece-b914-9260f5eb9fc1 |
| Receipt | pair root\build-pair.json |
| Receipt SHA-256 | 189A0E5C0B1FF12ED3AB565FB80F912075E3B762DD0F3833813CDDADCDC813DF |
| Test DLL SHA-256 | F39FE3BFBB48056B02125D316073C46EB6130EB955B224809BD53856856CFAC7 |
| Harness / candidate | pair root\harness / pair root\candidate |
| SDK / dotnet | 10.0.400 / C:\Users\admin\AppData\Local\Microsoft\dotnet\dotnet.exe |
| RunId | a1-meitu-7882-20260914-143954-d12383c8 |
| InvocationId | 0e65070b-cf80-464e-aeb3-b6321ae25f41 |
| Run root | D:\PrintFlowStudio\TestData\v1\runs\a1-meitu-7882-20260914-143954-d12383c8 |
| Result SHA-256 | F614FB7904F6C182A784E140FDFA6E1E90C2F52B7D4FD949F117F1F1E358D047 |
| Binding version / set | 2 / printflow-regression-v1 |
| Set content digest | 4DC88A55002AAA01D5DC6089433E77DCED773EC43AD7B88985E17E4E451A1DA1 |
| Derived preset SHA-256 | 5B4B2B00C0929D8047C71C93D0BA93F5A2A3AE7191B32E37BE0EDDBBC9F10F56 |
| Meitu SHA-256 | 9276B407F855A02F65B04FF0EBEAC1E778F6C8F24D25413A1C2D2853A3F96B0B |

Actual command arguments and wrapper times are in artifacts/pf-accept-a1/live-command.json; full
log is live-run.log. Wrapper window 14:39:54–14:41:08 NZST. Host completed successfully, one Fact
passed (exit 0); wrapper exit **1**, result **Failed**. The Fact's successful completion means the
runner produced its result, not that the cases passed. Exactly seven categories appear once.
The wrapper's paired vstest route performed no build/restore. It names host-results under the set
root for this InvocationId but enables only a console logger; no TRX file was produced.

| Category | Recorded outcome | Actual interpretation |
|---|---|---|
| NORMAL_JPG_PORTRAIT | Failed | Full enhancement/export path produced PNG; enhancedOutputIsLargerThanSource is false: 1200x1600 output from 1200x1600 source. Visual decision remains Pending. |
| COMPLEX_BACKGROUND_FINE_HAIR | Failed | Background Removal reached an unrecognized Meitu editor state; no navigation/export/further input at that boundary. caseCompleted false, no exported artifact. Empty StepsExecuted reflects exception serialization, not proof that no UI operation started. |
| TRANSPARENT_PNG | Passed | Deterministic internal trim produced expected 2724x3685. |
| COMPLETE_CUSTOMER_DESIGN | Failed | Production request refused after Meitu was observed computing; readiness lost. No TIFF produced. |
| PSD_WITH_COMPOSITE_PREVIEW | Failed | Production request refused after live-readiness invalidation. No output produced. |
| SINGLE_PAGE_PDF | Failed | Production request refused after live-readiness invalidation. No output produced. |
| REFERENCE_PRODUCTION_TIFF | Passed | Existing reference intact, structure checked, TIFF Home import refused; no physical print. |

The last three failures are prerequisite cascades, not three executed TIFF-output failures. This
is not an operator-review-only Pending run. No blind retry, manual completion or further guard change.

## Startup, probe and release

The user's current confirmation and later instructions authorized this window. Both minimized app
windows were restored through Computer Use, with no typing or settings edits. Meitu UIA showed its
clean StartupWidget page. Computer Use screenshots failed on this Windows host (SetIsBorderRequired,
0x80004002); Photoshop accessibility was unavailable, so a complete visual pre-run capture is absent.
The runner's own fresh readiness explicitly confirmed Photoshop KnownStartScreen, no document open.

Actual startup: **Attached**, Meitu PID 21884 (7.8.8.2, start 14:22:42 NZST) and Photoshop PID 25696
(start 11:46:41 NZST). No fresh launch occurred. All initial blocking readiness checks passed against
the derived snapshot. The existing 16/29 read-only-attribute advisory remained, with all evidence
hashes matched. No claim that the 7.8.7.5 baseline passed: the readiness prose "accepted baseline"
refers here to the explicitly marked derived snapshot. Likewise portrait ExternalApplications still
carries the manifest's expected 7.8.7.5 label; actual executed identity is established by the override,
binding hash and readiness process path. That inherited label is not a measured version observation.
Post-run evidence review identified that the emitted label is hard-coded in two runner case returns.
Those two descriptive strings were corrected to "Meitu XiuXiu" for future runs; exact identity stays
in the binding/readiness. The retained 920304f harness and raw result remain unchanged. This string-only
correction was not rerun against the desktop or rebuilt into the retained pair. Any future execution
of the corrected source needs its own new controlled pair; do not claim it was used by this run.

Probe 3b6d673f4de44913b6b76c98d0fd926c completed every stage through CleanupCompleted;
last requested stage CleanupAttempted, cleanup Succeeded, no primary/secondary failures. The live
lock row passed (its release-failure path would replace that result). Post-run passive canonical
manager ObserveAsync(null) at 14:42:35 NZST reported Free for
printflow-studio.external-automation.v1, database
C:\Users\admin\AppData\Local\PrintFlow Studio\workstation-automation-v1.db. This is separate from
per-case business-lock assertions. No owner row was edited and no external lease was held while
waiting for review. Later case readiness invalidation is retained, not overwritten by initial success.

Post-run Meitu exposed both welcome and image-editor windows. Passive editor UIA showed cutout
controls (automatic selection / partial cutout / manual repair); it was left untouched, not claimed
clean. Process identities remained the same. No process-tree cleanup or Adobe/IME/tool repair.
Later, the user instructed dismissing unpredictable "我知道了" notices. A fresh full UIA tree exposed
an AI-assistant novice guide, MainWindow.NoviceGuideWidget.MessageGuideWidget, title "AI助手来啦",
with an okButton labelled "我知道了". Computer Use attempted that exact control but returned
"coordinate input geometry is unavailable"; no successful dismissal is claimed. Observations are
retained under artifacts/pf-accept-a1/meitu-notice-*. This is a possible contributor to the unknown
state, not established causation. The completed failed run remains immutable; no retry occurred.

## Artifacts and exact unresolved Operator check

All produced-artifact hashes, original input/manifest hashes, RunId/InvocationId and BuildOrigin
were rechecked. Both pairs remain retained. Original preset hash A2E1936B355C28CDC9905EBF63107A7B4229B71D7586B3C304B7F284B59FCFA9
and appsettings hash 059E73E413A5EC7D1975CD87B61F540FA7F8F1DF2B0618C1FCD7F441D7B6B0F5 remain unchanged.
Machine-readable verification is artifacts/pf-accept-a1/post-run-verification.json.

PORTRAIT-VISUAL-001, NORMAL_JPG_PORTRAIT, this exact RunId:
run root\FIX-PORTRAIT-001-enhanced.png, SHA-256
6348E70A06441930520006EA3753254664D69B43218CB7D84D75B94BD81FB776.
Exact question: **Does the enhanced export still look like a correctly enhanced portrait — subject
sharp, skin tone unshifted, no visible artefact introduced along the hair or shoulder edges?**
Passed assertions: enhancementReachedReview, enhancedOutputIsPng, promotedRevisionExists,
sourceBytesUnchanged, automationLockFree. The size assertion failed independently. Unsubmitted
checklist: artifacts/pf-accept-a1/operator-review.md. No reviewer or decision is prefilled.
No reviewable fine-hair/customer-design artifact exists for this run. No human decision was recorded.

Transparent output: run root\FIX-TRANSPARENT-001-trimmed.png, SHA-256
62BFF224A8AB52337F3DA59712A42DB677406B9B390500D39FAFADBBAF0876B9.
Reference TIFF retains D1E69C4108D4C1D6119DB11DE036F56555CDE4A064F23AF541E24E1DAC5EA412.

## Review and stop boundary

Independent reviewer found no actionable runner-change defect. Process deviation: that reviewer
invoked a skill which spawned two read-only child reviews despite the A1 cap of one reviewer.
One returned a line-ending note; the other was interrupted. No child made edits or operated the
workstation. Line endings were normalized; no additional reviewer was started for the run.
Policy v2.3, offset -1; requested reviewer Sol Medium, actual metadata UNVERIFIED; parent
MODEL_SWITCH_UNAVAILABLE, CONTINUE. This is not independent release approval.
The same reviewer checked the actual run: identities, hashes, case completeness and outcome were
coherent; it identified the stale label described and corrected above. No evidence JSON was edited.

SCRUM-11065 stays PARTIAL; 11123/A2 and 11130/A3 unchanged. Real production revalidation NOT WRITTEN;
normal-App E2E NOT EXECUTED; R4 cold-launch finding unchanged. No preset/assets rebaseline, full
suite, Jira change, install/deploy/push or attribution trailer. Operator bundle and historical
failures/probes retained. Stop after reporting this failed exception run.

## Earlier preparation snapshots (historical)

Latest update: user supplied the current window confirmation ("Please continue. Meitu open and ps
ready."). Passive inventory found version 7.8.8.2, PID 21884, at its 7.8.8.2 executable path.
The accepted preset requires 7.8.7.5; its executable exists but is not running. Stopped before
operational setup to preserve attach-start scope. No lease acquired, no RunId claimed, no host or
case executed. This is a pre-run prerequisite mismatch, not seven automated output failures.
Original pair reverified successfully. No Operator result decisions were supplied. See PLAN's
latest checkpoint and local artifacts/pf-accept-a1/meitu-identity-mismatch.json.

The preparation snapshot below predates that confirmation; its no-run facts remain current.

14 September 2026, DESKTOP-0BG8884. Selected freeze and verified identities are recorded in
../remediation/PF-ACCEPT-A1/PLAN.md. Pair ef6182db-6f48-4ed2-ae8f-c4e52307ca9e remains intact;
paired source ce29954, actual starting HEAD a2cee64872deddec17213298b58edd433079e3a7, master.
A0 origin condition is CLOSED. No source or build changes were needed.

The supported VerifyOnly entry succeeded and the receipt hash matched the supplied identity.
Static preflight exit 0 confirms all seven categories and matching input hashes. Evidence remains
local under artifacts/pf-accept-a1/. These checks operated no application or workstation lease.

The original CSV requirement and current runbook support operator-prestarted, settled apps with
attach at entry. The unchanged processors attach to one accepted running instance. They can launch
if absent; no launch check has been disabled and no AttachOnly flag exists. Actual startup mode has
not been observed because the live window has not been confirmed in this execution.

| Layer | Current fact |
|---|---|
| Acceptance freeze | Selected retained pair, verified; no rebuild |
| Static preflight | Passed, exit 0 |
| Standard-set wrapper / test-host exit | NOT RUN / NOT RUN |
| RunId / InvocationId / result / TRX | None created for live execution |
| Seven automated cases | All NOT RUN; no output failure inferred |
| Operator review | No decisions supplied or recorded; no artifacts yet to review |
| Live readiness / probe / release | NOT RUN; no lease state inferred |
| Standard-set acceptance | Not earned; SCRUM-11065 remains PARTIAL |
| Production permission | Not granted; real revalidation NOT WRITTEN |

R4 fresh-launch MK_E_UNAVAILABLE finding remains unreproduced/unrepaired. Historical R4 PIDs and
availability are not current observations. Old wrapper, probes, failures and operator prompt bundle
were untouched. Normal-App E2E NOT EXECUTED; 11123/A2 and 11130/A3 remain separate. No Jira changes.
No full-suite rerun or inherited test counts presented as new evidence. Self-review only at this
preparation checkpoint; routing limitations and exact continuation procedure are in PLAN/HANDOFF.
