# SCRUM-11104 — Build TIFF Final Review Mode — completion report

**Date:** 8 September 2026
**Repository:** `D:\Repositories\printflow-Studio`, branch `master`, local commits only
**Result:** **PASS WITH NOTES — SCRUM-11104 TIFF FINAL REVIEW VERIFIED**

---

## 1. Requirement authority

Read before any Product source was edited, from
`C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv`, row 46, source Work Item
**11411** (`SCRUM key = CSV position from SCRUM-11060`, per §0 of the coverage re-audit):

> **Build TIFF Final Review Mode**
>
> Extend the shared review module with CMYK colour preview, white-ink channel preview,
> colour-plus-white overlay and production metadata including millimetres, pixels, effective DPI,
> colour-setting or preset identifier and output path. Final human approval must bind to the exact
> PrintOutput hash; no successful Photoshop automation may silently complete the Session without
> this review.

Parent epic, CSV **11400** → SCRUM-11093, *Generate and Validate Production TIFF Outputs with
Photoshop*.

The previously audited gaps (`original-jira-functional-coverage-reaudit.md`, SCRUM-11104 row) were:

1. CMYK colour preview
2. Separate W1-channel preview
3. Colour + white overlay preview
4. Effective DPI display
5. Output path display
6. Complete final-production metadata block

Already accepted and left untouched: structural validation before review, mandatory final review,
hash-bound approval, no silent completion after a successful Photoshop run, W1 branch statement,
Recycle-Bin rejection, and independent review per output size.

---

## 2. What the polarity and colour claims are actually based on

Nothing here guesses. The accepted production TIFF's own bytes were read before the decoder was
written, from the frozen Epic 11000 baseline
`D:\PrintFlowStudio\Baseline\workstation-v1\actions\authoring\FIX-CUSTOMER-DESIGN-001_W1-1PX.tif`
(3307 × 4474, uncompressed, `PhotometricInterpretation` 5, five 8-bit interleaved samples,
`ExtraSamples` 0, 300 PPI, one Photoshop W1 spot channel):

| Sampled pixel | C | M | Y | K | W1 |
| --- | --- | --- | --- | --- | --- |
| background, `y=4, x=0` | 0 | 0 | 0 | 0 | **255** |
| background, `y=2237, x=0` | 0 | 0 | 0 | 0 | **255** |
| artwork, `y=2237, x=827` | 72 | 159 | 144 | 63 | **0** |
| artwork, `y=2237, x=2480` | 38 | 41 | 59 | 2 | **0** |

Two different conventions live in one file, and that asymmetry is the whole of §41:

* **Process inks (C, M, Y, K)** follow the TIFF separated convention — **0 is no ink**, 255 is
  full ink. Blank paper is `0,0,0,0`.
* **The W1 spot channel** is stored the way Photoshop stores spot channels — **255 is no ink**,
  0 is 100% coverage. This is the same convention `ProductionTiffInspector` already validates
  against (a channel of all 255s is refused as "wholly empty on disk") and the same one the
  Photoshop histogram bridge counts coverage with.

---

## 3. Colour preview design (§6, §7)

**Identifier** (stable, non-localised, carried on every payload):

```text
cmyk-naive-device-v1: R=(255-C)(255-K)/255, G=(255-M)(255-K)/255, B=(255-Y)(255-K)/255;
no ICC profile applied; an uncalibrated screen approximation, not a colour proof
```

`WicImagePreviewDecoder`'s generic flattening was deliberately not reused as the Colour mode. WIC
presents a five-sample separated TIFF as BGRA with the Photoshop spot channel occupying the alpha
byte — which PrintFlow then has to normalise to opaque so the artefact is visible at all
(Epic 11600 Part D1). That is honest enough for "show me the file", but it spends the one channel a
specialist review is about. The specialist decoder reads the uncompressed interleaved strips
directly and keeps all five samples.

**Colour-management truthfulness.** The baseline TIFF carries an embedded ICC profile (tag 34675,
654,352 bytes). It is **not** applied. The Colour mode is an uncalibrated device conversion for
visual inspection — "is the right artwork here, the right way up, with the white where I expect
it" — and the screen says so:

> An uncalibrated screen approximation of the CMYK content. It is not proof of printed colour.

`LocalisationResourceTests.The_colour_preview_legend_disclaims_printed_colour_accuracy` keeps that
disclaimer in both languages and refuses "accurate", "exact", "as printed" and "faithful". The
Production TIFF structural contract (SCRUM-11103) remains the authority; nothing here re-validates
it.

**Independent check.** `The_colour_preview_agrees_with_an_independent_codec_about_ink_direction`
decodes the same fixture through WIC's own separated-CMYK conversion and requires the two to agree
per channel within 24/255 — a second opinion, from code that knows nothing about this decoder, on
the one thing a naive formula could get catastrophically wrong.

---

## 4. White-ink preview mapping and polarity (§8, §9, §41)

**Identifier:**

```text
w1-spot-inverted-v1: stored 255 = no white ink, stored 0 = 100% white ink;
displayed intensity = 255 - stored, so bright means ink
```

Bright is ink, black is none — the polarity §8 asks for. The preview is decoded from **the same
fifth sample `ProductionTiffInspector` validated**, in the same parse: `InspectForReview` returns
the validation facts *and* the strip geometry, and `Inspect` is that method with the raster
dropped. There is no second parser, so a reader that drew pixels could not accept a layout the
validator refuses.

It is never derived from source alpha, the original PNG's transparency, the Action branch name, or
colour luminance. `The_white_ink_preview_is_independent_of_the_colour_content` proves the last of
those directly: two files with identical W1 bands and completely different CMYK produce
byte-identical white-ink payloads.

**Region-level verification, not "the channel exists" (§10).** `ProductionTiffFixture` now writes
`W1VerticalBands` — equal-width vertical bands of stored fifth-sample values. With `[255, 128, 0]`
the preview must read black / mid grey / white across the canvas, and the tests assert each region
by value (`0`, `127`, `255`). The reported ink-sample count is accumulated over every source pixel
and must equal `ProductionTiffFacts.W1NonWhiteSampleCount` from the independent inspector.

---

## 5. Overlay design and legend (§11, §12)

Colour content with a restrained fixed-strength white wash where the fifth sample carries any ink:

```text
blended = colour + (255 - colour) * (ink / 255) * 0.55
```

Fixed strength rather than coverage-proportional opacity on purpose: proportional shading reads as
"this is how white it will look", which is a printed-appearance claim. A flat marker reads as "the
white ink is here", which is the only thing the overlay is entitled to say. The legend states it:

> White overlay = white ink coverage. Not how the print will look.

`The_overlay_marks_white_ink_over_the_colour_and_leaves_bare_colour_alone` requires the no-ink
region to be the colour image *exactly*, the full-ink region to be lighter but **not** pure white
(the artwork must remain visible under the marker), and the half-ink region to sit between them.
The TIFF is never altered; the overlay exists only as encoded display bytes.

---

## 6. Effective DPI (§18–§22, §46)

**Definition, one implementation** — `TiffEffectiveResolution.EffectiveDpi`:

```text
effective DPI X = source usable pixels X / (requested physical width mm / 25.4)
effective DPI Y = source usable pixels Y / (requested physical height mm / 25.4)
```

Two facts are kept apart and displayed as separate rows, exactly as §19 requires:

| Row | Meaning |
| --- | --- |
| `TIFF resolution` | The output's own tag: fixed at 300 PPI by contract, true of every production output |
| `Effective source resolution` | How many source pixels the chosen physical size actually had behind it |

**Authoritative source (§20).** `SourcePixelWidth`/`SourcePixelHeight` come from the
`PhotoshopPreparation` snapshotted on the attempt that produced *this exact* Revision — the
Domain's own binding to `SourceRevisionId` + `SourceSha256`. Never the original import, never a
historical Revision, never whatever the screen is currently showing, and never the session's
*pending* plan (which after a reject, a retry or an Add Another Size describes a different output).

**Physical dimensions (§26)** come from the same preparation: `ProjectedPixelWidth × 25.4 / 300`.
`PrintOutput.Dimensions` is deliberately not used for a maximum-bound run — there the pair is a
*fit box*, and 200 × 150 mm bounds routinely produce a 200 × 143 mm output, so reporting the box as
the size would overstate one edge on almost every job.

**No invented thresholds (§21).** There are no bands, no colours and no `IsAcceptable`. The only
derived statement is `IsBelowProductionResolution`, which is arithmetic against the 300 PPI the
output contract already fixes, rendered as "· below the 300 PPI production resolution".
`An_authorised_enlargement_reports_the_factual_resolution_and_the_authority` asserts, by whole-word
regex, that neither the effective-resolution row nor the enlargement row ever contains "green",
"yellow", "amber", "red", "acceptable", "unacceptable", "good", "poor" or "ok".

**Enlargement authority (§22).** The row appears **only** when the run actually required authority,
and states "required and authorised by the operator" — an authority given, never a quality verdict.
A job that never needed authority gets no row rather than a reassuring "not required".

**Test matrix (§46).** All verified by recomputing from the *persisted* preparation, never from the
view model:

| Case | Covered by |
| --- | --- |
| No enlargement (shrink) | `Effective_source_resolution_is_source_pixels_over_the_requested_inches` — 1200 × 900 source into 60 × 60 mm |
| Enlargement authorised | `An_authorised_enlargement_reports_the_factual_resolution_and_the_authority` — target-edge run below 300 PPI |
| Cropped source | `Effective_source_resolution_follows_the_bound_upstream_Revision(AutomaticTrim)` |
| Keep original extent | `…(KeepOriginalExtent)` — pre-Trim full canvas |
| Manual crop | `…(ManualCrop)` — the operator's own rectangle |

The three upstream routes leave three genuinely different canvases (120, 200 and 150 source px
wide from one 200 × 160 design), and the test asserts the bound Revision's own facts equal the
preparation's, so a metric computed from the import would fail.

---

## 7. Output path and file identity (§23, §24)

`TiffReviewPayload.OutputPath` is `IWorkspace.ResolveAbsolute(output.File)` — the managed final
TIFF, never the source, never a workspace intermediate, never a prior size's file. It appears twice
on screen: as the `Output file` and `Output path` metadata rows, and as a read-only, focusable
`TextBox` (`Session.TiffOutputPath`) the operator can select and copy with Ctrl+C.

**On "Copy path" (§24):** §24 says *consider* it. A read-only TextBox gives the same outcome with
standard controls, keeps the path selectable by keyboard, exposes it to UIA as a value a driver can
read, and adds no clipboard API to a view model. No shell-open or navigate action was added.

Independently verified in both the deterministic tests and the live run: the path shown on screen
equals `PrintOutput.OutputPath`, and hashing the bytes **at that path** reproduces the reviewed
SHA-256.

---

## 8. Production metadata block (§25–§28)

Rendered from `ObservableCollection<TiffMetadataRow>`; each row carries a stable English `Key`
(the `AutomationId`), a localised `Label` and a localised `Value`.

| Key | Label | Source |
| --- | --- | --- |
| `OutputFile` | Output file | `PrintOutput.File.FileName` |
| `OutputPath` | Output path | `IWorkspace.ResolveAbsolute` |
| `Pixels` | Pixel dimensions | decoded TIFF |
| `Physical` | Physical size | preparation projection at 300 PPI |
| `Resolution` | TIFF resolution | the file's own resolution tags |
| `EffectiveDpi` | Effective source resolution | preparation source px ÷ requested inches |
| `Enlargement` | Enlargement | *only when authority was required* |
| `Colour` | Colour | `CMYK · 8-bit · 5 ink channels` |
| `WhiteInk` | White ink | `W1 validated · <branch> · <n> px of ink` |
| `Preset` | Production preset | preset id + manifest hash short form |
| `Sha256` | SHA-256 | `D1E69C41…AC5EA412` |

Nothing is filled with assumptions. `EffectiveDpi` is absent when the producing attempt recorded
no preparation; `Enlargement` is absent unless one was actually required.

**Hash traceability (§28):** head-and-tail with an ellipsis, the form a person can match against a
support ticket at a glance. The approval was already bound to the full hash; this is traceability,
not verification, and the operator is never asked to compare 64 characters.

**Note — the preset version is deliberately not shown.** The original AC asks for a "colour-setting
or preset identifier". The `PrintOutput` row persists the preset **id** and **manifest SHA-256**
but not the version, so a row read back from the database carries the literal `"unknown"` for it
(`Mappers.cs:1659`). Printing "unknown" beside a real hash would state a non-fact where §25
requires an authoritative value or nothing, so the row shows
`printflow-workstation-v1 (6396FB4EB87F)` — and the manifest hash identifies the configuration more
exactly than a version string would. A regression asserts the row never contains "unknown". Adding
the version to the schema is a persistence change outside this task's scope.

**Note — the confirmed colour settings are not separately displayed.** The AC's "colour-setting
**or** preset identifier" is satisfied by the preset identifier; the colour settings themselves are
part of the signed manifest that hash pins.

---

## 9. Review-mode UI, decode-once architecture and hash binding

```text
validated TIFF
  → ProductionTiffInspector PASS (one parse, facts + raster)
  → PrintOutput persisted with its SHA-256
  → ITiffReviewDecoder.DecodeAsync(file, expectedSha256)
       → hash the file; refuse on mismatch
       → decode strips once
       → Colour PNG + White-ink PNG + Overlay PNG (identical geometry)
  → TiffReviewPayload (PrintOutputId + Sha256 + metadata + three payloads)
  → SessionViewModel holds all three; a mode switch re-points one ImageSource
```

**New types**

| Layer | Type | Purpose |
| --- | --- | --- |
| Workflow (port) | `ITiffReviewDecoder`, `DecodedTiffReview` | Takes `WorkspaceFileRef` + expected `Sha256`; never a path |
| Workflow (service) | `IProductionTiffReviewService`, `TiffReviewPayload`, `TiffEffectiveResolution` | Addressed by `SessionId` + `RevisionId`; read-only |
| Infrastructure | `ProductionTiffReviewDecoder` | Reads the accepted layout's strips; produces encoded PNG bytes |
| Infrastructure | `ProductionTiffRaster`, `ProductionTiffInspection` | Strip geometry kept out of `ProductionTiffFacts` |
| App (view model) | `SessionViewModel.TiffReview.cs`, `TiffReviewMode`, `TiffMetadataRow` | Presentation state only |
| App (view) | `TiffReviewSurface` | The specialist surface; **not** a widening of `SharedReviewSurface` |

**Hash binding (§16, §17, §40).** The decoder hashes the file before reading a byte for display and
refuses with `RevisionIntegrityMismatch` when it is not the expected hash. The hash it is given is
the **`PrintOutput`'s own** — the value an approval binds to. Consequences, all covered:

* a TIFF replaced after validation (even by another perfectly valid production TIFF) produces no
  payload, no metadata and no path; the review panel stays up and the workflow's own integrity
  contract still refuses the approval;
* the specialist surface is never "silently refreshed" onto a new hash under an old decision;
* a recycled output reports `OutputMissing` rather than being decoded.

**No approval bypass (§36, §49).** `OnTiffReviewModeChanged` raises property notifications and does
nothing else — no command, no repository call, no reload, no viewport reset.
`Switching_mode_changes_nothing_that_an_approval_is_bound_to` re-reads the persisted aggregate and
the TIFF's bytes after cycling all three modes and requires `PrintOutputId`, SHA-256, the absent
`ReviewDecision`, the step state and the file bytes all unchanged — then approves successfully from
whichever mode the operator was in.

**Boundaries (§43, §44).** No WPF type appears in Workflow — all three representations are
`ReadOnlyMemory<byte>` PNGs turned into an `ImageSource` by the view's `PreviewPayloadConverter`.
`TiffReviewBoundaryTests` asserts that neither seam takes a `string`, that the decoder's signature
names the expected `Sha256`, that neither names a mutating type, that the shell contains no TIFF
parsing tokens, and that `SharedReviewSurface` contains no `Tiff`/`WhiteInk`/`W1`/`Cmyk` text.

The payload names `InkChannelCount` where the parser says `SamplesPerPixel`: the accepted rule in
`PhotoshopWorkflowOutputBoundaryTests` keeps TIFF parser vocabulary inside Infrastructure, and that
rule was honoured rather than relaxed.

---

## 10. Viewport, geometry and the specialist/shared boundary (§13, §14, §33–§35)

* **Separate surfaces (§14).** `SharedReviewSurface` (SCRUM-11079's general Before/After
  comparison) is untouched. `TiffReviewSurface` is a new control shown *instead of* the general
  panes when a specialist payload exists — one artefact, one picture of it. When no payload can be
  produced, the general panes stay up exactly as before, so nothing regresses.
* **Zoom (§33).** The surface binds the screen's existing `ZoomScale` / `IsFitToViewport` and the
  same Zoom In / Out / Reset commands; there is no second zoom to learn.
* **Mode-switch alignment (§34).** One `Image` inside one `ScrollViewer`; a mode switch only
  re-points `Image.Source`. All three payloads share a canvas and therefore an extent, so the
  scroll offsets survive untouched — alignment holds by construction rather than by restoring a
  remembered position. The live run confirms zoom 2.0 and pan (0.75, 0.25) survive a switch.
* **Geometry (§35).** All three payloads are produced from one block-average grid in one pass, so
  they are the same size by construction, and that size is the TIFF's own canvas. A canvas beyond
  2048 px is reduced by the same integer factor for all three and *says so* — the accepted Epic
  11200 Part C1 convention (`IsDownsampledForDisplay`, "reduced for display"), rather than holding
  three 59 MB buffers for a 3307 × 4474 output. `PixelWidth`/`PixelHeight` always report the TIFF.
* **Inspection background (§13).** Deliberately none. A production TIFF is opaque by contract — the
  fifth sample is ink, not alpha — so a checkerboard would suggest a transparency the file cannot
  express.

---

## 11. Keyboard, UIA, accessibility and localisation (§29–§32)

Stable `AutomationId`s, never localised:

```text
Session.TiffReviewColour      Session.TiffReviewWhiteInk     Session.TiffReviewOverlay
Session.TiffReviewImage       Session.TiffReviewSurface      Session.TiffReviewLegend
Session.TiffReviewDetail      Session.TiffReviewUnavailable  Session.TiffOutputPath
Session.TiffProductionMetadata      Session.TiffMetadata.<Key>
Session.TiffReviewZoomIn/ZoomOut/ResetZoom
```

Accessible names are the operator's own localised wording, and the image's name follows the mode:
"Colour preview", "White ink preview", "Colour and white ink overlay" — never a class name.

* Standard `RadioButton`s in one group: reachable by Tab, walked with the arrow keys, selectable
  through `ISelectionItemProvider`.
* The `ScrollViewer` was made a tab stop so a keyboard-only operator can pan inside a mode with the
  arrow keys and Page Up/Down.
* `A_keyboard_operator_reaches_the_modes_the_path_and_both_decisions` renders the real screen and
  requires all three modes focusable and enabled, the mode group, the output path and the image
  present as tab stops, Approve and Reject still reachable, and **no** element confining Tab
  (`Cycle`/`Contained`) — no new focus trap.
* `Selecting_White_ink_through_UI_Automation_shows_the_white_ink_image` drives the real
  `ISelectionItemProvider` on a rendered radio button — never `TiffReviewMode` directly — and
  requires the rendered `Image.Source` to actually change, then re-reads persistence to confirm
  nothing was decided. No coordinate clicks anywhere.
* Localisation: 33 new keys, en-US and zh-CN, all named explicitly in
  `LocalisationResourceTests.The_production_TIFF_review_strings_exist_in_both_languages`. Culture is
  pinned with `CultureInfo.CurrentUICulture` in every locale test; nothing depends on the
  workstation display language.

---

## 12. Restart, multiple sizes and rejection (§37–§40)

* **Restart (§39).** `A_restart_rebuilds_the_review_without_another_Photoshop_run` builds a second
  service and view model over the same database and workspace: same `PrintOutput`, same SHA-256,
  byte-identical white-ink payload, identical metadata rows, all modes still working, Photoshop
  call count unchanged and the TIFF byte-identical. Mode selection itself is ephemeral (the
  original AC requires no persistence) and starts at Colour for a newly loaded payload.
* **Corrupt after restart (§40).** Covered above: fail closed, no stale cached preview.
* **Multiple sizes (§38).** Nothing is cached by session. The screen holds only the payload for the
  artefact currently on screen, and every payload carries the `PrintOutputId` and hash it was
  decoded from. `Each_output_size_is_reviewed_against_its_own_file` produces a second size with a
  different W1 pattern and requires a different path, a different hash, a different white-ink
  payload, the payload's id to be the *unreviewed* output's, and the first size's approved
  deliverable untouched.
* **Rejection (§37).** Unchanged. `Rejecting_after_using_the_modes_still_recycles_the_TIFF` cycles
  all three modes first, then rejects, and requires the exact Recycle Bin call, the file gone, the
  persisted `ReviewDecision`, `RetryRequired`, and the specialist surface to disappear with the
  bytes rather than linger over a gap.

---

## 13. Synthetic fixtures (§42)

`ProductionTiffFixture` gained `PixelWidth`, `PixelHeight` and `W1VerticalBands` (its 2 × 2 default
is unchanged, so every pre-existing caller behaves exactly as before). Fixtures exercised:

1. valid CMYK + W1 with an asymmetric `[255, 128, 0]` W1 pattern;
2. partial W1 coverage (the 128 band);
3. empty W1 — refused by the inspector, so no payload (`FifthSampleNonEmpty: false`);
4. wrong sample layout (4 and 6 samples) — refused, no payload;
5. modified-after-validation — refused with `RevisionIntegrityMismatch`;
6. multiple output sizes with different W1 patterns.

`SyntheticProductionTiffProcessor` (tests only) writes a **real** accepted production TIFF at the
projected geometry, so UI tests review a genuine separated-CMYK + W1 file rather than the ordinary
fake's copy of the input. `FakePhotoshopOutputProcessor` was deliberately left alone: every
pre-existing test keeps exercising exactly what it always did.

**No dependency change (§61).** No package was added. The existing `ProductionTiffInspector` was
extended to return the strip geometry it already parses.

---

## 14. Automated tests

| Suite | Tests | What it establishes |
| --- | --- | --- |
| `ProductionTiffReviewDecoderTests` | 14 | W1 per-region values, independence from colour, count agreement with the inspector, CMYK conversion vs. an independent codec, overlay behaviour, shared geometry, downsampling, hash mismatch, missing file, refused layouts, read-only, and the **real baseline TIFF** decoding into all three modes |
| `TiffFinalReviewModeTests` | 14 | Three modes over one canvas, W1 from the file's fifth sample, authority unchanged by mode switches, the full metadata block, effective DPI over four upstream shapes, mutated-file fail-closed, restart, multiple sizes, rejection, localisation |
| `TiffFinalReviewAccessibilityTests` | 8 | Rendered ids, localised names, mode-following accessible name, keyboard traversal with no focus trap, UIA selection changing the real image, metadata rows and path addressable |
| `ProductionTiffReviewServiceTests` | 5 | Payload identity, refusal for a non-output Revision, cross-session and invented ids, recycled output, and that asking advances nothing |
| `TiffReviewBoundaryTests` | 9 | No string parameters, hash in the decoder signature, no mutating types, byte payloads only, no TIFF parsing in the shell, shared surface untouched |
| `LocalisationResourceTests` | +2 | All 33 keys in both languages; the colour legend disclaims printed accuracy in both |
| `TiffFinalReviewWorkstationSmoke` | 1 | The live proof (opt-in) |

**53 new tests.**

---

## 15. Live final-review proof (§50–§54, §60)

`TiffFinalReviewWorkstationSmoke`, opt-in via `PRINTFLOW_TIFF_FINAL_REVIEW_SMOKE=1`, executed
successfully on 8 September 2026. Evidence written to
`D:\PrintFlowStudio\QA\SCRUM-11104\<token>\scrum-11104-final-review-evidence.json`.

**Note — what was real, and what was not.** Photoshop is **not installed** on this workstation
(`C:\Program Files\Adobe` holds Acrobat DC and Creative Cloud only). §60 permits reusing an already
generated validated TIFF when only review, decoder and UI changed, which is this slice exactly — so
the file under review is the frozen Epic 11000 baseline
`FIX-CUSTOMER-DESIGN-001_W1-1PX.tif`, produced by the validated production Action on the accepted
workstation. **No new Photoshop run was performed and none is claimed.** Every layer above the
bytes is the product's own: real `SessionService`, real SQLite repository and migrations, real
`FileWorkspace` under the real workspace root, real signed preset provider, real
`ProductionTiffInspector`, real review decoder, real `SessionViewModel`. The source is a synthetic
production design cut to the baseline TIFF's own geometry so the preparation and the file describe
the same output.

Recorded run:

```text
production TIFF      : FIX-CUSTOMER-DESIGN-001_W1-1PX.tif (real Photoshop output, Epic 11000 baseline)
synthetic source     : 3307x4474 px @ 300 ppi
reviewed PrintOutput : 01a07df2-6842-7777-8ce9-98745f452ad8
reviewed SHA-256     : D1E69C4108D4C1D6119DB11DE036F56555CDE4A064F23AF541E24E1DAC5EA412
output path          : ...\Working\...\PF_11104_..._280mm_CMYK_W.tif
TIFF pixels          : 3307x4474 @ 300x300 ppi
W1 ink samples       : 8228624
physical size        : 280.0 x 378.8 mm
effective source dpi : 300.0 x 300.0 PPI
  OutputFile    : PF_11104_20260908-101709-1D8E0AFB_280mm_CMYK_W.tif
  Pixels        : 3307 × 4474 px
  Physical      : 280.0 × 378.8 mm
  Resolution    : 300 × 300 PPI
  EffectiveDpi  : 300 × 300 PPI, from 3307 × 4474 source px
  Colour        : CMYK · 8-bit · 5 ink channels
  WhiteInk      : W1 validated · 1 px — ordinary artwork · 8,228,624 px of ink
  Preset        : printflow-workstation-v1 (6396FB4EB87F)
  Sha256        : D1E69C41…AC5EA412
RESULT               : final review verified, approved, and reconstructed after restart.
```

Independently verified inside the run, not read back from the view model:

* **§51 — W1.** `ProductionTiffInspector` re-inspected the file at the managed output path and
  reported `W1NonWhiteSampleCount = 8,228,624`; the White-ink metadata row states the same figure,
  and the decoder counted it over the TIFF's own fifth sample.
* **§52 — effective DPI.** Recomputed in the smoke from the *persisted* preparation:
  `3307 / (279.99 / 25.4) = 300.0` and `4474 / (378.80 / 25.4) = 300.0`, matched against the row.
* **§53 — path and hash.** The path on screen equals `IWorkspace.ResolveAbsolute(PrintOutput.File)`;
  hashing the bytes at that path yields `D1E69C41…AC5EA412`, equal to the reviewed SHA-256 and to
  the baseline file's own hash.
* **§50 — modes and viewport.** Colour → White ink → Overlay driven through the real view model,
  then zoom 2.0 and pan (0.75, 0.25) set and confirmed to survive a mode switch, then Approve and
  Complete; the output promoted to `Approved` with the hash unchanged.
* **§54 — restart.** A second service, repository, decoder and review service over the same
  database and workspace reconstructed the payload: same `PrintOutputId`, same SHA-256, same
  3307 × 4474 geometry, same W1 count, and the adapter's call count unchanged — no Photoshop call
  and no regeneration merely from reopening.

**§55, §56 honoured:** no Maintop automation, no physical print.

---

## 16. Build and full suite

```text
Build:  0 warnings, 0 errors   (dotnet build PrintFlowStudio.sln)

Full suite against final source:
  11,453 passed
       0 failed
       0 skipped
  (baseline 11,400 + 53 new tests)
```

No pre-existing test was weakened. Three accepted rules were honoured rather than relaxed:

* `PhotoshopWorkflowOutputBoundaryTests.Workflow_and_App_parse_no_TIFF_bytes` — the payload field
  was renamed to `InkChannelCount`;
* `PhotoshopTiffBoundaryTests.Workflow_and_App_cannot_name_the_C1_TIFF_types` — the Workflow port
  defines its own `DecodedTiffReview` and never names `ProductionTiffFacts`;
* `BannedApiEnforcementTests` — no `System.IO` reached a view model.

---

## 17. Jira reassessment

**SCRUM-11104 — Build TIFF Final Review Mode: previous audit PARTIAL → current FULL.**

| Original AC clause | Status |
| --- | --- |
| CMYK colour preview | ✔ separated-CMYK decoded from the TIFF's own samples |
| White-ink channel preview | ✔ the validated W1 fifth sample, inverted, region-verified |
| Colour-plus-white overlay | ✔ display-only marker with an unambiguous legend |
| Production metadata: millimetres | ✔ `Physical` row from the preparation projection |
| Production metadata: pixels | ✔ `Pixels` row from the decoded TIFF |
| Production metadata: effective DPI | ✔ `EffectiveDpi` row, bound to the plan's Revision |
| Production metadata: colour-setting or preset identifier | ✔ `Preset` row (id + manifest hash); version omitted as non-persisted |
| Production metadata: output path | ✔ `OutputPath` row plus a selectable full path |
| Approval binds to the exact PrintOutput hash | ✔ unchanged, and now also the binding the previews are decoded under |
| No successful automation silently completes the Session | ✔ unchanged |

One deviation from the AC's literal wording, recorded rather than glossed: the AC says "extend the
shared review module". §14 of this task's brief directs the opposite — keep the specialist surface
separate from the generic comparison, and do not turn the shared component into a TIFF parser. The
brief was followed: `SharedReviewSurface` is untouched and `TiffReviewSurface` sits beside it,
reusing the same zoom and viewport state. The operator-visible outcome the AC asks for is delivered
in full.

**SCRUM-11095 — Implement Effective-DPI Resolution Risk Rules: remains PARTIAL.** Its exact
original CSV row (Work Item 11402) was re-read:

> Implement sufficient, warning and blocking resolution states based on effective DPI and
> thresholds established through real print tests rather than invented values. Display millimetres,
> pixel dimensions, effective DPI and graphic bounds. A minor shortfall may warn, while a
> significant shortfall blocks output until the operator resolves the source or dimensions.

**Exact delta, and no more.** The historical row said "effective DPI is never computed or displayed
anywhere in the product (no such concept exists in the source)". That sentence is now false: the
concept exists (`TiffEffectiveResolution`), and millimetres, pixel dimensions and effective DPI are
displayed together at final review. Still open, and unchanged:

* effective DPI is shown **only at final review**, after the TIFF exists — not at the Print
  Dimensions decision, where the AC's preflight intent sits;
* **graphic bounds are still not displayed anywhere**;
* the sufficient / warning / blocking threshold bands remain **superseded** by explicit enlargement
  authority, deliberately and unchanged — no print-test thresholds were ever captured
  (SCRUM-11065/11066 waived), and inventing bands is still refused.

SCRUM-11095 therefore stays **PARTIAL**; only its "effective DPI is never computed or displayed"
half is retired.

**Not reassessed and not marked FULL:** SCRUM-11132, SCRUM-11134, SCRUM-11135, SCRUM-11105 and the
parent Epic SCRUM-11093.

---

## 18. Git state

Work was done only on `D:\Repositories\printflow-Studio`, branch `master`. No branch, worktree,
alternate clone, amend, rebase or push. Unrelated files untouched. No AI-attribution trailer.

**Product source**

```text
src/PrintFlow.Workflow/Ports/ITiffReviewDecoder.cs                              (new)
src/PrintFlow.Workflow/Services/IProductionTiffReviewService.cs                 (new)
src/PrintFlow.Workflow/Services/ProductionTiffReviewService.cs                  (new)
src/PrintFlow.Infrastructure/Adapters/Photoshop/ProductionTiffReviewDecoder.cs  (new)
src/PrintFlow.Infrastructure/Adapters/Photoshop/ProductionTiffInspector.cs      (raster + InspectForReview)
src/PrintFlow.App/ViewModels/SessionViewModel.TiffReview.cs                     (new)
src/PrintFlow.App/Views/TiffReviewSurface.cs                                    (new)
src/PrintFlow.App/ViewModels/SessionViewModel.cs                                (seam + load/clear)
src/PrintFlow.App/Views/SessionScreenView.xaml                                  (surface + metadata + path)
src/PrintFlow.App/Composition/ServiceRegistration.cs                            (registration)
src/PrintFlow.App/Resources/Strings.cs, Strings.resx, Strings.zh-CN.resx        (33 keys × 2 languages)
```

**Tests**

```text
tests/PrintFlow.Tests/Integration/Automation/ProductionTiffReviewDecoderTests.cs   (new)
tests/PrintFlow.Tests/Integration/Ui/TiffFinalReviewModeTests.cs                   (new)
tests/PrintFlow.Tests/Integration/Ui/TiffFinalReviewAccessibilityTests.cs          (new)
tests/PrintFlow.Tests/Integration/Persistence/ProductionTiffReviewServiceTests.cs  (new)
tests/PrintFlow.Tests/Architecture/TiffReviewBoundaryTests.cs                      (new)
tests/PrintFlow.Tests/Smoke/TiffFinalReviewWorkstationSmoke.cs                     (new)
tests/PrintFlow.Tests/Fixtures/SyntheticProductionTiffProcessor.cs                 (new)
tests/PrintFlow.Tests/Fixtures/TiffFinalReviewFixture.cs                           (new)
tests/PrintFlow.Tests/Fixtures/ProductionTiffFixture.cs                            (size + W1 bands)
tests/PrintFlow.Tests/Fixtures/SessionServiceHarness.cs, HomeScreenHarness.cs      (seam wiring)
tests/PrintFlow.Tests/Architecture/LocalisationResourceTests.cs                    (+2 tests)
… plus five existing files updated for the new constructor parameter
```

---

## 19. Verdict

**PASS WITH NOTES — SCRUM-11104 TIFF FINAL REVIEW VERIFIED**

The notes, all recorded above rather than hidden:

1. The live proof reuses the real Photoshop-produced Epic 11000 baseline TIFF under §60 because
   Photoshop is not installed on this workstation. No new Photoshop run was made and none is
   claimed.
2. The preset **version** is not shown because the `PrintOutput` row does not persist it; the
   preset id and manifest hash are shown instead.
3. The AC's "extend the shared review module" was delivered as a separate specialist surface, on
   the explicit instruction of §14 of this task.
4. Preview payloads for a canvas above 2048 px on the long edge are reduced for display — all three
   identically, and reported as reduced — following the accepted Epic 11200 Part C1 convention
   rather than holding full-resolution buffers for every mode.
