# SCRUM-11116 / SCRUM-11117 — Home and Recent Processing completion

| Item | Value |
| --- | --- |
| Date | 9 September 2026 |
| Repository | `D:\Repositories\printflow-Studio`, `master`, canonical checkout only |
| Jira authority | `C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv` (79 rows, original import) |
| Scope | SCRUM-11116 (CSV Work Item 11601), SCRUM-11117 (CSV 11602); parent SCRUM-11115 (CSV 11600) reassessed |
| Build | 0 warnings / 0 errors |
| Full suite | 11,632 passed / 0 failed / 0 skipped (accepted baseline 11,609 + 23 new tests) |
| Git | Local commits on `master` only. Nothing pushed, amended, rebased or deployed. |

---

## 1. Exact Jira acceptance criteria, quoted before any Product edit

The repository's phase documents use an internal numbering scheme; the Jira keys are a fixed
offset of the CSV `Work Item ID` (see §0 of the coverage re-audit). The rows below were read from
the original CSV, verbatim, before implementation.

### SCRUM-11116 — CSV Work Item **11601**, Task, parent 11600

> **Summary:** Complete the Home and Single-Image Drop Experience
>
> **Description:** Complete the operator-facing Home page with a clear one-image drop target,
> unsupported or multiple-file feedback, workflow-selection entry and Recent Processing access.
> The page must reflect the MVP one-image-at-a-time contract and avoid exposing internal
> architecture terminology.
>
> **Labels:** printflow, mvp, home, drag-drop, ux — **Priority:** High — **Points:** 3

### SCRUM-11117 — CSV Work Item **11602**, Task, parent 11600

> **Summary:** Implement Recent Processing with Resume and Record Management
>
> **Description:** Show the most recent 30 days or 100 Sessions, whichever limit is reached
> first, including thumbnail, display/output name, workflow, current step/state and timestamp
> plus supported resume and delete-record actions. Older records may disappear from the list
> without deleting approved production files; record management must respect active or
> interrupted processing safety.
>
> **Labels:** printflow, mvp, recent-processing, history, resume, retention — **Priority:** High
> — **Points:** 5

### Parent SCRUM-11115 — CSV Work Item **11600**, Epic

> **Summary:** Complete Operator UX, Localisation, Diagnostics and Offline Packaging
>
> **Description:** Complete the production-facing PrintFlow Studio experience with the confirmed
> Home/Drop, workflow, review, dimensions, TIFF review, Recent Processing, Settings/Environment
> Check and Error Details surfaces; simplified Chinese default and English switchable without
> restart; local-only logs and screenshots; explicit diagnostic-package export; and a repeatable
> versioned offline installer with no automatic updates. Operator-facing UI must use practical
> production terminology and hide internal implementation terms such as Revision, Session and
> Adapter while preserving stable internal English states and error codes.

Two supporting rows were also read, because they define what "supported input" means and neither
the historical audit nor the task brief could be trusted to state it:

> **CSV 11201 (SCRUM-11078):** Implement the Home/Drop entry flow for exactly one image. **Decode
> and preview the file before external automation**, save an InputSnapshot, show filename and
> editable output name, and present the three fixed workflow choices. Multiple files must be
> rejected clearly. …
>
> **CSV 11405 (SCRUM-11098):** Allow approved **PNG and JPG/JPEG** inputs to enter the Photoshop
> output flow… **CSV 11406:** Support **PSD** only when a Photoshop-compatible composite preview
> is available… **CSV 11407:** Support **single-page PDF**… Reject multi-page PDFs explicitly.

---

## 2. Pre-change matrix

Built by reading current source, not the historical audit. The audit's two claimed gaps were both
real; it also missed a third, larger one (there was no input-acceptance gate at all).

| AC clause | Current implementation before this slice | Already satisfied? | Evidence | Actual remaining gap |
| --- | --- | --- | --- | --- |
| **11116** clear one-image drop target | `HomeView.xaml` sets `AllowDrop` on the whole screen; `Home_ImportHint` states "One file per session — there is no batch import"; `Home.ChooseFile` button | **Yes** | `HomeAndWorkflowSelectionTests.One_dropped_file_is_accepted_and_starts_a_session` | none |
| **11116** multiple-file feedback | `HomeViewModel.DropFilesAsync` refuses `paths.Count > 1` with a count | **Yes** | `Dropping_more_than_one_file_is_refused_and_creates_no_session` | none |
| **11116** unsupported-file feedback | **No acceptance gate existed anywhere.** `SessionService.EstablishSourceAsync` copied the file, inspected it, and created a root Revision from *any* container — a `.txt` file imported successfully as `ImageFormat.Unknown`. `FailureCode` had no unsupported/unreadable input code. Home's only refusal sentence was `Home_ImportFailed` = "That file could not be imported ({0}). No session was created." | **No** | `SessionService.cs` `EstablishSourceAsync`; `FormatSniffer.Detect` returning `Unknown` with no consumer; `FailureCode.cs` | Truthful, format-specific refusal; a real gate; and the existing sentence was also **untruthful** — `FailImportAsync` does commit a session and a failed attempt |
| **11116** unsupported vs unreadable distinction | none | **No** | as above | two typed codes needed |
| **11116** PSD/PDF not called unsupported | PSD/PDF imported fine (they simply were not gated) | **Yes**, incidentally | `PsdInputPreparationTests`, `PdfPreparationWorkflowTests` | must not regress under the new gate |
| **11116** workflow-selection entry | `_navigation.GoToWorkflowSelection` after import | **Yes** | `Choosing_one_file_persists_a_real_session_and_opens_workflow_selection` | none |
| **11116** Recent Processing access | present on the page | **Yes** | `HomeView.xaml` | none |
| **11116** no internal architecture terminology | `DisplayNames` maps every enum; no Revision/Session/Adapter wording on Home | **Yes** | `Strings.resx`; `A_recent_row_shows_operator_information_and_no_storage_detail` | none |
| **11117** 30 days **or** 100, whichever first | `SessionService.RecentSessionLimit = 100`, `RecentSessionWindow = 30 days`, passed to `ISessionRepository.ListRecentAsync` | **Yes** | `Recent_processing_asks_persistence_for_at_most_100_sessions_from_the_last_30_days` | none |
| **11117** thumbnail | **absent.** `RecentSessionRow` carried no image; `IArtefactPreviewService` had only `GetPreviewAsync(session, revision)` at `MaximumDisplayEdge = 2048` | **No** | `RecentSessionRow.cs`; `HomeView.xaml` row template | a bounded, presentation-only picture from a persisted artefact |
| **11117** display/output name | `RecentSessionRow.DisplayName` | **Yes** | as above | none |
| **11117** workflow | `RecentSessionRow.Workflow` | **Yes** | as above | none |
| **11117** current step/state | `CurrentStep`, `State` | **Yes** | as above | none |
| **11117** timestamp | `UpdatedAt`, local time | **Yes** | as above | none |
| **11117** resume action | `ResumeCommand`, label switches Resume/Details | **Yes** | `Resume_restores_the_session_from_the_database_after_a_restart` | none |
| **11117** delete-record action | **absent.** `Abandon` was offered instead and explicitly deletes nothing; there was no way to take any entry off the list | **No** | `HomeViewModel.AbandonAsync`; `Home_AbandonDone` | a durable record-removal action bound by the AC's own safety sentence |
| **11117** older records disappear without deleting approved files | the age/count limits are query-side only; nothing deletes | **Yes** | `SqliteSessionRepository.ListRecentAsync` | none |
| **11117** respect active/interrupted safety | `SessionStateRules` had `AllowsProgress` and `AllowsAbandon`; no removal rule existed because no removal existed | **No** | `SessionStateRules.cs` | one authority deciding removal legality |

Two brief assumptions were checked and are **stale**: PSD and PDF are fully supported Product
paths (so the audit's "actively misleading for PSD" no longer applies — PSD imports and is
prepared), and SCRUM-11121's diagnostic retention is complete.

---

## 3. Current input-format authority

`src/PrintFlow.Domain/Files/SupportedInputFormats.cs` is now the single Product statement of what
PrintFlow Studio accepts. It is derived from MVP design §9.1/§9.2 and Jira 11201/11405/11406/11407,
not from any historical list:

| Format | Accepted at import | Decoded by PrintFlow at import | Authority |
| --- | --- | --- | --- |
| PNG | yes | yes | design §9.2 "Supported"; Jira 11405 |
| JPEG / JPG | yes | yes | design §9.2 "Supported"; Jira 11405 |
| PSD | yes | **no** — prepared later through Photoshop | design §9.2; Jira 11406 |
| PDF | yes (single page) | **no** — rasterised later at production DPI | design §9.2; Jira 11407 |
| Multi-page PDF | accepted as a container, refused by PDF preparation with `PdfMultiplePages` | — | design §9.2 "Unsupported; PrintFlow does not silently select the first page" |
| TIFF | **no** | — | design §9.2 lists no TIFF input rule; TIFF is this product's *output*, and Jira 11405–11407 define input paths for the four above and no others |
| anything else | no | — | `FormatSniffer` returns `Unknown` |

Three consequences, stated plainly because two of them are behaviour changes:

1. **A file that is not one of the four is now refused at import.** Before this slice it was
   imported and failed several steps later. This is the AC clause being closed.
2. **TIFF is now refused as an input**, and `Home_ImportFilter` no longer offers `*.tif`/`*.tiff`.
   This is a deliberate narrowing: nothing in the product ever had a TIFF input path, so a TIFF
   import could only produce a session that failed at the Photoshop step. Called out here because
   it is the one place this slice removed something that previously appeared to work. The only
   test that relied on it was using `ImportAsync` as a shortcut to place a production TIFF as a
   Revision; it now reaches the same state through the real Generate Print TIFF workflow, which
   is a stronger assertion.
3. **No format list exists in the shell.** `DisplayNames.SupportedInputList` renders
   `SupportedInputFormats.All` through the existing `DisplayNames.ImageFormat` mapping, so the
   sentence an operator reads cannot drift from the rule the import path applies. A test asserts
   the refusal names every accepted format by iterating the Domain authority.

The decision is always made from the file's **magic bytes** (`FormatSniffer`), never its
extension, and always about the **managed copy's** facts — the same single read that produces the
SHA-256 a Revision is bound to.

---

## 4. Unsupported vs unreadable semantics

Two new stable, persisted `FailureCode` values, kept apart because they lead to different
operator actions:

| Code | Meaning | Operator's next action | Where established |
| --- | --- | --- | --- |
| `SourceFormatUnsupported` | the container is not one of the four accepted inputs | choose a different kind of file | `SessionService.RefuseUnacceptableInput`, from `FileFacts.Format` |
| `SourceImageUnreadable` | an accepted, PrintFlow-decoded container (PNG/JPEG) from which no image could be read | check the file — it is incomplete or damaged | same, from `FileFacts.PixelWidth`/`PixelHeight` being null |

Both carry structured context — `sourceFormat` and `sourceFileName` — so the shell can be specific
without forming a second opinion about the file. `DisplayNames.ImportRefusal` turns one failure
into one sentence and appends the stable code; `TechnicalDetail` is never shown, because it is
English log text that can name a path.

Preserved distinctions, all still separately reported:

| Situation | What the operator is told |
| --- | --- |
| unsupported format, positively identified | "This is a **TIFF** file, which PrintFlow Studio cannot process. Choose one PNG, JPEG, PSD or PDF file instead — a PDF must have a single page." + code |
| unrecognised container | "“customer-notes.txt” is not an image PrintFlow Studio recognises. …" + code |
| supported but unreadable/corrupt | "“damaged.png” is a **PNG** file, but no image could be read from it — the file is incomplete or damaged. **This is not a format problem.**" + code |
| multiple files | "{n} files were dropped. PrintFlow Studio processes one file per session, so nothing was started…" (unchanged; refused before any file is examined) |
| PSD/PDF contract refusal | unchanged — `PsdUnsupported`, `PsdCompositeMissing`, `PsdUnreadable`, `PdfEncrypted`, `PdfMultiplePages`, … raised by their own preparation processors |
| general import/filesystem failure | the existing `DisplayNames.Failure(code)` sentence for `WorkspaceError`, `OutputMissing`, `PersistenceError`, `Cancelled`, … |

**A corrupted PNG is never described as an unsupported PNG.** A test asserts both the code and the
sentence, including the absence of the unsupported code.

**Honest boundary.** The readability check is what `WicFileInspector` can establish from one pass
with `BitmapCreateOptions.DelayCreation`: a container whose header cannot be parsed at all. A PNG
with an intact `IHDR` and a truncated `IDAT` still reports dimensions and is therefore accepted at
import, failing later at preview or at the adapter with `OutputUnreadable` — which is also not
"unsupported". Making import fully decode every pixel would be a different, heavier change to the
inspector and was not made.

**Where the gate sits, and why.** Inside `EstablishSourceAsync`, after the managed copy exists and
has been inspected — so a refusal follows exactly the same path as every other import failure:
`FailImportAsync` records the failed attempt and its structured evidence, and the refusal is
explainable through Error Details (SCRUM-11120) rather than vanishing. A pre-session gate was
considered and rejected: it would have broken
`ErrorDetailsServiceTests.Failure_before_an_output_destination_exists_says_not_established`, which
depends on a failed import still leaving an inspectable attempt. Because a refused import does
commit a session row, the old `Home_ImportFailed` claim "No session was created" was **untrue**;
it has been replaced with "Nothing was processed and your file was not changed", which is true.

Preserved unchanged: the exactly-one-image rule, `InputSnapshot`, source immutability and hash
binding, workflow-selection behaviour, and the PSD/PDF preparation architecture. No validation was
duplicated in the UI and the import engine was not redesigned.

---

## 5. Thumbnail authority, lifetime and performance

**Authority — a persisted artefact, chosen by the seam.**
`IArtefactPreviewService.GetRecentThumbnailAsync(SessionId, CancellationToken)`. The caller names
a session and nothing else; `ArtefactPreviewService.ThumbnailArtefactOf` picks:

1. the session's **root Revision** — the file the operator imported — when its container is one
   this product decodes; otherwise
2. the **managed raster derived directly from that root**, which is what PSD/PDF preparation
   produces; otherwise
3. nothing, and the row shows a neutral no-picture state.

The root was chosen because it is the one fact about a job that never changes: a row does not
become a different image between two visits to Home. No new image authority was invented, nothing
is persisted, and a Revision whose retention was released (`RetentionReleasedAtUtc`) is skipped.

**Lifetime — none.** Nothing is cached anywhere. Each row is decoded when the list asks and the
bytes are collectable as soon as the row is dropped, exactly as Epic 11200 Part C1 §6 requires and
as `DbInvariantTests.No_table_declares_a_binary_column` continues to guarantee for SQLite. No
general thumbnail cache was built, because nothing in the current architecture needs one.

**Presentation only.** `IArtefactPreviewService` names no mutating type and takes no path — both
enforced by `PreviewBoundaryTests`. Reading a thumbnail creates no Revision, records no review,
changes no workflow state, writes no file and never invokes Meitu or Photoshop. A test compares
the complete aggregate and the artefact bytes before and after a list load.

**Performance approach — bounded twice, sequential once.**

- `IImagePreviewDecoder.ThumbnailEdge = 192` is a second, much smaller bound alongside
  `MaximumDisplayEdge = 2048`, and it is the decoder's number rather than the caller's, so a
  screen cannot ask Home for a hundred review-sized previews by accident.
- `WicImagePreviewDecoder` already decodes on a worker thread (`Task.Run`), so nothing large is
  built on the dispatcher.
- `HomeViewModel.StartThumbnailLoad` walks the rows **one at a time**. That is a bounded degree of
  one without a queue, a semaphore or a scheduler — the smallest thing that rules out the decode
  storm. `RefreshAsync` publishes the rows and returns; the pictures arrive into rows already on
  screen.
- Staleness is structural rather than tracked: each refresh builds new row objects and a new
  `CancellationTokenSource`, cancelling the previous load. A late result therefore reaches an
  object no longer in `RecentSessions` — a stale result cannot bind to another row because no two
  loads ever share a row.
- Every outcome is non-fatal. A missing, unreadable or unprepared artefact leaves the neutral
  state and the walk continues; cancellation and infrastructure teardown are swallowed silently,
  the same rule `PreviewPayloadConverter` already follows, because a picture that cannot be drawn
  must never take a screen down.
- Accepted characteristic: leaving Home does not cancel its load (screens have no disposal hook
  and each visit is a fresh transient view model). The load is bounded, sequential and
  self-terminating, holds no lock and produces no record, so it costs at most a handful of small
  decodes for a screen nobody is looking at.

No performance timing test was added. The design boundary is proved structurally instead: with the
first decode held open by a gated double, the list is already complete and exactly one decode has
been requested.

---

## 6. Exact Remove / Delete semantics

**What the AC says, and what it was read to mean.** The clause is "supported resume and
**delete-record** actions", and its own next sentence binds record management: "Older records may
disappear from the list without deleting approved production files". Design §13.2 item 7 lists the
Recent Processing surface as "thumbnail, name, type, step, time, resume, and **delete**".

The delivered semantic: **the record leaves Recent Processing, durably and permanently, and
nothing is deleted.** Every persisted row — session, steps, Revisions, attempts, reviews,
PrintOutputs, InputSnapshot — and every file survives untouched.

**Why not destroy the metadata.** This was decided before coding, from the schema rather than from
the button label:

- `0001_initial_schema.sql` declares `CREATE TRIGGER ReviewDecision_NoDelete BEFORE DELETE ON
  ReviewDecision BEGIN SELECT RAISE(ABORT, 'ReviewDecision is append-only'); END`. Every child
  table cascades from `ProcessingSession`, so deleting a session row fires that trigger and is
  **aborted** for any session that ever recorded a review — which is every finished job.
- Removing or weakening that trigger would destroy the approval history binding an approved
  production file to the operator who approved it. That is precisely a "review-authoritative
  artefact", which the safety invariant for this slice forbids touching.
- Deleting only sessions that happen to have no reviews would give the same button two different
  meanings. Uniform dismissal is the honest option.

So the SQLite cascade did **not** define the Product semantic by accident; it was inventoried
first, and the semantic was chosen against it deliberately. Full cascade inventory:

| Table | On `DELETE FROM ProcessingSession` | Reached by this slice? |
| --- | --- | --- |
| `SessionStep` | `ON DELETE CASCADE` | no |
| `InputSnapshot` | `ON DELETE CASCADE` | no |
| `Revision` | `ON DELETE CASCADE` | no |
| `ProcessingAttempt` | `ON DELETE CASCADE` | no |
| `ReviewDecision` | `ON DELETE CASCADE` → **trigger ABORTs** | no |
| `PrintOutput` | `ON DELETE CASCADE` | no |
| `AutomationLock.SessionId` | no action → would fail while held | no |
| `AutomationLogEntry.SessionId` | `ON DELETE SET NULL` | no |

**How the state is represented, and why it is a new column.** Migration
`0017_recent_processing_record_removal.sql` adds one nullable column,
`ProcessingSession.RemovedFromRecentAtUtc TEXT NULL`. Existing metadata was inspected first: no
column, setting or flag could carry "this finished job is no longer listed" without overloading a
workflow fact. A new **table** was rejected — it would need its own identity, foreign key and
cleanup rules to say something that belongs to exactly one session. One nullable timestamp on the
row that already owns the record is the smallest safe representation:

- It is **not** part of the `ProcessingSession` domain record and is never named by the session
  upsert, whose `ON CONFLICT` clause lists its columns explicitly. A workflow command therefore
  cannot set or clear it, and record management cannot change workflow state.
- `ListRecentAsync` filters `RemovedFromRecentAtUtc IS NULL` **before** the 100-row limit, so a
  removed job does not occupy one of the places Home has to offer.
- It survives restart because it is persisted rather than remembered.

**The write.** `ISessionRepository.RemoveFromRecentAsync` is a single `UPDATE` of that one column,
with every safety condition in the same `WHERE` clause:

```sql
UPDATE ProcessingSession SET RemovedFromRecentAtUtc = @at
WHERE Id = @id
  AND RemovedFromRecentAtUtc IS NULL
  AND State IN ('COMPLETED', 'ABANDONED')
  AND NOT EXISTS (SELECT 1 FROM ProcessingAttempt a WHERE a.SessionId = @id AND a.ResultStatus = 'RUNNING')
  AND NOT EXISTS (SELECT 1 FROM AutomationLock l WHERE l.SessionId = @id);
```

It is deliberately **not** a `SessionMutation`: a mutation is what one workflow command wrote, and
taking a record off a list is not a workflow transition. One operator action, one statement, one
column — there is no `DELETE` here and no way to add one without rewriting the method. Because the
guards are part of the write, a session that resumed, acquired the automation lock or started an
attempt between the caller's check and the write simply matches no row and nothing happens.
Rows-changed must be exactly 1; a second removal of the same record is refused rather than
reported as done. No transaction/rollback case was added because no metadata is deleted and the
operation is a single atomic statement.

**Terminology.** The button reads "Remove from list" / "从列表移除", and the confirmation says what
is kept: "“{name}” was removed from Recent processing. Its imported file, approved outputs and
processing history are kept." Labelling a durable dismissal "Delete" would invite an operator to
believe they had cleaned up production files, which is the one thing it must never do.

**Abandon and Remove are not synonyms.** `Abandon` is a workflow command that ends a job and
records the decision; `Remove from list` is record management on a job that has already ended. They
are offered on disjoint sets — Abandon for `Active`/`HandedOff`, Remove for
`Completed`/`Abandoned` — so a job in progress is abandoned first and can be removed afterwards.

---

## 7. Production-file safety

The action's reachable surface is one nullable column of one row. It opens no file, and the
customer source, InputSnapshot bytes, Revision files, approved PNGs, PrintOutput TIFFs,
manual-result sources, review-authoritative rows and diagnostic evidence are all outside what its
SQL can name. Diagnostic evidence continues to expire only under SCRUM-11121's separate retention
authority, which this slice did not touch.

Proved rather than asserted: the completed-record test and the UIA proof both snapshot the bytes
of every Revision file, every PrintOutput file and the operator's own source before the action and
compare them afterwards, and both re-read the whole aggregate to show every row is identical.

---

## 8. Recovery interaction

SCRUM-11112's authority is preserved structurally, not by agreement:

- `FindRecoveryCandidatesAsync` selects only sessions in `ACTIVE` or `HANDED_OFF`.
  `SessionStateRules.AllowsRecordRemoval` admits only `Completed` or `Abandoned`. The two sets are
  disjoint, so **a recoverable session can never be removed** — there is no state in which both
  are true.
- Home already excludes any session with a recovery entry from Recent Processing, so an unresolved
  interruption appears as a recovery card and never simultaneously as a contradictory Recent row.
  The UIA proof asserts this with all three kinds of entry present at once.
- The service additionally refuses a session with an attempt still `RUNNING`, and the repository
  re-checks it plus the automation lock in the write. This is the same pair of facts that already
  gates completion retention.
- `Interrupted` is deliberately **not** a bar. An interrupted attempt on a `Completed` or
  `Abandoned` session is resolved history — recovery draws only from `Active`/`HandedOff` — so a
  crashed job the operator has since abandoned can be removed. Refusing it would have stranded
  that card on Home forever. A test covers exactly that case.
- Eligibility lives in one place. `SessionStateRules.AllowsRecordRemoval` is consumed by
  `SessionListItem.CanRemoveRecord` (which decides whether the button is offered) and by
  `SessionService.RemoveFromRecentAsync` (which decides whether the action is accepted). The Home
  view model has no rule of its own.

---

## 9. Persistence and restart

| Claim | How it is established |
| --- | --- |
| the record stays off the list after a restart | a second `HomeViewModel` over a freshly built session service and the same database still does not list it (two tests plus the live smoke) |
| the session is still complete and loadable | the whole aggregate is re-read and compared field by field |
| the 30-day / 100-row policy is unchanged | the existing recording-repository test still asserts 100 and 30 days |
| a removed record frees no production file and blocks no cleanup | `FindCompletedSessionsAsync` and the retention path are untouched |
| the migration is additive | `MigrationTests` upgrade paths from versions 1–7 and the fresh-database path all pass; `MaximumBoundsBoundaryTests` names 0017 explicitly |

---

## 10. Localisation and accessibility

**Localisation.** `ILocalisationService`, `OperatorCulture`, `Strings.resx` and
`Strings.zh-CN.resx` only — no second mechanism. Ten new keys, both languages, both files:
`Home_ImportRefused`, `Home_ImportFormatSeparator`, `Home_ImportUnsupportedFormat`,
`Home_ImportUnrecognisedFile`, `Home_ImportUnreadableImage`, `Home_RemoveRecord`,
`Home_RemoveDone`, `Home_RemoveFailed`, `Home_RecentNoThumbnail`, `Home_RecentThumbnailOf`; plus
`Failure_SourceFormatUnsupported` and `Failure_SourceImageUnreadable` for the shared failure
mapping. The now-untruthful `Home_ImportFailed` was removed from both files and from `Strings.cs`.
`LocalisationResourceTests` continues to enforce parity, non-empty values and accessor coverage.

**AutomationIds** follow the screen's existing convention (`Home.<Thing>`), so no existing id
moved and no existing driver or evidence broke:

| Id | Element |
| --- | --- |
| `Home.RecentSessionList` | the list (existing) |
| `Home.RecentSession` | one row container, accessible name = the operator's output name (existing) |
| `Home.RecentSessionThumbnail` | the picture |
| `Home.RecentSessionNoThumbnail` | the neutral line shown in its place |
| `Home.ResumeSession` | Resume / Details (existing) |
| `Home.AbandonSession` | Abandon (existing) |
| `Home.RemoveSessionRecord` | Remove from list |

No localised text appears in any id. The picture's identity sits on the `Image` and on the
`TextBlock` beside it rather than on the containing `Border`, because a `Border` creates no
automation peer and an id put there is invisible to a real UI Automation client — this was found
by the live smoke failing to see it, and fixed.

**Keyboard.** Both row actions are tab stops; the thumbnail is not (it is a picture, and making it
focusable would put a stop between the operator and every action on every row). No element on the
screen sets `Cycle` or `Contained` tab navigation, so nothing traps focus. Asserted from a real
measure/arrange pass.

**Row layout** (compact, existing visual system, no redesign):

```
[64×64 picture]  Output name
                 Workflow · current step · state · last activity     [Resume|Details] [Abandon] [Remove from list]
```

The picture keeps a fixed extent whether or not there is an image, so the list does not jump as
thumbnails arrive. Abandon and Remove are never both offered.

---

## 11. Targeted tests

23 new tests. No format × failure × workflow × state × locale matrix was built.

**`HomeInputAcceptanceTests` (7)** — SCRUM-11116
1. an unrecognised file is refused, naming the file and every accepted format read from the Domain
   authority; no Revision and no InputSnapshot; the operator's file byte-identical
2. a positively identified but unaccepted container (TIFF) is named as a TIFF; the dialog filter
   and the gate agree
3. a supported container carrying no readable image reports `SourceImageUnreadable`, never
   `SourceFormatUnsupported`, names PNG, and the persisted attempt carries the same code
4. + 5. PSD and single-page PDF still import, with `InputSnapshot` and an untouched source
   (Theory, 2 cases)
6. several dropped files are still refused with a count, before any file is examined
7. an accepted file still records snapshot, root Revision, hash-bound managed copy and untouched
   source

**`RecentProcessingRecordTests` (8)** — SCRUM-11117
1. a listed job shows a bounded thumbnail of its own root Revision (identity + `ThumbnailEdge`
   bound + downsampling from a 600 px source)
2. a row whose picture cannot be produced (deleted artefact; unprepared PSD) stays neutral, raises
   no notice, and still opens from persisted state
3. loading the list's pictures changes no metadata (full aggregate compared) and no file
4. the list is published before decoding and decodes one row at a time (gated double)
5. a completed record leaves the list durably while every file survives — restart, byte-compare of
   every Revision and PrintOutput file plus the customer source, full aggregate compare, unrelated
   job untouched
6. an abandoned record with a resolved interruption can be taken off the list
7. active, handed-off and recoverable records are refused — button not offered, service refuses,
   recovery entry unaffected
8. a record that has already left the list is refused a second time

**`HomeRecentAccessibilityTests` (7)** — UI coverage
1. a removable row renders every promised id, a real picture, and the row's accessible name; zero
   binding errors
2. row action labels are localised (`Remove from list` / `从列表移除`) while ids are identical
3. + 4. the neutral no-picture state is translated (Theory: en-US, zh-CN)
5. a keyboard operator reaches both row actions; the thumbnail is not a stop; nothing traps focus
6. both row actions expose `IInvokeProvider` and report `AutomationControlType.Button`
7. the bounded UIA proof (§12)

**`RecentRecordManagementLiveSmoke` (1)** — opt-in real-window proof (§12)

**Existing suites re-run:** Home and Workflow Selection, Recent Processing, recovery surface,
persistence, preview (including the alpha-normalisation slice), localisation resources,
navigation/composition and startup, and every architecture boundary suite. Three existing tests
needed changes, all recorded below in §14.

---

## 12. WPF / UIA proof

Synthetic and deterministic data only; no coordinate anywhere; Meitu and Photoshop are never
started. The scenario is the minimum that can distinguish right from wrong:

- one **finished** job carrying a real accepted separated-CMYK + W1 production TIFF, approved and
  completed through the real Generate Print TIFF workflow (a normal row with a thumbnail, and the
  removable one);
- one job **still in progress** (a normal row that must be untouched);
- one **unresolved interruption** (a recovery card that must stay recoverable);
- and, in the thumbnail suite, a row whose picture is unavailable.

**Default, unconditional proof** — `HomeRecentAccessibilityTests.
A_driver_removes_one_finished_record_and_nothing_else_changes`. The real `HomeView` is measured and
arranged; the row is found by the accessible name the operator gave the job; the action is found
inside that row by `AutomationId`; it is pressed through `IInvokeProvider.Invoke()` obtained from a
real `UIElementAutomationPeer`, and the dispatcher is pumped exactly as a desktop pumps it. No view
model property is read and no command is called directly.

**Opt-in real-window proof** — `RecentRecordManagementLiveSmoke`, gated by
`PRINTFLOW_RECENT_RECORD_LIVE` (evidence directory) and `PRINTFLOW_RECENT_RECORD_CULTURE`. A real
`Window` is shown, reached through the Windows UI Automation **client**
(`AutomationElement.FromHandle`), and driven with `InvokePattern`. Run once per language on this
workstation; both passed. Evidence in
`evidence/scrum-11117-recent-record-live/`: `recent-before-en-US.png`, `recent-after-en-US.png`,
`recent-before-zh-CN.png`, `recent-after-zh-CN.png` and a transcript per language. That directory
is local only — `/evidence/` is gitignored under the repository's deny-by-default privacy posture.

Human-readable summary of what those screenshots show: three cards on one screen (a finished job
with a thumbnail offering Details and Remove from list, an in-progress job offering Resume and
Abandon, and a separate Recovery needed card); after the invoke, the finished card is gone, the
notice states that its imported file, approved outputs and processing history are kept, and the
other two cards are unchanged. The zh-CN pair is the same screen fully translated.

Keyboard traversal is proved in-process rather than by synthesising key presses in the live smoke:
a Tab route driven at a shared desktop reports whichever window happens to hold the keyboard, and
the first attempt here did exactly that.

**Independently verified after the invoke, through SQLite and the services rather than the screen:**

| Claim | Result |
| --- | --- |
| the item is removed exactly as the AC specifies | `ListRecentAsync` no longer returns it; the in-progress job still is returned |
| the result survives restart | a fresh service and a fresh `HomeViewModel` over the same database agree |
| source bytes unchanged | the customer's own file is byte-identical |
| Revision / approved-output bytes unchanged | every Revision file and the approved PrintOutput TIFF are byte-identical and still present |
| the persisted record is intact | session still `Completed`; steps, Revisions, attempts, reviews, outputs and snapshot all compare equal |
| an unrelated record remains | the in-progress job is still listed and still loads |
| recovery authority remains truthful | the unresolved interruption is still a recovery entry, before and after |

---

## 13. Full-suite decision

A full run was justified rather than optional: this slice changed **Recent Processing query
semantics** (`ListRecentAsync` SQL), introduced **persisted record-management semantics** (a
migration and a new repository operation), extended **shared preview infrastructure**
(`IImagePreviewDecoder`, `IArtefactPreviewService`), and added an **acceptance gate on the single
import path** every workflow starts from.

- Targeted suites and a clean build first, throughout implementation.
- One complete run at the end: **11,632 passed / 0 failed / 0 skipped**, which is exactly the
  accepted 11,609 baseline plus the 23 new tests.
- Build: **0 warnings / 0 errors**.
- The full suite was not re-run after small fixes; targeted suites were.

---

## 14. Changes to existing tests

Three, each because a documented contract genuinely moved:

1. `ValueObjectTests.Failure_codes_keep_stable_English_names_because_they_are_persisted` — the two
   new codes added to the required set. This test exists to make a new persisted code a deliberate
   act.
2. `MaximumBoundsBoundaryTests.The_migration_set_ends_at_…` — renamed and moved to `0017`, with the
   paragraph the convention requires stating what the script does and why the destructive
   alternative was rejected.
3. `PreviewAlphaNormalizationTests.A_CMYK_W1_TIFF_Revision_previews_visibly_through_the_preview_service`
   — used `ImportAsync` to drop a production TIFF in as a session artefact, which the input gate
   now refuses. It reaches the same state through the real Generate Print TIFF workflow instead, so
   the bytes previewed are the ones the Photoshop step actually wrote.

Plus mechanical updates for the new `HomeViewModel` dependency (five construction sites) and the
new `IArtefactPreviewService` member (two test doubles).

---

## 15. Jira reassessment

Each AC was reread from the CSV after implementation and assessed clause by clause.

### SCRUM-11116 — PARTIAL → **FULL**

| Clause | Verdict |
| --- | --- |
| clear one-image drop target | satisfied (whole-screen drop, Choose file, hint states the contract) |
| **unsupported**-file feedback | satisfied — refused at import with a format-specific, truthful sentence and a stable code, from the one Product authority |
| **multiple**-file feedback | satisfied (unchanged, re-asserted) |
| workflow-selection entry | satisfied |
| Recent Processing access | satisfied |
| reflects one-image-at-a-time | satisfied |
| no internal architecture terminology | satisfied — no Revision/Session/Adapter wording on Home in either language |

No material clause remains open. Two behaviour changes are recorded above and are improvements in
truthfulness: TIFF is refused as an input, and the import-failure sentence no longer claims that no
session was created.

### SCRUM-11117 — PARTIAL → **FULL**, on a stated interpretation

| Clause | Verdict |
| --- | --- |
| 30 days or 100 Sessions, whichever first | satisfied (pre-existing, re-asserted) |
| **thumbnail** | satisfied — bounded, presentation-only, from the session's own persisted artefact, with a safe neutral fallback |
| display/output name | satisfied |
| workflow | satisfied |
| current step/state | satisfied |
| timestamp | satisfied |
| supported resume action | satisfied |
| supported **delete-record** action | satisfied **as a durable removal of the record from Recent Processing** — see the interpretation below |
| older records disappear without deleting approved production files | satisfied, and now proved by byte comparison |
| record management respects active or interrupted processing safety | satisfied — one authority, disjoint from recovery's candidate set, re-checked in the write |

**The interpretation, stated so it can be disagreed with specifically.** "Delete-record" was
implemented as *the record permanently leaves Recent Processing; nothing is deleted*. The AC's own
next sentence binds record management to not deleting approved production files, and the schema's
append-only `ReviewDecision` trigger makes deleting a reviewed session's row structurally
impossible without destroying the approval history that binds an approved production file to the
operator who approved it. The destructive reading was therefore rejected deliberately, not by
default. Everything the clause asks for operationally — an operator-invokable action, a durable
result, safety around active and interrupted work — is delivered. A reviewer who reads
"delete-record" as metadata destruction would find this clause PARTIAL; that is the one judgement
in this slice that turns on wording rather than on code, which is why the overall verdict below is
PASS WITH NOTES.

### Parent SCRUM-11115 — remains **PARTIAL**

Reassessed from its own exact CSV text, clause by clause, not inferred from child labels.

| Parent clause | Verdict |
| --- | --- |
| confirmed Home/Drop surface | **now satisfied** (this slice) |
| workflow surface | satisfied |
| review surface | satisfied |
| dimensions surface | satisfied |
| TIFF review surface | satisfied |
| Recent Processing surface | **now satisfied** (this slice) |
| Settings / Environment Check surfaces | satisfied (SCRUM-11118, SCRUM-11110) |
| Error Details surface | satisfied (SCRUM-11120) |
| Simplified Chinese default, English switchable without restart | satisfied (SCRUM-11119) |
| local-only logs and screenshots | satisfied (SCRUM-11121) |
| **explicit diagnostic-package export** | **open** — SCRUM-11122. Verified absent, not assumed: no export, package, preview or consent path exists anywhere in the source tree. |
| **repeatable versioned offline installer, no automatic updates** | **open** — SCRUM-11123. Verified absent: no installer project, script or documented install/configure/rollback procedure exists. |
| practical production terminology; internal terms hidden; stable internal English states and codes | satisfied — the two new failure codes are stable English and persisted by name |

Two parent clauses remain open, and they are exactly SCRUM-11122 and SCRUM-11123 — confirmed by
inspection rather than carried over from the previous audit.

---

## 16. Scope discipline

Not implemented, and deliberately: SCRUM-11122 diagnostic package export; SCRUM-11123 installer;
any new logging or retention behaviour; any new PSD/PDF architecture; any new Error Details
architecture; general file cleanup. Not added: code signing, digital signatures, certificates or
signing tests. Existing hash and preset integrity requirements are untouched — the import gate
reads `FileFacts` that the same single pass produced the SHA-256 from, and changes nothing about it.

---

## 17. Git state

Local commits on `master` in the canonical checkout. No branch, worktree, clone or alternate
checkout was created; nothing was amended, rebased, pushed or deployed; no AI-attribution trailer
was added.

**PASS WITH NOTES — SCRUM-11116 / SCRUM-11117 HOME AND RECENT PROCESSING VERIFIED**
