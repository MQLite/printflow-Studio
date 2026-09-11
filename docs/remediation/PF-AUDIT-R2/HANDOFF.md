# PF-AUDIT-R2 — Handoff

**Status: PASS WITH NOTES — PF-AUDIT-R2 FINAL-SOURCE QA VERIFIED; HISTORICAL ISOLATION DEVIATION RETAINED.** The final-source full suite now passes. The earlier two default-lease acquisitions remain an unwaived historical deviation. The validation narrative below is historical; the final-source closure section supersedes its missing-full-suite limitation. Stop before R3 and live acceptance.

| Item | State |
|---|---|
| Task | PF-AUDIT-R2 only — one workstation automation lease across isolated business databases |
| Repository / branch | `D:\Repositories\printflow-Studio`, `master` |
| Starting HEAD | `584d8685dabd0b835ee12f1ea32c462fcc7ed575` |
| Policy | Installed Global Development Routing & Context Policy 2.3; user `route_offset: 0` |
| Host | Local Windows workstation; SDK 10.0.400 verified through the per-user executable |
| Operator input | Untracked `printflow-remediation-prompts/`; preserve and do not commit |
| Previous full-suite baseline | R1: 11,804 passed / 0 failed / 0 skipped; not an R2 result |
| Real external applications | **NOT RUN** |
| Real production revalidation | **NOT WRITTEN** |
| Jira acceptance statuses | Unchanged |

**Execution deviation:** two existing production-gate tests in the preliminary affected union
used the default lease manager. Their passing gate-refusal assertions prove the actual default
workstation lease was acquired and released twice under R2's acquire-before-gate ordering.
This violated the requested test-isolation boundary and initialized/updated the default lease
store. No real external application ran and no production revalidation was written. Testing was
paused for fixture isolation review. The actual store was not inspected or deleted to hide or
repair the deviation. See the report for details; do not claim all R2 runs used isolated stores.

Read `docs/remediation/PF-AUDIT-R2/PLAN.md` and
`docs/printflow/audit-r2-workstation-automation-lease.md` for the original requirements,
design, entry points, synthetic evidence and limits. Verify current Git state rather than
treating the starting HEAD as a target to reset to:

```powershell
git branch --show-current
git rev-parse HEAD
git status --porcelain
git log --oneline -6
```

## Contracts carried forward

The physical automation lease is temporary permission over the workstation's external
applications; business-DB locks and attempts are correlation/recovery metadata. R1's run claim
is a permanent invocation identity and is never expired or released to recover the lease.
`RegressionExecutionClaim.Stake` must remain before operational setup.

Evidence binding version 1, revalidation schema 2, spent run IDs, failed-host/stale-result
refusals, refused-publication preservation, synthetic-review refusal and Product/test assembly
separation remain required. `ForStandardRegressionRun` omits only the self-referential
revalidation check. `environmentReadinessPassed` remains the existing operator attestation.

**Open pre-A1/A2 condition:** matching informational commit labels do not prove that harness
and candidate were built from identical clean source. This remains a candidate-freeze/publication
condition to resolve before A1/A2. R2 does not implement or waive it, does not call it solved,
and introduces no requirement for signatures.

## R3 boundary

The R2 design introduces passive Free / Busy / Unknown observations and recognition of an
explicit operation's own scope. An own-scope check must retain all other environment checks;
process identity or ambient context is not permission for a different operation. Readiness
cache lifetime, probe-stage diagnostics, Save As identity routes, Crop/IME/Generator behavior
and per-capability Production authorization remain outside R2.

Known Busy refuses external admission but preserves previously obtained live evidence without
refreshing its timestamp or running another probe. Unknown still invalidates it. Internal PDF
preparation uses a narrow gate that rechecks all applicable facts and marks only physical lease
availability Advisory/not required. It never acquires a physical scope or reports that scope
Passed/Owned. The regression bootstrap forwards both explicit own-scope and internal admission
through its normal and without-revalidation delegates; its omission remains revalidation-only.

Do not start R3 or live acceptance from this handoff without a new user instruction. Do not run
mixed old/new automation controllers; old tools do not retroactively obey the cooperative lease.
Reacquiring after controller death says nothing about retained documents/dialogs and must never
auto-resume an interrupted external sequence.

## Validation and delivery

Reviewed code/test commit: `689770a3a667f1a0a29954cfe1d8412cc0f16c69`.
Focused union: **195 passed / 0 failed / 0 skipped**. Clean Release build: **0 warnings / 0 errors**.
The single full suite on that commit: **11,788 passed / 31 failed / 0 skipped**, 11,819 executed,
exit 1, 5m30s. All failures were internal PDF preparation cases (28 workflow / 3 visual contract).
The physical lease had incorrectly included the in-process PDF processor because its mode is
Production. Raw evidence lives under `artifacts/pf-audit-r2/focused/` and
`artifacts/pf-audit-r2/final/`; the full run's source commit is recorded there.

Corrective code/test commit: `99caa61415b45fd0f50ed09076a8fd3cc2fa8ee3`.
The affected union plus both PDF classes passed **228 / 0 failed / 0 skipped** in 41 seconds.
The separate read-only reviewer inspected the seven-file correction and raw TRX/log and found
no remaining actionable issues. Evidence is under `artifacts/pf-audit-r2/corrected-targeted/`.
No second full suite was run; no final-source full pass may be inferred from the earlier run or
targeted results. Opted-out real-app tests remain NOT EXECUTED regardless of runner pass counts.

Final clean Release build on corrective commit `99caa61`: **0 warnings / 0 errors**, exit 0.
Final affected-only rerun against those clean-built assemblies: **228 passed / 0 failed /
0 skipped**, exit 0, 40 seconds. The parent parsed the TRX and verified source/test identity.
Evidence: `artifacts/pf-audit-r2/correction-final/` (`source-commit.txt`, clean/build logs,
`pf-audit-r2-correction-final.trx`, `targeted.log`, `targeted-exit.txt`). This is targeted evidence,
not a settled-source full-suite pass.

The two code commits are followed by a separate local documentation commit for PLAN, report and
this HANDOFF. Final tracked state is clean; only the untouched operator-owned
`printflow-remediation-prompts/` is untracked. Raw evidence is ignored local output. Nothing
was pushed, and R3 or live diagnostics must not start without a new user instruction.

## Current final-source closure — 2026-09-11

Full QA verified on `2f3d830f90489bd243e96f6888c76332d591e785`, `master`:
**11,819 passed / 0 failed / 0 skipped**, actual host exit **0**. Fresh clean Release build
with SDK 10.0.400: **0 warnings / 0 errors**, clean/build exits **0**. This continuation
changed **no Product source, tests or fixtures**; only R2 evidence/docs changed.

Complete-tree acquisition isolation review found no remaining gap. Corrected isolated fixtures
and owned-child controls were reused; live opt-ins were cleared process-locally with the existing
child PowerShell module-path correction. No default-store inspection or operation occurred.
No additional targeted run or independent-review repeat was needed. Independent TRX parsing
matched all individual outcomes, and before/after source/test inputs, assembly hashes and HEAD
matched. Historical failed-suite TRX/log hashes also matched.

Evidence: `artifacts/pf-audit-r2/final-source-qa-20260911/` (fresh TRX, complete log, exit,
SDK/source identity, launcher, isolation audit, independent parser/summary and drift manifests).
The report's final-source closure section records exact distinctions from R1 **11,804/0/0**,
initial R2 **11,788/31/0**, corrected affected **228/0/0** and corrected clean build **0/0**.
The authorization correction permits final settled-source proof; it does not erase earlier failures
or the two actual-default acquisitions. This run is the final-source proof previously missing.

Busy preservation, internal-PDF admission and exact own-scope semantics remain delivered.
R1 build-origin before A1/A2 remains unresolved; Jira acceptance statuses unchanged.
Real apps, real standard-set execution and production-revalidation writes **NOT EXECUTED**.
Policy 2.3 / offset 0 / CONTINUE; requested QA Sol High, docs Luna Low; ActualRoute UNVERIFIED,
MODEL_SWITCH_UNAVAILABLE; no runtime switch claimed. Local documentation-only commit follows;
operator prompt bundle preserved, no push/install/deploy. R2 complete with historical notes;
no next execution authorized. **Stop before R3 and live acceptance.**
