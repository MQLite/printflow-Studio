# SCRUM-11112 — Interrupted Attempt and Startup Recovery

Date: 8 September 2026. Status: **COMPLETE — SCRUM-11112 FULL; SCRUM-11107 FULL**.
Canonical checkout: `D:\Repositories\printflow-Studio`, `master`.

## Exact original Jira requirement

Source read directly: `C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv`.
The CSV has numeric source IDs, not Jira SCRUM keys. Original Work Item **11505**, title
**Implement Interrupted Attempt and Startup Recovery**, parent **11500**, maps to
**SCRUM-11112**, parent **SCRUM-11107**. Its Description is the complete original requirement;
there is no separate AC column:

> At startup, detect unfinished ProcessingAttempts and stale automation-lock state and mark such Attempts INTERRUPTED. Offer restart of the step from a fresh working copy, inspection/import of a manually saved result or abandonment of the Attempt. Never resume screen automation from the previous mouse position, selector sequence or uncertain external-application state.

## Pre-change requirement matrix

This reconciliation preceded Product edits. The historical 4 September audit was not used as
the implementation specification.

| Original AC clause | Current implementation before this slice | Evidence inspected | Remaining gap before changes |
| --- | --- | --- | --- |
| Detect unfinished attempts and stale locks; mark Attempts INTERRUPTED | Startup repairs persisted Running attempts and steps, releasing session locks only on confirmed process death | StartupRecoveryService, ProcessingAttempt.Interrupt, SessionStep, StartupRecoveryTests, SessionHygieneAndRecoveryTests | Environment-verification locks added by SCRUM-11110 have a purpose/token but no SessionId; startup overlooked them and the repository could not parse their timestamp format |
| Offer restart from fresh working copy | Retry sets Waiting without producing an attempt; later StartStep creates the new attempt/copy from the approved upstream; handed-off sessions use ReenterAutomation | WorkflowEngine.Retry/StartStep/ReenterAutomation, RetryAndReviewTests, RecoveryAndBranchTests | Explicit per-entry recovery action and proof of restart-before-Run persistence |
| Offer inspection/import of a manually saved result | SubmitManualResult already supports handed-off Enhancement/BackgroundRemoval with independent validation, managed copy, hash, provenance and ReviewRequired | ManualResultEligibility, SessionService, WicManualResultImporter, ManualResultImportTests, ManualResultFileTests; SCRUM-11092 evidence | Recovery must bridge an eligible Active interruption through existing HandOff and expose the same importer, retaining unresolved state after refusal |
| Offer abandonment | AbandonSession already records the decision and preserves source/history; Home already has general Abandon | HomeViewModel, SessionStateRules, workflow AbandonSession and Home tests | Explicit recovery card action, immediate lifecycle refresh, synthetic proof |
| Never resume old mouse/selector/uncertain external state | ApplicationStartup runs metadata recovery before shell; recovery neither restores coordinates nor drives an adapter; working leftovers are quarantined | ApplicationStartupTests, StartupRecoveryTests, FileWorkspace, production gate/starting-state tests | Preserve these boundaries through the new operator surface |

Already-complete behavior is reused. In particular, the original combined “inspection/import”
wording does not require a separate preview-before-import confirmation: validated import enters
the existing Session review with managed artifact facts/preview before approval. No extra importer
or confirmation screen is introduced. SCRUM-11092 is not reopened.

## Recovery entries and action authority

`ISessionService.ListRecoveryAsync` / `ResolveRecoveryAsync` and `SessionService.Recovery.cs`
provide the operator recovery authority. The design uses persisted session/step/attempt state, not an in-memory startup report or a
new recovery table. Discovery is independent of Recent Processing's age/count limits. Home uses a
compact recovery section; unresolved entries are excluded from Recent Processing to avoid two
cards offering inconsistent actions. Operator identity is output name, workflow, step, state and
last activity; the session ID is internal/stable automation identity, not primary display text.

The workflow service computes legal actions by probing existing engine authority. Active
interruption Restart uses Retry; handed-off Restart uses ReenterAutomation. Both restore Waiting
without creating an attempt, allocating a partial working copy or invoking an external app. Run
remains explicit, uses the normal production lock and creates its own approved-upstream copy.
Interrupted attempts are retained unchanged.

Manual result is restricted to **Enhancement and Background Removal**. Recovery uses the existing
HandOff/SubmitManualResult command sequence, existing owned Common File Dialog picker and existing
independent WIC validation. The user's selected file is not modified; the producing attempt records
ManualResultImport provenance and binds a managed Revision/hash that requires review. Trim, PSD
preparation, PDF preparation, Photoshop production TIFF and final review gain no arbitrary import.
Canceling selection issues no recovery command. Invalid imports remain unresolved and retain their
own failed import history as well as the original interrupted attempt. Existing rejection and
resubmission semantics remain authoritative. Discovery follows the latest attempt's retry chain:
only an interruption or failed manual imports leading directly back to it remain unresolved.
A final import metadata-commit failure leaves its Running manual attempt and Processing step
durable under the existing importer contract. That entry also remains visible, with no recovery
mutations while it might still be live, and bilingual guidance to restart PrintFlow if the import
failed. A subsequent startup reconciles the unfinished attempt through the existing recovery path.
A later ordinary retry failure or rejection of a successfully imported result cannot resurrect
the previously resolved interruption card merely because old Interrupted history still exists.

Abandon uses the established AbandonSession command and retention contract. It preserves the
source, InputSnapshot and historical rows. Successful recovery refreshes Home immediately;
failure reports a localized bounded message and stable failure code, retaining the unresolved
entry. Opening a session is navigation only.

## Startup locks and evidence

The new purpose-specific startup path checks process liveness for environment-verification
leases, which have no session aggregate. Dead owners may be released by a compare-and-swap on
purpose, token, process and machine. Alive or Unknown owners remain held; a replacement token is
never released. The session repository reads these leases' already-persisted round-trip timestamp
format while preserving the session-lock timestamp format. No session ID or attempt is invented
for a verification lease. No lock is acquired by these recovery metadata decisions.

Session lock recovery, interruption transactions, failed-attempt quarantine and reason sidecars
remain in the existing pipeline. Recovery actions do not delete quarantine evidence. Startup is
bounded to persisted recovery/retention work and does not execute a workflow or external app.
No signing, certificates, logging system, diagnostics export, background service or Settings work
was added; existing integrity requirements remain in place.

## Persistence and operator validation

`RecoverySurfaceTests` drives real persistence/services for unresolved reopen beyond the Recent
Processing age limit, ordinary-session exclusion, immutable interruption history, clean Restart
before Run, canceled/invalid/valid manual import, rejected successful manual result, unsupported
manual action, Abandon and source/InputSnapshot preservation. Exact-text WPF rendering pins en-US
or zh-CN explicitly and checks action names/AutomationIds with no binding errors. The focused
injected Restart commit refusal keeps the old state and card; a later ordinary retry failure does
not resurrect resolved interruption history. Final failure-path review is recorded below.

Live A/B/C passed as a **real WPF synthetic test window + UIA** using SQLite, FileWorkspace,
StartupRecoveryService, Home and SessionService. It is not an installed-shell fixed-workstation
production E2E claim. No Meitu or Photoshop processing was invoked. Actual per-phase runs are
retained at `artifacts/scrum-11112-recovery-live/final/`:

| Live proof | Actual action and direct verification | Raw evidence |
| --- | --- | --- |
| A — Restart | Real Tab route to Restart, Space activation, real Session screen with Run available but not invoked; persisted ACTIVE/Waiting, original interrupted attempt unchanged, no new attempt, no held lock; second startup remains resolved | `live-A.trx` (1/1), `live-transcript-A.txt`, `A-home.png`, `A-after.png` |
| B — Manual result | Real Tab reachability, UIA Invoke, actual owned Windows `#32770` common dialog, ValuePattern/InvokePattern file selection; real Session review; new MANUAL_RESULT_IMPORT attempt with `manual-result-import-v1`, managed file bytes and independently recomputed SHA-256 match; original manual source unchanged; startup preserves ReviewRequired/Revision/path/hash | `live-B.trx` (1/1), `live-transcript-B.txt`, `B-home.png`, `B-after.png` |
| C — Abandon | Real Tab route to Abandon and Space activation; entry disappears immediately, persisted ABANDONED, history/source/InputSnapshot unchanged, no held lock, startup keeps it resolved | `live-C.trx` (1/1), `live-transcript-C.txt`, `C-home.png`, `C-after.png` |

The native independent reviewer additionally opened **all three retained SQLite databases in
read-only mode**, checked session/step/attempt/lock rows, and independently matched source,
InputSnapshot and stored hashes. For B, the managed and selected manual file bytes/hash were
also checked independently of UI projections. Each transcript records its retained database and
workspace paths. The synthetic fixture's persisted activity clock is 19 August; the live proof
ran on 8 September.

Final Live B persisted Revision `01a07ed0-a554-7c4c-b093-9dbfdf93e9ec`, SHA-256
`D19E21026144AD63F22DF1C845BD3F9F17444A4628AA6E4A6F8E0ECD9C6E2B77`, under the new attempt's
managed Working folder. Its exact relative path is in `live-transcript-B.txt`.

The initial combined test-host run passed A but timed out discovering B's WPF UIA list after
creating a second STA window. That failed evidence remains in `recovery-surface.trx` and
`live-transcript.txt`. Running explicit A/B/C phases in separate test-host processes resolved the
test-harness UIA lifetime problem; no Product behavior was bypassed. The opt-in smoke is inert in
ordinary suite execution and the actual live passes above are reported separately.

Stable IDs follow the existing item-container convention: `Home.RecoveryList`, row
`Home.Recovery` with the operator job name, and scoped `Home.Recovery.Restart`, `.ManualResult`,
`.Open`, `.Abandon`. IDs contain no localized text. Job identity remains the persisted SessionId
inside the service/row. Native buttons support keyboard traversal, and Session review remains
reachable without a focus trap. The startup/empty-recent wording was made consistent with
already-unresolved cards on a second launch.

## Build, targeted tests and complete suite

Accepted starting baseline: **11,513 passed, 0 failed, 0 skipped; build 0 warnings, 0 errors**.
The default PATH resolved an older SDK; commands use the already-installed .NET 10.0.400 at
`C:\Users\admin\AppData\Local\Microsoft\dotnet\dotnet.exe`, with matching DOTNET_ROOT/PATH.
No SDK installation or repository SDK-policy change was needed.

- Initial lock filter: 19 passed, 4 failed. The new real verification-lease tests exposed the
  timestamp parsing defect above; Product parsing was corrected without weakening assertions.
- Corrected lock filter (`StartupRecoveryTests|ProductionLiveWorkstationVerifierTests`):
  **23 passed, 0 failed, 0 skipped**. TRX:
  `tests/PrintFlow.Tests/TestResults/scrum-11112-lock-targeted-final.trx`;
  log: `evidence/scrum-11112-lock-targeted-final.log`.
- First broad affected filter: **848 passed, 1 failed**. The unchanged startup single-callsite
  architecture assertion detected the unrelated new operator method name `RecoverAsync`.
  Renaming the operator service method `ResolveRecoveryAsync` keeps the two authorities distinct;
  no historical safety assertion was weakened. Next affected pass: **849 passed, 0 failed,
  0 skipped**, `artifacts/scrum-11112-recovery-live/targeted-pass.trx`.
- Independent review found the closing-manual-import-commit failure-path gap described above.
  The corrected focused filter (`RecoverySurfaceTests|ApplicationStartupTests|ManualResultImportTests`)
  passed **36/36**, zero failed/skipped, including the exact Home `FailFromCommit = 3` regression.
  Raw result: `artifacts/scrum-11112-recovery-live/review-fix-targeted.trx`.
- Final clean build: **0 warnings, 0 errors**, 8.10 seconds, after `dotnet clean PrintFlowStudio.sln`
  then `dotnet build PrintFlowStudio.sln --no-restore --verbosity minimal`. Logs:
  `artifacts/scrum-11112-recovery-live/final/clean.log` and `build.log`.
- One complete suite against frozen final source: **11,525 passed, 0 failed, 0 skipped**, logged
  test duration **4m39s**. TRX: `artifacts/scrum-11112-recovery-live/final/scrum-11112-full-suite-final.trx`;
  log: `artifacts/scrum-11112-recovery-live/final/full-suite.log`. Command:
  `dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --no-build --no-restore --logger "trx;LogFileName=scrum-11112-full-suite-final.trx" --results-directory artifacts/scrum-11112-recovery-live/final --verbosity minimal`.

The baseline increased by **12 cases**: four purpose/liveness/token tests, seven focused recovery
integration cases, and the opt-in WPF/UIA smoke entry. The smoke's actual A/B/C executions are
separate from its inert ordinary-suite invocation. Only one complete suite was run for this slice;
all implementation failures were handled with targeted checks first.

## Independent parent Epic reassessment

Original Work Item **11500**, **Implement Automation Safety, Environment Validation and Crash
Recovery**, maps to **SCRUM-11107**. Its exact Description was independently reread:

> Make PrintFlow desktop automation safe enough for a fixed production workstation by enforcing a single global automation lock, recognised starting states, workstation environment checks, clean retry semantics, crash/interruption recovery and controlled workspace cleanup. Automation must never guess clicks when Meitu, Photoshop, dialogs, unsaved documents, display configuration or required production presets do not match validated conditions; every retry begins from a fresh working copy of the latest approved upstream Revision, and startup never resumes from an old mouse position.

| Parent requirement | Current direct implementation/evidence | Assessment |
| --- | --- | --- |
| Single global automation lock | SQLite singleton acquisition/release, session attempt pipeline, token-owned live environment checks; StartupRecoveryTests and ProductionLiveWorkstationVerifierTests | Implemented; this slice closes token startup interaction |
| Recognised starting states; no guessed clicks or automatic unknown-document closure | Meitu/Photoshop classifiers, ProductionMeituProcessor/ProductionPhotoshopOutputProcessor starting-state refusal and reinspection, StateClassifier and ProductionAdapterGate tests | Existing FULL safety behavior retained |
| Workstation checks, display/preset drift, unsaved documents/dialogs | ProductionLiveWorkstationVerifier checks launch/safe state/four colour spaces/exact owned probe; VerifiedEnvironmentGate consumes the same verified result; SCRUM-11110 completion report includes actual 8 September live evidence | Existing FULL capability, no new workstation recertification claimed by this slice |
| Clean retry from latest approved upstream | WorkflowEngine.StartStep resolves UpstreamRevisionOf and emits fresh CreateWorkingCopy; Retry makes no attempt; RetryAndReviewTests and RecoveryAndBranchTests | Existing FULL behavior retained and recovery Restart reuses it |
| Crash/interruption recovery without old mouse position | StartupRecoveryService plus new persisted per-entry recovery authority and Home; this slice's targeted/live/persistence evidence | FULL; targeted/live/full-suite gates passed |
| Controlled workspace cleanup | SessionRetentionPlan preserves authority/unknown/evidence; SessionRetentionService copies/verifies, commits, rereads then deletes positively classified redundant files; SCRUM-11114 completion and retention suites | Existing FULL cleanup contract retained, including intentional conservative retention |

**SCRUM-11112: PARTIAL → FULL.** No material original-AC gap remains. The original restart,
inspection/import and abandonment choices are reachable per entry with engine/service authority,
durable history, bounded errors and source-safe recovery.

**SCRUM-11107: PARTIAL → FULL.** This assessment follows the exact parent clauses above, separately
checked by the no-history reviewer, rather than merely counting child labels. SCRUM-11108/11109/
11111/11113 retain existing FULL behavior; the latest SCRUM-11110 environment and SCRUM-11114
retention deltas are included alongside this recovery closure. Historical coverage rows remain
unchanged; a dated append-only delta records the current task and parent assessments. No external
Jira issue was changed or closed.

Final remaining gaps: **none within this authorized slice**. Evidence scope remains the permitted
synthetic WPF test window; no new installed-shell or fixed-workstation production certification is
claimed. Actual runtime model identity remains unverified as noted below.

## Routing, review and Git

Policy v2.1, PLAN_EXECUTE. Plan and checkpoint:
`docs/codex/SCRUM-11112/PLAN.md`, `docs/codex/SCRUM-11112/HANDOFF.md`.
Execution host: local Windows workstation, canonical checkout above. The native mixed UI sub-agent
was requested as `gpt-6-astra` / `high`, with no inherited conversation history. Actual runtime
model/effort is **UNVERIFIED**; the root likewise makes no unverified model-switch claim.
The independent acceptance audit used a separate native no-history Astra High request. It checked
the original CSV child and parent, current diff, raw TRX/screenshots and retained databases/files.
Its one material P2 finding was the disappearing card after failed final manual-import commit.
The reviewer inspected the fix and exact focused regression, and found it closed with no remaining
code findings. The reviewer also repeated read-only database/hash verification against the final
live recaptures and visually inspected the final Session/Abandon screens. The reviewer confirmed
the clean build and 11,525/0/0 complete-suite counters directly from their raw final artifacts.
No unresolved findings remain; the independent review completed with notes about the accurately
bounded synthetic evidence and unverified runtime model identity.

Initial HEAD: `8b904537c37c9179c03392f6f30c9a5d559bceba`; initial worktree clean. Product/tests
commit: **`7899769a6259a21d57a0d72fa32596fd3902b51b` — Complete per-session startup recovery actions**.
The report, routing checkpoint and append-only coverage delta follow in a separate local
documentation commit. Canonical `master` is retained, with all task changes committed and no
unrelated tracked changes. Local ignored raw evidence stays available at the paths above.
Nothing pushed, amended, rebased, branched, or moved into another checkout. No attribution trailer
or external Jira mutation was added.

**PASS WITH NOTES — SCRUM-11112 INTERRUPTED ATTEMPT AND STARTUP RECOVERY VERIFIED**
