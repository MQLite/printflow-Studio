# SCRUM-11177 独立整体审查

2026-10-19。审查范围 `de7bca6e0a8462584b4d6bf4d16307d1335e2fb3..9140f85f5a063fde34d96c1ad98ea032ade2c5d9`。审查者 `/root/whole_branch_reviewer` 使用 `fork_turns=none` 的全新上下文，运行信息核实为 `gpt-6-astra / high`，session `01a151c4-4248-7350-89b5-43d7c922f0d7`；没有继承实现过程、改动检出区或再派审查代理。

依据：原四项 Jira 验收条件、批准的五任务计划、v2 设计规范、实际差异及既有代码、21 张实际截图与两张原型图、截图/合成源哈希、原始基线与最终 TRX、构建与红绿日志。审查者逐项比较命令、参数、TargetIdentity、IsGestureGuarded、AutomationId、IsDefault、IsCancel；这些值与基线一致。

## 发现

**Important：另一选区渲染模式下缺少对比文字颜色。** 审查时 `src/PrintFlow.App/Themes/GuidedControls.xaml:85–86` 设置 `PF.Brush.Selection` 和 `SelectionOpacity=1`，未设置 `SelectionTextBrush`。审查者在独立进程显式设置 `UseAdornerForTextboxSelectionRendering=false` 后，用实际 .NET 10 WPF、主题字典和合成选择/焦点状态复现系统白字落在 `#E8F4EF` 浅底上，选中文字难以阅读；[修复前图](../../../output/ui/SCRUM-11177/selection-before-review.png)。无 Application 或可见窗口。

修复阶段进一步确认，当前测试宿主默认值为 true，默认路径的选区文字仍可读。因此该发现限定于上述另一渲染路径，不声称真实产品默认环境已复现不可读。共享样式仍应为可用的选区渲染路径显式提供对比文字色，故保留这项小范围修复；真实生产 App 未启动。

处置：控制会话维持 Important 分级，要求同一执行者完成一次 TDD 修复：先复现活动/非活动选区，再为该控件显式引用对比文字画刷，保留修复后视觉证据并重跑必要检查。实际修复提交与结果以 [执行结果](SCRUM-11177_RESULTS.md) 为准；本审查快照的结论是“修复该项后就绪”。按 Native executing-plans 流程不再另派第二次审查。

Critical：无。Minor：无。

## 修复收口

同一执行者在 `af040950c1a73441d5b9868f8e61d4b32ff04ff8` 为共享选区增加 Ink 文字画刷；两项新增选区测试先失败再通过。控制会话检查最终产品差异仅为该 Setter，并独立复跑全部40项 Guided 测试，40通过、0失败。修复后构建零警告/错误，广域回归1269通过、14既有失败、1跳过；控制会话独立解析原始 TRX，确认14项名称与错误信息均与基线一致，[复核记录](../../../output/ui/SCRUM-11177/checks/controller-final-audit.json)。

四张修复后选区图由执行者检查，分别标注默认模式与独立进程显式非 adorner 模式；没有把合成焦点或另一模式冒充生产默认/物理键盘验证。原21张页面与组件图保持原审查快照。按 Native 流程使用测试完成这一次修复验证，未宣称审查者复审了后续提交，也未派第二次审查。该条件渲染路径问题已处理，Critical/Important 无未处理项，Minor 无待办。

## 已核验的证据

- 21 张截图均已检查，记录的滚动状态下未发现遮挡；全部截图哈希及共同合成源哈希已复核。
- 审查快照中新增 Guided 测试 38 项通过，页面接入及既有子集 186 项通过，视觉案例 18 项通过；构建零警告、零错误。
- 审查快照广域回归为 1267 通过、14 失败、1 跳过；14 项失败的名称和错误信息均与基线匹配。此处保留审查时数字，修复后的最终数字见执行结果。
- 展示层边界保持：没有 ViewModel、code-behind、Session、Domain、Workflow、Infrastructure 修改。

## 未作判定的行为与控制会话裁定

1. 完整引导壳、历史导航、路线确认重构和其余页面迁移：批准范围属于后续 Epic，继续留在后续工作。代价是本项交付仍为共享组件及两个代表页。
2. 原有 PrintDimensionsPreflight、ManualCropAdjustment、KeepOriginalExtent、HomeRecentAccessibility、OperatorWave1AHost smoke 和 OperatorCultureIsolation 失败：名称与错误信息匹配基线，不扩大本项去修复。代价是遗留失败仍未解决。
3. 基线 FinalSave 失败未在审查快照复现：没有足够证据认定已修复，也没有相关实现变化。代价是该间歇失败仍需原任务继续追踪。
4. 真实键盘/屏幕阅读器、DPI/多显示器、新手及生产处理：离屏证据不能证明，继续标 NOT RUN 并保留后续验证责任；英文人工走查不在计划中。代价是真实工作站行为尚未完成验证。

以上裁定及实现阶段的六项 Ruling 保存在执行结果和 scoped ledger；没有把未运行、未复现或既有失败标为通过。
