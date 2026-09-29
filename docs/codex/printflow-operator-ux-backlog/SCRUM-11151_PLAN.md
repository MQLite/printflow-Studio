# SCRUM-11151 — failure guidance: implementation checklist and source-to-copy matrix

Task `PF-OPUX-v1-SCRUM-11151-impl-v1`, Planning-ID `PF-OPUX-v1-failure-next-step-copy`, 2026-09-29 NZ. Executes `printflow-remediation-prompts/PrintFlowStudio_SCRUM11151_Failure_Guidance_Implementation_Prompt_v1.md` (operator-owned, untracked). The owner approved its bounded interaction brief and authorised implementation, verification, independent review, in-scope fixes, direct `master` publication and SCRUM-11151 Jira updates only. The final bilingual wording still needs the owner's copy review: [SCRUM-11151_COPY_REVIEW.md](SCRUM-11151_COPY_REVIEW.md).

## Baseline

- `master` = `origin/master` = `91a21ea39c93c227a1ad980150dafc21b067e4c5` (fetch and `ls-remote` agree). The only local changes were the SCRUM-11149 and SCRUM-11150 audit residues, which stay unstaged.
- Jira pre-read: SCRUM-11151 (id 10881) To Do, 0 comments, parent SCRUM-11139 (In Progress), blocks 11154/11155, no inward Blocks. Transitions discovered live: To Do 11, In Progress 21, In Review 31, Done 41. Moved To Do → In Progress (21) before implementation; read back In Progress.
- Hooks/CI: no `.github/`, no active git hooks. A `master` push triggers no deployment or migration.

## Route

Claude adaptation v1.2 of policy v2.4, `route_offset: 0`. Opus/High for all work; no Sonnet unit. Host model `claude-opus-5-5`, effort UNVERIFIED. Context `CONTINUE`; one fresh read-only `personal-dev-reviewer` (Opus) at the review boundary.

## Checklist

1. Establish per-key facts from the source (matrix below) before writing copy.
2. Shared `Failure_*` sentences: what happened, plus only facts true on every producing path. No unconditional Run/Retry instruction: the surface supplies the available workflow next step; checking an external application is retained where justified.
3. Wrappers: plain sentence, then a context-true next step or the help path. The code leaves the sentence.
4. Error details for the failure of *this* action on every touched surface: an in-place "Error details / 错误详情" disclosure under the notice with the exact code.
5. Tests: resources/parity, wrapper formatting, code identity, retry-legal vs retry-illegal context, no-session surfaces, no mutation on language change/details/refresh, off-screen layout in both languages.
6. Review, publish, Jira, readback, CSV, audit, HANDOFF.

## Consumers of the shared `Failure_*` keys

Each of the ten `Failure_*` keys is the default `MessageKey` of every `OperationFailure.Create(code, …)` without an explicit key. About 440 producer references share them. Where they reach the screen:

| Consumer | How the key is chosen | Code reachable in |
|---|---|---|
| Session status line `StatusReason` (stored failed step) | `DisplayNames.Failure(FailureCode)`: **by code**. So a specialised key (`Failure_OperationFaulted` on AdapterUnavailable, `Failure_MeituInterrupted` on Cancelled) still shows the generic text here | Existing Error Details screen (button `Session.ErrorDetails`). It opens `CurrentFailureAttemptId`, the same newest attempt of the Failed step that `CurrentStepFailure` reads |
| Session notice `Session_ActionFailed` (any refused or failed command, Stop/Take Over request, enlargement authority, correction file repair) | `DisplayNames.FailureNotice(OperationFailure)`: persisted/explicit `MessageKey`, with notice-only variants for embedded Run/Retry advice | New in-place Error details under the notice. A refused command has no attempt, so the attempt-bound screen cannot show it |
| Colleague-correction wrappers `Session_CorrectionPrepareFailed` / `Session_CorrectionRefused` (not listed) | `MessageKey` | Their DB-verified wrappers/guarantees are unchanged. Preparation/import failures now have an adjacent in-place Error details disclosure bound to CorrectionErrorCode; the nested sentence uses the notice-only formatter |
| Error Details screen: `Description` of the opened attempt | stored `MessageKey` (historic attempts too) | Its existing Code row |
| Error Details screen: `Notice` (a recovery, back or refresh call failed) | `MessageKey`, through the notice-only formatter | New in-place Error details under that notice. The Code row shows the *attempt's* code, not this one |

Not consumers: Home, Workflow Selection and Settings wrappers (they format only the code, not the sentence); the preview pane (own strings); Production Readiness (check-specific keys).

## Source-to-copy matrix — shared failure sentences

Universal facts used:
- Design invariant §18.1: "PrintFlow never overwrites or deletes a user's source file" (also §3.2 and §5.2). It holds on every path, so every sentence ends with it.
- §18.4: "A failed Attempt never creates a usable Revision."
- §18.11: "Automation never guesses at clicks when the environment is unrecognised."

Approved results are never claimed unchanged in a shared sentence. On the same codes, a final TIFF can be promoted into `Approved\` or recycled before a commit that can still fail, and upstream changes can invalidate approvals.

| Key | Producer families and side-effect boundary | Provably unchanged / may have changed / unknown | Final choice |
|---|---|---|---|
| `Failure_Timeout` | Meitu UI driver (Enhancement/Background states: "no export or Revision", Meitu "possibly busy"); Photoshop UI driver (document close: "the document may still be loaded"); fake adapter. All inside producing attempts | Unchanged: source; no Revision from the attempt. May have changed: external app state, the attempt's own working file. Unknown: whether the app is still working | "Meitu or Photoshop did not finish … may still be busy or still have the picture open … No result from this attempt was accepted". **Not** "produced no result" (the Jira example): a working file or a saved TIFF can exist |
| `Failure_Cancelled` | Orchestration/adapter cancellation ("external application may still be running"), operator Stop/Take Over (own key `Failure_AutomationStopped`/`HandedOff`, but the status line maps by code), import, imaging processors, correction package store, workspace, verifier; `Failure_MeituInterrupted` shares the code | Unchanged: source. May have changed: partial work, external app state | "Stopped before it finished. Part of it may already have been done … Meitu or Photoshop, if it was in use, may still be busy". No claim that Stop acted at once or that anything was rolled back |
| `Failure_UnknownDialog` | No current producer: the translation tables do not map to UnknownDialog. Retained for stored history | Unchanged: source; nothing clicked in the unknown window (§18.11). Unknown: window still open | "A window PrintFlow did not recognise appeared, so automation stopped without clicking in it … check Meitu or Photoshop" |
| `Failure_AdapterUnavailable` | Automation lease busy; Meitu/Photoshop processors unavailable; lease row ownership changed at commit; readiness lock; verifier; retention skip; **contained fault** of any step (`Failure_OperationFaulted` key: "no Revision; external app untouched, state unknown", also internal steps) | Unchanged: source. Unknown: external state | "Could not finish: a program it needs was unavailable or in use by another job, or the operation stopped unexpectedly". The old "already in use" sentence was wrong for a fault |
| `Failure_OutputMissing` | Adapter expected output absent; workspace file missing (working copy, Approved file); recycle target missing; inspectors; TIFF review | Unchanged: source | "A file PrintFlow expected was not found". The old "result file was not produced" was wrong for workspace/recycle cases |
| `Failure_OutputUnreadable` | Output rules, TIFF saver/inspector/decoder, trim/crop, manual-result importer, file inspector | Unchanged: source; the unreadable file was not used | "Exists but could not be read completely, so it was not used" |
| `Failure_OutputValidationFailed` | Output rules, TIFF saver/W1/inspector, promoted-TIFF mismatch ("not approved"), crop geometry, manual/correction import ("nothing was imported"; correction variants have own keys) | Unchanged: source; the file was not accepted | "Did not pass PrintFlow's checks, so it was not accepted" |
| `Failure_WorkspaceError` | FileWorkspace, recycle, correction store, trim/crop/import, Photoshop saver/W1, diagnostics evidence/package, retention | May have changed: partial file operation | "Could not finish a file operation, and part of it may already have been done" |
| `Failure_PersistenceError` | Session commit (rolled back), settings upsert (rolled back), reads; multi-commit operations (opening commit landed, closing commit failed) | Single commit: nothing saved. Multi-commit: partly saved. Unknown to the sentence which | "Could not read or save the job's record, so the screen may not show its latest state". Never "not saved"; the Session wrapper says "could not confirm whether that action completed" for this code |
| `Failure_PreconditionNotMet` | 159 references: engine refusals, stale-screen identity checks, eligibility, stop with no run, recovery no longer available, repository guards | Unchanged: source. Not claimed: that nothing else changed | "Not possible in the job's current state; the screen may have been out of date" |

## Source-to-copy matrix — wrappers

| Key | Consumer / screen | Facts that bound the copy | Available action | Code reachable in | Final choice |
|---|---|---|---|---|---|
| `Session_ActionFailed` (+ new `Session_ActionUnconfirmed`, `Session_ActionFailedNext`, `Session_ActionFailedNextStale`) | `SessionViewModel` notice: `RunServiceAsync` (every command), Stop, Take Over, Continue with this size, correction file repair | Command refusals write nothing; integrity mismatch invalidates a Revision; TIFF promote/recycle precede a commit; producing steps persist a failed attempt; closing commits can fail after opening commits. The screen reloads after a failure, and that reload can fail silently | The status line, which is already built from legal commands only (Retry named only when `CanRetry`; correction wait names Import corrected image). Help path always available | In-place Error details (this failure's code). The status line's own failure stays on the existing Error Details screen | "That action did not complete. {nested}" or, for PersistenceError, "PrintFlow could not confirm whether that action completed. {nested}". Then: status + help, or, when the reload failed, "The screen could not be updated afterwards. Before doing anything else, open Error details below and ask a colleague or supervisor." Retry is never named |
| `Home_ResumeFailed` | Home Resume and recovery Open | `LoadAsync` only SELECTs and builds a view | Choose the same button again (read-only); help | In-place Error details | "Could not be opened. Opening a job only reads it, so nothing was changed. You can choose {Resume / Open job to import corrected image} again …" |
| `Home_AbandonFailed` | Home Abandon | One metadata commit, rolled back on failure; Abandon emits only ReleaseAutomationLock + MarkSessionAbandoned (no cleanup, no delete); the list is refreshed afterwards | Read the job's state in the list; help | In-place Error details | "Could not be abandoned. Abandoning never deletes files, so its imported file, approved outputs and history are kept. Check the job's state in the list …" |
| `Home_RecoveryFailed` | Home Restart step / Import manual result / Abandon | Import manual result can commit a hand-off before the submit fails and records a failed import attempt; Restart = Retry or Return to automation (no processor) | Open the job to see its state; do **not** repeat at once; help | In-place Error details | "Could not be completed. Part of it may already have been recorded (for example, the job may now be handed off), so do not repeat it straight away. Open the job from the list …" |
| `WorkflowSelection_Refused` | Workflow Selection Choose | The name commits first and is not rolled back; SelectWorkflow emits only `PersistWorkflowSelection`, never a producing step | Choose again (non-producing); help | In-place Error details | "Could not be selected. No processing was started, and the output name shown is kept. You can choose a workflow again …" |
| `Settings_SaveFailed` | Settings Apply | One transaction, rolled back on failure; the language is switched only after a commit; the entries stay on screen | Choose Apply again (same idempotent write); help | In-place Error details | "Could not be saved. Nothing was changed: the previous settings still apply and your entries are still shown. You can choose {Apply} again …" |

Not changed by this task: the non-listed Home/Settings/selection notices that still show a code in brackets (`Home_RecentUnavailable`, `Home_RecoveryUnavailable`, `Home_RemoveFailed`, `Settings_LoadFailed`, `WorkflowSelection_OutputNameRefused`, `Home_ImportRefused`, `DiagnosticPackage_*`); the correction and delivery messages.

## Error details gap (precise)

The existing Error Details screen is bound to one persisted failed attempt (`GoToErrorDetailsAsync(sessionId, attemptId)`), and navigation has no return target ("no back stack"). A refused command, a Home/Settings/selection failure and a failed recovery call on the Error Details screen have no attempt. Opening that screen for them would need a navigation/return-target change and would reload the screen being left, losing drafts. So each touched notice gets an in-place "Error details / 错误详情" disclosure, using the existing heading and "Code / 错误代码" labels, showing the exact `FailureCode` of the failure the notice describes. The full screen (paths, screenshot, package, recovery) stays attempt-only. This is reported as a limitation, not claimed as the full screen.

## Continuation after Claude's session limit

The original review found that appending the trim-editor-closed sentence discarded the notice's failure identity. Claude had only partly edited this before stopping. The continuation retains the appended resource key, so the exact failure code, next-step line and whole translated notice survive a language change. Correction preparation/import wrappers now retain their own code in an adjacent disclosure without changing request guards or their existing guarantees.

The original full-width failure notice reduced the picture row by up to 83 px. The final layout places action-failure guidance at the top of the existing right-hand scrolling panel and preserves the original one-line notice spacing above the picture. A new failure scrolls that panel to the notice using view state only. The main explanation remains outside the collapsed technical details. Both collapsed and expanded disclosures are tested against the original preview geometry.

### Notice-only specialised variants

`DisplayNames.FailureNotice` selects 13 small localised variants by exact existing MessageKey; it never parses translated text or changes stored failures. Session action notices, correction failure wrappers and Error Details action notices use it. Full attempt Description and original specialised resources remain unchanged. The exact bilingual values and claims are in the copy-review table.

| Variant suffix | Source boundary and bounded fact |
|---|---|
| RevisionIntegrityMismatch | `RevisionIntegrityGuard` also rejects already invalid/released or unreadable records, not only a changed hash. Identity is unconfirmed and the decision was not applied. Real trim refusal still offers approval, with no legal Run/Retry. |
| OperationFaulted | `SessionService` fault containment records no validated result/Revision and leaves external state unknown; it does not establish that no working file exists. |
| MeituLaunchFailed / PhotoshopLaunchFailed | `Win32ExternalAppWindowLocator` includes null/throw from Process.Start, plus processor readiness failures. The app may never have started. |
| MeituTargetLost / PhotoshopTargetLost | Scoped input and guarded driver/bridge identity checks refuse further input after loss of the verified target. Earlier work is not claimed undone. |
| MeituUnknownState | The guarded driver can fail after dispatching save-confirmation close, and processor recovery can be ambiguous. State unconfirmed; some work may have happened. |
| PhotoshopUnknownState | TIFF saving can fail after SaveInvocationCount becomes nonzero; the native bridge can have unknown action count. State unconfirmed; some work may have happened. |
| MeituBlockingDialog / PhotoshopBlockingDialog | Guarded processing observes a blocking dialog and refuses unidentified dismissal. No repeat-action advice is inherited. |
| MeituInterrupted | Processor cancellation stops further input but may leave external work running. Check Meitu; do not assume rollback. |
| PsdPreparationFailed / PdfPreparationFailed | SessionService independently rejects missing/wrong/unverified raster output; preparation uses managed working paths. No validated raster is available; a partial working file is not ruled out. PSD source protection is retained. |

The stale-reload path is real: a producing failure can be followed by a failed LoadAsync. Its old specialised sentence still recommended retry before the wrapper's stale-screen warning. These variants remove that embedded Run/Retry advice while retaining application/failure facts. No retry, recovery, persistence, fitting, approval, output invalidation or command gating changed.

**Legacy diagnostic residual:** the unchanged full attempt Description can still show specialised legacy Run/Retry advice or overclaims, including UnknownState, LaunchFailed and IntegrityMismatch. This is outside the notice-only correction and is not reported as fixed. The original listed generic Failure_* descriptions are updated. The in-place disclosure limitation and owner copy review also remain explicit.

### Continuation routing and evidence discipline

Policy 2.4, route_offset 0, approved plan retained. UI unit requested/accepted `gpt-6-astra` High in a native subagent; runtime metadata remains UNVERIFIED. Coordinator's normal route is Sol Medium; no main-session live switch is exposed, so MODEL_SWITCH_UNAVAILABLE is recorded without claiming a downgrade. A fresh read-only native reviewer is used because the original Claude reviewer cannot be resumed through this host; requested Astra High for the final all-path preservation and WPF acceptance audit, actual runtime metadata UNVERIFIED unless exposed. No global configuration was changed.

Focused failures were observed before the new fixes: language retention, preview size, missing correction disclosures, illegal nested advice, scrolled notice visibility and unsupported preservation claims. Compilation failures were not counted as RED, and no failed build was followed by stale-binary tests. Earlier Claude mutation runs remain labelled as mutations. Raw logs, TRX, producer pointers, renders and the original review are retained under `artifacts/pf-opux-scrum11151/codex-continuation/` locally.

The fresh independent review found one further P2: a diagnostic-package preview failure's old text could hide a later Back/recovery/details-refresh failure and its code. Six bilingual public-command sequences failed before the fix; the affected 42-test suite passed afterwards. Clearing old text when setting the new failure fixed the precedence without changing any command or service call. The same reviewer rechecked the three changed source/test files and approved the final code candidate subject to settled verification. It reran no tests. Earlier Claude P1/P2 findings are resolved within this task's bounded surfaces; the diagnostic and human-acceptance limits remain open.

### Acceptance map and limits

| Item | Evidence / disposition |
|---|---|
| AC1 — plain sentence, exact originating code reachable | Notice disclosures on all five screen classes plus correction panels; nested/appended and sequential replacement regressions. In-place code disclosure is intentionally documented as less than the attempt-bound full diagnostics page. |
| AC2 — truthful all-path claims | Per-key matrices above and exact helper claims in the copy-review table; source protection, atomic settings writes and non-producing actions audited; uncertain/partial work stated explicitly. Independent source review supports these boundaries, not exhaustive execution of every producer. |
| AC3 — legal next step | Shared copy avoids unconditional Run/Retry; notice-only variants remove misleading embedded advice; current status supplies available actions, failed refresh supplies help. Real trim refusal, correction handoff and stale-load tests preserve command availability. |
| AC4 — both locales and reachable layout | Resource parity plus off-screen WPF tests at 1000×700 and 1920×1040, 96 DPI; code actually brought into the viewport; preview/crop geometry unchanged. No live workstation/DPI or physical-input acceptance inferred. |
| Owner bilingual copy review | OPEN. Review the final table and record acceptance or explicit waiver on SCRUM-11151. Independent agent review does not satisfy this. |

Nonblocking residuals: English Home button labels are unquoted in prose; Home refresh/notice ordering and disclosure expansion state remain as before. Earlier 11150 Domain-invalid size inline-hint residual, real-window/UIA and production-startup exclusions, and the known hanging recovery test are unchanged. The excluded recovery case is NOT PASS. No 11152+ work or global-rule changes were performed.

## Final verification and publication

The reviewed 30-path code candidate was committed as `924281d22974f1693900052851f05c7ddfda750a`, tree `cb617e7d71d5529da5291e11e680f8f536474c27`, and normally fast-forward pushed to master. Fetch, ls-remote and authenticated GitHub readback all verified that SHA. Final sources match the reviewed hashes.

| Final gate | Observed result |
|---|---|
| Clean solution build, --no-incremental | PASS; 0 warnings, 0 errors |
| Architecture | 473/473; 0 failures/skips |
| Corrected safe combined UI/resource filter | 681/681; 0 failures/skips |
| Final off-screen captures | 36: 9 contexts × 2 languages × 2 viewports, at 96 DPI |
| Latest independent-review fix | Six behavioral failures before the fix; affected suite 42/42 after it; counts overlap with final UI |
| Source/copy table fidelity | All 66 locale values (33 keys × 2) present exactly |
| Jira exporter and independent raw/snapshot/CSV oracle | PASS; 17 issues, 26 Blocks, 51 string labels, 34 exact timestamps, 23 columns, UTF-8 BOM |

Final logs, TRX, frozen candidate hashes and 36 captures are retained locally in `artifacts/pf-opux-scrum11151/codex-continuation/`. Earlier settled 675/675 and intermediate/mutation runs are not the final gate. The final tests followed a successful build of these sources; excluded tests are not counted as passing.

After verified code publication, the immediately preceding Jira read found zero comments. One marked evidence comment was added: `10185`, created `2026-09-29T18:11:13.746+1300`. The freshly discovered transition `31` moved SCRUM-11151 from In Progress to In Review; readback updated `2026-09-29T18:11:22.295+1300`. Owner copy review remains OPEN, and Done was not set.

The complete authenticated initiative read at `2026-09-29T05:11:42.773Z` has one final page and complete comments. Compared with the resume snapshot, only this issue's status/updated/comment changed, plus the embedded linked status on 11154/11155. All prior comments, descriptions, AC, labels, parents and relationships remain unchanged. Only 11151 has STATUS_TRANSITION_AND_COMMENT attribution; the other 16 rows are READ_ONLY.

The public CSV is [SCRUM-11151_JIRA_FINAL.csv](SCRUM-11151_JIRA_FINAL.csv); raw responses and account-bearing readback remain ignored and local. The documentation commit publishes this plan, the exact copy-review table, CSV, public-safe audit, one exporter mapping and the new HANDOFF section. Its final SHA is recorded in the local final audit and execution report after verification, without a self-referential follow-up commit. Earlier 11149/11150 local audit residues remain unchanged and unstaged.
