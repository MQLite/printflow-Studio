# AI 分支合入新版 UI：本地整合记录

日期：2026-10-29（Pacific/Auckland）。Initiative：PF-AI-UI-MERGE-20261029。

用户要求将 AI 功能分支并入更新后的 UI 分支，并先建立 Jira Tasks。本次只整合本仓库的现有代码和必要兼容修正；保留来源分支及工作树。

## Jira 与基线

三个 Task 已创建于 AI Epic [SCRUM-11156](https://yituoxx.atlassian.net/browse/SCRUM-11156)，未指定负责人、Sprint 或估算：

| 顺序 | Task | 范围 |
| --- | --- | --- |
| M01 | [SCRUM-11214](https://yituoxx.atlassian.net/browse/SCRUM-11214) | 基线、工作区、差异及验收范围核对 |
| M02 | [SCRUM-11215](https://yituoxx.atlassian.net/browse/SCRUM-11215) | 三方合并及兼容修正 |
| M03 | [SCRUM-11216](https://yituoxx.atlassian.net/browse/SCRUM-11216) | 新鲜回归与证据归档 |

Blocks 方向：M01 → M02 → M03。创建后读回核对了标题、Task 类型、父 Epic、标签及初始 To Do 状态。

- 目标分支：`codex/scrum-11203-copy-inventory`，`27fec015408e97b1089682f190daf25d9a84deed`。
- AI 来源：`codex/prompt-work-images`，`d0bcc0519c64ca596f9fd8dfec3a98b9c6db29b0`。
- 共同祖先：`de7bca6e0a8462584b4d6bf4d16307d1335e2fb3`。
- 目标工作区：`D:/Repositories/printflow-Studio`；来源工作区：`C:/Users/admin/.codex/worktrees/prompt-work-images/printflow-Studio`。开始时二者均干净。
- AI 侧相对共同祖先新增/修改 85 个文件；包括状态机、持久化、Prompt、附件快照、NativeHost/Probe、扩展和独立本地 MCP 接收器。

## 合并处理原则与预演

`git merge-tree --write-tree HEAD codex/prompt-work-images` 预演只报告 `OperatorWave1AHostTests.cs` 内容冲突。目标侧通过 AutomationId 找到实际审核按钮并检查引导页可见性；来源侧也使用精确 AutomationId，并修复 SyntheticHostData 的语言选择清理及其回归。解决时保留两者意图，不整文件选取任一侧。

实际解决保留目标的 `Shown(approve)` 祖先可见性检查和来源的 `ActualHeight > 0`；来源语言清理与新回归也全部保留。`Shown` 已覆盖按钮自身 Visibility，未重复相同断言。

自动合并的 `PrintDimensionsPreflightUiTests.cs` 同时产生 `FrameworkElement` 和 `UserControl` 两个展开详情辅助方法，并重复展开调用。目标侧已经适配最新引导 UI、白墨分支和持久化事实；去掉重复部分后该文件与目标 HEAD 完全相同。测试项目引用及锁文件同时保留 AI Probe/NativeHost 与 WPF-UI 4.3.0；相对目标锁文件只增加两个 AI 项目依赖块，包版本不变。

## 验证与证据

新证据独立放在被 Git 忽略的 `artifacts/test-results/ai-ui-merge-20261029-r1/`，不覆盖历史交付。开始时对目标分支已跟踪的 `docs/` 和 `output/` 共 1,819 个文件保存 SHA-256 指纹，收尾逐项核对。

- SDK：`dotnet 10.0.400`；Node：`v24.14.0`。
- 目标基线 `dotnet restore PrintFlowStudio.sln --locked-mode`：exit 0。
- 目标基线 `dotnet build PrintFlowStudio.sln -c Release --no-restore --disable-build-servers`：exit 0，0 警告、0 错误。
- 目标受影响测试基线：exit 1，中止前 15 passed / 0 failed；在 `ManualResultImportTests.Interrupted_recovery_offers_restart_manual_import_and_abandonment` 停滞，被 120 秒看门狗中止。`baseline-overlap.log/.trx` 保留，不标为通过。
- 停滞原因与来源 `70fa317` 的既有诊断一致：测试遗弃的挂起 Task 仍持有进程内 `SessionCompletionGate`，原服务等待该门锁。来源修正使用既有 `SimulatedProcessDeath.BeginRecoveredProcess` 并重新创建服务，仅修正测试的崩溃模拟。
- 合并候选 locked restore 与 Release build：exit 0，0 警告、0 错误。
- 精确恢复用例 `merged-exact-recovery.trx`：exit 0，1 passed / 0 failed / 0 skipped，823 ms。
- 扩展 Node 测试：exit 0，7 passed / 0 failed / 0 skipped。
- 本地 MCP 锁定安装与 Node 测试：exit 0，24 passed / 0 failed / 0 skipped。
- 受影响测试组 `merged-overlap.trx`：exit 0，40 passed / 0 failed / 0 skipped，35 秒。包含审核按钮、语言清理、印刷尺寸、启动与手动导入。
- 整批安全普通测试集 `merged-safe-ordinary`：exit 1，中止前 13,532 passed / 0 failed / 0 skipped，19 分 40 秒；120 秒看门狗在 `RecoveryUtilityRolloutTests.Return_confirmation_keeps_readable_neutral_heading_and_cancel_preserves_task` 运行时中止 testhost。此运行是 ABORTED，不写为 PASS。进程 WorkingSet 一度约 9.8 GB；退出后系统可用内存恢复约 10 GB。
- 上述恢复提示用例在新进程中精确复测 `isolated-recovery-confirmation.trx`：exit 0，6 passed / 0 failed / 0 skipped，12 秒。资源累积是整批中止的可能因素，未修改产品或测试逻辑。
- 使用同一过滤器完成测试清单发现：14,515 项，exit 0。显示名称有重复或截断，使用跨运行保持稳定的 TRX `testId` 汇总覆盖。
- 按未完成类建立 21 个补齐批次，每批新建 testhost，大型 UI 类单独运行，其他组约 65 项；测试过滤器和全部原有排除保持不变。21 批均已完成；第 12 批使用下面披露的复测结果。合并整批已完成部分与补齐批次，按稳定 TRX `testId` 去重汇总 **14,515 个测试 ID，最新结果全部 Passed，0 个未通过结果**。原始中止、失败和复测记录均保留，不宣称曾有一次未中断的整批全绿运行。
- 发现清单的显示名已逐项覆盖；唯一无参数占位名 `ProductionTiffInspectorTests.Any_nonproduction_fact_is_rejected` 在运行时展开，补齐批次中其 12 个参数场景均通过。`coverage.json` 保存各运行计数、显示名差异与 ID 汇总。

补齐第 12 批曾出现 1 项中文首页断言失败：`HomeReadinessSummaryTests.With_everything_expanded_the_notice_and_the_last_recovery_action_stay_reachable(language: "zh-CN")`，`CodeVisible` 为 false；其余 62 项通过，原 `completion-12.log/.trx` 保留。未修改产品或测试逻辑：精确独立复测 2/2 通过，原组 `completion-12-recheck` 63/63 通过。另以目标 `27fec01` 的 `git archive` 源码快照（不切换工作区、不改变分支）完成 locked restore、Release build（0 警告/错误）及同组对照 63/63 通过。该测试和相关 App/服务代码相对目标均未变；这是一项未定位原因的偶发可见性断言，不能把重跑通过写成已修复，也不能据此宣称目标基线必然失败。

测试保留 U09/C05 安全包装器的 11 项精确排除、全部 `PrintFlow.Tests.Smoke.` 排除，并单独排除 `WorkstationEntryPhase=Runtime`。采用 `--blame-hang-timeout 120s --blame-hang-dump-type none`。原始命令、过滤器、日志和 TRX 留在新证据目录；通过、失败、未运行分别报告。

完整安全普通测试集的命令为：

```powershell
$excluded = Get-Content output/ui/SCRUM-11213/c05-20261029-r1/checks/safe-ui-exclusions.json -Raw | ConvertFrom-Json
$filter = '(FullyQualifiedName~PrintFlow.Tests.)&WorkstationEntryPhase!=Runtime&FullyQualifiedName!~PrintFlow.Tests.Smoke.' + (($excluded | ForEach-Object { '&FullyQualifiedName!~' + $_ }) -join '')
dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj -c Release --no-build --no-restore --filter $filter --blame-hang --blame-hang-timeout 120s --blame-hang-dump-type none --logger 'trx;LogFileName=merged-safe-ordinary.trx' --results-directory artifacts/test-results/ai-ui-merge-20261029-r1 --verbosity minimal
```

Node 命令为 `node --test extensions/printflow-work/tests/bridge-client.test.mjs extensions/printflow-work/tests/protocol.test.mjs`；MCP 目录内运行 `npm ci --no-audit --no-fund`、`npm test`。

## 独立静态审查

独立只读代理 `/root/merge_review` 完成合并审查：Critical 0 / Important 0 / Minor 0，无代码层面阻塞。审查范围为双方共享/冲突文件、项目/锁/solution、产品 UI 保留及独立 MCP 依赖隔离；不重审来源全部既有实现，不代替动态测试。

父代理也通过 `git diff --cached --exit-code` 核对：App 和现有 Workflow/Services 与目标相同；AI Domain/Workflow/Infrastructure、NativeHost、扩展和 MCP 目录与来源相同。`git diff --cached --check` exit 0，无未解决 index 冲突。AI 侧两个 Jira JSON 没有 accountId/emailAddress 字段或邮箱字符串；本次新建 Jira 记录只保存事项元数据，不提交原始个人资料响应。

## 本地整合结论

合并候选通过 locked restore、Release build、40 项受影响回归、31 项 Node 测试、完整选择范围的分批覆盖核对及独立静态审查。收尾再次核对原有 1,819 个文档/证据文件哈希全部不变。产品和测试代码在上述验证后未再修改；本记录与最小 Jira 元数据随本地两父提交的合并提交保存。

保留的限制为整批资源/停滞观察及一次尚未定位的偶发 UI 可见性断言（独立、原组复测及目标组对照均通过）；没有通过删测试、降低断言或改变产品行为消除这些记录。以后运行大批 UI 渲染宜使用独立进程批次。本次合并不构成发布或生产验收。

## 能力边界

本次合并不等于 AI 页面、会话导入或真实 Work 端到端已完成。原 SCRUM-11158、11162–11167、11184 的未完成范围和依赖继续保留。现有中文首页仍提示 AI 设计暂不可用，不伪造生成、停止、候选或导入成功。

继续遵守 AI handoff 中 Chrome、MCP/Drive 结果交接及禁止浏览器下载的当前约束。独立 MCP 接收器没有接入产品依赖或安装包。保留三条既有路线、原件/InputSnapshot、审批和手势保护、精确身份、下游失效、300 PPI/CMYK/W1、环境门禁、自动化锁及不覆盖保存合同。

真实 Chrome/Work、实际目标用户、DPI/物理键鼠/读屏、生产输出、NativeHost 安装/注册、隧道及账号配置均不在本次自动化验证内。未推送、发布、部署或修改生产数据。
