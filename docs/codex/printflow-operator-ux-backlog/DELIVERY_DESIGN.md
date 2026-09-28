# PF-OPUX-v1 delivery design — SCRUM-11144 / SCRUM-11145

Status: **PROPOSED / AWAITING_OWNER_REVIEW**. Date: 2026-09-23, Pacific/Auckland.
Task: `PF-OPUX-v1-delivery-design-v1`. Design preparation and technical review only; this document is not approval to implement, migrate, or amend acceptance criteria.

## 1. Purpose, authority and verified baseline

A beginner must be able to confirm an exact result, see its filename and destination, save a verified copy, and locate that copy. Approval, workflow completion, and external delivery are separate facts. A failed save must not cost another processing run or another approval.

Authority, in order: this task's supplied delivery-design prompt and current user restrictions; current amended Jira requirements; current source; explicitly dated historical evidence. Technical proposals below remain proposals even after independent review.

- Repository inspected in place: `D:/Repositories/printflow-Studio`, branch `master`, HEAD `eea60198c5095243f0694717a7479b00c3e1cd73`. Existing uncommitted Wave 1/1A/11146 source, tests and documentation are preserved.
- Prompt read: `printflow-remediation-prompts/PrintFlowStudio_SCRUM11144_11145_Delivery_Design_Prompt_v1.md`.
- Authenticated Atlassian Rovo `getJiraIssue`, `view=full`, read on **2026-09-23 NZ**: [SCRUM-11144](https://yituoxx.atlassian.net/browse/SCRUM-11144), updated `2026-09-22T14:27:19.196+1200`; [SCRUM-11145](https://yituoxx.atlassian.net/browse/SCRUM-11145), updated `2026-09-22T15:00:19.787+1200`. Both are To Do under SCRUM-11139. Complete descriptions, all AC and comment containers read; each has zero comments. The amended 11145 scope and AC7 explicitly permit lawful approval followed by export. No Jira writes or CSV regeneration occurred.
- `SCRUM-11146_PLAN.md` and `SCRUM-11146_JIRA_READBACK.json` are historical inputs. The latter is dated **2026-09-22T23:12:09.631Z**, not this task's readback. Current 11146 was read only to preserve its acceptance boundary; its implementation/review was not repeated.
- `docs/printflow/operator-ux-terminology.md` defines Approved, Completed and Saved separately. MVP design §§6, 9–10, 17 supplies lineage, source protection and retention constraints. Its older configurable-root wording is superseded by the current preset-owned root implementation; external destination is not that root.
- No application, picker, Explorer, build, test, migration, production startup, desktop capture/input probe, customer image or production database was opened/run.

### Settled requirements versus proposals

**Settled:** exact eligible approved PNG/TIFF only; explicit final confirmation; approval before export; no export after failed approval; no silent overwrite; temporary copy then publication; final verification and durable delivery evidence; visible/editable filename and destination; remember only a successfully used destination; no dialog on every image; retry without processing; select the actual delivered file in Explorer; en/zh-CN and accessible keyboard flow; keep independently valid sizes; preserve originals, workspace and retention rules.

**Proposed here:** typed dual artifact identity, SQLite delivery/attempt tables, a distinct destination preference, a widened per-session coordination boundary, a Windows destination adapter with constrained filesystem support, recoverable publication protocol, service DTOs and UI states. None of these storage/API choices is owner-approved.

**Explicit unresolved business compatibility issue:** the existing asset route can mark its internal PNG-export step Approved without any human `ReviewDecision` in its ancestry (§3.3). No new approval permission is inferred from this. The proposed refusal and a separately scoped remedy require owner disposition (§10).

## 2. Source findings and selected architecture

All references below are repository-relative and refer to the current working tree, including existing uncommitted work. Named methods avoid borrowing stale line numbers from Jira.

| Concern | Current evidence and design implication |
|---|---|
| PNG | `src/PrintFlow.Workflow/Definitions/WorkflowCatalog.cs`, `PrepareAsset`: terminal `ApprovedPngExport`, operation `PromoteApproved`, no second review. `SessionService.PerformStepWorkAsync` copies the upstream bytes into Approved and creates a new `Revision`; `CompleteProducingStepAsync` creates no `PrintOutput` for PNG. |
| PNG approval cache | `Revision.Create` initializes `ReviewState.NotReviewed`; `WorkflowEngine.AttemptSucceeded`/`TransitionTable.Destination` marks the step Approved without a new review. Do not require the promoted revision's cached ReviewState to equal Approved or invent a review for it. |
| TIFF | `src/PrintFlow.Domain/Outputs/PrintOutput.cs`; `SessionService.BuildPrintOutput` assigns the same underlying GUID to the TIFF `Revision` and `PrintOutput`. `PerformFinalReviewFileWorkAsync` promotes first; `BuildMetadataMutation` records a `ReviewSubjectKind.PrintOutput` review. The twin Revision's cached ReviewState is not the TIFF approval authority. |
| Authoritative paths | `PrintOutput.File` is the approved TIFF location; the producing Revision can still point to Working, later to Revisions after retention. PNG uses its promoted `Revision.File`. Resolve both only through `IWorkspace.ResolveAbsolute`. `InputSnapshot.OriginalSourcePath` is protected provenance. |
| Validity and sizes | `SessionService.ComputeDescendantInvalidations` walks `SourceRevisionId`; `WorkflowEngine.AddAnotherSize` preserves previous sibling outputs and resets the pending size. A different current size alone does not make an older approved output obsolete. |
| Physical TIFF size | `ProductionTiffReviewService.PhysicalMillimetres` uses the output's producing attempt preparation or decoded dimensions/DPI. `PrintDimensions.WidthMm/HeightMm` can be maximum bounds. Do not display those bounds as the actual delivered artwork size or use the session's current pending dimensions for an older output. |
| Coordination | `SessionCompletionGate` is a per-session semaphore. Currently only Complete/AddAnotherSize and retention take it; ordinary approval/return/invalidation do not. The ViewModel's IsBusy is only UI exclusion. Neither alone currently proves export safety. |
| Filesystem patterns | `FileWorkspace.PromoteRevisionAsync` demonstrates create-new staging, flush, hash, no-overwrite move, hash. `ReserveOutput`/`WriteReservedAsync` instead claim/write the final internal name; these are unsuitable for external delivery. `PathGuard` checks lexical boundaries; `ReparsePointGuard` rejects reparse ancestry but is not by itself a race-free external-folder guard. |
| Persistence/settings | `SqliteSessionRepository`, `SqliteConnectionFactory` and `MigrationRunner`: transaction-bound metadata, WAL/FULL, forward-only embedded migrations, newest current script 0017, reject a newer schema. `ISettingsRepository`/`SqliteSettingsRepository` use the existing Setting table. `SettingKey.DefaultOutputRoot` is a preset fact, not an editable export preference. |
| UI seams | `SessionViewModel.ApproveAsync` passes the displayed hash to the existing command; `ReviewTargetIdentity`, `ReviewApprovalButton`, and `SessionScreenView.FocusNewReview` protect exact-target activation. `IFilePicker` and `SaveDiagnosticPackageDialog` demonstrate App-level dialog ports; no delivery shell-selection port exists. |
| Retention | `SessionRetentionService` and migration 0017 preserve review/session metadata and approved files. Removing Recent Processing is a visibility timestamp, not deletion. Delivery must not alter that policy. |

**Recommendation:** a Workflow application service `IApprovedArtifactDeliveryService`, a small shared artifact resolver, a sibling `IDeliveryRepository` implemented in the existing SQLite database, and an Infrastructure `IDeliveryFileSystem` port for external file operations. App owns a folder picker, the final-save coordinator/read model, and a shell-selection port. Domain owns typed identities/results, with no SQL or Windows calls. Names in this paragraph and subsequent API sketches are proposed, not existing symbols.

Alternatives considered:

1. Reuse `PrintOutput.PromotionReservation` and workspace final-name writing: less new code, but excludes PNG, exposes an incomplete final file, and confuses internal approval with external delivery. Rejected.
2. JSON sidecar/settings-only journal outside SQLite: avoids a schema addition but creates another authoritative store, weaker relationships and harder atomic destination preference updates. Rejected.
3. Existing SQLite plus a narrow external adapter: adds a bounded schema/API while preserving current workflow authority. Selected. No watcher, background job engine, broad audit subsystem or signing requirement.

## 3. Exact artifact identity and eligibility

### 3.1 Common identity

`ArtifactKey = (SessionId, Kind, ArtifactId)` where Kind is `ApprovedAssetPng` or `ApprovedPrintTiff`. ArtifactId means promoted RevisionId for PNG and PrintOutputId for TIFF. The UI also carries an expected full SHA-256 and an offer version; filenames are display data only.

A resolved `ApprovedArtifact` contains the key, exact managed file reference, approved SHA-256, positive byte length, approval evidence reference, lineage IDs, format and display metadata. Approval evidence is an existing `ReviewId`, its subject kind/id and reviewed hash; for PNG it additionally records the promotion revision/attempt and upstream revision it covers. A presentation snapshot is never an eligibility grant.

On every save/retry/reconciliation/Open request, load the aggregate by SessionId and the exact IDs. Verify same-session membership, current validity along the recorded lineage, no rejection/recycle/retention release of the deliverable, and consistent hash/length/format. Resolve paths through workspace authority. Never search directories by filename, use `LatestApprovedRevisionId` as the target, or silently choose the newest output.

The selected approved source must still exist and hash to its recorded authority. Hash a held read handle that excludes write/delete sharing and retain it through copy and final delivery commit. A missing or changed source is refused; delivery does not repair it, reapprove it, or mutate workflow validity as a side effect. Existing workflow integrity commands remain responsible for their own invalidation behavior.

### 3.2 TIFF predicate

For the requested PrintOutput:

- `IsValid`, `ReviewState.Approved`, no `RecycledAtUtc`, no unresolved `PromotionReservation`, `File.Area == Approved`.
- An existing approving ReviewDecision with matching SessionId, SubjectKind PrintOutput, SubjectId and ReviewedSha256. A contradictory later decision/invalid record fails closed; a historical approval cannot override current rejection or invalidation.
- Exact twin Revision exists, is valid, operation PhotoshopOutput, same session, matching SHA/length and matching `SourceRevisionId`. Its ReviewState need not be Approved. The output path, not the twin's former Working path, supplies export bytes.
- All ancestor records exist and remain valid; the output's source/preparation identity remains consistent with its producing attempt. Use recorded preparation, branch, preset identity and dimensions for that output; never reinterpret it using a new session size or a newly selected preset.
- When the current Photoshop step points to this output, its hash/state must agree. When it points to a different size, that is **not** grounds for refusal. Explicit rejection/invalidation/recycling, inconsistent lineage, or a superseded output is obsolete; an independently valid earlier size remains exportable even while another size is being prepared.

The summary uses the same output-bound physical-size calculation as `ProductionTiffReviewService`; factor a shared metadata calculation if necessary. A verified TIFF decoder provides the legacy fallback; don't trigger Photoshop or relabel maximum bounds as measured artwork dimensions.

### 3.3 PNG predicate and discovered compatibility boundary

For a normal approved asset:

- Session workflow is PrepareAsset. Its terminal ApprovedPngExport step is Approved and references this exact revision/hash. The revision is valid, PNG, operation PromoteApproved, in Approved, with a successful matching promotion attempt.
- The promotion attempt's input, the promoted revision's SourceRevisionId, and the workflow's selected upstream result agree. Source and promoted SHA/length must agree: this is a byte-preserving copy, not a new image approval.
- Follow that immediate source to the **existing approving human Revision review** of exactly those bytes (normally Trim; an approved enhancement/background/manual result if KeepOriginalExtent lawfully bypassed Trim). Validate the selected step/skip path and lineage. Do not skip over a transformed unreviewed revision to find any convenient older approval.
- The promoted revision may have ReviewState.NotReviewed and no review of its own. That is expected; record the inherited upstream ReviewId and the promotion lineage. No fabricated approval or second PrintOutput is introduced.

**Current exception, confirmed in source and existing test source:** Import → ConfirmOriginal → skip Enhancement/BackgroundRemoval → KeepOriginalExtent → ApprovedPngExport can succeed for a PNG root. `ConfirmOriginal` records no human ReviewDecision for PrepareAsset, `KeepOriginalExtent` checks an Approved step (which can be Import), and promotion's step becomes Approved by construction. `KeepOriginalExtentPersistenceTests.Decision_survives_restart_and_downstream_consumes_original` exercises this route; it was read, not run.

Proposed handling: `ApprovalEvidenceMissing`, no external save/Open. Do not falsely describe this as a rejected image or silently grant approval from the terminal step. Ordinary reviewed PNGs remain supported. Adding a final human review or changing this route would change the approval contract and is **not** authorized in 11144/11145 implementation by this design. The owner must either accept this temporary fail-closed limitation or authorize a separate approval-contract correction before full route-wide delivery acceptance. A legacy non-PNG byte-preserving promotion must likewise never be relabelled PNG by extension.

### 3.4 Stable identity during async operations

Reuse and widen the existing per-session gate for delivery and every source-authority mutation path, including approval/rejection, return/reset, AddAnotherSize, completion/retention, and recovery commits. Audit alternate entries such as error-details commands and producing-step completion; a new export-only mutex is insufficient. Entry methods acquire once and call under-gate cores so Complete → retention cannot reacquire/deadlock. Stop signaling stays outside the gate. Do not hold a SQLite transaction over file I/O, and do not acquire the external automation lease for export.

Export holds the gate from authoritative resolution until delivery commit/reconciliation result. Existing in-flight production on that session must finish or refuse the request before this boundary; it cannot later commit stale authority across it. Other sessions may continue. At the short prepublication and delivery transactions recheck the exact authority predicate; before publication recheck source handle identity/hash and destination containment. This is a proposed coordination extension, not a claim about existing locking.

The current single-instance startup guard is a prerequisite, not an export feature. Recovery and export are on demand for the selected session/request; no external folders are traversed during application startup. Cross-process writers are handled by no-overwrite filesystem primitives, not the in-memory gate.

## 4. Proposed persistence and remembered location

Use two small tables, keeping the immutable delivered fact separate from retry-specific ownership fields. Both are in the existing database, through a sibling repository; do not stuff delivery data into SessionMutation or Settings JSON.

| Record | Required fields and constraints |
|---|---|
| `ArtifactDelivery` | DeliveryId; unique RequestId; monotonic IntentOrdinal; SessionId FK; Kind; exactly one PNG RevisionId / TIFF PrintOutputId FK; approval ReviewId FK plus approved subject and lineage snapshot; approved SHA-256 and byte length; requested folder/filename; normalized resolved folder and final path; volume and directory identity; logical destination key; optional ReplacementOfDeliveryId self-FK; status Pending/Delivered; CreatedAtUtc; nullable VerifiedAtUtc, verified hash/length, final file identity, winning AttemptId. Delivered requires all verification fields and matching approved hash/length. Successful evidence and source/destination tuple become immutable. No cascading deletes. |
| `DeliveryAttempt` | AttemptId; DeliveryId FK; increasing attempt number unique per delivery; state Intent/Staging/ReadyToPublish/Delivered/Failed/Cancelled/NeedsReconciliation; exact random staging basename; held directory identity; staging file identity and creation time once known; expected hash/length; stage verification time; last bounded failure code; started/finished times. ReadyToPublish requires durable ownership + verified stage metadata. At most one active attempt per delivery and per canonical final destination, enforced by partial unique indexes/transaction checks. |

File identity means resolved volume identity plus filesystem file ID and creation time, not a path string. Retain current hash/length verification as well; file IDs are not cryptographic provenance. All references are checked in the same short transaction; use discriminator CHECK constraints and real nullable FKs, not an unchecked string that might mean either table. Review subject/lineage consistency is an application predicate rechecked transactionally. Persist enums by stable names, timestamps as UTC, exact SHA-256, and size metadata without rounding into identity.

`RequestId` binds one immutable source approval and destination selection. Reusing it with different contents is `RequestConflict`. Multiple ordinary request IDs for the same artifact/approval + physical directory + normalized leaf are resolved to the same existing delivery (the newest explicit replacement generation, if present) under a transaction, with one active attempt; case/8.3/drive aliases cannot create separate active requests. The logical key is indexed but not globally unique: the typed replacement operation in §6 alone can create a new generation at the same path. A filtered unique constraint on non-null ReplacementOfDeliveryId permits at most one direct successor to each historical delivery. Concurrent/repeated confirmations coalesce to that successor instead of creating siblings. The active-destination constraint is global across sessions.

**Preference proposal:** add `SettingKey.LastSuccessfulDeliveryDestination`, separate from DefaultOutputRoot. Store a versioned value containing display/resolved folder, directory/volume identity and IntentOrdinal. It is a workstation-database operator preference, like the existing settings, not a new login/account feature. No customer's folder is seeded by a migration.

The delivery repository marks Delivered and updates this single Setting row in **one SQLite transaction**. Use an internal connection/transaction-aware setting write; do not call `ISettingsRepository.UpsertAsync` afterward on a second transaction. A failed/cancelled/colliding save or merely opening a picker changes no preference. An idempotent read/Open changes no preference. Compare IntentOrdinal so reconciliation of an older request cannot replace the folder of a newer successful save. A failed setting write rolls back the Delivered transition too and leaves reconciliation possible.

Read the preference through the existing settings seam. Show it before confirmation and validate it again. Invalid/unavailable settings are not silently replaced by another folder. First use: empty destination with “Choose a folder” and a disabled save action; the operator selects once. Cancelling the picker preserves the displayed draft, approval and previous preference. Never default to the internal Approved root, source directory, a guessed customer folder or an invisible OS dialog history value.

**Migration proposal:** one additive next-version embedded migration (0018 only if still next at implementation), plus stable settings mapping. Empty tables and absent setting preserve existing installations. No rewriting approvals, old outputs or old delivery history; no backfilled Saved state. Existing MigrationRunner transaction rolls back a failed migration/version/audit row and refuses startup with the persistence error. After a successful migration, an older binary rejects the newer schema; rollback means a validated pre-upgrade database backup under separately authorized release procedures, never deleting tables or editing user_version. No migration was created/executed here.

Retain delivery history with its session/output evidence; Recent Processing hiding and Working cleanup neither remove deliveries nor external files. No expiry for successful delivery rows in this scope. Failed attempts may retain their exact temp ownership metadata for on-demand cleanup; no directory sweeping, automatic external-file deletion, or new retention policy.

## 5. External path and publication protocol

### 5.1 Supported paths and filename

**Proposed initial support envelope, requiring owner agreement:** ordinary drive-letter folders on local fixed or removable **NTFS** volumes, with stable handle-based identities, no reparse ancestry and no case-sensitive directory mode. No UNC/mapped network drive, cloud placeholder/sync-root reparse traversal, FAT/exFAT, device namespace, alternate data stream or unresolved SUBST path. A disconnected supported drive is a retryable failure. This is a proposed implementation limit, not an existing Jira restriction; broader destination support requires proving the same contract, not weakening it.

Use a Windows-specific adapter. Normalize absolute paths, resolve an existing directory through a handle, compare volume identity and path **segments** case-insensitively after canonicalization, and reject reparse points at every existing ancestor. Resolve 8.3 aliases through handles; reject aliases that cannot be unambiguously resolved. Directory IDs detect replacement; volume IDs detect reuse of a drive letter. Revalidate at open, before publication and on retry. A lexical prefix such as `C:\Jobs` must not classify `C:\Jobs2` as its child.

The protected set is the actual configured managed workspace root (all sessions and all areas, including Source/Working/Approved/Rejected/Logs/Revisions), plus application-owned database/log/evidence roots supplied by composition. It is not just this session's Approved folder. Resolve protected-root aliases too. The imported original's exact path and any identifiable alias/hardlink are protected; saving a new, different filename beside an original is allowed if the folder is otherwise safe. Never create or truncate a final path to test writability. Existing final files, including hardlinks, are never opened for write.

Hold directory/ancestor handles with sharing that prevents rename/reparse substitution for the protected operation, and perform publication relative to the verified directory/held staged object. If the adapter cannot establish these protections, return UnsupportedDestination rather than using a weaker path-only fallback. This is a narrowly scoped native adapter, not a general filesystem abstraction. Handle-sharing and rename combinations require synthetic Windows adapter verification before implementation acceptance.

Filename editor starts with the exact approved artifact's suggested filename, without renaming Session.OutputName or the managed file. Present the full effective name before activation. Accept `.png` for PNG and `.tif`/`.tiff` for TIFF; missing extension is appended visibly before confirmation; a wrong extension is refused, never converted. Reject separators, control characters, ADS/colon, traversal/dot-only names, Windows reserved device basenames (including before a dot), trailing dot/space, and path-length overflow. Reuse OutputName validation where it applies, but its 80-character stem/sanitizer is not a complete external-leaf validator. Do not silently sanitize operator-entered names. A bounded `(2)`, `(3)` suggestion preserves the extension and length budget; it is a suggestion, not a reservation or automatic acceptance.

### 5.2 Ordered operation

1. Capture an immutable request/selection version. Acquire the per-session coordination gate. Resolve exact approval/lineage and source, validate destination/name, and first resolve repeat/recovery cases (§6). Reject before copying if any authority fails.
2. Open and retain the approved source for read with no write/delete sharing. Hash and measure its actual bytes. Resolve/hold the destination directory and protected-root guards. A precheck for an existing final file can return a suggestion early but cannot reserve the name.
3. Persist delivery intent and an attempt ID/random `.printflow-{attempt-id}.partial` staging leaf before creation. Create staging with create-new semantics in the **destination directory**. Capture its volume/file identity and creation time from the live handle and persist these before copying. If this metadata write fails, delete only the still-held, proven newly created temp. A crash before identity persistence may leave an unowned temp; never infer ownership from its name alone or sweep it.
4. Stream unchanged bytes from the held source into the held staging object, honoring cancellation. Flush data to disk. Rewind/re-read the staged bytes independently, verifying length and SHA-256 against approval. No image decode/resave, conversion, TIFF generation or production adapter call occurs here. Do not publish incomplete or mismatched bytes.
5. Recheck authority, source and directory safety; persist `ReadyToPublish` with staging identity/hash/length and final destination. This is the **durable before-publication checkpoint**. If its commit is uncertain, read it back before proceeding. No verified checkpoint, no publication.
6. Last cancellable boundary. Once publication begins, finish a short non-cancellable verification/persistence/reconciliation phase. Publish the held staging object by same-directory, same-volume rename with **replace disabled**. Another writer winning the final name returns Collision; even equal content is never overwritten or adopted. Only an existing journal-owned publication may enter the recovery path.
7. Open the **final destination name** read-only while retaining ownership/directory guards. Confirm it is the exact staged file object; remeasure and rehash through that final-name handle. Flush publication metadata where the selected Windows primitive supports it. A successful copy or rename alone is insufficient.
8. In a short SQLite transaction, recheck source eligibility and attempt state, mark the delivery verified with actual final path, object identity, hash/length and time, and update the destination preference (§4). If the commit outcome is unknown, read back by RequestId/AttemptId while holding guards. Return `Delivered` only when both final-name verification and durable commit are established. Hold the final handle against modification through this point.

Use the existing hashing/flush/no-overwrite patterns, but do not reuse the internal reservation primitive or its “existing file with matching hash” shortcut as external ownership proof. Proposed Windows publication uses `SetFileInformationByHandle`/`FILE_RENAME_INFO` with ReplaceIfExists false; the exact wrapper must be verified, not assumed from `File.Move` alone. Microsoft's [rename contract](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_rename_info) specifies refusal of an existing target when replacement is disabled.

Data flushes reduce the OS-cache interruption window; they do not make filesystem + SQLite a joint transaction or promise survival against faulty storage. The protocol guarantees that PrintFlow itself publishes only a complete verified staged file, and never exposes a streaming partial under the final name. A failure after publication can leave a complete file requiring reconciliation. External corruption/device loss after verification cannot be undone safely; report uncertainty rather than delete that final file. This is the realistic boundary of 11144 AC4, not permission to weaken prepublication verification. See Microsoft's [file caching guidance](https://learn.microsoft.com/en-us/windows/win32/fileio/file-caching).

### 5.3 Cancellation and cleanup

| Boundary | Result and allowed action |
|---|---|
| Before approval | Cancel the UI operation; no export. If approval commit is already in progress, refresh its durable result rather than assuming cancellation undid it. |
| After approval, before publication | Approved remains. Cancel stops staging/copy and removes only an exact, attempt-owned temp, using identity/handle proof. Cleanup failure is a named residual temp and retryable failure; no final partial exists. |
| At rename boundary | Cancellation is linearized before the rename or treated as too late. Never throw Cancelled after a publication that may have succeeded. |
| After rename, before durable Delivered | Show “Finishing save” / “正在完成保存”; finish final verification/commit with an internal token. If interrupted or storage fails, return NeedsReconciliation, not Saved or a guarantee that no file exists. |
| After Delivered | Cancellation cannot undo delivery. Never delete the delivered file as cancellation cleanup. |

No general final-file rollback/delete is allowed. In-process staging failures may delete the still-owned staging object; after restart deletion requires durable attempt ownership and matching object identity. A mismatched, foreign or uncertain temp is left untouched and named in the failure. On-demand reconciliation examines only the recorded staging/final entries in their verified directory, never scans a customer tree.

## 6. Interruption, collisions and idempotency

Same delivery means the same artifact key, approved content/approval evidence, resolved physical folder and normalized final leaf, associated with a durable request. It does not mean “same filename”, “hash happens to match”, or “a delivery row exists”. A new destination or an explicitly accepted new filename is a new delivery request; it copies only the same still-eligible approved bytes.

| Durable state and current observation | Required behavior |
|---|---|
| Intent; no recorded staged file identity | No publication was permitted. Leave any same-named unproved temp alone. Create a fresh attempt/temp only after checking final absence and reconciling active intent. |
| Staging; exact owned temp; final absent | Verify or discard only the owned temp. Incomplete data is never published. A new attempt may copy from the approved source; no processing. |
| ReadyToPublish; owned verified temp; final absent | Revalidate source, directory, hash/length and ownership; resume the same rename. Do not create another copy. |
| ReadyToPublish; temp absent; final is the recorded staged object and hashes correctly | The rename completed before metadata commit. Recheck eligibility and finalize this same DeliveryId/preference. No copy or new name. |
| ReadyToPublish; both names present, wrong object, unreadable final or uncertain volume | NeedsReconciliation/Collision/Unavailable as appropriate. Do not infer ownership from hash or filename, delete either file, or silently choose a suffix. User may reconnect/retry or explicitly choose another destination with an uncertainty warning. |
| Delivered row; exact current final object, hash/length and source eligibility verified | Return AlreadyDelivered with the existing evidence and a fresh availability timestamp; zero copies, reviews or production calls. Double-click/in-flight duplicates coalesce on the same request. |
| Delivered row; final missing | Historical delivery stays. Return DeliveredFileMissing, no automatic recopy. Offer an explicit new save of the still-approved result (including reuse of the now-absent name after recheck), creating a new delivery generation. |
| Delivered row; final replaced/changed | Historical delivery stays. Current file is not trusted even if only metadata/identity changed and bytes happen to match. Never overwrite; offer another name/folder. |
| Delivered row; volume/folder unavailable | Historical evidence remains; availability Unknown/Unavailable. Reconnect or explicitly choose a different location. No optimistic Saved/current-file claim and no silent second copy. |
| Any row; source now ineligible | No direct save or Open action. Preserve historical evidence but do not treat it as current export permission. |

Publication recovery proof combines the **durably recorded prepublication attempt**, exact directory/volume identity, staged object identity/creation time, expected rename relationship, final absence/presence observations, and a fresh final hash/length. An old successful delivery record alone proves no present availability. NTFS IDs may eventually be reused; a creation-time mismatch, restored/cloned volume, contradictory temp/final pair or other ambiguous identity is refused. No claim is made against an administrator deliberately forging all metadata; no signing or forensic subsystem is proposed. [Windows file identity](https://learn.microsoft.com/en-us/windows/win32/api/winbase/ns-winbase-file_id_info) and [resolved handle paths](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getfinalpathnamebyhandlew) supply the primitive evidence, not an everlasting ownership certificate.

The adapter must distinguish absent from inaccessible. It must never translate a read/access error into “does not exist”. Collision suggestions are calculated from current observations, returned with the attempted filename, and require an explicit operator selection and save. A race taking the suggestion triggers another collision; no auto-renumber loop or overwrite prompt.

Retry always first reconciles the previous attempt. A DB error after rename is not an ordinary “copy failed, try a new filename” condition. If the DB committed but acknowledgement was lost, readback recovers the same delivery; a second request must not create a second file. If the database is unavailable, keep the request identity/visible destination and say save completion cannot yet be verified. No background retry starts processing or new external copies.

**Explicit replacement of a missing delivered copy:** ordinary DeliverAsync cannot bypass coalescing by supplying another RequestId. `ReplaceMissingAsync` is a separate typed intent carrying a new RequestId, prior DeliveryId, the Missing observation returned by CheckDeliveredFileAsync, and the displayed selection version. It fixes the source/approval and destination to that prior delivery; changing the name/folder uses an ordinary new-destination request instead. Under the session/destination coordination boundary, reload the prior delivered row and validate the request/observation's prior identity. **First look up an existing linked successor or matching RequestId.** If one exists, apply its ordinary eligibility/current-file verification and reconciliation rules, returning its AlreadyDelivered, NeedsReconciliation or failure outcome; do not require the predecessor path to be absent and do not classify the successor's own verified publication as a foreign collision. Only when no successor exists, recheck current source eligibility and the original directory/volume identity and freshly prove the final path is absent (not inaccessible). The observation is an expectation, not authority. In a short transaction, recheck successor/active-attempt absence and create the sole linked successor and its attempt; a concurrent winner is loaded and handled by the existing-successor branch. Repeated same or different request IDs for this prior delivery therefore return that same successor. A replacement of the successor later requires a new explicit Missing observation of the successor. The ordinary no-overwrite protocol still arbitrates a file appearing after the absence check. Never mutate the predecessor's historical evidence, replace an existing file, or interpret a click on Retry/Reconcile as this new-copy consent.

## 7. SCRUM-11145 consumer and service contract

### 7.1 Proposed interfaces and boundaries

Existing style is `Task<OperationResult<T>>` with CancellationToken and typed failure details. Preserve that style; business outcomes such as Collision and NeedsReconciliation are discriminated delivery outcomes, not unhandled exceptions or fabricated production failures.

| Proposed seam | Request/result responsibilities |
|---|---|
| `GetDeliveryStateAsync(SessionId, ArtifactKey?, token)` | Metadata-only query for exact persisted deliveries/attempts after load or restart. Returns DeliveryId, original RequestId, full ArtifactKey/approval identity, immutable requested/resolved destination, status, latest attempt/state/failure, verification time, replacement links and an opaque state version. Optional ArtifactKey filters by exact ID; session query groups by artifact. Historical availability starts NotChecked, never VerifiedNow from the row alone. No folder scanning or startup reconciliation. |
| `GetOfferAsync(ArtifactKey, token)` | Reload authority; return eligible artifact/display metadata or typed ineligibility. No permission to copy just because an offer was returned. |
| `DeliverAsync(DeliveryRequest, progress, token)` | RequestId, exact ArtifactKey/expected SHA and approval reference, filename/folder, selection version. Returns Delivered/AlreadyDelivered with DeliveryId and actual verified path; NotDelivered/Cancelled; Collision with suggestion; Failed; or NeedsReconciliation. Echo request/key/version on all events/results. Caller-supplied hash/approval fields are expectations, verified against the repository. |
| `ReconcileAsync(DeliveryId, progress, token)` | Operates on recorded paths/attempt only, rechecks eligibility and files, and completes interrupted evidence where justified. No reapproval or production. |
| `CheckDeliveredFileAsync(DeliveryId, token)` | Returns historical evidence plus current availability: VerifiedNow, Missing, Changed, Unavailable, Ineligible or Uncertain; includes exact delivery/state identity and observation time. Fresh exact-file hash/object check; no continuous monitoring. A Missing observation is required input to explicit replacement but must be rechecked there. |
| `ReplaceMissingAsync(MissingDeliveryReplacementRequest, progress, token)` | Explicit new-copy intent described in §6, linked to a prior verified DeliveryId and freshly revalidated Missing observation. Returns the same delivery outcome union, with the canonical successor DeliveryId and echoed invocation RequestId/version. Ordinary retry never invokes it. |
| `AcquireDeliveredSelectionAsync(DeliveryId, token)` | Recheck eligibility and final-file identity/hash and return a short-lived, non-serializable disposable selection lease, or a typed availability failure. Lease holds session/directory/file guards through immediate shell dispatch and carries the actual final path; a plain earlier Check result is not a reusable shell authorization. |
| `IDeliveryRepository` | Resolve/coalesce immutable requests; persist attempt checkpoints; compare-and-set terminal outcome and preference transaction. SQL hidden from Workflow/App. |
| `IDeliveryFileSystem` | Open verified source/destination handles; create owned staging; copy/flush/hash; no-replace publish; final verify; bounded ownership cleanup. No approval decisions or naming UI. |
| App `IDeliveryFolderPicker` | Owned folder dialog, returns selection or cancellation; cannot create an approval/save. |
| App `IDeliveredFileShell` | Consumes a live delivered-selection lease once, opens parent and selects the exact file; caller disposes the lease immediately in finally. Expired/disposed selections fail. Typed missing/unavailable/shell failure; no command-string concatenation from names. |

Progress: Validating, Copying(bytes/total), Verifying, Publishing, Recording, Reconciling. Only prepublication phases are cancellable. Dispatcher marshaling follows existing ViewModel patterns. Proposed error categories: ApprovalFailed, StaleReviewTarget, ApprovalEvidenceMissing, IneligibleArtifact(reason), SourceMissing/Changed, InvalidName, ProtectedDestination, UnsupportedDestination, DestinationUnavailable/NotWritable, Collision, CopyFailed, VerificationFailed, PersistenceFailed, NeedsReconciliation, RequestConflict, DeliveredFileMissing/Changed, ShellFailed. Localized plain reason + safe next action are separate from technical details. Retryable does not imply automatic retry.

### 7.2 Final-review/save coordinator

The delivery service accepts **already approved** artifacts only. A proposed final-save coordinator in the Workflow application layer wraps existing lawful commands without adding approval to the delivery service or changing `WorkflowEngine.Approve` semantics. Its `FinalSaveRequest` contains an operation ID, exactly one of a pending `ReviewedResultIdentity(session, step, revision, displayedHash)` or an existing approved ArtifactKey/approval reference, the filename/folder draft and selection version. Its outcome carries ApprovalOutcome (not attempted/succeeded/failed/unknown), any exact promoted ArtifactKey, and the separate DeliveryOutcome. A PNG review identity and the later promoted artifact identity are different and must be explicitly linked in this result, never conflated:

1. UI captures `(session, step, revision, full displayed hash, selected artifact/size, destination draft version)` from the actual displayed review. Confirm result and save is a deliberate fresh activation, never a default Enter action or navigation side effect.
2. Validate the draft without exporting. Under the same session coordination gate, compare **revision ID and hash**, not hash alone, with the current review. Execute the existing `WorkflowCommand.Approve(step, displayedHash)` through an under-gate existing service core. This wrapper matters because the existing command payload itself carries only step/hash.
3. Publish an ApprovalSucceeded observation only after the approval commit is known. Reload and resolve the exact approved result linked to that review. If approval failed/was refused/has unknown commit outcome, no delivery until that outcome has been authoritatively resolved. Never fall through on an exception.
4. For TIFF, export the approved PrintOutput resolved from that exact reviewed revision. For PNG final Trim review, the existing internal ApprovedPngExport still has to run **once** after approval; the explicit combined action may continue only that byte-preserving internal promotion, bound to the approved source. It may not start enhancement, trim or another processor. Re-resolve the resulting promoted Revision and verify equality/lineage before external delivery. This is ordinary managed promotion, visibly separate from external save.
5. If internal PNG promotion fails, report “Approved; PNG preparation did not finish” and return to its existing explicit preparation retry; no external delivery was attempted. If external delivery fails after a PNG exists, retries use that exact promoted Revision and do not rerun promotion. Existing already-promoted asset sessions use the approved-only save path. The missing-human-review exception in §3.3 cannot use this coordinator to invent a new approval.
6. On external failure/cancel/collision retain ApprovalSucceeded and the ArtifactKey. A Retry save calls delivery/reconciliation only. It never calls WorkflowCommand.Retry, Approve, StartStep(PhotoshopOutput), Complete, or any processor. Approval and delivery results remain separately observable even if presented in one interaction.

Existing Complete remains a separate lawful workflow command; export does not automatically complete the session, and Completed never implies Saved. If approval succeeds but the user cancels before save, the screen truthfully shows Approved, not saved. A subsequent explicit save needs no new approval.

**Reload/restart:** when the session or selected output is loaded, call GetDeliveryStateAsync for its exact key, or the session grouping before final PNG resolution. Render prior deliveries with their original destinations and dates; they are not claims about the new draft/default. Present unresolved attempts as “Check this save again” bound to their persisted DeliveryId. Present delivered history as “Saved previously; location not checked” until a user-triggered Check/Open verifies availability. Choosing Resume/Reconcile displays and freezes the recorded destination; it never reconstructs it from the current preference. A different newly chosen destination is an explicit new save, with a warning if an earlier attempt is unresolved. Session/size changes select another group and cannot relabel a prior row. Metadata query failure reports history unavailable; it must not masquerade as no prior deliveries. This query restores access to history without a global startup recovery service.

Freeze target, name and destination editing from activation through the approval/save attempt. Allow cancellation where meaningful; switching output/session waits for cancellation/terminal outcome or detaches presentation using a new selection generation. Late progress/results are stored against their original key, never displayed as success for the new selection. Each valid TIFF size has its own delivery presentation. A requested change of folder/name after failure creates a new immutable request; it does not retarget an in-flight request.

Reuse `ReviewApprovalButton`'s fresh key/mouse gesture protection and exact `ReviewTargetIdentity`, plus the existing non-activating initial focus. The combined button's input identity also includes the destination draft generation so a held gesture cannot consent to newly changed save details. For already-approved results, use a non-null save-target identity instead of making the approval control silently nonfunctional. Do not weaken focus, stale-release, repeating-key or double-click protection, or infer acceptance from historical Wave1A tests.

### 7.3 State/action table and bilingual copy

| State | Operator-visible English / zh-CN | Actions/constraints |
|---|---|---|
| Generated / awaiting review | Result generated; waiting for your review. / 结果已生成，等待你审核。 | Show draft filename/folder, PNG or exact TIFF size. Explicit Confirm result and save only for the lawful final review. No direct export/Open. |
| No selected destination | Choose a folder before saving. / 保存前请选择文件夹。 | Choose folder and edit filename; no guessed default. Approval-only existing action remains governed by existing rules. |
| Approving | Confirming this result… / 正在确认此结果… | Separate approval phase; fields frozen; no export until success. |
| Approval failed | Result not approved: {reason}. / 结果尚未通过审核：{原因}。 | Refresh exact review, allow lawful review action; zero export. |
| Approved / not delivered | Approved, but not saved to your folder yet. / 已通过审核，尚未保存到你选择的文件夹。 | Save approved result / 保存已审核结果; name/location editable; no reapproval. |
| Delivering | Saving to {folder}… / 正在保存到 {文件夹}… | Show captured target; Cancel before publication only. |
| Verifying/recording | Finishing save… / 正在完成保存… | No cancelling publication; no Saved claim yet. |
| Verified delivery | Saved: {filename} in {folder}. / 已保存：{文件名}，位置：{文件夹}。 | Actual delivery fields, Open containing folder / 打开所在文件夹. |
| Failed/cancelled | Approved, but not saved: {reason}. Nothing needs processing again. / 已通过审核，但尚未保存：{原因}。无需重新处理图片。 | Retry save / 重试保存; change location/name. For uncertain publication use next row, not this categorical claim. |
| Needs reconciliation | A file may have been saved, but completion is not verified. Check this save again. / 文件可能已保存，但尚未核实完成。请重试核实此次保存。 | Reconcile same request; no optimistic Open or automatic extra copy. |
| Collision | This name already exists. Use {suggestion} or choose another folder. / 此文件名已存在。可使用 {建议名称} 或选择其他文件夹。 | Explicit Use suggested name followed by save; never overwrite. |
| Ineligible | This result cannot be saved: {reason}. / 此结果不能保存：{原因}。 | No direct save/Open. Missing review evidence says so, without claiming rejection. |
| Historical copy unavailable | Saved previously; the file is now missing/changed/unavailable. / 此前已保存；文件现已找不到、已更改或所在位置不可用。 | Preserve history, reconnect/check again, or explicitly save another copy if source remains eligible. |

Asset summary: `Save as: logo.png · PNG · Folder: …` / `另存为：logo.png · PNG · 文件夹：…`; no size or TIFF wording. TIFF summary: `Save as: design.tif · Print TIFF · {actual width} × {actual height} mm · Folder: …` / `另存为：design.tif · 打印 TIFF · {实际宽} × {实际高} 毫米 · 文件夹：…`. Long paths must be inspectable/copyable with accessible full text and wrapping; errors and state never rely on colour. Change location and the filename editor are keyboard reachable; preserve ordinary Tab order and do not auto-focus an approving action.

### 7.4 Open containing folder

Open takes a DeliveryId, never an editable draft or Session.TiffOutputPath. AcquireDeliveredSelectionAsync rechecks exact artifact eligibility and current final path/object/hash immediately before invoking the App shell port; hold its disposable selection lease through shell dispatch. Use `SHOpenFolderAndSelectItems` with a parsed absolute item ID on the appropriate COM/UI thread, without edit mode; Microsoft's [shell API](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shopenfolderandselectitems) selects the specified item. The port reports dispatch failure distinctly from file availability and never opens the image itself.

If the external file moved, changed, disappeared, or is on an unavailable volume, do not substitute an earlier size, the managed Approved copy, or a folder-only “success”. Show the last recorded path as history and the truthful availability reason. No drive-wide search or automatic relinking. Repeated clicks recheck/select only; zero approval/copy/production work. A later external move after dispatch is outside the point-in-time guarantee, not something a background watcher promises to prevent.

## 8. Original AC coverage and future evidence

All AC remain requirements. The mappings below are a design assessment, **not implementation/test PASS**. Full current descriptions were read through authenticated Jira; summaries below do not replace that source. The two owner decisions affecting scope are explicit in §10.

| Jira AC | Preserved requirement | Design / future evidence |
|---|---|---|
| 11144-1 | Eligible approved output arrives with equal hash and a recorded delivery path/hash. | §§3–5; synthetic PNG and TIFF through real temporary DB/filesystem; durable record and final bytes. |
| 11144-2 | Existing filename never overwritten; collision-safe alternative returned. | §§5.1, 6; existing foreign file including equal bytes, and a writer winning between check and rename. Explicit acceptance. |
| 11144-3 | Refuse workspace/protected original/snapshot destination. | §5.1; all managed areas, sibling-prefix negative control, alias/reparse and original hardlink checks. |
| 11144-4 | Cancel/read-only/disconnect/copy/verification failure leaves approved source intact, no partial final, and processing-free retry. | §§5.2–6; fail before publication, at boundary and after rename separately; complete-but-unrecorded is not a partial file or Saved. Unsupported storage proposal requires owner agreement. |
| 11144-5 | Waiting/rejected/invalidated/recycled/obsolete outputs refused. | §3; both kinds, exact source mutation, sibling sizes retained, missing inherited review evidence visibly refused. |
| 11144-6 | Same delivery twice does not copy or process again. | §6; same/different RequestIds, restart, concurrent double activation, current-file verification; explicit new copy is distinct consent. |
| 11144-7 | Delivered only after final-destination verification. | §5.2 steps 7–8; fault final read/hash or persistence; no success on copy/rename alone. |
| 11145-1 | Asset final step shows PNG name/destination, no TIFF/print size. | §7; promoted PNG and final-Trim chain ViewModel cases, plus §3.3 exception. |
| 11145-2 | TIFF final step shows filename/type/physical mm/destination. | §§3.2, 7; output-bound dimensions and older valid size despite another pending size. |
| 11145-3 | Accepted displayed destination requires no forced dialog; Approved precedes verified Saved. | §7.2; fake picker count zero, ordered approval/export events, no Saved on intermediate progress. |
| 11145-4 | Failure/cancel/collision shows approval separately, reason/suggestion and retry without processing. | §§5–7; retry does not call engine approval/processing/promotion after external delivery failure. Uncertain postpublication wording is distinct. |
| 11145-5 | Open selects delivered file; repeat starts no production. | §7.4; fake shell receives exact verified path; separate future human Explorer selection check. |
| 11145-6 | Next image visibly defaults to last successfully used destination. | §4; picker/failure do not update it; atomic save; older reconciled intent cannot supersede newer success. |
| 11145-7 | No direct pending/ineligible export/Open; lawful exact approval may precede export; failed approval prevents export; delivery failure does not regenerate. | §§3, 7.2; stale revision with same hash, failed/uncertain approval commit, exact PNG promotion linkage, TIFF and invalidated source tests. |
| 11145-8 | en/zh-CN reachable, unclipped, readable without colour. | §7.3; focused binding/resource/state/Tab tests and later human supported-workstation visual/input evidence. Historical Wave1A acceptance does not cover new controls. |

Smallest meaningful future verification groups (use GUID temporary folders/databases and synthetic images; no customer data):

1. **Identity/contract:** normal reviewed PNG through promotion; root-without-review refusal; TIFF twin-review authority; multiple valid sizes; pending/rejected/recycled/invalidated and source hash mismatch. Assert exact source bytes and zero processor calls during delivery/retry.
2. **Publication/filesystem:** successful copy/final verification; collision including equal-hash foreign file and check/rename race; managed-root/original/alias protections; staging copy/cancel/hash failure; final-name verification mismatch. Use the real Windows adapter for rename/identity/sharing behavior; fakes alone cannot prove it.
3. **Crash/persistence:** fault each materially distinct durable boundary: intent/temp ownership; ready checkpoint; rename before delivery commit; unknown commit acknowledgement; record/preference rollback. Reopen the temp DB and same temp destination, discover the persisted request through GetDeliveryStateAsync, and reconcile with no extra copy even if the remembered default now differs. Include changed/missing/unavailable final and false ownership; ordinary retry of Missing creates nothing, explicit replacement creates one successor, repeated/concurrent confirmations coalesce, and a file appearing before publication is a collision. Simulated disconnection/access failure is separate from a later removable-device check.
4. **Concurrency:** same request twice; competing name writer; same-session invalidation/retention serialized with copy; stale authority recheck; different-size selection cannot receive old completion. Assert no deadlock with Complete → retention and stop signaling.
5. **Consumer:** approval succeeds before export, approval failure/uncertainty exports nothing, cancellation after approval preserves it, retry calls only delivery, PNG promotion once, correct type/mm, first-use/picker cancel/last-success preference, explicit collision name acceptance, late event filtering, restart discovers exact historical/unresolved destinations, shell fake gets actual delivered target through a live selection lease and all success/failure paths dispose it.

A schema/gate change justifies focused existing migration/round-trip/settings-authority, retention/AddAnotherSize and affected session-command regressions. It does not justify rerunning the full historical suite after each slice. Native/human Explorer, bilingual supported resolution/scaling (existing baseline 1920×1080 display, 1920×1040 work area, 96 DPI), and fresh keyboard/mouse behavior remain separate future acceptance. **All tests/builds/native checks for this design are NOT RUN**, intentionally. No Wave1A probe or 11146 re-review is part of this design.

## 9. Future decomposition and routing evidence

After owner review and a separate implementation authorization:

1. Resolve §10 compatibility/scope decisions; turn the accepted design into a bounded implementation plan. No automatic next phase.
2. **11144 backend/persistence first:** artifact resolver, session coordination extension, additive persistence/preference transaction, Windows publication/recovery adapter and focused synthetic tests. Normal route `gpt-6-sol/high`; escalate a concrete critical recovery/path-safety issue to `gpt-6-astra/high` if needed. Do not mix these units with UI implementation.
3. **11145 integration next:** final-save coordinator, App picker/shell ports, exact-target state/controls and bilingual copy; `gpt-6-astra/high` for UI/interaction, with clearly separable backend service wiring at Sol High. Focused tests then separately authorized native/human acceptance. Approval-engine changes, if any, require their own accepted scope.
4. Proportionate fresh independent review against original AC/current candidate. No implementation, migration, release, Jira mutation, commit, push or deployment is authorized by the above sequence.

Policy loaded: `C:/Users/admin/.codex/workflows/development-routing.md`, **v2.3**, from the approved local installation (CODEX_HOME was not exported; default installed home was verified). `ROUTE_PROFILE.route_offset: 0` is explicit and inherited by the reviewer. Host: local Windows checkout above. Context for investigation/design is CONTINUE; only independent review is FRESH_REQUIRED.

| Boundary | NormalRoute / RequestedRoute | ExecutionTarget / ActualRoute | Adjustment and reason |
|---|---|---|---|
| Cross-module safety + interaction design | gpt-6-astra/high / same | Available current root / **gpt-6-astra/xhigh** | UNCHANGED offset; runtime turn_context for task 01a0cbfa-a0a2-7bd2-9b97-102b9f735905 exposes xhigh. This was the pre-existing root setting, not a requested escalation. In-place switch unavailable: MODEL_SWITCH_UNAVAILABLE. Safe stronger root used, without claiming High ran. |
| Post-architecture related resolution | Sol High if separable persistence-only work; Astra High for combined interaction/safety corrections | Current root until the natural independent-review boundary | Re-evaluated; no artificial context split solely to relabel effort/model. Any root fallback remains disclosed above. |
| Independent safety design review | gpt-6-astra/high / same | Requested fresh native reviewer; actual recorded in DELIVERY_DESIGN_REVIEW.md | FRESH_REQUIRED, fork_turns=none, original requirements/design/direct source only. Scope warrants data-safety/recovery capability. |
| Final documentation/integrity | gpt-6-sol/medium / same | Current root safe fallback unless a real switch is available | MODEL_SWITCH_UNAVAILABLE; no claimed downgrade, configuration edit or tiny model-label-only agent. |

Skills applied: using-superpowers and brainstorming for architectural design; requesting-code-review for fresh independent review; verification-before-completion for document/change-scope evidence. The explicit prompt overrides generic skill defaults for separate paragraph approvals, spec location, commits, test execution and implementation continuation.

## 10. Owner decisions and stopping condition

The owner reviews this coherent recommendation once. Settled filename visibility, remembered-success destination, explicit approval, no overwrite, retry, multi-size validity and Open behavior are not being asked again.

1. **Approve/revise the proposed architecture/storage:** two bounded delivery/attempt tables and a distinct last-success destination preference, with transactional evidence/preference and no changes to retention. This is technical design approval, not an existing owner decision or migration authorization.
2. **Destination support envelope:** accept the proposed first implementation restricted to verified local NTFS fixed/removable folders with the stated alias/reparse limits, or require network/cloud/non-NTFS support before implementation planning. Such support requires a revised adapter/recovery design and evidence; Jira is not silently narrowed.
3. **Unreviewed asset-path compatibility:** accept a visible fail-closed limitation for already generated PNGs lacking a human approval record, or authorize a separate approval-contract correction before full asset-route delivery acceptance. Recommended: correct that approval gap in a separately approved scope; do not grandfather it or fabricate reviews. Ordinary reviewed PNG/TIFF delivery design can stand, but route-wide acceptance cannot be claimed while the gap is unresolved.

Chinese owner summary:

建议使用现有 SQLite，增加独立交付记录/尝试记录和“上次成功保存位置”；先核对审核身份，再写目标文件夹中的临时文件，校验后以禁止覆盖方式发布，最终文件校验及记录提交均成功才显示“已保存”。发布后中断应核实同一次保存，不能自动另存一份。

请评审三点：存储与恢复方案；首版是否接受上述本地 NTFS 目录范围；“跳过处理并保留原始范围”得到的 PNG 若没有人工审核记录，应暂时禁止交付，还是另行批准修正审核流程。建议单独修正审核缺口，不能把旧状态当作人工批准。后续顺序为 11144 后端（Sol High）→ 11145 界面交互（Astra High），仍需独立实施授权。

Stop at **AWAITING_OWNER_REVIEW**. A favorable technical review approves neither these owner choices nor implementation. Existing Wave1A and 11146 human/native acceptance remains open under the preserved HANDOFF pointers.
