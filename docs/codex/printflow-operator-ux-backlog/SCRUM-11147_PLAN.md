# SCRUM-11147 — bounded implementation plan

Task `PF-OPUX-v1-SCRUM-11147-impl-v1`, 2026-09-25 NZ. Executes `printflow-remediation-prompts/PrintFlowStudio_SCRUM11147_Trim_Implementation_Prompt_v1.md`.

## Authorization

- **What the owner approved.** By submitting that prompt, the owner approved the final reviewed design [SCRUM-11147_TRIM_DESIGN.md](SCRUM-11147_TRIM_DESIGN.md) and authorized SCRUM-11147 implementation only.
  - Local bytes match the approved SHA-256 `be4d74916012936e991e5a98eb5479b649dbb7e70e4eab3ef5ffaa5454b69cb7` exactly.
  - The design and its [review](SCRUM-11147_TRIM_DESIGN_REVIEW.md) are preserved unchanged as history, including their "PROPOSED" wording.
  - All five §13 slices are authorized.
- **Jira.** One factual comment on SCRUM-11147 with marker `PF-OPUX-v1-SCRUM-11147-impl-v1`, then an authenticated initiative readback and export. Nothing else in Jira.
- **Not authorized:**
  - PNG approval repair;
  - SCRUM-11148;
  - Reject/KeepOriginalExtent hardening;
  - attempt cancellation;
  - delivery or schema changes;
  - production data/startup, shared-desktop input or capture;
  - Git mutation, deployment.

## Baseline

- `D:/Repositories/printflow-Studio`, `master`, HEAD `eea60198c5095243f0694717a7479b00c3e1cd73`, 53 dirty/untracked status entries preserved.
- `artifacts/pf-opux-scrum11147/`:
  - `baseline-all-files.sha256`: 848 tracked and unignored files, 2026-09-25T15:53:57+12:00;
  - `baseline-git-status.txt`;
  - `pretask/`: byte copies of every existing file this task touches, hashes in `pretask.sha256`.
- **Jira pre-read** (authenticated, 2026-09-25): SCRUM-11147 (id 10877) is To Do, updated 2026-09-22T14:27:25.050+1200, with 0 comments. AC unchanged from the design read.

## Route

- Claude adaptation v1.2 of policy v2.4, `route_offset: 0` (direct instruction).
- NormalRoute = RequestedRoute = ExecutionTarget = Opus/High for all substantive work. There is no Sonnet unit: no work was mechanical enough.
- ActualRoute: host model `claude-opus-5-5`, effort UNVERIFIED.
- Context `CONTINUE`. One fresh read-only reviewer at the review boundary (`FRESH_REQUIRED`).

## Source map and slices (design §13)

| Slice | Files | Content |
|---|---|---|
| 1 Workflow | `WorkflowCommand.cs`, `TransitionTable.cs`, `WorkflowEngine.cs`, new `Services/TrimAdjustmentEligibility.cs`, `SessionView.cs`, `SessionService.cs` | See the slice-1 list below |
| 2 Geometry | new `App/ViewModels/CropHandleGesture.cs` | Pure handle drag by displacement over the existing `TryToSourceBounds`, with unmoved edges copied. Crossing and corner all-or-nothing refusal; source-space nudge and border snap; inside-anchored handle centres; nearest-centre hit test with corners winning ties. |
| 3 VM | new `SessionViewModel.TrimAdjust.cs`; `SessionViewModel.cs`, `.FinalSave.cs`, `.OperatorStatus.cs`; `ArtefactPreviewPane.cs` (revision identity) | See the slice-3 list below |
| 4 View | `SessionScreenView.xaml(.cs)`, new proposed-crop converter, `Strings*.resx/.cs`, terminology reference | Inside-anchored `Thumb` handles, inset outline, dimming, pointer displacement drag with threshold/lost-capture/Esc, keyboard nudge, compare view, adjust-mode panel, en/zh-CN strings |
| 5 Validation | tests (below) | Targeted, then affected regressions on the settled candidate; one independent review; Jira |

**Slice 1 — workflow:**
- the `AdjustTrimFromReview(R1 id+hash, U id+hash, crop)` command;
- a `ReviewRequired`-only row with destination `Processing`;
- the engine exact checks, emitting only the three existing manual-crop effects with Tight margin;
- a probe with the snapshot's own identities;
- eligibility/projection with the three-way U agreement, validity, retention, raster format, geometry, dimensions and no descendants;
- the stored automatic suggestion;
- the `SessionView.TrimAdjustment` / `CanAdjustTrim` projection;
- in `ExecuteCoreAsync`, eligibility plus a crop-fits-U check, and integrity on U.

**Slice 3 — view model:**
- adjust mode and draft identity; the U pane;
- restore, compare rectangle, Use this trim, Cancel with a focus request;
- same-identity `Show()` retention with zoom; closure on identity loss (source-unavailable notice);
- consequential-action suppression and final-save gating (Open and Check saved stay read-only);
- plain Trim Approve through `ApproveExactReviewAsync`;
- status text and recommended styling.

## Source facts confirmed against the design

- `CommandKind` is not persisted, so adding a value needs no schema change.
- `TrimBoundaryTests.A_crop_rectangle_reaches_the_workflow_layer_only_as_a_command` pins the exact set of bounds-carrying commands. It is updated to name exactly the two commands; the property it protects (a rectangle leaves the shell only inside a command) is unchanged.
- **Compare deviation, disclosed.** U's preview payload may be display-reduced while R1's is not, so a shared numeric zoom would show the two at different scales. Compare therefore shows both fitted side by side, and zoom applies to the adjust surface. The design intent (display-only side-by-side of the same exact source) is kept.
- **Service pre-check, disclosed.** The service refuses a crop that does not fit U's recorded pixel size *before* any commit. A refused rectangle then never turns a reviewable R1 into a failed attempt. The processor refusal remains as the second guard.

## AC mapping and tests

Design §11 is the mapping. Future evidence is design §12, adapted:

| AC | Tests |
|---|---|
| 1, 5 | `Unit/Ui/CropHandleGestureTests`: layouts, the §5.2 example, moved/unmoved edges, no grab jump, crossing/corner/outside/zero, nudge/snap/one-pixel, hit test. VM tests: draft actions make no service call. |
| 2 | Integration (`TrimAdjustmentStepTests`): margined, full-canvas, after a setting change, repeated adjustment, absent suggestion. Unit (`TrimAdjustmentEligibilityTests`): lineage mismatch, invalid/released U, format, descendants. |
| 3, 4 | Integration: ReviewRequired → Processing → ReviewRequired(R2), one attempt, U crop and geometry, no reviews or invalidations, upstream attempts unchanged, same-bounds R2, same-hash stale ids refused, mutated U, crop failure, opening-commit failure, interrupted outcome. Engine unit tests. `TransitionMatrixTests` row. |
| 3 (save) | `TrimAdjustmentUiTests` plus final save: plain Trim Approve exact; stale Confirm on R1 after same-hash R2; pending target moves; old delivery facts only under their identity. |
| 6, 7 | Off-screen VM/render tests: en/zh-CN, 1000×700 and 1920×1040, adjust/compare, border handles, small selection, wrapping; resource parity. |

**Isolation.** Only `SessionServiceHarness`/`HomeScreenHarness` (GUID temp workspace/DB, temp lease DB, unverified gate, fake Meitu/Photoshop, real internal crop), off-screen `WpfRendering`, and the closeout exclusion filter. No `ApplicationStartup`, `Window.Show`/UIA, desktop, Explorer or picker.

**Build.** Per-user .NET 10 SDK by full path. After any temporary mutation or restore, an explicit bounded rebuild with the tested assembly hashes recorded.

## Stop

Stop at SCRUM-11147. No commit, push, deploy, SCRUM-11148, PNG repair or Jira field change.

## Execution record (added at completion)

- **Executed** as planned. Deviations from the approved design are recorded with reasons in [RESULTS.md](../../../artifacts/pf-opux-scrum11147/RESULTS.md) §1:
  - Compare shows both panes fitted;
  - the Adjust/Compare toggle is in the bottom row;
  - zh-CN uses 印刷尺寸 (terminology reference);
  - the handle Tab order is not re-scoped;
  - the service pre-checks that the crop fits U;
  - projection members are renamed `Result*`/`PreTrim*` (architecture token rule).
- **Validation, review and Jira:** RESULTS §2–§6 and [independent-review.md](../../../artifacts/pf-opux-scrum11147/independent-review.md).
  - Review: round 1 OPEN (one P2, fixed); recheck CLOSED.
  - Jira: comment 10171; readback 2026-09-27T21:29:59.658Z with oracle PASS.
- **Route used:** Opus High throughout. Host model `claude-opus-5-5`; effort UNVERIFIED. No Sonnet unit.
