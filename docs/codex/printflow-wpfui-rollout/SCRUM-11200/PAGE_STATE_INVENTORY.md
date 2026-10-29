# 全页面与状态清单

2026-10-22，Pacific/Auckland。SCRUM-11200；Native顺序执行。

复用SCRUM-11174的45个设计页面，追加真实状态、保存分支与条件AI。以下任务是后续迁移归属；源码存在不表示WPF UI迁移完成。逐项条件、显隐意图、业务约束、源码行号/哈希、原型/历史样板引用和NOT RUN证据栏见同目录JSON。CSV用于筛选。

首页和PNG保存页引用历史96 DPI离屏样板；其余原型均为设计参考。产品构建/测试、新截图、真实窗口/DPI/键鼠/读屏/真人走查，本次全部未执行。

| 编号 | 页面/状态 | 路线/输出 | 迁移 / 文案 | 优先级 | 源码或条件 |
| --- | --- | --- | --- | --- | --- |
| P-home | 开始任务 / normal | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11207 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/HomeViewModel.cs:27 |
| P-route | 选择路线 / normal | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11207 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/WorkflowSelectionViewModel.cs:125 |
| P-original | 确认原图 / normal | PrepareAsset,PrepareCustomerDesign / PNG,TIFF | SCRUM-11207 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:59 |
| P-pdf-psd | PDF与PSD源准备 / normal | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11207 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:17 |
| P-enhance-ready | 开始增强 / normal | PrepareAsset,PrepareCustomerDesign / PNG,TIFF | SCRUM-11208 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:17 |
| P-enhance-running | 增强处理中 / loading | PrepareAsset,PrepareCustomerDesign / PNG,TIFF | SCRUM-11208 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:41 |
| P-enhance-review | 增强结果审核 / review | PrepareAsset,PrepareCustomerDesign / PNG,TIFF | SCRUM-11208 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:83 |
| P-background-ready | 选择去背景 / normal | PrepareAsset,PrepareCustomerDesign / PNG,TIFF | SCRUM-11208 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:17 |
| P-background-running | 去背景处理中 / loading | PrepareAsset,PrepareCustomerDesign / PNG,TIFF | SCRUM-11208 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:41 |
| P-review | 审核图片 / review | PrepareAsset,PrepareCustomerDesign / PNG,TIFF | SCRUM-11208 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:83 |
| P-crop-review | 裁切结果审核 / review | PrepareAsset,PrepareCustomerDesign / PNG,TIFF | SCRUM-11208 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:83 |
| P-crop | 调整裁切 / normal | PrepareAsset,PrepareCustomerDesign / PNG,TIFF | SCRUM-11208 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:82 |
| P-dimensions | 印刷尺寸 / normal | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11209 / SCRUM-11205 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:108 |
| P-white-ink | 白墨选择 / normal | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11209 / SCRUM-11205 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:111 |
| P-preflight | 生成前确认 / normal | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11209 / SCRUM-11205 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:114 |
| P-tiff-running | TIFF生成中 / loading | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11209 / SCRUM-11205 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:41 |
| P-tiff-review | 最终TIFF审核 / review | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11209 / SCRUM-11205 | High | src/PrintFlow.App/Views/TiffReviewSurface.cs:42 |
| P-white-preview | TIFF白墨预览 / normal | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11209 / SCRUM-11205 | High | src/PrintFlow.App/Views/TiffReviewSurface.cs:42 |
| P-png-final | 素材PNG收尾 / normal | PrepareAsset / PNG | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:197 |
| P-png-save | 保存已批准PNG / normal | PrepareAsset / PNG | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:197 |
| P-save | 保存结果 / normal | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:197 |
| P-multi-size | 多尺寸结果 / normal | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:197 |
| P-saved | 保存成功 / normal | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:197 |
| P-png-saved | PNG保存成功 / normal | PrepareAsset / PNG | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:197 |
| P-completed | 任务完成 / normal | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:18 |
| P-correction-prepare | 准备同事修正 / normal | PrepareAsset,PrepareCustomerDesign / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:79 |
| P-correction-wait | 等待修正与回导 / recovery | PrepareAsset,PrepareCustomerDesign / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:79 |
| P-handed-off | Photoshop已接管 / recovery | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:79 |
| P-recovery | 中断后恢复 / recovery | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:79 |
| P-failure | 处理失败与重试 / error | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:84 |
| P-stopping | 正在安全停止 / recovery | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:43 |
| P-takeover | Photoshop接管确认 / confirmation | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11211 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:44 |
| P-save-failed | 保存失败 / error | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| P-save-uncertain | 保存结果待核实 / normal | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| P-collision | 同名文件冲突 / error | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| P-return-confirm | 返回重做确认 / confirmation | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:106 |
| P-draft-leave | 离开未提交输入 / confirmation | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:106 |
| P-abandon | 离开与放弃确认 / confirmation | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/HomeViewModel.cs:27 |
| P-enlargement | 放大风险确认 / confirmation | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11209 / SCRUM-11205 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:114 |
| P-input-error | 导入校验失败 / error | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11207 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/HomeViewModel.cs:27 |
| P-readiness | 环境检查 / normal | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/EnvironmentReadinessViewModel.cs:194 |
| P-settings | 设置 / normal | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | Medium | src/PrintFlow.App/Views/SettingsView.xaml:11 |
| P-recent | 最近任务 / normal | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | Medium | src/PrintFlow.App/ViewModels/HomeViewModel.cs:27 |
| P-error-details | 错误详情与诊断 / normal | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | Medium | src/PrintFlow.App/ViewModels/ErrorDetailsViewModel.cs:16 |
| P-ai-planned | AI设计分步接入 / conditional | ConditionalAi /  | SCRUM-11211 / SCRUM-11206 | Medium | 条件接入，尚未实现（11184/11166） |
| S-home-empty | 首页无最近任务 / empty | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11207 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/HomeViewModel.cs:27 |
| S-home-importing | 首页正在导入 / loading | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11207 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/HomeViewModel.cs:27 |
| S-home-disabled | 首页导入/继续被禁用 / disabled | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11207 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/HomeViewModel.cs:27 |
| S-recent-empty | 最近任务空列表 / empty | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | Medium | src/PrintFlow.App/ViewModels/HomeViewModel.cs:27 |
| S-recent-history | 最近任务有保存历史 / history | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | Medium | src/PrintFlow.App/ViewModels/HomeViewModel.cs:27 |
| S-recent-pending | 最近任务待处理/恢复 / recovery | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | Medium | src/PrintFlow.App/ViewModels/HomeViewModel.cs:27 |
| S-route-unselected | 路线尚未选择 / disabled | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11207 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/WorkflowSelectionViewModel.cs:125 |
| S-route-asset | 准备设计素材路线 / normal | PrepareAsset / PNG | SCRUM-11207 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/WorkflowSelectionViewModel.cs:125 |
| S-route-customer | 处理客户设计路线 / normal | PrepareCustomerDesign / TIFF | SCRUM-11207 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/WorkflowSelectionViewModel.cs:125 |
| S-route-tiff | 生成打印TIFF路线 / normal | GeneratePrintTiff / TIFF | SCRUM-11207 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/WorkflowSelectionViewModel.cs:125 |
| S-pdf-pending | PDF单页准备待开始 / normal | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11207 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:17 |
| S-pdf-running | PDF受管副本准备中 / loading | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11207 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:17 |
| S-psd-pending | PSD栅格副本待开始 / normal | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11207 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:17 |
| S-source-prepared-review | PDF/PSD准备后审核 / review | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11207 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:17 |
| S-source-failed | PDF/PSD准备失败 / error | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11207 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:17 |
| S-enhance-skip | 增强可跳过 / normal | PrepareAsset,PrepareCustomerDesign / PNG,TIFF | SCRUM-11208 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:17 |
| S-background-confirm | 自动去背景确认 / confirmation | PrepareAsset,PrepareCustomerDesign / PNG,TIFF | SCRUM-11208 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:17 |
| S-background-skip | 去背景可跳过 / normal | PrepareAsset,PrepareCustomerDesign / PNG,TIFF | SCRUM-11208 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:17 |
| S-review-disabled | 审核资格/身份缺失 / disabled | PrepareAsset,PrepareCustomerDesign / PNG,TIFF | SCRUM-11208 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:83 |
| S-review-history | 只读历史审核 / history | PrepareAsset,PrepareCustomerDesign / PNG,TIFF | SCRUM-11208 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:83 |
| S-review-rejected | 审核拒绝后重试 / error | PrepareAsset,PrepareCustomerDesign / PNG,TIFF | SCRUM-11208 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:83 |
| S-crop-invalid | 裁切输入无效 / disabled | PrepareAsset,PrepareCustomerDesign / PNG,TIFF | SCRUM-11208 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:82 |
| S-crop-cancel | 取消裁切草稿 / normal | PrepareAsset,PrepareCustomerDesign / PNG,TIFF | SCRUM-11208 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:82 |
| S-trim-ready | 自动裁切参数与留边 / normal | PrepareAsset,PrepareCustomerDesign / PNG,TIFF | SCRUM-11208 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:118 |
| S-crop-review-return | 使用裁切后返回审核 / review | PrepareAsset,PrepareCustomerDesign / PNG,TIFF | SCRUM-11208 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:83 |
| S-size-invalid | 印刷尺寸输入无效 / disabled | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11209 / SCRUM-11205 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:108 |
| S-size-custom | 自定义印刷尺寸 / normal | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11209 / SCRUM-11205 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:108 |
| S-white-none | 白墨尚未选择 / disabled | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11209 / SCRUM-11205 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:111 |
| S-white-0 | 白墨内缩0像素及理由 / confirmation | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11209 / SCRUM-11205 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:111 |
| S-white-1 | 白墨内缩1像素及理由 / confirmation | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11209 / SCRUM-11205 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:111 |
| S-white-2 | 白墨内缩2像素及理由 / confirmation | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11209 / SCRUM-11205 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:111 |
| S-preflight-blocked | 生成前条件未齐 / disabled | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11209 / SCRUM-11205 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:114 |
| S-tiff-failed | TIFF生成或校验失败 / error | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11209 / SCRUM-11205 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:41 |
| S-tiff-review-history | 旧尺寸TIFF审核历史 / history | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11209 / SCRUM-11205 | High | src/PrintFlow.App/Views/TiffReviewSurface.cs:42 |
| S-stop-requested | 停止请求已发出 / recovery | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:43 |
| S-stop-finished | 尝试停止后待重试 / recovery | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:84 |
| S-takeover-requested | Photoshop接管请求处理中 / loading | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11211 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:44 |
| S-correction-copy | 同事修正副本准备中 / loading | PrepareAsset,PrepareCustomerDesign / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:79 |
| S-correction-failed | 同事修正准备失败 / error | PrepareAsset,PrepareCustomerDesign / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:79 |
| S-correction-import-review | 修正回导后待审核 / review | PrepareAsset,PrepareCustomerDesign / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:79 |
| S-reentry | 自动化重新进入 / confirmation | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:79 |
| S-completed-abandoned | 任务已放弃 / history | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:18 |
| S-readiness-unknown | 环境尚未检查 / empty | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/EnvironmentReadinessViewModel.cs:194 |
| S-readiness-running | 环境检查进行中 / loading | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/EnvironmentReadinessViewModel.cs:194 |
| S-readiness-blocked | 环境阻塞 / error | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/EnvironmentReadinessViewModel.cs:194 |
| S-readiness-ready | 当前环境已就绪 / normal | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/EnvironmentReadinessViewModel.cs:194 |
| S-settings-production | 生产设置只读 / disabled | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | Medium | src/PrintFlow.App/Views/SettingsView.xaml:11 |
| S-error-empty | 无错误详情 / empty | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | Medium | src/PrintFlow.App/ViewModels/ErrorDetailsViewModel.cs:16 |
| S-error-expanded | 错误技术详情展开 / error | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | Medium | src/PrintFlow.App/ViewModels/ErrorDetailsViewModel.cs:16 |
| S-dialog-input-file | 选择输入文件对话框 / confirmation | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11207 / SCRUM-11204 | High | src/PrintFlow.App/Navigation/OpenFileDialogPicker.cs:11 |
| S-dialog-output-folder | 选择保存目录对话框 / confirmation | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/Navigation/OpenFolderDialogPicker.cs:8 |
| S-dialog-correction-file | 选择修正回导文件对话框 / confirmation | PrepareAsset,PrepareCustomerDesign / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:79 |
| S-dialog-diagnostic-save | 诊断包保存对话框 / confirmation | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | Medium | src/PrintFlow.App/Navigation/SaveDiagnosticPackageDialog.cs:11 |
| S-dialog-automatic-selection | 自动选择授权对话框 / confirmation | PrepareAsset,PrepareCustomerDesign / PNG,TIFF | SCRUM-11208 / SCRUM-11204 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:17 |
| S-dialog-trim-leave | 离开裁切草稿确认 / confirmation | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.Guided.cs:106 |
| S-source-tiff-review | 生成打印TIFF原图设计审核 / review | GeneratePrintTiff / TIFF | SCRUM-11207 / SCRUM-11204 | High | src/PrintFlow.Workflow/Definitions/WorkflowCatalog.cs:110 |
| S-meitu-takeover | 美图接管确认 / confirmation | PrepareAsset,PrepareCustomerDesign / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | High | src/PrintFlow.Workflow/Services/AutomationRuntime.cs:42 |
| S-meitu-takeover-requested | 美图接管请求处理中 / loading | PrepareAsset,PrepareCustomerDesign / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | High | src/PrintFlow.Workflow/Services/AutomationRuntime.cs:42 |
| S-source-preparation-takeover | PSD/PDF源准备接管确认 / confirmation | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | High | src/PrintFlow.Workflow/Services/SessionService.cs:1896 |
| S-source-preparation-takeover-requested | PSD/PDF源准备接管请求处理中 / loading | PrepareAsset,PrepareCustomerDesign,GeneratePrintTiff / PNG,TIFF | SCRUM-11211 / SCRUM-11206 | High | src/PrintFlow.Workflow/Services/AutomationRuntime.cs:42 |
| F-PNG-None | PNG保存 · None / disabled | PrepareAsset / PNG | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| F-PNG-HistoryUnknown | PNG保存 · HistoryUnknown / history | PrepareAsset / PNG | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| F-PNG-Pending | PNG保存 · Pending / disabled | PrepareAsset / PNG | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| F-PNG-PngPreparation | PNG保存 · PngPreparation / disabled | PrepareAsset / PNG | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| F-PNG-Ineligible | PNG保存 · Ineligible / disabled | PrepareAsset / PNG | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| F-PNG-ApprovedNotSaved | PNG保存 · ApprovedNotSaved / normal | PrepareAsset / PNG | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| F-PNG-Failed | PNG保存 · Failed / error | PrepareAsset / PNG | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| F-PNG-Collision | PNG保存 · Collision / error | PrepareAsset / PNG | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| F-PNG-Uncertain | PNG保存 · Uncertain / normal | PrepareAsset / PNG | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| F-PNG-Saved | PNG保存 · Saved / normal | PrepareAsset / PNG | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| F-PNG-SavedPreviously | PNG保存 · SavedPreviously / history | PrepareAsset / PNG | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| F-PNG-Missing | PNG保存 · Missing / error | PrepareAsset / PNG | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| F-PNG-Changed | PNG保存 · Changed / error | PrepareAsset / PNG | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| F-PNG-Unavailable | PNG保存 · Unavailable / error | PrepareAsset / PNG | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| F-PNG-Working | PNG保存 · Working / loading | PrepareAsset / PNG | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| F-TIFF-None | TIFF保存 · None / disabled | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| F-TIFF-HistoryUnknown | TIFF保存 · HistoryUnknown / history | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| F-TIFF-Pending | TIFF保存 · Pending / disabled | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| F-TIFF-Ineligible | TIFF保存 · Ineligible / disabled | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| F-TIFF-ApprovedNotSaved | TIFF保存 · ApprovedNotSaved / normal | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| F-TIFF-Failed | TIFF保存 · Failed / error | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| F-TIFF-Collision | TIFF保存 · Collision / error | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| F-TIFF-Uncertain | TIFF保存 · Uncertain / normal | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| F-TIFF-Saved | TIFF保存 · Saved / normal | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| F-TIFF-SavedPreviously | TIFF保存 · SavedPreviously / history | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| F-TIFF-Missing | TIFF保存 · Missing / error | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| F-TIFF-Changed | TIFF保存 · Changed / error | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| F-TIFF-Unavailable | TIFF保存 · Unavailable / error | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| F-TIFF-Working | TIFF保存 · Working / loading | PrepareCustomerDesign,GeneratePrintTiff / TIFF | SCRUM-11210 / SCRUM-11206 | High | src/PrintFlow.App/ViewModels/SessionViewModel.FinalSave.cs:119 |
| AI-requirements | 条件AI · 需求与模板 / conditional | ConditionalAi /  | SCRUM-11211 / SCRUM-11206 | Medium | 条件接入，尚未实现（11184/11166） |
| AI-attachments | 条件AI · 附件及实际上传清单 / conditional | ConditionalAi /  | SCRUM-11211 / SCRUM-11206 | Medium | 条件接入，尚未实现（11184/11166） |
| AI-prompt | 条件AI · Prompt编辑与确认 / conditional | ConditionalAi /  | SCRUM-11211 / SCRUM-11206 | Medium | 条件接入，尚未实现（11184/11166） |
| AI-generation | 条件AI · 上传/生成进度 / conditional | ConditionalAi /  | SCRUM-11211 / SCRUM-11206 | Medium | 条件接入，尚未实现（11184/11166） |
| AI-candidate | 条件AI · 候选校验与确认 / conditional | ConditionalAi /  | SCRUM-11211 / SCRUM-11206 | Medium | 条件接入，尚未实现（11184/11166） |
| AI-import | 条件AI · 候选导入与手动交接 / conditional | ConditionalAi /  | SCRUM-11211 / SCRUM-11206 | Medium | 条件接入，尚未实现（11184/11166） |
