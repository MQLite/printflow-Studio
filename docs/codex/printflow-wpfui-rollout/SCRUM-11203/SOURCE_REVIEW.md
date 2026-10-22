# 文案来源复核说明

本轮从U01冻结候选读取全部346个 `src` 下的 `.cs/.xaml/.resx` 文件；不从master资源猜测样板。SOURCE_MANIFEST逐文件记录SHA-256和行数，生成器先验证U01的冻结文件表，再从Git基线对象加冻结overlay和checkout-bytes重建内存语料，不启动产品或修改任一来源。源码路径/行号均指向这份冻结语料。

## 台账的阅读方式

| 位置 | 覆盖内容 | 如何使用 / 如何理解限制 |
| --- | --- | --- |
| COPY_INVENTORY.json / CSV | 1012个zh-CN/en资源键；现值、候选、完整占位符、技术词/单位、来源、消费者、语义风险与辅助要求 | 43项重点候选包含41项改写和2项显式保留。其他键保留当前值，不表示已获得真人批准。键缺少typed accessor不等于不可用，可能经Strings.Resolve解析。 |
| SOURCE_REGISTER.resourceExpressions（D） | 1082个直接资源读取或字符串资源键引用的源码行 | 包含插值中的读取及失败MessageKey引用；资源键引用不是运行时可见性证明。 |
| SOURCE_REGISTER.dynamicProjections（B） | 502个DisplayNames、Strings.Resolve、格式化、枚举回退、MessageKey/异常投影等复核点 | 保留动态值的来源，不能把系统/用户数据枚举成固定中文。B行与D行可重叠。 |
| SOURCE_REGISTER.stringLiterals（L） | 5332个C#字符串字面量，保守含标识/路径/诊断/格式/外部应用匹配词 | 是防遗漏的补充索引，不声称这些都是用户文案或都要翻译。`currentExpression`与`proposedExpression`相同；使用前沿消费者确认暴露面。 |
| SOURCE_REGISTER.xamlText（X） | 606个Text/Content/Header/Title/ToolTip属性 | 区分绑定/资源与字面量；需沿对应ViewModel解析。 |
| SOURCE_REGISTER.accessibility（A） | 549个辅助名称/说明/稳定ID/标签关系或代码调用位置 | 包含XAML与代码SetName/SetAutomationId；继承的框架名称可能来自Content，不表示每个控件都自定义peer或都已通过读屏。 |
| PAGE_COPY_MATRIX.json / MD | U01原140个页面/状态，含7个条件AI行和29个PNG/TIFF保存状态行 | `resourceKeys`是按页面语义整理的编辑复核集合，不是该状态同时可见的资源列表；实际可见性由原投影/模板/资格决定。 |

正则扫描是保守的源码索引，不是C#语义分析器；多行、插值、间接绑定及框架继承的完整行为须在C02/C03/C04消费时核对。所有字符串字面量都保留原文和行号，避免把动态或精确匹配词静默丢掉；没有把静态扫描写成运行时覆盖证明。

## 动态来源的业务处理

| 来源 | 当前事实与风险 | 文案/验证规则 |
| --- | --- | --- |
| Resources/Strings.cs | OperatorCulture每次读取；typed属性与Resolve都可取资源 | 键不改；完整索引/格式、Culture和资源缺失回退保留。 |
| Resources/DisplayNames.cs | 枚举→标签，ImportRefusal，Failure(code/MessageKey)，FailureNotice及单位格式化 | 枚举持久值与错误代码不翻译；操作者摘要使用资源。TechnicalDetail是技术信息，不能笼统声称它会作为主提示显示。新增枚举的ToString回退登记为技术值，不借文案任务改变业务。 |
| HomeViewModel | 最近任务状态、失败摘要、精确输出保存历史；空/查询失败/导入等待/放弃确认 | 复用11153：保存历史与主要待处理状态同时保留，查询失败不等于未保存；导入取消不承诺任务未创建。 |
| WorkflowSelectionViewModel | 三路线、源格式、基础文件名、进度及取消等待 | 首个结果生成才锁定路线；返回保留任务。名称后缀与源文件不重命名的事实保留。 |
| SessionViewModel各partial | 当前GuidedPage、运行阶段、审核要点、裁切、尺寸、白墨、接管、修正、确认及历史 | 字符串格式化的参数仍来自当前精确对象。详情与当前状态不混用；确认文案不能代替命令守卫。 |
| SessionViewModel.FinalSave.cs | FinalSaveKind与具体失败/历史/阶段/记录投影 | 29行保存状态按SAFETY_MEANINGS逐项核对；不把HistoryUnknown写成SavedPreviously。 |
| SharedReviewSurface / TiffReviewSurface / 编辑器代码 | 代码创建可见控件与辅助名称、动态图片标题、比较与缩放 | 结果标签、Name、当前图片身份一致；ReviewApprovalButton等审核类型/手势保护保持。 |
| EnvironmentReadinessViewModel / SettingsViewModel | 当前检查与历史、取消中/已取消、只读生产设置、语言选项 | 历史PASS不代表当前门禁开放；系统路径和应用状态不是可随意改写的固定文案。 |
| ErrorDetailsViewModel / NoticeErrorDetails / 诊断包对话框 | 本次错误、操作错误、恢复下一步、诊断预览/导出与Windows对话框名称/过滤器 | 显示本次正确错误代码；保留本地导出/人工交接和不覆盖限制。系统异常文本只登记动态边界，不凭空保证本地化。 |
| Workflow / Infrastructure | 失败MessageKey、技术详情、适配器日志、外部UI精确匹配词 | 用DisplayNames和资源提供操作摘要；错误代码/路径/技术详情/外部匹配词不能批量翻译。技术字面量登记不新增用户入口。 |

## 三个硬编码中文字符串的处理

| 冻结源码 | 现值 | C01处理 |
| --- | --- | --- |
| SettingsViewModel.cs:30 | 简体中文 | 语言的自称，保留；不是待缩短的错误提示。 |
| GuardedMeituUiDriver.cs:2573 | 自动选择 | 美图外部UI精确匹配词，保留原值，不能以文案统一为由翻译/替换。 |
| MeituAutomationOptions.cs:100 | 图片编辑 | 外部窗口身份配置，保留原值，不能作为PrintFlow标题改写。 |

## 页面映射与未绑定资源

每个已实现页面/状态都有编辑复核集合，原始source锚点、condition和routes被逐项保留。源准备接管、美图接管与TIFF接管均独立登记；素材路线仍没有印刷尺寸/白墨/TIFF。资源集合是跨消费者复核的起点，不能通过行数或资源数量证明模板显隐正确。

有些资源仅用于格式、错误原因、共用诊断或遗留入口，没有被启发式页面集合直接选中；COPY_INVENTORY仍全部保留，并用consumers/D/B/L索引定位。空pageStateIds不代表可删除；需要按具体消费者确定页面归属。后续任务遇到间接解析或多消费者键，先核对每个真实显示条件，再决定保持共用或另设资源键。

7个AI条件项只保留U01中的需求、附件、Prompt、生成、候选与导入等规划来源；当前无对应已实现业务页。既有Home_GuidedAiUnavailable提示照常登记；不为条件项虚构当前文案、按钮、命令、审核或新AI业务。

## 既有用语依据

- SCRUM-11139：沿用原件、当前结果审核、修正交接、恢复与交付安全合同。依据仓库 `docs/codex/printflow-operator-ux-backlog/PLAN.md`，不改旧Epic完成定义。
- SCRUM-11153：沿用“等待你检查”“已停止或失败”“需要你操作”和精确结果保存历史语义。依据 `SCRUM-11153_COPY_REVIEW.md`。
- SCRUM-11154：沿用“印刷尺寸/最大印刷尺寸”“驳回此 TIFF”，保留历史≠当前、审核≠保存≠完成及原有可见发现整改。依据 `SCRUM-11154_COPY_REVIEW.md` 与中文发现整改记录。

这些是来源复用，不代表旧人工文案验收、实际键鼠/DPI/读屏或真人走查在本轮已完成。
