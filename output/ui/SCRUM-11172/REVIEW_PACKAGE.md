# SCRUM-11172 whole-branch review package

The parent controller owns the single fresh-context review. Review the final committed HEAD against base `209d0b3a6442aac1f901864107b94c5e6a0cb52d` on branch `codex/scrum-11172-guided-completion`. This package is preparation, not a review verdict.

## Required context

- [Original Epic + F01–F05 AC](../../../docs/codex/printflow-operator-ux-backlog/SCRUM-11172_JIRA_SCOPE.json), including production and identity invariants.
- [Execution plan and Review Focus](../../../docs/superpowers/plans/2026-10-20-scrum-11172-guided-completion.md).
- [RESULTS](../../../docs/codex/printflow-operator-ux-backlog/SCRUM-11172_RESULTS.md), [RULINGS](RULINGS.md), [maintenance](../../../docs/printflow/guided-completion-recovery-utilities.md).
- [Visual index](INDEX.md), [hash manifest](visual-manifest.json), raw `checks/*.trx`, `*.log`, `*-filter.txt`.
- Approved v2 design at `C:/Users/admin/.codex/worktrees/scrum-11177-guided-theme/printflow-Studio/output/imagegen/SCRUM-11168/2026-10-19-v2/`; historical pending implementation flags are superseded by the user's later continuation authorization.
- Controller startup/routing/inherited-failure evidence in `checks/controller-*.json`, retained unchanged.

## Review focus

1. Approval, saved-file evidence and completion must remain independent. Per-size rows must derive from their own exact target. Save uncertainty, cancellation, filename collision and history unavailability must not become success or imply current file existence.
2. Correction preparation/wait/import/re-review and Meitu/Photoshop handoff must preserve lawful existing paths, exact identity, no silent restart and no Photoshop TIFF import.
3. Recent continuation/history must use actual state and never revive old approval. Neutral unknown labels must remain scoped; do not treat a culture failure's non-reproduction as a causal fix.
4. **Utility lease is new App behavior** because baseline had no source-task return. Inspect `NavigationService`, `IUtilityTaskNavigation`, `SessionViewModel.Utilities` and Settings/Readiness guards. Check source suspension, exact current requester, same-source ownership through chaining, authoritative reload before restoration, matching-draft qualification, failed/late load, and stale old actions. Ensure no old `CanExecute`/approval intent is revived.
5. **Same-context refresh now retires draft-leave confirmation.** Verify it keeps supported draft values, clears the old callback and leaves persisted task state unchanged. Check existing abandon/return/automatic selection/takeover identities and cancel behavior.
6. Feedback must qualify retained content when reload fails. Technical disclosure must not hide diagnostic availability, failure code or key operating constraints.
7. Check the legacy Settings test modification: it explicitly opens the collapsed advanced panel and retains the original assertions. No test/exclusion was weakened. Review fixture-only failed runs separately from valid behavioral RED.

## Safety and result boundaries

No Domain/Workflow/Infrastructure implementation changed. Test fixtures use synthetic temporary data, fake adapters and offscreen WPF; never start the real App, show windows, drive desktop input, set PRINTFLOW opt-ins or run unfiltered tests. Use the inherited safe script and exact exclusions.

Inherited F6/N1–N4, the earlier unresolved activation failures and raw broad-suite failure differences remain visible in RESULTS. Synthetic tiny TIFF dimensions are not production-size evidence. Real keyboard/UIA/DPI/workstation/novice/production acceptance is NOT RUN.

If Important/Critical findings arise, the same Native executor performs one correction pass with meaningful reproduction and affected verification. The parent does not dispatch per-subitem or second independent review. Do not merge, push, deploy, publish, write Jira comments, clean up scratch or start a later Epic.
