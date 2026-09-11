# PF-AUDIT-R3 — Readiness evidence lifecycle and truthful probe diagnostics

Status: implementation, focused proof and independent review complete; final-source QA pending.
This report will be closed against the settled source before delivery.

## Authority and inherited evidence

R3 only, canonical `D:\Repositories\printflow-Studio`, `master`. Starting HEAD verified as
`519be2421e6dc6ec968caa5e550700231a3a57b1`; tracked tree clean, operator-owned untracked
`printflow-remediation-prompts/` preserved. No reset or baseline-suite rerun. `global.json`
selects `10.0.400` with `latestFeature`; the per-user dotnet executable reported `10.0.400`.
The R2 report, PLAN and HANDOFF were read including their final-source closure sections.
R2's accepted 11,819 passed / 0 failed / 0 skipped and Release 0 warnings / 0 errors are inherited
evidence on `2f3d830`; documentation commit `519be24` changes no Product/test input. They are not
R3 test results. R2's historical 11,788/31/0 failed suite, corrected 228/0/0, and two actual-default
lease acquisitions remain in their original reports, unwaived and unchanged.

Exact original CSV rows read using the established mapping: index 50/source 11503/SCRUM-11110
requires named environment failures, no automatic repair, launchability, colour/safe-state and
test-image open/close checks. Index 51/source 11504/SCRUM-11111 requires blocking unconfirmed or
drifted assumptions, expected/current facts and no silent adaptation. Approved design §13.4/§14
and the existing Photoshop foundation/driver ownership rules constrain this slice.

Installed routing policy 2.3, explicit offset 0, PLAN_EXECUTE. Planning: Astra High for cross-module
state/cleanup analysis; backend implementation/test strategy: Sol High; existing UI detail text:
Astra High; docs: Luna Low; independent read-only review: Sol High. Offset result UNCHANGED.
Connected work stayed CONTINUE. In-place MODEL_SWITCH_UNAVAILABLE; safe current-runtime fallback,
ActualRoute UNVERIFIED. The separate native reviewer uses a fresh no-history context, not a
full-history fork; requested `gpt-5.6-sol`/high. A requested route is not proof of runtime selection.
No global routing configuration changed.

## Residual gaps and implemented distinctions

R2 already supplied Busy preservation without timestamp renewal, Unknown invalidation, exact
own-scope checks, the internal-PDF physical-availability Advisory, and forwarding through both
regression-bootstrap delegates. Those contracts are reused; no lock redesign or second
Busy-preservation implementation was added.

The actual remaining reporting gap was `RunProbeAsync` returning only `Unit`/failure. Its
caller reduced that to one check; the normal `VerifiedEnvironmentGate` report and JSON could
not distinguish requested, confirmed, unrun cleanup, or secondary cleanup failures. A synthetic
open/identity failure through the real live verifier, outer verifier and gate reproduced the
missing Lifecycle property: `artifacts/pf-audit-r3/repro/gap.trx`, 0 passed / 1 failed, host exit 1.
The preceding test-setup compilation error (missing explicit fixture argument) is retained as
`repro/setup-compile-error.log`; it is not the reproduced Product gap.

The additive shared `ReadinessEvidenceLifecycle` now separates:

- Last complete successful live observation time and its already-observed Meitu/Photoshop
  process identities. These historical facts survive diagnostic failure/invalidation.
- Whether live evidence remains available for current reobservation, and whether Busy deferred
  current runtime observations. Neither field is an Allowed decision; the gate still evaluates
  current automatic/live checks and explicit physical ownership on each operation.
- The latest returned diagnostic attempt's start time and optional partial probe result.
  A failed/incomplete attempt supplies no new successful timestamp or authorization evidence.
  There is no per-input streaming, persistent certificate, TTL, scheduler or trace archive.

Passive entry/Refresh does not acquire, open, close, repair or renew a probe. Busy returns blocked
runtime checks explicitly described as deferred; after release, fresh reinspection runs before
admission. Unknown and process drift invalidate evidence. Restoring the facts alone cannot revive
invalidated evidence; a new complete live run is required. Historical diagnostics cannot reopen
the gate. The current report's `ObservedAt` is distinct from the successful live timestamp.

One existing synchronization boundary had two demonstrated interleavings: an old failed passive
observation could clear a newer successful live result; conversely an old passing observation
could return Verified after a later failed attempt invalidated its evidence. A single revision
comparison under `_liveEvidenceSync` now prevents both: old failures cannot clear newer evidence,
and a stale passing round-trip claim becomes Blocked pending a new observation. Other concrete
failures remain in the report. Lifecycle snapshots are captured under the same lock.
The inverse case was reproduced with barriers, not sleeps: `interleaving-before/` 1 passed /
1 failed; its old successful read incorrectly returned true. Corrected focused proof is recorded
separately below. This is a bounded passive/live correction, not a new concurrency framework.

## Probe semantics and preservation rules

`ReadinessProbeDiagnostics` records a separate random probe OperationId (never a lease token),
contained managed path once established, the external process already observed before the probe,
a bounded set of typed milestones, last attempted/confirmed milestone, cleanup outcome/reason,
primary failure and secondary failures. Failure metadata retains stable code, bounded technical
detail and selected already-observed boolean context flags; arbitrary dictionaries/window
inventories are not copied. No additional COM or machine/document inventory query was added.

| Milestone | Evidence required |
|---|---|
| ProbeCreated | Existing contained PNG creation helper returned success |
| OpenGuard | Managed-file/open helper entered; this is not a request to open a document |
| OpenRequested | Signed Open control dispatch after the sink's final guards, immediately before native click; not proof of load |
| OpenConfirmed | Existing expected-document title/state criterion reached |
| IdentityConfirmed | Existing absolute-path Save As identity rule matched the managed probe |
| CloseGuard | Exact-close helper entered; identity/foreground checks may still refuse |
| CloseRequested | Close-input dispatch after identity/window/final-foreground guards, immediately before native input |
| CloseConfirmed | Existing bounded document-absence/identity criterion succeeded; send return alone is insufficient |
| PriorStateRestored | Existing readiness reinspection and before/after document-state comparison both passed |
| CleanupAttempted / CleanupCompleted | DeleteProbe actually invoked / returned success; no inference from missing errors or later disk absence |

Absent milestones are unrecorded/unconfirmed, not inferred successes. Legacy implementations
without request instrumentation leave requests unrecorded; successful foundation results still
carry their existing identity/close confirmation contract. Cleanup distinguishes NotRun, Succeeded,
Failed and Unknown (helper did not return a usable outcome). A primary failure remains primary
through restoration, cancellation-close, cleanup and release; secondary failures remain separate.
Successful cleanup cannot make an otherwise failed probe pass.

Existing safe ownership rules are preserved: open/identity failure does not gain a new close or
delete action; unconfirmed close retains the backing file. Cancellation retries only the exact
opened probe under the existing 15-second bounded unwind and holds the workstation lease until
unwind returns. A confirmed cancellation-close permits existing cleanup but does not pretend the
prior-state reinspection ran. Cleanup failure caused by changed probe bytes is retained instead
of silently discarded in finally. No blind close, process termination, modal dismissal, settings
repair or historical-probe deletion was introduced. Photoshop identity/close input, polling and
settings are unchanged; ROT remains the existing read-only runtime-fact reader only.

## Actual consumers

The same structured metadata flows through `WorkstationLiveVerification`,
`WorkstationVerificationResult`, `VerifiedEnvironmentGate.ToReadinessReport` and
`EnvironmentReadinessReport.Lifecycle`. The existing standard runner's `readiness.json` serializer
therefore carries it additively with its existing string-enum settings. Historical reports missing
Lifecycle deserialize to null, not a complete probe. Check/failure codes and authorization rules
remain unchanged; R1 binding versions and operator `environmentReadinessPassed` are untouched.

The readiness screen reuses its existing technical Detail row for the probe. New explanatory
labels are provided in the existing English/zh-CN resource system; typed stages/failure codes
remain stable support identifiers. No new screen or export/browser was added. The existing
controlled live-verification smoke now also prints the whole JSON report to its normal test
output, including refused attempts. Its business-DB lock line is labelled as a correlation lock,
not incorrectly offered as proof of physical ownership release.

Synthetic matrix tests drive the real probe orchestration, outer verifier and gate, write/read
`readiness.json` with the existing consumer's serializer settings, and compare operation/path,
process identity, primary/secondary failures and milestones. They also verify the existing row
receives the operation ID/failure code. Real foundation/guarded-driver tests establish the lower
request and confirmation boundaries against recording input/control seams.

## Isolation, review and verification

Reused R2's complete-tree synthetic-isolation audit and reviewed newly reachable R3 paths. New
tests use `WorkstationVerificationFixture`, contained FileWorkspace, recording process/fact/input
seams and RecordingLock, or the existing R2 explicitly isolated real store/resource composition.
No new production-capable default composition is reached. The live smoke opt-in guard still
precedes setup/acquisition. Owned child controls and existing environment-test serialization are
unchanged. Test launchers clear inherited PRINTFLOW_*, PF_R2_* and PF_R3_* controls process-locally
and preserve the Windows PowerShell child-module-path correction. No persistent environment edit,
default-store inspection/operation, real application or real production revalidation occurred.

| Run | Passed / failed / skipped | Meaning |
|---|---|---|
| Initial focused | 133 / 0 / 0 | Initial integration plus existing helper/readiness tests |
| Expanded diagnostic matrix | 146 / 0 / 0 | Added failure/lifecycle/unwind proofs |
| Affected union | 363 / 0 / 0 | Lease/gate/composition/PDF/bootstrap/R1 plus touched helpers |
| Inverse interleaving reproduction | 1 / 1 / 0 | Deliberate new barrier counterexample before its correction |
| Corrected focused | 84 / 0 / 0 | Correction plus live/gate/screen cases |
| Architecture correction | 108 / 0 / 0 | Exact interface multiset and diagnostic-signature constraints |
| Dispatch boundary | 168 / 0 / 0 | Final native-dispatch guard, partial-send, architecture/live/screen checks |
| Settled-source Release/full Product | Pending | Required because shared behavior changed |

Raw logs/TRX/host exits live under ignored `artifacts/pf-audit-r3/`; all counted live smokes remain
opted out and NOT EXECUTED even where xUnit counts their guarded return as Passed. No dependency
files changed, so no repeat dependency scan was needed. Review and final fingerprint/QA closure
will be recorded here before delivery.

One fresh no-history native read-only reviewer independently identified the stale-success race
and the two exact interface-shape tests omitted from the earlier affected filter. Both were
corrected and narrowly re-reviewed. Exact capability-name multisets remain asserted; the new
overloads must add only the typed diagnostic callback to an existing guarded signature. The
reviewer also checked the final dispatch correction past the primitive's last guard, and the
recording native delegate that keeps even failed guard tests incapable of sending real input.
Final review outcome: no remaining actionable findings; clear for settled-source QA. Reviewer
performed no edits, tests or external operations. Actual model selection remains UNVERIFIED.

## R4 entry and prerequisites — documented, not executed

Smallest existing entry: `PrintFlow.Tests.Smoke.WorkstationVerificationSmoke.`
`Run_the_explicit_live_application_verification` in `tests/PrintFlow.Tests/Smoke/WorkstationVerificationSmoke.cs`.
The exact source opt-in is `PRINTFLOW_WORKSTATION_LIVE_VERIFY=1`. The .NET filter is
`FullyQualifiedName=PrintFlow.Tests.Smoke.WorkstationVerificationSmoke.Run_the_explicit_live_application_verification`.
It uses normal App `ServiceRegistration`, repository `appsettings.json`, the configured real
workspace, a disposable business DB, and the R2 shared workstation lease. It performs bounded
whole-live-readiness checks, not a Photoshop-only probe or seven-category standard set.

R4 requires separate explicit permission for real workstation/store/application operations;
accepted configuration/preset/binaries, current interactive desktop and safe application state;
and every normal automatic check, including ProductionRevalidation, must pass before the live
phase starts. This engineering slice neither inspects nor repairs those real prerequisites.
If that normal prerequisite blocks the desired comparison, the existing entry returns blocked
diagnostics. There is no general bypass flag. Any separately authorized R4-only diagnostic wrapper
would need an explicit non-authorizing whole-live-check contract, the same R2 lease and ownership
guards, owned probe/output paths, and no revalidation publication or standard-set verdict; it is
not implemented here and cannot expose the regression bootstrap to the production App.

After separate authorization, the existing test can be selected with the exact filter above,
`-c Release --no-build --no-restore`, a retained TRX logger and detailed console logger. The opt-in
is process-local and must be cleared/restored afterward. R3 did not set it or execute this entry.
The report JSON is in normal test output; it has no new output-path or Photoshop-only switch.
Host exit 0 is not a readiness verdict: this diagnostic reports `Verified=false` without asserting
it true. Inspect `Verified`, blocking checks and Lifecycle/LatestProbe directly. A skipped opt-in
body is NOT EXECUTED. A business correlation lock being free is not physical-lease release proof.

R4 must compare actual observed facts without replacing Save As identity/close with ROT or script,
changing Generator/Crop/IME/settings/timing/preset, or granting per-capability Production authority.
The R1 harness/candidate build-origin condition remains unresolved before A1/A2. Operator readiness
attestation remains separate from tool observations. Jira acceptance statuses are unchanged.
Stop before R4 and live acceptance; real external operations NOT EXECUTED; real production
revalidation NOT WRITTEN.
