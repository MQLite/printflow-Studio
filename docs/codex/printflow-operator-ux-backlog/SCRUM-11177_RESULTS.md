# SCRUM-11177 执行结果

日期：2026-10-19（Pacific/Auckland）。范围：获批五项 Native 计划，WPF 共享主题、六类展示组件、首页/路线页接入及合成离屏证据。独立整体审查尚待控制会话执行；本文件不表示合并、发布、工作站或 Epic 验收通过。

## 实现与原始验收条件

| 条件 | 交付与证据 |
| --- | --- |
| 集中 ResourceDictionary / 变量 | GuidedTokens、GuidedControls、GuidedComponents、GuidedTheme；App 合并，MainWindow 显式采用字体和颜色；类型和值由 GuidedThemeResourceTests 检查 |
| 六类组件 | 页面标题、任务卡、状态提示、表单区、操作栏、只读步骤条；[用法文档](../../printflow/guided-theme-components.md) |
| 不自行授权或启动业务 | 真实 ReviewApprovalButton 的旧按键释放、连击和身份变化检查；步骤条无命令/可执行导航；两实页原 ICommand 实例、参数、警告、确认和 AutomationId 保留 |
| 代表页与状态验证 | 五种组件状态 × 三个 DIP 视口；两个实页 × 三个视口；绑定错误、长中文、底部操作、滚动可达性及折叠详情 UIA 面检查 |

产品文件仅限 `PrintFlow.App` 的主题、展示记录、App/MainWindow XAML 与两个 View XAML。没有修改 ViewModel、页面 code-behind、SessionScreenView、Domain、Workflow 或 Infrastructure。Home/WorkflowSelection 的命令、参数、TargetIdentity、IsGestureGuarded、AutomationId、IsDefault、IsCancel 属性值与基线逐项一致，见 [identity-comparison.json](../../../output/ui/SCRUM-11177/identity-comparison.json)。

本轮是主题与组件接入。完整原型中的侧栏导航、选择后再确认、真实步骤投影和其他业务页面属于后续 E2～E5；没有把 catalog 伪装为生产工作流。

## 执行上下文与提交

- 工作区：`C:/Users/admin/.codex/worktrees/scrum-11177-guided-theme/printflow-Studio`；分支 `codex/scrum-11177-guided-theme`；基线 `de7bca6e0a8462584b4d6bf4d16307d1335e2fb3`。
- 规则：全局 development-routing v2.4，RouteOffset=0。UI 路由计划/请求/实际均为 `gpt-6-astra / high`；实际值由父会话核对执行者 JSONL `turn_context`（2026-10-19T00:45:21Z，session `01a1519e-bae4-7662-be6e-619dc95921a2`）。
- 机制：单个隔离 Native 执行子代理顺序实施五任务，未拆任务代理或自行派审查者。中途只有上下文压缩；UI/模板/视觉 QA 紧密耦合，边界复评后保持 Astra/high。最终由控制会话安排全新上下文整体审查。
- 执行主机：本地 Windows / PowerShell / .NET SDK 10.0.400；离屏测试在 STA 中运行，没有 Window.Show、生产 App 启动、桌面输入或工作站 opt-in。
- Task 1 `097b54b`：共享变量与控件；Task 2 `4ea5697`：组合组件；Task 3 `d383ee4`：只读步骤；Task 4 `037f277`：两页接入；Task 5 为本文件所在证据提交。
- 未 push、merge、deploy；未改 Jira 状态、评论或关闭任务/Epic。原 `D:/Repositories/printflow-Studio` 的无关变更保持原样；约 70 MB 原型材料只读，不纳入提交。

## 验证记录

所有命令在上述工作区执行，测试项目为 `tests/PrintFlow.Tests/PrintFlow.Tests.csproj`。原始日志与 TRX 位于 [checks](../../../output/ui/SCRUM-11177/checks/)。

| 检查 | 通过 / 失败 / 跳过 | 结论 |
| --- | --- | --- |
| 改动前 UI + Architecture 基线 | 1228 / 15 / 1（1244） | 既有失败；不是全量 PASS |
| Task 1 首次 RED → 资源验证 | 0 / 4 / 0 → 4 / 0 / 0 | 缺少主题字典 → PASS |
| Task 2 首次 RED → 组件验证 | 1 / 6 / 0 → 7 / 0 / 0 | 样式/catalog 缺失 → PASS |
| Task 1+2 与审批/焦点回归 | 34 / 0 / 0 | PASS；其中 OperatorWave1 + OperatorWave1AFocus 23 项 |
| Task 3 RED → 步骤及组件验证 | 0 / 4 / 0 → 11 / 0 / 0 | 缺少步骤模板 → PASS；初始缺类型另有编译失败记录 |
| 两页接入前目标基线 | 181 / 0 / 0 | PASS；与广筛中的文化选择泄漏分别记录 |
| Task 4 首次 RED → 接入验证 | 0 / 5 / 0 → 5 / 0 / 0 | PASS |
| Task 4 首次原有回归 | 177 / 4 / 0 | 新的恢复列表布局失败；已修复，未归入基线 |
| Task 4 最终接入 + 原有回归 | 186 / 0 / 0 | PASS，含上述 4 项修复 |
| Task 5 布局及截图 | 18 / 0 / 0 | PASS；15 个组件案例 + 3 个成对页面案例，21 张图 |
| 最终构建 | 0 警告 / 0 错误 | PASS |
| 最终 UI + Architecture | 1267 / 14 / 1（1282） | FAIL；0 项新增失败，38 项新增 Guided 测试全部 PASS |

Task 5 首次运行 15 失败 / 3 通过，原因是新测试假设 native Expander 的折叠内容必有折叠视觉祖先。WPF 也可以完全不物化该内容；测试现检查“无视觉父级或折叠祖先”，并检查公开 Expander UIA 子树中不存在隐藏操作。这是测试修正，不宣称修复了产品缺陷。截图生成在新布局断言通过之后执行，未替代断言。

最终广筛耗时5分21秒。按精确测试名称比较，14项失败全部属于原15项基线失败，见 [baseline-comparison.json](../../../output/ui/SCRUM-11177/checks/baseline-comparison.json) 与 [基线失败详情](../../../output/ui/SCRUM-11177/checks/baseline-nonpass.json)。遗留分布为 PrintDimensionsPreflightUiTests 5项、ManualCropAdjustmentUiTests 2项、KeepOriginalExtentUiTests 3项、HomeRecentAccessibilityTests 2项、OperatorWave1AHostTests 非可见 smoke 1项、OperatorCultureIsolationTests 1项。没有删除、跳过或放宽它们。

基线的 `FinalSaveUiTests.Tiff_shows_backend_physical_size_keeps_older_size_selectable_and_late_results_stay_with_their_item` 本次未复现；没有改动该功能，不宣称已修复。HomeRecent 的2项在单独181/186项子集通过、在广筛失败，保留其文化选择隔离背景。广筛之后仅将截图用的微小透明样本换成既有 bordered-alpha fixture，使透明边缘容易辨认；产品代码未改，最终截图测试再次18/18通过。

精确命令：

```powershell
dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --filter FullyQualifiedName~GuidedThemeResourceTests
dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --filter FullyQualifiedName~GuidedComponentTests
dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --filter 'FullyQualifiedName~OperatorWave1Tests|FullyQualifiedName~OperatorWave1AFocusTests'
dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --filter 'FullyQualifiedName~GuidedStepNavigationTests|FullyQualifiedName~GuidedComponentTests'
dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --filter FullyQualifiedName~GuidedThemeAdoptionTests
dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --no-build --filter 'FullyQualifiedName~GuidedThemeAdoptionTests|FullyQualifiedName~HomeAndWorkflowSelectionTests|FullyQualifiedName~HomeReadiness|FullyQualifiedName~HomeRecentAccessibilityTests|FullyQualifiedName~HomeAbandonConfirmationTests|FullyQualifiedName~WorkflowPurposeCardTests|FullyQualifiedName~WorkflowSelectionAccessibilityTests|FullyQualifiedName~LocalisationResourceTests' --logger 'trx;LogFileName=scrum-11177-adoption-final.trx'
dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --filter FullyQualifiedName~GuidedThemeVisualEvidenceTests
dotnet build PrintFlowStudio.sln --no-restore
dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --no-build --filter 'FullyQualifiedName~Integration.Ui|FullyQualifiedName~Architecture' --logger 'trx;LogFileName=scrum-11177-ui.trx'
```

接入前基线使用上面的 Home/Workflow/Localisation 子集但不含 GuidedThemeAdoptionTests。广筛基线使用相同 UI/Architecture 过滤器，原始文件为 `scrum-11177-baseline.trx`。捕获仅临时设置 `PF_11177_CAPTURE_DIR`，finally 恢复原值。没设置该变量时仍执行布局断言，截图本身为 NOT RUN。

## 视觉证据与限制

[截图索引](../../../output/ui/SCRUM-11177/INDEX.md) 将原型与实际 WPF 对照；每张图旁的 JSON 记录 state、zh-CN、DIP、96 DPI、命令状态、布局测量、截图 SHA-256，两个页面共享同一合成源 SHA-256。`synthetic-source.png` 是测试生成的透明边缘样本，没有客户文件。

15 张组件图检查五种状态的标题/文字、表单、步骤、主操作与停止入口；小视口加载状态内容滚动，操作栏保留。两个页面在小窗口按既有滚动语义可达；Home 截图记录已滚动至导入按钮的状态，顶部环境卡可能位于滚动视口之外，不代表其被隐藏或删除。路线页保留长列表滚动和固定返回动作。

所有截图是离屏实际 WPF 渲染，三个尺寸为 1000×700、1280×720、1920×1080 DIP，96 DPI 下像素数与 DIP 相同。没有把图片缩放当成 125%/150% PerMonitorV2。加载控件属性确认 IsIndeterminate=true、无百分比；静态离屏帧不证明动画运行。焦点仅有模板/合成事件验证，不是物理键盘验收。

NOT RUN：真实窗口与物理键盘/屏幕阅读器、真实高 DPI、多显示器移动、中文新手走查、英文人工走查、生产预设和实际 Meitu/Photoshop、客户图片、生产审批/保存、工作站 opt-in。`OperatorWave1AHostTests.Operator_launched_visible_synthetic_host` 继续跳过。

## Rulings

1. 使用已安装 Git Bash 执行 task-start/task-done，输出放进本计划 scoped ledger；无需修改脚本或全局配置。风险：Windows 路径适配若出错会影响流程记录，不影响产品。
2. 五任务共用主题/模板/视觉上下文，单执行者持续 Astra/high。成本：UI 执行资源较高；没有虚称降级。
3. 跨合并字典的 token 在消费控件上使用 DynamicResource，同字典样式引用使用 StaticResource。原因：无 Application 的独立视图曾读到 UnsetValue；成本：普通动态资源查找。
4. 已记录 15 项既有基线失败后按批准范围继续，不扩展到 Session / 文化隔离修复。成本：遗留失败保持未解决。
5. 实页集成暴露 Header 为 null 时的可访问 Name 绑定错误；共享命名样式使用 TargetNullValue 空字符串。本地棋盘格旧键用 DrawingBrush 引用共享 drawing，避免 compiled-XAML 直接别名取到相邻 converter。成本：Task 4 多触及一个共享主题文件，保留一个兼容旧键适配。
6. 42-DIP 控件使旧上区分配只给恢复列表约 120 DIP，导致 4 项新回归。改用剩余高度 3:2 的 XAML 行分配，保留原 code-behind 上限与业务逻辑。成本：矮窗口上区需要更多滚动。

计划/交接：[批准计划](../../superpowers/plans/2026-10-19-scrum-11177-guided-theme.md)、[执行交接](SCRUM-11177_HANDOFF.md)。运行账本：`.superpowers/sdd/2026-10-19-scrum-11177-guided-theme/progress.md`；保留待独立审查，不清理工作树。
