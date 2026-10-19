# SCRUM-11170 — 开始任务、导入与路线选择

本轮沿用用户已批准的 v2 视觉和 Native 执行方式，在独立工作树完成 Home、显式路线选择、原图确认的引导式整理。原始范围见 [Jira 快照](SCRUM-11170_JIRA_SCOPE.json)，维护说明见 [guided-import-navigation.md](../../printflow/guided-import-navigation.md)。

I01/I02 已实现并完成本轮自动化验证，待独立整体审查；I03 仅“不可用入口不阻塞本地路线”具备证据，完整 AI 集成保持 BLOCKED_DEPENDENCY（SCRUM-11166 仍为 To Do）。本记录不等于真人验收或 Jira Done。未 push、merge、deploy、改生产数据或进入后续 Epic。

## AC 对应

| 条件 | 实现与证据 |
| --- | --- |
| I01.1 导入/继续优先，环境/历史/设置次级 | Home import card、ContinueSessions、共用行模板、历史/环境 disclosure；三尺寸 Home PNG；Import_is_primary_and_above_workstation_information |
| I01.2 单文件校验与原件不变 | 拖放/选择继续走同一 ImportAsync；原有格式、损坏、多文件、InputSnapshot 回归保留；多文件拒绝 PNG 与就近反馈几何断言 |
| I01.3 导入中/失败清楚，无静默覆盖 | 明确进行中与取消等待；Cancel/Deactivate 后迟到完成不得导航；Home 旧 UIA/卸载/重绑保护；真实服务保留原活动任务规则 |
| I01.4 成功后合法路线步骤、源摘要和预览 | 现有先导入再明确选路线；源文件名/预览/输出名称保留；三尺寸 route-choice PNG；既有 HomeAndWorkflowSelection/preview/name 持久化回归 |
| I02.1 三路线用途/PNG-TIFF/尺寸 | 三列紧凑卡；保留目的/结果/印刷尺寸信息；移除整段未来步骤；三按钮同时处于最小视口内 |
| I02.2 首个结果后锁路线/返回说明 | 现有 SelectWorkflow 门禁；新 Session.ChangeRoute 在合法时进入路线选择，复用离开决策；busy 文案不误说已锁定；迟到 rename/select、旧输入不跨任务 |
| I02.3 原图/名称/PDF/PSD 时机 | OriginalSourceSummary、ShowsGuidedPdfSource/ShowsGuidedPsdSource；源准备/审核显示、后续折叠；4个PDF/PSD页面与真实准备服务/测试适配器 |
| I02.4 当前任务专属内容 | E2投影复用；原图页不显示白墨/印刷参数/保存表单；三路线各三尺寸PNG；未新增编辑器或输入快照机制 |
| I03.1/2/3 AI所有权与完整分步/手动交接 | BLOCKED_DEPENDENCY；没有已有AI业务可接，不实现假上传/Prompt/生成/候选/进度 |
| I03.4 不可用入口且不阻塞三路线 | 中英 unavailable 文案，没有 AI 命令；Unavailable_ai_is_information_and_all_local_routes_still_reach_their_original_step |

## 验证边界与过程

仅合成/GUID临时文件、临时 SQLite、fake picker/adapters 与离屏 WPF；所有测试使用同一11项排除脚本。没有 App 实际启动、Window.Show/ShowDialog、工作站 opt-in、桌面输入、真实 Meitu/Photoshop 或生产。部分既有测试名含 Production_smoke，但其夹具是 TempApplication/Stub，这不是生产验收。

真实键盘/实际UIA设备、DPI缩放、中文新手走查和生产输出：NOT RUN。96 DPI RenderTargetBitmap 只证明离屏布局/控件事实。每次运行计数不可相加作为独立测试总数。

| 运行 | 通过/失败 | 说明 |
| --- | --- | --- |
| e3-baseline-targeted | 136/0 | 改动前鲜定向基线 |
| e3-home-red → e3-home-green3 | 0/5 → 121/0 | 导入/生命周期与Home相关回归 |
| e3-route-red + busy-red → route-green2 | 1/6 + 0/1 → 50/0 | 三卡/迟到操作/旧UIA与键输入 |
| e3-source-red3 + route-return-red → source-green3 | 1/4 + 0/2 → 154/0 | 源信息时机/合法返回/源准备 |
| e3-visual-layout-red → layout-green | 0/2 → 20/0 | 人工审图发现的按钮裁切和拒绝反馈位置 |
| e3-visual-final | 6/0 | 21张PNG及JSON；逐张查看；重要按钮须同时在根及滚动视口内 |

原始 RED、编译错误、首次测试夹具跨STA问题、失败及后续修复日志都保留在 [checks](../../../output/ui/SCRUM-11170/checks/)。不删除中间失败，不把 e3-visual3 初次6/6当最终视觉通过：随后审图发现根bounds不足以排除ScrollViewer裁切，新增真正失败的视口测试后修复。

## 已作出的合同调整与裁决

- 历史行测试显式展开新的次级历史入口；保留原文件/持久化/精确身份断言。代价：真实键盘展开仍待人工验收。
- 原路线按钮“在文字右侧”几何改为“在文字下方且不重叠、位于卡片及视口内”；中英换行、宽度、命令和输出说明均保留。代价：布局基准改变，已有三尺寸证据。
- 共享 PDF/PSD 源信息移除对将来 TIFF/印刷尺寸的错误承诺；术语测试只从实际印刷步骤列表移除共享源说明，同时增加中英PNG/无TIFF/无印刷尺寸断言。代价：印刷流程依赖其稍后的当前步骤说明。
- smoke fixture 原来导航后调用退役Home.Refresh。改用新瞬态Home独立读取最近任务，并补充它与 live session.Id 相同断言；保留下游完整流程/审核/导出断言。代价：测试多构建一个瞬态模型。
- Back/Cancel只退休当前UI等待，不撤销已经提交的导入或名称/路线事务，也不自动重试；正常最近任务/恢复保持权威。代价：完成的任务仍可能出现在最近任务中。
- PSD测试适配器内部可见性扩大到 internal 以共用既有合成处理器，不新增生产接口。代价：测试夹具可见性稍宽。

Native 实际 Astra/high，RouteOffset=0；父控制会话核对session 01a15273-3593-7c80-af03-4203e055fc1e / 2026-10-19T04:37:27.245Z turn_context。计划后与当前视觉/验证边界均复评：生命周期、WPF输入和页面几何仍紧密耦合，保持同一执行上下文；不声称降档或换模型。未产生执行者子代理。唯一全新整体审查由控制会话在本轮交付后安排。

首次Home基线/RED曾短暂重叠编译，文件复制重试后各自完成；后续测试顺序执行。原主检出及约70MB参考gallery只读。scratch与所有裁决保留。
## 首次广筛与晚到恢复补充

首次 e3-ui-architecture-final：1385项，1356过/29失败/0跳过，测试时长6分7秒。精确比较 [first-broad-comparison.json](../../../output/ui/SCRUM-11170/checks/first-broad-comparison.json)：14个继承失败与E2相同名称且错误消息完全一致；15个新失败为14个 SessionSmoke 共享fixture使用已退役Home，以及1个源说明术语合同。二者适配如上，不冒充继承问题。

随后主动检查发现继续/打开恢复/重启恢复的迟到完成也可能抢回已离开的页面。e3-late-continue-red 三例全部真实失败；三个路径在服务await后、恢复刷新前后检查Home仍有效。已提交的恢复事实保持，由下一次正常访问读取；代价是旧页面的完成不再自动跳转。e3-broad-contracts-green 160/160通过（25秒），包含三个回归、全部合成SessionSmoke、本地化与Home恢复相关用例。

计划、授权、AI边界、取消后的持久事实、测试合同、上下文路由和所有裁决/代价的逐项记录见 [RULINGS](../../../output/ui/SCRUM-11170/RULINGS.md)。保留scratch，不把经验判断视为人工验收。


## 最终安全广筛

`e3-ui-architecture-final2.trx`：1389项，1375通过/14失败/0跳过，测试时长6分14秒；TRX开始2026-10-19 18:09:32 +13:00，结束18:15:50。最终 [baseline-comparison.json](../../../output/ui/SCRUM-11170/checks/baseline-comparison.json) 对照用户已接受E2的 `review-ui-architecture-final.trx`：14项同名且错误消息逐字相同，NewFailureNames与BaselineFailuresNotReproduced均为空。不能称全量通过，也未修复这14项旧问题。

继承失败分布：HomeRecentAccessibility 2、ManualCropAdjustment 2、KeepOriginalExtent 3、PrintDimensionsPreflight 5、OperatorCultureIsolation 1、OperatorWave1AHost非可见smoke 1。未放宽原安全断言、未增加排除项；11项可见/opt-in排除名单与E2完全相同（safety-scope.json）。

## 最终交付与待审查状态

`e3-workflow-persistence-final.trx` 277/277通过，测试时长1分29秒，包含既有ReturnToStep、RetryAndReview、Stop/TakeOver、StartupRecovery、PDF/PSD准备、TIFF输出/审核、最终保存、同事修正、裁切、再做尺寸、环境门禁和SessionService。安全脚本仍应用相同11项排除；未扩大范围。最终机器索引见run-index.json，各次计数不可相加。

I01/I02实现及自动化/视觉证据已备齐，交回控制会话进行唯一全新整体审查；I03完整集成BLOCKED_DEPENDENCY，只有不可用提示与三本地路线不阻塞已验证。真人键盘、DPI、新手、实际工作站和生产验证继续NOT RUN。无提交到远端、发布、生产或Jira状态修改。

提交顺序：f4a7ab0 Home；66997ee路线；03a289a源信息/合法返回；最后一笔Task4提交保留视觉修复、晚到恢复保护、测试合同更新和所有证据，确切HEAD见控制会话交接。任务实现范围以基线55657e37到该HEAD为准。源/测试diff另存checks/source-and-tests.diff；不含参考gallery或其他工作树内容。安全源代码检查 `git diff --cached --check -- src tests docs` 通过。
