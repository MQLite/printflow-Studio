# SCRUM-11082 — Manual crop adjustments and persisted geometry

Date: 2026-09-07. Canonical repository: `D:\Repositories\printflow-Studio`, `master`.

## Requirement authority

Read the original CSV before Product edits: `C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv`. The exact title **Implement Manual Crop Fallback for Non-Transparent Inputs** is source Work Item **11205**, mapped to **SCRUM-11082**, parent 11200, High priority, 5 points. Original description / acceptance criteria, verbatim:

> When useful transparency is absent, do not infer artwork boundaries from white, black or another background colour. Enter manual crop mode and allow tight trim, uniform margin, independent edge adjustment or trim cancellation with bounds validation. The result must be represented as a new processing result suitable for independent review.

Also independently re-read **Test Trimming and Manual Crop Deterministically**, source Work Item **11702**, mapped to **SCRUM-11126**:

> Test the trimming module through its interface for alpha bounds, safety margins, transparent inputs, no-transparency inputs, manual crop adjustments, cancellation and invalid crop bounds. Verify physical-canvas and bounds metadata remain consistent and no colour-based automatic boundary guessing occurs.

Inspected the existing editor, `ManualCropEligibility`, `IManualCropProcessor` / `WicManualCropProcessor`, `ManualImport`, `TrimMargin`, `TrimBounds`, `TrimGeometry`, `ProcessingAttempt`, migration sequence through 0012, Session read model/review, and the accepted SCRUM-11081 and SCRUM-11083 reports. Historical phase numbers do not determine acceptance.

## Previous gaps and geometry contract

Manual crop previously accepted only a drawn rectangle and discarded its source-space origin after producing the file. Tight/Uniform/Per-edge controls existed only for automatic trim. This slice adds the manual controls and attempt-owned history without changing automatic detection or the independent shared review.

`ManualCropGeometry` is an immutable Domain value with `SelectedBounds`, `AppliedBounds`, and `Margin`. Selected bounds are the operator's rectangle before expansion; applied bounds are the exact source pixels kept. Both reuse `TrimBounds` half-open `[left, top → right, bottom)` coordinates. Width and height are edge differences. Manually selected pixels are never described as alpha-detected `ContentBounds`.

The audit found `TrimMargin` explicitly means margin around alpha content. Manual choices therefore use a separate `ManualCropMargin` value and persistence context. Only the existing mode vocabulary `TrimMode` is shared, with its documentation generalized to outward expansion around a base rectangle. Automatic session `TrimMargin` and automatic attempt `TrimParameters` remain independent. A test runs automatic Uniform 5 followed by a different manual choice and verifies both histories remain truthful.

Tight applies the selection unchanged. Uniform expands all four sides by the requested non-negative integer. Per-edge independently expands left/top/right/bottom. Expansion uses widened arithmetic and clamps to the physical source canvas, even for `int.MaxValue`; requested values remain recorded when expansion is clamped. Negative margins and invalid numeric UI input are refused.

The base rectangle is never repaired: empty, negative or out-of-canvas bounds are invalid. The WIC processor validates against the decoded physical canvas before margin expansion. The existing display mapping had accepted partially out-of-image drags by clipping them; it now refuses those drags, retaining only its numerical edge tolerance. Rendered and geometry tests cover that boundary.

## Producing and persisting a result

`SubmitManualCrop` carries the selected rectangle and explicit manual margin through the workflow effect to the processor. The processor does no alpha or colour-boundary analysis. It computes the geometry, crops the exact applied rectangle with WIC, and returns the recorded geometry. The service verifies the returned geometry against the request/source dimensions and independently inspects the actual output. A dimension mismatch fails validation. Success creates the normal managed `manual-crop.png`, immutable `ManualImport` Revision/hash, and `ReviewRequired` state.

The same closing transaction records geometry on the producing `ProcessingAttempt`, its output Revision, and review state. The normal `SessionView` resolves metadata by the displayed Revision's producing attempt and exposes `ArtefactManualCropGeometry`, `CurrentManualCropGeometry`, and `HasManualCropGeometry`. The ViewModel does not read raw attempt rows.

Forward migration **0013_manual_crop_geometry.sql** adds 13 nullable typed columns: eight selected/applied edges, four requested margins, and the requested mode. Historical migrations are unchanged. Existing rows stay null; source-space origins are not inferred from output dimensions, current settings, or timestamps. Migration and repository tests cover historical rows.

SQLite coherence triggers require complete non-empty containing rectangles, valid margin mode/values, and a successful manual-producing attempt with an output. They reject coexistence with automatic trim metadata. An immutability trigger refuses edits or erasure once recorded. Tests attempt individually coherent replacements of the rectangle pair and margin mode, so refusal is a history guarantee rather than merely a validation side effect. Rejecting crop A and applying crop B creates independent attempts/Revisions; returning upstream invalidates current descendants while preserving both histories.

## Editor and review

The existing drag editor now contains standard WPF Tight, Uniform, and Per-edge radio buttons and numeric text inputs. Selected and actual applied rectangles have separate restrained outlines and coordinate summaries. Changing values updates the applied preview without writing files, attempts, Revisions, or session settings. The actual clamped rectangle shown is the rectangle passed through the crop geometry contract.

Stable AutomationIds include `Session.ManualCropModeTight`, `Session.ManualCropModeUniform`, `Session.ManualCropModePerEdge`, `Session.ManualCropUniformMargin`, `Session.ManualCropMarginLeft/Top/Right/Bottom`, `Session.ApplyManualCrop`, and `Session.CancelManualCrop`. English and Chinese labels/names are exercised with explicitly pinned cultures. Standard keyboard focus traversal and UIA selection/value/invoke providers use the real command path.

The existing shared Before/After Trim review shows **Manual crop**, selected bounds, applied crop bounds, and the recorded margin choice. Pan, zoom, slider, and inspection backgrounds continue through the accepted shared surface.

Cancel exits editing and clears draft state, persisting nothing; the workflow still waits for a decision. **Keep original extent** remains a separate reachable action that records Trim as Skipped and retains the approved pre-Trim Revision. Apply produces a real result requiring review. These intentions are not combined.

Uncommitted pointer/margin editing is deliberately not persisted. Closing before Apply leaves no crop result or attempt. After Apply, loading a new service and reopening the review reads the same persisted Revision/hash, geometry/margin, and ReviewRequired state; it does not recalculate geometry from the cropped file.

Approval makes the exact manual Revision downstream authority. Customer Design Print Dimensions and its preparation plan bind that Revision and cropped dimensions. Prepare Asset export consumes that Revision and preserves its cropped bytes/dimensions. Direct integration assertions cover both workflows.

## Verification

New tests: `ManualCropGeometryTests`, `ManualCropGeometryPersistenceTests`, and `ManualCropAdjustmentUiTests`; extended real-interface processor, migration, Domain-boundary and crop-surface tests. Coverage includes Tight equality, exact Uniform and asymmetric edges, overflow-safe clamping, strict invalid bounds, negative inputs, exact raster pixels/dimensions, independent automatic/manual settings, SQLite roundtrip and legacy nulls, immutable retries, return-upstream history, review/restart, cancellation, and downstream authority.

Existing automatic trim, manual eligibility, shared review, Keep Original Extent, accessibility/localisation, and workflow tests remain part of final verification. Evidence logs are retained locally under `evidence/scrum-11082/` (git-ignored).

Clean build with installed SDK `10.0.400` at `C:\Users\admin\AppData\Local\Microsoft\dotnet\dotnet.exe`: **0 warnings, 0 errors**. No dependency or SDK configuration changed. Final targeted regression set: **558 passed, 0 failed, 0 skipped**, about 1 minute 3 seconds (`targeted-final.log` / `.trx`). The final complete-suite result is recorded below.

## Live A/B/C and independent readback

Three final cases ran separately with `PRINTFLOW_MANUAL_CROP_LIVE=1`, using generated opaque 12×10 artwork, real shown WPF windows, Windows UIA, production SessionService, real SQLite and real WIC. All **3/3 passed**. Rectangle selection uses the real measured canvas's drag-coordinate mapping seam; mode/value/Apply/Cancel use UIA. No screenshots or physical global pointer-drag evidence is claimed.

| Case | Selected | Requested adjustment | Actual applied | Raster |
| --- | --- | --- | --- | --- |
| A Uniform | `[3,2 → 9,7)` | Uniform 2 px | `[1,0 → 11,9)` | 10×9 |
| B Per-edge | `[3,2 → 9,7)` | Left 2, Top 0, Right 4, Bottom 1 px | `[1,2 → 12,8)` | 11×6 |
| C Cancel then retain extent | Same temporary selection as B | Same temporary margins as B | Not applied | No new raster |

A's output Revision is `01a07a2a-d4bf-7667-8d19-caf73300874e`, SHA-256 `ACEEDBE7484DA6F11E907A76CD64BB4CFD834B6E2A606C1904877D735BE21C71`. B's output Revision is `01a07a2b-19a1-7397-9161-29eb7cecf4df`, SHA-256 `9D039ADC3EB001EC934326BFD71F61D8C69DB64B858589B5FBCF4EE157B3348D`.

For A and B, direct raw SQLite SELECT independently checked all eight edges, four requested margins, mode, and OutputRevisionId. Actual PNG decoding checked dimensions and SHA-256. After closing the editor window, a newly built service loaded through a fresh SQLite connection; a new real WPF window displayed the same review metadata and shared Before/After previews. Revision, hash, geometry, margin and ReviewRequired survived. Approval then made the crop `UpstreamRevisionOf(PrintDimensions)`. The persistence tests additionally assert the asset export attempt's actual `InputRevisionId` and the customer preparation plan's source authority.

For C, temporary editing and Cancel left session/steps, attempts, Revisions and files unchanged. Keyboard traversal reached the separate Keep original extent action; UIA Invoke advanced to Print Dimensions with the pre-Trim Revision. All cases verified unchanged source bytes and unchanged/free automation lock. Synthetic source SHA-256: `D077D59C35EFE4F40AAA08A864D3F658ED2D2988D1547EB090FE4BB786F4D778`.

Transcripts: `manual-crop-live-uniform.txt`, `manual-crop-live-per-edge.txt`, `manual-crop-live-cancel-keep.txt`; final case results: `manual-crop-live-A/B/C.trx`. Earlier rendered assertions incorrectly used effective `IsVisible` on an unhosted visual tree; they now assert bound visibility, with effective focus/visibility checked in real windows. A combined rendered/live process encountered a UIA window/COM problem; its failed evidence is retained in `manual-crop-live-final.trx`. The final separate-process runs passed without a Product workaround. Restart evidence means closed/reopened real windows and rebuilt service/fresh SQLite connections, not termination/relaunch of the installed application executable.

Reproduction uses the pinned executable above:

```powershell
dotnet clean PrintFlowStudio.sln -v minimal
dotnet build PrintFlowStudio.sln --no-restore -v minimal
# In separate processes with PRINTFLOW_MANUAL_CROP_LIVE=1:
dotnet test tests/PrintFlow.Tests --no-build --filter 'FullyQualifiedName~Live_synthetic_WPF_UIA_uniform'
dotnet test tests/PrintFlow.Tests --no-build --filter 'FullyQualifiedName~Live_synthetic_WPF_UIA_per_edge'
dotnet test tests/PrintFlow.Tests --no-build --filter 'FullyQualifiedName~Live_synthetic_WPF_UIA_cancel_keep'
# Ordinary environment, live opt-in flag removed:
dotnet test PrintFlowStudio.sln --no-build --logger 'trx;LogFileName=full-final.trx' --results-directory evidence/scrum-11082
```

## SCRUM-11126 and Epic reassessment

The original SCRUM-11126 criteria are evaluated individually: alpha bounds, safety margins, transparent/no-transparency input and no colour-based guessing retain their real-interface coverage; manual Tight/Uniform/Per-edge, cancellation, strict invalid bounds and exact source pixels are tested through the real crop interface and service. SCRUM-11081 covers persisted automatic bounds; this slice adds manual bounds/margin and actual-raster/Revision consistency, including restart. Final status depends on the final verification results below.

SCRUM-11077 is independently assessed and remains **PARTIAL**. The separate SCRUM-11078 import/workflow-selection UX gaps are outside this slice. Closing manual crop does not imply completion of the Epic.

Final determination: **SCRUM-11082 PARTIAL → FULL** and, independently against the exact original criteria above, **SCRUM-11126 PARTIAL → FULL**. The metadata consistency gap is now covered for both automatic and manual results. The dated coverage delta preserves the historical audit rows.

## Final complete suite and known unrelated failure

One complete suite ran against final Product/test source: **11,382 passed, 1 failed, 0 skipped; 11,383 total**, duration **3 minutes 55 seconds**. This is the accepted 11,353-test baseline plus 30 additional cases. The ordinary suite includes three opt-in live test entry points with the live environment gate closed; their full live bodies separately passed as recorded above.

The only failure was the known unrelated PSD settle-poll case:

`PhotoshopPsdBoundaryTests.Production_psd_path_enforces_real_guards_and_independent_validation(variant: "malformed", expected: PsdPreparationFailed)`

It returned `OutputUnreadable` instead of `PsdPreparationFailed` under the full run. Evidence is preserved in `full-final.log` / `full-final.trx`. The **exact one case** passed on isolated rerun: **1 passed, 0 failed, 0 skipped**, 105 ms (`psd-exact-rerun.log` / `.trx`). No PSD code/test was modified, and no second complete-suite run was used to replace the original result.

```powershell
dotnet test tests/PrintFlow.Tests --no-build --filter 'FullyQualifiedName~PhotoshopPsdBoundaryTests.Production_psd_path_enforces_real_guards_and_independent_validation&DisplayName~malformed' --logger 'trx;LogFileName=psd-exact-rerun.trx' --results-directory evidence/scrum-11082
```

The full suite is therefore reported with the known unrelated failure, not as an all-green run. Manual crop's targeted regressions, live cases, persistence/readback and build all pass.

## Git discipline

Started clean on `master` at `b78f748` (accepted SCRUM-11083). Work stays in the canonical checkout; no branch, worktree, alternate clone, amend, rebase or push. Unrelated files and the known Photoshop PSD settle-poll flake are unchanged. Implementation, tests, this report and the dated coverage delta are included in local commit 49b6294 and a small follow-up removing only a terminal blank line from the new migration. The complete suite tested the same executable SQL/code; the whitespace cleanup changes no statement or test. `git diff --check` passes. No attribution trailer is added.

**PASS WITH NOTES — SCRUM-11082 MANUAL CROP FALLBACK VERIFIED**
