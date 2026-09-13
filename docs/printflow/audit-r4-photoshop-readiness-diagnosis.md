# PF-AUDIT-R4 — Photoshop readiness diagnosis

Primary verdict: **DIAGNOSIS INCOMPLETE — PF-AUDIT-R4: no Photoshop runtime-fact read against a
settled Photoshop was obtained; the comparison run was blocked earlier by Meitu presenting no
visible top-level window, a condition already observed before that run started.**

- **R4-A: VERIFIED.** `ProductionRevalidation` is the sole failing automatic prerequisite. The
  ordinary entry cannot reach the probe. A minimal opt-in, non-authorizing test-assembly wrapper
  was added, reviewed and committed. No production code changed.
- **R4-B: EXECUTED, INCOMPLETE.** Two live invocations ran after explicit current-session operator
  confirmation. Neither reached the Photoshop probe (`LatestProbe` = null in both).
  - Run 1 failed first at `PhotoshopSafeStartingState` (`MK_E_UNAVAILABLE`, 0x800401E3), shortly
    after Photoshop was freshly launched.
  - Run 2 failed at `MeituLaunchability` and did not discriminate anything (see
    [Procedural deviation](#procedural-deviation-run-2)).

Production authorization is unchanged. No standard-set result, operator review, revalidation
record or normal-App E2E was produced or earned.

## Startup and identity

| Item | Value |
|---|---|
| Starting HEAD | `fdd1d4cd2850fa52b4a7d125b46fa64b5b2e5dea` (R3 docs); tested source `ea12201` |
| Wrapper commit (live source) | `d30f63e355dac1e33a49295092ce4ee002d0b8b3` |
| SDK | `global.json` 10.0.400 `latestFeature`; per-user dotnet reports 10.0.400 |
| Inherited baseline | 11,836 / 0 / 0, Release 0 warnings / 0 errors — **not rerun** |
| Runtime | Claude Code, model reported as Opus 5; requested high effort not independently verifiable |

The R3 Release output was byte-identical to `artifacts/pf-audit-r3/final-corrected/assemblies-after.json`
when the R4-A prerequisite observations ran, so those used the R3-verified binaries. After the
wrapper was added, the test project was rebuilt in Release from unchanged `src/` (0 warnings /
0 errors). The Product DLL hashes differ from R3's because this is a new build, not because the
source changed. All five hashes are recorded in
`artifacts/pf-audit-r4/A-proof-corrected/assemblies.json`.

The live runs hashed only `PrintFlow.Tests.dll` at run time
(`689051D210476857C3121CDA1E8481429CED037D68AEAF3194F527D0E1CA25E6`). The Product DLL identity
follows from that earlier record plus `--no-build`. This supports diagnostic reproducibility only;
R1's harness-to-published-candidate build-origin condition remains **open** before A1/A2.

## R4-A — entry and prerequisites

### Actual prerequisites (read-only)

`WorkstationVerificationSmoke.Observe_the_current_accepted_workstation` with
`PRINTFLOW_WORKSTATION_VERIFY=1` (automatic-only; no launch, input or write):

- **Passed (10 of 11 blocking checks):** PresetIntegrity, EvidenceIntegrity (29/29),
  OperatingSystem, Meitu/Photoshop executables, Action artefact, WorkspaceRoot,
  InteractiveSession, DisplayConfiguration, UiCulture.
- **Advisory, non-blocking:** `FilesystemReadOnlyPolicyAdvisory` (16/29 evidence files not
  read-only; SHA-256 matched) and `ExternalApplicationUiLanguage`.
- **Failed:** `ProductionRevalidation`, "(no record)". `D:\PrintFlowStudio\Revalidation\` does not
  exist.

Evidence: `artifacts/pf-audit-r4/A-prereq/passive-observation.log`. On the live day, run 2's normal
report again lists only `ProductionRevalidation` as a blocking failure.

### The ordinary entry, run for its real refusal

`WorkstationVerificationSmoke.Run_the_explicit_live_application_verification` with
`PRINTFLOW_WORKSTATION_LIVE_VERIFY=1`, `-c Release --no-build --no-restore`, exact filter.

It was safe to run before operator confirmation for two source-proven reasons: `RunLiveChecksAsync`
returns before `_live.RunAsync` when automatic checks fail, and composing the lease manager is
inert (R2).

Result: host exit 0 and xUnit Passed, but **`Verified: false`**. All seven LiveApplication rows were
`Blocked` "(not run)" ("Automatic workstation checks must pass before applications are launched."),
and `LatestProbe` was null. This is Branch **B**. Evidence:
`artifacts/pf-audit-r4/A-prereq/ordinary-entry-blocked.log`.

### Wrapper (commit `d30f63e`, test assembly only)

| File | Role |
|---|---|
| `tests/PrintFlow.Tests/Diagnostics/ReadinessDiagnostic.cs` | Opt-in run and the diagnostic-only envelope |
| `tests/PrintFlow.Tests/Smoke/ReadinessDiagnosticSmoke.cs` | Live entry; composes a verifier and gate only |
| `tests/PrintFlow.Tests/Unit/Diagnostics/ReadinessDiagnosticTests.cs` | Synthetic admission-boundary proof |

- **Entry:** `PrintFlow.Tests.Smoke.ReadinessDiagnosticSmoke.Run_the_bounded_readiness_diagnosis`.
- **Opt-in:** `PRINTFLOW_READINESS_DIAGNOSTIC=1`, exact value, ordinal comparison. The guard runs
  before the composition delegate: with no opt-in there is no composition, no store open and no
  external call.
- **Reuse, not reimplementation.** The omission is the existing internal
  `ProductionWorkstationVerifier.ForStandardRegressionRun`. The admission decision is the existing
  `RegressionBootstrapWorkstationVerifier`: it asks the real verifier first and delegates only when
  `ProductionRevalidation` is the single blocking check; otherwise it returns the real refusal.
- **Added allowance:** exactly one new internal caller of that factory, in the test assembly.
  There is no new `InternalsVisibleTo`, no public API and no guard change.
  `The_omission_factory_is_not_reachable_from_the_application` still holds; it asserts
  accessibility and public-factory parameters and does not count callers.
- **Composition:** both verifiers take the App's arguments (`appsettings.json`, `FileWorkspace`,
  the `Evidence` directory, `TimeProvider.System`) and share **one** default
  `SqliteWorkstationAutomationLeaseManager`. There is no `ServiceProvider`, `SessionService`,
  adapter, workflow, persisted cache or business DB.
- **Envelope** (`printflow.readiness-diagnostic.v1`): a fresh `DiagnosticId` and `StartedAtUtc`;
  `RevalidationCheckOmitted` (the bootstrap's own `BootstrapWasUsed`); the unaltered `NormalReport`
  from `VerifiedEnvironmentGate.Read()`; the raw `DiagnosticReport` with its real Verified value,
  checks and Lifecycle; `ProductionAuthorised`, read from the normal report only; and a literal
  `Authority` statement. It carries no set, run or invocation identity, no binding, no attestation
  and no `StandardRegressionSetRunResult`.

### R4-A proof and review

| Run | Result |
|---|---|
| Focused `Unit.Diagnostics` (initial) | 17 / 0 / 0 |
| Affected union: Architecture, Unit.Regression, Integration.Verification, Unit.Diagnostics, Smoke, Gate (initial) | 757 / 0 / 0 |
| Focused, after review corrections | **18 / 0 / 0** |
| Affected union, after review corrections | **758 / 0 / 0** |
| Release build of the test project | exit 0, 0 warnings / 0 errors |

Cases covered:

- No opt-in, including near-miss values, performs zero composition; an opted-in run composes
  exactly once.
- A sole revalidation blocker allows the procedure with Meitu rows retained, and the normal report
  still refuses.
- Five second-failure cases stay blocking, and the omitting verifier is never used even though it
  would verify on its own.
- A verified workstation is diagnosed with no omission.
- A failed diagnosis preserves the probe's stages, last attempted and confirmed stage, cleanup
  outcome, and primary and secondary failures.
- Diagnostic identities are fresh.
- The envelope has none of the standard-set or run shapes.
- At the existing publication boundary, a synthetic `WorkstationVerificationFixture` record whose
  Passed outcome names no run or invocation is refused as "unbound", while the fixture's bound
  record still passes. The writer is unchanged.

Live smoke bodies in the affected union were opted out: **NOT EXECUTED**. No full suite was run,
because this is test-only orchestration and no shared Product composition or authorization
behavior changed.

**Pre-live review.** One separate read-only reviewer (a Claude Code Explore subagent, no edit
tools) found no blocking violation of the ten wrapper properties. It flagged these test weaknesses,
all corrected before commit:

- A vacuous assertion: the "would say yes" stub lacked PresetIntegrity, so it could never verify.
  It now verifies, and that is asserted.
- There was no ban on `StandardRegressionSetRunResult`; one was added.
- There was no compose-exactly-once test; one was added.
- Test literals used real workstation paths; they were replaced with `C:\Fake`.

Accepted non-blocking notes:

- The normal report and the admission decision are two separate passive observations.
- There are two verifier instances (only the omitting one drives applications), and no test pins
  their argument lists together.
- The gate over the omitting verifier would authorize if misused; only `RunLiveChecksAsync` is
  called on it, and it is never stored.
- The stubs mark checks as Immutable, so phase is not modelled.
- The publication test proves an existing evaluator property; the diagnosis has no writer.
- The smoke asserts nothing by design.

## R4-B — live procedure

**Authorization.** One explicit current-session operator confirmation covered the three R4 items:
the desktop is available; Meitu and Photoshop hold no unrelated documents needing protection; and
use of the real R2 lease and accepted binaries for this bounded procedure is authorized. No Adobe,
Generator, Crop, IME, preset, Action or timeout setting was changed.

**Command (both runs).** Inherited `PRINTFLOW_*` and `PF_R*` variables were cleared (none were
present), the opt-in was set process-locally and removed in `finally`, and then:

```
dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj -c Release --no-build --no-restore
  --filter "FullyQualifiedName=PrintFlow.Tests.Smoke.ReadinessDiagnosticSmoke.Run_the_bounded_readiness_diagnosis"
  --logger "trx;LogFileName=<run>.trx" --logger "console;verbosity=detailed"
```

`dotnet` is the per-user 10.0.400 SDK. Global lease observations used the canonical manager's
passive `ObserveAsync(null)` (read-only connection) through a throwaway scratchpad console tool
outside the repository.

**Evidence retention.** Some observations below were made during the session but not saved to an
artifact. Each is marked *(session observation)*.

### Pre-run baseline (2026-09-13 21:34–21:36Z)

- Photoshop and Meitu were not running. Chrome held the foreground *(session observation)*.
- Lease `printflow-studio.external-automation.v1` at
  `%LOCALAPPDATA%\PrintFlow Studio\workstation-automation-v1.db` was **Free** ("no owner")
  (`B-baseline/lease-before.txt`).
- `D:\PrintFlowStudio\EnvironmentVerification\` held six historical retained probes
  (`PF_ENV_PROBE_*.png`, 68 B, written 2026-09-10), listed in
  `B-baseline/environment-verification-before.json`. They were preserved.
- Photoshop CC 2019 Generator logs were copied to `B-baseline/`.

### Run 1 — fresh launch

| Field | Value |
|---|---|
| DiagnosticId | `ad9a9f76b6c94f22b4467dd35f069889`; started 21:36:44Z; report stamped 21:37:20.08Z; TRX end 21:37:20.17Z (35.9 s) |
| Host / TRX | Report and TRX written; xUnit Passed. The redirecting shell did not return (see Harness note) |
| RevalidationCheckOmitted | true |
| NormalReport.Verified / DiagnosticReport.Verified | false / **false** |
| ExternalApplicationAutomationLock | Passed — acquired. Still Passed in the final report, so this operation's own release succeeded: a failed or throwing release replaces the row with "Release failed" (`ProductionLiveWorkstationVerifier.cs` ~245–278) |
| MeituLaunchability / SafeStartingState | Passed — Launched, process 4904 / `KnownWelcome` |
| PhotoshopLaunchability | Passed — Launched, process 21000 (start 21:36:56Z, read from a later process snapshot) |
| **PhotoshopSafeStartingState** | **Failed** — "Photoshop live settings could not be read: 操作无法使用 (0x800401E3 (MK_E_UNAVAILABLE))" |
| PhotoshopColourSettings / TestImageRoundTrip | Blocked — "Photoshop's current document state could not be read." |
| Lifecycle | LatestAttemptAt 21:36:47Z; LastSuccessfulLiveAt null; EvidenceAvailable false; **LatestProbe null** |
| Global lease later | Free at 21:47:00Z *(session observation)* and at 21:49:03Z (`B-run2/lease-before.txt`) |
| Probe folder | No new entry *(session observation; reconfirmed read-only by the reviewer: still 18 entries dated 2026-09-10)* |

### Procedural deviation (run 2)

The prompt permits a repeat only when an observation or permitted change distinguishes competing
explanations, and it requires the accepted clean application states. Run 2 met neither condition.

Before it started, read-only window enumeration at 21:48:13Z *(session observation)* found no
visible top-level window for XiuXiu 4904, and `B-run2/processes-before.txt` recorded `title=''`.
The source already showed that run 2 could not reach Photoshop:

- `RunAsync` checks Meitu before Photoshop.
- Attaching to Meitu waits up to 10 s for a window.
- That wait counts only visible windows with non-empty bounds.

Run 2 was still started, on the reasoning that Photoshop 21000 was settled — responding, with an
enabled main window and no owned modal *(session observation)*. That reasoning covered only the
Photoshop half of the precondition. **Run 2 was non-discriminating by construction.** It is
recorded as a deviation, not as a justified comparison. It acquired and released the real lease
and changed no configuration.

### Run 2 — attach attempt

| Field | Value |
|---|---|
| DiagnosticId | `6db73a6cc57349d5851dbd109680257e`; started 21:49:09Z; TRX end 21:49:23.86Z (14.6 s) |
| Host exit / TRX | 0 / Passed — not the verdict |
| ExternalApplicationAutomationLock | Passed — acquired; still Passed in the final report (own release succeeded) |
| Global lease before / after | Free (21:49:03Z) / **Free** (21:49:24Z) |
| **MeituLaunchability** | **Failed** — "No top-level window belonging to Meitu process 4904 appeared within 10 s." |
| MeituSafeStartingState | Blocked — "Meitu launchability did not pass." |
| Photoshop rows (four) | Blocked — "Meitu did not reach a safe state, so no later application check ran." |
| DiagnosticReport.Verified / LatestProbe | false / **null** |
| Probe folder | No new entry *(session observation; reviewer reconfirmed)* |

Why Meitu's recognised welcome window became non-visible between the runs was not observed and is
unknown.

**Process state.** Before and after run 2, Photoshop 21000 and XiuXiu 4904 had the same pids and
start times and were responding (`B-run2/processes-*.txt`). No close, kill or dialog dismissal was
performed by the operator or the agent. What Meitu-side automation did to its own UI is unknown.

### Classification

In both runs `probe` is assigned only after colour settings pass, so neither run entered the probe
lifecycle. Source confirms "not reached"; no R3 probe stage exists to report. Primary and secondary
probe failures, cleanup and retained probe path therefore do not apply.

**Run 1** is a live safe-state prerequisite failing before the probe. Among the prompt's classes it
is "external exception observed with insufficient causal linkage", with a *supported but unproven*
candidate cause.

**Candidate: a single unwaited COM read too soon after a fresh launch.**

- After `LaunchAsync`, `ReachSafeStateAsync` polls only window identification and
  `GuardedPhotoshopUiDriver.InspectStateAsync`. That inspection uses only Win32 window facts
  (alive, refresh, visible classes, owned dialogs) — no COM.
- `ProductionLiveWorkstationVerifier.RunAsync` then calls `RotPhotoshopRuntimeFactReader.Read`
  **once**. That reader returns this failure marked `isRetryable: true`, but the verifier does not
  retry.
- `CLSIDFromProgID("Photoshop.Application.130")` succeeded; it precedes `GetActiveObject` and
  would have thrown otherwise. So the ProgID is registered, and the object was not in the Running
  Object Table at that moment.
- Timing: the process started 21:36:56Z; the read finished by 21:37:20.08Z (no more than 24 s
  after start); Generator *started* logging at 21:37:28Z (local 09:37:28, UTC+12, per the log's
  GMT+1200 header) and finished loading plugins at 21:37:40Z. The read therefore ended at least
  8 s before Generator's first log line. This does not prove Photoshop was still initialising.

**Competing explanations, not excluded:**

- The ROT entry is invisible because of an integrity-level or session mismatch (not observed).
- Registration depends on activation or foreground; Chrome held the foreground *(session
  observation)*.
- The launch context differs: this Photoshop was launched from a test host whose redirected
  console handles it inherited, unlike the ordinary App.
- Automation registration is broken regardless of timing. The six retained 2026-09-10 probes show
  that some earlier runs, on earlier source, passed this same read. Whether those runs launched
  Photoshop fresh or attached to it is unknown; if they attached, that history fits the timing
  candidate rather than contradicting a broken registration.
- The Generator exception log was unchanged across run 1; its earlier "Unknown JavaScript error"
  entries have no established causal link.

**Run 2** is a Meitu recognition refusal (no visible top-level window) with unknown cause. It is
non-discriminating, as described above.

No earlier hypothesis is rewritten as proven, and nothing here shows earlier failures were
environmental.

### Proposed correction (not implemented)

**Needed permission and prerequisite first.** The operator restores a visible, recognisable Meitu
welcome window (method is the operator's choice) and leaves Photoshop document-free at its settled
start screen. After a fresh confirmation, run the same command once.

- If `PhotoshopSafeStartingState` passes on attach, launch timing is supported, and the run
  continues into the real probe, whose stages then become reportable.
- If `MK_E_UNAVAILABLE` recurs on attach, launch timing is refuted, and the integrity, activation,
  launch-context and registration explanations need a separate observation plan.

**Smallest correction, only if timing is supported and separately approved.** After a *launch*
only, retry the runtime-fact read on `MK_E_UNAVAILABLE` within the existing `LaunchTimeout` budget,
without changing that value; attach keeps its single read.

- Scope: a Product change in `ProductionLiveWorkstationVerifier` or in the Photoshop foundation's
  launch readiness.
- Validation: a synthetic reader that returns `MK_E_UNAVAILABLE` and then success, plus the
  affected union; then one fresh-launch live run and one attach live run.
- Rollback: revert that single commit.
- Not permitted in this slice, which forbids timing changes.

### Harness note (not a Product fault)

The `*>`-redirected PowerShell wrapper in run 1 did not return after the test finished. Photoshop
and Meitu, launched by the test host, inherited its redirected handles. The background task was left
running rather than stopped, so that no process-tree kill could reach those applications. Its
intended post-steps were captured manually. Future fresh-launch runs should not wait on inherited
console handles.

## Surfaces touched

| Surface | What happened |
|---|---|
| Real default lease store | Acquired and released twice by the normal manager. Each operation's own release is proven by its Passed lock row; global Free was observed afterwards. No manual edit or reset |
| Meitu (XiuXiu 7.8.7.5) | Launched once by run 1 (pid 4904) to `KnownWelcome`; attach refused in run 2; left running. Meitu-side input is not instrumented in the report and is **unknown** |
| Photoshop CC 2019 | Launched once by run 1 (pid 21000); no document opened and no probe; left running |
| `D:\PrintFlowStudio\EnvironmentVerification` | No new entries; six historical probes preserved |
| Configured workspace / Evidence | Read for verification only |
| Production business DB, revalidation record | Not touched / not written. Run 2's normal report still says "(no record)", and `D:\PrintFlowStudio\Revalidation` is absent at closure. The wrapper composes no business DB |
| Adobe preferences, Generator, presets, Actions | Not changed; Generator logs were read and copied only |

Private raw evidence stays local under the ignored `artifacts/pf-audit-r4/` directory; SHA-256
values for the envelopes and TRX files are in `evidence-sha256.txt`. The same read-only reviewer
also reviewed these final causal and evidence claims against the raw evidence and source. Its one
blocking finding (run 2's justification) and its non-blocking corrections are incorporated above.

## Carried boundaries

- R2 Busy/Unknown/own-scope/internal-PDF semantics, R3 lifecycle and report semantics, R1
  evidence/publication/run-claim semantics and the operator readiness attestation are unchanged.
- R3's first 11,834/2/0 run and R2's historical default-store acquisitions stay as recorded.
- R1's pre-A1/A2 build-origin condition is **open**. A healthy future R4 probe would not by itself
  make A1/A2 release-ready.
- No standard-set execution, Operator review, production revalidation, normal-App E2E, install,
  deploy, push or Jira status change.
