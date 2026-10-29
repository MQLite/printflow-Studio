# SCRUM-11173 中文真实新手与工作站验收记录

本表是 Q03（SCRUM-11197）执行包，当前 **真人走查 NOT RUN；工作站输入、读屏与实际 DPI NOT RUN**。用户于2026-10-22选择“先准备走查材料，真人测试稍后安排”；本轮仅准备材料，实际参与者尚未确认。开发者说明、服务命令、离屏截图均不能填作参与者行为。

## 候选与现场启动门槛

候选身份、构建和静态验证见 [candidate](../../output/ui/SCRUM-11173/candidate/README.md)。使用该候选工作树内现有 `tests/PrintFlow.WorkstationEntry` 与 `tools/Start-WorkstationEntry.ps1`；它装载当前 App 的 Shell/MainWindow，顶部增加合成环境提示。不要使用产品安装目录、正常 App.exe、其他工作树或随手复制的旧 host。

1. 负责人先核对 candidate-provenance 的源码 HEAD、源码/资源哈希、五个产品/host DLL 的 SHA-256 与 MVID、构建日志哈希。源码/资源或二进制改变后重建新候选；不要改 manifest 来绕过拒绝。构建时不可变源码提交与后来纯文档 HEAD 分开核对；纯文档提交不会改变已绑定源码/资源哈希，无需因此重建。
2. 执行记录中的 `Validate` 命令；只接受 `STATIC_VALIDATED` 且 `GraphConstructed=false`。这一步不证明运行图、夹具已准备或可以启动真人走查。
3. 由负责人在单独获准的隔离工作站上执行 `PrepareAndSmoke`，使用候选 manifest 指定的全新 run root。保留退出码、ownership、typed prepared record、scenario-ledger、graph-smoke、startup/teardown evidence。检查窗口未显示、NativeDispatches=0、无非预期路径/外部进程。退出非零、PARTIAL、FAILED、BLOCKED_REQUIRED_SCENARIO 或没有匹配 prepared record 时停止；不得进入 Interactive。
4. 逐条检查场景来源和实际状态。F2/F3 的准备过程显式选择有效 fake TIFF，但 Interactive 是另一个进程，默认 fake Photoshop 的输出不能由上一进程设置推断。N4（PNG 字节伪装 TIFF）、F6（尺寸不一致）、PNG 保留原始范围审批和 generic recovery 均未被本轮关闭。审核当前候选的生成、解码、精确批准和交付证据后才准入对应场景；未证明安全的 TIFF 完整路线标 BLOCKED。
5. 负责人确认系统空闲、没有真实美图/Photoshop任务、只使用该 root 内已登记并校验哈希的合成资料；获得当次真实桌面操作许可后才加 `-SafeDesktopConfirmed` 执行 `Interactive`。这个参数不能从本文件或静态验证推断已经获准。当前代理没有执行此步骤。
6. 现场关闭使用窗口正常关闭；处理未停止时先按合法停止/接管并等状态稳定。遇到 host fault、未知文件选择、来源/哈希改变、真实软件被打开或输出落到 root 外，立即停止记录，保留证据交负责人处置；不强杀其他程序，不删 root，不重置生产配置。只有 host 支持且 ownership/candidate/scenario 均匹配时才 Resume。

Host 的 `PrepareAndSmoke` 会构造 WPF Application/MainWindow；即使不 Show，它也超出本轮仅离屏 UserControl 的执行边界，因此此处只提供负责人后续步骤。现有 host 的 PNG/TIFF 夹具不支持 PDF 准备；不要为了补齐表格改成生产适配器。

## 参与者与观察规范

| 字段 | 实际记录 |
| --- | --- |
| 匿名参与者代号 / 记录人代号 / 负责人 | UNOBSERVED |
| 中文阅读与岗位（如门店接单/制图/操作员） | UNOBSERVED |
| 是否未使用过本应用；过往类似工具经验 | UNOBSERVED；由本人确认，不推断 |
| 是否参与过开发、演示或本轮测试 | UNOBSERVED；开发者演示不作新手证据 |
| 候选源码提交、manifest SHA-256、host DLL SHA-256 | UNOBSERVED |
| run root、prepared record、scenario ledger 哈希 | UNOBSERVED |
| 实际 Windows/显示器/工作区像素/缩放/客户区 DIP | UNOBSERVED |
| 输入设备、读屏软件和版本、中文语言 | UNOBSERVED |
| 参与者同意匿名文字/合成截图记录 | UNOBSERVED |

主持人只读下表任务卡，不指按钮、不讲正确路线、不代操作。参加者求助时先原样记下问题，再记录主持人的每句协助；经协助的完成不计“独立完成”。不设置工时、耗时或改善百分比 KPI。不要求参加者使用客户图、个人路径或真实订单。截图只保留合成内容与匿名标识。

## 共享场景与中性任务卡

沿用 SCRUM-11155 的 A01–A12 场景编号；原始 `D:/Repositories/printflow-Studio/docs/printflow/operator-ux-acceptance-walkthrough.md` 和 `docs/codex/printflow-operator-ux-backlog/SCRUM-11155_RESULTS.md` 为只读历史来源。下表是当前 guided 候选的执行映射，不回写历史，不关闭旧 Epic。现有 discovery 没有真实参与者观察，比较栏应为 NOT AVAILABLE，不能补造改版前基线。

| 共享编号 | 给参加者的中文任务卡 | 负责人准备与限制 |
| --- | --- | --- |
| A01 首页/继续 | “请看看这里有哪些事情需要处理，选择你认为该继续的任务。” | 仅使用准备 ledger 记录的 F5 Recent；记录选择和其理由。预置任务不证明参与者做过之前转换。 |
| A02 素材路线 | “这张合成图片将留作以后设计用的素材。请选择合适方式并处理到可以保存。” | F1 合成透明边缘图；开始全新任务。正常裁切链可观察；KeepOriginalExtent→PNG 审批缺口单列 BLOCKED，不绕过。 |
| A03 客户设计 | “这是一张已经排好版的合成设计，请把它制作成打印 TIFF。” | F2C 2400×1800；仅在当前 Interactive 的 TIFF 链通过负责人门槛后准入，N4/F6 不可由预制成功豁免。 |
| A04 直接 TIFF | “店内设计已经完成，现在只需要制作打印 TIFF。请选合适方式继续。” | F2D 同尺寸合成图，独立新任务，限制同 A03。 |
| A05 状态/下一步 | “请告诉我现在发生了什么，你认为下一步应该做什么，然后继续。” | 在真实到达的需输入、处理中、审核、失败、移交、保存和完成状态分别记录；不能伪造处理等待。 |
| A06 审核/求助 | “请检查这个结果，决定是否接受。说说你检查了什么；不确定时按你认为合适的方法处理。” | F1/已准入 F2 当前精确结果；记录驳回/重试/重新审核。模拟操作不替代人工判断。 |
| A07 裁切 | “建议保留的范围稍有不合适，请调整到你认为合适的位置，再决定保留哪个范围。” | F1 1200×900、透明内容900×600；观察手柄、恢复建议、取消、使用与重新审核。不可提示控件名称。 |
| A08 修正 | “去背景结果需要同事修正。请准备交接，再把准备好的修正结果带回这个任务检查。” | F4 640×480；匹配任务/修订的回导文件必须由当前准备记录准入。没有当前身份匹配文件则 BLOCKED；无自动监控、上传或 TIFF 手工回导。 |
| A09 尺寸/另一尺寸 | “请按任务卡指定的尺寸制作结果，并说明你认为尺寸会怎样影响图片。随后再准备另一种尺寸。” | F2 长边152.4 mm（1800×1350）；F3 600×400→长边152.4 mm（1800×1200需放大），再101.6 mm。最大边界200×150与6×5 F6是已知负例，不计成功。白墨分支必须明确选择。 |
| A10 保存/完成 | “请保存这份最终结果，找到它所在的位置，再告诉我任务现在是否完成。” | 只用 root 内允许的交付目录，准备同名合成文件观察冲突取消/改名；不允许覆盖；记录批准/保存/完成分别是否理解。新生成 TIFF 未准入则 BLOCKED。 |
| A11 失败/恢复 | “处理没有成功，请判断图片是否还在、可以做什么，再尝试你认为合适的下一步。” | F5 预定超时/重试事实；真正重启中断需独立 OwnedRestart 授权与匹配证据。generic recovery 没有准备起点则 BLOCKED，不能制造生产故障。 |
| A12 全程用语 | “有哪些提示不好理解？结果在哪里？还有什么需要你处理？” | 收集原话和具体屏幕，不能替参与者归纳为满意。视觉负责人另检查与已批准主题一致性。 |

## 每次实际尝试记录（当前均未执行）

每一次重试新增一行，保留原失败。结果只允许 NOT RUN / BLOCKED / FAILED / COMPLETED WITH HELP / COMPLETED INDEPENDENTLY；不把可见但未操作写成完成。

| 场景 | 结果 | 独立完成 | 求助原话与实际协助 | 错误操作/走错步骤 | 参与者原话 | 证据文件与候选 | discovery 比较 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| A01 | NOT RUN | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT AVAILABLE |
| A02 | NOT RUN | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT AVAILABLE |
| A03 | NOT RUN | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT AVAILABLE |
| A04 | NOT RUN | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT AVAILABLE |
| A05 | NOT RUN | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT AVAILABLE |
| A06 | NOT RUN | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT AVAILABLE |
| A07 | NOT RUN | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT AVAILABLE |
| A08 | NOT RUN | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT AVAILABLE |
| A09 | NOT RUN | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT AVAILABLE |
| A10 | NOT RUN | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT AVAILABLE |
| A11 | NOT RUN | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT AVAILABLE |
| A12 | NOT RUN | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | UNOBSERVED | NOT AVAILABLE |

## 实际工作站矩阵

批准设计 §7 和 MainWindow.xaml 的 1000×700 DIP 是 **WPF 外部窗口尺寸基线**（Width/Height），不是客户区尺寸，也不是已声明的 MinWidth/MinHeight。下表保留该窗口目标；实际客户区会受标题栏/边框等影响，必须另外测量，不能把离屏 UserControl 的 1000×700 viewport 当作实际窗口客户区。离屏三尺寸覆盖因此不能证明批准外框尺寸下较小客户区的布局。系统缩放、显示器分辨率、物理工作区、实际外框与客户区 DIP/像素均分别记录。若显示器不足，标 UNSUPPORTED ON THIS WORKSTATION 并记录可用上限/后续处置；不减去猜测边框、不缩放 PNG 冒充实测。

| WPF 窗口目标 DIP | 100% 名义窗口像素 | 125% 名义窗口像素 | 150% 名义窗口像素 | 当前实际结果 |
| --- | --- | --- | --- | --- |
| 1000×700 | 1000×700 | 1250×875 | 1500×1050 | 三格均 NOT RUN |
| 1280×720 | 1280×720 | 1600×900 | 1920×1080 | 三格均 NOT RUN |
| 1920×1080 | 1920×1080 | 2400×1350 | 2880×1620 | 三格均 NOT RUN |

每格检查首页、选路线、原图、长中文、审核、裁切、尺寸/白墨、保存/多尺寸、失败和辅助页：图片有效区域、主操作/提示无遮挡、滚动可达且固定栏不压工作区。逐项记录 Tab/Shift+Tab 顺序、可见焦点、滚动自动跟随、隐藏/折叠内容不进入操作序列；Enter/Space长按与释放、换结果后余波、鼠标双击、文件/目录对话框取消；读屏中文名称/角色/状态和焦点回落。实际读屏可能与直接 WPF peer 结果不同，应保留失败，不用离屏通过覆盖。

## 负责人视觉与缺陷记录

| 检查项 | 结果 | 负责人/证据 |
| --- | --- | --- |
| 与批准 v2 的左侧步骤、当前任务工作区、固定操作栏一致 | NOT RUN（负责人） | UNOBSERVED |
| 中文层级、对比、换行、长路径/文件名、多尺寸、错误与空状态 | NOT RUN（负责人） | UNOBSERVED |
| 实际设备矩阵与辅助技术 | NOT RUN | UNOBSERVED |
| 新手原话、失败和协助完整保留 | NOT RUN | UNOBSERVED |

| 缺陷编号/场景/候选 | 原始观察与证据 | 严重程度及用户影响 | 责任人/关联任务 | 回归条件、证据与结论 |
| --- | --- | --- | --- | --- |
| 尚无本轮真人观察 | UNOBSERVED | UNOBSERVED | 待负责人分派 | NOT RUN |

分级以用户影响为准：阻止合法完成、错误审批/数据风险为 Important/Critical；可绕过的局部文案或外观为 Minor，并记录代价。旧缺陷见 [COVERAGE](../../output/ui/SCRUM-11173/COVERAGE.md)，不要混成新增回归。本表完成后仍需逐项评估 E6 与 11155 的原 AC；无真人证据不得宣称“新手验证完成”，任何结论均不授权发布或生产操作。
