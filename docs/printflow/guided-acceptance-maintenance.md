# 引导界面验收证据维护（SCRUM-11173）

该文档说明怎样重现和解释 E6 证据。原业务状态机、适配器、持久化和生产合同不由 E6 修改。批准的视觉参考是 SCRUM-11168 v2，用户已接受 E5 并授权继续 E6；不能用验收任务重新设计产品。

## 自动执行边界

每次测试必须通过 [run-safe-ui.ps1](../../output/ui/SCRUM-11173/checks/run-safe-ui.ps1)。它保留 E5 的 11 个精确排除项与整个 `PrintFlow.Tests.Smoke.` 命名空间；不要运行未过滤 `dotnet test`，不要设置 `PRINTFLOW*` 可见测试开关。允许 GUID 临时合成 PNG/TIFF、SQLite、fake adapters、离屏 WPF UserControl 和直接 peer/routed-event 测试。禁止真实 App/Window.Show、操作桌面、生产文件、外部软件与客户数据。

```powershell
# PowerShell 7 (pwsh)，仓库根目录；原始日志和 TRX 自动写到 checks。
& output/ui/SCRUM-11173/checks/run-safe-ui.ps1 -RunName e6-q01 -Include 'FullyQualifiedName~GuidedAcceptanceTests'
& output/ui/SCRUM-11173/checks/run-safe-ui.ps1 -RunName e6-ui-architecture-final
# 持久化 Include 从 e6-workflow-persistence-final-filter.txt 的前段选择复用；
# 仍由 wrapper 追加同一排除集，不直接执行筛选外测试。
```

已有安全 UI 套件包含旧失败，不能仅依据退出码判定新回归，也不能把整个套件写成 PASS。用 [compare-failures.ps1](../../output/ui/SCRUM-11173/checks/compare-failures.ps1) 比较唯一测试名称与原始错误消息；名称相同而消息改变仍需调查。通过数量增加只能说明相应测试通过，不自动关闭 N1–N4、F6、PNG/恢复缺口或早期 E4 输入不稳定。

## 新增覆盖与证据性质

`GuidedAcceptanceTests` 补三种当前页面（原图、审核、自定义尺寸）的直接 WPF peer 名称和候选 Tab 控件集合，检查关键操作可达、隐藏步骤不混入、没有默认按钮。它不驱动 Windows Tab 焦点，不证明实际读屏；隐藏/过期 provider、按键余波与双击的动作安全复用既有 GuidedSessionInput/EditorInput、GuidedImportRoute/Draft 和 GuidedUtilityReturn 测试。

本轮发现两尺寸保存页的无界输出历史列表挤占图片：1000×700仅1 DIP，1280×720约34 DIP。RED后作XAML修复：复用同一输出模板，审核阶段保持原位置，保存/完成阶段把输出记录放到现有右侧滚动区。业务资格和命令未变；原有输出名称/审核/位置断言保留。新增测试同时验证两个输出元数据可滚动到达且不再挤占图片。

另有长中文任务名/合成原图与两个尺寸的保存、完成场景，覆盖三种离屏 UserControl DIP viewport，断言实际 WPF 几何、滚动可见范围与图片区域。截图为 96 DPI `RenderTargetBitmap`，不换算成“125%/150%已通过”。文字输入超出框宽允许水平滚动；完整长值仍需真实输入/读屏验收。批准1000×700等外部窗口尺寸下的实际客户区可能更小，本轮未测量，离屏viewport不能替代。实际九格窗口/DPI矩阵和主持人记录在 [walkthrough](guided-acceptance-walkthrough.md)。

视觉产物可通过 `PF_11173_CAPTURE_DIR` 输出新场景；复用 E5 视觉 fixture 时使用 `PF_11172_CAPTURE_DIR` 和逗号分隔的 `PF_11172_CAPTURE_STATES` 选择采集状态；E4 使用 `PF_11171_CAPTURE_DIR`/`PF_11171_CAPTURE_STATE`。这些开关只写离屏 PNG/JSON，不授权桌面输入。捕获后取消环境变量；保留原始 TRX，逐张视觉检查并生成包含 PNG/geometry SHA-256 的 index/manifest。

## 故障处置与维护门槛

独立审查暂缓Minor E6-M1：当前候选重建helper会覆盖固定candidate证据目录。现有候选保持不变；下次重建前先修正候选专属证据路径与存在性保护，验证原包保留，不能仅换CandidateId便重用脚本。[审查和裁定](../codex/printflow-operator-ux-backlog/SCRUM-11173_REVIEW.md)保留具体影响。本轮用户选择先准备材料、真人测试稍后安排。

- 新增可复现 App 缺陷：先运行证明错误的测试，再作最小 App 修复，保留 RED/GREEN 和相关回归；不要修改已有断言以掩盖问题。
- 发现底层 TIFF、审批谱系、恢复或生产适配器问题：保留精确证据并交关联任务；E6 不越界修业务层。
- 新的 UI/状态交互修改后重新评估受影响截图和定向测试。源码冻结后只执行一次广泛 UI/Architecture 与同范围持久化；新变化、失败或未解风险才扩大重跑。
- 工作站/人类证据填写实际设备、候选、参与者和观察；未经执行不得预填 PASS。新的任务和 owner 处置不改变 SCRUM-11155 的历史记录。

## 路由与审查

路由 v2.4，RouteOffset=0。Native 顺序执行；Q01 UI 与视觉检查要求 Astra High。执行上下文经控制会话核实为 `gpt-6-astra/high`，2026-10-22T00:31:56.297Z。后续独立 Q02 测试策略/证据分析正常为 Sol High，机械文档正常为 Luna Low；本上下文没有实际切换接口，记录 `MODEL_SWITCH_UNAVAILABLE` 并以安全的较强上下文继续，不宣称已降级。仅在实际独立审查边界由控制会话发起一次全新整体审查；不为每个任务派审查。保留计划 scratch/ledger 供复核。
