# SCRUM-11152 readiness signal closeout

Task: `PF-OPUX-v1-SCRUM-11152-signal-closeout-v1`. Baseline master `20ece916d290e489fe64a58155800a873dde5ed0`; no reset. Four earlier publication audit residues, the prior packet and evidence are preserved. Scope is the supplied closeout prompt; publication/Jira/packet are coordinated separately.

## Integration contract (written before product edits)

```text
Readiness.Open/Refresh -> IEnvironmentDiagnostics.Read ------------------+
Settings.Open --------> IEnvironmentDiagnostics.Read ------------------+
DiagnosticPackageService.BuildPlanAsync -> IEnvironmentDiagnostics.Read +
Readiness.RunLiveChecks -> IEnvironmentDiagnostics.RunLiveChecksAsync --+--> VerifiedEnvironmentGate
automatic step -> IEnvironmentGate.Verify(Production[, ownLease]) -----+       |
                                                                            v
                                                        existing verifier result (unchanged)
                                                                            |
                                                     existing ToReadinessReport projection
                                                                            |
                                       Workflow IEnvironmentReadinessObservations write-only port
                                                                            |
                                       App ReadinessObservationAccessor (process singleton)
                                                                            |
                                       Home view lifetime + dispatcher -> summary only
```

`VerifiedEnvironmentGate` remains the only production `IEnvironmentDiagnostics` implementation. No wrapper, new verifier, check, poller, persistence or permission path. Reports retain `Verified`, first-blocker order, applicability, lifecycle and `ObservedAt`. `Authorise` continues to consume the exact original verification result. Diagnostic export success/failure never changes its embedded observation.

| Existing source | Result and timing | Logical observation / cancellation | Completion eligibility |
|---|---|---|---|
| Readiness passive | `Read` -> `Verify()` -> `ToReadinessReport`; verifier's `ObservedAt` | Gate owns one ticket for the read; thrown read abandons | Full authoritative report |
| Readiness explicit live | `RunLiveChecksAsync` -> existing automatic + live result; final verifier clock | One ticket spanning the whole live call, no publication from its internal automatic phase; thrown/cancelled or returned `Cancelled` check abandons | Full final report only; no cancelled pass |
| Settings passive | same gate `Read`, scheduled off UI thread | Same gate ownership; remove page Begin/Complete duplication | Full authoritative report |
| Production gate (including own lease) | `AuthoriseProduction` -> `Verify(lease)`; same result both for admission and report | Begin before existing call; complete or abandon without altering return/throw/lease | Full authoritative report |
| Diagnostic package | `BuildPlanAsync` line 68 existing `Read`; later archive/write is independent | Same gate ownership, exactly one read; package outcome irrelevant | Actual embedded report |
| Internal production admission | `VerifyForInternalWork` may omit automation availability (`VerifyCore(...false)`) | Separate gate operation; no nested call from live verifier | A failed report can withdraw pass; a relaxed internal success cannot restore main Ready (abandon/unconfirmed) |

Generation is monotonically increasing under the holder's lock, not wall-clock ordering. Begin hides the old report; only the current ticket can complete or abandon. Update immutable state before optional notifications. Each subscriber is isolated so a throw/disposal cannot change any operation result or ownership. Notifications carry no report: Home reads the latest state on its own dispatcher, so reordered queued notifications cannot revive old state.

Home subscribes only while its view is Loaded, detaches on Unloaded/DataContext change, and guards queued dispatcher callbacks against the old subscription. It refreshes only `Readiness`, never job lists, notices or focus. Construction/ordinary refresh/localization/navigation invoke no new diagnostics.

Passive prequeue cancellation has atomic ownership: immediately before `Task.Run`, capture the exact immutable `Current` reference. Pages perform no Begin/Complete. In a token-cancelled `OperationCanceledException` catch, call `AbandonIfUnchanged(captured)`. Under the same holder lock, compare `ReferenceEquals(_current, captured)`; only while unchanged increment the monotonic generation and replace state with Unfinished. Any gate Begin/Complete/Abandon or newer observation changes the reference, making this conditional invalidation a no-op. Thus a cancelled never-started request withdraws the old pass but cannot suppress any newer authority result; a gate-run cancellation produces no duplicate observation. Settings captures after preferences load, immediately before its diagnostic worker. Optional sink method exceptions are isolated at the gate in addition to subscriber exceptions inside the holder; no observer exception can replace an authority result/throw.

Ordering for reads that execute starts at actual gate entry. Once synchronous `Read` starts it is not cancellable; its actual report is retained even if the page token is subsequently cancelled. Every accepted holder transition allocates a new immutable snapshot. Focused tests cover unchanged captured pass -> prequeue cancellation -> Unfinished; captured pass -> newer failure/pass/in-progress -> old cancellation is ignored; real cancelled-before-worker page invocation; started synchronous read remains authoritative.

## Plan and validation

1. Independent contract precheck before cross-layer wiring. STOP at this boundary until coordinator supplies result.
2. Add behavioral RED tests using real gate and synthetic workstation / isolated package fixtures; compile and retain failing logs. No compile failure counts as RED.
3. Implement narrow publication port, gate ownership, remove page duplicates, singleton DI and lifetime-aware Home notification. Prove cancellation, slow earlier completion, later success and subscriber isolation.
4. Apply authorized bilingual Home copy, Home-only navigation and diagnostic-cleanup resources, exact technical CheckKey plus state. Preserve shared labels and warnings elsewhere.
5. Focused tests; one settled build; affected architecture, verification, diagnostics and safe UI groups. Render en/zh-CN at 1000x700 and 1920x1040, 96 DPI, including all disclosures and recovery reachability. Capture candidate file hashes/logs in `artifacts/pf-opux-scrum11152-closeout/`.
6. Same independent reviewer inspects final diff, evidence and exact copy; correct and recheck affected findings. Root coordinates publication, Jira and immutable packet.

Fixture inspection: `WorkstationVerificationFixture` creates synthetic manifest, executables, evidence and workspace beneath its unique temp root, uses fake facts/time, no live verifier. `SessionServiceHarness` uses unique temp SQLite/workspace, fake processors and a lease DB/name within the owned workspace. Existing off-screen `WpfRendering` is the only rendering mechanism. Real-window/UIA, ApplicationStartup, ProductionComposition, OperatorInteractive remain excluded; the known hanging recovery test remains NOT PASS. No broad 11,531-case suite.

Original AC map: AC1 targeted for full automated closure through all five observations and current Home updates; AC2 current-run empty; AC3 pass with true check time; AC4 recovery/count preservation; AC5 bilingual synthetic render/keyboard reachability, with physical input/other DPI/human acceptance NOT RUN. Final disposition depends on evidence, not this plan.

Routing: policy 2.4, route_offset 0, UNCHANGED. Normal/requested/execution target Astra 6.0 High (`gpt-6-astra`, High supported by native agent schema); actual model/effort UNVERIFIED. CONTINUE for this tightly coupled authority-to-view unit: splitting the small port from its cancellation/ordering UI contract would duplicate substantial context. Reassess after integration and visual evidence. Review is FRESH_REQUIRED in a separate coordinator-managed context.

## Implemented contract and evidence

The precheck initially held only the passive prequeue cancellation race. The same independent reviewer cleared the amended exact-snapshot conditional invalidation contract before any product wiring. Its original finding and recheck remain in the local `PRECHECK_REVIEW.md`; no broader behavior or permission change was required.

The implementation follows the graph above. `IEnvironmentReadinessObservations` is a write-only Workflow port implemented by the existing App holder. DI resolves that same singleton into the sole gate. Gate publication wraps existing observations; verifier calls, admission results and lease ownership remain authoritative. Cancelled result rows are recognized by their original `FailureCode.Cancelled` before projection. One full live call owns one generation; no automatic intermediate result is published. Internal-work success only completes as unconfirmed because its availability check is intentionally narrower.

`HomeView` attaches on Loaded, detaches on Unloaded/DataContext replacement, and queues a callback on its own dispatcher. A subscription generation guards queued obsolete callbacks. Each callback rereads current immutable state; it cannot revive an older report. No list reload, notice clearing or focus action occurs. The gate/holder publish state even when there is no Home view.

The existing first-blocker, not-run and no-reason projections are retained. The new Home button resource preserves the navigation command and destination. The exact technical CheckKey now includes its localized status (including Not run / 未运行). Cleanup warning wording is Home-only. The old copy review and old evidence are unchanged; new exact wording is in [the closeout copy review](SCRUM-11152_CLOSEOUT_COPY_REVIEW.md).

Behavioral RED before product edits: four tests failed for stale reopened/current Home and cleanup ambiguity, plus the actual diagnostic-package observation test failed. Both builds passed first. Intermediate correction attempts remain recorded: a package test wrongly expected name collision to fail (the writer correctly chooses a unique name); the test now creates an owned file at its staging-directory path to force a real write failure. One settled test asserted the old one-argument gate constructor; it now explicitly permits only the verifier and this write-only port. The sole-diagnostics-implementation architecture assertion is unchanged.

Candidate 02 focused tests pass 155/155, architecture tests pass 473/473, and the affected safe UI/resource set passes 225/225, with overlapping groups never summed. A clean `--no-incremental` product build passed with zero warnings/errors, followed by a clean build for the test-only constructor assertion correction. Final capture/reachability cases pass 4/4 and produced 48 off-screen PNGs: both locales, both viewport sizes, 96 DPI, including all disclosures and last recovery action. Exact commands, candidate hashes, check times and complete results are recorded in the closeout local RESULTS and packet. Candidate 02 source hashes matched after all runs.

### Original AC mapping

| AC | Current technical disposition | Direct evidence |
|---|---|---|
| 1 Latest failed verification -> first reason and readiness navigation; never Ready | PASS automated | Real gate previous-pass/refusal/current/reopened Home; real package read failure despite successful archive; monotonic ordering, returned/thrown cancellation, current-view notification. No broad failure-code heuristic. |
| 2 No current-run verification -> not checked, no old pass | PASS automated | Existing new-run and composed-singleton graph tests; startup preset remains separate. |
| 3 Passed -> status and actual check time | PASS automated | Real synthetic gate, `ObservedAt` date/local-time assertions, later successful eligible observation restores status. |
| 4 Recovery list and counts retained | PASS automated | Existing recovery/reachability tests, summary update collection-change assertion; no recovery command changes. |
| 5 Both locales, supported layout and keyboard reachability | PARTIAL pending human check | Existing off-screen assertions at 1000×700 and 1920×1040, 96 DPI, both locales; physical keyboard and other scaling NOT RUN. |

No final human acceptance, production readiness, physical input or display-scaling acceptance is inferred from automated results. No SCRUM-11153 work. Publication/Jira/packet status is recorded separately by the coordinator after final technical review.
