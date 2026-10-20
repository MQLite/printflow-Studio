# Guided completion, recovery and utilities

SCRUM-11172 / PF-GUX-v2-E5 extends the existing guided WPF shell. It does not introduce another workflow, delivery model or persistence authority. The approved Chinese v2 design remains the visual source; English resources remain supported.

## Ownership and projections

`SessionViewModel.Completion.cs` exposes approval, delivery and workflow completion as three separate read-only facts. Delivery text comes from the existing `FinalSaveDisplay` interpretation; each `FinalSaveTargetRow` asks for its own exact target's interpretation. An approved workspace artifact is not a delivered file, a historical delivery record is not proof that a file currently exists, and completing a task does not manufacture a save. Approved PNG export enters the guided Save page. The existing coordinator, save identities, picker cancellation, conflict/suggestion, reconciliation and uncertainty behavior remain authoritative.

`SessionViewModel.RecoveryGuidance.cs` describes correction preparation, waiting, explicit import and required re-review. Next actions are derived from current command qualifications. Preparing files does not send them to a colleague. Reopening a task does not approve a result or restart automation. Photoshop handoff has no manual TIFF import. Existing external-state warnings, correction repair and exact result identity remain in force.

`RecentSessionRow` provides localized stopped/unknown labels and a next-action sentence. Saved history remains explicitly historical. Continue, read-only history, abandon and remove-from-list retain their separate meanings and existing commands.

## Utility visits and drafts

The baseline navigation service had no source-task return. `IUtilityTaskNavigation` adds one App-owned source-task lease in `NavigationService`; it is not a persisted session state.

1. A current session can open Settings or Environment only when its existing live interaction gates allow it and no automation or final-save operation is active.
2. Entering a utility suspends the source instance, advances its open generation and retires confirmations. Its old actions cannot mutate the task while it is suspended.
3. Settings → Environment retains the same lease. Only the current utility instance may apply, run checks, chain navigation or return.
4. Back reloads the exact source task through `ISessionService`. It checks the requester and source instance again after the await. A failure remains on the utility with local explanation and the exact failure code. A late response after navigation elsewhere is ignored.
5. A successful return reopens the retained source instance. Existing navigation context equality decides whether its supported draft can survive. Changed task/result context discards the draft and exposes current gates. It never restores an old approval or confirmation.
6. Home, route selection, another session or error-details navigation clears the lease.

Keep these checks at the navigation boundary and the view-model action boundary; `CanExecute` alone is insufficient for a stale callback. Optional interface usage preserves simple test navigators. Tests in `GuidedUtilityReturnTests` exercise real navigation, transient utility instances, real temporary persistence and a held/failing load seam.

## Readiness and settings

Readiness summaries consume `ReadinessEvidenceLifecycle`; they introduce no new age threshold and do not change `report.Verified`. Unchecked, blocked/not run, expired historical evidence and deferred observation are distinct. Opening or refreshing reads the report; only the explicit live-check action invokes the diagnostic seam. The restart requirement remains visible.

Settings shows language, trim margin and log retention together. The approved v2 image places retention among common settings, so it remains visible with validation. Production output root, DPI, preset and colour configuration are read-only. Local diagnostic paths are under the collapsed technical disclosure. Applying invalid settings does not write them.

## Feedback and confirmation

Action feedback adds a retained-content sentence while preserving the existing cause, legal next action and technical code. When reload fails, it explicitly says current records/files have not been rechecked. Save uncertainty, save-history failure and unavailable diagnostics remain separate facts.

Refreshing a session now retires a pending draft-leave confirmation even when the navigation context is identical; the supported matching draft remains. Existing return and abandon ownership/cancel guards continue to apply. Error details uses the guided theme and a collapsed technical explanation, with failure and diagnostic availability visible independently.

## Validation and limits

New behavior tests are `GuidedCompletionSaveTests`, `GuidedCompletionRecoveryTests`, `GuidedCompletionRecentTests`, `GuidedCompletionUtilityTests`, `GuidedUtilityReturnTests` and `GuidedCompletionFeedbackTests`. `GuidedCompletionVisualEvidenceTests` uses synthetic files/adapters, temporary SQLite and offscreen `UserControl` rendering. `PF_11172_CAPTURE_DIR` controls capture output only; it does not enable workstation automation.

Use `output/ui/SCRUM-11172/checks/run-safe-ui.ps1` for every run. It preserves the eleven inherited exclusions and excludes the entire Smoke namespace. Do not replace this with an unfiltered suite or set `PRINTFLOW` opt-ins. The evidence index and RESULTS document distinguish final checks, inherited failures, fixture errors and tests not run.

Real keyboard, UIA devices, DPI scaling, external applications, workstation readiness, Chinese novice walkthrough and production output require separate validation. Offscreen screenshots are 96 DPI and do not establish those results. Existing F6/N1–N4 and earlier intermittent activation failures are not closed by this work.
