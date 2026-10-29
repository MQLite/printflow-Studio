# SCRUM-11177 Guided WPF Theme Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将已批准的浅色、青绿色视觉体系落为 WPF 共享资源和六类展示组件，并在首页、路线选择页及合成组件场景中验证。

**Architecture:** 使用有明确键名的 ResourceDictionary 和 WPF 原生控件样式，避免隐式样式改变尚未迁移的页面。组件接收标题、内容、步骤状态及现有 ICommand；业务资格、审批身份与导航仍由原 ViewModel 和服务负责。先迁移两个代表性页面，完整引导壳和 Session 拆页由 SCRUM-11169～11172 承接。

**Tech Stack:** .NET 10、WPF、CommunityToolkit.Mvvm、xUnit、Shouldly；复用现有 STA 离屏渲染与合成测试设施，不增加 NuGet 或 UI 框架。

**Spec:** [已批准设计规范 v2](../../../output/imagegen/SCRUM-11168/2026-10-19-v2/design-spec.html)，[45 页原型](../../../output/imagegen/SCRUM-11168/2026-10-19-v2/index.html)，[批准记录](../../../output/imagegen/SCRUM-11168/2026-10-19-v2/design-approval.json)。用户于 2026-10-19 回复「通过。请继续。」。

## Global Constraints

- 原图/InputSnapshot和精确审批身份保留；业务工作流、资格与下游失效由现有服务授权。
- 两条TIFF路线均需用户明确选0/1/2px白墨分支和理由；无默认选择；源PSD/PDF的专色不沿用。
- 素材路线没有印刷尺寸、白墨和TIFF；直接TIFF路线没有美图和默认裁切。
- 通过、保存、完成分别记录；未知进度不能伪造百分比。
- Photoshop接管不强制结束外部应用，不允许手工TIFF回导；重入之后另行启动新尝试。
- 同事修正只从合法去背景审核发起；准备副本后由人自行交给同事，再显式选择回导。
- 保存待核实先核实同一次保存；另存是明确独立操作；同名不会覆盖。
- 返回修改明确下游影响，历史审核保留；放弃本页输入不等于放弃任务。
- zh-CN/en 资源结构保留，中文为视觉和人工验收语言，不增加英文人工走查。
- 保留 `ReviewApprovalButton`、`TargetIdentity`、`IsGestureGuarded`、原命令/参数、AutomationId 和现有中性落焦行为；样式不能把专用审批按钮替换成普通按钮。
- 不触碰 Domain、Workflow、Infrastructure、数据库、生产预设或外部自动化；不实现 AI 生图、文件上传、发布或安装。
- 本计划不把 N1–N4/F6、PNG 审批/绑定、generic recovery 等旧问题视为已修复；基线失败与新回归分别记录。
- 候选目标为现有 1000×700 DIP 基线、1280×720、1920×1080 及 100%/125%/150% 缩放；须区分显示器物理像素与窗口 DIP。这是获批的验证目标，实际支持情况由后续工作站验证确定。

## Review Focus

1. 独立构造 View 时没有生产 Application，资源仍能解析且绑定无错误；Task 1 和 Task 4 检验。
2. 青色选中背景不能掩盖键盘焦点，禁用按钮不能触发命令或丢失原因文本；Task 1 检验。
3. 步骤条中的未来步骤不能成为 Tab 或 UIA 可执行入口，语言/状态刷新也不能启动命令；Task 3 检验。
4. 长中文、长文件名及小窗口不能覆盖主操作、错误提示或现有环境阻塞说明；Task 4 和 Task 5 检验。
5. 应用共享按钮样式后，旧按键释放、连击和已改变结果身份仍不能批准新结果；Task 2 检验。

---

## 实施范围与文件职责

当前基线：`master`，`de7bca6e0a8462584b4d6bf4d16307d1335e2fb3`；SDK 为 10.0.400。当前工作区已有其他未提交的文档、审计及验收材料，不能清理、覆盖或一起提交。实施时按 using-git-worktrees 技能选择隔离工作区，保留这份计划和原始批准材料供执行者读取。

| 文件 | 职责 |
| --- | --- |
| `src/PrintFlow.App/Themes/GuidedTokens.xaml` | 颜色、画刷、字体、尺寸、间距、圆角和透明棋盘格的唯一值来源 |
| `src/PrintFlow.App/Themes/GuidedControls.xaml` | 显式按钮、输入、选择、展开和进度样式 |
| `src/PrintFlow.App/Themes/GuidedComponents.xaml` | 页面标题、任务卡、提示、表单区、操作栏和步骤条样式 |
| `src/PrintFlow.App/Themes/GuidedTheme.xaml` | 按 Tokens → Controls → Components 顺序聚合上述字典 |
| `src/PrintFlow.App/Controls/GuidedStepItem.cs` | 纯展示步骤记录及枚举，无业务/导航命令 |
| `src/PrintFlow.App/App.xaml` | 合并主题资源，不创建隐式全局控件样式 |
| `src/PrintFlow.App/MainWindow.xaml` | 显式采用背景、文字与字体资源；维持现有 DataTemplate 导航 |
| `src/PrintFlow.App/Views/HomeView.xaml` | 首个真实页面接入；保留导入、环境、警告、最近任务与确认入口 |
| `src/PrintFlow.App/Views/WorkflowSelectionView.xaml` | 第二个接入页；保留三路线内容、输入预览、输出名和现有选择命令 |
| `tests/PrintFlow.Tests/Fixtures/GuidedThemeFixture.cs` | 为独立离屏控件加载相同主题，保持 STA 所有权 |
| `tests/PrintFlow.Tests/Fixtures/GuidedComponentCatalog.cs` | 六类组件的合成展示场景，不进入产品导航 |
| `tests/PrintFlow.Tests/Integration/Ui/GuidedThemeResourceTests.cs` | 资源装载、基础状态及无 Application 场景 |
| `tests/PrintFlow.Tests/Integration/Ui/GuidedComponentTests.cs` | 组合组件、状态、内容语义及审批按钮兼容性 |
| `tests/PrintFlow.Tests/Integration/Ui/GuidedStepNavigationTests.cs` | 步骤状态、文本、不可执行和刷新行为 |
| `tests/PrintFlow.Tests/Integration/Ui/GuidedThemeAdoptionTests.cs` | 真实页面接入、长文本、命令和可访问身份 |
| `tests/PrintFlow.Tests/Integration/Ui/GuidedThemeVisualEvidenceTests.cs` | 离屏截图和布局测量；无窗口、输入或生产启动 |
| `docs/printflow/guided-theme-components.md` | 组件用法、显隐/焦点职责与后续 Epic 接入边界 |
| `docs/codex/printflow-operator-ux-backlog/SCRUM-11177_RESULTS.md` | 执行结果、证据、基线/回归及未验证项 |

现有 `WpfRendering.cs` 无需修改：其 `RenderExpectingNoBindingErrors<T>(Func<UserControl>, Size, Func<RenderedTree,T>)` 负责布局与绑定检查，`CapturePng(Func<UserControl>, Size, string, Action<RenderedTree>?)` 负责 96 DPI 离屏截图。不把截图像素缩放冒充真实 PerMonitorV2 测试。

## Task 1: 共享变量和基础控件状态

**Files:** Create `GuidedTokens.xaml`、`GuidedControls.xaml`、`GuidedTheme.xaml`（上述 Themes 目录）；Create `GuidedThemeFixture.cs`；Test `GuidedThemeResourceTests.cs`。

**Interfaces:**

- `GuidedThemeFixture.CreateResources() -> ResourceDictionary`，使用 `/PrintFlow.App;component/Themes/GuidedTheme.xaml`；仅在 STA 内创建和消费。
- `GuidedThemeFixture.Host(FrameworkElement content) -> UserControl`，在容器资源合并上述字典；不构造 `App`、不调用 `Show`。
- `PF.Color.{Canvas,Surface,Ink,Muted,Primary,Selection,Border,Warning}` 为 `Color`；对应 `PF.Brush.*` 为 `SolidColorBrush`，两者不混用。
- `PF.Font.Family` 为 `FontFamily("Microsoft YaHei, Noto Sans SC")`；`PF.Font.{Title,Body,Auxiliary}` 为 double 28/14/12；`PF.Control.MinHeight` 为 double 42。
- `PF.Space.{4,8,12,16,24,32}` 为 double；`PF.Padding.Card` 为 Thickness(24)，`PF.Padding.Field` 为 Thickness(12,8,12,8)，`PF.Margin.Section` 为 Thickness(0,0,0,16)，`PF.Corner.Card` 为 CornerRadius(10)。间距 double 不能直接赋给 Thickness 属性。
- `PF.Image.Checkerboard` 为中性灰白 `DrawingBrush`，表达透明背景，不改变图片像素。
- 显式 Style 键：`PF.Button.{Primary,Secondary,Danger}` → Button；`PF.Input.TextBox` → TextBox；`PF.Input.ComboBox` → ComboBox；`PF.Input.CheckBox` → CheckBox；`PF.Input.RadioButton` → RadioButton；`PF.Disclosure` → Expander；`PF.Progress` → ProgressBar。

- [ ] **Step 1: 写测试。** `Dictionary_loads_without_production_application` 要求独立装载无异常；`Tokens_resolve_to_approved_values` 逐项断言 canvas #F6F7F4、surface #FFFFFF、ink #202E2A、muted #65706B、primary #16765D、selection #E8F4EF、border #DCE3DE、warning #FFF1C8 及上述资源类型；`Focused_selected_and_disabled_controls_remain_distinguishable` 要求主按钮 MinHeight ≥ 42、非空 FocusVisualStyle、禁用时 ICommand 不执行，旁边原因文本仍在树中。检查按钮模板保留 ContentPresenter，不截断中文文字。

```csharp
// Tokens_resolve_to_approved_values 的核心断言，运行于 WpfRendering.OnStaThread 内。
ResourceDictionary resources = GuidedThemeFixture.CreateResources();
((Color)resources["PF.Color.Primary"]).ShouldBe(Color.FromRgb(0x16, 0x76, 0x5D));
((SolidColorBrush)resources["PF.Brush.Canvas"]).Color.ShouldBe(Color.FromRgb(0xF6, 0xF7, 0xF4));
((double)resources["PF.Control.MinHeight"]).ShouldBe(42d);
((CornerRadius)resources["PF.Corner.Card"]).ShouldBe(new CornerRadius(10));
```

- [ ] **Step 2: 运行失败测试。** `dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --filter FullyQualifiedName~GuidedThemeResourceTests`；预期因字典/键缺失失败。
- [ ] **Step 3: 实现上述资源及 fixture。** 按接口定义集中值，按钮悬停/按下可加深主色；焦点使用独立外轮廓，不能复用选择填充。使用原生 Button/ICommand 和 TextBox/选择控件语义；不加全局输入处理器、不改 IsDefault/IsCancel、不覆盖命令资格。字典仅提供具名 Style，控件按需引用。
- [ ] **Step 4: 重跑 Step 2。** 预期全部 PASS，绑定错误为空。
- [ ] **Step 5: 提交本任务文件。** 逐一 `git add` 上述新文件，`git commit -m "feat(ui): add guided theme tokens and control styles"`；不得 `git add .`。

## Task 2: 页面组合组件和审批控件兼容

**Files:** Create `Themes/GuidedComponents.xaml`、`Fixtures/GuidedComponentCatalog.cs`；Modify `Themes/GuidedTheme.xaml`；Test `Integration/Ui/GuidedComponentTests.cs`。路径前缀分别同文件职责表。

**Interfaces:**

- 以下 Style 目标均为 `HeaderedContentControl`：`PF.PageHeader`（Header 标题、Content 说明）、`PF.TaskCard`（Header 卡片标题、Content 任意内容）、`PF.FormSection`（Header 标签组、Content 表单）、`PF.Notice.{Info,Warning,Error,Success}`（Header 明确状态文字、Content 解释/操作）。
- `PF.ActionBar` 目标为 `ContentControl`，Content 由调用方提供 WrapPanel/操作控件。它只负责表面、边线和内边距；页面 Grid 的 Auto 行负责固定可达。
- `PF.Card` 目标为 Border，供现有复杂容器渐进接入；与 TaskCard 共享背景、边线、圆角和内边距资源。
- `GuidedComponentCatalog.Create(string state) -> UserControl`，state 限定 normal/disabled/error/loading/long-text；返回 Task 1 fixture 容器，使用合成文本、原生命令计数器和六类组件（步骤条由 Task 3 补齐）。文字由调用方提供，组件不硬编码语言或从业务枚举推断文案。
- catalog 的操作栏和停止按钮固定采用测试身份 `GuidedCatalog.ActionBar`、`GuidedCatalog.Stop`；这些身份不加入产品页面。

- [ ] **Step 1: 写测试。** `Components_preserve_content_and_accessible_names` 要求标题和说明可读、任意子控件及原 AutomationId/Name 保留；`Warning_and_error_have_persistent_text_beyond_colour` 要求提示文本与非颜色标记同在、不会自行消失；`Loading_is_indeterminate_and_does_not_hide_legal_stop` 要求 IsIndeterminate=true、停止命令仍可访问且不会自动执行；`Styled_review_approval_preserves_target_and_gesture_guard` 用真实 `ReviewApprovalButton` 应用主样式，要求运行时类型不变、TargetIdentity/IsGestureGuarded 保留，并在旧按下/新身份释放及双击场景下命令计数为 0。复用 `OperatorWave1Tests` 已有合成输入方法的做法，不向桌面注入输入。

```csharp
// Loading_is_indeterminate_and_does_not_hide_legal_stop。
var loading = WpfRendering.RenderExpectingNoBindingErrors(
    () => GuidedComponentCatalog.Create("loading"), new Size(1000, 700), tree => (
        Indeterminate: tree.OfType<ProgressBar>().Single().IsIndeterminate,
        StopEnabled: tree.OfType<Button>().Single(b =>
            AutomationProperties.GetAutomationId(b) == "GuidedCatalog.Stop").IsEnabled));
loading.Facts.Indeterminate.ShouldBeTrue();
loading.Facts.StopEnabled.ShouldBeTrue();
```

- [ ] **Step 2: 运行失败测试。** `dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --filter FullyQualifiedName~GuidedComponentTests`；预期缺少组合样式或 catalog 而失败。
- [ ] **Step 3: 实现组合样式和 catalog。** ContentPresenter 保留调用方 DataContext；标题/正文允许换行；提示的图形采用 WPF 几何图形，不增加位图资产。操作栏不创建默认主命令、不吸走焦点；执行中通过已有 CanExecute/IsEnabled 禁用重复操作，停止/接管的资格仍由调用方提供。
- [ ] **Step 4: 重跑 Step 2，并运行** `dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --filter "FullyQualifiedName~OperatorWave1Tests|FullyQualifiedName~OperatorWave1AFocusTests"`；预期新增组件测试 PASS，既有审批/中性落焦断言保持。失败先核对同基线记录，不删除或放宽断言。
- [ ] **Step 5: 仅提交本任务文件。** `git commit -m "feat(ui): add guided presentation components"`。

## Task 3: 只读步骤条组件

**Files:** Create `src/PrintFlow.App/Controls/GuidedStepItem.cs`；Modify `Themes/GuidedComponents.xaml`、`Fixtures/GuidedComponentCatalog.cs`；Test `Integration/Ui/GuidedStepNavigationTests.cs`。

**Interfaces:**

```csharp
public enum GuidedStepState { Pending, Current, Completed, Attention, Skipped }
public sealed record GuidedStepItem(
    string Id, string Title, string Detail, string StatusText, GuidedStepState State);
```

- 命名空间 `PrintFlow.App.Controls`。`StatusText` 为调用方已本地化的状态文字，不能只靠图标/颜色表达状态。
- `PF.StepNavigation` 为 ItemsControl Style，ItemsSource 接受上述记录；固定保留输入顺序，显示 Title/Detail/StatusText。不包含 ICommand、选中状态写回或业务状态转换。后续 E2 才负责投影真实路线及有资格的历史导航。

- [ ] **Step 1: 写测试。** `All_five_states_show_text_and_distinct_current_marker` 断言五类记录按顺序显示、当前节点有标记且状态文字未丢失；`Future_steps_are_not_tab_stops_or_invokable` 断言步骤树中没有 Button、可聚焦项目或 Invoke/Selection 操作入口；`Replacing_items_does_not_execute_or_change_business_state` 替换中文/英文展示列表后要求命令计数和外部状态均不变；`Long_step_text_wraps_without_overlapping_neighbours` 在窄列中检查每个文本边界和相邻节点无交叠。

```csharp
// Future_steps_are_not_tab_stops_or_invokable 的树断言。
var pending = WpfRendering.RenderExpectingNoBindingErrors(() =>
{
    ItemsControl steps = new() { ItemsSource = new[] {
        new GuidedStepItem("save", "保存结果", "等待前面步骤完成", "待进行", GuidedStepState.Pending) } };
    steps.SetResourceReference(FrameworkElement.StyleProperty, "PF.StepNavigation");
    return GuidedThemeFixture.Host(steps);
}, new Size(240, 700), tree => (
    Buttons: tree.OfType<Button>().Count(),
    TabStops: tree.OfType<Control>().Count(c => c.IsTabStop && c.Focusable),
    HasStatus: tree.OfType<TextBlock>().Any(t => t.Text == "待进行")));
pending.Facts.Buttons.ShouldBe(0);
pending.Facts.TabStops.ShouldBe(0);
pending.Facts.HasStatus.ShouldBeTrue();
```

- [ ] **Step 2: 运行失败测试。** `dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --filter FullyQualifiedName~GuidedStepNavigationTests`；预期缺少记录类型/样式而失败。
- [ ] **Step 3: 实现记录、模板和 catalog 六类完整布局。** 用 DataTrigger 根据 State 改变标记与表面；装饰元素不进入 Tab，不给步骤加隐式点击/Enter 跳转。语言改变由调用方替换记录，组件不订阅全局语言服务。
- [ ] **Step 4: 重跑 Step 2 及 GuidedComponentTests。** 预期 PASS，所有页面绑定错误为空。
- [ ] **Step 5: 仅提交本任务文件。** `git commit -m "feat(ui): add read-only guided step navigation"`。

## Task 4: 首页与路线选择页采用共享主题

**Files:** Modify `App.xaml`、`MainWindow.xaml`、`Views/HomeView.xaml`、`Views/WorkflowSelectionView.xaml`；Test `Integration/Ui/GuidedThemeAdoptionTests.cs`。本任务不修改 ViewModel、SessionScreenView 或页面 code-behind。

**Interfaces:** 使用 Task 1～3 的资源键和现有 `HomeViewModel` / `WorkflowSelectionViewModel`。保留首页 `ChooseFileCommand`、环境与恢复警告、最近记录、放弃确认；保留路线页 `SelectCommand`、原参数、`CanSelect`、`BackToHomeCommand`、输出名和预览绑定。不新增「选择后确认」业务阶段或不存在的侧栏导航；这些语义变化属于 E2/E3。

- [ ] **Step 1: 记录改动前基线。** 使用同一提交和干净构建，运行下列 Step 5 命令，将现有失败、跳过及未运行项逐项写入结果文件。仅合成 harness，不设置任何工作站/可见窗口 opt-in 环境变量。
- [ ] **Step 2: 写测试。** `Standalone_adopted_views_resolve_theme_without_application` 分别构造两个 View 并要求无资源/绑定错误；`Home_keeps_import_warnings_and_recent_actions` 断言既有可访问身份、原命令和警告可达；`Route_cards_keep_exact_commands_parameters_and_three_workflows` 断言三条固定路线、原 ICommand 实例和参数不变，素材路线无印刷字段；`Long_Chinese_source_and_failure_text_keep_actions_reachable` 输入长中文文件名/失败说明，在 1000×700 DIP 及 1280×720 DIP 下要求横向不溢出、文本换行、底部返回操作在可视区，滚动后导入/路线操作可达，关键警告没有被折叠到低频详情内。

```csharp
// Home_keeps_import_warnings_and_recent_actions 的命令身份断言。
using HomeScreenHarness harness = new();
var home = WpfRendering.RenderExpectingNoBindingErrors(
    () => new HomeView { DataContext = harness.Home }, new Size(1000, 700), tree => {
        Button import = tree.OfType<Button>().Single(b =>
            AutomationProperties.GetAutomationId(b) == "Home.ChooseFile");
        return (SameCommand: ReferenceEquals(import.Command, harness.Home.ChooseFileCommand),
                MinimumHeight: import.MinHeight);
    });
home.Facts.SameCommand.ShouldBeTrue();
home.Facts.MinimumHeight.ShouldBeGreaterThanOrEqualTo(42d);
```

- [ ] **Step 3: 运行失败测试。** `dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --filter FullyQualifiedName~GuidedThemeAdoptionTests`；预期主题接入断言 FAIL。
- [ ] **Step 4: 采用显式资源和组合组件。** App 合并主题；两个 View 各自合并同一主题，支持独立构造。保留已有 Converter 和资源键；Home 将现有导入按钮设为主样式，其余操作按语义使用次样式，卡片、标题、提示和表单使用共享资源。路线页保留原路线选择交互，仅改容器和呈现。主要操作高度至少 42，布局按内容自动换行/滚动；不把现有关键说明收起。移除已迁移区域重复的颜色/字体/间距值。
- [ ] **Step 5: 验证。** 运行新增 GuidedThemeAdoptionTests；再运行 `dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --filter "FullyQualifiedName~HomeAndWorkflowSelectionTests|FullyQualifiedName~HomeReadiness|FullyQualifiedName~HomeRecentAccessibilityTests|FullyQualifiedName~HomeAbandonConfirmationTests|FullyQualifiedName~WorkflowPurposeCardTests|FullyQualifiedName~WorkflowSelectionAccessibilityTests|FullyQualifiedName~LocalisationResourceTests"`。预期新增测试 PASS，无新增失败，原资源双语完整性/路线资格/确认及可访问身份断言保持。
- [ ] **Step 6: 仅提交本任务文件。** `git commit -m "feat(ui): apply guided theme to home and workflow selection"`。

## Task 5: 视觉证据、组件说明与交付核对

**Files:** Create `Integration/Ui/GuidedThemeVisualEvidenceTests.cs`、`docs/printflow/guided-theme-components.md`、`docs/codex/printflow-operator-ux-backlog/SCRUM-11177_RESULTS.md`；证据保存 `output/ui/SCRUM-11177/`，不把临时构建产物或客户数据纳入提交。

**Interfaces:** 复用 `GuidedComponentCatalog.Create(string state)`、HomeScreenHarness、OperatorCultureScope、`WpfRendering.CapturePng`。可选环境变量 `PF_11177_CAPTURE_DIR` 仅指定本地证据目录；未设置时测试仍执行布局断言，截图状态记录 NOT RUN。不调用生产 Composition、文件选择器、Meitu/Photoshop、窗口 Show 或桌面输入。

- [ ] **Step 1: 写测试。** `All_component_states_fit_with_wrapping_and_visible_actions` 覆盖 normal/disabled/error/loading/long-text，三个 DIP 视口 1000×700、1280×720、1920×1080；要求无绑定错误、可视文本不横向溢出、操作栏不与内容重叠、底部操作可达、折叠内容退出 Tab/可执行 UIA 面。`Representative_views_capture_the_same_synthetic_sources` 复用同一合成图源生成真实首页、路线页证据，保留原命令状态，不用生产图片。

```csharp
// All_component_states_fit_with_wrapping_and_visible_actions 中，Theory 参数为 state/width/height。
var layout = WpfRendering.RenderExpectingNoBindingErrors(
    () => GuidedComponentCatalog.Create(state), new Size(width, height), tree => {
        FrameworkElement bar = tree.OfType<FrameworkElement>().Single(e =>
            AutomationProperties.GetAutomationId(e) == "GuidedCatalog.ActionBar");
        return bar.TransformToAncestor(tree.Root).TransformBounds(new Rect(bar.RenderSize));
    });
layout.Facts.Left.ShouldBeGreaterThanOrEqualTo(0d);
layout.Facts.Right.ShouldBeLessThanOrEqualTo(width + 0.5);
layout.Facts.Bottom.ShouldBeLessThanOrEqualTo(height + 0.5);
layout.Facts.Height.ShouldBeGreaterThan(0d);
```

- [ ] **Step 2: 运行失败测试。** `dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --filter FullyQualifiedName~GuidedThemeVisualEvidenceTests`；要求实际运行新断言，失败时定位组件/页面归属并修正，不将失败用截图生成成功替代。
- [ ] **Step 3: 生成截图和尺寸记录。** 在 PowerShell 中设置任务专用 `$env:PF_11177_CAPTURE_DIR` 为当前实施工作区 `output/ui/SCRUM-11177` 的绝对路径，运行 Step 2，finally 恢复原环境变量。至少生成 15 张中文组件场景图及两个真实页面的三个视口图；记录 state、DIP、96 DPI、命令状态、来源哈希和布局测量。离屏无键盘焦点截图不能声称验证了物理焦点；焦点外观另留组件模板检查结果。
- [ ] **Step 4: 逐张视觉检查生成图。** 通过本地图片查看工具检查中文换行、层级、留白、错误/禁用/加载区分及主操作；发现遮挡、空白、透明底变黑或裁字，回到所属任务修正并仅重跑受影响检查。保存原型 → 实际 WPF 页面的对照索引。
- [ ] **Step 5: 完成构建与回归。** `dotnet build PrintFlowStudio.sln --no-restore`；`dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --no-build --filter "FullyQualifiedName~Integration.Ui|FullyQualifiedName~Architecture" --logger "trx;LogFileName=scrum-11177-ui.trx"`。新代码必须通过；相同基线失败单列且不得直接宣称全量 PASS。正常人工/工作站 opt-in 测试保持未启用，跳过与通过分别统计。
- [ ] **Step 6: 写组件用法和结果。** 说明六类组件键名、输入类型、调用方负责的本地化/命令/显隐/焦点、三路线导航仍待 E2、其余页面仍待后续 Epic。结果列出测试命令和通过/失败/跳过数、截图路径、基线问题、真实键盘/DPI/新手/生产验证 NOT RUN。只有本项实际 AC 均具备证据才建议进入 In Review；Epic 不能提前 Done。
- [ ] **Step 7: 仅提交本任务文件。** `git commit -m "test(ui): capture guided theme evidence and usage"`。按用户选择的执行方式完成独立审查，修正新问题后再报告实际交付。

## 验收与后续责任

| SCRUM-11177 验收条件 | 本计划交付 |
| --- | --- |
| 集中式 ResourceDictionary 与变量 | Task 1，Task 4 实页接入 |
| 步骤条、标题、任务卡、提示、表单区、操作栏 | Task 2～3，Task 5 全状态组件图 |
| 不自行授权或启动业务 | Task 2 审批兼容，Task 3 不可执行步骤，Task 4 原命令身份测试 |
| 代表页、合成图、中文及状态/焦点检查 | Task 1～5；物理键盘和真实 DPI 另行记录 |

E2（SCRUM-11169）拥有真实步骤投影、历史/返回、当前工作区和固定操作栏的完整壳；E3～E5 拥有各业务页面及异常迁移；E6（SCRUM-11173）拥有跨窗口/DPI、可访问性和中文新手验收。本计划不以组件 catalog 代替上述产品功能。

## 计划自审

2026-10-19 已对照已批准设计规范和 Jira D04 的四项 AC 自审：范围映射完整；五项 Review Focus 都有所属任务；资源类型、命名与 fixture 签名一致；没有新增业务授权、底层服务或生产操作。宽屏/DPI 是验证目标，离屏证据的限制与后续责任已分开。五个任务分别产出可测试交付；不修改现有 Session 审批和图像区布局基线。

**执行状态：用户于2026-10-19批准计划并选择 Native。五项任务、一次独立整体审查及其一次 TDD 修复均已完成，SCRUM-11177 已进入 In Review。** 测试、证据与实际边界见 [执行结果](../../codex/printflow-operator-ux-backlog/SCRUM-11177_RESULTS.md)；原始要求和任务步骤保留供审查对照。真实工作站/人工验证继续按已列边界记录，后续 Epic 未开始。
