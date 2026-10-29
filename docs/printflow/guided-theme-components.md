# Guided WPF 主题与展示组件

SCRUM-11177 提供具名资源、六类展示组件，以及 Home / WorkflowSelection 两个页面接入。所有资源都是显式引用；没有全局隐式 Button、TextBox 或 Session 页面样式。

## 装载与变量

页面独立构造时也要在自己的 ResourceDictionary 中合并 `/PrintFlow.App;component/Themes/GuidedTheme.xaml`。聚合顺序为 Tokens → Controls → Components。App 同样合并主题，MainWindow 显式设置画布、文字与字体，原 DataTemplate 导航保持不变。

跨合并字典的 token 在消费控件上使用 DynamicResource，以支持没有生产 Application 的 STA 离屏视图；同一字典中的 Style BasedOn 使用 StaticResource。

| 类别 | 键与类型 |
| --- | --- |
| 颜色 | `PF.Color.*` 为 Color；`PF.Brush.*` 为 SolidColorBrush。Canvas #F6F7F4、Surface #FFFFFF、Ink #202E2A、Muted #65706B、Primary #16765D、Selection #E8F4EF、Border #DCE3DE、Warning #FFF1C8 |
| 字体 | `PF.Font.Family` 为 FontFamily（Microsoft YaHei, Noto Sans SC）；Title/Body/Auxiliary 为 double 28/14/12 |
| 布局 | `PF.Space.{4,8,12,16,24,32}` 为 double；`PF.Control.MinHeight` 为 double 42；Padding.Card / Padding.Field / Margin.Section 为 Thickness；Corner.Card / Corner.Control 为 CornerRadius |
| 图片背景 | `PF.Image.Checkerboard` 为 DrawingBrush，仅绘制在图片后面，不改写或合成源图像素 |
| 控件 | `PF.Button.Primary/Secondary/Danger`、`PF.Input.TextBox/ComboBox/CheckBox/RadioButton`、`PF.Disclosure`、`PF.Progress`，目标为对应原生 WPF 控件 |

不要把 double 间距资源赋给 Thickness，也不要把 Color 当 Brush。控件焦点采用独立外轮廓，选中填充不能代替焦点。物理键盘焦点和真实高 DPI 尚待工作站验证。

## 六类组件

| 组件 | Style / 目标类型 | 输入与职责 |
| --- | --- | --- |
| 页面标题 | `PF.PageHeader` / HeaderedContentControl | Header 为本地化标题，Content 为说明；两者可换行 |
| 任务卡 | `PF.TaskCard` / HeaderedContentControl；`PF.Card` / Border | Header 为标题，Content 或 Child 为任意内容；保留子控件 DataContext 和 AutomationId |
| 状态提示 | `PF.Notice.Info/Warning/Error/Success` / HeaderedContentControl | Header 为明确状态文字，Content 为解释和合法操作；几何标记配合文字，不能只依赖颜色 |
| 表单区 | `PF.FormSection` / HeaderedContentControl | Header 为标签组，Content 为调用方表单；不创建业务字段、验证规则或默认选项 |
| 操作栏 | `PF.ActionBar` / ContentControl | Content 通常为 WrapPanel；页面负责 Grid Auto 行、滚动区域和可达性 |
| 只读步骤条 | `PF.StepNavigation` / ItemsControl | ItemsSource 为有序 GuidedStepItem 记录；显示 Title、Detail、StatusText 与状态标记 |

`GuidedStepItem(string Id, string Title, string Detail, string StatusText, GuidedStepState State)` 是纯展示记录。状态为 Pending / Current / Completed / Attention / Skipped。组件不包含 ICommand、选中状态写回、业务状态转换或跳转；未来步骤不是 Tab 或 UIA 可执行入口。语言刷新由调用方替换列表，不订阅全局语言服务。

```xml
<HeaderedContentControl Style="{StaticResource PF.PageHeader}"
                        Header="{Binding Heading}" Content="{Binding Hint}" />
<HeaderedContentControl Style="{StaticResource PF.Notice.Warning}"
                        Header="{Binding WarningHeading}">
    <TextBlock Text="{Binding WarningReason}" TextWrapping="Wrap" />
</HeaderedContentControl>
<ContentControl Style="{StaticResource PF.ActionBar}">
    <WrapPanel>
        <Button Style="{StaticResource PF.Button.Secondary}"
                Content="{Binding BackLabel}" Command="{Binding BackCommand}" />
    </WrapPanel>
</ContentControl>
```

调用方负责本地化、显隐、命令资格、禁用原因、初始焦点和错误详情。只把低频辅助详情放进 `PF.Disclosure`；阻塞原因、必要审核说明和合法停止入口保持可读、可达。未知处理进度使用 `PF.Progress` 与 `IsIndeterminate="True"`，不制造百分比。

审批必须继续使用真实 `ReviewApprovalButton`，可应用主/危险按钮样式，但不得替换运行时类型或改写 TargetIdentity、IsGestureGuarded、Command/CommandParameter、AutomationId、IsDefault / IsCancel。样式不执行命令，也不替代业务授权。

## 已接入与后续边界

Home 保留导入、环境摘要、关键启动警告、恢复/最近任务和放弃确认；标题之外按 3:2 分配上下滚动区，让 1000×700 DIP 下的 42-DIP 操作仍可达。WorkflowSelection 保留预览、输出名、三条路线和现有单次选择命令；本轮没有增加“先选择再确认”的业务阶段。

E2 / SCRUM-11169 负责真实路线步骤投影、历史/返回资格、当前工作区和完整固定操作栏壳；E3～E5 负责其他业务页面；E6 负责真实窗口、键盘、高 DPI、可访问性和中文新手验收。组件 catalog 仅是合成展示，不进入产品导航。Session、Domain、Workflow、Infrastructure 未迁移。

## 验证与截图

`GuidedThemeFixture` 只在 STA 中创建和消费主题。`GuidedComponentCatalog.Create(state)` 支持 normal / disabled / error / loading / long-text。`GuidedThemeVisualEvidenceTests` 默认仍执行布局和绑定断言；仅当 `PF_11177_CAPTURE_DIR` 指向本地目录时输出截图及 JSON 测量。未设置该变量时截图生成是 NOT RUN，而不是失败。

截图为 96 DPI 的 WPF 离屏 RenderTargetBitmap，1000×700、1280×720、1920×1080 是窗口 DIP（在这些截图中一 DIP 对应一像素）。它不等同于真实 125% / 150% PerMonitorV2、桌面输入或工作站验证。详情见 [SCRUM-11177_RESULTS](../codex/printflow-operator-ux-backlog/SCRUM-11177_RESULTS.md)。
