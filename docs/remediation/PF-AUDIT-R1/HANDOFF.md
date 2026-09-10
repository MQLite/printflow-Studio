# PF-AUDIT-R1 — Handoff

## State at handoff

| | |
|---|---|
| Repository | `D:\Repositories\printflow-Studio` |
| Branch | `master` |
| HEAD at slice start | `3f83863521c9b682f02b19ede1bcdb3a86dc60aa` (the audit commit) |
| HEAD at handoff | `d30af787498c5c297caf0f9b007263255f2591d6` — verify it yourself, see §1 |
| This slice's commits | `0ae8087` (fix: code, tooling, tests) and `d30af78` (docs) |
| Working tree | clean, except the untracked `printflow-remediation-prompts\` prompt bundle, which is operator input and is deliberately not tracked |
| Release build | 0 warnings, 0 errors |
| Full Product suite | 11,804 passed / 0 failed / 0 skipped (Release, run on the settled source) |
| Opt-in live tests | NOT RUN, as before |
| Real live acceptance | **NOT RUN** |
| Real production revalidation | **NOT WRITTEN** |
| Jira acceptance statuses | unchanged |

## 1. Read these yourself before doing anything

```powershell
git -C D:\Repositories\printflow-Studio log --oneline -5
git -C D:\Repositories\printflow-Studio status --porcelain
```

The actual test baseline for this slice is **11,804 passed / 0 failed / 0 skipped**, recorded in
`docs/printflow/audit-r1-regression-evidence-integrity.md` §9 and taken from the run itself. Do not
carry forward the audit-era 11,779 figure: it predates this slice's 25 tests.

Do not reset to the audit commit because HEAD has moved. It is supposed to have moved.

## 2. What changed, as contracts

### `RegressionEvidenceBinding` — new, `evidenceBindingVersion = 1`

Lives on the run result (`tests/PrintFlow.Tests/Regression/StandardRegressionSetRun.cs`). Carries
the invocation identity, the harness and candidate Product assembly identities, the preset digest,
the observed Windows build, the accepted Meitu and Photoshop digests, the set's per-manifest digests
and a derived set content digest. Written by the run that tested those facts; read by the review
path and by the revalidation writer.

A run result without it is readable history and cannot produce a pass.

### `ProductionRevalidationRecord` — schema 1 → 2

`src/PrintFlow.Infrastructure/Verification/ProductionRevalidation.cs`. New top-level
`productAssemblies[]` (name, sha256, buildIdentity). `standardRegressionSet` gained `runId`,
`invocationId`, `evidenceBindingVersion` and `setContentDigest`.

Schema 1 records are parsed, readable, and block with their own diagnostic. They are never upgraded
in place.

### `ProductBuildIdentity` — new, production side

`src/PrintFlow.Infrastructure/Verification/ProductBuildIdentity.cs`. The single definition of what a
PrintFlow candidate is: four assemblies, each with a SHA-256 (byte identity) and an
`AssemblyInformationalVersion` (build identity). `FromFolder`, `Running`, `CompareBytes`,
`CompareBuildIdentity` — all pure except the two readers. The composite `Fingerprint` is diagnostics
only and decides nothing.

**Read the class comment before changing it.** The reason there are two identities rather than one is
measured, not stylistic: a RID self-contained publish and an ordinary Release build of one commit
produce different bytes and the same build identity.

### `RegressionExecutionClaim` — new, test side

`tests/PrintFlow.Tests/Regression/RegressionExecutionClaim.cs`. `Stake` is the atomic claim
(`FileMode.CreateNew`). It never deletes. It is the first thing the run does.

### Script exit codes

`Invoke-PrintFlowStandardRegressionSet.ps1`

| Exit | Meaning |
|---|---|
| 0 | preflight-only, or the host completed and this invocation's own result says Passed |
| 1 | the host completed, the result is this invocation's, and the set did not pass |
| 2 | preflight failed; nothing was started |
| 3 | the host completed and wrote no result |
| 4 | the run identity already has a destination; nothing was reused or deleted |
| 5 | the result on disk belongs to another execution |
| other | the test host's own exit code — the run did not complete |

`Set-PrintFlowProductionRevalidation.ps1`

| Exit | Meaning |
|---|---|
| 0 | validated and recorded; Production may resume |
| 2 | recorded honestly as not passing; Production stays closed (this is the deliberate revocation path — no `-StandardRegressionSetResult`, or a validly bound run that did not pass) |
| 3 | **refused.** Nothing written; any existing active record untouched. Covers input that cannot be bound to this installation, a supplied path that does not resolve, and a run concluded by a synthetic review |

The distinction between 2 and 3 is load-bearing: omitting a parameter is the operator deliberately
closing Production, while mistyping one is input the script cannot read. Do not collapse them.

## 3. Safe commands

All of these are safe on this workstation: none launches Meitu, Photoshop or Maintop, and none
writes to the real production workspace.

```powershell
# The evidence-chain protocol tests. Synthetic sets, synthetic installation, stand-in test host.
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test tests\PrintFlow.Tests\PrintFlow.Tests.csproj `
    -c Release --filter "FullyQualifiedName~RegressionEvidenceIntegrityTests"

# The revalidation and regression contract tests.
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" test tests\PrintFlow.Tests\PrintFlow.Tests.csproj `
    -c Release --filter "FullyQualifiedName~ProductionRevalidation|FullyQualifiedName~Regression"

# Clean Release build.
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build PrintFlowStudio.sln -c Release

# Layer 1 of the real set: static, offline, opens no application, writes no result.
tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1 -PreflightOnly
```

**NOT safe without the workstation prepared and the operator's agreement** — it drives real Meitu
and real Photoshop in the foreground:

```powershell
tools\regression\Invoke-PrintFlowStandardRegressionSet.ps1        # the full fixed-workstation run
```

The .NET 10 SDK is per-user and the machine-wide 8.x on `PATH` shadows it, which is why every command
above names `dotnet.exe` by full path.

## 4. Prerequisites for R2

R2 is the workstation lease slice (audit finding F2). Nothing in R1 blocks it. What R1 changes that
R2 should know:

1. **Do not write a production revalidation to unblock anything.** R1 deliberately obtained none, and
   R2 changes Product code, which changes the candidate bytes and would invalidate any record written
   now — correctly.
2. **Product code must keep out of the test assembly.** The shared contract is in
   `PrintFlow.Infrastructure`; the test assembly references it. If R2 needs a fact bound into
   evidence, add it to `RegressionEvidenceBinding` and raise `CurrentVersion`, then teach
   `Test-ProposedAttestation` the new version. Both sides refuse an unknown version, so a bump is
   safe and a silent divergence is not possible.
3. **Environment variables are process-global in the test host.** Anything that reads
   `PRINTFLOW_REGRESSION_*` belongs in `EnvironmentVariableCollection`, or a unit test can start a
   run that drives Photoshop. `StandardRegressionSetWorkstationSmoke` and
   `RegressionEvidenceIntegrityTests` are already in it.
4. **The claim is the run's first action.** If R2 or R3 adds setup before it, a refused identity
   would start doing work before refusing. Keep `RegressionExecutionClaim.Stake` first. Nothing
   expires a claim, so an interrupted run has spent that `RunId` — deliberate, and documented in the
   runbook §7.3.
5. **`environmentReadinessPassed` is still an operator switch, on purpose.** The run proves live
   readiness and writes `readiness.json`, but that proof is not bound into the record, because the
   brief requires operator attestation to stay distinct from tool-verified facts. If a later slice
   wants to bind it, that changes what opens Production and needs its own scope — do not fold it into
   R2 or R3 as a side effect.
6. **`ForStandardRegressionRun` still suppresses exactly one check.** It was not broadened here and
   should not be.

## 5. Prerequisites for A1 and A2 specifically

A1 runs the real standard set; A2 publishes from it. Two things will bite otherwise:

1. **The installed candidate must be built from the same source revision as the run's host.** The run
   drives PrintFlow from the built repository; the writer requires the installed payload's Product
   assemblies to be the ones the run attested. Install the payload built from the commit the run
   ran at, or publication is refused — with the reason named.
2. **`-CandidateInstallFolder`** must point at that installation if it is not
   `%ProgramFiles%\PrintFlow Studio`. A run that attests no candidate still produces evidence and
   says plainly that no revalidation can follow from it; the wrapper prints that at the end of a
   passing run rather than leaving the writer to discover it.

## 6. Not done, and deliberately

- No live acceptance, no operator review of a real run, no production approval.
- No new revocation subsystem — the existing "run the writer with no result" path is the revocation,
  and it is preserved.
- No signing, certificates or PKI.
- No staged future implementation, no scaffolding for R2/R3.
- SCRUM-11065 / 11123 / 11130 not marked FULL.
