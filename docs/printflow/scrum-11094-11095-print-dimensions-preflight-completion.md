# SCRUM-11094/11095 — Print Dimensions preflight

Date: 10 September 2026. Implementation, independent review and final validation complete.

## Original requirement authority

Exact CSV descriptions read before Product edits from
`C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv`.

**SCRUM-11094 / Work Item 11401 — Implement Locked-Aspect-Ratio Print Dimension Calculation**

> Implement width and height entry in millimetres with aspect ratio locked by default, automatic calculation of the paired dimension, pixel-dimension calculation at fixed 300 DPI and display of detected graphic bounds. Non-proportional stretching is not permitted during TIFF generation, and target dimensions describe the final transparent canvas including the approved trim safety margin.

**SCRUM-11095 / Work Item 11402 — Implement Effective-DPI Resolution Risk Rules**

> Implement sufficient, warning and blocking resolution states based on effective DPI and thresholds established through real print tests rather than invented values. Display millimetres, pixel dimensions, effective DPI and graphic bounds. A minor shortfall may warn, while a significant shortfall blocks output until the operator resolves the source or dimensions.

**Parent SCRUM-11093 / Work Item 11400 — Generate and Validate Production TIFF Outputs with Photoshop**

> Implement physical print sizing and the replaceable Photoshop Output Adapter used by the two production workflows. PrintFlow calculates locked-aspect-ratio dimensions at fixed 300 DPI, handles supported PNG/JPEG/PSD/single-page-PDF inputs, detects existing white-ink channels where relevant, invokes the validated Photoshop production Action and current confirmed colour settings, and validates the generated TIFF before final review. One approved design may generate multiple independently reviewed sizes; PrintFlow must never silently stretch artwork, choose a page from a multi-page PDF, overwrite outputs or treat an invalid TIFF as production-ready.

## Pre-change clause matrix

| Original AC clause | Current authority | Already met? | Superseded? | Actual remaining Product gap | Planned proof |
|---|---|---|---|---|---|
| 11094 mm entry; locked ratio; paired dimension | `PrintPreparationPlan.For` / `FitWithinBounds`; `TargetEdgePrintPreparationPlan.For` / `ScaleToTargetEdge` | Business outcome yes | Literal lock-toggle/pair entry replaced with fit bounds or one target edge | None in sizing engine | Existing sizing tests; draft projection uses same factories |
| 11094 fixed 300 DPI pixels; no stretch | Bound `PhotoshopPreparation` and projected output pixels | Yes | No | None | Same preparation in query and production; affected sizing tests |
| 11094 detected graphic bounds | Exact producing attempt `TrimGeometry`, `ManualCropGeometry` | Persisted, visible at Trim review only | No | Print Dimensions needs content/selection and final canvas | Automatic/manual/keep persisted geometry and rendered UI |
| 11094 final canvas includes approved margin | Approved upstream Revision and applied crop geometry | Yes | No | Present distinction usefully | Separate artwork content/selected artwork and final canvas |
| 11095 millimetres and pixels | Existing size/plan UI | Yes | No | Coherent compact proposal display | Plan-derived physical and pixel values |
| 11095 effective DPI | `TiffEffectiveResolution` / TIFF final review | Final Review only | No | Pre-Photoshop source resolution | Shared calculation; preflight/review agreement |
| 11095 graphic bounds | Same persisted geometry as above | Not at dimensions | No | Same display gap | Same geometry proof |
| 11095 empirical sufficient/warning/block bands; shortfall gate | Exact-offer `EnlargementAuthority` | Explicit enlargement block delivered | Bands replaced by explicit authority; no accepted print-test thresholds | No thresholds to implement; do not manufacture bands | Existing and focused offer/size/source invalidation tests |
| Parent input formats, production Action/colour settings, TIFF validation/review, multiple outputs and safety | Current PSD/PDF preparation; managed approved raster; adapter and per-output review | Reassess after children | Source W1 retain/regenerate choice separately superseded by visual-only inputs | No additional parent work authorized | Current accepted reports plus affected tests and independent clause reassessment |

Current evidence read includes SCRUM-11081 persisted automatic bounds, SCRUM-11082 manual
selected/applied geometry, SCRUM-11083 explicit Keep Original Extent, SCRUM-11104 effective
resolution, SCRUM-11118/11119 Settings/localization, PSD/PDF preparation and flexible sizing /
Add Another Size. The 4 September audit is historical and is not the implementation authority.

## Shared calculation and preparation authority

| Fact | Existing authority reused |
|---|---|
| Approved source identity / pixels | `SessionService.ResolveSizingSource`, approved upstream Revision and its validated `FileFacts`; PSD/PDF preparation predicates remain mandatory |
| Requested limits / target | `PlanSize`, `PrintDimensions`, `FlexibleSizeSelection`, verified `PresetPrintRecommendation` |
| Proportional output geometry | `PrintPreparationPlan.For` → `FitWithinBounds`, or `TargetEdgePrintPreparationPlan.For` → `ScaleToTargetEdge` |
| Executable preparation | `WorkflowSnapshot.UsablePhotoshopPreparation`; `FitWithinBoundsPreparation` / `TargetEdgePreparation`; immutable producing attempt snapshot |
| Output resolution | Existing `PrintDimensions.ProductionDpi` / preparation `ProductionDpi`, fixed at 300 |
| Effective source resolution | Existing `TiffEffectiveResolution.EffectiveDpi`, called by both preflight and Final Review |
| Enlargement requirement | Existing target-edge plan `RequiresEnlargementAuthority` |
| Permission to proceed | Existing `EnlargementAuthority`, snapshot validity predicates and exact `AuthoriseCurrentEnlargementAsync` offer binding |
| Current offer identity | Existing service-owned enlargement offer; no new offer system |

The new Workflow-owned `PrintDimensionsPreflight` carries only the operator facts needed here.
`SessionView.Preflight` reconstructs the committed proposal. The read-only
`ISessionService.PreviewPrintDimensionsAsync` accepts the same sizing commands and calls the
same `PlanSize` path as confirmation. It reads persisted authority without committing anything.
It deliberately does not call the mutating integrity-recovery path: merely viewing a proposal
cannot invalidate files or records. Actual confirmation and production keep their existing
integrity verification.

Effective source PPI is **source pixels / (physical millimetres / 25.4)** for each axis.
Physical millimetres describe the preparation's projected pixel canvas at 300 PPI, matching
SCRUM-11104's existing definition. They are not the maximum fit box, the customer's original
file DPI tag, or null PSD/PDF container dimensions. The final-review calculation continues to
read its exact producing attempt; it does not read a mutable current-session DPI.

There is one effective-DPI formula. WPF and the ViewModel only parse operator input and format
the returned facts. Internally the calculation retains precision; display uses existing one
decimal millimetres and whole PPI, with both axes retained when pixel rounding distinguishes them.
Projected output pixels remain planning evidence, not a Photoshop target pair or actual readback.

## Graphic bounds semantics

The projection resolves geometry by the preparation source Revision's exact producing attempt,
never by whichever attempt ran last and never by re-scanning alpha at Print Dimensions.

| Source history | Operator display |
|---|---|
| Automatic Trim | Artwork content = persisted `ContentBounds` size; final canvas = persisted `AppliedBounds` size |
| Manual Crop | Selected artwork = persisted `SelectedBounds` size; final canvas = persisted `AppliedBounds` size |
| Explicit Keep Original Extent | Full original canvas; exact approved upstream canvas dimensions; no fabricated rectangles |
| No relevant crop/trim | No crop or trim applied; exact approved canvas dimensions |
| Legacy crop without saved details | Crop details unavailable; exact approved canvas still shown, without invented content bounds |

The panel does not expose coordinates or half-open rectangle vocabulary. Content/selection and
final canvas are separate facts, so a safety margin never becomes falsely described as artwork.
An ordinary manual processing import is not automatically called a crop. Returning upstream and
reprocessing resolves a new approved source and producing attempt; invalidated plans disappear.

## Enlargement, prepared sources and multiple sizes

Numeric effective PPI is information. There are no invented sufficient/warning/blocking bands,
colours or hidden PPI blockers. A normal plan proceeds through existing controls. An enlargement
still requires the existing explicit authority for its exact source, requested edge, size and
offer. A draft creates neither an offer nor authorization. Confirming a changed size uses the
existing workflow, which invalidates the relevance of earlier authority even when output pixel
rounding happens to produce the same pair.

The committed offer handle follows existing service lifetime semantics; preflight does not
persist it. Restart reconstructs plan/source/bounds/resolution/authorization facts from existing
records, while an outstanding service offer may receive a fresh handle as it did before.

Independent review found that the old service retained previously displayed handles for the
same plan. The existing offer store is tightened to keep only the current offer per session.
Publishing a new offer replaces the old one; withdrawing the offer removes it. Acceptance checks
and consumes the exact current session/handle atomically, then delegates to the unchanged source,
hash, edge and requested-size checks. An old handle cannot consume the newer offer. This changes
no persisted authority schema and introduces no second authorization system.

PSD/PDF remain visual-only sources. Sizing uses the approved managed prepared visual raster
that production consumes. Source spot/W1 channels remain outside production authority, and
PrintFlow generates production W1 later through the existing contract. No source-format rules,
preparation adapter behavior, W1 or TIFF resolution contract were changed.

Add Another Size continues to create an independent proposal over the approved design. Each
size has its own physical geometry, effective resolution, enlargement decision and existing
authorization context. Earlier TIFF reviews remain attached to their immutable preparations.

## WPF, localization and accessibility

A compact panel was added to the existing Session layout, visible for a valid draft and a
committed preparation before Photoshop. Existing preset/custom controls and authorization flow
remain. Draft target edge/size and maximum-bound edits request the Workflow projection immediately;
invalid input clears old facts. Generation and session-identity checks reject late responses.

Labels use existing `Strings.resx`, `Strings.zh-CN.resx`, `OperatorCulture` and the existing
`ILocalisationService` screen-refresh mechanism. English/Chinese millimetre and pixel units retain
their existing conventions. Source identity, attempt and offer identifiers are not new operator
labels. Stable `Session.PrintDimensions.*` AutomationIds cover SourcePixels, GraphicBounds,
FinalCanvas, PrintSize, OutputPixels, EffectiveDpi, OutputDpi and EnlargementStatus; each UIA name
contains its localized label and visible value. Standard custom-size inputs also have stable IDs.

## Verification evidence

The smallest new cases cover draft side effects and rounding-sensitive authorization, replacement
of an offer handle, localized rendered drafts, exact saved crop geometry, maximum-bound/preset
updates, out-of-order query completion and keyboard traversal. Existing persistence and preparation
cases were extended rather than multiplied across formats and workflows.

| Requirement / risk | Proof location |
|---|---|
| Automatic content/applied bounds, restart | `TrimBoundsPersistenceTests`; rendered automatic proposal |
| Manual selected/applied bounds, restart | `ManualCropGeometryPersistenceTests`; rendered manual proposal |
| Explicit retained canvas and returning upstream | `KeepOriginalExtentPersistenceTests`; rendered Keep Original proposal |
| Source-bound normal/enlarged drafts, no records/files/authority written | `FlexibleSizeWorkflowTests`; `PrintDimensionsPreflightUiTests` |
| Existing enlargement block, changed-size and offer binding | `FlexibleSizeWorkflowTests`, `FlexibleSizeUiTests`, `EnlargementAuthorityTests` |
| Prepared raster authority | Existing `PsdPreparationWorkflowTests` and `PdfPreparationWorkflowTests`, with preflight assertions |
| Different sizes and independent output history | `FlexibleSizeWorkflowTests`, `AddAnotherSizeTests` |
| Same PPI before production and at Final Review | `ProductionTiffReviewServiceTests`: capture preflight while synthetic processor generation count is zero, then compare source identity, physical size and unrounded effective resolution to real decoded production-TIFF review. Nontrivial 600 effective PPI versus 300 output PPI |
| English/Chinese, UIA, keyboard, no binding errors | `PrintDimensionsPreflightUiTests`; existing affected Session/sizing/localization suites |

Focused Debug run: **132 passed, 0 failed, 0 skipped** in 53 seconds. Console evidence:
`artifacts/preflight-visual/focused-tests.console.txt`.

Affected Release run after shared command-builder correction: **787 passed, 0 failed, 0 skipped**
in 2 minutes 15 seconds. Evidence: `artifacts/tests/preflight/preflight-affected-final.trx` and
`artifacts/preflight-affected-final.log`. The final offer-handle regression and stronger
capture-before-generation assertion are validated separately after this run.

Final focused Release run after those changes: **67 passed, 0 failed, 0 skipped** in 32 seconds,
covering flexible-size persistence/UI, Final Review, preflight UI and Session accessibility.
Evidence: `artifacts/tests/preflight/offer-authority-final.trx` and
`artifacts/tests/preflight/offer-authority-final.console.log`. The new stale-offer test was first
observed failing against the old handle store, then passing after the correction.

Rendered synthetic WPF proof is saved under `artifacts/preflight-visual/`: English normal,
enlargement, automatic crop, manual crop and retained canvas, plus Chinese normal/enlargement.
Each is a real bound Session screen at 1000×700. Text and AutomationIds are read through normal
WPF automation peers; editable values use UIA Value providers. A bounded self-hosted WPF window
verifies actual Tab traversal from target edge to millimetres to Confirm and onward. No coordinate
clicks, Photoshop, Meitu or Maintop are involved. Captures are synthetic test evidence, not live
production or a physical print-quality claim.

Initial verification corrections are retained honestly: one crop-fixture constructor call and
three test-delegate type qualifications were corrected at compile time; the first rendered run
was 6/7 because the Chinese expectation used English units, while Product localization was
correct. The first broad run used stale Release binaries and reported 783/785, including that
old expectation and an architecture guard against duplicated size-command construction. Shared
command builders resolved the guard without changing the test. Capture-only dispatcher settling
resolved incomplete post-scroll text in initial PNGs. None of these earlier results is final-source
acceptance evidence.

One final full suite was justified by the shared `SessionView`, size-read-model and offer authority
changes. After the final focused fixes and independent review, Release clean and build both
completed successfully with **0 warnings and 0 errors**. The single final full Product run passed
**11,745 tests, 0 failed, 0 skipped**, in approximately 5 minutes 38 seconds, exit code 0.
Evidence: `artifacts/preflight-release-clean.log`, `artifacts/preflight-release-build.log`,
`artifacts/preflight-full.log` and `artifacts/tests/preflight/preflight-full.trx`.
The TRX counters were independently inspected. SHA-256 hashes of all 19 changed Product/test
files matched the frozen manifest `artifacts/tests/preflight/final-source-hashes.json` after
the full run; only documentation changed afterward. Historical 11,712 and the supplied
11,735/0/0 baseline are preserved rather than rewritten.

## Routing and independent review

Installed policy: **2.2 (2026-09-08)**, read from the active Codex home's
`workflows/development-routing.md`. Execution host: **Local Windows workstation**; only the
canonical repository was changed. The supplied plan/scope was retained.

| Work | Planned / requested | Actual evidence | Context mechanism |
|---|---|---|---|
| Architecture, WPF implementation, visual/interaction integration | Astra High | `gpt-6-astra` / `high`, verified from current turn runtime metadata | Root task, continued context |
| Workflow/read model, persisted authority tests and offer fix; final verification | Sol High | `gpt-5.6-sol` / `high`, runtime verified by executing agent | Native sub-agent at the separable backend boundary, reused for validation |
| Independent standards/spec review | Sol High | UNVERIFIED: reviewer reported no exposed actual runtime metadata; no model-switch claim is made | Separate native sub-agent with `fork_turns=none`, original request/CSV/current diff/evidence only |

Review found one actionable offer-identity issue, reproduced and corrected as described above.
Re-review reported **no remaining actionable code/spec findings** and independently assessed the
three original rows. Review did not run external apps or substitute its opinion for test evidence.
It verified the retained focused results and rendered proof. No new main task, worktree or branch
was created, and no root model switch is claimed. PLAN and HANDOFF are under
`docs/codex/scrum-11094-11095/`.

## Independent Jira reassessment

The exact three CSV descriptions above were re-read after implementation. These are repository
coverage classifications; no Jira issue is changed or closed through an external service.

**SCRUM-11094: SUPERSEDED_BY_DESIGN.** Millimetre sizing, automatically paired proportional
dimensions, 300-PPI pixel preparation, no silent stretch, and graphic-bounds/final-canvas display
are delivered. The approved safety margin is part of the final canvas. The literal two-field
lock-toggle design was not implemented: the accepted fit-box/one-target-edge design forbids an
unlocked non-proportional size by construction. Its business outcome is satisfied, and the
remaining display gap is closed; calling the original interaction literally FULL would be false.

**SCRUM-11095: SUPERSEDED_BY_DESIGN.** Millimetres, pixel dimensions, effective source DPI at
preflight, graphic bounds, and explicit exact-offer enlargement authority are delivered. The
literal empirical sufficient/warning/blocking bands are not delivered and are not invented.
The already accepted replacement is retained: factual resolution plus explicit enlargement
authority. No accepted print-test threshold dataset exists in the current authority.

**Parent SCRUM-11093: FULL for its current Product functional clauses.** This is an independent
clause assessment, not arithmetic over child
statuses and not a workstation/release acceptance verdict:

| Exact parent clause | Current authority and assessment |
|---|---|
| Physical sizing and replaceable Photoshop adapter for the production workflows | Workflow size plans and `IPhotoshopOutputProcessor` / preparation contract remain integrated. The new read model exposes the same plan; no second sizing engine |
| Locked aspect ratio at fixed 300 DPI | Accepted one-edge/fit-box proportional contract prevents silent stretch. Paired physical/pixel values are projected at fixed 300 PPI |
| Supported PNG/JPEG/PSD/single-page PDF | Existing PNG/JPEG managed-source routes and accepted SCRUM-11099/11100 prepared visual-raster implementations; focused PSD/PDF tests bind preflight to the actual prepared Revision |
| Detect existing white channels where relevant | Read against the accepted visual-only source contract. Source spots/W1 are not production authority; SCRUM-11101's explicit Retain/Regenerate task remains separately superseded. Production W1 validation/generation is unchanged |
| Validated Action and confirmed colour settings | Existing guarded Photoshop production seam, preset/action binding, environment gate and colour-confirmation authority remain required. Earlier accepted SCRUM-11102 evidence is retained; no new external run claimed |
| Validate generated TIFF before Final Review | Existing production TIFF inspector, exact output hash and independent review remain mandatory. The focused test uses a real accepted synthetic separated-CMYK/W1 TIFF and compares resolution to the captured pre-production projection |
| Multiple independently reviewed sizes from one approved design | Existing Add Another Size clears pending plans/authorization and preserves earlier output/attempt identity. Separate validation and hash-bound approval remain required for every output |
| Never stretch, choose a multipage PDF page, overwrite, or treat invalid TIFF as ready | Existing proportional-plan construction, explicit multipage rejection, output naming/no-overwrite and TIFF validation/readiness gates remain. Affected and full-suite evidence protects these existing boundaries |

The historical parent gap that two input formats were absent is no longer current: the accepted
PSD/PDF completion deltas closed those Product prerequisites. Keeping the parent PARTIAL merely
because separate live-acceptance tasks remain PARTIAL would repeat a child-status rollup instead
of assessing the parent wording. This task does not complete SCRUM-11132/11133 live acceptance,
SCRUM-11065 standard-set acceptance, SCRUM-11123 revalidation, Maintop, physical prints or
SCRUM-11136. No new workstation-production readiness is claimed.

## Git state and disposition

Work was confined to `D:\Repositories\printflow-Studio` on `master`, beginning at clean
baseline `421ecd346e224cc7079e428918b9982137a2760d`. Product and tests are committed locally as
`bbe89bfd8cfe2855f708b2a6d61b03ea061ba338` (`feat: add authoritative print dimensions preflight`).
This report, the append-only audit delta, PLAN and HANDOFF form the subsequent local
documentation commit, `docs: record print dimensions preflight verification`. Final delivery
requires a clean working tree on `master`; no branch, worktree, amend, rebase, push or deployment
was performed. No AI attribution or co-author trailer was added.

**PASS WITH NOTES — SCRUM-11094/11095 PRINT DIMENSIONS PREFLIGHT VERIFIED**

The notes are the retained accepted design supersessions: SCRUM-11094 and SCRUM-11095 are each
**SUPERSEDED_BY_DESIGN**; parent SCRUM-11093 is independently **FULL for current Product
functional clauses**. No empirical resolution bands or literal lock-toggle implementation
is claimed.
