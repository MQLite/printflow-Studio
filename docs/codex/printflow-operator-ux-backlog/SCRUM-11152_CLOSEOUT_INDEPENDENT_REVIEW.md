# SCRUM-11152 closeout — complete independent review record

Final technical verdict: PASS, no actionable findings. AC5/human acceptance remains open. One fresh reviewer context performed both the precheck and final review; no reviewer builds/tests were run. The initial HOLD and its clearance are retained below.

# SCRUM-11152 integration precheck

Task `PF-OPUX-v1-SCRUM-11152-signal-closeout-v1`. Independent read-only review, 2026-09-30. Product code not edited; no tests/builds, commits, Jira writes or UI automation run by this reviewer.

## Finding — contract HOLD before wiring

**P2: queued passive cancellation lacks a defined atomic ownership rule.** `SCRUM-11152_CLOSEOUT.md` says a request cancelled before its `Task.Run` starts needs an unfinished observation only if no gate observation began, but does not define how this is proven or ordered against other requests. Existing readiness and Settings pages begin before scheduling (`EnvironmentReadinessViewModel.cs:452`, `SettingsViewModel.cs:283`); moving ownership solely into `VerifiedEnvironmentGate.Read` means a pre-cancelled task never enters the gate. Conversely, issuing a fresh Begin/Abandon from a later catch can overwrite a newer legitimate report or in-flight observation. Specify a deterministic per-request/reservation or compare-and-update mechanism, including cancellation before scheduling, cancellation after the worker enters the gate, and cancellation racing a newer operation. One logical completed read must still produce one ticket. The implementation must not infer ownership from wall-clock time or a racy global state sample.

**Disposition:** precheck approval withheld only for this unresolved contract detail. No evidence currently requires permission, lock, verifier-lifecycle or persistence expansion; therefore `READINESS_SIGNAL_DESIGN_BLOCKED` is not triggered. A targeted amendment and same-context recheck are sufficient.

## Boundaries accepted in the proposed contract

- All five requested existing sources converge on `VerifiedEnvironmentGate`: readiness passive/live, Settings passive, full production admission (including an owned lease), and `DiagnosticPackageService.BuildPlanAsync`'s existing diagnostics read. No additional check or diagnostics implementation is needed.
- `AuthoriseProduction` can publish `ToReadinessReport` from exactly the result it already passes to `Authorise`; admission decisions, failure identities and lease handling must stay unchanged.
- The diagnostic package embeds the already-read report before later evidence inspection/export. Package success or failure has no readiness meaning.
- `VerifyForInternalWork` is an additional, separate PDF-admission path. `ProductionWorkstationVerifier.VerifyCore(... requireAutomationAvailability: false)` can pass while the ordinary full gate refuses the occupied automation domain. Publishing its failures is valid; its relaxed success must not restore Ready. Conservative unfinished/unconfirmed publication as proposed is permissible. Preserve actual operation return value.
- The live verifier calls its own `VerifyAutomatic`, not the gate, before the complete live result; publish only the full logical live result. Returned cancellation failure rows and thrown cancellation must not become invented workstation faults.
- Latest-started monotonic generation, immediate withdrawal of prior report at Begin, immutable state update before optional callbacks, and latest-state reads in dispatcher callbacks meet the stated ordering intent if implemented and tested.
- Home Loaded/Unloaded/DataContext subscription ownership plus a queued-callback generation guard is an appropriate view boundary. Refresh only the summary; do not reload jobs, clear notices or move focus.
- Keep `The_only_diagnostics_implementation_is_the_gate_itself` unchanged. The existing exact gate-constructor/field assertion will need an explicit narrow update for the approved observation-only port while continuing to forbid any other machine reader. Do not replace it with a broad exemption.
- Publication faults must be isolated for the whole optional transport, including Begin/Complete/Abandon and report projection as appropriate, not merely event subscribers. They cannot affect permission, original exceptions, successful file operations or lease ownership.

## Inspection and limitations

Read the entire supplied closeout prompt and authenticated original AC snapshot. Inspected the written contract, previous plan and HANDOFF top, owner-packet convention, authority/gate, verifier automatic/passive/live and internal paths, diagnostic-package service, readiness/Settings recording, holder, Home summary/view/model, transient navigation and DI, architecture diagnostics/verifier boundary tests, and existing internal/full admission regression shape. Baseline source still contains the known stale-Ready gap. No implementation approval, automated PASS or human acceptance is inferred from this precheck.

Routing policy 2.4; inherited `route_offset: 0`; NormalRoute/RequestedRoute/ExecutionTarget: Astra 6.0 High (`gpt-6-astra`, high). Actual model and effort: **UNVERIFIED**; no runtime model metadata is exposed to this review context. Context: fresh native review subagent, no parent exploration history supplied; same context retained for targeted contract recheck and final code review. No child agents dispatched.

## Targeted contract recheck — CLEARED FOR NARROW IMPLEMENTATION

Re-read the amended written contract in `docs/codex/printflow-operator-ux-backlog/SCRUM-11152_CLOSEOUT.md`. The passive prequeue cancellation paragraph now captures the exact immutable `Current` reference immediately before scheduling and calls `AbandonIfUnchanged(captured)` only on token-cancelled exception. Reference equality and the generation/state update occur under the existing holder lock; every accepted Begin/result/abandon replaces the snapshot. Any intervening authority activity defeats the conditional invalidation. This closes the P2 contract HOLD without nested observations or a broader lifecycle change.

Ordering is at actual gate entry for reads that run. The synchronous `Read()` has no cancellation argument: once entered, its actual completed report remains eligible even if the page token subsequently cancels. Conditional page invalidation covers only the never-entered case and must not generate a second ticket after any authority update. Verify both cancellation races and a real completed read producing exactly one observation in the implementation tests.

The amended contract also explicitly isolates optional sink-method faults in the gate in addition to subscriber faults in the holder. Narrow implementation may proceed; final acceptance still requires raw code, tests, actual rendered copy and evidence review in this same independent context. No product code or tests were changed/run by this reviewer. Original finding above is retained as review history; current precheck disposition is **CLEARED**, not final AC approval.


---

# SCRUM-11152 closeout independent final review

Task `PF-OPUX-v1-SCRUM-11152-signal-closeout-v1`. Same independent native review context as the contract precheck. Reviewer performed read-only source, diff, file-hash and saved-evidence inspection; ran **no builds, tests, renders, desktop control, commits or Jira mutations**. Only this report and PRECHECK_REVIEW.md were written by the reviewer.

## Findings and current verdict

**No actionable source defect identified.** The approved narrow transport is implemented within the authorized boundaries. The original precheck P2 cancellation-ownership question is closed by the implemented snapshot compare-and-update operation. No `READINESS_SIGNAL_DESIGN_BLOCKED` condition was found.

Source and final rendering/evidence review are complete for candidate 02. **Technical review PASS for this bounded closeout; no blocking findings remain.** This is not human acceptance, production-readiness approval, or publication/Jira audit approval.

## Candidate and inspected scope

Baseline `20ece916d290e489fe64a58155800a873dde5ed0`. Read `candidate-01-tracked.diff` and every changed product/test hunk, plus full untracked `IEnvironmentReadinessObservations.cs` and `HomeReadinessSignalCloseoutTests.cs`. Reviewed candidate 02's additional `VerifiedEnvironmentGateTests.cs` reflection assertion: the exact accepted constructor types are verifier plus the observation-only port. Gate-only diagnostics authority remains enforced by the unchanged test. Recomputed candidate 01's 15 hashes and candidate 02's 16 hashes: no mismatches at review time.

Reviewed the full supplied closeout prompt, authenticated original AC, approved contract and amendment, current copy-review table against both resource files, gate/passive/live/internal verifier boundaries, diagnostic-package service, page models, holder, Home model/summary/view lifecycle, transient navigation and singleton DI. Inspected synthetic workstation, Home/Settings and session fixture isolation, including test-owned SQLite/workspace and lease database/name. Read prior plan/copy/results/review context and preserved that evidence.

Also reviewed the bounded `Export-Wave1Jira.ps1` change: new closeout task allowlist, exactly-one-marker check for SCRUM-11152, write-action classification and notes only. It preserves the existing conversion schema and marks other issues read-only. This source review does not independently verify future authenticated snapshots, original comment preservation, exported data fidelity, remote publication or packet contents.

## Contract and behavior verified from source

- The five required paths now publish through the existing gate: readiness passive, readiness live, Settings passive, production admission with/without owned lease, and the diagnostic-package service's existing `Read`. No new diagnostic implementation, verification invocation, production condition, persistence, TTL or timer was added.
- Admission still calls the original verifier once and passes the same result to unchanged `Authorise`. Projection/publication errors are isolated from authorization. Original thrown verification errors and operation/lease behavior are preserved.
- Returned cancellation is checked on original verification rows before the App-safe projection loses FailureCode. Live caller cancellation also prevents publication. Neither case fabricates a blocker. The full live operation owns one ticket; its internal automatic result is never published.
- Internal PDF admission may omit automation availability. Its failed report can publish its real blocker; its subset success ends unconfirmed and cannot create a full Ready state. A fallback that really performs the full verifier remains eligible.
- The locked monotonic ticket makes latest-started authority work win. Begin immediately removes the old report; earlier late success/abandon cannot overwrite newer work. An actual subsequent eligible pass can restore Ready.
- Passive page models no longer Begin/Complete around the gate. Cancellation before worker entry uses reference identity against an immutable captured snapshot under the same lock, advances the ticket only when unchanged, and cannot overwrite any intervening authority update. A started synchronous Read remains authoritative despite later page-token cancellation.
- State updates precede notifications; subscribers are individually isolated and invoked outside the lock. Gate sink calls are also isolated. Home uses Loaded/Unloaded/DataContext ownership and a subscription generation to reject obsolete queued callbacks; callbacks read latest state on the view dispatcher. Only the summary changes: no job-list reload, notice clear, focus operation or diagnostics call.
- Package planning publishes its actual embedded diagnostics observation. Export success does not create Ready, and later archive/write failure does not create a readiness fault. Shared EnvironmentNotVerified codes elsewhere are not used as a heuristic.
- Main reason is still the first ordered authoritative blocker, with existing localization. Live not-run and missing-reason states stay truthful. Displayed time remains the report's actual ObservedAt with local date/time. Recovery commands/list, original-file protection, correction/final-save flows and workflow eligibility were not changed.

## Copy review

Exact changed en/zh-CN values match the new closeout copy table. Compact states, experienced-colleague/supervisor help, exact quoted Home button label, startup-only preset explanation and Home-only cleanup warning implement the supplied directions. Technical details retain the real CheckKey plus the existing localized report status. The obsolete claim that per-step checks do not update Home was removed; the remaining recorded-observation wording does not promise continuous monitoring. Shared readiness labels and the shared cleanup resource remain unchanged elsewhere. Human copy acceptance remains OPEN.

## Raw evidence inspected

- `red.log` / `red.trx`: four behavioral failures, covering reopened/loaded stale Ready and both cleanup locales; compiled source, not compile-error RED.
- `package-red.log` / `.trx`: one behavioral failure where the holder remained passed after the actual package diagnostics read failed.
- `green-02.log`: 73/73.
- `build-settled-01.log`: clean build, 0 warnings/errors.
- `settled-focused.log`: 154/155, with the second exact constructor assertion still expecting one dependency. This failed run is retained, not counted as PASS.
- Candidate 02's test-only assertion fix was inspected; `build-settled-02.log`: 0 warnings/errors; `settled-02-focused.log`: 155/155; `settled-02-architecture.log`: 473/473; `settled-02-ui.log`: 225/225, no failures/skips in these settled groups. Counts overlap and must not be summed.
- Inspected focused/UI filters. Excluded real-window/UIA, ApplicationStartup/ProductionComposition and interactive paths remain NOT RUN; known hanging recovery test is NOT PASS. The changed relevant constructor test was fixed and rerun, not excluded.

## Original AC disposition (source plus saved automated evidence)

| AC | Current review disposition |
|---|---|
| AC1 failed verification -> blocked, actual first reason, readiness button, never Ready | **PASS (technical/automated)** for the specific stale-Ready gap, including real gate/package seams and already-loaded Home. No failure-code mitigation substituted for full observation transport. Human acceptance remains open. |
| AC2 no verification this run -> not checked, never reused historic pass | PASS (automated/source); process singleton starts empty and startup preset is not readiness. |
| AC3 pass -> Ready with check time | PASS (automated/source); eligible complete full report and actual ObservedAt. |
| AC4 recovery list retained, counts under details | **PASS (automated/source/synthetic visual)**; commands/list preserved and final expanded-detail renders retain the last recovery actions. |
| AC5 both locales, supported layout/scaling, keyboard reachability | **PARTIAL**. Automated layout/control-property checks and inspected final synthetic 96-DPI renders pass. Physical keyboard, other DPI/scaling and Jira human check remain NOT RUN. |

## Routing and independence

Policy 2.4, inherited route_offset 0, adjustment UNCHANGED. Normal/requested/execution target: Astra 6.0 High (`gpt-6-astra`, high); actual model and effort **UNVERIFIED** because runtime metadata is not exposed here. Fresh review context did not receive parent exploration history; resumed the same context for contract and code review. Critical authority/AC audit plus UI contract/copy review continues to justify this route. No subagents were spawned by this reviewer.

## Final visual and evidence completion

Directly inspected these ten final PNGs in `renders-final/`:

- `home-live-check-pending-warnings-details-zh-CN-1000x700.png`
- `home-live-check-pending-warnings-details-en-1000x700.png`
- `home-all-details-recovery-end-en-1000x700.png`
- `home-all-details-recovery-end-zh-CN-1000x700.png`
- `home-not-confirmed-warnings-details-en-1920x1040.png`
- `home-not-confirmed-warnings-details-zh-CN-1000x700.png`
- `home-blocked-zh-CN-1920x1040.png`
- `home-ready-en-1000x700.png`
- `home-not-checked-zh-CN-1000x700.png`
- `home-checking-en-1000x700.png`

No actionable clipping or contradictory readiness/cleanup wording was found. The real technical key is labelled as an identifier with Not run / 未运行 in the pending case. The expanded-detail reachability images intentionally scroll the main summary above the viewport while showing the notice code, import control and last recovery actions; this is expected scrolling, not lost content. These images cover all main states and the materially risky bilingual expanded layouts at both requested viewport sizes. I did not visually inspect every PNG.

Read `renders-final/INDEX.md` and independently compared all 48 PNG file hashes with its entries: 48 matched. Recomputed all 16 candidate-02 source hashes: all matched. Read `candidate-02-verified.json` including captured binary hashes/times. Independently parsed the saved settled TRX XML: focused 155/155, architecture 473/473, UI 225/225, render/layout capture 4/4, each with zero failures or unexecuted cases. These are observed saved results from the implementer's runs, not test runs by this reviewer. The capture set is 48 synthetic off-screen PNGs, en/zh-CN at 1000×700 and 1920×1040, 96 DPI.

The initial `green-01.log` failed one package fixture expectation because exporting to an existing requested name succeeds through unique-name selection; the final test uses an owned file to prevent creating the staging directory and deterministically obtains a writer failure. This changes the test setup to exercise the intended existing failure branch; it does not weaken product behavior or treat that earlier run as PASS.

Final technical disposition: the missing authoritative observation signals are implemented; cancellation/order/subscriber/visible-view boundaries and reviewed copy are acceptable. AC1–4 have technical automated/source support as qualified above; AC5 remains PARTIAL. Owner copy acceptance and workstation human checks remain open. Code publication, final Jira readback/export fidelity and immutable packet validation are subsequent coordinator responsibilities and are not certified by this source/UI review.


Final documentation recheck: read the completed RESULTS.md and the closeout document's implemented-contract/evidence/AC sections. Their settled counters, retained failed attempts, candidate scope, fixture isolation, render limits and acceptance qualifications agree with the inspected raw evidence. The RESULTS creation-time note that review was still inspecting PNGs is historical; this FINAL_REVIEW.md is the completed review disposition. No further correction requested.
