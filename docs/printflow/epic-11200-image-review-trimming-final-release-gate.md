# Epic 11200 — Image Review, Comparison & Deterministic Trimming
## Final release gate

| | |
|---|---|
| Date | 2026-08-21 |
| Branch | `master`, 15 commits ahead of `origin/master`, **not pushed** |
| Head | `36defc1` |
| Authorities | MVP Design v1.0 (Confirmed); Epic 11200 Parts A, B, C1, C2, C3 phase reports |
| Toolchain | .NET SDK 10.0.400 |

---

## 1. Verdict

**`EPIC 11200 PASS — READY FOR EPIC 11300`**

Every behavioural gate holds against the real composed application graph. The
real-window pass was completed on an interactive desktop with synthetic images —
it was **not** substituted with render-tree assertions — and one genuine
operator-facing defect it exposed has been fixed. See §22 for the two changes and
§24 for the one item that still requires a human.

### Build state

| check | result |
|---|---|
| `dotnet restore --locked-mode` | PASS, 5 projects, lock files honoured |
| `dotnet build` | **0 warnings, 0 errors** |
| `dotnet test` | **6660 passed, 0 failed, 0 skipped** (6659 + 1 added by this gate) |
| `dotnet list package --vulnerable --include-transitive` | clean, all 5 projects |

---

## 2. Automatic Trim

Verified through `DeterministicAlphaTrimProcessor` resolved from the real
container, and end-to-end in the live application (§19).

The pipeline `approved upstream Revision → deterministic Trim → real cropped
file → FileInspector → SHA-256 → Revision(OperationKind.Trim) → ReviewRequired`
holds. `SessionService.RunProducingStepAsync` creates the Revision only from
`InspectAsync` of a file that exists on disk, so no Revision can describe bytes
that were never written.

| requirement | evidence |
|---|---|
| Exact `alpha > 0`, no opacity threshold | `AlphaBounds.Compute` tests `row[x] > 0` and nothing else. No constant, config value or tolerance exists in the type. |
| Transparent border removed, edge pixels retained | `DeterministicTrimTests` asserts surviving pixels read back through WIC; `Content_running_to_an_edge_is_not_clipped`, `A_single_pixel_in_the_bottom_right_corner_is_kept`. |
| Full-canvas image → `NoChangeRequired` | Decided in exactly one place, `TrimResult.Produced`, from `appliedBounds.CoversCanvas(...)`. A file is still written so the review chain stays unbroken. |
| Trim non-skippable | `WorkflowCatalog`: `StepKind.Trim, IsSkippable: false, RequiresReview: true` in both workflows that carry it. |
| Review hash-bound | `RevisionIntegrityGuard.VerifyAsync` re-reads and re-hashes every time; the cached `ReviewState` is never sufficient proof. |

**Live confirmation.** A 400×300 BGRA synthetic with content at x∈[120,280),
y∈[90,210) trimmed at a uniform 25 px margin produced exactly **210×170**
(160+50 × 120+50) — computed by the running application, written to disk as a
real `Bgra32` PNG, and displayed as such.

---

## 3. Alpha / no-alpha behaviour

| input | outcome | verified |
|---|---|---|
| Alpha image | automatic Trim | live + `DeterministicTrimTests` |
| Fully transparent | `ManualCropRequired`, **no Trim Revision** | `A_fully_transparent_image_demands_a_manual_crop_and_writes_nothing` |
| RGB / no alpha | `ManualCropRequired` | live (`Bgr24` synthetic) + `An_RGB_image_with_no_alpha_channel_demands_a_manual_crop` |
| Undecodable container | `ManualCropRequired`, not a silent failure | `A_container_WIC_cannot_decode_demands_a_manual_crop_rather_than_failing_silently` |

`WicPixelFormats.HasAlpha` is deliberately **three-valued**. The processor
branches on `HasAlpha(frame.Format) == false`, so a format that cannot say
(`null` — an indexed palette) falls through to the decode path rather than being
rounded down to "opaque". Unknown alpha therefore never infers a background.

A `ManualCropRequired` outcome is recorded as a **failed attempt carrying the
reason**, never as a fabricated Revision — the honest record that nothing was
produced.

**No forbidden technique exists.** A source-wide scan for colour-key trimming,
segmentation, chroma/flood-fill, white/black background inference, Meitu and
Photoshop found only: the two documented `Fake*` adapters, `AdapterKind`/
`OperationKind` enum members, and prose in comments. No real automation, and
nothing in the Trim or manual-crop implementations reads a colour channel to
decide anything.

---

## 4. Margin modes and audit

All three modes verified through the real command/session path
(`TrimParameterTests`, 13 tests) and `Tight`/`Uniform` again live.

* **Non-negative validation** — enforced in `TrimMargin`'s factories, and again
  as `CHECK (... >= 0)` in migration 0002. A stored `-4` is unrepresentable.
* **Source-bound clamping** — `TrimBounds.Expand` widens to `long` and clamps to
  the canvas, so a margin can never produce a coordinate outside the source.
* **Margin reaches the processor** — it travels `WorkflowSnapshot.TrimMargin` →
  `TrimRequest.Margin`, and `SetTrimParameters` is the only way to change it.
* **Persisted with the producing attempt** — written in the attempt's *opening*
  transaction, before any pixel work.
* **Previous parameters immutable** — independently confirmed in SQL: the
  `ProcessingAttempt` upsert's `DO UPDATE SET` clause lists only `EndedAtUtc`,
  `ResultStatus`, `OutputRevisionId`, `FailureCode`, `FailureDetailJson`. The
  trim columns are absent, so a retry at a different margin cannot rewrite what
  the first attempt did.

Live: the review panel showed `裁切: 统一 25 像素` beside the result it produced;
the manual-crop review showed no margin line at all, which is correct — a
rectangle a human drew has no margin.

---

## 5. `ManualCropRequired` and manual crop

The full no-alpha journey was run live, end to end:

```
PREPARE_ASSET → Confirm → skip Enhancement → skip BackgroundRemoval
→ automatic Trim → ManualCropRequired → draw crop → ManualImport Revision
→ ReviewRequired
```

| requirement | evidence |
|---|---|
| Session stays Active | live; `A_manual_crop_never_hands_the_session_off` |
| HandOff not used | as above |
| Fresh Attempt | `ProcessingAttempt.Start` with `context.NewAttemptId` |
| File exists before Revision | Revision is built from `InspectAsync` of the produced file |
| `OperationKind.ManualImport` | set in `ProducingWorkOf` for `RunManualCrop` |
| Correct `SourceRevisionId` | `work.InputRevision` — the file the operator drew on |
| Original untouched | live: `Source\` copy byte-identical, customer original hash unchanged |

Eligibility is defined **once**, in `ManualCropEligibility.IsEligible`, and both
consumed by `SessionView` (to offer the control) and enforced by `SessionService`
(to refuse the command whatever the screen offered).

**Reject gate.** `A_rejected_manual_crop_is_retained_and_another_crop_may_follow`
and `Both_the_refused_automatic_attempt_and_the_manual_one_are_retained` confirm
crop A's Revision and ReviewDecision, and the failed deterministic attempt, all
survive; crop B gets a new AttemptId and Revision and becomes the active result;
no previous file is overwritten.

Live: the crop produced a real **394×282 `Bgr24`** file from a (6,5) 394×282
selection — the exact rectangle, in the source's own pixel format. Nothing
invented an alpha channel.

---

## 6. Crop geometry

`CropSurfaceLayout` is pure arithmetic over six numbers — no file, no bitmap, no
WPF type. 26 unit tests cover fit, zoom, downsampled preview, letterboxing,
source boundaries and awkward scales (1.25³, 83⅓, 0.1, 3.7).

Rounding is **outward** (`floor` near, `ceil` far) so no selected column or row
is lost, with a `1e-6` source-pixel `EdgeTolerance` that stops outward rounding
over-reaching on an edge that lands a hair past a pixel boundary — the defect
found and fixed during Part C2, still covered by
`An_edge_on_a_pixel_boundary_does_not_gain_a_row_at_an_awkward_scale`.

Live: a drag covering ~98% of the displayed width and ~94% of its height mapped
to 394/400 and 282/300 source pixels — proportionally correct through fit
scaling and letterboxing.

---

## 7. Before / After lineage

Pairing is a **fact about lineage, never step order or filenames**:
`ArtefactView.SourceRevisionId` comes from `revision.SourceRevisionId`, and
`SessionView.UpstreamArtefact` is resolved through it. The invariant
`Before.RevisionId == After.SourceRevisionId` therefore holds by construction for
Enhancement, BackgroundRemoval, automatic Trim and ManualImport alike.

`The_upstream_artefact_is_the_source_Revision_of_the_step_result` and
`The_imported_original_has_no_before` assert both directions.

Live: `处理前 opaque-rgb.png 400×300` beside `处理后 manual-crop.png 394×282`,
and `alpha-bordered.png 400×300` beside `trimmed.png 210×170`.

---

## 8. Preview security

The seam is the security boundary: `IArtefactPreviewService.GetPreviewAsync`
names a `SessionId` and a `RevisionId` and **never a path**. There is no
`ReadAnyFile(string)` anywhere in the solution.

* **Wrong-session Revision refused** — membership *is* the lookup: the Revision
  is sought in the aggregate loaded for that session, so "belongs elsewhere" and
  "does not exist" are indistinguishable, both `PreconditionNotMet`.
* **Unknown Revision refused** — same path.
* **Workspace containment** — `IImagePreviewDecoder` takes a `WorkspaceFileRef`;
  `FileWorkspace.ResolveAbsolute` goes through `PathGuard.ResolveWithinRoot` and
  **throws** rather than resolving outside the root.
* **Preview writes nothing** — the encoder targets a `MemoryStream`; the only
  `FileStream` in the decoder is opened `FileAccess.Read`.
* **No preview bytes in SQLite** — no BLOB or image column exists in either
  migration.
* **No arbitrary path reaches a ViewModel** — independently grepped: `ViewModels/`
  contains no `System.IO`, no `File.`/`Path.`/`Directory.`, and no path-taking API.

---

## 9. Stale-preview race

Re-run as a **controlled** race, not a timing hope: `GatedPreviewService` holds
the first real decode open until the test releases it.

```
preview A loading → screen moves to Revision B → B loads → A finishes late
```

A publishes nothing. `SessionViewModel.LoadPreviewsAsync` compares the generation
token it was started with against `_previewGeneration`, which every
`ClearPreviews` (i.e. every state change) increments.

The manual-crop variant is stronger still: it asserts the *decoded pixel
dimensions* of what remains on screen (9×8, not the stale 4×4), so the displayed
image cannot become inconsistent with the current Revision's metadata.

---

## 10. Preview failure

A decode failure produces an `ArtefactPreviewPane.Unreadable` pane and changes
nothing else — no `Notice`, no reload, no command, no session mutation, and the
Revision is not invalidated. Preview failure is not processing failure.

`A_preview_that_cannot_be_produced_leaves_the_review_usable` and
`A_missing_file_fails_the_preview_and_leaves_the_session_untouched`.

---

## 11. Exact-hash integrity

For both automatic Trim and ManualImport: previewing a Revision, mutating the
bytes underneath, then approving yields **`RevisionIntegrityMismatch`** and no
progression.

`A_previewed_Revision_whose_bytes_change_still_refuses_approval` and
`A_mutated_manual_crop_refuses_approval_with_RevisionIntegrityMismatch`.

`ImagePreview` deliberately carries **no hash** — pairing pixels with a hash
would invite a screen to treat "I displayed this" as "I verified this". Visual
preview never replaces SHA-256 authority.

Live confirmation of the chain: the approved PNG export was byte-identical
(`D2533B252A19`) to the trimmed file reviewed and approved on screen.

---

## 12. ReturnToStep

Offered destinations are generated from **actual workflow authority**:
`WorkflowEngine.AvailableReturnTargets` applies the *real* `ReturnToStep(target)`
command to each step and keeps the accepted ones. The existence of a row **is**
its legality — the UI cannot offer a target the engine would refuse, and
`Every_offered_return_target_is_accepted_by_the_service` asserts it at service
level. `ReturnTargetTests` (279 cases) covers both directions exhaustively over
workflow × step × state.

Verified: PREPARE_ASSET return upstream after Trim; PREPARE_CUSTOMER_DESIGN
return upstream after PhotoshopOutput; and return upstream after an approved
manual crop with later progress.

**Invalidation is metadata, not evidence.** The `InvalidateDescendants` effect
produces only revision invalidations and output updates — there is no file
deletion on the path. Independently confirmed live: after returning upstream of
an approved manual crop, `manual-crop.png` was **still on disk** while the step
states reset correctly (去背景 → 等待中, 裁边 → 等待中, upstream 已通过/已跳过
untouched). Audit history is retained.

Live wording of the confirmation: *"返回该步骤会使其后生成的结果失效。已有的审核
记录将予以保留。"* — accurate on both counts.

---

## 13. Sibling-output semantics

The established invariant holds and was not re-litigated: **siblings share their
source**, so a `ReturnToStep` reaching that shared source invalidates *both*
(`Returning_upstream_of_the_shared_design_invalidates_both_sibling_outputs`), and
a return reaching neither leaves both valid.

`No_offered_return_target_invalidates_exactly_one_of_two_siblings` asserts the
honest negative property directly. Rejecting one sibling remains the path that
retires B while A survives. No nonexistent one-sibling `ReturnToStep` case was
invented.

---

## 14. Restart / resume and recovery

**Restart/resume.** Progress into Trim `ReviewRequired`, `ManualCropRequired` and
ManualImport `ReviewRequired` all survive service/application recreation
(`The_chosen_margin_survives_a_restart_and_is_what_actually_runs`,
`Resume_restores_the_session_from_the_database_after_a_restart`,
`Restart_and_reload_restores_a_structurally_identical_snapshot`,
`Going_home_releases_the_previews_and_resuming_reloads_them`).

Confirmed live: the application was killed and relaunched, the sessions appeared
under 最近处理, and resuming restored the exact post-return state with previews
reloaded from persistence — not from a cached image object.

**Recovery regression (Epic 11100).** Unbroken:
`A_crashed_Running_attempt_recovers_to_Interrupted_and_fabricates_no_Revision`.
No Revision is fabricated, and quarantine cannot touch protected areas —
`FileWorkspace.QuarantineWorkingFile` refuses anything that is not
`WorkspaceArea.Working`, so Source and Approved artefacts are never quarantined.

---

## 15. Migrations

* Fresh DB migrates through the newest schema — `Empty_database_migrates_successfully`.
* Migration 0002 adds the trim parameter fields correctly, on both
  `ProcessingSession` (the pending decision) and `ProcessingAttempt` (how *this*
  Revision was produced), typed and nullable with `CHECK (>= 0)`.
* **Pre-0002 database upgrades correctly** — this gate found no coverage for the
  upgrade path and added it (§22). Verified: seed from the shipped 0001 script,
  write a session row, migrate, and the columns arrive while the row survives
  reading NULL rather than a fabricated default.
* Unsupported future schema **fails closed** — `user_version` above the newest
  known script is refused outright, never auto-repaired or downgraded.
* `MigrationRunner.NewestKnownVersion` is the single current-version authority,
  derived from the embedded scripts rather than a literal.
* No test relies on a stale literal schema number; every assertion is against
  `NewestKnownVersion` (the sole literal, `999`, is deliberately "the future").

---

## 16. Architecture

Inspected directly at the project-reference and source level, not only via the
architecture tests.

| boundary | result |
|---|---|
| Domain Trim types perform no I/O | `PrintFlow.Domain.csproj` declares **no** `PackageReference` and **no** `ProjectReference`. Zero `System.IO`/`File.`/`Directory.` in the whole project. |
| Workflow owns ports/commands/state | references `PrintFlow.Domain` and nothing else |
| Infrastructure owns WIC | `UseWPF` for `System.Windows.Media.Imaging` and Recycle Bin only |
| ViewModels contain no `System.IO` | grep: none — the only hits are doc-comment prose |
| UI does not call processors | no processor interface is referenced from `ViewModels/` or `Views/` outside comments |
| Infrastructure touched only from the composition root | grep: no `PrintFlow.Infrastructure` reference in `App/` outside `Composition/` |
| Preview accepts no arbitrary path | `SessionId` + `RevisionId` only |
| Manual crop takes managed refs + source-pixel bounds | `ManualCropRequest(WorkspaceFileRef, WorkspaceFileRef, TrimBounds)` |
| No real Meitu/Photoshop automation | confirmed by source-wide scan |

---

## 17. Source and file safety

* Customer original never modified — confirmed live by hash after a full run.
* `InputSnapshot` unchanged (`The_imported_source_snapshot_is_unchanged_by_a_manual_crop`).
* Working area is per-Attempt.
* Rejected results retained.
* No silent overwrite — outputs are reserved through `IWorkspace.ReserveOutput`.
* `ReturnToStep` invalidates metadata, not evidence (§12, confirmed on disk).
* **No hard-delete fallback** — `RecycleBin` returns a structured failure if the
  Recycle Bin API fails; it never falls back to `File.Delete`.
* Manual crop cannot write outside the workspace — both paths resolve through
  `PathGuard.ResolveWithinRoot`, which throws rather than escaping the root.

---

## 18. Dependency audit

```
Microsoft.Data.Sqlite      10.0.11   ok
SQLitePCLRaw.*              2.1.12   ok  (core, bundle_e_sqlite3, lib, provider)
Dapper                      2.1.66
```

No vulnerable packages in any project, transitive included. **No
`NuGetAuditSuppress` exists anywhere** in the repository — the obsolete
suppression removed in Part A has not returned. Central package management with
transitive pinning is in force.

---

## 19. Real-window result

**Performed.** An interactive console session was available, the real WPF
application was launched, sized to exactly **1000×700**, and driven with
synthetic images generated for the gate (a `Bgra32` image with a wide transparent
border, and a `Bgr24` image with no alpha channel). Screens were captured and
inspected.

| judged by eye | result |
|---|---|
| Home | clean; recovery and preset status legible |
| Workflow Selection | all three workflows, correct step lists; Trim shown 需审核 and not 可跳过 |
| Automatic Trim review | correct |
| Before / After | 处理前 / 处理后 clearly labelled and unambiguous |
| Checkerboard transparency | readable; transparent margin plainly visible around the artwork |
| Zoom / pan | 适应窗口 / 缩小 / 放大 / 重置缩放 present and operable |
| `ManualCropRequired` | clear message + reason; margin controls correctly **withdrawn** |
| Manual crop rectangle | outline sits over the artwork; selection reported in source pixels |
| Manual crop review | correct, with no margin line (correct for a drawn rectangle) |
| Tight margin | default, stated as `裁切: 紧贴` |
| Uniform margin | applied at 25 px → produced exactly 210×170 |
| Edge-specific margin | control present and selectable; **not exercised end-to-end** |
| ReturnToStep | four correct upstream targets in workflow order; confirmation clear |
| zh-CN UI | wording fits throughout; no clipping or overlap at 1000×700 |

This was a real rendered-window inspection, not render-tree assertions. It is
**not** a substitute for operator sign-off by the shop's own staff (§24).

---

## 20. Git and privacy

* Working tree **clean**; `master` is **15 commits ahead of `origin/master`**.
* **Not pushed.** No force push, no history rewrite.
* 221 tracked files. No tracked PNG/JPEG/TIFF/PSD, customer image, screenshot,
  preview cache, runtime database, lock file, output TIFF or sign-off file.
* `.gitignore` is deny-by-default for customer artwork and production evidence,
  with anchored runtime rules.
* The only path in committed configuration is `D:\PrintFlowStudio`, the intended
  workspace root — not customer data and not a credential.
* All gate evidence stayed outside the repository. Synthetic images created for
  the run were deleted afterwards; the runtime database was backed up before the
  pass (`printflow.db.gatebackup-20260821-173004`) and the two synthetic sessions
  it created remain in the local runtime workspace, outside version control.

One incident worth recording: while automating the shell file dialog, a
misdirected keystroke reached Explorer's inline rename. Windows rejected the name
and **no file was renamed** — verified afterwards against the user's Documents
folder. Subsequent steps targeted controls explicitly instead.

---

## 21. Scope not validated by this Epic

Unchanged and still deferred: real Meitu enhancement, real Meitu cutout, real
Photoshop, CMYK correctness, W1 channel correctness, Photoshop Actions, Maintop,
and physical DTF output. Adapters ran in `Fake` mode, which the application
states on screen. Also still absent: resize handles, freeform crop, rotation,
annotation, colour tools, image filters, a runtime language switcher, and a
generic history browser.

---

## 22. Defects found and fixed

**1. Stale deferred-scope notice — operator-facing, fixed.**
The session screen told the operator that image preview, side-by-side comparison
and returning to an earlier step were *"not part of this build"* — while
rendering all three directly beneath the notice. Parts C1 and C3 delivered them;
only the browsable processing history remains deferred. Corrected in both
`Strings.resx` and `Strings.zh-CN.resx` to name only what is actually still
missing. This is the same defect class the Epic 11100 gate fixed in `a8d2c76`,
gone stale again, and it is only visible by looking at the running window.

**2. Migration 0002's upgrade path was untested — closed.**
`Empty_database_migrates_successfully` only ever exercised a database that had
every script applied in one pass, so the suite would still have passed if 0002
were unrunnable against an existing schema. An `ALTER TABLE` that only ever runs
against a table created moments earlier is not evidence that it runs against the
operator's database. Added
`A_pre_0002_database_upgrades_and_keeps_its_rows`, which seeds from the shipped
0001 script, writes a session row, migrates, and asserts the columns arrive and
the row survives reading NULL. It passes — the upgrade path was correct; only the
proof was missing.

No defect was found in the trim, alpha-safety, margin, manual-crop, preview,
integrity, invalidation or recovery behaviour.

---

## 23. Notes carried forward

* **Accessibility naming.** Several UIA elements expose their ViewModel type name
  instead of their visible label — the main window reports
  `PrintFlow.App.ViewModels.HomeViewModel`, and list items report
  `TrimModeChoice`, `RejectionReasonChoice` and `ReturnTargetRow`. The rendered
  text is correct and operators see the right words; a screen reader would not.
  Pre-existing, outside Epic 11200's scope, worth a `DisplayMemberPath` or
  `AutomationProperties.Name` pass in a later slice.
* `AvailableReturnTargets` applies the real command once per step per view build
  — negligible at six steps and a pure engine, worth revisiting only if a
  workflow ever grows large.
* `SetOutputName` still has no screen.
* Edge-specific margin was verified through the command/session path and its
  control was seen in the window, but not driven end-to-end interactively.

---

## 24. The one item still requiring a human

Everything above was verified either automatically or by inspecting the real
rendered window. What remains is **operator sign-off by the shop's own staff** —
whether the Chinese wording reads naturally to the people who will use it every
day, and whether the margin and return controls match how they actually work.
That is a judgement about fit for purpose, not about correctness, and no amount
of automation or screenshot inspection substitutes for it.

It does not block Epic 11300: it gates rollout to operators, not further
development, and nothing in Epic 11300 depends on its outcome.

---

## 25. Recommended next Epic

**Epic 11300**, on the foundation this Epic completes: a reviewed, hash-bound,
deterministically trimmed transparent PNG, with a manual crop path for files the
alpha scan cannot honestly handle, and a return path that invalidates metadata
while retaining every file and every audit record.

Carry into it: operator sign-off (§24) and the accessibility naming pass (§23).

---

**`EPIC 11200 PASS — READY FOR EPIC 11300`**
