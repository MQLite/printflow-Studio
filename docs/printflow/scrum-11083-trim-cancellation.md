# SCRUM-11083 — Keep original extent

Date: 2026-09-07. Canonical repository: `D:\Repositories\printflow-Studio`, `master`.

## Requirement authority

Read the original row from `C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv` before Product changes. Its original Work Item ID is **11206**, mapped to **SCRUM-11083**, title **Implement Independent Trim Review and Adjustment**, parent 11200 (SCRUM-11077), High priority, 5 points.

Exact original description / acceptance criteria:

> Make trimming a mandatory independent review step where applicable. Show before-and-after views, allow adjustment or cancellation, and produce Review Required before print dimensions or asset completion may continue. Any operator adjustment creates a new result that must be reviewed rather than silently mutating an already approved trim.

The supplied clarification defines cancellation as retaining the approved pre-Trim canvas and continuing. It does not approve a cropped result or abandon the session. Independent review remains mandatory for every actual Trim result the operator chooses to use.

Inspected `WorkflowCatalog`, `StepDefinition`, commands, transition table/engine, `SessionStep`, `WorkflowSnapshot`, `SessionService`, `SessionView`, Session UI, review/return/invalidation and TrimGeometry persistence paths, and the accepted SCRUM-11081 and SCRUM-11079 reports. No historical phase number is used as proof of original-AC completion.

## Previous gap and representation

Both workflows containing Trim set `IsSkippable: false`. Generic Skip also rejects ReviewRequired, so changing the flag alone could not offer one-action cancellation of a result awaiting review. Its generic default reason would not express the operator's canvas choice.

Added the typed `WorkflowCommand.KeepOriginalExtent`, limited by the engine to current Trim with an approved upstream Revision. It persists the existing `StepState.Skipped`, stable reason `Operator chose to keep original extent`, and state-entry timestamp. The current Revision ID/hash are cleared. No schema, opaque JSON, FailureCode, or new processing/review status is needed. Generic Skip eligibility is unchanged.

`Skipped` is a truthful finished step with no output authority. `RecordSkip` describes the effect; the existing `SessionService` metadata transaction persists the step rows. An explicit command is needed for intent and legality, while the existing state and persistence are sufficient for the outcome.

Legal states: Waiting, ReviewRequired, RetryRequired, Failed (including ManualCropRequired), Interrupted. Processing and finished states reject it; unrelated current steps, absent Trim, inactive sessions and missing approved upstream reject it. AvailableCommands probes the same engine handler. The existing file-integrity guard verifies the pre-Trim file before retaining it.

## History, restart and downstream authority

- **Waiting:** no processor, ProcessingAttempt, Revision, output file or TrimGeometry is created.
- **ReviewRequired:** the actual successful attempt, cropped Revision, file, geometry and review history stay unchanged. Clearing the current result pointer supersedes that offer. No fabricated approval or rejection row is recorded, and no rejection reason is requested.
- **ManualCropRequired:** the real failed attempt stays as evidence; the operator continues without producing any manual crop or pretending one occurred. CancelManualCrop retains its separate meaning.
- **Restart before deciding:** Trim remains Waiting; Run and Keep original extent remain available. No decision is inferred.
- **Restart after deciding:** Skipped plus its reason survive SQLite reload. The workflow remains advanced and no processing is launched by load.
- **ReturnToStep:** the existing Reset clears the no-trim state/reason and current results from the target onward, and clears downstream sizing decisions. Returning to Trim permits a new real attempt and independent review. Returning earlier invalidates derived export work and requires a new decision against the changed upstream. Existing approved/rejected review rows remain history.

`UpstreamRevisionOf(next)` resolves to the exact approved Revision that entered Trim. Print Dimensions sees that Revision's original canvas dimensions; its persisted print-preparation plan binds the same Revision. Prepare Design Asset promotion consumes the same source, retains the full dimensions, and copies the approved bytes under the existing export semantics. It cannot select a historical cropped offer.

No artificial TrimGeometry is stored. Historical geometry belongs only to real earlier attempts. No new successful no-op attempt or duplicate Revision is used to advance. The command emits only RecordSkip and takes the metadata path, never the processing or AutomationLock path; tests also exercise a lock held by another session.

## Operator surface

The existing Session action row contains a standard WPF Button with stable AutomationId `Session.KeepOriginalExtent`. The engine-derived capability controls visibility and execution; IsBusy also disables execution.

English: **Keep original extent**, hint **Continue without trimming**, completed Trim row **Original extent retained**. Chinese: **保留原始范围**, **不裁剪并继续**, **已保留原始范围**. Exact wording tests pin both cultures. `SessionView.OriginalExtentRetained` exposes the explicit outcome from persisted step state, rather than file absence.

Standard WPF focus traversal, keyboard activation and InvokePattern use the normal ViewModel/service command. The shared Before/After implementation, synchronized pan, backgrounds, slider and toolbar are unchanged. No SCRUM-11082 margin controls, crop rectangle persistence or manual geometry redesign is included.

## Verification

New regression suites:

| Suite | Evidence |
| --- | --- |
| `KeepOriginalExtentTests` | Exhaustive workflow/step/state legality; engine capability parity; exact downstream Revision; only RecordSkip, hence no processing, Revision, review or lock effect; missing upstream and inactive sessions refused. |
| `KeepOriginalExtentPersistenceTests` | Waiting/review/manual paths in both workflows; restart before/after; unchanged files, attempts, Revisions, reviews and geometry; full-canvas export and input ID; Print Dimensions/plan authority; approved derived upstream rather than import or historical Trim; return/re-run with and without earlier approval; upstream-change invalidation; another session's lock untouched; mutated source refused. |
| `KeepOriginalExtentUiTests` | Rendered bilingual action, stable ID, standard Invoke provider, tab-stop/focus properties, real command progression and localized retained outcome; action hidden on unrelated/advanced steps; three opt-in real-window cases. |
| Existing `TransitionMatrixTests` | New command participates in every state/command pair and every engine workflow/step/state/command combination. |

Intermediate broad targeted run (workflow, Trim, persistence, Session accessibility and localization): **9,215 passed, 0 failed, 0 skipped**. Final focused run: **162 passed, 0 failed, 0 skipped**. Full-suite results follow below.

The first added tests exposed fixture assumptions: ManualCropRequired is returned as a failed OperationResult plus persisted Failed state, and a skipped synthetic row must carry no Revision. Those fixtures were corrected. A later integrity regression initially hit the source file's read-only protection; the deliberate-tampering test now explicitly removes that attribute before changing its synthetic bytes. Product protections were not weakened.

Clean build with installed SDK `10.0.400` at `C:\Users\admin\AppData\Local\Microsoft\dotnet\dotnet.exe`: **0 warnings, 0 errors**. No SDK/dependency configuration was changed. Logs and TRX are retained locally under `evidence/scrum-11083/` (git-ignored).

Final-source complete suite: **11,353 passed, 0 failed, 0 skipped**, elapsed **3 minutes 30 seconds**. This is the accepted 10,903 baseline plus 162 focused cases and 288 additional cases generated by adding the command to the existing exhaustive matrices. The three opt-in live cases are included in the normal count with their environment gates closed; all three live bodies also passed separately as documented below. The known unrelated PSD settle-poll flake **did not occur**, and no exact-case rerun or unrelated change was required. Full result: `full-final.log` / `full-final.trx`; focused result: `targeted-final-pass.log` / `.trx`.

## Live synthetic proof

Three bounded tests ran with `PRINTFLOW_KEEP_EXTENT_LIVE=1`, each in a fresh test process, using real shown HWNDs, production `SessionScreenView`, real SessionService, SQLite repository, workspace files, deterministic Trim and manual-crop eligibility. Setup imports generated artwork into Prepare Customer Design and uses normal confirmation/skip commands to reach Trim. No external app processing or installed-shell flow is claimed. Source canvases are 12×10 pixels.

1. **Review:** Windows UIA RunStep invokes real automatic Trim, then UIA finds Before and After on ReviewRequired. Keyboard focus traversal reaches Keep original extent; InvokePattern advances to Print Dimensions. Upstream Revision `01a079b7-bb1f-7425-95f3-09e05cc75a4b` remains authoritative; historical Trim Revision `01a079b7-be7a-799b-a47a-dcbc07f4e156` remains present and unapproved.
2. **No alpha:** real opaque Bgr24 source causes ManualCropRequired. The same keyboard-accessible UIA action advances without any manual-crop output. Authoritative upstream Revision: `01a079b7-d508-7695-b4d4-8ffba6025458`.
3. **Before processing:** keyboard traversal reaches the action and a WPF routed Enter invokes the standard Button. It advances without a Trim attempt or file. Authoritative upstream Revision: `01a079b7-eda2-795d-a9e0-771adb14026b`.

All three independently reload persistence and read files after the UI action: Trim is Skipped with the explicit reason and no current result; Print Dimensions uses the pre-Trim Revision and its original dimensions; no files, attempts, Revisions or reviews were added by retaining the extent; upstream bytes and the automation lock are unchanged. Downstream controls accept focus and normal traversal continues. No screenshots or coordinate clicks are used. Keyboard evidence uses WPF focus traversal and routed Enter, not global physical keystrokes.

Final live results: **3/3 passed**, in separate one-case processes. Transcripts: `keep-extent-live-review.txt`, `keep-extent-live-manual.txt`, `keep-extent-live-waiting-keyboard.txt`; corresponding `live-final-*.log` and `.trx` under the evidence directory. Transparent-source SHA-256: `7C0C301F48992E292768A1156278CEAD3BF05829A792C53A80194E1F8981113B`; opaque-source SHA-256: `D077D59C35EFE4F40AAA08A864D3F658ED2D2988D1547EB090FE4BB786F4D778`.

Rejected live attempts are retained in `live.log`/`live.trx` and `live-second.log`/`live-second.trx`. Initially UIA could not find RunStep; the fixture now centers the window and completes its initial layout. A subsequent multi-window process encountered a Windows UIA COM timeout at FromHandle. Each final case used a fresh test process and passed. These failures are not counted as proof, and no shared-review or Product workaround was added.

Commands (using the pinned executable above):

```powershell
dotnet clean PrintFlowStudio.sln -v minimal
dotnet build PrintFlowStudio.sln --no-restore -v minimal
dotnet test tests/PrintFlow.Tests --no-build --filter 'FullyQualifiedName~KeepOriginalExtent'
# Each live case runs separately with PRINTFLOW_KEEP_EXTENT_LIVE=1:
dotnet test tests/PrintFlow.Tests --no-build --filter 'FullyQualifiedName~Live_synthetic_WPF_UIA_review'
dotnet test tests/PrintFlow.Tests --no-build --filter 'FullyQualifiedName~Live_synthetic_WPF_UIA_manual_crop_required'
dotnet test tests/PrintFlow.Tests --no-build --filter 'FullyQualifiedName~Live_synthetic_WPF_UIA_waiting_keyboard'
# Full suite uses the ordinary environment, with opt-in live gates closed:
dotnet test PrintFlowStudio.sln --no-build --logger 'trx;LogFileName=full-final.trx' --results-directory evidence/scrum-11083
```

## Git and coverage

Started clean on `master` at `094809d` (accepted SCRUM-11079). New local commit only; no branch, worktree, alternate clone, amend, rebase, push or attribution trailer. Unrelated PSD settle-poll code is untouched.

**SCRUM-11083: previous audit PARTIAL → current FULL.** The original criteria are satisfied: independent Trim review and Before/After remain intact; adjustments still produce a new result requiring review; explicit cancellation now retains the approved upstream extent with truthful history, persistence and downstream authority.

The dated addendum in `original-jira-functional-coverage-reaudit.md` preserves the historical row. **SCRUM-11077 remains PARTIAL. SCRUM-11082 remains PARTIAL**: the manual-crop controls and persisted manual rectangle remain outside this slice.

Implementation, tests, this report and the coverage delta are included in a new local commit on `master`; the final response identifies it. `git diff --check` passes. No changes are pushed.

**PASS — SCRUM-11083 INDEPENDENT TRIM REVIEW AND CANCELLATION VERIFIED**
