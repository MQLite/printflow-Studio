# SCRUM-11146 bounded implementation plan

2026-09-23 NZ. Executor: Codex. Task: PF-OPUX-v1-route-purpose-cards. Policy v2.3, explicit route_offset: 0.

## Scope and design

Use the existing three cards, in catalogue order. Add three wrapping lines between each title and existing steps: purpose, approved result, print-size requirement. Preserve every route, step, Choose command/binding, engine eligibility, lock message, preview, output name, and focus/approval implementation. No recommendation or preselection. No delivery, trim transition, correction re-import, production startup or other Epic work.

Authenticated SCRUM-11146 (10876) read on this run: description updated 2026-09-22T14:27:23.277+1200, no comments; saved intact in artifacts/pf-opux-scrum11146/jira-before.json. All five original AC and human bilingual visual validation remain applicable.

Intended English / zh-CN copy:

| Route | Purpose | Result | Dimensions |
|---|---|---|---|
| PrepareAsset | Use this for a picture or logo you will use in a design. Enhance or remove the background if needed. / 适用于要放入设计中的图片或标志。可按需增强图片或移除背景。 | You get an approved transparent PNG. / 得到审核通过的透明 PNG。 | No print size needed. No TIFF. / 无需印刷尺寸，不生成 TIFF。 |
| PrepareCustomerDesign | Use this for a customer's finished design that needs preparation for print. Enhance or remove the background if needed. / 适用于需要做打印前处理的客户成品设计。可按需增强图片或移除背景。 | You get an approved print TIFF (CMYK + white ink). / 得到审核通过的打印 TIFF（CMYK + 白墨）。 | Print size needed. / 需要印刷尺寸。 |
| GeneratePrintTiff | Use this for a finished design that is ready to make into a print file. / 适用于已完成、可直接制作打印文件的设计。 | You get an approved print TIFF (CMYK + white ink). / 得到审核通过的打印 TIFF（CMYK + 白墨）。 | Print size needed. / 需要印刷尺寸。 |

Transparent PNG remains the existing requirement. Optional preparation is explicit; no missing-content restoration or chosen-folder delivery is promised.

Actual source files: WorkflowSelectionViewModel.cs (WorkflowChoice localized projections), WorkflowSelectionView.xaml (wrapping copy), Strings.cs / Strings.resx / Strings.zh-CN.resx. Use established ILocalisationService/WeakEventManager pattern for active-language refresh, preserving existing choice objects and bindings. Add focused WorkflowPurposeCardTests.cs using HomeScreenHarness and WpfRendering. Extend terminology only to resolve the now-implemented card wording.

## AC and validation

1. Both languages: rendered purpose/result/dimensions match the intended facts, resource parity, binding errors absent, wrapping and scroll reachability at 1000x700 and supported 1920x1040 work-area-sized synthetic viewport, 96 DPI. Reuse CapturePng; no new input/capture framework. Human workstation inspection remains separate.
2. Asset route: assert no PrintDimensions or PhotoshopOutput; run existing persisted selection/reshape cases for all routes.
3. Run existing isolated locked-workflow refusal case; unchanged lock resource and command source verified by diff.
4. No selector/default/recommendation: ordinary equal-styled cards/buttons and no selection action on opening; verify unchanged order and no navigation on render/language change.
5. Verify enabled/focusable Tab stops, ordinary navigation modes, real Choose bindings, localization refresh without rebuilding choices; OS Tab/keyboard and human checks remain NOT RUN.

Use failing rendered-text tests before product edits. Then run only new tests, selected existing selection/reshape/lock/accessibility tests, and the four general resource parity/accessor/nonempty checks. No full suite, timestamp regression, Wave1A rerun or startup smoke.

## Isolation preflight (before each execution path)

- Unit/resource: local source XML and static catalogue only.
- New rendering/selection and selected existing tests: HomeScreenHarness directly composes SessionServiceHarness, GUID TempWorkspace and TempDatabase, workspace-local workstation-lease.db and unique test identity, local preset, synthetic PNG, fake Meitu/Photoshop, fake recycle bin, recorded navigation and stub picker. No ApplicationStartup, production DI/recovery/shared LocalAppData lease, real picker or external process. Existing WpfRendering measures/arranges off-screen without Window.Show or OS input; CapturePng is its existing RenderTargetBitmap helper, not native capture. Fixture disposal targets only its generated temporary paths.
- Export: explicit new snapshot/output paths; local ledger supplies mapping metadata only. Existing fixed exporter refuses overwrite. Its current task allowlist/write-action mapping only supports Wave1/1A: minimally add this task and its factual comment marker without interface/schema changes or a subsystem. Historical exports remain immutable. Report external count/link drift if fixed exporter rejects it; never change Jira to force counts.

## Progress, routing and review

- Repository: master, HEAD eea60198c5095243f0694717a7479b00c3e1cd73, original uncommitted changes captured before edits in artifacts/pf-opux-scrum11146/baseline-files.json and baseline/. No checkout/stash/clean/commit/push.
- Wave1A checkpoint: AWAITING_SAFE_DESKTOP; current safe operator confirmation absent and native control disabled. No run, no new postfix run directory. Candidate manifest and current hash comparison saved separately. Pre-fix native PASS is not transferred. Append dated block to WAVE1A_VERIFICATION; retain exact operator checks in handoff.
- UI plan/implementation/visual inspection: NormalRoute = RequestedRoute = ExecutionTarget = ActualRoute gpt-6-astra/high, UNCHANGED, CONTINUE. Runtime turn_context verified for 01a0cb55-7b6d-7f40-a9d0-f02fdb412d56.
- At documentation/export boundary normal Sol Medium; in-place switch unavailable, safe current Astra fallback disclosed as MODEL_SWITCH_UNAVAILABLE. No tiny agent solely for a model label.
- One fresh isolated native Sol High code reviewer (fork_turns=none), original prompt/AC, bounded baseline delta and direct results only. UI visual assessment remains Astra High. Review only SCRUM-11146 and narrow export metadata adaptation, not historical Wave1A code. Correct in-scope findings and recheck affected portion.
- Jira: reconcile comments immediately before one stable-marker evidence comment on SCRUM-11146; then one paginated authenticated initiative readback and exact 23-column BOM CSV. No other Jira writes.
- Ruling: user-authorized automatic bounded implementation and specified records override generic skill extra approval, worktree, commit and duplicate-ledger steps. Brainstorming intent is fully supplied; preserve this brief design and implement without another approval gate.

Completion means bounded implementation, targeted evidence/review, factual synchronization and handoff, with unperformed acceptance explicitly open. Stop after reporting; no autonomous continuation.

## Completed bounded execution

Implemented and validated: 6 new + 18 affected tests passed, zero failures/skips in final runs; eight synthetic PNGs generated with the existing renderer and six distinct views inspected. Earlier intended RED and test-assertion corrections remain in raw evidence. Original human/native acceptance stays open. Fresh independent code review found no actionable issues; requested Sol High, actual route UNVERIFIED, no model-switch claim.

Only comment 10167 was added to SCRUM-11146 after complete reconciliation. Final authenticated initiative readback 2026-09-22T23:12:09.631Z: 17 issues, 26 Blocks, statuses unchanged; separate SCRUM-11146_JIRA_READBACK.json and SCRUM-11146_JIRA_FINAL.csv pass the existing exporter/reparse checks, exact 23 columns/BOM and 34 timestamp strings. See artifacts/pf-opux-scrum11146/RESULTS.md for commands, evidence classes and limitations. Protected history and unrelated work retained. Stop after final handoff/report.
