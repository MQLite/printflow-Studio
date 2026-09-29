# SCRUM-11152 signal closeout — verification results

Task `PF-OPUX-v1-SCRUM-11152-signal-closeout-v1`, 2026-09-30 NZ. Baseline master `20ece916d290e489fe64a58155800a873dde5ed0`. This record covers implementation verification; coordinator records publication/Jira/packet separately. No production-readiness or human acceptance claim.

## Candidate and scope

Candidate 02 is frozen in `candidate-02.json` and `candidate-02-files.txt`: 16 source/test files, each with SHA-256 and UTC check time. `candidate-02-verified.json` rechecks every source hash after the settled runs and adds App, Infrastructure, Workflow and Tests DLL hashes/build times. No product change followed the clean `--no-incremental` build; candidate 02 adds only the exact new observation-port allowance to a second existing gate constructor assertion. `candidate-01.json` and failed attempts are retained.

Product changes: Workflow write-only observation port; sole gate publication of existing passive/live/production observations; current-run holder notifications and exact-snapshot prequeue cancellation; page duplicate recording removal; singleton DI; loaded Home dispatcher/lifetime updates; authorized bilingual copy and Home-only cleanup/navigation resources. No verifier, gate decision, processor, lock, retry, persistence, workflow eligibility or recovery operation changes. Internal-work verification omits automation availability, so its success cannot restore full Ready; its real failure remains informative.

## Runs (counts overlap; do not add them)

| Run | Outcome | Evidence |
|---|---|---|
| Baseline RED build | PASS, 0 warnings/errors | `build-red.log` |
| Behavioral RED before wiring | 4 failed / 4: stale reopened/current Home; en/zh cleanup ambiguity | `red.log`, `red.trx` |
| Package RED build | PASS, 0 warnings/errors | `build-package-red.log` |
| Actual package observation behavioral RED | 1 failed / 1 | `package-red.log`, `package-red.trx` |
| First green build | PASS, 0 warnings/errors | `build-green-01.log` |
| First green attempt | 4 passed / 5, one incorrect test assumption | `green-01.log`, `green-01.trx` |
| Expanded green build | PASS, 0 warnings/errors | `build-green-02.log` |
| Expanded focused green | 73 passed / 73 | `green-02.log`, `green-02.trx` |
| Settled clean build `--no-incremental` | PASS, 0 warnings/errors | `build-settled-01.log` |
| First settled focused run | 154 passed / 155, old constructor-shape assertion | `settled-focused.log`, `settled-focused.trx` |
| Candidate 02 test-only follow-up build | PASS, 0 warnings/errors | `build-settled-02.log` |
| Final focused verification/diagnostics/Home/composition | 155 passed / 155 | `settled-02-focused.log`, `.trx` |
| Final architecture | 473 passed / 473 | `settled-02-architecture.log`, `.trx` |
| Final affected safe UI/resource group | 225 passed / 225 | `settled-02-ui.log`, `.trx` |
| Final render capture/reachability | 4 passed / 4, 48 PNGs | `renders-final.log`, `.trx`, `renders-final/INDEX.md` |

`test-run-index.json` carries the raw TRX counters and exact start/finish strings. The first green package test assumed that exporting to an existing name fails; the real writer correctly chooses a unique name. The test now places an owned file at the archive staging-directory path to force a genuine write failure after a passed diagnostics read. Package behavior was not changed. The old reflection assertion now permits exactly `[IProductionWorkstationVerifier, IEnvironmentReadinessObservations]`, matching the architecture test's narrow allowance. `The_only_diagnostics_implementation_is_the_gate_itself` is unchanged and passes.

## Exact command forms

All commands ran from the repository root. Logs were redirected to this evidence folder. Test project:

```text
tests/PrintFlow.Tests/PrintFlow.Tests.csproj
```

Builds:

```text
dotnet build tests/PrintFlow.Tests/PrintFlow.Tests.csproj --no-restore -v minimal
dotnet build tests/PrintFlow.Tests/PrintFlow.Tests.csproj --no-restore --no-incremental -v minimal
```

Every test command:

```text
dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --no-build --no-restore --filter <FILTER> --logger "trx;LogFileName=<RUN>.trx" --results-directory artifacts/pf-opux-scrum11152-closeout
```

| RUN | FILTER |
|---|---|
| red | `FullyQualifiedName~HomeReadinessSignalCloseoutTests` |
| package-red | `FullyQualifiedName~Closeout_package` |
| green-01 | `FullyQualifiedName~HomeReadinessSignalCloseoutTests\|FullyQualifiedName~Closeout_package` |
| green-02 | `FullyQualifiedName~HomeReadinessSignalCloseoutTests\|FullyQualifiedName~HomeReadinessSummaryTests\|FullyQualifiedName~DiagnosticPackageTests\|FullyQualifiedName~EnvironmentReadinessScreenTests` |
| settled-focused and settled-02-focused | exact raw filter in `settled-focused-filter.txt` |
| settled-02-architecture | `FullyQualifiedName~PrintFlow.Tests.Architecture` |
| settled-02-ui | exact raw filter in `settled-ui-filter.txt` |
| renders-final | `FullyQualifiedName~HomeReadinessSummaryTests.Captures_the_representative_states_when_asked\|FullyQualifiedName~HomeReadinessSummaryTests.With_everything_expanded` |

The final render command alone sets `PF_SCRUM11152_CAPTURE_DIR` to the absolute path of `artifacts/pf-opux-scrum11152-closeout/renders-final`, then removes that temporary environment setting in finally. The ordinary capture test returns without writing when unset; those ordinary passes are not counted as rendering evidence.

The focused set contains closeout tests, original Home summary tests, actual diagnostic package tests, readiness screen tests, gate and production workstation verifier tests, and exactly three isolated composition cases: `Home_shows_the_composed_readiness_reading_and_a_new_graph_starts_unchecked`, `Exactly_one_environment_gate_is_registered_and_it_consults_the_verifier`, `Composing_the_graph_reads_no_workstation_file`.

The UI set is limited to Home readiness/input/recent accessibility, RecoverySurface, Settings/localization, correction UI, resource parity and off-screen ViewRendering. The Settings ApplicationStartup case is explicitly excluded. No unrelated broad workflow/persistence group ran.

## Deterministic behavioral coverage

- Existing readiness and Settings observations each produce one Begin/Observed sequence through the real gate. The current-run holder starts empty, independent of startup preset or historical pass.
- Real synthetic gate pass -> later production refusal updates reopened Home with the actual first blocker and verifier time. Loaded Home receives worker-thread publication on its dispatcher without refreshing job collections or clearing a notice. Unloaded views reject already queued callbacks.
- Actual diagnostic package BuildPlan reads the real gate. Failed readiness is recorded even though archive export succeeds. Later passed readiness stays passed when archive staging fails. A disposed subscriber cannot fail either operation.
- Slow old success cannot overwrite newer failure or an unfinished check. A later eligible success restores the summary. Generation uses ordering, not wall-clock recency.
- Returned Cancelled failure, thrown cancellation and preworker cancellation yield unconfirmed, not invented workstation faults. Exact immutable-snapshot conditional invalidation cannot override a newer failed/passed/in-progress observation. A synchronous read already running keeps its real report despite later page cancellation.
- Full live-call publication has no intermediate pass; the final typed Cancelled result is rejected even without caller-token cancellation. The authority alone owns the generation.
- Throwing optional sink methods or disposed/throwing view subscribers preserve gate permission/refusal, original exception and a test-owned active SQLite lease. Internal subset success becomes unconfirmed. A non-verifier unknown-mode EnvironmentNotVerified result does not become an observed blocker.
- Original not-run/advisory/no-reason shapes, first-blocker ordering, timestamp localization, no-diagnostics Home refresh/navigation, recovery lists/commands, keyboard-reachable navigation and import/recovery reachability remain tested.

## Isolation and exclusions

Fixtures inspected: `WorkstationVerificationFixture` uses unique temp synthetic manifests/executables/evidence/workspace, fake facts and fake time; no accepted production preset or live processor. `SessionServiceHarness` uses unique temp SQLite/workspace and fake external processors, with the workstation lease DB and resource identity owned by that workspace. `HomeScreenHarness`/`SettingsScreenHarness` wrap this isolated service. The selected composed graph uses `TempApplication` and overrides the lease authority to a unique path/resource in its temp workspace. `WpfRendering` creates no native window; it measures/arranges controls on STA threads and uses RenderTargetBitmap at 96 DPI. No production DB/default shared lease/customer file operation.

Preserved exclusions / NOT RUN:

- Real-window/UIA: ErrorDetailsRenderingTests, KeepOriginalExtentUiTests, ManualCropAdjustmentUiTests, PrintDimensionsPreflightUiTests, SharedReviewAuthorityTests, OperatorWave1AHostTests, OperatorInteractive.
- ApplicationStartup, SessionSmokeTests, HomeAndWorkflowSelectionTests, ApplicationStartupTests; the corresponding startup case inside EnvironmentGateCompositionTests; SettingsAndLocalisationTests.Composed_diagnostic_paths_render_read_only_and_copyable_in_both_languages.
- ProductionCompositionTests and the Production-mode composition case inside EnvironmentGateCompositionTests.
- `ManualResultImportTests.Interrupted_recovery_offers_restart_manual_import_and_abandonment`: known hanging test, excluded and **NOT PASS**.
- Broad 11,531-case workflow/persistence suite, real Windows/Photoshop automation, other DPI, physical keyboard/mouse traversal and Jira human acceptance.

## Visual evidence and review status

48 final PNGs, en/zh-CN, 1000×700 and 1920×1040, 96 DPI. States include not checked, checking, pass, failed blocker, not-run plus warnings, unconfirmed, unconfirmed plus cleanup warning, pass plus recovery warning, expanded variants, and all disclosures plus recovery failure notice/last recovery action. `renders-final/INDEX.md` lists every filename, SHA-256 and capture UTC time.

Implementer directly inspected: live-check-pending-warnings-details zh1000; blocked en1000; ready en1000; not-confirmed-warnings-details en1000; all-details-recovery-end zh1920 and en1000; not-checked zh1000; checking en1000. Text was legible and not clipped. Scrolled all-details captures deliberately move part of the upper summary out of view while showing the notice code and last recovery action. Automated assertions establish both import and recovery reachability.

Independent review: one coordinator-managed fresh native reviewer context, same context for precheck and final review. Precheck HOLD and exact-snapshot amendment/clearance retained in `PRECHECK_REVIEW.md`. The reviewer completed final source, raw evidence and direct PNG inspection: technical PASS, no actionable findings; the full independent review records its scope and limits. No reviewer test execution is claimed.

## Acceptance and routing

AC1–AC4 PASS automated. AC1's previous gate/package freshness gap is closed, including already-open Home. AC5 PARTIAL: synthetic bilingual layout and reachability pass; physical input/other scaling remain NOT RUN. Owner copy review remains OPEN; the prompt approved directions, not final novice acceptance.

Policy 2.4, route_offset 0. Mixed integration/UI unit planned/requested as Astra 6.0 High using native subagent support; actual model/effort UNVERIFIED. Post-plan reassessment retained Astra because this immediate authority/ordering/cancellation/view-lifetime unit is tightly coupled. No unrelated backend stage or claimed downgrade. Execution host: local Windows desktop workspace. The independent review is a real fresh context; no full-history fork was used for review.

No commit/push/Jira mutation was performed by the implementation agent; root coordinator owns those actions. All four earlier audit residues and the old review packet/evidence were preserved. No later Epic/11153 work.

## Coordinator final publication and acceptance record

Code [11df50f7143faa8cc33ec537eaf5f7a162a69d08](https://github.com/MQLite/printflow-Studio/commit/11df50f7143faa8cc33ec537eaf5f7a162a69d08) is MASTER_PUSH_VERIFIED: direct normal fast-forward from `20ece916d290e489fe64a58155800a873dde5ed0`, tree `1e12d2b8f071340285f40d750d31b90bec295ef0`, 16 candidate source/test paths. Authenticated GitHub branch/commit/tree/parent/path readback, fetch and ls-remote agree at `2026-09-29T22:57:10.9581530+00:00`. Staged paths exactly matched candidate 02, whose disk hashes were rechecked before commit. No Co-Authored-By trailer.

The same independent reviewer completed source, raw evidence and visual review: technical PASS, no actionable findings. Reviewer ran no tests/builds. AC1's missing observation paths are technically closed; AC1–4 pass source/automated verification. AC5 remains PARTIAL. Owner final copy acceptance, physical input, other DPI/scaling and Jira human check remain OPEN/NOT RUN. No synthetic result is human acceptance or production-readiness approval.

SCRUM-11152 alone moved In Review → In Progress (21) at corrective implementation start, then → In Review (31) after verified code publication and review. One deduplicated closeout comment `10187`; original comment `10186` body/created/updated unchanged. Final authenticated read `2026-09-29T22:58:30.117Z`: 17 issues, 26 Blocks links, 51 string labels, 34 exact issue timestamp strings, complete comment pagination. No other-issue business drift. Full descriptions/AC, parent fields, priorities, labels and links preserved. Corrected converter/exporter and independent raw-response fidelity oracle both PASS; exact 23-column CSV with UTF-8 BOM.

Privacy-safe docs are published in a separate following master commit. Its exact verified SHA is recorded only in the local final audit and packet to avoid a self-reference commit chain. Account-bearing raw responses/readback and packets remain ignored/local. All four old audit residues and previous packet/evidence remain preserved. No feature branch, PR, force-push, deployment, migration, production startup, desktop control or SCRUM-11153 work.
