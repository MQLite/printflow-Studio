# WPF UI 首页与保存页样板

2026-10-22。使用 WPF-UI **4.3.0**，沿用批准的浅色、青绿色原型。实现位于工作树 `C:/Users/admin/.codex/worktrees/scrum-11173-guided-acceptance/printflow-Studio` 的 `codex/wpf-ui-sample-pages` 分支；基于 `2561dfd127e3336ec57b37fb3fe47709c1d81fcb`，当前为本地未提交改动，没有合并、推送、部署或安装。

## 查看图片

- [可切换尺寸的原型／实现对比页](index.html)
- [首页并排对比](home-comparison.png)
- [PNG 保存页并排对比](save-png-comparison.png)

| 控件视口 | 首页实现 | 保存页实现 |
| --- | --- | --- |
| 1000 × 700 | [首页](screenshots/home-1000x700.png) | [保存](screenshots/save-png-1000x700.png) |
| 1280 × 720 | [首页](screenshots/home-1280x720.png) | [保存](screenshots/save-png-1280x720.png) |
| 1920 × 1080 | [首页](screenshots/home-1920x1080.png) | [保存](screenshots/save-png-1920x1080.png) |

六张图片均已逐张打开检查。并排图只按比例缩放排版，没有修饰界面。左侧是批准的 `01-home.png`／`44-png-save.png`，右侧是实际 WPF 实现。花朵是代码绘制的确定性测试素材，经过真实合成导入、跳过可选处理、裁切、审核和批准 PNG 准备；日期和目录来自测试夹具。没有把原型当成产品截图。

这些是 **96 DPI 的 UserControl 离屏截图**，不是完整外部窗口、物理键盘、读屏或 125%／150% DPI 的工作站验收；没有运行生产应用、Photoshop 或美图。模拟提示保留。PNG 截图不代表 TIFF 或打印输出验收。

## 改动

首页：增加左侧品牌与设置入口，集中导入区，WPF UI 卡片／图标／按钮模板，紧凑环境摘要和继续任务区域。保留选择文件、拖放、取消、继续、放弃确认和恢复命令。恢复区放在任务列表末尾，让滚动到底可到达最后一个恢复动作。

保存页：仅在 `HasFinalSave && GuidedPage == Save` 时实例化 WPF UI 保存模板；保留原来的协调器资格条件。使用 Fluent 输入框、紧凑步骤栏、当前结果的大预览和右下角保存动作。文件名、目录、多尺寸目标、保存记录、取消、核实、另存和错误信息继续绑定现有 ViewModel。长目录保持完整的 Value／复制能力，可通过悬停或键盘横向查看。

`ReviewApprovalButton`、`TargetIdentity`、原命令及参数、AutomationId、手势保护和中性焦点机制保留。`SharedReviewSurface.ResultOnly` 只控制展示，不修改比较模式、背景、缩放或平移状态；结果窗格使用 After 身份。联合确认保存按钮仍位于原审核动作组。没有改动 Domain、Workflow、Infrastructure、数据库或外部自动化代码。

主题资源局限于样板页面，没有把 WPF UI 隐式主题合入全局 Application。现有 `PF.Button.Primary`／`PF.PageHeader` 页面资源合同继续保留。依赖锁文件已更新。

## 验证结果

| 检查 | 结果 |
| --- | --- |
| 改动前的首页／保存定向基线 | 19 / 19 通过 |
| 最终兼容性回归 | **171 / 171 通过，0 失败** |
| 最终 App 构建 | **0 警告，0 错误** |
| 差异空白检查 | 通过 |
| 截图 | 两页各三个视口，共六张；已逐张检查 |

最终回归覆盖合成 PNG 准备、保存资格和状态、多尺寸／长中文、无协调器时不实例化保存表单、审核身份、输入保护、比较状态、首页导入／放弃／恢复、全部展开时的可达性，以及现有主题资源合同。详见 [最终测试日志](checks/wpf-ui-compatibility-final2.log)、[TRX](checks/wpf-ui-compatibility-final2.trx)、[构建日志](checks/wpf-ui-build-final.log)。

中间的广泛 UI／架构运行共 1503 项：1468 通过、35 失败，其中 14 项与原接受基线失败名称相同，另有 21 项本轮兼容性问题。21 项已全部在最终定向运行中按**完整测试实例名称**匹配并通过复测；[机器可读核对](validation.json) 的 `AllNewFailuresRetestedAndPassed=true`。其中包括紧凑视口的恢复可达性、保存容器身份、未提供协调器时的模板实例化、主题键兼容和多尺寸目录可见性。

**兼容性修正后没有再次运行整个 1503 项广泛套件，不能写“全套通过”或“完整最终套件只剩 14 项失败”。** 原 14 项基线失败及其名称／消息、21 项复测映射都列在 validation.json。早期失败日志和一次并行构建导致的测试 DLL 锁冲突日志也保留，后者没有产生测试结果。

一次独立只读代码审查没有发现 Critical／Important 问题。其 Minor 建议中的结果窗格身份、联合保存按钮作用范围以及比较状态断言已调整；没有增加第二次整体审查。实际物理输入与真实 DPI 仍待实机验证。

## 重新生成

在实现工作树中执行，继续沿用已有安全测试包装器，保留其 11 项精确排除与全部 Smoke 排除：

```powershell
$env:PF_WPFUI_SAMPLE_CAPTURE_DIR = Join-Path $PWD 'output/ui/WPF-UI-SAMPLES/screenshots'
pwsh -NoProfile -File output/ui/SCRUM-11173/checks/run-safe-ui.ps1 `
  -RunName wpf-ui-samples-recheck -Include 'FullyQualifiedName~WpfUiSample'
pwsh -NoProfile -File output/ui/WPF-UI-SAMPLES/render-comparisons.ps1
```

原型文件由 `render-comparisons.ps1 -PrototypeDirectory <目录>` 指定。`manifest.json` 记录本交付的截图／原型／并排图身份，`source-manifest.json` 记录当前实现和锁文件身份。后续重建会生成新的测试截图和临时目录，哈希可随之改变。
