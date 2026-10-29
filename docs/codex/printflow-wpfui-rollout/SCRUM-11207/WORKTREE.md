# SCRUM-11207 实现位置

U04 接续 C04，沿用 Native 和已有 U02/U03/C02/C03/C04 实现工作树，保留全部未提交改动。

- 工作树：`C:/Users/admin/.codex/worktrees/scrum-11201-shared-theme/printflow-Studio`
- 分支：`codex/scrum-11201-shared-theme`，HEAD `2561dfd127e3336ec57b37fb3fe47709c1d81fcb`。
- [交付报告](C:/Users/admin/.codex/worktrees/scrum-11201-shared-theme/printflow-Studio/docs/codex/printflow-wpfui-rollout/SCRUM-11207/README.md)
- [执行记录](C:/Users/admin/.codex/worktrees/scrum-11201-shared-theme/printflow-Studio/docs/codex/printflow-wpfui-rollout/SCRUM-11207/PROGRESS.md)
- [本轮精确补丁](C:/Users/admin/.codex/worktrees/scrum-11201-shared-theme/printflow-Studio/docs/codex/printflow-wpfui-rollout/SCRUM-11207/U04_ONLY.patch)

范围是首页、路线选择及原图确认的界面迁移。实施边界以 `output/ui/SCRUM-11207/source-before` 为准，不能把相对 HEAD 的全部改动归入 U04。不要 reset/clean/stash 或整包提交之前的未提交工作。

用户暂停时的 green4 已停止，没有完成 TRX，恢复后使用新日志和截图目录。最终验证以交付报告为准。未提交、合并、推送或修改 Jira；主工作区只新增本导航文件。
