# SCRUM-11171 visual evidence

Final evidence is **30 PNG/JSON pairs: 27 in screens-verified, the stopping capture in screens-runtime, and two R1 crop-error captures in screens-r1**, listed below. All 30 final PNGs were viewed individually. The other image folders, including screens-verified/stopping-1000x700, are retained intermediate evidence and are not the final stopping capture.

These are Chinese, synthetic offscreen WPF UserControl renders at 96 DPI. No App/Window.Show, desktop input, actual Photoshop/Meitu or customer data was used. This is not real-keyboard/UIA-device, DPI scaling, newcomer or production acceptance. Synthetic sources deliberately include tiny fixtures and checkerboard payloads; their names and image quality are fixture data.

`e4-visual-verified.trx` passed 52/52 E4 + SharedReview cases. The later `e4-broad-contracts-green.trx` passed 76/76 and rechecked all three visual cases after the content-visibility/test-contract correction. It captured only the corrected stopping state into screens-runtime; the other 27 images retain the earlier final geometry. Each paired JSON stores screen dimensions, state, essential-action rectangles, ancestor-scroll containment, print summary, custom input and screenshot SHA256. `final-images.json` is the final hash/inspection manifest.

| Group | Viewports | Inspection |
| --- | --- | --- |
| Processing | 1000×700, 1280×720, 1920×1080 DIP | Current application/step/state; no false percentage; actual Stop/TakeOver available; unrelated forms absent |
| Review enhancement | Same three | Before/After, backgrounds/zoom, current checks and fixed decisions; image owns the main column |
| Review background, trim, TIFF | 1000×700 | Relevant checks and specialized TIFF modes; legal correction/crop/approval actions; save form secondary |
| Crop | Three sizes, plus compare at 1000×700 | Full workspace, eight handles, original suggestion/restore/cancel/use; no duplicate manual-apply action |
| Dimensions, white ink, preflight | Three sizes each | Requested versus actual mm/pixels, current input above fold, all three explicit white-ink choices reachable, image preview over 100 DIP high, production contract/risk visible |
| Stopping / stopped / takeover / handed off | 1000×700 | Distinct transient state, failure retry, explicit takeover confirmation, retained-application warning and recovery action |
| Invalid size / enlargement | 1000×700 | Invalid raw text and local reason with disabled continue; enlargement warning and independent authorization |
| R1 crop failure, reload succeeds / fails | 1000×700 each | Real crop command refused at repository commit; preserved draft and result, visible Chinese reason, expanded read-only code and required actions within viewport |

R1 adds [crop failure after successful reload](screens-r1/crop-error-reloaded-1000x700.png) and [crop failure after failed reload](screens-r1/crop-error-reload-failed-1000x700.png). These supplement the original 28 images. Their paired metadata records code/notice/action bounds and the reload branch. Four zh-CN/en regression cases first failed on zero visible notices, then passed after the crop-local placement fix. The original no-error screens have no new visible content.

Initial image inspection found narrow unrelated source forms, a duplicate disabled crop action, custom input below the fold and 30–80 DIP print previews. Meaningful RED tests and follow-up GREEN runs are preserved. The first stopping screenshot had a fixture race: the fake had already changed to Idle during rendering. The corrected screenshot freezes the actual StopRequested event plus the earlier running SessionView; the rendered stopping text is asserted. It is explicitly a runtime snapshot, not a real desktop observation.

[AC map and results](../../../docs/codex/printflow-operator-ux-backlog/SCRUM-11171_RESULTS.md). [Rulings/costs](RULINGS.md). [Maintenance](../../../docs/printflow/guided-processing-review-print.md). [Raw checks](checks/).

## Final individual images

- [crop-1000x700](screens-verified/crop-1000x700.png) — [metadata](screens-verified/crop-1000x700.json)
- [crop-1280x720](screens-verified/crop-1280x720.png) — [metadata](screens-verified/crop-1280x720.json)
- [crop-1920x1080](screens-verified/crop-1920x1080.png) — [metadata](screens-verified/crop-1920x1080.json)
- [crop-comparison-1000x700](screens-verified/crop-comparison-1000x700.png) — [metadata](screens-verified/crop-comparison-1000x700.json)
- [dimensions-1000x700](screens-verified/dimensions-1000x700.png) — [metadata](screens-verified/dimensions-1000x700.json)
- [dimensions-1280x720](screens-verified/dimensions-1280x720.png) — [metadata](screens-verified/dimensions-1280x720.json)
- [dimensions-1920x1080](screens-verified/dimensions-1920x1080.png) — [metadata](screens-verified/dimensions-1920x1080.json)
- [enlargement-blocked-1000x700](screens-verified/enlargement-blocked-1000x700.png) — [metadata](screens-verified/enlargement-blocked-1000x700.json)
- [handed-off-1000x700](screens-verified/handed-off-1000x700.png) — [metadata](screens-verified/handed-off-1000x700.json)
- [invalid-size-1000x700](screens-verified/invalid-size-1000x700.png) — [metadata](screens-verified/invalid-size-1000x700.json)
- [preflight-1000x700](screens-verified/preflight-1000x700.png) — [metadata](screens-verified/preflight-1000x700.json)
- [preflight-1280x720](screens-verified/preflight-1280x720.png) — [metadata](screens-verified/preflight-1280x720.json)
- [preflight-1920x1080](screens-verified/preflight-1920x1080.png) — [metadata](screens-verified/preflight-1920x1080.json)
- [processing-1000x700](screens-verified/processing-1000x700.png) — [metadata](screens-verified/processing-1000x700.json)
- [processing-1280x720](screens-verified/processing-1280x720.png) — [metadata](screens-verified/processing-1280x720.json)
- [processing-1920x1080](screens-verified/processing-1920x1080.png) — [metadata](screens-verified/processing-1920x1080.json)
- [review-background-1000x700](screens-verified/review-background-1000x700.png) — [metadata](screens-verified/review-background-1000x700.json)
- [review-enhancement-1000x700](screens-verified/review-enhancement-1000x700.png) — [metadata](screens-verified/review-enhancement-1000x700.json)
- [review-enhancement-1280x720](screens-verified/review-enhancement-1280x720.png) — [metadata](screens-verified/review-enhancement-1280x720.json)
- [review-enhancement-1920x1080](screens-verified/review-enhancement-1920x1080.png) — [metadata](screens-verified/review-enhancement-1920x1080.json)
- [review-tiff-1000x700](screens-verified/review-tiff-1000x700.png) — [metadata](screens-verified/review-tiff-1000x700.json)
- [review-trim-1000x700](screens-verified/review-trim-1000x700.png) — [metadata](screens-verified/review-trim-1000x700.json)
- [stopped-failure-1000x700](screens-verified/stopped-failure-1000x700.png) — [metadata](screens-verified/stopped-failure-1000x700.json)
- [stopping-1000x700](screens-runtime/stopping-1000x700.png) — [metadata](screens-runtime/stopping-1000x700.json)
- [takeover-confirmation-1000x700](screens-verified/takeover-confirmation-1000x700.png) — [metadata](screens-verified/takeover-confirmation-1000x700.json)
- [white-ink-1000x700](screens-verified/white-ink-1000x700.png) — [metadata](screens-verified/white-ink-1000x700.json)
- [white-ink-1280x720](screens-verified/white-ink-1280x720.png) — [metadata](screens-verified/white-ink-1280x720.json)
- [white-ink-1920x1080](screens-verified/white-ink-1920x1080.png) — [metadata](screens-verified/white-ink-1920x1080.json)
