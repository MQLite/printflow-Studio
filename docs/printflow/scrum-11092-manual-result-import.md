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

---

# SCRUM-11092-A closure — UI Automation accessibility and live proof

Everything above is the record of the first attempt and is unchanged, including its **BLOCKED** desktop verdict and its final verdict line. This section is the follow-up slice that removed the blocker. Baseline for it is accepted `4111bd0`, same repository, `master`, same signed preset and Production adapters.

## Root cause of the first blocker

The first attempt concluded that keyboard navigation "stopped advancing focus on Session". That is not what was wrong. Measured against the *unmodified* application from the first attempt, before any change in this slice, Tab left Home, entered the recovered Session screen and reached Submit Manual Result in seven presses, then cycled:

```text
Window -> Re-enter Automation -> List -> Zoom out -> Zoom in -> Reset zoom -> List
       -> Submit Manual Result -> Back to Home -> (repeats)
```

The Session focus graph was sound. Three things about the **automation client**, not the Product, produced the failure, and each fails silently rather than reporting an error:

1. **Screenshot capture and coordinate input were unavailable** (`SetIsBorderRequired ... 0x80004002`, "coordinate input geometry is unavailable"). A driver that observes focus and acts through pixels therefore had no way to see that Tab was working, or to press anything.
2. **A modal common file dialog does not appear under `AutomationElement.RootElement`** on this desktop. Submit Manual Result *did* open the real Windows dialog on the first attempt's own build — three of them were found still open, owned by the PrintFlow window, while UI Automation's tree enumeration reported the application as having exactly one top-level window. All three were closed through the dialog's own Cancel button by UI Automation, writing nothing; the seeded database was byte-identical afterwards.
3. **Standard Win32 dialog controls expose no UI Automation patterns** until the client-side providers are registered. Without `ClientSettings.RegisterClientSideProviderAssembly`, the filename field and the Open button are `Pane` elements supporting nothing; with it they are a `ComboBox` with `ValuePattern` and a `Button` with `InvokePattern`.

So the blocker was the way the application was being driven. The Product defects this slice did find are real, but different, and are listed next.

## Product accessibility defects found and fixed

| Defect | Evidence before | Change |
|---|---|---|
| No control on any screen carried an `AutomationId`, so a driver or assistive technology could only match localized text | every element reported `id=""` | 32 stable identities, listed below |
| The application window announced itself as a view model class | UIA name `PrintFlow.App.ViewModels.HomeViewModel` / `...SessionViewModel`, while the Win32 title was correctly `PrintFlow Studio` | `AutomationProperties.Name` bound to the shell title on `MainWindow`, plus `PrintFlow.MainWindow` |
| List rows announced view model classes | `PrintFlow.App.ViewModels.SessionStepRow`, `...RecentSessionRow`, `...ArtefactPreviewPane`, `...TrimModeChoice`, `...WhiteUnderbaseChoice` | each list names its generated container from the row's own localized text |
| Display-only lists were tab stops with nothing to operate | the step list and the preview panes both took Tab focus | `IsTabStop="False"` on the step, preview, output and size-preset lists |

The last two row-name leaks — `TrimModeChoice` and `WhiteUnderbaseChoice` — were found by the new automated test, not by inspection, after the first three had been fixed.

No workflow, domain or persistence code was touched. The change is 142 inserted and 10 deleted lines across `MainWindow.xaml`, `HomeView.xaml` and `SessionScreenView.xaml`. `IFilePicker` / `OpenFileDialogPicker` already existed from the first slice, so the optional picker abstraction was not needed.

### Automation identities

```text
PrintFlow.MainWindow     Screen.Home              Screen.Session
Home.ShowEnvironment     Home.ChooseFile          Home.Refresh
Home.RecentSessionList   Home.RecentSession       Home.ResumeSession       Home.AbandonSession
Session.ConfirmOriginal  Session.RunStep          Session.Approve          Session.Reject
Session.Retry            Session.Skip             Session.SubmitManualResult
Session.HandOff          Session.Complete         Session.AddAnotherSize   Session.BackToHome
Session.Stop             Session.TakeOver         Session.TakeOverConfirm  Session.TakeOverCancel
Session.ReenterAutomation                         Session.BeginManualCrop
Session.StepList         Session.PreviewPanes     Session.OutputList       Session.SizePresetList
Session.TrimModes        Session.WhiteUnderbaseChoices
```

Every identity is an invariant string in the XAML; none is derived from localized text. Accessible names are bound to the same localized label the operator reads, so identity and wording move independently. The two Home row actions repeat once per session and are unique *within* a row: a driver selects the row by its accessible name — the operator's own output name — and then the action inside it.

## Automated accessibility tests

`tests/PrintFlow.Tests/Integration/Ui/SessionAccessibilityTests.cs`, nine rendered tests. They read `AutomationPeer`s built from elements that a real WPF measure and arrange pass produced, so a control present in XAML but never realised fails them; none of them searches XAML source text.

- Every required identity reaches a rendered button, across five session states: waiting, computing, failed, in review, handed off.
- No two simultaneously visible actions share an identity, in three states.
- The identity set is unchanged between en-US and zh-CN while the wording changes.
- `Session.SubmitManualResult` announces `Submit Manual Result` / `提交手动处理结果`.
- No rendered element announces a type, class or resource name.
- `Session.SubmitManualResult` is present, enabled and offers `InvokePattern` when eligible, and is **not rendered at all** when the session is still automated.
- Invoking the rendered button through `IInvokeProvider` — never through `SubmitManualResultCommand` — runs the real command, calls the picker seam once, and persists a `ManualResultImport` attempt, Revision and `ReviewRequired`.
- The tab stops include Submit Manual Result before Back to Home, and exclude the display-only lists.

The tab-order test states its own limit honestly. A tree that was measured and arranged but never attached to a window has no focus scope, so `PredictFocus` and `MoveFocus` give up after one element. What the test asserts instead is the two things the route is made of — which elements are tab stops, and in what tree order — together with the absence of any `TabIndex` and of any `Cycle` or `Contained` keyboard-navigation mode that could reorder or confine traversal. The traversal itself is proven live, below.

## The UI Automation driver

`tests/PrintFlow.Tests/Fixtures/DesktopAutomation.cs` locates controls by `AutomationId`, control type and UIA pattern. It contains no pixel read, no coordinate and no fixed position.

The file dialog is identified by **ownership**, which is what makes it safe as well as findable. Three facts must agree before a path is typed: the window belongs to the application's own process, it is owned by the application's own main window, and it was not present in the set of owned dialogs captured immediately before the action. It must then expose the documented common-dialog control ids — the filename field and the confirm button — as direct children. If any of that fails, the driver throws and touches nothing. Those control ids, rather than captions, are what let the same code drive the accepted zh-CN workstation, where the confirm button reads `打开(O)`.

The path is set with `ValuePattern.SetValue`, read back, and the dialog confirmed with `InvokePattern.Invoke`. No keystroke is guessed.

The keyboard walk refuses a route it cannot attribute: it waits for focus to be inside the application before starting, and fails if focus leaves mid-walk. This mattered — the first driven run recorded thirty stops belonging to another process, which said nothing about the Session screen. That run is not the record below.

## Live proof

Evidence directory: `D:\PrintFlowStudio\Evidence\SCRUM-11092A-live-uia-20260904`. The retained `PF_MANUAL_LIVE_A` / `PF_MANUAL_LIVE_B` sessions from the first attempt were audited first and were still valid — byte-identical database, loaded by the rebuilt application — and were driven successfully through the whole flow. That run is superseded here only because its keyboard-route record was not attributable; the sessions were reseeded with the committed seed harness so the authoritative record is clean. Synthetic files only; no customer artwork.

A copy of the application published from this slice's source runs with Production adapters and the accepted preset; only its database location is redirected. Repository configuration is unchanged.

Driven by `Drive_both_retained_sessions_through_the_real_session_screen`, opt-in through `PRINTFLOW_MANUAL_RESULT_DRIVE`. Transcript: `live-drive-transcript.txt`. Accessibility tree: `accessibility-tree.txt`.

**Live A — reached by keyboard.** Tab entered the Session screen and reached Submit Manual Result in six presses, every stop inside the application:

```text
PrintFlow.MainWindow -> Session.ReenterAutomation -> <Button: 缩小> -> <Button: 放大>
                     -> <Button: 重置缩放> -> Session.SubmitManualResult
```

Focus rested on `提交手动处理结果`, id `Session.SubmitManualResult`. **Space** opened the application's own file dialog. `PF_MANUAL_LIVE_A-manual.png` was chosen through it by `ValuePattern`, confirmed by `InvokePattern`, and the session moved to review. Then, through the real UI: Approve, Skip the next automated step, Run Step. The step that followed produced a different artefact (`F686CCC1A0D4`) from the manual result (`2B3067612ECE`).

**Live B — reached by UI Automation.** `Session.SubmitManualResult` was found by identity: enabled, named `提交手动处理结果`, `InvokePattern` available. `InvokePattern.Invoke()` opened the real dialog, and `PF_MANUAL_LIVE_B-manual.png` was chosen the same way. It was left at `ReviewRequired` and **not approved**. The application was closed normally, restarted, and the session reopened: the same SHA-256 prefix `2B3067612ECE` on screen.

No screenshot was taken and no coordinate was used. Every action above is an operation on a named control.

## Independent verification

`Verify_desktop_results_after_restart_and_authoritative_downstream_input`, unmodified, run afterwards in its own process against the evidence database with `PRINTFLOW_MANUAL_RESULT_VERIFY`: **passed**.

```text
PF_MANUAL_LIVE_A  session  01a06add-82fd-75a7-adec-ecc6a81bb48d
                  attempt  01a06add-fb8b-74f8-8ee7-6696d2b60bfa
                  revision 01a06add-fcad-77cb-bee1-38956484725c
                  sha256   2B3067612ECE142CA48E2A98708E97685D1F10F30DA89A83188AC691B0D66A1D
                  path     Sessions/S_20260904T052137Z_a81bb48d/Working/01a06add-.../PF_MANUAL_LIVE_A-manual.png
PF_MANUAL_LIVE_B  session  01a06add-84f8-76ac-8fed-15610e71585b
                  attempt  01a06ade-0ae2-7fae-8502-0daaf30a0035
                  revision 01a06ade-0bc8-7436-9514-108af0d791d2
                  sha256   2B3067612ECE142CA48E2A98708E97685D1F10F30DA89A83188AC691B0D66A1D
                  state    ReviewRequired after restart, same revision id, hash and managed path
```

What it asserted, from persistence rather than from the screen: the manual Revision carries `ManualResultImport` and its attempt ended; the preceding Enhance attempt is still `Failed`; the managed bytes equal the external synthetic file, and the root Revision still equals the synthetic source; for A the manual Revision is approved, `UpstreamRevisionOf(BackgroundRemoval)` resolves to it, and a **Trim attempt exists whose `InputRevisionId` is that manual Revision**; for B the reloaded session is `ReviewRequired` on the same Revision id and hash; and the automation lock is not held.

## Build and tests

- Clean solution build: **0 warnings / 0 errors**, pinned .NET SDK 10.0.400.
- Manual-result, migration, localisation and accessibility filter: **428 passed / 0 failed / 0 skipped** — the accepted 418, plus 9 new accessibility tests and the opt-in drive smoke.
- Broader focused run, adding stop/take-over, session controls and view rendering: **532 passed / 0 failed / 0 skipped**.
- Complete suite against final source: **10,866 total / 10,863 passed / 3 failed / 0 skipped**, 3m16s. Evidence: `tests/PrintFlow.Tests/TestResults/scrum-11092a-accessibility-full-suite.trx`.

The three failures are **pre-existing and not caused by this slice**. They are `ReturnAndTrimControlsUiTests.A_tight_trim_states_the_same_rectangle_twice`, `.The_review_states_the_detected_and_applied_bounds_in_words` and `.The_bounds_follow_the_result_on_screen_across_a_retry`, which assert English wording — `"Size 5 × 5 px"`, `"Detected graphic bounds"` — without pinning a culture. This workstation's Windows UI language override is `zh-CN`, so the .NET test host resolves `CurrentUICulture` to zh-CN and they read `"尺寸 5 × 5 像素"` and `"检测到的图形范围"`. The same three failures were **reproduced on the accepted baseline with this slice's changes stashed**, which is how they were attributed. The accepted 10,856 / 0 baseline was therefore recorded under a different display language. They are not weakened, skipped or edited here: pinning their culture the way their neighbours in `MaximumBoundsRenderingTests` do would fix them, but that is an unrelated accepted test file and is left for a separate decision.

One further failure, `ProductionMeituExportTests.A_successful_run_reports_the_source_and_output_facts_it_validated`, appeared in one full-suite run with `OutputUnreadable: the file's length was still changing`, and did not reproduce in isolation or in the following full run. It is a file-settle timing flake under full-suite load, of the same kind the first attempt recorded, and is not an assertion about this slice.

Dependencies and the signed preset are unchanged. The desktop sandbox setting `[windows] sandbox_private_desktop = false` was not touched, and nothing in this slice depends on it.

## Jira coverage delta

**SCRUM-11092: PARTIAL → FULL.** The one thing the first attempt was waiting on — live synthetic re-entry through the real Product UI — is now proven and independently verified. Stop and Take Over are unchanged and remain safe; a manual result is imported through the real Session screen and the real Windows picker, both by keyboard and by UI Automation; it is explicitly reviewed; the following step demonstrably consumed the approved manual Revision as its input; automation never resumed mid-sequence, and re-entry stayed explicit. The AC's force-termination sentence remains answered by the accepted Epic 11300 Part D2B decision that PrintFlow never force-terminates Meitu — an adjudicated policy from before this slice, carried forward unchanged and not re-argued here.

**SCRUM-11112: PARTIAL remains PARTIAL.** This slice widened nothing: manual result import is still Enhancement and Background Removal only, and general interrupted-attempt recovery across every adapter operation is still not certified. Accessibility work does not touch eligibility, and the eligibility tests assert that it did not.

## Git state for this slice

Repository `D:\Repositories\printflow-Studio`, branch `master`, started from accepted `4111bd0`. No worktree, no task branch, no alternate clone, no amend, rebase or push. Unrelated untracked files were left untouched and excluded from the commit.

**PASS WITH NOTES — MANUAL PROCESSING RESULT RE-ENTRY VERIFIED**

---

## Culture-sensitive test determinism follow-up — 2026-09-04

The accepted live verdict remains **PASS WITH NOTES — MANUAL PROCESSING RESULT RE-ENTRY VERIFIED**. **SCRUM-11092: PARTIAL → FULL; SCRUM-11112 remains PARTIAL.** This test-only follow-up preserves the original desktop-blocked evidence and the subsequent live proof above; it changes neither Product behaviour nor the coverage boundary.

The earlier **10,856 passed / 0 failed** suite was produced under a different UI-language environment. The later **10,866 total / 10,863 passed / 3 failed** run inherited this workstation's `zh-CN` UI culture. As recorded above, the same three failures were reproduced against the accepted pre-SCRUM-11092 baseline with the SCRUM-11092 changes stashed; they are pre-existing culture-sensitive test defects, not a SCRUM-11092 Product regression. That historical baseline attribution is retained, not re-run by stashing accepted work in this follow-up.

Fresh red evidence on accepted `1627a29`, before this correction: **3 failed / 0 passed / 0 skipped**. All three cases in `ReturnAndTrimControlsUiTests` assert English (`en-US`):

- `The_review_states_the_detected_and_applied_bounds_in_words`: expected `Detected graphic bounds`, actual `检测到的图形范围`.
- `A_tight_trim_states_the_same_rectangle_twice`: expected `Size 5 × 5 px`, actual `尺寸 5 × 5 像素`.
- `The_bounds_follow_the_result_on_screen_across_a_retry`: expected `Size 5 × 5 px`, actual `尺寸 5 × 5 像素`.

Each now saves `CurrentUICulture` and `CurrentCulture`, sets both to `en-US` before constructing the harness or resolving resources, and restores both in `finally`, exactly following `MaximumBoundsRenderingTests`. All original exact wording assertions remain; no tests were added, skipped or weakened, and no new culture helper was introduced. Existing Chinese rendering tests explicitly select `zh-CN` and remain unchanged.

Parallelism audit: both classes already belong to `SqliteCollection`, which serializes its tests while other collections may run in parallel. The changed cultures are scoped to the current execution context and flow across awaits; this correction never assigns process-wide `DefaultThreadCurrentCulture` / `DefaultThreadCurrentUICulture` or a shared resource-culture override. The `finally` also covers assertion and setup failures. No runner-wide serialization or runtime culture change is needed.

Verification uses the already-installed SDK **10.0.400** at `C:\Users\admin\AppData\Local\Microsoft\dotnet\dotnet.exe`; plain `dotnet` initially found only the system SDK 8.0.418. No SDK/configuration change was made. The targeted command performed the necessary test-project build; subsequent runs use those binaries with `--no-build`.

- Targeted red/green: **3 failed → 3 passed / 0 failed / 0 skipped**.
- Related filter (`ReturnAndTrimControlsUiTests`, `MaximumBoundsRenderingTests`, `ViewRenderingTests`, `LocalisationResourceTests`, `SessionAccessibilityTests`): **131 passed / 0 failed / 0 skipped**, including Trim rendering and Chinese resources.
- Final full suite: **10,866 passed / 0 failed / 0 skipped**, 3m10s. This is the actual post-correction result on the current workstation, not the earlier language-dependent baseline.

Local TRX evidence is retained under `tests/PrintFlow.Tests/TestResults/culture-determinism/`: `culture-red.trx`, `culture-green.trx`, `culture-related.trx`, and `culture-full-suite.trx`. These remain ignored test artifacts. Reproduce the three-case run with `dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --filter "FullyQualifiedName~A_tight_trim_states_the_same_rectangle_twice|FullyQualifiedName~The_review_states_the_detected_and_applied_bounds_in_words|FullyQualifiedName~The_bounds_follow_the_result_on_screen_across_a_retry"`; the full-suite command is `dotnet test PrintFlowStudio.sln --no-build` (using the SDK executable above).

Meitu settle observation: **observed once; not reproducible; no change made**. The previously recorded `ProductionMeituExportTests.A_successful_run_reports_the_source_and_output_facts_it_validated` transient did **not** reproduce in this final full suite. No Product/test timing, sleeps or assertions were changed for it.

Desktop acceptance/test-infrastructure convention, not Product runtime code:

- WPF controls: use `AutomationId` / UIA patterns.
- Owned modal dialogs: do not rely solely on `AutomationElement.RootElement`; discover by Win32 ownership and attach with `AutomationElement.FromHandle`.
- Standard Win32 dialog controls: register the client-side UI Automation provider assembly before expecting `ValuePattern` / `InvokePattern`.

Scope: only the three affected tests and this report. Product source, resource files, runtime localisation, XAML, ViewModels, workflow, persistence, adapters, configuration and presets are unchanged. Work stays in `D:\Repositories\printflow-Studio` on `master`, for a new local test/report commit; no branch, worktree, amend, rebase, push or AI-attribution trailer.

### Independent re-verification — 2026-09-07

The correction above was re-verified from a clean tree on accepted `7dba4a0`, on the same workstation, without changing any source. The workstation still resolves the culture that produced the original failures: a minimal .NET 10 probe reports `CurrentUICulture=zh-CN`, `CurrentCulture=zh-CN`, `InstalledUICulture=zh-CN`. The culture pin is therefore still load-bearing rather than a no-op — without it the three cases would still resolve Chinese resources on this machine.

A caveat worth recording for anyone repeating this check: Windows PowerShell 5.1 reports `CurrentUICulture=en-US` in the same shell, because PowerShell falls back to `en-US` when it has no localized resources for the display language. That reading is a PowerShell artifact and must not be used to judge the culture the test host will resolve; probe the .NET runtime directly.

Re-run results, all reproduced exactly:

- Targeted three cases: **3 passed / 0 failed / 0 skipped**.
- Related Trim UI / localisation filter: **131 passed / 0 failed / 0 skipped**.
- Complete suite (`dotnet test PrintFlowStudio.sln`): **10,866 passed / 0 failed / 0 skipped**, 3m42s.

Meitu settle observation on re-run: **did not reproduce** — the full suite was clean, so no Product or test timing change was made for it. No source, resource, configuration or preset file was modified during this re-verification; the only repository change is this note.

**PASS — SCRUM-11092 LIVE CLOSURE AND TEST BASELINE RECONCILED**
