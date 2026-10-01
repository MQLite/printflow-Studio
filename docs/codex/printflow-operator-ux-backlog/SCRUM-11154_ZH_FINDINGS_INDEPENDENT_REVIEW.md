# SCRUM-11154 findings remediation — independent review record

One fresh read-only reviewer (`personal-dev-reviewer`, definition model `opus`; runtime model/effort **UNVERIFIED**) was used in one continuing context for four rounds. It read diffs and current source only; **it ran no tests, builds, live smokes or UI sessions** and said so each round. Its inputs were the verbatim task requirements, the diffs and the source, not the implementer's reasoning; it noted that it inherited the coordinator's task framing. **This file is a condensed record of the returned reports, not their full text.** The returned reports are retained locally with the owner-review packet. Review clearance is not human acceptance.

## Round 1 — F-V7 test-entry boundary, before any runtime graph

Verdict for running PrepareAndSmoke on a fresh disposable root: **PASS WITH FINDINGS**.

| Id | Sev | Finding | Disposition |
|---|---|---|---|
| F1 | P1 | The fault gate handled faults in every mode but only Interactive watched it; noninteractive runs could keep executing after a handled fault, and some awaits were unbounded. | Fixed: first fault cancels the run token and disables the window in every mode; scenario and screen waits bounded; exit 5 whenever a fault was recorded. |
| F2 | P2 | No process-level proof of the RuntimeBootstrap fault path. | Fixed: test-only `--InjectHostFault` (fresh PrepareAndSmoke only) and one process run on its own disposable root (see results). |
| F3 | P2 | The new-folder refusal stopped at the first protected ancestor without verifying it. | Fixed: ancestor marker/state verified first; a tampered ancestor throws. |
| F4 | P3 | Exit 4 lost the fault cause. | Fixed: quiescence record names `HostFault`. |
| F5 | P3 | A fault after `prepared.json` leaves the record. | Documented: runbook requires PrepareAndSmoke exit 0 before any Interactive use. |
| F6 | P3 | Window could be shown after a fault during startup navigation; input window before disable; "settled" overstated. | Fixed: show only without fault; synchronous disable; `TrackedOwnedWorkSettled` with explicit limitation. |
| F7 | P3 | Ledger binding correct; minor ordering notes. | Accepted as read. |
| F8 | P3 | Test hygiene (retained roots, junction cleanup), untested paths. | Junction removed in `finally`; nested junction case added; roots retained like the existing prerequisite fixtures. |
| Obs. | — | Pre-existing nonfatal refusal of noncanonical/8.3 alias strings. | Left for the owner to confirm (nothing is admitted). |

## Round 2 — recheck of the F-V7 corrections

Verdict: **PASS WITH FINDINGS** for (a) one PrepareAndSmoke on a fresh root and (b) one separate `--InjectHostFault` run on another fresh root, with conditions: external time limit; (b) passes only with exit exactly 5, one `host-fault-*.json` (InvalidOperationException, `PreparedRecordWritten` false), a quiescence record naming the fault, no `prepared.json`, no `graph-smoke.json`, no window; exit 4 is inconclusive; root (b) retired; root (a) usable only with exit 0 and a matching `ScenarioLedgerSha256`.

Residuals recorded (P3 unless noted): R1 (P2) process proof pending until run (b); R2 some product commands in the smoke are not individually bounded; R3 a fault before the run's `try` exits 5 without fault/quiescence records; R4 cancellation-callback exceptions in the handler are not recorded; R5 nonquiescent teardown exits 4; R6 F5 control is documentary; R7 the injection flag is not recorded in ownership/prepared files. Launcher needs PowerShell 7 and `dotnet` resolvable (pre-existing).

## Round 3 — product delta F-V2 to F-V9

Verdict: **PASS WITH FINDINGS**, not ready for acceptance until P2s resolved. F-V2, F-V3, F-V8, F-V9 met as read; no change to gate decisions, failure codes, persisted evidence, schema or domain commands.

| Id | Sev | Finding | Disposition |
|---|---|---|---|
| P2-1 | P2 | The inline confirmation pushes the lists down, so the second click of the initiating double-click can hit another job's Remove/Restart/Open/Abandon. | Fixed: lists inert (`AreListsInteractive`) and all list commands ignored while a confirmation is open; a second Abandon no longer replaces the pending one; test added. |
| P2-2 | P2 | `RecoverySurfaceLiveSmoke` phase C still expected one-press Abandon. | Fixed in source (confirm via fresh Space); live smoke NOT RUN. |
| P2-3 | P2 | Dropdown items (reject reason, return-to) could be announced by type name. | Fixed: item-container names from labels and `ToString()` returning the label; expanded-dropdown UIA check NOT RUN. |
| P2-4 | P2 | Excluded window/startup classes, focus behaviour, physical/UIA runs and opt-in captures not run. | Recorded as NOT RUN. |
| P3-1 | P3 | F-V5: status kept whole but its line does not wrap; long name trims (tooltip mouse-only); `ShowsName` check is weak. | Recorded; "sensible wrapping" reading left to the owner. |
| P3-2 | P3 | Recovery `PreconditionNotMet` now shown as "state changed"; success notice new. | Supported by producers; recorded. |
| P3-3 | P3 | F-V8 extra aggregate loads; "reopen" covered as service reload only. | Recorded. |
| P3-4 | P3 | F-V9 notice ("尚未就绪") and description ("未能就绪") differ slightly; both supported. | Recorded. |
| P3-5 | P3 | F-V2 headline also appears in production when live checks have not run (truthful); AutomationIds now exposed on item containers. | Recorded. |

During this round the first F-V5 layout (wrapping status + compacted spacing) was replaced after the affected run showed it broke the existing exact picture-area lock; the reviewer assessed the current template.

## Round 4 — recheck of the product fixes

Verdict for publishing the scoped delta: **PASS WITH FINDINGS**, conditions: publish exactly the scoped files; exclude the stray empty root `RuntimeBootstrap.cs` (removed; it was an artefact of a failed relative-path script in this task); record NOT RUN items as NOT RUN. P2-1 and P2-2 verified fixed in source; P2-3 set but not proven at runtime (expanded dropdown); upper Home actions stay active while confirming, which the reviewer judged acceptable because they are not displaced and Refresh/navigation withdraw the confirmation.

## Codex closeout evidence review (separate from the implementation review)

A fresh isolated read-only subagent reviewed sections 7–9 of the original prompt against current documents, retained direct TRX/runtime/export evidence and actual provisional packet. Requested gpt-6-sol High, route_offset 0; actual runtime telemetry UNVERIFIED. It did not run product tests, builds, UI or runtime graphs and did not redo the implementation review. Verdict: **PASS WITH DOCUMENTED RESIDUALS, no outstanding P1/P2 closeout finding**.

It found and rechecked two corrections: (P1) sharing-copy TRX still exposed local machine/user/deployment identity and absolute paths; these were redacted, preserving counters, test IDs and outcomes; (P2) the F-V7.3 matrix overstated no-prepared/exit-5 for general faults; it now qualifies that to the tested injection and states startup/late-record/exit-4 limitations. Original private evidence remained unchanged. Its full returned report is included in the owner packet as `review/closure-evidence-review.md`; this section is a summary.

The reviewer independently checked the provisional packet's exact members/hashes and privacy, historical HANDOFF suffix/reference ZIP, sharing TRX counters, normal ledger binding, injected-fault record and CSV structure. It did not independently recompute all 757 source/808 candidate comparisons. Final documentation SHA, local audit and packet hash are deliberately verified after publication by the coordinator, not claimed by the earlier provisional review. Human acceptance remains open.
