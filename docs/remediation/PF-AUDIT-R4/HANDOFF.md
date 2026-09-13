# PF-AUDIT-R4 — Handoff

Status: **PASS WITH NOTES — PF-AUDIT-R4 DIAGNOSTIC PROBE LIFECYCLE VERIFIED;
PRODUCTION AUTHORIZATION UNCHANGED**

## R4-A — VERIFIED

- `ProductionRevalidation` is the sole failing automatic check. The ordinary entry's own report is
  `Verified: false`, with every live row Blocked.
- The opt-in, non-authorizing wrapper is commit `d30f63e` (test assembly only; `src/` unchanged).
- Tests: focused 18/0/0; affected union 758/0/0. Release build: 0 warnings / 0 errors.
- Reviewed before live use by one read-only reviewer.

## R4-B — EXECUTED

Four live invocations ran under explicit current-session operator confirmation.

Runs 3–4 followed the operator's "Meitu Restored and Photoshop ready." The agent accepted that message
in place of the fresh three-item confirmation the previous handoff had required. It did not raise
the minimized Photoshop window with the operator before run 3.

| Run | Path | Result |
|---|---|---|
| 1 | Fresh launch | Failed before the probe: `PhotoshopSafeStartingState` `MK_E_UNAVAILABLE` |
| 2 | Attach | **Procedural deviation.** Meitu showed no visible window, as already observed before the run; non-discriminating |
| 3 | Attach | **Passed.** Whole procedure; probe `d9c8b5aa…` through every stage `ProbeCreation` … `CleanupCompleted`; cleanup Succeeded; no failures |
| 4 | Attach, confirming | **Passed** with fresh identities; probe `29b606ff…`, same complete lifecycle; no intervention *(session observation)* |

- **Lease:** acquired and released every run (lock row Passed); global state Free before and after.
- **Workspace:** no probe left behind, and no revalidation record written.
- **Authorization:** `NormalReport.Verified` and `ProductionAuthorised` stayed false throughout. The
  scoped `Verified=true` in runs 3–4 is not App permission and not a standard-regression result.

Read `docs/printflow/audit-r4-photoshop-readiness-diagnosis.md` for the evidence, preconditions,
classification and the unimplemented launch-path correction. Scope is in `PLAN.md`.

## Identity

- Starting HEAD: `fdd1d4c` (tested source `ea12201`). The inherited 11,836/0/0 baseline was not
  rerun.
- Live source: `d30f63e` for runs 1–2; HEAD `fc7e28d` (docs only on top) for runs 3–4.
  `PrintFlow.Tests.dll` SHA-256 `689051D2…E1CA25E6` was identical for all four runs.
- The R4 closure commit changes documentation only.
- SDK 10.0.400 (per-user dotnet). The diagnostic build identity is not an R1 build-origin claim.

## Notes and open items

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
3. **Run 3 precondition:** run 3 began with Photoshop minimized, which only loosely meets "settled
   start screen". Source shows only the probe's `Activate` can restore it; the moment of the restore
   was not observed. Run 4 has no pre-run window or foreground capture of its own; the last capture
   was 42 s earlier.
4. **Harness:** the run 1 PowerShell wrapper may still be waiting on handles inherited by
   Photoshop/Meitu. It was intentionally not stopped and holds no lease.
5. **Unsaved observations:** some run 1–2 observations are session-only and not artifacts; they are
   marked in the report.
6. **Application state:** Photoshop 21000 and Meitu 4904 remain running, responding and visible,
   with Photoshop at its start screen in the foreground. They were left untouched.
7. **R1 pre-A1/A2 build-origin condition:** **open**, carried forward unchanged.

No standard-set result, Operator review, production revalidation, normal-App E2E, install, deploy,
push or Jira status change. Local commits on master only, with no attribution trailer. The
operator-owned untracked `printflow-remediation-prompts/` is preserved. Raw evidence stays local
under the ignored `artifacts/pf-audit-r4/`.

**Stop.** R4 ends here. No repair, A1/A2/A3 or live Jira acceptance was started.
