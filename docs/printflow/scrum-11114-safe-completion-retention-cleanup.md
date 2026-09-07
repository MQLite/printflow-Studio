# SCRUM-11114 safe completion and retention cleanup

Status: **PASS WITH NOTES — SCRUM-11114 SAFE COMPLETION AND RETENTION CLEANUP VERIFIED** (2026-09-08).

## Requirement authority and pre-change inventory (2026-09-08)

Source: `C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv`, matched by exact Summary. The CSV uses original Work Item ID **11507**, parent **11500**, not the subsequently assigned Jira key SCRUM-11114 / Epic SCRUM-11107. Exact Description/AC:

> After Session completion, remove only safe-to-delete Meitu and Photoshop working copies, retain approved PNG/TIFF files, never overwrite or delete the user's source and preserve the current valid InputSnapshot according to the confirmed retention rule. Retain rejected Meitu-derived files for review comparison until Session end, then clean them; rejected PrintFlow TIFFs follow the Windows Recycle Bin rule.

SCRUM-11127 matches original Work Item **11703**, parent **11700**. Exact Description/AC:

> Use temporary databases and directories to verify transactions, InputSnapshots, Revision registration, hashes, collision-safe output names, restart persistence, cleanup and Recycle Bin behaviour. Confirm tests never overwrite or delete fixture source files and that invalidated or rejected outputs cannot remain current production results.

Before this change, WorkflowEngine.Complete emits CleanupWorking, ReleaseAutomationLock and MarkSessionCompleted. SessionService commits completion metadata but does not interpret cleanup. FileWorkspace's isolated cleanup recursively deletes Working. The 11400 C2B report §12 and release gate §15 explicitly defer integration because Revision paths remain in Working. The 11600 reports concern external application document cleanup and evidence preservation, not safe workspace retention.

| Persisted authority | Location before cleanup | Authoritative / needed after completion | Deletion / durable copy |
|---|---|---|---|
| InputSnapshot.OriginalSourcePath | External customer source | Provenance only; customer owns bytes | Never touched; managed root Revision is an independent copy |
| InputSnapshot.RootRevisionId → Import Revision.File | Session Source | Immutable managed snapshot, needed | Preserve; no replacement required |
| Revision.File: Enhance / RemoveBackground | Working/attempt | History and review identity; retain except explicitly rejected Meitu results after completion | Promote retained history; rejected results require explicit persisted retention release before deletion |
| Revision.File: Trim and manual crop | Working/attempt | History including attempt-bound TrimGeometry / ManualCropGeometry | Promote unchanged; no durable copy assumed |
| Revision.File: PSD/PDF prepared raster | Working/attempt | Managed visual raster history; source-W1 rules unchanged | Promote unchanged |
| Revision.File: manual-result import | Managed Working/attempt | Same authority as automated result | Promote unchanged |
| Approved PNG export Revision.File | Approved | Final deliverable and review authority | Preserve; earlier revisions independently retained |
| Photoshop producing Revision.File | Working/attempt | Immutable producing history, separate from PrintOutput.File | Promote unless explicitly recycled; approved copy does not erase Revision authority |
| PrintOutput.File (all sizes) | Working before approval, Approved after promotion | Every unrecycled output, including invalidated history | Preserve; approved outputs never hard deleted |
| PrintOutput.PromotionReservation | Approved placeholder or copied candidate | Recoverable pending approval claim, not yet authoritative bytes | Preserve |
| ProcessingAttempt.InputRevisionId / OutputRevisionId | References Revision rows | Retry chain and geometry bind to those same identities | Keep IDs, hashes, facts and producing history |
| ProcessingAttempt.Failure.Context / TechnicalDetail / AdapterNotes | Diagnostics may name working input/output and screenshot paths | Failed/interrupted/cancelled attempt evidence must survive | Preserve failed attempt folders and evidence; never classify arbitrary diagnostic paths as scratch |
| ReviewDecision.SubjectId / ReviewedSha256 | References Revision / PrintOutput identity | Append-only approved/rejected review history | Preserve binding; only rejected Meitu bytes have explicit end-of-session expiry |
| SessionStep.CurrentRevisionId / hash; preparation and background-removal authority | References Revision identity | Current approved upstream supports AddAnotherSize and history | Preserve bytes and identity |
| Startup quarantine artefacts + reason sidecars | Workspace Quarantine outside session | Recovery evidence | Outside cleanup scope |
| Successful attempt's independent input copy | Working/attempt/input basename | Disposable only when positively derived from its persisted upstream and not another file authority | Delete after retained authority is secure |
| Unknown files, native exports without proven ownership, staging/evidence leftovers | Working or other managed areas | Unknown | Keep by default |

The durable vocabulary has Source, Working, Approved, Rejected and Logs, but no area for retained, unapproved Revision history. Revisions is therefore needed; Approved would falsely label unapproved history. Planning precedes mutation. Promotion must copy and verify before one metadata transaction, with original cleanup last. Rejected TIFFs retain their existing Recycle Bin boundary. Abandoned sessions are outside the AC.

## Retention decision and safe-to-delete definition

`SessionRetentionPlan.Create` is pure. It requires a persisted Completed session with a completion timestamp, every step Approved/Skipped, and no running attempt. The orchestration boundary also refuses the session if it holds the automation lock. Nonterminal processing, review, retry and interrupted steps are refused even if inconsistent session metadata claims completion. Manual processing is represented by HandedOff in this Product, not a separate `StepState.ManualProcessing`; HandedOff and Abandoned are excluded.

The closed categories are:

* **Preserve:** immutable InputSnapshot/root Revision; every approved deliverable; unrecycled PrintOutputs of every size; retained Revision history; failed/interrupted/cancelled attempt folders and explicitly referenced diagnostic files; reservations; quarantine; unknown files. An unknown name or a GUID attempt folder alone never authorises deletion.
* **Promote:** authoritative Working Revisions, including invalidated history, automatic/manual crops, imported manual results and prepared PSD/PDF rasters, into `Revisions/<RevisionId>/<original filename>`. Existing Source/Approved/Revisions authority stays in place.
* **Release comparison retention:** a rejected Enhance/RemoveBackground Revision with an exact hash-bound rejection, no approval, no current step or PrintOutput dependency, and no evidence retention obligation. Its row remains with `RetentionReleasedAtUtc`, `IsValid = false`, and `ReviewState = Rejected`. The path then explicitly denotes a historical location whose bytes may be absent. The source CSV authorises this specific expiry; it does not authorise erasing other history.
* **Delete:** a redundant non-TIFF former Working copy after its authority switched, an explicitly released rejected Meitu result, or the exact independent input copy created for a successful known producing attempt. The latter must match the persisted upstream basename and SHA-256 and have no remaining file authority or evidence claim. Unknown/native-export names are not guessed to be scratch.
* **Recycle:** exclusively the existing final-TIFF rejection flow. Cleanup never substitutes hard deletion, re-recycles an output, or invokes external applications.

The cached `Revision.ReviewState` is not updated by ordinary rejection today. The planner therefore uses the append-only **ReviewDecision** rows, including subject identity and reviewed hash, rather than trusting that cache. A released result cannot be consumed by RevisionIntegrityGuard or displayed as an available historical preview; both report the explicit retention state. SQLite refuses revival or new approval of released comparison bytes.

“Safe to delete” means **positively classified, exact-session-owned, non-authoritative after the committed retention transition, not diagnostic evidence, not a production TIFF, and still matching the expected hash immediately before deletion**. It does not mean “under Working” or “the session ended.”

## Promotion and crash ordering

Normal completion still follows the engine's existing semantics. SessionService first commits completion and lock release, then interprets `WorkflowEffect.CleanupWorking`. Cleanup is post-completion maintenance, not a second decision about whether approved production work succeeded.

1. Load committed metadata and list Working without following reparse points. Construct the complete pure plan before filesystem mutation.
2. Verify every required file's session containment and persisted hash. Corruption or missing authority fails closed before promotion or deletion.
3. Copy each retained Working Revision to a fresh staging file inside its deterministic Revisions destination directory. Flush copied bytes to disk, independently hash the staging copy, then rename it within that same directory with overwrite disabled. Verify the final destination again. The authoritative source is never moved or truncated.
4. A pre-existing final destination is reusable only if its hash matches. An existing partial staging file is **preserved**, not overwritten: retry creates a fresh staging file. Thus even a manipulated hard link at a staging name cannot cause an external source to be truncated.
5. In one SQLite maintenance transaction, switch every verified Revision location, record its `FormerWorkingPath`, and record any rejected-Meitu retention releases. Any unrecycled PrintOutput still sharing a moved Revision location follows that same verified destination in the same transaction. Hash, RevisionId, source identity, pixel facts, attempts, geometry and review bindings do not change.
6. Reload metadata and replan from committed authority. Verify the preserved set and the complete deletion set before the first delete. Delete only listed matching files, re-proving scope immediately before each mutation. There is no recursive directory deletion.

The copy crosses no assumed atomic filesystem boundary. Only staging-to-final rename uses `File.Move`, within one directory on one volume. Original-to-durable transfer is a verified copy, so a failure or process termination can leave duplicate bytes but cannot strand metadata at a moved-away source.

The next forward migration is **0014_revision_retention.sql**. Its two nullable Revision columns express actual file lifetime/provenance, not arbitrary JSON cleanup state. It retains immutability of identity and all pixel facts and permits only the narrow completed-session storage transition. No new session lifecycle state or cleanup-status migration is needed: pending work is derived from Completed state, persisted authority, former locations, explicit comparison expiry and current Working files.

A maintenance transaction never upserts stale ProcessingSession metadata. Eligibility is checked again inside SQLite; stale maintenance cannot turn an AddAnotherSize session back into Completed. The in-process completion gate serialises completion/retention with AddAnotherSize. Production startup already runs behind the application's single-instance guard before operator commands.

| Interruption | Durable truth and retry |
|---|---|
| Completion committed, effect not executed | Startup enumerates all Completed sessions, independently of Home's age/count limits, and invokes the same retention service. |
| Copy incomplete | Original Revision path remains authoritative. Partial staging bytes remain; retry creates a fresh verified copy. |
| Copy complete/hash verified, metadata not committed | Original metadata and source remain valid. An identical final copy is reused; a mismatched destination is refused. |
| One metadata update would fail | Every location/release in that transaction rolls back. No source cleanup has begun. |
| Metadata committed, source copies not removed | New durable paths resolve. FormerWorkingPath and release state let startup reconstruct the remaining deletion list. |
| Deletion stops partway | Only already-redundant files have been removed. All authoritative files remain. Missing delete candidates are no-ops on retry. |

## Scope, evidence, and operator truth

Every promotion/delete goes through the existing PathGuard plus exact session/declared-area validation. Retention accepts only the actual `Sessions/S_...` managed session root. It rejects cross-session references, traversal, protected-area references and reparse ancestry, including workspace ancestors. Working enumeration checks each entry before descending, so a junction is never traversed for cleanup.

Manual-result copies may be readonly. Only after their verified location switch can cleanup clear that obsolete managed copy's readonly flag and delete it. A Win32 file-identity query refuses shared/unverifiable hard links before changing attributes. The OS declaration stays in the repository's required `Automation/NativeMethods.cs`; this is a read-only file query, not an automation behaviour change.

TIFF protection is independent of “latest output”: the planner preserves every production-output origin, and the filesystem boundary rejects TIFF extensions and TIFF/BigTIFF signatures even under a renamed extension. Approved files never enter hard deletion. The existing `RecycledAtUtc` state also truthfully explains a rejected TIFF's producing twin Revision's absent bytes.

Failed retry evidence remains with the failed attempt or in the pre-existing recovery quarantine. Full-path references in failure context/notes are preserved; a different attempt's similarly named path is not mistaken for the same file. ReturnToStep invalidations and all associated review/attempt/geometry records remain history. Customer source paths are informational and are never passed to cleanup; Source snapshots retain their bytes and readonly attributes.

`SessionCleanupResult` reports committed promotions, deletions, preserved authority, warnings and failure separately from session state. A partial deletion failure reports its observed deletion count. A failure after valid completion leaves the session Completed and appears in the existing bilingual session notice as cleanup pending; startup records detailed failure entries in the existing recovery report. This adds no cleanup page, log-retention system or diagnostic export.

Conservative limits are intentional: redundant TIFF originals remain in Working rather than taking a hard-delete route; unknown files, interrupted staging evidence and empty directories remain. These are preserved categories, not a false claim that Working is empty. This task does not implement a general retention timer or reclaim all possible disk space.

## Verification record

Verification covers pure/service classification, all three workflow completions, three independently approved TIFF sizes, clean retry and failed evidence, rejected-Meitu expiry (including interruption after release), rejected-TIFF retry without double recycle, ReturnToStep history, manual import, manual/automatic crop geometry, PSD/PDF prepared history, corruption refusal, batch rollback, active/nonterminal refusal, concurrent AddAnotherSize, crash/restart boundaries, unknown files, junctions, hard links and TIFF hard-delete refusal. Final counts and independent live evidence follow.

### Bounded live/local filesystem proof

Run: **2026-09-07 23:32:19 UTC / 2026-09-08 New Zealand date**. Session: `01a07e37-269e-7ca9-b2c1-aca5741c28af`. This ran real SessionService, WorkflowEngine, FileWorkspace and SQLite, with deterministic Meitu and a synthetic processor that writes real CMYK/W1 TIFF bytes. No Photoshop or Meitu process was run; no customer job or baseline file was used.

The scenario failed an Enhancement attempt, persisted its generated screenshot path in failure context, retried from a clean upstream, approved intermediate results and a final TIFF, then completed through the real effect interpreter. A fresh repository/workspace/recovery service was rebuilt and the rows and files verified again. The opt-in proof retains its original synthetic files and DB for inspection; ordinary tests still dispose their generated temporary content.

Evidence manifest: [scrum-11114-live-filesystem-proof.json](scrum-11114-live-filesystem-proof.json). Independent verifier: [verify_retention_proof.py](../../tests/PrintFlow.Tests/Fixtures/verify_retention_proof.py), run with the bundled Python interpreter. It opens SQLite with `mode=ro` and re-reads disk using Python SHA-256, independently of the Product retention verifier and repository mapper.

| Observation | Result |
|---|---|
| Customer-source stand-in | Exists; SHA unchanged: `D19E21026144AD63F22DF1C845BD3F9F17444A4628AA6E4A6F8E0ECD9C6E2B77` |
| Managed InputSnapshot/root Revision | Exists in Source; hash matches SQLite |
| Retained Revisions | All **5** resolve and match persisted hashes; **4** Working Revisions promoted |
| Approved TIFF PrintOutput | Exists in Approved; SHA `C2ADA80DAA8EE63E7B6531E2216FF0720755C174667EBD34D9CC2A0D4D453A50`; approval still binds that hash |
| Classified working files | **7** existed before cleanup and are absent afterwards |
| Failed-attempt screenshot | Exists; SHA `9C44EE27124DC209743BC2FFB9CADE7279732BE06B1F6614D66DFCBBDBDCBBBB`; SQLite failure context still identifies it |
| SQLite | `integrity_check = ok`; no foreign-key violations |
| Automation lock | Free before/after restart; no cleanup lock mutation |
| Recycle Bin | No calls from completion cleanup |
| Independent verifier | **PASS**, six authoritative file-bearing rows verified |

The retained workspace is `C:\Users\admin\AppData\Local\Temp\PrintFlowTests\16b3d073fb6d4055838dff3eb86a9d55`; the retained DB is `C:\Users\admin\AppData\Local\Temp\PrintFlowTests\c6f7a3d8451441f5b576fad24d99f42d.db`. These contain generated synthetic data only. The fixed August session timestamp is the deterministic test clock; the manifest records the actual execution time.

### Build and targeted gates

* `dotnet build PrintFlowStudio.sln`: **0 warnings, 0 errors**.
* Targeted workspace, persistence, architecture, completion, startup/recovery, localisation and session-control gate: **1,057 passed, 0 failed, 0 skipped**. TRX: `tests/PrintFlow.Tests/TestResults/scrum-11114-final-targeted.trx`.
* Opt-in bounded live proof: **1 passed, 0 failed, 0 skipped**. TRX: `tests/PrintFlow.Tests/TestResults/scrum-11114-live-proof.trx`.
* Independent Python/SQLite/disk verification: **PASS**.

During development, a migration test needed to compare its original columns rather than new nullable columns; the latest-migration architecture expectation was advanced to 0014; and the new read-only Win32 link query was moved to the required NativeMethods boundary. All affected architecture and migration tests pass. Tests also exposed the non-authoritative cached rejection state and readonly manual-result copies; both were corrected before these gates.

The first complete-suite run reported **11,493 passed, 1 failed, 0 skipped**. The existing `TiffFinalReviewModeTests.An_authorised_enlargement_reports_the_factual_resolution_and_the_authority` asserted English text while inheriting this workstation's zh-CN UI culture. Its test now uses a scoped English UI culture and restores the prior culture afterwards. The isolated test passed, and the solution was rebuilt with **0 warnings / 0 errors** before the complete rerun. Product TIFF review behaviour was not changed. The initial result remains recorded in `scrum-11114-final-full.trx`.

### SCRUM-11127 independent clause reassessment

Original source row **11703** was re-read independently after implementation. Its AC does not require a Photoshop run; it requires real temporary DB/filesystem integration and the listed invariants.

| Exact AC clause | Evidence |
|---|---|
| Temporary databases and directories | Real TempDatabase/FileWorkspace in the targeted suite and retained synthetic live proof |
| Transactions | Existing repository all-or-nothing test plus new all-location-switch rollback and stale-maintenance refusal |
| InputSnapshots | Source byte/readonly preservation, root Revision authority, independent SQLite readback |
| Revision registration | Ordinary producing pipelines, unchanged IDs/facts, guarded location transition, no fabricated Revision on promotion |
| Hashes | Independently re-read after cleanup/restart; source/destination corruption fails closed |
| Collision-safe output names | Workspace reservation and NamingContractWorkflowRegressionTests; retention final destinations never overwritten |
| Restart persistence | Fresh repository/workspace/recovery services; before-copy, partial-copy, pre-commit and post-commit recovery |
| Cleanup | Real completion effect interpreter, derived startup retry, positive deletion classification and active refusal |
| Recycle Bin behaviour | Existing real temporary-file Recycle Bin test and failure/no-hard-delete tests; rejected TIFF is not recycled twice |
| Fixture source preservation | Product preserves source bytes and immutable snapshot; only generated temporary content participates in destructive tests |
| Invalidated/rejected outputs cannot remain current production results | Existing final-review and ReturnToStep checks, preserved invalidated history, explicit rejected-Meitu expiry and no revival |

The former integration gap is now covered. Each original clause is met: **SCRUM-11127 PARTIAL → FULL**.

### Final complete suite and coverage decision

`dotnet test PrintFlowStudio.sln --no-build --logger "trx;LogFileName=scrum-11114-final-full-verified.trx"` against the final built source: **11,494 passed, 0 failed, 0 skipped**, total **11,494**, duration **4 minutes 16 seconds**. This is the accepted 11,453 baseline plus **41** tests. No Product or test source changed after this run began; subsequent edits complete documentation only.

**SCRUM-11114 IMPLEMENTED_NOT_INTEGRATED → FULL.** Retention authority, verified promotion, truthful comparison expiry, production completion interpretation, startup retry and destructive-boundary protection are implemented and proven. **SCRUM-11127 PARTIAL → FULL** follows its independent clause assessment above. The dated delta is appended to [the coverage reaudit](original-jira-functional-coverage-reaudit.md); historical rows remain unchanged.

**SCRUM-11107 is not closed**; its other gaps, including SCRUM-11110 and SCRUM-11112, are outside this change. The three-size preservation test makes no independent SCRUM-11133 acceptance claim.

The verdict is **PASS WITH NOTES** because cleanup intentionally preserves redundant TIFF Working copies, unknown files, partial staging evidence and empty directories. All are bounded conservative retention choices; no claim is made that every completed Working directory is empty or every possible byte reclaimed.

### Git state and delivery

Work took place only in canonical `D:\Repositories\printflow-Studio` on `master`, starting from clean commit `62d9415`. No branch, worktree, alternate clone, amend, rebase or push was used. The implementation, tests, synthetic proof manifest, independent verifier, this report and the appended coverage delta are delivered in a new local commit named `Implement safe completion retention cleanup for SCRUM-11114`. No attribution trailer is added. The final response records the resulting commit ID and post-commit worktree status.
