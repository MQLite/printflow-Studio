# SCRUM-11169 — 引导式 Session 框架执行记录

本轮按用户“SCRUM-11177 通过，请继续推进 SCRUM-11169，沿用已批准视觉和 Native”的授权实施 G01–G04。交付左侧只读步骤、当前步骤专属页面、固定操作栏、只读历史、捕获身份的返回确认、临时草稿离开决策，以及恢复/异步/旧输入保护。业务工作流继续决定合法命令、审核与下游失效。

状态：G01–G04实现、视觉证据与受限回归已完成，Task5提交后验证记录待补；最终全新上下文整体审查由控制会话单独安排。未 push、merge、deploy、启动生产 App 或进入后续 Epic；本记录不等于真人验收、生产验证或 Jira Done。首轮广筛误执行三个无条件 Window.Show 测试，详见下面的边界更正；不再声称整个测试过程离屏。

## 范围与实现

| 原始条件 | 代码与可审查证据 |
| --- | --- |
| G01.1 任务名、当前步骤/状态、进度、工作区、固定主操作 | SessionScreenView / SessionViewModel.Guided；GuidedSessionShellTests 三尺寸位置、固定按钮、稳定 AutomationId |
| G01.2 必要输入/图片；历史/技术/低频次级入口 | ShowsGuided*、折叠 TechnicalDetails/HistorySelector/Return；39张真实页面 PNG，包含三路线及13状态 |
| G01.3 每步一推荐操作；驳回/求助/停止/接管可达 | RecommendedCommand 保留原命令；既有精确 ReviewApprovalButton；处理中 Stop/TakeOver 的启用和视口 bounds |
| G01.4 滚动不丢主操作，窄窗口可完成 | Session.ActionBar 在 Grid Auto 行；历史返回也固定；1000×700、1280×720、1920×1080 DIP |
| G02 步骤由业务事实投影；未来步骤不可执行 | GuidedSessionProjectionTests 检查三条 WorkflowCatalog 顺序、当前/完成/跳过/故障、必需审核及未来表单隐藏；步骤条无命令 |
| G02 各路线与当前内容 | PNG 路线无尺寸/白墨/TIFF；直接 TIFF 无美图/裁边；两条 TIFF 有明确0/1/2px、理由、尺寸、生成检查、审核、保存及再做尺寸；PDF/PSD先准备源图 |
| G03.1 查看历史不改变当前结果或审批 | SessionHistoryView 从已加载事实构建不可变 DTO；历史预览单独加载；History_is_readonly_and_cannot_approve_the_live_result 比较持久聚合 |
| G03.2–3 合法返回、影响确认、取消无写入、谱系失效 | ReturnIntent 捕获上下文/目标；重定向、取消、复用、换任务测试；原 ReturnToStep / FlexibleSize 持久化回归 |
| G03.4 草稿保留/放弃，不当成确认数据 | 下面五类 keep/discard 用例；仅当前 ViewModel，不新增存储、默认白墨或自动保存 |
| G04.1/4 恢复真实待输入/待审/中断状态 | Reopened_task_projects_persisted_page_without_starting_an_adapter 覆盖源图、尺寸、白墨、审核；中断用例与 StartupRecovery；记录适配器调用数为0 |
| G04.2 中性焦点；旧 Enter/Space/双击不审批新结果 | SessionActionButton + SessionActionScope；保留 exact ReviewApprovalButton；合成 WPF 键事件、UIA provider 和中性焦点测试，真实设备验证 NOT RUN |
| G04.3 重复/延迟/旧回调不覆盖新任务 | Open generation + operation ownership；延迟成功/失败/失败刷新、旧 finally、外来 runtime、隐藏/卸载/重绑、保留与已排队 UIA provider |

五类草稿均比较取消/保留/放弃前后的真实持久聚合，而不只检查显示字符串：

| 草稿 | 测试 |
| --- | --- |
| 尺寸 | Dirty_dimensions_require_keep_or_discard_before_home；Same_context_reload_preserves_dimension_draft |
| 白墨 | White_ink_draft_keep_and_discard_never_record_a_default |
| 修正 | Correction_note_keep_and_discard_are_transient |
| 裁切 | Manual_crop_selection_survives_reload_and_keep_but_discard_writes_nothing |
| 保存 | Final_save_draft_keep_or_discard_preserves_review_and_delivery_facts |

组件与维护说明：[guided-session-navigation.md](../../printflow/guided-session-navigation.md)。原始 Jira AC 快照：[SCRUM-11169_JIRA_SCOPE.json](SCRUM-11169_JIRA_SCOPE.json)。计划：[2026-10-19-scrum-11169-guided-shell.md](../../superpowers/plans/2026-10-19-scrum-11169-guided-shell.md)。

## 执行上下文与提交

- 工作区：C:/Users/admin/.codex/worktrees/scrum-11177-guided-theme/printflow-Studio；分支 codex/scrum-11169-guided-shell；基线 7fd87f6ef7ac349633af19aa04355def00c79b41。
- development-routing v2.4、RouteOffset=0；Normal/Requested/ExecutionTarget 与实际均 Astra/high。控制会话从执行者 turn_context 核对实际值：2026-10-19T01:57:26.572Z、session 01a151e0-b939-7122-9a42-544a9aeabb73。
- Native 单执行者连续处理，不按阶段重新委派；本轮没有执行者子代理或中途审查代理。计划后及上下文/证据边界复评：UI、交互与 WPF 几何紧密耦合，保持同一实际执行上下文，不声称模型降档。
- 提交：48798fb 计划/交接；1665381 G01；a203805 G02；dd1ee8e G03；c49d3a5 G04；Task5提交见最终交接 HEAD。
- Task3 task-done 在提交前执行，125项测试编译的内容与 dd1ee8e 完全一致；当时 ledger 的零长度范围已校正为 a203805..dd1ee8e。Task4先提交再 task-done，范围 dd1ee8e..c49d3a5。
- 原主检出和约70 MB未跟踪参考 gallery 保持原样。只使用合成源图、临时 SQLite/本地文件、既有 fake adapters；Windows PDF 的本地准备测试使用测试夹具，没有外部应用启动。

## 验证记录

原始 [checks](../../../output/ui/SCRUM-11169/checks/) 保留 RED、失败、修复及最终记录；不把中途失败删除后称全程成功。每次测试计数不可跨运行相加。可机器读取索引为 run-index.json。

| 检查 | 通过 / 失败 / 跳过 | 说明 |
| --- | --- | --- |
| E2 改动前目标基线 | 49 / 0 / 0 | 原 SessionControls、Return/Trim、Recovery、导航、焦点 |
| G01 首次 RED → task-done | 0 / 5 / 0 → 75 / 0 / 0 | 新壳/固定动作，FinalSave几何更新；实际 Tab 顺序缺陷已修复 |
| G02 task-done | 64 / 0 / 0 | 三路线、源图准备、明确白墨与必要审核 |
| G03 首次 RED → task-done | 1 / 4 / 0 → 125 / 0 / 0 | 历史/返回/草稿及尺寸/保存；旧360列定位更新后发现并修复真实缩放工具条裁切 |
| G04 首次 RED | 0 / 8 / 0 | 旧UIA/隐藏/卸载/输入和延迟覆盖被实测复现 |
| G04 扩展 dispatch 修复 | 17 / 0 / 0 | UIA Invoke 必须异步投递到 WPF Dispatcher |
| G04 宽回归 | 165 / 1 / 1（167） | 唯一失败为E1已有 Nonvisible_seed_and_real_command_persistence_smoke；工作站可见 smoke 保持跳过 |
| G04 模态/焦点 | 108 / 1 / 0 | 一次 Space 新手势未执行，详见下面，不能宣称修复 |
| G04 最新输入/确认 | 18 / 0 / 0 | 隐藏/卸载重绑、实际返回覆盖层 |
| G04 最终回归 / 提交后 task-done | 各95 / 0 / 0 | 输入、导航、exact review、焦点、恢复、启动恢复、FinalSave |
| G05 视觉/投影/壳最终 | 22 / 0 / 0 | 39张PNG；13状态×3尺寸；真实布局修复后 |
| G05 布局合同/保存最终 | 145 / 0 / 0 | 保留说明、警告、几何与保存身份断言；含16张补充审核PNG |
| 首轮 UI + Architecture | 1282 / 56 / 1（1339） | 14个E1同名失败；42个新失败，另误执行3个窗口测试 |
| 最终安全 UI + Architecture 子集 | 1314 / 14 / 0（1328） | 14个失败与E1同名且错误消息完全一致；明确排除11个可见/opt-in测试，不能称全量通过 |
| workflow/persistence 回归 | 277 / 0 / 0 | 经审计16类：导航/审批/恢复/源文件/路线/保存；原业务状态与持久事实 |
| Task5 提交后验证 | 待最终结果 | 最终证据与源码一致性 |

[baseline-comparison.json](../../../output/ui/SCRUM-11169/checks/baseline-comparison.json)保存14个当前/基线错误消息和堆栈，NewFailureNames及BaselineFailuresNotReproduced均为空。它们分布为HomeRecentAccessibility 2项、ManualCropAdjustment 2项、KeepOriginalExtent 3项、PrintDimensionsPreflight 5项、OperatorCultureIsolation 1项、OperatorWave1AHost非可见smoke 1项。测试失败仍保留，E2没有修复或接受这些旧问题。首轮42个新失败中，41个布局/页面合同问题已通过保持原安全断言的修正验证；余下可见键盘失败仅被排除，状态仍未验证。

G04初版 UIA provider 内联执行导致 SessionAccessibility 异步导入脱离 WPF 同步上下文并使 testhost 中止；原 g04-regression.trx（32 PASS/1 FAIL）及另一中止记录保留。修复使用 ButtonAutomationPeer 同样的异步 Dispatcher 投递，并在实际执行时重新核对捕获身份；没有绕开验证或放宽旧审批断言。

Space 波动的精确记录：g04-modal-focus.trx 的 GuidedSessionInputTests.Held_key_release_and_double_click_do_not_activate_new_context(key: Space)，最后新按下/释放应使调用计数从0变为1，实际仍为0。此前旧按下/重复/释放不得跨任务激活的断言通过。之后18、95与提交后95项运行中相同用例通过，代码没有针对该次失败作出已证实的修复。Keyboard.Modifiers 读取宿主状态、离屏焦点/调度是待验证假设；没有证据把它确定为产品缺陷、调度问题或基线故障。将此作为未解释波动交给最终审查，真人键盘也未测试。

### 验证边界更正

首轮广筛未经调用链审计，包含三个无条件 Window.Show：PrintDimensionsPreflightUiTests.Keyboard_traverses_the_bound_custom_size_controls_and_panel_does_not_trap_focus（FAIL），ErrorDetailsRenderingTests.Rendered_failure_entry_and_UIA_retry_preserve_history_and_only_reset_the_step（PASS），Error_details_previews_the_actual_plan_then_UIA_saves_an_exact_local_package（PASS）。它们确实显示了测试窗口并通过 WPF Focus/MoveFocus 移动焦点；三个finally均清空Content并Close，testhost已退出。这是执行边界错误，不能算授权桌面验收。

PrintDimensions键盘测试在从毫米框移到下一控件后期待确认按钮焦点，实际False；移至固定栏后的可见键盘顺序尚未验证。该测试源码与断言未改变，后续排除不是修复或PASS。另七个Live_*测试因未设置PRINTFLOW_*_LIVE变量立即返回，虽然TRX记Passed，真实UIA应记NOT RUN。Operator visible host为NotExecuted。

完整调用链、所有本轮TRX命中、时间、关闭证据和11项精确排除名单见 [visible-window-audit.md](../../../output/ui/SCRUM-11169/checks/visible-window-audit.md)、visible-window-path-results.json、safe-ui-exclusions.json。后续只运行经审计的run-safe-ui.ps1。E1基线同三个测试全部Passed、源码与本轮基线相同，因此历史E1广筛也不能描述为全程离屏；不改历史TRX、不撤销用户已验收范围、不借此扩展修旧问题。

## 几何与交互合同更新

这些是新壳导致的测试合同更新，单列而不伪装成E1遗留失败：

1. FinalSaveUiTests 和 TrimAdjustmentUiTests 原来按固定360 DIP列查找工作区。改用 Session.CurrentWorkspace 和实际列宽；保留裁切、控件可达、自动化名字、默认按钮、Tab顺序、精确裁边/保存身份与持久事实断言。
2. AdjustSize/ChangeSize 原来立即执行返回。对应旧用例现在明确调用 ConfirmReturn，并检查确认态；原预设、放大授权、不可变尝试审计、下游结果失效断言保持。
3. ReviewGuidanceLayoutTests 原 fddf243 矩形与 PrintSizeGuidanceLayoutTests 原 fe38160 矩形描述无侧栏布局。新测试比较同一新壳中显示/隐藏说明前后图片矩形一致；同时保留说明在右栏、换行、非覆盖、滚动可达、警告不折叠、技术行数与标题可见的断言。旧→新矩形保存在 geometry/。
4. 尺寸技术详情/准备状态的渲染用例先明确选择白墨，再观察实际生成前确认页；不恢复跨步骤同时显示的旧 UI。选择前后尺寸事实、详情值与只读展开不写数据的断言保持。
5. FailureGuidanceLayoutTests 的滚动行为用例显式展开技术详情，并仅把该用例的滚动容器限制为180 DIP，确保紧凑新页面仍产生真实滚动；保留故障前偏移大于0、故障后归零断言。正常视口另有布局测试。

真实视觉问题另行修复：历史关闭按钮在1000/1280下越界；尺寸推荐入口在1000下落到滚动区域之外；白墨选项底部839.7 DIP超过700视口；尺寸页下一步文案曾过早提示白墨。分别固定历史/尺寸动作、优先显示当前白墨选项、按当前页生成尺寸提示。相关 RED 保留为 g05-render-snapshots、g05-render-fixed、g05-white-choices-red；后续几何验证通过。

保留的审核标题可见性断言还发现1000宽度TIFF保存表单把审核说明挤到视口之外；现把原保存内容置于审核说明之后，原字段、命令和身份保持。最终相关145项通过。

## 视觉证据

[renders](../../../output/ui/SCRUM-11169/renders/) 包含39张页面PNG、39份对应JSON、一个合成原图。状态为三路线源图、返回确认、处理中停止/接管、停止失败、恢复中断、必需审核、只读历史、尺寸、草稿离开、明确白墨、生成前确认；每项有1000×700、1280×720、1920×1080 DIP。

JSON记录96 DPI、图片SHA256、同一源图SHA256、当前页面、动作矩形/启用/推荐、可见预览数量、滚动范围和逻辑焦点。源图SHA256均为 7C0C301F48992E292768A1156278CEAD3BF05829A792C53A80194E1F8981113B。真实 WPF 控件离屏 RenderTargetBitmap，没有 Window.Show。

已查看的最终选集19张：全部13个1000×700状态；1280×720的返回确认、白墨、历史；1920×1080的客户设计源图、必需审核、生成前确认。核对了固定动作、关键警告、当前内容、换行、透明边缘及页面/历史区分。这是执行者图像检查，不是用户或新手验收。

geometry/review-png另含16张中英文审核说明补充PNG；其中最终1000×700中文TIFF审核页已查看。evidence-index.json共55张页面，20张标记执行者已查看；英文自动布局/渲染不等于英文人工走查。source-hashes.json记录当前源码/测试SHA256，action-identity-comparison.json逐一比较基线53个原按钮：Command、CommandParameter、TargetIdentity、IsGestureGuarded、AutomationId、IsDefault、IsCancel均未变，且各保留一个实例。

初次新捕获测试修改已经在另一个STA渲染的模型，触发 CollectionView 跨线程限制（3 FAIL）；捕获改为对同一真实持久会话新建只读展示快照，驱动模型保持未绑定。该项是测试生命周期修正，不作为产品修复。未设置 PF_11169_CAPTURE_DIR 时仍运行布局断言，截图生成本身为NOT RUN。

## Rulings 与代价

- 已批准书面v2与直接 PLAN_EXECUTE/Native 授权替代重复计划/方法确认；代价是少一次泛化确认，由事前具体计划和最终整体审查补足。
- readonly history DTO 只暴露已经加载的版本事实，不创建可执行历史会话、新IO或schema；代价是只读投影维护及独立本地预览读取。
- 草稿基线只存在当前UI上下文，保存恢复不删已有结果事实；代价是明确离开决策，不承诺退出后恢复未提交文字。
- 改尺寸与其他返回统一捕获确认；代价是额外确认点击，换来明确下游失效边界。
- 通用输入 Scope 与精确业务审批身份独立；代价是控件/上下文生命周期维护，真实辅助技术客户端仍需验证。
- 旧服务结果只放弃展示采纳，不撤销已经完成的持久业务；代价是旧页面不显示其迟到结果，可重新加载查看真实记录。
- 新固定布局取代固定360列与旧绝对图片区域；代价是更改布局/Tab位置，用相对几何、保持的安全断言、旧→新矩形和PNG验证。
- 白墨页不重复尺寸/preflight卡片；代价是详细准备信息在相应页面看，保留白墨页最大尺寸摘要和下一页全部业务校验。
- 不扩大修复E1已知文化隔离/其他底层问题；代价是广筛可能仍FAIL，逐项列出而不授予全量PASS。
- 最终审核只由控制会话安排一次全新整体上下文；执行者不自行起审查者，不关闭Jira。

## NOT RUN 与边界

生产App启动、真实工作站 opt-in、物理键鼠注入/真实UIA客户端、真实多DPI/多显示器、真实美图/Photoshop、生产印刷TIFF/CMYK/W1、客户文件、外部上传、收费服务、真实新手中文走查、英文人工走查、发布/合并/推送均未执行。Window.Show与WPF原生窗口焦点遍历曾误执行三个测试，按上文单独记录，不能列为NOT RUN。源文件准备中的本地测试PDF不代表客户PDF或生产Photoshop验证。

SCRUM-11139/11154/11155 的未完成验收及旧缺陷保持原状态。后续E3–E6、底层流程设计、草稿持久化、其他仓库和发布需要另行授权。
