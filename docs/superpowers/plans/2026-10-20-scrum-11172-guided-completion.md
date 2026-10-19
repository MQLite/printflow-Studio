# SCRUM-11172 guided completion Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. The user selected Native: one sequential implementer and one final fresh whole-branch review arranged by the parent controller.

**Goal:** Make delivery, correction/recovery, recent tasks, readiness/settings and feedback follow the approved guided design while accurately separating approval, saved-file evidence and task completion.

**Architecture:** Extend App-only presentation projections over existing session, delivery, correction and readiness read models. Reuse command objects, exact identities, draft ownership and workflow gates. Use existing guided tokens, focused cards, disclosures and the fixed action bar; do not add another workflow or persistence model.

**Tech Stack:** C#/.NET WPF, CommunityToolkit.Mvvm, zh-CN/en RESX, xUnit/Shouldly, synthetic SQLite/files, offscreen WPF RenderTargetBitmap.

**Spec:** `docs/codex/printflow-operator-ux-backlog/SCRUM-11172_JIRA_SCOPE.json` (raw Epic and F01–F05 AC); approved `C:/Users/admin/.codex/worktrees/scrum-11177-guided-theme/printflow-Studio/output/imagegen/SCRUM-11168/2026-10-19-v2/design-spec.html`, `index.html`, `prototype-manifest.json`, `design-approval.json`; accepted E4 RESULTS/REVIEW and `docs/printflow/guided-processing-review-print.md`. The user's subsequent Native continuation authorization supersedes historical pending-implementation fields in the design record.

## Global Constraints

- Work only in `C:/Users/admin/.codex/worktrees/scrum-11172-guided-completion/printflow-Studio`, branch `codex/scrum-11172-guided-completion`, BASE `209d0b3a6442aac1f901864107b94c5e6a0cb52d`.
- App UI only; no Domain/Workflow/Infrastructure/business/persistence redesign, new automation/editor/batch/network/AI/external transfer.
- Preserve one task/image, three routes, original/InputSnapshot, exact task/step/revision/file approvals, preview and downstream invalidation, independent per-size review/save, 300 PPI/CMYK/W1, environment gate/automation locks and retention/recycle/privacy.
- Material route has no print/white/TIFF; direct TIFF has no Meitu/default crop; both TIFF routes require explicit 0/1/2 px white ink and reason, without a default.
- Existing Approved workspace paths are not delivery. Save history is not current existence. Reopening/recovery never approves or restarts external processing. No Photoshop manual TIFF import.
- Preserve zh-CN/en resources; Chinese is visual acceptance language. Keys/IDs and diagnostic codes may remain technical only within disclosures.
- No real App startup, Window.Show/ShowDialog, desktop input, real Meitu/Photoshop/customer/production files, or PRINTFLOW opt-ins. Every test run goes through the copied `output/ui/SCRUM-11172/checks/run-safe-ui.ps1`; preserve all 11 exact exclusions and entire Smoke namespace exclusion.
- Preserve inherited F6/N1–N4 and baseline failure names/messages. Do not label unrelated defects fixed. Real keyboard/UIA devices/DPI/workstation/newbie/production are NOT RUN.
- No merge/push/deploy/publish/Jira comments/later Epic. Parent owns Jira and one final review. Keep ledger and scratch; no cleanup deletion.

## Review Focus

1. A save loses its acknowledgment or conflicts with an existing filename while another fact also fails: preserve uncertainty, history warnings and exact recorded action identity; canceling folder selection changes nothing. Task 1 tests pin these projections alongside existing delivery tests.
2. A correction or takeover is reopened after interruption, or its callback arrives after another job is opened: expose only lawful current actions, preserve identity, require explicit reimport/re-review and never start external work. Task 2 tests pin this plus inherited async ownership tests.
3. A historical/unknown/internal status or interrupted job enters Recent: translated truthful text, correct continued page, read-only history, and no inference that a recorded saved file still exists. Task 3 tests pin these mappings and continuation.
4. Historical readiness evidence remains while current checks are deferred/blocked or not yet read: distinguish historical/stale/unchecked observations and preserve gate authority; settings/diagnostics navigation must preserve supported drafts without running checks. Task 4 tests pin this and collapsed validation visibility.
5. A confirmation is displayed and then the same job is refreshed or its target changes: stale confirmation must be inert, cancel harmless, coexisting blockers durable. Task 5 tests pin abandon/return/draft/feedback behavior and bounded screen geometry.

## Verification commands

`pwsh -NoProfile -File output/ui/SCRUM-11172/checks/run-safe-ui.ps1 -RunName <name> -Include '<filter>'` is the sole test entry. Expected scoped GREEN is exit 0, all selected tests passed. A meaningful RED is an assertion failure for the new behavior; compile/STA/setup errors are recorded separately and corrected before claiming RED. Broad final UI/Architecture is expected to preserve the inherited exact 14 failures (E4 pre-review baseline 1424 pass/14 fail); compare raw test names AND messages, investigate differences. Workflow/persistence accepted baseline is 277/277 with inherited exclusions.

### Task 1: F01 approval, delivery and completion

**Files:** Create `src/PrintFlow.App/ViewModels/SessionViewModel.Completion.cs`; modify `SessionViewModel.Guided.cs`, `SessionViewModel.FinalSave.cs`, `Views/SessionScreenView.xaml`, `Resources/Strings.resx`, `Strings.zh-CN.resx`; create `tests/PrintFlow.Tests/Integration/Ui/GuidedCompletionSaveTests.cs`.

**Interfaces:** Consume existing `GuidedPage`, `SaveDisplay: FinalSaveDisplay`, `DescribeFinalSave(FinalSaveTarget?)`, `FinalSaveTargets`, exact save commands/identities and `CanComplete`. Produce presentation-only `ShowsGuidedCompletion`, `GuidedCompletionHeading`, `GuidedCompletionNotice`, `GuidedApprovalState`, `GuidedDeliveryState`, `GuidedCompletionState` and per-target `DeliveryStatus`. Notification hook `NotifyGuidedCompletion()` is called by existing guided/save refresh paths.

- [x] Write tests `Approved_result_enters_save_without_claiming_delivery`, `Each_size_retains_its_own_review_and_delivery_summary`, and `Uncertain_collision_cancel_and_completed_are_distinct`. Assert approval-only performs zero deliveries; actual synthetic save changes delivery text while completion remains pending; completed-without-save remains truthful; per-size states derive independently; canceled folder keeps draft/identity; uncertainty is never success.
- [x] Run `e5-f01-red` with `FullyQualifiedName~GuidedCompletionSaveTests`; expected behavioral assertion failures for absent completion projection/layout, after compile/setup issues separately resolved.
- [x] Implement the presentation projection and focused save/completed summary; keep filename/folder editable through existing controls, existing exact save/open/conflict/cancel/retry mechanics and fixed bar. Per-size rows display both review and save facts from `DescribeFinalSave`, never from file extension/path alone.
- [x] Run `e5-f01-green` including `GuidedCompletionSaveTests|FinalSaveUiTests|GuidedSessionSaveOwnershipTests|GuidedProcessingReviewTests`. Expected GREEN; retain known baseline differences explicitly.
- [x] Commit explicit Task 1 files plus plan/safety audit/ledger evidence. Record ruling costs and actual tests.

### Task 2: F02 correction and recovery branch

**Files:** Create `src/PrintFlow.App/ViewModels/SessionViewModel.RecoveryGuidance.cs`; modify `SessionViewModel.Guided.cs`, `SessionViewModel.Correction.cs`, `Views/SessionScreenView.xaml`, both RESX files; create `tests/PrintFlow.Tests/Integration/Ui/GuidedCompletionRecoveryTests.cs`.

**Interfaces:** Consume `IsAskingColleague`, `ShowsCorrectionPanel`, `CorrectionActivity`, `CorrectionHandoff`, `CorrectionReturn`, `IsHandedOff`, `IsStoppedStep`, current command eligibility and existing import/reentry identities. Produce `ShowsGuidedRecovery`, `GuidedRecoveryHeading`, `GuidedRecoveryPreserved`, `GuidedRecoveryNext`, `GuidedCorrectionStages` presentation text and `NotifyGuidedRecovery()`.

- [x] Write tests `Correction_prepare_wait_import_and_review_remain_explicit`, `Photoshop_takeover_has_no_manual_tiff_import`, and `Stopped_restored_page_only_names_executable_actions`. Assert existing fake flow identities, zero implicit external starts, no approval on reimport/restore, and exactly lawful retry/reentry/manual actions.
- [x] Run `e5-f02-red` with `FullyQualifiedName~GuidedCompletionRecoveryTests`; expected missing branch guidance/layout assertion failures.
- [x] Make correction/recovery a focused branch with preparation/handoff/wait/reimport/re-review explanation, preserved-input notice and qualified next action. Reuse existing buttons and keep repair/missing files/unfinished import facts visible. Reentry prepares a new attempt without starting it.
- [x] Run `e5-f02-green` including `GuidedCompletionRecoveryTests|ColleagueCorrection|RecoverySurface|GuidedImportResume|GuidedSessionProjection`. Expected GREEN.
- [x] Commit explicit Task 2 files and ledger evidence.

### Task 3: F03 recent tasks and readonly history

**Files:** Modify `src/PrintFlow.App/ViewModels/RecentSessionRow.cs`, `Resources/DisplayNames.cs` only for scoped missing/unknown display mappings, `Views/HomeView.xaml`, both RESX files; create `tests/PrintFlow.Tests/Integration/Ui/GuidedCompletionRecentTests.cs`.

**Interfaces:** Consume immutable `SessionListItem`/command qualifications and existing Home resume/remove/abandon commands. Produce presentation `GuidedStateLabel`, `GuidedNextAction`/equivalent localized row guidance; leave stored enums and read-only history authority unchanged. No new status persistence.

- [x] Write tests `Recent_interrupted_and_unknown_states_use_operator_language`, `Recent_saved_history_does_not_claim_present_files`, `Recent_resume_projects_real_guided_page_and_history_is_read_only`. Assert interrupted→stopped, unknown values→neutral localized fallback in scoped surfaces, saved-history qualification, remove label meaning, and no approval/external-run side effects on continue/history.
- [x] Run `e5-f03-red` with `FullyQualifiedName~GuidedCompletionRecentTests`; expected assertion failures for missing localized/guided states.
- [x] Add legible recent status/action hierarchy and translated fallback; keep separate saved-history and wait-for-correction facts. Document bounded N1 presentation coverage and remaining legacy defects.
- [x] Run `e5-f03-green` including `GuidedCompletionRecentTests|RecentProcessing|RecentPending|HomeRecentAccessibility|GuidedImportHome|GuidedImportResume`. Expected GREEN.
- [x] Commit explicit Task 3 files and ledger evidence.

### Task 4: F04 readiness and settings hierarchy

**Files:** Modify `src/PrintFlow.App/ViewModels/EnvironmentReadinessViewModel.cs`, `SettingsViewModel.cs`, `Views/EnvironmentReadinessView.xaml`, `SettingsView.xaml`, both RESX files; create `tests/PrintFlow.Tests/Integration/Ui/GuidedCompletionUtilityTests.cs`.

**Interfaces:** Consume `EnvironmentReadinessReport`, per-check `Lifecycle`, `Verified`, `Blocked`, existing read/live-check commands, settings validation and navigation leases. Produce read-only `ObservationSummary`, per-row `EvidenceSummary`, settings `AdvancedHeading` and disclosure state as needed. No new age threshold or permission; historic evidence wording derives only from existing lifecycle facts.

- [ ] Write tests `Readiness_unchecked_blocked_stale_and_checked_are_distinct`, `Opening_utilities_does_not_run_live_checks_or_lose_source_draft`, `Settings_advanced_disclosure_preserves_validation_and_readonly_production`. Assert old last-success plus deferred observation cannot say currently passed; no-report neutral state; common inputs visible, technical facts collapsed; invalid advanced retention remains visible/reachable and prevents apply.
- [ ] Run `e5-f04-red` with `FullyQualifiedName~GuidedCompletionUtilityTests`; expected presentation assertion failures.
- [ ] Organize common settings and advanced technical disclosure, retain visible production boundary/validation and explicit diagnostic controls. Add readiness summary/per-check evidence qualifiers while retaining current gate output, all blockers and explicit check semantics.
- [ ] Run `e5-f04-green` including `GuidedCompletionUtilityTests|EnvironmentReadinessScreen|ReadinessRowPresentation|SettingsAndLocalisation|HomeReadiness|SettingsAuthority`. Expected GREEN except exact inherited baseline failures if any.
- [ ] Commit explicit Task 4 files and ledger evidence.

### Task 5: F05 feedback, confirmations and final evidence

**Files:** Modify scoped `SessionViewModel.FailureNotice.cs`, `HomeViewModel.cs`/confirmation partial if a demonstrated ownership gap requires it, `Views/SessionScreenView.xaml`, `HomeView.xaml`, `ErrorDetailsView.xaml`, `NoticeErrorDetails.xaml`, both RESX files; create `tests/PrintFlow.Tests/Integration/Ui/GuidedCompletionFeedbackTests.cs`, `GuidedCompletionVisualEvidenceTests.cs`. Create `docs/printflow/guided-completion-recovery-utilities.md`, `docs/codex/printflow-operator-ux-backlog/SCRUM-11172_RESULTS.md`, `output/ui/SCRUM-11172/INDEX.md`, hash/geometry manifest and review package.

**Interfaces:** Consume current job/result/navigation/confirmation identity and independent existing notice/save/diagnostic facts. Produce consistent local feedback using the existing guided theme; no toast-only blockers, new command qualification or diagnostics side effect. Visual fixtures use detached snapshots and existing WpfRendering helpers.

- [ ] Write tests `Failure_keeps_reason_preserved_content_and_legal_next_action_visible`, `Refreshed_abandon_return_and_draft_confirmations_are_inert`, `Uncertain_save_and_diagnostic_failure_remain_separate`. Assert unchanged persisted state on cancel/stale confirmation and both facts rendered concurrently. Audit existing identity guards before choosing the smallest UI fix.
- [ ] Run `e5-f05-red` with `FullyQualifiedName~GuidedCompletionFeedbackTests`; expected presentation/ownership assertion failure. Record existing behavior that already passes without pretending it was a RED.
- [ ] Apply only demonstrated scoped changes. Align inline loading/empty/no-preview/validation/failure/success and technical disclosures. Keep retained content statements truthful when reload fails.
- [ ] Run `e5-f05-green` with new feedback tests and existing confirmation/failure/error-details tests via safe script. Expected GREEN or exact inherited baseline failures documented.
- [ ] Capture Chinese representative save/completed/correction/recovery/recent/settings/readiness pages at 1000×700, 1280×720, 1920×1080 DIP, plus 1000×700 uncertainty/conflict/failure/blocked/confirmation states, using synthetic fixtures only. Inspect every final PNG with view_image; write geometry/hash/index.
- [ ] Run `e5-ui-architecture-final` default safe broad and `e5-workflow-persistence-final` using exactly accepted workflow/persistence selection. Compare raw failure names/messages to accepted E4 baseline; investigate every unexpected difference. Compile/STA glitches remain separate evidence.
- [ ] Write maintenance/results/review package with AC mapping, commands/counts/raw logs/TRX, all rulings and costs, inherited issues and NOT RUN limits; commit explicit paths. Parent dispatches one fresh whole-branch review only after final HEAD is complete. Keep same executor for one Important/Critical fix pass with RED→GREEN and affected/full safe verification; no second review or cleanup.
