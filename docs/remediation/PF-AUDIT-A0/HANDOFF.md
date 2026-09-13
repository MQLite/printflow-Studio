# PF-AUDIT-A0 — HANDOFF

**PASS WITH NOTES — PF-AUDIT-A0 BUILD-ORIGIN ASSOCIATION VERIFIED WITH EXPLICIT LOCAL-TRUST LIMITS**

A0 is complete. A1's build-origin prerequisite is satisfied for the retained pair below. This is
not release acceptance, a completed standard set, Production authorization or a final candidate
freeze. **Stop before A1/A2/A3.**

## Identity and state

- Canonical checkout: `D:\Repositories\printflow-Studio`, `master`; host `DESKTOP-0BG8884`.
- Initial HEAD: `1c591e986472644446d1112462b688e9c11e022b` (latest R4 closure, not reset).
- Implementation: `8f94f986113acbc60f13d12fcc58bf1371db8338`.
- Preparation correction / actual paired source: `ce299546e593cbc4c9b8dc59bd6aff9a9fc41cb8`.
- Closure commit contains this HANDOFF, PLAN, A0 report and appended R1 follow-up only. Read its
  current hash with `git log -1 --format=fuller`; no self-referential hash or rebuild is required.
- After closure: tracked tree clean; operator-owned `printflow-remediation-prompts/` remains
  untracked. Local proof output stays ignored under `artifacts/pf-audit-a0/`.
- No branch/worktree/clone, amend/rebase, attribution trailer, push, install or deploy.

## Mechanism and consumer locations

One controlled local build pair, receipt version **1**, regression evidence binding version **2**.
The command creates fresh ignored harness and candidate outputs from reviewed committed relevant
inputs, records exact profiles/SDK/locked dependencies and checks input stability before/between/after
builds and just before receipt completion. Failed/pending evidence is not usable origin status.
It never certifies arbitrary old folders from labels or today's git cleanliness.

- `tools/regression/New-PrintFlowBuildPair.ps1`: producer and read-only `-VerifyOnly` entry.
- `tools/regression/PrintFlowBuildPair.ps1`: shared wrapper/publication output and run-origin checks.
- `tests/PrintFlow.Tests/Regression/RegressionBuildOrigin.cs`: loaded-harness/candidate validation.
- `tests/PrintFlow.Tests/Smoke/StandardRegressionSetWorkstationSmoke.cs`: permanent claim first,
  origin next, configuration/composition later. Explicit diagnostic unbound runs stay nonpublishable.
- `tests/PrintFlow.Tests/Regression/StandardRegressionSetRun.cs`: immutable run reference
  `BuildOrigin { ReceiptPath, ReceiptSha256, PairId }`; review retains the original complete binding.
- `tools/regression/Invoke-PrintFlowStandardRegressionSet.ps1`: paired DLL execution via `vstest`,
  no build/restore. Bound review uses the **original run's** verified paired reviewer, avoiding stale
  conventional `bin` readers. No replacement receipt accepted during review.
- `tools/installer/Set-PrintFlowProductionRevalidation.ps1`: original receipt hash/id, retained
  harness/candidate files, run identities and current target candidate checked before active-record
  replacement. Missing/mismatched/substituted/legacy evidence is refused, not enriched.

R1 case completeness, invocation/host verdict, permanent claims, per-artifact integrity, review
history/synthetic refusal and refusal-versus-revocation remain covered. `environmentReadinessPassed`
remains an explicit operator attestation. Product runtime schema/evaluator and candidate-byte checks
are unchanged; the sole `src/` edit corrects identity comments. R4 diagnostic envelopes stay ineligible.

## Verified artifacts

Pair root:
`D:\Repositories\printflow-Studio\artifacts\pf-audit-a0\build-pairs\ef6182db-6f48-4ed2-ae8f-c4e52307ca9e`

- Receipt: `build-pair.json`, SHA-256
  `E0373C56D015FED9E09D8B0F77C55B026CAE40ED3B685ABFB899709944077FBF`.
- Harness: `harness\`; candidate: `candidate\`; intermediate paths separate and fresh.
- `harness\PrintFlow.Tests.dll` SHA-256:
  `777279AB5451E0B22F4F278754E3AE448ED2F5E5FCC50C39AA5F2DDD202CE400`.
- 601 committed input identities; input digest
  `32E37750703A447F6D341D400497D05D6AE4F25EA5FAB8FA67463854E74A359C`.
- 182 actual harness output files inventoried; four Product identities on each side.
- SDK `10.0.400`; dotnet `C:\Users\admin\AppData\Local\Microsoft\dotnet\dotnet.exe`.
- All four Product DLLs have label `0.1.0+ce299546e593cbc4c9b8dc59bd6aff9a9fc41cb8` and
  **different bytes across profiles**. Each side matches its own receipt. Full per-DLL hashes are
  in `docs/printflow/audit-a0-build-origin-closure.md` and `artifacts/pf-audit-a0/build-pair-summary.json`.

These are staged build-proof artifacts, not installed Product files or a real standard-set result.
A later Product/harness change or rebuild requires a fresh pair and matching new run. A docs-only
commit preserves this unchanged association; publication never substitutes latest HEAD for origin.
Retain the original receipt and both output sets through execution/review/publication. Losing them
cannot be repaired by attaching a newly built receipt to an old run.

## Verification and retained failures

| Check | Result |
|---|---|
| Focused consumer tests | 29 passed / 0 failed / 0 skipped |
| Affected architecture/regression/verification/diagnostics/smoke/gate union | 777 passed / 0 failed / 0 skipped |
| Final producer behavioral Pester | 4 passed / 0 failed |
| Controlled Release harness build | exit 0, 0 warnings / 0 errors |
| Controlled Release self-contained win-x64 publish | exit 0, no warnings/errors reported |
| Receipt verification | exit 0 |
| Actual loaded paired-harness verification | 1 passed / 0 failed |

Logs/TRX: `artifacts/pf-audit-a0/` (`origin-consumer-tests.log`, `affected-tests.log`,
`affected-results/affected.trx`, `producer-tests.log`, `controlled-build.log`, `loaded-pair-proof.log`)
and pair `harness-build.log`, `candidate-build.log`, `verification-results/loaded-pair.trx`.

The same-label proof compiles different tiny synthetic sources under the same label, shows the
actual pre-A0 writer at `1c591e9` accepts mismatched harness source, and current consumers refuse.
The existing active synthetic record remains unchanged. Additional checks cover replaced outputs,
receipt substitution, old/unbound runs, actual review preservation and original reviewer selection.

Earlier compile errors and focused 9/28 and 25/28 runs are retained in separate failure logs. The
first real preparation at `8f94f98` refused old `.trx` logs before building; its failed incomplete
pair `943fd426-3203-47c3-b4c4-7e400447620d` remains. Correction `ce29954` precisely distinguishes
those non-input logs and handles Update-only XML items. It was tested and independently re-reviewed
before the successful fresh pair. No failed output was promoted, reset or deleted.

No full suite was rerun: runtime Product behavior was unchanged; affected tooling tests plus actual
controlled builds and consumer proof fit the change. R3 11,836/0/0 and R4 18/18, 758/758 remain
historical, not A0 counts. Live smoke bodies stayed opted out (counted no-ops, not skipped).

## Actual supported preparation and verification commands

```powershell
Set-Location D:\Repositories\printflow-Studio
git branch --show-current
git rev-parse HEAD
git status --short
# Process-local only; does not affect R4's wrapper or application descendants.
Get-ChildItem Env: | Where-Object Name -Like 'PRINTFLOW_*' |
    ForEach-Object { Remove-Item -LiteralPath ('Env:' + $_.Name) }
$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"

# Select/verify the preserved pair without a build:
$receipt = 'D:\Repositories\printflow-Studio\artifacts\pf-audit-a0\build-pairs\ef6182db-6f48-4ed2-ae8f-c4e52307ca9e\build-pair.json'
$pair = tools\regression\New-PrintFlowBuildPair.ps1 -VerifyOnly -ReceiptPath $receipt

# Only when a NEW pair is required, preparation creates both outputs:
# $pair = tools\regression\New-PrintFlowBuildPair.ps1 -OutputRoot artifacts\build-pairs
# $receipt = Join-Path (Split-Path $pair.HarnessFolder) 'build-pair.json'

# Read-only actual loaded-harness check. It does not compose Product services.
$env:PRINTFLOW_BUILD_PAIR_PROOF_RECEIPT = $receipt
$env:PRINTFLOW_BUILD_PAIR_PROOF_CANDIDATE = $pair.CandidateFolder
try {
    & $pair.DotnetPath vstest (Join-Path $pair.HarnessFolder 'PrintFlow.Tests.dll') `
        '/TestCaseFilter:FullyQualifiedName~BuildPairVerificationSmoke' `
        '/Logger:console;verbosity=detailed' `
        "/ResultsDirectory:$(Join-Path (Split-Path $pair.HarnessFolder) 'verification-results')"
    if ($LASTEXITCODE -ne 0) { throw 'Loaded build-pair verification failed.' }
} finally {
    Remove-Item Env:PRINTFLOW_BUILD_PAIR_PROOF_RECEIPT, Env:PRINTFLOW_BUILD_PAIR_PROOF_CANDIDATE -ErrorAction SilentlyContinue
}
```

Focused tests, safe after clearing the inherited opt-ins as above:

```powershell
& $dotnet test tests\PrintFlow.Tests\PrintFlow.Tests.csproj -c Release `
    --filter 'FullyQualifiedName~RegressionEvidenceIntegrityTests'
& $dotnet test tests\PrintFlow.Tests\PrintFlow.Tests.csproj -c Release --no-build --no-restore `
    --filter 'FullyQualifiedName~Architecture|FullyQualifiedName~Unit.Regression|FullyQualifiedName~Integration.Verification|FullyQualifiedName~Unit.Diagnostics|FullyQualifiedName~Smoke|FullyQualifiedName~Gate'
# Windows PowerShell 5 Pester; preserve its built-in module discovery in child processes.
$env:PSModulePath = "$env:WINDIR\System32\WindowsPowerShell\v1.0\Modules;$env:PSModulePath"
& "$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -Command `
    '$r = Invoke-Pester -Script "tools/regression/tests/New-PrintFlowBuildPair.Tests.ps1" -PassThru; if ($r.FailedCount -ne 0) { exit 1 }'
```

The next commands are supported **but NOT EXECUTED or authorized by A0**. They require separate
live/Operator/publication authorization and the runbook's workstation preparation.

```powershell
# Exact paired DLL, no build/restore; drives real applications only under later authorization.
tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1 `
    -BuildPairReceipt $receipt -CandidateInstallFolder $pair.CandidateFolder -RunId '<new-run-id>'
# Original paired reviewer selected from the historical run. No replacement receipt.
tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1 `
    -RunId '<existing-run-id>' -RecordVisualReview '<actual-decisions.json>'
# Only after the genuine run/reviews and explicit readiness attestation, separately authorized:
tools\installer\Set-PrintFlowProductionRevalidation.ps1 `
    -InstallFolder '<actual-target-folder>' -EnvironmentReadinessPassed `
    -StandardRegressionSetPath '<actual-manifests-folder>' `
    -StandardRegressionSetResult '<actual-run-result.json>'
```

## Routing, trust and independent review

Installed Global Development Routing & Context Policy v2.3, explicit offset 0, UNCHANGED. Initial
analysis/review requested Astra High; producer and consumer-test subtasks requested Sol High.
Actual model/effort metadata is unavailable, recorded UNVERIFIED. Parent live switching was unavailable
(MODEL_SWITCH_UNAVAILABLE); no model downgrade is claimed. Cohesive work stayed CONTINUE; independent
review used a fresh native reviewer context with original AC and source, and narrowly re-reviewed
corrections. The reviewer checked the actual receipt, 182 files, candidate hashes and verification
logs at closure and found no remaining actionable issue.

The mechanism trusts local tools/SDK and unsigned evidence against accidental dirty/stale/mixed
artifacts. It is not tamper-proof against an administrator rewriting tools and records, or a source
file changed and restored entirely between stability samples. It is not cross-machine reproducibility,
a PKI, release authority, an App bypass or permission to execute acceptance. The R1 signing-necessity
claim is corrected by a dated append, not by rewriting historical results.

## R4 findings carried forward unchanged

These are inherited R4 findings, not new A0 observations of applications or lease state:

1. **Launch-path finding, unreproduced and not repaired.** A single unwaited COM runtime-fact read
   after a fresh Photoshop launch returned `MK_E_UNAVAILABLE`. The same process later passed the
   identical read twice:
   - **Run 3:** last observed minimized with Code in the foreground, about 61 s before the read.
   - **Run 4:** maximized and in the foreground.

   That refutes only a *persistently* broken registration. Elapsed time since launch is one of
   several uncontrolled variables; the others are Generator startup completing, operator desktop
   interaction, and a window-state change of unknown cause.
   - Reproducing it requires the operator to close Photoshop and permit one fresh-launch run.
   - Any bounded post-launch retry within the existing `LaunchTimeout` is a separate Product change
     needing its own approval.
   - Resolve or explicitly accept it before A1 relies on fresh Photoshop launches.
2. **Meitu window loss** between runs 1 and 2 is unexplained. The operator restored the window.
3. **Harness:** the run 1 PowerShell wrapper may still be waiting on handles inherited by
   Photoshop/Meitu. It was intentionally not stopped and holds no lease (R4 observation).

A0 did not launch, attach to, focus, inspect or stop these applications or their wrapper, operate or
inspect the actual default lease store, or perform any additional R4 diagnostics. Other R4 context
and precondition caveats remain in `docs/remediation/PF-AUDIT-R4/HANDOFF.md` unchanged.

Live operations **NOT EXECUTED**. Real production revalidation **NOT WRITTEN**. No readiness or
standard-set acceptance, real Operator review, preset/Adobe change or Jira status change. Cold-launch
finding **NOT REPAIRED**. Nothing pushed, installed or deployed. **Stop before A1/A2/A3.**
