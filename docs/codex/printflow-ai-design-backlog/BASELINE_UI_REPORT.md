# SCRUM-11157 bounded UI baseline repair

Status: **targeted PASS; full baseline and independent review pending**. Recorded 2026-10-05 12:23 NZDT.

## Scope and routing

- Workspace: `C:/Users/admin/.codex/worktrees/prompt-work-images/printflow-Studio`.
- Branch: `codex/prompt-work-images`; starting HEAD: `d865e7e5ce5fc7ff89923cf8fab9e9fad3c76cf9`.
- Development routing policy v2.4; inherited `route_offset: 0`.
- NormalRoute / RequestedRoute / ExecutionTarget: `gpt-6-astra/high`; AdjustmentResult: `UNCHANGED` (WPF UI rendering diagnosis/contract repair floor).
- ActualRoute: **`gpt-6-astra/high`**, verified from this execution's exact `turn_context`, session/thread `01a10933-c0e5-7972-a089-fcac6c14b08f`; runtime host is the local Windows workstation. The session's default cwd is the original checkout, but every repository command explicitly targeted the isolated workspace above.
- This context is the natural startup/rendering diagnostic boundary. It remained on Astra while repairing the tightly coupled WPF test unit. Next independent review and full suite are coordinated by the parent; no claimed live model switch.
- No production source, customer files, native visible UI, Jira, commits, pushes or deployments were changed by this unit. The existing `ManualResultImportTests.cs` edit belongs to another agent and was preserved.
- First phase was strictly read-only while the parent's full run was active. Tests/edits began only after the parent confirmed that run and its owned processes had ended.

## Findings and minimal changes

1. **P2 — stale startup warning selector** (`ApplicationStartupTests.cs`, old lines 324–326). The startup status and legacy `StartupSummary` assertions passed, but the rendered test searched for `Startup_DiagnosticRetentionWarning`. Current `HomeView.xaml:95` binds the visible warning to `RetentionWarningText`; `HomeViewModel.cs:169` resolves `Home_DiagnosticRetentionWarning`. The separate readiness test explicitly requires these messages to differ so the cleanup notice cannot imply production readiness. The test now selects the unique `Home.Startup.RetentionWarning`, requires Visible and positive height, and checks the current localized resource. Both languages, recovery/retention order, non-refusal, failure code, legacy summary, and private-detail suppression assertions remain; private-detail suppression is also checked on rendered text.

2. **P2 — missing expansion of technical details** (`PrintDimensionsPreflightUiTests.cs`, old lines 53 and 120). The five failing theory rows searched the visual tree for pixel/PPI rows inside the currently collapsed `Session.PrintDimensions.TechnicalDetails` Expander (`SessionScreenView.xaml:1739`). The tests now assert the section starts collapsed, expand that exact section and settle dispatcher/layout before reading the generated rows. Every original exact approved-artwork, canvas, pixel/PPI, UIA label, draft-update and no-persistence assertion is retained. Existing `PrintSizeGuidanceUiTests.RenderWithDetailsOpen` supplied the comparable rendering pattern.

3. **P2 — overly broad approval selector and leaking synthetic-host culture** (`OperatorWave1AHostTests.cs`, old lines 57 and 260–344). The screen legitimately has multiple `ReviewApprovalButton` instances. The host smoke test now uses `Single` for the stable `Session.Approve` identity, additionally checks visibility/positive height, and retains exact command, target identity and non-default assertions. Separately, `SyntheticHostData` captured `OperatorCulture.Current` (resolved fallback) and later restored it as an explicit selection. It now owns the existing `OperatorCultureScope`, which restores the raw selection and both ambient culture fields. A single producer-to-real-consumer regression exercises host disposal followed by the existing English no-thumbnail rendering test; this failed with Chinese text before the scope repair.

The original interrupted full-run log contained 15 failed cases: startup (1), print dimensions (5), host approval selector (1), culture isolation (1), English manual crop (2), English keep-original extent (3), and English recent-list accessibility (2). The last seven are consistent with leaked selected Chinese culture; the final targeted run included their complete classes and all passed. There was no evidence requiring production behavior changes or another repair scope.

## Direct evidence

Evidence root: `artifacts/test-results/prompt-work-images-baseline-ui/` (ignored test artifacts).

| Run | Exit | Observed result | Evidence |
|---|---:|---|---|
| Unchanged exact startup case | 1 | 1 failed / 1 total, old resource absent from rendered tree | `startup-red.log`, `startup-red.trx` |
| Unchanged print dimensions + host smoke + culture isolation | 1 | 7 failed, 4 passed / 11 total | `ui-contracts-red.log`, `ui-contracts-red.trx` |
| Added producer-to-consumer regression, before fixture repair | 1 | 1 failed / 1 total; expected `No preview`, actual `无预览` | `culture-producer-red.log`, `culture-producer-red.trx` |
| Final affected classes, Release | 0 | **49 passed, 0 failed, 1 intentional interactive skip / 50 total**; test duration 21 seconds | `ui-baseline-green.log`, `ui-baseline-green.trx` |

The existing skipped case is `Operator_launched_visible_synthetic_host`; no native interactive host was launched. TRX has 49 executed/passed and its individual skipped result is `NotExecuted` (the adapter's aggregate `notExecuted` attribute is zero); stdout reports the single skip explicitly.

All test commands used `tests/PrintFlow.Tests/PrintFlow.Tests.csproj -c Release --no-restore`, `--blame-hang-timeout 120s`, a named TRX and the evidence results directory. Initial unchanged reproduction also used `--no-build`; subsequent runs built the modified tests. No full 13k suite was launched by this unit.

Final command:

```powershell
dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~ApplicationStartupTests|FullyQualifiedName~PrintDimensionsPreflightUiTests|FullyQualifiedName~OperatorWave1AHostTests|FullyQualifiedName~OperatorCultureIsolationTests|FullyQualifiedName~ManualCropAdjustmentUiTests|FullyQualifiedName~KeepOriginalExtentUiTests|FullyQualifiedName~HomeRecentAccessibilityTests' --blame-hang-timeout 120s --logger 'trx;LogFileName=ui-baseline-green.trx' --results-directory artifacts/test-results/prompt-work-images-baseline-ui
```

Exact test-file diff is saved at `artifacts/test-results/prompt-work-images-baseline-ui/ui-contracts.patch`. `git diff --check` passed (only repository LF-to-CRLF informational warnings from Git).

## Handoff

- Changed only three existing test files plus this report; `SyntheticHostData` is nested in `OperatorWave1AHostTests.cs`.
- This agent's targeted verification supports only the bounded repair. The coordinator subsequently obtained the complete ordinary Release PASS: 13,919 passed / 0 failed / 1 existing interactive skip / 13,920 total, exit 0, 9m51s, with the existing `WorkstationEntryPhase!=Runtime` boundary. See BASELINE_REPORT.md for exact scope, failed unfiltered history and seven dedicated Runtime cases NOT RUN.
- Fresh read-only review `/root/ui_baseline_review` verified production bindings, independent readiness contracts, stable approval identity, culture restoration and raw RED/GREEN; no actionable findings. Actual `gpt-6.1-sol/high` verified from exact session `01a1093c-9e59-78c1-a3e5-46d766539918`, a disclosed generation-6 fallback after Sol6.0 capacity interruptions. A further independent baseline-scope audit verified the four-file diff and full raw evidence. Existing original-checkout audit changes remain outside this unit.
- No independent review claim is made by the implementer. No additional tests or edits are pending in this unit unless review identifies a concrete issue.
