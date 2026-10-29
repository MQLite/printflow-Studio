# SCRUM-11172 E5 实现与验证

本轮在批准的 v2 视觉方案上完成 F01–F05 的 App 层实现及唯一一次独立整体审查，等待用户验收。审查为 0 Critical、0 Important、1 Minor 暂缓，详见 [REVIEW](SCRUM-11172_REVIEW.md)。分支 `codex/scrum-11172-guided-completion`，基点 `209d0b3a6442aac1f901864107b94c5e6a0cb52d`，审查源码提交 `41da7d4aeef33957cf5fb23f250526221f6f2822`；工作目录为 `C:/Users/admin/.codex/worktrees/scrum-11172-guided-completion/printflow-Studio`。执行为一个 Native 顺序实现者，没有按子项派发审查或第二实现者。

原始 Epic/子项见 [JIRA_SCOPE](SCRUM-11172_JIRA_SCOPE.json)。历史规划文案的待实施状态由用户后续明确继续执行授权取代；本记录不把实现或离屏测试等同用户验收。

## AC 对应

| 条件 | 实现与证据 |
| --- | --- |
| F01.1 最终审核→保存，文件名/目录可见 | Approved PNG preparation 归入 Save；Save/Completed 增宽详情列，表单置于冗长说明前；GuidedCompletionSaveTests 的 PNG 过渡与最小窗口输入断言，三尺寸 save PNG |
| F01.2 批准/保存/完成独立 | Completion 投影读取既有 FinalSaveDisplay；10 个保存用例涵盖批准/未保存/已保存/完成交叉、保存中、冲突与不确定；completed/uncertain/collision PNG |
| F01.3 冲突/取消/重试/打开 | 复用原 coordinator、精确身份、选择取消与核实命令；冲突保留已有文件，选取取消不改身份；原 FinalSave/SaveOwnership 回归 |
| F01.4 多尺寸各自状态 | 每个 FinalSaveTargetRow 独立 DescribeFinalSave；Each_size_retains_its_own_review_and_delivery_summary，原持久化 AddAnotherSize/FinalSaveCoordinator 选择 |
| F02.1 准备→交接→等待→导入→重审 | RecoveryGuidance 与 correction prepare/wait/returned 图；四阶段测试保持原审批/修订身份，导入后必须重新审核 |
| F02.2 Meitu/Photoshop 支持边界 | fake Meitu takeover 与 Photoshop failure/review handoff 三分支；Photoshop 明确无手动 TIFF 回导，无自动文件侦测 |
| F02.3 合法恢复动作/原输入 | 下一步只列当前 Can* 提供的动作；恢复停止页断言无非法重试/导入建议，原件/身份保持 |
| F02.4 真实步骤/无隐式重启 | reopened 与 returned 状态测试；reentry 后步骤 Waiting 或仍需审核，attempt/review 未增加，恢复不授予批准 |
| F03.1 状态来自真实值 | RecentSessionRow 的停止、未知值中性翻译及状态徽标/下一步；两个语言的状态映射测试；recent 三尺寸图 |
| F03.2 继续正确/历史只读 | Recent_resume_projects_real_guided_page_and_history_is_read_only：继续到真实 Review，完成历史无批准/运行资格，数据库不变 |
| F03.3 历史不等于当前文件 | 真实合成保存后移除测试目标文件，历史标签继续限定为记录；原列表移除语义未变 |
| F03.4 枚举/N1 来源 | 未知值 fallback 仅限 RecentSessionRow；没有全局文化行为改动。继承失败逐项比较；未把窄范围通过宣称 N1 已修复 |
| F04.1 未检查/已检查/失效/阻塞 | ObservationSummary 和逐行 EvidenceSummary 读取既有 lifecycle；不新增时效阈值，不改变 Verified；五状态用例及 blocked/stale/unchecked 图 |
| F04.2 设置分组/校验/资源 | 常用语言、裁切边、日志保留；生产只读；本地路径技术披露；非法 retention 不写入，校验与输入仍可见；zh/en 保留 |
| F04.3 返回来源/草稿 | 新 App 单一来源任务 lease：暂停旧动作，返回先重载，仅同身份保留支持的草稿；Settings→Readiness 保留来源；failed/late load 与 stale utility action 测试 |
| F04.4 显式诊断 | 打开/刷新只读；仅显式 RunLiveChecks 调用 fake seam；旧 utility、pending return 不能运行；无工作站配置改动 |
| F05.1 就地反馈 | 复用既有加载/无预览/校验/保存事实；引导卡、状态徽标、错误技术披露共用主题。旧空状态/无预览失败不伪装修复 |
| F05.2 原因/保留/真实下一步 | ActionPreservedText 根据重载成功/失败限定保留事实；错误码独立可查，技术详情默认折叠；failure/error-details 图 |
| F05.3 当前确认/取消无副作用 | 同 context 刷新也清除旧 draft-leave intent，但保留合法草稿；旧 discard callback 不导航；return/abandon 原身份守卫回归 |
| F05.4 并存事实 | uncertain save 与 history failure 同时可见；diagnostic unavailability 不遮住 failure；无 toast 替代关键阻塞 |

## 行为验证

所有测试都经 [run-safe-ui.ps1](../../../output/ui/SCRUM-11172/checks/run-safe-ui.ps1)，使用原 11 项排除及整个 Smoke 命名空间排除。未增加排除，没有设置 PRINTFLOW opt-in。没有真实 App 启动、Window.Show/ShowDialog、桌面输入、Meitu/Photoshop、客户或生产数据。仅合成临时文件、GUID 临时 SQLite、fake adapters 与离屏 UserControl。

| 运行 | 通过/失败 | 说明 |
| --- | --- | --- |
| e5-baseline | 536/0 | 实施前相关范围基线 |
| e5-f01-red-behavior → e5-f01-green | 0/8 → 64/0 | 三类独立状态、每尺寸状态 |
| e5-f01-transition-red → e5-f01-transition-green | 8/2 → 66/0 | PNG 页面过渡、保存输入下折的行为 RED→GREEN |
| e5-f02-red-corrected → e5-f02-green-final | 6 缺失指引失败及 1 夹具错误 → 411/0 | 修正/接管/恢复，8 个新用例；夹具错误单列 |
| e5-f03-red → e5-f03-green | 2/3 → 58/0 | 3 个新映射失败，2 个已有正确行为；最终含 5 新用例 |
| e5-f04-red | 1/6 | 缺失状态摘要/披露，显式诊断已有边界通过 |
| e5-f04-resumed-final | 154/0 | 7 个呈现用例及 4 个 utility return 用例；导航 RED 和失效动作/重载 busy RED 见 ledger |
| e5-f05-red-behavior → e5-f05-green | 1/5 → 125/0 | 4 个真正缺口；1 个 history warning 控件选择夹具错误；1 个原 return 守卫已通过 |
| e5-visual-final | 11/0 | 3 个窗口尺寸和 8 个恢复用例，31 张最终截图 |

上述运行覆盖重叠，不能累加为独立测试数。编译/夹具/跨 STA 错误不冒充行为 RED；被用户中断且无 TRX 的运行不计为通过。F02 曾挂起的 fake Photoshop 等待夹具仅终止自身测试进程树，原始记录保留。旧 Settings diagnostic-path 测试明确展开新的高级披露后继续全部原 copy/read-only/geometry 断言；没有删除旧断言或扩大排除。

最终完整回归与精确继承比较在文末记录。

## 视觉、裁决和维护

[INDEX](../../../output/ui/SCRUM-11172/INDEX.md) 列出 31 张中文最终 PNG/geometry JSON 和逐张检查；[visual-manifest](../../../output/ui/SCRUM-11172/visual-manifest.json) 保存 SHA-256。7 个主要状态各 3 个尺寸，最小窗口另有修正准备/重审、冲突/不确定、确认、失败/详情和环境阻塞/失效/未检查。主动作与关键输入有边界断言；长内容保留滚动。

主要裁决及出错代价见 [RULINGS](../../../output/ui/SCRUM-11172/RULINGS.md)，顺序记录见 [ledger](../../../output/ui/SCRUM-11172/checks/execution-ledger.md)。维护接口、导航 lease、独立状态和验证边界见 [维护说明](../../printflow/guided-completion-recovery-utilities.md)。

没有改 Domain/Workflow/Infrastructure 或业务持久化。没有 merge/push/deploy/publish、Jira 评论、生产变更、后续 Epic 或其他工作树写入。控制会话原始 evidence 文件按原样保留。scratch 保留。

继承 F6/N1–N4 与 E4 两个早期按键激活零次现象仍保持原状态；没有根因证据便不声称修复。合成 TIFF 的微小图像/元数据差异不能证明尺寸生产正确性。真实键盘、UIA 设备、DPI、真实工作站、中文新手走查和生产输出：**NOT RUN**。

路由为 Windows/local、development-routing v2.4、RouteOffset=0。控制会话核实 Native 实际 `gpt-6-astra/high`；规划后及主要上下文边界均按 UI/交互风险重新评估。纯机械文档/持久化验证本应较低路由，但本上下文没有原位切换能力，记录 `MODEL_SWITCH_UNAVAILABLE`，未虚称降级或另派实现者。

## 最终完整回归

`e5-ui-architecture-final.trx`：**1485 项，1471 通过、14 失败、0 跳过**。与控制会话保存的 E4 接受基线逐项比较，**14 项名称与原始错误消息完全相同；新增 0、消息变化 0、未复现 0**。[精确比较](../../../output/ui/SCRUM-11172/checks/e5-ui-architecture-final-comparison.json) 保留完整分类。

继承项为 PrintDimensionsPreflightUi 5 项、KeepOriginalExtentUi 英文 3 项、ManualCropAdjustmentUi 英文 2 项、HomeRecentAccessibility 2 项、OperatorCultureIsolation 1 项及 OperatorWave1AHost.Nonvisible_seed_and_real_command_persistence_smoke 1 项。两项 HomeRecentAccessibility 在 F03 窄范围中通过，但最终广筛仍同名同消息失败，因此没有将 N1 或文化隔离归因为本轮修复。

此完整筛选包含 E4 修正后新增 4 个用例及本轮新增 43 个用例；与基线相比总数增加 47。所有新增用例通过。保存/恢复/utility/确认呈现及最后两行 correction 样式变化均包含在此次最终测试源码中。[源码哈希](../../../output/ui/SCRUM-11172/checks/tested-source-hashes.json) 用于核对后续提交未改变测试对象。

`e5-workflow-persistence-final.trx`：**277/277 通过，0 失败、0 跳过**。完整筛选文本与 E4 接受选择完全相同；原 11 排除逐字相同，Smoke 命名空间继续排除。[范围核验](../../../output/ui/SCRUM-11172/checks/final-scope-verification.json) 和原 filter/log/TRX 均保留。最终源码 diff 检查通过，测试后未再改产品或测试源码。

技术实现与证据已准备好交由控制会话进行唯一一次独立整体审查。没有将该状态标作用户验收通过或全套测试全绿；14 项继承失败及 NOT RUN 边界仍有效。

## 控制会话收口

SCRUM-11172 与 11190–11194 已转为 **In Review**，等待用户验收；回执为 `output/ui/SCRUM-11172/checks/jira-final-status.json`。唯一独立整体审查为 0 Critical、0 Important、1 Minor 暂缓。轻微项是环境页返回任务时的短暂忙碌文案误称检查工作站；没有实际启动检查，本轮不声称已修复。

控制会话新鲜复验 **48/48 通过**；源码/测试保持审查提交 `41da7d4aeef33957cf5fb23f250526221f6f2822`。31 张图片与清单及各自元数据哈希一致；主检出的 13 个已修改跟踪文件、5 个预存未跟踪文档和 Git 状态均与启动快照一致。原有 14 项广筛失败仍同名、同消息。详细审查、裁决与代价见 `docs/codex/printflow-operator-ux-backlog/SCRUM-11172_REVIEW.md`，最终核对见 `output/ui/SCRUM-11172/checks/controller-final-verification.json`。未合并、推送、部署、关闭旧缺陷或启动后续 Epic。
