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

---

## Final fresh-session closure

This section records the fresh controlled real production order that closes Part D. Every section
above is preserved unchanged as defect-discovery history: the first Background-layer failure, the
Background promotion engineering fix, the invalid `RetrySequence = 1` rerun, the WIC preview
defect and the D1 preview fix. The `11600-D BLOCKED` verdict above states the position reached at
the end of that earlier narrative; it is superseded by the verdict at the foot of this section.

### Final-binary process boundary

- Working tree clean at commit `035c4e2` — the accepted D1 commit. The two latest local commits
  carry no `Co-Authored-By` trailer, so the §23 one-time cleanup was already satisfied and no
  history was rewritten. Both commits are frozen.
- The built binaries were verified **byte-identical** to the identities the D1 gate accepted:
  `PrintFlow.App.dll` SHA-256 `B4ED9D9E94E2C735544BBABF0970386713AD631DE060D04B1E8F576686A274E6`
  and `PrintFlow.Infrastructure.dll` SHA-256
  `8565583DD175949FE032516647C7032C82CC88D76C4C3864C76E8C71E8A68B15`. A rebuild was therefore not
  necessary and was not performed; the final D1 source and the running binaries are the same
  artefacts the accepted gate measured.
- The previous PrintFlow process — PID 5592, the process D1 used for its read-only preview proof —
  was closed normally through its main window, not terminated. Process count afterwards: 0.
  Automation lock free.
- A new PrintFlow process **PID 2216** was started from those binaries at 2026-09-03 16:50:33
  Pacific/Auckland, responding, window `PrintFlow Studio`.
- Per §21 the 10,146-case suite was not re-run: product source, configuration, preset and
  dependencies are unchanged from the D1 gate.

### Production Readiness

Checked in the new process immediately before Import, at 2026-09-03 16:51:

```text
本工作站已通过生产环境校验。        (workstation passed production environment verification)
verified workstation preset   printflow-workstation-v1 1.16.0 (6396FB4EB87F)
Adapters.Mode                 Production
EvidenceIntegrity             All 28 evidence files the preset vouches for are present and hash exactly
blocking items                none
gate(Production)              ALLOWED
```

All mandatory checks passed: `PresetIntegrity`, `EvidenceIntegrity`, `OperatingSystem`,
`MeituExecutable`, `PhotoshopExecutable`, `PhotoshopActionArtifact`, `WorkspaceRoot`,
`InteractiveSession`, `DisplayConfiguration`, `UiCulture`. The two advisories
(`FilesystemReadOnlyPolicyAdvisory` — 16 of 28 evidence files no longer carry the read-only
attribute, signatures still exact; and `ExternalApplicationUiLanguage`) remained informational and
did not block production, exactly as in the accepted baseline.

### Photoshop preflight

The accepted Photoshop CC 2019 process PID 26924 (`D:\Adobe Photoshop CC 2019\Photoshop.exe`) was
**not** in the accepted clean starting state: it still had the *historical* session's Production
TIFF open, precisely the condition the D1 report flagged as preventing a clean-state claim.

Before Import this was cleared as environment hygiene, not as order preparation: the stale
document was closed through Photoshop's normal close path. It was not dirty, so no save prompt
appeared and nothing was written. The historical TIFF was measured before and after and is
byte-identical — 7,863,396 bytes, SHA-256
`8771725E958DA79DAB941557AC09D17B9A971D1FF262401B86F1C0BDA74AD78A`, last-write time
2026-09-03T03:37:16.4037731Z unchanged.

Resulting accepted state before Import: exact accepted executable, main frame
`Adobe Photoshop CC 2019`, no document, no blocking dialog, no retained PrintFlow Working document,
one top-level window. Meitu was not required and was not exercised.

### Customer source identity

Re-measured immediately before the new session, not copied from the historical record:

```text
path          C:\Users\admin\Downloads\ChatGPT Image Sep 2, 2026, 03_41_18 PM.png
byte length   1,665,601
sha256        3A81C2598A989C4C5A9BC9130CC3564D5FA32958CD5B9A4CE83154AE3475D96F
```

This matches the expected historical identity, so the intended order source is unchanged.

### Fresh session from Import

```text
SessionId              01a0659e-8930-757c-88d8-035104eaeac1
relative workspace     Sessions/S_20260903T045443Z_04eaeac1
created                2026-09-03T04:54:43.760Z
workflow               GENERATE_PRINT_TIFF
```

The session is new in every required respect: new SessionId, new session directory, new Import
attempt, new source Revision, `RetrySequence = 0`. The historical session was not retried, not
resumed, not returned to, and its workflow state was not cloned.

- Import attempt `01a0659e-8930-7809-90e2-06629a60f204`, adapter `internal-import-v1`,
  `SUCCEEDED`, `RetrySequence = 0`.
- Immutable source Revision `01a0659e-8a4c-78af-84b0-44fb054bdcf4`, `IMPORT`, PNG, 1254 × 1254,
  1,665,601 bytes, SHA-256 `3A81C2598A98…D96F` — equal to the selected original.
- `InputSnapshot` records `OriginalSourcePath` as the exact selected customer file.
- The on-disk Source snapshot is read-only and hashes to the same SHA-256.
- No Revision belonging to this session is shared with any other session.
- Explicit Original Confirmation was performed on the displayed original; `ReviewDecision`
  `01a0659f-f7df-7519-b3b1-b12194f7127b`, `OriginalConfirmation`/`REVISION`, `APPROVED`, reviewed
  SHA-256 `3A81C2598A98…D96F`, operator `admin`, 2026-09-03T04:56:17.631Z.

### Print dimensions and W1

The real order requirement was re-entered in the new session rather than inherited from workflow
state, and it is unchanged from the order as originally specified:

```text
target edge            width
width                  80 mm
proportional           yes ("图片将按比例缩小")
output resolution      300 PPI (fixed)
persisted projection   945 × 945 px, 80 × 80.01 mm
preset / semantics     CUSTOM / TARGET_EDGE_V1
W1 branch              W1_1PX  ("1 像素 — 普通图案")
```

### One Production Photoshop attempt

Exactly one Photoshop Production attempt was run. No manual Photoshop preparation of any kind was
performed: the Background was not unlocked or promoted by hand, no layer was created, no Action was
run manually, W1 was not repaired, no TIFF was saved manually and no cleanup was fixed by hand.

```text
attempt                       01a065a2-4843-78b6-ac4d-0bc4f46be465
adapter                       photoshop-cc2019-production-v1
result                        SUCCEEDED
RetrySequence                 0
RetryOfAttemptId              NULL
PhotoshopOutput.AttemptCount  1
```

The first attempt succeeded — from the same starting condition that failed in the historical
session with the Background-layer defect.

### Background promotion and W1

The accepted product code performed the whole operation. In the generated W1 program the
non-mutating guards still precede all mutation: managed-path identity, prepared geometry and
300 PPI, RGB/8 mode, a channel census requiring exactly three component channels and no
pre-existing W1, and `verifyRuntime()`. Only then does `promoteBackgroundLayer(doc)` run, which
detects Photoshop's special Background layer, activates it, clears `isBackgroundLayer`, and throws
if the layer is still a Background layer afterwards — promotion is verified, not assumed. The very
next statement is the sole `app.doAction` call, which is the only `app.doAction` occurrence in the
bridge.

The invocation count is not merely observed but enforced: `GuardedPhotoshopW1Executor` fails the
attempt unless `ActionInvocationCount == 1`, and `GuardedPhotoshopTiffSaver` refuses to save unless
`ActionInvocationOccurredExactlyOnce`. A saved, validated TIFF is therefore proof that the signed
Action ran exactly once.

```text
Background promotion required   yes (the imported flattened PNG opens as a Background layer)
promotion verified              yes (enforced by the program's own post-condition)
signed Action branch            W1_1PX
app.doAction count              1  (enforced by the executor and the saver)
```

Adapter-recorded W1 facts: `spot W1 (photoshop spot True, non-white 893025 px)` — W1 exists, is a
Photoshop spot channel, and contains non-white content. No corrective second Action invocation
occurred.

### Production TIFF contract

Full existing Production validation ran (`photoshop-tiff-validation-c1-v1`) and the artefact was
re-measured independently from its bytes:

```text
relative path   Sessions/S_20260903T045443Z_04eaeac1/Working/
                01a065a2-4843-78b6-ac4d-0bc4f46be465/
                ChatGPT Image Sep 2, 2026, 03_41_18 PM_80mm_CMYK_W.tif
sha256          3669ACCF70BA8B940019C516505EDA4EADEDFDCF237E1666513DF0FCC04E9BCD
byte length     7,863,396
dimensions      945 × 945 px
resolution      300 × 300 DPI (ResolutionUnit = inch)
byte order      II — IBM PC / little-endian
photometric     5 = Separated (CMYK)
samples/pixel   5, BitsPerSample 8,8,8,8,8, PlanarConfiguration 1 (interleaved)
compression     1 = none
W1 spot channel non-empty, 893,025 non-white pixels
alpha           ExtraSamples = 0 — no actual alpha
pyramid         none (single IFD, NewSubfileType = 0)
layers          1, all-RLE
software        Adobe Photoshop CC 2019 (Windows), DateTime 2026:09:03 16:58:59
```

Adapter cleanup: `cleanup signed owned-document discard completed; expected Working document gone`,
with no cleanup warning. The output settled over 3 observations in 2.1 s. The source backing was
recorded unchanged (`backing unchanged 3A81C2598A98`) and the attempt-scoped Working PNG hashes to
the original's SHA-256. The automation lock was free after the attempt.

The TIFF remained byte-identical through cleanup, through review and through approval promotion.

### Visible preview proof in the actual operator UI

This is the D1 closure proof taken on the real review screen, not by service-level pixel inspection.

At `ReviewRequired` the PrintFlow review screen displayed both panes: `处理前` (the original PNG,
1254 × 1254) and `处理后` (the Production TIFF, 945 × 945). The Production TIFF preview was
**visibly rendered** — the full customer emblem was drawn, not the fully transparent pane the
historical session showed.

Measured directly from the rendered screen pixels of the maximized window, sampling each pane's
image region:

```text
                        处理后 (Production TIFF)   处理前 (original PNG)
distinct colours        9,765                     9,164
luminance range         15 – 255                  0 – 255
dark pixels (<100)      7.31 %                    7.08 %
non-white pixels        43.07 %                   41.73 %
```

Under the historical defect the after-pane would have measured ~0 % non-white. The two panes are
closely comparable, which is the on-screen statement that the operator sees the same artwork at the
same visual density in both. Displayed TIFF metadata remained authoritative and agreed with the
file: `TIFF`, `945 x 945`, `300 x 300`, SHA-256 `3669ACCF70BA`, Revision `442b4f92`, and
`已验证 CMYK + W1 · 1 像素 — 普通图案`.

### Human visual review

Automation stopped at `ReviewRequired` and the decision was put to the operator, who reviewed the
rendered production preview at full window size.

- correct customer artwork — yes, the imported emblem, matching this session's original;
- correct orientation — yes, upright;
- correct crop — yes, the full circular emblem, nothing clipped;
- correct size — 945 × 945 px, 80 × 80.01 mm at 300 dpi;
- no stretch — the square source maps to a square output and the circle remains circular;
- no missing content — ring text, cross, sunburst, mountains, open book and `EFIS` all present;
- no wrong-session artwork — the artwork belongs to this session's source;
- no obvious processing artefact — clean linework, smooth gradient, no banding or halo;
- production preview visually usable — yes.

```text
Operator review: PASS
```

No auto-approval occurred; the decision was made by the operator and only then acted upon.

### Explicit approval

```text
state before approval   PhotoshopOutput = REVIEW_REQUIRED, PrintOutput = NOT_REVIEWED
approved Revision       01a065a2-7e01-753a-ab55-aae3442b4f92   (= the reviewed Revision)
approved hash           3669ACCF70BA8B940019C516505EDA4EADEDFDCF237E1666513DF0FCC04E9BCD
                                                               (= the reviewed TIFF hash)
ReviewDecision          01a065aa-613e-7a3e-b85f-d832c8382308
                        PhotoshopOutput / PRINT_OUTPUT / APPROVED
operator                admin
decided                 2026-09-03T05:07:39.966Z
```

The approval was an explicit operator action taken through the normal review control after the
review verdict, on exactly the Revision and hash that were reviewed.

### Final approved artefact

Re-read after approval from its promoted location:

```text
approved Revision id    01a065a2-7e01-753a-ab55-aae3442b4f92
approved relative path  Sessions/S_20260903T045443Z_04eaeac1/Approved/
                        ChatGPT Image Sep 2, 2026, 03_41_18 PM_80mm_CMYK_W.tif
exists                  yes
byte length             7,863,396      (unchanged)
sha256                  3669ACCF70BA8B940019C516505EDA4EADEDFDCF237E1666513DF0FCC04E9BCD
                                       (unchanged)
inspector               still passes — 945 × 945, 300 × 300 DPI, separated CMYK, 5 × 8-bit
                        interleaved, ExtraSamples 0, single IFD
```

### Persistence readback

The fresh session read back independently from the workspace database:

```text
Import attempt                 Succeeded   (01a0659e-8930-7809-90e2-06629a60f204, retrySeq 0)
Photoshop Production attempt   Succeeded   (01a065a2-4843-78b6-ac4d-0bc4f46be465)
Photoshop RetrySequence        0
Photoshop AttemptCount         1
Photoshop attempts total       1, of which failed: 0
RetryOfAttemptId               NULL
ReviewRequired                 occurred (observed live; step moved REVIEW_REQUIRED → APPROVED)
Operator approval              exactly one for PhotoshopOutput; rejections: 0
Approved Revision              exactly the reviewed Revision
Revisions                      2 (IMPORT, PHOTOSHOP_OUTPUT)
Automation log entries         0
Automation lock                free
Steps                          Import / OriginalConfirmation / PrintDimensions /
                               PhotoshopOutput all APPROVED
```

No hidden retry, and no failed Photoshop attempt exists in this fresh proof session.

Observation, not a defect of this order: `ProcessingAttempt.EndedAtUtc` equals `StartedAtUtc` for
every attempt in the database, historical and fresh alike, so the attempt row does not express
duration. This is pre-existing persistence behaviour, unchanged by this order, and outside Part D's
definition of done. The TIFF's own `DateTime` of 16:58:59 against the 16:58:48 step start is the
evidence that real Photoshop work occurred.

The session was left at its approved state with all four steps passed. Part D's path ends at
explicit approval, so the session was deliberately not carried further.

### Historical session preserved

Read back after the fresh order completed, unchanged in every recorded respect:

```text
session state   ACTIVE, step PhotoshopOutput
attempts        01a0651e-8125-7b5a-be86-43601434c7ae  Import           Succeeded  retrySeq 0
                01a06530-f227-710e-ad3c-4677458a2b53  PhotoshopOutput  Failed     retrySeq 0
                01a06557-1975-73c2-a47b-19d1174a42a4  PhotoshopOutput  Succeeded  retrySeq 1
output          01a06557-adb2-79e7-994a-0e7d9edca312  NOT_REVIEWED, still in Working
reviews         1 (OriginalConfirmation, approved)
tiff sha256     8771725E958DA79DAB941557AC09D17B9A971D1FF262401B86F1C0BDA74AD78A (unchanged)
```

The defect-discovery history was not rewritten: attempt 1 remains failed, attempt 2 remains the
invalid `RetrySequence = 1` success, and its Revision remains unreviewed. Nothing in it was
approved, rejected, retried or deleted.

### Original source final integrity

```text
before fresh order   1,665,601 bytes  3A81C2598A989C4C5A9BC9130CC3564D5FA32958CD5B9A4CE83154AE3475D96F
after fresh order    1,665,601 bytes  3A81C2598A989C4C5A9BC9130CC3564D5FA32958CD5B9A4CE83154AE3475D96F
```

Equal, with the original's last-write time also unchanged. The customer original was not renamed,
moved, overwritten or deleted.

### Photoshop final state

After approval:

```text
process                              PID 26924, exact accepted executable, responding
main frame                           Adobe Photoshop CC 2019  (accepted no-document / start state)
documents open                       0
customer Working document retained   no
blocking dialog                      none
top-level windows                    1
automation lock                      free
```

The adapter's signed owned-document discard removed the Working document by itself; no manual
close was needed after the attempt. Unlike the earlier post-fix activity, no customer Working
document was left open at the end of this proof.

### Filesystem census

```text
session directories   103 before  →  104 after   (= before + 1)
lost sessions         0
Comparison files      70 before   →  70 after
Quarantine files      2 before    →  2 after
```

The only new session directory is `Sessions/S_20260903T045443Z_04eaeac1`, this order's session. No
previous failed or retry session was deleted.

### Engineering status

No product code, configuration, preset, Action artifact or dependency was changed during this final
order. No Production defect appeared, so no engineering slice was entered and no patch-and-continue
occurred.

### Verdict

The definition of done is met in full: the final D1 binaries were running, a completely fresh
session started from Import, the historical failed/retry session is untouched, the source bytes are
unchanged, Production Readiness passed, exactly one Photoshop Production attempt succeeded with
`RetrySequence = 0`, Background promotion worked automatically, the signed Action ran exactly once,
W1 validated, the TIFF validated, the preview is visibly usable in the actual review screen, human
review passed, an explicit approval occurred, the approved Revision is exactly the reviewed
Revision, the approved output remains byte-identical, the persistence readback agrees, the lock is
free, Photoshop ended clean, and no unrelated customer artefact was touched.

**11600-D PASS — FIRST CONTROLLED REAL PRODUCTION ORDER VERIFIED**
