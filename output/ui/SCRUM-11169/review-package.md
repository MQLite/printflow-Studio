# SCRUM-11169 whole-branch review package

Review baseline: `7fd87f6ef7ac349633af19aa04355def00c79b41` (accepted E1). Final review target is the HEAD reported by the controlling chat after Task5 evidence is committed. Implementation commits: `1665381` G01, `a203805` G02, `dd1ee8e` G03, `c49d3a5` G04, then Task5 polish/evidence. Review the entire branch once in a fresh context; this package is not a review verdict.

Worktree: `C:/Users/admin/.codex/worktrees/scrum-11177-guided-theme/printflow-Studio`; branch `codex/scrum-11169-guided-shell`. Main checkout and untracked `output/imagegen` reference gallery are outside this change. No push, merge, deployment, production application, real adapters, uploads or later Epic work is authorized.

## Start here

- `docs/codex/printflow-operator-ux-backlog/SCRUM-11169_RESULTS.md`: AC/code/test map, exact test scope/counts, rulings, anomalies and NOT RUN boundaries.
- `docs/printflow/guided-session-navigation.md`: page projection, history, return capture, draft lifetime and input/async contracts.
- `docs/superpowers/plans/2026-10-19-scrum-11169-guided-shell.md`: approved task plan and verification correction.
- `source.diff`: baseline-to-final source/test patch; `source-hashes.json` pins 31 source/test file contents. Use Git for authoritative final diff.
- `evidence-index.json`: 55 page images, 20 inspected by executor. `renders/` has 39 states/sizes and JSON measurements; `geometry/` retains old/new/without-guidance rectangles and 16 supplementary review images.
- `action-identity-comparison.json`: all 53 original action controls retain their command, parameter, exact identity, gesture guard, automation ID and default/cancel settings, each present once.

## Risk-focused review

1. Pure page projection must preserve original route legality, required review and explicit white-ink selection; no UI step navigation may confer command authority.
2. Immutable history facts and separately loaded previews must never become executable active results. Captured return intent must not retarget or survive a changed task/result.
3. Five draft categories must preserve temporary input on keep/refresh and discard only UI state; confirmed dimensions, approvals, output and save history remain persisted business facts.
4. General gesture/UIA scope is independent of exact approval identity. Check hidden/unloaded/rebound trees, retained and queued UIA providers, key release/repeat and double-click boundaries. Async success/failure/runtime/finally from A must not overwrite B.
5. Layout changes must keep fixed legal actions, stop/takeover, current mandatory choices and long Chinese guidance visible/reachable at 1000×700. Geometry fixture changes preserve safety/identity assertions; review their meaning, not just passing totals.

## Verification boundaries that must remain visible

- Final audited UI/Architecture subset: **1314 PASS / 14 FAIL / 0 SKIP, 1328 total**. All 14 names and error messages match E1 exactly (`checks/baseline-comparison.json`). This excludes 11 visible/opt-in methods; it is not a full-suite PASS.
- Audited 16-class workflow/persistence subset: **277 PASS / 0 FAIL / 0 SKIP**. Final layout/guidance/save contract subset: **145 PASS / 0 FAIL / 0 SKIP**.
- The first broad run mistakenly executed three existing unconditional `Window.Show` tests. Their finally blocks closed them and the testhost exited. Preserve raw results and `checks/visible-window-audit.md`; no further visible execution is authorized. Seven opt-in guard-return `Passed` outcomes are not live verification.
- `PrintDimensionsPreflightUiTests.Keyboard_traverses_the_bound_custom_size_controls_and_panel_does_not_trap_focus` failed in that initial run (`confirm.IsKeyboardFocused`: expected true, actual false). Its source/assertions are unchanged; exclusion leaves it **UNVERIFIED**, not fixed or passed.
- `GuidedSessionInputTests.Held_key_release_and_double_click_do_not_activate_new_context(key: Space)` once failed only the fresh-activation count (expected 1, actual 0), after stale-key rejection passed. Later passes do not establish a fix. Modifier/focus/scheduling causes remain hypotheses.
- Human Chinese/English walkthrough, actual keyboard/mouse, real UIA client, multiple DPI/monitors, real Photoshop/Meitu/production TIFF/customer files remain NOT RUN. Offscreen rendering cannot certify those behaviors.

## Reproduction within the allowed boundary

Run `output/ui/SCRUM-11169/checks/run-safe-ui.ps1` for the audited UI/Architecture filter. Do not substitute the old unqualified filter. Run `run-workflow-persistence.ps1` for the audited 16-class persistence subset. `verify-evidence.ps1`, `compare-baseline.ps1` and `index-runs.ps1` regenerate integrity/comparison/index records. Original failed/aborted runs are retained.

After review, the controlling chat owns disposition and Jira updates. No executor-created review agent, Jira closure, push or merge has occurred.
