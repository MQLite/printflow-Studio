# PF-ACCEPT-A1 — Regression v3 fine-hair trim contract

**Complete as an offline change; the A1 run it enabled is Pending on three Operator reviews.**
The operator chose option (b) from the completed fine-hair diagnosis: a separately versioned
`printflow-regression-v3` set whose `FIX-FINE-HAIR-001` structural property is the deterministic
trim contract. This is an explicit expectation change, not a Product bug fix, an artwork approval
or a rewrite of the failed v2 result. v2 is not described as incorrectly implemented: it enforced
what it froze, and that requirement is unattainable for these cutouts.

Implementation commit: `a39846d8e0398e061e445fc79fe219b9bb195401`. No `src/` file changed.

## Exact v2-to-v3 difference

| Area | v2 | v3 |
|---|---|---|
| Set identity | `printflow-regression-v2`, `v2` | `printflow-regression-v3`, `v3` |
| Local paths | `D:\PrintFlowStudio\TestData\v2\...` | `D:\PrintFlowStudio\TestData\v3\...` |
| Fine-hair property | `trimBoundsStrictlyInsideCanvas: true` | `trimMatchesAlphaBoundsAndMargins: true` |
| Fine-hair comparison prose | "the trim found bounds inside the canvas" | the exact alpha-bounds/margin/crop contract below |

Normalized comparison of all seven manifests found no other difference: the other six are
identical after set-id, fixture-version and root normalization. Schema stays 2; the manifest schema
version and the set version are separate concepts and only the latter moved. The portrait keeps
v2's two-axis `enhancedOutputIsNotSmallerThanSource`; `TRANSPARENT_PNG` keeps its fixed expected
rectangle x=229, y=1210, 2724x3685 and still runs the real internal Trim; every manual check,
comparison policy, privacy rule, provenance and external-application text is unchanged.

The nine copied files are byte-identical to their current v2 sources (seven category files plus
the two fixed references). No image, PSD, PDF or prior AI output was regenerated, and no run
directory, review, claim or failed output was copied. Evidence:
`artifacts/pf-accept-a1/regression-v3/v3-copy-comparison.json` and `v2-to-v3-semantic-diff.txt`.

## What the new check actually verifies

`RegressionTrimGeometry.Verify`, called by the existing fine-hair case, uses each new run's own
successful Trim attempt, its exact pre-Trim input Revision and output Revision, the files whose
SHA-256 match those Revisions, and the attempt's recorded margin. A missing attempt, margin,
geometry, Revision, file or digest fails the check; nothing is guessed or substituted.

It then computes the expectation independently of the Product's crop calculation:

1. decode the actual pre-Trim PNG and find the minimal rectangle containing every `alpha > 0`
   pixel, with no threshold, in input coordinates;
2. expand by the attempt's recorded per-edge margins and clamp to the input canvas;
3. compare that with the persisted `ContentBounds` and `AppliedBounds`;
4. require the decoded output's dimensions to equal the computed applied rectangle and its decoded
   samples to equal exactly that region of the decoded input;
5. require the verified pair to be the cutout the caller approved and the Trim artefact it exports.

A full-canvas result passes only when the computed rectangle is the full canvas. A no-op copy fails
whenever a removable border exists, and correct dimensions never excuse wrong pixels, a shifted
crop or an overcrop. Comparison is on straight 8-bit BGRA samples with the stored pixel format
preserved and colour management ignored; another stored format is refused rather than converted, so
no conversion, premultiplication, resampling or tolerance can hide a difference. Compressed file
hashes remain artefact identity only and are never used for pixel equality. An input with no
`alpha > 0` pixel fails rather than passing vacuously. Product trim behaviour, the `alpha > 0`
rule, margins, manual-crop policy and `NoChangeRequired` semantics are untouched.

The check does not decide whether alpha-bearing edge pixels are desirable foreground.
`FINE-HAIR-VISUAL-001` remains the Operator's question, and a structural pass leaves the case
Pending until an actual decision exists.

## Version semantics kept explicit

v1 is still refused for a new execution and stays readable for historical review. v2 stays
executable and its fine-hair caller keeps the original `trimBoundsInsideCanvas` assertion, with the
same name, rule and message; only v3 manifests select the new check. The generator, the PowerShell
preflight, the C# loader and the case caller all agree on v3 and refuse a missing, false,
non-boolean or conflicting fine-hair property. No historical run is ever re-aggregated under v3's
changed expectation: review re-derivation applies decisions and re-evaluates no assertion.

## Verification

- Focused tests, source build: **113 passed / 0 failed / 0 skipped**
  (`artifacts/pf-accept-a1/regression-v3/test-results/focused.trx`,
  SHA-256 `FF585BA53A26AEBC7E2F8D539D02BE248B9A691B3B4F2C78BDF7FDA27CB3F98A`). The behavioural matrix
  covers full-canvas alpha with a low nonzero extremum, a changed pixel in a same-size output, a
  real removable border, an unchanged copy, a wrong offset, an overcrop, asymmetric margins with a
  canvas clamp, tampered persisted geometry, missing attempt/margin/geometry facts, Revision-byte
  and lineage mismatch, caller binding, attempt selection, a refused pixel format, empty alpha, the
  preserved v2 assertion and an undecided visual check.
- Clean Release solution build: exit 0, **0 warnings / 0 errors** (`release-build.log`).
- One independent read-only review found no blocking issue; its actionable points (caller binding
  to the approved artefacts, the untested refusal paths, weak assertions, decode exception filter,
  a preflight case and a clearer generator failure) were applied before the pair was built.
- Controlled pair `f0ac92e2-853e-4d2f-a6e4-c0af1a24e0e8` from `a39846d`, SDK 10.0.400, 610 inputs,
  input digest `66C4BEFAE2E11FE8DF5D3D521BBA5D5593912F9CE03340AF89AE5124E2E65FBE`, receipt SHA-256
  `BEBEDFFC235F40AE66554F13D0BD0354C20DE75F41FA654341621DA836BFBDC4`; harness build 0 warnings /
  0 errors. Loaded-pair proof 1/1 and paired focused tests 58/58 inside that harness.
- Explicit v3 preflight: **PASS**, seven categories, every hash recomputed, no result written
  (`v3-preflight.log`). Set content digest
  `75DA6EC6306B3C4E59EB1DE8644DA1C22B214AF066DBC2555533B8B6E6ACDEE8`.

No full suite was rerun for this tooling/test-only change, and the inherited 11,899/0/0 is
historical evidence from the Photoshop repair source, not a new v3 result.

## The A1 run this enabled

`a1-v3-20260916-121334-903986e7`, invocation `cbffa3cc-ea7d-4f8b-be52-cb94d8361e17`: seven
categories, no filter, no executable override, no diagnostic mode, no build or restore.
Result **Pending, 4/7 passed, zero failed assertions**. `COMPLEX_BACKGROUND_FINE_HAIR` now passes
its structural check — the independent scan, the recorded margin, the persisted geometry and the
decoded output all agree that the correct crop is the full 1200x1600 canvas — and is Pending only
on its Operator question. Details, hashes and the three review objects are in
`docs/remediation/PF-ACCEPT-A1/HANDOFF.md`.

No production revalidation was written, no publication followed, and no A2, A3, install, deploy,
push, signing or Jira change was performed.
