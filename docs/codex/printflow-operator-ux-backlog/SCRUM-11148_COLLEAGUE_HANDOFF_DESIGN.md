# SCRUM-11148 — Ask a colleague to correct a background-removal result: design

Task `PF-OPUX-v1-SCRUM-11148-design-v1` (PLAN_ONLY / REVIEW_ONLY), planning id `PF-OPUX-v1-colleague-correction-handoff`, 2026-09-28 NZ.
**Status: PROPOSED — AWAITING_OWNER_REVIEW.** Nothing here is authorized for implementation, migration or approval of the contract changes it proposes. Review record: [SCRUM-11148_COLLEAGUE_HANDOFF_DESIGN_REVIEW.md](SCRUM-11148_COLLEAGUE_HANDOFF_DESIGN_REVIEW.md).

Evidence labels used throughout:
- **[SRC]** current-source fact, read in the working tree on 2026-09-28 (`master@eea60198`, with the accumulated uncommitted SCRUM-11144…11147 work).
- **[REC]** dated recorded evidence from an earlier document or Jira.
- **[PROP]** design proposal. Nothing labelled PROP exists yet.
- **[UNV]** unverified assumption or runtime behaviour. No build, test, application run, picker, Explorer or desktop action was performed for this design.

Names used below:
- **R**: the background-removal result currently under review.
- **U**: the picture background removal consumed, that is, R's input.
- **R2**: the corrected picture the operator imports.

## 1. Requirement source

- **Authenticated, read-only Jira read (2026-09-28).** SCRUM-11148, issue id 10878, Task.
  - Status To Do; updated `2026-09-22T14:27:26.992+1200`; 0 comments.
  - Labels `operator-ux`, `pf-opux-v1`, `printflow`. Parent SCRUM-11139 "Operator Usability & Workflow Simplification" (the initiative Epic).
  - It blocks SCRUM-11154 and SCRUM-11155.
  - The eight AC below are quoted verbatim from that read. No Jira write was made.
- **AC.**
  1. Given a background-removal review, then "Ask a colleague to correct this image" is visible next to Approve and Reject, and choosing it without a note succeeds.
  2. Given the handoff completed, then the panel shows the reference copy, the working copy, their folder with an Open folder action, the return format, where to save and how to return; the reference copy is marked do-not-edit.
  3. Given the handoff, then the real original, InputSnapshot and approved Revisions are unchanged and not offered for editing.
  4. Given the session is handed off, then Home, Recent and the Session screen show it as handed off, never as processing, and a normal Run does not resume it.
  5. Given the colleague saved a PNG and the operator chooses Import corrected image, then the file is imported as a new Revision that is still reviewed, and the next step shown is the one after background removal, not background removal again.
  6. Given an unsupported file (wrong format, no transparency where required, unreadable), then the import is refused with a plain reason and the handed-off state is kept.
  7. Given no corrected file is imported, then nothing happens automatically; PrintFlow does not watch folders or Photoshop.
  8. In zh-CN and en the panel is not clipped, keyboard reachable, and states are readable without colour.
- **Non-goals, from Jira and the task prompt.**
  - No eraser, restore brush, mask editor or other editor.
  - No accounts, assignment, notifications or cloud sharing.
  - No Photoshop-save watcher, automatic pickup or automatic attachment.
  - No manual TIFF return; no extension to other steps.
  - No KeepOriginalExtent PNG repair; no crop-page redesign; no other Task.

## 2. Source map

### 2.1 Current-source facts

| # | Fact [SRC] | Location |
|---|---|---|
| F1 | `HandOff` is allowed in the `ReviewRequired`, `RetryRequired`, `Failed` and `Interrupted` rows. `Destination(HandOff)` is `ReviewRequired`, but the handler keeps the step as it is. | `Engine/TransitionTable.cs` L165–212, L287 |
| F2 | The `HandOff` handler refuses an empty reason. It emits `CreateWorkingCopy(step, LatestApprovedRevisionId ?? UpstreamRevisionOf(step))`, `OpenForManualWork`, `ReleaseAutomationLock` and `MarkSessionHandedOff`, and sets the session to `HandedOff`. | `Engine/WorkflowEngine.cs` L1149–1180 |
| F3 | **No code realizes `CreateWorkingCopy` or `OpenForManualWork` outside the producing path.** `ExecuteCoreAsync` commits `HandOff` through `BuildMetadataMutation`/`MergeSession`, which apply only the session fields and the lock release. An operator handoff therefore creates **no file today**. Its declared source would also be the pre-background input, not R. | `Services/SessionService.cs` L553–607, L3103–3152, L3259–3281; `WorkflowEffect.cs` L36, L170 |
| F4 | `ManualResultEligibility.CanSubmit` requires a `HandedOff` session and a current Enhancement/BackgroundRemoval step in `Failed`, `Interrupted` or `RetryRequired`, with an upstream Revision. | `Services/ManualResultEligibility.cs` |
| F5 | In `HandedOff`, `Resolve` refuses every step-scoped command except one special `Reject` of a current Enhancement/BackgroundRemoval `ReviewRequired` result. `ReenterAutomation` refuses a `ReviewRequired` step. | `WorkflowEngine.cs` L1719–1731, L1676 |
| F6 | `SubmitManualResult` moves the step to `Processing`, clears its current Revision and sets the session `Active`. It emits `RecordAttemptStarted(ManualResultImport, input = UpstreamRevisionOf(step))` and `ImportManualResult`. | `WorkflowEngine.cs` L1606–1629 |
| F7 | The import producing path writes `ManualResultSourcePath`, a background-removal authority `ManualResultForReviewedContent` bound to the input Revision, and a new Revision with `SourceRevisionId = input`. `AttemptSucceeded` then moves background removal to `ReviewRequired`. | `SessionService.cs` L1854–1910, L2091–2177 |
| F8 | A failed manual import closes the attempt `Failed` and re-applies `HandOff` with "Manual result validation failed; manual processing remains authorised." The step becomes `Failed` with no current Revision. | `SessionService.cs` L2682–2690 |
| F9 | Importer limits for background removal. | `Infrastructure/Imaging/WicManualResultImporter.cs` |
|    | – Extension `.png` only; `Path.IsPathFullyQualified` (a UNC path also passes); the file must exist. | |
|    | – 1 byte ≤ size ≤ 256 MiB; pixel count ≤ 100,000,000; the inspector reports PNG with pixel dimensions. | |
|    | – Exactly one frame. Width and height equal the **input Revision's** dimensions (`Failure_ManualResultCanvas`). | |
|    | – At least one pixel with alpha < 255 **and** at least one with alpha > 0 (`Failure_ManualResultTransparency`). | |
|    | – The source is held with `FileShare.Read` and hashed; the managed copy (`CreateNew`) is hashed again before and after the full decode; the copy is then set ReadOnly. | |
|    | – No resize, conversion or colour transformation (`IgnoreColorProfile` affects only the decode check). | |
| F10 | Meitu's cutout rule requires output pixel dimensions equal to the source, so R's canvas equals U's. | `Adapters/Meitu/MeituCutoutOutputRule.cs` L84–97 |
| F11 | `UpstreamResultOf(BackgroundRemoval)` is the last earlier step offering a Revision: the approved Enhancement result, or the confirmed original/prepared raster when Enhancement was skipped. | `Engine/WorkflowSnapshot.cs` L566–618 |
| F12 | Workspace layout: `Sessions\S_…\{Source, Working\<attemptId>, Revisions, Approved, Rejected, Logs}`. `Source` is copied read-only. Startup recovery scans only `Working\`: unknown folders are reported and left untouched; files of Failed/Interrupted/Cancelled attempts are quarantined. Retention deletes only listed, hash-matching `Working` copies. Nothing deletes directories recursively. | `FileWorkspace.cs` L13–30; `StartupRecoveryService.cs` L376–449; `SessionRetentionPlan.cs` L103–118 |
| F13 | The session row stores `HandedOffAtUtc` and `HandOffReason` only. No table records what was handed out, where, or for which Revision. Attempts cannot record a successful non-producing action, because `SUCCEEDED` requires an output Revision. | Migrations 0001, 0012, 0016 |
| F14 | The Session screen shows a generic "Hand off manually" button whenever `HandOff` is legal, including background-removal review. It uses the fixed reason "Handed off to the operator from the session screen." | `SessionViewModel.cs` L462, L1595, L2211; `SessionScreenView.xaml` L2105–2109 |
| F15 | Background-removal Approve is the plain `Approve(step, hash)`. Only Trim uses `ApproveExactReviewAsync(id + hash)`. | `SessionViewModel.cs` L2155–2169 |
| F16 | Recent rows show `DisplayNames.SessionState(State)`: en "Handed off", but zh-CN `SessionState_HandedOff` is **"已转交"**. The Session status `Session_StatusHandedOff` and the terminology reference use "已移交". `CanContinueProcessing` is false for `HandedOff`, so the row offers "Details", a label its code comment reserves for finished jobs. Home recovery lists only Interrupted/Failed/RetryRequired/Processing steps with an unresolved interruption. | `RecentSessionRow.cs` L38–43, L112–117; `Strings.zh-CN.resx` L194, L1026; `SessionStateRules.cs`; `SessionService.Recovery.cs` L39–68 |
| F22 | Stop is offered for **any** Running attempt (`CanStopAutomation => State == Running`), including a manual-result import. `StopAttemptAsync` closes it `Cancelled` and moves the step to `Interrupted`. It re-hands off only for TakeOver, so a stopped import leaves the session `Active`. Startup recovery applies only `AttemptInterrupted`, which also leaves an interrupted import `Active` (session set Active by F6). From `Interrupted`, StartStep and Retry are legal. The handed-off status panel recommends `ReenterAutomation` whenever it is legal. | `AutomationRuntime.cs` L77; `SessionService.cs` L2768–2821; `StartupRecoveryService.cs` L281–340; `TransitionTable.cs` L205–212; `SessionViewModel.OperatorStatus.cs` L71 |
| F17 | `IFilePicker.PickSingleFile(title, filter)` has no initial folder. `IDeliveredFileShell.SelectInFolder` needs a verified *delivery* selection lease. | `Navigation/IFilePicker.cs`; `Delivery/IDeliveredFileShell.cs` |
| F18 | `SessionCompletionGate` is a per-session `SemaphoreSlim`. `ExecuteAsync`, `ApproveExactReviewAsync` and `ResolveRecoveryAsync` each take it once and call `ExecuteCoreAsync`, never `ExecuteAsync`, to avoid re-entry. | `SessionCompletionGate.cs`; `SessionService.cs` L370–426; `SessionService.Recovery.cs` L96 |
| F19 | Test `A_handed_off_review_offer_must_be_rejected_before_manual_replacement` pins today's rule: after a generic review-state handoff, the operator must Reject before a manual result can be submitted. | `tests/.../ManualResultImportTests.cs` L249–262 |
| F20 | Trim adjustment (SCRUM-11147) applies only to a Trim result whose producing Trim attempt carries geometry. An approved upstream that came from a manual import is a valid pre-trim source once Trim has run on it. | `Services/TrimAdjustmentEligibility.cs` L78–131 |
| F21 | An `AutomationLockChange.Release` updates the lock row only when it belongs to this session or is empty. Otherwise `changed != 1` **rolls back the whole commit** ("changed ownership"). A `ReviewRequired` background-removal step already released its lock at `AttemptSucceeded` (adapter-backed). `FailAttemptAsync` filters `ReleaseAutomationLock` out of its re-handoff, and manual-import closings set `LockChange = null`. | `SqliteSessionRepository.cs` L278–287, L923–934; `WorkflowEngine.cs` L1508–1511; `SessionService.cs` L2689, L2143 |

### 2.2 The mismatch, exactly

A background-removal review handed off with today's `HandOff` lands in session `HandedOff`, step `ReviewRequired`, with R still current (F1, F2). From there:
- `SubmitManualResult` is refused (F4).
- `ReenterAutomation` is refused (F5).
- Approve is refused because the session is not Active (F5).
- No file is prepared (F3).

The only exits are the special Reject, which records a human rejection and turns R into `RetryRequired` before import is allowed (F19), or Abandon. The historical Jira observation (HandOff "creates a working copy from the latest approved or upstream Revision") is **partly outdated**: the effect is declared but never executed, and it names the wrong file for this purpose.

## 3. Bounded alternatives and recommendation

| | A. Widen `CanSubmit` to handed-off `ReviewRequired` | B. Move the step to `RetryRequired` on handoff (Jira example) | **C. Dedicated exact-target request/import pair (recommended)** |
|---|---|---|---|
| Truth | Every existing handed-off review, including legacy generic ones, becomes importable. That is the broad allowance the task forbids. | Manufactures retry evidence: `RetryRequired` means rejected or needs another attempt. It clears R without a decision and exposes Retry and Run. | R stays under review, unjudged. Asking for help is neither approval nor rejection. |
| Binding | No record of which R or files were handed out, so no restart display. | Same gap. | A persisted request binds session, step, R and U by id and hash, the folder and the two files. |
| Files | None (F3). | None. | Prepared and verified before "handed off" is committed. |
| Default reason | Needs the global guard relaxed, which is forbidden. | Same. | A reason is carried only by the new command; the `HandOff` guard is unchanged. |
| Risk | Low code, high semantic risk. | Medium; contradicts F19 semantics. | Moderate code, contained semantics. |

**Recommendation: C.** It reuses the existing `HandedOff` session state, the `MarkSessionHandedOff` effect, the manual-result producing path and the importer. It adds no parallel handoff state machine: the new request command applies the same session-level handoff as `HandOff`, through a shared private reducer helper, with an exact target and a default reason. The generic `HandOff` command and its tests (F19) are unchanged.

It deliberately omits `ReleaseAutomationLock`. A review-state step holds no lock (F21). A release would roll back the handoff whenever another session's automation, or environment verification, holds the row. This is the existing `FailAttemptAsync` precedent.

**Observation, not fixed here:** the generic `HandOff` from a review also emits the release. By the F21 source reading it would fail the same way while another session holds the lock [UNV at runtime].

## 4. Identity and persistence

### 4.1 What binds a correction request

| Identity | Bound as | Checked at |
|---|---|---|
| Session | `SessionId` | every entry |
| Workflow step | `BackgroundRemoval` only | engine and service |
| Reviewed result R, handed out as the working copy | Revision id **and** SHA-256 | request (engine and service), import (engine and service), re-preparation (service) |
| Reference U, R's input | Revision id **and** SHA-256; must equal `UpstreamResultOf(BackgroundRemoval)` and `R.SourceRevisionId` | request, import |
| Request | `CorrectionRequestId` (Guid), minted by the screen once per fresh click and reused on a blind retry | request, re-prepare, import |
| Returned bytes | new SHA-256 of the managed copy; a new Revision id R2 with `SourceRevisionId = U` (F7) | import |

Hash equality alone never authorizes. A same-hash R held by a different Revision id is refused. This matters because a colleague may return unchanged bytes (§7.5).

### 4.2 Is new storage necessary?

Existing records **cannot** hold the request (F13):
- `HandOffReason` is free text. Using it as a marker means parsing text, and a typed note would overwrite the marker.
- After a failed or interrupted import (F8), the step no longer names R. A request derived only from step state loses its binding and its folder exactly when restart and retry need them.
- An attempt row cannot represent "files prepared" without a fake output Revision.

**[PROP] Owner decision D1 — additive migration `0019_correction_request.sql`.** It adds one new table and alters no existing table, column, constraint or value.

```
CorrectionRequest(
  Id TEXT PK,                            -- the screen's intent id
  SessionId TEXT NOT NULL REFERENCES ProcessingSession(Id) ON DELETE CASCADE,
  StepKind TEXT NOT NULL,                -- existing StepKind storage; BackgroundRemoval only
  HandedOutRevisionId TEXT NOT NULL REFERENCES Revision(Id), HandedOutSha256 TEXT NOT NULL (64),
  ReferenceRevisionId TEXT NOT NULL REFERENCES Revision(Id), ReferenceSha256 TEXT NOT NULL (64),
  FolderRelativePath TEXT NOT NULL UNIQUE,   -- WorkspaceDirRef, relative like every stored path
  ReferenceFileName TEXT NOT NULL, WorkingFileName TEXT NOT NULL, SuggestedReturnName TEXT NOT NULL,
  Note TEXT NULL, EffectiveReason TEXT NOT NULL,
  Status TEXT NOT NULL CHECK (Status IN ('PREPARING','READY','RETURNED','SUPERSEDED')),
  CreatedAtUtc TEXT NOT NULL, ReadyAtUtc TEXT NULL, ClosedAtUtc TEXT NULL,
  LastImportAttemptId TEXT NULL REFERENCES ProcessingAttempt(Id),
  ResultRevisionId TEXT NULL REFERENCES Revision(Id))
UNIQUE INDEX one open request per session ON (SessionId) WHERE Status IN ('PREPARING','READY')
```

- `SessionMutation` gains `CorrectionRequestChanges`, committed in the same transaction as the session, step and attempt changes.
- `SessionAggregate` gains `CorrectionRequests`.
- Identity columns are immutable, following the precedent of the `Revision_Immutable_Update` trigger.

**"Open" is computed, not stored.** A `READY` row grants import only while `CorrectionRequestEligibility` (§6.4) holds. A `READY` row that no longer matches the session, for example because R was rejected, is displayed as history and grants nothing. Such a row is marked `SUPERSEDED` only when a later request is committed. This avoids teaching every other command path about requests.

**Fallback if D1 is declined.** Keep the note and default reason in `HandOffReason` behind a stable constant prefix, derive the folder from R's id, and derive the files from Revision rows. The consequences:
- import eligibility is lost after any failed or interrupted import;
- legacy detection relies on reason text;
- there is no idempotent intent id.

This design does not recommend the fallback.

## 5. Files: which bytes, where, and how they are protected

### 5.1 Which revision supplies each copy

| Copy | Source | Why |
|---|---|---|
| **Reference — do not edit** | U = `UpstreamResultOf(BackgroundRemoval)` (F11), R's recorded input | The exact full-resolution picture background removal consumed. It shows the hair, edges and detail the cutout may have lost. |
| **Correct this file** | R, the exact result under review | A transparent PNG at U's canvas (F10). The colleague restores missing parts from the reference and erases leftovers, then saves a PNG of the same size. |

**Explicit interpretation (owner decision D3).** "Original-reference" means *the picture as it was just before background removal*, not necessarily the uploaded original:
- When Enhancement was skipped, U is the confirmed original or prepared raster, whose bytes are the InputSnapshot or the prepared raster.
- When Enhancement ran and was approved, U is the enhanced picture, which is what the cutout was made from and what the returned canvas must match (F9).

Neither is a display-reduced preview. "Latest approved" is **not** used, because `LatestApprovedRevisionId` (F2) can be something else; U is resolved and checked against `R.SourceRevisionId`. Adding the InputSnapshot as a third file is possible but not recommended: it can differ in pixels from U after enhancement and would invite a canvas mismatch.

### 5.2 Folder and file names [PROP]

- **Folder:** `{workspace root}\Sessions\S_…\Correction\{OutputName ≤40 chars}-{first 8 hex of request id}\`, resolved only through `IWorkspace` and `PathGuard`.
  - It is outside `Working\`, so startup quarantine and retention never touch it (F12).
  - Nothing sweeps it. It survives completion and Recent removal. Disk growth is a recorded residual.
  - **Owner decision D2:** this managed location is recommended over an operator-chosen or shared folder. A shared folder needs a picker, settings and network-path support, and would reintroduce the "remembered final-save destination" confusion this design avoids.
  - The actual workspace root on the workstation is configuration-dependent [UNV]. A colleague at another PC needs the operator to copy the files, and PrintFlow says so plainly.
- **File names:** chosen once in the operator's current language, persisted in the row and never re-localized.

  | | en | zh-CN |
  |---|---|---|
  | Reference | `{name} - REFERENCE (do not edit).{ext of U}` | `{name} - 参考（请勿修改）.{ext}` |
  | Working copy | `{name} - CORRECT THIS.png` | `{name} - 请修改此文件.png` |
  | Suggested return name | `{name} - CORRECTED.png` | `{name} - 已修正.png` |

- **Instructions:** an optional `Instructions.txt` containing the panel's steps in both languages, the note, the required size (W × H px) and the return rule.
- The internal job and revision ids appear only in the database, the folder suffix and Production/Error details, never in instructions.

### 5.3 Protection on both sides

- **Only copies.** The files are byte copies made with `CreateNew`/`FileShare.None`. There are no hard links, symbolic links or directory junctions (reparse ancestors are refused, as the importer already does in F9).
  - Source\, Revisions\, Approved\ and the uploaded original are only read.
  - The reference copy gets the ReadOnly attribute as a courtesy hint. The panel never claims that makes it immutable, because authority stays in the database hashes of R and U.
- **Package files carry no authority.** The import is validated against U's recorded facts, never against the reference file, so an edited reference copy cannot change what is accepted.
- **PrintFlow never edits, moves or deletes a file in a `READY` folder.**
  - Re-preparation (§6.3) only creates missing files with `CreateNew`.
  - The only files it removes are this request's own `*.partial-{requestId8}` temporaries in a `PREPARING` folder.
  - A colleague's edited working copy or saved return is never overwritten or deleted.
- **This is not delivery.** No `ArtifactDelivery`/`DeliveryAttempt` row is written, the remembered final-save destination is neither read nor written, and approved-only export eligibility (SCRUM-11144) is untouched. The working copy may be an unapproved result, which is the point.
- **Open folder is separate from "Open containing folder".** It is a separate port (§8.4) with its own wording, and it opens a managed correction folder, not a verified delivered file.

## 6. Commands, service entries, order and locking

### 6.1 Workflow layer [PROP]

- **`WorkflowCommand.RequestColleagueCorrection(Guid CorrectionRequestId, RevisionId ReviewedRevision, Sha256 ReviewedHash, RevisionId ReferenceRevision, Sha256 ReferenceHash, string? Note)`.**
  - `EffectiveReason` equals the trimmed note, or `DefaultReason` = "Operator asked a colleague to correct the background-removal result." when the note is empty. That is stable English, following the `Skip.DefaultReason` precedent.
  - Step-scoped, in the `ReviewRequired` row only. `Resolve` requires the session to be Active.
  - Engine guards: the step is BackgroundRemoval; the current Revision equals the command's R id and hash; `UpstreamResultOf(BackgroundRemoval)` equals the reference id and hash.
  - Effects: only `MarkSessionHandedOff(now, EffectiveReason)`, through the shared HandOff helper.
  - It emits no `ReleaseAutomationLock` (F21) and not the unrealized `CreateWorkingCopy`/`OpenForManualWork`. The step keeps `ReviewRequired` with R current.
- **`WorkflowCommand.ImportCorrectedImage(Guid CorrectionRequestId, RevisionId ReviewedRevision, Sha256 ReviewedHash, string SelectedPath)`.**
  - Session-scoped like `SubmitManualResult`, because the session is `HandedOff`.
  - Engine half, legal only when all of these hold: session `HandedOff`; current step BackgroundRemoval in `ReviewRequired` with exactly R current; an upstream Revision exists; the path is not empty.
  - Effects and state: identical to `SubmitManualResult` (F6).
  - `ManualResultEligibility.CanSubmit` is **unchanged**.
- `BuildProbe` gains probes built from the snapshot's own R and U, like SCRUM-11147. `TransitionMatrixTests` gains the rows.
- **The engine half never grants anything alone.** It cannot see requests. `ExecuteCoreAsync` refuses both commands (`PreconditionNotMet`, following the `AdjustTrimFromReview` guard at L498) unless it receives an internal `CorrectionContext`. Only the dedicated service entries (§6.2, §6.5) build that context, after checking the request row with the same `CorrectionRequestId` and bindings, and `CorrectionRequestEligibility`.
  - So `ExecuteAsync(new ImportCorrectedImage(…))` or `ExecuteAsync(new RequestColleagueCorrection(…))` is always refused, including on a legacy generic review-state handoff.
  - `SessionView` derives `CanAskColleague` and `CanImportCorrectedImage` from the `CorrectionHandoff` projection, never from the raw `AvailableCommands` probe.

### 6.2 Request: `ISessionService.RequestColleagueCorrectionAsync(SessionId, Guid requestId, RevisionId r, Sha256 rHash, string? note, string? operator, ct)`

One `SessionCompletionGate` acquisition. The core is invoked directly; there is no nested `ExecuteAsync`.

1. **Load the aggregate and resolve.**
   - If request `requestId` exists: when it is `READY` and the session is `HandedOff`, return the view (idempotent repeat after an uncertain commit). When it is `PREPARING`, resume at step 4. Otherwise refuse ("no longer available").
   - If another request is open:
     - if it is `READY` and eligible, return it (no second package);
     - if it is `PREPARING` for the same R, resume it under its own id;
     - otherwise it is obsolete and is superseded in step 3's commit.
2. **Eligibility.**
   - The session is Active, and background removal is in `ReviewRequired` with exactly (r, rHash) current.
   - U = `UpstreamResultOf(BackgroundRemoval)` and `R.SourceRevisionId == U.Id`.
   - R and U are valid and not retention-released. R is PNG. U is a raster with pixel dimensions equal to R's.
   - No valid Revision or PrintOutput descends from R. This is proven here, not assumed; by construction nothing downstream can start before background-removal approval.
   - The engine probe accepts the command.
   - Both R and U pass `RevisionIntegrityGuard.VerifyAsync`. A mismatch follows the existing `FileMutated` invalidation and refusal.
3. **Commit 1:** insert the row as `PREPARING` with id, bindings, folder, names, note and reason, and supersede an obsolete open row. Session and step are unchanged.
4. **File work (inside the gate, no person involved).** For each of the reference and the working copy:
   - copy the Revision file to `{final}.partial-{id8}`, flush, and hash the written bytes against the recorded hash;
   - `File.Move(partial, final, overwrite: false)`;
   - if `final` already exists with the expected hash, accept it (resume); with any other hash, stop without touching it.

   Then write `Instructions.txt` with `CreateNew`, skipped if it already exists. Delete only this request's leftover partials.
5. **Commit 2:** apply `RequestColleagueCorrection` through `ExecuteCoreAsync` with the `CorrectionContext` for this `PREPARING` row, whose files step 4 has just verified. This gives session `HandedOff`, `HandedOffAtUtc` and `HandOffReason = EffectiveReason`, with `LockChange = null` (F21). The row is set `READY` with `ReadyAtUtc`. Both happen in one transaction.
6. Only after commit 2 does the view report "handed off", with the files ready.

**Failure order.** Every failure before commit 2 leaves the session Active and R reviewable. The row stays `PREPARING`, its partial or final files remain in the owned folder, and a plain failure is returned: "The files for your colleague could not be prepared. Nothing was handed off. {reason}".

Source integrity is checked at two points:
- **Step 2, before commit 1:** a mismatch writes no row and follows the existing `FileMutated` invalidation.
- **Step 4, when a written copy does not hash to the recorded value:** the `PREPARING` row remains. The source Revision is then re-verified with `RevisionIntegrityGuard`. If the source itself changed, the existing `FileMutated` invalidation applies, and the row can never become `READY`, because R or U is then invalid. Otherwise the copy failure is reported and a retry resumes.

Covered causes:
- the source is missing or mutated;
- the folder is unavailable or access is denied;
- the path is too long [UNV: long-path support];
- the disk is full;
- a name collision with different bytes;
- commit 1 or commit 2 fails.

Pressing the button again resumes the same row. A crash anywhere is covered the same way: nothing else writes `PREPARING` rows, and they are inert.

### 6.3 Re-prepare: `RepairCorrectionFilesAsync(SessionId, Guid requestId, ct)`

Offered only for an eligible `READY` request when a recorded file is missing. It runs under one gate. It re-verifies R and U integrity and recreates **only missing** files with `CreateNew`. It changes no row except an optional `UpdatedAtUtc`.

### 6.4 Correction eligibility (application layer, like `ManualCropEligibility`) [PROP]

`CorrectionRequestEligibility.Resolve(snapshot, aggregate)` returns the open request and its import mode, or null. The request is `READY`, its session matches, and U is still `UpstreamResultOf(BackgroundRemoval)` with the same id and hash. Then:

| Mode | Session and step state | Import route under the gate |
|---|---|---|
| Review | `HandedOff`, background removal `ReviewRequired`, current = request R (id + hash) | `ImportCorrectedImage` |
| AfterUnfinishedImport | `HandedOff`, background removal `Failed` or `Interrupted`, latest background-removal attempt = `LastImportAttemptId` with status Failed, Interrupted or Cancelled | existing `SubmitManualResult` (already legal, F4), with the `CorrectionContext` so the row is updated |
| — | background removal `Processing` (import live, or its closing commit lost) | refused: "The last import is still finishing. If this persists, restart PrintFlow." (existing rule) |
| — | anything else, for example R rejected, a new result, retried or returned | obsolete; history only |

**[PROP] A correction-bound import never leaves the job un-handed-off.** Today only a failed import re-hands off (F8). A stopped or interrupted import leaves the session `Active`, where Run and Retry would restart background removal (F22).

A narrow rule applies wherever an attempt whose id equals an open `READY` request's `LastImportAttemptId` is closed. In that same closing commit, the engine `HandOff(step, request.EffectiveReason)` is applied with its `ReleaseAutomationLock` removed (F21; the `FailAttemptAsync` filter precedent). The seams:
- `FailAttemptAsync` (F8), which already re-hands off; it uses the request's reason instead of the generic text;
- `StopAttemptAsync` for `StopOperation`;
- `StartupRecoveryService`'s Running → Interrupted closing.

The session is therefore `HandedOff` after every unfinished correction import, AC4 holds, and Run stays refused. Because the re-handoff shares the attempt's closing commit, the `Active` + `Interrupted` combination cannot arise for a bound attempt. Generic manual-result imports keep today's behaviour.

Precise seams:
- **Identifying a bound attempt.**
  - In `FailAttemptAsync` and `StopAttemptAsync`, the bound request comes from `ProducingWork.CorrectionRequestId` (threaded in §6.5), which is passed to both.
  - These methods receive only the in-memory `afterStart` aggregate (`SessionService.cs` L1924–1929), which predates the opening commit's `LastImportAttemptId`. So the opening commit's request change is also applied to `afterStart`, as its attempt is at L1928, and the closing commit then updates a consistent row.
  - Startup recovery has no `ProducingWork`. It reloads the aggregate and reads the persisted `READY` row whose `LastImportAttemptId` equals the closing attempt's id.
- **`StopAttemptAsync`.** For a `ManualResultImport` attempt, the whole closing commit sets `LockChange = null`, mirroring `CompleteProducingStepAsync` L2143. An import never acquires the lock, and `AttemptCancelled`'s unconditional release would otherwise roll back the Stop close whenever another session or environment verification holds the row (F21).
- **Startup recovery.** It builds the session with `aggregate.Session with { … }`, not `MergeSession`, so it sets `State = HandedOff`, `HandOffReason = request.EffectiveReason` and `HandedOffAtUtc = now` explicitly in the same transaction. It already never applies engine lock effects.

Stop stays offered during an import: it is a legitimate cancellation, and it now lands in AfterUnfinishedImport.

`SessionView` gains `CorrectionHandoff`, carrying the names, folder, note, required size, status, mode and `CanImportCorrectedImage`, plus `CanAskColleague`. The existing `CanSubmitManualResult` keeps its service meaning.

While `CanImportCorrectedImage` is true, the Session screen changes three things:
- it **suppresses** the generic "Submit Manual Result" button, so there is one import action and the request is never orphaned;
- the status panel recommends Import corrected image instead of `ReenterAutomation` (F22);
- "Return to automation" moves under *Other options*.

### 6.5 Import: `ImportCorrectedImageAsync(SessionId, Guid requestId, RevisionId? r, Sha256? rHash, string selectedPath, string? operator, ct)`

1. **Outside the gate** (the screen has already closed the picker): run `IManualResultImporter.PreflightAsync(BackgroundRemoval, U facts, path, ct)`.
   - It is the importer's own validation (F9), refactored so the same routine checks the selected file read-only (held `FileShare.Read`, full decode). It writes nothing.
   - It returns the selected hash, length and dimensions.
   - A refusal is returned with the existing message keys (§7.3). Nothing is written; the handoff and R are untouched.
2. **Under one gate acquisition:**
   - reload and require `CorrectionRequestEligibility` with the same request id (and, in Review mode, the same R id and hash);
   - re-hash the selected file and require it to equal the preflight hash, else refuse: "The picture changed while it was being checked. Save it again, then choose Import corrected image.";
   - refuse a hash equal to `ReferenceSha256`: "This is the reference copy. Choose the corrected picture.";
   - run the route in §6.4 through `ExecuteCoreAsync` with the `CorrectionContext`.
   - `EnsureIntegrityAsync` gains `ImportCorrectedImage` → `UpstreamRevisionOf(BackgroundRemoval)`, as `SubmitManualResult` has (L1352), so U's bytes are re-verified before the attempt opens.
   - `ProducingWork` carries `CorrectionRequestId` and the preflight hash.
     - The **opening** commit sets `LastImportAttemptId`.
     - The **closing success** commit sets the row `RETURNED` with `ResultRevisionId = R2` and `ClosedAtUtc`, in the same transaction as the Revision and the attempt.
3. The importer then runs its unchanged checks on the managed copy. `ImportAsync` also gains an optional `expectedSelectedHash`: when the held source hashes differently (swapped after the re-hash), the attempt fails before copying. A late change or fault fails the attempt as today (F8), giving mode AfterUnfinishedImport: still handed off, still importable.

## 7. Return lifecycle

### 7.1 States and actions

"Writes" lists persisted rows and files. Picker, preview and folder dispatch are read-only unless stated.

| State | Visible actions | Writes | After failure or uncertainty |
|---|---|---|---|
| **S0** Background-removal review of R (Active, `ReviewRequired`) | Approve (exact), Reject (exact, §7.5), **Ask a colleague to correct this image**. Generic "Hand off manually" is hidden here (D4). | Approve or Reject: existing review, state and invalidation writes | — |
| **S1** Ask panel open (inline, not modal) | Optional note, **Prepare files for my colleague**, Cancel. Approve, Reject, Run and Return-to-step are disabled while it is open, following the SCRUM-11147 editor precedent. | none; Cancel writes nothing and re-enables them | — |
| **S2** Preparing (busy) | none; consequential actions disabled | Commit 1 (row `PREPARING`), files, Commit 2 (session `HandedOff`, row `READY`; no lock change) | Before Commit 2: back to S0 with a notice and **Try again**. Row `PREPARING`, R reviewable, owned files kept. Uncertain Commit 2: reload shows S3 or S0-with-retry; a repeat with the same intent id never duplicates. |
| **S3** Handed off, waiting for the colleague (mode Review or AfterUnfinishedImport) | Open folder, **Import corrected image** (the recommended command), Prepare files again (only if a file is missing). *Other options:* existing Reject in Review mode (special handed-off Reject, F5), or existing Return to automation in AfterUnfinishedImport. Abandon on Home. The generic Submit Manual Result is suppressed. | none (Open folder dispatches only) | Restart re-reads the row; the same panel returns. The session is `HandedOff` in both modes (§6.4 rule), so normal Run is refused. |
| **S4** Choosing or checking a return | Picker, then preflight (busy "Checking the picture…") | none | Picker cancel: S3, nothing written. Preflight refusal: S3 with a plain reason (§7.3). |
| **S5** Importing (session Active, step Processing, attempt Running) | Existing Stop only (F22); everything else busy | opening commit (attempt, step Processing, session Active, row `LastImportAttemptId`), managed copy | Late validation failure or fault: attempt Failed, step Failed. Stop: attempt Cancelled, step Interrupted. Crash: startup closes it Interrupted. In all three the same closing commit re-hands off with the request's reason (§6.4 rule), giving S3 in mode AfterUnfinishedImport, with the reason or "The last import did not finish". |
| **S6** Review of R2 (Active, `ReviewRequired`, R2 current) | Approve (exact), Reject, Ask a colleague (a new round with R2 as the working copy) | closing commit (R2 `ManualResultImport`, attempt Succeeded, row `RETURNED`) | Closing commit lost: attempt Running, then startup Interrupted, then S3. No R2 exists. |
| **S7** R2 approved | Trim is current (`Waiting`); **Start this step**. Background removal is not offered again. | existing Approve writes (review decision, `LatestApprovedRevisionId`) | — |
| **S8** Downstream processing | existing Trim, size and TIFF flows | existing | existing |
| Obsolete request (for example R rejected from S3) | history line with the folder path; no Import | — | Existing generic paths apply (for example `SubmitManualResult` after Reject, F19). |

### 7.2 What changes, when

| Event | Attempts | Revisions | Reviews | Session / step | Request |
|---|---|---|---|---|---|
| Prepare (S2) | — | — | — | HandedOff / unchanged `ReviewRequired`(R) | `PREPARING` → `READY` |
| Preflight refusal | — | — | — | unchanged | unchanged |
| Import open (S5) | + Running `ManualResultImport`, input U, source path | — | — | Active / Processing, current cleared | `LastImportAttemptId` |
| Import success | Succeeded | + R2 (`SourceRevisionId` U) | — | Active / `ReviewRequired`(R2) | `RETURNED`, `ResultRevisionId` |
| Late failure, Stop or crash during import | Failed / Cancelled / Interrupted | — | — | HandedOff (reason = request's `EffectiveReason`) / Failed or Interrupted | unchanged (`READY`); the note survives in the row |
| Approve R2 | — | R2 ReviewState Approved | + Approved | Active / background removal Approved, Trim current | — |
| Reject R2 | — | R2 Rejected; its descendants invalidated (none) | + Rejected | Active / `RetryRequired` | — |

R is never rejected, approved or invalidated by this feature. It stops being the step's offer when S5 opens, as with `SubmitManualResult` and `AdjustTrimFromReview`, and stays valid, unreviewed history. The no-invalidation choice rests on §6.2's explicit no-descendant check, re-checked at import.

### 7.3 Return validation: capable, transparent, acceptable

These are the actual limits (F9), unchanged and without new thresholds:
- PNG extension and PNG content, one frame;
- 1 B to 256 MiB, at most 100 MP;
- canvas **exactly** U's W × H;
- at least one non-opaque pixel and at least one non-transparent pixel;
- stable bytes.

Three separate claims must be distinguished:
- **Alpha-capable:** a PNG with an alpha channel. Not checked on its own.
- **Transparent content:** the pixel rule above. One semi-transparent pixel satisfies it, so it does not prove the background was removed.
- **Acceptable correction:** only the human review of R2 establishes this. Neither the folder, the file name nor the sidecar proves semantic correctness.

There is no resize, conversion, quality classifier or new threshold.

The mapping below is the importer's actual `Localise` behaviour (F9, L133–137):

| Refusal | Code → message key |
|---|---|
| wrong extension or format, malformed, not one frame, over limit | OutputValidationFailed → `Failure_ManualResultInvalid` |
| missing file | OutputMissing → `Failure_ManualResultInvalid` |
| locked or unreadable (IOException) | OutputUnreadable → `Failure_ManualResultInvalid` |
| access denied (UnauthorizedAccess) | WorkspaceError → `Failure_ManualResultImport` |
| cancelled | Cancelled → `Failure_ManualResultImport` |
| no transparency, or nothing visible | → `Failure_ManualResultTransparency` |
| wrong size | → `Failure_ManualResultCanvas`, plus the required W × H in the panel |
| reference copy chosen; changed during checking | new keys (§8.5) |

**[PROP, optional]** A preflight sharing violation could get its own plain message, "The picture is still open in another program. Close it, then try again." (`Session_CorrectionInUse`), instead of the generic invalid-file text.

### 7.4 Edited again during import

- The preflight hash is compared again under the gate.
- The importer holds the source with `FileShare.Read`, so writers are excluded during the copy (F9).
- The managed copy's hash is checked before and after decoding.
- The Revision records the managed copy's hash, and review binds to it. The reviewed bytes are therefore always the stored bytes, whatever happens to the colleague's file afterwards.
- The colleague's file is never modified.

### 7.5 Identity notes

- A return identical to R's bytes is allowed: the colleague may judge R fine. It becomes R2 with a new id, and the review shows the notice "This picture is identical to the one you sent."
- Because of this case, **both** background-removal review decisions become exact.
  - Approve switches to `ApproveExactReviewAsync(id + hash)`, as Trim did (F15).
  - Reject gets a sibling `RejectExactReviewAsync(id, step, revision, hash, reason, notes)` entry. It takes one gate acquisition, checks the id and hash, then runs the unchanged `Reject` core. It is used for background removal in S0 and S3 and in the review of R2.
  - Both buttons use the fresh-gesture `ReviewApprovalButton` bound to `revision id|hash`.
  - A stale screen of R therefore cannot approve or reject a same-hash R2.
  - The engine `Approve` and `Reject` commands are unchanged. Trim Reject's missing exact binding stays a separate recorded residual.
- Filename similarity never selects a job. The operator imports from inside the chosen job, bound to its request id.

## 8. Review, continuation and integration

### 8.1 AC5: "next step after background removal"

AC5 does not skip review. After import, the immediate action is to review R2, and the **next step is named while R2 is under review**. The status reads: "Check the corrected picture, then choose Approve or Reject. After you approve it, PrintFlow continues with {next step}. Background removal will not run again."

`{next step}` is the display name of the step after background removal in this workflow's definition, which is Trim on both background-removal routes (`WorkflowCatalog`). It is computed from the definition, not hard-coded.

`NextStepText` (`SessionViewModel.OperatorStatus.cs` L49) gains one branch ahead of the generic `Session_NextReview`. It applies when the current step is background removal in `ReviewRequired` and its current Revision equals the `ResultRevisionId` of a `RETURNED` request (from the projection).

After a lawful Approve, Trim (or, on routes without Trim, the next defined step) becomes current. The existing next-step sentence names it with "Start this step". Nothing auto-starts:
- import, refresh, approval and save issue no `StartStep`;
- the handed-off session refuses Run (F5);
- `ReenterAutomation` stays refused while `ReviewRequired`.

The only route back to automatic background removal is the existing explicit chain Reject → Retry/`ReenterAutomation` → Start. It is kept, but it is never the default.

### 8.2 Trim, final save, delivery

- **Trim.** After R2 is approved, Trim runs its ordinary deterministic trim on R2. The Trim review then offers "Adjust trim edges" when `TrimAdjustmentEligibility` holds: R2 is a valid raster, and the automatic Trim attempt recorded geometry (F20).
  - R2 itself is a background-removal Revision without trim geometry. It is never offered to the trim editor.
  - KeepOriginalExtent keeps its current behaviour and its open PNG approval gap. No backfill, grandfathering or auto-approval is introduced.
- **Final save.** Background removal is never a final-save point. Asset Trim and print-TIFF final reviews use `FinalSaveCoordinator` and the exact-identity entries unchanged.
  - Earlier delivery records and delivered files remain bound to their own identities (SCRUM-11144 resolver).
  - They are never proof that R2 or its descendants are approved or saved.
  - The correction folder is never a delivery destination.
- **Downstream invalidation.** No new rule. Existing `Reject` → `InvalidateDescendants` and `ReturnToStep` invalidation apply unchanged. The SCRUM-11147 no-descendant supersede rule is not copied: this feature proves its own prerequisite (§6.2) and re-checks it.

### 8.3 Home, Recent and Session projections

- **Session:** status "Handed off", plus the S3 panel.
- **Recent:** shows en "Handed off", but zh-CN "已转交", and offers "Details" (F16).
  - **[PROP]** Align zh-CN `SessionState_HandedOff` with the terminology reference's "已移交". This is a one-value resource change inside the permitted "handed-off status" scope.
  - **[PROP]** Add a second line, "Waiting for a colleague's corrected picture" (`Home_RecentWaitingForCorrection`), from `SessionListItem.HasOpenCorrection`. The row's open action still reads "Details" (existing label, recorded limitation), so this line is what tells the operator there is something to return to. It is part of slice 4, not optional.
- **Home recovery:** after the §6.4 re-handoff, a crash-interrupted correction import is listed by the existing rule as `HandedOff` + `Interrupted`. Its actions are Restart (= explicit `ReenterAutomation`), Import manual result and Abandon. A stopped (Cancelled) import is not listed.
  - The existing Home "Import manual result" goes through the generic `SubmitManualResult`, so it imports correctly but bypasses the request, which then shows as history. Nothing is lost; this is a recorded residual.
  - **[PROP, small]** Label that Home action "Import corrected image" when `HasOpenCorrection`, keeping its route.
  - Restart stays an explicit existing alternative.
- There is no folder watching, Photoshop observation or automatic import anywhere (AC7).

### 8.4 Ports

- **`IFilePicker.PickSingleFile(title, filter, initialFolder)` overload.**
  - `InitialDirectory` is set to the request folder when it exists.
  - The filter is `Session_ManualCutoutFilter`.
- **New `ICorrectionFolderShell.Open(absoluteFolder, selectFileOrNull)`.**
  - Implemented in Infrastructure with `SHOpenFolderAndSelectItems` on the working copy, or a plain folder open when that file is missing.
  - The path comes from `IWorkspace.ResolveAbsoluteDirectory` and is checked to exist first.
  - It reports dispatched, not visibly shown [UNV].
  - It shares the native helper, not the delivery lease or wording.

### 8.5 Beginner UI and copy [PROP]

**Placement.**
- The Ask action sits in the existing review action row (`SessionScreenView.xaml` L2040–2110), after Approve and Reject, on background-removal `ReviewRequired` only.
- The S1 and S3 panels sit in the right-hand details column (the `ScrollViewer` at L1112). They replace the review decision details while shown; they are conditional and compact, not a permanently expanded form.
- The preview keeps showing R in S3, labelled "Picture sent for correction".

**Interaction.**
- **Fresh intent.**
  - *Prepare files* and *Import corrected image* use the existing fresh-gesture `ReviewApprovalButton`. Their `TargetIdentity` is `requestId|R id|R hash`, or `sessionId|R id|R hash` for Ask.
  - There is no default Enter submission and no timing gate.
  - The note box never submits on Enter.
- **Target binding.** Each consequential call carries the identities captured at click time. A stale async result (preflight or import) whose request, session or R no longer matches the view is discarded with a reload.
  - A refresh, selection, language change or Open folder never creates a request, import, approval or run.
- **Busy.** While S2, S4 or S5 runs, all consequential session actions are disabled. Read-only Open folder stays available in S3.
- **Focus.**
  - Ask moves focus to the note box. Cancel returns it to the Ask button.
  - After a successful preparation, focus goes to the non-activating S3 status heading, as the Wave1A pattern does.
  - After the picker closes, focus returns to Import.
  - A refusal notice is announced with a live region and keeps focus on Import.
  - After a successful import, focus goes to the non-activating review status of R2.
- **Readable without colour.** Every state has a text label ("Handed off", "Checking the picture…", "Import refused: …"). The reference row carries the words "do not edit", not only colour.
- **Small viewports.** SCRUM-11147 reported that at 1000×700 the crop surface is about 50–80 px high [REC, not re-observed]. The S3 panel wraps, scrolls within the existing details column and adds no permanent rows elsewhere. Crop height, Tab order and Compare remain existing limitations for SCRUM-11154 or human evaluation; no layout rewrite is proposed.

**Candidate copy** (terminology reference style; final wording subject to the novice walkthrough):

| Key (new unless noted) | en | zh-CN |
|---|---|---|
| `Session_AskColleague` | Ask a colleague to correct this image | 请同事修正此图片 |
| `Session_AskColleagueHint` | Not sure this looks right? A colleague can correct it. | 不确定效果是否正确？可以请同事修正。 |
| `Session_CorrectionNoteLabel` | What to fix (optional) | 需要修正的地方（可选） |
| `Session_CorrectionPrepare` | Prepare files for my colleague | 为同事准备文件 |
| `Session_CorrectionPreparing` | Preparing files… | 正在准备文件… |
| `Session_CorrectionPrepareFailed` | The files for your colleague could not be prepared. Nothing was handed off. {0} | 无法为同事准备文件，未移交任何内容。{0} |
| `Session_CorrectionStatus` | Handed off — waiting for your colleague's corrected picture | 已移交 — 等待同事修正后的图片 |
| `Session_CorrectionStep1` | 1. Give your colleague these two files: | 1. 把这两个文件交给同事： |
| `Session_CorrectionReference` | Reference (do not edit): {0} | 参考（请勿修改）：{0} |
| `Session_CorrectionWorking` | Correct this file: {0} | 请修改此文件：{0} |
| `Session_CorrectionFolder` | Folder: {0} | 文件夹：{0} |
| `Session_CorrectionOpenFolder` | Open folder | 打开文件夹 |
| `Session_CorrectionStep2` | 2. What to fix: {0} | 2. 需要修正：{0} |
| `Session_CorrectionNoNote` | No note — check hair, edges and leftover background. | 无备注 — 请检查头发、边缘和残留背景。 |
| `Session_CorrectionStep3` | 3. Save the corrected picture as a PNG with a transparent background, {0} × {1} pixels, in the same folder (for example "{2}"). | 3. 将修正后的图片另存为透明背景的 PNG，尺寸 {0} × {1} 像素，保存在同一文件夹（例如“{2}”）。 |
| `Session_CorrectionStep4` | 4. Back in PrintFlow, open this job and choose Import corrected image. PrintFlow will not repeat background removal on the imported picture. | 4. 回到 PrintFlow，打开此任务并选择“导入修正后的图片”。PrintFlow 不会对导入的图片重新去除背景。 |
| `Session_CorrectionImport` | Import corrected image | 导入修正后的图片 |
| `Session_CorrectionPickerTitle` | Choose the corrected picture | 选择修正后的图片 |
| `Session_CorrectionChecking` | Checking the picture… | 正在检查图片… |
| `Session_CorrectionRefused` | Import refused: {0} The job is still handed off. | 已拒绝导入：{0} 此任务仍为已移交状态。 |
| `Session_CorrectionIsReference` | This is the reference copy. Choose the corrected picture. | 这是参考副本。请选择修正后的图片。 |
| `Session_CorrectionChanged` | The picture changed while it was being checked. Save it again, then choose Import corrected image. | 检查期间图片发生了变化。请重新保存后再选择“导入修正后的图片”。 |
| `Session_CorrectionMissingFiles` | Some files for your colleague are missing. | 部分给同事的文件已丢失。 |
| `Session_CorrectionRepair` | Prepare files again | 重新准备文件 |
| `Session_CorrectionLastImportFailed` | The last import did not finish. Choose Import corrected image again. | 上次导入未完成。请再次选择“导入修正后的图片”。 |
| `Session_CorrectionReview` | Check the corrected picture, then choose Approve or Reject. After you approve it, PrintFlow continues with {0}. Background removal will not run again. | 请检查修正后的图片，然后选择“通过”或“驳回”。通过后，PrintFlow 将继续进行“{0}”。不会重新去除背景。 |
| `Session_CorrectionIdentical` | This picture is identical to the one you sent. | 此图片与发出的图片完全相同。 |
| `Session_CorrectionOtherComputer` | If your colleague uses another computer, copy these files to them yourself. | 如果同事使用另一台电脑，请自行将这些文件复制给对方。 |
| `Session_CorrectionObsolete` | This correction request is no longer current. Its files remain in {0}. | 此修正请求已不再有效，其文件仍在 {0}。 |
| `Home_RecentWaitingForCorrection` | Waiting for a colleague's corrected picture | 等待同事修正后的图片 |
| `SessionState_HandedOff` (existing, zh-CN value only) | Handed off (unchanged) | 已转交 → **已移交** |
| `Session_CorrectionInUse` (optional) | The picture is still open in another program. Close it, then try again. | 图片仍在其他程序中打开。请关闭后重试。 |

The terminology reference's reserved "Ask a colleague to correct this image" row changes from "Future wording" to "Keep" at implementation.

## 9. Failure behaviour summary

| Case | Result |
|---|---|
| R or U mutated or missing at request time | Detected in step 2: existing FileMutated invalidation, refusal, no row; session Active. Detected during the copy (step 4): the row stays `PREPARING`, the source is re-verified, and FileMutated applies if the source changed (§6.2). |
| `ExecuteAsync` called directly with either new command | Refused: no `CorrectionContext` (§6.1) |
| Folder unavailable, access denied, disk full, path too long | `PREPARING` kept, S0 plus Try again; nothing handed off |
| Final-name collision with different bytes | Stop; that file untouched; plain reason; Try again after the operator moves it |
| Commit 1 or commit 2 fails, or crash between | Session Active; resumable; same intent id means no duplicate |
| Blind repeat after an uncertain handoff | Returns the existing `READY` request |
| R changed by another action before the request commits | Exact-id check refuses: "This result is no longer the one under review." |
| Picker cancel; preflight cancel | Nothing written |
| Unsupported, transparency-less, wrong-size or unreadable return | Refused with a plain reason; S3 kept; no attempt |
| Changed between preflight and import | Detected by the re-hash under the gate: refused before any commit. Swapped after that: `expectedSelectedHash` fails the attempt, re-handed off, mode AfterUnfinishedImport. |
| Stop pressed during import | Attempt Cancelled, re-handed off in the same commit, mode AfterUnfinishedImport |
| Crash or lost closing commit during import | Startup closes it Interrupted and re-hands off in the same commit; mode AfterUnfinishedImport |
| Repeat import after success | Request `RETURNED`, refused "already imported"; no second R2 |
| Package files deleted by someone | S3 shows missing files and Prepare files again (only missing files are recreated) |
| Request obsolete (R rejected, retried, returned elsewhere) | History only |

## 10. Mapping to the eight AC

| AC | Design | Seams | Smallest future evidence |
|---|---|---|---|
| 1 | Ask beside Approve/Reject on background-removal review; empty note gives the default reason (§6.1) | engine command, table row, probe; `ExecuteCoreAsync` context guard; VM `CanAskColleague`; XAML action row | Engine: legal only in background-removal `ReviewRequired` with exact ids; generic `HandOff` still refuses an empty reason. Service: `ExecuteAsync` with either new command is refused, including on a legacy generic review handoff. VM: Ask → Prepare with an empty note ends `HandedOff` with `DefaultReason` and a `READY` row; Approve/Reject are disabled while the panel is open; the action is visible with Approve/Reject and hidden elsewhere. |
| 2 | S3 panel: two files, folder, Open folder, note, format and size, save location, return (§5.2, §8.5) | `SessionView.CorrectionHandoff`; `ICorrectionFolderShell`; strings | VM panel-content tests en/zh-CN; Open folder dispatch to a fake shell with the resolved managed path; the reference row text includes "do not edit" |
| 3 | Copies only; ReadOnly hint; files never authority; no writes to Source/Revisions/Approved (§5.3) | `IWorkspace` copy method; service order | Integration: hashes of Source, InputSnapshot, all Revision and Approved files unchanged; copies are distinct files (no link); R and U hashes equal the copies |
| 4 | Session `HandedOff`; Run refused; re-handoff after an unfinished import; Recent "Handed off"/已移交 plus the waiting line (§6.4, §8.3) | existing rules; closing seams in `FailAttemptAsync`, `StopAttemptAsync` and startup recovery; `SessionListItem.HasOpenCorrection`; zh-CN value | Service: StartStep, ReenterAutomation (Review mode) and Approve refused in S3. After a failed, stopped or crash-recovered correction import, the session is `HandedOff` with the request's reason and StartStep is refused. A Stop close still commits while another session holds the lock row, and that row is unchanged. Recent row state and line in en/zh-CN. No automatic action on reload. |
| 5 | Import creates R2 in `ReviewRequired`; next step named during the review; exact approve and reject; Trim next (§7, §8.1) | `ImportCorrectedImage`, service import, `ProducingWork.CorrectionRequestId` | Integration: R2 new id, `ManualResultImport`, source U, one attempt, no Meitu call (the fake records zero calls), `RETURNED` row. VM: the R2 review text names Trim in en/zh-CN. After approve, the current step is Trim and no background-removal attempt is added. A same-hash R2 refuses a stale approve or reject aimed at R. |
| 6 | Preflight refusals; handed-off state and R kept (§7.3) | importer `PreflightAsync` refactor | Wrong extension, JPEG-as-PNG, opaque, empty-alpha, wrong canvas, malformed, locked, missing, reference chosen, changed: no attempt, no Revision, session `HandedOff`, R current |
| 7 | No watcher; import only by explicit command (§8.3) | none | Architecture: no `FileSystemWatcher` or new timer in the App/Workflow correction code; reload does not change rows |
| 8 | Wrapped, scrolled panel; keyboard order; text states (§8.5) | XAML, strings | Off-screen renders en/zh-CN at 1000×700 and 1920×1040 (inspected); resource parity; Tab-reachability via existing synthetic helpers; human check NOT RUN |

## 11. Future verification (NOT RUN)

- **Fixtures.**
  - Synthetic PNG/JPEG only, from `SyntheticImages`.
  - `SessionServiceHarness`: GUID temp workspace and database, temp lease, fake Meitu that counts calls.
  - `OperatorCultureScope` for language tests.
  - Real NTFS temp folders for the copy/collision/partial tests.
- **Excluded.** Unsafe `ApplicationStartup`, `Window.Show` and UIA paths stay excluded until separately authorized.
- **Order.** Targeted tests first:
  - engine and `TransitionMatrixTests` rows (impact reason: a new command kind and a new table entry);
  - eligibility;
  - request and import integration, including restart via a new harness service over the same database;
  - partial-copy/commit-failure seams using the existing failing-repository pattern;
  - importer preflight parity with the import checks;
  - VM and renders.
- **Must stay green.** `StopAndTakeOverTests`, `ManualResultImportTests` (including F19), `ManualResultEligibilityTests`, trim/final-save suites touched by the exact-approve change.
- **Candidate.** Rebuild after any temporary mutation and record the tested assembly hashes.
- **Not required.** A full suite per slice, timing KPIs, signing or retired acceptance checks.
- **Human/native checks, NOT RUN.** Real picker initial folder, Explorer selecting the working copy, physical keyboard and focus, supported-workstation bilingual layout, novice walkthrough with a real colleague round trip.

## 12. Proposed implementation slices

| Slice | Scope |
|---|---|
| 1. Persistence | migration 0019, row and mutation mapping, aggregate load; schema tests (only if D1 is approved) |
| 2. Workflow | two commands, table rows, probes, shared HandOff helper, `CorrectionRequestEligibility`, `SessionView` projection |
| 3. Files and service | `IWorkspace` verified copy; request/repair/import service entries and the `CorrectionContext` guard; `RejectExactReviewAsync`; importer `PreflightAsync` refactor and `expectedSelectedHash`; `ProducingWork` request threading; re-handoff in `FailAttemptAsync`, `StopAttemptAsync` and the startup-recovery closing for bound imports; `EnsureIntegrityAsync` mapping |
| 4. App | VM state machine (S0–S7); exact background-removal approve and reject; suppression of generic Submit/ReenterAutomation recommendation; picker overload; `ICorrectionFolderShell`; XAML panel; strings including the zh-CN `SessionState_HandedOff` value and the Recent line; terminology reference; hiding generic Hand off on background-removal review |
| 5. Validation | targeted tests, affected regressions, renders, one independent review |

## 13. Owner decisions and residuals

**Decisions.** A favourable review decides none of these.
- **D1** — Approve the additive `CorrectionRequest` table (migration 0019). Recommended. The fallback is in §4.2.
- **D2** — Keep the correction folder inside the managed session workspace. Recommended over a chosen or shared folder.
- **D3** — Accept "reference" as U, the exact input of background removal, which is the enhanced picture when Enhancement ran, rather than the uploaded original.
- **D4** — Hide the generic "Hand off manually" on background-removal review in favour of the new action. The engine legality of `HandOff` is unchanged. Legacy sessions already handed off from review keep today's Reject-then-submit route and are not converted.
- **D5** — Approve the narrow closing-seam rule (§6.4): a correction-bound import that fails, is stopped or is crash-recovered re-hands the session off, with the lock release removed. Without it, a stopped or interrupted correction import leaves the job `Active`, where Run would restart background removal. This changes existing Stop and startup-recovery outcomes, but only for attempts bound to an open request.
- **Not proposed:** a "withdraw request" action. Returning an unchanged working copy, or the existing Reject, covers it. The owner may add it later.

**Residuals.**
- Correction folders accumulate; there is no cleanup. This includes the `*.partial` temporaries of `PREPARING` rows that were later superseded.
- An import through Home recovery bypasses the request, which then shows as history.
- A late import failure, Stop or crash removes R from review (it remains history).
- The session's `HandOffReason` holds the latest effective reason; the note itself survives in the request row.
- The Recent open action still reads "Details" for handed-off jobs.
- The transparency rule is weak (§7.3).
- UNC paths pass the existing importer check [UNV].
- Long paths [UNV].
- Open folder dispatch is not proof of display.
- Existing items stay open: the 1000×700 height budget, the KeepOriginalExtent PNG gap, Trim Reject/KeepOriginalExtent exact binding, and the Wave1A/11145/11146/11147 human checks.

**Not run.** Builds, tests, application, picker, Explorer, desktop, migrations, Jira writes and CSV regeneration.
