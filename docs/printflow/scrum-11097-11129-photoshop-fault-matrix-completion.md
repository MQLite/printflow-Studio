# SCRUM-11097 / SCRUM-11129 — Photoshop fault simulation and validation matrix completion

*10 September 2026. Local branch `master`, working from `456cb842b0b5c87a31b9ccdd562151ebd2c392aa`.
No Photoshop, Meitu or Maintop process was launched for any part of this slice. No push, no
deploy, no Jira service mutation.*

---

## 1. Exact original acceptance criteria

Quoted verbatim from `C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv`, re-read
for this slice. The CSV's own Work Item IDs are given beside the SCRUM keys.

**SCRUM-11097 / Work Item 11404 — Build Deterministic Fake Photoshop Adapter** (parent 11400,
High, 3 points, labels `printflow,mvp,photoshop,fake-adapter,testing,simulation`):

> Implement a test Adapter capable of simulating a valid TIFF, missing white channel, wrong colour
> mode, wrong dimensions, incorrect output metadata, export failure, timeout, interruption and
> unknown dialog without launching Photoshop. Tests must observe the same interface used by
> production and avoid asserting click order.

**SCRUM-11129 / Work Item 11705 — Execute the Photoshop Adapter Failure and Validation Matrix**
(parent 11700, High, 5 points, labels `printflow,mvp,qa,photoshop,failure-matrix,tiff-validation`):

> Test Photoshop outcomes including successful TIFF, missing white channel, wrong colour mode,
> wrong physical or pixel dimensions, incorrect DPI, invalid/unreadable output, timeout,
> interruption and unknown dialog. Confirm invalid outputs cannot enter final approval and all
> retries start from a clean approved upstream source.

**SCRUM-11103 / Work Item 11410 — Validate Generated Production TIFF** (parent 11400), the
validation authority both of the above are measured against:

> After export, verify that the TIFF exists, size is stable, reopens fully, matches target physical
> and pixel dimensions at 300 DPI, has no unexpected aspect-ratio change, uses expected CMYK mode,
> contains the expected white-ink channel, matches validated bit depth and compression, and does
> not contain an obviously empty or abnormal full-canvas white channel. Calculate and persist the
> output hash before final review.

**SCRUM-11096 / Work Item 11403 — Define the Photoshop Output Adapter Contract** (parent 11400),
the seam both adapters are required to be observed through:

> Define a small Photoshop module interface accepting only an approved input, target physical
> dimensions, production-preset identifier and output name and returning a validated PrintOutput or
> structured failure. Keep Photoshop selectors, Actions, dialogs, save steps, reopen logic and UI
> automation hidden from the workflow layer.

**Parent SCRUM-11093 / Work Item 11400 — Generate and Validate Production TIFF Outputs with
Photoshop** (Epic):

> Implement physical print sizing and the replaceable Photoshop Output Adapter used by the two
> production workflows. PrintFlow calculates locked-aspect-ratio dimensions at fixed 300 DPI,
> handles supported PNG/JPEG/PSD/single-page-PDF inputs, detects existing white-ink channels where
> relevant, invokes the validated Photoshop production Action and current confirmed colour
> settings, and validates the generated TIFF before final review. One approved design may generate
> multiple independently reviewed sizes; PrintFlow must never silently stretch artwork, choose a
> page from a multi-page PDF, overwrite outputs or treat an invalid TIFF as production-ready.

---

## 2. Pre-change matrix

Established by inspecting the source before any edit. "Correct seam" means: reachable through
`IPhotoshopOutputProcessor`, the port `ProductionPhotoshopOutputProcessor` implements and
`SessionService` consumes.

| AC case | Fake capability before | Inspector capability before | Workflow/integration proof before | Actual gap | Closure evidence added |
|---|---|---|---|---|---|
| Valid TIFF | **No.** `FakeAdapterScenarioKind.Succeed` copied the approved input — a PNG named `.tif`. Never a production TIFF | `ProductionTiffInspectorTests.Minimal_accepted_fixture_proves_the_same_closed_contract` | `PhotoshopTiffWorkflowOutputTests` reached ReviewRequired on the copied PNG; `RetentionCleanupTests` used the test-side `SyntheticProductionTiffProcessor` | Fake could not emit an accepted production TIFF at all | `FakePhotoshopTiffOutput.ValidTiff`; `FakePhotoshopStructuralFaultTests.A_valid_fake_TIFF_is_accepted_by_the_real_inspector_at_the_projected_geometry`; `PhotoshopFaultMatrixTests.A_valid_fake_TIFF_reaches_review_and_binds_final_approval_to_its_own_hash` |
| Missing white channel | **No** | Yes — `InvalidContracts` row `SamplesPerPixel: 4` | Inspector fixture only | Not reachable through the fake | `FakePhotoshopTiffOutput.MissingWhiteChannel` + both new test files |
| Empty white channel | **No** | Yes — `FifthSampleNonEmpty: false` | Inspector fixture only | Not reachable through the fake | `FakePhotoshopTiffOutput.EmptyWhiteChannel` |
| Wrong colour mode | **No** | **No.** The fixture hard-coded PhotometricInterpretation 5; the inspector's check at `ProductionTiffInspector.cs:139` had no fixture that could exercise it | None | Neither fake nor fixture could produce a non-CMYK production TIFF | `FakeProductionTiffOptions.PhotometricInterpretation`; `FakePhotoshopTiffOutput.WrongColourMode`; new `ProductionTiffInspectorTests` theory row |
| Wrong pixel dimensions | **No** | **No.** `ProductionTiffInspector` is deliberately preparation-blind; it validates absolute facts only | **The recorded gap.** The only preparation-vs-pixels comparison in the Product was `GuardedPhotoshopDocumentPreparer.cs:491` — the *pre-save read-back of the Photoshop document*. `GuardedPhotoshopTiffSaver.cs:244` then compared the saved TIFF to that document, never to the preparation | No authority compared **saved bytes** against the expected preparation; unreachable at all without Photoshop | `ProductionTiffPreparationMatch`; `FakePhotoshopTiffOutput.WrongPixelWidth` / `WrongPixelHeight`; `The_wrong_dimension_TIFF_is_otherwise_a_fully_accepted_production_file` |
| Wrong physical dimensions | **No** | Indirect (300 PPI contract) | None | No stated Product interpretation of the clause | Section 6 below; `An_exactly_correct_grid_at_the_wrong_resolution_is_still_the_wrong_print` |
| Incorrect DPI | **No** | Yes — `Dpi: 72` | Inspector fixture only | Not reachable through the fake | `FakePhotoshopTiffOutput.IncorrectDpi` |
| Incorrect output metadata | **No** | Yes — `Compression: 5`, `LayerCompression: 2`, `PlanarConfiguration: 2`, `ExtraSample: 2`, `LittleEndian: false`, `IncludePyramidIfd: true`, `ChannelName: "White"` | Inspector fixture only | Not reachable through the fake | `FakePhotoshopTiffOutput.IncorrectMetadata` and `IncorrectChannelMetadata` |
| Export failure | Yes — `FakeAdapterScenario.FailWith` | n/a | `PhotoshopTiffWorkflowOutputTests` §17 theory | **None** | Re-covered in the single matrix, not re-implemented |
| Timeout | Yes — `FakeAdapterScenarioKind.Timeout` | n/a | Partial: not in the workflow theory | Matrix completeness only | `PhotoshopFaultMatrixTests` behavioural theory |
| Interruption | Yes — `HangUntilCancelled`, `ReportPhaseAndWaitForStop` | n/a | `Cancellation_before_the_success_commit_creates_no_Revision` | **None** | Re-covered as the matrix's interruption row |
| Unknown dialog | Yes — `FailWith(PhotoshopBlockingDialog)` | n/a | Not in the workflow theory | Matrix completeness only | `PhotoshopFaultMatrixTests` behavioural theory |
| Missing output | Yes — `ProduceMissingFile` | n/a | `PhotoshopTiffWorkflowOutputTests` §17 theory | **None** | Re-covered in the matrix |
| Invalid/unreadable output | Yes — `ProduceUnreadableFile` (zero bytes) | Yes | `PhotoshopTiffWorkflowOutputTests` §17 theory | **None** | Re-covered in the matrix |
| Invalid output cannot reach final approval | Partial: attempt state asserted, approval never attempted | n/a | `A_failed_or_invalid_output_creates_no_Revision_and_no_ReviewRequired` stopped at the attempt row | No test actually tried to approve after an invalid run | `AssertNothingReviewableAsync` attempts approval for **every** matrix row |
| Retry from clean approved upstream | Yes, for Meitu (`RetryAndReviewTests`) | n/a | `Retry_uses_a_new_attempt_a_new_Working_directory_and_a_new_output_path` (adapter-failure only, no output on disk) | No retry test after a **structural output** failure, where a bad TIFF exists to be wrongly inherited | `A_retry_after_a_structural_failure_starts_from_the_clean_approved_upstream_Revision` |

Cases marked "**None**" in the gap column were already genuinely covered through the correct seam
and were not reimplemented. They appear in the new matrix because SCRUM-11129 asks for one
accounting of all nine outcomes, not because they were missing.

---

## 3. Fake adapter architecture

The Fake Photoshop adapter is not a test double living in the test project. It is a real product
mode: `ServiceRegistration` registers `FakePhotoshopOutputProcessor` for the `Fake` branch, and
`ProductionActivationBoundaryTests.There_is_exactly_one_production_adapter_per_port` asserts these
are the only two implementations of `IPhotoshopOutputProcessor` in the shipped graph. That is why
the work below is in `src/`, not in `tests/`.

```
SessionService (Workflow)
      |  IPhotoshopOutputProcessor.GenerateAsync(PhotoshopRequest, ct)
      |        -> OperationResult<AdapterOutput>
      +-- ProductionPhotoshopOutputProcessor        FakePhotoshopOutputProcessor
            |  open / prepare / W1 / save                 |  write bytes
            |                                             |
            |  GuardedPhotoshopDocumentPreparer           |
            |     document pixels == preparation          |
            |  GuardedPhotoshopTiffSaver                  |
            |     ProductionTiffInspector  <-- shared --> ProductionTiffInspector
            |     saved pixels == document                |    ProductionTiffPreparationMatch
            |  PhotoshopAdapterOutputFactory              |       saved pixels == preparation
            v                                             v
        OperationResult<AdapterOutput>              OperationResult<AdapterOutput>
```

`ProductionTiffInspector` is literally the same type on both sides. `ProductionTiffPreparationMatch`
is reached only from the fake, because production establishes the same fact transitively through the
Photoshop document it prepared; section 5 sets out why, and why forcing production through it would
break an accepted architectural boundary rather than tighten anything.

Both branches end at the same type, and `SessionService` cannot tell them apart. There is
deliberately **no** `ITestOnlyPhotoshopValidator`, no `FakeOnlySessionService`, no
`TestOnlyWorkflowCommand`, and no second route from a fake result to a Revision: the C2A boundary
test still asserts `new AdapterOutput` appears exactly once in `Adapters/Photoshop`, in
`PhotoshopAdapterOutputFactory`, and the fake constructs its own through the ordinary port return
just as it always did.

New and changed Product files:

| File | Change |
|---|---|
| `src/PrintFlow.Infrastructure/Adapters/Fake/FakeProductionTiff.cs` | **New.** The one deterministic production-TIFF encoder, relocated out of the test project so the shipped fake can write real TIFF bytes. Adds a `PhotometricInterpretation` option |
| `src/PrintFlow.Infrastructure/Adapters/Fake/FakePhotoshopTiffOutput.cs` | **New.** The closed output-class vocabulary |
| `src/PrintFlow.Infrastructure/Adapters/Fake/FakePhotoshopOutputProcessor.cs` | `SetTiffOutput(...)`; a `WriteProductionTiff` path that writes then validates |
| `src/PrintFlow.Infrastructure/Adapters/Photoshop/ProductionTiffPreparationMatch.cs` | **New.** Saved TIFF pixel grid vs. expected `PhotoshopPreparation`. Called by the Fake adapter only — see section 5 |
| `src/PrintFlow.Domain/Outputs/PrintDimensions.cs` | **New** public `MillimetresFromPixels`, the inverse of the existing public `PixelsFromMillimetres` |
| `src/PrintFlow.Domain/Outputs/FitWithinBounds.cs` | Private duplicate of the mm/px expression removed; calls the shared one |
| `src/PrintFlow.Domain/Outputs/TargetEdgePrintPreparationPlan.cs` | Same |

No `GuardedPhotoshopUiDriver`, `GuardedPhotoshopDocumentPreparer`, `GuardedPhotoshopW1Executor` or
`GuardedPhotoshopTiffSaver` change was required or made. See section 12.

---

## 4. Scenario vocabulary

The fake now has two **independent** scripted dimensions, because they answer independent
questions. Collapsing them would force every behavioural outcome to be restated once per output
class.

**Behavioural — `FakeAdapterScenario` (pre-existing, unchanged, shared with the Meitu fake):**
`Succeed`, `FailWith(code)`, `Timeout`, `ProduceUnreadableFile`, `ProduceMissingFile`,
`HangUntilCancelled`, `WaitForStopAt(phase)`.

**Output class — `FakePhotoshopTiffOutput` (new, Photoshop-specific):**

| Member | Bytes written | Refused by | Refusal text |
|---|---|---|---|
| `CopyApprovedInput` *(default)* | Copy of the approved input | — | accepted by the workflow, as before |
| `ValidTiff` | Accepted layout at the projected geometry | — | accepted |
| `MissingWhiteChannel` | 4 samples, no fifth ink in the raster | `ProductionTiffInspector` | "requires exactly five 8-bit samples" |
| `EmptyWhiteChannel` | 5 samples, W1 wholly blank | `ProductionTiffInspector` | "W1 production ink sample is wholly empty on disk" |
| `WrongColourMode` | PhotometricInterpretation 2 (RGB) | `ProductionTiffInspector` | "not separated CMYK (PhotometricInterpretation 5)" |
| `WrongPixelWidth` | Projected width + 1 px | `ProductionTiffPreparationMatch` | "not the immutable projected dimensions this preparation asked for" |
| `WrongPixelHeight` | Projected height + 1 px | `ProductionTiffPreparationMatch` | same |
| `IncorrectDpi` | Projected grid at 150 PPI | `ProductionTiffInspector` | "not exactly 300 pixels per inch" |
| `IncorrectMetadata` | Compression tag declares LZW | `ProductionTiffInspector` | "image compression is not None (TIFF value 1)" |
| `IncorrectChannelMetadata` | Spot channel named "White" | `ProductionTiffInspector` | "do not identify exactly one W1 channel as a spot colour" |

Every member other than `CopyApprovedInput` is built as `accepted with { one field changed }`, so a
structural fault is never accidentally also a geometry fault and vice versa. A test asserting a
specific refusal therefore cannot pass on the strength of a second, unintended deviation.

Three consequences of that one-field discipline are recorded rather than left to be rediscovered,
because each produces a file Photoshop itself could not emit:

- `MissingWhiteChannel` drops the fifth sample from the raster but leaves the Photoshop resource
  block in place, so the file claims a W1 spot channel it no longer has. Invisible to the outcome —
  the inspector checks the sample count before it reads any resource.
- `WrongColourMode` holds the sample count at five deliberately. A real RGB export would carry
  three samples and be refused one check earlier, which would leave the inspector's photometric
  branch with no scenario reaching it at all. Isolating the rule is the point; imitating an RGB
  export is not.
- `IncorrectMetadata` declares LZW over strips that are still written uncompressed, so a reader
  trusting the tag would fail to decode it. The inspector refuses the tag before any strip is
  decoded, which is precisely the "matches validated bit depth and compression" clause — but it is
  not a file a viewer would open happily. `IncorrectChannelMetadata` is the fault that is: every
  byte decodes, the geometry, resolution and ink are all right, and it is still not a file Maintop
  may be handed, because PrintFlow's white-underbase contract is a channel *named* W1.

`WrongPixelWidth` and `WrongPixelHeight` deviate **upward**. Subtracting a pixel needs a clamp at
1, and a clamp means a preparation projecting a one-pixel edge would silently be handed the
accepted geometry — a scenario named "wrong dimensions" that wrote a correct file and returned
success. Adding a pixel is always representable, so the scenario cannot invert. (This was an actual
defect in the first draft, found in review.)

`CopyApprovedInput` remains the default deliberately. Dozens of workflow tests depend on a Fake
success being a cheap byte-for-byte copy;
`The_default_output_class_still_copies_the_approved_input` is the regression that keeps it so.

Names were chosen to extend the repository's existing vocabulary rather than to mirror the task
brief's suggested list: the behavioural half already had `ProduceMissingFile`,
`ProduceUnreadableFile`, `Timeout`, `HangUntilCancelled` and `FailWith`, and duplicating those as
output classes (`MissingOutput`, `UnreadableOutput`, `ExportFailure`, `Timeout`, `Interrupted`,
`UnknownDialog`) would have created a second way to say the same thing.

---

## 5. The saved-TIFF wrong-pixel-dimension fixture

This is the substantive Product addition, and it closes the one gap the previous audit recorded.

**What was missing.** `ProductionTiffInspector` is preparation-blind by design: it answers "is this
an accepted separated-CMYK + W1 production TIFF at exactly 300 PPI", a question with no reference
to any particular job. The comparison against the job existed only inside the production UI
automation, split across two stages — `GuardedPhotoshopDocumentPreparer` refused a *document* whose
pixels were not the projected pixels, and `GuardedPhotoshopTiffSaver` refused a *saved TIFF* whose
pixels were not that document's. Transitively sound, but it means the Fake adapter — which has no
Photoshop document in the middle — had no way to reach the conclusion at all, and no single named
authority stated "the saved bytes are the geometry the workflow validated".

**What was added.** `ProductionTiffPreparationMatch.Check(ProductionTiffFacts, PhotoshopPreparation)`
returns the refusal a saved file earns against a preparation, or null. It compares the pixel grid
and nothing else: resolution is an absolute contract that `ProductionTiffInspector` owns and has
already enforced by the time this runs, so a resolution branch here would be unreachable code.

**Its only caller is the Fake adapter, and that is stated plainly rather than dressed up.** An
earlier draft of this document and of the code comments claimed the production TIFF save path uses
this rule too. It does not, and independent review caught the overclaim. `GuardedPhotoshopTiffSaver`
compares its saved bytes to the Photoshop document it prepared, and that document has already been
compared to the preparation by `GuardedPhotoshopDocumentPreparer`, so production needs no direct
comparison. Routing the guarded save path through this rule would mean handing it the preparation,
which its accepted B1B surface deliberately does not accept — `PhotoshopTiffBoundaryTests` asserts
that surface takes only factual prepared-document state and one managed reference. The shared type
`ProductionTiffInspector` is genuinely shared and unchanged; this one is the fake's, because only
the fake lacks the document in the middle.

The consequence is recorded too: the `expectedPixels` / `actualPixels` / `expectedMillimetres` /
`actualMillimetres` evidence keys appear on Fake-mode refusals only. A production run that produced
a wrong-sized TIFF fails earlier, in the preparer or the saver, with those adapters' own
diagnostics.

**Why it is a genuine mismatch and not a label.** The fake writes the file *first*, with one fact
deliberately wrong, and only then submits it. `The_wrong_dimension_TIFF_is_otherwise_a_fully_accepted_production_file`
proves the produced file passes every absolute check the inspector makes — it exists, it reopens,
PhotometricInterpretation 5, five 8-bit interleaved samples, uncompressed, ExtraSamples 0, a
Photoshop W1 spot resource with a non-empty channel, exactly 300 PPI — and is refused **solely**
because its pixel grid disagrees with the preparation. Given the fixture's projected 240 × 360 px,
the file on disk is 241 × 360 px.

Both axes are covered, so the comparison cannot be accidentally width-only:
`A_saved_TIFF_that_disagrees_on_either_axis_is_refused_against_the_preparation` is a two-row theory
asserting `actualPixels` is `241x360` for the width case and `240x361` for the height case, with
the matching millimetres. There is no combinatorial matrix beyond that, and the two geometry faults
are deliberately absent from the general structural theory so the scenario is not run twice.

The deviation is one pixel on purpose. A file at half the expected size would be caught by almost
any check; one pixel is the smallest disagreement that still has to be caught, and it is the shape
a real rounding or resample defect takes.

---

## 6. Physical-dimension interpretation

SCRUM-11129 names "wrong physical or pixel dimensions" and "incorrect DPI" as separate clauses, so
the first question was whether the Product has three distinct semantics or two.

**Established Product definition.** The millimetre/pixel relation at the fixed production
resolution had one public authority, `PrintDimensions.PixelsFromMillimetres`, plus three private
copies of its inverse expression (`FitWithinBounds.SourceMillimetres`,
`TargetEdgePrintPreparationPlan.AsRecordedDimensions`, and the derivation implied by stored
dimensions). Saved-TIFF validation needed a fourth. Rather than invent one, the inverse was made
public as `PrintDimensions.MillimetresFromPixels` and the two live private copies now call it.
`ProductionDpi` is fixed at 300 and is not operator-selectable.

**The three clauses, as this Product actually means them:**

| Clause | Meaning here | Authority | Refused by |
|---|---|---|---|
| Wrong pixel dimensions | The saved pixel grid is not the immutable grid the preparation projected | `PhotoshopPreparation.ProjectedPixelWidth/Height` | `ProductionTiffPreparationMatch` |
| Incorrect DPI | The stored resolution is not exactly 300 PPI in ResolutionUnit 2 | `PrintDimensions.ProductionDpi` | `ProductionTiffInspector` (absolute, preparation-blind) |
| Wrong physical dimensions | The pixel grid and resolution together resolve to a canvas that is not the expected preparation's | `PrintDimensions.MillimetresFromPixels` | **No third check.** It is entailed by the two above |

**PrintFlow has no independent physical-size assertion over a saved TIFF, and this is recorded as a
fact rather than treated as a gap.** At a fixed 300 PPI the pixel grid and the resolution together
*define* the physical canvas. A file whose pixels match the preparation and whose resolution is
exactly 300 PPI cannot have a wrong physical size; a file that fails either check already has one.
Adding a third validation engine would not catch a ninth kind of defect — it would restate one of
the two in millimetres and give a reader two numbers to reconcile.

The clause is therefore proven through the smallest non-duplicative matrix rather than a
manufactured third test name. Both routes to a wrong canvas are asserted over the same
240 × 360 px / 20.32 × 30.48 mm fixture:

- **Wrong grid, right resolution.** 241 × 360 px at 300 PPI. The refusal's context carries
  `expectedMillimetres = 20.32x30.48 mm` and `actualMillimetres = 20.4047x30.48 mm`; the height
  row carries `20.32x30.5647 mm`. The physical clause is visible in the evidence, per axis.
- **Right grid, wrong resolution.** 240 × 360 px at 150 PPI, which is twice the intended canvas.
  `An_exactly_correct_grid_at_the_wrong_resolution_is_still_the_wrong_print` reads the ImageWidth
  and ImageLength tags straight out of the refused file to prove the grid really is exactly right,
  then asserts the refusal carries **no** `expectedMillimetres` and no `actualPixels`.

That last pair of assertions is the point of the test, and it is what stops it duplicating the DPI
row of the structural theory: the two clauses are answered by different authorities. A wrong grid is
*relative* and is refused by `ProductionTiffPreparationMatch`, whose refusal carries the
millimetres. A wrong resolution is *absolute* and is refused by `ProductionTiffInspector` before any
preparation is in scope, so its refusal deliberately carries none. An earlier draft of this document
claimed both refusals state the millimetres; independent review caught that, and the code now says
what is true.

The millimetres that are reported are computed at the production resolution rather than at the
file's own stored resolution, because the question being answered is "what canvas was this grid
meant to be", not "what would a viewer that trusted the file's metadata show".

---

## 7. Incorrect-output-metadata simulation

SCRUM-11097's "incorrect output metadata" is read against the TIFF contract SCRUM-11103 defines:
"matches validated bit depth and compression" alongside the CMYK mode and white-ink clauses. The
inspector already refused six distinct metadata faults, all previously reachable only by writing a
fixture directly.

Two were chosen rather than all six, because they answer different halves of the clause:

- **`IncorrectMetadata`** declares LZW image compression (TIFF value 5) in place of the contracted
  None, covering SCRUM-11103's explicit "matches validated bit depth and compression" wording.
  `ProductionTiffInspector` refuses it with "Production TIFF image compression is not None (TIFF
  value 1)", at the tag, before any strip is decoded. The strips themselves are still uncompressed,
  so this file is genuinely corrupt as well as mis-labelled — an earlier draft described it as one
  "a viewer would open without complaint", which independent review correctly rejected.
- **`IncorrectChannelMetadata`** is the file that description actually fits: a completely
  well-formed, openable production TIFF whose Photoshop spot channel is named "White" instead of
  "W1". Every byte decodes; the geometry, resolution and ink are all correct; and it is still not a
  file Maintop may be handed, because PrintFlow's white-underbase contract is a channel *named* W1.
  Refused with "Photoshop image resources do not identify exactly one W1 channel as a spot colour".

Two is the minimal set that proves the clause honestly — one metadata fault that is caught before
the raster is read, one that survives a full decode. The remaining five (byte order, planar
configuration, extra-sample/alpha, layer compression, image pyramid) stay covered where they already
were, as `ProductionTiffInspectorTests.InvalidContracts` rows. Promoting all of them to fake output
classes would prove the same seam seven times, which is the over-testing the brief warns against.
One addition was made to that inspector theory — `PhotometricInterpretation: 2` — because the new
encoder option made a previously unexercised inspector branch reachable for the first time.

---

## 8. The full SCRUM-11129 matrix

Every row is driven through `SessionService` on a real SQLite database and a real filesystem, in
`tests/PrintFlow.Tests/Integration/Persistence/PhotoshopFaultMatrixTests.cs`. "Final approval
reachable?" is not inferred from the attempt state — an `Approve` command is genuinely issued for
every invalid row and its refusal is asserted.

| AC case | Fake stimulus | Output produced? | Inspector / rule result | Attempt state | Revision? | PrintOutput? | Final approval reachable? |
|---|---|---|---|---|---|---|---|
| Successful TIFF | `SetTiffOutput(ValidTiff)` | Yes — accepted production TIFF | Accepted | `Succeeded` | **Yes** | **Yes**, on approval | **Yes**, bound to the validated hash |
| Missing white channel | `SetTiffOutput(MissingWhiteChannel)` | Yes — real file, 4 samples | `OutputValidationFailed` "five 8-bit samples" | `Failed` | No | No | **No** — `Approve` refused |
| Empty white channel | `SetTiffOutput(EmptyWhiteChannel)` | Yes — real file, blank W1 | `OutputValidationFailed` "wholly empty" | `Failed` | No | No | **No** |
| Wrong colour mode | `SetTiffOutput(WrongColourMode)` | Yes — real file, RGB photometric | `OutputValidationFailed` "separated CMYK" | `Failed` | No | No | **No** |
| Wrong pixel dimensions (width) | `SetTiffOutput(WrongPixelWidth)` | Yes — otherwise-accepted TIFF, 1 px wide | `OutputValidationFailed` "projected dimensions" | `Failed` | No | No | **No** |
| Wrong pixel dimensions (height) | `SetTiffOutput(WrongPixelHeight)` | Yes — otherwise-accepted TIFF, 1 px tall | `OutputValidationFailed` "projected dimensions" | `Failed` | No | No | **No** |
| Wrong physical dimensions | Entailed — see section 6 | Yes (both routes) | `OutputValidationFailed`, millimetres in context for the grid route | `Failed` | No | No | **No** |
| Incorrect DPI | `SetTiffOutput(IncorrectDpi)` | Yes — real file at 150 PPI | `OutputValidationFailed` "300 pixels per inch" | `Failed` | No | No | **No** |
| Incorrect output metadata (compression) | `SetTiffOutput(IncorrectMetadata)` | Yes — real file, LZW declared | `OutputValidationFailed` "compression is not None" | `Failed` | No | No | **No** |
| Incorrect output metadata (channel) | `SetTiffOutput(IncorrectChannelMetadata)` | Yes — fully decodable file, channel named "White" | `OutputValidationFailed` "exactly one W1 channel" | `Failed` | No | No | **No** |
| Invalid / unreadable output | `SetScenario(ProduceUnreadableFile)` | Yes — genuine zero-byte file | `OutputUnreadable` from the workflow's file inspection | `Failed` | No | No | **No** |
| Missing output | `SetScenario(ProduceMissingFile)` | **No** — nothing at the reserved path | `OutputMissing` | `Failed` | No | No | **No** |
| Export failure | `SetScenario(FailWith(OutputValidationFailed))` | No | n/a — failed before writing | `Failed` | No | No | **No** |
| Timeout | `SetScenario(Timeout)` | No | `Timeout` | `Failed` | No | No | **No** |
| Unknown dialog | `SetScenario(FailWith(PhotoshopUnknownState))` | No | `PhotoshopUnknownState` | `Failed` | No | No | **No** |
| Blocking dialog | `SetScenario(FailWith(PhotoshopBlockingDialog))` | No | `PhotoshopBlockingDialog` | `Failed` | No | No | **No** |
| Interruption | `SetScenario(HangUntilCancelled)` + cancel in flight | No | n/a | not `Succeeded` | No | No | **No** — step never reaches ReviewRequired |

Every row's output-produced column is asserted from
`FailureEvidence.ExpectedOutputPathKey` in the persisted failure context, so "a real file was
written and refused" and "nothing was written" are distinguished from evidence rather than assumed.
Every row's failure code is asserted, not merely `IsFailure`.

Two corrections came out of independent review and are recorded because the first draft passed
while proving less than it claimed:

- The timeout row was building its scenario from the presence of a scripted failure code, which
  routed it down the generic `FailWith` path and left `FakeAdapterScenarioKind.Timeout` untested.
  It passed regardless, because both paths return `FailureCode.Timeout`. The scenario is now built
  from the kind alone.
- "Unknown dialog" was mapped to `FailureCode.PhotoshopBlockingDialog`, which means the opposite —
  a *recognised* modal owned by Photoshop. The unrecognised-screen code is `PhotoshopUnknownState`,
  which is what the real Photoshop path returns for this condition. The AC's outcome is now covered
  by that code, and the blocking-dialog case is kept as its own row under its own honest label.

No row in this table is satisfied by a direct inspector fixture. Where SCRUM-11097 requires the
Fake Adapter, the stimulus column names a fake scenario and the run goes through
`IPhotoshopOutputProcessor`.

---

## 9. Invalid outputs cannot enter final approval

Proven at the real workflow boundary, not at `ProductionTiffInspector.Inspect(...)`.

For every invalid row above, `AssertNothingReviewableAsync` asserts, from the reloaded aggregate:

- no `OperationKind.PhotoshopOutput` Revision exists;
- `aggregate.Outputs` is empty;
- the attempt is `Failed`, `OutputRevisionId` is null, and its `Failure` is retained;
- the step is `Failed` with no `CurrentRevisionId`;
- and then an `Approve(StepKind.PhotoshopOutput, …)` command is issued and **refused** with
  `FailureCode.PreconditionNotMet`, after which `aggregate.Outputs` is still empty.

The approval is attempted with the *upstream Revision's own hash*, deliberately. There is no output
Revision after an invalid run, so the only value a caller could plausibly reach for is an artefact
that does exist; refusing an obviously fabricated hash would prove much less.

For the seven structural rows a **second** approval is attempted, with the refused TIFF's own
SHA-256 — the file is still on disk, so that hash is a value a caller could genuinely present. This
is the stronger form of the property, and it exists because independent review pointed out that
approving from a `Failed` step is refused by the state machine whatever hash is offered: the first
attempt alone demonstrates "you cannot approve a failed step", which is weaker than the AC's
"invalid outputs cannot enter final approval". The second attempt shows the refusal covers the
invalid artefact itself.

**Failure evidence is preserved, not changed.** No retention or error semantics were modified.
`persistedFailure.Failure.Context[ExpectedOutputPathKey]` still names the exact refused file, the
refused TIFF is still on disk after the attempt fails, and
`ProductionTiffPreparationMatch` adds `expectedPixels`, `actualPixels`, `expectedDpi`, `actualDpi`,
`expectedMillimetres`, `actualMillimetres`, `adapterOutputConstructed=false` and
`revisionCreated=false` to the diagnostic context — additive only.

---

## 10. Clean-retry proof

`A_retry_after_a_structural_failure_starts_from_the_clean_approved_upstream_Revision` uses the
ordinary `WorkflowCommand.Retry` path. No second retry implementation was added.

The wrong-dimension fault was chosen for this test specifically because it is the one where a retry
that read the previous output would still be handed a *structurally perfect* TIFF: every absolute
check would pass and only the preparation comparison would catch it. A retry that silently
inherited a bad canvas is therefore a defect this case can detect and the other faults cannot.

| Stage | Asserted |
|---|---|
| Approved upstream Revision A | captured before either attempt, with its bytes hashed |
| Attempt 1 | `WrongPixelWidth` → `OutputValidationFailed`; the refused TIFF exists on disk at the recorded path, one pixel wider than this session's projected grid |
| Retry | `WorkflowCommand.Retry` then `StartStep`, output class `ValidTiff` |
| Attempt 2 identity | a different `AttemptId`; `Succeeded` |
| Clean upstream | `secondAttempt.InputRevisionId == firstAttempt.InputRevisionId == upstream.Id`, and the upstream file's SHA-256 is unchanged from before attempt 1 |
| Not from the failed run | the output Revision's path contains attempt 2's id and **not** attempt 1's; its `SourceRevisionId` is the upstream Revision; its hash differs from the failed TIFF's; its absolute path is not the failed TIFF's |
| History preserved | exactly two Photoshop attempts; the failed one is still `Failed`, still has no `OutputRevisionId`, and still carries its `OutputValidationFailed` failure with `expectedOutputPath` and `actualPixels` intact |
| Final review binding | approval binds a single `PrintOutput` to the successful validated hash, and that hash is asserted **not** to be the failed TIFF's |

---

## 11. Determinism

- No Photoshop, Meitu or Maintop process; no COM; no UIA; no foreground interaction; no dependency
  on the verified workstation. Every case is bytes written to a local directory and read back.
- `The_same_scripted_output_is_byte_identical_across_calls` asserts two calls with the same output
  class produce the same SHA-256. No timestamp, GUID or random sample enters the encoder.
- No environment-variable switch or production-code test hack. `SetTiffOutput` is the existing
  fake-mode configuration mechanism, sitting beside the existing `SetScenario`; nothing was added to
  any production adapter's behaviour.
- `An_export_failure_writes_nothing_whatever_the_output_class_says` proves the two halves of the
  vocabulary stay independent — a scenario that produces no file ignores the output class entirely,
  so no orphan file is left behind that no attempt claims.
- The successful TIFF path composes its adapter note from the same `Notes(request)` the copy path
  uses — edge, resize policy, projection scale, preset and capacity limits, enlargement authority —
  and appends the validated facts. It was originally a thinner, separately-composed string, which
  would have meant the one path capable of backing a real Fake-mode Revision carried the *least*
  evidence. Independent review caught that.

---

## 12. Production adapter untouched

`GuardedPhotoshopUiDriver`, `GuardedPhotoshopDocumentPreparer`, `GuardedPhotoshopW1Executor`,
`GuardedPhotoshopTiffSaver`, `ProductionPhotoshopOutputProcessor` and `PhotoshopAdapterOutputFactory`
are unchanged.

This was checked rather than assumed. The production path's saved-TIFF geometry is validated
against the Photoshop document (`GuardedPhotoshopTiffSaver.cs:244`) and that document is validated
against the preparation (`GuardedPhotoshopDocumentPreparer.cs:491`), and the composition in
`ProductionPhotoshopOutputProcessor.GenerateAsync` makes the preparer stage unskippable — a
preparation failure returns before the W1 and save stages exist. The chain is therefore complete
for the production adapter, so the missing saved-TIFF-versus-preparation comparison was a real
absence of *fake reachability*, not a hole in production validation. Adding the rule and calling it
from the fake is the smallest correct boundary; forcing the guarded save path through it would have
duplicated a check that path already makes, and would have required handing the saver a
preparation, which `PhotoshopTiffBoundaryTests` asserts its surface does not accept.

The honest consequence — that `ProductionTiffPreparationMatch` therefore has one caller and its
evidence keys appear on Fake-mode refusals only — is stated in the type's own remarks, in the fake's
remarks, and in section 5, rather than being papered over with a claim of shared use. That claim
was in the first draft and independent review removed it.

No click order is asserted anywhere in the new tests, for either adapter.

---

## 13. Targeted tests

Debug, `PrintFlow.Tests`, local machine.

| Suite | Before review fixes | After review fixes |
|---|---|---|
| `FakePhotoshopStructuralFaultTests` + `PhotoshopFaultMatrixTests` (both new) + `ProductionTiffInspectorTests` | 31 + 12 | **47 passed, 0 failed, 0 skipped** |
| `ProductionTiffInspectorTests`, `PhotoshopTiffWorkflowOutputTests`, `PhotoshopTiffFinalReviewTests`, `PhotoshopTiffSaveTests`, `PhotoshopWorkflowOutputTests`, `RetryAndReviewTests`, `RecoveryAndBranchTests`, `ProductionTiffReviewDecoderTests`, `RetentionCleanupTests`, `TiffFinalReviewModeTests` | 132 | **173 passed, 0 failed, 0 skipped** |
| All `Architecture` boundary tests plus every preparation / print-dimension / target-edge / fit-bounds suite | 623 passed, 0 failed, 0 skipped | unchanged by the review fixes |

The architecture and preparation suites were run because fixture placement moved into `src/` and
because the Domain millimetre authority changed. `RetentionCleanupTests` and
`TiffFinalReviewModeTests` were added to the affected set after review, because they are the
consumers of `SyntheticProductionTiffProcessor`, whose class remark this slice touched.

---

## 14. Full-suite decision

**Run, not skipped.** The brief permits skipping the full suite when a slice is confined to fake and
test infrastructure. This slice is not: it changes `PrintDimensions` (a shared Domain authority) and
its two Domain call sites, and it adds a shared Infrastructure rule. Both fall squarely inside the
"shared Product behaviour" list that requires a full run.

It was run **twice**. The first run (11,777 passed) validated the implementation before independent
review. Because review produced real Product changes — the `+1` deviation fix, the removed
resolution branch, a new output class, the recomposed adapter note — that result no longer describes
the final source, and a second full run was made after every fix. Only the second is the
final-source evidence; both are reported in section 17 so the sequence is auditable.

---

## 15. Independent review

**A genuinely separate read-only reviewer was used.** A fresh sub-agent with no memory of the
implementation was given the two exact ACs, the change list and ten specific axes, and was
constrained to reading and `git diff` only — no edits, no build, no test run, and no Photoshop,
Meitu or Maintop. This is not SELF-REVIEW ONLY.

It returned eleven findings. All were triaged; the substantive ones were fixed before this document
was finalised.

| # | Finding | Disposition |
|---|---|---|
| 1 | The timeout matrix row built its scenario from the presence of a scripted failure code, routing it down the generic `FailWith` path and leaving `FakeAdapterScenarioKind.Timeout` untested. It passed anyway, because both paths return `FailureCode.Timeout` | **Fixed.** Scenario is now built from the kind alone; the remark records the trap |
| 2 | "Unknown dialog" was mapped to `FailureCode.PhotoshopBlockingDialog`, which means a *recognised* modal. The unrecognised-screen code is `PhotoshopUnknownState` | **Fixed.** Row remapped; blocking dialog kept as its own row under its own label |
| 3 | `ProductionTiffPreparationMatch` was described in three places as shared with the production save path. It has one caller — the fake | **Fixed by correcting the claims**, not by forcing production through it: the guarded save surface deliberately does not accept a preparation, and an architecture test enforces that. Sections 3, 5 and 12 now say what is true |
| 4 | "Both refusals state the millimetres" was untrue for the DPI case, which never reaches the match | **Fixed.** The unreachable resolution branch was removed from the rule entirely, and the test now asserts the DPI refusal carries *no* millimetres and *no* pixel context — which is the distinguishing evidence rather than a repeat |
| 5 | "Invalid outputs cannot enter final approval" proved only "you cannot approve a failed step"; the refusal code was unasserted | **Fixed.** The refusal code is now pinned, and every structural row additionally attempts approval with the refused TIFF's own on-disk SHA-256 |
| 6 | `Math.Max(1, projected - 1)` could hand a one-pixel-edge preparation the *accepted* geometry, inverting a wrong-dimension scenario into a silent success | **Fixed.** Deviates upward (`+ 1`), which is always representable; no clamp remains |
| 7 | `IncorrectMetadata` writes a genuinely corrupt file (LZW declared over raw strips), not the "viewer would open it happily" case the remark claimed | **Fixed both ways.** Remark corrected, and `IncorrectChannelMetadata` added as the genuinely-openable metadata fault |
| 8 | `MissingWhiteChannel` and `WrongColourMode` emit self-contradictory files Photoshop could not produce | **Documented, not re-engineered.** Both are caught by the intended rule; the one-field-changed discipline is what produces the inconsistency, and each member now records it. Re-engineering would have grown the encoder's surface to make files look real without changing a single outcome |
| 9 | `IncorrectDpi` asserted twice; `WrongPixelWidth` driven by four separate tests | **Fixed.** The geometry faults were removed from the general structural theory (their own theory makes every assertion it did, plus per-axis evidence), and the duplicated DPI assertion was replaced with the non-duplicative one described in #4 |
| 10 | Dead `fileExpected` theory parameter — one value across all rows | **Fixed.** Inlined in the structural theory; retained in the behavioural theory, where it genuinely varies |
| 11 | The valid-TIFF path emitted a thinner audit note than the copy path, so the one path that can back a real Fake-mode Revision carried the least evidence | **Fixed.** It now composes from the same `Notes(request)` and appends the validated facts |

The reviewer additionally reported no finding on: saved-TIFF wrong-dimension realism (called "the
strongest part of the change"), physical-vs-pixel-vs-DPI distinctness, click-order assertions,
determinism and unchanged default fake behaviour, and the reachability of the new
`MillimetresFromPixels` guard from its two refactored call sites.

Its note on `SyntheticProductionTiffProcessor` was accepted: the fake's `ValidTiff` now subsumes
that double's "write a real accepted TIFF" job, but it still uniquely owns the `Bands` W1 pattern a
preview assertion is checked against and the `GenerateCount` counter restart tests use. Neither
belongs in a shipped adapter, so the double stays and its stale class remark was corrected to say
what it now uniquely provides.

---

## 16. Jira reassessment

Assessed against the exact CSV wording in section 1, clause by clause. No Jira service was mutated.

### SCRUM-11097 — **FULL**

| AC clause | Evidence |
|---|---|
| valid TIFF | `FakePhotoshopTiffOutput.ValidTiff`; accepted by the real inspector at the projected geometry |
| missing white channel | `MissingWhiteChannel` |
| wrong colour mode | `WrongColourMode` |
| wrong dimensions | `WrongPixelWidth` / `WrongPixelHeight`, against the **saved bytes** |
| incorrect output metadata | `IncorrectMetadata` (compression) and `IncorrectChannelMetadata` (spot-channel name) |
| export failure | `FakeAdapterScenario.FailWith` |
| timeout | `FakeAdapterScenarioKind.Timeout`, now genuinely exercised |
| interruption | `HangUntilCancelled` cancelled in flight; `WaitForStopAt` remains for operator stop |
| unknown dialog | `FailWith(PhotoshopUnknownState)` |
| without launching Photoshop | No process, COM or UIA anywhere; bytes on a local filesystem |
| same interface used by production | `IPhotoshopOutputProcessor`, the port `ProductionPhotoshopOutputProcessor` implements and `SessionService` consumes. No test-only validator, service or command exists |
| avoid asserting click order | No sequence or order assertion in either new test file; the prohibition is stated in both |

All twelve clauses are met through the fake seam. No clause is counted on the strength of an
inspector-only fixture.

### SCRUM-11129 — **FULL**

Every named outcome has explicit evidence in the section 8 matrix, driven through `SessionService`
against a real database and filesystem, with the failure code asserted for each. Both added
properties hold: invalid outputs cannot enter final approval (section 9, including approval
attempted with the refused TIFF's own hash), and a retry after a structural output failure starts
from the clean approved upstream Revision (section 10).

The previously recorded sole gap — saved-TIFF dimension mismatch — is closed. Re-reading the exact
AC surfaced no new gap.

**Two interpretations are declared rather than left implicit**, because a reader should be able to
disagree with them:

1. *"Wrong physical or pixel dimensions"* is satisfied by pixel dimensions on both axes plus the
   derived physical canvas, not by a third independent physical check. Section 6 sets out why
   PrintFlow has no such check and why adding one would restate an existing answer rather than
   catch a further defect. The AC's own "or" is satisfied on the pixel side regardless.
2. The saved-TIFF-versus-preparation comparison is executed by the **Fake** adapter. The production
   adapter establishes the same fact transitively, through the Photoshop document it prepared
   (section 12). This is not a production validation gap, but it does mean the two adapters reach
   the conclusion by different routes, and that is now written down in three places in the code.

### Parent SCRUM-11093 — **confirmed FULL**, unchanged

Assessed independently against the Epic's own functional clauses, not by rolling up child statuses.

This slice **confirms** the accepted position and does not downgrade it. What it found was a gap in
*test reachability* — the Fake adapter could not emit a structurally faulty TIFF, so several
validation rules were only ever exercised by writing fixture bytes directly — and the absence of a
*named* authority for saved-bytes-versus-preparation. Neither is a hole in production validation:
`ProductionPhotoshopOutputProcessor` composes the preparer before the W1 and save stages, so a
document whose pixels are not the projected pixels cannot reach a save, and a saved TIFF whose
pixels are not that document's cannot reach `PhotoshopAdapterOutputFactory`.

The Epic's strongest clause — *"PrintFlow must never … treat an invalid TIFF as production-ready"* —
is now positively demonstrated end to end for eleven distinct invalid outcomes, rather than inferred
from the inspector's unit coverage. That strengthens the parent's FULL position; it does not change
it. No previously unknown Product gap was exposed.

---

## 17. Results

**Final-source validation.**

| | Result |
|---|---|
| Release build (`PrintFlowStudio.sln -c Release`) | **0 warnings, 0 errors** |
| Debug build | 0 warnings, 0 errors |
| Full Product suite — final source | **11,778 passed, 0 failed, 0 skipped** (5 m 20 s) |
| Full Product suite — pre-review implementation | 11,777 passed, 0 failed, 0 skipped |
| Recorded baseline before this slice | 11,745 passed, 0 failed, 0 skipped |

The +33 delta against the baseline is fully accounted for: 15 tests in
`FakePhotoshopStructuralFaultTests`, 17 in `PhotoshopFaultMatrixTests`, and 1 added
`ProductionTiffInspectorTests` theory row. Nothing pre-existing was removed, disabled or skipped.

The one-test difference between the two full runs is the net effect of the review fixes: the two
geometry faults were removed from the general structural theory (−2), `IncorrectChannelMetadata` was
added to two theories (+2), and the behavioural theory gained a blocking-dialog row (+1).

**Git state.** Local `master` only. Started from `456cb842b0b5c87a31b9ccdd562151ebd2c392aa`. No
branch, worktree, alternate clone or alternate checkout was created; nothing was amended, rebased,
pushed or deployed; no AI-attribution trailer was added. Two local commits, recorded in the re-audit
delta.

**Environment.** No Photoshop, Meitu or Maintop process was launched at any point. No COM, no UI
automation, no dependency on the verified workstation. The existing 300-PPI TIFF contract, W1
behaviour, workstation preset and regression assets are unchanged.
