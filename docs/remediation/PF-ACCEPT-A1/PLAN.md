# PF-ACCEPT-A1 — Acceptance freeze and execution plan

## 15 September 2026 — stale preset tests corrected; offline gate restored; A1 awaiting desktop availability

Task `PF-ACCEPT-A1 — Align Preset Tests with Accepted 1.18.0, Then Resume A1` started from the
actual clean HEAD `c75c8dee90a747e93ecd8fba0e9e3dbe43257466`; no reset or historical checkout was
performed. Accepted preset `printflow-workstation-v1` `1.18.0` remains unchanged at
`D:\PrintFlowStudio\Baseline\workstation-v1\preset\printflow-workstation-v1.18.0.json`, read-only,
SHA-256 `8484F0AA18728FAC58B58ACD8E811C7058743B61271506647179CA0D0A6C8E0F`. Acceptance and
configuration were not repeated.

Both failures were extracted independently from the preserved 88-pass/2-fail TRX, whose SHA-256
remains `C47FA4B18B856DB130BD6DA014BE2E35940FA5B75634358B25D48FA8E554389C`:

1. `Configured_workstation_preset_is_the_immutable_v1_17_contract` was a current-configuration
   test with a stale expected version and path. It now independently pins exact Id, version
   `1.18.0`, relative path and accepted SHA-256, and hashes the selected manifest bytes against
   that literal rather than comparing configuration with itself.
2. `Manifest_reverifies_every_inherited_and_resize_evidence_entry` was a generic integrity test.
   Only its irrelevant 1.17.0 lineage and fixed-count coupling was removed. It still enumerates
   every declared integrity entry, recomputes every SHA-256, requires every file to exist and
   retains the required evidence-category/read-only assertions.

Two other tests that directly read current `appsettings.json` were aligned to the same independent
exact version/path/hash check. Historical/synthetic 1.17.0 test data and superseded manifest
identities remain explicit and unchanged; there was no bulk replacement, skip, self-comparison or
hash weakening. Correction commit:
`e85f56115cc0cfb5f47c5b38573f2c15be86c88a` (`test: align preset checks with accepted 1.18.0`).

Required bounded validation passed without a full suite:

- source focused configuration/integrity checks: 4 passed / 0 failed / 0 skipped; TRX
  `artifacts/pf-accept-a1/accepted-1.18.0-test-correction/focused.trx`, SHA-256
  `DFA7A387B9B289E51A7751847CD587C4940050E322E773B4563EB1D9C87CEE5B`;
- original source provider/configuration slice: 90/0/0; TRX
  `artifacts/pf-accept-a1/accepted-1.18.0-test-correction/configuration-provider-corrected.trx`,
  SHA-256 `4B9288DB6558E8212EAAFB587ADA6D4C33463E979DA0980DE2D56E2B9AB0664C`;
- fresh controlled pair `ee8e0280-4fbe-436c-953a-8d27b347d35e`, source `e85f561`, SDK
  `10.0.400`, input digest `FB899B0BB01B2868619F31C8310E02D367DE455292E6E1A048A778D70C7FBE6E`,
  zero build warnings/errors, receipt SHA-256
  `52DA695226F1024DF925442EC776E2B0163FBA997CF53A09DBA8EC827996A259`;
- final checks through that pair's exact `harness/PrintFlow.Tests.dll` (SHA-256
  `9CD3F752BA06B99D6BDDEC2267FF244F23D1803905082839AAB47F844F7C5F45`): focused 4/0/0,
  TRX SHA-256 `C6E47349393551F55456B18AD61F741F8F9B9B49171BABE563EE8AFF346564EE`; provider slice
  90/0/0, TRX SHA-256 `355E0B6FAD428139961C1E1A872342FA1EF5D3B0A3DCA87AEBFA560CFFE1F23E`;
- explicit `D:\PrintFlowStudio\TestData\v2` preflight: PASS, seven categories and all manifest/file
  hashes exact; no result written. Transcript
  `artifacts/pf-accept-a1/accepted-1.18.0-v2-preflight.log`, SHA-256
  `7BF792DAD2A5255AB9DD1BA425A02020B1F29663259349244480DE53BD5F4C62`.

Pair `2851ad7b-8a02-422d-8f3f-c7b615d5f45d` reverified after the new build and remains unchanged;
its receipt SHA-256 is still
`AA97C17617FB2D4BC55A47532986995D77911808ACE8C61B43BB60F96B98F70E`.

The current desktop was not eligible for the one fresh complete live run. At approximately
`2026-09-15T15:00:42+12:00`, Meitu 7.8.8.2 PID 11484 had a responding `美图秀秀` window, but
Photoshop PID 1488 had the unrelated document
`Faileaso Lualua Vaeai_Lowback_A4.tif @ 33.3% (图层 1, CMYK/16)` open. The required exclusive,
settled and document-free condition was therefore false. Nothing was closed, saved, activated,
launched or dismissed. No live RunId/InvocationId, readiness, lease, standard-set result or
Operator review was created. Offline gate status is **RESTORED**; A1 is **AWAITING DESKTOP
AVAILABILITY**, not blocked by another Product defect.

Resume only after one current explicit confirmation that the desktop is exclusive and both Meitu
and Photoshop are settled, document-free and modal-free. Reobserve the windows, then use the new
pair receipt and candidate with the normal wrapper against v2 exactly once: no category filter,
executable override, diagnostic-unbound mode, fake adapter, extra lease, build or restore. Stop at
the actual seven-category A1 result or genuine Operator review boundary. Earlier artwork decisions
do not transfer.

Routing: policy v2.3, `EXECUTE_HANDOFF`, `CONTINUE`, explicit RouteOffset `-1`. Test correction
NormalRoute `gpt-5.6-terra/medium`, requested target `gpt-5.6-terra/low`; operational evidence
NormalRoute `gpt-5.6-sol/high`, requested target `gpt-5.6-sol/medium`; adjustment `APPLIED` in both
route calculations. This context could not perform or verify a real switch, so
`MODEL_SWITCH_UNAVAILABLE`, ActualRoute `UNVERIFIED`; the available context remained safe. No
reviewer was used; bounded self-review only, with no independent acceptance/release approval.

## 15 September 2026 — exact baseline accepted; continuation stopped at first provider-check failure

The current user instruction `PF-ACCEPT-A1 — Exact Baseline Acceptance and Authorized
Continuation` accepted exact preset `printflow-workstation-v1` `1.18.0` at
`D:\PrintFlowStudio\Baseline\workstation-v1\preset\printflow-workstation-v1.18.0.json`, SHA-256
`8484F0AA18728FAC58B58ACD8E811C7058743B61271506647179CA0D0A6C8E0F`, within the existing
qualification scope and limits. The decision and its observed source times
`2026-09-15T14:28:53.7036758+12:00` / `2026-09-15T02:28:53.7086167Z` are recorded only in the
existing external checklist. The frozen manifest, candidate, qualification record and accepted
1.17.0 rehashed exactly and were not changed.

Starting HEAD `7bfe8b6b96fca2db8efac7d9d7dfd95fb28a73e5` was clean and proved to descend from the
proposal's recorded `bd8f2e28d48128cd9a101ab775147c415fd7c5f0`; the only intervening changes
were the expected A1 PLAN/HANDOFF proposal commit. Commit
`4fb596b320f33be5d1d662fc70da9350cd9f092c` changes only the configured preset Version, relative
Path and ExpectedSha256 (the Id was already exact). Direct JSON/path/hash verification resolved
the selector to the accepted read-only bytes.

The controlled producer then created and VerifyOnly-verified pair
`2851ad7b-8a02-422d-8f3f-c7b615d5f45d` from source `4fb596b`, SDK `10.0.400`, with zero build
warnings/errors. Receipt SHA-256 is
`AA97C17617FB2D4BC55A47532986995D77911808ACE8C61B43BB60F96B98F70E`; its embedded input and
harness inventories and both product-assembly inventories are retained with the harness/candidate.

Continuation stopped at the first actual configuration/provider-check failure. Paired-harness
vstest run `2e7c4948-df3d-4b46-84af-7cb4432859b0` ran only
`PrintFlow.Tests.Integration.Preset`: 88 passed, 2 failed, 0 skipped. The first failure was
`WorkstationPresetResizeContractEvidenceTests.Manifest_reverifies_every_inherited_and_resize_evidence_entry`
at line 54: the manifest actually reported `1.18.0`, while the test hard-codes `1.17.0`. The other
failure was the same class's `Configured_workstation_preset_is_the_immutable_v1_17_contract` at
line 27. Exact preserved TRX:
`artifacts/pf-accept-a1/accepted-1.18.0-configuration-checks/configuration-provider.trx`, SHA-256
`C47FA4B18B856DB130BD6DA014BE2E35940FA5B75634358B25D48FA8E554389C`.

Per the acceptance boundary, no code was changed and the unchanged conditions were not retried.
Offline v2 preflight was not run, no current live-desktop confirmation was requested, and no
application/readiness/lease/standard-set operation occurred. The scoped configuration commit,
fresh failed-check evidence and pair are preserved. A1 is **BLOCKED AT FIRST CHECK FAILURE**;
standard-set status is **NOT RUN**, not Passed. Any correction to the version-specific provider
evidence tests requires a new, separately authorized code-change cycle before acceptance resumes.

Routing: policy v2.3, `EXECUTE_HANDOFF`, `CONTINUE`; explicit RouteOffset `0`. NormalRoute,
RequestedRoute and ExecutionTarget were `gpt-5.6-sol/high` for safety-sensitive bound acceptance
evidence, `UNCHANGED`. This context could not verify a real switch, so `MODEL_SWITCH_UNAVAILABLE`
and ActualRoute `UNVERIFIED`. No independent acceptance or release approval is claimed.

## 15 September 2026 — final-form 1.18.0 exact-hash proposal prepared

Starting HEAD was verified as `bd8f2e28d48128cd9a101ab775147c415fd7c5f0`; no reset was
performed. The frozen candidate and its qualification record rehashed exactly as supplied:
`358FE9EAC7F634CA0B09875BE4AE918110702DE112FC3F5897021491C6A4AE09` and
`DD61BC655536300C98FC22E93E043AE9FD9FC55247C5950AEB999D96C8B35D29`. Both remain read-only
and unchanged, as does accepted 1.17.0.

A separate final-form manifest is now frozen read-only at
`D:\PrintFlowStudio\Baseline\workstation-v1\preset\printflow-workstation-v1.18.0.json`, 26,996
bytes, SHA-256 `8484F0AA18728FAC58B58ACD8E811C7058743B61271506647179CA0D0A6C8E0F`.
Candidate-to-final semantic comparison proves exactly three finalization changes:
`presetVersion` `1.18.0-candidate` -> `1.18.0`, `status` `CANDIDATE_UNACCEPTED` ->
`ACCEPTED_IMMUTABLE`, and the final-form `createdAtLocal`. The latter status is the existing
final-manifest convention inside the proposed bytes, not an acceptance decision. The qualified
Meitu identity, qualification state and all unrelated operating/safety contracts are unchanged.

Offline validation passed: JSON serialization; 30/30 `sourceManifestIntegrity` entries; 55/55
path-bound references across the manifest and nested qualification record; retained exact pair
`ecadbf13-a74a-4b90-9a18-e9a26479b6bb` VerifyOnly; and direct task-local loads through that pair's
`WorkstationPresetProvider` and `PresetMeituBaselineProvider`. The latter returned exact Meitu
7.8.8.2 path/hash. Repository `appsettings.json` remains byte-identical at SHA-256
`059E73E413A5EC7D1975CD87B61F540FA7F8F1DF2B0618C1FCD7F441D7B6B0F5` and still selects exact
1.17.0. No retained build output was modified. Detailed local receipt:
`artifacts/pf-accept-a1/final-form-1.18.0-proposal.json`, SHA-256
`7033A5B115C4E080E2F38D16D68DC310B60B01EDEFF1C93A6986066DC097B082`.

The existing checklist now carries the exact-hash request and still says
`NO DECISION RECORDED`. Overall state is **PREPARED / UNACCEPTED / NOT CONFIGURED**. Stop here:
no configuration, A1, live readiness, application/lease operation, standard-set run,
revalidation, build pair, full suite, artwork decision transfer, Jira change, signing, push,
install or deploy was authorized or performed. CandidateProblems, historical results, approvals
and the operator prompt bundle remain unchanged.

Routing: policy v2.3, `EXECUTE_HANDOFF`, `CONTINUE`. NormalRoute and RequestedRoute were
`gpt-5.6-sol/high` for safety-sensitive exact-hash preparation; explicit RouteOffset `0`,
AdjustmentResult `UNCHANGED`. ExecutionTarget was `gpt-5.6-sol/high`; the current context could
not perform or verify a real model switch, so `MODEL_SWITCH_UNAVAILABLE`, ActualRoute
`UNVERIFIED`; safe continuation was retained. Post-preparation documentation was re-evaluated as
bounded checkpoint work, with the same correctness boundary. Self-review only; no independent
acceptance or release review is claimed.

## 15 September 2026 — mainline anchor restored

A1 is again the current mainline. `PF-FIX-MEITU-CONFIRM` is a completed A1-blocker
remediation only; its approved Revision/hash, `ManualResultImport` provenance and failed staging
attempt remain exact historical facts. They are not a substitute for the complete standard set,
and the unreviewed Enhancement is not accepted by implication. R1/R2/R3/R4/A0 remain closed absent
new contradictory evidence.

Two prerequisites gate the next fresh complete v2 run:

1. **Shared ReviewState candidate QA — PASSED.** This correction unblocks A1's requirement that
   hash-bound review evidence reload coherently: the append-only `ReviewDecision` and the reviewed
   Revision's cached `ReviewState` are committed in the same guarded SQLite transaction. Retained
   pair `ecadbf13-a74a-4b90-9a18-e9a26479b6bb`, source `522fca3`, reverified; its exact harness
   passed 11,895/11,895 with all `PRINTFLOW_*` opt-ins cleared. Durable evidence:
   `artifacts/pf-accept-a1/reviewstate-candidate-qa-20260915/summary.json` and `full.trx`
   (TRX SHA-256 `F1E8BC9D02B6FEC68B8E404357F87447DCE2AC431C804FC12DF4D5440276862A`).
   This is candidate QA, not A1 acceptance. Independent review was not available in this context;
   record `SELF-REVIEW ONLY / INDEPENDENT REVIEW NOT COMPLETED` rather than claiming one.

2. **Meitu 7.8.8.2 candidate-baseline route — PREPARED, final preset acceptance still required.**
   Another `-MeituExecutablePath` run would necessarily retain `CandidateProblems` and could not
   support A2. Do not run the complete set that way. The existing preset contract already defines
   the upgrade route: use a new versioned candidate derived from immutable 1.17.0, changing only
   the explicitly justified Meitu identity/evidence values; validate the candidate before signing;
   then obtain an explicit operator decision on the exact proposed version, manifest hash and
   evidence scope. Until that decision, 1.17.0 and repository `appsettings.json` remain unchanged.

Candidate qualification reused, without replay or transfer of approval, the already-preserved
7.8.8.2 compatibility facts: initial derived-baseline readiness, the completed guarded Enhancement
export, and the exact approved Background Removal cutout from `PF-FIX-MEITU-CONFIRM`. The cutout is
evidence about that exact object only. The operator response `It pass. Please prepare` passes only
the displayed Enhancement SHA-256
`6348E70A06441930520006EA3753254664D69B43218CB7D84D75B94BD81FB776` for candidate-baseline
qualification; it is not approval of a future output or the historical failed set. Any genuinely
new candidate output gets a new identity and no inherited decision.

Prepared local candidate `printflow-workstation-v1` `1.18.0-candidate` is frozen read-only at
`D:\PrintFlowStudio\Baseline\workstation-v1\preset\printflow-workstation-v1.18.0-candidate.json`,
SHA-256 `358FE9EAC7F634CA0B09875BE4AE918110702DE112FC3F5897021491C6A4AE09`, status
`CANDIDATE_UNACCEPTED`. It differs from exact 1.17.0 only in candidate/supersedes metadata, the
proposed Meitu version/path/hash, and one qualification block/integrity entry. All 30 integrity
entries match; exact-pair `WorkstationPresetProvider` and `PresetMeituBaselineProvider` loads pass.
The qualification record is also frozen read-only, SHA-256
`DD61BC655536300C98FC22E93E043AE9FD9FC55247C5950AEB999D96C8B35D29`.

After explicit candidate-baseline acceptance only: write/freeze the new immutable preset, update
the configured version/path/hash, perform its non-live integrity/readiness prechecks, build and
verify one fresh source-bound pair, and execute one fresh complete `printflow-regression-v2` run
without `-MeituExecutablePath`. That run must have no `CandidateProblems`, exactly seven cases,
actual operator decisions for its own reviewable outputs, coherent readiness/lease cleanup and the
normal evidence bindings. Return to A1 and stop at its verdict. A2 production revalidation and A3
operator-facing workflow remain separate later stages and are not authorized by this plan update.

No accepted preset, appsettings pointer or final preset-acceptance decision was created by this
checkpoint. No live application, recovered Session, standard-set case, A2/A3 command or Jira state
was operated. The exact unsubmitted decision boundary is retained at
`artifacts/pf-accept-a1/meitu-7882-candidate-baseline-checklist.md`.

14 September 2026; host DESKTOP-0BG8884; canonical checkout D:\Repositories\printflow-Studio,
master. Initial HEAD a2cee64872deddec17213298b58edd433079e3a7. Only the operator-owned
printflow-remediation-prompts/ bundle was untracked; preserve it.

## Selected acceptance freeze

Select and retain pair ef6182db-6f48-4ed2-ae8f-c4e52307ca9e, built from
ce299546e593cbc4c9b8dc59bd6aff9a9fc41cb8. HEAD differs only in four A0 closure documents.
A0 HANDOFF, PLAN, report and dated R1 follow-up close the origin prerequisite for this pair;
older R4 open statements are historical. No rebuild, reset, recertification or output move.

- Root: D:\Repositories\printflow-Studio\artifacts\pf-audit-a0\build-pairs\ef6182db-6f48-4ed2-ae8f-c4e52307ca9e
- Original receipt: root\build-pair.json; SHA-256 E0373C56D015FED9E09D8B0F77C55B026CAE40ED3B685ABFB899709944077FBF.
- Original harness: root\harness; PrintFlow.Tests.dll SHA-256 777279AB5451E0B22F4F278754E3AE448ED2F5E5FCC50C39AA5F2DDD202CE400.
- Candidate: root\candidate. Retain both inventories and receipt through review/publication.
- Verified DotnetPath: C:\Users\admin\AppData\Local\Microsoft\dotnet\dotnet.exe; receipt SDK 10.0.400.
- Repository appsettings.json SHA-256 059E73E413A5EC7D1975CD87B61F540FA7F8F1DF2B0618C1FCD7F441D7B6B0F5.
- Workspace D:\PrintFlowStudio; Production adapters; preset printflow-workstation-v1 1.17.0,
  Baseline\workstation-v1\preset\printflow-workstation-v1.17.0.json,
  measured SHA-256 A2E1936B355C28CDC9905EBF63107A7B4229B71D7586B3C304B7F284B59FCFA9.
- Set printflow-regression-v1, v1/schema 2; D:\PrintFlowStudio\TestData\v1.

## Procedure and criteria

Re-read original CSV C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv,
sixth data row / Work Item ID 11005 mapped to SCRUM-11065. It requires the seven fixed
categories, expected paths/properties, local data and repeatable automated/workstation/upgrade
testing; it does not require automatic fresh launch. Current installer runbook prerequisites
explicitly require Meitu open on its clean start page and Photoshop open, settled and document-free.
Its start-from-nothing advice is a way for the operator to prepare, not a required runner launch.

Chosen mode: operator-prestarted, settled applications; attach at entry. Actual startup mode is
NOT OBSERVED until the live execution. Unchanged Meitu/Photoshop processors select attach for one
accepted running executable, refuse multiple instances, and launch if absent. There is no AttachOnly
switch. Verify both apps first; do not use a cold-launch recovery loop if the condition changes.
The runner performs its own readiness and real canonical lease checks; no extra outer lease.
ProductionRevalidation is the sole supported bootstrap omission. No normal-App E2E claim.

Supported read-only pair verification succeeded; expected receipt hash matched. Static wrapper
preflight exited 0: seven categories and every input hash matched; no result.json was written.
Local evidence: artifacts/pf-accept-a1/verified-pair.json and preflight.log.

Next, obtain one current explicit exclusive live-window confirmation, observe accepted executable
identities/PIDs/start times, visible clean app windows, no documents/modal/crop/IME intervention or
competing controller, and preserve that observation. Leave R4's old wrapper and descendants alone.
Then save/clear inherited PRINTFLOW_* controls process-locally, preserve Windows PowerShell module
resolution, and invoke the original supported wrapper once with a fresh RunId, BuildPairReceipt and
CandidateInstallFolder only. Use paired vstest without build/restore, no category filter or diagnostic
mode. Restore inherited environment; retain log, exits and actual result paths (do not invent TRX).
Verify invocation/origin/binding, exactly seven cases, assertions/artifact hashes, readiness/probe
unwind and original pair after execution. Hand off exact unresolved visual questions/artifacts/hashes;
never manufacture Operator choices. No blind retry. Stop after A1.

## Routing and boundaries

Installed policy C:\Users\admin\.codex\workflows\development-routing.md v2.3 (CODEX_HOME was
unset; conventional installed home used). EXECUTE_HANDOFF, CONTINUE. NormalRoute gpt-5.6-sol/high
for bound operational evidence; explicit RouteOffset -1; RequestedRoute/desired ExecutionTarget
gpt-5.6-sol/medium, APPLIED in planning. No source/safety mechanism changes justify a higher floor.
Current-context MODEL_SWITCH_UNAVAILABLE; safe continuation, ActualRoute UNVERIFIED; no runtime
downgrade claimed. Re-evaluated after preparation with the same operational scope. Self-review only
at this preparation checkpoint, no independent acceptance approval.

No full suite, build/restore, production revalidation, publication script invocation, normal Product
launch, A2/A3, Adobe repair, preset/assets changes, process cleanup, branch/worktree/clone, reset,
amend/rebase, push/install/deploy, attribution trailer or Jira mutation. Historical failures remain.

## Current live-window attempt — prerequisite mismatch

The user replied, "Please continue. Meitu open and ps ready." Accepted as confirmation of the
preceding exclusive attach-start window question; no repeated permission request is needed.
Passive process/window inventory then found Meitu PID 21884 running from
C:\Users\admin\AppData\Local\MeituApp\XiuXiu\7.8.8.2\XiuXiu.exe, started
2026-09-14T14:22:42.5489744+12:00. The preset requires version 7.8.7.5 at the corresponding
7.8.7.5 path. That executable exists, but no running accepted instance was observed.
Photoshop PID 25696 at D:\Adobe Photoshop CC 2019\Photoshop.exe started
2026-09-14T11:46:41.877468+12:00; a titled window exists. Its settled/document-free state was
not fully inspected after the earlier Meitu identity prerequisite failed.

Stopped before wrapper invocation, RunId claim, readiness or lease acquisition. A wrapper start
could launch the absent accepted instance, which would not meet the selected attach-start procedure.
No app was activated, closed, launched or repaired. The pair reverified successfully after this
observation. Raw local evidence: pre-run-processes.json and meitu-identity-mismatch.json under
artifacts/pf-accept-a1/. Operator next action: prepare the accepted 7.8.7.5 executable normally,
settled at its clean page, and report ready. Do not change preset authority to accept 7.8.8.2.

## User-authorized runner change supersedes the version stop

The user subsequently requested continuing on 7.8.8.2 and explicitly instructed: "change the
runner code, remove this restriction, then continue." This authorizes the runner implementation
and necessary focused build/test/new-pair work; the original operational-only/no-rebuild boundary
cannot execute newly edited code. All other production/publication/A2/A3 and preservation boundaries
remain. Original ce29954 pair is retained, not modified or relabelled as the changed harness.

Implementation: explicit -MeituExecutablePath for a new bound standard run. The paired harness
creates meitu-override-preset.json inside its already-claimed run folder from the hash-verified
original preset, pins the selected executable's actual version/path/SHA-256 and marks the snapshot
REGRESSION_EXECUTABLE_OVERRIDE_NOT_REVALIDATED. Existing readiness, UI recognition, lease, artifact
and manual-review checks remain. A persistent CandidateProblems entry forbids publication from the
exception run; its output results remain useful but do not establish original frozen-preset A1
acceptance. No src/ code or accepted preset changes. Review retains original binding unchanged.

Tests: focused 66 passed / 0 failed / 0 skipped, log artifacts/pf-accept-a1/override-tests.log.
Old-harness override execution refused before host, exit 2 (old-harness-refusal.log); preflight passed.
No full suite. One fresh-context read-only native reviewer requested gpt-5.6-sol/medium: NormalRoute
Sol High, offset -1, requested Sol Medium; actual metadata UNVERIFIED. Parent remains CONTINUE with
MODEL_SWITCH_UNAVAILABLE. Commit reviewed source, create a fresh controlled pair in the ignored A1
artifact root, freeze it, reobserve current windows, then run the seven-category wrapper once with
the explicit 7.8.8.2 selection. Capture truthful version-exception and actual case/readiness results.

## Executed checkpoint — stop after the failed exception run

Code 920304f produced verified pair b5f2e3b8-d47a-4ece-b914-9260f5eb9fc1; identities frozen in
artifacts/pf-accept-a1/override-freeze.json before execution. One live RunId
a1-meitu-7882-20260914-143954-d12383c8, InvocationId 0e65070b-cf80-464e-aeb3-b6321ae25f41.
Host exit 0, wrapper 1, set Failed: 2 passed / 5 failed. Initial readiness attached Meitu21884 and
Photoshop25696, probe cleanup succeeded; downstream readiness was lost after unknown Meitu cutout
state. Canonical physical lease observed Free after the run. No blind retry or manual unwind.
Full outcome and exact Operator checklist are linked from HANDOFF; source/assets/old pair verified.

The independent evidence review found stale hard-coded 7.8.7.5 application labels in two case
returns. Source strings were corrected to generic Meitu XiuXiu for future runs only; no raw result,
retained harness, pair or outcome was changed. This low-impact string correction did not warrant
a build/full-suite/live rerun. Future corrected-code execution requires its own controlled pair.
The report also discloses the reviewer's excess child-review contexts; none operated the desktop.
SCRUM-11065 remains PARTIAL, production revalidation NOT WRITTEN, normal-App E2E NOT EXECUTED,
A2/A3 not begun. Completion here is runner change plus truthful failed-run handoff, not A1 acceptance.
