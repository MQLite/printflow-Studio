# SCRUM-11065 — Meitu JPG-to-PNG export route remediation

Date: 2026-09-10 (Pacific/Auckland)  
Routing policy: Codex Global Development Routing & Context Policy 2.2  
Authorized scope: remediate the signed-production Meitu JPG-to-PNG export-format route, verify it on the fixed workstation, complete the seven-category SCRUM-11065 acceptance run if possible, revalidate Production only after a Passed run, reassess the named Jira items, document, and create local commits. SCRUM-11136 execution, deployment, push, production data, and other repositories are excluded.

## Baseline

- Repository: `D:\Repositories\printflow-Studio`
- Branch: `master`
- Starting HEAD: `967fc2ba5fb2f195e031a2f6fbcec161c67ecb13`
- Starting worktree: clean
- Accepted Product test baseline: 11,712 passed, 0 failed, 0 skipped. Later diagnostic/closure runs do not replace it unless Product source/tests change.
- Current statuses remain: SCRUM-11065 PARTIAL; SCRUM-11123 PARTIAL; SCRUM-11115 FULL; SCRUM-11136 PARTIAL/not executed.

## Requirement and evidence matrix

| Requirement / invariant | Current implementation | Current accepted evidence | Live observed behavior | Actual gap | Evidence needed before code change |
|---|---|---|---|---|---|
| Fixed-workstation Meitu export behavior (SCRUM-11062 / 11002) | `GuardedMeituUiDriver` recognizes the Save surface and reads/writes its format control | `apps/meitu/editor-export.json` records a `Value`-pattern `formatCombo`, required/observed value `png` | With a JPG source, the initial value is `jpg`; `SetValue("png")` can report success while read-back stays `jpg` | Accepted evidence came from a PNG/RGBA source and does not describe the JPG default or popup route | Runtime hierarchy and semantics for the recognized combo, owned `QComboBoxPrivateContainer`, and `png` item |
| Fail closed before Save (SCRUM-11088/11089 and Epic 11300) | Setter result is never trusted; failed read-back stops before Save As | Negative export evidence and existing `A_format_that_does_not_read_back_as_the_signed_value_stops_before_Save_As` test | Current path stops safely | No deterministic recovery route exists | Positive activation mechanism plus absent/unknown/wrong-process/missing/disabled/stale/read-back-negative observations |
| Validate output before Revision (SCRUM-11090 / 11305) | Existing output stability, decode, hash, alpha and revision ordering remain downstream | Current accepted Meitu output contracts | Already proven for routes that export | Must remain unchanged and downstream of confirmed PNG read-back | Live portrait and fine-hair proof including source hash and lock release |
| Immutable preset authority | `appsettings.json` binds preset version/path/digest; provider validates evidence digests | Preset 1.16.0 is `ACCEPTED_IMMUTABLE`; export evidence and all 28 integrity entries rehash | Popup route is not in signed authority | Existing accepted files cannot be edited in place | New supplemental evidence, next legitimate preset, new hashes, and provider/integrity tests |
| Full local acceptance (SCRUM-11065 / 11005) | Standard-set runner requires one seven-category run and real visual decisions | Current record is PARTIAL; several categories are individually proven | JPG categories are blocked on this defect | Separate diagnostic successes cannot close the item | One unfiltered seven-category run and Operator decisions on that same run |
| Production resumption (SCRUM-11123 / 11608) | Revalidation record plus normal Product verifier gate Production | Current record is PARTIAL | Not yet eligible | Must not create revalidation early | Passed standard-set result, actually tested install folder, independently checked record, normal verifier Allowed |

Exact Jira source: `C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv`. Relevant rows: SCRUM-11062/11002, SCRUM-11088/11303, SCRUM-11089/11304, SCRUM-11090/11305, SCRUM-11065/11005, SCRUM-11123/11608; parent epics 11000, 11300, and 11600.

Current immutable authority:

- `D:\PrintFlowStudio\Baseline\workstation-v1\apps\meitu\editor-export.json` — SHA-256 `574DE5FD437CA50C13EBC925404A0A7FB003235DC45E98CE08884306E214CF8E`
- `D:\PrintFlowStudio\Baseline\workstation-v1\preset\printflow-workstation-v1.16.0.json` — SHA-256 `6396FB4EB87F69C6789304CE191453654B2B75E82A5A9AB0161F90556A6F1A80`
- Accepted `XiuXiu.exe` SHA-256 `D65C6D82323275361EA0ADFBB3F6A5C0D2A5CF4CF63EA3AF1A7DDD4544B037B1`

## Execution stages

### 1. Tight red-capable feedback loop — CONTINUE

- Objective: extend `GuardedMeituExportTests` with a realistic recognized-popup fake for a JPG default whose Value setter silently drops the write.
- Planned model/effort: GPT-5.6 Sol High because this is a safety state machine and regression boundary.
- Requested model/effort: GPT-5.6 Sol High. Actual model/effort: UNVERIFIED; native alternate execution context accepted the request, but runtime metadata did not report the actual model.
- Required test: the new focused case must fail before adapter support and later pass; it must assert selection precedes fresh `png` read-back and that Save As/confirm never precede read-back.
- Existing guard remains required: silent setter refusal with no recognized popup stops before Save.
- Completion: one fast deterministic command catches the exact missing recovery behavior.

### 2. Live popup evidence — CONTINUE

- Objective: use only `FIX-PORTRAIT-001.jpg` (and, if needed, the fine-hair fixture) without producing a final export; verify clean Meitu state and accepted executable digest; capture the full runtime hierarchy and popup lifecycle.
- Dependencies: fixed workstation, recognized Save surface, lock free, no unrelated work or unknown modal.
- Planned model/effort: Sol High. This is production-adapter evidence and environment safety, not visual UI design.
- Required evidence: process/ownership, window/control class, AutomationId, Name, ControlType, ancestry, runtime id, enabled/offscreen, supported patterns, bounds/clickable point, current-value semantics, popup open/replacement/disappearance behavior, and all requested negative cases.
- Interaction priority: SelectionItem, Invoke, then only a runtime-derived point on a positively recognized visible enabled item owned by the accepted Meitu process.
- Completion: the narrow positive route and fail-closed negative contract are factual and reproducible.

### 3. Evidence/preset reissue — CONTINUE

- Objective: create supplemental export-format-selection evidence and the next legitimate preset (expected 1.17.0 after confirming it is unused), without modifying 1.16.0 or its evidence.
- Dependencies: stage 2 observations.
- Planned model/effort: Sol High due to signed-baseline integrity and production-gate risk.
- Required checks: supersedes 1.16.0 and exact digest; preserve and rehash inherited integrity entries; add the new evidence digest; mark new evidence/preset read-only; hash exact bytes; update `appsettings.json` version/path/digest atomically; provider/integrity tests.
- Completion: current pointer, new preset, evidence, parser, and digest verification agree; historical authority remains byte-identical.

### 4. Infrastructure-only adapter repair and targeted tests — CONTINUE

- Objective: add an optional signed popup-selection signature and implement the smallest bounded recognized route inside Infrastructure/Meitu.
- Planned model/effort: Sol High for stale-target, ownership, unknown-state, and pre-Save safety invariants.
- Algorithm: fresh format read; if already `png`, continue without dropdown; otherwise validate Save surface/combo, open combo, acquire exactly the recognized owned popup/item, activate through supported pattern or tightly validated live point, settle/reacquire within bounded retries, and freshly read format; only `png` permits existing Save path.
- Required tests: already PNG; JPG popup success; silent setter refusal not treated as success; absent/unknown/wrong-process popup; missing/disabled item; popup disappearance/replacement/stale target; read-back still JPG; zero Save for every refusal.
- Architecture guards: popup knowledge stays in Infrastructure/Meitu; no fixed coordinates; no generic arbitrary popup API exposed to Workflow/Domain/App; unknown states remain fail closed.
- Completion: focused and affected Meitu/preset/output-validation/architecture tests pass.

### 5. Live adapter verification — CONTINUE

- Objective: run the actual portrait enhancement, fine-hair background removal, and bounded already-PNG route.
- Planned model/effort: Sol High.
- Required proof: JPG starts as `jpg`, recognized route produces read-back `png`, Save happens afterward; output validation and Revision ordering; portrait output; fine-hair real alpha and subsequent trim eligibility; sources unchanged; lock released; already-PNG route does not open dropdown.
- Completion: both JPG categories have direct live proof and existing PNG is not regressed.

### 6. Product gates and complete standard set — CONTINUE

- Objective: focused suites, affected safety/boundary suites, clean Release build, one final full Product suite after stability, static preflight, and one unfiltered seven-category run.
- Planned model/effort: Sol High.
- Explicitly unnecessary: repeated full-suite runs during development; category-scoped runs as closure evidence; asset regeneration.
- Completion: automated run completes; if only manual visual checks remain, stop external automation and hand off exact run/check/artifact details.

### 7. Operator gate, revalidation, Jira, docs, review, Git — FRESH_PREFERRED only at final review

- Objective: apply only real Operator decisions to the same run; after status `Passed`, create and independently inspect revalidation, run the normal verifier, reassess Jira from exact rows, append factual docs, self-review, and commit logically.
- Planned model/effort: Sol High for normal review; use a materially fresh context only if it improves independence/relevance. Do not claim independent review without a genuinely isolated context.
- Hard gates: no fabricated Operator decision; no revalidation before Passed; no SCRUM-11136 execution/FULL; no Jira mutation without explicit authorization; no push/deploy.
- Required docs: new Meitu remediation report; append-only SCRUM-11065 and coverage re-audit; conditional SCRUM-11123 addendum; runbook only if operator-visible behavior changes.
- Completion: clean `master`, local logical commits, accurate PASS/PASS WITH NOTES/BLOCKED result, no push.

## Context and routing

- Default label is `CONTINUE`; implementation/test progression stays in this task.
- `FRESH_PREFERRED` applies only at a genuinely large final review boundary.
- `FRESH_REQUIRED` applies only if context integrity or required independent acceptance demands it.
- `MODEL_SWITCH_UNAVAILABLE`: the root runtime does not expose a verified in-thread model switch or actual model metadata. A native alternate execution context accepted the requested `gpt-5.6-sol` / `high` parameters, but reported actual runtime as `UNVERIFIED`; no claim of a completed model switch will be made.

## Stop conditions

- Unsafe or non-deterministic popup recognition/operation.
- Unknown ownership/state that cannot be resolved with bounded reacquisition.
- Risk to unrelated unsaved operator work.
- Missing real Operator visual decisions when they are the only remaining gate.
- Any requirement to deploy, push, change production data, execute SCRUM-11136, or close Jira without additional authority.
