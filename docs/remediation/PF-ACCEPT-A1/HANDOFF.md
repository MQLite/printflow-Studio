# PF-ACCEPT-A1 — Handoff

**OFFLINE GATE RESTORED — TEST CORRECTION COMMITTED — FRESH PAIR VERIFIED — A1 AWAITING AN
EXCLUSIVE, DOCUMENT-FREE DESKTOP WINDOW**

Actual starting HEAD was `c75c8dee90a747e93ecd8fba0e9e3dbe43257466`; no reset. The accepted
read-only `printflow-workstation-v1` 1.18.0 manifest and exact SHA-256
`8484F0AA18728FAC58B58ACD8E811C7058743B61271506647179CA0D0A6C8E0F` remain unchanged and
configured. Acceptance was not requested or recorded again.

The preserved failure TRX (SHA-256
`C47FA4B18B856DB130BD6DA014BE2E35940FA5B75634358B25D48FA8E554389C`) contained exactly two
failures, classified independently: the named configured-contract test was a stale current
configuration assertion; the manifest traversal was a generic integrity test with irrelevant
1.17.0 lineage/count coupling. Commit `e85f56115cc0cfb5f47c5b38573f2c15be86c88a` corrects the
current appsettings readers to exact 1.18.0 version/path/hash and removes only that generic stale
coupling. Actual manifest hashing remains pinned to the literal accepted hash. Historical and
synthetic 1.17.0 fixtures/identities remain explicit and unchanged; no skips, bulk replacement,
self-comparison or integrity weakening.

Offline results (no full suite): source focused 4/0/0 and original preset/provider slice 90/0/0.
Fresh pair `ee8e0280-4fbe-436c-953a-8d27b347d35e` was built from `e85f561` with zero warnings/
errors and verified. Its receipt SHA-256 is
`52DA695226F1024DF925442EC776E2B0163FBA997CF53A09DBA8EC827996A259`; exact harness test DLL
SHA-256 is `9CD3F752BA06B99D6BDDEC2267FF244F23D1803905082839AAB47F844F7C5F45`. Final vstest checks
through that harness passed focused 4/0/0 and original provider slice 90/0/0. Paired TRX hashes:
`C6E47349393551F55456B18AD61F741F8F9B9B49171BABE563EE8AFF346564EE` and
`355E0B6FAD428139961C1E1A872342FA1EF5D3B0A3DCA87AEBFA560CFFE1F23E` under
`artifacts/pf-accept-a1/accepted-1.18.0-fresh-pair-provider-checks/`.

Explicit v2 preflight passed all seven categories and all recomputed hashes; it wrote no result.
Transcript: `artifacts/pf-accept-a1/accepted-1.18.0-v2-preflight.log`, SHA-256
`7BF792DAD2A5255AB9DD1BA425A02020B1F29663259349244480DE53BD5F4C62`. Preserved pair
`2851ad7b-8a02-422d-8f3f-c7b615d5f45d` reverified unchanged; receipt SHA-256 remains
`AA97C17617FB2D4BC55A47532986995D77911808ACE8C61B43BB60F96B98F70E`.

Live execution did not start because the current workstation window is not document-free.
Meitu 7.8.8.2 PID 11484 has a responding `美图秀秀` window; Photoshop PID 1488 is showing the
unrelated document `Faileaso Lualua Vaeai_Lowback_A4.tif @ 33.3% (图层 1, CMYK/16)`. No app or
document was touched. There is no new RunId/InvocationId, readiness, lease, result, claim or
Operator review. This is **A1 AWAITING DESKTOP AVAILABILITY**, not another Product defect.

Next: after one current explicit exclusive-desktop confirmation with both apps settled,
document-free and modal-free, reobserve and run exactly once with:

```powershell
$receipt = 'D:\Repositories\printflow-Studio\artifacts\pf-accept-a1\build-pairs\ee8e0280-4fbe-436c-953a-8d27b347d35e\build-pair.json'
$pair = tools\regression\New-PrintFlowBuildPair.ps1 -VerifyOnly -ReceiptPath $receipt
$runId = '<fresh-unused-id>'
tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1 `
    -SetRoot 'D:\PrintFlowStudio\TestData\v2' `
    -BuildPairReceipt $receipt `
    -CandidateInstallFolder $pair.CandidateFolder `
    -RunId $runId
```

Do not add categories, executable override, diagnostic mode, fake adapter, outer lease, build or
restore. Do not transfer previous artwork decisions. Stop at the A1 result or actual Operator
review boundary. No A2/A3/revalidation/install/deploy/push/signing/Jira work is authorized.

Policy v2.3, `EXECUTE_HANDOFF`, `CONTINUE`, RouteOffset `-1`. Test work normal Terra Medium ->
requested Terra Low; operational evidence normal Sol High -> requested Sol Medium. Actual route
`UNVERIFIED`, `MODEL_SWITCH_UNAVAILABLE`; safe continuation. No scoped reviewer used and no
independent acceptance/release approval claimed.

**EXACT 1.18.0 BASELINE ACCEPTED AND CONFIGURED — STOPPED AT FIRST PROVIDER-CHECK FAILURE;
V2 PREFLIGHT AND LIVE STANDARD SET NOT RUN**

The current user instruction accepted exact read-only preset `printflow-workstation-v1` `1.18.0`
at `D:\PrintFlowStudio\Baseline\workstation-v1\preset\printflow-workstation-v1.18.0.json`,
SHA-256 `8484F0AA18728FAC58B58ACD8E811C7058743B61271506647179CA0D0A6C8E0F`, and separately
authorized the scoped A1 continuation. The existing external checklist records the source and the
observed local/UTC time. The manifest itself was not changed; the candidate, qualification record
and accepted 1.17.0 remain exact and read-only.

Verified clean starting HEAD `7bfe8b6`; committed only `appsettings.json` selector changes as
`4fb596b320f33be5d1d662fc70da9350cd9f092c` (`config: select accepted workstation preset
1.18.0`). The selector resolves to the exact accepted path/hash and Production mode remains
unchanged. No push occurred.

Fresh controlled pair `2851ad7b-8a02-422d-8f3f-c7b615d5f45d` was created from that commit and
VerifyOnly passed. Pair root:
`artifacts/pf-accept-a1/build-pairs/2851ad7b-8a02-422d-8f3f-c7b615d5f45d/`; receipt SHA-256
`AA97C17617FB2D4BC55A47532986995D77911808ACE8C61B43BB60F96B98F70E`. Preserve the receipt,
embedded inventories, build logs, harness and candidate without patching them.

The first relevant paired-harness check failed, so execution stopped. Vstest RunId
`2e7c4948-df3d-4b46-84af-7cb4432859b0`, filter
`FullyQualifiedName~PrintFlow.Tests.Integration.Preset`: **88 passed / 2 failed / 0 skipped**.
First failure:
`WorkstationPresetResizeContractEvidenceTests.Manifest_reverifies_every_inherited_and_resize_evidence_entry`
at `tests/PrintFlow.Tests/Integration/Preset/WorkstationPresetResizeContractEvidenceTests.cs:54`;
expected hard-coded `1.17.0`, actual accepted manifest `1.18.0`. Second failure was the same
version-specific contract at line 27. Preserved TRX:
`artifacts/pf-accept-a1/accepted-1.18.0-configuration-checks/configuration-provider.trx`, SHA-256
`C47FA4B18B856DB130BD6DA014BE2E35940FA5B75634358B25D48FA8E554389C`.

Do not retry unchanged conditions or edit code during this acceptance execution. Offline preflight
with `D:\PrintFlowStudio\TestData\v2` is **NOT RUN**. No current exclusive-desktop confirmation was
requested because the stop preceded all real application/lease operations. No RunId/InvocationId,
host/wrapper exit, readiness, lease or seven-category result exists for this accepted-preset
attempt. Historical failures and CandidateProblems remain untouched; no Operator decision was
created or transferred. A1 result: **BLOCKED AT FIRST CONFIGURATION/PROVIDER CHECK FAILURE;
STANDARD SET NOT RUN**. A separately authorized correction cycle is required before resuming.

Policy v2.3, `EXECUTE_HANDOFF`, `CONTINUE`, RouteOffset `0`; NormalRoute/RequestedRoute/
ExecutionTarget `gpt-5.6-sol/high`, `UNCHANGED`; `MODEL_SWITCH_UNAVAILABLE`, ActualRoute
`UNVERIFIED`. No independent acceptance/release review was performed or claimed.

**FINAL-FORM 1.18.0 PREPARED — EXACT-HASH ACCEPTANCE PENDING; UNACCEPTED AND NOT CONFIGURED**

Verified starting HEAD `bd8f2e28d48128cd9a101ab775147c415fd7c5f0`; no reset. Frozen
final-form manifest:
`D:\PrintFlowStudio\Baseline\workstation-v1\preset\printflow-workstation-v1.18.0.json`, 26,996
bytes, read-only, SHA-256
`8484F0AA18728FAC58B58ACD8E811C7058743B61271506647179CA0D0A6C8E0F`.

Frozen inputs remain exact and read-only:

- candidate `1.18.0-candidate`: `358FE9EAC7F634CA0B09875BE4AE918110702DE112FC3F5897021491C6A4AE09`;
- qualification record: `DD61BC655536300C98FC22E93E043AE9FD9FC55247C5950AEB999D96C8B35D29`;
- accepted/configured 1.17.0: `A2E1936B355C28CDC9905EBF63107A7B4229B71D7586B3C304B7F284B59FCFA9`.

Semantic diff from candidate is limited to `presetVersion`, `status` and `createdAtLocal`.
`ACCEPTED_IMMUTABLE` is the proposed final-form field required by existing manifest convention;
it does not itself accept the bytes. The later authority remains outside the manifest in
`artifacts/pf-accept-a1/meitu-7882-candidate-baseline-checklist.md`, which asks about the exact
full hash above and records **NO DECISION RECORDED**. Acceptance, if later supplied, does not by
itself authorize configuration or A1 execution.

Offline evidence is PASS: valid serialization; 30/30 source-integrity entries and 55/55 total
path-bound manifest/qualification references; retained pair
`ecadbf13-a74a-4b90-9a18-e9a26479b6bb` VerifyOnly; exact-pair `WorkstationPresetProvider` load as
`printflow-workstation-v1 1.18.0 (8484F0AA1872)`; exact-pair `PresetMeituBaselineProvider` load of
the qualified 7.8.8.2 executable identity. Direct task-local constructor inputs were used;
repository and retained-pair appsettings were not changed. Local receipt:
`artifacts/pf-accept-a1/final-form-1.18.0-proposal.json`, SHA-256
`7033A5B115C4E080E2F38D16D68DC310B60B01EDEFF1C93A6986066DC097B082`.

Stop at the exact-hash request. No Product change, new build pair, full-suite rerun, live readiness,
application/lease operation, standard-set run, revalidation, configuration, A1/A2/A3, artwork
approval, Jira change, signing, push, install or deploy. Preserve all historical results,
CandidateProblems, approvals and the operator prompt bundle.

Policy v2.3, `EXECUTE_HANDOFF`, `CONTINUE`, explicit RouteOffset 0. NormalRoute/RequestedRoute/
ExecutionTarget `gpt-5.6-sol/high`, `UNCHANGED`; `MODEL_SWITCH_UNAVAILABLE`, ActualRoute
`UNVERIFIED`. Self-review only; no independent acceptance/release review claimed.

**A1 MAINLINE — ReviewState candidate QA passed; 7.8.8.2 candidate prepared; final immutable
baseline acceptance is required before one fresh complete v2 run**

## Current mainline checkpoint

The shared ReviewState persistence correction at `522fca3` has passed candidate QA against exact
retained pair `ecadbf13-a74a-4b90-9a18-e9a26479b6bb`. VerifyOnly passed and the pair's full harness
passed 11,895/11,895 with no live opt-ins. Evidence is under
`artifacts/pf-accept-a1/reviewstate-candidate-qa-20260915/`; `full.trx` SHA-256 is
`F1E8BC9D02B6FEC68B8E404357F87447DCE2AC431C804FC12DF4D5440276862A`. This closes the
candidate QA prerequisite for A1's review/reload consistency requirement. It does not accept A1.
Self-review only; independent review not completed.

Do not invoke the full v2 set with `-MeituExecutablePath`: that route correctly writes a persistent
`CandidateProblems` publication prohibition and cannot proceed to A2. Resolve 7.8.8.2 through the
existing candidate-before-signing preset contract. Preserve 1.17.0 and `appsettings.json` until an
operator explicitly accepts the exact proposed final version/hash/evidence scope. Existing 7.8.8.2
readiness, Enhancement/export and exact approved cutout facts support candidate qualification, but
the cutout approval stays bound only to its existing Revision/hash. The operator response
`It pass. Please prepare` passes only the displayed Enhancement SHA-256
`6348E70A06441930520006EA3753254664D69B43218CB7D84D75B94BD81FB776` for candidate-baseline
qualification; no future output inherits that decision.

Prepared and frozen candidate: `1.18.0-candidate`, status `CANDIDATE_UNACCEPTED`, SHA-256
`358FE9EAC7F634CA0B09875BE4AE918110702DE112FC3F5897021491C6A4AE09`, at
`D:\PrintFlowStudio\Baseline\workstation-v1\preset\printflow-workstation-v1.18.0-candidate.json`.
Its 30/30 integrity entries and exact-pair preset-provider loads pass. It is not configured or
accepted. Next authority is preparation of the final immutable 1.18.0 bytes; their new hash must
then receive explicit acceptance before configuration changes or A1 execution.

Only after that explicit baseline decision: freeze/configure the accepted immutable preset, create
and verify a fresh build pair, preflight v2, then execute one new complete seven-category run without
an executable override. Return to A1 for the verdict and stop. A2 and A3 remain separate execution
and authorization stages.

No accepted preset, configuration change, live run or final preset-acceptance decision was made in
this checkpoint. The exact unsubmitted operator boundary is
`artifacts/pf-accept-a1/meitu-7882-candidate-baseline-checklist.md`. The failed exception run below
remains historical and retains its `CandidateProblems`.

## Historical exception run — preserved, not current acceptance

**RUN INCOMPLETE — PF-ACCEPT-A1: 2 passed / 5 failed under authorized Meitu 7.8.8.2 exception**

## Current result supersedes the preparation stops below

User explicitly requested changing the runner to continue on 7.8.8.2. Implemented and committed
920304f, focused tests 66/0/0, fresh controlled pair b5f2e3b8-d47a-4ece-b914-9260f5eb9fc1.
Full evidence and exact case table: ../../printflow/audit-a1-standard-regression-set-acceptance.md.
Original ef6182db pair preserved and reverified; no source/preset inputs were silently substituted.

Executed exactly once via the supported wrapper's paired vstest route with -MeituExecutablePath
C:\Users\admin\AppData\Local\MeituApp\XiuXiu\7.8.8.2\XiuXiu.exe. This option removes the old
executable selection restriction for an explicit new bound run, creates a run-local derived preset,
and records actual bytes plus a publication prohibition. Other guards remain; src/ is unchanged.
The new pair is under artifacts/pf-accept-a1/build-pairs/b5f2e3b8-d47a-4ece-b914-9260f5eb9fc1/;
keep its original build-pair.json, harness/ and candidate/ with the original A0 pair.
Receipt SHA-256 189A0E5C0B1FF12ED3AB565FB80F912075E3B762DD0F3833813CDDADCDC813DF.

RunId a1-meitu-7882-20260914-143954-d12383c8;
InvocationId 0e65070b-cf80-464e-aeb3-b6321ae25f41.
Run root D:\PrintFlowStudio\TestData\v1\runs\a1-meitu-7882-20260914-143954-d12383c8.
Host exit 0 (runner Fact completed); wrapper exit 1; result.json Failed. No TRX produced by the
console-only logger. Initial readiness attached both existing processes and completed the full
Photoshop probe cleanup successfully. No cold start. Canonical lease observed Free after execution.

Portrait failed size assertion (1200x1600 was not larger than source), fine-hair failed on unknown
Meitu state, customer design/PSD/PDF then failed on readiness prerequisites. Transparent PNG and
reference TIFF passed. Do not describe downstream prerequisite failures as completed output tests.
Meitu was left on its editor/cutout page; do not claim restoration to a clean welcome screen.

One actual pending visual check: PORTRAIT-VISUAL-001. Exact artifact/question/hash and passed
structural assertions are in artifacts/pf-accept-a1/operator-review.md (unsubmitted checklist).
Human review cannot erase the portrait structural failure. No other reviewable failed-case outputs
exist. No decisions supplied, no review file submitted; no synthetic reviewer.
Later actual decisions use the original run's -RunId and -RecordVisualReview only, omitting new
receipt/MeituExecutablePath. Review retains the original exception and cannot grant publication.

Preserve result.json, readiness.json, case files, claim, derived snapshot and outputs. Local logs:
artifacts/pf-accept-a1/live-command.json, live-run.log, override-freeze.json,
post-run-verification.json, lease-after.json and pre/post UI/process observations.
Portrait ExternalApplications retains the manifest's expected 7.8.7.5 text; actual 7.8.8.2 identity
is in readiness and binding. Keep this label limitation explicit; do not patch historical JSON.
After evidence review, two hard-coded version labels were changed to the generic "Meitu XiuXiu"
in source for future runs only. No rebuild/rerun followed that string correction. Retained live
harness/source remains 920304f; a future run using newer code must use a new controlled pair.

Stop: no retry, further repair, publication/revalidation command, normal-App E2E, A2/A3 or Jira
change. SCRUM-11065 remains PARTIAL. Next work would need a separately scoped examination of the
portrait size expectation and changed cutout state, not automatic rebaseline or another blind run.
Read the report's reviewer-cap deviation disclosure; no final release approval is claimed.

## Operator guidance received after execution

User authorized dismissing unpredictable informational notices with "我知道了" and continuing.
Current Meitu UIA then revealed MainWindow.NoviceGuideWidget.MessageGuideWidget with title
"AI助手来啦", text "选中底图，在图片编辑里也能随时和AI对话改图啦，快试试吧~", and okButton "我知道了".
The supported Computer Use click failed with "coordinate input geometry is unavailable"; dismissal
was not confirmed. Before/after-error trees are retained under artifacts/pf-accept-a1/meitu-notice-*.
This is a concrete candidate explanation for an unexpected state, not proven causation or a repaired
runner. No blanket unknown-dialog handler was added and no failed RunId was resumed or rewritten.
The user's authorization to dismiss this informational notice is recorded; it is not an Operator
acceptance of artwork. The portrait enlargement assertion remains an independent failed boundary.

## Historical preparation checkpoint

Latest checkpoint supersedes the missing-confirmation text below: user said "Please continue.
Meitu open and ps ready." This was accepted as the current attach-start window confirmation.
Passive observation found Meitu 7.8.8.2 (PID 21884), not preset-required 7.8.7.5. The accepted
executable exists at C:\Users\admin\AppData\Local\MeituApp\XiuXiu\7.8.7.5\XiuXiu.exe.
Operator must prepare that accepted instance normally and settle at its clean page before resuming.
Do not change the preset, install/downgrade software, close apps or invoke a cold-launch workaround.
Photoshop PID 25696 has a titled window; full clean-state observation remains to be completed.
No live wrapper/host/run/lease occurred. Pair verified again; raw observations are retained in
artifacts/pf-accept-a1/pre-run-processes.json and meitu-identity-mismatch.json. Resume with fresh
current process/window observations; the confirmation is already supplied for this execution.

## Earlier preparation handoff (historical)

A1 preparation is complete; live execution and human acceptance are incomplete. Read PLAN.md and
../../printflow/audit-a1-standard-regression-set-acceptance.md. Current-session confirmation has
not yet been supplied. Ask once whether the desktop is exclusively available, both accepted apps
are already running/settled and contain no unrelated work, for attach-start with no cold-start claim.
This requirement comes from section 3 of the supplied A1 prompt, not a new approval policy.

Canonical checkout D:\Repositories\printflow-Studio, master; starting HEAD a2cee64 (docs closure).
Original pair ef6182db-6f48-4ed2-ae8f-c4e52307ca9e from ce29954 verified; receipt, harness and
candidate stay at artifacts\pf-audit-a0\build-pairs\ef6182db-6f48-4ed2-ae8f-c4e52307ca9e\.
Receipt SHA-256 E0373C56D015FED9E09D8B0F77C55B026CAE40ED3B685ABFB899709944077FBF.
No rebuild required for documentation changes. Preserve original files for later review and A2.

After confirmation, retain current passive app/window observations and confirm runbook conditions.
Use the actual canonical lease through the runner, never a second outer lease or synthetic store.
Save/clear PRINTFLOW_* opt-ins in the owned process; preserve child Windows PowerShell module paths.
Select a fresh unique RunId only at execution. Invoke:

```powershell
$receipt = 'D:\Repositories\printflow-Studio\artifacts\pf-audit-a0\build-pairs\ef6182db-6f48-4ed2-ae8f-c4e52307ca9e\build-pair.json'
$pair = tools\regression\New-PrintFlowBuildPair.ps1 -VerifyOnly -ReceiptPath $receipt
# $runId must be freshly generated and unused; only after current confirmation/observations.
tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1 `
    -BuildPairReceipt $receipt -CandidateInstallFolder $pair.CandidateFolder -RunId $runId
```

BuildPairReceipt selects the original paired harness for vstest with no build/restore;
CandidateInstallFolder binds its candidate. Do not pass Categories or DiagnosticUnbound.
Capture actual wrapper/host exits, log and actual result paths under
D:\PrintFlowStudio\TestData\v1\runs\<run-id> and host-results\<invocation-id>.
No live RunId, result or review path exists yet. Preflight log and verified pair snapshot are under
artifacts/pf-accept-a1/. A static pass is not a run or Passed set.

After execution verify binding version/origin/invocation, seven-case completeness, artifacts/hashes,
assertions, live readiness and cleanup/release, then reverify retained pair. If only human checks
remain, provide each exact manifest question, category/check ID, artifact path/hash and structural
assertions. Leave decisions unresolved. Only actual later decisions may use:

```powershell
tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1 `
    -RunId '<original-run-id>' -RecordVisualReview '<actual-decisions-file>'
```

That is original-harness reaggregation, not re-execution; supply no replacement receipt. No current
Operator-review artifact handoff can be fabricated before the run. Host success alone is not set
acceptance. Keep SCRUM-11065 PARTIAL until the complete run and actual reviews pass.

No production revalidation/publication command, A2/A3, normal-App launch, cold-start repair, process
tree cleanup, input/preset edits, suite rerun, push/install/deploy or Jira updates. Old R4 wrapper and
all historical evidence preserved. Policy v2.3, offset -1, CONTINUE; actual routing UNVERIFIED,
MODEL_SWITCH_UNAVAILABLE. Self-review only at preparation; no independent approval claimed.
