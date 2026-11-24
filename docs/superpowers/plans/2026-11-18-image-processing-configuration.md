# PrintFlow Studio 图像处理配置实施计划

**状态**　待审批 · 2026-11-18  
**目标**　在「设置 → 图像处理」为清晰化、去背景和裁切选择工作站默认方式，让操作员在任务中看见将使用的方法、复核处理结果，并在失败后选用可用的替代方式。

**架构**　工作流继续负责素材版本、步骤状态和审核。设置页保存偏好，任务保存当前选择，处理尝试保存实际使用的方法和版本。工作流按实际后端决定运行条件，基础设施层执行本地处理、外部桌面自动化或 API 请求。

**技术栈**　现有 .NET 10／WPF／SQLite。传统锐化用 Magick.NET，AI 超分评估 Real-ESRGAN，抠图用 ONNX Runtime＋BiRefNet Lite，云端接 OpenAI Images。算法与服务选型见[前期调研](../../printflow/image-processing-alternatives-2026-11-17.md)。

## 1. 设计与范围

- 「准备设计素材」「准备客户设计」采用这组配置。「生成生产 TIFF」从已完成设计开始，现有流程没有这三个步骤。
- 缺省和升级后的行为为美图清晰化、美图去背景、现有 Alpha 自动裁切。保存新默认值只影响之后创建的任务。已打开或恢复的任务继续用上次保存的选择，可以在任务内显式调整未处理的步骤。
- 生产 CMYK／W1 TIFF 在本计划中沿用 Photoshop Action、生产尺寸规则和现有 TIFF 验证。Magick.NET 普通 TIFF 与兼容蒙泰的 W1 写入可按前期调研独立立项。
- 比较过三条路线。全内嵌能离线、无按张费用，但需要验证模型质量和安装包。纯 SaaS 接入快，但客户素材会发到外部、结果和费用受服务影响。推荐兼容式迁移，先把现有方式做成可配置并加入本地选项，随后提供显式启用的 OpenAI 模式。

## 2. 配置页和任务入口

**设置 → 图像处理**使用现有设置导航和中英文资源，添加独立内容视图。默认显示三个类别、方式下拉框、适用条件与本机可用状态。操作员可恢复默认、保存、返回。离开有未保存的改动时，提供保存、放弃和继续编辑。

| 字段 | 可选方式 | 界面细节 |
| --- | --- | --- |
| 清晰化 | 美图现有方式／本地轻度锐化／本地 AI 超分／OpenAI 照片修复／跳过 | 模型项显示可用性。任务开始前显示方法，有确定目标时显示输出尺寸。OpenAI 标注「上传图片，按量计费」。不支持 AI 超分的电脑仍可选轻度锐化。 |
| 去背景 | 美图现有方式／本地 BiRefNet 抠图／OpenAI 生成式去背景／保留背景 | 美图沿用现有识别选项。本地 BiRefNet 首版提供通用模式，OpenAI 显示可能重绘主体。任务上已有透明图可显式保留原图。 |
| 裁切 | Alpha 自动裁切／手动框选／保留原画布 | 自动项显示现有安全边距和去小杂点开关，手动项进入任务内框选，保留项沿用原幅命令。全透明或透明度不可判定时给出人工处理入口。 |
| OpenAI 合并处理 | 开启「一次完成清晰化＋去背景」 | 作为成组方式。开启后两行都显示「OpenAI 合并处理」，关闭后恢复编辑前的独立选择。分别选择两项 OpenAI 会走两次请求、两次审核。 |

- 首次启用 OpenAI 显示独立的密钥管理区和状态。「已配置」仅表示有凭据。连通性由后续实际请求确认。方法随各 Epic 实际交付解锁。模型、算力、依赖缺失时显示原因和可选方式，无法运行的选项不可保存为新的默认值。已经保存的选择在依赖消失时保留并标记不可用。
- 任务页在准备运行的步骤显示方法和「更改本任务的方式」。允许在等待、失败／中断或审核拒绝后的合法重试位置更改当前步骤。处理中禁改，待审核时先完成审核决策。更改方法时清掉依赖原方法的确认，历史尝试继续显示当时的方式。跳过或保留画布仍需操作员在对应步骤确认。
- 示例。低清人像可选「本地 AI 超分 → 本地去背景 → Alpha 裁切」。客户要求保留有背景的已排版成品时可选「跳过 → 保留背景 → 保留原画布」。严重损坏的照片可选「OpenAI 合并处理 → 审核 → Alpha 裁切」。

## 3. 运行和记录

### 设置、任务与尝试

| 记录 | 拟定契约 | 保存时机 |
| --- | --- | --- |
| 工作站偏好 | `SettingKey.ImageProcessingDefaults` 保存版本、增强方式、抠图方式、裁切方式和去噪开关。默认边距继续由现有 `TrimSafetyMarginPixels` 保存。新子页读写同一键，原位置只显示入口。 | 每次通过 `ISettingsRepository.UpsertAsync` 一次提交两键，创建任务时使用同一次 `ReadAllAsync` 读取。缺行用兼容默认。损坏或未知版本显示修复提示，任务不得悄悄改用其他方法。 |
| 任务选择 | `ImageProcessingPolicy` 和 `SessionImageProcessingSelection`。方法标识用稳定枚举。组合模式用独立标识表达，分步选择一起保存以便退出合并模式。 | 在两种适用流程的创建事务写入，通过现有会话命令和事务调整。升级前已存在的任务确定性还原成兼容方式，现有边距和去噪值按会话保留。 |
| 执行记录 | `ImageProcessingRunDetails` 记录实际方法、`AdapterId`、模型或算法版本、必要参数、输入 Revision／SHA-256。云端额外记录确认时间、操作者、服务请求 ID 和有返回时的用量。 | 运行前在 Attempt 开启事务锁定计划。结束时补写结果证据和输出 Revision。历史行用原 AdapterId 和现有裁切字段显示。API 密钥放在 Windows 用户凭据存储中。 |

工作流层新增 `IImageProcessingBackend`，提供按方法查询能力和 `ProcessAsync(ImageProcessingRequest, CancellationToken)`。请求包含操作、受管工作副本、预期路径、固定参数及适用的审核授权。后端返回 `OperationResult<AdapterOutput>` 交由工作流统一检查。裁切沿用 `ITrimProcessor` 和 `IManualCropProcessor`。新的任务命令 `SetImageProcessingSelection(StepKind, SessionImageProcessingSelection)` 改单步或在增强步骤改组合模式。服务从同一个会话聚合核对步骤和上游版本，通过现有事务写入。UI 和运行服务都使用同一合法性判断。

在 `SessionService` 将实际后端解析成一次执行计划，由同一计划决定校验、桌面锁、停止／接管、UI 提示和 Attempt。美图走原自动化执行及认领逻辑。对本地处理校验运行库和模型资产，对云端校验凭据和本次上传确认。后两者用普通取消及有界并发，不出现「接管美图」提示。统一从不可变素材建立工作副本，在工作目录内验证可解码结果后再创建候选 Revision。取消、失败、响应迟到或残留文件都不能晋级为结果。

### 三种本地处理

1. **轻度锐化与 AI 超分。** 锐化采用固定版本的低强度算法，尺寸不变。已有 Alpha 的素材分离处理颜色和 Alpha，保持边缘和正确的透明度。AI 超分以原比例 2× 为首版方案，采用固定支持 2× 的模型，在算力检查后运行，限制输入像素和输出尺寸。过大、模型不可用或原图已足够时，提供轻度锐化或跳过。输出仍需对比五官、小字、边缘和色偏。
2. **去背景。** BiRefNet Lite 只生成连续 Alpha 蒙版，回映射到原画布，保留原尺寸和主体颜色通道。已有 Alpha 与模型蒙版合并，避免透明区域被重新填实。全透明、全不透明、尺寸错位或返回坏图时进入失败／人工选择。沿用现有按素材版本和哈希绑定的去背景确认及手动结果进口。
3. **裁切。** Alpha 自动继续用 `DeterministicAlphaTrimProcessor` 的边框、边距和去小噪点规则。把直接手动框选纳入 `ManualCropEligibility` 和状态表的等待态入口，不生成一次伪失败尝试。保留原画布复用 `KeepOriginalExtent`，只继承已审核的上游，原 PNG／TIFF 的最终交付约束继续在各自流程末端执行。

### OpenAI 独立处理和合并处理

- 独立处理复用同一 API 后端。清晰化通过「照片修复」提示词输出 PNG，去背景通过透明输出提供候选图。原本有 Alpha 的素材如果选照片修复，要求输出保留透明度并验证；把不支持的输入送回操作员选择。本地验证输出尺寸、解码与 Alpha，审核人员并排检查主体、文字、轮廓、发丝。 [编辑 API](https://developers.openai.com/api/reference/resources/images/methods/edit) · [结果检查](https://developers.openai.com/api/docs/guides/image-prompting?model=gpt-image-2.6)
- 合并模式下，增强步骤唯一的正常前进操作是「继续到合并处理」，使用现有跳过语义加专用原因 `CombinedWithBackgroundRemoval`。此处只记录计划，不发请求、不产生增强 Revision。进入去背景后，基于同一已审核上游素材，用户确认一次上传，发一次 `/v1/images/edits`，使用 `background=transparent` 和 `output_format=png`。一个 Attempt、一张候选结果、一次联合审核，然后才进入裁切。
- 合并调用失败时，用户可以重新确认并重试，或返回增强切换到本地／美图分步处理。需区分「本次放弃合并结果」和「明确保留已审核原图」。返回上游、换图、改方法或重试均需要新上传确认。确认绑定到服务、操作、输入 Revision 和 SHA-256，款项提示说明网络超时后的服务端请求仍可能已处理。API 过期凭据、限流／余额、断网、超时和无效输出分别显示可执行恢复选项，不自动重发可能已计费的编辑请求。
- 首版推荐测试 `gpt-image-2.6-sunburst`，生产采用试验确认的模型标识，后台配置固定后写入每次 Attempt。云端仅传已确认的工作副本图像和处理指令，日志、诊断和项目数据排除密钥、可反查的响应链接及图像内容。CI 用 HTTP 测试替身覆盖请求和输出形态。实际云调用使用获准上传的样图和预先确认的账户额度。

## 4. 仓库实施拆分与 Jira 草案

目标项目拟沿用 PrintFlow 现有的 `SCRUM`。下表中的 ID 是本计划标识，批准后用标题和 `pf-image-processing-20261118` 去重。拟建 **3 个 Epic、10 个 Task**，复核每项父 Epic 和阻塞关系。任务描述内嵌验收、依赖和必要的实施信息。负责人、Sprint 和估点按 Jira 项目现行规则保留默认。

| Epic | Task | 实施位置与交付 | 依赖 | 验收证明 |
| --- | --- | --- | --- | --- |
| **A · 图像处理配置与执行基础** | **A1 · 持久化配置与尝试证据** | 新建 Domain 配置和值对象，改 `SettingKey.cs`、`ProcessingSession.cs`、`ProcessingAttempt.cs`、`SessionAggregate.cs`、`SessionRows.cs`、`Mappers.cs`、`SqliteSessionRepository.cs` 和下一号 SQLite 迁移。 | — | 旧库升级、默认保存取消、标准导入和 Work 结果导入、活动任务快照、重启及历史读回。 |
| A | **A2 · 设置二级页与任务方法入口** | 在 `SettingsView.xaml` 内挂载新 `ImageProcessingSettingsView.xaml` 及 ViewModel，接 `ServiceRegistration.cs`、现有 Utility 返回路径、`SessionScreenView.xaml` 和中英文资源。 | A1、A3 | 键盘、中文英文、未保存确认、依赖不可用、重开设置、新旧任务分别展示正确配置。 |
| A | **A3 · 执行路由、审核与失败恢复** | 新增 `IImageProcessingBackend` 和执行计划，改 `SessionService.cs`、`WorkflowCommand.cs`、`WorkflowEngine.cs`、`WorkflowSnapshot.cs`、`TransitionTable.cs` 和 `SessionView.cs`。保留现有美图和手工路径。 | A1 | 真实方法决定锁和 UI，修改或重试写独立 Attempt。工作副本被替换、审核冲突、取消或崩溃时无候选晋级。 |
| A | **A4 · 按操作拆分工作站就绪检查** | 检查 `VerifiedEnvironmentGate.cs`、`ProductionWorkstationVerifier.cs`、`ProductionLiveWorkstationVerifier.cs`。按共享条件＋实际桌面应用检查，保留现有显式的全站检查及 Photoshop 的预设、颜色设置、Action、验证和租约要求。 | A3 | 无美图时可完成本地素材流程，在已获验的 Photoshop 条件下可产生产 TIFF。错误 Photoshop 版本、Action 或颜色设置仍阻止对应作业。 |
| **B · 本地增强、抠图与裁切** | **B1 · 本地锐化和 2× 超分** | 在 `src/PrintFlow.Infrastructure/Imaging` 增加实现，用 `Directory.Packages.props` 固定库版本。记录模型标识、输入输出范围和支持的硬件。 | A3 | 锐化保持几何和 Alpha。2× 保持比例，过大、低算力、取消或模型缺失能安全恢复。 |
| B | **B2 · 本地 BiRefNet 去背景** | 在 `Infrastructure/Imaging` 增加 ONNX Runtime 后端，固定具体权重来源、校验值和预处理规格，共用 PNG 校验。 | A3 | 通用及人像样图，保尺寸、保原有 Alpha，拒绝错位或无效蒙版，留存审核。 |
| B | **B3 · 自动、手动和保留画布** | 改 `ManualCropEligibility.cs`、`TransitionTable.cs` 与任务投影，调用现有 Alpha 和手动裁切端口。 | A2、A3 | 等待态直接框选，PNG 安全留边，拒绝空矩形，按原图保留分支重开仍可继续。 |
| B | **B4 · 离线依赖打包与工作站回归** | 核对 `installer/payload-policy.json`，添加明确许可与模型文件条目。复用 `tools/regression/Invoke-PrintFlowStandardRegressionSet.ps1`，补同批脱敏样图对比。 | A2、A4、B1、B2、B3 | 干净 win-x64 环境离线执行，依赖缺失提示正确，现有美图、手工、PNG、Photoshop 和蒙泰链路回归。 |
| **C · OpenAI 可选图像处理** | **C1 · OpenAI 请求、凭据与输出验证** | 在 `Infrastructure/Adapters/OpenAI` 添加 HTTP 后端和 Windows 凭据实现，在设置页显示状态。实现共享的单次上传确认及独立修复、独立去背景、合并请求类型。 | A2、A3 | 测试替身覆盖参数、真实 Alpha、401／429／超时／取消、坏响应、无隐式重发、无凭据泄露和无确认零上传。 |
| C | **C2 · 合并交互、上传确认与样图对照** | 完成复选项、绑定输入的上传确认、一次联合审核、重新运行及返回分步处理。 | B3、C1 | 本地和云端混配，合并只发一次请求，换源后旧确认无效，审核前不可裁切。经授权的实际试验记录耗时、花费和通过率。 |

**执行顺序**　先完成持久化和执行路由（A1 → A3），再接入设置页和操作级就绪检查（A2、A4）。Epic B 的三个处理任务可在各自依赖满足后推进，B4 汇总验证。Epic C 可在 A2、A3 之后接入，用本地／现有生产路线作为回归基准。C 未部署或断网时仍可使用 B。

## 5. 验收与验证方法

每个 Task 先以当前缺失行为写一个可失败的针对性用例，再实现最小变化，运行自己的测试和相关已有回归。以下是需要加入各任务的边界用例。

| 所属 | 必须验证的场景 | 现有用例入口或新增测试 |
| --- | --- | --- |
| A1 | 旧会话没有配置列、有裁切历史或正在等待旧审核。升级读回仍用原方法和边距。保存偏好与同时启动任务只读取完整版本。 | `MigrationTests`、`AutomationLogAndSettingPersistenceTests`、新增 `ImageProcessingPolicyPersistenceTests` |
| A2 | 从任务进设置时保留原任务；改动取消后回到相同步骤。中英两种界面可用键盘完成。 | `SettingsAndLocalisationTests`、`GuidedUtilityReturnTests` |
| A3、A4 | Meitu 未安装、本地无需其租约；图像处理失败切换后旧 Attempt 和拒绝记录仍在；Photoshop 的受验条件被破坏时仍阻止出片。 | `RetryAndReviewTests`、`StartupRecoveryTests`、`StopAndTakeOverTests`、`VerifiedEnvironmentGateTests` |
| B1、B2 | 大图、低显存、透明素材、细发丝、白色边缘、16-bit 输入、EXIF 方向和带 ICC 的素材。超出支持范围显示原因，避免意外丢透明度或颜色配置。 | 新增 `LocalEnhancementTests`、`LocalBackgroundMatteTests` |
| B3 | 透明噪点、近全透明、没有 Alpha、保留原图、手动框选及重复审核。保留原图时仍由后续流程检查输入交付资格。 | `DeterministicTrimTests`、`ManualCropStepTests`、`KeepOriginalExtentPersistenceTests`、`GuidedProcessingCropTests` |
| C1、C2 | 输出 JPG 冒充 PNG、全透明图、无透明像素、断网、额度不足、超时后返回。两线程重复触发、换素材后旧确认和回到上游重跑。 | 新增 `OpenAiImageBackendTests`、`CombinedImageProcessingTests` |

验证命令采用 `dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj --filter FullyQualifiedName~测试类名` 做针对性回归。合并前跑 `dotnet build PrintFlowStudio.sln` 和 `dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj`，另在固定生产工作站跑现有标准回归集并保留输入、输出和结论。维护 UI 可见的真实进度、可取消、失败提示和工作区清理。

**质量对照**　用同一批经授权的样图比较原图、美图、本地结果和可选 OpenAI 结果。首轮每类至少三张，覆盖低清人像、细发丝、白色边缘、带文字 Logo、已透明图片、故意保留背景的成品。记录主体变化、文字误改、Alpha 白边、裁切像素、分辨率、耗时和峰值内存。云端另外记录单张与重试总成本。严格本地模式的任务须通过尺寸和 Alpha 契约，且支持范围内不得有非预期画布改变或背景残留进入最终文件。生成式结果逐张审核，并单独报告合格率。生产交付仍须通过现有 TIFF 结构和尺寸验收。

**上线与恢复**　升级默认沿用原行为，按工作站选择启用本地方案，云端由操作者显式配置。出现效果问题时，可恢复工作站默认并在受影响任务切回原方式后从上游重跑。安装包部署或生产工作站修改沿用项目现有发布流程。

## 6. 审批后的 Jira 操作

本次交付计划供审批。批准后，在目标项目先查重并确认可用的 Epic／Task 和关联类型，再建 Epic 与子 Task，设置上述依赖和共同标签。创建后回读数量、父子关系、标题和验收条件，并返回工单链接。待审批的设计选择为兼容默认值、OpenAI 独立与合并选项，以及本轮范围限定在三个处理步骤。
