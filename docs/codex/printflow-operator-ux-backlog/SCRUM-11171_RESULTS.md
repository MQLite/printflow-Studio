# SCRUM-11171 — 处理、审核、裁切与印刷准备

沿用用户已批准的 v2 视觉与 Native 方式，在独立工作树完成 P01–P05 的 App 层呈现。BASE 为 `5c061dd15f23d2cc33a7e62dca9e130296b14a8a`。原始验收条件见 [Jira 范围快照](SCRUM-11171_JIRA_SCOPE.json)，维护边界见 [guided-processing-review-print.md](../../printflow/guided-processing-review-print.md)。独立整体审查由控制会话安排；本记录不代表用户已验收或 Jira Done。

## AC 对应

| 条件 | 实现与证据 |
| --- | --- |
| P01.1 当前应用/真实状态/不定进度 | 独立 Processing 卡读取运行时 phase/state；不显示伪百分比；GuidedProcessingTests，running/stopping PNG |
| P01.2 运行/停止中/失败/接管/等待人工 | 复用 Stop/TakeOver 的原语义、确认及保留状态警告；停止与接管实跑 fake adapter，无结果/审批写入；恢复及失败 PNG |
| P01.3 成功后必要审核 | 成功进入 ReviewRequired，下游未自动开始且 Reviews 为空；Successful_processing_enters_required_review_without_automatic_next_step |
| P01.4 关键阻塞和停止入口 | 原环境/模拟模式/外部应用保留提示不折叠；Stop/TakeOver 根与滚动视口几何；运行时重新打开也不提供更换路线 |
| P02.1 一致骨架/2–4检查要点 | 增强、去背景、裁切、TIFF 共用原指导，图片占主要列、指导280 DIP；四类审核PNG与GuidedProcessingReviewTests |
| P02.2 有意义的工具 | Before/After顺序、缩放/平移/背景/比较沿用；无有效双图隐藏比较，无图隐藏背景与100%；SharedReview回归；印刷准备预览独立适配窗口 |
| P02.3 合法决策/不等于已保存 | 原命令、合法可见性与通过文案保留；保存选项在审核页折叠、仍可明确展开，最终保存页展开 |
| P02.4 精确结果身份 | 保留原ReviewApprovalButton/TargetIdentity、预览失效和旧动作屏蔽；原SharedReviewAuthority/GuidedSessionReviewFix/ReviewGuidance及持久化回归 |
| P03.1 独立裁切工作区 | 图片跨两列，隐藏技术/保存表单；八个手柄、恢复/取消/对比/使用均可达；调整时只显示其适用的使用按钮 |
| P03.2 取消/新结果回审核 | Cancel_preserves_exact_review_and_using_changed_crop_requires_a_new_review：取消不改revision/review；使用后新revision及新review identity，未自动批准 |
| P03.3 原建议/非破坏/非法反馈 | 原裁切几何、建议恢复、对比和就地反馈保留；ManualCrop/AdjustTrim/CropHandle定向回归；crop-comparison PNG |
| P03.4 鼠标/键盘/单位 | 复用SCRUM-11147能力与中文引导，不改手势/键处理。合成控件回归不替代真实鼠标/键盘验收，继承失败保持；真实设备 NOT RUN |
| P04.1 合法打印尺寸阶段 | 打印页340 DIP表单，预设/自定义、毫米/像素/比例和实时preflight摘要；自定义时预设可展开且输入在最小视口内；素材路线无打印摘要 |
| P04.2 最大边界/实际尺寸/原值 | 请求值与像素取整后实际毫米分列，显示最多两位小数；123.456789输入保持原值；不改算法、恢复精度或声称修复F6 |
| P04.3 风险/独立授权/非法输入 | 尺寸旁摘要直接显示放大风险；原额外授权独立，invalid无过时摘要且继续禁用；invalid/enlargement PNG |
| P04.4 另一个尺寸独立身份 | 未改业务服务；AddAnotherSize/FlexibleSize/FinalSaveCoordinator/ReturnToStep等相关持久化回归，未复用其他尺寸审批 |
| P05.1 显式白墨的批准解释 | 按批准v2：两条TIFF路线都显式选择0/1/2 px及原分类原因，无默认值；素材路线不显示；六条双路线分支测试 |
| P05.2 生成前摘要与可见风险 | 请求/实际毫米/像素/白墨/300 PPI/CMYK/W1摘要；技术信息折叠、阻塞与额外放大风险保留；preflight PNG |
| P05.3 原生产规则与门禁 | 原业务命令、环境门禁、生产预设/TIFF检查不变；App无授权扩张；EnvironmentGate/PhotoshopTiffWorkflowOutput及UI回归 |
| P05.4 返回失效/精确TIFF终审 | 原ReturnToStep/下游失效与PhotoshopTiffFinalReview/FinalSave机制保留；生产TIFF审核专属预览仍使用既有解码 |

## 验证

仅使用合成/GUID临时文件、临时SQLite、fake adapters和离屏WPF UserControl。所有运行使用明确过滤及继承的11项排除；E4另外统一排除整个Smoke命名空间。任务2早期过滤曾命中未启用的opt-in no-op；未设置PRINTFLOW opt-in变量，未运行工作站操作，后续均排除。没有App实际启动、Window.Show/ShowDialog、桌面输入、真实Meitu/Photoshop或客户/生产数据。

真实键盘、实际UIA设备、DPI缩放、中文新手走查、生产输出：**NOT RUN**。截图为96 DPI RenderTargetBitmap，不能作为上述验收。每次运行重叠覆盖，计数不可相加为独立测试总数。

| 运行 | 通过/失败 | 含义 |
| --- | --- | --- |
| baseline-scoped | 89/0 | 实施前当前相关基线 |
| task1-red-behavior → task1-green | 1/2 → 68/0 | 真实运行/停止/接管与成功后必要审核 |
| task2-red → task2-green | 0/6 → 120/0 | 主要图片区域、有意义工具、保存披露；早期opt-in no-op已说明 |
| task3-red → task3-green-final | 1/2 → 188/0 | 裁切区域/动作/精确新结果 |
| task4-red-behavior → task4-green | 1/8 → 75/5 | 九个新打印测试全过；五失败与E3同名同消息 |
| visual-refinement-red → green | 3/7 → 10/0 | 源页面比例误缩、自定义输入下折、重复裁切动作、运行中改路线 |
| preview-area-red → e4-visual-verified | 1/2 → 52/0 | 目视发现打印图片过小；上下文工具/适配预览修复；含全部E4与SharedReview定向检查 |

最终广筛、持久化及停止状态补充结果由下方最终记录列出。原始TRX/log/filter、编译和夹具失败均保留在 [checks](../../../output/ui/SCRUM-11171/checks/)。未将编译/跨STA错误冒充行为RED，未削弱继承断言。

## 视觉与维护

[证据索引](../../../output/ui/SCRUM-11171/INDEX.md) 指定最终28张PNG/JSON与逐张检查。1000×700、1280×720、1920×1080 DIP涵盖处理/审核/裁切/尺寸/白墨/确认；最小窗口另有停止、接管、恢复、非法尺寸和放大阻塞。初次截图的停止状态存在fake完成过快的竞争；最终补充冻结真实StopRequested事件与运行会话快照，断言屏幕确实为停止中。生产代码没有为截图修改运行时。

主要裁决和代价见 [RULINGS](../../../output/ui/SCRUM-11171/RULINGS.md)。未改Domain/Workflow/Infrastructure、业务持久化、外部自动化或AI；未merge/push/deploy、写Jira、触及其他Epic。主检出与旧工作树保持只读。独立审查前不宣告用户验收完成。

## First broad run and targeted follow-up

`e4-ui-architecture-final.trx`：1438项，1397通过/41失败/0跳过。`e4-ui-architecture-final-comparison.json`核对接受的E3 `review-ui-architecture-final2.trx`：14项失败名称和消息完全相同；另27项新观察必须单独处置。

27项分布：11项未实现模板的预设内容可见性、8项预设说明默认展开假设、6项保存选项/旧页面比较假设，以及2项键盘激活断言。对预设内容补充当前页可见性；旧布局测试明确展开披露，仍保留所有文本换行/滚动/可达断言。保存测试展开保存选项，并让无coordinator比较模型读取相同的最新持久化状态，避免将已批准的碰撞保存页面与旧待审页面比较。业务/身份/保存断言未删除。

`e4-broad-contracts-green.trx`：76/76通过，包括全部上述受影响类别、两个未修改的键盘用例及最终停止状态快照。`ReviewApprovalButton`、手势护栏和旧键盘测试均未修改，没有增加排除项。

## 最终检查

`e4-ui-architecture-final2.trx`：1438项，1424通过、14失败、0跳过。`e4-ui-architecture-final2-comparison.json`确认14项均与接受的E3基线名称和消息完全相同；新增失败0、消息变化0、基线失败消失0。首轮两个键盘失败在76项定向检查和最终1438项广筛均未复现，根因仍未确定，**不称已修复或继承失败**，详见`activation-investigation.json`。

28张最终PNG的SHA256全部复核一致；主检出13个已修改跟踪文件与启动快照哈希全部相同。`final-scope-check.json`记录检查结果；产品改动仅在App层。所有原始运行包含过滤、日志与完成时TRX，`run-index.json`区分未完成编译/夹具尝试，不能只挑成功记录作为历史。

`e4-workflow-persistence-final.trx`：277/277通过，0失败、0跳过。覆盖ReturnToStep、RetryAndReview、StopAndTakeOver、StartupRecovery、PDF/PSD preparation、TIFF output/final review、FinalSaveCoordinator、ColleagueCorrection、ManualCrop、TrimAdjustment、AddAnotherSize、FlexibleSize、EnvironmentGate和SessionService；使用E3同一相关集合，保留原11排除并额外排除Smoke。

最终验证边界重新评估：UI呈现、布局与视觉证据仍为Astra/high；纯持久化测试执行的正常/请求路线为Sol/high、RouteOffset=0。当前Native上下文无法原位切换，记录`MODEL_SWITCH_UNAVAILABLE`；该命令实际仍在已验证的`gpt-6-astra/high`执行，不声称降级。控制会话的独立核对使用其实际模型，并另行安排一个新的Astra/high整体UI审查。规则版本2.4，执行主机Windows/local；无发布操作。

## R1 独立审查修正

整体审查在`685b8d5ee59877498bef45a51c6bf6fc91d49f8b`发现1项Important：裁切保持编辑状态的命令失败，其编码提示被隐藏的详情列遮蔽。执行者独立核实后，只在裁切工作区增加21行XAML，绑定现有Notice、NoticeErrorCode与本地化诊断标签；`Session.Crop.*` ID独立，错误详情默认折叠、代码只读可复制。未修改ViewModel、服务、审批、裁切算法或恢复语义。

新`GuidedProcessingCropFailureTests`通过真实UseThisTrim命令和SessionService，在现有FaultingRepository的首次提交处拒绝写入；仅失败重载分支在读取边界注入错误，未直接设置Notice。zh-CN/en × 重载成功/失败四项均保留草稿、结果身份及数据库的步骤/尝试/修订/审批；并验证原因与代码可见、诊断展开不写入、取消/使用动作在1000×700内。`r1-crop-failure-red-behavior`四项因可见提示为0而失败；`r1-crop-failure-green`四项全过。初次编译空值标注错误另存`r1-crop-failure-red.log`，不作为行为RED。

新增两张中文最小窗口截图已逐张检查并写入最终清单，最终证据合计30张。原28张与所有中间证据保留。完整广筛1424/14和持久化277/277是R1之前的检查；此次仅改裁切的条件化消息放置，使用受影响筛选补充验证，不将旧广筛冒充修正后的结果。两个早期键盘激活零次失败仍根因未定，不声称修复或继承；真实键盘/UIA/DPI/新手/生产验收仍NOT RUN。

修正后`r1-crop-failure-affected.trx`：**251/251通过，0失败、0跳过**。覆盖全部GuidedProcessing、FailureGuidance、TrimAdjustment（含持久化）、ManualCropUi/Adjustment、CropHandleGesture、ReturnAndTrimControls及GuidedSessionProjection/Shell；过滤与原排除保存。30张最终图像哈希一致，主检出13个跟踪文件仍与启动快照一致。`r1-validation.json`明确修正后的结果与修正前完整广筛之间的边界。独立审查R1已由原执行者修正，控制会话最终集中验证待完成；本记录不冒充第二次独立审查或用户验收。
