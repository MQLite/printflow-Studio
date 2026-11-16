# Memorial Preservation and Product Conversion Implementation Plan — B

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在A工作台上实现原照片/装饰沿用、精确文字层、真实产品版式、独立成品版本与结果核对，并输出受管设计素材PNG。

**Architecture:** 纯Domain几何决定允许的版式，Workflow冻结版式/文字/素材清单并跟踪核对，Infrastructure在专用STA上用WIC/WPF逐层合成。新生成内容只是指定的新图层，原照片与原装饰来自已验证快照，文字来自已核对原文。没有自动通道时可由用户明确选择新增背景素材完成全链路的本地部分。

**Tech Stack:** 沿用 .NET SDK 10.0.400、WPF/WIC、System.Text.Json、xUnit 2.9.3、Shouldly 4.3.0。不新增图像/字体NuGet包，不改变Photoshop生产TIFF处理。

**Spec:** [已通过设计](../specs/2026-11-03-memorial-guided-design.md)；同时读取[主计划](2026-11-03-memorial-authoring-plan.md)与[A接口](2026-11-03-memorial-prompt-workbench.md)。

2026-11-05 续接：[B3–B5 实施与验证记录](../../codex/memorial-authoring/SCRUM-11228-11230.md)。受管发布、转换和导出共用修订保护，作为同一集成提交记录；人工及生产验收继续保留。

## Global Constraints

继承主计划全部约束。A完成后B才可实施。原样沿用表示来源素材与内容不被重新绘制，只允许明确的几何变换；等比重采样后的PNG字节可以不同，不能用输出字节不同就判断人物已被重绘。参考原图不具备可用素材时准确报缺口，不调用自动抠图或装饰拆层。

## Review Focus

主计划第5项由B1/B2/B3覆盖；额外检查新背景盖住人物、预览棋盘被烘焙、辅助线进入PNG、来源变更后仍接受旧预览以及取消转换改变来源。每项都有以下任务的命名测试。

## B新增接口与数据

Domain新类型放在 `PrintFlow.Domain.AiDesign.Memorial`。几何统一使用毫米，像素只在解码/输出时使用；不把WPF Rect/Color/FontFamily放入Domain。

```csharp
record RectMm(double X, double Y, double Width, double Height);
record RectPx(int X, int Y, int Width, int Height);
record MemorialCanvas(double WidthMm, double HeightMm, double? OuterDiameterMm, double? SafeDiameterMm);
enum MemorialLayerRole { Person, OriginalDecor, GeneratedDecor, Background }
record MemorialImageLayer(Guid AssetId, MemorialLayerRole Role, RectMm Bounds, RectPx? SourceCrop, int ZOrder);
enum MemorialTextPlacement { Flat, Arc }
record MemorialTextStyle(string Key, string FontFamily, double FontSizeMm,
    RectMm Bounds, MemorialTextPlacement Placement, double? ArcRadiusMm);
record MemorialTextLine(string Key, string Text, string FontFamily, double FontSizeMm,
    RectMm Bounds, MemorialTextPlacement Placement, double? ArcRadiusMm);
record MemorialLayout(MemorialCanvas Canvas, IReadOnlyList<MemorialImageLayer> Images,
    IReadOnlyList<MemorialTextLine> Text, bool ProductTemplateConfirmed);
enum MemorialRenderPurpose { Preview, DesignMaterial }
record MemorialRenderPlan(Guid DraftId, long DraftRevision, MemorialLayout Layout,
    IReadOnlyList<AiDesignAttachment> Assets, MemorialRenderPurpose Purpose, string SourceManifestSha256);
record MemorialRenderRef(Guid DraftId, Guid RenderId, MemorialRenderPurpose Purpose);
record MemorialRenderResult(MemorialRenderRef File, long DraftRevision, FileFacts Facts, string SourceManifestSha256);
```

B1扩展Authoring schema至2，增加 `Layout:MemorialLayout?`，并在Workflow命令union增加 `SetLayout(MemorialLayout Layout)`，通过A的ApplyAsync及revision CAS保存；读取schema1时保持所有原文/照片与确认，Layout为空且待设置，不能自动确认。ParentDraftId/ParentDraftRevision继续使用A中已定义属性。`SetLayout`不能改变已核对的Text原文，只能调整样式/几何；不匹配Semantic对应字段时返回issue。

B1实施接口补充（2026-11-03）：`MemorialImageLayer.SourceExtent:RectPx?`记录原点为0的完整源像素框，B3须对真实解码尺寸核验；`AllowEdgeExtension:bool`明确未受保护新增装饰的环带权限。`MemorialLayoutModule.TextInputs(draft)`返回冻结的精确文字字典；日期原文沿用`birth-date`/`death-date`等确认key，非空日期对应的非空标签另用`DateLabelKey(field)`（`date-label:<文字确认key>`）作为图层key，与该日期共用原文/标签/解释的内容确认。日期不显示时不输出其标签。B2须沿用此接口，不能从未经核对的常量绘制标签。不同几何/裁切的SetLayout先取消版式确认，随后同内容SetLayout才可明确确认；历史恢复也遵守几何变更失效规则。

```csharp
// B1 pure geometry/permission module, no rendering or file work.
IReadOnlyList<AiAuthoringIssue> MemorialLayoutModule.Validate(AiAuthoringDraft draft, MemorialLayout layout);
// B2 preserves semantic text; missing glyph/oversized text are issues, never truncation.
OperationResult<IReadOnlyList<MemorialTextLine>> IMemorialTextLayout.Arrange(AiAuthoringDraft draft, MemorialCanvas canvas, IReadOnlyList<MemorialTextStyle> styles);
// B3 application entry freezes the current verified draft, then calls the renderer port.
Task<OperationResult<MemorialRenderResult>> MemorialCompositionModule.RenderAsync(Guid draftId, long expectedRevision, MemorialRenderPurpose purpose, CancellationToken ct);
// B3 reads owned snapshot streams and publishes a complete, independently inspected PNG.
Task<OperationResult<MemorialRenderResult>> IMemorialRenderer.RenderAsync(MemorialRenderPlan plan, CancellationToken ct);
Task<OperationResult<DecodedPreview>> IMemorialRenderPreview.LoadAsync(MemorialRenderResult expected, CancellationToken ct);
Task<OperationResult<MemorialRenderResult>> IMemorialRenderStore.ReadAsync(MemorialRenderRef file, CancellationToken ct);
Task<OperationResult<IReadOnlyList<MemorialRenderResult>>> IMemorialRenderStore.ListAsync(Guid draftId, CancellationToken ct);
// B4 re-owns selected immutable assets; no parent attachment is passed as a child-owned file.
Task<OperationResult<IReadOnlyList<AiDesignAttachment>>> IAiAuthoringAssetFork.CopyAsync(Guid sourceOwnerId, Guid targetOwnerId, IReadOnlyList<AiDesignAttachment> assets, CancellationToken ct);
Task<OperationResult<AiAuthoringDraft>> MemorialVersionModule.ForkAsync(Guid sourceId, long sourceRevision, AiProductKind target, CancellationToken ct);
// B5 design-only copy, not a production delivery/approval.
Task<OperationResult<string>> IMemorialMaterialExporter.ExportAsync(MemorialRenderResult expected, string selectedAbsolutePath, CancellationToken ct);
```

MemorialVersionModule消耗A的store和B4的asset-fork port；所有返回集合冻结。内部renderer与preview复用A3的同句柄VerifiedAiAttachmentReader。导出返回准确的新文件路径，仅来自用户明确选择的目的文件，不用于寻找/猜测生成结果。B中所有Workflow接口/模块文件均位于 `src/PrintFlow.Workflow/AiDesign/Memorial/`，除非明确写为Authoring。

核对状态独立于草稿内容修订：Domain `MemorialReviewState`包含 `Render:MemorialRenderResult`、`Revision:long`、`Checks:IReadOnlyDictionary<MemorialReviewItem,bool>`、`Accepted:bool`；`MemorialReviewItem`精确为Person/Decor/Text/Layout/Transparency。Workflow `IMemorialReviewStore.ReadAsync(MemorialRenderRef file,CancellationToken ct)` / `SaveAsync(MemorialReviewState value,long? expectedRevision,CancellationToken ct)`均返回 `Task<OperationResult<MemorialReviewState>>`，null仅新建。`MemorialReviewChecklist.SetCheckAsync(MemorialRenderResult expected,long expectedReviewRevision,MemorialReviewItem item,bool value,CancellationToken ct)`和 `AcceptAsync(MemorialRenderResult expected,long expectedReviewRevision,CancellationToken ct)`返回相同类型。每次操作从render store核对真实结果和当前draft revision/来源指纹，不接受调用者自造Facts；独立核对revision递增不会使自己的render过期。B3记录的结果清单支持重启后按DraftId读取历史，不用扫描“最新PNG”猜结果。

### Task B1: 毫米版式、保护权限与规格

2026-11-03 实施记录：[SCRUM-11226 / B1](../../codex/memorial-authoring/SCRUM-11226.md)。毫米版式、保留/几何独立权限、日期标签原文、schema1→2与CAS持久化已实现。独立审查5项Important已RED→GREEN修复；38项B1新用例，最终19组973项通过；locked restore与零警告/错误Release构建通过。本地交付，B2–B5未启动。

**Files:** Create Domain `AiDesign/Memorial/{MemorialLayout,MemorialRenderPlan}.cs`; Create Workflow `AiDesign/Memorial/MemorialLayoutModule.cs`; Modify Authoring DTO/store的schema读取；Tests `Unit/AiDesign/Memorial/MemorialLayoutTests.cs`、`Integration/AiDesign/Authoring/AuthoringSchemaUpgradeTests.cs`。

**Interfaces:** Consumes A草稿/规则；Produces B数据与Validate。精确预设：T恤 `280×390`，徽章 `86×86`、OuterDiameter=`86`、SafeDiameter=`72`；单侧环带`7`。数字必须有限且>0；安全直径≤外圆，徽章宽高等于外圆。

- [x] **Step 1 — RED tests:** `Badge_ring_is_7mm`、`Critical_bounds_must_fit_safe_circle`、`Medal_cannot_be_cropped_to_make_layout_fit`、`Unconfirmed_template_cannot_be_accepted`、`Schema1_keeps_manual_text_and_requires_layout`。断言：
  ```csharp
  ((86d - 72d) / 2).ShouldBe(7);
  outsideIssues.Select(i => i.Key).ShouldContain("OutsideSafeArea");
  croppedIssues.Select(i => i.Key).ShouldContain("PreservationConflict");
  migrated.FinalDocument.ShouldBe(original.FinalDocument);
  ```
- [x] **Step 2:** Run `dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj -c Release --filter "FullyQualifiedName~MemorialLayoutTests|FullyQualifiedName~AuthoringSchemaUpgradeTests"`；预期编译或行为失败。
- [x] **Step 3:** 用布局坐标验证：关键矩形四角均在36mm半径安全圆内；人物完整源框默认受保护，用户明确裁切后只接受被确认范围，不能自动猜人脸区域。原装饰整体变换需匹配其权限；复杂拆分保持未支持。只允许背景/明确允许的边缘装饰进入7mm环带。配置变化撤销布局确认，不撤销无变化文字内容确认。
- [x] **Step 4:** 几何、NaN/负数、边界相切、超长姓名、权限冲突及schema升级测试0失败；不改变旧Request schema。
- [x] **Step 5:** Commit `feat(memorial): validate protected layouts and product specifications`。

### Task B2: 精确文字层与弧线排版

2026-11-03 实施记录：[SCRUM-11227 / B2](../../codex/memorial-authoring/SCRUM-11227.md)。精确确认文字/日期标签、真实字体度量与缺字检查、塑形字形簇弧线、RTL段落与可复用毫米轮廓已实现。独立审查6项Important已RED→GREEN修复，44项新测试；最终20组1017项通过，locked restore与零警告/错误Release构建通过。1项极长文本扫描性能Minor延期；B3–B5未启动。

**Files:** Create Infrastructure `AiDesign/Memorial/WpfMemorialTextLayout.cs`; Create Workflow `IMemorialTextLayout.cs`; Tests `Integration/AiDesign/Memorial/MemorialTextLayoutTests.cs`。

**Interfaces:** Consumes A中Semantic、姓名/日期确认、B1 Canvas与显式styles；ProducesArrange返回的真实TextLine。Style的Key绑定语义字段，Text只能从该草稿对应已确认字段读取。WPF类型留在Infrastructure；使用系统已安装字体和WPF字体度量，不下载字体，不调用模型重写文字。

B2接入合同：`WpfMemorialTextLayout`的Infrastructure内部`Prepare(line)`返回冻结的毫米轮廓和只读字形簇来源/位置；B3须复用同一配置的适配器，不能另写一套文字测量/绘制逻辑。Domain仍只保存文字和style，不保存WPF对象或机器字体glyph编号。失败通过`OperationFailure.Context["issue"]`和`["field"]`定位。直线只采用原文显式换行，弧线目前为不超过π的单行上弧；用户指定字号不自动变化。默认2mm下限可配置，不代表实物可读性标准。RTL段落按Windows只读双向分类与显式方向标记处理；`GetStringTypeW`声明集中于既有`Automation/NativeMethods.cs`，不增加包。B3/B4在渲染/后台路径调用同步接口；极长文字的线性索引优化仍待后续处理。

- [x] **Step 1 — RED tests:** `Name_macrons_apostrophe_and_case_survive_layout`、`Long_name_offers_layout_change_instead_of_truncation`、`Missing_glyph_blocks_confirmation`、`Arc_changes_positions_not_text`。断言：
  ```csharp
  nameLine.Text.ShouldBe("ʻĀna  DEMO 陈");
  arcLine.Text.ShouldBe(flatLine.Text);
  longNameFailure.IsSuccess.ShouldBeFalse();
  originalDate.ShouldBe("12/06/1039");
  ```
- [x] **Step 2:** Run `dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj -c Release --filter FullyQualifiedName~MemorialTextLayoutTests`；预期失败。
- [x] **Step 3:** 沿用A6日期解释与确认，不对原文大小写/Unicode做归一化。直线排版使用字体度量，弧线排版放置同一文字的glyph，连字/组合符作为塑形后的cluster处理，不能按UTF-16 char逐字拆。缺字、无法容纳、未核对文字返回issue。视觉模板字号/留白集中为可调整参数；达到可读性下限时让用户换行/改版式，不静默缩字或删字。
- [x] **Step 4:** 真实系统字体度量与glyph覆盖、组合字符、CJK/Latin混排、可选日期行和弧线测试0失败；具体可读性仍需后续真实成品验收，不能由像素图推定。
- [x] **Step 5:** Commit `feat(memorial): lay out verified literal text as editable layers`。

### Task B3: 原素材合成与受管PNG

**Files:** Create Infrastructure `AiDesign/Memorial/{WpfMemorialRenderer,FileMemorialRenderStore}.cs`; Create Workflow `MemorialCompositionModule.cs`、`IMemorialRenderStore.cs`、`IMemorialRenderer.cs`、`IMemorialRenderPreview.cs`; Tests `Integration/AiDesign/Memorial/{MemorialRendererTests,MemorialRenderOwnershipTests}.cs`；必要时只为这些精确新文件更新Architecture的允许I/O清单，并新增反例。

**Interfaces:** Consumes B1/B2、A3同句柄读取；ProducesRender/Preview接口。根为 `AiDesign/Authoring/Renders/<draftId>/<renderId>/`，preview与design各是独立新对象，不能覆盖旧图。Preview固定96 DPI、DesignMaterial默认300 DPI，导出前展示真实像素；`pixels=Ceiling(mm/25.4*dpi)`，这不产生CMYK/W1或生产批准。

- [ ] **Step 1 — RED tests:** `Original_person_and_medal_are_drawn_from_verified_source`、`Background_cannot_cover_preserved_layers`、`Badge_outside_circle_is_really_alpha_zero`、`Overlay_guides_and_checkerboard_never_enter_png`、`Source_swap_or_cancel_does_not_publish_ready`。采用小型合成色块/胸章像素fixture，不用真人照片；断言实际解码像素与hash：
  ```csharp
  outsidePixel.A.ShouldBe((byte)0);
  renderedMedalPixel.ShouldBe(expectedSourceMedalPixel);
  publishedAfterCancellation.ShouldBeFalse();
  originalSourceHash.ShouldBe(hashBeforeRender);
  ```
- [ ] **Step 2:** 分别 Run `dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj -c Release --filter FullyQualifiedName~MemorialRendererTests` 和相同命令的 `FullyQualifiedName~MemorialRenderOwnershipTests`；预期失败。
- [ ] **Step 3:** App只调用CompositionModule，模块核对expectedRevision、文字/保留确认及B1布局后构建不可变plan。SourceManifestSha256对规范序列化的草稿Id/revision、完整Layout、purpose及活动素材Id/长度/hash做SHA256；renderer重新验证匹配，拒绝缺失素材或越权变换。在单个专用STA上用WIC解码＋WPF DrawingVisual/RenderTargetBitmap合成；原照/原装饰由已确认源绘制，新背景放在受保护图层之后。按目标尺寸解码、逐层释放、同一模块串行渲染，不同时全量持有8份大图。固定64百万目标像素/40 MiB输出限额，超限明确失败。辅助线、棋盘和黑白底只属于View，不进入render plan。
- [ ] **Step 4:** 原子发布前完整重新解码并核对PNG实际尺寸、alpha、hash与来源指纹；持有创建身份直到发布/失败清理。测试路径替换、reparse、坏PNG、取消和同名版本，不删未知文件或旧版本；新类＋旧OwnedStaging/Architecture通过。任何新增I/O例外仅允许确切适配器，保留禁止其他兄弟类绕过的反例。
- [ ] **Step 5:** Commit `feat(memorial): compose preserved assets into verified design PNGs`。

### Task B4: 详细页预览、结果核对与独立转换版本

**Files:** Create Domain `AiDesign/Memorial/MemorialReviewState.cs`; Create Workflow `AiDesign/Memorial/{MemorialVersionModule,IAiAuthoringAssetFork,MemorialReviewChecklist,IMemorialReviewStore}.cs`; Create Infrastructure `AiDesign/Authoring/FileAiAuthoringAssetFork.cs`、`AiDesign/Memorial/FileMemorialReviewStore.cs`; Create VM `.Layout.cs`、`.Results.cs`; Create Views `MemorialLayoutView.xaml`、`MemorialResultReviewView.xaml`; Tests `Integration/AiDesign/Memorial/MemorialVersionTests.cs`、`Integration/Ui/MemorialConversionTests.cs`。

**Interfaces:** Consumes A与B1–B3；ProducesFork、受管preview与五组人工check。Checklist模块使用上述ReviewStore及ReviewState；通过Render准确绑定RenderRef、DraftRevision、SourceManifestSha256，没有这些来源信息的旧checkbox不能套在新结果上。核对存储位于 `AiDesign/Authoring/Reviews/<draftId>/<renderId>/`，使用A2相同CAS/owned发布原语；重开时恢复同一结果的核对状态。

- [ ] **Step 1 — RED tests:** `Tshirt_to_badge_forks_without_changing_parent`、`Cancel_conversion_keeps_source_result`、`Changed_name_only_revokes_its_text_and_layout_checks`、`Stale_render_cannot_be_accepted`、`Child_assets_are_owned_by_child_and_preserve_bytes`、`Restart_restores_checks_for_exact_render_without_mutating_draft_revision`。断言：
  ```csharp
  child.ParentDraftId.ShouldBe(source.Id);
  child.ParentDraftRevision.ShouldBe(source.Revision);
  child.Id.ShouldNotBe(source.Id);
  parentAfter.ShouldBe(parentBefore);
  childAsset.Snapshot.RequestId.ShouldBe(child.Id);
  childAsset.Sha256.ShouldBe(parentAsset.Sha256);
  ```
- [ ] **Step 2:** 分别 Run `dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj -c Release --filter FullyQualifiedName~MemorialVersionTests` 及同命令的 `"FullyQualifiedName~MemorialConversionTests&WorkstationEntryPhase!=Runtime&FullyQualifiedName!~PrintFlow.Tests.Smoke."`；UI额外拼接A中11项排除，预期失败。
- [ ] **Step 3:** 明确选定sourceId/revision后新建child，记录父版本并从已验证流复制活动素材到child owner；按新AssetId重绑槽位/保留规则，不把parent文件冒充child所有。保持未变文字的内容确认，目标版式重新核对。两种成品双向适配，显示移动/等比缩放/文字布局/安全区差异；权限不足就等待选择，不自动裁掉内容。复制中断不发布半完成子稿，来源不变。
- [ ] **Step 4:** 新render必须与当前revision/来源hash相同；修改后旧结果只能作历史查看。五组check完成才允许接受；人脸/装饰/文字可读性由人核对，文件及几何检查不代替这一步。图片缓存按RenderRef，按尺寸释放；两类测试和三条旧路线针对性回归0新增失败。
- [ ] **Step 5:** Commit `feat(memorial): review protected results and fork product variants safely`。

### Task B5: 设计素材导出、能力门禁与全组验证

**Files:** Create Workflow `IMemorialMaterialExporter.cs`; Create Infrastructure `AiDesign/Memorial/FileMemorialMaterialExporter.cs`; Create App Navigation `IAiDesignPngSavePicker.cs`与SaveFileDialog适配器；Modify新VM/Composition/资源；Tests `Integration/AiDesign/Memorial/MemorialExportTests.cs`、`Integration/Ui/MemorialWorkflowAcceptanceTests.cs`。

**Interfaces:** Consumes已核对的RenderResult与用户明确选择的PNG目的路径；ProducesExportAsync。`IAiDesignPngSavePicker.PickDestination(string suggestedFileName):string?`返回明确目的路径，取消返回null。导出适配器从ReviewStore核对该DesignMaterial结果已被接受且仍匹配当前草稿，不能用低分辨率Preview冒充最终素材。实现构造时接收Composition已知的受保护root集合；原工作区内部受保护路径、签署的preset/baseline/evidence沿用既有禁止覆盖政策。不能把AI导出接入生产批准/交付资格。

- [ ] **Step 1 — RED tests:** `Export_copies_exact_verified_bytes_without_overwrite`、`Protected_destination_and_symlink_are_refused`、`Review_and_export_do_not_create_production_approval`、`Import_button_remains_unavailable_without_idempotent_importer`、`Offline_manual_background_supports_local_workflow`。断言：
  ```csharp
  destinationHash.ShouldBe(accepted.Facts.Sha256);
  existingDestinationBytes.ShouldBe(before);
  productionApprovalCount.ShouldBe(0);
  autoImportEnabled.ShouldBeFalse();
  ```
- [ ] **Step 2:** 分别 Run `dotnet test tests/PrintFlow.Tests/PrintFlow.Tests.csproj -c Release --filter FullyQualifiedName~MemorialExportTests` 及同命令的 `"FullyQualifiedName~MemorialWorkflowAcceptanceTests&WorkstationEntryPhase!=Runtime&FullyQualifiedName!~PrintFlow.Tests.Smoke."`；UI额外拼接A中11项排除，预期失败。各任务GREEN复用该任务的相同filter，要求0失败。
- [ ] **Step 3:** 导出只使用预期RenderRef/hash的已核验流，目标采用CreateNew与创建身份清理，不覆盖现有文件；取消/失败保留受管结果与原文件。界面写“设计素材PNG，仍需印刷核对”。允许人工选择的新背景走同样接收/角色/核对，不伪造Work生成回执。没有原11165的真实事务性幂等导入能力时，只提供素材导出及人工进入既有流程的说明，不启用自动接受→Session按钮。
- [ ] **Step 4:** locked restore＋Release build，运行A+B新测试、旧AI/Architecture/Startup/Import及三路线定向回归；新增UI和大型渲染类各用新进程，保留旧11排除/Smoke/Runtime门禁。核对保护规则、长姓名/缺字、日期修改、互转、取消/重启、原装饰缺失、源文件被替换、真实alpha与辅助线不导出。只在实际执行后记录结果。
- [ ] **Step 5:** Commit `feat(memorial): export reviewed design materials with honest capability gates`；写A/B覆盖与未运行列表。真实Work、真人/DPI/读屏、实际印刷、安装发布仍按授权与原待办单独验收。

## 自审完成条件

- [x] A的所有类型/属性与本计划一致；同稿切换不变Id，成品fork必须新Id并有父revision。
- [x] 几何、文字、合成、来源所有权、结果核对与导出各有接口行为测试。
- [x] 无通过占位图/模拟成功启用严格保真、远端通道或自动Session导入。
- [x] 导出的物理像素/hash来自真实PNG检查；原照与装饰保持源hash，变换权限可追踪；不承诺缩放后输出字节与源字节相同。

2026-11-03已完成计划自审；以上勾选是规划审查结果，不代表实现或测试已完成。
