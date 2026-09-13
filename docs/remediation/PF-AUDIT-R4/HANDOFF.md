# PF-AUDIT-R4 — Handoff

Status: **DIAGNOSIS INCOMPLETE — PF-AUDIT-R4: no Photoshop runtime-fact read against a settled
Photoshop was obtained; the comparison run was blocked earlier by Meitu presenting no visible
top-level window, a condition already observed before that run started.**

## Checkpoints

**R4-A: VERIFIED.**

- `ProductionRevalidation` is the sole failing automatic check. This was confirmed read-only, and by
  the ordinary entry's own `Verified: false` report, in which every live row was Blocked.
- The opt-in, non-authorizing wrapper is commit `d30f63e` (test assembly only; `src/` unchanged).
- Tests: focused 18/0/0, affected union 758/0/0. Release build: 0 warnings / 0 errors.
- Reviewed before live use by one read-only reviewer: no blocking finding; its test weaknesses were
  corrected.

**R4-B: EXECUTED, INCOMPLETE.** Two live invocations ran under explicit current-session operator
confirmation.

- **Run 1 (fresh launch):** earliest failure `PhotoshopSafeStartingState` `MK_E_UNAVAILABLE`.
- **Run 2 (attach):** `MeituLaunchability` — no visible top-level window for pid 4904. This is a
  **procedural deviation**: the blocking Meitu state had already been observed before run 2
  started, so it could not discriminate anything.
- **Both runs:** `LatestProbe` null and no probe stage exists. The lock row stayed Passed (own
  release succeeded), and the global lease was Free afterwards.

Read `docs/printflow/audit-r4-photoshop-readiness-diagnosis.md` for the evidence, classification,
competing explanations and the unimplemented proposed correction. Scope is in `PLAN.md`. The same
reviewer narrowly re-reviewed the final causal and evidence claims; its findings are incorporated.

## Identity

- Starting HEAD: `fdd1d4c` (tested source `ea12201`). The inherited 11,836/0/0 baseline was not
  rerun.
- Live source: `d30f63e`; `PrintFlow.Tests.dll` SHA-256 `689051D2…E1CA25E6`. The R4 closure commit
  that adds these documents changes documentation only.
- SDK 10.0.400 (per-user dotnet). The diagnostic build identity is not an R1 build-origin claim.

## Live-window handoff for the next authorized attempt

1. **Operator prerequisites** (performed deliberately by the operator and recorded):
   - Meitu presents a visible, recognisable welcome window. As last observed, pid 4904 was running
     without one; restoring it is the operator's choice.
   - Photoshop CC 2019 is document-free at its settled start screen with no modal. As last
     observed, at 21:49Z, pid 21000 was responding.
   - No other PrintFlow automation is running.
   - A fresh explicit current-session confirmation of the three R4 authorization items.
2. **Verify the preconditions read-only before invoking.** Meitu must have a visible top-level
   window. If it has none, do not run.
3. **Run once.** Clear inherited `PRINTFLOW_*` and `PF_R*` variables, set
   `PRINTFLOW_READINESS_DIAGNOSTIC=1` process-locally only, then run:
   `dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName=PrintFlow.Tests.Smoke.ReadinessDiagnosticSmoke.Run_the_bounded_readiness_diagnosis" --logger "trx;LogFileName=<run>.trx" --logger "console;verbosity=detailed"`
   - Run it without waiting on inherited console handles.
   - Remove the opt-in afterwards.
   - Save the process, window and probe-folder snapshots as artifacts.
4. **Inspect the printed envelope, not exit 0:** `RevalidationCheckOmitted`,
   `DiagnosticReport.Verified`, the first non-Passed LiveApplication row, the lock row, and
   `Lifecycle.LatestProbe` stages, failures, cleanup and path. Then take a global lease reading
   with the manager's passive `ObserveAsync(null)`.
5. **Interpret the result:**
   - If Photoshop's safe state passes on attach, the launch-timing candidate is supported, and any
     probe stages then localise the round trip.
   - If `MK_E_UNAVAILABLE` recurs, launch timing is refuted.
   - Do not repeat beyond what distinguishes the explanations.

## Open items and state

- The run 1 PowerShell background wrapper may still be waiting on handles inherited by Photoshop
  and Meitu. It was intentionally not stopped. It holds no lease, and its post-steps were captured
  manually.
- The proposed wait for Photoshop's COM object after launch is a separate Product change that needs
  its own approval. It was **not** implemented, and no timing changed.
- R1 pre-A1/A2 build-origin condition: **open**, carried forward unchanged.
- No standard-set result, Operator review, production revalidation, normal-App E2E, install, deploy,
  push or Jira status change. Local commits on master only, with no attribution trailer.
- The operator-owned untracked `printflow-remediation-prompts/` is preserved. Raw evidence stays
  local under the ignored `artifacts/pf-audit-r4/`.

**Stop.** R4 ends here. No repair, A1/A2/A3 or live Jira acceptance was started.
