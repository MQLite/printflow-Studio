# Jira 建单结果

2026-10-22（Pacific/Auckland）。已建立 2 个 Epics、14 个 Tasks，全部 To Do。父级、标题、类型、Planning-ID 及逐条验收内容均已读回匹配；28 条 Blocks 前置依赖和 1 条 Epic Relates 关系已读回核对。

用户于 2026-10-22 确认“先完成方案并建立 Jira（推荐）”；仅规划与建立 Jira，本轮不实施产品代码。

[完整方案](PLAN.md) · [结构化范围](BACKLOG.json) · [创建台账](JIRA_CREATED.json) · [依赖台账](JIRA_LINKS.json) · [最终读回](JIRA_READBACK.json)

## 创建项目

| 计划编号 | Jira | 类型 | 标题 | 父 Epic |
| --- | --- | --- | --- | --- |
| E1 | [SCRUM-11198](https://yituoxx.atlassian.net/browse/SCRUM-11198) | Epic | WPF UI 全页面推广与审核差异整改 | — |
| E2 | [SCRUM-11199](https://yituoxx.atlassian.net/browse/SCRUM-11199) | Epic | 中文文案简化与老年用户易用性改进 | — |
| U01 | [SCRUM-11200](https://yituoxx.atlassian.net/browse/SCRUM-11200) | Task | 建立全部页面与状态清单并冻结 WPF UI 样板基线 | SCRUM-11198 |
| U02 | [SCRUM-11201](https://yituoxx.atlassian.net/browse/SCRUM-11201) | Task | 将 WPF UI 样板整理为统一主题与可复用页面组件 | SCRUM-11198 |
| U03 | [SCRUM-11202](https://yituoxx.atlassian.net/browse/SCRUM-11202) | Task | 统一当前步骤标题、紧凑步骤栏与固定操作栏 | SCRUM-11198 |
| C01 | [SCRUM-11203](https://yituoxx.atlassian.net/browse/SCRUM-11203) | Task | 盘点全部用户文案并制定简明中文术语与改写规范 | SCRUM-11199 |
| C02 | [SCRUM-11204](https://yituoxx.atlassian.net/browse/SCRUM-11204) | Task | 改写首页、路线选择、处理、审核与裁切引导文案 | SCRUM-11199 |
| C03 | [SCRUM-11205](https://yituoxx.atlassian.net/browse/SCRUM-11205) | Task | 简化尺寸、白墨和 TIFF 的说明并保留必要印刷信息 | SCRUM-11199 |
| C04 | [SCRUM-11206](https://yituoxx.atlassian.net/browse/SCRUM-11206) | Task | 简化保存、完成、失败恢复与辅助页面的状态和提示 | SCRUM-11199 |
| U04 | [SCRUM-11207](https://yituoxx.atlassian.net/browse/SCRUM-11207) | Task | 推广首页样板并统一导入、路线选择和原图确认 | SCRUM-11198 |
| U05 | [SCRUM-11208](https://yituoxx.atlassian.net/browse/SCRUM-11208) | Task | 统一处理进度、图片审核和裁切工作区 | SCRUM-11198 |
| U06 | [SCRUM-11209](https://yituoxx.atlassian.net/browse/SCRUM-11209) | Task | 统一印刷尺寸、白墨选择与 TIFF 生成确认页面 | SCRUM-11198 |
| U07 | [SCRUM-11210](https://yituoxx.atlassian.net/browse/SCRUM-11210) | Task | 推广保存页样板并重做独立完成摘要 | SCRUM-11198 |
| U08 | [SCRUM-11211](https://yituoxx.atlassian.net/browse/SCRUM-11211) | Task | 统一恢复分支、最近任务、辅助页面和对话框 | SCRUM-11198 |
| U09 | [SCRUM-11212](https://yituoxx.atlassian.net/browse/SCRUM-11212) | Task | 完成全页面视觉、业务安全与无障碍回归并归档证据 | SCRUM-11198 |
| C05 | [SCRUM-11213](https://yituoxx.atlassian.net/browse/SCRUM-11213) | Task | 制定并执行老年用户文案理解度与操作走查验收 | SCRUM-11199 |

## 继续顺序

先执行 [SCRUM-11200](https://yituoxx.atlassian.net/browse/SCRUM-11200) 建立可重建样板基线与全页面状态矩阵，再执行共享主题、页面壳和文案规范。页面/文案迁移后开展整体回归和目标用户走查。

本轮未修改产品代码、未重新执行产品构建或测试、未更改旧 Jira 状态。旧工作区内容及未提交样板保留；未提交 Git、合并、推送或发布。老年用户理解度与真实 DPI 等仍为后续验收事项。

