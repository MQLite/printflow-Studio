# SCRUM-11150 — print-size guidance: implementation checklist

Task `PF-OPUX-v1-SCRUM-11150-impl-v1`, Planning-ID `PF-OPUX-v1-print-size-plain-guidance`, 2026-09-29 NZ. Executes `printflow-remediation-prompts/PrintFlowStudio_SCRUM11150_Print_Size_Guidance_Implementation_Prompt_v1.md` (operator-owned, untracked). The owner approved its bounded interaction brief and authorized implementation, verification, independent review, in-scope fixes, direct `master` publication and SCRUM-11150 Jira updates only.

## Baseline

- `master` = `origin/master` = `fe38160796e39af5dd511cacf676778e9c3bee16` (fetch and `ls-remote` agree). The only local change was the SCRUM-11149 audit residue (`SCRUM-11149_PUBLICATION_STATUS_AUDIT.json`), preserved and not staged.
- Jira pre-read: SCRUM-11150 (id 10880) To Do, 0 comments, parent SCRUM-11139 (In Progress), blocks 11154/11155, no inward Blocks. Transitions discovered live: To Do 11, In Progress 21, In Review 31, Done 41. Moved To Do → In Progress (21) before implementation; read back In Progress.
- Hooks/CI: no `.github/workflows`, no active git hooks. A `master` push triggers no deployment or migration.

## Route

Claude adaptation v1.2 of policy v2.4, `route_offset: 0`. Opus/High for all work; no Sonnet unit. Host model `claude-opus-5-5`, effort UNVERIFIED. Context `CONTINUE`; one fresh read-only `personal-dev-reviewer` (Opus) at the review boundary.

## What the code actually offers (copy is written from this)

| Surface | Real semantics | Source |
|---|---|---|
| Use preset (A3 landscape, A3 portrait, A4, A5 — as configured) | A **maximum**: box (A3), long edge (A4) or short edge (A5). Pressing a preset records it at once. A bigger picture is reduced proportionally; a smaller one is never enlarged (`ResolutionOnly`). | `PresetPrintRecommendation.Fit` → `FitWithinBounds` → `PrintPreparationPlan` (`Mode`, `LimitingEdge`) |
| Custom size | Exactly one exact edge: Width, Height or Long edge (Long edge = the longer side of *this* picture; a square picks Width). The other edge follows proportionally. May enlarge; enlargement needs the existing explicit offer. Draft preflight while typing; Confirm records. | `ScaleToTargetEdge` → `TargetEdgePrintPreparationPlan.Projection` (`SelectedTargetEdge`, `PhotoshopTargetEdge`, `Direction`) |
| Typed maximum bounds (`WidthMmText`/`HeightMmText`) | Still in the view model (draft + `SetPrintDimensions`), not in the current XAML. The summary covers it because it is a bounds plan. | `SetMaximumBoundsCommand` |

There is no width/height/long-edge option on presets and no "recommended size" to invent. A tie (source exactly the box's proportions) is reported by the model as **Width**; the copy follows the model and never infers an edge from rounded dimensions.

## Source-to-copy map

| Copy | Resource | Facts it is formatted from |
|---|---|---|
| Preset help ("a maximum, not the exact print size … never enlarged") | `Session_SizeHelpPreset` | Contract above; no numbers |
| Custom help, naming the existing choices | `Session_SizeHelpCustom` | `Session_CustomSize`, `TargetEdge_*` labels |
| Draft / chosen sentence, "about W × H mm", whole picture incl. see-through edges | `Session_SizeSummaryDraft` / `Session_SizeSummaryCurrent` | `Preflight.PhysicalWidthMm/HeightMm` (projected output pixels ÷ 300 PPI of the sized Revision's whole canvas), `IsDraftPreflight` |
| What decided it | `Session_SizeGovernor*` | New read-only `Preflight.Governor` / `GoverningEdge` / `SelectedTargetEdge`, copied in `SessionService.PreflightFrom` from the same plan: `bounds.RequiresShrink` + `bounds.LimitingEdge`, or `target.Projection.SelectedTargetEdge` + `PhotoshopTargetEdge`. Null governor ⇒ sentence omitted |
| Proportions kept | `Session_SizeSummaryProportions` | Both contracts are proportional |
| Draft enlargement note | `Session_SizeDraftEnlargement` | `Preflight.RequiresEnlargement` on a draft (no offer exists) |
| Plain enlargement warning, naming Change size / Continue with this size / Run step | `Session_SizeEnlargementPlain` | Same condition as before: `Sizing.NeedsEnlargementAuthority` |
| Inline validation | existing `Session_TargetSizeInvalid` | Same rule as Confirm: `ReadCustomSizeCommand() is null` with non-empty text |
| Technical details (collapsed) | `Session_SizeTechnicalDetails` | Unchanged `PreflightRows` (source px, artwork/crop, final canvas, print size, output px, effective source PPI, 300 PPI output, status) and the plan's limiting edge, projected px, fixed 300 PPI and the existing `EnlargementWarning` (scale %, 300 PPI) |

Terminology: heading `Session_PreflightHeading` "Print size check / 印刷尺寸核对" and the zh-CN row label `Session_PreflightPrintSize` 印刷尺寸 (was 打印…), per `docs/printflow/operator-ux-terminology.md`.

## Implementation

- `PrintDimensionsPreflight.cs`: additive `PrintSizeGovernor` enum and three init-only properties (`Governor`, `GoverningEdge`, `SelectedTargetEdge`). Constructor unchanged; never persisted.
- `SessionService.PreflightFrom`: copies those three facts from the plan it already projects. No fitting rule, rounding or comparison added.
- `SessionViewModel.PrintDimensions.cs`: `PresetModeHelp`, `CustomModeHelp`, `PrintSizeSummary`, `PreflightEnlargementNote`, `EnlargementPlainWarning`, `CustomSizeInputHint`; rows rebuilt on a language change from the preflight already shown (no query, no generation change). `SessionViewModel.cs`: one call in the language handler.
- `SessionScreenView.xaml` (right-hand details column only): help beside each choice, validation under the input, summary + collapsed "Technical details" in the preflight section, the plain warning in the existing warning box with the same two buttons, and the plan's pixel/PPI/edge lines plus the scale-% line moved into a collapsed "Technical details" in the preparation section.
- `Strings*.resx` / `Strings.cs`: 15 `Session_Size*` keys in both languages; two terminology value changes.
- Not changed: fitting maths, the 300 PPI rule, enlargement offer identity/expiry/reset, command gating, stale-response guard, approval/delivery/correction, persistence, presets, asset route.

## AC → evidence map

| AC | Evidence | Remaining |
|---|---|---|
| 1 valid maximum bounds → sentence matches the preflight projection in mm and names the governing edge | Preset theory (box/long/short edge, landscape/portrait, within-limits, equal-proportions; en/zh-CN): preflight = plan pixels/edge/mode; sentence mm = preflight = unchanged technical row; edge sentence from the plan's `LimitingEdge`; box never shown as the size. Typed-bounds draft = service projection. Mutation M1 (swapped edge sentence) caught | Human bilingual check |
| 2 enlargement → explicit confirmation still required, Run unavailable until then | Draft note creates no offer; committed offer: `CanRunPhotoshopOutput` false, rendering/details/language switch accept nothing (persisted fingerprint unchanged), only Continue with this size authorises; changed size removes authority | — |
| 3 invalid input → existing validation with plain wording | Invalid/partial/zero/negative input removes the summary, shows `Session_TargetSizeInvalid` beside the input, Confirm keeps its refusal, nothing recorded | — |
| 4 asset route never shows this step | Asset Enhancement and Trim screens: no size state, no `Session.PrintDimensions.*` element shown | — |
| 5 pixel and PPI values remain under details, unchanged | Same eight rows and values; details collapsed by default and every row value reachable when opened; opening writes nothing | — |
| 6 not clipped at supported resolution/scaling, both languages | 20 off-screen layouts (5 states × en/zh-CN × 1000×700/1920×1040, 96 DPI): picture area identical to baseline `fe38160`; every line in the size sections wraps inside the column; every guidance element reachable by scrolling; warning outside the details | Workstation DPI/scaling check (human) |

## Human-check addendum (NOT RUN)

With synthetic images on the supported workstation, in English and 简体中文:

1. On the print-size step, read both "choose this when" lines; press a preset and confirm the "Print size check" sentence names the millimetres and the deciding edge, and that it is smaller than the preset when the picture's proportions differ.
2. Custom size: type a size, change the edge, clear the box, type letters — the sentence follows each change and the hint appears under the input; nothing is recorded until Confirm.
3. With a small picture, confirm a size that needs enlarging: the warning says soft or blurry, Run step stays unavailable until Continue with this size.
4. Open both "Technical details" sections; all pixel and PPI values are present.
5. At 1000×700 and the workstation's own scaling, confirm the picture is not smaller than before and every line can be scrolled to.

## Verification

- Tests were written alongside the code, not strictly first. Two deliberate mutations (limit-edge sentences swapped; target edge forced to Width) failed 4 tests and were restored byte-for-byte.
- New `PrintSizeGuidance*`: 47/47. Existing sizing/preflight/workflow/resource suites incl. MaximumBoundsUiTests and FlexibleSizeUiTests: 264/264.
- Settled candidate: clean `--no-incremental` build 0/0; architecture 452/452; combined UI 601/601 (SCRUM-11148 corrected filter); workflow/persistence 11531/11531, run once because `SessionService.PreflightFrom` is on every session load. Counts overlap.
- Exclusions unchanged: real-window/UIA classes (incl. `PrintDimensionsPreflightUiTests`), `ApplicationStartup` classes, `ProductionCompositionTests`. The baseline recovery hang stays excluded and NOT PASS.
- Renders: 20 off-screen layouts at 96 DPI; the preview area is identical to baseline `fe38160` in all 20; no clipped line; every guidance element reachable by scrolling.
- Independent review: one fresh read-only reviewer, two rounds, CLOSED with no P0–P2. Fixed: within-limits wording (P3-2), customer-design trimmed-canvas case (P3-4/P3-7). Residuals: a parsable number the Domain refuses shows no inline hint (P3-1); other DPI scaling not run (P3-3); zh-CN 打印尺寸/打印准备 remain in older labels on this screen (P3-5).

Full evidence: `artifacts/pf-opux-scrum11150/RESULTS.md` (local, not published).

## Publication and Jira

- Code commit `03da756f6b021cce809b8591229b0c605ebe1f55` pushed to `master` (fast-forward from `fe38160`). Fetch, `ls-remote` and the GitHub API agree: MASTER_PUSH_VERIFIED.
- SCRUM-11150 transitions: 21 To Do → In Progress (before implementation); 31 In Progress → In Review (after the verified push). Evidence comment 10184 carries marker `PF-OPUX-v1-SCRUM-11150-impl-v1`. No other initiative issue was written.
- Not Done: AC6 needs the human bilingual check at the workstation's own scaling, and no acceptance or waiver was given.
- Final authenticated readback 2026-09-29T01:57:19.864Z: 17 issues, 26 Blocks, 51 string labels. The only drift is on SCRUM-11150 and the embedded 11150 status inside SCRUM-11154/11155 links.
- Outputs:
  - [SCRUM-11150_JIRA_FINAL.csv](SCRUM-11150_JIRA_FINAL.csv): 23 columns, UTF-8 BOM, exporter PASS, independent oracle PASS, export-integrity 18/18.
  - [SCRUM-11150_PUBLICATION_STATUS_AUDIT.json](SCRUM-11150_PUBLICATION_STATUS_AUDIT.json).
  - `SCRUM-11150_JIRA_READBACK.json` stays local; it is gitignored because it carries account metadata.
