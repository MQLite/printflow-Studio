# PF-ACCEPT-A1 — Handoff

**RUN INCOMPLETE — PF-ACCEPT-A1: accepted Meitu 7.8.7.5 is not the running instance**

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
