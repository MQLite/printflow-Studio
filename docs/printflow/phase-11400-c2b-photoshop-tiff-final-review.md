# Epic 11400 Part C2B — Photoshop TIFF final review and file lifecycle

**Verdict: 11400-C2B PASS — READY FOR EPIC 11400 FINAL QA**

C2B adds the one thing C2A left open: what happens to a validated production TIFF once a human
decides about it. Approval promotes the exact reviewed bytes into `Approved\` and completes the
workflow; rejection sends them to the Windows Recycle Bin and returns the step to `RetryRequired`.
No second review state machine was built, no new Revision is created by a file moving folders, and
global Production remains closed.

---

## 1. C2A Working-location handoff

C2A left the TIFF in the attempt's own `Working\<attemptId>\` directory with the `PhotoshopOutput`
step in `ReviewRequired`, one Revision, one `PrintOutput` at `ReviewState.NotReviewed`, and nothing
in `Approved\`. That placement was forced — C1's accepted saver refuses any destination that is not
`Working`, and an unreviewed artefact in `Approved\` would have been a lie — and it handed C2B a
promotion step the TIFF workflows had never had.

Two C2A notes carried into this slice and both are addressed below: the promotion itself (§4–§10),
and the dormant `WorkflowEffect.CleanupWorking` (§12).

## 2. Existing review architecture audit

The audit's conclusion was that **most of C2B already existed and was correct**, so the slice is
narrow by design rather than by ambition:

| Concern | Where it already lives | C2B's change |
|---|---|---|
| Approve / Reject commands | `WorkflowEngine.Approve` / `.Reject` | none |
| Hash-bound review authority | `SessionService.EnsureIntegrityAsync` → `RevisionIntegrityGuard` | none |
| Step transitions | `TransitionTable` — `Approve→Approved`, `Reject→RetryRequired` | none |
| `Session Completed` | `WorkflowEngine.Complete`, session-scoped | none |
| Retry semantics | `WorkflowEngine.Retry`, fresh attempt directory | none |
| `ReviewDecision` (append-only, typed reason, notes) | `Domain.Reviews` | none |
| `PrintOutput.ReviewState` projection | `SessionService.BuildMetadataMutation` | none |
| Recycle Bin abstraction | `IRecycleBin` / `RecycleBin` — registered, **never called** | first production caller |
| `RecycledAtUtc` on `PrintOutput` | modelled since Epic 11100, **never written** | first writer |
| Promotion primitive | `ReserveOutput` + `WriteReservedAsync`, used by `ApprovedPngExport` | reused for the TIFF |

The exact-hash guard already covers Approve and Reject for **every** artefact, so §5 needed no new
code — only regressions proving it (§27 below). `ReviewSubjectKind.PrintOutput`, the twin
`PrintOutputId`/`RevisionId` GUID, and the cached `ReviewState` update were all already wired for
`PhotoshopOutput`; what was missing was the file work.

## 3. Revision and file-location model

**The architecture answers this question, and it answers it as option B: a promoted artefact
reference associated with the same immutable Revision.**

The evidence is not stylistic. `Revision_Immutable_Update` in `0001_initial_schema.sql` aborts any
update that changes a Revision's `RelativePath`, `Sha256`, `SourceRevisionId`, `Operation` or
`ByteLength`. `PrintOutput` carries its own `File`, `ByteLength`, `Sha256`, `ReviewState` **and**
`RecycledAtUtc`, and has no such trigger. The database has always said that a Revision records what
was produced and where it was produced, while the `PrintOutput` records where the deliverable is.

So:

- the **Revision** is untouched by approval — same id, same SHA, same `SourceRevisionId`, same
  Working path, still re-hashable by the integrity guard;
- the **PrintOutput**'s `File` moves from `Working` to `Approved`, keeping its hash and byte length;
- the producing `Attempt`, the `SourceRevisionId` and the review history are all unchanged.

**No second content Revision is created by promotion**, and an architecture test forbids one
(`Promotion_records_the_moved_file_on_the_PrintOutput_and_never_on_the_Revision`). `PrepareAsset`'s
`ApprovedPngExport` still creates a Revision, because it is a producing *step* in the workflow
definition — a different thing, unchanged by this slice.

**One C2A read-model defect was found and fixed.** `Mappers.ToDomain(OutputRow)` hardcoded
`WorkspaceArea.Approved` when rehydrating `PrintOutput.File`. That was harmless while every TIFF was
reserved in `Approved\`; after C2A moved them to `Working\` it meant every unreviewed TIFF reloaded
claiming to be approved. It now uses the existing `InferArea(relativePath)` helper, exactly as a
Revision does.

## 4. Approval promotion contract

```
Approve(PhotoshopOutput, reviewedHash)
  → EnsureIntegrityAsync            re-read + re-hash the Working TIFF (existing guard)
  → engine accepts                  step Approved, RecordReview effect
  → reserve Approved destination    ReserveOutput, atomic FileMode.CreateNew
  → COMMIT 1                        PrintOutput.PromotionReservation = <reserved ref>
  → WriteReservedAsync              copy the bytes unchanged
  → InspectAsync + compare          promoted SHA == Revision SHA, promoted length == Revision length
  → COMMIT 2                        ReviewDecision + PrintOutput.File = <approved> + ReviewState.Approved
                                    + PromotionReservation = null + step Approved
```

Nothing is marked Approved before the promoted file is positively established: every failure before
COMMIT 2 leaves the step in `ReviewRequired` with no review decision. The re-hash after the copy is
deliberate — `WriteReservedAsync` returning success is the copy's own report about itself, and the
only independent check of what actually landed is reading it back.

**Exact bytes are preserved.** Working SHA == Approved SHA == Revision SHA == `PrintOutput.Sha256`.
Photoshop is not reopened, nothing is resaved, recompressed, re-encoded or regenerated.
`PrintOutput_Identity_Immutable` (migration 0007) makes the database refuse any update that changes
a `PrintOutput`'s hash, byte length, source Revision or creation instant, so "promotion is not
another export" is enforced below the repository as well as in it.

**Move versus copy.** The established promotion primitive is reserve-then-copy, and it is retained
rather than replaced by a literal `File.Move`. `ApprovedPngExport` has always promoted this way; the
reservation is what makes collision numbering atomic; and deleting the Working original would break
the producing Revision, whose integrity guard re-reads that exact file. So after a successful
approval the workspace holds **one authoritative Approved copy** and one retained producing artefact
that nothing presents as pending review (the step is `Approved`, no review command is offered, and
the output row names the Approved file). Clearing `Working\` is a session-completion concern — see
§12.

## 5. Exact-hash review authority

Unchanged and reused, not reimplemented. Immediately before Approve or Reject, `EnsureIntegrityAsync`
resolves the step's current Revision, requires it to be valid, re-reads the bytes on disk through
`IFileInspector` and requires the hash to equal the recorded Revision SHA. A mismatch invalidates the
Revision in its own transaction and refuses the command with `RevisionIntegrityMismatch`. The engine
additionally requires the operator's `ReviewedHash` to equal the step's current hash.

No hashing was added in App or in a ViewModel; an architecture test bans `SHA256`/`ComputeHash(` from
the shell entirely.

## 6. Approved collision behaviour

The existing authority does all of it. The proposed name is the one the workflow's naming authority
already rendered for this TIFF (`{Name}_{SizeMm}mm_CMYK_W.tif`) — approval names nothing.
`ReserveOutput` then walks `OutputFileNaming.BuildCollisionCandidate` with the preset's collision
pattern, claiming each candidate with `FileMode.CreateNew` until one succeeds: `{Name}.tif`,
`{Name}_02.tif`, `{Name}_03.tif`, …

An existing Approved file is never overwritten and never silently replaced, and the persisted and
displayed reference is the name that was actually taken. The C1 attempt-directory no-collision rule
is not applied here.

## 7. Filesystem / database ordering

Two separate orderings, chosen in opposite directions, because the two decisions fail differently.

**Approval — file first, with the reservation persisted before the copy.** The reservation exists
precisely so a crash is recoverable: a process that dies after the copy but before COMMIT 2 leaves a
record of the destination it had already claimed, and the operator's next approval resumes into that
same file. Without it the restart would find the name taken by its predecessor's own copy and claim
`_02`, leaving two identical approved TIFFs of which only one is recorded.

**Rejection — disposal first, then the transaction.** A recycle that fails therefore records nothing:
the step stays in `ReviewRequired`, the TIFF stays where it is, and the operator can reject again.
The opposite ordering would let the database say a TIFF was disposed of while it sat on disk, which
§14 forbids outright — a failed disposal must not be dressed up as a completed rejection.

## 8. Approval crash and restart

| Point | Outcome | Test |
|---|---|---|
| Crash before any file work | Nothing changed; `ReviewRequired`; nothing in `Approved\`; the restart simply approves | `A_crash_before_the_promotion_leaves_the_TIFF_awaiting_review` |
| Crash after the copy, before the review commit | Reservation persisted, file on disk, still `ReviewRequired`; the restart **resumes into the same file** — one Approved copy named `{Name}.tif`, one review decision, no `_02` | `A_crash_after_the_promotion_resumes_into_the_same_Approved_file` |
| Restart after a complete approval | Nothing changes; a replayed Approve is refused by the existing transition table | `A_restart_after_a_completed_approval_changes_nothing` |
| Copy fails (I/O) | No approval, no completion, Working TIFF retained; the retry writes into the claimed destination | `A_failed_promotion_copy_records_no_approval` |
| Promoted bytes do not match | Refused with `OutputValidationFailed`; the bad copy is **quarantined out of `Approved\`**; the reservation is released so the next approval claims a fresh name | `A_promoted_file_that_does_not_match_the_reviewed_bytes_is_refused_and_quarantined` |

One residue is stated rather than hidden. A hard process death between `ReserveOutput` claiming the
name on disk and COMMIT 1 landing leaves an **empty** reservation file in `Approved\`, because no
code runs to clear it; the next approval numbers around it. What it cannot leave is a second copy of
the approved TIFF — the bytes are written only after that commit lands, and no record points at the
residue. When the commit merely *fails* (rather than the process dying), the reservation is
quarantined out of `Approved\` immediately; that behaviour was added after the first test run caught
the orphan.

Approval idempotency: after success the step is `Approved`, and `Approve` is not a legal command
against an `Approved` step, so a replay cannot reach the file work at all. No duplicate Approved
copy, no second Revision, no second `ReviewDecision`, no re-completion.

## 9. Rejection and Recycle Bin contract

```
Reject(PhotoshopOutput, reviewedHash, reason, notes)
  → EnsureIntegrityAsync            re-read + re-hash (existing guard)
  → engine accepts                  step RetryRequired, CurrentRevisionId cleared, RecordReview effect
  → IRecycleBin.SendToRecycleBin    exactly once, for exactly the reviewed file's absolute path
  → COMMIT                          ReviewDecision(reason, notes) + ReviewState.Rejected
                                    + PrintOutput.RecycledAtUtc
```

The reason vocabulary is the existing `RejectionReason` enum — no Photoshop-specific value was added,
because `WhiteInkIssue`, `ColourIssue` and `DimensionIssue` already cover what a TIFF reviewer
rejects for. The typed reason is persisted, not a display string, alongside the optional free-text
notes, the reviewed Revision's hash and the producing attempt.

**No hard-delete fallback exists anywhere.** `RecycleBin` returns a structured failure rather than
falling back to deletion, `SendToRecycleBin` has exactly one caller (`SessionService`) and one
implementation (Infrastructure), and an architecture test bans `File.Delete(` from all four projects
— with two exemptions named individually: `FakeAdapterExecution.cs` (a deterministic fake making an
output vanish so failure handling can be tested) and `FileWorkspace.CleanupWorking` for
`Directory.Delete(`.

**Rejected history survives disposal.** The Revision id, SHA, filename, `SourceRevisionId`, producing
attempt and the decision all remain; `RecycledAtUtc` is what distinguishes historical metadata from
currently available bytes, and the read model reports that distinction (`PrintOutputView.IsRecycled`)
rather than continuing to offer a file that is gone.

## 10. Rejection crash and restart

| Point | Outcome | Test |
|---|---|---|
| Recycle fails | No decision recorded, TIFF intact, `ReviewRequired`, structured failure carrying `recycled=false`, `reviewRecorded=false`, `hardDeleted=false`; rejecting again once cleared succeeds | `A_failed_recycle_records_no_rejection_and_keeps_the_TIFF` |
| Crash before the recycle | Nothing changed; the restart rejects normally | `A_crash_before_the_recycle_leaves_the_TIFF_awaiting_review` |
| Crash after the recycle, before the commit | No decision, no false state, bytes in the Recycle Bin (not deleted); the next decision on that step is refused with `RevisionIntegrityMismatch` and the Revision is invalidated | `A_crash_after_the_recycle_records_no_decision_and_refuses_the_next_one` |
| Restart after a complete rejection | Decision, `Rejected`, `RecycledAtUtc` and `RetryRequired` all read back; nothing is disposed of twice | `A_restart_after_a_completed_rejection_changes_nothing` |

The third row is the one window this architecture cannot close, and it is documented rather than
suppressed. The behaviour is deterministic and safe — the operator cannot approve a disposed TIFF,
nothing untrue is recorded, and the file is restorable from the Windows Recycle Bin because it was
never hard-deleted. Closing it would require recording the rejection before disposing of the file,
which trades this window for the one §14 explicitly forbids.

## 11. RetryRequired and the new attempt

Rejection clears the step's `CurrentRevisionId` and moves it to `RetryRequired` through the existing
transition table — no new rejection state was invented. `Retry` then returns the step to `Waiting`
and the following `StartStep` creates a new `AttemptId`, a new `Working\<attemptId>\` directory and
therefore a new TIFF destination, with `RetryOfAttemptId` pointing at the previous attempt.

The recycled TIFF is never restored or reused, and no old Working path is overwritten. The size
decision, the enlargement authority and the W1 branch are **not** reconfirmed: existing workflow
policy already carries them across a retry, and forcing reconfirmation would have been an invention.

## 12. CleanupWorking decision — **not wired**

Audited, and deliberately left unexecuted.

- **Who emits it:** `WorkflowEngine.Complete`, for every workflow.
- **What it would remove:** `FileWorkspace.CleanupWorking` deletes the entire `Working\` tree
  recursively and recreates it empty. It is not scoped to one attempt, one artefact or one status.
- **What still references those files:** since C2A the production TIFF's Revision names a file inside
  that tree — and so does every Meitu-derived Revision, and every trim and manual-crop result.
  Executing the effect as it stands would destroy the artefacts immutable records point at and break
  their integrity guard on the next read.
- **Whether cleanup means delete, recycle or quarantine:** unresolved. MVP design §10 says "clean
  safe-to-remove working copies", and "safe-to-remove" has no definition anywhere in the system —
  which is precisely the missing piece.

It is therefore not wired, and C2B implements only the exact TIFF promotion and disposal it owns.
Two tests keep the decision honest: `CleanupWorking_still_has_no_production_interpreter_or_caller`
fails the moment the effect gains one, and `Completing_the_session_does_not_delete_the_Working_artefacts`
asserts that every Revision's file survives completion today.

**Recommendation for Epic 11400 Final QA / a later slice:** define "safe to remove" against attempt
status and Revision reachability, then wire it with the §21 test matrix.

## 13. Final-review UI and read model

The existing review panel is reused. There is no second review screen, no Photoshop-specific command,
and `Approve`/`Reject` act on the current step's Revision exactly as they do for an enhanced PNG.
What a TIFF changes is wording and two lines of context:

| | en-US | zh-CN |
|---|---|---|
| Heading | Production TIFF | 生产 TIFF |
| Summary | CMYK + W1 validated · {branch} | 已验证 CMYK + W1 · {branch} |
| Caveat | The file's structure was checked, not its artistic or print quality. | 系统验证的是文件结构，而非画面或印刷质量。 |
| Approve | Approve this TIFF | 批准此 TIFF |
| Reject | Reject and make another TIFF | 拒绝并重新生成 TIFF |

The caveat is there because "validated" must not be read as "this will print well" — the system
checked structure, not artwork. The facts §22 asks for (filename, pixels, DPI, SHA short id, W1
branch, the preparation audit) are the ones the artefact and preparation panes already show; no
parser internals — sample layout, byte order, layer compression, spot identity — are exposed. Those
stay in the producing attempt's `AdapterNotes` where an investigation can find them.

The output list gained one truthful field: **where the file is**, as an area and never a path —
"awaiting review in Working" / "in Approved" / "sent to the Recycle Bin". After approval the review
panel disappears, the row says Approved, and `Complete` becomes available; after rejection the row
says recycled and is marked unavailable, the step is retryable and the session stays active.

Rejection uses no confirmation dialog: the reason and notes are inline in the review panel, so
"Cancel" is simply not pressing Reject. A test proves that choosing a reason and typing notes
performs no review, no recycle and no state change.

## 14. Fake approve and reject

Both workflows run end to end under `Adapters.Mode = Fake`. The whole lifecycle — reject → recycle →
retry → new attempt → approve → promote → complete — is exercised through the Fake Photoshop adapter,
and the file lifecycle is not conditional on the adapter being Production. An architecture test
asserts the promotion and disposal code names no `AdapterExecutionMode`, `AdapterKind`, `_photoshop`
or `_meitu`, and the same test that runs the Fake lifecycle re-asserts that the environment gate
still refuses Production while it does.

`PREPARE_ASSET` is proven unaffected: its intermediate rejections recycle nothing (its derived files
are retained for comparison, per MVP design §10), and its approved PNG still arrives through
`ApprovedPngExport` with the same hash and a Completed session.

## 15. Controlled production approval smoke

`PhotoshopFinalReviewWorkstationSmoke` (`PRINTFLOW_PHOTOSHOP_FINAL_REVIEW_SMOKE=1`), composing the
real `ProductionPhotoshopOutputProcessor`, a real `SessionService`, a real migrated SQLite database,
a real `FileWorkspace` and the **real** `RecycleBin`. A fresh synthetic job, not the retained C2A QA
session. No customer artwork. Passed on the first attempt.

| Fact | Value |
|---|---|
| Controlled workspace | `D:\PrintFlowStudio\QA\Epic11400C2B\20260901-100401-B7F7B364` |
| Preset | v1.14.0 / `F74792276C0B264C9F064D1C82CB26806F7B836A543E0AF5FC8B4E7FB1738C62` |
| Global `Adapters.Mode` | `Fake` (asserted unchanged by the smoke itself) |
| Adapter | `photoshop-cc2019-production-v1` / `Production` |
| Session | `S_20260831T220402Z_3b7ae25e` |
| Source | 1200×800 px @ 240 ppi synthetic PNG |
| Size decision | max 50.8 × 100 mm @ 300 ppi → 600 × 400 px, branch `W1_1px` |
| Attempt | `01a059d9-d329-762c-b274-d4f1236e935c` — Succeeded |
| Reviewed TIFF | `…/Working/01a059d9-…/PF_C2B_20260901-100401-B7F7B364_51mm_CMYK_W.tif` |
| Approved TIFF | `…/Approved/PF_C2B_20260901-100401-B7F7B364_51mm_CMYK_W.tif` |
| SHA-256 (both) | `30F401F2321351F9061C37B61F78D1FBD98BF33DF8965B302954BDF0A7BBA7A2` |
| Bytes | 2 452 724 |
| Pixels | 600 × 400 @ 300 × 300 dpi |
| Review state | `Approved` |
| Session state | **Completed** |
| TIFF Revisions | exactly 1 |
| Review decisions | exactly 1 |
| Files under `Approved\` | exactly 1 |

The TIFF was inspected before the decision — the run prints its path, dimensions, hash and the full
C1 validation note and pauses on that line for exactly that purpose. The hashes were then verified
independently of PrintFlow after the run:

```
sha256sum Approved/*.tif Working/*/*.tif
30f401f2…a7a2  Approved/PF_C2B_…_51mm_CMYK_W.tif
30f401f2…a7a2  Working/01a059d9-…/PF_C2B_…_51mm_CMYK_W.tif
```

Byte-identical, matching the recorded Revision SHA exactly.

A second live Photoshop **rejection** run was not performed. The rejection path is covered
deterministically end to end — the exact single Recycle Bin call, the reason persistence, the
failure case and both crash points — and a second real Photoshop job would establish none of it
better. This is the option §37 explicitly allows.

**Final Photoshop state:** process 21244 holds `PF_C2B_20260901-100401-B7F7B364.png` with unsaved
in-memory CMYK+W1 changes — the same retained synthetic source C2A left. Cleanup is not automated;
the operator closes it with Don't Save. The QA TIFFs, database and workspace are retained and the
hashes above are captured.

The B1A.3 geometry matrix, all three W1 branches and the C1 parser matrix were **not** re-run: C2B
consumes those already accepted stages, and one controlled `W1_1px` end-to-end is what the approval
smoke needs.

## 16. Targeted tests

| Suite | Tests | Status |
|---|---|---|
| `PhotoshopTiffFinalReviewTests` *(new — approval, promotion, collision, idempotency, rejection, recycle, retry, mutation, failures, crash points, read model, PREPARE_ASSET)* | 24 | pass |
| `PhotoshopFinalReviewBoundaryTests` *(new — C2B architecture)* | 7 | pass |
| `TiffFinalReviewUiTests` *(new — review wording, approve/reject UI, no-op reason selection)* | 4 | pass |
| `PhotoshopFinalReviewWorkstationSmoke` *(new — opt-in live approval)* | 1 | pass |
| `MaximumBoundsBoundaryTests` *(updated — newest migration is now 0007)* | 1 | pass |

All 24 behaviours required by §41 are covered:

1. exact-hash approval — `Approval_promotes_the_exact_reviewed_TIFF_…`
2. mutated TIFF approval refusal — `A_mutated_TIFF_cannot_be_approved_…`
3. mutated TIFF rejection refusal — `A_mutated_TIFF_cannot_be_rejected_…`
4. Working → Approved promotion — `Approval_promotes_the_exact_reviewed_TIFF_…`
5. approved collision naming — `Approval_never_overwrites_an_existing_Approved_file_…`
6. promotion hash preservation — same, plus the live smoke
7. no second Revision from promotion — `Approval_creates_no_second_Revision_…` + boundary test
8. Approved ReviewState — `Approval_promotes_…`, `The_read_model_reports_…`
9. Session Completed after final approval — `The_session_completes_only_after_the_TIFF_is_approved`
10. rejection reason persistence — `Rejection_recycles_the_exact_TIFF_…`
11. Recycle Bin exact invocation — same (`Recycled.ShouldBe([tiffPath])`)
12. no hard-delete fallback — `The_only_deletion_route_is_the_Recycle_Bin_abstraction_in_Infrastructure`
13. recycle failure — `A_failed_recycle_records_no_rejection_…`
14. rejection → RetryRequired — `Rejection_recycles_…`
15. retry new Attempt/path — `Retry_after_rejection_uses_a_new_attempt_and_a_new_output_path`
16. approval promotion failure — `A_failed_promotion_copy_…`, `A_promoted_file_that_does_not_match_…`
17. approval idempotency — `A_replayed_approval_is_refused_…`
18. restart approval fault points — three tests (§8 table)
19. restart rejection fault points — four tests (§10 table)
20. Fake approve/reject — `The_file_lifecycle_is_not_conditional_on_a_production_adapter`
21. production controlled approval smoke — §15
22. PREPARE_ASSET unaffected — `PREPARE_ASSET_review_and_completion_are_unchanged`
23. EnvironmentGate still closed — `The_environment_gate_still_refuses_every_production_adapter`
24. CleanupWorking not globalised — `CleanupWorking_still_has_no_production_interpreter_or_caller`

One existing test was updated rather than deleted: `MaximumBoundsBoundaryTests` pinned the newest
migration at `0006`. It now pins `0007` and explains why C2B added exactly one script — the
number moving is what that assertion is for.

## 17. Was a complete suite necessary? — **Yes, and it was run once**

C2B crosses two of §44's shared-boundary criteria:

- **persistence / migration** — migration `0007` adds `PrintOutput.PromotionReservedPath` and the
  `PrintOutput_Identity_Immutable` trigger, and the `PrintOutput` upsert now updates `RelativePath`;
- **shared promotion behaviour** — the `PrintOutput` mapper's area inference changed for every
  output row, and `SessionService.ExecuteAsync` now runs file work on the Approve/Reject path that
  all three workflows share.

The generic Approve/Reject semantics, `WorkflowEffect` execution and the `Session Completed`
transition are **unchanged** — but the migration alone makes the run mandatory under §35 and §44.

```
dotnet test PrintFlowStudio.sln --no-build --no-restore
Passed! - Failed: 0, Passed: 9757, Skipped: 0, Total: 9757, Duration: 1 m 48 s
```

Run exactly once, after the targeted tests were green, the live approval smoke had passed and the
build was clean. (C2A's baseline was 9722; the difference is exactly the 35 tests added here.)

## 18. Build

```
dotnet build
0 Warning(s)
0 Error(s)
```

Clean at preflight and clean at the gate.

## 19. EnvironmentGate status

`FoundationEnvironmentGate` is **unchanged**. `appsettings.json` still selects preset v1.14.0 and
`Adapters.Mode = Fake`. Normal Production still returns `EnvironmentNotVerified`, asserted directly
in two tests. No UI switch was added, and an architecture test proves nothing an operator can reach
in the shell names `IEnvironmentGate` — only the composition root does, in order to register the one
real gate.

The controlled bypass exists solely as a `ControlledSeamEnvironmentGate` declared inside the test
project and passed as a parameter to one `SessionService` in one opt-in smoke.

**Epic 11400 passing does not mean production deployment readiness.** Global Production stays blocked
until Epic 11500 implements real workstation verification.

## 20. Preset and evidence

**No preset change. Remains v1.14.0.** C2B discovered no new Photoshop runtime fact — final review,
promotion and disposal are PrintFlow workflow and filesystem behaviour, not a new Photoshop runtime
control — so no new workstation evidence was captured and no v1.15.0 was minted.

**No dependency change.** No package, project or lock file was touched, so no vulnerability audit was
run; it remains with Epic 11400 Final QA.

## 21. Remaining Epic 11400 Final QA scope

1. **Dependency / vulnerability audit** — deferred from every slice since B1A.
2. **`CleanupWorking`** — define "safe to remove" and either wire the effect with the §21 test matrix
   or remove it from the reducer. Today it is emitted and never carried out, and the Working tree
   grows for the life of a workspace.
3. **The rejection crash window** (§10, third row) — deterministic and safe, but worth a second
   opinion on whether the trade against §14 is the one the product wants.
4. **The hard-death reservation residue** (§8) — an empty file in `Approved\` after a process death
   in a sub-millisecond window; harmless, but Final QA should decide whether a startup sweep of
   zero-byte Approved reservations is worth having.
5. **Foreground dependency, carried forward from C2A and unchanged here:** if another application
   owns the foreground at the moment Photoshop requires guarded input, PrintFlow fails closed with
   `PhotoshopTargetLost` and `inputSent=false`. C2B did not change this. It belongs in Epic 11500's
   workstation/operator-readiness checks — the production path depends on the operator's desktop
   state at run time. (This run happened to take the foreground first time; C2A's did not.)
6. **Epic 11500** — real workstation verification, so global Production can open.

## 22. Git state

Branch `master`, based on `26c9d2a` ("Report: Epic 11400 C2A validated TIFF workflow output
integration"), present and unmodified throughout. No push, no amend, no rebase, no history rewrite.

**Product source (12 files, 1 new):**

- `src/PrintFlow.Domain/Outputs/PrintOutput.cs`
- `src/PrintFlow.Infrastructure/Sqlite/Migrations/0007_print_output_promotion.sql` *(new)*
- `src/PrintFlow.Infrastructure/Sqlite/Mappers.cs`
- `src/PrintFlow.Infrastructure/Sqlite/SessionRows.cs`
- `src/PrintFlow.Infrastructure/Sqlite/SqliteSessionRepository.cs`
- `src/PrintFlow.Workflow/Services/SessionService.cs`
- `src/PrintFlow.Workflow/Services/SessionView.cs`
- `src/PrintFlow.App/ViewModels/SessionViewModel.cs`
- `src/PrintFlow.App/Views/SessionScreenView.xaml`
- `src/PrintFlow.App/Resources/DisplayNames.cs`, `Strings.cs`, `Strings.resx`, `Strings.zh-CN.resx`

**Tests (5 new, 4 updated).** No synthetic input, generated QA TIFF, runtime database, Photoshop
workspace, screenshot, transcript or external preset/evidence file is committed.

---

## Notes on the verdict

PASS, without notes attached to the verdict itself — the three things a reviewer should look at are
already stated where they belong rather than being caveats on the result: the deliberate
non-wiring of `CleanupWorking` (§12), the rejection crash window and why the alternative is worse
(§10), and the empty-reservation residue after a hard death (§8). None of them can produce a false
review state, a lost file or a duplicate deliverable, and each is covered by a test that fails if it
changes.

All PASS criteria are met: the ReviewRequired TIFF is exact-hash-bound before either decision;
approval produces one authoritative Approved TIFF with byte-identical content and the same SHA;
approval creates no second content Revision; final approval completes the applicable workflow and
session; rejection records a typed reason and sends the exact generated TIFF to the Recycle Bin; no
hard-delete fallback exists anywhere; recycle and promotion failures cannot create false review
state; crash and restart behaviour for both decisions is deterministic and tested; rejection returns
the workflow to `RetryRequired` and Retry uses a fresh attempt and output path; the review semantics
remain adapter-agnostic; `CleanupWorking` was audited and deliberately left alone; the controlled
production approval smoke passed with independently verified hashes; the EnvironmentGate remains
closed; targeted tests pass; the build has 0 warnings and 0 errors; and the one migration-mandated
complete suite is green.
