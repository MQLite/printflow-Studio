# SCRUM-11092 — Manual processing result import

Related recovery scope: **SCRUM-11112**. No new Epic. Baseline: `bd0dbaf`, Production adapters, signed `printflow-workstation-v1 1.16.0`. Accepted PSD, PDF and trim contracts remain in force.

## Original acceptance authority

Read before product changes from `C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv`. The CSV uses legacy work item IDs: 11307 maps by exact title to SCRUM-11092; 11505 maps to SCRUM-11112. These rows have their requirements in Description, without a separate Acceptance column.

**SCRUM-11092 — Implement Meitu Stop and Manual Processing Handoff** (verbatim):

> Implement safe operator interruption and manual takeover. While Meitu is computing, Stop cancels subsequent automatic steps after the current computation where possible; the completed result may then be imported or discarded. Force termination is a last resort behind an explicit warning. Manual Processing ends the automated flow and never resumes in the middle of a prior mouse or UI-automation sequence.

**SCRUM-11112 — Implement Interrupted Attempt and Startup Recovery** (verbatim):

> At startup, detect unfinished ProcessingAttempts and stale automation-lock state and mark such Attempts INTERRUPTED. Offer restart of the step from a fresh working copy, inspection/import of a manually saved result or abandonment of the Attempt. Never resume screen automation from the previous mouse position, selector sequence or uncertain external-application state.

## Missing return path and supported operations

Previously Stop / Take Over / Hand Off could persist manual ownership, but there was no command to bring an externally completed result into the session. Re-enter Automation restarted automation; it did not import evidence. The existing `ManualImport` operation identifies an in-product manual crop and remains unchanged.

The closed list is **Enhancement and Background Removal**, in Prepare Asset and Prepare Customer Design. These are the Meitu operations covered by SCRUM-11092 and defined as reviewed producing steps in WorkflowCatalog. Import, original/PSD/PDF preparation, Trim, PNG promotion, sizing and Photoshop Production TIFF replacement are excluded. SCRUM-11112's general recovery wording does not define a validated arbitrary Production TIFF replacement contract; this slice does not invent one.

`CanSubmitManualResult` is derived from the engine's accepted command: session HandedOff, current supported step Failed / Interrupted / RetryRequired, and an approved upstream result exists. Ordinary active failure, Waiting, Processing, ReviewRequired, Approved, skipped steps, abandoned sessions and unrelated steps cannot submit. The service repeats the eligibility guard before integrity/file work. The 288-case state/step/session test matrix checks both command availability and actual acceptance.

If a late takeover or explicit Hand Off retained an already-validated ReviewRequired offer, Submit stays unavailable. An explicit hash-bound Reject can discard that offer while retaining manual ownership, after which submission becomes legal. It neither erases the old Revision nor resumes automation.

## File contracts, copying and validation

Enhancement accepts single-frame PNG or JPEG (`.png`, `.jpg`, `.jpeg`), with matching content signatures, positive decodable pixel dimensions and a complete pixel decode. Its dimensions and bytes need not equal the approved input.

Background Removal accepts single-frame PNG only, must retain the approved upstream canvas dimensions, and must contain actual decoded alpha values below 255 **and** visible foreground above 0. This preserves the substantive MeituCutoutOutputRule / MeituTransparencyRule contract: opaque RGBA, fully transparent output and changed canvas size are refused. Filename and channel metadata never establish transparency.

WicManualResultImporter opens selected evidence read-only with FileShare.Read, excluding writers and rename/delete throughout snapshot acquisition. It checks a 256 MiB encoded size bound, hashes the external input, and copies with CreateNew to `current-session/Working/new-attempt-id/selected-filename`. Existing typed workspace refs and path resolution guard the destination; redirected destination ancestors and invalid filename characters are refused. Paths do not depend on filename uniqueness.

The managed copy is independently reopened, inspected and hashed, fully decoded row by row within a 100,000,000-pixel limit, and rehashed while held against writers. The selected and managed hashes and byte lengths must agree. A successful managed file becomes read-only. No external path is stored as Revision storage, and the external file is never moved, renamed, deleted or edited by Product. Selecting another session's Working file still creates a distinct current-session copy.

Source and upstream Revision integrity are checked before submission. The external file is evidence of the operator's choice; semantic association is established by the command's session/step and approved upstream Revision, with the before/after review allowing the operator to catch a wrong image.

## Provenance, attempts and persistence

`OperationKind.ManualResultImport` is persisted on both attempt and Revision as `MANUAL_RESULT_IMPORT`. Manual crop keeps `MANUAL_IMPORT`; Meitu operations keep ENHANCE / REMOVE_BACKGROUND. Adapter identity is `manual-result-import-v1`. This closed identity, rather than notes, establishes manual provenance.

Each submission commits a new Running attempt before file work. The closing transaction atomically writes Succeeded, its output Revision and ReviewRequired. Failure records a terminal Failed attempt with no output Revision and preserves manual-processing eligibility through the engine's HandOff transition. A closing storage failure creates no successful Revision; startup recovery can interrupt the remaining Running attempt.

AttemptCount / RetrySequence count producing attempts at the same step; RetryOfAttemptId links the preceding same-step attempt. They do not claim automation succeeded: Operation and AdapterId distinguish the manual attempt. The preceding automated attempt is not updated. InputRevisionId and Revision.SourceRevisionId bind the approved upstream; the output id binds the managed file/facts. Safe selected filename, copied hash and operator identity are retained in attempt audit notes; the opening record also retains the filename/operator for failed submissions. The existing terminal-clock observation runs after file work, proven for both success and failure with 17 seconds of elapsed time.

Forward migration **0012_manual_result_import.sql** widens Revision's operation check and the attempt's BackgroundRemovalDecision check using transactional rebuilds. All columns, historical values, indexes, foreign keys and existing triggers are retained. No historical script is edited or provenance backfilled. An upgrade test populates the actual v11 schema, compares every historical Revision/attempt field before and after, checks foreign keys and tests the immutable Revision trigger. The session's automatic-selection decision vocabulary is not widened.

A manual cutout attempt records `ManualResultForReviewedContent`, bound to the approved upstream id/hash. It does not inherit automatic Meitu selection authority. The review readback says manual processing in both languages.

## Review, rejection and downstream use

Successful import returns to Active / ReviewRequired, never Approved or Completed. The existing review surface shows the approved upstream and imported result, filename, format, pixel dimensions and “Manual processing result” / “手动处理结果”. Approve/Reject retain their existing hash guards and persisted ReviewDecision records.

Reject keeps the file, attempt and Revision as history, removes the current offer and enters RetryRequired. The operator can choose Hand Off again and submit another result: new attempt, Revision and managed path. Re-enter Automation remains available before submission; it cannot overwrite a ReviewRequired manual result. Return upstream invalidates descendants normally without altering retained manual identity or bytes.

After approval, UpstreamRevisionOf resolves to the manual Revision. Regression tests execute the following Background Removal or Trim step and verify its actual attempt InputRevisionId. Downstream sizing and Photoshop continue through the existing upstream-resolution mechanism. The remaining desktop proof must run Trim from the approved manual Enhancement result after explicitly skipping Background Removal.

## Restart, recovery and lock boundaries

Persisted HandedOff state survives reopening and keeps Submit available. Persisted ReviewRequired state survives reopening with the same Revision, hash and path; Load does not copy or validate again just to reconstruct the view. Removing the external evidence after successful import does not affect review.

After interrupted Meitu recovery, restart-step, Hand Off/manual submission and abandonment are separate reachable choices. Recovery itself never resumes automation or automatically adopts a saved file. An interrupted manual import also recovers to Interrupted and can be explicitly handed off again.

Manual file selection, copying and validation use no external application or environment gate. The shared runner treats this operation as local work and neither acquires nor releases the global automation lock. Tests import while another session owns the lock, including copying that session's managed file, and verify the lock owner remains unchanged.

## Session UI and localization

The existing Session screen gains Submit Manual Result with a standard single-file picker and operation-specific extension filters. Cancel returns before any command, history or file write. Normal busy-state controls prevent repeated UI submission. English and zh-CN strings cover the command, provenance, invalid file/import failure, transparency and canvas failures. Operator messages use resource text, never raw failure enums, keys or machine paths. Localisation parity runs with the targeted and full suites.

## Verification

- Final targeted suite: **418 passed / 0 failed / 0 skipped** (manual result, migration and localization filters). The earlier broader focused run included stop/takeover coverage: **468 passed / 0 failed / 0 skipped**.
- Clean solution build: **0 warnings / 0 errors**, pinned .NET SDK **10.0.400**.
- Complete suite against final source: **10,856 passed / 0 failed / 0 skipped**, 3m11s. Evidence: `tests/PrintFlow.Tests/TestResults/manual-result-final-full-suite.trx`. The first full run exposed a missing command constructor in the exhaustive test fixture (272 cases), two older handoff assertions, and one transient malformed-PSD boundary failure. The fixture and handoff assertions were updated to exercise the new command and explicit reject-before-replacement behavior; none were removed or skipped. The accepted PSD implementation and its test were unchanged and passed in the final full suite.
- Dependencies and signed preset: unchanged.
- New tests cover eligibility, real managed decoding/copying, both supported operations, malformed/missing/locked/wrong-format/opaque/empty-alpha/wrong-canvas files, size limits, collision, picker cancel, rejection/resubmission, persistence fault, restart, interrupted recovery, re-entry, downstream input, upstream return and terminal timing.

## Controlled desktop proof

**Incomplete; blocked at desktop interaction.** After the green build and suite, the opt-in synthetic seed test passed. It uses a scripted automation timeout to establish real Product attempt/HandOff state in an isolated evidence database. A copy of the built desktop application was launched with Production adapters and the accepted preset; only the copied application's database location was redirected to the evidence database. Repository configuration is unchanged. No customer artwork was used or manipulated.

Evidence directory: `D:\PrintFlowStudio\Evidence\SCRUM-11092-manual-result-20260904`.

- Live A: `PF_MANUAL_LIVE_A`, session `01a06a6c-765a-73d8-aa06-8b1036960aef`.
- Live B: `PF_MANUAL_LIVE_B`, session `01a06a6c-785d-7c85-b1c4-b4d9d1231960`.
- Both have synthetic source and separately generated manual PNG files, an earlier Failed Enhancement attempt and persisted HandedOff state. `expectation.json` records the database/session identities. Seed test evidence is `tests/PrintFlow.Tests/TestResults/manual-result-live-seed.trx`.
- The desktop process was closed and restarted. Home listed both sessions as handed off. Keyboard navigation opened Live B; its recovered Session accessibility tree showed Enhancement Failed, the handoff explanation, Re-enter Automation and Submit Manual Result. Saved as `recovered-session-accessibility.txt` in the evidence directory.
- Screenshot capture failed with `SetIsBorderRequired failed: 不支持此接口 (0x80004002)`. Indexed clicks failed with `coordinate input geometry is unavailable`. Keyboard navigation worked on Home but stopped advancing focus on Session, including after activation and refreshed observations. No manual-result picker, import, approval or downstream run was completed through the desktop.
- At the user's request, the desktop sandbox setting was checked: `C:\Users\admin\.codex\config.toml` already had `[windows] sandbox_private_desktop = false`. No configuration change was necessary; the capture/input failures persisted with that stored value.

The automated real-file, service, ViewModel and persistence tests verify the implemented lifecycle, but they do not substitute for the required live proof. The desktop application and isolated synthetic evidence are retained for completion. Remaining steps: submit each matching `PF_MANUAL_LIVE_*-manual.png`; for A explicitly approve, skip Background Removal and run Trim; leave B in ReviewRequired; close/restart the desktop and verify the persisted review. Run the opt-in `Verify_desktop_results_after_restart_and_authoritative_downstream_input` test with `PRINTFLOW_MANUAL_RESULT_VERIFY` set to the absolute `expectation.json` path only after those desktop actions. That verifier has not been claimed as passed.

## Jira coverage delta

**SCRUM-11092: PARTIAL remains PARTIAL pending the mandatory live proof.** The missing manual-result command, validation, provenance, explicit review and recovery re-entry are implemented and covered by automated tests. Existing Stop/Take Over semantics remain covered; no force termination or mid-sequence resume was introduced. The definition of done expressly requires live synthetic re-entry before changing coverage, so the coverage matrix is unchanged.

**SCRUM-11112: PARTIAL remains PARTIAL.** Automated recovery tests establish distinct restart, manual import and abandonment choices for interrupted Meitu operations. General interrupted-attempt recovery extends beyond the two supported manual-result operations; this slice does not certify arbitrary saved-result import for every adapter operation. Its original AC is evaluated independently, with no blanket FULL claim.

## Git state

Started from accepted `bd0dbaf`. The original checkout contained an unrelated untracked `docs/ai-follow-smoke.md`, preserved untouched. Implementation uses a clean worktree at `D:\Repositories\printflow-manual-result-import`, branch `codex/manual-result-import`. Product and tests are committed as `65ee297` (`feat: add validated manual result re-entry for SCRUM-11092`). This report is recorded in a subsequent local documentation commit. New local commits only; no amend, rebase, push or attribution trailer.

**BLOCKED — MANUAL PROCESSING RESULT RE-ENTRY NOT VERIFIED**
