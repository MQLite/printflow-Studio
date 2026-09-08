# SCRUM-11075 / SCRUM-11078 — Editable Output Name and Import/Selection Visual Context

| Item | Value |
| --- | --- |
| Date | 8 September 2026 |
| Repository | `d:\Repositories\printflow-Studio`, branch `master` (no branch, worktree or clone created) |
| Baseline commit | `21a596e` — *Record recovery verification and parent Epic reassessment* |
| Jira authority | `C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv`, read before any Product edit |
| Scope | The two remaining operator-facing gaps: an editable Output Name, and the source filename plus preview before workflow selection |

---

## 1. The exact original acceptance criteria

The CSV holds 79 rows keyed `11000`–`11714`; the Jira keys are the fixed offset the existing
re-audit established (`SCRUM key = row position, starting at SCRUM-11060`). Both rows below were
read from the CSV first and are quoted verbatim.

### SCRUM-11075 — CSV Work Item `11107`, Task, parent `11100`, 2 points

> **Implement Collision-Safe Output Naming**
>
> Provide an editable operator-facing Output Name defaulted from the source filename without
> renaming the user's source. Sanitize Windows-invalid characters and generate names such as
> Name_HD.png, Name_CUTOUT.png and Name_280mm_CMYK_W.tif. If a target exists, generate _02, _03
> and subsequent suffixes rather than silently overwriting any file.

Labels: `printflow, mvp, file-naming, output-name, no-overwrite`.

### SCRUM-11078 — CSV Work Item `11201`, Task, parent `11200`, 3 points

> **Build Single-Image Import, Validation and Workflow Selection**
>
> Implement the Home/Drop entry flow for exactly one image. Decode and preview the file before
> external automation, save an InputSnapshot, show filename and editable output name, and present
> the three fixed workflow choices. Multiple files must be rejected clearly. Workflow selection may
> change only until the first processed result exists; after that, the operator must end the
> Session and import again to use another route.

Labels: `printflow, mvp, import, drag-drop, workflow-selection, preview`.

### Parent Epics, read independently

**SCRUM-11068 — CSV `11100`:** *Build the WPF/.NET desktop foundation, fixed workflow state model,
SQLite metadata persistence, immutable input snapshot and revision rules, controlled local file
workspace and collision-safe naming required by the PrintFlow Studio MVP …*

**SCRUM-11077 — CSV `11200`:** *Build the shared operator review experience and deterministic
trimming flow used after significant transformations. Support single-image import confirmation,
side-by-side or slider comparison, synchronised zoom and pan, transparency inspection backgrounds,
structured approve/reject decisions and alpha-based trimming with manual crop fallback when useful
transparency is absent. Trimming is an independent review step between background removal and
physical sizing, and returning upstream must invalidate all downstream derived results so no
outdated TIFF or approval remains current.*

---

## 2. Pre-change requirement matrix

Built by reading the source tree, not by trusting the historical audit rows.

| # | Requirement clause | Current implementation before this slice | Already satisfied | Remaining gap |
| --- | --- | --- | --- | --- |
| 75-a | Output Name defaulted from the source filename | `SessionService.ImportAsync` → `OutputName.Sanitise(StemOf(sourcePath))` | **Yes** | — |
| 75-b | Never renames the operator's source | `FileWorkspace.ImportSourceAsync` copies and sets the copy read-only; `InputSnapshot.OriginalSourcePath` is informational | **Yes** | — |
| 75-c | Sanitise Windows-invalid characters | `OutputName.Sanitise` (never fails) and `OutputName.Create` (rejects operator input); unit-covered by `SanitiserTests` | **Yes** | — |
| 75-d | Generate `Name_HD.png`, `Name_CUTOUT.png`, `Name_280mm_CMYK_W.tif` | `OutputFileNaming.BuildProposedFileName` + `NamingPatternRenderer` over the verified preset patterns | **Yes** | — |
| 75-e | `_02`, `_03` rather than silent overwrite | `OutputFileNaming.BuildCollisionCandidate` + `FileWorkspace` atomic reservation; `WorkspaceTests`, `AcceptedNamingContractTests` | **Yes** | — |
| 75-f | **Editable operator-facing Output Name** | `WorkflowCommand.SetOutputName` existed in the engine with **zero call sites in `PrintFlow.App`**; `CommandKind.SetOutputName` was deliberately left unprobed | **No** | **The whole gap.** No screen, and no way for the engine to report whether editing was legal |
| 78-a | Home/Drop entry for exactly one image | `HomeViewModel.DropCommand` / `ChooseFileCommand` | **Yes** | — |
| 78-b | Multiple files rejected clearly | `HomeAndWorkflowSelectionTests` covers the refusal | **Yes** | — |
| 78-c | Save an InputSnapshot | `SessionService.ImportAsync` writes `InputSnapshot` with `OriginalFileName` and `RootRevisionId` | **Yes** | — |
| 78-d | Three fixed workflow choices | `WorkflowCatalog.All` → `WorkflowSelectionViewModel.Workflows` | **Yes** | — |
| 78-e | Selection changeable only until the first processed result | `CanSelect` reads the engine's `AvailableCommands`; refusal comes back through the command path | **Yes** | — |
| 78-f | **Decode and preview the file before external automation** | `IArtefactPreviewService` existed and was used **only by the Session screen**, i.e. after the workflow had already been chosen | **Partly** | **Not shown on Workflow Selection** |
| 78-g | **Show filename** | `SessionView` carried no source file name at all; the screen showed only `SessionName` (the output name) | **No** | **The source was never identified before selection** |
| 78-h | **Show editable output name** | — | **No** | Same gap as 75-f |

Nothing in the "Already satisfied" column was rebuilt. The naming engine, the sanitiser, the
collision reservation, the preset patterns, the import flow and the workflow catalogue are
byte-for-byte unchanged.

---

## 3. What was implemented

### 3.1 Output Name authority — reused, not duplicated

The screen owns no naming rule.

* **Default** — seeded in `WorkflowSelectionViewModel.Open` from `session.OutputName.Value`, which
  is what `ImportAsync` already derived with `OutputName.Sanitise`. The default is *not* recomputed
  on the screen; a second derivation would be a second authority.
* **Validation** — `OutputName.Create`, the operator-input half of the existing contract. It
  **rejects** rather than silently rewriting, so what an operator typed and what gets written can
  never quietly differ. `OutputName.Sanitise` stays where it belongs: deriving the default from an
  imported stem.
* **Commit** — `WorkflowCommand.SetOutputName`, the pre-existing engine command, through
  `ISessionService.ExecuteAsync`. The view model constructs no domain record and writes no row.
* **Message** — rendered from the authority's own constants. `OutputName.ForbiddenCharacterList`
  was added as a public accessor over the existing private `ForbiddenCharacters` array (the exact
  list `Create` already puts in its own error), and `OutputName.MaxLength` was already public. The
  localised sentence takes both as format arguments, so the rule an operator reads and the check
  that rejected them cannot diverge. **No Windows filename rule is restated in XAML or in a view
  model.**

One engine change was required and is the smallest one available: `CommandKind.SetOutputName` was
previously left **unprobed** in `WorkflowEngine.BuildProbe`, with the stated reason *"SetOutputName
has no screen in this slice"*. That reason no longer holds. It is now probed with the session's
**current** name — exactly as `SelectWorkflow` is probed with its current workflow and
`SetTrimParameters` with its current margin — so the payload is valid by construction and the only
guard that varies is whether the session may still progress. `CanEditOutputName` reads that answer.
No transition, no guard and no state was changed; the transition table already listed
`SetOutputName` as a session-scoped command.

### 3.2 Commit ordering — the name before the workflow

`SelectAsync` now commits the name first and only then selects the workflow:

1. If the typed value equals what the session already holds, **no command is sent** — re-committing
   an unchanged value would write a metadata transaction for a decision nobody made.
2. Otherwise `OutputName.Create` validates. A refusal sets a bounded, localised notice and
   **returns**: no workflow is selected and no navigation happens (§20).
3. An accepted value goes through `SetOutputName`. A refusal from the engine or persistence is
   reported the same way and again starts nothing.
4. Only then is `SelectWorkflow` issued, against the session view the rename returned.

This closes the §19 race outright: the workflow can never start against a name the box on screen
disagrees with, because the name is already persisted before the selection command is built.

### 3.3 Source filename and preview

Two read-only fields were added to `SessionView`, both resolved from the **same** root Revision
that already supplies `OriginalSourceFormat`, so the name, the format and the previewable identity
can never describe three different files:

* `SourceFileName` — the workspace copy's file name, which is the name the operator chose.
* `RootRevisionId` — the identity only. (Named `RootRevisionId` rather than `SourceRevisionId`
  deliberately: `SourceRevisionId` is a banned token inside `PrintFlow.App/ViewModels` under
  `MaximumBoundsBoundaryTests`, which guards the stale-plan rule. The guard was respected, not
  weakened.)

The preview reuses `IArtefactPreviewService` — the single read-only image seam — and
`ArtefactPreviewPane`, the same pane type the Session review surface uses, rendered through the
same `PreviewPayloadConverter` and the same transparency checkerboard brush. **No second image
decoder was introduced**, and no new preview reduction policy: whatever
`IImagePreviewDecoder.MaximumDisplayEdge` already reduces is what is shown, and
`ImagePreview.IsDownsampledForDisplay` already produces the "reduced for display" note that appears
beside the pixel figures.

Transparency is displayed truthfully by painting the existing checkerboard brush *behind* the
image. Nothing is flattened, composited or written.

**Preview semantics.** A preview creates no Revision, records no `ReviewDecision`, touches no source
byte, alters no workflow state and runs no adapter. That is structural — `IArtefactPreviewService`
cannot do any of those things — and it is asserted directly.

**Formats.** No format support was added. A PSD or PDF source, whose managed raster is prepared
later inside the Session, is not asked about at all and is labelled with a truthful sentence instead
of a fabricated picture. Every other supported input previews normally.

### 3.4 Layout, accessibility, localisation

The screen stays compact: preview thumbnail on the left; source file, pixel detail, the Output Name
box and its hint on the right; the three workflow cards below; notice and Back at the foot. Home and
Session were not touched.

Stable AutomationIds, none of which contain localised text:

| Id | Element |
| --- | --- |
| `Screen.WorkflowSelection` | the screen |
| `WorkflowSelection.Preview` | the preview surface |
| `WorkflowSelection.SourceFile` | the imported file name |
| `WorkflowSelection.PreviewDetail` | pixel figures / reduced-for-display note |
| `WorkflowSelection.OutputName` | the editable box |
| `WorkflowSelection.PrepareDesignAsset` | workflow 1 |
| `WorkflowSelection.PrepareCustomerDesign` | workflow 2 |
| `WorkflowSelection.GeneratePrintTiff` | workflow 3 |
| `WorkflowSelection.Notice` | the operator-readable refusal |
| `WorkflowSelection.Back` | Back |

The workflow ids come from `WorkflowChoice.AutomationId`, derived from the persisted
`WorkflowType`, never from the translated title.

Ordinary WPF controls throughout: a `TextBox` exposing `IValueProvider` as `AutomationControlType.Edit`,
and `Button`s exposing `IInvokeProvider` as `AutomationControlType.Button`. No custom peer, and no
coordinate interaction anywhere in the tests.

New strings, en-US and zh-CN:

| Key | en-US | zh-CN |
| --- | --- | --- |
| `WorkflowSelection_SourceFileLabel` | Source file | 源文件 |
| `WorkflowSelection_OutputNameLabel` | Output name | 输出名称 |
| `WorkflowSelection_OutputNameHint` | The base name PrintFlow gives the files it produces. The source file is never renamed. Size and ink suffixes, and a numbered suffix if a file of that name already exists, are added automatically. | PrintFlow 生成文件时所用的基础名称。源文件不会被重命名。尺寸与油墨后缀，以及同名文件已存在时的编号后缀，都会自动添加。 |
| `WorkflowSelection_OutputNameRejected` | That output name cannot be used. Enter 1 to {1} characters, without control characters and without any of: {0} | 无法使用该输出名称。请输入 1 至 {1} 个字符，不能包含控制字符，也不能包含以下任一字符：{0} |
| `WorkflowSelection_OutputNameRefused` | The output name could not be saved ({0}). Nothing was started. | 无法保存输出名称（{0}）。未启动任何处理。 |
| `WorkflowSelection_PreviewHeading` | Imported file | 已导入的文件 |
| `WorkflowSelection_PreviewNeedsPreparation` | This file is prepared for display inside the session. Choose a workflow to continue. | 此文件将在处理页面中准备后才能显示。请选择流程以继续。 |

The hint is deliberately truthful about §8: the operator edits the **base** name, and the size/ink
suffixes and the collision counter are added by the product afterwards. The operator never edits a
final filesystem filename.

### 3.5 Persistence and downstream naming

No schema change, and none was needed. `SetOutputName` emits `WorkflowEffect.PersistOutputName`;
`MergeSession` carries `WorkflowSnapshot.OutputName` onto `ProcessingSession`; the `Session` row's
`OutputName` column is written by the ordinary metadata transaction and read back by
`SqliteSessionRepository`. Downstream naming reads `aggregate.Session.OutputName` in
`SessionService`, so the edited base flows into `OutputFileNaming.BuildProposedFileName` unchanged.

### 3.6 What was deliberately not changed

* Sanitisation, collision numbering, suffix patterns, size suffixes and the CMYK/W naming
  contract — untouched (§29).
* The three fixed workflows and the workflow-lock rule — untouched (§17, §18).
* Home's import flow — untouched (§28). The AC's "show filename and editable output name" sits in
  the same sentence as "present the three fixed workflow choices", so Workflow Selection is the
  smallest surface that satisfies it.
* No Settings screen, no Recent Processing thumbnail or delete action (§26, §27).
* No digital signature, code-signing or certificate work of any kind (§30).

---

## 4. Targeted tests

Two new files, 16 cases. Nothing re-tests the naming contract itself, which already has thorough
unit coverage.

**`tests/PrintFlow.Tests/Integration/Ui/OutputNameAndSourceContextTests.cs`** — 9 cases against the
real session service, real `FileWorkspace` and real SQLite:

| Test | Requirement |
| --- | --- |
| `The_box_opens_on_the_source_derived_name_beside_the_source_file` | initial source-derived value; filename shown separately (§6, §11) |
| `An_edited_name_is_committed_before_the_workflow_and_reaches_the_session` | edit uses the existing authority, is persisted, reaches Session; source not renamed (§5, §9) |
| `The_edited_name_is_the_base_the_produced_file_is_named_from` | downstream naming — produced file is `Memorial Design_HD.png` (§9) |
| `A_refused_name_leaves_the_operator_on_workflow_selection` (×2) | invalid edit starts nothing; bounded readable notice; nothing persisted (§20) |
| `An_untouched_name_is_not_re_committed` | no metadata transaction for a decision nobody made |
| `A_produced_result_closes_the_name_box_and_the_workflow_choice_together` | both answers come from the engine (§18) |
| `The_imported_design_previews_before_selection_and_changes_nothing` | preview before selection; no Revision, review, attempt or state change (§12, §13) |
| `A_psd_source_is_labelled_rather_than_drawn` | truthful behaviour for a not-yet-preparable source (§14) |

**`tests/PrintFlow.Tests/Integration/Ui/WorkflowSelectionAccessibilityTests.cs`** — 7 cases, every
assertion taken from a tree a real WPF measure/arrange pass produced:

| Test | Requirement |
| --- | --- |
| `The_source_context_and_all_three_workflows_render_with_stable_ids` | AutomationIds, no binding errors, source ≠ output name, an `Image` with a real source (§22) |
| `The_labels_are_localised_while_the_identities_are_not` | en-US / zh-CN, ids stable (§25) |
| `The_source_and_output_name_labels_are_translated` (×2) | exact text, culture pinned explicitly (§25) |
| `A_keyboard_operator_reaches_the_name_the_workflows_and_back` | tab stops; no `Cycle`/`Contained` focus trap (§23) |
| `The_output_name_box_exposes_the_value_pattern` | writable `IValueProvider` / `Edit`; `IInvokeProvider` / `Button` (§24) |
| `A_driver_retypes_the_name_chooses_a_workflow_and_the_output_is_named_from_it` | the live proof, below (§36–§38) |

**Collision-safe naming** was not re-tested with a new matrix. The engine underneath is unchanged,
and its existing coverage — `SanitiserTests.Collision_candidates_follow_base_02_03`,
`AcceptedNamingContractTests`, `WorkspaceTests.Collision_reservation_yields_base_then_02_then_03_and_never_overwrites`,
`PhotoshopTiffFinalReviewTests` (`tiff-out_200mm_CMYK_W_02.tif`) — all remain green.

---

## 5. Live WPF proof

One bounded synthetic case, `A_driver_retypes_the_name_chooses_a_workflow_and_the_output_is_named_from_it`.
Synthetic PNG artwork only; no Meitu or Photoshop executable is launched.

Real WPF `WorkflowSelectionView` on an STA thread, real `WorkflowSelectionViewModel`, real
`SessionService`, real `FileWorkspace`, real SQLite. The driver:

1. reads the rendered `WorkflowSelection.SourceFile` text → `live-proof.png`;
2. confirms a rendered `Image` actually has a source → the picture is on screen;
3. reads the rendered box → `live-proof`, the source-derived default;
4. obtains `IValueProvider` from the `TextBox`'s automation peer and calls `SetValue("Live Proof Name")`;
5. obtains `IInvokeProvider` from the `WorkflowSelection.PrepareDesignAsset` button's peer and calls
   `Invoke()`, then pumps the dispatcher — as a real desktop does — until the command completes.

No view-model property is set and no command is executed directly; no coordinate is used.

Independent verification afterwards: `navigation.SessionFor` opened on `PrepareAsset` with
`OutputName = "Live Proof Name"`; SQLite's `Session` row holds `"Live Proof Name"` and the
`InputSnapshot` still holds `OriginalFileName = "live-proof.png"`. The session is then driven one
deterministic step (`ConfirmOriginal` → `StartStep(Enhancement)` against the fake adapter) and the
produced Revision's file is `Live Proof Name_HD.png` — the edited base, through the unchanged
preset pattern.

---

## 6. Build and suite

* Clean build of `PrintFlowStudio.sln`: **0 warnings, 0 errors**.
* **Full suite: RUN — 11,541 passed, 0 failed, 0 skipped.**

The scoped policy would have permitted targeted tests only, but this slice made one change inside
`WorkflowEngine` (probing `CommandKind.SetOutputName`), which alters what `AvailableCommands`
reports for every session. That is a real core read-model boundary, so a complete suite was run
rather than claimed unnecessary. Baseline was 11,525; the 16 new cases account for the difference
exactly.

---

## 7. Jira reassessment

Each row was reassessed against the exact original CSV Description re-read after implementation,
independently of the other.

### SCRUM-11075 — **PARTIAL → FULL**

| Clause | Verdict |
| --- | --- |
| "editable operator-facing Output Name" | **Now met.** A real `TextBox` on Workflow Selection, before the workflow runs, offered on the engine's own `SetOutputName` availability |
| "defaulted from the source filename" | Met — seeded from what `ImportAsync` derived with `OutputName.Sanitise` |
| "without renaming the user's source" | Met — the source copy is read-only and `SourceFileName` still reports the original name after an edit; asserted |
| "Sanitize Windows-invalid characters" | Met and unchanged — `Sanitise` for the derived default, `Create` for operator input, whose refusal is now shown truthfully instead of being unreachable |
| "generate names such as Name_HD.png …" | Met and unchanged; asserted end-to-end from an edited base |
| "_02, _03 … rather than silently overwriting" | Met and unchanged; existing coverage green |

### SCRUM-11078 — **PARTIAL → FULL**

| Clause | Verdict |
| --- | --- |
| "Home/Drop entry flow for exactly one image" | Met (pre-existing) |
| "Decode and preview the file before external automation" | **Now met on the selection screen** — the operator sees the design before any workflow is chosen, and long before an adapter runs |
| "save an InputSnapshot" | Met (pre-existing); re-verified live |
| "show filename" | **Now met** — `WorkflowSelection.SourceFile`, distinct from the output name |
| "and editable output name" | **Now met** — see SCRUM-11075 |
| "present the three fixed workflow choices" | Met and unchanged — still exactly three, from `WorkflowCatalog` |
| "Multiple files must be rejected clearly" | Met (pre-existing); coverage green |
| "selection may change only until the first processed result exists" | Met and unchanged — `CanSelect` reads the engine; re-asserted |

### Parent SCRUM-11068 — **remains PARTIAL**

Closing SCRUM-11075 removes the "output-name editing" half of the Epic's recorded remaining gap.
The other half stands: **SCRUM-11076** is still PARTIAL. `AutomationLogEntry` and `Setting` are
created by migration `0001` and, verified again in this slice, have **no reader and no writer
anywhere in C#** — two of the seven record types the Epic's "SQLite metadata persistence" clause
covers are never persisted. The Epic is not FULL.

### Parent SCRUM-11077 — **PARTIAL → FULL**

Reassessed against the exact original `11200` Description, clause by clause, not inferred from child
labels:

| Epic clause | Where it is satisfied |
| --- | --- |
| "single-image import confirmation" | Home single-file import + `OriginalConfirmation`, now with the file named and drawn **before** the route is chosen (this slice) |
| "side-by-side or slider comparison" | `SharedReviewSurface` — both modes (SCRUM-11079) |
| "synchronised zoom and pan" | `SharedReviewSurface` normalised bidirectional pan and shared zoom (SCRUM-11079); this closed the historical Epic-row gap "comparison surface incomplete" |
| "transparency inspection backgrounds" | checkerboard, white and black, selectable (SCRUM-11079) |
| "structured approve/reject decisions" | `ReviewDecision` bound to the reviewed SHA-256, seven quick reasons (SCRUM-11080) |
| "alpha-based trimming …" | `DeterministicAlphaTrimProcessor`; bounds now persisted and surfaced as `SessionView.CurrentTrimGeometry` (SCRUM-11081 bounds contract) — this closed the historical Epic-row gap "trim bounds never returned to the product" |
| "… with manual crop fallback when useful transparency is absent" | manual crop with tight/uniform/per-edge adjustment and persisted geometry (SCRUM-11082) |
| "Trimming is an independent review step between background removal and physical sizing" | mandatory reviewed step, with explicit **Keep original extent** cancellation (SCRUM-11083) |
| "returning upstream must invalidate all downstream derived results" | `WorkflowEffect.InvalidateDescendants` on `ReturnToStep`, reset and rejection (SCRUM-11084) |

Both gaps the historical Epic row named are closed, and SCRUM-11078 — the last child still
PARTIAL — is closed by this slice. Every child of SCRUM-11077 is now FULL.

This is a requirement-and-evidence assessment. The two clauses the Epic row itself called out were
re-verified directly in the current source (`SharedReviewSurface`'s three backgrounds and two
comparison modes; `SessionView.ArtefactTrimGeometry`/`CurrentTrimGeometry`); the remaining clauses
rest on the recorded child completions and their own evidence, each of which carries its own report.

No external Jira mutation was performed.

---

## 8. Git state

Local commits only. No branch, worktree or alternate clone was created; nothing was pushed,
amended or rebased; no AI-attribution trailer was added.

Files changed:

```text
 M src/PrintFlow.App/Resources/Strings.cs
 M src/PrintFlow.App/Resources/Strings.resx
 M src/PrintFlow.App/Resources/Strings.zh-CN.resx
 M src/PrintFlow.App/ViewModels/ArtefactPreviewPane.cs
 M src/PrintFlow.App/ViewModels/WorkflowSelectionViewModel.cs
 M src/PrintFlow.App/Views/WorkflowSelectionView.xaml
 M src/PrintFlow.Domain/Files/OutputName.cs
 M src/PrintFlow.Workflow/Engine/WorkflowEngine.cs
 M src/PrintFlow.Workflow/Services/SessionView.cs
 M tests/PrintFlow.Tests/Fixtures/HomeScreenHarness.cs
 A tests/PrintFlow.Tests/Integration/Ui/OutputNameAndSourceContextTests.cs
 A tests/PrintFlow.Tests/Integration/Ui/WorkflowSelectionAccessibilityTests.cs
 A docs/printflow/scrum-11075-11078-output-name-import-selection-completion.md
 M docs/printflow/original-jira-functional-coverage-reaudit.md
```

**PASS — SCRUM-11075 / SCRUM-11078 UX COMPLETION VERIFIED**
