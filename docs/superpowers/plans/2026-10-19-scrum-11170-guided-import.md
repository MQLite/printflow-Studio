# SCRUM-11170 Guided Import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Make Home, route choice and original confirmation distinct, focused guided steps while preserving existing single-image workflow authority.
**Architecture:** Reuse the accepted E1 theme and E2 session projection. Add App-only entry-screen lifetime/input ownership around existing service calls; reorganize Home and route cards without changing the engine, persistence, adapters or preview authority.
**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm, xUnit, synthetic SQLite fixtures, offscreen RenderTargetBitmap.
**Spec:** `C:/Users/admin/.codex/worktrees/scrum-11177-guided-theme/printflow-Studio/output/imagegen/SCRUM-11168/2026-10-19-v2/design-spec.html`; exact AC: `docs/codex/printflow-operator-ux-backlog/SCRUM-11170_JIRA_SCOPE.json`.

## Global Constraints
- One task / one image; import before explicit route choice; first derived revision locks route; preserve InputSnapshot and source bytes.
- Preserve three routes, exact review identity, downstream invalidation, 300 PPI/CMYK/W1, environment gates, automation lock, retention and local privacy.
- Asset ends in PNG with no print dimensions/white ink/TIFF. Direct TIFF excludes Meitu and default crop. TIFF white ink remains explicit 0/1/2 px plus reason, no default.
- Preserve zh-CN/en. AI dependency SCRUM-11166 is To Do: show unavailable entry only; full AI integration BLOCKED_DEPENDENCY.
- Native sequential execution, no task agents, one fresh whole-branch review arranged by parent. No merge/push/deploy/Jira writes or scratch deletion.
- Only synthetic GUID/temp SQLite/files, fake adapters/pickers, offscreen WPF; no actual App launch, Window.Show/ShowDialog, workstation opt-in or desktop input. All dotnet test commands use the audited E3 filter script.

## Review Focus
- Late cancelled import after leaving Home must neither navigate back nor replace another notice (Task 1).
- Old route UIA provider or held key after rebinding must never select for the new task (Task 2).
- Async rename/select after Open changes must never alter the new task or clear its busy ownership (Task 2).
- Unsupported/corrupt/multiple input remains distinct with original bytes intact; picker/drop share authority (Task 1 existing + regression tests).
- PDF/PSD source details and source-only page must not expose future dimensions/white ink/save forms (Task 3).

### Task 1: Home import ownership and focused start
**Files:** Modify `src/PrintFlow.App/ViewModels/HomeViewModel.cs`, `Views/HomeView.xaml`, `Views/HomeView.xaml.cs`, `Navigation/NavigationService.cs`, `Resources/Strings*.resx`; create `Navigation/IEntryScreenLifetime.cs`, `Views/EntryActionScope.cs`; test `tests/PrintFlow.Tests/Integration/Ui/GuidedImportHomeTests.cs`.
**Interfaces:** Consumes existing ISessionService.ImportAsync, IFilePicker, INavigationService; produces IEntryScreenLifetime.Deactivate(), HomeViewModel.IsImporting/CanStartImport/CancelImportCommand and guarded entry input scope. No new business authority.
- [ ] Write failing real-service/deferred-service tests for late/cancelled import and retained Home UIA; offscreen geometry asserts import above environment.
- [ ] Run E3 filter script Include FullyQualifiedName~GuidedImportHomeTests; expected meaningful RED.
- [ ] Implement transient deactivation/cancellation and shared picker/drop guard; import progress/feedback; import first, continuing tasks above finished history, environment details secondary. Keep error and recovery notices reachable.
- [ ] Run Home, GuidedImportHome and existing import-cancellation filters; expected no new failures versus fresh baseline.
- [ ] Commit the concrete Task 1 source/tests/resources and record exact test result/rulings.

### Task 2: Explicit route cards and route-screen ownership
**Files:** Modify `ViewModels/WorkflowSelectionViewModel.cs`, `Views/WorkflowSelectionView.xaml`, `.xaml.cs`; test `Integration/Ui/GuidedImportRouteTests.cs`.
**Interfaces:** Consumes Task 1 IEntryScreenLifetime/EntryActionScope and existing SelectWorkflow/SetOutputName; produces screen-local operation generation and interaction identity. Workflows retains exactly three catalogue choices.
- [ ] Write tests for three simultaneously visible route actions with purpose/output/dimensions, late rename after Open and stale UIA/value-provider/held-key input.
- [ ] Run GuidedImportRouteTests; expected RED from missing lifetime/layout guard.
- [ ] Implement operation generation checks after awaits and in finally; reuse E2 guarded controls with entry scope; compact cards remove full future step lists, preserve source summary and preview, explain route lock/back behavior.
- [ ] Run GuidedImportRoute + HomeAndWorkflowSelection + workflow-selection tests; expected no new failures.
- [ ] Commit Task 2 and ledger results.

### Task 3: Source conditions and truthful AI availability
**Files:** Modify `ViewModels/SessionViewModel.Guided.cs`, `Views/SessionScreenView.xaml`, Home localized labels as needed; test `Integration/Ui/GuidedImportSourceTests.cs`.
**Interfaces:** Consumes existing GuidedPage/HasPdfSource/HasPsdSource and InputSnapshot projection; produces only source-page visibility projection. AI is inert localized information, no command/service/transfer.
- [ ] Write meaningful tests that PDF/PSD info follows corresponding source phase and later phase collapses it; local three-route source page remains usable with unavailable AI.
- [ ] Run GuidedImportSourceTests; expected RED where needed; record already-correct source projection as characterized rather than inventing production changes.
- [ ] Restrict source notices to relevant source/preparation/review and surface current filename/route, without adding source editing or preparation business.
- [ ] Run source-focused and E2 guided filters; expected no new failures.
- [ ] Commit Task 3 and record AI partial/BLOCKED_DEPENDENCY boundary.

### Task 4: Evidence and final validation
**Files:** Create `Integration/Ui/GuidedImportVisualEvidenceTests.cs`; `output/ui/SCRUM-11170/INDEX.md`, checks, Chinese PNG/JSON; `docs/codex/printflow-operator-ux-backlog/SCRUM-11170_RESULTS.md` and maintenance notes.
**Interfaces:** Consumes delivered screens; emits original TRX, 1000x700/1280x720/1920x1080 DIP offscreen evidence with command/geometry facts.
- [ ] Capture Home, three-route choice and representative original/preparation pages; assertions enforce no binding errors, actions in viewport, conditional controls hidden. Inspect every PNG with view_image.
- [ ] Run one relevant safe UI/architecture broad suite and targeted persistence suite through E3 script. Compare failures by exact name/message with fresh baseline and E2 reference, list inherited failures without claiming fixes.
- [ ] Save AC mapping, maintenance boundaries, all rulings with cost, source diff, exact test counts, NOT RUN real keyboard/DPI/newcomer/production evidence.
- [ ] Commit concrete evidence/docs and hand HEAD to parent for one independent whole-branch review; retain this plan scratch.

## Self-review and routing
Self-review: all I01/I02 AC map to tasks 1-3; I03 integration remains blocked while unavailable-entry AC is tested. Existing authority/tests cover format sniffing, snapshot preservation and route lock. No new persistence or AI execution.
Routing post-plan: NormalRoute gpt-6-astra/high; RouteOffset=0; RequestedRoute/ExecutionTarget unchanged; ActualRoute verified by parent session 01a15273-3593-7c80-af03-4203e055fc1e turn_context 2026-10-19T04:37:27.245Z. Every immediate unit mixes WPF interaction/visuals with transient UI ownership; splitting would duplicate the same lifetime/control context. CONTINUE Native. Reevaluate at major boundary; no claimed model switch.
Authorization ruling: existing approved v2 and latest PLAN_EXECUTE/Native authorization permit execution after self-review without re-requesting approval. Cost if wrong: user may prefer revisiting the already approved arrangement; no external publication is made.

