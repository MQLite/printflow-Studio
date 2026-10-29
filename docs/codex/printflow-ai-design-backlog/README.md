# PrintFlow AI 设计素材 Jira 待办

用户于 2026-10-05 批准设计规格修订 0.2，并授权先拆分和同步 Jira。使用 Atlassian spec-to-backlog 工作流创建独立 Epic 与 Task；未开始功能实现。

- 项目：SCRUM（My Software Team），https://yituoxx.atlassian.net
- Epic：[SCRUM-11156](https://yituoxx.atlassian.net/browse/SCRUM-11156)
- 规格：[2026-10-05-prompt-work-images-design.md](../../superpowers/specs/2026-10-05-prompt-work-images-design.md)
- 隔离分支：`codex/prompt-work-images`
- Initiative／查询标签：`PF-AIDESIGN-v1`／`pf-ai-design-v1`
- 范围：三个 Prompt 模板、参考图片和文件上传、Work 网页生图、本地接收、素材导入及完整手动降级。Linux／Google／MCP 输出中转留到后续。

## 已同步任务

| 顺序 | Jira Task | 任务 | 前置（Blocks） |
| --- | --- | --- | --- |
| T01 | [SCRUM-11157](https://yituoxx.atlassian.net/browse/SCRUM-11157) | 建立 AI 设计独立分支的回归基线 | 无 |
| T02 | [SCRUM-11158](https://yituoxx.atlassian.net/browse/SCRUM-11158) | 验证 Edge 上 Work 生图、附件上传和下载的网页合同 | 无 |
| T03 | [SCRUM-11159](https://yituoxx.atlassian.net/browse/SCRUM-11159) | 实现生图请求状态、跨进程持久化及停止恢复 | SCRUM-11157 |
| T04 | [SCRUM-11160](https://yituoxx.atlassian.net/browse/SCRUM-11160) | 实现常用 Prompt 模板、编辑快照和附件引用 | SCRUM-11159 |
| T05 | [SCRUM-11161](https://yituoxx.atlassian.net/browse/SCRUM-11161) | 实现附件选择、不可变快照及完整性校验 | SCRUM-11159 |
| T06 | [SCRUM-11162](https://yituoxx.atlassian.net/browse/SCRUM-11162) | 实现 Native Messaging 宿主、分块协议和本地登记 | SCRUM-11161 |
| T07 | [SCRUM-11163](https://yituoxx.atlassian.net/browse/SCRUM-11163) | 实现 Work 网页附件上传确认和单次生图提交 | SCRUM-11158、SCRUM-11160、SCRUM-11162 |
| T08 | [SCRUM-11164](https://yituoxx.atlassian.net/browse/SCRUM-11164) | 实现生成结果识别、下载接收和图片内容校验 | SCRUM-11163 |
| T09 | [SCRUM-11165](https://yituoxx.atlassian.net/browse/SCRUM-11165) | 实现 AI 结果与 ProcessingSession 的事务性幂等导入 | SCRUM-11164 |
| T10 | [SCRUM-11166](https://yituoxx.atlassian.net/browse/SCRUM-11166) | 实现 WPF AI设计页面、进度及完整手动交接 | SCRUM-11165 |
| T11 | [SCRUM-11167](https://yituoxx.atlassian.net/browse/SCRUM-11167) | 完成真实端到端、故障恢复验收与本地试用文档 | SCRUM-11166 |

## 同步核对

12 个事项（1 Epic＋11 Task）均已逐项读回，标题、类型、标签、完整要求与验收条件核对通过；11 个 Task 均以 `parent=SCRUM-11156` 归属，11 条 Blocks 依赖方向正确。状态均为 To Do；未指定负责人、Sprint、工期或估算。

写入清单见 [JIRA_WRITE_LEDGER.json](JIRA_WRITE_LEDGER.json)，服务器读回见 [JIRA_READBACK_SNAPSHOT.json](JIRA_READBACK_SNAPSHOT.json)。后续继续任务应复用这些编号，不重复创建。

## 当前限制

真实 Work 自动操作尚未经过实现验收；T02 在早期验证网页上传、生图与下载合同。完整 Debug 回归曾停滞并终止，原因未定位；T01 核实既有基线，不能以已有 Release 构建和两个 lease 用例通过推定完整测试通过。本轮完成的是待办同步，未实施、合并、发布或部署。
