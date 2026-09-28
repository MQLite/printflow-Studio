# SCRUM-11147 — trim adjustment from review: design

Task `PF-OPUX-v1-SCRUM-11147-design-v1`, 2026-09-25 NZ. Mode PLAN_ONLY. **PROPOSED / AWAITING_OWNER_REVIEW.** This document is a design. It does not authorize implementation. Independent review: [SCRUM-11147_TRIM_DESIGN_REVIEW.md](SCRUM-11147_TRIM_DESIGN_REVIEW.md).

## 0. Authority and evidence basis

- **Requirement.** SCRUM-11147 (issue 10877, Planning-ID `PF-OPUX-v1-trim-boundary-adjustment`), read with an authenticated Jira call on 2026-09-25. Status To Do. `updated` 2026-09-22T14:27:25.050+1200, no comments. Blocks SCRUM-11154 and SCRUM-11155. The description and the seven AC match the closeout snapshot `SCRUM-11145_CLOSEOUT_JIRA_READBACK.json`. Nothing was written to Jira.
- **Repository.** `D:/Repositories/printflow-Studio`, `master`, HEAD `eea60198c5095243f0694717a7479b00c3e1cd73`, with the accumulated uncommitted work preserved (53 status entries at start). Every source fact below comes from reading that working tree. Line numbers are omitted on purpose; the named members are the reference.
- **Evidence class.** Everything here is **static source inspection plus design**. No build, test, application launch, desktop input or capture was performed. No part of this document is runtime evidence.

## 1. Current-source map (facts)

| Area | Current fact | Where |
|---|---|---|
| Steps | Trim exists only in `PrepareAsset` (then `ApprovedPngExport`) and `PrepareCustomerDesign` (then `PrintDimensions`, `PhotoshopOutput`). `GeneratePrintTiff` has no Trim. Trim `RequiresReview: true`, `AdapterKind.Internal`. | `WorkflowCatalog` |
| Legality | `SubmitManualCrop` is allowed only in `Failed` and `RetryRequired`. `ReviewRequired` allows `Approve`, `KeepOriginalExtent`, `Reject`, `HandOff`. | `TransitionTable.AllowedByState` |
| Eligibility (history half) | Manual crop needs Trim current, session progressing, and either a `ManualCropRequired`/manual-crop failure (`Failed`) or a **rejected manual crop** (`RetryRequired`). A rejected *deterministic* trim is only an ordinary Retry case. | `ManualCropEligibility.IsEligible`, enforced in `SessionService.ExecuteCoreAsync` |
| Engine | `SubmitManualCrop` → Trim `Processing`, `AttemptCount+1`, effects `CreateWorkingCopy(Trim, upstream)`, `RecordAttemptStarted(ManualImport)`, `RunManualCrop(crop, margin)`. The input is `state.UpstreamRevisionOf(Trim)`. | `WorkflowEngine.SubmitManualCrop` |
| Pre-trim source | `UpstreamRevisionOf(Trim)` is the latest step result before Trim. Skipped steps fall through. While Trim is under review this is the approved Enhancement/Background-removal result, or the confirmed/prepared original. | `WorkflowSnapshot.UpstreamRevisionOf` / `UpstreamResultOf` |
| Lineage | A produced Revision's `SourceRevisionId` is `work.InputRevision`, the revision the attempt consumed. The attempt row's `InputRevisionId` is the same value. | `SessionService.CompleteProducingStepAsync`, `RunProducingStepWithinLeaseAsync` |
| Automatic geometry | The deterministic trim result is the pre-trim source cropped at `AppliedBounds` = alpha `ContentBounds` grown by the attempt's margin and clamped to the canvas. Both rectangles are persisted as `TrimGeometry` on the succeeded attempt. The margin is persisted too, as `TrimParameters`. | `DeterministicAlphaTrimProcessor`, `TrimResult.Produced`, `ProcessingAttempt.WithTrimGeometry` |
| Manual geometry | A manual crop persists `ManualCropGeometry(SelectedBounds, AppliedBounds, Margin)`. Applied = selected grown by the margin and clamped. With `ManualCropMargin.Tight`, applied equals selected. The processor refuses an empty or out-of-canvas base rectangle; only a margin may clamp. The service checks that the processor returned exactly the requested geometry. | `ManualCropGeometry.Create`, `WicManualCropProcessor`, `SessionService.PerformStepWorkAsync` |
| Coordinates | `CropSurfaceLayout` maps surface ↔ payload ↔ source. It covers fit or zoom (`DisplayScale`), preview reduction (`SourcePerPayload*`, preview max edge 2048) and letterboxing. Scrolling is absent by construction. `TryToSourceBounds` refuses out-of-canvas and zero-area rectangles and rounds outward with `EdgeTolerance = 1e-6`. There is no EXIF-orientation handling on either the preview or the crop path, so both use the stored pixel orientation. | `CropSurfaceLayout`, `WicImagePreviewDecoder` |
| Crop UI | Crop mode is transient VM state (`IsCropping`, `CropSelection` in source px, margin inputs). Drawing a new rectangle replaces the old one, and a refused drag **clears** the selection. There are no handles. Apply issues `SubmitManualCrop`; Cancel touches nothing. `Show()` clears crop state on **every** view refresh. | `SessionViewModel(.ManualCrop).cs`, `SessionScreenView.xaml(.cs)` |
| Crop pane | `CropPane = PreviewPanes[^1]`: in Failed/RetryRequired that is the step input. **During a trim review it would be the "After" pane, the already-cropped result.** | `SessionViewModel.CropPane`, `LoadPreviewsAsync` |
| Approval | The VM's `Approve` goes through `ExecuteAsync(Approve(step, hash))`, and the engine checks the **hash only**. `ApproveExactReviewAsync` checks revision **and** hash under one `SessionCompletionGate` acquisition; `FinalSaveCoordinator` uses it. | `WorkflowEngine.Approve`, `SessionService.ApproveExactReviewAsync` |
| Supersede precedent | `KeepOriginalExtent` from `ReviewRequired` supersedes the trim offer with no `ReviewDecision` and no fabricated rejection. | `WorkflowEngine.KeepOriginalExtent` |
| Invalidation | `Reject` emits `InvalidateDescendants(subject, Rejected)`, which walks `SourceRevisionId` children and dependent PrintOutputs, not the subject itself. `InvalidationReason.Superseded` ("a newer result replaced it at the same step") exists, is mapped to `SUPERSEDED` in SQLite and is currently unused by the engine. | `SessionService.ComputeDescendantInvalidations`, `RevisionEnums`, `Mappers` |
| Integrity | `EnsureIntegrityAsync` re-verifies the bytes for Approve, Reject, StartStep, KeepOriginalExtent and the sizing decisions. It does **not** do so for `SubmitManualCrop`, and working-copy creation copies without re-hashing. | `SessionService.EnsureIntegrityAsync`, `FileWorkspace.CreateWorkingCopyAsync` |
| Gate | `ExecuteAsync` holds `SessionCompletionGate` for load → engine → opening commit → in-process work → closing commit. Nothing acquires the gate from the view model. | `SessionService.ExecuteAsync` |
| Final save | Asset route: while Trim is reviewable, the `png` target is *pending* on the exact displayed revision+hash. `SetPending` drops facts learned about another identity and keeps the name/folder draft. `ConfirmAndSaveIdentity` includes the review identity. PNG delivery requires a human `ReviewDecision` on the promoted source and valid ancestors, otherwise `ApprovalEvidenceMissing`. | `SessionViewModel.FinalSave.cs`, `ApprovedArtifactResolver` |
| Input guard | `ReviewApprovalButton` activates only on a fresh, non-repeat press whose `TargetIdentity` still matches on release. It resets on a target change. `FocusNewReview` moves non-activating focus to `OperatorStatusPanel` when `ReviewTargetIdentity` changes. | `ReviewApprovalButton`, `SessionScreenView.xaml.cs` |
| Recovery | A crash during a producing attempt is closed as `Interrupted` by existing startup recovery. From `Interrupted`, the allowed commands are Retry, StartStep, Skip, KeepOriginalExtent and HandOff (not a manual crop). | `TransitionTable`, `StartupRecoveryService` |

## 2. Settled requirements versus proposals

**Settled (Jira + the owner's prompt):**
- The seven AC.
- Adjustment starts from the trim review without a separate Reject.
- Edge and corner handles, and reuse of `CropSurfaceLayout`.
- Zoom plus a before/after preview.
- Exact "Restore automatic suggestion".
- An explicit "Use this trim" that goes through the existing manual-crop path and produces a new Revision that is still reviewed.
- No rerun of approved upstream work; existing invalidation rules preserved.
- No rotation, perspective, masks, content recovery, route changes, new Generate Print TIFF trim, attempt-level cancel, KeepOriginalExtent repair, 11148 re-import or delivery P3s.

**Proposed here:** everything in §3–§12. The engine command name, read-model shape, copy and layout are proposals for owner review.

## 3. The source problem, resolved

The trim under review (R1) is the pre-trim source (U) cropped at R1's applied bounds. Editing R1 itself would make any outward drag impossible: the pixels the first crop discarded are not in R1. Repeating that would compound losses. **The editor therefore always displays, maps and crops U, never R1.**

U is identified by three independent sources, and all three must agree before the editor is offered:
1. R1's `SourceRevisionId`;
2. the `InputRevisionId` of the succeeded Trim attempt whose `OutputRevisionId` is R1;
3. `snapshot.UpstreamResultOf(Trim)` (id + SHA-256), which is what the existing manual-crop engine path crops.

U and R1 must also still be valid: not invalidated, and U not retention-released. Dimensions come from U's recorded `Facts` and must equal the loaded preview's `SourcePixelWidth/Height`.

Exposing U's retained pixels outside R1's bounds is **not** inventing content. They are real pixels of an approved revision that the first crop left out. Nothing beyond U's canvas can be selected; that stays refused, exactly as today.

When U cannot be used, the editor shows a truthful unavailable message, "Use this trim" is disabled and the review is unchanged. There is no fallback to R1, to another revision or to a guessed file. This covers: the preview fails, the dimensions disagree, the format is PSD/PDF, retention has been released, or the identity rules above do not hold. A mutated U file is caught at commit by the integrity check (§7.3).

## 4. Recommended beginner interaction

1. The trim review looks as it does today. One new ordinary button, **"Adjust trim edges"**, appears in the review action bar when adjustment is eligible (§7.2). It is not the recommended default action, and it never approves anything.
2. Pressing it opens the existing crop surface in *adjust mode*, over U, fitted:
   - the current boundary (R1's applied bounds) is drawn with eight handles;
   - the area outside the boundary is dimmed.
3. The operator drags an edge handle (moves that edge) or a corner handle (moves both adjacent edges). Zoom in/out/fit are the existing buttons, and the scrollbars work as today. Keyboard users Tab to a handle and use the arrow keys (§9.3).
4. **"Compare"** switches the surface to the existing two-pane layout:
   - "Current result (waiting for review)": R1's existing After pane;
   - "Proposed trim (not used yet)": U's preview payload cropped for display only.

   The two panes share one zoom. **"Adjust"** switches back. Nothing is written.
5. **"Restore automatic suggestion"** puts the boundary back exactly on the automatic trim's applied bounds (§6.2). When there is no genuine suggestion it is disabled, and a line says so.
6. **"Use this trim"** submits once (§7). The editor closes. The screen then shows the new result R2 **waiting for review**, with the ordinary Approve / Reject / "Confirm result and save" of that review bound to R2. "Use this trim" neither approves nor saves anything.
7. **"Cancel adjustment"** discards the draft. The review is exactly as before.

Alternatives considered: redrawing a rectangle (the current MVP, which the issue rejects as the beginner difficulty), and a separate editor window or navigation (excluded by the prompt). The chosen design reuses the one crop surface and adds handles plus dimming.

## 5. Coordinates, handles and the numeric example

### 5.1 Mapping and gesture rules

- **The draft is only ever a source-pixel `TrimBounds`.** It is re-projected with `CropSurfaceLayout.TryToSurfaceRect` whenever zoom, fit, resize or the pane changes, as the outline is today.
- **Pointer handle drag.** A new pure helper next to `CropSurfaceLayout`, working with a `CropHandle` enum (Left, Top, Right, Bottom, and the four corners), computes the new rectangle:
  1. Project the pre-gesture bounds to surface coordinates.
  2. Move each moved edge by the **pointer's displacement since the press**: its pre-gesture surface position plus (current pointer − press pointer), per axis. This is not the absolute pointer position, because a handle is grabbed up to 10 DIP inside its edge (§9.1) and using the absolute position would jump the edge by the grab offset. A movement below the system drag threshold (`SystemParameters.MinimumHorizontal/VerticalDragDistance`) is not a drag: pressing and releasing a handle without moving leaves the bounds exactly unchanged.
  3. Pass the four surface edges to the **existing** `TryToSourceBounds`, which does the refusal and outward rounding.
  4. Copy the unmoved edges from the pre-gesture bounds, so projection noise can never shift an edge the operator did not touch. `EdgeTolerance` already makes the round trip exact; the copy makes it structural.

  No crop maths is rewritten, and `CropSurfaceLayout` itself is unchanged.
- **Crossing.** A moved edge that reaches or passes its opposite edge on the surface is refused for that pointer position. For a corner, if either axis is invalid (crossing, outside or zero), the whole position is refused and both edges keep their pre-gesture values. A corner is never half-applied. This is a gesture rule; it adds no domain validation. The rectangle never silently flips.
- **Outside and zero area.** These are refused by the existing `TryToSourceBounds`: any edge beyond the canvas (tolerance 1e-6), or `right <= left`. Only the refusal behaviour changes, and only for handle gestures: while the pointer is at an invalid position the outline shows the **pre-gesture boundary** and the notice. A release there keeps the pre-gesture boundary. The existing draw-new-rectangle path in the Failed/RetryRequired fallback is unchanged, including its "outline goes away" behaviour.
- **Reaching the picture border exactly.** Overshoot is refused, never clamped (AC5). At fit zoom the border column is only about 0.26 DIP wide, so a pointer rarely lands exactly on it. The design says so plainly and offers three exact routes:
  - zoom in, where the border is many DIP wide;
  - Ctrl+arrow on a focused handle, which moves that edge to the picture border in that direction (a source-space move to 0 or width/height, validated like any nudge);
  - Restore, when the automatic suggestion touches the border.

  The invalid notice mentions zoom and Ctrl+arrow.
- **One-pixel minimum.** Outward rounding can legitimately yield a 1-px-wide selection, which today's rules accept. This is not changed.
- **Interruption.** `LostMouseCapture` (alt-tab, dialog) or Esc during a drag restores the pre-gesture boundary. A drag never writes the draft until release, as today.
- **Keyboard nudge.** An arrow key moves the focused handle's edge or edges by 1 source pixel; Shift moves 10; Ctrl moves to the picture border. The result is validated in source space by the same rules: inside the canvas, non-empty, no crossing. A refused nudge keeps the boundary and shows the notice. The arrow-key events are marked handled, so the surrounding `ScrollViewer` does not scroll.
- **Margin.** In adjust mode the margin is fixed at `ManualCropMargin.Tight` and the margin controls are hidden, so the outlined rectangle *is* the kept area. The fallback path keeps its margin controls unchanged.

### 5.2 Worked example (design arithmetic, not executed evidence)

Pre-trim source U is 4000 × 3000 px. The preview payload is 2048 × 1536, so there are 1.953125 source px per payload px. The crop surface is 1024 × 800 DIP, fitted: `DisplayScale = min(1024/2048, 800/1536) = 0.5`, the drawn image is 1024 × 768, `LetterboxX = 0` and `LetterboxY = 16`. One DIP is 3.90625 source px.

- **Automatic trim.** `ContentBounds [1200,800 → 2800,2200)`, margin Uniform 20, so `AppliedBounds [1180,780 → 2820,2220)` (1640 × 1440). R1 is a 1640 × 1440 PNG.
- **Initial outline.** R1's applied bounds project to surface x 302.08–721.92 and y 215.68–584.32.
- **Expansion (left handle).** The operator grabs the left handle and moves the pointer 12.08 DIP left, so the edge's surface position goes from 302.08 to 290.0 wherever inside the handle the grab happened. Source x = 290 / 0.5 × 1.953125 = 1132.8125; floor gives 1132. The other edges are copied, giving `[1132,780 → 2820,2220)` (1688 × 1440). Its 48 extra columns are real U pixels that R1 does not contain; cropping R1 could never produce them.
- **Reduction (bottom handle).** The bottom edge is moved 24.32 DIP up, from surface y 584.32 to 560.0. Source y = (560 − 16) / 0.5 × 1.953125 = 2125.0; ceil(2125 − 1e-6) gives 2125. Result `[1132,780 → 2820,2125)` (1688 × 1345). At fit zoom, one DIP is about 3.9 source px. For single-pixel precision the operator zooms in (at 800%, one DIP is about 0.24 source px) or uses the arrow keys.
- **Invalid.** Moving the left edge beyond the canvas (its surface x < 0, so source < −1e-6) is refused. So is moving it to x ≥ 721.92 (crossing). In both cases the outline stays at `[1132,780 → 2820,2125)` and the notice is shown.
- **Exact restoration.** The selection becomes the stored attempt value `[1180,780 → 2820,2220)`, with the margin Tight. There is no recomputation from `ContentBounds` plus today's `TrimMargin`, so the 20-px margin is not applied twice and the result is not the full canvas.
- **Submission.** "Use this trim" with `[1132,780 → 2820,2125)` crops U, never R1. R2 is 1688 × 1345, with `ManualCropGeometry(selected = applied = [1132,780 → 2820,2125), Tight)` and `SourceRevisionId = U`.

## 6. Transient draft, preview and restoration

### 6.1 Draft identity and data

This is VM-only state, never persisted, and there is no editing journal. It reuses `IsCropping`/`CropSelection` and adds a mode flag plus the draft's identity:
- `TrimAdjustmentView` from the read model (§7.2): session, reviewed revision R1 + SHA-256, source U + SHA-256, U's pixel size, `CurrentBounds` (R1's applied bounds in U pixels) and `AutomaticSuggestion` (bounds + producing attempt id, or null);
- the current valid draft `TrimBounds` in U pixels;
- the transient gesture state (the handle and the pre-gesture bounds) in view code.

The **draft identity** is `session | Trim | R1 | hash(R1) | U | hash(U)`.

### 6.2 Where the automatic suggestion comes from

It is the `TrimGeometry.AppliedBounds` of the **most recent succeeded deterministic Trim attempt** (`OperationKind.Trim`, geometry present) whose `InputRevisionId` is U. This is the rectangle the automatic trim actually cropped at, and it already includes that attempt's margin. It is read from the attempt row, never recomputed, and never taken from today's settings or the session's `TrimMargin`.
- If the displayed R1 is itself a manual crop (a second adjustment), the suggestion still comes from that automatic attempt on U, when one exists.
- If no such attempt exists — the `ManualCropRequired` fallback, a legacy result without geometry, or an upstream that has changed — the suggestion is null. Restore is then disabled with "There is no automatic suggestion for this picture." Nothing is invented.
- `NoChangeRequired` (applied = full canvas) is a genuine suggestion and is restored as the full canvas.
- The existing Failed/RetryRequired manual-crop fallback is left **unchanged**: no handles, no Restore, and the margin controls work as today. That keeps its selected/applied semantics and margin behaviour untouched. After a failed adjustment the operator uses that fallback (draw), or Retry, which re-runs the automatic trim and then allows adjustment from the new review.

### 6.3 What the transient actions do and do not do

Opening the editor, dragging, nudging, zooming, comparing, restoring and cancelling create **no** attempt, review, revision, invalidation, file or print-dimension change. They issue no service call. The preview is display-only and comes from the U payload already decoded by the existing preview seam, so there is no second decode and no file. Its label says it is proposed.

Leaving the screen, pressing Back or cancelling discards the draft. Discarding a UI draft is not attempt cancellation. Once "Use this trim" has started the persisted attempt, the existing processing behaviour applies. No new Cancel command is designed.

### 6.4 Refresh and late events

`Show()` currently clears crop state on every refresh. Proposed: in adjust mode, `Show()` keeps the draft when the new view's `TrimAdjustmentView` has the **same draft identity**; otherwise it discards the draft and closes the editor.
- Same identity: language change, a refused command followed by a refresh, or a plain reload.
- Different identity: R1 or U changed, the step moved, or adjustment is no longer offered.

A null `TrimAdjustmentView` counts as a different identity. That is what happens after an integrity refusal invalidates U, because eligibility then fails (§7.2). The editor closes and shows `Session_TrimAdjustSourceUnavailable` rather than failing again on every press.

When the draft is kept, `Show()` also keeps the zoom and fit (it skips `ResetZoom`). Scroll position may reset when the previews reload; the draft is unaffected. Previews reload under the existing generation token. The draft is in source pixels and re-projects when the pane arrives. A late preview for an older generation is ignored as today. Selection or save-target changes in the final-save section do not touch the draft.

## 7. Persistent transition

### 7.1 Options compared

| Option | Verdict |
|---|---|
| A. A wrapper: `Reject` R1, then `SubmitManualCrop` | Rejected. It writes a fabricated rejection `ReviewDecision` with a reason the operator never gave, marks R1 `Rejected`, and uses two commits with an observable intermediate `RetryRequired` state. `ManualCropEligibility` also refuses a manual crop after a rejected deterministic trim, so the wrapper would need a bypass. |
| B. Add `SubmitManualCrop` to the `ReviewRequired` row, with optional reviewed-identity fields | Workable, but it overloads one command with two legality regimes, and its availability probe would change `CanManualCrop`, which gates the margin controls. |
| **C. New exact-target command `AdjustTrimFromReview`, legal only in `ReviewRequired`, emitting the existing manual-crop effects** | **Recommended.** One transition and one opening commit. It carries the exact R1 and U identities. Processing, the attempt row, geometry, failure, recovery and review are all the existing manual-crop machinery. The existing `Failed`/`RetryRequired` `SubmitManualCrop` rows are untouched. |

### 7.2 Recommended design (option C)

- **Command.** `WorkflowCommand.AdjustTrimFromReview(RevisionId ReviewedRevision, Sha256 ReviewedHash, RevisionId SourceRevision, Sha256 SourceHash, TrimBounds Crop)`, with a matching `CommandKind`.
- **Transition table.** `CommandKind.AdjustTrimFromReview` is added to the `ReviewRequired` row only, with `Destination` = `Processing`. The exhaustiveness tests gain the row.
- **Engine** `AdjustTrimFromReview`:
  1. `Resolve(state, Trim, …)` covers the progressing session, Trim as the current step and the table row.
  2. `step.CurrentRevisionId == ReviewedRevision` **and** `step.CurrentRevisionSha256 == ReviewedHash`. A matching hash with a different revision id is refused.
  3. `UpstreamResultOf(Trim) == (SourceRevision, SourceHash)`.
  4. `Crop` is non-empty.
  5. New step: `Processing`, `CurrentRevisionId/Sha256 = null`, `AttemptCount + 1`. `HasDerivedRevision` is recomputed as `Reject` does. Taking R1 off the step pointer is the whole supersede, exactly as `KeepOriginalExtent` does from `ReviewRequired`.
  6. Effects: **only** the three existing manual-crop effects, `CreateWorkingCopy(Trim, U)`, `RecordAttemptStarted(ManualImport, U, retrySequence)` and `RunManualCrop(Trim, U, Crop, ManualCropMargin.Tight)`.
     - There is no `RecordReview`: R1 receives no approval or rejection.
     - There is no new effect type and no `BuildMetadataMutation` change.
     - R1 is **not** invalidated. `Superseded` ("a newer result replaced it") would be false if the crop then failed, was interrupted, or its closing commit failed.
     - There is no descendant walk, because none is needed: eligibility requires that R1 has no descendant revision and no PrintOutput (below). A situation that would need invalidation is refused rather than silently left valid.
- **Probe.** `AvailableCommands` probes the command with the snapshot's own current R1 and U identities and the existing `ProbeCrop`, so the offered button and the accepted command share one rule.
- **History-half eligibility**, in the workflow layer next to `ManualCropEligibility`. `TrimAdjustmentEligibility` returns a `TrimAdjustmentView` or null. It is non-null only when all of these hold:
  - the session progresses, Trim is current and in `ReviewRequired`, with a current R1;
  - R1 was produced by a succeeded Trim-step attempt whose operation is `Trim` (with `TrimGeometry`) or `ManualImport` (with `ManualCropGeometry`);
  - that attempt's `InputRevisionId`, R1's `SourceRevisionId` and `UpstreamResultOf(Trim)` all name U;
  - U and R1 are valid, and U is not retention-released;
  - no Revision has `SourceRevisionId == R1`, and no PrintOutput is sourced from R1 (true by construction while R1 is under review; checked so the rule cannot be bypassed);
  - U's format is a raster that the crop processor accepts.

  `ManualResultImport` (colleague) results and geometry-less legacy results are **not** eligible; their existing Reject/Retry/manual paths remain. `ExecuteCoreAsync` refuses the command unless eligibility holds, as it does for `SubmitManualCrop`. `SessionView` exposes `TrimAdjustment` and `CanAdjustTrim`. `CanManualCrop` keeps its current meaning.
- **Integrity.** `EnsureIntegrityAsync` gains `AdjustTrimFromReview → U`. A mutated U is refused with the existing `FileMutated` handling before anything is committed. (The existing `SubmitManualCrop` path is deliberately left unchanged.)
- **Gate.** The VM calls the existing `ExecuteAsync`. One `SessionCompletionGate` acquisition covers: fresh load, eligibility, integrity, the engine's exact checks, the opening commit, the in-process crop and the closing commit. No gate is held while a person drags or reviews, and nothing nests.
- **Audit.** R1 stays an immutable, valid Revision with its attempt and no review decision. It is no longer the step's current result, which is the same persisted shape `KeepOriginalExtent` leaves. The new attempt's `RetryOfAttemptId` links it to R1's producing attempt. After a successful crop, R2's attempt (with `ManualCropGeometry`) records the replacement. R2 carries `ManualCropGeometry` and `SourceRevisionId = U`. The Enhancement and Background-removal steps, their revisions and their attempts are untouched, and no adapter is invoked.
- **Same bounds.** Submitting bounds equal to R1's is still a new attempt and a new Revision R2 that needs review (AC3). This is not optimized away. The editor shows a one-line note when the draft equals the current boundary. Bytes may even hash-match R1; R2 is still a distinct revision.

### 7.3 Transition and action table

| State | Allowed operator actions | Persistent writes | Identity in → out | Visible message | Safe next action |
|---|---|---|---|---|---|
| Trim review (R1, `ReviewRequired`), editor closed | Approve, Reject, Keep original extent, Hand off, Confirm result and save (asset), **Adjust trim edges** | Only on Approve/Reject/Keep/HandOff/Confirm, as today | ReviewTarget = R1 | Existing review status | Approve or adjust |
| Adjust mode, draft open | Drag or nudge handles, zoom, Adjust/Compare, Restore, Use this trim, Cancel adjustment, Back | **None** | Draft identity R1+U; the draft is in U px | Route-specific instruction; "Kept area…"; invalid notice when refused | Use this trim or Cancel |
| Restore / Cancel | — | None | Restore: draft := automatic applied bounds, margin Tight. Cancel: draft discarded, editor closed | — | Continue editing, or review R1 |
| Submitting ("Use this trim" pressed) | None (busy) | One opening commit: Trim `Processing` with no current result, plus the attempt row. Then the crop and the closing commit | In: R1, hash, U, hash, crop | "Making the new trim…" | Wait |
| Refused before commit (stale R1/U, not eligible, integrity) | The editor stays only if the refreshed identity is unchanged and still eligible; otherwise it closes (an integrity refusal invalidates U, so it closes with the source-unavailable line) | None, except the existing `FileMutated` invalidation of U | Nothing changed, or the refreshed identity | Existing localized failure plus code | Re-check the screen; review whatever is shown |
| New trim waiting (R2, `ReviewRequired`) | The same review actions, now bound to R2; Adjust again | Existing `AttemptSucceeded` closing commit (R2 with geometry) | ReviewTarget = R2; asset pending save = R2 | Existing review status, plus "The new trim is ready. Check it, then approve it." | Review R2 |
| Crop failed (`Failed`, latest attempt `ManualImport`) | Existing: Manual Crop (the unchanged draw fallback), Retry, Keep original extent, Hand off. Trim is not skippable | Existing `AttemptFailed` commit | No current result; U remains the input; R1 stays valid history with no review | Existing failure notice | Manual crop again, or Retry the automatic trim and adjust from its review |
| Outcome uncertain: crash mid-attempt, or a closing commit that failed and left the step `Processing` | Nothing until the next startup recovery closes the attempt as `Interrupted`; then the existing Retry, Keep original extent, Hand off | Existing recovery only | No current result | Existing failure/interrupted notices | Retry the automatic trim, then adjust again if needed |
| Existing `Failed`/`RetryRequired` manual-crop entry | Unchanged `SubmitManualCrop`: draw, optional margins, no handles, no Restore | Unchanged | Unchanged | Unchanged | Unchanged |

Not added: adjustment from `Approved`, completed, handed-off, historical or TIFF-review states. Changing an approved trim still uses the existing lawful Return-to-step first.

## 8. Approval, final save, focus and invalidation

- **While the editor is open:**
  - These actions are **hidden**, and their VM `Can*` values are false while `IsCropping`: Approve, Reject, Keep original extent, Hand off, Return-to-step, Run, Skip, "Adjust trim edges" itself, and in the final-save section "Confirm result and save", Save, Retry, Check again and Save another copy.
  - The final-save section shows one line instead: "Finish or cancel the trim adjustment before approving or saving."
  - Open containing folder for an already delivered copy stays available (read-only).

  Hiding is presentation only. The commit-time authorities remain:
  - `ApproveExactReviewAsync` (revision + hash), used by Confirm result and save;
  - the engine's exact R1/U check for `AdjustTrimFromReview`.
- **Plain Approve on a Trim review.** It is proposed to route through the existing `ApproveExactReviewAsync` with the displayed R1 id and hash. Reason: a same-bounds R2 can have R1's hash, and the plain `Approve` command binds the hash only. This is the narrow hardening that makes "refuse stale revision ids even when hashes match" hold for the trim review. Other steps are unchanged.
- **During submission,** `IsBusy` disables everything, as today. The processing gate is inside the service.
- **On success,** `Show(R2)`:
  - the editor closes and the previews reload for R2 (Before = U, After = R2);
  - `ReviewTargetIdentity` becomes R2, so `FocusNewReview` places non-activating focus on `OperatorStatusPanel`;
  - every `ReviewApprovalButton` resets its gesture target;
  - the final-save `png` target moves to pending R2 through `SetPending`: facts about R1 are forgotten, and the file-name/folder draft is kept;
  - `ConfirmAndSaveIdentity` changes.

  A held or repeating Enter, a double-click remainder or a stale release from "Use this trim" therefore cannot activate Approve or Confirm on R2. "Use this trim" is itself a `ReviewApprovalButton` bound to the draft identity, so a key or click that began before the editor opened cannot submit.
- **Cancel and focus.** Cancel adjustment does not change `ReviewTargetIdentity`, so `FocusNewReview` does not run. The view therefore moves keyboard focus explicitly to "Adjust trim edges", or to `OperatorStatusPanel` when that button is no longer offered, so focus never stays on a collapsed control.
- **Known residual (pre-existing, not widened into a new authority).** Trim Reject binds the hash only, and Keep original extent binds no revision. Neither is a fresh-gesture button. The in-app screen shows R2 before any further action (`IsBusy` covers the transition), and a mistaken Reject of R2 is recoverable (`RetryRequired` after a manual crop allows another manual crop). Hardening those two actions is outside this design and is recorded for owner awareness only.
- **Invalidation and descendants.** While R1 is under review it has no descendants and no PrintOutputs: the downstream steps are Waiting, and any earlier outputs were already invalidated by the Return-to-step that reopened Trim. Eligibility checks this (§7.2). The transition therefore performs **no** invalidation, exactly like the existing `KeepOriginalExtent` supersede.
  - Earlier approved and delivered PNG/TIFF copies, their delivery records and external files are untouched. They are never deleted, overwritten or relabelled.
  - A prior delivery cannot count as saving R2: delivery keys are bound to other promoted revisions or outputs, and R2 has none until it is approved and, for assets, promoted once.
  - Sibling TIFF sizes follow the existing lineage rules, unchanged.
- **Upstream reuse.** U and all upstream revisions keep their state, approvals and validity. R2's ancestor chain (U and above) remains valid, which is what the PNG resolver's `ValidAncestors` check requires. No Enhancement, Background-removal or Meitu work runs.

## 9. Layout, wording and keyboard

### 9.1 Layout (inside the current Session screen)

The layout reuses the existing crop `DockPanel` row, which replaces the review panes while `IsCropping`.
- **Header row:** the heading, the Adjust/Compare toggle, and the existing zoom out/in/fit with its zoom label.
- **Surface:** the existing `ScrollViewer` → `Image` → `CropOverlay` stack, bound to the **U pane** rather than `PreviewPanes[^1]`. `CropOverlay` keeps `ClipToBounds` and its current size, so `CropSurfaceLayout` is unchanged. The canvas carries:
  - four dimming rectangles outside the draft;
  - the outline as a dashed dark stroke over a solid light stroke, drawn **inside** the boundary (inset by the stroke width);
  - eight 10 × 10 DIP square handles (white fill, dark border) as `Thumb`s with `Focusable="True"` set explicitly, with edge and corner resize cursors.

  Each handle is anchored to its edge or corner from the **inside** of the kept rectangle, so it stays whole and visible when the boundary lies on the picture border. That happens with a full-canvas suggestion, a `NoChangeRequired` restore, or at fit zoom, where one axis has no letterbox. The handle's hit area is its drawn square. A pointer drag uses the pointer's displacement measured on `CropOverlay` (`Mouse.GetPosition(CropOverlay)` at the press and at each move), not the handle's own position or the absolute pointer position (§5.1).

On a small selection (under about 20 DIP on an axis), inside-anchored handles overlap. The press goes to the handle whose centre is nearest the pointer, with corners winning ties. The "Kept area" line and the instruction suggest zooming in when the selection is that small, and keyboard nudges on any handle still work.
- **Compare view:** the existing two-pane review template with the headings "Current result (waiting for review)" and "Proposed trim (not used yet)". The proposed pane is U's payload cropped by a view converter to the draft mapped into payload pixels (rounded outward). The two panes share one zoom.
- **Bottom stack (wrapping text):**
  - the instruction;
  - "Kept area: [l,t → r,b) · w × h px";
  - "Automatic suggestion: …" or its unavailable line;
  - the same-bounds note;
  - the invalid notice;
  - the buttons **Use this trim**, **Restore automatic suggestion** and **Cancel adjustment**.
- **Right column:** unchanged review metadata, trim bounds block and final-save section, with its actions gated per §8.

The design must render unclipped in en and zh-CN at 1000 × 700 and 1920 × 1040 at 96 DPI. That is checked off-screen first; the supported workstation is a human check.

### 9.2 Proposed copy (candidate; owner and novice review pending)

| Key (proposed) | en | zh-CN |
|---|---|---|
| `Session_TrimAdjustBegin` / `_Heading` | Adjust trim edges | 调整裁切边缘 |
| `Session_TrimAdjustInstructions` | Drag the edges or corners to change what is kept. Trimming only changes the picture's edges. | 拖动边线或角点，调整要保留的范围。裁切只改变图片边缘。 |
| `Session_TrimAdjustPrintSizeLater` (TIFF route only; appended) | The print size is set in a later step. | 打印尺寸在后面的步骤中设置。 |
| `Session_TrimAdjustKeyboard` | Keyboard: Tab to a handle, then use the arrow keys (Shift moves 10 pixels; Ctrl moves to the picture's border). | 键盘操作：按 Tab 选中控制点，再用方向键移动（按住 Shift 每次移动 10 像素；按住 Ctrl 移到图片边界）。 |
| `Session_TrimAdjustUse` | Use this trim | 使用此裁切 |
| `Session_TrimAdjustRestore` | Restore automatic suggestion | 恢复自动建议 |
| `Session_TrimAdjustNoSuggestion` | There is no automatic suggestion for this picture. | 这张图片没有自动建议的裁切范围。 |
| `Session_TrimAdjustCancel` | Cancel adjustment | 取消调整 |
| `Session_TrimAdjustAdjustView` / `_CompareView` | Adjust / Compare | 调整 / 对比 |
| `Session_TrimAdjustCurrent` | Current result (waiting for review) | 当前结果（等待审核） |
| `Session_TrimAdjustProposed` | Proposed trim (not used yet) | 建议的裁切（尚未使用） |
| `Session_TrimAdjustKept` / `_Suggestion` | Kept area: {0} / Automatic suggestion: {0} | 保留范围：{0} / 自动建议：{0} |
| `Session_TrimAdjustInvalid` | That boundary can't be used: it must stay inside the picture and keep at least one pixel. The previous boundary is kept. To place an edge exactly on the picture's border, zoom in or press Ctrl+arrow on its handle. | 无法使用该边界：边界必须在图片范围内，且至少保留一个像素。已保留之前的边界。如需让边线正好落在图片边界上，请放大图片，或选中控制点后按 Ctrl+方向键。 |
| `Session_TrimAdjustUnchanged` | The boundary is unchanged. Using it still makes a new result that needs review. | 边界没有变化。使用后仍会生成一个需要审核的新结果。 |
| `Session_TrimAdjustBusy` | Making the new trim… | 正在生成新的裁切… |
| `Session_TrimAdjustReady` | The new trim is ready. Check it, then approve it. | 新的裁切已生成。请检查后再通过。 |
| `Session_TrimAdjustSourceUnavailable` | The picture this trim was made from can't be opened, so the edges can't be adjusted here. The current result is unchanged. | 无法打开生成此裁切的原图，因此无法在这里调整边缘。当前结果没有变化。 |
| `FinalSave_TrimAdjustOpen` | Finish or cancel the trim adjustment before approving or saving. | 请先完成或取消裁切调整，再通过或保存。 |
| `Session_NextTrimAdjust` (status) | Drag the edges or corners, then choose {Use this trim}. | 拖动边线或角点，然后选择“{使用此裁切}”。 |

- **Asset route (AC6):** the instruction without the print-size sentence. No adjust-mode string mentions print size.
- **TIFF route (`PrepareCustomerDesign`):** the instruction plus `Session_TrimAdjustPrintSizeLater`.
- The terminology reference gains rows for "Adjust trim edges", "Use this trim" and "Restore automatic suggestion", aligned with "Trim image edges / 裁切图片边缘". The existing "Trim" row's "future SCRUM-11147" note is updated at implementation time.

### 9.3 Keyboard and focus (existing capabilities only)

- Every button is an ordinary focusable control. The eight handles are focusable `Thumb`s with `AutomationProperties.Name` (for example "Left edge", "Top-left corner") and follow the zoom controls in Tab order.
- Arrow keys, Shift+arrow and Ctrl+arrow move edges (§5.1), and the events are marked handled. Esc during a pointer drag restores the pre-gesture boundary; Esc otherwise does nothing destructive.
- When the editor opens, focus lands on the heading/instruction region, not on "Use this trim".
- After a successful "Use this trim", the existing `FocusNewReview` rules apply, because the target changes. After Cancel, focus returns to "Adjust trim edges" (§8).
- The boundary is readable without colour: handle shapes, a dashed-over-solid outline, dimming (luminance) and the "Kept area" numbers as text.
- This uses standard WPF focus and `Thumb`; it is not a new accessibility framework.

## 10. KeepOriginalExtent and `ApprovalEvidenceMissing`

- The new path creates an ordinary reviewed trim Revision (R2). Its approval is the ordinary human `ReviewDecision`, so its PNG promotion and delivery follow the normal reviewed path. There is **no dependency** on the KeepOriginalExtent repair.
- `KeepOriginalExtent` stays available on the trim review, as today, and is hidden only while the editor is open. Its delivery behaviour is unchanged:
  - where the retained upstream has no human review (for example, a confirmed original with Enhancement and Background removal skipped), the PNG is refused with `ApprovalEvidenceMissing`;
  - where the upstream was itself human-approved, it passes as today.

  This design does not fabricate a review, does not grandfather an unreviewed PNG, and is not offered as the fix for that gap.
- An operator may deliberately adjust to the full canvas. That is a real, reviewed crop of U, and lawful. The UI does not suggest it as a workaround, and the PNG gap remains a separate owner item.

## 11. Acceptance-criteria mapping

| AC | Design clauses | Minimal future evidence |
|---|---|---|
| 1 Handles update the outline and source bounds through the existing mapping; nothing is committed before Use this trim | §4, §5.1, §6.3, §9.1 | Unit: the handle helper over fitted, magnified, letterboxed and reduced-payload layouts, each edge/corner, unmoved edges exact; a press-and-release without movement (and a sub-threshold movement) leaves the bounds unchanged; the grab offset inside the handle does not move the edge; overlapping handles on a tiny selection resolve to the nearest centre, corners first. VM: opening, dragging, nudging, zooming, comparing and restoring issue no service call (fake `ISessionService` call log) and leave the DB unchanged. |
| 2 Restore is exact | §6.2, §5.2 | Integration (temp DB): the suggestion equals the stored `TrimGeometry.AppliedBounds` for margined, tight and `NoChangeRequired` cases; no double margin; a changed `TrimMargin` has no effect; null for a ManualCropRequired-only history. VM: restore sets the draft exactly with margin Tight. |
| 3 New Revision through the manual-crop path, awaiting review, no implicit approval | §7.2, §7.3, §8 | Engine unit plus integration: `ReviewRequired` → `Processing` → `ReviewRequired(R2)`; `ManualImport` attempt; no `ReviewDecision` on R1 or R2; R1 valid, unreviewed and no longer current, including after a failed crop; same-bounds submission still yields R2. Stale R1 id with an equal hash is refused. Plain Trim Approve goes through the exact entry. |
| 4 Upstream reused; invalidation per existing rules | §7.2, §8 | Integration: Enhancement/Background revisions, states and attempt counts unchanged; fake Meitu call count zero; R2 source = U; no invalidation rows written; eligibility refuses an R1 that has a descendant; delivered records/files untouched (existing delivery fixtures). |
| 5 Invalid outside/zero selections refused; the prior boundary stays | §5.1 | Unit: outside, zero-area and crossing positions return false; a corner with one invalid axis is refused whole; nudge refusals; Ctrl+arrow reaches exactly 0/width/height. VM/view: a refused gesture keeps the pre-gesture draft. Engine: empty crop refused; the processor still refuses out-of-canvas input (existing tests). |
| 6 Asset copy has no print size; TIFF copy says print size later | §9.2 | VM tests on both routes for the instruction strings; resource-parity test. |
| 7 en/zh-CN, keyboard reachable, not clipped, visible without colour | §9 | Off-screen renders (en/zh-CN, 1000 × 700 and 1920 × 1040, 96 DPI) in adjust and compare views, including a boundary lying on the picture border (handles whole); Tab-order/`AutomationProperties`/`Focusable` inspection; human supported-workstation check. |

## 12. Future validation plan (NOT RUN now)

- **Unit/geometry:** the new handle/nudge helper (reusing `CropSurfaceLayoutTests` layouts); `TransitionMatrixTests` exhaustiveness for the new row; engine tests modelled on `KeepOriginalExtentTests`; eligibility cases (automatic, manual, colleague import, legacy geometry-less, changed upstream, handed-off, not current).
- **Temporary DB/filesystem integration:** extend `ManualCropStepTests` / `TrimStepTests` / `ManualCropGeometryPersistenceTests` via `SessionServiceHarness`:
  - direct submission from review, and the unchanged Failed/RetryRequired paths;
  - the failure boundary: processor refusal leads to `Failed` with the manual fallback eligible;
  - an opening-commit failure leaves nothing persisted;
  - an interrupted attempt is closed by existing recovery;
  - a mutated U is refused;
  - stale revision ids;
  - no upstream rerun.

  Extend `FinalSaveCoordinatorTests` with a pending target moving from R1 to R2 and a stale Confirm on R1 being refused.
- **Off-screen WPF (VM + view, no `Window.Show`):**
  - extend `ManualCropUiTests` / `FinalSaveUiTests` / `ViewRenderingTests`: the editor binds to the U pane, not After; consequential actions are hidden and false while editing; the draft and zoom survive a same-identity `Show()`; the draft is discarded on an identity change, and after an integrity refusal the editor closes; the Use-this-trim identity; focus/target reset after R2; focus after Cancel; the fallback manual crop is unchanged;
  - AC6 copy;
  - renders at the two sizes and in both languages.
- **Not in automated scope:** `ManualCropAdjustmentUiTests`, `KeepOriginalExtentUiTests` and the other closeout-excluded classes (`Window.Show`/UIA/`ApplicationStartup`) stay excluded until their isolation is fixed under separate authorization. Before running *any* path, inspect its full isolation, not only its fake adapters.
- **Native physical input, supported-workstation visuals and novice validation:** human checks with synthetic images. Held or repeating keys, double-click remainder, drag outside the window, bilingual layout at 1920 × 1080 / 96 DPI, and a beginner completing an expansion and a reduction.
- **Carried lessons:**
  - use the shared `OperatorCultureScope` rather than excluding the language classes;
  - keep the migration inventory (no schema change or migration is expected) and the centralized native declarations (no new P/Invoke is expected);
  - red/green or mutation evidence must name a rebuilt candidate: a timestamp-preserving restore can leave a stale DLL, so use a bounded explicit rebuild;
  - targeted suites per slice, plus one settled-source affected run, not a full suite per slice;
  - no waived timing targets, retired MVP supplemental acceptance or signing gates.

## 13. Implementation slices (for a future authorized task)

1. **Workflow:**
   - the command, the table row and `Destination`;
   - the engine transition, which reuses the existing manual-crop effects and adds no new effect;
   - `TrimAdjustmentEligibility` and `SessionView.TrimAdjustment`/`CanAdjustTrim`;
   - the `ExecuteCoreAsync` eligibility check and `EnsureIntegrityAsync` case;
   - Trim Approve through the exact entry.

   Unit and integration tests.
2. **App geometry:** the pure handle, nudge and border-snap helper. Unit tests.
3. **App VM:**
   - adjust mode and draft identity;
   - the U-pane binding;
   - restore, compare data, "Use this trim" via `ExecuteAsync`;
   - consequential-action gating and final-save gating;
   - same-identity draft retention in `Show()`;
   - status and recommended command.

   Off-screen VM tests.
4. **View and resources:** Thumbs, dimming, the compare converter, en/zh-CN strings and terminology rows. Off-screen renders.
5. **Validation and one independent code review;** then report. Jira remains read-only unless separately authorized.

Presentation parts must not ship ahead of slice 1: the editor must never be wired to `SubmitManualCrop` from a review.

## 14. Owner decisions

None blocks the design. The owner is asked only to approve or revise it as a whole. The notable choices are recorded as recommendations, not open questions:
- the new command rather than a Reject wrapper (§7.1);
- R1 taken off the step without a review or an invalidation, as KeepOriginalExtent does;
- plain Trim Approve routed through the exact entry;
- keyboard nudging and border snap through focusable handles;
- same-bounds submission allowed with a note;
- the fallback manual crop left unchanged.

The final copy wording remains subject to the SCRUM-11155 novice walkthrough. For owner awareness only, a pre-existing residual stays outside this design: Trim Reject and Keep original extent do not bind the exact revision and have no fresh-gesture guard (§8).

## 15. Routing and limitations

- Policy: approved Claude adaptation v1.2 of policy v2.4, `route_offset: 0` (direct task instruction). NormalRoute = RequestedRoute = ExecutionTarget = Opus/High. Actual host model `claude-opus-5-5`, effort UNVERIFIED. AdjustmentResult UNCHANGED. There was no Sonnet unit, since no mechanical unit qualified.
- Not run: builds, tests, application, desktop, Jira writes, CSV export, migrations. All future evidence above is **NOT RUN**.
