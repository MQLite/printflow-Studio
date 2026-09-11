# PF-AUDIT-R3 — Handoff

Status: final review/QA closure pending; do not infer PASS from this draft.

- Authorized task: R3 only, canonical `D:\Repositories\printflow-Studio`, `master`.
- Starting HEAD: `519be2421e6dc6ec968caa5e550700231a3a57b1`; SDK selected/reported 10.0.400.
- Installed routing policy 2.3 / explicit route_offset 0. CONTINUE for related work; one fresh
  read-only native reviewer requested at gpt-5.6-sol/high. Runtime selection UNVERIFIED;
  in-place MODEL_SWITCH_UNAVAILABLE, safe supported current-runtime fallback.
- Inherited R2 final-source evidence: 11,819/0/0, Release 0 warnings/errors, tested 2f3d830,
  later docs commit 519be24. Not rerun at startup. Historical R2 failed run/default-store
  acquisitions remain in the unchanged R2 records.
- Operator-owned untracked `printflow-remediation-prompts/` preserved and excluded from commits.

Read PLAN.md and `docs/printflow/audit-r3-readiness-lifecycle.md` for the exact residual gaps,
stage semantics, synchronization correction, synthetic proof and source-verified R4 entry.
R3 adds optional Lifecycle metadata through the actual verifier/gate/report/JSON and existing
localized technical-detail surface. It preserves first/secondary failures and records cleanup
only if the existing ownership guard actually runs it. Prior successful evidence, current
admission and the latest returned diagnostic attempt are distinct. Revision comparison prevents
old passive reads from clearing new evidence or authorizing after a newer invalidating failure.

R2 Busy/Unknown/own-scope/internal-PDF and regression-bootstrap semantics remain required.
Photoshop open/identity/close protocol, Generator/Crop/IME/settings, R1 binding/publication and
operator environmentReadinessPassed attestation are unchanged. The unresolved R1 pre-A1/A2
build-origin condition remains open and is neither solved nor waived here.

## R4 — future authorization boundary

Actual existing diagnostic: `WorkstationVerificationSmoke.Run_the_explicit_live_application_verification`.
Exact source opt-in: `PRINTFLOW_WORKSTATION_LIVE_VERIFY=1`; exact test filter:
`FullyQualifiedName=PrintFlow.Tests.Smoke.WorkstationVerificationSmoke.Run_the_explicit_live_application_verification`.
Normal App composition, configured workspace, scratch business DB and R2 shared physical lease;
all normal automatic checks including ProductionRevalidation must pass. No bypass flag exists.
The report is serialized in the test's existing output. Read its Verified/checks/Lifecycle rather
than treating host exit 0 as a pass. This is bounded whole-live-readiness, not the standard set,
does not earn its verdict, and writes no production revalidation.

Expected optional fields: Lifecycle.LastSuccessfulLiveAt, Meitu/Photoshop observed process
identities, EvidenceAvailable, CurrentObservationDeferred, LatestAttemptAt and LatestProbe;
probe operation/path/process, Stages, LastAttemptedStage, LastConfirmedStage, CleanupOutcome,
CleanupReason, PrimaryFailure and SecondaryFailures. Absent history is unknown. LatestProbe
describes a returned attempt, never an unobserved in-flight success. No lease capability appears.

Separate R4 authorization must explicitly cover real application/workstation-store actions and
safe controlled diagnostic prerequisites. If normal automatic checks block, any missing narrow
non-authorizing wrapper is a separate R4 design boundary; do not expose the regression bootstrap
to normal production or invent CLI controls. No Photoshop-only command is supplied.

Real external operations NOT EXECUTED. Real production revalidation NOT WRITTEN. Jira acceptance
statuses unchanged. No push/install/deploy, branch/worktree/clone, amend/rebase or attribution
trailer. Stop before R4 and live acceptance.
