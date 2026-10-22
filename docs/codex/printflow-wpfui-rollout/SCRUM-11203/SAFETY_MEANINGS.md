# 不可合并的文案含义

编号供 COPY_INVENTORY.meaningAndRisk 引用。标签由关键词辅助分组，不能替代对消费者条件的业务判断。所有状态仍取自原有服务及投影；文字不能创造审核、保存或自动化资格。

| 规则 | 当前事实 / 可以怎样写 | 不可推断或删除的限制 | 主要来源 / 后续验证 |
| --- | --- | --- | --- |
| S00-CONTEXT | 当前页只描述当前任务和合法下一步。 | 来源登记不是运行时可见性证明；保留只读、空、忙、禁用、未知和错误的区别。 | U01页面条件、当前ViewModel、XAML绑定；按真实状态复核。 |
| S01-REVIEW | 等待检查、已通过审核、已驳回分开；审核只针对当前精确结果。 | 生成/校验成功≠人工审核；使用裁切≠审核通过；已回导≠已审核。保留ReviewApprovalButton、TargetIdentity、手势保护、必要审核及下游失效。 | Session_Guidance*、Session_Approve*、ReviewState_*；当前结果身份变更/旧手势不能批准新结果。 |
| S02-SAVE | 已审核尚未保存；已核实保存；保存结果待核实分开。 | 内部Approved文件不是外部交付；联合确认保存入口受原有资格限制；保存阶段不可取消时不能说“可随时取消”。 | FinalSave_*、Session_Completion*；保留原协调器与不覆盖合同。 |
| S03-COMPLETE | 只有任务状态Completed可写任务已完成；保存事实独立呈现。 | 已保存≠已完成；任务完成≠外部文件存在；清理待重试≠丢弃已批准输出。 | Session_StatusCompleted、Session_CompletionFinished/NotFinished/CleanupPending。 |
| S04-HISTORY | 已有匹配记录可写有保存记录，当前文件待核实。 | HistoryUnknown不是SavedPreviously；读取失败不是零条；记录过时必须标明；兄弟尺寸保存不算当前尺寸保存；历史只读不能批准当前结果。 | SCRUM-11153_COPY_REVIEW、11154_COPY_REVIEW；Home_Recent*、FinalSave_History*、Session_History*。 |
| S05-STOP | 请求停止/接管→正在停止/正在交接；确认终态后才写已停止或已移交。 | 不承诺外部应用已停止、导出已结束或被强制关闭；残留窗口须按现有恢复指引处理；接管不增加手工TIFF回导。 | AutomationRuntime、Session_GuidedStopping/TakingOver、Session_*TakeOver*、Failure_Cancelled。 |
| S06-RECOVERY | 重试保存是重新执行符合资格的保存；核实是查询同一次操作；另存产生独立副本。 | 三种动作不能互换；另存不覆盖旧文件，不解决先前保存的不确定性；核实失败不立即宣称文件缺失。 | FinalSave_RetrySave/CheckAgain/CheckSaved/SaveAnotherCopy/UncertainElsewhere。 |
| S07-CANCEL | 取消本次输入/调整/等待；返回首页；放弃任务分别命名。 | 取消等待可能已创建任务；返回不删除任务；放弃不是仅从最近列表隐藏。确认中的保留文件、历史及不可逆业务状态须准确。 | Home_GuidedImportCancelled、WorkflowSelection_GuidedCancelled、Home_Abandon*、Session_*Cancel。 |
| S08-PRINT | 两条TIFF路线显式选择0/1/2像素内缩及对应分类理由；生成后仍须校验和审核。 | 0不等于无白墨；不沿用源PSD/PDF专色；素材路线无尺寸/白墨/TIFF/多印刷尺寸；300 PPI/CMYK/W1、宽高/长边、最大与实际尺寸不能混用。确认尺寸不自动授权放大。 | 11154“印刷尺寸”术语；Session_GuidedPrint*、Session_*WhiteInk*、Session_Preflight*。 |
| S09-COLLEAGUE | 准备本地副本、人工交接、显式导入修正文件分开。 | 准备成功不是已发送；不新增自动发送、监控保存、自动批准或手工TIFF回导。 | 11139修正/交接合同、Session_Correction*、Session_GuidanceHelpAskColleague。 |
| S10-SOURCE | 原件与InputSnapshot保持；源准备只显示实际产生的受管结果。 | 不把准备PNG说成改写原件；不虚构PSD/PDF预览、页数、白墨来源或外部适配器完成事实。 | SourcePreparation、Session_PdfPrepared/Psd*、Home_GuidedImporting、WorkflowCatalog。 |
| S11-ENVIRONMENT | 当前检查事实、取消中、已取消、历史检查分别表述。 | 历史PASS不授权当前生产；不能用简化提示隐藏环境门禁、自动化锁或生产设置只读要求。 | Environment*、Settings*、ReadinessRecovery*；当前失败原因/合法下一步要可见。 |

## 保存状态逐项复核

PNG 与 TIFF 复用以下语义，但 PNG 准备状态只适用于 PNG；三路线和尺寸身份仍由原有资格控制。PAGE_COPY_MATRIX 的 F-* 行逐项列出对应资源，不通过一个泛“已完成”覆盖全部状态。

| FinalSaveKind | 应表达的事实 | 保留的动作边界 |
| --- | --- | --- |
| None | 当前没有可显示的最终保存目标 | 不凭空提示可保存或已保存。 |
| HistoryUnknown | 正在读取或无法确定保存记录 | 不预设曾保存；不将失败查询当零记录。 |
| Pending | 当前结果尚待审核 / 确认 | 联合确认保存仅在原有入口资格下出现；不是所有审核页面都带保存表单。 |
| PngPreparation | 已审核，但批准PNG尚待准备 | 无需重新处理图片；先完成已有PNG准备动作。TIFF无此项。 |
| Ineligible | 当前结果不可保存，说明真实原因 | 缺失、失效、拒绝、来源变化等原因不能统写未审核。 |
| ApprovedNotSaved | 已通过审核，尚未对外保存 | 指向现有保存动作，不要求重复审核或处理。 |
| Failed | 本次保存未完成；保留具体原因 | 与Uncertain不同，依据现有资格允许重试保存。 |
| Collision | 同名文件存在，未覆盖 | 使用建议名称或其他文件夹；不能暗示覆盖已有文件。 |
| Uncertain | 本次文件可能已写入，完成事实尚待核实 | 首选核实同次保存；另存须明确独立副本。 |
| Saved | 当前保存结果已核实 | 打开文件夹仍使用实时验证与现有资格。 |
| SavedPreviously | 有先前匹配保存记录，当前文件未核实 | 显示历史与当前区别；不得直接声称文件现在存在。 |
| Missing | 曾保存，但检查时找不到文件 | 不推断谁删除了文件；另存仍不覆盖。 |
| Changed | 曾保存，但检查时文件已变化 | 不覆盖，不用旧审核为变化后的外部文件担保。 |
| Unavailable | 当前无法访问先前保存位置 | 与确认缺失分开；按现有动作恢复访问后再检查。 |
| Working | 正在检查、审核记录、准备、保存或收尾 | 具体阶段来自真实投影；不能伪造百分比或承诺可随时取消。 |

## 后续验收方法

文案定稿前逐项询问：“在这个真实状态下，用户会误以为发生了什么？”必须能区分：当前结果已审核但没保存；已保存但任务未完成；只知道历史记录；历史未知；正在停止；已经停止；核实同次保存；另存新副本；取消当前页面；放弃任务。

自动检查负责键/完整占位符/单位和状态清单完整性；针对性产品测试负责现有命令资格、身份、防连击、历史、恢复与不覆盖合同；真实窗口/DPI/键鼠/读屏验证负责标签可见与可操作；实际目标用户负责能否正确解释状态。当前C01仅完成静态盘点与编辑规范，后三种验收均未运行。
