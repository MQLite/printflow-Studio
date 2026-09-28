# SCRUM-11148 — bounded implementation plan

Task `PF-OPUX-v1-SCRUM-11148-impl-v1`, 2026-09-28 NZ. Executes `printflow-remediation-prompts/PrintFlowStudio_SCRUM11148_Colleague_Correction_Implementation_Prompt_v1.md` (byte-identical to the submitted copy in Downloads).

## Authorization

- **What the owner approved.** By submitting that prompt the owner approved the base design, the design addendum and D1–D5, plus C1 (Home navigation and both service guards) and C2 (the precise binding predicate and the transactional assertion), and separately authorized this bounded implementation.
  - [SCRUM-11148_COLLEAGUE_HANDOFF_DESIGN.md](SCRUM-11148_COLLEAGUE_HANDOFF_DESIGN.md): SHA-256 `8d4edaec9c9b02d94bde75ac18d85f7a1bbf609bdcd667dc2df21e0c316bd9b2` — verified.
  - [SCRUM-11148_DESIGN_ADDENDUM.md](SCRUM-11148_DESIGN_ADDENDUM.md): SHA-256 `90f5d61a2b51f48188766d81d9dd5a652f11494bc576ae2aed643d6559ec5f28` — verified.
  - The two review records are evidence and correction history only. All four files are preserved byte-for-byte, including their historical PROPOSED wording.
  - The addendum overrides only the base clauses listed in its §5. The superseded Home-import route and the superseded all-manual-import Stop rule are not implemented.
- **Jira.** At most one factual comment on SCRUM-11148 with marker `PF-OPUX-v1-SCRUM-11148-impl-v1`, then an authenticated initiative readback and export. Nothing else in Jira.
- **Not authorized:** commit, push, deployment; production startup/DI graph, database migration or customer files; visible windows, UIA, OS input or capture; generic-import Stop/TakeOver repair; PNG approval repair; rule/memory updates; SCRUM-11149 or any other Task.

## Baseline

- `D:/Repositories/printflow-Studio`, `master`, HEAD `eea60198c5095243f0694717a7479b00c3e1cd73`; 72 dirty/untracked status entries, preserved.
- `artifacts/pf-opux-scrum11148/`: `baseline-all-files.sha256` (866 tracked/unignored files, 2026-09-28T15:26:39+13:00), `baseline-git-status.txt`, `baseline-head.txt`; `pretask/` byte copies of every existing file this task touches, hashes in `pretask.sha256`.
- Baseline build of the test project: 0 warnings, 0 errors (`build-00-baseline.log`).
- **Jira pre-read** (authenticated, 2026-09-28): SCRUM-11148 (id 10878) To Do, updated `2026-09-22T14:27:26.992+1200`, 0 comments, parent SCRUM-11139, blocks SCRUM-11154/11155, labels `operator-ux`, `pf-opux-v1`, `printflow`, Planning-ID `PF-OPUX-v1-colleague-correction-handoff`. Description and all eight AC are identical to the design's dated read.

## Route

- Claude adaptation v1.2 of policy v2.4, `route_offset: 0` (direct instruction).
- NormalRoute = RequestedRoute = ExecutionTarget = Opus/High for all substantive work. No Sonnet unit (nothing here is mechanical enough).
- ActualRoute: host model `claude-opus-5-5`, effort UNVERIFIED. Context `CONTINUE`; one fresh read-only reviewer at the review boundary (`FRESH_REQUIRED`).

## Source findings that shape the implementation (drift from the design pseudocode)

| # | Finding | Consequence |
|---|---|---|
| G1 | `IWorkspace` has two test doubles (`FaultingWorkspace`, `HomeScreenHarness`) and no generic copy-with-verify primitive. | Package file work goes through a new Workflow port `ICorrectionPackageStore` (Infrastructure `FileCorrectionPackageStore`), resolved through `IWorkspace.ResolveAbsoluteDirectory`. `IWorkspace` is unchanged. |
| G2 | `IManualResultImporter` has a test double (`TimedImporter`). | `PreflightAsync`, `HashSelectedAsync` and the `expectedSelectedHash` overload are added as interface members with safe defaults (unavailable preflight refuses; the default hash overload post-checks the managed copy). `WicManualResultImporter` implements all three natively on one shared validation routine. |
| G3 | `SessionAggregate`/`SessionMutation`/`SessionListItem` are positional records constructed in many places. | New members are `init` properties with empty defaults (`CorrectionRequests`, `CorrectionRequestChanges`, `HasOpenCorrection`). |
| G4 | `ViewOf` has seven callers building the view from partial aggregates. | `ViewOf` gains a required `corrections` argument; every caller passes the rows it holds. The dedicated entries return a fresh `LoadAsync` view. |
| G5 | `SessionListItem` comes from SQL. | `HasOpenCorrection` is computed in `SessionService.ListRecentAsync` for handed-off rows only, from the loaded aggregate and the same eligibility; no repository SQL change. |
| G6 | Migration inventory: 0001–0018 exist; only `MaximumBoundsBoundaryTests` pins the last script. | New `0019_correction_request.sql`; that single expectation moves to 0019 with its rationale. |
| G7 | `Allows()` already gates every consequential button on `!IsAdjustingTrim`. | The S1 panel reuses the same gate (`IsAskingColleague`). |

## Slices (base §12 amended by addendum §7)

| Slice | Files | Content |
|---|---|---|
| 1 Persistence | `Migrations/0019_correction_request.sql`; `SqliteSessionRepository.cs`; new `Services/CorrectionRequest.cs`; `SessionAggregate.cs`, `SessionMutation.cs` | Table, CHECKs, one-open unique index, identity-immutability and forward-only triggers; aggregate load; conditional changes `Insert`, `Supersede`, `MarkReady`, `SetLastImportAttempt`, `AssertBound`, `MarkReturned` — every non-insert change is a conditional update whose `changed != 1` rolls back the whole commit. |
| 2 Workflow | `WorkflowCommand.cs`, `TransitionTable.cs`, `WorkflowEngine.cs`, new `CorrectionRequestEligibility.cs`, `SessionView.cs` | `RequestColleagueCorrection` (ReviewRequired row, Active, exact R/U, only `MarkSessionHandedOff`, no lock effect) and `ImportCorrectedImage` (session-scoped, SubmitManualResult effects); probes from the snapshot's own R/U; eligibility modes Review / AfterUnfinishedImport; `BoundCorrectionClose`; `CorrectionHandoffView`; refined `CanSubmitManualResult`. |
| 3 Files and service | new `SessionService.Correction.cs`, `SessionService.cs`, `SessionService.Recovery.cs`, `StartupRecoveryService.cs`, `ISessionService.cs`, new `Ports/ICorrectionPackageStore.cs`, `Ports/IManualResultImporter.cs`, `WicManualResultImporter.cs`, new `FileCorrectionPackageStore.cs` | Request/repair/import entries with one gate each; `CorrectionContext` guard for both commands; guard (b) on generic submit; `RejectExactReviewAsync`; preflight + `expectedSelectedHash`; `ProducingWork.CorrectionRequestId` with the opening change applied to `afterStart`; bound Success/Failed/Cancelled/fail-closed closes; bound startup close; `RecoveryOf.HasOpenCorrection` with ManualResult omitted; `EnsureIntegrityAsync` mapping. |
| 4 App | new `SessionViewModel.Correction.cs`, `SessionViewModel.cs`, `.OperatorStatus.cs`, `SessionScreenView.xaml(.cs)`, `HomeViewModel.cs`, `RecoverySessionRow.cs`, `RecentSessionRow.cs`, `HomeView.xaml`, `IFilePicker` overload, new `ICorrectionFolderShell` + Infrastructure shell, `ServiceRegistration.cs`, `Strings*.resx/.cs`, terminology reference | S0–S7; exact BG Approve/Reject; generic HandOff hidden on BG review; generic Submit and Return-to-automation demoted while importable; Home first-position "Open job to import corrected image" (navigation only) and waiting lines; zh-CN `SessionState_HandedOff` → 已移交. |
| 5 Validation | new tests; migration inventory; `TransitionMatrixTests` rows | Targeted tests, one settled affected run, renders, one independent review, Jira. |

## AC and T1–T9 evidence map (planned; results in RESULTS.md)

| Item | Evidence |
|---|---|
| AC1 | Engine rows/probes; `ExecuteAsync` refuses both commands (incl. legacy handoff); service request with empty note → `HandedOff` + default reason + READY row; VM Ask→Prepare; Approve/Reject disabled while S1 open; Ask hidden elsewhere. |
| AC2 | VM panel content en/zh-CN; Open folder to a fake shell with the managed path; reference row says "do not edit". |
| AC3 | Real NTFS: Source/InputSnapshot/all Revision/Approved hashes unchanged; copies are distinct files (no link), hashes equal R/U; colleague edits never overwritten on repair. |
| AC4 | StartStep/ReenterAutomation/Approve refused in S3; Failed/Stopped/crash-recovered bound import → `HandedOff` with the request's reason; Recent row state + waiting line en/zh-CN; reload writes nothing. |
| AC5 | R2 new id, `ManualResultImport`, source U, one attempt, zero Meitu calls, RETURNED row; R2 review names Trim; approve → Trim current, no BG attempt added; same-hash R2 refuses stale exact approve/reject. |
| AC6 | Preflight refusals (wrong ext, JPEG-as-PNG, opaque, empty alpha, wrong canvas, malformed, locked, missing, reference chosen, changed): no attempt, no Revision, `HandedOff`, R current. |
| AC7 | Architecture: no `FileSystemWatcher`/timer in the correction code; reload changes no rows. |
| AC8 | Off-screen renders en/zh-CN at 1000×700 and 1920×1040 (inspected); resource parity; Tab reachability via existing synthetic helpers. Human check NOT RUN. |
| T1–T9 | Addendum §6 cases, each as a named test (see RESULTS.md). |

## Test isolation

GUID-owned temporary SQLite and workspace (`SessionServiceHarness`, `HomeScreenHarness`), temporary lease DB, `UnverifiedEnvironmentGate`, fake Meitu/Photoshop, real WIC importer on synthetic PNGs, a gated importer double, `FaultingRepository`, `FakeProcessLiveness` with `CreateRecoveryService`, stub picker, recording folder shell, off-screen `WpfRendering`. The existing unsafe-class exclusion filter is reused unchanged.

## Deviations and refinements

Recorded here as they arise; see RESULTS.md §1 for the final list.

## Codex continuation — 2026-09-28

- Resumed the explicitly requested Claude chat `SCRUM-11148 colleague correction implementation`, session `58c23d0a-0ac1-44c6-a1c9-3a728d6d9436`. Its substantive implementation remains in the same dirty `master` checkout at the recorded HEAD. Preserved the approved scope and all prior evidence; no commit/push/production/desktop authorization inferred.
- Reused this plan under installed Codex policy v2.4, EXECUTE_HANDOFF, inherited route_offset 0. No new design cycle, branch reset or clean checkout: the authorized continuation needs the accumulated uncommitted work.
- Resume tasks: reproduce/fix the migration expectation, independent candidate review, bounded findings corrections, targeted plus affected validation, one authorized Jira comment/readback/export, final RESULTS and appended HANDOFF.
- Migration red: DeliverySchemaTests expected 18 but migration0019 correctly yielded19. Updated expected version to19 and added empty CorrectionRequest assertion while retaining existing-session and delivery preservation checks. New targeted 8/8 PASS; affected workflow/persistence11528/11528 and backend/delivery146/146 PASS before review fixes.
- Fresh independent native reviewer uses fork_turns=none and only requirements/source/diff/raw evidence. Requested gpt-6-astra/high for recovery/data-safety/UI review; runtime identity UNVERIFIED. Initial findings R1/R2 (unsafe temporary overwrite/linked-final acceptance) and R3 (same-session stale asynchronous UI results) preserved in independent-review-initial.md.
- Review corrections split at a genuine independent boundary via dispatching-parallel-agents: package protection requested gpt-6-sol/high; correction UI requested gpt-6-astra/high. Distinct file ownership, one parent-controlled build/test sequence. No history-fork claimed independent. Parent current runtime model/effort UNVERIFIED; in-place switch unavailable (MODEL_SWITCH_UNAVAILABLE), no claimed downgrade. Delegated routes are supported requests, not runtime telemetry.
- Jira verified again through authenticated Rovo: same issue10878 / parent11139 / Planning-ID, zero comments. Only minimal SCRUM-11148 task-marker mapping added to the existing exporter; historical outputs preserved.
- Final outcomes, candidate hashes, AC/T1–T9 mapping and deviations are recorded in artifacts/pf-opux-scrum11148/RESULTS.md after validation.