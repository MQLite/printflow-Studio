# Epic 11400 Part B1A.1 — Photoshop Resampling and Aspect-Ratio Contract Acceptance

**Verdict: 11400-B1A.1 NOT READY — RESAMPLING OR DIMENSION CONTRACT UNRESOLVED**

This acceptance slice completed controlled native discovery, the synthetic 24-cell shrink matrix,
objective output validation, a live resolution-only check, and the current two-dimension model
audit. It stops before accepted immutable evidence and preset creation because the required human
quality review, fixed-method selection, authoritative-axis approval, and arithmetic-realization
approval have not been provided. Approval is not inferred from running the requested work.

No production resize seam was added. No production Photoshop document was inspected or mutated,
no W1 Action ran, no CMYK conversion occurred, no TIFF was saved, and no AdapterOutput or Revision
was created.

## 1. B1A blocker

The historical B1A report remains intact and uncommitted. Preset v1.9.0 requires proportional,
shrink-only resizing at 300 PPI but selects no interpolation method and carries no executable
source-ratio derivation. `PrintDimensions` still trusts two independently entered physical
dimensions. Those blockers remain product/evidence decisions rather than production-code gaps.

This slice adds two further acceptance facts:

- automated correctness checks cannot certify the visible quality of either candidate; and
- the written midpoint rule needs an exact numeric realization. With ordinary binary `double`
  arithmetic, an exact derived value of 500.5 can evaluate as 500.49999999999994 and round to 500,
  contradicting mathematical midpoint-away-from-zero, which would produce 501.

## 2. Candidates evaluated

Exactly the requested initial candidates were evaluated:

| Candidate | Role | Live Photoshop route exercised | Acceptance |
| --- | --- | --- | --- |
| Bicubic Sharper | recommended first reduction candidate | `Document.resizeImage(..., ResampleMethod.BICUBICSHARPER)` | pending human review |
| Bicubic | fixed alternative if Sharper creates objectionable halos/ringing | `Document.resizeImage(..., ResampleMethod.BICUBIC)` | pending human review |

Preserve Details/2.0, Bicubic Smoother, Automatic, Nearest Neighbor, and Bilinear were not added.
No content-type detection or automatic switching was evaluated.

## 3. Native Photoshop CC 2019 identifiers

Discovery attached through the Running Object Table to the already-running exact object. It did
not activate either registered LocalServer and did not start another Photoshop process.

| Runtime fact | Observed |
| --- | --- |
| process | PID 6272, started 2026-08-28 09:56:22 local |
| executable | `D:\Adobe Photoshop CC 2019\Photoshop.exe` |
| product / COM version | 20.0 / 20.0.10 |
| executable SHA-256 | `81EE8930FC1E28637B501866A8B946FA0740C376CDA4302FEA61AA82806A80C5` |
| ROT moniker observed | `!{0E9AAF8C-058B-433D-A42B-5B98325FA81C}` |

The live object resolved these identifiers:

| Meaning | DOM/ExtendScript | Action Manager representation |
| --- | --- | --- |
| Image Size event | `Document.resizeImage` | event `ImgS` / `imageSize`, type ID 1231906643 |
| width | method width argument | key `Wdth` / `width`, type ID 1466201192 |
| height | method height argument | key `Hght` / `height`, type ID 1214736500 |
| resolution | numeric PPI argument | key `Rslt` / `resolution`, type ID 1383296116 |
| interpolation key/type | resample-method argument | key `Intr`, enum type `Intp` / `interpolationType` |
| Bicubic | `ResampleMethod.BICUBIC` | `Bcbc` / `bicubic`, type ID 1113809507 |
| Bicubic Sharper | `ResampleMethod.BICUBICSHARPER` | string ID `bicubicSharper`, type ID 2327 |
| no resampling | `ResampleMethod.NONE` | the accepted route for this slice is the explicit DOM enum, not remembered dialog state |

The tempting four-character value `BcbS` is **not** Bicubic Sharper on this object: it resolved to
type ID 1113809491, while `bicubicSharper` resolved to 2327. They are not equivalent and `BcbS`
must not be used as guessed production authority.

The comparison invoked the object-scoped DOM identifiers, not a localized display string and not
an Action Manager command against an unverified active document. No fifth `amount` argument or
other sharpening control was supplied. Bicubic Sharper's inherent sharper interpolation is the
candidate under review; no separate extra-sharpening step was requested or applied.

The installed UI is Chinese, but the Windows capture helper could not inspect the CC 2019 window
(`SetIsBorderRequired` returned interface-not-supported). No blind shortcut was sent and no exact
localized candidate spelling is claimed. Display text is non-authoritative; the executable DOM
and Action Manager identifiers above came directly from the 20.0.10 object.

## 4. Fixtures and shrink matrix

Controlled workspace (outside Git):
`D:\PrintFlowStudio\Comparison\11400-B1A1`. Originals remain under `Sources`, outputs under
`Outputs`, and objective transcripts under `Evidence`. All fixtures are deterministic synthetic
PNG files, 2400×1800 pixels, 8-bit RGB/RGBA, with no W1 channel and no customer content.

| Fixture | Category / alpha | Source SHA-256 |
| --- | --- | --- |
| `01-transparent-text-logo.png` | anti-aliased text/logo; transparent and semi-transparent RGBA edges | `2B76F616CE4E398994019690E3FE6AECB3A89B2C73C17374B103E403AF6D0F66` |
| `02-fine-lines-small-text.png` | fine 1–12 px lines and small text; opaque RGB | `6D387523315340C4CB79A1F7AB2BAA3341C75CB4B6D37A410CA07F747155AAFD` |
| `03-flat-colour-graphic.png` | high-contrast solid/flat-colour edges; opaque RGB | `B3E76DC3A3C5565A48A9C31732A11848DAA8A181FC0510657029675F58FFE8CA` |
| `04-detailed-raster.png` | deterministic detailed photographic/raster texture; opaque RGB | `7F042A784609FE411383E7EE0F97B98E5A029DF062FC30A03114077F2A4C3FE8` |

Each fixture was freshly reopened for each candidate and each ratio. Exact targets were 1800×1350
(75%), 1200×900 (50%), and 600×450 (25%), producing 24 distinct comparison outputs. The DOM call
also set 300 PPI. It did not crop, extend canvas, convert colour, run W1, apply post-processing, or
overwrite a source.

Objective validation passed every cell:

- target pixels were exact and remained exactly 4:3;
- RGB mode and 8-bit depth were unchanged;
- three RGB component channels were unchanged and W1 was absent;
- RGBA output retained zero, partial, and full alpha for the transparency fixture;
- opaque fixtures remained opaque RGB;
- all PNGs decoded successfully;
- candidate decoded-pixel hashes differed for every fixture/ratio pair;
- all four original SHA-256 values remained unchanged.

These checks establish correct comparisons, not quality acceptance.

## 5. Human quality results

Human review is pending. The outside-Git worksheet is
`D:\PrintFlowStudio\Comparison\11400-B1A1\OPERATOR-REVIEW.md`; every matrix cell remains
`PENDING — human reason required` for both Bicubic Sharper and Bicubic.

The operator must inspect all 12 pairs at 100% and, where useful, fit view, judging halos, edge
ringing, transparent-edge contamination, line continuity, small-text legibility, jaggies,
softening, tonal-gradient damage, and flat-colour edge cleanliness. Automated metrics and this
agent do not self-certify those judgments.

## 6. Accepted method or unresolved decision

No method is accepted. Bicubic Sharper remains the recommended first candidate, while Bicubic is
the fixed fallback if the human matrix finds representative over-sharpening or halos. Exactly one
must be selected explicitly. If neither passes every representative category, the result remains
NOT READY and the one-fixed-method product policy must change before implementation; Automatic is
not an implicit fallback.

## 7. Resolution-only behavior

The exact live route exercised on Photoshop 20.0.10 was:

```javascript
document.resizeImage(undefined, undefined, 300, ResampleMethod.NONE);
```

On the fresh flat-colour fixture:

| Fact | Before | After |
| --- | ---: | ---: |
| pixels | 2400×1800 | 2400×1800 |
| resolution reported by Photoshop | 72 PPI | 300 PPI |
| mode | RGB | RGB |
| bit depth | 8 | 8 |
| channels | red, green, blue components | unchanged |

The saved comparison copy decoded to exactly the same RGB byte sequence before and after. Both
decoded-pixel SHA-256 values were
`08954B16EC795D6AC3A768FA259B069AAC44EED7B3ECD071A81CB1F8A5F9ADE0`.
PNG stores resolution as integer pixels per metre, so independent file inspection reports
299.9994 PPI; Photoshop itself reported 300. The source backing file hash remained unchanged.

This proves the explicit DOM no-resample route for the tested 8-bit RGB case. The future production
seam must still verify before/after mode, bit depth, channels, pixel dimensions, and 300 PPI on the
exact managed document.

## 8. Aspect-ratio and rounding proposal

The product checkpoint is the requested model:

1. The operator explicitly selects authoritative `Width` or `Height` and supplies only that value
   in millimetres.
2. `authoritativeTargetPixels = RoundAwayFromZero(enteredMillimetres / 25.4 × 300)`.
3. `scale = authoritativeTargetPixels / correspondingSourcePixels`; require `0 < scale <= 1`.
4. `derivedOtherPixels = RoundAwayFromZero(otherSourcePixels × scale)`.
5. `derivedOtherMillimetres = derivedOtherPixels / 300 × 25.4` and is read-only.
6. Both target pixel dimensions must be positive integers. No stretch, crop, canvas extension,
   enlargement, or second independently authoritative dimension is allowed.

For executable determinism, approval must also state that entered decimal millimetres are converted
with exact decimal arithmetic and the derived expression is evaluated as the exact rational
`otherSourcePixels × authoritativeTargetPixels / correspondingSourcePixels` before midpoint-away
rounding. This preserves the written formula while avoiding binary floating-point midpoint drift.
The present `double`-based type does not provide that guarantee. No implementation was added.

## 9. Authoritative-axis rule

Proposed typed values are exactly:

```text
PrintDimensionAxis.Width
PrintDimensionAxis.Height
```

The decision is per output, explicit, persistable, and auditable. It is never inferred from the
last edited textbox, larger dimension, orientation, filename, or preset. This model is pending
operator/product approval.

## 10. Ratio and rounding verification matrix

The following conceptual results use exact decimal/rational arithmetic and mathematical
midpoint-away-from-zero:

| Case | Source px | Authority / entered mm | Raw → target authority px | Scale | Raw → derived px | Result |
| --- | ---: | --- | ---: | ---: | ---: | --- |
| landscape | 6000×4000 | Width / 254 | 3000 → 3000 | 0.5 | 2000 → 2000 (169.333333 mm) | accept |
| portrait | 4000×6000 | Height / 254 | 3000 → 3000 | 0.5 | 2000 → 2000 (169.333333 mm) | accept |
| square / exact integers | 3600×3600 | Width / 152.4 | 1800 → 1800 | 0.5 | 1800 → 1800 (152.4 mm) | accept |
| fractional authority | 2400×1600 | Width / 100 | 1181.102362… → 1181 | 0.492083333… | 787.333333… → 787 (66.632667 mm) | accept |
| exact midpoint on both rounds | 2000×1000 | Width / 84.709 | 1000.5 → 1001 | 0.5005 | 500.5 → 501 (42.418 mm) | accept |
| scale exactly 1 | 3000×2000 | Width / 254 | 3000 → 3000 | 1 | 2000 → 2000 | accept; resolution-only if both pixels already match |
| scale slightly below 1 | 3001×2000 | Width / 254 | 3000 → 3000 | 0.999666777… | 1999.333555… → 1999 | accept |
| requested enlargement | 2999×2000 | Width / 254 | 3000 → 3000 | 1.000333444… | not evaluated authoritatively | reject before Photoshop |

The formulas use numeric values, not formatted strings, so calculation is culture-independent.
UI parsing still needs one explicit culture/decimal-normalization rule in the future implementation.
Inputs that round either target axis below one pixel are rejected by the positive-pixel rule.

## 11. Existing two-dimension model and future change

No current code or schema was modified.

- `SessionViewModel` exposes editable `WidthMmText` and `HeightMmText`, parses both, and calls
  `PrintDimensions.TryFromMillimetres`.
- `PrintDimensions` stores `double WidthMm`, `double HeightMm`, and independently rounds both to
  pixels without source dimensions or an axis.
- `WorkflowCommand.SetPrintDimensions` and `WorkflowEffect.PersistPrintDimensions` carry that pair.
- SQLite Session rows persist width mm, height mm, width pixels, height pixels and preset; output
  rows persist both target millimetres and pixels. Mappers reconstruct from the two millimetres.
- No authoritative axis or original entered-only millimetre value is persisted.

The future B1A implementation must persist the axis and entered millimetres, derive the other axis
from exact source pixels, and ensure the displayed/persisted pair is derived rather than trusted.
Existing independent pairs must be rejected or safely migrated/revalidated. This slice does not
invent that migration or change SQLite.

## 12. Rejected alternatives and known limitations

- Preserve Details/2.0 is enlargement-oriented and was not evaluated as shrink policy.
- Bicubic Smoother, Automatic, Nearest Neighbor, and Bilinear were outside the accepted candidate set.
- Remembered Photoshop preferences and localized labels are not automation identifiers.
- Guessed Action Manager `BcbS` is demonstrably not `bicubicSharper` on CC 2019.
- Binary-double midpoint behavior is not accepted as an implementation of exact decimal/rational
  midpoint-away-from-zero.
- Visual quality remains unknown until the human matrix is completed; inherent Bicubic Sharper
  edge enhancement may produce halos, while Bicubic may be visibly softer.
- Resolution-only equality was live-proved on one 8-bit RGB synthetic fixture, not every Photoshop
  mode/bit-depth combination. The production seam must remain fail-closed for unsupported inputs.

## 13. Evidence and preset changes

No accepted immutable resize-contract evidence was created because explicit approval is absent.
The controlled comparison metadata (`fixture-manifest.json`, `comparison-results.tsv`,
`resolution-only-results.tsv`, and `objective-validation.json`) is draft local test evidence outside
Git and does not claim product acceptance.

Preset `printflow-workstation-v1.9.0.json` remains read-only and unchanged at configured SHA-256
`0DA89F8FE4067574FD7568F1FA8D1C0F2000469E3059FEE28BFDB0C867B5FB58`. `appsettings.json` still
selects v1.9.0 and `Adapters.Mode` remains `Fake`. No v1.10.0 preset, pointer update, or sign-off was
created. The canonical `.atn` was not read for execution, changed, loaded, or invoked.

## 14. Targeted checks

The complete suite was not run.

```text
dotnet test tests\PrintFlow.Tests\PrintFlow.Tests.csproj --no-build \
  --filter "FullyQualifiedName~WorkstationPresetProviderTests"
```

Result: **6 passed, 0 failed, 0 skipped**.

```text
dotnet test tests\PrintFlow.Tests\PrintFlow.Tests.csproj --no-build \
  --filter "FullyQualifiedName~PrintDimensions"
```

Result: **19 passed, 0 failed, 0 skipped**. These are the existing two-dimension contract and
related persistence checks; no proposed one-axis calculation was encoded.

The required normal solution build used the repository-pinned user-local .NET SDK 10.0.400:
**0 warnings, 0 errors**. The system-wide `dotnet` contains only SDK 8.0.418 and could not resolve
`global.json`; repository configuration was not changed. Dependency files are unchanged, so the
vulnerability audit remains deferred to Epic 11400 Final QA.

## 15. Remaining B1A implementation scope

Before production implementation begins:

1. a human must complete all 24 PASS/FAIL quality judgments with reasons;
2. the operator must select exactly Bicubic Sharper or Bicubic;
3. the operator/product owner must approve the explicit per-output Width/Height axis model;
4. exact decimal input and rational derived-pixel arithmetic must be approved (or an alternative
   that produces the stated midpoint results must be specified);
5. known visual limitations must be accepted;
6. only then may immutable evidence and the next semantic preset version be created and rehashed.

After those acceptance steps, B1A may implement the operation-specific resize seam and focused
tests. It still must not run W1, convert to CMYK, save the production TIFF, return AdapterOutput,
create Revision, or start Epic 11500 in this acceptance slice.

## 16. Git state

Preflight began on `master` at `005c97e`, ahead of `origin/master` by 14 commits, with the historical
B1A NOT READY report as the only untracked file. That report remains intact and uncommitted.

This B1A.1 report is the only intended repository addition from this slice. No source, test,
project, dependency, lock, migration, configuration, preset, sign-off, or canonical Action file
changed. Synthetic sources, comparison outputs, scripts, transcripts, and review worksheet remain
under `D:\PrintFlowStudio\Comparison\11400-B1A1` outside Git. No commit, amend, rebase, history
rewrite, or push occurred.

11400-B1A.1 NOT READY — RESAMPLING OR DIMENSION CONTRACT UNRESOLVED

---

# R2 — Simplified Photoshop Fit-Within-Bounds Contract Acceptance

The historical NOT READY result above is preserved verbatim. Product policy is now clarified and
removes both unresolved decisions: the operator does not choose an authoritative axis or a
resampling method. PrintFlow selects the limiting edge from maximum millimetre bounds, Photoshop
keeps dimensions linked and derives the other edge, and shrinking always uses Bicubic Sharper.

This remains a contract/evidence slice. No production Photoshop resize route was added or invoked;
no managed Working document was opened or mutated; no W1 Action ran; no CMYK conversion, save or
export occurred; and no AdapterOutput or Revision was created. `Adapters.Mode` remains `Fake`.

## R2.1 Operator clarification

The accepted default operator unit is millimetres. Operators do not enter pixels, select an
authoritative Width/Height axis, or select/compare interpolation methods. The 24 diagnostic outputs
and 12 A/B pairs from the earlier investigation may remain outside Git, but they are not product or
operator acceptance evidence and are not referenced by the new evidence.

## R2.2 Maximum-bound semantics

`WidthMm` and `HeightMm` are accepted as operator-facing maximum bounds, exposed explicitly as
`MaxWidthMm` and `MaxHeightMm`. They are not two independently exact final output dimensions. The
existing independently converted `PixelWidth`/`PixelHeight` values remain temporarily for the
pre-existing UI and persistence representation, but are documented as non-executable: they must
not be sent to Photoshop as a target pair.

The pure `FitWithinBounds` calculation consumes source pixels and the millimetre limits. It returns
only the operation mode and the one limiting edge/value that a future Photoshop operation may
write. Its projected pair exists solely to prove that the selected edge fits the other bound; the
actual dimensions and pixels read back from Photoshop will be production authority.

## R2.3 Automatic limiting-edge calculation

For a maximum-width/maximum-height box, the accepted rule is:

```text
sourceRatio = sourceWidthPixels / sourceHeightPixels
boxRatio    = MaxWidthMm / MaxHeightMm

sourceRatio >= boxRatio  ->  Width is limiting; write MaxWidthMm
sourceRatio <  boxRatio  ->  Height is limiting; write MaxHeightMm
```

The implementation compares the positive cross-products rather than dividing the ratios. Equality
selects Width exactly as the product rule requires. Only that one edge is writable; Photoshop must
derive the other with Constrain Proportions enabled. Stretching, cropping, canvas extension,
padding, enlargement and orientation changes remain prohibited.

## R2.4 Long-edge-only behavior

For A4 (`maxLongEdge = 280 mm`) and A5 (`maxLongEdge = 135 mm`), the source document's current long
edge is selected. If shrinking is required, only that source edge is set to the configured maximum.
The short edge remains Photoshop-derived. A square tie selects Width deterministically without
changing orientation.

## R2.5 Already-within-limit behavior and no enlargement

Natural physical dimensions are evaluated at the fixed production resolution:

```text
sourceWidthMm  = sourceWidthPixels  * 25.4 / 300
sourceHeightMm = sourceHeightPixels * 25.4 / 300
```

When both dimensions already fit, or the long edge already fits its long-edge-only limit, no edge
is forced to its maximum and no enlargement occurs. The accepted operation is
`ResampleMethod.NONE`: preserve the source pixel dimensions exactly and set only the resolution
metadata to 300 PPI.

## R2.6 Shrink behavior

When the source exceeds its bounds, the fixed internal method is
`ResampleMethod.BICUBICSHARPER` (`BICUBICSHARPER` in evidence). Photoshop Automatic, remembered
dialog state, Preserve Details, Bicubic Smoother and content-dependent switching are prohibited.
The operator has no method selector and no visual-comparison gate.

The future native operation must reverify the exact managed Working document by absolute path,
require linked millimetre dimensions, set 300 PPI, write only the automatically selected edge,
apply once, and read the resulting facts. This slice deliberately does not implement that route.

## R2.7 Result validation and canonical audit contract

Future implementation must require both actual edges to remain within their maxima; when shrinking,
the limiting edge must equal its maximum within an accepted Photoshop numeric tolerance. The other
edge must be Photoshop-derived and proportional within integer-pixel rounding tolerance. It must
also prove 300 PPI, no enlargement, unchanged colour mode/bit depth/channels, no W1, and an unchanged
document path. It must not require both edges to equal the box.

The canonical audit record will carry `MaxWidthMm`, `MaxHeightMm`, the selected preset/custom limit,
automatic `LimitingEdge`, the one value written, actual Photoshop Width/Height in millimetres,
actual pixel width/height, 300 PPI, and `NONE` or `BICUBICSHARPER`. The Photoshop-returned pixel pair
becomes the production result; PrintFlow does not command an independently calculated pair.

## R2.8 Immutable evidence and preset v1.10.0

New local immutable evidence was created at:

```text
D:\PrintFlowStudio\Baseline\workstation-v1\apps\photoshop-2019\resize-contract.json
SHA-256 74E1ABC5B522F013EA5901235B1AA59FEAF958BF749598C7C1A2949E2AA5EC98
```

It records millimetres, maximum-box and long-edge semantics, automatic edge selection, constrained
proportions, one-edge writing, 300 PPI, no enlargement, `NONE`, fixed `BICUBICSHARPER`, no operator
axis/method choice, no comparison acceptance and all result-validation requirements. It contains no
comparison images or image paths and is marked read-only.

Preset v1.9.0 was not edited and still hashes to
`0DA89F8FE4067574FD7568F1FA8D1C0F2000469E3059FEE28BFDB0C867B5FB58`. The next immutable preset was
created at:

```text
D:\PrintFlowStudio\Baseline\workstation-v1\preset\printflow-workstation-v1.10.0.json
SHA-256 27B5E9E08FD7F968ADA2AAD88758FD4CBD0C3F66C39F8CCA60CC36CF0EEDD033
```

All 21 inherited `sourceManifestIntegrity` entries rehashed exactly; the new evidence is the 22nd
verified entry. The canonical `PrintFlow-DTF-v1.atn` remains unchanged at
`A04203EDEA623C0737D911601A3A005033789BD095130F02A5F8C04CBFCD83EE`. The v1.10.0 manifest and its
evidence are read-only. No separate sign-off is required under current project policy.
`appsettings.json` was updated only after final manifest verification and now selects v1.10.0 by
its exact hash. `Adapters.Mode` remains `Fake`.

## R2.9 Targeted verification

The complete suite was not run.

```text
dotnet test tests\PrintFlow.Tests\PrintFlow.Tests.csproj --no-build \
  --filter "FullyQualifiedName~FitWithinBounds"
```

Result: **12 passed, 0 failed, 0 skipped**. This covers wide/tall sources, equal ratios, both A3
orientations, A4/A5 long-edge limits, already-fitting sources, one-edge and two-edge exceedance,
proportional non-limiting results, no enlargement and the `PrintDimensions` maximum-bound bridge.

```text
dotnet test tests\PrintFlow.Tests\PrintFlow.Tests.csproj --no-build \
  --filter "FullyQualifiedName~PrintDimensions"
```

Result: **20 passed, 0 failed, 0 skipped**. Existing conversion, validation, workflow and persistence
coverage remains green alongside the maximum-bound contract test.

```text
dotnet test tests\PrintFlow.Tests\PrintFlow.Tests.csproj --no-build \
  --filter "FullyQualifiedName~WorkstationPresetProviderTests|FullyQualifiedName~AcceptedNamingContractTests|FullyQualifiedName~WorkstationPresetResizeContractEvidenceTests"
```

Result: **13 passed, 0 failed, 0 skipped**. The configured manifest digest, read-only state,
v1.9.0 supersession, all 22 integrity entries, resize evidence semantics, preserved naming contract
and `Adapters.Mode = Fake` are verified.

The required preflight and final normal solution builds used the repository-pinned user-local .NET
SDK 10.0.400. The system-wide installation exposes only 8.0.418 and cannot resolve `global.json`;
no repository SDK configuration was changed. Final normal solution build: **0 warnings, 0 errors**.
Dependency and lock files are unchanged, so no vulnerability audit was run.

## R2.10 Remaining B1A implementation scope

B1A may now implement the operation-specific Photoshop preparation seam, exact managed-document
identity recheck, the native one-edge Image Size call, post-operation fact reads and validation,
canonical result/audit persistence, and the required UI/persistence migration from the legacy
independent-pixel representation to maximum-bound/actual-result semantics. UI labels should make
the limits explicit in English and natural zh-CN.

That later implementation must remain separate from B1B and Part C: it must not invoke W1, convert
to CMYK, save/export, create AdapterOutput or create Revision. Global Production mode remains
disabled.

## R2.11 Git state

Preflight began on `master` at `005c97e`, ahead of `origin/master` by 14 commits, with the historical
B1A NOT READY report and historical B1A.1 NOT READY report untracked. Both historical reports are
preserved; this R2 result is appended to the latter. The repository changes are the maximum-bound
contract documentation and pure calculator, focused tests, the acceptance-report append, and the
v1.10.0 `appsettings.json` pointer. The immutable preset/evidence remain in the established local
baseline outside Git.

No commit, amend, rebase, history rewrite or push occurred. No dependency, lock, migration, UI,
production adapter, canonical Action, comparison output, customer image, TIFF, AdapterOutput or
Revision changed.

11400-B1A.1 PASS — FIT-WITHIN-BOUNDS CONTRACT ACCEPTED; READY FOR B1A IMPLEMENTATION
