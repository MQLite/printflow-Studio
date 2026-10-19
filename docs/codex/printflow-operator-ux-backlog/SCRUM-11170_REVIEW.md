# SCRUM-11170 独立整体审查与处置

## 审查身份和范围

唯一一次独立整体审查，2026-10-19。机制：spawn_agent，fork_turns=none，不继承实现者/控制聊天历史；没有审查子代理。实际运行 gpt-6-astra/high，turn_context 2026-10-19T05:20:34.337Z，session 01a1529a-b0cc-7d71-88a4-ca2ad2b9f39d。RouteOffset=0；UI/交互/视觉整体审查，development-routing v2.4。

范围：55657e37badb406cbc96a0d8db24abe6386d932d..6df111bf07243c9a18a80995b927c0db9b5cf586。原始要求为 SCRUM-11170_JIRA_SCOPE.json 和已批准 v2 design-spec.html，读当前diff、原始TRX、实际PNG及RULINGS；未以实现者RESULTS自评替代审查。审查没有修改源、测试、索引或分支。

## 独立发现（保留审查者原分级）

### R1 Important：未选择路线的任务可通过继续绕过显式选择

原位置 src/PrintFlow.App/ViewModels/HomeViewModel.cs:649。导入图片→不选择卡片→返回首页→继续，ResumeAsync直接进入Session，临时PrepareAsset变为实际路线。独立探针：WasExplicitSelectInvoked=false，ResumedRoute=PrepareAsset，ResumedStep=OriginalConfirmation，Destination=Session，CanConfirmOriginal=true。违反I02用户明确选择要求。基础Resume行为在base已存在，本轮相关AC未封闭，不是E3新引入回归。

建议：尚处于原图确认且路线仍可选的恢复入口先进入路线选择；缺少可靠已选择标记时再次明确选择，不能把临时路线推断为意图；无需新增引擎或迁移。

### R2 Minor：取消已提交导入的等待后当前列表未显示提示所指任务

原位置 HomeViewModel.cs:754。已提交但延迟返回的导入被取消等待后，提示可在最近任务找到，列表未刷新。探针：Persisted=1、ContinueRows=0、RecentRows=0。任务未丢失，可手动刷新，但提示缺少明确的刷新引导，可能使人误以为未创建而再次导入。建议仅在当前Home仍拥有上下文时刷新并保留取消提示，或提供就地刷新动作；不能削弱离页后迟到结果保护。

### R3 Minor：路线输出名称草稿离开时无提示丢失

原位置 src/PrintFlow.App/ViewModels/WorkflowSelectionViewModel.cs:501。输入operator-new-output-name后返回首页，BackToHomeAsync直接退役导航，持久化名称仍为原名称，重进无法恢复输入。探针：NavigatedImmediately=true、Typed=operator-new-output-name、Persisted=never-chose-a-route。批准书面设计§5要求离开未提交输入与放弃任务分别确认。是既有行为在本轮路线页范围内的设计缺口，不是新增数据损坏。

建议有真实编辑时提供继续编辑／放弃输入并离开；无编辑直接返回，不能以自动保存替代用户确认。

## 控制会话验证与重定级

控制会话已直接读取独立probe-final.log并核对Resume及BackToHome源码。R1维持Important。R3上调Important：无提示丢弃操作者输入、重进恢复默认名，可能造成错误交付名；也缺少已批准的草稿离开决策。R2维持Minor：持久事实完整且现有刷新可恢复，明确延期，不混入一次修复。

本次一次统一修复范围：R1、R3，由原Native执行者补真实RED→GREEN与相关最终回归；没有第二轮审查。若实现中发现必须改变持久化或引擎合同，不能默认为获批。R2在最终报告明确列为deferred minor及刷新代价。

## 独立Review Focus结论

- 离页导入：_active覆盖成功/失败/取消异常，不抢导航或新提示；留在Home取消的列表问题见R2。
- 旧路线UIA/持有按键跨重绑：EntryActionScope更新身份，按钮和编辑器验证捕获身份；独立聚焦测试通过。
- 异步名称/路线跨Open：捕获session、名称、generation，await后和finally查所有权，旧操作不改新任务或busy。已提交旧事实不回滚合理。
- 格式/损坏/多文件：picker/drop最终共用ImportAsync权威，原件/快照/拒绝断言保留，无新格式猜测或快照旁路。
- PDF/PSD：源类型与OriginalConfirmation限制展示，PDF页号/300PPI/实际尺寸保留；后续印刷/白墨/保存未提前展开。

I01主要符合，R2为恢复可见性缺口；I02三卡/路线锁/合法返回成立，R1及R3待处置；I03不可用说明真实，完整集成仍依赖阻塞。

## 独立证据

查看13张实际中文PNG，覆盖三窗口、三路线原图、导入中/拒绝、PDF/PSD准备与审核，对照批准的三核心原型及完整规范。关键操作可见，最小窗口环境区需滚动，符合次级定位。

独立安全测试24/24 PASS，使用--no-build --no-restore及全部11项精确排除。原最终广筛1375过/14失败，14项与E2同名同消息；持久化277/277、目标160/160。四处旧测试适配未放宽业务核心断言。

原始探针、源码、过滤器、TRX与日志复制至 output/ui/SCRUM-11170/review/；原始合成数据库位置保留在日志，源位置 C:/Users/admin/.codex/visualizations/2026/10/19/01a15270-969f-71a2-be47-942b2badb0bf/e3-review-probes/，没有复制客户数据。

## Declined to judge与控制会话裁定

1. 完整AI：11166尚未具备，按批准条件保留BLOCKED_DEPENDENCY；后续能力可接入时再完成I03，代价是当前无AI可用承诺。
2. 真实键盘/设备UIA/DPI/工作站/新手/生产：本轮禁止执行，NOT RUN；离屏证据不代替，代价是这些实际验收仍欠缺。
3. 14项继承失败和既有真实键盘异常：保持原状态，不扩修其他Epic，不宣称修复；代价是遗留故障继续存在。

独立原结论：With fixes，无Critical、1 Important、2 Minor。控制重定级：无Critical、2 Important待一次修复、1 Minor延期。修复和最终验证由后续记录追加；当前本文件不是用户验收或Jira Done。


## 原Native执行者统一修复记录

按控制会话裁定只修R1/R3，未另起整体审查。R1现在仅对服务返回的OriginalConfirmation/Waiting且允许SelectWorkflow的任务进入选择，继续、打开恢复及重启恢复共用；已确认原图/源准备审核/后续或中断位置不退回。由于没有可靠显式选择持久标记，未确认原图重复继续需要再次选择，这是明确接受的成本。12条新真实服务用例RED5/12后，相关Home/Recovery48/48通过。

R3现在区分干净名称与真实草稿，采用E2继续编辑/放弃输入样式和语义，不自动保存，不放弃整项任务。名称或路线提交期间禁用Back；保留外部Open/Deactivate世代保护。当前请求引用+generation、普通控件模态禁用和中性标题焦点阻止旧离开请求/UIA/值输入跨上下文。初始RED3/3；扩展模态中性焦点RED1/9；最终草稿/路线/输出名26/26通过。旧pending-route测试改为真正Deactivate以保留晚到回调断言，busy Back本身由新测试明确覆盖。

四张新增离屏中文PNG已逐张查看（恢复选择1000x700；草稿决策1000x700、1280x720、1920x1080），review-visual3/3。这不是实际设备键盘或DPI验收。R2保持Deferred Minor，当前列表可能需现有Refresh才能看到已提交任务；任务未丢失但提示仍不足。完整AI依赖、14项继承失败和真实设备/生产NOT RUN裁定不变。

最终安全广筛 review-ui-architecture-final2：1399通过/14失败/0跳过（1413），6分22秒。对E2精确比较：14项同名同消息，无新增或缺失继承项。关联回归review-contract-green94/94；原图/草稿补充视觉3/3。最终状态由控制会话核验，本执行记录不构成第二次整体审查、用户验收或Jira Done。
