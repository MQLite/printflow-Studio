# Memorial Prompt Workbench Implementation Plan — A

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 独立交付模板可编辑、最终全文可编辑、引用处可选照片、同稿可切详细的离线提示词工作台。

**Architecture:** `AiAuthoringModule` 是App与测试共同调用的接口；模块内部处理模板组装、草稿修订、引用和确认失效。模板与草稿分别持久化；图片沿用既有受管快照。`AiPromptDocument`保留文字节点与图片引用节点，WPF编辑器只是投影，复制时展开为真实附件上传名。

**Tech Stack:** .NET SDK 10.0.400、WPF、WPF-UI 4.3.0、CommunityToolkit.Mvvm 8.4.2、System.Text.Json、xUnit 2.9.3、Shouldly 4.3.0；无新增包。

**Spec:** [已通过设计](../specs/2026-11-03-memorial-guided-design.md)；执行前读取[主计划](2026-11-03-memorial-authoring-plan.md)。

## Global Constraints

继承主计划全部约束。快速模式不强制六步，不调用远端模型；复制草稿不受自动通道/合成能力门禁影响。旧 `AiDesignPrompt`/Request schema及三个v1模板保持兼容；新增Authoring不伪装成已就绪Request。

## Review Focus

主计划第1–4项分别落在A1/A4/A6、A3/A5、A4/A6、A2/A5/A7。测试必须包括粘贴文字、文件选择器延迟、保存失败与关闭取消，不能只用VM属性断言替代编辑/保存行为。

## 约定类型与接口

以下均为**待创建的计划接口**，当前源码不存在；新增Domain类型位于 `PrintFlow.Domain.AiDesign.Authoring`，模块与ports位于 `PrintFlow.Workflow.AiDesign.Authoring`。

```csharp
enum AiAuthoringMode { Quick, Detailed }
enum AiProductKind { MemorialTshirt, MemorialBadge, TextChange }
enum AiTemplateFieldKind { Text, OptionalDateLine, ImageReference }
enum AiTemplateSemantic { FreeText, Name, BirthDate, DeathDate, PersonImage, LayoutImage, EditSourceImage }
record AiTemplateRef(string Id, int Revision);
record AiTemplateField(string Key, AiTemplateFieldKind Kind, bool Required,
    string? DefaultValue = null, string? SlotId = null, string? DateLabel = null,
    AiTemplateSemantic Semantic = AiTemplateSemantic.FreeText);
record AiTemplateContent(string Name, AiProductKind Product, string Body,
    IReadOnlyList<AiTemplateField> Fields);
record AiTemplateDefinition(AiTemplateRef Reference, AiTemplateContent Content, string? BuiltinOriginId);
abstract record AiPromptPart(Guid PartId);
record AiTextPart(Guid PartId, string Text) : AiPromptPart(PartId);
record AiImagePart(Guid PartId, string SlotId) : AiPromptPart(PartId);
record AiPromptDocument(IReadOnlyList<AiPromptPart> Parts);
record AiReferenceSlot(string SlotId, string Role, bool Required);
record AiSlotBinding(string SlotId, Guid AssetId);
record AiAuthoringIssue(string Key, string? FieldKey, string? SlotId);
record AiPromptComposition(AiPromptDocument Document, IReadOnlyList<AiAuthoringIssue> Issues);
record AiPromptExport(string Text, IReadOnlyList<AiDesignAttachment> RequiredAttachments,
    IReadOnlyList<AiAuthoringIssue> Issues);
```

`AiAuthoringDraft`为init-only record，必须定义这些准确属性：`Id:Guid`、`Revision:long`、`SchemaVersion:int=1`、`Template:AiTemplateRef`、`TemplateSnapshot:AiTemplateContent`、`Product:AiProductKind`、`Mode:AiAuthoringMode`、`Fields:IReadOnlyDictionary<string,string>`、`InitialDocument/FinalDocument:AiPromptDocument`、`Assets:IReadOnlyList<AiDesignAttachment>`、`Bindings:IReadOnlyList<AiSlotBinding>`、`IsManuallyEdited/NeedsFieldReconcile:bool`、`Confirmations:IReadOnlyDictionary<string,string>`、`Preservation:IReadOnlyList<AiPreservationRule>`、`ParentDraftId:Guid?`、`ParentDraftRevision:long?`。确认值存所确认输入的内容指纹，不存一个无来源的true。`AiPreservationRule`由A6解释；A1先以同名record定义 `AssetId:Guid`、`KeepContent:bool`、`AllowMove/AllowUniformScale/AllowCrop/AllowRedraw:bool`，B只扩展版式而不改变这些语义。

草稿另含 `Slots:IReadOnlyList<AiReferenceSlot>`：初始从模板图片字段创建，详细页可新增人物/原装饰/版式/风格等角色，不限于内置两个槽位。新增命令 `InsertImageReference(Guid PartId,AiReferenceSlot Slot,Guid? AfterPartId)`同时保存槽位定义与插入节点；null表示文档末尾，PartId/SlotId必须唯一。删除节点保留未使用素材；导出角色来自Slots，不从“图几”文字猜测。

字段Key保留模板中的准确拼写；Semantic用于快速→详细映射，不能因用户改变量名而丢失“姓名/生卒日期/人物图”的含义。三份内置模板必须填正确Semantic；同一纪念模板的Name/BirthDate/DeathDate/PersonImage不允许含糊重复，新增普通图片槽位可待用户分配角色。OptionalDateLine的Fields值是原始日期文本，标签从DateLabel或显式的 `<Key>.label` 覆盖读取，日期解释存在 `<Key>.format`，不把标签重复拼入日期。格式明确值为 `dd/MM/yyyy`、`MM/dd/yyyy`、`yyyy-MM-dd`、`yyyy`；缺失/歧义未解释时保持待核对。

`AiAuthoringEdit`使用明确的sealed record分支：`SetField(string Key,string Value)`、`ReplaceDocument(AiPromptDocument Document)`、`Recompose(bool PreservePrevious)`、`BindImage(string SlotId,SelectedAiDesignFile Selection)`、`BindExistingImage(string SlotId,Guid AssetId)`、`RemoveReference(Guid PartId)`、`RemoveImage(Guid AssetId)`、`SwitchMode(AiAuthoringMode Mode)`、`SelectTemplate(AiTemplateRef Template)`；A6再增加规则/确认编辑。此命令union位于Workflow，因其图片选择参数属于既有Workflow类型；Domain只持久化处理后的纯数据。所有集合在进入模块和存储前深度冻结。

```csharp
// A1: pure composer; values inside operator text are never re-parsed as template code.
AiPromptComposition PromptDocumentComposer.Compose(AiTemplateContent template, IReadOnlyDictionary<string,string> fields);
// A2: storage revision is 0 on creation, +1 per committed change; stale writes fail.
Task<OperationResult<AiAuthoringDraft>> IAiAuthoringDraftStore.ReadAsync(Guid id, CancellationToken ct);
Task<OperationResult<AiAuthoringDraft>> IAiAuthoringDraftStore.SaveAsync(AiAuthoringDraft value, long? expectedRevision, CancellationToken ct);
// A2: source null creates new; builtin save produces a personal id, keeping BuiltinOriginId.
Task<OperationResult<AiTemplateDefinition>> AiTemplateCatalog.SaveAsync(AiTemplateRef? source, AiTemplateContent content, bool saveAsNew, CancellationToken ct);
Task<OperationResult<IReadOnlyList<AiTemplateDefinition>>> AiTemplateCatalog.ListAsync(CancellationToken ct);
// A2/A3/A6: one authoring interface; file and business work are not split across VMs.
Task<OperationResult<AiAuthoringDraft>> AiAuthoringModule.CreateAsync(AiTemplateRef template, CancellationToken ct);
Task<OperationResult<AiAuthoringDraft>> AiAuthoringModule.ReadAsync(Guid id, CancellationToken ct);
Task<OperationResult<AiAuthoringDraft>> AiAuthoringModule.ApplyAsync(Guid id, long expectedRevision, AiAuthoringEdit edit, CancellationToken ct);
AiPromptExport AiAuthoringModule.Export(AiAuthoringDraft draft);
// A3: bounded immutable PNG preview, using the existing Workflow.Ports.DecodedPreview type.
Task<OperationResult<DecodedPreview>> IAiAuthoringAssetPreview.LoadAsync(Guid ownerId, AiDesignAttachment asset, CancellationToken ct);
```

模板目录偏好（收藏、排序、默认）属于Domain的 `AiTemplateLibrary`：`Revision:long`、`Templates:IReadOnlyList<AiTemplateDefinition>`、`FavoriteIds/OrderedIds:IReadOnlyList<string>`、`DefaultId:string`。A2定义 `IAiTemplateLibraryStore.ReadAsync(CancellationToken ct)` / `SaveAsync(AiTemplateLibrary value,long expectedRevision,CancellationToken ct)`，均返回 `Task<OperationResult<AiTemplateLibrary>>`；空目录初始化为revision 0及三份内置模板。Catalog的 `UpdatePreferencesAsync(long expectedLibraryRevision,IReadOnlyList<string> favorites,IReadOnlyList<string> order,string defaultId,CancellationToken ct)`、`DeletePersonalAsync(AiTemplateRef source,CancellationToken ct)`、`RestoreBuiltinAsync(string builtinId,CancellationToken ct)`也返回该Task类型。已使用模板的完整内容保存在草稿内，不依赖目录继续存在。

### Task A1: 模板和结构化提示词文档

2026-11-03 实施记录：[SCRUM-11219 / A1](../../codex/memorial-authoring/SCRUM-11219.md)。新旧模板23项通过，AI/架构回归复跑596项通过；首次旧NativeHost时序失败保留在记录中；独立代码审查无问题。

**Files:** Create `src/PrintFlow.Domain/AiDesign/Authoring/{AiTemplateDefinition,AiPromptDocument,AiAuthoringDraft,AiPromptExport}.cs`; Create `src/PrintFlow.Workflow/AiDesign/Authoring/{AiAuthoringEdit,BuiltInAiTemplates,PromptDocumentComposer}.cs`; Test `tests/PrintFlow.Tests/Unit/AiDesign/Authoring/PromptDocumentComposerTests.cs`。

**Interfaces:** Consumes design样例；Produces以上DTO、`BuiltInAiTemplates.All`与`Compose`。三个模板id准确为 `memorial-tshirt-quick`、`memorial-badge-quick`、`memorial-text-change-quick`；将样例人工整理为Workflow静态数据，不在运行时读output目录。

- [x] **Step 1 — RED tests:** 定义 `Compose_preserves_operator_literals`、`Missing_dates_omit_whole_label_line`、`Unknown_variable_remains_visible`、`Image_variable_creates_slot_not_a_path`。断言至少含：
  ```csharp
  literal.ShouldBe("  ʻĀna\r\n陈 {{姓名}}  "); // value copied exactly; no recursive substitution
  missingDatesText.ShouldNotContain("SUNRISE");
  imagePart.SlotId.ShouldBe("person-photo");
  unknownText.ShouldContain("{{未定义}}");
  ```
- [x] **Step 2:** Run `dotnet test tests/PrintFlow.Tests -c Release --filter FullyQualifiedName~PromptDocumentComposerTests`；预期未创建类型或行为断言失败。
- [x] **Step 3:** 实现固定语法 `{{字段名}}` 单次替换；OptionalDateLine的标签与值形成一个可省略行；普通未知/空变量保留并报issue；图片输出 `AiImagePart`。body没有变量也合法；不加脚本/表达式/递归。内置中英文提示分别定义，客户值不翻译。
- [x] **Step 4:** 同filter必须0失败，并跑原 `PromptTemplateTests`；v1行为不变。
- [x] **Step 5:** Commit `feat(ai-authoring): compose editable prompt documents from reusable templates`。

### Task A2: 模板个人版本和可恢复草稿

2026-11-03 实施记录：[SCRUM-11220 / A2](../../codex/memorial-authoring/SCRUM-11220.md)。19项新增测试；针对性66项、最终AI/架构分组615项通过；locked restore/Release build通过。独立审查4项Important均已RED→GREEN修复。当前稿重开已实现；历史版本浏览/恢复接口需在A5接入UI前补齐。

**Files:** Create Domain `AiDesign/Authoring/AiTemplateLibrary.cs`; Create Workflow `IAiAuthoringDraftStore.cs`、`IAiTemplateLibraryStore.cs`、`AiTemplateCatalog.cs`、`AiAuthoringModule.cs`; Create Infrastructure `AiDesign/Authoring/{FileAiAuthoringDraftStore,FileAiTemplateLibraryStore}.cs`; Tests `Integration/AiDesign/Authoring/{AuthoringPersistenceTests,TemplateCatalogTests}.cs`。

**Interfaces:** Consumes A1；Produces存储/Catalog/Module接口。文件根为 `AiDesign/Authoring/Drafts/<id>/` 与 `AiDesign/Authoring/Templates/`；不修改SQLite或旧Request schema。草稿保存保留不可变历史版本及当前指针，模板版本与草稿版本独立。

- [x] **Step 1 — RED tests:** `Two_store_instances_cannot_overwrite_same_revision`、`Builtin_edit_preserves_original_and_frozen_old_draft`、`Failed_publication_keeps_last_saved_draft`、`Deleting_template_does_not_erase_saved_prompt`。断言：
  ```csharp
  saves.Count(x => x.IsSuccess).ShouldBe(1);
  saved.TemplateSnapshot.Body.ShouldBe(originalBody);
  reopened.FinalDocument.ShouldBe(lastCommittedDocument);
  personal.BuiltinOriginId.ShouldBe("memorial-tshirt-quick");
  ```
- [x] **Step 2:** Run filter `FullyQualifiedName~AuthoringPersistenceTests|FullyQualifiedName~TemplateCatalogTests`；预期行为失败。
- [x] **Step 3:** 实现FileShare.None锁＋CAS；原子发布只操作本次创建并持有身份的临时文件，复用现有owned-staging原语，拒绝reparse ancestry，不广泛删除目录。`SaveAsync(expectedRevision:null)`仅新建；模板保存/恢复/偏好修改均保留历史。Module先实现非图片编辑、读取、创建、按真实最终文档复制；切模板和重建前写历史快照，写入失败保持原稿并返回失败。
- [x] **Step 4:** 新存储测试与原 `AiDesignRequestStoreTests`、`AiDesignOwnedStagingFilesTests`均0失败；测试真实磁盘、损坏JSON、取消、两实例竞争及owned替换失败，禁止用fixture返回期望字符串代替。
- [x] **Step 5:** Commit `feat(ai-authoring): persist versioned templates and drafts without overwriting edits`。

### Task A3: 槽位、受管照片与纯文本交接

**Files:** Modify Module及必要的 `Infrastructure/AiDesign/FileAiDesignAttachments.cs`; Create Infrastructure `AiDesign/Authoring/{VerifiedAiAttachmentReader,FileAiAuthoringAssetPreview}.cs`; Create Workflow `IAiAuthoringAssetPreview.cs`; Tests `Integration/AiDesign/Authoring/AuthoringReferenceTests.cs`。

内部读取契约：`VerifiedAiAttachmentReader.OpenAsync(Guid ownerId,AiDesignAttachment asset,CancellationToken ct):Task<OperationResult<VerifiedAiAttachmentRead>>`。`VerifiedAiAttachmentRead:IAsyncDisposable`持有已核验的只读`Stream`与祖先/文件身份句柄，直至调用者完成读取才释放；不向调用者提供用于重新打开的路径。预览有2048像素最长边限制，返回真实解码后的PNG payload；验证与预览期间均不修改快照。

**Interfaces:** Consumes A1/A2与现有 `IAiDesignAttachments.SnapshotAsync/VerifyAsync`；Produces `BindImage/BindExistingImage/RemoveReference/RemoveImage`、`Export`的真实RequiredAttachments，以及Preview seam。DraftId作为快照ownerId，复用现有Requests附件根与持久化高水位计数；不创建 `request.json`，旧ListAsync仍忽略无authority的目录。

- [ ] **Step 1 — RED tests:** `Replacing_person_image_preserves_manual_text`、`Same_names_and_reordering_do_not_rebind_slots`、`Removing_reference_does_not_upload_unused_asset`、`Changed_original_cannot_change_snapshot`、`One_asset_can_fill_two_roles_once`、`Inserted_decor_reference_keeps_explicit_role_after_roundtrip`。断言：
  ```csharp
  after.FinalDocument.Parts.OfType<AiTextPart>().ShouldBe(beforeTextParts);
  export.RequiredAttachments.Select(a => a.Id).Distinct().Count().ShouldBe(1);
  export.Text.ShouldContain("attachment-02.png");
  export.Text.ShouldNotContain("C:\\");
  ```
- [ ] **Step 2:** Run filter `FullyQualifiedName~AuthoringReferenceTests`；预期失败。
- [ ] **Step 3:** 执行明确选图后封存，用稳定SlotId→AssetId绑定，不解析普通“图3”。替换时只把仍活动的独立素材传入SnapshotAsync，历史素材元数据另保留、不计当前8份/80MiB；编号继续用旧模块持久化高水位。复用同图需明确绑定已有AssetId或验证完整hash/长度后去重，不能按文件名去重。抽取同句柄验证读取供预览/B合成复用，包含seal、长度、hash及祖先检查，禁止verify后另开未核验路径。
- [ ] **Step 4:** Export仅展开文档仍引用的槽位，缺图输出待补充说明；移除引用不等于删除素材/目录。新测试＋旧Attachment/OwnedStaging/Architecture测试0失败，并验证新草稿不进入旧Request列表。
- [ ] **Step 5:** Commit `feat(ai-authoring): bind inline image roles to verified attachment snapshots`。

### Task A4: 可编辑全文与内嵌选图控件

**Files:** Create `src/PrintFlow.App/Controls/AiPromptEditor.xaml`、`.xaml.cs`及`AiPromptEditorProjection.cs`; Test `tests/PrintFlow.Tests/Integration/Ui/AiPromptEditorTests.cs`。

**Interfaces:** Consumes A1文档；Produces dependency property `Document:AiPromptDocument`、`ImageReferenceInvoked`（SlotId事件）及 `DocumentEdited`（新Document事件），供A5使用。控件不自行保存、选文件或组装模板。

- [ ] **Step 1 — RED tests:** `Typing_and_paste_preserve_text_around_reference_nodes`、`Removing_token_emits_reference_removal_without_choosing_another_image`、`Keyboard_can_activate_both_photo_slots`。断言实际编辑后的模型与复制结果：
  ```csharp
  editedText.ShouldBe("  ʻĀna\r\n陈 {{姓名}}  ");
  invokedSlot.ShouldBe("layout-reference");
  plainTypedImage3.ShouldBeOfType<AiTextPart>();
  ```
- [ ] **Step 2:** 在安全UI过滤器下单独运行该类，预期失败；不启动真实桌面输入。
- [ ] **Step 3:** RichTextBox/FlowDocument只投影已知Text/Image节点，引用用可聚焦按钮；禁止把TextRange.Text附带的末尾换行回写数据，也不能正则重建所有引用。实现caret、粘贴、撤销/重做的节点编辑映射；模板变量在最终文档中不是可执行语法。
- [ ] **Step 4:** 字符、引用身份、键盘焦点、重绑定循环和撤销/重做测试0失败；绑定错误为0。
- [ ] **Step 5:** Commit `feat(ui): edit prompt text with actionable image-reference tokens`。

### Task A5: 快速页与模板编辑

**Files:** Create `ViewModels/AiDesign/{AiPromptWorkbenchViewModel,AiPromptWorkbenchViewModel.Quick,AiPromptWorkbenchViewModel.Templates}.cs`; Create `Views/AiDesign/{AiPromptWorkbenchView,QuickPromptView,TemplateEditorView}.xaml`及必要code-behind；Modify `Resources/Strings.resx`、`Strings.zh-CN.resx`; Tests `Integration/Ui/{AiQuickPromptTests,AiTemplateEditorTests}.cs`。

**Interfaces:** Consumes Module/Catalog/Preview、现有 `IFilePicker` 和A4；Produces VM `OpenAsync(Guid? draftId,ct)`、`FlushAsync(ct):Task<OperationResult<AiAuthoringDraft>>`，`PickSlotImageCommand(string)`、`ComposeCommand`、`CopyCommand`、`SaveDraftCommand`、`EditTemplateCommand`。新增 App seam `IAiPromptClipboard.SetText(string)`，系统Clipboard与测试adapter各自实现，避免测试触碰真实剪贴板。

- [ ] **Step 1 — RED tests:** 用真实Module＋临时store＋受控picker验证生成后手改、改表单不覆盖、保存模板不改旧稿、同名异图、picker取消/晚返回。断言：
  ```csharp
  clipboard.Text.ShouldBe(verbatimFinalText);
  afterTemplateSave.FinalDocument.ShouldBe(manualDocument);
  inactiveScreenHasNewPhoto.ShouldBeFalse();
  ```
- [ ] **Step 2:** 安全过滤分别运行两类，预期失败。
- [ ] **Step 3:** 实现三栏Quick与模板对话页；复用05概念图但不照搬固定宽度。照片卡和内嵌按钮调用同一Slot命令。每次action先捕获编辑器最新文档并Flush；模板切换/重建先保存旧稿，手改时提供保留旧稿后重建/取消。保存客户实例为通用模板前展示变量还原预览，确认后存。Picker返回需同时匹配草稿Id、revision和VM呈现epoch；Deactivate只撤销呈现资格，不把晚结果绑定到其他草稿。
- [ ] **Step 4:** 两类测试0失败；Quick默认、无需六步、缺图仍可复制待补充草稿；图1/图2真实映射与文件名清单正确。客户名字、日期和图片不成为模板默认值。
- [ ] **Step 5:** Commit `feat(ui): add quick memorial prompts and reusable template editing`。

### Task A6: 同稿切详细、文字与保留确认

**Files:** Create Workflow `Authoring/{AiDetailedReview,AiAuthoringConfirmationRules}.cs`; Create VM `.Detailed.cs`; Create View `DetailedPromptView.xaml`; Tests `Unit/AiDesign/Authoring/DetailedConfirmationTests.cs`、`Integration/Ui/AiModeHandoffTests.cs`。

**Interfaces:** Consumes A1–A5；Adds edits `SetPreservation(AiPreservationRule Rule)`、`ConfirmField(string Key,string ExpectedContentHash)`、`SetDateInterpretation(string FieldKey,string Format)`、`AcknowledgeDocumentReconciliation(string ExpectedDocumentHash,string ExpectedFieldsHash)`；Produces `AiDetailedReview.GetFirstIncompleteStep(AiAuthoringDraft):int`（1..6）、`ValidateDates(string? birth,string? death,string birthFormat,string deathFormat):IReadOnlyList<AiAuthoringIssue>`。姓名确认key=`name`，日期=`birth-date`/`death-date`；标签/解释变化使对应hash失效。模板变量改名后通过Semantic找到对应值；图片保留确认绑定源内容hash和权限，不依赖图1/图2显示序号。Reconciliation仅在用户并排核对且两个hash仍匹配时解除冲突标记，不解析手改全文，也不代替任何逐项确认；任一侧再改即重新待核对。

- [ ] **Step 1 — RED tests:** `Quick_detail_roundtrip_keeps_id_images_and_manual_document`、`Manual_name_conflict_is_not_auto_parsed_or_confirmed`、`Changing_color_does_not_revoke_name_confirmation`、`Year_1039_is_not_silently_changed`、`Ambiguous_day_month_needs_explicit_format`。断言：
  ```csharp
  detailed.Id.ShouldBe(quick.Id);
  detailed.FinalDocument.ShouldBe(manualDocument);
  detailed.NeedsFieldReconcile.ShouldBeTrue();
  detailed.Confirmations.ShouldNotContainKey("name");
  originalDate.ShouldBe("12/06/1039");
  ```
- [ ] **Step 2:** 分别运行两个新类，预期失败。
- [ ] **Step 3:** 实现六步输入/对照，带入已知值但不伪造确认；原文/字段不一致时并排核对；用户确认后才接受指定内容hash。省略日期不编造值；合法日期与先后关系校验，异常年份只提醒并记录用户核实。模式切换前Flush，在同一Draft内Apply，不调用旧Coordinator.CreateDraft/Start。B未完成时版式预览/严格合成明确不可用；仍可编辑、保存和复制完整需求草稿。
- [ ] **Step 4:** 新测试0失败，并验证返回Quick后全文/图片不丢；确认失效仅影响相关项目，无法用手改纯文本绕过保护。
- [ ] **Step 5:** Commit `feat(ai-authoring): continue quick drafts in detailed memorial review`。

### Task A7: 接入首页、退出保护与整合交付

**Files:** Create Navigation `IAiDesignNavigation.cs`、`IAiAuthoringCloseGuard.cs`、`IAiDraftLeavePrompt.cs`和系统prompt adapter；Modify `NavigationService.cs`、`Composition/ServiceRegistration.cs`、`HomeViewModel.cs`、`HomeView.xaml`、`MainWindow.xaml`、`MainWindow.xaml.cs`、新VM与资源；Tests `Integration/Ui/AiAuthoringEntryTests.cs`、`Integration/AiDesign/Authoring/AuthoringGraphTests.cs`。

**Interfaces:** `IAiDesignNavigation.GoToPromptWorkbenchAsync(Guid? draftId,ct):Task`，NavigationService实现，和现有INavigationService解析到同一singleton。新入口能力可选注入Home，旧fixture构造不强迫改写；入口只在当前Home且无导入操作时可用。`IAiAuthoringCloseGuard.RequestCloseAsync(ct):Task<bool>`由工作台VM实现；`IAiDraftLeavePrompt.ChooseAsync(ct):Task<AiDraftLeaveChoice>`，enum值`Save/Discard/Cancel`。

- [ ] **Step 1 — RED tests:** `Home_opens_prompt_only_workbench_without_starting_workflow_or_browser`、`Save_failure_blocks_close_and_mode_change`、`Close_cancel_keeps_latest_editor_content`、`Double_close_awaits_one_save`；用真实store与受控prompt adapter，验证保存失败不导航、取消不丢编辑、成功保存后重开原文。
- [ ] **Step 2:** 安全过滤运行两个新类，预期失败。
- [ ] **Step 3:** 注册新模块/文件存储/VM/DataTemplate。Home新增“AI设计提示词”入口，原Unavailable文案只对应实际不可用能力；不全局打开自动生图。VM保存/返回先Flush；正常关闭若有未保存编辑提供Save/Discard/Cancel，失败或取消保持窗口。MainWindow仅在Current实现CloseGuard时异步协调关闭及重入，Shell仍只持Current，不接管业务。进程异常退出只恢复上次成功保存稿，界面不把未落盘内容标已保存。
- [ ] **Step 4:** locked restore＋Release build＋本计划所有新测试＋原AI/Architecture/Startup/ManualResultImport/Home路线针对性回归0新增失败；UI分进程。检查zh/en键/占位符、1000×700和1920×1080离屏布局，主操作与两个图片按钮均可键盘到达。真实窗口/DPI/读屏另列NOT RUN。
- [ ] **Step 5:** Commit `feat(ai-authoring): expose prompt workbench with durable leave protection`；更新任务与验证台账，明确A已交付、B/真实自动通道仍待完成。

## 运行命令约定

测试项目准确路径为 `tests/PrintFlow.Tests/PrintFlow.Tests.csproj`；上文简写 `dotnet test tests/PrintFlow.Tests`使用同一项目。RED首次可包含新项目的编译失败；随后必须有行为断言失败证据。GREEN执行后记录完整命令/退出码/计数，不能引用旧结果。

UI通用filter：`(<类名组合>)&WorkstationEntryPhase!=Runtime&FullyQualifiedName!~PrintFlow.Tests.Smoke.` 再拼接现有11项精确排除。目录使用每任务新的唯一子目录，`--blame-hang-timeout 120s --blame-hang-dump-type none`；不要运行OperatorInteractive或有授权清单要求的Runtime方法。

## 自审完成条件

- [x] A1–A7的数据/接口名称一致，新增类型均有定义任务。
- [x] 模板编辑、本次编辑、引用选择、往返模式和保存失败均有真实行为测试归属。
- [x] 不因草稿可复制而启用自动通道，不把图片槽位标签当真实附件身份。
- [x] 只新增Authoring能力，旧Request schema/生产流程不受破坏；B缺少时UI准确披露。

2026-11-03已完成计划自审；以上勾选是规划审查结果，不代表实现或测试已完成。
