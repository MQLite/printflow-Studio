# SCRUM-11177 执行交接

用户授权：2026-10-19「计划通过，按推荐的 Native 方式继续执行 SCRUM-11177。」批准计划为 `docs/superpowers/plans/2026-10-19-scrum-11177-guided-theme.md`，必须完整顺序执行五项任务。

工作区：`C:/Users/admin/.codex/worktrees/scrum-11177-guided-theme/printflow-Studio`；分支 `codex/scrum-11177-guided-theme`；基线 `de7bca6e0a8462584b4d6bf4d16307d1335e2fb3`。原工作区 `D:/Repositories/printflow-Studio` 有其他未提交文档，禁止清理/覆盖。新工作区复制的设计材料位于 `output/imagegen/SCRUM-11168/2026-10-19-v2/`，只作参考，不将约70MB图库或原工作区其他变更一起提交。

范围：仅 SCRUM-11177，WPF 共享主题、六类展示组件、首页/路线页接入、合成离屏证据。保留全部原业务命令、参数、审批身份、AutomationId、关键阻塞/停止入口和中性落焦。禁止改 Domain/Workflow/Infrastructure、Session 页面/审批逻辑、外部应用、生产数据、部署、推送和后续 Epic。两条TIFF路线白墨无默认值且明确选择0/1/2px和理由；素材路线无印刷配置。中文人工验收，英文资源保留，不做英文人工走查。

全局路由 v2.4 已按 EXECUTE_HANDOFF 启用，规则源 `C:/Users/admin/.codex/workflows/development-routing.md`。RouteOffset=0（默认）。当前控制会话实际运行 `gpt-6.1-sol / xhigh`（当前任务 JSONL 最新 turn_context 实证），没有原地模型切换工具，记 `MODEL_SWITCH_UNAVAILABLE`（仅当前会话）。UI 必须 Astra High，工具实际支持精确目标 `gpt-6-astra / high`。整组 UI 作为一个紧密耦合实现单元交由单一替代执行上下文顺序执行；不拆每任务实现者/审查者。这是已披露的 Native 执行机制适配。实际执行模型须读取执行上下文元数据核实，不能以请求被接受冒充实际模型。

模式：执行者自己按 executing-plans 顺序实施五任务，每任务 TDD、定向验证和提交；不要再派实现子代理。最后由控制会话安排一次全新上下文的整体审查（UI审查 Astra High）。不得跳过最后审查，也无需执行者再次派审查代理。当前没有产品源代码变更。

所需技能：executing-plans、using-git-worktrees（工作区已就绪）、test-driven-development（含 writing-good-tests）、verification-before-completion；代码失败用 systematic-debugging。技能路径根 `C:/Users/admin/.codex/plugins/cache/openai-curated-remote/superpowers/6.4.2/skills/`。Git Bash 已安装；技能 task-start/task-done/sdd-workspace 脚本可从 PowerShell 调用 bash，不能修改全局配置。

计划批准和完整设计已读取；执行者必须再读本工作区计划、Spec、任务 brief 和相关代码。沿用 `.superpowers/sdd/<plan-basename>/progress.md` 账本（首行标明计划身份），写清共享接口预检、每任务基线/红绿测试/提交和 Ruling。禁止编造测试通过。当前任务 Jira In Progress，11174～11176 Done，Epic11168仍InProgress；不关闭 Jira/Epic、不发送评论。Jira 状态由控制会话收口。

验证：优先离屏WPF/合成harness，禁止生产 App 启动、Show、Meitu/Photoshop、UI输入、客户文件和 opt-in 工作站变量。保留基线失败，不降断言。按计划记录截图状态、96DPI/DIP、源图哈希和截图索引，真实键盘/高DPI/新手/生产均单列 NOT RUN。图像通过本地 view_image 检查；禁止为打开 file:// 原型而起HTTP服务或换浏览器（此前安全策略已明确拒绝 file://，不得绕过）。原型图片可通过本地文件图片查看工具读取。

完成条件：五项计划交付具备代码、定向测试、计划要求回归/构建、至少21张离屏视觉证据和准确结果记录；随后向控制会话提供：提交列表、实际模型证据、完整检查命令和计数、截图路径、账本Ruling、失败/未运行项、可直接审查的 diff 与证据。勿 merge/push/deploy、勿清理工作树；保留证据待最终整体审查。
