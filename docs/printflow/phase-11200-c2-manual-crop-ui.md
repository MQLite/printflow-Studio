# Epic 11200 Part C2 — Manual crop after `ManualCropRequired`

Deterministic alpha trimming refuses a file it cannot measure. Part C2 gives the operator the
way forward: draw a rectangle, and let the ordinary attempt/hash/review machinery treat the
result exactly as it treats an automatic one.

---

## 1. Eligibility

The rule lives in one place — `ManualCropEligibility.IsEligible(WorkflowSnapshot, attempts)`
(`src/PrintFlow.Workflow/Services/ManualCropEligibility.cs`) — and has exactly two readers:
`SessionView.CanManualCrop`, which decides whether the control is offered, and
`SessionService.ExecuteAsync`, which refuses the command. One predicate, so an offered button
and an accepted command cannot disagree.

A crop is legal when the session is `Active`, the current step is `Trim`, and:

| Step state      | Newest ended `Trim` attempt                      | Why                                           |
|-----------------|--------------------------------------------------|-----------------------------------------------|
| `Failed`        | failed with `ManualCropRequired`                  | no automatic crop is honest (§3)              |
| `Failed`        | a manual crop that produced nothing               | the rectangle was refused; draw another (§21) |
| `RetryRequired` | a manual crop that succeeded (and was rejected)   | the corrective path stays manual (§21)        |

Everything else is refused: an unrelated Trim failure, a Trim awaiting an ordinary review, a
successful Trim, a **rejected deterministic** trim (that is an ordinary Retry), a completed
session, and any step other than Trim.

The rule is split across two layers on purpose. `WorkflowEngine` owns the half a pure reducer
can answer — session Active, Trim is current, the step state permits it, the rectangle is
non-empty — because a `WorkflowSnapshot` deliberately carries no attempt history. The
application layer owns the historical half. Both run on every command.

"Latest attempt" is ordered by `RetrySequence` first and only then by `EndedAtUtc`: the
sequence is the step's own attempt counter and cannot tie, whereas two attempts finishing in
the same clock tick can — which is not theoretical, since a refusal and the crop that follows
it easily share a timestamp.

**Manual crop is not `HandOff`.** The session stays `Active`, no automation lock is taken, and
the operator continues inside PrintFlow (§4).

---

## 2. Crop geometry

The rectangle is a `TrimBounds` in **source-image pixel coordinates**, reusing the existing
half-open convention (`Left`, `Top`, `RightExclusive`, `BottomExclusive`) rather than inventing
a second rectangle type — a conversion at the seam is where an off-by-one clips a column of
artwork.

`CropSurfaceLayout` (`src/PrintFlow.App/ViewModels/CropSurfaceLayout.cs`) is a pure value that
maps between three spaces:

- **surface** — device-independent pixels on the scrollable content area the operator drags in;
- **payload** — pixels of the decoded preview, which is *not* the source when the preview was
  reduced for display (Part C1 §6);
- **source** — pixels of the artefact, the only space a crop is recorded in.

It accounts for fit-to-viewport scaling, the preview's own display reduction, the current zoom
and letterboxing. There is no scroll-offset term because the overlay lives *inside* the scrolled
content, so scrolling is handled by construction rather than by a number that could be supplied
wrongly.

Clamping and rounding: the drag is intersected with the canvas **before** rounding, so a drag
that overlaps the image yields the overlap and one that misses entirely is refused rather than
clamped to a sliver nobody drew. Rounding is outward (`floor`/`ceil`) so every pixel the
rectangle touched survives, with a `1e-6` source-pixel tolerance on each edge — see §9.

The final rectangle is validated three times, independently: in the helper, in the engine
(`IsEmpty`), and in the processor (`FitsWithin` the decoded canvas).

---

## 3. Manual crop processor

`IManualCropProcessor` (`src/PrintFlow.Workflow/Ports/IManualCropProcessor.cs`) takes a managed
input reference, a controlled output reference and a source-pixel `TrimBounds`. No path crosses
the seam.

`WicManualCropProcessor` (`src/PrintFlow.Infrastructure/Imaging/WicManualCropProcessor.cs`)
decodes with `PreservePixelFormat`, cuts the rectangle with `CroppedBitmap`, and writes a PNG.
It does nothing else: no alpha analysis, no segmentation, no colour correction, no resize, no
enhancement, no external application. Colour pixels, alpha, bit depth and DPI travel with the
frame because the crop runs on it in its own pixel format.

The one behaviour that deliberately differs from `DeterministicAlphaTrimProcessor`: **a source
with no alpha channel is cropped normally.** That case is the reason manual crop exists.

A rectangle that does not fit the decoded canvas is **refused, not clamped** — the caller
already clamped it to the canvas it was showing, so a mismatch here means the two disagree
about the image, and shrinking it silently would hide that.

---

## 4. Workflow transition

```
Trim Failed / ManualCropRequired
  → WorkflowCommand.SubmitManualCrop(StepKind.Trim, TrimBounds)
  → engine: CreateWorkingCopy + RecordAttemptStarted + RunManualCrop, step → Processing
  → SessionService: attempt row committed, then the crop, then FileInspector + SHA-256
  → AttemptSucceeded → Trim step → ReviewRequired
```

`WorkflowEffect.RunManualCrop` is a separate effect from `RunAdapter` because it carries the one
thing an adapter call never does: geometry a human chose.

`SessionService.RunAdapterBackedStepAsync` was generalised to `RunProducingStepAsync`, taking a
`ProducingWork` value that normalises "adapter call" and "manual crop" onto one shape. A second
near-copy would have been a second place for "record the attempt before the file work" and "hash
before committing" to drift. The environment gate and automation lock are still taken only for
genuinely adapter-backed steps, so a manual crop — like the deterministic trim — takes neither.

The UI calls `ISessionService.ExecuteAsync` and never the processor.

---

## 5. `ManualImport` Revision semantics

A `Revision(OperationKind.ManualImport)` is created **only after** a real cropped file exists and
passes the ordinary `FileInspector` → readability → metadata → SHA-256 pipeline. Selecting a
rectangle creates nothing.

Lineage: `ManualImport.SourceRevisionId` is the Revision the operator actually cropped — the
Trim step's upstream, resolved through `UpstreamRevisionOf(Trim)`. The failed deterministic
attempt is not a Revision and has none, so the derivation reads truthfully as
*upstream artwork → ManualImport Revision*.

Attempts: a fresh `ProcessingAttempt` with processor identity `internal-manual-crop-v1`. The
failed `internal-alpha-trim-v1` attempt is never reused and never erased.

Output file: `manual-crop.png`, inside the attempt's own `Working\<attemptId>\` directory — which
is what makes a rejected crop and its replacement structurally incapable of overwriting each
other.

---

## 6. Before/After review

Unchanged from C1 and reused as-is. `SessionView.UpstreamArtefact` resolves through
`Revision.SourceRevisionId`, so a manual crop pairs Before = the Revision that was cropped,
After = the `ManualImport` Revision, with no filename special-casing anywhere.

Approval remains exact-hash-bound: `Approve(displayed hash)` → `Trim` step `Approved` → the
workflow continues. There is no "Manual Approved" state, and nothing auto-approves a crop.

`RevisionIntegrityMismatch` still refuses approval of a mutated `ManualImport`; the preview
remains non-authoritative.

---

## 7. Reject / retry behaviour

Rejecting a crop preserves the `ManualImport` Revision, its file, the `ReviewDecision`, the
source Revision and the failed deterministic attempt. The next crop is a fresh attempt with a
fresh output.

After a rejection the operator stays on the manual path — `CanManualCrop` remains true and no
`Retry` is needed first — because the deterministic trim has already said it cannot help with
this file. Ordinary Retry semantics are untouched everywhere else, including for a rejected
*automatic* trim, which remains an ordinary Retry case.

Cancel (§22) is structurally incapable of leaving a trace: `CancelManualCropCommand` touches no
file, issues no command and reaches no service.

---

## 8. UI, tests and smoke

**Interaction (§8), stated:** dragging on the crop surface draws a rectangle and nothing else.
There is no drag-to-pan to disable — panning has always been the scrollbars (C1 §13) — and a
magnified image is navigated with them while the selection stays over the same artwork, because
it is stored in source pixels and re-projected on every zoom, resize or scroll. Drawing a new
rectangle replaces the old one; there are no transform handles, and no spacebar modifier.

Tests added:

| Area | File | Covers |
|---|---|---|
| Geometry | `Unit/Ui/CropSurfaceLayoutTests.cs` (26) | fit, 200%, non-square viewport, letterboxing, both boundaries, reduced preview, refusals, round trip |
| Processor | `Integration/Files/ManualCropProcessorTests.cs` (11) | transparent PNG exact pixels, **opaque RGB**, JPEG, DPI, bounds, refusals, cancellation |
| Workflow | `Integration/Persistence/ManualCropStepTests.cs` (12) | §28 acceptance scenario, §29 audit, §30 reject-then-crop, §13 guards, §16 lineage, §26 hash, §36 source untouched |
| Screen | `Integration/Ui/ManualCropUiTests.cs` (14) | §31 list in full, §22 cancel, §25 stale preview, §26 hash |
| Render | `ViewRenderingTests.cs` (+3) | crop surface fitted and magnified, refused selection, crop result — no binding errors |
| Smoke | `SessionSmokeTests.Smoke_I` | the whole journey through the real composed graph |

**Human smoke (§35): no human visual judgement occurred.** This build has no interactive
desktop. What stands in for it is `Smoke_I_a_no_alpha_photo_is_cropped_by_hand_and_completes`,
which walks Home → PREPARE_ASSET → confirm → skip → skip → automatic refusal → crop → review →
approve → export → complete through the graph `ApplicationStartup` composes, including the real
`WicManualCropProcessor` resolved from the container. The closest automated answer to "does the
selection align with the image when fitted and zoomed" is in that test: the crop surface is
arranged at a real size and the same drag is mapped through the same `CropSurfaceLayout` the
view uses, at fit and at 200%, and must land on the same source rectangle both times. **Nobody
has confirmed by eye that the rectangle sits over the artwork.**

Localisation: eight new keys in English and zh-CN, plus a correction to
`Session_ManualCropRequiredNotice`, which still told the operator manual crop would arrive in a
later workflow step. Parity is enforced by `LocalisationResourceTests`.

---

## 9. Defects found and fixed

**Outward rounding over-reached on a pixel boundary.** A drag whose edge sat exactly on a pixel
boundary came back from the display round trip as `9.000000000001`, and `Ceiling` turned that
into `10` — a whole extra column of the operator's artwork, on a rectangle that looked exactly
right. Fixed with a `1e-6` source-pixel tolerance on each edge (`CropSurfaceLayout.EdgeTolerance`),
far below anything a mouse can express and far above the error the conversion accumulates.
Regression test: `An_edge_on_a_pixel_boundary_does_not_gain_a_row_at_an_awkward_scale`, at
scales 1.25³, 83⅓, 0.1 and 3.7.

**A failed manual crop sent the operator back through the alpha trim.** The first eligibility
rule covered only `ManualCropRequired` and a rejected crop. A crop that failed for another
reason — a rectangle the processor refused — left the step `Failed` with a different code, so the
crop control disappeared and the only route on was Retry → Run Step → the deterministic trim
refusing again. Found by
`A_rectangle_outside_the_source_fails_the_attempt_and_creates_no_Revision`; fixed by treating
"the newest ended Trim attempt was a manual crop" as an opening in its own right.

**Test-infrastructure defects (no product impact):** a rendered `Button` stayed subscribed to the
view model's `CanExecuteChanged` after its STA render thread ended, crashing the test host when a
later command completed — fixed by detaching the DataContext at the end of `WpfRendering.Render`.
And a rendered `ItemsControl` binds `PreviewPanes` to the render thread's dispatcher permanently,
so `Smoke_I` drives one screen and renders throwaway second screens.

---

## 10. Deferred to C3

Untouched by this slice, as instructed: `ReturnToStep` UI, Trim margin controls, automatic margin
policy, a fake-scenario selector, annotation, colour tools, AI segmentation, real Meitu, real
Photoshop, and any general freeform image editor.

Also deferred, and worth naming: resize handles on the crop rectangle (drawing a new one is the
whole MVP interaction), and any crop surface for a step other than Trim.

---

## 11. Git state

Branch `master`, ahead of `origin/master`. Nothing pushed. No generated crop image, runtime
database, screenshot, preview cache or synthetic smoke asset is staged — every test image is
generated at test time under the OS temp directory and every test workspace is disposed.

Gates, from an actual run:

```
dotnet restore --locked-mode   ok
dotnet build                   0 warnings, 0 errors
dotnet test                    6039 passed, 0 failed, 0 skipped
dotnet list package --vulnerable --include-transitive   no vulnerable packages
```

Baseline at the start of the slice was 5679 passing.
