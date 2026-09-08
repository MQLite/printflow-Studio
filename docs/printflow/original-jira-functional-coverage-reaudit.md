# PrintFlow Studio — Original Jira Functional Coverage Re-Audit

| Item | Value |
| --- | --- |
| Audit date | 4 September 2026 |
| Repository | `d:\Repositories\printflow-Studio`, `master` at `9be97b5`, working tree clean |
| Jira authority | `printflow_studio_mvp_jira_epics_tasks.csv` (79 rows, original import) |
| Implementation authority | Current source tree, current WPF composition, current preset/evidence on `D:\PrintFlowStudio` |
| Scope | SCRUM-11060 … SCRUM-11138 (8 Epics, 71 Tasks) |
| Nature | **Audit only.** No product source, test, configuration, preset or evidence was changed. |
| Test suite | Not re-run for this audit (per instruction). Latest recorded full-suite figure: 10,158 passing / 0 failed / 0 skipped, at the SCRUM-11137 prerequisite attempt-timing gate. |

---

## 0. Jira key reconciliation — read this first

The repository's phase and gate documents use an **internal numbering scheme** (`11000`, `11100`,
`11200` … `11700`, tasks `11001–11007`, `11101–11108`, …). The Jira keys in this audit's scope are
**not** those numbers. The original CSV export holds exactly 79 rows and Jira assigned them
contiguous keys, so the mapping is a fixed offset:

```text
SCRUM key = CSV "Work Item ID" position, starting at SCRUM-11060
```

| CSV id | Jira key | Title |
| --- | --- | --- |
| 11000 | SCRUM-11060 | Establish PrintFlow Studio Fixed Production Environment Baseline (Epic) |
| 11100 | SCRUM-11068 | Build PrintFlow Studio Core Desktop and Workflow Foundation (Epic) |
| 11200 | SCRUM-11077 | Implement Image Review, Comparison and Deterministic Trimming (Epic) |
| 11300 | SCRUM-11085 | Automate Meitu Enhancement and Background Removal Safely (Epic) |
| 11400 | SCRUM-11093 | Generate and Validate Production TIFF Outputs with Photoshop (Epic) |
| 11500 | SCRUM-11107 | Implement Automation Safety, Environment Validation and Crash Recovery (Epic) |
| 11600 | SCRUM-11115 | Complete Operator UX, Localisation, Diagnostics and Offline Packaging (Epic) |
| 11700 | SCRUM-11124 | Execute Automated QA and Fixed-Workstation Production Acceptance (Epic) |
| 11204 | SCRUM-11081 | Implement Alpha-Based Automatic Trimming |
| 11702 | SCRUM-11126 | Test Trimming and Manual Crop Deterministically |
| 11713 | SCRUM-11137 | Measure Post-MVP Operator-Time Outcome |

The mapping is verified against every anchor the task brief supplied (11077 → trimming Epic,
11078–11084 → its seven children, 11126 → trimming acceptance, 11137 → operator-time measurement,
11138 → final gate). **This is a live collision hazard**: the repo's `phase-11100` plan calls
naming "Task 11107", but SCRUM-11107 is the Automation Safety Epic. Nothing in the repository
records the Jira keys at all.

---

## 1. Current operator-flow map (walked from the composition root)

`MainWindow.xaml` declares exactly **four** `DataTemplate` destinations, and `INavigationService`
exposes exactly **four** navigation methods. There is no other reachable screen.

```text
                    ┌──────────────────────────────────────────┐
                    │ Home  (HomeView / HomeViewModel)         │
                    │  · startup-recovery one-line summary     │
                    │  · preset verified yes/no                │
                    │  · drop one file / Choose file           │
                    │  · Recent Processing (30 d / 100)        │
                    │       → Resume | Details | Abandon       │
                    └───────┬───────────────────────┬──────────┘
                            │ import                │ ShowEnvironment
                            ▼                       ▼
        ┌───────────────────────────────┐   ┌──────────────────────────────┐
        │ Workflow Selection            │   │ Production Readiness         │
        │  · session output name (RO)   │   │  (EnvironmentReadinessView)  │
        │  · three fixed workflows      │   │  · 12 checks, read-only      │
        │  · Back to Home               │   │  · Refresh, Back             │
        └───────────────┬───────────────┘   └──────────────────────────────┘
                        │ SelectWorkflow
                        ▼
        ┌──────────────────────────────────────────────────────────────────┐
        │ Session screen (SessionScreenView / SessionViewModel, 3 439 LoC) │
        │  Import · Original Confirmation · Enhancement · Background       │
        │  Removal · Trim · Approved PNG Export · Print Dimensions ·       │
        │  Photoshop Output                                                │
        │  Commands: Confirm Original, Run Step, Approve, Reject(+reason   │
        │  +notes), Retry, Skip, Hand Off, Stop, Take Over, Re-enter       │
        │  Automation, Manual Crop (begin/apply/cancel), Trim margin,      │
        │  Return to step, Size preset/custom/max-bounds, Authorise        │
        │  enlargement, White underbase branch, Add Another Size,          │
        │  Complete, Back to Home, Zoom in/out/reset                       │
        └──────────────────────────────────────────────────────────────────┘
```

**Screens the original Jira asks for that do not exist:**

* **Settings** (SCRUM-11118) — no view, no view model, no navigation. The `Setting` SQLite table
  created in migration `0001` has **no reader and no writer** anywhere in the product.
* **Error Details** (SCRUM-11120) — failures surface as one localised sentence plus a stable
  `FailureCode` inline on whichever screen issued the command.
* **Diagnostic package export** (SCRUM-11122) — no command, no service, no UI.
* **Language switcher** (SCRUM-11119) — `Strings.cs` states in its own doc comment: *"A runtime
  language switcher is a later slice."*
* **Editable Output Name** — `WorkflowCommand.SetOutputName` exists in the engine and has **no
  screen**. `WorkflowSelectionView.xaml` binds `SessionName` to a read-only `TextBlock`.
* **Per-entry startup-recovery surface** — `HomeViewModel.StartupSummary` states in its own doc
  comment: *"the surface that lists individual recovery entries is a later slice."*

---

## 2. Full coverage table

Legend for the boolean columns: `Y` yes, `N` no, `P` partial, `—` not applicable.

### Epic SCRUM-11060 — Fixed Production Environment Baseline

| Jira | Original Task | Current Status | Product Reachable | Integrated | Persisted | Automated Evidence | Live Evidence | Missing AC | Priority |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **SCRUM-11060** | *Epic* | **PARTIAL** | — | — | — | — | — | Two children waived at acceptance rather than delivered | P3 |
| SCRUM-11061 | Capture Fixed Windows Workstation Contract | **FULL** | Y (Production Readiness) | Y | Y (preset v1.16.0) | `WorkstationVerificationBoundaryTests`, `ProductionWorkstationVerifierTests` | `signoff\11001.json`; 11000 final report §7 | — | — |
| SCRUM-11062 | Record Meitu Production Configuration and Operating Steps | **PARTIAL** | Y | Y | Y | `PresetMeituBaselineProviderTests` | 11 Meitu baseline screenshots; `signoff\11002.json` | 11000 final report explicitly waives the **abnormal-dialog / collision catalogue** the AC requires for "later safe-stop handling" | P3 |
| SCRUM-11063 | Record Photoshop Production Configuration | **FULL** | Y | Y | Y | `WorkstationPresetProviderTests` | Colour-settings, workspace and panel screenshots; `signoff\11003.json` | — | — |
| SCRUM-11064 | Freeze and Validate the Photoshop Production Action | **FULL** | Y | Y | Y (`.atn` hash in the `PhotoshopActionArtifact` check) | `WorkstationPresetW1ContractEvidenceTests` | `PrintFlow-DTF-v1.atn` `A04203ED…83EE`; W1_1px recorded/replay screenshots | — | — |
| SCRUM-11065 | Build the Standard Local Regression Image Set | **NOT_IMPLEMENTED** | N | N | N | — | `D:\PrintFlowStudio\TestData\v1\inputs` holds **one** file (`FIX-CUSTOMER-DESIGN-001.jpeg`) | Seven required categories (JPG portrait, fine-hair background, transparent PNG, complete customer design, PSD with composite, single-page PDF, reference TIFF) absent. Waived in the 11000 report as "Confirmed by acceptance". **Blocks SCRUM-11136.** | P3 |
| SCRUM-11066 | Capture Current Manual Processing Benchmark | **NOT_IMPLEMENTED** | N | N | N | — | None | No timing data for ≥20 real manual cases exists anywhere. Waived as "Confirmed by business acceptance". **Hard-blocks SCRUM-11137** — there is nothing to compare against. | P3 |
| SCRUM-11067 | Validate and Archive a Maintop-Proven Reference TIFF | **PARTIAL** | — | Y | Y | — | Maintop v6.1 loaded and previewed the reference TIFF (11000 report §6); `maintop-v6.1-import-w1-1px-tiff-001.png` | The report states plainly: *"No separate physical print was produced or photographed during this validation session."* | P3 |

### Epic SCRUM-11068 — Core Desktop and Workflow Foundation

| Jira | Original Task | Current Status | Product Reachable | Integrated | Persisted | Automated Evidence | Live Evidence | Missing AC | Priority |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **SCRUM-11068** | *Epic* | **PARTIAL** | — | — | — | — | — | Output-name editing and AutomationLog/Setting persistence | P2 |
| SCRUM-11069 | Create the WPF Solution and Module Boundaries | **FULL** | Y (app launches) | Y | — | `DependencyRuleTests`, `BannedApiEnforcementTests`, `ScopeGuardTests`, `ProductionCompositionTests` | Application runs in Production mode (11600-D) | — | — |
| SCRUM-11070 | Implement Session/Revision/Attempt/Review/PrintOutput Models | **FULL** | Y | Y | Y | `ValueObjectTests`, `DbInvariantTests`, `AddAnotherSizeTests` | 11600-D persistence readback | — | — |
| SCRUM-11071 | Implement the Three Fixed MVP Workflow State Machines | **FULL** | Y (Workflow Selection) | Y | Y | `WorkflowShapeTests`, `TransitionMatrixTests`; `WorkflowCatalog.cs` is the whole configuration | 11500-D Job A/B; 11600-D | — | — |
| SCRUM-11072 | Implement User-Facing States and Valid Transition Commands | **FULL** | Y | Y | Y | `ValidTransitionTests`, `InvalidTransitionTests`, `EnginePurityTests`, `SessionControlsTests` | 11600-B soak (40 jobs) | — | — |
| SCRUM-11073 | Implement Immutable Revision Metadata and Hash-Bound Approval | **FULL** | Y | Y | Y (`Revision`, `ReviewDecision.ReviewedSha256`) | `RevisionIntegrityGuard`, `RetryAndReviewTests`, `DbInvariantTests` CHECK constraints | 11600-D: approved Revision == reviewed Revision, byte-identical | — | — |
| SCRUM-11074 | Implement Controlled Session File Workspace and Input Snapshot | **FULL** | Y | Y | Y | `WorkspaceTests`, `PathGuard`, `WorkspaceImportCancellationTests` | 11600-D: source read-only, hash unchanged, 104 session dirs, 0 lost | — | — |
| SCRUM-11075 | Implement Collision-Safe Output Naming | **PARTIAL** | **N** for the editable name | Y | Y | `SanitiserTests`, `NamingPatternRendererTests`, `ProductionTiffNamingTests`, `AcceptedNamingContractTests` | `qa-gate-design_200mm_CMYK_W.tif` produced live from the preset pattern | **"Editable operator-facing Output Name" has no UI.** `SetOutputName` is an engine command with zero call sites in `PrintFlow.App`. The name is derived from the source stem and cannot be changed by the operator. | **P2** |
| SCRUM-11076 | Implement Transactional SQLite Metadata Persistence | **PARTIAL** | Y | Y | Y | `MigrationTests`, `SessionServiceTests`, `StartupRecoveryTests` | 11600-C restart + readback | **`AutomationLogEntry` has no writer** and **`Setting` has no reader or writer** — two of the seven record types the AC names are never persisted. | P2 |

### Epic SCRUM-11077 — Image Review, Comparison and Deterministic Trimming

| Jira | Original Task | Current Status | Product Reachable | Integrated | Persisted | Automated Evidence | Live Evidence | Missing AC | Priority |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **SCRUM-11077** | *Epic* | **PARTIAL** | — | — | — | — | — | Comparison surface incomplete; trim bounds never returned to the product | **P1** |
| SCRUM-11078 | Build Single-Image Import, Validation and Workflow Selection | **PARTIAL** | Y | Y | Y | `HomeAndWorkflowSelectionTests`, `WorkspaceImportCancellationTests` | 11600-D fresh session from Import | Multiple-file drop rejected clearly ✓; InputSnapshot ✓; three workflow choices ✓; selection locked after the first result ✓. **Missing: the editable output name, and the filename/preview are not shown on the import or selection screen** — the first decode the operator sees is on the Session screen, after the workflow is already chosen. | P2 |
| SCRUM-11079 | Build Shared Before-and-After Review Component | **PARTIAL** | Y | Y | — | `ImagePreviewControlTests`, `ArtefactPreviewTests`, `ViewRenderingTests` | 11200 gate §19 real-window inspection; 11600-D visible preview proof | Side-by-side ✓ (one `DataTemplate` serves both panes and every workflow — no duplicated review logic); **synchronised zoom ✓ but pan is NOT synchronised** (each pane owns an independent `ScrollViewer`, `SessionScreenView.xaml:86`); Fit ✓, 100 % ✓, 8× magnification ✓; **checkerboard only — no white and no black inspection background**; **no slider/overlay comparison mode**. | **P1** |
| SCRUM-11080 | Implement Review Decisions and Rejection Reasons | **FULL** | Y | Y | Y (`ReviewDecision`: `ReviewedSha256`, `Operator`, `DecidedAtUtc`, `QuickReason`, `Notes`) | `RetryAndReviewTests`, `SessionControlsTests`, `PhotoshopTiffFinalReviewTests` | 11600-D explicit approval bound to the reviewed SHA-256 | All seven quick reasons present (`RejectionReason` enum); Approve / Reject / Retry / Hand Off all reachable; no state is inferred without a recorded decision. | — |
| SCRUM-11081 | Implement Alpha-Based Automatic Trimming | **PARTIAL** | Y | Y | **P** | `AlphaBoundsTests`, `TrimBoundsTests`, `TrimMarginTests`, `DeterministicTrimTests`, `TrimStepTests`, `TrimParameterTests`, `TrimBoundaryTests` | 11200 gate §2 live 400×300 confirmation | See §3 below. **The one material gap: the AC's "Return original and final bounds" is not met in the product.** `TrimResult.ContentBounds` / `AppliedBounds` are computed correctly, are asserted only by tests, and are then discarded by `SessionService.cs:1859` — no column, no `SessionView` field, no operator display. | **P0** |
| SCRUM-11082 | Implement Manual Crop Fallback for Non-Transparent Inputs | **PARTIAL** | Y | Y | **P** | `ManualCropProcessorTests`, `ManualCropStepTests`, `ManualCropUiTests`, `CropSurfaceLayoutTests` | 11200 gate §5, §6 | No colour inference anywhere ✓ (source-wide scan, 11200 gate §3); crop mode opens only on a genuine `ManualCropRequired` (`ManualCropEligibility`) ✓; the drawn rectangle is validated against the decoded canvas and refused, never clamped ✓; Cancel touches nothing ✓; the crop becomes the authoritative `ManualImport` Revision ✓. **Missing: the AC's "tight trim, uniform margin, independent edge adjustment" controls exist only on the *automatic* path (`SetTrimParameters`), not in manual crop mode — manual crop is drag-a-rectangle only. The chosen rectangle is not persisted as metadata**; only the resulting file's dimensions survive. | **P1** |
| SCRUM-11083 | Implement Independent Trim Review and Adjustment | **PARTIAL** | Y | Y | Y | `TrimStepTests`, `ReturnAndTrimControlsUiTests`, `TrimParameterTests` | 11200 gate §4, §7 | Trim is a mandatory `RequiresReview: true` step in both workflows that have it ✓; Before/After panes ✓; adjustment writes a new margin, a new attempt and a new Revision that must be reviewed ✓ (it never mutates an approved trim). **Missing: "cancellation" — `StepDefinition.Trim` is `IsSkippable: false` in both workflows, so an operator who wants the untrimmed extent has no cancel path.** | P1 |
| SCRUM-11084 | Implement Downstream Invalidation When Returning Upstream | **FULL** | Y (Return to step) | Y | Y | `ReturnToStepTests`, `ReturnTargetTests`, `RecoveryAndBranchTests` | 11200 gate §12 | `WorkflowEffect.InvalidateDescendants` fires on `ReturnToStep` (`UpstreamChanged`), on reset and on rejection; reprocessing rebinds to the latest approved upstream Revision; prior `ReviewDecision` rows remain history bound to their old hashes. | — |

### Epic SCRUM-11085 — Meitu Automation

| Jira | Original Task | Current Status | Product Reachable | Integrated | Persisted | Automated Evidence | Live Evidence | Missing AC | Priority |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **SCRUM-11085** | *Epic* | **PARTIAL** | — | — | — | — | — | Manual-result import; evidence retention | P1 |
| SCRUM-11086 | Define the Meitu Processing Adapter Contract | **FULL** | — | Y | — | `AutomationBoundaryTests`, `DependencyRuleTests` | — | The port takes input file, operation and working directory only; every selector, marker and coordinate lives inside `Infrastructure/Adapters/Meitu`. | — |
| SCRUM-11087 | Build Deterministic Fake Meitu Adapter | **FULL** | Y (`AdapterMode` config; the shell says so on screen) | Y | — | `FakeAdapterScenarioTests`, `FakeMeituBackgroundRemovalTests` | — | Succeed / FailWith / Timeout / ProduceUnreadableFile / ProduceMissingFile / HangUntilCancelled / ReportPhaseAndWaitForStop; tests observe only the port. | — |
| SCRUM-11088 | Implement Meitu AI Enhancement Automation | **FULL** | Y (Run Step) | Y | Y | `GuardedMeituEnhancementTests`, `MeituEnhancementRuleTests`, `MeituLoadObservationTests` | 11300 gate §5; 11500-D Job B; **20/20 Meitu jobs in the 11600-B soak** | Signed markers, observable completion (never a bare sleep), unknown screens stop. | — |
| SCRUM-11089 | Implement Meitu Background Removal Automation | **FULL** | Y | Y | Y (`BackgroundRemovalAuthority`) | `GuardedMeituBackgroundRemovalTests`, `MeituCutoutOutputRuleTests`, `MeituTransparencyRuleTests`, `BackgroundRemovalUiTests` | 11300 gate §6 | Transparency inspected via `WicMeituTransparencyInspector`; unrecognised screens return structured failure. | — |
| SCRUM-11090 | Validate Meitu Export Before Creating a Revision | **FULL** | — | Y | Y | `MeituOutputValidationTests`, `ProductionMeituExportTests`, `GuardedMeituExportTests` | 11300 gate §8 | `MeituOutputStability` (size settled) → full decode → hash → only then a Revision. Invalid exports stay failed Attempts (enforced by a DB CHECK). | — |
| SCRUM-11091 | Capture Structured Meitu Automation Failure Evidence | **PARTIAL** | P (code shown, screenshot not) | Y | Y (`ProcessingAttempt.FailureDetailJson` carries `Code`, `MessageKey`, `TechnicalDetail`, `Context`) | `ProductionFailurePersistenceTests` | 9 real capture files under `D:\PrintFlowStudio\Evidence` | Session ✓, step ✓, timestamp ✓, screenshot path ✓ (`evidencePath` in `Context`), structured code ✓, bilingual description ✓ (via `MessageKey` + resx), retry count ✓ (`RetrySequence`). **Missing: input path and expected output path are not systematically recorded; the "configured retention policy" is not enforced (see SCRUM-11121); the operator cannot see the screenshot.** | P2 |
| SCRUM-11092 | Implement Meitu Stop and Manual Processing Handoff | **PARTIAL** | Y (Stop, Take Over, Hand Off, Re-enter) | Y | Y | `StopAndTakeOverTests`, `StopAndTakeOverUiTests`, `AutomationStopPolicyTests`, `ForceTerminationPolicyBoundaryTests` | 11300 gate §10–§12 | Stop after the current computation ✓; force termination behind an explicit confirmation ✓; Manual Processing never resumes mid-sequence ✓; `ReenterAutomation` ✓. **Missing: "the completed result may then be imported or discarded" — there is no command anywhere to bring an operator-produced file back into the session.** `WorkflowCommand` has no manual-import member; `OperationKind.ManualImport` is used only by the manual-crop path. | **P1** |

### Epic SCRUM-11093 — Photoshop Production TIFF

| Jira | Original Task | Current Status | Product Reachable | Integrated | Persisted | Automated Evidence | Live Evidence | Missing AC | Priority |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **SCRUM-11093** | *Epic* | **PARTIAL** | — | — | — | — | — | Two of the four declared input formats are absent | **P0** |
| SCRUM-11094 | Implement Locked-Aspect-Ratio Print Dimension Calculation | **PARTIAL** (design supersession with a residual gap) | Y | Y | Y | `FitWithinBoundsTests`, `ScaleToTargetEdgeTests`, `PrintPreparationPlanTests`, `MaximumBoundsUiTests`, `FlexibleSizeUiTests` | 11600-D: 80 mm target edge → 945 × 945 px @ 300 PPI | **Original:** width+height in mm with an aspect-ratio lock and automatic pairing. **Replacement:** a maximum-bound fit box (`FitWithinBounds`) or one exact target edge (`ScaleToTargetEdge`); the paired dimension is derived, never entered, so non-proportional stretch is *unconstructible* rather than merely forbidden. **Why changed:** a lock toggle can be unlocked; a single-edge contract cannot. **Outcome satisfied?** For "no silent stretch", yes, and more strongly. **Not satisfied: "display of detected graphic bounds"** — the trim's content bounds never reach the screen (SCRUM-11081). | **P1** |
| SCRUM-11095 | Implement Effective-DPI Resolution Risk Rules | **PARTIAL** (design supersession with a residual gap) | Y | Y | Y (`EnlargementAuthority` rows) | `EnlargementAuthorityTests`, `FlexibleSizeWorkflowTests`, `FlexibleSizeRenderingTests` | 11400 gate §4 | **Original:** sufficient / warning / blocking states from effective-DPI thresholds established by real print tests. **Replacement:** output is fixed at 300 PPI, so any shortfall is exactly "this is an enlargement"; an enlargement is *blocked* until the operator records an explicit `AuthoriseEnlargement` against that specific offer id. **Why changed:** no print-test thresholds were ever captured (SCRUM-11065/11066 waived), so invented numbers were refused. **Outcome satisfied?** The block half, yes. **Not satisfied: effective DPI is never computed or displayed anywhere in the product** (no such concept exists in the source), nor are graphic bounds, and there is no graduated warning band. | **P1** |
| SCRUM-11096 | Define the Photoshop Output Adapter Contract | **FULL** | — | Y | — | `PhotoshopBoundaryTests`, `AutomationBoundaryTests` | — | The port takes an approved input, a preparation, the preset ref, the branch and an output name. Selectors, Actions, dialogs and save steps all sit behind `IPhotoshopUiDriver`. | — |
| SCRUM-11097 | Build Deterministic Fake Photoshop Adapter | **PARTIAL** | Y | Y | — | `FakeAdapterScenarioTests`, `ProductionTiffInspectorTests` | — | The fake expresses **export failure, timeout, interruption, unknown dialog, missing output and unreadable output** (as scenarios and `FailureCode`s), and tests never assert click order ✓. **The structural faults the AC names by content — missing white channel, wrong colour mode, wrong dimensions, incorrect metadata — are not producible by the fake adapter**; they are covered instead by `ProductionTiffFixture` driving `ProductionTiffInspector` directly (11 fault variants). The business outcome is covered; the stated seam is not. | P3 |
| SCRUM-11098 | Implement PNG and JPEG Production Input Path | **FULL** | Y | Y | Y | `PhotoshopWorkflowOutputTests`, `PhotoshopTiffWorkflowOutputTests` | 11600-D (PNG, real customer order); 11500-D Job A | Managed working copies; source preserved read-only; only an approved upstream Revision may produce a TIFF. | — |
| SCRUM-11099 | Implement PSD Input Preparation | **NOT_IMPLEMENTED** | **N** | **N** | N | None | None | **No PSD path exists.** `FormatSniffer` returns `ImageFormat.Psd` and `DisplayNames` can label it; nothing else in `Workflow`, `Infrastructure/Adapters` or `App` references PSD. There is no composite-preview detection, no colour-mode/alpha/spot-channel inspection, and no Photoshop-driven flatten to a managed working copy. **Worse than absent — it fails late:** `Home_ImportFilter` advertises `*.psd`, `WicFileInspector` records the format with **null pixel dimensions**, and the session then dies at Print Dimensions with `PreconditionNotMet` (`SessionService.cs:671`) after the operator has already imported and chosen a workflow. | **P0** |
| SCRUM-11100 | Implement Single-Page PDF Input Preparation | **NOT_IMPLEMENTED** | **N** | **N** | N | None | None | No PDF path exists. No page-count read, **no multi-page rejection**, no rasterisation at production DPI. PDF is not even offered in the file filter, so the explicit "reject multi-page PDFs" AC has nothing to run against. | **P0** |
| SCRUM-11101 | Handle Existing White-Ink Spot Channels Explicitly | **PARTIAL** | N (no prompt) | Y | — | `PhotoshopPreparationBoundaryTests`, `PhotoshopW1BoundaryTests` | 11400 gate §5, §6 | **No silent choice is made** ✓ — `GuardedPhotoshopDocumentPreparer.cs:477` refuses outright when `W1Exists` or any spot channel is present, and W1 polarity/density/choke remain preset concerns. **Missing: the AC's "stop and ask the operator whether to retain the existing white ink or regenerate"** — there is no operator question, only a hard failure. (Moot in practice today, because the PSD/PDF preparation that would surface an existing W1 does not exist.) | **P1** |
| SCRUM-11102 | Execute the Validated Photoshop Production Action | **FULL** | Y (Run Step) | Y | Y | `PhotoshopW1ExecutionTests`, `PhotoshopPreparationTests`, `GuardedPhotoshopUiDriverTests` | 11600-D: the signed Action ran exactly once, `RetrySequence = 0`; **20/20 Photoshop jobs in the 11600-B soak** | Managed copy opened, 300 PPI applied with the validated resample policy, confirmed colour settings, W1 via the signed Action, validated save options; unknown dialogs and unsaved documents stop automation. | — |
| SCRUM-11103 | Validate Generated Production TIFF | **FULL** | — | Y | Y (hash on `PrintOutput` before review) | `ProductionTiffInspectorTests` (11 rejection variants), `PhotoshopTiffSaveTests` | 11400 gate §7; 11600-D TIFF contract | Existence, stability, full reopen, 300 DPI, CMYK sample layout, W1 spot channel present **and non-empty**, bit depth, compression, byte order, no pyramid, no RLE layer data. Hash persisted before final review. | — |
| SCRUM-11104 | Build TIFF Final Review Mode | **PARTIAL** | Y | Y | Y | `PhotoshopTiffFinalReviewTests`, `TiffFinalReviewUiTests`, `PhotoshopFinalReviewBoundaryTests` | 11400 gate §9; 11600-D human review + approval | Approval binds to the exact `PrintOutput` hash ✓; no successful automation can complete the session without it ✓; a flattened preview renders ✓; the W1 branch and "CMYK + W1 validated" are stated ✓. **Missing: the AC's CMYK colour preview, separate white-ink channel preview and colour-plus-white overlay; and of the required production metadata, effective DPI and the output path are not shown.** | **P1** |
| SCRUM-11105 | Support Multiple Independently Reviewed TIFF Sizes | **PARTIAL** | Y (Add Another Size) | Y | Y | `AddAnotherSizeTests`, `MaximumBoundsPlanTests` | 11400 gate §11 | The implementation is complete and correct: each output rebinds to the same approved source Revision, gets its own preparation, validation, hash and review, and approving one never touches another. Marked PARTIAL only because the **acceptance run of ≥3 sizes (SCRUM-11133) has not been executed**; the capability itself is FULL. | P3 |
| SCRUM-11106 | Move Rejected PrintFlow TIFFs to the Windows Recycle Bin | **FULL** | Y (Reject) | Y | Y (rejection + invalidation + recycle recorded) | `PhotoshopTiffFinalReviewTests` | 11400 gate §9 | Real `SHFileOperation` recycle, **no hard-delete fallback anywhere** in the call chain; applies to PrintFlow outputs only; the operator's source is never touched. Known NOTE: a crash between recycle and commit leaves a restorable file and records nothing untrue. | — |

### Epic SCRUM-11107 — Automation Safety, Environment Validation and Crash Recovery

| Jira | Original Task | Current Status | Product Reachable | Integrated | Persisted | Automated Evidence | Live Evidence | Missing AC | Priority |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **SCRUM-11107** | *Epic* | **PARTIAL** | — | — | — | — | — | Environment-check coverage; recovery choices; retention cleanup | P1 |
| SCRUM-11108 | Implement the Global Automation Lock | **FULL** | Y (refusals surface) | Y | Y (`AutomationLock` row with `ProcessId` + `MachineName`) | `EnvironmentGateTests`, `SessionHygieneAndRecoveryTests`, `ProcessLivenessTests` | 11600-B: every one of 40 jobs released the lock; 11600-D lock free | Single owner, crash-detectable ownership, stale detection at startup, protects external-app automation only. | — |
| SCRUM-11109 | Implement Recognised Safe Starting-State Detection | **FULL** | Y (refusals shown) | Y | — | `MeituStateClassifierTests`, `PhotoshopStateClassifierTests`, `ExternalStateHygieneTests`, `PhotoshopFoundationTests` | 11600-C matrices §4–§8 | Positively recognised states only; everything else is `Unknown` and stops. PrintFlow never closes an unknown document. | — |
| SCRUM-11110 | Build the Operating Environment Check Page | **PARTIAL** | Y (Production Readiness) | Y | Y (preset-derived) | `EnvironmentReadinessScreenTests`, `ProductionWorkstationVerifierTests`, `VerifiedEnvironmentGateTests` | 11500-C/D; 11600-D readiness pass | Twelve checks, each named and individually reported with expected-vs-current detail, **no automatic repair** ✓. **Missing four AC items:** Meitu/Photoshop are verified by path + SHA-256 but **not launched**; **Photoshop colour settings are not confirmed** by any check; **no "a test image can be opened and closed" check**; **no unsaved-document / unknown-dialog check on this page** (that lives only in the per-attempt starting-state gate). | **P1** |
| SCRUM-11111 | Block High-Risk Automation on Environment Drift | **FULL** | Y | Y | Y | `VerifiedEnvironmentGateTests`, `ProductionAdapterGateTests`, `ProductionGateSideEffectTests`, `ProductionActivationBoundaryTests` | 11500-D activation gate | One gate answers both "may Production run" and "why not", so the screen and the refusal cannot disagree. Selectors and coordinates are never adapted to an unknown environment. | — |
| SCRUM-11112 | Implement Interrupted Attempt and Startup Recovery | **PARTIAL** | P (summary only) | Y | Y | `StartupRecoveryTests`, `SessionHygieneAndRecoveryTests`, `ApplicationStartupTests` | 11600-C §15 restart + readback; 11600-B restart checkpoint | Unfinished attempts → `INTERRUPTED`, stale locks released on confirmed process death, orphaned working files quarantined, **never resumes from a prior mouse position** ✓. Restart-the-step ✓ and Abandon ✓ are reachable. **Missing: the AC's third option — "inspection/import of a manually saved result" — does not exist; and Home shows only a three-number recovery summary, with no per-entry recovery surface** (stated as deferred in `HomeViewModel`). | **P1** |
| SCRUM-11113 | Implement Clean Retry Semantics | **FULL** | Y (Retry → Run Step) | Y | Y (`RetryOfAttemptId`, `RetrySequence`) | `RetryAndReviewTests`, `RecoveryAndBranchTests` | 11600-B job 3r retried cleanly into its own attempt folder | Every retry is a new attempt with a fresh working copy from `UpstreamRevisionOf(step)`; failed evidence is retained; a failed attempt can never carry an output Revision (DB CHECK). | — |
| SCRUM-11114 | Implement Safe Completion and Retention Cleanup | **IMPLEMENTED_NOT_INTEGRATED** | **N** | **N** | — | `WorkspaceTests` exercises `CleanupWorking` directly; `PhotoshopFinalReviewBoundaryTests.CleanupWorking_still_has_no_production_interpreter_or_caller` **asserts it stays unwired** | 11400 gate §15 records this as a deliberate NOTE | `WorkflowEffect.CleanupWorking` is emitted by `WorkflowEngine.Complete` for every workflow and **has no production interpreter**; `IWorkspace.CleanupWorking` **has no production caller**. Source preservation ✓ and Recycle Bin for rejected TIFFs ✓ are met; **nothing else is** — no working copies are removed, no rejected Meitu-derived files are cleaned at session end, and "safe to delete" has no definition, because Revisions now point *into* `Working\`. Working artefacts accumulate for the life of the workspace. | **P1** |

### Epic SCRUM-11115 — Operator UX, Localisation, Diagnostics and Offline Packaging

| Jira | Original Task | Current Status | Product Reachable | Integrated | Persisted | Automated Evidence | Live Evidence | Missing AC | Priority |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **SCRUM-11115** | *Epic* | **PARTIAL** | — | — | — | — | — | Three of eight children have no product at all | **P2** |
| SCRUM-11116 | Complete the Home and Single-Image Drop Experience | **PARTIAL** | Y | Y | — | `HomeAndWorkflowSelectionTests`, `HomeScreenHarness`, `ViewRenderingTests` | 11400 gate §10 human visual inspection (en-US + zh-CN) | One-image drop over the whole screen ✓; multiple-file drop refused with a count ✓; workflow-selection entry ✓; Recent Processing on the page ✓; internal terms hidden ✓. **Missing: unsupported-file feedback is a generic "could not be imported ({code})" rather than a format-specific message** — and it is actively misleading for PSD, which imports successfully and fails several steps later. | P2 |
| SCRUM-11117 | Implement Recent Processing with Resume and Record Management | **PARTIAL** | Y | Y | Y | `HomeAndWorkflowSelectionTests` | 11400 gate §10 | 30 days **and** 100 sessions, whichever first ✓ (`RecentSessionLimit = 100`, `RecentSessionWindow = 30 days`); display name, workflow, current step, state, timestamp ✓; Resume/Details ✓; older records disappear without deleting production files ✓. **Missing: the thumbnail the AC names, and the delete-record action** — `Abandon` is offered instead and explicitly deletes nothing. | **P2** |
| SCRUM-11118 | Complete Settings and Production Preset Display | **NOT_IMPLEMENTED** | **N** | **N** | N | None | None | **There is no Settings screen.** None of the seven items exists as an operator surface: default output root, UI language, trim safety margin *default*, fixed production DPI display, workstation-preset details, Photoshop colour-settings confirmation, log retention. What exists instead is a one-line "preset verified / not verified" on Home plus the read-only Production Readiness screen. The `Setting` table has no reader and no writer. | **P2** |
| SCRUM-11119 | Implement Simplified Chinese and English Localisation | **PARTIAL** | P | Y | — | `LocalisationResourceTests` (342 / 342 key parity) | 11400 gate §10: both locales visually signed off | Complete `Strings.resx` + `Strings.zh-CN.resx`; print-production terminology; internal state names, `FailureCode`s and adapter ids stay stable English ✓. **Missing: there is no language selection anywhere, so "English selection in Settings without application restart" is not met; and Simplified Chinese is not an app-enforced first-run default** — resolution follows the OS `CurrentUICulture` (nothing sets it), which happens to be zh-CN on the one supported workstation. | **P2** |
| SCRUM-11120 | Build Structured Error Details and Recovery UI | **PARTIAL** | P | Y | Y (in `FailureDetailJson`) | `SessionControlsTests`, `ProductionFailurePersistenceTests` | 11600-C failure matrices | The recovery actions are correct: Retry and Manual Processing are offered exactly where the engine allows them, and **no misleading Continue exists** ✓. Bilingual description ✓ via `MessageKey`. **Missing: there is no Error Details page.** A failure is one localised sentence plus a stable code on the current screen; the captured screenshot, input path, expected output path and retry count are persisted but never shown. | **P2** |
| SCRUM-11121 | Implement Local Log and Screenshot Retention | **PARTIAL** | **N** | P | Y (screenshots on disk) | None for retention | 9 capture files under `D:\PrintFlowStudio\Evidence` | Local only, **no upload of any kind** ✓; approved files and the source are never touched ✓. **Missing almost everything else: there is no logging framework in the product at all** (no `ILogger`, no Serilog, no log-file writer — `AutomationLogEntry` is never written); `Logging:RetentionDays = 30` is read into `LoggingConfiguration` and **has no consumer, so no cleanup ever runs**; retention settings and stored locations are not visible to the operator. | **P2** |
| SCRUM-11122 | Implement Explicit Diagnostic Package Export | **NOT_IMPLEMENTED** | **N** | **N** | N | None | None | No export command, service, manifest preview or confirmation step exists. (The "no automatic upload" half of the AC is satisfied vacuously — nothing uploads because nothing exports.) | P4 |
| SCRUM-11123 | Build a Versioned Offline Installer and Upgrade Procedure | **NOT_IMPLEMENTED** | **N** | **N** | N | None | None | No installer project, script or artefact of any kind (`.wxs`, `.iss`, `.wixproj`, publish script — none present). No documented installation or rollback-to-previous-version procedure; `production-activation-runbook.md` covers only rolling the *adapter mode* back to Fake. The "no automatic update mechanism" half is satisfied vacuously. | P4 |

### Epic SCRUM-11124 — Final QA and Fixed-Workstation Production Acceptance

| Jira | Original Task | Current Status | Product Reachable | Integrated | Persisted | Automated Evidence | Live Evidence | Missing AC | Priority |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **SCRUM-11124** | *Epic* | **PARTIAL** | — | — | — | — | — | Four acceptance runs never executed | P3 |
| SCRUM-11125 | Test Workflow State Transitions and Invariants | **FULL** | — | — | — | `ValidTransitionTests`, `InvalidTransitionTests`, `TransitionMatrixTests`, `WorkflowShapeTests`, `RecoveryAndBranchTests`, `ReturnToStepTests`, `EnginePurityTests`, `ScopeGuardTests` | — | All three workflows, valid/invalid, skip, reject, retry, handoff, downstream invalidation, interruption, completion; UI bypass prevented architecturally; failed attempts cannot create Revisions (DB CHECK); approvals hash-bound. | — |
| SCRUM-11126 | Test Trimming and Manual Crop Deterministically | **PARTIAL** | — | — | — | `AlphaBoundsTests`, `TrimBoundsTests`, `TrimMarginTests`, `DeterministicTrimTests`, `ManualCropProcessorTests`, `ManualCropStepTests`, `TrimParameterTests`, `TrimBoundaryTests` | 11200 gate §2–§6 | Covered: alpha bounds ✓, safety margins ✓, transparent input ✓, no-transparency input ✓, partially transparent edges ✓ (the `alpha > 0` rule is asserted explicitly), manual crop ✓, invalid crop bounds ✓, crop cancellation ✓, no colour-based guessing ✓ (source-wide scan). **Not covered — and untestable as the product stands: "physical-canvas and bounds metadata remain consistent."** Bounds metadata is never persisted (SCRUM-11081), so there is nothing to assert consistency against outside the processor's own return value. See §4. | **P1** |
| SCRUM-11127 | Test SQLite, File Workspace and Safe Cleanup Integration | **PARTIAL** | — | — | — | `MigrationTests`, `DbInvariantTests`, `WorkspaceTests`, `SessionServiceTests`, `StartupRecoveryTests`, `NamingContractWorkflowRegressionTests` | 11600-C restart readback | Temporary DBs and directories ✓, transactions ✓, InputSnapshots ✓, Revision registration and hashes ✓, collision-safe names ✓, restart persistence ✓, Recycle Bin ✓, fixtures never overwritten ✓, invalidated/rejected outputs cannot remain current ✓. **"Safe cleanup" is tested only against `FileWorkspace` in isolation, because the production path is deliberately unwired (SCRUM-11114).** | P2 |
| SCRUM-11128 | Execute the Meitu Adapter Failure and Recovery Matrix | **FULL** | — | — | — | `FakeAdapterScenarioTests`, `GuardedMeitu*Tests`, `MeituOutputValidationTests`, `StopAndTakeOverTests`, `ProductionMeituProcessorTests` | 11300 gate §13; 11600-C §6 | Success, timeout, unknown dialog, missing output, still-changing output, unreadable output, interruption, review rejection, clean retry, Stop and takeover all covered; only validated exports create Revisions; unsafe states stop with local evidence. | — |
| SCRUM-11129 | Execute the Photoshop Adapter Failure and Validation Matrix | **PARTIAL** | — | — | — | `ProductionTiffInspectorTests` (11 variants), `PhotoshopTiffSaveTests`, `PhotoshopW1ExecutionTests`, `GuardedPhotoshopUiDriverTests`, `FinalReviewFaults` | 11400 gate §13; 11600-C §5; the two natural soak failures (`PhotoshopTargetLost`, `PhotoshopUnknownState`) | Successful TIFF ✓, missing/empty white channel ✓, wrong colour mode ✓, incorrect DPI ✓, invalid/unreadable output ✓, timeout ✓, interruption ✓, unknown dialog ✓; invalid outputs cannot enter approval ✓; retries start from a clean approved source ✓. **Missing: "wrong physical or pixel dimensions" is enforced at the Photoshop document read-back, not asserted against a saved TIFF whose dimensions disagree with the preparation.** | P3 |
| SCRUM-11130 | Run Fixed-Workstation E2E for Prepare Design Asset | **PARTIAL** | — | — | — | Full fake-mode workflow tests exist | 11300 gate proves real Meitu enhancement and real cutout individually; 11500-D Job B ran one real enhancement | **The AC's run has not been executed.** No recorded end-to-end JPG → enhancement → background removal → trimming → approved transparent PNG on the fixed workstation through the operator UI, and none of the six required variants (rejected review, automatic retry, manual takeover, restart recovery, unknown dialog, output-validation failure) inside such a run. | P3 |
| SCRUM-11131 | Run Fixed-Workstation E2E for Prepare Customer Design | **NOT_IMPLEMENTED** | — | — | — | Fake-mode workflow tests only | **None** | The acceptance run has never been performed. No document in `docs/printflow` records a `PREPARE_CUSTOMER_DESIGN` execution, live or otherwise. All six required variants (rejected review, retry, takeover, interruption, white-ink validation failure, rejected-TIFF recycle) are unexecuted for this workflow. | P3 |
| SCRUM-11132 | Run Fixed-Workstation E2E for Generate Print TIFF | **PARTIAL** | — | — | — | `PhotoshopTiffWorkflowOutputTests` | **11600-D PASS** — one complete real customer order, PNG input, fresh session, one Photoshop attempt, W1 + TIFF validated, preview rendered, human approval, persistence readback | PNG ✓. **JPG/JPEG not recorded end-to-end. PSD and single-page PDF cannot be run at all (SCRUM-11099/11100), so PSD composite-preview requirements, explicit multi-page-PDF rejection and existing-white-channel handling are unexecuted and unexecutable.** | **P0** (blocked by the product gap) |
| SCRUM-11133 | Verify Multiple TIFF Output Sizes from One Approved Design | **PARTIAL** | — | — | — | `AddAnotherSizeTests`, `MaximumBoundsPlanTests` | 11400 gate §11 covers the approval lifecycle, not a ≥3-size run | The capability is complete and tested (see SCRUM-11105). **The acceptance run of at least three differently sized PrintOutputs from one approved source Revision has not been executed.** | P3 |
| SCRUM-11134 | Validate PrintFlow TIFFs in the Current Maintop Environment | **PARTIAL** | — | — | — | — | 11400 gate §12: one current generated TIFF accepted by Maintop v6.1; 11000 report §6: the reference TIFF loads | One TIFF, not "representative TIFFs"; **no side-by-side comparison of a PrintFlow output against the proven reference TIFF** is recorded. Conclusions correctly limited to this fixed environment ✓; Maintop is not automated ✓. | P3 |
| SCRUM-11135 | Execute Representative Physical DTF Print Validation | **NOT_IMPLEMENTED** | — | — | — | — | **None** | No physical DTF print from a PrintFlow-generated TIFF has been run or photographed. The 11000 report states the boundary explicitly. Nothing about physical size, aspect ratio, colour, white ink, transparency edges, trimming or fine detail has been inspected on a printed article. | P3 |
| SCRUM-11136 | Measure Standard-Test-Set Automation Success Rate | **PARTIAL** | — | — | — | `ProductionMultiJobSoakSmoke` | 11600-B: 40/40 jobs across four blocks with a restart checkpoint; run 2 recorded 2 natural failures in 21 jobs, both retried | **This is not the AC's measurement.** The soak is harness-driven (not the operator UI), each "job" is a **single adapter step** terminating at `ReviewRequired` rather than a complete workflow, and it runs **one synthetic image** — because the standard local regression set (SCRUM-11065) does not exist. The 90 % figure cannot be claimed against a test set that was never built. | P3 |
| SCRUM-11137 | Measure Post-MVP Operator-Time Outcome | **NOT_IMPLEMENTED** | — | — | — | `AttemptTimingTests` (prerequisite infrastructure, valid — do not revert) | None | Not started, and **not startable as specified**: the AC requires comparison against the pre-MVP benchmark, and SCRUM-11066 was waived, so **no baseline exists**. Active operator time, wait time, total time, takeover rate, retry rate and rejection rate have never been measured on either side. | P3 (blocked) |
| SCRUM-11138 | Execute the Final PrintFlow Studio MVP Release Gate | **NOT_IMPLEMENTED** | — | — | — | Per-Epic gates exist (11100, 11200, 11300, 11400, 11500-D, 11600-A/B/C/D) | — | No integrated release-gate report covering all eighteen listed subjects exists, and it cannot honestly issue PASS / PASS WITH NOTES / NOT READY while SCRUM-11131, 11135, 11136 and 11137 are unexecuted and SCRUM-11099/11100 are unimplemented. | P3 (blocked) |

---

## 3. SCRUM-11081 — alpha trimming, answered point by point

The task brief asked seven specific questions. Answers, with evidence.

**A. Detection.** Yes. `AlphaBounds.Compute` (`PrintFlow.Domain/Trimming/AlphaBounds.cs`) scans the
alpha plane row-major and returns the smallest half-open rectangle containing every pixel with
**`alpha > 0`** — no opacity threshold, by explicit design ("a threshold of 10 or 128 would quietly
shave the soft edge off every cut-out in the shop"). A file with no such pixel returns `null`, which
becomes `TrimOutcome.ManualCropRequired`. Alpha extraction, premultiplication and pixel format are
the decoder's problem (`DeterministicAlphaTrimProcessor`, `WicPixelFormats`), so the geometry
function is pure and fully unit-testable.

**B. Safety margin.** Yes, and it is auditable. `TrimMargin` carries the mode (`TightCrop`,
`UniformMargin`, `EdgeSpecificMargin`) together with its four non-negative values; negatives are
refused at construction, because "a margin expands the crop, it never crops further in".
`TrimBounds.Expand` clamps to the canvas, so a margin can never produce an out-of-canvas
coordinate. The operator sets it through `WorkflowCommand.SetTrimParameters`, reachable from the
session screen. It is persisted **twice** by migration `0002`, deliberately split: on
`ProcessingSession` ("what the next trim will use") and on `ProcessingAttempt` ("how *this*
Revision was produced"). **The one weakness:** there is no configured default —
`WorkflowSnapshot.TrimMargin` defaults to `TrimMargin.Tight` (zero on every edge) and the preset
carries no trim-margin value. The AC's "configurable safety margin" is operator-configurable per
session, not workstation-configurable, and SCRUM-11118 (Settings, which the original Jira says
should hold "trim safety margin") does not exist.

**C. Actual crop.** Yes — a real artefact, not a calculation. `DeterministicAlphaTrimProcessor`
writes a cropped PNG to `TrimRequest.ExpectedOutput` inside the attempt's own `Working\` directory.

**D. Product integration.** Yes, fully. `ITrimProcessor` is registered in
`ServiceRegistration.cs:76`; the Trim step is `AdapterKind.Internal` in both workflows that have
it; `SessionService.cs:1834` builds the `TrimRequest` from `state.TrimMargin` — the operator's
persisted decision, not a constant. `ManualCropRequired` is returned as a **failed attempt with a
stable code**, never a fabricated Revision, and the manual-crop surface routes on that code.

**E. Persistence.** **Partial — this is the gap.** The *margin* is persisted; the produced file
becomes a hashed Revision with its own dimensions. **The computed bounds are not.**
`TrimResult.ContentBounds` and `TrimResult.AppliedBounds` are correct and are asserted by
`DeterministicTrimTests`, but `SessionService` discards them at line 1859
(`return await InspectAsync(result.ProducedFile!.Value, cancellationToken);`) — the tuple it
returns carries only file, facts and adapter notes. Searching for `ContentBounds` / `AppliedBounds`
outside `ITrimProcessor.cs` finds **test files only**. There are no bounds columns in any
migration.

**F. Restart.** Yes for everything that is persisted. The margin, the Revision, the step state and
the review decision all survive restart (`TrimParameterTests`, 11200 gate §14). The bounds do not
survive, because they were never stored.

**G. Downstream.** Yes. The trimmed Revision is what `UpstreamRevisionOf(PrintDimensions)` resolves
to, and the sizing plan is calculated from *its* pixel dimensions and bound to *its* hash
(`SessionService.cs:680`). Photoshop then works from a fresh working copy of it.

**Verdict: PARTIAL, on E — "Return original and final bounds" is a stated acceptance criterion and
the product does not meet it.** This is also why SCRUM-11094's "display of detected graphic bounds"
and SCRUM-11126's "bounds metadata remain consistent" cannot be satisfied.

---

## 4. SCRUM-11126 — trimming acceptance matrix, item by item

| Required case | Algorithm test exists | Real product workflow exists | Evidence |
| --- | --- | --- | --- |
| Transparent image | **Yes** | **Yes** | `DeterministicTrimTests:41` (bounds `[3,2 → 8,7)`); `TrimStepTests` |
| No-transparency image | **Yes** | **Yes** | `DeterministicTrimTests:154` (`ContentBounds` null → `ManualCropRequired`); `ManualCropStepTests` |
| Partially transparent edges | **Yes** | **Yes** | The `alpha > 0` rule; `DeterministicTrimTests:111` (a single 1 px content cell at `[15,11 → 16,12)`) |
| Alpha bounds | **Yes** | **Yes** | `AlphaBoundsTests`, `TrimBoundsTests` |
| Safety margin | **Yes** | **Yes** | `DeterministicTrimTests:232` (content `[5,5 → 7,7)` → applied `[3,3 → 9,9)`); `:252` and `:268` prove canvas clamping; `TrimMarginTests`; `TrimParameterTests` persists mode + four values on session and attempt |
| Manual crop | **Yes** | **Yes** | `ManualCropProcessorTests`, `ManualCropStepTests`, `ManualCropUiTests`, `CropSurfaceLayoutTests` |
| Invalid crop bounds | **Yes** | **Yes** | The processor fails rather than clamps when the rectangle exceeds the decoded canvas; `TrimBounds.FromEdges` refuses a zero-area or negative rectangle at construction |
| Crop cancellation | **Yes** | **Yes** | `CancelManualCrop` reaches no service and touches no file — a structural guarantee, not a promise |
| **Metadata persistence** | **N/A** | **NO** | Margin ✓ (migration `0002`). **Content/applied bounds: no column, no field, nothing.** The manual crop rectangle is likewise not stored — only the resulting file. |
| Restart / resume | — | **Yes** | 11200 gate §14: progress into Trim `ReviewRequired` and into `ManualCropRequired` both survive restart |
| Downstream invalidation | **Yes** | **Yes** | `ReturnToStepTests`; `WorkflowEffect.InvalidateDescendants`; 11200 gate §12 |
| No colour-based guessing | **Yes** | **Yes** | 11200 gate §3 source-wide scan found no colour-key trimming; `ITrimProcessor`'s contract forbids it and `TrimBoundaryTests` enforces the boundary |

**One row fails: metadata persistence.** Everything else in the original acceptance matrix is
genuinely covered at both levels.

---

## 5. Discrepancies with previous PASS claims

Recorded because the brief asks for them explicitly. In each case the earlier report was
**honest about its own scope** — the discrepancy is between that scope and the original Jira AC,
not a false claim.

1. **The Epic 11200 gate says `PASS`**; against the original Jira, SCRUM-11079, 11081, 11082 and
   11083 are each PARTIAL. The gate tested what the phase plans specified (Parts B, C1, C2, C3);
   the phase plans did not carry forward "return original and final bounds", "synchronised pan",
   "white and black backgrounds", the slider mode, or trim cancellation. The gate's own §21 lists
   "a runtime language switcher" as still absent, which is accurate.
2. **The Epic 11400 gate says `PASS WITH NOTES`**; SCRUM-11099 and SCRUM-11100 (PSD and single-page
   PDF) were **never in any 11400 phase plan** and so were never tested, passed or failed. They are
   simply missing from the implementation history.
3. **The Epic 11000 report marks 11005/11006/11007 "Confirmed"** by operator waiver. Against the
   original Jira acceptance text they are NOT_IMPLEMENTED, NOT_IMPLEMENTED and PARTIAL. The waiver
   is legitimate as a business decision and is recorded in the report; it does not make the AC met,
   and SCRUM-11066's waiver is the direct cause of SCRUM-11137 being unrunnable.
4. **The 11600-B soak is described in places as a 40-job production soak.** It is — but as evidence
   for SCRUM-11136 it is single-image, single-step and harness-driven, which is a materially weaker
   claim than the AC's "standard local regression set, run repeatedly".
5. **`CleanupWorking`** is the cleanest example of the class this audit was asked to catch: the
   algorithm exists, the port exists, a test exercises it, and an architecture test *guarantees*
   it is never reachable from the product. `IMPLEMENTED_NOT_INTEGRATED`, not FULL.

---

## A. Totals

Counted over the **71 Tasks** (the 8 Epics are rollups and are excluded from the arithmetic).

| Status | Count | Share |
| --- | ---: | ---: |
| `FULL` | **28** | 39.4 % |
| `PARTIAL` | **31** | 43.7 % |
| `IMPLEMENTED_NOT_INTEGRATED` | **1** | 1.4 % |
| `TEST_ONLY_OR_HARNESS_ONLY` | **0** | 0.0 % |
| `NOT_IMPLEMENTED` | **11** | 15.5 % |
| `SUPERSEDED_BY_DESIGN` | **0** | 0.0 % |
| **Total** | **71** | 100 % |

`SUPERSEDED_BY_DESIGN` is zero **by rule, not by absence**. Two tasks — SCRUM-11094 (aspect lock →
single-edge contract) and SCRUM-11095 (effective-DPI bands → enlargement authority) — are genuine,
well-reasoned design supersessions whose replacements are arguably safer than the originals. Both
are classified `PARTIAL` because §4 of the audit brief requires it: neither replacement delivers
the display half of its AC (graphic bounds; effective DPI).

Epic rollups: all eight Epics are `PARTIAL`.

---

## B. Core MVP coverage

Core product = the 48 Tasks under SCRUM-11068, 11077, 11085, 11093 and 11107, plus the six
operator-product Tasks SCRUM-11116–11121. Excludes environment baseline, packaging and QA.

| Epic | Tasks | FULL | PARTIAL | INI | NOT_IMPL |
| --- | ---: | ---: | ---: | ---: | ---: |
| SCRUM-11068 Core foundation | 8 | 6 | 2 | 0 | 0 |
| SCRUM-11077 Review & trimming | 7 | 2 | 5 | 0 | 0 |
| SCRUM-11085 Meitu | 7 | 5 | 2 | 0 | 0 |
| SCRUM-11093 Photoshop | 13 | 6 | 5 | 0 | 2 |
| SCRUM-11107 Safety & recovery | 7 | 4 | 2 | 1 | 0 |
| SCRUM-11116–11121 Operator UX | 6 | 0 | 5 | 0 | 1 |
| **Core total** | **48** | **23** | **21** | **1** | **3** |

**Core MVP FULL coverage: 23 / 48 = 47.9 %.**

The foundation, the Meitu adapter, the workflow engine and the safety gates are the strongest
areas. The two weakest are **Operator UX (0 / 6 FULL)** and **Image Review & Trimming (2 / 7
FULL)** — and the single largest hole is in Photoshop, where two of the four input formats the MVP
declares do not exist.

**Route map for the three MVP workflows, with broken links marked.**

```text
Prepare Design Asset
  Home → drop → [✗ no editable output name] → Workflow Selection → SelectWorkflow
    → Session: Import ✓ → Original Confirmation ✓
    → Enhancement (Meitu ✓ live) → ReviewRequired ✓ → Approve/Reject ✓ → persisted ✓
    → Background Removal (Meitu ✓ live) → ReviewRequired ✓ → persisted ✓
    → Trim (alpha ✓ / manual crop ✓) → ReviewRequired ✓
         [✗ bounds not returned or persisted] [✗ no trim cancellation]
    → Approved PNG Export ✓ → Complete ✓  [✗ CleanupWorking never runs]
    STATUS: complete, but never run end-to-end live (SCRUM-11130)

Prepare Customer Design
  … as above through Trim …
    → Print Dimensions ✓ (max-bounds or target edge; [✗ no graphic bounds, no effective DPI])
    → White underbase branch ✓ → Photoshop Output ✓ → TIFF validation ✓
    → ReviewRequired ✓  [✗ no CMYK / W1 / overlay previews]
    → Approve ✓ (hash-bound) | Reject → Recycle Bin ✓
    STATUS: complete, but never run end-to-end live (SCRUM-11131)

Generate Print TIFF
  Home → drop
    ├─ PNG  ✓ ────────────────────────────────► proven live end-to-end (11600-D PASS)
    ├─ JPEG ✓ (code path shared with PNG) ────► never run live
    ├─ PSD  ✗ IMPORTS, THEN DEAD-ENDS at Print Dimensions with PreconditionNotMet
    └─ PDF  ✗ not offered; no page-count read; no multi-page rejection
    → Original Confirmation (RequiresReview ✓) → Print Dimensions ✓
    → Photoshop Output ✓ → TIFF validation ✓ → final review ✓ → Complete ✓
    → Add Another Size ✓
```

---

## C. QA / acceptance coverage

The 14 Tasks under SCRUM-11124.

| Status | Count |
| --- | ---: |
| `FULL` | 2 |
| `PARTIAL` | 8 |
| `NOT_IMPLEMENTED` | 4 |

**Implemented capability vs. executed acceptance — the distinction the brief asked for:**

| Capability that exists | Original acceptance run that has *not* happened |
| --- | --- |
| Photoshop drives the signed Action, validates and reviews a TIFF | A representative **PSD** E2E (SCRUM-11132) — impossible today |
| The TIFF inspector rejects 11 structural fault classes | A **physical DTF print** (SCRUM-11135) — never attempted |
| A 40-job Photoshop/Meitu soak passes with a restart checkpoint | The **standard-test-set** success-rate measurement (SCRUM-11136) — the test set does not exist |
| `ProcessingAttempt` terminal timing is now correct | The **operator-time outcome** (SCRUM-11137) — and the pre-MVP baseline it must be compared against was never captured |
| One real customer order completed and approved (11600-D) | **Prepare Customer Design** E2E (SCRUM-11131) — no record of any run |
| `AddAnotherSize` is implemented and unit-tested | **≥3 independently reviewed sizes** from one approved design (SCRUM-11133) |
| One generated TIFF accepted by Maintop v6.1 | Comparison of representative outputs **against the proven reference TIFF** (SCRUM-11134) |

---

## D. Operations / packaging coverage

| Jira | Status |
| --- | --- |
| SCRUM-11122 Diagnostic package export | `NOT_IMPLEMENTED` |
| SCRUM-11123 Versioned offline installer + upgrade procedure | `NOT_IMPLEMENTED` |

**0 / 2.** There is no way to install PrintFlow Studio on a workstation other than building it from
source, and no way to hand a support case anything but manually gathered files. Neither blocks the
core workflows, which is why both sit at P4.

---

## E. Priority gap list

### P0 — incomplete core workflow

| # | Slice | Closes | Why P0 |
| --- | --- | --- | --- |
| P0-1 | **PSD input preparation** — composite-preview detection, colour-mode / alpha / spot-channel inspection, Photoshop-driven flatten to a managed working copy, clear early failure for unsupported PSDs | SCRUM-11099; unblocks part of SCRUM-11132 | An entire declared input class is missing, and the current behaviour is worse than refusal: the file picker advertises `*.psd`, the import succeeds, and the session dies several steps later at Print Dimensions |
| P0-2 | **Single-page PDF input preparation** — page-count read, **explicit multi-page rejection**, rasterisation at production DPI into the session workspace, original preserved | SCRUM-11100; unblocks the rest of SCRUM-11132 | Second missing declared input class; the "never silently select the first page" safety rule has nothing to run against |
| P0-3 | **Trim bounds contract** — carry `ContentBounds` / `AppliedBounds` out of `SessionService`, persist them on `ProcessingAttempt`, expose them on `SessionView` | SCRUM-11081; unblocks SCRUM-11094 and SCRUM-11126 | A stated AC of a core algorithm that is computed and then thrown away; small, cheap, and three other Tasks depend on it |

### P1 — missing safety / review behaviour

| # | Slice | Closes |
| --- | --- | --- |
| P1-1 | Import of an operator-produced result after Stop / Take Over (a `SubmitManualResult` command with validation, hashing and its own review) | SCRUM-11092, SCRUM-11112 |
| P1-2 | Existing-W1 operator question (retain vs. regenerate) instead of a hard refusal | SCRUM-11101 |
| P1-3 | Review surface completion — synchronised pan across panes, white and black inspection backgrounds, and trim cancellation | SCRUM-11079, SCRUM-11083 |
| P1-4 | TIFF final review — CMYK preview, W1 channel preview, colour+white overlay, effective DPI and output path in the metadata block | SCRUM-11104 |
| P1-5 | Manual-crop margin controls (tight / uniform / per-edge) and persistence of the chosen rectangle | SCRUM-11082 |
| P1-6 | Workspace retention contract, then wire `CleanupWorking` behind it (define "safe to delete" against attempt status and Revision reachability) | SCRUM-11114, part of SCRUM-11127 |
| P1-7 | Environment-check completion — launchability, Photoshop colour-settings confirmation, test-image open/close | SCRUM-11110 |

### P2 — missing operator UX

| # | Slice | Closes |
| --- | --- | --- |
| P2-1 | Settings screen (output root, language, trim safety-margin default, fixed DPI display, preset details, colour-settings confirmation, log retention), backed by the existing unused `Setting` table | SCRUM-11118, half of SCRUM-11095's display gap |
| P2-2 | Runtime language switcher with an enforced zh-CN first-run default | SCRUM-11119 |
| P2-3 | Editable Output Name — give `SetOutputName` a screen | SCRUM-11075, SCRUM-11078 |
| P2-4 | Recent Processing thumbnail + delete-record action | SCRUM-11117 |
| P2-5 | Error Details page (screenshot, input/expected-output path, retry count) | SCRUM-11120 |
| P2-6 | Real logging + retention enforcement (`AutomationLogEntry` writer; make `Logging:RetentionDays` do something; show locations to the operator) | SCRUM-11121, SCRUM-11076, SCRUM-11091 |

### P3 — acceptance evidence

| # | Slice | Closes |
| --- | --- | --- |
| P3-1 | Build the seven-category standard local regression set | SCRUM-11065 — **hard prerequisite for SCRUM-11136** |
| P3-2 | Capture the ≥20-case manual processing benchmark | SCRUM-11066 — **hard prerequisite for SCRUM-11137** |
| P3-3 | Run the three fixed-workstation E2E acceptances with their variant matrices | SCRUM-11130, 11131, 11132 |
| P3-4 | Run ≥3 sizes from one approved design; compare representative TIFFs against the reference in Maintop | SCRUM-11133, 11134 |
| P3-5 | Physical DTF print validation | SCRUM-11135 |
| P3-6 | Automation success rate on the real regression set | SCRUM-11136 |
| P3-7 | Operator-time measurement against the P3-2 baseline | SCRUM-11137 |
| P3-8 | Integrated final release gate | SCRUM-11138 |

### P4 — packaging / operations

| # | Slice | Closes |
| --- | --- | --- |
| P4-1 | Explicit diagnostic package export with a manifest preview and confirmation | SCRUM-11122 |
| P4-2 | Versioned offline installer, install/configure/rollback documentation, revalidation rule | SCRUM-11123 |

---

## F. On SCRUM-11137

The suspension in the task brief is correct, and there is a second, stronger reason for it than the
P0 gaps: **SCRUM-11137 cannot be completed as written today regardless of feature completeness.**
Its AC requires comparison against the pre-MVP benchmark from SCRUM-11066, and that benchmark was
waived at the Epic 11000 acceptance and never captured. Measuring post-MVP operator time now would
produce a number with nothing to compare it to.

The `ProcessingAttempt` terminal-timing correction (`phase-11137-prerequisite-attempt-timing.md`,
commit `9be97b5`) remains valid prerequisite infrastructure and has **not** been touched by this
audit.

---

## G. Audit method and limits

* Requirement authority: the original CSV export, read in full; every classification above is
  against the Jira acceptance text, not against any phase plan or gate report.
* Implementation authority: source inspection of the product tree, the four XAML views, the eight
  SQLite migrations, `ServiceRegistration`, `NavigationService` and `MainWindow.xaml`; the test
  file inventory; the preset and evidence trees on `D:\PrintFlowStudio`; and the 61 phase and gate
  documents in `docs/printflow`.
* **The full test suite was not re-run** (per instruction §15). Test evidence is cited by file and,
  where a specific assertion mattered, by line. The last recorded full-suite result is 10,158
  passing / 0 failed / 0 skipped, at the SCRUM-11137 prerequisite attempt-timing gate.
* **No product source, test, configuration, preset or evidence file was modified.** The working
  tree was clean at `9be97b5` before this audit and the only file added is this report.
* Live behaviour was assessed from recorded workstation evidence (11300, 11400, 11500-D, 11600-A
  through D) rather than by launching the application, since the audit forbids product changes and
  several live paths require Meitu and Photoshop in their signed starting states.

---

ORIGINAL JIRA FUNCTIONAL COVERAGE RE-AUDITED

* **Core Product blockers: 3** — SCRUM-11099 (PSD input preparation), SCRUM-11100 (single-page PDF
  input preparation), SCRUM-11081 (trim bounds never returned or persisted). A further **10** core
  gaps sit at P1/P2 behind them.
* **Acceptance-only gaps: 12** — SCRUM-11065, 11066, 11067, 11126, 11127, 11129, 11130, 11131,
  11133, 11134, 11135, 11136 — plus SCRUM-11137 and SCRUM-11138, both currently blocked.
* **Operations / packaging gaps: 2** — SCRUM-11122, SCRUM-11123.

**Recommended next implementation Task: SCRUM-11099 — Implement PSD Input Preparation**, taking
the small SCRUM-11081 bounds-contract slice (P0-3) with it or immediately before it, since that
slice is a few hours' work and unblocks SCRUM-11094 and SCRUM-11126. SCRUM-11100 follows directly.

**The MVP is not feature-complete.**

---

## H. Coverage delta — SCRUM-11101 superseded by business design (7 September 2026)

This section is a **delta**, not a revision. Nothing above it has been edited: sections 0–G and the
summary record what was true on 4 September 2026 at `9be97b5`, and the SCRUM-11101 row in section 2
is preserved exactly as that audit wrote it. What follows records one classification change and the
business decision behind it.

| Jira | Previous audit | Current | Nature of the change |
| --- | --- | --- | --- |
| SCRUM-11101 | **PARTIAL** | **SUPERSEDED_BY_DESIGN** | Requirements change, not implementation |

### Why

The original acceptance criterion (CSV Work Item ID `11408`, *Handle Existing White-Ink Spot
Channels Explicitly*) reads:

> When PSD or PDF preparation detects an existing white-ink spot channel, stop and ask the operator
> whether to retain the existing white ink or regenerate white ink using the validated production
> preset. Do not make a silent choice, and keep channel-name, polarity, density, choke and
> colour/white order as production-preset concerns rather than generic workflow assumptions.

That criterion assumes input PSD/PDF white-ink channels could be production authority, requiring a
Retain-versus-Regenerate operator decision. The business production contract now explicitly defines
PSD and PDF as **visual-only** inputs:

```text
PSD and PDF are visual design inputs only.
Existing spot-colour / white-ink channels in customer source files are NOT production authority.
PrintFlow does NOT retain source W1.
PrintFlow does NOT ask Retain vs Regenerate.
Production W1 is always generated by PrintFlow from the final approved visual artwork
immediately before the production TIFF is created for Maintop.
```

Source white-ink and spot channels are therefore not retained as production channels, and
production W1 is always generated by PrintFlow immediately before final TIFF generation for
Maintop.

### Classification

**SUPERSEDED_BY_DESIGN**, deliberately **not FULL**. The original Retain/Regenerate acceptance
criterion was not implemented; it was superseded. Recording it as FULL would claim work that was
intentionally never done.

The **P1 blocker is removed by approved business-design supersession** — not by delivery. The P1-2
row in section E ("Existing-W1 operator question (retain vs. regenerate) instead of a hard refusal")
is cancelled on the same authority.

### What changed in the Product

The audit's SCRUM-11101 row noted that a hard refusal stood in place of the operator question. That
refusal is now gone as well, because it rested on the same superseded assumption — that a source
ink channel is a production fact the workflow must resolve. A supported PSD is no longer refused
solely because it carries a spot or W1 channel; it prepares as ordinary visual artwork. Every other
PSD refusal named in SCRUM-11099 is untouched.

Full detail, including the preserved original Jira wording, the feasibility findings that preceded
the clarification, and the live evidence: `docs/printflow/scrum-11101-existing-white-ink-decision.md`.

### Unchanged by this delta

| Jira | Status | Note |
| --- | --- | --- |
| SCRUM-11104 | **PARTIAL** | Untouched. Still owns the CMYK preview, the W1 preview, the colour-plus-white overlay and the final-TIFF review metadata gaps. Not closed by this clarification. |
| SCRUM-11132 | **PARTIAL** | The PSD and PDF input prerequisites are now **FULL** (SCRUM-11099 and SCRUM-11100 shipped after this audit), and the existing-white-input decision is **not required** by the current production contract. Still pending: the actual fixed-workstation multi-format E2E acceptance. Not FULL. |

---

## I. Coverage delta — SCRUM-11079 shared review completion (7 September 2026)

**SCRUM-11079: PARTIAL → FULL.** This supersedes the shared-review gap assessment in the historical row above; it does not rewrite the original audit or close the parent Epic.

One `SharedReviewSurface` now provides bidirectional normalized pan, shared checkerboard/white/black inspection backgrounds, and side-by-side or slider comparison across the existing general review workflows. It preserves shared zoom, Fit, 100%, and high magnification. Different-sized images keep their natural aspect ratios and a documented shared top-left origin in Slider mode. Display changes reuse preview bitmaps and do not change revision bytes, hashes, review authority, or workflow state.

Evidence: **149 targeted tests passed**; **10,903 full-suite tests passed, 0 failed, 0 skipped**; clean build **0 warnings / 0 errors**; real synthetic WPF window/UIA smoke passed, including normalized scrolling, background/mode changes, RangeValue slider operation, WPF keyboard traversal/routed keys, normal approval, and independent persisted revision/hash binding verification. The known Photoshop settle-poll flake did not occur.

Full original Jira wording, coordinate policy, zero-extent/resize/reset behavior, ephemeral preference lifetime, keyboard-evidence limits, tests, live transcript, and Git discipline: [SCRUM-11079 completion report](scrum-11079-shared-review-completion.md).

This closes only the shared-review portion of historical P1-3. **SCRUM-11077 remains PARTIAL. SCRUM-11082 and SCRUM-11083 are unchanged**; trim cancellation and manual-crop gaps remain outside this slice.

---

## J. Coverage delta — SCRUM-11083 explicit trim cancellation (7 September 2026)

**SCRUM-11083: previous audit PARTIAL → current FULL.** This supersedes the cancellation gap in the historical row without rewriting that row. The exact original CSV requirement is Work Item 11206, **Implement Independent Trim Review and Adjustment**.

The operator can now choose **Keep original extent** before processing, after a Trim result reaches ReviewRequired, or after ManualCropRequired. The typed command persists the existing Skipped state and explicit reason, clears the current Trim offer, and advances using the exact approved pre-Trim Revision. It fabricates no processing attempt, Revision, geometry, file or review approval. Real earlier Trim results and decisions remain history. Restart preserves the choice; ReturnToStep clears it for reevaluation and retains normal downstream invalidation. Print Dimensions and approved PNG export use the original upstream canvas. The action is bilingual, keyboard accessible and addressable as `Session.KeepOriginalExtent` through UIA.

Evidence: **9,215 intermediate broad targeted tests passed**; **162 final focused tests passed**; final-source full suite **11,353 passed, 0 failed, 0 skipped**; clean build **0 warnings / 0 errors**. Three real synthetic WPF cases passed independently: automatic Trim/Before-After/Keep via UIA, no-alpha/ManualCropRequired/Keep via UIA, and Waiting/Keep via keyboard Enter. Independent repository and file readback confirms original Revision authority, immutable historical offers, no fabricated output or approval, full canvas dimensions and unchanged automation lock. The known unrelated PSD settle-poll flake did not occur.

Exact original AC, representation rationale, restart/return behavior, test coverage, live evidence and its scope, rejected harness attempts, build and Git discipline: [SCRUM-11083 trim cancellation report](scrum-11083-trim-cancellation.md).

**SCRUM-11077 remains PARTIAL. SCRUM-11082 remains PARTIAL.** The Epic is reassessed independently: closing independent Trim cancellation does not deliver manual-crop margin controls or persisted manual-crop rectangle metadata. Shared review remains accepted under SCRUM-11079 and its implementation is unchanged by this slice.

## K. Coverage delta — SCRUM-11082 manual crop completion (7 September 2026)

**SCRUM-11082: historical PARTIAL → current FULL.** The exact original CSV row is source Work Item 11205, **Implement Manual Crop Fallback for Non-Transparent Inputs**, read before Product edits. Manual crop now supports Tight, Uniform and independent per-edge expansion around the operator's strictly validated selection. The editor displays selected and actual clamped applied bounds immediately. No colour/alpha-boundary inference is introduced. Apply produces the real managed `ManualImport` Revision and independent shared review; Cancel persists nothing; Keep original extent remains a distinct available action.

Migration 0013 records selected/applied half-open bounds and explicit manual margin mode/values on the producing attempt, independently of automatic trim settings. Old rows remain null without inferred origins. SQLite prevents rewriting recorded history and ambiguous automatic/manual metadata. Rejected/retried crops retain independent geometry. Restart/reopened review retains Revision/hash/geometry/state. Actual raster dimensions and pixels agree with applied bounds; approval selects that exact Revision for customer dimensions and asset export. Existing shared-review mechanics and SCRUM-11083 behavior remain intact.

**SCRUM-11126: independently reassessed PARTIAL → FULL.** Its exact original source Work Item 11702, **Test Trimming and Manual Crop Deterministically**, was re-read. Existing real-interface alpha bounds, safety margins, transparent/no-transparency inputs, cancellation and no-colour-guessing coverage remains; new manual adjustment, invalid bounds, actual raster/Revision consistency, persistence and restart tests close its remaining metadata criterion. SCRUM-11081 already closed the automatic bounds portion.

**SCRUM-11077 remains PARTIAL.** SCRUM-11078's separate import/workflow-selection UX gaps are unchanged; neither child completion nor the testing reassessment automatically completes the Epic.

Evidence: clean build **0 warnings / 0 errors**; final targeted regression **558 passed, 0 failed, 0 skipped**; final live Uniform, Per-edge and Cancel/Keep cases **3/3 passed**, including raw SQLite readback, real raster/hash checks, reopened real WPF review, keyboard/UIA controls and unchanged source/lock. One complete suite against final source: **11,382 passed, 1 failed, 0 skipped** (11,383 total). The sole failure was the known unrelated PSD malformed-input settle-poll case; its exact isolated rerun **passed 1/1**. Both results are preserved; PSD code is untouched. No second full-suite run replaced the failure.

Full requirements, semantics, tests, live boundaries, evidence, known failure and Git discipline: [SCRUM-11082 manual crop completion report](scrum-11082-manual-crop-completion.md).

**PASS WITH NOTES — SCRUM-11082 MANUAL CROP FALLBACK VERIFIED**

---

## L. Coverage delta — SCRUM-11104 TIFF final review mode (8 September 2026)

**SCRUM-11104: previous audit PARTIAL → current FULL.** This supersedes the preview and metadata
gaps in the historical row without rewriting that row. The exact original CSV requirement is source
Work Item **11411**, *Build TIFF Final Review Mode*, re-read before any Product edit.

All six audited gaps are closed. The final TIFF review now offers three distinct modes — **Colour**,
**White ink** and **Colour + white overlay** — over one canvas, decoded once from the validated
file's own samples rather than from a generic WIC flattening that spends the spot channel on alpha.
The Colour mode is an uncalibrated device conversion of the separated CMYK and says so on screen in
both languages; the embedded ICC profile is deliberately not applied and no printed-colour claim is
made. The White-ink mode is the validated W1 fifth sample under one closed, documented, tested
polarity (stored 255 = no ink, 0 = 100% ink, displayed inverted so bright means ink), verified
region by region against deliberately banded fixtures and against the inspector's own non-empty
count. The overlay marks W1 coverage over the colour with a restrained fixed-strength wash and an
unambiguous legend.

The production metadata block now states output filename, output path, pixel dimensions, physical
dimensions, TIFF resolution, **effective source resolution**, enlargement authority where one was
required, colour mode and bit depth, W1 status with its ink-sample count, the signed preset
identifier with its manifest hash, and an abbreviated SHA-256. Rows a payload cannot answer are
absent rather than guessed; the preset **version** is omitted because the `PrintOutput` row does not
persist it. Effective resolution is bound to the exact Revision the producing attempt's preparation
was calculated from — proven across automatic-trim, keep-original-extent and manual-crop upstream
routes — and introduces **no thresholds, bands or colours**.

Every preview mode is bound to the reviewed `PrintOutput` hash: the decoder hashes the file before
reading a byte for display and refuses a mismatch, so a TIFF changed after validation yields no
preview, no metadata and no path, and the existing approval refusal is unaffected. Changing mode
alters nothing an approval binds to. Rejection and Recycle Bin disposal, multiple independently
reviewed sizes, and restart reconstruction without a Photoshop rerun are all unchanged and
regression-covered. `SharedReviewSurface` (SCRUM-11079) is untouched; the specialist surface sits
beside it and reuses the same zoom and viewport state.

Evidence: clean build **0 warnings / 0 errors**; final-source full suite **11,453 passed, 0 failed,
0 skipped** (baseline 11,400 + 53 new tests); the live workstation proof passed against the **real
Photoshop-produced Epic 11000 baseline TIFF** with W1 ink samples, effective DPI, output path and
hash each verified independently of the view model. **Note:** Photoshop is not installed on this
workstation, so under §60 the live proof reuses that already generated validated TIFF rather than
producing a new one; no new Photoshop run was made and none is claimed.

**SCRUM-11095: remains PARTIAL — one factual half of its gap retired.** Its exact original source
Work Item **11402**, *Implement Effective-DPI Resolution Risk Rules*, was independently re-read. The
historical row's statement that "effective DPI is never computed or displayed anywhere in the
product (no such concept exists in the source)" is no longer true: the concept exists and
millimetres, pixel dimensions and effective DPI are displayed together at final review. Still open
and unchanged: effective DPI is shown only **after** the TIFF exists rather than at the Print
Dimensions preflight decision; **graphic bounds are still not displayed anywhere**; and the
sufficient / warning / blocking bands remain superseded by explicit enlargement authority, because
no print-test thresholds were ever captured (SCRUM-11065/11066 waived) and invented numbers are
still refused. This delta is recorded exactly and claims nothing further.

**SCRUM-11132, SCRUM-11134, SCRUM-11135 and SCRUM-11105 are unchanged, and the parent Epic
SCRUM-11093 is not closed by this slice** — its PSD and PDF input gaps (SCRUM-11099, SCRUM-11100)
are untouched.

Exact original AC, polarity evidence from the real production TIFF, colour-management limits,
effective-DPI definition and matrix, metadata block, hash binding, restart and integrity behaviour,
keyboard/UIA/localisation coverage, tests, live proof and its stated scope, build, suite and Git
discipline: [SCRUM-11104 TIFF final review completion report](scrum-11104-tiff-final-review-completion.md).

**PASS WITH NOTES — SCRUM-11104 TIFF FINAL REVIEW VERIFIED**

---

## M. Coverage delta — SCRUM-11114 safe completion and retention cleanup (8 September 2026)

**SCRUM-11114: IMPLEMENTED_NOT_INTEGRATED → FULL.** The exact original CSV row is Work Item **11507**, parent **11500**. Completion now interprets CleanupWorking after the valid completion transaction. A pure retention plan preserves source snapshots, approved PNG/TIFF deliverables and failed-attempt evidence, promotes authoritative Working Revision history by verified copy before one metadata transaction, and deletes only positively classified redundant files. Rejected Meitu comparison bytes expire through explicit persisted retention state after session end. Unknown files remain preserved; final TIFF disposal retains its existing Recycle Bin boundary.

Crash recovery derives pending cleanup from completed metadata and file authority, and startup invokes the same retention service. All three workflows, multiple approved TIFF sizes, retry/ReturnToStep history, manual imports/crops, PSD/PDF prepared rasters, active-session refusal, path containment, junctions, hard links, hash corruption, transaction rollback and interruption boundaries are covered. No authoritative file reference silently loses its bytes.

**SCRUM-11127: PARTIAL → FULL, independently reassessed.** Original CSV Work Item **11703**, parent **11700**, was re-read after implementation. Every clause is supported: temporary SQLite/filesystem integration; transactions; InputSnapshots; Revision registration; hashes; collision-safe output naming; restart persistence; production cleanup; Recycle Bin behaviour; fixture-source preservation; and refusal to retain invalidated/rejected outputs as current production results. The completion report contains the clause-by-clause evidence matrix.

Final build: **0 warnings / 0 errors**. Targeted gate: **1,057 passed, 0 failed, 0 skipped**. Final complete suite: **11,494 passed, 0 failed, 0 skipped** (11,453 accepted baseline + 41). The first complete run exposed one existing English-text assertion inheriting zh-CN culture; a scoped test-only culture fix passed in isolation, then the clean rebuild and complete rerun passed. Product TIFF review behaviour is unchanged.

The bounded synthetic live proof used real SQLite, FileWorkspace, SessionService and completion interpretation, then rebuilt services for restart verification. Independent read-only Python/SQLite/disk checks passed: six authoritative file-bearing rows resolved with matching hashes, four Revisions were promoted, seven classified files removed, source/snapshot/output/evidence survived and the automation lock remained free. No customer files or external application execution were used.

**PASS WITH NOTES:** redundant TIFF Working copies, unknown files, partial staging evidence and empty directories remain intentionally preserved. This is safe completion retention, not a claim of complete disk reclamation. **SCRUM-11107 remains open**, including its other SCRUM-11110/11112 gaps. The three-size test makes no SCRUM-11133 acceptance claim. Historical audit rows above are unchanged.

Evidence: [SCRUM-11114 safe completion and retention cleanup report](scrum-11114-safe-completion-retention-cleanup.md), including exact AC, authority inventory, crash ordering, live proof, verification results and Git discipline.

---

## N. Coverage delta — SCRUM-11110 operating environment check completion (8 September 2026)

**SCRUM-11110: historical PARTIAL → current FULL.** The exact original CSV row is Work Item
**11503**, parent **11500**, *Build the Operating Environment Check Page*. The existing twelve
automatic checks remain intact. Seven typed live/smoke checks now cover the shared automation lock,
Meitu launchability and recognised state, Photoshop launchability and recognised state,
unsaved/unknown refusal, actual active RGB/CMYK/Gray/Spot settings, and a contained exact-identity
synthetic image open/close without save. `Blocked` distinguishes checks not run after a failed
prerequisite.

Passive page entry and Refresh still launch nothing. The explicit asynchronous live command uses
the existing accepted foundations, retains the single `ProductionWorkstationVerifier` authority,
never executes an unverified binary, never repairs settings or closes operator work, and leaves
applications running. Its ephemeral certificate is tied to the same process identities and is
re-observed by the Production gate; the existing per-attempt state guards remain and now also
re-check Photoshop colour spaces and unsaved documents. Lock ownership is tokenised in migration
0015 so live verification and production work cannot manipulate the applications concurrently or
release one another's lock.

The controlled fixed-workstation proof passed all ten blocking automatic checks and all seven live
checks: Meitu launched to `KnownWelcome`; Photoshop attached to an existing accepted process at
`KnownStartScreen` with no documents; its four working spaces exactly matched preset v1.16.0; the
1×1 PrintFlow-owned image was opened, positively identified, closed without saving and cleaned;
the lock was free afterwards. No customer file was used or modified. The two existing automatic
advisories remained truthful and non-blocking.

Evidence: clean build **0 warnings / 0 errors**; final targeted gate **224 passed, 0 failed, 0
skipped**; final-source complete suite **11,513 passed, 0 failed, 0 skipped**. The preceding complete
run's sole failure was a legacy synthetic W1 preset fixture missing the newly mandatory colour
spaces; Product remained fail-closed, the fixture was corrected, and its isolated **3/3** plus the
complete rerun passed. Full exact AC, classification, vocabulary, authority model, colour reader,
probe/lock lifecycle, matrix, expected/current live facts and Git discipline:
[SCRUM-11110 operating environment completion report](scrum-11110-operating-environment-check-completion.md).

**Parent Epic SCRUM-11107 remains PARTIAL.** SCRUM-11112 recovery gaps remain outside this slice;
SCRUM-11118 Settings/preset display is unchanged. Closing SCRUM-11110 does not close the parent.

**PASS — SCRUM-11110 OPERATING ENVIRONMENT CHECK VERIFIED**
