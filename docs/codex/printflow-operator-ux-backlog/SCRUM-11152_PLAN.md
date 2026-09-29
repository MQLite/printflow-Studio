# SCRUM-11152 — Home readiness summary: checklist, source-to-display matrix and AC map

Task `PF-OPUX-v1-SCRUM-11152-impl-v1`, Planning-ID `PF-OPUX-v1-home-readiness-summary`, 2026-09-30 NZ. Executes `printflow-remediation-prompts/PrintFlowStudio_SCRUM11152_Home_Readiness_Implementation_Prompt_v1.md` (operator-owned, untracked). The owner approved its bounded interaction brief and authorised implementation, verification, one independent review with fixes, direct `master` publication, SCRUM-11152 Jira updates only and the owner-review packet. The shipped wording is listed for owner review in [SCRUM-11152_COPY_REVIEW.md](SCRUM-11152_COPY_REVIEW.md).

## Baseline

- `master` = `origin/master` = `d017fdba365bd3425585688acd09b6b179d380d2` (fetch and `ls-remote` agree); code checkpoint `924281d` is its parent. Local changes before starting: the three 11149/11150/11151 audit residues only. They stay unstaged and byte-for-byte unchanged.
- Jira pre-read: SCRUM-11152 (id 10882) To Do, 0 comments, parent SCRUM-11139, blocks SCRUM-11154/11155. The five AC match the prompt word for word. Transitions discovered live: To Do 11, In Progress 21, In Review 31, Done 41. Moved To Do → In Progress (21) at implementation start; the response confirmed In Progress.
- Hooks/CI: no `.github/`, no active git hooks, no `core.hooksPath`. A `master` push triggers no deployment or migration.

## Route

Claude adaptation v1.2 of policy v2.4, `route_offset: 0` (explicit, valid; UNCHANGED). Opus/High for all work; no Sonnet unit. Host model `claude-opus-5-5`, effort UNVERIFIED. Context `CONTINUE`; one fresh read-only `personal-dev-reviewer` (definition `model: opus`, runtime UNVERIFIED) at the review boundary.

## What the source says (audit)

- **One authority, two operations.** `IEnvironmentDiagnostics` is implemented only by `VerifiedEnvironmentGate` (architecture test). `Read()` is passive: automatic checks, then a re-observation of the retained live evidence. `RunLiveChecksAsync` is the explicit live check. Both return an immutable `EnvironmentReadinessReport` with `Verified`, `ObservedAt`, ordered `Checks`, `BlockingFailures` (blocking Failed/Blocked, in evaluation order) and `Advisories`.
- **Live evidence is in memory.** `ProductionWorkstationVerifier` keeps `_liveEvidence` and `_lastSuccessfulLiveAt` in the singleton, per process. Nothing about readiness is persisted, so a new run cannot have a current pass until something observes again. A passive read without retained live evidence reports the live rows as Blocked (not run), so `Verified` is false.
- **Before this change** only `EnvironmentReadinessViewModel` (Refresh/Open → `Read`, explicit button → `RunLiveChecksAsync`) and `SettingsViewModel` (open → `Read`) read the seam in the shell, and each kept its report privately. Home only navigated. Screens are transient; `NavigationService` builds a fresh Home on every visit. Production Readiness disables Back while a reading runs.
- **Home's old top lines**: `StartupSummary` (recovery counts, or the pending-recovery count, plus the retention warning) and `PresetStatus` (startup's preset check: "Preset verified").

## Decisions

1. **An app-lifetime in-memory holder**, `ReadinessObservationAccessor` (registered singleton, `PrintFlow.App.Startup`, like `StartupStatusAccessor`). The two screens that already read the seam record each reading in it: `Begin` → ticket, `Complete(ticket, report)`, `Abandon(ticket)` on a throw or cancellation. It stores observation facts, never permission; no gate, workflow or adapter reads it. It starts empty in every process.
2. **Only the latest observation counts.** A result or an unfinished end is recorded only while its ticket is the latest. A started observation hides the previous report. So an older pass cannot come back on screen: not during a newer check, not after a failed or cancelled one, not through a slow earlier completion.
3. **Settings is included** because its reading is the same `Read()` of the same authority. Leaving it out would let Home keep an older readiness-screen pass while a newer Settings reading had failed.
4. **Home takes a snapshot** of the holder when it is built and on each ordinary Refresh (memory only). There is no event subscription, so no duplicate subscriptions and nothing reaches a superseded view. This matches the existing transient-screen pattern.
5. **The summary restates no rule**: `HomeReadinessSummary` maps the report's own `Verified` and first `BlockingFailures` entry. The reason text is `EnvironmentCheckRow`'s `Name` and `Explanation`, the same wording Production Readiness uses, rebuilt on every read so a language change rewords all of it. One exception (review F1): when that first entry is a live check that was not run (the report's own `Blocked` + `LiveApplication` flags), the readiness screen's "a prerequisite has not passed" would be false, because such a row can only come first when nothing before it failed. Home then says the Meitu and Photoshop check has no current result and names the screen's own "Safe recovery and recheck" button.
6. **The button** is the existing `ShowEnvironmentCommand` (`Home.ShowEnvironment`, label "Production readiness" / "生产就绪状态"), moved into the summary. It only navigates. The destination's existing open behaviour (one passive reading) is unchanged; its live check still needs its own button.
7. **Startup details**: the counts line moves under a collapsed "Startup details / 启动详情" expander, with the pending-recovery count and the preset line. Three warnings stay visible: preset not verified, startup recovery did not run, and the diagnostic retention warning. The Recovery list, its actions, correction navigation and the 11151 notice + Error details are untouched. `StartupSummary` is unchanged for the startup tests that read it; `PresetStatus` (no longer bound) is removed.
8. **Preset wording** now names what it is: "Production setup file (preset): verified at startup. This alone does not mean the workstation is ready."
9. **A cancelled live check is unfinished** (review F3). The real gate returns a report whose running step is Failed with code Cancelled. The readiness screen shows it; Home records the observation as unfinished instead of presenting that row as a fault.
10. **Ready is worded as past** (review F2, option b): "Ready to process when last checked", and the hint says per-step checks do not update Home.
11. **The upper part scrolls on its own when it must** (review F4). Everything between the title and the lists sits in `Home.Upper`, capped by the view at 65% of the height. With every disclosure open at 1000×700 the lists had shrunk to 1px; now they keep room to scroll to their last action. In ordinary states nothing moves.

## Source-to-display matrix

| Home state | Authoritative source | App-run applicability | Freshness / invalidation source | Time shown | First reason | Next action |
|---|---|---|---|---|---|---|
| Not checked yet | `ReadinessObservationAccessor.Current.State == NotObserved` | New holder per process, so a previous run's pass or startup's preset check never counts | — | none | none | Button to Production readiness ("Open Production readiness to check this workstation. A result from an earlier run is not used.") |
| Checking | `State == InProgress` (a readiness-screen or Settings reading, or the explicit live check, has begun) | This run | A started ticket hides the previous report | none | none | Button to the screen |
| Ready ("when last checked") | `State == Observed` and `Report.Verified == true` | This run, latest ticket | Replaced by any later recorded ticket (begin, result, failure); **not** by the gate's re-verification (see missing signals) | `Report.ObservedAt` → local `"g"` (date and time), labelled "Checked at": the same field, label and format the readiness screen shows | none (advisories never enter) | Button; hint that this is what that check found, that per-step checks do not update Home, and to open Production readiness if a step is refused |
| Blocked | `State == Observed`, `Verified == false`, first entry of `Report.BlockingFailures` | This run, latest ticket | As above | `ObservedAt` | `EnvironmentCheckRow(first).Name` + `.Explanation`; if that entry is `Blocked` + `LiveApplication`: `Home_ReadinessLiveCheckPending`, naming "Safe recovery and recheck"; `CheckKey` in collapsed Technical details | Button; supervisor line |
| Not confirmed (unfinished) | `State == Unfinished`: the latest reading threw, or the live check was cancelled (thrown, or returned with a Cancelled row) | This run | The unfinished ticket withdrew the earlier report | none; the earlier time belongs to a result no longer shown | "The last check did not finish, so no earlier result is shown as current." | Button; check again |
| Not confirmed (no reason) | `State == Observed`, `Verified == false`, no blocking failure | This run | As above | `ObservedAt` | "The last check did not name a reason. Production readiness shows the details." (no guessed fault) | Button; check again |

**Missing signals (documented, not hidden; AC1 stays PARTIAL).** Two verifications by the same authority are not recorded:

- the gate's own re-verification before each production step, `VerifiedEnvironmentGate.AuthoriseProduction` (`VerifiedEnvironmentGate.cs` ~188–197), which returns no report to the shell;
- the passive `Read()` that `DiagnosticPackageService` (Workflow layer, ~line 68) takes when a support package is written.

So after a later production refusal Home can still show an earlier Ready, worded "when last checked" with that reading's own time, until the next readiness or Settings reading. The shell has no precise signal for a gate refusal. `FailureCode.EnvironmentNotVerified` is also produced on over a hundred adapter baseline paths (Meitu/Photoshop presets, UI driver, export rule), and gate refusals mostly land as persisted failed attempts. Closing this needs a gate refusal event or a Workflow-level observation port: a new cross-layer seam this task excludes. Production is unaffected: the gate re-asks the verifier on every production request and never consults Home or the holder.

**Owner option (not implemented).** Home could downgrade to "not confirmed" whenever a session action reports `EnvironmentNotVerified`. That could only add unnecessary caution, never a false Ready (the reviewer's counterpoint). It would touch the session screen, so it is left for an owner decision.

## Checklist

1. Holder + summary projection; record from Production Readiness (passive and live) and Settings; Home snapshot on open/refresh.
2. View: summary block (heading, status, reason, time, hint, technical details, button), visible warnings, collapsed startup details; Settings button moved to the title row.
3. en/zh-CN resources (20 new keys).
4. Behavioral tests first where practical (RED against an unwired stub), then wiring; mutations; composed-graph integration.
5. Settled build, focused/architecture/safe UI runs, synthetic renders, independent review and fixes.
6. Publication, Jira, readback/CSV/audit, HANDOFF, owner-review packet.

## Test plan (prompt §5) and where each item lives

All in `HomeReadinessSummaryTests` unless noted.

| Item | Case(s) |
|---|---|
| Fresh app/no report, incl. historical pass and preset verified | `A_new_run_is_not_checked_even_after_a_previous_run_passed_and_startup_verified_the_preset`; the real guarantee is the per-process singleton, shown by the composed-graph case below |
| Current pass with exact time; advisories do not change authority | `A_passed_reading_in_this_run_shows_ready_with_that_readings_own_time`, `Advisories_never_change_the_answer_in_either_direction` |
| Failed/blocked with multiple reasons, report's first blocker | `A_failed_reading_is_blocked_with_the_reports_first_blocker_and_never_ready` (real gate, three failures), `A_live_check_that_has_not_run_is_explained_without_claiming_any_failure` (production shape), `A_not_verified_report_that_names_no_blocker_is_not_confirmed_and_guesses_no_fault` |
| Stale/invalidated pass; failed or cancelled later observation; late earlier completion | `A_later_reading_that_fails_replaces_the_earlier_pass`, `A_later_reading_that_throws_withdraws_the_earlier_pass`, `A_running_then_cancelled_live_check_never_shows_the_earlier_pass_as_current` (returned-cancelled and thrown shapes), `A_slow_earlier_pass_completing_after_a_newer_failure_does_not_come_back`, `Only_the_latest_ticket_is_recorded_and_a_started_observation_hides_the_previous_report`, `A_settings_reading_is_recorded_like_any_other_observation` |
| Same-run navigation vs new run | `Reopening_home_in_the_same_run_keeps_the_observation_and_a_new_run_starts_unchecked`; composed graph: `EnvironmentGateCompositionTests.Home_shows_the_composed_readiness_reading_and_a_new_graph_starts_unchecked` |
| No check/repair/processor/write side effects from rendering, refresh, locale, details, navigation | `Home_rendering_refresh_language_details_and_navigation_observe_nothing_and_run_nothing` (Read/Live counts unchanged, holder unchanged, Meitu 0, no diagnostics dependency) |
| Navigation invokes the destination once; live-check count zero | `The_readiness_button_opens_the_existing_screen_once_and_the_screen_runs_no_live_check` |
| Interrupted jobs, zero/non-zero counts, startup warning, Home error notice with the summary | `The_recovery_list_counts_warnings_and_a_failure_notice_coexist_with_the_summary`, `Recovery_counts_read_zero_when_startup_recovered_nothing_and_warn_when_it_did_not_run` |
| Import/Resume/recovery eligibility and import unchanged | `The_readiness_answer_changes_no_import_resume_or_recovery_eligibility`; correction-import navigation: existing `ColleagueCorrectionTests`/`ColleagueCorrectionUiTests` (constructor-only edits) |
| Bilingual parity; rewording without a new observation | `Every_state_is_worded_from_resources_in_both_languages` (exact satellite, no fallback), `A_language_change_rewords_the_same_observation_and_keeps_its_time` (incl. the check's own name and explanation); global parity: `LocalisationResourceTests` |
| Report-to-Home integration | Real `EnvironmentReadinessViewModel` over the real `VerifiedEnvironmentGate` + `WorkstationVerificationFixture`; composed-graph case above |
| Layout/keyboard (AC5) | `The_summary_wraps_its_button_is_keyboard_reachable_and_recovery_stays_reachable` × en/zh-CN × 1000×700/1920×1040 (button in view, focusable, tab stop, not default; no clipped summary text; last recovery action and Choose file reachable); `With_everything_expanded_the_notice_and_the_last_recovery_action_stay_reachable` × en/zh-CN at 1000×700 |

Opt-in only: `Captures_the_representative_states_when_asked` writes PNGs when `PF_SCRUM11152_CAPTURE_DIR` is set; without it the case returns immediately. Its pass is not counted as evidence of anything.

## AC disposition

| AC | Disposition | Basis |
|---|---|---|
| 1 Failed → blocked, first reason, button, never Ready | **PARTIAL** | Every recorded reading (readiness screen passive and live, Settings) is shown truthfully, never Ready when failed, with a true first reason. Gate-time and support-package verifications are not recorded (missing signals above); mitigated, not solved, by the "when last checked" wording |
| 2 No verification this session → Not checked yet, no old pass | PASS (automated) | Per-process holder; new-run and composed-graph tests |
| 3 Passed → Ready with the check time | PASS (automated) | `ObservedAt`, same field, label and format as the readiness screen; worded "when last checked" |
| 4 Recovery list stays, counts under details | PASS (automated) | Recovery list untouched; counts in "Startup details"; warnings visible; worst-case reachability test |
| 5 Both locales, no clipping at supported resolution/scaling, keyboard-reachable button | PARTIAL | Synthetic 96-DPI renders and layout assertions at 1000×700 and 1920×1040 in both languages PASS; other scaling, real keyboard traversal and the Jira "human check" are NOT RUN |

Also open: the owner's copy review and the Jira "human check".

## Independent review

One fresh read-only reviewer, two rounds in the same context. The reviewer ran no tests.

- **Round 1: NOT ACCEPTABLE AS-IS.** F1 and F2 were P1, F3 and F4 were P2, and F5–F9 were P3.
- **Round 2:**
  - F1, F3, F4, F5 and F6 are CLOSED.
  - F2 is dispositioned under option b, and AC1 stays PARTIAL.
  - F7 and F8 are accepted residuals. The final renders close F9.
- **New P3s from round 2:**
  - N1: the English button name is unquoted. It stays as is, matching the existing English convention, and is noted for the copy review.
  - N2: the reviewer asked for a tighter bound in the reachability test. Done.
  - N3: "Choose file" reachability is now asserted and rendered.
- No blocking defect remains.

## Accepted residuals

- Home takes a snapshot of the holder when it opens and on Refresh. A reading that finishes while Home is already shown appears on the next Refresh; for example, Settings left before its read ended. This errs toward "checking", never toward Ready.
- Cancelling just after a genuine failure has already returned shows "not confirmed" rather than "blocked". This is conservative.
- Settings moved to the title row. The tab order is now Settings, then Production readiness, then Choose file.
- `Preset_Verified` / `Preset_NotVerified` are no longer shown on Home and remain in the resource files.
- In the "no current result" case, Technical details names the first live row (`ExternalApplicationAutomationLock`). That is true, but it is not a lock fault.
