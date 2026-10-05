# PrintFlow Prompt、附件与 Work 生图 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. 保留用户已授权的自动执行方式；按下述真实模块／独立审查边界调度，不能机械地每个 Jira Task 新开上下文。

**Goal:** 在独立分支实现三个本地 Prompt 模板、必要附件快照、真实 Work 单图上传／生图／回收、事务性幂等素材导入及完整手动降级。

**Architecture:** Domain 保存请求值与状态规则；Workflow 协调请求、浏览器通道、文件与导入端口；Infrastructure 实现原子文件存储、严格图片检测及 SQLite 关联。独立 MV3 扩展由浏览器启动 .NET stdio 宿主，使用当前用户显式配置的受管请求目录交接；WPF 只消费协调器视图。复用现有 SessionService 导入内部流程，在初始 Session 事务内建立唯一请求关联。

**Tech Stack:** 现有 .NET 10、WPF、CommunityToolkit.Mvvm、Microsoft.Data.Sqlite／Dapper、WIC、xUnit／Shouldly；MV3 原生 JavaScript ES modules。无新增 NuGet／npm 运行时包。

**Spec:** `docs/superpowers/specs/2026-10-05-prompt-work-images-design.md` 修订 0.2；原始 11 项 Task requirements／AC 为 `docs/codex/printflow-ai-design-backlog/JIRA_WRITE_LEDGER.json` 的 `plannedTasks`。用户之后直接授权 Route Rules 规划、自动执行及 Jira 写回，取代规格末尾历史性的“等待执行选择”，但不改变技术 AC。

## Global Constraints

- 使用已有个人 Plus／Pro，不增加订阅、收费 API 或托管；只在当前独立 worktree／分支工作，保留原 master 及其修改。
- 第一版默认验证 Windows Microsoft Edge；改用 Chrome 验收必须有用户明确的新指示。不能用普通 Chat 或 Codex 工具能力代替产品 Work 通道。
- 附件允许 PNG、JPEG、WebP、PDF、TXT、Markdown、CSV、DOCX、XLSX、PPTX；图片单文件上限 **20 MiB**、文档 **40 MiB**、最多 **8** 个、总计 **80 MiB**。每个保留附件均为必要输入。
- 原始附件不变；稳定 attachmentId／上传名／用途／长度／SHA-256／受管快照引用随请求保存。每个原始字节块最多 **256 KiB**。
- 所有必要附件明确 Uploaded 后才可 Submitted；每 attempt 最多一次提交，不自动重发。上传总计 **5 分钟**、生成 **10 分钟**、下载 **2 分钟**。
- 接收真实 PNG／JPEG，非空、完整可解码、最多 **40 MiB**、**64,000,000** 像素。按文件事实报告尺寸、透明通道和哈希。
- 状态：`PromptReady → Queued → Uploading → Submitted → Generating → Receiving → Validating → Importing → Imported`；无附件跳过 Uploading；其他结束状态 `PromptOnly / Cancelled / Interrupted / ImportFailed`。
- `Queued` 至 `Validating` 可以停止；`Importing` 禁止停止；停止和进入导入串行裁决。新 attempt 不复用旧上传回执。重启不自动上传／提交。
- 请求写入当前 Workspace Root 的 `AiDesign/Requests/<requestId>/`；跨进程互斥＋同目录临时文件原子替换；回报匹配 requestId、attemptId、stateVersion、随机令牌。
- requestId 唯一关联与新 ProcessingSession 初始创建必须在同一 SQLite 事务内；导入实际源快照必须匹配候选预期哈希。
- 不新增 WorkflowType，不修改 Meitu／Photoshop 驱动、签署 Baseline 或生产安装；不合并、推送、发布、部署 Linux／Google／MCP 或生成生产 TIFF。
- 不引入 Selenium／Playwright、未公开 ChatGPT 接口、cookie 提取或 .NET HTTP 下载器；既有依赖／安装边界测试保留。
- 测试只用临时目录、合成图片和脱敏网页夹具。账号数据、真实附件、下载图片、请求令牌不进入 Git。

## Review Focus

1. Workspace Root 变更或桥接重连不能让上一根目录的请求在新配置自动启动：T03／T06 测试根身份与连接代次，变更时拒绝活跃切换并要求重新握手。
2. 上传名稳定但编辑后的 Prompt 引用已删除附件：T04／T05 测试稳定 ID 引用集合校验，不能仅以自然语言字符串猜测有效性。
3. 网页点击已发生但确认丢失：T03／T07 测试持久化提交保留位；恢复为 Interrupted，不能重放点击。
4. 文件通过元数据读取却像素体截断：T08 强制完整像素解码测试；现有 WicFileInspector 的可空元数据成功结果不能直接作为 AI 图片通过。
5. SQLite 已建会话而请求 JSON 未写 sessionId 即崩溃：T09／T11 测试从数据库唯一关联修复显示，不能再创建会话或清除关联。

## 执行顺序、门槛与上下文

| Task | Jira | 必须满足的前置 | 自然执行模块／默认路由 |
| --- | --- | --- | --- |
| T01 | SCRUM-11157 | 无 | 现有基线代理；Sol High |
| T02 | SCRUM-11158 | 无 | 真实网页合同；Astra High |
| T03 | SCRUM-11159 | T01 PASS | 本地请求／持久化，Sol High |
| T04 | SCRUM-11160 | T03 | 本地模板，正常分类 Sol Medium；可在紧密的 T03–T06 Sol High 上下文完成 |
| T05 | SCRUM-11161 | T03 | 本地附件／安全文件，Sol High |
| T06 | SCRUM-11162 | T05 | 宿主／协议，Sol High；扩展页面操作留给 T07 Astra |
| T07 | SCRUM-11163 | T02、T04、T06 | 网页上传／提交，Astra High |
| T08 | SCRUM-11164 | T07 | 网页结果识别 Astra High；可分离接收／解码 Sol High |
| T09 | SCRUM-11165 | T08 | 导入事务／恢复，Sol High |
| T10 | SCRUM-11166 | T09 | WPF／导航／交互，Astra High |
| T11 | SCRUM-11167 | T10 | 真实验收 Astra High；独立代码审查 Sol High，必要文件／恢复审计 Astra High |

规划初稿时 T01 被既有停滞阻塞。经四个测试文件的最小夹具修正及独立审查，2026-10-05 12:49 NZDT 的完整普通 Release 回归已实际 exit0：13,919 passed / 0 failed / 1既有交互skip / 13,920 total。现有 `WorkstationEntryPhase!=Runtime` 边界仅排除七项需独立 reviewed run authority 的专用用例；这些当前验收 NOT RUN，未过滤 exit1 历史证据保留。详见 `BASELINE_REPORT.md`。T01 已 Jira Done，本地提交 `70fa317`；T03–T06 的本地实现门槛已满足。

T02 仍被当前工具无法连接 Edge 阻塞，见 `WEB_CAPABILITY_REPORT.md`。T07 及其依赖链不越过 T02 的真实网页门槛。T06 的本地协议通过不替代真实登记/Edge连通/撤销验收。不能自行把 T09/T10 提前完成后关闭 Jira；用户若明确允许调整依赖／先交付本地能力，记录新指示与局部计划变化后再做。

使用最少自然上下文：本地后端 T03–T06 一个连续执行上下文；网页合同／适配 T02/T07/T08 可复用页面上下文；文件接收与 T09 可复用后端上下文；T10 是真实 UI 边界；最终审查必须无完整实现历史的新上下文。规划完成后重分类，不让 Astra 自动延续全部后端实现。路由 profile 固定继承 `route_offset: 0`，真实运行证据写 HANDOFF。

## 文件结构与共享接口

新文件以下面精确落点为准；同一职责的小型 DTO 可以保留在列出的合同文件，避免为每个 record 建一层服务。现有代码无需大规模重构。

| 路径 | 责任／所有者 |
| --- | --- |
| `src/PrintFlow.Domain/AiDesign/AiDesignRequest.cs` | 请求／附件／候选值、枚举、结构化失败；T03 |
| `src/PrintFlow.Domain/AiDesign/AiDesignTransitions.cs` | 状态与提交／停止前置；T03 |
| `src/PrintFlow.Workflow/AiDesign/AiDesignContracts.cs` | draft、view、ports DTO；T03，按下表冻结接口 |
| `src/PrintFlow.Workflow/AiDesign/IAiDesignCoordinator.cs`、`AiDesignCoordinator.cs` | 页面入口和串行协调；T03，T08/T09接通 |
| `src/PrintFlow.Workflow/AiDesign/IAiDesignRequestStore.cs`、`IAiDesignChannel.cs` | CAS store、浏览器能力与提交端口；T03 |
| `src/PrintFlow.Workflow/AiDesign/PromptTemplateService.cs` | 三个本地模板和引用校验；T04 |
| `src/PrintFlow.Workflow/AiDesign/IAiDesignFiles.cs` | 快照、接收、候选解析、快照展示端口；T05/T08 |
| `src/PrintFlow.Infrastructure/AiDesign/FileAiDesignRequestStore.cs`、`AiDesignBrowserLease.cs` | 独占更新、安装级浏览器占用；T03 |
| `src/PrintFlow.Infrastructure/AiDesign/FileAiDesignAttachments.cs` | 显式文件快照、哈希、受管路径；T05 |
| `src/PrintFlow.Infrastructure/AiDesign/AiDesignBridgeConfiguration.cs`、`FileAiDesignChannel.cs`、`NativeMessageCodec.cs` | 本地配置、持久连接／请求交接、帧协议；T06 |
| `src/PrintFlow.NativeHost/PrintFlow.NativeHost.csproj`、`Program.cs`、`NativeHostSession.cs` | 独立 Windows stdio exe，不引用 App；T06 |
| `extensions/printflow-work/manifest.json`、`service-worker.js`、`protocol.js` | MV3 origin／native／下载路由；T06/T07 |
| `extensions/printflow-work/work-page.js`、`work-contract.js` | 由 T02 真实证据导出的 DOM 合同；T07/T08 |
| `src/PrintFlow.Infrastructure/AiDesign/FileAiDesignReceiver.cs`、`WicAiDesignImageValidator.cs` | 身份、复制、完整解码、原子候选发布；T08 |
| `src/PrintFlow.Workflow/AiDesign/IAiDesignImporter.cs` | 候选→会话合同；T09 |
| `src/PrintFlow.Workflow/Services/SessionService.AiDesign.cs` | 幂等入口、既有导入核心复用；T09 |
| `src/PrintFlow.Infrastructure/Sqlite/Migrations/0020_ai_design_import.sql` | 新的唯一关联表；T09（若执行时已占用编号，改用下一个而不改历史脚本） |
| `src/PrintFlow.App/ViewModels/AiDesignViewModel.cs`、`Views/AiDesignView.xaml`、`Views/AiDesignView.xaml.cs` | 独立页面；T10 |
| `src/PrintFlow.App/Navigation/IAiDesignFilePicker.cs`、`AiDesignFilePicker.cs` | 显式多选附件／单选结果；T10 |
| `build/ai-design/Register-NativeHost.ps1`、`Unregister-NativeHost.ps1` | 当前用户试用登记／撤销；T06 |

**共同类型（除既有 `OperationResult<T>`、`Unit`、`Sha256`、`FileFacts`、`SessionId`、`SessionView` 外，均在上列新合同文件定义）：**

- `Guid` 用作 requestId、attemptId、attachmentId、candidateId；不复用生产 `AttemptId`。`AiDesignFence(Guid RequestId, Guid AttemptId, long StateVersion, string Token)` 为消息 CAS 身份。
- `AiDesignFileRef(Guid RequestId, string RelativePath)` 仅允许本请求根内的规范化相对路径；原始用户路径只出现在显式选择的入口，不保存到浏览器消息。
- `AiDesignAttachment`：Id、Order、OriginalName、UploadName、Purpose、MediaType、ByteLength、Sha256、Snapshot；`AiDesignUploadReceipt`：AttachmentId、AttemptId、ConversationId、Status、EvidenceKey。附件清单和回执分开，重试清空回执。
- `AiDesignPrompt(string TemplateId, int TemplateVersion, IReadOnlyDictionary<string,string> Fields, string Text, IReadOnlyList<Guid> AttachmentReferences, string HandoffInstructions)`；Text 保留用户编辑，附件引用使用结构化 ID 集合。组合出的上传名／用途段来自清单，不能靠解析 Windows 路径推定附件。
- `AiDesignDraft(AiDesignPrompt Prompt, IReadOnlyList<AiDesignAttachment> Attachments)`；`SelectedAiDesignFile(string AbsolutePath, string Purpose)` 只在 picker→协调器→快照端口间流转。
- `AiDesignCandidate(AiDesignFileRef File, FileFacts Facts)`；`AiDesignDownload`：RequestId、AttemptId、TabId、ConversationId、ResultId、DownloadId、AbsolutePath、IsComplete；只有宿主从已登记 download ID 取得位置，网页不得自报任意路径。
- `AiDesignFailure`：稳定 Code、Stage、可本地化 DetailArguments、LastSuccessfulStage、RemoteStopConfirmed。不要把账号／令牌／任意原文日志作为错误参数。
- `AiDesignRequest` 包含规格 §6 全部持久化字段，额外 `SubmissionReservedAtUtc` 防止点击不确定后重试。`AiDesignView` 投影 Request、CanEditAttachments、CanStartAutomatically、CanStop、BridgeUnavailableReason、ManualHandoffText；不向 UI 暴露令牌。
- `AiDesignBridgeReadiness`：Available、ProtocolVersion、HostVersion、ExtensionOrigin、WorkspaceIdentity、Reason；Available 需要当前存活连接与已验证网页适配，不仅是注册表存在。
- `AiDesignEvent` 是有限事件联合：DraftSaved、Queued、UploadStarted、UploadConfirmed、SubmitReserved、SubmitConfirmed、GenerationStarted、DownloadRegistered、DownloadCompleted、ValidationStarted、CandidatePublished、ImportStarted、ImportCompleted、Failed、StopAccepted、Interrupted。事件携带对应数据；状态规则拒绝跨阶段／缺少证明的事件。

**接口签名（所有异步入口都接收 `CancellationToken ct`）：**

```csharp
// IAiDesignRequestStore；ChangeAsync 在跨进程锁内检查 fence、转移并原子替换。
Task<OperationResult<AiDesignRequest>> CreateAsync(Guid requestId, AiDesignDraft draft, CancellationToken ct);
Task<OperationResult<AiDesignRequest>> ReadAsync(Guid requestId, CancellationToken ct);
Task<OperationResult<AiDesignRequest>> ChangeAsync(AiDesignFence expected, AiDesignEvent change, CancellationToken ct);
Task<OperationResult<IReadOnlyList<AiDesignRequest>>> ListAsync(CancellationToken ct);
// 同一跨进程锁内，持久化提交保留位→有界click→确认；不确定则Interrupted。
Task<OperationResult<AiDesignRequest>> SubmitOnceAsync(AiDesignFence expected, Func<CancellationToken, Task<OperationResult<Unit>>> click, CancellationToken ct);

// IAiDesignCoordinator；应用实例拥有运行循环，VM 不持有浏览器或文件对象。
OperationResult<AiDesignPrompt> ComposePrompt(string templateId, IReadOnlyDictionary<string,string> fields, IReadOnlyList<AiDesignAttachment> attachments, string language);
Task<OperationResult<AiDesignView>> CreateDraftAsync(AiDesignPrompt prompt, CancellationToken ct);
Task<OperationResult<AiDesignView>> SaveDraftAsync(Guid requestId, AiDesignDraft draft, CancellationToken ct);
Task<OperationResult<AiDesignView>> AddAttachmentsAsync(Guid requestId, IReadOnlyList<SelectedAiDesignFile> files, CancellationToken ct);
Task<OperationResult<AiDesignView>> StartAsync(Guid requestId, CancellationToken ct);
Task<OperationResult<AiDesignView>> StopAsync(Guid requestId, CancellationToken ct);
Task<OperationResult<AiDesignView>> RetryAsync(Guid requestId, CancellationToken ct);
Task<OperationResult<AiDesignView>> ImportManualAsync(Guid requestId, string selectedAbsolutePath, CancellationToken ct);
Task<OperationResult<AiDesignView>> ResumeCandidateAsync(Guid requestId, CancellationToken ct);
Task<OperationResult<AiDesignView>> ReadAsync(Guid requestId, CancellationToken ct);
Task<OperationResult<Unit>> RecoverAsync(CancellationToken ct);
Task<OperationResult<Unit>> RevealAttachmentAsync(Guid requestId, Guid attachmentId, CancellationToken ct);

// PromptTemplateService：纯逻辑；生成、编辑提交、复制都用相同引用验证。
OperationResult<AiDesignPrompt> Compose(string templateId, IReadOnlyDictionary<string,string> fields, IReadOnlyList<AiDesignAttachment> attachments, string language);
OperationResult<Unit> Validate(AiDesignPrompt prompt, IReadOnlyList<AiDesignAttachment> attachments);
string RenderHandoff(AiDesignPrompt prompt, IReadOnlyList<AiDesignAttachment> attachments);

// IAiDesignChannel；实现不阻塞 UI，事件回写相同 store，ReadAsync 读取进度。
Task<OperationResult<AiDesignBridgeReadiness>> CheckAsync(CancellationToken ct);
Task<OperationResult<Unit>> EnqueueAsync(AiDesignRequest request, CancellationToken ct);
Task<OperationResult<Unit>> StopAsync(AiDesignFence fence, CancellationToken ct);

// IAiDesignAttachments / IAiDesignReceiver / IAiDesignCandidateResolver（IAiDesignFiles.cs）
Task<OperationResult<IReadOnlyList<AiDesignAttachment>>> SnapshotAsync(Guid requestId, IReadOnlyList<SelectedAiDesignFile> selected, IReadOnlyList<AiDesignAttachment> existing, CancellationToken ct);
Task<OperationResult<Unit>> VerifyAsync(Guid requestId, IReadOnlyList<AiDesignAttachment> attachments, CancellationToken ct);
Task<OperationResult<AiDesignCandidate>> ReceiveAsync(AiDesignFence fence, AiDesignDownload download, CancellationToken ct);
Task<OperationResult<AiDesignCandidate>> ReceiveManualAsync(AiDesignFence fence, string selectedAbsolutePath, CancellationToken ct);
Task<OperationResult<string>> ResolveVerifiedAsync(Guid requestId, AiDesignCandidate candidate, CancellationToken ct);
// IAiDesignAttachmentShell：仅选中快照，不执行附件；协调器核验后调用。
Task<OperationResult<Unit>> RevealAsync(Guid requestId, AiDesignAttachment attachment, CancellationToken ct);

// IAiDesignImporter；由 SessionService 实现，只接受已发布的受管候选。
Task<OperationResult<SessionView>> ImportAsync(Guid requestId, AiDesignCandidate candidate, string? operatorName, CancellationToken ct);
Task<OperationResult<SessionView?>> FindAsync(Guid requestId, CancellationToken ct);
```

附件的“删除／说明调整”通过 `SaveDraftAsync` 提交完整草稿；受管引用必须属于当前请求且元数据与持久记录匹配，不能借 DTO 替换任意文件。`SnapshotAsync` 在持锁的草稿变更内调用，失败不提交半份清单。上传名按首次选择的递增序号分配，不因删除而重编号，不重复使用序号。初始 PromptReady 使用 `AttemptId=Guid.Empty` 和本地草稿令牌，Start／Retry／新的手动尝试才分配非空 attemptId；草稿令牌不能获准调用浏览器。

`RetryAsync` 仅响应明确点击，核验快照并创建新 attempt/token；`ImportManualAsync` 在取消／中断后也建立新 attempt，再走相同接收、验证、导入路径。已有数据库关联时优先返回原会话；ImportFailed 不重新生图。`RecoverAsync` 先从唯一导入关联校准 Imported／ImportFailed，再把仍未完成自动化请求标记 Interrupted；不把活跃另一个应用实例的请求误判为死进程。

## Task 1 — SCRUM-11157：建立准确基线

**范围补充（2026-10-05）**：现有 `SCRUM-11154_WORKSTATION_ENTRY_RUNBOOK.md` 将七项 `WorkstationEntryPhase=Runtime` 测试放在独立的 reviewed candidate/run authority 下。普通完整 Release 基线使用现有 trait 边界 `WorkstationEntryPhase!=Runtime`；不删除/跳过测试代码、不伪造运行身份、不以历史 runtime PASS 证明当前字节。未过滤运行的失败 TRX 保留，七项专用 runtime 验收单独 NOT RUN。新鲜独立范围审查确认这符合原 T01 的“合适的完整回归”，但仍须普通完整运行实际 exit 0 后才满足 T03 门槛。

**Files:** 默认只读已有测试与 solution；已定位的基线测试场景错误允许最小修正，生产锁与原断言保持。证据写 `docs/codex/printflow-ai-design-backlog/BASELINE_REPORT.md`（如基线代理已有文件则复用）。原始 TRX／日志在 Git 忽略的 `artifacts/test-results/prompt-work-images-baseline/`，文档记录准确位置。**Interfaces:** 无；向 T03 提供 PASS 或明确 BLOCKED 的基线结果。

- [ ] 记录 worktree、branch、HEAD、SDK、准确命令与既有修改；不重新创建分支。
- [ ] `dotnet restore PrintFlowStudio.sln --locked-mode`，再 `dotnet build PrintFlowStudio.sln -c Release --no-restore`；记录退出码、警告，不并发启动另一构建。
- [ ] 在已构建 Release 上运行两个跨进程 lease 用例，再运行完整回归：`dotnet test PrintFlowStudio.sln -c Release --no-build --no-restore --logger trx --blame-hang --blame-hang-timeout 120s --blame-hang-dump-type none`；由当前基线代理沿用其准确 filter、证据目录和有界外层超时。
- [ ] 若停滞，保存 blame／TRX／末活动用例及子进程证据，定位到最小复现；两次不同尝试仍未解释则记录阻塞，不扩修旧 Epic。
- [ ] 逐项核对 AC1–4；完整回归未解决不能 Done。Jira 写回由主协调者完成，不重复执行基线代理已完成的检查。

**有界修正规则（本轮已定位）：** `Interrupted_recovery_offers_restart_manual_import_and_abandonment` 的旧 HangUntilCancelled 调用持续持有静态 SessionCompletionGate；恢复后测试仍向同一活进程发命令。复用仓库既有 `SimulatedProcessDeath.BeginRecoveredProcess(report)` 并创建恢复后的服务，正确模拟真正进程死亡。保留所有原断言和生产锁规则；已有45秒单用例挂起是 RED 证据，随后验证该用例、同类恢复测试、完整回归。此修正只恢复 T01 验证能力，不扩展旧 Epic 产品范围。

后续有界全量暴露的既有 UI 测试合同失配由 Astra High 处理：按首页稳定 AutomationId 验证当前清理警告；先展开技术详情再严格验证原尺寸／PPI事实；按准确审批控件身份保持 Single 查询；复用 OperatorCultureScope 恢复原始语言选择。只改三个测试文件并增加一条真实语言消费者回归，无产品 UI 改动。目标测试 49 PASS／0 FAIL／1 既有交互跳过；独立审查与完整回归仍是 T01 完成门槛。最后一次完整运行保留120秒不活动诊断并采用明确20分钟总wall上限，防止此前8分钟界限截断持续进展的全量测试。

## Task 2 — SCRUM-11158：真实网页合同

**Files:** 更新现有 `docs/codex/printflow-ai-design-backlog/WEB_CAPABILITY_REPORT.md`；新增 `extensions/printflow-work/fixtures/work-contract.json` 及最小脱敏 DOM 夹具。**Interfaces:** `work-contract.json` 记录验证日期、浏览器配置、Work 入口、登录、upload/input/submit/progress/error/result/download 的可观察规则及证据路径；没有验证过的 selector 不写成支持合同。

- [ ] 解决真实 Edge 控制可达性；继续当前待答澄清，不重复询问。用户允许 Chrome 时明确更新验收范围记录，不能自行替代。
- [ ] 用合成输入分别观察无附件和图＋短 TXT 需求文件链路。记录真实上传控件接受 File 内容、每个上传名接收完成、Work 身份、生成结果身份及原始下载操作。
- [ ] 验证 download ID 可与该结果唯一绑定；参考图、头像、旧图、缩略图和别的标签页均排除。此时不调用未公开接口，也不据 DOM 仿真宣称网页可行。
- [ ] 留存脱敏合同／夹具，纯文本、带附件、下载分别标 PASS／FAIL／BLOCKED／NOT RUN；未知交互给出具体失败和手动路线。
- [ ] 核对 AC1–4。若核心操作不可验证，保留自动通道不可用与 T07 依赖阻塞；后端准备不意味着该 Task 完成。

## Task 3 — SCRUM-11159：请求状态、存储、停止与恢复

**Files:** 上表 T03 全部文件；`tests/PrintFlow.Tests/Unit/AiDesign/AiDesignTransitionsTests.cs`、`Integration/AiDesign/AiDesignRequestStoreTests.cs`、`AiDesignCoordinatorTests.cs`。真实双进程探针落在 `tests/PrintFlow.AiDesignProbe/Program.cs` 与同名 csproj，仅做 store CAS／导入崩溃边界，T09 复用。**Interfaces:** 产出上述请求合同、store、coordinator、channel；首次先使用显式 unavailable channel，绝不回报假成功。

- [ ] 写测试：`Requires_every_upload_before_submit`、`Rejects_stale_fence`、`Stop_and_import_have_one_winner`、`Restart_preserves_inputs_without_requeue`、`Second_automatic_request_is_refused`；assert 全附件 Uploaded、stateVersion 单调、新 attempt 回执为空、取消后无导入、Importing 时 stop 被拒。使用 FakeTimeProvider 测 5/10/2 分钟边界。
- [ ] 新测试 filter `FullyQualifiedName~AiDesignTransitionsTests|FullyQualifiedName~AiDesignRequestStoreTests|FullyQualifiedName~AiDesignCoordinatorTests` 先失败；再实现有限转移及明确错误，不增加通用状态机框架。
- [ ] 文件存储使用受管根内 `.lock` 的 `FileShare.None` 跨进程独占；锁内读－校验－变更－递增版本－flush 临时文件－原子替换。临时文件不是恢复权威；损坏权威记录报错，不擅自重置。浏览器占用使用安装的当前用户桥接配置目录独占锁，不能仅按 requestId／Workspace 区分而允许第二个根并发运行。
- [ ] 实现持久提交保留位、owner process／连接代次、令牌失效、重启校准和阶段 deadline。真实双进程同时 CAS 断言只有一个成功且无丢失更新；在替换前后终止探针，完整旧／新状态二者之一可读。
- [ ] 跑该 filter 与既有 architecture 基础边界；测试 PASS 后核对 AC1–5、记录证据并只提交本 Task 文件。没有桥接也必须能创建／保存 Prompt；不能让依赖后续模块的未实现入口假报成功。

## Task 4 — SCRUM-11160：本地 Prompt 与引用

**Files:** `PromptTemplateService.cs`；`tests/PrintFlow.Tests/Unit/AiDesign/PromptTemplateTests.cs`；模板正文放同一模块内固定表，不引入可执行模板引擎。**Interfaces:** 消费 AiDesignPrompt／Attachment；产出 Compose／Validate／RenderHandoff。

- [ ] 写 `Three_templates_require_subject_or_text`、`Preserves_literal_operator_text`、`Deleted_reference_blocks_submit`、`Edited_prompt_is_frozen`：主体／必须保留文字为空失败，汉字／引号／换行逐字保留，缺失 attachmentId 拒绝，运行快照不随后续 draft 编辑变化。
- [ ] `dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~PromptTemplateTests` 先失败，再实现三模板字段：单主体（主体/风格/颜色/背景/构图/比例）；徽章（主体/形状/风格/色数/文字/比例）；文字印花（文字/字体风格/装饰/颜色/比例）。
- [ ] 默认要求完整主体、清晰边缘、构图和避免水印；交接指令另存并要求一张可下载图，requestId 不进入绘图内容。引用清单使用稳定 ID；提交时从清单生成上传名＋用途段。编辑正文不隐式翻译、不改写用户文字。
- [ ] 测无附件和两个原文件同名的复制文本；清单包含唯一稳定上传名及用途，不包含本机路径，也不声称 Uploaded；缺失引用必须由用户删引用或恢复附件后才通过。
- [ ] 同一 filter PASS，核对 AC1–5，记录证据并提交。

## Task 5 — SCRUM-11161：必要附件快照

**Files:** `IAiDesignFiles.cs`、`FileAiDesignAttachments.cs`；`tests/PrintFlow.Tests/Integration/AiDesign/AiDesignAttachmentTests.cs`。复用 `Infrastructure/Workspace/PathGuard.cs`／`ReparsePointGuard.cs` 的保护原则，不放宽旧 IWorkspace 的会话专用引用。

- [ ] 写表驱动测试覆盖允许类型、图片20MiB／文档40MiB／8个／80MiB的恰好上限与超过1字节/1个、空/不可读文件、同名文件、跨请求引用、越界及 reparse 路径；使用临时合成附件。
- [ ] 跑 `FullyQualifiedName~AiDesignAttachmentTests` 验证失败，再实现只对显式文件列表复制、流式限长／哈希、稳定名和逐文件 CreateNew。只在完整成功后发布清单；失败的 staging 不成为可上传引用，原件不改动。
- [ ] 受管路径归属、普通文件与 reparse 检查在实际打开处重核；打开时禁止同时写入，完整读取后复核长度／哈希。Office 仅复制，不解析宏。宿主上传前同样验证。
- [ ] `Original_unchanged_and_snapshot_tamper_rejected`、`Names_do_not_rebind_after_remove`、`Running_request_rejects_attachment_edit` 通过；失败时保留已有已提交清单。
- [ ] 同一 filter PASS，核对 AC1–5、临时文件归属与原件哈希，记录并提交。

## Task 6 — SCRUM-11162：宿主、协议、试用登记

**Files:** 上表 T06 文件、新 host solution entry／lock file；`tests/PrintFlow.Tests/Integration/AiDesign/NativeMessageCodecTests.cs`、`NativeHostBoundaryTests.cs`；`extensions/printflow-work/tests/protocol.test.mjs`。**Interfaces:** Native JSON envelope `{protocolVersion:1, messageId, requestId, attemptId, stateVersion, token, kind, payload}`；kind 限定 Hello、Ready、Request、AttachmentChunk、AttachmentComplete、UploadReceipt、SubmitPermit、SubmitReceipt、Progress、DownloadRegistered、DownloadComplete、Stop、Failure、Ack，不提供命令执行或任意路径读写 kind。

- [ ] 写帧／边界测试：4字节长度与 UTF-8 JSON、读到半帧、负/超限长度、stdout 仅协议、origin/version/fence 错误、256KiB恰好通过与+1拒绝、乱序／缺块／重复／完整hash不符。envelope JSON 总长限制1MiB；base64后的块仍在该限内。
- [ ] 运行 `FullyQualifiedName~NativeMessageCodecTests|FullyQualifiedName~NativeHostBoundaryTests` 先失败，再实现 binary stdio codec；日志只写 stderr。host 首参数 origin 必须精确匹配已配置 ID。宿主只读当前清单允许的附件，不接受网页发送绝对路径作附件来源。
- [ ] 建立可启动的产品交接：用户首次启用扩展时 `connectNative`，宿主读取固定当前用户本地配置并保持端口；PrintFlow EnqueueAsync 写受管请求，宿主仅查询已知受管队列，App轮询自己的请求视图。连接不存在时自动按钮禁用并指出需连接扩展。无 HTTP、服务、开机项或任务计划；断线使现有 attempt Interrupted，重连不重放旧 Request。连接握手固定 WorkspaceIdentity；换根先终止旧占用并重新握手。
- [ ] register 脚本接受准确扩展ID、host exe、Workspace Root、下载根，校验绝对路径并写固定的 `%LOCALAPPDATA%/PrintFlowStudio/AiDesignBridge` 配置、host manifest和 `HKCU/Software/Microsoft/Edge/NativeMessagingHosts/com.printflow.ai_design`。撤销仅清理匹配该安装记录的键／manifest，不删请求、附件或下载。注册发生于显式本地试用，不改 MSI。
- [ ] 每 attachment 传输串行，帧含 attachmentId、chunkIndex、chunkCount、rawLength、totalLength、sha256；扩展完整重组核验才提供网页 File。逐帧检查取消／fence，但分片不增 stateVersion；上传回执改变状态后 ack 返回新 fence。测试 runner 先验证已有 `node`；可用则 `node --test extensions/printflow-work/tests/protocol.test.mjs`，不可用时报告该验证未运行，不擅自安装。
- [ ] 完成本地干净用户配置登记→Hello→撤销 smoke（依赖真实可用 Edge），再运行 ScopeGuardTests／InstallerBoundaryTests／BannedApiEnforcementTests。T06 本地协议可先完成，实际浏览器配置 smoke 缺失必须留 AC4 未完成，不以模拟替代；核对 AC1–5、记录并提交。

## Task 7 — SCRUM-11163：Work 上传与单次提交

**Files:** `work-page.js`、`work-contract.js`、`service-worker.js`；`extensions/printflow-work/tests/work-page.test.mjs` 与 T02 夹具；协调器仅作必要接线。**Interfaces:** adapter 导出 `inspectPage(document)`、`uploadAttachment(file, identity)`、`submitOnce(prompt, identity)`、`tryStop(identity)`；返回结构化观察／失败，identity含request／attempt／tab／conversation。必须基于 T02 实际观察实现具体选择器与完成条件。

- [ ] 在网页 fixture 测 `not Work`、未登录、未知控件、部分上传、旧会话回执、格式拒绝，assert submitCalls=0；全部匹配附件完成时 submitCalls=1，重复请求仍=1。
- [ ] 创建本请求专用 tab，登记 tabId／conversationId；通过验证的标准上传控件设置真实 File 字节。名字相同不足以证明本次接收，回执同时绑定当前 attempt／conversation／attachmentId。
- [ ] 提交前持久化 SubmitReserved。最后 fence 检查与实际点击／确认处使用同一受限提交临界区；Stop 必须按同一顺序裁决，不出现接受 Stop 后再授予提交许可。临界区只覆盖本地最后校验及有界页面点击往返，不覆盖生图等待；不确定点击、port断开或宿主崩溃→Interrupted，禁止重发。
- [ ] 上传总计5分钟超时、停止和失败立即停止后续帧／提交；网页远端 stop 仅在明确确认后显示成功，否则说明可能仍继续。保留已有上传事实，不声称附件撤回。
- [ ] 跑 `node --test extensions/printflow-work/tests/work-page.test.mjs` 及相关协调器测试；用真实 Edge 的混合附件／同名附件／上传失败／停止复核。AC1–5 的真实网页证据满足才通过并提交；缺失则继续 BLOCKED。

## Task 8 — SCRUM-11164：结果识别、下载和严格图片接收

**Files:** 上表 T08 文件；`extensions/printflow-work/tests/result-download.test.mjs`；`tests/PrintFlow.Tests/Integration/AiDesign/AiDesignReceiverTests.cs`、`AiDesignImageValidationTests.cs`。**Interfaces:** ReceiveAsync／ReceiveManualAsync 返回同一个 AiDesignCandidate；ResolveVerifiedAsync 只解析完整已发布候选。

- [ ] 写结果 fixture 测试：参考图、旧图、头像、缩略图、别的tab／attempt全部拒绝；只有已绑定生成完成结果可启动下载；下载超时2分钟和生成超时10分钟保留手动输入，不选择最新文件。
- [ ] 下载操作使用 T02 验证过的网页按钮或已暴露链接。持久登记唯一 browser downloadId，按该ID监听 complete 并取得浏览器返回的位置；同URL／同文件名不能代替结果绑定，不使用通配目录扫描。
- [ ] 写真实文件测试并先观察失败：PNG/JPEG成功；假扩展名由真实类型决定扩展名；空文件／截断像素／不支持类型／40MiB+1／64,000,001像素拒绝。超限尺寸从头部先拒绝，完整解码使用有界行缓冲强制读取全部像素，不能仅依赖 WicFileInspector 的元数据。保留既有通用 inspector 对PSD/PDF等行为。
- [ ] 验证已登记download身份、配置根和普通文件／reparse归属；以禁止写共享的文件句柄完整复制到请求staging，限长／检测／hash后 no-overwrite 原子发布为 `.png`/`.jpg`。取消、hash变化、publish前崩溃不返回候选；停止后的迟到完整下载也不自动采用。手动选择跳过browser下载身份，但仍走相同文件校验与候选发布。
- [ ] 跑 `FullyQualifiedName~AiDesignReceiverTests|FullyQualifiedName~AiDesignImageValidationTests` 和 result-download JS test；真实 Edge 回收证明 request→attempt→conversation→result→downloadId→hash。核对 AC1–5，记录并提交。

## Task 9 — SCRUM-11165：同事务唯一 Session 导入

**Files:** `IAiDesignImporter.cs`、`SessionService.AiDesign.cs`；修改 `SessionService.cs` 的 ImportAsync 私有核心抽取、`SessionMutation.cs`、`Ports/ISessionRepository.cs`、`Infrastructure/Sqlite/SqliteSessionRepository.cs`；新 `0020_ai_design_import.sql`；`tests/PrintFlow.Tests/Integration/AiDesign/AiDesignImportTests.cs`；复用 T03 probe。

**Interfaces:** `SessionMutation.NewAiDesignImport` 可空，类型 `AiDesignImportLink(Guid RequestId, SessionId SessionId, Sha256 ExpectedHash)`；`ISessionRepository.FindAiDesignImportAsync(Guid requestId, CancellationToken ct)` 返回 `Task<OperationResult<AiDesignImportLink?>>`。SessionService 实现 IAiDesignImporter，内部核心 `ImportCoreAsync(WorkflowType type, string sourceAbsolutePath, string? outputName, string? operatorName, AiDesignImportLink? link, CancellationToken ct)`，已有 ImportAsync 传null；AI先解析受管候选，固定 PrepareAsset，并在源快照建立后比对 ExpectedHash。

- [ ] 写 `Concurrent_requests_create_one_session`、`Repeat_has_no_new_revision_or_copy`、`Hash_change_never_imports`、`Crash_before_and_after_opening_transaction`、`Failure_returns_existing_session`，明确断言关联数=1、SessionId相同、Revision不增长、原手动入口仍无需requestId。
- [ ] 创建表 `AiDesignImport(RequestId TEXT PRIMARY KEY, SessionId TEXT NOT NULL UNIQUE REFERENCES ProcessingSession(Id), ExpectedHash TEXT NOT NULL)`。opening SessionMutation 与 link 同一个 CommitAsync 事务；unique冲突回滚整个opening，再按requestId读取胜出会话。禁止 `INSERT OR REPLACE`、事后receipt和删除已有失败关联。维护类 mutation不得夹带link。
- [ ] 抽取最小共享导入核心，保留现有 cancellation containment、源快照、检测、root Revision 与 closing事务；AI新会话使用传入link.SessionId，手动入口沿用原ID生成，禁止关联与opening使用不同ID。实际快照hash不匹配走既有 FailImportAsync。只有首次新建者复制导入；读到已有running/failed/interrupted关联返回同一Session实际状态，不从第二调用继续复制。
- [ ] 用临时SQLite和探针在opening前／commit后／closing后杀进程，重启通过关联找回同一Session。已有失败／中断进入 `ISessionService.ResolveRecoveryAsync` 原恢复路径；AI请求在恢复后重读会话，不创建第二会话。持有候选／Session身份以供诊断。
- [ ] 跑 `FullyQualifiedName~AiDesignImportTests|FullyQualifiedName~SessionServiceTests|FullyQualifiedName~ImportCancellationShellBoundaryTests|FullyQualifiedName~ManualResultImportTests|FullyQualifiedName~MigrationTests|FullyQualifiedName~DbInvariantTests`；核对AC1–5并提交。Imported仅当真实Import步骤已成功，不自动审阅或生产批准。

## Task 10 — SCRUM-11166：WPF 页面与完整降级

**Files:** 上表 T10 文件；修改 `HomeViewModel.cs`、`Views/HomeView.xaml`、`Navigation/INavigationService.cs`、`NavigationService.cs`、`App.xaml`、`Composition/ServiceRegistration.cs`／`ApplicationStartup.cs`、`Resources/Strings.resx`／`Strings.zh-CN.resx`／`Strings.cs`；`WorkflowSelectionViewModel.cs`／对应XAML只增加预选视觉状态；`tests/PrintFlow.Tests/Integration/Ui/AiDesignViewModelTests.cs`。

**Interfaces:** `INavigationService.GoToAiDesignAsync(Guid? requestId, CancellationToken ct)`；既有 `GoToWorkflowSelection(SessionView session)` 保持，新增重载 `(SessionView session, WorkflowType preferredWorkflow)`；`WorkflowSelectionViewModel.Open(SessionView session, WorkflowType? preferredWorkflow)` 保留原Open调用路径。预选只显示 PrepareAsset 选择，不代替操作者执行工作流。`IAiDesignFilePicker.PickAttachments(string title, string filter)` 返回 `IReadOnlyList<string>`，`PickResult(string title, string filter)` 返回 `string?`；VM把选中的路径交协调器，不自行读写。

- [ ] 写VM测试：无桥接仍生成复制；混合／同名附件显示原名、上传名、类型、大小、用途、逐件状态；运行时附件编辑／二次提交禁用，Importing停止禁用，旧attempt回报无导航。
- [ ] 实现首页入口与依次排布的字段、Prompt、附件清单、阶段／结果；每个必填项有明确提示，复制的是完整交接。快照打开操作按requestId+attachmentId调用协调器，Infrastructure shell只选中已核验文件；不自动打开执行Office附件。
- [ ] 页面显示当前实际尝试快照与正在编辑表单的区别；能力检查解释扩展连接／版本／origin／root／网页合同哪个缺失。提供停止、明确重试、选择图片、恢复候选、返回首页及已保留请求入口；窗口启动调用 RecoverAsync，不启动新生成。
- [ ] 成功只在 Imported且当前显示request／attempt匹配时导航Workflow Selection并预选PrepareAsset。失败／中断返回同一会话诊断；新手动图片同样走校验及唯一导入。导航离开不暗中重新提交，事件订阅解除并保留应用拥有的运行协调。
- [ ] 跑 `FullyQualifiedName~AiDesignViewModelTests` 及既有 Home／Navigation／Localization 相关测试；在可运行WPF配置实际检查 zh/en、长文件名、多行用途、缩放、忙碌／取消状态，记录截图位置和未验证限制。核对AC1–5后提交。

## Task 11 — SCRUM-11167：真实验收、审查与试用文档

**Files:** `docs/codex/printflow-ai-design-backlog/ACCEPTANCE_REPORT.md`、`docs/ai-design-local-trial.md`；已有 HANDOFF 和网页报告只更新事实。无需新增重复计划系统。**Interfaces:** 每项Task AC编号→代码／测试名→TRX／脱敏网页证据→PASS/FAIL/BLOCKED/NOT RUN。

- [ ] 顺序执行锁定restore、Release build、新增针对性用例及完整Release回归；复用已通过且未受后续改动影响的证据，出现新失败再扩大复测。所有命令、退出码、警告与原始证据位置可审阅。
- [ ] 在明确启用的真实试用浏览器上跑无附件、合成参考图＋短需求文件两条完整链路；各记录 request／attempt、upload回执（适用时）、生成result身份、downloadId、候选hash、唯一Session，不记录token或账号。
- [ ] 真实检查上传失败、上传中停止、断线及重启；保留Prompt／必要附件、不重提交，ImportFailed／重复点击返回同一会话。自动纯文本、自动带附件、Prompt＋手动回收三个能力分别判定。
- [ ] 文档包含本地构建、Edge sideload、个人登录、当前用户登记／撤销、路径配置、手动上传真实快照、排错、仅清理本功能拥有且不再使用的试用文件；干净配置走一遍登记和撤销。无生产安装、付费依赖、发布或部署步骤。
- [ ] 派发真正独立的 Sol High 审查：只交原AC、spec、计划、baseline→当前diff及直接证据，不交实现者结论；文件/恢复风险需要时单独 Astra High审计，UI视觉审核Astra High。修复后按影响范围重跑并复审。
- [ ] 逐项写回11 Tasks和Epic：未满足真实链路或基线的任务保留未完成；只能在全部原AC和审查通过时Done。最终报告Git/commit、实际路由、独立上下文机制、已实现／阻塞能力、未推送／未部署事实。

## 验证纪律与自审结果

- 以上T03–T10每项先写最小行为／故障测试，先见具体失败，再实现，再同命令通过；不增加只镜像实现的测试。提交只包含当前Task明确文件，先review diff，避免带入其他代理工作。
- C# 通用命令前缀：`dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj -c Release --no-restore --filter "<上述filter>" --logger trx`。已有Release跨进程依赖必须先build；父协调者串行安排构建，不能争用基线测试。
- 无需付费API、Linux/MCP、断网、生产安装／签署Baseline、ZIP打包验证；这些不属于本Epic。现有安装／包边界检查仍必须通过。
- Spec §1–3/10由全局约束和T01/T02/T06/T11覆盖；§4/4.1/4.3由T04/T05/T10；§4.2/7由T02/T06/T07/T08；§5/6/9由共享接口及T03；§8由T08/T09；§11由各Task tests和T11实证覆盖。原11 Task AC全部保留，无降级替代自动化验收。
- 类型自审：所有请求／附件／candidate Guid、FileFacts/Sha256及SessionId统一；浏览器fence不混用生产AttemptId；Task9方法是IAiDesignImporter.ImportAsync，旧ISessionService.ImportAsync不改参数；UI不拿token或直接解析受管路径。
- 架构自审已补足：浏览器主动启动native host；断线不重放；稳定附件ID与编辑文本并存；提交不确定保留位；完整像素解码；requestJSON／SQLite间崩溃由数据库唯一关联裁决。计划本身通过自审不代表实现／测试／真实网页通过。

协议事实来源（2026-10-05读取）：[Microsoft Edge Native Messaging](https://learn.microsoft.com/en-us/microsoft-edge/extensions/developer-guide/native-messaging)、[Chromium Native Messaging](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging)、[Downloads API](https://developer.chrome.com/docs/extensions/reference/api/downloads)。这些只证明扩展协议和下载API合同；Work网页能力仍由T02真实证据决定。
