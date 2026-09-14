# PF-FIX-MEITU-CONFIRM — handoff

## Status

**PARTIAL — correction and paired recovery executable complete; one current live association
confirmation is required before Save/export input.**

No live Meitu input was sent in this task. The retained cutout, original A1 database and prior
evidence remain unchanged. The exact pending question is whether the current Meitu 7.8.8.2 editor
window still shows the accepted `FIX-FINE-HAIR-001` cutout on its checkerboard result screen.

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

## Exact continuation after confirmation

Run only `MeituObservedResultRecoverySmoke` from the retained harness with:

- workspace `D:\PrintFlowStudio`;
- A1 database
  `D:\PrintFlowStudio\TestData\v1\runs\a1-meitu-7882-20260914-143954-d12383c8\regression-run.db`;
- session `01a09dc9-d68f-7f44-8f03-b1bd33cd06d1`;
- working input
  `Sessions/S_20260914T024045Z_33cd06d1/Working/01a09dc9-d787-7e7a-a96e-a9f10557911e/FIX-FINE-HAIR-001.jpg`;
- output on the same attempt-owned directory named `FIX-FINE-HAIR-001_CUTOUT.png`;
- the existing run-local override preset and hash above;
- a new task-owned evidence directory.

The smoke acquires the canonical shared lease before touching Meitu. The live route must stop if
the current phase is not exactly `BackgroundRemovalResult`, if the Save basename is not exactly
`FIX-FINE-HAIR-001_副本`, if any output already occupies the destination, or if decoded validation
fails. It must not substitute another PNG or another visible image.

## Routing

Personal development routing v2.3; explicit RouteOffset `0` (`UNCHANGED`). Normal/requested/
execution route was `gpt-5.6-sol/high`. No in-thread switch metadata was available:
`MODEL_SWITCH_UNAVAILABLE`, ActualRoute `UNVERIFIED`. Work continued in the current safe context;
self-review only because delegation was not authorized.
