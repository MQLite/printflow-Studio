# Memorial AI Authoring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交付可独立使用的快速/详细提示词工作台，再交付人物与装饰原样沿用的排版、结果对照及成品转换。

**Architecture:** Authoring 是可编辑需求，Request 是已冻结的生成尝试，二者分开。模板、文本与图片引用、保留与版式规则集中在 Workflow 的深模块内；App 只呈现同一草稿，Infrastructure 负责受管文件与 WIC/WPF 光栅化。自动 Work 通道与幂等 Session 导入继续通过既有能力门禁接入，不因工作台上线自动启用。

**Tech Stack:** .NET SDK 10.0.400；C#；WPF；WPF-UI 4.3.0；CommunityToolkit.Mvvm 8.4.2；System.Text.Json；Windows WIC/WPF imaging；xUnit 2.9.3、Shouldly 4.3.0。不新增 NuGet 包。

**Spec:** [已通过的0.3设计稿](../specs/2026-11-03-memorial-guided-design.md)；[概念图与模板样例](../../../output/imagegen/ai-memorial-guided/2026-11-03-v1/README.md)。执行者同时读取规格和本计划。

## 授权与基线

- 2026-11-03 用户通过设计并要求 Superpowers 开发前规划。本次仅写计划与记录批准状态，没有实施、运行新功能测试、建 Jira、推送或发布。
- 当前规划分支 `codex/memorial-ai-guided-design`；规划前 HEAD `c3e967f`。其产品代码与已推送的 `master`/`origin/master` `43a8a42` 相同。
- 普通开发前规划未激活个人 development-routing v2.5；没有模型切换、全局自动执行或新聊天的授权/声明。
- 执行时以包含本计划的明确提交为起点，按 using-git-worktrees 检查可复用的隔离环境；不要假定 master 已包含本计划。保留现有工作，禁止强制覆盖。

## Global Constraints

- 设计只覆盖同一人物纪念 T恤、圆形徽章，以及“只改姓名日期”的快速模板；快速默认、详细可切换。
- 保留模板修改与本次全文修改的独立性；切换模式使用同一 DraftId，保留原文、图片身份和未完成确认。
- 图片类型沿用 PNG/JPEG/WebP；复用现有附件快照限额：图片20 MiB、文档40 MiB、最多8份、总计80 MiB。合成/结果另受40 MiB、64百万像素限制。
- T恤示例280×390 mm；徽章画布86 mm、安全区72 mm、单侧环带7 mm；均为需确认的历史预设，不是已验证设备成品口径。
- 真实文字原文、重音字符、大小写、标点与换行保持；生卒日期不猜测、不静默纠正。人物和原装饰采用已确认素材与允许变换，AI只负责授权新增内容。
- 不增加 ImageSharp、EF Core、通用状态机、收费 API、常驻服务或公网依赖；Domain 无 PackageReference。
- 保持三条既有路线、原件/InputSnapshot、审批身份、手势保护、下游失效、300 PPI/CMYK/W1、环境门禁和不覆盖保存合同。
- 产物为设计素材 PNG，不是生产 TIFF；不自动选择白墨，不把设计接受等同生产批准。严禁以图1/图2可读标签猜测附件、以最新文件猜测结果或恢复浏览器控制下载方案。
- 执行者仅按获授权的计划范围实施。只授权A时不自动开展B；明确授权A+B时按依赖顺序执行，不因内部任务边界重复索取同一授权。

## Review Focus

1. 长姓名、组合重音字符、CRLF及手动粘贴内容不能被编辑器重写：A1/A4/A6测试。
2. 文件选择器晚返回、同名异图或原文件被替换，不能把错图绑定到新页面：A3/A5测试。
3. 提示词已手改、表单仍为旧姓名时切详细，不能暗中覆盖或伪造确认：A4/A6测试。
4. 切模板、模式或关闭窗口发生保存失败，不能丢稿或显示保存成功：A2/A5/A7测试。
5. 长姓名与胸章靠近徽章安全区，不能用截字/裁饰物“自动适配”：B1/B2/B3测试。

## 分计划与执行顺序

这里的计划A/B是实施分包，均服务于已通过的“分步向导＋参考对照”，不改变产品方案中的A＋B含义。

| 计划 | 独立交付 | 前置 |
| --- | --- | --- |
| [A：提示词工作台](2026-11-03-memorial-prompt-workbench.md) | 模板可编辑、全文可编辑、引用处选照片、同稿切详细、保存与复制；无需自动通道 | 已通过设计 |
| [B：保真排版与成品转换](2026-11-03-memorial-composition.md) | 原素材与文字层合成、真实版式预览、安全区检查、T恤/徽章版本、结果对照与设计素材导出 | A的草稿和素材接口 |

A可在自动生图不可用时独立交付；详细页中的实际版式预览/合成入口在B完成前明确显示能力缺口。A完成不等于整套详细流程或AI端到端完成。B可先使用人工提供的新背景素材，独立验收合成与转换，不依赖网页自动生成。

## 文件职责地图

| 位置 | 职责 |
| --- | --- |
| `src/PrintFlow.Domain/AiDesign/Authoring/` | 模板、文档片段、槽位、草稿、确认与产品规格的纯数据；不依赖WPF/文件系统 |
| `src/PrintFlow.Workflow/AiDesign/Authoring/` | 模板组装、草稿命令、确认失效与复制导出；模板/草稿存储seam |
| `src/PrintFlow.Infrastructure/AiDesign/Authoring/` | 模板与草稿CAS持久化；受管快照预览；不增加网络 |
| `src/PrintFlow.App/ViewModels/AiDesign/`、`Views/AiDesign/` | 单一工作台VM的Quick/Detail呈现、模板编辑、图片槽位与预览 |
| `src/PrintFlow.App/Controls/AiPromptEditor.*` | 可编辑文本与可操作引用节点的WPF投影，不承担领域校验 |
| `src/PrintFlow.Domain/AiDesign/Memorial/`、`Workflow/AiDesign/Memorial/` | 毫米几何、素材权限、版式、版本与结果核对 |
| `src/PrintFlow.Infrastructure/AiDesign/Memorial/` | 专用STA上WIC解码、WPF文字/图层合成、受管PNG发布 |

具体创建/修改文件与任务接口见两个分计划。不要重构旧 `AiDesignCoordinator`、`FileAiDesignRequestStore` 或 SessionService 来承载未完成的创作表单。

## 既有能力依赖与接入门禁

本节是接入条件，不是新建任务或对Jira实时状态的声明。

| 能力 | 既有工作项/合同 | 本轮处理 |
| --- | --- | --- |
| Work上传/单次提交 | SCRUM-11158、11162、11163 | 未具备真实验证时不启用自动提交；工作台可复制需求 |
| 结果身份、MCP/Drive接收、PNG/JPEG完整解码 | SCRUM-11164与现行spec 0.6 | 外部生成素材必须经该合同或明确人工选图；不选“最新图” |
| request与Session事务性幂等导入 | SCRUM-11165 | 自动“接受并进入处理”保持禁用直至真实接入；不能用按钮防双击冒充跨重启幂等 |
| AI页面/统一引导与真实E2E | SCRUM-11166、11184、11167 | 本计划提供新的UI与保真分工；真实通道/真人/生产验证单独取证 |

后续连接自动尝试时，必须先把最终文档、活动槽位和当前素材冻结，给新Request分配自己的附件所有权并原子发布完整请求；不能把未完成Authoring草稿放进 `PromptReady`，也不能把Draft所有权的文件引用直接冒充另一Request的附件。该适配与导入须在上述既有合同完成后另列接入任务，本轮A/B不扩展旧协议或伪造可用性。

## 验证与证据策略

- 每个任务遵循 RED → 最小实现 → GREEN → 任务提交。先写行为失败测试，不用只断言getter/setter的测试代替用户行为。
- 计划中的命令是未来执行指令，不是已运行结果。未实现的新测试不能标PASS。
- 使用 `artifacts/test-results/memorial-authoring/<唯一运行名>/` 新目录；固定候选commit/构建哈希、filter、退出码、TRX及必要离屏图，不覆盖历史证据。
- 复用 `output/ui/SCRUM-11213/c05-20261029-r1/checks/safe-ui-exclusions.json` 的11项排除，加全部Smoke及 `WorkstationEntryPhase!=Runtime`。大UI类分进程运行，避免已知整批内存累积；不以删测试解决问题。
- 最终至少完成 locked restore、Release build、所有新增单元/集成/可安全运行UI测试、原AI与Architecture、启动/导入及三路线针对性回归；只有新增担忧才扩大或重复测试。
- 物理键鼠、真实窗口DPI、读屏、目标用户、真实Work、设备打印、安装/隧道/生产验证分别保留NOT RUN，除非获得对应授权并实际执行。

## 规格覆盖与自审

| 规格章节 | 负责任务 |
| --- | --- |
| §2.1/2.2 快速、模板、最终全文 | A1、A2、A4、A5 |
| §2.3/2.4 同稿切换、引用位置选照片 | A3、A4、A6 |
| §3/4 详细步骤、参考与保留 | A6；实际保真由B1–B4 |
| §5 姓名日期与精确文字 | A6、B2 |
| §6 产品/安全区/透明边界 | B1、B2、B3 |
| §7 版本转换与取消 | B1、B4 |
| §8 文档/处理计划/手改保护 | A1、A4、A6；B1/B3 |
| §9 结果核对、导出/后续导入 | B4、B5；自动通道/幂等导入按上表保持资格门禁 |
| §10 确认失效/异常 | A2–A7、B1/B4 |
| §11 验收情景1–15 | A/B任务测试与B5整合验收 |

自审须确认类型/方法名跨任务一致、所有五项Review Focus有测试归属、每一步可验收，且没有把未授权外部接入写成已包含实现。通过后提交文档，交用户评审并选择执行方式；不在本规划轮实施代码。

2026-11-03本轮自审已完成：

- [x] 对照规格各节与15项情景确认任务归属；自动通道及Session导入明确为既有依赖门禁。
- [x] 修正跨任务数据归属、日期解释参数、模板库类型、版式保存和独立结果核对revision；手改冲突需显式核对解除。
- [x] 五项Review Focus均落到具名行为测试；每任务含RED、实现、GREEN与提交步骤。
- [x] 检查计划比例与实现边界；本轮只改文档，没有把未来验证标成已通过。
