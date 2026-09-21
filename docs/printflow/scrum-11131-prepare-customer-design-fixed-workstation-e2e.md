# SCRUM-11131 — Prepare Customer Design fixed-workstation E2E

Date: 21 September 2026

Work item: SCRUM-11131 / CSV Work Item 11707

Verdict: **PARTIAL — the ordinary customer-design golden path passed on the fixed workstation, with
no trim operation executed; the six variant clauses are supported only at candidate-pinned harness
or isolated-validation level, and four of them were explicitly deferred by the Operator rather than
observed live**

Review status: **INDEPENDENT READ-ONLY RECORD AND SOURCE REVIEW COMPLETED; INDEPENDENT RE-EXECUTION
NOT PERFORMED** (§9).

Raw local evidence (git-ignored, not committed):
`artifacts/scrum-11131/11131-customer-20260921-e3c6/`.

---

## 1. Acceptance authority

The complete acceptance criterion, read from the retained Jira export
`C:\Users\admin\Downloads\printflow_studio_mvp_jira_epics_tasks.csv` row **11707**, is:

> Execute customer design through optional Meitu processing, trimming, physical dimensions,
> Photoshop production Action, TIFF validation and final approval on the fixed workstation. Include
> rejected review, retry, manual takeover, interruption, white-ink validation failure and
> rejected-TIFF Recycle Bin behaviour.

The row was read directly; no Jira connection was attempted and no online transition is claimed.
The established SCRUM → CSV mapping was reused (SCRUM-11130 → 11706, so SCRUM-11131 → 11707) and the
row's title, `Run Fixed-Workstation E2E for Prepare Customer Design`, confirms it.

The criterion has two halves and they are reported separately: one continuous operator-facing run of
`PREPARE_CUSTOMER_DESIGN` through to a final approval, and six variant clauses.

## 2. Execution record

| Fact | Value |
|---|---|
| Executor | Claude Code, VS Code extension host (detected from the runtime, not from a document title) |
| Native policy | `C:\Users\admin\.claude\workflows\development-routing.md` adaptation **v1.1** (2026-09-18), loaded through the managed `PERSONAL_DEV_ROUTING` entry in `~/.claude/CLAUDE.md` |
| ExecutionTarget | Opus / High |
| ActualModel | Opus 5 (`claude-opus-5`), reported by the host |
| ActualEffort | **UNVERIFIED** — per-request effort metadata is not exposed by this host. User settings carry `effortLevel: high`, which is the configured intent, not an observation of this request |
| ActualRoute | Opus High *requested and configured*; the effort component is `UNVERIFIED`, so this is **not** a fully verified `ActualRoute = Opus High` |
| RouteOffset | 0 — none supplied, none inherited |
| Context | CONTINUE |
| Sonnet delegation | None. No model switch was attempted, so `MODEL_SWITCH_UNAVAILABLE` does not apply |
| HEAD at start | `cb552eefad00682c2e2f868206d248715dfeb6d3`, working tree clean |
| HEAD at end | unchanged except for this scoped documentation commit |
| Repository / workspace | `D:\Repositories\printflow-Studio` on `master`; `D:\PrintFlowStudio`. No branch, worktree, clone, reset, clean, amend or rebase |
| Input channel | Native UI Automation (`InvokePattern`, `ValuePattern`, `ExpandCollapsePattern`, `SelectionItemPattern`) on visible, enabled, on-screen controls resolved by exact `AutomationId`. No SendKeys, no synthetic mouse, no harness, bootstrap, Fake mode, direct service call, database write or injected readiness |

## 3. Retained authority, re-verified read-only

Every identity named in the authorising prompt was re-read from actual bytes before the run and
re-read again at handback. All matched.

| Item | Value | Re-verified |
|---|---|---|
| Build pair | `d915b1a6-4c06-4b16-9a20-5e1341d1ae9c` | `New-PrintFlowBuildPair.ps1 -VerifyOnly` → "Build pair verified: d915b1a6-4c06-4b16-9a20-5e1341d1ae9c" |
| Receipt | `artifacts/pf-accept-a2/build-pairs/d915b1a6-…/build-pair.json` | `52E6DBC5…8FBC` |
| Qualification result | `a2-v3-20260917-154736-d915b1a6/result.json` | `4083F1FB…5FDA1A`, `Status = Passed`, 7/7 categories |
| Active record | `D:\PrintFlowStudio\Revalidation\production-revalidation.json` | `E6A7D7EA…F3B9` |
| Preset | `printflow-workstation-v1` 1.18.0 | `8484F0AA…C8E0F` |
| Candidate assemblies | `PrintFlow.App/Domain/Infrastructure/Workflow.dll` | `20E228EE…`, `7F4808F8…`, `4E77AD36…`, `1AF48C6C…` — identical to the active record's four rows |
| Candidate executable | `…\candidate\PrintFlow.App.exe` | `06FB7C7A…6BCA`, 162,304 bytes |
| Harness assembly | `…\harness\PrintFlow.Tests.dll` | `9AAC5BEB…FAB9` |

The candidate was resolved from the qualification result's own `Binding.BuildOrigin`
(`ReceiptPath` + `ReceiptSha256` + `PairId`), not from an ordinary `bin/Release`. No new pair was
created and nothing was built.

### Observation sidecar

`artifacts/scrum-11131/11131-customer-20260921-e3c6/observation-sidecar.json` links this execution
id and time to the receipt, the candidate executable and assembly hashes, the actual running process
(PID 1320, its path, start time and command line), the active record, the preset, the fixture
measurement, the lease-store hash and the resulting `SessionId`. It is **executor-measured
provenance**, explicitly not a Product-emitted or independently measured attestation. No signing,
PKI or Product instrumentation was added.

Two properties of that file, so it is not over-read. Its `createdAtLocal` is 13:00:34, the moment it
was first written, but it was **amended twice afterwards** — once with the running process and gate
result, once with the `SessionId` and the delivered TIFF. It is an executor-maintained file, not a
pre-committed attestation, and it cannot corroborate pre-run state independently of the executor.

## 4. Fixture, optional branches and the physical size plan

**Fixture.** `FIX-CUSTOMER-DESIGN-001`, the v3 set's `COMPLETE_CUSTOMER_DESIGN` asset.
`D:\PrintFlowStudio\TestData\v3\inputs\FIX-CUSTOMER-DESIGN-001.jpeg`, SHA-256
`8A7063D8F81FB72A1DB7F5633980660905E2A6C3D3971E4F94A138B5C8394879`, 216,162 bytes, 1024 × 1536
JPEG, `Format24bppRgb`, no alpha. Its manifest marks it `APPROVED_LOCAL_ONLY`, `gitAllowed: false`,
`uploadAllowed: false`, `containsIdentifiablePerson: true`, `containsMemorialDesign: true`.

**Restricted pixels.** Because the manifest forbids it, the agent never opened the fixture's pixels,
never opened the produced TIFF's pixels and never passed either to model vision. Every image
judgement in this report is the Operator's, made on screen. Nothing from the fixture entered Git.
The original input, not its old approved TIFF and not the A3 approved PNG, was imported, and the
retained 17 September `CUSTOMER-DESIGN-VISUAL-001` approval was **not** carried over.

**Optional Meitu branches — both skipped, and a skip is not an executed operation.** The accepted
manifest's `expectedProcessingPath` records Enhancement as skipped ("the design is finished") and
BackgroundRemoval as skipped ("a finished design may intentionally keep its background"), and
`WorkflowCatalog.PrepareCustomerDesign` marks both `IsSkippable: true`. Both were skipped through
the ordinary 跳过 control. **No Meitu AI operation ran in this task**, so nothing here re-evidences
A3's enhancement or background-removal work, and no Meitu save-mode setup was needed or performed.

**Trim — `KeepOriginalExtent`, the accepted path for this fixture, and a real gap beside it.** Trim
is not skippable in the catalog. `DeterministicAlphaTrimProcessor` refuses a source whose format
carries no alpha channel — "`{format}` carries no alpha channel; PrintFlow does not infer a
background from colour" — and this fixture is an opaque 24-bit JPEG, so the automatic alpha trim
would not have produced a full-canvas crop. The ordinary 保留原始范围 control was used, exactly as
the manifest's `expectedProcessingPath` and the retained qualification case both record.

*The refusal is not a dead end, and an earlier draft of this report wrongly said the extent decision
was the only valid one.* `DeterministicAlphaTrimProcessor` returns
`TrimResult.ManualCropRequired(...)`, `SessionService` records that as a Failed attempt carrying
`FailureCode.ManualCropRequired`, and `ManualCropEligibility.IsEligible` treats exactly that code as
its first opening. `WicManualCropProcessor` states explicitly that it has **no alpha requirement**
and exists for this case. A manual crop produces a Trim Revision, and Trim is `RequiresReview: true`
— so this workflow does have a supported route in which trimming is executed and a trim artefact is
reviewed by the Operator.

*Why it was not taken:* `SessionViewModel.CanApplyManualCrop` requires `DraftManualCropGeometry`,
which requires a non-empty `CropSelection`. That selection comes from a pointer-drawn rectangle on
the `Session.ManualCropSurface` `Canvas`; the mode radio buttons and margin boxes only refine an
existing selection. The permitted input channel here is native UI Automation on visible controls,
which cannot draw that rectangle, and `Session.ApplyManualCrop` stays disabled without it. An
Operator can draw it.

*Consequence, stated plainly:* `WorkflowEngine.KeepOriginalExtent` persists the Trim step as
`SKIPPED` with reason "Operator chose to keep original extent" and produces **no** Revision, so
**no trim operation was executed and no trim artefact was reviewed in this run**. This run therefore
contains **one** required Operator review, not two. The clause's "trimming" is evidenced only as the
recorded operator extent decision. This is a precise remaining gap of the same kind as the six
variant rows, and its smallest safe next action is in §7.8.

**Physical size — read from the retained case's own contract, not from a filename.** The retained
qualification case records `PrintDimensions 50.8×100 mm (600×1181 px @ 300 dpi, Custom)`; the
harness constant behind it is `PrintDimensions.FromMillimetres(50.8, 100, SizePreset.Custom)`, a
*fit box*. The preset's `productionGeometryContract.resize.automaticLimitingEdge.boundingBoxRule` is
`sourceRatio >= boxRatio ? WIDTH : HEIGHT`; here `1024/1536 = 0.6667 >= 600/1181 = 0.5080`, so the
limiting edge is **WIDTH at 50.8 mm** and the other edge is Photoshop-derived under constrained
proportions — which is why that case produced 600 × 900 px.

The ordinary UI offers `PRESET_FIT` (A3 landscape / A3 portrait / A4 / A5) or `CUSTOM_TARGET_EDGE`
with one authoritative physical value, per `customTargetEdge.authoritativePhysicalValueCount: 1`.
The single authoritative value the retained case actually applied is therefore what was entered:
**目标边 = 宽度 (WIDTH), 目标尺寸 = 50.8 mm**. `600 = round-half-away-from-zero(50.8 × 1500 / 127)`,
identical to the retained case's outcome. No minimum-DPI traffic light, no three sizes, no new
dimension rule and no enlargement approval were invented; the preflight reported 状态 = 无需放大.

Persisted plan, read back from the database:

```
SizingMode = CUSTOM_TARGET_EDGE      SizingTargetEdge = WIDTH        SizingRequestedMm = 50.8
TargetPlanPhotoshopEdge = WIDTH      TargetPlanProjectedPixel = 600 × 900
TargetPlanDirection = SHRINK         TargetPlanResizePolicy = BICUBIC_SHARPER
TargetPlanScale = 75/128             TargetPlanProductionDpi = 300
TargetPlanSourceSha256 = 8A7063D8F81FB72A1DB7F5633980660905E2A6C3D3971E4F94A138B5C8394879
```

**White underbase.** `W1_1PX` — the preset's `whiteUnderbaseContract.branches` entry for "ordinary
complete designs" and the branch the retained case used. Selected and confirmed through the ordinary
`Session.WhiteUnderbaseChoices` list and 确认 W1 分支 control. The Product states in that panel that
it has no default and will not decide from the image.

## 5. Desktop admission and the ordinary gate

One **current** exclusive-use / saved-work confirmation was obtained from the Operator before any
desktop input, for this execution specifically. The A2 and A3 windows were not reused. Verbatim
answer: 「可以独占，我会配合」. Recorded in `exclusive-use-confirmation.json` with the exact question
asked, which disclosed the real customer job then occupying the workstation.

At the moment the confirmation was requested, the workstation was in genuine production use:
Photoshop held `Filomena Hansen.tif`, CorelDRAW held the matching `.cdr` under
`D:\2工作Disk\…\九月\`, and the Maintop RIP was open. **Nothing of that was touched.** The Operator
themselves closed Photoshop and returned Meitu to its start page; the agent then launched the
accepted install.

| Fact | Value |
|---|---|
| Product | `…\build-pairs\d915b1a6-…\candidate\PrintFlow.App.exe`, PID **1320**, started 13:17:08, command line carries **no arguments** |
| Photoshop | PID **8408**, `D:\Adobe Photoshop CC 2019\Photoshop.exe` — the accepted path. The byte-identical `C:\ps2019\…` copy was neither used nor touched |
| Meitu | PID 8368, `…\MeituApp\XiuXiu\7.8.8.2\XiuXiu.exe`, on its signed start page throughout |
| Gate before the run | 13:22 — **本工作站已通过生产环境校验。** All seven live application checks 通过 |
| Gate at handback | 13:51 — passed again, a **new live verification**, not a passive 刷新状态 |

The gate was run **once** before the business run. It was not retried unchanged: the first
invocation is what produced the pass. Its static section carries **11** 必须满足 rows, all 通过, plus
**2** 仅供参考 advisory rows; the seven live rows moved from 未运行 to 通过 in that one run.

One of those advisories is named here rather than passed over: **已认可文件的只读标记** —
「部分已认可的文件不再标记为只读。其签名仍然一致，因此不会影响生产处理。」 The Product classes it as
informational and not blocking. Its substance is independently mitigated for everything this task
relies on: the receipt, the active record, the preset, the qualification result and both fixtures
were hash-verified before the run and re-hashed unchanged at handback (§3, §8). No read-only
attribute was changed by this task.

### One honest correction to a prior diagnostic

A separate read of the COM Running Object Table from a PowerShell process showed **zero** Photoshop
entries both before and after a fully passing gate (the table itself was readable — it listed two
CorelDRAW documents). So "zero Photoshop ROT entries as read from an out-of-process PowerShell
session" is **not** a reliable indicator of the Product's own Photoshop channel, and the earlier
diagnosis that tied the two together is narrowed accordingly. The unresolved ROT incident recorded
under A3 is not re-opened here, and nothing was done to register, re-install or otherwise change any
installation, shortcut, COM or security setting.

## 6. The primary ordinary workflow — PASS

One continuous run in the visible ordinary UI. Every business transition was a single
`InvokePattern.Invoke()` on a visible, enabled control.

```
Home → 选择文件… (import) → 处理客户设计 / 输出名称 → 原图确认
     → 高清化 跳过 → 去背景 跳过 → 裁边 保留原始范围
     → 打印尺寸 自定义 宽度 50.8 mm → 白墨 W1 1 像素
     → 执行步骤 (Photoshop production Action + TIFF validation) → 批准此 TIFF → 完成
```

| Fact | Value |
|---|---|
| SessionId | `01a0c196-b242-784c-a05d-ca7e980bf829` |
| Output name | `11131-CUSTOMER-20260921-E3C6` |
| Workspace | `Sessions/S_20260921T013113Z_980bf829` |
| Persisted workflow | **`PREPARE_CUSTOMER_DESIGN`** — not `PREPARE_ASSET`, not `GENERATE_PRINT_TIFF` |
| Created / completed | `2026-09-21T01:31:13.858Z` → `2026-09-21T01:46:35.767Z` (UTC) |
| Final state | `State = COMPLETED`, 所有步骤已完成 |
| Attempts | **2**, both `ResultStatus = SUCCEEDED`. Zero failures, zero retries, `RetrySequence = 0`, `RetryOfAttemptId = null` on both |
| Step outcomes | Import APPROVED · OriginalConfirmation APPROVED · Enhancement SKIPPED · BackgroundRemoval SKIPPED · Trim SKIPPED (keep original extent) · PrintDimensions APPROVED · PhotoshopOutput APPROVED |
| Photoshop attempt | `01a0c19b-3849-76ac-be76-a106e06a5eaa`, `PHOTOSHOP_OUTPUT`, adapter **`photoshop-cc2019-production-v1`**, `01:36:10.313Z → 01:36:28.594Z` |

### Source preservation

| Measurement | Value |
|---|---|
| Original, measured immediately before import (13:27:37 local) | `8A7063D8…4879`, 216,162 bytes |
| Session `Source/FIX-CUSTOMER-DESIGN-001.jpeg` snapshot | `8A7063D8…4879` — identical |
| Import Revision `01a0c196-b416-…` | `8A7063D8…4879`, 1024 × 1536, JPEG, RGB, no alpha |
| Original, re-measured at handback | `8A7063D8…4879`, 216,162 bytes — **unchanged** |
| `TargetPlanSourceSha256` on the Photoshop attempt | `8A7063D8…4879` |
| Adapter's own note | "backing unchanged 8A7063D8F81F" |

One vestigial field, so a later reader of the record is not misled: `ProcessingSession.TrimMode`
reads `TIGHT_CROP` with all four margins 0. That is the row's creation-time default — it is already
present in the pre-trim dump `session-db-02-size.json`, before any trim decision — and **no trim
mode was applied**, because the Trim step is `SKIPPED` with no Revision.

`InputSnapshot` still stores only the original path and import time, with no hash of the original
file — the SCRUM-11130 F5 finding, unchanged and carried forward. Source preservation here rests on
contemporaneous external file-level measurement by this executor, disclosed as such. **No schema
migration was proposed or performed**, and none is required by the criterion.

### The produced TIFF, its validation and the final review

| Fact | Value |
|---|---|
| Produced at | `Sessions/S_20260921T013113Z_980bf829/Working/01a0c19b-3849-…/11131-CUSTOMER-20260921-E3C6_51mm_CMYK_W.tif` |
| Delivered at | `Sessions/S_20260921T013113Z_980bf829/Approved/11131-CUSTOMER-20260921-E3C6_51mm_CMYK_W.tif` |
| Bytes | 5,391,052 |
| SHA-256 | `1DD555E877A5B0C8F09A5D06D0EF1C4037070650F393818A33842875A2900A5D` |
| Pixels / physical / resolution | 600 × 900 · 50.8 × 76.2 mm · 300 × 300 PPI |
| Name | `{Name}_{SizeMm}mm_CMYK_W.tif` with `{SizeMm}` = the target **width** rounded to whole millimetres, so `51mm` is a rendering of 50.8 mm and is **not** the authoritative value |
| Validator | `photoshop-tiff-validation-c1-v1` |
| Save settings | `TIFFEncoding.NONE` / `LayerCompression.RLE` / `ByteOrder.IBM`, as copy = true; settled over 3 observations in 2.9 s |
| Cleanup | "signed owned-document discard completed; expected Working document gone" — Photoshop was left document-free |

Structural re-measurement by this executor, parsing the TIFF IFD directly rather than trusting the
record. It is independent **of the Product record**, not of the executor — the executor measured it:

```
byte order II (little-endian)   ImageWidth 600   ImageLength 900
BitsPerSample 8,8,8,8,8         SamplesPerPixel 5    Compression 1 (none)
PhotometricInterpretation 5 (separated)              PlanarConfiguration 1 (interleaved)
ExtraSamples 0 (not alpha)      XResolution 3000000/10000 = 300   YResolution = 300
StripByteCounts 2,700,000 = 600 × 900 × 5            Photoshop image resources present
```

The Product additionally reported `白墨 W1 已验证 · 1 像素 — 普通图案 · 540,000 像素有墨`
(540,000 = 600 × 900, i.e. the whole canvas carries ink, which is what a full-bleed opaque design
gives) and the adapter recorded `spot W1 (photoshop spot True, non-white 540000 px)`. The adapter's
own stated limitation is carried forward verbatim: *"W1 sample content is proven non-empty from the
fifth uncompressed interleaved sample; the inspector does not interpret the ink's visual meaning."*
The presence of a channel named `W1` is not by itself treated as proof of valid white ink; the
inspector requires the Photoshop image resources to identify exactly one W1 **spot** channel and the
fifth sample to be non-empty.

**The final review was a real Operator decision.** The exact artefact and its full current identity
— file name, absolute path, 600 × 900, 50.8 × 76.2 mm, 300 PPI, CMYK 8-bit 5 ink channels, W1
verified with its ink-pixel count, preset id and short hash, and SHA-256 `1DD555E8…A2900A5D` — were
put to the Operator, together with the manifest's own question
(`CUSTOMER-DESIGN-VISUAL-001`: does the TIFF show the complete design at the requested size with W1
covering the intended ink region?). The Operator looked at PrintFlow's TIFF review preview and
answered 「批准这个 TIFF」.

**Who pressed the control, and what the record can and cannot show.** The judgement is the
Operator's; the **executor** then actuated `Session.Approve` through `InvokePattern`, immediately
after that answer, exactly as A3 recorded the same division. `ReviewDecision.Operator = "admin"` is
the Windows account that both the Operator and the executor run as, so the Product record **cannot**
distinguish which of them pressed the button. The attribution above is documentary, not
record-provable, and is not restated anywhere as if it were record-proven.

```
ReviewDecision 01a0c1a4-971a-71b8-9af6-154bdf057c33
  StepKind     PhotoshopOutput          SubjectKind PRINT_OUTPUT
  SubjectId    01a0c19b-7fb4-7c9a-a427-23f73fc3045b
  ReviewedSha256 1DD555E877A5B0C8F09A5D06D0EF1C4037070650F393818A33842875A2900A5D
  Operator     admin                    Decision APPROVED
  DecidedAtUtc 2026-09-21T01:46:24.410Z
```

**Lineage, not byte equality with the input.** The delivered TIFF's bytes are
byte-identical to their own reviewed hash (re-measured independently at 13:47:08 →
`1DD555E8…A2900A5D`, equal to both `PrintOutput.Sha256` and `ReviewDecision.ReviewedSha256`), and the
output's `SourceRevisionId` is the approved import Revision `01a0c196-b416-…` whose SHA-256 is the
original input's. No claim is made that the TIFF hash equals the input hash; conversion changes
bytes.

### Executor actions inside the flow, disclosed separately

- **原图确认** (`Session.ConfirmOriginal`) was invoked by the executor. It is an ordinary in-flow
  transition, not a `RequiresReview` step, and it creates no `ReviewDecision` row.
- **Skips, keep-original-extent, target edge, millimetres, W1 branch, 执行步骤, 完成** were invoked
  by the executor as ordinary in-flow controls.
- **Output name** was typed into `WorkflowSelection.OutputName` through `ValuePattern`, set and
  verified twice.
- **The one required review decision was the Operator's**, on this session's own output and hash.
- **Operator assistance** (this was not an unattended run): the exclusive-use confirmation; selecting
  the fixture in the native 选择文件… dialog, which exposes no UIA patterns and cannot be driven by
  the permitted tools; and the final review decision. The Operator also cancelled a second file
  dialog after declining the live negative checks.

**Verdict for section 6: PASS.** One continuous ordinary `PREPARE_CUSTOMER_DESIGN` run on the fixed
workstation produced a validated production TIFF bound to a real Operator approval, with the source
byte-identical before and after. No failure occurred and none is being masked.

## 7. The six variant clauses

Mechanism labels are strict and are never merged into one undifferentiated PASS:
**harness** = in-process test doubles and fake windows; **synthetic live** = a real WPF window and
real UIA driven by the test, synthetic files, no external application; **ordinary Product** = the
shipped app driven on the fixed workstation.

**Applicability was checked against this workflow's actual wiring, not inherited from SCRUM-11130.**
The rows below cite `PhotoshopTiffFinalReviewTests`, `ProductionTiffInspectorTests`,
`ProductionTiffReviewDecoderTests` and `PhotoshopTiffWorkflowOutputTests`, which are the
`PhotoshopOutput` / production-TIFF wiring, rather than SCRUM-11130's Meitu-step rows.

**All evidence cited below was executed in this task, non-building, from the accepted candidate
pair's own retained `PrintFlow.Tests.dll`.** Every TRX carries
`storage=…\build-pairs\d915b1a6-…\harness\printflow.tests.dll`, so applicability rests on direct
execution at the accepted pair rather than on a manifest comparison. No suite was replayed and no
counts from different runs are summed.

| Run | Filter | Result | Evidence |
|---|---|---|---|
| A | `ProductionTiffInspectorTests` | **15 / 15 passed** | `tiff-validation/tiff-inspector.trx` |
| B | `PhotoshopTiffFinalReviewTests` + `ProductionTiffReviewDecoderTests` | **38 / 38 passed** | `tiff-validation/tiff-final-review.trx` |
| C | `RetryAndReviewTests` + `StartupRecoveryTests` + `StopAndTakeOverTests` + `ManualResultImportTests` + `PhotoshopTiffWorkflowOutputTests` | **71 / 71 passed** | `variant-contracts/variant-contracts.trx` |

### 7.1 Rejected review

| Field | Value |
|---|---|
| Identity | `PhotoshopTiffFinalReviewTests.Rejection_recycles_the_exact_TIFF_records_the_reason_and_returns_to_RetryRequired`; `A_rejected_TIFF_stays_auditable_after_its_bytes_are_recycled`; `A_mutated_TIFF_cannot_be_rejected_and_the_Recycle_Bin_is_never_called`; `A_failed_recycle_records_no_rejection_and_keeps_the_TIFF`; `A_restart_after_a_completed_rejection_changes_nothing` |
| Mechanism | **Harness**, candidate-pinned (run B) |
| Wiring | This is the `PhotoshopOutput` final-review path itself, the same path the primary run used |
| Outcome | Rejection recycles the exact reviewed TIFF, records the reason, returns the step to `RetryRequired`, and the rejected output stays auditable after its bytes are gone. A TIFF mutated after validation can be neither approved nor rejected, and the Recycle Bin is never called for it. Passed |
| Exact remaining gap | **No review has been rejected in the ordinary Product on this workstation.** The primary run contains zero rejections, and the Operator declined the authorised live disposable check |
| Smallest safe next action | One fresh disposable session in the running candidate, driven to the TIFF review on a synthetic input, with the Operator pressing 拒绝并重新生成 TIFF **declared as a deliberate functional-test rejection**, then a read-only `ReviewDecision` / `PrintOutput` readback. Needs an Operator and a current exclusive-use window |

### 7.2 Retry

| Field | Value |
|---|---|
| Identity | `PhotoshopTiffFinalReviewTests.Retry_after_rejection_uses_a_new_attempt_and_a_new_output_path`; `PhotoshopTiffWorkflowOutputTests.Retry_uses_a_new_attempt_a_new_Working_directory_and_a_new_output_path`; `RetryAndReviewTests.Reject_then_retry_keeps_the_rejected_Revision_audit_visible_and_produces_a_distinct_approved_one`; `Retry_after_a_fake_failure_gets_a_fresh_attempt_and_working_directory_then_succeeds`; `StartupRecoveryTests.Retry_after_recovery_gets_a_new_attempt_and_a_new_working_directory` |
| Mechanism | **Harness**, candidate-pinned (runs B and C) |
| Clause reading | Carried forward from the settled SCRUM-11130 finding: this is the **retry of an automated step through the supported workflow**, with clean attempt and working-copy provenance. It is not unattended re-invocation, and no `AutoRetry` / `RetryPolicy` / `MaxRetries` construct exists in `src/`. Nothing was added |
| Outcome | A retry after rejection or failure takes a **new** attempt id, a **new** `Working\<attemptId>\` directory and a new output path; the earlier attempt and its audit survive; the approved result is a distinct sibling, not a child. Passed |
| Exact remaining gap | No Product retry has been observed on this workstation; the primary run recorded zero failures and zero retries |
| Smallest safe next action | The same disposable session as 7.1 — after the declared test rejection, press the ordinary retry route and read back `RetryOfAttemptId` / `RetrySequence` and the new working directory. Do not inject a live failure and do not bypass a non-retryable guard |

### 7.3 Manual takeover — a precise, evidenced scope limit

| Field | Value |
|---|---|
| Identity | `StopAndTakeOverTests` (21 rows) — `Take_over_ends_the_attempt_and_hands_the_session_to_the_operator`, `Stopping_releases_the_global_automation_lock(mode: TakeOver)`, `A_restart_after_take_over_does_not_resume_automation`, `Re_entry_creates_a_new_attempt_and_leaves_the_handed_off_one_immutable`, `Releasing_the_lock_does_not_let_a_handed_off_session_start_another_run`, `Internal_work_offers_no_take_over`; `ManualResultImportTests` (18 rows) including `Takeover_import_review_restart_and_downstream_preserve_truth(step: Enhancement / BackgroundRemoval)` and `A_handed_off_review_offer_must_be_rejected_before_manual_replacement` |
| Mechanism | **Harness**, candidate-pinned (run C) |
| Wiring check — the finding | `ManualResultEligibility.Supports(StepKind step) => step is StepKind.Enhancement or StepKind.BackgroundRemoval`. **Manual result import is closed to those two Meitu steps and does not cover `PhotoshopOutput`.** The `StopAndTakeOverTests` rows likewise exercise Enhancement and BackgroundRemoval |
| What this means for 11131 | The *takeover* half is workflow-wide and reaches this workflow: `HandOff` is step-generic in the engine, and 转为人工处理 (`Session.HandOff`) was **present** on the live TIFF review surface in the primary run — the retained UIA dump records presence, not an enabled flag, and the control was never invoked, so "enabled" is not claimed. The harness rows prove hand-off ends the attempt, releases the lock, leaves the handed-off attempt immutable and refuses automation to resume without explicit re-entry. The *validated manual-result submission* half has **no route at `PhotoshopOutput` in this Product version**, so a manually produced TIFF cannot be submitted back through the Product for a customer design |
| Honest label | This is an existing closed scope (SCRUM-11092 / SCRUM-11112), **not** a defect found here, and no source was changed to widen it |
| Exact remaining gap | No takeover has been performed in the ordinary Product on this workstation for this workflow, and the manual-result half is architecturally out of reach for `PhotoshopOutput` without new Product work that this task must not do |
| Smallest safe next action | On a disposable session, use the supported 转为人工处理 at the TIFF review and read back `HandedOffAtUtc` / `HandOffReason` / attempt immutability. Widening manual-result submission to `PhotoshopOutput` is separate, separately authorised work and is **not** claimed here |

### 7.4 Interruption

| Field | Value |
|---|---|
| Identity | `PhotoshopTiffFinalReviewTests.A_crash_before_the_recycle_leaves_the_TIFF_awaiting_review`, `A_crash_after_the_recycle_records_no_decision_and_refuses_the_next_one`, `A_crash_before_the_promotion_leaves_the_TIFF_awaiting_review`, `A_crash_after_the_promotion_resumes_into_the_same_Approved_file`, `A_restart_after_a_completed_approval_changes_nothing`, `A_restart_after_a_completed_rejection_changes_nothing`; `PhotoshopTiffWorkflowOutputTests.Restart_before_the_success_commit_interrupts_the_attempt_and_adopts_no_TIFF`, `Restart_after_the_success_commit_duplicates_nothing_and_reruns_nothing`; `StartupRecoveryTests` (13 rows) |
| Mechanism | **Harness**, candidate-pinned (runs B and C) |
| Outcome | A crashed `Running` attempt recovers to `Interrupted` and fabricates no Revision; a restart before the TIFF success commit adopts no TIFF; a restart after it duplicates and re-runs nothing; a lock whose owner is alive or unverifiable is never stolen; a partial file left by a crash is quarantined and protected areas are untouched; recovery is idempotent. Passed |
| Exact remaining gap | **The ordinary `PrintFlow.App` process has not been interrupted and recovered as acceptance evidence for this workflow.** No orderly close/reopen of a paused disposable session was performed either, because the Operator declined the live negative checks |
| Smallest safe next action | A new disposable session paused at a review, then an **orderly** close and reopen of the app, resumed through `Home.ResumeSession` scoped to that session's own row. That proves orderly restart only and must not be written up as crash recovery. No process kill, no power interruption, no replay of the two pre-existing August interrupted tasks — which were not opened or touched in this task |

### 7.5 White-ink validation failure

| Field | Value |
|---|---|
| Identity | `ProductionTiffInspectorTests.Any_nonproduction_fact_is_rejected` over **12** invalid contracts — `compression`, `byte order`, `five 8-bit` (SamplesPerPixel 4), `separated CMYK`, `interleaved`, `alpha`, `300` dpi, **`ChannelName: "White"` → rejected for not being W1**, **`ChannelKind: 1` → rejected for not being a spot channel**, **`FifthSampleNonEmpty: false` → rejected as empty**, `pyramid`, `RLE`; plus `Truncated_or_non_tiff_bytes_never_become_a_candidate`; plus `ProductionTiffReviewDecoderTests.A_TIFF_the_production_inspector_refuses_produces_no_payload(samples: 4 / 6)`, `A_TIFF_changed_after_validation_produces_no_payload`, `A_missing_TIFF_produces_no_payload` |
| Mechanism | **Harness / isolated manual-validation route**, candidate-pinned (runs A and B) |
| Test inputs | Each case writes a **new disposable synthetic TIFF** into a fresh `%TEMP%\PrintFlow-ProductionTiffInspector-<guid>` directory and deletes it afterwards. The accepted baseline TIFF under `Baseline\workstation-v1\actions\authoring\` is opened **read-only** and is unchanged |
| Outcome | Every invalid fact is refused with `FailureCode.OutputValidationFailed` and a technical detail naming the violated fact. The three W1-specific rows are the white-ink clause proper: a wrongly named channel, a non-spot channel and an empty fifth sample are each refused. Passed |
| What was **not** done | No live Product-owned output, approved TIFF, Photoshop Action, preset or validator was altered to manufacture a failure, and no all-image-255 mask or input-white-preservation requirement was invented |
| Exact remaining gap | No white-ink validation failure has been observed against a real Photoshop output on this workstation. Producing one would require either corrupting a Product-owned output or changing the Action or preset — all excluded — so this row closes at candidate-pinned isolated-validation level by design |

### 7.6 Rejected-TIFF Recycle Bin behaviour

| Field | Value |
|---|---|
| Identity | `PhotoshopTiffFinalReviewTests.Rejection_recycles_the_exact_TIFF_records_the_reason_and_returns_to_RetryRequired`; `A_rejected_TIFF_stays_auditable_after_its_bytes_are_recycled`; `A_failed_recycle_records_no_rejection_and_keeps_the_TIFF`; `A_mutated_TIFF_cannot_be_rejected_and_the_Recycle_Bin_is_never_called`; `A_crash_before/after_the_recycle…`; `The_file_lifecycle_is_not_conditional_on_a_production_adapter` |
| Mechanism | **Harness**, candidate-pinned (run B) |
| Product route, read at source | `SessionService.RecycleRejectedOutput` sends the exact rejected TIFF through `IRecycleBin`; `Infrastructure.Workspace.RecycleBin` uses `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(..., RecycleOption.SendToRecycleBin, ...)` and carries **no hard-delete fallback** — a Recycle-Bin failure is a structured `WorkspaceError` and the rejection is then **not** recorded. `PrintOutput.RecycledAtUtc` marks the row, and `ProductionTiffReviewService` refuses to decode a recycled output with `OutputMissing` |
| Outcome | Only the precisely identified rejected output is recycled; the rejected-history metadata survives its bytes; a failed recycle leaves both the file and the absence of a rejection. Passed |
| Exact remaining gap | **The Product's own recycle path has not been exercised live on this workstation.** The authorised disposable check — letting the Product recycle one new disposable unapproved TIFF after a declared test rejection — was offered and declined |
| Smallest safe next action | Coalesced with 7.1: the same declared test rejection covers this row. Record the TIFF's pre-rejection identity, let the **Product** move it, then read back `RecycledAtUtc` and the Recycle Bin. Never empty the Recycle Bin, never delete or move files manually, and never touch the primary approved TIFF |

### 7.7 What was offered and declined, stated plainly

The bounded live negative checks (7.1, 7.2, 7.4, 7.6, coalesced onto **one** fresh explicitly
disposable session with a synthetic 1024 × 1536 JPEG generated for this task and kept outside the
repository and outside `D:\PrintFlowStudio`) were prepared, the file dialog was opened, and the
Operator answered 「现在不做负向检查」. That is the Operator's scope decision and it is recorded as
such, verbatim, in `negative-checks-declined.json` beside the grant it modifies — the exclusive-use
record had listed a functional-test rejection among the planned Operator assistance, and only the
approve half was performed. Those four rows remain at candidate-pinned harness level with the
precise next actions above, and **no live ordinary-Product observation is claimed for any of the
six**. The disposable image was never imported; it stays in the session scratchpad, measured at
`07F90E65…87D3`, 50,714 bytes.

### 7.8 Trimming — the seventh precise gap

Not one of the criterion's six variant clauses, but recorded in the same style because §4 found it.

| Field | Value |
|---|---|
| Gap | No trim **operation** was executed and no trim artefact was reviewed. The Trim step is persisted `SKIPPED` by the operator's keep-original-extent decision, which is the accepted path for this fixture but is not an executed trim |
| Why | The automatic alpha trim cannot measure an alpha-less source; the supported route out of that refusal is manual crop, whose rectangle needs a pointer drag on the `Session.ManualCropSurface` canvas that the permitted UIA channel cannot make, so `Session.ApplyManualCrop` never becomes available to it |
| Reused evidence | `ManualCropStepTests`, `ManualCropGeometryPersistenceTests`, `KeepOriginalExtentPersistenceTests`, `TrimBoundsPersistenceTests` and `ManualCropAdjustmentUiTests` exist in the retained harness. They were **not** re-executed in this task and nothing is claimed from them here |
| Smallest safe next action | On a fresh disposable session, run 执行步骤 at 裁边, let it fail with `ManualCropRequired`, have the **Operator** draw a rectangle on the manual-crop surface, apply it, and take their decision on the resulting Trim Revision. Needs an Operator and a current exclusive-use window. Do not force a visibly smaller crop on the primary approved session and do not substitute a different fixture to manufacture one |

## 8. Safety, non-actions and handback

**Not performed under this task:** no build, restore or new build pair; no full-suite or
standard-set replay; no qualification, revalidation publication or revocation; no preset, signed
evidence or Photoshop Action modification; no Product, test-source, schema or configuration-file
edit; no signing, certificate or PKI work; no install, deploy, push or Jira transition; no
production customer job; no global routing change; no reopening of Prompt 24, 25 or 26 or of
R1–R4 / A0; no Maintop, RIP or printer control and no physical printing; no forced crash, no
arbitrary dialog, no indefinite monitoring; no deletion of historical or approved evidence; no start
of SCRUM-11132. Prompt 25 was **not** executed. SCRUM-11130 was **not** reopened and is **not**
upgraded.

**Handback state, 13:51–14:26 local.**

| Item | Observation |
|---|---|
| Gate | 13:51 — **本工作站已通过生产环境校验。**, all seven live checks 通过. A new live verification, not a refresh |
| PrintFlow | PID 1320, the candidate's own `PrintFlow.App.exe`, left running on the Home screen |
| Photoshop | PID 8408, `D:\Adobe Photoshop CC 2019\Photoshop.exe`, title exactly `Adobe Photoshop CC 2019` — **document-free** |
| Meitu | PID 8368, `美图秀秀` start page — no document, never used in this task |
| Automation lease | Canonical `workstation-automation-v1.db`, resource `printflow-studio.external-automation.v1`: **all owner fields null** at `2026-09-21T02:26:36Z`. No outer lease, no competing lease, no watcher was added. The store file's hash moved from `F2EFEFB3…1C02B` to `EAB7F13A…C4863`, which is the Product acquiring and releasing it during the gate runs and the Photoshop step — an honest in-run change, not a mismatch |
| Probe directories | **Nine** historical `EnvironmentVerification` directories, unchanged. The Product cleaned up its own new probes. No old directory was swept |
| Retained authority rehashed | Receipt `52E6DBC5…8FBC`, active record `E6A7D7EA…F3B9`, preset `8484F0AA…C8E0F`, qualification result `4083F1FB…5FDA1A`, `FIX-CUSTOMER-DESIGN-001.jpeg` `8A7063D8…4879`, `FIX-FINE-HAIR-001.jpg` `5A705FE3…8D8E` — **all unchanged** |
| A3 deliverable | `A3-R3-FINE-HAIR-20260921.png` still `8D94D320…80C1`, untouched |
| Primary deliverable | `11131-CUSTOMER-20260921-E3C6_51mm_CMYK_W.tif` `1DD555E8…00A5D`, untouched by any later action |
| Customer work | The Operator's Photoshop / CorelDRAW / Maintop job was never opened, closed, saved or modified by this task |

**In-run lease rows are absent, and that absence is preserved.** `session-record` carries no
`AutomationLock` / `AutomationLogEntry` rows, so in-run lock ownership is not evidenced from the
Product record — the SCRUM-11130 F10 finding, unchanged. A free lease observed at handback is **not**
back-inferred to mean the lease behaved correctly during the run.

**Evidence index** — `artifacts/scrum-11131/11131-customer-20260921-e3c6/`:

```
observation-sidecar.json          invocation / process / binary / receipt / preset / fixture provenance
exclusive-use-confirmation.json   the current window, its verbatim grant and the question asked
negative-checks-declined.json     the Operator's verbatim decline of the live negative checks
desktop-state-before-request.json the real customer job present when the window was requested
gate-1-before.txt                 readiness before the live run (live rows 未运行)
gate-1-allchecks.txt              the full check list, thirteen static rows 通过
gate-2-passed.txt / -allchecks    13:22 pass, seven live rows 通过
gate-3-handback.txt / -allchecks  13:51 handback pass
rot-before-gate.txt               the ROT observation that narrows the earlier diagnosis
ui-after-choose-file.txt          workflow selection, source identity and pixel dimensions
ui-03-after-keep-extent.txt       trim decision, size presets and recommendations
ui-04-after-size.txt              print-dimensions preflight rows
ui-05-tiff-review.txt             the TIFF review surface and its ten production-metadata rows
ui-06-completed.txt               completed session screen
session-db-01-created.json        session, InputSnapshot, import Revision
session-db-02-size.json           persisted CUSTOM_TARGET_EDGE plan
session-db-03-tiff.json           PrintOutput and the Photoshop attempt with its adapter notes
session-db-04-completed.json      ReviewDecision, APPROVED PrintOutput, COMPLETED session
lease-pre.json / lease-handback.json   canonical lease rows, read-only, before and after
handback-rehash.json              retained authority re-measured at handback
tiff-validation/*.trx             runs A and B, candidate-pinned
variant-contracts/*.trx           run C, candidate-pinned
```

All SQLite reads used `Mode=ReadOnly` through the candidate's own `Microsoft.Data.Sqlite`. No
WAL-backed file was copied and no business schema was changed. No fixture pixels entered Git,
uploads or model vision.

## 9. Independent review

### Mechanism and its actual limits

| Fact | Value |
|---|---|
| Reviewer | **One** scoped `personal-dev-reviewer` subagent, isolated context, `Read` / `Glob` / `Grep` only |
| Isolation | Genuine: no shell, no execution, no hashing, no delegation, no access to this executor's reasoning or conversation. It received the verbatim criterion, an evidence index and source pointers, and it re-read the CSV row at source rather than accepting it as quoted |
| Rounds | One. No affected-scope recheck was needed: every finding is documentation-only and none changed another row's evidence |
| Real limitation | It could not execute, build, hash or re-run anything. It compared **documents against documents**, never bytes against a digest, so every SHA-256 in this report remains **the executor's measurement**, accepted by the reviewer as such. It could not observe the live UI, who pressed any control, the `-VerifyOnly` output, the executor's TIFF IFD parse, process identities, the desktop state, the Recycle Bin, or the probe directories |
| Status line | **INDEPENDENT READ-ONLY RECORD AND SOURCE REVIEW COMPLETED; INDEPENDENT RE-EXECUTION NOT PERFORMED** |

No new database field, cryptographic signature, second machine, mandatory independent re-execution
or schema migration was added to the acceptance criterion, and SCRUM-11130 was not reopened.

### Findings, severity and disposition

| # | Severity | Finding | Disposition |
|---|---|---|---|
| F1 | **MAJOR** | The trim justification was wrong. `DeterministicAlphaTrimProcessor` returns `TrimResult.ManualCropRequired`, which `ManualCropEligibility` treats as the opening of the supported manual-crop route; `WicManualCropProcessor` has no alpha requirement and exists for exactly this case. So `KeepOriginalExtent` was **not** "the only valid decision", and the missing trim review is a gap, not a property of the fixture | **Accepted and corrected.** Verified at source by this executor. §4 rewritten, §7.8 added as a seventh precise gap with its smallest safe next action, and the §10 trimming row downgraded from PASS to **PARTIAL — decision recorded, operation not executed**. No Product change was made or proposed |
| F2 | MINOR | "All thirteen static checks were already 通过" misstated the gate: it carries 11 必须满足 rows plus 2 仅供参考 advisories, and the report never named the 已认可文件的只读标记 advisory | **Accepted and corrected.** Counts fixed and the advisory named verbatim with its mitigation |
| F3 | MINOR | Who actuated `Session.Approve` was not stated, and `ReviewDecision.Operator = "admin"` cannot distinguish Operator from executor | **Accepted.** §6 now says the executor pressed the control on the Operator's decision, and that the record cannot evidence which of them did |
| F4 | MINOR | "present **and enabled**" for `Session.HandOff` exceeds the retained UIA dump, which records presence and an off-screen flag but no enabled flag | **Accepted.** Softened to "present"; the control was never invoked |
| F5 | MINOR | The Operator's decline of the live negative checks — the sole reason four rows stay at harness level — had no artefact, while the grant it modifies did | **Accepted.** `negative-checks-declined.json` added with the verbatim answer, the question, the consequence and the unused disposable input's hash |
| F6 | OBSERVATION | `ProcessingSession.TrimMode = TIGHT_CROP` persists although Trim is `SKIPPED`; a later reader could take it for an applied trim mode | **Accepted.** §6 now identifies it as the creation-time default, present before any trim decision |
| F7 | OBSERVATION | The sidecar's `createdAtLocal` predates facts it contains, so it is an amended executor file rather than a pre-committed attestation | **Accepted.** §3 now says so explicitly |
| F8 | OBSERVATION | Three wordings invited misreading: "independent … by this executor"; the matrix crediting the skipped Meitu branch as PASS; "no harness" in §2 beside harness tests in §7 | **Accepted** for the first two — "independent of the Product record" and **NOT EXERCISED (accepted skip)**. The third is left as written: §2's row is about how the **Product** was actuated, §7 is about test execution, and both scopes are already stated |

### Verified without defect

The reviewer confirmed at record and source level, independently of this report's prose: the
persisted workflow, `COMPLETED` state, two `SUCCEEDED` attempts with zero retries, the exact step
outcomes, exactly **one** `ReviewDecision`, and the closed hash chain
`ReviewDecision.ReviewedSha256 = PrintOutput.Sha256 = producing Revision.Sha256`, with the decision
timestamp inside the session's own window; the size derivation, including the engine's own
`sourceWidthPixels * maxHeightMm >= sourceHeightPixels * maxWidthMm` form of the bounding-box rule
(`102400 >= 78028.8` → WIDTH), the exact `600` and `900` projections, the `75/128` scale and the
`{SizeMm}` rounding that makes `51mm` a rendering; the `KeepOriginalExtent` transition; the
`ManualResultEligibility` scope limit and `HandOff`'s step-generic gating; and all **113**
`storage=` attributes across the three TRX files pinned to pair `d915b1a6-…`, with counters 15/15,
38/38 and 71/71, no non-`Passed` outcome anywhere, the per-class row counts exact, and every cited
test name present and Passed — including all twelve `Any_nonproduction_fact_is_rejected` labels.

It found **no** case of harness evidence presented as an ordinary-Product observation, no claimed
review that did not happen, no claim that source preservation is provable from the Product record,
no summing of counts across runs, and no implication that the six variant clauses are
production-observed.

### Reviewer verdict, as delivered

**SCRUM-11131 should be accepted as PARTIAL**, and on the trimming row the report was, before
correction, slightly generous to itself. The golden path is genuinely production-observed and
internally consistent; all six variant clauses rest on candidate-pinned harness or
isolated-validation evidence that is real, pinned and fully passing, labelled without inflation and
carrying precise next actions.

## 10. Acceptance matrix

| Clause | Evidence level | Status |
|---|---|---|
| Continuous customer-design run: import → confirmation → optional Meitu → trim → physical dimensions → Photoshop production Action → TIFF validation → final approval | **Ordinary Product, fixed workstation** | **PASS** |
| Optional Meitu processing | Ordinary Product — both steps **skipped** through the supported control, per the accepted manifest. A skip is not an executed operation | **NOT EXERCISED (accepted skip)** |
| Trimming | Ordinary Product — the operator extent decision was recorded through 保留原始范围. No trim operation ran, no trim Revision exists, and therefore **no trim review** exists. The supported manual-crop route was reachable but needed a pointer drag the permitted channel cannot make (§4, §7.8) | **PARTIAL — decision recorded, operation not executed** |
| Physical dimensions | Ordinary Product — `CUSTOM_TARGET_EDGE` WIDTH 50.8 mm, persisted plan 600 × 900 @ 300 dpi, SHRINK / BICUBIC_SHARPER, no enlargement | PASS |
| Photoshop production Action + TIFF validation | Ordinary Product — `photoshop-cc2019-production-v1`, `photoshop-tiff-validation-c1-v1`, structure independently re-measured | PASS |
| Final approval | Ordinary Product — one real Operator `APPROVED` decision bound to the delivered bytes | PASS |
| Source remains untouched | External file-level measurement by this executor, before and after; not provable from `InputSnapshot` | SUPPORTED, provenance disclosed |
| Rejected review | Candidate-pinned harness | SUPPORTED, not production-observed |
| Retry | Candidate-pinned harness | SUPPORTED, not production-observed |
| Manual takeover | Candidate-pinned harness for the takeover half; **no manual-result route exists at `PhotoshopOutput`** | PARTIALLY SUPPORTED, with an evidenced scope limit |
| Interruption | Candidate-pinned harness | SUPPORTED, not production-observed |
| White-ink validation failure | Candidate-pinned isolated validation over disposable synthetic TIFFs | SUPPORTED, not production-observed |
| Rejected-TIFF Recycle Bin behaviour | Candidate-pinned harness | SUPPORTED, not production-observed |

## 11. Reassessment

| Item | Status | Reason |
|---|---|---|
| **Primary ordinary flow** | **PASS, with one qualification** | One continuous `PREPARE_CUSTOMER_DESIGN` run on the fixed workstation, ordinary app, ordinary controls, real Operator approval bound to the delivered TIFF's own hash, source unchanged. The qualification: the trim step recorded the operator's extent decision but executed no trim operation, so the run carries one required review, not two (§4, §7.8) |
| **This bounded execution** | **COMPLETE within its authorization** | Authority re-verified, sidecar written, primary run executed and evidenced, six-row matrix built from wiring actually checked, all cited variant evidence re-executed non-building at the accepted candidate pair, one isolated independent review obtained and its MAJOR finding accepted and corrected, safe handback established. The four live negative checks were prepared and offered; the Operator declined them |
| **SCRUM-11131 coverage** | **PARTIAL** | The golden path is production-observed. Not one of the six variant clauses is an ordinary-Product observation; manual takeover additionally has no manual-result route at `PhotoshopOutput`; and the criterion's "trimming" is evidenced only as a recorded extent decision, with the manual-crop route unreached by the permitted input channel |
| **SCRUM-11130** | **PARTIAL, unchanged** | Not reopened, not re-run, not upgraded. Its 21 September golden-path PASS, its evidence closure and every historical failure stand exactly as written |
| **Whole-project release** | **NOT READY** | SCRUM-11131 is PARTIAL, SCRUM-11130 is PARTIAL, and SCRUM-11132, 11135, 11136, 11137 and the 11138 integrated gate are unexecuted |

**SCRUM-11132 (Generate Print TIFF fixed-workstation E2E) is the next planned business acceptance
item.** Nothing found here blocks it: the Photoshop production path, the TIFF validator and the
final-review/recycle lifecycle all behaved correctly under this run, and `GeneratePrintTiff` shares
that tail. It was **not** started.

**PARTIAL — THE ORDINARY CUSTOMER-DESIGN GOLDEN PATH IS PRODUCTION-OBSERVED AND HOLDS, WITH NO TRIM
OPERATION EXECUTED; ALL SIX VARIANT CLAUSES REST ON CANDIDATE-PINNED HARNESS OR ISOLATED-VALIDATION
EVIDENCE, FOUR OF THEM BECAUSE THE OPERATOR DECLINED THE AUTHORISED LIVE CHECKS, SO SCRUM-11131 IS
NOT FULL.**
