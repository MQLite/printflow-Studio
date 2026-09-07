# SCRUM-11079 — Shared before-and-after review completion

Date: 2026-09-07. Canonical checkout: `D:\Repositories\printflow-Studio`, branch `master`.

## Requirement authority

Read the original CSV at `C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv` before changing Product code. The exact title is **Build Shared Before-and-After Review Component**, export Work Item ID **11202**, parent **11200**. The existing export-to-Jira mapping in `original-jira-functional-coverage-reaudit.md` maps this to **SCRUM-11079** under **SCRUM-11077**; the CSV does not literally contain the new Jira key.

Exact original Description, including acceptance criteria:

> Implement a reusable review surface for enhancement, background-removal and trim review with side-by-side and/or slider comparison, synchronised zoom and pan, Fit, 100 percent and high magnification, plus checkerboard, white and black backgrounds. Acceptance: the same component supports detailed edge inspection without duplicating workflow-specific review logic and remains practical for high-resolution local files.

## Before and after

Previously `SessionScreenView.xaml` had one shared `ArtefactPreviewPane` DataTemplate inside an ItemsControl. Its independent ScrollViewers shared the SessionViewModel zoom scale but not offsets. Only the checkerboard background and side-by-side layout existed.

The template/ItemsControl is now one `SharedReviewSurface` control. All existing general preview placements use that same control, including single-image and unavailable-preview states. Its compact wrapping toolbar sits immediately below the existing zoom toolbar. Side-by-side and checkerboard remain the defaults. Existing crop handling and TIFF specialist views are unchanged.

`ReviewViewportState` is an App presentation model held by `SessionViewModel`; it contains comparison mode, background, slider percentage, and two normalized pan positions. Existing `ZoomScale` and `IsFitToViewport` remain the zoom authority. There is no workflow-specific comparison branch and no Domain, Workflow, Infrastructure, processing, or schema change.

## Pan, zoom, fit, reset, and resize

The mapping is normalized scroll progress on each axis:

```text
position = source offset / source scrollable extent
other offset = position * other scrollable extent
```

Both axes work in either direction, with unequal image and viewport dimensions. This maps corresponding relative canvas regions; it does not claim exact registered pixels for a cropped result. Positions are bounded to [0,1]. A zero-extent axis produces offset zero and does not overwrite the remembered position. The fitting canvas stays centered; enlarging it restores its remembered relative position.

WPF delivers ScrollChanged after layout, so a synchronous reentrancy flag alone is insufficient. The control also records each programmatically requested target and consumes matching delayed events. Extent/viewport-change events reapply the authoritative normalized position rather than mistaking layout changes for user pan. Rendered tests bound the event count and verify that the settled panes produce no further events.

Resize and zoom preserve normalized progress and recalculate offsets against the new extents. This is approximate region preservation, not a graphics-editor image-center transform: with unequal extents, the exact source pixel at the center can shift. Both panes remain synchronized and bounded. Mode switches preserve this same state and the explicit zoom value.

Reset sets Fit, scale 1, and normalized pan (0.5,0.5), leaving mode, background, and slider percentage alone. An explicit 100% button selects scale 1 outside Fit. Existing 0.10–8.00 magnification limits and zoom commands remain intact. Scaling follows the existing decoded-preview pixel contract; reduced previews retain the existing source-dimensions/reduced-preview notice. Fit computes one common proportional scale from the largest payload dimensions and available review area.

## Inspection and slider policy

Checkerboard, white, and black are brushes behind the image, shared by both panes. No bitmap is flattened or replaced by changing the background. The existing Session checkerboard resource is reused. Tests verify bitmap object identity, original alpha bytes, pane identity, and actual rendered pixels under transparent, partial-alpha white, and partial-alpha dark edges.

Slider overlays After on Before using the same normalized pan state and the same scale calculation. Both use the same top-left origin on a canvas whose width and height are the maxima of the two payload canvases. Each image preserves its natural aspect ratio; neither is stretched to match the other. Uncovered areas show the chosen inspection background. This deliberately shows differences in canvas size rather than fabricating registration or crop-offset correspondence.

After reveals from left to right: 0% is Before only, 50% is After on the left and Before on the right, 100% is After only. The After layer includes its inspection background, so transparent After pixels do not accidentally reveal Before pixels. A blue divider marks intermediate positions and disappears at either endpoint. Clipping is in the shared image-canvas coordinates, so panning can move the dividing line out of the visible viewport. The accessible toolbar slider remains available. An unavailable image disables Slider while preserving its explanatory message and normal review actions.

Images decode when the pane collection is loaded. Pan, resize, background selection, slider movement, and mode switches reuse those ImageSource objects. Slider movement adjusts clipping and the divider only; no raster files are generated.

## State lifetime

All preferences are ephemeral. Mode/background/slider percentage survive review steps while the same SessionViewModel remains alive. The existing new-result Reset behavior resets zoom and pan on a new review result. They are not persisted across application restart. No database migration was added.

## Keyboard, localization, and UIA

Standard RadioButtons expose SelectionItem; the standard 0–100 Slider exposes RangeValue and supports Right/Left and Home/End. Images are not focusable. Existing Approve/Reject controls and command bindings remain unchanged.

Stable identities:

- `Session.ReviewModeSideBySide`, `Session.ReviewModeSlider`
- `Session.ReviewBackgroundCheckerboard`, `Session.ReviewBackgroundWhite`, `Session.ReviewBackgroundBlack`
- `Session.ReviewComparisonSlider`, `Session.ReviewActualSize`
- `Session.ReviewZoomOut`, `Session.ReviewZoomIn`, `Session.ReviewResetZoom`
- `Session.ReviewBefore`, `Session.ReviewAfter`, `Session.ReviewOverlay`
- The shared surface retains `Session.PreviewPanes`.

Operator names are in English and Chinese resource files, with culture explicitly pinned in exact-wording tests. IDs contain no localized text. For example, the slider announces “Comparison slider” / “比较滑块”, and White announces “White” / “白色”. Existing accessibility tests confirm no internal type-name leakage and no duplicate visible action IDs.

## Verification

New tests: `SharedReviewSurfaceTests` and `SharedReviewAuthorityTests` — **22 cases**, including the opt-in live case. They cover bidirectional unequal-extent scrolling, zero extents, fitting recovery, resize, zoom/reset, bounded event delivery, all backgrounds, actual alpha/halo pixel compositing, same bitmap identity, slider boundary pixels and 25/50/75% clip geometry, unequal dimensions without stretching, origin alignment, mode round trips, bilingual UIA names and patterns, and persisted review authority.

Targeted shared-review / rendering / accessibility / image-preview / localization / workflow smoke filter: **149 passed, 0 failed, 0 skipped**. Existing review assertions were not weakened. Two initial new background pixel checks sampled the panned middle of the image; the fixture now explicitly pans to the transparent edge. Both corrected checks pass.

Final clean solution build using the already-installed SDK `10.0.400` at `C:\Users\admin\AppData\Local\Microsoft\dotnet\dotnet.exe`: **0 warnings, 0 errors**. The system PATH finds only SDK 8, so the pinned SDK executable is invoked directly without changing configuration.

Final-source full suite: **10,903 passed, 0 failed, 0 skipped**, elapsed **3 minutes 20 seconds**. This is the accepted 10,881 baseline plus 22 new cases. The known unrelated `PhotoshopPsdBoundaryTests` settle-poll flake was **not observed**; no exact-case rerun or unrelated fix was needed. The opt-in live body was run separately and passed; the normal full-suite count includes that case with its environment gate closed.

Local verification logs and TRX: `evidence/scrum-11079/` (git-ignored). Commands use the pinned executable above:

```powershell
dotnet clean PrintFlowStudio.sln -v minimal
dotnet build PrintFlowStudio.sln --no-restore -v minimal
dotnet test tests/PrintFlow.Tests --no-build --filter 'FullyQualifiedName~SharedReview|FullyQualifiedName~SessionSmokeTests|FullyQualifiedName~ViewRenderingTests|FullyQualifiedName~SessionAccessibilityTests|FullyQualifiedName~ImagePreviewControlTests|FullyQualifiedName~Localisation'
dotnet test PrintFlowStudio.sln --no-build --logger 'trx;LogFileName=full-final.trx' --results-directory evidence/scrum-11079
```

## Live synthetic WPF/UIA proof

The opt-in `Live_synthetic_review_window_is_operable_through_Windows_UIA` passed on this workstation, with `PRINTFLOW_SHARED_REVIEW_LIVE=1`. It opens a real HWND containing the production `SessionScreenView` at a real Enhancement review step, using the existing isolated HomeScreenHarness, real repository, revision files, and review service; external processing uses the existing deterministic fake adapter. It is a focused real-window control/service smoke, not an installed-shell or external-application smoke. Only generated 1800×1200 artwork is used.

Windows UIA locates controls by stable identity and operates ScrollPattern, SelectionItemPattern, RangeValuePattern, and InvokePattern. No screenshots or coordinate clicks are used. The retained transcript records:

1. Before ScrollPattern at 70% horizontal / 30% vertical; After reaches 70/30.
2. White → Black → Checkerboard, with shared render-state confirmation.
3. Slider RangeValue at 25%, 50%, and 75%.
4. Real WPF keyboard traversal through background choices reaches the Slider. Routed Right/Home/End produce 76/0/100; it returns to 75%.
5. Zoom In, overlay pan at 60/40, and aligned 75% clip; return to side-by-side retains 60/40.
6. All comparison/background choices accept keyboard focus; WPF keyboard traversal reaches Approve after the new controls.
7. Approve through UIA completes normally, followed by independent repository reload and hash/subject verification.

A supplemental attempt using global physical keystrokes lost foreground focus; its route was rejected rather than counted as proof. The passing test uses WPF's actual focus-traversal and routed-key mechanisms in the shown window, avoiding delivery to unrelated applications. These are not claimed as physical-keyboard end-to-end evidence. Both rejected attempt logs are retained locally alongside the passing run.

Passing transcript: `evidence/scrum-11079/live-transcript.txt`; live log: `live-final.log`. Revision: `01a079a4-d2d2-7bca-a3f1-365122ed3a96`. SHA-256: `30E264742A25E34E4E975E3C1F87DF73C15C74FE08F118C261B44AC6607CCE0F`.

## Product authority and scope

The independent automated authority test reloads the repository after each background/mode/zoom change and asserts unchanged Revisions, Reviews, and Steps. It rereads the actual file bytes, then approves and confirms that ReviewDecision.SubjectId and ReviewedSha256 still identify the original reviewed Revision. The live test separately reloads that binding after UIA approval.

No change to review decisions, rejection reasons, hash checks, transitions, Trim processing, Photoshop, Meitu, TIFF specialist review, or persistence schema. SCRUM-11083 and SCRUM-11082 remain out of scope; SCRUM-11077 is not closed.

## Git and coverage decision

Started from clean `master` at `a19333f`. Only App presentation/resources, focused tests, and this report/coverage evidence are changed. New local commits only; no branch, worktree, clone, amend, rebase, push, or attribution trailer.

**SCRUM-11079: PARTIAL → FULL.** All material original criteria and the requested completion checks are met. The dated addendum in `original-jira-functional-coverage-reaudit.md` records this scoped update while preserving the historical audit. SCRUM-11077 remains PARTIAL; SCRUM-11082 and SCRUM-11083 are unchanged.

Implementation, tests, this report, and the coverage addendum are included in a new local commit on `master`; the final response identifies that commit. `git diff --check` passes. No changes are pushed.

**PASS — SCRUM-11079 SHARED BEFORE/AFTER REVIEW VERIFIED**
