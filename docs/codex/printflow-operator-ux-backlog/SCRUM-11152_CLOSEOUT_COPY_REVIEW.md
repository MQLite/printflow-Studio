# SCRUM-11152 closeout copy review

Task `PF-OPUX-v1-SCRUM-11152-signal-closeout-v1`. Exact final resource values below. Owner directions implemented; final human copy acceptance remains OPEN. Original copy table is preserved. Shared readiness labels and cleanup warning remain unchanged on other screens.

| Key / condition | Previous English | Final English | Previous Chinese | Final Chinese |
|---|---|---|---|---|
| `Home_ReadinessNotChecked` | Not checked yet since PrintFlow started. | Not checked in this run. | 自 PrintFlow 本次启动以来尚未检查。 | 本次启动后尚未检查。 |
| `Home_ReadinessChecking` | A workstation check is running. There is no result yet. | Checking the workstation… | 工作站检查正在进行，尚无结果。 | 正在检查工作站…… |
| `Home_ReadinessReady` | Ready to process when last checked. | Passed at last check. | 上次检查时可以进行处理。 | 上次检查通过。 |
| `Home_ReadinessBlocked` | Automatic processing is blocked. | Automatic processing is blocked. | 自动处理已被阻止。 | 暂时不能自动处理。 |
| `Home_ReadinessNotConfirmed` | Readiness is not confirmed. | Readiness needs to be checked again. | 就绪状态未确认。 | 需要重新检查工作站。 |
| `Home_ReadinessNotCheckedHint` | Open Production readiness to check this workstation. A result from an earlier run is not used. | Choose “View workstation checks” to check this workstation. A result from an earlier run is not used. | 请打开“生产就绪状态”检查本工作站。之前运行的检查结果不会沿用。 | 请选择“查看工作站检查”检查本工作站。之前运行的检查结果不会沿用。 |
| `Home_ReadinessCheckingHint` | Open Production readiness to see the check. | Choose “View workstation checks” to see the check. | 打开“生产就绪状态”查看该检查。 | 请选择“查看工作站检查”查看该检查。 |
| `Home_ReadinessReadyHint` | This is what that check found. Each automatic step checks the workstation again before it runs, and those checks do not update Home. If a step is refused, open Production readiness. | This is a recorded observation. Each automatic step still uses its own workstation check. | 这是该次检查的结果。每个自动步骤开始前都会再次检查工作站，但这些检查不会更新主页。如果某个步骤被拒绝，请打开“生产就绪状态”。 | 这是已记录的检查结果。每个自动步骤仍会执行自己的工作站检查。 |
| `Home_ReadinessFirstReason` | First reason: {0}. {1} | Reason: {0}. {1} | 首要原因：{0}。{1} | 原因：{0}。{1} |
| `Home_ReadinessLiveCheckPending` | First reason: the Meitu and Photoshop check has no current result. Open Production readiness and choose {0}. | Reason: the Meitu and Photoshop check has no current result. Choose “View workstation checks”, then “{0}”. | 首要原因：美图秀秀和 Photoshop 的检查目前没有有效结果。请打开“生产就绪状态”并选择“{0}”。 | 原因：美图秀秀和 Photoshop 的检查目前没有有效结果。请选择“查看工作站检查”，然后选择“{0}”。 |
| `Home_ReadinessBlockedHint` | Open Production readiness to see every check and what to do. If you cannot fix this, ask your supervisor. | Choose “View workstation checks” to see every check. Ask an experienced colleague or supervisor for help. | 打开“生产就绪状态”查看全部检查项及处理方法。如果无法自行解决，请联系主管。 | 请选择“查看工作站检查”查看全部检查项。请熟练同事或主管帮忙。 |
| `Home_ReadinessNoReason` | The last check did not name a reason. Production readiness shows the details. | The last check did not name a reason. Choose “View workstation checks” for details. | 上一次检查未给出原因。详情请查看“生产就绪状态”。 | 上一次检查未给出原因。请选择“查看工作站检查”查看详情。 |
| `Home_ReadinessNotConfirmedHint` | Open Production readiness and check again. | Choose “View workstation checks” to check again. Ask an experienced colleague or supervisor for help. | 请打开“生产就绪状态”并重新检查。 | 请选择“查看工作站检查”重新检查。请熟练同事或主管帮忙。 |
| `Home_ReadinessTechnicalCheck` | Check: {0} | Check identifier: {0} · {1} | 检查项：{0} | 检查标识：{0} · {1} |
| `Home_StartupPresetVerified` | Production setup file (preset): verified at startup. This alone does not mean the workstation is ready. | The production setup file was verified at startup. This does not confirm that the workstation is ready now. | 生产设置文件（预设）：启动时已通过校验。仅凭这一点并不表示工作站已可以处理。 | 生产设置文件在启动时已通过校验；这不代表工作站现在可以处理。 |
| `Home_StartupPresetNotVerified` | Production setup file (preset): not verified at startup. Production readiness shows why. | Startup could not verify the production setup file. Choose “View workstation checks” for details. | 生产设置文件（预设）：启动时未通过校验。原因请查看“生产就绪状态”。 | 启动时未能校验生产设置文件。请选择“查看工作站检查”查看详情。 |
| `Home_ViewWorkstationChecks` | (new Home-only resource) | View workstation checks | (新增主页专用资源) | 查看工作站检查 |
| `Home_DiagnosticRetentionWarning` | (new Home-only resource) | Automatic cleanup of diagnostic records did not finish; it will be retried at the next startup. This notice does not mean the workstation passed its checks. | (新增主页专用资源) | 诊断记录清理未完成，下次启动时会重试。此提示不表示工作站已通过检查。 |

Unchanged supporting resources: `Home_ReadinessUnfinishedReason` explains the unfinished result without inventing a fault; `Home_ReadinessCheckedAt` formats the report’s real local date/time. Reason uses the existing localized check name and authoritative explanation in order. Technical details retain the exact CheckKey and localized report status (Blocked means not run for a live row).

Home navigation uses its existing command; “View workstation checks” opens the existing readiness page, whose heading and explicit “Safe recovery and recheck” button are unchanged. Opening Home does not run a check.

The recorded result receives gate and diagnostic-package observations now; no continuous monitoring is promised. Startup preset validation never counts as current readiness. The Home-only cleanup notice remains visible alongside blocked/unconfirmed states.
