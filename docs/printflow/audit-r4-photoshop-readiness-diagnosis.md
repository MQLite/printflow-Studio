# PF-AUDIT-R4 — Photoshop readiness diagnosis

Primary verdict: **PASS WITH NOTES — PF-AUDIT-R4 DIAGNOSTIC PROBE LIFECYCLE VERIFIED;
PRODUCTION AUTHORIZATION UNCHANGED**

- **R4-A: VERIFIED.** `ProductionRevalidation` is the sole failing automatic prerequisite, so the
  ordinary entry cannot reach the probe. A minimal opt-in, non-authorizing test-assembly wrapper
  was added, reviewed and committed. No production code changed.
- **R4-B: EXECUTED — scoped diagnostic lifecycle verified on the attach path.** Four live
  invocations ran under explicit current-session operator confirmation.
  - **Runs 3 and 4:** the whole live-check procedure passed, including the complete R3 probe
    lifecycle from `ProbeCreation` to `CleanupCompleted`, with fresh diagnostic and probe
    identities.
  - **Run 1:** after a fresh launch, the procedure failed before the probe at
    `PhotoshopSafeStartingState` with `MK_E_UNAVAILABLE`.
  - **Run 2:** a documented, non-discriminating procedural deviation.

A scoped diagnostic `Verified=true` means only that this diagnosis' checks passed at those moments.
It is **not** normal App permission and **not** a standard-regression result. The normal report
stayed `Verified=false` in every run. No standard-set result, operator review, revalidation record
or normal-App E2E was produced or earned.

**Notes carried with the verdict:**

1. The fresh-launch failure is a supported, unreproduced launch-path finding. It is not repaired.
2. Run 2 deviated from the repeat rule.
3. Meitu's window loss between runs 1 and 2 is unexplained.
4. Run 3 began with Photoshop minimized. That only loosely meets the "settled start screen"
   precondition, and the agent did not raise it with the operator before running.
5. Some session observations are not retained as artifacts.

## Startup and identity

| Item | Value |
|---|---|
| Starting HEAD | `fdd1d4cd2850fa52b4a7d125b46fa64b5b2e5dea` (R3 docs); tested source `ea12201` |
| Wrapper commit (live source) | `d30f63e355dac1e33a49295092ce4ee002d0b8b3`; runs 3–4 at HEAD `fc7e28d` (docs-only on top) |
| SDK | `global.json` 10.0.400 `latestFeature`; per-user dotnet reports 10.0.400 |
| Inherited baseline | 11,836 / 0 / 0, Release 0 warnings / 0 errors — **not rerun** |
| Runtime | Claude Code, model reported as Opus 5; requested high effort not independently verifiable |

The R3 Release output was byte-identical to `artifacts/pf-audit-r3/final-corrected/assemblies-after.json`
when the R4-A prerequisite observations ran.

After the wrapper was added, the test project was rebuilt in Release from unchanged `src/`
(0 warnings / 0 errors). Product DLL hashes differ from R3 only because this is a new build; the
five hashes are in `artifacts/pf-audit-r4/A-proof-corrected/assemblies.json`.

Every live run hashed `PrintFlow.Tests.dll` at invocation, and it was identical in all four:
`689051D210476857C3121CDA1E8481429CED037D68AEAF3194F527D0E1CA25E6`. Product DLL identity follows
from the recorded build plus `--no-build`.

This is diagnostic reproducibility only. R1's harness-to-published-candidate build-origin condition
remains **open** before A1/A2.

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

Evidence: `artifacts/pf-audit-r4/A-prereq/passive-observation.log`. Every live run's normal report
again lists only `ProductionRevalidation` as a blocking failure.

### The ordinary entry, run for its real refusal

`WorkstationVerificationSmoke.Run_the_explicit_live_application_verification` with
`PRINTFLOW_WORKSTATION_LIVE_VERIFY=1`, `-c Release --no-build --no-restore`, exact filter.

It was safe to run before operator confirmation for two source-proven reasons: `RunLiveChecksAsync`
returns before `_live.RunAsync` when automatic checks fail, and lease-manager composition is inert
(R2).

Result: host exit 0 and xUnit Passed, but **`Verified: false`**. All seven LiveApplication rows were
`Blocked` "(not run)", and `LatestProbe` was null. This is Branch **B**. Evidence:
`A-prereq/ordinary-entry-blocked.log`.

### Wrapper (commit `d30f63e`, test assembly only)

| File | Role |
|---|---|
| `tests/PrintFlow.Tests/Diagnostics/ReadinessDiagnostic.cs` | Opt-in run and the diagnostic-only envelope |
| `tests/PrintFlow.Tests/Smoke/ReadinessDiagnosticSmoke.cs` | Live entry; composes a verifier and gate only |
| `tests/PrintFlow.Tests/Unit/Diagnostics/ReadinessDiagnosticTests.cs` | Synthetic admission-boundary proof |

- **Entry:** `PrintFlow.Tests.Smoke.ReadinessDiagnosticSmoke.Run_the_bounded_readiness_diagnosis`.
- **Opt-in:** `PRINTFLOW_READINESS_DIAGNOSTIC=1`, exact value, ordinal comparison. The guard runs
  before the composition delegate: with no opt-in there is no composition, store open or external
  call.
- **Reuse, not reimplementation.** The omission is the existing internal
  `ProductionWorkstationVerifier.ForStandardRegressionRun`. The admission decision is the existing
  `RegressionBootstrapWorkstationVerifier`: it asks the real verifier first and delegates only when
  `ProductionRevalidation` is the single blocking check; otherwise it returns the real refusal.
- **Added allowance:** exactly one new internal caller of that factory, in the test assembly.
  There is no new `InternalsVisibleTo`, no public API and no guard change.
  `The_omission_factory_is_not_reachable_from_the_application` still holds; it does not count
  callers.
- **Composition:** both verifiers take the App's arguments (`appsettings.json`, `FileWorkspace`,
  the `Evidence` directory, `TimeProvider.System`) and share **one** default
  `SqliteWorkstationAutomationLeaseManager`. There is no `ServiceProvider`, `SessionService`,
  adapter, workflow, persisted cache or business DB.
- **Envelope** (`printflow.readiness-diagnostic.v1`): a fresh `DiagnosticId` and `StartedAtUtc`;
  `RevalidationCheckOmitted` (the bootstrap's own `BootstrapWasUsed`); the unaltered `NormalReport`
  from `VerifiedEnvironmentGate.Read()`; the raw `DiagnosticReport`; `ProductionAuthorised`, read
  from the normal report only; and a literal `Authority` statement. It carries no set, run or
  invocation identity, no binding, no attestation and no `StandardRegressionSetRunResult`.

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
- Five second-failure cases stay blocking, and the self-verifying omitting stub is never used.
- A verified workstation is diagnosed with no omission.
- A failed diagnosis preserves stages, last attempted and confirmed stage, cleanup outcome, and
  primary and secondary failures.
- Diagnostic identities are fresh.
- The envelope has none of the standard-set or run shapes.
- At the existing publication boundary, a synthetic fixture record whose Passed outcome names no
  run or invocation is refused as "unbound", while the bound record still passes. The writer is
  unchanged.

Live smoke bodies in the union were opted out: **NOT EXECUTED**. No full suite was run, because this
is test-only orchestration.

**Pre-live review.** One separate read-only reviewer (a Claude Code Explore subagent, no edit
tools) found no blocking violation of the ten wrapper properties. It flagged these weaknesses, all
corrected before commit:

- A vacuous "would say yes" stub.
- No ban on `StandardRegressionSetRunResult`.
- No compose-once test.
- Real paths used in test literals.

Accepted non-blocking notes:

- The normal report and the admission decision are two separate passive observations.
- There are two verifier instances (only the omitting one drives applications).
- The gate over the omitting verifier would authorize if misused; only `RunLiveChecksAsync` is
  called on it, and it is never stored.
- The stubs are labelled Immutable, so phase is not modelled.
- The publication test proves an existing evaluator property.
- The smoke asserts nothing by design.

## R4-B — live procedure

**Authorization.** One explicit current-session operator confirmation covered the three R4 items:
the desktop is available; Meitu and Photoshop hold no unrelated documents needing protection; and
use of the real R2 lease and accepted binaries for this bounded procedure is authorized.

After runs 1–2, the operator restored Meitu and reported "Meitu Restored and Photoshop ready." The
agent took that as the go-ahead for the remaining bounded sequence under the same authorization.

The previous handoff had required a fresh explicit confirmation of the three items before another
attempt, and this message did not restate them. The agent accepted the message in place of that
self-set requirement. It also did not raise with the operator that Photoshop turned out to be
minimized. No artifact of the message exists beyond the conversation.

No Adobe, Generator, Crop, IME, preset, Action or timeout setting was changed.

**Command (every run).** Inherited `PRINTFLOW_*` and `PF_R*` variables were cleared (none present),
the opt-in was set process-locally and removed in `finally`, and then:

```
dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj -c Release --no-build --no-restore
  --filter "FullyQualifiedName=PrintFlow.Tests.Smoke.ReadinessDiagnosticSmoke.Run_the_bounded_readiness_diagnosis"
  --logger "trx;LogFileName=<run>.trx" --logger "console;verbosity=detailed"
```

Global lease readings used the canonical manager's passive `ObserveAsync(null)` (read-only
connection), through a throwaway scratchpad console tool outside the repository. Observations made
in-session but not saved as artifacts are marked *(session observation)*.

### Pre-run baseline (2026-09-13 21:34–21:36Z)

- Photoshop and Meitu were not running. Chrome held the foreground *(session observation)*.
- Lease `printflow-studio.external-automation.v1` at
  `%LOCALAPPDATA%\PrintFlow Studio\workstation-automation-v1.db` was **Free**
  (`B-baseline/lease-before.txt`).
- `D:\PrintFlowStudio\EnvironmentVerification\` held six historical retained probes
  (`PF_ENV_PROBE_*.png`, 68 B, written 2026-09-10; `B-baseline/environment-verification-before.json`).
  They were preserved and are still present after run 4.
- Generator logs were copied to `B-baseline/`.

### Run summary

| | Run 1 | Run 2 | Run 3 | Run 4 |
|---|---|---|---|---|
| DiagnosticId | `ad9a9f76…f069889` | `6db73a6c…0257e` | `bcb476fa…6c4f5d` | `9cb37c78…04ed6d` |
| Started (UTC) | 21:36:44Z | 21:49:09Z | 22:03:38Z | 22:05:06Z |
| TRX duration | 35.9 s | 14.6 s | 11.2 s | 8.8 s |
| Meitu | Launched 4904, `KnownWelcome` | **Failed**: no visible top-level window within 10 s | Attached 4904, `KnownWelcome` | Attached 4904, `KnownWelcome` |
| Photoshop launch | Launched 21000 | Blocked | Attached 21000 | Attached 21000 |
| Photoshop safe state | **Failed** `MK_E_UNAVAILABLE` | Blocked | Passed: `KnownStartScreen; No document is open.` | Passed: same |
| Colour settings | Blocked | Blocked | Passed: sRGB / Coated FOGRA39 / Dot Gain 15% ×2 | Passed: same |
| Test-image round trip | Blocked | Blocked | **Passed** | **Passed** |
| DiagnosticReport.Verified | false | false | **true** | **true** |
| NormalReport.Verified / ProductionAuthorised | false / false | false / false | false / false | false / false |
| Lock row (own release) | Passed | Passed | Passed | Passed |
| Global lease before → after | Free → Free* | Free → Free | Free → Free | Free → Free |
| LatestProbe | null | null | complete | complete |
| New EnvironmentVerification entries | none* | none* | none | none |

\* Run 1's lease-after reading (21:47:00Z) and runs 1–2's probe-folder comparisons are *session
observations*. A reviewer later reconfirmed the folder read-only, and runs 3–4 saved theirs.

In every run the `ExternalApplicationAutomationLock` row stayed Passed. A failed or throwing
release replaces it with "Release failed" (`ProductionLiveWorkstationVerifier.cs` ~245–278), so each
operation's own release succeeded.

### Runs 3 and 4 — complete probe lifecycle

| Field | Run 3 | Run 4 |
|---|---|---|
| Probe OperationId | `d9c8b5aa7507456f8964e111620535ea` | `29b606ffcf6d43e8ae8da33a3002cbd8` |
| ManagedPath | `D:\PrintFlowStudio\EnvironmentVerification\d9c8b5aa…\Working\PF_ENV_PROBE_d9c8b5aa….png` | `…\29b606ff…\Working\PF_ENV_PROBE_29b606ff….png` |
| Process | 21000 `D:\Adobe Photoshop CC 2019\Photoshop.exe` (StartedAt 21:36:56.095Z) | same |
| LatestAttemptAt | 22:03:39.244Z | 22:05:07.958Z |
| Stages | ProbeCreation → ProbeCreated → OpenGuard → OpenRequested → OpenConfirmed → IdentityCheck → IdentityConfirmed → CloseGuard → CloseRequested → CloseConfirmed → PriorStateCheck → PriorStateRestored → CleanupAttempted → CleanupCompleted | identical |
| Last attempted / confirmed | CleanupAttempted / CleanupCompleted | same |
| Cleanup | Succeeded — "The exact probe close was confirmed." | same |
| PrimaryFailure / SecondaryFailures | null / [] | null / [] |
| Lifecycle | EvidenceAvailable true | EvidenceAvailable true; LastSuccessfulLiveAt 22:05:15.496Z |

The request stages (`OpenRequested`, `CloseRequested`) were recorded separately from their
confirmations; the report does not infer them from later stages. Cleanup was reported by the helper
itself, not inferred from the later empty folder. Both probe paths are absent after the runs
(`B-run3` and `B-run4` `environment-verification-new.txt`: none). Lifecycle values describe each
run's own verifier instance, since every invocation composes fresh.

**Preconditions for runs 3 and 4** (run 3: `B-run3/preconditions.txt`; run 4: `B-run3/windows-after.txt`
and `B-run4/processes-before.txt`):

- **Run 3:** Meitu 4904 had a visible `美图秀秀` window (1064×744). Photoshop 21000 was
  responding, but its main window was **minimized** (−32000,−32000, 160×28). Code held the
  foreground.
  - Source: the Photoshop adapter has no minimized-window rejection, and `FindTopLevelWindows` keeps
    visible windows with non-empty bounds.
  - The only restore is `Win32ExternalAppWindowLocator.Activate` (`IsIconic` → `SW_RESTORE` →
    `SetForegroundWindow`), which belongs to the existing guarded probe flow.
  - By 22:03:54Z, during run 3, Photoshop had been restored to its maximized window and held the
    foreground.
    - The exact moment of the restore was not observed.
    - Source shows that only the probe's `Activate` calls (inside open, identity and close) can
      restore Photoshop on this path.
    - No input was sent outside the PrintFlow diagnostic process. That is a self-attestation, not
      instrumented.
- **Run 4:** at 22:04:24Z, 42 s before run 4 started, Photoshop 21000 was maximized and in the
  foreground and Meitu 4904 was visible (`B-run3/windows-after.txt`).
  - Run 4 has no window or foreground capture of its own before the run, only
    `processes-before.txt`.
  - Same pids and start times show the applications were not restarted or reset between runs 3
    and 4.

After run 4 (`B-run4/windows-after.txt`), both processes were unchanged (same pids and start times),
responding and visible, with Photoshop in the foreground. Generator's exception log was unchanged
across all runs.

The run 3 and run 4 copies of `generator_latest.txt` are byte-identical to run 1's (last written
09:37:40 local), so the latest log recorded nothing across two probe open/close cycles. Those copies
are not cited as evidence either way.

### Run 1 — fresh launch, failed before the probe

- Photoshop 21000 was launched at 21:36:56Z. The safe-state fact read failed with "Photoshop live
  settings could not be read: 操作无法使用 (0x800401E3 (MK_E_UNAVAILABLE))". The report was stamped
  21:37:20.08Z.
- PhotoshopColourSettings and TestImageRoundTrip were Blocked: "Photoshop's current document state
  could not be read."
- `probe` is assigned only after colour settings pass, so the probe was **not reached**. No stage,
  cleanup or path exists.

### Procedural deviation (run 2)

The prompt permits a repeat only when an observation or permitted change distinguishes competing
explanations, and it requires accepted clean application states. Run 2 met neither condition.

Before it started, read-only enumeration at 21:48:13Z *(session observation)* found no visible
top-level window for XiuXiu 4904, and `B-run2/processes-before.txt` recorded `title=''`. The source
already showed that run 2 could not reach Photoshop: Meitu is checked first, the attach waits up to
10 s, and only visible, non-empty windows count.

The run was started on reasoning that covered only Photoshop's state. **It was non-discriminating
by construction** and is recorded as a deviation. Its outcome:

- MeituLaunchability Failed ("No top-level window belonging to Meitu process 4904 appeared within
  10 s.").
- MeituSafeStartingState Blocked ("Meitu launchability did not pass.").
- The four Photoshop rows Blocked ("Meitu did not reach a safe state, so no later application check
  ran.").
- The lease was acquired and released.

Why Meitu's recognised window became non-visible between runs 1 and 2 was not observed and remains
unknown. The operator restored it before run 3.

## Classification and comparison

**Current bounded success.** Runs 3 and 4 attached to the same settled Photoshop and Meitu
processes and each completed the full diagnostic procedure and probe lifecycle, with fresh
identities and no intervention during either run *(session observation)*. This establishes current, bounded diagnostic
success on the **attach path**. It does not show that earlier failures were environmental or
impossible, and it grants no Production authority.

**Run 1 (launch path): "external exception observed with insufficient causal linkage", with a
supported but unreproduced candidate.**

- **Candidate: a single unwaited COM read too soon after a fresh launch.**
  - After `LaunchAsync`, `ReachSafeStateAsync` polls only window identification and
    `GuardedPhotoshopUiDriver.InspectStateAsync`, which uses only Win32 window facts and no COM.
  - `ProductionLiveWorkstationVerifier.RunAsync` then calls `RotPhotoshopRuntimeFactReader.Read`
    **once**. The reader marks this failure `isRetryable: true`, but the verifier does not retry.
  - `CLSIDFromProgID("Photoshop.Application.130")` succeeded before `GetActiveObject` failed, so
    the ProgID is registered; only the Running Object Table entry was absent at that moment.
  - The read finished no more than 24 s after process start and at least 8 s before Generator
    *started* logging (21:37:28Z; local UTC+12 per the log header).
- **What runs 3–4 add.** The **same Photoshop process** passed the identical read about 27 and 28
  minutes after launch.
  - **Run 3:** no PrintFlow code path restores or activates Photoshop before the read
    (`ProductionLiveWorkstationVerifier.cs` ~158 precedes `RunProbeAsync` ~207, and the only
    `Activate` calls are inside the probe). The last observed state, about 61 s before the read, was
    minimized with Code in the foreground. The state at the read instant itself was not captured.
  - **Run 4:** Photoshop was maximized and in the foreground 42 s before the run.
  - **Refuted:** a *persistently* broken registration for this process. A transient Running Object
    Table absence shortly after launch is the candidate itself, and is not refuted.
  - **Weakened:** an integrity or session mismatch (same Photoshop process and user, although the
    reading test hosts in runs 3–4 were new processes); the test-host launch context (the same
    test-host-launched process later passed); and a dependency on Photoshop being in the foreground
    *at the moment of the read*.
  - **Not tested:** a dependency on the window having been shown or activated at some point since
    launch.
- **What remains open.** Elapsed time since launch is **one of several uncontrolled variables**
  between run 1 (21:37Z) and run 3 (22:03Z). The others:
  - Generator finished its startup (21:37:28–40Z).
  - The operator interacted with the desktop, including restoring Meitu.
  - Photoshop's window went to minimized, cause unknown.

  Any of these fits the fail-then-pass difference as well as timing does. The candidate rests on a
  single fresh-launch observation. It was not reproduced, because that requires closing and
  relaunching Photoshop — an operator decision not taken in this slice.

**Run 2:** a Meitu recognition refusal (no visible top-level window) with unknown cause; a
procedural deviation.

No earlier hypothesis is rewritten as proven.

## Proposed correction for the launch path (not implemented)

- **Reproducer (needs separate operator permission):** close Photoshop CC 2019 by the operator's
  choice, leave Meitu visible at its welcome screen, then run the same command once so the
  procedure launches Photoshop fresh. Expected if the candidate holds: `PhotoshopSafeStartingState`
  fails with `MK_E_UNAVAILABLE` while an attach run shortly afterwards passes.
- **Affected boundary:** the Photoshop launch-readiness to runtime-fact handoff in
  `ProductionLiveWorkstationVerifier.RunAsync`, or the Photoshop foundation's launch readiness.
  Launch-path production operations may share the same single-read pattern; that was not assessed
  here.
- **Smallest correction, only after reproduction and separate approval:** after a *launch* only,
  retry the runtime-fact read on `MK_E_UNAVAILABLE` within the existing `LaunchTimeout` budget,
  without changing that value; attach keeps its single read. This is a Product change and outside
  this slice, which forbids timing changes.
- **Validation:** a synthetic reader that returns `MK_E_UNAVAILABLE` and then success, plus the
  affected union; then one fresh-launch live run and one attach live run.
- **Rollback:** revert that single commit.

## Harness note (not a Product fault)

Run 1's `*>`-redirected PowerShell wrapper did not return after the test finished, because the
Photoshop and Meitu processes launched by the test host inherited its redirected handles. That
background task was left running rather than stopped, so no process-tree kill could reach those
applications; its post-steps were captured manually. Runs 2–4 attached, ran as background tasks and
returned normally. Future fresh-launch runs should not wait on inherited console handles.

## Surfaces touched

| Surface | What happened |
|---|---|
| Real default lease store | Acquired and released four times by the normal manager. Each own release is proven by its Passed lock row, and the global state was Free before and after. No manual edit or reset |
| Meitu (XiuXiu 7.8.7.5) | Launched once by run 1 (pid 4904); attach refused in run 2; the operator restored its window; attached in runs 3–4; left running. Meitu-side input is not instrumented in the report and is **unknown** |
| Photoshop CC 2019 | Launched once by run 1 (pid 21000). In runs 3–4, two PrintFlow-owned probes were opened, identified, closed and deleted through the existing guarded protocol; the window was restored from minimized by the existing activation guard. Left running at its start screen |
| `D:\PrintFlowStudio\EnvironmentVerification` | Two probe directories created and cleaned by the procedure; no new entries remain; six historical probes preserved |
| Configured workspace / Evidence | Read for verification; probes written only under EnvironmentVerification |
| Production business DB, revalidation record | Not touched / not written. `D:\PrintFlowStudio\Revalidation` is absent after run 4. The wrapper composes no business DB |
| Adobe preferences, Generator, presets, Actions | Not changed; Generator logs read and copied only |

No close, kill or dialog dismissal was performed by the operator or the agent during the automated
runs. The operator's restoration of Meitu between runs 2 and 3 was a deliberate operator action.

Private raw evidence stays local under the ignored `artifacts/pf-audit-r4/` directory. SHA-256
values for all four envelopes and TRX files are in `evidence-sha256.txt`. The same read-only
reviewer reviewed three things: the admission boundary before live use, the runs 1–2 closure claims,
and these runs 3–4 claims and the verdict against the raw evidence and source. Its findings are
incorporated, including one blocking correction to the handoff's causal summary.

## Carried boundaries

- R2 Busy/Unknown/own-scope/internal-PDF semantics, R3 lifecycle and report semantics, R1
  evidence/publication/run-claim semantics and the operator readiness attestation are unchanged.
- R3's first 11,834/2/0 run and R2's historical default-store acquisitions stay as recorded.
- R1's pre-A1/A2 build-origin condition is **open**. These healthy R4 probes do not make A1/A2
  release-ready while it is open, and the unreproduced launch-path finding should be resolved or
  explicitly accepted before A1 relies on fresh Photoshop launches.
- No standard-set execution, Operator review, production revalidation, normal-App E2E, install,
  deploy, push or Jira status change.
