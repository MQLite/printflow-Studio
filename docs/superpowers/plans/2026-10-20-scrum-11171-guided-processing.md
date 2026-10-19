# SCRUM-11171 Guided Processing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Focus automatic processing, exact-result review, crop, print dimensions and TIFF preparation on the current decision.
**Architecture:** Extend the accepted E1 theme and E2/E3 page projection in the App layer. Reorganize the existing SessionScreenView and comparison controls around service-authorized state; retain exact approval controls, crop geometry, service gates and downstream invalidation.
**Tech Stack:** .NET 10 WPF, CommunityToolkit.Mvvm, xUnit, synthetic SQLite/fake adapters, offscreen RenderTargetBitmap.
**Spec:** `C:/Users/admin/.codex/worktrees/scrum-11177-guided-theme/printflow-Studio/output/imagegen/SCRUM-11168/2026-10-19-v2/design-spec.html`; original AC `docs/codex/printflow-operator-ux-backlog/SCRUM-11171_JIRA_SCOPE.json`.

## Global Constraints
- One task/image, three existing routes, original/InputSnapshot unchanged. Asset PNG has no print/white/TIFF; direct TIFF has no Meitu/default crop.
- Both TIFF routes require explicit 0/1/2 px white ink choice and reason, no default; source PSD/PDF spot channels are not reused.
- Preserve exact task/step/revision/file approval, preview-failure invalidation, downstream invalidation, independent per-size generation/review/save, 300 PPI/CMYK/W1/environment gates and local privacy.
- App UI only. No Domain/Workflow/Infrastructure/persistence/automation redesign, AI execution, real external app, desktop input, merge/push/deploy/Jira write or later Epic.
- zh-CN/en resources; Chinese visual QA. Test only synthetic GUID/temp files/SQLite, fake ports, offscreen UserControl rendering. All tests use audited 11-exclusion safe script; never unfiltered test or Window.Show/ShowDialog/App launch.
- Native sequential execution; parent arranges one fresh overall review. Retain plan scratch. No per-task agents or repeated approval.

## Review Focus
- Stop/takeover while running must remain reachable even as unrelated forms disappear (Task 1).
- Single/missing preview must not offer meaningless comparison or allow stale exact-result approval (Task 2).
- Crop cancel must preserve pending identity; new crop must re-enter review with unrelated save/print forms hidden (Task 3).
- Tiny/fractional/invalid dimensions retain raw validation precision and expose current risk without silently authorizing enlargement (Task 4).
- Both TIFF routes must explicitly select white ink and expose blockers before generating; no inferred approval from another size (Task 4).

### Task 1: Focused processing and truthful status
**Files:** Modify `src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs`, `Views/SessionScreenView.xaml`, `Resources/Strings*.resx`; create `tests/PrintFlow.Tests/Integration/Ui/GuidedProcessingTests.cs`.
**Interfaces:** Consume GuidedPage, runtime CanStopAutomation/CanTakeOverAutomation/IsStopping and existing stop notices; produce ShowsGuidedProcessing, GuidedProcessingApplication/State/Notice, ShowsGuidedImageWorkspace and ShowsGuidedTechnicalDetails presentation only.
- [x] Write real fake-adapter running/stop/takeover/success tests asserting indeterminate progress, application/state, no review/form controls while running and unchanged service approvals.
- [x] Run safe script Include FullyQualifiedName~GuidedProcessingTests, observe meaningful RED.
- [x] Implement focused progress card and page visibility, preserving stop/takeover warning blocks and failure/recovery routes.
- [x] Run processing and existing stop/takeover/guided projection filters; record GREEN and commit.

### Task 2: Image-dominant common review
**Files:** Modify `Views/SessionScreenView.xaml`, `Views/SharedReviewSurface.cs`, `ViewModels/SessionViewModel.Guided.cs`; create `Integration/Ui/GuidedProcessingReviewTests.cs`.
**Interfaces:** Consume existing ReviewGuidanceChecks, ReviewTargetIdentity, PreviewPanes, HasTiffReview, review commands; produce meaningful-tool visibility and common image-heavy layout without replacing ReviewApprovalButton.
- [x] Write offscreen tests for image region greater than guidance region, 2-4 checks, single/missing-image tool visibility and final-review save draft isolation.
- [x] Run new tests to meaningful RED.
- [x] Use responsive bounded guidance column, retain common exact-result frame/neutral focus; hide unrelated save draft during review and comparison controls when no meaningful pair/image.
- [x] Run new tests plus SharedReview/ReviewGuidance/TiffFinalReview/authority tests through safe script; commit with unchanged baseline failures distinguished.

### Task 3: Isolated crop workspace
**Files:** Modify `Views/SessionScreenView.xaml`, `ViewModels/SessionViewModel.Guided.cs`; create `Integration/Ui/GuidedProcessingCropTests.cs`.
**Interfaces:** Consume existing IsCropping/IsAdjustingTrim, restore/cancel/compare/use commands and CropSurfaceLayout; produce crop-only workspace layout and visibility, no crop math/service changes.
- [x] Write tests asserting unrelated technical/save/review forms absent, usable image/handles/actions, cancel identity preserved and use returns to required review.
- [x] Run crop tests to meaningful RED.
- [x] Give crop image full workspace with existing own controls, retain inline invalid/units/compare/original suggestion and exact apply legality.
- [x] Run crop tests and safe ManualCropAdjustment/ManualCropUi/CropHandleGesture/AdjustTrimFromReview coverage; commit.

### Task 4: Focused dimensions and explicit white ink/preflight
**Files:** Modify `ViewModels/SessionViewModel.Guided.cs`, `ViewModels/SessionViewModel.PrintDimensions.cs`, `Views/SessionScreenView.xaml`, `Resources/Strings*.resx`; create `Integration/Ui/GuidedProcessingPrintTests.cs`.
**Interfaces:** Consume existing Preflight/PreparationPlan, ConfirmedMaximumBounds/ConfirmedWhiteUnderbase, WhiteUnderbaseChoices and command gates; produce read-only concise actual/requested/pixel/white-ink summary with readable display formatting only.
- [x] Write both-route tests for explicit 0/1/2/no-default selection, summaries, invalid raw value preservation, enlargement separation, legal page visibility and no form on asset route.
- [x] Run print tests to meaningful RED.
- [x] Surface requested vs actual size/pixels/risk and white-ink reason with source-channel caveat; keep production technical facts in disclosure and blockers visible. Format display precision only; preserve raw values and inherited F6 status.
- [x] Run new print tests plus PrintSizeGuidance/PrintDimensions/white-ink/TIFF relevant safe tests; commit.

### Task 5: Chinese evidence and completion checks
**Files:** Create `Integration/Ui/GuidedProcessingVisualEvidenceTests.cs`, `output/ui/SCRUM-11171/INDEX.md`, checks/PNG/geometry, `docs/codex/printflow-operator-ux-backlog/SCRUM-11171_RESULTS.md` and maintenance notes.
**Interfaces:** Render delivered screens from synthetic state, record 1000x700/1280x720/1920x1080 DIP, SHA256, geometry and raw TRX/logs.
- [x] Capture representative running/stopping/review/crop/dimensions/white/preflight/blocked states; assert binding/action/geometry invariants and inspect every final PNG.
- [x] Run one final safe UI/architecture broad suite and relevant persistence suite; compare exact inherited failure names/messages to accepted E3 evidence, never weaken tests.
- [x] Save AC map, rulings/costs, maintenance boundaries and NOT RUN real keyboard/UIA/DPI/novice/production.
- [x] Commit code/evidence with clean branch and hand full BASE..HEAD to parent for one independent review; keep this context for required fixes.

## Routing and authorization
NormalRoute/RequestedRoute/ExecutionTarget gpt-6-astra/high, RouteOffset=0, UNCHANGED. Parent verified runtime session 01a155df-3280-7bb1-aecc-56adaf01199a turn_context 2026-10-19T20:34:15.714Z model gpt-6-astra effort high. Post-plan: immediate units are WPF visual/interaction integration and their inseparable synthetic safety proofs; CONTINUE. Reevaluate at major context boundary; report cleanly separable non-UI work rather than claiming a switch.
Ruling: existing approved v2 and explicit continue/Native authorization permit plan then execution without repeated approval — no new product design — cost if wrong: review may require adjustments to presentation; no external publication.
Ruling: exact approved v2 clarifies P05 as both TIFF routes explicit white ink, overriding older ambiguous PSD-only wording — preserves business rules — cost if wrong: operator sees an extra explicit classification already required by service.
Self-review: P01-05 each mapped above; no business-layer rewrite is required. Existing authority and persistence tests supplement UI checks; inherited failures remain explicitly unclaimed.

### R1 correction after the one independent review
- [x] Independently trace hidden coded crop failures and reproduce a real refused crop with preserved draft/result, including reload success/failure in zh-CN/en.
- [x] Add crop-local existing failure bindings with unique diagnostic IDs; meaningful RED 4/4 then GREEN 4/4.
- [x] Capture and inspect two Chinese 1000×700 error states; preserve prior evidence.
- [x] Finish affected crop/failure/guided checks, record scope/limitations, and return a clean correction commit to the parent for final focused verification.
