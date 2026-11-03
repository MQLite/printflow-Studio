# 人物纪念创作：Jira Backlog

日期：2026-11-03，Pacific/Auckland。状态：**已创建并回读核对**。

开发跟踪（后续记录，不改变下方建单时的历史快照）：A1 / SCRUM-11219 已实现并通过独立代码审查，验证见[实施记录](../../codex/memorial-authoring/SCRUM-11219.md)。Jira移交In Review；未关闭Task/Epic。A2–A7和B1–B5仍待实施。

后续A2 / SCRUM-11220 已实现：个人模板版本、目录偏好、锁与CAS、原子持久化及保存稿重开；独立审查4项Important均已测试修复，最终615项回归通过，详见[A2实施记录](../../codex/memorial-authoring/SCRUM-11220.md)。移交In Review，未关闭任务；A3–A7及B1–B5仍待实施。A5接入前需补齐历史版本浏览/恢复接口。

后续A3 / SCRUM-11221 已实现：稳定图片槽位、受管快照、同句柄核验预览、当前引用纯文本与附件清单；独立审查4项Important已RED→GREEN修复，最终636项回归通过，详见[A3实施记录](../../codex/memorial-authoring/SCRUM-11221.md)。移交In Review，未关闭任务；A4–A7及B1–B5仍待实施。

后续A4 / SCRUM-11222 已实现：可编辑全文、稳定内嵌引用、键盘激活、精确文字粘贴、撤销/重做与宿主标签更新；独立审查1项P1已RED→GREEN修复并复审通过，最终750项分组测试通过，详见[A4实施记录](../../codex/memorial-authoring/SCRUM-11222.md)。本地交付可供Review，未修改Jira状态或关闭任务；A5–A7及B1–B5仍待实施。

后续A5 / SCRUM-11223 已实现：快速三栏、独立模板编辑与本次另存预览、精确复制/附件清单、迟到选图保护、最近历史浏览与核验恢复；独立审查3项P2已RED→GREEN修复并复审通过，最终770项分组测试通过，详见[A5实施记录](../../codex/memorial-authoring/SCRUM-11223.md)。本地交付，未修改Jira状态或关闭任务；A6/A7及B1–B5仍待实施，首页/导航/退出保护仍属A7。

用户已批准实施计划，并明确要求“请据此计划添加到Jira Tasks & Epics”。本轮使用 Atlassian spec-to-backlog 工作流在既有 SCRUM 项目创建2个Epic和12个Task，没有启动产品实施或推送/发布。负责人、Sprint、故事点未设置；全部工单回读为To Do。

## 来源与追踪

- 批准计划快照：仓库printflow-Studio，分支codex/memorial-ai-guided-design，commit `adee5e8`。
- [总计划](2026-11-03-memorial-authoring-plan.md)、[A工作台](2026-11-03-memorial-prompt-workbench.md)、[B合成与转换](2026-11-03-memorial-composition.md)、[设计规格0.3](../specs/2026-11-03-memorial-guided-design.md)。
- Jira站点：yituoxx.atlassian.net；项目SCRUM；共同label：`memorial-authoring`、`pf-ma-20261103`。
- 每项有唯一Planning-ID：`PF-MA-20261103-EA/EB/A1…A7/B1…B5`，并内嵌来源commit、验收条件、改动文件/接口及批准计划的实施与验证步骤。公共数据契约内嵌所属Epic。
- 源计划尚未推送；Jira描述没有引用未发布的GitHub文件URL，也没有将本地路径伪装成团队可访问的网络链接。

## Epics

| Jira | 目标 | 子Task数 | 回读状态 |
| --- | --- | --- | --- |
| [SCRUM-11217](https://yituoxx.atlassian.net/browse/SCRUM-11217) | PrintFlow AI创作：快速/详细提示词工作台 | 7 | To Do |
| [SCRUM-11218](https://yituoxx.atlassian.net/browse/SCRUM-11218) | PrintFlow AI创作：纪念T恤/徽章保真排版与成品转换 | 5 | To Do |

## Tasks与前置依赖

所有子项均为Task，保持To Do并正确设置parent；不是Subtask，也未搬动旧任务。

| 计划ID | Jira | 内容 | Parent Epic | 前置Task |
| --- | --- | --- | --- | --- |
| A1 | [SCRUM-11219](https://yituoxx.atlassian.net/browse/SCRUM-11219) | 模板和结构化提示词文档 | [SCRUM-11217](https://yituoxx.atlassian.net/browse/SCRUM-11217) | — |
| A2 | [SCRUM-11220](https://yituoxx.atlassian.net/browse/SCRUM-11220) | 模板个人版本和可恢复草稿 | [SCRUM-11217](https://yituoxx.atlassian.net/browse/SCRUM-11217) | [SCRUM-11219](https://yituoxx.atlassian.net/browse/SCRUM-11219) |
| A3 | [SCRUM-11221](https://yituoxx.atlassian.net/browse/SCRUM-11221) | 槽位、受管照片与纯文本交接 | [SCRUM-11217](https://yituoxx.atlassian.net/browse/SCRUM-11217) | [SCRUM-11220](https://yituoxx.atlassian.net/browse/SCRUM-11220) |
| A4 | [SCRUM-11222](https://yituoxx.atlassian.net/browse/SCRUM-11222) | 可编辑全文与内嵌选图控件 | [SCRUM-11217](https://yituoxx.atlassian.net/browse/SCRUM-11217) | [SCRUM-11219](https://yituoxx.atlassian.net/browse/SCRUM-11219) |
| A5 | [SCRUM-11223](https://yituoxx.atlassian.net/browse/SCRUM-11223) | 快速页与模板编辑 | [SCRUM-11217](https://yituoxx.atlassian.net/browse/SCRUM-11217) | [SCRUM-11221](https://yituoxx.atlassian.net/browse/SCRUM-11221)、[SCRUM-11222](https://yituoxx.atlassian.net/browse/SCRUM-11222) |
| A6 | [SCRUM-11224](https://yituoxx.atlassian.net/browse/SCRUM-11224) | 同稿切详细、文字与保留确认 | [SCRUM-11217](https://yituoxx.atlassian.net/browse/SCRUM-11217) | [SCRUM-11223](https://yituoxx.atlassian.net/browse/SCRUM-11223) |
| A7 | [SCRUM-11225](https://yituoxx.atlassian.net/browse/SCRUM-11225) | 接入首页、退出保护与整合交付 | [SCRUM-11217](https://yituoxx.atlassian.net/browse/SCRUM-11217) | [SCRUM-11224](https://yituoxx.atlassian.net/browse/SCRUM-11224) |
| B1 | [SCRUM-11226](https://yituoxx.atlassian.net/browse/SCRUM-11226) | 毫米版式、保护权限与规格 | [SCRUM-11218](https://yituoxx.atlassian.net/browse/SCRUM-11218) | [SCRUM-11225](https://yituoxx.atlassian.net/browse/SCRUM-11225) |
| B2 | [SCRUM-11227](https://yituoxx.atlassian.net/browse/SCRUM-11227) | 精确文字层与弧线排版 | [SCRUM-11218](https://yituoxx.atlassian.net/browse/SCRUM-11218) | [SCRUM-11226](https://yituoxx.atlassian.net/browse/SCRUM-11226) |
| B3 | [SCRUM-11228](https://yituoxx.atlassian.net/browse/SCRUM-11228) | 原素材合成与受管PNG | [SCRUM-11218](https://yituoxx.atlassian.net/browse/SCRUM-11218) | [SCRUM-11227](https://yituoxx.atlassian.net/browse/SCRUM-11227) |
| B4 | [SCRUM-11229](https://yituoxx.atlassian.net/browse/SCRUM-11229) | 详细页预览、结果核对与独立转换版本 | [SCRUM-11218](https://yituoxx.atlassian.net/browse/SCRUM-11218) | [SCRUM-11228](https://yituoxx.atlassian.net/browse/SCRUM-11228) |
| B5 | [SCRUM-11230](https://yituoxx.atlassian.net/browse/SCRUM-11230) | 设计素材导出、能力门禁与全组验证 | [SCRUM-11218](https://yituoxx.atlassian.net/browse/SCRUM-11218) | [SCRUM-11229](https://yituoxx.atlassian.net/browse/SCRUM-11229) |

## 已核验的原生Jira关系

### Blocks：13条

- [SCRUM-11217](https://yituoxx.atlassian.net/browse/SCRUM-11217) blocks [SCRUM-11218](https://yituoxx.atlassian.net/browse/SCRUM-11218)
- [SCRUM-11219](https://yituoxx.atlassian.net/browse/SCRUM-11219) blocks [SCRUM-11220](https://yituoxx.atlassian.net/browse/SCRUM-11220)
- [SCRUM-11220](https://yituoxx.atlassian.net/browse/SCRUM-11220) blocks [SCRUM-11221](https://yituoxx.atlassian.net/browse/SCRUM-11221)
- [SCRUM-11219](https://yituoxx.atlassian.net/browse/SCRUM-11219) blocks [SCRUM-11222](https://yituoxx.atlassian.net/browse/SCRUM-11222)
- [SCRUM-11221](https://yituoxx.atlassian.net/browse/SCRUM-11221) blocks [SCRUM-11223](https://yituoxx.atlassian.net/browse/SCRUM-11223)
- [SCRUM-11222](https://yituoxx.atlassian.net/browse/SCRUM-11222) blocks [SCRUM-11223](https://yituoxx.atlassian.net/browse/SCRUM-11223)
- [SCRUM-11223](https://yituoxx.atlassian.net/browse/SCRUM-11223) blocks [SCRUM-11224](https://yituoxx.atlassian.net/browse/SCRUM-11224)
- [SCRUM-11224](https://yituoxx.atlassian.net/browse/SCRUM-11224) blocks [SCRUM-11225](https://yituoxx.atlassian.net/browse/SCRUM-11225)
- [SCRUM-11225](https://yituoxx.atlassian.net/browse/SCRUM-11225) blocks [SCRUM-11226](https://yituoxx.atlassian.net/browse/SCRUM-11226)
- [SCRUM-11226](https://yituoxx.atlassian.net/browse/SCRUM-11226) blocks [SCRUM-11227](https://yituoxx.atlassian.net/browse/SCRUM-11227)
- [SCRUM-11227](https://yituoxx.atlassian.net/browse/SCRUM-11227) blocks [SCRUM-11228](https://yituoxx.atlassian.net/browse/SCRUM-11228)
- [SCRUM-11228](https://yituoxx.atlassian.net/browse/SCRUM-11228) blocks [SCRUM-11229](https://yituoxx.atlassian.net/browse/SCRUM-11229)
- [SCRUM-11229](https://yituoxx.atlassian.net/browse/SCRUM-11229) blocks [SCRUM-11230](https://yituoxx.atlassian.net/browse/SCRUM-11230)

Epic A阻塞Epic B，A7完成后开始B1；A4在A1完成后可独立于A2/A3推进，A5汇合。任务执行仍遵守批准计划和实际授权。

### Relates：7条

- [SCRUM-11217](https://yituoxx.atlassian.net/browse/SCRUM-11217) relates to [SCRUM-11156](https://yituoxx.atlassian.net/browse/SCRUM-11156)
- [SCRUM-11218](https://yituoxx.atlassian.net/browse/SCRUM-11218) relates to [SCRUM-11156](https://yituoxx.atlassian.net/browse/SCRUM-11156)
- [SCRUM-11225](https://yituoxx.atlassian.net/browse/SCRUM-11225) relates to [SCRUM-11166](https://yituoxx.atlassian.net/browse/SCRUM-11166)
- [SCRUM-11225](https://yituoxx.atlassian.net/browse/SCRUM-11225) relates to [SCRUM-11184](https://yituoxx.atlassian.net/browse/SCRUM-11184)
- [SCRUM-11228](https://yituoxx.atlassian.net/browse/SCRUM-11228) relates to [SCRUM-11164](https://yituoxx.atlassian.net/browse/SCRUM-11164)
- [SCRUM-11230](https://yituoxx.atlassian.net/browse/SCRUM-11230) relates to [SCRUM-11165](https://yituoxx.atlassian.net/browse/SCRUM-11165)
- [SCRUM-11230](https://yituoxx.atlassian.net/browse/SCRUM-11230) relates to [SCRUM-11167](https://yituoxx.atlassian.net/browse/SCRUM-11167)

既有任务职责不变：SCRUM-11166/11184保留远端Request运行、回收/导入与统一引导壳的分工；新A7提供Authoring工作台及入口，按描述复用。SCRUM-11164关联外部结果接收；SCRUM-11165仅作为自动接受→Session的真实资格门禁，不阻塞本地PNG导出；SCRUM-11167仍负责真实端到端验收。两新Epic关联原AI Epic SCRUM-11156，不把新Epic作为Epic的子项或重复创建旧能力。

## 回读验证

- 直接读取全部14个新建工单，核对key、IssueType、Planning-ID、源commit、描述内容、label、To Do和未分配负责人。
- 逐一核对12个Task的parent及验收条件；Epic为7+5个子项。
- 从issuelinks核对全部20条原生关系及Blocks方向，缺失0条。
- 创建前进行了相关JQL和语义检索，并读取既有AI任务以确认范围分工；未发现本轮计划的重复工单。
- 本轮验证为Jira回读和文档检查，不是产品实现或功能测试结果。
