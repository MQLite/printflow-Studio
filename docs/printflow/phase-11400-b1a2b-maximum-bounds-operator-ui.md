# Epic 11400 — Part B1A.2B: Maximum-Bounds Operator UI and Fake Workflow Integration

Status: **PASS WITH NOTES**

A small UI and read-model slice over the accepted B1A.1 fit-within-bounds contract and the
B1A.2A workflow/persistence foundations. The operator screen now speaks maximum-bound
millimetres, shows the plan the workflow layer calculated, fails closed on dimensions that
cannot be executed, and audits a Fake Photoshop result against the plan that attempt actually
ran under.

Nothing in this slice launches Photoshop, resizes, converts to CMYK, saves a TIFF, executes W1,
adds a migration, or enables production mode. `Adapters.Mode` remains `Fake` and the preset stays
`printflow-workstation-v1` v1.10.0.

---

## 1. Operator terminology

The exact-target wording is gone from the size decision. The two millimetre boxes are stated as
limits everywhere they appear.

| Concept | en-US | zh-CN |
| --- | --- | --- |
| Panel heading | Maximum print size | 最大打印尺寸 |
| Width box | Maximum width (mm) | 最大宽度（mm） |
| Height box | Maximum height (mm) | 最大高度（mm） |
| Long-edge limit | Maximum long edge (mm) | 最大长边（mm） |
| Confirm action | Confirm maximum print size | 确认最大打印尺寸 |
| Explanation | "Enter the maximum width and maximum height the print may reach, in millimetres. The image is fitted proportionally inside those limits — it is never stretched and never enlarged. Fixed production resolution: 300 PPI." | "请输入打印成品允许达到的最大宽度和最大高度，单位为毫米。图像会按原图比例适应最大尺寸，既不会被拉伸，也不会被放大。固定输出分辨率：300 PPI。" |

What the screen deliberately does **not** offer:

- no authoritative-axis selection;
- no resampling-method selection, and the words `Bicubic Sharper`, `NONE`, `resample` and
  `interpolation` appear in no operator string in either language;
- no required pixel inputs.

The internal `PhotoshopResizeMode` stays auditable Domain state. The operator is told what will
*happen* — "pixel dimensions will remain unchanged", or "reduced proportionally" — which is the
behaviour they can act on.

Millimetres remain the operator-facing unit throughout.

## 2. Preset and long-edge presentation

`SizePresetChoice` gained a `BoundsLabel`, built from `PrintDimensions.NominalMillimetres` — the
single Domain authority for what a named preset is. No size is stated in the ViewModel or in
XAML; the test compares each choice's millimetres against the Domain call rather than against a
literal.

The label form follows the shape of the box:

- two different limits → `Maximum 420 × 297 mm` / `最大 420 × 297 mm`;
- one limit (a square box) → `Maximum long edge: 150 mm` / `最大长边：150 mm`.

The long-edge form is the honest reading of a square fit box: fitting proportionally inside one
caps whichever source edge is longer and lets Photoshop derive the other, which is exactly what a
long-edge limit means. Presenting a *non-square* box that way would hide its second limit, so a
non-square preset keeps the box form.

**Note (carried to B1A.3 or a later slice).** The signed preset's
`productionGeometryContract.resize.limitsMillimetres` states A4 `maxLongEdge = 280` and A5
`maxLongEdge = 135`, and `FitWithinBounds.CalculateLongEdge` exists in the Domain — but neither is
wired into any executable path. `PrintDimensions.NominalMillimetres` still models A4 and A5 as
paper-size boxes (210×297, 148×210), and that pair is what `SetPrintDimensions` persists. Showing
"Maximum long edge: 297 mm" for A4 while a 210×297 box is applied would misdescribe what the run
will do, so this slice presents A4 and A5 truthfully as maximum boxes. Making them genuine
long-edge presets means changing what a named preset persists — a Domain/persistence change,
outside a UI slice, and one that would require the complete suite. The long-edge presentation
rule itself is implemented and covered.

The operator never chooses the limiting edge anywhere.

## 3. Maximum-bound input

The panel is visible exactly while `SessionView.CanSetMaximumBounds` is true — the workflow
layer's own answer about whether `SetPrintDimensions` would be accepted. That is also what reopens
it after Add Another Size, with no second rule about reopening.

It carries: the maximum width box, the maximum height box, the preset shortcuts, the
proportional-fit explanation, the pending-limits read-out, and **Confirm maximum print size**.

Confirming calls the existing `WorkflowCommand.SetPrintDimensions` path and nothing else, then
shows the `SessionView` the service returned. Parsing and validation stay
`PrintDimensions.TryFromMillimetres`; unusable millimetres produce a notice and persist nothing.

The pending read-out shows millimetres only. It deliberately shows no pixel figure: converting the
two limits independently would produce a pair that looks exactly like an output size and is not
one. What the image becomes is `FitWithinBounds`' answer and arrives with the plan.

## 4. Plan summary

Shown when a currently usable `MaxBoundsV1` plan exists. Every value is read from `SessionView`,
which reports the **usable** plan and nothing else.

`ResolutionOnly`:

> The image already fits within the maximum size. Pixel dimensions will remain unchanged.
> Projected size: 6 × 5 px
> Fixed production resolution: 300 PPI

No limiting-edge line is shown, because no edge is written.

`ProportionalShrink`:

> The image will be reduced proportionally to fit the maximum size.
> Limiting edge: Width
> Projected size: 591 × 295 px
> Fixed production resolution: 300 PPI

The pixels are labelled *projected* in both languages, and a localisation test forbids the words
"actual" and "final" beside them. They remain a plan until B1A.3 reads the real Photoshop result
back.

## 5. Run gating

`SessionViewModel.CanRunPhotoshopOutput` reads `SessionView.CanRunPhotoshopOutput` and nothing
else. Neither `Dimensions != null` nor `DimensionSemantics == MaxBoundsV1` is consulted anywhere:
a session can hold a raw plan calculated against content that has since been replaced, and both of
those would call it ready.

Verified at every stage of a real session — before bounds, after bounds but before the W1 branch,
after both, after a stale rebinding, and after Add Another Size — by comparing the screen's answer
against the read model's on each transition.

## 6. Legacy review and reconfirmation

When `NeedsDimensionReview` is true the screen shows a warning that names a size decision to
redo, not a Photoshop failure:

> This size was saved under the previous exact-size rule, or the image it was measured against has
> changed. Review the maximum print bounds before continuing.

A localisation test forbids "failed", "failure", "error", "deleted" and "lost" in it. No attempt is
created, no adapter is called, and no automation lock is taken.

The retained millimetres stay on screen under "Saved earlier, not confirmed as maximum bounds" /
"此前保存，尚未确认为最大尺寸范围". They are never presented as active limits, never auto-converted, and
never marked confirmed because they happen to suit the current source ratio.

**Review maximum bounds** / **重新确认最大尺寸** issues no new command path. It selects the
`PrintDimensions` row *from `SessionView.ReturnTargets`* — a row the engine produced by applying
the real `ReturnToStep` command — and opens the existing return confirmation, with its existing
wording about later derived results becoming invalid and audit history being retained. It says
nothing about files being deleted, because `ReturnToStep` deletes none. When no such return target
is offered the action is not enabled; the shell does not assume the size step is always behind the
Photoshop step.

Cancelling issues no command, changes no state, creates no attempt and acquires no automation
lock — asserted by re-reading the persisted aggregate after the cancel.

Confirming reaches `PrintDimensions` with dimensions and semantics cleared, and a fresh bounds
decision produces `MaxBoundsV1` and makes the workflow runnable again. There is no direct legacy
conversion button.

## 7. Stale, retry and Add Another Size behaviour

**Stale plan.** A plan rebound to a different `SourceRevisionId` is retained in the database and
never appears on screen: the summary is hidden, the confirmed bounds read "Not set", Run is
unavailable, and the ordinary review state is shown instead. No Revision or SHA comparison exists
anywhere in the shell.

**Same-content retry.** After a Fake output is rejected and retried against unchanged upstream
content, the plan is still displayed, the bounds are unchanged, `NeedsDimensionReview` is false and
Run is available again. There is no "always reconfirm on retry" policy in the UI.

**Add Another Size.** The new branch presents a fresh maximum-size decision: the input panel
reopens, no plan is shown, the confirmed bounds read "Not set", Run is unavailable, and the state
is *not* a review state — nothing unexecutable is being carried forward. The output already
produced stays listed and valid.

## 8. Producing-attempt audit display

New read-model type `PrintPreparationAttemptView` (`Semantics`, `MaxWidthMm`, `MaxHeightMm`,
`Mode`, `LimitingEdge`, `ProjectedPixelWidth`, `ProjectedPixelHeight`, `ProductionDpi`,
`IsFakeProjection`), exposed as `SessionView.AttemptPreparation`.

It is resolved through `ProcessingAttempt.OutputRevisionId` — the attempt that says it produced
this exact Revision — exactly as `CurrentTrimParameters` and the background-removal attempt
authority are, and never from the session's pending plan. It is a flattened projection rather than
the `PrintPreparationPlan` record, so `SourceRevisionId`, `SourceSha256` and `Covers` never reach
the shell. There are no `Actual*` fields.

Beside a `ReviewRequired` result the screen shows:

> Print preparation used
> Maximum bounds 50 × 50 mm
> Proportional reduction
> Limiting edge: Width
> Projected 591 × 295 px at 300 PPI
> *Projected plan — Photoshop was not run.*

Covered by three tests: the audit matches the attempt's immutable snapshot field by field; it keeps
quoting that snapshot after the session's pending plan is rebound to a different box; and an
artefact no Photoshop attempt produced carries no audit at all, even while the session holds a
usable plan.

## 9. Fake workflow smoke

Real WPF view models, real `SessionService`, real SQLite, real workspace, Fake adapters. Import →
GENERATE_PRINT_TIFF → confirm original → PrintDimensions → preset/typed maximum bounds → confirm →
plan summary appears → W1 branch → Run becomes available → Fake PhotoshopOutput → ReviewRequired →
attempt preparation audit visible.

Verified: the Fake output is written to the reserved output path as a separate workspace file; the
imported Revision's bytes and file are unchanged; the projected-plan note is on screen and says
Photoshop was not run; the attempt's `AdapterId` is `fake-photoshop-v1` with
`AdapterExecutionMode.Fake`; `SessionView.ProcessingMode` is `Fake`; no production read-back claim
appears anywhere.

(The Fake adapter copies the input to the reserved output path, so the two files share a hash by
construction — which is why "input unchanged" is asserted on the input's own path and Revision
rather than by comparing the two hashes.)

## 10. Localisation and rendering

Full en-US/zh-CN parity for 29 new keys, enforced by name in `LocalisationResourceTests` on top of
the existing two-way parity and typed-accessor checks. No operator string is hard-coded in XAML or
in the ViewModel.

Three new wording tests: no resource in either language offers a resampling method
(`Bicubic`/`双三次`/`Resample`/`重新取样`/`Interpolation`/`插值`); the maximum-bound hint says limits,
proportion and 300 rather than "exact"; the projected figures are never called actual or final.

Rendering: all eight states from §27 measured and arranged at the signed-off 1000×700 viewport with
`PresentationTraceSources.DataBindingSource` escalated to error level — maximum-box input, preset
shortcuts, `ResolutionOnly` summary, `ProportionalShrink` summary, legacy review warning, the
`ReturnToStep` confirmation, the Fake `ReviewRequired` attempt audit, and the Add Another Size
fresh-decision state. Plus a zh-CN pass over the input panel, a shrink summary and the attempt
audit, asserting the satellite really is what is shown (a missing translation would fall back to
the raw resource key). Binding errors are failures; there were none.

No golden-screenshot system. Human visual inspection is not part of this slice and remains Final
QA's.

## 11. Targeted tests

Run per §1 — no unfiltered `dotnet test`.

| Suite | Passed | Failed |
| --- | ---: | ---: |
| `MaximumBoundsUiTests` (new) | 30 | 0 |
| `MaximumBoundsRenderingTests` (new) | 9 | 0 |
| `MaximumBoundsBoundaryTests` (extended) | 47 | 0 |
| `LocalisationResourceTests` (extended) | 41 | 0 |
| `MaximumBoundsPlanTests` | 12 | 0 |
| `DimensionsW1AndOutputTests` (updated) | 25 | 0 |
| `ViewRenderingTests` | 22 | 0 |
| `SessionSmokeTests` | 14 | 0 |
| All `Integration.Ui` + `Architecture` | 458 | 0 |
| All `Unit.Workflow` + `Integration.Persistence` | 7274 | 0 |

The last two rows were widened beyond the strictly focused set for one reason, stated here as §1
requires: `SessionView` gained a positional record parameter, which is a shared read-model change,
so every suite that consumes the read model was run. No workflow rule, persistence schema,
migration or engine precondition was touched, so the complete suite stays deferred to Epic 11400
Final QA.

Architecture checks (§29), all passing:

- App/ViewModel calls no `FitWithinBounds` and constructs no `PrintPreparationPlan` (pre-existing);
- no view model names `Covers`, `UsablePrintPreparationPlan`, `SourceRevisionId`, `SourceSha256` or
  `Rehydrate` (pre-existing);
- **new:** no view model reaches `System.IO`, `File.`, `Directory.`, `Path.` or `FileStream`;
- **new:** no view model derives pixels from the operator's millimetres
  (`PixelsFromMillimetres`, `MillimetresPerInch`, `25.4`);
- **new:** the shell has exactly one `WorkflowCommand.SetPrintDimensions` call site;
- **new:** the review action resolves its destination from `ReturnTargets` and issues
  `WorkflowCommand.ReturnToStep`;
- **new:** `PrintPreparationAttemptView` carries no `Actual*`, no `Sha`, no `RevisionId`;
- **new:** the migration set still ends at `0005`;
- no resampling vocabulary in `PrintFlow.Workflow` or `PrintFlow.App` (pre-existing);
- the production Photoshop adapter gained no `resizeImage`, `changeMode`, `ConvertProfile`,
  `SaveAs` or `DoAction` call (pre-existing).

## 12. Build

`dotnet build` — **0 warnings, 0 errors.**

## 13. Dependency and preset state

Dependency graph unchanged; no `.csproj`, `Directory.Packages.props`, `nuget.config` or
`packages.lock.json` was modified. **Vulnerability audit deferred to Epic 11400 Final QA.**

`appsettings.json` unchanged: preset `printflow-workstation-v1` v1.10.0 with its recorded SHA-256,
`Adapters.Mode = Fake`. No preset or evidence file was touched, so no integrity rehash was needed.

## 14. Remaining B1A.3 production scope

Not started, and nothing here anticipates it:

- launching and driving production Photoshop for size preparation;
- writing the plan's single limiting edge with proportions constrained, at BicubicSharper for a
  shrink and NONE for a resolution-only run;
- reading the **actual** Photoshop-returned geometry back and recording it — the point at which
  `Projected*` gains an `Actual*` counterpart and the audit line can stop saying "Photoshop was not
  run";
- W1 execution, CMYK conversion, the TIFF save, `AdapterOutput` and production Revision creation;
- wiring the signed contract's A4/A5 long-edge limits into a Domain authority (see §2).

## 15. Git state

Branch `master`, no push, no amend, no rebase.

Changed:

- `src/PrintFlow.Workflow/Services/SessionView.cs`
- `src/PrintFlow.App/ViewModels/SessionViewModel.cs`
- `src/PrintFlow.App/Views/SessionScreenView.xaml`
- `src/PrintFlow.App/Resources/Strings.cs`
- `src/PrintFlow.App/Resources/Strings.resx`
- `src/PrintFlow.App/Resources/Strings.zh-CN.resx`
- `src/PrintFlow.App/Resources/DisplayNames.cs`
- `tests/PrintFlow.Tests/Architecture/MaximumBoundsBoundaryTests.cs`
- `tests/PrintFlow.Tests/Architecture/LocalisationResourceTests.cs`
- `tests/PrintFlow.Tests/Integration/Ui/DimensionsW1AndOutputTests.cs`
- `tests/PrintFlow.Tests/Integration/Ui/SessionSmokeTests.cs`
- `tests/PrintFlow.Tests/Integration/Ui/ViewRenderingTests.cs`

Added:

- `tests/PrintFlow.Tests/Integration/Ui/MaximumBoundsUiTests.cs`
- `tests/PrintFlow.Tests/Integration/Ui/MaximumBoundsRenderingTests.cs`
- `docs/printflow/phase-11400-b1a2b-maximum-bounds-operator-ui.md`

No runtime database, synthetic file, screenshot, WPF artefact, external preset/evidence file or
Photoshop file is committed.

---

## Notes attached to this PASS

1. **A4 and A5 are presented as maximum boxes, not as long-edge limits.** The signed preset states
   long-edge limits for them (280 mm / 135 mm) which are not yet wired into any executable path;
   presenting them as long-edge while a paper-size box is applied would misdescribe the run.
   The long-edge presentation rule is implemented and tested, and applies the moment a preset
   states a single limit. Deferred — §2.
2. **`SessionView` gained a positional record parameter.** This is a shared read-model change, so
   the `Unit.Workflow` and `Integration.Persistence` suites were run beyond the strictly focused
   set. No workflow rule, schema, migration or precondition changed — §11.
3. **Human visual inspection has not been done.** Rendering is asserted only for binding
   correctness and fit at the representative viewport, and the zh-CN wording has not been read by a
   Chinese operator. Final QA's.

---

**11400-B1A.2B PASS WITH NOTES — READY FOR PRODUCTION PHOTOSHOP SIZE PREPARATION**
