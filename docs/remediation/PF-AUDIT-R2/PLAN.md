# PF-AUDIT-R2 — Plan: one workstation automation lease across isolated databases

**Task:** PF-AUDIT-R2 (source-audit finding F2; SCRUM-11108 / SCRUM-11112 only)  
**Routing policy:** Global Development Routing & Context Policy 2.3, `route_offset: 0`  
**Canonical checkout:** `D:\Repositories\printflow-Studio`, `master`  
**HEAD at start:** `584d8685dabd0b835ee12f1ea32c462fcc7ed575`  
**Tracked state at start:** clean  
**Untracked state at start:** operator-owned `printflow-remediation-prompts\` only; it remains untouched  
**Previous settled evidence:** Release build 0 warnings / 0 errors; Product suite 11,804 passed / 0 failed / 0 skipped. The full suite is not rerun at startup.

## 1. Scope and preserved contracts

This slice repairs the mismatch between the physical resource and its current authority. Every real
operation in this product drives the same workstation desktop and the same Meitu/Photoshop pair, but
the current `AutomationLock` row lives in whichever business database a caller supplies. The normal
App database and each standard-set `regression-run.db` can therefore grant simultaneous ownership.

The protected resource is one exclusive **PrintFlow external-application automation domain for the
current Windows operator on this workstation**. Its identity is stable across business database path,
workspace, `RunId`, installation folder and preset version. It covers any operation that may launch,
open, focus, send input, run a mutable script, or probe Meitu/Photoshop. Fake adapters, deterministic
pixel work, imports, file review and other internal/read-only operations do not acquire it.

Three existing concepts remain distinct:

| Concept | Lifetime and authority |
|---|---|
| Workstation automation lease | Temporary shared-store-backed permission to drive the external applications. This is the only physical-control authority. |
| Per-database `AutomationLock` and attempt rows | Workflow correlation, crash history and recovery metadata for that database. They no longer prove physical permission by themselves. |
| R1 `RegressionExecutionClaim` | Permanent single-use identity for one evidence-producing invocation. It remains the standard runner's first action and never expires to recover a lease. |

R1 `evidenceBindingVersion = 1`, revalidation schema 2, refusal/publication behavior,
`environmentReadinessPassed`, and `ForStandardRegressionRun` remain unchanged. The unresolved
build-origin limitation remains an open candidate-freeze/publication condition before A1/A2. R2
does not add signing, alter presets/global rules, or enter R3.

## 2. Requirement and pre-fix reproduction

The established CSV mapping gives:

- SCRUM-11108 (index 48 / source 11501), *Implement the Global Automation Lock*: one
  `ProcessingSession` controls Meitu/Photoshop, ownership is persisted for crash detection,
  concurrent requests are safely rejected, startup detects stale ownership, and ordinary review
  does not take the lock.
- SCRUM-11112 (index 52 / source 11505), *Implement Interrupted Attempt and Startup Recovery*:
  unfinished attempts and stale locks become Interrupted; recovery offers fresh-copy restart,
  manual-result inspection/import, or abandon; it never resumes mouse/selector/uncertain state.

Before changing product code, run a focused counterexample against the current public admission
mechanism: two `SqliteEnvironmentAutomationLock` clients backed by different synthetic business
databases target one recording external-operation boundary. While the first remains held, current
source is expected to let the second cross its independent row and record a second operation. The
captured failing assertion is that only the first should reach the boundary. Final proof adds the
stronger two-process barrier case and real `SessionService` compositions.

## 3. Design choice

### Options compared

| Option | Merits | Costs / risks | Decision |
|---|---|---|---|
| One shared SQLite coordination store under Local AppData | Atomic token changes and passive reads use one transactionally consistent authority; independent of every business DB; owner identity can include process start time and name rather than PID alone. | Adds one tiny application-owned database and explicit initialization; controller death requires exact liveness evaluation on the next acquisition. | Chosen. |
| One exclusive OS file handle at a canonical Local AppData path | Atomic exclusion is independent of every business DB; ownership follows a handle, so it is thread/`await` independent and Windows releases it on process death. | A readable sidecar cannot publish Busy/Free atomically with handle acquisition/release, while an exclusive handle cannot itself be observed passively. Named `Mutex` ownership is thread-affine across `await`. Either choice weakens required observation or complicates lifetime. | Rejected for R2. |

The implementation is a narrow Workflow port with an Infrastructure SQLite implementation. The
default authority resolves to `%LOCALAPPDATA%\PrintFlow Studio\workstation-automation-v1.db`;
the final test fixtures inject an isolated database path and resource identity. Initialization is
idempotent and uses only the current operator's application-data directory, with no service,
machine provisioning or broad permission grant. Two preliminary production-gate test executions
used the default authority before those fixtures were corrected, as recorded in the handoff and
audit report; the actual store was not inspected or altered afterward to conceal that deviation.

The lease object is the unforgeable owner capability. A failed acquisition returns no lease. A
nested helper can receive the acquired lease explicitly and create a child scope without another
store acquisition; child release cannot release the root. Process identity and ambient context
never confer ownership. A separate operation, including one in the same process, performs a fresh
atomic store acquisition and therefore competes normally.

Production PDF inspection and raster preparation remains subject to its existing Production
environment gate and per-business-database attempt/correlation lifecycle, but it runs in process
and does not control Meitu or Photoshop. Its explicit internal-work gate route omits only the
physical lease-availability observation and re-runs every other automatic and live workstation
check. It neither supplies nor fabricates an owner capability. A known Busy observation still
refuses ordinary external admission but preserves the already-certified live identities so this
internal route can immediately re-inspect them; Unknown and all other evidence-invalidating
failures retain their previous invalidation behavior.

The authority row carries the opaque token, machine, PID, process name and process start time.
Passive observation opens that authority database read-only and reads the committed row without acquiring or releasing anything: null is
`Free`, an exactly live owner is `Busy`, and unreadable/unverifiable/stale data is `Unknown`.
Missing or failed store initialization is `Unknown`, never fabricated `Free`. Acquisition commits
the owner row before returning a usable capability, so no operation can begin in a pre-publication
window. Release clears the row only through token compare-and-swap; if the write fails it reports a
material release failure and observation does not claim Free. No TTL evicts a live, suspended,
stalled or unobservable owner. A subsequent acquisition may replace an owner only after exact
machine/PID/name/start-time liveness proves that controller died. Reacquisition never bypasses the
existing safe-start/document/dialog checks.

## 4. Ordering and lifecycle

For a Session operation the order is: physical lease -> complete environment reinspection with
that exact explicit own-scope capability -> opening database transaction (`Running` attempt + per-DB correlation lock) -> external adapter ->
bounded stop/unwind -> closing database transaction -> physical release. If opening persistence
fails, no external call occurs and the lease is released. If closing persistence fails, the old
truthful `Running` attempt/per-DB lock remains for startup recovery; the external adapter has
already returned or completed its bounded stop path before physical ownership is released.

Live verification acquires the same lease before `EnsureReady`, runtime inspection or the probe and
holds it until its complete unwind. Passive reobservation reads the authority database only; when called
from an explicitly owned scope it recognises that exact lease as its own without treating it as a
competitor, but still performs every other readiness check.

| Point | Physical lease | Per-DB state / behavior |
|---|---|---|
| Pre-acquire | None; passive gate checks only | Existing state is read; no external side effect. |
| Acquired | Exclusive owner token committed in the canonical authority | No workflow ownership claimed yet. |
| Attempt committed | Still held | `Running` attempt and correlation lock committed atomically. |
| External work | Still held | Only this explicit scope may reach a real adapter/probe. |
| Termination / unwind | Still held | Cancellation waits for the adapter's bounded return; exceptions are contained. |
| Persistence outcome | Still held until commit attempt completes | Success/failure closes normally; failed close leaves truthful recovery metadata. |
| Release | Only the owning root scope can clear its exact token; failure is surfaced | No successor token is fabricated and per-DB history is not rewritten. |
| Owner death | Exact controller identity is proved dead during a later acquisition; no elapsed-time takeover | The shared row is replaced atomically; stale business-DB attempts remain for existing Interrupted recovery and safe restart/manual/abandon choices. |

## 5. Entry-point inventory and intended ownership

| Entry / caller class | Current physical admission | R2 treatment |
|---|---|---|
| `SessionService.RunProducingStepAsync`, including Meitu, Photoshop output, and PSD preparation | Per-database `AutomationLock`; gate first | Acquire shared physical lease, then re-run every gate check with that explicit own scope, then surround opening commit, adapter and closing/unwind; retain DB lock as correlation metadata. Fake/manual/internal work stays lease-free. |
| `ProductionLiveWorkstationVerifier.RunAsync`, including Meitu/Photoshop readiness and Photoshop round-trip probe | `SqliteEnvironmentAutomationLock` from caller's DB | Replace with the shared lease for the whole live phase. |
| `ProductionLiveWorkstationVerifier.Reobserve` and readiness/settings readers | Reads per-DB lock; performs no acquisition | Passive shared observation with own-scope recognition; no acquisition or fabricated live evidence. |
| `StandardRegressionSetWorkstationSmoke` / `RegressionBootstrapWorkstationVerifier` composition | Uses the run's `regression-run.db` | Preserve first-action run claim; every real verifier/session operation resolves the same shared authority. Prove composition with an injected isolated lease path and recording effects. |
| Normal App `ServiceRegistration` | Session and verifier share the App DB | Register one canonical lease manager independent of the DB and inject it into both paths. |
| Direct Meitu production foundation/processor callers in `MeituWorkstationSmoke` (locator/discovery, busy-cancel, supervised-stop, production processor paths) | Runnable opt-in callers can drive the real desktop without a shared physical lease | Acquire an explicit production lease after opt-in safety checks and enclose each real operation. |
| Direct Photoshop `CreateFoundation`, `CreatePreparationAutomation`, `CreateW1Automation`, `CreateTiffAutomation`, and `CreateProductionProcessor` callers, plus `PhotoshopOwnedDocumentCleanupProbe` open/close, `PsdPreparationWorkstationSmoke`'s direct `EnsureReady` phases and `VisualOnlyProductionWhiteInkSmoke`'s direct readiness/TIFF phases | Runnable opt-in callers can drive the real desktop without a shared physical lease | Acquire an explicit production lease after opt-in checks and enclose each mutating operation. When a workflow operation and direct chain share a test, scope only the direct phase or pass the scope explicitly; never let `SessionService` blindly reacquire its own outer lease. Passive evidence-only smokes remain lease-free where code inspection confirms no launch/focus/input/script/probe. |
| Startup recovery | Reads/releases only the per-DB row using liveness | Keep attempt/DB recovery unchanged. It cannot release the shared physical lease; a later acquisition replaces the authority row only after exact controller liveness proves the owner dead. It continues to mark Interrupted and never resumes external state. |

The report records the inspected caller inventory. A factory's existence is not counted as
admission; its operation caller is.

## 6. Focused proof and safety

1. Pre-fix same-process/two-database reproduction through recording adapters.
2. Two independent `dotnet test` child processes with distinct initialized business databases,
   explicitly coordinated by barrier files, call the shared lease manager directly against the
   same isolated authority: one holds; the second is refused with zero recorded operations; after
   release it acquires. This is a manager/process-boundary proof, not a second SessionService test.
   No fixed sleep proves overlap; waits are watchdogs only.
3. Two independent operations in one process exclude; an explicit nested scope does not reacquire
   or prematurely release the root.
4. Failed acquisition yields no scope; repeated/stale disposal and non-owner behavior cannot affect
   a successor.
5. Normal completion, adapter exception/cancellation, opening-commit failure and closing-commit
   failure prove lease/attempt/DB ordering and retained history.
6. A helper process owned by the test exits while holding and the parent reacquires; a live helper
   cannot be evicted by elapsed time.
7. App/SessionService, real live verifier, regression bootstrap and direct-smoke helper share one
   isolated authority and compete at recording external seams. The bootstrap forwards the exact
   own-scope lease through both real and without-revalidation verifier calls.
8. Fake adapter and internal deterministic work remain usable while another synthetic lease is held.
9. With established synthetic live evidence and an isolated external scope held, the real
   `VerifiedEnvironmentGate` still refuses ordinary Production admission while an app-composed
   `WindowsPdfPreparationProcessor` passes the internal route, re-inspects all non-lease facts,
   completes its normal database lifecycle and leaves the external scope held.

All test invocations clear every opt-in `PRINTFLOW_*` execution variable process-locally. Child
processes receive an explicit safe environment and an isolated lease path. No test opens the
production DB or workspace or an external application. After the preliminary default-authority
deviation described above, every affected fixture was audited and given an isolated lease path;
the focused runs used only those corrected fixtures. Tests that mutate process-global
variables use the existing `EnvironmentVariableCollection`.

Focused lease, admission, persistence/recovery, composition and directly affected R1 tests are run
with `C:\Users\admin\AppData\Local\Microsoft\dotnet\dotnet.exe` (SDK 10.0.400). Before the full run,
the isolated affected union passed 195 / 195. The one authorized full Product-suite run then ran on
commit `689770a` and failed: 11,788 passed / 31 failed / 0 skipped, total 11,819. All 31 failures
were the same R2 classification defect: 28 `PdfPreparationWorkflowTests` and three
`VisualOnlyPdfInputContractTests` were incorrectly required to compose the physical workstation
lease even though their Production PDF processor runs in process. Raw evidence is
`artifacts/pf-audit-r2/final/pf-audit-r2-full.trx` and `artifacts/pf-audit-r2/final/full.log`.

After the bounded PDF correction, the affected union plus both PDF classes passed 228 / 228 with
zero failures or skips. Raw evidence is
`artifacts/pf-audit-r2/corrected-targeted/pf-audit-r2-corrected-targeted.trx` and `.log`. The full
suite was deliberately not run a second time. Final corrected source therefore has clean focused
evidence but no passing full-suite claim; delivery must retain the failed full-run result rather
than present the earlier R1 baseline or the targeted correction as a final-source full pass.

## 7. Stages and routing record

| Stage | Completion | Route and context |
|---|---|---|
| Reproduce and inventory | Failing counterexample captured; real callers classified | NormalRoute / RequestedRoute / ExecutionTarget: Sol High, offset 0, `UNCHANGED`; ActualRoute `UNVERIFIED`; concurrency boundary; `CONTINUE`. |
| Implement lease and wire normal/verifier/compositions | One shared authority; explicit ordering/release; no R1/R3 contract drift | Sol High, offset 0, `UNCHANGED`; ActualRoute `UNVERIFIED`; persistence/concurrency/recovery; `CONTINUE`. |
| Focused lifecycle and cross-process proof | Required matrix passes with raw TRX/log evidence; no live opt-ins | Sol High, offset 0, `UNCHANGED`; ActualRoute `UNVERIFIED`; critical concurrency tests; `CONTINUE`. |
| Documentation and handoff | Final entry inventory, limits, exact commands/counts and open build-origin condition recorded | NormalRoute Luna Low; offset 0; runtime switching unavailable (`MODEL_SWITCH_UNAVAILABLE`), ActualRoute `UNVERIFIED`; continue safely in current runtime; `FRESH_REQUIRED` for the parent's independent reviewer. |

This worker makes no commit; the parent owns any authorized local logical commit. No branch,
worktree, clone, amend, rebase, push, install, deploy, Jira transition, production
read/write/revalidation, preset/Adobe/global-rule change, or live application execution is in scope.

## 8. Parent delivery record

Initial reviewed implementation: local commit `689770a3a667f1a0a29954cfe1d8412cc0f16c69`.
Independently re-reviewed PDF correction: `99caa61415b45fd0f50ed09076a8fd3cc2fa8ee3`.
The parent clean-built corrective committed source with SDK 10.0.400 (0 warnings / 0 errors)
and reran only the affected union plus both PDF classes against that build: 228 passed /
0 failed / 0 skipped, 40 seconds, exit 0. Raw TRX/logs and source identity are in
`artifacts/pf-audit-r2/correction-final/`. The original single full-suite failure is retained;
no second full suite ran, and no final-source full pass is claimed.

At the parent context boundary, Astra High was reconsidered: only bounded review follow-through,
validation and documentation remained. No runtime downgrade or model switch was verifiable, so
none is claimed. The one fresh independent reviewer was reused for narrow corrections;
no extra reviewer or user-owned task was created. Routing requests are not actual-runtime proof.

Delivery is PARTIAL because full-suite proof for the corrected ownership boundary remains
incomplete. The historical actual-default-lease test acquisition remains an explicit unwaived
execution deviation. All scoped work is committed locally in two code commits and a separate
documentation commit; tracked state is clean and only the operator prompt bundle is untracked.
Nothing was pushed. Real external applications NOT RUN; real production revalidation NOT WRITTEN;
Jira acceptance statuses unchanged. Stop before R3.
