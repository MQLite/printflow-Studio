# SCRUM-11130 execution plan

Policy: Codex Global Development Routing & Context Policy v2.2 (2026-09-08),
`PLAN_EXECUTE`. Canonical checkout: `D:\Repositories\printflow-Studio`, `master`, initial HEAD
`e0d81c8e6413a89c1afa5da2904d7e7987b9ea5d`, initially clean. No branch, worktree, clone,
amend, rebase, push, deploy, customer work, or Jira mutation.

Requested route is GPT-5.6 Sol Medium for workstation acceptance and bounded remediation. The
current task runtime does not expose verifiable model/effort metadata or an in-thread switch, so
actual model/effort is `UNVERIFIED` and `MODEL_SWITCH_UNAVAILABLE`; no model-switch claim will be
made. Re-evaluate only if a reproducible shared Product defect requires production adapter,
SessionService, workflow-state, persistence/recovery, or shared-validation changes.

## Original authority

CSV source: `C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv`. The CSV uses
legacy Work Item IDs rather than SCRUM keys. Exact title mapping is SCRUM-11130 = 11706,
SCRUM-11124 = 11700, SCRUM-11088 = 11303, SCRUM-11089 = 11304, SCRUM-11090 = 11305,
SCRUM-11092 = 11307, and SCRUM-11112 = 11505.

SCRUM-11130 / Work Item 11706 Description (the complete AC, verbatim):

> Execute the confirmed path JPG photo to enhancement to background removal to trimming to approved transparent PNG on the fixed workstation. Include rejected review, automatic retry, manual takeover, restart recovery, unknown dialog and output-validation failure cases. Verify the source remains untouched and the completed approved PNG is bound to the reviewed hash.

Parent SCRUM-11124 / Work Item 11700 Description (verbatim):

> Execute the final evidence-based QA and production release gate for the PrintFlow Studio MVP. Test workflow transitions, trimming, SQLite and file safety, Meitu and Photoshop Adapter success/failure matrices, all three fixed end-to-end workflows, multiple output sizes, Maintop compatibility, physical DTF prints, crash and unknown-dialog handling, retry/manual takeover, the 90 percent automation-success target and the 30 percent active-operator-time reduction target. The release gate must not mark unexecuted cases as passed and must produce a clear PASS, PASS WITH NOTES or NOT READY verdict before production use.

## Acceptance matrix before execution

| Original AC case | Existing automated evidence | Existing live evidence | What must be executed now | Pass condition | Evidence location |
| --- | --- | --- | --- | --- | --- |
| Operator-facing JPG -> Enhancement -> Background Removal -> Trim -> approved transparent PNG | Full fake-mode workflow; production Meitu seam, output validation, alpha and trim suites | Real Enhancement and Background Removal exist as separate adapter/smoke runs; no complete UI chain | Import supported portrait JPG from Home, choose Prepare Design Asset, drive every real Product step with Production adapters, independently read SQLite/files | One continuous session completes; exact attempt files/hashes and review bindings agree; source unchanged; lock released | `D:\PrintFlowStudio\Evidence\SCRUM-11130-*`; completion report |
| Meitu JPG -> PNG export route | Preset/parser/guard tests including fresh read-back | Evidence-first 1.17.0 remediation, but final portrait chain still pending | Capture the first live export's initial `jpg`, recognised combo/popup, runtime-derived `png`, fresh `png` read-back, then Save | Save is not invoked until fresh read-back is `png`; output fully decodes | Product structured evidence/log plus filesystem facts |
| Rejected review | Retry/review and downstream-authority tests | Shared review live proofs, not this fixed-workstation E2E variant | Reject a real produced Meitu Revision using a supported reason | decision and reviewed SHA persist; rejected Revision is not approved authority; history/reprocess route remains | SQLite/file audit and UI capture |
| Automatic retry | Workflow retry creates a fresh attempt/working copy from approved upstream; no background adapter retry is authorised | Prior synthetic/live retry evidence | Use a safe controlled failure, choose the Product's explicit Retry, then perform the successful retry with real Meitu when required | new AttemptId/path; failed evidence retained; no failed output reuse; upstream hash unchanged | SQLite/file audit and evidence logs |
| Manual takeover | Stop/Take Over/Hand Off and SubmitManualResult suites | SCRUM-11092 live synthetic WPF/UIA result import | Use existing takeover/handoff and a controlled local file through Product UI | automation stops safely; manual file validated/provenanced; new managed Revision reaches ReviewRequired; no mid-click resume | Variant session DB/files/UI evidence |
| Restart recovery | Startup recovery, Home card and recovery-action suites | SCRUM-11112 synthetic WPF/UIA A/B/C | Interrupt a meaningful Prepare Design Asset step through supported state, restart PrintFlow, use legal recovery action | card shows exact session/step/state; no external app starts on restart; restart uses clean upstream; manual import remains reachable where legal | Variant session DB/files/UI evidence |
| Unknown dialog | Production classifier/driver negative matrix; no speculative clicks | Prior Meitu adapter safety evidence, not this exact session | Use the smallest existing controlled adapter/harness seam that presents an unrecognised state without changing the signed preset | structured failure persists; no click/output Revision/review; restore Meitu manually after capture | Harness-labelled variant evidence and report |
| Output-validation failure | Missing/unreadable/changing/invalid-alpha validation and persistence tests | Prior fault-matrix evidence, not this exact session | Use the smallest safe existing seam through SessionService/production boundary | failure code persists; no Revision/review; retry is clean; failed evidence retained | Harness-labelled variant evidence and report |

Current accepted authority is `printflow-workstation-v1` version 1.17.0, manifest SHA-256
`A2E1936B355C28CDC9905EBF63107A7B4229B71D7586B3C304B7F284B59FCFA9`, Production adapter mode.
Historical SCRUM-11097/11129/11093 are not reopened. SCRUM-11065 remains independent and cannot
be called FULL without one genuine seven-category `Passed` run and required operator visual decisions.

## Execution stages

1. **CONTINUE — authority, integrity, environment.** Verify the manifest plus all 29 evidence
   hashes and required executable/action authorities; inspect the accepted SCRUM-11065, 11110,
   11112, 11082/11083, and JPG-to-PNG evidence. Establish recognisable clean app states without
   touching unknown/customer work. Run the existing production environment verification.
2. **CONTINUE — golden operator path.** Launch the current Release Product, import the controlled
   portrait JPG through Home/file picker/workflow selection, and drive Original Confirmation,
   real Meitu Enhancement/review, real Background Removal/review, alpha Trim/review, Approved PNG
   Export and completion. Capture UI, Product logs, SQLite rows, paths, hashes, image facts,
   source immutability and automation-lock release.
3. **CONTINUE — required variants.** Prefer real Product UI. Use bounded harness support only when
   a fault cannot safely be manufactured in Meitu, and label that evidence accurately. Preserve
   failed/rejected evidence and never fake an E2E with unit-only evidence.
4. **FRESH_REQUIRED — independent final acceptance audit.** A truly independent review context is
   required by the supplied handoff and policy. If no isolated reviewer mechanism is available,
   report `SELF-REVIEW ONLY / INDEPENDENT REVIEW NOT COMPLETED` and do not overclaim FULL. Review
   the exact AC, raw evidence, customer/operator safety, Production/operator-facing character and
   harness classification; fix documentation/evidence gaps proportionately.
5. **CONTINUE — report and delivery.** Create
   `docs/printflow/scrum-11130-prepare-design-asset-fixed-workstation-e2e.md`, append a dated delta
   without rewriting the historical coverage row, run targeted validation and a Release build if
   Product changed (documentation-only work does not justify a full suite), commit genuine tracked
   changes locally, and finish on clean `master`. Accepted full-suite baseline is 11,778/0/0;
   absent Product changes it is not rerun.

Completion requires the one real operator-facing Production golden path plus all six variants,
independent raw-evidence verification, factual Jira reassessment and a clean committed checkout.
