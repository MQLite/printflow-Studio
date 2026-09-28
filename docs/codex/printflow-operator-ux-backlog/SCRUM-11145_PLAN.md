# SCRUM-11145 final save UI and delivery integration plan

Task `PF-OPUX-v1-SCRUM-11145-ui-v1`; 2026-09-24 NZ. Executes the owner-submitted `printflow-remediation-prompts/PrintFlowStudio_SCRUM11145_Final_Save_UI_Implementation_Prompt_v1.md`. Authorizes SCRUM-11145 only. The accepted SCRUM-11144 backend (SQLite delivery/attempt journal with DeliveryRequestAlias, atomic destination preference, native NTFS no-replace publication, R1–R4 corrections, local fixed/removable drive-letter NTFS envelope, fail-closed `ApprovalEvidenceMissing` for unreviewed PNG roots) is consumed as implemented and is not redesigned.

## Baseline, route and boundaries

- Checkout `D:/Repositories/printflow-Studio`, `master`, HEAD `eea60198c5095243f0694717a7479b00c3e1cd73`; 40 dirty/untracked entries of accumulated uncommitted work preserved. Pre-task hashes of 824 tracked/unignored files and a 757-file source/doc copy are in `artifacts/pf-opux-scrum11145/baseline-*` and `pretask/`; the scoped diff is computed against that copy, not HEAD.
- Jira read 2026-09-24 NZ: SCRUM-11145 = issue 10875, Planning-ID `PF-OPUX-v1-completion-delivery-summary`, To Do, updated `2026-09-22T15:00:19.787+1200`, zero comments, blocked by SCRUM-11144 (10874).
- Executor: Claude Code (VS Code extension), approved local Claude adaptation v1.2 of Codex policy v2.4, `route_offset: 0`. Prompt's Codex routes (Astra 6.0 High for UI, Sol 6.0 High for backend orchestration) map to **Opus High** under the standing Claude profile. NormalRoute = RequestedRoute = ExecutionTarget = Opus/High for all substantive units; ActualRoute runtime-reported model `claude-opus-5-5`, effort metadata UNVERIFIED. AdjustmentResult UNCHANGED; no Sonnet split (no qualifying mechanical unit), no xhigh. Context CONTINUE for implementation/test/fix; the single final safety review is FRESH_REQUIRED in a fresh read-only `personal-dev-reviewer` subagent (Opus High) with requirements/diff/evidence only.
- No production App launch, production DB/migration, customer files, picker/Explorer on the shared desktop, OS input, dependency changes, Git mutation, deployment, Jira field/status changes. No PNG approval repair, 11147 trim transitions, 11148 re-import. No new migration.

## Actual seams (source-resolved)

| Concern | Existing fact | 11145 use |
|---|---|---|
| Approval | `ISessionService.ExecuteAsync(Approve(step, hash))` takes `SessionCompletionGate` once and calls `ExecuteCoreAsync`; engine checks hash only. TIFF approval promotes into Approved inside the same command. | New `ApproveExactReviewAsync(session, step, revision, hash)`: acquires the gate once, reloads, requires `CurrentRevisionId == revision` **and** hash, then calls the same `ExecuteCoreAsync(Approve)`. Default interface member keeps other implementers compiling. |
| PNG promotion | PrepareAsset Trim approval leaves `ApprovedPngExport` waiting; `StartStep(ApprovedPngExport)` runs the internal byte-preserving promotion under the gate. | New `PromoteReviewedPngAsync(session, reviewedRevision, hash)`: under the gate, requires Trim Approved on that exact revision/hash; returns the existing promotion if `ApprovedPngExport` already holds a promoted revision of that source (no second promotion); otherwise runs `ExecuteCoreAsync(StartStep(ApprovedPngExport))` once. No enhancement/background/trim/Photoshop path. |
| Delivery | `IApprovedArtifactDeliveryService` GetOffer/Deliver/Reconcile/Check/ReplaceMissing/AcquireDeliveredSelection/GetDeliveryState; each entry takes the session gate itself; offer version required by Deliver/ReplaceMissing. | Consumed unchanged. Additive read-only members: last-successful destination read (from the existing `LAST_SUCCESSFUL_DELIVERY_DESTINATION` row) and a pre-approval draft check (leaf + folder support/protected-root). |
| App ports | `IFilePicker`, `IDiagnosticPackageDestinationPicker`; no shell port. | New `IDeliveryFolderPicker` (WPF `OpenFolderDialog`) and `IDeliveredFileShell` (`SHParseDisplayName` + `SHOpenFolderAndSelectItems`, no command strings). |
| Input guard | `ReviewApprovalButton` fresh-gesture/target identity; `ReviewTargetIdentity`; non-activating initial focus on `OperatorStatusPanel`. | Combined and approved-only save buttons reuse `ReviewApprovalButton`; identity = review/save target + draft generation. Focus logic unchanged. |

Lock audit: coordinator never holds `SessionCompletionGate`; each backend call acquires it once (approve-exact, promote, deliver, reconcile, check, replace, lease). Picker and UI waits occur outside every gate. `RequestStop` untouched. Cancellation token reaches only delivery prepublication; approval/promotion use `CancellationToken.None` once started, and uncertain approval is resolved by authoritative reload.

## Coordinator sequence (`PrintFlow.Workflow/Delivery/FinalSaveCoordinator.cs`)

1. Capture `FinalSaveRequest(OperationId, RequestId, ReviewedResultIdentity? | ArtifactKey?, Folder, FileName, DraftGeneration)`.
2. Draft check before anything else; invalid → `ApprovalOutcome.NotAttempted`, zero approval/export.
3. Pending review → `ApproveExactReviewAsync`. Failure → reload authority: exact approved ⇒ Succeeded; still pending/other ⇒ Refused; unreadable ⇒ Unknown. Only Succeeded continues. Observation `Approved` published only after known commit.
4. PNG → `PromoteReviewedPngAsync` once; failure ⇒ `PngPreparation.Failed` (“Approved; PNG preparation did not finish”, existing Run/Retry). TIFF → artifact is the reviewed PrintOutput id.
5. Re-resolve through `GetOfferAsync`; require offer hash == displayed hash and PNG `SourceRevisionId` == reviewed revision.
6. `DeliverAsync` with fresh offer version. Outcome returned separately from approval.
Approved-only save/Retry: steps 5–6 only (same RequestId for the same captured intent). Recorded history: Retry/Check-again → `ReconcileAsync(DeliveryId)`; Check → `CheckDeliveredFileAsync`; Save another copy → Check then `ReplaceMissingAsync` (only on Missing); Open → `AcquireDeliveredSelectionAsync`, shell dispatch, dispose in `finally`.

## State/action mapping (UI)

| State | Text source | Actions |
|---|---|---|
| Final review, valid draft | awaiting confirmation + draft summary | Confirm result and save (fresh gesture); existing Approve/Reject remain |
| No folder | “Choose a folder before saving.” | Change location; combined disabled; Approve-only still lawful |
| Confirming / preparing PNG / saving / finishing | phase text with captured target | Cancel only in Validating/Copying/Verifying |
| Approved, not saved | “Approved, but not saved to your folder yet.” | Save approved result |
| Verified | “Saved: {name} in {folder}.” + full path (read-only TextBox) | Open containing folder |
| Prepublication fail/cancel | “Approved, but not saved: {reason}. Nothing needs processing again.” | Retry save, Change location |
| Uncertain | “A file may have been saved; check this save again.” | Check this save again (Reconcile) |
| Collision | attempted name + suggestion | Use suggested name (edits draft only), Change location |
| Ineligible / ApprovalEvidenceMissing | reason, never “Rejected” | none |
| History not checked | “Saved previously; location not checked.” | Check, Open |
| History missing/changed/unavailable | preserved record + observation | Save another copy (Missing only) / Check again / new name or folder |
| PNG preparation incomplete | “Approved; PNG preparation did not finish.” | existing Run step / Retry |

History query failure shows “Save history could not be read” (never empty history). No startup/render-time reconciliation or folder scanning. Late progress/results apply only when session, target key, operation id and selection generation all match.

## Acceptance criteria (current Jira, all eight)

| AC | Evidence plan |
|---|---|
| 1 Asset final step: PNG name/destination, no size/TIFF | VM test at Trim review and after promotion; rendered en/zh-CN |
| 2 TIFF: name, TIFF type, physical mm, destination | VM test using backend physical mm; older approved size selectable while newer pending |
| 3 No forced dialog; Approved → Saved only on verified delivery | picker call count 0 with remembered folder; ordered observations; real SQLite/NTFS integration |
| 4 Failure/cancel/collision: Approved not saved, reason, suggestion, change+retry without processing | coordinator + integration tests with zero approval/processor/promotion calls on retry |
| 5 Open selects delivered file; repeat starts no production | fake shell receives live lease path; lease disposed on all paths; human Explorer check NOT RUN |
| 6 Next image shows last successful destination before confirmation | integration test with second session |
| 7 No direct pending export/open; exact approval first; failed approval → no export; ineligible explained | same-hash/different-revision refusal, failed/unknown approval zero export, unreviewed root blocked |
| 8 en/zh-CN keyboard reachable, unclipped, readable without colour | resource parity, Tab-stop/UIA facts, off-screen renders 1000×700 and 1920×1040; human workstation check NOT RUN |

## Validation plan and evidence limits

Focused tests first (red where feasible), then targeted groups: coordinator (fakes), real integration through `ApprovedArtifactDeliveryService` + `SqliteDeliveryRepository` + `WindowsDeliveryFileSystem` on GUID temp DB/workspace/destination with fake processors/picker/shell, VM/UI, rendering. Affected regressions: existing delivery service tests, session approval/promotion tests touched by the new session-service members, Wave1A/UI focus tests. No full suite, no startup/crop smoke. Off-screen renders are synthetic; actual picker/Explorer/physical input/novice checks are listed as NOT RUN for a later authorized desktop.

Artifacts: `artifacts/pf-opux-scrum11145/` (logs, TRX, renders, scoped diff, manifest, review). Jira: at most one evidence comment with marker `PF-OPUX-v1-SCRUM-11145-ui-v1`, then authenticated readback → `SCRUM-11145_JIRA_READBACK.json` → existing exporter → `SCRUM-11145_JIRA_FINAL.csv`.

## Execution checkpoints (2026-09-24 NZ)

- Implementation and targeted tests as planned; red/intermediate runs kept under `artifacts/pf-opux-scrum11145/`.
- Architecture rules found this task's ViewModel `System.IO`/`Verification` tokens; the pure text rules moved to `Workflow/Delivery/DeliveryDraftText.cs` and the shell P/Invokes live in `NativeMethods.cs`. Two remaining architecture failures read unchanged SCRUM-11144 files (migration 0018; `WindowsDeliveryNative.cs`) and were not modified.
- Independent review (fresh read-only Opus High context, actual model UNVERIFIED): round 1 and round 2 ACTIONABLE (6 P2 in total), corrected with regression tests; round 3 CLOSED; one P3 (T1) fixed and confirmed. Remaining P3 items are listed in RESULTS.md.
- Final candidate `final-candidate-manifest.json`; final evidence 49/49 final-save, 377/377 affected UI, 78/78 backend + composition.
- Jira: comment 10169; final readback 2026-09-24T03:25:06.124Z; `SCRUM-11145_JIRA_READBACK.json` and `SCRUM-11145_JIRA_FINAL.csv` (exporter PASS). Route: Opus High throughout (CONTINUE), reviewer FRESH_REQUIRED. Stop at SCRUM-11145.
