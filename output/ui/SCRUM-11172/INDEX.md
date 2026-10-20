# SCRUM-11172 visual and verification evidence

Final capture run: [e5-visual-final](checks/e5-visual-final.trx), 11/11 passed (three viewport cases plus eight recovery behavior cases). The 31 final PNGs below were individually inspected through the image viewer after that run. [visual-manifest.json](visual-manifest.json) records exact SHA-256 values; each JSON contains measured bounds and scroll containment. The filenames/destinations and chosen primary actions are asserted where listed in each metadata file.

Seven representative states appear at 1000×700, 1280×720 and 1920×1080 DIP. Minimum-size supplemental states cover correction preparation/re-review, save conflict/uncertainty, abandon confirmation, failure/error details and blocked/stale/unchecked readiness.

This is synthetic offscreen WPF UserControl rendering at 96 DPI. Fake TIFF image dimensions/production metadata are not evidence of production correctness; the known F6 discrepancy is not fixed here. Disabled folder-launch controls reflect the fixture's missing OS-launch adapter. Readiness reports are synthetic and do not describe this workstation. Small windows retain scrolling for secondary details while primary actions and current save destination remain visible. Long temporary paths wrap; no images were edited after capture.

Real keyboard, UIA device interaction, DPI scaling, Meitu/Photoshop, workstation readiness, Chinese novice walkthrough and production output: **NOT RUN**.

| Image | Viewport DIP | Measurements | Inspection |
| --- | --- | --- | --- |
| [completed](screenshots/completed-1000x700.png) | 1000 × 700 | [geometry](screenshots/completed-1000x700.json) | inspected |
| [completed](screenshots/completed-1280x720.png) | 1280 × 720 | [geometry](screenshots/completed-1280x720.json) | inspected |
| [completed](screenshots/completed-1920x1080.png) | 1920 × 1080 | [geometry](screenshots/completed-1920x1080.json) | inspected |
| [confirmation](screenshots/confirmation-1000x700.png) | 1000 × 700 | [geometry](screenshots/confirmation-1000x700.json) | inspected |
| [correction-prepare](screenshots/correction-prepare-1000x700.png) | 1000 × 700 | [geometry](screenshots/correction-prepare-1000x700.json) | inspected |
| [correction-returned](screenshots/correction-returned-1000x700.png) | 1000 × 700 | [geometry](screenshots/correction-returned-1000x700.json) | inspected |
| [correction-waiting](screenshots/correction-waiting-1000x700.png) | 1000 × 700 | [geometry](screenshots/correction-waiting-1000x700.json) | inspected |
| [correction-waiting](screenshots/correction-waiting-1280x720.png) | 1280 × 720 | [geometry](screenshots/correction-waiting-1280x720.json) | inspected |
| [correction-waiting](screenshots/correction-waiting-1920x1080.png) | 1920 × 1080 | [geometry](screenshots/correction-waiting-1920x1080.json) | inspected |
| [error-details](screenshots/error-details-1000x700.png) | 1000 × 700 | [geometry](screenshots/error-details-1000x700.json) | inspected |
| [failure](screenshots/failure-1000x700.png) | 1000 × 700 | [geometry](screenshots/failure-1000x700.json) | inspected |
| [readiness](screenshots/readiness-1000x700.png) | 1000 × 700 | [geometry](screenshots/readiness-1000x700.json) | inspected |
| [readiness](screenshots/readiness-1280x720.png) | 1280 × 720 | [geometry](screenshots/readiness-1280x720.json) | inspected |
| [readiness](screenshots/readiness-1920x1080.png) | 1920 × 1080 | [geometry](screenshots/readiness-1920x1080.json) | inspected |
| [readiness-blocked](screenshots/readiness-blocked-1000x700.png) | 1000 × 700 | [geometry](screenshots/readiness-blocked-1000x700.json) | inspected |
| [readiness-stale](screenshots/readiness-stale-1000x700.png) | 1000 × 700 | [geometry](screenshots/readiness-stale-1000x700.json) | inspected |
| [readiness-unchecked](screenshots/readiness-unchecked-1000x700.png) | 1000 × 700 | [geometry](screenshots/readiness-unchecked-1000x700.json) | inspected |
| [recent](screenshots/recent-1000x700.png) | 1000 × 700 | [geometry](screenshots/recent-1000x700.json) | inspected |
| [recent](screenshots/recent-1280x720.png) | 1280 × 720 | [geometry](screenshots/recent-1280x720.json) | inspected |
| [recent](screenshots/recent-1920x1080.png) | 1920 × 1080 | [geometry](screenshots/recent-1920x1080.json) | inspected |
| [recovery](screenshots/recovery-1000x700.png) | 1000 × 700 | [geometry](screenshots/recovery-1000x700.json) | inspected |
| [recovery](screenshots/recovery-1280x720.png) | 1280 × 720 | [geometry](screenshots/recovery-1280x720.json) | inspected |
| [recovery](screenshots/recovery-1920x1080.png) | 1920 × 1080 | [geometry](screenshots/recovery-1920x1080.json) | inspected |
| [save](screenshots/save-1000x700.png) | 1000 × 700 | [geometry](screenshots/save-1000x700.json) | inspected |
| [save](screenshots/save-1280x720.png) | 1280 × 720 | [geometry](screenshots/save-1280x720.json) | inspected |
| [save](screenshots/save-1920x1080.png) | 1920 × 1080 | [geometry](screenshots/save-1920x1080.json) | inspected |
| [save-collision](screenshots/save-collision-1000x700.png) | 1000 × 700 | [geometry](screenshots/save-collision-1000x700.json) | inspected |
| [save-uncertain](screenshots/save-uncertain-1000x700.png) | 1000 × 700 | [geometry](screenshots/save-uncertain-1000x700.json) | inspected |
| [settings](screenshots/settings-1000x700.png) | 1000 × 700 | [geometry](screenshots/settings-1000x700.json) | inspected |
| [settings](screenshots/settings-1280x720.png) | 1280 × 720 | [geometry](screenshots/settings-1280x720.json) | inspected |
| [settings](screenshots/settings-1920x1080.png) | 1920 × 1080 | [geometry](screenshots/settings-1920x1080.json) | inspected |

Behavior, AC mapping and test comparison: [RESULTS](../../../docs/codex/printflow-operator-ux-backlog/SCRUM-11172_RESULTS.md). Decisions: [RULINGS](RULINGS.md). Review entry: [REVIEW_PACKAGE](REVIEW_PACKAGE.md).

