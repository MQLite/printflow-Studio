# SCRUM-11173 E6 技术验证与真实验收准备

E6 沿用已批准的 v2 风格，补齐可复核的离屏可达性/布局证据，并修复两尺寸保存/完成页的图片挤压。**真实中文新手、实际外部窗口/DPI、物理输入和读屏仍 NOT RUN，E6 整体验收未完成。** 参与者可用性 UNKNOWN；没有把未收到回复写成“新人不可用”。本轮没有发布、部署、推送或生产操作。

工作树 `C:/Users/admin/.codex/worktrees/scrum-11173-guided-acceptance/printflow-Studio`，分支 `codex/scrum-11173-guided-acceptance`，BASE `65c5853e0e3f4f828b7851479bb418d043b0e44e`。产品/测试冻结检查点 `24f2b3bb18a6ae96f34fa39a572bd7a9dd9e8ebe`；之后提交仅文档和证据。候选构建时提交及文件身份见 [candidate](../../../output/ui/SCRUM-11173/candidate/README.md)，最终审查由控制会话另行记录，不能把本实现者的检查称作独立审查。

## 改动与因果证据

唯一产品改动在 `src/PrintFlow.App/Views/SessionScreenView.xaml`。两尺寸输出历史原位于图片下方的无界 Auto 行：1000×700 离屏 viewport 中图片仅1 DIP，1280×720约34 DIP。新增图片高度断言后，`e6-multisize-red` 有2失败/1通过。复用原输出 DataTemplate，Save/Completed 将历史放入现有右侧详情滚动区，先前审核页面保持原位置；名称、审批、位置、有效性事实与命令保持。相关 GREEN39/39、最终 Q01 45/45。

新增 `GuidedAcceptanceTests` 的9个参数化用例覆盖当前原图/审核/尺寸页的直接 WPF peer 名称与候选 Tab 控件集合、长中文原图，以及真实合成两尺寸保存/完成的图片和主操作几何、两个输出名称与滚动可达性。既有输入身份、按键余波、双击、隐藏/过期 provider 和取消测试继续执行。`GuidedCompletionVisualEvidenceTests` 只增加截图状态过滤；不会过滤其断言。

初次9/9 characterization和15/15视觉运行缺少多尺寸图片断言，不能算该问题已通过；初始14张图与JSON保留于 `visuals-before-fix`。另有一次测试编译错误（Shouldly重载）与3个新增完成夹具跨STA错误（42/45）均保留，后者采用既有E5的独立render snapshot模式修正；这些不是产品RED，也不宣称产品线程缺陷已修复。

## 执行结果

所有测试只经 [safe wrapper](../../../output/ui/SCRUM-11173/checks/run-safe-ui.ps1)，原11项精确排除与整个 `PrintFlow.Tests.Smoke.` 不变。只用合成GUID临时文件/SQLite、fake adapters、离屏UserControl和直接peer/routed events；未启用PRINTFLOW桌面开关、启动真实App、调用Window.Show/ShowDialog或操作外部软件。

| 运行 | 总数/通过/失败 | 解释 |
| --- | --- | --- |
| E6 broad baseline | 1485 / 1471 / 14 | 与E5接受的14项名称及原始消息完全一致 |
| e6-multisize-red | 3 / 1 / 2 | 实际图片空间丢失的断言RED |
| e6-multisize-green | 39 / 39 / 0 | 布局修复与原输出/保存/审核回归 |
| e6-q01-final | 45 / 45 / 0 | 最终定向运行与17张图 |
| e6-ui-architecture-final | 1494 / 1480 / 14 | 与E5和E6 baseline逐项相同，new/missing/changed均0 |
| e6-workflow-persistence-final | 277 / 277 / 0 | 完全相同E5筛选串，合成持久化回归 |
| controller-focused-final | 9 / 9 / 0 | 控制会话另跑全部新增用例，无截图开关；不是独立整体审查 |

各运行覆盖重叠，不能累加为独立测试数。广泛套件有旧失败，不能写全套PASS。[inherited failures](../../../output/ui/SCRUM-11173/inherited-failures.md) 列全部精确名称，[E5→final](../../../output/ui/SCRUM-11173/checks/e6-final-vs-e5.json) 与 [baseline→final](../../../output/ui/SCRUM-11173/checks/e6-final-vs-baseline.json) 均为ExactMatch=true，比较JSON保留原消息。[COVERAGE](../../../output/ui/SCRUM-11173/COVERAGE.md) 与 `checks/coverage-map.json` 将AC映射到最终TRX的精确测试实例，区分旧缺陷、旧验收缺口和本轮修复。

## 视觉与实际设备边界

[可视索引](../../../output/ui/SCRUM-11173/index.html) 包含17张已逐张打开检查的中文PNG及对应geometry JSON；[manifest](../../../output/ui/SCRUM-11173/visual-manifest.json) 记录34个SHA-256。长中文、两尺寸Save/Completed、crop、correction-waiting各三个viewport，另有readiness-blocked和save-uncertain最小viewport。图片与主操作可见；较小viewport的历史位于详情折叠线以下，已验证滚动可达。原截图、失败和后续修复证据分别保留。

这些是1000×700、1280×720、1920×1080 **UserControl viewport、96 DPI**。批准设计的1000×700是WPF**外部窗口**Width/Height；实际客户区更小且未测量。不能把这三张大小当成100/125/150%实测或批准外框客户区验证。九格实际矩阵、物理Tab/焦点、Enter/Space/鼠标、系统对话框和读屏均待负责人执行。

## Q01–Q03状态及下一门槛

| 子项 | 本轮可审阅成果 | 仍未完成 |
| --- | --- | --- |
| Q01 / 11195 | 离屏中文布局、直接peer/候选Tab集合、身份/输入回归与因果布局修复 | 实际外框/客户区、DPI、物理输入和读屏；不能整体标Done |
| Q02 / 11196 | 三路线与异常、身份/快照/锁/TIFF合同的合成回归及精确继承失败核对 | 14旧失败及N1–N4/F6/PNG/generic recovery等仍开放；不等于生产验收 |
| Q03 / 11197 | 构建与静态候选、12条中性中文A01–A12任务卡、匿名观察/缺陷/视觉表 | 真人走查、负责人视觉签收及运行host准备均NOT RUN；整项未完成 |

[中文走查包](../../printflow/guided-acceptance-walkthrough.md) 保留11155场景映射、空观察记录、独立完成/求助/误解/原话字段；没有造参与者、结果或耗时KPI。[维护说明](../../printflow/guided-acceptance-maintenance.md) 提供安全重现方式。[rulings](../../../output/ui/SCRUM-11173/RULINGS.md) 说明边界和代价。

候选构建提交为d21c14497697ead627386a5f0ac3f39a7bb4ea60；Build退出0、0警告/0错误；Validate退出0，STATIC_VALIDATED且GraphConstructed=false，runtime root不存在；809源码/资源与54构建产物的863条哈希已复核。候选仅允许静态Build/manifest/Validate。本轮不执行构造Application/MainWindow的PrepareAndSmoke，也不执行Interactive。负责人另行取得隔离工作站许可、核实prepared record和场景ledger后才可启动；N4假PNG改名TIFF、F6尺寸错配、KeepOriginalExtent→PNG审批、generic recovery及PDF fixture缺口不得用预制结果绕过。未被当前Interactive链证明安全的完整TIFF走查仍BLOCKED。

N1局部Recent投影、N2诊断列表CLR名、N3长小数、N4、F6、PNG、generic recovery、早期E4按键异常及E5 M1误导busy文案的状态逐项保留，没有因为窄测通过关闭旧Epic。建议先审阅技术候选和未解决项，再由负责人完成当前候选准入、真实中文新手及工作站验收；这些动作也不构成发布或生产授权。
