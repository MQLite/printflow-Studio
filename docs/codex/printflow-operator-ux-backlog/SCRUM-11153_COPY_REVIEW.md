# SCRUM-11153 final copy review

Task `PF-OPUX-v1-SCRUM-11153-impl-v1`. These are the exact resource values and display conditions. They reuse the established session panel status words. Owner copy acceptance remains OPEN.

| Condition / resource | English | 简体中文 |
|---|---|---|
| Current step ReviewRequired / `Session_StatusReview` | Waiting for your review · {step} | 等待你检查 · {步骤} |
| Current step Failed or RetryRequired / `Session_StatusStopped` | Stopped or failed · {step} | 已停止或失败 · {步骤} |
| Current step Waiting, Approved or Skipped / `Session_StatusInput` | Needs your input · {step} | 需要你操作 · {步骤} |
| Current step Processing with a running current attempt / `Session_StatusProcessing` | Processing automatically · {step} | 正在自动处理 · {步骤} |
| Active fact missing or unsupported / `Home_RecentStatusUnknown` | Status unavailable (with step when known) | 状态暂不可用（已知时附步骤） |
| Session HandedOff / `Session_StatusHandedOff` | Handed off | 已移交 |
| Session Completed / `Session_StatusCompleted` | Completed | 已完成 |
| Session Abandoned / `SessionState_Abandoned` | Abandoned | 已放弃 |
| Exact approved PNG has a Delivered record / `Home_RecentPngSavedPreviously` | Saved previously (approved PNG); check job details | 已批准的 PNG 曾保存过；请在任务详情中检查 |
| N exact approved TIFF outputs have Delivered records / `Home_RecentTiffSavedPreviously` | {0} approved TIFF size(s) saved previously; check job details | {0} 个已批准的 TIFF 尺寸曾保存过；请在任务详情中检查 |
| Delivery-history query unavailable / `Home_RecentSaveHistoryUnavailable` | Save history unavailable; check job details | 保存记录暂不可用；请在任务详情中检查 |
| Confirmed zero matching records | No save-history line | 不显示保存记录行 |

The separate history line means a prior database delivery record for the exact still-eligible approved result. It does not say the destination file currently exists, is readable, or has been verified. For TIFF, `{0}` counts individual output IDs/sizes with matching records; a saved sibling never labels a new pending size as saved. Primary review/stopped/handoff status remains visible when new work is pending. Completed alone never produces saved wording; an unavailable query never produces a zero or a saved claim.

The existing Recovery correction prompt, Resume/Details, Remove-from-list explanation, Abandon wording and session panel strings are unchanged. Status is text and wraps in the row; colour is not the carrier of meaning. Human wording approval and physical input/scaling checks remain open.
