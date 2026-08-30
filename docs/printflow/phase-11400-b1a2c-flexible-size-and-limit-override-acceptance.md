# Epic 11400 Part B1A.2C-R2 — Flexible Size and Explicit Limit Override Acceptance

Status: **PASS**

This acceptance slice supersedes only v1.10.0's hard-limit-only assumptions. Named presets remain
the default operator path, while an operator may now choose one custom target edge, explicitly
override a preset recommendation, and separately authorise a real pixel enlargement for one exact
reviewed image and target. The operator still works in millimetres at fixed 300 PPI, Photoshop
keeps proportions linked, and PrintFlow will write only one edge.

No production Photoshop size-preparation adapter, workflow command, UI, migration, Attempt
persistence, AdapterOutput, Revision, W1 action, CMYK conversion, save, export, or Production mode
was added. `Adapters.Mode` remains `Fake`.

## 1. Product-policy change

Preset limits are recommendations rather than absolute barriers, and enlargement is no longer
categorically prohibited. Neither change is silent: a preset override must be recorded as an
override, and enlargement requires a second explicit authority after the projected result is
known. Existing v1.10.0 `FitWithinBoundsV1` decisions remain valid under their original
shrink-or-resolution-only semantics and gain no enlargement permission.

Preserved throughout: millimetres, 300 PPI, proportional dimensions, one-edge Photoshop input,
no stretch, no crop, no canvas extension, and no operator choice of interpolation method.

## 2. Preset versus custom modes

Exactly two typed selections exist:

- `PresetFit`: a named `SizePreset`; no axis or custom millimetre value exists on this shape.
  Existing maximum-box or maximum-long-edge logic remains `FitWithinBounds`' authority.
- `CustomTargetEdge`: one `Width`, `Height`, or `LongEdge` plus one positive decimal millimetre
  value. A second independently authoritative dimension cannot be represented.

An ordinary preset cannot be passed to the new `TargetEdgeV1` planner. This prevents an existing
`MaxBoundsV1` plan from being reinterpreted as an exact custom target.

## 3. Preset override

`FlexibleSizeSelection.OverridePreset` retains all four facts together: the named preset, the
explicit override flag, the configured recommendation, and the requested target edge/value. For
the A4 example, the record therefore continues to say A4 / 280 mm while also saying LongEdge /
320 mm. The implementation does not replace or rewrite the recommendation.

`PresetLimitExceeded` means only that the requested override is greater than the recorded preset
recommendation. It does not imply that pixels must be enlarged.

## 4. Target-edge semantics

`ScaleToTargetEdge` is a pure Domain calculator separate from `FitWithinBounds`.

- Width writes Width; Photoshop derives Height.
- Height writes Height; Photoshop derives Width.
- LongEdge resolves to Width for landscape, Height for portrait, and Width for a square tie.

The result retains both the operator's selected edge (`LongEdge` remains `LongEdge`) and the
concrete edge Photoshop will eventually receive. It returns a projected pair for planning and
warning evidence only; production Photoshop read-back remains future authority.

## 5. Preset limit versus source capacity

The two classifications are independent and tested:

- A4 recommendation 280 mm, requested LongEdge 320 mm, source 6000×4000 px:
  `PresetLimitExceeded = true`, `SourceCapacityExceeded = false`.
- The same selection against 3000×2000 px:
  `PresetLimitExceeded = true`, `SourceCapacityExceeded = true`.

Only the first classification needs the explicit preset override already represented by the size
selection. Only the second needs enlargement authority.

## 6. Explicit enlargement authority

`EnlargementAuthority` can be created only for an `Enlarge` plan and binds exactly:

- source `RevisionId` and SHA-256;
- `CustomTargetEdge` sizing mode;
- selected target edge;
- requested decimal millimetres;
- reduced exact projected scale ratio;
- projected target pixel width and height.

`TargetEdgePrintPreparationPlan.IsExecutableWith` returns false for enlargement without matching
authority. Tests prove a changed Revision, changed hash, changed requested millimetres, or changed
edge invalidates authority. Resolution-only and shrink plans need no enlargement authority, and
cannot acquire one. The type is pending Domain state only; no Session or database field was added.

## 7. Fixed internal resize methods

The direction is selected by exact comparison of authoritative target pixels with the matching
source pixels:

| Direction | Exact comparison | Fixed internal policy | Photoshop identifier for future mapping |
| --- | --- | --- | --- |
| `ResolutionOnly` | target = source | `None` | `ResampleMethod.NONE` |
| `Shrink` | target < source | `BicubicSharper` | `ResampleMethod.BICUBICSHARPER` |
| `Enlarge` | target > source | `PreserveDetails` | `ResampleMethod.PRESERVEDETAILS` |

`FlexibleSizeSelection` exposes no resampling/interpolation property or factory parameter. The
method is fixed after calculation and will not be shown to the shop operator.

## 8. Native Photoshop enlargement identifier

The exact accepted object was found through the Running Object Table, without activating another
registered Photoshop server:

| Fact | Verified value |
| --- | --- |
| application | Adobe Photoshop CC 2019 |
| version | `20.0.10` |
| path | `D:\Adobe Photoshop CC 2019` |
| ROT moniker | `!{0E9AAF8C-058B-433D-A42B-5B98325FA81C}` |
| executable SHA-256 | `81EE8930FC1E28637B501866A8B946FA0740C376CDA4302FEA61AA82806A80C5` |
| exercised enlargement identifier | `ResampleMethod.PRESERVEDETAILS` |

Photoshop also resolved string IDs named `preserveDetailsUpscale` (type ID 2329) and
`preserveDetails` (type ID 3425), but neither was guessed as equivalent or accepted merely because
it resolved. The accepted identifier is the DOM enum actually exercised successfully.

## 9. Projected pixels and scale

The requested decimal is converted to an exact integer rational and calculated as:

```text
targetPixels = round-half-away-from-zero(requestedMillimetres × 1500 / 127)
otherPixels  = round-half-away-from-zero(
                 sourceOtherPixels × targetPixels / sourceAuthoritativePixels)
```

Rounding compares `2 × remainder` with the positive denominator, so binary floating-point
midpoint behaviour is not involved. The exact-midpoint test uses 84.709 mm against a 2000×1000
source and produces 1001×501 px. Scale is retained as a reduced integer ratio; direction equality
uses integer comparison, not a tolerance.

## 10. Operator warning

The immutable evidence records the future warning contract:

> This size requires enlarging the image to {Scale}% at 300 PPI. Enlargement may reduce image
> clarity.

> 此尺寸需要在 300 PPI 下将图片放大至 {Scale}%。放大可能降低图片清晰度。

The future actions are Return and change size / Continue with this size. The warning names no
internal method and makes no quality guarantee. Selecting a larger size does not itself perform
the second action or create authority.

## 11. Targeted tests

The complete suite was not run.

| Focused set | Result |
| --- | ---: |
| `ScaleToTargetEdgeTests`, `FlexibleSizeSelectionTests`, `EnlargementAuthorityTests`, unchanged `FitWithinBoundsTests` | 38 passed |
| `WorkstationPresetResizeContractEvidenceTests`, `WorkstationPresetProviderTests` | 11 passed |

The final focused rerun and normal solution build are recorded in §13 after all repository files
were complete. No persistence, migration, workflow state, shared UI/read-model boundary, project,
package, or lock file changed, so testing was not widened and the vulnerability audit remains
deferred to Epic 11400 Final QA.

## 12. Live technical verification

Only the synthetic
`D:\PrintFlowStudio\Comparison\11400-B1A1\Sources\03-flat-colour-graphic.png` was opened. Its
absolute Photoshop `fullName` matched before mutation. The controlled document was enlarged by
writing Width only; Photoshop derived the linked Height.

| Fact | Before | After |
| --- | ---: | ---: |
| pixels | 2400×1800 | 3000×2250 |
| resolution | 72 PPI | 300 PPI |
| mode | RGB | RGB |
| bit depth | 8 | 8 |
| component channels | 3 | 3 |
| W1 | absent | absent |

Five operator-owned documents were open before and five remained afterward. The original active
document was restored. The synthetic document alone was closed with do-not-save; no comparison
copy was created. The backing source hash remained
`B3E76DC3A3C5565A48A9C31732A11848DAA8A181FC0510657029675F58FFE8CA`. No W1 action, CMYK
conversion, save, export, AdapterOutput, or Revision occurred.

## 13. Evidence and preset version

New accepted external evidence (read-only):

`D:\PrintFlowStudio\Baseline\workstation-v1\apps\photoshop-2019\flexible-size-and-enlargement-contract.json`

SHA-256:
`AAEEC6AD6ABF3C9AC65F0F3862A25BBE55B06CAB92F0B3CDE6EA09F06276384A`

New immutable preset (read-only):

`D:\PrintFlowStudio\Baseline\workstation-v1\preset\printflow-workstation-v1.11.0.json`

SHA-256:
`A6E5DC172817F2F992114A1FDE0DCBAACC80D9CADD148C37D25CA3F816AC8AD1`

The preset supersedes v1.10.0 at its exact prior hash. All 22 inherited integrity entries and the
new evidence entry reverified (23/23). The canonical `PrintFlow-DTF-v1.atn` remained exact at
`A04203EDEA623C0737D911601A3A005033789BD095130F02A5F8C04CBFCD83EE`. v1.10.0 remains read-only
and exact at `27B5E9E08FD7F968ADA2AAD88758FD4CBD0C3F66C39F8CCA60CC36CF0EEDD033`.

`appsettings.json` now selects v1.11.0 by the final hash. `Adapters.Mode` remains `Fake`.

Final verification:

- focused flexible-size, authority, legacy fit/plan, and preset/evidence tests: **72 passed, 0 failed, 0 skipped**;
- normal solution build: **0 warnings, 0 errors**.

## 14. Remaining implementation, persistence, and UI scope

The implementation slice must still add the operator UI and workflow sequence, persist the sizing
semantics/selection/projection and exact enlargement authority, snapshot those facts immutably on
the producing Attempt, reject stale or unsupported plans before Attempt creation, map the fixed
policies in Infrastructure, drive the exact managed Photoshop document, and read actual Photoshop
geometry back.

Still not started here: migration, production Photoshop resizing, W1, CMYK, TIFF save/export,
AdapterOutput, output Revision, and global Production mode.

## 15. Git state

Work began on `master` at `9168b87`, ahead of `origin/master` by 20 commits, with a clean worktree.
The repository changes are limited to pure Domain contract types, focused Domain/preset tests,
the v1.11.0 appsettings pointer, and this report. Historical B1/B1A reports, B1A.1 history,
v1.10.0, the canonical Action, and `Adapters.Mode = Fake` remain unchanged.

No synthetic image, comparison output, Photoshop workspace, screenshot/UI dump, external
preset/evidence file, or smoke transcript was added to Git. No commit, push, amend, rebase, or
history rewrite occurred.

**11400-B1A.2C PASS — FLEXIBLE SIZE AND LIMIT OVERRIDE CONTRACT ACCEPTED; READY FOR IMPLEMENTATION**
