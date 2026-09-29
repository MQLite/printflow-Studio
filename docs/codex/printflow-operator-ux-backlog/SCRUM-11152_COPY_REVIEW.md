# SCRUM-11152 — Home readiness wording for the owner's bilingual copy review

Task `PF-OPUX-v1-SCRUM-11152-impl-v1`. This page holds the **final shipped wording** of Home's readiness summary and startup details, in English and 简体中文. The table is generated from the committed `Strings.resx` / `Strings.zh-CN.resx`. The review is **open**: nothing here has been accepted or waived.

Why each state is shown, which report field it comes from and when it is withdrawn is in the [source-to-display matrix](SCRUM-11152_PLAN.md#source-to-display-matrix). It is not repeated here.

## How to read it

- **One summary, five states.** A small caption ("Workstation check / 工作站检查") sits above one status line. Below that come the first reason and the check time (only where true), a hint, and the one button "Production readiness / 生产就绪状态". The status is always words; colour carries no meaning.
- **Ready is a past result, not a permission.** It shows only when the latest recorded reading of *this run* reported Verified, and it says "when last checked". The gate checks the workstation again before every automatic step, but those checks do not reach Home, and the hint says so.
- **The time is the reading's own time** (`ObservedAt`), in local time with the date. It is labelled "Checked at / 检查时间", exactly as the readiness screen labels the same field. It is never the time Home was opened or refreshed, and never the last successful live-probe time.
- **The reason is the readiness screen's own wording** for the report's first blocking check: its name and its explanation. There is one exception. When that check is a live check that simply has not run, Home says the Meitu and Photoshop check has no current result and names the readiness screen's "Safe recovery and recheck" button. The readiness screen's own sentence for that row ("a prerequisite has not passed") would be untrue in that position.
- **Nothing on Home runs a check.** The button opens Production readiness. That screen takes its usual passive reading when it opens; the live check still needs its own button there.
- **Startup details are collapsed; warnings are not.** Recovery counts, pending recovery and the preset line sit under "Startup details / 启动详情". Three warnings stay visible: a preset that was not verified, startup recovery that did not run, and the diagnostic retention warning. The Recovery list and its buttons are unchanged.

## Examples (synthetic)

Failed desktop-session check first in the report:
- English: "First reason: Desktop session. A supported interactive desktop session is not available."
- 简体中文: "首要原因：桌面会话。当前没有可用的受支持交互式桌面会话。"

Usual first reading of a new run (automatic checks passed, live check not run yet):
- English: "First reason: the Meitu and Photoshop check has no current result. Open Production readiness and choose Safe recovery and recheck."
- 简体中文: "首要原因：美图秀秀和 Photoshop 的检查目前没有有效结果。请打开“生产就绪状态”并选择“安全恢复并重新检查”。"

## Wording by key

| Key | Where / when | English | 简体中文 |
|---|---|---|---|
| `Home_ReadinessHeading` | Caption over the summary, every state | Workstation check | 工作站检查 |
| `Home_ReadinessNotChecked` | Status: nothing observed since this run started | Not checked yet since PrintFlow started. | 自 PrintFlow 本次启动以来尚未检查。 |
| `Home_ReadinessNotCheckedHint` | Hint under Not checked yet | Open Production readiness to check this workstation. A result from an earlier run is not used. | 请打开“生产就绪状态”检查本工作站。之前运行的检查结果不会沿用。 |
| `Home_ReadinessChecking` | Status: a reading or the explicit live check has started and has no result | A workstation check is running. There is no result yet. | 工作站检查正在进行，尚无结果。 |
| `Home_ReadinessCheckingHint` | Hint under Checking | Open Production readiness to see the check. | 打开“生产就绪状态”查看该检查。 |
| `Home_ReadinessReady` | Status: latest recorded report Verified (worded as past: gate checks before each step do not update Home) | Ready to process when last checked. | 上次检查时可以进行处理。 |
| `Home_ReadinessReadyHint` | Hint under Ready | This is what that check found. Each automatic step checks the workstation again before it runs, and those checks do not update Home. If a step is refused, open Production readiness. | 这是该次检查的结果。每个自动步骤开始前都会再次检查工作站，但这些检查不会更新主页。如果某个步骤被拒绝，请打开“生产就绪状态”。 |
| `Home_ReadinessBlocked` | Status: latest report not Verified, names a blocking check | Automatic processing is blocked. | 自动处理已被阻止。 |
| `Home_ReadinessFirstReason` | Reason under Blocked. {0} = check name, {1} = the readiness screen's explanation for that check | First reason: {0}. {1} | 首要原因：{0}。{1} |
| `Home_ReadinessLiveCheckPending` | Reason under Blocked when the first blocking check is a live check that did not run (usual first reading of a new run). {0} = the readiness screen's own button label (Environment_RunLiveChecks) | First reason: the Meitu and Photoshop check has no current result. Open Production readiness and choose {0}. | 首要原因：美图秀秀和 Photoshop 的检查目前没有有效结果。请打开“生产就绪状态”并选择“{0}”。 |
| `Home_ReadinessBlockedHint` | Hint under Blocked | Open Production readiness to see every check and what to do. If you cannot fix this, ask your supervisor. | 打开“生产就绪状态”查看全部检查项及处理方法。如果无法自行解决，请联系主管。 |
| `Home_ReadinessNotConfirmed` | Status: latest reading did not finish, or its report names no blocking check | Readiness is not confirmed. | 就绪状态未确认。 |
| `Home_ReadinessUnfinishedReason` | Reason under Not confirmed when the reading threw or the live check was cancelled | The last check did not finish, so no earlier result is shown as current. | 上一次检查未完成，因此不会把更早的结果显示为当前结果。 |
| `Home_ReadinessNoReason` | Reason under Not confirmed when the report is not Verified but names no blocking check | The last check did not name a reason. Production readiness shows the details. | 上一次检查未给出原因。详情请查看“生产就绪状态”。 |
| `Home_ReadinessNotConfirmedHint` | Hint under Not confirmed | Open Production readiness and check again. | 请打开“生产就绪状态”并重新检查。 |
| `Home_ReadinessCheckedAt` | Time line under Ready, Blocked and Not confirmed (no reason). {0} = the report's ObservedAt in local time with date | Checked at {0} | 检查时间：{0} |
| `Home_ReadinessTechnicalCheck` | Inside the collapsed Technical details under Blocked. {0} = stable check identifier | Check: {0} | 检查项：{0} |
| `Home_StartupDetails` | Header of the collapsed startup expander | Startup details | 启动详情 |
| `Home_StartupPresetVerified` | Inside Startup details when startup verified the preset | Production setup file (preset): verified at startup. This alone does not mean the workstation is ready. | 生产设置文件（预设）：启动时已通过校验。仅凭这一点并不表示工作站已可以处理。 |
| `Home_StartupPresetNotVerified` | Visible warning and inside Startup details when startup did not verify the preset | Production setup file (preset): not verified at startup. Production readiness shows why. | 生产设置文件（预设）：启动时未通过校验。原因请查看“生产就绪状态”。 |
| `Environment_Open` | Button label (existing, unchanged) | Production readiness | 生产就绪状态 |
| `Environment_RunLiveChecks` | Filled into Home_ReadinessLiveCheckPending (existing readiness button, unchanged) | Safe recovery and recheck | 安全恢复并重新检查 |
| `Environment_TechnicalDetails` | Technical details header (existing, unchanged) | Technical details | 技术详情 |
| `Startup_RecoverySummary` | Inside Startup details (existing counts line, unchanged) | Startup recovery: {0} interrupted attempt(s), {1} stale lock(s) released, {2} file(s) quarantined. | 启动恢复：中断的处理 {0} 个，释放过期锁 {1} 个，隔离文件 {2} 个。 |
| `Startup_RecoveryClean` | Inside Startup details when recovery found nothing (existing) | Startup recovery found nothing to recover. | 启动恢复未发现需要恢复的内容。 |
| `Startup_RecoveryNotRun` | Visible warning when startup recovery did not run (existing) | Startup recovery did not run. | 未执行启动恢复。 |
| `Home_RecoveryPending` | Inside Startup details when jobs wait for a recovery decision (existing) | Interrupted jobs needing a recovery decision: {0}. | {0} 个中断的任务需要选择恢复操作。 |
| `Startup_DiagnosticRetentionWarning` | Visible warning (existing, now on its own line) | Automatic cleanup of local diagnostic records and screenshots could not finish safely. You can continue working; cleanup will be tried again at the next startup. | 本机诊断记录和截图的自动清理未能安全完成。您可以继续工作；下次启动时将重试清理。 |

The earlier "Preset verified / 预设已校验" and "Preset not verified / 预设未校验" lines no longer appear on Home; their keys remain in the resource files.

## Items for the owner

1. **State names.** Accept or reword:
   - "Ready to process when last checked / 上次检查时可以进行处理"
   - "Automatic processing is blocked / 自动处理已被阻止"
   - "Readiness is not confirmed / 就绪状态未确认"
   - "Not checked yet since PrintFlow started / 自 PrintFlow 本次启动以来尚未检查"
2. **Supervisor line** in the Blocked hint ("If you cannot fix this, ask your supervisor / 如果无法自行解决，请联系主管"): keep, or name a different contact.
3. **Button label.** It stays the existing "Production readiness / 生产就绪状态" rather than the Jira proposal "Check the workstation", because opening it does not by itself run the live check. Confirm, or approve a relabel.
4. **Reason format** "First reason: {name}. {explanation}". The explanations are the existing readiness-screen sentences, and some are long. Accept the reuse, or ask for shorter Home-only variants.
5. **Button name inside a sentence.** In English, "choose Safe recovery and recheck" is unquoted, following the existing English convention; zh-CN uses “”. Decide whether English should quote button names. This is the same open question as the 11151 nonblocking note.
6. **Technical details** in the "no current result" case shows `Check: ExternalApplicationAutomationLock`. That is the report's first live row, but it is not a lock fault. Accept, or ask for a different identifier there.
7. **Preset line** wording, both in Startup details and as a warning.
8. **Known limit (a decision, not copy).** Home does not see the gate's own check before each production step, nor support-package readings. After a refused step it can still show an earlier "Ready to process when last checked" until the next readiness or Settings reading; see the plan. One option, not implemented here: switch Home to "not confirmed" whenever a job step reports "workstation not verified". That code is shared by many other adapter checks, so it would sometimes be over-cautious, but it could never show a false Ready.

No other business decision is needed. The Jira "human check" remains open: both languages at the workstation's own display scaling, and real keyboard traversal.
