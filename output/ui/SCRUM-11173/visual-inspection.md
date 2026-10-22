# Q01 visual inspection

All 17 final PNGs were opened and inspected individually after the final 45/45 run. This is visual inspection of synthetic offscreen WPF at 96 DPI, not human acceptance or physical-device evidence. The approved 1000×700 DIP target means the outer WPF window; its smaller actual client area has not been measured.

| Image | UserControl viewport DIP | Inspection | Finding |
|---|---|---|---|
| [correction-waiting-1000x700](visuals/correction-waiting-1000x700.png) | 1000 × 700 | Inspected | Waiting state and next action visible; details scroll normally. |
| [correction-waiting-1280x720](visuals/correction-waiting-1280x720.png) | 1280 × 720 | Inspected | Waiting state and next action visible; details scroll normally. |
| [correction-waiting-1920x1080](visuals/correction-waiting-1920x1080.png) | 1920 × 1080 | Inspected | Waiting state and next action visible; details scroll normally. |
| [crop-1000x700](visuals/crop-1000x700.png) | 1000 × 700 | Inspected | Image, crop handles, tools and main actions visible. |
| [crop-1280x720](visuals/crop-1280x720.png) | 1280 × 720 | Inspected | Image, crop handles, tools and main actions visible. |
| [crop-1920x1080](visuals/crop-1920x1080.png) | 1920 × 1080 | Inspected | Image, crop handles, tools and main actions visible. |
| [long-chinese-source-1000x700](visuals/long-chinese-source-1000x700.png) | 1000 × 700 | Inspected | Chinese source heading, image and main actions visible. |
| [long-chinese-source-1280x720](visuals/long-chinese-source-1280x720.png) | 1280 × 720 | Inspected | Chinese source heading, image and main actions visible. |
| [long-chinese-source-1920x1080](visuals/long-chinese-source-1920x1080.png) | 1920 × 1080 | Inspected | Chinese source heading, image and main actions visible. |
| [multisize-completed-1000x700](visuals/multisize-completed-1000x700.png) | 1000 × 700 | Inspected | Image and fixed main actions visible; both output names reachable through details scrolling. |
| [multisize-completed-1280x720](visuals/multisize-completed-1280x720.png) | 1280 × 720 | Inspected | Image and fixed main actions visible; both output names reachable through details scrolling. |
| [multisize-completed-1920x1080](visuals/multisize-completed-1920x1080.png) | 1920 × 1080 | Inspected | Image and fixed main actions visible; both output names reachable through details scrolling. |
| [multisize-long-name-1000x700](visuals/multisize-long-name-1000x700.png) | 1000 × 700 | Inspected | Image and fixed main actions visible; both output names reachable through details scrolling. |
| [multisize-long-name-1280x720](visuals/multisize-long-name-1280x720.png) | 1280 × 720 | Inspected | Image and fixed main actions visible; both output names reachable through details scrolling. |
| [multisize-long-name-1920x1080](visuals/multisize-long-name-1920x1080.png) | 1920 × 1080 | Inspected | Image and fixed main actions visible; both output names reachable through details scrolling. |
| [readiness-blocked-1000x700](visuals/readiness-blocked-1000x700.png) | 1000 × 700 | Inspected | Readiness gate visibly blocks progress and explains required action. |
| [save-uncertain-1000x700](visuals/save-uncertain-1000x700.png) | 1000 × 700 | Inspected | Uncertain save state does not claim completion; recovery action visible. |

The initial 14 images are retained separately in `visuals-before-fix`. Initial 9/9 characterization and 15/15 visual checks did not catch the multi-size image collapse. Actual RED was 2/3 failing after adding the missing image-height assertion; final targeted evidence is 45/45.
History is deliberately inside the existing details scroll on Save/Completed. At 1000 and 1280 the history can be below the fold; tests verify both saved names after scrolling. At 1920 the Save history is visible and Completed history can be partly below the fold. No image is scaled to impersonate 125/150% DPI.
