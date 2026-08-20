# Epic 11200 Part B — Deterministic Alpha Trim Core

Implementation slice. The Trim placeholder is gone; a real alpha-bound crop replaces it.
No image-review UI, no manual crop UI, no ReturnToStep.

---

## 1. Algorithm

```text
working input
  → WIC decode, first frame, PreservePixelFormat | IgnoreColorProfile, cache OnLoad
  → is the frame's own pixel format alpha-capable?   no  → ManualCropRequired
  → present as BGRA32 and copy one alpha byte per pixel, row by row
  → AlphaBounds.Compute(width, height, alphaPlane)   null → ManualCropRequired
  → TrimBounds.Expand(margin, width, height)         (clamped to the canvas)
  → CroppedBitmap on the original frame, in its own pixel format
  → PngBitmapEncoder → expected output path
```

`AlphaBounds.Compute` walks the plane once, tracking the first and last row containing any
alpha and the minimum/maximum column across those rows, then returns the half-open rectangle.
It is a pure function over `(int width, int height, ReadOnlySpan<byte> alpha)` living in
`PrintFlow.Domain.Trimming` — no file, no decoder, no workspace — which is what makes the
ten threshold and edge cases in §7 cheap to state exhaustively.

## 2. Alpha semantics

**The rule is exactly `alpha > 0`.** No opacity threshold exists anywhere in the slice, and
`AlphaBoundsTests.Alpha_of_1_counts_as_content` asserts values 1, 9, 10, 127, 128, 254 and
255 all count, precisely so a later "cleanup" to `> 10` or `> 128` fails a test rather than
quietly shaving the soft edge off every cut-out in the shop.

Premultiplied alpha is a non-issue because the scan reads the alpha byte itself.
Premultiplication changes how the *colour* channels are interpreted, never whether a pixel
has alpha, so `Pbgra32` and `Bgra32` produce identical bounds.

Alpha capability is decided from the frame's **stored** pixel format, before any conversion —
which is why the decode uses `PreservePixelFormat`. Without it WIC may hand back a converted
surface, and an RGB photograph would arrive as opaque BGRA32 and look like full-canvas
content. The three-valued answer is shared with `WicFileInspector` through the new
`WicPixelFormats` helper, so the facts recorded on a Revision and the decision the trim makes
cannot drift apart.

| `HasAlpha(format)` | Behaviour |
| --- | --- |
| `true` | Scan the alpha plane. |
| `false` | `ManualCropRequired`. No colour inference, ever. |
| `null` (e.g. indexed palettes) | Attempt the BGRA32 decode path and scan what it yields; if that fails, `ManualCropRequired`. |

## 3. Margins

`TrimMargin` carries its own `TrimMode`, set by the factory that built it — `Tight`,
`Uniform(px)`, `PerEdge(top, right, bottom, left)`. Mode and values therefore cannot
disagree, and a `TightCrop` holding a non-zero margin is not constructible. `default` is
`Tight`.

Margins are non-negative; a negative value throws rather than meaning "crop further in".
Trimming into the operator's artwork is destructive and has to be asked for in words, not in
the sign of a number.

Margins are applied **after** the content bounds are computed, and clamped:

```text
content left = 5, left margin = 10   →  left = 0        (never −5)
content right = 45, right margin = 10, canvas = 50  →  right = 50
uniform margin of int.MaxValue       →  the whole canvas, not an overflow
```

Clamping happens inside `TrimBounds.Expand`, which also refuses bounds that do not fit the
stated canvas — so no caller has to re-validate a rectangle before cropping to it.

## 4. WIC implementation

`PrintFlow.Infrastructure.Imaging.DeterministicAlphaTrimProcessor`. No ImageSharp; no new
package of any kind.

- **Colour is never altered.** The crop runs on the decoded frame *in its own pixel format*
  via `CroppedBitmap`, so a 16-bit-per-channel source stays 16-bit and the DPI travels with
  it. The BGRA32 conversion exists only to measure alpha and never reaches the encoder. Trim
  changes canvas extent and nothing else — a test asserts the output keeps the source's
  300 DPI.
- **Output is PNG**, always, because the artefact is a transparent cut-out. Never JPEG.
- **Memory**: alpha is copied row by row, so peak cost is one alpha plane (w × h bytes) plus
  one BGRA row, not a four-bytes-per-pixel copy of the whole image.
- **Threading**: the work runs on a thread-pool thread via `Task.Run`, because decoding and
  scanning a production-sized image is real CPU time and doing it inline would freeze the
  operator's window. Every bitmap is frozen, so none acquires an affinity to the thread it
  was built on.
- `BitmapCacheOption.OnLoad` releases the input's file handle before the sibling output is
  written.

## 5. SessionService integration

The `AdapterKind.Internal` branch of `PerformStepWorkAsync` no longer inspects the unchanged
working copy. It now checks `definition.Kind == StepKind.Trim` (rather than assuming every
future internal operation is a trim), calls `ITrimProcessor`, and inspects the produced file.

```text
upstream approved Revision
  → CreateWorkingCopyAsync (fresh Working\<attemptId>\)
  → ITrimProcessor.TrimAsync(input, sibling "trimmed.png", TrimMargin.Tight)
  → FileInspector  → SHA-256  → Revision(OperationKind.Trim)  → ReviewRequired
```

The output name is a fixed `trimmed.png` beside the working copy: this is an intermediate
artefact of one attempt, not a deliverable, so it is deliberately not coupled to the signed
preset's naming patterns. Operator-facing naming still applies at `ApprovedPngExport`.

Trim keeps `AdapterKind.Internal` and `IsAdapterBacked = false`, so it takes **no** automation
lock and passes through **no** environment gate — asserted by
`TrimStepTests.Trim_never_acquires_the_external_automation_lock`. The attempt's `AdapterId`
is now the processor's own `internal-alpha-trim-v1` instead of the old
`internal-trim-placeholder-v1`, so every Revision records which algorithm produced it.

DI: `ITrimProcessor → DeterministicAlphaTrimProcessor`, registered unconditionally in
`ServiceRegistration` outside `RegisterAdapters` — there is no Fake/Production duality to
choose between for PrintFlow's own pixel work.

Review, Approve, Reject and Retry are untouched. No special Trim approval mechanism exists.

## 6. `ManualCropRequired` representation

The processor returns a **successful** `OperationResult` carrying
`TrimOutcome.ManualCropRequired` plus an English reason. "This image has no alpha" is a fact
the processor established, not a failure of the processor.

`SessionService` converts that outcome into a failed attempt with the new stable
`FailureCode.ManualCropRequired`, `IsRetryable: false`. Concretely, and asserted:

- **No `Revision` is written.** Not a Trim of the untrimmed working copy, and not a
  `ManualImport` of a file no human has edited yet — either would record work that did not
  happen.
- The step lands in `StepState.Failed`; the attempt row is retained with the reason.
- The session stays `Active`. Nothing is handed off on the operator's behalf.
- `IsRetryable: false` because the same bytes deterministically produce the same answer;
  pressing Retry cannot help.

The failure code carries an operator sentence in both `Strings.resx` and `Strings.zh-CN.resx`
and a `DisplayNames.Failure` arm, so the screen already shows a real message rather than the
raw code. That is the only shell change in the slice.

**How Part C will consume it.** The manual-crop surface has one stable thing to route on: a
`Trim` step in `StepState.Failed` whose latest attempt's failure code is
`ManualCropRequired`. From there Part C can offer the operator a crop, and the file they
produce enters through the existing `OperationKind.ManualImport` concept — which stays
unused here precisely because no human-edited file exists yet.

## 7. Tests

90 new tests; suite 5546 → 5636.

| File | Covers |
| --- | --- |
| `Unit/Trimming/AlphaBoundsTests` | Centre pixel; every corner and edge midpoint; transparent border; sparse disconnected pixels; lone edge pixel; full opaque canvas; fully transparent canvas; 1-px-wide and 1-px-tall content; alpha 1/9/10/127/128/254/255 all count; alpha 0 never counts; plane-length and dimension contracts. |
| `Unit/Trimming/TrimMarginTests` | Tight = exact content; uniform; edge-specific; clamp top/left; clamp bottom/right; margin larger than canvas; `int.MaxValue` clamps instead of overflowing; negative uniform and per-edge margins refused; mode travels with the margin. |
| `Unit/Trimming/TrimBoundsTests` | The half-open convention, `CoversCanvas`, `FitsWithin`, and the refusal of rectangles containing no pixel. |
| `Integration/Files/DeterministicTrimTests` | Cases A–E against real synthetic PNGs, plus margins end to end, DPI preservation, byte-identical repeat runs, missing input, and cancellation. |
| `Integration/Persistence/TrimStepTests` | §23 real `PREPARE_ASSET` flow, §24 skip fall-through, §25 reject/retry, and the manual-crop boundary at session level. |
| `Architecture/TrimBoundaryTests` | Domain trim types name no file/workspace/imaging type; the port is declared in Workflow and implemented only in Infrastructure; the processor's type graph cannot reach Meitu, Photoshop or the environment gate; Trim stays Internal and not adapter-backed; no view model holds trim types. |

Case results, as required by §22:

| Case | Input | Result |
| --- | --- | --- |
| A | 12×10, opaque block (3,2)–(7,6) | `Trimmed`, 5×5 output, valid PNG, alpha retained, every surviving pixel opaque |
| B | Content running to the left/top/bottom edges; and a lone (15,11) pixel | No clipping; bounds `[0,0→4,8)` and `[15,11→16,12)` |
| C | Alpha 255 edge to edge | `NoChangeRequired`, file still written and hashed |
| D | Fully transparent | `ManualCropRequired`, no file, no Revision |
| E | RGB PNG with no alpha; and a JPEG | `ManualCropRequired`, no colour-based guess |

Every image is generated at test time by WIC's own encoders. No fixture image, database or
generated output is committed.

"No AI or background-colour inference" is proven behaviourally by Case E rather than by a
source-text search — an RGB image with an obviously uniform border still refuses — and
structurally by the processor's type graph, which contains no adapter port at all.

## 8. Defects found

**One, in the audit surface rather than in behaviour.** `WicFileInspector` inferred alpha
from the pixel format WIC returned under `BitmapCreateOptions.DelayCreation`, and the trim
processor needed the same judgement. Two independent copies of that table would have been a
live contradiction waiting to happen — an inspector recording `HasAlpha = false` on a
Revision while the trim happily cropped the same file. Both now share `WicPixelFormats`,
and the duplicated private helpers were deleted from the inspector.

No defect was found in the Epic 11100 foundation. `WorkflowSnapshot.UpstreamRevisionOf`
already produced the correct input for Trim when both Meitu steps were skipped, with no
special case needed (§24).

Stale comments claiming the trim algorithm was still deferred were corrected in
`StepKind.Trim`, `OperationKind.Trim`, `AdapterKind.Internal`, `WorkflowCatalog` and
`WorkflowShapeTests`.

## 9. Deferred UI scope

Untouched, exactly as instructed: image preview, image bytes into WPF, checkerboard,
before/after comparison, zoom/pan, manual crop UI, the `ManualImport` workflow from the UI,
ReturnToStep, trim margin controls, real Meitu, real Photoshop.

The trim margin seam exists and is exercised by tests, but the running application always
passes `TrimMargin.Tight` — there is no control to set anything else yet, and none was added.

## 10. Git state

Branch `master`, ahead of `origin/master`. No history rewritten, no force push, nothing
pushed. Working tree contains source and test changes only.

New files:

```text
src/PrintFlow.Domain/Trimming/TrimEnums.cs
src/PrintFlow.Domain/Trimming/TrimBounds.cs
src/PrintFlow.Domain/Trimming/TrimMargin.cs
src/PrintFlow.Domain/Trimming/AlphaBounds.cs
src/PrintFlow.Workflow/Ports/ITrimProcessor.cs
src/PrintFlow.Infrastructure/Imaging/DeterministicAlphaTrimProcessor.cs
src/PrintFlow.Infrastructure/Imaging/WicPixelFormats.cs
tests/PrintFlow.Tests/Unit/Trimming/{AlphaBounds,TrimBounds,TrimMargin}Tests.cs
tests/PrintFlow.Tests/Integration/Files/DeterministicTrimTests.cs
tests/PrintFlow.Tests/Integration/Persistence/TrimStepTests.cs
tests/PrintFlow.Tests/Architecture/TrimBoundaryTests.cs
```

Modified: `SessionService`, `FailureCode`, `WicFileInspector`, `ServiceRegistration`,
`DisplayNames`, `Strings.cs`/`.resx`/`.zh-CN.resx`, the workflow/step/operation doc comments
listed in §8, and four test files (harness, synthetic images, the failure-code name list, one
`SessionService` construction site).

## 11. Gates

| Gate | Result |
| --- | --- |
| `dotnet restore --locked-mode` | clean, no lock file changed |
| `dotnet build` | 0 warnings, 0 errors |
| `dotnet test` | 5636 passed, 0 failed, 0 skipped |
| `dotnet list package --vulnerable --include-transitive` | no vulnerable packages in any project |

Baseline before the slice was 5546 passed / 0 warnings / 0 errors / no vulnerable packages,
confirmed at preflight.

---

**11200-B PASS — READY FOR IMAGE REVIEW UI**
