# PF-FIX-MEITU-CONFIRM — handoff

## Status

**COMPLETE FOR CORRECTION, REDO, EXPORT AND REGISTRATION — fresh cutout quality review remains
intentionally open.**

## 15 September redo completion

The reboot made the previously accepted live result unrecoverable, so the operator's request to
redo this part was treated as authority for one new `FIX-FINE-HAIR-001` Background Removal run,
not as approval of its future output. The new run is now exported and registered. Its Revision is
`ReviewRequired`/`NotReviewed`; the earlier acceptance was not transferred.

The first paired redo exposed a real post-open target defect: Meitu replaced editor handle
`0x61F76` after accepting the exact Working-copy path, while `OpenWorkingCopyAsync` returned that
destroyed handle to `ObserveLoadedDocumentAsync`. Commit
`7de898b107ec74c3f24c507b7cae498d4738930a` now waits read-only for exactly one signed
same-process successor document or Busy surface and refuses zero/wrong-owner/ambiguous candidates.

The retained exact document was then resumed without another Open. Background Removal executed
and returned a visible cutout to the ordinary editor, but its terminal identity confirmation hit
one markerless UIA repaint and reported failure even though the finished effect was visible.
The preserved before/result captures are:

- `D:\PrintFlowStudio\QA\PF-FIX-MEITU-CONFIRM\redo-20260915-110930\evidence\20260914T230940Z_open-unsettled_61F76.png`
  (`A3945859AE76E4322DC1E2E9CAE4F8D347559FB48FBE82D1B6D301C1265AA0E9`), showing the
  original green background;
- `D:\PrintFlowStudio\QA\PF-FIX-MEITU-CONFIRM\redo-20260915-110930\loaded-resume-evidence\20260914T233151Z_loaded-redo-action-refused_61F76.png`
  (`6BD366125978B7743B4F86E97BCFBD2A20FD456A204EEDEC1A08A4BBA91C0C4B`), showing the same
  object cut out on checkerboard in the ordinary editor.

Commit `ac3e27f5d0069c8f65275e80680f1c908b6c746e` makes the identity probe wait out only that
markerless repaint while the exact accepted editor title remains enabled and unblocked. An
unaccepted title, owned dialog, disabled surface or ambiguous result still returns immediately
for refusal. The recovery then invoked no processing: it confirmed exact Save-default identity
`FIX-FINE-HAIR-001_副本`, used the guarded Save/另存为 route once, waited for stable bytes,
decoded and validated the PNG, imported the exact hash, dismissed the signed result surface and
closed the correlated document.

Final result:

- session: `01a0a22e-e1a5-77c8-bb67-19543bfc0a33`;
- preserved failed staging attempt: `01a0a22e-e3e7-7f59-b2cf-75e2737d3e37`;
- manual-result Revision: `01a0a248-d7eb-7d20-ad5d-389610b1287a`;
- output: `D:\PrintFlowStudio\QA\PF-FIX-MEITU-CONFIRM\redo-20260915-110930\workspace\Sessions\S_20260914T230936Z_3bfc0a33\Working\01a0a22e-e3e7-7f59-b2cf-75e2737d3e37\FIX-FINE-HAIR-001-REDO_CUTOUT.png`;
- output SHA-256: `731BE2E042A3FA47EFDD21254FA8F774E97F39E046BFFCE73BF63DEA0918DB19`;
- decoded size: 1200×1600, 1,430,946 bytes;
- alpha: 764,826 transparent pixels and 1,545,347 visible pixels;
- unchanged source SHA-256:
  `5A705FE390AF87D1D48A0554D4908C425D4703A8807CA78EC73AC0E55E3C8D8E`;
- review state: `NotReviewed`; workflow state: `ReviewRequired`;
- cleanup: `KnownEditorEmpty`; business lock free; canonical lease released and reacquired as an
  independent release check;
- receipt:
  `D:\PrintFlowStudio\QA\PF-FIX-MEITU-CONFIRM\redo-20260915-110930\loaded-redo-receipt.json`.

Final source-bound pair `82f8d5fc-9318-473d-a72a-3d551902a523` was built from
`ac3e27f5d0069c8f65275e80680f1c908b6c746e` with SDK 10.0.400, zero warnings/errors and
receipt SHA-256 `2B61DC4C65B08D1D7F292C2DA2FF570C7A4005F4EBDCDFBE3C30E6481B334220`.
The focused transient regression was red before the fix and green after it; the affected Meitu
set passed 79/79 and the full Automation integration scope passed 473/473. No full suite or
seven-category regression was run because the final diff stayed inside the Meitu open/identity
and task-scoped recovery seams.

## Pre-redo recovery history

The following preserved history describes the state after the reboot and before the operator
authorized the new redo; it is not the current completion status above.

Before the redo was authorized, no live Meitu input had been sent. The retained cutout, original A1 database and prior
evidence remain unchanged. The operator confirmed that the pre-restart Meitu 7.8.8.2 editor was
showing the accepted `FIX-FINE-HAIR-001` cutout on its checkerboard result screen, but also reported
that the computer was restarted before this task's export request was seen.

Read-only follow-up on 15 September confirms the machine booted at 10:27 NZST and the new Meitu
process started at 10:34 on the clean welcome page. The earlier controlled cutout output was absent. Meitu's
cache retains only a 60×80 opaque thumbnail of the exact cutout
(`thumbnailtNWzrR.png`, SHA-256
`630ABE82809A4D5D97DF576168F1999FB1560DABAD5D2B06ADA839CF2BDA5FBA`); its cache database records
that thumbnail as 60×60 metadata created at 14:40:44 on 14 September, and no other Meitu file was
written in the corresponding 14:39–14:45 interval. The thumbnail is not the 1200×1600 transparent
result and is therefore ineligible for export, import or registration.

The A1 database remains unchanged: the session is Active at BackgroundRemoval, the original step
is Failed with one attempt, and there are zero ManualResultImport Revisions and zero
BackgroundRemoval review decisions. Both the A1 business lock and the canonical workstation lease
row are free.

## Diagnosis and correction

The previous report attached
`D:\PrintFlowStudio\Evidence\20260914T043542Z_open-unconfirmed_131090.png` to the v2 portrait
Enhancement failure, but the pixels show the distinctive `FIX-FINE-HAIR-001` object on Meitu's
completed Background Removal screen. The A1 captures show the same image first in signed cutout
Busy states and the A1 workspace retains the byte-identical working copy. The relevant execution
context is therefore A1 session `01a09dc9-d68f-7f44-8f03-b1bd33cd06d1`, failed attempt
`01a09dc9-d787-7e7a-a96e-a9f10557911e`, not the later v2 session that never created an attempt.

The actual failure caller was `ProductionMeituProcessor.OpenWorkingCopyAsync` invoking
`GuardedMeituUiDriver.ConfirmWorkingCopyIdentityAsync`. At that site, Save is a non-writing
identity probe: open the signed Save surface, read its default basename, and cancel it. The probe
was refused before invocation because it required generic loaded-editor markers. Export
reacquisition repeated the same classifier, so correcting only the first call would have moved the
same mismatch one boundary later.

Commit `928f6afbd211848280b2df73fc498df75a2b6b65` corrects both boundaries:

- identity probing and editor reacquisition admit the ordinary loaded editor, exact signed
  Enhancement-result, or exact signed BackgroundRemoval-result phase;
- ambiguous result signatures, owned dialogs, disabled/wrong windows and unknown screens remain
  refused;
- exact Save-default identity is still mandatory, and a wrong identity is canceled and refused;
- the opt-in observed-result recovery seam selects exactly one requested result phase on exactly
  one accepted already-running process, never opens a document or invokes processing, and reuses
  the existing guarded export, settling, decoded PNG, same-canvas, real-transparency and unchanged
  input checks;
- the task-specific recovery smoke will preserve the original failed attempt, hand off the A1
  session, import the exact exported PNG as a new `ManualResultImport` attempt/Revision, approve
  only that exact cutout hash under the supplied operator decision, dismiss the signed export
  result surface, close the correlated document, and verify readiness. It does not approve the
  Enhancement result.

## Verification completed before live input

- Red regression: the signed cutout-result screen failed before Save with the original
  “does not match the signed loaded-document structure” message.
- Focused phase/identity/export set: 110 passed, 0 failed, 0 skipped.
- Affected Automation + manual-result import set: 909 passed, 0 failed, 0 skipped.
- Fresh Release harness/candidate pair: 0 warnings, 0 errors.
- Pair ID: `d951deb3-f856-4f11-a47c-5b0fe833e065`.
- Pair source: `928f6afbd211848280b2df73fc498df75a2b6b65`.
- Pair input digest: `2C2659793DB7C63E8FB3742608C08F49E7760833C5B41C53AE44294CD6E53AC4`.
- Receipt SHA-256: `B51219155BEB2E333DBFBCEF3D4CB8F6163750B172FBA6C7096F73CFDC08349A`.
- Derived Meitu 7.8.8.2 preset SHA-256:
  `5B4B2B00C0929D8047C71C93D0BA93F5A2A3AE7191B32E37BE0EDDBBC9F10F56`.
- Meitu PID 16016 remains alive with title `美图秀秀-图片编辑`.
- The A1 business-database AutomationLock is free. The canonical workstation lease was not
  acquired because no live input was yet authorized.

The full suite was not run: the changed scope is the Meitu identity/result/export seam plus the
existing manual-import boundary, and the 909-test affected set and fresh paired Release build cover
that scope. No seven-category run, notice investigation, A2/A3, install, deploy, publish, push,
preset acceptance, `CandidateProblems` change or Production revalidation occurred.

## Exact remaining confirmation gap

Only the fresh redo's visual quality remains for the operator to accept or reject. No approval was
manufactured from the pre-restart decision. Review exact Revision
`01a0a248-d7eb-7d20-ad5d-389610b1287a` / SHA-256
`731BE2E042A3FA47EFDD21254FA8F774E97F39E046BFFCE73BF63DEA0918DB19`.
No further Meitu processing or export is required for that decision.

## Routing

Personal development routing v2.3; explicit RouteOffset `0` (`UNCHANGED`). Normal/requested/
execution route was `gpt-5.6-sol/high`. No in-thread switch metadata was available:
`MODEL_SWITCH_UNAVAILABLE`, ActualRoute `UNVERIFIED`. Work continued in the current safe context;
self-review only because delegation was not authorized.
