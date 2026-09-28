# PF-OPUX-v1-wave1 — bounded execution

Date: 2026-09-22. Status: **PARTIALLY COMPLETE — bounded implementation, documents, independent review and Jira synchronization finished; interactive input and human acceptance evidence remains open.** Authority: `printflow-remediation-prompts/PrintFlowStudio_Operator_UX_Wave1_Development_Prompt_v1.md` and the current user's batch authorization. Original AC remain in PLAN.md section 6 and the immutable import snapshot; only the prompt's section 4 amendments apply. Stopped at the Wave 1 boundary.

## Baseline and scope

Workspace: D:/Repositories/printflow-Studio; branch master; HEAD eea60198c5095243f0694717a7479b00c3e1cd73. Codex Desktop 0.155.0-alpha.9.2, local Windows host. No checkout, commits, push, deployment, production app launch, customer work, dependency upgrades or Wave 2 execution.

Pre-existing: the untracked initiative folder and two modified protected documents. Their initial SHA256 values, plus immutable import evidence:

| File | SHA256 |
|---|---|
| docs/printflow/original-jira-functional-coverage-reaudit.md | 2AD445564697603395DCBE07A35CB02A7BDC625F52278B2316EDCBF8B9B0CFBF |
| docs/printflow/scrum-11134-maintop-reference-comparison-fixed-workstation.md | C341CD5AA4C49FD3D0B685207379A6BBF12C7B5F512446604D60779F093EE845 |
| JIRA_FINAL_IMPORT.csv | AC0FBEB35D2279B76018B3ED56BC1DB9296A5FF8912367DA95CF4CBFDBC767E5 |
| JIRA_READBACK_SNAPSHOT.json | 3B663216B861A9DB3E7465A45F958195414E810AF33FEEC7E8D871D37AAD8A2E |
| JIRA_WRITE_LEDGER.json | 8CB71EEC43DA1761BBCCA7C4C57653B77878B2FDE510ED400297EFA26F04AE79 |

Authenticated baseline: Atlassian Rovo full search, `project = SCRUM AND labels = pf-opux-v1 ORDER BY key ASC`, 17 issues, isLast=true. Identity verified by Planning-ID and returned IDs. No newer comments in the retrieved initiative. Initial compact search was identity-only and is not full-description evidence.

## Executable sequence and ownership

1. Record section 4 amendments and mark planning/access history obsolete without changing original descriptions/import evidence.
2. One Astra High UI writer implements 11142, then 11143, with focused failing tests before fixes; it owns SessionViewModel, SessionScreenView, resources and related tests. Complete the decision table below before panel code.
3. Separate documentation work: 11140 bilingual reference at `docs/printflow/operator-ux-terminology.md`; 11141 script/template/safe setup at `docs/printflow/operator-ux-novice-walkthrough.md`. Documentation does not block UI work.
4. Run relevant Stop/Take Over, transition/command surface, input, review, localization and WPF rendering checks; save commands/counts/exit codes and synthetic captures. Inspect captures in both languages. Production App startup is prohibited.
5. One fresh isolated review of combined code/docs and raw test/UI evidence (Astra High because WPF/input safety and visual inspection form an inseparable review). Fix relevant findings, rerun affected checks and re-review affected scope if necessary.
6. Re-read then amend only 11143/11145/11154 descriptions; comment only 11140–11143 with factual evidence. Read back writes. Fetch all 17 current issues, save separate readback and exact 23-column UTF-8 BOM CSV; validate current descriptions, AC, IDs and links. No statuses or other fields changed.
7. Verify protected hashes and actual diff; report implementation, automation, rendered UI, agent visual review, human checks and Jira separately; stop.

## Panel design and decision table (refine against actual code before implementation)

Sources: `_session.State`, `_session.CurrentStep`, `_session.CurrentStepFailure`, `_runtime.State`, existing `Can*` properties, existing commands and exact current artefact identity. Display getters have no service side effects. Existing `CanRetry` additionally suppresses non-retryable PDF failures; manual import, runtime Stop/Take Over and other specialized controls use their existing eligibility projections. Therefore preserve the actual baseline visible/enabled surface, not a newly manufactured equality with bare AvailableCommands.

| Precedence / state | Category and next step | Recommendation | Secondary actions | Focus |
|---|---|---|---|---|
| HandedOff, even with ReviewRequired step | Handed off; automation has ended; only truthful supported guidance | No Run/Approve | Existing controls unchanged | Non-activating status |
| Completed / Abandoned | Completed / abandoned; no delivery claim | None | Existing controls unchanged | Preserve valid focus |
| Runtime active / stopping | Processing automatically / stop request notice | No Approve or Complete | Existing Stop/Take Over unchanged | Preserve valid focus |
| Current review + lawful Approve | Waiting for review; inspect current result | Existing Approve, visually emphasized | Reject and all other baseline actions | New target: non-activating heading/preview; refresh: preserve |
| Failed / RetryRequired | Stopped or failed, localized reason | Retry only if currently offered | Existing alternatives/details unchanged | Preserve valid focus |
| Needs input / ready step | Needs input, specific existing next operation | Legal existing input/run action | Existing alternatives unchanged | Predictable keyboard navigation |

Review-entry protection is transient App/UI input handling only. Key repeats, remaining events and double-clicks cannot approve a later target. Fresh navigation and activation still work. No delays, extra checkboxes, persistent approval state, transition or engine changes.

## Authorized clarification addendum — 2026-09-22

11143 scope originally says “recommended action gets primary styling and keyboard default focus”; AC8 ends “keyboard focus starts on the recommended action.” 11154 scope says “focus moves to the recommended action after each transition”; AC2 says “focus landing on the recommended action after each transition.” Replace only those focus clauses with the full section 4B contract: visual emphasis plus predictable keyboard access; on a new review target focus a non-activating heading/preview, never a consequential action or implicit approval default; preserve valid focus during refresh; every approval requires fresh intentional input for the displayed result; repeated/remaining input and double-clicks cannot approve a subsequent result; explicit navigation then fresh activation remains supported. Preserve 11143 AC8 localization, clipping and text-based status clauses and all other AC.

11145 scope/AC7 clarification: pending review cannot directly export/open as finished; an explicit final “Confirm result and save” may first lawfully approve the current result, then export that exact successfully approved result. Approval failure means no export. Rejected, invalidated, recycled and obsolete results remain ineligible. Approval and delivery are separately observable; export failure never reruns image processing/TIFF generation. This is documentation only; no delivery implementation.

Reason: avoid unintended approval and remove the ambiguity between lawful approval-then-export and direct pending export. Original exact text and saved replacement are retained in the separate Jira amendment record. Historical import files are immutable.

## Routing and context checkpoints

Approved local policy: C:/Users/admin/.codex/workflows/development-routing.md, v2.3 (2026-09-10), loaded through the installed global AGENTS.md. CODEX_HOME environment variable is unset; existing default home and current session metadata identify C:/Users/admin/.codex. No remote policy retrieval/install/update; upstream freshness not rechecked. ROUTE_PROFILE route_offset=0 from prompt section 2. Supported native agent schemas expose actual selectable model/effort pairs.

| Boundary | NormalRoute | Offset | RequestedRoute / ExecutionTarget | ActualRoute | Adjustment | Context / reason |
|---|---|---|---|---|---|---|
| Preparation / UI design | gpt-6-astra high | 0, prompt | gpt-6-astra high | gpt-6-astra high, verified turn_context in current session JSONL | UNCHANGED | CONTINUE; UI/input safety floor |
| Post-plan UI implementation | gpt-6-astra high | 0, inherited | gpt-6-astra high | gpt-6-astra high, verified turn_context; artifacts/pf-opux-wave1/ui-route-evidence.json | UNCHANGED | One UI writer; CONTINUE across A/C/fix/render/test; tightly coupled UI/input unit retains UI floor |
| Separate document semantics | gpt-5.6-sol medium | 0, inherited | gpt-5.6-sol medium | gpt-5.6-sol medium, verified child turn_context | UNCHANGED | Separate files and context; no UI implementation |
| Export serialization / validation | gpt-5.6-sol medium | 0, inherited | gpt-5.6-sol medium | gpt-5.6-sol medium, verified child turn_context | UNCHANGED | Known schema and independent file; actual downgrade via native context |
| Coordinator Jira merge/readback/reporting | gpt-5.6-sol medium | 0, prompt | Sol medium intended; available current Astra high safe fallback | gpt-6-astra high | UNCHANGED profile; MODEL_SWITCH_UNAVAILABLE in-place | CONTINUE; coordination retained in root, no claimed live switch; substantive independent docs/export routed to verified Sol contexts |
| Independent combined review | gpt-6-astra high | 0, inherited | gpt-6-astra high | gpt-6-astra high, verified reviewer turn_context; review and correction re-review complete | UNCHANGED | FRESH_REQUIRED; fork_turns=none, original AC + diff + raw evidence only |

Do not infer runtime from requested selection. Record actual model and effort separately. In-place model-switch tool unavailable; use verified native execution contexts for separable work and disclose coordinator retention as MODEL_SWITCH_UNAVAILABLE where relevant.

## Validation and completion record

Units A/C implemented and targeted automated validation PASS (228/228, exit 0). Fresh independent review and correction re-review complete; no remaining actionable product defect found. Actual hosted keyboard focus/navigation and positive fresh physical mouse activation: NOT RUN/unverified. Human workstation visual check: NOT RUN. Novice walkthrough: NOT RUN. Full Epic/operator acceptance: not claimed. Later design gates remain 11144 delivery storage/export, 11147 trim-from-review, 11148 colleague-correction re-import.

| Item / requirement | Final evidence status |
|---|---|
| 11142 AC1–5: application copy, handoff guidance and existing command behavior | PASS in targeted automated checks and code review |
| 11142 AC6: bilingual resources and clipping | PASS resource parity and synthetic WPF rendering at 1000×700 / 96 DPI; real workstation resolution/scaling check NOT RUN |
| 11140 AC1–4 documentation scope | PASS documentation review; no signature gate or implementation dependency |
| 11143 AC1–5 and AC7: status, next step, legal recommendation, truthful completion, placeholder | PASS automated checks and independent review |
| 11143 AC6: command surface | PASS original toolbar inventory plus authoritative transition/service fixtures and nearby regression tests; existing PDF retry suppression explicitly preserved |
| 11143 amended AC8: bilingual layout, emphasis and review input | PASS synthetic rendering, routed-input/UIA and logical-focus checks; genuine hosted keyboard navigation and positive fresh physical mouse activation NOT RUN/unverified; workstation visual check NOT RUN |
| 11141 preparation | COMPLETE; script, empty template and safe-run instructions reviewed. Human-conditioned AC1 NOT RUN; proposed-addition template for AC2 prepared, AC3–4 non-substitution/privacy retained |
| 11145 scope/AC7 and 11154 scope/AC2 | PASS clarification-only Jira save/readback; implementation outside this batch |

Independent review found one P2 documentation defect: Return to automation was described as starting an attempt, although it only restores an active/waiting state and makes the separate Run action available. The glossary was corrected and the reviewer re-read the affected text against the implementation; finding RESOLVED. No product-code change or test rerun was needed for this prose correction. Review also recorded the input-evidence limitations below and inspected all eight final synthetic captures. The reviewer read direct test output and independently checked the then-current CSV against its authenticated snapshot; it did not claim a human session or new test execution. The final comment-only refresh was revalidated by the coordinator/export script.

### Refined UI design before Unit C implementation

Runtime source is `AutomationRuntimeView.State` (`Running`, `StopRequested`, `TakeOverRequested`) plus the current persisted `StepState.Processing`; `SessionState.Active` alone never means running. Actual source preparation uses `OriginalConfirmation` plus `OriginalSourceFormat=Psd/Pdf`, not imaginary PSD/PDF step kinds. Review identity comprises session ID, current step, exact Revision ID and SHA-256; no business state is introduced.

| Precedence | Heading / one next-step sentence | Existing recommended binding | Retained secondary actions / focus |
|---|---|---|---|
| HandedOff | Handed off; Photoshop unsupported-return notice when applicable, conditional Return to automation only if offered | ReenterAutomation only when currently offered | All baseline bindings retained, including manual result when legal; non-activating panel for a new target |
| Completed / Abandoned | Completed / Stopped or failed; workflow complete / job abandoned, without a delivery claim | None | Baseline actions unchanged; preserve focus |
| Runtime Running or step Processing; stopping requests | Processing automatically; wait for named current step / actual stopping notice | None | Stop/Take Over unchanged; no approval emphasis |
| ReviewRequired with CanApprove | Waiting for your review; inspect this result then Approve or Reject | ApproveCommand | Reject and all baseline controls unchanged; focus the non-activating status container once for each new exact target |
| Failed / Interrupted / RetryRequired | Stopped or failed; Retry if offered, else manual crop if offered, else read failure details | RetryCommand / BeginManualCropCommand only if offered | PDF-specific retry suppression retained; other actions unchanged |
| Needs original, background selection, dimensions, white ink, ready step, ready completion | Needs your input; specific existing action or input instruction | Existing ConfirmOriginal / StartStep / RunBackgroundRemoval / RunPhotoshopOutput / Complete when actually available; no invented Continue | All baseline controls unchanged; no focus stealing on refresh |

Recommended-command identity only controls styling. It never determines button visibility or CanExecute. A local approval Button handles fresh Enter/Space press-and-release and mouse gestures against its exact target; repeat keys, stale releases and double-click continuations are consumed. UIA remains explicit invocation. No timing gates, checkboxes, persistent flags, engine changes or alternate command set. Language uses the existing localisation event convention; Show/runtime/language changes refresh display only.

### Unit A/C implementation and direct evidence — UI writer handoff

Product/test source is frozen for fresh review. Unit A changes only App-level application-specific labels, hints, confirmation, stopping/retained notices and post-handoff guidance. Meitu resource values and all existing eligibility/command methods are unchanged. PSD/PDF preparation is recognized by OriginalConfirmation plus the source format. Photoshop return guidance interpolates the existing Return to automation / 恢复自动处理 label only when CanReenterAutomation is true; otherwise it states unsupported manual import without inventing an action. No global failure-copy sweep.

Unit C lives in SessionViewModel.OperatorStatus.cs, SessionScreenView.xaml/.cs, ReviewApprovalButton.cs and RecommendedCommandConverter.cs, plus bilingual resources. A single notification hook in SessionViewModel.ManualCrop.cs keeps the projection current when the existing crop draft changes. The compact status panel replaces the old raw subtitle, with workflow identity retained beside the title and the complete step list retained. The developer placeholder is no longer rendered. The existing toolbar and all specialized action bindings retain their original visibility and CanExecute behavior; primary styling compares existing command instances only. No engine, transition, manual-result eligibility, persistence, adapter or delivery code was changed.

New exact review identity includes job ID, step, Revision ID and full hash. A newly displayed target puts logical/keyboard focus on the non-activating status border; normal refresh and language changes preserve valid focus. ReviewApprovalButton consumes Enter/Space repeats and stale releases, ties a fresh press/release to the current target, and consumes second/later mouse clicks and stale mouse releases. There is no IsDefault approval, timing gate, checkbox or persistent approval state. Explicit UIA invocation and fresh keyboard activation remain supported. Input tests route real WPF events through the rendered button; WPF internal event flags are set in the fixture instead of injecting global desktop input. Logical focus/UIA evidence is not a physical human keyboard session.

Stopped reasons use existing localized CurrentStepFailure text, or the authoritative LastAutomationStop audit for operator stops (cancelled attempts do not expose CurrentStepFailure). Active crop is Needs your input and requests boundary adjustment followed by the existing Apply Crop; the prior failure remains in the existing notice, rather than being duplicated in that active-input panel. Completed says only the workflow is complete; no Saved state or delivery flag exists.

Safety execution path: HomeScreenHarness → SessionServiceHarness creates GUID TempWorkspace/TempDatabase roots below OS temp/PrintFlowTests, fake adapters, synthetic files and a workspace-local lease database with a unique test identity. WpfRendering creates/arranges controls on an STA without App startup or showing a window. The input fixture uses a hidden, zero-style HwndSource and no global keyboard/mouse injection. These paths were read before the first test. During the first expanded nearby run, the additional SettingsScreenHarness/TempApplication/startup-composition paths were inspected: their configuration, workspace, database, preset and recovery are temporary; adapters are Fake and the instance guard is fake. SessionService bypasses the machine automation lease for fake-mode processing. That expanded-path check happened during the run, not before dispatch; do not overstate preflight timing. Normal production App startup was never launched. No Photoshop/Meitu process, customer file or production configuration was operated on.

All exact test executable/arguments/environment/exit codes are recorded in artifacts/pf-opux-wave1/test-commands.json. Logs and TRX remain alongside it; parsed counts are in test-results-index.json. SDK: the already-installed per-user .NET 10.0.400. Initial PATH-only invocation selected system .NET 8 and failed SDK resolution before code ran; no toolchain was installed. Successful dotnet test invocations compiled the affected App and test projects and their references.

| Evidence / correction | Result |
|---|---|
| 11142-red | Expected FAIL: 6 wrong-application cases, 2 synthetic captures passed |
| 11142-green → 11142-green-final | First found 3 Chinese button-label mismatches; interpolation corrected them; final PASS 127/127 including StopAndTakeOver and resource parity |
| 11143-red | Expected FAIL: missing status projection |
| input-red | Expected FAIL: repeated Enter reached the subsequent target; Space fixture passed baseline |
| 11143-green / focused iterations | Fixed ancestor binding inside item templates; corrected exact enum names in test/projection compile failures; fixed fixture language-service Current mismatch; used stop audit for localized cancellation; prevented failed-step recommendation falling through to StartStep when Retry is suppressed |
| wave1-targeted | 225 PASS / 1 FAIL; actual crop smoke found zero usable crop height from added header space |
| layout-fix → layout-fix-2 | Replaced stacked subtitle/panel with compact inline status, preserving image area. First run caught Run.Text default TwoWay binding; explicit OneWay fixed it. PASS 20/20 including the unchanged crop smoke |
| crop-guidance-red | Expected FAIL: already-open crop still said stopped/begin crop |
| wave1-focused-final | PASS 22/22; crop-open guidance and both-language captures included |
| wave1-targeted-final | PASS 228/228, 0 failed, 0 skipped, exit 0; 52 seconds |

The final filter is: `FullyQualifiedName~OperatorWave1Tests|FullyQualifiedName~StopAndTakeOver|FullyQualifiedName~LocalisationResourceTests|FullyQualifiedName~SessionControlsTests|FullyQualifiedName~SessionAccessibilityTests|FullyQualifiedName~SessionSmokeTests|FullyQualifiedName~ViewRenderingTests|FullyQualifiedName~SettingsAndLocalisationTests`. It uses `dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --no-restore --filter <filter> --logger 'trx;LogFileName=wave1-targeted-final.trx' --results-directory artifacts/pf-opux-wave1`, with the full executable and environment in the command record. The existing 10,000+ test suite was not run. Nearby rerun was justified by the observed crop/layout defect and subsequent correction, not a repeated full-suite gate.

The new toolbar regression anchors all 12 original toolbar actions to the baseline inventory and actual persisted/service command answers through original-confirmation, ready, review, reject/retry and handed-off transitions. It explicitly retains the original PDF retry suppression. Existing nearby tests cover runtime Stop/Take Over, manual imports/reentry, all three route smoke paths, production review, completion, accessibility and localization. Input cases cover Enter/Space across exact-revision and job changes, non-activating entry, refresh/language focus preservation, double-click/stale mouse releases and explicit accessibility invocation.

Rendered UI: PASS for the existing 1000×700 / 96-DPI WPF harness, with synthetic data only. `before/review-en.png` and `before/review-zh-CN.png` show the old placeholder and equal-weight actions. Final `after/` has `review`, `photoshop-confirmation`, `photoshop-handedoff` and `manual-crop` PNGs in en and zh-CN. Agent visual review inspected these categories in both languages: wrapping readable, no clipped new text, status focus outline and primary styling distinct, secondary actions retained, no Meitu text in Photoshop notices. The crop surface remains narrow with the existing crop controls at this minimum viewport, but the original fit/zoom/drag smoke passes; no broader crop redesign was attempted. Photoshop captures are explicitly constructed presentation fixtures, not evidence of a real Photoshop operation or takeover. Human workstation scaling/visual check and novice walkthrough remain NOT RUN.

UI writer ActualRoute: gpt-6-astra / high, verified from turn_context in C:/Users/admin/.codex/sessions/2026/09/22/rollout-2026-09-22T14-58-18-01a0c70c-c684-7d60-a5c2-9f8dc42d3b52.jsonl. Minimal source/timestamp/model/effort evidence is in ui-route-evidence.json. Policy v2.3, inherited route_offset 0, UNCHANGED; CONTINUE for tightly coupled UI implementation and input/render correction. Independent review is owned by the coordinator in a fresh context; the UI writer does not claim it complete.

### Documentation and synchronization record

SCRUM-11140 documentation scope: PASS AC1–4. `docs/printflow/operator-ux-terminology.md` contains bilingual core and introduced terms, keep/rename/details-only treatment and allowed technical surfaces, exact current resources, owner-authorized state/delivery distinction and nonblocking later semantic decisions. No global copy sweep or signature gate.

SCRUM-11141 preparation scope: COMPLETE / HUMAN WALKTHROUGH NOT RUN. `docs/printflow/operator-ux-novice-walkthrough.md` provides neutral zh-CN instructions, English anonymized record, empty findings and assumption outcomes, all required scenarios and safe-run conditions. Current manual-crop fallback is separate from future direct-review handles; delivery and correction re-import are marked unavailable. Human-conditioned AC1 is NOT RUN; AC2's proposed-addition template and AC3–4's non-substitution/privacy requirements are preserved. No observation, quote, success rate or timing evidence was invented.

Jira: description amendments only SCRUM-11143 (scope/AC8), 11145 (scope/AC7), 11154 (scope/AC2); exact originals, replacements and immediate saved readbacks in WAVE1_JIRA_AMENDMENTS.json. The connector's Markdown round trip adds hard-break trailing spaces; substantive text was verified and all raw strings retained. Final CSV preserves saved strings exactly, without that comparison normalization. Evidence comments only 11140–11143, IDs 10161–10164 respectively; exact saved bodies verified in WAVE1_JIRA_COMMENTS.json. No issue creation, status transition, closure, parent/link/priority/assignee/sprint mutation was requested.

Final current authenticated search was recorded at 2026-09-22T03:39:52.576Z (15:39 NZST), after the last evidence-comment correction, one page with isLast=true and 17 issues. WAVE1_JIRA_READBACK.json includes actual IDs/keys, full saved descriptions, issue timestamps, parents/links, statuses, priorities, labels and verified comments; unnecessary comment-author contact metadata is explicitly omitted. Comparison against the authenticated start-of-run snapshot confirms summary, status, priority, labels, parent, type, relationships and creation time unchanged for every issue. All remain To Do. The only description differences are the three authorized amendments. No pending Jira patches or unverified writes remain.

`& 'docs/codex/printflow-operator-ux-backlog/Export-Wave1Jira.ps1'` — exit 0, PASS, 17 rows / 17 unique IDs, keys and Planning-IDs / 26 Blocks links / 23 columns / UTF-8 BOM. Export-Csv output was parsed and compared to the current snapshot, including complete descriptions and raw AC substrings, status and relationships. Header also independently compared exactly to JIRA_FINAL_IMPORT.csv. Evidence: artifacts/pf-opux-wave1/jira-export-validation.log. Original import files supplied only the fixed schema and local planning metadata; no historical Jira facts filled the current export.

Runtime metadata sources (model and effort fields verified separately): coordinator rollout-2026-09-22T14-54-29-01a0c709-47e4-78d0-b7e1-77b7b78a1826.jsonl (Astra high); documentation rollout-2026-09-22T14-58-31-01a0c70c-f834-7982-9d68-d6650edd67cb.jsonl (Sol medium); export rollout-2026-09-22T15-01-04-01a0c70f-50e7-7961-91d4-bbc28e9fd247.jsonl (Sol medium); reviewer rollout-2026-09-22T15-28-31-01a0c728-6f28-7881-b448-518c6c359d3d.jsonl (Astra high). These are local files in C:/Users/admin/.codex/sessions/2026/09/22. Each child was dispatched through native spawn_agent with fork_turns=none and explicit supported model/effort. Reviewer received original requirements, current diff and direct evidence, without implementer conversation. This is a fresh isolated subagent, not a new main task or a renamed/full-history reviewer.

Protected-file recheck after export: all five initial hashes above still match. `git diff --check -- src tests` passed (only normal LF/CRLF informational warnings). Product changes are App-only; no Domain, Workflow, Infrastructure, configuration or dependency file changes. Git remains master at the initial HEAD, with pre-existing changes preserved and new Wave 1 work uncommitted. No stage/commit/push/deploy operation.

### Independent-review input-evidence follow-up — bounded hidden-host probe

The reviewer identified evidence gaps rather than a demonstrated product defect: the maintained tests prove routed WPF key handling, stale/double-click event consumption, logical focus preservation and explicit UIA invocation, but they do not prove a fresh physical mouse down/up succeeds or that Loaded establishes actual keyboard focus followed by native keyboard traversal.

One approved capability probe reused WpfRendering.OnStaThread/Render with the existing HomeScreenHarness synthetic review and a hidden, non-activating HwndSource (WindowStyle=0, WS_EX_NOACTIVATE, offscreen coordinates). Its RootVisual was the real SessionScreenView and the dispatcher processed Loaded. It neither launched App/startup nor showed/activated a visible window, called SendInput/SetForegroundWindow, moved the cursor, forged Button/device state, or addressed another process. Mouse messages were sent only to that owned test HWND.

Observed: IsLoaded was true; Keyboard.FocusedElement was null; local WM_MOUSEMOVE/WM_LBUTTONDOWN/WM_LBUTTONUP produced zero approvals. The two explicit capability assertions therefore FAILED (0/2, exit 1), in hidden-input-capability.log/.trx. This establishes that the chosen safe hidden host did not provide the real input state needed; it is not a physical-input acceptance PASS or proof of a product defect. Exploration stopped after this single bounded attempt. No new framework, device-state forgery, visible window or global input workaround was introduced.

The temporary capability probe is preserved verbatim as artifacts/pf-opux-wave1/hidden-host-probe.cs.txt and was removed from the maintained test project (not disabled or weakened). Product source and the original 228-pass test baseline remain unchanged. `dotnet build tests/PrintFlow.Tests/PrintFlow.Tests.csproj --no-restore` then rebuilt the project to remove the temporary probe from its test assembly: exit 0, 0 warnings, 0 errors; post-probe-build.log. Historical green and failed logs were not overwritten.

Remaining evidence: **NOT RUN — genuine hosted keyboard focus/navigation and positive fresh physical mouse activation** in an authorized safe interactive synthetic session. Existing routed-event/UIA checks and captures remain valid but do not substitute for those checks. Human workstation visual/scaling and novice walkthrough remain NOT RUN. No further product/test edits were made for this evidence-only follow-up; the independent reviewer can review this limitation and retained probe source.

### Wave 1A follow-up — 2026-09-22

The bounded follow-up is recorded in [WAVE1A_VERIFICATION.md](WAVE1A_VERIFICATION.md), with new raw evidence only under `artifacts/pf-opux-wave1a/`. The preceding Wave 1 results are dated history and have not been regenerated. Its CSV schema/content/link PASS did not establish timestamp fidelity: Wave 1A reproduced loss of offsets/fractional seconds in all 34 timestamp cells and added an exact raw-JSON regression before fixing the exporter. The original Wave 1 snapshot, CSV, logs, captures, failed hidden probe and import evidence remain preserved. Wave 1A does not authorize Wave 2, product startup, customer work, status transitions, commit, push or deployment; see the new record and concise HANDOFF for current evidence classes and the operator-launch fallback.
