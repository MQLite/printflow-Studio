# PF-AUDIT-R2 — Workstation automation lease

## Scope and starting evidence

R2 addresses source-audit finding F2 only. The canonical checkout is
`D:\Repositories\printflow-Studio`, `master`; starting HEAD is
`584d8685dabd0b835ee12f1ea32c462fcc7ed575`. Tracked files were clean. The untracked
`printflow-remediation-prompts/` bundle is operator-owned input and is excluded from commits.

The actual current R1 HANDOFF, PLAN and final report were read before R2 work. The previous
final-source baseline is **11,804 passed / 0 failed / 0 skipped**, with a Release build reporting
0 warnings / 0 errors. It is historical evidence, not an R2 test result. No baseline full suite
was rerun. `global.json` selects SDK 10.0.400; the per-user executable at
`%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe` reported **10.0.400**.

The original CSV was read using the established ordinal mapping, without rescanning Jira:

| Jira requirement | CSV row (zero-based) / source ID | Requirement |
|---|---|---|
| SCRUM-11108 — Implement the Global Automation Lock | 48 / 11501 | Only one ProcessingSession controls Meitu or Photoshop; persist ownership for crash detection, safely reject concurrent requests, detect stale locks at startup. Protect external automation, not general review. |
| SCRUM-11112 — Implement Interrupted Attempt and Startup Recovery | 52 / 11505 | Detect unfinished attempts and stale lock state; mark attempts INTERRUPTED. Offer fresh-copy restart, manual-result inspection/import or abandonment. Never resume a mouse/selector sequence or uncertain external state. |

Source: `C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv`.
The audit's F2 counterexample is distinct business databases independently granting their
`AutomationLock` row while controllers target the same desktop applications.

## Evidence and acceptance boundaries

R1's evidence binding version 1, revalidation schema 2, permanent run-identity claim and
publication refusal contracts must remain unchanged. A run claim is spent once staked;
temporary physical ownership must never expire or recover that claim. The claim remains before
operational setup. `ForStandardRegressionRun` omits only the self-referential revalidation check;
`environmentReadinessPassed` remains the existing operator attestation.

**Open pre-A1/A2 condition:** matching informational commit labels do not establish that harness
and installed candidate were built from identical clean source. Candidate freeze/publication
must resolve this build-origin limitation before A1/A2. R2 neither implements a fix nor waives
the condition, and does not claim it solved. This is not a blocker to isolated synthetic lease
engineering; no signature requirement is introduced.

Real external applications **NOT RUN**. Real production revalidation **NOT WRITTEN**.
Jira acceptance statuses remain unchanged. Synthetic lease evidence does not mark
SCRUM-11065, SCRUM-11123, SCRUM-11130 or a whole safety Epic FULL.

## Execution deviation: default lease acquired by two existing tests

The preliminary affected-test union included two existing `ProductionGateSideEffectTests`
that still composed the default lease manager. R2 moved physical acquisition before the
environment gate; those tests expected gate refusal but had not yet been changed to inject an
isolated authority. Both tests passed their `EnvironmentNotVerified` assertions. Together with
the acquire-before-gate code and checked release result, that proves **the actual default
workstation lease was acquired and released for those two tests**. This initialized/updated the
default lease store and violated the user's explicit test-isolation boundary. It is not described
as hypothetical or as a synthetic-store-only run.

The environment gate refused external work, so no real external application was launched.
The production revalidation record was not involved. Further test execution was paused when the
error was identified; the implementer and independent reviewer audited production-composition
fixtures for isolated lease injection before resuming. The actual default store was not inspected,
deleted or changed as a cleanup action. No production-data remediation is authorized by R2.

This deviation must remain visible in the final handoff even after the tests are corrected.

## Entry-point inventory inspected before implementation

| Entry point | Original admission / operations |
|---|---|
| Normal App `ServiceRegistration` → `SessionService.RunProducingStepAsync` | Environment gate, then business-DB `AutomationLock` around adapter-backed steps; includes PSD preparation. |
| `ProductionWorkstationVerifier.ForWorkstation` → `ProductionLiveWorkstationVerifier.RunAsync` | `SqliteEnvironmentAutomationLock` constructed from supplied business DB; launches/readiness and Photoshop probe under that DB token. |
| `StandardRegressionSetWorkstationSmoke` | Permanent invocation claim first; separate `regression-run.db` per run; composition override supplies the restricted regression verifier; live checks then SessionService operations. |
| Standalone Meitu smoke | Foundation open/enhance/export/close, background-removal discovery and raw driver helpers, busy-cancel discovery, supervised stop/takeover, and direct production-processor calls. These are runnable entry points. |
| Standalone Photoshop smoke | Foundation identify/open/close; preparation, W1 and TIFF compositions; owned-document cleanup probe. Workflow-output, final-review and final-gate callers also compose the production processor with SessionService. |
| Other production smoke compositions | Activation, recovery, multi-job soak and PSD preparation/resume use the application composition and its session service. PSD preparation also directly invokes `EnsureReadyAsync` before/after steps; those phases need separate ownership. |
| Visual-only white-ink smoke | A SessionService PSD preparation is followed by a direct TIFF automation chain; ownership must cover the later direct chain without competing with its preceding session operation. |
| Startup recovery | Per-DB unfinished attempts and stale lock metadata; liveness-dependent repair, no adapter execution or automatic resume. |
| Passive readiness / retained observations | Reobserve/Read and read-only diagnostic probes must not acquire/release physical ownership or manufacture live verification evidence. |
| Fake, trim, manual crop, PDF/file review | No external application control; workstation availability must not become a prerequisite. |

## Operational limits

The supported lease domain is the current Windows operator on this workstation. Its canonical
identity must be shared by the normal App, verifier and runners regardless of business DB,
workspace, run ID, installation path or preset version. This slice does not establish a new
multi-user automation architecture.

Older binaries and tools do not retroactively participate in cooperative exclusion. Do not run
mixed old/new automation controllers against the workstation. A person or unrelated software
can still take the foreground; the lease does not prevent that and adds no process killer.
Existing foreground, identity, unknown-document/dialog and safe-start checks remain necessary.

Controller death can make physical ownership available again, but proves nothing about what
Photoshop or Meitu retained. Per-DB interrupted-attempt recovery and explicit fresh-step restart,
manual-result handling or abandonment remain the workflow choices. There is no automatic resume.

## Reproduced before the fix

`Different_business_databases_cannot_both_admit_the_same_external_boundary` created two temporary
SQLite databases and two `SqliteEnvironmentAutomationLock` clients. With the first lease still
held, the second database also granted a lease. Both calls reached a recording external boundary;
no external application was used. The expected exclusion assertion failed: `secondLease.IsFailure`
was False rather than True. Raw TRX: `artifacts/pf-audit-r2/repro/two-database-before-fix.trx`,
**1 executed / 0 passed / 1 failed**, intentionally recorded before Product changes.

This reproduces the database-scoped authority defect. It is not the two-independent-process
post-fix proof; that must separately coordinate real overlap.

## Chosen authority and ordering

The chosen implementation is one shared SQLite authority at
`%LOCALAPPDATA%\PrintFlow Studio\workstation-automation-v1.db`, with resource identity
`printflow-studio.external-automation.v1`. It is independent of the session database. Tests inject
an isolated store and resource identity. It uses the current operator's application-data
permissions and requires no system provisioning or broadened ACL.

The initial exclusive-file-handle proposal was rejected during design review: passive status would
need a second sidecar that could not publish ownership atomically with the handle. The shared
store keeps owner publication, release and passive reads in one authority. Only this solution is
implemented; there is no second physical-control lock, service or scheduler.

The owner record combines an opaque token with machine, PID, process name and process start time.
Acquisition may recover only a controller proven dead. Alive and Unknown owners remain protected
without a TTL. Release compares the token, so an old owner cannot clear a successor. Passive
observation does not acquire or release: a committed empty row is Free, a live owner is Busy,
and missing, unreadable, stale or unobservable ownership is Unknown. An explicitly supplied active
scope permits its own readiness reinspection without treating itself as a competitor.

| Lifecycle point | Required behavior |
|---|---|
| Pre-acquire | No physical capability and no external effect. |
| Acquired | Shared owner transaction committed; full gate reinspection uses that exact scope. |
| Attempt committed | Running attempt and business-DB correlation lock committed before adapter work. |
| External work | Retain physical scope across adapter calls and legitimate nested helpers. |
| Termination/unwind | A cancellation request alone does not release; wait for adapter/cleanup return. |
| Persistence outcome | Attempt closing commit while still owning; failed persistence retains truthful recovery history. |
| Release | Explicit token comparison; report material failures rather than assuming Free. |
| Owner death | Later acquisition verifies death and replaces the stale token atomically; no external-state auto-resume. |

## Review process

A separate read-only reviewer was created with no inherited conversation history and requested
Astra High / offset 0 for the ownership/recovery audit. It inspected the original R2 requirements,
R1 contracts, current code and the design, independently of the implementation agent. Its initial
findings covered the split DB authority, the file-handle sidecar publication gap, the mixed
visual-only smoke entry point, cancellation cleanup and nested-scope lifetime. The sidecar finding
changed the selected design to the single shared store described above.

During settled-code review the reviewer found that `RegressionBootstrapWorkstationVerifier`
inherited the new scope-aware interface method but forwarded only parameterless `Verify()`.
The standard-set gate would consequently observe its own physical lease as Busy and refuse the
first external operation. Correcting scope forwarding through both wrapped verifier calls is a
necessary R2 integration change; it does not broaden the revalidation-only bootstrap exception.

The reviewer also identified direct `EnsureReadyAsync` calls before and after SessionService work
in `PsdPreparationWorkstationSmoke`. Those calls can launch Photoshop and need their own bounded
scopes. Finally, manager-level exclusion and dependency-identity assertions alone did not prove the
actual verifier/bootstrap/direct-smoke admission connections; the required integration tests must
retain the real isolated authority and replace only external effects. Cancellation proof must
hold cleanup in flight and demonstrate a denied contender before allowing unwind to finish.

The same independent reviewer re-reviewed these corrections and the raw focused TRX/log.
It confirmed scope forwarding through both bootstrap delegates, all three PSD readiness scopes,
real shared-store composition/direct-helper coverage, the blocked-unwind cancellation assertion,
and isolated production-capable test fixtures. It found no remaining actionable code/proof
issues and approved proceeding to the final build/full suite. That technical approval explicitly
does **not** waive the earlier default-lease acquisition violation.

## Test-host environment correction

The preliminary affected union also reported 17 R1 evidence-test failures because its
Windows PowerShell child processes could not resolve `Get-FileHash`. A read-only diagnostic
reproduced this through `ProcessStartInfo` with the inherited Codex PowerShell 7 module path
(exit 1). The same child command with `PSModulePath` restricted to
`%WINDIR%\System32\WindowsPowerShell\v1.0\Modules` resolved
`Microsoft.PowerShell.Utility` 3.1.0.0 successfully (exit 0).

Subsequent validation uses that setting only in the command's process environment, alongside
clearing `PRINTFLOW_*` and child-test control variables. No persistent environment setting,
global routing rule or R1 publication script was changed to address this host issue.

## Focused proof and raw evidence

Pre-full focused union: **195 passed / 0 failed / 0 skipped**, exit 0, 42 seconds.
Raw evidence: `artifacts/pf-audit-r2/focused/pf-audit-r2-focused.trx` and
`artifacts/pf-audit-r2/focused/pf-audit-r2-focused.log`. The parent independently parsed the TRX.

| Distinct boundary | Evidence |
|---|---|
| Different business databases, actual SessionService admission | `Different_business_databases_share_one_SessionService_external_authority`: first production-mode recording PSD processor blocks; second is refused with zero calls and no attempt; second succeeds after release. |
| Independent OS processes and overlapping ownership | `Independent_processes_exclude_across_business_databases_and_reacquire_after_release`: separate owned test processes initialize distinct synthetic business DBs and contend through the real shared manager. Explicit ready/release files coordinate overlap and the recording log proves zero denied effects. This helper tests the manager boundary; it does not execute SessionService in its child processes. |
| Explicit nesting and same-process competition | `Explicit_nested_scope_cannot_release_outer_and_unrelated_same_process_operation_is_refused`; `Releasing_one_handle_concurrently_is_idempotent_and_does_not_release_a_nested_owner`. |
| Controller death, live/unknown ownership, stale release | `Crashed_controller_is_recovered_only_after_its_owned_process_exits`; `Unverifiable_live_owner_is_never_evicted_and_release_failure_never_reports_Free`. An owned helper exits without releasing; a later acquisition proves death before replacement. |
| Opening/closing persistence | `Opening_commit_failure_releases_physical_ownership_before_any_adapter_effect`; `Closing_commit_failure_retains_running_history_but_releases_physical_ownership`. |
| Cancellation while cleanup is still in flight | `Cancellation_waits_for_adapter_unwind_and_closing_commit_before_releasing_ownership`: explicit unwind barrier; a contender remains denied after cancellation until cleanup is allowed to complete. |
| Actual composition connections | `App_live_verifier_bootstrap_and_direct_smoke_share_one_real_isolated_authority`: real App/SessionService admission, real live verifier, bootstrap own-scope reobservation and the direct-smoke helper share an isolated real store with recording external seams. |
| Passive observations and Fake work | `Constructing_and_observing_a_missing_authority_are_physically_read_only`; `Missing_and_unreadable_authority_are_Unknown_not_Free`; `Fake_work_remains_available_while_the_same_physical_domain_is_held`. |
| Preserved recovery, gate and R1 evidence contracts | The focused union includes existing startup recovery, production gate/composition, standard-set and R1 evidence-integrity tests. |

The exact focused class filter is:

```text
WorkstationAutomationLeaseTests|ProductionLiveWorkstationVerifierTests|VerifiedEnvironmentGateTests|ProductionAdapterGateTests|ProductionGateSideEffectTests|ProductionCompositionTests|ApplicationStartupTests|EnvironmentGateCompositionTests|StartupRecoveryTests|StandardRegressionSetTests|RegressionEvidenceIntegrityTests
```

Each class is selected as `FullyQualifiedName~<class>`. Owned helper processes receive explicit
safe environments and isolated identities; watchdog cleanup targets only the processes the
test created. No elapsed-time takeover, stress matrix or real-app opt-in was used. The preliminary
`FailFast` helper experiment was replaced with abrupt `Environment.Exit(17)` after a hang;
its exact owned process tree was cleaned up by the test's `finally` path. Interim test-setup
failures were corrected rather than removed or weakened.

## Local delivery and final validation

Code/test commit: **`689770a3a667f1a0a29954cfe1d8412cc0f16c69`** —
`fix: share workstation automation ownership across isolated databases`.
The first clean build and the single full suite used that committed source. A bounded PDF
correction follows in a separate code commit; no amend, rebase, branch, worktree, clone, push,
install or deploy occurred.

Clean Release build: **0 warnings / 0 errors**, exit 0. Raw logs:
`artifacts/pf-audit-r2/final/clean.log` and `artifacts/pf-audit-r2/final/build.log`.
The single full Product run on that commit completed with **11,788 passed / 31 failed / 0 skipped**,
11,819 executed, exit 1, 5 minutes 30 seconds. Raw evidence:
`artifacts/pf-audit-r2/final/pf-audit-r2-full.trx`, `full.log`, `full-exit.txt` and
`source-commit.txt`. The parent independently parsed the counters and all failures.

The observed total is 15 cases above R1's 11,804: 13 lease tests (including the guarded child
helper), one composition test and one combined live/bootstrap/direct-admission test. This is
an observed run total, not a prediction of a passing count.

All 31 failures were PDF cases: 28 in `PdfPreparationWorkflowTests` and 3 in
`VisualOnlyPdfInputContractTests`. The in-process Windows PDF processor reports Production mode;
the physical-lease predicate had incorrectly treated that as external automation. The focused
union and initial review missed this internal-work boundary.

The correction restricts physical ownership to Production Meitu/Photoshop. Only PDF uses the
narrow internal-production gate; implementations without that capability fall back to ordinary
verification. The real gate and regression wrapper retain every other environment and workflow
check. Internal reobservation marks physical lease availability as a nonblocking Advisory, never
as Passed or Owned, and does not acquire the lease.

Known Busy observation still refuses external admission but now preserves existing live evidence
so internal PDF work can immediately re-inspect all applicable facts. It does not refresh the
evidence timestamp, run another probe or confer external permission. Unknown still invalidates
the evidence. This is the minimal R2 Busy/own-scope admission adjustment, not a readiness-cache
lifetime redesign.

The corrected targeted union, extended with both PDF classes, passed **228 / 0 failed / 0 skipped**,
exit 0, 41 seconds. Raw evidence:
`artifacts/pf-audit-r2/corrected-targeted/pf-audit-r2-corrected-targeted.trx` and `.log`.
The parent independently parsed the counters. The combined real-composition test now proves,
in order under a held isolated scope: ordinary gate refusal, unchanged live-probe counts,
internal verification with lease Advisory, actual Windows PDF preparation and successful attempt
persistence, a released business-DB correlation lock, and still-Busy physical authority.
It also retains scoped bootstrap verification, external PSD refusal while held and success after
release. The targeted union contains 33 PDF cases, including the 31 that failed the full run.

**No second full suite was run. No settled-source full-suite pass is claimed for the corrected
code.** The single failed full run and the earlier default-authority test violation remain
delivery limitations; successful targeted correction and review do not erase them.

Corrective code/test commit: **`99caa61415b45fd0f50ed09076a8fd3cc2fa8ee3`** —
`fix: preserve internal PDF admission during automation contention`.
The same separate read-only reviewer inspected this seven-file diff and the corrected targeted
TRX/log, finding no remaining actionable findings. No tests or edits were performed by that
reviewer. It explicitly retained the missing final-source full-suite proof and historical
isolation violation as unwaived limitations.

The parent then validated committed source `99caa61` with a fresh Release clean/build:
**0 warnings / 0 errors**, exit 0. A targeted rerun against those clean-built assemblies passed
**228 / 0 failed / 0 skipped**, exit 0, 40 seconds. This rerun establishes the affected checks
against the corrective commit; it is not a second full suite. Raw evidence is under
`artifacts/pf-audit-r2/correction-final/`: `clean.log`, `build.log`, `targeted.log`,
`pf-audit-r2-correction-final.trx`, `targeted-exit.txt` and `source-commit.txt`.
An initial clean command incorrectly included unsupported `--no-restore`; it failed at CLI
argument parsing before cleaning/building and is retained as `clean-cli-error.log`.
The successful clean omitted that switch; build used `--no-restore`, and targeted test used
`--no-build --no-restore`, the class filter above plus `PdfPreparationWorkflowTests` and
`VisualOnlyPdfInputContractTests`. The test environment was sanitized as described above.
The parent parsed the final TRX and verified no subsequent source/test drift from `99caa61`.

Delivery uses two local code/test commits followed by a documentation-only commit containing
this report, PLAN and HANDOFF. Tracked work is committed; the only untracked path is the untouched
operator prompt bundle. Generated raw evidence is ignored local output, not committed. Nothing
was pushed. The final response records the documentation commit hash without a self-referential
hash chase in these files.

## Coverage / audit delta

F2 now has a single canonical physical authority across App, session operations, live verification,
regression bootstrap and supported direct smoke callers. Synthetic evidence covers overlapping
independent processes, same-process competitors, nesting, stale tokens, controller death,
failure/cancellation persistence order, and actual composed admission. Internal PDF work remains
available while that authority is held. This appends engineering evidence to the audit; it does
not rewrite the original finding, grant live acceptance or mark an Epic FULL.

R1 binding/schema/run-claim contracts and the open pre-A1/A2 build-origin condition remain as
stated above. R3/live diagnostics were not started. Opted-out live tests are **NOT EXECUTED**,
irrespective of the runner counting their guarded returns as passes.

## Final-source QA closure — 2026-09-11

**PASS WITH NOTES — PF-AUDIT-R2 FINAL-SOURCE QA VERIFIED;
HISTORICAL ISOLATION DEVIATION RETAINED.**

This continuation explicitly supersedes the earlier interpretation of “one final full suite”
as a lifetime attempt limit. Earlier statements that no second full suite had run describe
that historical delivery, not the current status. The failed initial suite and the two actual
default-workstation-lease acquisitions remain historical evidence and are not waived or erased.

Verified starting/tested HEAD: `2f3d830f90489bd243e96f6888c76332d591e785`, `master`, canonical
`D:\Repositories\printflow-Studio`. This continuation changed **no Product source, tests or
fixtures**. The only difference from corrected code commit `99caa61` before testing was the
three R2 documentation files. No reset was performed; the operator prompt bundle was preserved.

Before execution, the audit searched acquisition and composition callers across the complete
test tree, not only the previous 228-case filter. The shared SessionServiceHarness and corrected
production-gate/startup/composition fixtures inject temporary stores and synthetic resource
identities before acquisition. The real App/live/bootstrap/direct-helper integration uses one
explicit isolated authority; other live-verifier tests use recording authorities and synthetic
workspaces. Owned lease children receive explicit temporary store/resource/business-DB/barrier
paths after inherited controls are cleared. Direct live smoke guards precede acquisition.
Synthetic standard-runner claim refusal occurs before composition; its environment mutation is
serialized in the existing EnvironmentVariableCollection. Fake/manual/internal-PDF paths do not
acquire physical ownership. No remaining isolation gap was found and no fixture was changed.
The audit did not inspect, acquire, initialize, release, reset or delete the actual default store.

The launcher cleared inherited `PRINTFLOW_*` and `PF_R2_*` variables process-locally and retained
the Windows PowerShell child module-path correction to
`%WINDIR%\System32\WindowsPowerShell\v1.0\Modules`. Relevant tests supply their own explicit
synthetic controls. Persistent environment settings and the shipped default authority were
unchanged; locking was not disabled. The unchanged 228-case set was not rerun ceremonially.

A fresh clean Release build was chosen because the prior build evidence did not include a
saved binary fingerprint sufficient to demonstrate reuse. SDK **10.0.400**, local Windows
`win-x64`; clean exit **0**, build exit **0**, **0 warnings / 0 errors**. The complete Release
Product suite then ran with no class filter or exclusions, using `--no-build --no-restore`.

| Evidence | Passed | Failed | Skipped | Meaning |
|---|---:|---:|---:|---|
| R1 historical baseline | 11,804 | 0 | 0 | Not an R2 result |
| R2 initial full suite, `689770a` | 11,788 | 31 | 0 | Preserved failed run on superseded source |
| Corrected R2 affected set, `99caa61` | 228 | 0 | 0 | Existing focused proof; not rerun here |
| R2 final-source full suite, `2f3d830` | **11,819** | **0** | **0** | Fresh complete run; host exit **0** |

The full runner reported 6.4585 minutes; captured host interval was
2026-09-11 14:26:17–14:32:47 +12:00. Independent XML parsing found 11,819 individual Passed
results, matching total/executed/passed counters, with zero non-Passed results and zero skipped.
Before/after SHA-256 manifests match for tracked non-document inputs and tested PrintFlow
assemblies; HEAD and tracked status also remained unchanged. The earlier failed full TRX and
log hashes match before/after. No unchanged-code retry was performed.

New ignored raw evidence: `artifacts/pf-audit-r2/final-source-qa-20260911/`:
`runner.ps1` contains exact clean/build/test commands and environment handling;
`isolation-audit.txt`, `sdk.txt`, `source-commit.txt`, `clean.log`, `build.log`,
`full.log`, `full-exit.txt`, `pf-audit-r2-final-source-full.trx`, `parse-results.ps1`,
`independent-summary.json`, and before/after input, assembly and historical-evidence manifests.
The historical `artifacts/pf-audit-r2/final/` and `correction-final/` evidence remains distinct.

No material code/isolation change required narrow re-review; the existing independent reviews
were retained and the entire review was not repeated for evidence/docs alone. Policy 2.3,
route offset 0: QA NormalRoute/RequestedRoute Sol High, documentation Luna Low;
AdjustmentResult UNCHANGED; context CONTINUE. No supported in-place runtime switch was available
(`MODEL_SWITCH_UNAVAILABLE`); execution continued safely in the current runtime, ActualRoute
UNVERIFIED. No model downgrade, independent review or new context is falsely claimed.

Busy preserves existing live evidence without refreshing it; Unknown invalidates it. Internal
PDF admission and explicit own-scope checks retain all other required facts. These delivered R2
semantics are unchanged. R1's unresolved harness/candidate build-origin condition remains open
before A1/A2; this synthetic QA does not resolve or waive it. Jira acceptance statuses are
unchanged. Real external applications, real standard-set execution, production DB/preset/customer
artwork operations and production-revalidation writes: **NOT EXECUTED**, regardless of guarded
no-op runner pass counts. No R3 or live acceptance started. Delivery is a local documentation-only
commit; no push, install, deploy, branch/worktree/clone, amend/rebase or attribution trailer.
