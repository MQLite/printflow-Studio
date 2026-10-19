# Guided Session 导航与操作约束

SCRUM-11169 把 SessionScreenView 组织为左侧只读步骤、当前工作区和固定操作栏。页面独立加载 GuidedTheme；颜色和推荐操作样式沿用 SCRUM-11177。Domain、工作流转换、数据库结构和外部适配器没有改变。

## 页面投影

SessionViewModel.Guided.cs 从当前 SessionView、WorkflowCatalog 和临时编辑模式计算 GuidedPage；它只选择显示内容，不判断新的业务资格。按钮仍绑定原来的命令及 CanExecute。步骤条是 ItemsControl，没有跳转命令；未完成步骤显示需完成当前步骤。

三条路线保持原顺序。PrepareAsset 没有印刷尺寸或白墨页；GeneratePrintTiff 没有美图或默认裁边页。两个 TIFF 路线进入尺寸、明确白墨选择、生成前确认、必要审核和保存。白墨没有默认值；0/1/2px 及原因沿用既有资源。PDF/PSD 先走原有源文件准备和必要审核。

ShowsGuided* 属性只收窄现有页面显示。尺寸预设留在表单中，自定义尺寸入口与确认按钮在固定栏中交替显示。白墨页优先显示选项；尺寸与环境检查在相应尺寸/生成前确认页保留。停止/接管位于工作区滚动外，仍由运行时资格决定。

## 历史、返回与草稿

SessionHistoryView 是从已经加载的步骤、版本和尝试记录投影出的只读事实；没有历史 SessionView、执行命令、新服务端点或额外数据库查询。历史预览使用既有本地预览服务；已释放或不可读的文件保留元数据与说明。历史页隐藏当前操作，返回当前步骤固定在页底；浏览不更改当前结果或审批。

BeginReturn 捕获当前任务/步骤/结果上下文和目标。确认文案列出返回位置、后续步骤、尺寸、输出和审核影响；确认时仍检查捕获值与当前状态，再调用原 ReturnToStep。取消、换任务、换结果或变更目标不能把旧确认重定向到其他目标。调整尺寸也走此确认。

草稿只保存在当前 ViewModel 中。尺寸、自定义尺寸、白墨、修正备注、裁切/裁边、保存名和文件夹离开前显示继续编辑/放弃本页输入。继续编辑保留输入；放弃恢复当前页基线，然后执行捕获的离开意图。没有草稿持久化、自动确认或自动放弃任务。保存草稿恢复不抹掉已捕获的保存结果与历史。相同上下文刷新保留输入，真正改变上下文清除旧意图。

## 输入与异步所有权

SessionActionScope 是展示上下文，不是审批凭据。其对象身份会随当前上下文、页面和生命周期变化；隐藏或卸载树不可操作。SessionActionButton 在按键/鼠标开始或 UIA Invoke provider 获取时捕获 Scope、命令、参数和版本；旧释放、重复键、双击第二次按下和保留的 UIA provider 不能操作新上下文。新获取的 UIA provider 异步投递到 WPF Dispatcher 后再次校验。

ReviewApprovalButton 继承通用保护，但继续独立验证原有精确 TargetIdentity 和 IsGestureGuarded。不要把通用上下文替代审批、裁边或保存身份。上下文改变后焦点回到可见中性标题；原有裁边/修正更具体的焦点合同保留。

服务调用捕获 Open 代数和任务 ID。旧成功、失败、刷新、运行时回调或 finally 不得覆盖新任务、清除新任务忙碌状态或改变其停止/接管资格。恢复只加载持久状态，不自动执行步骤。停止/接管仍独立于普通忙碌抑制。

## 扩展时的检查

- 新的跨页面操作使用 SessionActionButton，保留原 Command、CommandParameter、AutomationId 与精确审批控件。
- 影响页面/结果的临时模式加入 GuidedInteractionIdentity，并发出对应 PropertyChanged；不要把每次打字加入身份而抢走输入焦点。
- 新的历史字段保持不可变，只读取已经加载的事实；新增持久状态或服务资格属于另一个设计变更。
- 主操作放在滚动区域外；在 1000×700、1280×720、1920×1080 DIP 测试中文长内容及关键阻塞。
- 离屏 WPF 布局/合成输入不能替代真人键盘、真实 UIA 客户端、工作站和生产验证。

入口与证据见 [SCRUM-11169_RESULTS](../codex/printflow-operator-ux-backlog/SCRUM-11169_RESULTS.md)。
