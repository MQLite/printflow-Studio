# 新手操作员完整流程验收 / Beginner acceptance walkthrough

**PREPARATION ONLY — HUMAN WALKTHROUGH NOT RUN.** Prepared 2026-10-05 for SCRUM-11155, Epic SCRUM-11139. No participant, visible session, build or fixture qualification has occurred in this preparation.

Reuse: [discovery script and original record](operator-ux-novice-walkthrough.md), [terminology](operator-ux-terminology.md), [execution plan](../codex/printflow-operator-ux-backlog/SCRUM-11155_PLAN.md), [current blockers/results](../codex/printflow-operator-ux-backlog/SCRUM-11155_RESULTS.md). The old discovery document's FUTURE labels describe its September preparation scope. Direct trim adjustment, delivery and colleague correction now have implementation records, but each needs a currently qualified fixture before this session.

## 主持人准备

1. 参加者是真实操作员，不熟悉 Photoshop、美图秀秀、图层、通道、DPI 或程序状态；记录匿名编号，不能由代理或开发者代替。若发现阶段实际有参加者，尽量邀请另一人。
2. 采用中文界面和中性任务卡。仅用合成图片或用户同意的非客户回归图片；图片、任务名、选择器内容和记录都不能出现客户信息。
3. 工作站没有正在进行的生产工作。沿用已审查的[隔离入口流程](../codex/printflow-operator-ux-backlog/SCRUM-11154_WORKSTATION_ENTRY_RUNBOOK.md)，使用新候选、新隔离根目录和实际通过的准备证据。任何历史测试根或确认都不能复用。
4. 在 visible/Interactive 会话前取得与本次运行绑定的安全桌面确认。主持人核对显示条件、夹具、可用操作及停止方式。这里的文档不能充当确认；不得直接启动正常生产应用。
5. [最新检查](../codex/printflow-operator-ux-backlog/SCRUM-11154_ZH_RECHECK_RESULTS.md)中的 N4（PNG 字节冒充 TIFF）、F6 尺寸不符和缺失 Recovery 夹具尚未解决。此前置状态不支持完整验收。相关场景保留 BLOCKED，直到修复或明确处理并验证；不绕过审批、保存或文件身份检查。
6. 主持人先逐场景确认合法、可用的起点；每条路线使用独立的新合成任务。开发者练习、预置批准和代理截图均不能算参加者完成。

发生客户内容泄露、隔离不确定、意外外部程序动作、不可识别状态、非预期文件变更或参加者要求停止时，立即停止操作并记录匿名事实。交由工作站负责人判断，不擅自修复生产环境。

## 主持原则和记录规则

只读下表的任务提示，不预先教路线、指按钮或解释术语。参加者请求帮助时，先记录在哪里受阻和原始请求，再给予最少的安全提示。经提示完成记为 assisted，不能写独立完成 YES。不要记录耗时、成功率、时间目标或客户信息。

结果使用 PASS、FAIL、BLOCKED、NOT RUN；独立完成使用 YES、NO、UNOBSERVED。PASS 必须来自真实参加者完成该场景的事实；已知失败、缺失夹具或只完成一部分均不能记 PASS。使用合成处理器时，仅评估交互理解，不声称真实 Meitu/Photoshop、打印质量或生产适用性通过。

## 中文任务提示

| ID | 场景 / 中性提示 | 主持人观察和前置条件（不要向参加者提示答案） |
|---|---|---|
| A01 | 首页与近期任务：“请看首页，告诉我这台工作站现在能做什么，哪些任务需要你处理，并按你认为合适的方式继续。” | 分清未检查、被阻止、上次检查与当前权限；区分正在处理、待检查、停止、移交、完成与曾保存。N1 Interrupted 状态不一致仍开放。 |
| A02 | 素材路线：“这是一张以后还要用于设计的素材图片。请选合适的处理方式，把它处理成系统认可的 PNG 结果。” | 选择目的正确；一张图一个任务；无印刷尺寸或 TIFF 步骤。KeepOriginalExtent 的 PNG 人工审批缺口仍需处理；未审核的预置结果不能支持审批 PASS。 |
| A03 | 客户设计路线：“这是一张已经排好版的合成设计图。请选合适的处理方式，制作打印 TIFF。” | 使用合成图和独立任务；完整维度、审核与交付链。N4/F6 未处理时 BLOCKED。 |
| A04 | 生成 TIFF 路线：“店内设计已经完成，只需要制作打印 TIFF。请选合适的处理方式并继续。” | 与 A03 用不同新任务；观察错误选路、修正及帮助。N4/F6 未处理时 BLOCKED。 |
| A05 | 状态与下一步：“请告诉我现在是什么状态、系统希望你下一步做什么，然后继续。” | 在处理中、需输入、待审核、停止、移交、批准、保存等实际可达状态分别观察。预置状态不等于参加者走过该转换。 |
| A06 | 审核与求助：“请检查这个处理结果，决定是否接受。请说出你会检查什么；不确定时请按你认为合适的方式处理。” | 分别提供高清化、去背景、裁切和 TIFF 合法审核起点；观察检查提示、通过/驳回后果和求助路径。不能用有缺陷的假 TIFF 证明最终审核成功。 |
| A07 | 直接调整裁切：“系统建议保留的范围稍有不合适。请从当前页面把范围调到你认为合适的位置，并查看如何回到系统建议，再确认保留范围。” | 使用已实现的边/角手柄、缩放和恢复建议；确认只改变合成任务。旧手动裁切后备界面不是本场景的替代证据。 |
| A08 | 同事修正与明确导回：“这个去背景结果需要同事修正。请把它交给同事；修正文件准备好以后，请把结果带回这个任务并检查。” | 记录原图参考副本、工作副本和说明；合成修正返回文件预先合法准备；必须实际选择、导入、检查当前结果。不得假定自动检测、云共享或 Photoshop TIFF 导回。 |
| A09 | 印刷尺寸与放大：“请把这张图设成任务卡上的印刷尺寸。告诉我你认为尺寸会怎样影响图片，再继续。” | 两张中性尺寸卡：普通尺寸、需放大尺寸；观察最大边界/目标边理解、无效输入和明确接受放大。F6 不可被隐藏；N3 数值可读性仍开放。 |
| A10 | 保存、完成和找文件：“请选择保存位置和文件名，保存最终结果，打开它所在的文件夹，并告诉我这个任务是否已经完成。” | 真实精确审核后才交付；观察批准、曾保存和完成的区别；实际验证交付文件而非内部 Approved。TIFF 受 N4/F6 阻挡；夹具内文件选择器有已记录边界。 |
| A11 | 失败与恢复：“处理没有成功。请根据屏幕上的信息判断发生了什么、文件是否还在，以及下一步怎么做，再尝试可用的恢复方式。” | 仅预先设计的隔离故障；记录错误详情/诊断入口、合法重试或 Recovery。当前缺失 Recovery 起点时记录 BLOCKED，不能制造生产故障。N2 accessible name 与诊断导航残留仍开放。 |
| A12 | 全程用语与结束：“请告诉我这些提示中有哪些不明白的地方，最终结果在哪里，以及还有没有任务需要你处理。” | 汇总术语理解与跨屏状态一致性；不给参加者代写判断。中文 owner copy、完整键盘/视觉条件的其他验收仍需独立事实支持。 |

## Anonymous session record (English)

| Field | Value |
|---|---|
| Session date / participant / facilitator codes | UNOBSERVED |
| Participant qualification and relevant prior experience | UNOBSERVED |
| Different discovery participant, if discovery actually ran | UNOBSERVED |
| Candidate source / binary / manifest identity | UNOBSERVED |
| Fresh isolated root identity / preparation evidence | UNOBSERVED |
| Run-bound safe-desktop acknowledgment / idle workstation evidence | UNOBSERVED |
| Synthetic fixture IDs and supported returned-file identity | UNOBSERVED |
| Display, work area, scaling and viewport | UNOBSERVED |
| UI language | zh-CN planned; NOT RUN |
| Synthetic processor limits / scenario availability | UNOBSERVED |
| Session result | NOT RUN |
| Discovery comparison source | NOT AVAILABLE — no genuine discovery observations were found in the current record |

## Per-scenario observations (English)

Add a separate row for every actual attempt. Preserve failures and the candidate identity when repeating a scenario. All fields below deliberately contain no participant evidence.

| Scenario | Result | Independently completed | Help requests | Wrong clicks/routes | Hesitation / factual quotation | Discovery comparison |
|---|---|---|---|---|---|---|
| A01 Home / Recent | NOT RUN | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT AVAILABLE |
| A02 Asset route | NOT RUN | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT AVAILABLE |
| A03 Customer-design route | NOT RUN | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT AVAILABLE |
| A04 Generate TIFF route | NOT RUN | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT AVAILABLE |
| A05 Status / next step | NOT RUN | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT AVAILABLE |
| A06 Review / ask for help | NOT RUN | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT AVAILABLE |
| A07 Trim adjustment | NOT RUN | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT AVAILABLE |
| A08 Correction / re-import | NOT RUN | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT AVAILABLE |
| A09 Size / enlargement | NOT RUN | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT AVAILABLE |
| A10 Save / folder / Complete | NOT RUN | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT AVAILABLE |
| A11 Failure / recovery | NOT RUN | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT AVAILABLE |
| A12 Wording / whole journey | NOT RUN | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT AVAILABLE |

## Open findings and proposed follow-ups

The inherited N1–N4/F6 and prior authority/recovery gaps are predecessor findings, not newly observed novice failures. Keep them in the linked issue results. Populate this table only from a real session; a new finding outside existing Epic tasks is a proposed addition, with no automatic scope expansion.

| Finding / scenario / candidate | Factual observation | Severity / impact | Proposed follow-up and existing Task | Recheck evidence / disposition |
|---|---|---|---|---|
| UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED |

## Closeout

Record all failed, unavailable and unattempted scenarios without converting them to PASS. Compare against discovery only where genuine prior observations exist. Check that written findings and any retained media contain no identifying information. Report SCRUM-11155 as NOT RUN if no suitable operator session occurred, and keep the Epic's operator validation unclaimed. Completion still requires assessment of every child Task and the Epic's original criteria.
