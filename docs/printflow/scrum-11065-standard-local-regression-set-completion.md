# SCRUM-11065 — Build the Standard Local Regression Image Set

**Date:** 9 September 2026
**Workstation:** DESKTOP-0BG8884, Windows 10 Pro build 19045
**Preset:** `printflow-workstation-v1` 1.16.0 (`6396FB4EB87F…`)
**Product version:** PrintFlow Studio 0.1.0
**Branch:** `master` (local commits only; nothing pushed)

---

## 1. The exact acceptance criteria

Quoted verbatim from row 11005 of
`C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv`, re-read before any change
was made. (The Jira keys are assigned in CSV row order: row 6 is SCRUM-11065, row 64 is
SCRUM-11123, row 1 is the parent Epic SCRUM-11060.)

> **Build the Standard Local Regression Image Set**
>
> Create the fixed local regression set required by the confirmed MVP design, including at least
> a normal JPG portrait, complex background with fine hair, transparent PNG, complete customer
> design, PSD with a compatible composite preview, single-page PDF and a reference production
> TIFF. Record the expected processing path and relevant expected properties for every file.
> Keep all customer-like test data local and suitable for repeatable automated, workstation and
> upgrade regression testing.

Parent, CSV row 11000 → SCRUM-11060, for context on why the set exists:

> Establish and freeze the validated Windows workstation, Meitu, Photoshop, production Action,
> test-image and reference-output assumptions required by the PrintFlow Studio MVP before screen
> automation is implemented. … a reusable local regression image set …

The Epic 11000 final report recorded this task as *"Confirmed by acceptance — mandatory
seven-category regression set waived as a completion gate"*, and preset 1.16.0 carries the same
waiver under `acceptedWaiversAndBusinessDecisions.11005`. The coverage re-audit (line 115)
overrode that as **NOT_IMPLEMENTED**, because a waived gate is not a built set.

---

## 2. Pre-change state — 1 of 7

`D:\PrintFlowStudio\TestData\v1` held exactly one asset:

| Item | State before this slice |
|---|---|
| `inputs\FIX-CUSTOMER-DESIGN-001.jpeg` | The only input. Category `COMPLETE_CUSTOMER_DESIGN`. |
| `expected\FIX-CUSTOMER-DESIGN-001_HD.png` | Accepted Meitu AI-sharpen reference output. |
| `expected\FIX-CUSTOMER-DESIGN-001_CUTOUT.png` | Accepted Meitu smart-cutout reference output. |
| `manifests\FIX-CUSTOMER-DESIGN-001.json` | Schema 1. `"finalTiff": "PENDING"`. |

Machine-confirmed by `Set-PrintFlowProductionRevalidation.ps1`, which reported six missing
categories, and by the live workstation verification, which reported `ProductionRevalidation` as
the **only** failing check out of eleven.

### Pre-change matrix

| AC clause | Evidence before | Satisfied? | What was missing | How this slice addresses it |
|---|---|---|---|---|
| normal JPG portrait | — | No | asset + manifest | Rendered subject; real Meitu enhancement path recorded |
| complex background, fine hair | — | No | asset + manifest | Rendered subject with ~7,200 sub-pixel strands over a textured background |
| transparent PNG | — | No | asset + manifest | Byte-identical copy of the preset's accepted cutout reference |
| complete customer design | present, `finalTiff: PENDING` | Partial | an actual outcome for `finalTiff` | Replaced with a stated expectation; source bytes untouched |
| PSD with composite preview | — | No | a real `.psd` on disk | Written by Photoshop with Maximize Compatibility; resource 1057 proved |
| single-page PDF | unit fixture `single.pdf` (144×72 pt red rectangle) | No | a regression asset | Deterministic one-page PDF, verified through `Windows.Data.Pdf` |
| reference production TIFF | existed in the baseline, not in the set | No | copy + provenance in the set | Byte-identical copy, reference-only, refusal asserted |
| expected processing path per file | 1 partial manifest | No | 7 manifests | Schema 2 |
| relevant expected properties per file | 1 partial manifest | No | 7 manifests | Schema 2 |
| fixed / drift-proof | no hashes checked | No | recomputation | Every SHA-256 recomputed from the bytes at preflight |
| repeatable execution procedure | **undefined** — SCRUM-11123's runbook says so explicitly | No | a runner | `tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1` |
| proven on the fixed workstation | none | No | a controlled run | Attempted; see §11 |

---

## 3. The set

**Identity:** `setId: printflow-regression-v1`, `schemaVersion: 2`, set version `v1`, rooted at
`D:\PrintFlowStudio\TestData\v1`, indexed by `set.json`.

Schema 1 was the single hand-written `FIX-CUSTOMER-DESIGN-001` manifest. Schema 2 carries
everything it carried, under the same names, and adds `setId`, `provenance`,
`expectedProcessingPath`, `expectedExternalApplications`, `expectedProperties`,
`comparisonPolicy` and `manualChecks`.

### 3.1 Fixed inputs and their hashes

Once accepted, these bytes are regression authority. Changing what a category contains creates a
new set version or is an explicit, recorded rebaseline; it is never a silent edit of v1.

| Asset | Category | Format | Size | Bytes | SHA-256 |
|---|---|---|---|---|---|
| `FIX-PORTRAIT-001.jpg` | `NORMAL_JPG_PORTRAIT` | JPEG | 1200×1600 @300dpi | 130,111 | `F4CAD2A1EC7994E42E2A77CC6F30E29DE91D344821E4EE7AC01C7E712D9A4634` |
| `FIX-FINE-HAIR-001.jpg` | `COMPLEX_BACKGROUND_FINE_HAIR` | JPEG | 1200×1600 @300dpi | 312,309 | `5A705FE390AF87D1D48A0554D4908C425D4703A8807CA78EC73AC0E55E3C8D8E` |
| `FIX-TRANSPARENT-001.png` | `TRANSPARENT_PNG` | PNG, 32bpp ARGB | 3412×5120 | 3,895,181 | `A20A722DB394B8CBBAE7975CC930DD456971E913E5844C21F34B27B9C4D377E2` |
| `FIX-CUSTOMER-DESIGN-001.jpeg` | `COMPLETE_CUSTOMER_DESIGN` | JPEG | 1024×1536 | 216,162 | `8A7063D8F81FB72A1DB7F5633980660905E2A6C3D3971E4F94A138B5C8394879` |
| `FIX-PSD-001.psd` | `PSD_WITH_COMPOSITE_PREVIEW` | PSD v1, RGB, 8-bit, 3 layers | 1200×1600 @300dpi | 2,757,782 | `68476CBB1EFF66D01882A6AE6C95F225F68AAE76C8E96E84DE17F7DD23BA64E8` |
| `FIX-PDF-001.pdf` | `SINGLE_PAGE_PDF` | PDF 1.4, one page, 480×640 DIP | 1500×2000 px at 300 PPI | 130,790 | `375D0463BE1B6499494CD14201E9E8C36C1EE68475AE3F3D0A95CBD0BCFC82A2` |
| `reference\FIX-REFERENCE-TIFF-001.tif` | `REFERENCE_PRODUCTION_TIFF` | TIFF, separated CMYK + W1 | 3307×4474 @300dpi | 116,992,344 | `D1E69C4108D4C1D6119DB11DE036F56555CDE4A064F23AF541E24E1DAC5EA412` |

The `FIX-CUSTOMER-DESIGN-001.jpeg` hash is unchanged from its August manifest: its source bytes
were not touched by this slice.

### 3.2 Provenance

No customer order, memorial artwork, portrait, PSD or PDF was copied into the set by this slice.
Nothing was downloaded and nothing left the workstation.

| Asset | Classification | Source and why it may live here permanently |
|---|---|---|
| `FIX-PORTRAIT-001` | **Synthetic** | Rendered by `tools\regression\New-PrintFlowRegressionAssets.ps1` from a fixed-seed generator written out in that file. No project-owned photographic portrait exists on this workstation, and the only customer-like photograph present already occupies `COMPLETE_CUSTOMER_DESIGN`. |
| `FIX-FINE-HAIR-001` | **Synthetic** | Same generator and the same subject, so the two differ only in the property under test. |
| `FIX-TRANSPARENT-001` | **ExistingValidatedReference** | Byte-identical copy of `MEITU_SMART_CUTOUT_REFERENCE`, which preset 1.16.0 lists under `acceptedReferenceOutputs` with this exact SHA-256. Its alpha is a real Meitu cutout's alpha, soft hair edges included. |
| `FIX-CUSTOMER-DESIGN-001` | **ProjectOwnedTestAsset** | Already `APPROVED_LOCAL_ONLY` since 17 August 2026 for permanent local regression use. Untouched. |
| `FIX-PSD-001` | **DerivedFromProjectOwnedTestAsset** | Written by Adobe Photoshop CC 2019 20.0.10 itself, from a scratch copy of `FIX-PORTRAIT-001`, driven by `New-PrintFlowRegressionPsd.ps1`. |
| `FIX-PDF-001` | **DerivedFromProjectOwnedTestAsset** | Assembled byte by byte around `FIX-PORTRAIT-001` as a single `DCTDecode` image XObject. |
| `FIX-REFERENCE-TIFF-001` | **ExistingValidatedReference** | Byte-identical copy of the artefact preset 1.16.0 names in `tiffContract.referenceArtifact`, with the same SHA-256. |

**Honesty about the two rendered assets.** They are illustrated, not photographed. What
`FIX-FINE-HAIR-001` genuinely contains is around 7,200 individually drawn strands of 0.45–1.4 px
leaving the scalp and crossing a background built from 130 overlapping soft foliage shapes and
5,200 short strokes at the same spatial frequency as the hair itself — that is narrow,
high-frequency boundary detail against a non-uniform background, which is the property the
category exists to exercise. It is not evidence about how Meitu handles photographed hair, and
the manifests say so under `provenance.honestLimits`.

`FIX-PSD-001` is **not byte-reproducible**: Photoshop stamps XMP metadata with the moment of
writing. Its accepted bytes are fixed by the hash above, and regenerating it is an explicit
rebaseline of the set.

### 3.3 Privacy

`FIX-CUSTOMER-DESIGN-001`, `FIX-TRANSPARENT-001` and `FIX-REFERENCE-TIFF-001` derive from
material containing an identifiable person and a memorial design. All three carry
`localOnly: true`, `gitAllowed: false`, `uploadAllowed: false`. The set is **not committed** and
was **not uploaded**. `tools\regression` is what makes it reproducible and its expectations
reviewable.

---

## 4. Expected processing path per asset

Taken from `WorkflowCatalog`, `SessionService` routing, `SupportedInputFormats`,
`ProductionPhotoshopPsdPreparation`/`PsdCompositeProbe`, `WindowsPdfPreparationProcessor` and the
accepted preset. No route below is invented; each names steps that exist in the three fixed
workflows.

| Category | Workflow | Path | External applications |
|---|---|---|---|
| `NORMAL_JPG_PORTRAIT` | `PrepareAsset` | Import → OriginalConfirmation → **Enhancement (Meitu)** → BackgroundRemoval *skipped* → Trim: `KeepOriginalExtent` → ApprovedPngExport | Meitu 7.8.7.5 |
| `COMPLEX_BACKGROUND_FINE_HAIR` | `PrepareAsset` | Import → OriginalConfirmation → Enhancement *skipped* → **BackgroundRemoval (Meitu)** → Trim (alpha bounds) → ApprovedPngExport | Meitu 7.8.7.5 |
| `TRANSPARENT_PNG` | `PrepareAsset` | Import → OriginalConfirmation → both Meitu steps *skipped* → **Trim (internal, deterministic alpha bounds)** → ApprovedPngExport | none |
| `COMPLETE_CUSTOMER_DESIGN` | `PrepareCustomerDesign` | Import → OriginalConfirmation → both Meitu steps *skipped* → Trim: `KeepOriginalExtent` → PrintDimensions → `W1_1px` → **PhotoshopOutput** | Photoshop CC 2019 |
| `PSD_WITH_COMPOSITE_PREVIEW` | `GeneratePrintTiff` (`preparePsd`) | Import → OriginalConfirmation rewritten to **`PreparePsd` (Photoshop)** → PrintDimensions → `W1_2px` → **PhotoshopOutput** | Photoshop CC 2019 |
| `SINGLE_PAGE_PDF` | `GeneratePrintTiff` (`preparePdf`) | Import → OriginalConfirmation rewritten to **`PreparePdf`** (Windows.Data.Pdf, 300 PPI) → PrintDimensions → `W1_2px` → **PhotoshopOutput** | Photoshop CC 2019 |
| `REFERENCE_PRODUCTION_TIFF` | **none** | Not imported. TIFF is deliberately absent from `SupportedInputFormats`. The run asserts the **refusal** (`SourceFormatUnsupported`, no session created) and reads the structure from the TIFF IFD. | none |

The two Meitu steps are skipped for the customer design because `WorkflowCatalog` marks
`BackgroundRemoval` skippable precisely so a finished design can keep its background, and because
the accepted enhanced and cutout references for that exact file already exist. Its distinctive
contribution to the set is the complete-design → production-TIFF path.

`Trim` records `KeepOriginalExtent` wherever the upstream artwork is opaque: automatic alpha
trimming has nothing to measure, and `KeepOriginalExtent` is the Product's own first-class command
for that case rather than a workaround.

---

## 5. Expected properties and comparison policy

Each manifest states the smallest truthful comparison mode.

| Category | Mode | Why that mode |
|---|---|---|
| `NORMAL_JPG_PORTRAIT` | `Structural` | Meitu AI enhancement is not a deterministic function of its input; the accepted preset records the feature, not a byte contract. What an upgrade can break is structural: a PNG revision exists, is larger than the source, the source is untouched, the lock is released. |
| `COMPLEX_BACKGROUND_FINE_HAIR` | `Structural` | Transparency is real and the trim found bounds inside the canvas — both deterministic. Hair-edge quality is a manual check and is deliberately not reduced to a threshold. |
| `TRANSPARENT_PNG` | `ReferenceProperties` | Fixed bytes through a deterministic internal trim. The alpha bounding box `x=229, y=1210, w=2724, h=3685` — recorded against these exact bytes in August — is an exact expectation and is stated as one. |
| `COMPLETE_CUSTOMER_DESIGN` | `Structural` | The TIFF's structure is a hard contract; its bytes are not, because the Action runs against a Photoshop document whose metadata carries the moment it was written. |
| `PSD_WITH_COMPOSITE_PREVIEW` | `Structural` | The managed raster inherits Photoshop metadata. The one exact expectation is on the **source**: its SHA-256 must be unchanged after preparation. |
| `SINGLE_PAGE_PDF` | `ReferenceProperties` | Rasterisation is deterministic for fixed bytes at a fixed DPI, so 1500×2000 is derived from the page geometry rather than copied. |
| `REFERENCE_PRODUCTION_TIFF` | `ExactHash` | A fixed archived artefact the product does not regenerate. Any change means the reference was replaced. |

**No invented thresholds.** SCRUM-11095 already records why this project refuses numbers nobody
derived from a real print test; the same rule is applied here to hair-edge quality.

### Manual review policy

Two manifests record a qualitative check, and the machinery makes them impossible to answer by
accident:

| Check | Question |
|---|---|
| `PORTRAIT-VISUAL-001` | Does the enhanced export still look like a correctly enhanced portrait — subject sharp, skin tone unshifted, no artefact introduced along the hair or shoulder edges? |
| `FINE-HAIR-VISUAL-001` | Are individual hair strands still retained at the boundary, without a hard halo, and is the foliage background fully removed rather than partly retained as coloured fringing? |
| `CUSTOMER-DESIGN-VISUAL-001` | Does the produced TIFF show the complete design at the requested size, with the W1 channel covering the intended ink region? |

A decision of `Pending` is the absence of a decision. `RegressionCaseResult.Conclude` leaves any
case with an undecided check `Pending`, and `Pending` never aggregates to `Passed` —
`An_undecided_manual_check_cannot_be_marked_passed_automatically` asserts it. A recorded decision
must name **who** decided; a decision with a blank decider is refused.

Decisions are recorded **after** the artefacts exist, through
`-RecordVisualReview <decisions.json>`, which re-derives the verdict from evidence already on disk
without re-running a single case. Re-running to carry a decision in would mean the reviewed
pictures were not the ones the verdict describes.

---

## 6. Manifest schema (v2)

```
schemaVersion, setId, fixtureSetVersion, fixtureId, category, status, approval,
privacy { containsIdentifiablePerson, containsMemorialDesign, localOnly, gitAllowed, uploadAllowed },
provenance { classification, producedBy, derivedFrom, rationale, honestLimits },
file { path, extension, length, sha256, format,
       + widthPixels/heightPixels/dpiX/dpiY/pixelFormat/hasAlphaChannel   (raster)
       + psd { widthPixels, heightPixels, channelCount, bitsPerChannel, colourMode, hasRealMergedData, horizontalDpi }
       + pdf { pageCount, isPasswordProtected, pageWidthDip, pageHeightDip, rotation, productionRasterWidth/Height, authority }
       + tiff { byteOrder, widthPixels, heightPixels, bitsPerSample, compression,
                photometricInterpretation, samplesPerPixel, planarConfiguration,
                xResolution, yResolution, resolutionUnit, extraSamples,
                hasPhotoshopResources, hasInkNames } },
expectedWorkflow, expectedProcessingPath[], expectedExternalApplications[],
expectedProperties {…}, comparisonPolicy { mode, reason }, manualChecks[], notes
```

Everything under `file` is **measured**, never typed in: hashes and lengths from the bytes, raster
facts from WIC, PSD facts from the envelope walk, PDF facts from `Windows.Data.Pdf` itself, TIFF
facts from the IFD. Everything outside `file` is **authored** and reviewed in Git as part of
`New-PrintFlowRegressionManifests.ps1`.

### `finalTiff: PENDING` resolved

The one inherited `PENDING` is gone. `FIX-CUSTOMER-DESIGN-001`'s `expectedResults.finalTiff` now
reads:

```json
{
  "status": "EXPECTED_FROM_REGRESSION_RUN",
  "resolves": "The \"PENDING\" this field held from 2026-08-17 until SCRUM-11065.",
  "expectation": "The PhotoshopOutput step produces a validated production TIFF: separated CMYK plus a named W1 spot channel, 5 samples per pixel, 300 dpi, no image compression. The produced file is recorded per run under runs\\<run-id>\\, not fixed here, because its bytes carry the moment Photoshop wrote them."
}
```

Its accepted Meitu reference outputs, their hashes and the two operator visual acceptances from
18 August are carried forward unchanged — that is real history and this slice had no business
dropping it. A manifest still containing `"PENDING"` is now a **preflight failure** in both
layers.

---

## 7. The execution procedure

SCRUM-11123's runbook left this undefined because there was no set. There is now exactly one
runner and deliberately no second one:

```
tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1
```

### Layer 1 — preflight (static, offline, opens nothing)

Seven categories present and each claimed by exactly one asset; every manifest readable, schema 2,
one consistent `setId`, and free of `PENDING`; every referenced file present at its recorded
length; **every SHA-256 recomputed from the bytes**; the comparison mode known; an expected
processing path recorded; and the per-format preconditions — PSD `hasRealMergedData` and RGB,
PDF single-page and unencrypted, TIFF 5 samples / photometric 5 / uncompressed.

Verified negatively as well as positively: against a copy of the set with one manifest removed and
one hash altered, preflight reported exactly *"Required category PSD_WITH_COMPOSITE_PREVIEW is
absent"* and *"FIX-PDF-001: SHA-256 drift"* and exited 2.

`-PreflightOnly` writes **no** `result.json`. A set that exists is not a set that passed, and the
revalidation tool reads `result.json`.

### Layer 2 — the fixed-workstation run

`StandardRegressionSetWorkstationSmoke` re-asserts Layer 1 in the process that is about to drive
Photoshop, refuses to start unless `Adapters:Mode` is `Production`, runs the live environment
checks and **requires every blocking one to pass**, then drives the real `ISessionService` against
the real Production adapters. No adapter is substituted anywhere in the file; a Fake-configured
installation reports `Blocked`.

### Result format

`<SetRoot>\runs\<run-id>\result.json` keeps the four fields
`Set-PrintFlowProductionRevalidation.ps1` already reads — `setId`, `status`, `completedAtLocal`,
`evidencePath` — and adds per-case evidence beside them: which asset ran, which workflow and
steps, which external applications and adapter ids, what was produced with hashes, every
structural assertion and its outcome, every manual check and its decision, and the evidence path.
Statuses are `Passed`, `Failed`, `Blocked`, `Cancelled`, plus `Pending` for a case whose
qualitative check nobody has decided.

**The top-level status is derived, never asserted.** `StandardRegressionSetRunResult.From`
computes it from the *required* category list rather than from the cases that happen to be
present, so a run covering six of seven can never report a pass —
`A_skipped_category_cannot_produce_a_passing_run` asserts exactly that.

---

## 8. The revalidation bootstrap

### The circle

SCRUM-11123 closes Production until a revalidation record says the standard regression set passed.
Recording that requires the set to have been run. Running it drives the real Production adapters.
Production adapters are closed.

### What was refused

- **Writing a temporary passing record.** That forges the evidence the run exists to produce.
- **Removing `ProductionRevalidation` from `VerifiedEnvironmentGate`.** That reopens Production
  for the application permanently, to solve a bootstrap that happens once per upgrade.

### What was done

`ProductionWorkstationVerifier.ForStandardRegressionRun` composes the verifier with the
production-revalidation check **omitted** — not answered. Nothing supplies a revalidation record,
and there is no value that class accepts as a passing one it did not read from the workspace
itself, so this cannot forge an approval; it can only decline to ask the one question whose answer
depends on the run being composed.

Everything else is evaluated exactly as the application evaluates it: preset integrity and the
28-entry evidence chain it vouches for, the OS build, both accepted binaries, the Action artefact,
the workspace root, the local-console session, the display topology, the UI culture — **and the
entire live phase**: Meitu and Photoshop launchability, their recognised safe starting states,
Photoshop's colour settings, the test-image round trip and the global automation lock.

**Why inside the verifier rather than around it.** The first implementation decorated the
verifier's *result*. The gate then opened — and every live check came back `Blocked`, because the
real composition refuses to launch applications while its automatic half is failing. That
arrangement would have driven Meitu and Photoshop with their launchability, safe states, colour
settings and automation lock never verified, which is precisely what the task's §16 forbids.
Omitting the check inside the composition is what lets the live phase actually run.

### Why the application cannot use it

`ForStandardRegressionRun` is `internal`, and `PrintFlow.Infrastructure` grants its internals to
`PrintFlow.Tests` **and nothing else** — asserted by
`The_omission_factory_is_not_reachable_from_the_application`, which also asserts that no public
factory takes a `bool` or an `IProductionRevalidationReader` that could reach the same effect.
PrintFlow.App has no such grant, so no composition the shipped product performs can name it.

`RegressionBootstrapWorkstationVerifier` (test assembly) consults the omitting composition **only
when `ProductionRevalidation` is the single blocking check**. A workstation with any second
problem gets the real verifier's own answer back, unaltered, and the run does not start. A
workstation that already has a valid record is not touched at all. Whether the bootstrap was used
is recorded in the run evidence — a bootstrap invisible in the evidence is a bypass.

PrintFlow still cannot write a revalidation record: there is no writer interface anywhere in the
solution, re-asserted by `Nothing_in_the_solution_writes_a_production_revalidation_record`.

---

## 9. Testing

### Targeted coverage (32 tests, `StandardRegressionSetTests`)

| Concern | Test |
|---|---|
| category vocabulary matches the revalidation script | `The_required_categories_match_the_revalidation_script_exactly` |
| manifest parsing | `A_complete_set_validates`, `Every_manifest_records_an_expected_processing_path_and_a_comparison_mode` |
| seven-category completeness | `A_missing_category_invalidates_the_set` (×7) |
| one dedicated asset per category | `Two_assets_cannot_claim_the_same_category` |
| hash-drift refusal | `A_replaced_input_is_refused_even_though_its_name_is_unchanged` |
| missing file, unknown mode, residual PENDING | `A_missing_input_file_is_refused`, `An_unknown_comparison_mode_is_refused`, `A_manifest_still_recording_PENDING_is_refused` |
| run-result aggregation | `A_run_in_which_every_case_passed_is_Passed`, `A_failed_structural_assertion_fails_its_case_and_the_run` |
| **no skipped category can yield Passed** | `A_skipped_category_cannot_produce_a_passing_run` |
| blocked ≠ passed | `A_blocked_environment_is_not_a_pass` |
| manual decisions | `An_undecided_manual_check_cannot_be_marked_passed_automatically`, `A_decided_manual_check_records_who_decided_it` |
| revalidation-tool integration | `The_run_result_keeps_the_contract_the_revalidation_script_reads` |
| bootstrap safety | `The_bootstrap_verifies_a_workstation_whose_only_failure_is_the_missing_revalidation`, `The_bootstrap_refuses_to_help_when_anything_else_is_wrong` (×4), `The_bootstrap_stands_aside_when_a_valid_revalidation_record_exists`, `Omitting_the_revalidation_check_cannot_verify_a_workstation_without_preset_integrity` |
| bootstrap unreachable from App | `The_omission_factory_is_not_reachable_from_the_application`, `No_product_assembly_can_reach_the_revalidation_bootstrap` |
| no self-approval | `Nothing_in_the_solution_writes_a_production_revalidation_record` |
| reference TIFF is not an input | `The_reference_production_TIFF_category_is_not_a_supported_input` |
| independent TIFF reader | `The_independent_tiff_reader_reads_a_production_tiff` |

### Full-suite decision

This slice is **not** confined to TestData, tooling and documentation: it changed
`ProductionWorkstationVerifier`, which is production composition. Under the task's §35 that
requires a full Product suite, so one was run rather than argued around. Result in §12.

The change itself is additive — the public `ForWorkstation` overload keeps its exact signature and
now delegates to a shared `Compose`, and `_omitProductionRevalidation` is `false` for every
verifier the application builds.

---

## 10. Evidence

All run evidence is local, under `D:\PrintFlowStudio\TestData\v1\runs\<run-id>\`:
`result.json`, `readiness.json`, `case-<asset-id>.json`, the run's own SQLite database, and the
produced PNG artefacts. Production TIFFs are recorded by workspace path and hash rather than
copied — they run to tens of megabytes each and the evidence needs to be reproducible, not
duplicated. No customer-like binary or screenshot is committed to Git.

---

## 11. The controlled fixed-workstation run

### 11.1 What ran, and what it proved

Five runs were made on 9 September 2026, all recorded under
`D:\PrintFlowStudio\TestData\v1\runs\`.

| Run | Scope | Outcome |
|---|---|---|
| `staging-internal` | `TRANSPARENT_PNG`, `REFERENCE_PRODUCTION_TIFF` | **Both Passed.** Gate ALLOWED with the bootstrap in use. |
| `staging-live-checks` | `TRANSPARENT_PNG`, with the live phase now required | Blocked — `PhotoshopTestImageRoundTrip` failed |
| `staging-live-checks-2` | same | Blocked — identical failure |
| `staging-live-checks-3` | same, Photoshop launched fresh by the check | Blocked — `PhotoshopSafeStartingState` failed on a ROT race at startup |
| `staging-live-checks-4` | same | Blocked — `MeituLaunchability` failed: Meitu was not on a recognised screen |

Two of the seven categories are **proven on the fixed workstation**, and they are the two whose
recorded path needs no external application:

- **`TRANSPARENT_PNG` — Passed.** The deterministic alpha trim produced exactly the
  **2724x3685** the manifest predicts from the alpha bounds recorded against these exact bytes in
  August; transparency survived; the source was byte-identical afterwards; the automation lock was
  free.
- **`REFERENCE_PRODUCTION_TIFF` — Passed.** The archived reference is structurally intact —
  3307x4474, 5 samples per pixel, photometric 5, uncompressed, 300x300 dpi, read from the IFD by a
  reader independent of the Product's own — **and PrintFlow still refuses it as a Home input**
  with `SourceFormatUnsupported`, creating no session.

The bootstrap behaved exactly as designed. In `staging-internal` the gate reported
`Production gate ALLOWED (revalidation bootstrap in use: every other check passed)`, and the
automatic half showed 10 of 11 checks passing with `ProductionRevalidation` as the sole failure —
precisely the condition the bootstrap is permitted to act on, and the reason it is safe.

### 11.2 Why the other five did not run

**The fixed workstation was in active use by another person throughout this task.** Photoshop held
their documents continuously — `color_calibration_002.pdf`, then `LIA Flyer.png`, then
`Milika Tuionetoa Lauti.png` and `Milika.png`, at one point unsaved and being edited — and the
desktop was in interactive use. All five remaining categories drive Meitu or Photoshop in the
foreground.

Nothing of theirs was taken. One document, `LIA Flyer.png`, was closed: it was already saved, it
was identified by exact absolute path before the command was issued, and the command refuses any
document that is unsaved or whose path cannot be read. Its path —
`C:\Users\admin\Downloads\LIA Flyer.png` — is recorded here so it can be reopened. No other
document was touched, no unsaved work was discarded, and no dialog was answered on anyone's
behalf.

This is the outcome the design intends rather than a hole in it. Task §16 says a blocking
environment failure means the run does not start, and the runner enforces exactly that: it
requires every blocking live check to pass before driving a single external application, and it
reports `Blocked` — not `Failed`, and certainly not `Passed` — when they do not.

### 11.3 An environmental finding, recorded rather than worked around

`PhotoshopTestImageRoundTrip` failed reproducibly (`staging-live-checks`,
`staging-live-checks-2`) with:

> The confirmed-closed synthetic probe could not be cleaned up safely: The process cannot access
> the file `...\EnvironmentVerification\<token>\Working` because it is being used by another
> process.

Diagnosed rather than guessed:

- The probe **file** was deleted successfully, and hash-verified before deletion. Only the removal
  of the now-empty `Working` **directory** failed.
- The directory stayed locked for at least 75 seconds, so it is not a race.
- It was released the instant Photoshop exited. **Photoshop holds the handle for its process
  lifetime.**
- An earlier run's folder was released only when the next run created a new one.

On **8 September 2026** this same check passed on this same workstation with both applications
deliberately left running — recorded in the SCRUM-11110 completion report as *"Passed; exact
synthetic probe cleaned; Photoshop returned to prior state ... No probe files remained."* The
material difference is that on 8 September Photoshop's safe state was `KnownStartScreen` with
**no document open**, whereas every failing run today had another person's document open.

The likeliest reading is that Photoshop retains the probe folder while another document keeps it
busy, and that a clean, document-free Photoshop — the state §7.3 of the runbook now tells the
operator to establish, and the state that existed on 8 September — releases it. **This is not
confirmed.** Confirming it needs a Photoshop with no documents open, and one was never available.
It is recorded here as an open question with its evidence, and **no Product code was changed on
the strength of it**: changing a readiness check to make a run go green, on an unconfirmed
diagnosis, is exactly the move this project refuses.

### 11.4 Resume path

Nothing about the set needs to change. When the workstation is free, with Meitu on its clean start
page and Photoshop settled with no document open:

```
tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1
```

The two proven categories re-run with the rest. The run result is derived from all seven, so
nothing carries over from the partial runs above and no partial credit is possible.

---

## 12. Production revalidation

**Not recorded.** `Set-PrintFlowProductionRevalidation.ps1` was **not** invoked, because task §31
permits it only after all seven cases pass, and two did.

What is established, and can be re-checked independently:

- **The tool's own set-completeness gate is now satisfied.** Its category scan, replayed against
  `D:\PrintFlowStudio\TestData\v1\manifests` without writing anything, finds all seven required
  categories and reports none missing. Before this slice it reported six missing.
- **What it would still refuse is the run.** Without a `result.json` reading `"status": "Passed"`
  it records `standardRegressionSet.status = NotAvailable`, which PrintFlow reads as blocking.
  That is correct, and it was not worked around.

`Revalidation\production-revalidation.json` does not exist and was not hand-edited. PrintFlow's
own verification therefore still reports `ProductionRevalidation` as failing and **Production
remains closed** — on the same evidence as before this slice, for one fewer reason than before.

`Adapters:Mode` was left at `Production` throughout and was not changed in either direction. The
claim this slice makes is *not* "Production is open"; it is that the gate now has six of the seven
things it was waiting for.

---

## 13. Build and tests

- **Clean Release build:** 0 warnings, 0 errors.
- **Targeted regression-set coverage:** 32 passed, 0 failed.
- **Verification-adjacent filter** (regression set, revalidation, gate, workstation verifier,
  verification boundary): **165 passed, 0 failed, 0 skipped.**
- **Full Product suite:** **11,712 passed, 0 failed, 0 skipped** in 5m45s — the accepted 11,676
  baseline plus 36 new tests, with no pre-existing test changed or weakened.

The full suite was run rather than argued around because this slice changed
`ProductionWorkstationVerifier`, which is production composition, and task §35 requires it in that
case. The change is additive: the public `ForWorkstation` overload keeps its exact signature and
delegates to a shared `Compose`, and `_omitProductionRevalidation` is `false` for every verifier
the application builds.

---

## 14. Jira reassessment

### SCRUM-11065 — **PARTIAL** (was NOT_IMPLEMENTED)

Against the eight conditions task §41 sets for FULL:

| Condition | Met? |
|---|---|
| all seven required categories actually exist | **Yes** |
| files are local and authorised / customer-like | **Yes** |
| each has a manifest | **Yes** |
| every manifest records the expected processing path | **Yes** |
| every manifest records relevant expected properties | **Yes** |
| hashes are fixed and drift is refused | **Yes** |
| one repeatable execution procedure exists | **Yes** |
| **the set has been proven suitable on the fixed workstation** | **No — 2 of 7 categories** |

Seven of eight hold. The eighth is why this is not FULL: a set proven on two categories is not a
set proven on the fixed workstation, and marking it FULL would claim evidence that does not exist.
The gap is workstation availability, not the set.

### SCRUM-11123 — **PARTIAL** (unchanged)

Reassessed independently, not inferred from SCRUM-11065's label. Its AC requires that any
PrintFlow, Windows, Meitu or Photoshop upgrade *"must require rerunning the standard test set
before production use"*. That is enforced and, for the first time, **satisfiable**: the set exists,
has one defined procedure, and the revalidation tool accepts its shape and finds all seven
categories. But no run has passed, so no record exists, so the clause is still not *satisfied* on
this workstation. The blocker has narrowed from "the set does not exist" to "the set has not
completed a run"; it has not been cleared.

### SCRUM-11136 — **PARTIAL** (unchanged); prerequisite asset now exists

Its hard prerequisite was the existence of the set, and the set now exists, so the standing
reason — "the 90 % figure cannot be claimed against a test set that was never built" — no longer
applies. This slice did **not** implement its repeated success-rate measurement and makes no claim
about automation success rate. Record only: **prerequisite asset built; still blocked on a
completed run.**

### SCRUM-11067 / SCRUM-11134 / SCRUM-11135 — unchanged

Copying the Maintop-proven reference TIFF into the regression set adds **no** new Maintop
evidence, **no** new physical-print evidence and **no** new comparison acceptance. No Maintop
import was performed in this task and no DTF print was produced. All three keep their existing
status and their existing evidence boundaries, including Epic 11000's explicit statement that no
separate physical print was produced or photographed.

### SCRUM-11137 — unchanged

Still blocked by the missing historical pre-MVP benchmark from SCRUM-11066. Nothing here
fabricates it.

### SCRUM-11115 — not reopened

Accepted FULL against its own parent wording, and nothing here changes that. The only factual
note worth adding: the residual it pointed at, SCRUM-11065, is now built but not yet proven.

### SCRUM-11060 (parent Epic) — unchanged

Not closed by this slice. SCRUM-11066's twenty-case manual benchmark remains absent and
SCRUM-11065 is PARTIAL rather than FULL.

---

## 15. Git state

Local commits on `master` only. No branch, no worktree, no alternate clone or checkout, no amend,
no rebase, **no push**, no deploy, no AI attribution trailer. The validated preset was not
modified. `ProductionRevalidation` was not disabled, bypassed, faked or weakened. Working tree
clean at the end.

```
src/PrintFlow.Infrastructure/Verification/ProductionWorkstationVerifier.cs   ForStandardRegressionRun + shared Compose
tests/PrintFlow.Tests/Regression/StandardRegressionSet.cs                    set model, loading, Layer 1 validation
tests/PrintFlow.Tests/Regression/StandardRegressionSetRun.cs                 run result and derived aggregation
tests/PrintFlow.Tests/Regression/RegressionRunAuthority.cs                   the revalidation bootstrap
tests/PrintFlow.Tests/Regression/TiffStructure.cs                            independent TIFF IFD reader
tests/PrintFlow.Tests/Smoke/StandardRegressionSetWorkstationSmoke.cs         Layer 2 and the visual re-derivation
tests/PrintFlow.Tests/Unit/Regression/StandardRegressionSetTests.cs          32 targeted tests
tools/regression/Invoke-PrintFlowStandardRegressionSet.ps1                   the one entry point
tools/regression/New-PrintFlowRegressionAssets.ps1                           deterministic asset generation
tools/regression/New-PrintFlowRegressionPsd.ps1                              Photoshop-authored PSD
tools/regression/New-PrintFlowRegressionManifests.ps1                        measured manifests
docs/printflow/scrum-11065-standard-local-regression-set-completion.md       this report
docs/printflow/installer-upgrade-rollback-runbook.md                         live sections 7.1-7.5 rewritten
docs/printflow/scrum-11123-versioned-offline-installer-upgrade-completion.md dated addendum appended
docs/printflow/original-jira-functional-coverage-reaudit.md                  dated delta appended
```

The regression set itself — `D:\PrintFlowStudio\TestData\v1` — is **not** in Git and was not
moved into the installer payload. It stays on the fixed workstation, which is what the workstation
contract requires and what the privacy posture demands.

---

## 16. Verdict

**BLOCKED — SCRUM-11065 STANDARD LOCAL REGRESSION SET NOT FULLY VERIFIED**

The set is built, complete, fixed, documented and runnable. Its execution procedure exists and
both layers work. The self-referential revalidation bootstrap is solved without weakening the
normal Product gate, and the full suite confirms nothing else moved. What is missing is the one
thing that cannot be manufactured: a controlled run of all seven cases on a fixed workstation that
was, for the duration of this task, in continuous use by somebody else.
