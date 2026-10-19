# SCRUM-11169 Native 执行交接

用户最新授权（2026-10-19）：「SCRUM-11177 通过。请继续推进 SCRUM-11169，沿用已批准的视觉风格和 Native 执行方式。」本次范围是整个 E2：SCRUM-11178～11181。直接授权优先于 Jira 中最初创建待办时的“仅规划/尚未授权实现”历史文字。

## 工作区与范围

复用已附加的隔离工作树 `C:/Users/admin/.codex/worktrees/scrum-11177-guided-theme/printflow-Studio`，新分支 `codex/scrum-11169-guided-shell`，基线 `7fd87f6ef7ac349633af19aa04355def00c79b41`。原 SCRUM-11177 分支保持原提交；主检出 `D:/Repositories/printflow-Studio` 保持 master 和其他未提交文档，禁止修改/清理其源代码或合并。本工作树未跟踪的 `output/imagegen/` 是批准的参考材料，保留、不得误提交约70MB图库。

Jira 精确需求与依赖快照见 `SCRUM-11169_JIRA_SCOPE.json`。本次已将用户明确验收的 SCRUM-11177 转为 Done；Epic11169 和 G01/11178 为 In Progress；G02～G04待实现。Jira 状态由父会话更新，不发送评论、不关闭E2或其他Epic。

用户目标：按真实业务状态引导一步一步操作，只呈现当前必要内容；左侧步骤/当前工作区/固定主操作栏，合法辅助动作、关键阻塞及停止/接管保持可达。三路线、所有必要审核、原件/InputSnapshot、精确审批身份、下游失效、300 PPI/CMYK/W1、环境门禁、自动化锁、保留/回收与本地隐私契约均保持。素材路线无印刷/白墨/TIFF，直接TIFF无美图和默认裁切，两条TIFF均需明确选择0/1/2px白墨及理由，无默认值。

批准规范为 `output/imagegen/SCRUM-11168/2026-10-19-v2/design-spec.html`，45页原型/18组流程已获用户批准，SCRUM-11177共享主题也已获批准。这里复用这份已批准的书面设计和现成 Theme/组件，不重新选择视觉风格。E2实现应用壳、状态投影、历史只读/合法返回确认、瞬时草稿离开与恢复/异步/输入身份保护；后续 E3～E6、AI业务、新编辑器、数据库草稿持久化/迁移均不在本次范围。不得重写业务状态机、重新实现审批/保存/恢复服务或创造命令资格。

## 路由与执行方法

用户个人 development-routing v2.4 已按延续的 PLAN_EXECUTE 范围启用，源 `C:/Users/admin/.codex/workflows/development-routing.md`，RouteOffset=0。按该用户指令：形成可执行计划后在授权范围内继续执行，不逐阶段重新请求授权。本次已有批准的完整书面v2设计且用户明确选择Native并授权E2；技能的通用阶段确认流程不能重复索取这些已给出的授权。请在账本中记清这一依据；如发现必须改变批准的产品行为/持久化/安全契约，先报告父会话，不能把范围外新设计默认为获批。

控制会话实际 gpt-6.1-sol/xhigh，无原地切换工具（MODEL_SWITCH_UNAVAILABLE仅指当前控制会话）。精确可选 UI执行目标 gpt-6-astra/high。你是一个整组Native执行上下文：自己顺序实施全部E2任务，不派逐任务实现者或审查者；完成后父会话安排唯一一次全新上下文整体审查。实际模型须核实元数据后记录，不能以派发成功当作证明。规划后及真实边界按规则复评；紧密耦合的UI/展示投影单元保持Astra High，若出现可分离的非UI后端工作先提出真实边界，不擅自扩展业务层。

## 计划、账本与实现要求

先读取源码、上述Spec和Jira快照，使用 brainstorming核对已定意图/方案，再用writing-plans形成 `docs/superpowers/plans/2026-10-19-scrum-11169-guided-shell.md`；精确列出文件、接口、G01～G04映射、测试、原控制与新控制的职责和边界。源代码探索后决定最小安全拆分，不凭旧任务文件名猜实现。计划须具体可审阅、自审，随后按已授权的Native继续；发布计划和重要新决策给父会话，父会话会向用户展示，方法不再重新选择。

应用 executing-plans、using-git-worktrees（隔离已就绪，无需新建）、test-driven-development及writing-good-tests、verification-before-completion；代码失败用systematic-debugging。技能根 `C:/Users/admin/.codex/plugins/cache/openai-curated-remote/superpowers/6.4.2/skills/`。Git Bash `C:/Program Files/Git/bin/bash.exe`；使用本计划自己的 `.superpowers/sdd/<plan-basename>/progress.md` 和 task-start/task-done。不要改读写E1的scoped ledger来当自己的账本；E1的已提交结果/证据可读取。

原型不承诺新增草稿存储。未提交输入的“保留”可落实为继续编辑/取消离开，放弃仅丢本页草稿；离开草稿与放弃整个任务分开。历史只能是不可执行的快照，不可把旧 SessionView 交给可执行当前会话命令；返回重做必须绑定当前任务/结果/目标身份、展示下游影响、取消无业务副作用，并调用现有合法返回入口。恢复以持久化状态为准，不自动启动外部动作。隐藏/已卸载页的旧UIA引用、回调、Enter/Space余波、双击、语言/窗口刷新都必须不能操纵新步骤或新任务。保留现有 ReviewApprovalButton 和精确 TargetIdentity/IsGestureGuarded 合同；通用主操作的上下文防重不等于授权审批。

本项允许必要的App层 SessionViewModel 分部、SessionScreenView/子视图与code-behind改造，不能照抄D04的“Session不改”限制。优先保持业务层只读复用；若历史所需读模型/接口确有缺失，计划中精确定义最小只读扩展及测试，报告父会话，不擅自改数据库或引擎语义。保留当前相关动作的AutomationId、ICommand/参数、预览/裁切/TIFF身份和中性焦点语义。已有布局测试若与批准的新布局发生有意冲突，明确记录被替代的几何契约与新断言/视觉证据；不能删除安全断言、放宽资格/身份检查或把新失败归入基线。

## 验证与证据

E1最终广域基线原始证据可复用：`output/ui/SCRUM-11177/checks/scrum-11177-ui-fix.trx`，1284项，1269 PASS/14 FAIL/1 SKIP，40新Guided通过；af04095到7fd87f6没有src/tests变化。记录这一精确关联，优先跑新Epic的相关新鲜定向基线，避免无变化地重复5分钟广筛；最终E2必须跑适当业务/导航/显示/身份回归和至少一次UI/Architecture广筛，跟踪每个旧/新失败名字、原因及有意UI契约更新。不能把“与基线相同”改称全量通过，不能把旧测试偶尔不复现称修复。

三路线及正常/跳过/处理/审核/失败/接管/修正/完成、PDF/PSD、尺寸与白墨/多尺寸条件映射须行为验证；历史、取消返回、确认后下游失效、草稿离开、持久化恢复定位、旧回调/旧输入和隐藏UIA都需真实组件或合成harness证据。使用已有真实服务+temp SQLite/filesystem及可控延迟adapter，避免只测试mock或重复实现映射逻辑。

新证据目录 `output/ui/SCRUM-11169/`，以有代表性的中文步骤、三路线、历史、返回确认、草稿离开与恢复/失败截图覆盖最小充分集，记录源和截图哈希、实际DIP、96DPI、滚动/焦点/命令状态并逐图查看；1000×700、1280×720、1920×1080为候选验证目标，不冒充真实DPI支持。保留zh-CN/en资源，中文为人工验收语言，不安排英文人工走查。

禁止真实Application启动、Window.Show、工作站opt-in、桌面输入、实际Meitu/Photoshop、客户文件、外传/新服务、合并/推送/部署。此前file://浏览器被安全策略拒绝，不能起HTTP服务或换浏览器绕过；本地view_image检查PNG允许。真实键盘/屏幕阅读器/DPI/新手/生产均单列NOT RUN。WPF选区此前明确区分默认adorner与另一路径，不能把特定开关的合成图概括为生产默认行为。

## 完成交回

每个有意义任务顺序TDD并提交，写清Ruling及成本；每个里程碑发送简洁进度与对应Jira子项。完成后保存 `SCRUM-11169_RESULTS.md`、组件/导航用法、证据索引、原始TRX/对比、审查用source diff和账本；向父会话交回HEAD、提交、实际模型、精确测试计数、剩余失败/NOT RUN、全部Ruling、G01～G04各AC映射。停止于唯一最终整体审查之前，父会话安排该审查、后续一次修复和Jira收口。保留工作树与批准参考材料。
