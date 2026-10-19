# SCRUM-11169 Guided Session Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Native execution is already selected; one fresh whole-branch review is coordinated by the parent after all tasks.

**Goal:** Present the persisted workflow as a focused current step with fixed legal actions, safe history/return navigation and fresh input boundaries.

**Architecture:** Keep SessionView/WorkflowCatalog/AvailableCommands as business authority. Add an App-only presentation projection, readonly history DTOs built from already-loaded revisions, and transient navigation/input contexts. Reorganize the existing real SessionScreenView and retain its preview, crop, review, save and correction components and exact approval contracts.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm, xUnit/Shouldly, real temp SQLite/filesystem harness, headless WPF rendering.

**Spec:** `output/imagegen/SCRUM-11168/2026-10-19-v2/design-spec.html` (approved written v2); exact E2 ACs in `docs/codex/printflow-operator-ux-backlog/SCRUM-11169_JIRA_SCOPE.json`.

## Global Constraints

- Scope SCRUM-11169 only: G01/11178, G02/11179, G03/11180, G04/11181. E3–E6 and business-engine redesign remain outside scope.
- Preserve all three routes; PrepareAsset has no print/white-ink/TIFF forms; GeneratePrintTiff has no Meitu/default trim; both TIFF routes require explicit 0/1/2px choice and reason, with no default.
- Preserve original/InputSnapshot, 300 PPI/CMYK/W1, exact approval identity, downstream invalidation, environment gates, automation lock and retention/privacy contracts.
- No database schema or draft persistence; keep/cancel means continue editing; discard concerns the current transient page only.
- No Application startup, Window.Show, workstation opt-in, native desktop input, external applications, customer files, uploads, paid services, merge/push/deploy or Jira comments/closures.
- Local PNG inspection is permitted; do not bypass the prior file-browser rejection with a server/browser.
- Use linked worktree `C:/Users/admin/.codex/worktrees/scrum-11177-guided-theme/printflow-Studio`, branch `codex/scrum-11169-guided-shell`, base `7fd87f6ef7ac349633af19aa04355def00c79b41`. Preserve main checkout and untracked reference gallery.
- Runtime confirmed by parent from executor turn_context: `gpt-6-astra/high`, timestamp `2026-10-19T01:57:26.572Z`, session `01a151e0-b939-7122-9a42-544a9aeabb73`. Normal/Requested/ExecutionTarget= Astra High; RouteOffset=0; ActualRoute verified; AdjustmentResult=UNCHANGED. Post-plan re-evaluation: the remaining UI/projection/interaction unit remains tightly coupled; no separable backend implementation is introduced.
- Direct user PLAN_EXECUTE authorization and retained Native method supersede repeated generic spec/plan/method gates. Written v2 design and E1 theme already approved. No new scope permission inferred.
- E1 broad baseline: `scrum-11177-ui-fix.trx`, 1269 PASS/14 FAIL/1 SKIP; af04095→7fd87f6 has no src/tests changes. E2 fresh targeted baseline: 49 PASS/0 FAIL, `output/ui/SCRUM-11169/checks/e2-targeted-baseline.trx`.

## Review Focus

1. A held key, double click, or retained UIA provider must not operate a newly shown step/task; test physical input and provider capture against changed presentation identity in Task 4.
2. A delayed service failure/success or runtime callback from task A must not replace task B; use a controlled adapter/deferred service harness and real persisted sessions in Task 4.
3. A history view or changed return selection must not make a prior result current or retarget an open confirmation; assert persisted state/lineage/approval before and after cancel/confirm in Task 3.
4. Dirty dimensions, white-ink selection, correction, crop and save input must not silently become confirmed data or disappear on attempted navigation; exercise preserve/discard and same-context language refresh in Task 3.
5. Minimum viewport plus long Chinese warnings must retain a visible fixed main action and reachable stop/takeover; render at 1000×700, 1280×720 and 1920×1080 DIP in Tasks 1 and 5.

## File responsibilities and existing controls

- `src/PrintFlow.App/Views/SessionScreenView.xaml`: navigation/current workspace/fixed actionbar layout; retain existing AutomationIds, ICommand objects/parameters, ReviewApprovalButton identities, crop/preview names and all critical notices.
- `SessionScreenView.xaml.cs`: neutral focus, visible/hidden/unloaded interaction lifecycle; existing crop geometry remains unchanged.
- `src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs` (new): catalog/state-to-presentation only, current page/step identity, sidebar presentation; no workflow transitions or derived qualifications.
- `SessionViewModel.Navigation.cs` (new): history selection, captured return intent, transient leave intent and draft preservation. Existing `SessionViewModel.cs` delegates navigation and guards asynchronous adoption.
- `src/PrintFlow.Workflow/Services/SessionHistoryView.cs` (new) and `SessionView.cs`: immutable readonly history of already-loaded step/revision facts. No new service method, IO, mutation, engine logic or schema.
- `src/PrintFlow.App/Views/SessionActionButton.cs` and `SessionActionScope.cs` (new), plus `ReviewApprovalButton.cs`: general context gesture/UIA guard, separate from existing exact approval authority; retain `IsGestureGuarded` review semantics.
- `src/PrintFlow.App/Resources/Strings.resx`, `Strings.zh-CN.resx`, generated `Strings.Designer.cs`: English/Chinese user-facing labels.
- New test suites `GuidedSessionShellTests`, `GuidedSessionProjectionTests`, `GuidedSessionNavigationTests`, `GuidedSessionInputTests`, `GuidedSessionVisualEvidenceTests` under `tests/PrintFlow.Tests/Integration/Ui/`; test fixtures only where a real external operation needs controlled delay.
- Shared E1 GuidedTheme tokens/components reused. Old table geometry is intentionally replaced by left navigation; existing safety/identity assertions are preserved. Any old layout assertions needing new geometry are individually recorded.

### Task 1: G01 — Focused shell and fixed actions

**Files:** modify SessionScreenView.xaml/.xaml.cs; add SessionViewModel.Guided.cs; modify SessionViewModel.OperatorStatus.cs; resources; create GuidedSessionShellTests.cs.

**Interfaces:** consumes current SessionViewModel properties, RecommendedCommand and exact action identities. Produces `GuidedCurrentTitle`, `GuidedCurrentStatus`, `GuidedSteps` and fixed `Session.ActionBar` / `Session.CurrentWorkspace` surfaces. Existing controls keep their commands and identities; no generic approval proxy.

- [ ] Write failing real WPF tests `Navigation_is_left_of_current_workspace_and_primary_actions_remain_fixed`, `Current_input_actions_are_in_the_fixed_bar`, `Technical_metadata_is_disclosed_without_hiding_failure_or_stop`. Assert action rectangles inside 1000×700 and outside workspace scrolling, no duplicate automation IDs for moved controls, existing approval identity binding intact.
- [ ] Run `dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --filter FullyQualifiedName~GuidedSessionShellTests --results-directory output/ui/SCRUM-11169/checks --logger trx` and observe missing layout failure.
- [ ] Reorganize shell into left steps, current workspace and fixed bottom actions; reuse shared theme. Move primary input/review/save actions to fixed controls with their existing command/visibility/identity contracts. Put metadata and low-frequency return details behind explicit disclosures; keep critical warnings and stop/takeover visible. Extend styling recommendation only for existing legal current-form submissions.
- [ ] Run the shell suite and relevant SessionAccessibility/OperatorStatus/FinalSave tests; record any intentional geometry contract update separately.
- [ ] Commit `feat: add guided session shell with fixed legal actions`.

### Task 2: G02 — State projection and current-only content

**Files:** SessionViewModel.Guided.cs; SessionScreenView.xaml; resources; GuidedSessionProjectionTests.cs.

**Interfaces:** consumes persisted `SessionView.Steps`, `CurrentStep`, `AvailableCommands`, source format, Sizing and white-ink state. Produces `GuidedPage` enum (source/ready/processing/review/dimensions/white-ink/preflight/crop/correction/failure/save/completed), `GuidedSteps` display states and `ShowsGuided*` booleans. Projection only narrows visibility; services retain command authority.

- [ ] Write failing tests across three actual service routes: import→confirm→optional process/skip→required review→trim→dimensions→white ink→TIFF review→save/another size. Assert literal step sequences, pending steps non-executable, current/finished/skipped/attention states, no white ink before current print preparation, no print content on PNG route, direct TIFF has no Meitu/trim; use source-preparation fixtures for PDF/PSD.
- [ ] Run `dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --filter FullyQualifiedName~GuidedSessionProjectionTests --results-directory output/ui/SCRUM-11169/checks --logger trx` and observe missing projection/current-only failure.
- [ ] Implement pure presentation mapping from the existing read model/catalog and display state. Gate forms by current page while retaining existing legality checks. Do not infer new command eligibility or run transitions through navigation.
- [ ] Run projection suite plus `SessionControlsTests`, source-preparation/white-ink and completion tests. Future/hidden actions must be absent from normal UIA traversal; direct stale invocation is additionally guarded in Task 4.
- [ ] Commit `feat: project guided steps from persisted workflow state`.

### Task 3: G03 — Readonly history, captured return and draft leave

**Files:** SessionHistoryView.cs; SessionView.cs; SessionViewModel.Navigation.cs and existing navigation methods; SessionScreenView.xaml; resources; GuidedSessionNavigationTests.cs.

**Interfaces:** `SessionHistoryView` contains immutable step/revision facts only. `ViewHistoryCommand(SessionStepRow)` selects a history snapshot, `CloseHistoryCommand` restores live view without service calls. `BeginReturn` captures current task/step/result/target and computed display impact; `ConfirmReturn` accepts only that still-current capture and calls existing ReturnToStep. `KeepDraftCommand` cancels leave; `DiscardDraftAndLeaveCommand` discards page draft then resumes captured Home/history/return intent.

- [ ] Write failing tests `History_does_not_change_persisted_state_or_approval`, `Return_cancel_has_no_mutation`, `Return_confirmation_cannot_be_retargeted_or_reused`, `Confirmed_return_invalidates_downstream_with_existing_lineage_rules`, and parameterized dirty-page navigation tests (dimensions/white ink/save/correction/crop). Assert actual repo state/revisions/reviews unchanged on view/cancel/preserve, original draft unchanged on keep, discard resets only UI state, no draft persistence.
- [ ] Run `dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --filter FullyQualifiedName~GuidedSessionNavigationTests --results-directory output/ui/SCRUM-11169/checks --logger trx` and observe missing safeguards.
- [ ] Add readonly projection from already-loaded revisions; history never receives an executable SessionView. Implement identity-bound return confirmation and explicit impact text listing target/downstream steps, dimensions, outputs and approvals that must be redone. Implement transient draft leave state; dismiss pending intent on changed task/result; keep means continue editing.
- [ ] Run navigation suite plus `ReturnAndTrimControlsUiTests`, `ReturnToStepTests`, correction/crop/save regressions.
- [ ] Commit `feat: add safe guided history and draft navigation`.

### Task 4: G04 — Recovery positioning and stale-input protection

**Files:** SessionActionButton.cs; SessionActionScope.cs; ReviewApprovalButton.cs; SessionScreenView.xaml/.xaml.cs; SessionViewModel.cs/.Guided.cs/.Navigation.cs and affected partials; GuidedSessionInputTests.cs.

**Interfaces:** presentation identity includes session, current step/state/result and transient page. General input guards capture context at gesture start and UIA pattern acquisition; exact ReviewApprovalButton target identity remains independent. Service-call adoption captures an open-generation and session identity; stale results/refresh/runtime callbacks are ignored. Recovery continues to consume service-loaded state and never launches work.

- [ ] Write failing tests for hidden/unloaded action invocation, stale UIA invoke provider after step/task change, Enter/Space repeat/release and double-click across contexts, duplicate outstanding commands, delayed success/failure arriving after Open(B), and recovery of input/review/interrupted states with zero adapter starts.
- [ ] Run `dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --filter FullyQualifiedName~GuidedSessionInputTests --results-directory output/ui/SCRUM-11169/checks --logger trx` and observe the stale-adoption/input failures.
- [ ] Implement narrowly scoped guards and neutral visible focus on context change; cancel/context-check callbacks and draft intents. Do not replace service approval validation or recover more than persisted contracts support. Keep stop/takeover independent of generic busy suppression.
- [ ] Run input suite plus existing exact-review/focus/automation/RecoverySurface and StartupRecovery tests.
- [ ] Commit `fix: bind guided input and async refresh to current session`.

### Task 5: E2 evidence, regression and handoff

**Files:** GuidedSessionVisualEvidenceTests.cs; output/ui/SCRUM-11169; docs/codex/printflow-operator-ux-backlog/SCRUM-11169_RESULTS.md and component/navigation usage.

**Interfaces:** uses real final SessionScreenView, synthetic sources and service harness. Captures local PNGs, SHA256/source hashes, DIP bounds, 96 DPI and command/focus/scroll facts; never displays a production Window.

- [ ] Add/run representative render assertions for Chinese three routes, review/processing/failure, history, return confirmation, draft leave and recovered state at 1000×700/1280×720/1920×1080.
- [ ] Inspect each selected PNG via view_image; correct genuine layout defects with targeted evidence, preserving safety assertions.
- [ ] Run `dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --filter 'FullyQualifiedName~Integration.Ui|FullyQualifiedName~Architecture' --results-directory output/ui/SCRUM-11169/checks --logger 'trx;LogFileName=e2-ui-architecture-final.trx'`; compare every nonpass by exact name/error against E1 baseline. Record broad suite truthfully even when all failures are inherited.
- [ ] Run relevant workflow/persistence navigation, approval, recovery, source, route and save regression suite; preserve workstation/production excluded tests as NOT RUN.
- [ ] Write AC/code/test map, exhaustive Rulings with cost, baseline comparison, remaining failures and NOT RUN list, usage and evidence index; save source diff and review package. Commit exact paths only, never reference gallery.
- [ ] Report HEAD/commits/evidence/counts to parent and stop before the single fresh whole-branch review.

## Plan self-review

All G01–G04 ACs map above; readonly history extension has no new mutable authority; fixed controls retain specific approval contracts; show-only page mapping is not a second engine. Plan has explicit verification and no unresolved product/persistence decision. Existing user authorization covers implementation after this concrete plan; parent receives path and decisions before execution.
