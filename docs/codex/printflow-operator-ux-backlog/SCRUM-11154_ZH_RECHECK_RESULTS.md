# SCRUM-11154 — Chinese targeted workstation recheck

**Result: CHINESE_TARGETED_RECHECK_RECORDED — OWNER_DISPOSITION_REQUIRED** (2026-10-01T21:55Z – 2026-10-02T04:36Z). One visible synthetic session, zh-CN only, owner present and performing every application input. Structured record: [SCRUM-11154_ZH_RECHECK_LEDGER.json](SCRUM-11154_ZH_RECHECK_LEDGER.json). Checklist: [SCRUM-11154_ZH_RETEST_CHECKLIST.md](SCRUM-11154_ZH_RETEST_CHECKLIST.md) (unchanged). English: **NOT RUN — NOT REQUIRED BY OWNER** (“不需要安排英文测试”, [scope decision](SCRUM-11154_ACCEPTANCE_SCOPE.md)).

This was verification only. No product, host, test, launcher or configuration code changed. New defects are recorded, not fixed.

## Candidate and preparation

| | |
|---|---|
| Source | `20cae9f` (docs-only after remediation code `f720899`); of 808 manifest entries compared with the remediation candidate `2AD0F5E9…C571`, 0 source files differ and 13 freshly compiled host outputs differ |
| Candidate / scenario | `D3FEAB1B…ADCD3` / `910A32A3…86F0` |
| Host / test assembly | `CF0FC64C…A255` / `4D2378E9…FFF0` |
| Checks | isolated build 0 warnings/errors; entry static 65/65; ownership prerequisite 7/7 (fresh runs on this build; earlier 736/476/… totals remain history) |
| Validate / PrepareAndSmoke | exit 0 / exit 0 (21:58:56–21:59:35Z), settled, no window |
| Binding | `prepared.json` tokens match ownership and manifests; `ScenarioLedgerSha256` `F840E1A4…E4EA` equals the exact local `evidence/scenario-ledger.json` bytes; root, lock and six database identities match |
| Root | new direct child `runs/q-20261001T215519Z`; no Runtime7, OwnedRestart, separate Resume, injected fault or link test on it |

The candidate generator needed two retries: under Windows PowerShell 5.1 it lacks `PEReader`; under PowerShell 7 the PATH `dotnet` had no SDK 10.0.400. Both attempts wrote nothing and their logs are kept. The launcher ran under PowerShell 7 with the per-user SDK first on the process PATH.

## Session

- Owner acknowledgment bound to run and candidate at 22:18:02Z; Interactive 22:18:22Z – 04:36:15Z; **launcher exit 0**; quiescence settled (0 pending, no late registration, no host fault); session record `ClosedNormally`, 12 fixtures admitted, 4 native dispatches, 1 picker refusal. The owner closed the window after confirming no dialog was open. The root is consumed.
- Display (read-only): 1920×1080, work area 1920×1040 (read after the window closed), 96 DPI (UIA during the run); no display setting changed. The window was 1000×700 at launch and at the first A8 capture, then 1000×1047 from 22:58Z onward; a later Win+Down did not restore it. **Only A8 was seen at 1000×700.**
- Input: owner keyboard/mouse/dialogs and Alt+PrtScn only. The agent used read-only UIA snapshots, a focus trace, a list-item watcher, hashes and clipboard-to-PNG saving. Prepared fixture approvals are setup facts, not human approvals.
- Consent: the synthetic window was often out of the foreground while other applications were in use. There is no fixed absence threshold; the agent judged when to ask again. All four renewals preceded later consequential steps (A2; A4 abandon and A6; the A12 delivery choice and A10 save; B8). The 00:56:56Z and 02:33:09Z answers arrived after the steps they were first asked for (A3; the B1 export run and the A12 folder). **Six owner actions followed an absence without a prior renewal**: A8 Tab traversal (22:57Z, non-consequential); all of A3 (00:48–00:50Z, no state change); the B1 export run (02:29Z); the A12 empty folder (02:32Z); A10 完成 (02:58Z, no renewal asked); the B5 re-run (04:11Z). For A3, A12 and B1/B5 the renewal question and the steps were sent together or the renewal came later. These actions stayed inside the synthetic root under the run-bound acknowledgment; they are disclosed, not retroactively authorized.
- The owner stopped the session after B8: “Stop right now. Write the reports. Process next time.” B2 and B7's remaining parts were not started.

## Results

| ID | Result | Observed (zh-CN) |
|---|---|---|
| A1 F-V3 | PARTIAL | Pixel-checked Before (opaque) left / After (transparent top band) right at ~25% and ~71% (captures); 100% all Before (capture); 0% all After, ~75% and 125% zoom owner-reported; mode round trip unchanged. Window resize sub-step not achieved (two capture names wrongly say 50%/resized; annotated). The owner first asked whether the sides were reversed, then confirmed “左前右后没问题”. |
| A2 F-V4 | PASS | Inline 放弃“keep-extent”？ with exact name and consequence; focus on 保留任务; other rows inert. |
| A3 F-V4 | PASS | Esc, 保留任务, held Enter, double-click: not abandoned; focus returns to the row's 放弃. (Consent: all A3 actions preceded the renewal.) |
| A4 F-V4 | PASS | Tab to 确认放弃 + one fresh Enter abandoned only keep-extent; same-name pair and other rows unchanged. |
| A5 F-V4 | BLOCKED | No Recovery card exists on this root (no interrupted recovery candidate); nothing seeded. |
| A6 F-V4 | PASS | 从列表移除 one step, no confirmation; all session file counts and bytes unchanged. |
| A7 F-V5 | PARTIAL | At 1000×1047 the status, location and 有效 are fully visible; tooltip/accessible name give the full file name. Not checked at 1000×700. F6 visible again. |
| A8 F-V2/F-V6 | PASS | At 1000×700: 未列出的检查项, 本项检查未运行，没有观察结果。, heading “尚未确认：有必需检查未运行”; identifier `SyntheticEntry`; lists named and not Tab stops. |
| A9 F-V6 | PASS | Reject reason, note, return selector and all their items carry Chinese names; TIFF 生产信息 rows named by label (read-only UIA). |
| A10 F-V8 | PASS | 保存已审核结果 wrote the exact reviewed bytes; Home showed 需要你操作 · 完成任务 plus a separate saved-history line; after 完成, 已完成 with history kept. |
| A11 F-V9 | PASS | 美图未能启动或未能就绪。 with `MeituLaunchFailed`; fake scripted failure, no live Meitu. |
| A12 F-V7 | PASS | New folder refused with SYNTHETIC notice, host kept running; Cancel no error; prepared `delivery` and `diagnostics\export` folders accepted. |
| B1 | PARTIAL | Positive Space control; held Space on 执行步骤 and on 通过 activated only their own target once; successor review/step not activated; fresh Enter then worked. WPF buttons activate Space on release, so “residual release across a new exact target” could not occur as worded and is NOT RUN. |
| B2 | NOT RUN | Owner stopped. |
| B3 | PASS | Presets and custom modes, 0 mm refused, 150 mm projection, enlargement warning, Run gated until 仍按此尺寸处理. |
| B4 | **FAIL** | 确认结果并保存 is distinct and present; the screen already showed 当前文件 PNG 600 x 400 beside a “valid” 1772 × 1181 TIFF row; activation recorded approval then refused the save: 此结果不能保存：其审核记录与文件不一致。 (N4). TIFF Complete, previous size and uncertain retry NOT RUN. |
| B5 | PARTIAL | Refresh keeps focus; TIFF reject moves the file to the entry's simulated recycle folder and requires Run; regeneration adds a separate row (its file again PNG bytes, N4). Language-switch focus NOT RUN — NOT REQUIRED BY OWNER. Windows Recycle Bin not exercised. |
| B6 | PARTIAL | Only the not-run readiness state and no Recovery list are reachable; Recent scrolled to the bottom captured. |
| B7 | PARTIAL | Earlier 152 mm output kept as its own row (已通过 · 已在 Approved · 已失效); no earlier delivery existed to preserve; Ctrl nudge/snap and note-draft refresh NOT RUN. |
| B8 | PARTIAL | Handed-off view displayed with its actions, none activated (owner: works fine); continue path, combined notices and running rows NOT RUN. |
| B9 | PARTIAL | Capture manifest records time, size, hash and state; viewport from UIA window rect; scroll positions not recorded except B6; wrong notes corrected in a private annotation file. |
| F6 | KNOWN NEGATIVE | Row 200 × 150 毫米 / 2362 × 1772 像素 versus actual 6 × 5 像素 (0.5 × 0.4 毫米). Not fixed. |

## New findings (not fixed)

- **N1 (P2, AC1)** — Recent shows `状态暂不可用 · 高清化` for the active job whose current step is Interrupted after StopOperation (observed). `RecentSessionRow.Status` maps Failed/RetryRequired to stopped but not Interrupted, whereas `SessionViewModel.OperatorStatus` groups Interrupted with Failed (source only; that Session screen was not opened).
- **N2 (P3, AC2)** — Diagnostic package preview items expose `DiagnosticPackagePreviewItem { Text = … }` as accessible names (F-V6 class on a surface F-V6 did not cover).
- **N3 (P3, AC1/AC3)** — Output row prints `150 × 99.99133333333333 毫米`.
- **N4 (P1 pending diagnosis, AC1)** — In Interactive, the Fake Photoshop step wrote the input PNG bytes (`89504E47`, same SHA-256 as `F3-enlarge.png`) under a `.tif` name, and did so again on the B5 re-run. The review screen showed 当前文件 PNG 600 x 400 next to an output row claiming a valid 1772 × 1181, 300 dpi TIFF and 已验证 CMYK + W1. 确认结果并保存 recorded approval and promoted the file, then refused delivery; afterwards the row still read 有效 and the header asked for 完成. Fail-closed and contained (no delivery write). Whether the fault is the test entry's fake processor or missing validation before review is undetermined.
- Observations for owner disposition: owner-reported no visible focus cue after mouse-opening the confirmation (the A2 capture shows a dotted cue, possibly after the Alt key); held Enter toggles the confirmation open/closed; focus to window root after abandon; “这是上次成功保存使用的文件夹” after an explicit choice; enlarge heading names the white-ink choice while enlargement acceptance blocks; owner-reported that 导出诊断包 opens no dialog (it opens an in-window preview; the dialog comes from 保存诊断包), relevant to the open diagnostic-navigation item; 修改尺寸 flips the earlier output from 有效 to 已失效; a rejected, recycled output still says 有效; three buttons lack AutomationIds.

## Changed-path dispositions and remaining items

| Item | Disposition after this recheck |
|---|---|
| F-V2, F-V4, F-V6, F-V7, F-V8, F-V9 | Chinese changed paths observed passing on this candidate (A5 Recovery card blocked by fixture) |
| F-V3 | Orientation passes; resize sub-step not observed |
| F-V5 | Passes at 1000×1047; not checked at 1000×700 |
| N1–N4 and observations | Owner disposition / separate authorization for diagnosis or repair |
| F6, PNG approval gap, legacy binding, generic recovery, diagnostic navigation | Unchanged, open |
| B2, B7 remainder, B4 Complete/retry, B6/B8 unreachable states | NOT RUN or BLOCKED |
| AC1 | OPEN (N1, N4; English half NOT RUN — NOT REQUIRED BY OWNER) |
| AC2 | PARTIAL (residual release across a new target and keyboard-only whole journey not run) |
| AC3 | OPEN (only A8 at 1000×700; N3) |
| AC4 | RECORDED |
| Owner Chinese copy acceptance, novice validation, print quality | NOT RUN — the owner's “works fine” and A1 remark are not copy acceptance |

Window captures and raw UIA/trace logs stay out of Git; the window-only captures travel only in the local owner packet. Earlier historical results remain tied to their own candidates and roots. Not Done; SCRUM-11155 not started.
