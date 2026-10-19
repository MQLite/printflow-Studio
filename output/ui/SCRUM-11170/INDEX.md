# SCRUM-11170 evidence

These are real WPF UserControl renders in Chinese, generated offscreen at 96 DPI using synthetic/temp images and fake adapters. No application window, desktop keyboard/input, real Photoshop/Meitu or production data was used. This is not newcomer, real-device, DPI scaling or production acceptance.

Final capture: `checks/e3-visual-final.trx` — 6 passed; 21 PNG/JSON pairs. Every final PNG was viewed individually. JSON records source/screenshot SHA256, essential actions, enabled/CanExecute facts, rectangles, scroll metrics and preview count. Fixture source filenames and English enum suffixes in synthetic task names are test data.

| Screen group | Captures | Inspection |
| --- | --- | --- |
| Home / continue | 1000x700, 1280x720, 1920x1080 | Import first, visible continuing task, history secondary; environment remains scrollable at small heights |
| Explicit route choice | Same three sizes | Three complete selectable cards with purpose/result/dimensions; all buttons fit the inner viewport; source preview/name retained |
| PrepareAsset original | Same three sizes | PNG-specific navigation; original confirmation/route return/home available; no print-size/white-ink/save forms |
| PrepareCustomerDesign original | Same three sizes | Current original image and fixed action bar; required simulation warning remains visible |
| GeneratePrintTiff original | Same three sizes | No Meitu/crop route steps; current original confirmation only |
| PDF pending / review | 1000x700 each | No preview invented before preparation; page1 physical/pixel sizes shown only when known; generated raster review remains explicit |
| PSD pending / review | 1000x700 each | Supported-input/channel caveat visible; prepared raster must be reviewed; no TIFF promise in shared source text |
| Import pending / multiple refusal | 1000x700 each | Cancel-wait action visible; refusal adjacent to file chooser |

The first image pass exposed two genuine problems despite root-rectangle checks: route buttons crossed the ScrollViewer viewport bottom and the refusal message was below Home's fold. `e3-visual-layout-red.trx` preserves two failures. The final code compacts the source summary and moves notice/error details into the import card. Final capture adds ancestor ScrollViewer bounds checks.

All raw runs (including RED/build/fixture failures) are preserved in checks. Use `run-safe-ui.ps1`; the 11 exclusions match the E2 audited visible/opt-in set. E2 audit reference: `../SCRUM-11169/checks/visible-window-audit.md`. Test names containing Production_smoke still use synthetic fixtures and are not production runs.

[Results and AC mapping](../../../docs/codex/printflow-operator-ux-backlog/SCRUM-11170_RESULTS.md). [Maintenance](../../../docs/printflow/guided-import-navigation.md).

## Individual images
- [home-continue-1000x700](screens/home-continue-1000x700.png) — [metadata](screens/home-continue-1000x700.json)
- [home-continue-1280x720](screens/home-continue-1280x720.png) — [metadata](screens/home-continue-1280x720.json)
- [home-continue-1920x1080](screens/home-continue-1920x1080.png) — [metadata](screens/home-continue-1920x1080.json)
- [import-pending-1000x700](screens/import-pending-1000x700.png) — [metadata](screens/import-pending-1000x700.json)
- [import-refused-multiple-1000x700](screens/import-refused-multiple-1000x700.png) — [metadata](screens/import-refused-multiple-1000x700.json)
- [original-GeneratePrintTiff-1000x700](screens/original-GeneratePrintTiff-1000x700.png) — [metadata](screens/original-GeneratePrintTiff-1000x700.json)
- [original-GeneratePrintTiff-1280x720](screens/original-GeneratePrintTiff-1280x720.png) — [metadata](screens/original-GeneratePrintTiff-1280x720.json)
- [original-GeneratePrintTiff-1920x1080](screens/original-GeneratePrintTiff-1920x1080.png) — [metadata](screens/original-GeneratePrintTiff-1920x1080.json)
- [original-PrepareAsset-1000x700](screens/original-PrepareAsset-1000x700.png) — [metadata](screens/original-PrepareAsset-1000x700.json)
- [original-PrepareAsset-1280x720](screens/original-PrepareAsset-1280x720.png) — [metadata](screens/original-PrepareAsset-1280x720.json)
- [original-PrepareAsset-1920x1080](screens/original-PrepareAsset-1920x1080.png) — [metadata](screens/original-PrepareAsset-1920x1080.json)
- [original-PrepareCustomerDesign-1000x700](screens/original-PrepareCustomerDesign-1000x700.png) — [metadata](screens/original-PrepareCustomerDesign-1000x700.json)
- [original-PrepareCustomerDesign-1280x720](screens/original-PrepareCustomerDesign-1280x720.png) — [metadata](screens/original-PrepareCustomerDesign-1280x720.json)
- [original-PrepareCustomerDesign-1920x1080](screens/original-PrepareCustomerDesign-1920x1080.png) — [metadata](screens/original-PrepareCustomerDesign-1920x1080.json)
- [pdf-pending-1000x700](screens/pdf-pending-1000x700.png) — [metadata](screens/pdf-pending-1000x700.json)
- [pdf-review-1000x700](screens/pdf-review-1000x700.png) — [metadata](screens/pdf-review-1000x700.json)
- [psd-pending-1000x700](screens/psd-pending-1000x700.png) — [metadata](screens/psd-pending-1000x700.json)
- [psd-review-1000x700](screens/psd-review-1000x700.png) — [metadata](screens/psd-review-1000x700.json)
- [route-choice-1000x700](screens/route-choice-1000x700.png) — [metadata](screens/route-choice-1000x700.json)
- [route-choice-1280x720](screens/route-choice-1280x720.png) — [metadata](screens/route-choice-1280x720.json)
- [route-choice-1920x1080](screens/route-choice-1920x1080.png) — [metadata](screens/route-choice-1920x1080.json)

Review handoff uses the original Jira snapshot, base-to-HEAD source/test diff and raw evidence. Implementer narrative is evidence provenance, not independent review approval. Source/test changes were staged only after the 160-test target passed; staging changes no code under the running broad suite.

Final validation: safe UI/Architecture 1375 passed /14 inherited failures /0 skipped (1389), exact baseline names/messages all match; no new failures. Workflow/persistence277/277. Focused160/160. Raw TRX/log/filter files and baseline-comparison.json/run-index.json preserve the precise scope. I01/I02 ready for independent review; I03 full integration BLOCKED_DEPENDENCY.
