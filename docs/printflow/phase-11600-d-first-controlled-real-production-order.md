# EPIC 11600 — Part D: First Controlled Real Production Order

## Preflight

- Date: 2026-09-03 (Pacific/Auckland).
- Safe order identifier: `PF-11600D-20260903-01`.
- Selected order: one square RGB PNG, one design, one print TIFF, no Meitu work, no restoration, and no bulk variants.
- Intended path recorded before Production execution: Import → Original Confirmation → Print Size → Photoshop Production Output → ReviewRequired → operator visual review → explicit approval.
- `Adapters.Mode = Production`.
- Preset: `printflow-workstation-v1 1.16.0`, expected SHA-256 `6396FB4EB87F69C6789304CE191453654B2B75E82A5A9AB0161F90556A6F1A80`.
- Immediate `VerifiedEnvironmentGateWorkstationSmoke`: 2/2 passed; 28/28 integrity entries; Production Readiness `Ready`; gate(Production) `ALLOWED`; no blocker. The 16 read-only file-attribute observations and UI language remained advisory only.
- Immediate Photoshop read-only preflight: accepted Photoshop CC 2019 process PID 29480, exact accepted executable, `KnownStartScreen`, no operator document, no blocking dialog, no PrintFlow Working document, reused rather than launched.
- Meitu was not required and was not run.
- Non-destructive fallback: stop PrintFlow, retain the original, and allow the operator to use the established manual shop workflow outside this failed PrintFlow attempt if the order still needs completion.

## Customer source

- Source filename: `ChatGPT Image Sep 2, 2026, 03_41_18 PM.png`.
- Source format and dimensions: PNG, RGB, 1254 × 1254 px.
- Byte length: 1,665,601.
- SHA-256 before import: `3A81C2598A989C4C5A9BC9130CC3564D5FA32958CD5B9A4CE83154AE3475D96F`.
- SHA-256 after the failed attempt and engineering investigation: `3A81C2598A989C4C5A9BC9130CC3564D5FA32958CD5B9A4CE83154AE3475D96F`.
- PrintFlow's Source snapshot and attempt-scoped Working copy have the same byte length and SHA-256 as the selected original. The original was not renamed, moved, overwritten, or deleted.
- Customer artwork was not copied into Git or embedded in this report.

## Workflow

- Session id: `01a0651e-8125-7ff2-aed9-e76843e8af85`.
- Relative workspace: `Sessions/S_20260903T023453Z_43e8af85`.
- Workflow: `GENERATE_PRINT_TIFF`.
- Import attempt `01a0651e-8125-7b5a-be86-43601434c7ae` succeeded through `internal-import-v1` and created the immutable source Revision.
- The operator explicitly approved the displayed original before confirming the W1 branch.
- Print sizing was custom width 80 mm, proportional, 300 PPI; the authoritative target projection was 945 × 945 px.
- W1 branch: `W1_1PX`.
- Photoshop attempt `01a06530-f227-710e-ad3c-4677458a2b53` ran once through `photoshop-cc2019-production-v1` and failed. `RetrySequence = 0`; no retry or hand-off was used.
- Visible failure: `OutputValidationFailed`.
- Persisted technical detail: `W1 is missing, is not a spot channel, or contains no non-white content. No corrective conversion, retry, save or later operation was attempted.`
- Operator observation at the Action boundary: the imported flattened image was still a locked Photoshop Background layer, so the Action's selection-dependent work failed.
- No manual input was used to rescue or finish the automated attempt.

## Outputs

- No Production TIFF was created.
- No `PrintOutput` row was created.
- No Photoshop output Revision was created.
- The attempt directory contains only the unchanged Working PNG; no validated output exists.
- TIFF settle/inspection did not run because the W1 precondition defect prevented a candidate TIFF from being produced.

## Human review

- `ReviewRequired` was not reached.
- Operator Production-output review: not performed.
- No approval was issued.
- There is no approved Revision or reviewed output hash.

## Persistence

- Session state: `ACTIVE`; current step `PhotoshopOutput`.
- Step states: Import `APPROVED`; Original Confirmation `APPROVED`; Print Dimensions `APPROVED`; Photoshop Output `FAILED`.
- Attempts: exactly two total—one successful Import and one failed Photoshop Output.
- Revisions: exactly one—the immutable imported PNG Revision `01a0651e-8258-71ee-8e1e-960190aff235`.
- Review decisions: exactly one explicit approval of that original Revision and SHA-256.
- Hidden retries: none.
- Automation lock after failure: free; no row is held by this session.
- The persisted state agrees with the visible failure and contains the failed attempt.

## Final state

- Immediately after failure, a read-only Photoshop check found the accepted PID 29480 at `KnownStartScreen` with no document loaded; the customer Working document was not retained.
- After the separate engineering smoke, the owned synthetic document was closed through the signed discard path. A later read-only check found a different operator document open. It was not inspected or closed. No customer Working document from this session was loaded.
- Meitu was not used.
- Session-directory census: 102 before, 103 after; the only new session is `S_20260903T023453Z_43e8af85`.
- Comparison file census: 70 before and after.
- Quarantine file census: 2 before and after.
- Original-source SHA-256 remained unchanged.

## Safety

- Customer artefacts touched: the selected source was read; PrintFlow created its own Source snapshot and one attempt-scoped Working copy.
- Unrelated customer image content was not inspected. Only aggregate directory counts were read outside the selected session.
- Normal operator input was used to select the file/workflow, confirm the original, choose size/W1, and start the one Production step. This was necessary because Codex desktop coordinate automation is unavailable on this Windows 10 build. It did not rescue the running adapter attempt.
- No manual Photoshop completion, no retry, no output promotion, no approval, and no attempt deletion occurred.
- The failure exposed a new Production correctness defect; therefore Part D is blocked.

## Engineering status

- Production source changed after the failed order was stopped: **yes**.
- Production configuration changed: **no**.
- Accepted preset or Action artifact changed: **no**.
- Fix: the fixed W1 program now detects Photoshop's special Background layer, activates it, promotes it to a normal editable layer, verifies that promotion succeeded, and only then invokes the one signed Action. All non-mutating path, geometry, colour/channel, Action-set, and Action-transcript guards still run before this mutation.
- Regression: the generated program must perform the promotion after runtime verification and immediately before its sole `app.doAction` call. The new test failed before the fix and passed afterward.
- Focused W1 execution suite: 37/37 passed.
- Live bounded Photoshop smoke: one fresh flattened/no-alpha synthetic PNG, `W1_1px` once, 600 × 400 px at 300 PPI, CMYK/8, one non-empty W1 spot channel with 240,000 non-white pixels, unchanged backing SHA-256, no save/output, and exact owned-document signed-discard cleanup passed.
- Full product-source suite after the change: **10,138 passed / 0 failed / 0 skipped**.
- Git state: the adapter fix, regression/smoke coverage, and this report are the only intended changes. No rebase, amend, or push was performed.
- The running PrintFlow app still needs to be closed, rebuilt from this final source, and restarted before a new Production attempt. Production Readiness and the controlled real-order proof must then begin again from the start; this failed session must not be retried and reported as the Part D proof.

## Post-fix rerun attempt — invalid closure and preview defect

This section records the later post-fix workstation activity without rewriting the original failed
attempt above into a success.

### Final-binary restart and readiness

- No PrintFlow process was running immediately before the rebuild.
- The final accepted source was rebuilt on 2026-09-03 with .NET SDK 10.0.400: 0 warnings and
  0 errors.
- Built identities included `PrintFlow.App.dll` SHA-256
  `9114C2FB1CDD641B05C98CE9A7A83159E88066185FD6111DCDAFB62B24EAB3B6` and
  `PrintFlow.Infrastructure.dll` SHA-256
  `94634C3DA2FE7DAB8CD467D822D7ABD9015E19153F05CFBE98B2B9C61C123EEB`, both written after
  the accepted Background-layer source change.
- A new PrintFlow process, PID 3156, started from those binaries at 2026-09-03 15:31:15
  Pacific/Auckland.
- Immediate `VerifiedEnvironmentGateWorkstationSmoke`: 2/2 passed; 28/28 integrity entries;
  Production Readiness `Ready`; `gate(Production) = ALLOWED`. The same 16 read-only attribute
  and external-application language observations remained advisory.
- The selected original was independently re-read before the UI activity: 1,665,601 bytes,
  SHA-256 `3A81C2598A989C4C5A9BC9130CC3564D5FA32958CD5B9A4CE83154AE3475D96F`.

### Mandatory fresh-session boundary was not met

- No new session was created. The session-directory census remained 103.
- The operator resumed the original failed session
  `01a0651e-8125-7ff2-aed9-e76843e8af85` / `Sessions/S_20260903T023453Z_43e8af85`.
- A second Photoshop attempt `01a06557-1975-73c2-a47b-19d1174a42a4` was created inside that
  session with `RetrySequence = 1` and succeeded.
- The original failed attempt `01a06530-f227-710e-ad3c-4677458a2b53` remains present and failed,
  but its session no longer satisfies the required no-retry history. `PhotoshopOutput.AttemptCount`
  is now 2.
- This successful retry is therefore **not eligible** to close Part D even if every later check
  were to pass.

### Successful adapter output and new review blocker

- The retry reached `PhotoshopOutput = REVIEW_REQUIRED` and created Revision
  `01a06557-adb2-79e7-994a-0e7d9edca312`.
- The adapter recorded the `W1_1px` branch, a Photoshop spot W1 channel with 893,025 non-white
  pixels, unchanged source backing bytes, signed owned-document discard, and no cleanup warning.
- The attempt-scoped TIFF is
  `Sessions/S_20260903T023453Z_43e8af85/Working/01a06557-1975-73c2-a47b-19d1174a42a4/ChatGPT Image Sep 2, 2026, 03_41_18 PM_80mm_CMYK_W.tif`.
- TIFF identity: 7,863,396 bytes; SHA-256
  `8771725E958DA79DAB941557AC09D17B9A971D1FF262401B86F1C0BDA74AD78A`; 945 × 945 px;
  300 × 300 DPI; five 8-bit interleaved separated samples; CMYK plus non-empty Photoshop spot W1;
  no alpha; no pyramid; one all-RLE layer.
- The PrintFlow review screen showed the before/after pane and TIFF metadata but rendered the
  TIFF preview fully transparent. Operator visual review could not be completed.
- No TIFF approval or rejection decision was recorded. The output remains `NOT_REVIEWED` in
  Working, and the session remains active at `ReviewRequired`.

### Preview defect diagnosis

- The exact live session/revision resolves successfully through `ArtefactPreviewService`.
  WIC decoding returns a 945 × 945 display payload of 1,521,566 bytes, and
  `PreviewPayloadConverter` returns a 945 × 945 `BitmapImage`.
- Pixel inspection is the red-capable signal for the visible failure: all 893,025 payload pixels
  have alpha 0, so WPF correctly draws no visible picture. 535,626 pixels still carry non-white
  colour values; the image data was not absent.
- The source frame is a WIC `Default` 40-bpp frame. `WicPixelFormats.HasAlpha` returns unknown/null,
  which the decoder truthfully reports as no confirmed transparency, but
  `FormatConvertedBitmap(..., Bgra32, ...)` maps the fifth W1 sample into BGRA alpha.
- A minimal accepted 2 × 2 CMYK+W1 TIFF reproduces the defect when all four W1 samples represent
  ink: the preview has 0/4 opaque pixels. The fifth sample is load-bearing for the repro.
- In-memory normalization of alpha to 255 when source alpha is not positively identified changes
  the live preview from 0/893,025 opaque pixels to 893,025/893,025 without changing its colour
  samples. This confirms the fault is the preview normalization boundary, not the Production TIFF
  or the W1 Action.
- No Production source was changed during this diagnosis. The temporary diagnostic TIFF was
  removed.

### Final observed state

- The selected original still has the same 1,665,601-byte identity and SHA-256
  `3A81C2598A989C4C5A9BC9130CC3564D5FA32958CD5B9A4CE83154AE3475D96F`.
- The produced TIFF still has the persisted byte length and SHA-256 above.
- Automation lock is free.
- Session directories: 103 before and after; Comparison files: 70 before and after; Quarantine
  files: 2 before and after.
- During operator investigation after the adapter cleanup, Photoshop was observed with the exact
  produced TIFF open. A clean no-document final state therefore cannot be claimed.
- Because the mandatory fresh-session boundary was missed, the original failed session was
  retried, operator review was blocked by a confirmed preview defect, no approval occurred, and
  final Photoshop state was not clean, Part D remains blocked. A preview fix needs its own
  engineering/test gate, followed by another restart and a genuinely fresh controlled session.

**11600-D BLOCKED — REAL PRODUCTION ORDER NOT VERIFIED**
